using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.Tests;

public sealed class AnimationGifExporterTests
{
    [Fact]
    public void Export_WritesMultiFrameGifWithDelaysFromSpritePhase()
    {
        // ── Arrange ──
        var spriteInfo = new SpriteInfo
        {
            PatternWidth = 1, PatternHeight = 1, PatternDepth = 1, Layers = 1,
        };
        spriteInfo.SpriteId.Add(new uint[] { 1, 2, 3, 4 });
        spriteInfo.Animation = new SpriteAnimation
        {
            LoopType = ANIMATION_LOOP_TYPE.Infinite,
            SpritePhase =
            {
                new SpritePhase { DurationMin = 100, DurationMax = 100 },
                new SpritePhase { DurationMin = 200, DurationMax = 200 },
                new SpritePhase { DurationMin = 150, DurationMax = 150 },
                new SpritePhase { DurationMin = 120, DurationMax = 120 },
            },
        };
        var appearance = new Appearance
        {
            Id = 1,
            AppearanceType = APPEARANCE_TYPE.AppearanceObject,
            FrameGroup = { new FrameGroup { SpriteInfo = spriteInfo } },
        };

        var assets = Substitute.For<IAssetsService>();
        var pixels = new byte[32 * 32 * 4];
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        assets.RenderAppearance(Arg.Any<Appearance>(), Arg.Any<AppearanceRenderOptions>())
              .Returns(new RenderedSpriteImage(pixels, 32, 32));

        var exporter = new AnimationGifExporter(assets);
        var outputPath = Path.Combine(Path.GetTempPath(), $"narzedzia-test-{Guid.NewGuid():N}.gif");
        var options = new AppearanceRenderOptions(GroupIndex: 0, Direction: 0, Addon: 0, PatternZ: 0, Frame: 0,
                                                   BlendLayers: false, FullAddons: false);

        try
        {
            // ── Act ──
            var report = exporter.Export(appearance, options, outputPath);

            // ── Assert ──
            report.FramesWritten.Should().Be(4);
            File.Exists(outputPath).Should().BeTrue();

            using var loaded = Image.Load<Bgra32>(outputPath);
            loaded.Frames.Count.Should().Be(4, "GIF musi zawierać 4 klatki z animacji");
            loaded.Width.Should().Be(32);

            // Frame 0 delay = 100ms → 10 hundredths
            loaded.Frames[0].Metadata.GetGifMetadata().FrameDelay.Should().Be(10);
            // Frame 1 delay = 200ms → 20 hundredths
            loaded.Frames[1].Metadata.GetGifMetadata().FrameDelay.Should().Be(20);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
