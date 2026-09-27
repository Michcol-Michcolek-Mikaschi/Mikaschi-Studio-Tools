using FluentAssertions;
using Narzedzia.Core.Assets;

namespace Narzedzia.Core.Tests;

public sealed class SpriteSheetLayoutTests
{
    [Theory]
    [InlineData(0, 32, 32, 12, 12, 144)]
    [InlineData(3, 64, 64, 6, 6, 36)]
    [InlineData(35, 384, 384, 1, 1, 1)]
    public void FromSpriteType_ReturnsExpectedSheetGrid(int type, int w, int h, int cols, int rows, int total)
    {
        var layout = SpriteSheetLayout.FromSpriteType(type);

        layout.TileWidth.Should().Be(w);
        layout.TileHeight.Should().Be(h);
        layout.Columns.Should().Be(cols);
        layout.Rows.Should().Be(rows);
        layout.TileCount.Should().Be(total);
    }

    [Fact]
    public void TryFromDimensions_AcceptsEveryLayoutSupportedByOtclient()
    {
        for (var type = 0; type < SpriteSheetLayout.SupportedTypeCount; type++)
        {
            var expected = SpriteSheetLayout.FromSpriteType(type);

            SpriteSheetLayout.TryFromDimensions(
                    expected.TileWidth,
                    expected.TileHeight,
                    out var actual)
                .Should().BeTrue();
            actual.SpriteType.Should().Be(type);
        }
    }

    [Theory]
    [InlineData(17, 17)]
    [InlineData(32, 48)]
    [InlineData(768, 64)]
    [InlineData(384, 385)]
    public void TryFromDimensions_RejectsSizesOutsideOtclientLayouts(int width, int height)
    {
        SpriteSheetLayout.TryFromDimensions(width, height, out _).Should().BeFalse();
    }
}
