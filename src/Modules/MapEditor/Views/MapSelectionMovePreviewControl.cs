using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Modules.MapEditor.ViewModels;

namespace Modules.MapEditor.Views;

/// <summary>
/// Lekka warstwa przeciąganego zaznaczenia. Przechowuje tylko referencje do
/// widocznych, już zbudowanych kafelków i przesuwa je transformacją renderera.
/// Ruch myszy nie zmienia dzięki temu VisibleTiles ani cache fragmentów mapy.
/// </summary>
public sealed class MapSelectionMovePreviewControl : Control
{
    private static readonly RenderOptions PixelArtRenderOptions = new()
    {
        BitmapInterpolationMode = BitmapInterpolationMode.None
    };

    private MapTileItem[] _sourceTiles = [];
    private int _offsetTilesX;
    private int _offsetTilesY;

    public MapSelectionMovePreviewControl()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        DetachedFromVisualTree += (_, _) => Clear();
    }

    /// <summary>
    /// Zapamiętuje widoczną część zaznaczenia dokładnie raz na początku drag.
    /// Zaznaczenie może obejmować więcej mapy, ale elementy poza ekranem nie
    /// wymagają kosztownego tworzenia obiektów wyłącznie na potrzeby podglądu.
    /// </summary>
    public void BeginPreview(IReadOnlyList<MapTileItem>? viewportTiles)
    {
        _sourceTiles = viewportTiles is { Count: > 0 }
            ? viewportTiles.Where(tile => tile.IsSelected).ToArray()
            : [];
        _offsetTilesX = 0;
        _offsetTilesY = 0;
        InvalidateVisual();
    }

    public void UpdateOffset(int offsetTilesX, int offsetTilesY)
    {
        if (_offsetTilesX == offsetTilesX && _offsetTilesY == offsetTilesY) return;
        _offsetTilesX = offsetTilesX;
        _offsetTilesY = offsetTilesY;
        InvalidateVisual();
    }

    public void Clear()
    {
        if (_sourceTiles.Length == 0 && _offsetTilesX == 0 && _offsetTilesY == 0) return;
        _sourceTiles = [];
        _offsetTilesX = 0;
        _offsetTilesY = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_sourceTiles.Length == 0 || (_offsetTilesX == 0 && _offsetTilesY == 0)) return;

        var tileSize = _sourceTiles[0].TileSize;
        using var renderOptions = context.PushRenderOptions(PixelArtRenderOptions);
        using var opacity = context.PushOpacity(0.76);
        using var transform = context.PushTransform(Matrix.CreateTranslation(
            _offsetTilesX * tileSize,
            _offsetTilesY * tileSize));
        MapViewportControl.RenderTileBatch(context, _sourceTiles);
    }
}
