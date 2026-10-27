# Sietom KD — wersja 1.0.27 (27.10.2026)

Instrukcja administratora: [INSTRUKCJA-INSTALACJI.txt](INSTRUKCJA-INSTALACJI.txt).
Jedyna paczka wdrożeniowa: `publish/KD-INSTALACJA-10.0.207.5.zip`.

KD samodzielnie synchronizuje karty API PRD do istniejącej SietomPartsBox.
Potwierdzone połączenie: `tcp:10.0.207.5,51433`, Windows Authentication,
`Encrypt=False`, `TrustServerCertificate=False`. Usługa: `SietomAccessControl`,
konto `TSA\rwyrwicki`, folder `C:\Services\SietomAccessControl`.
Login SQL aplikacji na laptopie nie jest kontem usługi KD.

27.10.2026 bezpośrednio w SQL potwierdzono dwa automatyczne importy:
07:53:34 czasu polskiego — 3897 kart; 07:55:36 — 3898 kart.
Oba Succeeded, bez błędów i konfliktów. Wcześniej brak wpisów od 20.10
wynikał z połączenia KD na stary port; zmiana na 51433 przywróciła importy.
Nie trzeba ponownie instalować działającej usługi tylko z powodu wydania paczki.

## Zawartość

- `src` — kod workera; źródłowa konfiguracja PRD z portem 51433, domyślnie wyłączona do instalacji.
- `tests` — testy API, synchronizacji i integracji SQL.
- `deploy` — instalator i diagnostyka API.
- `publish` — jeden kompletny ZIP dla Windows x64, zawierający runtime .NET.
- `secrets` — jeśli istnieje, prywatne pliki lokalne; pomijane w Git i paczce.

Kanoniczny SQL: `../Sietom RFID PartsBOX/sql/04_Integracja_KD.sql`.
Worker osadza go jako zasób; instalator wykonuje go bez ręcznych migracji.
Instalator sprawdza ustawienia paczki przed zatrzymaniem usługi i pobiera
połączenie z jej appsettings.json, zamiast przywracać stary port 1433.
Zachowuje chroniony sekret i dodatkowe ustawienia lokalne.

## Budowanie i weryfikacja

Wymagany SDK .NET 10. Z katalogu KD:

```powershell
$env:SIETOM_TEST_SQL = '.\SQLEXPRESS'
dotnet test .\tests\Sietom.AccessControl.Tests\Sietom.AccessControl.Tests.csproj -c Release
dotnet publish .\src\Sietom.AccessControl.Worker\Sietom.AccessControl.Worker.csproj -c Release -r win-x64 --self-contained true -o .\publish\KD-INSTALACJA-10.0.207.5
```

Testy integracyjne tworzą i usuwają wyłącznie osobne lokalne bazy
`PartsBoxKD_Test_<GUID>`. Nigdy nie wskazuj serwera klienta w SIETOM_TEST_SQL.
Bez tej zmiennej testy SQL są pomijane.

Do świeżego folderu publikacji dołącz dwa skrypty z deploy, instrukcję,
WERSJA.txt i kanoniczny SQL jako sql/04_Integracja_KD.sql. Domyślna konfiguracja
w źródłach ma już PRD, port 51433 i Sync.Enabled=false; instalator włącza import.
Nie kopiuj do paczki katalogu secrets, OAuth2.txt, plików .secret, .bak ani Local.

Przed spakowaniem wykonaj w Windows PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\publish\KD-INSTALACJA-10.0.207.5\Instaluj-KD.ps1 -ValidatePackage
```

Spakuj zawartość, bez nadrzędnego folderu, do jedynego ZIP. Usuń roboczy katalog
po zweryfikowaniu archiwum. Plik WERSJA.txt w paczce identyfikuje wydanie.

Wcześniejsza weryfikacja 19.10.2026: 57 zaliczonych testów, w tym cztery SQL.
Bieżąca kontrola wydania i jej wyniki są zapisane w WERSJA.txt.
### Odtworzenie publikacji offline na komputerze autora

Bieżącą paczkę zbudowano z lokalnego runtime 10.0.13 i hosta 10.0.14 z SDK.
Przy niedostępnym NuGet standardowa publikacja próbowała pobrać runtime 10.0.14.
W katalogu KD użyto poniższego polecenia (ścieżka hosta musi istnieć):

```powershell
$cache = Join-Path $env:USERPROFILE '.nuget\packages'
$hostSource = 'C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\10.0.14\runtimes\win-x64\native\apphost.exe'
dotnet publish .\src\Sietom.AccessControl.Worker\Sietom.AccessControl.Worker.csproj -c Release -r win-x64 --self-contained true -o .\publish\KD-INSTALACJA-10.0.207.5 -p:UseAppHost=true -p:AppHostSourcePath=$hostSource -p:EnableAppHostPackDownload=false -p:NuGetAudit=false -p:RestoreSources=$cache -p:RuntimeFrameworkVersion=10.0.13 -p:TargetLatestRuntimePatch=false
```

Parametrów offline nie zapisano w projekcie. Na serwerze klienta nie jest wymagany
SDK ani oddzielna instalacja .NET — cały runtime znajduje się w ZIP.
Bieżąca walidacja instalatora w Windows PowerShell potwierdziła odrzucenie:
starego portu 1433, SQL Authentication oraz domyślnie włączonej synchronizacji.