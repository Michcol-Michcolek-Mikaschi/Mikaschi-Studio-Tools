using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.SpriteSheetCutter.Services;

/// <summary>
/// Cuts sprite sheets into individual sprite frames.
/// Replicates the Python sprite_sheet_cutter.py behavior.
/// </summary>
public class SpriteSheetCutterService
{
    /// <summary>
    /// Cuts all PNG sprite sheets in the input folder into individual frames.
    /// </summary>
    public CutResult CutBatch(
        string inputFolder,
        string outputFolder,
        int spriteWidth,
        int spriteHeight,
        string filenameSuffix = "",
        IProgress<string>? progress = null)
    {
        if (!Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException($"Input folder not found: {inputFolder}");

        Directory.CreateDirectory(outputFolder);

        var files = Directory.GetFiles(inputFolder, "*.png", SearchOption.TopDirectoryOnly);
        var result = new CutResult { TotalFiles = files.Length };

        foreach (var file in files)
        {
            try
            {
                var baseName = Path.GetFileNameWithoutExtension(file);
                var count = CutSingle(file, outputFolder, baseName, spriteWidth, spriteHeight);
                result.TotalFrames += count;
                result.SucceededFiles++;
                progress?.Report($"Wycięto {count} klatek z: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                result.FailedFiles++;
                result.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                progress?.Report($"Błąd: {Path.GetFileName(file)} — {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Cuts a single sprite sheet into frames saved as {baseName}_{row}_{col}.png.
    /// Returns the count of frames saved.
    /// </summary>
    public int CutSingle(string inputPath, string outputFolder, string baseName, int spriteWidth, int spriteHeight)
    {
        using var sheet = Image.Load<Rgba32>(inputPath);

        int cols = sheet.Width  / spriteWidth;
        int rows = sheet.Height / spriteHeight;

        if (cols == 0 || rows == 0)
            throw new InvalidOperationException(
                $"Image {Path.GetFileName(inputPath)} ({sheet.Width}×{sheet.Height}) is smaller than sprite size ({spriteWidth}×{spriteHeight}).");

        int count = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                var bounds = new Rectangle(col * spriteWidth, row * spriteHeight, spriteWidth, spriteHeight);
                using var frame = sheet.Clone(ctx => ctx.Crop(bounds));
                var outputPath = Path.Combine(outputFolder, $"{baseName}_{row}_{col}.png");
                frame.SaveAsPng(outputPath);
                count++;
            }
        }
        return count;
    }
}

public class CutResult
{
    public int TotalFiles { get; set; }
    public int SucceededFiles { get; set; }
    public int FailedFiles { get; set; }
    public int TotalFrames { get; set; }
    public List<string> Errors { get; } = new();
}
