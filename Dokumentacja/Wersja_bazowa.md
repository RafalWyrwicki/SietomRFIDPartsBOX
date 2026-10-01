# Dokumentacja wersji bazowej

## Zakres

PartsBOX obsługuje lokalne logowanie kartą, Zebra FX7500, odczyt EPC, skaner kodów 2D, pobrania, zwroty, kontrole stanów oraz zapis SQL i raportów Excel/CSV.

## Integracje

- SQL Server Express: źródło lokalnych materiałów, zleceń, pracowników i audytu.
- Zebra FX7500: odczyt wyłącznie EPC; TID nie jest używany.
- RDR-805x1BxU: czytnik klawiaturowy, format kart `site_code,card_number` i Enter.
- SAP: integracja zaplanowana; do czasu wdrożenia używane są importy XLSX/CSV.

## Dane kart

Aktualny skrypt SQL tworzy `240,38032` dla Jabłońskiego Artura oraz `240,38386` dla Wywrot Julii. Konfiguracja czytnika używa `AUTO`, ponieważ urządzenie klawiaturowe nie udostępnia stabilnego identyfikatora USB.

## Zasady operacji

Każdy fizyczny tag ma jeden unikalny EPC. Start i Stop są sterowane przez operatora. Zapis pobrania, zwrotu i kontroli jest transakcyjny i audytowany w SQL. Po udanej kontroli lista odczytów jest czyszczona bez wylogowania.

## SQL

Pełną kolejność tworzenia bazy i kart opisuje `README.md` oraz skrypty w katalogu `sql`. Skrypt odtworzenia pustej bazy jest destrukcyjny i służy wyłącznie do środowisk testowych.

## Stan wdrożenia

Wersja bazowa nie księguje jeszcze operacji w SAP. Przed produkcją należy wdrożyć API SAP, autoryzację, sekrety, role użytkowników, migracje produkcyjne i testy akceptacyjne.
