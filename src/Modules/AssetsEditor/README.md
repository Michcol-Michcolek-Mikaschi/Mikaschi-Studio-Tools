# Assets Editor

Moduł do edycji nowszego formatu assets OTClient/Tibia 12+:

- `catalog-content.json`
- `appearances.dat`
- arkusze sprite wskazane w katalogu assets
- import/eksport kontenerów `.aec`

Ten moduł nie obsługuje klasycznych `Tibia.spr` i `Tibia.dat`. Te pliki należą do modułu Object Builder.

## Shift (przesunięcie sprite'a)

Pole `AppearanceFlagShift.x/y` jest w proto typu `uint32` (kompatybilne z OTClient mehah).
UI używa typu `int` ze znakiem i konwertuje przez `unchecked((uint)int)` /
`unchecked((int)uint)` — bitowy round-trip zachowuje wzorzec znaczących negatywnych wartości.

Zakres wartości jest pobierany z aktywnego presetu (`Assets/presets.json`):

| Preset | Shift Min | Shift Max | Uwagi |
| --- | --- | --- | --- |
| `Tibia 13.30+` | -32 | +32 | Domyślne; zgodne z natywnym wyświetlaniem klienta CipSoft |
| `Tibia 12.x (Legacy)` | -32 | +32 | Klasyczny format Tibia 12 |
| `opentibiabr` | -32 | +32 | Fork OTClient |
| `Tibia (Free Shift)` | -512 | +512 | Dla zmodyfikowanych klientów |
| `Local Dev` | -32768 | +32767 | Tylko do testów |

Gdy załadowany plik zawiera Shift X/Y poza zakresem aktywnego presetu, limity są
automatycznie rozszerzane (nie clampowane) — żadna informacja nie zostaje utracona,
a user zobaczy w status bar komunikat: `Załadowano Shift X=…, Y=… poza zakresem presetu — limity rozszerzone do […]`.

## Hook compatibility (podwójna emisja)

`AppearanceFlagHook` jest zapisywany w dwóch reprezentacjach proto **jednocześnie**:

| Reprezentacja | Pole proto | Konsument |
| --- | --- | --- |
| Direction enum | `Flags.Hook.Direction` (HOOK_TYPE) | Oryginalny Tibia client / WPF Assets-Editor |
| Booleans | `Flags.HookSouth`, `Flags.HookEast` | OTClient mehah (czyta wprost) |

UI redukuje to do jednego wyboru typu `HookMode { None, South, East }` w postaci radio-group.
Pola pochodne `FlagHookDirection`, `FlagHookSouth`, `FlagHookEast` są synchronizowane przez
`partial void OnHookModeChanged` w ViewModel.

Przy **ładowaniu** ze starych plików zachowane są oba kierunki: priorytet ma `Direction`
(wartości 1=South, 2=East); jeśli Direction nie jest ustawione, używamy boolean fields jako fallback.
