# Read-only diagnostic. Never imports employees or changes the service/configuration.
[CmdletBinding()]
param([string]$ApplicationDirectory = 'C:\Services\SietomAccessControl')
$ErrorActionPreference = 'Stop'
$stage = 'Konfiguracja'
$client = $null
$handler = $null
$secretBytes = $null
$tokenReply = $null
$form = $null
try {
    Add-Type -AssemblyName System.Net.Http
    Add-Type -AssemblyName System.Security
    $base = Get-Content -LiteralPath (Join-Path $ApplicationDirectory 'appsettings.json') -Raw | ConvertFrom-Json
    $local = $null
    $localPath = Join-Path $ApplicationDirectory 'appsettings.Local.json'
    if (Test-Path -LiteralPath $localPath) { $local = Get-Content -LiteralPath $localPath -Raw | ConvertFrom-Json }
    function Read-Setting([string]$section, [string]$name) {
        $value = $null
        if ($base.$section) { $value = $base.$section.$name }
        $envKey = $section + '__' + $name
        $override = [Environment]::GetEnvironmentVariable($envKey)
        if ($null -ne $override) { $value = $override }
        if ($local -and $local.$section -and $local.$section.PSObject.Properties[$name]) { $value = $local.$section.$name }
        $override = [Environment]::GetEnvironmentVariable('SIETOM_' + $envKey)
        if ($null -ne $override) { $value = $override }
        return $value
    }
    $environment = Read-Setting 'Sync' 'Environment'
    $tokenUrl = Read-Setting 'OAuth' 'TokenUrl'
    $scope = Read-Setting 'OAuth' 'Scope'
    $clientId = Read-Setting 'OAuth' 'ClientId'
    $secretFile = Read-Setting 'OAuth' 'ClientSecretFile'
    $authentication = Read-Setting 'OAuth' 'ClientAuthentication'
    # Do not send credentials to an arbitrary URL from a malformed configuration.
    if ($tokenUrl -ne 'https://sso.tarczynski.pl/realms/software/protocol/openid-connect/token' -or
        $environment -notin @('PRD','STG') -or $authentication -notin @('Post','Basic') -or
        $scope -ne 'access-control.active-card-users.list' -or [string]::IsNullOrWhiteSpace($clientId)) {
        throw [InvalidOperationException]::new('Invalid configuration')
    }
    $apiHost = if ($environment -eq 'PRD') { 'integration-api.tarczynski.pl' } else { 'integration-api-stg.tarczynski.pl' }
    $apiUrl = 'https://' + $apiHost + '/external/api/v1/access-control/active-card-users/list'
    Write-Output ('Czas UTC: ' + [DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss'))
    Write-Output ('Komputer: ' + $env:COMPUTERNAME)
    Write-Output ('Konto: ' + [Security.Principal.WindowsIdentity]::GetCurrent().Name)
    $service = Get-Service -Name SietomAccessControl -ErrorAction SilentlyContinue
    Write-Output ('Usluga: ' + $(if ($service) { $service.Status } else { 'Brak' }))
    Write-Output ('Srodowisko: ' + $environment + '; Sync.Enabled: ' + (Read-Setting 'Sync' 'Enabled'))
    Write-Output ('API: ' + $apiUrl)
    $stage = 'Odczyt chronionego sekretu (konto Windows)'
    $secretBytes = [Security.Cryptography.ProtectedData]::Unprotect(
        [IO.File]::ReadAllBytes($secretFile), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    $secret = [Text.Encoding]::UTF8.GetString($secretBytes)
    [Array]::Clear($secretBytes, 0, $secretBytes.Length)
    $secretBytes = $null
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(30)
    $client.MaxResponseContentBufferSize = 65536
    $values = [Collections.Generic.Dictionary[string,string]]::new()
    $values.Add('grant_type','client_credentials')
    $values.Add('scope',$scope)
    if ($authentication -eq 'Basic') {
        $credentials = [Net.WebUtility]::UrlEncode($clientId) + ':' + [Net.WebUtility]::UrlEncode($secret)
        $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new(
            'Basic', [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($credentials)))
        $credentials = $null
    } else {
        $values.Add('client_id',$clientId)
        $values.Add('client_secret',$secret)
    }
    $form = [Net.Http.FormUrlEncodedContent]::new($values)
    $values.Clear()
    $secret = $null
    $stage = 'SSO - pobranie tokenu'
    $tokenReply = $client.PostAsync($tokenUrl, $form).GetAwaiter().GetResult()
    Write-Output ('SSO HTTP: ' + [int]$tokenReply.StatusCode)
    if (!$tokenReply.IsSuccessStatusCode) { exit 1 }
    $tokenData = $tokenReply.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $bearer = $tokenData.access_token
    if ([string]::IsNullOrWhiteSpace($bearer) -or $tokenData.token_type -ne 'Bearer') {
        throw [InvalidOperationException]::new('Invalid token response')
    }
    # Inspect only two booleans; never print the token or employee information.
    # JWT decoding here is informational, not signature validation.
    try {
        $encoded = $bearer.Split('.')[1].Replace('-','+').Replace('_','/')
        $encoded = $encoded.PadRight($encoded.Length + ((4 - $encoded.Length % 4) % 4), '=')
        $claims = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($encoded)) | ConvertFrom-Json
        Write-Output ('Token deklaruje audience integration-api: ' + (@($claims.aud) -contains 'integration-api'))
        Write-Output ('Token deklaruje wymagany scope: ' + (@(([string]$claims.scope) -split ' ') -contains $scope))
        $claims = $null
        $encoded = $null
    } catch { Write-Output 'Token: brak mozliwosci odczytu metadanych JWT.' }
    $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $bearer)
    $tokenData = $null
    $bearer = $null
    foreach ($withCorrelation in @($true, $false)) {
        $stage = 'API - pobranie naglowkow listy kart'
        $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Get, $apiUrl)
        $response = $null
        try {
            $request.Headers.Accept.ParseAdd('application/json')
            $label = 'GET bez dodatkowego naglowka'
            if ($withCorrelation) {
                $id = [Guid]::NewGuid().ToString()
                $request.Headers.Add('X-Correlation-Id', $id)
                $label = 'GET jak w KD; X-Correlation-Id=' + $id
            }
            $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            Write-Output ($label + '; HTTP=' + [int]$response.StatusCode)
            foreach ($header in @('X-Correlation-Id','X-Total-Count')) {
                if ($response.Headers.Contains($header)) {
                    $value = ($response.Headers.GetValues($header) -join ',')
                    if ($value -match '^[0-9a-fA-F-]{36}$' -or $value -match '^\d{1,12}$') {
                        Write-Output ($header + ': ' + $value)
                    }
                }
            }
            # Deliberately do not read or print the list of employees or error body.
        } finally {
            if ($response) { $response.Dispose() }
            $request.Dispose()
        }
    }
    Write-Output 'Koniec diagnostyki. Nie zmieniono SQL, konfiguracji ani uslugi. HTTP 200 potwierdza odpowiedz, nie pelny import.'
} catch {
    Write-Output ('Blad etapu: ' + $stage + '; typ: ' + $_.Exception.GetType().Name)
    if ($_.Exception.InnerException) { Write-Output ('Przyczyna techniczna: ' + $_.Exception.InnerException.GetType().Name) }
    exit 1
} finally {
    if ($secretBytes) { [Array]::Clear($secretBytes, 0, $secretBytes.Length) }
    $secret = $null
    $bearer = $null
    $tokenData = $null
    if ($tokenReply) { $tokenReply.Dispose() }
    if ($form) { $form.Dispose() }
    if ($client) { $client.Dispose() }
    if ($handler) { $handler.Dispose() }
}
