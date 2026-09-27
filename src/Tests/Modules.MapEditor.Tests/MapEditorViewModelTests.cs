using FluentAssertions;
using Modules.MapEditor.Services;
using Modules.MapEditor.ViewModels;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.MapEditor.Tests;

public sealed class MapEditorViewModelTests
{
    [Fact]
    public async Task LoadMapAsync_LoadsAndPositionsEveryVisibleTileOnCanvas()
    {
        var map = new OtbmMap
        {
            Version = 2,
            Width = 2048,
            Height = 2048,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 63
        };
        AddTile(map, 1000, 1000, 7, 100);
        AddTile(map, 1001, 1000, 7, 101);
        AddTile(map, 1000, 1001, 7, 102);

        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "viewport.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        viewModel.SetViewportSize(8, 8);
        await viewModel.LoadMapAsync(path);

        viewModel.MapFileName.Should().Be("viewport.otbm");
        viewModel.CurrentFloor.Should().Be(7);
        viewModel.VisibleTiles.Should().HaveCount(3);
        viewModel.VisibleTiles.Select(tile => (tile.X, tile.Y)).Should().Equal(
            [(X: (ushort)1000, Y: (ushort)1000), (X: (ushort)1000, Y: (ushort)1001), (X: (ushort)1001, Y: (ushort)1000)],
            "RME rysuje kolumnami X, a wewnatrz nich wierszami Y");
        viewModel.VisibleTiles.Select(tile => (tile.CanvasX, tile.CanvasY))
            .Should().OnlyHaveUniqueItems("każdy tile musi trafić w osobne miejsce Canvas");
    }

    [Fact]
    public async Task LoadMapAsync_DoesNotRequireAssetsAndReportsMapSummary()
    {
        var map = new OtbmMap
        {
            Version = 3,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57
        };
        AddTile(map, 50, 60, 6, 100);

        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "map-only.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);

        viewModel.MapSummary.Should().Contain("1 kafelków");
        viewModel.Status.Should().Contain("mapa: 1 kafelków");
        viewModel.CanSave.Should().BeTrue();
    }

    [Fact]
    public async Task LoadMapAsync_DoesNotScanAdjacentServerCreatureFolders()
    {
        var map = new OtbmMap
        {
            Version = 3,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57
        };
        AddTile(map, 50, 60, 7, 100);

        var root = Directory.CreateTempSubdirectory();
        var data = Directory.CreateDirectory(Path.Combine(root.FullName, "data"));
        var world = Directory.CreateDirectory(Path.Combine(data.FullName, "world"));
        var monsters = Directory.CreateDirectory(Path.Combine(data.FullName, "monster"));
        var mapPath = Path.Combine(world.FullName, "isolated.otbm");
        new OtbmWriter().Write(map, mapPath);
        File.WriteAllText(Path.Combine(monsters.FullName, "mikaschi-isolation-test.xml"),
            "<monster name=\"Mikaschi Isolation Test\"><look type=\"34\"/></monster>");
        File.WriteAllText(Path.Combine(monsters.FullName, "monsters.xml"),
            "<monsters><monster name=\"Mikaschi Isolation Test\" file=\"mikaschi-isolation-test.xml\"/></monsters>");

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(mapPath);

        viewModel.CreaturePalette.Should().NotContain(item => item.Name == "Mikaschi Isolation Test");
        viewModel.Status.Should().NotContain("z serwera");
        viewModel.MaterialStatus.Should().NotContain("Serwer:");
    }

    [Fact]
    public async Task LoadMapAsync_ReportsCompletionAndBuildsMinimapWithoutBlockingTheLoader()
    {
        var map = new OtbmMap
        {
            Version = 3,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57
        };
        AddTile(map, 80, 90, 7, 100);
        AddTile(map, 81, 90, 7, 101);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "progress.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);

        viewModel.IsLoading.Should().BeFalse();
        viewModel.LoadProgress.Should().Be(100);
        viewModel.LoadProgressLabel.Should().Be("100%");
        viewModel.LoadStage.Should().Be("Wczytywanie zakończone.");

        await viewModel.RefreshMinimapCommand.ExecuteAsync(null);

        viewModel.IsMinimapLoading.Should().BeFalse();
        viewModel.MinimapProgress.Should().Be(100);
        viewModel.MinimapStatus.Should().Contain("Z=7").And.Contain("2 kafelków");

        viewModel.CurrentFloor = 6;
        await viewModel.RefreshMinimapCommand.ExecuteAsync(null);
        viewModel.IsMinimapLoading.Should().BeFalse();
        viewModel.MinimapProgress.Should().Be(100, "puste piętro także kończy kompletne zlecenie");
        viewModel.MinimapStatus.Should().Be("Brak kafelków na Z=6.");
    }

    [Fact]
    public async Task SetViewOrigin_UpdatesMinimapOverlayWithoutRebuildingFloorRaster()
    {
        var map = new OtbmMap
        {
            Version = 3,
            Width = 2048,
            Height = 2048,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57
        };
        AddTile(map, 100, 100, 7, 100);
        AddTile(map, 1100, 1100, 7, 101);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "minimap-pan.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        viewModel.SetViewportSize(8, 8);
        await viewModel.LoadMapAsync(path);
        await viewModel.RefreshMinimapCommand.ExecuteAsync(null);
        viewModel.SetViewOrigin(100, 100);
        var initialLeft = viewModel.MinimapViewportLeft;
        var initialStatus = viewModel.MinimapStatus;

        viewModel.SetViewOrigin(300, 300);

        viewModel.IsMinimapViewportVisible.Should().BeTrue();
        viewModel.MinimapViewportLeft.Should().BeGreaterThan(initialLeft);
        viewModel.IsMinimapLoading.Should().BeFalse(
            "pan aktualizuje tylko ramkę i nie uruchamia pracy dla całego piętra");
        viewModel.MinimapStatus.Should().Be(initialStatus);
        viewModel.MinimapProgress.Should().Be(100);
    }

