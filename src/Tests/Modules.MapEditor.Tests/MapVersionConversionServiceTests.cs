using FluentAssertions;
using Modules.MapEditor.Services;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.MapEditor.Tests;

public sealed class MapVersionConversionServiceTests
{
    [Fact]
    public void AnalyzeAndApply_MapsGroundNestedItemsAndHeaderBySpriteHash()
    {
        var directory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(directory.FullName, "source.otb");
        var targetPath = Path.Combine(directory.FullName, "target.otb");
        var groundHash = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        var itemHash = Enumerable.Range(21, 16).Select(value => (byte)value).ToArray();
        SaveOtb(sourcePath, 57,
            Item(100, 500, OtbItemType.Ground, groundHash),
            Item(200, 600, OtbItemType.Container, itemHash));
        SaveOtb(targetPath, 74,
            Item(900, 1500, OtbItemType.Ground, groundHash),
            Item(901, 1600, OtbItemType.Container, itemHash));
        var map = new OtbmMap { ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        var tile = new OtbmTile { X = 10, Y = 20, Z = 7, GroundItemId = 100 };
        var container = new OtbmItem { Id = 200 };
        container.Contents.Add(new OtbmItem { Id = 200 });
        tile.Items.Add(container);
        map.Tiles[new(10, 20, 7)] = tile;

        var service = new MapVersionConversionService();
        var plan = service.Analyze(map, sourcePath, targetPath);

        plan.CanApply.Should().BeTrue();
        plan.Mappings.Should().Contain(new KeyValuePair<ushort, ushort>(100, 900));
        plan.Mappings.Should().Contain(new KeyValuePair<ushort, ushort>(200, 901));
        service.Apply(map, plan).Should().ContainSingle();
        tile.GroundItemId.Should().Be(900);
        tile.Items.Single().Id.Should().Be(901);
        tile.Items.Single().Contents.Single().Id.Should().Be(901);
        map.ItemsMinorVersion.Should().Be(74);
    }

    [Fact]
    public void Analyze_BlocksWholeConversionWhenAnyUsedIdHasNoSafeTarget()
    {
        var directory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(directory.FullName, "source.otb");
        var targetPath = Path.Combine(directory.FullName, "target.otb");
        SaveOtb(sourcePath, 57, Item(100, 500, OtbItemType.Ground, [1, 2, 3, 4]));
        SaveOtb(targetPath, 74, Item(900, 1500, OtbItemType.Ground, [9, 9, 9, 9]));
        var map = new OtbmMap();
        map.Tiles[new(1, 1, 7)] = new OtbmTile { X = 1, Y = 1, Z = 7, GroundItemId = 100 };

        var plan = new MapVersionConversionService().Analyze(map, sourcePath, targetPath);

        plan.CanApply.Should().BeFalse();
        plan.Issues.Should().ContainSingle(issue => issue.ServerId == 100);
    }

    private static OtbItem Item(ushort serverId, ushort clientId, OtbItemType type, byte[] hash) => new()
    {
        ServerId = serverId,
        ClientId = clientId,
        ItemType = type,
        SpriteHash = hash
    };

    private static void SaveOtb(string path, uint minor, params OtbItem[] items) =>
        new OtbParser().Save(new OtbFile
        {
            MajorVersion = 3,
            MinorVersion = minor,
            Items = items.ToList()
        }, path);
}
