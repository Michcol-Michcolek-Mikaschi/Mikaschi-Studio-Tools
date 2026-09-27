using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

public sealed class SprParserOptions
{
    public bool ExtendedSprites { get; init; } = true;
    public bool? TransparentSprites { get; init; }
}

/// <summary>
/// Parser for Tibia sprite (.spr) files.
/// Sprites are 32x32 pixels, stored with simple transparent-color RLE compression.
/// </summary>
public class SprParser
{
    public const int SpriteWidth  = 32;
    public const int SpriteHeight = 32;
    public const int SpriteDataSize = SpriteWidth * SpriteHeight * 4; // RGBA

    public SprFile Parse(string filePath, SprParserOptions? options = null)
    {
        var data = File.ReadAllBytes(filePath);

        if (data.Length < 6)
            throw new InvalidDataException("File too small to be a valid SPR file.");

        var file = new SprFile();
        file.Signature = BitConverter.ToUInt32(data, 0);

        var header = DetectHeader(data, options);
        file.ExtendedSprites = header.ExtendedSprites;

        // Read offset table
        var offsets = new uint[(int)header.SpriteCount];
        var pos = header.OffsetTableStart;
        for (var i = 0; i < header.SpriteCount; i++)
        {
            if (pos + 4 > data.Length) break;
            offsets[i] = BitConverter.ToUInt32(data, pos); pos += 4;
        }

        var invalidEntries = 0;

        // Read sprite data. Część edytorów zapisuje na końcu tabeli wskaźnik
        // równy długości pliku jako wartownika. Oryginalny Object Builder nie
        // odrzuca przez niego całego SPR, dlatego traktujemy go jak pusty wpis.
        for (var i = 0; i < offsets.Length; i++)
        {
            var entry = new SpriteEntry();
            if (offsets[i] == 0 || offsets[i] == data.Length)
            {
                entry.IsEmpty = true;
            }
            else
            {
                var offset = (int)offsets[i];
                if (IsValidSpriteOffset(offset, header.OffsetTableEnd, data.Length))
                {
                    // Tibia .spr stores transparent color as 3 RGB bytes before the RLE payload size.
                    offset += 3;
                    var compressedSize = BitConverter.ToUInt16(data, offset); offset += 2;
                    var end = offset + compressedSize;
                    if (end <= data.Length)
                    {
                        entry.CompressedData = data[offset..end];
                    }
                    else
                    {
                        entry.IsEmpty = true;
                        invalidEntries++;
                    }
                }
                else
                {
                    entry.IsEmpty = true;
                    invalidEntries++;
                }
            }
            file.Sprites.Add(entry);
        }

        if (invalidEntries > 0)
        {
            file.ParseWarning =
                $"Plik SPR zawiera {invalidEntries} nieprawidłowych lub brakujących wpisów. " +
                "Pozostałe sprite'y zostały wczytane, a niedostępne wpisy oznaczono jako puste.";
        }

        file.TransparentSprites = options?.TransparentSprites ?? DetectTransparency(file.Sprites);

        return file;
    }

    /// <summary>
    /// Decompresses a sprite entry into RGBA pixel data (4096 bytes for 32x32).
    /// Uses transparent-color RLE: pairs of (transparentPixels, coloredPixels) followed by RGB triples.
    /// Magenta (255,0,255) is used for transparent pixels in the output.
    /// </summary>
    public static byte[] DecompressSprite(SpriteEntry entry, bool transparentSprites = false)
    {
        var rgba = new byte[SpriteDataSize];
        // Fill with magenta (transparent color)
        for (int i = 0; i < SpriteWidth * SpriteHeight; i++)
        {
            rgba[i * 4 + 0] = transparentSprites ? (byte)0 : (byte)255;
            rgba[i * 4 + 1] = 0;
            rgba[i * 4 + 2] = transparentSprites ? (byte)0 : (byte)255;
            rgba[i * 4 + 3] = transparentSprites ? (byte)0 : (byte)255;
        }

        if (entry.IsEmpty || entry.CompressedData == null)
            return rgba;

        var src = entry.CompressedData;
        int srcPos = 0;
        int dstPixel = 0;

        var componentCount = transparentSprites ? 4 : 3;
        while (srcPos + 3 < src.Length && dstPixel < SpriteWidth * SpriteHeight)
        {
            ushort transparentPixels = BitConverter.ToUInt16(src, srcPos); srcPos += 2;
            ushort coloredPixels     = BitConverter.ToUInt16(src, srcPos); srcPos += 2;

            dstPixel += transparentPixels; // skip transparent (already magenta)

            for (int c = 0; c < coloredPixels && dstPixel < SpriteWidth * SpriteHeight; c++)
            {
                if (srcPos + componentCount > src.Length) break;
                rgba[dstPixel * 4 + 0] = src[srcPos++]; // R
                rgba[dstPixel * 4 + 1] = src[srcPos++]; // G
                rgba[dstPixel * 4 + 2] = src[srcPos++]; // B
                rgba[dstPixel * 4 + 3] = transparentSprites ? src[srcPos++] : (byte)255; // A
                dstPixel++;
            }
        }

        return rgba;
    }

