using Narzedzia.Core.Models;

namespace Modules.MapEditor.Services;

/// <summary>
/// Odtwarza GroundBrush::doBorders z RME: kolejność sąsiadów, z-order,
/// przyjaźnie/wrogów, bordery opcjonalne i bloki specific.
/// </summary>
public sealed class RmeGroundBorderService(RmeMaterialCatalog catalog)
{
    private static readonly (int X, int Y, int Mask)[] Neighbours =
    [
        (-1, -1, 1), (0, -1, 2), (1, -1, 4), (-1, 0, 8),
        (1, 0, 16), (-1, 1, 32), (0, 1, 64), (1, 1, 128)
    ];

    // Dokładna tablica GroundBrush::border_types[256] z brush_tables.cpp RME.
    // Kolejność jest istotna, bo addBorderItem umieszcza każdy element na dole stosu.
    private static readonly string[] BorderTypes =
    [
        "", "cnw", "n", "n", "cne", "cnw,cne", "n", "n", "w", "w", "dnw", "dnw", "w,cne", "w,cne", "dnw", "dnw",
        "e", "cnw,e", "dne", "dne", "e", "cnw,e", "dne", "dne", "w,e", "w,e", "n,w,e", "n,w,e", "e,w", "e,w", "n,e,w", "n,e,w",
        "csw", "csw,cnw", "csw,n", "csw,n", "csw,cne", "csw,cne,cnw", "csw,n", "csw,n", "w", "w", "dnw", "dnw", "w,cne", "w,cne", "dnw", "dnw",
        "csw,e", "csw,e,cnw", "csw,dne", "csw,dne", "csw,e", "csw,e,cnw", "csw,dne", "csw,dne", "w,e", "w,e", "w,e,n", "w,e,n", "w,e", "w,e", "w,e,n", "w,e,n",
        "s", "s,cnw", "s,n", "s,n", "s,cne", "s,cne,cnw", "s,n", "s,n", "dsw", "dsw", "s,n,w", "s,n,w", "dsw,cne", "dsw,cne", "s,n,w", "s,n,w",
        "dse", "dse,cnw", "s,n,e", "s,n,e", "dse", "dse,cnw", "s,n,e", "s,n,e", "s,w,e", "s,w,e", "s,w,e,n", "s,w,e,n", "s,w,e", "s,w,e", "s,w,e,n", "s,w,e,n",
        "s", "s,cnw", "s,n", "s,n", "s,cne", "s,cnw,cne", "s,n", "s,n", "dsw", "dsw", "s,w,n", "s,w,n", "dsw,cne", "dsw,cne", "s,w,n", "s,w,n",
        "dse", "dse,cnw", "s,e,n", "s,e,n", "dse", "dse,cnw", "s,e,n", "s,e,n", "s,e,w", "s,e,w", "s,e,n,w", "s,e,n,w", "s,e,w", "s,e,w", "s,e,n,w", "s,e,n,w",
        "cse", "cnw,cse", "n,cse", "n,cse", "cne,cse", "cne,cnw,cse", "n,cse", "n,cse", "w,cse", "w,cse", "dnw,cse", "dnw,cse", "w,cne,cse", "w,cne,cse", "dnw,cse", "dnw,cse",
        "e", "e,cnw", "dne", "dne", "e", "e,cnw", "dne", "dne", "e,w", "e,w", "n,e,w", "e,w,n", "e,w", "e,w", "n,e,w", "n,e,w",
        "csw,cse", "csw,cnw,cse", "csw,n,cse", "csw,n,cse", "csw,cne,cse", "csw,cne,cnw,cse", "csw,n,cse", "csw,n,cse", "w,cse", "w,cse", "dnw,cse", "dnw,cse", "w,cne,cse", "w,cne,cse", "dnw,cse", "dnw,cse",
        "csw,e", "csw,e,cnw", "csw,dne", "csw,dne", "csw,e", "csw,e,cnw", "csw,dne", "csw,dne", "w,e", "w,e", "w,e,n", "w,e,n", "w,e", "w,e", "w,e,n", "w,e,n",
        "s", "s,cnw", "s,n", "s,n", "s,cne", "s,cne,cnw", "s,n", "s,n", "dsw", "dsw", "s,n,w", "s,n,w", "dsw,cne", "dsw,cne", "s,n,w", "s,n,w",
        "dse", "dse,cnw", "s,n,e", "s,n,e", "dse", "dse,cnw", "s,n,e", "s,n,e", "s,w,e", "s,w,e", "s,w,e,n", "s,w,e,n", "s,w,e", "s,w,e", "s,w,e,n", "s,w,e,n",
        "s", "s,cnw", "s,n", "s,n", "s,cne", "s,cnw,cne", "s,n", "s,n", "dsw", "dsw", "s,w,n", "s,w,n", "dsw,cne", "dsw,cne", "s,w,n", "s,w,n",
        "dse", "dse,cnw", "s,e,n", "s,e,n", "dse", "dse,cnw", "s,e,n", "s,e,n", "s,e,w", "s,e,w", "s,e,n,w", "s,e,n,w", "s,e,w", "s,e,w", "s,e,n,w", "s,e,n,w"
    ];

