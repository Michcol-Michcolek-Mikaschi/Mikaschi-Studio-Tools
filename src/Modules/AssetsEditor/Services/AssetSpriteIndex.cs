using Narzedzia.Core.Assets;

namespace Modules.AssetsEditor.Services;

public sealed record AssetSpriteReference(uint SpriteId, CatalogEntry CatalogEntry);

public sealed class AssetSpriteIndex
{
    private readonly List<SpriteRange> _ranges;

    private AssetSpriteIndex(List<SpriteRange> ranges)
    {
        _ranges = ranges;
        TotalSprites = ranges.Sum(range => range.Count);
    }

    public int TotalSprites { get; }

    public int TotalPages(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        return Math.Max(1, (int)Math.Ceiling(TotalSprites / (double)pageSize));
    }

    public IReadOnlyList<AssetSpriteReference> GetPage(int pageIndex, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        if (TotalSprites == 0)
        {
            return [];
        }

        var startOffset = checked(pageIndex * pageSize);
        if (startOffset >= TotalSprites)
        {
            return [];
        }

        var remainingSkip = startOffset;
        var remainingTake = Math.Min(pageSize, TotalSprites - startOffset);
        var result = new List<AssetSpriteReference>(remainingTake);

        foreach (var range in _ranges)
        {
            if (remainingSkip >= range.Count)
            {
                remainingSkip -= range.Count;
                continue;
            }

            var spriteId = range.FirstSpriteId + (uint)remainingSkip;
            var count = Math.Min(remainingTake, range.Count - remainingSkip);
            for (var i = 0; i < count; i++)
            {
                result.Add(new AssetSpriteReference(spriteId + (uint)i, range.Entry));
            }

            remainingTake -= count;
            remainingSkip = 0;
            if (remainingTake == 0)
            {
                break;
            }
        }

        return result;
    }

    public int FindPageForSprite(uint spriteId, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var offset = 0;
        foreach (var range in _ranges)
        {
            if (range.Contains(spriteId))
            {
                return offset / pageSize;
            }

            offset += range.Count;
        }

        return 0;
    }

    public static AssetSpriteIndex FromCatalog(IEnumerable<CatalogEntry> catalog)
    {
        var ranges = catalog
            .Where(entry =>
                string.Equals(entry.Type, "sprite", StringComparison.OrdinalIgnoreCase) &&
                entry.FirstSpriteid >= 0 &&
                entry.LastSpriteid >= entry.FirstSpriteid)
            .OrderBy(entry => entry.FirstSpriteid)
            .Select(entry => new SpriteRange((uint)entry.FirstSpriteid, (uint)entry.LastSpriteid, entry))
            .ToList();

        return new AssetSpriteIndex(ranges);
    }

    private sealed record SpriteRange(uint FirstSpriteId, uint LastSpriteId, CatalogEntry Entry)
    {
        public int Count => checked((int)(LastSpriteId - FirstSpriteId + 1));

        public bool Contains(uint spriteId) => FirstSpriteId <= spriteId && spriteId <= LastSpriteId;
    }
}
