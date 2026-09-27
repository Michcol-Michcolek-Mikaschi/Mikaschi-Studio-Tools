using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Modules.OldItemEditor.Services;
using Modules.OldItemEditor.ViewModels;
using Narzedzia.Contracts.Localization;

namespace Modules.OldItemEditor.Views;

public partial class OldItemEditorView : UserControl
{
    public OldItemEditorView()
    {
        InitializeComponent();
    }

    private async void OpenOtb_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContext(out var vm, out var topLevel)) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Otwórz items.otb"),
            AllowMultiple = false,
            FileTypeFilter = [OtbFileType]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await vm.OpenFileAsync(path);
    }

    private async void SaveAsOtb_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContext(out var vm, out var topLevel)) return;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Translate("Zapisz items.otb"),
            SuggestedFileName = string.IsNullOrWhiteSpace(vm.FilePath) ? "items.otb" : Path.GetFileName(vm.FilePath),
            FileTypeChoices = [OtbFileType]
        });
        if (file?.TryGetLocalPath() is { } path) vm.SaveFileAs(path);
    }

    private async void OpenClientFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContext(out var vm, out var topLevel)) return;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz folder klienta z Tibia.dat i Tibia.spr"),
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            await vm.LoadClientFolderAsync(path);
    }

    private async void Preferences_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContext(out var vm, out var topLevel) || topLevel is not Window owner) return;
        var dialog = new OldItemEditorPreferencesWindow(vm.Preferences);
        var preferences = await dialog.ShowDialog<OldItemEditorPreferences?>(owner);
        if (preferences is null || !vm.SavePreferences(preferences)) return;
        await vm.LoadClientFolderAsync(preferences.ClientDirectory);
    }

    private async void CompareOtb_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContext(out var vm, out var topLevel)) return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz drugi plik OTB do porównania"),
            AllowMultiple = false,
            FileTypeFilter = [OtbFileType]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await vm.CompareWithFileAsync(path);
    }

    private async void UpdateVersion_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetContext(out var vm, out var topLevel)) return;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz folder klienta docelowego z Tibia.dat i Tibia.spr"),
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            await vm.UpdateToClientFolderAsync(path);
    }

    private void About_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OldItemEditorViewModel vm)
            vm.CompareResultText = "Old Item Editor w Mikaschi Studio Tools\nOdtworzenie klasycznego OTTools ItemEditor dla items.otb oraz klientów Tibia 8.00–10.98.";
    }

    private void FindItem_Click(object? sender, RoutedEventArgs e)
    {
        ItemSearchBox.Focus();
        ItemSearchBox.SelectAll();
    }

    private bool TryGetContext(out OldItemEditorViewModel viewModel, out TopLevel topLevel)
    {
        viewModel = DataContext as OldItemEditorViewModel ?? null!;
        topLevel = TopLevel.GetTopLevel(this) ?? null!;
        return viewModel is not null && topLevel is not null;
    }

    private static readonly FilePickerFileType OtbFileType = new("OpenTibia Binary")
    {
        Patterns = ["*.otb"],
        MimeTypes = ["application/octet-stream"]
    };
}
