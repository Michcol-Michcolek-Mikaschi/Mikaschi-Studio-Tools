using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Modules.MapEditor.Services;

/// <summary>
/// Loads the version-specific material database used by Remere's Map Editor.
/// The loader intentionally follows materials.xml includes and preserves the
/// tileset/category/brush order because that order is part of the RME workflow.
/// </summary>
public sealed class RmeMaterialCatalogService
{
    private static readonly Regex BareAmpersand = new(
        "&(?!#\\d+;|#x[0-9a-fA-F]+;|[A-Za-z_:][A-Za-z0-9_.:-]*;)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public RmeMaterialCatalog Load(string itemsOtbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemsOtbPath);
        var versionDirectory = Path.GetDirectoryName(Path.GetFullPath(itemsOtbPath))
                               ?? throw new InvalidOperationException("Nie można ustalić katalogu danych RME.");
        var entryPoint = Path.Combine(versionDirectory, "materials.xml");
        if (!File.Exists(entryPoint))
            throw new FileNotFoundException("Przy items.otb nie znaleziono materials.xml RME.", entryPoint);

        var brushes = new Dictionary<string, RmeBrushDefinition>(StringComparer.OrdinalIgnoreCase);
        var borders = new Dictionary<int, RmeBorderDefinition>();
        var tilesets = new List<RmeTilesetDefinition>();
        var warnings = new List<string>();
        var loadedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        LoadFile(entryPoint, versionDirectory, loadedFiles, brushes, borders, tilesets, warnings);
        ResolveTilesetEntries(tilesets, brushes, warnings);

        var groundBrushesByItemId = new Dictionary<ushort, RmeBrushDefinition>();
        var itemBrushesByItemId = new Dictionary<ushort, RmeBrushDefinition>();
        foreach (var brush in brushes.Values.Where(brush =>
                     brush.Type.Equals("ground", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var itemId in brush.Alternatives.SelectMany(alternative => alternative.SingleItems)
                         .Select(item => item.ItemId))
                groundBrushesByItemId[itemId] = brush;
            if (brush.ServerLookId is > 0 and <= ushort.MaxValue)
                groundBrushesByItemId[(ushort)brush.ServerLookId] = brush;
        }
        foreach (var brush in brushes.Values)
        foreach (var itemId in brush.AllItemIds)
            itemBrushesByItemId[itemId] = brush;
        var inlineBorders = brushes.Values
            .SelectMany(brush => (brush.BorderRules ?? []).Select(rule => rule.InlineBorder)
                .Concat([brush.OptionalBorder?.InlineBorder]))
            .Where(border => border is not null)
            .Cast<RmeBorderDefinition>()
            .ToArray();
        var allBorders = borders.Values.Concat(inlineBorders).Distinct().ToArray();
        var borderItemIds = allBorders.SelectMany(border => border.Items.Values)
            .Concat(brushes.Values.SelectMany(brush => brush.BorderRules ?? [])
                .SelectMany(rule => rule.SpecificCases)
                .Select(specific => specific.Actions.WithItemId)
                .Where(id => id > 0))
            .ToHashSet();
        var optionalBorderItemIds = allBorders.Where(border => border.IsOptional)
            .SelectMany(border => border.Items.Values)
            .ToHashSet();

        return new RmeMaterialCatalog(
            versionDirectory,
            tilesets,
            brushes,
            borders,
            groundBrushesByItemId,
            itemBrushesByItemId,
            borderItemIds,
            optionalBorderItemIds,
            loadedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings);
    }

    private static void LoadFile(
        string filePath,
        string versionDirectory,
        HashSet<string> loadedFiles,
        Dictionary<string, RmeBrushDefinition> brushes,
        Dictionary<int, RmeBorderDefinition> borders,
        List<RmeTilesetDefinition> tilesets,
        List<string> warnings)
    {
        var fullPath = Path.GetFullPath(filePath);
        var versionRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(versionDirectory)) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(versionRoot, StringComparison.OrdinalIgnoreCase) || !loadedFiles.Add(fullPath))
            return;

