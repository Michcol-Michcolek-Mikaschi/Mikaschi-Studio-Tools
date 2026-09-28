using FluentAssertions;
using Modules.SpriteSheetCutter.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.SpriteTools.Tests;

public sealed class SpriteSheetCutterServiceTests
{
    [Fact]
    public void PresetCatalog_MatchesTheOriginal294EntryTable()
    {
        var presets = SpriteSizePresetCatalog.All;

        presets.Should().HaveCount(294);
        presets.Select(preset => preset.Label).Should().OnlyHaveUniqueItems();
        presets[0].Should().Be(new SpriteSizePreset("32x32 (1×1)", 32, 32));
        presets[14].Should().Be(new SpriteSizePreset("32x512 (1×16)", 32, 512));
        presets[15].Should().Be(new SpriteSizePreset("64x32 (2×1)", 64, 32));
        presets[209].Should().Be(new SpriteSizePreset("448x512 (14×16)", 448, 512));
        presets[210].Should().Be(new SpriteSizePreset("480x32 (15×1)", 480, 32));
        presets[225].Should().Be(new SpriteSizePreset("480x512 (15×16)", 480, 512));
        presets[226].Should().Be(new SpriteSizePreset("512x32 (16×1)", 512, 32));
        presets[240].Should().Be(new SpriteSizePreset("512x512 (16×16)", 512, 512));
        presets[241].Should().Be(new SpriteSizePreset("544x544 (17×17)", 544, 544));
        presets[258].Should().Be(new SpriteSizePreset("1088x1088 (34×34)", 1088, 1088));
        presets[259].Should().Be(new SpriteSizePreset("1120x32 (35×1)", 1120, 32));
        presets[^1].Should().Be(new SpriteSizePreset("1120x1120 (35×35)", 1120, 1120));
        SpriteSizePresetCatalog.Default.Should().Be(new SpriteSizePreset("64x64 (2×2)", 64, 64));
    }

    [Fact]
    public void CutSingle_UsesRowMajorNumberingAndPadsPartialRightAndBottomFrames()
    {
        using var temp = new TemporaryFolder();
        var inputPath = temp.PathFor("sheet.png");
        using (var sheet = new Image<Rgba32>(3, 3))
        {
            for (var y = 0; y < sheet.Height; y++)
            {
                for (var x = 0; x < sheet.Width; x++)
                {
                    sheet[x, y] = new Rgba32((byte)(10 + x), (byte)(20 + y), (byte)(30 + x + y), 255);
                }
            }

            sheet.SaveAsPng(inputPath);
        }

        var result = new SpriteSheetCutterService().CutSingle(inputPath, 2, 2);

        result.Columns.Should().Be(2);
        result.Rows.Should().Be(2);
        result.TotalFrames.Should().Be(4);
        result.OutputFolder.Should().Be(temp.PathFor("sliced_sprites"));
        result.OutputFiles.Select(Path.GetFileName).Should().Equal(
            "sprite_000.png",
            "sprite_001.png",
            "sprite_002.png",
            "sprite_003.png");

        using var topLeft = Image.Load<Rgba32>(result.OutputFiles[0]);
        using var topRight = Image.Load<Rgba32>(result.OutputFiles[1]);
        using var bottomLeft = Image.Load<Rgba32>(result.OutputFiles[2]);
        using var bottomRight = Image.Load<Rgba32>(result.OutputFiles[3]);
        topLeft[0, 0].Should().Be(new Rgba32(10, 20, 30, 255));
        topLeft[1, 1].Should().Be(new Rgba32(11, 21, 32, 255));
        topRight[0, 0].Should().Be(new Rgba32(12, 20, 32, 255));
        topRight[1, 0].A.Should().Be(0);
        bottomLeft[0, 0].Should().Be(new Rgba32(10, 22, 32, 255));
        bottomLeft[0, 1].A.Should().Be(0);
        bottomRight[0, 0].Should().Be(new Rgba32(12, 22, 34, 255));
        bottomRight[1, 1].A.Should().Be(0);
    }

