using System.Collections.ObjectModel;
using System.Collections.Immutable;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modules.MapEditor.Services;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.MapEditor.ViewModels;

/// <summary>
/// Edytor map OTBM zgodny z Remere's Map Editor. Wczytuje klasyczne Tibia.dat/.spr
/// oraz assets Tibia 12+ i mapuje Server ID przez właściwy plik items.otb.
/// </summary>
public partial class MapEditorViewModel : ObservableObject, IDisposable
{
    private const double MinimapDisplaySize = 256d;
    private MapAssetService _assets = new();
    private readonly PreviousGenerationRetainer<MapAssetService> _retiredAssets = new();
    private readonly MapMinimapService _minimapService = new();
    private readonly RmeMaterialCatalogService _materialLoader = new();
    private readonly RmeCreatureCatalogService _creatureLoader = new();
    private readonly MapEditHistory _history = new();
    private readonly MapVersionConversionService _conversion = new();
    private readonly Random _brushRandom = new();
    private OtbmMap? _map;
    private RmeMaterialCatalog? _materials;
    private RmeGroundBorderService? _groundBorders;
    private RmeConnectedBrushService? _connectedBrushes;
    private IReadOnlyDictionary<string, RmeCreatureDefinition> _creatureDefinitions =
        new Dictionary<string, RmeCreatureDefinition>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _importedCreatureNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<byte, int> _floorTileCounts = [];
    private readonly HashSet<OtbmTileCoord> _spawnCenters = [];
    private readonly Dictionary<byte, IReadOnlyList<OtbmSpawn>> _spawnsByFloor = [];
    private readonly Dictionary<OtbmTileCoord, IReadOnlyList<OtbmCreature>> _creaturesByTile = [];
    private readonly Dictionary<OtbmTileCoord, OtbmHouse> _houseExits = [];
    private readonly Dictionary<OtbmTileCoord, IReadOnlyList<string>> _waypointsByTile = [];
    private readonly Dictionary<OtbmTileCoord, IReadOnlyList<string>> _townsByTile = [];
    private bool _externalDataDirty;
    private Dictionary<OtbmTileCoord, OtbmTile?>? _activeTileStroke;
    private string _activeTileStrokeDescription = string.Empty;
    private readonly HashSet<OtbmTileCoord> _selectedCoordinates = [];
    private readonly HashSet<OtbmTileCoord> _selectionBase = [];
    private OtbmTileCoord? _selectionAnchor;
    private HashSet<OtbmTileCoord>? _selectionMoveSource;
    private OtbmTileCoord? _selectionMoveAnchor;
    private int _selectionMoveOffsetX;
    private int _selectionMoveOffsetY;
    private int _selectionMoveMinX;
    private int _selectionMoveMaxX;
    private int _selectionMoveMinY;
    private int _selectionMoveMaxY;
    private MapClipboard? _clipboard;
    private readonly HashSet<OtbmTileCoord> _previewCoordinates = [];
    private readonly HashSet<OtbmTileCoord> _borderPreviewCoordinates = [];
    private MapBrushPreviewGeometryKey? _previewGeometryKey;
    private MapBrushPreviewOffset[] _previewGeometry = [];
    private readonly Dictionary<OtbmTileCoord, OtbmTile?> _modifiedBaseline = [];
    private ushort _minimapMinX;
    private ushort _minimapMinY;
    private int _minimapScale = 1;
    private int _minimapPixelWidth;
    private int _minimapPixelHeight;
    private MapConversionPlan? _conversionPlan;
    private readonly Stack<MapViewPosition> _previousPositions = [];
    private readonly Stack<MapViewPosition> _nextPositions = [];
    private int _animationFrame;
    private bool _visibleHasAnimations;
    private bool _suppressViewportRefresh;
    private bool _strokeViewportDirty;
    private Dictionary<OtbmTileCoord, MapTileItem> _viewportTileCache = [];
    private MapLightViewportCache? _lightViewportCache;
    private OtbmTileCoord? _contextCoordinate;
    private bool _synchronizingPaletteNavigation;
    private bool _showRawOthers;
    private readonly Dictionary<string, string> _paletteGroupMemory = new(StringComparer.OrdinalIgnoreCase);
    private MapPaletteItem? _selectedPaletteVisual;
    private uint? _housePaletteTownFilterId;
    private bool _housePaletteWithoutTown;
    private int _loadOperationActive;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _minimapCancellation;
    private Task _minimapBuildTask = Task.CompletedTask;
    private long _minimapRequestId;
    private IReadOnlyDictionary<uint, ushort> _minimapColorLookup =
        new Dictionary<uint, ushort>();
    // Minimap reads only immutable data. The large base snapshot is created while a
    // freshly parsed map is still private to the loader; later edits append small,
    // structurally-shared overrides on the UI thread. A worker therefore never
    // enumerates the mutable OTBM dictionary or item lists while painting continues.
    private ImmutableDictionary<byte, ImmutableArray<MapMinimapSourceSample>> _minimapSourceByFloor =
        ImmutableDictionary<byte, ImmutableArray<MapMinimapSourceSample>>.Empty;
    private ImmutableDictionary<byte, ImmutableDictionary<OtbmTileCoord, MapMinimapSourceOverride>>
        _minimapSourceOverridesByFloor =
            ImmutableDictionary<byte, ImmutableDictionary<OtbmTileCoord, MapMinimapSourceOverride>>.Empty;
    private bool _disposed;

    public MapEditorViewModel(MapEditorPreferences? preferences = null)
    {
        _history.TileChangesPushed += RememberModifiedBaselines;
        preferences ??= MapEditorPreferences.Default;
        foreach (var pair in preferences.PaletteGroups)
            _paletteGroupMemory[pair.Key] = pair.Value;
        if (PaletteSections.Contains(preferences.PaletteSection, StringComparer.Ordinal))
            _selectedPaletteSection = preferences.PaletteSection;
        _selectedPaletteGroup = _paletteGroupMemory.GetValueOrDefault(_selectedPaletteSection) ?? string.Empty;
        _selectedWorkspaceTabIndex = Math.Max(0, PaletteSectionIndex(_selectedPaletteSection));
        _activeWorkspaceLabel = _selectedPaletteSection;
        _brushSize = BrushSizes.Contains(preferences.BrushSize) ? preferences.BrushSize : 0;
        _brushShape = BrushShapes.Contains(preferences.BrushShape, StringComparer.Ordinal)
            ? preferences.BrushShape
            : "Kwadrat";
        _automagic = preferences.Automagic;
    }

    [ObservableProperty] private string _title = "Map Editor (RME Redux)";
    [ObservableProperty] private string _mapPath = string.Empty;
    [ObservableProperty] private string _assetsPath = string.Empty;
    [ObservableProperty] private string _status = "Wczytaj mapę .otbm oraz folder klienta lub assets Tibia 12+.";
    [ObservableProperty] private string _mapSummary = "Brak mapy.";
    [ObservableProperty] private double _renderFramesPerSecond;
    [ObservableProperty] private double _averageRenderMilliseconds;
    [ObservableProperty] private double _viewportBuildMilliseconds;
    [ObservableProperty] private int _renderSampleFrames;
    [ObservableProperty] private string _renderDiagnosticsLabel = "FPS: — · render: — · widok: —";
    [ObservableProperty] private byte _currentFloor = 0;
    [ObservableProperty] private int _tileSize = 32;
    [ObservableProperty] private string _floorBreakdown = string.Empty;
    [ObservableProperty] private int _currentFloorTileCount;
    [ObservableProperty] private string _hoveredTileLabel = "—";
    [ObservableProperty] private string _mapFileName = "(brak mapy)";
    [ObservableProperty] private string _assetsFolderName = "(brak assets)";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private double _loadProgress;
    [ObservableProperty] private string _loadStage = "Gotowy.";
    [ObservableProperty] private string _loadProgressLabel = "0%";
    [ObservableProperty] private bool _isLoadProgressIndeterminate;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _showGrid;
    [ObservableProperty] private bool _showHouses = true;
    [ObservableProperty] private bool _showSpawns = true;
    [ObservableProperty] private bool _showCreatures = true;
    [ObservableProperty] private bool _showSpecialTiles = true;
    [ObservableProperty] private bool _showItems = true;
    [ObservableProperty] private bool _showWaypoints = true;
    [ObservableProperty] private bool _showBlocking;
    [ObservableProperty] private bool _showTooltips = true;
    [ObservableProperty] private bool _showPreview = true;
    [ObservableProperty] private bool _showAnimation = true;
    [ObservableProperty] private bool _showOnlyColors;
    [ObservableProperty] private bool _showAsMinimap;
    [ObservableProperty] private bool _showAllFloors = true;
    [ObservableProperty] private bool _showShade = true;
    [ObservableProperty] private bool _showTowns;
    [ObservableProperty] private bool _showLights = true;
    [ObservableProperty] private bool _showLightStrength = true;
    [ObservableProperty] private bool _experimentalFog;
    [ObservableProperty] private bool _showWallHooks;
    [ObservableProperty] private bool _highlightItems;
    [ObservableProperty] private bool _highlightLockedDoors = true;
    [ObservableProperty] private bool _ghostLooseItems;
    [ObservableProperty] private bool _ghostHigherFloors;
    [ObservableProperty] private bool _showClientBox;
    [ObservableProperty] private bool _showOnlyModified;
    [ObservableProperty] private bool _alwaysShowZones = true;
    [ObservableProperty] private bool _extendedHouseShader = true;
    [ObservableProperty] private bool _showTechnicalItems = true;
    [ObservableProperty] private Bitmap? _minimapImage;
    [ObservableProperty] private string _minimapStatus = "Minimapa nie została wygenerowana.";
    [ObservableProperty] private bool _isMinimapLoading;
    [ObservableProperty] private double _minimapProgress;
    [ObservableProperty] private bool _isMinimapViewportVisible;
    [ObservableProperty] private double _minimapViewportLeft;
    [ObservableProperty] private double _minimapViewportTop;
    [ObservableProperty] private double _minimapViewportWidth;
    [ObservableProperty] private double _minimapViewportHeight;
    [ObservableProperty] private string _minimapExportMode = "Current Floor";
    [ObservableProperty] private string _conversionTargetFolder = string.Empty;
    [ObservableProperty] private string _conversionReport = "Wybierz folder docelowego klienta i przeanalizuj mapę.";
    [ObservableProperty] private bool _canApplyConversion;
    [ObservableProperty] private bool _selectionMode;
    [ObservableProperty] private string _selectionLabel = "Zaznaczenie: 0 pól";
    [ObservableProperty] private string _selectionFloorMode = "Current Floor";
    [ObservableProperty] private bool _compensateSelection = true;
    [ObservableProperty] private int _bulkItemId;
    [ObservableProperty] private int _bulkReplacementId;
    [ObservableProperty] private bool _bulkSelectionOnly;
    [ObservableProperty] private string _mapStatistics = "Statystyki nie zostały obliczone.";
    [ObservableProperty] private string _materialStatus = "Palety RME nie są wczytane.";
    [ObservableProperty] private string _paletteSearch = string.Empty;
    [ObservableProperty] private string _creatureFilter = "Wszystkie";
    [ObservableProperty] private bool _automagic = true;
    [ObservableProperty] private int _brushSize;
    [ObservableProperty] private string _brushShape = "Kwadrat";
    [ObservableProperty] private int _spawnRadius = 5;
    [ObservableProperty] private int _defaultSpawnTime = 60;
    [ObservableProperty] private MapTilePropertyItem? _selectedPropertyItem;
    [ObservableProperty] private string _propertyTileLabel = "Najedź na pole mapy i wczytaj jego właściwości.";
    [ObservableProperty] private string _propertyStatus = string.Empty;
    [ObservableProperty] private int _propertyActionId;
    [ObservableProperty] private int _propertyUniqueId;
    [ObservableProperty] private int _propertyCount;
    [ObservableProperty] private string _propertyText = string.Empty;
    [ObservableProperty] private string _propertyDescription = string.Empty;
    [ObservableProperty] private int _propertyDepotId;
    [ObservableProperty] private int _propertyHouseDoorId;
    [ObservableProperty] private int _propertyTier;
    [ObservableProperty] private int _propertyRuneCharges;
    [ObservableProperty] private long _propertyDuration;
    [ObservableProperty] private int _propertyDecayingState;
    [ObservableProperty] private long _propertyWrittenDate;
    [ObservableProperty] private string _propertyWrittenBy = string.Empty;
    [ObservableProperty] private long _propertySleeperGuid;
    [ObservableProperty] private long _propertySleepStart;
    [ObservableProperty] private int _propertyCharges;
    [ObservableProperty] private bool _propertyHasPodiumOutfit;
    [ObservableProperty] private bool _propertyPodiumShowPlatform;
    [ObservableProperty] private bool _propertyPodiumShowOutfit;
    [ObservableProperty] private bool _propertyPodiumShowMount;
    [ObservableProperty] private int _propertyPodiumDirection;
    [ObservableProperty] private int _propertyPodiumLookType;
    [ObservableProperty] private int _propertyPodiumLookHead;
    [ObservableProperty] private int _propertyPodiumLookBody;
    [ObservableProperty] private int _propertyPodiumLookLegs;
    [ObservableProperty] private int _propertyPodiumLookFeet;
    [ObservableProperty] private int _propertyPodiumLookAddon;
    [ObservableProperty] private int _propertyPodiumLookMount;
    [ObservableProperty] private int _propertyPodiumMountHead;
    [ObservableProperty] private int _propertyPodiumMountBody;
    [ObservableProperty] private int _propertyPodiumMountLegs;
    [ObservableProperty] private int _propertyPodiumMountFeet;
    [ObservableProperty] private MapCustomAttributeItem? _selectedPropertyCustomAttribute;
    [ObservableProperty] private int _propertyTeleportX;
    [ObservableProperty] private int _propertyTeleportY;
    [ObservableProperty] private int _propertyTeleportZ;
    [ObservableProperty] private bool _propertyProtectionZone;
    [ObservableProperty] private bool _propertyNoPvpZone;
    [ObservableProperty] private bool _propertyNoLogoutZone;
    [ObservableProperty] private bool _propertyPvpZone;
    [ObservableProperty] private bool _propertyRefreshTile;
    [ObservableProperty] private string _mapDescription = string.Empty;
    [ObservableProperty] private int _mapWidth = 512;
    [ObservableProperty] private int _mapHeight = 512;
    [ObservableProperty] private int _mapVersion = 2;
    [ObservableProperty] private int _mapItemsMajorVersion = 3;
    [ObservableProperty] private int _mapItemsMinorVersion = 57;
    [ObservableProperty] private string _mapHouseFile = "houses.xml";
    [ObservableProperty] private string _mapSpawnFile = "spawns.xml";
    [ObservableProperty] private string _mapSpawnNpcFile = string.Empty;
    [ObservableProperty] private OtbmTown? _selectedTown;
    [ObservableProperty] private int _townId;
    [ObservableProperty] private string _townName = string.Empty;
    [ObservableProperty] private int _townTempleX;
    [ObservableProperty] private int _townTempleY;
    [ObservableProperty] private int _townTempleZ = 7;
    [ObservableProperty] private OtbmHouse? _selectedHouse;
    [ObservableProperty] private int _houseId;
    [ObservableProperty] private string _houseName = string.Empty;
    [ObservableProperty] private int _houseEntryX;
    [ObservableProperty] private int _houseEntryY;
    [ObservableProperty] private int _houseEntryZ = 7;
    [ObservableProperty] private int _houseRent;
    [ObservableProperty] private int _houseTownId;
    [ObservableProperty] private bool _houseIsGuildhall;
    [ObservableProperty] private OtbmWaypoint? _selectedWaypoint;
    [ObservableProperty] private string _waypointName = string.Empty;
    [ObservableProperty] private int _waypointX;
    [ObservableProperty] private int _waypointY;
    [ObservableProperty] private int _waypointZ = 7;
    [ObservableProperty] private int _importOffsetX;
    [ObservableProperty] private int _importOffsetY;
    [ObservableProperty] private string _importHouseMode = "Smart Merge";
    [ObservableProperty] private bool _importSpawns = true;
    [ObservableProperty] private string _searchMode = "Item ID";
    [ObservableProperty] private string _mapSearchQuery = string.Empty;
    [ObservableProperty] private bool _searchSelectionOnly;
    [ObservableProperty] private MapSearchResult? _selectedSearchResult;
    [ObservableProperty] private int _goToX;
    [ObservableProperty] private int _goToY;
    [ObservableProperty] private int _goToZ = 7;
    [ObservableProperty] private int _jumpToItemId;
    [ObservableProperty] private string _jumpToBrushQuery = string.Empty;
    [ObservableProperty] private int _selectedWorkspaceTabIndex;
    [ObservableProperty] private string _selectedPaletteSection = "Terrain Palette";
    [ObservableProperty] private string _selectedPaletteGroup = string.Empty;
    [ObservableProperty] private IReadOnlyList<string> _paletteGroups = Array.Empty<string>();
    [ObservableProperty] private string _activeWorkspaceLabel = "Terrain Palette";
    public bool CanSave => _map is not null;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public IReadOnlyList<int> BrushSizes { get; } = [0, 1, 2, 4, 6, 8, 11];
    public IReadOnlyList<string> BrushShapes { get; } = ["Kwadrat", "Okrąg"];
    public IReadOnlyList<string> ImportHouseModes { get; } = ["Smart Merge", "Insert", "Merge", "Don't Import"];
    public IReadOnlyList<string> SearchModes { get; } =
        ["Item ID", "Unique ID", "Action ID", "Container", "Writeable", "Text", "Everything", "Position"];
    public IReadOnlyList<string> CreatureFilters { get; } = ["Wszystkie", "Potwory", "NPC"];
    public IReadOnlyList<string> PaletteSections { get; } =
    [
        "Terrain Palette", "Doodad Palette", "Collections Palette", "Item Palette",
        "House Palette", "Waypoint Palette", "Creature Palette", "RAW Palette"
    ];
    public string PaletteGroupLabel => SelectedPaletteSection switch
    {
        "House Palette" => "Miasto",
        "Creature Palette" => "Grupa",
        "Waypoint Palette" => "Lista",
        _ => "Tileset"
    };
    public IReadOnlyList<string> SelectionFloorModes { get; } = ["Current Floor", "Lower Floors", "Visible Floors"];
    public IReadOnlyList<string> MinimapExportModes { get; } =
        ["Current Floor", "Ground Floor", "All Floors", "Selected Area"];
    public IReadOnlyList<string> CustomAttributeTypes { get; } =
        ["String", "Integer", "Float", "Boolean", "Double"];
    public ObservableCollection<MapCustomAttributeItem> PropertyCustomAttributes { get; } = [];
    public string DirtyLabel => IsDirty ? "Niezapisane zmiany" : "Zapisano";
    public bool HasSelection => _selectedCoordinates.Count > 0;
    /// <summary>
    /// Aktualne przesunięcie lekkiego podglądu drag. Widok odczytuje tę wartość
    /// bezpośrednio po UpdateSelectionMove, więc nie wymaga ona powiadomień ani
    /// przebudowy kolekcji VisibleTiles.
    /// </summary>
    public (int X, int Y) SelectionMoveOffset => (_selectionMoveOffsetX, _selectionMoveOffsetY);
    public bool HasClipboard => _clipboard is { Tiles.Count: > 0 } or { Spawns.Count: > 0 };
    public bool CanNavigateBack => _previousPositions.Count > 0;
    public bool CanNavigateForward => _nextPositions.Count > 0;
    public int AnimationFrame => _animationFrame;

    // ── Narzędzie pędzla RME ────────────────────────────────────────────────────────
    /// <summary>Wybrany item z palety — używany jako "pędzel" przy klik na tile.</summary>
    [ObservableProperty] private MapPaletteItem? _selectedBrush;
    [ObservableProperty] private RmeTilesetDefinition? _selectedTileset;
    [ObservableProperty] private int _brushVariation;
    [ObservableProperty] private string _brushModeLabel = "Brush: (brak — wybierz item z palety)";

    partial void OnSelectedBrushChanged(MapPaletteItem? value)
    {
        if (_selectedPaletteVisual is not null && !ReferenceEquals(_selectedPaletteVisual, value))
            _selectedPaletteVisual.IsSelected = false;
        if (value is not null) value.IsSelected = true;
        _selectedPaletteVisual = value;
        BrushModeLabel = value is null
            ? "Brush: (brak — wybierz item z palety)"
            : $"Brush: {value.CategoryLabel} · {(value.ItemId == 0 ? value.Name : $"#{value.ItemId} {value.Name}")}" +
              (value.VariationCount > 1 ? $" · wariant {BrushVariation % value.VariationCount + 1}/{value.VariationCount}" : string.Empty);
        OnPropertyChanged(nameof(BrushVariationMaximum));
        UpdateBrushPreview();
    }

    partial void OnSelectedTilesetChanged(RmeTilesetDefinition? value)
    {
        BuildSelectedTilesetPalettes();
        if (_synchronizingPaletteNavigation || value is null ||
            !TryGetMaterialPaletteCategory(SelectedPaletteSection, out var category) ||
            !value.Categories.TryGetValue(category, out var entries) || entries.Count == 0)
            return;
        _paletteGroupMemory[SelectedPaletteSection] = value.Name;
        if (PaletteGroups.Contains(value.Name, StringComparer.OrdinalIgnoreCase))
            SelectedPaletteGroup = PaletteGroups.First(group =>
                group.Equals(value.Name, StringComparison.OrdinalIgnoreCase));
    }
    partial void OnPaletteSearchChanged(string value) => BuildSelectedTilesetPalettes();
    partial void OnCreatureFilterChanged(string value) => BuildSelectedTilesetPalettes();

    partial void OnSelectedPaletteSectionChanged(string value)
    {
        if (_synchronizingPaletteNavigation) return;
        ActivatePaletteSection(value);
    }

    partial void OnSelectedPaletteGroupChanged(string value)
    {
        if (_synchronizingPaletteNavigation) return;
        if (!IsPalettePlaceholder(value)) _paletteGroupMemory[SelectedPaletteSection] = value;
        ApplyPaletteGroup(value);
    }

    partial void OnSelectedWorkspaceTabIndexChanged(int value)
    {
        ActiveWorkspaceLabel = WorkspaceLabel(value);
        if (_synchronizingPaletteNavigation || !TryGetPaletteSection(value, out var section)) return;
        ActivatePaletteSection(section);
    }

    public bool IsPaletteGroupVisible => SelectedPaletteSection != "Waypoint Palette";

    public void ActivateSelectedPalette() => ActivatePaletteSection(SelectedPaletteSection);

    public MapEditorPreferences CapturePreferences()
    {
        if (!IsPalettePlaceholder(SelectedPaletteGroup))
            _paletteGroupMemory[SelectedPaletteSection] = SelectedPaletteGroup;
        return new MapEditorPreferences(
            SelectedPaletteSection,
            new Dictionary<string, string>(_paletteGroupMemory, StringComparer.OrdinalIgnoreCase),
            BrushSize,
            BrushShape,
            Automagic);
    }

    public void SelectWorkspacePanel(string panel)
    {
        var index = panel switch
        {
            "Towns" => 8,
            "View" => 9,
            "Minimap" => 10,
            "Map" => 11,
            "ImportSearch" => 12,
            "Convert" => 13,
            "Tools" => 14,
            "Tile" => 15,
            _ => -1
        };
        if (index < 0) return;
        SelectedWorkspaceTabIndex = index;
        Status = $"Otworzono panel: {WorkspaceLabel(index)}.";
    }

    private void ActivatePaletteSection(string section)
    {
        var index = PaletteSectionIndex(section);
        if (index < 0) return;
        if (section != "RAW Palette") _showRawOthers = false;
        _synchronizingPaletteNavigation = true;
        try
        {
            if (SelectedPaletteSection != section) SelectedPaletteSection = section;
            SelectedWorkspaceTabIndex = index;
            ActiveWorkspaceLabel = section;
            OnPropertyChanged(nameof(PaletteGroupLabel));
            OnPropertyChanged(nameof(IsPaletteGroupVisible));
        }
        finally
        {
            _synchronizingPaletteNavigation = false;
        }
        RebuildPaletteNavigationGroups();
    }

