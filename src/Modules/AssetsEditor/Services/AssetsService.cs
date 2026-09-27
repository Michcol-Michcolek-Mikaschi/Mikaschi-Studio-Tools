using Narzedzia.Core.Appearances;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.AssetsEditor.Services;

public sealed class AssetsService : IAssetsService
{
    private Appearances?       _appearances;
    private List<CatalogEntry> _catalog     = [];
    private string?            _folderPath;

    private readonly Dictionary<int, byte[]>        _pixelCache = new();
    private readonly Dictionary<int, (int W, int H)> _sizeCache  = new();
    private readonly object _cacheLock = new();

    public bool IsLoaded        => _appearances is not null;
    public bool IsLegacyFormat  { get; private set; }

    public Appearances?                AppearancesData => _appearances;
    public IReadOnlyList<CatalogEntry> Catalog         => _catalog;
    public string?                     FolderPath      => _folderPath;

    public void LoadFromFolder(string folderPath)
    {
        _folderPath = folderPath;
        IsLegacyFormat = false;
        _pixelCache.Clear();
        _sizeCache.Clear();

        var catalogPath = Path.Combine(folderPath, "catalog-content.json");
        _catalog = CatalogReader.Read(catalogPath);

        var appEntry = _catalog.FirstOrDefault(e =>
            string.Equals(e.Type, "appearances", StringComparison.OrdinalIgnoreCase));

        if (appEntry is not null)
        {
            var appPath = Path.Combine(folderPath, appEntry.File);
            _appearances = new AppearancesReader().Read(appPath);
        }
    }

    public byte[]? GetSpritePixels(uint spriteId)
    {
        if (_folderPath is null) return null;

        lock (_cacheLock)
        {
            if (_pixelCache.TryGetValue((int)spriteId, out var cached)) return cached;

            LoadSheetForSprite(spriteId);
            return _pixelCache.TryGetValue((int)spriteId, out var result) ? result : null;
        }
    }

    public (int Width, int Height) GetSpriteSize(uint spriteId)
    {
        lock (_cacheLock)
        {
            if (_sizeCache.TryGetValue((int)spriteId, out var sz)) return sz;
            var entry = FindEntry(spriteId);
            if (entry is null) return (32, 32);
            var layout = SpriteSheetLayout.FromSpriteType(entry.SpriteType);
            var size = (layout.TileWidth, layout.TileHeight);
            _sizeCache[(int)spriteId] = size;
            return size;
        }
    }

    public IReadOnlyList<AppearanceSpritePart> GetSpriteSlots(Appearance appearance, int groupIndex) =>
        AppearanceTextureLayout.EnumerateSpriteSlots(appearance, groupIndex);

