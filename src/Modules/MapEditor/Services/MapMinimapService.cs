using System.Collections.Immutable;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.MapEditor.Services;

/// <summary>
/// Buduje rastrowy podgląd minimapy bez zależności od Avalonia. Wejście jest
/// niezmienną migawką jednego piętra, dlatego praca może bezpiecznie odbywać się
/// poza wątkiem interfejsu, równolegle z dalszym działaniem edytora.
/// </summary>
internal sealed class MapMinimapService
{
    internal const int DefaultMaximumDimension = 256;
    private const int CancellationCheckStride = 2048;

    private static readonly Rgba32 BackgroundColor = new(2, 6, 23, 255);
    private static readonly Rgba32 UnknownTileColor = new(51, 65, 85, 255);

    /// <summary>
    /// Uruchamia zarówno rasteryzację, jak i kodowanie PNG na workerze. Wynik
    /// zawiera wyłącznie bajty i metadane; Bitmapę Avalonia tworzy dopiero UI.
    /// </summary>
    public Task<MapMinimapResult> BuildAsync(
        MapMinimapRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Build(request, cancellationToken), cancellationToken);
    }

    private static MapMinimapResult Build(MapMinimapRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.MaximumDimension is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaximumDimension,
                "Maksymalny bok minimapy musi mieścić się w zakresie 1–4096 px.");

        var samples = request.Samples.IsDefault
            ? ImmutableArray<MapMinimapTileSample>.Empty
            : request.Samples;
        if (samples.IsEmpty)
            return MapMinimapResult.Empty(request.RequestId, request.Floor);

        var minX = ushort.MaxValue;
        var minY = ushort.MaxValue;
        var maxX = ushort.MinValue;
        var maxY = ushort.MinValue;
        for (var index = 0; index < samples.Length; index++)
        {
            if ((index & (CancellationCheckStride - 1)) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var sample = samples[index];
            minX = Math.Min(minX, sample.X);
            minY = Math.Min(minY, sample.Y);
            maxX = Math.Max(maxX, sample.X);
            maxY = Math.Max(maxY, sample.Y);
        }

        var mapWidth = maxX - minX + 1;
        var mapHeight = maxY - minY + 1;
        var scale = Math.Max(
            1,
            (int)Math.Ceiling(Math.Max(mapWidth, mapHeight) / (double)request.MaximumDimension));
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(mapWidth / (double)scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(mapHeight / (double)scale));

        using var image = new Image<Rgba32>(pixelWidth, pixelHeight, BackgroundColor);
        for (var index = 0; index < samples.Length; index++)
        {
            if ((index & (CancellationCheckStride - 1)) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var sample = samples[index];
            image[(sample.X - minX) / scale, (sample.Y - minY) / scale] =
                ResolveColor(sample.ColorIndex);
        }

        // Viewport jest lekką nakładką UI aktualizowaną przy przesuwaniu mapy.
        // Nie wypalamy go w PNG, dzięki czemu sam pan/zoom nie wymaga kopiowania
        // i ponownej rasteryzacji nawet miliona próbek całego piętra.
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        cancellationToken.ThrowIfCancellationRequested();
        return new MapMinimapResult(
            request.RequestId,
            request.Floor,
            stream.ToArray(),
            minX,
            minY,
            scale,
            pixelWidth,
            pixelHeight,
            samples.Length);
    }

    private static Rgba32 ResolveColor(ushort colorIndex) => colorIndex is > 0 and < 216
        ? new Rgba32(
            (byte)(colorIndex / 36 % 6 * 51),
            (byte)(colorIndex / 6 % 6 * 51),
            (byte)(colorIndex % 6 * 51),
            255)
        : UnknownTileColor;
}

/// <summary>Pojedynczy, gotowy do rasteryzacji punkt jednego piętra mapy.</summary>
internal readonly record struct MapMinimapTileSample(ushort X, ushort Y, ushort ColorIndex);

/// <summary>
/// Pełna migawka zlecenia. RequestId i Floor wracają w wyniku, dzięki czemu
/// ViewModel może odrzucić spóźniony rezultat po zmianie mapy lub piętra.
/// </summary>
internal sealed record MapMinimapRequest(
    long RequestId,
    byte Floor,
    ImmutableArray<MapMinimapTileSample> Samples,
    int MaximumDimension = MapMinimapService.DefaultMaximumDimension);

/// <summary>Bajty PNG i geometria potrzebna do nawigacji po minimapie.</summary>
internal sealed record MapMinimapResult(
    long RequestId,
    byte Floor,
    byte[] PngBytes,
    ushort MinX,
    ushort MinY,
    int Scale,
    int PixelWidth,
    int PixelHeight,
    int TileCount)
{
    public bool HasTiles => TileCount > 0;

    public static MapMinimapResult Empty(long requestId, byte floor) =>
        new(requestId, floor, [], 0, 0, 1, 0, 0, 0);
}
