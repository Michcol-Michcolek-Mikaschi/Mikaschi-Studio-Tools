using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.SpriteResizer.Services;

/// <summary>
/// Resizes PNG sprite images while preserving magenta (255,0,255) transparency.
/// Replicates the Python sprite_resizer.py behavior in C#.
/// </summary>
public class SpriteResizerService
{
    /// <summary>
    /// The magenta color used as transparent color in Tibia sprites.
    /// </summary>
    public static readonly Rgba32 MagentaColor = new(255, 0, 255, 255);

    /// <summary>
    /// Resizes all PNG files in the input folder to the target size and saves them to the output folder.
    /// </summary>
    public ResizeResult ResizeBatch(string inputFolder, string outputFolder, int targetWidth, int targetHeight, IProgress<string>? progress = null)
    {
        if (!Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException($"Input folder not found: {inputFolder}");

        Directory.CreateDirectory(outputFolder);

        var files = Directory.GetFiles(inputFolder, "*.png", SearchOption.TopDirectoryOnly);
        var result = new ResizeResult { Total = files.Length };

        foreach (var file in files)
        {
            try
            {
                var outputPath = Path.Combine(outputFolder, Path.GetFileName(file));
                ResizeSingle(file, outputPath, targetWidth, targetHeight);
                result.Succeeded++;
                progress?.Report($"Przetworzono: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                progress?.Report($"Błąd: {Path.GetFileName(file)} — {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Resizes a single sprite PNG, converting magenta pixels to fully transparent before resize,
    /// then restoring magenta after resize.
    /// </summary>
    public void ResizeSingle(string inputPath, string outputPath, int targetWidth, int targetHeight)
    {
        using var image = Image.Load<Rgba32>(inputPath);

        // Convert magenta → transparent
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (IsMagenta(row[x]))
                        row[x] = new Rgba32(0, 0, 0, 0);
                }
            }
        });

        // Resize with NearestNeighbor to preserve pixel art look
        image.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(targetWidth, targetHeight),
            Mode = ResizeMode.Stretch,
            Sampler = KnownResamplers.NearestNeighbor
        }));

        // Convert transparent back → magenta
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0)
                        row[x] = MagentaColor;
                }
            }
        });

        image.SaveAsPng(outputPath);
    }

    private static bool IsMagenta(Rgba32 pixel) =>
        pixel.R == 255 && pixel.G == 0 && pixel.B == 255;
}

public class ResizeResult
{
    public int Total { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; } = new();
}
