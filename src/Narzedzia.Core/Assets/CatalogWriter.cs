using System.Text.Json;
using System.Text.Json.Serialization;

namespace Narzedzia.Core.Assets;

public static class CatalogWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Write(string catalogPath, IReadOnlyList<CatalogEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, Options);
        File.WriteAllText(catalogPath, json);
    }

    /// <summary>
    /// Atomic-ish write: serialize to *.tmp, then File.Replace(tmp, target, backup).
    /// Pozostawia plik docelowy nienaruszony jeśli serializacja rzuci.
    /// </summary>
    public static void WriteAtomic(string catalogPath, IReadOnlyList<CatalogEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries, Options);
        var tmpPath = catalogPath + ".tmp";

        File.WriteAllText(tmpPath, json);

        if (File.Exists(catalogPath))
        {
            File.Replace(tmpPath, catalogPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmpPath, catalogPath);
        }
    }
}