    /// <summary>
    /// Buduje payload RLE sprite'a z pikseli BGRA używanych przez Avalonię.
    /// </summary>
    public static SpriteEntry CompressSpriteFromBgra(byte[] bgra, bool transparentSprites)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        if (bgra.Length != SpriteDataSize)
        {
            throw new ArgumentException($"Sprite 32×32 wymaga dokładnie {SpriteDataSize} bajtów BGRA.", nameof(bgra));
        }

        static bool IsTransparent(byte[] pixels, int pixel, bool rgba)
        {
            var offset = pixel * 4;
            return pixels[offset + 3] == 0 ||
                   (!rgba && pixels[offset] == 255 && pixels[offset + 1] == 0 && pixels[offset + 2] == 255);
        }

        if (Enumerable.Range(0, SpriteWidth * SpriteHeight).All(pixel => IsTransparent(bgra, pixel, transparentSprites)))
        {
            return new SpriteEntry { IsEmpty = true };
        }

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        var position = 0;
        var pixelCount = SpriteWidth * SpriteHeight;
        while (position < pixelCount)
        {
            var transparentStart = position;
            while (position < pixelCount && IsTransparent(bgra, position, transparentSprites)) position++;
            var transparentCount = position - transparentStart;

            var coloredStart = position;
            while (position < pixelCount && !IsTransparent(bgra, position, transparentSprites)) position++;
            var coloredCount = position - coloredStart;

            writer.Write((ushort)transparentCount);
            writer.Write((ushort)coloredCount);
            for (var pixel = coloredStart; pixel < coloredStart + coloredCount; pixel++)
            {
                var offset = pixel * 4;
                writer.Write(bgra[offset + 2]); // R
                writer.Write(bgra[offset + 1]); // G
                writer.Write(bgra[offset]);     // B
                if (transparentSprites) writer.Write(bgra[offset + 3]);
            }
        }

