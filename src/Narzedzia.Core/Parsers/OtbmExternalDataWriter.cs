using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

/// <summary>
/// Zapisuje zewnętrzne XML mapy w układzie RME. Obsługuje też rozszerzenie
/// Canary z osobnym plikiem NPC, nie dublując stworzeń między plikami.
/// </summary>
public static class OtbmExternalDataWriter
{
    public static int Save(OtbmMap map, string otbmPath)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentException.ThrowIfNullOrWhiteSpace(otbmPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(otbmPath))
                        ?? throw new InvalidOperationException("Mapa OTBM nie ma katalogu nadrzędnego.");
        var written = 0;

        if (ResolveRelativeFile(directory, map.HouseFile) is { } housePath)
        {
            WriteAtomically(BuildHouses(map), housePath);
            written++;
        }

        var spawnPath = ResolveRelativeFile(directory, map.SpawnFile);
        var npcPath = ResolveRelativeFile(directory, map.SpawnNpcFile);
        var splitNpcs = spawnPath is not null && npcPath is not null &&
                        !string.Equals(spawnPath, npcPath, StringComparison.OrdinalIgnoreCase);
        if (spawnPath is not null)
        {
            WriteAtomically(BuildSpawns(map, splitNpcs ? false : null, preserveEmptySpawns: true), spawnPath);
            written++;
        }
        if (splitNpcs)
        {
            WriteAtomically(BuildSpawns(map, true, preserveEmptySpawns: false), npcPath!);
            written++;
        }

        return written;
    }

    private static XDocument BuildHouses(OtbmMap map)
    {
        var root = new XElement("houses");
        foreach (var house in map.Houses.OrderBy(house => house.Id))
        {
            var size = (uint)map.Tiles.Values.Count(tile => tile.IsHouseTile && tile.HouseId == house.Id);
            house.Size = size;
            var node = new XElement("house",
                new XAttribute("name", house.Name),
                new XAttribute("houseid", house.Id),
                new XAttribute("entryx", house.EntryX),
                new XAttribute("entryy", house.EntryY),
                new XAttribute("entryz", house.EntryZ),
                new XAttribute("rent", house.Rent));
            if (house.IsGuildhall) node.Add(new XAttribute("guildhall", true));
            node.Add(new XAttribute("townid", house.TownId));
            node.Add(new XAttribute("size", size));
            root.Add(node);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static XDocument BuildSpawns(OtbmMap map, bool? npcOnly, bool preserveEmptySpawns)
    {
        var root = new XElement("spawns");
        var savedPositions = new HashSet<OtbmTileCoord>();
        foreach (var spawn in map.Spawns.OrderBy(spawn => spawn.CenterZ)
                     .ThenBy(spawn => spawn.CenterY).ThenBy(spawn => spawn.CenterX))
        {
            var creatures = spawn.Creatures
                .Where(creature => npcOnly is null || creature.IsNpc == npcOnly.Value)
                .Where(creature => savedPositions.Add(new(creature.X, creature.Y, creature.Z)))
                .ToArray();
            if (!preserveEmptySpawns && creatures.Length == 0) continue;

            var node = new XElement("spawn",
                new XAttribute("centerx", spawn.CenterX),
                new XAttribute("centery", spawn.CenterY),
                new XAttribute("centerz", spawn.CenterZ),
                new XAttribute("radius", Math.Max(1, spawn.Radius)));
            foreach (var creature in creatures)
            {
                var child = new XElement(creature.IsNpc ? "npc" : "monster",
                    new XAttribute("name", creature.Name),
                    new XAttribute("x", (int)creature.X - spawn.CenterX),
                    new XAttribute("y", (int)creature.Y - spawn.CenterY),
                    new XAttribute("z", creature.Z),
                    new XAttribute("spawntime", Math.Max(1, creature.SpawnTime)));
                if (creature.Direction != 0)
                    child.Add(new XAttribute("direction", creature.Direction));
                node.Add(child);
            }
            root.Add(node);
        }
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static string? ResolveRelativeFile(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Path.IsPathRooted(name) ? Path.GetFullPath(name) : Path.GetFullPath(Path.Combine(directory, name));
    }

    private static void WriteAtomically(XDocument document, string path)
    {
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Nieprawidłowa ścieżka pliku XML RME.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Indent = true,
                IndentChars = "\t",
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };
            using (var writer = XmlWriter.Create(temporary, settings)) document.Save(writer);
            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
