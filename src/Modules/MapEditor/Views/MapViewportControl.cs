using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Modules.MapEditor.Services;
using Modules.MapEditor.ViewModels;

namespace Modules.MapEditor.Views;

/// <summary>
/// Lekki renderer viewportu mapy. Cały widok jest rysowany jednym przebiegiem zamiast
/// tworzenia kilku tysięcy drzew kontrolek Avalonia. Zasady kotwiczenia, displacement
/// i elevation odpowiadają MapDrawer::BlitItem z RME.
/// </summary>
public sealed class MapViewportControl : Control
{
    private const int CachedTileSize = 8;
    private const int ChunkTiles = 16;
    private const int MaximumCachedChunks = 192;
    private const long MaximumChunkCacheBytes = 64L * 1024 * 1024;
    private const int BitmapRetirementFrames = 3;
    private const double RenderMetricsWindowMilliseconds = 500;

    public static readonly StyledProperty<IReadOnlyList<MapTileItem>?> TilesProperty =
        AvaloniaProperty.Register<MapViewportControl, IReadOnlyList<MapTileItem>?>(nameof(Tiles));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<MapViewportControl, IBrush?>(nameof(Background));

    private static readonly Dictionary<string, IBrush> BrushCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<(string Text, double Size, string Color), FormattedText> TextCache = [];
    private static readonly Typeface MarkerTypeface = new("Segoe UI", FontStyle.Normal, FontWeight.Bold);
    private static readonly IPen GridPen = new Pen(GetBrush("#1E293B"), 0.5);
    private static readonly IPen HousePen = new Pen(GetBrush("#FFB000"), 2);
    private static readonly IPen SpawnZonePen = new Pen(GetBrush("#E879F9"), 1);
    private static readonly IPen SpawnCenterPen = new Pen(GetBrush("#F0ABFC"), 2);
    private static readonly IPen ProtectionZonePen = new Pen(GetBrush("#00E5A8"), 2);
    private static readonly IPen NoPvpZonePen = new Pen(GetBrush("#FB7185"), 1);
    private static readonly IPen NoLogoutZonePen = new Pen(GetBrush("#60A5FA"), 1);
    private static readonly IPen PvpZonePen = new Pen(GetBrush("#C084FC"), 1);
    private static readonly IPen MonsterMarkerPen = new Pen(GetBrush("#FFF1F2"), 0.7);
    private static readonly IPen NpcMarkerPen = new Pen(GetBrush("#ECFEFF"), 0.7);
    private static readonly IPen BlockingPen = new Pen(GetBrush("#EF4444"), 1);
    private static readonly IPen ItemHighlightPen = new Pen(GetBrush("#FACC15"), 2);
    private static readonly IPen DoorHighlightPen = new Pen(GetBrush("#FB923C"), 2);
    private static readonly IPen CompactWallHookPen = new Pen(GetBrush("#F472B6"), 1);
    private static readonly IPen PreviewPen = new Pen(GetBrush("#4ADE80"), 2);
    private static readonly IPen BorderPreviewPen = new Pen(GetBrush("#F59E0B"), 1);
    private static readonly IPen SelectionPen = new Pen(GetBrush("#38BDF8"), 2);
    private static readonly RenderOptions PixelArtRenderOptions = new()
    {
        BitmapInterpolationMode = BitmapInterpolationMode.None
    };

    private readonly Dictionary<MapChunkKey, MapChunkCacheEntry> _chunkCache = [];
    private readonly Queue<RetiredBitmap> _retiredBitmaps = [];
    private long _chunkCacheBytes;
    private long _renderFrame;
    private bool _chunkCacheUnavailable;
    private long _renderMetricsWindowStarted;
    private int _renderMetricsFrameCount;
    private double _renderMetricsTotalMilliseconds;
    private MapViewportRenderMetricsEventArgs? _pendingRenderMetrics;
    private bool _renderMetricsPublishScheduled;

    static MapViewportControl()
    {
        AffectsRender<MapViewportControl>(TilesProperty, BackgroundProperty);
    }

    public MapViewportControl()
    {
        ClipToBounds = true;
        DetachedFromVisualTree += (_, _) => ClearChunkCache(disposeImmediately: true);
    }

    /// <summary>
    /// Próbka rzeczywistych wywołań renderera. Zdarzenie jest ograniczone do
    /// maksymalnie dwóch aktualizacji na sekundę, aby sama diagnostyka nie obciążała UI.
    /// </summary>
    public event EventHandler<MapViewportRenderMetricsEventArgs>? RenderMetricsUpdated;

