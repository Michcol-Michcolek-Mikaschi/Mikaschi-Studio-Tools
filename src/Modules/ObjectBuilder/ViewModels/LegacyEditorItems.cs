using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Modules.ObjectBuilder.Services;
using Narzedzia.Core.Models;

namespace Modules.ObjectBuilder.ViewModels;

public sealed class LegacyThingListItem : ObservableObject
{
    private readonly LegacyThumbnailCache _cache;

    public LegacyThingListItem(DatThingType thing, LegacyThumbnailCache cache)
    {
        Thing = thing;
        _cache = cache;
    }

    public DatThingType Thing { get; }
    public uint Id => Thing.Id;
    public string Flags => Thing.ActiveFlagsText();
    public string Category => Thing.Category switch
    {
        DatThingCategory.Items => "Przedmiot",
        DatThingCategory.Outfits => "Strój",
        DatThingCategory.Effects => "Efekt",
        DatThingCategory.Missiles => "Pocisk",
        _ => "Obiekt"
    };
    public Bitmap? Thumbnail => _cache.GetThing(Thing);
    public DatThingType Source => Thing;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Flags));
        OnPropertyChanged(nameof(Thumbnail));
    }
}

public sealed class LegacySpriteListItem
{
    private readonly LegacyThumbnailCache _cache;

    public LegacySpriteListItem(uint id, LegacyThumbnailCache cache)
    {
        Id = id;
        _cache = cache;
    }

    public uint Id { get; }
    public Bitmap? Thumbnail => _cache.GetSprite(Id);
}

public sealed partial class LegacySpriteSlotItem : ObservableObject
{
    private readonly LegacyThumbnailCache _cache;

    public LegacySpriteSlotItem(
        int index,
        uint spriteId,
        int tileX,
        int tileY,
        int gridColumn,
        int gridRow,
        int layer,
        LegacyThumbnailCache cache)
    {
        Index = index;
        TileX = tileX;
        TileY = tileY;
        GridColumn = gridColumn;
        GridRow = gridRow;
        Layer = layer;
        _spriteId = spriteId;
        _cache = cache;
    }

    public int Index { get; }
    public int TileX { get; }
    public int TileY { get; }
    public int GridColumn { get; }
    public int GridRow { get; }
    public int Layer { get; }
    public string Details =>
        $"Pole: kolumna {GridColumn + 1}, wiersz {GridRow + 1} · warstwa {Layer + 1} · sprite #{SpriteId} · slot DAT {Index}";
    public bool IsEmpty => SpriteId == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Thumbnail))]
    [NotifyPropertyChangedFor(nameof(Details))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private uint _spriteId;

    [ObservableProperty]
    private bool _isSelected;

    public Bitmap? Thumbnail => _cache.GetSprite(SpriteId);
}

public sealed record LegacyFrameGroupListItem(int Index, string Name, int Frames = 1)
{
    public string DisplayName => $"{Index + 1}. {Name} · {Frames} {FrameWord(Frames)}";

    private static string FrameWord(int count) => count == 1 ? "klatka" : count is >= 2 and <= 4 ? "klatki" : "klatek";
}

/// <summary>
/// Jedna pozycja z 216-kolorowej palety używanej przez oryginalny Object Builder.
/// Identyfikator jest jednocześnie wartością zapisywaną w Tibia.dat.
/// </summary>
public sealed record LegacyTibiaColorOption(int Id, IBrush Brush)
{
    public static LegacyTibiaColorOption FromId(int id)
    {
        var safeId = Math.Clamp(id, 0, 215);
        var red = (byte)(safeId / 36 % 6 * 51);
        var green = (byte)(safeId / 6 % 6 * 51);
        var blue = (byte)(safeId % 6 * 51);
        return new LegacyTibiaColorOption(
            safeId,
            new SolidColorBrush(Color.FromRgb(red, green, blue)));
    }
}
