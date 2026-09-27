using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Modules.AssetsEditor.ViewModels;
using Narzedzia.Contracts.Localization;

namespace Modules.AssetsEditor.Views;

public partial class SpriteSheetEditorWindow : Window
{
    private readonly LocalizationScope _localizationScope;

    public SpriteSheetEditorWindow()
    {
        InitializeComponent();
        _localizationScope = LocalizationScope.Attach(this);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        Closed += (_, _) =>
        {
            _localizationScope.Dispose();
            if (DataContext is SpriteSheetEditorViewModel vm)
            {
                vm.Dispose();
            }
        };
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not SpriteSheetEditorViewModel vm || e.DataTransfer is null)
        {
            return;
        }

        var paths = DataTransferExtensions.TryGetFiles(e.DataTransfer)?
            .OfType<IStorageFile>()
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();

        if (paths is { Length: > 0 })
        {
            vm.ImportImagesFromPaths(paths);
        }
    }
}
