namespace Modules.MapEditor.Services;

/// <summary>
/// Odtwarza geometrię pędzla oraz bufor doodadów z klasycznego RME.
/// </summary>
internal static class RmeBrushPlacementService
{
    public static RmeBrushPlacement Create(
        RmeBrushDefinition? brush,
        ushort fallbackItemId,
        int variation,
        int size,
        RmeBrushShape shape,
        Random random)
    {
        size = Math.Clamp(size, 0, 11);
        if (brush?.Type.Equals("doodad", StringComparison.OrdinalIgnoreCase) == true)
            return CreateDoodadPlacement(brush, variation, size, shape, random);

        var tiles = new List<RmeCompositeTile>();
        foreach (var (offsetX, offsetY) in GetAreaOffsets(size, shape))
        {
            var placement = brush?.CreatePlacement(variation, random)
                            ?? (fallbackItemId > 0
                                ? new RmeBrushPlacement([new RmeCompositeTile(0, 0, 0, [fallbackItemId])])
                                : RmeBrushPlacement.Empty);
            foreach (var tile in placement.Tiles)
                tiles.Add(new RmeCompositeTile(
                    tile.X + offsetX,
                    tile.Y + offsetY,
                    tile.Z,
                    tile.ItemIds));
        }
        return new RmeBrushPlacement(tiles);
    }

    public static IReadOnlyList<(int X, int Y)> GetAreaOffsets(int size, RmeBrushShape shape)
    {
        size = Math.Clamp(size, 0, 11);
        var offsets = new List<(int X, int Y)>();
        for (var y = -size; y <= size; y++)
        for (var x = -size; x <= size; x++)
        {
            if (shape == RmeBrushShape.Circle && Math.Sqrt(x * x + y * y) >= size + 0.005)
                continue;
            offsets.Add((x, y));
        }
        return offsets;
    }

    private static RmeBrushPlacement CreateDoodadPlacement(
        RmeBrushDefinition brush,
        int variation,
        int size,
        RmeBrushShape shape,
        Random random)
    {
        if (size == 0 || brush.OneSize)
            return brush.CreatePlacement(variation, random);

        // To celowo jest wzór RME, a nie liczba pól (2*size+1)^2.
        var area = shape == RmeBrushShape.Square
            ? 2 * size * (2 * size) + 1
            : size == 1 ? 5 : (int)(0.5 + size * size * Math.PI);
        var objectRange = brush.Thickness * area / Math.Max(1, brush.ThicknessCeiling);
        var objectCount = Math.Max(1, objectRange + (objectRange > 0 ? random.Next(objectRange + 1) : 0));
        var occupied = new HashSet<(int X, int Y, int Z)>();
        var tiles = new List<RmeCompositeTile>();

        for (var objectIndex = 0; objectIndex < objectCount; objectIndex++)
        {
            for (var retry = 0; retry < 5; retry++)
            {
                if (!TryRandomOffset(size, shape, random, out var offsetX, out var offsetY))
                    continue;
                var placement = brush.CreatePlacement(variation, random);
                if (placement.Tiles.Count == 0) break;
                if (placement.Tiles.Any(tile => occupied.Contains((
                        tile.X + offsetX,
                        tile.Y + offsetY,
                        tile.Z))))
                    continue;

                foreach (var tile in placement.Tiles)
                {
                    var coordinate = (tile.X + offsetX, tile.Y + offsetY, tile.Z);
                    occupied.Add(coordinate);
                    tiles.Add(new RmeCompositeTile(coordinate.Item1, coordinate.Item2, coordinate.Item3, tile.ItemIds));
                }
                break;
            }
        }

        return new RmeBrushPlacement(tiles);
    }

    private static bool TryRandomOffset(
        int size,
        RmeBrushShape shape,
        Random random,
        out int x,
        out int y)
    {
        for (var retry = 0; retry < 5; retry++)
        {
            x = random.Next(-size, size + 1);
            y = random.Next(-size, size + 1);
            if (shape == RmeBrushShape.Square || Math.Sqrt(x * x + y * y) < size + 0.005)
                return true;
        }
        x = 0;
        y = 0;
        return false;
    }
}

internal enum RmeBrushShape
{
    Square,
    Circle
}
