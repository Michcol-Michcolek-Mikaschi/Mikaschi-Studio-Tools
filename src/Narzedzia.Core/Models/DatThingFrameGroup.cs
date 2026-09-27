namespace Narzedzia.Core.Models;

/// <summary>Czas trwania jednej klatki animacji (minimalna i maksymalna).</summary>
public sealed class DatFrameDuration
{
    public uint Min { get; set; }
    public uint Max { get; set; }
    public override string ToString() => $"{Min}-{Max} ms";
}

/// <summary>
/// Jedna grupa klatek (frame group) z pliku .dat.
/// Outfit może mieć dwie grupy: Idle (0) i Walking (1).
/// Pozostałe kategorie mają zawsze jedną grupę.
/// </summary>
public sealed class DatThingFrameGroup
{
    /// <summary>Typ grupy: 0 = Idle, 1 = Walking (tylko outfity).</summary>
    public int GroupType { get; set; }

    /// <summary>Szerokość sprite'a w kafelkach 32 px (zwykle 1 lub 2).</summary>
    public byte Width { get; set; }

    /// <summary>Wysokość sprite'a w kafelkach 32 px (zwykle 1 lub 2).</summary>
    public byte Height { get; set; }

    /// <summary>Dokładny rozmiar pikseli dla obiektów 2×2 (inaczej 32).</summary>
    public byte ExactSize { get; set; } = 32;

    /// <summary>Liczba warstw (layers), np. 1 lub 2 dla outfitów z addons.</summary>
    public byte Layers { get; set; }

    /// <summary>Wzór w osi X — liczba wariantów poziomych (np. 4 kierunki).</summary>
    public byte PatternX { get; set; }

    /// <summary>Wzór w osi Y — liczba wariantów pionowych.</summary>
    public byte PatternY { get; set; }

    /// <summary>Wzór w osi Z — głębokość wzoru.</summary>
    public byte PatternZ { get; set; }

    /// <summary>Liczba klatek animacji.</summary>
    public byte Frames { get; set; }

    /// <summary>Czy obiekt ma animację (więcej niż 1 klatka).</summary>
    public bool IsAnimation => Frames > 1;

    // --- Dane animacji (improvedAnimations, klient >= 10.10) ---

    /// <summary>Tryb animacji: 0 = asynchroniczny, 1 = synchroniczny.</summary>
    public byte AnimationMode { get; set; }

    /// <summary>Liczba pętli animacji (-1 = bez końca, 0 = ping-pong).</summary>
    public int LoopCount { get; set; }

    /// <summary>Klatka startowa (signed; -1 = losowa).</summary>
    public sbyte StartFrame { get; set; }

    /// <summary>Czasy trwania poszczególnych klatek (dla improvedAnimations).</summary>
    public DatFrameDuration[] FrameDurations { get; set; } = [];

    /// <summary>Tablica ID sprite'ów dla wszystkich slotów grupy.</summary>
    public uint[] SpriteIds { get; set; } = [];

    /// <summary>Łączna liczba slotów sprite = W*H*Layers*PatX*PatY*PatZ*Frames.</summary>
    public int TotalSprites =>
        Math.Max(1, (int)Width) * Math.Max(1, (int)Height) *
        Math.Max(1, (int)Layers) * Math.Max(1, (int)PatternX) *
        Math.Max(1, (int)PatternY) * Math.Max(1, (int)PatternZ) *
        Math.Max(1, (int)Frames);

    /// <summary>Pierwszy sprite ID w tej grupie (dla podglądu).</summary>
    public uint FirstSpriteId => SpriteIds.Length > 0 ? SpriteIds[0] : 0;

    public string GroupName => GroupType == 1 ? "Walking" : "Idle";

    /// <summary>Tworzy niezależną kopię grupy razem z tablicami animacji i sprite ID.</summary>
    public DatThingFrameGroup DeepClone()
    {
        var clone = (DatThingFrameGroup)MemberwiseClone();
        clone.SpriteIds = [.. SpriteIds];
        clone.FrameDurations = FrameDurations
            .Select(duration => new DatFrameDuration { Min = duration.Min, Max = duration.Max })
            .ToArray();
        return clone;
    }
}
