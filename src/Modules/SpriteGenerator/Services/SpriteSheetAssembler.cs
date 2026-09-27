using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.SpriteGenerator.Services;

/// <summary>
/// Buduje sprite sheet (PNG) z wygenerowanych klatek.
/// Układ: grid CxR (preferowany kwadratowy), wszystkie klatki tego samego rozmiaru,
/// 2px padding między klatkami (ochrona przed bleedingiem przy mip-mapping).
/// </summary>
public sealed class SpriteSheetAssembler
{
    public byte[] Assemble(IReadOnlyList<byte[]> framePngBytes, int targetTileSize, int padding = 2)
    {
        if (framePngBytes.Count == 0) throw new ArgumentException("Brak klatek.", nameof(framePngBytes));

        // Najpierw dekoduj wszystkie klatki + przeskaluj do targetTileSize
        var frames = new List<Image<Rgba32>>(framePngBytes.Count);
        try
        {
            foreach (var bytes in framePngBytes)
            {
                var img = Image.Load<Rgba32>(bytes);
                if (img.Width != targetTileSize || img.Height != targetTileSize)
                {
                    img.Mutate(ctx => ctx.Resize(new ResizeOptions
                    {
                        Size = new Size(targetTileSize, targetTileSize),
                        Sampler = KnownResamplers.Box, // dobry dla pixel-art
                        Mode = ResizeMode.Stretch,
                    }));
                }
                frames.Add(img);
            }

            // Układ grid: cols = ceil(sqrt(N)), rows = ceil(N / cols)
            var cols = (int)Math.Ceiling(Math.Sqrt(frames.Count));
            var rows = (int)Math.Ceiling((double)frames.Count / cols);

            var sheetW = cols * targetTileSize + (cols - 1) * padding;
            var sheetH = rows * targetTileSize + (rows - 1) * padding;

            using var sheet = new Image<Rgba32>(sheetW, sheetH, new Rgba32(0, 0, 0, 0));
            for (var i = 0; i < frames.Count; i++)
            {
                var r = i / cols;
                var c = i % cols;
                var x = c * (targetTileSize + padding);
                var y = r * (targetTileSize + padding);
                var f = frames[i];
                sheet.Mutate(ctx => ctx.DrawImage(f, new Point(x, y), 1f));
            }

            using var ms = new MemoryStream();
            sheet.SaveAsPng(ms);
            return ms.ToArray();
        }
        finally
        {
            foreach (var f in frames) f.Dispose();
        }
    }
}
