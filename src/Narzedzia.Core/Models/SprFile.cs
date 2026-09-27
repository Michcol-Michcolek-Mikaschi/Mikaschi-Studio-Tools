namespace Narzedzia.Core.Models;

public class SprFile
{
    public uint Signature { get; set; }
    public bool ExtendedSprites { get; set; }  // 4-bajtowa liczba sprite'ów (Object Builder: klient 9.60+)
    public bool TransparentSprites { get; set; } // Kolorowe piksele zawierają kanał alfa (RGBA).
    /// <summary>
    /// Informacja o wpisach tabeli SPR, których nie dało się odczytać. Poprawne
    /// sprite'y pozostają dostępne, tak jak w oryginalnym Object Builderze.
    /// </summary>
    public string? ParseWarning { get; set; }
    public List<SpriteEntry> Sprites { get; set; } = new();
}

public class SpriteEntry
{
    public bool IsEmpty { get; set; }
    /// <summary>Raw compressed data as stored in the file (null = empty sprite).</summary>
    public byte[]? CompressedData { get; set; }
    /// <summary>Decompressed RGBA pixel data (32x32 = 1024 pixels = 4096 bytes), null if not yet decoded.</summary>
    public byte[]? PixelData { get; set; }
}
