using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.MapEditor.Services;

/// <summary>
/// Bezpieczna konwersja Server ID pomiędzy wersjami items.otb. Najpierw dopasowuje
/// niezmienny hash sprite'a, potem zgodny Client ID i typ. Brak lub konflikt mapowania
/// blokuje zmianę całej mapy zamiast usuwać albo podmieniać obiekty w ciemno.
/// </summary>
internal sealed class MapVersionConversionService
{
    public MapConversionPlan Analyze(OtbmMap map, string sourceOtbPath, string targetOtbPath)
    {
        var parser = new OtbParser();
        var source = parser.Parse(sourceOtbPath);
        var target = parser.Parse(targetOtbPath);
        var sourceById = source.Items
            .Where(item => item.ItemType != OtbItemType.Deprecated)
            .GroupBy(item => item.ServerId)
            .ToDictionary(group => group.Key, group => group.First());
        var targetItems = target.Items.Where(item => item.ItemType != OtbItemType.Deprecated).ToArray();
        var usedIds = EnumerateUsedIds(map).Where(id => id != 0).Distinct().OrderBy(id => id).ToArray();
        var mappings = new Dictionary<ushort, ushort>();
        var issues = new List<MapConversionIssue>();

        foreach (var sourceId in usedIds)
        {
            if (!sourceById.TryGetValue(sourceId, out var sourceItem))
            {
                issues.Add(new(sourceId, "Brak Server ID w źródłowym items.otb."));
                continue;
            }
            var candidates = FindCandidates(sourceItem, targetItems).ToArray();
            if (candidates.Length == 1)
            {
                mappings[sourceId] = candidates[0].ServerId;
                continue;
            }
            issues.Add(new(sourceId, candidates.Length == 0
                ? $"Brak odpowiednika (Client ID {sourceItem.ClientId}, typ {sourceItem.ItemType})."
                : $"Niejednoznaczne mapowanie: {string.Join(", ", candidates.Select(item => item.ServerId))}."));
        }

        return new MapConversionPlan(
            sourceOtbPath,
            targetOtbPath,
            source.MajorVersion,
            source.MinorVersion,
            target.MajorVersion,
            target.MinorVersion,
            mappings,
            issues,
            usedIds.Length,
            mappings.Count(pair => pair.Key != pair.Value));
    }

    public IReadOnlyList<MapTileChange> Apply(OtbmMap map, MapConversionPlan plan)
    {
        if (!plan.CanApply)
            throw new InvalidOperationException("Plan konwersji zawiera nierozwiązane obiekty.");
        var changes = new List<MapTileChange>();
        foreach (var (coordinate, tile) in map.Tiles)
        {
            var before = MapEditHistory.CloneTile(tile);
            var changed = false;
            if (tile.GroundItemId != 0 && plan.Mappings.TryGetValue(tile.GroundItemId, out var mappedGround) &&
                mappedGround != tile.GroundItemId)
            {
                tile.GroundItemId = mappedGround;
                changed = true;
            }
            foreach (var item in tile.Items.SelectMany(EnumerateItemTree))
            {
                if (!plan.Mappings.TryGetValue(item.Id, out var mappedItem) || mappedItem == item.Id) continue;
                item.Id = mappedItem;
                changed = true;
            }
            if (changed)
                changes.Add(new MapTileChange(coordinate, before, tile));
        }
        map.ItemsMajorVersion = plan.TargetMajorVersion;
        map.ItemsMinorVersion = plan.TargetMinorVersion;
        return changes;
    }

    public static string? FindItemsOtb(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        var direct = Path.Combine(folder, "items.otb");
        if (File.Exists(direct)) return direct;
        return Directory.EnumerateFiles(folder, "items.otb", SearchOption.AllDirectories)
            .OrderBy(path => path.Count(character => character is '\\' or '/'))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static IEnumerable<OtbItem> FindCandidates(OtbItem source, IReadOnlyList<OtbItem> target)
    {
        var hasHash = source.SpriteHash.Any(value => value != 0);
        if (hasHash)
        {
            var byHash = target.Where(item =>
                item.ItemType == source.ItemType && item.SpriteHash.AsSpan().SequenceEqual(source.SpriteHash)).ToArray();
            if (byHash.Length > 0) return byHash;
        }
        var byClient = target.Where(item =>
            item.ClientId == source.ClientId && item.ItemType == source.ItemType).ToArray();
        if (byClient.Length > 0) return byClient;
        var sameServer = target.Where(item =>
            item.ServerId == source.ServerId && item.ItemType == source.ItemType).ToArray();
        return sameServer;
    }

    private static IEnumerable<ushort> EnumerateUsedIds(OtbmMap map)
    {
        foreach (var tile in map.Tiles.Values)
        {
            if (tile.GroundItemId != 0) yield return tile.GroundItemId;
            foreach (var item in tile.Items.SelectMany(EnumerateItemTree)) yield return item.Id;
        }
    }

    private static IEnumerable<OtbmItem> EnumerateItemTree(OtbmItem item)
    {
        yield return item;
        foreach (var child in item.Contents)
        foreach (var nested in EnumerateItemTree(child))
            yield return nested;
    }
}

internal sealed record MapConversionPlan(
    string SourceOtbPath,
    string TargetOtbPath,
    uint SourceMajorVersion,
    uint SourceMinorVersion,
    uint TargetMajorVersion,
    uint TargetMinorVersion,
    IReadOnlyDictionary<ushort, ushort> Mappings,
    IReadOnlyList<MapConversionIssue> Issues,
    int UsedObjectCount,
    int ChangedObjectCount)
{
    public bool CanApply => Issues.Count == 0;
}

internal sealed record MapConversionIssue(ushort ServerId, string Reason);
