using System.Buffers;
using System.Buffers.Binary;
using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

/// <summary>
/// Parser OTBM (OpenTibia Binary Map) — format kontener-drzewo (NODE_START/END/ESCAPE)
/// taki sam jak OTB. Read-only MVP, zgodne ze specyfikacją RME/TFS.
/// </summary>
public sealed class OtbmReader
{
    private const byte NODE_START = 0xFE;
    private const byte NODE_END   = 0xFF;
    private const byte ESCAPE     = 0xFD;

    // OTBM node types
    private const byte OTBM_MAP_DATA   = 0x02;
    private const byte OTBM_TILE_AREA  = 0x04;
    private const byte OTBM_TILE       = 0x05;
    private const byte OTBM_ITEM       = 0x06;
    private const byte OTBM_TOWNS      = 0x0C;
    private const byte OTBM_TOWN       = 0x0D;
    private const byte OTBM_HOUSETILE  = 0x0E;
    private const byte OTBM_WAYPOINTS  = 0x0F;
    private const byte OTBM_WAYPOINT   = 0x10;

    // OTBM attribute types
    private const byte OTBM_ATTR_DESCRIPTION       = 1;
    private const byte OTBM_ATTR_EXT_FILE          = 2;
    private const byte OTBM_ATTR_TILE_FLAGS        = 3;
    private const byte OTBM_ATTR_ACTION_ID         = 4;
    private const byte OTBM_ATTR_UNIQUE_ID         = 5;
    private const byte OTBM_ATTR_TEXT              = 6;
    private const byte OTBM_ATTR_DESC              = 7;
    private const byte OTBM_ATTR_TELE_DEST         = 8;
    private const byte OTBM_ATTR_ITEM              = 9;
    private const byte OTBM_ATTR_DEPOT_ID          = 10;
    private const byte OTBM_ATTR_EXT_SPAWN_FILE    = 11;
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

    public OtbmMap Read(string path) => Read(path, progress: null, CancellationToken.None);

    /// <summary>
    /// Wczytuje mapę i raportuje rzeczywisty postęp przejścia po strumieniu OTBM.
    /// Raportowanie jest ograniczone do zmian co najmniej 0,5%, aby duże mapy nie
    /// zalewały kolejki UI setkami tysięcy powiadomień.
    /// </summary>
    public OtbmMap Read(
        string path,
        IProgress<OtbmReadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new OtbmStreamReader(path, cancellationToken);
        progress?.Report(new OtbmReadProgress(0, 0, 0, reader.Length));
        var context = new ReadContext(reader.Length, progress, cancellationToken);

        // Pierwsze 4 bajty: wersja pliku (zwykle 0)
        if (reader.Length < 4) throw new InvalidDataException("OTBM: za krótki nagłówek.");
        var fileVersion = reader.ReadUInt32();

        // Root node start
        if (!reader.TryReadByte(out var rootMarker) || rootMarker != NODE_START)
            throw new InvalidDataException("OTBM: brak korzenia (NODE_START).");
        if (!reader.TryReadByte(out var rootType))
            throw new InvalidDataException("OTBM: brak typu korzenia.");

        // Root body: u32 version, u16 width, u16 height, u32 itemsMajor, u32 itemsMinor
        var rootBody = ReadNodeBody(reader);
        if (rootBody.Length < 16) throw new InvalidDataException("OTBM: za krótki nagłówek mapy.");

        var map = new OtbmMap
        {
            FileVersion      = fileVersion,
            RootNodeType     = rootType,
            Version           = BitConverter.ToUInt32(rootBody, 0),
            Width             = BitConverter.ToUInt16(rootBody, 4),
            Height            = BitConverter.ToUInt16(rootBody, 6),
            ItemsMajorVersion = BitConverter.ToUInt32(rootBody, 8),
            ItemsMinorVersion = BitConverter.ToUInt32(rootBody, 12),
        };
        // Dzieci korzenia
        while (reader.TryPeekByte(out var marker) && marker == NODE_START)
        {
            reader.ReadByte(); // skip NODE_START
            var nodeType = reader.ReadRequiredByte("OTBM: brak typu węzła korzenia.");
            switch (nodeType)
            {
                case OTBM_MAP_DATA:
                    ParseMapData(reader, map, context);
                    ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł danych mapy.");
                    break;
                default:            SkipNode(reader); break;
            }
            context.Report(reader.Position);
        }

        ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł główny mapy.");
        if (reader.TryReadByte(out _))
            throw new InvalidDataException("OTBM: nieoczekiwane dane za końcem węzła głównego.");

        context.Complete(reader.Position);
        return map;
    }

