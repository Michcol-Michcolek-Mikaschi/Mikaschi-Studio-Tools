using Narzedzia.Core.Models;

namespace Narzedzia.Core.Interchange;

/// <summary>
/// Dopasowuje model animacji stroju między klientami ze wspólną grupą klatek
/// i klientami zapisującymi osobne grupy Idle/Walking.
/// </summary>
public static class LegacyOutfitFrameGroupAdapter
{
    public static bool Adapt(
        DatThingType thing,
        bool sourceUsesFrameGroups,
        bool targetSupportsFrameGroups)
    {
        ArgumentNullException.ThrowIfNull(thing);
        if (thing.Category != DatThingCategory.Outfits ||
            sourceUsesFrameGroups == targetSupportsFrameGroups ||
            thing.FrameGroups.Count == 0)
        {
            return false;
        }

        if (!sourceUsesFrameGroups)
        {
            SplitSharedGroup(thing);
            return true;
        }

        CollapseToSharedGroup(thing);
        return true;
    }

    private static void SplitSharedGroup(DatThingType thing)
    {
        var moving = thing.FrameGroups[0];
        var idle = moving.DeepClone();
        var frames = Math.Max(1, (int)moving.Frames);
        var spritesPerFrame = Math.Max(1, moving.TotalSprites / frames);

        idle.GroupType = 0;
        idle.Frames = 1;
        idle.SpriteIds = moving.SpriteIds.Take(spritesPerFrame).ToArray();
        idle.FrameDurations = moving.FrameDurations.Length > 0
            ? [new DatFrameDuration
            {
                Min = moving.FrameDurations[0].Min,
                Max = moving.FrameDurations[0].Max
            }]
            : [];

        moving.GroupType = 1;
        thing.FrameGroups.Clear();
        thing.FrameGroups.Add(idle);
        thing.FrameGroups.Add(moving);
    }

    private static void CollapseToSharedGroup(DatThingType thing)
    {
        var shared = thing.FrameGroups.FirstOrDefault(group => group.GroupType == 1)
                     ?? thing.FrameGroups[0];
        shared.GroupType = 0;
        thing.FrameGroups.Clear();
        thing.FrameGroups.Add(shared);
    }
}