    [Fact]
    public void CutSingle_TileLargerThanSheet_StillWritesOnePaddedFrame()
    {
        using var temp = new TemporaryFolder();
        var inputPath = temp.PathFor("tiny.png");
        CreateSolidPng(inputPath, 2, 1, new Rgba32(30, 40, 50, 255));

        var result = new SpriteSheetCutterService().CutSingle(inputPath, 32, 32);

        result.TotalFrames.Should().Be(1);
        using var frame = Image.Load<Rgba32>(result.OutputFiles.Single());
        frame.Size.Should().Be(new Size(32, 32));
        frame[0, 0].Should().Be(new Rgba32(30, 40, 50, 255));
        frame[2, 0].A.Should().Be(0);
        frame[0, 1].A.Should().Be(0);
    }

    [Fact]
    public void CutSingle_ReportsProgressAndOverwritesOriginalNumberedNames()
    {
        using var temp = new TemporaryFolder();
        var inputPath = temp.PathFor("sheet.png");
        CreateSolidPng(inputPath, 6, 2, new Rgba32(1, 2, 3, 255));
        var updates = new List<SpriteCutProgress>();

        var result = new SpriteSheetCutterService().CutSingle(
            inputPath,
            2,
            2,
            new SynchronousProgress<SpriteCutProgress>(updates.Add));

        result.TotalFrames.Should().Be(3);
        updates.Should().HaveCount(3);
        updates.Select(update => update.Completed).Should().Equal(1, 2, 3);
        updates[^1].Total.Should().Be(3);
        result.OutputFiles.Should().OnlyContain(path => File.Exists(path));
    }

    [Fact]
    public void CreatePreview_DrawsTwoPixelRedGridAndReportsActualPaddedFrameCount()
    {
        using var temp = new TemporaryFolder();
        var inputPath = temp.PathFor("preview.png");
        CreateSolidPng(inputPath, 10, 10, new Rgba32(255, 255, 255, 255));

        var preview = new SpriteSheetCutterService().CreatePreview(inputPath, 4, 4);

        preview.SourceWidth.Should().Be(10);
        preview.SourceHeight.Should().Be(10);
        preview.PreviewWidth.Should().Be(10);
        preview.PreviewHeight.Should().Be(10);
        preview.Columns.Should().Be(3);
        preview.Rows.Should().Be(3);
        preview.FrameCount.Should().Be(9);

        using var image = Image.Load<Rgba32>(preview.PngData);
        image[3, 3].Should().Be(new Rgba32(255, 255, 255, 255));
        image[4, 1].Should().Be(new Rgba32(255, 0, 0, 255));
        image[5, 1].Should().Be(new Rgba32(255, 0, 0, 255));
        image[1, 4].Should().Be(new Rgba32(255, 0, 0, 255));
        image[1, 5].Should().Be(new Rgba32(255, 0, 0, 255));
        image[8, 8].Should().Be(new Rgba32(255, 0, 0, 255));
    }

    [Fact]
    public void CreatePreview_UsesLanczosScaleWithin800By600AndNeverUpscales()
    {
        using var temp = new TemporaryFolder();
        var largePath = temp.PathFor("large.png");
        var smallPath = temp.PathFor("small.png");
        CreateSolidPng(largePath, 1600, 1200, new Rgba32(1, 2, 3, 255));
        CreateSolidPng(smallPath, 200, 100, new Rgba32(1, 2, 3, 255));
        var service = new SpriteSheetCutterService();

        var large = service.CreatePreview(largePath, 64, 64);
        var small = service.CreatePreview(smallPath, 32, 32);

        (large.PreviewWidth, large.PreviewHeight).Should().Be((800, 600));
        (small.PreviewWidth, small.PreviewHeight).Should().Be((200, 100));
    }

    [Fact]
    public void CutSingle_PreCancelledToken_DoesNotCreateOutputFolder()
    {
        using var temp = new TemporaryFolder();
        var inputPath = temp.PathFor("sheet.png");
        CreateSolidPng(inputPath, 64, 64, new Rgba32(1, 2, 3, 255));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => new SpriteSheetCutterService().CutSingle(
            inputPath,
            32,
            32,
            cancellationToken: cancellation.Token);

        action.Should().Throw<OperationCanceledException>();
        Directory.Exists(temp.PathFor("sliced_sprites")).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 32)]
    [InlineData(32, 0)]
    [InlineData(-1, 32)]
    public void CutSingle_InvalidSpriteSize_IsRejected(int width, int height)
    {
        var action = () => new SpriteSheetCutterService().CutSingle("missing.png", width, height);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static void CreateSolidPng(string path, int width, int height, Rgba32 color)
    {
        using var image = new Image<Rgba32>(width, height, color);
        image.SaveAsPng(path);
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
