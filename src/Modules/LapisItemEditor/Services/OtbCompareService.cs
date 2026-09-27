using Narzedzia.Core.Models;

namespace Modules.LapisItemEditor.Services;

public enum OtbComparisonKind
{
    Added,
    Removed,
    ClientIdChanged,
    NameChanged,
    FlagsChanged,
    TypeChanged
}

public sealed record OtbComparisonEntry(
    OtbComparisonKind Kind,
    ushort ServerId,
    string Label,
    string OldValue,
    string NewValue);

public sealed record OtbComparisonResult(IReadOnlyList<OtbComparisonEntry> Entries)
{
    public int AddedCount => Entries.Count(entry => entry.Kind == OtbComparisonKind.Added);
    public int RemovedCount => Entries.Count(entry => entry.Kind == OtbComparisonKind.Removed);
    public int ChangedCount => Entries.Count - AddedCount - RemovedCount;

    public string Summary =>
        $"Dodane: {AddedCount}, usunięte: {RemovedCount}, zmienione: {ChangedCount}.";
}

public sealed class OtbCompareService
{
    public OtbComparisonResult Compare(OtbFile left, OtbFile right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var result = new List<OtbComparisonEntry>();
        var leftByServerId = left.Items.ToDictionary(item => item.ServerId);
        var rightByServerId = right.Items.ToDictionary(item => item.ServerId);

        foreach (var (serverId, leftItem) in leftByServerId.OrderBy(pair => pair.Key))
        {
            if (!rightByServerId.TryGetValue(serverId, out var rightItem))
            {
                result.Add(new OtbComparisonEntry(
                    OtbComparisonKind.Removed,
                    serverId,
                    Describe(leftItem),
                    leftItem.ClientId.ToString(),
                    string.Empty));
                continue;
            }

            AddChangedEntries(result, leftItem, rightItem);
        }

        foreach (var (serverId, rightItem) in rightByServerId.OrderBy(pair => pair.Key))
        {
            if (!leftByServerId.ContainsKey(serverId))
            {
                result.Add(new OtbComparisonEntry(
                    OtbComparisonKind.Added,
                    serverId,
                    Describe(rightItem),
                    string.Empty,
                    rightItem.ClientId.ToString()));
            }
        }

        return new OtbComparisonResult(result);
    }

    private static void AddChangedEntries(List<OtbComparisonEntry> result, OtbItem left, OtbItem right)
    {
        if (left.ClientId != right.ClientId)
        {
            result.Add(Change(OtbComparisonKind.ClientIdChanged, left, left.ClientId, right.ClientId));
        }

        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal))
        {
            result.Add(Change(OtbComparisonKind.NameChanged, left, left.Name, right.Name));
        }

        if (left.Flags != right.Flags)
        {
            result.Add(Change(OtbComparisonKind.FlagsChanged, left, left.Flags, right.Flags));
        }

        if (left.ItemType != right.ItemType)
        {
            result.Add(Change(OtbComparisonKind.TypeChanged, left, left.ItemType, right.ItemType));
        }
    }

    private static OtbComparisonEntry Change<T>(OtbComparisonKind kind, OtbItem item, T oldValue, T newValue) =>
        new(kind, item.ServerId, Describe(item), oldValue?.ToString() ?? string.Empty, newValue?.ToString() ?? string.Empty);

    private static string Describe(OtbItem item) =>
        string.IsNullOrWhiteSpace(item.Name)
            ? $"#{item.ServerId}"
            : $"#{item.ServerId} - {item.Name}";
}
