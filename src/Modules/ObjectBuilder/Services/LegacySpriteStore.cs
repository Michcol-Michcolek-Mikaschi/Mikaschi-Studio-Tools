using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.ObjectBuilder.Services;

public sealed class LegacySpriteStore
{
    private readonly SprParser _parser = new();
    private readonly Dictionary<uint, byte[]> _pixels = new();

    public SprFile? Data { get; private set; }
    public string? FilePath { get; private set; }

    public void Load(string filePath, bool extendedSprites = true, bool? transparentSprites = null)
    {
        Data = _parser.Parse(filePath, new SprParserOptions
        {
            ExtendedSprites = extendedSprites,
            TransparentSprites = transparentSprites
        });
        FilePath = filePath;
        _pixels.Clear();
    }

    public void LoadAuto(string filePath, bool? transparentSprites = null)
    {
        Data = _parser.Parse(filePath);
        if (transparentSprites.HasValue) Data.TransparentSprites = transparentSprites.Value;
        FilePath = filePath;
        _pixels.Clear();
    }

    public void Save(string filePath)
    {
        if (Data is null)
        {
            throw new InvalidOperationException("Brak wczytanego pliku SPR.");
        }

        if (!string.IsNullOrWhiteSpace(Data.ParseWarning))
        {
            throw new InvalidOperationException(
                "Nie zapisuję niekompletnego Tibia.spr. Oryginalny plik może być ucięty; " +
                "najpierw użyj kompletnego pliku SPR.");
        }

        _parser.Save(Data, filePath);
        FilePath = filePath;
    }

    public uint ImportSprite(uint preferredId, byte[] bgraPixels)
    {
        if (Data is null)
        {
            throw new InvalidOperationException("Wczytaj Tibia.spr przed importem obiektów.");
        }

        ArgumentNullException.ThrowIfNull(bgraPixels);
        if (bgraPixels.Length != SprParser.SpriteDataSize)
        {
            throw new ArgumentException("Importowany kafel musi mieć rozmiar 32×32 BGRA.", nameof(bgraPixels));
        }

        if (IsEmpty(bgraPixels)) return 0;

        if (preferredId > 0 && preferredId <= Data.Sprites.Count)
        {
            var existing = GetSpritePixels(preferredId);
            if (existing is not null && existing.AsSpan().SequenceEqual(bgraPixels))
            {
                return preferredId;
            }
        }

        if (!Data.ExtendedSprites && Data.Sprites.Count >= ushort.MaxValue)
        {
            throw new InvalidOperationException("Plik SPR osiągnął limit 65535 sprite'ów dla 16-bitowych ID.");
        }

        var entry = SprParser.CompressSpriteFromBgra(bgraPixels, Data.TransparentSprites);
        Data.Sprites.Add(entry);
        var newId = checked((uint)Data.Sprites.Count);
        _pixels[newId] = (byte[])bgraPixels.Clone();
        return newId;
    }

    public byte[]? GetSpritePixels(uint spriteId)
    {
        if (spriteId == 0 || Data is null)
        {
            return null;
        }

        if (_pixels.TryGetValue(spriteId, out var cached))
        {
            return cached;
        }

        var index = checked((int)spriteId - 1);
        if (index < 0 || index >= Data.Sprites.Count)
        {
            return null;
        }

        var rgba = SprParser.DecompressSprite(Data.Sprites[index], Data.TransparentSprites);
        var bgra = ConvertRgbaToBgra(rgba, !Data.TransparentSprites);
        _pixels[spriteId] = bgra;
        return bgra;
    }

    private static byte[] ConvertRgbaToBgra(byte[] rgba, bool useMagentaKey)
    {
        var bgra = new byte[rgba.Length];
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            var r = rgba[i + 0];
            var g = rgba[i + 1];
            var b = rgba[i + 2];
            var a = rgba[i + 3];

            if (useMagentaKey && r == 255 && g == 0 && b == 255)
            {
                a = 0;
            }

            bgra[i + 0] = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = a;
        }

        return bgra;
    }

    private static bool IsEmpty(byte[] pixels)
    {
        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != 0) return false;
        }
        return true;
    }
}
