using System.Collections.Concurrent;
using System.Text.Json;

namespace Narzedzia.Contracts.Preferences;

/// <summary>
/// Ostatnie katalogi wejściowe używane przez narzędzia do pracy ze sprite'ami.
/// Brak wartości oznacza, że narzędzie powinno użyć domyślnej lokalizacji systemu.
/// </summary>
public sealed record SpriteToolsPreferences(
    string? ResizerInputFolder,
    string? CutterInputFolder)
{
    public static SpriteToolsPreferences Empty { get; } = new(null, null);
}

/// <summary>
/// Przechowuje ostatnie katalogi Resizera i Cuttera we wspólnym dokumencie.
/// Aktualizacja jednego katalogu zawsze wczytuje najnowszy dokument, dzięki czemu
/// nie nadpisuje ustawienia należącego do drugiego narzędzia.
/// </summary>
public sealed class SpriteToolsPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly ConcurrentDictionary<string, object> FileLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _filePath;
    private readonly object _fileLock;

    public SpriteToolsPreferencesStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MikaschiStudioTools",
            "sprite-tools.json");
        _fileLock = FileLocks.GetOrAdd(GetLockKey(_filePath), static _ => new object());
    }

    public SpriteToolsPreferences Load()
    {
        lock (_fileLock)
        {
            return LoadCore();
        }
    }

    public bool TrySetResizerInputFolder(string folderPath)
    {
        return TrySetFolder(
            folderPath,
            static (preferences, normalizedPath) => preferences with
            {
                ResizerInputFolder = normalizedPath
            });
    }

    public bool TrySetCutterInputFolder(string folderPath)
    {
        return TrySetFolder(
            folderPath,
            static (preferences, normalizedPath) => preferences with
            {
                CutterInputFolder = normalizedPath
            });
    }

    private bool TrySetFolder(
        string folderPath,
        Func<SpriteToolsPreferences, string, SpriteToolsPreferences> update)
    {
        if (!TryNormalizeFolder(folderPath, out var normalizedPath) || normalizedPath is null)
        {
            return false;
        }

        lock (_fileLock)
        {
            var preferences = update(LoadCore(), normalizedPath);
            return TryWriteCore(preferences);
        }
    }

    private SpriteToolsPreferences LoadCore()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return SpriteToolsPreferences.Empty;
            }

            var document = JsonSerializer.Deserialize<SpriteToolsPreferences>(
                File.ReadAllText(_filePath),
                JsonOptions);
            return Normalize(document);
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or JsonException
                                   or NotSupportedException
                                   or ArgumentException)
        {
            return SpriteToolsPreferences.Empty;
        }
    }

    private bool TryWriteCore(SpriteToolsPreferences preferences)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var temporaryPath = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(Normalize(preferences), JsonOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or NotSupportedException
                                   or ArgumentException)
        {
            return false;
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static SpriteToolsPreferences Normalize(SpriteToolsPreferences? preferences)
    {
        if (preferences is null)
        {
            return SpriteToolsPreferences.Empty;
        }

        TryNormalizeFolder(preferences.ResizerInputFolder, out var resizerFolder);
        TryNormalizeFolder(preferences.CutterInputFolder, out var cutterFolder);
        return new SpriteToolsPreferences(resizerFolder, cutterFolder);
    }

    private static bool TryNormalizeFolder(string? folderPath, out string? normalizedPath)
    {
        normalizedPath = null;
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(folderPath.Trim());
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return false;
        }
    }

    private static string GetLockKey(string filePath)
    {
        try
        {
            return Path.GetFullPath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            return filePath;
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or NotSupportedException
                                   or ArgumentException)
        {
            // Nieudane czyszczenie preferencji nie może zatrzymać aplikacji.
        }
    }
}
