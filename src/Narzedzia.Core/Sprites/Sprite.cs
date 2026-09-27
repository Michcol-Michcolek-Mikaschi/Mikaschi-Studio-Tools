namespace Narzedzia.Core.Sprites;

/// <summary>
/// Klasyczny sprite Tibii 32x32 w formacie BGRA [kolejnosc kanalow: niebieski-zielony-czerwony-alpha].
/// </summary>
public sealed class Sprite
{
    public const int DefaultSize = 32;
    public const ushort RgbPixelsDataSize = 3072;
    public const ushort ArgbPixelsDataSize = 4096;

    public uint Id { get; set; }
    public uint Size { get; set; }
    public byte[]? CompressedPixels { get; set; }
    public bool Transparent { get; set; }

    private static readonly byte[] EmptyPixels = new byte[ArgbPixelsDataSize];

    public byte[] GetPixelsBgra()
    {
        if (CompressedPixels is null || CompressedPixels.Length != Size)
        {
            return (byte[])EmptyPixels.Clone();
        }

        return UncompressPixelsBgra(CompressedPixels, Transparent);
    }

    public static Sprite Empty(uint id) => new()
    {
        Id = id
    };

    public static byte[] UncompressPixelsBgra(byte[] compressed, bool transparent)
    {
        ArgumentNullException.ThrowIfNull(compressed);

        var pixels = new byte[ArgbPixelsDataSize];
        var componentCount = transparent ? 4 : 3;
        var write = 0;
        var position = 0;

        while (position < compressed.Length)
        {
            if (position + 4 > compressed.Length)
            {
                break;
            }

            var transparentPixels = compressed[position++] | (compressed[position++] << 8);
            var coloredPixels = compressed[position++] | (compressed[position++] << 8);

            for (var i = 0; i < transparentPixels && write + 4 <= pixels.Length; i++)
            {
                pixels[write++] = 0;
                pixels[write++] = 0;
                pixels[write++] = 0;
                pixels[write++] = 0;
            }

            for (var i = 0; i < coloredPixels && write + 4 <= pixels.Length; i++)
            {
                if (position + componentCount > compressed.Length)
                {
                    break;
                }

                var r = compressed[position++];
                var g = compressed[position++];
                var b = compressed[position++];
                var a = transparent ? compressed[position++] : (byte)0xFF;

                pixels[write++] = b;
                pixels[write++] = g;
                pixels[write++] = r;
                pixels[write++] = a;
            }
        }

        return pixels;
    }
}
