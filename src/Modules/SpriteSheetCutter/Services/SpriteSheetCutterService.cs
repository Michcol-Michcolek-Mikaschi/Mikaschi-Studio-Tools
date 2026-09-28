using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.SpriteSheetCutter.Services;

/// <summary>
/// Native port of the original sprite_sheet_cutter.py processing pipeline.
/// </summary>
public sealed class SpriteSheetCutterService
{
    public CutResult CutSingle(
        string inputPath,
        int spriteWidth,
        int spriteHeight,
        IProgress<SpriteCutProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ValidateSpriteSize(spriteWidth, spriteHeight);
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Nie znaleziono pliku wejściowego.", inputPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var sheet = Image.Load<Rgba32>(inputPath);
        var columns = DivideRoundUp(sheet.Width, spriteWidth);
        var rows = DivideRoundUp(sheet.Height, spriteHeight);
        var totalFrames = checked(columns * rows);
        var outputFolder = BuildOutputFolder(inputPath);
        Directory.CreateDirectory(outputFolder);

        var result = new CutResult
        {
            InputPath = Path.GetFullPath(inputPath),
            OutputFolder = outputFolder,
            SourceWidth = sheet.Width,
            SourceHeight = sheet.Height,
            Columns = columns,
            Rows = rows,
            TotalFrames = totalFrames
        };

        var frameIndex = 0;
        var progressInterval = Math.Max(1, totalFrames / 200);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourceX = column * spriteWidth;
                var sourceY = row * spriteHeight;
                var copiedWidth = Math.Min(spriteWidth, sheet.Width - sourceX);
                var copiedHeight = Math.Min(spriteHeight, sheet.Height - sourceY);

                using var frame = new Image<Rgba32>(
                    spriteWidth,
                    spriteHeight,
                    new Rgba32(0, 0, 0, 0));
                using var fragment = sheet.Clone(context => context.Crop(
                    new Rectangle(sourceX, sourceY, copiedWidth, copiedHeight)));
                frame.Mutate(context => context.DrawImage(fragment, Point.Empty, 1f));

                var outputPath = Path.Combine(outputFolder, $"sprite_{frameIndex:D3}.png");
                frame.SaveAsPng(outputPath);
                result.OutputFiles.Add(outputPath);
                frameIndex++;
                if (frameIndex == 1 || frameIndex == totalFrames || frameIndex % progressInterval == 0)
                {
                    progress?.Report(new SpriteCutProgress(frameIndex, totalFrames, outputPath));
                }
            }
        }

        return result;
    }

    public SpriteSheetPreview CreatePreview(
        string inputPath,
        int spriteWidth,
        int spriteHeight,
        int maxPreviewWidth = 800,
        int maxPreviewHeight = 600,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ValidateSpriteSize(spriteWidth, spriteHeight);
        if (maxPreviewWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPreviewWidth));
        }

        if (maxPreviewHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPreviewHeight));
        }

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Nie znaleziono pliku wejściowego.", inputPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var preview = Image.Load<Rgba32>(inputPath);
        var sourceWidth = preview.Width;
        var sourceHeight = preview.Height;
        DrawCuttingGrid(preview, spriteWidth, spriteHeight, cancellationToken);

        var scale = Math.Min(
            Math.Min(maxPreviewWidth / (double)preview.Width, maxPreviewHeight / (double)preview.Height),
            1d);
        var previewWidth = Math.Max(1, (int)(preview.Width * scale));
        var previewHeight = Math.Max(1, (int)(preview.Height * scale));
        if (previewWidth != preview.Width || previewHeight != preview.Height)
        {
            preview.Mutate(context => context.Resize(new ResizeOptions
            {
                Size = new Size(previewWidth, previewHeight),
                Mode = ResizeMode.Stretch,
                Sampler = KnownResamplers.Lanczos3
            }));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        preview.SaveAsPng(stream);

        var columns = DivideRoundUp(sourceWidth, spriteWidth);
        var rows = DivideRoundUp(sourceHeight, spriteHeight);
        return new SpriteSheetPreview(
            stream.ToArray(),
            sourceWidth,
            sourceHeight,
            previewWidth,
            previewHeight,
            spriteWidth,
            spriteHeight,
            columns,
            rows,
            checked(columns * rows));
    }

    public static string BuildOutputFolder(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputPath))!, "sliced_sprites");
    }

    private static void DrawCuttingGrid(
        Image<Rgba32> image,
        int spriteWidth,
        int spriteHeight,
        CancellationToken cancellationToken)
    {
        var red = new Rgba32(255, 0, 0, 255);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = accessor.GetRowSpan(y);
                for (var x = spriteWidth; x < row.Length; x += spriteWidth)
                {
                    row[x] = red;
                    if (x + 1 < row.Length)
                    {
                        row[x + 1] = red;
                    }
                }
            }

            for (var y = spriteHeight; y < accessor.Height; y += spriteHeight)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var lineOffset = 0; lineOffset < 2 && y + lineOffset < accessor.Height; lineOffset++)
                {
                    accessor.GetRowSpan(y + lineOffset).Fill(red);
                }
            }
        });
    }

    private static int DivideRoundUp(int value, int divisor) =>
        checked((int)(((long)value + divisor - 1) / divisor));

    private static void ValidateSpriteSize(int spriteWidth, int spriteHeight)
    {
        if (spriteWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(spriteWidth), "Szerokość sprite'a musi być większa od zera.");
        }

        if (spriteHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(spriteHeight), "Wysokość sprite'a musi być większa od zera.");
        }
    }
}

