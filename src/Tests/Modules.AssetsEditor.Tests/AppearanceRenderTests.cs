using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.Tests;

public sealed class AppearanceRenderTests
{
    [Fact]
    public void EnumerateVisibleSprites_ForTwoByTwoObject_UsesBottomRightVisualOrder()
    {
        var info = new SpriteInfo
        {
            PatternWidth = 2,
            PatternHeight = 2,
            PatternDepth = 1,
            Layers = 1,
            PatternFrames = 1
        };
        info.SpriteId.Add([1u, 2u, 3u, 4u]);

        var appearance = new Appearance
        {
            AppearanceType = APPEARANCE_TYPE.AppearanceObject,
            FrameGroup = { new FrameGroup { SpriteInfo = info } }
        };

        var parts = AppearanceTextureLayout.EnumerateVisibleSprites(
            appearance,
            new AppearanceRenderOptions(0, 0, 0, 0, 0, true, false));

        parts.Select(p => p.SpriteId).Should().Equal(1u, 2u, 3u, 4u);
    }

    [Fact]
    public void OutfitColorizer_UsesMaskColorsToTintOutfitParts()
    {
        var outfitPixels = Bgra(60, 80, 100, 255);
        var templatePixels = Bgra(0, 255, 255, 255);
        var colors = new OutfitColorSet(
            Head: new OutfitColor(255, 255, 255),
            Body: new OutfitColor(255, 0, 0),
            Legs: new OutfitColor(0, 255, 0),
            Feet: new OutfitColor(0, 0, 255));

        var result = OutfitColorizer.ApplyTemplate(templatePixels, outfitPixels, 1, 1, colors);

        result.Should().Equal(Bgra(157, 167, 177, 255));
    }

    [Fact]
    public void RenderAppearance_ForMultiTileOutfitUsesMatchingMaskTile()
    {
        var dir = Directory.CreateTempSubdirectory();
        var catalogPath = Path.Combine(dir.FullName, "catalog-content.json");
        var catalog = new List<CatalogEntry>();
        using (var store = new AssetSpriteStore(dir.FullName, catalog, catalogPath))
        {
            var sheet = store.CreateSheet(spriteType: 0, firstSpriteId: 100);
            store.ReplaceTile(sheet, 0, Tile(32, 32, new Bgra32(10, 10, 10, 255)));
            store.ReplaceTile(sheet, 1, Tile(32, 32, new Bgra32(100, 100, 100, 255)));
            store.ReplaceTile(sheet, 2, Tile(32, 32, new Bgra32(255, 255, 0, 255)));
            store.ReplaceTile(sheet, 3, Tile(32, 32, new Bgra32(100, 100, 100, 255)));
            store.SaveSheet(sheet);
            store.SaveCatalog();
        }

        using var service = new AssetsService();
        service.LoadFromFolder(dir.FullName);
        var info = new SpriteInfo
        {
            PatternWidth = 2,
            PatternHeight = 1,
            PatternLayers = 2,
            PatternX = 1,
            PatternY = 1,
            PatternZ = 1,
            PatternFrames = 1
        };
        info.SpriteId.Add([100u, 101u, 102u, 103u]);
        var appearance = new Appearance
        {
            AppearanceType = APPEARANCE_TYPE.AppearanceOutfit,
            FrameGroup = { new FrameGroup { SpriteInfo = info } }
        };

        var rendered = service.RenderAppearance(
            appearance,
            new AppearanceRenderOptions(0, 0, 0, 0, 0, true, false, true, 0, 0, 0, 0));

        rendered.Should().NotBeNull();
        var leftTileOffset = 16 * 4;
        rendered!.Pixels[leftTileOffset + 0].Should().Be(100);
        rendered.Pixels[leftTileOffset + 1].Should().Be(100);
        rendered.Pixels[leftTileOffset + 2].Should().Be(100);
    }

    [Fact]
    public void RenderAppearance_WhenSpriteSizesDiffer_UsesLargestCellWithoutCropping()
    {
        var dir = Directory.CreateTempSubdirectory();
        var catalogPath = Path.Combine(dir.FullName, "catalog-content.json");
        var catalog = new List<CatalogEntry>();
        using (var store = new AssetSpriteStore(dir.FullName, catalog, catalogPath))
        {
            var smallSheet = store.CreateSheet(spriteType: 0, firstSpriteId: 200);
            store.ReplaceTile(smallSheet, 0, Tile(32, 32, new Bgra32(0, 0, 255, 255)));
            store.SaveSheet(smallSheet);

            var largeSheet = store.CreateSheet(spriteType: 3, firstSpriteId: 500);
            store.ReplaceTile(largeSheet, 0, Tile(64, 64, new Bgra32(0, 255, 0, 255)));
            store.SaveSheet(largeSheet);
            store.SaveCatalog();
        }

        using var service = new AssetsService();
        service.LoadFromFolder(dir.FullName);

        var info = new SpriteInfo
        {
            PatternWidth = 2,
            PatternHeight = 1,
            PatternLayers = 1,
            PatternX = 1,
            PatternY = 1,
            PatternZ = 1,
            PatternFrames = 1
        };
        info.SpriteId.Add([200u, 500u]);

        var appearance = new Appearance
        {
            AppearanceType = APPEARANCE_TYPE.AppearanceObject,
            FrameGroup = { new FrameGroup { SpriteInfo = info } }
        };

        var rendered = service.RenderAppearance(
            appearance,
            new AppearanceRenderOptions(0, 0, 0, 0, 0, true, false));

        rendered.Should().NotBeNull();
        rendered!.Width.Should().Be(128);
        rendered.Height.Should().Be(64);
    }

    private static byte[] Bgra(byte b, byte g, byte r, byte a) => [b, g, r, a];

    private static byte[] Tile(int width, int height, Bgra32 color)
    {
        using var image = new Image<Bgra32>(width, height, color);
        var pixels = new byte[width * height * 4];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }
}
