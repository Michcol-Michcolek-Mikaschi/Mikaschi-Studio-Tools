using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modules.AssetsEditor.Services;
using Narzedzia.Contracts.Localization;
using Narzedzia.Core.Assets;

namespace Modules.AssetsEditor.ViewModels;

public sealed record SpriteSheetListItem(AssetSpriteSheet Sheet)
{
    public string DisplayName =>
        $"{Sheet.File} [{Sheet.FirstSpriteId}-{Sheet.LastSpriteId}] {Sheet.TileWidth}x{Sheet.TileHeight}";
}

public sealed record SpriteSheetLayoutOption(int SpriteType, int TileWidth, int TileHeight)
{
    public string DisplayName => $"Typ {SpriteType} ({TileWidth}x{TileHeight})";
}

public partial class SpriteSheetEditorViewModel : ObservableObject, IDisposable
{
    private readonly IAssetSpriteStore _store;

    public ObservableCollection<SpriteSheetListItem> Sheets { get; } = new();

    [ObservableProperty] private SpriteSheetListItem? _selectedSheetItem;
    [ObservableProperty] private Bitmap? _sheetPreview;
    [ObservableProperty] private uint _searchSpriteId;
    [ObservableProperty] private int _newSheetSpriteType;
    [ObservableProperty] private uint _newSheetFirstSpriteId;
    [ObservableProperty] private SpriteSheetLayoutOption? _selectedNewSheetLayout;
    [ObservableProperty] private int _importStartTile;
    [ObservableProperty] private string _statusText = "Gotowe.";

    public IReadOnlyList<SpriteSheetLayoutOption> NewSheetLayouts { get; }

    public SpriteSheetEditorViewModel(IAssetSpriteStore store, uint initialSpriteId = 0)
    {
        _store = store;
        NewSheetLayouts = Enumerable.Range(0, 36)
            .Select(type =>
            {
                var layout = SpriteSheetLayout.FromSpriteType(type);
                return new SpriteSheetLayoutOption(layout.SpriteType, layout.TileWidth, layout.TileHeight);
            })
            .ToArray();

        SelectedNewSheetLayout = NewSheetLayouts.FirstOrDefault();
        SearchSpriteId = initialSpriteId;
        NewSheetFirstSpriteId = initialSpriteId == 0 ? 1u : initialSpriteId;
        RefreshSheets();

        if (initialSpriteId > 0)
        {
            SelectSprite(initialSpriteId);
        }
        else
        {
            SelectedSheetItem = Sheets.FirstOrDefault();
        }
    }

    public void Dispose()
    {
        SheetPreview?.Dispose();
        _store.Dispose();
    }

    [RelayCommand]
    private void SearchSheet()
    {
        if (SearchSpriteId == 0)
        {
            StatusText = "Podaj ID sprite'a.";
            return;
        }

        SelectSprite(SearchSpriteId);
    }

    [RelayCommand]
    private void CreateSheet()
    {
        var spriteType = SelectedNewSheetLayout?.SpriteType ?? NewSheetSpriteType;
        var sheet = _store.CreateSheet(spriteType, NewSheetFirstSpriteId);
        RefreshSheets();
        SelectedSheetItem = Sheets.FirstOrDefault(item => item.Sheet.File == sheet.File);
        StatusText = $"Utworzono nowy arkusz {sheet.File}.";
    }

    [RelayCommand]
    private async Task ImportImageAsync()
    {
        if (SelectedSheetItem is null)
        {
            StatusText = "Wybierz arkusz przed importem.";
            return;
        }

        var tl = GetTopLevel();
        if (tl is null) return;

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationManager.Translate("Importuj obraz do arkusza sprite'ow"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationManager.Translate("Obrazy"))
                {
                    Patterns = ["*.png", "*.bmp", "*.jpg", "*.jpeg"]
                }
            ]
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToArray();

