using FluentAssertions;
using Modules.MapEditor.Services;
using Modules.MapEditor.ViewModels;
using Narzedzia.Core.Models;

namespace Modules.MapEditor.Tests;

public sealed class RmeMaterialCatalogServiceTests
{
    [Fact]
    public void Load_PreservesRmeTilesetsCategoriesRangesAndBrushDefinitions()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllBytes(Path.Combine(directory.FullName, "items.otb"), []);
        File.WriteAllText(Path.Combine(directory.FullName, "materials.xml"), """
            <materials>
              <include file="grounds.xml"/>
              <include file="doodads.xml"/>
              <include file="tilesets.xml"/>
            </materials>
            """);
        File.WriteAllText(Path.Combine(directory.FullName, "grounds.xml"), """
            <materials>
              <brush name="grass" type="ground" server_lookid="4526" z-order="3500">
                <item id="4526" chance="2500"/>
                <item id="4527" chance="10"/>
              </brush>
            </materials>
            """);
        File.WriteAllText(Path.Combine(directory.FullName, "doodads.xml"), """
            <materials>
              <brush name="broken tree" type="doodad" server_lookid="8793" thickness="12/100">
                <item id="8792" chance="10"/>
                <composite chance="20">
                  <tile x="0" y="0"><item id="8793"/></tile>
                  <tile x="-1" y="0"><item id="8794"/></tile>
                </composite>
              </brush>
            </materials>
            """);
        File.WriteAllText(Path.Combine(directory.FullName, "tilesets.xml"), """
            <materials>
              <tileset name="Nature">
                <terrain><brush name="grass"/><item fromid="100" toid="102"/></terrain>
                <doodad_and_raw><brush name="broken tree"/></doodad_and_raw>
              </tileset>
            </materials>
            """);

        var catalog = new RmeMaterialCatalogService().Load(Path.Combine(directory.FullName, "items.otb"));

        catalog.LoadedFiles.Should().HaveCount(4);
        catalog.Brushes.Should().ContainKeys("grass", "broken tree");
        catalog.Brushes["grass"].PreviewItemId.Should().Be(4526);
        catalog.Brushes["grass"].ZOrder.Should().Be(3500);
        catalog.Brushes["broken tree"].Thickness.Should().Be(12);
        catalog.Brushes["broken tree"].ThicknessCeiling.Should().Be(100);
        catalog.Brushes["broken tree"].Alternatives.Single().CompositeItems.Single().Tiles.Should().HaveCount(2);

