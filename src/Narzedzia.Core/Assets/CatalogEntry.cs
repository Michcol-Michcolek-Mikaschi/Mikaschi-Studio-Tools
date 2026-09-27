using System.Text.Json;
using System.Text.Json.Serialization;

namespace Narzedzia.Core.Assets;

/// <summary>
/// Jeden wpis z pliku catalog-content.json (klient Tibia 12+).
/// Opisuje arkusz sprite'ów (type="sprite") lub plik appearances.dat (type="appearances").
/// </summary>
public sealed class CatalogEntry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    /// <summary>Indeks do tablicy SpriteSizes (rozmiar kafelka w arkuszu).</summary>
    [JsonPropertyName("spritetype")]
    public int SpriteType { get; set; }

    [JsonPropertyName("firstspriteid")]
    public int FirstSpriteid { get; set; }

    [JsonPropertyName("lastspriteid")]
    public int LastSpriteid { get; set; }

    [JsonPropertyName("area")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Area { get; set; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Version { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
