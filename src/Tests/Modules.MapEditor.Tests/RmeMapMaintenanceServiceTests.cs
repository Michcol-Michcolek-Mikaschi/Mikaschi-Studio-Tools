using FluentAssertions;
using Modules.MapEditor.Services;
using Narzedzia.Core.Models;

namespace Modules.MapEditor.Tests;

public sealed class RmeMapMaintenanceServiceTests
{
    [Fact]
    public void FindUnreachableTiles_MatchesRmeClientViewRange()
    {
        var map = new OtbmMap();
        Add(map, 100, 100, 7, blocking: false);
        Add(map, 110, 108, 7, blocking: true);
        Add(map, 111, 108, 7, blocking: true);

        var result = RmeMapMaintenanceService.FindUnreachableTiles(
            map, tile => tile.Flags == 1);

        result.Should().NotContain(new OtbmTileCoord(110, 108, 7),
            "RME widzi osiągalne pole włącznie z granicą 10×8");
        result.Should().Contain(new OtbmTileCoord(111, 108, 7));
    }

    [Fact]
    public void RemoveSimpleItems_LeavesComplexCorpseNodesUntouched()
    {
        var items = new List<OtbmItem>
        {
            new() { Id = 300 },
            new() { Id = 300, UniqueId = 1000 },
            new() { Id = 301 }
        };

        RmeMapMaintenanceService.RemoveSimpleItems(items, new HashSet<ushort> { 300 })
            .Should().Be(1);
        items.Should().HaveCount(2);
        items.Should().ContainSingle(item => item.Id == 300 && item.UniqueId == 1000);
    }

    private static void Add(OtbmMap map, ushort x, ushort y, byte z, bool blocking)
    {
        map.Tiles[new(x, y, z)] = new OtbmTile
        {
            X = x,
            Y = y,
            Z = z,
            GroundItemId = 100,
            Flags = blocking ? 1u : 0u
        };
    }
}
