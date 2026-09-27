using System.Text.Json;

namespace Modules.OldItemEditor.Services;

public sealed record OldItemEditorPreferences(
    string ClientDirectory,
    bool ExtendedSprites,
    bool FrameDurations,
    bool Transparency)
{
    public static OldItemEditorPreferences Default { get; } = new(string.Empty, false, false, false);
}

/// <summary>
/// Przechowuje ustawienia klasycznego Item Editora w profilu użytkownika. Zapis przez plik
/// tymczasowy zapobiega pozostawieniu uszkodzonego JSON-a po przerwanym zapisie.
/// </summary>
public sealed class OldItemEditorPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public OldItemEditorPreferencesStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MikaschiStudioTools",
            "old-item-editor.json");
    }

    public OldItemEditorPreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return OldItemEditorPreferences.Default;
            return JsonSerializer.Deserialize<OldItemEditorPreferences>(File.ReadAllText(_filePath), JsonOptions)
                   ?? OldItemEditorPreferences.Default;
        }
        catch (IOException)
        {
            return OldItemEditorPreferences.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return OldItemEditorPreferences.Default;
        }
        catch (JsonException)
        {
            return OldItemEditorPreferences.Default;
        }
    }

    public void Save(OldItemEditorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var directory = Path.GetDirectoryName(_filePath)
                        ?? throw new InvalidOperationException("Nieprawidłowa ścieżka ustawień Old Item Editora.");
        Directory.CreateDirectory(directory);
        var temporary = _filePath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(temporary, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
