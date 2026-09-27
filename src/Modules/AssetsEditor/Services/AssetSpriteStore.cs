using Narzedzia.Core.Assets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Modules.AssetsEditor.Services;

public interface IAssetSpriteStore : IDisposable
{
    IReadOnlyList<CatalogEntry> Catalog { get; }
    AssetSpriteSheet OpenSheet(uint spriteId);
    AssetSpriteSheet CreateSheet(int spriteType, uint firstSpriteId);
    byte[]? ReadTile(AssetSpriteSheet sheet, int tileIndex);
    void ReplaceTile(AssetSpriteSheet sheet, int tileIndex, byte[] bgraPixels);
    void ImportImageIntoSheet(AssetSpriteSheet sheet, string imagePath, int startTile);
    int ImportImagesIntoSheet(AssetSpriteSheet sheet, IReadOnlyList<string> imagePaths, int startTile);
    void ExportSheet(AssetSpriteSheet sheet, string outputPath);
    void SaveSheet(AssetSpriteSheet sheet);
    void SaveCatalog();
}

public sealed class AssetSpriteStore : IAssetSpriteStore
{
    private readonly List<CatalogEntry> _catalog;
    private readonly string? _assetsFolder;
    private readonly string? _catalogPath;
    private readonly Dictionary<string, Image<Bgra32>> _sheetCache = new(StringComparer.OrdinalIgnoreCase);

    public AssetSpriteStore(IReadOnlyList<CatalogEntry> catalog)
        : this(null, catalog, null)
    {
    }

    public AssetSpriteStore(string assetsFolder, List<CatalogEntry> catalog, string catalogPath)
        : this((string?)assetsFolder, (IReadOnlyList<CatalogEntry>)catalog, catalogPath)
    {
    }

    private AssetSpriteStore(string? assetsFolder, IReadOnlyList<CatalogEntry> catalog, string? catalogPath)
    {
        _assetsFolder = assetsFolder;
        _catalogPath = catalogPath;
        _catalog = catalog as List<CatalogEntry> ?? catalog.ToList();
    }

    public IReadOnlyList<CatalogEntry> Catalog => _catalog;

    public AssetSpriteSheet OpenSheet(uint spriteId)
    {
        var entry = FindSheetForSprite(_catalog, spriteId)
            ?? throw new InvalidOperationException($"Nie znaleziono arkusza dla sprite ID {spriteId}.");

        return AssetSpriteSheet.FromCatalogEntry(entry);
    }

    public AssetSpriteSheet CreateSheet(int spriteType, uint firstSpriteId)
    {
        EnsureAssetsFolder();

        var layout = SpriteSheetLayout.FromSpriteType(spriteType);
        var lastSpriteId = checked(firstSpriteId + (uint)layout.TileCount - 1);
        var fileName = CreateUniqueSheetFileName(firstSpriteId, layout.SpriteType);

        var entry = new CatalogEntry
        {
            Type = "sprite",
            File = fileName,
            SpriteType = layout.SpriteType,
            FirstSpriteid = checked((int)firstSpriteId),
            LastSpriteid = checked((int)lastSpriteId)
        };

        _catalog.Add(entry);
        var sheet = AssetSpriteSheet.FromCatalogEntry(entry);
        _sheetCache[sheet.File] = CreateEmptySheet();
        return sheet;
    }

    public void ReplaceTile(AssetSpriteSheet sheet, int tileIndex, byte[] bgraPixels)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        ValidateTileIndex(sheet, tileIndex);
        var expectedLength = sheet.TileWidth * sheet.TileHeight * 4;
        if (bgraPixels.Length < expectedLength)
        {
            throw new ArgumentException($"Kafel wymaga co najmniej {expectedLength} bajtow BGRA.", nameof(bgraPixels));
        }

        var image = LoadSheetImage(sheet);
        var point = GetTilePoint(sheet, tileIndex);

