using Narzedzia.Core.Models;

namespace Modules.ObjectBuilder.Services;

public readonly record struct LegacyPatternPosition(int X, int Y);

/// <summary>
/// Mapowanie kierunków zgodne z ThingTypeEditor.selectDirectionButton
/// oryginalnego Object Buildera. Outfit zapisuje cztery kierunki w Pattern X,
/// natomiast missile używa ośmiu pól siatki Pattern X=3 × Pattern Y=3 bez środka.
/// </summary>
public static class LegacyDirectionMapping
{
    public const int North = 0;
    public const int East = 1;
    public const int South = 2;
    public const int West = 3;
    public const int SouthWest = 4;
    public const int SouthEast = 5;
    public const int NorthWest = 6;
    public const int NorthEast = 7;

    public static int GetDirectionCount(DatThingCategory category) => category switch
    {
        DatThingCategory.Outfits => 4,
        DatThingCategory.Missiles => 8,
        _ => 1
    };

    public static LegacyPatternPosition ToPatternPosition(
        DatThingCategory category,
        int direction,
        int addon,
        int patternXCount,
        int patternYCount)
    {
        var safePatternXCount = Math.Max(1, patternXCount);
        var safePatternYCount = Math.Max(1, patternYCount);

        if (category == DatThingCategory.Missiles)
        {
            var position = NormalizeDirection(direction, 8) switch
            {
                North => new LegacyPatternPosition(1, 0),
                East => new LegacyPatternPosition(2, 1),
                South => new LegacyPatternPosition(1, 2),
                West => new LegacyPatternPosition(0, 1),
                SouthWest => new LegacyPatternPosition(0, 2),
                SouthEast => new LegacyPatternPosition(2, 2),
                NorthWest => new LegacyPatternPosition(0, 0),
                NorthEast => new LegacyPatternPosition(2, 0),
                _ => new LegacyPatternPosition(1, 2)
            };

            // Oryginalny Object Builder używa operatora modulo, dzięki czemu
            // niestandardowe pociski z mniejszym patternem nadal są obsługiwane.
            return new LegacyPatternPosition(
                position.X % safePatternXCount,
                position.Y % safePatternYCount);
        }

        if (category == DatThingCategory.Outfits)
        {
            return new LegacyPatternPosition(
                NormalizeDirection(direction, 4) % safePatternXCount,
                Math.Max(0, addon) % safePatternYCount);
        }

        return new LegacyPatternPosition(
            Math.Max(0, direction) % safePatternXCount,
            Math.Max(0, addon) % safePatternYCount);
    }

    private static int NormalizeDirection(int direction, int count) =>
        ((direction % count) + count) % count;
}
