using FluentAssertions;
using Narzedzia.Core.Appearances;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Tests;

public sealed class AppearanceLayoutInfoTests
{
    [Fact]
    public void FromSpriteInfo_UsesLegacyStyleFieldsWhenNewFieldsAreMissing()
    {
        var info = new SpriteInfo
        {
            PatternWidth = 4,
            PatternHeight = 2,
            PatternDepth = 1,
            Layers = 2,
            PatternFrames = 3
        };

        var layout = AppearanceLayoutInfo.From(info);

        layout.PatternX.Should().Be(4);
        layout.PatternY.Should().Be(2);
        layout.PatternZ.Should().Be(1);
        layout.Layers.Should().Be(2);
        layout.Frames.Should().Be(3);
    }

    [Fact]
    public void FromSpriteInfo_UsesExplicitNewFieldsWhenPresent()
    {
        var info = new SpriteInfo
        {
            PatternWidth = 2,
            PatternHeight = 2,
            PatternLayers = 1,
            PatternX = 4,
            PatternY = 3,
            PatternZ = 1,
            PatternFrames = 5
        };

        var layout = AppearanceLayoutInfo.From(info);

        layout.TileWidth.Should().Be(2);
        layout.TileHeight.Should().Be(2);
        layout.PatternX.Should().Be(4);
        layout.PatternY.Should().Be(3);
        layout.PatternZ.Should().Be(1);
        layout.Frames.Should().Be(5);
    }
}
