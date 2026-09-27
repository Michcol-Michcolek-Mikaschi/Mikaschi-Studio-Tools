using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Tibia12;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.Tests;

public sealed class MissileSheetExporterTests
{
    [Fact]
    public void Export_Produces3x3Sheet_With8DirectionsAndEmptyCenter()
    {
        var spriteInfo = new SpriteInfo
        {
            PatternWidth = 3, PatternHeight = 3, PatternDepth = 1, Layers = 1,
        };
        spriteInfo.SpriteId.Add(Enumerable.Range(1, 9).Select(i => (uint)i));
        var missile = new Appearance
        {
            Id = 1,
            AppearanceType = APPEARANCE_TYPE.AppearanceMissile,
            FrameGroup = { new FrameGroup { SpriteInfo = spriteInfo } },
        };

        var assets = Substitute.For<IAssetsService>();
        // Pixele: 32x32 fully opaque biały — tylko żeby cell był "non-empty"
        var whitePixels = new byte[32 * 32 * 4];
        for (var i = 0; i < whitePixels.Length; i += 4)
        {
            whitePixels[i]     = 255; // B
            whitePixels[i + 1] = 255; // G
            whitePixels[i + 2] = 255; // R
            whitePixels[i + 3] = 255; // A
        }
        assets.RenderAppearance(Arg.Any<Appearance>(), Arg.Any<AppearanceRenderOptions>())
              .Returns(new RenderedSpriteImage(whitePixels, 32, 32));

        var exporter = new MissileSheetExporter(assets);
        var outputPath = Path.Combine(Path.GetTempPath(), $"missile-{Guid.NewGuid():N}.png");

        try
        {
            var report = exporter.Export(missile, outputPath);

            report.DirectionalsRendered.Should().Be(8, "musi wyrenderować 8 kierunków (centrum pomijane)");
            report.Width.Should().Be(96);  // 3 * 32
            report.Height.Should().Be(96);

            using var sheet = Image.Load<Bgra32>(outputPath);
            sheet.Width.Should().Be(96);
            sheet.Height.Should().Be(96);

            // Centrum (1,1) musi pozostać puste — sprawdź pixel w środku
            sheet.ProcessPixelRows(accessor =>
            {
                var centerRow = accessor.GetRowSpan(48);
                centerRow[48].A.Should().Be(0, "centrum sheeta missile powinno być puste");
            });
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}
