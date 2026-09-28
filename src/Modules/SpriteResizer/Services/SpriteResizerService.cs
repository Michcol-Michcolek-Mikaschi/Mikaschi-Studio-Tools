using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.SpriteResizer.Services;

/// <summary>
/// Native port of the original sprite_resizer.py processing pipeline.
/// </summary>
public sealed class SpriteResizerService
{
    public static readonly Rgba32 MagentaColor = new(255, 0, 255, 255);

    public ResizeResult ResizeFiles(
        IEnumerable<string> inputFiles,
        int targetWidth,
        int targetHeight,
        IProgress<SpriteResizeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateTargetSize(targetWidth, targetHeight);

        var files = inputFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        var result = new ResizeResult { Total = files.Length };

        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputPath = files[index];
            var fileName = Path.GetFileName(inputPath);

            try
            {
                var outputPath = BuildOutputPath(inputPath, targetWidth, targetHeight);
                ResizeSingle(inputPath, outputPath, targetWidth, targetHeight, cancellationToken);
                result.Succeeded++;
                result.OutputFiles.Add(outputPath);
                progress?.Report(new SpriteResizeProgress(index + 1, files.Length, fileName, null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{fileName}: {ex.Message}");
                progress?.Report(new SpriteResizeProgress(index + 1, files.Length, fileName, ex.Message));
            }
        }

        return result;
    }

    public void ResizeSingle(
        string inputPath,
        string outputPath,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ValidateTargetSize(targetWidth, targetHeight);

        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Nie znaleziono pliku wejściowego.", inputPath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var source = Image.Load<Rgba32>(inputPath);

        var minX = source.Width;
        var minY = source.Height;
        var maxX = -1;
        var maxY = -1;

        source.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    ref var pixel = ref row[x];
                    if (IsExactMagenta(pixel))
                    {
                        pixel = new Rgba32(0, 0, 0, 0);
                    }

                    if (pixel.A == 0)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        });

        using var output = new Image<Rgba32>(targetWidth, targetHeight, new Rgba32(0, 0, 0, 0));
        if (maxX >= minX && maxY >= minY)
        {
            using var sprite = source.Clone(context => context.Crop(
                new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)));

            if (sprite.Width > targetWidth || sprite.Height > targetHeight)
            {
                var scale = Math.Min(
                    targetWidth / (double)sprite.Width,
                    targetHeight / (double)sprite.Height);
                var resizedWidth = Math.Max(1, (int)(sprite.Width * scale));
                var resizedHeight = Math.Max(1, (int)(sprite.Height * scale));
                sprite.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(resizedWidth, resizedHeight),
                    Mode = ResizeMode.Stretch,
                    Sampler = KnownResamplers.NearestNeighbor
                }));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var offsetX = (targetWidth - sprite.Width) / 2;
            var offsetY = (targetHeight - sprite.Height) / 2;
            output.Mutate(context => context.DrawImage(sprite, new Point(offsetX, offsetY), 1f));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        output.SaveAsPng(outputPath);
    }

    public static string BuildOutputPath(string inputPath, int targetWidth, int targetHeight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ValidateTargetSize(targetWidth, targetHeight);

        var fullPath = Path.GetFullPath(inputPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var baseName = Path.GetFileNameWithoutExtension(fullPath);
        return Path.Combine(directory, $"{baseName}_{targetWidth}x{targetHeight}_clean.png");
    }

    private static bool IsExactMagenta(Rgba32 pixel) =>
        pixel.R == 255 && pixel.G == 0 && pixel.B == 255;

    private static void ValidateTargetSize(int targetWidth, int targetHeight)
    {
        if (targetWidth < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetWidth), "Szerokość musi być większa od zera.");
        }

        if (targetHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetHeight), "Wysokość musi być większa od zera.");
        }
    }
}

public sealed record SpriteResizeProgress(
    int Completed,
    int Total,
    string FileName,
    string? ErrorMessage);

public sealed class ResizeResult
{
    public int Total { get; init; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<string> OutputFiles { get; } = [];
    public List<string> Errors { get; } = [];
}
