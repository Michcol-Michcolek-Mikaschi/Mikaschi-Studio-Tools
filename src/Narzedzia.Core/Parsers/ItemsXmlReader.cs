using System.Xml.Linq;
using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

/// <summary>
/// Parser items.xml w formacie TFS/OTServ. Czyta &lt;items&gt; z dziećmi &lt;item&gt;,
/// dla każdego zbiera atrybuty z elementów &lt;attribute key="" value=""/&gt;.
/// </summary>
public static class ItemsXmlReader
{
    public static List<ItemXmlEntry> Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Brak pliku items.xml.", path);
        }

        var doc = XDocument.Load(path);
        var root = doc.Root
            ?? throw new InvalidDataException("items.xml nie ma korzenia.");

        var entries = new List<ItemXmlEntry>();
        foreach (var node in root.Elements("item"))
        {
            var entry = new ItemXmlEntry
            {
                Article = node.Attribute("article")?.Value ?? string.Empty,
                Name = node.Attribute("name")?.Value ?? string.Empty,
                Plural = node.Attribute("plural")?.Value ?? string.Empty,
                EditorSuffix = node.Attribute("editorsuffix")?.Value ?? string.Empty,
            };

            if (TryParseUshort(node.Attribute("id")?.Value, out var id))
            {
                entry.Id = id;
            }
            if (TryParseUshort(node.Attribute("fromid")?.Value, out var fromId))
            {
                entry.FromId = fromId;
            }
            if (TryParseUshort(node.Attribute("toid")?.Value, out var toId))
            {
                entry.ToId = toId;
            }

            foreach (var attr in node.Elements("attribute"))
            {
                var key = attr.Attribute("key")?.Value;
                var value = attr.Attribute("value")?.Value;
                if (!string.IsNullOrWhiteSpace(key) && value is not null)
                {
                    entry.Attributes[key] = value;
                }
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>Buduje słownik ServerId → ItemXmlEntry, rozwijając zakresy fromid/toid.</summary>
    public static Dictionary<ushort, ItemXmlEntry> BuildLookup(IEnumerable<ItemXmlEntry> entries)
    {
        var lookup = new Dictionary<ushort, ItemXmlEntry>();
        foreach (var entry in entries)
        {
            if (entry.IsRange)
            {
                for (var id = entry.FromId; id <= entry.ToId; id++)
                {
                    lookup[id] = entry;
                    if (id == ushort.MaxValue) break;
                }
            }
            else if (entry.Id > 0)
            {
                lookup[entry.Id] = entry;
            }
        }
        return lookup;
    }

    private static bool TryParseUshort(string? text, out ushort value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return false;
        }
        return ushort.TryParse(text.Trim(), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
