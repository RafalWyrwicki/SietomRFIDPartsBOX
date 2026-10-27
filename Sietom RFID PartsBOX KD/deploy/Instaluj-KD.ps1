# One installer for the existing SietomPartsBox database and Windows service.
[CmdletBinding()]
param(
    [string]$Destination = 'C:\Services\SietomAccessControl',
    [string]$SecretTextFile = '',
    [switch]$ValidatePackage
)
$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
if (!$source) { $source = Split-Path -Parent $MyInvocation.MyCommand.Path }
$exeName = 'Sietom.AccessControl.Worker.exe'
foreach ($required in @($exeName,'Sietom.AccessControl.Worker.dll','appsettings.json','sql\04_Integracja_KD.sql')) {
    if (!(Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) {
        throw ('Niekompletna paczka KD: ' + $required)
    }
}
$defaultConfig = Get-Content -Raw -LiteralPath (Join-Path $source 'appsettings.json') | ConvertFrom-Json
Add-Type -AssemblyName System.Data
$packageSql = [Data.SqlClient.SqlConnectionStringBuilder]::new($defaultConfig.Sql.ConnectionString)
if ($packageSql.DataSource -ne 'tcp:10.0.207.5,51433' -or
    $packageSql.InitialCatalog -ne 'SietomPartsBox' -or !$packageSql.IntegratedSecurity -or
    $packageSql.Encrypt -or $packageSql.TrustServerCertificate -or $packageSql.ConnectTimeout -lt 1 -or
    $defaultConfig.Sync.Environment -ne 'PRD' -or $defaultConfig.Sync.Enabled -ne $false) {
    throw 'Niepoprawna paczka: wymagane PRD, SQL tcp:10.0.207.5,51433 / SietomPartsBox, Windows Authentication i Sync.Enabled=false.'
}
# One source of the connection string: validated package configuration, including port 51433.
$clientConnectionString = $packageSql.ConnectionString
if ($ValidatePackage) {
    Write-Output 'Paczka KD kompletna: jeden instalator, jeden SQL, program i runtime.'
    return
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if ($env:COMPUTERNAME -ne 'TSASRVW25RFID' -or $identity.Name -ne 'tsa\rwyrwicki') {
    throw 'Uruchom instalator na TSASRVW25RFID przez RDP jako tsa\rwyrwicki.'
}
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Otworz Windows PowerShell jako administrator i ponow instalacje.'
}
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
$exe = Join-Path $Destination $exeName
$serviceName = 'SietomAccessControl'
$existing = Get-CimInstance Win32_Service -Filter "Name='SietomAccessControl'"
if ($existing -and $existing.StartName -ne $identity.Name) {
    throw 'Istniejaca usluga ma inne konto. Uzgodnij konto i przygotuj sekret pod jego tozsamoscia przed aktualizacja.'
}
$credential = $null
if (!$existing) {
    $credential = Get-Credential -UserName $identity.Name -Message 'Haslo konta Windows tsa\rwyrwicki dla uslugi KD'
    if ($null -eq $credential -or $credential.UserName -ne $identity.Name) {
        throw 'Usluga musi uzywac tego samego konta co chroniony sekret.'
    }
}
if ($existing) {
    Stop-Service -Name $serviceName -ErrorAction Stop
    (Get-Service $serviceName).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
}
# Stop only workers from the exact destination executable, never other applications.
foreach ($worker in @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $exe })) {
    Stop-Process -Id $worker.ProcessId -Force
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$backup = Join-Path $Destination ('backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($name in @('appsettings.json','appsettings.Local.json',$exeName,'Sietom.AccessControl.Worker.dll','Sietom.AccessControl.Worker.pdb')) {
    $old = Join-Path $Destination $name
    if (Test-Path -LiteralPath $old -PathType Leaf) { Copy-Item -LiteralPath $old -Destination $backup }
}
if ($source.TrimEnd('\') -ne $Destination) {
    foreach ($item in Get-ChildItem -LiteralPath $source -Force) {
        if ($item.Name -in @('appsettings.Local.json','secrets','backups')) { continue }
        if ($item.Name -eq 'appsettings.json' -and (Test-Path -LiteralPath (Join-Path $Destination 'appsettings.json'))) { continue }
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}
$localPath = Join-Path $Destination 'appsettings.Local.json'
$local = if (Test-Path -LiteralPath $localPath) { Get-Content -Raw -LiteralPath $localPath | ConvertFrom-Json } else { $null }
$secretPath = if ($local -and $local.OAuth) { $local.OAuth.ClientSecretFile } else { '' }
if (!$secretPath -or !(Test-Path -LiteralPath $secretPath -PathType Leaf)) {
    if (!$SecretTextFile) {
        $candidate = Join-Path $Destination 'secrets\OAuth2.txt'
        $SecretTextFile = if (Test-Path -LiteralPath $candidate) { $candidate } else { Read-Host 'Pelna sciezka do OAuth2.txt na serwerze' }
    }
    if (!(Test-Path -LiteralPath $SecretTextFile -PathType Leaf)) { throw 'Nie znaleziono pliku sekretu OAuth.' }
    $secretDirectory = 'C:\ProgramData\SietomAccessControl\secrets'
    New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
    $allowed = @($identity.User.Value,'S-1-5-18','S-1-5-32-544')
    $grants = @($allowed | ForEach-Object { '*' + $_ + ':(OI)(CI)F' })
    & icacls.exe $secretDirectory /inheritance:r /grant:r $grants | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Nie mozna ustawic uprawnien katalogu sekretu.' }
    foreach ($rule in (Get-Acl -LiteralPath $secretDirectory).Access) {
        $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if ($sid -notin $allowed) {
            & icacls.exe $secretDirectory /remove ('*' + $sid) | Out-Null
        } elseif ($rule.AccessControlType -eq 'Deny') {
            & icacls.exe $secretDirectory /remove:d ('*' + $sid) | Out-Null
        } else { continue }
        if ($LASTEXITCODE -ne 0) { throw 'Nie mozna ograniczyc dostepu do katalogu sekretu.' }
    }
    Add-Type -AssemblyName System.Security
    $plain = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $SecretTextFile).Path).Trim()
    if ([string]::IsNullOrWhiteSpace($plain) -or $plain -match '\s') { throw 'OAuth2.txt musi zawierac tylko sekret w jednym wierszu.' }
    $bytes = [Text.Encoding]::UTF8.GetBytes($plain)
    $plain = $null
    $secretPath = Join-Path $secretDirectory ('oauth-prd-' + [Guid]::NewGuid().ToString('N') + '.secret')
    try {
        $cipher = [Security.Cryptography.ProtectedData]::Protect($bytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        [IO.File]::WriteAllBytes($secretPath,$cipher)
    } finally { [Array]::Clear($bytes,0,$bytes.Length) }
}
# Preserve custom settings; explicitly set the agreed server, environment and protected secret.
$config = if ($local) { $local } else { [pscustomobject]@{} }
foreach ($section in @('Sync','OAuth','Sql')) {
    if (!$config.$section) { $config | Add-Member -MemberType NoteProperty -Name $section -Value ([pscustomobject]@{}) -Force }
}
$config.Sync | Add-Member -MemberType NoteProperty -Name Enabled -Value $false -Force
$config.Sync | Add-Member -MemberType NoteProperty -Name Environment -Value 'PRD' -Force
$config.OAuth | Add-Member -MemberType NoteProperty -Name ClientId -Value 'rfid-maintenance-sietom-client' -Force
$config.OAuth | Add-Member -MemberType NoteProperty -Name ClientAuthentication -Value 'Post' -Force
$config.OAuth | Add-Member -MemberType NoteProperty -Name ClientSecretFile -Value $secretPath -Force
$config.Sql | Add-Member -MemberType NoteProperty -Name ConnectionString -Value $clientConnectionString -Force
[IO.File]::WriteAllText($localPath,($config | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
& $exe --check-secret
if ($LASTEXITCODE -ne 0) { throw 'Biezace konto nie moze odczytac sekretu. Usluga nie zostala uruchomiona.' }
& $exe --install-sql
if ($LASTEXITCODE -ne 0) { throw 'Aktualizacja SQL nie powiodla sie. Usluga nie zostala uruchomiona.' }
$config.Sync.Enabled = $true
[IO.File]::WriteAllText($localPath,($config | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
& $exe --once
$firstImportExitCode = $LASTEXITCODE
if ($firstImportExitCode -eq 2) {
    Write-Warning 'API jest niedostepne lub zwraca blad przejsciowy. Usluga zostanie zainstalowana i bedzie ponawiac synchronizacje. Ostatnia poprawna lista pozostaje w bazie.'
} elseif ($firstImportExitCode -ne 0) {
    $config.Sync.Enabled=$false
    [IO.File]::WriteAllText($localPath,($config | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    throw 'Pierwszy import nie powiodl sie. Popraw przyczyne i ponow ten sam instalator.'
}
if (!$existing) {
    New-Service -Name $serviceName -DisplayName 'Sietom synchronizacja kart dostepu' -BinaryPathName ('"' + $exe + '"') -Credential $credential -StartupType Automatic -Description 'API PRD do SietomPartsBox; pomijanie spornych kart'
} else {
    $result = Invoke-CimMethod -InputObject $existing -MethodName Change -Arguments @{PathName=('"'+$exe+'"');StartMode='Automatic'}
    if ($result.ReturnValue -ne 0) { throw ('Nie mozna zaktualizowac uslugi. Kod: ' + $result.ReturnValue) }
}
& sc.exe failure $serviceName 'reset=' '86400' 'actions=' 'restart/60000/restart/60000/restart/60000' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Nie mozna ustawic odzyskiwania uslugi.' }
Start-Service -Name $serviceName
(Get-Service $serviceName).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
# Remove only explicitly superseded helper scripts and incremental SQL files.
foreach ($obsolete in @('Przygotuj-KD-PRD.ps1','deploy\Install-Service.ps1','deploy\Protect-ClientSecret.ps1',
    'sql\001_PartsBox_KD.sql','sql\002_Ostatnia_lista_i_ostrzezenie.sql','sql\003_Liczby_ze_znakiem.sql',
    'sql\004_Karty_sporne.sql','06_KD_Liczby_ze_znakiem.sql','07_KD_Pomijanie_spornych_kart.sql')) {
    $old = [IO.Path]::GetFullPath((Join-Path $Destination $obsolete))
    if (!$old.StartsWith($Destination+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Niepoprawna sciezka sprzatania.' }
    if (Test-Path -LiteralPath $old -PathType Leaf) { Remove-Item -LiteralPath $old -Force }
}
Write-Output 'Gotowe: usluga SietomAccessControl dziala i startuje automatycznie z Windows.'
if ($firstImportExitCode -eq 2) {
    Write-Warning 'Usluga dziala, ale nowy import nie zostal jeszcze potwierdzony. Sprawdz kolejne wpisy Succeeded w SQL; utrzymujacy sie HTTP 500 wymaga wyjasnienia z administratorem API klienta.'
}
Write-Output ('Kopia poprzedniej konfiguracji i programu: ' + $backup)
Write-Output 'Sprawdz kolejne sukcesy w SietomPartsBox.dbo.AccessCardSyncRuns oraz bledy w dzienniku Application.'