    /// <summary>Kończy rozpoznany węzeł i odrzuca ucięte lub źle zagnieżdżone drzewo.</summary>
    private static void ConsumeRequiredEnd(OtbmStreamReader reader, string errorMessage)
    {
        if (!reader.TryReadByte(out var marker) || marker != NODE_END)
            throw new InvalidDataException(errorMessage);
    }

    private void ParseMapData(OtbmStreamReader reader, OtbmMap map, ReadContext context)
    {
        var body = ReadNodeBody(reader);
        map.RawMapDataAttributeData = body;
        // Atrybuty: 1 bajt typ, dalej dane (zmienne) — używane: OTBM_ATTR_DESCRIPTION + 2 * EXT_FILE
        int bp = 0;
        while (bp < body.Length)
        {
            var attr = body[bp++];
            switch (attr)
            {
                case OTBM_ATTR_DESCRIPTION:
                    map.Description = string.IsNullOrEmpty(map.Description)
                        ? ReadString(body, ref bp)
                        : map.Description + "\n" + ReadString(body, ref bp);
                    break;
                case OTBM_ATTR_EXT_SPAWN_FILE:
                    map.SpawnFile = ReadString(body, ref bp);
                    break;
                case OTBM_ATTR_EXT_HOUSE_FILE:
                    map.HouseFile = ReadString(body, ref bp);
                    break;
                case OTBM_ATTR_EXT_SPAWN_NPC_FILE:
                    map.SpawnNpcFile = ReadString(body, ref bp);
                    break;
                default:
                    // nieznany atrybut → przerwij parsowanie atrybutów
                    bp = body.Length;
                    break;
            }
        }

        while (reader.TryPeekByte(out var marker) && marker == NODE_START)
        {
            reader.ReadByte();
            var childType = reader.ReadRequiredByte("OTBM: brak typu potomnego węzła mapy.");
            switch (childType)
            {
                case OTBM_TILE_AREA:
                    ParseTileArea(reader, map, context);
                    ConsumeRequiredEnd(reader, "OTBM: niedomknięty obszar kafelków.");
                    break;
                case OTBM_TOWNS:
                    ParseTowns(reader, map);
                    ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł miast.");
                    break;
                case OTBM_WAYPOINTS:
                    ParseWaypoints(reader, map);
                    ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł waypointów.");
                    break;
                default:             SkipNode(reader); break; // SkipNode consumes own NODE_END
            }
            context.Report(reader.Position);
        }
    }

    private void ParseTileArea(OtbmStreamReader reader, OtbmMap map, ReadContext context)
    {
        var body = ReadNodeBody(reader);
        if (body.Length < 5)
        {
            SkipChildren(reader);
            return;
        }
        ushort baseX = BitConverter.ToUInt16(body, 0);
        ushort baseY = BitConverter.ToUInt16(body, 2);
        byte baseZ   = body[4];

        while (reader.TryPeekByte(out var marker) && marker == NODE_START)
        {
            reader.ReadByte();
            var tileType = reader.ReadRequiredByte("OTBM: brak typu kafelka.");
            if (tileType == OTBM_TILE || tileType == OTBM_HOUSETILE)
            {
                ParseTile(reader, baseX, baseY, baseZ, tileType == OTBM_HOUSETILE, map, context);
                ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł kafelka.");
            }
            else
            {
                SkipNode(reader);
            }
        }
    }

