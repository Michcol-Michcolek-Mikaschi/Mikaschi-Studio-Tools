using FluentAssertions;
using Modules.MapEditor.Services;
using Modules.MapEditor.ViewModels;
using System.Reflection;

namespace Modules.MapEditor.Tests;

public sealed class CreatureImportSourceServiceTests
{
    [Fact]
    public void Load_ExplicitDirectory_LoadsStandaloneMonstersAndNpcs()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "dragon.xml"),
                "<monster name=\"Dragon\"><look type=\"34\"/></monster>");
            File.WriteAllText(Path.Combine(directory.FullName, "Captain.xml"),
                "<npc><look type=\"128\"/></npc>");

            var result = new CreatureImportSourceService().Load([
                new CreatureImportSourcePreference(
                    directory.FullName,
                    CreatureImportSourceKind.Directory)
            ]);

            result.Creatures.Keys.Should().BeEquivalentTo("Dragon", "Captain");
            result.Creatures["Captain"].IsNpc.Should().BeTrue();
            result.Sources.Should().ContainSingle();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Load_Directory_NeverSearchesParentDirectory()
    {
        var parent = Directory.CreateTempSubdirectory();
        var selected = Directory.CreateDirectory(Path.Combine(parent.FullName, "selected"));
        try
        {
            File.WriteAllText(Path.Combine(parent.FullName, "outside.xml"),
                "<monster name=\"Outside\"><look type=\"99\"/></monster>");
            File.WriteAllText(Path.Combine(selected.FullName, "inside.xml"),
                "<npc><look type=\"128\"/></npc>");

            var result = new CreatureImportSourceService().Load([
                new CreatureImportSourcePreference(selected.FullName, CreatureImportSourceKind.Directory)
            ]);

            result.Creatures.Should().ContainKey("inside");
            result.Creatures.Should().NotContainKey("Outside");
        }
        finally
        {
            parent.Delete(true);
        }
    }

    [Fact]
    public void Load_MonstersIndex_CannotEscapeSelectedDirectory()
    {
        var parent = Directory.CreateTempSubdirectory();
        var selected = Directory.CreateDirectory(Path.Combine(parent.FullName, "selected"));
        try
        {
            File.WriteAllText(Path.Combine(parent.FullName, "outside.xml"),
                "<monster name=\"Outside\"><look type=\"99\"/></monster>");
            File.WriteAllText(Path.Combine(selected.FullName, "monsters.xml"),
                "<monsters><monster name=\"Outside\" file=\"../outside.xml\"/></monsters>");

            var result = new CreatureImportSourceService().Load([
                new CreatureImportSourcePreference(selected.FullName, CreatureImportSourceKind.Directory)
            ]);

            result.Creatures.Should().BeEmpty();
            result.Sources.Single().Warnings.Should().ContainSingle(message =>
                message.Contains("poza wybranym źródłem", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            parent.Delete(true);
        }
    }

    [Fact]
    public void Load_LaterSourceOverridesEarlierDefinition()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var first = Path.Combine(directory.FullName, "first.xml");
            var second = Path.Combine(directory.FullName, "second.xml");
            File.WriteAllText(first, "<monster name=\"Dragon\"><look type=\"34\"/></monster>");
            File.WriteAllText(second, "<monster name=\"Dragon\"><look type=\"39\"/></monster>");

            var result = new CreatureImportSourceService().Load([
                new CreatureImportSourcePreference(first, CreatureImportSourceKind.XmlFile),
                new CreatureImportSourcePreference(second, CreatureImportSourceKind.XmlFile)
            ]);

            result.Creatures["Dragon"].LookType.Should().Be(39);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Load_CancelledToken_StopsBeforeDirectoryScan()
    {
        var directory = Directory.CreateTempSubdirectory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => new CreatureImportSourceService().Load([
            new CreatureImportSourcePreference(directory.FullName, CreatureImportSourceKind.Directory)
        ], cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
        directory.Delete(true);
    }

    [Fact]
    public void PreferencesStore_RoundTripsCreatureSourcesAndReadsLegacyJson()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var file = Path.Combine(directory.FullName, "map-editor.json");
            var sourcePath = Path.Combine(directory.FullName, "monsters");
            var store = new MapEditorPreferencesStore(file);
            var preferences = MapEditorPreferences.Default with
            {
                CreatureSources =
                [
                    new CreatureImportSourcePreference(sourcePath, CreatureImportSourceKind.Directory, false)
                ],
                AutoLoadCreatureSources = false
            };

            store.TrySave(preferences).Should().BeTrue();
            var restored = store.Load();
            restored.AutoLoadCreatureSources.Should().BeFalse();
            restored.CreatureSources.Should().ContainSingle().Which.Should().Be(
                new CreatureImportSourcePreference(
                    Path.GetFullPath(sourcePath), CreatureImportSourceKind.Directory, false));

            File.WriteAllText(file, """
                {
                  "PaletteSection": "Terrain Palette",
                  "PaletteGroups": {},
                  "BrushSize": 0,
                  "BrushShape": "Kwadrat",
                  "Automagic": true
                }
                """);
            var legacy = store.Load();
            legacy.AutoLoadCreatureSources.Should().BeTrue();
            legacy.CreatureSources.Should().BeEmpty();
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task ViewModel_ManualCreatureCacheSurvivesPaletteRebuildAndCanBeRemoved()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var creaturePath = Path.Combine(directory.FullName, "dragon.xml");
            File.WriteAllText(creaturePath,
                "<monster name=\"Persistent Dragon\"><look type=\"34\"/></monster>");
            using var viewModel = new MapEditorViewModel();

            await viewModel.ImportCreatureFilesAsync([creaturePath]);
            viewModel.CreaturePalette.Should().Contain(item => item.Name == "Persistent Dragon");
            viewModel.CapturePreferences().CreatureSources.Should().ContainSingle();

            typeof(MapEditorViewModel)
                .GetMethod("BuildPalette", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(viewModel, null);
            var definitions = (IReadOnlyDictionary<string, RmeCreatureDefinition>)typeof(MapEditorViewModel)
                .GetField("_creatureDefinitions", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(viewModel)!;
            definitions.Should().ContainKey("Persistent Dragon");

            viewModel.SelectedCreatureSource = viewModel.CreatureSources.Single();
            await viewModel.RemoveSelectedCreatureSourceAsync();
            viewModel.CapturePreferences().CreatureSources.Should().BeEmpty();
            viewModel.CreaturePalette.Should().NotContain(item => item.Name == "Persistent Dragon");
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