    public IReadOnlyList<MapTileItem>? Tiles
    {
        get => GetValue(TilesProperty);
        set => SetValue(TilesProperty, value);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var renderStarted = Stopwatch.GetTimestamp();
        try
        {
            base.Render(context);
            _renderFrame++;
            DisposeRetiredBitmaps();
            var background = Background ?? GetBrush("#020617");
            context.DrawRectangle(background, null, Bounds);
            if (Tiles is not { Count: > 0 } tiles) return;

            // Przy maksymalnym oddaleniu pojedyncza klatka zawiera zwykle ponad 15 tys.
            // pól. Rasteryzujemy światowo wyrównane fragmenty tylko raz, a podczas panowania
            // przesuwamy gotowe bitmapy. Edycja wymienia referencje wyłącznie zmienionych pól,
            // więc automatycznie przebudowują się tylko zależne fragmenty.
            if (!_chunkCacheUnavailable && tiles[0].TileSize == CachedTileSize)
            {
                try
                {
                    if (RenderChunkCache(context, tiles)) return;
                }
                catch (Exception exception) when (exception is InvalidOperationException or
                                                   NotSupportedException or PlatformNotSupportedException or ArgumentException)
                {
                    _chunkCacheUnavailable = true;
                    ClearChunkCache(disposeImmediately: false);
                    // Czyścimy ewentualnie częściowo narysowane chunki i bezpiecznie wracamy
                    // do sprawdzonego renderera bez utraty obrazu mapy.
                    context.DrawRectangle(background, null, Bounds);
                }
            }
            else if (_chunkCache.Count > 0)
            {
                ClearChunkCache(disposeImmediately: false);
            }

            RenderTileBatch(context, tiles);
        }
        finally
        {
            RecordRenderMetrics(renderStarted);
        }
    }

    internal void RecordRenderMetrics(long renderStarted)
    {
        var completed = Stopwatch.GetTimestamp();
        var renderMilliseconds = Stopwatch.GetElapsedTime(renderStarted, completed).TotalMilliseconds;
        if (_renderMetricsWindowStarted == 0)
            _renderMetricsWindowStarted = renderStarted;
        _renderMetricsFrameCount++;
        _renderMetricsTotalMilliseconds += renderMilliseconds;

        var windowMilliseconds = Stopwatch
            .GetElapsedTime(_renderMetricsWindowStarted, completed)
            .TotalMilliseconds;
        if (windowMilliseconds < RenderMetricsWindowMilliseconds) return;

        var frameCount = _renderMetricsFrameCount;
        var framesPerSecond = windowMilliseconds <= 0
            ? 0
            : frameCount * 1000d / windowMilliseconds;
        var averageRenderMilliseconds = frameCount == 0
            ? 0
            : _renderMetricsTotalMilliseconds / frameCount;

        _renderMetricsWindowStarted = completed;
        _renderMetricsFrameCount = 0;
        _renderMetricsTotalMilliseconds = 0;
        // Render() nie może bezpośrednio zmieniać żadnego zbindowanego Visuala.
        // Publikujemy próbkę dopiero po zakończeniu bieżącego przebiegu renderera.
        // Jedna oczekująca operacja oraz nadpisywanie próbki chronią kolejkę UI
        // przed narastaniem podczas maksymalizacji albo szybkiego przesuwania mapy.
        _pendingRenderMetrics = new MapViewportRenderMetricsEventArgs(
            framesPerSecond,
            averageRenderMilliseconds,
            frameCount);
        if (_renderMetricsPublishScheduled) return;

        _renderMetricsPublishScheduled = true;
        Dispatcher.UIThread.Post(PublishRenderMetrics, DispatcherPriority.Background);
    }

    private void PublishRenderMetrics()
    {
        _renderMetricsPublishScheduled = false;
        var metrics = _pendingRenderMetrics;
        _pendingRenderMetrics = null;
        if (metrics is not null)
            RenderMetricsUpdated?.Invoke(this, metrics);
    }

    /// <summary>
    /// Rysuje partię kafelków zachowując kolejność warstw RME. Metoda jest
    /// współdzielona z lekkim podglądem przesuwanego zaznaczenia, aby podgląd
    /// nie duplikował złożonych zasad sprite displacement i elevation.
    /// </summary>
    internal static void RenderTileBatch(DrawingContext context, IReadOnlyList<MapTileItem> tiles)
    {

        // RME czysci bufor przed rysowaniem mapy. Tlo kafla nie moze byc rysowane
        // pomiedzy sprite'ami, bo zaslanialoby wystajace fragmenty obiektow 64x64+.
        DrawTileBackgrounds(context, tiles);
        foreach (var tile in tiles)
            DrawTileContent(context, tile);
        foreach (var tile in tiles)
            DrawTilePostEffects(context, tile);
    }