    private void ParseTile(OtbmStreamReader reader, ushort baseX, ushort baseY, byte baseZ,
        bool isHouseTile, OtbmMap map, ReadContext context)
    {
        var body = ReadNodeBody(reader);
        if (body.Length < 2)
        {
            SkipChildren(reader);
            return;
        }
        var tile = new OtbmTile
        {
            X = (ushort)(baseX + body[0]),
            Y = (ushort)(baseY + body[1]),
            Z = baseZ,
            IsHouseTile = isHouseTile,
        };

        int bp = 2;
        if (isHouseTile && body.Length >= bp + 4)
        {
            tile.HouseId = BitConverter.ToUInt32(body, bp); bp += 4;
        }
        tile.RawAttributeData = body[bp..];

        while (bp < body.Length)
        {
            var attr = body[bp++];
            switch (attr)
            {
                case OTBM_ATTR_TILE_FLAGS:
                    if (bp + 4 > body.Length) { bp = body.Length; break; }
                    tile.Flags = BitConverter.ToUInt32(body, bp); bp += 4;
                    break;
                case OTBM_ATTR_ITEM:
                    if (bp + 2 > body.Length) { bp = body.Length; break; }
                    tile.GroundItemId = BitConverter.ToUInt16(body, bp); bp += 2;
                    break;
                default:
                    bp = body.Length;
                    break;
            }
        }

        // Dzieci tile'a: OTBM_ITEM*
        while (reader.TryPeekByte(out var marker) && marker == NODE_START)
        {
            reader.ReadByte();
            var childType = reader.ReadRequiredByte("OTBM: brak typu obiektu kafelka.");
            if (childType == OTBM_ITEM)
            {
                var item = ParseItem(reader, map.Version, context);
                if (item is not null) tile.Items.Add(item);
                ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł obiektu kafelka.");
            }
            else
            {
                SkipNode(reader);
            }
        }

        map.Tiles[new OtbmTileCoord(tile.X, tile.Y, tile.Z)] = tile;
        context.TileRead(reader.Position);
    }