    public RenderedSpriteImage? RenderAppearance(Appearance appearance, AppearanceRenderOptions options)
    {
        var group = AppearanceTextureLayout.GetFrameGroup(appearance, options.GroupIndex);
        var spriteInfo = group?.SpriteInfo;
        if (group is null || spriteInfo is null)
        {
            return null;
        }

        var parts = AppearanceTextureLayout.EnumerateVisibleSprites(appearance, options);
        if (parts.Count == 0)
        {
            return null;
        }

        var firstSpriteId = parts.FirstOrDefault(part => part.SpriteId != 0)?.SpriteId ?? 0;
        var (fallbackW, fallbackH) = firstSpriteId == 0
            ? (32, 32)
            : GetSpriteSize(firstSpriteId);

        var layout = AppearanceLayoutInfo.From(spriteInfo);
        var isOutfit = appearance.AppearanceType == APPEARANCE_TYPE.AppearanceOutfit;
        // Pociski (Missile) z patternX>1 i patternY>1 układają 8 kierunków w grid 3×3 (centrum puste),
        // tak jak WPF DatEditor. Zamiast nakładać wszystkie patternX×patternY w (0,0), rozkładamy je
        // na osobnych komórkach canvas.
        var isMissileGrid = appearance.AppearanceType == APPEARANCE_TYPE.AppearanceMissile
                            && layout.PatternX > 1 && layout.PatternY > 1
                            && layout.TileWidth == 1 && layout.TileHeight == 1;

        var (tileW, tileH) = ResolveRenderCellSize(parts, fallbackW, fallbackH);
        var canvasW = isMissileGrid
            ? Math.Max(1, layout.PatternX * tileW)
            : Math.Max(1, layout.TileWidth * tileW);
        var canvasH = isMissileGrid
            ? Math.Max(1, layout.PatternY * tileH)
            : Math.Max(1, layout.TileHeight * tileH);

        using var canvas = new Image<Bgra32>(canvasW, canvasH, new Bgra32(0, 0, 0, 0));
        if (isOutfit && options.ColorizeOutfit && options.BlendLayers && AppearanceTextureLayout.GetLayers(spriteInfo) > 1)
        {
            DrawColorizedOutfit(canvas, parts, tileW, tileH, layout.TileWidth, layout.TileHeight, options);
            var colorizedOutput = new byte[canvasW * canvasH * 4];
            canvas.CopyPixelDataTo(colorizedOutput);
            return new RenderedSpriteImage(colorizedOutput, canvasW, canvasH);
        }

        foreach (var part in parts)
        {
            if (part.SpriteId == 0)
            {
                continue;
            }

            var pixels = GetSpritePixels(part.SpriteId);
            if (pixels is null)
            {
                continue;
            }

            var (spriteW, spriteH) = GetSpriteSize(part.SpriteId);
            if (pixels.Length < spriteW * spriteH * 4)
            {
                continue;
            }

            int cellX, cellY;
            if (isMissileGrid)
            {
                cellX = part.PatternX * tileW;
                cellY = part.PatternY * tileH;
            }
            else
            {
                cellX = (layout.TileWidth - part.TileX - 1) * tileW;
                cellY = (layout.TileHeight - part.TileY - 1) * tileH;
            }
            var x = cellX + Math.Max(0, tileW - spriteW);
            var y = cellY + Math.Max(0, tileH - spriteH);
            DrawTile(canvas, pixels, spriteW, spriteH, x, y);
        }

        var output = new byte[canvasW * canvasH * 4];
        canvas.CopyPixelDataTo(output);
        return new RenderedSpriteImage(output, canvasW, canvasH);
    }

    private void DrawColorizedOutfit(
        Image<Bgra32> canvas,
        IReadOnlyList<AppearanceSpritePart> parts,
        int tileW,
        int tileH,
        int layoutTileWidth,
        int layoutTileHeight,
        AppearanceRenderOptions options)
    {
        var colors = OutfitColorSet.FromHsi(options.HeadColor, options.BodyColor, options.LegsColor, options.FeetColor);
        foreach (var basePart in parts.Where(part => part.Layer == 0 && part.SpriteId != 0))
        {
            var basePixels = GetSpritePixels(basePart.SpriteId);
            if (basePixels is null)
            {
                continue;
            }

            var (spriteW, spriteH) = GetSpriteSize(basePart.SpriteId);
            if (basePixels.Length < spriteW * spriteH * 4)
            {
                continue;
            }

            var templatePart = parts.FirstOrDefault(part =>
                part.Layer == 1 &&
                part.PatternX == basePart.PatternX &&
                part.PatternY == basePart.PatternY &&
                part.PatternZ == basePart.PatternZ &&
                part.Frame == basePart.Frame &&
                part.TileX == basePart.TileX &&
                part.TileY == basePart.TileY &&
                part.SpriteId != 0);

            var renderPixels = basePixels;
            if (templatePart is { SpriteId: > 0 } && GetSpritePixels(templatePart.SpriteId) is { } templatePixels)
            {
                renderPixels = OutfitColorizer.ApplyTemplate(templatePixels, basePixels, spriteW, spriteH, colors);
            }

            var cellX = (layoutTileWidth - basePart.TileX - 1) * tileW;
            var cellY = (layoutTileHeight - basePart.TileY - 1) * tileH;
            var x = cellX + Math.Max(0, tileW - spriteW);
            var y = cellY + Math.Max(0, tileH - spriteH);
            DrawTile(canvas, renderPixels, spriteW, spriteH, x, y);
        }
    }

