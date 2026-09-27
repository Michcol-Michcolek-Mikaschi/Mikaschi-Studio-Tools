using FluentAssertions;
using Modules.ObjectBuilder.Services;
using Modules.OldItemEditor.Services;
using Modules.OldItemEditor.ViewModels;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.OldItemEditor.Tests;

public sealed class OldItemEditorCompatibilityTests
{
    [Fact]
    public void Catalog_ReproducesOriginalItemEditorPlugins()
    {
        OldItemEditorClientCatalog.All.Should().HaveCount(54);
        OldItemEditorClientCatalog.All.First().Should().Be(
            new OldItemEditorClientProfile(7, 800, "8.00", 0x467FD7E6, 0x467F9E74));
        OldItemEditorClientCatalog.All.Last().Should().Be(
            new OldItemEditorClientProfile(57, 1098, "10.98", 0x000042A3, 0x57BBD603));
    }

    [Fact]
    public void ClientSnapshot_UsesOriginalPluginGenerationRules()
    {
        var thing = CreateThing();
        var sprites = new LegacySpriteStore();

        var pluginTwo = new OldItemClientSnapshot(thing, sprites, 986);
        var pluginThree = new OldItemClientSnapshot(thing, sprites, 1010);

        ((OldItemFlags)pluginTwo.Flags).Should().HaveFlag(OldItemFlags.Readable);
        ((OldItemFlags)pluginTwo.Flags).Should().HaveFlag(OldItemFlags.IsAnimation);
        ((OldItemFlags)pluginTwo.Flags).Should().NotHaveFlag(OldItemFlags.ForceUse);
        ((OldItemFlags)pluginTwo.Flags).Should().NotHaveFlag(OldItemFlags.FullGround);
        ((OldItemFlags)pluginThree.Flags).Should().HaveFlag(OldItemFlags.ForceUse);
        ((OldItemFlags)pluginThree.Flags).Should().HaveFlag(OldItemFlags.FullGround);
    }

    [Fact]
    public void ClientSnapshot_IgnoresTechnicalFlagsLikeOriginalItemEquals()
    {
        var snapshot = new OldItemClientSnapshot(CreateThing(), new LegacySpriteStore(), 1010);
        var item = new OtbItem { ServerId = 100 };
        snapshot.ApplyTo(item);
        item.Flags |= (uint)(OldItemFlags.AllowDistanceRead | OldItemFlags.ClientCharges | OldItemFlags.FloorChangeDown);

        snapshot.Matches(item).Should().BeTrue();
    }

    [Fact]
    public void ViewModel_CreatesEditsAndSavesClassicOtb()
    {
        var path = Path.Combine(Path.GetTempPath(), $"old-item-editor-{Guid.NewGuid():N}.otb");
        var settingsPath = Path.Combine(Path.GetTempPath(), $"old-item-editor-settings-{Guid.NewGuid():N}.json");
        try
        {
            using var viewModel = new OldItemEditorViewModel(new OldItemEditorPreferencesStore(settingsPath));
            viewModel.NewDocumentCommand.Execute(null);
            viewModel.HasDocument.Should().BeTrue();
            viewModel.Items.Should().ContainSingle();
            viewModel.SelectedItem!.ServerId.Should().Be(100);

            viewModel.SelectedTypeIndex = (int)OtbItemType.Ground;
            viewModel.SelectedGroundSpeed = 140;
            viewModel.Unpassable = true;
            viewModel.SaveFileAs(path);

            var saved = new OtbParser().Parse(path);
            saved.MinorVersion.Should().Be(57);
            saved.Items.Should().ContainSingle();
            saved.Items[0].Speed.Should().Be(140);
            ((OldItemFlags)saved.Items[0].Flags).Should().HaveFlag(OldItemFlags.Unpassable);
            viewModel.IsDirty.Should().BeFalse();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    [Fact]
    public void PreferencesStore_RoundTripsClassicClientOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"old-item-editor-settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new OldItemEditorPreferencesStore(path);
            var expected = new OldItemEditorPreferences(@"C:\Tibia\8.60", true, false, true);

            store.Save(expected);

            store.Load().Should().Be(expected);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static DatThingType CreateThing()
    {
        var thing = new DatThingType
        {
            Id = 100,
            Category = DatThingCategory.Items,
            IsGround = true,
            GroundSpeed = 140,
            ForceUse = true,
            HasLensHelp = true,
            LensHelp = 1112,
            IsFullGround = true
        };
        thing.FrameGroups.Add(new DatThingFrameGroup
        {
            Width = 1,
            Height = 1,
            Layers = 1,
            PatternX = 1,
            PatternY = 1,
            PatternZ = 1,
            Frames = 2,
            SpriteIds = [0, 0]
        });
        return thing;
    }
}
