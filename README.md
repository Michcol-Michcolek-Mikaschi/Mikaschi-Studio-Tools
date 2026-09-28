# Mikaschi Studio Tools

Samodzielny zestaw narzędzi do pracy z OTS, rozwijany w C#/.NET i Avalonia.
Repozytorium zawiera kod źródłowy, dane zgodności RME oraz gotową aplikację dla
Windows x64.

## Uruchomienie

Gotowy program znajduje się tutaj:

`build/Mikaschi-Studio-Tools-win-x64/Mikaschi Studio Tools.exe`

Aplikacja jest publikowana jako **self-contained**, więc do samego uruchomienia
nie trzeba instalować środowiska .NET. Folderu z programem nie należy rozdzielać:
pliki DLL i katalog `rme-data` są częścią aplikacji.

## Map Editor — skąd pochodzą potwory i NPC

Edytor korzysta wyłącznie z jawnych, odizolowanych źródeł:

1. `rme-data/<wersja>/creatures.xml` dołączonego do programu;
2. stworzeń i spawnów zapisanych w otwartej mapie OTBM oraz jej plikach danych;
3. katalogów lub plików XML dodanych ręcznie w **Menedżerze katalogów potworów i NPC**.

Menedżer zapamiętuje źródła, ich kolejność i stan włączenia. Ręczny import
obsługuje katalogi `monster`/`monsters` i `npc`/`npcs`, indeks `monsters.xml`,
pojedyncze pliki `monster` i `npc` oraz plik RME `creatures.xml`. Program **nie
wykrywa i nie przeszukuje automatycznie folderów silnika ani katalogów
nadrzędnych**. Silnik TFS, OTClient ani prywatne dane serwera nie są częścią tego
repozytorium.

Wygląd stworzenia jest odczytywany z `looktype` albo `lookitem`, natomiast jego
grafika pochodzi z folderu klienta/assets wskazanego ręcznie w Map Editorze.
Dlatego do poprawnego podglądu należy wczytać assets zgodne z wersją mapy.

## Kompilowanie

Wymagania dla programisty:

- Windows x64;
- .NET 10 SDK;
- PowerShell.

Pełne testy i publikacja:

```powershell
.\build-program.ps1 -Test
```

Sama publikacja:

```powershell
.\build-program.ps1
```

Skrypt zawsze zastępuje tylko znane wyniki w `build` i pozostawia jedną gotową
wersję programu. Więcej informacji znajduje się w [build.md](build.md).

## Struktura repozytorium

- `src/` — kod aplikacji i testy;
- `rme-data/` — wersjonowane dane zgodności Map Editora;
- `build/Mikaschi-Studio-Tools-win-x64/` — gotowa aplikacja;
- `licenses/` — warunki dotyczące dołączonych składników zewnętrznych;
- `.github/workflows/` — automatyczna weryfikacja kompilacji i testów.

## Prywatność i dane użytkownika

Repozytorium nie zawiera silnika gry, klienta, map użytkownika, danych kont,
sekretów ani lokalnych ścieżek roboczych. Mapy, assets i XML-e są wybierane przez
użytkownika i nie są automatycznie kopiowane do repozytorium.

## Licencje

Nie nadano jeszcze jednej licencji obejmującej cały autorski kod projektu.
Składniki zewnętrzne zachowują własne warunki. Szczegóły znajdują się w
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) oraz katalogu `licenses`.
