using Narzedzia.Core.Models;

namespace Modules.LapisItemEditor.Services;

public sealed class LapisItemSearchService
{
    public IReadOnlyList<OtbItem> Filter(IEnumerable<OtbItem> items, string? query)
    {
        ArgumentNullException.ThrowIfNull(items);

        var normalized = query?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return items.OrderBy(item => item.ServerId).ToList();
        }

        return items
            .Where(item => Matches(item, normalized))
            .OrderBy(item => item.ServerId)
            .ToList();
    }

    private static bool Matches(OtbItem item, string query)
    {
        if (item.ServerId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.ClientId.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.ItemType.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return LapisItemFlagDescriptor.All.Any(descriptor =>
            (item.Flags & (uint)descriptor.Flag) != 0 &&
            (descriptor.Flag.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
             descriptor.Label.Contains(query, StringComparison.OrdinalIgnoreCase)));
    }
}
