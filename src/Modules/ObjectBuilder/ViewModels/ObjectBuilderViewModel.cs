using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using Modules.ObjectBuilder.Services;
using Narzedzia.Contracts.Localization;
using Narzedzia.Core.Interchange;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;
using Narzedzia.Core.Tibia12;

namespace Modules.ObjectBuilder.ViewModels;

public partial class ObjectBuilderViewModel : ObservableObject
{
    private const int SpritePageSize = 120;
    private const int MaxEditableSpriteSlots = 250_000;

    private readonly LegacyDatService _datService = new();
    private readonly LegacySpriteStore _spriteStore = new();
    private readonly LegacyObdCodec _obdCodec = new();
    private readonly LegacyThingExportService _exportService = new();
    private readonly LegacySpriteImageImportService _spriteImageImportService = new();
    private readonly LegacyThumbnailCache _thumbnailCache;
    private readonly DispatcherTimer _animationTimer;
    private readonly List<LegacyThingListItem> _selectedThings = [];
    private int _previewWidth;
    private int _previewHeight;
    private bool _datDirty;
    private bool _sprDirty;

    public ObjectBuilderViewModel()
    {
        _thumbnailCache = new LegacyThumbnailCache(_spriteStore);
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _animationTimer.Tick += (_, _) => AdvanceAnimation();
    }

    public ObservableCollection<LegacyThingListItem> Things { get; } = new();
    public ObservableCollection<LegacySpriteSlotItem> SpriteSlots { get; } = new();
    public ObservableCollection<LegacySpriteListItem> AvailableSprites { get; } = new();
    public ObservableCollection<LegacyFrameGroupListItem> FrameGroups { get; } = new();

    [ObservableProperty] private string _title = "Old Assets Editor";
    [ObservableProperty] private string _statusText = "Wczytaj Tibia.dat i Tibia.spr, aby rozpocząć.";
    [ObservableProperty] private string _datPath = "(brak)";
    [ObservableProperty] private string _sprPath = "(brak)";
    [ObservableProperty] private int _activeCategory;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private LegacyThingListItem? _selectedThing;
    [ObservableProperty] private LegacySpriteSlotItem? _selectedSpriteSlot;
    [ObservableProperty] private LegacySpriteListItem? _selectedAvailableSprite;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private string _previewInfo = "Brak podglądu.";
    [ObservableProperty] private int _selectedGroup;
    [ObservableProperty] private int _direction;
    [ObservableProperty] private int _addon;
    [ObservableProperty] private int _patternZ;
    [ObservableProperty] private int _frame;
    [ObservableProperty] private int _currentLayer;
    [ObservableProperty] private int _zoom = 4;
    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private bool _showCropSize;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private int _spritePage;
    [ObservableProperty] private int _spriteJumpId = 1;
    [ObservableProperty] private int _spritePanelTab;
    [ObservableProperty] private string _spriteImportSummary = "Upuść PNG, BMP albo JPG. Każdy kafel 32×32 otrzyma nowe ID.";
    [ObservableProperty] private bool _isImportingSprites;
    [ObservableProperty] private bool _legacyExtendedSprites = true;
    [ObservableProperty] private bool _legacyTransparency;
    [ObservableProperty] private bool _legacyImprovedAnimations = true;
    [ObservableProperty] private bool _legacyFrameGroups = true;
    [ObservableProperty] private string _legacyMetadataFormat = "auto";
    [ObservableProperty] private string _legacyProtocol = "auto";
    [ObservableProperty] private bool _hasUnsavedChanges;

    private DatThingType? CurrentThing => SelectedThing?.Source;
    private DatThingFrameGroup? CurrentGroup => CurrentThing is { FrameGroups.Count: > 0 } thing
        ? thing.FrameGroups[Math.Clamp(SelectedGroup, 0, thing.FrameGroups.Count - 1)]
        : null;

    public string LegacyOptionsSummary =>
        $"Protokół {LegacyProtocol}, format DAT: {LegacyMetadataFormat}. Sprite ID {(LegacyExtendedSprites ? "32-bit" : "16-bit")}, " +
        $"alfa {(LegacyTransparency ? "RGBA" : "RGB")}, " +
        $"animacje {(LegacyImprovedAnimations ? "nowe" : "stare")}, frame groups {(LegacyFrameGroups ? "tak" : "nie")}.";

    public string TabItems => $"Przedmioty ({_datService.Data?.ItemCount ?? 0})";
    public string TabOutfits => $"Stroje ({_datService.Data?.OutfitCount ?? 0})";
    public string TabEffects => $"Efekty ({_datService.Data?.EffectCount ?? 0})";
    public string TabMissiles => $"Pociski ({_datService.Data?.MissileCount ?? 0})";
    public int SelectedThingCount => _selectedThings.Count > 0 ? _selectedThings.Count : SelectedThing is null ? 0 : 1;
    public bool HasSelectedThings => SelectedThingCount > 0;
    public string SuggestedExportName => SelectedThing is null
        ? "object"
        : CategoryFileName(SelectedThing.Source.Category);
    public string ObjectPosition => SelectedThingCount > 1
        ? $"Zaznaczono {SelectedThingCount} / {Things.Count}"
        : SelectedThing is null
        ? $"0 / {Things.Count}"
        : $"{Things.IndexOf(SelectedThing) + 1} / {Things.Count}";
    public string CurrentObjectTitle => SelectedThing is null
        ? "Brak wybranego obiektu"
        : $"{CategoryDisplayName(SelectedThing.Source.Category)} {SelectedThing.Id}";
    public bool HasSelectedThing => SelectedThing is not null;
    public bool HasLoadedDat => _datService.Data is not null;
    public bool HasLoadedSprites => _spriteStore.Data is not null;
    public bool DeletionWillRenumberIds
    {
        get
        {
            var selected = GetSelectedThingSources();
            if (selected.Length == 0) return false;
            var lastId = _datService.GetCategory(selected[0].Category).LastOrDefault()?.Id ?? 0;
            return selected.Any(thing => thing.Id < lastId);
        }
    }
    public int GroupMaximum => Math.Max(0, (CurrentThing?.FrameGroups.Count ?? 1) - 1);
    public int DirectionMaximum => Math.Max(0,
        LegacyDirectionMapping.GetDirectionCount(CurrentThing?.Category ?? DatThingCategory.Items) - 1);
    public int AddonMaximum => Math.Max(0, (CurrentGroup?.PatternY ?? 1) - 1);
    public int PatternZMaximum => Math.Max(0, (CurrentGroup?.PatternZ ?? 1) - 1);
    public int FrameMaximum => Math.Max(0, (CurrentGroup?.Frames ?? 1) - 1);
    public int LayerMaximum => Math.Max(0, (CurrentGroup?.Layers ?? 1) - 1);
    public int LayerCount => LayerMaximum + 1;
    public int EditedLayerNumber { get => CurrentLayer + 1; set => CurrentLayer = Math.Clamp(value - 1, 0, LayerMaximum); }
    public string LayerPosition => $"Warstwa {CurrentLayer + 1}/{LayerMaximum + 1}";
    public bool HasEightDirections => DirectionMaximum >= 7;
    public bool ShowAddonSelector => IsOutfitCategory && AddonMaximum > 0;
    public bool ShowMountSelector => PatternZMaximum > 0;
    public bool ShowLayerSelector => LayerMaximum > 0;
    public bool CanEditFrameGroups => LegacyFrameGroups && ActiveCategory == (int)DatThingCategory.Outfits;
    public bool CanRemoveFrameGroup => CanEditFrameGroups && (CurrentThing?.FrameGroups.Count ?? 0) > 1;
    public string PlayButtonText => IsPlaying ? "⏸ Pauza" : "▶ Odtwórz";
    public string AnimationPosition => $"{Math.Clamp(Frame, 0, FrameMaximum) + 1}/{FrameMaximum + 1}";
    public bool IsItemCategory => CurrentThing?.Category == DatThingCategory.Items;
    public bool IsOutfitCategory => CurrentThing?.Category == DatThingCategory.Outfits;
    public bool IsEffectCategory => CurrentThing?.Category == DatThingCategory.Effects;
    public bool IsMissileCategory => CurrentThing?.Category == DatThingCategory.Missiles;
    public bool ShowPropertiesSection => HasSelectedThing;
    public bool ShowDirectionSelector => IsOutfitCategory || IsMissileCategory;
    public bool ShowFlagsSection => HasSelectedThing && !IsMissileCategory;
    public bool ShowAnimationPanel => FrameMaximum > 0;
    public bool ShowImprovedAnimationSettings => ShowAnimationPanel
        && LegacyImprovedAnimations
        && CurrentGroup?.GroupType == 0
        && (IsItemCategory
            || IsEffectCategory
            || (IsOutfitCategory && (CurrentThing?.AnimateAlways == true || (CurrentThing?.FrameGroups.Count ?? 0) > 1)));
    public bool SupportsGroundBorder => IsItemCategory && Capabilities.GroundBorder;
    public bool SupportsWallHooks => IsItemCategory && Capabilities.WallHooks;
    public bool SupportsDontHide => IsItemCategory && Capabilities.DontHide;
    public bool SupportsTranslucent => IsItemCategory && Capabilities.Translucent;
    public bool SupportsCharges => IsItemCategory && Capabilities.Charges;
    public bool SupportsFloorChange => IsItemCategory && Capabilities.FloorChange;
    public bool SupportsEquip => IsItemCategory && Capabilities.Equip;
    public bool SupportsMarket => IsItemCategory && Capabilities.Market;
    public bool SupportsDefaultAction => IsItemCategory && Capabilities.DefaultAction;
    public bool SupportsNoMoveAnimation => IsItemCategory && Capabilities.NoMoveAnimation;
    public bool SupportsUseable => IsItemCategory && Capabilities.Useable;
    public bool SupportsWrappable => IsItemCategory && Capabilities.Wrapping;
    public bool SupportsTopEffect => IsEffectCategory && Capabilities.TopEffect;
    public bool CanPersistWrappable => Capabilities.CanPersistWrapping;
    public bool CanPersistTopEffect => Capabilities.CanPersistTopEffect;
    public bool SupportsBones => HasSelectedThing;
    public bool CanPersistBones => Capabilities.Bones;
    public bool SupportsPatternZ => Capabilities.PatternZ;
    public int ObjectOffsetMinimum => Capabilities.OffsetMinimum;
    public int ObjectOffsetMaximum => Capabilities.OffsetMaximum;
    private DatMetadataFormat MetadataFormat => _datService.Options.MetadataFormat;
    private uint DatSignature => _datService.Data?.Signature ?? 0;
    private LegacyObjectBuilderCapabilities Capabilities =>
        LegacyObjectBuilderCapabilities.For(MetadataFormat, DatSignature);

