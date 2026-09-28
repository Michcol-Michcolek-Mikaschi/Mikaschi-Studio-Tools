# Sprite Resizer

Natywna migracja `sprite_resizer.py` do Avalonia/.NET z wykorzystaniem ImageSharp.
Moduł nie uruchamia Pythona, konsoli ani procesów zewnętrznych.

## Przetwarzanie

- wybór wielu plików PNG z okna systemowego albo przez przeciągnięcie;
- dodawanie przeciągniętych plików bez duplikatów i zapamiętywanie ostatniego folderu;
- zamiana dokładnego koloru `#FF00FF` na pełną przezroczystość;
- wykrycie granic widocznego sprite'a i usunięcie pustych marginesów;
- proporcjonalne skalowanie wyłącznie w dół filtrem nearest-neighbor;
- wyśrodkowanie bez powiększania na przezroczystym płótnie docelowym;
- zapis obok oryginału jako `<nazwa>_<szerokość>x<wysokość>_clean.png`;
- postęp, raport błędów i anulowanie operacji.

Zachowanie algorytmu i nazewnictwo wyników odpowiadają oryginalnemu narzędziu.
