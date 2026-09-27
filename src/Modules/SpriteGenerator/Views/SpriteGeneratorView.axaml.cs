using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Modules.SpriteGenerator.ViewModels;
using Narzedzia.Contracts.Localization;

namespace Modules.SpriteGenerator.Views;

public partial class SpriteGeneratorView : UserControl
{
    public SpriteGeneratorView()
    {
        InitializeComponent();
        DataContext = new SpriteGeneratorViewModel();
    }

    private async void ExportSheet_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SpriteGeneratorViewModel vm) return;
        var bytes = vm.GetLastSheetPng();
        if (bytes is null || TopLevel.GetTopLevel(this) is not { } top) return;

        var safeName = $"sprite-sheet-{vm.SelectedCategory.Name.Split(' ')[0].ToLowerInvariant()}.png";
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Translate("Eksport sprite sheet PNG"),
            DefaultExtension = "png",
            SuggestedFileName = safeName,
            FileTypeChoices = [PngFileType],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            await System.IO.File.WriteAllBytesAsync(path, bytes);
        }
    }

    private static readonly FilePickerFileType PngFileType = new("PNG")
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"],
    };
}