    public IReadOnlyList<string> LensHelpTypeLabels { get; } =
    [
        "Ladders", "Sewer Grates", "Dungeon Floor", "Levers", "Doors",
        "Special Doors", "Stairs", "Mailboxes", "Depot Boxes", "Dustbins",
        "Stone Piles", "Signs", "Books and Scrolls"
    ];

    public IReadOnlyList<string> EquipSlotLabels { get; } =
    [
        "Two Hand Weapon", "Helmet", "Amulet", "Backpack", "Armor", "Shield",
        "One Hand Weapon", "Legs", "Boots", "Ring", "Arrow"
    ];

    public IReadOnlyList<string> DefaultActionLabels { get; } =
        ["None", "Look", "Use", "Open", "Autowalk Highlight"];

    public IReadOnlyList<string> LegacyMarketCategoryLabels { get; } =
    [
        "Armors", "Amulets", "Boots", "Containers", "Decoration", "Foods",
        "Helmets and Hats", "Legs", "Others", "Potions", "Rings", "Runes",
        "Shields", "Tools", "Valuables", "Ammunition", "Axes", "Clubs",
        "Distance", "Swords", "Wands and Rods", "Premium Scrolls", "Meta Weapons"
    ];
    public IReadOnlyList<LegacyTibiaColorOption> TibiaColorOptions { get; } = Enumerable.Range(0, 216)
        .Select(LegacyTibiaColorOption.FromId)
        .ToArray();

    public int LightColorSelectionIndex
    {
        get => Math.Clamp((int)(CurrentThing?.LightColor ?? 0), 0, 215);
        set => SetIndexedProperty(value, 0, 215,
            static (thing, color) => thing.LightColor = (ushort)color,
            nameof(LightColorSelectionIndex));
    }

    public int AutomapColorSelectionIndex
    {
        get => Math.Clamp((int)(CurrentThing?.MiniMapColor ?? 0), 0, 215);
        set => SetIndexedProperty(value, 0, 215,
            static (thing, color) => thing.MiniMapColor = (ushort)color,
            nameof(AutomapColorSelectionIndex));
    }

    public double PreviewDisplayWidth => Math.Max(32, _previewWidth) * Zoom;
    public double PreviewDisplayHeight => Math.Max(32, _previewHeight) * Zoom;
    public int SpritePageCount => Math.Max(1,
        (int)Math.Ceiling((_spriteStore.Data?.Sprites.Count ?? 0) / (double)SpritePageSize));
    public string SpritePageSummary =>
        $"Strona {Math.Min(SpritePage + 1, SpritePageCount)} / {SpritePageCount} · {_spriteStore.Data?.Sprites.Count ?? 0} sprite'ów";
    public int MaxSpriteId => _spriteStore.Data?.Sprites.Count ?? 1;

    public int TextureWidth { get => CurrentGroup?.Width ?? 1; set => ChangeStructure(group => group.Width = ToDimension(value)); }
    public int TextureHeight { get => CurrentGroup?.Height ?? 1; set => ChangeStructure(group => group.Height = ToDimension(value)); }
    public int TextureExactSize { get => CurrentGroup?.ExactSize ?? 32; set => ChangeSimple(group => group.ExactSize = (byte)Math.Clamp(value, 0, byte.MaxValue)); }
    public int TextureLayers { get => CurrentGroup?.Layers ?? 1; set => ChangeStructure(group => group.Layers = ToDimension(value)); }
    public int TexturePatternX { get => CurrentGroup?.PatternX ?? 1; set => ChangeStructure(group => group.PatternX = ToDimension(value)); }
    public int TexturePatternY { get => CurrentGroup?.PatternY ?? 1; set => ChangeStructure(group => group.PatternY = ToDimension(value)); }
    public int TexturePatternZ { get => CurrentGroup?.PatternZ ?? 1; set => ChangeStructure(group => group.PatternZ = ToDimension(value)); }
    public int TextureFrames { get => CurrentGroup?.Frames ?? 1; set => ChangeStructure(group => group.Frames = ToDimension(value)); }
    public int CurrentGroupType { get => CurrentGroup?.GroupType ?? 0; set => ChangeSimple(group => group.GroupType = Math.Clamp(value, 0, 1), true); }
    public int AnimationMode { get => CurrentGroup?.AnimationMode ?? 0; set => ChangeSimple(group => group.AnimationMode = (byte)Math.Clamp(value, 0, 1)); }
    public int AnimationLoopCount { get => CurrentGroup?.LoopCount ?? -1; set => ChangeSimple(group => group.LoopCount = Math.Clamp(value, -1, 100_000)); }
    public int AnimationStartFrame { get => CurrentGroup?.StartFrame ?? 0; set => ChangeSimple(group => group.StartFrame = (sbyte)Math.Clamp(value, -1, Math.Min(sbyte.MaxValue, FrameMaximum))); }
    public int FrameDurationMin { get => (int)GetCurrentDuration().Min; set => SetCurrentDuration((uint)Math.Clamp(value, 0, 60_000), null); }
    public int FrameDurationMax { get => (int)GetCurrentDuration().Max; set => SetCurrentDuration(null, (uint)Math.Clamp(value, 0, 60_000)); }
    public bool HasBones
    {
        get => CurrentThing?.HasBones == true;
        set
        {
            if (CurrentThing is not { } thing || thing.HasBones == value) return;
            thing.HasBones = value;
            SetDirty(dat: true);
            OnPropertyChanged();
        }
    }
    public int BoneOffsetX { get => GetBoneOffset(false); set => SetBoneOffset(false, value); }
    public int BoneOffsetY { get => GetBoneOffset(true); set => SetBoneOffset(true, value); }

    public bool IsNorth { get => Direction == 0; set { if (value) Direction = 0; } }
    public bool IsEast { get => Direction == 1; set { if (value) Direction = 1; } }
    public bool IsSouth { get => Direction == 2; set { if (value) Direction = 2; } }
    public bool IsWest { get => Direction == 3; set { if (value) Direction = 3; } }
    public bool IsSouthWest { get => Direction == 4; set { if (value) Direction = 4; } }
    public bool IsSouthEast { get => Direction == 5; set { if (value) Direction = 5; } }
    public bool IsNorthWest { get => Direction == 6; set { if (value) Direction = 6; } }
    public bool IsNorthEast { get => Direction == 7; set { if (value) Direction = 7; } }

    public bool IsCommonRenderOrder { get => CurrentThing is { IsGroundBorder: false, IsOnBottom: false, IsOnTop: false }; set { if (value) SetRenderOrder(0); } }
    public bool IsGroundBorderRenderOrder { get => CurrentThing?.IsGroundBorder == true; set { if (value) SetRenderOrder(1); } }
    public bool IsBottomRenderOrder { get => CurrentThing?.IsOnBottom == true; set { if (value) SetRenderOrder(2); } }
    public bool IsTopRenderOrder { get => CurrentThing?.IsOnTop == true; set { if (value) SetRenderOrder(3); } }

    public int LensHelpSelectionIndex
    {
        get
        {
            var value = CurrentThing?.LensHelp ?? 0;
            return value is >= 1100 and <= 1112 ? value - 1100 : 0;
        }
        set => SetIndexedProperty(value, 0, LensHelpTypeLabels.Count - 1,
            static (thing, index) => thing.LensHelp = (ushort)(index + 1100), nameof(LensHelpSelectionIndex));
    }

    public int EquipSlotSelectionIndex
    {
        get => Math.Clamp((int)(CurrentThing?.ClothSlot ?? 0), 0, EquipSlotLabels.Count - 1);
        set => SetIndexedProperty(value, 0, EquipSlotLabels.Count - 1,
            static (thing, index) => thing.ClothSlot = (ushort)index, nameof(EquipSlotSelectionIndex));
    }

    public int DefaultActionSelectionIndex
    {
        get => Math.Clamp((int)(CurrentThing?.DefaultAction ?? 0), 0, DefaultActionLabels.Count - 1);
        set => SetIndexedProperty(value, 0, DefaultActionLabels.Count - 1,
            static (thing, index) => thing.DefaultAction = (ushort)index, nameof(DefaultActionSelectionIndex));
    }

    public int MarketCategorySelectionIndex
    {
        get
        {
            var value = CurrentThing?.MarketCategory ?? 0;
            return value == 0 ? 8 : Math.Clamp(value - 1, 0, LegacyMarketCategoryLabels.Count - 1);
        }
        set => SetIndexedProperty(value, 0, LegacyMarketCategoryLabels.Count - 1,
            static (thing, index) => thing.MarketCategory = (ushort)(index + 1), nameof(MarketCategorySelectionIndex));
    }

