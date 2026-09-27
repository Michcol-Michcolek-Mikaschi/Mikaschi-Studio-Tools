using FluentAssertions;
using Narzedzia.Contracts.Localization;

namespace Modules.AssetsEditor.Tests;

public sealed class AppLanguagePreferencesStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MikaschiStudioTools.Tests",
        Guid.NewGuid().ToString("N"));

    private string PreferencesPath => Path.Combine(_directory, "application.json");

    [Fact]
    public void Load_ReturnsPolish_WhenPreferencesDoNotExist()
    {
        var store = new AppLanguagePreferencesStore(PreferencesPath);

        store.Load().Should().Be(AppLanguage.Polish);
    }

    [Theory]
    [InlineData(AppLanguage.Polish)]
    [InlineData(AppLanguage.English)]
    [InlineData(AppLanguage.Spanish)]
    public void TrySave_AndLoad_RoundTripEverySupportedLanguage(AppLanguage language)
    {
        var store = new AppLanguagePreferencesStore(PreferencesPath);

        store.TrySave(language).Should().BeTrue();
        store.Load().Should().Be(language);
        File.Exists(PreferencesPath + ".tmp").Should().BeFalse();
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"Language\":\"German\"}")]
    public void Load_ReturnsPolish_WhenPreferencesAreInvalid(string contents)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PreferencesPath, contents);
        var store = new AppLanguagePreferencesStore(PreferencesPath);

        store.Load().Should().Be(AppLanguage.Polish);
    }

    [Fact]
    public void TrySave_RejectsUnknownEnumValue()
    {
        var store = new AppLanguagePreferencesStore(PreferencesPath);

        store.TrySave((AppLanguage)999).Should().BeFalse();
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
