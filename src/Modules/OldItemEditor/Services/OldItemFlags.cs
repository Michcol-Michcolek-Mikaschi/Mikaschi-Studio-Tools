namespace Modules.OldItemEditor.Services;

[Flags]
public enum OldItemFlags : uint
{
    None = 0,
    Unpassable = 1u << 0,
    BlockMissiles = 1u << 1,
    BlockPathfinder = 1u << 2,
    HasElevation = 1u << 3,
    MultiUse = 1u << 4,
    Pickupable = 1u << 5,
    Movable = 1u << 6,
    Stackable = 1u << 7,
    FloorChangeDown = 1u << 8,
    FloorChangeNorth = 1u << 9,
    FloorChangeEast = 1u << 10,
    FloorChangeSouth = 1u << 11,
    FloorChangeWest = 1u << 12,
    StackOrder = 1u << 13,
    Readable = 1u << 14,
    Rotatable = 1u << 15,
    Hangable = 1u << 16,
    HookSouth = 1u << 17,
    HookEast = 1u << 18,
    CanNotDecay = 1u << 19,
    AllowDistanceRead = 1u << 20,
    Unused = 1u << 21,
    ClientCharges = 1u << 22,
    IgnoreLook = 1u << 23,
    IsAnimation = 1u << 24,
    FullGround = 1u << 25,
    ForceUse = 1u << 26
}

public static class OldItemFlagMasks
{
    // Dokładnie te właściwości porównuje Item.Equals w oryginalnym OTTools ItemEditor.
    // Bity techniczne, których stary interfejs nie porównywał, nadal są zachowywane w OTB.
    public const OldItemFlags Comparable =
        OldItemFlags.Unpassable |
        OldItemFlags.BlockMissiles |
        OldItemFlags.BlockPathfinder |
        OldItemFlags.HasElevation |
        OldItemFlags.ForceUse |
        OldItemFlags.MultiUse |
        OldItemFlags.Pickupable |
        OldItemFlags.Movable |
        OldItemFlags.Stackable |
        OldItemFlags.Readable |
        OldItemFlags.Rotatable |
        OldItemFlags.Hangable |
        OldItemFlags.HookSouth |
        OldItemFlags.HookEast |
        OldItemFlags.IgnoreLook |
        OldItemFlags.IsAnimation |
        OldItemFlags.FullGround;
}
