namespace Narzedzia.Core.Assets;

public sealed record AssetSpriteSheet(
    string File,
    int SpriteType,
    uint FirstSpriteId,
    uint LastSpriteId,
    SpriteSheetLayout Layout)
{
    public int TileWidth => Layout.TileWidth;
    public int TileHeight => Layout.TileHeight;
    public int Columns => Layout.Columns;
    public int Rows => Layout.Rows;
    public int TileCount => Math.Min(Layout.TileCount, (int)(LastSpriteId - FirstSpriteId + 1));

    public bool Contains(uint spriteId) =>
        spriteId >= FirstSpriteId && spriteId <= LastSpriteId;

    public static AssetSpriteSheet FromCatalogEntry(CatalogEntry entry)
    {
        return new AssetSpriteSheet(
            entry.File,
            entry.SpriteType,
            ToUInt(entry.FirstSpriteid),
            ToUInt(entry.LastSpriteid),
            SpriteSheetLayout.FromSpriteType(entry.SpriteType));
    }

    private static uint ToUInt(int value) => value < 0 ? 0u : (uint)value;
}
