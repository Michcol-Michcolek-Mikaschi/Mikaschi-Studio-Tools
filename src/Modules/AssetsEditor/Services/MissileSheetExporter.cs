using Narzedzia.Core.Tibia12;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.Services;

/// <summary>
/// Faza 7b — eksport pocisku (Missile) jako PNG w układzie 3×3 z pominiętym centrum.
/// Pociski Tibii mają 8 wzorców kierunkowych (NW, N, NE, W, E, SW, S, SE),
/// kodowanych w SpriteInfo poprzez patternX (0..2) × patternY (0..2). Komórka (1,1) — pusta.
/// </summary>
public sealed class MissileSheetExporter
{
    private static readonly (int X, int Y)[] DirectionalCells =
    [
        (0, 0), (1, 0), (2, 0),  // NW, N, NE
        (0, 1),         (2, 1),  // W,       E
        (0, 2), (1, 2), (2, 2),  // SW, S, SE
    ];

    private readonly IAssetsService _assets;

    public MissileSheetExporter(IAssetsService assets)
    {
        _assets = assets;
    }

    public ExportReport Export(Appearance missile, string outputPath, int groupIndex = 0, int frame = 0)
    {
        ArgumentNullException.ThrowIfNull(missile);
        ArgumentNullException.ThrowIfNull(outputPath);

        var fg = AppearanceTextureLayout.GetFrameGroup(missile, groupIndex)
            ?? throw new InvalidOperationException("Pocisk nie ma FrameGroup.");
        var spriteInfo = fg.SpriteInfo
            ?? throw new InvalidOperationException("FrameGroup pocisku nie ma SpriteInfo.");

        // Wyznacz rozmiar pojedynczej komórki — bierzemy render pierwszej dostępnej.
        var (cellW, cellH) = ResolveCellSize(missile, groupIndex, frame);

        var sheetW = cellW * 3;
        var sheetH = cellH * 3;
        using var output = new Image<Bgra32>(sheetW, sheetH);

        var rendered = 0;
        foreach (var (gridX, gridY) in DirectionalCells)
        {
            var opt = new AppearanceRenderOptions(
                GroupIndex: groupIndex,
                Direction: gridX,
                Addon: gridY,
                PatternZ: 0,
                Frame: frame,
                BlendLayers: false,
                FullAddons: false);

            var img = _assets.RenderAppearance(missile, opt);
            if (img is null) continue;

            using var frameImg = Image.LoadPixelData<Bgra32>(img.Pixels, img.Width, img.Height);
            var dstX = gridX * cellW + Math.Max(0, (cellW - img.Width) / 2);
            var dstY = gridY * cellH + Math.Max(0, (cellH - img.Height) / 2);
            CopyOpaque(frameImg, output, dstX, dstY);
            rendered++;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        output.SaveAsPng(outputPath);

        return new ExportReport(DirectionalsRendered: rendered, Width: sheetW, Height: sheetH, Path: outputPath);
    }

    private (int Width, int Height) ResolveCellSize(Appearance missile, int groupIndex, int frame)
    {
        // Pierwsza niepusta klatka → rozmiar komórki.
        foreach (var (gridX, gridY) in DirectionalCells)
        {
            var opt = new AppearanceRenderOptions(groupIndex, gridX, gridY, 0, frame, false, false);
            var img = _assets.RenderAppearance(missile, opt);
            if (img is not null)
            {
                return (img.Width, img.Height);
            }
        }
        return (32, 32);
    }

    private static void CopyOpaque(Image<Bgra32> source, Image<Bgra32> destination, int dstX, int dstY)
    {
        source.ProcessPixelRows(destination, (srcAccessor, dstAccessor) =>
        {
            for (var sy = 0; sy < srcAccessor.Height; sy++)
            {
                var dy = dstY + sy;
                if (dy < 0 || dy >= dstAccessor.Height) continue;
                var srcRow = srcAccessor.GetRowSpan(sy);
                var dstRow = dstAccessor.GetRowSpan(dy);
                for (var sx = 0; sx < srcRow.Length; sx++)
                {
                    var dx = dstX + sx;
                    if (dx < 0 || dx >= dstRow.Length) continue;
                    if (srcRow[sx].A == 0) continue;
                    dstRow[dx] = srcRow[sx];
                }
            }
        });
    }

    public sealed record ExportReport(int DirectionalsRendered, int Width, int Height, string Path);
}
