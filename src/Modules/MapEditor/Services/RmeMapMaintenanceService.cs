using Narzedzia.Core.Models;

namespace Modules.MapEditor.Services;

/// <summary>
/// Operacje konserwacyjne odpowiadające menu Edit/Other Options w RME.
/// Wyszukiwanie nieosiągalnych pól korzysta z indeksu przestrzennego, dzięki czemu
/// nie skanuje całej mapy dla każdego kafelka.
/// </summary>
public static class RmeMapMaintenanceService
{
    private const int BucketSize = 16;

    public static IReadOnlyList<OtbmTileCoord> FindUnreachableTiles(
        OtbmMap map,
        Func<OtbmTile, bool> isBlocking)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(isBlocking);

        var reachable = map.Tiles
            .Where(pair => !isBlocking(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();
        var buckets = reachable
            .GroupBy(position => (position.Z, X: position.X / BucketSize, Y: position.Y / BucketSize))
            .ToDictionary(group => group.Key, group => group.ToArray());

        var result = new List<OtbmTileCoord>();
        foreach (var (position, tile) in map.Tiles)
        {
            if (!isBlocking(tile)) continue;
            var minX = Math.Max(0, position.X - 10);
            var maxX = Math.Min(ushort.MaxValue, position.X + 10);
            var minY = Math.Max(0, position.Y - 8);
            var maxY = Math.Min(ushort.MaxValue, position.Y + 8);
            var minZ = position.Z <= 7 ? 0 : Math.Max(7, position.Z - 2);
            var maxZ = position.Z <= 7 ? 9 : Math.Min(15, position.Z + 2);
            if (!HasReachableTile(buckets, minX, maxX, minY, maxY, minZ, maxZ))
                result.Add(position);
        }
        return result;
    }

    public static int RemoveSimpleItems(List<OtbmItem> items, IReadOnlySet<ushort> ids)
    {
        var removed = 0;
        for (var index = items.Count - 1; index >= 0; index--)
        {
            var item = items[index];
            if (!ids.Contains(item.Id) || IsComplex(item)) continue;
            items.RemoveAt(index);
            removed++;
        }
        return removed;
    }

    private static bool HasReachableTile(
        IReadOnlyDictionary<(byte Z, int X, int Y), OtbmTileCoord[]> buckets,
        int minX, int maxX, int minY, int maxY, int minZ, int maxZ)
    {
        for (var z = minZ; z <= maxZ; z++)
        for (var bucketY = minY / BucketSize; bucketY <= maxY / BucketSize; bucketY++)
        for (var bucketX = minX / BucketSize; bucketX <= maxX / BucketSize; bucketX++)
        {
            if (!buckets.TryGetValue(((byte)z, bucketX, bucketY), out var positions)) continue;
            if (positions.Any(position =>
                    position.X >= minX && position.X <= maxX &&
                    position.Y >= minY && position.Y <= maxY))
                return true;
        }
        return false;
    }

    private static bool IsComplex(OtbmItem item) =>
        item.Contents.Count > 0 || item.ActionId.HasValue || item.UniqueId.HasValue || item.Count.HasValue ||
        item.Text is not null || item.Description is not null || item.DepotId.HasValue ||
        item.HouseDoorId.HasValue || item.RuneCharges.HasValue || item.Duration.HasValue ||
        item.DecayingState.HasValue || item.WrittenDate.HasValue || item.WrittenBy is not null ||
        item.SleeperGuid.HasValue || item.SleepStart.HasValue || item.Charges.HasValue ||
        item.Tier.HasValue || item.PodiumOutfit is not null || item.TeleportX.HasValue ||
        item.TeleportY.HasValue || item.TeleportZ.HasValue || item.CustomAttributes.Count > 0 ||
        item.RawAttributeData.Length > 0;
}
