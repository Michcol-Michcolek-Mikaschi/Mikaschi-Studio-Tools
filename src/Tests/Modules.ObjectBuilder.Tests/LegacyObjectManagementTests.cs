using FluentAssertions;
using Avalonia.Media;
using Modules.ObjectBuilder.Services;
using Modules.ObjectBuilder.ViewModels;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using AvaloniaColor = Avalonia.Media.Color;

namespace Modules.ObjectBuilder.Tests;

public sealed class LegacyObjectManagementTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(36, 51, 0, 0)]
    [InlineData(156, 204, 102, 0)]
    [InlineData(215, 255, 255, 255)]
    public void TibiaColorPalette_MatchesOriginalObjectBuilder(
        int id,
        byte red,
        byte green,
        byte blue)
    {
        var option = LegacyTibiaColorOption.FromId(id);

        option.Id.Should().Be(id);
        option.Brush.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(AvaloniaColor.FromRgb(red, green, blue));
    }

    [Fact]
    public void PropertyColorSelectors_UpdateLegacyDatValues()
    {
        var thing = CreateThing(100);
        using var cache = new LegacyThumbnailCache(new LegacySpriteStore());
        var viewModel = new ObjectBuilderViewModel
        {
            SelectedThing = new LegacyThingListItem(thing, cache)
        };

        viewModel.LightColorSelectionIndex = 156;
        viewModel.AutomapColorSelectionIndex = 129;

        thing.LightColor.Should().Be(156);
        thing.MiniMapColor.Should().Be(129);
        viewModel.LightColorSelectionIndex.Should().Be(156);
        viewModel.AutomapColorSelectionIndex.Should().Be(129);
    }

    [Theory]
    [InlineData(4, "southwest")]
    [InlineData(5, "southeast")]
    [InlineData(6, "northwest")]
    [InlineData(7, "northeast")]
    public void DiagonalDirectionIndexes_MatchOriginalObjectBuilder(int direction, string expected)
    {
        var viewModel = new ObjectBuilderViewModel { Direction = direction };

        var selected = expected switch
        {
            "southwest" => viewModel.IsSouthWest,
            "southeast" => viewModel.IsSouthEast,
            "northwest" => viewModel.IsNorthWest,
            "northeast" => viewModel.IsNorthEast,
            _ => false
        };

        selected.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 1, 0)] // North
    [InlineData(1, 2, 1)] // East
    [InlineData(2, 1, 2)] // South
    [InlineData(3, 0, 1)] // West
    [InlineData(4, 0, 2)] // South west
    [InlineData(5, 2, 2)] // South east
    [InlineData(6, 0, 0)] // North west
    [InlineData(7, 2, 0)] // North east
    public void MissileDirections_MapToOriginalObjectBuilderThreeByThreeGrid(
        int direction,
        int expectedX,
        int expectedY)
    {
        var position = LegacyDirectionMapping.ToPatternPosition(
            DatThingCategory.Missiles,
            direction,
            addon: 0,
            patternXCount: 3,
            patternYCount: 3);

        position.Should().Be(new LegacyPatternPosition(expectedX, expectedY));
    }

    [Fact]
    public void MissileDirections_SelectEightDistinctSpriteSlots()
    {
        var group = new DatThingFrameGroup
        {
            Width = 1,
            Height = 1,
            Layers = 1,
            PatternX = 3,
            PatternY = 3,
            PatternZ = 1,
            Frames = 1,
            SpriteIds = new uint[9]
        };
        var selectedSlots = Enumerable.Range(0, 8)
            .Select(direction => LegacyDirectionMapping.ToPatternPosition(
                DatThingCategory.Missiles,
                direction,
                addon: 0,
                patternXCount: 3,
                patternYCount: 3))
            .Select(position => DatParser.CalculateSpriteIndex(
                group,
                tileX: 0,
                tileY: 0,
                layer: 0,
                patternX: position.X,
                patternY: position.Y,
                patternZ: 0,
                frame: 0))
            .ToArray();

        selectedSlots.Should().Equal(1, 5, 7, 3, 6, 8, 0, 2);
        selectedSlots.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(DatThingCategory.Items, 1)]
    [InlineData(DatThingCategory.Outfits, 4)]
    [InlineData(DatThingCategory.Effects, 1)]
    [InlineData(DatThingCategory.Missiles, 8)]
    public void DirectionCount_MatchesObjectBuilderCategory(
        DatThingCategory category,
        int expected)
    {
        LegacyDirectionMapping.GetDirectionCount(category).Should().Be(expected);
    }

    [Fact]
    public void MissileProperties_MatchObjectBuilder_CommonPropertiesVisibleButFlagsHidden()
    {
        var thing = CreateThing(1);
        thing.Category = DatThingCategory.Missiles;
        using var cache = new LegacyThumbnailCache(new LegacySpriteStore());
        var viewModel = new ObjectBuilderViewModel
        {
            SelectedThing = new LegacyThingListItem(thing, cache)
        };

        viewModel.ShowPropertiesSection.Should().BeTrue();
        viewModel.ShowDirectionSelector.Should().BeTrue();
        viewModel.ShowFlagsSection.Should().BeFalse();
    }

    [Theory]
    [InlineData(1, "1. Idle + Walking (wspólna) · 1 klatka")]
    [InlineData(3, "1. Idle + Walking (wspólna) · 3 klatki")]
    [InlineData(8, "1. Idle + Walking (wspólna) · 8 klatek")]
    public void FrameGroupLabel_ShowsMeaningAndFrameCount(int frames, string expected)
    {
        var item = new LegacyFrameGroupListItem(0, "Idle + Walking (wspólna)", frames);

        item.DisplayName.Should().Be(expected);
    }

    [Fact]
    public void SpriteImageImport_CutsInRowOrderAndUsesAlphaOrMagentaAsTransparency()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "sheet.png");
        using (var image = new Image<Rgba32>(96, 32))
        {
            for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
            {
                image[x, y] = x < 32
                    ? new Rgba32(255, 0, 0, 255)
                    : x < 64
                        ? new Rgba32(0, 255, 0, 255)
                        : new Rgba32(255, 0, 255, 255);
            }
            image[0, 0] = new Rgba32(255, 0, 255, 255);
            image.SaveAsPng(path);
        }

        var batch = new LegacySpriteImageImportService().DecodeFiles([path]);

        batch.SourceFileCount.Should().Be(1);
        batch.EmptyTileCount.Should().Be(1);
        batch.Tiles.Should().HaveCount(2);
        batch.Tiles[0].Column.Should().Be(0);
        batch.Tiles[1].Column.Should().Be(1);
        batch.Tiles[0].BgraPixels[3].Should().Be(0);
        batch.Tiles[0].BgraPixels.AsSpan(4, 4).ToArray().Should().Equal(0, 0, 255, 255);
        batch.Tiles[1].BgraPixels.AsSpan(0, 4).ToArray().Should().Equal(0, 255, 0, 255);
    }

    [Fact]
    public void SpriteImageImport_RejectsDimensionsThatAreNotMultiplesOf32()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "invalid.png");
        using (var image = new Image<Rgba32>(33, 32)) image.SaveAsPng(path);

        var action = () => new LegacySpriteImageImportService().DecodeFiles([path]);

        action.Should().Throw<InvalidDataException>().WithMessage("*wielokrotnością 32*");
    }

    [Fact]
    public void DeepClone_CopiesFlagsGroupsAndArraysWithoutSharingMutableState()
    {
        var source = CreateThing(100);
        source.IsGround = true;
        source.GroundSpeed = 150;
        source.BoneOffsets[0] = 7;
        source.FrameGroups[0].SpriteIds = [11, 12];
        source.FrameGroups[0].FrameDurations = [new DatFrameDuration { Min = 80, Max = 120 }];

        var clone = source.DeepClone();
        clone.GroundSpeed = 200;
        clone.BoneOffsets[0] = 20;
        clone.FrameGroups[0].SpriteIds[0] = 99;
        clone.FrameGroups[0].FrameDurations[0].Min = 500;

        clone.IsGround.Should().BeTrue();
        source.GroundSpeed.Should().Be(150);
        source.BoneOffsets[0].Should().Be(7);
        source.FrameGroups[0].SpriteIds[0].Should().Be(11);
        source.FrameGroups[0].FrameDurations[0].Min.Should().Be(80);
    }

    [Fact]
    public void RemoveThings_ReindexesLegacyCategoryAndUpdatesMaximumId()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "Tibia.dat");
        var options = DatParserOptions.Default;
        var data = new DatFile
        {
            Signature = 0x12345678,
            ItemsMaxId = 102,
            OutfitsMaxId = 0,
            EffectsMaxId = 0,
            MissilesMaxId = 0
        };
        data.Items.Add(CreateThing(100));
        data.Items.Add(CreateThing(101));
        data.Items.Add(CreateThing(102));
        data.Items[0].HasMarketInfo = true;
        data.Items[0].MarketTradeAs = 101;
        data.Items[0].MarketShowAs = 102;
        DatWriter.Write(path, data, options);
        var service = new LegacyDatService();
        service.Load(path, options);

        var removed = service.RemoveThings([service.Data!.Items[1]]);

        removed.Should().Be(1);
        service.Data.Items.Select(thing => thing.Id).Should().Equal(100u, 101u);
        service.Data.ItemsMaxId.Should().Be(101);
        service.Data.Items[0].MarketTradeAs.Should().Be(0);
        service.Data.Items[0].MarketShowAs.Should().Be(101);
    }

    private static DatThingType CreateThing(uint id) => new()
    {
        Id = id,
        Category = DatThingCategory.Items,
        FrameGroups =
        {
            new DatThingFrameGroup
            {
                Width = 1,
                Height = 1,
                ExactSize = 32,
                Layers = 1,
                PatternX = 1,
                PatternY = 1,
                PatternZ = 1,
                Frames = 1,
                SpriteIds = [0]
            }
        }
    };

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "old-assets-management-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