    [Fact]
    public async Task LoadMapAsync_LeavesEditorResponsiveAndReportsErrorForMissingFile()
    {
        using var viewModel = new MapEditorViewModel();

        await viewModel.LoadMapAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".otbm"));

        viewModel.IsLoading.Should().BeFalse();
        viewModel.IsLoadProgressIndeterminate.Should().BeFalse();
        viewModel.LoadStage.Should().Be("Błąd wczytywania.");
        viewModel.Status.Should().Contain("Nie udało się wczytać danych");
    }

    [Fact]
    public async Task LoadMapAsync_CancelledBeforePublish_KeepsPreviousMapAndViewportAtomically()
    {
        var directory = Directory.CreateTempSubdirectory();
        var previousPath = Path.Combine(directory.FullName, "previous.otbm");
        var candidatePath = Path.Combine(directory.FullName, "candidate.otbm");
        var previousMap = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512,
            ItemsMajorVersion = 3, ItemsMinorVersion = 57
        };
        AddTile(previousMap, 100, 100, 7, 100);
        new OtbmWriter().Write(previousMap, previousPath);
        var candidateMap = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512,
            ItemsMajorVersion = 3, ItemsMinorVersion = 57
        };
        AddTile(candidateMap, 200, 200, 7, 200);
        new OtbmWriter().Write(candidateMap, candidatePath);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(previousPath);
        var previousVisibleTiles = viewModel.VisibleTiles;
        var cancellationRequested = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MapEditorViewModel.LoadStage) ||
                !viewModel.LoadStage.StartsWith("Publikowanie mapy", StringComparison.Ordinal)) return;
            cancellationRequested = true;
            viewModel.CancelActiveLoad();
        };

        await viewModel.LoadMapAsync(candidatePath);

        cancellationRequested.Should().BeTrue();
        viewModel.MapFileName.Should().Be("previous.otbm");
        viewModel.VisibleTiles.Should().BeSameAs(previousVisibleTiles,
            "an aborted pre-publication import must not replace the live viewport");
        viewModel.VisibleTiles.Should().ContainSingle(tile => tile.X == 100 && tile.Y == 100 && tile.GroundItemId == 100);
        viewModel.LoadStage.Should().Be("Wczytywanie anulowane.");
    }

    [Fact]
    public void PaletteNavigation_MatchesRmeOrderAndReturnsFromManagementPanel()
    {
        using var viewModel = new MapEditorViewModel();

        viewModel.PaletteSections.Should().Equal(
            "Terrain Palette", "Doodad Palette", "Collections Palette", "Item Palette",
            "House Palette", "Waypoint Palette", "Creature Palette", "RAW Palette");

        viewModel.SelectedPaletteSection = "RAW Palette";
        viewModel.SelectedWorkspaceTabIndex.Should().Be(3);
        viewModel.ActiveWorkspaceLabel.Should().StartWith("RAW Palette");

        viewModel.SelectWorkspacePanel("Tools");
        viewModel.SelectedWorkspaceTabIndex.Should().Be(14);
        viewModel.ActiveWorkspaceLabel.Should().Be("Narzędzia");

        viewModel.ActivateSelectedPalette();
        viewModel.SelectedWorkspaceTabIndex.Should().Be(3);
        viewModel.ActiveWorkspaceLabel.Should().StartWith("RAW Palette");
    }

    [Fact]
    public void PaletteNavigation_KeepsSectionTilesetAndContentSynchronized()
    {
        var cityWalls = Tileset("City Walls", RmePaletteCategory.Terrain, 100);
        var seaRaw = Tileset("Sea", RmePaletteCategory.Raw, 200);
        var seaDoodad = Tileset("Sea", RmePaletteCategory.Doodad, 300);
        using var viewModel = new MapEditorViewModel();
        viewModel.Tilesets.Add(cityWalls);
        viewModel.Tilesets.Add(seaRaw);
        viewModel.Tilesets.Add(seaDoodad);

        viewModel.SelectedPaletteSection = "Doodad Palette";

        viewModel.PaletteGroups.Should().Equal("Sea");
        viewModel.SelectedPaletteGroup.Should().Be("Sea");
        viewModel.SelectedTileset.Should().BeSameAs(seaDoodad);
        viewModel.SelectedWorkspaceTabIndex.Should().Be(1);
        viewModel.DoodadPalette.Should().ContainSingle().Which.ItemId.Should().Be(300);
        var doodadMenu = viewModel.PaletteGroups;

        viewModel.SelectedPaletteSection = "Terrain Palette";

        viewModel.PaletteGroups.Should().NotBeSameAs(doodadMenu,
            "ComboBox musi dostać nową listę i nie może zachowywać starych kontenerów Doodad Palette");
        viewModel.PaletteGroups.Should().Equal("City Walls");
        viewModel.SelectedPaletteGroup.Should().Be("City Walls");
        viewModel.SelectedTileset.Should().BeSameAs(cityWalls);
        viewModel.SelectedWorkspaceTabIndex.Should().Be(0);
        viewModel.TerrainPalette.Should().ContainSingle().Which.ItemId.Should().Be(100);

        viewModel.SelectedPaletteSection = "Doodad Palette";
        viewModel.SelectedPaletteGroup.Should().Be("Sea", "każda paleta pamięta swój ostatni tileset");
        viewModel.SelectedTileset.Should().BeSameAs(seaDoodad);
    }

    [Fact]
    public void MapEditorPreferencesStore_RestoresPaletteTilesetAndBrush()
    {
        var directory = Directory.CreateTempSubdirectory();
        var store = new MapEditorPreferencesStore(Path.Combine(directory.FullName, "map-editor.json"));
        var preferences = new MapEditorPreferences(
            "Doodad Palette",
            new Dictionary<string, string> { ["Doodad Palette"] = "Sea" },
            4,
            "Okrąg",
            false);

        store.TrySave(preferences).Should().BeTrue();
        var restored = store.Load();
        using var viewModel = new MapEditorViewModel(restored);

        viewModel.SelectedPaletteSection.Should().Be("Doodad Palette");
        viewModel.SelectedPaletteGroup.Should().Be("Sea");
        viewModel.SelectedWorkspaceTabIndex.Should().Be(1);
        viewModel.BrushSize.Should().Be(4);
        viewModel.BrushShape.Should().Be("Okrąg");
        viewModel.Automagic.Should().BeFalse();
    }

    [Fact]
    public async Task HousePaletteNavigation_GroupsHousesByTownLikeRme()
    {
        var map = new OtbmMap
        {
            Version = 2,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57,
            HouseFile = "houses.xml"
        };
        map.Towns.Add(new OtbmTown { Id = 1, Name = "Konoha", TempleX = 100, TempleY = 100, TempleZ = 7 });
        map.Towns.Add(new OtbmTown { Id = 2, Name = "Suna", TempleX = 200, TempleY = 200, TempleZ = 7 });
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "house-navigation.otbm");
        new OtbmWriter().Write(map, path);
        File.WriteAllText(Path.Combine(directory.FullName, "houses.xml"),
            "<houses>" +
            "<house name=\"Konoha 1\" houseid=\"11\" townid=\"1\" entryx=\"100\" entryy=\"100\" entryz=\"7\"/>" +
            "<house name=\"Suna 1\" houseid=\"21\" townid=\"2\" entryx=\"200\" entryy=\"200\" entryz=\"7\"/>" +
            "</houses>");

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);
        viewModel.SelectedPaletteSection = "House Palette";

        viewModel.PaletteGroups.Should().Equal("Konoha", "Suna");
        viewModel.SelectedPaletteGroup = "Suna";
        viewModel.HousePalette.Should().ContainSingle()
            .Which.Name.Should().Contain("Suna 1");
    }

    [Fact]
    public async Task HoverWithoutBrush_DoesNotRebuildViewport()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "hover.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);
        var visibleTiles = viewModel.VisibleTiles;
        var rebuilds = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapEditorViewModel.VisibleTiles)) rebuilds++;
        };

        viewModel.SetHoveredTile(100, 100);
        viewModel.SetHoveredTile(100, 100);

        viewModel.VisibleTiles.Should().BeSameAs(visibleTiles);
        rebuilds.Should().Be(0, "zwykły ruch kursora nie zmienia obrazu mapy");
    }

    [Fact]
    public async Task SetViewOrigin_RebuildsViewportOnlyOnceForBothAxes()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "pan.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);
        var rebuilds = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapEditorViewModel.VisibleTiles)) rebuilds++;
        };

        viewModel.SetViewOrigin(120, 130);

        viewModel.ViewX.Should().Be(120);
        viewModel.ViewY.Should().Be(130);
        rebuilds.Should().Be(1, "przesunięcie obu osi jest jedną operacją renderera");
    }

    [Fact]
    public async Task ZoomAt_PreservesMapTileUnderCursorAndRebuildsOnlyOnce()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 112, 209, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "zoom-anchor.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);
        viewModel.SetViewOrigin(100, 200);
        viewModel.SetViewportSize(25, 19);
        var pointerX = 400d;
        var pointerY = 300d;
        var beforeX = viewModel.ViewX + (int)Math.Floor(pointerX / viewModel.TileSize);
        var beforeY = viewModel.ViewY + (int)Math.Floor(pointerY / viewModel.TileSize);
        var rebuilds = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapEditorViewModel.VisibleTiles)) rebuilds++;
        };

        viewModel.ZoomAt(pointerX, pointerY, 800, 600, zoomIn: true);

        viewModel.TileSize.Should().Be(40);
        (viewModel.ViewX + (int)Math.Floor(pointerX / viewModel.TileSize)).Should().Be(beforeX);
        (viewModel.ViewY + (int)Math.Floor(pointerY / viewModel.TileSize)).Should().Be(beforeY);
        viewModel.VisibleColumns.Should().Be(20);
        viewModel.VisibleRows.Should().Be(15);
        rebuilds.Should().Be(1, "zoom pod kursorem jest jedna atomowa operacja renderera");
    }

    [Fact]
    public async Task PanBy_ClampsCoordinatesAndRebuildsOnlyOnce()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "keyboard-pan.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);
        viewModel.SetViewOrigin(5, 6);
        var rebuilds = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapEditorViewModel.VisibleTiles)) rebuilds++;
        };

        viewModel.PanBy(-10, 3);

        viewModel.ViewX.Should().Be(0);
        viewModel.ViewY.Should().Be(9);
        rebuilds.Should().Be(1);
    }

    [Fact]
    public async Task PanBy_ReusesTileContentForOverlappingViewportArea()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        for (ushort x = 100; x < 112; x++)
        for (ushort y = 100; y < 112; y++)
            AddTile(map, x, y, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "pan-cache.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        viewModel.SetViewportSize(8, 8);
        await viewModel.LoadMapAsync(path);
        viewModel.SetViewOrigin(100, 100);
        var temporarilyHiddenTile = viewModel.VisibleTiles.Single(tile => tile.X == 100 && tile.Y == 102);
        var overlappingTile = viewModel.VisibleTiles.Single(tile => tile.X == 102 && tile.Y == 102);
        var previousCanvasX = overlappingTile.CanvasX;

        viewModel.PanBy(1, 0);

        viewModel.VisibleTiles.Single(tile => tile.X == 102 && tile.Y == 102)
            .Should().BeSameAs(overlappingTile, "przesuwanie nie powinno ponownie dekodować niezmienionych kafelków");
        overlappingTile.CanvasX.Should().Be(previousCanvasX - viewModel.TileSize);

        viewModel.PanBy(-1, 0);
        viewModel.VisibleTiles.Single(tile => tile.X == 100 && tile.Y == 102)
            .Should().BeSameAs(temporarilyHiddenTile,
                "krótki powrót powinien korzystać z marginesu cache poza ekranem");
    }

    [Fact]
    public async Task NonLightTileEdit_InvalidatesChangedTileButReusesNearbyCachedTile()
    {
        var map = new OtbmMap
        {
            Version = 2,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57
        };
        for (ushort x = 100; x < 108; x++)
        for (ushort y = 100; y < 108; y++)
            AddTile(map, x, y, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "non-light-cache-invalidation.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        viewModel.SetViewportSize(8, 8);
        await viewModel.LoadMapAsync(path);
        viewModel.SetViewOrigin(100, 100);
        viewModel.ShowLights = true;
        viewModel.Automagic = false;
        viewModel.SelectedBrush = new MapPaletteItem { ItemId = 200, Name = "RAW #200" };
        var changedTile = viewModel.VisibleTiles.Single(tile => tile.X == 101 && tile.Y == 101);
        var nearbyUnchangedTile = viewModel.VisibleTiles.Single(tile => tile.X == 105 && tile.Y == 101);

        viewModel.BeginTileStroke(erasing: false).Should().BeTrue();
        viewModel.PlaceBrushAt(101, 101);
        viewModel.FlushTileStrokeViewport();
        viewModel.EndTileStroke();

        var refreshedChangedTile = viewModel.VisibleTiles.Single(tile => tile.X == 101 && tile.Y == 101);
        refreshedChangedTile.Should().NotBeSameAs(changedTile,
            "zmienione pole musi zostać przebudowane w cache");
        refreshedChangedTile.ItemCount.Should().Be(1);
        viewModel.VisibleTiles.Single(tile => tile.X == 105 && tile.Y == 101)
            .Should().BeSameAs(nearbyUnchangedTile,
                "edycja bez zmiany źródła światła nie może unieważniać sąsiednich kafelków promieniem światła");
    }

    [Fact]
    public async Task BrushHover_UpdatesOnlyLightweightPreviewOverlay()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "brush-preview-overlay.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);
        viewModel.SelectedBrush = new MapPaletteItem { ItemId = 200, Name = "RAW #200" };
        var visibleTiles = viewModel.VisibleTiles;
        var rebuilds = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapEditorViewModel.VisibleTiles)) rebuilds++;
        };

        viewModel.SetHoveredTile(100, 100);
        var firstPreviewX = viewModel.VisibleBrushPreview.Should().ContainSingle().Subject.CanvasX;
        viewModel.SetHoveredTile(101, 100);

        viewModel.VisibleTiles.Should().BeSameAs(visibleTiles);
        viewModel.VisibleBrushPreview.Should().ContainSingle().Which.CanvasX
            .Should().Be(firstPreviewX + viewModel.TileSize);
        rebuilds.Should().Be(0, "ruch podglądu pędzla nie może przebudowywać mapy");
    }

    [Fact]
    public async Task TileStroke_RefreshesViewportOncePerInputBatch()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "stroke-render-batch.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);
        viewModel.SelectedBrush = new MapPaletteItem { ItemId = 200, Name = "RAW #200" };
        var rebuilds = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapEditorViewModel.VisibleTiles)) rebuilds++;
        };

        viewModel.BeginTileStroke(erasing: false).Should().BeTrue();
        viewModel.PlaceBrushPath([(100, 100), (101, 100), (102, 100)]);
        rebuilds.Should().Be(0, "pola jednego odcinka myszy są grupowane przed renderem");
        viewModel.FlushTileStrokeViewport();
        viewModel.EndTileStroke();

        rebuilds.Should().Be(1);
        viewModel.VisibleTiles.Should().Contain(tile => tile.X == 102 && tile.Y == 100);
    }

    [Fact]
    public async Task TileEdit_CanBeUndoneAndRedoneWithoutLosingDocumentState()
    {
        var map = new OtbmMap
        {
            Version = 2,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57
        };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "history.otbm");
        new OtbmWriter().Write(map, path);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);
        viewModel.SelectedBrush = new MapPaletteItem { ItemId = 200 };

        viewModel.PlaceBrushAt(100, 100);
        viewModel.IsDirty.Should().BeTrue();
        viewModel.CanUndo.Should().BeTrue();

        viewModel.UndoCommand.Execute(null);
        viewModel.IsDirty.Should().BeFalse("cofnięto do ostatnio zapisanego stanu");
        viewModel.CanRedo.Should().BeTrue();
        viewModel.SetHoveredTile(100, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=0");

        viewModel.RedoCommand.Execute(null);
        viewModel.IsDirty.Should().BeTrue();
        viewModel.SetHoveredTile(100, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=1");
    }

    [Fact]
    public async Task SaveAs_CopiesReferencedRmeHouseAndSpawnFiles()
    {
        var sourceDirectory = Directory.CreateTempSubdirectory();
        var destinationDirectory = Directory.CreateTempSubdirectory();
        var sourcePath = Path.Combine(sourceDirectory.FullName, "source.otbm");
        var destinationPath = Path.Combine(destinationDirectory.FullName, "copy.otbm");
        var map = new OtbmMap
        {
            Version = 2,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57,
            SpawnFile = "source-spawn.xml",
            HouseFile = "source-house.xml"
        };
        AddTile(map, 100, 100, 7, 100);
        new OtbmWriter().Write(map, sourcePath);
        File.WriteAllText(Path.Combine(sourceDirectory.FullName, map.SpawnFile), "<spawns />");
        File.WriteAllText(Path.Combine(sourceDirectory.FullName, map.HouseFile), "<houses />");

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(sourcePath);
        await viewModel.SaveMapAsync(destinationPath);

        File.Exists(destinationPath).Should().BeTrue();
        File.ReadAllText(Path.Combine(destinationDirectory.FullName, map.SpawnFile)).Should().Be("<spawns />");
        File.ReadAllText(Path.Combine(destinationDirectory.FullName, map.HouseFile)).Should().Be("<houses />");
        viewModel.Status.Should().Contain("2 pliki XML RME");
    }

    [Fact]
    public async Task CompositeRmeBrush_IsAppliedAndUndoneAsOneOperation()
    {
        var map = new OtbmMap
        {
            Version = 2,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57
        };
        AddTile(map, 100, 100, 7, 100);
        AddTile(map, 101, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var path = Path.Combine(directory.FullName, "composite.otbm");
        new OtbmWriter().Write(map, path);
        var brush = new RmeBrushDefinition(
            "broken tree", "doodad", 200, false, false, 0, 0,
            [new RmeBrushAlternative([], [new RmeWeightedComposite(1,
                [new RmeCompositeTile(0, 0, 0, [200]), new RmeCompositeTile(1, 0, 0, [201])])])]);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(path);
        viewModel.SelectedBrush = new MapPaletteItem
        {
            ItemId = 200,
            Name = "broken tree",
            Category = RmePaletteCategory.Doodad,
            Brush = brush
        };

        viewModel.PlaceBrushAt(100, 100);
        viewModel.SetHoveredTile(101, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=1");

        viewModel.UndoCommand.Execute(null);
        viewModel.SetHoveredTile(101, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=0");
        viewModel.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task HousePalette_PaintsARealOtbmHouseTile()
    {
        var map = new OtbmMap
        {
            Version = 2,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57,
            HouseFile = "houses.xml"
        };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "house-source.otbm");
        var saved = Path.Combine(directory.FullName, "house-saved.otbm");
        new OtbmWriter().Write(map, source);
        File.WriteAllText(Path.Combine(directory.FullName, "houses.xml"),
            "<houses><house name=\"Test House\" houseid=\"42\" entryx=\"100\" entryy=\"100\" entryz=\"7\"/></houses>");

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectedBrush = viewModel.HousePalette.Should().ContainSingle().Subject;
        viewModel.PlaceBrushAt(100, 100);
        await viewModel.SaveMapAsync(saved);

        var savedMap = new OtbmReader().Read(saved);
        savedMap.Tiles[new(100, 100, 7)].IsHouseTile.Should().BeTrue();
        savedMap.Tiles[new(100, 100, 7)].HouseId.Should().Be(42);
    }

    [Fact]
    public async Task HouseBrush_DragEraseAndUndoOperateAsSingleRmeStroke()
    {
        var map = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57,
            HouseFile = "houses.xml"
        };
        AddTile(map, 100, 100, 7, 100);
        AddTile(map, 101, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "house-stroke.otbm");
        new OtbmWriter().Write(map, source);
        File.WriteAllText(Path.Combine(directory.FullName, "houses.xml"),
            "<houses><house name=\"Test House\" houseid=\"42\" entryx=\"100\" entryy=\"100\" entryz=\"7\"/></houses>");

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectedBrush = viewModel.HousePalette.Should().ContainSingle().Subject;
        viewModel.BeginTileStroke(erasing: false).Should().BeTrue();
        viewModel.PlaceBrushAt(100, 100);
        viewModel.PlaceBrushAt(101, 100);
        viewModel.EndTileStroke();
        viewModel.VisibleTiles.Count(tile => tile.IsHouseTile).Should().Be(2);

        viewModel.UndoCommand.Execute(null);
        viewModel.VisibleTiles.Should().OnlyContain(tile => !tile.IsHouseTile);
        viewModel.RedoCommand.Execute(null);
        viewModel.BeginTileStroke(erasing: true).Should().BeTrue();
        viewModel.EraseAt(100, 100);
        viewModel.EraseAt(101, 100);
        viewModel.EndTileStroke();
        viewModel.VisibleTiles.Should().OnlyContain(tile => !tile.IsHouseTile);
    }

    [Fact]
    public async Task WaypointPalette_MovesMarkerAndSupportsUndo()
    {
        var map = new OtbmMap
        {
            Version = 2,
            Width = 512,
            Height = 512,
            ItemsMajorVersion = 3,
            ItemsMinorVersion = 57
        };
        AddTile(map, 100, 100, 7, 100);
        map.Waypoints.Add(new OtbmWaypoint { Name = "Temple", X = 100, Y = 100, Z = 7 });
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "waypoint.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectedBrush = viewModel.WaypointPalette.Should().ContainSingle().Subject;
        viewModel.PlaceBrushAt(101, 100);

        viewModel.VisibleTiles.Single(tile => tile.WaypointNames == "Temple").X.Should().Be(101);
        viewModel.UndoCommand.Execute(null);
        viewModel.VisibleTiles.Single(tile => tile.WaypointNames == "Temple").X.Should().Be(100);
        viewModel.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task RawBrushEraser_RemovesOnlyMatchingIdsAndSupportsUndo()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        map.Tiles[new(100, 100, 7)].Items.AddRange([
            new OtbmItem { Id = 200 }, new OtbmItem { Id = 201 }, new OtbmItem { Id = 200 }
        ]);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "raw-eraser.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectedBrush = new MapPaletteItem { ItemId = 200, Name = "RAW #200" };
        viewModel.EraseAt(100, 100);

        viewModel.SetHoveredTile(100, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=1");
        viewModel.UndoCommand.Execute(null);
        viewModel.SetHoveredTile(100, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=3");
    }

    [Fact]
    public async Task ErasePath_GroupsWholeMouseSegmentIntoOneRefreshAndOneUndo()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        for (ushort x = 100; x <= 102; x++)
        {
            AddTile(map, x, 100, 7, 100);
            map.Tiles[new(x, 100, 7)].Items.Add(new OtbmItem { Id = 200 });
        }
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "eraser-path.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectedBrush = new MapPaletteItem { ItemId = 200, Name = "RAW #200" };
        var viewportRefreshes = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MapEditorViewModel.VisibleTiles)) viewportRefreshes++;
        };

        viewModel.BeginTileStroke(erasing: true).Should().BeTrue();
        viewModel.ErasePath([(100, 100), (101, 100), (102, 100)]);
        viewportRefreshes.Should().Be(0, "cały odcinek gumki jest mutowany przed renderem");
        viewModel.FlushTileStrokeViewport();
        viewModel.EndTileStroke();

        viewportRefreshes.Should().Be(1);
        foreach (ushort x in new ushort[] { 100, 101, 102 })
        {
            viewModel.SetHoveredTile(x, 100);
            viewModel.HoveredTileLabel.Should().Contain("items=0");
        }

        viewModel.UndoCommand.Execute(null);
        viewModel.CanUndo.Should().BeFalse("cały odcinek gumki ma tworzyć jedną operację historii");
        foreach (ushort x in new ushort[] { 100, 101, 102 })
        {
            viewModel.SetHoveredTile(x, 100);
            viewModel.HoveredTileLabel.Should().Contain("items=1");
        }
    }

    [Fact]
    public async Task ErasePath_PreservesRepeatedTileSemanticsAndUndoRestoresWholeStack()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        map.Tiles[new(100, 100, 7)].Items.AddRange([
            new OtbmItem { Id = 200 }, new OtbmItem { Id = 201 }, new OtbmItem { Id = 202 }
        ]);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "eraser-repeat.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectedBrush = null;

        viewModel.ErasePath([(100, 100), (100, 100)]);
        viewModel.SetHoveredTile(100, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=1");

        viewModel.UndoCommand.Execute(null);
        viewModel.SetHoveredTile(100, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=3");
    }

    [Fact]
    public void RenderDiagnostics_ExposeFpsRenderAndViewportPreparationTimes()
    {
        using var viewModel = new MapEditorViewModel();

        viewModel.UpdateRenderDiagnostics(59.94, 2.345, 30);

        viewModel.RenderFramesPerSecond.Should().BeApproximately(59.94, 0.001);
        viewModel.AverageRenderMilliseconds.Should().BeApproximately(2.345, 0.001);
        viewModel.RenderSampleFrames.Should().Be(30);
        viewModel.RenderDiagnosticsLabel.Should().Contain("FPS: 59").And.Contain("render:").And.Contain("widok:");
    }

    [Fact]
    public async Task CreatureBrush_AutoCreatesLocalSpawnAndSavesExternalXmlWithUndo()
    {
        var map = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57,
            SpawnFile = "creatures-spawn.xml"
        };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "creature-source.otbm");
        var saved = Path.Combine(directory.FullName, "creature-saved.otbm");
        new OtbmWriter().Write(map, source);
        File.WriteAllText(Path.Combine(directory.FullName, map.SpawnFile), "<spawns />");

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectedBrush = new MapPaletteItem
        {
            Name = "Dragon",
            EntityName = "Dragon",
            EntityKind = MapPaletteEntityKind.Creature,
            CreatureIsNpc = false,
            Category = RmePaletteCategory.Creature
        };
        viewModel.PlaceBrushAt(100, 100);
        viewModel.VisibleTiles.Single(tile => tile.X == 100 && tile.Y == 100).MonsterCount.Should().Be(1);

        viewModel.UndoCommand.Execute(null);
        viewModel.VisibleTiles.Single(tile => tile.X == 100 && tile.Y == 100).MonsterCount.Should().Be(0);
        viewModel.RedoCommand.Execute(null);
        await viewModel.SaveMapAsync(saved);

        var reloaded = new OtbmReader().Read(saved);
        OtbmExternalDataReader.Load(reloaded, saved).Should().BeEmpty();
        reloaded.Spawns.Should().ContainSingle().Which.Creatures.Should().ContainSingle()
            .Which.Name.Should().Be("Dragon");
    }

    [Fact]
    public async Task SpawnRadius_IsRenderedAsAVisibleZoneIncludingEmptyTiles()
    {
        var map = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57,
            SpawnFile = "spawns.xml"
        };
        AddTile(map, 100, 100, 7, 100);
        map.Spawns.Add(new OtbmSpawn { CenterX = 100, CenterY = 100, CenterZ = 7, Radius = 2 });
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "spawn-zone.otbm");
        new OtbmWriter().Write(map, source);
        OtbmExternalDataWriter.Save(map, source);

        using var viewModel = new MapEditorViewModel();
        viewModel.SetViewportSize(10, 10);
        await viewModel.LoadMapAsync(source);

        viewModel.VisibleTiles.Should().Contain(tile => tile.X == 98 && tile.Y == 100 && tile.IsSpawnZone);
        viewModel.VisibleTiles.Should().ContainSingle(tile => tile.X == 100 && tile.Y == 100 && tile.HasSpawn);
    }

    [Fact]
    public async Task ZoneBrush_PaintsExistingTilesAndUndoRestoresFlags()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "zone-brush.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectMapTool("ProtectionZone");
        viewModel.PlaceBrushAt(100, 100);

        viewModel.VisibleTiles.Single(tile => tile.X == 100 && tile.Y == 100).IsProtectionZone.Should().BeTrue();
        viewModel.UndoCommand.Execute(null);
        viewModel.VisibleTiles.Single(tile => tile.X == 100 && tile.Y == 100).IsProtectionZone.Should().BeFalse();
    }

    [Fact]
    public async Task TileStroke_GroupsMultiplePlacementsIntoOneUndoOperation()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "stroke.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectedBrush = new MapPaletteItem { ItemId = 200, Name = "RAW #200" };

        viewModel.BeginTileStroke(erasing: false).Should().BeTrue();
        viewModel.PlaceBrushPath([(100, 100), (101, 100), (102, 100)]);
        viewModel.EndTileStroke();

        viewModel.CanUndo.Should().BeTrue();
        viewModel.UndoCommand.Execute(null);
        viewModel.CanUndo.Should().BeFalse("całe przeciągnięcie jest jedną operacją");
        viewModel.SetHoveredTile(100, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=0");
        viewModel.SetHoveredTile(101, 100);
        viewModel.HoveredTileLabel.Should().Contain("(puste)");

        viewModel.RedoCommand.Execute(null);
        viewModel.SetHoveredTile(100, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=1");
        viewModel.SetHoveredTile(101, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=1");
        viewModel.SetHoveredTile(102, 100);
        viewModel.HoveredTileLabel.Should().Contain("items=1");
    }

    [Fact]
    public async Task ItemProperties_ValidateSaveRoundTripAndSupportUndoRedo()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        map.Tiles[new(100, 100, 7)].Items.Add(new OtbmItem
        {
            Id = 200, ActionId = 100, RuneCharges = 2, Duration = 500,
            DecayingState = 1, WrittenDate = 1000, WrittenBy = "RME",
            SleeperGuid = 20, SleepStart = 30, Charges = 40,
            PodiumOutfit = new byte[15]
        });
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "properties-source.otbm");
        var saved = Path.Combine(directory.FullName, "properties-saved.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SetHoveredTile(100, 100);
        viewModel.OpenHoveredTilePropertiesCommand.Execute(null);
        viewModel.SelectedPropertyItem.Should().NotBeNull();
        viewModel.PropertyActionId = 450;
        viewModel.PropertyUniqueId = 1200;
        viewModel.PropertyCount = 7;
        viewModel.PropertyText = "RME text";
        viewModel.PropertyDescription = "RME description";
        viewModel.PropertyDepotId = 4;
        viewModel.PropertyHouseDoorId = 2;
        viewModel.PropertyTier = 3;
        viewModel.PropertyRuneCharges = 5;
        viewModel.PropertyDuration = 6000;
        viewModel.PropertyDecayingState = 2;
        viewModel.PropertyWrittenDate = 1_700_000_000;
        viewModel.PropertyWrittenBy = "Michał";
        viewModel.PropertySleeperGuid = 55;
        viewModel.PropertySleepStart = 66;
        viewModel.PropertyCharges = 77;
        viewModel.PropertyHasPodiumOutfit = true;
        viewModel.PropertyPodiumShowOutfit = true;
        viewModel.PropertyPodiumDirection = 3;
        viewModel.PropertyPodiumLookType = 128;
        viewModel.PropertyPodiumLookHead = 10;
        viewModel.PropertyTeleportX = 321;
        viewModel.PropertyTeleportY = 654;
        viewModel.PropertyTeleportZ = 7;
        viewModel.ApplyItemPropertiesCommand.Execute(null);

        viewModel.CanUndo.Should().BeTrue();
        viewModel.UndoCommand.Execute(null);
        viewModel.PropertyActionId.Should().Be(100);
        viewModel.RedoCommand.Execute(null);
        viewModel.PropertyActionId.Should().Be(450);
        await viewModel.SaveMapAsync(saved);

        var item = new OtbmReader().Read(saved).Tiles[new(100, 100, 7)].Items.Single();
        item.ActionId.Should().Be(450);
        item.UniqueId.Should().Be(1200);
        item.Count.Should().Be(7);
        item.Text.Should().Be("RME text");
        item.Description.Should().Be("RME description");
        item.DepotId.Should().Be(4);
        item.HouseDoorId.Should().Be(2);
        item.Tier.Should().Be(3);
        item.RuneCharges.Should().Be(5);
        item.Duration.Should().Be(6000);
        item.DecayingState.Should().Be(2);
        item.WrittenDate.Should().Be(1_700_000_000);
        item.WrittenBy.Should().Be("Michał");
        item.SleeperGuid.Should().Be(55);
        item.SleepStart.Should().Be(66);
        item.Charges.Should().Be(77);
        item.PodiumOutfit.Should().NotBeNull().And.HaveCount(15);
        item.PodiumOutfit![0].Should().Be(2);
        item.PodiumOutfit[1].Should().Be(3);
        BitConverter.ToUInt16(item.PodiumOutfit, 2).Should().Be(128);
        (item.TeleportX, item.TeleportY, item.TeleportZ).Should().Be(((ushort)321, (ushort)654, (byte)7));
    }

    [Fact]
    public async Task ItemProperties_EditTypedOtbm4CustomAttributesAndUndo()
    {
        var map = new OtbmMap { Version = 4, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 74 };
        AddTile(map, 100, 100, 7, 100);
        var item = new OtbmItem { Id = 200, ActionId = 100 };
        item.CustomAttributes.Add(new OtbmCustomAttribute
            { Key = "owner", Type = OtbmCustomAttributeType.String, StringValue = "RME" });
        map.Tiles[new(100, 100, 7)].Items.Add(item);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "custom-source.otbm");
        var saved = Path.Combine(directory.FullName, "custom-saved.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SetHoveredTile(100, 100);
        viewModel.OpenHoveredTilePropertiesCommand.Execute(null);
        viewModel.PropertyCustomAttributes.Should().ContainSingle(attribute =>
            attribute.Key == "owner" && attribute.Value == "RME");
        viewModel.MapVersion = 3;
        viewModel.ApplyMapPropertiesCommand.Execute(null);
        viewModel.Status.Should().Contain("zablokowana").And.Contain("OTBM 4");
        viewModel.MapVersion = 4;
        viewModel.PropertyCustomAttributes[0].Value = "Michał";
        viewModel.PropertyCustomAttributes.Add(new MapCustomAttributeItem
            { Key = "level", Type = "Integer", Value = "42" });
        viewModel.ApplyItemPropertiesCommand.Execute(null);
        viewModel.UndoCommand.Execute(null);
        viewModel.PropertyCustomAttributes.Should().ContainSingle(attribute => attribute.Value == "RME");
        viewModel.RedoCommand.Execute(null);
        await viewModel.SaveMapAsync(saved);

        var loaded = new OtbmReader().Read(saved).Tiles[new(100, 100, 7)].Items.Single();
        loaded.CustomAttributes.Should().Contain(attribute =>
            attribute.Key == "owner" && attribute.StringValue == "Michał");
        loaded.CustomAttributes.Should().Contain(attribute =>
            attribute.Key == "level" && attribute.IntegerValue == 42);
        loaded.ActionId.Should().Be(100);
    }

    [Fact]
    public async Task NewMapAndMapProperties_CreateSaveAndUndoRmeMetadata()
    {
        var directory = Directory.CreateTempSubdirectory();
        var saved = Path.Combine(directory.FullName, "new-map.otbm");
        using var viewModel = new MapEditorViewModel
        {
            MapWidth = 1024,
            MapHeight = 768,
            MapVersion = 3,
            MapItemsMajorVersion = 3,
            MapItemsMinorVersion = 74,
            MapDescription = "Nowa mapa RME",
            MapHouseFile = "new-house.xml",
            MapSpawnFile = "new-spawn.xml"
        };

        viewModel.NewMapCommand.Execute(null);
        viewModel.CanSave.Should().BeTrue();
        viewModel.MapSummary.Should().Contain("1024×768").And.Contain("items 3.74");

        viewModel.MapWidth = 2048;
        viewModel.MapDescription = "Zmieniony opis";
        viewModel.ApplyMapPropertiesCommand.Execute(null);
        viewModel.CanUndo.Should().BeTrue();
        viewModel.UndoCommand.Execute(null);
        viewModel.MapWidth.Should().Be(1024);
        viewModel.MapDescription.Should().Be("Nowa mapa RME");
        viewModel.RedoCommand.Execute(null);
        await viewModel.SaveMapAsync(saved);

        var reloaded = new OtbmReader().Read(saved);
        reloaded.Width.Should().Be(2048);
        reloaded.Height.Should().Be(768);
        reloaded.Version.Should().Be(3);
        reloaded.ItemsMinorVersion.Should().Be(74);
        reloaded.Description.Should().Be("Zmieniony opis");
        reloaded.HouseFile.Should().Be("new-house.xml");
        reloaded.SpawnFile.Should().Be("new-spawn.xml");
    }

    [Fact]
    public async Task SelectionClipboard_CopiesCutsPastesAndUndoesTilesAndSpawnsAsOneEdit()
    {
        var map = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512,
            ItemsMajorVersion = 3, ItemsMinorVersion = 57,
            SpawnFile = "spawns.xml"
        };
        AddTile(map, 100, 100, 7, 4526);
        map.Tiles[new(101, 100, 7)] = new OtbmTile { X = 101, Y = 100, Z = 7 };
        map.Tiles[new(101, 100, 7)].Items.Add(new OtbmItem { Id = 2160, Count = 2 });
        var spawn = new OtbmSpawn { CenterX = 100, CenterY = 100, CenterZ = 7, Radius = 3 };
        spawn.Creatures.Add(new OtbmCreature
        {
            Name = "Rat", X = 101, Y = 100, Z = 7, SpawnTime = 60, Direction = 2
        });
        map.Spawns.Add(spawn);

        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "selection-source.otbm");
        var pastedPath = Path.Combine(directory.FullName, "selection-pasted.otbm");
        var undoPath = Path.Combine(directory.FullName, "selection-undo.otbm");
        new OtbmWriter().Write(map, source);
        OtbmExternalDataWriter.Save(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.BeginSelection(100, 100, additive: false);
        viewModel.UpdateSelection(101, 100);
        viewModel.EndSelection();
        viewModel.CopySelectionCommand.Execute(null);
        viewModel.HasClipboard.Should().BeTrue();
        viewModel.PasteSelectionAt(200, 220, 7);
        await viewModel.SaveMapAsync(pastedPath);

        var pasted = new OtbmReader().Read(pastedPath);
        OtbmExternalDataReader.Load(pasted, pastedPath).Should().BeEmpty();
        pasted.Tiles[new(200, 220, 7)].GroundItemId.Should().Be(4526);
        pasted.Tiles[new(201, 220, 7)].Items.Single().Id.Should().Be(2160);
        pasted.Spawns.Should().ContainSingle(candidate =>
            candidate.CenterX == 200 && candidate.CenterY == 220 && candidate.CenterZ == 7);
        pasted.Spawns.Single(candidate => candidate.CenterX == 200 && candidate.CenterY == 220)
            .Creatures.Should().ContainSingle(candidate => candidate.X == 201 && candidate.Y == 220);

        viewModel.UndoCommand.Execute(null);
        await viewModel.SaveMapAsync(undoPath);
        var undone = new OtbmReader().Read(undoPath);
        OtbmExternalDataReader.Load(undone, undoPath).Should().BeEmpty();
        undone.Tiles.Should().NotContainKey(new OtbmTileCoord(200, 220, 7));
        undone.Tiles.Should().NotContainKey(new OtbmTileCoord(201, 220, 7));
        undone.Spawns.Should().NotContain(candidate => candidate.CenterX == 200 && candidate.CenterY == 220);
    }

    [Fact]
    public async Task Selection_VisibleFloorsAndCompensationMatchRmeGeometry()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 5, 100);
        AddTile(map, 99, 99, 6, 101);
        AddTile(map, 98, 98, 7, 102);
        AddTile(map, 98, 98, 8, 103);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "selection-floors.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.CurrentFloor = 5;
        viewModel.ShowAllFloors = true;
        viewModel.SelectionFloorMode = "Visible Floors";
        viewModel.CompensateSelection = true;
        viewModel.BeginSelection(100, 100, additive: false);
        viewModel.EndSelection();
        viewModel.SelectionLabel.Should().Be("Zaznaczenie: 3 pól", "RME pokazuje na powierzchni warstwy od bieżącej do Z=7");
        viewModel.CopySelectionCommand.Execute(null);
        viewModel.PasteSelectionAt(200, 200, 5);
        viewModel.SetHoveredTile(202, 202);
        viewModel.HoveredTileLabel.Should().Contain("ground=#100");
        viewModel.CurrentFloor = 6;
        viewModel.SetHoveredTile(201, 201);
        viewModel.HoveredTileLabel.Should().Contain("ground=#101");
        viewModel.CurrentFloor = 7;
        viewModel.SetHoveredTile(200, 200);
        viewModel.HoveredTileLabel.Should().Contain("ground=#102");

        viewModel.CurrentFloor = 8;
        viewModel.BeginSelection(98, 98, additive: false);
        viewModel.EndSelection();
        viewModel.SelectionLabel.Should().Be("Zaznaczenie: 1 pól",
            "geometria obejmuje trzy piętra, ale RME zaznacza wyłącznie istniejące tile");
    }

    [Fact]
    public async Task Selection_SelectsOnlyExistingTilesAndShowsDarkOverlayTargets()
    {
        var map = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512,
            ItemsMajorVersion = 3, ItemsMinorVersion = 57
        };
        AddTile(map, 100, 100, 7, 100);
        AddTile(map, 102, 100, 7, 102);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "selection-existing-only.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.BeginSelection(100, 100, additive: false);
        viewModel.UpdateSelection(102, 100);
        viewModel.EndSelection();

        viewModel.SelectionLabel.Should().Be("Zaznaczenie: 2 pól");
        viewModel.VisibleTiles.Where(tile => tile.IsSelected)
            .Select(tile => (tile.X, tile.Y, tile.Z))
            .Should().BeEquivalentTo([(100, 100, 7), (102, 100, 7)]);
        viewModel.IsTileSelected(101, 100).Should().BeFalse("puste pole nie może wejść do zaznaczenia");
    }

    [Fact]
    public async Task SelectionMove_MovesCompleteTilesAndEntitiesAsOneUndoableRmeOperation()
    {
        var map = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512,
            ItemsMajorVersion = 3, ItemsMinorVersion = 57,
            HouseFile = "houses.xml", SpawnFile = "spawns.xml"
        };
        AddTile(map, 100, 100, 7, 4526);
        var sourceGround = map.Tiles[new(100, 100, 7)];
        sourceGround.Flags = 0x01;
        sourceGround.IsHouseTile = true;
        sourceGround.HouseId = 7;
        sourceGround.Items.Add(new OtbmItem { Id = 2160, Count = 2 });

        var sourceItems = new OtbmTile { X = 101, Y = 100, Z = 7 };
        sourceItems.Items.Add(new OtbmItem { Id = 300 });
        map.Tiles[new(101, 100, 7)] = sourceItems;
        AddTile(map, 102, 100, 7, 500);
        map.Tiles[new(102, 100, 7)].Items.Add(new OtbmItem { Id = 501 });
        AddTile(map, 103, 100, 7, 600);
        map.Tiles[new(103, 100, 7)].Items.Add(new OtbmItem { Id = 601 });

        map.Towns.Add(new OtbmTown { Id = 1, Name = "Town", TempleX = 100, TempleY = 100, TempleZ = 7 });
        map.Houses.Add(new OtbmHouse
        {
            Id = 7, Name = "House", EntryX = 100, EntryY = 100, EntryZ = 7, TownId = 1
        });
        map.Waypoints.Add(new OtbmWaypoint { Name = "Way", X = 101, Y = 100, Z = 7 });
        var spawn = new OtbmSpawn { CenterX = 100, CenterY = 100, CenterZ = 7, Radius = 3 };
        spawn.Creatures.Add(new OtbmCreature
        {
            Name = "Rat", X = 101, Y = 100, Z = 7, SpawnTime = 60, Direction = 2
        });
        map.Spawns.Add(spawn);

        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "selection-move-source.otbm");
        new OtbmWriter().Write(map, source);
        OtbmExternalDataWriter.Save(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.BeginSelection(100, 100, additive: false);
        viewModel.UpdateSelection(101, 100);
        viewModel.EndSelection();

        viewModel.BeginSelectionMove(100, 100).Should().BeTrue();
        var visibleTilesBeforeDrag = viewModel.VisibleTiles;
        viewModel.UpdateSelectionMove(102, 100);
        viewModel.VisibleTiles.Should().BeSameAs(visibleTilesBeforeDrag,
            "ruch podglądu nie może przebudowywać viewportu ani unieważniać cache chunków");
        viewModel.SelectionMoveOffset.Should().Be((2, 0));
        viewModel.VisibleTiles.Where(tile => tile.IsSelected)
            .Select(tile => (tile.X, tile.Y))
            .Should().BeEquivalentTo([(100, 100), (101, 100)],
                "lekka warstwa widoku przesuwa snapshot, a dane bazowego viewportu pozostają stabilne");
        viewModel.EndSelectionMove();

        var movedDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "moved"));
        var movedPath = Path.Combine(movedDirectory.FullName, "moved.otbm");
        await viewModel.SaveMapAsync(movedPath);
        var moved = new OtbmReader().Read(movedPath);
        OtbmExternalDataReader.Load(moved, movedPath).Should().BeEmpty();
        moved.Tiles.Should().NotContainKey(new OtbmTileCoord(100, 100, 7));
        moved.Tiles.Should().NotContainKey(new OtbmTileCoord(101, 100, 7));
        moved.Tiles[new(102, 100, 7)].GroundItemId.Should().Be(4526,
            "RME zastępuje konflikt docelowy, gdy przenoszony tile ma ground");
        moved.Tiles[new(102, 100, 7)].Items.Select(item => item.Id).Should().Equal(2160);
        moved.Tiles[new(102, 100, 7)].IsHouseTile.Should().BeTrue();
        moved.Tiles[new(103, 100, 7)].GroundItemId.Should().Be(600,
            "RME scala item-only tile z istniejącym ground");
        moved.Tiles[new(103, 100, 7)].Items.Select(item => item.Id).Should().Equal(601, 300);
        moved.Spawns.Should().ContainSingle(candidate => candidate.CenterX == 102 && candidate.CenterY == 100);
        moved.Spawns.Single().Creatures.Should().ContainSingle(candidate => candidate.X == 103 && candidate.Y == 100);
        moved.Towns.Single().TempleX.Should().Be(102);
        moved.Houses.Single().EntryX.Should().Be(102);
        moved.Waypoints.Single().X.Should().Be(103);

        viewModel.UndoCommand.Execute(null);
        var undoDirectory = Directory.CreateDirectory(Path.Combine(directory.FullName, "undo"));
        var undoPath = Path.Combine(undoDirectory.FullName, "undo.otbm");
        await viewModel.SaveMapAsync(undoPath);
        var undone = new OtbmReader().Read(undoPath);
        OtbmExternalDataReader.Load(undone, undoPath).Should().BeEmpty();
        undone.Tiles[new(100, 100, 7)].GroundItemId.Should().Be(4526);
        undone.Tiles[new(101, 100, 7)].Items.Should().ContainSingle(item => item.Id == 300);
        undone.Tiles[new(102, 100, 7)].GroundItemId.Should().Be(500);
        undone.Tiles[new(102, 100, 7)].Items.Should().ContainSingle(item => item.Id == 501);
        undone.Tiles[new(103, 100, 7)].GroundItemId.Should().Be(600);
        undone.Tiles[new(103, 100, 7)].Items.Should().ContainSingle(item => item.Id == 601);
        undone.Spawns.Should().ContainSingle(candidate => candidate.CenterX == 100 && candidate.CenterY == 100);
        undone.Spawns.Single().Creatures.Should().ContainSingle(candidate => candidate.X == 101 && candidate.Y == 100);
        undone.Towns.Single().TempleX.Should().Be(100);
        undone.Houses.Single().EntryX.Should().Be(100);
        undone.Waypoints.Single().X.Should().Be(101);
    }

    [Fact]
    public async Task SelectionMove_CancelKeepsMapAndViewportUnchanged()
    {
        var map = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512,
            ItemsMajorVersion = 3, ItemsMinorVersion = 57
        };
        AddTile(map, 100, 100, 7, 4526);
        AddTile(map, 101, 100, 7, 4527);

        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "selection-cancel-source.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.BeginSelection(100, 100, additive: false);
        viewModel.UpdateSelection(101, 100);
        viewModel.EndSelection();

        viewModel.BeginSelectionMove(100, 100).Should().BeTrue();
        var visibleTilesBeforeDrag = viewModel.VisibleTiles;
        viewModel.UpdateSelectionMove(104, 103);
        viewModel.VisibleTiles.Should().BeSameAs(visibleTilesBeforeDrag);
        viewModel.SelectionMoveOffset.Should().Be((4, 3));

        viewModel.CancelSelectionMove();

        viewModel.SelectionMoveOffset.Should().Be((0, 0));
        viewModel.IsTileSelected(100, 100).Should().BeTrue();
        viewModel.IsTileSelected(101, 100).Should().BeTrue();
        viewModel.IsTileSelected(104, 103).Should().BeFalse();
        viewModel.CanUndo.Should().BeFalse("anulowany podgląd nie modyfikuje mapy ani historii");

        var saved = Path.Combine(directory.FullName, "selection-cancel-saved.otbm");
        await viewModel.SaveMapAsync(saved);
        var reloaded = new OtbmReader().Read(saved);
        reloaded.Tiles[new(100, 100, 7)].GroundItemId.Should().Be(4526);
        reloaded.Tiles[new(101, 100, 7)].GroundItemId.Should().Be(4527);
        reloaded.Tiles.Should().NotContainKey(new OtbmTileCoord(104, 103, 7));
    }

    [Fact]
    public async Task Viewport_ProjectsLowerAndHigherFloorsLikeRme()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 98, 98, 7, 100);
        AddTile(map, 101, 101, 4, 101);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "floor-projection.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.ViewX = 95;
        viewModel.ViewY = 95;
        viewModel.CurrentFloor = 5;
        viewModel.ShowAllFloors = true;
        viewModel.GhostHigherFloors = false;

        viewModel.VisibleTiles.Should().Contain(tile => tile.X == 100 && tile.Y == 100,
            "RME rzutuje Z=7 z (98,98) na pole widoku (100,100) dla Z=5");

        viewModel.ShowAllFloors = false;
        viewModel.GhostHigherFloors = true;
        viewModel.VisibleTiles.Should().Contain(tile => tile.X == 100 && tile.Y == 100,
            "RME rzutuje wyższe Z=4 z (101,101) na pole widoku (100,100) dla Z=5");

        viewModel.GhostHigherFloors = false;
        viewModel.VisibleTiles.Should().NotContain(tile => tile.X == 100 && tile.Y == 100);
    }

    [Fact]
    public async Task EntityEditors_KeepTownHouseWaypointRelationsAndUndo()
    {
        var map = new OtbmMap
        {
            Version = 2, Width = 512, Height = 512,
            ItemsMajorVersion = 3, ItemsMinorVersion = 57,
            HouseFile = "houses.xml", SpawnFile = "spawns.xml"
        };
        map.Towns.Add(new OtbmTown { Id = 1, Name = "Thais", TempleX = 100, TempleY = 100, TempleZ = 7 });
        map.Houses.Add(new OtbmHouse { Id = 1, Name = "Villa", TownId = 1, EntryX = 101, EntryY = 100, EntryZ = 7 });
        map.Waypoints.Add(new OtbmWaypoint { Name = "Depot", X = 102, Y = 100, Z = 7 });
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "entities.otbm");
        new OtbmWriter().Write(map, source);
        OtbmExternalDataWriter.Save(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SelectedTown.Should().NotBeNull();
        viewModel.TownId = 7;
        viewModel.TownName = "New Thais";
        viewModel.ApplyTownCommand.Execute(null);

        viewModel.TownEntries.Should().ContainSingle(town => town.Id == 7 && town.Name == "New Thais");
        viewModel.HouseEntries.Should().ContainSingle(house => house.TownId == 7);
        viewModel.DeleteTownCommand.Execute(null);
        viewModel.Status.Should().Contain("Nie można usunąć miasta");

        viewModel.AddWaypointCommand.Execute(null);
        viewModel.WaypointEntries.Should().HaveCount(2);
        viewModel.UndoCommand.Execute(null);
        viewModel.WaypointEntries.Should().ContainSingle(waypoint => waypoint.Name == "Depot");
    }

    [Fact]
    public async Task ImportMapCore_SmartMergesIdsTranslatesDataAndIsSingleUndoStep()
    {
        var target = new OtbmMap
        {
            Version = 2, Width = 256, Height = 256,
            ItemsMajorVersion = 3, ItemsMinorVersion = 57,
            HouseFile = "houses.xml", SpawnFile = "spawns.xml"
        };
        target.Towns.Add(new OtbmTown { Id = 1, Name = "Existing", TempleX = 10, TempleY = 10, TempleZ = 7 });
        target.Houses.Add(new OtbmHouse { Id = 1, Name = "Existing House", TownId = 1, EntryX = 11, EntryY = 10, EntryZ = 7 });
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "target.otbm");
        var saved = Path.Combine(directory.FullName, "imported.otbm");
        new OtbmWriter().Write(target, source);
        OtbmExternalDataWriter.Save(target, source);
        var imported = new OtbmMap { Width = 256, Height = 256 };
        imported.Towns.Add(new OtbmTown { Id = 1, Name = "Different", TempleX = 20, TempleY = 30, TempleZ = 7 });
        imported.Houses.Add(new OtbmHouse { Id = 1, Name = "Imported House", TownId = 1, EntryX = 21, EntryY = 30, EntryZ = 7 });
        imported.Waypoints.Add(new OtbmWaypoint { Name = "Imported", X = 22, Y = 30, Z = 7 });
        imported.Tiles[new(20, 30, 7)] = new OtbmTile
        {
            X = 20, Y = 30, Z = 7, GroundItemId = 100, IsHouseTile = true, HouseId = 1
        };
        var spawn = new OtbmSpawn { CenterX = 20, CenterY = 30, CenterZ = 7, Radius = 2 };
        spawn.Creatures.Add(new OtbmCreature { Name = "Rat", X = 21, Y = 30, Z = 7, SpawnTime = 60 });
        imported.Spawns.Add(spawn);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.ImportMapCore(imported, 100, 200, "Smart Merge", importSpawns: true);
        await viewModel.SaveMapAsync(saved);

        var result = new OtbmReader().Read(saved);
        OtbmExternalDataReader.Load(result, saved).Should().BeEmpty();
        var tile = result.Tiles[new(120, 230, 7)];
        tile.IsHouseTile.Should().BeTrue();
        tile.HouseId.Should().NotBe(1, "konflikt House ID musi zostać bezpiecznie przemapowany");
        result.Towns.Should().HaveCount(2);
        result.Houses.Should().ContainSingle(house => house.Id == tile.HouseId && house.TownId != 1);
        result.Spawns.Should().ContainSingle(spawnResult => spawnResult.CenterX == 120 && spawnResult.CenterY == 230);

        viewModel.UndoCommand.Execute(null);
        viewModel.SearchMode = "Position";
        viewModel.MapSearchQuery = "120,230,7";
        viewModel.SearchMapCommand.Execute(null);
        viewModel.SearchResults.Should().ContainSingle(resultItem => resultItem.Description == "Puste pole");
    }

    [Fact]
    public async Task TileFlags_RoundTripPreservesUnknownBitsAndSupportsUndo()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        map.Tiles[new(100, 100, 7)].Flags = 0x02;
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "tile-flags.otbm");
        var saved = Path.Combine(directory.FullName, "tile-flags-saved.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.SetHoveredTile(100, 100);
        viewModel.OpenHoveredTilePropertiesCommand.Execute(null);
        viewModel.PropertyProtectionZone = true;
        viewModel.PropertyNoLogoutZone = true;
        viewModel.PropertyPvpZone = true;
        viewModel.ApplyTilePropertiesCommand.Execute(null);
        viewModel.UndoCommand.Execute(null);
        viewModel.PropertyProtectionZone.Should().BeFalse();
        viewModel.RedoCommand.Execute(null);
        await viewModel.SaveMapAsync(saved);

        var reloaded = new OtbmReader().Read(saved);
        reloaded.Tiles[new(100, 100, 7)].Flags.Should().Be(0x02u | 0x01u | 0x08u | 0x10u);
    }

    [Fact]
    public async Task Navigation_KeepsPreviousAndForwardViewportPositions()
    {
        var map = new OtbmMap { Version = 2, Width = 1024, Height = 1024, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "navigation.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        viewModel.SetViewportSize(10, 10);
        await viewModel.LoadMapAsync(source);
        var original = (viewModel.ViewX, viewModel.ViewY, viewModel.CurrentFloor);
        viewModel.GoToX = 500;
        viewModel.GoToY = 600;
        viewModel.GoToZ = 5;
        viewModel.GoToPositionCommand.Execute(null);

        viewModel.CanNavigateBack.Should().BeTrue();
        viewModel.NavigateBackCommand.Execute(null);
        (viewModel.ViewX, viewModel.ViewY, viewModel.CurrentFloor).Should().Be(original);
        viewModel.CanNavigateForward.Should().BeTrue();
        viewModel.NavigateForwardCommand.Execute(null);
        (viewModel.ViewX, viewModel.ViewY, viewModel.CurrentFloor).Should().Be(((ushort)495, (ushort)595, (byte)5));
    }

    [Fact]
    public async Task ExportMinimap_WritesRmeCompatibleBitmapForCurrentFloor()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        AddTile(map, 102, 101, 7, 101);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "minimap.otbm");
        var exported = Path.Combine(directory.FullName, "minimap.bmp");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.MinimapExportMode = "Current Floor";
        await viewModel.ExportMinimapAsync(exported);

        var bytes = File.ReadAllBytes(exported);
        bytes.Should().HaveCountGreaterThan(54);
        bytes[0].Should().Be((byte)'B');
        bytes[1].Should().Be((byte)'M');
        viewModel.Status.Should().Contain("Wyeksportowano minimapę");
    }

    [Fact]
    public async Task ModifiedViewAndClearState_AreIndependentFromSaveHistory()
    {
        var map = new OtbmMap { Version = 2, Width = 512, Height = 512, ItemsMajorVersion = 3, ItemsMinorVersion = 57 };
        AddTile(map, 100, 100, 7, 100);
        var directory = Directory.CreateTempSubdirectory();
        var source = Path.Combine(directory.FullName, "modified.otbm");
        new OtbmWriter().Write(map, source);

        using var viewModel = new MapEditorViewModel();
        await viewModel.LoadMapAsync(source);
        viewModel.ShowOnlyModified = true;
        viewModel.VisibleTiles.Should().BeEmpty();
        viewModel.SelectedBrush = new MapPaletteItem { ItemId = 200 };
        viewModel.PlaceBrushAt(100, 100);
        viewModel.VisibleTiles.Should().ContainSingle();
        viewModel.ClearModifiedStateCommand.Execute(null);

        viewModel.VisibleTiles.Should().BeEmpty();
        viewModel.IsDirty.Should().BeTrue("Clear Modified State nie może udawać zapisu dokumentu");
        viewModel.CanUndo.Should().BeTrue();
    }

    private static RmeTilesetDefinition Tileset(string name, RmePaletteCategory category, ushort itemId) =>
        new(name, new Dictionary<RmePaletteCategory, IReadOnlyList<RmePaletteEntry>>
        {
            [category] = [RmePaletteEntry.ForItem(itemId)]
        });

    private static void AddTile(OtbmMap map, ushort x, ushort y, byte z, ushort groundId)
    {
        var tile = new OtbmTile { X = x, Y = y, Z = z, GroundItemId = groundId };
        map.Tiles[new(x, y, z)] = tile;
    }
}
