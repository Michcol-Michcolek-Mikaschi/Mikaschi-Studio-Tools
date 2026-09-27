namespace Modules.AssetsEditor.Services;

public readonly record struct OutfitColor(byte R, byte G, byte B);

public readonly record struct OutfitColorSet(
    OutfitColor Head,
    OutfitColor Body,
    OutfitColor Legs,
    OutfitColor Feet)
{
    public static OutfitColorSet FromHsi(int head, int body, int legs, int feet) =>
        new(HsiToRgb(head), HsiToRgb(body), HsiToRgb(legs), HsiToRgb(feet));

    public static OutfitColor HsiToRgb(int color)
    {
        const int values = 7;
        const int steps = 19;
        double h;
        double s;
        double i;
        double r;
        double g;
        double b;

        if (color < 0 || color >= steps * values)
        {
            color = 0;
        }

        if (color % steps == 0)
        {
            h = 0;
            s = 0;
            i = 1 - color / (double)steps / values;
        }
        else
        {
            h = color % steps * (1d / 18d);
            s = color / steps switch
            {
                0 => 0.25,
                1 => 0.25,
                2 => 0.5,
                3 => 0.667,
                _ => 1
            };
            i = color / steps switch
            {
                0 => 1,
                1 => 0.75,
                2 => 0.75,
                3 => 0.75,
                4 => 1,
                5 => 0.75,
                _ => 0.5
            };
        }

        if (i == 0)
        {
            return new OutfitColor(0, 0, 0);
        }

        if (s == 0)
        {
            var gray = ToByte(i * 255);
            return new OutfitColor(gray, gray, gray);
        }

        if (h < 1d / 6d)
        {
            r = i;
            b = i * (1 - s);
            g = b + (i - b) * 6 * h;
        }
        else if (h < 2d / 6d)
        {
            g = i;
            b = i * (1 - s);
            r = g - (i - b) * (6 * h - 1);
        }
        else if (h < 3d / 6d)
        {
            g = i;
            r = i * (1 - s);
            b = r + (i - r) * (6 * h - 2);
        }
        else if (h < 4d / 6d)
        {
            b = i;
            r = i * (1 - s);
            g = b - (i - r) * (6 * h - 3);
        }
        else if (h < 5d / 6d)
        {
            b = i;
            g = i * (1 - s);
            r = g + (i - g) * (6 * h - 4);
        }
        else
        {
            r = i;
            g = i * (1 - s);
            b = r - (i - g) * (6 * h - 5);
        }

        return new OutfitColor(ToByte(r * 255), ToByte(g * 255), ToByte(b * 255));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)value, 0, 255);
}

public static class OutfitColorizer
{
    public static byte[] ApplyTemplate(
        ReadOnlySpan<byte> templatePixels,
        ReadOnlySpan<byte> outfitPixels,
        int width,
        int height,
        OutfitColorSet colors)
    {
        var expectedLength = checked(width * height * 4);
        if (templatePixels.Length < expectedLength)
        {
            throw new ArgumentException("Maska kolorów jest krótsza niż rozmiar obrazu.", nameof(templatePixels));
        }

        if (outfitPixels.Length < expectedLength)
        {
            throw new ArgumentException("Warstwa stroju jest krótsza niż rozmiar obrazu.", nameof(outfitPixels));
        }

        var result = outfitPixels[..expectedLength].ToArray();
        for (var offset = 0; offset < expectedLength; offset += 4)
        {
            var templateB = templatePixels[offset];
            var templateG = templatePixels[offset + 1];
            var templateR = templatePixels[offset + 2];
            var outfitB = result[offset];
            var outfitG = result[offset + 1];
            var outfitR = result[offset + 2];

            if (templateR == outfitR && templateG == outfitG && templateB == outfitB)
            {
                continue;
            }

            var color = ResolvePartColor(templateR, templateG, templateB, colors);
            if (color is null)
            {
                continue;
            }

            result[offset] = Average(outfitB, color.Value.B);
            result[offset + 1] = Average(outfitG, color.Value.G);
            result[offset + 2] = Average(outfitR, color.Value.R);
        }

        return result;
    }

    private static OutfitColor? ResolvePartColor(byte r, byte g, byte b, OutfitColorSet colors)
    {
        if (r > 0 && g > 0 && b == 0)
        {
            return colors.Head;
        }

        if (r > 0 && g == 0 && b == 0)
        {
            return colors.Body;
        }

        if (r == 0 && g > 0 && b == 0)
        {
            return colors.Legs;
        }

        if (r == 0 && g == 0 && b > 0)
        {
            return colors.Feet;
        }

        return null;
    }

    private static byte Average(byte value, byte color) => (byte)((value + color) / 2);
}