    public void Reborder(OtbmMap map, IEnumerable<OtbmTileCoord> coordinates)
    {
        foreach (var coordinate in coordinates.Distinct())
        {
            if (map.Tiles.TryGetValue(coordinate, out var tile))
                ReborderTile(map, tile);
        }
    }

    public void ReborderTile(OtbmMap map, OtbmTile tile, bool? optionalBorderState = null)
    {
        var hasOptionalBorder = optionalBorderState ??
                                tile.Items.Any(item => catalog.OptionalBorderItemIds.Contains(item.Id));
        tile.Items.RemoveAll(item => catalog.BorderItemIds.Contains(item.Id));
        catalog.GroundBrushesByItemId.TryGetValue(tile.GroundItemId, out var centerBrush);

        var neighbourGroups = CollectNeighbourGroups(map, tile);
        var clusters = new List<BorderCluster>();
        var specificRules = new List<RmeBrushBorderRule>();

        foreach (var group in neighbourGroups)
        {
            var other = group.Brush;
            if (centerBrush is not null)
            {
                if (other is not null)
                {
                    if (ReferenceEquals(centerBrush, other)) continue;
                    if (!HasOuterBorder(other) && !HasInnerBorder(centerBrush)) continue;

                    var onlyOptional = false;
                    if (FriendOf(other, centerBrush) || FriendOf(centerBrush, other))
                    {
                        if (ResolveBorder(other.OptionalBorder) is null) continue;
                        onlyOptional = true;
                    }

                    var optional = ResolveBorder(other.OptionalBorder);
                    if (optional is not null && hasOptionalBorder)
                    {
                        AddCluster(clusters, optional, group.Mask, int.MaxValue);
                        if (other.SoloOptionalBorder) onlyOptional = true;
                    }

                    if (!onlyOptional)
                    {
                        var rule = GetBrushTo(centerBrush, other);
                        if (rule is not null)
                        {
                            var border = ResolveBorder(rule);
                            if (border is not null)
                                AddCluster(clusters, border, group.Mask, other.ZOrder);
                            AddSpecificRule(specificRules, rule);
                        }
                    }
                }
                else if (HasInnerZilchBorder(centerBrush))
                {
                    var rule = GetBrushTo(centerBrush, null);
                    if (rule is null) continue;
                    var border = ResolveBorder(rule);
                    if (border is not null)
                        AddCluster(clusters, border, group.Mask, 5000);
                    AddSpecificRule(specificRules, rule);
                }
            }
            else if (other is not null && HasOuterZilchBorder(other))
            {
                var rule = GetBrushTo(null, other);
                if (rule is not null)
                {
                    var border = ResolveBorder(rule);
                    if (border is not null)
                        AddCluster(clusters, border, group.Mask, other.ZOrder);
                    AddSpecificRule(specificRules, rule);
                }

                var optional = ResolveBorder(other.OptionalBorder);
                if (optional is not null && hasOptionalBorder)
                    AddCluster(clusters, optional, group.Mask, int.MaxValue);
                else
                    hasOptionalBorder = false;
            }
        }

        var placed = BuildBorderStack(clusters);
        ApplySpecificCases(placed, specificRules);
        if (placed.Count > 0)
            tile.Items.InsertRange(0, placed.Select(border => border.Item));
    }

    private List<NeighbourGroup> CollectNeighbourGroups(OtbmMap map, OtbmTile tile)
    {
        var result = new List<NeighbourGroup>();
        foreach (var (offsetX, offsetY, mask) in Neighbours)
        {
            var x = tile.X + offsetX;
            var y = tile.Y + offsetY;
            RmeBrushDefinition? brush = null;
            if (x is >= 0 and <= ushort.MaxValue && y is >= 0 and <= ushort.MaxValue &&
                map.Tiles.TryGetValue(new((ushort)x, (ushort)y, tile.Z), out var neighbour))
            {
                catalog.GroundBrushesByItemId.TryGetValue(neighbour.GroundItemId, out brush);
            }

            var existing = result.FirstOrDefault(group => ReferenceEquals(group.Brush, brush));
            if (existing is null)
                result.Add(new NeighbourGroup(brush, mask));
            else
                existing.Mask |= mask;
        }
        return result;
    }

