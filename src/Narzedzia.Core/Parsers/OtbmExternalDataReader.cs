using System.Globalization;
using System.Xml.Linq;
using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

/// <summary>
/// Czyta zewnętrzne pliki spawnów i domów dokładnie w układzie używanym przez RME.
/// Brak pliku dodatkowego nie blokuje otwarcia samej mapy OTBM.
/// </summary>
public static class OtbmExternalDataReader
{
    public static IReadOnlyList<string> Load(OtbmMap map, string otbmPath)
    {
        ArgumentNullException.ThrowIfNull(map);
        var directory = Path.GetDirectoryName(Path.GetFullPath(otbmPath))
                        ?? throw new InvalidOperationException("Mapa OTBM nie ma katalogu nadrzędnego.");
        var warnings = new List<string>();
        map.Houses.Clear();
        map.Spawns.Clear();

        LoadHouses(map, ResolveRelativeFile(directory, map.HouseFile), warnings);
        var spawnPath = ResolveRelativeFile(directory, map.SpawnFile);
        var npcPath = ResolveRelativeFile(directory, map.SpawnNpcFile);
        LoadSpawns(map, spawnPath, warnings);
        if (npcPath is not null && !string.Equals(spawnPath, npcPath, StringComparison.OrdinalIgnoreCase))
            LoadSpawns(map, npcPath, warnings);
        return warnings;
    }

    private static void LoadHouses(OtbmMap map, string? path, List<string> warnings)
    {
        if (path is null) return;
        if (!File.Exists(path))
        {
            warnings.Add($"Nie znaleziono pliku domów: {Path.GetFileName(path)}.");
            return;
        }

        try
        {
            var root = XDocument.Load(path).Root;
            if (root is null || !string.Equals(root.Name.LocalName, "houses", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Nieprawidłowy element główny; oczekiwano <houses>.");

            foreach (var node in root.Elements().Where(node =>
                         string.Equals(node.Name.LocalName, "house", StringComparison.OrdinalIgnoreCase)))
            {
                if (!TryUInt(node, "houseid", out var id) || id == 0) continue;
                map.Houses.Add(new OtbmHouse
                {
                    Id = id,
                    Name = Attribute(node, "name") ?? $"House #{id}",
                    EntryX = ToUShort(node, "entryx"),
                    EntryY = ToUShort(node, "entryy"),
                    EntryZ = ToByte(node, "entryz"),
                    Rent = ToUInt(node, "rent"),
                    TownId = ToUInt(node, "townid"),
                    Size = ToUInt(node, "size"),
                    IsGuildhall = ToBool(node, "guildhall")
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
        {
            warnings.Add($"Nie udało się odczytać pliku domów {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static void LoadSpawns(OtbmMap map, string? path, List<string> warnings)
    {
        if (path is null) return;
        if (!File.Exists(path))
        {
            warnings.Add($"Nie znaleziono pliku spawnów: {Path.GetFileName(path)}.");
            return;
        }

        try
        {
            var root = XDocument.Load(path).Root;
            if (root is null || !string.Equals(root.Name.LocalName, "spawns", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Nieprawidłowy element główny; oczekiwano <spawns>.");

            foreach (var node in root.Elements().Where(node =>
                         string.Equals(node.Name.LocalName, "spawn", StringComparison.OrdinalIgnoreCase)))
            {
                var centerX = ToUShort(node, "centerx");
                var centerY = ToUShort(node, "centery");
                var centerZ = ToByte(node, "centerz");
                var radius = Math.Max(1, ToInt(node, "radius", 1));
                if (centerX == 0 || centerY == 0) continue;

                var spawn = map.Spawns.FirstOrDefault(existing =>
                    existing.CenterX == centerX && existing.CenterY == centerY && existing.CenterZ == centerZ);
                if (spawn is null)
                {
                    spawn = new OtbmSpawn
                    {
                        CenterX = centerX,
                        CenterY = centerY,
                        CenterZ = centerZ,
                        Radius = radius
                    };
                    map.Spawns.Add(spawn);
                }
                else
                {
                    spawn.Radius = Math.Max(spawn.Radius, radius);
                }
                foreach (var creatureNode in node.Elements())
                {
                    var element = creatureNode.Name.LocalName;
                    var isNpc = string.Equals(element, "npc", StringComparison.OrdinalIgnoreCase);
                    if (!isNpc && !string.Equals(element, "monster", StringComparison.OrdinalIgnoreCase)) continue;
                    var name = Attribute(creatureNode, "name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var x = centerX + ToInt(creatureNode, "x");
                    var y = centerY + ToInt(creatureNode, "y");
                    if (x is < 0 or > ushort.MaxValue || y is < 0 or > ushort.MaxValue) continue;
                    spawn.Creatures.Add(new OtbmCreature
                    {
                        Name = name,
                        X = (ushort)x,
                        Y = (ushort)y,
                        Z = centerZ,
                        SpawnTime = ToInt(creatureNode, "spawntime"),
                        Direction = ToInt(creatureNode, "direction"),
                        IsNpc = isNpc
                    });
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidDataException)
        {
            warnings.Add($"Nie udało się odczytać pliku spawnów {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static string? ResolveRelativeFile(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Path.IsPathRooted(name) ? Path.GetFullPath(name) : Path.GetFullPath(Path.Combine(directory, name));
    }

    private static string? Attribute(XElement node, string name) =>
        node.Attributes().FirstOrDefault(attribute =>
            string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static bool TryUInt(XElement node, string name, out uint value) =>
        uint.TryParse(Attribute(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static uint ToUInt(XElement node, string name) => TryUInt(node, name, out var value) ? value : 0;
    private static ushort ToUShort(XElement node, string name) =>
        ushort.TryParse(Attribute(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (ushort)0;
    private static byte ToByte(XElement node, string name) =>
        byte.TryParse(Attribute(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (byte)0;
    private static int ToInt(XElement node, string name, int fallback = 0) =>
        int.TryParse(Attribute(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static bool ToBool(XElement node, string name) =>
        (Attribute(node, name) ?? string.Empty).ToLowerInvariant() is "1" or "true" or "yes";
}
