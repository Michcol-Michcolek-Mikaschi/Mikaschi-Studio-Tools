using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Services;

/// <summary>
/// Replikuje WPF "ProcessTransparentSheets" + "ChangeSpritesAlpha" (`DatEditor.xaml.cs:3167-3206, 3219-3321`).
/// Dla każdej Appearance z ustawioną `Flags.Transparencylevel.Level` mutuje kanał alfa
/// odpowiadających tile'ów w arkuszach sprite LZMA: REPLACE (nie multiply), pomijając
/// w pełni przezroczyste piksele i marker magenta (R=255,G=0,B=255).
///
/// Decyzja portu: in-place overwrite arkusza (zamiast generowania nowej nazwy jak WPF).
/// OTClient i tak czyta sprite'y po `firstspriteid/lastspriteid`, więc nazwa pliku jest stabilna.
/// </summary>
public sealed class TransparentSheetProcessor
{
    public ProcessReport Process(Appearances appearances, IAssetSpriteStore store)
    {
        ArgumentNullException.ThrowIfNull(appearances);
        ArgumentNullException.ThrowIfNull(store);

        // 1) Zbierz wszystkie (spriteId, alpha) — last writer wins (jak WPF).
        var spriteToAlpha = new Dictionary<uint, byte>();
        foreach (var a in EnumerateAll(appearances))
        {
            var tl = a.Flags?.Transparencylevel;
            if (tl is null || !tl.HasLevel) continue;
            var alpha = (byte)Math.Clamp((int)tl.Level, 0, 255);
            foreach (var fg in a.FrameGroup)
            {
                var si = fg.SpriteInfo;
                if (si is null) continue;
                foreach (var spriteId in si.SpriteId)
                {
                    spriteToAlpha[spriteId] = alpha;
                }
            }
        }

        if (spriteToAlpha.Count == 0)
        {
            return new ProcessReport(ProcessedAppearances: 0, ProcessedTiles: 0, ModifiedSheets: 0);
        }

        // 2) Pogrupuj per sheet po sprite ID.
        var perSheet = new Dictionary<string, SheetBucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var (spriteId, alpha) in spriteToAlpha)
        {
            AssetSpriteSheet sheet;
            try
            {
                sheet = store.OpenSheet(spriteId);
            }
            catch (InvalidOperationException)
            {
                continue; // sprite ID poza znanym katalogiem
            }

            var tileIndex = (int)(spriteId - sheet.FirstSpriteId);
            if (tileIndex < 0 || tileIndex >= sheet.TileCount)
            {
                continue;
            }

            if (!perSheet.TryGetValue(sheet.File, out var bucket))
            {
                bucket = new SheetBucket(sheet);
                perSheet[sheet.File] = bucket;
            }
            bucket.Tiles.Add((tileIndex, alpha));
        }

        // 3) Wykonaj mutację per sheet — wszystkie tile'e, potem jeden SaveSheet.
        var processedTiles = 0;
        foreach (var bucket in perSheet.Values)
        {
            foreach (var (tileIndex, alpha) in bucket.Tiles)
            {
                var pixels = store.ReadTile(bucket.Sheet, tileIndex);
                if (pixels is null) continue;
                ApplyAlpha(pixels, alpha);
                store.ReplaceTile(bucket.Sheet, tileIndex, pixels);
                processedTiles++;
            }
            store.SaveSheet(bucket.Sheet);
        }

        return new ProcessReport(
            ProcessedAppearances: CountAppearancesWithTransparency(appearances),
            ProcessedTiles: processedTiles,
            ModifiedSheets: perSheet.Count);
    }

    /// <summary>
    /// Zastąp alfę w pikselach BGRA. Pomija piksele w pełni przezroczyste (a=0)
    /// oraz magenta marker (R=255,G=0,B=255) zgodnie z WPF (`ChangeAlphaInRegion:3315`).
    /// </summary>
    internal static void ApplyAlpha(byte[] bgraPixels, byte newAlpha)
    {
        for (var i = 0; i + 3 < bgraPixels.Length; i += 4)
        {
            var b = bgraPixels[i];
            var g = bgraPixels[i + 1];
            var r = bgraPixels[i + 2];
            var a = bgraPixels[i + 3];

            // Pomijamy w pełni przezroczyste (po MakeMagentaTransparent magenta już tu wpada)
            if (a == 0) continue;
            // Defensywnie: jawnie magenta (gdyby ktoś przekazał surowe piksele bez konwersji)
            if (r == 255 && g == 0 && b == 255) continue;

            bgraPixels[i + 3] = newAlpha;
        }
    }

    private static IEnumerable<Appearance> EnumerateAll(Appearances a)
    {
        foreach (var x in a.Object)  yield return x;
        foreach (var x in a.Outfit)  yield return x;
        foreach (var x in a.Effect)  yield return x;
        foreach (var x in a.Missile) yield return x;
    }

    private static int CountAppearancesWithTransparency(Appearances appearances)
    {
        var count = 0;
        foreach (var a in EnumerateAll(appearances))
        {
            if (a.Flags?.Transparencylevel?.HasLevel == true) count++;
        }
        return count;
    }

    private sealed class SheetBucket
    {
        public AssetSpriteSheet Sheet { get; }
        public List<(int TileIndex, byte Alpha)> Tiles { get; } = new();
        public SheetBucket(AssetSpriteSheet sheet) => Sheet = sheet;
    }

    public sealed record ProcessReport(int ProcessedAppearances, int ProcessedTiles, int ModifiedSheets);
}
