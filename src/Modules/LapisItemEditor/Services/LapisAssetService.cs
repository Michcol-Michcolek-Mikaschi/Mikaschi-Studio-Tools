using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Narzedzia.Core.Appearances;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.LapisItemEditor.Services;

/// <summary>
/// Wczytuje assets klienta Tibia 12+ (catalog-content.json + appearances.dat + arkusze sprite LZMA)
/// i udostępnia miniaturę sprite oraz appearance po ClientId. Niezależny od modułu AssetsEditor —
/// korzysta tylko z prymitywów w <see cref="Narzedzia.Core"/>.
/// </summary>
public sealed class LapisAssetService
{
    // Słowniki PER KATEGORIA. ClientId z items.otb ZAWSZE mapuje na Object —
    // Outfit/Effect/Missile mają oddzielne numeracje Id i nigdy nie mieszają się z itemami.
    private readonly Dictionary<uint, Appearance> _objectAppearances = new();
    private readonly Dictionary<uint, Appearance> _outfitAppearances = new();
    private readonly Dictionary<uint, Appearance> _effectAppearances = new();
    private readonly Dictionary<uint, Appearance> _missileAppearances = new();
    private readonly Dictionary<uint, Bitmap?> _thumbnailCache = new();
    private List<CatalogEntry> _catalog = new();
    private string? _folderPath;

    public bool IsLoaded => !string.IsNullOrEmpty(_folderPath) && _objectAppearances.Count > 0;
    public int CatalogEntryCount  => _catalog.Count;
    public int AppearanceCount    => _objectAppearances.Count;
    public string? FolderPath     => _folderPath;

