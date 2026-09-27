namespace Narzedzia.Core.Models;

/// <summary>
/// Reprezentuje jeden wpis z TFS/OTServ items.xml. Każdy wpis może opisywać pojedynczy ID
/// (Id) lub zakres (FromId..ToId — wówczas Id=0). Atrybuty są surowymi parami klucz/wartość
/// z elementów &lt;attribute key="..." value="..."/&gt;.
/// </summary>
public sealed class ItemXmlEntry
{
    public ushort Id { get; set; }
    public ushort FromId { get; set; }
    public ushort ToId { get; set; }
    public string Article { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Plural { get; set; } = string.Empty;
    public string EditorSuffix { get; set; } = string.Empty;

    public Dictionary<string, string> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsRange => FromId > 0 && ToId >= FromId;

    public bool Matches(ushort serverId)
    {
        if (IsRange) return serverId >= FromId && serverId <= ToId;
        return Id == serverId;
    }

    /// <summary>Wartość atrybutu (case-insensitive) lub null gdy brak.</summary>
    public string? GetAttribute(string key) =>
        Attributes.TryGetValue(key, out var value) ? value : null;
}
