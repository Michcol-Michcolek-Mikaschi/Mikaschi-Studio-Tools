using FluentAssertions;
using Modules.SpriteResizer.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.SpriteTools.Tests;

public class SpriteResizerServiceTests
{
    private static string CreateTempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CreateTestPng(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        // Make half the pixels magenta
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height / 2; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = new Rgba32(255, 0, 255, 255);
            }
        });
        image.SaveAsPng(path);
    }

    [Fact]
    public void ResizeSingle_OutputHasCorrectDimensions()
    {
        var inputDir  = CreateTempFolder();
        var outputDir = CreateTempFolder();
        try
        {
            var inputPath  = Path.Combine(inputDir, "test.png");
            var outputPath = Path.Combine(outputDir, "test.png");

            CreateTestPng(inputPath, 32, 32);

            var service = new SpriteResizerService();
            service.ResizeSingle(inputPath, outputPath, 64, 64);

            using var result = Image.Load<Rgba32>(outputPath);
            result.Width.Should().Be(64);
            result.Height.Should().Be(64);
        }
        finally
        {
            Directory.Delete(inputDir, true);
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ResizeBatch_ProcessesAllPngs()
    {
        var inputDir  = CreateTempFolder();
        var outputDir = CreateTempFolder();
        try
        {
            CreateTestPng(Path.Combine(inputDir, "a.png"), 32, 32);
            CreateTestPng(Path.Combine(inputDir, "b.png"), 32, 32);

            var service = new SpriteResizerService();
            var result  = service.ResizeBatch(inputDir, outputDir, 64, 64);

            result.Total.Should().Be(2);
            result.Succeeded.Should().Be(2);
            result.Failed.Should().Be(0);
            Directory.GetFiles(outputDir, "*.png").Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(inputDir, true);
            Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public void ResizeSingle_MagentaPixels_ArePreserved()
    {
        var inputDir  = CreateTempFolder();
        var outputDir = CreateTempFolder();
        try
        {
            var inputPath  = Path.Combine(inputDir, "test.png");
            var outputPath = Path.Combine(outputDir, "test.png");

            // Create a fully-magenta image
            using (var image = new Image<Rgba32>(32, 32))
            {
                image.ProcessPixelRows(acc =>
                {
                    for (int y = 0; y < acc.Height; y++)
                    {
                        var row = acc.GetRowSpan(y);
                        for (int x = 0; x < row.Length; x++)
                            row[x] = SpriteResizerService.MagentaColor;
                    }
                });
                image.SaveAsPng(inputPath);
            }

            var service = new SpriteResizerService();
            service.ResizeSingle(inputPath, outputPath, 64, 64);

            using var result = Image.Load<Rgba32>(outputPath);
            // Corner pixel should still be magenta
            var pixel = result[0, 0];
            pixel.R.Should().Be(255);
            pixel.G.Should().Be(0);
            pixel.B.Should().Be(255);
        }
        finally
        {
            Directory.Delete(inputDir, true);
            Directory.Delete(outputDir, true);
        }
    }
}