        XDocument document;
        try
        {
            // Several official legacy RME packs contain a trailing NUL byte or
            // literal '&' in a brush name. PugiXML accepted these packs; strict
            // System.Xml does not, so normalize only those two known defects.
            var source = File.ReadAllText(fullPath).TrimEnd('\0');
            source = BareAmpersand.Replace(source, "&amp;");
            document = XDocument.Parse(source, LoadOptions.SetLineInfo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            warnings.Add($"Nie można wczytać {Path.GetFileName(fullPath)}: {ex.Message}");
            return;
        }

        var root = document.Root;
        if (root is null || !root.Name.LocalName.Equals("materials", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Plik {Path.GetFileName(fullPath)} nie ma elementu głównego materials.");
            return;
        }

        foreach (var node in root.Elements())
        {
            switch (node.Name.LocalName.ToLowerInvariant())
            {
                case "include":
                    var includeName = Attribute(node, "file");
                    if (string.IsNullOrWhiteSpace(includeName))
                    {
                        warnings.Add($"Pominięto include bez nazwy w {Path.GetFileName(fullPath)}.");
                        break;
                    }
                    LoadFile(Path.Combine(versionDirectory, includeName), versionDirectory, loadedFiles,
                        brushes, borders, tilesets, warnings);
                    break;
                case "brush":
                    AddBrush(node, brushes, warnings);
                    break;
                case "border":
                    AddBorder(node, borders, warnings);
                    break;
                case "tileset":
                    AddTileset(node, tilesets, warnings);
                    break;
            }
        }
    }

    private static void AddBrush(
        XElement node,
        Dictionary<string, RmeBrushDefinition> brushes,
        List<string> warnings)
    {
        var name = Attribute(node, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            warnings.Add("Pominięto brush bez nazwy.");
            return;
        }

        var type = Attribute(node, "type")?.Trim().ToLowerInvariant() ?? "raw";
        var lookId = UIntAttribute(node, "server_lookid") ?? UIntAttribute(node, "lookid") ?? 0;
        var (thickness, thicknessCeiling) = ParseThickness(Attribute(node, "thickness"));
        var zOrder = IntAttribute(node, "z-order") ?? 0;
        var borderRules = node.Elements().Where(element =>
                element.Name.LocalName.Equals("border", StringComparison.OrdinalIgnoreCase))
            .Select(ParseBrushBorderRule)
            .ToArray();
        var relations = node.Elements().Where(element =>
                element.Name.LocalName.Equals("friend", StringComparison.OrdinalIgnoreCase) ||
                element.Name.LocalName.Equals("enemy", StringComparison.OrdinalIgnoreCase))
            .Select(element => new
            {
                Name = Attribute(element, "name"),
                Enemy = element.Name.LocalName.Equals("enemy", StringComparison.OrdinalIgnoreCase)
            })
            .Where(relation => !string.IsNullOrWhiteSpace(relation.Name))
            .ToArray();
        var friends = relations.Select(relation => relation.Name!).ToArray();
        var hateFriends = relations.LastOrDefault()?.Enemy ?? false;
        var redirectBrushName = node.Elements().Where(element =>
                element.Name.LocalName.Equals("friend", StringComparison.OrdinalIgnoreCase) &&
                BoolAttribute(element, "redirect"))
            .Select(element => Attribute(element, "name"))
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        var optionalNode = node.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("optional", StringComparison.OrdinalIgnoreCase));
        var optionalBorder = optionalNode is null ? null : ParseBorderReference(optionalNode, forceOptional: true);
        var orientedItems = ParseOrientedItems(node);
        var doors = ParseDoors(node);
        var alternatives = new List<RmeBrushAlternative>();

        foreach (var alternate in node.Elements().Where(element =>
                     element.Name.LocalName.Equals("alternate", StringComparison.OrdinalIgnoreCase)))
        {
            alternatives.Add(ParseAlternative(alternate));
        }

        var directAlternative = ParseAlternative(node, directChildrenOnly: true);
        if (!directAlternative.IsEmpty)
        {
            if (alternatives.Count == 0)
                alternatives.Add(directAlternative);
            else
                alternatives[^1] = alternatives[^1].Merge(directAlternative);
        }

        // Walls, tables and carpets use orientation-specific nodes. They still
        // belong in the exact RME palettes; keep all possible item IDs so the
        // editor can show and place a safe representative before automagic runs.
        if (alternatives.Count == 0)
        {
            var flattenedItems = orientedItems.Values.SelectMany(items => items).ToArray();
            if (flattenedItems.Length > 0)
                alternatives.Add(new RmeBrushAlternative(flattenedItems, []));
        }

        var definition = new RmeBrushDefinition(
            name,
            type,
            lookId,
            BoolAttribute(node, "one_size"),
            BoolAttribute(node, "redo_borders") || BoolAttribute(node, "reborder"),
            thickness,
            thicknessCeiling,
            alternatives,
            zOrder,
            borderRules,
            friends,
            hateFriends,
            optionalBorder,
            BoolAttribute(node, "solo_optional"),
            orientedItems,
            doors,
            redirectBrushName,
            BoolAttribute(node, "on_blocking"),
            BoolAttribute(node, "on_duplicate"),
            BoolAttribute(node, "draggable"));

        // Some RME data files contain a forward declaration followed by the
        // complete brush. The complete definition must win.
        if (!brushes.TryGetValue(name, out var existing) ||
            definition.Alternatives.Sum(alternative => alternative.TotalChance) >=
            existing.Alternatives.Sum(alternative => alternative.TotalChance))
        {
            brushes[name] = definition;
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<RmeWeightedItem>> ParseOrientedItems(XElement node)
    {
        var result = new Dictionary<string, IReadOnlyList<RmeWeightedItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var oriented in node.Elements().Where(element =>
                     element.Name.LocalName is "wall" or "table" or "carpet"))
        {
            var alignment = Attribute(oriented,
                oriented.Name.LocalName.Equals("wall", StringComparison.OrdinalIgnoreCase) ? "type" : "align");
            if (string.IsNullOrWhiteSpace(alignment)) continue;

            var items = oriented.Elements().Where(element =>
                    element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
                .Select(ParseSingle)
                .Where(item => item is not null)
                .Cast<RmeWeightedItem>()
                .ToList();
            if (items.Count == 0 && UIntAttribute(oriented, "id") is > 0 and <= ushort.MaxValue and var directId)
                items.Add(new RmeWeightedItem((ushort)directId, 1));
            if (items.Count > 0)
                result[NormalizeAlignment(alignment)] = items;
        }
        return result;
    }

    private static IReadOnlyList<RmeDoorDefinition> ParseDoors(XElement node)
    {
        var result = new List<RmeDoorDefinition>();
        foreach (var wall in node.Elements().Where(element =>
                     element.Name.LocalName.Equals("wall", StringComparison.OrdinalIgnoreCase)))
        {
            var alignment = NormalizeAlignment(Attribute(wall, "type") ?? string.Empty);
            foreach (var door in wall.Elements().Where(element =>
                         element.Name.LocalName.Equals("door", StringComparison.OrdinalIgnoreCase)))
            {
                if (UIntAttribute(door, "id") is not (> 0 and <= ushort.MaxValue) ||
                    Attribute(door, "type") is not { } type)
                    continue;
                result.Add(new RmeDoorDefinition(
                    (ushort)UIntAttribute(door, "id")!.Value,
                    alignment,
                    NormalizeAlignment(type),
                    BoolAttribute(door, "open") || Attribute(door, "open") is null,
                    BoolAttribute(door, "locked"),
                    BoolAttribute(door, "hate")));
            }
        }
        return result;
    }

    private static string NormalizeAlignment(string value)
    {
        var normalized = string.Join(' ', value.Trim().ToLowerInvariant()
            .Replace('_', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        // RME maps the legacy XML alias "corner" to WALL_NORTHWEST_DIAGONAL.
        return normalized == "corner" ? "northwest diagonal" : normalized;
    }

    private static RmeBrushBorderRule ParseBrushBorderRule(XElement node)
    {
        var reference = ParseBorderReference(node, forceOptional: false);
        var specifics = node.Elements().Where(element =>
                element.Name.LocalName.Equals("specific", StringComparison.OrdinalIgnoreCase))
            .Select(ParseSpecificCase)
            .Where(specific => specific is not null)
            .Cast<RmeBorderSpecificCase>()
            .ToArray();
        return new RmeBrushBorderRule(
            Attribute(node, "align") ?? "outer",
            Attribute(node, "to"),
            reference.BorderId,
            reference.InlineBorder,
            BoolAttribute(node, "super"),
            specifics);
    }

    private static RmeBorderReference ParseBorderReference(XElement node, bool forceOptional)
    {
        var id = IntAttribute(node, "id");
        if (id is not null)
            return new RmeBorderReference(id.Value, null);

        var equivalent = UIntAttribute(node, "ground_equivalent");
        if (equivalent is not > 0 or > ushort.MaxValue)
            return new RmeBorderReference(0, null);

        var items = ParseBorderItems(node);
        var inline = new RmeBorderDefinition(
            0,
            IntAttribute(node, "group"),
            items,
            forceOptional || string.Equals(Attribute(node, "type"), "optional", StringComparison.OrdinalIgnoreCase),
            (ushort)equivalent.Value);
        return new RmeBorderReference(0, inline);
    }

    private static RmeBorderSpecificCase? ParseSpecificCase(XElement node)
    {
        var matches = new List<RmeBorderMatch>();
        var matchGroup = 0;
        string? groupAlignment = null;
        foreach (var condition in node.Elements().Where(element =>
                     element.Name.LocalName.Equals("conditions", StringComparison.OrdinalIgnoreCase))
                 .SelectMany(element => element.Elements()))
        {
            switch (condition.Name.LocalName.ToLowerInvariant())
            {
                case "match_border":
                    if (IntAttribute(condition, "id") is { } borderId &&
                        Attribute(condition, "edge") is { } borderEdge)
                        matches.Add(RmeBorderMatch.ForBorder(borderId, borderEdge));
                    break;
                case "match_group":
                    if (IntAttribute(condition, "group") is { } group &&
                        Attribute(condition, "edge") is { } groupEdge)
                    {
                        matchGroup = group;
                        groupAlignment = groupEdge;
                        matches.Add(RmeBorderMatch.ForGroup(group));
                    }
                    break;
                case "match_item":
                    if (UIntAttribute(condition, "id") is > 0 and <= ushort.MaxValue and var itemId)
                        matches.Add(RmeBorderMatch.ForItem((ushort)itemId));
                    break;
            }
        }

        ushort directReplaceId = 0;
        int borderReplaceId = 0;
        string? borderReplaceEdge = null;
        ushort withId = 0;
        var deleteBorders = false;
        foreach (var action in node.Elements().Where(element =>
                     element.Name.LocalName.Equals("actions", StringComparison.OrdinalIgnoreCase))
                 .SelectMany(element => element.Elements()))
        {
            switch (action.Name.LocalName.ToLowerInvariant())
            {
                case "replace_border":
                    borderReplaceId = IntAttribute(action, "id") ?? 0;
                    borderReplaceEdge = Attribute(action, "edge");
                    directReplaceId = 0;
                    if (UIntAttribute(action, "with") is > 0 and <= ushort.MaxValue and var borderWith)
                        withId = (ushort)borderWith;
                    break;
                case "replace_item":
                    if (UIntAttribute(action, "id") is > 0 and <= ushort.MaxValue and var replaceId)
                        directReplaceId = (ushort)replaceId;
                    borderReplaceId = 0;
                    borderReplaceEdge = null;
                    if (UIntAttribute(action, "with") is > 0 and <= ushort.MaxValue and var itemWith)
                        withId = (ushort)itemWith;
                    break;
                case "delete_borders":
                    deleteBorders = true;
                    break;
            }
        }

        if (matches.Count == 0) return null;
        return new RmeBorderSpecificCase(
            matches,
            matchGroup,
            groupAlignment,
            new RmeBorderActions(deleteBorders, directReplaceId, borderReplaceId, borderReplaceEdge, withId),
            BoolAttribute(node, "keep_border"));
    }

    private static RmeBrushAlternative ParseAlternative(XElement node, bool directChildrenOnly = false)
    {
        var singles = new List<RmeWeightedItem>();
        var composites = new List<RmeWeightedComposite>();
        var children = directChildrenOnly ? node.Elements() : node.Elements();
        foreach (var child in children)
        {
            var childName = child.Name.LocalName.ToLowerInvariant();
            if (childName == "item")
            {
                var single = ParseSingle(child);
                if (single is not null) singles.Add(single);
            }
            else if (childName == "composite")
            {
                var tiles = new List<RmeCompositeTile>();
                foreach (var tileNode in child.Elements().Where(element =>
                             element.Name.LocalName.Equals("tile", StringComparison.OrdinalIgnoreCase)))
                {
                    var x = IntAttribute(tileNode, "x") ?? 0;
                    var y = IntAttribute(tileNode, "y") ?? 0;
                    var z = IntAttribute(tileNode, "z") ?? 0;
                    var itemIds = tileNode.Elements()
                        .Where(element => element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
                        .Select(element => UIntAttribute(element, "id") ?? 0)
                        .Where(id => id > 0 && id <= ushort.MaxValue)
                        .Select(id => (ushort)id)
                        .ToArray();
                    if (itemIds.Length > 0)
                        tiles.Add(new RmeCompositeTile(x, y, z, itemIds));
                }
                if (tiles.Count > 0)
                    composites.Add(new RmeWeightedComposite(Math.Max(0, IntAttribute(child, "chance") ?? 0), tiles));
            }
        }
        return new RmeBrushAlternative(singles, composites);
    }

    private static RmeWeightedItem? ParseSingle(XElement node)
    {
        var id = UIntAttribute(node, "id");
        return id is > 0 and <= ushort.MaxValue
            ? new RmeWeightedItem((ushort)id.Value, Math.Max(0, IntAttribute(node, "chance") ?? 1))
            : null;
    }

    private static void AddBorder(XElement node, Dictionary<int, RmeBorderDefinition> borders, List<string> warnings)
    {
        var id = IntAttribute(node, "id");
        if (id is null)
        {
            warnings.Add("Pominięto border bez ID.");
            return;
        }

        borders[id.Value] = new RmeBorderDefinition(
            id.Value,
            IntAttribute(node, "group"),
            ParseBorderItems(node),
            string.Equals(Attribute(node, "type"), "optional", StringComparison.OrdinalIgnoreCase),
            null);
    }

    private static IReadOnlyDictionary<string, ushort> ParseBorderItems(XElement node)
    {
        var items = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in node.Elements().Where(element =>
                     element.Name.LocalName.Equals("borderitem", StringComparison.OrdinalIgnoreCase)))
        {
            var edge = Attribute(item, "edge");
            var itemId = UIntAttribute(item, "item");
            if (!string.IsNullOrWhiteSpace(edge) && itemId is > 0 and <= ushort.MaxValue)
                items[edge] = (ushort)itemId.Value;
        }
        return items;
    }

    private static void AddTileset(XElement node, List<RmeTilesetDefinition> tilesets, List<string> warnings)
    {
        var name = Attribute(node, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            warnings.Add("Pominięto tileset bez nazwy.");
            return;
        }

        var categories = Enum.GetValues<RmePaletteCategory>()
            .ToDictionary(category => category, _ => new List<RmePaletteEntry>());
        foreach (var categoryNode in node.Elements())
        {
            foreach (var category in MapCategories(categoryNode.Name.LocalName))
            {
                foreach (var entryNode in categoryNode.Elements())
                {
                    if (entryNode.Name.LocalName.Equals("brush", StringComparison.OrdinalIgnoreCase))
                    {
                        var brushName = Attribute(entryNode, "name");
                        if (!string.IsNullOrWhiteSpace(brushName))
                            categories[category].Add(RmePaletteEntry.ForBrush(brushName));
                    }
                    else if (entryNode.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
                    {
                        var from = UIntAttribute(entryNode, "id") ?? UIntAttribute(entryNode, "fromid");
                        var to = UIntAttribute(entryNode, "toid") ?? from;
                        if (from is null || to is null || from == 0 || from > ushort.MaxValue) continue;
                        var upper = Math.Min(to.Value, ushort.MaxValue);
                        for (var id = from.Value; id <= upper; id++)
                            categories[category].Add(RmePaletteEntry.ForItem((ushort)id));
                    }
                    else if (entryNode.Name.LocalName.Equals("creature", StringComparison.OrdinalIgnoreCase))
                    {
                        var creatureName = Attribute(entryNode, "name");
                        if (!string.IsNullOrWhiteSpace(creatureName))
                            categories[category].Add(RmePaletteEntry.ForNamedEntity(creatureName));
                    }
                }
            }
        }
        tilesets.Add(new RmeTilesetDefinition(name, categories.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<RmePaletteEntry>)pair.Value)));
    }

    private static IEnumerable<RmePaletteCategory> MapCategories(string elementName)
    {
        switch (elementName.ToLowerInvariant())
        {
            case "terrain": yield return RmePaletteCategory.Terrain; break;
            case "doodad": yield return RmePaletteCategory.Doodad; break;
            case "items": yield return RmePaletteCategory.Item; break;
            case "raw": yield return RmePaletteCategory.Raw; break;
            case "collections": yield return RmePaletteCategory.Collection; break;
            case "creatures": yield return RmePaletteCategory.Creature; break;
            case "house": yield return RmePaletteCategory.House; break;
            case "waypoint": yield return RmePaletteCategory.Waypoint; break;
            case "terrain_and_raw":
                yield return RmePaletteCategory.Terrain;
                yield return RmePaletteCategory.Raw;
                break;
            case "collections_and_raw":
                yield return RmePaletteCategory.Collection;
                yield return RmePaletteCategory.Raw;
                break;
            case "collections_and_terrain":
                yield return RmePaletteCategory.Collection;
                yield return RmePaletteCategory.Terrain;
                break;
            case "doodad_and_raw":
                yield return RmePaletteCategory.Doodad;
                yield return RmePaletteCategory.Raw;
                break;
            case "items_and_raw":
                yield return RmePaletteCategory.Item;
                yield return RmePaletteCategory.Raw;
                break;
        }
    }

    private static void ResolveTilesetEntries(
        IEnumerable<RmeTilesetDefinition> tilesets,
        IReadOnlyDictionary<string, RmeBrushDefinition> brushes,
        List<string> warnings)
    {
        var missingBrushes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in tilesets.SelectMany(tileset => tileset.Categories.Values).SelectMany(entries => entries))
        {
            if (entry.BrushName is null) continue;
            if (brushes.TryGetValue(entry.BrushName, out var brush))
                entry.Brush = brush;
            else
                missingBrushes.Add(entry.BrushName);
        }
        if (missingBrushes.Count > 0)
            warnings.Add($"Brak {missingBrushes.Count} definicji brush wskazanych przez tilesets.xml.");
    }

    private static (int Value, int Ceiling) ParseThickness(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (0, 0);
        var parts = value.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var thickness))
            return (0, 0);
        var ceiling = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var parsedCeiling) ? parsedCeiling : thickness;
        return (Math.Max(0, thickness), Math.Max(thickness, ceiling));
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static uint? UIntAttribute(XElement element, string name) =>
        uint.TryParse(Attribute(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int? IntAttribute(XElement element, string name) =>
        int.TryParse(Attribute(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static bool BoolAttribute(XElement element, string name) =>
        bool.TryParse(Attribute(element, name), out var value) && value;
}

public sealed record RmeMaterialCatalog(
    string VersionDirectory,
    IReadOnlyList<RmeTilesetDefinition> Tilesets,
    IReadOnlyDictionary<string, RmeBrushDefinition> Brushes,
    IReadOnlyDictionary<int, RmeBorderDefinition> Borders,
    IReadOnlyDictionary<ushort, RmeBrushDefinition> GroundBrushesByItemId,
    IReadOnlyDictionary<ushort, RmeBrushDefinition> ItemBrushesByItemId,
    IReadOnlySet<ushort> BorderItemIds,
    IReadOnlySet<ushort> OptionalBorderItemIds,
    IReadOnlyList<string> LoadedFiles,
    IReadOnlyList<string> Warnings);

public sealed record RmeTilesetDefinition(
    string Name,
    IReadOnlyDictionary<RmePaletteCategory, IReadOnlyList<RmePaletteEntry>> Categories)
{
    public override string ToString() => Name;
}

public sealed class RmePaletteEntry
{
    private RmePaletteEntry(ushort? itemId, string? brushName, string? entityName)
    {
        ItemId = itemId;
        BrushName = brushName;
        EntityName = entityName;
    }

    public ushort? ItemId { get; }
    public string? BrushName { get; }
    public string? EntityName { get; }
    public RmeBrushDefinition? Brush { get; internal set; }
    public static RmePaletteEntry ForItem(ushort id) => new(id, null, null);
    public static RmePaletteEntry ForBrush(string name) => new(null, name, null);
    public static RmePaletteEntry ForNamedEntity(string name) => new(null, null, name);
}

public sealed record RmeBrushDefinition(
    string Name,
    string Type,
    uint ServerLookId,
    bool OneSize,
    bool RedoBorders,
    int Thickness,
    int ThicknessCeiling,
    IReadOnlyList<RmeBrushAlternative> Alternatives,
    int ZOrder = 0,
    IReadOnlyList<RmeBrushBorderRule>? BorderRules = null,
    IReadOnlyList<string>? Friends = null,
    bool HateFriends = false,
    RmeBorderReference? OptionalBorder = null,
    bool SoloOptionalBorder = false,
    IReadOnlyDictionary<string, IReadOnlyList<RmeWeightedItem>>? OrientedItems = null,
    IReadOnlyList<RmeDoorDefinition>? Doors = null,
    string? RedirectBrushName = null,
    bool PlaceOnBlocking = false,
    bool PlaceOnDuplicate = false,
    bool Draggable = false)
{
    public IEnumerable<ushort> AllItemIds =>
        Alternatives.SelectMany(alternative => alternative.SingleItems).Select(item => item.ItemId)
            .Concat(Alternatives.SelectMany(alternative => alternative.CompositeItems)
                .SelectMany(composite => composite.Tiles).SelectMany(tile => tile.ItemIds))
            .Concat((OrientedItems ?? new Dictionary<string, IReadOnlyList<RmeWeightedItem>>())
                .Values.SelectMany(items => items).Select(item => item.ItemId))
            .Concat((Doors ?? []).Select(door => door.ItemId))
            .Distinct();

    public uint PreviewItemId => ServerLookId != 0
        ? ServerLookId
        : Alternatives.SelectMany(alternative => alternative.SingleItems).Select(item => (uint)item.ItemId)
            .Concat(Alternatives.SelectMany(alternative => alternative.CompositeItems)
                .SelectMany(composite => composite.Tiles).SelectMany(tile => tile.ItemIds).Select(id => (uint)id))
            .FirstOrDefault();

    public RmeBrushPlacement CreatePlacement(int variation, Random random)
    {
        if (Alternatives.Count == 0)
            return ServerLookId is > 0 and <= ushort.MaxValue
                ? new RmeBrushPlacement([new RmeCompositeTile(0, 0, 0, [(ushort)ServerLookId])])
                : RmeBrushPlacement.Empty;

        var alternative = Alternatives[Math.Abs(variation) % Alternatives.Count];
        var singleChance = alternative.SingleItems.Sum(item => item.Chance);
        var compositeChance = alternative.CompositeItems.Sum(item => item.Chance);
        if (singleChance + compositeChance <= 0)
            return ServerLookId is > 0 and <= ushort.MaxValue
                ? new RmeBrushPlacement([new RmeCompositeTile(0, 0, 0, [(ushort)ServerLookId])])
                : RmeBrushPlacement.Empty;

        var typeRoll = random.Next(1, singleChance + compositeChance + 1);
        if (typeRoll <= compositeChance && compositeChance > 0)
        {
            var composite = PickWeighted(alternative.CompositeItems, compositeChance, item => item.Chance, random);
            return composite is null ? RmeBrushPlacement.Empty : new RmeBrushPlacement(composite.Tiles);
        }

        var single = PickWeighted(alternative.SingleItems, singleChance, item => item.Chance, random);
        return single is null
            ? RmeBrushPlacement.Empty
            : new RmeBrushPlacement([new RmeCompositeTile(0, 0, 0, [single.ItemId])]);
    }

    private static T? PickWeighted<T>(IReadOnlyList<T> values, int total, Func<T, int> weight, Random random)
        where T : class
    {
        if (values.Count == 0 || total <= 0) return null;
        var roll = random.Next(1, total + 1);
        foreach (var value in values)
        {
            roll -= Math.Max(0, weight(value));
            if (roll <= 0) return value;
        }
        return values[^1];
    }
}

public sealed record RmeBrushAlternative(
    IReadOnlyList<RmeWeightedItem> SingleItems,
    IReadOnlyList<RmeWeightedComposite> CompositeItems)
{
    public bool IsEmpty => SingleItems.Count == 0 && CompositeItems.Count == 0;
    public int TotalChance => SingleItems.Sum(item => item.Chance) + CompositeItems.Sum(item => item.Chance);
    public RmeBrushAlternative Merge(RmeBrushAlternative other) => new(
        SingleItems.Concat(other.SingleItems).ToArray(),
        CompositeItems.Concat(other.CompositeItems).ToArray());
}

public sealed record RmeWeightedItem(ushort ItemId, int Chance);
public sealed record RmeWeightedComposite(int Chance, IReadOnlyList<RmeCompositeTile> Tiles);
public sealed record RmeCompositeTile(int X, int Y, int Z, IReadOnlyList<ushort> ItemIds);
public sealed record RmeBrushPlacement(IReadOnlyList<RmeCompositeTile> Tiles)
{
    public static RmeBrushPlacement Empty { get; } = new([]);
}
public sealed record RmeBorderDefinition(
    int Id,
    int? Group,
    IReadOnlyDictionary<string, ushort> Items,
    bool IsOptional = false,
    ushort? GroundEquivalent = null);

public sealed record RmeBorderReference(int BorderId, RmeBorderDefinition? InlineBorder);

public sealed record RmeBrushBorderRule(
    string Alignment,
    string? TargetBrush,
    int BorderId,
    RmeBorderDefinition? InlineBorder,
    bool Super,
    IReadOnlyList<RmeBorderSpecificCase> SpecificCases);

public sealed record RmeBorderSpecificCase(
    IReadOnlyList<RmeBorderMatch> Matches,
    int MatchGroup,
    string? GroupAlignment,
    RmeBorderActions Actions,
    bool KeepBorder);

public sealed record RmeBorderMatch(RmeBorderMatchKind Kind, int Value, string? Edge)
{
    public static RmeBorderMatch ForBorder(int borderId, string edge) =>
        new(RmeBorderMatchKind.Border, borderId, edge);
    public static RmeBorderMatch ForGroup(int group) =>
        new(RmeBorderMatchKind.Group, group, null);
    public static RmeBorderMatch ForItem(ushort itemId) =>
        new(RmeBorderMatchKind.Item, itemId, null);
}

public sealed record RmeBorderActions(
    bool DeleteBorders,
    ushort DirectReplaceItemId,
    int ReplaceBorderId,
    string? ReplaceBorderEdge,
    ushort WithItemId);

public sealed record RmeDoorDefinition(
    ushort ItemId,
    string Alignment,
    string Type,
    bool IsOpen,
    bool IsLocked,
    bool HatesBrush);

public enum RmeBorderMatchKind
{
    Border,
    Group,
    Item
}

public enum RmePaletteCategory
{
    Terrain,
    Doodad,
    Item,
    Raw,
    Collection,
    Creature,
    House,
    Waypoint
}
