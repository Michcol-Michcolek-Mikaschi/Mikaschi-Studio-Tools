using Narzedzia.Core.Models;
using Narzedzia.Core.Tibia12;

namespace Narzedzia.Core.Interchange;

/// <summary>
/// Tworzy reprezentację obiektu dla trybu transferu bez flag rozgrywki.
/// Zachowuje typ obiektu, układ animacji, sprite'y oraz opcjonalne
/// przesunięcie X/Y.
/// </summary>
public static class ObjectTransferProfile
{
    public static DatThingType Create(DatThingType source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = new DatThingType
        {
            Id = source.Id,
            Category = source.Category,
            HasOffset = source.HasOffset,
            OffsetX = source.HasOffset ? source.OffsetX : (short)0,
            OffsetY = source.HasOffset ? source.OffsetY : (short)0
        };
        result.FrameGroups.AddRange(source.FrameGroups.Select(group => group.DeepClone()));
        return result;
    }

    public static Appearance Create(Appearance source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = new Appearance
        {
            Id = source.Id,
            AppearanceType = source.AppearanceType
        };
        result.FrameGroup.Add(source.FrameGroup.Select(group => group.Clone()));
        result.SpriteData.Add(source.SpriteData);

        if (source.Flags?.Shift is { } shift)
        {
            result.Flags = new AppearanceFlags { Shift = shift.Clone() };
        }

        return result;
    }
}
