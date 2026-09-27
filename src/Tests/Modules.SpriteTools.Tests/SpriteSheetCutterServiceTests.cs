using FluentAssertions;
using Modules.SpriteSheetCutter.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.SpriteTools.Tests;

public class SpriteSheetCutterServiceTests
{
    private static string CreateTempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CreateTestSheet(string path, int totalWidth, int totalHeight)
    {
        using var image = new Image<Rgba32>(totalWidth, totalHeight);
        image.SaveAsPng(path);
    }

    [Fact]
    public void CutSingle_2x2Sheet_Produces4Frames()
    {
        var inputDir  = CreateTempFolder();
        var outputDir = CreateTempFolder();
        try
        {
            var inputPath = Path.Combine(inputDir, "sheet.png");
            CreateTestSheet(inputPath, 64, 64); // 2×2 of 32×32

            var service = new SpriteSheetCutterService();
            int count   = service.CutSingle(inputPath, outputDir, "sheet", 32, 32);

            count.Should().Be(4);
            Directory.GetFiles(outputDir, "*.png").Should().HaveCount(4);
        }
        finally
        {
            Directory.Delete(inputDir, true);
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void CutSingle_FrameHasCorrectDimensions()
    {
        var inputDir  = CreateTempFolder();
        var outputDir = CreateTempFolder();
        try
        {
            var inputPath = Path.Combine(inputDir, "sheet.png");
            CreateTestSheet(inputPath, 96, 32); // 3×1 of 32×32

            var service = new SpriteSheetCutterService();
            service.CutSingle(inputPath, outputDir, "sheet", 32, 32);

            var framePath = Path.Combine(outputDir, "sheet_0_0.png");
            File.Exists(framePath).Should().BeTrue();
            using var frame = Image.Load<Rgba32>(framePath);
            frame.Width.Should().Be(32);
            frame.Height.Should().Be(32);
        }
        finally
        {
            Directory.Delete(inputDir, true);
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void CutBatch_MultipleSheetsProcessed()
    {
        var inputDir  = CreateTempFolder();
        var outputDir = CreateTempFolder();
        try
        {
            CreateTestSheet(Path.Combine(inputDir, "s1.png"), 64, 32);
            CreateTestSheet(Path.Combine(inputDir, "s2.png"), 96, 32);

            var service = new SpriteSheetCutterService();
            var result  = service.CutBatch(inputDir, outputDir, 32, 32);

            result.TotalFiles.Should().Be(2);
            result.SucceededFiles.Should().Be(2);
            result.TotalFrames.Should().Be(5); // 2 + 3
        }
        finally
        {
            Directory.Delete(inputDir, true);
            Directory.Delete(outputDir, true);
        }
    }
}
