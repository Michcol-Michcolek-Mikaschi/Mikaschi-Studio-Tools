using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Modules.AssetsEditor.ViewModels;

namespace Modules.AssetsEditor.Views;

public partial class AssetsEditorView : UserControl
{
    private static readonly DataFormat<string> SpriteDragFormat =
        DataFormat.CreateInProcessFormat<string>("NarzedziaAssetsSpriteId");
    private readonly Border? _previewDropSurface;
    private readonly Border? _newSpritesDropSurface;

    public AssetsEditorView()
    {
        InitializeComponent();
        _previewDropSurface = this.FindControl<Border>("PreviewDropSurface");
        _newSpritesDropSurface = this.FindControl<Border>("NewSpritesDropSurface");
        // DataContext jest ustawiany przez AssetsEditorModule.CreateMainView()
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private async void GlobalSpriteItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: SpriteBrowserItem sprite } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(SpriteDragFormat, sprite.SpriteId.ToString(CultureInfo.InvariantCulture)));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
    }

    private void ThingList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && DataContext is AssetsEditorViewModel vm)
        {
            vm.SetSelectedAppearances(listBox.SelectedItems?.OfType<AppearanceListItem>() ?? []);
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not AssetsEditorView view)
        {
            return;
        }

        if (TryReadSpriteId(e, out _) && FindDataContext<SpriteSlotItem>(e.Source as Control) is not null)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (TryReadSpriteId(e, out _) && view.IsInsideControl(e.Source as Control, view._previewDropSurface))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (TryReadDroppedFilePaths(e).Length > 0 && view.IsInsideControl(e.Source as Control, view._newSpritesDropSurface))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not AssetsEditorViewModel vm)
        {
            return;
        }

        var droppedFiles = TryReadDroppedFilePaths(e);
        if (droppedFiles.Length > 0 && IsInsideControl(e.Source as Control, _newSpritesDropSurface))
        {
            var sequenceMode = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
            vm.AddNewSpriteFilesFromPaths(droppedFiles, sequenceMode);
            e.Handled = true;
            return;
        }

        if (!TryReadSpriteId(e, out var spriteId))
        {
            return;
        }

        if (FindDataContext<SpriteSlotItem>(e.Source as Control) is { } slot)
        {
            vm.AssignSpriteToSlot(slot.Index, spriteId);
            e.Handled = true;
            return;
        }

        if (_previewDropSurface is not null && IsInsideControl(e.Source as Control, _previewDropSurface))
        {
            var point = e.GetPosition(_previewDropSurface);
            vm.AssignSpriteFromPreviewDrop(
                spriteId,
                point.X,
                point.Y,
                _previewDropSurface.Bounds.Width,
                _previewDropSurface.Bounds.Height);
            e.Handled = true;
        }
    }

    private bool IsInsideControl(Control? source, Control? target)
    {
        if (target is null)
        {
            return false;
        }

        while (source is not null)
        {
            if (ReferenceEquals(source, target))
            {
                return true;
            }

            source = source.GetVisualParent() as Control;
        }

        return false;
    }

    private static bool TryReadSpriteId(DragEventArgs e, out uint spriteId)
    {
        spriteId = 0;
        var value = e.DataTransfer?.TryGetValue(SpriteDragFormat);
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out spriteId);
    }

    private static string[] TryReadDroppedFilePaths(DragEventArgs e)
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

    private static T? FindDataContext<T>(Control? control)
        where T : class
    {
        while (control is not null)
        {
            if (control.DataContext is T value)
            {
                return value;
            }

            control = control.GetVisualParent() as Control;
        }

        return null;
    }
}
