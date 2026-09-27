using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.ComponentModel;
using Modules.MapEditor.Services;
using Modules.MapEditor.ViewModels;
using Narzedzia.Contracts.Localization;
// MapPaletteItem jest w tym samym namespace co ViewModel

namespace Modules.MapEditor.Views;

public partial class MapEditorView : UserControl
{
    private bool _isDragging;
    private bool _isPainting;
    private bool _isErasing;
    private bool _isSelecting;
    private bool _isMovingSelection;
    private Avalonia.Point _dragStart;
    private (ushort X, ushort Y) _dragStartView;
    private (ushort X, ushort Y)? _lastStrokeTile;
    private (ushort X, ushort Y)? _pendingStrokeTile;
    private (ushort X, ushort Y)? _pendingSelectionTile;
    private (ushort X, ushort Y)? _pendingSelectionMoveTile;
    private (ushort X, ushort Y)? _pendingPanOrigin;
    private int _pendingPanDeltaX;
    private int _pendingPanDeltaY;
    private bool _viewportInputScheduled;
    private bool _viewportResizeScheduled;
    private Size _pendingViewportSize;
    private readonly DispatcherTimer _animationTimer;
    private readonly DispatcherTimer _preferenceSaveTimer;
    private readonly MapEditorPreferencesStore _preferencesStore = new();
    private MenuFlyout? _paletteGroupFlyout;

    public MapEditorView()
    {
        InitializeComponent();
        var viewModel = new MapEditorViewModel(_preferencesStore.Load());
        DataContext = viewModel;
        viewModel.PropertyChanged += OnPreferencePropertyChanged;
        SizeChanged += OnSizeChanged;
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _animationTimer.Tick += (_, _) =>
        {
            if (DataContext is MapEditorViewModel vm) vm.AdvanceAnimationFrame();
        };
        _preferenceSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _preferenceSaveTimer.Tick += (_, _) =>
        {
            _preferenceSaveTimer.Stop();
            SavePreferences();
        };
        AttachedToVisualTree += (_, _) => _animationTimer.Start();
        DetachedFromVisualTree += (_, _) =>
        {
            CancelActiveViewportInteraction();
            _animationTimer.Stop();
            _preferenceSaveTimer.Stop();
            _viewportInputScheduled = false;
            _viewportResizeScheduled = false;
            ClearQueuedViewportInput();
            SavePreferences();
        };
    }

