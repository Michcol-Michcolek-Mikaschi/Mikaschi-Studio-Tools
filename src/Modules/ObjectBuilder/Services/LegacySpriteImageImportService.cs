using Narzedzia.Core.Parsers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.ObjectBuilder.Services;

public sealed record LegacySpriteImportTile(
    string SourcePath,
    int Column,
    int Row,
    byte[] BgraPixels);

public sealed record LegacySpriteImportBatch(
    int SourceFileCount,
    int RejectedFileCount,
    int EmptyTileCount,
    bool HasPartialAlpha,
    IReadOnlyList<LegacySpriteImportTile> Tiles);

/// <summary>
/// Wczytuje pliki graficzne do starego formatu SPR. Obrazy są cięte na kafle
/// 32×32 bez skalowania, w naturalnej kolejności: wierszami od lewej do prawej.
/// </summary>
public sealed class LegacySpriteImageImportService
{
    private const int MaxTilesPerBatch = 100_000;

    public static bool IsSupportedFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public LegacySpriteImportBatch DecodeFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var files = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var tiles = new List<LegacySpriteImportTile>();
        var sourceFileCount = 0;
        var rejectedFileCount = 0;
        var emptyTileCount = 0;
        var hasPartialAlpha = false;

        foreach (var path in files)
        {
            if (!IsSupportedFile(path))
            {
                rejectedFileCount++;
                continue;
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Nie znaleziono pliku sprite'a.", path);
            }

            using var image = Image.Load<Rgba32>(path);
            if (image.Width < SprParser.SpriteWidth || image.Height < SprParser.SpriteHeight ||
                image.Width % SprParser.SpriteWidth != 0 || image.Height % SprParser.SpriteHeight != 0)
            {
                throw new InvalidDataException(
                    $"{Path.GetFileName(path)} ma rozmiar {image.Width}×{image.Height}. " +
                    "Każdy wymiar musi być dodatnią wielokrotnością 32 px.");
            }

            sourceFileCount++;
            var columns = image.Width / SprParser.SpriteWidth;
            var rows = image.Height / SprParser.SpriteHeight;
            if ((long)tiles.Count + (long)columns * rows > MaxTilesPerBatch)
            {
                throw new InvalidDataException(
                    $"Jednorazowo można zaimportować maksymalnie {MaxTilesPerBatch:N0} kafli 32×32.");
            }

            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var pixels = DecodeTile(image, column, row, ref hasPartialAlpha);
                if (IsEmpty(pixels))
                {
                    emptyTileCount++;
                    continue;
                }

                tiles.Add(new LegacySpriteImportTile(path, column, row, pixels));
            }
        }

        return new LegacySpriteImportBatch(
            sourceFileCount,
            rejectedFileCount,
            emptyTileCount,
            hasPartialAlpha,
            tiles);
    }

    private static byte[] DecodeTile(
        Image<Rgba32> image,
        int column,
        int row,
        ref bool hasPartialAlpha)
    {
        var result = new byte[SprParser.SpriteDataSize];
        var startX = column * SprParser.SpriteWidth;
        var startY = row * SprParser.SpriteHeight;

        for (var y = 0; y < SprParser.SpriteHeight; y++)
        for (var x = 0; x < SprParser.SpriteWidth; x++)
        {
            var source = image[startX + x, startY + y];
            var isMagentaKey = source.R == byte.MaxValue && source.G == 0 && source.B == byte.MaxValue;
            var alpha = isMagentaKey ? (byte)0 : source.A;
            if (alpha is > 0 and < byte.MaxValue) hasPartialAlpha = true;

            var offset = (y * SprParser.SpriteWidth + x) * 4;
            result[offset] = source.B;
            result[offset + 1] = source.G;
            result[offset + 2] = source.R;
            result[offset + 3] = alpha;
        }

        return result;
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
