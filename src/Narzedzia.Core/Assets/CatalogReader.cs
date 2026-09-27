using System.Text.Json;
using System.Text.Json.Serialization;

namespace Narzedzia.Core.Assets;

/// <summary>
/// Wczytuje plik catalog-content.json z folderu assetów Tibii 12+.
/// </summary>
public static class CatalogReader
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static List<CatalogEntry> Read(string catalogPath)
    {
        var json = File.ReadAllText(catalogPath);
        return JsonSerializer.Deserialize<List<CatalogEntry>>(json, Opts) ?? [];
    }

    public static async Task<List<CatalogEntry>> ReadAsync(string catalogPath)
    {
        var json = await File.ReadAllTextAsync(catalogPath).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<CatalogEntry>>(json, Opts) ?? [];
    }
}