        ImportImagesFromPaths(paths);
    }

    [RelayCommand]
    private async Task ExportSheetAsync()
    {
        if (SelectedSheetItem is null)
        {
            StatusText = "Wybierz arkusz przed eksportem.";
            return;
        }

        var tl = GetTopLevel();
        if (tl is null) return;

        var file = await tl.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationManager.Translate("Eksportuj arkusz sprite'ow"),
            SuggestedFileName = Path.ChangeExtension(SelectedSheetItem.Sheet.File, ".png")
        });

        var path = file?.TryGetLocalPath();
        if (path is null) return;

        _store.ExportSheet(SelectedSheetItem.Sheet, path);
        StatusText = $"Wyeksportowano {Path.GetFileName(path)}.";
    }

    [RelayCommand]
    private void SaveSheet()
    {
        if (SelectedSheetItem is null)
        {
            StatusText = "Wybierz arkusz przed zapisem.";
            return;
        }

        _store.SaveSheet(SelectedSheetItem.Sheet);
        _store.SaveCatalog();
        StatusText = $"Zapisano {SelectedSheetItem.Sheet.File} i catalog-content.json.";
    }

    [RelayCommand]
    private void ClearTile()
    {
        if (SelectedSheetItem is null)
        {
            StatusText = "Wybierz arkusz przed czyszczeniem kafla.";
            return;
        }

        var tile = new byte[SelectedSheetItem.Sheet.TileWidth * SelectedSheetItem.Sheet.TileHeight * 4];
        _store.ReplaceTile(SelectedSheetItem.Sheet, ImportStartTile, tile);
        RefreshPreview();
        StatusText = $"Wyczyszczono kafel {ImportStartTile}.";
    }

    partial void OnSelectedSheetItemChanged(SpriteSheetListItem? value)
    {
        RefreshPreview();
    }

    private void SelectSprite(uint spriteId)
    {
        try
        {
            var sheet = _store.OpenSheet(spriteId);
            SelectedSheetItem = Sheets.FirstOrDefault(item => item.Sheet.File == sheet.File);
            StatusText = $"Znaleziono arkusz dla sprite ID {spriteId}.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private void RefreshSheets()
    {
        Sheets.Clear();
        foreach (var entry in _store.Catalog.Where(entry =>
                     string.Equals(entry.Type, "sprite", StringComparison.OrdinalIgnoreCase)))
        {
            Sheets.Add(new SpriteSheetListItem(AssetSpriteSheet.FromCatalogEntry(entry)));
        }
    }

    private void RefreshPreview()
    {
        if (SelectedSheetItem is null || _store is not AssetSpriteStore store)
        {
            SetPreview(null);
            return;
        }

        var sheet = SelectedSheetItem.Sheet;
        try
        {
            var pixels = store.ReadSheetPixels(sheet);
            SetPreview(ToBitmap(pixels, sheet.Layout.SheetWidth, sheet.Layout.SheetHeight));
        }
        catch (Exception ex)
        {
            SetPreview(null);
            StatusText = $"Nie udało się wczytać arkusza {sheet.File}: {ex.Message}";
        }
    }

    public bool ImportImageFromPath(string path)
    {
        return ImportImagesFromPaths([path]);
    }

    public bool ImportImagesFromPaths(IReadOnlyList<string> paths)
    {
        if (SelectedSheetItem is null)
        {
            StatusText = "Wybierz arkusz przed importem.";
            return false;
        }

        if (paths.Count == 0)
        {
            StatusText = "Nie znaleziono pliku obrazu do importu.";
            return false;
        }

        var imported = _store.ImportImagesIntoSheet(SelectedSheetItem.Sheet, paths, ImportStartTile);
        RefreshPreview();
        StatusText = paths.Count == 1
            ? $"Zaimportowano {Path.GetFileName(paths[0])} ({imported} kafli)."
            : $"Zaimportowano {paths.Count} plików od kafla {ImportStartTile} ({imported} kafli).";
        return true;
    }

    partial void OnSelectedNewSheetLayoutChanged(SpriteSheetLayoutOption? value)
    {
        if (value is not null)
        {
            NewSheetSpriteType = value.SpriteType;
        }
    }

    private void SetPreview(Bitmap? bitmap)
    {
        var old = SheetPreview;
        SheetPreview = bitmap;
        if (old is not null && !ReferenceEquals(old, bitmap))
        {
            old.Dispose();
        }
    }

    private static Bitmap ToBitmap(byte[] pixels, int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using var fb = bitmap.Lock();
        for (var row = 0; row < height; row++)
        {
            var dst = IntPtr.Add(fb.Address, row * fb.RowBytes);
            Marshal.Copy(pixels, row * width * 4, dst, width * 4);
        }

        return bitmap;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is
            IClassicDesktopStyleApplicationLifetime { MainWindow: { } win })
        {
            return TopLevel.GetTopLevel(win);
        }

        return null;
    }
}
