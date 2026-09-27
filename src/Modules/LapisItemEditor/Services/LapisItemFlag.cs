namespace Modules.LapisItemEditor.Services;

[Flags]
public enum LapisItemFlag : uint
{
    None = 0,
    Unpassable = 1u << 0,
    BlockMissiles = 1u << 1,
    BlockPathfinder = 1u << 2,
    HasElevation = 1u << 3,
    MultiUse = 1u << 4,
    Pickupable = 1u << 5,
    Movable = 1u << 6,
    Stackable = 1u << 7,
    FloorChangeDown = 1u << 8,
    FloorChangeNorth = 1u << 9,
    FloorChangeEast = 1u << 10,
    FloorChangeSouth = 1u << 11,
    FloorChangeWest = 1u << 12,
    StackOrder = 1u << 13,
    Readable = 1u << 14,
    Rotatable = 1u << 15,
    Hangable = 1u << 16,
    HookSouth = 1u << 17,
    HookEast = 1u << 18,
    CanNotDecay = 1u << 19,
    AllowDistanceRead = 1u << 20,
    Unused = 1u << 21,
    ClientCharges = 1u << 22,
    IgnoreLook = 1u << 23,
    IsAnimation = 1u << 24,
    FullGround = 1u << 25,
    ForceUse = 1u << 26
}

public sealed record LapisItemFlagDescriptor(LapisItemFlag Flag, string Label, string Description)
{
    public static IReadOnlyList<LapisItemFlagDescriptor> All { get; } =
    [
        new(LapisItemFlag.Unpassable, "Blokuje przejście", "Postać nie może przejść przez item."),
        new(LapisItemFlag.BlockMissiles, "Blokuje pociski", "Item zatrzymuje pociski i czary dystansowe."),
        new(LapisItemFlag.BlockPathfinder, "Blokuje pathfinder", "AI traktuje item jako przeszkodę."),
        new(LapisItemFlag.HasElevation, "Ma podwyższenie", "Item podnosi pozycję chodzenia."),
        new(LapisItemFlag.MultiUse, "Multi-use", "Item obsługuje użycie na celu."),
        new(LapisItemFlag.Pickupable, "Można podnieść", "Item można zabrać do ekwipunku."),
        new(LapisItemFlag.Movable, "Przesuwalny", "Item można przesuwać."),
        new(LapisItemFlag.Stackable, "Stackowalny", "Item łączy się w stosy."),
        new(LapisItemFlag.StackOrder, "Kolejność stosu", "Item ma specjalną kolejność rysowania na tile."),
        new(LapisItemFlag.Readable, "Czytelny", "Item ma tekst do czytania."),
        new(LapisItemFlag.Rotatable, "Obracalny", "Item można obracać."),
        new(LapisItemFlag.Hangable, "Do zawieszenia", "Item można zawiesić na ścianie."),
        new(LapisItemFlag.HookSouth, "Hak południe", "Wariant haka południowego."),
        new(LapisItemFlag.HookEast, "Hak wschód", "Wariant haka wschodniego."),
        new(LapisItemFlag.AllowDistanceRead, "Czytanie z dystansu", "Tekst można czytać z większej odległości."),
        new(LapisItemFlag.ClientCharges, "Ładunki klienta", "Klient pokazuje ładunki itemu."),
        new(LapisItemFlag.IgnoreLook, "Ignoruj look", "Item nie pokazuje standardowego opisu."),
        new(LapisItemFlag.IsAnimation, "Animowany", "Item ma animację klienta."),
        new(LapisItemFlag.FullGround, "Pełny ground", "Item przykrywa cały ground."),
        new(LapisItemFlag.ForceUse, "Wymuś use", "Item wymusza akcję użycia.")
    ];
}
