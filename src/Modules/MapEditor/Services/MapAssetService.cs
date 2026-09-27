using System.Collections.Frozen;
using System.Xml.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Modules.ObjectBuilder.Services;
using Narzedzia.Core.Appearances;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;
using Narzedzia.Core.Tibia12;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.MapEditor.Services;

/// <summary>
/// Wspólny magazyn grafiki mapy dla klasycznych klientów DAT+SPR oraz klientów 12+.
/// Identyfikatory OTBM są Server ID, dlatego przed pobraniem grafiki są mapowane przez
/// items.otb na Client ID dokładnie tak, jak robi to RME.
/// </summary>
public sealed class MapAssetService : IDisposable
{
    private const uint PickupableFlag = 1u << 5;

    private readonly Dictionary<uint, Appearance> _modernObjects = [];
    private readonly Dictionary<uint, Appearance> _modernOutfits = [];
    private readonly Dictionary<uint, DatThingType> _legacyObjects = [];
    private readonly Dictionary<uint, DatThingType> _legacyOutfits = [];
    private readonly Dictionary<uint, OtbItem> _serverItems = [];
    private readonly Dictionary<ushort, ItemXmlEntry> _itemNames = [];
    private readonly Dictionary<(uint ServerId, int Frame, int PatternX, int PatternY, int PatternZ, int Subtype), Bitmap?> _cache = [];
    private readonly Dictionary<(uint LookType, int Direction, int Frame), Bitmap?> _outfitCache = [];
    private readonly Dictionary<(uint ClientId, int Frame), Bitmap?> _clientObjectCache = [];
    private readonly Dictionary<string, Image<Bgra32>> _modernSheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly LegacyDatService _legacyDat = new();
    private readonly LegacySpriteStore _legacySprites = new();
    private List<CatalogEntry> _catalog = [];
    private string? _folderPath;
    private string? _itemsOtbPath;

    public bool IsLoaded => !string.IsNullOrEmpty(_folderPath) && (_modernObjects.Count > 0 || _legacyObjects.Count > 0);
    public int ObjectCount => _serverItems.Count > 0
        ? _serverItems.Count
        : Math.Max(_modernObjects.Count, _legacyObjects.Count);
    public string? FolderPath => _folderPath;
    public string? ItemsOtbPath => _itemsOtbPath;
    public MapAssetFormat Format { get; private set; }
    public IReadOnlyCollection<uint> ObjectIds => _serverItems.Count > 0
        ? _serverItems.Keys
        : _modernObjects.Count > 0 ? _modernObjects.Keys : _legacyObjects.Keys;

