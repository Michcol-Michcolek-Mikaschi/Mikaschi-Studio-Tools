using System.Text.Json;
using System.Text.Json.Serialization;

namespace Modules.MapEditor.Services;

public sealed record MapEditorPreferences(
    string PaletteSection,
    IReadOnlyDictionary<string, string> PaletteGroups,
    int BrushSize,
    string BrushShape,
    bool Automagic)
{
    /// <summary>
    /// Źródła dodane jawnie przez użytkownika. Nie są uzupełniane na podstawie
    /// położenia mapy ani folderów nadrzędnych.
    /// </summary>
    public IReadOnlyList<CreatureImportSourcePreference> CreatureSources { get; init; } = [];

    public bool AutoLoadCreatureSources { get; init; } = true;

    public static MapEditorPreferences Default { get; } = new(
        "Terrain Palette",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        0,
        "Kwadrat",
        true);
}

public enum CreatureImportSourceKind
{
    Directory,
    XmlFile
}

/// <summary>
/// Jedno, jawnie wskazane przez użytkownika źródło definicji stworzeń.
/// Kolejność rekordów jest istotna: późniejsze źródło nadpisuje wcześniejsze.
/// </summary>
public sealed record CreatureImportSourcePreference(
    string Path,
    CreatureImportSourceKind Kind,
    bool Enabled = true);

/// <summary>
/// Przechowuje ostatni wybór palety i pędzla. Zapis tymczasowy zapobiega
/// pozostawieniu uszkodzonego pliku po przerwaniu pracy programu.
/// </summary>
public sealed class MapEditorPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly string _filePath;

    public MapEditorPreferencesStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MikaschiStudioTools",
            "map-editor.json");
    }

    public MapEditorPreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return MapEditorPreferences.Default;
            var preferences = JsonSerializer.Deserialize<MapEditorPreferences>(
                File.ReadAllText(_filePath), JsonOptions);
            return preferences is null
                ? MapEditorPreferences.Default
                : Normalize(preferences);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return MapEditorPreferences.Default;
        }
    }

    public bool TrySave(MapEditorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var temporary = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(Normalize(preferences), JsonOptions));
            File.Move(temporary, _filePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Preferencje nie mogą przerwać zamykania edytora.
            }
        }
    }

    private static MapEditorPreferences Normalize(MapEditorPreferences preferences)
    {
        var section = string.IsNullOrWhiteSpace(preferences.PaletteSection)
            ? MapEditorPreferences.Default.PaletteSection
            : preferences.PaletteSection;
        var groups = preferences.PaletteGroups?
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var shape = preferences.BrushShape is "Kwadrat" or "Okrąg"
            ? preferences.BrushShape
            : MapEditorPreferences.Default.BrushShape;
        var sources = (preferences.CreatureSources ?? [])
            .Select(NormalizeSource)
            .Where(source => source is not null)
            .Cast<CreatureImportSourcePreference>()
            .DistinctBy(
                source => source.Path,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new MapEditorPreferences(section, groups, Math.Clamp(preferences.BrushSize, 0, 11), shape,
            preferences.Automagic)
        {
            CreatureSources = sources,
            AutoLoadCreatureSources = preferences.AutoLoadCreatureSources
        };
    }

    private static CreatureImportSourcePreference? NormalizeSource(CreatureImportSourcePreference source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Path) || !Enum.IsDefined(source.Kind)) return null;
        try
        {
            return source with { Path = Path.GetFullPath(source.Path.Trim()) };
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
