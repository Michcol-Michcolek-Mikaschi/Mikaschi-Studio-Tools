using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Modules.SpriteSheetCutter.ViewModels;
using Narzedzia.Contracts.Localization;
using Narzedzia.Contracts.Preferences;

namespace Modules.SpriteSheetCutter.Views;

public partial class SpriteSheetCutterView : UserControl
{
    private static readonly FilePickerFileType PngFileType = new("PNG")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"]
    };
    private readonly SpriteToolsPreferencesStore _preferencesStore = new();

    public SpriteSheetCutterView()
    {
        InitializeComponent();
        DataContext = new SpriteSheetCutterViewModel();
        CutterDropZone.AddHandler(DragDrop.DragOverEvent, InputFiles_DragOver);
        CutterDropZone.AddHandler(DragDrop.DragLeaveEvent, InputFiles_DragLeave);
        CutterDropZone.AddHandler(DragDrop.DropEvent, InputFiles_Drop);
        DetachedFromVisualTree += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private async void SelectFile_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SpriteSheetCutterViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var preferences = _preferencesStore.Load();
        using var suggestedStartLocation = await TryResolveFolderAsync(
            topLevel.StorageProvider,
            preferences.CutterInputFolder);
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz arkusz sprite'ów"),
            AllowMultiple = false,
            FileTypeFilter = [PngFileType, CreateAllFilesType()],
            SuggestedStartLocation = suggestedStartLocation
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            SelectInputFile(viewModel, path);
        }
    }

    private void InputFiles_DragOver(object? sender, DragEventArgs e)
    {
        var canAccept = DataContext is SpriteSheetCutterViewModel { IsBusy: false } &&
                        ReadDroppedPngFiles(e).Length == 1;
        e.DragEffects = canAccept
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        SetDropVisualState(canAccept);
        e.Handled = true;
    }

    private void InputFiles_DragLeave(object? sender, DragEventArgs e)
    {
        ResetDropVisualState();
        e.Handled = true;
    }

    private void InputFiles_Drop(object? sender, DragEventArgs e)
    {
        ResetDropVisualState();
        if (DataContext is not SpriteSheetCutterViewModel viewModel || viewModel.IsBusy)
        {
            return;
        }

        var files = ReadDroppedPngFiles(e);
        e.Handled = true;
        if (files.Length != 1)
        {
            viewModel.ReportInvalidDrop();
            return;
        }

        SelectInputFile(viewModel, files[0]);
    }

    private void SetDropVisualState(bool canAccept)
    {
        CutterDropZone.Classes.Set("drag-valid", canAccept);
        CutterDropZone.Classes.Set("drag-invalid", !canAccept);
        CutterDropHintText.Text = LocalizationManager.Translate(canAccept
            ? "Upuść, aby wybrać arkusz PNG"
            : "Można wybrać wyłącznie jeden lokalny plik PNG");
    }

    private void ResetDropVisualState()
    {
        CutterDropZone.Classes.Set("drag-valid", false);
        CutterDropZone.Classes.Set("drag-invalid", false);
        CutterDropHintText.Text = LocalizationManager.Translate(
            "Przeciągnij tutaj jeden lokalny plik PNG albo wybierz go z dysku.");
    }

    private void SelectInputFile(SpriteSheetCutterViewModel viewModel, string path)
    {
        viewModel.SetInputFile(path);
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
        {
            _preferencesStore.TrySetCutterInputFolder(folder);
        }
    }

    private static string[] ReadDroppedPngFiles(DragEventArgs e)
    {
        if (e.DataTransfer is null)
        {
            return [];
        }

        return DataTransferExtensions.TryGetFiles(e.DataTransfer)?
            .OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .Where(path => path is not null &&
                           File.Exists(path) &&
                           string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
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

    private static FilePickerFileType CreateAllFilesType() => new(
        LocalizationManager.Translate("Wszystkie pliki"))
    {
        Patterns = ["*.*"]
    };
}
