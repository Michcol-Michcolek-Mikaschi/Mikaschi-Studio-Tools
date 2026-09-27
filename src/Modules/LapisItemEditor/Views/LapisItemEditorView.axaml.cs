using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Modules.LapisItemEditor.ViewModels;
using Narzedzia.Contracts.Localization;

namespace Modules.LapisItemEditor.Views;

public partial class LapisItemEditorView : UserControl
{
    public LapisItemEditorView()
    {
        InitializeComponent();
    }

    private async void OpenOtb_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LapisItemEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Otwórz items.otb"),
            AllowMultiple = false,
            FileTypeFilter = [OtbFileType]
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await vm.OpenFileAsync(path);
        }
    }

    private async void SaveAsOtb_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LapisItemEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Translate("Zapisz items.otb"),
            SuggestedFileName = "items.otb",
            FileTypeChoices = [OtbFileType]
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            vm.SaveFileAs(path);
        }
    }

    private async void OpenItemsXml_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LapisItemEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Otwórz items.xml (TFS/OTServ)"),
            AllowMultiple = false,
            FileTypeFilter = [ItemsXmlFileType]
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.OpenItemsXml(path);
        }
    }

    private static readonly FilePickerFileType ItemsXmlFileType = new("items.xml")
    {
        Patterns = ["*.xml"],
        MimeTypes = ["application/xml", "text/xml"]
    };

    private async void OpenAssetsFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LapisItemEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz folder assets Tibia 12+ (z catalog-content.json + appearances.dat)"),
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            vm.OpenAssetsFolder(path);
        }
    }

    private async void CompareOtb_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LapisItemEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz drugi plik OTB do porównania"),
            AllowMultiple = false,
            FileTypeFilter = [OtbFileType]
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            vm.CompareWithFile(path);
        }
    }

    private static readonly FilePickerFileType OtbFileType = new("OpenTibia Binary")
    {
        Patterns = ["*.otb"],
        MimeTypes = ["application/octet-stream"]
    };
}