        var tileset = catalog.Tilesets.Should().ContainSingle().Subject;
        tileset.Name.Should().Be("Nature");
        tileset.Categories[RmePaletteCategory.Terrain].Should().HaveCount(4);
        tileset.Categories[RmePaletteCategory.Doodad].Should().ContainSingle();
        tileset.Categories[RmePaletteCategory.Raw].Should().ContainSingle();
        tileset.Categories[RmePaletteCategory.Doodad][0].Brush.Should().NotBeNull();
    }

    [Fact]
    public void Load_RejectsIncludeEscapingTheVersionDirectory()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllBytes(Path.Combine(directory.FullName, "items.otb"), []);
        File.WriteAllText(Path.Combine(directory.FullName, "materials.xml"),
            "<materials><include file=\"../outside.xml\"/></materials>");

        var catalog = new RmeMaterialCatalogService().Load(Path.Combine(directory.FullName, "items.otb"));

        catalog.LoadedFiles.Should().ContainSingle();
        catalog.Tilesets.Should().BeEmpty();
    }

    [Fact]
    public void PackagedRme1287Catalog_IsCompleteAndInternallyConsistent()
    {
        var itemsOtbPath = Path.Combine(AppContext.BaseDirectory, "rme-data", "1287", "items.otb");

        var catalog = new RmeMaterialCatalogService().Load(itemsOtbPath);

        catalog.Tilesets.Should().HaveCount(38);
        catalog.Brushes.Should().HaveCount(523);
        catalog.Borders.Should().HaveCount(161);
        catalog.LoadedFiles.Should().HaveCount(6);
        catalog.Warnings.Should().BeEmpty();
        catalog.OptionalBorderItemIds.Should().NotBeEmpty();
        catalog.Brushes.Values.SelectMany(brush => brush.BorderRules ?? [])
            .SelectMany(rule => rule.SpecificCases).Should().HaveCount(117);
        catalog.Brushes.Values.SelectMany(brush => brush.BorderRules ?? [])
            .Count(rule => rule.InlineBorder is not null).Should().Be(3);
        catalog.Brushes["stone wall"].OrientedItems.Should().ContainKey("northwest diagonal");
        catalog.Brushes["stone wall"].Doors.Should().NotBeEmpty();
        catalog.Brushes["log"].OrientedItems.Should().ContainKeys("alone", "horizontal", "vertical");
        catalog.Brushes["ice floe"].OrientedItems.Should().ContainKey("center");
        var nature = catalog.Tilesets.Single(tileset => tileset.Name == "Nature");
        nature.Categories[RmePaletteCategory.Terrain].Should().HaveCount(47);
        nature.Categories[RmePaletteCategory.Doodad].Should().HaveCount(97);
    }

    [Fact]
    public void PackagedRme1310Catalog_ProvidesTheSameTilesetMenusAsRme()
    {
        var itemsOtbPath = Path.Combine(AppContext.BaseDirectory, "rme-data", "1310", "items.otb");
        var catalog = new RmeMaterialCatalogService().Load(itemsOtbPath);

        Names(RmePaletteCategory.Terrain).Should().Equal(
            "City Grounds", "City Grounds - Tiny Borders", "City Walls", "Nature",
            "Nature - Tiny Borders", "Roofs", "Snow", "Stairs / Ramps / Ladders");
        Names(RmePaletteCategory.Doodad).Should().Equal(
            "Architecture", "Corpses", "Exterior", "Hangables", "Interior", "Magic Fields",
            "Nature", "Sea", "Signs", "Snow", "Splash", "Statues", "Trash");
        Names(RmePaletteCategory.Item).Should().Equal(
            "Addon and Quest Items", "Containers", "Creature Products", "Dolls", "Equipment",
            "Fluid Containers", "Food", "Jewelry", "Ornaments", "Runes", "Shields", "Taming Items",
            "Tools", "Trinkets", "Weapons", "Weapons (Magic)",
            "Weapons (Mayhem, Remedy, Carving)", "Writeables / Signs / Other");
        Names(RmePaletteCategory.Raw).Should().Equal(
            "Architecture", "Banners", "Blank", "Boats", "Borders", "Bridges", "Cake", "Corpses",
            "Creature Figures", "Crystals", "Desert", "Exterior", "Grounds", "Hangables", "Interior",
            "Magic Fields", "Mountains", "Nature", "Others", "Pipes", "Redundant", "Roofs", "Sea",
            "Skulls / Bones / Undead", "Splash", "Stairs / Ramps / Ladders", "Walls",
            "Writeables / Signs / Other");
        Names(RmePaletteCategory.Collection).Should().BeEmpty();

        string[] Names(RmePaletteCategory category) =>
            MapEditorViewModel.GetRmePaletteGroupNames(catalog.Tilesets, category).ToArray();
    }

    [Fact]
    public void Load_AcceptsLegacyRmeTrailingNullAndBareAmpersand()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllBytes(Path.Combine(directory.FullName, "items.otb"), []);
        File.WriteAllText(Path.Combine(directory.FullName, "materials.xml"),
            "<materials><brush name=\"fire & ice\" type=\"doodad\" server_lookid=\"100\"><item id=\"100\" chance=\"1\"/></brush></materials>\0");

        var catalog = new RmeMaterialCatalogService().Load(Path.Combine(directory.FullName, "items.otb"));

        catalog.Brushes.Should().ContainKey("fire & ice");
        catalog.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void PackagedRmeCatalogs_AllSupportedVersionsLoadTheirPalettes()
    {
        var dataRoot = Path.Combine(AppContext.BaseDirectory, "rme-data");
        var versionDirectories = Directory.EnumerateDirectories(dataRoot)
            .Where(directory => File.Exists(Path.Combine(directory, "items.otb")))
            .ToArray();
        var loader = new RmeMaterialCatalogService();

        versionDirectories.Should().HaveCount(35);
        foreach (var directory in versionDirectories)
        {
            var catalog = loader.Load(Path.Combine(directory, "items.otb"));
            catalog.Tilesets.Should().NotBeEmpty($"wersja {Path.GetFileName(directory)} musi mieć palety RME");
            catalog.Brushes.Should().NotBeEmpty($"wersja {Path.GetFileName(directory)} musi mieć brushe RME");
            catalog.Borders.Should().NotBeEmpty($"wersja {Path.GetFileName(directory)} musi mieć obramowania RME");
        }
    }

    [Fact]
    public void CreatureCatalog_LoadsVersionedMonsterNpcAndOutfitData()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(directory.FullName, "creatures.xml"), """
            <creatures>
              <creature name="Dragon & Lord" type="monster" looktype="34" />
              <creature name="Guide" type="npc" looktype="128" lookitem="2160" />
            </creatures>
            """);

        var creatures = new RmeCreatureCatalogService().Load(directory.FullName);

        creatures.Should().HaveCount(2);
        creatures["Dragon & Lord"].IsNpc.Should().BeFalse();
        creatures["Dragon & Lord"].LookType.Should().Be(34);
        creatures["Guide"].IsNpc.Should().BeTrue();
        creatures["Guide"].LookItem.Should().Be(2160);
    }

    [Fact]
    public void GroundBorderService_UsesRmeZOrderAndCleansObsoleteBorderItems()
    {
        var itemsOtbPath = Path.Combine(AppContext.BaseDirectory, "rme-data", "1287", "items.otb");
        var catalog = new RmeMaterialCatalogService().Load(itemsOtbPath);
        var map = new OtbmMap();
        var dirt = new OtbmTile { X = 100, Y = 100, Z = 7, GroundItemId = 103 };
        var grass = new OtbmTile { X = 101, Y = 100, Z = 7, GroundItemId = 4526 };
        map.Tiles[new(100, 100, 7)] = dirt;
        map.Tiles[new(101, 100, 7)] = grass;
        var borders = new RmeGroundBorderService(catalog);

        borders.ReborderTile(map, dirt);

        dirt.Items.Select(item => item.Id).Should().Contain(4543,
            "grass na wschód od dirt używa wschodniej części borderu #2");

        grass.GroundItemId = 103;
        borders.ReborderTile(map, dirt);
        dirt.Items.Select(item => item.Id).Should().NotContain(4543,
            "nieaktualny wschodni border trawy musi zostać usunięty");
        dirt.Items.Select(item => item.Id).Should().Contain(891,
            "dirt zachowuje zdefiniowany w RME inner border do pustego terenu");
    }

    [Fact]
    public void GroundBorderService_AppliesRmeSpecificReplacementRules()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllBytes(Path.Combine(directory.FullName, "items.otb"), []);
        File.WriteAllText(Path.Combine(directory.FullName, "materials.xml"), """
            <materials>
              <border id="1"><borderitem edge="e" item="100"/></border>
              <brush name="earth" type="ground" server_lookid="10" z-order="0">
                <item id="10" chance="1"/>
                <border align="inner" to="grass" id="1">
                  <specific>
                    <conditions><match_border id="1" edge="e"/></conditions>
                    <actions><replace_border id="1" edge="e" with="150"/></actions>
                  </specific>
                </border>
              </brush>
              <brush name="grass" type="ground" server_lookid="20" z-order="1">
                <item id="20" chance="1"/>
              </brush>
            </materials>
            """);
        var catalog = new RmeMaterialCatalogService().Load(Path.Combine(directory.FullName, "items.otb"));
        var map = new OtbmMap();
        var earth = new OtbmTile { X = 100, Y = 100, Z = 7, GroundItemId = 10 };
        map.Tiles[new(100, 100, 7)] = earth;
        map.Tiles[new(101, 100, 7)] = new OtbmTile { X = 101, Y = 100, Z = 7, GroundItemId = 20 };

        new RmeGroundBorderService(catalog).ReborderTile(map, earth);

        earth.Items.Select(item => item.Id).Should().Equal(150);
        catalog.BorderItemIds.Should().Contain(150, "wynik replace_border także jest borderem w RME");
    }

    [Fact]
    public void GroundBorderService_PreservesOptionalBorderExactlyLikeRme()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllBytes(Path.Combine(directory.FullName, "items.otb"), []);
        File.WriteAllText(Path.Combine(directory.FullName, "materials.xml"), """
            <materials>
              <border id="2" type="optional"><borderitem edge="e" item="200"/></border>
              <brush name="earth" type="ground" server_lookid="10" z-order="0">
                <item id="10" chance="1"/>
                <friend name="mountain"/>
              </brush>
              <brush name="mountain" type="ground" server_lookid="30" z-order="10" solo_optional="true">
                <item id="30" chance="1"/>
                <optional id="2"/>
              </brush>
            </materials>
            """);
        var catalog = new RmeMaterialCatalogService().Load(Path.Combine(directory.FullName, "items.otb"));
        var map = new OtbmMap();
        var earth = new OtbmTile { X = 100, Y = 100, Z = 7, GroundItemId = 10 };
        earth.Items.Add(new OtbmItem { Id = 200 });
        map.Tiles[new(100, 100, 7)] = earth;
        map.Tiles[new(101, 100, 7)] = new OtbmTile { X = 101, Y = 100, Z = 7, GroundItemId = 30 };

        new RmeGroundBorderService(catalog).ReborderTile(map, earth);

        earth.Items.Select(item => item.Id).Should().Equal(200);
        catalog.OptionalBorderItemIds.Should().Contain(200);
    }

    [Fact]
    public void GroundBorderService_OptionalBorderToolCanExplicitlyDrawAndEraseState()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllBytes(Path.Combine(directory.FullName, "items.otb"), []);
        File.WriteAllText(Path.Combine(directory.FullName, "materials.xml"), """
            <materials>
              <border id="2" type="optional"><borderitem edge="e" item="200"/></border>
              <brush name="earth" type="ground" server_lookid="10"><item id="10"/><friend name="mountain"/></brush>
              <brush name="mountain" type="ground" server_lookid="30" z-order="10"><item id="30"/><optional id="2"/></brush>
            </materials>
            """);
        var catalog = new RmeMaterialCatalogService().Load(Path.Combine(directory.FullName, "items.otb"));
        var map = new OtbmMap();
        var earth = new OtbmTile { X = 100, Y = 100, Z = 7, GroundItemId = 10 };
        map.Tiles[new(100, 100, 7)] = earth;
        map.Tiles[new(101, 100, 7)] = new OtbmTile { X = 101, Y = 100, Z = 7, GroundItemId = 30 };
        var service = new RmeGroundBorderService(catalog);

        service.ReborderTile(map, earth, optionalBorderState: true);
        earth.Items.Select(item => item.Id).Should().Equal(200);

        service.ReborderTile(map, earth, optionalBorderState: false);
        earth.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(17, "cnw", "e")]
    [InlineData(26, "n", "w", "e")]
    [InlineData(90, "s", "w", "e", "n")]
    [InlineData(165, "csw", "cne", "cnw", "cse")]
    public void ResolveEdges_UsesExactRmeBrushTableOrder(int mask, params string[] edges)
    {
        RmeGroundBorderService.ResolveEdges(mask).Should().Equal(edges);
    }

    [Theory]
    [InlineData(0, "alone")]
    [InlineData(2, "south")]
    [InlineData(3, "alone")]
    [InlineData(8, "east")]
    [InlineData(16, "west")]
    [InlineData(24, "horizontal")]
    [InlineData(66, "vertical")]
    [InlineData(67, "north")]
    public void ConnectedBrushService_UsesExactRmeTableAlignment(int mask, string alignment)
    {
        RmeConnectedBrushService.ResolveTableAlignment(mask).Should().Be(alignment);
    }

    [Fact]
    public void ConnectedBrushService_RebuildsWallsTablesAndCarpetsLikeRme()
    {
        var catalog = LoadConnectedBrushCatalog();
        var service = new RmeConnectedBrushService(catalog);

        var wallMap = MapWithItems((10, 10, 100), (11, 10, 100));
        service.Rebuild(wallMap, wallMap.Tiles.Keys.ToArray(), new Random(1));
        wallMap.Tiles[new(10, 10, 7)].Items.Single().Id.Should().Be(104, "wschodni sąsiad daje west end");
        wallMap.Tiles[new(11, 10, 7)].Items.Single().Id.Should().Be(102, "zachodni sąsiad daje east end");

        var tableMap = MapWithItems((10, 10, 200), (11, 10, 200));
        service.Rebuild(tableMap, tableMap.Tiles.Keys.ToArray(), new Random(1));
        tableMap.Tiles[new(10, 10, 7)].Items.Single().Id.Should().Be(204);
        tableMap.Tiles[new(11, 10, 7)].Items.Single().Id.Should().Be(203);

        var carpetMap = MapWithItems(
            (10, 10, 312), (11, 10, 312), (12, 10, 312),
            (10, 11, 312), (11, 11, 312), (12, 11, 312),
            (10, 12, 312), (11, 12, 312), (12, 12, 312));
        service.Rebuild(carpetMap, carpetMap.Tiles.Keys.ToArray(), new Random(1));
        carpetMap.Tiles[new(10, 10, 7)].Items.Single().Id.Should().Be(307, "lewy górny róg używa cse");
        carpetMap.Tiles[new(11, 11, 7)].Items.Single().Id.Should().Be(312, "środek pełnego dywanu pozostaje center");
    }

    private static RmeMaterialCatalog LoadConnectedBrushCatalog()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllBytes(Path.Combine(directory.FullName, "items.otb"), []);
        File.WriteAllText(Path.Combine(directory.FullName, "materials.xml"), """
            <materials>
              <brush name="test wall" type="wall" server_lookid="100">
                <wall type="pole"><item id="100"/></wall>
                <wall type="south end"><item id="101"/></wall>
                <wall type="east end"><item id="102"/></wall>
                <wall type="corner"><item id="103"/></wall>
                <wall type="west end"><item id="104"/></wall>
                <wall type="northeast diagonal"><item id="105"/></wall>
                <wall type="horizontal"><item id="106"/></wall>
                <wall type="south T"><item id="107"/></wall>
                <wall type="north end"><item id="108"/></wall>
                <wall type="vertical"><item id="109"/></wall>
                <wall type="southwest diagonal"><item id="110"/></wall>
                <wall type="east T"><item id="111"/></wall>
                <wall type="southeast diagonal"><item id="112"/></wall>
                <wall type="west T"><item id="113"/></wall>
                <wall type="north T"><item id="114"/></wall>
                <wall type="intersection"><item id="115"/></wall>
              </brush>
              <brush name="test table" type="table" server_lookid="200">
                <table align="alone"><item id="200"/></table>
                <table align="north"><item id="201"/></table>
                <table align="south"><item id="202"/></table>
                <table align="east"><item id="203"/></table>
                <table align="west"><item id="204"/></table>
                <table align="horizontal"><item id="205"/></table>
                <table align="vertical"><item id="206"/></table>
              </brush>
              <brush name="test carpet" type="carpet" server_lookid="312">
                <carpet align="n" id="300"/><carpet align="e" id="301"/>
                <carpet align="s" id="302"/><carpet align="w" id="303"/>
                <carpet align="cnw" id="304"/><carpet align="cne" id="305"/>
                <carpet align="csw" id="306"/><carpet align="cse" id="307"/>
                <carpet align="dnw" id="308"/><carpet align="dne" id="309"/>
                <carpet align="dse" id="310"/><carpet align="dsw" id="311"/>
                <carpet align="center" id="312"/>
              </brush>
            </materials>
            """);
        return new RmeMaterialCatalogService().Load(Path.Combine(directory.FullName, "items.otb"));
    }

    private static OtbmMap MapWithItems(params (ushort X, ushort Y, ushort ItemId)[] entries)
    {
        var map = new OtbmMap();
        foreach (var (x, y, itemId) in entries)
        {
            var tile = new OtbmTile { X = x, Y = y, Z = 7 };
            tile.Items.Add(new OtbmItem { Id = itemId });
            map.Tiles[new(x, y, 7)] = tile;
        }
        return map;
    }
}
