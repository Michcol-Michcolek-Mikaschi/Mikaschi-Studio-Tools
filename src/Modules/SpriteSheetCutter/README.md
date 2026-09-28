# Sprite Sheet Cutter

Natywna migracja `sprite_sheet_cutter.py` do Avalonia/.NET z wykorzystaniem ImageSharp.
Moduł nie uruchamia Pythona, konsoli ani procesów zewnętrznych.

## Przetwarzanie

- pojedynczy arkusz wejściowy wybierany z okna systemowego albo przez przeciągnięcie;
- zapamiętywanie ostatniego folderu wejściowego;
- oryginalna lista 294 presetów rozmiaru;
- domyślny preset `64x64 (2×2)`;
- podgląd z czerwonymi liniami cięcia;
- podgląd ograniczony do 800×600 px, bez powiększania, skalowany filtrem Lanczos;
- cięcie w kolejności wierszowej;
- zachowanie niepełnych komórek z przezroczystym dopełnieniem do pełnego rozmiaru;
- zapis do `sliced_sprites` obok arkusza jako `sprite_000.png`, `sprite_001.png` itd.;
- postęp i anulowanie operacji.

Zachowanie, presety i nazewnictwo wyników odpowiadają oryginalnemu narzędziu.
