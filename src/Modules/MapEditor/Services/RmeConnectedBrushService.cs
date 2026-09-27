using Narzedzia.Core.Models;

namespace Modules.MapEditor.Services;

/// <summary>
/// Układa kierunkowe elementy ścian, stołów i dywanów według tych samych
/// masek sąsiedztwa co WallBrush, TableBrush i CarpetBrush w RME.
/// </summary>
public sealed class RmeConnectedBrushService(RmeMaterialCatalog catalog)
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<RmeWeightedItem>> NoOrientedItems =
        new Dictionary<string, IReadOnlyList<RmeWeightedItem>>(StringComparer.OrdinalIgnoreCase);

    private static readonly (int X, int Y, int Mask)[] EightNeighbours =
    [
        (-1, -1, 1), (0, -1, 2), (1, -1, 4), (-1, 0, 8),
        (1, 0, 16), (-1, 1, 32), (0, 1, 64), (1, 1, 128)
    ];

    private static readonly (int X, int Y, int Mask)[] WallNeighbours =
    [
        (0, -1, 1), (-1, 0, 2), (1, 0, 4), (0, 1, 8)
    ];

    private static readonly string[] FullWallTypes =
    [
        "pole", "south end", "east end", "northwest diagonal",
        "west end", "northeast diagonal", "horizontal", "south t",
        "north end", "vertical", "southwest diagonal", "east t",
        "southeast diagonal", "west t", "north t", "intersection"
    ];

    private static readonly string[] HalfWallTypes =
    [
        "pole", "vertical", "horizontal", "northwest diagonal",
        "pole", "vertical", "horizontal", "northwest diagonal",
        "pole", "vertical", "horizontal", "northwest diagonal",
        "pole", "vertical", "horizontal", "northwest diagonal"
    ];

    // Dokładna tablica CarpetBrush::carpet_types[256] z brush_tables.cpp.
    private static readonly byte[] CarpetTypes =
    [
        13, 13, 13, 5, 6, 1, 6, 1, 13, 4, 5, 5, 13, 13, 5, 5,
        13, 6, 6, 6, 6, 6, 6, 6, 13, 1, 1, 1, 1, 1, 1, 1,
        7, 4, 7, 6, 6, 5, 6, 1, 7, 7, 5, 5, 7, 13, 5, 5,
        13, 13, 6, 6, 13, 6, 6, 6, 7, 13, 13, 13, 13, 13, 13, 1,
        7, 5, 13, 5, 6, 1, 6, 1, 7, 4, 4, 5, 7, 5, 5, 1,
        8, 8, 2, 2, 8, 8, 2, 2, 3, 3, 13, 13, 3, 3, 13, 1,
        7, 7, 7, 4, 7, 13, 13, 4, 7, 7, 4, 4, 7, 7, 4, 4,
        8, 8, 2, 13, 8, 8, 2, 2, 3, 3, 13, 13, 3, 3, 13, 9,
        8, 5, 8, 5, 2, 1, 6, 1, 3, 5, 5, 5, 2, 1, 5, 5,
        3, 8, 6, 6, 2, 2, 6, 6, 3, 3, 1, 1, 2, 1, 1, 1,
        3, 13, 3, 4, 13, 13, 6, 1, 7, 4, 5, 5, 7, 4, 5, 5,
        8, 8, 6, 6, 2, 2, 6, 6, 3, 3, 1, 1, 3, 13, 13, 1,
        8, 8, 2, 13, 8, 8, 2, 2, 7, 7, 4, 4, 7, 7, 4, 4,
        8, 8, 2, 2, 8, 8, 2, 2, 3, 3, 13, 13, 3, 3, 13, 10,
        3, 3, 13, 4, 3, 13, 2, 13, 7, 7, 4, 4, 7, 7, 4, 4,
        8, 8, 2, 2, 8, 8, 2, 2, 3, 3, 13, 12, 3, 3, 11, 13
    ];

    private static readonly string[] CarpetAlignment =
    [
        "", "n", "e", "s", "w", "cnw", "cne", "csw",
        "cse", "dnw", "dne", "dse", "dsw", "center"
    ];

    public static bool IsConnected(RmeBrushDefinition? brush) =>
        brush is not null && (brush.Type.Equals("wall", StringComparison.OrdinalIgnoreCase) ||
                              brush.Type.Equals("wall decoration", StringComparison.OrdinalIgnoreCase) ||
                              brush.Type.Equals("table", StringComparison.OrdinalIgnoreCase) ||
                              brush.Type.Equals("carpet", StringComparison.OrdinalIgnoreCase));

    public void RemoveExistingBrushItems(OtbmTile tile, RmeBrushDefinition brush)
    {
        if (brush.Type.Equals("carpet", StringComparison.OrdinalIgnoreCase))
        {
            tile.Items.RemoveAll(item => catalog.ItemBrushesByItemId.TryGetValue(item.Id, out var existing) &&
                                         existing.Type.Equals("carpet", StringComparison.OrdinalIgnoreCase));
            return;
        }

        tile.Items.RemoveAll(item => catalog.ItemBrushesByItemId.TryGetValue(item.Id, out var existing) &&
                                     ReferenceEquals(existing, brush));
    }

    public void Rebuild(OtbmMap map, IEnumerable<OtbmTileCoord> coordinates, Random random)
    {
        foreach (var coordinate in coordinates.Distinct())
            if (map.Tiles.TryGetValue(coordinate, out var tile))
                RebuildTile(map, tile, random);
    }

    public void RebuildTile(OtbmMap map, OtbmTile tile, Random random)
    {
        foreach (var item in tile.Items.ToArray())
        {
            if (!catalog.ItemBrushesByItemId.TryGetValue(item.Id, out var brush) || !IsConnected(brush))
                continue;

            string? alignment;
            if (brush.Type.Equals("wall", StringComparison.OrdinalIgnoreCase))
                alignment = ResolveWallAlignment(map, tile, brush);
            else if (brush.Type.Equals("wall decoration", StringComparison.OrdinalIgnoreCase))
                alignment = ResolveDecorationAlignment(tile);
            else if (brush.Type.Equals("table", StringComparison.OrdinalIgnoreCase))
                alignment = ResolveTableAlignment(map, tile, brush);
            else
                alignment = ResolveCarpetAlignment(map, tile, brush);

            if (alignment is null || IsAlreadyAligned(item.Id, brush, alignment)) continue;
            var replacement = PickOrientedItem(brush, alignment, random);
            if (replacement != 0) item.Id = replacement;
        }
    }

    private string? ResolveWallAlignment(OtbmMap map, OtbmTile tile, RmeBrushDefinition brush)
    {
        var mask = 0;
        foreach (var (offsetX, offsetY, bit) in WallNeighbours)
            if (HasMatchingWall(map, tile, offsetX, offsetY, brush)) mask |= bit;

        var full = FullWallTypes[mask];
        if (HasOrientedItems(brush, full)) return full;
        var half = HalfWallTypes[mask];
        return HasOrientedItems(brush, half) ? half : null;
    }

    private string? ResolveDecorationAlignment(OtbmTile tile)
    {
        foreach (var item in tile.Items)
        {
            if (!catalog.ItemBrushesByItemId.TryGetValue(item.Id, out var brush) ||
                !brush.Type.Equals("wall", StringComparison.OrdinalIgnoreCase))
                continue;
            return FindAlignment(brush, item.Id);
        }
        return null;
    }

    private string ResolveTableAlignment(OtbmMap map, OtbmTile tile, RmeBrushDefinition brush)
    {
        var mask = NeighbourMask(map, tile, brush);
        return ResolveTableAlignment(mask);
    }

    internal static string ResolveTableAlignment(int mask)
    {
        if ((mask & 24) == 24) return "horizontal";
        if ((mask & 8) != 0) return "east";
        if ((mask & 16) != 0) return "west";
        var north = (mask & 2) != 0;
        var south = (mask & 64) != 0;
        var upperDiagonal = (mask & 5) != 0;
        if (north && south && !upperDiagonal) return "vertical";
        if ((mask & 64) != 0) return "north";
        if (north && !upperDiagonal) return "south";
        return "alone";
    }

    private string ResolveCarpetAlignment(OtbmMap map, OtbmTile tile, RmeBrushDefinition brush) =>
        CarpetAlignment[CarpetTypes[NeighbourMask(map, tile, brush)]];

    private int NeighbourMask(OtbmMap map, OtbmTile tile, RmeBrushDefinition brush)
    {
        var mask = 0;
        foreach (var (offsetX, offsetY, bit) in EightNeighbours)
        {
            var x = tile.X + offsetX;
            var y = tile.Y + offsetY;
            if (x is < 0 or > ushort.MaxValue || y is < 0 or > ushort.MaxValue ||
                !map.Tiles.TryGetValue(new((ushort)x, (ushort)y, tile.Z), out var neighbour))
                continue;
            if (neighbour.Items.Any(item => catalog.ItemBrushesByItemId.TryGetValue(item.Id, out var other) &&
                                           ReferenceEquals(other, brush)))
                mask |= bit;
        }
        return mask;
    }

    private bool HasMatchingWall(
        OtbmMap map,
        OtbmTile tile,
        int offsetX,
        int offsetY,
        RmeBrushDefinition brush)
    {
        var x = tile.X + offsetX;
        var y = tile.Y + offsetY;
        if (x is < 0 or > ushort.MaxValue || y is < 0 or > ushort.MaxValue ||
            !map.Tiles.TryGetValue(new((ushort)x, (ushort)y, tile.Z), out var neighbour))
            return false;

        foreach (var item in neighbour.Items)
        {
            if (!catalog.ItemBrushesByItemId.TryGetValue(item.Id, out var other) ||
                !other.Type.Equals("wall", StringComparison.OrdinalIgnoreCase))
                continue;
            var hates = (other.Doors ?? []).Any(door => door.ItemId == item.Id && door.HatesBrush);
            if (!hates && (ReferenceEquals(other, brush) || FriendOf(brush, other) || FriendOf(other, brush)))
                return true;
        }
        return false;
    }

    private static bool FriendOf(RmeBrushDefinition first, RmeBrushDefinition second)
    {
        var listed = (first.Friends ?? []).Any(name =>
            name.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(second.Name, StringComparison.OrdinalIgnoreCase));
        return listed ? !first.HateFriends : first.HateFriends;
    }

    private ushort PickOrientedItem(RmeBrushDefinition brush, string alignment, Random random)
    {
        var current = brush;
        var visited = new HashSet<RmeBrushDefinition>(ReferenceEqualityComparer.Instance);
        while (visited.Add(current))
        {
            if ((current.OrientedItems ?? NoOrientedItems).TryGetValue(alignment, out var items) && items.Count > 0)
            {
                var total = items.Sum(item => Math.Max(0, item.Chance));
                if (total <= 0) return items[0].ItemId;
                var chance = random.Next(1, total + 1);
                foreach (var item in items)
                {
                    chance -= Math.Max(0, item.Chance);
                    if (chance <= 0) return item.ItemId;
                }
            }

            if (current.Type.Equals("carpet", StringComparison.OrdinalIgnoreCase) && alignment != "center" &&
                (current.OrientedItems ?? NoOrientedItems).TryGetValue("center", out var center) && center.Count > 0)
                return PickWeighted(center, random);

            if (string.IsNullOrWhiteSpace(current.RedirectBrushName) ||
                !catalog.Brushes.TryGetValue(current.RedirectBrushName, out var redirected))
                break;
            current = redirected;
        }

        return brush.OrientedItems?.Values.FirstOrDefault(items => items.Count > 0) is { } fallback
            ? PickWeighted(fallback, random)
            : (ushort)0;
    }

    private static ushort PickWeighted(IReadOnlyList<RmeWeightedItem> items, Random random)
    {
        var total = items.Sum(item => Math.Max(0, item.Chance));
        if (total <= 0) return items[0].ItemId;
        var chance = random.Next(1, total + 1);
        foreach (var item in items)
        {
            chance -= Math.Max(0, item.Chance);
            if (chance <= 0) return item.ItemId;
        }
        return items[^1].ItemId;
    }

    private static bool HasOrientedItems(RmeBrushDefinition brush, string alignment) =>
        (brush.OrientedItems ?? NoOrientedItems).TryGetValue(alignment, out var items) && items.Count > 0;

    private static bool IsAlreadyAligned(ushort itemId, RmeBrushDefinition brush, string alignment) =>
        ((brush.OrientedItems ?? NoOrientedItems).TryGetValue(alignment, out var items) &&
         items.Any(item => item.ItemId == itemId)) ||
        (brush.Doors ?? []).Any(door => door.ItemId == itemId && door.Alignment == alignment);

    private static string? FindAlignment(RmeBrushDefinition brush, ushort itemId)
    {
        foreach (var (alignment, items) in brush.OrientedItems ?? NoOrientedItems)
            if (items.Any(item => item.ItemId == itemId)) return alignment;
        return (brush.Doors ?? []).FirstOrDefault(door => door.ItemId == itemId)?.Alignment;
    }
}
