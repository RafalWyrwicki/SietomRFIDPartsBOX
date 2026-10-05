# Sietom RFID PartsBOX

## Przeznaczenie

WPF dla Windows do obsługi stanowiska PartsBOX: logowania kartą, odczytu EPC z Zebra FX7500, ręcznego skanowania kodów, pobierania i zwrotu części oraz kontroli stanów magazynowych.

Wersja bazowa działa lokalnie z SQL Server Express. SAP nie jest jeszcze podłączony; miejsce integracji stanowią przyszłe adaptery API.

## Uruchomienie

1. Otwórz `Sietom RFID PartsBOX.sln` w Visual Studio z .NET 10 Desktop.
2. Ustaw połączenie i urządzenia w `appsettings.json`.
3. Zbuduj projekt i uruchom go z Visual Studio.

`CardReaderDeviceId: AUTO` jest przeznaczone dla czytnika RDR-805x1BxU emulującego klawiaturę. Czytnik wysyła format `site_code,card_number` zakończony Enterem, np. `240,38032`.

## Karty pracowników

Skrypt `sql/02_Uzytkownicy_i_karty.sql` przygotowuje karty `240,38032` (Jabłoński Artur, `TARC19571`) oraz `240,38386` (Wywrot Julia, `TARC19687`). Skrypt jest idempotentny i aktualizuje istniejące przypisania.

## SQL

Uruchamiaj skrypty w kolejności:

1. `sql/01_Odtworz_pusta_baze.sql` — usuwa i tworzy bazę `SietomPartsBox`; tylko dla środowiska testowego.
2. `sql/02_Uzytkownicy_i_karty.sql` — dodaje lub aktualizuje użytkowników i karty.
3. Import zleceń i magazynu wykonuj z aplikacji. `sql/03_Import_plikow_i_ilosci.sql` jest schematem obiektów importu osadzonym w aplikacji, a nie skryptem do codziennego uruchamiania.

## Operacje

- **Pobranie części**: wybierz zlecenie, użyj Start/Stop, a następnie Zatwierdź. EPC musi istnieć w `Parts` i mieć stan `Available`.
- **Zwrot**: wybierz pobranie, odczytaj EPC, zatrzymaj odczyt i zatwierdź zwrot.
- **Kontrola stanów**: odczytaj unikalne EPC i zapisz kontrolę. SQL zapisuje audyt oraz raport Excel/CSV; po sukcesie lista jest czyszczona.
- Czytnik kodów 2D może wprowadzać EPC zakończony Enterem lub Tabem.

Odczyt RFID korzysta wyłącznie z EPC. TID nie jest wymagany ani pobierany.

## FX7500

Program korzysta z Zebra RFID FXSeries Host .NET SDK. `ReaderAddressMode` ustaw na `Host`, aby łączyć się z FX7500 po nazwie `FX75005D0A26`. Czytnik kart pozostaje urządzeniem USB i jest ustawiany osobno przez `CardReaderDeviceId`.

## Budowanie i testy

```text
dotnet restore "Sietom RFID PartsBOX.sln"
dotnet build "Sietom RFID PartsBOX.sln"
dotnet publish PartsBox.csproj -c Release -o .\publish
```

Testy automatyczne mogą wymagać działającego SQL oraz FX7500. Wyniki testów i raporty robocze nie powinny być przechowywane w katalogu źródłowym.

## Wersja bazowa

Katalog źródłowy nie zawiera wyników kompilacji, raportów testowych ani kopii konfiguracji. Wersja produkcyjna wymaga jeszcze adaptera SAP, autoryzacji API, zarządzania sekretami, uprawnień magazynowych i testów integracyjnych z klientem.