        // Bezpośrednia kopia pikseli (REPLACE) — pomijamy DrawImage żeby uniknąć alpha
        // compositingu (SrcOver), który mieszałby alfę nowego tile'a z istniejącą zawartością.
        image.ProcessPixelRows(accessor =>
        {
            for (var ty = 0; ty < sheet.TileHeight; ty++)
            {
                var dst = accessor.GetRowSpan(point.Y + ty).Slice(point.X, sheet.TileWidth);
                var srcOffset = ty * sheet.TileWidth * 4;
                for (var tx = 0; tx < sheet.TileWidth; tx++)
                {
                    var p = srcOffset + tx * 4;
                    dst[tx] = new Bgra32(bgraPixels[p + 2], bgraPixels[p + 1], bgraPixels[p], bgraPixels[p + 3]);
                }
            }
        });
    }

    public byte[]? ReadTile(AssetSpriteSheet sheet, int tileIndex)
    {
        ValidateTileIndex(sheet, tileIndex);
        var image = LoadSheetImage(sheet);
        var point = GetTilePoint(sheet, tileIndex);

        using var tile = image.Clone(ctx => ctx.Crop(new Rectangle(point.X, point.Y, sheet.TileWidth, sheet.TileHeight)));
        var pixels = new byte[sheet.TileWidth * sheet.TileHeight * 4];
        tile.CopyPixelDataTo(pixels);
        MakeMagentaTransparent(pixels);
        return pixels;
    }

    public byte[] ReadSheetPixels(AssetSpriteSheet sheet)
    {
        var image = LoadSheetImage(sheet);
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }

    public void ImportImageIntoSheet(AssetSpriteSheet sheet, string imagePath, int startTile)
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Nie znaleziono obrazu do importu.", imagePath);
        }

        ValidateTileIndex(sheet, startTile);
        using var input = Image.Load<Bgra32>(imagePath);
        ImportImageIntoSheet(sheet, input, startTile);
    }

    public int ImportImagesIntoSheet(AssetSpriteSheet sheet, IReadOnlyList<string> imagePaths, int startTile)
    {
        ArgumentNullException.ThrowIfNull(imagePaths);
        ValidateTileIndex(sheet, startTile);

        var tileIndex = startTile;
        foreach (var imagePath in imagePaths)
        {
            if (tileIndex >= sheet.TileCount)
            {
                break;
            }

            if (!File.Exists(imagePath))
            {
                throw new FileNotFoundException("Nie znaleziono obrazu do importu.", imagePath);
            }

            using var input = Image.Load<Bgra32>(imagePath);
            tileIndex += ImportImageIntoSheet(sheet, input, tileIndex);
        }

        return tileIndex - startTile;
    }

    private int ImportImageIntoSheet(AssetSpriteSheet sheet, Image<Bgra32> input, int startTile)
    {
        var tilesX = Math.Max(1, input.Width / sheet.TileWidth);
        var tilesY = Math.Max(1, input.Height / sheet.TileHeight);
        var tileIndex = startTile;

        for (var y = 0; y < tilesY && tileIndex < sheet.TileCount; y++)
        {
            for (var x = 0; x < tilesX && tileIndex < sheet.TileCount; x++, tileIndex++)
            {
                using var tile = ExtractImportTile(input, sheet, x, y);
                var pixels = new byte[sheet.TileWidth * sheet.TileHeight * 4];
                tile.CopyPixelDataTo(pixels);
                ReplaceTile(sheet, tileIndex, pixels);
            }
        }

        return tileIndex - startTile;
    }

    public void ExportSheet(AssetSpriteSheet sheet, string outputPath)
    {
        var image = LoadSheetImage(sheet);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

        if (outputPath.EndsWith(".lzma", StringComparison.OrdinalIgnoreCase))
        {
            SaveCompressedBmp(image, outputPath);
            return;
        }

        image.Save(outputPath);
    }

    public void SaveSheet(AssetSpriteSheet sheet)
    {
        EnsureAssetsFolder();
        var image = LoadSheetImage(sheet);
        SaveCompressedBmp(image, GetSheetPath(sheet));
    }

    public void SaveCatalog()
    {
        if (string.IsNullOrWhiteSpace(_catalogPath))
        {
            throw new InvalidOperationException("Nie ustawiono sciezki catalog-content.json.");
        }

        CatalogWriter.Write(_catalogPath, _catalog);
    }

    public static CatalogEntry? FindSheetForSprite(IEnumerable<CatalogEntry> catalog, uint spriteId)
    {
        return catalog.FirstOrDefault(entry =>
            string.Equals(entry.Type, "sprite", StringComparison.OrdinalIgnoreCase) &&
            Contains(entry, spriteId));
    }

    public void Dispose()
    {
        foreach (var image in _sheetCache.Values)
        {
            image.Dispose();
        }

        _sheetCache.Clear();
    }

    private Image<Bgra32> LoadSheetImage(AssetSpriteSheet sheet)
    {
        if (_sheetCache.TryGetValue(sheet.File, out var cached))
        {
            return cached;
        }

        var path = GetSheetPath(sheet);
        if (!File.Exists(path))
        {
            _sheetCache[sheet.File] = CreateEmptySheet();
            return _sheetCache[sheet.File];
        }

        var bytes = LzmaImageCodec.ReadImageBytes(path);
        var image = Image.Load<Bgra32>(bytes);
        if (image.Width != sheet.Layout.SheetWidth || image.Height != sheet.Layout.SheetHeight)
        {
            image.Mutate(ctx => ctx.Resize(sheet.Layout.SheetWidth, sheet.Layout.SheetHeight));
        }

        _sheetCache[sheet.File] = image;
        return image;
    }

    private string GetSheetPath(AssetSpriteSheet sheet)
    {
        EnsureAssetsFolder();
        return Path.Combine(_assetsFolder!, sheet.File);
    }

    private string CreateUniqueSheetFileName(uint firstSpriteId, int spriteType)
    {
        for (var i = 0; i < 1000; i++)
        {
            var suffix = i == 0 ? "" : $"-{i}";
            var name = $"sprites-{firstSpriteId}-{spriteType}{suffix}.bmp.lzma";
            if (!_catalog.Any(entry => string.Equals(entry.File, name, StringComparison.OrdinalIgnoreCase)) &&
                !File.Exists(Path.Combine(_assetsFolder!, name)))
            {
                return name;
            }
        }

        throw new InvalidOperationException("Nie mozna utworzyc unikalnej nazwy arkusza sprite.");
    }

    private static Image<Bgra32> CreateEmptySheet() =>
        new(384, 384, new Bgra32(255, 0, 255, 255));

    private static Image<Bgra32> ExtractImportTile(Image<Bgra32> input, AssetSpriteSheet sheet, int tileX, int tileY)
    {
        var source = new Rectangle(
            tileX * sheet.TileWidth,
            tileY * sheet.TileHeight,
            Math.Min(sheet.TileWidth, input.Width - tileX * sheet.TileWidth),
            Math.Min(sheet.TileHeight, input.Height - tileY * sheet.TileHeight));

        var tile = input.Clone(ctx => ctx.Crop(source));
        if (tile.Width != sheet.TileWidth || tile.Height != sheet.TileHeight)
        {
            tile.Mutate(ctx => ctx.Resize(sheet.TileWidth, sheet.TileHeight));
        }

        return tile;
    }

    private static Point GetTilePoint(AssetSpriteSheet sheet, int tileIndex)
    {
        var x = tileIndex % sheet.Columns * sheet.TileWidth;
        var y = tileIndex / sheet.Columns * sheet.TileHeight;
        return new Point(x, y);
    }

    private static void ValidateTileIndex(AssetSpriteSheet sheet, int tileIndex)
    {
        if (tileIndex < 0 || tileIndex >= sheet.TileCount)
        {
            throw new ArgumentOutOfRangeException(nameof(tileIndex), tileIndex, "Indeks kafla jest poza arkuszem.");
        }
    }

    private void EnsureAssetsFolder()
    {
        if (string.IsNullOrWhiteSpace(_assetsFolder))
        {
            throw new InvalidOperationException("AssetSpriteStore wymaga folderu assets dla operacji zapisu.");
        }

        Directory.CreateDirectory(_assetsFolder);
    }

    private static bool Contains(CatalogEntry entry, uint spriteId)
    {
        if (entry.FirstSpriteid < 0 || entry.LastSpriteid < 0)
        {
            return false;
        }

        return (uint)entry.FirstSpriteid <= spriteId && (uint)entry.LastSpriteid >= spriteId;
    }

    private static void SaveCompressedBmp(Image<Bgra32> source, string outputPath)
    {
        using var bmpImage = source.Clone();
        MakeTransparentMagenta(bmpImage);

        // 32bpp z kanałem alfa — zgodnie z oryginałem WPF (LZMA.cs:ExportLzmaFile używa Format32bppArgb).
        // Zachowuje stopniowe wartości alfa potrzebne dla ProcessTransparentSheets.
        using var raw = new MemoryStream();
        bmpImage.Save(raw, new BmpEncoder { BitsPerPixel = BmpBitsPerPixel.Pixel32 });
        var rawBytes = raw.ToArray();

        LzmaImageCodec.WriteCipSoftImage(outputPath, rawBytes);
    }

    private static void MakeTransparentMagenta(Image<Bgra32> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0)
                    {
                        row[x] = new Bgra32(255, 0, 255, 255);
                    }
                }
            }
        });
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
}
