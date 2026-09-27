namespace Narzedzia.Core.Assets;

public readonly record struct SpriteSheetLayout(
    int SpriteType,
    int TileWidth,
    int TileHeight,
    int SheetWidth,
    int SheetHeight)
{
    private static readonly (int W, int H)[] Sizes =
    [
        (32, 32), (32, 64), (64, 32), (64, 64), (32, 96), (32, 128), (32, 192), (32, 384),
        (64, 96), (64, 128), (64, 192), (64, 384), (96, 32), (96, 64), (96, 96), (96, 128),
        (96, 192), (96, 384), (128, 32), (128, 64), (128, 96), (128, 128), (128, 192), (128, 384),
        (192, 32), (192, 64), (192, 96), (192, 128), (192, 192), (192, 384), (384, 32), (384, 64),
        (384, 96), (384, 128), (384, 192), (384, 384)
    ];

    public static int SupportedTypeCount => Sizes.Length;

    public int Columns => SheetWidth / TileWidth;

    public int Rows => SheetHeight / TileHeight;

    public int TileCount => Columns * Rows;

    public static SpriteSheetLayout FromSpriteType(int spriteType)
    {
        var safeType = spriteType >= 0 && spriteType < Sizes.Length ? spriteType : 0;
        var (w, h) = Sizes[safeType];
        return new SpriteSheetLayout(safeType, w, h, 384, 384);
    }

    public static bool TryFromDimensions(int width, int height, out SpriteSheetLayout layout)
    {
        for (var spriteType = 0; spriteType < Sizes.Length; spriteType++)
        {
            var (w, h) = Sizes[spriteType];
            if (w != width || h != height) continue;

            layout = new SpriteSheetLayout(spriteType, w, h, 384, 384);
            return true;
        }

        layout = default;
        return false;
    }
}