    [RelayCommand]
    private async Task OpenLegacyAssetsAsync()
    {
        var folder = await PickFolderAsync("Wybierz folder z Tibia.dat i Tibia.spr");
        if (folder is null) return;

        var configuration = LegacyClientConfiguration.LoadFromDirectory(folder);
        var datPath = FindLegacyFile(folder, configuration.MetadataFileName, "Tibia.dat", "*.dat");
        var sprPath = FindLegacyFile(folder, configuration.SpritesFileName, "Tibia.spr", "*.spr");
        if (datPath is null || sprPath is null)
        {
            StatusText = "W folderze nie znaleziono pary Tibia.dat + Tibia.spr.";
            return;
        }

        try
        {
            StopAnimation();
            _datService.LoadAuto(datPath, BuildPreferredDatOptions(configuration, datPath));
            _spriteStore.LoadAuto(sprPath, configuration.Transparency);
            ValidateDatSprPair();
            _thumbnailCache.Clear();
            ApplyDetectedDatOptions(_datService.Options);
            LegacyTransparency = _spriteStore.Data?.TransparentSprites ?? configuration.Transparency ?? false;
            DatPath = datPath;
            SprPath = sprPath;
            _datDirty = false;
            _sprDirty = false;
            UpdateDirtyState();
            SpritePage = 0;
            RefreshAllData();
            StatusText = BuildLoadedStatus($"Wczytano komplet legacy: {Path.GetFileName(datPath)} + {Path.GetFileName(sprPath)}." + BuildConfigurationStatus(configuration));
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd wczytywania kompletu legacy: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenDatAsync()
    {
        var path = await PickOpenFileAsync("Wybierz Tibia.dat", ["*.dat"]);
        if (path is null) return;

        try
        {
            StopAnimation();
            var configuration = LegacyClientConfiguration.LoadFromDirectory(Path.GetDirectoryName(path)!);
            _datService.LoadAuto(path, BuildPreferredDatOptions(configuration, path));
            ValidateDatSprPair();
            ApplyDetectedDatOptions(_datService.Options);
            _thumbnailCache.Clear();
            DatPath = path;
            _datDirty = false;
            UpdateDirtyState();
            RefreshAllData();
            StatusText = BuildLoadedStatus("Wczytano Tibia.dat." + BuildConfigurationStatus(configuration));
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd wczytywania DAT: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveDatAsync()
    {
        if (_datService.Data is null)
        {
            StatusText = "Najpierw wczytaj Tibia.dat.";
            return;
        }

        var savePath = await PickSaveFileAsync("Zapisz Tibia.dat", "Tibia.dat");
        if (savePath is null) return;

        try
        {
            _datService.Save(savePath);
            DatPath = savePath;
            _datDirty = false;
            UpdateDirtyState();
            StatusText = _sprDirty
                ? "Zapisano Tibia.dat. Tibia.spr nadal zawiera niezapisane zmiany."
                : "Zapisano Tibia.dat.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd zapisu DAT: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenSprAsync()
    {
        var path = await PickOpenFileAsync("Wybierz Tibia.spr", ["*.spr"]);
        if (path is null) return;

        try
        {
            var configuration = LegacyClientConfiguration.LoadFromDirectory(Path.GetDirectoryName(path)!);
            _spriteStore.LoadAuto(path, configuration.Transparency);
            ValidateDatSprPair();
            _thumbnailCache.Clear();
            LegacyExtendedSprites = _spriteStore.Data?.ExtendedSprites ?? LegacyExtendedSprites;
            LegacyTransparency = _spriteStore.Data?.TransparentSprites ?? LegacyTransparency;
            UpdateDetectedProtocol();
            SprPath = path;
            _sprDirty = false;
            UpdateDirtyState();
            SpritePage = 0;
            RefreshSpriteBrowser();
            RefreshThingList(true);
            RefreshPreview();
            StatusText = BuildLoadedStatus($"Wczytano Tibia.spr.{BuildConfigurationStatus(configuration)}");
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd wczytywania SPR: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSprAsync()
    {
        if (_spriteStore.Data is null)
        {
            StatusText = "Najpierw wczytaj Tibia.spr.";
            return;
        }

        var savePath = await PickSaveFileAsync("Zapisz Tibia.spr", "Tibia.spr");
        if (savePath is null) return;

        try
        {
            _spriteStore.Save(savePath);
            SprPath = savePath;
            _sprDirty = false;
            UpdateDirtyState();
            StatusText = _datDirty
                ? "Zapisano Tibia.spr. Tibia.dat nadal zawiera niezapisane zmiany."
                : "Zapisano Tibia.spr.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd zapisu SPR: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportObdAsync()
    {
        if (_datService.Data is null || _spriteStore.Data is null)
        {
            StatusText = "Przed importem OBD wczytaj komplet Tibia.dat + Tibia.spr.";
            return;
        }

        var paths = await PickOpenFilesAsync("Importuj obiekty Object Builder", ["*.obd"]);
        if (paths.Length == 0) return;

        try
        {
            var documents = paths.Select(_obdCodec.Read).ToArray();
            var imported = new List<DatThingType>(documents.Length);
            foreach (var document in documents)
            {
                for (var groupIndex = 0; groupIndex < document.Thing.FrameGroups.Count; groupIndex++)
                {
                    var group = document.Thing.FrameGroups[groupIndex];
                    var pixels = document.SpritePixelsByGroup[groupIndex];
                    var remappedIds = new Dictionary<uint, uint>();
                    for (var index = 0; index < group.SpriteIds.Length; index++)
                    {
                        var sourceId = group.SpriteIds[index];
                        if (sourceId == 0)
                        {
                            group.SpriteIds[index] = 0;
                            continue;
                        }

                        if (!remappedIds.TryGetValue(sourceId, out var targetId))
                        {
                            targetId = _spriteStore.ImportSprite(sourceId, pixels[index]);
                            remappedIds[sourceId] = targetId;
                        }
                        group.SpriteIds[index] = targetId;
                    }
                }

                LegacyOutfitFrameGroupAdapter.Adapt(
                    document.Thing,
                    sourceUsesFrameGroups: document.UsesFrameGroups,
                    targetSupportsFrameGroups: LegacyFrameGroups);

                imported.Add(_datService.AddThing(document.Thing));
            }

            _thumbnailCache.Clear();
            SetDirty(dat: true, spr: true);
            ActiveCategory = (int)imported[^1].Category;
            SearchText = string.Empty;
            SpritePage = Math.Max(0, SpritePageCount - 1);
            RefreshAllData();
            SelectedThing = Things.FirstOrDefault(item => ReferenceEquals(item.Source, imported[^1]));
            SetSelectedThings(SelectedThing is null ? [] : [SelectedThing]);
            StatusText = $"Zaimportowano {imported.Count} obiektów OBD ze sprite'ami, offsetem i flagami zapisanymi w pliku. Dodane sprite'y są w pamięci — zapisz Tibia.dat i Tibia.spr.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd importu OBD: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportAecAsync()
    {
        if (_datService.Data is null || _spriteStore.Data is null)
        {
            StatusText = "Przed importem AEC wczytaj komplet Tibia.dat + Tibia.spr.";
            return;
        }

        var paths = await PickOpenFilesAsync("Importuj kontenery Assets Editor", ["*.aec"]);
        if (paths.Length == 0) return;

        try
        {
            var conversions = new List<LegacyObjectConversion>();
            var warnings = new List<string>();
            foreach (var path in paths)
            {
                await using var input = File.OpenRead(path);
                var container = Appearances.Parser.ParseFrom(input);
                foreach (var embedded in AecContainerCodec.ReadEmbeddedAppearances(
                             container,
                             preserveFlags: false))
                {
                    var conversion = LegacyAppearanceConverter.ToLegacy(
                        embedded.Appearance,
                        embedded.Appearance.AppearanceType,
                        index => index <= int.MaxValue && (int)index < embedded.Sprites.Count
                            ? embedded.Sprites[(int)index]
                            : null,
                        targetSupportsFrameGroups: LegacyFrameGroups);
                    conversions.Add(conversion);
                    warnings.AddRange(conversion.Warnings);
                }
            }

            if (conversions.Count == 0)
            {
                StatusText = "Wybrane kontenery AEC nie zawierają obiektów.";
                return;
            }

            // Najpierw parsujemy i konwertujemy cały zestaw. Dzięki temu uszkodzony
            // kolejny plik nie zostawi częściowo zaimportowanych wcześniejszych danych.
            var imported = conversions.Select(ImportLegacyConversion).ToArray();
            FinalizeImportedThings(imported);
            StatusText = $"Zaimportowano {imported.Length} obiektów AEC ze sprite'ami i offsetem (bez flag). Sprite'y zostały podzielone na kafle 32×32 — zapisz Tibia.dat i Tibia.spr.{WarningSuffix(warnings)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd importu AEC: {ex.Message}";
        }
    }

    public void SetSelectedThings(IEnumerable<LegacyThingListItem> items)
    {
        _selectedThings.Clear();
        _selectedThings.AddRange(items.Where(Things.Contains).Distinct());
        OnPropertyChanged(nameof(SelectedThingCount));
        OnPropertyChanged(nameof(HasSelectedThings));
        OnPropertyChanged(nameof(DeletionWillRenumberIds));
        OnPropertyChanged(nameof(ObjectPosition));
    }

    public void ExportSelectedObjects(LegacyExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_spriteStore.Data is null)
        {
            StatusText = "Przed eksportem wczytaj Tibia.spr.";
            return;
        }

        var selected = GetSelectedThingSources();
        if (selected.Length == 0)
        {
            StatusText = "Zaznacz co najmniej jeden obiekt do eksportu.";
            return;
        }

        try
        {
            Directory.CreateDirectory(options.OutputDirectory);
            var baseName = SanitizeFileName(options.BaseName);
            if (options.Format == LegacyExportFormat.Aec)
            {
                var container = new Appearances();
                var warnings = new List<string>();
                foreach (var thing in selected)
                {
                    var conversion = LegacyAppearanceConverter.ToAppearance(
                        thing,
                        _spriteStore.GetSpritePixels,
                        sourceUsesFrameGroups: LegacyFrameGroups);
                    var partial = AecContainerCodec.BuildExportContainer(
                        [conversion.Appearance],
                        id => conversion.Sprites.TryGetValue(id, out var payload) ? payload : null,
                        preserveFlags: false);
                    MergeAecContainer(container, partial);
                    warnings.AddRange(conversion.Warnings);
                }

                var aecPath = Path.Combine(options.OutputDirectory, baseName + options.Extension);
                using var output = File.Create(aecPath);
                container.WriteTo(output);
                StatusText = $"Wyeksportowano {selected.Length} obiektów ze sprite'ami i offsetem do AEC (bez flag): {aecPath}{WarningSuffix(warnings)}";
                return;
            }

            foreach (var thing in selected)
            {
                var itemName = selected.Length == 1
                    ? baseName
                    : $"{baseName}_{CategoryFileName(thing.Category)}_{thing.Id}";
                var path = Path.Combine(options.OutputDirectory, itemName + options.Extension);
                if (options.Format == LegacyExportFormat.Obd)
                {
                    _obdCodec.Write(path, thing, _spriteStore, useFrameGroups: LegacyFrameGroups);
                }
                else
                {
                    var transparent = options.TransparentBackground && options.Format != LegacyExportFormat.Jpg;
                    _exportService.ExportImage(path, thing, _spriteStore, transparent);
                }
            }

            StatusText = options.Format == LegacyExportFormat.Obd
                ? $"Wyeksportowano {selected.Length} obiektów ze sprite'ami, offsetem i flagami jako OBD do: {options.OutputDirectory}"
                : $"Wyeksportowano {selected.Length} obiektów jako {options.Format.ToString().ToUpperInvariant()} do: {options.OutputDirectory}";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd eksportu obiektów: {ex.Message}";
        }
    }

    private DatThingType ImportLegacyConversion(LegacyObjectConversion conversion)
    {
        var remappedIds = new Dictionary<uint, uint>();
        foreach (var group in conversion.Thing.FrameGroups)
        for (var index = 0; index < group.SpriteIds.Length; index++)
        {
            var sourceId = group.SpriteIds[index];
            if (sourceId == 0)
            {
                group.SpriteIds[index] = 0;
                continue;
            }

            if (!remappedIds.TryGetValue(sourceId, out var targetId))
            {
                if (!conversion.TileSprites.TryGetValue(sourceId, out var pixels))
                {
                    throw new InvalidDataException($"Brak kafla 32×32 konwersji #{sourceId}.");
                }
                targetId = _spriteStore.ImportSprite(sourceId, pixels);
                remappedIds[sourceId] = targetId;
            }
            group.SpriteIds[index] = targetId;
        }

        return _datService.AddThing(conversion.Thing);
    }

    private void FinalizeImportedThings(IReadOnlyList<DatThingType> imported)
    {
        _thumbnailCache.Clear();
        SetDirty(dat: true, spr: true);
        ActiveCategory = (int)imported[^1].Category;
        SearchText = string.Empty;
        SpritePage = Math.Max(0, SpritePageCount - 1);
        RefreshAllData();
        SelectedThing = Things.FirstOrDefault(item => ReferenceEquals(item.Source, imported[^1]));
        SetSelectedThings(SelectedThing is null ? [] : [SelectedThing]);
    }

    private static void MergeAecContainer(Appearances target, Appearances source)
    {
        target.Object.Add(source.Object);
        target.Outfit.Add(source.Outfit);
        target.Effect.Add(source.Effect);
        target.Missile.Add(source.Missile);
    }

    private static string WarningSuffix(IReadOnlyCollection<string> warnings) => warnings.Count == 0
        ? string.Empty
        : $" Ostrzeżenia zgodności: {warnings.Count} (pierwsze: {warnings.First()})";

    [RelayCommand]
    private void NewThing()
    {
        if (_datService.Data is null)
        {
            StatusText = "Najpierw wczytaj Tibia.dat.";
            return;
        }

        try
        {
            EnsureThingCapacity(1);
            var thing = new DatThingType { Category = (DatThingCategory)ActiveCategory };
            thing.FrameGroups.Add(CreateDefaultGroup());
            _datService.AddThing(thing);
            FinishObjectMutation(thing);
            MarkChanged($"Dodano nowy obiekt #{thing.Id}. Przeciągnij sprite z biblioteki na siatkę 32×32.");
        }
        catch (Exception ex)
        {
            StatusText = $"Nie udało się dodać obiektu: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DuplicateSelectedThings()
    {
        var selected = GetSelectedThingSources();
        if (_datService.Data is null || selected.Length == 0)
        {
            StatusText = "Zaznacz co najmniej jeden obiekt do zduplikowania.";
            return;
        }

        try
        {
            EnsureThingCapacity(selected.Length);
            var duplicates = new List<DatThingType>(selected.Length);
            foreach (var source in selected)
            {
                var duplicate = source.DeepClone();
                _datService.AddThing(duplicate);
                duplicates.Add(duplicate);
            }

            FinishObjectMutation(duplicates[^1]);
            MarkChanged(duplicates.Count == 1
                ? $"Zduplikowano obiekt jako #{duplicates[0].Id}. Układ, flagi i sprite ID zostały zachowane."
                : $"Zduplikowano {duplicates.Count} obiektów. Zaznaczony jest ostatni: #{duplicates[^1].Id}.");
        }
        catch (Exception ex)
        {
            StatusText = $"Nie udało się zduplikować obiektów: {ex.Message}";
        }
    }

    public void DeleteSelectedThings()
    {
        var selected = GetSelectedThingSources();
        if (_datService.Data is null || selected.Length == 0)
        {
            StatusText = "Zaznacz co najmniej jeden obiekt do usunięcia.";
            return;
        }

        try
        {
            StopAnimation();
            var firstVisibleIndex = selected
                .Select(source => Things.ToList().FindIndex(item => ReferenceEquals(item.Source, source)))
                .Where(index => index >= 0)
                .DefaultIfEmpty(0)
                .Min();
            var renumbered = DeletionWillRenumberIds;
            var removed = _datService.RemoveThings(selected);
            if (removed == 0)
            {
                StatusText = "Wybrane obiekty nie należą już do aktywnej kategorii.";
                return;
            }

            _thumbnailCache.Clear();
            SearchText = string.Empty;
            NotifyCategoryState();
            RefreshThingList();
            SelectedThing = Things.Count == 0 ? null : Things[Math.Clamp(firstVisibleIndex, 0, Things.Count - 1)];
            SetSelectedThings(SelectedThing is null ? [] : [SelectedThing]);
            SetDirty(dat: true);
            StatusText = $"Usunięto {removed} {(removed == 1 ? "obiekt" : "obiekty")}." +
                         (renumbered ? " Późniejsze ID w tej kategorii zostały przenumerowane." : string.Empty) +
                         " Zapisz Tibia.dat, aby utrwalić zmianę.";
        }
        catch (Exception ex)
        {
            StatusText = $"Nie udało się usunąć obiektów: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportSpriteFilesAsync()
    {
        if (_spriteStore.Data is null)
        {
            StatusText = "Najpierw wczytaj Tibia.spr.";
            return;
        }

        var paths = await PickOpenFilesAsync(
            "Dodaj sprite'y do Tibia.spr",
            ["*.png", "*.bmp", "*.jpg", "*.jpeg"],
            "Obrazy sprite'ów");
        if (paths.Length > 0) await ImportSpriteFilesFromPathsAsync(paths);
    }

    public async Task ImportSpriteFilesFromPathsAsync(IEnumerable<string> paths)
    {
        if (_spriteStore.Data is null)
        {
            StatusText = "Najpierw wczytaj Tibia.spr.";
            return;
        }
        if (IsImportingSprites)
        {
            StatusText = "Import sprite'ów już trwa.";
            return;
        }

        try
        {
            IsImportingSprites = true;
            StatusText = "Sprawdzam pliki i dzielę obrazy na kafle 32×32…";
            var normalizedPaths = paths.ToArray();
            var batch = await Task.Run(() => _spriteImageImportService.DecodeFiles(normalizedPaths));
            if (batch.SourceFileCount == 0)
            {
                SpriteImportSummary = "Nie znaleziono obsługiwanych plików PNG, BMP, JPG lub JPEG.";
                StatusText = SpriteImportSummary;
                return;
            }
            if (batch.Tiles.Count == 0)
            {
                SpriteImportSummary = $"Pliki zawierały wyłącznie przezroczyste kafle. Pominięto: {batch.EmptyTileCount}.";
                StatusText = SpriteImportSummary;
                return;
            }

            var spriteData = _spriteStore.Data;
            if (!spriteData.ExtendedSprites && spriteData.Sprites.Count + batch.Tiles.Count > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Import przekroczyłby limit 65535 sprite'ów. Wolne miejsca: {ushort.MaxValue - spriteData.Sprites.Count}.");
            }

            uint firstId = 0;
            uint lastId = 0;
            foreach (var tile in batch.Tiles)
            {
                var id = _spriteStore.ImportSprite(0, tile.BgraPixels);
                if (id == 0) continue;
                if (firstId == 0) firstId = id;
                lastId = id;
            }

            _thumbnailCache.Clear();
            SetDirty(spr: true);
            var targetPage = checked((int)((firstId - 1) / SpritePageSize));
            if (SpritePage == targetPage) RefreshSpriteBrowser(); else SpritePage = targetPage;
            SpriteJumpId = checked((int)firstId);
            SelectedAvailableSprite = AvailableSprites.FirstOrDefault(sprite => sprite.Id == firstId);
            SpritePanelTab = 0;

            var alphaWarning = batch.HasPartialAlpha && !spriteData.TransparentSprites
                ? " Uwaga: ten SPR jest w trybie RGB, więc półprzezroczystość została spłaszczona."
                : string.Empty;
            var skipped = batch.EmptyTileCount + batch.RejectedFileCount;
            SpriteImportSummary = $"Dodano {batch.Tiles.Count} sprite'ów z {batch.SourceFileCount} plików: ID #{firstId}–#{lastId}." +
                                  (skipped > 0 ? $" Pominięto: {skipped}." : string.Empty) + alphaWarning;
            StatusText = SpriteImportSummary + " Zapisz Tibia.spr, aby utrwalić zmiany.";
        }
        catch (Exception ex)
        {
            SpriteImportSummary = $"Import nie został wykonany: {ex.Message}";
            StatusText = SpriteImportSummary;
        }
        finally
        {
            IsImportingSprites = false;
        }
    }

    [RelayCommand]
    private void SetActiveCategory(string category)
    {
        if (!int.TryParse(category, out var value)) return;
        StopAnimation();
        ActiveCategory = Math.Clamp(value, 0, 3);
        SearchText = string.Empty;
        RefreshThingList();
        NotifyCategoryState();
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand] private void FirstThing() { if (Things.Count > 0) SelectedThing = Things[0]; }
    [RelayCommand] private void PreviousThing() { var index = SelectedThing is null ? -1 : Things.IndexOf(SelectedThing); if (index > 0) SelectedThing = Things[index - 1]; }
    [RelayCommand] private void NextThing() { var index = SelectedThing is null ? -1 : Things.IndexOf(SelectedThing); if (index >= 0 && index < Things.Count - 1) SelectedThing = Things[index + 1]; }
    [RelayCommand] private void LastThing() { if (Things.Count > 0) SelectedThing = Things[^1]; }
    [RelayCommand] private void PreviousFrame() => Frame = Frame <= 0 ? FrameMaximum : Frame - 1;
    [RelayCommand] private void NextFrame() => Frame = Frame >= FrameMaximum ? 0 : Frame + 1;

    [RelayCommand]
    private void ToggleAnimation()
    {
        if (FrameMaximum <= 0)
        {
            StopAnimation();
            return;
        }

        IsPlaying = !IsPlaying;
        if (IsPlaying)
        {
            UpdateAnimationInterval();
            _animationTimer.Start();
        }
        else
        {
            _animationTimer.Stop();
        }
    }

    [RelayCommand]
    private void ApplyDurationToAll()
    {
        var group = CurrentGroup;
        if (group is null) return;
        EnsureFrameDurations(group);
        var duration = GetCurrentDuration();
        foreach (var item in group.FrameDurations)
        {
            item.Min = duration.Min;
            item.Max = duration.Max;
        }
        MarkChanged("Zastosowano czas klatki do całej animacji.");
    }

    [RelayCommand]
    private void AddFrameGroup()
    {
        var thing = CurrentThing;
        if (thing is null || !CanEditFrameGroups) return;
        var clone = CloneGroup(CurrentGroup ?? CreateDefaultGroup());
        clone.GroupType = thing.FrameGroups.Any(group => group.GroupType == 1) ? 0 : 1;
        thing.FrameGroups.Add(clone);
        SelectedGroup = thing.FrameGroups.Count - 1;
        RefreshFrameGroups();
        OnCurrentGroupChanged();
        MarkChanged("Dodano grupę klatek.");
    }

    [RelayCommand]
    private void RemoveFrameGroup()
    {
        var thing = CurrentThing;
        if (thing is null || !CanRemoveFrameGroup) return;
        thing.FrameGroups.RemoveAt(Math.Clamp(SelectedGroup, 0, thing.FrameGroups.Count - 1));
        SelectedGroup = Math.Clamp(SelectedGroup, 0, thing.FrameGroups.Count - 1);
        RefreshFrameGroups();
        OnCurrentGroupChanged();
        MarkChanged("Usunięto grupę klatek.");
    }

    [RelayCommand] private void PreviousSpritePage() { if (SpritePage > 0) SpritePage--; }
    [RelayCommand] private void NextSpritePage() { if (SpritePage < SpritePageCount - 1) SpritePage++; }

    [RelayCommand]
    private void JumpToSprite()
    {
        if (_spriteStore.Data is null) return;
        SpriteJumpId = Math.Clamp(SpriteJumpId, 1, MaxSpriteId);
        SpritePage = (SpriteJumpId - 1) / SpritePageSize;
        SelectedAvailableSprite = AvailableSprites.FirstOrDefault(sprite => sprite.Id == (uint)SpriteJumpId);
    }

    [RelayCommand]
    private void AssignSprite()
    {
        var slot = SelectedSpriteSlot;
        var sprite = SelectedAvailableSprite;
        if (slot is null || sprite is null)
        {
            StatusText = "Wybierz kafelek na siatce i sprite z biblioteki po prawej.";
            return;
        }

        AssignSpriteToSlot(slot, sprite.Id);
    }

    public void AssignSpriteToSlot(LegacySpriteSlotItem slot, uint spriteId)
    {
        var group = CurrentGroup;
        if (group is null || !SpriteSlots.Contains(slot) || slot.Index < 0 || slot.Index >= group.SpriteIds.Length)
        {
            StatusText = "Nie można przypisać sprite'a: wskazany kafelek nie należy do bieżącego widoku.";
            return;
        }

        group.SpriteIds[slot.Index] = spriteId;
        slot.SpriteId = spriteId;
        SelectedSpriteSlot = slot;
        RefreshThingThumbnail();
        RefreshPreview();
        MarkChanged($"Przypisano sprite #{spriteId}: kolumna {slot.GridColumn + 1}, wiersz {slot.GridRow + 1}, warstwa {slot.Layer + 1}.");
    }

    [RelayCommand]
    private void ClearSpriteSlot()
    {
        var group = CurrentGroup;
        var slot = SelectedSpriteSlot;
        if (group is null || slot is null || slot.Index >= group.SpriteIds.Length) return;
        group.SpriteIds[slot.Index] = 0;
        slot.SpriteId = 0;
        RefreshThingThumbnail();
        RefreshPreview();
        MarkChanged($"Wyczyszczono slot {slot.Index}.");
    }

    [RelayCommand]
    private void ApplyProperties()
    {
        if (CurrentThing is null) return;
        RefreshThingThumbnail();
        NotifyPropertyEditor();
        MarkChanged("Zastosowano właściwości obiektu w pamięci. Zapisz Tibia.dat, aby utrwalić zmiany.");
    }

    partial void OnSearchTextChanged(string value) => RefreshThingList(true);

    partial void OnSelectedThingChanged(LegacyThingListItem? value)
    {
        StopAnimation();
        SelectedGroup = 0;
        Direction = Math.Min(2, DirectionMaximum);
        Addon = 0;
        PatternZ = 0;
        Frame = 0;
        CurrentLayer = 0;
        RefreshFrameGroups();
        OnCurrentGroupChanged();
        NotifyPropertyEditor();
        NotifyCategoryState();
        OnPropertyChanged(nameof(HasSelectedThing));
        OnPropertyChanged(nameof(CurrentObjectTitle));
        OnPropertyChanged(nameof(SuggestedExportName));
        OnPropertyChanged(nameof(DeletionWillRenumberIds));
        OnPropertyChanged(nameof(ObjectPosition));
    }

    partial void OnSelectedGroupChanged(int value)
    {
        var safe = Math.Clamp(value, 0, GroupMaximum);
        if (value != safe) { SelectedGroup = safe; return; }
        Direction = Math.Clamp(Direction, 0, DirectionMaximum);
        Addon = Math.Clamp(Addon, 0, AddonMaximum);
        PatternZ = Math.Clamp(PatternZ, 0, PatternZMaximum);
        Frame = Math.Clamp(Frame, 0, FrameMaximum);
        OnCurrentGroupChanged();
    }

    partial void OnDirectionChanged(int value) { NotifyDirectionState(); RefreshSpriteSlots(); RefreshPreview(); }
    partial void OnAddonChanged(int value) { RefreshSpriteSlots(); RefreshPreview(); }
    partial void OnPatternZChanged(int value) { RefreshSpriteSlots(); RefreshPreview(); }
    partial void OnFrameChanged(int value)
    {
        RefreshSpriteSlots();
        RefreshPreview();
        OnPropertyChanged(nameof(FrameDurationMin));
        OnPropertyChanged(nameof(FrameDurationMax));
        OnPropertyChanged(nameof(AnimationPosition));
    }
    partial void OnCurrentLayerChanged(int value)
    {
        var safe = Math.Clamp(value, 0, LayerMaximum);
        if (value != safe) { CurrentLayer = safe; return; }
        OnPropertyChanged(nameof(EditedLayerNumber));
        OnPropertyChanged(nameof(LayerPosition));
        RefreshSpriteSlots();
    }

    partial void OnSelectedSpriteSlotChanged(LegacySpriteSlotItem? oldValue, LegacySpriteSlotItem? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    partial void OnZoomChanged(int value)
    {
        if (value != Math.Clamp(value, 1, 8)) { Zoom = Math.Clamp(value, 1, 8); return; }
        OnPropertyChanged(nameof(PreviewDisplayWidth));
        OnPropertyChanged(nameof(PreviewDisplayHeight));
    }

    partial void OnShowGridChanged(bool value) => RefreshPreview();
    partial void OnShowCropSizeChanged(bool value) => RefreshPreview();
    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayButtonText));
    partial void OnSpritePageChanged(int value) => RefreshSpriteBrowser();
    partial void OnLegacyExtendedSpritesChanged(bool value) => OnPropertyChanged(nameof(LegacyOptionsSummary));
    partial void OnLegacyTransparencyChanged(bool value) => OnPropertyChanged(nameof(LegacyOptionsSummary));
    partial void OnLegacyImprovedAnimationsChanged(bool value)
    {
        OnPropertyChanged(nameof(LegacyOptionsSummary));
        OnPropertyChanged(nameof(ShowImprovedAnimationSettings));
    }
    partial void OnLegacyFrameGroupsChanged(bool value)
    {
        OnPropertyChanged(nameof(LegacyOptionsSummary));
        OnPropertyChanged(nameof(CanEditFrameGroups));
        OnPropertyChanged(nameof(CanRemoveFrameGroup));
        RefreshFrameGroups();
    }
    partial void OnLegacyMetadataFormatChanged(string value)
    {
        OnPropertyChanged(nameof(LegacyOptionsSummary));
        NotifyCategoryState();
    }
    partial void OnLegacyProtocolChanged(string value) => OnPropertyChanged(nameof(LegacyOptionsSummary));

    public void StopAnimation()
    {
        _animationTimer.Stop();
        IsPlaying = false;
    }

    private void AdvanceAnimation()
    {
        if (!IsPlaying || FrameMaximum <= 0) { StopAnimation(); return; }
        Frame = Frame >= FrameMaximum ? 0 : Frame + 1;
        UpdateAnimationInterval();
    }

    private void UpdateAnimationInterval()
    {
        var duration = GetCurrentDuration();
        var milliseconds = duration.Min == 0 && duration.Max == 0 ? 150 : (int)((duration.Min + duration.Max) / 2);
        _animationTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(milliseconds, 40, 2_000));
    }

    private void RefreshAllData()
    {
        NotifyCategoryState();
        RefreshThingList();
        RefreshSpriteBrowser();
        OnPropertyChanged(nameof(HasLoadedDat));
        OnPropertyChanged(nameof(HasLoadedSprites));
        OnPropertyChanged(nameof(MaxSpriteId));
        OnPropertyChanged(nameof(SpritePageCount));
        OnPropertyChanged(nameof(SpritePageSummary));
    }

    private void RefreshThingList(bool preserveSelection = false)
    {
        var selectedSource = preserveSelection ? SelectedThing?.Source : null;
        Things.Clear();
        var category = (DatThingCategory)ActiveCategory;
        var query = SearchText.Trim();
        foreach (var thing in _datService.GetCategory(category))
        {
            if (query.Length > 0 && !thing.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) && !thing.ActiveFlagsText().Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            Things.Add(new LegacyThingListItem(thing, _thumbnailCache));
        }

        SelectedThing = selectedSource is null
            ? Things.FirstOrDefault()
            : Things.FirstOrDefault(item => ReferenceEquals(item.Source, selectedSource)) ?? Things.FirstOrDefault();
        OnPropertyChanged(nameof(ObjectPosition));
    }

    private void RefreshFrameGroups()
    {
        FrameGroups.Clear();
        var thing = CurrentThing;
        if (thing is null) return;
        for (var index = 0; index < thing.FrameGroups.Count; index++)
        {
            var group = thing.FrameGroups[index];
            var sharedOutfitGroup = thing.Category == DatThingCategory.Outfits &&
                                    !LegacyFrameGroups &&
                                    thing.FrameGroups.Count == 1;
            FrameGroups.Add(new LegacyFrameGroupListItem(
                index,
                sharedOutfitGroup
                    ? "Idle + Walking (wspólna)"
                    : group.GroupType == 1 ? "Walking / ruch" : "Idle / bezczynność",
                Math.Max(1, (int)group.Frames)));
        }
    }

    private void RefreshSpriteSlots()
    {
        SpriteSlots.Clear();
        SelectedSpriteSlot = null;
        var group = CurrentGroup;
        if (group is null) return;
        var width = Math.Max(1, (int)group.Width);
        var height = Math.Max(1, (int)group.Height);
        var layers = Math.Max(1, (int)group.Layers);
        var layer = Math.Clamp(CurrentLayer, 0, layers - 1);
        var patternPosition = LegacyDirectionMapping.ToPatternPosition(
            CurrentThing?.Category ?? DatThingCategory.Items,
            Direction,
            Addon,
            group.PatternX,
            group.PatternY);

        // DAT zapisuje kafle od prawego dolnego rogu. Kolekcja jest odwrócona,
        // aby siatka montażowa odpowiadała dokładnie temu, co renderuje podgląd.
        for (var tileY = height - 1; tileY >= 0; tileY--)
        for (var tileX = width - 1; tileX >= 0; tileX--)
        {
            var index = DatParser.CalculateSpriteIndex(
                group,
                tileX,
                tileY,
                layer,
                patternPosition.X,
                patternPosition.Y,
                PatternZ,
                Frame);
            var spriteId = index >= 0 && index < group.SpriteIds.Length ? group.SpriteIds[index] : 0;
            var gridColumn = width - tileX - 1;
            var gridRow = height - tileY - 1;
            SpriteSlots.Add(new LegacySpriteSlotItem(index, spriteId, tileX, tileY, gridColumn, gridRow, layer, _thumbnailCache));
        }
        SelectedSpriteSlot = SpriteSlots.FirstOrDefault();
    }

    private void RefreshSpriteBrowser()
    {
        AvailableSprites.Clear();
        SelectedAvailableSprite = null;
        var count = _spriteStore.Data?.Sprites.Count ?? 0;
        var safePage = Math.Clamp(SpritePage, 0, SpritePageCount - 1);
        if (safePage != SpritePage) { SpritePage = safePage; return; }
        var first = safePage * SpritePageSize + 1;
        var last = Math.Min(count, first + SpritePageSize - 1);
        for (var id = first; id <= last; id++) AvailableSprites.Add(new LegacySpriteListItem((uint)id, _thumbnailCache));
        OnPropertyChanged(nameof(SpritePageCount));
        OnPropertyChanged(nameof(SpritePageSummary));
        OnPropertyChanged(nameof(MaxSpriteId));
    }

    private void RefreshPreview()
    {
        var thing = CurrentThing;
        if (thing is null || _spriteStore.Data is null) { SetPreview(null, "Brak podglądu."); return; }
        var rendered = LegacyThingRenderer.Render(thing, _spriteStore, SelectedGroup, Direction, Addon, PatternZ, Frame);
        if (rendered is null) { SetPreview(null, "Brak danych tekstury dla wybranego obiektu."); return; }
        if (ShowGrid || ShowCropSize) DecoratePreview(rendered.Pixels, rendered.Width, rendered.Height);
        SetPreview(LegacyBitmapFactory.FromBgra(rendered.Pixels, rendered.Width, rendered.Height),
            $"Podgląd: {rendered.Width}×{rendered.Height}px · kierunek {Direction + 1}/{DirectionMaximum + 1} · klatka {Frame + 1}/{FrameMaximum + 1}.");
    }

    private void DecoratePreview(byte[] pixels, int width, int height)
    {
        if (ShowGrid)
        {
            for (var x = 32; x < width; x += 32) DrawLine(pixels, width, height, x, 0, x, height - 1, 255, 190, 50);
            for (var y = 32; y < height; y += 32) DrawLine(pixels, width, height, 0, y, width - 1, y, 255, 190, 50);
        }
        if (ShowCropSize && CurrentGroup is { } group)
        {
            var crop = group.ExactSize == 0 ? Math.Min(width, height) : Math.Min(group.ExactSize, Math.Min(width, height));
            var left = Math.Max(0, width - crop);
            var top = Math.Max(0, height - crop);
            DrawLine(pixels, width, height, left, top, width - 1, top, 60, 210, 255);
            DrawLine(pixels, width, height, left, top, left, height - 1, 60, 210, 255);
            DrawLine(pixels, width, height, width - 1, top, width - 1, height - 1, 60, 210, 255);
            DrawLine(pixels, width, height, left, height - 1, width - 1, height - 1, 60, 210, 255);
        }
    }

    private static void DrawLine(byte[] pixels, int width, int height, int x0, int y0, int x1, int y1, byte blue, byte green, byte red)
    {
        if (x0 == x1)
        {
            for (var y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++) SetPixel(pixels, width, x0, y, blue, green, red);
        }
        else
        {
            for (var x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++) SetPixel(pixels, width, x, y0, blue, green, red);
        }
    }

    private static void SetPixel(byte[] pixels, int width, int x, int y, byte blue, byte green, byte red)
    {
        var index = (y * width + x) * 4;
        if (index < 0 || index + 3 >= pixels.Length) return;
        pixels[index] = blue;
        pixels[index + 1] = green;
        pixels[index + 2] = red;
        pixels[index + 3] = byte.MaxValue;
    }

    private void SetPreview(Bitmap? bitmap, string info)
    {
        var old = PreviewImage;
        PreviewImage = bitmap;
        PreviewInfo = info;
        _previewWidth = bitmap?.PixelSize.Width ?? 0;
        _previewHeight = bitmap?.PixelSize.Height ?? 0;
        OnPropertyChanged(nameof(PreviewDisplayWidth));
        OnPropertyChanged(nameof(PreviewDisplayHeight));
        if (old is not null && !ReferenceEquals(old, bitmap)) old.Dispose();
    }

    private void OnCurrentGroupChanged()
    {
        OnPropertyChanged(nameof(GroupMaximum));
        OnPropertyChanged(nameof(DirectionMaximum));
        OnPropertyChanged(nameof(AddonMaximum));
        OnPropertyChanged(nameof(PatternZMaximum));
        OnPropertyChanged(nameof(FrameMaximum));
        OnPropertyChanged(nameof(LayerMaximum));
        OnPropertyChanged(nameof(LayerCount));
        OnPropertyChanged(nameof(EditedLayerNumber));
        OnPropertyChanged(nameof(LayerPosition));
        OnPropertyChanged(nameof(HasEightDirections));
        OnPropertyChanged(nameof(ShowAddonSelector));
        OnPropertyChanged(nameof(ShowMountSelector));
        OnPropertyChanged(nameof(ShowLayerSelector));
        OnPropertyChanged(nameof(CanEditFrameGroups));
        OnPropertyChanged(nameof(CanRemoveFrameGroup));
        OnPropertyChanged(nameof(TextureWidth));
        OnPropertyChanged(nameof(TextureHeight));
        OnPropertyChanged(nameof(TextureExactSize));
        OnPropertyChanged(nameof(TextureLayers));
        OnPropertyChanged(nameof(TexturePatternX));
        OnPropertyChanged(nameof(TexturePatternY));
        OnPropertyChanged(nameof(TexturePatternZ));
        OnPropertyChanged(nameof(TextureFrames));
        OnPropertyChanged(nameof(CurrentGroupType));
        OnPropertyChanged(nameof(AnimationMode));
        OnPropertyChanged(nameof(AnimationLoopCount));
        OnPropertyChanged(nameof(AnimationStartFrame));
        OnPropertyChanged(nameof(FrameDurationMin));
        OnPropertyChanged(nameof(FrameDurationMax));
        OnPropertyChanged(nameof(AnimationPosition));
        OnPropertyChanged(nameof(ShowAnimationPanel));
        OnPropertyChanged(nameof(ShowImprovedAnimationSettings));
        CurrentLayer = Math.Clamp(CurrentLayer, 0, LayerMaximum);
        NotifyDirectionState();
        RefreshSpriteSlots();
        RefreshPreview();
    }

    private void ChangeSimple(Action<DatThingFrameGroup> change, bool refreshGroups = false)
    {
        var group = CurrentGroup;
        if (group is null) return;
        change(group);
        if (refreshGroups) RefreshFrameGroups();
        MarkChanged("Zmieniono parametry grupy klatek.");
        OnCurrentGroupChanged();
    }

    private void ChangeStructure(Action<DatThingFrameGroup> change)
    {
        var group = CurrentGroup;
        if (group is null) return;
        var before = GroupShape.From(group);
        change(group);
        var after = GroupShape.From(group);
        if (after.Total > MaxEditableSpriteSlots)
        {
            before.Apply(group);
            StatusText = $"Zmiana anulowana: grupa przekroczyłaby limit {MaxEditableSpriteSlots:N0} slotów sprite.";
            OnCurrentGroupChanged();
            return;
        }
        ResizeSpritesPreservingCoordinates(group, before, after);
        ResizeFrameDurations(group, before.Frames, after.Frames);
        Direction = Math.Clamp(Direction, 0, DirectionMaximum);
        Addon = Math.Clamp(Addon, 0, AddonMaximum);
        PatternZ = Math.Clamp(PatternZ, 0, PatternZMaximum);
        Frame = Math.Clamp(Frame, 0, FrameMaximum);
        RefreshThingThumbnail();
        MarkChanged("Zmieniono układ tekstury i dopasowano sloty sprite.");
        OnCurrentGroupChanged();
    }

    private static void ResizeSpritesPreservingCoordinates(DatThingFrameGroup group, GroupShape before, GroupShape after)
    {
        var oldSprites = group.SpriteIds;
        var newSprites = new uint[after.Total];
        for (var frame = 0; frame < Math.Min(before.Frames, after.Frames); frame++)
        for (var patternZ = 0; patternZ < Math.Min(before.PatternZ, after.PatternZ); patternZ++)
        for (var patternY = 0; patternY < Math.Min(before.PatternY, after.PatternY); patternY++)
        for (var patternX = 0; patternX < Math.Min(before.PatternX, after.PatternX); patternX++)
        for (var layer = 0; layer < Math.Min(before.Layers, after.Layers); layer++)
        for (var tileY = 0; tileY < Math.Min(before.Height, after.Height); tileY++)
        for (var tileX = 0; tileX < Math.Min(before.Width, after.Width); tileX++)
        {
            var oldIndex = CalculateIndex(before, tileX, tileY, layer, patternX, patternY, patternZ, frame);
            var newIndex = CalculateIndex(after, tileX, tileY, layer, patternX, patternY, patternZ, frame);
            if (oldIndex < oldSprites.Length && newIndex < newSprites.Length) newSprites[newIndex] = oldSprites[oldIndex];
        }
        group.SpriteIds = newSprites;
    }

    private static int CalculateIndex(GroupShape shape, int tileX, int tileY, int layer, int patternX, int patternY, int patternZ, int frame)
    {
        var index = frame;
        index = index * shape.PatternZ + patternZ;
        index = index * shape.PatternY + patternY;
        index = index * shape.PatternX + patternX;
        index = index * shape.Layers + layer;
        index = index * shape.Height + tileY;
        index = index * shape.Width + tileX;
        return index;
    }

    private static void ResizeFrameDurations(DatThingFrameGroup group, int oldFrames, int newFrames)
    {
        var source = group.FrameDurations;
        var durations = new DatFrameDuration[newFrames];
        for (var index = 0; index < newFrames; index++)
        {
            durations[index] = index < Math.Min(oldFrames, source.Length)
                ? new DatFrameDuration { Min = source[index].Min, Max = source[index].Max }
                : new DatFrameDuration { Min = 100, Max = 100 };
        }
        group.FrameDurations = durations;
    }

    private (uint Min, uint Max) GetCurrentDuration()
    {
        var group = CurrentGroup;
        if (group is null) return (100, 100);
        EnsureFrameDurations(group);
        var duration = group.FrameDurations[Math.Clamp(Frame, 0, group.FrameDurations.Length - 1)];
        return (duration.Min, duration.Max);
    }

    private void SetCurrentDuration(uint? minimum, uint? maximum)
    {
        var group = CurrentGroup;
        if (group is null) return;
        EnsureFrameDurations(group);
        var duration = group.FrameDurations[Math.Clamp(Frame, 0, group.FrameDurations.Length - 1)];
        if (minimum.HasValue) duration.Min = minimum.Value;
        if (maximum.HasValue) duration.Max = maximum.Value;
        if (duration.Max < duration.Min) duration.Max = duration.Min;
        SetDirty(dat: true);
        OnPropertyChanged(nameof(FrameDurationMin));
        OnPropertyChanged(nameof(FrameDurationMax));
        UpdateAnimationInterval();
    }

    private static void EnsureFrameDurations(DatThingFrameGroup group)
    {
        var frames = Math.Max(1, (int)group.Frames);
        if (group.FrameDurations.Length != frames) ResizeFrameDurations(group, group.FrameDurations.Length, frames);
    }

    private void SetRenderOrder(int order)
    {
        var thing = CurrentThing;
        if (thing is null) return;
        thing.IsGroundBorder = order == 1;
        thing.IsOnBottom = order == 2;
        thing.IsOnTop = order == 3;
        NotifyPropertyEditor();
        SetDirty(dat: true);
    }

    private void NotifyPropertyEditor()
    {
        OnPropertyChanged(nameof(IsCommonRenderOrder));
        OnPropertyChanged(nameof(IsGroundBorderRenderOrder));
        OnPropertyChanged(nameof(IsBottomRenderOrder));
        OnPropertyChanged(nameof(IsTopRenderOrder));
        OnPropertyChanged(nameof(HasBones));
        OnPropertyChanged(nameof(BoneOffsetX));
        OnPropertyChanged(nameof(BoneOffsetY));
        OnPropertyChanged(nameof(LensHelpSelectionIndex));
        OnPropertyChanged(nameof(EquipSlotSelectionIndex));
        OnPropertyChanged(nameof(DefaultActionSelectionIndex));
        OnPropertyChanged(nameof(MarketCategorySelectionIndex));
        OnPropertyChanged(nameof(LightColorSelectionIndex));
        OnPropertyChanged(nameof(AutomapColorSelectionIndex));
        OnPropertyChanged(nameof(ShowImprovedAnimationSettings));
    }

    private void NotifyDirectionState()
    {
        OnPropertyChanged(nameof(IsNorth));
        OnPropertyChanged(nameof(IsEast));
        OnPropertyChanged(nameof(IsSouth));
        OnPropertyChanged(nameof(IsWest));
        OnPropertyChanged(nameof(IsNorthEast));
        OnPropertyChanged(nameof(IsSouthEast));
        OnPropertyChanged(nameof(IsSouthWest));
        OnPropertyChanged(nameof(IsNorthWest));
        OnPropertyChanged(nameof(BoneOffsetX));
        OnPropertyChanged(nameof(BoneOffsetY));
    }

    private int GetBoneOffset(bool yAxis)
    {
        var offsets = CurrentThing?.BoneOffsets;
        var index = GetBonePairIndex(Direction) + (yAxis ? 1 : 0);
        return offsets is not null && index < offsets.Length ? offsets[index] : 0;
    }

    private void SetBoneOffset(bool yAxis, int value)
    {
        var thing = CurrentThing;
        if (thing is null) return;
        if (thing.BoneOffsets.Length < 8)
        {
            var offsets = thing.BoneOffsets;
            Array.Resize(ref offsets, 8);
            thing.BoneOffsets = offsets;
        }
        var index = GetBonePairIndex(Direction) + (yAxis ? 1 : 0);
        thing.BoneOffsets[index] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        SetDirty(dat: true);
        OnPropertyChanged(yAxis ? nameof(BoneOffsetY) : nameof(BoneOffsetX));
    }

    private static int GetBonePairIndex(int direction) => direction switch
    {
        0 => 0,
        1 => 4,
        2 => 2,
        3 => 6,
        _ => 0
    };

    private void NotifyCategoryState()
    {
        OnPropertyChanged(nameof(TabItems));
        OnPropertyChanged(nameof(TabOutfits));
        OnPropertyChanged(nameof(TabEffects));
        OnPropertyChanged(nameof(TabMissiles));
        OnPropertyChanged(nameof(CanEditFrameGroups));
        OnPropertyChanged(nameof(CanRemoveFrameGroup));
        OnPropertyChanged(nameof(IsItemCategory));
        OnPropertyChanged(nameof(IsOutfitCategory));
        OnPropertyChanged(nameof(IsEffectCategory));
        OnPropertyChanged(nameof(IsMissileCategory));
        OnPropertyChanged(nameof(ShowPropertiesSection));
        OnPropertyChanged(nameof(ShowDirectionSelector));
        OnPropertyChanged(nameof(ShowAddonSelector));
        OnPropertyChanged(nameof(ShowMountSelector));
        OnPropertyChanged(nameof(ShowLayerSelector));
        OnPropertyChanged(nameof(ShowFlagsSection));
        OnPropertyChanged(nameof(SupportsGroundBorder));
        OnPropertyChanged(nameof(SupportsWallHooks));
        OnPropertyChanged(nameof(SupportsDontHide));
        OnPropertyChanged(nameof(SupportsTranslucent));
        OnPropertyChanged(nameof(SupportsCharges));
        OnPropertyChanged(nameof(SupportsFloorChange));
        OnPropertyChanged(nameof(SupportsEquip));
        OnPropertyChanged(nameof(SupportsMarket));
        OnPropertyChanged(nameof(SupportsDefaultAction));
        OnPropertyChanged(nameof(SupportsNoMoveAnimation));
        OnPropertyChanged(nameof(SupportsUseable));
        OnPropertyChanged(nameof(SupportsWrappable));
        OnPropertyChanged(nameof(SupportsTopEffect));
        OnPropertyChanged(nameof(CanPersistWrappable));
        OnPropertyChanged(nameof(CanPersistTopEffect));
        OnPropertyChanged(nameof(SupportsBones));
        OnPropertyChanged(nameof(CanPersistBones));
        OnPropertyChanged(nameof(SupportsPatternZ));
        OnPropertyChanged(nameof(ObjectOffsetMinimum));
        OnPropertyChanged(nameof(ObjectOffsetMaximum));
    }

    private DatThingType[] GetSelectedThingSources()
    {
        var selected = _selectedThings
            .Where(Things.Contains)
            .Select(item => item.Source)
            .Distinct()
            .ToArray();
        return selected.Length > 0
            ? selected
            : SelectedThing is null ? [] : [SelectedThing.Source];
    }

    private void EnsureThingCapacity(int additionalCount)
    {
        var category = (DatThingCategory)ActiveCategory;
        var currentCount = _datService.GetCategory(category).Count;
        var maximumCount = category == DatThingCategory.Items
            ? ushort.MaxValue - 99
            : ushort.MaxValue;
        if ((long)currentCount + additionalCount > maximumCount)
        {
            throw new InvalidOperationException($"Kategoria {category} osiągnęłaby limit ID {ushort.MaxValue}.");
        }
    }

    private void FinishObjectMutation(DatThingType selectedSource)
    {
        _thumbnailCache.Clear();
        SearchText = string.Empty;
        NotifyCategoryState();
        RefreshThingList();
        SelectedThing = Things.FirstOrDefault(item => ReferenceEquals(item.Source, selectedSource));
        SetSelectedThings(SelectedThing is null ? [] : [SelectedThing]);
    }

    private void SetIndexedProperty(
        int value,
        int minimum,
        int maximum,
        Action<DatThingType, int> setter,
        string propertyName)
    {
        if (CurrentThing is not { } thing) return;
        setter(thing, Math.Clamp(value, minimum, maximum));
        OnPropertyChanged(propertyName);
        MarkChanged("Zmieniono właściwości obiektu.");
    }

    private void RefreshThingThumbnail()
    {
        if (CurrentThing is null || SelectedThing is null) return;
        _thumbnailCache.InvalidateThing(CurrentThing);
        SelectedThing.Refresh();
    }

    private void MarkChanged(string message)
    {
        SetDirty(dat: true);
        StatusText = message;
    }

    private void SetDirty(bool dat = false, bool spr = false)
    {
        _datDirty |= dat;
        _sprDirty |= spr;
        UpdateDirtyState();
    }

    private void UpdateDirtyState() => HasUnsavedChanges = _datDirty || _sprDirty;

    private void ValidateDatSprPair()
    {
        var validation = LegacyAssetPairValidator.Validate(
            _datService.Data,
            _datService.Options,
            _spriteStore.Data);
        if (!validation.IsCompatible)
        {
            throw new InvalidDataException(validation.Error);
        }
    }

    private static DatParserOptions BuildPreferredDatOptions(LegacyClientConfiguration configuration, string datPath)
    {
        var signature = ReadDatSignature(datPath);
        var format = DatParser.GuessMetadataFormat(signature);
        var protocol = LegacyObjectBuilderProtocolCatalog.Resolve(format, signature);
        return new DatParserOptions
        {
            ExtendedSprites = configuration.ExtendedSprites ?? protocol.ExtendedSprites,
            ImprovedAnimations = configuration.ImprovedAnimations ?? protocol.ImprovedAnimations,
            FrameGroups = configuration.FrameGroups ?? protocol.FrameGroups,
            MetadataFormat = format,
            AllowPartial = false
        };
    }

    private void ApplyDetectedDatOptions(DatParserOptions options)
    {
        LegacyExtendedSprites = options.ExtendedSprites;
        LegacyImprovedAnimations = options.ImprovedAnimations;
        LegacyFrameGroups = options.FrameGroups;
        LegacyMetadataFormat = options.MetadataFormat.ToDisplayName();
        UpdateDetectedProtocol();
    }

    private void UpdateDetectedProtocol()
    {
        if (_datService.Data is null)
        {
            LegacyProtocol = "auto";
            return;
        }

        LegacyProtocol = LegacyObjectBuilderProtocolCatalog.Resolve(
            _datService.Options.MetadataFormat,
            _datService.Data.Signature,
            _spriteStore.Data?.Signature).DisplayName;
    }

    private static uint ReadDatSignature(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < sizeof(uint))
        {
            throw new InvalidDataException("Plik jest za mały, aby zawierał sygnaturę Tibia.dat.");
        }

        return reader.ReadUInt32();
    }

    private static DatThingFrameGroup CreateDefaultGroup() => new()
    {
        Width = 1, Height = 1, ExactSize = 32, Layers = 1, PatternX = 1, PatternY = 1, PatternZ = 1, Frames = 1,
        SpriteIds = [0], FrameDurations = [new DatFrameDuration { Min = 100, Max = 100 }]
    };

    private static DatThingFrameGroup CloneGroup(DatThingFrameGroup source) => new()
    {
        GroupType = source.GroupType, Width = source.Width, Height = source.Height, ExactSize = source.ExactSize,
        Layers = source.Layers, PatternX = source.PatternX, PatternY = source.PatternY, PatternZ = source.PatternZ,
        Frames = source.Frames, AnimationMode = source.AnimationMode, LoopCount = source.LoopCount, StartFrame = source.StartFrame,
        SpriteIds = [.. source.SpriteIds],
        FrameDurations = source.FrameDurations.Select(duration => new DatFrameDuration { Min = duration.Min, Max = duration.Max }).ToArray()
    };

    private static byte ToDimension(int value) => (byte)Math.Clamp(value, 1, byte.MaxValue);

    private static string CategoryDisplayName(DatThingCategory category) => category switch
    {
        DatThingCategory.Items => "Przedmiot",
        DatThingCategory.Outfits => "Strój",
        DatThingCategory.Effects => "Efekt",
        DatThingCategory.Missiles => "Pocisk",
        _ => "Obiekt"
    };

    private static string BuildConfigurationStatus(LegacyClientConfiguration configuration) =>
        configuration.SourcePath is null ? string.Empty : $" Ustawienia: {Path.GetFileName(configuration.SourcePath)}.";

    private string BuildLoadedStatus(string prefix)
    {
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(_datService.Data?.ParseWarning))
        {
            warnings.Add($"DAT wczytany częściowo. {_datService.Data.ParseWarning}");
        }
        if (!string.IsNullOrWhiteSpace(_spriteStore.Data?.ParseWarning))
        {
            warnings.Add(_spriteStore.Data.ParseWarning);
        }
        warnings.AddRange(LegacyAssetPairValidator.Validate(
            _datService.Data,
            _datService.Options,
            _spriteStore.Data).Warnings);

        return warnings.Count == 0
            ? $"{prefix} {LegacyOptionsSummary}"
            : $"{prefix} UWAGA: {string.Join(" ", warnings)} {LegacyOptionsSummary}";
    }

    private static TopLevel? GetTopLevel()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }) return TopLevel.GetTopLevel(window);
        return null;
    }

    private static async Task<string?> PickOpenFileAsync(string title, IEnumerable<string> patterns)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null) return null;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate(title), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(LocalizationManager.Translate("Pliki")) { Patterns = patterns.ToArray() }]
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private static async Task<string[]> PickOpenFilesAsync(
        string title,
        IEnumerable<string> patterns,
        string? fileTypeName = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null) return [];
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate(title),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(fileTypeName ??
                    (patterns.Any(pattern => pattern.EndsWith(".aec", StringComparison.OrdinalIgnoreCase))
                        ? "Assets Editor Container"
                        : "Object Builder Data"))
                {
                    Patterns = patterns.ToArray()
                }
            ]
        });
        return files.Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    private static async Task<string?> PickFolderAsync(string title)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null) return null;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = LocalizationManager.Translate(title), AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static async Task<string?> PickSaveFileAsync(string title, string defaultName)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null) return null;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = LocalizationManager.Translate(title), SuggestedFileName = defaultName });
        return file?.TryGetLocalPath();
    }

    private static string? FindLegacyFile(string folder, string? configuredName, string preferredName, string pattern)
    {
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            var configuredPath = Path.IsPathRooted(configuredName) ? configuredName : Path.Combine(folder, configuredName);
            if (File.Exists(configuredPath)) return configuredPath;
        }
        var preferred = Path.Combine(folder, preferredName);
        if (File.Exists(preferred)) return preferred;
        return Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string((string.IsNullOrWhiteSpace(value) ? "object" : value.Trim())
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "object" : sanitized;
    }

    private static string CategoryFileName(DatThingCategory category) => category switch
    {
        DatThingCategory.Items => "item",
        DatThingCategory.Outfits => "outfit",
        DatThingCategory.Effects => "effect",
        DatThingCategory.Missiles => "missile",
        _ => "object"
    };

    private readonly record struct GroupShape(int Width, int Height, int Layers, int PatternX, int PatternY, int PatternZ, int Frames)
    {
        public int Total => checked(Width * Height * Layers * PatternX * PatternY * PatternZ * Frames);
        public static GroupShape From(DatThingFrameGroup group) => new(
            Math.Max(1, (int)group.Width), Math.Max(1, (int)group.Height), Math.Max(1, (int)group.Layers),
            Math.Max(1, (int)group.PatternX), Math.Max(1, (int)group.PatternY), Math.Max(1, (int)group.PatternZ),
            Math.Max(1, (int)group.Frames));
        public void Apply(DatThingFrameGroup group)
        {
            group.Width = (byte)Width; group.Height = (byte)Height; group.Layers = (byte)Layers;
            group.PatternX = (byte)PatternX; group.PatternY = (byte)PatternY; group.PatternZ = (byte)PatternZ; group.Frames = (byte)Frames;
        }
    }
}
