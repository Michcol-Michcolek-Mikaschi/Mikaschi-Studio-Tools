using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Modules.SpriteResizer.ViewModels;
using Narzedzia.Contracts.Localization;
using Narzedzia.Contracts.Preferences;

namespace Modules.SpriteResizer.Views;

public partial class SpriteResizerView : UserControl
{
    private static readonly FilePickerFileType PngFileType = new("PNG")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"]
    };
    private readonly SpriteToolsPreferencesStore _preferencesStore = new();

    public SpriteResizerView()
    {
        InitializeComponent();
        DataContext = new SpriteResizerViewModel();
        DropSurface.AddHandler(DragDrop.DragOverEvent, DropSurface_DragOver);
        DropSurface.AddHandler(DragDrop.DragLeaveEvent, DropSurface_DragLeave);
        DropSurface.AddHandler(DragDrop.DropEvent, DropSurface_Drop);
    }

    private async void SelectFiles_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SpriteResizerViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var preferences = _preferencesStore.Load();
        using var suggestedStartLocation = await TryResolveFolderAsync(
            topLevel.StorageProvider,
            preferences.ResizerInputFolder);
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz pliki PNG do skalowania"),
            AllowMultiple = true,
            FileTypeFilter = [PngFileType],
            SuggestedStartLocation = suggestedStartLocation
        });

        if (files.Count == 0)
        {
            return;
        }

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();
        viewModel.SetInputFiles(paths);
        RememberInputFolder(paths.FirstOrDefault(SpriteResizerViewModel.IsSupportedInputFile));
    }

    private void DropSurface_DragOver(object? sender, DragEventArgs e)
    {
        var canAccept = DataContext is SpriteResizerViewModel { IsBusy: false } &&
                        ReadDroppedFilePaths(e).Any(SpriteResizerViewModel.IsSupportedInputFile);

        e.DragEffects = canAccept ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropVisualState(canAccept);
        e.Handled = true;
    }

    private void DropSurface_DragLeave(object? sender, DragEventArgs e)
    {
        ResetDropVisualState();
        e.Handled = true;
    }

    private void DropSurface_Drop(object? sender, DragEventArgs e)
    {
        ResetDropVisualState();
        if (DataContext is not SpriteResizerViewModel { IsBusy: false } viewModel)
        {
            e.Handled = true;
            return;
        }

        var paths = ReadDroppedFilePaths(e);
        viewModel.AddInputFiles(paths);
        RememberInputFolder(paths.FirstOrDefault(SpriteResizerViewModel.IsSupportedInputFile));
        e.Handled = true;
    }

    private static string[] ReadDroppedFilePaths(DragEventArgs e)
    {
        if (e.DataTransfer is null)
        {
            return [];
        }

        return DataTransferExtensions.TryGetFiles(e.DataTransfer)?
            .OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray() ?? [];
    }

    private void SetDropVisualState(bool canAccept)
    {
        DropSurface.Classes.Set("drag-valid", canAccept);
        DropSurface.Classes.Set("drag-invalid", !canAccept);
        DropHintText.Text = LocalizationManager.Translate(canAccept
            ? "Upuść, aby dodać pliki PNG"
            : "Można dodać wyłącznie lokalne pliki PNG");
    }

    private void ResetDropVisualState()
    {
        DropSurface.Classes.Set("drag-valid", false);
        DropSurface.Classes.Set("drag-invalid", false);
        DropHintText.Text = LocalizationManager.Translate(
            "Przeciągnij tutaj jeden lub wiele lokalnych plików PNG");
    }

    private void RememberInputFolder(string? path)
    {
        if (path is not null && Path.GetDirectoryName(path) is { Length: > 0 } folder)
        {
            _preferencesStore.TrySetResizerInputFolder(folder);
        }
    }

    private static async Task<IStorageFolder?> TryResolveFolderAsync(
        IStorageProvider storageProvider,
        string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return null;
        }

        try
        {
            return await storageProvider.TryGetFolderFromPathAsync(folderPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
