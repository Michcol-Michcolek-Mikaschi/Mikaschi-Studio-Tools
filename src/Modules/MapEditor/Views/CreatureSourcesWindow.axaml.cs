using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Modules.MapEditor.ViewModels;
using Narzedzia.Contracts.Localization;

namespace Modules.MapEditor.Views;

public partial class CreatureSourcesWindow : Window
{
    private static readonly FilePickerFileType XmlFileType = new("XML")
    {
        Patterns = ["*.xml"]
    };

    private readonly LocalizationScope _localizationScope;

    public CreatureSourcesWindow()
    {
        InitializeComponent();
        _localizationScope = LocalizationScope.Attach(this);
        Closed += (_, _) => _localizationScope.Dispose();
    }

    public CreatureSourcesWindow(MapEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private async void AddDirectory_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel viewModel) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz katalog potworów lub NPC"),
            AllowMultiple = true
        });
        var paths = folders.Select(folder => folder.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        if (paths.Length > 0) await viewModel.AddCreatureDirectoriesAsync(paths);
    }

    private async void AddXml_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel viewModel) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz monsters.xml, creatures.xml, potwora lub NPC"),
            AllowMultiple = true,
            FileTypeFilter = [XmlFileType]
        });
        var paths = files.Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        if (paths.Length > 0) await viewModel.ImportCreatureFilesAsync(paths);
    }

    private async void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel viewModel)
            await viewModel.RemoveSelectedCreatureSourceAsync();
    }

    private async void MoveUp_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel viewModel)
            await viewModel.MoveSelectedCreatureSourceAsync(-1);
    }

    private async void MoveDown_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel viewModel)
            await viewModel.MoveSelectedCreatureSourceAsync(1);
    }

    private async void Reload_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel viewModel)
            await viewModel.ReloadCreatureSourcesAsync();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
