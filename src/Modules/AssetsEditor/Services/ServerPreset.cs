using System.Text.Json.Serialization;

namespace Modules.AssetsEditor.Services;

/// <summary>
/// Preset serwera OT — definiuje limity i możliwości dla wybranej wersji klienta/serwera.
/// </summary>
public sealed record ServerPreset
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("clientVersion")]
    public string ClientVersion { get; init; } = string.Empty;

    [JsonPropertyName("shiftMin")]
    public int ShiftMin { get; init; } = -32;

    [JsonPropertyName("shiftMax")]
    public int ShiftMax { get; init; } = 32;

    [JsonPropertyName("maxLevel")]
    public int MaxLevel { get; init; } = 999;

    [JsonPropertyName("imbueableSlotCountMax")]
    public int ImbueableSlotCountMax { get; init; } = 3;

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;

    public override string ToString() => $"{Name} ({ClientVersion})";
}
