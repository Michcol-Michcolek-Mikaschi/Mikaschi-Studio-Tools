using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Modules.MapEditor.Services;

/// <summary>
/// Wczytuje wersjonowany creatures.xml dostarczany z RME. Dzięki temu paleta
/// zachowuje rozróżnienie Monster/NPC oraz właściwy outfit dla danego klienta.
/// </summary>
public sealed class RmeCreatureCatalogService
{
    private static readonly Regex BareAmpersand = new(
        "&(?!#\\d+;|#x[0-9a-fA-F]+;|[A-Za-z_:][A-Za-z0-9_.:-]*;)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyDictionary<string, RmeCreatureDefinition> Load(string versionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(versionDirectory);
        var path = Path.Combine(Path.GetFullPath(versionDirectory), "creatures.xml");
        if (!File.Exists(path)) return new Dictionary<string, RmeCreatureDefinition>();

        var source = File.ReadAllText(path).TrimEnd('\0');
        source = BareAmpersand.Replace(source, "&amp;");
        var root = XDocument.Parse(source).Root;
        if (root is null || !root.Name.LocalName.Equals("creatures", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plik creatures.xml nie ma elementu głównego <creatures>.");

        var result = new Dictionary<string, RmeCreatureDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in root.Elements().Where(element =>
                     element.Name.LocalName.Equals("creature", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Attribute(node, "name")?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            result[name] = new RmeCreatureDefinition(
                name,
                string.Equals(Attribute(node, "type"), "npc", StringComparison.OrdinalIgnoreCase),
                UIntAttribute(node, "looktype"),
                UIntAttribute(node, "lookitem"));
        }
        return result;
    }

    public RmeCreatureImportResult ImportFromOtFiles(
        IEnumerable<string> paths,
        CancellationToken cancellationToken = default,
        string? allowedRoot = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedRoot = string.IsNullOrWhiteSpace(allowedRoot)
            ? null
            : Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var imported = new Dictionary<string, RmeCreatureDefinition>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)) continue;
            try
            {
                ImportFile(Path.GetFullPath(path), imported, warnings, visited, cancellationToken, normalizedRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           System.Xml.XmlException or ArgumentException)
            {
                warnings.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return new RmeCreatureImportResult(imported, warnings);
    }

    private static void ImportFile(
        string path,
        IDictionary<string, RmeCreatureDefinition> imported,
        ICollection<string> warnings,
        ISet<string> visited,
        CancellationToken cancellationToken,
        string? allowedRoot)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        if (allowedRoot is not null && !IsPathInsideRoot(path, allowedRoot))
        {
            warnings.Add($"Pominięto odwołanie poza wybranym źródłem: {path}.");
            return;
        }
        if (!visited.Add(path)) return;
        if (!File.Exists(path))
        {
            warnings.Add($"Nie znaleziono pliku {path}.");
            return;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var source = BareAmpersand.Replace(File.ReadAllText(path).TrimEnd('\0'), "&amp;");
        cancellationToken.ThrowIfCancellationRequested();
        var root = XDocument.Parse(source).Root;
        cancellationToken.ThrowIfCancellationRequested();
        if (root is null)
        {
            warnings.Add($"{Path.GetFileName(path)}: pusty dokument XML.");
            return;
        }

        if (root.Name.LocalName.Equals("monsters", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(path)!;
            foreach (var node in root.Elements().Where(element =>
                         element.Name.LocalName.Equals("monster", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Attribute(node, "file");
                if (string.IsNullOrWhiteSpace(relative)) continue;
                var nestedPath = Path.GetFullPath(Path.Combine(directory, relative));
                try
                {
                    ImportFile(nestedPath, imported, warnings, visited, cancellationToken, allowedRoot);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                               System.Xml.XmlException or ArgumentException)
                {
                    warnings.Add($"{Path.GetFileName(nestedPath)}: {ex.Message}");
                }
            }
            return;
        }

        if (root.Name.LocalName.Equals("creatures", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var node in root.Elements().Where(element =>
                         element.Name.LocalName.Equals("creature", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = ParseRmeCreature(node, Path.GetFileNameWithoutExtension(path));
                if (definition is not null) imported[definition.Name] = definition;
            }
            return;
        }

        if (!root.Name.LocalName.Equals("monster", StringComparison.OrdinalIgnoreCase) &&
            !root.Name.LocalName.Equals("npc", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"{Path.GetFileName(path)} nie jest plikiem monster, npc ani monsters.xml.");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var isNpc = root.Name.LocalName.Equals("npc", StringComparison.OrdinalIgnoreCase);
        var name = isNpc ? Path.GetFileNameWithoutExtension(path) : Attribute(root, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            warnings.Add($"{Path.GetFileName(path)}: brak nazwy stworzenia.");
            return;
        }
        var look = root.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("look", StringComparison.OrdinalIgnoreCase));
        var lookItem = UIntAttribute(look, "item");
        if (lookItem == 0) lookItem = UIntAttribute(look, "lookex");
        if (lookItem == 0) lookItem = UIntAttribute(look, "typeex");
        imported[name] = new RmeCreatureDefinition(
            name,
            isNpc,
            UIntAttribute(look, "type"),
            lookItem);
    }

    private static RmeCreatureDefinition? ParseRmeCreature(XElement node, string fallbackName)
    {
        var name = Attribute(node, "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = fallbackName;
        if (string.IsNullOrWhiteSpace(name)) return null;
        return new RmeCreatureDefinition(
            name,
            Attribute(node, "type")?.Equals("npc", StringComparison.OrdinalIgnoreCase) == true,
            UIntAttribute(node, "looktype"),
            UIntAttribute(node, "lookitem"));
    }

    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static uint UIntAttribute(XElement? element, string name) =>
        element is not null && uint.TryParse(Attribute(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static bool IsPathInsideRoot(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record RmeCreatureDefinition(string Name, bool IsNpc, uint LookType, uint LookItem);
public sealed record RmeCreatureImportResult(
    IReadOnlyDictionary<string, RmeCreatureDefinition> Creatures,
    IReadOnlyList<string> Warnings);
