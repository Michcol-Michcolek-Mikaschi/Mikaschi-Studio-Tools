using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Modules.ObjectBuilder.Services;
using Modules.ObjectBuilder.ViewModels;

namespace Modules.ObjectBuilder.Views;

public partial class ObjectBuilderView : UserControl
{
    private static readonly DataFormat<string> SpriteDragFormat =
        DataFormat.CreateInProcessFormat<string>("NarzedziaObjectBuilderSpriteId");

    public ObjectBuilderView()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        SpriteLibraryDropSurface.AddHandler(DragDrop.DragOverEvent, SpriteFiles_DragOver);
        SpriteLibraryDropSurface.AddHandler(DragDrop.DropEvent, SpriteFiles_Drop);
    }

    private async void AvailableSpriteItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LegacySpriteListItem sprite } ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (DataContext is ObjectBuilderViewModel vm)
        {
            vm.SelectedAvailableSprite = sprite;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(SpriteDragFormat, sprite.Id.ToString(CultureInfo.InvariantCulture)));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
    }

    private void SpriteGridCell_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: LegacySpriteSlotItem slot } &&
            DataContext is ObjectBuilderViewModel vm)
        {
            vm.SelectedSpriteSlot = slot;
            e.Handled = true;
        }
    }

    private void ThingList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && DataContext is ObjectBuilderViewModel vm)
        {
            vm.SetSelectedThings(listBox.SelectedItems?.OfType<LegacyThingListItem>() ?? []);
        }
    }

    private async void DeleteObjects_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ObjectBuilderViewModel vm || !vm.HasSelectedThings) return;
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            vm.StatusText = "Nie udało się otworzyć potwierdzenia usuwania.";
            return;
        }

        var dialog = new LegacyDeleteConfirmationWindow(
            vm.SelectedThingCount,
            vm.DeletionWillRenumberIds);
        if (await dialog.ShowDialog<bool>(owner))
        {
            vm.DeleteSelectedThings();
        }
    }

    private async void ExportObjects_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ObjectBuilderViewModel vm) return;
        if (!vm.HasSelectedThings)
        {
            vm.StatusText = "Zaznacz co najmniej jeden obiekt do eksportu.";
            return;
        }

        try
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                vm.StatusText = "Nie udało się ustalić głównego okna aplikacji.";
                return;
            }

            var dialog = new LegacyExportWindow(vm.SuggestedExportName, vm.SelectedThingCount);
            var options = await dialog.ShowDialog<LegacyExportOptions?>(owner);
            if (options is not null) vm.ExportSelectedObjects(options);
        }
        catch (Exception ex)
        {
            vm.StatusText = $"Nie udało się otworzyć okna eksportu: {ex.Message}";
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!TryReadSpriteId(e, out _) ||
            FindDataContext<LegacySpriteSlotItem>(e.Source as Control) is not { } slot)
        {
            return;
        }

        if (DataContext is ObjectBuilderViewModel vm)
        {
            vm.SelectedSpriteSlot = slot;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ObjectBuilderViewModel vm ||
            !TryReadSpriteId(e, out var spriteId) ||
            FindDataContext<LegacySpriteSlotItem>(e.Source as Control) is not { } slot)
        {
            return;
        }

        vm.AssignSpriteToSlot(slot, spriteId);
        e.Handled = true;
    }

    private void SpriteFiles_DragOver(object? sender, DragEventArgs e)
    {
        var paths = TryReadDroppedFilePaths(e);
        if (!paths.Any(LegacySpriteImageImportService.IsSupportedFile)) return;

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void SpriteFiles_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ObjectBuilderViewModel vm) return;
        var paths = TryReadDroppedFilePaths(e);
        if (!paths.Any(LegacySpriteImageImportService.IsSupportedFile)) return;

        e.Handled = true;
        vm.SpritePanelTab = 1;
        await vm.ImportSpriteFilesFromPathsAsync(paths);
    }

    private static bool TryReadSpriteId(DragEventArgs e, out uint spriteId)
    {
        spriteId = 0;
        var value = e.DataTransfer?.TryGetValue(SpriteDragFormat);
        return uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out spriteId);
    }

    private static string[] TryReadDroppedFilePaths(DragEventArgs e)
    {
        if (e.DataTransfer is null) return [];
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