    private void OnPreferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MapEditorViewModel.SelectedPaletteSection) or
            nameof(MapEditorViewModel.SelectedPaletteGroup) or nameof(MapEditorViewModel.BrushSize) or
            nameof(MapEditorViewModel.BrushShape) or nameof(MapEditorViewModel.Automagic))) return;
        _preferenceSaveTimer.Stop();
        _preferenceSaveTimer.Start();
    }

    private void SavePreferences()
    {
        if (DataContext is MapEditorViewModel viewModel)
            _preferencesStore.TrySave(viewModel.CapturePreferences());
    }

    private void PaletteGroupSelector_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control target || DataContext is not MapEditorViewModel viewModel) return;

        // RME utrzymuje osobne kontrolki tilesetów dla każdej palety. Tutaj osiągamy
        // ten sam efekt, tworząc świeże pozycje menu z niezmiennej migawki aktywnej
        // palety. Żaden kontener wizualny nie może przejść z Terrain do Item/House itd.
        var section = viewModel.SelectedPaletteSection;
        var groups = viewModel.PaletteGroups.ToArray();
        var flyout = new MenuFlyout
        {
            Placement = PlacementMode.BottomEdgeAlignedLeft
        };

        foreach (var group in groups)
        {
            var selectedGroup = group;
            var item = new MenuItem
            {
                Header = selectedGroup,
                MinWidth = Math.Max(220, target.Bounds.Width - 12),
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "RmePaletteTilesets",
                IsChecked = string.Equals(
                    selectedGroup,
                    viewModel.SelectedPaletteGroup,
                    StringComparison.OrdinalIgnoreCase)
            };
            item.Click += (_, _) =>
            {
                if (DataContext is not MapEditorViewModel current ||
                    !string.Equals(current.SelectedPaletteSection, section, StringComparison.Ordinal) ||
                    !current.PaletteGroups.Contains(selectedGroup, StringComparer.OrdinalIgnoreCase))
                    return;

                current.SelectedPaletteGroup = current.PaletteGroups.First(candidate =>
                    candidate.Equals(selectedGroup, StringComparison.OrdinalIgnoreCase));
            };
            flyout.Items.Add(item);
        }

        _paletteGroupFlyout?.Hide();
        _paletteGroupFlyout = flyout;
        flyout.ShowAt(target);
        e.Handled = true;
    }

    private void MapEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm) return;
        if (vm.IsLoading)
        {
            if (e.Key == Key.Escape)
                vm.CancelLoadCommand.Execute(null);
            e.Handled = true;
            return;
        }
        // Podgląd drag jest celowo snapshotem bieżącego viewportu. Nie pozwalamy
        // w jego trakcie zmienić ViewX/ViewY ani zoomu, bo wymagałoby to ponownego
        // budowania całej mapy i niwelowałoby optymalizację.
        if (_isMovingSelection)
        {
            if (e.Key == Key.Escape)
            {
                _pendingSelectionMoveTile = null;
                _isMovingSelection = false;
                vm.CancelSelectionMove();
                SelectionMovePreview.Clear();
            }
            e.Handled = true;
            return;
        }
        var arrowStep = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 10 : vm.TileSize == 32 ? 1 : 3;
        var arrowDelta = e.Key switch
        {
            Key.Left or Key.NumPad4 => (-arrowStep, 0),
            Key.Right or Key.NumPad6 => (arrowStep, 0),
            Key.Up or Key.NumPad8 => (0, -arrowStep),
            Key.Down or Key.NumPad2 => (0, arrowStep),
            _ => (0, 0)
        };
        if (arrowDelta != (0, 0))
        {
            _pendingPanDeltaX += arrowDelta.Item1;
            _pendingPanDeltaY += arrowDelta.Item2;
            ScheduleViewportInputFrame();
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.Delete && vm.DeleteSelectionCommand.CanExecute(null))
            {
                vm.DeleteSelectionCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && vm.ClearSelectionCommand.CanExecute(null))
            {
                vm.ClearSelectionCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.P && vm.NavigateBackCommand.CanExecute(null))
            {
                vm.NavigateBackCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.F5 && vm.ReloadDataCommand.CanExecute(null))
            {
                vm.ReloadDataCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.L)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) vm.ShowLights = !vm.ShowLights;
                else vm.ShowAnimation = !vm.ShowAnimation;
                e.Handled = true;
            }
            else if (e.Key == Key.G)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) vm.ShowGrid = !vm.ShowGrid;
                else vm.GhostLooseItems = !vm.GhostLooseItems;
                e.Handled = true;
            }
            else if (e.Key == Key.I)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) vm.ShowClientBox = !vm.ShowClientBox;
                else vm.SelectedWorkspaceTabIndex = 2;
                e.Handled = true;
            }
            else if (e.Key == Key.K)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) vm.ShowLightStrength = !vm.ShowLightStrength;
                else vm.ShowWallHooks = !vm.ShowWallHooks;
                e.Handled = true;
            }
            else if (e.Key == Key.T)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) vm.ShowTechnicalItems = !vm.ShowTechnicalItems;
                else vm.SelectedWorkspaceTabIndex = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.W)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) vm.ShowWaypoints = !vm.ShowWaypoints;
                else vm.SelectedWorkspaceTabIndex = 7;
                e.Handled = true;
            }
            else if (e.Key == Key.Y)
            {
                vm.ShowTooltips = !vm.ShowTooltips;
                e.Handled = true;
            }
            else if (e.Key == Key.Q)
            {
                vm.ShowShade = !vm.ShowShade;
                e.Handled = true;
            }
            else if (e.Key == Key.F)
            {
                vm.ShowCreatures = !vm.ShowCreatures;
                e.Handled = true;
            }
            else if (e.Key == Key.S)
            {
                vm.ShowSpawns = !vm.ShowSpawns;
                e.Handled = true;
            }
            else if (e.Key == Key.E)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) vm.ShowAsMinimap = !vm.ShowAsMinimap;
                else vm.ShowSpecialTiles = !vm.ShowSpecialTiles;
                e.Handled = true;
            }
            else if (e.Key == Key.O)
            {
                vm.ShowBlocking = !vm.ShowBlocking;
                e.Handled = true;
            }
            else if (e.Key == Key.V)
            {
                vm.HighlightItems = !vm.HighlightItems;
                e.Handled = true;
            }
            else if (e.Key == Key.U)
            {
                vm.HighlightLockedDoors = !vm.HighlightLockedDoors;
                e.Handled = true;
            }
            else if (e.Key == Key.A)
            {
                vm.Automagic = !vm.Automagic;
                e.Handled = true;
            }
            else if (e.Key == Key.D)
            {
                vm.SelectedWorkspaceTabIndex = 1;
                e.Handled = true;
            }
            else if (e.Key == Key.N)
            {
                vm.SelectedWorkspaceTabIndex = 4;
                e.Handled = true;
            }
            else if (e.Key == Key.H)
            {
                vm.SelectedWorkspaceTabIndex = 6;
                e.Handled = true;
            }
            else if (e.Key == Key.C)
            {
                vm.SelectedWorkspaceTabIndex = 5;
                e.Handled = true;
            }
            else if (e.Key == Key.R)
            {
                vm.SelectedWorkspaceTabIndex = 3;
                e.Handled = true;
            }
            else if (e.Key == Key.M)
            {
                vm.SelectedWorkspaceTabIndex = 10;
                e.Handled = true;
            }
            else if (e.Key == Key.F8)
            {
                vm.CalculateStatisticsCommand.Execute(null);
                vm.SelectedWorkspaceTabIndex = 14;
                e.Handled = true;
            }
            else if (e.Key == Key.F10)
            {
                TakeScreenshot_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F11)
            {
                ToggleFullscreen_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.J)
            {
                vm.JumpToBrushCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }
        switch (e.Key)
        {
            case Key.N when e.KeyModifiers == KeyModifiers.Control:
                vm.NewMapCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.O when e.KeyModifiers == KeyModifiers.Control:
                OpenOtbm_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.E:
                vm.ShowOnlyColors = !vm.ShowOnlyColors;
                e.Handled = true;
                break;
            case Key.M:
                vm.ShowOnlyModified = !vm.ShowOnlyModified;
                e.Handled = true;
                break;
            case Key.L:
                vm.GhostHigherFloors = !vm.GhostHigherFloors;
                e.Handled = true;
                break;
            case Key.H:
                vm.ShowHouses = !vm.ShowHouses;
                e.Handled = true;
                break;
            case Key.W:
                vm.ShowAllFloors = !vm.ShowAllFloors;
                e.Handled = true;
                break;
            case Key.T:
                vm.SelectedWorkspaceTabIndex = 8;
                e.Handled = true;
                break;
            case Key.P:
                vm.SelectedWorkspaceTabIndex = 11;
                e.Handled = true;
                break;
            case Key.F:
                vm.SelectedWorkspaceTabIndex = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 14 : 12;
                e.Handled = true;
                break;
            case Key.B when vm.BorderizeSelectionCommand.CanExecute(null):
                vm.BorderizeSelectionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Shift) && vm.RedoCommand.CanExecute(null):
                vm.RedoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Z when vm.UndoCommand.CanExecute(null):
                vm.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Y when vm.RedoCommand.CanExecute(null):
                vm.RedoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.S when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                SaveAsOtbm_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.S when vm.SaveMapCommand.CanExecute(null):
                vm.SaveMapCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.C when vm.CopySelectionCommand.CanExecute(null):
                vm.CopySelectionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.X when vm.CutSelectionCommand.CanExecute(null):
                vm.CutSelectionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.V when vm.PasteSelectionCommand.CanExecute(null):
                vm.PasteSelectionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.A when vm.SelectAllCommand.CanExecute(null):
                vm.SelectAllCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.G when vm.GoToPositionCommand.CanExecute(null):
                vm.GoToPositionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.J:
                vm.JumpToItemCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                vm.ZoomNormalCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.OemPlus:
            case Key.Add:
                vm.ZoomInCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.OemMinus:
            case Key.Subtract:
                vm.ZoomOutCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _pendingViewportSize = e.NewSize;
        if (_viewportResizeScheduled) return;

        // Maksymalizacja emituje serię zmian rozmiaru. Przeliczenie tysięcy pól
        // dla każdego pośredniego wymiaru blokowało wątek UI, więc zachowujemy
        // tylko ostatni rozmiar i stosujemy go raz w następnej klatce.
        _viewportResizeScheduled = true;
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.RequestAnimationFrame(_ => ApplyPendingViewportSize());
        else
            Dispatcher.UIThread.Post(ApplyPendingViewportSize, DispatcherPriority.Render);
    }

    private void ApplyPendingViewportSize()
    {
        _viewportResizeScheduled = false;
        if (DataContext is not MapEditorViewModel vm) return;

        // Po zakończeniu layoutu znamy faktyczny obszar mapy, bez zgadywania
        // szerokości palety i pasków narzędzi. Fallback obsługuje pierwszą klatkę.
        var measuredWidth = ViewportHost.Bounds.Width;
        var measuredHeight = ViewportHost.Bounds.Height;
        var widthPx = double.IsFinite(measuredWidth) && measuredWidth > 0
            ? measuredWidth
            : Math.Max(200, _pendingViewportSize.Width - 280);
        var heightPx = double.IsFinite(measuredHeight) && measuredHeight > 0
            ? measuredHeight
            : Math.Max(200, _pendingViewportSize.Height - 200);
        var columns = Math.Max(8, (int)(widthPx / Math.Max(1, vm.TileSize)));
        var rows = Math.Max(8, (int)(heightPx / Math.Max(1, vm.TileSize)));
        vm.SetViewportSize(columns, rows);
    }

    private async void OpenOtbm_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = LocalizationManager.Translate("Wybierz plik OTBM"),
                AllowMultiple = false,
                FileTypeFilter = [OtbmFileType],
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
                await vm.LoadMapAsync(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Map Editor file picker failed: {ex}");
            vm.Status = $"Nie udało się otworzyć mapy: {ex.Message}";
        }
    }

    private async void SaveAsOtbm_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Translate("Zapisz mapę OTBM jako..."),
            DefaultExtension = "otbm",
            SuggestedFileName = string.IsNullOrEmpty(vm.MapFileName) ? "moja.otbm" : vm.MapFileName,
            FileTypeChoices = [OtbmFileType],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            await vm.SaveMapAsync(path);
        }
    }

    private async void OpenAssets_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        try
        {
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LocalizationManager.Translate("Wybierz folder klienta (Tibia.dat + Tibia.spr) albo assets Tibia 12+"),
                AllowMultiple = false,
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
                await vm.LoadAssetsAsync(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Map Editor assets picker failed: {ex}");
            vm.Status = $"Nie udało się otworzyć folderu assets: {ex.Message}";
        }
    }

    private async void ImportOtbm_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Importuj mapę OTBM"),
            AllowMultiple = false,
            FileTypeFilter = [OtbmFileType]
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await vm.ImportMapAsync(path);
    }

    private async void ConversionFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz klienta docelowego konwersji"),
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            await vm.AnalyzeConversionAsync(path);
    }

    private async void ExportMinimap_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Translate("Eksportuj minimapę RME"),
            DefaultExtension = "bmp",
            SuggestedFileName = Path.GetFileNameWithoutExtension(vm.MapFileName) + "_minimap.bmp",
            FileTypeChoices = [BitmapFileType]
        });
        if (file?.TryGetLocalPath() is { } path)
            await vm.ExportMinimapAsync(path);
    }

    private async void ImportCreatures_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Importuj monsters.xml, potwora lub NPC"),
            AllowMultiple = true,
            FileTypeFilter = [XmlFileType]
        });
        var paths = files.Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();
        if (paths.Length > 0) await vm.ImportCreatureFilesAsync(paths);
    }

    private async void ExportTilesets_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || TopLevel.GetTopLevel(this) is not { } top)
            return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Translate("Eksportuj tilesety RME"),
            DefaultExtension = "xml",
            SuggestedFileName = "tilesets.xml",
            FileTypeChoices = [XmlFileType]
        });
        if (file?.TryGetLocalPath() is { } path) await vm.ExportTilesetsAsync(path);
    }

    private async void TakeScreenshot_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || ViewportHost.Bounds.Width < 1 || ViewportHost.Bounds.Height < 1)
            return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Translate("Zapisz zrzut widoku mapy"),
            DefaultExtension = "png",
            SuggestedFileName = "map-view.png",
            FileTypeChoices = [PngFileType]
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        var bitmap = new RenderTargetBitmap(
            new PixelSize(Math.Max(1, (int)ViewportHost.Bounds.Width), Math.Max(1, (int)ViewportHost.Bounds.Height)),
            new Vector(96, 96));
        bitmap.Render(ViewportHost);
        bitmap.Save(path);
        bitmap.Dispose();
        if (DataContext is MapEditorViewModel vm) vm.Status = $"Zapisano zrzut widoku: {Path.GetFileName(path)}.";
    }

    private void ToggleFullscreen_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;
        window.WindowState = window.WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }

    private void Viewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || sender is not Control control || e.Delta.Y == 0) return;
        if (_isMovingSelection)
        {
            e.Handled = true;
            return;
        }
        var position = e.GetPosition(control);
        vm.ZoomAt(position.X, position.Y, control.Bounds.Width, control.Bounds.Height, e.Delta.Y > 0);
        e.Handled = true;
    }

    private void Minimap_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm || sender is not Image image || vm.MinimapImage is null)
            return;
        var size = vm.MinimapImage.PixelSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        var scale = Math.Min(image.Bounds.Width / size.Width, image.Bounds.Height / size.Height);
        var renderedWidth = size.Width * scale;
        var renderedHeight = size.Height * scale;
        var offsetX = (image.Bounds.Width - renderedWidth) / 2;
        var offsetY = (image.Bounds.Height - renderedHeight) / 2;
        var position = e.GetPosition(image);
        if (position.X < offsetX || position.Y < offsetY ||
            position.X > offsetX + renderedWidth || position.Y > offsetY + renderedHeight) return;
        vm.NavigateFromMinimap(
            (position.X - offsetX) / renderedWidth,
            (position.Y - offsetY) / renderedHeight);
        e.Handled = true;
    }

    private void Viewport_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm) return;
        Focus();
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsLeftButtonPressed && vm.SelectionMode && sender is Control selectionControl)
        {
            var pos = e.GetPosition(selectionControl);
            var ts = Math.Max(1, vm.TileSize);
            var tileX = vm.ViewX + (int)(pos.X / ts);
            var tileY = vm.ViewY + (int)(pos.Y / ts);
            if (tileX is >= 0 and <= ushort.MaxValue && tileY is >= 0 and <= ushort.MaxValue)
            {
                var mapX = (ushort)tileX;
                var mapY = (ushort)tileY;
                var additive = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                if (!additive && vm.BeginSelectionMove(mapX, mapY))
                {
                    SelectionMovePreview.BeginPreview(MapViewport.Tiles);
                    _isMovingSelection = true;
                }
                else
                {
                    _isSelecting = true;
                    vm.BeginSelection(mapX, mapY, additive);
                }
                e.Pointer.Capture(sender as IInputElement);
            }
            e.Handled = true;
            return;
        }

        // PPM otwiera menu pola jak w RME; Shift+PPM pozostaje szybka gumka.
        if (props.IsRightButtonPressed && sender is Control rightCtl)
        {
            var pos = e.GetPosition(rightCtl);
            var ts = Math.Max(1, vm.TileSize);
            var tileX = vm.ViewX + (int)(pos.X / ts);
            var tileY = vm.ViewY + (int)(pos.Y / ts);
            if (tileX is >= 0 and <= ushort.MaxValue && tileY is >= 0 and <= ushort.MaxValue)
            {
                vm.SetContextTile((ushort)tileX, (ushort)tileY);
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    _isErasing = vm.BeginTileStroke(erasing: true);
                    _lastStrokeTile = ((ushort)tileX, (ushort)tileY);
                    vm.EraseAt((ushort)tileX, (ushort)tileY);
                    vm.FlushTileStrokeViewport();
                    if (_isErasing) e.Pointer.Capture(sender as IInputElement);
                    e.Handled = true;
                }
            }
            return;
        }

        // Lewy z wybranym pędzlem = użycie aktualnego narzędzia RME.
        if (props.IsLeftButtonPressed && vm.SelectedBrush is not null && sender is Control leftCtl)
        {
            var pos = e.GetPosition(leftCtl);
            var ts = Math.Max(1, vm.TileSize);
            var tileX = vm.ViewX + (int)(pos.X / ts);
            var tileY = vm.ViewY + (int)(pos.Y / ts);
            if (tileX is >= 0 and <= ushort.MaxValue && tileY is >= 0 and <= ushort.MaxValue)
            {
                _isPainting = vm.BeginTileStroke(erasing: false);
                _lastStrokeTile = ((ushort)tileX, (ushort)tileY);
                vm.PlaceBrushAt((ushort)tileX, (ushort)tileY);
                vm.FlushTileStrokeViewport();
                if (_isPainting) e.Pointer.Capture(sender as IInputElement);
            }
            e.Handled = true;
            return;
        }

        // Lewy / środkowy bez pędzla = drag-pan
        if (props.IsLeftButtonPressed || props.IsMiddleButtonPressed)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(this);
            _dragStartView = (vm.ViewX, vm.ViewY);
            e.Pointer.Capture(sender as IInputElement);
        }
    }

    private void Palette_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm) return;
        if (sender is Control ctl && ctl.DataContext is MapPaletteItem item)
        {
            vm.SelectedBrush = item;
            e.Handled = true;
        }
    }

    private void MapTool_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel vm && sender is Control { Tag: not null } control)
            vm.SelectMapTool(control.Tag.ToString() ?? string.Empty);
    }

    private void ShowSelectedPalette_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MapEditorViewModel)?.ActivateSelectedPalette();

    private void WorkspacePanel_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel vm && sender is Control { Tag: not null } control)
            vm.SelectWorkspacePanel(control.Tag.ToString() ?? string.Empty);
    }

    private void BrushSize_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel vm && sender is Control { Tag: not null } control &&
            int.TryParse(control.Tag.ToString(), out var size) && vm.BrushSizes.Contains(size))
            vm.BrushSize = size;
    }

    private void BrushShape_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel vm && sender is Control { Tag: not null } control &&
            control.Tag.ToString() is { } shape && vm.BrushShapes.Contains(shape))
            vm.BrushShape = shape;
    }

    private void ContextTileProperties_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MapEditorViewModel)?.OpenContextTileProperties();

    private void ContextFindItem_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MapEditorViewModel)?.FindContextItemInPalette();

    private void ContextFindCreature_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MapEditorViewModel)?.FindContextCreatureInPalette();

    private void ContextSelectTile_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MapEditorViewModel)?.SelectContextTile();

    private void ContextAssignHouse_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MapEditorViewModel)?.AssignContextTileToSelectedHouse();

    private void ContextToggleZone_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel vm && sender is Control { Tag: not null } control)
            vm.ToggleContextZone(control.Tag.ToString() ?? string.Empty);
    }

    private void ContextRemoveEntity_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MapEditorViewModel vm && sender is Control { Tag: not null } control)
            vm.RemoveContextSpawnOrCreatures(
                string.Equals(control.Tag.ToString(), "Spawn", StringComparison.OrdinalIgnoreCase));
    }

    private void ContextEraseTile_Click(object? sender, RoutedEventArgs e) =>
        (DataContext as MapEditorViewModel)?.EraseContextTile();

    private void Viewport_RenderMetricsUpdated(object? sender, MapViewportRenderMetricsEventArgs e)
    {
        if (DataContext is MapEditorViewModel viewModel)
            viewModel.UpdateRenderDiagnostics(
                e.FramesPerSecond,
                e.AverageRenderMilliseconds,
                e.SampledFrames);
    }

    private void ScheduleViewportInputFrame()
    {
        if (_viewportInputScheduled) return;
        _viewportInputScheduled = true;
        if (TopLevel.GetTopLevel(this) is { } topLevel)
            topLevel.RequestAnimationFrame(_ => ProcessQueuedViewportInput());
        else
            Dispatcher.UIThread.Post(ProcessQueuedViewportInput, DispatcherPriority.Render);
    }

    private void ProcessQueuedViewportInput()
    {
        _viewportInputScheduled = false;
        if (DataContext is not MapEditorViewModel vm)
        {
            ClearQueuedViewportInput();
            return;
        }

        if (_pendingPanOrigin is { } origin)
        {
            _pendingPanOrigin = null;
            vm.SetViewOrigin(origin.X, origin.Y);
        }
        if (_pendingPanDeltaX != 0 || _pendingPanDeltaY != 0)
        {
            var deltaX = _pendingPanDeltaX;
            var deltaY = _pendingPanDeltaY;
            _pendingPanDeltaX = 0;
            _pendingPanDeltaY = 0;
            vm.PanBy(deltaX, deltaY);
        }

        if (_pendingSelectionTile is { } selection)
        {
            _pendingSelectionTile = null;
            if (_isSelecting) vm.UpdateSelection(selection.X, selection.Y);
        }
        if (_pendingSelectionMoveTile is { } move)
        {
            _pendingSelectionMoveTile = null;
            if (_isMovingSelection)
            {
                vm.UpdateSelectionMove(move.X, move.Y);
                var (offsetX, offsetY) = vm.SelectionMoveOffset;
                SelectionMovePreview.UpdateOffset(offsetX, offsetY);
            }
        }

        ApplyPendingStroke(vm);
    }

    private void ApplyPendingStroke(MapEditorViewModel vm)
    {
        if (_pendingStrokeTile is not { } current) return;
        _pendingStrokeTile = null;
        if (!_isPainting && !_isErasing) return;
        if (_lastStrokeTile is not { } previous || current == previous) return;

        var segment = RasterizeLine(previous, current).Skip(1).ToArray();
        if (_isPainting) vm.PlaceBrushPath(segment);
        else vm.ErasePath(segment);
        _lastStrokeTile = current;
        vm.FlushTileStrokeViewport();
    }

    private void ClearQueuedViewportInput()
    {
        _pendingStrokeTile = null;
        _pendingSelectionTile = null;
        _pendingSelectionMoveTile = null;
        _pendingPanOrigin = null;
        _pendingPanDeltaX = 0;
        _pendingPanDeltaY = 0;
    }

    private void Viewport_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MapEditorViewModel vm) return;

        // Hover tile pod kursorem — wylicz koordynaty mapy z pozycji w canvas
        if (!_isDragging && sender is Control control)
        {
            var canvasPos = e.GetPosition(control);
            var tileSize = Math.Max(1, vm.TileSize);
            // Pozycja w scrollu: ScrollViewer.Offset wpływa, ale w naszym layout canvas children są
            // pozycjonowani per CanvasX/CanvasY = (dx, dy) * TileSize bezpośrednio.
            var dx = (int)(canvasPos.X / tileSize);
            var dy = (int)(canvasPos.Y / tileSize);
            var tileX = vm.ViewX + dx;
            var tileY = vm.ViewY + dy;
            if (tileX >= 0 && tileX <= ushort.MaxValue && tileY >= 0 && tileY <= ushort.MaxValue)
            {
                // Podgląd pędzla ma osobną, lekką warstwę, więc może podążać za
                // kursorem natychmiast. Ciężkie malowanie nadal wykonujemy raz na klatkę.
                vm.SetHoveredTile((ushort)tileX, (ushort)tileY);
            }
        }

        if ((_isPainting || _isErasing) && sender is Control strokeControl)
        {
            var position = e.GetPosition(strokeControl);
            var tileSize = Math.Max(1, vm.TileSize);
            var tileX = vm.ViewX + (int)(position.X / tileSize);
            var tileY = vm.ViewY + (int)(position.Y / tileSize);
            if (tileX is >= 0 and <= ushort.MaxValue && tileY is >= 0 and <= ushort.MaxValue)
            {
                var current = ((ushort)tileX, (ushort)tileY);
                if (_lastStrokeTile is { } previous && current != previous)
                {
                    _pendingStrokeTile = current;
                    ScheduleViewportInputFrame();
                }
            }
            e.Handled = true;
            return;
        }

        if (_isSelecting && sender is Control selectionControl)
        {
            var position = e.GetPosition(selectionControl);
            var tileSize = Math.Max(1, vm.TileSize);
            var tileX = vm.ViewX + (int)(position.X / tileSize);
            var tileY = vm.ViewY + (int)(position.Y / tileSize);
            if (tileX is >= 0 and <= ushort.MaxValue && tileY is >= 0 and <= ushort.MaxValue)
            {
                _pendingSelectionTile = ((ushort)tileX, (ushort)tileY);
                ScheduleViewportInputFrame();
            }
            e.Handled = true;
            return;
        }

        if (_isMovingSelection && sender is Control moveControl)
        {
            var position = e.GetPosition(moveControl);
            var tileSize = Math.Max(1, vm.TileSize);
            var tileX = vm.ViewX + (int)(position.X / tileSize);
            var tileY = vm.ViewY + (int)(position.Y / tileSize);
            if (tileX is >= 0 and <= ushort.MaxValue && tileY is >= 0 and <= ushort.MaxValue)
            {
                _pendingSelectionMoveTile = ((ushort)tileX, (ushort)tileY);
                ScheduleViewportInputFrame();
            }
            e.Handled = true;
            return;
        }

        if (!_isDragging) return;
        var cur = e.GetPosition(this);
        var dxd = cur.X - _dragStart.X;
        var dyd = cur.Y - _dragStart.Y;
        var ts = Math.Max(1, vm.TileSize);
        var deltaX = (int)(-dxd / ts);
        var deltaY = (int)(-dyd / ts);
        var newX = Math.Max(0, _dragStartView.X + deltaX);
        var newY = Math.Max(0, _dragStartView.Y + deltaY);
        _pendingPanOrigin = (
            (ushort)Math.Min(ushort.MaxValue, newX),
            (ushort)Math.Min(ushort.MaxValue, newY));
        ScheduleViewportInputFrame();
    }

    private void Viewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ProcessQueuedViewportInput();
        if (DataContext is MapEditorViewModel vm && (_isPainting || _isErasing))
            vm.EndTileStroke();
        if (DataContext is MapEditorViewModel selectionVm && _isSelecting)
            selectionVm.EndSelection();
        if (DataContext is MapEditorViewModel moveVm && _isMovingSelection)
        {
            moveVm.EndSelectionMove();
            SelectionMovePreview.Clear();
        }
        _isPainting = false;
        _isErasing = false;
        _lastStrokeTile = null;
        _isDragging = false;
        _isSelecting = false;
        _isMovingSelection = false;
        e.Pointer.Capture(null);
    }

    private void Viewport_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        CancelActiveViewportInteraction();

    private void CancelActiveViewportInteraction()
    {
        ProcessQueuedViewportInput();
        if (DataContext is MapEditorViewModel vm)
        {
            if (_isPainting || _isErasing) vm.EndTileStroke();
            if (_isSelecting) vm.EndSelection();
            // The map is not modified until mouse-up, so an unexpected capture loss
            // must restore the source selection rather than committing an accidental move.
            if (_isMovingSelection)
            {
                vm.CancelSelectionMove();
                SelectionMovePreview.Clear();
            }
        }
        _isPainting = false;
        _isErasing = false;
        _lastStrokeTile = null;
        _isDragging = false;
        _isSelecting = false;
        _isMovingSelection = false;
        ClearQueuedViewportInput();
    }

    private static IEnumerable<(ushort X, ushort Y)> RasterizeLine(
        (ushort X, ushort Y) start,
        (ushort X, ushort Y) end)
    {
        var x = (int)start.X;
        var y = (int)start.Y;
        var targetX = (int)end.X;
        var targetY = (int)end.Y;
        var dx = Math.Abs(targetX - x);
        var stepX = x < targetX ? 1 : -1;
        var dy = -Math.Abs(targetY - y);
        var stepY = y < targetY ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            yield return ((ushort)x, (ushort)y);
            if (x == targetX && y == targetY) yield break;
            var doubled = error * 2;
            if (doubled >= dy) { error += dy; x += stepX; }
            if (doubled <= dx) { error += dx; y += stepY; }
        }
    }

    private static FilePickerFileType OtbmFileType => new(LocalizationManager.Translate("Mapa OTBM"))
    {
        Patterns = ["*.otbm"],
        MimeTypes = ["application/octet-stream"],
    };

    private static FilePickerFileType BitmapFileType => new(LocalizationManager.Translate("Bitmapa minimapy"))
    {
        Patterns = ["*.bmp"],
        MimeTypes = ["image/bmp"]
    };

    private static FilePickerFileType XmlFileType => new(LocalizationManager.Translate("Plik XML"))
    {
        Patterns = ["*.xml"],
        MimeTypes = ["application/xml", "text/xml"]
    };

    private static FilePickerFileType PngFileType => new(LocalizationManager.Translate("Obraz PNG"))
    {
        Patterns = ["*.png"],
        MimeTypes = ["image/png"]
    };
}