    public LoadReport LoadFromFolder(
        string folderPath,
        uint? expectedOtbMinorVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException("Folder klienta/assets nie istnieje: " + folderPath);

        // Sprawdzenie przed Clear() zachowuje poprzednio wczytaną generację, gdy
        // operacja została anulowana jeszcze przed rozpoczęciem pracy.
        cancellationToken.ThrowIfCancellationRequested();
        Clear();
        try
        {
            _folderPath = folderPath;
            var warnings = new List<string>();

            _itemsOtbPath = FindItemsOtb(folderPath, expectedOtbMinorVersion, cancellationToken);
            OtbFile? otb = null;
            if (_itemsOtbPath is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                otb = new OtbParser().Parse(_itemsOtbPath);
                cancellationToken.ThrowIfCancellationRequested();
                var itemIndex = 0;
                foreach (var item in otb.Items)
                {
                    if ((itemIndex++ & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (item.ItemType != OtbItemType.Deprecated)
                        _serverItems[item.ServerId] = item;
                }
                LoadItemNames(Path.GetDirectoryName(_itemsOtbPath)!, cancellationToken);

                if (expectedOtbMinorVersion is { } expected && otb.MinorVersion != expected)
                    warnings.Add($"Mapa wymaga OTB {expected}, a znaleziono OTB {otb.MinorVersion}.");
            }
            else
            {
                warnings.Add("Nie znaleziono zgodnego items.otb; grafika używa awaryjnie tych samych Server ID i Client ID.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var catalogPath = Path.Combine(folderPath, "catalog-content.json");
            int spriteSheetCount;
            if (File.Exists(catalogPath))
            {
                spriteSheetCount = LoadModern(folderPath, catalogPath, cancellationToken);
                Format = MapAssetFormat.ModernAssets;
            }
            else
            {
                warnings.AddRange(LoadLegacy(folderPath, cancellationToken));
                spriteSheetCount = 1;
                Format = MapAssetFormat.LegacyDatSpr;
            }

            ValidateMappings(warnings, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new LoadReport(
                folderPath,
                ObjectCount,
                spriteSheetCount,
                Format,
                _itemsOtbPath,
                otb?.MajorVersion,
                otb?.MinorVersion,
                warnings);
        }
        catch (OperationCanceledException)
        {
            // Anulowany serwis-kandydat nie może pozostać pozornie załadowany ani
            // trzymać dużych struktur/protobufów do czasu działania GC.
            Clear();
            throw;
        }
    }

    public Appearance? GetObject(uint serverId)
    {
        var clientId = ResolveClientId(serverId);
        return _modernObjects.GetValueOrDefault(clientId);
    }

    public string? GetObjectName(uint serverId)
    {
        if (serverId <= ushort.MaxValue && _itemNames.TryGetValue((ushort)serverId, out var xml) &&
            !string.IsNullOrWhiteSpace(xml.Name))
            return xml.Name;
        if (_serverItems.TryGetValue(serverId, out var otb) && !string.IsNullOrWhiteSpace(otb.Name))
            return otb.Name;
        return GetObject(serverId)?.Name;
    }

    public bool IsGround(uint serverId) =>
        _serverItems.TryGetValue(serverId, out var item)
            ? item.ItemType == OtbItemType.Ground
            : GetObject(serverId)?.Flags?.Bank is not null ||
              _legacyObjects.GetValueOrDefault(ResolveClientId(serverId))?.IsGround == true;

    public bool IsContainer(uint serverId) =>
        _serverItems.TryGetValue(serverId, out var item)
            ? item.ItemType == OtbItemType.Container
            : GetObject(serverId)?.Flags?.Container == true ||
              _legacyObjects.GetValueOrDefault(ResolveClientId(serverId))?.IsContainer == true;

    public bool IsPickupable(uint serverId) =>
        _serverItems.TryGetValue(serverId, out var item)
            ? (item.Flags & PickupableFlag) != 0
            : GetObject(serverId)?.Flags?.Take == true ||
              _legacyObjects.GetValueOrDefault(ResolveClientId(serverId))?.IsPickupable == true;

    public bool IsUnpassable(uint serverId) =>
        _serverItems.TryGetValue(serverId, out var item)
            ? (item.Flags & 1u) != 0
            : GetObject(serverId)?.Flags?.Unpass == true ||
              _legacyObjects.GetValueOrDefault(ResolveClientId(serverId))?.IsUnpassable == true;

    public ushort GetMinimapColor(uint serverId)
    {
        if (_serverItems.TryGetValue(serverId, out var item) && item.MinimapColor != 0)
            return item.MinimapColor;
        var modern = GetObject(serverId);
        if (modern?.Flags?.Automap is { } automap)
            return (ushort)Math.Clamp((int)automap.Color, 0, ushort.MaxValue);
        var legacy = _legacyObjects.GetValueOrDefault(ResolveClientId(serverId));
        return legacy?.HasMiniMapColor == true ? legacy.MiniMapColor : (ushort)0;
    }

    /// <summary>
    /// Tworzy niezmienny indeks kolorów do pracy w tle. Renderer minimapy nie
    /// odwołuje się dzięki temu do mutowalnych słowników assets ani bitmap UI.
    /// </summary>
    public IReadOnlyDictionary<uint, ushort> CreateMinimapColorLookup()
    {
        IEnumerable<uint> identifiers = _serverItems.Count > 0
            ? _serverItems.Keys
            : _modernObjects.Count > 0
                ? _modernObjects.Keys
                : _legacyObjects.Keys;
        var colors = new Dictionary<uint, ushort>();
        foreach (var identifier in identifiers)
        {
            var color = GetMinimapColor(identifier);
            if (color is > 0 and < 216)
                colors[identifier] = color;
        }
        return colors.ToFrozenDictionary();
    }

    public bool HasWallHook(uint serverId)
    {
        var modern = GetObject(serverId)?.Flags;
        if (modern?.Hook is not null || modern?.HookSouth == true || modern?.HookEast == true) return true;
        var legacy = _legacyObjects.GetValueOrDefault(ResolveClientId(serverId));
        return legacy?.IsHorizontal == true || legacy?.IsVertical == true;
    }

    public (ushort Level, ushort Color)? GetLight(uint serverId)
    {
        var light = GetObject(serverId)?.Flags?.Light;
        if (light is not null)
            return ((ushort)Math.Min(ushort.MaxValue, light.Brightness),
                    (ushort)Math.Min(ushort.MaxValue, light.Color));
        var legacy = _legacyObjects.GetValueOrDefault(ResolveClientId(serverId));
        return legacy?.HasLight == true ? (legacy.LightLevel, legacy.LightColor) : null;
    }

    public bool IsTechnicalItem(uint serverId) => GetTechnicalColor(serverId) is not null ||
                                                   IsPrimalLight(ResolveClientId(serverId));

    /// <summary>Kolor zastępczy niewidzialnych itemów technicznych zgodny z RME.</summary>
    public string? GetTechnicalColor(uint serverId)
    {
        var clientId = ResolveClientId(serverId);
        return clientId switch
        {
            0 => "#AAEF4444",
            469 => "#AAA3A300",
            470 or 17970 or 20028 or 34168 => "#AAEF4444",
            2187 => "#AA22D3EE",
            _ => null
        };
    }

    private static bool IsPrimalLight(uint clientId) =>
        clientId is >= 39092 and <= 39100 or 39236 or 39367 or 39368;

    public int GetAnimationFrameCount(uint serverId)
    {
        var clientId = ResolveClientId(serverId);
        if (Format == MapAssetFormat.ModernAssets &&
            _modernObjects.TryGetValue(clientId, out var modern) &&
            modern.FrameGroup.FirstOrDefault()?.SpriteInfo is { } spriteInfo)
            return Math.Max(1, AppearanceLayoutInfo.From(spriteInfo).Frames);
        if (Format == MapAssetFormat.LegacyDatSpr &&
            _legacyObjects.TryGetValue(clientId, out var legacy) && legacy.FirstGroup is { } group)
            return Math.Max(1, (int)group.Frames);
        return 1;
    }

    /// <summary>
    /// Zwraca parametry rysowania zapisane w DAT/appearances. RME odejmuje displacement
    /// od punktu bazowego sprite'a, a elevation przesuwa kolejne elementy stosu w lewo i w górę.
    /// </summary>
    public MapSpriteRenderMetrics GetRenderMetrics(uint serverId)
    {
        var clientId = ResolveClientId(serverId);
        return GetClientRenderMetrics(clientId);
    }

    /// <summary>
    /// Zwraca grafikę obiektu bezpośrednio po Client ID. Dotyczy to technicznych
    /// markerów RME oraz lookItem/typeex stworzeń; nie wolno mapować ich przez items.otb.
    /// </summary>
    public Bitmap? GetClientObjectThumbnail(uint clientId, int frame = 0)
    {
        var frameCount = GetClientAnimationFrameCount(clientId);
        frame = PositiveModulo(frame, frameCount);
        var key = (clientId, frame);
        if (_clientObjectCache.TryGetValue(key, out var cached)) return cached;
        Bitmap? bitmap;
        try
        {
            bitmap = Format switch
            {
                MapAssetFormat.ModernAssets => RenderModern(clientId, frame, 0, 0, 0, -1),
                MapAssetFormat.LegacyDatSpr when _legacyObjects.TryGetValue(clientId, out var legacy) =>
                    RenderLegacy(legacy, frame, 0, 0, 0, -1),
                _ => null
            };
        }
        catch
        {
            bitmap = null;
        }
        _clientObjectCache[key] = bitmap;
        return bitmap;
    }

    public MapSpriteRenderMetrics GetClientObjectRenderMetrics(uint clientId) => GetClientRenderMetrics(clientId);

    private int GetClientAnimationFrameCount(uint clientId)
    {
        if (Format == MapAssetFormat.ModernAssets &&
            _modernObjects.TryGetValue(clientId, out var modern) &&
            modern.FrameGroup.FirstOrDefault()?.SpriteInfo is { } spriteInfo)
            return Math.Max(1, AppearanceLayoutInfo.From(spriteInfo).Frames);
        if (Format == MapAssetFormat.LegacyDatSpr &&
            _legacyObjects.TryGetValue(clientId, out var legacy) && legacy.FirstGroup is { } group)
            return Math.Max(1, (int)group.Frames);
        return 1;
    }

    private MapSpriteRenderMetrics GetClientRenderMetrics(uint clientId)
    {
        if (Format == MapAssetFormat.ModernAssets &&
            _modernObjects.TryGetValue(clientId, out var modern))
        {
            var shift = modern.Flags?.Shift;
            var height = modern.Flags?.Height;
            return new MapSpriteRenderMetrics(
                shift is null ? 0 : (int)Math.Min(int.MaxValue, shift.X),
                shift is null ? 0 : (int)Math.Min(int.MaxValue, shift.Y),
                height is null ? 0 : (int)Math.Min(int.MaxValue, height.Elevation));
        }

        if (Format == MapAssetFormat.LegacyDatSpr &&
            _legacyObjects.TryGetValue(clientId, out var legacy))
        {
            return new MapSpriteRenderMetrics(
                legacy.HasOffset ? legacy.OffsetX : 0,
                legacy.HasOffset ? legacy.OffsetY : 0,
                legacy.HasElevation ? legacy.Elevation : 0);
        }

        return default;
    }

    public Bitmap? GetThumbnail(
        uint serverId,
        int frame = 0,
        int patternX = 0,
        int patternY = 0,
        int patternZ = 0,
        int subtype = -1)
    {
        var clientId = ResolveClientId(serverId);
        var dimensions = GetLayoutDimensions(clientId, outfit: false);
        subtype = ResolveRenderSubtype(clientId, subtype);
        frame = PositiveModulo(frame, dimensions.Frames);
        patternX = PositiveModulo(patternX, dimensions.PatternX);
        patternY = PositiveModulo(patternY, dimensions.PatternY);
        patternZ = PositiveModulo(patternZ, dimensions.PatternZ);
        var key = (serverId, frame, patternX, patternY, patternZ, subtype);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        Bitmap? bitmap;
        try
        {
            bitmap = Format switch
            {
                MapAssetFormat.ModernAssets => RenderModern(clientId, frame, patternX, patternY, patternZ, subtype),
                MapAssetFormat.LegacyDatSpr => RenderLegacy(clientId, frame, patternX, patternY, patternZ, subtype),
                _ => null
            };
        }
        catch
        {
            bitmap = null;
        }

        _cache[key] = bitmap;
        return bitmap;
    }

    public Bitmap? GetCreatureThumbnail(
        uint lookType,
        uint lookItem = 0,
        int direction = 0,
        int frame = 0)
    {
        // XML potwora/NPC zapisuje typeex jako Client ID obiektu, nie Server ID
        // mapy. Przepuszczenie go przez items.otb potrafiło zamienić outfit np.
        // w ścianę o przypadkowo odpowiadającym identyfikatorze serwerowym.
        if (lookItem != 0) return GetClientObjectThumbnail(lookItem, frame);
        if (lookType == 0) return null;
        var dimensions = GetLayoutDimensions(lookType, outfit: true);
        direction = PositiveModulo(direction, dimensions.PatternX);
        frame = PositiveModulo(frame, dimensions.Frames);
        var key = (lookType, direction, frame);
        if (_outfitCache.TryGetValue(key, out var cached)) return cached;

        Bitmap? bitmap;
        try
        {
            bitmap = Format switch
            {
                MapAssetFormat.ModernAssets when _modernOutfits.TryGetValue(lookType, out var appearance) =>
                    RenderModern(appearance, frame, direction, 0, 0),
                MapAssetFormat.LegacyDatSpr when _legacyOutfits.TryGetValue(lookType, out var thing) =>
                    RenderLegacy(thing, frame, direction, 0, 0),
                _ => null
            };
        }
        catch
        {
            bitmap = null;
        }

        _outfitCache[key] = bitmap;
        return bitmap;
    }

    public int GetCreatureAnimationFrameCount(uint lookType, uint lookItem = 0)
    {
        if (lookItem != 0) return GetClientAnimationFrameCount(lookItem);
        if (lookType == 0) return 1;
        if (Format == MapAssetFormat.ModernAssets &&
            _modernOutfits.TryGetValue(lookType, out var modern) &&
            modern.FrameGroup.FirstOrDefault()?.SpriteInfo is { } spriteInfo)
            return Math.Max(1, AppearanceLayoutInfo.From(spriteInfo).Frames);
        if (Format == MapAssetFormat.LegacyDatSpr &&
            _legacyOutfits.TryGetValue(lookType, out var legacy) && legacy.FirstGroup is { } group)
            return Math.Max(1, (int)group.Frames);
        return 1;
    }

    public void Dispose() => Clear();

    private int LoadModern(string folderPath, string catalogPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _catalog = CatalogReader.Read(catalogPath).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        var appearanceEntry = _catalog.FirstOrDefault(entry =>
            string.Equals(entry.Type, "appearances", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Brak wpisu appearances w catalog-content.json.");
        var appearancePath = Path.Combine(folderPath, appearanceEntry.File);
        if (!File.Exists(appearancePath))
            throw new FileNotFoundException("Brak appearances.dat.", appearancePath);

        using var appearanceStream = new CancellationCheckingReadStream(
            new FileStream(appearancePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan),
            cancellationToken);
        var appearances = new AppearancesReader().Read(appearanceStream);
        cancellationToken.ThrowIfCancellationRequested();
        var appearanceIndex = 0;
        foreach (var appearance in appearances.Object)
        {
            if ((appearanceIndex++ & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            _modernObjects[appearance.Id] = appearance;
        }
        foreach (var appearance in appearances.Outfit)
        {
            if ((appearanceIndex++ & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            _modernOutfits[appearance.Id] = appearance;
        }

        var spriteSheetCount = 0;
        for (var index = 0; index < _catalog.Count; index++)
        {
            if ((index & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(_catalog[index].Type, "sprite", StringComparison.OrdinalIgnoreCase))
                spriteSheetCount++;
        }
        return spriteSheetCount;
    }

    private IReadOnlyList<string> LoadLegacy(string folderPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var datPath = FindClientFile(folderPath, ".dat", cancellationToken)
                      ?? throw new FileNotFoundException("Folder klasycznego klienta nie zawiera Tibia.dat.");
        var sprPath = FindClientFile(folderPath, ".spr", cancellationToken)
                      ?? throw new FileNotFoundException("Folder klasycznego klienta nie zawiera Tibia.spr.");
        var configuration = LegacyClientConfiguration.LoadFromDirectory(folderPath);
        cancellationToken.ThrowIfCancellationRequested();
        DatParserOptions? preferred = null;
        if (configuration.ExtendedSprites.HasValue || configuration.ImprovedAnimations.HasValue || configuration.FrameGroups.HasValue)
        {
            preferred = new DatParserOptions
            {
                MetadataFormat = DatParser.GuessMetadataFormat(ReadSignature(datPath)),
                ExtendedSprites = configuration.ExtendedSprites ?? false,
                ImprovedAnimations = configuration.ImprovedAnimations ?? false,
                FrameGroups = configuration.FrameGroups ?? false
            };
        }
        _legacyDat.LoadAuto(datPath, preferred);
        cancellationToken.ThrowIfCancellationRequested();
        // Tabela SPR sama jednoznacznie określa, czy licznik ma 16 czy 32 bity.
        // Nie wolno wymuszać wariantu wykrytego z DAT: zmodyfikowane klienty OTS
        // często łączą rozszerzony DAT z klasyczną tabelą SPR (i odwrotnie).
        _legacySprites.LoadAuto(sprPath, configuration.Transparency);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = LegacyAssetPairValidator.Validate(_legacyDat.Data, _legacyDat.Options, _legacySprites.Data);
        if (!validation.IsCompatible)
            throw new InvalidDataException(validation.Error);

        var thingIndex = 0;
        foreach (var thing in _legacyDat.Data?.Items ?? [])
        {
            if ((thingIndex++ & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            _legacyObjects[thing.Id] = thing;
        }
        foreach (var thing in _legacyDat.Data?.Outfits ?? [])
        {
            if ((thingIndex++ & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            _legacyOutfits[thing.Id] = thing;
        }
        return validation.Warnings;
    }

    private Bitmap? RenderLegacy(uint clientId, int frame, int patternX, int patternY, int patternZ, int subtype)
    {
        if (!_legacyObjects.TryGetValue(clientId, out var thing)) return null;
        return RenderLegacy(thing, frame, patternX, patternY, patternZ, subtype);
    }

    private Bitmap? RenderLegacy(
        DatThingType thing,
        int frame = 0,
        int patternX = 0,
        int patternY = 0,
        int patternZ = 0,
        int subtype = -1)
    {
        var rendered = LegacyThingRenderer.Render(
            thing,
            _legacySprites,
            0,
            patternX,
            patternY,
            patternZ,
            frame,
            subtype);
        return rendered is null ? null : LegacyBitmapFactory.FromBgra(rendered.Pixels, rendered.Width, rendered.Height);
    }

    private Bitmap? RenderModern(uint clientId, int frame, int patternX, int patternY, int patternZ, int subtype)
    {
        if (!_modernObjects.TryGetValue(clientId, out var appearance) || appearance.FrameGroup.Count == 0)
            return null;
        return RenderModern(appearance, frame, patternX, patternY, patternZ, subtype);
    }

    private Bitmap? RenderModern(
        Appearance appearance,
        int frame = 0,
        int patternX = 0,
        int patternY = 0,
        int patternZ = 0,
        int subtype = -1)
    {
        if (appearance.FrameGroup.Count == 0) return null;
        var spriteInfo = appearance.FrameGroup[0].SpriteInfo;
        if (spriteInfo is null || spriteInfo.SpriteId.Count == 0) return null;

        var layout = AppearanceLayoutInfo.From(spriteInfo);
        var widthTiles = Math.Max(1, layout.TileWidth);
        var heightTiles = Math.Max(1, layout.TileHeight);
        var parts = new List<ModernSpritePart>(widthTiles * heightTiles * Math.Max(1, layout.Layers));

        // W assets 12+ pojedynczy Sprite ID nie musi oznaczac kafla 32x32. Katalog
        // grupuje rowniez gotowe obrazy 32x64, 64x64, 96x96 itd. Poprzedni renderer
        // zawsze tworzyl canvas w wielokrotnosci 32 px i obcinal np. outfit 211 oraz
        // wysokie sciany do lewego-gornego fragmentu. RME rysuje caly region atlasu.
        var cellWidth = 32;
        var cellHeight = 32;
        var safeFrame = PositiveModulo(frame, layout.Frames);
        var safePatternX = PositiveModulo(patternX, layout.PatternX);
        var safePatternY = PositiveModulo(patternY, layout.PatternY);
        var safePatternZ = PositiveModulo(patternZ, layout.PatternZ);
        for (var layer = 0; layer < layout.Layers; layer++)
        for (var tileY = 0; tileY < heightTiles; tileY++)
        for (var tileX = 0; tileX < widthTiles; tileX++)
        {
            var index = subtype >= 0 && widthTiles <= 1 && heightTiles <= 1
                ? spriteInfo.SpriteId.Count <= 1 ? 0 : subtype % spriteInfo.SpriteId.Count
                : safeFrame;
            if (subtype < 0 || widthTiles > 1 || heightTiles > 1)
            {
                index = index * layout.PatternZ + safePatternZ;
                index = index * layout.PatternY + safePatternY;
                index = index * layout.PatternX + safePatternX;
                index = index * layout.Layers + layer;
                index = index * heightTiles + tileY;
                index = index * widthTiles + tileX;
            }
            if (index >= spriteInfo.SpriteId.Count) continue;
            var spriteId = spriteInfo.SpriteId[index];
            if (spriteId == 0) continue;

            var (spriteWidth, spriteHeight) = GetModernSpriteSize(spriteId);
            cellWidth = Math.Max(cellWidth, spriteWidth);
            cellHeight = Math.Max(cellHeight, spriteHeight);
            parts.Add(new ModernSpritePart(spriteId, tileX, tileY));
        }

        var canvasWidth = widthTiles * cellWidth;
        var canvasHeight = heightTiles * cellHeight;
        var canvas = new byte[canvasWidth * canvasHeight * 4];
        foreach (var part in parts)
        {
            var pixels = LoadSpritePixels(part.SpriteId, out var spriteWidth, out var spriteHeight);
            if (pixels is null) continue;
            var drawX = (widthTiles - part.TileX - 1) * cellWidth + Math.Max(0, cellWidth - spriteWidth);
            var drawY = (heightTiles - part.TileY - 1) * cellHeight + Math.Max(0, cellHeight - spriteHeight);
            AlphaBlend(canvas, canvasWidth, canvasHeight, pixels, spriteWidth, spriteHeight, drawX, drawY);
        }

        return CreateBitmapFromBgra(canvas, canvasWidth, canvasHeight);
    }

    private (int Width, int Height) GetModernSpriteSize(uint spriteId)
    {
        var entry = _catalog.FirstOrDefault(candidate =>
            string.Equals(candidate.Type, "sprite", StringComparison.OrdinalIgnoreCase) &&
            candidate.FirstSpriteid <= (int)spriteId && candidate.LastSpriteid >= (int)spriteId);
        if (entry is null) return (32, 32);
        var layout = SpriteSheetLayout.FromSpriteType(entry.SpriteType);
        return (layout.TileWidth, layout.TileHeight);
    }

    private static int PositiveModulo(int value, int modulo)
    {
        if (modulo <= 1) return 0;
        var result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    private (int PatternX, int PatternY, int PatternZ, int Frames) GetLayoutDimensions(uint clientId, bool outfit)
    {
        if (Format == MapAssetFormat.ModernAssets)
        {
            var source = outfit ? _modernOutfits : _modernObjects;
            if (source.TryGetValue(clientId, out var appearance) &&
                appearance.FrameGroup.FirstOrDefault()?.SpriteInfo is { } spriteInfo)
            {
                var layout = AppearanceLayoutInfo.From(spriteInfo);
                return (layout.PatternX, layout.PatternY, layout.PatternZ, layout.Frames);
            }
        }
        else if (Format == MapAssetFormat.LegacyDatSpr)
        {
            var source = outfit ? _legacyOutfits : _legacyObjects;
            if (source.TryGetValue(clientId, out var thing) && thing.FirstGroup is { } group)
                return (
                    Math.Max(1, (int)group.PatternX),
                    Math.Max(1, (int)group.PatternY),
                    Math.Max(1, (int)group.PatternZ),
                    Math.Max(1, (int)group.Frames));
        }

        return (1, 1, 1, 1);
    }

    private int ResolveRenderSubtype(uint clientId, int count)
    {
        if (count < 0) return -1;
        var isStackable = Format switch
        {
            MapAssetFormat.ModernAssets => _modernObjects.GetValueOrDefault(clientId)?.Flags?.Cumulative == true,
            MapAssetFormat.LegacyDatSpr => _legacyObjects.GetValueOrDefault(clientId)?.IsStackable == true,
            _ => false
        };
        if (isStackable)
        {
            return count switch
            {
                <= 1 => 0,
                2 => 1,
                3 => 2,
                4 => 3,
                < 10 => 4,
                < 25 => 5,
                < 50 => 6,
                _ => 7
            };
        }

        var isFluid = Format switch
        {
            MapAssetFormat.ModernAssets => _modernObjects.GetValueOrDefault(clientId)?.Flags is { } flags &&
                                               (flags.Liquidpool || flags.Liquidcontainer),
            MapAssetFormat.LegacyDatSpr => _legacyObjects.GetValueOrDefault(clientId) is { } thing &&
                                               (thing.IsFluid || thing.IsFluidContainer),
            _ => false
        };
        return isFluid ? count : -1;
    }

    private uint ResolveClientId(uint serverId) =>
        _serverItems.TryGetValue(serverId, out var item) ? item.ClientId : serverId;

    private void ValidateMappings(List<string> warnings, CancellationToken cancellationToken)
    {
        if (_serverItems.Count == 0) return;
        var available = 0;
        var index = 0;
        foreach (var item in _serverItems.Values)
        {
            if ((index++ & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (Format switch
                {
                    MapAssetFormat.ModernAssets => _modernObjects.ContainsKey(item.ClientId),
                    MapAssetFormat.LegacyDatSpr => _legacyObjects.ContainsKey(item.ClientId),
                    _ => false
                })
                available++;
        }
        var ratio = available / (double)_serverItems.Count;
        if (ratio < 0.9)
            warnings.Add($"Tylko {available}/{_serverItems.Count} mapowań items.otb ma grafikę; prawdopodobnie wybrano inną wersję klienta.");
    }

    private void LoadItemNames(string directory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "items.xml");
        if (!File.Exists(path)) return;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = 0;
            foreach (var pair in ItemsXmlReader.BuildLookup(ItemsXmlReader.Read(path)))
            {
                if ((index++ & 0xFF) == 0) cancellationToken.ThrowIfCancellationRequested();
                _itemNames[pair.Key] = pair.Value;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Nazwy są dodatkiem; uszkodzony items.xml nie może blokować grafiki mapy.
        }
    }

    private static string? FindItemsOtb(
        string folderPath,
        uint? expectedMinor,
        CancellationToken cancellationToken)
    {
        var current = new DirectoryInfo(folderPath);
        for (var depth = 0; current is not null && depth < 4; depth++, current = current.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var direct = Path.Combine(current.FullName, "items.otb");
            if (File.Exists(direct)) return direct;
        }

        if (expectedMinor is null) return null;
        foreach (var dataRoot in FindRmeDataRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = ResolveRmeItemsOtb(dataRoot, expectedMinor.Value);
            if (resolved is not null) return resolved;
        }
        return null;
    }

    private static IEnumerable<string> FindRmeDataRoots()
    {
        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var candidate = Path.GetFullPath(Path.Combine(root, "rme-data"));
            if (checkedPaths.Add(candidate) && File.Exists(Path.Combine(candidate, "clients.xml")))
                yield return candidate;
        }
    }

    private static string? ResolveRmeItemsOtb(string dataRoot, uint expectedMinor)
    {
        try
        {
            var document = XDocument.Load(Path.Combine(dataRoot, "clients.xml"));
            var otbName = document.Root?.Element("otbs")?.Elements("otb")
                .FirstOrDefault(node => uint.TryParse(node.Attribute("id")?.Value, out var id) && id == expectedMinor)
                ?.Attribute("client")?.Value;
            if (otbName is null) return null;

            var directory = document.Root?.Element("clients")?.Elements("client")
                .FirstOrDefault(node => string.Equals(node.Attribute("otb")?.Value, otbName, StringComparison.OrdinalIgnoreCase))
                ?.Attribute("data_directory")?.Value;
            if (directory is null) return null;
            var path = Path.Combine(dataRoot, directory, "items.otb");
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string? FindClientFile(
        string directory,
        string extension,
        CancellationToken cancellationToken)
    {
        var preferred = Path.Combine(directory, "Tibia" + extension);
        if (File.Exists(preferred)) return preferred;
        string? result = null;
        foreach (var path in Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result is null || StringComparer.OrdinalIgnoreCase.Compare(path, result) < 0)
                result = path;
        }
        return result;
    }

    private static uint ReadSignature(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < sizeof(uint)) throw new InvalidDataException("Plik DAT jest za mały.");
        return reader.ReadUInt32();
    }

    private byte[]? LoadSpritePixels(uint spriteId, out int width, out int height)
    {
        width = 32;
        height = 32;
        if (_folderPath is null) return null;

        var entry = _catalog.FirstOrDefault(candidate =>
            string.Equals(candidate.Type, "sprite", StringComparison.OrdinalIgnoreCase) &&
            candidate.FirstSpriteid <= (int)spriteId && candidate.LastSpriteid >= (int)spriteId);
        if (entry is null) return null;

        var sheetPath = Path.Combine(_folderPath, entry.File);
        if (!File.Exists(sheetPath)) return null;
        var sheetLayout = SpriteSheetLayout.FromSpriteType(entry.SpriteType);
        width = sheetLayout.TileWidth;
        height = sheetLayout.TileHeight;
        var spriteWidth = width;
        var spriteHeight = height;

        if (!_modernSheets.TryGetValue(sheetPath, out var sheet))
        {
            var imageBytes = LzmaImageCodec.ReadImageBytes(sheetPath);
            using var stream = new MemoryStream(imageBytes);
            sheet = Image.Load<Bgra32>(stream);
            _modernSheets[sheetPath] = sheet;
        }
        var index = (int)spriteId - entry.FirstSpriteid;
        var column = index % sheetLayout.Columns;
        var row = index / sheetLayout.Columns;
        if (row >= sheetLayout.Rows) return null;

        using var crop = sheet.Clone(context =>
            context.Crop(new Rectangle(column * spriteWidth, row * spriteHeight, spriteWidth, spriteHeight)));
        var pixels = new byte[width * height * 4];
        crop.CopyPixelDataTo(pixels);
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 2] == 255 && pixels[i + 1] == 0 && pixels[i] == 255)
                pixels[i + 3] = 0;
        }
        return pixels;
    }

    private static void AlphaBlend(
        byte[] destination,
        int destinationWidth,
        int destinationHeight,
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        int x,
        int y)
    {
        for (var sourceY = 0; sourceY < sourceHeight; sourceY++)
        for (var sourceX = 0; sourceX < sourceWidth; sourceX++)
        {
            var destinationX = x + sourceX;
            var destinationY = y + sourceY;
            if (destinationX < 0 || destinationX >= destinationWidth || destinationY < 0 || destinationY >= destinationHeight)
                continue;
            var sourceIndex = (sourceY * sourceWidth + sourceX) * 4;
            var sourceAlpha = source[sourceIndex + 3];
            if (sourceAlpha == 0) continue;
            var destinationIndex = (destinationY * destinationWidth + destinationX) * 4;
            if (sourceAlpha == byte.MaxValue || destination[destinationIndex + 3] == 0)
            {
                Buffer.BlockCopy(source, sourceIndex, destination, destinationIndex, 4);
                continue;
            }

            var destinationAlpha = destination[destinationIndex + 3];
            var inverseAlpha = byte.MaxValue - sourceAlpha;
            var outputAlpha = sourceAlpha + (destinationAlpha * inverseAlpha + 127) / 255;
            if (outputAlpha == 0) continue;
            for (var channel = 0; channel < 3; channel++)
            {
                var sourcePremultiplied = source[sourceIndex + channel] * sourceAlpha;
                var destinationPremultiplied = destination[destinationIndex + channel] * destinationAlpha * inverseAlpha / 255;
                destination[destinationIndex + channel] = (byte)Math.Clamp(
                    (sourcePremultiplied + destinationPremultiplied + outputAlpha / 2) / outputAlpha,
                    0,
                    byte.MaxValue);
            }
            destination[destinationIndex + 3] = (byte)outputAlpha;
        }
    }

    private static Bitmap CreateBitmapFromBgra(byte[] bgra, int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new Avalonia.PixelSize(width, height),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        using var locked = bitmap.Lock();
        for (var row = 0; row < height; row++)
        {
            var destination = IntPtr.Add(locked.Address, row * locked.RowBytes);
            System.Runtime.InteropServices.Marshal.Copy(bgra, row * width * 4, destination, width * 4);
        }
        return bitmap;
    }

    private void Clear()
    {
        foreach (var bitmap in _cache.Values) bitmap?.Dispose();
        foreach (var bitmap in _outfitCache.Values) bitmap?.Dispose();
        foreach (var bitmap in _clientObjectCache.Values) bitmap?.Dispose();
        foreach (var sheet in _modernSheets.Values) sheet.Dispose();
        _cache.Clear();
        _outfitCache.Clear();
        _clientObjectCache.Clear();
        _modernSheets.Clear();
        _modernObjects.Clear();
        _modernOutfits.Clear();
        _legacyObjects.Clear();
        _legacyOutfits.Clear();
        _serverItems.Clear();
        _itemNames.Clear();
        _catalog.Clear();
        _folderPath = null;
        _itemsOtbPath = null;
        Format = MapAssetFormat.None;
    }

    /// <summary>
    /// Dekorator strumienia sprawdzający anulowanie przy każdym pobraniu kolejnej
    /// porcji danych. Dzięki temu parser protobuf appearances.dat nie musi być
    /// zmieniany, a duży plik przestaje być czytany po kliknięciu Anuluj.
    /// </summary>
    private sealed class CancellationCheckingReadStream(Stream inner, CancellationToken cancellationToken) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.Read(buffer);
        }

        public override int ReadByte()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReadByte();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    public sealed record LoadReport(
        string FolderPath,
        int Objects,
        int SpriteSheets,
        MapAssetFormat Format,
        string? ItemsOtbPath,
        uint? OtbMajorVersion,
        uint? OtbMinorVersion,
        IReadOnlyList<string> Warnings);
}

internal readonly record struct ModernSpritePart(
    uint SpriteId,
    int TileX,
    int TileY);

public readonly record struct MapSpriteRenderMetrics(int OffsetX, int OffsetY, int Elevation);

public enum MapAssetFormat
{
    None,
    LegacyDatSpr,
    ModernAssets
}
