using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Assets;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.Tests;

public sealed class AssetSpriteStoreTests
{
    [Fact]
    public void FindSheetForSprite_ReturnsContainingCatalogEntry()
    {
        var entries = new[]
        {
            new CatalogEntry { Type = "sprite", File = "a.bmp.lzma", SpriteType = 0, FirstSpriteid = 1, LastSpriteid = 144 },
            new CatalogEntry { Type = "sprite", File = "b.bmp.lzma", SpriteType = 3, FirstSpriteid = 145, LastSpriteid = 180 }
        };

        var sheet = AssetSpriteStore.FindSheetForSprite(entries, 160);

        sheet!.File.Should().Be("b.bmp.lzma");
    }

    [Fact]
    public void FindSheetForSprite_IgnoresNonSpriteEntries()
    {
        var entries = new[]
        {
            new CatalogEntry { Type = "appearances", File = "appearances.dat", FirstSpriteid = 1, LastSpriteid = 999 },
            new CatalogEntry { Type = "sprite", File = "sprites.bmp.lzma", SpriteType = 0, FirstSpriteid = 10, LastSpriteid = 20 }
        };

        var sheet = AssetSpriteStore.FindSheetForSprite(entries, 5);

        sheet.Should().BeNull();
    }

    [Fact]
    public void CreateReplaceSaveAndReloadSheet_RoundTripsTilePixels()
    {
        var dir = Directory.CreateTempSubdirectory();
        var catalogPath = Path.Combine(dir.FullName, "catalog-content.json");
        var catalog = new List<CatalogEntry>();
        var store = new AssetSpriteStore(dir.FullName, catalog, catalogPath);

        var sheet = store.CreateSheet(spriteType: 0, firstSpriteId: 100);
        var tile = CreateTile(32, 32, new Bgra32(1, 2, 3, 255));

        store.ReplaceTile(sheet, tileIndex: 0, tile);
        store.SaveSheet(sheet);
        store.SaveCatalog();

        File.Exists(Path.Combine(dir.FullName, sheet.File)).Should().BeTrue();
        File.ReadAllText(catalogPath).Should().Contain("firstspriteid");

        var reloadedCatalog = CatalogReader.Read(catalogPath);
        var reloadedStore = new AssetSpriteStore(dir.FullName, reloadedCatalog, catalogPath);
        var pixels = reloadedStore.ReadTile(sheet, tileIndex: 0);

        pixels.Should().NotBeNull();
        pixels![0].Should().Be(3);
        pixels[1].Should().Be(2);
        pixels[2].Should().Be(1);
        pixels[3].Should().Be(255);
    }

    [Fact]
    public void ImportImageIntoSheet_SlicesImageByTileSize()
    {
        var dir = Directory.CreateTempSubdirectory();
        var catalog = new List<CatalogEntry>();
        var store = new AssetSpriteStore(dir.FullName, catalog, Path.Combine(dir.FullName, "catalog-content.json"));
        var sheet = store.CreateSheet(spriteType: 0, firstSpriteId: 1);
        var inputPath = Path.Combine(dir.FullName, "input.png");

        using (var image = new Image<Bgra32>(64, 32))
        {
            image[0, 0] = new Bgra32(10, 20, 30, 255);
            image[32, 0] = new Bgra32(40, 50, 60, 255);
            image.Save(inputPath);
        }

        store.ImportImageIntoSheet(sheet, inputPath, startTile: 0);

        store.ReadTile(sheet, 0)![2].Should().Be(10);
        store.ReadTile(sheet, 1)![2].Should().Be(40);
    }

    [Fact]
    public void ImportImagesIntoSheet_PutsMultipleFilesInSequence()
    {
        var dir = Directory.CreateTempSubdirectory();
        var catalog = new List<CatalogEntry>();
        var store = new AssetSpriteStore(dir.FullName, catalog, Path.Combine(dir.FullName, "catalog-content.json"));
        var sheet = store.CreateSheet(spriteType: 0, firstSpriteId: 1);
        var firstPath = Path.Combine(dir.FullName, "first.png");
        var secondPath = Path.Combine(dir.FullName, "second.png");

        using (var image = new Image<Bgra32>(32, 32, new Bgra32(10, 20, 30, 255)))
        {
            image.Save(firstPath);
        }

        using (var image = new Image<Bgra32>(32, 32, new Bgra32(40, 50, 60, 255)))
        {
            image.Save(secondPath);
        }

        var imported = store.ImportImagesIntoSheet(sheet, [firstPath, secondPath], startTile: 5);

        imported.Should().Be(2);
        store.ReadTile(sheet, 5)![2].Should().Be(10);
        store.ReadTile(sheet, 6)![2].Should().Be(40);
    }

    private static byte[] CreateTile(int width, int height, Bgra32 color)
    {
        using var image = new Image<Bgra32>(width, height, color);
        var pixels = new byte[width * height * 4];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }
}
