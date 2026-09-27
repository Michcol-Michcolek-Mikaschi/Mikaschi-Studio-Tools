using FluentAssertions;
using Modules.MapEditor.Services;
using Modules.MapEditor.ViewModels;
using Narzedzia.Core.Appearances;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;
using Narzedzia.Core.Tibia12;

namespace Modules.MapEditor.Tests;

public sealed class MapAssetServiceTests
{
    [Fact]
    public void LoadFromFolder_PreCancelledToken_StopsBeforeReplacingLoadedGeneration()
    {
        var directory = CreateMappedAssetDirectory();
        using var assets = new MapAssetService();
        assets.LoadFromFolder(directory.FullName);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var load = () => assets.LoadFromFolder(
            directory.FullName,
            expectedOtbMinorVersion: 57,
            cancellationToken: cancellation.Token);

        load.Should().Throw<OperationCanceledException>();
        assets.IsLoaded.Should().BeTrue(
            "anulowanie przed startem nie może usuwać działającej generacji assets");
        assets.ObjectCount.Should().Be(1);
        assets.GetAnimationFrameCount(100).Should().Be(7);
    }

    [Fact]
    public void PreviousGenerationRetainer_DelaysDisposalByOneReplacementAndBoundsMemory()
    {
        using var retainer = new PreviousGenerationRetainer<DisposableGeneration>();
        var first = new DisposableGeneration();
        var second = new DisposableGeneration();

        retainer.Retain(first);

        first.DisposeCount.Should().Be(0,
            "the UI may still render bitmaps from the immediately replaced asset generation");
        retainer.HasRetainedGeneration.Should().BeTrue();

        retainer.Retain(second);

        first.DisposeCount.Should().Be(1, "only the generation older than the visible one can be released");
        second.DisposeCount.Should().Be(0);

        retainer.Dispose();

        second.DisposeCount.Should().Be(1, "disposing the editor releases the last retained generation");
    }

    [Fact]
    public void CreatureTypeEx_UsesDirectClientIdInsteadOfItemsOtbServerMapping()
    {
        var directory = CreateMappedAssetDirectory();
        using var assets = new MapAssetService();
        assets.LoadFromFolder(directory.FullName);

        assets.GetAnimationFrameCount(100).Should().Be(7,
            "zwykły item mapy jest mapowany z Server ID 100 na Client ID 200");
        assets.GetCreatureAnimationFrameCount(lookType: 0, lookItem: 100).Should().Be(3,
            "typeex/lookItem w XML stworzenia jest już bezpośrednim Client ID");
    }

    [Fact]
    public async Task SpawnCenter_UsesProceduralMarkerInsteadOfClientObjectSprite()
    {
        var assetsDirectory = CreateMappedAssetDirectory();
        var mapDirectory = Directory.CreateTempSubdirectory();
        var mapPath = Path.Combine(mapDirectory.FullName, "spawn-marker.otbm");
        var map = new OtbmMap
        {
            Version = 2,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57,
            SpawnFile = "spawn-marker-spawn.xml"
        };
        map.Tiles[new(100, 100, 7)] = new OtbmTile
        {
            X = 100,
            Y = 100,
            Z = 7,
            GroundItemId = 100
        };
        map.Spawns.Add(new OtbmSpawn { CenterX = 100, CenterY = 100, CenterZ = 7, Radius = 1 });
        new OtbmWriter().Write(map, mapPath);
        OtbmExternalDataWriter.Save(map, mapPath);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadAllAsync(mapPath, assetsDirectory.FullName);

        var center = viewModel.VisibleTiles.Single(tile => tile.X == 100 && tile.Y == 100);
        center.HasSpawn.Should().BeTrue();
        center.MarkerLayers.Should().BeEmpty(
            "centrum spawnu jest rysowane niezależnym krzyżykiem i nie może używać stałego Client ID, które w custom assets bywa ścianą");
    }

    private static DirectoryInfo CreateMappedAssetDirectory()
    {
        var directory = Directory.CreateTempSubdirectory();
        new OtbParser().Save(new OtbFile
        {
            MajorVersion = 3,
            MinorVersion = 57,
            BuildNumber = 1,
            Items =
            [
                new OtbItem
                {
                    ServerId = 100,
                    ClientId = 200,
                    ItemType = OtbItemType.None,
                    SpriteHash = new byte[16]
                }
            ]
        }, Path.Combine(directory.FullName, "items.otb"));

        var appearances = new Appearances();
        appearances.Object.Add(ObjectAppearance(100, frames: 3));
        appearances.Object.Add(ObjectAppearance(200, frames: 7));
        new AppearancesReader().Write(Path.Combine(directory.FullName, "appearances.dat"), appearances);
        CatalogWriter.Write(Path.Combine(directory.FullName, "catalog-content.json"),
        [
            new CatalogEntry { Type = "appearances", File = "appearances.dat" }
        ]);
        return directory;
    }

    private static Appearance ObjectAppearance(uint id, uint frames)
    {
        var spriteInfo = new SpriteInfo
        {
            PatternWidth = 1,
            PatternHeight = 1,
            PatternLayers = 1,
            PatternX = 1,
            PatternY = 1,
            PatternZ = 1,
            PatternFrames = frames
        };
        for (var frame = 0; frame < frames; frame++) spriteInfo.SpriteId.Add(1);
        return new Appearance
        {
            Id = id,
            AppearanceType = APPEARANCE_TYPE.AppearanceObject,
            FrameGroup = { new FrameGroup { SpriteInfo = spriteInfo } }
        };
    }

    private sealed class DisposableGeneration : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