        return new SpriteEntry { CompressedData = output.ToArray() };
    }

    public void Save(SprFile file, string filePath)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(file.Signature);

        if (file.ExtendedSprites)
            writer.Write((uint)file.Sprites.Count);
        else
        {
            if (file.Sprites.Count > ushort.MaxValue)
            {
                throw new InvalidOperationException("Plik SPR bez rozszerzonych ID nie może mieć więcej niż 65535 sprite'ów.");
            }

            writer.Write((ushort)file.Sprites.Count);
        }

        // Offset table placeholder positions
        long offsetTablePos = ms.Position;
        var offsets = new uint[file.Sprites.Count];
        for (int i = 0; i < file.Sprites.Count; i++)
            writer.Write(0u); // placeholder

        // Write sprite data
        for (int i = 0; i < file.Sprites.Count; i++)
        {
            var entry = file.Sprites[i];
            if (entry.IsEmpty || entry.CompressedData == null)
            {
                offsets[i] = 0;
                continue;
            }
            offsets[i] = (uint)ms.Position;
            writer.Write((byte)0xFF); // transparent color R
            writer.Write((byte)0x00); // transparent color G
            writer.Write((byte)0xFF); // transparent color B
            writer.Write((ushort)entry.CompressedData.Length);
            writer.Write(entry.CompressedData);
        }

        // Patch offset table
        long endPos = ms.Position;
        ms.Position = offsetTablePos;
        for (int i = 0; i < offsets.Length; i++)
            writer.Write(offsets[i]);
        ms.Position = endPos;

        File.WriteAllBytes(filePath, ms.ToArray());
    }

    private static SprHeader DetectHeader(byte[] data, SprParserOptions? options)
    {
        if (TryGetEncryptionMarker(data, out var marker))
        {
            throw new InvalidDataException(
                $"Plik SPR jest zaszyfrowany ({marker}). Do odczytu potrzebny jest klucz " +
                "lub odszyfrowany plik z tego samego klienta.");
        }

        if (options is not null)
        {
            if (!TryReadHeader(data, options.ExtendedSprites, out var forced))
            {
                throw new InvalidDataException(options.ExtendedSprites
                    ? "Plik SPR nie pasuje do trybu rozszerzonych 32-bitowych sprite ID."
                    : "Plik SPR nie pasuje do trybu klasycznych 16-bitowych sprite ID.");
            }

            return forced;
        }

        var hasShort = TryReadHeader(data, false, out var shortHeader);
        var hasExtended = TryReadHeader(data, true, out var extendedHeader);

        if (hasExtended && !hasShort)
        {
            return extendedHeader;
        }

        if (hasShort && !hasExtended)
        {
            return shortHeader;
        }

        if (hasExtended && hasShort)
        {
            return extendedHeader.QualityScore > shortHeader.QualityScore
                ? extendedHeader
                : shortHeader;
        }

        var analyzedHeader = shortHeader.SpriteCount >= extendedHeader.SpriteCount
            ? shortHeader
            : extendedHeader;
        if (analyzedHeader.SpriteCount > 0)
        {
            throw new InvalidDataException(
                "Nie udało się rozpoznać tabeli sprite'ów. Plik wygląda na zaszyfrowany, " +
                "chroniony przez niestandardowy klient albo poważnie uszkodzony. " +
                "Potrzebny jest odszyfrowany SPR lub algorytm i klucz użyty przez klienta.");
        }

        throw new InvalidDataException("Nie udało się rozpoznać tabeli sprite'ów w pliku SPR.");
    }

    private static bool TryReadHeader(byte[] data, bool extendedSprites, out SprHeader header)
    {
        header = default;
        var countOffset = 4;
        var countSize = extendedSprites ? 4 : 2;
        if (data.Length < countOffset + countSize)
        {
            return false;
        }

        var spriteCount = extendedSprites
            ? BitConverter.ToUInt32(data, countOffset)
            : BitConverter.ToUInt16(data, countOffset);

        if (spriteCount > int.MaxValue)
        {
            return false;
        }

        var offsetTableStart = countOffset + countSize;
        if (spriteCount > (data.Length - offsetTableStart) / 4)
        {
            return false;
        }

        var offsetTableEnd = offsetTableStart + (int)spriteCount * 4;
        if (offsetTableEnd > data.Length)
        {
            return false;
        }

        var plausibleEntries = 0;
        var validPayloadEntries = 0;
        for (var i = 0; i < spriteCount; i++)
        {
            var offset = BitConverter.ToUInt32(data, offsetTableStart + i * 4);
            if (offset == 0 || offset == data.Length)
            {
                plausibleEntries++;
                continue;
            }

            if (offset > int.MaxValue || !IsValidSpriteOffset((int)offset, offsetTableEnd, data.Length))
            {
                continue;
            }

            var compressedSize = BitConverter.ToUInt16(data, (int)offset + 3);
            if ((int)offset + 5 + compressedSize > data.Length)
            {
                continue;
            }

            plausibleEntries++;
            validPayloadEntries++;
        }

        // Zaszyfrowane lub losowe dane czasami przypadkiem tworzą kilka
        // wskaźników w zakresie pliku. Wymagamy spójności co najmniej 80%
        // tabeli; pozwala to jednocześnie otworzyć częściowo ucięty SPR.
        var qualityScore = spriteCount == 0
            ? 1000
            : checked((int)((long)plausibleEntries * 1000 / spriteCount));
        header = new SprHeader(
            extendedSprites,
            spriteCount,
            offsetTableStart,
            offsetTableEnd,
            qualityScore);

        if (spriteCount > 0 && ((long)plausibleEntries * 100 < (long)spriteCount * 80 ||
            (validPayloadEntries == 0 && plausibleEntries < spriteCount)))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetEncryptionMarker(byte[] data, out string marker)
    {
        marker = string.Empty;
        if (data.Length < 4 || data[0] != (byte)'E' || data[1] != (byte)'N' || data[2] != (byte)'C')
        {
            return false;
        }

        var suffix = (char)data[3];
        if (!char.IsAsciiLetterOrDigit(suffix))
        {
            return false;
        }

        marker = $"ENC{suffix}";
        return true;
    }

    private static bool IsValidSpriteOffset(int offset, int offsetTableEnd, int fileLength)
    {
        return offset >= offsetTableEnd && offset + 5 <= fileLength;
    }

    private static bool DetectTransparency(IEnumerable<SpriteEntry> sprites)
    {
        var rgbOnly = 0;
        var rgbaOnly = 0;
        var checkedCount = 0;
        foreach (var entry in sprites)
        {
            if (entry.IsEmpty || entry.CompressedData is not { Length: > 0 } data) continue;

            var rgb = IsValidRle(data, 3);
            var rgba = IsValidRle(data, 4);
            if (rgb && !rgba) rgbOnly++;
            if (rgba && !rgb) rgbaOnly++;
            if (++checkedCount >= 128) break;
        }

        return rgbaOnly > rgbOnly;
    }

    private static bool IsValidRle(byte[] data, int componentCount)
    {
        var offset = 0;
        var pixels = 0;
        while (offset < data.Length)
        {
            if (offset + 4 > data.Length) return false;
            var transparent = BitConverter.ToUInt16(data, offset);
            var colored = BitConverter.ToUInt16(data, offset + 2);
            offset += 4;
            if (pixels + transparent + colored > SpriteWidth * SpriteHeight) return false;
            var colorBytes = colored * componentCount;
            if (offset + colorBytes > data.Length) return false;
            offset += colorBytes;
            pixels += transparent + colored;
        }

        return offset == data.Length;
    }

    private readonly record struct SprHeader(
        bool ExtendedSprites,
        uint SpriteCount,
        int OffsetTableStart,
        int OffsetTableEnd,
        int QualityScore);
}
