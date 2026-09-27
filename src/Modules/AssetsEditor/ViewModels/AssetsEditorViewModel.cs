using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf;
using Modules.ObjectBuilder.Services;
using Modules.AssetsEditor.Services;
using Modules.AssetsEditor.Views;
using Narzedzia.Contracts.Localization;
using Narzedzia.Core.Appearances;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Interchange;
using Narzedzia.Core.Tibia12;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.ViewModels;

public partial class AssetsEditorViewModel : ObservableObject
{
    private const int SpriteBrowserPageSize = 250;
    private const string SupportedNewSpriteSizes =
        "Dozwolone rozmiary OTClient: szerokość i wysokość 32, 64, 96, 128, 192 albo 384 px.";

    private readonly IAssetsService _assets;

    private readonly List<AppearanceListItem> _allItems    = new();
    private readonly List<AppearanceListItem> _allOutfits  = new();
    private readonly List<AppearanceListItem> _allEffects  = new();
    private readonly List<AppearanceListItem> _allMissiles = new();
    private readonly List<Appearance> _exportAppearances = new();
    private readonly List<AppearanceListItem> _selectedAppearances = [];
    private readonly LegacyObdCodec _obdCodec = new();
    private List<uint>? _frameClipboard;
    private int _textureRequestId;
    private int _slotRequestId;
    private int _spriteBrowserRequestId;
    private int _listThumbnailRequestId;
    private readonly DispatcherTimer _animationTimer;
    private AssetSpriteIndex? _spriteIndex;
    private bool _isLoadingAppearance;
    private bool _isAutoApplying;

    private readonly PresetService _presetService = new();

    public AssetsEditorViewModel(IAssetsService assetsService)
    {
        _assets = assetsService;
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _animationTimer.Tick += (_, _) => AdvanceAnimation();
        SpriteSheetImportLayouts = Enumerable.Range(0, SpriteSheetLayout.SupportedTypeCount)
            .Select(type =>
            {
                var layout = SpriteSheetLayout.FromSpriteType(type);
                return new SpriteSheetLayoutOption(layout.SpriteType, layout.TileWidth, layout.TileHeight);
            })
            .ToArray();

        SheetImportSelectedLayout = SpriteSheetImportLayouts.FirstOrDefault();

        foreach (var p in _presetService.Load())
            AvailablePresets.Add(p);
        SelectedPreset = AvailablePresets.FirstOrDefault();
    }

    // ── Kolekcje publiczne ──────────────────────────────────────────────────────
    public ObservableCollection<AppearanceListItem> Things        { get; } = new();
    public ObservableCollection<SpriteSlotItem>     SpriteSlots   { get; } = new();
    public ObservableCollection<SpriteBrowserItem>  GlobalSprites { get; } = new();
    public ObservableCollection<BoundingBoxItem>    BoundingBoxes { get; } = new();
    public ObservableCollection<AppearanceFrameGroupListItem> TextureFrameGroups { get; } = new();
    public ObservableCollection<NpcSaleEntryViewModel> NpcSaleEntries { get; } = new();

    [ObservableProperty] private NpcSaleEntryViewModel? _selectedNpcSaleEntry;

    // ── Presety serwerów ─────────────────────────────────────────────────────────
    public ObservableCollection<ServerPreset> AvailablePresets { get; } = new();

    [ObservableProperty]
    private ServerPreset? _selectedPreset;

    partial void OnSelectedPresetChanged(ServerPreset? value)
    {
        if (value is null) return;
        // Aktualizacja limitów pól numerycznych po zmianie presetu (override z load resetuje się)
        ShiftMinimum = value.ShiftMin;
        ShiftMaximum = value.ShiftMax;
        FlagShiftX = Math.Clamp(FlagShiftX, ShiftMinimum, ShiftMaximum);
        FlagShiftY = Math.Clamp(FlagShiftY, ShiftMinimum, ShiftMaximum);
        OnPropertyChanged(nameof(MaxLevelValue));
        OnPropertyChanged(nameof(ImbueableSlotMax));
    }

    [ObservableProperty] private int _shiftMinimum = Narzedzia.Core.Appearances.ShiftService.DefaultMin;
    [ObservableProperty] private int _shiftMaximum = Narzedzia.Core.Appearances.ShiftService.DefaultMax;
    public int MaxLevelValue     => SelectedPreset?.MaxLevel           ?? 999;
    public int ImbueableSlotMax  => SelectedPreset?.ImbueableSlotCountMax ?? 3;

    /// <summary>
    /// Jeśli wczytane z pliku wartości shift są poza zakresem aktualnego presetu,
    /// rozszerz limity (nie clampuj), aby zachować dane i ostrzec użytkownika.
    /// </summary>
    private void EnsureShiftRangeCovers(int x, int y)
    {
        var newMin = Math.Min(ShiftMinimum, Math.Min(x, y));
        var newMax = Math.Max(ShiftMaximum, Math.Max(x, y));
        if (newMin < ShiftMinimum || newMax > ShiftMaximum)
        {
            ShiftMinimum = newMin;
            ShiftMaximum = newMax;
            StatusText = $"Załadowano Shift X={x}, Y={y} poza zakresem presetu — limity rozszerzone do [{newMin}..{newMax}].";
        }
    }

    // ── Ścieżka folderu / status ─────────────────────────────────────────────────
    [ObservableProperty] private string _folderPath  = string.Empty;
    [ObservableProperty] private string _statusText  = "Otwórz folder assetów, aby rozpocząć.";
    [ObservableProperty] private string _exportListSummary = "Zaznaczono: 0";

    // ── Wybrana pozycja ──────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseDirections))]
    [NotifyPropertyChangedFor(nameof(CanUseAddons))]
    [NotifyPropertyChangedFor(nameof(CanUsePatternZ))]
    private AppearanceListItem? _selected;
    [ObservableProperty] private string _filter = string.Empty;

    // ── Kategoria ────────────────────────────────────────────────────────────────
    [ObservableProperty] private int _activeCategory;

    // ── Liczniki ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private int _itemCount;
    [ObservableProperty] private int _outfitCount;
    [ObservableProperty] private int _effectCount;
    [ObservableProperty] private int _missileCount;

    public string TabItems    => $"Przedmioty ({ItemCount})";
    public string TabOutfits  => $"Stroje ({OutfitCount})";
    public string TabEffects  => $"Efekty ({EffectCount})";
    public string TabMissiles => $"Pociski ({MissileCount})";

    public bool IsItemsTab    => ActiveCategory == 0;
    public bool IsOutfitsTab  => ActiveCategory == 1;
    public bool IsEffectsTab  => ActiveCategory == 2;
    public bool IsMissilesTab => ActiveCategory == 3;
    public int SelectedAppearanceCount => _selectedAppearances.Count > 0
        ? _selectedAppearances.Count
        : Selected is null ? 0 : 1;

    public void SetSelectedAppearances(IEnumerable<AppearanceListItem> items)
    {
        _selectedAppearances.Clear();
        _selectedAppearances.AddRange(items.Where(Things.Contains).Distinct());
        OnPropertyChanged(nameof(SelectedAppearanceCount));
        RefreshExportListSummary();
    }

    // ══════════════════════════════════════════════════════════════════════════════
    //  PANEL PROPERTIES — tożsamość
    // ══════════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private bool   _hasDetails;
    [ObservableProperty] private uint   _appearanceId;
    [ObservableProperty] private string _appearanceName        = string.Empty;
    [ObservableProperty] private string _appearanceDescription = string.Empty;

    // ── Flagi — lewa kolumna ─────────────────────────────────────────────────────

    // Transparency
    [ObservableProperty] private bool _flagTransparency;
    [ObservableProperty] private int  _flagTransparencyLevel;

    // Ground (Bank = waypoints dla patfindingu)
    [ObservableProperty] private bool _flagGround;
    [ObservableProperty] private int  _flagGroundSpeed;

    [ObservableProperty] private bool _flagClip;
    [ObservableProperty] private bool _flagBottom;
    [ObservableProperty] private bool _flagTop;
    [ObservableProperty] private bool _flagContainer;
    [ObservableProperty] private bool _flagCumulative;
    [ObservableProperty] private bool _flagUsable;
    [ObservableProperty] private bool _flagForceuse;
    [ObservableProperty] private bool _flagMultiuse;

    // Write (możliwość pisania na obiekcie)
    [ObservableProperty] private bool _flagWrite;
    [ObservableProperty] private int  _flagWriteMaxLength;
    [ObservableProperty] private bool _flagWriteOnce;
    [ObservableProperty] private int  _flagWriteOnceMaxLength;

    [ObservableProperty] private bool _flagLiquidpool;
    [ObservableProperty] private bool _flagUnpass;
    [ObservableProperty] private bool _flagUnmove;
    [ObservableProperty] private bool _flagUnsight;
    [ObservableProperty] private bool _flagAvoid;
    [ObservableProperty] private bool _flagNoMovementAnimation;
    [ObservableProperty] private bool _flagTake;
    [ObservableProperty] private bool _flagLiquidcontainer;
    [ObservableProperty] private bool _flagHang;

    // Hook (wieszak na ścianę)
    // HookMode jest jedynym wyborem w UI (radio-group); pozostałe pola są
    // utrzymywane jako pochodne dla zachowania binarnej kompatybilności z proto.
    [ObservableProperty] private bool _flagHook;
    [ObservableProperty] private HookMode _hookMode = HookMode.None;
    [ObservableProperty] private int  _flagHookDirection;
    [ObservableProperty] private bool _flagHookSouth;
    [ObservableProperty] private bool _flagHookEast;

    // Radio-binding helpers (jeden RadioButton per opcja).
    public bool HookModeIsNone
    {
        get => HookMode == HookMode.None;
        set { if (value) HookMode = HookMode.None; }
    }
    public bool HookModeIsSouth
    {
        get => HookMode == HookMode.South;
        set { if (value) HookMode = HookMode.South; }
    }
    public bool HookModeIsEast
    {
        get => HookMode == HookMode.East;
        set { if (value) HookMode = HookMode.East; }
    }

    partial void OnHookModeChanged(HookMode value)
    {
        // Synchronizacja pól pochodnych — emitowanych zarówno w UI jak i w proto.
        FlagHookDirection = (int)value;
        FlagHookSouth = value == HookMode.South;
        FlagHookEast  = value == HookMode.East;
        OnPropertyChanged(nameof(HookModeIsNone));
        OnPropertyChanged(nameof(HookModeIsSouth));
        OnPropertyChanged(nameof(HookModeIsEast));
    }

    partial void OnFlagHookChanged(bool value)
    {
        // Wyłączenie haka resetuje radio do None.
        if (!value && HookMode != HookMode.None)
        {
            HookMode = HookMode.None;
        }
        // Włączenie haka bez wybranego kierunku domyślnie ustawia South.
        else if (value && HookMode == HookMode.None)
        {
            HookMode = HookMode.South;
        }
    }

    [ObservableProperty] private bool _flagRotate;

    // Light (świecenie)
    [ObservableProperty] private bool _flagLight;
    [ObservableProperty] private int  _flagLightBrightness;
    [ObservableProperty] private int  _flagLightColor;

    [ObservableProperty] private bool _flagDontHide;
    [ObservableProperty] private bool _flagTranslucent;

    // Shift (przesunięcie sprite'a)
    [ObservableProperty] private bool _flagShift;
    [ObservableProperty] private int  _flagShiftX;
    [ObservableProperty] private int  _flagShiftY;

    // Height (uniesienie — np. dla mebli)
    [ObservableProperty] private bool _flagHeight;
    [ObservableProperty] private int  _flagElevation;

    // Reverse Addons (dla outfitów)
    [ObservableProperty] private bool _flagReverseAddonsEast;
    [ObservableProperty] private bool _flagReverseAddonsWest;
    [ObservableProperty] private bool _flagReverseAddonsSouth;
    [ObservableProperty] private bool _flagReverseAddonsNorth;

    // ── Flagi — prawa kolumna ────────────────────────────────────────────────────

    [ObservableProperty] private bool _flagLyingObject;
    [ObservableProperty] private bool _flagAnimateAlways;

    // AutoMap (kolor na mapie)
    [ObservableProperty] private bool _flagAutomap;
    [ObservableProperty] private int  _flagAutomapColor;

    // LensHelp (podpowiedź dla soczewki)
    [ObservableProperty] private bool _flagLenshelp;
    [ObservableProperty] private int  _flagLenshelpId;

    [ObservableProperty] private bool _flagFullbank;
    [ObservableProperty] private bool _flagIgnoreLook;

    // Clothes (slot ekwipunku)
    [ObservableProperty] private bool _flagClothes;
    [ObservableProperty] private int  _flagClothesSlot;

    // Default Action (domyślna akcja gracza)
    [ObservableProperty] private bool _flagDefaultAction;
    [ObservableProperty] private int  _flagDefaultActionIndex;

    // Market (rynek przedmiotów)
    [ObservableProperty] private bool   _flagMarket;
    [ObservableProperty] private int    _flagMarketCategory;
    [ObservableProperty] private int    _flagMarketTradeAs;
    [ObservableProperty] private int    _flagMarketShowAs;
    [ObservableProperty] private int    _flagMarketMinLevel;
    [ObservableProperty] private bool   _flagMarketProfAny;
    [ObservableProperty] private bool   _flagMarketProfNone;
    [ObservableProperty] private bool   _flagMarketProfKnight;
    [ObservableProperty] private bool   _flagMarketProfPaladin;
    [ObservableProperty] private bool   _flagMarketProfSorcerer;
    [ObservableProperty] private bool   _flagMarketProfDruid;
    [ObservableProperty] private bool   _flagMarketProfPromoted;

    // Nazwa i opis wyglądu
    [ObservableProperty] private string _appearanceNameField  = string.Empty;
    [ObservableProperty] private string _appearanceDescField  = string.Empty;

    [ObservableProperty] private bool _flagWrap;
    [ObservableProperty] private bool _flagUnwrap;
    [ObservableProperty] private bool _flagTopeffect;
    [ObservableProperty] private bool _flagDecoItemKit;

    // Changed To Expire (zamiana po wygaśnięciu)
    [ObservableProperty] private bool _flagChangedtoexpire;
    [ObservableProperty] private int  _flagChangedToExpireFormerId;

    [ObservableProperty] private bool _flagCorpse;
    [ObservableProperty] private bool _flagPlayerCorpse;
    [ObservableProperty] private bool _flagNpcSaleData;
    [ObservableProperty] private bool _flagShowOffSocket;
    [ObservableProperty] private bool _flagReportable;

    // Upgrade Classification (ulepszenie klasyfikacji)
    [ObservableProperty] private bool _flagUpgradeclassification;
    [ObservableProperty] private int  _flagUpgradeClassificationAmount;

    [ObservableProperty] private bool _flagWearout;
    [ObservableProperty] private bool _flagClockexpire;
    [ObservableProperty] private bool _flagExpire;
    [ObservableProperty] private bool _flagExpirestop;

    // Cyclopedia
    [ObservableProperty] private bool _flagCyclopedia;
    [ObservableProperty] private int  _flagCyclopediaType;

    [ObservableProperty] private bool _flagAmmo;

    // SkillWheel Gem
    [ObservableProperty] private bool _flagSkillwheelGem;
    [ObservableProperty] private int  _flagGemQualityId;
    [ObservableProperty] private int  _flagGemVocId;