    private (int Width, int Height) ResolveRenderCellSize(
        IReadOnlyList<AppearanceSpritePart> parts,
        int fallbackWidth,
        int fallbackHeight)
    {
        var maxWidth = Math.Max(1, fallbackWidth);
        var maxHeight = Math.Max(1, fallbackHeight);

        foreach (var part in parts)
        {
            if (part.SpriteId == 0)
            {
                continue;
            }

            var (width, height) = GetSpriteSize(part.SpriteId);
            maxWidth = Math.Max(maxWidth, Math.Max(1, width));
            maxHeight = Math.Max(maxHeight, Math.Max(1, height));
        }

        return (maxWidth, maxHeight);
    }

    private static void DrawTile(Image<Bgra32> canvas, byte[] pixels, int width, int height, int x, int y)
    {
        using var tile = Image.LoadPixelData<Bgra32>(pixels, width, height);
        canvas.Mutate(ctx => ctx.DrawImage(tile, new Point(x, y), 1f));
    }

    public IAssetSpriteStore CreateSpriteStore()
    {
        if (_folderPath is null)
        {
            throw new InvalidOperationException("Najpierw wczytaj folder assetow.");
        }

        return new AssetSpriteStore(_folderPath, _catalog, Path.Combine(_folderPath, "catalog-content.json"));
    }

    public void InvalidateSpriteCache()
    {
        lock (_cacheLock)
        {
            _pixelCache.Clear();
            _sizeCache.Clear();
        }
    }

    private CatalogEntry? FindEntry(uint id) =>
        _catalog.FirstOrDefault(e =>
            string.Equals(e.Type, "sprite", StringComparison.OrdinalIgnoreCase) &&
            e.FirstSpriteid <= (int)id && e.LastSpriteid >= (int)id);

    private void LoadSheetForSprite(uint spriteId)
    {
        if (_folderPath is null) return;
        var entry = FindEntry(spriteId);
        if (entry is null) return;

        var sheetPath = Path.Combine(_folderPath, entry.File);
        if (!File.Exists(sheetPath)) return;

        try
        {
            var imageBytes = LzmaImageCodec.ReadImageBytes(sheetPath);

            var layout = SpriteSheetLayout.FromSpriteType(entry.SpriteType);
            var tileW = layout.TileWidth;
            var tileH = layout.TileHeight;

            using var ms    = new MemoryStream(imageBytes);
            using var sheet = Image.Load<Bgra32>(ms);
            int cols = layout.Columns;
            int rows = layout.Rows;
            int idx  = 0;

            for (int r = 0; r < rows && entry.FirstSpriteid + idx <= entry.LastSpriteid; r++)
            for (int c = 0; c < cols && entry.FirstSpriteid + idx <= entry.LastSpriteid; c++, idx++)
            {
                int sid = entry.FirstSpriteid + idx;

                var tile = new byte[tileW * tileH * 4];
                using var crop = sheet.Clone(ctx =>
                    ctx.Crop(new Rectangle(c * tileW, r * tileH, tileW, tileH)));
                crop.CopyPixelDataTo(tile.AsSpan());
                MakeMagentaTransparent(tile);

                _pixelCache[sid] = tile;
                _sizeCache[sid]  = (tileW, tileH);
            }
        }
        catch { /* ignoruj uszkodzone arkusze */ }
    }

    private static void MakeMagentaTransparent(byte[] pixels)
    {
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];

            if (r == 255 && g == 0 && b == 255)
            {
                pixels[i + 3] = 0;
            }
        }
    }

    public void Dispose()
    {
        _pixelCache.Clear();
        _sizeCache.Clear();
    }
}