    private bool RenderChunkCache(DrawingContext context, IReadOnlyList<MapTileItem> tiles)
    {
        var tileSize = tiles[0].TileSize;
        if (tileSize != CachedTileSize || Bounds.Width <= 0 || Bounds.Height <= 0) return false;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return false;
        var chunkPixels = ChunkTiles * tileSize;
        var renderScaling = Math.Max(1d, topLevel.RenderScaling);
        var devicePixels = Math.Max(1, (int)Math.Ceiling(chunkPixels * renderScaling));
        var buckets = BuildChunkBuckets(tiles, tileSize, devicePixels, chunkPixels);
        if (buckets.Count == 0) return false;

        // CanvasX/Y są ekranowe, X/Y światowe. Różnica jest wspólnym przesunięciem
        // całego widoku i nie stanowi części klucza cache.
        var anchor = tiles[0];
        var worldToCanvasX = anchor.CanvasX - anchor.X * tileSize;
        var worldToCanvasY = anchor.CanvasY - anchor.Y * tileSize;
        using var renderOptions = context.PushRenderOptions(PixelArtRenderOptions);

        foreach (var (key, bucket) in buckets
                     .OrderBy(pair => pair.Key.ChunkX)
                     .ThenBy(pair => pair.Key.ChunkY))
        {
            var destination = new Rect(
                key.ChunkX * chunkPixels + worldToCanvasX,
                key.ChunkY * chunkPixels + worldToCanvasY,
                chunkPixels,
                chunkPixels);
            var visiblePart = destination.Intersect(Bounds);
            if (visiblePart.Width <= 0 || visiblePart.Height <= 0) continue;

            var entry = GetOrCreateChunk(key, bucket, destination, chunkPixels, devicePixels);
            entry.LastUsedFrame = _renderFrame;
            using var clip = context.PushClip(visiblePart);
            context.DrawImage(
                entry.Bitmap,
                new Rect(0, 0, entry.Bitmap.PixelSize.Width, entry.Bitmap.PixelSize.Height),
                destination);
        }

        TrimChunkCache();
        return true;
    }

    private static Dictionary<MapChunkKey, MapChunkBucket> BuildChunkBuckets(
        IReadOnlyList<MapTileItem> tiles,
        int tileSize,
        int devicePixels,
        int chunkPixels)
    {
        var buckets = new Dictionary<MapChunkKey, MapChunkBucket>();
        foreach (var tile in tiles)
        {
            var localKey = new MapChunkKey(
                tile.X / ChunkTiles,
                tile.Y / ChunkTiles,
                tile.Z,
                tileSize,
                devicePixels);
            GetBucket(localKey).LocalTiles.Add(tile);

            var bounds = CalculateWorldVisualBounds(tile);
            var minimumChunkX = (int)Math.Floor(bounds.Left / chunkPixels);
            var minimumChunkY = (int)Math.Floor(bounds.Top / chunkPixels);
            var maximumChunkX = (int)Math.Floor((bounds.Right - 0.001) / chunkPixels);
            var maximumChunkY = (int)Math.Floor((bounds.Bottom - 0.001) / chunkPixels);
            for (var chunkX = minimumChunkX; chunkX <= maximumChunkX; chunkX++)
            for (var chunkY = minimumChunkY; chunkY <= maximumChunkY; chunkY++)
            {
                var key = new MapChunkKey(chunkX, chunkY, tile.Z, tileSize, devicePixels);
                GetBucket(key).Dependencies.Add(tile);
            }
        }

        return buckets;

        MapChunkBucket GetBucket(MapChunkKey key)
        {
            if (buckets.TryGetValue(key, out var bucket)) return bucket;
            bucket = new MapChunkBucket();
            buckets.Add(key, bucket);
            return bucket;
        }
    }