    private OtbmItem? ParseItem(OtbmStreamReader reader, uint mapVersion, ReadContext context)
    {
        var body = ReadNodeBody(reader);
        if (body.Length < 2) { SkipChildren(reader); return null; }

        var item = new OtbmItem
        {
            Id = BitConverter.ToUInt16(body, 0),
            RawAttributeData = body[2..]
        };

        int bp = 2;
        // OTBM 1 (wartość wersji 0 w nagłówku) zapisuje subtype bez znacznika
        // OTBM_ATTR_COUNT. O tym, czy dany typ naprawdę używa subtype, decyduje
        // items.otb; bez niego zachowujemy bajt i dzięki RawAttributeData zapisujemy
        // oryginał bez zmian.
        if (mapVersion == 0 && bp < body.Length)
            item.Count = body[bp++];
        while (bp < body.Length)
        {
            var attr = body[bp++];
            switch (attr)
            {
                case OTBM_ATTR_COUNT:
                    if (bp + 1 > body.Length) { bp = body.Length; break; }
                    item.Count = body[bp++];
                    break;
                case OTBM_ATTR_ACTION_ID:
                    if (bp + 2 > body.Length) { bp = body.Length; break; }
                    item.ActionId = BitConverter.ToUInt16(body, bp); bp += 2;
                    break;
                case OTBM_ATTR_UNIQUE_ID:
                    if (bp + 2 > body.Length) { bp = body.Length; break; }
                    item.UniqueId = BitConverter.ToUInt16(body, bp); bp += 2;
                    break;
                case OTBM_ATTR_TEXT:
                    item.Text = ReadString(body, ref bp);
                    break;
                case OTBM_ATTR_DESC:
                    item.Description = ReadString(body, ref bp);
                    break;
                case OTBM_ATTR_TELE_DEST:
                    if (bp + 5 > body.Length) { bp = body.Length; break; }
                    item.TeleportX = BitConverter.ToUInt16(body, bp); bp += 2;
                    item.TeleportY = BitConverter.ToUInt16(body, bp); bp += 2;
                    item.TeleportZ = body[bp++];
                    break;
                case OTBM_ATTR_DEPOT_ID:
                    if (bp + 2 > body.Length) { bp = body.Length; break; }
                    item.DepotId = BitConverter.ToUInt16(body, bp); bp += 2;
                    break;
                case OTBM_ATTR_HOUSEDOORID:
                    if (bp >= body.Length) { bp = body.Length; break; }
                    item.HouseDoorId = body[bp++];
                    break;
                case 12: // OTBM_ATTR_RUNE_CHARGES
                    if (bp >= body.Length) { bp = body.Length; break; }
                    item.RuneCharges = body[bp++];
                    break;
                case OTBM_ATTR_DURATION:
                    if (bp + 4 > body.Length) { bp = body.Length; break; }
                    item.Duration = BitConverter.ToUInt32(body, bp); bp += 4;
                    break;
                case OTBM_ATTR_DECAYING_STATE:
                    if (bp >= body.Length) { bp = body.Length; break; }
                    item.DecayingState = body[bp++];
                    break;
                case OTBM_ATTR_WRITTENDATE:
                    if (bp + 4 > body.Length) { bp = body.Length; break; }
                    item.WrittenDate = BitConverter.ToUInt32(body, bp); bp += 4;
                    break;
                case OTBM_ATTR_WRITTENBY:
                    item.WrittenBy = ReadString(body, ref bp);
                    break;
                case OTBM_ATTR_SLEEPERGUID:
                    if (bp + 4 > body.Length) { bp = body.Length; break; }
                    item.SleeperGuid = BitConverter.ToUInt32(body, bp); bp += 4;
                    break;
                case OTBM_ATTR_SLEEPSTART:
                    if (bp + 4 > body.Length) { bp = body.Length; break; }
                    item.SleepStart = BitConverter.ToUInt32(body, bp); bp += 4;
                    break;
                case OTBM_ATTR_CHARGES:
                    if (bp + 2 > body.Length) { bp = body.Length; break; }
                    item.Charges = BitConverter.ToUInt16(body, bp); bp += 2;
                    break;
                case OTBM_ATTR_PODIUMOUTFIT:
                    if (bp + 15 > body.Length) { bp = body.Length; break; }
                    item.PodiumOutfit = body[bp..(bp + 15)]; bp += 15;
                    break;
                case OTBM_ATTR_TIER:
                    if (bp >= body.Length) { bp = body.Length; break; }
                    item.Tier = body[bp++];
                    break;
                case OTBM_ATTR_ATTRIBUTE_MAP:
                    if (!ReadCustomAttributeMap(body, ref bp, item)) bp = body.Length;
                    break;
                default:
                    bp = body.Length;
                    break;
            }
        }

        // Dzieci item-u (np. zawartość containera)
        while (reader.TryPeekByte(out var marker) && marker == NODE_START)
        {
            reader.ReadByte();
            var childType = reader.ReadRequiredByte("OTBM: brak typu obiektu w kontenerze.");
            if (childType == OTBM_ITEM)
            {
                var child = ParseItem(reader, mapVersion, context);
                if (child is not null) item.Contents.Add(child);
                ConsumeRequiredEnd(reader, "OTBM: niedomknięty obiekt w kontenerze.");
            }
            else
            {
                SkipNode(reader);
            }
        }

        return item;
    }

