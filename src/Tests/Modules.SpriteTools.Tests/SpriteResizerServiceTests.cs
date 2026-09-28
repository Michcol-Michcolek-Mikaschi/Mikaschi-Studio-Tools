using FluentAssertions;
using Modules.SpriteResizer.Services;
using Modules.SpriteResizer.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.SpriteTools.Tests;

public sealed class SpriteResizerServiceTests
{
    [Fact]
    public void ViewModel_AddInputFiles_AddsManyPngFilesAndRejectsDuplicatesAndOtherFormats()
    {
        using var temp = new TemporaryFolder();
        var first = temp.PathFor("first.png");
        var second = temp.PathFor("SECOND.PNG");
        var text = temp.PathFor("notes.txt");
        CreateSolidPng(first, 1, 1, new Rgba32(1, 2, 3, 255));
        CreateSolidPng(second, 1, 1, new Rgba32(4, 5, 6, 255));
        File.WriteAllText(text, "not a sprite");
        var viewModel = new SpriteResizerViewModel();

        var firstUpdate = viewModel.AddInputFiles([first, second, first, text, temp.PathFor("missing.png")]);
        var secondUpdate = viewModel.AddInputFiles([second]);

        firstUpdate.Should().Be(new SpriteInputFilesUpdate(2, 1, 2));
        secondUpdate.Should().Be(new SpriteInputFilesUpdate(0, 1, 0));
        viewModel.InputFiles.Select(file => file.FullPath).Should().Equal(
            Path.GetFullPath(first),
            Path.GetFullPath(second));
    }

    [Fact]
    public void ViewModel_SetInputFiles_ReplacesSelectionWithUniqueExistingPngFiles()
    {
        using var temp = new TemporaryFolder();
        var first = temp.PathFor("first.png");
        var second = temp.PathFor("second.png");
        var text = temp.PathFor("notes.txt");
        CreateSolidPng(first, 1, 1, new Rgba32(1, 2, 3, 255));
        CreateSolidPng(second, 1, 1, new Rgba32(4, 5, 6, 255));
        File.WriteAllText(text, "not a sprite");
        var viewModel = new SpriteResizerViewModel();
        viewModel.SetInputFiles([first]);

        viewModel.SetInputFiles([second, second, text]);

        viewModel.InputFiles.Should().ContainSingle();
        viewModel.InputFiles[0].FullPath.Should().Be(Path.GetFullPath(second));
    }

    [Fact]
    public void ResizeSingle_RemovesExactMagenta_CropsAndCentersWithoutUpscaling()
    {
        using var temp = new TemporaryFolder();
        var inputPath = temp.PathFor("source.png");
        var outputPath = temp.PathFor("result.png");
        var blue = new Rgba32(10, 80, 220, 255);

        using (var source = new Image<Rgba32>(10, 8, SpriteResizerService.MagentaColor))
        {
            for (var y = 2; y <= 3; y++)
            {
                for (var x = 3; x <= 6; x++)
                {
                    source[x, y] = blue;
                }
            }

            source.SaveAsPng(inputPath);
        }

        new SpriteResizerService().ResizeSingle(inputPath, outputPath, 8, 8);

        using var result = Image.Load<Rgba32>(outputPath);
        result.Size.Should().Be(new Size(8, 8));
        result[2, 3].Should().Be(blue);
        result[5, 4].Should().Be(blue);
        result[1, 3].A.Should().Be(0);
        result[6, 4].A.Should().Be(0);
        EnumeratePixels(result).Should().NotContain(pixel =>
            pixel.R == 255 && pixel.G == 0 && pixel.B == 255);
    }

    [Fact]
    public void ResizeSingle_DownscalesProportionallyWithNearestNeighbor()
    {
        using var temp = new TemporaryFolder();
        var inputPath = temp.PathFor("wide.png");
        var outputPath = temp.PathFor("wide_6x6_clean.png");
        var red = new Rgba32(200, 25, 10, 255);

        using (var source = new Image<Rgba32>(12, 6, red))
        {
            source.SaveAsPng(inputPath);
        }

        new SpriteResizerService().ResizeSingle(inputPath, outputPath, 6, 6);

        using var result = Image.Load<Rgba32>(outputPath);
        result.Size.Should().Be(new Size(6, 6));
        for (var x = 0; x < 6; x++)
        {
            result[x, 0].A.Should().Be(0);
            result[x, 1].Should().Be(red);
            result[x, 3].Should().Be(red);
            result[x, 4].A.Should().Be(0);
        }
    }

