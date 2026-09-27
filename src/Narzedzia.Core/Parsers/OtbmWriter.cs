using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

/// <summary>
/// Serializator OTBM. Tile grupuje po sektorach 256×256 per Z (zgodnie ze strukturą formatu).
/// Każdy bajt body jest escape-encoded: bajty kolidujące z NODE_START/END/ESCAPE poprzedzane ESCAPE.
/// </summary>
public sealed class OtbmWriter
{
    private const byte NODE_START = 0xFE;
    private const byte NODE_END   = 0xFF;
    private const byte ESCAPE     = 0xFD;

    private const byte OTBM_MAP_DATA   = 0x02;
    private const byte OTBM_TILE_AREA  = 0x04;
    private const byte OTBM_TILE       = 0x05;
    private const byte OTBM_ITEM       = 0x06;
    private const byte OTBM_TOWNS      = 0x0C;
    private const byte OTBM_TOWN       = 0x0D;
    private const byte OTBM_HOUSETILE  = 0x0E;
    private const byte OTBM_WAYPOINTS  = 0x0F;
    private const byte OTBM_WAYPOINT   = 0x10;

    private const byte OTBM_ATTR_DESCRIPTION       = 1;
    private const byte OTBM_ATTR_TILE_FLAGS        = 3;
    private const byte OTBM_ATTR_ACTION_ID         = 4;
    private const byte OTBM_ATTR_UNIQUE_ID         = 5;
    private const byte OTBM_ATTR_TEXT              = 6;
    private const byte OTBM_ATTR_DESC              = 7;
    private const byte OTBM_ATTR_TELE_DEST         = 8;
    private const byte OTBM_ATTR_ITEM              = 9;
    private const byte OTBM_ATTR_DEPOT_ID          = 10;
    private const byte OTBM_ATTR_EXT_SPAWN_FILE    = 11;
    private const byte OTBM_ATTR_RUNE_CHARGES      = 12;
    private const byte OTBM_ATTR_EXT_HOUSE_FILE    = 13;
    private const byte OTBM_ATTR_HOUSEDOORID       = 14;
    private const byte OTBM_ATTR_COUNT             = 15;
    private const byte OTBM_ATTR_DURATION          = 16;
    private const byte OTBM_ATTR_DECAYING_STATE    = 17;
    private const byte OTBM_ATTR_WRITTENDATE       = 18;
    private const byte OTBM_ATTR_WRITTENBY         = 19;
    private const byte OTBM_ATTR_SLEEPERGUID       = 20;
    private const byte OTBM_ATTR_SLEEPSTART        = 21;
    private const byte OTBM_ATTR_CHARGES           = 22;
    private const byte OTBM_ATTR_EXT_SPAWN_NPC_FILE = 23;
    private const byte OTBM_ATTR_PODIUMOUTFIT      = 40;
    private const byte OTBM_ATTR_TIER              = 41;
    private const byte OTBM_ATTR_ATTRIBUTE_MAP     = 128;

    public void Write(OtbmMap map, string path)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        // File version (4 bajty)
        w.Write(map.FileVersion);

        // Root node
        w.Write(NODE_START);
        w.Write(map.RootNodeType);
        WriteEscapedU32(w, map.Version);
        WriteEscapedU16(w, map.Width);
        WriteEscapedU16(w, map.Height);
        WriteEscapedU32(w, map.ItemsMajorVersion);
        WriteEscapedU32(w, map.ItemsMinorVersion);

        WriteMapData(w, map);

