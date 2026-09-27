using FluentAssertions;
using Modules.AssetsEditor.Services;
using Modules.AssetsEditor.ViewModels;
using Narzedzia.Core.Assets;
using NSubstitute;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.Tests;

public sealed class AssetsEditorViewModelNewSpriteImportTests
{
    [Fact]
    public void AddNewSpriteFilesFromPaths_ShouldQueueSupportedFilesAndSkipUnsupportedOrDuplicate()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var pngPath = Path.Combine(tempDir.FullName, "sprite-a.png");
            var bmpPath = Path.Combine(tempDir.FullName, "sprite-b.bmp");
            var txtPath = Path.Combine(tempDir.FullName, "not-sprite.txt");

            CreateImageFile(pngPath, 32, 32);
            CreateImageFile(bmpPath, 64, 64);
            File.WriteAllText(txtPath, "x");

            var vm = CreateViewModelForImport(Substitute.For<IAssetSpriteStore>());

            vm.AddNewSpriteFilesFromPaths([pngPath, bmpPath, txtPath, pngPath]);

            vm.PendingNewSprites.Should().HaveCount(2);
            vm.PendingNewSprites.Select(item => item.FileName)
                .Should().BeEquivalentTo(["sprite-a.png", "sprite-b.bmp"]);
            vm.NewSpritesSummary.Should().Contain("W kolejce: 2");
            vm.NewSpritesSummary.Should().Contain("Dodano teraz: 2");
            vm.NewSpritesSummary.Should().Contain("Pominięto: 2");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ImportPendingNewSprites_ShouldCreateSheetSaveCatalogAndClearQueue()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var pngPath = Path.Combine(tempDir.FullName, "sprite-import.png");
            CreateImageFile(pngPath, 32, 32);

            var store = Substitute.For<IAssetSpriteStore>();
            store.Catalog.Returns(new List<CatalogEntry>
            {
                new() { Type = "sprite", File = "sprites-old.bmp.lzma", SpriteType = 0, FirstSpriteid = 1, LastSpriteid = 99 }
            });

            var createdSheet = new AssetSpriteSheet(
                File: "sprites-100-0.bmp.lzma",
                SpriteType: 0,
                FirstSpriteId: 100,
                LastSpriteId: 243,
                Layout: SpriteSheetLayout.FromSpriteType(0));

            store.CreateSheet(0, 100).Returns(createdSheet);
            store.ImportImagesIntoSheet(createdSheet, Arg.Any<IReadOnlyList<string>>(), 0).Returns(1);

            var assets = Substitute.For<IAssetsService>();
            assets.IsLoaded.Returns(true);
            assets.Catalog.Returns(Array.Empty<CatalogEntry>());
            assets.CreateSpriteStore().Returns(store);

            var vm = new AssetsEditorViewModel(assets);
            vm.AddNewSpriteFilesFromPaths([pngPath]);

            vm.ImportPendingNewSpritesCommand.Execute(null);

            store.Received(1).CreateSheet(0, 100);
            store.Received(1).ImportImagesIntoSheet(createdSheet, Arg.Any<IReadOnlyList<string>>(), 0);
            store.Received(1).SaveSheet(createdSheet);
            store.Received(1).SaveCatalog();
            assets.Received(1).InvalidateSpriteCache();

            vm.PendingNewSprites.Should().BeEmpty();
            vm.NewSpritesSummary.Should().Contain("Zaimportowano 1 plików jako 1 sprite'ów");
            vm.StatusText.Should().Contain("Dodano nowe sprite'y na koniec katalogu");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AddNewSpriteFilesFromPaths_ShouldRejectUnsupportedSizeBeforeQueueing()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var pngPath = Path.Combine(tempDir.FullName, "sprite-unsupported.png");
            CreateImageFile(pngPath, 17, 17);

            var vm = CreateViewModelForImport(Substitute.For<IAssetSpriteStore>());
            vm.AddNewSpriteFilesFromPaths([pngPath]);

            vm.PendingNewSprites.Should().BeEmpty();
            vm.NewSpritesSummary.Should().Contain("Pominięto: 1");
            vm.NewSpritesValidationMessage.Should().Contain("17×17 px");
            vm.NewSpritesValidationMessage.Should().Contain("32, 64, 96, 128, 192 albo 384 px");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AddNewSpriteFilesFromPaths_ShouldRejectCorruptImageWithoutThrowing()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var pngPath = Path.Combine(tempDir.FullName, "corrupt.png");
            File.WriteAllText(pngPath, "to nie jest obraz");
            var vm = CreateViewModelForImport(Substitute.For<IAssetSpriteStore>());

            var action = () => vm.AddNewSpriteFilesFromPaths([pngPath]);

            action.Should().NotThrow();
            vm.PendingNewSprites.Should().BeEmpty();
            vm.NewSpritesValidationMessage.Should().Contain("nie można odczytać obrazu");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    private static AssetsEditorViewModel CreateViewModelForImport(IAssetSpriteStore store)
    {
        var assets = Substitute.For<IAssetsService>();
        assets.IsLoaded.Returns(true);
        assets.Catalog.Returns(Array.Empty<CatalogEntry>());
        assets.CreateSpriteStore().Returns(store);
        return new AssetsEditorViewModel(assets);
    }

    [Fact]
    public void AddNewSpriteFilesFromPaths_SequenceMode_OrdersByFilenameAndAddsCtrlNote()
    {
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var p1 = Path.Combine(tempDir.FullName, "frame_03.png");
            var p2 = Path.Combine(tempDir.FullName, "frame_01.png");
            var p3 = Path.Combine(tempDir.FullName, "frame_02.png");
            CreateImageFile(p1, 32, 32);
            CreateImageFile(p2, 32, 32);
            CreateImageFile(p3, 32, 32);

            var vm = CreateViewModelForImport(Substitute.For<IAssetSpriteStore>());

            // Wejście niesortowane; tryb Ctrl=true sortuje leksykograficznie po nazwie
            vm.AddNewSpriteFilesFromPaths([p1, p2, p3], sequenceMode: true);

            vm.PendingNewSprites.Select(i => i.FileName).Should().Equal(
                "frame_01.png", "frame_02.png", "frame_03.png");
            vm.NewSpritesSummary.Should().Contain("(Ctrl: sekwencja klatek)");
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    private static void CreateImageFile(string path, int width, int height)
    {
        using var image = new Image<Bgra32>(width, height, new Bgra32(10, 20, 30, 255));
        image.Save(path);
    }
}
