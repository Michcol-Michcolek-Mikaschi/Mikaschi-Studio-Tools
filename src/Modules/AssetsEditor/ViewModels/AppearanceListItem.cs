using Avalonia.Media.Imaging;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.ViewModels;

/// <summary>Element listy obiektów/outfitów — opakowanie Appearance z miniaturą.</summary>
public sealed partial class AppearanceListItem : ObservableObject
{
    [ObservableProperty] private Bitmap? _thumbnail;

    public Appearance Source { get; }

    /// <summary>
    /// Kategoria katalogu, do którego należy ten element (Object/Outfit/Effect/Missile).
    /// Wynika z listy proto (app.Object/Outfit/Effect/Missile[]), a NIE z pola
    /// `Source.AppearanceType` — to ostatnie zwykle ma default=Object w wczytanym pliku.
    /// </summary>
    public APPEARANCE_TYPE Category { get; }

    public uint   Id          => Source.Id;
    public string DisplayName => Source.Name is { Length: > 0 } n
        ? $"{Source.Id} — {n}"
        : $"{Source.Id}";

    public uint FirstSpriteId =>
        Source.FrameGroup.Count > 0 && Source.FrameGroup[0].SpriteInfo?.SpriteId.Count > 0
            ? Source.FrameGroup[0].SpriteInfo.SpriteId[0]
            : 0;

    public AppearanceListItem(Appearance source, APPEARANCE_TYPE category = APPEARANCE_TYPE.AppearanceObject)
    {
        Source = source;
        Category = category;
    }
}

/// <summary>Czytelna pozycja wyboru rzeczywistej grupy klatek obiektu.</summary>
public sealed record AppearanceFrameGroupListItem(int Index, string Name, int Frames)
{
    public string DisplayName => $"{Index + 1}. {Name} · {Frames} {FrameWord(Frames)}";

    public static AppearanceFrameGroupListItem From(int index, FrameGroup group)
    {
        var name = group.FixedFrameGroup switch
        {
            FIXED_FRAME_GROUP.OutfitMoving => "Walking / ruch",
            FIXED_FRAME_GROUP.ObjectInitial => "Obiekt / animacja",
            _ => "Idle / bezczynność"
        };
        var frames = group.SpriteInfo is null
            ? 0
            : Narzedzia.Core.Appearances.AppearanceLayoutInfo.From(group.SpriteInfo).Frames;
        return new AppearanceFrameGroupListItem(index, name, Math.Max(1, frames));
    }

    private static string FrameWord(int count) => count == 1 ? "klatka" : count is >= 2 and <= 4 ? "klatki" : "klatek";
}

/// <summary>Jeden slot sprite'a w panelu Sprite List.</summary>
public sealed partial class SpriteSlotItem : ObservableObject
{
    [ObservableProperty] private Bitmap? _thumbnail;

    public int  Index    { get; init; }
    public uint SpriteId { get; init; }
    public int Layer { get; init; }
    public int PatternX { get; init; }
    public int PatternY { get; init; }
    public int PatternZ { get; init; }
    public int Frame { get; init; }

    public string Label  => $"Slot {Index}: sprite #{SpriteId}";
    public string Details => $"warstwa {Layer}, X {PatternX}, Y {PatternY}, Z {PatternZ}, klatka {Frame}";
}

/// <summary>Pojedynczy sprite z globalnej listy assets po prawej stronie edytora.</summary>
public sealed partial class SpriteBrowserItem(uint spriteId) : ObservableObject
{
    [ObservableProperty] private Bitmap? _thumbnail;

    public uint SpriteId { get; } = spriteId;
    public string Label => $"Sprite #{SpriteId}";
}

/// <summary>Wiersz w tabeli BoundingBoxPerDirection (zakładka Texture).</summary>
public sealed class BoundingBoxItem
{
    public int X      { get; init; }
    public int Y      { get; init; }
    public int Width  { get; init; }
    public int Height { get; init; }
}

/// <summary>Edytowalny wpis oferty NPC zapisany w powtarzalnej fladze npcsaledata.</summary>
public sealed partial class NpcSaleEntryViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _location = string.Empty;

    [ObservableProperty] private decimal _buyPrice;
    [ObservableProperty] private decimal _salePrice;
    [ObservableProperty] private decimal _currencyObjectTypeId;
    [ObservableProperty] private string _currencyQuestFlagDisplayName = string.Empty;

    public string Summary
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Name) ? "Nowy wpis NPC" : Name.Trim();
            return string.IsNullOrWhiteSpace(Location) ? name : $"{name} — {Location.Trim()}";
        }
    }

    public static NpcSaleEntryViewModel From(AppearanceFlagNPC source) => new()
    {
        Name = source.HasName ? source.Name : string.Empty,
        Location = source.HasLocation ? source.Location : string.Empty,
        BuyPrice = source.HasBuyPrice ? source.BuyPrice : 0,
        SalePrice = source.HasSalePrice ? source.SalePrice : 0,
        CurrencyObjectTypeId = source.HasCurrencyObjectTypeId ? source.CurrencyObjectTypeId : 0,
        CurrencyQuestFlagDisplayName = source.HasCurrencyQuestFlagDisplayName
            ? source.CurrencyQuestFlagDisplayName
            : string.Empty
    };

    public NpcSaleDataEditState ToEditState() => new()
    {
        Name = Name,
        Location = Location,
        BuyPrice = ToUInt(BuyPrice),
        SalePrice = ToUInt(SalePrice),
        CurrencyObjectTypeId = ToUInt(CurrencyObjectTypeId),
        CurrencyQuestFlagDisplayName = CurrencyQuestFlagDisplayName
    };

    private static uint ToUInt(decimal value) => value switch
    {
        <= 0 => 0,
        >= uint.MaxValue => uint.MaxValue,
        _ => decimal.ToUInt32(decimal.Truncate(value))
    };
}

/// <summary>Kolor z 216-elementowej palety 6×6×6 używanej przez Assets Editor.</summary>
public sealed record TibiaColorOption(int Id, IBrush Brush)
{
    public static TibiaColorOption FromId(int id)
    {
        var safeId = Math.Clamp(id, 0, 215);
        var red = (byte)(safeId / 36 % 6 * 51);
        var green = (byte)(safeId / 6 % 6 * 51);
        var blue = (byte)(safeId % 6 * 51);
        return new TibiaColorOption(safeId, new SolidColorBrush(Color.FromRgb(red, green, blue)));
    }
}