public sealed record SpriteCutProgress(int Completed, int Total, string OutputPath);

public sealed record SpriteSheetPreview(
    byte[] PngData,
    int SourceWidth,
    int SourceHeight,
    int PreviewWidth,
    int PreviewHeight,
    int SpriteWidth,
    int SpriteHeight,
    int Columns,
    int Rows,
    int FrameCount);

public sealed class CutResult
{
    public required string InputPath { get; init; }
    public required string OutputFolder { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public int Columns { get; init; }
    public int Rows { get; init; }
    public int TotalFrames { get; init; }
    public List<string> OutputFiles { get; } = [];
}

public sealed record SpriteSizePreset(string Label, int Width, int Height)
{
    public override string ToString() => Label;
}

/// <summary>
/// The exact 294-entry preset table from sprite_sheet_cutter.py, kept in its original order.
/// </summary>
public static class SpriteSizePresetCatalog
{
    public static IReadOnlyList<SpriteSizePreset> All { get; } = CreateAll();

    public static SpriteSizePreset Default { get; } =
        All.Single(preset => preset.Width == 64 && preset.Height == 64);

    private static IReadOnlyList<SpriteSizePreset> CreateAll()
    {
        var result = new List<SpriteSizePreset>(294);

        for (var columns = 1; columns <= 14; columns++)
        {
            for (var rows = 1; rows <= 14; rows++)
            {
                result.Add(Create(columns, rows));
            }

            result.Add(Create(columns, 16));
        }

        for (var rows = 1; rows <= 16; rows++)
        {
            result.Add(Create(15, rows));
        }

        for (var rows = 1; rows <= 14; rows++)
        {
            result.Add(Create(16, rows));
        }

        result.Add(Create(16, 16));

        for (var square = 17; square <= 34; square++)
        {
            result.Add(Create(square, square));
        }

        for (var rows = 1; rows <= 35; rows++)
        {
            result.Add(Create(35, rows));
        }

        if (result.Count != 294)
        {
            throw new InvalidOperationException($"Nieprawidłowa liczba presetów: {result.Count}.");
        }

        return result.AsReadOnly();
    }

    private static SpriteSizePreset Create(int columns, int rows)
    {
        var width = checked(columns * 32);
        var height = checked(rows * 32);
        return new SpriteSizePreset($"{width}x{height} ({columns}×{rows})", width, height);
    }
}