    private sealed class ReadContext(
        long totalBytes,
        IProgress<OtbmReadProgress>? progress,
        CancellationToken cancellationToken)
    {
        private const int ReportStepPermille = 5;
        private int _lastReportedPermille = -ReportStepPermille;
        private int _tilesRead;

        public void TileRead(long position)
        {
            _tilesRead++;
            // Sprawdzamy anulowanie często, ale obliczenia i callback wykonujemy
            // tylko co 4096 kafelków albo po przejściu kolejnych 0,5% pliku.
            cancellationToken.ThrowIfCancellationRequested();
            if ((_tilesRead & 4095) == 0)
                Report(position);
        }

        public void Report(long position)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (totalBytes <= 0) return;
            var permille = Math.Clamp((int)((long)position * 1000 / totalBytes), 0, 1000);
            if (permille - _lastReportedPermille < ReportStepPermille) return;
            _lastReportedPermille = permille;
            progress?.Report(new OtbmReadProgress(
                permille / 1000d,
                _tilesRead,
                Math.Clamp(position, 0L, totalBytes),
                totalBytes));
        }

        public void Complete(long position)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _lastReportedPermille = 1000;
            progress?.Report(new OtbmReadProgress(1, _tilesRead, position, totalBytes));
        }
    }

    private static bool ReadCustomAttributeMap(byte[] body, ref int bp, OtbmItem item)
    {
        if (bp + 2 > body.Length) return false;
        var count = BitConverter.ToUInt16(body, bp);
        bp += 2;
        for (var index = 0; index < count; index++)
        {
            if (!TryReadString(body, ref bp, out var key) || bp >= body.Length) return false;
            var type = (OtbmCustomAttributeType)body[bp++];
            var attribute = new OtbmCustomAttribute { Key = key, Type = type };
            switch (type)
            {
                case OtbmCustomAttributeType.String:
                    if (!TryReadLongString(body, ref bp, out var text)) return false;
                    attribute.StringValue = text;
                    break;
                case OtbmCustomAttributeType.Integer:
                    if (bp + 4 > body.Length) return false;
                    attribute.IntegerValue = BitConverter.ToInt32(body, bp);
                    bp += 4;
                    break;
                case OtbmCustomAttributeType.Float:
                    if (bp + 4 > body.Length) return false;
                    attribute.FloatValue = BitConverter.ToSingle(body, bp);
                    bp += 4;
                    break;
                case OtbmCustomAttributeType.Boolean:
                    if (bp >= body.Length) return false;
                    attribute.BooleanValue = body[bp++] != 0;
                    break;
                case OtbmCustomAttributeType.Double:
                    if (bp + 8 > body.Length) return false;
                    attribute.DoubleValue = BitConverter.ToDouble(body, bp);
                    bp += 8;
                    break;
                default:
                    return false;
            }

            item.CustomAttributes.RemoveAll(existing => string.Equals(existing.Key, key, StringComparison.Ordinal));
            item.CustomAttributes.Add(attribute);
            ApplyKnownCustomAttribute(item, attribute);
        }
        return true;
    }

    private static void ApplyKnownCustomAttribute(OtbmItem item, OtbmCustomAttribute attribute)
    {
        switch (attribute.Key)
        {
            case "aid" when attribute.Type == OtbmCustomAttributeType.Integer:
                item.ActionId = unchecked((ushort)attribute.IntegerValue);
                break;
            case "uid" when attribute.Type == OtbmCustomAttributeType.Integer:
                item.UniqueId = unchecked((ushort)attribute.IntegerValue);
                break;
            case "text" when attribute.Type == OtbmCustomAttributeType.String:
                item.Text = attribute.StringValue;
                break;
            case "desc" when attribute.Type == OtbmCustomAttributeType.String:
                item.Description = attribute.StringValue;
                break;
            case "tier" when attribute.Type == OtbmCustomAttributeType.Integer:
                item.Tier = unchecked((byte)attribute.IntegerValue);
                break;
        }
    }

    private void ParseTowns(OtbmStreamReader reader, OtbmMap map)
    {
        _ = ReadNodeBody(reader); // brak atrybutów na poziomie kolekcji
        while (reader.TryPeekByte(out var marker) && marker == NODE_START)
        {
            reader.ReadByte();
            var childType = reader.ReadRequiredByte("OTBM: brak typu węzła miasta.");
            if (childType == OTBM_TOWN)
            {
                var body = ReadNodeBody(reader);
                if (body.Length < 4)
                {
                    SkipChildren(reader);
                    ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł miasta.");
                    continue;
                }
                int bp = 0;
                var town = new OtbmTown { Id = BitConverter.ToUInt32(body, bp) };
                bp += 4;
                town.Name = ReadString(body, ref bp);
                if (bp + 5 <= body.Length)
                {
                    town.TempleX = BitConverter.ToUInt16(body, bp); bp += 2;
                    town.TempleY = BitConverter.ToUInt16(body, bp); bp += 2;
                    town.TempleZ = body[bp++];
                }
                map.Towns.Add(town);
                SkipChildren(reader);
                ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł miasta.");
            }
            else
            {
                SkipNode(reader);
            }
        }
    }

    private void ParseWaypoints(OtbmStreamReader reader, OtbmMap map)
    {
        _ = ReadNodeBody(reader);
        while (reader.TryPeekByte(out var marker) && marker == NODE_START)
        {
            reader.ReadByte();
            var childType = reader.ReadRequiredByte("OTBM: brak typu węzła waypointu.");
            if (childType == OTBM_WAYPOINT)
            {
                var body = ReadNodeBody(reader);
                if (body.Length < 2)
                {
                    SkipChildren(reader);
                    ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł waypointu.");
                    continue;
                }
                int bp = 0;
                var wp = new OtbmWaypoint { Name = ReadString(body, ref bp) };
                if (bp + 5 <= body.Length)
                {
                    wp.X = BitConverter.ToUInt16(body, bp); bp += 2;
                    wp.Y = BitConverter.ToUInt16(body, bp); bp += 2;
                    wp.Z = body[bp++];
                }
                map.Waypoints.Add(wp);
                SkipChildren(reader);
                ConsumeRequiredEnd(reader, "OTBM: niedomknięty węzeł waypointu.");
            }
            else
            {
                SkipNode(reader);
            }
        }
    }

    private static string ReadString(byte[] body, ref int bp)
    {
        if (bp + 2 > body.Length) { bp = body.Length; return string.Empty; }
        ushort len = BitConverter.ToUInt16(body, bp);
        bp += 2;
        if (bp + len > body.Length) { bp = body.Length; return string.Empty; }
        var s = System.Text.Encoding.UTF8.GetString(body, bp, len);
        bp += len;
        return s;
    }

    private static bool TryReadString(byte[] body, ref int bp, out string value)
    {
        value = string.Empty;
        if (bp + 2 > body.Length) return false;
        var length = BitConverter.ToUInt16(body, bp);
        bp += 2;
        if (bp + length > body.Length) return false;
        value = System.Text.Encoding.UTF8.GetString(body, bp, length);
        bp += length;
        return true;
    }

    private static bool TryReadLongString(byte[] body, ref int bp, out string value)
    {
        value = string.Empty;
        if (bp + 4 > body.Length) return false;
        var unsignedLength = BitConverter.ToUInt32(body, bp);
        bp += 4;
        if (unsignedLength > int.MaxValue) return false;
        var length = (int)unsignedLength;
        if (length > body.Length - bp) return false;
        value = System.Text.Encoding.UTF8.GetString(body, bp, length);
        bp += length;
        return true;
    }

    private static byte[] ReadNodeBody(OtbmStreamReader reader)
    {
        const int maximumNodeBodyLength = 64 * 1024 * 1024;
        var result = new ArrayBufferWriter<byte>(256);
        while (reader.TryPeekByte(out var marker))
        {
            if (marker == NODE_START || marker == NODE_END) break;
            var b = reader.ReadByte();
            if (b == ESCAPE)
            {
                if (!reader.TryReadByte(out b))
                    throw new InvalidDataException("OTBM: urwany escape w treści węzła.");
            }
            if (result.WrittenCount >= maximumNodeBodyLength)
                throw new InvalidDataException("OTBM: treść pojedynczego węzła przekracza bezpieczny limit 64 MB.");
            result.GetSpan(1)[0] = b;
            result.Advance(1);
        }
        return result.WrittenSpan.ToArray();
    }

    private static void SkipNode(OtbmStreamReader reader)
    {
        // Czytnik jest tuż za typem nodea.
        int depth = 1;
        while (reader.TryReadByte(out var b) && depth > 0)
        {
            if (b == ESCAPE)
            {
                if (!reader.TryReadByte(out _))
                    throw new InvalidDataException("OTBM: urwany escape podczas pomijania węzła.");
            }
            else if (b == NODE_START)
            {
                _ = reader.ReadRequiredByte("OTBM: brak typu zagnieżdżonego węzła.");
                depth++;
            }
            else if (b == NODE_END)   { depth--; }
        }
        if (depth != 0)
            throw new InvalidDataException("OTBM: niedomknięty węzeł.");
    }

    private static void SkipChildren(OtbmStreamReader reader)
    {
        while (reader.TryPeekByte(out var marker) && marker == NODE_START)
        {
            reader.ReadByte(); // NODE_START
            _ = reader.ReadRequiredByte("OTBM: brak typu potomnego węzła.");
            SkipNode(reader);
        }
    }

    /// <summary>
    /// Sekwencyjny, buforowany czytnik surowych bajtów OTBM. Utrzymuje w pamięci
    /// tylko małe okno pliku zamiast drugiej, pełnej kopii dużej mapy.
    /// </summary>
    private sealed class OtbmStreamReader : IDisposable
    {
        private const int BufferSize = 128 * 1024;
        private const long CancellationCheckMask = (64 * 1024) - 1;

        private readonly FileStream _stream;
        private readonly CancellationToken _cancellationToken;
        private byte[]? _buffer;
        private int _bufferOffset;
        private int _bufferCount;

        public OtbmStreamReader(string path, CancellationToken cancellationToken)
        {
            _stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.SequentialScan);
            _cancellationToken = cancellationToken;
            _buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        }

        public long Length => _stream.Length;
        public long Position { get; private set; }

        public uint ReadUInt32()
        {
            Span<byte> value = stackalloc byte[sizeof(uint)];
            for (var index = 0; index < value.Length; index++)
                value[index] = ReadRequiredByte("OTBM: urwany nagłówek pliku.");
            return BinaryPrimitives.ReadUInt32LittleEndian(value);
        }

        public byte ReadByte() =>
            ReadRequiredByte("OTBM: nieoczekiwany koniec pliku.");

        public byte ReadRequiredByte(string errorMessage) =>
            TryReadByte(out var value)
                ? value
                : throw new InvalidDataException(errorMessage);

        public bool TryReadByte(out byte value)
        {
            if (!EnsureBuffered())
            {
                value = 0;
                return false;
            }

            value = _buffer![_bufferOffset++];
            Position++;
            if ((Position & CancellationCheckMask) == 0)
                _cancellationToken.ThrowIfCancellationRequested();
            return true;
        }

        public bool TryPeekByte(out byte value)
        {
            if (!EnsureBuffered())
            {
                value = 0;
                return false;
            }

            value = _buffer![_bufferOffset];
            return true;
        }

        private bool EnsureBuffered()
        {
            if (_bufferOffset < _bufferCount) return true;
            _cancellationToken.ThrowIfCancellationRequested();
            if (_buffer is null) throw new ObjectDisposedException(nameof(OtbmStreamReader));
            _bufferCount = _stream.Read(_buffer, 0, _buffer.Length);
            _bufferOffset = 0;
            return _bufferCount > 0;
        }

        public void Dispose()
        {
            var buffer = Interlocked.Exchange(ref _buffer, null);
            if (buffer is not null)
                ArrayPool<byte>.Shared.Return(buffer);
            _stream.Dispose();
        }
    }
}

/// <summary>Rzeczywisty postęp odczytu binarnego strumienia OTBM.</summary>
public readonly record struct OtbmReadProgress(
    double Fraction,
    int TilesRead,
    long BytesRead,
    long TotalBytes);
