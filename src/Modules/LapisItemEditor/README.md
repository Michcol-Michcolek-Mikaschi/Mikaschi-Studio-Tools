# Lapis Item Editor

Moduł do edycji `items.otb` i `appearances.dat` klienta Tibia. Oparta na Avalonia UI.

## Funkcje

- Edycja items.otb (format OTB)
- Edycja appearances.dat
- Obsługa formatu LZMA
- Zarządzanie właściwościami itemów

## Status

🚧 In progress — moduł ma już podstawowy workflow `items.otb`: nowy dokument, odczyt, zapis, tabela itemów, wyszukiwanie, edycja SID/CID/nazwy/typu/speed, flagi, duplikowanie i porównanie OTB.

## Parsery (z Narzedzia.Core)

- `OtbParser` — odczyt/zapis items.otb
- `AppearancesParser` — odczyt/zapis appearances.dat
