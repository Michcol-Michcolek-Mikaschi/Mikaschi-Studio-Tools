using Avalonia.Controls;
using FluentAssertions;
using Narzedzia.Contracts.Localization;
using System.Reflection;

namespace Modules.AssetsEditor.Tests;

[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationTestCollection;

[Collection("Localization")]
public sealed class LocalizationScopeTests
{
    private static readonly MethodInfo RefreshScope =
        typeof(LocalizationScope).GetMethod(
            "ScanAndRefresh",
            BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Localization scope refresh method was not found.");

    [Fact]
    public void LanguageCycle_RestoresPolishSources_ForTextBlockAndMenuItem()
    {
        RunWithLanguageReset(() =>
        {
            LocalizationManager.SetLanguage(AppLanguage.Polish);

            var text = new TextBlock { Text = "Otwórz" };
            var menuItem = new MenuItem { Header = "Zapisz" };
            var root = new StackPanel { Children = { text, menuItem } };
            using var scope = LocalizationScope.Attach(root);
            Refresh(scope);

            SetLanguageAndRefresh(scope, AppLanguage.English);
            text.Text.Should().Be("Open");
            menuItem.Header.Should().Be("Save");

            SetLanguageAndRefresh(scope, AppLanguage.Spanish);
            text.Text.Should().Be("Abrir");
            menuItem.Header.Should().Be("Guardar");

            SetLanguageAndRefresh(scope, AppLanguage.Polish);
            text.Text.Should().Be("Otwórz");
            menuItem.Header.Should().Be("Zapisz");
        });
    }

    [Fact]
    public void DynamicSourceChanges_AreTranslatedAndSurviveFollowingLanguageChanges()
    {
        RunWithLanguageReset(() =>
        {
            LocalizationManager.SetLanguage(AppLanguage.Polish);

            var text = new TextBlock { Text = "Otwórz" };
            var menuItem = new MenuItem { Header = "Zapisz" };
            var root = new StackPanel { Children = { text, menuItem } };
            using var scope = LocalizationScope.Attach(root);
            Refresh(scope);
            SetLanguageAndRefresh(scope, AppLanguage.English);

            text.Text = "Wyczyść";
            menuItem.Header = "Usuń";
            text.Text.Should().Be("Clear");
            menuItem.Header.Should().Be("Delete");

            SetLanguageAndRefresh(scope, AppLanguage.Spanish);
            text.Text.Should().Be("Limpiar");
            menuItem.Header.Should().Be("Eliminar");

            SetLanguageAndRefresh(scope, AppLanguage.Polish);
            text.Text.Should().Be("Wyczyść");
            menuItem.Header.Should().Be("Usuń");
        });
    }

    [Fact]
    public void LogicalTreeChanges_PruneRemovedControls_AndTrackNewControls()
    {
        RunWithLanguageReset(() =>
        {
            LocalizationManager.SetLanguage(AppLanguage.Polish);

            var removedText = new TextBlock { Text = "Otwórz" };
            var root = new StackPanel { Children = { removedText } };
            using var scope = LocalizationScope.Attach(root);
            Refresh(scope);

            root.Children.Remove(removedText);
            Refresh(scope);
            SetLanguageAndRefresh(scope, AppLanguage.English);
            removedText.Text.Should().Be("Otwórz",
                "a control removed from the logical tree must no longer be localized");

            var addedText = new TextBlock { Text = "Zapisz" };
            var addedMenuItem = new MenuItem { Header = "Usuń" };
            root.Children.Add(addedText);
            root.Children.Add(addedMenuItem);
            Refresh(scope);
            addedText.Text.Should().Be("Save");
            addedMenuItem.Header.Should().Be("Delete");

            SetLanguageAndRefresh(scope, AppLanguage.Spanish);
            addedText.Text.Should().Be("Guardar");
            addedMenuItem.Header.Should().Be("Eliminar");
        });
    }

    private static void SetLanguageAndRefresh(LocalizationScope scope, AppLanguage language)
    {
        LocalizationManager.SetLanguage(language);
        Refresh(scope);
    }

    private static void RunWithLanguageReset(Action action)
    {
        try
        {
            action();
        }
        finally
        {
            LocalizationManager.SetLanguage(AppLanguage.Polish);
        }
    }

    // LocalizationScope schedules scans through the global Avalonia dispatcher.
    // Other test classes can initialize that dispatcher on another worker thread,
    // so a direct scan is the deterministic no-window equivalent for this suite.
    private static void Refresh(LocalizationScope scope) =>
        RefreshScope.Invoke(scope, null);
}
