using System.Text.Json;

namespace Narzedzia.Contracts.Localization;

/// <summary>
/// Persists the application language independently from module-specific settings.
/// The temporary-file swap prevents a terminated process from leaving a truncated
/// preferences document behind.
/// </summary>
public sealed class AppLanguagePreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    public AppLanguagePreferencesStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MikaschiStudioTools",
            "application.json");
    }

    public AppLanguage Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return AppLanguage.Polish;
            }

            var document = JsonSerializer.Deserialize<PreferencesDocument>(
                File.ReadAllText(_filePath), JsonOptions);
            return TryParse(document?.Language, out var language)
                ? language
                : AppLanguage.Polish;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return AppLanguage.Polish;
        }
    }

    public bool TrySave(AppLanguage language)
    {
        if (!Enum.IsDefined(language))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var temporaryPath = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            var document = new PreferencesDocument(language.ToString());
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
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
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A failed preferences cleanup must never interrupt application shutdown.
            }
        }
    }

    private static bool TryParse(string? value, out AppLanguage language)
    {
        if (Enum.TryParse(value, ignoreCase: true, out language) && Enum.IsDefined(language))
        {
            return true;
        }

        language = AppLanguage.Polish;
        return false;
    }

    private sealed record PreferencesDocument(string Language);
}