    private RmeBrushBorderRule? GetBrushTo(RmeBrushDefinition? first, RmeBrushDefinition? second)
    {
        if (first is not null)
        {
            if (second is not null)
            {
                if (first.ZOrder < second.ZOrder && HasOuterBorder(second))
                {
                    if (HasInnerBorder(first))
                    {
                        var inner = FindRule(first, outer: false, second.Name);
                        if (inner is not null) return inner;
                    }
                    return FindRule(second, outer: true, first.Name);
                }

                if (HasInnerBorder(first))
                    return FindRule(first, outer: false, second.Name);
            }
            else if (HasInnerZilchBorder(first))
            {
                return FindRule(first, outer: false, null);
            }
        }
        else if (second is not null && HasOuterZilchBorder(second))
        {
            return FindRule(second, outer: true, null);
        }
        return null;
    }

    private static RmeBrushBorderRule? FindRule(RmeBrushDefinition brush, bool outer, string? otherName)
    {
        foreach (var rule in brush.BorderRules ?? [])
        {
            if (IsOuter(rule) != outer) continue;
            if (otherName is null)
            {
                if (rule.TargetBrush?.Equals("none", StringComparison.OrdinalIgnoreCase) == true)
                    return rule;
                continue;
            }
            if (string.IsNullOrWhiteSpace(rule.TargetBrush) ||
                rule.TargetBrush.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                rule.TargetBrush.Equals(otherName, StringComparison.OrdinalIgnoreCase))
                return rule;
        }
        return null;
    }

    private static bool IsOuter(RmeBrushBorderRule rule) =>
        !rule.Alignment.Equals("inner", StringComparison.OrdinalIgnoreCase);

    private static bool HasInnerBorder(RmeBrushDefinition brush) =>
        (brush.BorderRules ?? []).Any(rule => !IsOuter(rule) &&
            rule.TargetBrush?.Equals("none", StringComparison.OrdinalIgnoreCase) != true);

    private static bool HasInnerZilchBorder(RmeBrushDefinition brush) =>
        (brush.BorderRules ?? []).Any(rule => !IsOuter(rule) &&
            rule.TargetBrush?.Equals("none", StringComparison.OrdinalIgnoreCase) == true);

    private bool HasOuterBorder(RmeBrushDefinition brush) =>
        ResolveBorder(brush.OptionalBorder) is not null ||
        (brush.BorderRules ?? []).Any(rule => IsOuter(rule) &&
            rule.TargetBrush?.Equals("none", StringComparison.OrdinalIgnoreCase) != true);

    private bool HasOuterZilchBorder(RmeBrushDefinition brush) =>
        ResolveBorder(brush.OptionalBorder) is not null ||
        (brush.BorderRules ?? []).Any(rule => IsOuter(rule) &&
            rule.TargetBrush?.Equals("none", StringComparison.OrdinalIgnoreCase) == true);

    private static bool FriendOf(RmeBrushDefinition first, RmeBrushDefinition second)
    {
        var listed = (first.Friends ?? []).Any(name =>
            name.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(second.Name, StringComparison.OrdinalIgnoreCase));
        return listed ? !first.HateFriends : first.HateFriends;
    }

    private RmeBorderDefinition? ResolveBorder(RmeBrushBorderRule rule) =>
        rule.InlineBorder ?? catalog.Borders.GetValueOrDefault(rule.BorderId);

    private RmeBorderDefinition? ResolveBorder(RmeBorderReference? reference) =>
        reference?.InlineBorder ?? (reference is null ? null : catalog.Borders.GetValueOrDefault(reference.BorderId));

    private static void AddCluster(List<BorderCluster> clusters, RmeBorderDefinition border, int mask, int zOrder)
    {
        var existing = clusters.FirstOrDefault(cluster => ReferenceEquals(cluster.Border, border));
        if (existing is null)
            clusters.Add(new BorderCluster(border, mask, zOrder));
        else
        {
            existing.Mask |= mask;
            existing.ZOrder = Math.Max(existing.ZOrder, zOrder);
        }
    }

    private static void AddSpecificRule(List<RmeBrushBorderRule> rules, RmeBrushBorderRule rule)
    {
        if (rule.SpecificCases.Count > 0 && !rules.Any(existing => ReferenceEquals(existing, rule)))
            rules.Add(rule);
    }

