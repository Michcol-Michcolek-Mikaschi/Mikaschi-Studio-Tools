using FluentAssertions;
using Modules.MapEditor.Services;

namespace Modules.MapEditor.Tests;

public sealed class RmeBrushPlacementServiceTests
{
    [Fact]
    public void AreaOffsets_MatchRmeSquareAndCircleGeometry()
    {
        RmeBrushPlacementService.GetAreaOffsets(1, RmeBrushShape.Square)
            .Should().HaveCount(9);
        RmeBrushPlacementService.GetAreaOffsets(1, RmeBrushShape.Circle)
            .Should().BeEquivalentTo([(0, 0), (-1, 0), (1, 0), (0, -1), (0, 1)]);
        RmeBrushPlacementService.GetAreaOffsets(2, RmeBrushShape.Circle)
            .Should().HaveCount(13);
    }

    [Fact]
    public void GroundBrush_FillsEveryTileInSelectedShape()
    {
        var brush = CreateBrush("ground", thickness: 0, ceiling: 0, oneSize: false);

        var placement = RmeBrushPlacementService.Create(
            brush, 0, 0, 1, RmeBrushShape.Square, new Random(1));

        placement.Tiles.Should().HaveCount(9);
        placement.Tiles.Select(tile => (tile.X, tile.Y)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void DoodadOneSize_IgnoresGlobalBrushSizeLikeRme()
    {
        var brush = CreateBrush("doodad", thickness: 100, ceiling: 100, oneSize: true);

        var placement = RmeBrushPlacementService.Create(
            brush, 0, 0, 11, RmeBrushShape.Square, new Random(1));

        placement.Tiles.Should().ContainSingle();
        placement.Tiles[0].X.Should().Be(0);
        placement.Tiles[0].Y.Should().Be(0);
    }

    [Fact]
    public void DoodadDensity_UsesRmeThicknessAndNeverOverlapsGeneratedObjects()
    {
        var brush = CreateBrush("doodad", thickness: 100, ceiling: 100, oneSize: false);

        var placement = RmeBrushPlacementService.Create(
            brush, 0, 0, 2, RmeBrushShape.Square, new Random(7));

        placement.Tiles.Should().NotBeEmpty();
        placement.Tiles.Should().HaveCountLessThanOrEqualTo(25);
        placement.Tiles.Select(tile => (tile.X, tile.Y, tile.Z)).Should().OnlyHaveUniqueItems();
        placement.Tiles.Should().OnlyContain(tile =>
            tile.X >= -2 && tile.X <= 2 && tile.Y >= -2 && tile.Y <= 2);
    }

    private static RmeBrushDefinition CreateBrush(string type, int thickness, int ceiling, bool oneSize) =>
        new("test", type, 200, oneSize, false, thickness, ceiling,
            [new RmeBrushAlternative([new RmeWeightedItem(200, 1)], [])]);
}