    /// <summary>
    /// Wczytuje folder z assetami Tibia 12+. Wymaga obecności catalog-content.json + appearances.dat.
    /// </summary>
    public LoadReport LoadFromFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder assets nie istnieje: {folderPath}");
        }

        _folderPath = folderPath;
        _objectAppearances.Clear();
        _outfitAppearances.Clear();
        _effectAppearances.Clear();
        _missileAppearances.Clear();
        _thumbnailCache.Clear();

        var catalogPath = Path.Combine(folderPath, "catalog-content.json");
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException("Brak catalog-content.json w folderze assets.", catalogPath);
        }
        _catalog = CatalogReader.Read(catalogPath).ToList();

        var appEntry = _catalog.FirstOrDefault(e =>
            string.Equals(e.Type, "appearances", StringComparison.OrdinalIgnoreCase));
        if (appEntry is null)
        {
            throw new InvalidDataException("Brak wpisu type=\"appearances\" w catalog-content.json.");
        }

        var appPath = Path.Combine(folderPath, appEntry.File);
        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException("Brak pliku appearances.dat.", appPath);
        }
        var appearances = new AppearancesReader().Read(appPath);

        // Indeksuj per kategoria. OTB ClientId mapuje WYŁĄCZNIE na Object — outfit/effect/missile
        // mają osobne numeracje (np. Outfit Id=1 = "citizen", a Object Id=1 nigdy nie istnieje).
        foreach (var a in appearances.Object)  _objectAppearances[a.Id] = a;
        foreach (var a in appearances.Outfit)  _outfitAppearances[a.Id] = a;
        foreach (var a in appearances.Effect)  _effectAppearances[a.Id] = a;
        foreach (var a in appearances.Missile) _missileAppearances[a.Id] = a;

        var spriteSheetCount = _catalog.Count(e =>
            string.Equals(e.Type, "sprite", StringComparison.OrdinalIgnoreCase));

        return new LoadReport(
            FolderPath: folderPath,
            AppearancesPath: appPath,
            CatalogEntries: _catalog.Count,
            SpriteSheets: spriteSheetCount,
            Objects: appearances.Object.Count,
            Outfits: appearances.Outfit.Count,
            Effects: appearances.Effect.Count,
            Missiles: appearances.Missile.Count);
    }

    public Appearance? GetAppearanceByClientId(uint clientId)
    {
        return _objectAppearances.TryGetValue(clientId, out var a) ? a : null;
    }

    public Appearance? GetOutfit(uint id)  => _outfitAppearances.TryGetValue(id, out var a) ? a : null;
    public Appearance? GetEffect(uint id)  => _effectAppearances.TryGetValue(id, out var a) ? a : null;
    public Appearance? GetMissile(uint id) => _missileAppearances.TryGetValue(id, out var a) ? a : null;

    /// <summary>
    /// Zwraca miniaturę pierwszego sprite z FrameGroup[0] dla wskazanego ClientId.
    /// Cache'uje wyniki; zwraca null gdy nie znaleziono appearance lub sheeta.
    /// </summary>
    public Bitmap? GetThumbnail(uint clientId)
    {
        if (_thumbnailCache.TryGetValue(clientId, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            var appearance = GetAppearanceByClientId(clientId);
            if (appearance is null || appearance.FrameGroup.Count == 0) goto cache;
            var spriteInfo = appearance.FrameGroup[0].SpriteInfo;
            if (spriteInfo is null || spriteInfo.SpriteId.Count == 0) goto cache;

            var spriteId = spriteInfo.SpriteId.FirstOrDefault(id => id != 0);
            if (spriteId == 0) goto cache;

            var pixels = LoadSpritePixels(spriteId, out var w, out var h);
            if (pixels is null) goto cache;

            bitmap = CreateBitmapFromBgra(pixels, w, h);
        }
        catch
        {
            bitmap = null;
        }

        cache:
        _thumbnailCache[clientId] = bitmap;
        return bitmap;
    }

    private byte[]? LoadSpritePixels(uint spriteId, out int width, out int height)
    {
        width = 32;
        height = 32;
        if (_folderPath is null) return null;

        var entry = _catalog.FirstOrDefault(e =>
            string.Equals(e.Type, "sprite", StringComparison.OrdinalIgnoreCase) &&
            e.FirstSpriteid <= (int)spriteId && e.LastSpriteid >= (int)spriteId);
        if (entry is null) return null;

        var sheetPath = Path.Combine(_folderPath, entry.File);
        if (!File.Exists(sheetPath)) return null;

        var imageBytes = LzmaImageCodec.ReadImageBytes(sheetPath);
        var layout = SpriteSheetLayout.FromSpriteType(entry.SpriteType);
        var tileW = layout.TileWidth;
        var tileH = layout.TileHeight;
        width = tileW;
        height = tileH;

        using var ms = new MemoryStream(imageBytes);
        using var sheet = Image.Load<Bgra32>(ms);

        var index = (int)spriteId - entry.FirstSpriteid;
        var col = index % layout.Columns;
        var row = index / layout.Columns;
        if (row >= layout.Rows) return null;

        using var crop = sheet.Clone(ctx =>
            ctx.Crop(new Rectangle(col * tileW, row * tileH, tileW, tileH)));
        var pixels = new byte[tileW * tileH * 4];
        crop.CopyPixelDataTo(pixels);
        MakeMagentaTransparent(pixels);
        return pixels;
    }

    private static void MakeMagentaTransparent(byte[] pixels)
    {
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 2] == 255 && pixels[i + 1] == 0 && pixels[i] == 255)
            {
                pixels[i + 3] = 0;
            }
        }
    }

    private static Bitmap CreateBitmapFromBgra(byte[] bgraPixels, int width, int height)
    {
        var bmp = new WriteableBitmap(
            new Avalonia.PixelSize(width, height),
            new Avalonia.Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
        using (var locked = bmp.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(
                bgraPixels, 0, locked.Address, bgraPixels.Length);
        }
        return bmp;
    }

    public sealed record LoadReport(
        string FolderPath,
        string AppearancesPath,
        int CatalogEntries,
        int SpriteSheets,
        int Objects,
        int Outfits,
        int Effects,
        int Missiles);
}