    [Fact]
    public void ResizeSingle_DoesNotEnlargeSmallSprite()
    {
        using var temp = new TemporaryFolder();
        var inputPath = temp.PathFor("small.png");
        var outputPath = temp.PathFor("small_8x8_clean.png");
        var green = new Rgba32(15, 210, 70, 255);

        using (var source = new Image<Rgba32>(2, 2, green))
        {
            source.SaveAsPng(inputPath);
        }

        new SpriteResizerService().ResizeSingle(inputPath, outputPath, 8, 8);

        using var result = Image.Load<Rgba32>(outputPath);
        result[3, 3].Should().Be(green);
        result[4, 4].Should().Be(green);
        result[2, 3].A.Should().Be(0);
        result[5, 4].A.Should().Be(0);
        EnumeratePixels(result).Count(pixel => pixel.A > 0).Should().Be(4);
    }

    [Fact]
    public void ResizeSingle_FullyMagentaSource_ProducesTransparentCanvas()
    {
        using var temp = new TemporaryFolder();
        var inputPath = temp.PathFor("magenta.png");
        var outputPath = temp.PathFor("magenta_7x5_clean.png");

        using (var source = new Image<Rgba32>(3, 4, SpriteResizerService.MagentaColor))
        {
            source.SaveAsPng(inputPath);
        }

        new SpriteResizerService().ResizeSingle(inputPath, outputPath, 7, 5);

        using var result = Image.Load<Rgba32>(outputPath);
        result.Size.Should().Be(new Size(7, 5));
        EnumeratePixels(result).Should().OnlyContain(pixel => pixel.A == 0);
    }

    [Fact]
    public void ResizeFiles_PreservesSelectionOrderAndWritesBesideEachSource()
    {
        using var temp = new TemporaryFolder();
        var first = temp.PathFor("first.png");
        var second = temp.PathFor("second.png");
        CreateSolidPng(first, 2, 2, new Rgba32(1, 2, 3, 255));
        CreateSolidPng(second, 3, 3, new Rgba32(4, 5, 6, 255));
        var progressEvents = new List<SpriteResizeProgress>();

        var result = new SpriteResizerService().ResizeFiles(
            [second, first],
            64,
            32,
            new SynchronousProgress<SpriteResizeProgress>(progressEvents.Add));

        result.Total.Should().Be(2);
        result.Succeeded.Should().Be(2);
        result.Failed.Should().Be(0);
        result.OutputFiles.Should().Equal(
            temp.PathFor("second_64x32_clean.png"),
            temp.PathFor("first_64x32_clean.png"));
        result.OutputFiles.Should().OnlyContain(path => File.Exists(path));
        progressEvents.Select(update => update.FileName).Should().Equal("second.png", "first.png");
    }

    [Fact]
    public void ResizeFiles_ReportsBadFileWithoutStoppingRemainingFiles()
    {
        using var temp = new TemporaryFolder();
        var valid = temp.PathFor("valid.png");
        CreateSolidPng(valid, 1, 1, new Rgba32(1, 2, 3, 255));

        var result = new SpriteResizerService().ResizeFiles(
            [temp.PathFor("missing.png"), valid],
            32,
            32);

        result.Total.Should().Be(2);
        result.Succeeded.Should().Be(1);
        result.Failed.Should().Be(1);
        result.Errors.Should().ContainSingle(message => message.StartsWith("missing.png:", StringComparison.Ordinal));
        File.Exists(temp.PathFor("valid_32x32_clean.png")).Should().BeTrue();
    }

    [Fact]
    public void ResizeFiles_PreCancelledToken_StopsBeforeWriting()
    {
        using var temp = new TemporaryFolder();
        var input = temp.PathFor("source.png");
        CreateSolidPng(input, 2, 2, new Rgba32(1, 2, 3, 255));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => new SpriteResizerService().ResizeFiles(
            [input],
            32,
            32,
            cancellationToken: cancellation.Token);

        action.Should().Throw<OperationCanceledException>();
        File.Exists(temp.PathFor("source_32x32_clean.png")).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 32)]
    [InlineData(32, 0)]
    [InlineData(-1, 32)]
    public void ResizeFiles_InvalidTargetSize_IsRejected(int width, int height)
    {
        var action = () => new SpriteResizerService().ResizeFiles([], width, height);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static void CreateSolidPng(string path, int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        image.SaveAsPng(path);
    }

    private static IEnumerable<Rgba32> EnumeratePixels(Image<Rgba32> image)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                yield return image[x, y];
            }
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryFolder() => Directory.CreateDirectory(_path);

        public string PathFor(string name) => Path.Combine(_path, name);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
