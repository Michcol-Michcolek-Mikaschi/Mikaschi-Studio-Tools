using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.Tests;

/// <summary>
/// Testy Fazy 4: ProcessTransparentSheets — mutacja alfa w arkuszach LZMA
/// dla obiektów z ustawioną Flags.Transparencylevel.Level.
/// </summary>
public sealed class TransparentSheetProcessorTests
{
    [Fact]
    public void ApplyAlpha_ReplacesNonTransparentPixels_LeavesTransparentAndMagenta()
    {
        // BGRA — 4 piksele: zwykły, magenta (R=255,G=0,B=255,A=255), w pełni przezroczysty, czarny
        var pixels = new byte[]
        {
            10, 20, 30, 200,       // zwykły kolor; A=200 → powinno się zmienić
            255, 0, 255, 255,      // magenta w surowych bajtach → pomijamy
            0, 0, 0, 0,            // transparent → pomijamy (warunek a==0)
            5, 10, 15, 255,        // zwykły; A=255 → zmieniamy
        };

        TransparentSheetProcessor.ApplyAlpha(pixels, newAlpha: 128);

        pixels[3].Should().Be(128, "zwykły pixel — alpha zastąpiona");
        pixels[7].Should().Be(255, "magenta — niezmieniona (marker CipSoft)");
        pixels[11].Should().Be(0,   "transparent — niezmieniony");
        pixels[15].Should().Be(128, "zwykły pixel — alpha zastąpiona");
    }

    [Fact]
    public void Process_AppliesAlphaToSpriteTiles_FromAppearancesWithTransparencyFlag()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            // ── Arrange: 1 catalog entry (sheet z 144 tile'ami 32x32) + 1 appearance z Transparencylevel=128 ──
            var catalogPath = Path.Combine(dir.FullName, "catalog-content.json");
            var catalog = new List<CatalogEntry>();

            uint firstSpriteId;
            using (var store = new AssetSpriteStore(dir.FullName, catalog, catalogPath))
            {
                var sheet = store.CreateSheet(spriteType: 0, firstSpriteId: 100);
                firstSpriteId = sheet.FirstSpriteId;

                // Wypełnij tile 0 nieprzezroczystym pomarańczem; tile 1 magenta-markerem
                store.ReplaceTile(sheet, 0, Tile(32, 32, new Bgra32(50, 150, 250, 255)));
                store.SaveSheet(sheet);
                store.SaveCatalog();
            }

            var appearances = new Appearances();
            appearances.Object.Add(new Appearance
            {
                Id = 1,
                AppearanceType = APPEARANCE_TYPE.AppearanceObject,
                Flags = new AppearanceFlags
                {
                    Transparencylevel = new AppearanceFlagTransparencyLevel { Level = 128 },
                },
                FrameGroup =
                {
                    new FrameGroup { SpriteInfo = new SpriteInfo { SpriteId = { firstSpriteId } } },
                },
            });

            // ── Act ──
            int processedTiles;
            using (var store = new AssetSpriteStore(dir.FullName, catalog, catalogPath))
            {
                var report = new TransparentSheetProcessor().Process(appearances, store);
                processedTiles = report.ProcessedTiles;
                report.ProcessedAppearances.Should().Be(1);
                report.ModifiedSheets.Should().Be(1);
            }

            // ── Assert: po reload sheet'a tile 0 ma alpha=128 ──
            using (var store = new AssetSpriteStore(dir.FullName, catalog, catalogPath))
            {
                var sheet = store.OpenSheet(firstSpriteId);
                var pixels = store.ReadTile(sheet, 0)!;
                // ImageSharp Bgra32 memory layout: B(0) G(1) R(2) A(3) — constructor (R=50,G=150,B=250,A=255)
                pixels[0].Should().Be(250, "B");
                pixels[1].Should().Be(150, "G");
                pixels[2].Should().Be(50,  "R");
                pixels[3].Should().Be(128, "alpha tile'a została zastąpiona przez TransparencyLevel");
            }

            processedTiles.Should().Be(1);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { /* swallow */ }
        }
    }

    [Fact]
    public void Process_NoTransparencyFlag_ReturnsZeroProcessed()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var catalogPath = Path.Combine(dir.FullName, "catalog-content.json");
            var catalog = new List<CatalogEntry>();

            using (var store = new AssetSpriteStore(dir.FullName, catalog, catalogPath))
            {
                var sheet = store.CreateSheet(spriteType: 0, firstSpriteId: 200);
                store.ReplaceTile(sheet, 0, Tile(32, 32, new Bgra32(10, 20, 30, 255)));
                store.SaveSheet(sheet);
                store.SaveCatalog();
            }

            var appearances = new Appearances();
            appearances.Object.Add(new Appearance
            {
                Id = 2,
                AppearanceType = APPEARANCE_TYPE.AppearanceObject,
                Flags = new AppearanceFlags(),
                FrameGroup = { new FrameGroup { SpriteInfo = new SpriteInfo { SpriteId = { 200u } } } },
            });

            using var sprStore = new AssetSpriteStore(dir.FullName, catalog, catalogPath);
            var report = new TransparentSheetProcessor().Process(appearances, sprStore);

            report.ProcessedAppearances.Should().Be(0);
            report.ProcessedTiles.Should().Be(0);
            report.ModifiedSheets.Should().Be(0);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { /* swallow */ }
        }
    }

    private static byte[] Tile(int w, int h, Bgra32 color)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i + 3 < px.Length; i += 4)
        {
            px[i]     = color.B;
            px[i + 1] = color.G;
            px[i + 2] = color.R;
            px[i + 3] = color.A;
        }
        return px;
    }
}
