using System.Text.Json;

namespace Modules.MapEditor.Services;

public sealed record MapEditorPreferences(
    string PaletteSection,
    IReadOnlyDictionary<string, string> PaletteGroups,
    int BrushSize,
    string BrushShape,
    bool Automagic)
{
    public static MapEditorPreferences Default { get; } = new(
        "Terrain Palette",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        0,
        "Kwadrat",
        true);
}

/// <summary>
/// Przechowuje ostatni wybór palety i pędzla. Zapis tymczasowy zapobiega
/// pozostawieniu uszkodzonego pliku po przerwaniu pracy programu.
/// </summary>
public sealed class MapEditorPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
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
        return new MapEditorPreferences(section, groups, Math.Clamp(preferences.BrushSize, 0, 11), shape,
            preferences.Automagic);
    }
}
