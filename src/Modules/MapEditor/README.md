# Map Editor

Edytor map OTBM napisany w C# i Avalonia, z układem pracy inspirowanym Remere's
Map Editor.

## Główne funkcje

- otwieranie, zapisywanie i import map OTBM;
- wersjonowane palety Terrain, Doodad, Items, RAW, Creatures, Houses i Waypoints;
- pędzle, automatyczne bordery, gumka, historia zmian i narzędzia stref;
- zaznaczanie istniejących kafelków oraz ich półprzezroczysty podgląd podczas
  przesuwania;
- obsługa domów, miast, spawnów, potworów i NPC;
- minimapa generowana w tle, diagnostyka czasu renderowania i FPS;
- renderowanie sprite'ów klasycznych DAT/SPR oraz assets Tibia 12+;
- interfejs polski, angielski i hiszpański.

## Odizolowane dane RME

Moduł kompiluje i publikuje dane wyłącznie z katalogu repozytorium `rme-data`.
Nie szuka instalacji RME, repozytorium referencyjnego ani danych silnika w
katalogach nadrzędnych.

Potwory i NPC pochodzą z:

1. wersjonowanego `rme-data/<wersja>/creatures.xml`;
2. spawnów zapisanych w otwartej mapie;
3. jawnego importu XML wykonanego przez użytkownika.

Import ręczny obsługuje `monsters.xml`, pojedyncze pliki `monster`/`npc` oraz
RME `creatures.xml`. Grafika outfitu pochodzi z klienta/assets wybranego ręcznie
w interfejsie.

## Status

Moduł jest funkcjonalny i aktywnie rozwijany. Krytyczne ścieżki wczytywania,
renderowania, palet, minimapy, pędzli i selekcji są objęte testami automatycznymi.
