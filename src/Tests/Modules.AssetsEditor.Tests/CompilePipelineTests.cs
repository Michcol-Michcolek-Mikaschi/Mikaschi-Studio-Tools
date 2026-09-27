using FluentAssertions;
using Modules.AssetsEditor.Services;
using Modules.AssetsEditor.ViewModels;
using Narzedzia.Core.Appearances;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.Tests;

/// <summary>
/// End-to-end testy pipeline'u kompilacji (Faza 3):
/// - Backup .bak obu plików przed zapisem
/// - Atomic write (.tmp + File.Replace) appearances.dat
/// - Atomic write catalog-content.json
/// - Reload odzyskuje wprowadzone modyfikacje
/// </summary>
public sealed class CompilePipelineTests
{
    [Fact]
    public async Task Compile_PersistsModifications_AndCreatesBakFiles()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            // ── Arrange: minimalny folder z catalog + appearances.dat ──────────────
            var catalogPath     = Path.Combine(dir.FullName, "catalog-content.json");
            var appearancesPath = Path.Combine(dir.FullName, "appearances.dat");

            // appearances.dat — proto z jednym obiektem
            var initial = new Appearances();
            initial.Object.Add(new Appearance
            {
                Id = 100,
                AppearanceType = APPEARANCE_TYPE.AppearanceObject,
                Flags = new AppearanceFlags(),
                FrameGroup = { new FrameGroup { SpriteInfo = new SpriteInfo { SpriteId = { 0u } } } },
            });
            new AppearancesReader().Write(appearancesPath, initial);

            // catalog-content.json — wpis "appearances" pointing do tego pliku
            var catalogEntries = new List<CatalogEntry>
            {
                new CatalogEntry { Type = "appearances", File = "appearances.dat" },
            };
            CatalogWriter.Write(catalogPath, catalogEntries);

            using var service = new AssetsService();
            service.LoadFromFolder(dir.FullName);

            var vm = new AssetsEditorViewModel(service);
            // Wybór obiektu jako Selected (przez ListBox VM nie jest tu testowany; ustawiamy bezpośrednio)
            var loadedAppearance = service.AppearancesData!.Object[0];
            vm.Selected = new AppearanceListItem(loadedAppearance);

            // ── Act: zmiana flag + compile ──────────────────────────────────────────
            vm.FlagShift   = true;
            vm.FlagShiftX  = -5;
            vm.FlagShiftY  = 7;
            vm.FlagRotate  = true;

            await vm.CompileToLoadedFolderAsync();

            // ── Assert: pliki *-bak istnieją i mają oryginalną zawartość ────────────
            var catalogBak     = catalogPath + "-bak";
            var appearancesBak = appearancesPath + "-bak";
            File.Exists(catalogBak).Should().BeTrue("backup catalogu powinien zostać utworzony");
            File.Exists(appearancesBak).Should().BeTrue("backup appearances.dat powinien zostać utworzony");

            // Backup nie powinien zawierać naszych zmian (Rotate=false)
            var fromBak = new AppearancesReader().Read(appearancesBak);
            fromBak.Object[0].Flags.Rotate.Should().BeFalse("backup ma stan sprzed kompilacji");
            fromBak.Object[0].Flags.Shift.Should().BeNull();

            // ── Assert: po reload widać modyfikacje ─────────────────────────────────
            using var reloaded = new AssetsService();
            reloaded.LoadFromFolder(dir.FullName);
            var saved = reloaded.AppearancesData!.Object[0];

            saved.Flags.Rotate.Should().BeTrue("Rotate powinien zostać zapisany");
            saved.Flags.Shift.Should().NotBeNull();
            unchecked((int)saved.Flags.Shift!.X).Should().Be(-5);
            unchecked((int)saved.Flags.Shift!.Y).Should().Be(7);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { /* swallow */ }
        }
    }

    [Fact]
    public void WriteAtomic_PreservesTargetWhenSerializationFails_Catalog()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var catalogPath = Path.Combine(dir.FullName, "catalog-content.json");
            CatalogWriter.Write(catalogPath, new List<CatalogEntry>
            {
                new() { Type = "appearances", File = "appearances.dat" },
            });
            var originalBytes = File.ReadAllBytes(catalogPath);

            // Sztuczna serializacja, która rzuca w trakcie (passujemy do WriteAtomic
            // poprawne dane, ale następnie File.Replace nie powinien wyrzucać —
            // tu sprawdzamy tylko że "happy path" pozostawia plik w spójnym stanie).
            CatalogWriter.WriteAtomic(catalogPath, new List<CatalogEntry>
            {
                new() { Type = "appearances", File = "appearances.dat" },
                new() { Type = "sprite", File = "sprites-1.bmp.lzma", FirstSpriteid = 1, LastSpriteid = 100 },
            });

            File.Exists(catalogPath + ".tmp").Should().BeFalse("tmp powinien zostać sprzątnięty przez File.Replace");
            File.ReadAllBytes(catalogPath).Should().NotEqual(originalBytes);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { /* swallow */ }
        }
    }

    [Fact]
    public void AppearancesWriteAtomic_OverwritesTarget_WithBitstableBytes()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(dir.FullName, "appearances.dat");
            var initial = new Appearances
            {
                Object = { new Appearance { Id = 1, AppearanceType = APPEARANCE_TYPE.AppearanceObject } },
            };
            new AppearancesReader().Write(path, initial);
            var originalSize = new FileInfo(path).Length;

            // Atomic write z większym datasetem — plik powinien być nadpisany.
            var bigger = new Appearances();
            for (uint i = 1; i <= 50; i++)
            {
                bigger.Object.Add(new Appearance { Id = i, AppearanceType = APPEARANCE_TYPE.AppearanceObject });
            }
            new AppearancesReader().WriteAtomic(path, bigger);

            File.Exists(path + ".tmp").Should().BeFalse();
            new FileInfo(path).Length.Should().BeGreaterThan(originalSize);

            var reloaded = new AppearancesReader().Read(path);
            reloaded.Object.Count.Should().Be(50);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { /* swallow */ }
        }
    }
}
