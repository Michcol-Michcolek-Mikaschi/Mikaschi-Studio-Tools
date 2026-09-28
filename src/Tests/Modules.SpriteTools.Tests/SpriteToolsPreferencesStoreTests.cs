using FluentAssertions;
using Narzedzia.Contracts.Preferences;

namespace Modules.SpriteTools.Tests;

public sealed class SpriteToolsPreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MikaschiStudioTools.Tests",
        Guid.NewGuid().ToString("N"));

    private string PreferencesPath => Path.Combine(_directory, "sprite-tools.json");

    [Fact]
    public void Load_ReturnsEmptyPreferences_WhenFileDoesNotExist()
    {
        var store = new SpriteToolsPreferencesStore(PreferencesPath);

        store.Load().Should().Be(SpriteToolsPreferences.Empty);
    }

    [Fact]
    public void FolderUpdates_RoundTripBothValues()
    {
        var resizerFolder = Path.Combine(_directory, "resizer");
        var cutterFolder = Path.Combine(_directory, "cutter");
        var store = new SpriteToolsPreferencesStore(PreferencesPath);

        store.TrySetResizerInputFolder(resizerFolder).Should().BeTrue();
        store.TrySetCutterInputFolder(cutterFolder).Should().BeTrue();

        store.Load().Should().Be(new SpriteToolsPreferences(
            Path.GetFullPath(resizerFolder),
            Path.GetFullPath(cutterFolder)));
        File.Exists(PreferencesPath + ".tmp").Should().BeFalse();
    }

    [Fact]
    public void SeparateStoreInstances_DoNotOverwriteOtherToolsFolder()
    {
        var resizerFolder = Path.Combine(_directory, "resizer");
        var replacementResizerFolder = Path.Combine(_directory, "resizer-new");
        var cutterFolder = Path.Combine(_directory, "cutter");
        var resizerStore = new SpriteToolsPreferencesStore(PreferencesPath);
        var cutterStore = new SpriteToolsPreferencesStore(PreferencesPath);

        resizerStore.TrySetResizerInputFolder(resizerFolder).Should().BeTrue();
        cutterStore.TrySetCutterInputFolder(cutterFolder).Should().BeTrue();
        resizerStore.TrySetResizerInputFolder(replacementResizerFolder).Should().BeTrue();

        cutterStore.Load().Should().Be(new SpriteToolsPreferences(
            Path.GetFullPath(replacementResizerFolder),
            Path.GetFullPath(cutterFolder)));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"ResizerInputFolder\":\"   \",\"CutterInputFolder\":null}")]
    public void Load_ReturnsEmptyPreferences_WhenDocumentIsInvalidOrEmpty(string contents)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PreferencesPath, contents);
        var store = new SpriteToolsPreferencesStore(PreferencesPath);

        store.Load().Should().Be(SpriteToolsPreferences.Empty);
    }

    [Fact]
    public void Update_RecoversFromCorruptedDocument()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PreferencesPath, "not-json");
        var cutterFolder = Path.Combine(_directory, "cutter");
        var store = new SpriteToolsPreferencesStore(PreferencesPath);

        store.TrySetCutterInputFolder(cutterFolder).Should().BeTrue();

        store.Load().Should().Be(new SpriteToolsPreferences(
            null,
            Path.GetFullPath(cutterFolder)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FolderUpdates_RejectEmptyPath(string folderPath)
    {
        var store = new SpriteToolsPreferencesStore(PreferencesPath);

        store.TrySetResizerInputFolder(folderPath).Should().BeFalse();
        store.TrySetCutterInputFolder(folderPath).Should().BeFalse();
        File.Exists(PreferencesPath).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