        w.Write(NODE_END);
    }

    private void WriteMapData(BinaryWriter w, OtbmMap map)
    {
        w.Write(NODE_START);
        w.Write(OTBM_MAP_DATA);

        if (map.RawMapDataAttributeData.Length > 0)
        {
            foreach (var value in map.RawMapDataAttributeData) WriteEscaped(w, value);
        }
        else
        {
            if (!string.IsNullOrEmpty(map.Description))
                WriteStringAttribute(w, OTBM_ATTR_DESCRIPTION, map.Description);
            if (!string.IsNullOrEmpty(map.SpawnFile))
                WriteStringAttribute(w, OTBM_ATTR_EXT_SPAWN_FILE, map.SpawnFile);
            if (!string.IsNullOrEmpty(map.HouseFile))
                WriteStringAttribute(w, OTBM_ATTR_EXT_HOUSE_FILE, map.HouseFile);
            if (!string.IsNullOrEmpty(map.SpawnNpcFile))
                WriteStringAttribute(w, OTBM_ATTR_EXT_SPAWN_NPC_FILE, map.SpawnNpcFile);
        }

        // Grupuj tile w sektory 256×256 per Z
        var sectors = map.Tiles.Values
            .GroupBy(t => new SectorKey(
                (ushort)(t.X & 0xFF00),
                (ushort)(t.Y & 0xFF00),
                t.Z))
            .OrderBy(g => g.Key.Z).ThenBy(g => g.Key.BaseY).ThenBy(g => g.Key.BaseX);

        foreach (var sector in sectors)
        {
            WriteTileArea(w, sector.Key.BaseX, sector.Key.BaseY, sector.Key.Z, sector, map.Version);
        }

        if (map.Towns.Count > 0) WriteTowns(w, map.Towns);
        if (map.Waypoints.Count > 0) WriteWaypoints(w, map.Waypoints);

        w.Write(NODE_END);
    }

    private void WriteTileArea(BinaryWriter w, ushort baseX, ushort baseY, byte baseZ,
        IEnumerable<OtbmTile> tiles, uint mapVersion)
    {
        w.Write(NODE_START);
        w.Write(OTBM_TILE_AREA);
        WriteEscapedU16(w, baseX);
        WriteEscapedU16(w, baseY);
        WriteEscaped(w, baseZ);

        foreach (var tile in tiles)
        {
            WriteTile(w, tile, baseX, baseY, mapVersion);
        }

        w.Write(NODE_END);
    }

    private void WriteTile(BinaryWriter w, OtbmTile tile, ushort baseX, ushort baseY, uint mapVersion)
    {
        w.Write(NODE_START);
        w.Write(tile.IsHouseTile ? OTBM_HOUSETILE : OTBM_TILE);

        WriteEscaped(w, (byte)(tile.X - baseX));
        WriteEscaped(w, (byte)(tile.Y - baseY));

        if (tile.IsHouseTile)
        {
            WriteEscapedU32(w, tile.HouseId);
        }

        if (tile.RawAttributeData.Length > 0)
        {
            foreach (var value in tile.RawAttributeData) WriteEscaped(w, value);
        }
        else if (tile.Flags != 0)
        {
            WriteEscaped(w, OTBM_ATTR_TILE_FLAGS);
            WriteEscapedU32(w, tile.Flags);
            if (tile.GroundItemId != 0)
            {
                WriteEscaped(w, OTBM_ATTR_ITEM);
                WriteEscapedU16(w, tile.GroundItemId);
            }
        }
        else if (tile.GroundItemId != 0)
        {
            WriteEscaped(w, OTBM_ATTR_ITEM);
            WriteEscapedU16(w, tile.GroundItemId);
        }

        foreach (var item in tile.Items)
        {
            WriteItem(w, item, mapVersion);
        }

        w.Write(NODE_END);
    }

    private void WriteItem(BinaryWriter w, OtbmItem item, uint mapVersion)
    {
        w.Write(NODE_START);
        w.Write(OTBM_ITEM);

        WriteEscapedU16(w, item.Id);

        if (item.RawAttributeData.Length > 0)
        {
            foreach (var value in item.RawAttributeData) WriteEscaped(w, value);
            foreach (var child in item.Contents) WriteItem(w, child, mapVersion);
            w.Write(NODE_END);
            return;
        }

        if (mapVersion == 0 && item.Count is { } legacyCount)
        {
            WriteEscaped(w, legacyCount);
        }
        else if (item.Count is { } count)
        {
            WriteEscaped(w, OTBM_ATTR_COUNT);
            WriteEscaped(w, count);
        }
        if (mapVersion >= 4)
        {
            WriteCustomAttributeMap(w, item);
        }
        else
        {
            if (item.ActionId is { } aid)
            {
                WriteEscaped(w, OTBM_ATTR_ACTION_ID);
                WriteEscapedU16(w, aid);
            }
            if (item.UniqueId is { } uid)
            {
                WriteEscaped(w, OTBM_ATTR_UNIQUE_ID);
                WriteEscapedU16(w, uid);
            }
            if (item.Text is { Length: > 0 } txt)
            {
                WriteEscaped(w, OTBM_ATTR_TEXT);
                WriteEscapedString(w, txt);
            }
            if (item.Description is { Length: > 0 } description)
            {
                WriteEscaped(w, OTBM_ATTR_DESC);
                WriteEscapedString(w, description);
            }
        }
        if (item.DepotId is { } depotId)
        {
            WriteEscaped(w, OTBM_ATTR_DEPOT_ID);
            WriteEscapedU16(w, depotId);
        }
        if (item.HouseDoorId is { } doorId)
        {
            WriteEscaped(w, OTBM_ATTR_HOUSEDOORID);
            WriteEscaped(w, doorId);
        }
        if (item.RuneCharges is { } runeCharges)
        {
            WriteEscaped(w, OTBM_ATTR_RUNE_CHARGES);
            WriteEscaped(w, runeCharges);
        }
        if (item.Duration is { } duration)
        {
            WriteEscaped(w, OTBM_ATTR_DURATION);
            WriteEscapedU32(w, duration);
        }
        if (item.DecayingState is { } decayingState)
        {
            WriteEscaped(w, OTBM_ATTR_DECAYING_STATE);
            WriteEscaped(w, decayingState);
        }
        if (item.WrittenDate is { } writtenDate)
        {
            WriteEscaped(w, OTBM_ATTR_WRITTENDATE);
            WriteEscapedU32(w, writtenDate);
        }
        if (item.WrittenBy is { Length: > 0 } writtenBy)
        {
            WriteEscaped(w, OTBM_ATTR_WRITTENBY);
            WriteEscapedString(w, writtenBy);
        }
        if (item.SleeperGuid is { } sleeperGuid)
        {
            WriteEscaped(w, OTBM_ATTR_SLEEPERGUID);
            WriteEscapedU32(w, sleeperGuid);
        }
        if (item.SleepStart is { } sleepStart)
        {
            WriteEscaped(w, OTBM_ATTR_SLEEPSTART);
            WriteEscapedU32(w, sleepStart);
        }
        if (item.Charges is { } charges)
        {
            WriteEscaped(w, OTBM_ATTR_CHARGES);
            WriteEscapedU16(w, charges);
        }
        if (item.PodiumOutfit is { Length: 15 } podium)
        {
            WriteEscaped(w, OTBM_ATTR_PODIUMOUTFIT);
            foreach (var value in podium) WriteEscaped(w, value);
        }
        if (mapVersion < 4 && item.Tier is { } tier)
        {
            WriteEscaped(w, OTBM_ATTR_TIER);
            WriteEscaped(w, tier);
        }
        if (item.TeleportX is not null && item.TeleportY is not null && item.TeleportZ is not null)
        {
            WriteEscaped(w, OTBM_ATTR_TELE_DEST);
            WriteEscapedU16(w, item.TeleportX.Value);
            WriteEscapedU16(w, item.TeleportY.Value);
            WriteEscaped(w, item.TeleportZ.Value);
        }

        foreach (var child in item.Contents)
        {
            WriteItem(w, child, mapVersion);
        }

        w.Write(NODE_END);
    }

    private static void WriteCustomAttributeMap(BinaryWriter writer, OtbmItem item)
    {
        var attributes = item.CustomAttributes
            .Where(attribute => !string.IsNullOrEmpty(attribute.Key))
            .GroupBy(attribute => attribute.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => CloneAttribute(group.Last()), StringComparer.Ordinal);

        SetCanonicalAttribute(attributes, "aid", item.ActionId is { } aid
            ? IntegerAttribute("aid", aid)
            : null);
        SetCanonicalAttribute(attributes, "uid", item.UniqueId is { } uid
            ? IntegerAttribute("uid", uid)
            : null);
        SetCanonicalAttribute(attributes, "text", item.Text is { Length: > 0 } text
            ? StringAttribute("text", text)
            : null);
        SetCanonicalAttribute(attributes, "desc", item.Description is { Length: > 0 } description
            ? StringAttribute("desc", description)
            : null);
        SetCanonicalAttribute(attributes, "tier", item.Tier is { } tier
            ? IntegerAttribute("tier", tier)
            : null);

        var ordered = attributes.Values.OrderBy(attribute => attribute.Key, StringComparer.Ordinal)
            .Take(ushort.MaxValue).ToArray();
        if (ordered.Length == 0) return;

        WriteEscaped(writer, OTBM_ATTR_ATTRIBUTE_MAP);
        WriteEscapedU16(writer, (ushort)ordered.Length);
        foreach (var attribute in ordered)
        {
            WriteEscapedString(writer, attribute.Key);
            WriteEscaped(writer, (byte)attribute.Type);
            switch (attribute.Type)
            {
                case OtbmCustomAttributeType.String:
                    WriteEscapedLongString(writer, attribute.StringValue);
                    break;
                case OtbmCustomAttributeType.Integer:
                    WriteEscapedU32(writer, unchecked((uint)attribute.IntegerValue));
                    break;
                case OtbmCustomAttributeType.Float:
                    WriteEscapedU32(writer, BitConverter.SingleToUInt32Bits(attribute.FloatValue));
                    break;
                case OtbmCustomAttributeType.Boolean:
                    WriteEscaped(writer, attribute.BooleanValue ? (byte)1 : (byte)0);
                    break;
                case OtbmCustomAttributeType.Double:
                    WriteEscapedU64(writer, unchecked((ulong)BitConverter.DoubleToInt64Bits(attribute.DoubleValue)));
                    break;
                default:
                    throw new InvalidDataException($"Nieobsługiwany typ atrybutu OTBM: {attribute.Type}.");
            }
        }
    }

    private static void SetCanonicalAttribute(
        IDictionary<string, OtbmCustomAttribute> attributes,
        string key,
        OtbmCustomAttribute? value)
    {
        if (value is null) attributes.Remove(key);
        else attributes[key] = value;
    }

    private static OtbmCustomAttribute IntegerAttribute(string key, int value) => new()
    {
        Key = key,
        Type = OtbmCustomAttributeType.Integer,
        IntegerValue = value
    };

    private static OtbmCustomAttribute StringAttribute(string key, string value) => new()
    {
        Key = key,
        Type = OtbmCustomAttributeType.String,
        StringValue = value
    };

    private static OtbmCustomAttribute CloneAttribute(OtbmCustomAttribute source) => new()
    {
        Key = source.Key,
        Type = source.Type,
        StringValue = source.StringValue,
        IntegerValue = source.IntegerValue,
        FloatValue = source.FloatValue,
        BooleanValue = source.BooleanValue,
        DoubleValue = source.DoubleValue
    };

    private void WriteTowns(BinaryWriter w, IEnumerable<OtbmTown> towns)
    {
        w.Write(NODE_START);
        w.Write(OTBM_TOWNS);
        foreach (var t in towns)
        {
            w.Write(NODE_START);
            w.Write(OTBM_TOWN);
            WriteEscapedU32(w, t.Id);
            WriteEscapedString(w, t.Name);
            WriteEscapedU16(w, t.TempleX);
            WriteEscapedU16(w, t.TempleY);
            WriteEscaped(w, t.TempleZ);
            w.Write(NODE_END);
        }
        w.Write(NODE_END);
    }

    private void WriteWaypoints(BinaryWriter w, IEnumerable<OtbmWaypoint> waypoints)
    {
        w.Write(NODE_START);
        w.Write(OTBM_WAYPOINTS);
        foreach (var wp in waypoints)
        {
            w.Write(NODE_START);
            w.Write(OTBM_WAYPOINT);
            WriteEscapedString(w, wp.Name);
            WriteEscapedU16(w, wp.X);
            WriteEscapedU16(w, wp.Y);
            WriteEscaped(w, wp.Z);
            w.Write(NODE_END);
        }
        w.Write(NODE_END);
    }

    private static void WriteStringAttribute(BinaryWriter w, byte attrType, string value)
    {
        WriteEscaped(w, attrType);
        WriteEscapedString(w, value);
    }

    private static void WriteEscapedString(BinaryWriter w, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
            throw new InvalidDataException("Napis OTBM przekracza limit 65535 bajtów UTF-8.");
        WriteEscapedU16(w, (ushort)bytes.Length);
        foreach (var b in bytes) WriteEscaped(w, b);
    }

    private static void WriteEscapedLongString(BinaryWriter w, string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        WriteEscapedU32(w, (uint)bytes.Length);
        foreach (var b in bytes) WriteEscaped(w, b);
    }

    private static void WriteEscapedU64(BinaryWriter w, ulong value)
    {
        for (var i = 0; i < 8; i++) WriteEscaped(w, (byte)((value >> (i * 8)) & 0xFF));
    }

    private static void WriteEscapedU32(BinaryWriter w, uint value)
    {
        for (var i = 0; i < 4; i++) WriteEscaped(w, (byte)((value >> (i * 8)) & 0xFF));
    }

    private static void WriteEscapedU16(BinaryWriter w, ushort value)
    {
        WriteEscaped(w, (byte)(value & 0xFF));
        WriteEscaped(w, (byte)((value >> 8) & 0xFF));
    }

    private static void WriteEscaped(BinaryWriter w, byte b)
    {
        if (b == NODE_START || b == NODE_END || b == ESCAPE)
            w.Write(ESCAPE);
        w.Write(b);
    }

    private readonly record struct SectorKey(ushort BaseX, ushort BaseY, byte Z);
}
