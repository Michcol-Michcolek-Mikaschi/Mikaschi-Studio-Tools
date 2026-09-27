using FluentAssertions;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Narzedzia.Core.Tests;

public sealed class OtbmReaderTests
{
    [Fact]
    public void Read_ParsesMinimalHeaderAndSingleTile()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "test.otbm");

        // Buduj minimalny OTBM: header + root + MAP_DATA + TILE_AREA + 1 TILE z groundItemId
        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(0u);             // version
            bw.Write((byte)0xFE);     // root NODE_START
            bw.Write((byte)0x00);     // root type
            // root body: version(4) + width(2) + height(2) + itemsMajor(4) + itemsMinor(4)
            bw.Write(2u);             // OTBM version
            bw.Write((ushort)1024);   // width
            bw.Write((ushort)1024);   // height
            bw.Write(3u);             // itemsMajor
            bw.Write(57u);            // itemsMinor

            // MAP_DATA child
            bw.Write((byte)0xFE);
            bw.Write((byte)0x02);     // OTBM_MAP_DATA
            bw.Write((byte)1);        // OTBM_ATTR_DESCRIPTION
            bw.Write((ushort)4);
            bw.Write("test".ToCharArray());

            // TILE_AREA child
            bw.Write((byte)0xFE);
            bw.Write((byte)0x04);     // OTBM_TILE_AREA
            bw.Write((ushort)1000);   // baseX
            bw.Write((ushort)1000);   // baseY
            bw.Write((byte)7);        // baseZ

            // TILE
            bw.Write((byte)0xFE);
            bw.Write((byte)0x05);     // OTBM_TILE
            bw.Write((byte)5);        // x offset
            bw.Write((byte)6);        // y offset
            bw.Write((byte)9);        // OTBM_ATTR_ITEM (ground)
            bw.Write((ushort)100);    // item id
            bw.Write((byte)0xFF);     // TILE end

            bw.Write((byte)0xFF);     // TILE_AREA end
            bw.Write((byte)0xFF);     // MAP_DATA end
            bw.Write((byte)0xFF);     // root end
        }

        var map = new OtbmReader().Read(path);

        map.Version.Should().Be(2);
        map.Width.Should().Be(1024);
        map.Height.Should().Be(1024);
        map.ItemsMajorVersion.Should().Be(3);
        map.ItemsMinorVersion.Should().Be(57);
        map.Description.Should().Be("test");
        map.Tiles.Should().HaveCount(1);
        var tile = map.Tiles.Values.Single();
        tile.X.Should().Be(1005);
        tile.Y.Should().Be(1006);
        tile.Z.Should().Be(7);
        tile.GroundItemId.Should().Be(100);
    }

    [Fact]
    public void Read_ParsesMultipleTilesAcrossTwoTileAreas()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "multi.otbm");
        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(0u);                  // file version
            bw.Write((byte)0xFE);          // root NODE_START
            bw.Write((byte)0x00);          // root type
            bw.Write(2u); bw.Write((ushort)1024); bw.Write((ushort)1024);
            bw.Write(3u); bw.Write(57u);

            bw.Write((byte)0xFE); bw.Write((byte)0x02); // OTBM_MAP_DATA

            // TILE_AREA #1: baseX=1000, baseY=1000, baseZ=7 — 2 tile
            bw.Write((byte)0xFE); bw.Write((byte)0x04);
            bw.Write((ushort)1000); bw.Write((ushort)1000); bw.Write((byte)7);
            // tile A
            bw.Write((byte)0xFE); bw.Write((byte)0x05);
            bw.Write((byte)1); bw.Write((byte)1);
            bw.Write((byte)9); bw.Write((ushort)100);
            bw.Write((byte)0xFF);
            // tile B
            bw.Write((byte)0xFE); bw.Write((byte)0x05);
            bw.Write((byte)2); bw.Write((byte)1);
            bw.Write((byte)9); bw.Write((ushort)101);
            bw.Write((byte)0xFF);
            bw.Write((byte)0xFF); // TILE_AREA end

            // TILE_AREA #2: baseX=2000, baseY=2000, baseZ=7 — 1 tile
            bw.Write((byte)0xFE); bw.Write((byte)0x04);
            bw.Write((ushort)2000); bw.Write((ushort)2000); bw.Write((byte)7);
            bw.Write((byte)0xFE); bw.Write((byte)0x05);
            bw.Write((byte)5); bw.Write((byte)5);
            bw.Write((byte)9); bw.Write((ushort)200);
            bw.Write((byte)0xFF);
            bw.Write((byte)0xFF); // TILE_AREA end

            bw.Write((byte)0xFF); // MAP_DATA end
            bw.Write((byte)0xFF); // root end
        }

        var map = new OtbmReader().Read(path);

        map.Tiles.Should().HaveCount(3, "OTBM ma 2 TILE_AREA z łącznie 3 tile");
        map.Tiles[new(1001, 1001, 7)].GroundItemId.Should().Be(100);
        map.Tiles[new(1002, 1001, 7)].GroundItemId.Should().Be(101);
        map.Tiles[new(2005, 2005, 7)].GroundItemId.Should().Be(200);
    }

    [Fact]
    public void Read_ReportsMonotonicByteAndTileProgress()
    {
        var map = CreateProgressMap(5_000);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "progress.otbm");
        new OtbmWriter().Write(map, path);
        var reports = new List<OtbmReadProgress>();

        var loaded = new OtbmReader().Read(path, new InlineProgress<OtbmReadProgress>(reports.Add));

        loaded.Tiles.Should().HaveCount(5_000);
        reports.Should().NotBeEmpty();
        reports[0].Fraction.Should().Be(0);
        reports[^1].Fraction.Should().Be(1);
        reports[^1].TilesRead.Should().Be(5_000);
        reports[0].TotalBytes.Should().Be(new FileInfo(path).Length);
        reports[^1].BytesRead.Should().Be(reports[^1].TotalBytes);
        reports.Select(report => report.Fraction)
            .Should().BeInAscendingOrder();
        reports.Select(report => report.BytesRead)
            .Should().BeInAscendingOrder();
    }

    [Fact]
    public void Read_ParsesLargeMapAcrossMultipleStreamBuffers()
    {
        const int tileCount = 25_000;
        var map = CreateProgressMap(tileCount);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "buffered-stream.otbm");
        new OtbmWriter().Write(map, path);
        new FileInfo(path).Length.Should().BeGreaterThan(128 * 1024,
            "fixture musi przekraczać wewnętrzne okno bufora czytnika");

        var loaded = new OtbmReader().Read(path);

        loaded.Tiles.Should().HaveCount(tileCount);
        loaded.Tiles[new OtbmTileCoord(199, 349, 7)].GroundItemId.Should().Be(100);
    }

    [Fact]
    public void Read_HonorsCancellationWhileParsingTiles()
    {
        var map = CreateProgressMap(8_500);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "cancel.otbm");
        new OtbmWriter().Write(map, path);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<OtbmReadProgress>(report =>
        {
            if (report.TilesRead >= 4_096)
                cancellation.Cancel();
        });

        var action = () => new OtbmReader().Read(path, progress, cancellation.Token);

        action.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Read_RejectsTruncatedNodeTree(int removedByteCount)
    {
        var map = CreateProgressMap(1);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "truncated.otbm");
        new OtbmWriter().Write(map, path);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^removedByteCount]);

        var action = () => new OtbmReader().Read(path);

        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Read_RejectsDataAfterRootNode()
    {
        var map = CreateProgressMap(1);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "trailing-data.otbm");
        new OtbmWriter().Write(map, path);
        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
            stream.WriteByte(0x42);

        var action = () => new OtbmReader().Read(path);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*za końcem węzła głównego*");
    }

    [Fact]
    public void Writer_RoundTripsMapWithTilesItemsTownsAndWaypoints()
    {
        // Arrange: zbuduj mapę programowo
        var map = new OtbmMap
        {
            RootNodeType = 0,
            Version = 2,
            Width = 1024,
            Height = 1024,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57,
            Description = "round-trip test",
            SpawnFile = "spawn.xml",
            HouseFile = "houses.xml",
        };

        // Tile z ground + 2 items
        var tile1 = new OtbmTile { X = 1005, Y = 1006, Z = 7, GroundItemId = 100, Flags = 0x01 };
        tile1.Items.Add(new OtbmItem { Id = 2000, Count = 5 });
        tile1.Items.Add(new OtbmItem { Id = 2001, ActionId = 1234 });
        map.Tiles[new OtbmTileCoord(1005, 1006, 7)] = tile1;

        // Tile w innym sektorze (z = 6 + offset > 256)
        var tile2 = new OtbmTile { X = 2100, Y = 2100, Z = 6, GroundItemId = 200 };
        map.Tiles[new OtbmTileCoord(2100, 2100, 6)] = tile2;

        // House tile
        var house = new OtbmTile { X = 1010, Y = 1010, Z = 7, GroundItemId = 105, IsHouseTile = true, HouseId = 42 };
        map.Tiles[new OtbmTileCoord(1010, 1010, 7)] = house;

        map.Towns.Add(new OtbmTown { Id = 1, Name = "Main", TempleX = 1000, TempleY = 1000, TempleZ = 7 });
        map.Waypoints.Add(new OtbmWaypoint { Name = "spot", X = 999, Y = 999, Z = 7 });

        // Zapisz + odczytaj
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "roundtrip.otbm");
        new OtbmWriter().Write(map, path);
        var loaded = new OtbmReader().Read(path);

        // Assert
        loaded.Version.Should().Be(2);
        loaded.RootNodeType.Should().Be(0);
        loaded.Width.Should().Be(1024);
        loaded.Height.Should().Be(1024);
        loaded.Description.Should().Be("round-trip test");
        loaded.SpawnFile.Should().Be("spawn.xml");
        loaded.HouseFile.Should().Be("houses.xml");
        loaded.Tiles.Should().HaveCount(3);

        var lt1 = loaded.Tiles[new OtbmTileCoord(1005, 1006, 7)];
        lt1.GroundItemId.Should().Be(100);
        lt1.Flags.Should().Be(0x01u);
        lt1.Items.Should().HaveCount(2);
        lt1.Items[0].Id.Should().Be(2000);
        lt1.Items[0].Count.Should().Be((byte)5);
        lt1.Items[1].Id.Should().Be(2001);
        lt1.Items[1].ActionId.Should().Be((ushort)1234);

        var lt2 = loaded.Tiles[new OtbmTileCoord(2100, 2100, 6)];
        lt2.GroundItemId.Should().Be(200);

        var lh = loaded.Tiles[new OtbmTileCoord(1010, 1010, 7)];
        lh.IsHouseTile.Should().BeTrue();
        lh.HouseId.Should().Be(42u);

        loaded.Towns.Should().ContainSingle();
        loaded.Towns[0].Name.Should().Be("Main");
        loaded.Towns[0].TempleX.Should().Be((ushort)1000);

        loaded.Waypoints.Should().ContainSingle();
        loaded.Waypoints[0].Name.Should().Be("spot");
    }

    private static OtbmMap CreateProgressMap(int tileCount)
    {
        var map = new OtbmMap
        {
            Version = 2,
            Width = 1024,
            Height = 1024,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57
        };
        for (var index = 0; index < tileCount; index++)
        {
            var x = (ushort)(100 + index % 100);
            var y = (ushort)(100 + index / 100);
            var tile = new OtbmTile { X = x, Y = y, Z = 7, GroundItemId = 100 };
            map.Tiles[new(x, y, 7)] = tile;
        }
        return map;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    [Fact]
    public void Writer_RoundTripsAdvancedRmeItemAttributesWithoutDataLoss()
    {
        var map = new OtbmMap
        {
            Version = 3,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 63,
            SpawnNpcFile = "npc-spawn.xml"
        };
        var tile = new OtbmTile { X = 100, Y = 101, Z = 7, GroundItemId = 4526 };
        var container = new OtbmItem
        {
            Id = 1988,
            Count = 7,
            ActionId = 123,
            UniqueId = 456,
            Text = "tekst",
            Description = "opis",
            DepotId = 3,
            HouseDoorId = 4,
            RuneCharges = 5,
            Duration = 6000,
            DecayingState = 1,
            WrittenDate = 1_700_000_000,
            WrittenBy = "RME",
            SleeperGuid = 55,
            SleepStart = 66,
            Charges = 77,
            Tier = 8,
            PodiumOutfit = Enumerable.Range(1, 15).Select(value => (byte)value).ToArray()
        };
        container.Contents.Add(new OtbmItem { Id = 2148, Count = 100 });
        tile.Items.Add(container);
        map.Tiles[new(tile.X, tile.Y, tile.Z)] = tile;

        var directory = Directory.CreateTempSubdirectory();
        var firstPath = Path.Combine(directory.FullName, "advanced.otbm");
        var secondPath = Path.Combine(directory.FullName, "advanced-copy.otbm");
        new OtbmWriter().Write(map, firstPath);

        var loaded = new OtbmReader().Read(firstPath);
        new OtbmWriter().Write(loaded, secondPath);

        var item = loaded.Tiles.Values.Single().Items.Single();
        loaded.SpawnNpcFile.Should().Be("npc-spawn.xml");
        item.Description.Should().Be("opis");
        item.DepotId.Should().Be(3);
        item.HouseDoorId.Should().Be(4);
        item.Duration.Should().Be(6000);
        item.WrittenBy.Should().Be("RME");
        item.PodiumOutfit.Should().Equal(Enumerable.Range(1, 15).Select(value => (byte)value));
        item.Contents.Should().ContainSingle().Which.Id.Should().Be(2148);
        File.ReadAllBytes(secondPath).Should().Equal(File.ReadAllBytes(firstPath));
    }

    [Fact]
    public void WriterAndReader_RoundTripOtbm4TypedAttributeMapAndCanonicalProperties()
    {
        var map = new OtbmMap
        {
            Version = 4, Width = 512, Height = 512,
            ItemsMajorVersion = 3, ItemsMinorVersion = 74
        };
        var tile = new OtbmTile { X = 100, Y = 100, Z = 7, GroundItemId = 100 };
        var item = new OtbmItem
        {
            Id = 200,
            ActionId = 450,
            UniqueId = 1200,
            Text = "book",
            Description = "description",
            Tier = 4
        };
        item.CustomAttributes.Add(new OtbmCustomAttribute
            { Key = "owner", Type = OtbmCustomAttributeType.String, StringValue = "Michał" });
        item.CustomAttributes.Add(new OtbmCustomAttribute
            { Key = "level", Type = OtbmCustomAttributeType.Integer, IntegerValue = -12 });
        item.CustomAttributes.Add(new OtbmCustomAttribute
            { Key = "ratio", Type = OtbmCustomAttributeType.Float, FloatValue = 1.25f });
        item.CustomAttributes.Add(new OtbmCustomAttribute
            { Key = "enabled", Type = OtbmCustomAttributeType.Boolean, BooleanValue = true });
        item.CustomAttributes.Add(new OtbmCustomAttribute
            { Key = "precise", Type = OtbmCustomAttributeType.Double, DoubleValue = Math.PI });
        tile.Items.Add(item);
        map.Tiles[new(tile.X, tile.Y, tile.Z)] = tile;

        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "attribute-map.otbm");
        var canonicalPath = Path.Combine(directory.FullName, "attribute-map-canonical.otbm");
        new OtbmWriter().Write(map, path);

        var loaded = new OtbmReader().Read(path);
        var loadedItem = loaded.Tiles.Values.Single().Items.Single();
        loadedItem.ActionId.Should().Be(450);
        loadedItem.UniqueId.Should().Be(1200);
        loadedItem.Text.Should().Be("book");
        loadedItem.Description.Should().Be("description");
        loadedItem.Tier.Should().Be(4);
        loadedItem.CustomAttributes.Should().Contain(attribute =>
            attribute.Key == "owner" && attribute.StringValue == "Michał");
        loadedItem.CustomAttributes.Should().Contain(attribute =>
            attribute.Key == "level" && attribute.IntegerValue == -12);
        loadedItem.CustomAttributes.Should().Contain(attribute =>
            attribute.Key == "ratio" && attribute.FloatValue == 1.25f);
        loadedItem.CustomAttributes.Should().Contain(attribute =>
            attribute.Key == "enabled" && attribute.BooleanValue);
        loadedItem.CustomAttributes.Should().Contain(attribute =>
            attribute.Key == "precise" && attribute.DoubleValue == Math.PI);

        loadedItem.RawAttributeData = [];
        new OtbmWriter().Write(loaded, canonicalPath);
        var canonical = new OtbmReader().Read(canonicalPath).Tiles.Values.Single().Items.Single();
        canonical.CustomAttributes.Should().HaveCount(10, "pięć własnych i pięć kanonicznych atrybutów RME");
        canonical.CustomAttributes.Should().Contain(attribute => attribute.Key == "aid");
        canonical.CustomAttributes.Should().Contain(attribute => attribute.Key == "uid");
    }

    [Fact]
    public void ExternalDataReader_LoadsRmeHouseAndSpawnFiles()
    {
        var directory = Directory.CreateTempSubdirectory();
        var mapPath = Path.Combine(directory.FullName, "world.otbm");
        var map = new OtbmMap
        {
            HouseFile = "world-house.xml",
            SpawnFile = "world-spawn.xml"
        };
        File.WriteAllText(Path.Combine(directory.FullName, map.HouseFile),
            """
            <?xml version="1.0"?>
            <houses>
              <house name="Test House" houseid="7" entryx="101" entryy="102" entryz="6" rent="300" townid="2" size="20" guildhall="true" />
            </houses>
            """);
        File.WriteAllText(Path.Combine(directory.FullName, map.SpawnFile),
            """
            <?xml version="1.0"?>
            <spawns>
              <spawn centerx="200" centery="210" centerz="7" radius="4">
                <monster name="Dragon" x="-1" y="2" z="7" spawntime="60" direction="2" />
                <npc name="Guide" x="0" y="0" z="7" spawntime="30" />
              </spawn>
            </spawns>
            """);

        var warnings = OtbmExternalDataReader.Load(map, mapPath);

        warnings.Should().BeEmpty();
        map.Houses.Should().ContainSingle();
        map.Houses[0].Should().BeEquivalentTo(new OtbmHouse
        {
            Id = 7,
            Name = "Test House",
            EntryX = 101,
            EntryY = 102,
            EntryZ = 6,
            Rent = 300,
            TownId = 2,
            Size = 20,
            IsGuildhall = true
        });
        map.Spawns.Should().ContainSingle();
        map.Spawns[0].Creatures.Should().HaveCount(2);
        map.Spawns[0].Creatures[0].X.Should().Be(199);
        map.Spawns[0].Creatures[0].Y.Should().Be(212);
        map.Spawns[0].Creatures[1].IsNpc.Should().BeTrue();
    }

    [Fact]
    public void ExternalDataWriter_RoundTripsHousesAndSplitCanaryNpcSpawns()
    {
        var directory = Directory.CreateTempSubdirectory();
        var mapPath = Path.Combine(directory.FullName, "world.otbm");
        var map = new OtbmMap
        {
            HouseFile = "world-house.xml",
            SpawnFile = "world-spawn.xml",
            SpawnNpcFile = "world-npc.xml"
        };
        map.Houses.Add(new OtbmHouse
        {
            Id = 4, Name = "Guild & Hall", EntryX = 100, EntryY = 101, EntryZ = 7,
            Rent = 900, TownId = 2, Size = 30, IsGuildhall = true
        });
        map.Tiles[new(100, 100, 7)] = new OtbmTile
            { X = 100, Y = 100, Z = 7, IsHouseTile = true, HouseId = 4 };
        map.Tiles[new(101, 100, 7)] = new OtbmTile
            { X = 101, Y = 100, Z = 7, IsHouseTile = true, HouseId = 4 };
        var spawn = new OtbmSpawn { CenterX = 200, CenterY = 210, CenterZ = 7, Radius = 5 };
        spawn.Creatures.Add(new OtbmCreature
        {
            Name = "Dragon", X = 199, Y = 212, Z = 7, SpawnTime = 60, Direction = 2
        });
        spawn.Creatures.Add(new OtbmCreature
        {
            Name = "Guide", X = 200, Y = 210, Z = 7, SpawnTime = 30, IsNpc = true
        });
        map.Spawns.Add(spawn);

        OtbmExternalDataWriter.Save(map, mapPath).Should().Be(3);
        File.ReadAllText(Path.Combine(directory.FullName, map.SpawnFile)).Should().Contain("<monster").And.NotContain("<npc");
        File.ReadAllText(Path.Combine(directory.FullName, map.SpawnNpcFile)).Should().Contain("<npc").And.NotContain("<monster");

        var loaded = new OtbmMap
        {
            HouseFile = map.HouseFile,
            SpawnFile = map.SpawnFile,
            SpawnNpcFile = map.SpawnNpcFile
        };
        OtbmExternalDataReader.Load(loaded, mapPath).Should().BeEmpty();

        loaded.Houses.Should().ContainSingle().Which.Name.Should().Be("Guild & Hall");
        loaded.Houses[0].Size.Should().Be(2, "RME wylicza size z rzeczywistych pól domu");
        loaded.Spawns.Should().ContainSingle().Which.Creatures.Should().HaveCount(2);
        loaded.Spawns[0].Creatures.Should().Contain(creature => creature.Name == "Dragon" && !creature.IsNpc);
        loaded.Spawns[0].Creatures.Should().Contain(creature => creature.Name == "Guide" && creature.IsNpc);
    }
}