    private MapChunkCacheEntry GetOrCreateChunk(
        MapChunkKey key,
        MapChunkBucket bucket,
        Rect destination,
        int logicalPixels,
        int devicePixels)
    {
        if (_chunkCache.TryGetValue(key, out var cached) &&
            HasSameDependencies(cached.Dependencies, bucket.Dependencies))
            return cached;

        if (cached is not null)
        {
            _chunkCache.Remove(key);
            _chunkCacheBytes -= cached.ByteSize;
            RetireBitmap(cached.Bitmap);
        }

        var effectiveDpi = 96d * devicePixels / logicalPixels;
        var bitmap = new RenderTargetBitmap(
            new PixelSize(devicePixels, devicePixels),
            new Vector(effectiveDpi, effectiveDpi));
        try
        {
            using var drawing = bitmap.CreateDrawingContext();
            using var renderOptions = drawing.PushRenderOptions(PixelArtRenderOptions);
            using var clip = drawing.PushClip(new Rect(0, 0, logicalPixels, logicalPixels));
            using var transform = drawing.PushTransform(Matrix.CreateTranslation(-destination.X, -destination.Y));
            DrawTileBackgrounds(drawing, bucket.LocalTiles);
            foreach (var tile in bucket.Dependencies)
                DrawTileContent(drawing, tile);
            foreach (var tile in bucket.Dependencies)
                DrawTilePostEffects(drawing, tile);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        var stamps = bucket.Dependencies
            .Select(tile => new MapTileStamp(tile, tile.OutsideClientBox))
            .ToArray();
        var entry = new MapChunkCacheEntry(
            bitmap,
            stamps,
            (long)devicePixels * devicePixels * 4,
            _renderFrame);
        _chunkCache.Add(key, entry);
        _chunkCacheBytes += entry.ByteSize;
        return entry;
    }

    private static bool HasSameDependencies(
        IReadOnlyList<MapTileStamp> cached,
        IReadOnlyList<MapTileItem> current)
    {
        if (cached.Count != current.Count) return false;
        for (var index = 0; index < cached.Count; index++)
        {
            if (!ReferenceEquals(cached[index].Tile, current[index]) ||
                cached[index].OutsideClientBox != current[index].OutsideClientBox)
                return false;
        }
        return true;
    }

    private static Rect CalculateWorldVisualBounds(MapTileItem tile)
    {
        var tileSize = tile.TileSize;
        var baseX = (double)tile.X * tileSize;
        var baseY = (double)tile.Y * tileSize;
        var bounds = new Rect(baseX, baseY, tileSize, tileSize).Inflate(2);
        if (!tile.ShowSprites) return bounds;

        IncludeLayerStack(tile.LowerFloorLayers, baseX, baseY, tileSize, ref bounds);
        var cursorX = baseX;
        var cursorY = baseY;
        IncludeBitmap(tile.Thumbnail, tile.GroundRenderMetrics, tileSize, ref cursorX, ref cursorY, ref bounds);
        IncludeLayerStack(tile.Overlays, baseX, baseY, tileSize, ref cursorX, ref cursorY, ref bounds);
        IncludeLayerStack(tile.CreatureLayers, baseX, baseY, tileSize, ref cursorX, ref cursorY, ref bounds);
        IncludeLayerStack(tile.MarkerLayers, baseX, baseY, tileSize, ref cursorX, ref cursorY, ref bounds);
        IncludeLayerStack(tile.HigherFloorLayers, baseX, baseY, tileSize, ref bounds);
        return bounds;
    }

    private static void IncludeLayerStack(
        IReadOnlyList<MapTileLayer> layers,
        double baseX,
        double baseY,
        int tileSize,
        ref Rect bounds)
    {
        var cursorX = baseX;
        var cursorY = baseY;
        IncludeLayerStack(layers, baseX, baseY, tileSize, ref cursorX, ref cursorY, ref bounds);
    }

    private static void IncludeLayerStack(
        IReadOnlyList<MapTileLayer> layers,
        double baseX,
        double baseY,
        int tileSize,
        ref double cursorX,
        ref double cursorY,
        ref Rect bounds)
    {
        foreach (var layer in layers)
        {
            if (layer.StartsNewStack)
            {
                cursorX = baseX;
                cursorY = baseY;
            }

            if (layer.TechnicalColor is not null)
                bounds = bounds.Union(new Rect(cursorX, cursorY, tileSize, tileSize));
            else
                IncludeBitmap(layer.Thumbnail, layer.RenderMetrics, tileSize,
                    ref cursorX, ref cursorY, ref bounds);
        }
    }

    private static void IncludeBitmap(
        Bitmap? bitmap,
        MapSpriteRenderMetrics metrics,
        int tileSize,
        ref double cursorX,
        ref double cursorY,
        ref Rect bounds)
    {
        if (bitmap is null) return;
        var scale = tileSize / 32d;
        var pixelSize = bitmap.PixelSize;
        var width = pixelSize.Width * scale;
        var height = pixelSize.Height * scale;
        var x = cursorX - metrics.OffsetX * scale - Math.Max(0, pixelSize.Width - 32) * scale;
        var y = cursorY - metrics.OffsetY * scale - Math.Max(0, pixelSize.Height - 32) * scale;
        bounds = bounds.Union(new Rect(x, y, width, height));
        cursorX -= metrics.Elevation * scale;
        cursorY -= metrics.Elevation * scale;
    }

    private void TrimChunkCache()
    {
        while (_chunkCache.Count > MaximumCachedChunks || _chunkCacheBytes > MaximumChunkCacheBytes)
        {
            var oldest = _chunkCache
                .Where(pair => pair.Value.LastUsedFrame < _renderFrame - 1)
                .OrderBy(pair => pair.Value.LastUsedFrame)
                .FirstOrDefault();
            if (oldest.Value is null) break;
            _chunkCache.Remove(oldest.Key);
            _chunkCacheBytes -= oldest.Value.ByteSize;
            RetireBitmap(oldest.Value.Bitmap);
        }
    }

    private void ClearChunkCache(bool disposeImmediately)
    {
        foreach (var entry in _chunkCache.Values)
        {
            if (disposeImmediately) entry.Bitmap.Dispose();
            else RetireBitmap(entry.Bitmap);
        }
        _chunkCache.Clear();
        _chunkCacheBytes = 0;

        if (!disposeImmediately) return;
        while (_retiredBitmaps.TryDequeue(out var retired))
            retired.Bitmap.Dispose();
        _chunkCacheUnavailable = false;
    }

    private void RetireBitmap(RenderTargetBitmap bitmap) =>
        _retiredBitmaps.Enqueue(new RetiredBitmap(bitmap, _renderFrame + BitmapRetirementFrames));

    private void DisposeRetiredBitmaps()
    {
        while (_retiredBitmaps.TryPeek(out var retired) && retired.DisposeAfterFrame <= _renderFrame)
        {
            _retiredBitmaps.Dequeue();
            retired.Bitmap.Dispose();
        }
    }

    private readonly record struct MapChunkKey(
        int ChunkX,
        int ChunkY,
        byte Floor,
        int TileSize,
        int DevicePixels);

    private readonly record struct MapTileStamp(MapTileItem Tile, bool OutsideClientBox);

    private readonly record struct RetiredBitmap(RenderTargetBitmap Bitmap, long DisposeAfterFrame);

    private sealed class MapChunkBucket
    {
        public List<MapTileItem> LocalTiles { get; } = [];
        public List<MapTileItem> Dependencies { get; } = [];
    }

    private sealed class MapChunkCacheEntry(
        RenderTargetBitmap bitmap,
        MapTileStamp[] dependencies,
        long byteSize,
        long lastUsedFrame)
    {
        public RenderTargetBitmap Bitmap { get; } = bitmap;
        public MapTileStamp[] Dependencies { get; } = dependencies;
        public long ByteSize { get; } = byteSize;
        public long LastUsedFrame { get; set; } = lastUsedFrame;
    }

    private static void DrawTileBackgrounds(DrawingContext context, IReadOnlyList<MapTileItem> tiles)
    {
        for (var index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            if (tile.GridThickness > 0)
            {
                context.DrawRectangle(
                    GetBrush(tile.TileBackground),
                    GridPen,
                    new Rect(tile.CanvasX, tile.CanvasY, tile.TileSize, tile.TileSize));
                continue;
            }

            // Przy dużym oddaleniu większość pól w kolumnie ma identyczne tło.
            // Jeden pionowy prostokąt zastępuje wtedy dziesiątki osobnych wywołań Skia.
            var end = index + 1;
            var endY = tile.CanvasY + tile.TileSize;
            while (end < tiles.Count)
            {
                var next = tiles[end];
                if (next.GridThickness > 0 || next.CanvasX != tile.CanvasX || next.CanvasY != endY ||
                    next.TileSize != tile.TileSize ||
                    !string.Equals(next.TileBackground, tile.TileBackground, StringComparison.Ordinal))
                    break;
                endY += next.TileSize;
                end++;
            }

            context.DrawRectangle(
                GetBrush(tile.TileBackground),
                null,
                new Rect(tile.CanvasX, tile.CanvasY, tile.TileSize, endY - tile.CanvasY));
            index = end - 1;
        }
    }

    private static void DrawTileContent(DrawingContext context, MapTileItem tile)
    {
        var tileRect = new Rect(tile.CanvasX, tile.CanvasY, tile.TileSize, tile.TileSize);
        if (tile.ShowSprites)
        {
            DrawLayerStack(context, tile.LowerFloorLayers, tile.CanvasX, tile.CanvasY, tile.TileSize, 1);
            if (tile.LowerFloorShadeOpacity > 0)
            {
                using var shade = context.PushOpacity(tile.LowerFloorShadeOpacity);
                context.DrawRectangle(GetBrush("#FF000000"), null, tileRect);
            }

            var cursorX = (double)tile.CanvasX;
            var cursorY = (double)tile.CanvasY;
            DrawBitmap(context, tile.Thumbnail, tile.GroundRenderMetrics, tile.TileSize, ref cursorX, ref cursorY);

            if (tile.IsHouseGroundOverlay)
                context.DrawRectangle(GetBrush("#55FFB000"), HousePen, tileRect);

            DrawLayerStack(context, tile.Overlays, tile.CanvasX, tile.CanvasY, tile.TileSize,
                tile.ItemOpacity, ref cursorX, ref cursorY);
            DrawLayerStack(context, tile.CreatureLayers, tile.CanvasX, tile.CanvasY, tile.TileSize,
                1, ref cursorX, ref cursorY);
            DrawLayerStack(context, tile.MarkerLayers, tile.CanvasX, tile.CanvasY, tile.TileSize,
                1, ref cursorX, ref cursorY);
        }

        if (tile.IsHouseExtendedOverlay)
            context.DrawRectangle(GetBrush("#55FFB000"), HousePen, tileRect);
        if (tile.IsSpawnZone)
            context.DrawRectangle(GetBrush("#265B21B6"), SpawnZonePen, tileRect);
        if (tile.IsProtectionZone)
        {
            // PZ jest celowo turkusowe i lekko wsunięte. Dzięki temu na polu,
            // które jednocześnie należy do domu, pozostaje widoczna bursztynowa
            // zewnętrzna ramka domu zamiast dwóch nakładających się obrysów.
            var inset = Math.Clamp(tile.TileSize * 0.08, 1, 3);
            var protectionRect = new Rect(
                tile.CanvasX + inset,
                tile.CanvasY + inset,
                Math.Max(1, tile.TileSize - inset * 2),
                Math.Max(1, tile.TileSize - inset * 2));
            context.DrawRectangle(GetBrush("#5500E5A8"), ProtectionZonePen, protectionRect);
        }
        if (tile.IsNoPvpZone)
            context.DrawRectangle(GetBrush("#3DEF4444"), NoPvpZonePen, tileRect);
        if (tile.IsNoLogoutZone)
            context.DrawRectangle(GetBrush("#3D3B82F6"), NoLogoutZonePen, tileRect);
        if (tile.IsPvpZone)
            context.DrawRectangle(GetBrush("#3DA855F7"), PvpZonePen, tileRect);
        if (tile.IsBlocking)
            context.DrawRectangle(GetBrush("#66EF4444"), BlockingPen, tileRect);
        if (tile.HighlightItem)
            context.DrawRectangle(null, ItemHighlightPen, tileRect);
        if (tile.HighlightLockedDoor)
            context.DrawRectangle(GetBrush("#55F97316"), DoorHighlightPen, tileRect);
        if (tile.OutsideClientBox)
            context.DrawRectangle(GetBrush("#88000000"), null, tileRect);
    }

    private static void DrawTilePostEffects(DrawingContext context, MapTileItem tile)
    {
        var tileRect = new Rect(tile.CanvasX, tile.CanvasY, tile.TileSize, tile.TileSize);
        if (!tile.LightOverlayColor.Equals("#00000000", StringComparison.Ordinal))
            context.DrawRectangle(GetBrush(tile.LightOverlayColor), null, tileRect);
        if (tile.FogOpacity > 0)
        {
            using var fog = context.PushOpacity(tile.FogOpacity);
            context.DrawRectangle(GetBrush("#FF0A0A0A"), null, tileRect);
        }

        if (tile.ShowSprites && tile.HigherFloorLayers.Count > 0)
            DrawLayerStack(context, tile.HigherFloorLayers, tile.CanvasX, tile.CanvasY, tile.TileSize, 0.38);

        if (tile.IsPreview)
            context.DrawRectangle(GetBrush("#334ADE80"), PreviewPen, tileRect);
        if (tile.IsBorderPreview)
            context.DrawRectangle(GetBrush("#22F59E0B"), BorderPreviewPen, tileRect);
        if (tile.IsSelected)
            // RME przyciemnia zaznaczony tile, ale pozostawia sprite czytelny.
            // Czarna półprzezroczysta warstwa jest jednoznaczna również na bardzo
            // jasnych podłożach; błękitny obrys zachowuje widoczną krawędź grupy.
            context.DrawRectangle(GetBrush("#66000000"), SelectionPen, tileRect);

        if (tile.HasSpawn || tile.NpcCount > 0 || tile.MonsterCount > 0 ||
            !string.IsNullOrEmpty(tile.HouseExitName) || !string.IsNullOrEmpty(tile.WaypointNames) ||
            !string.IsNullOrEmpty(tile.TownNames) || !string.IsNullOrEmpty(tile.LightLabel) || tile.HasWallHook)
            DrawMarkers(context, tile);
    }

    private static void DrawLayerStack(
        DrawingContext context,
        IReadOnlyList<MapTileLayer> layers,
        double baseX,
        double baseY,
        int tileSize,
        double opacity)
    {
        var cursorX = baseX;
        var cursorY = baseY;
        DrawLayerStack(context, layers, baseX, baseY, tileSize, opacity, ref cursorX, ref cursorY);
    }

    private static void DrawLayerStack(
        DrawingContext context,
        IReadOnlyList<MapTileLayer> layers,
        double baseX,
        double baseY,
        int tileSize,
        double opacity,
        ref double cursorX,
        ref double cursorY)
    {
        if (layers.Count == 0) return;
        if (opacity >= 0.999)
        {
            DrawLayerStackCore(context, layers, baseX, baseY, tileSize, ref cursorX, ref cursorY);
            return;
        }

        using var opacityScope = context.PushOpacity(opacity);
        DrawLayerStackCore(context, layers, baseX, baseY, tileSize, ref cursorX, ref cursorY);
    }

    private static void DrawLayerStackCore(
        DrawingContext context,
        IReadOnlyList<MapTileLayer> layers,
        double baseX,
        double baseY,
        int tileSize,
        ref double cursorX,
        ref double cursorY)
    {
        foreach (var layer in layers)
        {
            if (layer.StartsNewStack)
            {
                cursorX = baseX;
                cursorY = baseY;
            }

            if (layer.TechnicalColor is { } technicalColor)
                context.DrawRectangle(GetBrush(technicalColor), null, new Rect(cursorX, cursorY, tileSize, tileSize));
            else
                DrawBitmap(context, layer.Thumbnail, layer.RenderMetrics, tileSize, ref cursorX, ref cursorY);
        }
    }

    private static void DrawBitmap(
        DrawingContext context,
        Bitmap? bitmap,
        MapSpriteRenderMetrics metrics,
        int tileSize,
        ref double cursorX,
        ref double cursorY)
    {
        if (bitmap is null) return;
        var scale = tileSize / 32d;
        var pixelSize = bitmap.PixelSize;
        var width = pixelSize.Width * scale;
        var height = pixelSize.Height * scale;
        var x = cursorX - metrics.OffsetX * scale - Math.Max(0, pixelSize.Width - 32) * scale;
        var y = cursorY - metrics.OffsetY * scale - Math.Max(0, pixelSize.Height - 32) * scale;
        context.DrawImage(
            bitmap,
            new Rect(0, 0, pixelSize.Width, pixelSize.Height),
            new Rect(x, y, width, height));
        cursorX -= metrics.Elevation * scale;
        cursorY -= metrics.Elevation * scale;
    }

    private static void DrawMarkers(DrawingContext context, MapTileItem tile)
    {
        if (tile.HasSpawn)
        {
            var inset = Math.Max(1, tile.TileSize * 0.12);
            var centerX = tile.CanvasX + tile.TileSize / 2d;
            var centerY = tile.CanvasY + tile.TileSize / 2d;
            context.DrawRectangle(null, SpawnCenterPen,
                new Rect(tile.CanvasX + inset, tile.CanvasY + inset,
                    Math.Max(1, tile.TileSize - inset * 2), Math.Max(1, tile.TileSize - inset * 2)));
            context.DrawLine(SpawnCenterPen,
                new Point(centerX - tile.TileSize * 0.22, centerY),
                new Point(centerX + tile.TileSize * 0.22, centerY));
            context.DrawLine(SpawnCenterPen,
                new Point(centerX, centerY - tile.TileSize * 0.22),
                new Point(centerX, centerY + tile.TileSize * 0.22));
        }

        if (tile.TileSize < 16)
        {
            DrawCompactEntityMarkers(context, tile);
        }
        else if (tile.MarkerLayers.All(layer => layer.Thumbnail is null) &&
                 tile.CreatureLayers.All(layer => layer.Thumbnail is null) &&
                 !string.IsNullOrEmpty(tile.MarkerLabel))
        {
            var text = GetText(tile.MarkerLabel, Math.Max(7, tile.TileSize / 4d), "#22D3EE");
            var origin = new Point(tile.CanvasX + 1, tile.CanvasY);
            context.DrawRectangle(GetBrush("#CC0F172A"), null,
                new Rect(origin.X, origin.Y, text.Width + 2, text.Height));
            context.DrawText(text, origin);
        }

        if (!string.IsNullOrEmpty(tile.LightLabel) && tile.TileSize < 16)
        {
            var size = Math.Max(2, tile.TileSize / 4d);
            context.DrawRectangle(GetBrush("#FDE047"), null,
                new Rect(tile.CanvasX + tile.TileSize - size, tile.CanvasY, size, size));
        }
        else if (!string.IsNullOrEmpty(tile.LightLabel))
        {
            var text = GetText(tile.LightLabel, Math.Max(6, tile.TileSize / 4.5), "#FDE047");
            var origin = new Point(tile.CanvasX + tile.TileSize - text.Width - 1, tile.CanvasY);
            context.DrawRectangle(GetBrush("#AA0F172A"), null,
                new Rect(origin.X - 1, origin.Y, text.Width + 2, text.Height));
            context.DrawText(text, origin);
        }

        if (tile.HasWallHook && tile.TileSize < 16)
        {
            var y = tile.CanvasY + tile.TileSize / 2d;
            context.DrawLine(CompactWallHookPen,
                new Point(tile.CanvasX + 1, y),
                new Point(tile.CanvasX + tile.TileSize - 1, y));
        }
        else if (tile.HasWallHook)
        {
            var text = GetText("↔", Math.Max(8, tile.TileSize / 3d), "#F472B6");
            context.DrawText(text, new Point(
                tile.CanvasX + (tile.TileSize - text.Width) / 2,
                tile.CanvasY + (tile.TileSize - text.Height) / 2));
        }
    }

    private static void DrawCompactEntityMarkers(DrawingContext context, MapTileItem tile)
    {
        var diameter = Math.Max(3, tile.TileSize * 0.48);
        if (tile.MonsterCount > 0)
        {
            context.DrawEllipse(GetBrush("#F43F5E"), MonsterMarkerPen,
                new Point(tile.CanvasX + tile.TileSize * 0.38, tile.CanvasY + tile.TileSize * 0.55),
                diameter / 2, diameter / 2);
        }
        if (tile.NpcCount > 0)
        {
            context.DrawEllipse(GetBrush("#22D3EE"), NpcMarkerPen,
                new Point(tile.CanvasX + tile.TileSize * 0.66, tile.CanvasY + tile.TileSize * 0.35),
                diameter / 2, diameter / 2);
        }

        var markerSize = Math.Max(2, tile.TileSize / 4d);
        var markerX = (double)tile.CanvasX;
        if (!string.IsNullOrEmpty(tile.HouseExitName))
        {
            context.DrawRectangle(GetBrush("#F59E0B"), null,
                new Rect(markerX, tile.CanvasY, markerSize, markerSize));
            markerX += markerSize;
        }
        if (!string.IsNullOrEmpty(tile.WaypointNames))
        {
            context.DrawRectangle(GetBrush("#22D3EE"), null,
                new Rect(markerX, tile.CanvasY, markerSize, markerSize));
            markerX += markerSize;
        }
        if (!string.IsNullOrEmpty(tile.TownNames))
            context.DrawRectangle(GetBrush("#60A5FA"), null,
                new Rect(markerX, tile.CanvasY, markerSize, markerSize));
    }

    private static IBrush GetBrush(string color)
    {
        if (BrushCache.TryGetValue(color, out var brush)) return brush;
        brush = new SolidColorBrush(Color.Parse(color));
        BrushCache[color] = brush;
        return brush;
    }

    private static FormattedText GetText(string value, double size, string color)
    {
        var key = (value, Math.Round(size, 2), color);
        if (TextCache.TryGetValue(key, out var text)) return text;
        text = new FormattedText(
            value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            MarkerTypeface,
            key.Item2,
            GetBrush(color));
        TextCache[key] = text;
        return text;
    }
}

public sealed class MapViewportRenderMetricsEventArgs(
    double framesPerSecond,
    double averageRenderMilliseconds,
    int sampledFrames) : EventArgs
{
    public double FramesPerSecond { get; } = framesPerSecond;
    public double AverageRenderMilliseconds { get; } = averageRenderMilliseconds;
    public int SampledFrames { get; } = sampledFrames;
}

/// <summary>
/// Osobna, bardzo lekka warstwa podglądu pędzla. Ruch kursora odświeża wyłącznie
/// kilka prostokątów tej warstwy, bez ponownego budowania i rysowania całej mapy.
/// </summary>
public sealed class MapBrushPreviewControl : Control
{
    public static readonly StyledProperty<IReadOnlyList<MapBrushPreviewTile>?> TilesProperty =
        AvaloniaProperty.Register<MapBrushPreviewControl, IReadOnlyList<MapBrushPreviewTile>?>(nameof(Tiles));

    private static readonly IBrush PreviewBrush = new SolidColorBrush(Color.Parse("#334ADE80"));
    private static readonly IBrush BorderPreviewBrush = new SolidColorBrush(Color.Parse("#22F59E0B"));
    private static readonly IPen PreviewPen = new Pen(new SolidColorBrush(Color.Parse("#4ADE80")), 2);
    private static readonly IPen BorderPreviewPen = new Pen(new SolidColorBrush(Color.Parse("#F59E0B")), 1);

    static MapBrushPreviewControl()
    {
        AffectsRender<MapBrushPreviewControl>(TilesProperty);
    }

    public MapBrushPreviewControl()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    public IReadOnlyList<MapBrushPreviewTile>? Tiles
    {
        get => GetValue(TilesProperty);
        set => SetValue(TilesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Tiles is not { Count: > 0 } tiles) return;
        foreach (var tile in tiles)
        {
            var rectangle = new Rect(tile.CanvasX, tile.CanvasY, tile.TileSize, tile.TileSize);
            context.DrawRectangle(
                tile.IsBorder ? BorderPreviewBrush : PreviewBrush,
                tile.IsBorder ? BorderPreviewPen : PreviewPen,
                rectangle);
        }
    }
}
