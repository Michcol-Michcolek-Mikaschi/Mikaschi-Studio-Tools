namespace Modules.AssetsEditor.Services;

public sealed record TextureCursorContext(
    int GroupIndex,
    int Direction,
    int Addon,
    int PatternZ,
    int Frame,
    int Layer,
    int TileX,
    int TileY,
    int SlotIndex);