    [ObservableProperty] private bool _flagDualWielding;

    // Imbueable (można imbuować)
    [ObservableProperty] private bool _flagImbueable;
    [ObservableProperty] private int  _flagImbueableSlotCount;

    // Proficiency (biegłość)
    [ObservableProperty] private bool _flagProficiency;
    [ObservableProperty] private int  _flagProficiencyId;

    [ObservableProperty] private int _flagMinimumLevel;

    [ObservableProperty] private int  _flagWeaponTypeIndex;

    // Restrict Vocation (ograniczenie do profesji)
    [ObservableProperty] private bool _flagRestrictVocAny;
    [ObservableProperty] private bool _flagRestrictVocNone;
    [ObservableProperty] private bool _flagRestrictVocKnight;
    [ObservableProperty] private bool _flagRestrictVocPaladin;
    [ObservableProperty] private bool _flagRestrictVocSorcerer;
    [ObservableProperty] private bool _flagRestrictVocDruid;
    [ObservableProperty] private bool _flagRestrictVocMonk;
    [ObservableProperty] private bool _flagRestrictVocPromoted;

    // ══════════════════════════════════════════════════════════════════════════════
    //  PANEL TEXTURE — dane sprite'ów
    // ══════════════════════════════════════════════════════════════════════════════
    [ObservableProperty] private int  _propGroups;
    [ObservableProperty] private int  _propLayers;
    [ObservableProperty] private int  _propTileWidth;
    [ObservableProperty] private int  _propTileHeight;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseDirections))]
    [NotifyPropertyChangedFor(nameof(MaxDirectionIndex))]
    private int  _propPatternX;
    [ObservableProperty] private int  _propPatternY;
    [ObservableProperty] private int  _propPatternZ;
    [ObservableProperty] private int  _propFrames;
    [ObservableProperty] private int  _propBoundingSquare;
    [ObservableProperty] private bool _propIsOpaque;

    [ObservableProperty] private int  _selectedGroupIndex;
    [ObservableProperty] private int  _currentFrameIndex;
    [ObservableProperty] private int  _currentFrameCount;
    [ObservableProperty] private int  _currentDirection;
    [ObservableProperty] private int  _currentAddonIndex;
    [ObservableProperty] private int  _currentPatternZIndex;
    [ObservableProperty] private bool _blendLayers = true;
    [ObservableProperty] private bool _showAllAddons;
    [ObservableProperty] private bool _outfitColorize = true;
    [ObservableProperty] private int _outfitHeadColor;
    [ObservableProperty] private int _outfitBodyColor;
    [ObservableProperty] private int _outfitLegsColor;
    [ObservableProperty] private int _outfitFeetColor;
    [ObservableProperty] private SpriteSlotItem? _selectedSpriteSlot;
    [ObservableProperty] private SpriteBrowserItem? _selectedGlobalSprite;
    [ObservableProperty] private uint _spriteIdToAssign;
    [ObservableProperty] private uint _spriteSearchId;
    [ObservableProperty] private int _spritePageIndex;
    [ObservableProperty] private string _spritePageInfo = "Lista sprite'ów: brak wczytanych assets.";
    [ObservableProperty] private string _textureSummary = "Brak wybranego obiektu.";
    [ObservableProperty] private string _currentDirectionName = "Góra";

    // Import sprite sheetu
    [ObservableProperty] private string _sheetImportFilePath = string.Empty;
    [ObservableProperty] private Bitmap? _sheetImportPreview;
    [ObservableProperty] private string _sheetImportSummary = "Wybierz plik sprite sheetu i ustaw siatkę cięcia.";
    [ObservableProperty] private SpriteSheetLayoutOption? _sheetImportSelectedLayout;
    [ObservableProperty] private int _sheetImportSpriteType;
    [ObservableProperty] private string _newSpritesSummary = "Przeciągnij pliki sprite albo wybierz je z dysku.";
    [ObservableProperty] private string _newSpritesValidationMessage = SupportedNewSpriteSizes;

    public IReadOnlyList<SpriteSheetLayoutOption> SpriteSheetImportLayouts { get; }
    public ObservableCollection<NewSpriteImportItem> PendingNewSprites { get; } = new();

    // Animacja
    [ObservableProperty] private bool _hasAnimation;
    [ObservableProperty] private int  _animDefaultStartPhase;
    [ObservableProperty] private bool _animRandomStartPhase;
    [ObservableProperty] private bool _animSynchronized;
    [ObservableProperty] private int  _animLoopTypeIndex;
    [ObservableProperty] private int  _animLoopCount;
    [ObservableProperty] private int  _animCurrentFrameMin;
    [ObservableProperty] private int  _animCurrentFrameMax;

    // ── Podgląd ────────────────────────────────────────────────────────────────
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private string _previewInfo = "Brak podglądu.";
    [ObservableProperty] private int _previewPixelWidth;
    [ObservableProperty] private int _previewPixelHeight;
    [ObservableProperty] private bool _isPlaying;

    public bool CanUseDirections => Selected is { Category: APPEARANCE_TYPE.AppearanceOutfit } && PropPatternX > 1;
    public bool CanUseAddons => PropPatternY > 1;
    public bool CanUsePatternZ => PropPatternZ > 1;
    public bool CanUseFrames => PropFrames > 1;
    public bool IsDirectionUp => CurrentDirection == 0;
    public bool IsDirectionRight => CurrentDirection == 1;
    public bool IsDirectionDown => CurrentDirection == 2;
    public bool IsDirectionLeft => CurrentDirection == 3;
    public int MaxGroupIndex => Math.Max(0, PropGroups - 1);
    public int MaxDirectionIndex => Math.Max(0, PropPatternX - 1);
    public int MaxAddonIndex => Math.Max(0, PropPatternY - 1);
    public int MaxPatternZIndex => Math.Max(0, PropPatternZ - 1);
    public int MaxFrameIndex => Math.Max(0, PropFrames - 1);
    public string FrameLabel => $"{Math.Min(CurrentFrameIndex + 1, Math.Max(1, PropFrames))}/{Math.Max(1, PropFrames)}";
    public string PlayButtonText => IsPlaying ? "⏸ Pauza" : "▶ Odtwórz";
    public IReadOnlyList<string> AnimationLoopTypeLabels { get; } = ["Ping-pong", "Bez końca", "Określona liczba"];
    public IReadOnlyList<string> FrameGroupTypeLabels { get; } = ["Idle", "Walking", "ObjectInitial"];
    public IReadOnlyList<string> LenshelpTypeLabels { get; } =
    [
        "Ladders", "Sewer Grates", "Dungeon Floor", "Levers", "Doors",
        "Special Doors", "Stairs", "Mailboxes", "Depot Boxes",
        "Dustbins", "Stone Piles", "Signs", "Books and Scrolls"
    ];
    public IReadOnlyList<string> ClothesSlotLabels { get; } =
    [
        "None", "Helmet", "Amulet", "Backpack", "Armor", "Shield",
        "Weapon", "Legs", "Boots", "Ring", "Arrow"
    ];
    public IReadOnlyList<string> DefaultActionLabels { get; } = ["None", "Look", "Use", "Open", "Highlight"];
    public IReadOnlyList<string> MarketCategoryLabels { get; } =
    [
        "Armors", "Amulets", "Boots", "Containers", "Decoration", "Food",
        "Helmets Hats", "Legs", "Others", "Potions", "Rings", "Runes",
        "Shields", "Tools", "Valuables", "Ammunition", "Axes", "Clubs",
        "Distance Weapons", "Swords", "Wands Rods", "Premium Scrolls", "Tibia Coins",
        "Creature Products", "Quiver", "SoulCores", "FistWeapons"
    ];
    public IReadOnlyList<string> WeaponTypeLabels { get; } =
    [
        "No Weapon", "Sword", "Axe", "Club", "Fist", "Bow",
        "CrossBow", "Wand/Rod", "Throw"
    ];
    public IReadOnlyList<TibiaColorOption> TibiaColorOptions { get; } = Enumerable.Range(0, 216)
        .Select(TibiaColorOption.FromId)
        .ToArray();

    public int FlagLenshelpSelectionIndex
    {
        get => FlagLenshelpId is >= 1100 and <= 1112 ? FlagLenshelpId - 1100 : -1;
        set
        {
            if (value is >= 0 and < 13)
            {
                FlagLenshelpId = value + 1100;
            }
        }
    }

    partial void OnFlagLenshelpIdChanged(int value) =>
        OnPropertyChanged(nameof(FlagLenshelpSelectionIndex));

    public int AnimLoopTypeSelectionIndex
    {
        get => AnimLoopTypeIndex switch
        {
            -1 => 0,
            1 => 2,
            _ => 1
        };
        set => AnimLoopTypeIndex = value switch
        {
            0 => -1,
            2 => 1,
            _ => 0
        };
    }

    public int SelectedFrameGroupTypeIndex
    {
        get
        {
            var group = Selected?.Source is { } appearance
                ? AppearanceTextureLayout.GetFrameGroup(appearance, SelectedGroupIndex)
                : null;

            return group?.FixedFrameGroup switch
            {
                FIXED_FRAME_GROUP.OutfitMoving => 1,
                FIXED_FRAME_GROUP.ObjectInitial => 2,
                _ => 0
            };
        }
        set
        {
            if (Selected?.Source is not { } appearance ||
                AppearanceTextureLayout.GetFrameGroup(appearance, SelectedGroupIndex) is not { } group)
            {
                return;
            }

            group.FixedFrameGroup = value switch
            {
                1 => FIXED_FRAME_GROUP.OutfitMoving,
                2 => FIXED_FRAME_GROUP.ObjectInitial,
                _ => FIXED_FRAME_GROUP.OutfitIdle
            };

            OnPropertyChanged();
            RefreshTextureFrameGroups(appearance);
            RefreshTextureState();
        }
    }

    // ── Other (dump protobuf) ──────────────────────────────────────────────────
    [ObservableProperty] private string _otherFullInfo = string.Empty;

    // ============================================================================
    //  KOMENDY
    // ============================================================================

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var tl = GetTopLevel();
        if (tl is null) return;

        var folders = await tl.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title         = LocalizationManager.Translate("Wybierz folder assetów Tibii (zawiera catalog-content.json)"),
            AllowMultiple = false
        });

        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (path is null) return;

        try
        {
            _assets.LoadFromFolder(path);
            FolderPath = path;
            RebuildLists();
            RebuildSpriteBrowser(resetPage: true);
            ApplyFilter();

            var app = _assets.AppearancesData;
            StatusText = app is null
                ? $"Wczytano katalog, ale brak appearances.dat w: {path}"
                : $"Wczytano: {app.Object.Count} items, {app.Outfit.Count} outfits, " +
                  $"{app.Effect.Count} efektów, {app.Missile.Count} pocisków. [{path}]";

            RefreshRenderedPreview(Selected);
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd wczytywania: {ex.Message}";
        }
    }

    /// <summary>
    /// Pełna kompilacja do załadowanego folderu: backup + atomic write + catalog rewrite.
    /// Replikuje WPF "Compile_Click" — zapisuje wszystkie 3 artefakty (catalog + appearances + .bak).
    /// </summary>
    [RelayCommand]
    private Task CompileAsync() => CompileToLoadedFolderAsync();

    public async Task CompileToLoadedFolderAsync()
    {
        if (_assets.AppearancesData is null || _assets.FolderPath is null)
        {
            StatusText = "Brak wczytanych danych — najpierw otwórz folder z assetami.";
            return;
        }

        try
        {
            // Apply pending edits do bieżącej appearance przed zapisem.
            if (Selected?.Source is { } appearance)
            {
                ApplyCurrentAppearanceChanges(appearance);
            }

            var folder = _assets.FolderPath;
            var catalogEntries = _assets.Catalog.ToList();
            var appEntry = catalogEntries.FirstOrDefault(e =>
                string.Equals(e.Type, "appearances", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    "Brak wpisu typu 'appearances' w catalog-content.json — nie wiadomo gdzie zapisać appearances.dat.");

            var catalogPath = Path.Combine(folder, "catalog-content.json");
            var appearancesPath = Path.Combine(folder, appEntry.File);
            var catalogBakPath = catalogPath + "-bak";
            var appearancesBakPath = appearancesPath + "-bak";

            // 1) Backup obu plików — overwrite=true, zgodnie z WPF.
            if (File.Exists(catalogPath))     File.Copy(catalogPath, catalogBakPath, overwrite: true);
            if (File.Exists(appearancesPath)) File.Copy(appearancesPath, appearancesBakPath, overwrite: true);

            // 2) ProcessTransparentSheets — alpha mutation w arkuszach LZMA dla obiektów
            //    z ustawioną Flags.Transparencylevel. Wykonujemy PRZED zapisem catalogu,
            //    bo procesor może (jak WPF) modyfikować wpisy katalogu.
            TransparentSheetProcessor.ProcessReport? transparencyReport = null;
            using (var store = _assets.CreateSpriteStore())
            {
                transparencyReport = new TransparentSheetProcessor().Process(_assets.AppearancesData, store);
            }

            // 3) Atomic write appearances.dat (primary).
            new Narzedzia.Core.Appearances.AppearancesReader().WriteAtomic(appearancesPath, _assets.AppearancesData);

            // 4) Atomic write catalog-content.json (secondary). Jeśli failuje — przywróć appearances z .bak.
            try
            {
                Narzedzia.Core.Assets.CatalogWriter.WriteAtomic(catalogPath, catalogEntries);
            }
            catch
            {
                if (File.Exists(appearancesBakPath))
                {
                    File.Copy(appearancesBakPath, appearancesPath, overwrite: true);
                }
                throw;
            }

            var app = _assets.AppearancesData;
            var transparencyMsg = transparencyReport is { ProcessedTiles: > 0 } r
                ? $" Transparency: {r.ProcessedTiles} tile w {r.ModifiedSheets} arkuszach."
                : string.Empty;
            StatusText = $"Skompilowano: {app.Object.Count} obiektów, {app.Outfit.Count} strojów, " +
                         $"{app.Effect.Count} efektów, {app.Missile.Count} pocisków " +
                         $"(4 pliki zapisane do {folder}).{transparencyMsg}";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd kompilacji: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAppearancesAsAsync()
    {
        if (_assets.AppearancesData is null)
        {
            StatusText = "Brak wczytanych danych.";
            return;
        }

        var path = await PickSaveFileAsync("Zapisz appearances.dat jako…", "appearances.dat");
        if (path is null) return;

        try
        {
            if (Selected?.Source is { } appearance)
            {
                ApplyCurrentAppearanceChanges(appearance);
            }

            new Narzedzia.Core.Appearances.AppearancesReader().Write(path, _assets.AppearancesData);
            StatusText = $"Zapisano do {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd zapisu: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveCurrentObject()
    {
        if (Selected?.Source is not { } appearance)
        {
            StatusText = "Nie wybrano obiektu.";
            return;
        }

        ApplyCurrentAppearanceChanges(appearance);
        RefreshTextureState();
        OtherFullInfo = appearance.ToString();
        StatusText = $"Zapisano zmiany obiektu #{appearance.Id} w pamięci.";
    }

    [RelayCommand]
    private void ApplyAppearanceId()
    {
        if (Selected?.Source is not { } appearance || _assets.AppearancesData is not { } data)
        {
            StatusText = "Nie wybrano obiektu.";
            return;
        }

        if (AppearanceId == 0)
        {
            StatusText = "ID obiektu musi być większe od zera.";
            AppearanceId = appearance.Id;
            return;
        }

        IEnumerable<Appearance> category = Selected.Category switch
        {
            APPEARANCE_TYPE.AppearanceOutfit => data.Outfit,
            APPEARANCE_TYPE.AppearanceEffect => data.Effect,
            APPEARANCE_TYPE.AppearanceMissile => data.Missile,
            _ => data.Object
        };

        if (category.Any(candidate =>
                !ReferenceEquals(candidate, appearance) && candidate.Id == AppearanceId))
        {
            StatusText = $"ID #{AppearanceId} jest już zajęte w tej kategorii.";
            AppearanceId = appearance.Id;
            return;
        }

        var previousId = appearance.Id;
        appearance.Id = AppearanceId;
        Selected = null;
        RebuildLists();
        ApplyFilter();
        Selected = Things.FirstOrDefault(item => ReferenceEquals(item.Source, appearance));
        StatusText = previousId == appearance.Id
            ? $"ID obiektu pozostaje bez zmian: #{appearance.Id}."
            : $"Zmieniono ID obiektu z #{previousId} na #{appearance.Id}.";
    }

    [RelayCommand]
    private void AddNpcSaleEntry()
    {
        var entry = new NpcSaleEntryViewModel();
        AttachNpcSaleEntry(entry);
        NpcSaleEntries.Add(entry);
        SelectedNpcSaleEntry = entry;
        FlagNpcSaleData = true;
        TryAutoPersistCurrentAppearance(nameof(FlagNpcSaleData));
    }

    [RelayCommand]
    private void RemoveNpcSaleEntry()
    {
        if (SelectedNpcSaleEntry is null)
        {
            StatusText = "Wybierz wpis sprzedaży NPC do usunięcia.";
            return;
        }

        var removedIndex = NpcSaleEntries.IndexOf(SelectedNpcSaleEntry);
        DetachNpcSaleEntry(SelectedNpcSaleEntry);
        NpcSaleEntries.Remove(SelectedNpcSaleEntry);
        SelectedNpcSaleEntry = NpcSaleEntries.Count == 0
            ? null
            : NpcSaleEntries[Math.Clamp(removedIndex, 0, NpcSaleEntries.Count - 1)];
        FlagNpcSaleData = NpcSaleEntries.Count > 0;
        TryAutoPersistCurrentAppearance(nameof(FlagNpcSaleData));
    }

    [RelayCommand]
    private void ClearNpcSaleEntries()
    {
        ClearNpcSaleEntryCollection();
        FlagNpcSaleData = false;
        TryAutoPersistCurrentAppearance(nameof(FlagNpcSaleData));
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (_assets.AppearancesData is null)
        {
            StatusText = "Brak wczytanych danych.";
            return;
        }

        var path = await PickSaveFileAsync("Eksport do CSV", "appearances_export.csv");
        if (path is null) return;

        try
        {
            var lines = new List<string> { "Id;Category;Name;FirstSpriteId" };
            void Add(IEnumerable<AppearanceListItem> list, string cat)
            {
                foreach (var it in list)
                    lines.Add($"{it.Id};{cat};\"{it.Source.Name}\";{it.FirstSpriteId}");
            }

            Add(_allItems,    "Object");
            Add(_allOutfits,  "Outfit");
            Add(_allEffects,  "Effect");
            Add(_allMissiles, "Missile");

            await File.WriteAllLinesAsync(path, lines, System.Text.Encoding.UTF8);
            StatusText = $"Wyeksportowano {lines.Count - 1} wpisów do {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd eksportu: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddCurrentToExport()
    {
        var selectedItems = GetSelectedAppearanceItems();
        if (selectedItems.Length == 0)
        {
            StatusText = "Wybierz obiekt do eksportu .aec.";
            return;
        }

        if (Selected?.Source is { } current) ApplyCurrentAppearanceChanges(current);
        foreach (var item in selectedItems)
        {
            item.Source.AppearanceType = item.Category;
            if (!_exportAppearances.Contains(item.Source)) _exportAppearances.Add(item.Source);
        }

        RefreshExportListSummary();
        StatusText = $"Dodano do listy eksportu: {selectedItems.Length} obiektów.";
    }

    [RelayCommand]
    private void ClearExportList()
    {
        _exportAppearances.Clear();
        RefreshExportListSummary();
        StatusText = "Wyczyszczono listę eksportu .aec.";
    }

    [RelayCommand]
    private async Task ExportAecAsync()
    {
        if (_assets.AppearancesData is null)
        {
            StatusText = "Najpierw wczytaj folder assetów.";
            return;
        }

        var selectedItems = GetSelectedAppearanceItems();
        var toExport = selectedItems.Length > 0
            ? selectedItems.Select(item =>
            {
                item.Source.AppearanceType = item.Category;
                return item.Source;
            }).ToArray()
            : _exportAppearances.ToArray();

        if (toExport.Length == 0)
        {
            StatusText = "Brak obiektów do eksportu .aec.";
            return;
        }

        var path = await PickSaveFileAsync("Eksportuj kontener .aec", "appearance_export.aec");
        if (path is null) return;

        try
        {
            if (Selected?.Source is { } current) ApplyCurrentAppearanceChanges(current);
            var container = AppearanceContainerService.BuildExportContainer(toExport, ReadAssetSpritePayload);
            await using var output = File.Create(path);
            container.WriteTo(output);
            StatusText = $"Wyeksportowano {toExport.Length} obiektów ze sprite'ami, offsetem i flagami do AEC v2: {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd eksportu .aec: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportAecAsync()
    {
        if (_assets.AppearancesData is null)
        {
            StatusText = "Najpierw wczytaj folder assetów.";
            return;
        }

        var paths = await PickOpenFilesAsync("Importuj kontenery .aec", "Kontener appearance .aec", ["*.aec"]);
        if (paths.Length == 0) return;

        try
        {
            var containers = new List<Appearances>(paths.Length);
            var embeddedCount = 0;
            foreach (var path in paths)
            {
                await using var input = File.OpenRead(path);
                var container = Appearances.Parser.ParseFrom(input);
                // Pełny odczyt przed modyfikacją sprawdza nagłówki, rozmiary i indeksy.
                embeddedCount += AecContainerCodec.ReadEmbeddedAppearances(container).Count;
                containers.Add(container);
            }

            if (embeddedCount == 0)
            {
                StatusText = "Wybrane kontenery AEC nie zawierają obiektów.";
                return;
            }

            using var store = _assets.CreateSpriteStore();
            var allocator = new SpriteImportAllocator(store);
            var imported = new List<Appearance>(embeddedCount);
            foreach (var container in containers)
            {
                imported.AddRange(AppearanceContainerService.ImportContainer(
                    container,
                    (AecSpritePayload payload) => allocator.AddSprite(payload)));
            }
            allocator.Save();

            foreach (var appearance in imported)
            {
                AddAppearanceToCategory(appearance);
            }

            RebuildLists();
            ApplyFilter();
            SelectImportedCategory(imported[^1]);
            Selected = Things.FirstOrDefault(item => ReferenceEquals(item.Source, imported[^1]));
            RefreshTextureState();
            StatusText = $"Zaimportowano {imported.Count} obiektów ze sprite'ami, offsetem i flagami zapisanymi w {paths.Length} plikach AEC.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd importu .aec: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportObdAsync()
    {
        if (_assets.AppearancesData is null)
        {
            StatusText = "Najpierw wczytaj folder assetów.";
            return;
        }

        var selectedItems = GetSelectedAppearanceItems();
        if (selectedItems.Length == 0)
        {
            StatusText = "Zaznacz co najmniej jeden obiekt do eksportu OBD.";
            return;
        }

        var folder = await PickFolderAsync("Wybierz folder eksportu OBD");
        if (folder is null) return;

        try
        {
            if (Selected?.Source is { } current) ApplyCurrentAppearanceChanges(current);
            Directory.CreateDirectory(folder);
            var warnings = new List<string>();
            foreach (var item in selectedItems)
            {
                item.Source.AppearanceType = item.Category;
                var conversion = LegacyAppearanceConverter.ToLegacy(
                    item.Source,
                    item.Category,
                    ReadAssetSpritePayload,
                    targetSupportsFrameGroups: false);
                warnings.AddRange(conversion.Warnings);
                var path = Path.Combine(folder, $"{CategoryFileName(item.Category)}_{item.Id}.obd");
                _obdCodec.Write(path, conversion.Thing,
                    id => conversion.TileSprites.TryGetValue(id, out var pixels) ? pixels : null,
                    useFrameGroups: false,
                    preserveFlags: false);
            }

            StatusText = $"Wyeksportowano {selectedItems.Length} obiektów ze sprite'ami i offsetem jako OBD v2 (bez flag) do: {folder}{WarningSuffix(warnings)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd eksportu OBD: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportObdAsync()
    {
        if (_assets.AppearancesData is null)
        {
            StatusText = "Najpierw wczytaj folder assetów.";
            return;
        }

        var paths = await PickOpenFilesAsync("Importuj obiekty OBD", "Object Builder Data", ["*.obd"]);
        if (paths.Length == 0) return;

        try
        {
            var documents = paths
                .Select(path => _obdCodec.Read(path, preserveFlags: false))
                .ToArray();
            var conversions = documents.Select(document =>
            {
                var legacyPixels = BuildObdSpriteLookup(document);
                return LegacyAppearanceConverter.ToAppearance(
                    document.Thing,
                    id => legacyPixels.TryGetValue(id, out var pixels) ? pixels : null,
                    sourceUsesFrameGroups: document.UsesFrameGroups);
            }).ToArray();

            using var store = _assets.CreateSpriteStore();
            var allocator = new SpriteImportAllocator(store);
            var imported = new List<Appearance>(conversions.Length);
            var warnings = new List<string>();
            foreach (var conversion in conversions)
            {
                RemapModernSprites(conversion, allocator);
                warnings.AddRange(conversion.Warnings);
                imported.Add(conversion.Appearance);
            }
            allocator.Save();

            foreach (var appearance in imported) AddAppearanceToCategory(appearance);
            _assets.InvalidateSpriteCache();
            RebuildLists();
            ApplyFilter();
            SelectImportedCategory(imported[^1]);
            Selected = Things.FirstOrDefault(item => ReferenceEquals(item.Source, imported[^1]));
            RefreshTextureState();
            StatusText = $"Zaimportowano {imported.Count} obiektów OBD ze złożonymi sprite'ami i offsetem (bez flag).{WarningSuffix(warnings)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd importu OBD: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SetActiveCategory(string cat)
    {
        if (int.TryParse(cat, out var v)) ActiveCategory = v;
    }

    [RelayCommand]
    private void SetDirection(string direction)
    {
        if (!int.TryParse(direction, out var value))
        {
            return;
        }

        CurrentDirection = Math.Clamp(value, 0, MaxDirectionIndex);
        CurrentDirectionName = CurrentDirection switch
        {
            1 => "Prawo",
            2 => "Dół",
            3 => "Lewo",
            _ => "Góra"
        };
    }

    [RelayCommand]
    private void PreviousFrame()
    {
        if (PropFrames <= 1)
        {
            CurrentFrameIndex = 0;
            return;
        }

        CurrentFrameIndex = CurrentFrameIndex <= 0 ? MaxFrameIndex : CurrentFrameIndex - 1;
    }

    [RelayCommand]
    private void NextFrame()
    {
        if (PropFrames <= 1)
        {
            CurrentFrameIndex = 0;
            return;
        }

        CurrentFrameIndex = CurrentFrameIndex >= MaxFrameIndex ? 0 : CurrentFrameIndex + 1;
    }

    [RelayCommand]
    private void ToggleAnimation()
    {
        if (IsPlaying)
        {
            StopAnimation();
            return;
        }

        if (PropFrames <= 1)
        {
            StatusText = "Wybrana grupa ma tylko jedną klatkę.";
            return;
        }

        IsPlaying = true;
        UpdateAnimationInterval();
        _animationTimer.Start();
    }

    private void AdvanceAnimation()
    {
        if (!IsPlaying || PropFrames <= 1)
        {
            StopAnimation();
            return;
        }

        CurrentFrameIndex = CurrentFrameIndex >= MaxFrameIndex ? 0 : CurrentFrameIndex + 1;
        UpdateAnimationInterval();
    }

    private void StopAnimation()
    {
        _animationTimer.Stop();
        IsPlaying = false;
    }

    private void UpdateAnimationInterval()
    {
        var phases = GetSelectedSpriteInfo()?.Animation?.SpritePhase;
        var delay = 150;
        if (phases is { Count: > 0 })
        {
            var phase = phases[Math.Clamp(CurrentFrameIndex, 0, phases.Count - 1)];
            var min = phase.DurationMin == 0 ? 100u : phase.DurationMin;
            var max = phase.DurationMax < min ? min : phase.DurationMax;
            delay = (int)Math.Clamp(((long)min + max) / 2L, 16L, 60_000L);
        }

        _animationTimer.Interval = TimeSpan.FromMilliseconds(delay);
    }

    [RelayCommand]
    private void NewAppearance()
    {
        if (_assets.AppearancesData is null)
        {
            StatusText = "Najpierw wczytaj folder assetów.";
            return;
        }

        var appearance = CreateDefaultAppearance(GetActiveAppearanceType(), NextIdForActiveCategory());
        AddAppearanceToActiveCategory(appearance);
        RebuildLists();
        ApplyFilter();
        Selected = ActiveList().FirstOrDefault(item => item.Source == appearance);
        StatusText = $"Dodano nowy obiekt #{appearance.Id}.";
    }

    [RelayCommand]
    private void DuplicateAppearance()
    {
        if (_assets.AppearancesData is null || Selected is null)
        {
            StatusText = "Nie ma czego duplikować.";
            return;
        }

        var duplicate = Selected.Source.Clone();
        duplicate.Id = NextIdForActiveCategory();
        AddAppearanceToActiveCategory(duplicate);
        RebuildLists();
        ApplyFilter();
        Selected = ActiveList().FirstOrDefault(item => item.Source == duplicate);
        StatusText = $"Zduplikowano obiekt jako #{duplicate.Id}.";
    }

    [RelayCommand]
    private void DeleteAppearance()
    {
        if (_assets.AppearancesData is null || Selected is null)
        {
            StatusText = "Nie ma czego usunąć.";
            return;
        }

        var id = Selected.Id;
        RemoveAppearanceFromActiveCategory(Selected.Source);
        RebuildLists();
        ApplyFilter();
        StatusText = $"Usunięto obiekt #{id}.";
    }

    [RelayCommand]
    private void AddSpriteSlot()
    {
        var spriteInfo = GetSelectedSpriteInfo();
        if (spriteInfo is null)
        {
            StatusText = "Brak wybranej tekstury.";
            return;
        }

        spriteInfo.SpriteId.Add(0);
        RefreshTextureState();
        StatusText = "Dodano pusty slot sprite'a do wybranego obiektu.";
    }

    [RelayCommand]
    private void RemoveSpriteSlot()
    {
        var spriteInfo = GetSelectedSpriteInfo();
        if (spriteInfo is null || SelectedSpriteSlot is null)
        {
            StatusText = "Wybierz slot sprite'a do usunięcia.";
            return;
        }

        if (SelectedSpriteSlot.Index < 0 || SelectedSpriteSlot.Index >= spriteInfo.SpriteId.Count)
        {
            return;
        }

        spriteInfo.SpriteId.RemoveAt(SelectedSpriteSlot.Index);
        RefreshTextureState();
        StatusText = $"Usunięto slot #{SelectedSpriteSlot.Index}.";
    }

    [RelayCommand]
    private void OpenSpriteSheetEditor()
    {
        if (SelectedSpriteSlot is null || SelectedSpriteSlot.SpriteId == 0)
        {
            StatusText = "Wybierz sprite obiektu.";
            return;
        }

        try
        {
            var store = _assets.CreateSpriteStore();
            var window = new SpriteSheetEditorWindow
            {
                DataContext = new SpriteSheetEditorViewModel(store, SelectedSpriteSlot.SpriteId)
            };

            window.Closed += (_, _) =>
            {
                _assets.InvalidateSpriteCache();
                RebuildSpriteBrowser(resetPage: false);
                RefreshTextureState();
            };

            if (GetMainWindow() is { } owner)
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }

            StatusText = $"Otworzono manager arkusza dla sprite ID {SelectedSpriteSlot.SpriteId}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Nie można otworzyć managera arkuszy: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PreviousSpritePage()
    {
        if (_spriteIndex is null || SpritePageIndex <= 0)
        {
            return;
        }

        SpritePageIndex--;
        RefreshSpriteBrowserPage();
    }

    [RelayCommand]
    private void FirstSpritePage()
    {
        if (_spriteIndex is null || SpritePageIndex == 0)
        {
            return;
        }

        SpritePageIndex = 0;
        RefreshSpriteBrowserPage();
    }

    [RelayCommand]
    private void NextSpritePage()
    {
        if (_spriteIndex is null)
        {
            return;
        }

        var maxPage = _spriteIndex.TotalPages(SpriteBrowserPageSize) - 1;
        if (SpritePageIndex >= maxPage)
        {
            return;
        }

        SpritePageIndex++;
        RefreshSpriteBrowserPage();
    }

    [RelayCommand]
    private void LastSpritePage()
    {
        if (_spriteIndex is null)
        {
            return;
        }

        var lastPage = Math.Max(0, _spriteIndex.TotalPages(SpriteBrowserPageSize) - 1);
        if (SpritePageIndex == lastPage)
        {
            return;
        }

        SpritePageIndex = lastPage;
        RefreshSpriteBrowserPage();
    }

    [RelayCommand]
    private void SearchGlobalSprite()
    {
        if (_spriteIndex is null || SpriteSearchId == 0)
        {
            StatusText = "Wczytaj assets i podaj ID sprite'a.";
            return;
        }

        SpritePageIndex = _spriteIndex.FindPageForSprite(SpriteSearchId, SpriteBrowserPageSize);
        RefreshSpriteBrowserPage();
        SelectedGlobalSprite = GlobalSprites.FirstOrDefault(sprite => sprite.SpriteId == SpriteSearchId);
        StatusText = SelectedGlobalSprite is null
            ? $"Nie znaleziono sprite #{SpriteSearchId} w katalogu."
            : $"Znaleziono sprite #{SpriteSearchId} na stronie listy sprite'ów.";
    }

    [RelayCommand]
    private void AssignSelectedGlobalSpriteToSlot()
    {
        if (SelectedGlobalSprite is null)
        {
            StatusText = "Wybierz sprite z prawej listy.";
            return;
        }

        if (SelectedSpriteSlot is null)
        {
            StatusText = "Wybierz slot tekstury obiektu.";
            return;
        }

        AssignSpriteToSlot(SelectedSpriteSlot.Index, SelectedGlobalSprite.SpriteId);
    }

    [RelayCommand]
    private void SetLayerCount() =>
        MutateTexture(info => AppearanceTextureMutator.SetLayerCount(info, PropLayers), "Zmieniono liczbę warstw.");

    [RelayCommand]
    private void SetTileWidth() =>
        MutateTexture(info => AppearanceTextureMutator.SetTileWidth(info, PropTileWidth), "Zmieniono szerokość siatki kafli.");

    [RelayCommand]
    private void SetTileHeight() =>
        MutateTexture(info => AppearanceTextureMutator.SetTileHeight(info, PropTileHeight), "Zmieniono wysokość siatki kafli.");

    [RelayCommand]
    private void SetPatternX() =>
        MutateTexture(info => AppearanceTextureMutator.SetPatternX(info, PropPatternX), "Zmieniono pattern X.");

    [RelayCommand]
    private void SetPatternY() =>
        MutateTexture(info => AppearanceTextureMutator.SetPatternY(info, PropPatternY), "Zmieniono pattern Y.");

    [RelayCommand]
    private void SetPatternZ() =>
        MutateTexture(info => AppearanceTextureMutator.SetPatternZ(info, PropPatternZ), "Zmieniono pattern Z.");

    [RelayCommand]
    private void SetFrameCount() =>
        MutateTexture(info => AppearanceTextureMutator.SetFrameCount(info, PropFrames), "Zmieniono liczbę klatek.");

    [RelayCommand]
    private void SetBoundingSquare() =>
        MutateTexture(info => AppearanceTextureMutator.SetBoundingSquare(info, PropBoundingSquare), "Zmieniono bounding square.");

    [RelayCommand]
    private void SetOpaque() =>
        MutateTexture(info => AppearanceTextureMutator.SetOpaque(info, PropIsOpaque), "Zmieniono flagę nieprzezroczystości.");

    [RelayCommand]
    private void AssignSpriteToSelectedSlot()
    {
        if (SelectedSpriteSlot is null)
        {
            StatusText = "Wybierz slot sprite'a.";
            return;
        }

        AssignSpriteToSlot(SelectedSpriteSlot.Index, SpriteIdToAssign);
    }

    [RelayCommand]
    private void ClearSelectedSpriteSlot()
    {
        MutateTexture(info =>
        {
            if (SelectedSpriteSlot is null)
            {
                throw new InvalidOperationException("Wybierz slot sprite'a.");
            }

            AppearanceTextureMutator.AssignSprite(info, SelectedSpriteSlot.Index, 0);
        }, "Wyczyszczono wybrany slot sprite'a.");
    }

    [RelayCommand]
    private void CopyFrame()
    {
        var spriteInfo = GetSelectedSpriteInfo();
        if (spriteInfo is null)
        {
            StatusText = "Brak wybranej tekstury.";
            return;
        }

        _frameClipboard = AppearanceTextureMutator.CopyFrame(spriteInfo, CurrentFrameIndex).ToList();
        StatusText = $"Skopiowano klatkę {CurrentFrameIndex + 1}.";
    }

    [RelayCommand]
    private void PasteFrame()
    {
        if (_frameClipboard is null)
        {
            StatusText = "Najpierw skopiuj klatkę.";
            return;
        }

        MutateTexture(
            info => AppearanceTextureMutator.PasteFrame(info, CurrentFrameIndex, _frameClipboard),
            $"Wklejono klatkę {CurrentFrameIndex + 1}.");
    }

    [RelayCommand]
    private void ClearFrame() =>
        MutateTexture(info => AppearanceTextureMutator.ClearFrame(info, CurrentFrameIndex), $"Wyczyszczono klatkę {CurrentFrameIndex + 1}.");

    [RelayCommand]
    private void ApplyDurationToAllFrames()
    {
        var animation = GetSelectedSpriteInfo()?.Animation;
        if (animation is null)
        {
            StatusText = "Wybrana tekstura nie ma animacji.";
            return;
        }

        AppearanceTextureMutator.ApplyDurationToAllFrames(
            animation,
            ToUInt(AnimCurrentFrameMin),
            ToUInt(AnimCurrentFrameMax));
        RefreshTextureState();
        StatusText = "Zastosowano czas aktualnej klatki do wszystkich klatek.";
    }

    [RelayCommand]
    private void RandomizeOutfitColors()
    {
        OutfitHeadColor = Random.Shared.Next(0, 133);
        OutfitBodyColor = Random.Shared.Next(0, 133);
        OutfitLegsColor = Random.Shared.Next(0, 133);
        OutfitFeetColor = Random.Shared.Next(0, 133);
        RefreshRenderedPreview(Selected);
        StatusText = "Wylosowano kolory stroju.";
    }

    [RelayCommand]
    private async Task PickSpriteSheetAsync()
    {
        var tl = GetTopLevel();
        if (tl is null)
        {
            return;
        }

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz sprite sheet"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationManager.Translate("Obrazy"))
                {
                    Patterns = ["*.png", "*.bmp", "*.jpg", "*.jpeg", "*.webp"]
                }
            ]
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SheetImportFilePath = path;
        RefreshSheetImportPreview();
    }

    [RelayCommand]
    private void ImportSpriteSheet()
    {
        if (!_assets.IsLoaded)
        {
            StatusText = "Najpierw wczytaj folder assetów.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SheetImportFilePath) || !File.Exists(SheetImportFilePath))
        {
            StatusText = "Wybierz poprawny plik sprite sheetu.";
            return;
        }

        try
        {
            using var store = _assets.CreateSpriteStore();

            var spriteType = SheetImportSelectedLayout?.SpriteType ?? SheetImportSpriteType;
            var firstId = store.Catalog
                .Where(entry => string.Equals(entry.Type, "sprite", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.LastSpriteid)
                .DefaultIfEmpty(0)
                .Max() + 1;

            var sheet = store.CreateSheet(spriteType, (uint)Math.Max(1, firstId));
            var importedTiles = store.ImportImagesIntoSheet(sheet, [SheetImportFilePath], 0);
            store.SaveSheet(sheet);
            store.SaveCatalog();

            _assets.InvalidateSpriteCache();
            RebuildSpriteBrowser(resetPage: false);
            SpriteSearchId = sheet.FirstSpriteId;
            SearchGlobalSprite();

            StatusText =
                $"Zaimportowano sprite sheet do nowego arkusza {sheet.File}. Dodano {importedTiles} kafli od sprite #{sheet.FirstSpriteId}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd importu sprite sheetu: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task PickNewSpriteFilesAsync()
    {
        var tl = GetTopLevel();
        if (tl is null)
        {
            return;
        }

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz nowe sprite"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationManager.Translate("Obrazy"))
                {
                    Patterns = ["*.png", "*.bmp", "*.jpg", "*.jpeg"]
                }
            ]
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();

        AddNewSpriteFilesFromPaths(paths);
    }

    [RelayCommand]
    private void ClearPendingNewSprites()
    {
        PendingNewSprites.Clear();
        NewSpritesSummary = "Wyczyszczono listę nowych sprite'ów.";
        NewSpritesValidationMessage = SupportedNewSpriteSizes;
    }

    [RelayCommand]
    private void ImportPendingNewSprites()
    {
        if (!_assets.IsLoaded)
        {
            StatusText = "Najpierw wczytaj folder assetów.";
            return;
        }

        if (PendingNewSprites.Count == 0)
        {
            StatusText = "Dodaj pliki sprite przed importem.";
            return;
        }

        try
        {
            using var store = _assets.CreateSpriteStore();
            var createdSheets = 0;
            var importedSprites = 0;
            var skippedUnsupported = 0;
            uint? firstImportedSpriteId = null;

            foreach (var group in PendingNewSprites.GroupBy(item => (item.Width, item.Height)))
            {
                var spriteType = ResolveSpriteType(group.Key.Width, group.Key.Height);
                if (spriteType is null)
                {
                    skippedUnsupported += group.Count();
                    continue;
                }

                var queue = group.Select(item => item.FullPath).ToList();
                while (queue.Count > 0)
                {
                    var firstSpriteId = store.Catalog
                        .Where(entry => string.Equals(entry.Type, "sprite", StringComparison.OrdinalIgnoreCase))
                        .Select(entry => entry.LastSpriteid)
                        .DefaultIfEmpty(0)
                        .Max() + 1;

                    var sheet = store.CreateSheet(spriteType.Value, (uint)Math.Max(1, firstSpriteId));
                    createdSheets++;

                    var batchCount = Math.Min(sheet.TileCount, queue.Count);
                    var batch = queue.Take(batchCount).ToList();
                    var imported = store.ImportImagesIntoSheet(sheet, batch, 0);
                    if (imported <= 0)
                    {
                        skippedUnsupported += batch.Count;
                        break;
                    }

                    store.SaveSheet(sheet);
                    importedSprites += imported;
                    firstImportedSpriteId ??= sheet.FirstSpriteId;

                    queue.RemoveRange(0, Math.Min(batch.Count, imported));
                }
            }

            if (importedSprites > 0)
            {
                store.SaveCatalog();
                _assets.InvalidateSpriteCache();
                RebuildSpriteBrowser(resetPage: false);
                if (firstImportedSpriteId is { } firstId)
                {
                    SpriteSearchId = firstId;
                    SearchGlobalSprite();
                }
            }

            var importedFiles = PendingNewSprites.Count - skippedUnsupported;
            PendingNewSprites.Clear();

            NewSpritesSummary =
                $"Zaimportowano {importedFiles} plików jako {importedSprites} sprite'ów. Arkusze: {createdSheets}. Pominięto: {skippedUnsupported}.";
            StatusText = importedSprites > 0
                ? "Dodano nowe sprite'y na koniec katalogu i odświeżono listę sprite'ów."
                : "Nie udało się zaimportować nowych sprite'ów. Sprawdź rozmiary plików.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd importu nowych sprite'ów: {ex.Message}";
        }
    }

    public void AddNewSpriteFilesFromPaths(IEnumerable<string> paths, bool sequenceMode = false)
    {
        var added = 0;
        var skipped = 0;
        var validationErrors = new List<string>();
        var knownPaths = PendingNewSprites
            .Select(item => item.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Ctrl-drag = sekwencja klatek: posortuj pliki leksykograficznie po nazwie,
        // aby przyległe sprite ID alokowały się w deterministycznej kolejności.
        var ordered = sequenceMode
            ? paths.OrderBy(p => p ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            : paths;

        foreach (var path in ordered)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                skipped++;
                validationErrors.Add($"{DisplayDroppedFileName(path)} — plik nie istnieje");
                continue;
            }

            if (!IsSupportedSpriteFile(path))
            {
                skipped++;
                validationErrors.Add($"{Path.GetFileName(path)} — nieobsługiwany format");
                continue;
            }

            if (knownPaths.Contains(path))
            {
                skipped++;
                validationErrors.Add($"{Path.GetFileName(path)} — plik jest już w kolejce");
                continue;
            }

            (int Width, int Height)? dimensions;
            try
            {
                var info = Image.Identify(path);
                dimensions = info is null ? null : (info.Width, info.Height);
            }
            catch (Exception)
            {
                skipped++;
                validationErrors.Add($"{Path.GetFileName(path)} — nie można odczytać obrazu");
                continue;
            }

            if (dimensions is not { } size ||
                !SpriteSheetLayout.TryFromDimensions(size.Width, size.Height, out _))
            {
                skipped++;
                var sizeLabel = dimensions is null
                    ? "nieznany rozmiar"
                    : $"{dimensions.Value.Width}×{dimensions.Value.Height} px";
                validationErrors.Add($"{Path.GetFileName(path)} — {sizeLabel}");
                continue;
            }

            PendingNewSprites.Add(new NewSpriteImportItem(
                Path.GetFileName(path),
                path,
                size.Width,
                size.Height));
            knownPaths.Add(path);
            added++;
        }

        var modeNote = sequenceMode ? " (Ctrl: sekwencja klatek)" : string.Empty;
        NewSpritesSummary =
            $"W kolejce: {PendingNewSprites.Count}. Dodano teraz: {added}. Pominięto: {skipped}.{modeNote}";
        NewSpritesValidationMessage = validationErrors.Count == 0
            ? $"Wszystkie dodane pliki są zgodne. {SupportedNewSpriteSizes}"
            : $"Odrzucono: {string.Join("; ", validationErrors.Take(3))}" +
              (validationErrors.Count > 3 ? $"; oraz {validationErrors.Count - 3} kolejnych." : ".") +
              $" {SupportedNewSpriteSizes}";
    }

    private int? ResolveSpriteType(int width, int height)
    {
        return SpriteSheetLayout.TryFromDimensions(width, height, out var layout)
            ? layout.SpriteType
            : null;
    }

    private static string DisplayDroppedFileName(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "Nieznany plik" : Path.GetFileName(path);

    private static bool IsSupportedSpriteFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task CopyOutfitXml()
    {
        if (Selected?.Source is not { } appearance ||
            appearance.AppearanceType != APPEARANCE_TYPE.AppearanceOutfit)
        {
            StatusText = "Wybierz strój, żeby skopiować XML look.";
            return;
        }

        var xml = $"<look type=\"{appearance.Id}\" head=\"{OutfitHeadColor}\" body=\"{OutfitBodyColor}\" legs=\"{OutfitLegsColor}\" feet=\"{OutfitFeetColor}\" addons=\"{CurrentAddonIndex}\"/>";
        var clipboard = GetTopLevel()?.Clipboard;
        if (clipboard is null)
        {
            StatusText = xml;
            return;
        }

        await clipboard.SetTextAsync(xml);
        StatusText = "Skopiowano XML look do schowka.";
    }

    // ============================================================================
    //  REAKCJE NA ZMIANY
    // ============================================================================

    partial void OnActiveCategoryChanged(int value)
    {
        OnPropertyChanged(nameof(IsItemsTab));
        OnPropertyChanged(nameof(IsOutfitsTab));
        OnPropertyChanged(nameof(IsEffectsTab));
        OnPropertyChanged(nameof(IsMissilesTab));
        ApplyFilter();
    }

    partial void OnSelectedChanged(AppearanceListItem? value)
    {
        StopAnimation();
        _isLoadingAppearance = true;
        try
        {
            LoadAppearanceDetails(value?.Source);
            RefreshTextureFrameGroups(value?.Source);
            ResetTextureNavigation(value?.Source);
            OnPropertyChanged(nameof(SelectedGroupIndex));
            RefreshTextureState();
        }
        finally
        {
            _isLoadingAppearance = false;
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSelectedGroupIndexChanged(int value)
    {
        StopAnimation();
        if (Selected is not null)
            RefreshTextureState();

        OnPropertyChanged(nameof(SelectedFrameGroupTypeIndex));
    }

    partial void OnCurrentFrameIndexChanged(int value)
    {
        if (Selected is not null)
        {
            RefreshSpriteInfo(Selected.Source);
        }

        OnPropertyChanged(nameof(FrameLabel));
        if (IsPlaying) UpdateAnimationInterval();
        RefreshRenderedPreview(Selected);
    }

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(PlayButtonText));

    partial void OnCurrentDirectionChanged(int value)
    {
        CurrentDirectionName = value switch
        {
            1 => "Prawo",
            2 => "Dół",
            3 => "Lewo",
            _ => "Góra"
        };
        OnPropertyChanged(nameof(IsDirectionUp));
        OnPropertyChanged(nameof(IsDirectionRight));
        OnPropertyChanged(nameof(IsDirectionDown));
        OnPropertyChanged(nameof(IsDirectionLeft));
        RefreshRenderedPreview(Selected);
    }

    partial void OnCurrentAddonIndexChanged(int value) => RefreshRenderedPreview(Selected);

    partial void OnCurrentPatternZIndexChanged(int value) => RefreshRenderedPreview(Selected);

    partial void OnBlendLayersChanged(bool value) => RefreshRenderedPreview(Selected);

    partial void OnShowAllAddonsChanged(bool value) => RefreshRenderedPreview(Selected);

    partial void OnOutfitColorizeChanged(bool value) => RefreshRenderedPreview(Selected);

    partial void OnOutfitHeadColorChanged(int value) => RefreshRenderedPreview(Selected);

    partial void OnOutfitBodyColorChanged(int value) => RefreshRenderedPreview(Selected);

    partial void OnOutfitLegsColorChanged(int value) => RefreshRenderedPreview(Selected);

    partial void OnOutfitFeetColorChanged(int value) => RefreshRenderedPreview(Selected);

    partial void OnSheetImportSelectedLayoutChanged(SpriteSheetLayoutOption? value)
    {
        if (value is null)
        {
            return;
        }

        SheetImportSpriteType = value.SpriteType;
        RefreshSheetImportPreview();
    }

    partial void OnAnimLoopTypeIndexChanged(int value) => OnPropertyChanged(nameof(AnimLoopTypeSelectionIndex));

    // ============================================================================
    //  PRYWATNE HELPERY
    // ============================================================================

    private void RebuildLists()
    {
        _allItems.Clear();
        _allOutfits.Clear();
        _allEffects.Clear();
        _allMissiles.Clear();

        if (_assets.AppearancesData is { } app)
        {
            // Wczytany plik appearances.dat z CipSoft nie ustawia AppearanceType w polu proto;
            // ustawiamy go na podstawie listy źródłowej, aby rendering/UI mogły rozróżnić outfit/effect/missile.
            foreach (var a in app.Object)
            {
                a.AppearanceType = APPEARANCE_TYPE.AppearanceObject;
                _allItems.Add(new AppearanceListItem(a, APPEARANCE_TYPE.AppearanceObject));
            }
            foreach (var a in app.Outfit)
            {
                a.AppearanceType = APPEARANCE_TYPE.AppearanceOutfit;
                _allOutfits.Add(new AppearanceListItem(a, APPEARANCE_TYPE.AppearanceOutfit));
            }
            foreach (var a in app.Effect)
            {
                a.AppearanceType = APPEARANCE_TYPE.AppearanceEffect;
                _allEffects.Add(new AppearanceListItem(a, APPEARANCE_TYPE.AppearanceEffect));
            }
            foreach (var a in app.Missile)
            {
                a.AppearanceType = APPEARANCE_TYPE.AppearanceMissile;
                _allMissiles.Add(new AppearanceListItem(a, APPEARANCE_TYPE.AppearanceMissile));
            }

            ItemCount    = _allItems.Count;
            OutfitCount  = _allOutfits.Count;
            EffectCount  = _allEffects.Count;
            MissileCount = _allMissiles.Count;
        }

        OnPropertyChanged(nameof(TabItems));
        OnPropertyChanged(nameof(TabOutfits));
        OnPropertyChanged(nameof(TabEffects));
        OnPropertyChanged(nameof(TabMissiles));
    }

    private List<AppearanceListItem> ActiveList() => ActiveCategory switch
    {
        1 => _allOutfits,
        2 => _allEffects,
        3 => _allMissiles,
        _ => _allItems
    };

    private void ApplyFilter()
    {
        var norm = Filter.Trim();
        IEnumerable<AppearanceListItem> src = ActiveList();

        if (!string.IsNullOrWhiteSpace(norm))
            src = src.Where(it =>
                it.Id.ToString().Contains(norm) ||
                it.Source.Name.Contains(norm, StringComparison.OrdinalIgnoreCase));

        Things.Clear();
        foreach (var item in src) Things.Add(item);

        if (Selected is null || !Things.Contains(Selected))
            Selected = Things.FirstOrDefault();

        QueueListThumbnails();
    }

    private void RebuildSpriteBrowser(bool resetPage)
    {
        _spriteIndex = AssetSpriteIndex.FromCatalog(_assets.Catalog);
        if (resetPage)
        {
            SpritePageIndex = 0;
        }

        RefreshSpriteBrowserPage();
    }

    private void RefreshSpriteBrowserPage()
    {
        GlobalSprites.Clear();
        if (_spriteIndex is null || _spriteIndex.TotalSprites == 0)
        {
            SpritePageInfo = "Lista sprite'ów: brak sprite'ów w katalogu.";
            return;
        }

        var totalPages = _spriteIndex.TotalPages(SpriteBrowserPageSize);
        SpritePageIndex = Math.Clamp(SpritePageIndex, 0, totalPages - 1);
        var page = _spriteIndex.GetPage(SpritePageIndex, SpriteBrowserPageSize);
        foreach (var sprite in page)
        {
            GlobalSprites.Add(new SpriteBrowserItem(sprite.SpriteId));
        }

        var first = page.FirstOrDefault()?.SpriteId;
        var last = page.LastOrDefault()?.SpriteId;
        SpritePageInfo = first is null || last is null
            ? $"Strona {SpritePageIndex + 1}/{totalPages}, brak sprite'ów."
            : $"Strona {SpritePageIndex + 1}/{totalPages}: sprite {first}-{last} z {_spriteIndex.TotalSprites}.";

        SelectedGlobalSprite = GlobalSprites.FirstOrDefault(sprite => sprite.SpriteId == SpriteSearchId)
            ?? GlobalSprites.FirstOrDefault();
        QueueSpriteBrowserThumbnails();
    }

    private void QueueSpriteBrowserThumbnails()
    {
        if (!_assets.IsLoaded || GlobalSprites.Count == 0)
        {
            return;
        }

        var requestId = ++_spriteBrowserRequestId;
        var sprites = GlobalSprites.ToList();
        var assets = _assets;

        _ = Task.Run(async () =>
        {
            foreach (var sprite in sprites)
            {
                if (requestId != _spriteBrowserRequestId)
                {
                    return;
                }

                var pixels = assets.GetSpritePixels(sprite.SpriteId);
                var (width, height) = assets.GetSpriteSize(sprite.SpriteId);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (requestId == _spriteBrowserRequestId && GlobalSprites.Contains(sprite))
                    {
                        sprite.Thumbnail = SpriteToBitmap(pixels, width, height);
                    }
                }, DispatcherPriority.Background);
            }
        });
    }

    private void QueueListThumbnails()
    {
        if (!_assets.IsLoaded || Things.Count == 0)
        {
            return;
        }

        var requestId = ++_listThumbnailRequestId;
        var items = Things.Where(item => item.Thumbnail is null).ToList();
        var assets = _assets;

        _ = Task.Run(async () =>
        {
            var renderedCount = 0;
            foreach (var item in items)
            {
                if (requestId != _listThumbnailRequestId)
                {
                    return;
                }

                RenderedSpriteImage? rendered = null;
                try
                {
                    rendered = assets.RenderAppearance(item.Source, BuildListThumbnailOptions(item.Source));
                }
                catch
                {
                    // Miniatury listy nie mogą blokować pracy edytora.
                }

                if (rendered is null)
                {
                    continue;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (requestId == _listThumbnailRequestId && item.Thumbnail is null)
                    {
                        item.Thumbnail = SpriteToBitmap(rendered.Pixels, rendered.Width, rendered.Height);
                    }
                }, DispatcherPriority.Background);

                renderedCount++;
                if (renderedCount % 64 == 0)
                {
                    await Task.Delay(1);
                }
            }
        });
    }

    private static AppearanceRenderOptions BuildListThumbnailOptions(Appearance appearance) =>
        new(
            GroupIndex: 0,
            Direction: appearance.AppearanceType == APPEARANCE_TYPE.AppearanceOutfit ? 2 : 0,
            Addon: 0,
            PatternZ: 0,
            Frame: 0,
            BlendLayers: true,
            FullAddons: false,
            ColorizeOutfit: true,
            HeadColor: 0,
            BodyColor: 0,
            LegsColor: 0,
            FeetColor: 0);

    private void LoadAppearanceDetails(Appearance? a)
    {
        if (a is null)
        {
            HasDetails = false;
            return;
        }

        HasDetails           = true;
        AppearanceId          = a.Id;
        AppearanceName        = a.HasName        ? a.Name        : string.Empty;
        AppearanceDescription = a.HasDescription ? a.Description : string.Empty;

        var f = a.Flags ?? new AppearanceFlags();
        if (a.Flags is null) ClearAllFlags();

        // Transparency
        FlagTransparency      = f.Transparencylevel is not null;
        FlagTransparencyLevel = f.Transparencylevel is { } tl ? (int)tl.Level : 0;

        // Ground
        FlagGround      = f.Bank is not null;
        FlagGroundSpeed = f.Bank is { } bk ? (int)bk.Waypoints : 0;

        FlagClip      = f.Clip;
        FlagBottom    = f.Bottom;
        FlagTop       = f.Top;
        FlagContainer = f.Container;
        FlagCumulative = f.Cumulative;
        FlagUsable    = f.Usable;
        FlagForceuse  = f.Forceuse;
        FlagMultiuse  = f.Multiuse;

        FlagWrite           = f.Write is not null;
        FlagWriteMaxLength  = f.Write is { } wr ? (int)wr.MaxTextLength : 0;
        FlagWriteOnce          = f.WriteOnce is not null;
        FlagWriteOnceMaxLength = f.WriteOnce is { } wo ? (int)wo.MaxTextLengthOnce : 0;

        FlagLiquidpool         = f.Liquidpool;
        FlagUnpass             = f.Unpass;
        FlagUnmove             = f.Unmove;
        FlagUnsight            = f.Unsight;
        FlagAvoid              = f.Avoid;
        FlagNoMovementAnimation = f.NoMovementAnimation;
        FlagTake               = f.Take;
        FlagLiquidcontainer    = f.Liquidcontainer;
        FlagHang               = f.Hang;

        // Hook: priorytet Direction (oryginał WPF) > HookEast > HookSouth (OTClient mehah).
        // Direction=0 (proto default; enum nie definiuje wartości 0) traktujemy jak "nie ustawione".
        var hookDirRaw = f.Hook is { } hk ? (int)hk.Direction : 0;
        HookMode resolvedHook;
        if (hookDirRaw == (int)HOOK_TYPE.East)       resolvedHook = HookMode.East;
        else if (hookDirRaw == (int)HOOK_TYPE.South) resolvedHook = HookMode.South;
        else if (f.HookEast)                         resolvedHook = HookMode.East;
        else if (f.HookSouth)                        resolvedHook = HookMode.South;
        else                                         resolvedHook = HookMode.None;

        FlagHook  = f.Hook is not null || f.HookSouth || f.HookEast;
        HookMode  = resolvedHook;  // setter w VM zaktualizuje FlagHookDirection/HookSouth/HookEast

        FlagRotate = f.Rotate;

        FlagLight           = f.Light is not null;
        FlagLightBrightness = f.Light is { } li ? (int)li.Brightness : 0;
        FlagLightColor      = f.Light is { } lc ? (int)lc.Color : 0;

        FlagDontHide    = f.DontHide;
        FlagTranslucent = f.Translucent;

        FlagShift  = f.Shift is not null;
        FlagShiftX = f.Shift is { } sh ? unchecked((int)sh.X) : 0;
        FlagShiftY = f.Shift is { } sy ? unchecked((int)sy.Y) : 0;
        if (FlagShift)
        {
            EnsureShiftRangeCovers(FlagShiftX, FlagShiftY);
        }

        FlagHeight    = f.Height is not null;
        FlagElevation = f.Height is { } he ? (int)he.Elevation : 0;

        FlagReverseAddonsEast  = f.ReverseAddonsEast;
        FlagReverseAddonsWest  = f.ReverseAddonsWest;
        FlagReverseAddonsSouth = f.ReverseAddonsSouth;
        FlagReverseAddonsNorth = f.ReverseAddonsNorth;

        // Prawa kolumna
        FlagLyingObject   = f.LyingObject;
        FlagAnimateAlways = f.AnimateAlways;

        FlagAutomap      = f.Automap is not null;
        FlagAutomapColor = f.Automap is { } am ? (int)am.Color : 0;

        FlagLenshelp   = f.Lenshelp is not null;
        FlagLenshelpId = f.Lenshelp is { } lh ? (int)lh.Id : 0;

        FlagFullbank   = f.Fullbank;
        FlagIgnoreLook = f.IgnoreLook;

        FlagClothes     = f.Clothes is not null;
        FlagClothesSlot = f.Clothes is { } cl ? (int)cl.Slot : 0;

        FlagDefaultAction      = f.DefaultAction is not null;
        FlagDefaultActionIndex = f.DefaultAction is { } da ? (int)da.Action : 0;

        FlagMarket    = f.Market is not null;
        FlagMarketTradeAs  = f.Market is { } mk && mk.HasTradeAsObjectId  ? (int)mk.TradeAsObjectId  : 0;
        FlagMarketShowAs   = f.Market is { } ms && ms.HasShowAsObjectId   ? (int)ms.ShowAsObjectId   : 0;
        FlagMarketMinLevel = f.Market is { } ml && ml.HasMinimumLevel     ? (int)ml.MinimumLevel     : 0;
        FlagMarketCategory = f.Market is { } mc && mc.HasCategory         ? (int)mc.Category - 1     : 0;

        FlagMarketProfAny = FlagMarketProfNone = FlagMarketProfKnight = false;
        FlagMarketProfPaladin = FlagMarketProfSorcerer = FlagMarketProfDruid = FlagMarketProfPromoted = false;
        if (f.Market is { } mkt)
        {
            foreach (var v in mkt.RestrictToVocation)
                SetMarketVoc(v);
        }

        AppearanceNameField  = a.HasName        ? a.Name        : string.Empty;
        AppearanceDescField  = a.HasDescription ? a.Description : string.Empty;

        FlagWrap        = f.Wrap;
        FlagUnwrap      = f.Unwrap;
        FlagTopeffect   = f.Topeffect;
        FlagDecoItemKit = f.DecoItemKit;

        FlagChangedtoexpire          = f.Changedtoexpire is not null;
        FlagChangedToExpireFormerId  = f.Changedtoexpire is { } cte ? (int)cte.FormerObjectTypeid : 0;

        FlagCorpse       = f.Corpse;
        FlagPlayerCorpse = f.PlayerCorpse;
        ClearNpcSaleEntryCollection();
        foreach (var npc in f.Npcsaledata)
        {
            var entry = NpcSaleEntryViewModel.From(npc);
            AttachNpcSaleEntry(entry);
            NpcSaleEntries.Add(entry);
        }
        SelectedNpcSaleEntry = NpcSaleEntries.FirstOrDefault();
        FlagNpcSaleData  = NpcSaleEntries.Count > 0;
        FlagShowOffSocket = f.ShowOffSocket;
        FlagReportable   = f.Reportable;

        FlagUpgradeclassification       = f.Upgradeclassification is not null;
        FlagUpgradeClassificationAmount = f.Upgradeclassification is { } uc
            ? (int)uc.UpgradeClassification : 0;

        FlagWearout    = f.Wearout;
        FlagClockexpire = f.Clockexpire;
        FlagExpire     = f.Expire;
        FlagExpirestop = f.Expirestop;

        FlagCyclopedia     = f.Cyclopediaitem is not null;
        FlagCyclopediaType = f.Cyclopediaitem is { } cyclopedia ? (int)cyclopedia.CyclopediaType : 0;
        FlagAmmo       = f.Ammo;

        FlagSkillwheelGem = f.SkillwheelGem is not null;
        FlagGemQualityId  = f.SkillwheelGem is { } sg ? (int)sg.GemQualityId : 0;
        FlagGemVocId      = f.SkillwheelGem is { } sv ? (int)sv.VocationId   : 0;

        FlagDualWielding = f.DualWielding;

        FlagImbueable          = f.Imbueable is not null;
        FlagImbueableSlotCount = f.Imbueable is { } im ? (int)im.SlotCount : 0;

        FlagProficiency   = f.Proficiency is not null;
        FlagProficiencyId = f.Proficiency is { } pr ? (int)pr.ProficiencyId : 0;

        FlagMinimumLevel  = f.HasMinimumLevel ? (int)f.MinimumLevel : 0;
        FlagWeaponTypeIndex = f.HasWeaponType ? (int)f.WeaponType : 0;

        FlagRestrictVocAny = FlagRestrictVocNone = FlagRestrictVocKnight = false;
        FlagRestrictVocPaladin = FlagRestrictVocSorcerer = FlagRestrictVocDruid = false;
        FlagRestrictVocMonk = FlagRestrictVocPromoted = false;
        foreach (var v in f.RestrictToVocation)
            SetRestrictVoc(v);

        // Texture
        RefreshSpriteInfo(a);

        // Other - pełny dump protobuf
        OtherFullInfo = a.ToString();
    }

    private void RefreshSpriteInfo(Appearance a)
    {
        PropGroups = a.FrameGroup.Count;

        if (a.FrameGroup.Count == 0)
        {
            PropLayers = PropTileWidth = PropTileHeight = 0;
            PropPatternX = PropPatternY = PropPatternZ = 0;
            PropBoundingSquare = 0;
            PropIsOpaque = false;
            HasAnimation = false;
            PropFrames = CurrentFrameCount = 0;
            AnimDefaultStartPhase = AnimLoopTypeIndex = AnimLoopCount = 0;
            AnimRandomStartPhase = AnimSynchronized = false;
            AnimCurrentFrameMin = AnimCurrentFrameMax = 0;
            return;
        }

        var groupIdx = Math.Clamp(SelectedGroupIndex, 0, a.FrameGroup.Count - 1);
        var fg = a.FrameGroup.Count > groupIdx ? a.FrameGroup[groupIdx] : null;
        var si = fg?.SpriteInfo;
        var layout = si is null ? null : AppearanceLayoutInfo.From(si);

        PropLayers         = si is null ? 0 : AppearanceTextureLayout.GetLayers(si);
        PropTileWidth      = layout?.TileWidth ?? 0;
        PropTileHeight     = layout?.TileHeight ?? 0;
        PropPatternX       = si is null ? 0 : AppearanceTextureLayout.GetPatternWidth(si);
        PropPatternY       = si is null ? 0 : AppearanceTextureLayout.GetPatternHeight(si);
        PropPatternZ       = si is null ? 0 : AppearanceTextureLayout.GetPatternDepth(si);
        PropBoundingSquare = (int)(si?.BoundingSquare ?? 0);
        PropIsOpaque       = si?.IsOpaque ?? false;

        var anim = si?.Animation;
        HasAnimation = anim is not null;
        if (anim is not null)
        {
            AnimDefaultStartPhase = anim.HasDefaultStartPhase ? (int)anim.DefaultStartPhase : 0;
            AnimRandomStartPhase  = anim.HasRandomStartPhase  ? anim.RandomStartPhase  : false;
            AnimSynchronized      = anim.HasSynchronized      ? anim.Synchronized      : false;
            AnimLoopTypeIndex     = anim.HasLoopType          ? (int)anim.LoopType     : 0;
            AnimLoopCount         = anim.HasLoopCount         ? (int)anim.LoopCount    : 0;
            PropFrames            = AppearanceTextureLayout.GetFrameCount(si!);
            CurrentFrameCount     = PropFrames;

            var frameIndex = Math.Clamp(CurrentFrameIndex, 0, Math.Max(0, anim.SpritePhase.Count - 1));
            if (anim.SpritePhase.Count > 0)
            {
                AnimCurrentFrameMin = (int)anim.SpritePhase[frameIndex].DurationMin;
                AnimCurrentFrameMax = (int)anim.SpritePhase[frameIndex].DurationMax;
            }
        }
        else
        {
            AnimDefaultStartPhase = AnimLoopTypeIndex = AnimLoopCount = 0;
            AnimRandomStartPhase = AnimSynchronized = false;
            PropFrames = si is null ? 0 : AppearanceTextureLayout.GetFrameCount(si);
            CurrentFrameCount = PropFrames;
            AnimCurrentFrameMin = AnimCurrentFrameMax = 0;
        }

        SelectedGroupIndex = Math.Clamp(SelectedGroupIndex, 0, MaxGroupIndex);
        CurrentFrameIndex = Math.Clamp(CurrentFrameIndex, 0, MaxFrameIndex);
        CurrentDirection = Math.Clamp(CurrentDirection, 0, MaxDirectionIndex);
        CurrentAddonIndex = Math.Clamp(CurrentAddonIndex, 0, MaxAddonIndex);
        CurrentPatternZIndex = Math.Clamp(CurrentPatternZIndex, 0, MaxPatternZIndex);
        UpdateTextureDerivedProperties();

        BoundingBoxes.Clear();
        if (si?.BoundingBoxPerDirection is { Count: > 0 } boxes)
        {
            foreach (var b in boxes)
                BoundingBoxes.Add(new BoundingBoxItem
                    { X = (int)b.X, Y = (int)b.Y, Width = (int)b.Width, Height = (int)b.Height });
        }
    }

    private void RefreshSpriteSlots(Appearance? a)
    {
        SpriteSlots.Clear();
        if (a is null) return;

        foreach (var part in _assets.GetSpriteSlots(a, SelectedGroupIndex))
        {
            SpriteSlots.Add(new SpriteSlotItem
            {
                Index = part.Index,
                SpriteId = part.SpriteId,
                Layer = part.Layer,
                PatternX = part.PatternX,
                PatternY = part.PatternY,
                PatternZ = part.PatternZ,
                Frame = part.Frame
            });
        }

        _slotRequestId++;
        _ = LoadSlotThumbnailsAsync();
    }

    private void RefreshRenderedPreview(AppearanceListItem? item)
    {
        _ = RefreshRenderedPreviewAsync(item);
    }

    private async Task LoadSlotThumbnailsAsync()
    {
        var requestId = _slotRequestId;
        var slots = SpriteSlots.ToList();
        var svc   = _assets;

        var data = await Task.Run(() =>
        {
            var result = new List<(SpriteSlotItem Slot, byte[]? Pixels, int Width, int Height)>(slots.Count);
            foreach (var slot in slots)
            {
                if (slot.SpriteId == 0)
                {
                    result.Add((slot, null, 32, 32));
                    continue;
                }

                var pixels = svc.GetSpritePixels(slot.SpriteId);
                var (w, h) = svc.GetSpriteSize(slot.SpriteId);
                result.Add((slot, pixels, w, h));
            }

            return result;
        });

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (requestId != _slotRequestId)
            {
                return;
            }

            foreach (var (slot, pixels, width, height) in data)
            {
                slot.Thumbnail = SpriteToBitmap(pixels, width, height);
            }
        });
    }

    private async Task RefreshRenderedPreviewAsync(AppearanceListItem? item)
    {
        var requestId = ++_textureRequestId;
        if (item is null || !_assets.IsLoaded)
        {
            SetPreview(null);
            PreviewPixelWidth = 0;
            PreviewPixelHeight = 0;
            PreviewInfo = "Brak podglądu.";
            TextureSummary = "Brak wybranego obiektu.";
            return;
        }

        var options = BuildRenderOptions();
        RenderedSpriteImage? rendered = null;
        try
        {
            rendered = await Task.Run(() => _assets.RenderAppearance(item.Source, options));
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (requestId == _textureRequestId)
                {
                    StatusText = $"Błąd podglądu: {ex.Message}";
                }
            });
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (requestId != _textureRequestId || !ReferenceEquals(Selected, item))
            {
                return;
            }

            if (rendered is null)
            {
                SetPreview(null);
                PreviewPixelWidth = 0;
                PreviewPixelHeight = 0;
                PreviewInfo = "Brak grafiki dla wybranego obiektu.";
                return;
            }

            var bitmap = SpriteToBitmap(rendered.Pixels, rendered.Width, rendered.Height);
            SetPreview(bitmap);
            PreviewPixelWidth = rendered.Width;
            PreviewPixelHeight = rendered.Height;
            if (item.Thumbnail is null && bitmap is not null)
            {
                item.Thumbnail = SpriteToBitmap(rendered.Pixels, rendered.Width, rendered.Height);
            }

            PreviewInfo = $"Podgląd: {rendered.Width}x{rendered.Height}px, kierunek: {CurrentDirectionName}, klatka: {FrameLabel}.";
            TextureSummary = $"Grupa {SelectedGroupIndex + 1}/{Math.Max(1, PropGroups)}, sprite'y obiektu: {SpriteSlots.Count}, warstwy: {PropLayers}.";
        });
    }

    private AppearanceRenderOptions BuildRenderOptions() =>
        new(
            SelectedGroupIndex,
            CurrentDirection,
            CurrentAddonIndex,
            CurrentPatternZIndex,
            CurrentFrameIndex,
            BlendLayers,
            ShowAllAddons,
            OutfitColorize,
            OutfitHeadColor,
            OutfitBodyColor,
            OutfitLegsColor,
            OutfitFeetColor);

    private void RefreshTextureState()
    {
        if (Selected is null)
        {
            SpriteSlots.Clear();
            SetPreview(null);
            PreviewPixelWidth = 0;
            PreviewPixelHeight = 0;
            TextureSummary = "Brak wybranego obiektu.";
            PreviewInfo = "Brak podglądu.";
            return;
        }

        RefreshSpriteInfo(Selected.Source);
        RefreshSpriteSlots(Selected.Source);
        RefreshRenderedPreview(Selected);
    }

    private void ResetTextureNavigation(Appearance? appearance)
    {
        SelectedGroupIndex = appearance?.AppearanceType == APPEARANCE_TYPE.AppearanceOutfit
            ? FindPreferredOutfitGroup(appearance)
            : 0;
        CurrentFrameIndex = 0;
        CurrentDirection = 0;
        CurrentAddonIndex = 0;
        CurrentPatternZIndex = 0;
        SelectedSpriteSlot = null;
        CurrentDirectionName = "Góra";

        if (appearance?.AppearanceType == APPEARANCE_TYPE.AppearanceOutfit)
        {
            CurrentDirection = 2;
            CurrentDirectionName = "Dół";
        }
    }

    private static int FindPreferredOutfitGroup(Appearance appearance)
    {
        for (var index = 0; index < appearance.FrameGroup.Count; index++)
        {
            if (appearance.FrameGroup[index].FixedFrameGroup == FIXED_FRAME_GROUP.OutfitIdle)
            {
                return index;
            }
        }

        return 0;
    }

    private void UpdateTextureDerivedProperties()
    {
        OnPropertyChanged(nameof(CanUseDirections));
        OnPropertyChanged(nameof(CanUseAddons));
        OnPropertyChanged(nameof(CanUsePatternZ));
        OnPropertyChanged(nameof(CanUseFrames));
        OnPropertyChanged(nameof(MaxGroupIndex));
        OnPropertyChanged(nameof(MaxDirectionIndex));
        OnPropertyChanged(nameof(MaxAddonIndex));
        OnPropertyChanged(nameof(MaxPatternZIndex));
        OnPropertyChanged(nameof(MaxFrameIndex));
        OnPropertyChanged(nameof(FrameLabel));
    }

    private void RefreshSheetImportPreview()
    {
        if (string.IsNullOrWhiteSpace(SheetImportFilePath) || !File.Exists(SheetImportFilePath))
        {
            SetSheetImportPreview(null);
            SheetImportSummary = "Wybierz plik sprite sheetu i ustaw siatkę cięcia.";
            return;
        }

        try
        {
            var tileWidth = SheetImportSelectedLayout?.TileWidth ?? 32;
            var tileHeight = SheetImportSelectedLayout?.TileHeight ?? 32;

            using var image = Image.Load<Bgra32>(SheetImportFilePath);
            DrawCutGrid(image, tileWidth, tileHeight);

            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            SetSheetImportPreview(SpriteToBitmap(pixels, image.Width, image.Height));

            var columns = (int)Math.Ceiling(image.Width / (double)Math.Max(1, tileWidth));
            var rows = (int)Math.Ceiling(image.Height / (double)Math.Max(1, tileHeight));
            var totalTiles = Math.Max(1, columns * rows);

            SheetImportSummary =
                $"Podgląd: {image.Width}x{image.Height}px, cięcie {tileWidth}x{tileHeight}, szacowane kafle: {totalTiles}.";
        }
        catch (Exception ex)
        {
            SetSheetImportPreview(null);
            SheetImportSummary = $"Nie udało się przygotować podglądu sprite sheetu: {ex.Message}";
        }
    }

    private static void DrawCutGrid(Image<Bgra32> image, int tileWidth, int tileHeight)
    {
        var safeTileWidth = Math.Max(1, tileWidth);
        var safeTileHeight = Math.Max(1, tileHeight);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var horizontalLine = y % safeTileHeight == 0;
                for (var x = 0; x < row.Length; x++)
                {
                    if (horizontalLine || x % safeTileWidth == 0)
                    {
                        row[x] = new Bgra32(0, 0, 255, 255);
                    }
                }
            }
        });
    }

    private void SetSheetImportPreview(Bitmap? bmp)
    {
        var old = SheetImportPreview;
        SheetImportPreview = bmp;
        if (old is not null && !ReferenceEquals(old, bmp))
        {
            old.Dispose();
        }
    }

    private SpriteInfo? GetSelectedSpriteInfo()
    {
        if (Selected?.Source is not { } appearance)
        {
            return null;
        }

        return AppearanceTextureLayout.GetFrameGroup(appearance, SelectedGroupIndex)?.SpriteInfo;
    }

    private int ResolveDropLayer(SpriteInfo info)
    {
        var layerCount = AppearanceTextureLayout.GetLayers(info);
        if (SelectedSpriteSlot is { } slot)
        {
            return Math.Clamp(slot.Layer, 0, Math.Max(0, layerCount - 1));
        }

        return 0;
    }

    public void AssignSpriteToSlot(int slotIndex, uint spriteId)
    {
        MutateTexture(info =>
        {
            AppearanceTextureMutator.AssignSprite(info, slotIndex, spriteId);
        }, $"Przypisano sprite #{spriteId} do slotu #{slotIndex}.");

        SelectedSpriteSlot = SpriteSlots.FirstOrDefault(slot => slot.Index == slotIndex);
    }

    public void AssignSpriteFromPreviewDrop(
        uint spriteId,
        double pointerX,
        double pointerY,
        double surfaceWidth,
        double surfaceHeight)
    {
        if (Selected?.Source is not { } appearance)
        {
            StatusText = "Wybierz obiekt przed upuszczeniem sprite'a na podgląd.";
            return;
        }

        var (spriteWidth, spriteHeight) = _assets.GetSpriteSize(spriteId);
        var assignedSlotIndex = -1;
        var dropDirection = CurrentDirection;
        var dropAddon = CurrentAddonIndex;

        MutateTexture(info =>
        {
            AppearanceTextureMutator.EnsureLayoutForFirstSpriteDrop(
                info,
                appearance.AppearanceType,
                spriteWidth,
                spriteHeight);

            // Dla pocisków (Missile) z gridem patternX×patternY > 1 mapujemy pozycję drop'a
            // na konkretną komórkę grid'a (kierunek), zamiast nadpisywać bieżący CurrentDirection.
            var layout = AppearanceLayoutInfo.From(info);
            var isMissileGrid = Selected?.Category == APPEARANCE_TYPE.AppearanceMissile
                                && layout.PatternX > 1 && layout.PatternY > 1
                                && layout.TileWidth == 1 && layout.TileHeight == 1;
            if (isMissileGrid && surfaceWidth > 0 && surfaceHeight > 0)
            {
                var col = (int)Math.Clamp(Math.Floor(pointerX / surfaceWidth * layout.PatternX), 0, layout.PatternX - 1);
                var row = (int)Math.Clamp(Math.Floor(pointerY / surfaceHeight * layout.PatternY), 0, layout.PatternY - 1);
                dropDirection = col;
                dropAddon = row;
            }

            var context = AppearanceTextureLayout.ResolveCursorContext(
                info,
                SelectedGroupIndex,
                dropDirection,
                dropAddon,
                CurrentPatternZIndex,
                CurrentFrameIndex,
                ResolveDropLayer(info),
                pointerX,
                pointerY,
                surfaceWidth,
                surfaceHeight,
                PreviewPixelWidth > 0 ? PreviewPixelWidth : spriteWidth,
                PreviewPixelHeight > 0 ? PreviewPixelHeight : spriteHeight);

            AppearanceTextureMutator.AssignSpriteAtContext(info, context, spriteId);
            assignedSlotIndex = context.SlotIndex;
        }, $"Przypisano sprite #{spriteId} do podglądu tekstury.");

        if (assignedSlotIndex >= 0)
        {
            SelectedSpriteSlot = SpriteSlots.FirstOrDefault(slot => slot.Index == assignedSlotIndex);
        }
    }

    private void MutateTexture(Action<SpriteInfo> mutation, string successStatus)
    {
        var spriteInfo = GetSelectedSpriteInfo();
        if (spriteInfo is null)
        {
            StatusText = "Brak wybranej tekstury.";
            return;
        }

        try
        {
            mutation(spriteInfo);
            if (Selected?.Source is { } appearance)
            {
                ApplyCurrentAppearanceChanges(appearance);
                OtherFullInfo = appearance.ToString();
            }

            CurrentFrameIndex = Math.Clamp(CurrentFrameIndex, 0, Math.Max(0, AppearanceTextureLayout.GetFrameCount(spriteInfo) - 1));
            RefreshTextureState();
            RefreshTextureFrameGroups(Selected?.Source);
            StatusText = successStatus;
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd edycji tekstury: {ex.Message}";
        }
    }

    private void ApplyCurrentAppearanceChanges(Appearance appearance)
    {
        AppearanceEditorService.Apply(appearance, BuildAppearanceEditStateFromViewModel());
    }

    internal AppearanceEditState BuildAppearanceEditStateFromViewModel()
    {
        var state = new AppearanceEditState
        {
            Name = AppearanceNameField,
            Description = AppearanceDescField,
            HasTransparency = FlagTransparency,
            TransparencyLevel = ToUInt(FlagTransparencyLevel),
            IsGround = FlagGround,
            GroundSpeed = ToUInt(FlagGroundSpeed),
            Clip = FlagClip,
            Bottom = FlagBottom,
            Top = FlagTop,
            Container = FlagContainer,
            Cumulative = FlagCumulative,
            Usable = FlagUsable,
            Forceuse = FlagForceuse,
            Multiuse = FlagMultiuse,
            IsWriteable = FlagWrite,
            WriteMaxLength = ToUInt(FlagWriteMaxLength),
            IsWriteOnce = FlagWriteOnce,
            WriteOnceMaxLength = ToUInt(FlagWriteOnceMaxLength),
            Liquidpool = FlagLiquidpool,
            IsUnpassable = FlagUnpass,
            Unmove = FlagUnmove,
            Unsight = FlagUnsight,
            Avoid = FlagAvoid,
            NoMovementAnimation = FlagNoMovementAnimation,
            Take = FlagTake,
            Liquidcontainer = FlagLiquidcontainer,
            Hang = FlagHang,
            HasHook = FlagHook,
            HookDirection = ToEnum(FlagHookDirection, HOOK_TYPE.South),
            HookSouth = FlagHookSouth,
            HookEast = FlagHookEast,
            Rotate = FlagRotate,
            HasLight = FlagLight,
            LightBrightness = ToUInt(FlagLightBrightness),
            LightColor = ToUInt(FlagLightColor),
            DontHide = FlagDontHide,
            Translucent = FlagTranslucent,
            HasShift = FlagShift,
            ShiftX = FlagShiftX,
            ShiftY = FlagShiftY,
            ShiftMin = ShiftMinimum,
            ShiftMax = ShiftMaximum,
            HasHeight = FlagHeight,
            Elevation = ToUInt(FlagElevation),
            ReverseAddonsEast = FlagReverseAddonsEast,
            ReverseAddonsWest = FlagReverseAddonsWest,
            ReverseAddonsSouth = FlagReverseAddonsSouth,
            ReverseAddonsNorth = FlagReverseAddonsNorth,
            LyingObject = FlagLyingObject,
            AnimateAlways = FlagAnimateAlways,
            HasAutomap = FlagAutomap,
            AutomapColor = ToUInt(FlagAutomapColor),
            HasLenshelp = FlagLenshelp,
            LenshelpId = ToUInt(FlagLenshelpId),
            Fullbank = FlagFullbank,
            IgnoreLook = FlagIgnoreLook,
            HasClothes = FlagClothes,
            ClothesSlot = ToUInt(FlagClothesSlot),
            HasDefaultAction = FlagDefaultAction,
            DefaultAction = ToEnum(FlagDefaultActionIndex, PLAYER_ACTION.None),
            HasMarket = FlagMarket,
            MarketCategoryIndex = FlagMarketCategory,
            MarketTradeAsObjectId = ToUInt(FlagMarketTradeAs),
            MarketShowAsObjectId = ToUInt(FlagMarketShowAs),
            MarketMinimumLevel = ToUInt(FlagMarketMinLevel),
            Wrap = FlagWrap,
            Unwrap = FlagUnwrap,
            Topeffect = FlagTopeffect,
            DecoItemKit = FlagDecoItemKit,
            HasChangedToExpire = FlagChangedtoexpire,
            ChangedToExpireFormerId = ToUInt(FlagChangedToExpireFormerId),
            Corpse = FlagCorpse,
            PlayerCorpse = FlagPlayerCorpse,
            HasNpcSaleData = FlagNpcSaleData,
            ShowOffSocket = FlagShowOffSocket,
            Reportable = FlagReportable,
            HasUpgradeClassification = FlagUpgradeclassification,
            UpgradeClassification = ToUInt(FlagUpgradeClassificationAmount),
            Wearout = FlagWearout,
            Clockexpire = FlagClockexpire,
            Expire = FlagExpire,
            Expirestop = FlagExpirestop,
            HasCyclopedia = FlagCyclopedia,
            CyclopediaType = ToUInt(FlagCyclopediaType),
            Ammo = FlagAmmo,
            HasSkillwheelGem = FlagSkillwheelGem,
            GemQualityId = ToUInt(FlagGemQualityId),
            GemVocationId = ToUInt(FlagGemVocId),
            DualWielding = FlagDualWielding,
            HasImbueable = FlagImbueable,
            ImbueableSlotCount = ToUInt(FlagImbueableSlotCount),
            HasProficiency = FlagProficiency,
            ProficiencyId = ToUInt(FlagProficiencyId),
            MinimumLevel = ToUInt(FlagMinimumLevel),
            WeaponType = ToEnum(FlagWeaponTypeIndex, WEAPON_TYPE.Noweapon)
        };

        AddVocation(state.MarketVocations, FlagMarketProfAny, VOCATION.Any);
        AddVocation(state.MarketVocations, FlagMarketProfNone, VOCATION.None);
        AddVocation(state.MarketVocations, FlagMarketProfKnight, VOCATION.Knight);
        AddVocation(state.MarketVocations, FlagMarketProfPaladin, VOCATION.Paladin);
        AddVocation(state.MarketVocations, FlagMarketProfSorcerer, VOCATION.Sorcerer);
        AddVocation(state.MarketVocations, FlagMarketProfDruid, VOCATION.Druid);
        AddVocation(state.MarketVocations, FlagMarketProfPromoted, VOCATION.Promoted);

        AddVocation(state.RestrictToVocations, FlagRestrictVocAny, VOCATION.Any);
        AddVocation(state.RestrictToVocations, FlagRestrictVocNone, VOCATION.None);
        AddVocation(state.RestrictToVocations, FlagRestrictVocKnight, VOCATION.Knight);
        AddVocation(state.RestrictToVocations, FlagRestrictVocPaladin, VOCATION.Paladin);
        AddVocation(state.RestrictToVocations, FlagRestrictVocSorcerer, VOCATION.Sorcerer);
        AddVocation(state.RestrictToVocations, FlagRestrictVocDruid, VOCATION.Druid);
        AddVocation(state.RestrictToVocations, FlagRestrictVocMonk, VOCATION.Monk);
        AddVocation(state.RestrictToVocations, FlagRestrictVocPromoted, VOCATION.Promoted);

        if (FlagNpcSaleData)
        {
            foreach (var entry in NpcSaleEntries)
            {
                state.NpcSaleData.Add(entry.ToEditState());
            }
        }

        return state;
    }

    private static void AddVocation(ICollection<VOCATION> target, bool enabled, VOCATION vocation)
    {
        if (enabled)
        {
            target.Add(vocation);
        }
    }

    private static uint ToUInt(int value) => value < 0 ? 0u : (uint)value;

    private static TEnum ToEnum<TEnum>(int value, TEnum fallback)
        where TEnum : struct, Enum
    {
        return Enum.IsDefined(typeof(TEnum), value) ? (TEnum)(object)value : fallback;
    }

    private APPEARANCE_TYPE GetActiveAppearanceType() => ActiveCategory switch
    {
        1 => APPEARANCE_TYPE.AppearanceOutfit,
        2 => APPEARANCE_TYPE.AppearanceEffect,
        3 => APPEARANCE_TYPE.AppearanceMissile,
        _ => APPEARANCE_TYPE.AppearanceObject
    };

    private uint NextIdForActiveCategory()
    {
        var max = ActiveList().Count == 0 ? 0 : ActiveList().Max(item => item.Id);
        if (ActiveCategory == 0 && max < 99)
        {
            return 100;
        }

        return max + 1;
    }

    private static Appearance CreateDefaultAppearance(APPEARANCE_TYPE type, uint id)
    {
        return new Appearance
        {
            Id = id,
            AppearanceType = type,
            Flags = new AppearanceFlags(),
            FrameGroup =
            {
                new FrameGroup
                {
                    FixedFrameGroup = type == APPEARANCE_TYPE.AppearanceOutfit
                        ? FIXED_FRAME_GROUP.OutfitIdle
                        : FIXED_FRAME_GROUP.ObjectInitial,
                    SpriteInfo = new SpriteInfo
                    {
                        PatternWidth = type == APPEARANCE_TYPE.AppearanceOutfit ? 4u : 1u,
                        PatternHeight = 1,
                        PatternDepth = 1,
                        Layers = 1,
                        PatternFrames = 1,
                        BoundingSquare = 32,
                        SpriteId = { 0 }
                    }
                }
            }
        };
    }

    private void AddAppearanceToActiveCategory(Appearance appearance)
    {
        if (_assets.AppearancesData is not { } app)
        {
            return;
        }

        switch (ActiveCategory)
        {
            case 1:
                app.Outfit.Add(appearance);
                break;
            case 2:
                app.Effect.Add(appearance);
                break;
            case 3:
                app.Missile.Add(appearance);
                break;
            default:
                app.Object.Add(appearance);
                break;
        }
    }

    private void AddAppearanceToCategory(Appearance appearance)
    {
        if (_assets.AppearancesData is not { } app)
        {
            return;
        }

        var target = appearance.AppearanceType switch
        {
            APPEARANCE_TYPE.AppearanceOutfit => app.Outfit,
            APPEARANCE_TYPE.AppearanceEffect => app.Effect,
            APPEARANCE_TYPE.AppearanceMissile => app.Missile,
            _ => app.Object
        };

        if (target.Any(existing => existing.Id == appearance.Id))
        {
            appearance.Id = target.Count == 0
                ? 100u
                : checked(target.Max(existing => existing.Id) + 1);
        }

        target.Add(appearance);
    }

    private void RefreshExportListSummary()
    {
        var selectedCount = SelectedAppearanceCount;
        ExportListSummary = selectedCount > 0
            ? $"Zaznaczono: {selectedCount}"
            : $"Lista AEC: {_exportAppearances.Count}";
    }

    private AppearanceListItem[] GetSelectedAppearanceItems() => _selectedAppearances.Count > 0
        ? _selectedAppearances.ToArray()
        : Selected is null ? [] : [Selected];

    private AecSpritePayload? ReadAssetSpritePayload(uint spriteId)
    {
        if (spriteId == 0 || _assets.GetSpritePixels(spriteId) is not { } pixels) return null;
        var (width, height) = _assets.GetSpriteSize(spriteId);
        return new AecSpritePayload(pixels, width, height);
    }

    private static Dictionary<uint, byte[]> BuildObdSpriteLookup(LegacyObdDocument document)
    {
        var result = new Dictionary<uint, byte[]>();
        for (var groupIndex = 0; groupIndex < document.Thing.FrameGroups.Count; groupIndex++)
        {
            var group = document.Thing.FrameGroups[groupIndex];
            var pixels = document.SpritePixelsByGroup[groupIndex];
            for (var index = 0; index < group.SpriteIds.Length && index < pixels.Length; index++)
            {
                var spriteId = group.SpriteIds[index];
                if (spriteId != 0 && !result.ContainsKey(spriteId)) result[spriteId] = pixels[index];
            }
        }
        return result;
    }

    private static void RemapModernSprites(ModernObjectConversion conversion, SpriteImportAllocator allocator)
    {
        var remap = new Dictionary<uint, uint>();
        foreach (var group in conversion.Appearance.FrameGroup)
        {
            var spriteInfo = group.SpriteInfo;
            if (spriteInfo is null) continue;
            for (var index = 0; index < spriteInfo.SpriteId.Count; index++)
            {
                var sourceId = spriteInfo.SpriteId[index];
                if (sourceId == 0)
                {
                    spriteInfo.SpriteId[index] = 0;
                    continue;
                }

                if (!remap.TryGetValue(sourceId, out var targetId))
                {
                    if (!conversion.Sprites.TryGetValue(sourceId, out var payload))
                    {
                        throw new InvalidDataException($"Brak sprite'a konwersji #{sourceId}.");
                    }
                    targetId = allocator.AddSprite(payload);
                    remap[sourceId] = targetId;
                }
                spriteInfo.SpriteId[index] = targetId;
            }
        }
    }

    private void SelectImportedCategory(Appearance appearance)
    {
        ActiveCategory = appearance.AppearanceType switch
        {
            APPEARANCE_TYPE.AppearanceOutfit => 1,
            APPEARANCE_TYPE.AppearanceEffect => 2,
            APPEARANCE_TYPE.AppearanceMissile => 3,
            _ => 0
        };
        ApplyFilter();
    }

    private static string CategoryFileName(APPEARANCE_TYPE category) => category switch
    {
        APPEARANCE_TYPE.AppearanceOutfit => "outfit",
        APPEARANCE_TYPE.AppearanceEffect => "effect",
        APPEARANCE_TYPE.AppearanceMissile => "missile",
        _ => "item"
    };

    private static string WarningSuffix(IReadOnlyCollection<string> warnings) => warnings.Count == 0
        ? string.Empty
        : $" Ostrzeżenia zgodności: {warnings.Count} (pierwsze: {warnings.First()})";

    private void RemoveAppearanceFromActiveCategory(Appearance appearance)
    {
        if (_assets.AppearancesData is not { } app)
        {
            return;
        }

        switch (ActiveCategory)
        {
            case 1:
                app.Outfit.Remove(appearance);
                break;
            case 2:
                app.Effect.Remove(appearance);
                break;
            case 3:
                app.Missile.Remove(appearance);
                break;
            default:
                app.Object.Remove(appearance);
                break;
        }
    }

    private void ClearAllFlags()
    {
        FlagGround = FlagClip = FlagBottom = FlagTop = FlagContainer = FlagCumulative = false;
        FlagUsable = FlagForceuse = FlagMultiuse = FlagWrite = FlagWriteOnce = false;
        FlagLiquidpool = FlagUnpass = FlagUnmove = FlagUnsight = FlagAvoid = false;
        FlagNoMovementAnimation = FlagTake = FlagLiquidcontainer = FlagHang = false;
        FlagHook = FlagHookSouth = FlagHookEast = FlagRotate = false;
        FlagLight = FlagDontHide = FlagTranslucent = FlagShift = FlagHeight = false;
        FlagReverseAddonsEast = FlagReverseAddonsWest = FlagReverseAddonsSouth = FlagReverseAddonsNorth = false;
        FlagLyingObject = FlagAnimateAlways = FlagAutomap = FlagLenshelp = FlagFullbank = FlagIgnoreLook = false;
        FlagClothes = FlagDefaultAction = FlagMarket = FlagWrap = FlagUnwrap = FlagTopeffect = FlagDecoItemKit = false;
        FlagChangedtoexpire = FlagCorpse = FlagPlayerCorpse = FlagNpcSaleData = FlagShowOffSocket = FlagReportable = false;
        FlagUpgradeclassification = FlagWearout = FlagClockexpire = FlagExpire = FlagExpirestop = FlagCyclopedia = FlagAmmo = false;
        FlagSkillwheelGem = FlagDualWielding = FlagImbueable = FlagProficiency = FlagTransparency = false;
        FlagRestrictVocAny = FlagRestrictVocNone = FlagRestrictVocKnight = FlagRestrictVocPaladin = false;
        FlagRestrictVocSorcerer = FlagRestrictVocDruid = FlagRestrictVocMonk = FlagRestrictVocPromoted = false;
        ClearNpcSaleEntryCollection();
    }

    private void SetMarketVoc(VOCATION v)
    {
        switch (v)
        {
            case VOCATION.Any:      FlagMarketProfAny = true;      break;
            case VOCATION.None:     FlagMarketProfNone = true;     break;
            case VOCATION.Knight:   FlagMarketProfKnight = true;   break;
            case VOCATION.Paladin:  FlagMarketProfPaladin = true;  break;
            case VOCATION.Sorcerer: FlagMarketProfSorcerer = true; break;
            case VOCATION.Druid:    FlagMarketProfDruid = true;    break;
            case VOCATION.Promoted: FlagMarketProfPromoted = true; break;
        }
    }

    private void SetRestrictVoc(VOCATION v)
    {
        switch (v)
        {
            case VOCATION.Any:      FlagRestrictVocAny = true;      break;
            case VOCATION.None:     FlagRestrictVocNone = true;     break;
            case VOCATION.Knight:   FlagRestrictVocKnight = true;   break;
            case VOCATION.Paladin:  FlagRestrictVocPaladin = true;  break;
            case VOCATION.Sorcerer: FlagRestrictVocSorcerer = true; break;
            case VOCATION.Druid:    FlagRestrictVocDruid = true;    break;
            case VOCATION.Monk:     FlagRestrictVocMonk = true;     break;
            case VOCATION.Promoted: FlagRestrictVocPromoted = true; break;
        }
    }

    private void RefreshTextureFrameGroups(Appearance? appearance)
    {
        var requestedIndex = SelectedGroupIndex;
        TextureFrameGroups.Clear();

        if (appearance is null)
        {
            SelectedGroupIndex = 0;
            OnPropertyChanged(nameof(SelectedGroupIndex));
            return;
        }

        for (var index = 0; index < appearance.FrameGroup.Count; index++)
        {
            TextureFrameGroups.Add(AppearanceFrameGroupListItem.From(index, appearance.FrameGroup[index]));
        }

        SelectedGroupIndex = TextureFrameGroups.Count == 0
            ? 0
            : Math.Clamp(requestedIndex, 0, TextureFrameGroups.Count - 1);
        OnPropertyChanged(nameof(SelectedGroupIndex));
    }

    private void AttachNpcSaleEntry(NpcSaleEntryViewModel entry) =>
        entry.PropertyChanged += OnNpcSaleEntryPropertyChanged;

    private void DetachNpcSaleEntry(NpcSaleEntryViewModel entry) =>
        entry.PropertyChanged -= OnNpcSaleEntryPropertyChanged;

    private void ClearNpcSaleEntryCollection()
    {
        foreach (var entry in NpcSaleEntries)
        {
            DetachNpcSaleEntry(entry);
        }

        NpcSaleEntries.Clear();
        SelectedNpcSaleEntry = null;
    }

    private void OnNpcSaleEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isLoadingAppearance)
        {
            TryAutoPersistCurrentAppearance(nameof(FlagNpcSaleData));
        }
    }

    private void SetPreview(Bitmap? bmp)
    {
        var old = PreviewImage;
        PreviewImage = bmp;
        if (old is not null && !ReferenceEquals(old, bmp)) old.Dispose();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        TryAutoPersistCurrentAppearance(e.PropertyName);
    }

    private void TryAutoPersistCurrentAppearance(string? propertyName)
    {
        if (_isLoadingAppearance || _isAutoApplying ||
            Selected?.Source is not { } appearance ||
            string.IsNullOrWhiteSpace(propertyName))
        {
            return;
        }

        var shouldPersist = propertyName.StartsWith("Flag", StringComparison.Ordinal) ||
                            propertyName is nameof(AppearanceNameField) or nameof(AppearanceDescField);

        if (!shouldPersist)
        {
            return;
        }

        _isAutoApplying = true;
        try
        {
            ApplyCurrentAppearanceChanges(appearance);
            OtherFullInfo = appearance.ToString();
            RefreshRenderedPreview(Selected);
        }
        finally
        {
            _isAutoApplying = false;
        }
    }

    private static Bitmap? SpriteToBitmap(byte[]? pixels, int w, int h)
    {
        if (pixels is null || pixels.Length < w * h * 4) return null;

        var bmp = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using var fb = bmp.Lock();
        for (int row = 0; row < h; row++)
        {
            var dst = IntPtr.Add(fb.Address, row * fb.RowBytes);
            Marshal.Copy(pixels, row * w * 4, dst, w * 4);
        }
        return bmp;
    }

    private static async Task<string?> PickSaveFileAsync(string title, string suggested)
    {
        var tl = GetTopLevel();
        if (tl is null) return null;
        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title             = LocalizationManager.Translate(title),
            SuggestedFileName = suggested
        });
        return file?.TryGetLocalPath();
    }

    private static async Task<string?> PickOpenFileAsync(string title)
    {
        var files = await PickOpenFilesAsync(title, "Kontener appearance .aec", ["*.aec"]);
        return files.FirstOrDefault();
    }

    private static async Task<string[]> PickOpenFilesAsync(
        string title,
        string typeName,
        IReadOnlyList<string> patterns)
    {
        var tl = GetTopLevel();
        if (tl is null) return [];
        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate(title),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationManager.Translate(typeName))
                {
                    Patterns = patterns
                },
                FilePickerFileTypes.All
            ]
        });

        return files.Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
    }

    private static async Task<string?> PickFolderAsync(string title)
    {
        var tl = GetTopLevel();
        if (tl is null) return null;
        var folders = await tl.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Translate(title),
            AllowMultiple = false
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private static Avalonia.Controls.TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } win })
            return Avalonia.Controls.TopLevel.GetTopLevel(win);
        return null;
    }

    private static Avalonia.Controls.Window? GetMainWindow()
    {
        return Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } win }
            ? win
            : null;
    }

    private sealed class SpriteImportAllocator(IAssetSpriteStore store)
    {
        private readonly Dictionary<int, SheetCursor> _cursors = new();
        private readonly Dictionary<string, AssetSpriteSheet> _dirtySheets = new(StringComparer.OrdinalIgnoreCase);

        public uint AddSprite(byte[] pixels) =>
            AddSprite(AecContainerCodec.DecodeSpritePayload(pixels));

        public uint AddSprite(AecSpritePayload payload)
        {
            if (payload.IsEmpty) return 0;
            var spriteType = FindSpriteType(payload.Width, payload.Height);
            if (!_cursors.TryGetValue(spriteType, out var cursor) || cursor.NextTile >= cursor.Sheet.TileCount)
            {
                cursor = new SheetCursor(CreateSheet(spriteType), 0);
                _cursors[spriteType] = cursor;
            }

            var tileIndex = cursor.NextTile++;
            var spriteId = cursor.Sheet.FirstSpriteId + (uint)tileIndex;
            store.ReplaceTile(cursor.Sheet, tileIndex, payload.Pixels);
            _dirtySheets[cursor.Sheet.File] = cursor.Sheet;
            return spriteId;
        }

        public void Save()
        {
            foreach (var sheet in _dirtySheets.Values)
            {
                store.SaveSheet(sheet);
            }

            if (_dirtySheets.Count > 0)
            {
                store.SaveCatalog();
            }
        }

        private AssetSpriteSheet CreateSheet(int spriteType)
        {
            var firstId = store.Catalog
                .Where(entry => string.Equals(entry.Type, "sprite", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.LastSpriteid)
                .DefaultIfEmpty(0)
                .Max() + 1;

            return store.CreateSheet(spriteType, (uint)Math.Max(1, firstId));
        }

        private static int FindSpriteType(int width, int height)
        {
            if (SpriteSheetLayout.TryFromDimensions(width, height, out var layout))
            {
                return layout.SpriteType;
            }

            throw new InvalidDataException(
                $"Format assets nie obsługuje rozmiaru sprite'a {width}×{height}.");
        }

        private sealed class SheetCursor(AssetSpriteSheet sheet, int nextTile)
        {
            public AssetSpriteSheet Sheet { get; } = sheet;
            public int NextTile { get; set; } = nextTile;
        }
    }
}

public sealed record NewSpriteImportItem(string FileName, string FullPath, int Width, int Height)
{
    public string SizeLabel => $"{Width}x{Height}";
}
