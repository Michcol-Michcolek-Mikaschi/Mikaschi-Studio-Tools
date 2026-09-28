namespace Modules.MapEditor.Services;

/// <summary>
/// Wczytuje wyłącznie źródła jawnie wskazane przez użytkownika. Nie próbuje
/// odnajdywać silnika względem mapy, katalogu roboczego ani katalogów nadrzędnych.
/// </summary>
public sealed class CreatureImportSourceService
{
    private static readonly HashSet<string> CatalogDirectoryNames = new(
        ["monster", "monsters", "npc", "npcs"],
        StringComparer.OrdinalIgnoreCase);

    private readonly RmeCreatureCatalogService _catalogService;

    public CreatureImportSourceService(RmeCreatureCatalogService? catalogService = null)
    {
        _catalogService = catalogService ?? new RmeCreatureCatalogService();
    }

    public CreatureImportSourcesResult Load(
        IEnumerable<CreatureImportSourcePreference> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var merged = new Dictionary<string, RmeCreatureDefinition>(StringComparer.OrdinalIgnoreCase);
        var sourceResults = new List<CreatureImportSourceResult>();

        foreach (var source in sources.Where(source => source.Enabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = LoadOne(source, cancellationToken);
            sourceResults.Add(result);
            foreach (var (name, definition) in result.Creatures)
                merged[name] = definition;
        }

        return new CreatureImportSourcesResult(merged, sourceResults);
    }

    private CreatureImportSourceResult LoadOne(
        CreatureImportSourcePreference source,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(source.Path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            warnings.Add($"Nieprawidłowa ścieżka: {ex.Message}");
            return new CreatureImportSourceResult(source, EmptyDefinitions(), warnings);
        }

        if (source.Kind == CreatureImportSourceKind.XmlFile)
        {
            if (!File.Exists(fullPath))
            {
                warnings.Add($"Nie znaleziono pliku: {fullPath}");
                return new CreatureImportSourceResult(source, EmptyDefinitions(), warnings);
            }

            var root = Path.GetDirectoryName(fullPath)!;
            var imported = _catalogService.ImportFromOtFiles([fullPath], cancellationToken, root);
            return new CreatureImportSourceResult(source, imported.Creatures, imported.Warnings);
        }

        if (!Directory.Exists(fullPath))
        {
            warnings.Add($"Nie znaleziono katalogu: {fullPath}");
            return new CreatureImportSourceResult(source, EmptyDefinitions(), warnings);
        }

        IReadOnlyList<string> files;
        try
        {
            files = FindCatalogFiles(fullPath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Nie można odczytać katalogu: {ex.Message}");
            return new CreatureImportSourceResult(source, EmptyDefinitions(), warnings);
        }

        if (files.Count == 0)
        {
            warnings.Add("W wybranym katalogu nie znaleziono plików XML.");
            return new CreatureImportSourceResult(source, EmptyDefinitions(), warnings);
        }

        var directoryImport = _catalogService.ImportFromOtFiles(files, cancellationToken, fullPath);
        return new CreatureImportSourceResult(source, directoryImport.Creatures, directoryImport.Warnings);
    }

    internal static IReadOnlyList<string> FindCatalogFiles(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        var allXml = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.GetExtension(file).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                allXml.Add(Path.GetFullPath(file));
        }
        allXml.Sort(StringComparer.OrdinalIgnoreCase);

        // Przy wskazaniu całego katalogu danych serwera ograniczamy skan do
        // standardowych katalogów monster/npc. Jeżeli użytkownik wskazał własny,
        // płaski katalog, akceptujemy wszystkie znajdujące się w nim XML-e.
        var preferred = allXml.Where(path => IsCatalogFile(root, path)).ToArray();
        return preferred.Length > 0 ? preferred : allXml;
    }

    private static bool IsCatalogFile(string root, string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.Equals("monsters.xml", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("creatures.xml", StringComparison.OrdinalIgnoreCase))
            return true;

        if (CatalogDirectoryNames.Contains(Path.GetFileName(root))) return true;
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Take(Math.Max(0, segments.Length - 1)).Any(CatalogDirectoryNames.Contains);
    }

    private static IReadOnlyDictionary<string, RmeCreatureDefinition> EmptyDefinitions() =>
        new Dictionary<string, RmeCreatureDefinition>(StringComparer.OrdinalIgnoreCase);
}

public sealed record CreatureImportSourceResult(
    CreatureImportSourcePreference Source,
    IReadOnlyDictionary<string, RmeCreatureDefinition> Creatures,
    IReadOnlyList<string> Warnings);

public sealed record CreatureImportSourcesResult(
    IReadOnlyDictionary<string, RmeCreatureDefinition> Creatures,
    IReadOnlyList<CreatureImportSourceResult> Sources);