    private void RebuildPaletteNavigationGroups()
    {
        var previous = SelectedPaletteGroup;
        var groups = new List<string>();
        RmePaletteCategory? materialCategory = null;
        if (TryGetMaterialPaletteCategory(SelectedPaletteSection, out var category))
        {
            materialCategory = category;
            groups.AddRange(GetRmePaletteGroupNames(Tilesets, category));
        }
        else if (SelectedPaletteSection == "Creature Palette")
        {
            groups.Add("NPCs");
            groups.Add("Others");
        }
        else if (SelectedPaletteSection == "House Palette")
        {
            if (_map is not null)
            {
                var usedTownIds = _map.Houses.Select(house => house.TownId).ToHashSet();
                groups.AddRange(_map.Towns.Where(town => usedTownIds.Contains(town.Id))
                    .OrderBy(town => town.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(town => town.Name));
                if (_map.Houses.Any(house => house.TownId == 0 || _map.Towns.All(town => town.Id != house.TownId)))
                    groups.Add("No Town");
            }
        }

        if (groups.Count == 0 && SelectedPaletteSection != "Waypoint Palette")
            groups.Add(_assets.IsLoaded && Tilesets.Count == 0
                ? "Wszystkie obiekty klienta"
                : "Brak dostępnych grup");

        var remembered = _paletteGroupMemory.GetValueOrDefault(SelectedPaletteSection);
        var selectedTilesetMatchesCategory = SelectedTileset is not null && materialCategory is { } selectedCategory &&
                                             SelectedTileset.Categories.TryGetValue(selectedCategory, out var selectedEntries) &&
                                             selectedEntries.Count > 0;
        var preferred = !string.IsNullOrWhiteSpace(remembered) &&
                        groups.Contains(remembered, StringComparer.OrdinalIgnoreCase)
            ? groups.First(group => group.Equals(remembered, StringComparison.OrdinalIgnoreCase))
            : selectedTilesetMatchesCategory &&
              groups.Contains(SelectedTileset!.Name, StringComparer.OrdinalIgnoreCase)
                ? groups.First(group => group.Equals(SelectedTileset.Name, StringComparison.OrdinalIgnoreCase))
                : groups.Contains(previous, StringComparer.OrdinalIgnoreCase)
                    ? groups.First(group => group.Equals(previous, StringComparison.OrdinalIgnoreCase))
                    : groups.FirstOrDefault() ?? string.Empty;

        _synchronizingPaletteNavigation = true;
        try
        {
            // Nowa instancja listy jest celowa: Avalonia ComboBox potrafi zachować
            // stare kontenery po Clear()+Add(), co miesza tilesety różnych palet.
            PaletteGroups = groups.ToArray();
            SelectedPaletteGroup = preferred;
        }
        finally
        {
            _synchronizingPaletteNavigation = false;
        }
        ApplyPaletteGroup(preferred);
    }

    private void ApplyPaletteGroup(string group)
    {
        if (IsPalettePlaceholder(group)) return;
        _synchronizingPaletteNavigation = true;
        try
        {
            if (TryGetMaterialPaletteCategory(SelectedPaletteSection, out var category))
            {
                _showRawOthers = category == RmePaletteCategory.Raw &&
                                 group.Equals("Others", StringComparison.OrdinalIgnoreCase);
                if (_showRawOthers)
                {
                    if (SelectedTileset is not null) SelectedTileset = null;
                    else BuildSelectedTilesetPalettes();
                }
                else
                {
                    var tileset = Tilesets.FirstOrDefault(candidate =>
                        candidate.Name.Equals(group, StringComparison.OrdinalIgnoreCase) &&
                        candidate.Categories.TryGetValue(category, out var entries) && entries.Count > 0);
                    if (tileset is not null && !ReferenceEquals(SelectedTileset, tileset)) SelectedTileset = tileset;
                }
            }
            else if (SelectedPaletteSection == "Creature Palette")
            {
                _showRawOthers = false;
                CreatureFilter = group == "NPCs" ? "NPC" : "Potwory";
                var creatureTileset = Tilesets.FirstOrDefault(candidate =>
                    candidate.Name.Equals(group, StringComparison.OrdinalIgnoreCase));
                if (creatureTileset is not null && !ReferenceEquals(SelectedTileset, creatureTileset))
                    SelectedTileset = creatureTileset;
            }
            else if (SelectedPaletteSection == "House Palette")
            {
                _showRawOthers = false;
                _housePaletteWithoutTown = group == "No Town";
                _housePaletteTownFilterId = _housePaletteWithoutTown
                    ? null
                    : _map?.Towns.FirstOrDefault(town =>
                        town.Name.Equals(group, StringComparison.CurrentCultureIgnoreCase))?.Id;
                RebuildMapEntityPalettes();
                UpdatePalettePresentation();
                if (_map is not null)
                {
                    SelectedHouse = _map.Houses.FirstOrDefault(HouseMatchesNavigationFilter)
                                    ?? SelectedHouse;
                }
            }
        }
        finally
        {
            _synchronizingPaletteNavigation = false;
        }
        ActiveWorkspaceLabel = SelectedPaletteSection +
                               (string.IsNullOrWhiteSpace(group) ? string.Empty : $" · {group}");
    }

    private static bool IsPalettePlaceholder(string? group) =>
        string.IsNullOrWhiteSpace(group) || group is "Brak dostępnych grup" or "Wszystkie obiekty klienta";

    internal static IReadOnlyList<string> GetRmePaletteGroupNames(
        IEnumerable<RmeTilesetDefinition> tilesets,
        RmePaletteCategory category)
    {
        var names = tilesets
            .Where(tileset => tileset.Categories.TryGetValue(category, out var entries) && entries.Count > 0)
            .Select(tileset => tileset.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (category == RmePaletteCategory.Raw &&
            !names.Contains("Others", StringComparer.OrdinalIgnoreCase))
            names.Add("Others");
        return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private bool HouseMatchesNavigationFilter(OtbmHouse house)
    {
        if (_map is null) return false;
        if (_housePaletteWithoutTown)
            return house.TownId == 0 || _map.Towns.All(town => town.Id != house.TownId);
        return _housePaletteTownFilterId is null || house.TownId == _housePaletteTownFilterId;
    }

    private static int PaletteSectionIndex(string section) => section switch
    {
        "Terrain Palette" => 0,
        "Doodad Palette" => 1,
        "Item Palette" => 2,
        "RAW Palette" => 3,
        "Collections Palette" => 4,
        "Creature Palette" => 5,
        "House Palette" => 6,
        "Waypoint Palette" => 7,
        _ => -1
    };

    private static bool TryGetPaletteSection(int index, out string section)
    {
        section = index switch
        {
            0 => "Terrain Palette",
            1 => "Doodad Palette",
            2 => "Item Palette",
            3 => "RAW Palette",
            4 => "Collections Palette",
            5 => "Creature Palette",
            6 => "House Palette",
            7 => "Waypoint Palette",
            _ => string.Empty
        };
        return section.Length > 0;
    }

    private static bool TryGetMaterialPaletteCategory(string section, out RmePaletteCategory category)
    {
        category = section switch
        {
            "Terrain Palette" => RmePaletteCategory.Terrain,
            "Doodad Palette" => RmePaletteCategory.Doodad,
            "Item Palette" => RmePaletteCategory.Item,
            "RAW Palette" => RmePaletteCategory.Raw,
            "Collections Palette" => RmePaletteCategory.Collection,
            _ => default
        };
        return section is "Terrain Palette" or "Doodad Palette" or "Item Palette" or
            "RAW Palette" or "Collections Palette";
    }

    private static string WorkspaceLabel(int index) => index switch
    {
        0 => "Terrain Palette", 1 => "Doodad Palette", 2 => "Item Palette", 3 => "RAW Palette",
        4 => "Collections Palette", 5 => "Creature Palette", 6 => "House Palette",
        7 => "Waypoint Palette", 8 => "Town Editor", 9 => "Ustawienia widoku",
        10 => "Minimapa", 11 => "Właściwości mapy", 12 => "Import i wyszukiwanie",
        13 => "Konwersja", 14 => "Narzędzia", 15 => "Właściwości pola", _ => "Map Editor"
    };

    partial void OnBrushVariationChanged(int value)
    {
        if (value < 0) BrushVariation = 0;
        OnSelectedBrushChanged(SelectedBrush);
    }

    partial void OnAutomagicChanged(bool value)
    {
        UpdateBrushPreview();
    }

    partial void OnBrushSizeChanged(int value)
    {
        UpdateBrushPreview();
    }

    partial void OnBrushShapeChanged(string value)
    {
        UpdateBrushPreview();
    }

    partial void OnSelectionModeChanged(bool value)
    {
        if (value) SelectedBrush = null;
        Status = value
            ? "Tryb zaznaczania: przeciągnij lewym przyciskiem po mapie. Ctrl+C/X/V działa jak w RME."
            : "Tryb zaznaczania wyłączony.";
    }

    public int BrushVariationMaximum => Math.Max(0, (SelectedBrush?.VariationCount ?? 1) - 1);

    partial void OnSelectedPropertyItemChanged(MapTilePropertyItem? value)
    {
        if (value?.Item is not { } item)
        {
            ClearPropertyFields();
            return;
        }

        PropertyActionId = item.ActionId ?? 0;
        PropertyUniqueId = item.UniqueId ?? 0;
        PropertyCount = item.Count ?? 0;
        PropertyText = item.Text ?? string.Empty;
        PropertyDescription = item.Description ?? string.Empty;
        PropertyDepotId = item.DepotId ?? 0;
        PropertyHouseDoorId = item.HouseDoorId ?? 0;
        PropertyTier = item.Tier ?? 0;
        PropertyRuneCharges = item.RuneCharges ?? 0;
        PropertyDuration = item.Duration ?? 0;
        PropertyDecayingState = item.DecayingState ?? 0;
        PropertyWrittenDate = item.WrittenDate ?? 0;
        PropertyWrittenBy = item.WrittenBy ?? string.Empty;
        PropertySleeperGuid = item.SleeperGuid ?? 0;
        PropertySleepStart = item.SleepStart ?? 0;
        PropertyCharges = item.Charges ?? 0;
        LoadPodiumProperties(item.PodiumOutfit);
        PropertyCustomAttributes.Clear();
        SelectedPropertyCustomAttribute = null;
        foreach (var attribute in item.CustomAttributes.Where(attribute => !IsCanonicalCustomKey(attribute.Key)))
            PropertyCustomAttributes.Add(MapCustomAttributeItem.FromModel(attribute));
        PropertyTeleportX = item.TeleportX ?? 0;
        PropertyTeleportY = item.TeleportY ?? 0;
        PropertyTeleportZ = item.TeleportZ ?? 0;
        PropertyStatus = item.RawAttributeData.Length > 0
            ? "Po zatwierdzeniu znane atrybuty OTBM zostaną zapisane w kanonicznym układzie RME."
            : string.Empty;
    }

    private void LoadPodiumProperties(byte[]? podium)
    {
        PropertyHasPodiumOutfit = podium is { Length: 15 };
        PropertyPodiumShowPlatform = podium is { Length: 15 } && (podium[0] & 0x01) != 0;
        PropertyPodiumShowOutfit = podium is { Length: 15 } && (podium[0] & 0x02) != 0;
        PropertyPodiumShowMount = podium is { Length: 15 } && (podium[0] & 0x04) != 0;
        PropertyPodiumDirection = podium is { Length: 15 } ? podium[1] : 0;
        PropertyPodiumLookType = podium is { Length: 15 } ? BitConverter.ToUInt16(podium, 2) : 0;
        PropertyPodiumLookHead = podium is { Length: 15 } ? podium[4] : 0;
        PropertyPodiumLookBody = podium is { Length: 15 } ? podium[5] : 0;
        PropertyPodiumLookLegs = podium is { Length: 15 } ? podium[6] : 0;
        PropertyPodiumLookFeet = podium is { Length: 15 } ? podium[7] : 0;
        PropertyPodiumLookAddon = podium is { Length: 15 } ? podium[8] : 0;
        PropertyPodiumLookMount = podium is { Length: 15 } ? BitConverter.ToUInt16(podium, 9) : 0;
        PropertyPodiumMountHead = podium is { Length: 15 } ? podium[11] : 0;
        PropertyPodiumMountBody = podium is { Length: 15 } ? podium[12] : 0;
        PropertyPodiumMountLegs = podium is { Length: 15 } ? podium[13] : 0;
        PropertyPodiumMountFeet = podium is { Length: 15 } ? podium[14] : 0;
    }

    partial void OnSelectedTownChanged(OtbmTown? value)
    {
        if (value is null) return;
        TownId = value.Id > int.MaxValue ? int.MaxValue : (int)value.Id;
        TownName = value.Name;
        TownTempleX = value.TempleX;
        TownTempleY = value.TempleY;
        TownTempleZ = value.TempleZ;
    }

    partial void OnSelectedHouseChanged(OtbmHouse? value)
    {
        if (value is null) return;
        HouseId = value.Id > int.MaxValue ? int.MaxValue : (int)value.Id;
        HouseName = value.Name;
        HouseEntryX = value.EntryX;
        HouseEntryY = value.EntryY;
        HouseEntryZ = value.EntryZ;
        HouseRent = value.Rent > int.MaxValue ? int.MaxValue : (int)value.Rent;
        HouseTownId = value.TownId > int.MaxValue ? int.MaxValue : (int)value.TownId;
        HouseIsGuildhall = value.IsGuildhall;
    }

    partial void OnSelectedWaypointChanged(OtbmWaypoint? value)
    {
        if (value is null) return;
        WaypointName = value.Name;
        WaypointX = value.X;
        WaypointY = value.Y;
        WaypointZ = value.Z;
    }

    /// <summary>
    /// Klik LEWY na tile → postaw wybrany brush jako ground (jeśli kategoria=Grunt)
    /// lub dodaj item na stos (jeśli kategoria=Item/Container/Doodad).
    /// </summary>
    public void PlaceBrushAt(ushort x, ushort y)
    {
        if (_map is null || SelectedBrush is null) return;
        if (TryGetZoneFlag(SelectedBrush.EntityKind, out var zoneFlag, out var zoneName))
        {
            PaintZoneAt(x, y, zoneFlag, zoneName, enabled: true);
            return;
        }
        if (SelectedBrush.EntityKind == MapPaletteEntityKind.Creature)
        {
            PlaceCreatureAt(x, y, SelectedBrush);
            return;
        }
        if (SelectedBrush.EntityKind == MapPaletteEntityKind.Spawn)
        {
            PlaceSpawnAt(x, y);
            return;
        }
        if (SelectedBrush.EntityKind == MapPaletteEntityKind.OptionalBorder)
        {
            SetOptionalBorderAt(x, y, enabled: true);
            return;
        }
        if (SelectedBrush.EntityKind == MapPaletteEntityKind.House)
        {
            PaintHouseAt(x, y, SelectedBrush.EntityId);
            return;
        }
        if (SelectedBrush.EntityKind == MapPaletteEntityKind.Waypoint)
        {
            MoveWaypointTo(x, y, SelectedBrush.EntityIndex);
            return;
        }
        PlaceBrushPath([(x, y)]);
    }

    /// <summary>
    /// Nakłada cały odcinek zwykłego pędzla jako jedną operację mapy.
    /// Placement każdego punktu zachowuje własną losowość RME, natomiast
    /// automatyczne bordery, historia i unieważnienie cache wykonują się raz.
    /// </summary>
    public void PlaceBrushPath(IReadOnlyList<(ushort X, ushort Y)> centers)
    {
        if (_map is null || SelectedBrush is not { } selectedBrush || centers.Count == 0) return;

        // House i strefy obsługują przeciąganie, lecz nie korzystają z automatycznych
        // borderów. Pozostałe narzędzia specjalne są pojedynczym kliknięciem.
        if (selectedBrush.EntityKind != MapPaletteEntityKind.None)
        {
            foreach (var center in centers)
                PlaceBrushAt(center.X, center.Y);
            return;
        }

        var fallbackItemId = selectedBrush.ItemId is > 0 and <= ushort.MaxValue
            ? (ushort)selectedBrush.ItemId
            : (ushort)0;
        var changes = new Dictionary<OtbmTileCoord, OtbmTile?>();
        var isGroundBrush = selectedBrush.Brush?.Type.Equals("ground", StringComparison.OrdinalIgnoreCase) == true ||
                            (selectedBrush.Brush is null && _assets.IsGround(selectedBrush.ItemId));
        var isConnectedBrush = RmeConnectedBrushService.IsConnected(selectedBrush.Brush);
        var isDoodadBrush = selectedBrush.Brush?.Type.Equals("doodad", StringComparison.OrdinalIgnoreCase) == true;
        var needsAutomagicNeighbourhood = Automagic &&
            (isGroundBrush && _groundBorders is not null ||
             isConnectedBrush && _connectedBrushes is not null ||
             isDoodadBrush && selectedBrush.Brush!.RedoBorders);
        var shape = BrushShape.Equals("Okrąg", StringComparison.Ordinal)
            ? RmeBrushShape.Circle
            : RmeBrushShape.Square;
        var hasPlacement = false;

        foreach (var center in centers)
        {
            // Osobny placement jest celowy: doodady i warianty muszą być losowane
            // dla każdego punktu linii tak samo jak przy ręcznym malowaniu w RME.
            var placement = RmeBrushPlacementService.Create(
                selectedBrush.Brush,
                fallbackItemId,
                BrushVariation,
                BrushSize,
                shape,
                _brushRandom);
            if (placement.Tiles.Count == 0) continue;
            hasPlacement = true;

            if (needsAutomagicNeighbourhood)
            {
                foreach (var placementTile in placement.Tiles)
                {
                    CaptureNeighbourhood(
                        changes,
                        center.X + placementTile.X,
                        center.Y + placementTile.Y,
                        CurrentFloor + placementTile.Z);
                }
            }

            foreach (var placementTile in placement.Tiles)
            {
                var targetX = center.X + placementTile.X;
                var targetY = center.Y + placementTile.Y;
                var targetZ = CurrentFloor + placementTile.Z;
                if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue ||
                    targetZ is < 0 or > 15)
                    continue;

                var coord = new OtbmTileCoord((ushort)targetX, (ushort)targetY, (byte)targetZ);
                if (!changes.ContainsKey(coord))
                    changes[coord] = MapEditHistory.CloneTile(_map.Tiles.GetValueOrDefault(coord));
                if (!_map.Tiles.TryGetValue(coord, out var tile))
                {
                    if (isDoodadBrush && selectedBrush.Brush is { PlaceOnBlocking: false })
                        continue;
                    tile = new OtbmTile { X = coord.X, Y = coord.Y, Z = coord.Z };
                    _map.Tiles[coord] = tile;
                    _floorTileCounts[coord.Z] = _floorTileCounts.GetValueOrDefault(coord.Z) + 1;
                }

                if (isConnectedBrush && selectedBrush.Brush is not null && _connectedBrushes is not null)
                    _connectedBrushes.RemoveExistingBrushItems(tile, selectedBrush.Brush);

                if (isDoodadBrush && selectedBrush.Brush is { } doodadBrush)
                {
                    if (!doodadBrush.PlaceOnBlocking && IsTileBlocking(tile))
                        continue;
                    if (!doodadBrush.PlaceOnDuplicate &&
                        tile.Items.Any(item => doodadBrush.AllItemIds.Contains(item.Id)))
                        continue;
                }

                foreach (var itemId in placementTile.ItemIds)
                {
                    var shouldBecomeGround = isGroundBrush ||
                                             (selectedBrush.Brush is null && _assets.IsGround(itemId));
                    if (shouldBecomeGround && tile.GroundItemId == 0)
                    {
                        tile.GroundItemId = itemId;
                        tile.RawAttributeData = [];
                    }
                    else if (shouldBecomeGround && placementTile.ItemIds.Count == 1)
                    {
                        tile.GroundItemId = itemId;
                        tile.RawAttributeData = [];
                    }
                    else
                    {
                        tile.Items.Add(new OtbmItem { Id = itemId });
                    }
                }
            }
        }

        if (!hasPlacement)
        {
            Status = $"Brush {selectedBrush.Name} nie ma poprawnej definicji placementu.";
            return;
        }

        if (isGroundBrush && Automagic && _groundBorders is not null)
            _groundBorders.Reborder(_map, changes.Keys);
        if (isConnectedBrush && Automagic && _connectedBrushes is not null)
            _connectedBrushes.Rebuild(_map, changes.Keys, _brushRandom);
        if (isDoodadBrush && selectedBrush.Brush!.RedoBorders && Automagic)
        {
            _groundBorders?.Reborder(_map, changes.Keys);
            _connectedBrushes?.Rebuild(_map, changes.Keys, _brushRandom);
        }

        // CaptureNeighbourhood obejmuje także sąsiadów, których wynikowy
        // border nie uległ zmianie. Nie wysyłamy ich do historii ani cache.
        var actualChanges = changes
            .Where(pair => !TilesEqual(pair.Value, _map.Tiles.GetValueOrDefault(pair.Key)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (actualChanges.Count == 0) return;
        RecordTileChanges($"Brush {selectedBrush.Name}", actualChanges);
        RefreshAfterTileEdit();
        // Podczas przeciągania końcowy komunikat ustawia EndTileStroke. Pomijamy
        // tu budowę tekstu dla każdego pośredniego punktu linii pędzla.
        if (_activeTileStroke is null)
        {
            var last = centers[centers.Count - 1];
            Status = centers.Count == 1
                ? $"Postawiono {selectedBrush.Name} na ({last.X}, {last.Y}, {CurrentFloor}); zmieniono {actualChanges.Count} tile."
                : $"Narysowano {selectedBrush.Name}: {centers.Count} punktów, zmieniono {actualChanges.Count} tile.";
        }
    }

    private void PaintHouseAt(ushort x, ushort y, uint houseId)
    {
        if (_map is null || houseId == 0) return;
        var coordinate = new OtbmTileCoord(x, y, CurrentFloor);
        var before = MapEditHistory.CloneTile(_map.Tiles.GetValueOrDefault(coordinate));
        if (!_map.Tiles.TryGetValue(coordinate, out var tile))
        {
            tile = new OtbmTile { X = x, Y = y, Z = CurrentFloor };
            _map.Tiles[coordinate] = tile;
            _floorTileCounts[CurrentFloor] = _floorTileCounts.GetValueOrDefault(CurrentFloor) + 1;
        }
        if (tile.IsHouseTile && tile.HouseId == houseId) return;
        tile.IsHouseTile = true;
        tile.HouseId = houseId;
        RecordTileChanges($"House #{houseId}", new Dictionary<OtbmTileCoord, OtbmTile?>
        {
            [coordinate] = before
        });
        _externalDataDirty = true;
        RefreshAfterTileEdit();
        Status = $"Przypisano tile ({x}, {y}, {CurrentFloor}) do domu #{houseId}.";
    }

    private void ClearHouseAt(OtbmTileCoord coordinate, uint houseId)
    {
        if (_map is null || !_map.Tiles.TryGetValue(coordinate, out var tile) ||
            !tile.IsHouseTile || houseId != 0 && tile.HouseId != houseId)
        {
            Status = "Na tym polu nie ma przypisania do wybranego domu.";
            return;
        }
        var before = MapEditHistory.CloneTile(tile);
        tile.IsHouseTile = false;
        tile.HouseId = 0;
        if (tile.GroundItemId == 0 && tile.Items.Count == 0 && tile.Flags == 0)
        {
            _map.Tiles.Remove(coordinate);
            _floorTileCounts[coordinate.Z] = Math.Max(0, _floorTileCounts.GetValueOrDefault(coordinate.Z) - 1);
        }
        RecordTileChanges("Usuń House Tile", new Dictionary<OtbmTileCoord, OtbmTile?>
        {
            [coordinate] = before
        });
        _externalDataDirty = true;
        RefreshAfterTileEdit();
        Status = $"Usunięto przypisanie domu z ({coordinate.X}, {coordinate.Y}, {coordinate.Z}).";
    }

    private void MoveWaypointTo(ushort x, ushort y, int waypointIndex)
    {
        if (_map is null || waypointIndex < 0 || waypointIndex >= _map.Waypoints.Count) return;
        var waypoint = _map.Waypoints[waypointIndex];
        var before = new OtbmTileCoord(waypoint.X, waypoint.Y, waypoint.Z);
        var after = new OtbmTileCoord(x, y, CurrentFloor);
        waypoint.X = x;
        waypoint.Y = y;
        waypoint.Z = CurrentFloor;
        _history.PushWaypoint($"Waypoint {waypoint.Name}", new MapWaypointChange(waypointIndex, before, after));
        IndexExternalMapData(_map);
        UpdateHistoryState();
        RefreshViewport();
        Status = $"Przeniesiono waypoint '{waypoint.Name}' do ({x}, {y}, {CurrentFloor}).";
    }

    private void PlaceCreatureAt(ushort x, ushort y, MapPaletteItem brush)
    {
        if (_map is null || string.IsNullOrWhiteSpace(brush.EntityName)) return;
        var coordinate = new OtbmTileCoord(x, y, CurrentFloor);
        if (!_map.Tiles.TryGetValue(coordinate, out var tile) || tile.GroundItemId == 0)
        {
            Status = "RME: stworzenie można postawić tylko na istniejącym polu z groundem.";
            return;
        }
        if (IsTileBlocking(tile))
        {
            Status = "RME: wybrane pole blokuje ruch stworzeń.";
            return;
        }
        if (!brush.CreatureIsNpc && (tile.Flags & 0x01) != 0)
        {
            Status = "RME: potwora nie można postawić w Protection Zone; NPC jest dozwolony.";
            return;
        }

        var before = MapEditHistory.CloneSpawns(_map.Spawns);
        foreach (var existing in _map.Spawns)
            existing.Creatures.RemoveAll(creature =>
                creature.X == x && creature.Y == y && creature.Z == CurrentFloor);

        var spawn = _map.Spawns
            .Where(candidate => candidate.CenterZ == CurrentFloor &&
                                Math.Abs((int)candidate.CenterX - x) <= candidate.Radius &&
                                Math.Abs((int)candidate.CenterY - y) <= candidate.Radius)
            .OrderBy(candidate => Math.Abs((int)candidate.CenterX - x) + Math.Abs((int)candidate.CenterY - y))
            .FirstOrDefault();
        if (spawn is null)
        {
            // CreatureBrush w RME tworzy automatycznie lokalny spawn o promieniu 1.
            spawn = new OtbmSpawn { CenterX = x, CenterY = y, CenterZ = CurrentFloor, Radius = 1 };
            _map.Spawns.Add(spawn);
        }
        spawn.Creatures.Add(new OtbmCreature
        {
            Name = brush.EntityName,
            X = x,
            Y = y,
            Z = CurrentFloor,
            SpawnTime = Math.Max(1, DefaultSpawnTime),
            Direction = 0,
            IsNpc = brush.CreatureIsNpc
        });

        _history.PushSpawns($"Creature {brush.EntityName}", before, _map.Spawns);
        _externalDataDirty = true;
        IndexExternalMapData(_map);
        UpdateHistoryState();
        RefreshViewport();
        Status = $"Postawiono {(brush.CreatureIsNpc ? "NPC" : "monster")} {brush.EntityName} na ({x}, {y}, {CurrentFloor}).";
    }

    private void PlaceSpawnAt(ushort x, ushort y)
    {
        if (_map is null) return;
        var coordinate = new OtbmTileCoord(x, y, CurrentFloor);
        if (!_map.Tiles.ContainsKey(coordinate))
        {
            Status = "RME: centrum spawnu można postawić tylko na istniejącym polu mapy.";
            return;
        }
        if (_map.Spawns.Any(spawn => spawn.CenterX == x && spawn.CenterY == y && spawn.CenterZ == CurrentFloor))
        {
            Status = $"Na ({x}, {y}, {CurrentFloor}) istnieje już centrum spawnu.";
            return;
        }

        var before = MapEditHistory.CloneSpawns(_map.Spawns);
        _map.Spawns.Add(new OtbmSpawn
        {
            CenterX = x,
            CenterY = y,
            CenterZ = CurrentFloor,
            Radius = Math.Max(1, SpawnRadius)
        });
        _history.PushSpawns("Spawn Brush", before, _map.Spawns);
        _externalDataDirty = true;
        IndexExternalMapData(_map);
        UpdateHistoryState();
        RefreshViewport();
        Status = $"Dodano spawn radius={Math.Max(1, SpawnRadius)} na ({x}, {y}, {CurrentFloor}).";
    }

    public void SelectMapTool(string tool)
    {
        if (string.Equals(tool, "Creature", StringComparison.OrdinalIgnoreCase))
        {
            SelectedWorkspaceTabIndex = 5;
            SelectedBrush = null;
            Status = "Wybierz potwora albo NPC z palety Stworzenia.";
            return;
        }
        if (string.Equals(tool, "House", StringComparison.OrdinalIgnoreCase))
        {
            SelectedWorkspaceTabIndex = 6;
            SelectedBrush = null;
            Status = "Wybierz dom i jego brush w palecie Domy.";
            return;
        }

        var kind = tool switch
        {
            "ProtectionZone" => MapPaletteEntityKind.ProtectionZone,
            "NoPvpZone" => MapPaletteEntityKind.NoPvpZone,
            "NoLogoutZone" => MapPaletteEntityKind.NoLogoutZone,
            "PvpZone" => MapPaletteEntityKind.PvpZone,
            "Spawn" => MapPaletteEntityKind.Spawn,
            "OptionalBorder" => MapPaletteEntityKind.OptionalBorder,
            _ => MapPaletteEntityKind.None
        };
        if (kind == MapPaletteEntityKind.None)
        {
            SelectedBrush = null;
            Status = "Wyczyszczono aktywne narzędzie.";
            return;
        }

        SelectedBrush = new MapPaletteItem
        {
            Name = kind switch
            {
                MapPaletteEntityKind.ProtectionZone => "Protection Zone",
                MapPaletteEntityKind.NoPvpZone => "No PvP Zone",
                MapPaletteEntityKind.NoLogoutZone => "No Logout Zone",
                MapPaletteEntityKind.PvpZone => "PvP Zone",
                MapPaletteEntityKind.Spawn => "Spawn Brush",
                _ => "Optional Border Tool"
            },
            EntityName = tool,
            EntityKind = kind,
            Category = kind == MapPaletteEntityKind.Spawn
                ? RmePaletteCategory.Creature
                : RmePaletteCategory.Terrain
        };
        Status = $"Aktywne narzędzie: {SelectedBrush.Name}. Lewy przycisk dodaje, Shift+PPM usuwa.";
    }

    private void PaintZoneAt(ushort x, ushort y, uint flag, string zoneName, bool enabled, int? size = null)
    {
        if (_map is null) return;
        var changes = new Dictionary<OtbmTileCoord, OtbmTile?>();
        var shape = BrushShape.Equals("Okrąg", StringComparison.Ordinal)
            ? RmeBrushShape.Circle
            : RmeBrushShape.Square;
        foreach (var (offsetX, offsetY) in RmeBrushPlacementService.GetAreaOffsets(size ?? BrushSize, shape))
        {
            var targetX = x + offsetX;
            var targetY = y + offsetY;
            if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue) continue;
            var coordinate = new OtbmTileCoord((ushort)targetX, (ushort)targetY, CurrentFloor);
            if (!_map.Tiles.TryGetValue(coordinate, out var tile)) continue;
            var updatedFlags = enabled ? tile.Flags | flag : tile.Flags & ~flag;
            if (updatedFlags == tile.Flags) continue;
            changes[coordinate] = MapEditHistory.CloneTile(tile);
            tile.Flags = updatedFlags;
            tile.RawAttributeData = [];
        }

        if (changes.Count == 0)
        {
            Status = enabled
                ? $"Nie znaleziono istniejących pól do oznaczenia jako {zoneName}."
                : $"Na wskazanym obszarze nie ma strefy {zoneName}.";
            return;
        }
        RecordTileChanges($"{(enabled ? "Dodaj" : "Usuń")} {zoneName}", changes);
        RefreshAfterTileEdit();
        Status = $"{(enabled ? "Oznaczono" : "Wyczyszczono")} {changes.Count} pól: {zoneName}.";
    }

    private static bool TryGetZoneFlag(MapPaletteEntityKind kind, out uint flag, out string name)
    {
        (flag, name) = kind switch
        {
            MapPaletteEntityKind.ProtectionZone => (0x01u, "Protection Zone"),
            MapPaletteEntityKind.NoPvpZone => (0x04u, "No PvP Zone"),
            MapPaletteEntityKind.NoLogoutZone => (0x08u, "No Logout Zone"),
            MapPaletteEntityKind.PvpZone => (0x10u, "PvP Zone"),
            _ => (0u, string.Empty)
        };
        return flag != 0;
    }

    private bool IsTileBlocking(OtbmTile tile) =>
        (tile.GroundItemId != 0 && _assets.IsUnpassable(tile.GroundItemId)) ||
        tile.Items.Any(item => _assets.IsUnpassable(item.Id));

    private void SetOptionalBorderAt(ushort x, ushort y, bool enabled)
    {
        if (_map is null || _materials is null || _groundBorders is null) return;
        var coordinate = new OtbmTileCoord(x, y, CurrentFloor);
        _map.Tiles.TryGetValue(coordinate, out var tile);

        if (enabled)
        {
            if (tile is not null && _materials.GroundBrushesByItemId.TryGetValue(tile.GroundItemId, out var ownGround) &&
                ownGround.OptionalBorder is not null)
            {
                Status = "RME: Optional Border Tool nie rysuje bezpośrednio na groundzie, który definiuje ten border.";
                return;
            }

            var hasCompatibleNeighbour = false;
            for (var offsetY = -1; offsetY <= 1 && !hasCompatibleNeighbour; offsetY++)
            for (var offsetX = -1; offsetX <= 1 && !hasCompatibleNeighbour; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0) continue;
                var neighbourX = x + offsetX;
                var neighbourY = y + offsetY;
                if (neighbourX is < 0 or > ushort.MaxValue || neighbourY is < 0 or > ushort.MaxValue ||
                    !_map.Tiles.TryGetValue(new((ushort)neighbourX, (ushort)neighbourY, CurrentFloor), out var neighbour))
                    continue;
                hasCompatibleNeighbour = _materials.GroundBrushesByItemId.TryGetValue(neighbour.GroundItemId, out var ground) &&
                                         ground.OptionalBorder is not null;
            }
            if (!hasCompatibleNeighbour)
            {
                Status = "RME: obok pola nie ma ground brushu z opcjonalnym borderem.";
                return;
            }
        }
        else if (tile is null || !tile.Items.Any(item => _materials.OptionalBorderItemIds.Contains(item.Id)))
        {
            Status = "Na tym polu nie ma opcjonalnego borderu.";
            return;
        }

        var before = MapEditHistory.CloneTile(tile);
        if (tile is null)
        {
            tile = new OtbmTile { X = x, Y = y, Z = CurrentFloor };
            _map.Tiles[coordinate] = tile;
            _floorTileCounts[CurrentFloor] = _floorTileCounts.GetValueOrDefault(CurrentFloor) + 1;
        }
        _groundBorders.ReborderTile(_map, tile, enabled);
        if (tile.GroundItemId == 0 && tile.Items.Count == 0 && tile.Flags == 0 && !tile.IsHouseTile)
        {
            _map.Tiles.Remove(coordinate);
            _floorTileCounts[CurrentFloor] = Math.Max(0, _floorTileCounts.GetValueOrDefault(CurrentFloor) - 1);
        }

        _history.Push(enabled ? "Optional Border Tool" : "Usuń optional border",
            coordinate, before, _map.Tiles.GetValueOrDefault(coordinate));
        UpdateMinimapSourceOverrides([coordinate]);
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = enabled ? "Dodano opcjonalny border RME." : "Usunięto opcjonalny border RME.";
    }

    /// <summary>
    /// Klik PRAWY na tile → usuń ostatni item na stosie; jeśli brak items, wyczyść ground.
    /// </summary>
    public void EraseAt(ushort x, ushort y) => ErasePath([(x, y)]);

    /// <summary>
    /// Usuwa cały odcinek pędzla w jednej transakcji. Kolejność oraz powtórzone
    /// pola są zachowane, ale automatyczne bordery i connected brushe są liczone raz.
    /// </summary>
    public void ErasePath(IReadOnlyList<(ushort X, ushort Y)> centers)
    {
        if (_map is null || centers.Count == 0) return;

        // Strefy i encje mają własną semantykę (XML spawnów, flagi, domy).
        // Obsługujemy je dotychczasową drogą; aktywny stroke nadal scala Undo.
        if (SelectedBrush?.EntityKind is not (MapPaletteEntityKind.None or null))
        {
            foreach (var center in centers)
                EraseSpecialAt(center.X, center.Y);
            return;
        }

        var selectedBrush = SelectedBrush;
        var selectedDefinition = selectedBrush?.Brush;
        if (selectedDefinition is null && selectedBrush?.ItemId is > 0 and <= ushort.MaxValue && _materials is not null)
        {
            _materials.GroundBrushesByItemId.TryGetValue((ushort)selectedBrush.ItemId, out selectedDefinition);
            if (selectedDefinition is null)
                _materials.ItemBrushesByItemId.TryGetValue((ushort)selectedBrush.ItemId, out selectedDefinition);
        }

        var selectedIsGround = selectedDefinition?.Type.Equals("ground", StringComparison.OrdinalIgnoreCase) == true ||
                               selectedBrush is { Brush: null, ItemId: > 0 } && _assets.IsGround(selectedBrush.ItemId);
        var rebuildGround = selectedIsGround && Automagic && _groundBorders is not null;
        var rebuildConnected = RmeConnectedBrushService.IsConnected(selectedDefinition) &&
                               Automagic && _connectedBrushes is not null;
        var shape = BrushShape.Equals("Okrąg", StringComparison.Ordinal)
            ? RmeBrushShape.Circle
            : RmeBrushShape.Square;
        var offsets = RmeBrushPlacementService.GetAreaOffsets(BrushSize, shape);
        var changes = new Dictionary<OtbmTileCoord, OtbmTile?>();
        var changedTargets = new HashSet<OtbmTileCoord>();
        var removedCount = 0;

        foreach (var center in centers)
        foreach (var (offsetX, offsetY) in offsets)
        {
            var targetX = center.X + offsetX;
            var targetY = center.Y + offsetY;
            if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue) continue;

            var coordinate = new OtbmTileCoord((ushort)targetX, (ushort)targetY, CurrentFloor);
            if (!_map.Tiles.TryGetValue(coordinate, out var tile)) continue;

            // Snapshot powstaje przed pierwszą mutacją. Nie deduplikujemy targetów:
            // bez wybranego brushu kolejne przejście zdejmuje kolejny element stosu.
            var before = changes.ContainsKey(coordinate) ? null : MapEditHistory.CloneTile(tile);
            var removedHere = EraseTileContent(tile, selectedBrush, selectedDefinition);
            if (removedHere == 0) continue;
            if (before is not null) changes.Add(coordinate, before);
            changedTargets.Add(coordinate);
            removedCount += removedHere;
        }

        if (removedCount == 0)
        {
            if (_activeTileStroke is null)
                Status = selectedBrush is null
                    ? "Na wskazanym odcinku nie ma elementów do usunięcia."
                    : $"Na wskazanym odcinku nie ma elementów brushu {selectedBrush.Name}.";
            return;
        }

        // Sąsiedztwo przechwytujemy po zwykłych mutacjach, lecz przed jedynym
        // przebiegiem borderów. Zmienione targety zachowują już pierwotny snapshot.
        if (rebuildGround || rebuildConnected)
            foreach (var coordinate in changedTargets)
                CaptureNeighbourhood(changes, coordinate.X, coordinate.Y, coordinate.Z);

        if (rebuildGround)
            _groundBorders!.Reborder(_map, changes.Keys);
        if (rebuildConnected)
            _connectedBrushes!.Rebuild(_map, changes.Keys, _brushRandom);

        // Puste pole usuwamy dopiero po borderowaniu, ponieważ sąsiedni ground może
        // prawidłowo utworzyć na nim zewnętrzny border.
        foreach (var coordinate in changedTargets)
        {
            if (!_map.Tiles.TryGetValue(coordinate, out var tile) ||
                tile.GroundItemId != 0 || tile.Items.Count != 0 || tile.Flags != 0 || tile.IsHouseTile)
                continue;
            _map.Tiles.Remove(coordinate);
            _floorTileCounts[coordinate.Z] = Math.Max(0, _floorTileCounts.GetValueOrDefault(coordinate.Z) - 1);
        }

        var actualChanges = changes
            .Where(pair => !TilesEqual(pair.Value, _map.Tiles.GetValueOrDefault(pair.Key)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (actualChanges.Count == 0) return;

        var description = $"Eraser {selectedBrush?.Name ?? "tile"}";
        RecordTileChanges(description, actualChanges);
        RefreshAfterTileEdit();
        if (_activeTileStroke is null)
            Status = $"Usunięto {removedCount} elementów na {changedTargets.Count} polach.";
    }

    private void EraseSpecialAt(ushort x, ushort y)
    {
        if (_map is null) return;
        var coord = new OtbmTileCoord(x, y, CurrentFloor);
        if (SelectedBrush is not null &&
            TryGetZoneFlag(SelectedBrush.EntityKind, out var zoneFlag, out var zoneName))
        {
            PaintZoneAt(x, y, zoneFlag, zoneName, enabled: false);
            return;
        }
        if (SelectedBrush?.EntityKind == MapPaletteEntityKind.OptionalBorder)
        {
            SetOptionalBorderAt(x, y, enabled: false);
            return;
        }
        if (SelectedBrush?.EntityKind is MapPaletteEntityKind.Creature or MapPaletteEntityKind.Spawn)
        {
            EraseMapEntityAt(coord, SelectedBrush.EntityKind);
            return;
        }
        if (SelectedBrush?.EntityKind == MapPaletteEntityKind.House)
        {
            ClearHouseAt(coord, SelectedBrush.EntityId);
        }
    }

    private int EraseTileContent(
        OtbmTile tile,
        MapPaletteItem? selectedBrush,
        RmeBrushDefinition? selectedDefinition)
    {
        if (selectedDefinition?.Type.Equals("ground", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (_materials?.GroundBrushesByItemId.GetValueOrDefault(tile.GroundItemId) is { } tileGround &&
                ReferenceEquals(tileGround, selectedDefinition))
            {
                tile.GroundItemId = 0;
                tile.RawAttributeData = [];
                return 1;
            }
            return 0;
        }
        else if (RmeConnectedBrushService.IsConnected(selectedDefinition) && _connectedBrushes is not null)
        {
            var beforeCount = tile.Items.Count;
            _connectedBrushes.RemoveExistingBrushItems(tile, selectedDefinition!);
            return beforeCount - tile.Items.Count;
        }
        else if (selectedDefinition?.Type.Equals("doodad", StringComparison.OrdinalIgnoreCase) == true && _materials is not null)
        {
            // Domyślne DOODAD_BRUSH_ERASE_LIKE=0 w RME usuwa wszystkie doodady z pola.
            var removed = tile.Items.Count(item =>
                    _materials.ItemBrushesByItemId.TryGetValue(item.Id, out var brush) &&
                    brush.Type.Equals("doodad", StringComparison.OrdinalIgnoreCase));
            tile.Items.RemoveAll(item =>
                _materials.ItemBrushesByItemId.TryGetValue(item.Id, out var brush) &&
                brush.Type.Equals("doodad", StringComparison.OrdinalIgnoreCase));
            if (_materials.ItemBrushesByItemId.TryGetValue(tile.GroundItemId, out var groundDoodad) &&
                groundDoodad.Type.Equals("doodad", StringComparison.OrdinalIgnoreCase))
            {
                removed++;
                tile.GroundItemId = 0;
                tile.RawAttributeData = [];
            }
            return removed;
        }
        else if (selectedBrush?.ItemId is > 0 and <= ushort.MaxValue)
        {
            var selectedId = (ushort)selectedBrush.ItemId;
            var removed = tile.Items.Count(item => item.Id == selectedId);
            tile.Items.RemoveAll(item => item.Id == selectedId);
            if (tile.GroundItemId == selectedId)
            {
                removed++;
                tile.GroundItemId = 0;
                tile.RawAttributeData = [];
            }
            return removed;
        }
        else if (tile.Items.Count > 0)
        {
            tile.Items.RemoveAt(tile.Items.Count - 1);
            return 1;
        }
        else if (tile.GroundItemId != 0)
        {
            tile.GroundItemId = 0;
            tile.RawAttributeData = [];
            return 1;
        }
        return 0;
    }

    public bool BeginTileStroke(bool erasing)
    {
        if (_activeTileStroke is not null ||
            SelectedBrush?.EntityKind is not (MapPaletteEntityKind.None or MapPaletteEntityKind.House or
                MapPaletteEntityKind.ProtectionZone or MapPaletteEntityKind.NoPvpZone or
                MapPaletteEntityKind.NoLogoutZone or MapPaletteEntityKind.PvpZone or null))
            return false;
        _activeTileStroke = [];
        _strokeViewportDirty = false;
        _activeTileStrokeDescription = erasing
            ? $"Eraser {SelectedBrush?.Name ?? "tile"}"
            : $"Brush {SelectedBrush?.Name ?? "tile"}";
        return true;
    }

    public void EndTileStroke()
    {
        if (_activeTileStroke is not { } changes || _map is null) return;
        _activeTileStroke = null;
        FlushTileStrokeViewport();
        var editChanges = changes
            .Where(pair => pair.Value is not null || _map.Tiles.ContainsKey(pair.Key))
            .Select(pair => new MapTileChange(pair.Key, pair.Value, _map.Tiles.GetValueOrDefault(pair.Key)))
            .ToArray();
        if (editChanges.Length == 0) return;
        _history.Push(_activeTileStrokeDescription, editChanges);
        UpdateHistoryState();
        RequestMinimapRefresh();
        Status = $"{_activeTileStrokeDescription}: zmieniono {editChanges.Length} tile.";
    }

    private void RecordTileChanges(string description, IReadOnlyDictionary<OtbmTileCoord, OtbmTile?> changes)
    {
        InvalidateViewportTileCache(changes);
        UpdateMinimapSourceOverrides(changes.Keys);
        foreach (var (coordinate, before) in changes)
            RememberModifiedBaseline(coordinate, before);

        if (_activeTileStroke is { } stroke)
        {
            foreach (var (coordinate, before) in changes)
                stroke.TryAdd(coordinate, before);
            IsDirty = true;
            return;
        }

        if (_map is null) return;
        var editChanges = changes
            .Where(pair => pair.Value is not null || _map.Tiles.ContainsKey(pair.Key))
            .Select(pair => new MapTileChange(pair.Key, pair.Value, _map.Tiles.GetValueOrDefault(pair.Key)))
            .ToArray();
        if (editChanges.Length == 0) return;
        _history.Push(description, editChanges);
        UpdateHistoryState();
    }

    private void RefreshAfterTileEdit()
    {
        if (_activeTileStroke is not null)
        {
            _strokeViewportDirty = true;
            return;
        }

        RefreshViewport(reuseTileContent: true);
    }

    /// <summary>
    /// Kończy paczkę zmian powstałą w jednym zdarzeniu myszy. Cały odcinek
    /// pędzla jest dzięki temu rysowany jednym przebiegiem.
    /// </summary>
    public void FlushTileStrokeViewport()
    {
        if (!_strokeViewportDirty) return;
        _strokeViewportDirty = false;
        RefreshViewport(reuseTileContent: true);
    }

    private void InvalidateViewportTileCache(IReadOnlyDictionary<OtbmTileCoord, OtbmTile?> changes)
    {
        if (_viewportTileCache.Count == 0 || _map is null) return;
        var invalidated = new HashSet<OtbmTileCoord>();
        foreach (var (coordinate, before) in changes)
        {
            // Światło ma zasięg ośmiu pól, lecz zwykła edycja gruntu nie może przez to
            // wyrzucać z cache obszaru 17x17. Szeroki promień jest potrzebny wyłącznie
            // wtedy, gdy edycja faktycznie zmieniła efektywne źródło światła.
            var after = _map.Tiles.GetValueOrDefault(coordinate);
            var lightChanged = ShowLights && !HaveEqualTileLights(before, after);
            if (lightChanged) _lightViewportCache = null;
            var radius = lightChanged ? 8 : 0;
            var projectedX = (int)coordinate.X;
            var projectedY = (int)coordinate.Y;
            if (coordinate.Z > CurrentFloor && ShowAllFloors)
            {
                var offset = coordinate.Z - CurrentFloor;
                projectedX += offset;
                projectedY += offset;
            }
            else if (coordinate.Z + 1 == CurrentFloor && GhostHigherFloors)
            {
                projectedX--;
                projectedY--;
            }
            else if (coordinate.Z != CurrentFloor)
            {
                continue;
            }

            for (var offsetX = -radius; offsetX <= radius; offsetX++)
            for (var offsetY = -radius; offsetY <= radius; offsetY++)
            {
                var x = projectedX + offsetX;
                var y = projectedY + offsetY;
                if (x is < 0 or > ushort.MaxValue || y is < 0 or > ushort.MaxValue) continue;
                invalidated.Add(new OtbmTileCoord((ushort)x, (ushort)y, CurrentFloor));
            }
        }

        // Sąsiedztwa pędzla mocno na siebie zachodzą. Najpierw je scalamy, dzięki
        // czemu każdy wpis słownika usuwamy najwyżej raz na paczkę ruchu myszy.
        foreach (var coordinate in invalidated)
            _viewportTileCache.Remove(coordinate);
    }

    private bool HaveEqualTileLights(OtbmTile? before, OtbmTile? after)
    {
        var beforeLights = Collect(before);
        var afterLights = Collect(after);
        if (beforeLights.Count != afterLights.Count) return false;
        foreach (var (color, level) in beforeLights)
        {
            if (!afterLights.TryGetValue(color, out var afterLevel) || afterLevel != level)
                return false;
        }
        return true;

        Dictionary<ushort, ushort> Collect(OtbmTile? tile)
        {
            var result = new Dictionary<ushort, ushort>();
            if (tile is null) return result;
            Add(_assets.GetLight(tile.GroundItemId));
            foreach (var item in tile.Items)
                Add(_assets.GetLight(item.Id));
            return result;

            void Add((ushort Level, ushort Color)? candidate)
            {
                if (candidate is not { Level: > 0 } value) return;
                result[value.Color] = Math.Max(result.GetValueOrDefault(value.Color), value.Level);
            }
        }
    }

    private void EraseMapEntityAt(OtbmTileCoord coordinate, MapPaletteEntityKind tool)
    {
        if (_map is null) return;
        var before = MapEditHistory.CloneSpawns(_map.Spawns);
        var removed = 0;
        if (tool == MapPaletteEntityKind.Spawn)
        {
            var spawn = _map.Spawns.FirstOrDefault(candidate =>
                candidate.CenterX == coordinate.X && candidate.CenterY == coordinate.Y && candidate.CenterZ == coordinate.Z);
            if (spawn is not null)
            {
                removed = 1 + spawn.Creatures.Count;
                _map.Spawns.Remove(spawn);
            }
        }
        else
        {
            foreach (var spawn in _map.Spawns)
                removed += spawn.Creatures.RemoveAll(creature =>
                    creature.X == coordinate.X && creature.Y == coordinate.Y && creature.Z == coordinate.Z);
        }

        if (removed == 0)
        {
            Status = tool == MapPaletteEntityKind.Spawn
                ? "Na tym polu nie ma centrum spawnu."
                : "Na tym polu nie ma stworzenia.";
            return;
        }

        var description = tool == MapPaletteEntityKind.Spawn ? "Usuń spawn" : "Usuń creature";
        _history.PushSpawns(description, before, _map.Spawns);
        _externalDataDirty = true;
        IndexExternalMapData(_map);
        UpdateHistoryState();
        RefreshViewport();
        Status = tool == MapPaletteEntityKind.Spawn
            ? $"Usunięto centrum spawnu i {removed - 1} przypisanych stworzeń."
            : "Usunięto stworzenie.";
    }

    private void CaptureNeighbourhood(
        IDictionary<OtbmTileCoord, OtbmTile?> changes,
        int centerX,
        int centerY,
        int centerZ)
    {
        if (_map is null) return;
        foreach (var offsetY in Enumerable.Range(-1, 3))
        foreach (var offsetX in Enumerable.Range(-1, 3))
        {
            var affectedX = centerX + offsetX;
            var affectedY = centerY + offsetY;
            if (affectedX is < 0 or > ushort.MaxValue || affectedY is < 0 or > ushort.MaxValue ||
                centerZ is < 0 or > 15) continue;
            var affected = new OtbmTileCoord((ushort)affectedX, (ushort)affectedY, (byte)centerZ);
            if (!changes.ContainsKey(affected))
                changes[affected] = MapEditHistory.CloneTile(_map.Tiles.GetValueOrDefault(affected));
        }
    }

    [RelayCommand]
    private void SelectBrush(MapPaletteItem? item) => SelectedBrush = item;

    public void BeginSelection(ushort x, ushort y, bool additive)
    {
        if (_map is null) return;
        ResetSelectionMoveState();
        SelectionMode = true;
        _selectionAnchor = new OtbmTileCoord(x, y, CurrentFloor);
        _selectionBase.Clear();
        if (additive)
            _selectionBase.UnionWith(_selectedCoordinates);
        else
            _selectedCoordinates.Clear();
        UpdateSelection(x, y);
    }

    public void UpdateSelection(ushort x, ushort y)
    {
        if (_map is null || _selectionAnchor is not { } anchor || anchor.Z != CurrentFloor) return;
        var minX = Math.Min(anchor.X, x);
        var maxX = Math.Max(anchor.X, x);
        var minY = Math.Min(anchor.Y, y);
        var maxY = Math.Max(anchor.Y, y);
        var floors = SelectionFloors().ToArray();
        var area = (long)(maxX - minX + 1) * (maxY - minY + 1) * floors.Length;
        if (area > 1_000_000)
        {
            Status = "Zaznaczenie przekracza bezpieczny limit 1 000 000 pól.";
            return;
        }

        _selectedCoordinates.Clear();
        _selectedCoordinates.UnionWith(_selectionBase);
        foreach (var floor in floors)
        {
            var compensation = CompensateSelection && CurrentFloor <= 7
                ? CurrentFloor - Math.Min(floor, (byte)7)
                : 0;
            var adjustedMinX = Math.Max(0, (int)minX + compensation);
            var adjustedMaxX = Math.Min(ushort.MaxValue, (int)maxX + compensation);
            var adjustedMinY = Math.Max(0, (int)minY + compensation);
            var adjustedMaxY = Math.Min(ushort.MaxValue, (int)maxY + compensation);
            for (var tileY = adjustedMinY; tileY <= adjustedMaxY; tileY++)
            for (var tileX = adjustedMinX; tileX <= adjustedMaxX; tileX++)
            {
                var coordinate = new OtbmTileCoord((ushort)tileX, (ushort)tileY, floor);
                // RME nie tworzy zaznaczenia na pustym obszarze. Dzięki temu drag
                // obejmuje wyłącznie realne tile i późniejsze przenoszenie nie
                // materializuje przypadkowych, pustych pól mapy.
                if (_map.Tiles.ContainsKey(coordinate))
                    _selectedCoordinates.Add(coordinate);
            }
        }
        UpdateSelectionState();
        RefreshViewport();
    }

    public void EndSelection()
    {
        _selectionAnchor = null;
        _selectionBase.Clear();
        Status = SelectionLabel + ".";
    }

    /// <summary>
    /// Sprawdza, czy pole jest częścią aktywnego zaznaczenia. Widok używa tej
    /// informacji, aby kliknięcie zaznaczonego tile rozpoczęło drag zamiast
    /// tworzyć od początku prostokąt zaznaczenia.
    /// </summary>
    public bool IsTileSelected(ushort x, ushort y)
    {
        var coordinate = new OtbmTileCoord(x, y, CurrentFloor);
        return IsSelectionMoveActive
            ? TryGetSelectionMoveSourceCoordinate(coordinate, out _)
            : _selectedCoordinates.Contains(coordinate);
    }

    /// <summary>Rozpoczyna bezstratny podgląd przesuwania aktualnego zaznaczenia.</summary>
    public bool BeginSelectionMove(ushort x, ushort y)
    {
        if (_map is null || !IsTileSelected(x, y) || _selectedCoordinates.Count == 0)
            return false;

        _selectionAnchor = null;
        _selectionBase.Clear();
        _selectionMoveSource = [.. _selectedCoordinates];
        _selectionMoveAnchor = new OtbmTileCoord(x, y, CurrentFloor);
        _selectionMoveOffsetX = 0;
        _selectionMoveOffsetY = 0;
        _selectionMoveMinX = ushort.MaxValue;
        _selectionMoveMaxX = ushort.MinValue;
        _selectionMoveMinY = ushort.MaxValue;
        _selectionMoveMaxY = ushort.MinValue;
        foreach (var coordinate in _selectionMoveSource)
        {
            _selectionMoveMinX = Math.Min(_selectionMoveMinX, coordinate.X);
            _selectionMoveMaxX = Math.Max(_selectionMoveMaxX, coordinate.X);
            _selectionMoveMinY = Math.Min(_selectionMoveMinY, coordinate.Y);
            _selectionMoveMaxY = Math.Max(_selectionMoveMaxY, coordinate.Y);
        }
        Status = $"Przenoszenie {_selectionMoveSource.Count} pól — przeciągnij i puść, aby zatwierdzić.";
        return true;
    }

    /// <summary>
    /// Aktualizuje wyłącznie półprzezroczysty podgląd celu. Dane mapy są
    /// modyfikowane dopiero przy puszczeniu myszy, więc cały drag tworzy jedno Undo.
    /// </summary>
    public void UpdateSelectionMove(ushort x, ushort y)
    {
        if (_selectionMoveSource is not { Count: > 0 } ||
            _selectionMoveAnchor is not { } anchor)
            return;

        // Granice są policzone raz przy rozpoczęciu drag. Ruch myszy wykonuje
        // dzięki temu stałą liczbę operacji niezależnie od liczby zaznaczonych pól.
        var offsetX = Math.Clamp(
            (int)x - anchor.X,
            -_selectionMoveMinX,
            ushort.MaxValue - _selectionMoveMaxX);
        var offsetY = Math.Clamp(
            (int)y - anchor.Y,
            -_selectionMoveMinY,
            ushort.MaxValue - _selectionMoveMaxY);
        if (offsetX == _selectionMoveOffsetX && offsetY == _selectionMoveOffsetY) return;

        _selectionMoveOffsetX = offsetX;
        _selectionMoveOffsetY = offsetY;
        Status = $"Przesunięcie zaznaczenia: X {offsetX:+#;-#;0}, Y {offsetY:+#;-#;0}.";
    }

    /// <summary>Zatwierdza cały drag jako jedną atomową operację historii.</summary>
    public void EndSelectionMove()
    {
        if (_selectionMoveSource is not { Count: > 0 } source) return;
        var original = source.ToArray();
        var offsetX = _selectionMoveOffsetX;
        var offsetY = _selectionMoveOffsetY;
        ResetSelectionMoveState();

        if (offsetX == 0 && offsetY == 0)
        {
            _selectedCoordinates.Clear();
            _selectedCoordinates.UnionWith(original);
            UpdateSelectionState();
            RefreshViewport(updateStatus: false);
            Status = SelectionLabel + ".";
            return;
        }

        MoveSelectionCore(original, offsetX, offsetY);
    }

    /// <summary>Anuluje drag i odtwarza zaznaczenie w pozycji źródłowej.</summary>
    public void CancelSelectionMove()
    {
        if (_selectionMoveSource is not { } source) return;
        _selectedCoordinates.Clear();
        _selectedCoordinates.UnionWith(source);
        ResetSelectionMoveState();
        UpdateSelectionState();
        RefreshViewport(updateStatus: false);
        Status = "Anulowano przenoszenie zaznaczenia.";
    }

    private void ResetSelectionMoveState()
    {
        _selectionMoveSource = null;
        _selectionMoveAnchor = null;
        _selectionMoveOffsetX = 0;
        _selectionMoveOffsetY = 0;
        _selectionMoveMinX = 0;
        _selectionMoveMaxX = 0;
        _selectionMoveMinY = 0;
        _selectionMoveMaxY = 0;
    }

    private bool IsSelectionMoveActive =>
        _selectionMoveSource is { Count: > 0 } &&
        (_selectionMoveOffsetX != 0 || _selectionMoveOffsetY != 0);

    /// <summary>
    /// Mapuje widoczne pole docelowe na pole źródłowe w O(1). Podczas drag
    /// sprawdzamy tylko kafelki aktualnego viewportu, zamiast budować słownik
    /// obejmujący nawet milion zaznaczonych pól przy każdej klatce.
    /// </summary>
    private bool TryGetSelectionMoveSourceCoordinate(
        OtbmTileCoord destination,
        out OtbmTileCoord sourceCoordinate)
    {
        sourceCoordinate = default;
        if (!IsSelectionMoveActive || _selectionMoveSource is not { } source) return false;

        var sourceX = (int)destination.X - _selectionMoveOffsetX;
        var sourceY = (int)destination.Y - _selectionMoveOffsetY;
        if (sourceX is < 0 or > ushort.MaxValue || sourceY is < 0 or > ushort.MaxValue)
            return false;

        sourceCoordinate = new OtbmTileCoord((ushort)sourceX, (ushort)sourceY, destination.Z);
        return source.Contains(sourceCoordinate);
    }

    [RelayCommand]
    private void ClearSelection()
    {
        ResetSelectionMoveState();
        _selectionAnchor = null;
        _selectionBase.Clear();
        _selectedCoordinates.Clear();
        UpdateSelectionState();
        RefreshViewport();
        Status = "Wyczyszczono zaznaczenie.";
    }

    [RelayCommand]
    private void SelectAll()
    {
        if (_map is null) return;
        ResetSelectionMoveState();
        _selectedCoordinates.Clear();
        var floors = SelectionFloors().ToHashSet();
        _selectedCoordinates.UnionWith(_map.Tiles.Keys.Where(coord => floors.Contains(coord.Z)));
        UpdateSelectionState();
        RefreshViewport();
        Status = $"Zaznaczono wszystkie pola z zawartością na Z={CurrentFloor}.";
    }

    private IEnumerable<byte> SelectionFloors()
    {
        if (SelectionFloorMode.Equals("Lower Floors", StringComparison.Ordinal))
        {
            for (var floor = (int)CurrentFloor; floor <= 15; floor++) yield return (byte)floor;
            yield break;
        }
        if (SelectionFloorMode.Equals("Visible Floors", StringComparison.Ordinal) && ShowAllFloors)
        {
            var lastFloor = CurrentFloor <= 7 ? 7 : Math.Min(15, CurrentFloor + 2);
            for (var floor = (int)CurrentFloor; floor <= lastFloor; floor++) yield return (byte)floor;
            yield break;
        }
        yield return CurrentFloor;
    }

    private void MoveSelectionCore(
        IReadOnlyCollection<OtbmTileCoord> sourceCoordinates,
        int offsetX,
        int offsetY)
    {
        if (_map is null || sourceCoordinates.Count == 0) return;

        var sourceSet = sourceCoordinates.ToHashSet();
        var sourceTiles = sourceCoordinates
            .Where(_map.Tiles.ContainsKey)
            .ToDictionary(
                coordinate => coordinate,
                coordinate => MapEditHistory.CloneTile(_map.Tiles[coordinate])!);
        if (sourceTiles.Count == 0)
        {
            _selectedCoordinates.Clear();
            UpdateSelectionState();
            RefreshViewport(updateStatus: false);
            Status = "Zaznaczone pola nie zawierają już tile do przeniesienia.";
            return;
        }

        var destinations = sourceTiles.Keys.ToDictionary(
            coordinate => coordinate,
            coordinate => OffsetCoordinate(coordinate, offsetX, offsetY));
        var affectedCoordinates = sourceTiles.Keys
            .Concat(destinations.Values)
            .Distinct()
            .ToArray();
        var beforeTiles = affectedCoordinates.ToDictionary(
            coordinate => coordinate,
            coordinate => MapEditHistory.CloneTile(_map.Tiles.GetValueOrDefault(coordinate)));
        var spawnsBefore = MapEditHistory.CloneSpawns(_map.Spawns);
        var entitiesBefore = MapEntitiesSnapshot.FromMap(_map);

        // Wszystkie źródła usuwamy przed wstawianiem. Jest to istotne dla
        // przesunięć zachodzących na siebie: A,B przesunięte o jedno pole dają
        // dokładnie A,B, bez przypadkowego duplikowania B na pozycji A.
        foreach (var coordinate in sourceTiles.Keys)
            _map.Tiles.Remove(coordinate);

        var mergedConflicts = 0;
        var replacedConflicts = 0;
        foreach (var (sourceCoordinate, sourceTile) in sourceTiles
                     .OrderBy(pair => pair.Key.Z)
                     .ThenBy(pair => pair.Key.Y)
                     .ThenBy(pair => pair.Key.X))
        {
            var destination = destinations[sourceCoordinate];
            var movedTile = MapEditHistory.CloneTile(sourceTile)!;
            movedTile.X = destination.X;
            movedTile.Y = destination.Y;
            movedTile.Z = destination.Z;

            if (_map.Tiles.TryGetValue(destination, out var existing))
            {
                // Domyślne zachowanie RME: tile z ground zastępuje cel, a tile
                // zawierający wyłącznie obiekty jest dokładany do istniejącego
                // stosu. W obu przypadkach Undo przechowuje pełny stan celu.
                if (movedTile.GroundItemId == 0)
                {
                    movedTile = MergeClipboardTile(existing, movedTile, destination);
                    mergedConflicts++;
                }
                else
                {
                    replacedConflicts++;
                }
            }

            _map.Tiles[destination] = movedTile;
        }

        foreach (var coordinate in affectedCoordinates)
        {
            var existedBefore = beforeTiles[coordinate] is not null;
            var existsAfter = _map.Tiles.ContainsKey(coordinate);
            if (existedBefore == existsAfter) continue;
            _floorTileCounts[coordinate.Z] = Math.Max(
                0,
                _floorTileCounts.GetValueOrDefault(coordinate.Z) + (existsAfter ? 1 : -1));
        }

        var spawnChanged = false;
        var movedSpawnCenters = new HashSet<OtbmSpawn>();
        foreach (var spawn in _map.Spawns)
        {
            var center = new OtbmTileCoord(spawn.CenterX, spawn.CenterY, spawn.CenterZ);
            if (sourceSet.Contains(center))
            {
                var moved = OffsetCoordinate(center, offsetX, offsetY);
                spawn.CenterX = moved.X;
                spawn.CenterY = moved.Y;
                movedSpawnCenters.Add(spawn);
                spawnChanged = true;
            }

            foreach (var creature in spawn.Creatures)
            {
                var position = new OtbmTileCoord(creature.X, creature.Y, creature.Z);
                if (!sourceSet.Contains(position)) continue;
                var moved = OffsetCoordinate(position, offsetX, offsetY);
                creature.X = moved.X;
                creature.Y = moved.Y;
                spawnChanged = true;
            }
        }
        if (movedSpawnCenters.Count > 0)
            MergeSpawnCenterConflicts(_map.Spawns, movedSpawnCenters);

        var entitiesChanged = false;
        foreach (var town in _map.Towns)
        {
            var position = new OtbmTileCoord(town.TempleX, town.TempleY, town.TempleZ);
            if (!sourceSet.Contains(position)) continue;
            var moved = OffsetCoordinate(position, offsetX, offsetY);
            town.TempleX = moved.X;
            town.TempleY = moved.Y;
            entitiesChanged = true;
        }
        foreach (var house in _map.Houses)
        {
            var position = new OtbmTileCoord(house.EntryX, house.EntryY, house.EntryZ);
            if (!sourceSet.Contains(position)) continue;
            var moved = OffsetCoordinate(position, offsetX, offsetY);
            house.EntryX = moved.X;
            house.EntryY = moved.Y;
            entitiesChanged = true;
        }
        foreach (var waypoint in _map.Waypoints)
        {
            var position = new OtbmTileCoord(waypoint.X, waypoint.Y, waypoint.Z);
            if (!sourceSet.Contains(position)) continue;
            var moved = OffsetCoordinate(position, offsetX, offsetY);
            waypoint.X = moved.X;
            waypoint.Y = moved.Y;
            entitiesChanged = true;
        }

        var tileChanges = affectedCoordinates
            .Select(coordinate => new MapTileChange(
                coordinate,
                beforeTiles[coordinate],
                _map.Tiles.GetValueOrDefault(coordinate)))
            .Where(change => !TilesEqual(change.Before, change.After))
            .ToArray();
        _history.PushComposite(
            "Przenieś zaznaczenie",
            tileChanges,
            spawnChanged ? spawnsBefore : null,
            spawnChanged ? _map.Spawns : null,
            entitiesChanged ? entitiesBefore : null,
            entitiesChanged ? MapEntitiesSnapshot.FromMap(_map) : null);

        if (spawnChanged || entitiesChanged)
        {
            _externalDataDirty = true;
            IndexExternalMapData(_map);
        }
        if (entitiesChanged)
        {
            RefreshEntityEditors();
            RebuildMapEntityPalettes();
        }

        _selectedCoordinates.Clear();
        _selectedCoordinates.UnionWith(destinations.Values);
        UpdateMinimapSourceOverrides(affectedCoordinates);
        UpdateSelectionState();
        UpdateHistoryState();
        RefreshMapSummary();
        RefreshViewport(updateStatus: false);
        RequestMinimapRefresh();

        var conflictStatus = (mergedConflicts, replacedConflicts) switch
        {
            (0, 0) => string.Empty,
            (> 0, 0) => $" Połączono {mergedConflicts} stosów obiektów z zawartością celu.",
            (0, > 0) => $" Zastąpiono {replacedConflicts} docelowych tile z ground (dostępne Undo).",
            _ => $" Połączono {mergedConflicts} stosów i zastąpiono {replacedConflicts} tile z ground (dostępne Undo)."
        };
        Status = $"Przeniesiono {sourceTiles.Count} tile o X {offsetX:+#;-#;0}, Y {offsetY:+#;-#;0}.{conflictStatus}";
    }

    private static OtbmTileCoord OffsetCoordinate(OtbmTileCoord coordinate, int offsetX, int offsetY) =>
        new(
            checked((ushort)(coordinate.X + offsetX)),
            checked((ushort)(coordinate.Y + offsetY)),
            coordinate.Z);

    private static void MergeSpawnCenterConflicts(
        IList<OtbmSpawn> spawns,
        IReadOnlySet<OtbmSpawn> movedCenters)
    {
        var groups = spawns
            .GroupBy(spawn => new OtbmTileCoord(spawn.CenterX, spawn.CenterY, spawn.CenterZ))
            .Where(group => group.Count() > 1 && group.Any(movedCenters.Contains))
            .ToArray();
        foreach (var group in groups)
        {
            // Preferujemy przenoszony spawn jako wynik, a stworzenia istniejącego
            // spawnu dołączamy. Pozwala to zachować promień narzędzia i nie gubi
            // żadnego NPC/potwora przy kolizji centrów.
            var target = group.First(movedCenters.Contains);
            foreach (var duplicate in group.Where(spawn => !ReferenceEquals(spawn, target)).ToArray())
            {
                target.Radius = Math.Max(target.Radius, duplicate.Radius);
                foreach (var creature in duplicate.Creatures)
                    target.Creatures.Add(creature);
                spawns.Remove(duplicate);
            }
        }
    }

    [RelayCommand]
    private void CopySelection()
    {
        if (_map is null || _selectedCoordinates.Count == 0)
        {
            Status = "Brak zaznaczonych pól do skopiowania.";
            return;
        }

        var originX = _selectedCoordinates.Min(coord => coord.X);
        var originY = _selectedCoordinates.Min(coord => coord.Y);
        var originZ = _selectedCoordinates.Min(coord => coord.Z);
        var tiles = new List<MapClipboardTile>();
        foreach (var coord in _selectedCoordinates.OrderBy(coord => coord.Z).ThenBy(coord => coord.Y).ThenBy(coord => coord.X))
        {
            if (!_map.Tiles.TryGetValue(coord, out var tile)) continue;
            tiles.Add(new MapClipboardTile(
                coord.X - originX,
                coord.Y - originY,
                coord.Z - originZ,
                MapEditHistory.CloneTile(tile)!));
        }

        var spawns = new List<MapClipboardSpawn>();
        foreach (var spawn in _map.Spawns)
        {
            var center = new OtbmTileCoord(spawn.CenterX, spawn.CenterY, spawn.CenterZ);
            var selectedCreatures = spawn.Creatures.Where(creature =>
                _selectedCoordinates.Contains(new OtbmTileCoord(creature.X, creature.Y, creature.Z))).ToArray();
            if (!_selectedCoordinates.Contains(center) && selectedCreatures.Length == 0) continue;
            spawns.Add(new MapClipboardSpawn(
                spawn.CenterX - originX,
                spawn.CenterY - originY,
                spawn.CenterZ - originZ,
                spawn.Radius,
                selectedCreatures.Select(creature => new MapClipboardCreature(
                    creature.Name,
                    creature.X - originX,
                    creature.Y - originY,
                    creature.Z - originZ,
                    creature.SpawnTime,
                    creature.Direction,
                    creature.IsNpc)).ToArray()));
        }

        _clipboard = new MapClipboard(tiles, spawns);
        OnPropertyChanged(nameof(HasClipboard));
        Status = $"Skopiowano {tiles.Count} tile i {spawns.Sum(spawn => spawn.Creatures.Count)} stworzeń.";
    }

    [RelayCommand]
    private void CutSelection()
    {
        CopySelection();
        if (_clipboard is null) return;
        DeleteSelectionCore("Wytnij zaznaczenie");
    }

    [RelayCommand]
    private void DeleteSelection() => DeleteSelectionCore("Usuń zaznaczenie");

    private void DeleteSelectionCore(string description)
    {
        if (_map is null || _selectedCoordinates.Count == 0)
        {
            Status = "Brak zaznaczonych pól do usunięcia.";
            return;
        }

        var tileChanges = new List<MapTileChange>();
        foreach (var coord in _selectedCoordinates)
        {
            if (!_map.Tiles.Remove(coord, out var before)) continue;
            tileChanges.Add(new MapTileChange(coord, before, null));
            _floorTileCounts[coord.Z] = Math.Max(0, _floorTileCounts.GetValueOrDefault(coord.Z) - 1);
        }

        var spawnsBefore = MapEditHistory.CloneSpawns(_map.Spawns);
        var spawnChanged = false;
        for (var index = _map.Spawns.Count - 1; index >= 0; index--)
        {
            var spawn = _map.Spawns[index];
            if (_selectedCoordinates.Contains(new OtbmTileCoord(spawn.CenterX, spawn.CenterY, spawn.CenterZ)))
            {
                _map.Spawns.RemoveAt(index);
                spawnChanged = true;
                continue;
            }
            spawnChanged |= spawn.Creatures.RemoveAll(creature =>
                _selectedCoordinates.Contains(new OtbmTileCoord(creature.X, creature.Y, creature.Z))) > 0;
        }

        if (tileChanges.Count == 0 && !spawnChanged)
        {
            Status = "Zaznaczone pola są puste.";
            return;
        }

        _history.PushComposite(
            description,
            tileChanges,
            spawnChanged ? spawnsBefore : null,
            spawnChanged ? _map.Spawns : null);
        UpdateMinimapSourceOverrides(tileChanges.Select(change => change.Coordinate));
        if (spawnChanged)
        {
            _externalDataDirty = true;
            IndexExternalMapData(_map);
        }
        _selectedCoordinates.Clear();
        UpdateSelectionState();
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = $"{description}: {tileChanges.Count} tile.";
    }

    [RelayCommand]
    private void PasteSelection()
    {
        var x = _hoveredMapX ?? (ushort)Math.Min(ushort.MaxValue, ViewX + _viewWidth / 2);
        var y = _hoveredMapY ?? (ushort)Math.Min(ushort.MaxValue, ViewY + _viewHeight / 2);
        PasteSelectionAt(x, y, CurrentFloor);
    }

    public void PasteSelectionAt(ushort x, ushort y, byte z)
    {
        if (_map is null || _clipboard is not { } clipboard)
        {
            Status = "Schowek mapy jest pusty.";
            return;
        }

        var changes = new Dictionary<OtbmTileCoord, OtbmTile?>();
        var pasted = new HashSet<OtbmTileCoord>();
        foreach (var entry in clipboard.Tiles)
        {
            var targetX = x + entry.OffsetX;
            var targetY = y + entry.OffsetY;
            var targetZ = z + entry.OffsetZ;
            if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue || targetZ is < 0 or > 15)
                continue;
            var coord = new OtbmTileCoord((ushort)targetX, (ushort)targetY, (byte)targetZ);
            var before = MapEditHistory.CloneTile(_map.Tiles.GetValueOrDefault(coord));
            changes[coord] = before;
            var source = MapEditHistory.CloneTile(entry.Tile)!;
            source.X = coord.X;
            source.Y = coord.Y;
            source.Z = coord.Z;
            if (source.GroundItemId == 0 && before is not null)
                source = MergeClipboardTile(before, source, coord);
            _map.Tiles[coord] = source;
            if (before is null)
                _floorTileCounts[coord.Z] = _floorTileCounts.GetValueOrDefault(coord.Z) + 1;
            pasted.Add(coord);
        }

        var spawnsBefore = MapEditHistory.CloneSpawns(_map.Spawns);
        var spawnChanged = false;
        foreach (var entry in clipboard.Spawns)
        {
            var centerX = x + entry.OffsetX;
            var centerY = y + entry.OffsetY;
            var centerZ = z + entry.OffsetZ;
            if (centerX is < 0 or > ushort.MaxValue || centerY is < 0 or > ushort.MaxValue || centerZ is < 0 or > 15)
                continue;
            var spawn = _map.Spawns.FirstOrDefault(candidate =>
                candidate.CenterX == centerX && candidate.CenterY == centerY && candidate.CenterZ == centerZ);
            if (spawn is null)
            {
                spawn = new OtbmSpawn
                {
                    CenterX = (ushort)centerX,
                    CenterY = (ushort)centerY,
                    CenterZ = (byte)centerZ,
                    Radius = entry.Radius
                };
                _map.Spawns.Add(spawn);
            }
            else
            {
                spawn.Radius = Math.Max(spawn.Radius, entry.Radius);
            }
            foreach (var creature in entry.Creatures)
            {
                var creatureX = x + creature.OffsetX;
                var creatureY = y + creature.OffsetY;
                var creatureZ = z + creature.OffsetZ;
                if (creatureX is < 0 or > ushort.MaxValue || creatureY is < 0 or > ushort.MaxValue || creatureZ is < 0 or > 15)
                    continue;
                spawn.Creatures.Add(new OtbmCreature
                {
                    Name = creature.Name,
                    X = (ushort)creatureX,
                    Y = (ushort)creatureY,
                    Z = (byte)creatureZ,
                    SpawnTime = creature.SpawnTime,
                    Direction = creature.Direction,
                    IsNpc = creature.IsNpc
                });
                pasted.Add(new OtbmTileCoord((ushort)creatureX, (ushort)creatureY, (byte)creatureZ));
            }
            pasted.Add(new OtbmTileCoord((ushort)centerX, (ushort)centerY, (byte)centerZ));
            spawnChanged = true;
        }

        var tileChanges = changes.Select(pair => new MapTileChange(
            pair.Key,
            pair.Value,
            _map.Tiles.GetValueOrDefault(pair.Key))).ToArray();
        if (tileChanges.Length == 0 && !spawnChanged)
        {
            Status = "Schowek nie mieści się w granicach mapy.";
            return;
        }
        _history.PushComposite(
            "Wklej zaznaczenie",
            tileChanges,
            spawnChanged ? spawnsBefore : null,
            spawnChanged ? _map.Spawns : null);
        UpdateMinimapSourceOverrides(tileChanges.Select(change => change.Coordinate));
        if (spawnChanged)
        {
            _externalDataDirty = true;
            IndexExternalMapData(_map);
        }
        _selectedCoordinates.Clear();
        _selectedCoordinates.UnionWith(pasted);
        UpdateSelectionState();
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = $"Wklejono {tileChanges.Length} tile i {clipboard.Spawns.Sum(spawn => spawn.Creatures.Count)} stworzeń.";
    }

    private static OtbmTile MergeClipboardTile(OtbmTile destination, OtbmTile source, OtbmTileCoord coordinate)
    {
        var merged = MapEditHistory.CloneTile(destination)!;
        merged.X = coordinate.X;
        merged.Y = coordinate.Y;
        merged.Z = coordinate.Z;
        merged.Flags |= source.Flags;
        if (source.IsHouseTile)
        {
            merged.IsHouseTile = true;
            merged.HouseId = source.HouseId;
        }
        foreach (var item in source.Items)
        {
            var holder = new OtbmTile();
            holder.Items.Add(item);
            merged.Items.Add(MapEditHistory.CloneTile(holder)!.Items[0]);
        }
        return merged;
    }

    private void UpdateSelectionState()
    {
        SelectionLabel = $"Zaznaczenie: {_selectedCoordinates.Count} pól";
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void AddTown()
    {
        if (_map is null) return;
        var before = MapEntitiesSnapshot.FromMap(_map);
        var id = _map.Towns.Count == 0 ? 1u : _map.Towns.Max(town => town.Id) + 1;
        var town = new OtbmTown
        {
            Id = id,
            Name = $"Town {id}",
            TempleX = _hoveredMapX ?? ViewX,
            TempleY = _hoveredMapY ?? ViewY,
            TempleZ = CurrentFloor
        };
        _map.Towns.Add(town);
        _history.PushEntities("Dodaj miasto", before, MapEntitiesSnapshot.FromMap(_map));
        RefreshEntityEditors(townId: town.Id);
        UpdateHistoryState();
        Status = $"Dodano miasto #{town.Id} {town.Name}.";
    }

    [RelayCommand]
    private void ApplyTown()
    {
        if (_map is null || SelectedTown is null) return;
        if (TownId <= 0 || string.IsNullOrWhiteSpace(TownName) || !ValidPosition(TownTempleX, TownTempleY, TownTempleZ))
        {
            Status = "Miasto wymaga dodatniego ID, nazwy i poprawnej pozycji świątyni.";
            return;
        }
        if (_map.Towns.Any(town => !ReferenceEquals(town, SelectedTown) && town.Id == (uint)TownId))
        {
            Status = $"Miasto o ID {TownId} już istnieje.";
            return;
        }
        var before = MapEntitiesSnapshot.FromMap(_map);
        var oldId = SelectedTown.Id;
        SelectedTown.Id = (uint)TownId;
        SelectedTown.Name = TownName.Trim();
        SelectedTown.TempleX = (ushort)TownTempleX;
        SelectedTown.TempleY = (ushort)TownTempleY;
        SelectedTown.TempleZ = (byte)TownTempleZ;
        if (oldId != SelectedTown.Id)
        {
            foreach (var house in _map.Houses.Where(house => house.TownId == oldId))
                house.TownId = SelectedTown.Id;
        }
        _history.PushEntities("Edytuj miasto", before, MapEntitiesSnapshot.FromMap(_map));
        RefreshEntityEditors(townId: SelectedTown.Id);
        UpdateHistoryState();
        Status = $"Zapisano miasto #{TownId}.";
    }

    [RelayCommand]
    private void DeleteTown()
    {
        if (_map is null || SelectedTown is null) return;
        var houses = _map.Houses.Count(house => house.TownId == SelectedTown.Id);
        if (houses > 0)
        {
            Status = $"Nie można usunąć miasta: korzysta z niego {houses} domów.";
            return;
        }
        var before = MapEntitiesSnapshot.FromMap(_map);
        var name = SelectedTown.Name;
        _map.Towns.Remove(SelectedTown);
        _history.PushEntities("Usuń miasto", before, MapEntitiesSnapshot.FromMap(_map));
        RefreshEntityEditors();
        UpdateHistoryState();
        Status = $"Usunięto miasto {name}.";
    }

    [RelayCommand]
    private void GoToTown() => CenterOn(TownTempleX, TownTempleY, TownTempleZ);

    [RelayCommand]
    private void AddHouse()
    {
        if (_map is null) return;
        var before = MapEntitiesSnapshot.FromMap(_map);
        var id = _map.Houses.Count == 0 ? 1u : _map.Houses.Max(house => house.Id) + 1;
        var house = new OtbmHouse
        {
            Id = id,
            Name = $"House {id}",
            EntryX = _hoveredMapX ?? ViewX,
            EntryY = _hoveredMapY ?? ViewY,
            EntryZ = CurrentFloor,
            TownId = _map.Towns.FirstOrDefault()?.Id ?? 0
        };
        _map.Houses.Add(house);
        _history.PushEntities("Dodaj dom", before, MapEntitiesSnapshot.FromMap(_map));
        _externalDataDirty = true;
        IndexExternalMapData(_map);
        RefreshEntityEditors(houseId: house.Id);
        RebuildMapEntityPalettes();
        UpdateHistoryState();
        RefreshViewport();
        Status = $"Dodano dom #{house.Id} {house.Name}.";
    }

    [RelayCommand]
    private void ApplyHouse()
    {
        if (_map is null || SelectedHouse is null) return;
        if (HouseId <= 0 || string.IsNullOrWhiteSpace(HouseName) || !ValidPosition(HouseEntryX, HouseEntryY, HouseEntryZ) ||
            HouseRent < 0 || HouseTownId < 0)
        {
            Status = "Dom wymaga dodatniego ID, nazwy, poprawnego wejścia oraz nieujemnego czynszu.";
            return;
        }
        if (_map.Houses.Any(house => !ReferenceEquals(house, SelectedHouse) && house.Id == (uint)HouseId))
        {
            Status = $"Dom o ID {HouseId} już istnieje.";
            return;
        }
        if (HouseTownId != 0 && _map.Towns.All(town => town.Id != (uint)HouseTownId))
        {
            Status = $"Miasto o ID {HouseTownId} nie istnieje.";
            return;
        }
        var before = MapEntitiesSnapshot.FromMap(_map);
        var oldId = SelectedHouse.Id;
        SelectedHouse.Id = (uint)HouseId;
        SelectedHouse.Name = HouseName.Trim();
        SelectedHouse.EntryX = (ushort)HouseEntryX;
        SelectedHouse.EntryY = (ushort)HouseEntryY;
        SelectedHouse.EntryZ = (byte)HouseEntryZ;
        SelectedHouse.Rent = (uint)HouseRent;
        SelectedHouse.TownId = (uint)HouseTownId;
        SelectedHouse.IsGuildhall = HouseIsGuildhall;
        var tileChanges = new List<MapTileChange>();
        if (oldId != SelectedHouse.Id)
        {
            foreach (var (coordinate, tile) in _map.Tiles.Where(pair => pair.Value.IsHouseTile && pair.Value.HouseId == oldId).ToArray())
            {
                var tileBefore = MapEditHistory.CloneTile(tile);
                tile.HouseId = SelectedHouse.Id;
                tileChanges.Add(new MapTileChange(coordinate, tileBefore, tile));
            }
        }
        _history.PushComposite(
            "Edytuj dom", tileChanges, null, null, before, MapEntitiesSnapshot.FromMap(_map));
        _externalDataDirty = true;
        IndexExternalMapData(_map);
        RefreshEntityEditors(houseId: SelectedHouse.Id);
        RebuildMapEntityPalettes();
        UpdateHistoryState();
        RefreshViewport();
        Status = $"Zapisano dom #{HouseId}.";
    }

    [RelayCommand]
    private void DeleteHouse()
    {
        if (_map is null || SelectedHouse is null) return;
        var before = MapEntitiesSnapshot.FromMap(_map);
        var id = SelectedHouse.Id;
        var tileChanges = new List<MapTileChange>();
        foreach (var (coordinate, tile) in _map.Tiles.Where(pair => pair.Value.IsHouseTile && pair.Value.HouseId == id).ToArray())
        {
            var tileBefore = MapEditHistory.CloneTile(tile);
            tile.IsHouseTile = false;
            tile.HouseId = 0;
            tileChanges.Add(new MapTileChange(coordinate, tileBefore, tile));
        }
        _map.Houses.Remove(SelectedHouse);
        _history.PushComposite(
            "Usuń dom", tileChanges, null, null, before, MapEntitiesSnapshot.FromMap(_map));
        _externalDataDirty = true;
        IndexExternalMapData(_map);
        RefreshEntityEditors();
        RebuildMapEntityPalettes();
        UpdateHistoryState();
        RefreshViewport();
        Status = $"Usunięto dom #{id} i odłączono {tileChanges.Count} pól.";
    }

    [RelayCommand]
    private void GoToHouse() => CenterOn(HouseEntryX, HouseEntryY, HouseEntryZ);

    [RelayCommand]
    private void AddWaypoint()
    {
        if (_map is null) return;
        var before = MapEntitiesSnapshot.FromMap(_map);
        var baseName = "Waypoint";
        var suffix = 1;
        var name = baseName;
        while (_map.Waypoints.Any(waypoint => waypoint.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = baseName + " " + suffix++;
        var waypoint = new OtbmWaypoint
        {
            Name = name,
            X = _hoveredMapX ?? ViewX,
            Y = _hoveredMapY ?? ViewY,
            Z = CurrentFloor
        };
        _map.Waypoints.Add(waypoint);
        _history.PushEntities("Dodaj waypoint", before, MapEntitiesSnapshot.FromMap(_map));
        IndexExternalMapData(_map);
        RefreshEntityEditors(waypointName: waypoint.Name);
        RebuildMapEntityPalettes();
        UpdateHistoryState();
        RefreshViewport();
        Status = $"Dodano waypoint {waypoint.Name}.";
    }

    [RelayCommand]
    private void ApplyWaypoint()
    {
        if (_map is null || SelectedWaypoint is null) return;
        if (string.IsNullOrWhiteSpace(WaypointName) || !ValidPosition(WaypointX, WaypointY, WaypointZ))
        {
            Status = "Waypoint wymaga unikalnej nazwy i poprawnej pozycji.";
            return;
        }
        if (_map.Waypoints.Any(waypoint => !ReferenceEquals(waypoint, SelectedWaypoint) &&
            waypoint.Name.Equals(WaypointName.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            Status = $"Waypoint o nazwie {WaypointName.Trim()} już istnieje.";
            return;
        }
        var before = MapEntitiesSnapshot.FromMap(_map);
        SelectedWaypoint.Name = WaypointName.Trim();
        SelectedWaypoint.X = (ushort)WaypointX;
        SelectedWaypoint.Y = (ushort)WaypointY;
        SelectedWaypoint.Z = (byte)WaypointZ;
        _history.PushEntities("Edytuj waypoint", before, MapEntitiesSnapshot.FromMap(_map));
        IndexExternalMapData(_map);
        RefreshEntityEditors(waypointName: SelectedWaypoint.Name);
        RebuildMapEntityPalettes();
        UpdateHistoryState();
        RefreshViewport();
        Status = $"Zapisano waypoint {WaypointName}.";
    }

    [RelayCommand]
    private void DeleteWaypoint()
    {
        if (_map is null || SelectedWaypoint is null) return;
        var before = MapEntitiesSnapshot.FromMap(_map);
        var name = SelectedWaypoint.Name;
        _map.Waypoints.Remove(SelectedWaypoint);
        _history.PushEntities("Usuń waypoint", before, MapEntitiesSnapshot.FromMap(_map));
        IndexExternalMapData(_map);
        RefreshEntityEditors();
        RebuildMapEntityPalettes();
        UpdateHistoryState();
        RefreshViewport();
        Status = $"Usunięto waypoint {name}.";
    }

    [RelayCommand]
    private void GoToWaypoint() => CenterOn(WaypointX, WaypointY, WaypointZ);

    private void CenterOn(int x, int y, int z)
    {
        if (!ValidPosition(x, y, z)) return;
        NavigateToViewport(
            (ushort)Math.Max(0, x - _viewWidth / 2),
            (ushort)Math.Max(0, y - _viewHeight / 2),
            (byte)z,
            rememberCurrent: true);
    }

    [RelayCommand]
    private void GoToPosition()
    {
        if (!ValidPosition(GoToX, GoToY, GoToZ))
        {
            Status = "Pozycja musi mieścić się w zakresie X/Y 0–65535 i Z 0–15.";
            return;
        }
        CenterOn(GoToX, GoToY, GoToZ);
        SetHoveredTile(GoToX, GoToY);
        Status = $"Przejście do ({GoToX}, {GoToY}, {GoToZ}).";
    }

    [RelayCommand]
    private void JumpToItem()
    {
        if (!_assets.IsLoaded)
        {
            Status = "Najpierw wczytaj assets klienta.";
            return;
        }
        if (JumpToItemId is <= 0 or > ushort.MaxValue ||
            !_assets.ObjectIds.Contains((uint)JumpToItemId))
        {
            Status = $"Server ID #{JumpToItemId} nie istnieje w aktywnym items.otb.";
            return;
        }

        var id = (uint)JumpToItemId;
        PaletteSearch = id.ToString(CultureInfo.InvariantCulture);
        SelectedBrush = new MapPaletteItem
        {
            ItemId = id,
            Name = _assets.GetObjectName(id) ?? $"RAW #{id}",
            Category = RmePaletteCategory.Raw,
            ThumbnailLoader = () => _assets.GetThumbnail(id)
        };
        Status = $"Jump to Item: wybrano Server ID #{id} w palecie RAW.";
    }

    [RelayCommand]
    private void JumpToBrush()
    {
        var query = JumpToBrushQuery.Trim();
        if (query.Length == 0)
        {
            Status = "Wpisz nazwę brushu.";
            return;
        }
        PaletteSearch = query;
        var match = EnumeratePaletteItems().FirstOrDefault(item =>
            item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        if (match is null)
        {
            Status = $"Nie znaleziono brushu „{query}” w aktywnym tilesecie.";
            return;
        }
        SelectedBrush = match;
        Status = $"Jump to Brush: wybrano {match.Name}.";
    }

    private IEnumerable<MapPaletteItem> EnumeratePaletteItems() =>
        TerrainPalette.Concat(DoodadPalette).Concat(ItemPalette).Concat(RawPalette)
            .Concat(CollectionPalette).Concat(CreaturePalette).Concat(HousePalette).Concat(WaypointPalette);

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private void NavigateBack()
    {
        if (!_previousPositions.TryPop(out var position)) return;
        _nextPositions.Push(CurrentViewPosition());
        NavigateToViewport(position.X, position.Y, position.Z, rememberCurrent: false);
        UpdateNavigationState();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateForward))]
    private void NavigateForward()
    {
        if (!_nextPositions.TryPop(out var position)) return;
        _previousPositions.Push(CurrentViewPosition());
        NavigateToViewport(position.X, position.Y, position.Z, rememberCurrent: false);
        UpdateNavigationState();
    }

    private void NavigateToViewport(ushort x, ushort y, byte z, bool rememberCurrent)
    {
        var target = new MapViewPosition(x, y, z);
        var current = CurrentViewPosition();
        if (target == current) return;
        if (rememberCurrent)
        {
            if (_previousPositions.Count == 0 || _previousPositions.Peek() != current)
                _previousPositions.Push(current);
            _nextPositions.Clear();
        }
        _suppressViewportRefresh = true;
        try
        {
            CurrentFloor = z;
            ViewX = x;
            ViewY = y;
        }
        finally
        {
            _suppressViewportRefresh = false;
        }
        RefreshViewport();
        RequestMinimapRefresh();
        UpdateNavigationState();
    }

    private MapViewPosition CurrentViewPosition() => new(ViewX, ViewY, CurrentFloor);

    private void UpdateNavigationState()
    {
        OnPropertyChanged(nameof(CanNavigateBack));
        OnPropertyChanged(nameof(CanNavigateForward));
        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
    }

    private static bool ValidPosition(int x, int y, int z) =>
        x is >= 0 and <= ushort.MaxValue && y is >= 0 and <= ushort.MaxValue && z is >= 0 and <= 15;

    private void RefreshEntityEditors(uint? townId = null, uint? houseId = null, string? waypointName = null)
    {
        TownEntries.Clear();
        HouseEntries.Clear();
        WaypointEntries.Clear();
        if (_map is null) return;
        foreach (var town in _map.Towns.OrderBy(town => town.Id)) TownEntries.Add(town);
        foreach (var house in _map.Houses.OrderBy(house => house.Id)) HouseEntries.Add(house);
        foreach (var waypoint in _map.Waypoints.OrderBy(waypoint => waypoint.Name, StringComparer.CurrentCultureIgnoreCase))
            WaypointEntries.Add(waypoint);
        SelectedTown = TownEntries.FirstOrDefault(town => town.Id == townId) ?? TownEntries.FirstOrDefault();
        SelectedHouse = HouseEntries.FirstOrDefault(house => house.Id == houseId) ?? HouseEntries.FirstOrDefault();
        SelectedWaypoint = WaypointEntries.FirstOrDefault(waypoint =>
                               waypoint.Name.Equals(waypointName, StringComparison.OrdinalIgnoreCase))
                           ?? WaypointEntries.FirstOrDefault();
        RebuildPaletteNavigationGroups();
    }

    public async Task ImportMapAsync(string path)
    {
        if (_map is null)
        {
            Status = "Najpierw otwórz mapę docelową albo utwórz nową.";
            return;
        }
        try
        {
            Status = $"Wczytywanie mapy do importu: {Path.GetFileName(path)}...";
            var imported = await Task.Run(() =>
            {
                var loaded = new OtbmReader().Read(path);
                var warnings = OtbmExternalDataReader.Load(loaded, path);
                return (Map: loaded, Warnings: warnings);
            }).ConfigureAwait(true);
            ImportMapCore(imported.Map, ImportOffsetX, ImportOffsetY, ImportHouseMode, ImportSpawns);
            if (imported.Warnings.Count > 0)
                Status += " Ostrzeżenia XML: " + string.Join(" ", imported.Warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Status = $"Błąd importu mapy: {ex.Message}";
        }
    }

    internal void ImportMapCore(OtbmMap imported, int offsetX, int offsetY, string houseMode, bool importSpawns)
    {
        if (_map is null) return;
        var propertiesBefore = MapPropertiesSnapshot.FromMap(_map);
        var entitiesBefore = MapEntitiesSnapshot.FromMap(_map);
        var spawnsBefore = MapEditHistory.CloneSpawns(_map.Spawns);
        var changes = new List<MapTileChange>();
        var townMap = new Dictionary<uint, uint>();
        var houseMap = new Dictionary<uint, uint>();
        var importHouses = !houseMode.Equals("Don't Import", StringComparison.OrdinalIgnoreCase);

        if (importHouses)
        {
            foreach (var source in imported.Towns.OrderBy(town => town.Id))
            {
                var existing = _map.Towns.FirstOrDefault(town => town.Id == source.Id);
                uint targetId;
                var add = true;
                if (houseMode.Equals("Insert", StringComparison.OrdinalIgnoreCase))
                    targetId = NextEntityId(_map.Towns.Select(town => town.Id));
                else if (existing is null)
                    targetId = source.Id;
                else if (houseMode.Equals("Merge", StringComparison.OrdinalIgnoreCase) ||
                         existing.Name.Equals(source.Name, StringComparison.Ordinal))
                {
                    targetId = existing.Id;
                    add = false;
                }
                else
                    targetId = NextEntityId(_map.Towns.Select(town => town.Id));
                townMap[source.Id] = targetId;
                if (!add) continue;
                var temple = TranslatePosition(source.TempleX, source.TempleY, source.TempleZ, offsetX, offsetY);
                _map.Towns.Add(new OtbmTown
                {
                    Id = targetId,
                    Name = source.Name,
                    TempleX = temple.X,
                    TempleY = temple.Y,
                    TempleZ = temple.Z
                });
            }

            foreach (var source in imported.Houses.OrderBy(house => house.Id))
            {
                var mappedTown = townMap.GetValueOrDefault(source.TownId, source.TownId);
                var existing = _map.Houses.FirstOrDefault(house => house.Id == source.Id);
                uint targetId;
                var add = true;
                if (houseMode.Equals("Insert", StringComparison.OrdinalIgnoreCase))
                    targetId = NextEntityId(_map.Houses.Select(house => house.Id));
                else if (existing is null)
                    targetId = source.Id;
                else if (houseMode.Equals("Merge", StringComparison.OrdinalIgnoreCase) ||
                         existing.Name.Equals(source.Name, StringComparison.Ordinal) && existing.TownId == mappedTown)
                {
                    targetId = existing.Id;
                    add = false;
                }
                else
                    targetId = NextEntityId(_map.Houses.Select(house => house.Id));
                houseMap[source.Id] = targetId;
                var entry = TranslatePosition(source.EntryX, source.EntryY, source.EntryZ, offsetX, offsetY);
                if (!add)
                {
                    existing!.EntryX = entry.X;
                    existing.EntryY = entry.Y;
                    existing.EntryZ = entry.Z;
                    continue;
                }
                _map.Houses.Add(new OtbmHouse
                {
                    Id = targetId,
                    Name = source.Name,
                    EntryX = entry.X,
                    EntryY = entry.Y,
                    EntryZ = entry.Z,
                    Rent = source.Rent,
                    TownId = mappedTown,
                    Size = source.Size,
                    IsGuildhall = source.IsGuildhall
                });
            }
        }

        foreach (var source in imported.Waypoints)
        {
            var position = TranslatePosition(source.X, source.Y, source.Z, offsetX, offsetY);
            var name = UniqueWaypointName(source.Name, _map.Waypoints.Select(waypoint => waypoint.Name));
            _map.Waypoints.Add(new OtbmWaypoint { Name = name, X = position.X, Y = position.Y, Z = position.Z });
        }

        var discarded = 0;
        foreach (var (sourceCoordinate, sourceTile) in imported.Tiles)
        {
            var targetX = sourceCoordinate.X + offsetX;
            var targetY = sourceCoordinate.Y + offsetY;
            if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue)
            {
                discarded++;
                continue;
            }
            var target = new OtbmTileCoord((ushort)targetX, (ushort)targetY, sourceCoordinate.Z);
            var before = MapEditHistory.CloneTile(_map.Tiles.GetValueOrDefault(target));
            var tile = MapEditHistory.CloneTile(sourceTile)!;
            tile.X = target.X;
            tile.Y = target.Y;
            if (tile.IsHouseTile)
            {
                if (importHouses && houseMap.TryGetValue(tile.HouseId, out var mappedHouse))
                    tile.HouseId = mappedHouse;
                else
                {
                    tile.IsHouseTile = false;
                    tile.HouseId = 0;
                }
            }
            foreach (var item in tile.Items.SelectMany(EnumerateItemTree))
            {
                if (item.TeleportX is not { } teleportX || item.TeleportY is not { } teleportY) continue;
                var translatedX = teleportX + offsetX;
                var translatedY = teleportY + offsetY;
                if (translatedX is >= 0 and <= ushort.MaxValue && translatedY is >= 0 and <= ushort.MaxValue)
                {
                    item.TeleportX = (ushort)translatedX;
                    item.TeleportY = (ushort)translatedY;
                }
            }
            _map.Tiles[target] = tile;
            if (before is null)
                _floorTileCounts[target.Z] = _floorTileCounts.GetValueOrDefault(target.Z) + 1;
            changes.Add(new MapTileChange(target, before, tile));
            _map.Width = Math.Max(_map.Width, target.X);
            _map.Height = Math.Max(_map.Height, target.Y);
        }

        if (importSpawns)
        {
            foreach (var source in imported.Spawns)
            {
                var centerX = source.CenterX + offsetX;
                var centerY = source.CenterY + offsetY;
                if (centerX is < 0 or > ushort.MaxValue || centerY is < 0 or > ushort.MaxValue) continue;
                _map.Spawns.RemoveAll(spawn =>
                    spawn.CenterX == centerX && spawn.CenterY == centerY && spawn.CenterZ == source.CenterZ);
                var spawn = new OtbmSpawn
                {
                    CenterX = (ushort)centerX,
                    CenterY = (ushort)centerY,
                    CenterZ = source.CenterZ,
                    Radius = source.Radius
                };
                foreach (var creature in source.Creatures)
                {
                    var creatureX = creature.X + offsetX;
                    var creatureY = creature.Y + offsetY;
                    if (creatureX is < 0 or > ushort.MaxValue || creatureY is < 0 or > ushort.MaxValue) continue;
                    spawn.Creatures.Add(new OtbmCreature
                    {
                        Name = creature.Name,
                        X = (ushort)creatureX,
                        Y = (ushort)creatureY,
                        Z = creature.Z,
                        SpawnTime = creature.SpawnTime,
                        Direction = creature.Direction,
                        IsNpc = creature.IsNpc
                    });
                }
                _map.Spawns.Add(spawn);
            }
        }

        _history.PushComposite(
            "Import mapy",
            changes,
            importSpawns ? spawnsBefore : null,
            importSpawns ? _map.Spawns : null,
            entitiesBefore,
            MapEntitiesSnapshot.FromMap(_map),
            propertiesBefore,
            MapPropertiesSnapshot.FromMap(_map));
        _externalDataDirty = true;
        IndexExternalMapData(_map);
        RefreshEntityEditors();
        RebuildMapEntityPalettes();
        LoadMapPropertyFields(_map);
        RefreshMapSummary();
        UpdateMinimapSourceOverrides(changes.Select(change => change.Coordinate));
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = $"Zaimportowano {changes.Count} tile z {Path.GetFileName(MapPath)}; odrzucono {discarded}.";
    }

    private static uint NextEntityId(IEnumerable<uint> ids)
    {
        var used = ids.ToHashSet();
        for (uint id = 1; id < uint.MaxValue; id++)
            if (!used.Contains(id)) return id;
        throw new InvalidOperationException("Brak wolnego ID encji mapy.");
    }

    private static string UniqueWaypointName(string requested, IEnumerable<string> existingNames)
    {
        var used = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(requested)) return requested;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{requested} ({suffix})";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private static OtbmTileCoord TranslatePosition(ushort x, ushort y, byte z, int offsetX, int offsetY)
    {
        var translatedX = Math.Clamp(x + offsetX, 0, ushort.MaxValue);
        var translatedY = Math.Clamp(y + offsetY, 0, ushort.MaxValue);
        return new OtbmTileCoord((ushort)translatedX, (ushort)translatedY, z);
    }

    [RelayCommand]
    private void SearchMap()
    {
        SearchResults.Clear();
        if (_map is null) return;
        var query = MapSearchQuery.Trim();
        var queryRequired = SearchMode is "Item ID" or "Unique ID" or "Action ID" or "Position" or "Text";
        if (queryRequired && query.Length == 0)
        {
            Status = "Wpisz szukaną wartość.";
            return;
        }
        if (SearchMode.Equals("Position", StringComparison.Ordinal))
        {
            var values = query.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length != 3 || !ushort.TryParse(values[0], out var x) ||
                !ushort.TryParse(values[1], out var y) || !byte.TryParse(values[2], out var z) || z > 15)
            {
                Status = "Pozycję wpisz jako X,Y,Z, np. 1000,1000,7.";
                return;
            }
            AddPositionSearchResult(new OtbmTileCoord(x, y, z));
        }
        else
        {
            var numericMode = SearchMode is "Item ID" or "Unique ID" or "Action ID";
            if (numericMode && !ushort.TryParse(query, out _))
            {
                Status = "Wpisz liczbowe Item ID, Unique ID albo Action ID.";
                return;
            }
            ushort.TryParse(query, out var value);
            foreach (var (coordinate, tile) in _map.Tiles.OrderBy(pair => pair.Key.Z).ThenBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X))
            {
                if (SearchSelectionOnly && !_selectedCoordinates.Contains(coordinate)) continue;
                if (SearchMode.Equals("Item ID", StringComparison.Ordinal) && tile.GroundItemId == value)
                    SearchResults.Add(new MapSearchResult(coordinate, $"Ground #{value}"));
                var index = 0;
                foreach (var item in tile.Items.SelectMany(EnumerateItemTree))
                {
                    var match = SearchMode switch
                    {
                        "Item ID" => item.Id == value,
                        "Unique ID" => item.UniqueId == value,
                        "Action ID" => item.ActionId == value,
                        "Container" => _assets.IsContainer(item.Id),
                        "Writeable" => item.Text is not null || item.WrittenBy is not null || item.WrittenDate.HasValue,
                        "Text" => (item.Text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                                  (item.Description?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                                  (item.WrittenBy?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false),
                        "Everything" => item.UniqueId.HasValue || item.ActionId.HasValue ||
                                        item.Text is not null || item.WrittenBy is not null || _assets.IsContainer(item.Id),
                        _ => false
                    };
                    if (match)
                        SearchResults.Add(new MapSearchResult(coordinate,
                            numericMode
                                ? $"Item #{item.Id} · stos {index} · {SearchMode}={value}"
                                : $"Item #{item.Id} · stos {index} · {SearchMode}"));
                    index++;
                }
            }
        }
        SelectedSearchResult = SearchResults.FirstOrDefault();
        Status = $"Wyniki wyszukiwania: {SearchResults.Count}.";
    }

    private void AddPositionSearchResult(OtbmTileCoord coordinate)
    {
        if (_map is null) return;
        var labels = new List<string>();
        if (_map.Tiles.TryGetValue(coordinate, out var tile))
            labels.Add($"ground #{tile.GroundItemId}, items {tile.Items.Count}");
        if (_spawnCenters.Contains(coordinate)) labels.Add("spawn");
        if (_creaturesByTile.TryGetValue(coordinate, out var creatures)) labels.Add($"creatures {creatures.Count}");
        SearchResults.Add(new MapSearchResult(coordinate,
            labels.Count == 0 ? "Puste pole" : string.Join(", ", labels)));
    }

    [RelayCommand]
    private void GoToSearchResult()
    {
        if (SelectedSearchResult is not { } result) return;
        CenterOn(result.X, result.Y, result.Z);
        SetHoveredTile(result.X, result.Y);
    }

    [RelayCommand]
    private async Task RefreshMinimapAsync()
    {
        RequestMinimapRefresh(debounce: false);
        await _minimapBuildTask.ConfigureAwait(true);
    }

    /// <summary>
    /// Zleca przebudowę treści minimapy bez blokowania UI. Każde nowsze zlecenie
    /// anuluje starsze. Zwykłe przesuwanie i zoom aktualizują już tylko lekką
    /// ramkę viewportu i nie trafiają do tej ścieżki.
    /// </summary>
    private void RequestMinimapRefresh(bool debounce = true)
    {
        var previous = Interlocked.Exchange(ref _minimapCancellation, new CancellationTokenSource());
        previous?.Cancel();
        previous?.Dispose();

        var requestId = Interlocked.Increment(ref _minimapRequestId);
        var map = _map;
        var floor = CurrentFloor;
        if (_disposed || map is null)
        {
            SwapMinimapImage(null);
            IsMinimapViewportVisible = false;
            IsMinimapLoading = false;
            MinimapProgress = 0;
            MinimapStatus = "Brak mapy.";
            return;
        }

        IsMinimapLoading = true;
        MinimapProgress = 2;
        MinimapStatus = $"Budowanie minimapy Z={floor} w tle…";
        var baseSamples = _minimapSourceByFloor.GetValueOrDefault(
            floor,
            ImmutableArray<MapMinimapSourceSample>.Empty);
        var sourceOverrides = _minimapSourceOverridesByFloor.GetValueOrDefault(
            floor,
            ImmutableDictionary<OtbmTileCoord, MapMinimapSourceOverride>.Empty);
        var colors = _minimapColorLookup;
        var token = _minimapCancellation!.Token;
        _minimapBuildTask = BuildAndPublishMinimapAsync(
            map,
            requestId,
            floor,
            baseSamples,
            sourceOverrides,
            colors,
            debounce,
            token);
    }

    private async Task BuildAndPublishMinimapAsync(
        OtbmMap map,
        long requestId,
        byte floor,
        ImmutableArray<MapMinimapSourceSample> baseSamples,
        ImmutableDictionary<OtbmTileCoord, MapMinimapSourceOverride> sourceOverrides,
        IReadOnlyDictionary<uint, ushort> colors,
        bool debounce,
        CancellationToken cancellationToken)
    {
        try
        {
            if (debounce)
                await Task.Delay(90, cancellationToken).ConfigureAwait(true);

            MinimapProgress = 10;
            var samples = await Task.Run(
                    () => CreateMinimapSnapshot(
                        floor,
                        baseSamples,
                        sourceOverrides,
                        colors,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(true);
            if (!IsCurrentMinimapRequest(map, requestId, floor)) return;

            MinimapProgress = 45;
            var result = await _minimapService.BuildAsync(
                    new MapMinimapRequest(requestId, floor, samples),
                    cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentMinimapRequest(map, result.RequestId, result.Floor)) return;

            MinimapProgress = 92;
            if (!result.HasTiles)
            {
                SwapMinimapImage(null);
                _minimapMinX = 0;
                _minimapMinY = 0;
                _minimapScale = 1;
                _minimapPixelWidth = 0;
                _minimapPixelHeight = 0;
                UpdateMinimapViewportOverlay();
                MinimapProgress = 100;
                MinimapStatus = $"Brak kafelków na Z={floor}.";
                return;
            }

            Bitmap? bitmap = null;
            try
            {
                using var stream = new MemoryStream(result.PngBytes, writable: false);
                bitmap = new Bitmap(stream);
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("IPlatformRenderInterface", StringComparison.Ordinal))
            {
                // Testy i narzędzia CLI nie uruchamiają renderera Avalonia. Sam
                // raster PNG został zbudowany poprawnie w zadaniu roboczym.
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentMinimapRequest(map, result.RequestId, result.Floor))
            {
                bitmap?.Dispose();
                return;
            }

            SwapMinimapImage(bitmap);
            _minimapMinX = result.MinX;
            _minimapMinY = result.MinY;
            _minimapScale = result.Scale;
            _minimapPixelWidth = result.PixelWidth;
            _minimapPixelHeight = result.PixelHeight;
            UpdateMinimapViewportOverlay();
            MinimapProgress = 100;
            MinimapStatus =
                $"Z={floor} · {result.PixelWidth}×{result.PixelHeight} px · skala 1:{result.Scale} · {result.TileCount:N0} kafelków.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normalne zachowanie: nowsza mapa, piętro lub pozycja zastąpiły to zlecenie.
        }
        catch (Exception ex)
        {
            if (IsCurrentMinimapRequest(map, requestId, floor))
                MinimapStatus = $"Nie udało się odświeżyć minimapy: {ex.Message}";
        }
        finally
        {
            if (requestId == Volatile.Read(ref _minimapRequestId))
                IsMinimapLoading = false;
        }
    }

    private static ImmutableArray<MapMinimapTileSample> CreateMinimapSnapshot(
        byte floor,
        ImmutableArray<MapMinimapSourceSample> baseSamples,
        ImmutableDictionary<OtbmTileCoord, MapMinimapSourceOverride> sourceOverrides,
        IReadOnlyDictionary<uint, ushort> colors,
        CancellationToken cancellationToken)
    {
        var overrides = sourceOverrides.ToDictionary(pair => pair.Key, pair => pair.Value);
        var builder = ImmutableArray.CreateBuilder<MapMinimapTileSample>(
            checked(baseSamples.Length + overrides.Count));
        var visited = 0;
        foreach (var source in baseSamples)
        {
            if ((visited++ & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            var coordinate = new OtbmTileCoord(source.X, source.Y, floor);
            if (overrides.Remove(coordinate, out var replacement))
            {
                if (replacement.Exists)
                    builder.Add(ToMinimapTileSample(replacement.Source, colors));
                continue;
            }
            builder.Add(ToMinimapTileSample(source, colors));
        }

        foreach (var replacement in overrides.Values)
        {
            if ((visited++ & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (replacement.Exists)
                builder.Add(ToMinimapTileSample(replacement.Source, colors));
        }
        return builder.ToImmutable();
    }

    private static MapMinimapTileSample ToMinimapTileSample(
        MapMinimapSourceSample source,
        IReadOnlyDictionary<uint, ushort> colors)
    {
        ushort color = 0;
        if (source.PreferredItemId != 0)
            colors.TryGetValue(source.PreferredItemId, out color);
        if (color is not (> 0 and < 216))
            colors.TryGetValue(source.GroundItemId, out color);
        return new MapMinimapTileSample(source.X, source.Y, color);
    }

    private static ImmutableDictionary<byte, ImmutableArray<MapMinimapSourceSample>>
        CreateMinimapSourceIndex(
            OtbmMap map,
            IReadOnlyDictionary<uint, ushort> colors,
            CancellationToken cancellationToken)
    {
        var floors = new Dictionary<byte, ImmutableArray<MapMinimapSourceSample>.Builder>();
        var visited = 0;
        foreach (var (coordinate, tile) in map.Tiles)
        {
            if ((visited++ & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!floors.TryGetValue(coordinate.Z, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<MapMinimapSourceSample>();
                floors.Add(coordinate.Z, builder);
            }
            builder.Add(CreateMinimapSourceSample(coordinate, tile, colors));
        }

        return floors.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value.ToImmutable());
    }

    private static MapMinimapSourceSample CreateMinimapSourceSample(
        OtbmTileCoord coordinate,
        OtbmTile tile,
        IReadOnlyDictionary<uint, ushort> colors)
    {
        uint preferredItemId = 0;
        for (var index = tile.Items.Count - 1; index >= 0; index--)
        {
            var itemId = tile.Items[index].Id;
            if (colors.TryGetValue(itemId, out var color) && color is > 0 and < 216)
            {
                preferredItemId = itemId;
                break;
            }
        }
        // A map may be opened before assets. Keeping the top object ID lets a later
        // asset load color it without rescanning the mutable map.
        if (preferredItemId == 0 && tile.Items.Count > 0)
            preferredItemId = tile.Items[^1].Id;
        return new MapMinimapSourceSample(
            coordinate.X,
            coordinate.Y,
            tile.GroundItemId,
            preferredItemId);
    }

    private void UpdateMinimapSourceOverrides(IEnumerable<OtbmTileCoord> coordinates)
    {
        if (_map is null) return;
        var floors = _minimapSourceOverridesByFloor.ToBuilder();
        foreach (var group in coordinates.Distinct().GroupBy(coordinate => coordinate.Z))
        {
            var existingFloor = floors.TryGetValue(group.Key, out var existing)
                ? existing
                : ImmutableDictionary<OtbmTileCoord, MapMinimapSourceOverride>.Empty;
            var floor = existingFloor.ToBuilder();
            foreach (var coordinate in group)
            {
                floor[coordinate] = _map.Tiles.TryGetValue(coordinate, out var tile)
                    ? new MapMinimapSourceOverride(
                        true,
                        CreateMinimapSourceSample(coordinate, tile, _minimapColorLookup))
                    : new MapMinimapSourceOverride(false, default);
            }
            floors[group.Key] = floor.ToImmutable();
        }
        _minimapSourceOverridesByFloor = floors.ToImmutable();
    }

    private bool IsCurrentMinimapRequest(OtbmMap map, long requestId, byte floor) =>
        !_disposed &&
        requestId == Volatile.Read(ref _minimapRequestId) &&
        ReferenceEquals(map, _map) &&
        floor == CurrentFloor;

    private void SwapMinimapImage(Bitmap? replacement)
    {
        var previous = MinimapImage;
        MinimapImage = replacement;
        if (!ReferenceEquals(previous, replacement)) previous?.Dispose();
    }

    /// <summary>
    /// Przelicza wyłącznie położenie lekkiej ramki na gotowym obrazie minimapy.
    /// Operacja jest O(1), dlatego pan, strzałki i zoom nie kopiują już całego
    /// piętra ani nie uruchamiają kodera PNG.
    /// </summary>
    private void UpdateMinimapViewportOverlay()
    {
        if (_map is null || _minimapPixelWidth <= 0 || _minimapPixelHeight <= 0 ||
            _minimapScale <= 0)
        {
            IsMinimapViewportVisible = false;
            return;
        }

        var mapLeft = (long)_minimapMinX;
        var mapTop = (long)_minimapMinY;
        var mapRight = mapLeft + (long)_minimapPixelWidth * _minimapScale;
        var mapBottom = mapTop + (long)_minimapPixelHeight * _minimapScale;
        var viewportLeft = (long)ViewX;
        var viewportTop = (long)ViewY;
        var viewportRight = viewportLeft + Math.Max(1, _viewWidth);
        var viewportBottom = viewportTop + Math.Max(1, _viewHeight);

        var clippedLeft = Math.Max(mapLeft, viewportLeft);
        var clippedTop = Math.Max(mapTop, viewportTop);
        var clippedRight = Math.Min(mapRight, viewportRight);
        var clippedBottom = Math.Min(mapBottom, viewportBottom);
        if (clippedLeft >= clippedRight || clippedTop >= clippedBottom)
        {
            IsMinimapViewportVisible = false;
            return;
        }

        var displayScale = Math.Min(
            MinimapDisplaySize / _minimapPixelWidth,
            MinimapDisplaySize / _minimapPixelHeight);
        var renderedWidth = _minimapPixelWidth * displayScale;
        var renderedHeight = _minimapPixelHeight * displayScale;
        var offsetX = (MinimapDisplaySize - renderedWidth) / 2d;
        var offsetY = (MinimapDisplaySize - renderedHeight) / 2d;
        var left = offsetX + (clippedLeft - mapLeft) / _minimapScale * displayScale;
        var top = offsetY + (clippedTop - mapTop) / _minimapScale * displayScale;
        var requestedWidth = (clippedRight - clippedLeft) / _minimapScale * displayScale;
        var requestedHeight = (clippedBottom - clippedTop) / _minimapScale * displayScale;

        MinimapViewportLeft = Math.Clamp(left, offsetX, offsetX + renderedWidth);
        MinimapViewportTop = Math.Clamp(top, offsetY, offsetY + renderedHeight);
        MinimapViewportWidth = Math.Min(
            offsetX + renderedWidth - MinimapViewportLeft,
            Math.Max(1d, requestedWidth));
        MinimapViewportHeight = Math.Min(
            offsetY + renderedHeight - MinimapViewportTop,
            Math.Max(1d, requestedHeight));
        IsMinimapViewportVisible = MinimapViewportWidth > 0 && MinimapViewportHeight > 0;
    }

    public async Task ExportMinimapAsync(string path)
    {
        if (_map is null)
        {
            Status = "Brak mapy do eksportu minimapy.";
            return;
        }
        try
        {
            var exported = await Task.Run(() => ExportMinimapFiles(path)).ConfigureAwait(true);
            Status = $"Wyeksportowano minimapę: {exported} plik(i) BMP.";
            MinimapStatus = Status;
        }
        catch (Exception ex)
        {
            Status = $"Błąd eksportu minimapy: {ex.Message}";
            MinimapStatus = Status;
        }
    }

    public async Task ImportCreatureFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        try
        {
            var result = await Task.Run(() => _creatureLoader.ImportFromOtFiles(paths)).ConfigureAwait(true);
            MergeCreatureDefinitions(result);
            Status = $"Zaimportowano {result.Creatures.Count} definicji potworów/NPC." +
                     (result.Warnings.Count > 0 ? $" Ostrzeżenia: {result.Warnings.Count}." : string.Empty);
            MaterialStatus = Status + (result.Warnings.Count > 0
                ? " " + string.Join(" ", result.Warnings.Take(3))
                : string.Empty);
        }
        catch (Exception ex)
        {
            Status = $"Błąd importu potworów/NPC: {ex.Message}";
        }
    }

    private void MergeCreatureDefinitions(RmeCreatureImportResult result)
    {
        if (result.Creatures.Count == 0) return;
        var merged = new Dictionary<string, RmeCreatureDefinition>(
            _creatureDefinitions, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, creature) in result.Creatures)
        {
            merged[name] = creature;
            _importedCreatureNames.Add(name);
        }
        _creatureDefinitions = merged;
        BuildSelectedTilesetPalettes();
    }

    public async Task ExportTilesetsAsync(string path)
    {
        if (_materials is null)
        {
            Status = "Eksport tilesetów wymaga wczytanego materials.xml RME.";
            return;
        }
        try
        {
            await Task.Run(() => ExportTilesets(path, _materials)).ConfigureAwait(true);
            Status = $"Wyeksportowano tilesety do {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Status = $"Błąd eksportu tilesetów: {ex.Message}";
        }
    }

    private static void ExportTilesets(string path, RmeMaterialCatalog materials)
    {
        var root = new System.Xml.Linq.XElement("materials");
        var categories = new (RmePaletteCategory Category, string Name)[]
        {
            (RmePaletteCategory.Terrain, "terrain"),
            (RmePaletteCategory.Doodad, "doodad"),
            (RmePaletteCategory.Item, "items"),
            (RmePaletteCategory.Collection, "collection"),
            (RmePaletteCategory.Raw, "raw")
        };
        foreach (var source in materials.Tilesets)
        {
            var tileset = new System.Xml.Linq.XElement("tileset", new System.Xml.Linq.XAttribute("name", source.Name));
            var hasExportableNonRawCategory = categories
                .Where(candidate => candidate.Category != RmePaletteCategory.Raw)
                .Any(candidate => source.Categories.TryGetValue(candidate.Category, out var values) && values.Count > 0);
            foreach (var (category, name) in categories)
            {
                if (!source.Categories.TryGetValue(category, out var entries) || entries.Count == 0) continue;
                if (source.Name.Equals("Others", StringComparison.OrdinalIgnoreCase) &&
                    category == RmePaletteCategory.Raw &&
                    !hasExportableNonRawCategory)
                    continue;
                var palette = new System.Xml.Linq.XElement(name);
                foreach (var entry in entries)
                {
                    var brushName = entry.Brush?.Name ?? entry.BrushName;
                    if (!string.IsNullOrWhiteSpace(brushName))
                        palette.Add(new System.Xml.Linq.XElement("brush", new System.Xml.Linq.XAttribute("name", brushName)));
                    else if (entry.ItemId is { } itemId)
                        palette.Add(new System.Xml.Linq.XElement("item", new System.Xml.Linq.XAttribute("id", itemId)));
                }
                if (palette.HasElements) tileset.Add(palette);
            }
            if (tileset.HasElements) root.Add(tileset);
        }
        var document = new System.Xml.Linq.XDocument(
            new System.Xml.Linq.XDeclaration("1.0", "utf-8", null), root);
        var fullPath = Path.ChangeExtension(Path.GetFullPath(path), ".xml");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            document.Save(temporary);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private int ExportMinimapFiles(string path)
    {
        if (_map is null) return 0;
        var normalizedPath = Path.ChangeExtension(Path.GetFullPath(path), ".bmp");
        var selectionOnly = MinimapExportMode.Equals("Selected Area", StringComparison.Ordinal);
        byte[] floors = MinimapExportMode switch
        {
            "Ground Floor" => [7],
            "All Floors" => _map.UsedFloors.ToArray(),
            "Selected Area" => _selectedCoordinates.Select(position => position.Z).Distinct().OrderBy(z => z).ToArray(),
            _ => [CurrentFloor]
        };
        if (selectionOnly && _selectedCoordinates.Count == 0)
            throw new InvalidOperationException("Najpierw zaznacz obszar mapy.");
        if (floors.Length == 0)
            throw new InvalidOperationException("Wybrany zakres nie zawiera żadnych pięter.");

        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath)!);
        var multiple = floors.Length > 1 || MinimapExportMode.Equals("All Floors", StringComparison.Ordinal);
        var written = 0;
        foreach (var floor in floors)
        {
            var output = multiple
                ? Path.Combine(Path.GetDirectoryName(normalizedPath)!,
                    $"{Path.GetFileNameWithoutExtension(normalizedPath)}_{floor}.bmp")
                : normalizedPath;
            ExportMinimapFloor(output, floor, selectionOnly);
            written++;
        }
        return written;
    }

    private void ExportMinimapFloor(string path, byte floor, bool selectionOnly)
    {
        if (_map is null) return;
        var area = selectionOnly
            ? _selectedCoordinates.Where(position => position.Z == floor).ToArray()
            : _map.Tiles.Keys.Where(position => position.Z == floor).ToArray();
        if (area.Length == 0)
            throw new InvalidOperationException($"Brak pól do eksportu na Z={floor}.");
        var minX = area.Min(position => position.X);
        var minY = area.Min(position => position.Y);
        var maxX = area.Max(position => position.X);
        var maxY = area.Max(position => position.Y);
        var width = maxX - minX + 1;
        var height = maxY - minY + 1;
        const long maximumPixels = 64L * 1024 * 1024;
        if ((long)width * height > maximumPixels)
            throw new InvalidOperationException(
                $"Zakres Z={floor} ma {width}×{height} px i przekracza bezpieczny limit 64 mln pikseli. Wyeksportuj zaznaczony obszar.");

        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
            width, height, new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 0, 0, 255));
        HashSet<OtbmTileCoord>? selected = selectionOnly ? _selectedCoordinates : null;
        foreach (var (coordinate, tile) in _map.Tiles)
        {
            if (coordinate.Z != floor || selected is not null && !selected.Contains(coordinate)) continue;
            image[coordinate.X - minX, coordinate.Y - minY] = GetMinimapPixel(tile);
        }
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = File.Create(temporary))
                image.Save(stream, new SixLabors.ImageSharp.Formats.Bmp.BmpEncoder());
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private SixLabors.ImageSharp.PixelFormats.Rgba32 GetMinimapPixel(OtbmTile tile)
    {
        var colorIndex = tile.Items.AsEnumerable().Reverse()
            .Select(item => _assets.GetMinimapColor(item.Id))
            .FirstOrDefault(color => color is > 0 and < 216);
        if (colorIndex == 0) colorIndex = _assets.GetMinimapColor(tile.GroundItemId);
        return colorIndex is > 0 and < 216
            ? MinimapColor(colorIndex)
            : new SixLabors.ImageSharp.PixelFormats.Rgba32(51, 65, 85, 255);
    }

    private static SixLabors.ImageSharp.PixelFormats.Rgba32 MinimapColor(ushort color) => new(
        (byte)(color / 36 % 6 * 51),
        (byte)(color / 6 % 6 * 51),
        (byte)(color % 6 * 51),
        255);

    private static string MinimapHex(ushort color) =>
        $"#{color / 36 % 6 * 51:X2}{color / 6 % 6 * 51:X2}{color % 6 * 51:X2}";

    public void NavigateFromMinimap(double relativeX, double relativeY)
    {
        if (_map is null || _minimapPixelWidth <= 0 || _minimapPixelHeight <= 0) return;
        var pixelX = Math.Clamp((int)(relativeX * _minimapPixelWidth), 0, _minimapPixelWidth - 1);
        var pixelY = Math.Clamp((int)(relativeY * _minimapPixelHeight), 0, _minimapPixelHeight - 1);
        var mapX = _minimapMinX + pixelX * _minimapScale;
        var mapY = _minimapMinY + pixelY * _minimapScale;
        NavigateToViewport(
            (ushort)Math.Max(0, mapX - _viewWidth / 2),
            (ushort)Math.Max(0, mapY - _viewHeight / 2),
            CurrentFloor,
            rememberCurrent: true);
    }

    private void UpdateBrushPreview()
    {
        _previewCoordinates.Clear();
        _borderPreviewCoordinates.Clear();
        if (!ShowPreview || _map is null || SelectedBrush is null ||
            _hoveredMapX is not { } x || _hoveredMapY is not { } y)
        {
            RefreshBrushPreviewOverlay();
            return;
        }
        var geometry = GetBrushPreviewGeometry(SelectedBrush);
        foreach (var entry in geometry)
        {
            var targetX = x + entry.X;
            var targetY = y + entry.Y;
            var targetZ = CurrentFloor + entry.Z;
            if (targetX is < 0 or > ushort.MaxValue || targetY is < 0 or > ushort.MaxValue ||
                targetZ is < 0 or > 15) continue;
            var coordinate = new OtbmTileCoord((ushort)targetX, (ushort)targetY, (byte)targetZ);
            if (entry.IsBorder) _borderPreviewCoordinates.Add(coordinate);
            else _previewCoordinates.Add(coordinate);
        }
        RefreshBrushPreviewOverlay();
    }

    private IReadOnlyList<MapBrushPreviewOffset> GetBrushPreviewGeometry(MapPaletteItem brush)
    {
        var key = new MapBrushPreviewGeometryKey(brush, BrushVariation, BrushSize, BrushShape, Automagic);
        if (_previewGeometryKey == key) return _previewGeometry;

        var fallbackItemId = brush.ItemId is > 0 and <= ushort.MaxValue
            ? (ushort)brush.ItemId
            : (ushort)0;
        var placement = RmeBrushPlacementService.Create(
            brush.Brush,
            fallbackItemId,
            BrushVariation,
            BrushSize,
            BrushShape.Equals("Okrąg", StringComparison.Ordinal) ? RmeBrushShape.Circle : RmeBrushShape.Square,
            new Random(0));
        var painted = placement.Tiles
            .Select(entry => (entry.X, entry.Y, entry.Z))
            .ToHashSet();
        var border = new HashSet<(int X, int Y, int Z)>();
        var affectsNeighbours = Automagic &&
            (brush.Brush?.Type.Equals("ground", StringComparison.OrdinalIgnoreCase) == true ||
             RmeConnectedBrushService.IsConnected(brush.Brush));
        if (affectsNeighbours)
        {
            foreach (var position in painted)
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0) continue;
                var neighbour = (position.X + offsetX, position.Y + offsetY, position.Z);
                if (!painted.Contains(neighbour)) border.Add(neighbour);
            }
        }

        _previewGeometry = border.Select(position =>
                new MapBrushPreviewOffset(position.X, position.Y, position.Z, true))
            .Concat(painted.Select(position =>
                new MapBrushPreviewOffset(position.X, position.Y, position.Z, false)))
            .ToArray();
        _previewGeometryKey = key;
        return _previewGeometry;
    }

    private void RefreshBrushPreviewOverlay()
    {
        if (_previewCoordinates.Count == 0 && _borderPreviewCoordinates.Count == 0)
        {
            VisibleBrushPreview = Array.Empty<MapBrushPreviewTile>();
            return;
        }

        var result = new List<MapBrushPreviewTile>(
            _previewCoordinates.Count + _borderPreviewCoordinates.Count);
        Add(_borderPreviewCoordinates, isBorder: true);
        Add(_previewCoordinates, isBorder: false);
        VisibleBrushPreview = result;

        void Add(IEnumerable<OtbmTileCoord> coordinates, bool isBorder)
        {
            foreach (var coordinate in coordinates)
            {
                if (coordinate.Z != CurrentFloor || coordinate.X < ViewX || coordinate.Y < ViewY) continue;
                var dx = coordinate.X - ViewX;
                var dy = coordinate.Y - ViewY;
                if (dx >= _viewWidth || dy >= _viewHeight) continue;
                result.Add(new MapBrushPreviewTile(
                    dx * TileSize,
                    dy * TileSize,
                    TileSize,
                    isBorder));
            }
        }
    }

    public async Task AnalyzeConversionAsync(string targetFolder)
    {
        ConversionTargetFolder = targetFolder;
        CanApplyConversion = false;
        _conversionPlan = null;
        if (_map is null)
        {
            ConversionReport = "Najpierw otwórz mapę.";
            return;
        }
        if (_assets.ItemsOtbPath is not { } sourceOtb || !File.Exists(sourceOtb))
        {
            ConversionReport = "Bieżący klient nie ma źródłowego items.otb. Bez niego bezpieczne mapowanie jest niemożliwe.";
            return;
        }
        try
        {
            var targetOtb = await Task.Run(() => MapVersionConversionService.FindItemsOtb(targetFolder)).ConfigureAwait(true);
            if (targetOtb is null)
            {
                ConversionReport = "W folderze docelowym nie znaleziono items.otb.";
                return;
            }
            var plan = await Task.Run(() => _conversion.Analyze(_map, sourceOtb, targetOtb)).ConfigureAwait(true);
            _conversionPlan = plan;
            CanApplyConversion = plan.CanApply;
            ConversionReport = plan.CanApply
                ? $"Gotowe: OTB {plan.SourceMajorVersion}.{plan.SourceMinorVersion} → " +
                  $"{plan.TargetMajorVersion}.{plan.TargetMinorVersion}; użyte obiekty {plan.UsedObjectCount}, " +
                  $"zmieniane ID {plan.ChangedObjectCount}. Żaden obiekt nie zostanie zgubiony."
                : $"Konwersja zablokowana: {plan.Issues.Count} nierozwiązanych ID. " +
                  string.Join(" ", plan.Issues.Take(12).Select(issue => $"#{issue.ServerId}: {issue.Reason}")) +
                  (plan.Issues.Count > 12 ? $" …i {plan.Issues.Count - 12} kolejnych." : string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            ConversionReport = $"Błąd analizy konwersji: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyConversion()
    {
        if (_map is null || _conversionPlan is not { CanApply: true } plan) return;
        try
        {
            // Powtórna analiza chroni przed zastosowaniem nieaktualnego planu po edycji mapy.
            var verified = await Task.Run(() => _conversion.Analyze(_map, plan.SourceOtbPath, plan.TargetOtbPath))
                .ConfigureAwait(true);
            if (!verified.CanApply)
            {
                _conversionPlan = verified;
                CanApplyConversion = false;
                ConversionReport = $"Mapa zmieniła się od analizy. Konwersja zablokowana: {verified.Issues.Count} braków.";
                return;
            }
            var propertiesBefore = MapPropertiesSnapshot.FromMap(_map);
            var changes = _conversion.Apply(_map, verified);
            var propertiesAfter = MapPropertiesSnapshot.FromMap(_map);
            _history.PushComposite(
                "Konwersja wersji klienta",
                changes,
                null,
                null,
                null,
                null,
                propertiesBefore,
                propertiesAfter);
            var assetWarning = string.Empty;
            try
            {
                var report = _assets.LoadFromFolder(ConversionTargetFolder, verified.TargetMinorVersion);
                _minimapColorLookup = _assets.CreateMinimapColorLookup();
                AssetsPath = ConversionTargetFolder;
                AssetsFolderName = Path.GetFileName(ConversionTargetFolder.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                BuildPalette();
                if (report.Warnings.Count > 0) assetWarning = " " + string.Join(" ", report.Warnings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                assetWarning = $" Konwersja mapy jest gotowa, ale podgląd klienta docelowego nie został wczytany: {ex.Message}";
            }
            LoadMapPropertyFields(_map);
            RefreshMapSummary();
            UpdateMinimapSourceOverrides(changes.Select(change => change.Coordinate));
            UpdateHistoryState();
            RefreshViewport();
            RequestMinimapRefresh();
            _conversionPlan = null;
            CanApplyConversion = false;
            ConversionReport = $"Zastosowano konwersję do OTB {verified.TargetMajorVersion}.{verified.TargetMinorVersion}. " +
                               $"Zmieniono {changes.Count} tile; zapis utworzy kopię .bak istniejącej mapy." + assetWarning;
            Status = "Konwersja wersji klienta zakończona w pamięci. Zapisz mapę, aby utrwalić zmiany.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            ConversionReport = $"Nie zastosowano konwersji: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BorderizeSelection() => BorderizeCoordinates(_selectedCoordinates, "Borderize Selection");

    [RelayCommand]
    private void BorderizeMap() => BorderizeCoordinates(_map?.Tiles.Keys.ToArray() ?? [], "Borderize Map");

    private void BorderizeCoordinates(IEnumerable<OtbmTileCoord> source, string description)
    {
        if (_map is null || _groundBorders is null)
        {
            Status = "Borderize wymaga wczytanych materiałów RME dla tej wersji klienta.";
            return;
        }
        var requested = source.Distinct().ToArray();
        if (requested.Length == 0)
        {
            Status = "Brak pól do obramowania.";
            return;
        }
        var before = new Dictionary<OtbmTileCoord, OtbmTile?>();
        foreach (var coordinate in requested)
            CaptureNeighbourhood(before, coordinate.X, coordinate.Y, coordinate.Z);
        _groundBorders.Reborder(_map, before.Keys);
        var changes = before.Select(pair => new MapTileChange(
            pair.Key, pair.Value, _map.Tiles.GetValueOrDefault(pair.Key))).ToArray();
        _history.Push(description, changes);
        UpdateMinimapSourceOverrides(changes.Select(change => change.Coordinate));
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = $"{description}: przeliczono {requested.Length} pól.";
    }

    [RelayCommand]
    private void RandomizeSelection() => RandomizeCoordinates(_selectedCoordinates, "Randomize Selection");

    [RelayCommand]
    private void RandomizeMap() => RandomizeCoordinates(_map?.Tiles.Keys.ToArray() ?? [], "Randomize Map");

    private void RandomizeCoordinates(IEnumerable<OtbmTileCoord> source, string description)
    {
        if (_map is null || _materials is null)
        {
            Status = "Randomize wymaga wczytanych materiałów RME.";
            return;
        }
        var changes = new List<MapTileChange>();
        var affected = new HashSet<OtbmTileCoord>();
        foreach (var coordinate in source.Distinct())
        {
            if (!_map.Tiles.TryGetValue(coordinate, out var tile) ||
                !_materials.GroundBrushesByItemId.TryGetValue(tile.GroundItemId, out var brush)) continue;
            var placement = brush.CreatePlacement(0, _brushRandom);
            var randomized = placement.Tiles.FirstOrDefault(entry => entry.X == 0 && entry.Y == 0 && entry.Z == 0)
                ?.ItemIds.FirstOrDefault(id => _materials.GroundBrushesByItemId.ContainsKey(id)) ?? 0;
            if (randomized == 0 || randomized == tile.GroundItemId) continue;
            var before = MapEditHistory.CloneTile(tile);
            tile.GroundItemId = randomized;
            changes.Add(new MapTileChange(coordinate, before, tile));
            affected.Add(coordinate);
        }
        if (Automagic && affected.Count > 0 && _groundBorders is not null)
        {
            var neighbourBefore = new Dictionary<OtbmTileCoord, OtbmTile?>();
            foreach (var coordinate in affected)
                CaptureNeighbourhood(neighbourBefore, coordinate.X, coordinate.Y, coordinate.Z);
            _groundBorders.Reborder(_map, neighbourBefore.Keys);
            foreach (var (coordinate, before) in neighbourBefore.Where(pair => !affected.Contains(pair.Key)))
                changes.Add(new MapTileChange(coordinate, before, _map.Tiles.GetValueOrDefault(coordinate)));
        }
        if (changes.Count == 0)
        {
            Status = "Nie znaleziono groundów z wariantami do losowania.";
            return;
        }
        _history.Push(description, changes);
        UpdateMinimapSourceOverrides(changes.Select(change => change.Coordinate));
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = $"{description}: zmieniono {affected.Count} groundów.";
    }

    [RelayCommand]
    private void ReplaceItems() => TransformItems(remove: false);

    [RelayCommand]
    private void RemoveItems() => TransformItems(remove: true);

    private void TransformItems(bool remove)
    {
        if (_map is null || BulkItemId is <= 0 or > ushort.MaxValue ||
            !remove && BulkReplacementId is <= 0 or > ushort.MaxValue)
        {
            Status = "Podaj poprawny źródłowy i docelowy Server ID.";
            return;
        }
        var sourceId = (ushort)BulkItemId;
        var replacementId = remove ? (ushort)0 : (ushort)BulkReplacementId;
        var coordinates = BulkSelectionOnly ? _selectedCoordinates : _map.Tiles.Keys.ToHashSet();
        var changes = new List<MapTileChange>();
        var occurrences = 0;
        foreach (var coordinate in coordinates.ToArray())
        {
            if (!_map.Tiles.TryGetValue(coordinate, out var tile)) continue;
            var before = MapEditHistory.CloneTile(tile);
            var tileOccurrences = 0;
            if (tile.GroundItemId == sourceId)
            {
                tile.GroundItemId = replacementId;
                tileOccurrences++;
            }
            tileOccurrences += TransformItemList(tile.Items, sourceId, replacementId, remove);
            if (tileOccurrences == 0) continue;
            occurrences += tileOccurrences;
            changes.Add(new MapTileChange(coordinate, before, tile));
        }
        if (changes.Count == 0)
        {
            Status = $"Nie znaleziono Server ID #{sourceId}.";
            return;
        }
        _history.Push(remove ? "Remove Items by ID" : "Replace Items", changes);
        UpdateMinimapSourceOverrides(changes.Select(change => change.Coordinate));
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = remove
            ? $"Usunięto {occurrences} wystąpień #{sourceId}."
            : $"Zamieniono {occurrences} wystąpień #{sourceId} na #{replacementId}.";
    }

    private static int TransformItemList(List<OtbmItem> items, ushort sourceId, ushort replacementId, bool remove)
    {
        var count = 0;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            var item = items[index];
            count += TransformItemList(item.Contents, sourceId, replacementId, remove);
            if (item.Id != sourceId) continue;
            count++;
            if (remove) items.RemoveAt(index);
            else item.Id = replacementId;
        }
        return count;
    }

    [RelayCommand]
    private void ClearInvalidHouses()
    {
        if (_map is null) return;
        var valid = _map.Houses.Select(house => house.Id).ToHashSet();
        var changes = new List<MapTileChange>();
        foreach (var (coordinate, tile) in _map.Tiles.Where(pair => pair.Value.IsHouseTile && !valid.Contains(pair.Value.HouseId)))
        {
            var before = MapEditHistory.CloneTile(tile);
            tile.IsHouseTile = false;
            tile.HouseId = 0;
            changes.Add(new MapTileChange(coordinate, before, tile));
        }
        if (changes.Count == 0)
        {
            Status = "Nie znaleziono nieprawidłowych pól domów.";
            return;
        }
        _history.Push("Clear Invalid Houses", changes);
        UpdateMinimapSourceOverrides(changes.Select(change => change.Coordinate));
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = $"Wyczyszczono {changes.Count} nieprawidłowych pól domów.";
    }

    [RelayCommand]
    private void CleanupMap()
    {
        if (_map is null || !_assets.IsLoaded)
        {
            Status = "Cleanup wymaga wczytanego klienta i items.otb.";
            return;
        }
        var valid = _assets.ObjectIds.Where(id => id <= ushort.MaxValue).Select(id => (ushort)id).ToHashSet();
        var changes = new List<MapTileChange>();
        var removed = 0;
        foreach (var (coordinate, tile) in _map.Tiles)
        {
            var before = MapEditHistory.CloneTile(tile);
            var tileRemoved = 0;
            if (tile.GroundItemId != 0 && !valid.Contains(tile.GroundItemId))
            {
                tile.GroundItemId = 0;
                tileRemoved++;
            }
            tileRemoved += RemoveUnknownItems(tile.Items, valid);
            if (tileRemoved == 0) continue;
            removed += tileRemoved;
            changes.Add(new MapTileChange(coordinate, before, tile));
        }
        if (changes.Count == 0)
        {
            Status = "Mapa nie zawiera obiektów spoza items.otb.";
            return;
        }
        _history.Push("Map Cleanup", changes);
        UpdateMinimapSourceOverrides(changes.Select(change => change.Coordinate));
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = $"Cleanup usunął {removed} nieprawidłowych obiektów z {changes.Count} tile.";
    }

    private static int RemoveUnknownItems(List<OtbmItem> items, IReadOnlySet<ushort> valid)
    {
        var removed = 0;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            removed += RemoveUnknownItems(items[index].Contents, valid);
            if (valid.Contains(items[index].Id)) continue;
            items.RemoveAt(index);
            removed++;
        }
        return removed;
    }

    [RelayCommand]
    private void RemoveCorpses()
    {
        if (_map is null || _materials is null)
        {
            Status = "Remove Corpses wymaga zgodnych palet materials.xml RME.";
            return;
        }
        var corpseTileset = _materials.Tilesets.FirstOrDefault(tileset =>
            tileset.Name.Equals("Corpses", StringComparison.OrdinalIgnoreCase));
        if (corpseTileset is null)
        {
            Status = "Palety tej wersji klienta nie zawierają tilesetu Corpses.";
            return;
        }
        var corpseIds = new HashSet<ushort>();
        foreach (var entry in corpseTileset.Categories.Values.SelectMany(entries => entries))
        {
            if (entry.ItemId is { } itemId) corpseIds.Add(itemId);
            var brush = entry.Brush;
            if (brush is null && entry.BrushName is { } brushName)
                _materials.Brushes.TryGetValue(brushName, out brush);
            if (brush is not null) corpseIds.UnionWith(brush.AllItemIds);
        }
        if (corpseIds.Count == 0)
        {
            Status = "Tileset Corpses nie zawiera identyfikatorów itemów.";
            return;
        }

        var changes = new List<MapTileChange>();
        var removed = 0;
        foreach (var (coordinate, tile) in _map.Tiles)
        {
            var before = MapEditHistory.CloneTile(tile);
            var tileRemoved = RmeMapMaintenanceService.RemoveSimpleItems(tile.Items, corpseIds);
            if (tileRemoved == 0) continue;
            removed += tileRemoved;
            changes.Add(new MapTileChange(coordinate, before, tile));
        }
        if (changes.Count == 0)
        {
            Status = "Mapa nie zawiera prostych obiektów z tilesetu Corpses.";
            return;
        }
        _history.Push("Remove all Corpses", changes);
        UpdateMinimapSourceOverrides(changes.Select(change => change.Coordinate));
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = $"Usunięto {removed} zwłok z {changes.Count} pól.";
    }

    [RelayCommand]
    private void RemoveUnreachableTiles()
    {
        if (_map is null || !_assets.IsLoaded)
        {
            Status = "Remove Unreachable Tiles wymaga wczytanego klienta i items.otb.";
            return;
        }
        var coordinates = RmeMapMaintenanceService.FindUnreachableTiles(_map, IsTileBlocking);
        if (coordinates.Count == 0)
        {
            Status = "Nie znaleziono nieosiągalnych pól.";
            return;
        }
        var removedSet = coordinates.ToHashSet();
        var changes = coordinates.Select(coordinate =>
                new MapTileChange(coordinate, MapEditHistory.CloneTile(_map.Tiles[coordinate]), null))
            .ToArray();
        var spawnsBefore = MapEditHistory.CloneSpawns(_map.Spawns);
        foreach (var coordinate in coordinates)
        {
            _map.Tiles.Remove(coordinate);
            _floorTileCounts[coordinate.Z] = Math.Max(0, _floorTileCounts.GetValueOrDefault(coordinate.Z) - 1);
            _selectedCoordinates.Remove(coordinate);
        }
        for (var index = _map.Spawns.Count - 1; index >= 0; index--)
        {
            var spawn = _map.Spawns[index];
            if (removedSet.Contains(new(spawn.CenterX, spawn.CenterY, spawn.CenterZ)))
            {
                _map.Spawns.RemoveAt(index);
                continue;
            }
            spawn.Creatures.RemoveAll(creature => removedSet.Contains(new(creature.X, creature.Y, creature.Z)));
            if (spawn.Creatures.Count == 0) _map.Spawns.RemoveAt(index);
        }
        var spawnsAfter = MapEditHistory.CloneSpawns(_map.Spawns);
        _history.PushComposite("Remove all Unreachable Tiles", changes, spawnsBefore, spawnsAfter);
        UpdateMinimapSourceOverrides(changes.Select(change => change.Coordinate));
        _externalDataDirty = true;
        IndexExternalMapData(_map);
        UpdateSelectionState();
        UpdateHistoryState();
        RefreshMapSummary();
        RefreshViewport();
        RequestMinimapRefresh();
        Status = $"Usunięto {coordinates.Count} nieosiągalnych pól zgodnie z zakresem widoku RME.";
    }

    [RelayCommand]
    private void CalculateStatistics()
    {
        if (_map is null) return;
        var allItems = _map.Tiles.Values.SelectMany(tile => tile.Items).SelectMany(EnumerateItemTree).ToArray();
        MapStatistics = $"Tile: {_map.Tiles.Count:N0}\n" +
                        $"Ground: {_map.Tiles.Values.Count(tile => tile.GroundItemId != 0):N0}\n" +
                        $"Items: {allItems.Length:N0}\n" +
                        $"Unique ID: {allItems.Count(item => item.UniqueId.HasValue):N0}\n" +
                        $"Action ID: {allItems.Count(item => item.ActionId.HasValue):N0}\n" +
                        $"Towns: {_map.Towns.Count:N0}, Houses: {_map.Houses.Count:N0}\n" +
                        $"Spawns: {_map.Spawns.Count:N0}, Creatures: {_map.Spawns.Sum(spawn => spawn.Creatures.Count):N0}\n" +
                        $"Waypoints: {_map.Waypoints.Count:N0}";
        Status = "Obliczono statystyki mapy.";
    }

    [ObservableProperty]
    private IReadOnlyList<MapTileItem> _visibleTiles = Array.Empty<MapTileItem>();
    [ObservableProperty]
    private IReadOnlyList<MapBrushPreviewTile> _visibleBrushPreview = Array.Empty<MapBrushPreviewTile>();
    public ObservableCollection<RmeTilesetDefinition> Tilesets { get; } = new();
    public ObservableCollection<MapPaletteItem> TerrainPalette { get; } = new();
    public ObservableCollection<MapPaletteItem> DoodadPalette { get; } = new();
    public ObservableCollection<MapPaletteItem> ItemPalette { get; } = new();
    public ObservableCollection<MapPaletteItem> RawPalette { get; } = new();
    public ObservableCollection<MapPaletteItem> CollectionPalette { get; } = new();
    public ObservableCollection<MapPaletteItem> CreaturePalette { get; } = new();
    public ObservableCollection<MapPaletteItem> HousePalette { get; } = new();
    public ObservableCollection<MapPaletteItem> WaypointPalette { get; } = new();
    public ObservableCollection<MapTilePropertyItem> TilePropertyItems { get; } = new();
    public ObservableCollection<OtbmTown> TownEntries { get; } = new();
    public ObservableCollection<OtbmHouse> HouseEntries { get; } = new();
    public ObservableCollection<OtbmWaypoint> WaypointEntries { get; } = new();
    public ObservableCollection<MapSearchResult> SearchResults { get; } = new();

    public string TerrainPaletteHeader => $"Teren ({TerrainPalette.Count})";
    public string DoodadPaletteHeader => $"Dekoracje ({DoodadPalette.Count})";
    public string ItemPaletteHeader => $"Przedmioty ({ItemPalette.Count})";
    public string RawPaletteHeader => $"RAW ({RawPalette.Count})";
    public string CollectionPaletteHeader => $"Kolekcje ({CollectionPalette.Count})";
    public string CreaturePaletteHeader => $"Stworzenia ({CreaturePalette.Count})";
    public bool IsTerrainPaletteEmpty => TerrainPalette.Count == 0;
    public bool IsDoodadPaletteEmpty => DoodadPalette.Count == 0;
    public bool IsItemPaletteEmpty => ItemPalette.Count == 0;
    public bool IsRawPaletteEmpty => RawPalette.Count == 0;
    public bool IsCollectionPaletteEmpty => CollectionPalette.Count == 0;
    public bool IsCreaturePaletteEmpty => CreaturePalette.Count == 0;
    public string PaletteEmptyMessage => string.IsNullOrWhiteSpace(PaletteSearch)
        ? "Ten tileset nie ma elementów w tej palecie. Wybierz inny tileset albo rodzaj palety."
        : $"Brak wyników dla „{PaletteSearch}”. Wyczyść wyszukiwanie albo wybierz inny tileset.";

    private OtbmTileCoord? _propertyCoordinate;
    private ushort? _hoveredMapX;
    private ushort? _hoveredMapY;

    public int VisibleColumns => _viewWidth;
    public int VisibleRows => _viewHeight;
    private int _viewWidth = 50;
    private int _viewHeight = 35;
    [ObservableProperty] private ushort _viewX = 1000;
    [ObservableProperty] private ushort _viewY = 1000;

    public int CanvasWidth  => _viewWidth  * TileSize;
    public int CanvasHeight => _viewHeight * TileSize;

    partial void OnTileSizeChanged(int value)
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        if (!_suppressViewportRefresh)
        {
            RefreshViewport();
            UpdateMinimapViewportOverlay();
        }
    }

    internal void CancelActiveLoad() => _loadCancellation?.Cancel();

    [RelayCommand]
    private void CancelLoad() => CancelActiveLoad();

    public async Task LoadAllAsync(string? otbmPath, string? assetsFolder)
    {
        if (Interlocked.CompareExchange(ref _loadOperationActive, 1, 0) != 0)
        {
            Status = "Wczytywanie już trwa — poczekaj na zakończenie bieżącej operacji.";
            return;
        }

        var operation = new CancellationTokenSource();
        _loadCancellation = operation;
        IsLoading = true;
        LoadProgress = 0;
        LoadProgressLabel = "0%";
        LoadStage = "Przygotowywanie danych…";
        IsLoadProgressIndeterminate = false;
        try
        {
            await LoadAllCoreAsync(otbmPath, assetsFolder, operation).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            LoadStage = "Wczytywanie anulowane.";
            IsLoadProgressIndeterminate = false;
            Status = "Wczytywanie anulowane.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Map Editor load failed: {ex}");
            LoadStage = "Błąd wczytywania.";
            IsLoadProgressIndeterminate = false;
            Status = $"Nie udało się wczytać danych: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            if (ReferenceEquals(_loadCancellation, operation))
                _loadCancellation = null;
            operation.Dispose();
            Volatile.Write(ref _loadOperationActive, 0);
        }
    }

    private async Task LoadAllCoreAsync(
        string? otbmPath,
        string? assetsFolder,
        CancellationTokenSource operation)
    {
        var cancellationToken = operation.Token;
        Status = "Wczytywanie...";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        OtbmMap? loadedMap = null;
        MapLoadAnalysis? mapAnalysis = null;
        MapAssetService? pendingAssets = null;
        MapAssetService.LoadReport? assetReport = null;
        var pendingMinimapSource =
            ImmutableDictionary<byte, ImmutableArray<MapMinimapSourceSample>>.Empty;
        IReadOnlyList<string> externalWarnings = [];
        try
        {
            if (!string.IsNullOrWhiteSpace(otbmPath))
            {
                if (!File.Exists(otbmPath)) throw new FileNotFoundException("Nie znaleziono mapy OTBM.", otbmPath);
                ReportLoadProgress(operation, 2, "Odczytywanie mapy OTBM…");
                var parserProgress = new Progress<OtbmReadProgress>(progress =>
                    ReportLoadProgress(
                        operation,
                        2 + progress.Fraction * 50,
                        $"Odczytywanie mapy: {progress.TilesRead:N0} kafelków…"));
                var mapLoad = await Task.Run(() =>
                    {
                        var map = new OtbmReader().Read(otbmPath, parserProgress, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        var warnings = OtbmExternalDataReader.Load(map, otbmPath);
                        cancellationToken.ThrowIfCancellationRequested();
                        return (Map: map, Warnings: warnings);
                    }, cancellationToken)
                    .ConfigureAwait(true);
                loadedMap = mapLoad.Map;
                externalWarnings = mapLoad.Warnings;

                ReportLoadProgress(operation, 55, "Analizowanie pięter i położenia mapy…");
                mapAnalysis = await Task.Run(
                        () => AnalyzeLoadedMap(mapLoad.Map, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            if (!string.IsNullOrWhiteSpace(assetsFolder))
            {
                if (!Directory.Exists(assetsFolder))
                    throw new DirectoryNotFoundException("Nie znaleziono folderu klienta/assets: " + assetsFolder);
                ReportLoadProgress(operation, 60, "Wczytywanie assets klienta…", indeterminate: true);
                var expectedOtb = loadedMap?.ItemsMinorVersion ?? _map?.ItemsMinorVersion;
                pendingAssets = new MapAssetService();
                var candidate = pendingAssets;
                assetReport = await Task.Run(
                        () => candidate.LoadFromFolder(assetsFolder, expectedOtb, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                ReportLoadProgress(operation, 76, "Assets zostały odczytane. Budowanie indeksów…");
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Przy wczytaniu samych assets również przebudowujemy źródło minimapy.
            // Obszar roboczy jest na czas operacji wyłączony, więc worker może
            // bezpiecznie odczytać aktywną mapę, a po podmianie klienta kolory nie
            // pozostaną oparte na poprzedniej wersji assets.
            var minimapSourceMap = loadedMap ?? (pendingAssets is not null ? _map : null);
            if (minimapSourceMap is not null)
            {
                ReportLoadProgress(operation, 77, "Budowanie bezpiecznego indeksu minimapy…");
                var minimapColors = pendingAssets?.CreateMinimapColorLookup() ?? _minimapColorLookup;
                pendingMinimapSource = await Task.Run(
                        () => CreateMinimapSourceIndex(minimapSourceMap, minimapColors, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            // Wszystkie kosztowne i anulowalne operacje kończą się przed zmianą
            // aktywnej mapy/assets. Definicje stworzeń pochodzą wyłącznie z
            // wbudowanych rme-data, otwartej mapy albo jawnego importu XML.
            cancellationToken.ThrowIfCancellationRequested();
            ReportLoadProgress(operation, 88, "Publikowanie mapy, assets i indeksów…");
            // PropertyChanged działa synchronicznie; również anulowanie wywołane
            // dokładnie po pokazaniu etapu publikacji musi zatrzymać operację przed
            // pierwszą podmianą aktywnego stanu.
            cancellationToken.ThrowIfCancellationRequested();
            if (loadedMap is { } map)
            {
                _map = map;
                _minimapSourceByFloor = pendingMinimapSource;
                _minimapSourceOverridesByFloor =
                    ImmutableDictionary<byte, ImmutableDictionary<OtbmTileCoord, MapMinimapSourceOverride>>.Empty;
                MapPath = otbmPath!;
                MapFileName = Path.GetFileName(otbmPath!);
                IsDirty = false;
                _externalDataDirty = false;
                _history.Reset();
                _modifiedBaseline.Clear();
                _previousPositions.Clear();
                _nextPositions.Clear();
                UpdateNavigationState();
                ResetSelectionMoveState();
                _selectedCoordinates.Clear();
                _selectionAnchor = null;
                UpdateSelectionState();
                UpdateHistoryState();
                OnPropertyChanged(nameof(CanSave));
                LoadMapPropertyFields(map);
                RefreshMapSummary();
                IndexExternalMapData(map);
                RefreshEntityEditors();

                _floorTileCounts.Clear();
                if (mapAnalysis is { HasTiles: true } analysis)
                {
                    foreach (var (floor, count) in analysis.FloorTileCounts)
                        _floorTileCounts[floor] = count;
                    FloorBreakdown = analysis.FloorBreakdown;
                    _suppressViewportRefresh = true;
                    try
                    {
                        CurrentFloor = analysis.TargetFloor;
                        ViewX = (ushort)Math.Max(0, analysis.AverageX - _viewWidth / 2);
                        ViewY = (ushort)Math.Max(0, analysis.AverageY - _viewHeight / 2);
                    }
                    finally
                    {
                        _suppressViewportRefresh = false;
                    }
                    GoToX = analysis.AverageX;
                    GoToY = analysis.AverageY;
                    GoToZ = analysis.TargetFloor;
                }
                else
                {
                    FloorBreakdown = string.Empty;
                }
            }

            if (pendingAssets is not null && assetReport is not null)
            {
                var previousAssets = _assets;
                _assets = pendingAssets;
                pendingAssets = null;
                _minimapColorLookup = _assets.CreateMinimapColorLookup();
                // VisibleTiles oraz kontenery palet Avalonia mogą jeszcze przez jedną
                // generację renderu trzymać bitmapy poprzedniego magazynu. Zachowujemy
                // więc poprzednią generację do następnej podmiany (lub Dispose VM),
                // zamiast unieważniać bitmapy w trakcie klatki UI.
                _retiredAssets.Retain(previousAssets);

                AssetsPath = assetsFolder!;
                AssetsFolderName = Path.GetFileName(
                    assetsFolder!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (loadedMap is null && _map is not null)
                {
                    _minimapSourceByFloor = pendingMinimapSource;
                    _minimapSourceOverridesByFloor =
                        ImmutableDictionary<byte, ImmutableDictionary<OtbmTileCoord, MapMinimapSourceOverride>>.Empty;
                }
                ReportLoadProgress(operation, 91, "Budowanie palet RME…", ignoreCancellation: true);
                BuildPalette();
            }

            ReportLoadProgress(operation, 95, "Przygotowywanie widoku mapy…", ignoreCancellation: true);
            RebuildMapEntityPalettes();

            sw.Stop();
            var parts = new List<string>();
            if (_map is not null) parts.Add($"mapa: {_map.Tiles.Count} kafelków");
            if (_assets.IsLoaded) parts.Add($"assets: {_assets.ObjectCount} obiektów");
            RefreshViewport();
            RequestMinimapRefresh(debounce: false);
            Status = parts.Count == 0
                ? "Brak wczytanych zasobów."
                : $"Gotowe w {sw.ElapsedMilliseconds} ms: " + string.Join(", ", parts) + "." +
                  (assetReport?.Warnings.Count > 0 ? " " + string.Join(" ", assetReport.Warnings) : string.Empty);
            if (externalWarnings.Count > 0)
                Status += " " + string.Join(" ", externalWarnings);
            ReportLoadProgress(operation, 100, "Wczytywanie zakończone.", ignoreCancellation: true);
        }
        finally
        {
            pendingAssets?.Dispose();
        }
    }

    private void ReportLoadProgress(
        CancellationTokenSource operation,
        double progress,
        string stage,
        bool indeterminate = false,
        bool ignoreCancellation = false)
    {
        if (!ReferenceEquals(_loadCancellation, operation) ||
            !ignoreCancellation && operation.IsCancellationRequested) return;
        var normalized = Math.Clamp(progress, 0, 100);
        if (normalized + 0.001 < LoadProgress) return;
        LoadProgress = normalized;
        LoadProgressLabel = $"{normalized:0}%";
        LoadStage = stage;
        IsLoadProgressIndeterminate = indeterminate;
    }

    private static MapLoadAnalysis AnalyzeLoadedMap(OtbmMap map, CancellationToken cancellationToken)
    {
        if (map.Tiles.Count == 0)
            return new MapLoadAnalysis(
                new Dictionary<byte, int>(),
                string.Empty,
                0,
                0,
                0,
                false);

        var floorStats = new Dictionary<byte, (long SumX, long SumY, int Count)>();
        var index = 0;
        foreach (var tile in map.Tiles.Values)
        {
            if ((index++ & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
            var stats = floorStats.GetValueOrDefault(tile.Z);
            floorStats[tile.Z] = (stats.SumX + tile.X, stats.SumY + tile.Y, stats.Count + 1);
        }

        var targetFloor = floorStats.MaxBy(pair => pair.Value.Count).Key;
        var targetStats = floorStats[targetFloor];
        var counts = floorStats.ToDictionary(pair => pair.Key, pair => pair.Value.Count);
        var breakdown = string.Join(" | ",
            floorStats.OrderBy(pair => pair.Key).Select(pair => $"Z{pair.Key}={pair.Value.Count}"));
        return new MapLoadAnalysis(
            counts,
            breakdown,
            targetFloor,
            (int)(targetStats.SumX / targetStats.Count),
            (int)(targetStats.SumY / targetStats.Count),
            true);
    }

    [RelayCommand]
    private async Task LoadAll() => await LoadAllAsync(MapPath, AssetsPath).ConfigureAwait(true);

    [RelayCommand]
    private async Task ReloadData()
    {
        if (string.IsNullOrWhiteSpace(MapPath) && string.IsNullOrWhiteSpace(AssetsPath))
        {
            Status = "Brak wczytanych danych do ponownego załadowania.";
            return;
        }
        if (IsDirty)
        {
            Status = "Najpierw zapisz albo cofnij zmiany — Reload nie nadpisuje niezapisanej mapy.";
            return;
        }
        await LoadAllAsync(
            File.Exists(MapPath) ? MapPath : null,
            Directory.Exists(AssetsPath) ? AssetsPath : null).ConfigureAwait(true);
    }

    // Otwarcie kolejnej mapy nie może ponownie parsować całego klienta. Aktywne
    // assets pozostają w pamięci; jawne „Wczytaj wszystko” i „Reload” nadal mogą
    // przeładować oba źródła, gdy użytkownik tego potrzebuje.
    public Task LoadMapAsync(string path) => LoadAllAsync(path, null);

    public Task LoadAssetsAsync(string folderPath) => LoadAllAsync(null, folderPath);

    public async Task SaveMapAsync(string path)
    {
        if (_map is null)
        {
            Status = "Brak mapy do zapisu.";
            return;
        }
        Status = $"Zapisywanie {Path.GetFileName(path)}...";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var sourceMapPath = MapPath;
            var externalDataDirty = _externalDataDirty;
            var copiedFiles = await Task.Run(() =>
            {
                SaveAtomically(_map, path);
                return externalDataDirty
                    ? OtbmExternalDataWriter.Save(_map, path)
                    : CopyExternalMapFiles(_map, sourceMapPath, path);
            }).ConfigureAwait(true);
            sw.Stop();
            MapPath = path;
            MapFileName = Path.GetFileName(path);
            IsDirty = false;
            _externalDataDirty = false;
            _history.MarkSaved();
            CaptureModifiedBaseline();
            UpdateHistoryState();
            Status = $"Zapisano {Path.GetFileName(path)} w {sw.ElapsedMilliseconds} ms." +
                     (copiedFiles > 0 ? $" Skopiowano także {copiedFiles} pliki XML RME." : string.Empty);
        }
        catch (Exception ex)
        {
            Status = $"Błąd zapisu: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveMap()
    {
        if (string.IsNullOrWhiteSpace(MapPath))
        {
            Status = "Podaj ścieżkę zapisu lub użyj 'Zapisz jako'.";
            return;
        }
        await SaveMapAsync(MapPath).ConfigureAwait(true);
    }

    [RelayCommand]
    private void FloorUp()
    {
        if (CurrentFloor > 0) CurrentFloor--;
    }

    [RelayCommand]
    private void FloorDown()
    {
        if (CurrentFloor < 15) CurrentFloor++;
    }

    [RelayCommand] private void PanLeft()  => PanBy(-1, 0);
    [RelayCommand] private void PanRight() => PanBy(1, 0);
    [RelayCommand] private void PanUp()    => PanBy(0, -1);
    [RelayCommand] private void PanDown()  => PanBy(0, 1);

    public void PanBy(int deltaX, int deltaY)
    {
        var x = (ushort)Math.Clamp((long)ViewX + deltaX, 0, ushort.MaxValue);
        var y = (ushort)Math.Clamp((long)ViewY + deltaY, 0, ushort.MaxValue);
        SetViewOrigin(x, y);
    }

    /// <summary>Wyśrodkuj viewport na centroid tile na aktualnym floor.</summary>
    [RelayCommand]
    private void AutoCenter()
    {
        if (_map is null) return;
        var floorTiles = _map.Tiles.Values.Where(t => t.Z == CurrentFloor).ToList();
        if (floorTiles.Count == 0) return;
        var avgX = (int)floorTiles.Average(t => (double)t.X);
        var avgY = (int)floorTiles.Average(t => (double)t.Y);
        SetViewOrigin(
            (ushort)Math.Max(0, avgX - _viewWidth / 2),
            (ushort)Math.Max(0, avgY - _viewHeight / 2));
    }

    [RelayCommand]
    private void ZoomIn()
    {
        if (TileSize < 64) TileSize += 8;
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (TileSize > 8) TileSize -= 8;
    }

    [RelayCommand]
    private void ZoomNormal() => TileSize = 32;

    /// <summary>
    /// Zmienia zoom z kotwica pod kursorem, tak jak RME. Wspolrzedna mapy wskazywana
    /// przez mysz pozostaje w tym samym miejscu ekranu, a caly viewport jest liczony raz.
    /// </summary>
    public void ZoomAt(double pointerX, double pointerY, double viewportWidth, double viewportHeight, bool zoomIn)
    {
        var oldTileSize = Math.Max(1, TileSize);
        var newTileSize = Math.Clamp(oldTileSize + (zoomIn ? 8 : -8), 8, 64);
        if (newTileSize == oldTileSize) return;

        var safePointerX = Math.Clamp(pointerX, 0, Math.Max(0, viewportWidth));
        var safePointerY = Math.Clamp(pointerY, 0, Math.Max(0, viewportHeight));
        var anchorMapX = ViewX + (int)Math.Floor(safePointerX / oldTileSize);
        var anchorMapY = ViewY + (int)Math.Floor(safePointerY / oldTileSize);
        var columns = Math.Max(8, (int)Math.Ceiling(Math.Max(1, viewportWidth) / newTileSize));
        var rows = Math.Max(8, (int)Math.Ceiling(Math.Max(1, viewportHeight) / newTileSize));
        var originX = (ushort)Math.Clamp(
            anchorMapX - (int)Math.Floor(safePointerX / newTileSize),
            0,
            ushort.MaxValue);
        var originY = (ushort)Math.Clamp(
            anchorMapY - (int)Math.Floor(safePointerY / newTileSize),
            0,
            ushort.MaxValue);

        _suppressViewportRefresh = true;
        try
        {
            TileSize = newTileSize;
            _viewWidth = columns;
            _viewHeight = rows;
            ViewX = originX;
            ViewY = originY;
        }
        finally
        {
            _suppressViewportRefresh = false;
        }

        OnPropertyChanged(nameof(VisibleColumns));
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        RefreshViewport();
        UpdateMinimapViewportOverlay();
    }

    partial void OnCurrentFloorChanged(byte value)
    {
        if (_suppressViewportRefresh) return;
        RefreshViewport();
        RequestMinimapRefresh();
    }
    partial void OnViewXChanged(ushort value)
    {
        if (!_suppressViewportRefresh)
        {
            RefreshViewport();
            UpdateMinimapViewportOverlay();
        }
    }
    partial void OnViewYChanged(ushort value)
    {
        if (!_suppressViewportRefresh)
        {
            RefreshViewport();
            UpdateMinimapViewportOverlay();
        }
    }
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(DirtyLabel));
    partial void OnShowGridChanged(bool value) => RefreshViewport();
    partial void OnShowHousesChanged(bool value) => RefreshViewport();
    partial void OnShowSpawnsChanged(bool value) => RefreshViewport();
    partial void OnShowCreaturesChanged(bool value) => RefreshViewport();
    partial void OnShowSpecialTilesChanged(bool value) => RefreshViewport();
    partial void OnShowItemsChanged(bool value) => RefreshViewport();
    partial void OnShowWaypointsChanged(bool value) => RefreshViewport();
    partial void OnShowBlockingChanged(bool value) => RefreshViewport();
    partial void OnShowTooltipsChanged(bool value) => RefreshViewport();
    partial void OnShowPreviewChanged(bool value)
    {
        UpdateBrushPreview();
    }
    partial void OnShowAnimationChanged(bool value)
    {
        if (!value) _animationFrame = 0;
        OnPropertyChanged(nameof(AnimationFrame));
        RefreshViewport();
    }
    partial void OnShowOnlyColorsChanged(bool value) => RefreshViewport();
    partial void OnShowAsMinimapChanged(bool value) => RefreshViewport();
    partial void OnShowAllFloorsChanged(bool value) => RefreshViewport();
    partial void OnShowShadeChanged(bool value) => RefreshViewport();
    partial void OnShowTownsChanged(bool value) => RefreshViewport();
    partial void OnShowLightsChanged(bool value) => RefreshViewport();
    partial void OnShowLightStrengthChanged(bool value) => RefreshViewport();
    partial void OnExperimentalFogChanged(bool value) => RefreshViewport();
    partial void OnShowWallHooksChanged(bool value) => RefreshViewport();
    partial void OnHighlightItemsChanged(bool value) => RefreshViewport();
    partial void OnHighlightLockedDoorsChanged(bool value) => RefreshViewport();
    partial void OnGhostLooseItemsChanged(bool value) => RefreshViewport();
    partial void OnGhostHigherFloorsChanged(bool value) => RefreshViewport();
    partial void OnShowClientBoxChanged(bool value) => RefreshViewport();
    partial void OnAlwaysShowZonesChanged(bool value) => RefreshViewport();
    partial void OnExtendedHouseShaderChanged(bool value) => RefreshViewport();
    partial void OnShowTechnicalItemsChanged(bool value) => RefreshViewport();
    partial void OnShowOnlyModifiedChanged(bool value) => RefreshViewport();

    /// <summary>
    /// Przesuwa wspólny zegar animacji mapy. Tak jak RME, animuje tylko przy
    /// zbliżeniu pozwalającym rozpoznać sprite'y i nie przebudowuje widoku w trybie minimapy.
    /// </summary>
    public void AdvanceAnimationFrame()
    {
        if (!ShowAnimation || ShowOnlyColors || ShowAsMinimap || TileSize < 16 || _map is null || !_assets.IsLoaded ||
            !_visibleHasAnimations) return;
        _animationFrame = (_animationFrame + 1) % 1024;
        OnPropertyChanged(nameof(AnimationFrame));
        RefreshViewport(updateStatus: false);
    }

    [RelayCommand]
    private void ClearModifiedState()
    {
        if (_map is null) return;
        CaptureModifiedBaseline();
        RefreshViewport();
        Status = "Wyczyszczono wizualne oznaczenie zmodyfikowanych pól. Stan Undo i niezapisane zmiany pozostały bez zmian.";
    }

    private void CaptureModifiedBaseline()
    {
        if (_map is null) return;
        foreach (var coordinate in _modifiedBaseline.Keys.ToArray())
            _modifiedBaseline[coordinate] = MapEditHistory.CloneTile(_map.Tiles.GetValueOrDefault(coordinate));
    }

    private bool IsTileModified(OtbmTileCoord coordinate, OtbmTile? current) =>
        _modifiedBaseline.TryGetValue(coordinate, out var baseline) && !TilesEqual(baseline, current);

    private void RememberModifiedBaselines(IReadOnlyList<MapTileChange> changes)
    {
        foreach (var change in changes)
            RememberModifiedBaseline(change.Coordinate, change.Before);
    }

    private void RememberModifiedBaseline(OtbmTileCoord coordinate, OtbmTile? before)
    {
        if (_modifiedBaseline.ContainsKey(coordinate)) return;
        _modifiedBaseline[coordinate] = MapEditHistory.CloneTile(before);
    }

    private static bool TilesEqual(OtbmTile? left, OtbmTile? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.X != right.X || left.Y != right.Y || left.Z != right.Z ||
            left.Flags != right.Flags || left.GroundItemId != right.GroundItemId ||
            left.IsHouseTile != right.IsHouseTile || left.HouseId != right.HouseId ||
            !left.RawAttributeData.AsSpan().SequenceEqual(right.RawAttributeData) ||
            left.Items.Count != right.Items.Count) return false;
        for (var index = 0; index < left.Items.Count; index++)
            if (!ItemsEqual(left.Items[index], right.Items[index])) return false;
        return true;
    }

    private static bool ItemsEqual(OtbmItem left, OtbmItem right)
    {
        if (left.Id != right.Id || left.ActionId != right.ActionId || left.UniqueId != right.UniqueId ||
            left.Count != right.Count || left.Text != right.Text || left.Description != right.Description ||
            left.DepotId != right.DepotId || left.HouseDoorId != right.HouseDoorId ||
            left.RuneCharges != right.RuneCharges || left.Duration != right.Duration ||
            left.DecayingState != right.DecayingState || left.WrittenDate != right.WrittenDate ||
            left.WrittenBy != right.WrittenBy || left.SleeperGuid != right.SleeperGuid ||
            left.SleepStart != right.SleepStart || left.Charges != right.Charges || left.Tier != right.Tier ||
            left.TeleportX != right.TeleportX || left.TeleportY != right.TeleportY || left.TeleportZ != right.TeleportZ ||
            !left.RawAttributeData.AsSpan().SequenceEqual(right.RawAttributeData) ||
            !(left.PodiumOutfit ?? []).AsSpan().SequenceEqual(right.PodiumOutfit ?? []) ||
            !CustomAttributesEqual(left.CustomAttributes, right.CustomAttributes) ||
            left.Contents.Count != right.Contents.Count) return false;
        for (var index = 0; index < left.Contents.Count; index++)
            if (!ItemsEqual(left.Contents[index], right.Contents[index])) return false;
        return true;
    }

    private static bool CustomAttributesEqual(
        IReadOnlyList<OtbmCustomAttribute> left,
        IReadOnlyList<OtbmCustomAttribute> right)
    {
        if (left.Count != right.Count) return false;
        var orderedLeft = left.OrderBy(attribute => attribute.Key, StringComparer.Ordinal).ToArray();
        var orderedRight = right.OrderBy(attribute => attribute.Key, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < orderedLeft.Length; index++)
        {
            var a = orderedLeft[index];
            var b = orderedRight[index];
            if (a.Key != b.Key || a.Type != b.Type || a.StringValue != b.StringValue ||
                a.IntegerValue != b.IntegerValue ||
                BitConverter.SingleToInt32Bits(a.FloatValue) != BitConverter.SingleToInt32Bits(b.FloatValue) ||
                a.BooleanValue != b.BooleanValue ||
                BitConverter.DoubleToInt64Bits(a.DoubleValue) != BitConverter.DoubleToInt64Bits(b.DoubleValue))
                return false;
        }
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        ResetSelectionMoveState();
        var edit = _history.Undo();
        if (edit is null || _map is null) return;
        foreach (var change in edit.Changes)
            ApplySnapshot(change.Coordinate, change.Before);
        UpdateMinimapSourceOverrides(edit.Changes.Select(change => change.Coordinate));
        if (edit.WaypointChange is { } waypointChange)
            ApplyWaypointSnapshot(waypointChange.Index, waypointChange.Before);
        if (edit.SpawnChange is { } spawnChange)
            ApplySpawnSnapshot(spawnChange.Before);
        if (edit.PropertiesChange is { } propertiesChange)
            ApplyMapPropertiesSnapshot(propertiesChange.Before);
        if (edit.EntitiesChange is { } entitiesChange)
            ApplyEntitiesSnapshot(entitiesChange.Before);
        PruneSelectionToExistingTiles();
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        if (_propertyCoordinate is { } propertyCoordinate)
            LoadTileProperties(propertyCoordinate);
        Status = $"Cofnięto: {edit.Description}.";
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        ResetSelectionMoveState();
        var edit = _history.Redo();
        if (edit is null || _map is null) return;
        foreach (var change in edit.Changes)
            ApplySnapshot(change.Coordinate, change.After);
        UpdateMinimapSourceOverrides(edit.Changes.Select(change => change.Coordinate));
        if (edit.WaypointChange is { } waypointChange)
            ApplyWaypointSnapshot(waypointChange.Index, waypointChange.After);
        if (edit.SpawnChange is { } spawnChange)
            ApplySpawnSnapshot(spawnChange.After);
        if (edit.PropertiesChange is { } propertiesChange)
            ApplyMapPropertiesSnapshot(propertiesChange.After);
        if (edit.EntitiesChange is { } entitiesChange)
            ApplyEntitiesSnapshot(entitiesChange.After);
        PruneSelectionToExistingTiles();
        UpdateHistoryState();
        RefreshViewport();
        RequestMinimapRefresh();
        if (_propertyCoordinate is { } propertyCoordinate)
            LoadTileProperties(propertyCoordinate);
        Status = $"Ponowiono: {edit.Description}.";
    }

    private void ApplySnapshot(OtbmTileCoord coordinate, OtbmTile? snapshot)
    {
        if (_map is null) return;
        var existed = _map.Tiles.ContainsKey(coordinate);
        if (snapshot is null)
            _map.Tiles.Remove(coordinate);
        else
            _map.Tiles[coordinate] = MapEditHistory.CloneTile(snapshot)!;

        if (!existed && snapshot is not null)
            _floorTileCounts[coordinate.Z] = _floorTileCounts.GetValueOrDefault(coordinate.Z) + 1;
        else if (existed && snapshot is null)
            _floorTileCounts[coordinate.Z] = Math.Max(0, _floorTileCounts.GetValueOrDefault(coordinate.Z) - 1);
    }

    private void PruneSelectionToExistingTiles()
    {
        if (_map is null)
            _selectedCoordinates.Clear();
        else
            _selectedCoordinates.RemoveWhere(coordinate => !_map.Tiles.ContainsKey(coordinate));
        UpdateSelectionState();
    }

    private void ApplyWaypointSnapshot(int index, OtbmTileCoord coordinate)
    {
        if (_map is null || index < 0 || index >= _map.Waypoints.Count) return;
        var waypoint = _map.Waypoints[index];
        waypoint.X = coordinate.X;
        waypoint.Y = coordinate.Y;
        waypoint.Z = coordinate.Z;
        IndexExternalMapData(_map);
    }

    private void ApplySpawnSnapshot(IReadOnlyList<OtbmSpawn> snapshot)
    {
        if (_map is null) return;
        _map.Spawns.Clear();
        foreach (var spawn in MapEditHistory.CloneSpawns(snapshot))
            _map.Spawns.Add(spawn);
        _externalDataDirty = true;
        IndexExternalMapData(_map);
    }

    private void ApplyMapPropertiesSnapshot(MapPropertiesSnapshot snapshot)
    {
        if (_map is null) return;
        snapshot.ApplyTo(_map);
        LoadMapPropertyFields(_map);
        RefreshMapSummary();
    }

    private void ApplyEntitiesSnapshot(MapEntitiesSnapshot snapshot)
    {
        if (_map is null) return;
        snapshot.ApplyTo(_map);
        _externalDataDirty = true;
        IndexExternalMapData(_map);
        RefreshEntityEditors();
        RebuildMapEntityPalettes();
    }

    [RelayCommand]
    private void NewMap()
    {
        if (IsDirty)
        {
            Status = "Najpierw zapisz albo cofnij niezapisane zmiany przed utworzeniem nowej mapy.";
            return;
        }
        if (!ValidateMapPropertyFields(out var validationError))
        {
            Status = validationError;
            return;
        }

        _map = new OtbmMap
        {
            FileVersion = 0,
            RootNodeType = 0,
            Version = (uint)MapVersion,
            Width = (ushort)MapWidth,
            Height = (ushort)MapHeight,
            ItemsMajorVersion = (uint)MapItemsMajorVersion,
            ItemsMinorVersion = (uint)MapItemsMinorVersion,
            Description = MapDescription,
            HouseFile = MapHouseFile.Trim(),
            SpawnFile = MapSpawnFile.Trim(),
            SpawnNpcFile = MapSpawnNpcFile.Trim()
        };
        _minimapSourceByFloor =
            ImmutableDictionary<byte, ImmutableArray<MapMinimapSourceSample>>.Empty;
        _minimapSourceOverridesByFloor =
            ImmutableDictionary<byte, ImmutableDictionary<OtbmTileCoord, MapMinimapSourceOverride>>.Empty;
        MapPath = string.Empty;
        MapFileName = "(nowa mapa)";
        _suppressViewportRefresh = true;
        try
        {
            CurrentFloor = 7;
            ViewX = 0;
            ViewY = 0;
        }
        finally
        {
            _suppressViewportRefresh = false;
        }
        _floorTileCounts.Clear();
        _externalDataDirty = true;
        _history.Reset();
        _modifiedBaseline.Clear();
        ResetSelectionMoveState();
        _selectedCoordinates.Clear();
        _selectionAnchor = null;
        UpdateSelectionState();
        UpdateHistoryState();
        IndexExternalMapData(_map);
        RefreshEntityEditors();
        TilePropertyItems.Clear();
        SelectedPropertyItem = null;
        _propertyCoordinate = null;
        OnPropertyChanged(nameof(CanSave));
        RefreshMapSummary();
        RefreshViewport();
        RequestMinimapRefresh(debounce: false);
        Status = "Utworzono nową pustą mapę. Użyj „Zapisz jako…”, aby nadać jej nazwę.";
    }

    [RelayCommand]
    private void ApplyMapProperties()
    {
        if (_map is null)
        {
            Status = "Najpierw otwórz mapę albo utwórz nową.";
            return;
        }
        if (!ValidateMapPropertyFields(out var validationError))
        {
            Status = validationError;
            return;
        }

        var before = MapPropertiesSnapshot.FromMap(_map);
        _map.Version = (uint)MapVersion;
        _map.Width = (ushort)MapWidth;
        _map.Height = (ushort)MapHeight;
        _map.ItemsMajorVersion = (uint)MapItemsMajorVersion;
        _map.ItemsMinorVersion = (uint)MapItemsMinorVersion;
        _map.Description = MapDescription;
        _map.HouseFile = MapHouseFile.Trim();
        _map.SpawnFile = MapSpawnFile.Trim();
        _map.SpawnNpcFile = MapSpawnNpcFile.Trim();
        var after = MapPropertiesSnapshot.FromMap(_map);
        _history.PushMapProperties("Map Properties", before, after);
        if (before.HouseFile != after.HouseFile || before.SpawnFile != after.SpawnFile ||
            before.SpawnNpcFile != after.SpawnNpcFile)
            _externalDataDirty = true;
        UpdateHistoryState();
        RefreshMapSummary();
        Status = "Zastosowano właściwości mapy.";
    }

    private bool ValidateMapPropertyFields(out string error)
    {
        if (MapWidth is < 256 or > ushort.MaxValue || MapHeight is < 256 or > ushort.MaxValue)
        {
            error = "Wymiary mapy muszą mieścić się w zakresie 256–65535.";
            return false;
        }
        if (MapVersion is < 0 or > 4)
        {
            error = "Wersja OTBM musi mieścić się w zakresie 0–4.";
            return false;
        }
        if (MapItemsMajorVersion < 0 || MapItemsMinorVersion < 0)
        {
            error = "Wersje items.otb nie mogą być ujemne.";
            return false;
        }
        if (MapVersion < 4 && _map is not null && EnumerateMapItems(_map)
                .Any(item => item.CustomAttributes.Any(attribute => !IsCanonicalCustomKey(attribute.Key))))
        {
            error = "Konwersja do OTBM 0–3 została zablokowana: mapa zawiera własne atrybuty OTBM 4, których starszy format nie potrafi zapisać.";
            return false;
        }
        if (System.Text.Encoding.UTF8.GetByteCount(MapDescription) > ushort.MaxValue)
        {
            error = "Opis mapy przekracza limit 65535 bajtów UTF-8 formatu OTBM.";
            return false;
        }
        foreach (var fileName in new[] { MapHouseFile, MapSpawnFile, MapSpawnNpcFile })
        {
            if (fileName.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                error = "Nazwa zewnętrznego pliku XML zawiera niedozwolone znaki.";
                return false;
            }
            if (System.Text.Encoding.UTF8.GetByteCount(fileName) > ushort.MaxValue)
            {
                error = "Nazwa zewnętrznego pliku XML przekracza limit 65535 bajtów UTF-8.";
                return false;
            }
        }
        error = string.Empty;
        return true;
    }

    private void LoadMapPropertyFields(OtbmMap map)
    {
        MapDescription = map.Description;
        MapWidth = map.Width;
        MapHeight = map.Height;
        MapVersion = checked((int)map.Version);
        MapItemsMajorVersion = checked((int)map.ItemsMajorVersion);
        MapItemsMinorVersion = checked((int)map.ItemsMinorVersion);
        MapHouseFile = map.HouseFile;
        MapSpawnFile = map.SpawnFile;
        MapSpawnNpcFile = map.SpawnNpcFile;
    }

    private void RefreshMapSummary()
    {
        if (_map is not { } map)
        {
            MapSummary = "Brak mapy.";
            return;
        }
        MapSummary = $"Mapa: {map.Width}×{map.Height}, {map.Tiles.Count} kafelków, " +
                     $"{map.Towns.Count} miast, {map.Houses.Count} domów, " +
                     $"{map.Spawns.Count} spawnów, {map.Spawns.Sum(spawn => spawn.Creatures.Count)} stworzeń, " +
                     $"{map.Waypoints.Count} waypoint-ów, items {map.ItemsMajorVersion}.{map.ItemsMinorVersion}.";
    }

    private void UpdateHistoryState()
    {
        IsDirty = _history.IsDirty;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    public void SetHoveredTile(int? tileX, int? tileY)
    {
        if (_map is null || tileX is null || tileY is null)
        {
            if (_hoveredMapX is null && _hoveredMapY is null) return;
            _hoveredMapX = null;
            _hoveredMapY = null;
            HoveredTileLabel = "—";
            return;
        }
        var x = (ushort)tileX.Value;
        var y = (ushort)tileY.Value;
        var hoverChanged = _hoveredMapX != x || _hoveredMapY != y;
        _hoveredMapX = x;
        _hoveredMapY = y;
        var coord = new OtbmTileCoord(x, y, CurrentFloor);
        if (_map.Tiles.TryGetValue(coord, out var tile))
        {
            var groundName = _assets.IsLoaded && tile.GroundItemId != 0
                ? (_assets.GetObjectName(tile.GroundItemId) ?? "?")
                : "(brak)";
            HoveredTileLabel = $"({tileX}, {tileY}, {CurrentFloor})  ground=#{tile.GroundItemId} {groundName}  items={tile.Items.Count}" +
                               (tile.IsHouseTile ? $"  HOUSE#{tile.HouseId}" : string.Empty);
        }
        else
        {
            HoveredTileLabel = $"({tileX}, {tileY}, {CurrentFloor})  (puste)";
        }
        // Sam status kursora nie wymaga przebudowania renderera. Podgląd pędzla
        // odświeżamy wyłącznie wtedy, gdy faktycznie może pojawić się na mapie.
        if (hoverChanged && ShowPreview && SelectedBrush is not null)
            UpdateBrushPreview();
    }

    [RelayCommand]
    private void OpenHoveredTileProperties()
    {
        if (_map is null || _hoveredMapX is null || _hoveredMapY is null)
        {
            PropertyStatus = "Najpierw najedź kursorem na pole mapy.";
            return;
        }
        LoadTileProperties(new OtbmTileCoord(_hoveredMapX.Value, _hoveredMapY.Value, CurrentFloor));
    }

    public void SetContextTile(ushort x, ushort y)
    {
        _contextCoordinate = new OtbmTileCoord(x, y, CurrentFloor);
        SetHoveredTile(x, y);
    }

    public void OpenContextTileProperties()
    {
        if (_contextCoordinate is not { } coordinate)
        {
            Status = "Kliknij prawym przyciskiem na pole mapy.";
            return;
        }
        LoadTileProperties(coordinate);
        SelectedWorkspaceTabIndex = 15;
        Status = $"Otwarto właściwości pola ({coordinate.X}, {coordinate.Y}, {coordinate.Z}).";
    }

    public void SelectContextTile()
    {
        if (_map is null || _contextCoordinate is not { } coordinate) return;
        if (!_map.Tiles.ContainsKey(coordinate))
        {
            Status = "To pole jest puste — zaznaczać można wyłącznie istniejące tile.";
            return;
        }
        SelectionMode = true;
        ResetSelectionMoveState();
        _selectedCoordinates.Clear();
        _selectedCoordinates.Add(coordinate);
        _selectionAnchor = coordinate;
        UpdateSelectionState();
        RefreshViewport();
        Status = $"Zaznaczono pole ({coordinate.X}, {coordinate.Y}, {coordinate.Z}).";
    }

    public void EraseContextTile()
    {
        if (_contextCoordinate is not { } coordinate) return;
        EraseAt(coordinate.X, coordinate.Y);
    }

    public void FindContextItemInPalette()
    {
        if (_map is null || _contextCoordinate is not { } coordinate ||
            !_map.Tiles.TryGetValue(coordinate, out var tile))
        {
            Status = "Na tym polu nie ma itemu do wyszukania w palecie.";
            return;
        }

        var itemId = tile.Items.LastOrDefault()?.Id ?? tile.GroundItemId;
        if (itemId == 0)
        {
            Status = "Na tym polu nie ma itemu do wyszukania w palecie.";
            return;
        }

        var preferredCategory = _assets.IsGround(itemId)
            ? RmePaletteCategory.Terrain
            : RmePaletteCategory.Item;
        var match = Tilesets
            .SelectMany(tileset => tileset.Categories.SelectMany(category => category.Value.Select(entry =>
                (Tileset: tileset, Category: category.Key, Entry: entry))))
            .Where(candidate => PaletteEntryContains(candidate.Entry, itemId))
            .OrderBy(candidate => candidate.Category == preferredCategory ? 0 :
                candidate.Category == RmePaletteCategory.Raw ? 2 : 1)
            .FirstOrDefault();

        if (match.Tileset is not null)
        {
            PaletteSearch = string.Empty;
            SelectedTileset = match.Tileset;
            PaletteSearch = match.Entry.Brush?.Name ?? match.Entry.BrushName ?? itemId.ToString(CultureInfo.InvariantCulture);
            SelectedWorkspaceTabIndex = PaletteCategoryToTab(match.Category);
            SelectedBrush = EnumeratePaletteItems().FirstOrDefault(candidate =>
                                candidate.ItemId == itemId ||
                                candidate.Brush?.AllItemIds.Contains(itemId) == true) ??
                            EnumeratePaletteItems().FirstOrDefault();
            Status = $"Znaleziono Server ID #{itemId}: {match.Tileset.Name} / {match.Category}.";
            return;
        }

        PaletteSearch = itemId.ToString(CultureInfo.InvariantCulture);
        SelectedWorkspaceTabIndex = 3;
        SelectedBrush = new MapPaletteItem
        {
            ItemId = itemId,
            Name = _assets.GetObjectName(itemId) ?? $"RAW #{itemId}",
            Category = RmePaletteCategory.Raw,
            ThumbnailLoader = () => _assets.GetThumbnail(itemId)
        };
        Status = $"Server ID #{itemId} nie należy do tilesetu RME; wybrano go jako RAW.";
    }

    public void FindContextCreatureInPalette()
    {
        if (_contextCoordinate is not { } coordinate ||
            !_creaturesByTile.TryGetValue(coordinate, out var creatures) || creatures.Count == 0)
        {
            Status = "Na tym polu nie ma potwora ani NPC.";
            return;
        }
        var creature = creatures[^1];
        CreatureFilter = "Wszystkie";
        PaletteSearch = creature.Name;
        SelectedWorkspaceTabIndex = 5;
        SelectedBrush = CreaturePalette.FirstOrDefault(item =>
            string.Equals(item.EntityName, creature.Name, StringComparison.OrdinalIgnoreCase));
        Status = SelectedBrush is null
            ? $"Stworzenie „{creature.Name}” jest na mapie, ale nie ma definicji wyglądu."
            : $"Znaleziono {(creature.IsNpc ? "NPC" : "potwora")} „{creature.Name}” w palecie.";
    }

    public void ToggleContextZone(string zone)
    {
        if (_map is null || _contextCoordinate is not { } coordinate ||
            !_map.Tiles.TryGetValue(coordinate, out var tile))
        {
            Status = "Strefę można ustawić tylko na istniejącym polu mapy.";
            return;
        }
        var kind = zone switch
        {
            "ProtectionZone" => MapPaletteEntityKind.ProtectionZone,
            "NoPvpZone" => MapPaletteEntityKind.NoPvpZone,
            "NoLogoutZone" => MapPaletteEntityKind.NoLogoutZone,
            "PvpZone" => MapPaletteEntityKind.PvpZone,
            _ => MapPaletteEntityKind.None
        };
        if (!TryGetZoneFlag(kind, out var flag, out var name)) return;
        PaintZoneAt(coordinate.X, coordinate.Y, flag, name, (tile.Flags & flag) == 0, size: 0);
    }

    public void AssignContextTileToSelectedHouse()
    {
        if (_contextCoordinate is not { } coordinate) return;
        if (SelectedHouse is null)
        {
            SelectedWorkspaceTabIndex = 6;
            Status = "Najpierw wybierz dom w zakładce Domy, potem ponów operację.";
            return;
        }
        PaintHouseAt(coordinate.X, coordinate.Y, SelectedHouse.Id);
    }

    public void RemoveContextSpawnOrCreatures(bool removeSpawn)
    {
        if (_contextCoordinate is not { } coordinate) return;
        EraseMapEntityAt(coordinate, removeSpawn ? MapPaletteEntityKind.Spawn : MapPaletteEntityKind.Creature);
    }

    private static int PaletteCategoryToTab(RmePaletteCategory category) => category switch
    {
        RmePaletteCategory.Terrain => 0,
        RmePaletteCategory.Doodad => 1,
        RmePaletteCategory.Item => 2,
        RmePaletteCategory.Raw => 3,
        RmePaletteCategory.Collection => 4,
        RmePaletteCategory.Creature => 5,
        RmePaletteCategory.House => 6,
        RmePaletteCategory.Waypoint => 7,
        _ => 0
    };

    private static bool PaletteEntryContains(RmePaletteEntry entry, ushort itemId) =>
        entry.ItemId == itemId || entry.Brush?.ServerLookId == itemId ||
        entry.Brush?.AllItemIds.Contains(itemId) == true;

    private void LoadTileProperties(OtbmTileCoord coordinate)
    {
        TilePropertyItems.Clear();
        SelectedPropertyItem = null;
        _propertyCoordinate = coordinate;
        PropertyTileLabel = $"Pole ({coordinate.X}, {coordinate.Y}, {coordinate.Z})";
        if (_map is null || !_map.Tiles.TryGetValue(coordinate, out var tile))
        {
            LoadTileFlagFields(null);
            PropertyStatus = "Pole jest puste.";
            return;
        }

        LoadTileFlagFields(tile);

        for (var index = tile.Items.Count - 1; index >= 0; index--)
            AddPropertyItem(tile.Items[index], $"[{index}]", depth: 0);
        if (TilePropertyItems.Count > 0)
        {
            SelectedPropertyItem = TilePropertyItems[0];
            PropertyStatus = $"Ground #{tile.GroundItemId}; elementów edytowalnych: {TilePropertyItems.Count}.";
        }
        else
        {
            PropertyStatus = tile.GroundItemId == 0
                ? "Pole nie zawiera itemów."
                : $"Ground #{tile.GroundItemId} nie ma osobnego węzła atrybutów.";
        }
    }

    private void LoadTileFlagFields(OtbmTile? tile)
    {
        var flags = tile?.Flags ?? 0;
        PropertyProtectionZone = (flags & 0x01) != 0;
        PropertyNoPvpZone = (flags & 0x04) != 0;
        PropertyNoLogoutZone = (flags & 0x08) != 0;
        PropertyPvpZone = (flags & 0x10) != 0;
        PropertyRefreshTile = (flags & 0x20) != 0;
    }

    [RelayCommand]
    private void ApplyTileProperties()
    {
        if (_map is null || _propertyCoordinate is not { } coordinate)
        {
            PropertyStatus = "Najpierw wczytaj pole pod kursorem.";
            return;
        }

        _map.Tiles.TryGetValue(coordinate, out var tile);
        var before = MapEditHistory.CloneTile(tile);
        tile ??= new OtbmTile { X = coordinate.X, Y = coordinate.Y, Z = coordinate.Z };

        const uint editableFlags = 0x01 | 0x04 | 0x08 | 0x10 | 0x20;
        var flags = tile.Flags & ~editableFlags;
        if (PropertyProtectionZone) flags |= 0x01;
        if (PropertyNoPvpZone) flags |= 0x04;
        if (PropertyNoLogoutZone) flags |= 0x08;
        if (PropertyPvpZone) flags |= 0x10;
        if (PropertyRefreshTile) flags |= 0x20;
        if (flags == tile.Flags)
        {
            PropertyStatus = "Flagi pola nie zostały zmienione.";
            return;
        }

        tile.Flags = flags;
        tile.RawAttributeData = [];
        _map.Tiles[coordinate] = tile;
        if (before is null)
            _floorTileCounts[coordinate.Z] = _floorTileCounts.GetValueOrDefault(coordinate.Z) + 1;
        _history.Push("Tile Flags", coordinate, before, tile);
        UpdateMinimapSourceOverrides([coordinate]);
        UpdateHistoryState();
        RefreshMapSummary();
        RefreshViewport();
        RequestMinimapRefresh();
        LoadTileProperties(coordinate);
        PropertyStatus = $"Zapisano flagi pola ({coordinate.X}, {coordinate.Y}, {coordinate.Z}).";
        Status = PropertyStatus;
    }

    private void AddPropertyItem(OtbmItem item, string path, int depth)
    {
        var indent = new string('·', depth);
        TilePropertyItems.Add(new MapTilePropertyItem
        {
            Item = item,
            Label = $"{indent}{path}  #{item.Id}" +
                    (item.Contents.Count > 0 ? $"  (container: {item.Contents.Count})" : string.Empty)
        });
        for (var index = 0; index < item.Contents.Count; index++)
            AddPropertyItem(item.Contents[index], $"{path}/{index}", depth + 1);
    }

    [RelayCommand]
    private void ApplyItemProperties()
    {
        if (_map is null || _propertyCoordinate is not { } coordinate ||
            SelectedPropertyItem?.Item is not { } item ||
            !_map.Tiles.TryGetValue(coordinate, out var tile))
        {
            PropertyStatus = "Wybierz item z wczytanego pola.";
            return;
        }
        if (PropertyActionId is not 0 && PropertyActionId is < 100 or > ushort.MaxValue)
        {
            PropertyStatus = "Action ID musi być równe 0 albo mieścić się w zakresie 100–65535.";
            return;
        }
        if (PropertyUniqueId is not 0 && PropertyUniqueId is < 1000 or > ushort.MaxValue)
        {
            PropertyStatus = "Unique ID musi być równe 0 albo mieścić się w zakresie 1000–65535.";
            return;
        }
        if (PropertyUniqueId != 0 && EnumerateMapItems(_map).Any(candidate =>
                !ReferenceEquals(candidate, item) && candidate.UniqueId == PropertyUniqueId))
        {
            PropertyStatus = $"Unique ID {PropertyUniqueId} jest już używane na mapie.";
            return;
        }
        if (PropertyCount is < 0 or > byte.MaxValue || PropertyDepotId is < 0 or > ushort.MaxValue ||
            PropertyHouseDoorId is < 0 or > byte.MaxValue || PropertyTier is < 0 or > byte.MaxValue ||
            PropertyRuneCharges is < 0 or > byte.MaxValue || PropertyDecayingState is < 0 or > byte.MaxValue ||
            PropertyCharges is < 0 or > ushort.MaxValue ||
            PropertyDuration is < 0 or > uint.MaxValue || PropertyWrittenDate is < 0 or > uint.MaxValue ||
            PropertySleeperGuid is < 0 or > uint.MaxValue || PropertySleepStart is < 0 or > uint.MaxValue ||
            PropertyTeleportX is < 0 or > ushort.MaxValue || PropertyTeleportY is < 0 or > ushort.MaxValue ||
            PropertyTeleportZ is < 0 or > 15)
        {
            PropertyStatus = "Jedna z wartości liczbowych wykracza poza zakres formatu OTBM.";
            return;
        }
        if (!TryBuildCustomAttributes(out var customAttributes, out var customError))
        {
            PropertyStatus = customError;
            return;
        }
        if (!ValidatePodiumProperties(out var podiumError))
        {
            PropertyStatus = podiumError;
            return;
        }
        if (System.Text.Encoding.UTF8.GetByteCount(PropertyText) > ushort.MaxValue ||
            System.Text.Encoding.UTF8.GetByteCount(PropertyDescription) > ushort.MaxValue ||
            System.Text.Encoding.UTF8.GetByteCount(PropertyWrittenBy) > ushort.MaxValue)
        {
            PropertyStatus = "Tekst, opis lub autor przekracza limit 65535 bajtów UTF-8 formatu OTBM.";
            return;
        }

        var before = MapEditHistory.CloneTile(tile);
        item.ActionId = PropertyActionId == 0 ? null : (ushort)PropertyActionId;
        item.UniqueId = PropertyUniqueId == 0 ? null : (ushort)PropertyUniqueId;
        item.Count = PropertyCount == 0 ? null : (byte)PropertyCount;
        item.Text = string.IsNullOrEmpty(PropertyText) ? null : PropertyText;
        item.Description = string.IsNullOrEmpty(PropertyDescription) ? null : PropertyDescription;
        item.DepotId = PropertyDepotId == 0 ? null : (ushort)PropertyDepotId;
        item.HouseDoorId = PropertyHouseDoorId == 0 ? null : (byte)PropertyHouseDoorId;
        item.Tier = PropertyTier == 0 ? null : (byte)PropertyTier;
        item.RuneCharges = PropertyRuneCharges == 0 ? null : (byte)PropertyRuneCharges;
        item.Duration = PropertyDuration == 0 ? null : (uint)PropertyDuration;
        item.DecayingState = PropertyDecayingState == 0 ? null : (byte)PropertyDecayingState;
        item.WrittenDate = PropertyWrittenDate == 0 ? null : (uint)PropertyWrittenDate;
        item.WrittenBy = string.IsNullOrEmpty(PropertyWrittenBy) ? null : PropertyWrittenBy;
        item.SleeperGuid = PropertySleeperGuid == 0 ? null : (uint)PropertySleeperGuid;
        item.SleepStart = PropertySleepStart == 0 ? null : (uint)PropertySleepStart;
        item.Charges = PropertyCharges == 0 ? null : (ushort)PropertyCharges;
        item.PodiumOutfit = PropertyHasPodiumOutfit ? BuildPodiumOutfit() : null;
        item.CustomAttributes.RemoveAll(attribute => !IsCanonicalCustomKey(attribute.Key));
        item.CustomAttributes.AddRange(customAttributes);
        var hasTeleport = PropertyTeleportX != 0 || PropertyTeleportY != 0 || PropertyTeleportZ != 0;
        item.TeleportX = hasTeleport ? (ushort)PropertyTeleportX : null;
        item.TeleportY = hasTeleport ? (ushort)PropertyTeleportY : null;
        item.TeleportZ = hasTeleport ? (byte)PropertyTeleportZ : null;
        // Edytowany element przechodzi z bezstratnej kopii surowej do jawnych,
        // zwalidowanych atrybutów. Nieedytowane elementy nadal są kopiowane 1:1.
        item.RawAttributeData = [];

        _history.Push("Item Properties", coordinate, before, tile);
        UpdateHistoryState();
        RefreshViewport();
        LoadTileProperties(coordinate);
        PropertyStatus = $"Zapisano właściwości itemu #{item.Id}.";
        Status = $"Zmieniono właściwości itemu #{item.Id} na ({coordinate.X}, {coordinate.Y}, {coordinate.Z}).";
    }

    [RelayCommand]
    private void AddCustomAttribute() =>
        PropertyCustomAttributes.Add(new MapCustomAttributeItem { Type = "String" });

    [RelayCommand]
    private void RemoveCustomAttribute()
    {
        if (SelectedPropertyCustomAttribute is { } attribute)
            PropertyCustomAttributes.Remove(attribute);
    }

    private bool TryBuildCustomAttributes(
        out IReadOnlyList<OtbmCustomAttribute> attributes,
        out string error)
    {
        var result = new List<OtbmCustomAttribute>(PropertyCustomAttributes.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var editor in PropertyCustomAttributes)
        {
            var key = editor.Key.Trim();
            if (string.IsNullOrEmpty(key))
            {
                attributes = [];
                error = "Klucz własnego atrybutu nie może być pusty.";
                return false;
            }
            if (System.Text.Encoding.UTF8.GetByteCount(key) > ushort.MaxValue)
            {
                attributes = [];
                error = "Klucz własnego atrybutu przekracza limit 65535 bajtów UTF-8.";
                return false;
            }
            if (IsCanonicalCustomKey(key))
            {
                attributes = [];
                error = $"Atrybut „{key}” jest edytowany w podstawowej sekcji właściwości.";
                return false;
            }
            if (!keys.Add(key))
            {
                attributes = [];
                error = $"Klucz własnego atrybutu „{key}” występuje więcej niż raz.";
                return false;
            }
            if (!editor.TryToModel(key, out var attribute))
            {
                attributes = [];
                error = $"Wartość atrybutu „{key}” nie pasuje do typu {editor.Type}.";
                return false;
            }
            result.Add(attribute);
        }
        attributes = result;
        error = string.Empty;
        return true;
    }

    private bool ValidatePodiumProperties(out string error)
    {
        if (!PropertyHasPodiumOutfit)
        {
            error = string.Empty;
            return true;
        }
        if (PropertyPodiumDirection is < 0 or > byte.MaxValue ||
            PropertyPodiumLookType is < 0 or > ushort.MaxValue ||
            PropertyPodiumLookMount is < 0 or > ushort.MaxValue ||
            PropertyPodiumLookAddon is < 0 or > 3 ||
            PropertyPodiumLookHead is < 0 or > 133 || PropertyPodiumLookBody is < 0 or > 133 ||
            PropertyPodiumLookLegs is < 0 or > 133 || PropertyPodiumLookFeet is < 0 or > 133 ||
            PropertyPodiumMountHead is < 0 or > 133 || PropertyPodiumMountBody is < 0 or > 133 ||
            PropertyPodiumMountLegs is < 0 or > 133 || PropertyPodiumMountFeet is < 0 or > 133)
        {
            error = "Dane podium są poza zakresem RME: kolory 0–133, addon 0–3, LookType/Mount 0–65535.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private byte[] BuildPodiumOutfit()
    {
        var podium = new byte[15];
        if (PropertyPodiumShowPlatform) podium[0] |= 0x01;
        if (PropertyPodiumShowOutfit) podium[0] |= 0x02;
        if (PropertyPodiumShowMount) podium[0] |= 0x04;
        podium[1] = (byte)PropertyPodiumDirection;
        BitConverter.TryWriteBytes(podium.AsSpan(2, 2), (ushort)PropertyPodiumLookType);
        podium[4] = (byte)PropertyPodiumLookHead;
        podium[5] = (byte)PropertyPodiumLookBody;
        podium[6] = (byte)PropertyPodiumLookLegs;
        podium[7] = (byte)PropertyPodiumLookFeet;
        podium[8] = (byte)PropertyPodiumLookAddon;
        BitConverter.TryWriteBytes(podium.AsSpan(9, 2), (ushort)PropertyPodiumLookMount);
        podium[11] = (byte)PropertyPodiumMountHead;
        podium[12] = (byte)PropertyPodiumMountBody;
        podium[13] = (byte)PropertyPodiumMountLegs;
        podium[14] = (byte)PropertyPodiumMountFeet;
        return podium;
    }

    private static bool IsCanonicalCustomKey(string key) =>
        key is "aid" or "uid" or "text" or "desc" or "tier";

    private static IEnumerable<OtbmItem> EnumerateMapItems(OtbmMap map)
    {
        foreach (var item in map.Tiles.Values.SelectMany(tile => tile.Items))
        foreach (var nested in EnumerateItemTree(item))
            yield return nested;
    }

    private static IEnumerable<OtbmItem> EnumerateItemTree(OtbmItem item)
    {
        yield return item;
        foreach (var child in item.Contents)
        foreach (var nested in EnumerateItemTree(child))
            yield return nested;
    }

    private void ClearPropertyFields()
    {
        PropertyActionId = 0;
        PropertyUniqueId = 0;
        PropertyCount = 0;
        PropertyText = string.Empty;
        PropertyDescription = string.Empty;
        PropertyDepotId = 0;
        PropertyHouseDoorId = 0;
        PropertyTier = 0;
        PropertyRuneCharges = 0;
        PropertyDuration = 0;
        PropertyDecayingState = 0;
        PropertyWrittenDate = 0;
        PropertyWrittenBy = string.Empty;
        PropertySleeperGuid = 0;
        PropertySleepStart = 0;
        PropertyCharges = 0;
        LoadPodiumProperties(null);
        PropertyCustomAttributes.Clear();
        SelectedPropertyCustomAttribute = null;
        PropertyTeleportX = 0;
        PropertyTeleportY = 0;
        PropertyTeleportZ = 0;
    }

    public void SetViewportSize(int columns, int rows)
    {
        var width = Math.Max(8, columns);
        var height = Math.Max(8, rows);
        if (_viewWidth == width && _viewHeight == height) return;
        _viewWidth = width;
        _viewHeight = height;
        OnPropertyChanged(nameof(VisibleColumns));
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        RefreshViewport();
        UpdateMinimapViewportOverlay();
    }

    /// <summary>
    /// Aktualizuje lekką diagnostykę rzeczywistego renderera. Kontrolka raportuje
    /// próbkę najwyżej dwa razy na sekundę, więc licznik nie wpływa zauważalnie na UI.
    /// </summary>
    public void UpdateRenderDiagnostics(double framesPerSecond, double averageRenderMilliseconds, int sampledFrames)
    {
        RenderFramesPerSecond = Math.Max(0, framesPerSecond);
        AverageRenderMilliseconds = Math.Max(0, averageRenderMilliseconds);
        RenderSampleFrames = Math.Max(0, sampledFrames);
        UpdateRenderDiagnosticsLabel();
    }

    private void UpdateRenderDiagnosticsLabel()
    {
        RenderDiagnosticsLabel = string.Create(
            CultureInfo.CurrentCulture,
            $"FPS: {RenderFramesPerSecond:0.0} · render: {AverageRenderMilliseconds:0.00} ms · widok: {ViewportBuildMilliseconds:0.00} ms");
    }

    /// <summary>
    /// Ustawia oba wymiary początku widoku atomowo. Dzięki temu przeciąganie mapy
    /// wykonuje jedno, a nie dwa pełne odświeżenia viewportu na zdarzenie myszy.
    /// </summary>
    public void SetViewOrigin(ushort x, ushort y)
    {
        if (ViewX == x && ViewY == y) return;
        _suppressViewportRefresh = true;
        try
        {
            ViewX = x;
            ViewY = y;
        }
        finally
        {
            _suppressViewportRefresh = false;
        }
        RefreshViewport(reuseTileContent: true);
        UpdateMinimapViewportOverlay();
    }

    private void RefreshViewport(bool updateStatus = true, bool reuseTileContent = false)
    {
        var viewportBuildStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_map is null)
        {
            _viewportTileCache.Clear();
            VisibleTiles = Array.Empty<MapTileItem>();
            VisibleBrushPreview = Array.Empty<MapBrushPreviewTile>();
            ViewportBuildMilliseconds = 0;
            UpdateRenderDiagnosticsLabel();
            return;
        }

        // Podgląd przenoszenia zmienia zawartość i zaznaczenie widocznych pól,
        // więc nie może użyć kafelków zbuforowanych dla poprzedniego offsetu.
        if (IsSelectionMoveActive) reuseTileContent = false;

        var maximumTileSlots = _viewWidth * _viewHeight;
        var visibleTiles = new List<MapTileItem>(Math.Min(maximumTileSlots, _map.Tiles.Count));
        if (!reuseTileContent)
        {
            _viewportTileCache.Clear();
            _lightViewportCache = null;
        }

        CurrentFloorTileCount = _floorTileCounts.GetValueOrDefault(CurrentFloor);
        var visibleCount = 0;
        if (!reuseTileContent) _visibleHasAnimations = false;
        var colorOnly = ShowOnlyColors || ShowAsMinimap;
        var lightSources = ShowLights && !colorOnly
            ? CollectVisibleLights()
            : Array.Empty<MapLightSource>();
        var visibleSpawnZones = ShowSpawns
            ? BuildVisibleSpawnZoneTiles()
            : new HashSet<OtbmTileCoord>();
        var selectionMoveActive = IsSelectionMoveActive;

        // Identyczna kolejnosc jak MapDrawer RME: kolumny X, a w nich wiersze Y.
        // Ma to znaczenie dla wystajacych sprite'ow i elevation na sasiednich polach.
        for (var dx = 0; dx < _viewWidth; dx++)
        for (var dy = 0; dy < _viewHeight; dy++)
        {
            if (ViewX + dx > ushort.MaxValue || ViewY + dy > ushort.MaxValue) continue;
            var x = (ushort)(ViewX + dx);
            var y = (ushort)(ViewY + dy);
            var coord = new OtbmTileCoord(x, y, CurrentFloor);
            if (reuseTileContent && _viewportTileCache.TryGetValue(coord, out var cachedTile))
            {
                cachedTile.CanvasX = dx * TileSize;
                cachedTile.CanvasY = dy * TileSize;
                cachedTile.OutsideClientBox = ShowClientBox &&
                                              (Math.Abs(dx - _viewWidth / 2) > 9 || Math.Abs(dy - _viewHeight / 2) > 7);
                visibleTiles.Add(cachedTile);
                visibleCount++;
                continue;
            }
            _map.Tiles.TryGetValue(coord, out var tile);
            // During drag the real map remains untouched until mouse-up. Render a
            // ghost of the source tile at its destination so the user sees the
            // actual ground and object stack, not only an empty selection rectangle.
            var selectionMoveSourceCoordinate = default(OtbmTileCoord);
            var isSelectionMoveDestination = selectionMoveActive &&
                                             TryGetSelectionMoveSourceCoordinate(
                                                 coord,
                                                 out selectionMoveSourceCoordinate);
            if (isSelectionMoveDestination &&
                _map.Tiles.TryGetValue(selectionMoveSourceCoordinate, out var previewTile))
                tile = previewTile;
            List<(OtbmTileCoord Coordinate, OtbmTile Tile)>? lowerFloorTiles = null;
            if (ShowAllFloors)
            {
                var startZ = CurrentFloor <= 7 ? 7 : Math.Min(15, CurrentFloor + 2);
                for (var lowerZ = startZ; lowerZ > CurrentFloor; lowerZ--)
                {
                    var offset = lowerZ - CurrentFloor;
                    if (x < offset || y < offset) continue;
                    var lowerCoordinate = new OtbmTileCoord(
                        (ushort)(x - offset),
                        (ushort)(y - offset),
                        (byte)lowerZ);
                    if (_map.Tiles.TryGetValue(lowerCoordinate, out var lowerTile))
                        (lowerFloorTiles ??= []).Add((lowerCoordinate, lowerTile));
                }
            }
            OtbmTile? higherFloorTile = null;
            var higherFloorX = x;
            var higherFloorY = y;
            var higherFloorZ = CurrentFloor;
            if (GhostHigherFloors && CurrentFloor is not 0 and not 8)
            {
                var higherZ = (byte)(CurrentFloor - 1);
                const int projectionOffset = 1;
                var sourceX = x + projectionOffset;
                var sourceY = y + projectionOffset;
                if (sourceX <= ushort.MaxValue && sourceY <= ushort.MaxValue)
                {
                    higherFloorX = (ushort)sourceX;
                    higherFloorY = (ushort)sourceY;
                    higherFloorZ = higherZ;
                    _map.Tiles.TryGetValue(new OtbmTileCoord((ushort)sourceX, (ushort)sourceY, higherZ), out higherFloorTile);
                }
            }
            if (ShowOnlyModified && !IsTileModified(coord, tile) &&
                lowerFloorTiles?.Any(candidate => IsTileModified(candidate.Coordinate, candidate.Tile)) != true) continue;
            var isSelected = selectionMoveActive
                ? isSelectionMoveDestination
                : _selectedCoordinates.Contains(coord);
            var hasSpawn = ShowSpawns && _spawnCenters.Contains(coord);
            var hasSpawnZone = ShowSpawns && visibleSpawnZones.Contains(coord);
            IReadOnlyList<OtbmCreature>? creatures = null;
            if (ShowCreatures) _creaturesByTile.TryGetValue(coord, out creatures);
            OtbmHouse? houseExit = null;
            if (ShowHouses) _houseExits.TryGetValue(coord, out houseExit);
            IReadOnlyList<string>? waypoints = null;
            if (ShowWaypoints) _waypointsByTile.TryGetValue(coord, out waypoints);
            IReadOnlyList<string>? towns = null;
            if (ShowTowns) _townsByTile.TryGetValue(coord, out towns);
            if (tile is null && lowerFloorTiles is null && higherFloorTile is null && !hasSpawn && !hasSpawnZone && creatures is null && houseExit is null && waypoints is null &&
                towns is null && !isSelected) continue;
            visibleCount++;

            Bitmap? groundBmp = null;
            var groundTechnicalColor = tile is { GroundItemId: > 0 } && ShowTechnicalItems
                ? _assets.GetTechnicalColor(tile.GroundItemId)
                : null;
            if (!colorOnly && _assets.IsLoaded && tile is { GroundItemId: > 0 } &&
                (ShowTechnicalItems || !_assets.IsTechnicalItem(tile.GroundItemId)) && groundTechnicalColor is null)
                groundBmp = GetMapThumbnail(tile.GroundItemId, x, y, CurrentFloor);

            List<MapTileLayer>? overlays = null;
            if (ShowItems && !colorOnly && _assets.IsLoaded && tile is not null)
            {
                foreach (var item in tile.Items)
                {
                    if (!ShowTechnicalItems && _assets.IsTechnicalItem(item.Id)) continue;
                    (overlays ??= new List<MapTileLayer>(tile.Items.Count)).Add(new MapTileLayer
                    {
                        ItemId = item.Id,
                        RenderMetrics = _assets.GetRenderMetrics(item.Id),
                        Thumbnail = _assets.GetTechnicalColor(item.Id) is null
                            ? GetMapThumbnail(item.Id, x, y, CurrentFloor, item.Count ?? 1)
                            : null,
                        TechnicalColor = ShowTechnicalItems ? _assets.GetTechnicalColor(item.Id) : null,
                        Count = item.Count,
                    });
                }
            }
            List<MapTileLayer>? lowerFloorLayers = null;
            if (!colorOnly && _assets.IsLoaded && lowerFloorTiles is not null)
            {
                foreach (var (lowerCoordinate, lowerTile) in lowerFloorTiles)
                {
                    var startsNewStack = true;
                    if (lowerTile.GroundItemId > 0 &&
                        (ShowTechnicalItems || !_assets.IsTechnicalItem(lowerTile.GroundItemId)))
                    {
                        var technicalColor = ShowTechnicalItems
                            ? _assets.GetTechnicalColor(lowerTile.GroundItemId)
                            : null;
                        (lowerFloorLayers ??= []).Add(new MapTileLayer
                        {
                            ItemId = lowerTile.GroundItemId,
                            RenderMetrics = _assets.GetRenderMetrics(lowerTile.GroundItemId),
                            StartsNewStack = startsNewStack,
                            Thumbnail = technicalColor is null
                                ? GetMapThumbnail(lowerTile.GroundItemId, lowerCoordinate.X, lowerCoordinate.Y, lowerCoordinate.Z)
                                : null,
                            TechnicalColor = technicalColor
                        });
                        startsNewStack = false;
                    }
                    if (!ShowItems) continue;
                    foreach (var item in lowerTile.Items)
                    {
                        if (!ShowTechnicalItems && _assets.IsTechnicalItem(item.Id)) continue;
                        var technicalColor = ShowTechnicalItems ? _assets.GetTechnicalColor(item.Id) : null;
                        (lowerFloorLayers ??= []).Add(new MapTileLayer
                        {
                            ItemId = item.Id,
                            RenderMetrics = _assets.GetRenderMetrics(item.Id),
                            StartsNewStack = startsNewStack,
                            Thumbnail = technicalColor is null
                                ? GetMapThumbnail(item.Id, lowerCoordinate.X, lowerCoordinate.Y, lowerCoordinate.Z, item.Count ?? 1)
                                : null,
                            TechnicalColor = technicalColor,
                            Count = item.Count
                        });
                        startsNewStack = false;
                    }
                }
            }
            List<MapTileLayer>? higherFloorLayers = null;
            if (!colorOnly && _assets.IsLoaded && higherFloorTile is not null)
            {
                var startsNewStack = true;
                if (higherFloorTile.GroundItemId > 0 &&
                    (ShowTechnicalItems || !_assets.IsTechnicalItem(higherFloorTile.GroundItemId)))
                {
                    var technicalColor = ShowTechnicalItems
                        ? _assets.GetTechnicalColor(higherFloorTile.GroundItemId)
                        : null;
                    (higherFloorLayers ??= []).Add(new MapTileLayer
                    {
                        ItemId = higherFloorTile.GroundItemId,
                        RenderMetrics = _assets.GetRenderMetrics(higherFloorTile.GroundItemId),
                        StartsNewStack = startsNewStack,
                        Thumbnail = technicalColor is null
                            ? GetMapThumbnail(higherFloorTile.GroundItemId, higherFloorX, higherFloorY, higherFloorZ)
                            : null,
                        TechnicalColor = technicalColor
                    });
                    startsNewStack = false;
                }
                if (ShowItems)
                {
                    foreach (var item in higherFloorTile.Items)
                    {
                        if (!ShowTechnicalItems && _assets.IsTechnicalItem(item.Id)) continue;
                        var technicalColor = ShowTechnicalItems ? _assets.GetTechnicalColor(item.Id) : null;
                        (higherFloorLayers ??= []).Add(new MapTileLayer
                        {
                            ItemId = item.Id,
                            RenderMetrics = _assets.GetRenderMetrics(item.Id),
                            StartsNewStack = startsNewStack,
                            Thumbnail = technicalColor is null
                                ? GetMapThumbnail(item.Id, higherFloorX, higherFloorY, higherFloorZ, item.Count ?? 1)
                                : null,
                            TechnicalColor = technicalColor,
                            Count = item.Count
                        });
                        startsNewStack = false;
                    }
                }
            }
            List<MapTileLayer>? creatureLayers = null;
            if (ShowCreatures && TileSize >= 16 && !colorOnly && _assets.IsLoaded && creatures is not null)
            {
                foreach (var creature in creatures)
                {
                    if (!_creatureDefinitions.TryGetValue(creature.Name, out var definition)) continue;
                    if (_assets.GetCreatureAnimationFrameCount(definition.LookType, definition.LookItem) > 1)
                        _visibleHasAnimations = true;
                    (creatureLayers ??= []).Add(new MapTileLayer
                    {
                        Thumbnail = _assets.GetCreatureThumbnail(
                            definition.LookType,
                            definition.LookItem,
                            creature.Direction,
                            ShowAnimation ? _animationFrame : 0),
                        RenderMetrics = definition.LookItem == 0
                            ? default
                            : _assets.GetClientObjectRenderMetrics(definition.LookItem)
                    });
                }
            }
            List<MapTileLayer>? markerLayers = null;
            if (TileSize >= 16 && !colorOnly && _assets.IsLoaded)
            {
                if (waypoints is not null) AddEditorMarker(1397);
                if (houseExit is not null) AddEditorMarker(2019);
                if (towns is not null) AddEditorMarker(1436);
                // Spawn ma własny proceduralny obrys i krzyżyk w rendererze.
                // Stały Client ID 1507 z RME nie jest przenośny między klientami
                // i w niestandardowych assets potrafił wyświetlać ścianę.
            }

            void AddEditorMarker(uint clientId)
            {
                (markerLayers ??= new List<MapTileLayer>(4)).Add(new MapTileLayer
                {
                    ItemId = (ushort)clientId,
                    Thumbnail = _assets.GetClientObjectThumbnail(clientId),
                    RenderMetrics = _assets.GetClientObjectRenderMetrics(clientId)
                });
            }
            ushort minimapColor = 0;
            if (ShowAsMinimap)
            {
                var displayTile = tile ?? (lowerFloorTiles is { Count: > 0 } ? lowerFloorTiles[^1].Tile : null);
                minimapColor = displayTile is null ? (ushort)0 : displayTile.Items.AsEnumerable().Reverse()
                    .Select(item => _assets.GetMinimapColor(item.Id))
                    .FirstOrDefault(color => color is > 0 and < 216);
                if (minimapColor == 0 && displayTile is not null)
                    minimapColor = _assets.GetMinimapColor(displayTile.GroundItemId);
            }
            var isBlocking = ShowBlocking && tile is not null &&
                             (_assets.IsUnpassable(tile.GroundItemId) || tile.Items.Any(item => _assets.IsUnpassable(item.Id)));
            var hasWallHook = ShowWallHooks && tile is not null && tile.Items.Any(item => _assets.HasWallHook(item.Id));
            var light = ShowLights && tile is not null
                ? tile.Items.AsEnumerable().Reverse().Select(item => _assets.GetLight(item.Id))
                    .FirstOrDefault(candidate => candidate.HasValue) ?? _assets.GetLight(tile.GroundItemId)
                : null;
            var highlightsItem = HighlightItems && tile is { Items.Count: > 0 };
            var highlightsDoor = HighlightLockedDoors && tile is not null && tile.Items.Any(item =>
                item.HouseDoorId.HasValue || item.ActionId.HasValue &&
                (_assets.GetObjectName(item.Id)?.Contains("door", StringComparison.CurrentCultureIgnoreCase) ?? false));
            var outsideClientBox = ShowClientBox &&
                                   (Math.Abs(dx - _viewWidth / 2) > 9 || Math.Abs(dy - _viewHeight / 2) > 7);
            var showZoneOverlay = (ShowSpecialTiles || ShowOnlyColors) && tile is not null &&
                                  (tile.GroundItemId != 0 || AlwaysShowZones);

            var renderedTile = new MapTileItem
            {
                X = x,
                Y = y,
                Z = CurrentFloor,
                CanvasX = dx * TileSize,
                CanvasY = dy * TileSize,
                TileSize = TileSize,
                GroundItemId = tile?.GroundItemId ?? 0,
                ItemCount = tile?.Items.Count ?? 0,
                Thumbnail = groundBmp,
                GroundRenderMetrics = tile is { GroundItemId: > 0 }
                    ? _assets.GetRenderMetrics(tile.GroundItemId)
                    : default,
                Overlays = overlays is null ? Array.Empty<MapTileLayer>() : overlays,
                LowerFloorLayers = lowerFloorLayers is null ? Array.Empty<MapTileLayer>() : lowerFloorLayers,
                HigherFloorLayers = higherFloorLayers is null ? Array.Empty<MapTileLayer>() : higherFloorLayers,
                CreatureLayers = creatureLayers is null ? Array.Empty<MapTileLayer>() : creatureLayers,
                MarkerLayers = markerLayers is null ? Array.Empty<MapTileLayer>() : markerLayers,
                IsHouseTile = ShowHouses && tile?.IsHouseTile == true,
                IsHouseGroundOverlay = ShowHouses && tile?.IsHouseTile == true && !ExtendedHouseShader,
                IsHouseExtendedOverlay = ShowHouses && tile?.IsHouseTile == true && ExtendedHouseShader,
                HouseId = tile?.HouseId ?? 0,
                ItemListLabel = tile is { Items.Count: > 0 } ? $"+{tile.Items.Count}" : string.Empty,
                HasSpawn = hasSpawn,
                IsSpawnZone = hasSpawnZone,
                CreatureNames = creatures is null ? string.Empty : string.Join(", ", creatures.Select(creature => creature.Name)),
                NpcCount = creatures?.Count(creature => creature.IsNpc) ?? 0,
                MonsterCount = creatures?.Count(creature => !creature.IsNpc) ?? 0,
                HouseExitName = houseExit?.Name ?? string.Empty,
                WaypointNames = waypoints is null ? string.Empty : string.Join(", ", waypoints),
                TownNames = towns is null ? string.Empty : string.Join(", ", towns),
                GridThickness = ShowGrid ? 0.5 : 0,
                IsProtectionZone = showZoneOverlay && (tile!.Flags & 0x01) != 0,
                IsNoPvpZone = showZoneOverlay && (tile!.Flags & 0x04) != 0,
                IsNoLogoutZone = showZoneOverlay && (tile!.Flags & 0x08) != 0,
                IsPvpZone = showZoneOverlay && (tile!.Flags & 0x10) != 0,
                IsSelected = isSelected,
                IsPreview = false,
                IsBorderPreview = false,
                IsBlocking = isBlocking,
                ShowSprites = !colorOnly,
                TileBackground = ShowAsMinimap
                    ? minimapColor is > 0 and < 216 ? MinimapHex(minimapColor) : "#000000"
                    : ShowOnlyColors ? "#0F172A" : groundTechnicalColor ?? "#0F172A",
                LowerFloorShadeOpacity = ShowShade && lowerFloorLayers is { Count: > 0 } ? 0.5 : 0,
                TooltipsEnabled = ShowTooltips,
                HasWallHook = hasWallHook,
                LightLabel = light is { } lightValue && ShowLightStrength ? $"L{lightValue.Level}" : string.Empty,
                LightOverlayColor = CalculateLightOverlay(x, y, lightSources),
                FogOpacity = ShowLights && ExperimentalFog && !colorOnly ? 0.31 : 0,
                HighlightItem = highlightsItem,
                HighlightLockedDoor = highlightsDoor,
                ItemOpacity = GhostLooseItems ? 0.35 : 1,
                OutsideClientBox = outsideClientBox
            };
            visibleTiles.Add(renderedTile);
            _viewportTileCache[coord] = renderedTile;
        }

        VisibleTiles = visibleTiles;
        // Zachowujemy niewielki margines kafelków poza ekranem, aby szybki powrót
        // strzałką nie wymagał dekodowania sprite'ów. Limit zapobiega wzrostowi cache
        // podczas długiego przemieszczania się po mapie.
        if (_viewportTileCache.Count > Math.Max(1024, maximumTileSlots * 2))
        {
            _viewportTileCache = visibleTiles.ToDictionary(
                tile => new OtbmTileCoord(tile.X, tile.Y, tile.Z),
                tile => tile);
        }
        RefreshBrushPreviewOverlay();
        ViewportBuildMilliseconds = System.Diagnostics.Stopwatch
            .GetElapsedTime(viewportBuildStarted)
            .TotalMilliseconds;
        UpdateRenderDiagnosticsLabel();
        if (updateStatus)
            Status = $"Floor Z={CurrentFloor}: {CurrentFloorTileCount} tile, widocznych: {visibleCount}.";
    }

    private HashSet<OtbmTileCoord> BuildVisibleSpawnZoneTiles()
    {
        var result = new HashSet<OtbmTileCoord>();
        if (!_spawnsByFloor.TryGetValue(CurrentFloor, out var spawns)) return result;
        var visibleMaxX = Math.Min(ushort.MaxValue, (long)ViewX + _viewWidth - 1);
        var visibleMaxY = Math.Min(ushort.MaxValue, (long)ViewY + _viewHeight - 1);
        foreach (var spawn in spawns)
        {
            var radius = Math.Max(0, spawn.Radius);
            var minX = Math.Max(ViewX, (int)spawn.CenterX - radius);
            var maxX = Math.Min(visibleMaxX, (long)spawn.CenterX + radius);
            var minY = Math.Max(ViewY, (int)spawn.CenterY - radius);
            var maxY = Math.Min(visibleMaxY, (long)spawn.CenterY + radius);
            if (minX > maxX || minY > maxY) continue;
            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
                result.Add(new OtbmTileCoord((ushort)x, (ushort)y, CurrentFloor));
        }
        return result;
    }

    private Bitmap? GetMapThumbnail(uint itemId, int x, int y, int z, int subtype = -1)
    {
        if (ShowAnimation && _assets.GetAnimationFrameCount(itemId) > 1) _visibleHasAnimations = true;
        return _assets.GetThumbnail(itemId, ShowAnimation ? _animationFrame : 0, x, y, z, subtype);
    }

    private MapLightSource[] CollectVisibleLights()
    {
        if (_map is null || !_assets.IsLoaded) return [];
        const int radius = 8;
        const int panMargin = 16;
        var requiredMinX = Math.Max(0, ViewX - radius);
        var requiredMinY = Math.Max(0, ViewY - radius);
        var requiredMaxX = Math.Min(ushort.MaxValue, ViewX + _viewWidth + radius);
        var requiredMaxY = Math.Min(ushort.MaxValue, ViewY + _viewHeight + radius);
        var lastFloor = ShowAllFloors
            ? CurrentFloor <= 7 ? 7 : Math.Min(15, CurrentFloor + 2)
            : CurrentFloor;
        if (_lightViewportCache is { } cached &&
            cached.Floor == CurrentFloor && cached.LastFloor == lastFloor &&
            requiredMinX >= cached.MinX && requiredMinY >= cached.MinY &&
            requiredMaxX <= cached.MaxX && requiredMaxY <= cached.MaxY)
            return cached.Sources;

        // Bufor obejmuje dodatkowy margines panowania. Dopóki użytkownik porusza się
        // wewnątrz niego, strzałki nie skanują ponownie dziesiątek tysięcy współrzędnych.
        var minX = Math.Max(0, requiredMinX - panMargin);
        var minY = Math.Max(0, requiredMinY - panMargin);
        var maxX = Math.Min(ushort.MaxValue, requiredMaxX + panMargin);
        var maxY = Math.Min(ushort.MaxValue, requiredMaxY + panMargin);
        var merged = new Dictionary<(ushort X, ushort Y, ushort Color), ushort>();

        // Skanuj tylko prostokąt widoczny z marginesem światła. Poprzednia wersja
        // przechodziła po całej mapie (np. 1,4 mln tile) przy każdym pan/hover.
        for (var z = CurrentFloor; z <= lastFloor; z++)
        {
            var floorOffset = z - CurrentFloor;
            var sourceMinX = Math.Max(0, minX - floorOffset);
            var sourceMinY = Math.Max(0, minY - floorOffset);
            var sourceMaxX = Math.Min(ushort.MaxValue, maxX - floorOffset);
            var sourceMaxY = Math.Min(ushort.MaxValue, maxY - floorOffset);
            if (sourceMinX > sourceMaxX || sourceMinY > sourceMaxY) continue;

            for (var sourceY = sourceMinY; sourceY <= sourceMaxY; sourceY++)
            for (var sourceX = sourceMinX; sourceX <= sourceMaxX; sourceX++)
            {
                var coord = new OtbmTileCoord((ushort)sourceX, (ushort)sourceY, (byte)z);
                if (!_map.Tiles.TryGetValue(coord, out var tile)) continue;
                var projectedX = (ushort)(sourceX + floorOffset);
                var projectedY = (ushort)(sourceY + floorOffset);
                AddLight(projectedX, projectedY, _assets.GetLight(tile.GroundItemId));
                foreach (var item in tile.Items)
                    AddLight(projectedX, projectedY, _assets.GetLight(item.Id));
            }
        }

        var result = merged.Select(pair => new MapLightSource(
            pair.Key.X,
            pair.Key.Y,
            pair.Value,
            pair.Key.Color)).ToArray();
        if (_lightViewportCache is { } previous)
            InvalidateChangedLightInfluence(previous.Sources, result);
        _lightViewportCache = new MapLightViewportCache(
            CurrentFloor,
            (byte)lastFloor,
            minX,
            minY,
            maxX,
            maxY,
            result);
        return result;

        void AddLight(ushort x, ushort y, (ushort Level, ushort Color)? light)
        {
            if (light is not { Level: > 0 } value) return;
            var key = (x, y, value.Color);
            merged[key] = Math.Max(merged.GetValueOrDefault(key), (ushort)Math.Min(8, (int)value.Level));
        }
    }

    private void InvalidateChangedLightInfluence(
        IReadOnlyList<MapLightSource> previous,
        IReadOnlyList<MapLightSource> current)
    {
        if (_viewportTileCache.Count == 0) return;
        var oldLevels = previous.ToDictionary(
            light => (light.X, light.Y, light.Color),
            light => light.Level);
        var newLevels = current.ToDictionary(
            light => (light.X, light.Y, light.Color),
            light => light.Level);
        var changedSources = oldLevels.Keys
            .Concat(newLevels.Keys)
            .Distinct()
            .Where(key => oldLevels.GetValueOrDefault(key) != newLevels.GetValueOrDefault(key));
        var invalidated = new HashSet<OtbmTileCoord>();
        foreach (var source in changedSources)
        for (var offsetX = -8; offsetX <= 8; offsetX++)
        for (var offsetY = -8; offsetY <= 8; offsetY++)
        {
            var x = source.X + offsetX;
            var y = source.Y + offsetY;
            if (x is < 0 or > ushort.MaxValue || y is < 0 or > ushort.MaxValue) continue;
            invalidated.Add(new OtbmTileCoord((ushort)x, (ushort)y, CurrentFloor));
        }

        foreach (var coordinate in invalidated)
            _viewportTileCache.Remove(coordinate);
    }

    private string CalculateLightOverlay(ushort x, ushort y, IReadOnlyList<MapLightSource> lights)
    {
        if (!ShowLights || ShowOnlyColors || ShowAsMinimap) return "#00000000";
        var red = 50;
        var green = 50;
        var blue = 50;
        foreach (var light in lights)
        {
            var dx = x - light.X;
            var dy = y - light.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            var intensity = Math.Clamp((-distance + light.Level) * 0.2, 0, 1);
            if (intensity < 0.01) continue;
            var color = EightBitColor(light.Color);
            red = Math.Max(red, (int)(color.Red * intensity));
            green = Math.Max(green, (int)(color.Green * intensity));
            blue = Math.Max(blue, (int)(color.Blue * intensity));
        }
        return $"#8C{red:X2}{green:X2}{blue:X2}";
    }

    private static (int Red, int Green, int Blue) EightBitColor(ushort color) =>
        color is > 0 and < 216
            ? ((color / 36 % 6) * 51, (color / 6 % 6) * 51, color % 6 * 51)
            : (0, 0, 0);

    private void BuildPalette()
    {
        Tilesets.Clear();
        ClearCategoryPalettes();
        _materials = null;
        _groundBorders = null;
        _connectedBrushes = null;
        _importedCreatureNames.Clear();
        _creatureDefinitions = new Dictionary<string, RmeCreatureDefinition>(StringComparer.OrdinalIgnoreCase);
        if (!_assets.IsLoaded)
        {
            RebuildPaletteNavigationGroups();
            return;
        }

        if (_assets.ItemsOtbPath is { } itemsOtbPath &&
            File.Exists(Path.Combine(Path.GetDirectoryName(itemsOtbPath)!, "materials.xml")))
        {
            try
            {
                _materials = _materialLoader.Load(itemsOtbPath);
                _groundBorders = new RmeGroundBorderService(_materials);
                _connectedBrushes = new RmeConnectedBrushService(_materials);
                string? creatureWarning = null;
                try
                {
                    _creatureDefinitions = _creatureLoader.Load(_materials.VersionDirectory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
                {
                    creatureWarning = $" creatures.xml: {ex.Message}";
                }
                foreach (var tileset in _materials.Tilesets)
                    Tilesets.Add(tileset);
                MaterialStatus = $"RME: {Tilesets.Count} tilesetów, {_materials.Brushes.Count} brushy, " +
                                 $"{_materials.Borders.Count} obramowań, {_creatureDefinitions.Count} stworzeń " +
                                 $"({Path.GetFileName(_materials.VersionDirectory)})." +
                                 (_materials.Warnings.Count > 0 ? $" Ostrzeżenia: {_materials.Warnings.Count}." : string.Empty) +
                                 creatureWarning;
                SelectedTileset = Tilesets.FirstOrDefault(tileset =>
                                      tileset.Name.Equals("Nature", StringComparison.OrdinalIgnoreCase))
                                  ?? Tilesets.FirstOrDefault();
                RebuildPaletteNavigationGroups();
                return;
            }
            catch (Exception ex)
            {
                MaterialStatus = $"Nie wczytano palet RME: {ex.Message}";
            }
        }
        else
        {
            MaterialStatus = "Brak zgodnego materials.xml — użyto awaryjnej palety obiektów klienta.";
        }

        BuildFallbackPalette();
        RebuildPaletteNavigationGroups();
    }

    private void BuildSelectedTilesetPalettes()
    {
        ClearCategoryPalettes();
        if (_showRawOthers)
        {
            BuildRawOthersPalette();
            RebuildMapEntityPalettes();
            UpdatePalettePresentation();
            return;
        }
        if (SelectedTileset is null)
        {
            if (_assets.IsLoaded) BuildFallbackPalette();
            RebuildMapEntityPalettes();
            UpdatePalettePresentation();
            return;
        }

        AddCategory(SelectedTileset, RmePaletteCategory.Terrain, TerrainPalette);
        AddCategory(SelectedTileset, RmePaletteCategory.Doodad, DoodadPalette);
        AddCategory(SelectedTileset, RmePaletteCategory.Item, ItemPalette);
        AddCategory(SelectedTileset, RmePaletteCategory.Raw, RawPalette);
        AddCategory(SelectedTileset, RmePaletteCategory.Collection, CollectionPalette);
        AddCategory(SelectedTileset, RmePaletteCategory.Creature, CreaturePalette);
        AddCategory(SelectedTileset, RmePaletteCategory.House, HousePalette);
        AddCategory(SelectedTileset, RmePaletteCategory.Waypoint, WaypointPalette);
        RebuildMapEntityPalettes();
        UpdatePalettePresentation();
    }

    private void BuildRawOthersPalette()
    {
        var assignedIds = (_materials?.Tilesets ?? Tilesets)
            .SelectMany(tileset => tileset.Categories.TryGetValue(RmePaletteCategory.Raw, out var entries)
                ? entries
                : Array.Empty<RmePaletteEntry>())
            .SelectMany(entry => entry.ItemId is { } itemId
                ? new uint[] { itemId }
                : entry.Brush is { } brush
                    ? brush.AllItemIds.Select(id => (uint)id)
                    : Array.Empty<uint>())
            .ToHashSet();
        foreach (var id in _assets.ObjectIds.OrderBy(id => id))
        {
            if (assignedIds.Contains(id)) continue;
            var name = _assets.GetObjectName(id) ?? string.Empty;
            if (!MatchesPaletteSearch(name, id)) continue;
            RawPalette.Add(new MapPaletteItem
            {
                ItemId = id,
                Name = name,
                Category = RmePaletteCategory.Raw,
                ThumbnailLoader = () => _assets.GetThumbnail(id)
            });
        }
    }

    private void AddCategory(
        RmeTilesetDefinition tileset,
        RmePaletteCategory category,
        ObservableCollection<MapPaletteItem> target)
    {
        if (category == RmePaletteCategory.Creature && CreatureFilter != "NPC" &&
            MatchesPaletteSearch("Spawn Brush", 0))
        {
            target.Add(new MapPaletteItem
            {
                Name = "Spawn Brush",
                EntityName = "Spawn Brush",
                EntityKind = MapPaletteEntityKind.Spawn,
                Category = category
            });
        }
        if (!tileset.Categories.TryGetValue(category, out var entries)) return;
        foreach (var entry in entries)
        {
            if (entry.EntityName is { } entityName)
            {
                if (!MatchesPaletteSearch(entityName, 0)) continue;
                _creatureDefinitions.TryGetValue(entityName, out var creature);
                if (!MatchesCreatureFilter(creature?.IsNpc == true)) continue;
                target.Add(new MapPaletteItem
                {
                    Name = entityName,
                    EntityName = entityName,
                    EntityKind = MapPaletteEntityKind.Creature,
                    CreatureIsNpc = creature?.IsNpc == true,
                    Category = category,
                    ThumbnailLoader = creature is null
                        ? null
                        : () => _assets.GetCreatureThumbnail(creature.LookType, creature.LookItem)
                });
                continue;
            }

            var id = (uint)(entry.ItemId ?? entry.Brush?.PreviewItemId ?? 0);
            if (id == 0) continue;
            var name = entry.Brush?.Name ?? _assets.GetObjectName(id) ?? string.Empty;
            if (!MatchesPaletteSearch(name, id)) continue;
            target.Add(new MapPaletteItem
            {
                ItemId = id,
                Name = name,
                Category = category,
                Brush = entry.Brush,
                ThumbnailLoader = () => _assets.GetThumbnail(id)
            });
        }
    }

    private void BuildFallbackPalette()
    {
        foreach (var id in _assets.ObjectIds.OrderBy(id => id))
        {
            var name = _assets.GetObjectName(id) ?? string.Empty;
            if (!MatchesPaletteSearch(name, id)) continue;
            var item = new MapPaletteItem
            {
                ItemId = id,
                Name = name,
                Category = RmePaletteCategory.Raw,
                ThumbnailLoader = () => _assets.GetThumbnail(id)
            };
            RawPalette.Add(item);
            if (_assets.IsGround(id))
                TerrainPalette.Add(CloneForCategory(item, RmePaletteCategory.Terrain));
            else if (_assets.IsPickupable(id) || _assets.IsContainer(id))
                ItemPalette.Add(CloneForCategory(item, RmePaletteCategory.Item));
            else
                DoodadPalette.Add(CloneForCategory(item, RmePaletteCategory.Doodad));
        }
    }

    private void RebuildMapEntityPalettes()
    {
        HousePalette.Clear();
        WaypointPalette.Clear();
        var displayedCreatures = CreaturePalette
            .Where(item => item.EntityKind == MapPaletteEntityKind.Creature)
            .Select(item => item.EntityName ?? item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mapCreatures = _map?.Spawns.SelectMany(spawn => spawn.Creatures)
            .GroupBy(creature => creature.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, OtbmCreature>(StringComparer.OrdinalIgnoreCase);
        var creatureNames = _importedCreatureNames.Concat(mapCreatures.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var name in creatureNames)
        {
            if (displayedCreatures.Contains(name) || !MatchesPaletteSearch(name, 0)) continue;
            _creatureDefinitions.TryGetValue(name, out var creature);
            var isNpc = creature?.IsNpc ?? mapCreatures.GetValueOrDefault(name)?.IsNpc == true;
            if (!MatchesCreatureFilter(isNpc)) continue;
            CreaturePalette.Add(new MapPaletteItem
            {
                Name = name,
                EntityName = name,
                EntityKind = MapPaletteEntityKind.Creature,
                CreatureIsNpc = isNpc,
                Category = RmePaletteCategory.Creature,
                ThumbnailLoader = creature is null
                    ? null
                    : () => _assets.GetCreatureThumbnail(creature.LookType, creature.LookItem)
            });
        }
        if (_map is null) return;

        foreach (var house in _map.Houses.OrderBy(house => house.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (SelectedPaletteSection == "House Palette" && !HouseMatchesNavigationFilter(house)) continue;
            if (!MatchesPaletteSearch(house.Name, house.Id)) continue;
            HousePalette.Add(new MapPaletteItem
            {
                Name = $"#{house.Id} {house.Name}",
                Category = RmePaletteCategory.House,
                EntityKind = MapPaletteEntityKind.House,
                EntityId = house.Id
            });
        }

        for (var index = 0; index < _map.Waypoints.Count; index++)
        {
            var waypoint = _map.Waypoints[index];
            if (!MatchesPaletteSearch(waypoint.Name, 0)) continue;
            WaypointPalette.Add(new MapPaletteItem
            {
                Name = waypoint.Name,
                Category = RmePaletteCategory.Waypoint,
                EntityKind = MapPaletteEntityKind.Waypoint,
                EntityIndex = index
            });
        }
    }

    private bool MatchesPaletteSearch(string name, uint id)
    {
        var query = PaletteSearch.Trim();
        return query.Length == 0 ||
               name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               (id != 0 && id.ToString().Contains(query, StringComparison.Ordinal));
    }

    private bool MatchesCreatureFilter(bool isNpc) => CreatureFilter switch
    {
        "Potwory" => !isNpc,
        "NPC" => isNpc,
        _ => true
    };

    private void UpdatePalettePresentation()
    {
        foreach (var propertyName in new[]
                 {
                     nameof(TerrainPaletteHeader), nameof(DoodadPaletteHeader), nameof(ItemPaletteHeader),
                     nameof(RawPaletteHeader), nameof(CollectionPaletteHeader), nameof(CreaturePaletteHeader),
                     nameof(IsTerrainPaletteEmpty), nameof(IsDoodadPaletteEmpty), nameof(IsItemPaletteEmpty),
                     nameof(IsRawPaletteEmpty), nameof(IsCollectionPaletteEmpty), nameof(IsCreaturePaletteEmpty),
                     nameof(PaletteEmptyMessage)
                 })
            OnPropertyChanged(propertyName);
    }

    private static MapPaletteItem CloneForCategory(MapPaletteItem item, RmePaletteCategory category) => new()
    {
        ItemId = item.ItemId,
        Name = item.Name,
        Category = category,
        ThumbnailLoader = item.ThumbnailLoader
    };

    private void ClearCategoryPalettes()
    {
        TerrainPalette.Clear();
        DoodadPalette.Clear();
        ItemPalette.Clear();
        RawPalette.Clear();
        CollectionPalette.Clear();
        CreaturePalette.Clear();
        HousePalette.Clear();
        WaypointPalette.Clear();
    }

    private void IndexExternalMapData(OtbmMap map)
    {
        _spawnCenters.Clear();
        _spawnsByFloor.Clear();
        _creaturesByTile.Clear();
        _houseExits.Clear();
        _waypointsByTile.Clear();
        _townsByTile.Clear();

        foreach (var spawn in map.Spawns)
        {
            _spawnCenters.Add(new(spawn.CenterX, spawn.CenterY, spawn.CenterZ));
        }

        foreach (var floor in map.Spawns.GroupBy(spawn => spawn.CenterZ))
            _spawnsByFloor[floor.Key] = floor.ToArray();

        foreach (var group in map.Spawns.SelectMany(spawn => spawn.Creatures)
                     .GroupBy(creature => new OtbmTileCoord(creature.X, creature.Y, creature.Z)))
        {
            _creaturesByTile[group.Key] = group.ToArray();
        }

        foreach (var house in map.Houses.Where(house => house.EntryX != 0 && house.EntryY != 0))
        {
            _houseExits[new(house.EntryX, house.EntryY, house.EntryZ)] = house;
        }

        foreach (var group in map.Waypoints.GroupBy(waypoint =>
                     new OtbmTileCoord(waypoint.X, waypoint.Y, waypoint.Z)))
        {
            _waypointsByTile[group.Key] = group.Select(waypoint => waypoint.Name).ToArray();
        }

        foreach (var group in map.Towns.GroupBy(town =>
                     new OtbmTileCoord(town.TempleX, town.TempleY, town.TempleZ)))
        {
            _townsByTile[group.Key] = group.Select(town => town.Name).ToArray();
        }
    }

    private static void SaveAtomically(OtbmMap map, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new InvalidOperationException("Nieprawidłowa ścieżka zapisu mapy.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            new OtbmWriter().Write(map, temporary);
            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, fullPath + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static int CopyExternalMapFiles(OtbmMap map, string sourceMapPath, string destinationMapPath)
    {
        if (string.IsNullOrWhiteSpace(sourceMapPath) || !File.Exists(sourceMapPath)) return 0;
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceMapPath));
        var destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationMapPath));
        if (sourceDirectory is null || destinationDirectory is null ||
            string.Equals(sourceDirectory, destinationDirectory, StringComparison.OrdinalIgnoreCase)) return 0;

        var copied = 0;
        foreach (var relativeName in new[] { map.SpawnFile, map.HouseFile, map.SpawnNpcFile })
        {
            if (string.IsNullOrWhiteSpace(relativeName) || Path.IsPathRooted(relativeName)) continue;
            var source = Path.GetFullPath(Path.Combine(sourceDirectory, relativeName));
            if (!File.Exists(source)) continue;
            var destination = Path.GetFullPath(Path.Combine(destinationDirectory, relativeName));
            var destinationRoot = Path.TrimEndingDirectorySeparator(destinationDirectory) + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            CopyFileAtomically(source, destination);
            copied++;
        }
        return copied;
    }

    private static void CopyFileAtomically(string source, string destination)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, temporary, overwrite: true);
            if (File.Exists(destination))
                File.Replace(temporary, destination, destination + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loadCancellation?.Cancel();
        var minimapCancellation = Interlocked.Exchange(ref _minimapCancellation, null);
        minimapCancellation?.Cancel();
        minimapCancellation?.Dispose();
        SwapMinimapImage(null);
        _assets.Dispose();
        _retiredAssets.Dispose();
    }
}

public sealed class MapTileItem
{
    public ushort X { get; init; }
    public ushort Y { get; init; }
    public byte Z { get; init; }
    public int CanvasX { get; set; }
    public int CanvasY { get; set; }
    public int TileSize { get; init; }
    public ushort GroundItemId { get; init; }
    public int ItemCount { get; init; }
    /// <summary>Ground sprite (warstwa 0) — pierwszy renderowany.</summary>
    public Bitmap? Thumbnail { get; init; }
    public MapSpriteRenderMetrics GroundRenderMetrics { get; init; }
    /// <summary>Sprite items na stosie nad ground, w kolejności OTBM (najwyższy ostatni).</summary>
    public IReadOnlyList<MapTileLayer> Overlays { get; init; } = Array.Empty<MapTileLayer>();
    /// <summary>Niższe poziomy Z renderowane od najgłębszego do najbliższego.</summary>
    public IReadOnlyList<MapTileLayer> LowerFloorLayers { get; init; } = Array.Empty<MapTileLayer>();
    /// <summary>Natychmiastowe wyższe piętro, rzutowane i półprzezroczyste jak w RME.</summary>
    public IReadOnlyList<MapTileLayer> HigherFloorLayers { get; init; } = Array.Empty<MapTileLayer>();
    /// <summary>Grafiki NPC/potworów odczytane z katalogu creatures RME.</summary>
    public IReadOnlyList<MapTileLayer> CreatureLayers { get; init; } = Array.Empty<MapTileLayer>();
    /// <summary>Techniczne markery RME: waypoint, house exit i town. Spawn ma przenośny obrys proceduralny.</summary>
    public IReadOnlyList<MapTileLayer> MarkerLayers { get; init; } = Array.Empty<MapTileLayer>();
    public bool IsHouseTile { get; init; }
    public bool IsHouseGroundOverlay { get; init; }
    public bool IsHouseExtendedOverlay { get; init; }
    public uint HouseId { get; init; }
    public string ItemListLabel { get; init; } = string.Empty;
    public bool HasSpawn { get; init; }
    public bool IsSpawnZone { get; init; }
    public int NpcCount { get; init; }
    public int MonsterCount { get; init; }
    public string CreatureNames { get; init; } = string.Empty;
    public string HouseExitName { get; init; } = string.Empty;
    public string WaypointNames { get; init; } = string.Empty;
    public string TownNames { get; init; } = string.Empty;
    public double GridThickness { get; init; }
    public bool IsProtectionZone { get; init; }
    public bool IsNoPvpZone { get; init; }
    public bool IsNoLogoutZone { get; init; }
    public bool IsPvpZone { get; init; }
    public bool IsSelected { get; init; }
    public bool IsPreview { get; init; }
    public bool IsBorderPreview { get; init; }
    public bool IsBlocking { get; init; }
    public bool ShowSprites { get; init; } = true;
    public string TileBackground { get; init; } = "#0F172A";
    public double LowerFloorShadeOpacity { get; init; }
    public bool TooltipsEnabled { get; init; } = true;
    public bool HasWallHook { get; init; }
    public string LightLabel { get; init; } = string.Empty;
    public string LightOverlayColor { get; init; } = "#00000000";
    public double FogOpacity { get; init; }
    public bool HighlightItem { get; init; }
    public bool HighlightLockedDoor { get; init; }
    public double ItemOpacity { get; init; } = 1;
    public bool OutsideClientBox { get; set; }
    public string MarkerLabel =>
        (HasSpawn ? "S" : string.Empty) +
        (NpcCount > 0 ? "N" : string.Empty) +
        (MonsterCount > 0 ? "M" : string.Empty) +
        (!string.IsNullOrEmpty(HouseExitName) ? "H" : string.Empty) +
        (!string.IsNullOrEmpty(WaypointNames) ? "W" : string.Empty) +
        (!string.IsNullOrEmpty(TownNames) ? "T" : string.Empty);
    public string Tooltip => !TooltipsEnabled ? string.Empty : $"({X}, {Y}, {Z}) ground=#{GroundItemId}, items={ItemCount}" +
                             (IsHouseTile ? $", dom={HouseId}" : string.Empty) +
                             (IsSpawnZone ? ", obszar spawnu" : string.Empty) +
                             (HasSpawn ? ", centrum spawnu" : string.Empty) +
                             (!string.IsNullOrEmpty(CreatureNames) ? $", stworzenia: {CreatureNames}" : string.Empty) +
                             (!string.IsNullOrEmpty(HouseExitName) ? $", wyjście domu: {HouseExitName}" : string.Empty) +
                             (!string.IsNullOrEmpty(WaypointNames) ? $", waypoint: {WaypointNames}" : string.Empty) +
                             (!string.IsNullOrEmpty(TownNames) ? $", town: {TownNames}" : string.Empty);
}

public readonly record struct MapBrushPreviewTile(int CanvasX, int CanvasY, int TileSize, bool IsBorder);

internal readonly record struct MapBrushPreviewGeometryKey(
    MapPaletteItem Brush,
    int Variation,
    int Size,
    string Shape,
    bool Automagic);

internal readonly record struct MapBrushPreviewOffset(int X, int Y, int Z, bool IsBorder);

public sealed class MapTilePropertyItem
{
    public required string Label { get; init; }
    public required OtbmItem Item { get; init; }
}

public partial class MapCustomAttributeItem : ObservableObject
{
    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _type = "String";
    [ObservableProperty] private string _value = string.Empty;

    public static MapCustomAttributeItem FromModel(OtbmCustomAttribute attribute) => new()
    {
        Key = attribute.Key,
        Type = attribute.Type.ToString(),
        Value = attribute.Type switch
        {
            OtbmCustomAttributeType.String => attribute.StringValue,
            OtbmCustomAttributeType.Integer => attribute.IntegerValue.ToString(CultureInfo.InvariantCulture),
            OtbmCustomAttributeType.Float => attribute.FloatValue.ToString("R", CultureInfo.InvariantCulture),
            OtbmCustomAttributeType.Boolean => attribute.BooleanValue ? "true" : "false",
            OtbmCustomAttributeType.Double => attribute.DoubleValue.ToString("R", CultureInfo.InvariantCulture),
            _ => string.Empty
        }
    };

    public bool TryToModel(string key, out OtbmCustomAttribute attribute)
    {
        attribute = new OtbmCustomAttribute { Key = key };
        switch (Type)
        {
            case "String":
                attribute.Type = OtbmCustomAttributeType.String;
                attribute.StringValue = Value;
                return true;
            case "Integer" when int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer):
                attribute.Type = OtbmCustomAttributeType.Integer;
                attribute.IntegerValue = integer;
                return true;
            case "Float" when float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var single):
                attribute.Type = OtbmCustomAttributeType.Float;
                attribute.FloatValue = single;
                return true;
            case "Boolean" when bool.TryParse(Value, out var boolean):
                attribute.Type = OtbmCustomAttributeType.Boolean;
                attribute.BooleanValue = boolean;
                return true;
            case "Boolean" when Value is "1" or "0":
                attribute.Type = OtbmCustomAttributeType.Boolean;
                attribute.BooleanValue = Value == "1";
                return true;
            case "Double" when double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                attribute.Type = OtbmCustomAttributeType.Double;
                attribute.DoubleValue = number;
                return true;
            default:
                return false;
        }
    }
}

public sealed record MapSearchResult(OtbmTileCoord Coordinate, string Description)
{
    public ushort X => Coordinate.X;
    public ushort Y => Coordinate.Y;
    public byte Z => Coordinate.Z;
    public string PositionLabel => $"({X}, {Y}, {Z})";
}

internal readonly record struct MapViewPosition(ushort X, ushort Y, byte Z);
internal readonly record struct MapMinimapSourceSample(
    ushort X,
    ushort Y,
    uint GroundItemId,
    uint PreferredItemId);
internal readonly record struct MapMinimapSourceOverride(bool Exists, MapMinimapSourceSample Source);
internal sealed record MapLoadAnalysis(
    IReadOnlyDictionary<byte, int> FloorTileCounts,
    string FloorBreakdown,
    byte TargetFloor,
    int AverageX,
    int AverageY,
    bool HasTiles);
internal readonly record struct MapLightSource(ushort X, ushort Y, ushort Level, ushort Color);
internal sealed record MapLightViewportCache(
    byte Floor,
    byte LastFloor,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY,
    MapLightSource[] Sources);

/// <summary>Jedna warstwa renderowana nad ground tile (item z `tile.Items`).</summary>
public sealed class MapTileLayer
{
    public ushort ItemId { get; init; }
    public Bitmap? Thumbnail { get; init; }
    public MapSpriteRenderMetrics RenderMetrics { get; init; }
    public bool StartsNewStack { get; init; }
    public byte? Count { get; init; }
    public string? TechnicalColor { get; init; }
    public bool HasTechnicalColor => TechnicalColor is not null;
}

public sealed partial class MapPaletteItem : ObservableObject
{
    private Bitmap? _thumbnail;
    private bool _thumbnailLoaded;

    public uint ItemId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? EntityName { get; init; }
    public MapPaletteEntityKind EntityKind { get; init; }
    public uint EntityId { get; init; }
    public int EntityIndex { get; init; } = -1;
    public bool CreatureIsNpc { get; init; }
    public RmePaletteCategory Category { get; init; } = RmePaletteCategory.Raw;
    public RmeBrushDefinition? Brush { get; init; }
    internal Func<Bitmap?>? ThumbnailLoader { get; init; }
    public int VariationCount => Math.Max(1, Brush?.Alternatives.Count ?? 1);
    public string CategoryLabel => Category.ToString();
    public string Label => ItemId == 0 ? Name : $"#{ItemId} {Name}";
    [ObservableProperty] private bool _isSelected;

    public Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded) return _thumbnail;
            _thumbnailLoaded = true;
            _thumbnail = ThumbnailLoader?.Invoke();
            return _thumbnail;
        }
        set
        {
            if (ReferenceEquals(_thumbnail, value) && _thumbnailLoaded) return;
            _thumbnail = value;
            _thumbnailLoaded = true;
            OnPropertyChanged();
        }
    }
}

public enum MapPaletteEntityKind
{
    None,
    House,
    Waypoint,
    Creature,
    Spawn,
    OptionalBorder,
    ProtectionZone,
    NoPvpZone,
    NoLogoutZone,
    PvpZone
}

internal sealed record MapClipboard(
    IReadOnlyList<MapClipboardTile> Tiles,
    IReadOnlyList<MapClipboardSpawn> Spawns);

internal sealed record MapClipboardTile(
    int OffsetX,
    int OffsetY,
    int OffsetZ,
    OtbmTile Tile);

internal sealed record MapClipboardSpawn(
    int OffsetX,
    int OffsetY,
    int OffsetZ,
    int Radius,
    IReadOnlyList<MapClipboardCreature> Creatures);

internal sealed record MapClipboardCreature(
    string Name,
    int OffsetX,
    int OffsetY,
    int OffsetZ,
    int SpawnTime,
    int Direction,
    bool IsNpc);