    private static List<PlacedBorder> BuildBorderStack(IEnumerable<BorderCluster> clusters)
    {
        var result = new List<PlacedBorder>();
        foreach (var cluster in clusters.OrderByDescending(cluster => cluster.ZOrder))
        {
            foreach (var edge in ResolveEdges(cluster.Mask))
            {
                if (cluster.Border.Items.TryGetValue(edge, out var itemId))
                {
                    result.Insert(0, new PlacedBorder(new OtbmItem { Id = itemId }, cluster.Border, edge));
                    continue;
                }

                foreach (var fallback in DiagonalFallback(edge))
                {
                    if (cluster.Border.Items.TryGetValue(fallback, out itemId))
                        result.Insert(0, new PlacedBorder(new OtbmItem { Id = itemId }, cluster.Border, fallback));
                }
            }
        }
        return result;
    }

    private void ApplySpecificCases(List<PlacedBorder> borders, IEnumerable<RmeBrushBorderRule> rules)
    {
        foreach (var rule in rules)
        foreach (var specific in rule.SpecificCases)
        {
            var matchIds = specific.Matches.Select(ResolveMatchId).ToArray();
            var matches = 0;
            foreach (var border in borders)
            {
                if (specific.MatchGroup > 0 && border.Definition?.Group == specific.MatchGroup &&
                    border.Edge?.Equals(specific.GroupAlignment, StringComparison.OrdinalIgnoreCase) == true)
                {
                    matches++;
                    continue;
                }

                foreach (var matchId in matchIds)
                    if (border.Item.Id == matchId)
                        matches++;
            }

            if (matches < specific.Matches.Count) continue;

            var actions = specific.Actions;
            var toReplace = actions.DirectReplaceItemId;
            if (toReplace == 0 && actions.ReplaceBorderId != 0 && actions.ReplaceBorderEdge is not null &&
                catalog.Borders.TryGetValue(actions.ReplaceBorderId, out var replaceBorder))
                replaceBorder.Items.TryGetValue(actions.ReplaceBorderEdge, out toReplace);

            var replaced = actions.DeleteBorders;
            for (var index = 0; index < borders.Count;)
            {
                var border = borders[index];
                if (!matchIds.Any(matchId => matchId == border.Item.Id))
                {
                    index++;
                    continue;
                }

                if (!replaced && border.Item.Id == toReplace && actions.WithItemId != 0)
                {
                    border.Item.Id = actions.WithItemId;
                    border.Definition = null;
                    border.Edge = null;
                    replaced = true;
                    index++;
                }
                else if (actions.DeleteBorders || !specific.KeepBorder)
                {
                    borders.RemoveAt(index);
                }
                else
                {
                    index++;
                }
            }
        }
    }

    private ushort ResolveMatchId(RmeBorderMatch match)
    {
        if (match.Kind == RmeBorderMatchKind.Item || match.Kind == RmeBorderMatchKind.Group)
            return match.Value is > 0 and <= ushort.MaxValue ? (ushort)match.Value : (ushort)0;
        return match.Edge is not null && catalog.Borders.TryGetValue(match.Value, out var border) &&
               border.Items.TryGetValue(match.Edge, out var itemId)
            ? itemId
            : (ushort)0;
    }

    internal static IReadOnlyList<string> ResolveEdges(int mask)
    {
        if (mask is < 0 or > 255) return [];
        var encoded = BorderTypes[mask];
        return encoded.Length == 0 ? [] : encoded.Split(',');
    }

    private static IEnumerable<string> DiagonalFallback(string edge) => edge switch
    {
        "dnw" => ["w", "n"],
        "dne" => ["e", "n"],
        "dsw" => ["s", "w"],
        "dse" => ["s", "e"],
        _ => []
    };

    private sealed class NeighbourGroup(RmeBrushDefinition? brush, int mask)
    {
        public RmeBrushDefinition? Brush { get; } = brush;
        public int Mask { get; set; } = mask;
    }

    private sealed class BorderCluster(RmeBorderDefinition border, int mask, int zOrder)
    {
        public RmeBorderDefinition Border { get; } = border;
        public int Mask { get; set; } = mask;
        public int ZOrder { get; set; } = zOrder;
    }

    private sealed class PlacedBorder(OtbmItem item, RmeBorderDefinition? definition, string? edge)
    {
        public OtbmItem Item { get; } = item;
        public RmeBorderDefinition? Definition { get; set; } = definition;
        public string? Edge { get; set; } = edge;
    }
}
