using System.Buffers.Binary;
using System.Text;
using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

/// <summary>
/// Odczyt i zapis klasycznego OpenTibia Binary items.otb. Format jest drzewem
/// węzłów, w którym bajty sterujące 0xFD-0xFF są poprzedzane znakiem ucieczki.
/// </summary>
public sealed class OtbParser
{
    private const byte NodeStart = 0xFE;
    private const byte NodeEnd = 0xFF;
    private const byte Escape = 0xFD;
    private const byte RootAttributeVersion = 0x01;

    private const byte AttributeServerId = 0x10;
    private const byte AttributeClientId = 0x11;
    private const byte AttributeName = 0x12;
    private const byte AttributeGroundSpeed = 0x14;
    private const byte AttributeSpriteHash = 0x20;
    private const byte AttributeMinimapColor = 0x21;
    private const byte AttributeMaxReadWriteChars = 0x22;
    private const byte AttributeMaxReadChars = 0x23;
    private const byte AttributeLight = 0x2A;
    private const byte AttributeStackOrder = 0x2B;
    private const byte AttributeTradeAs = 0x2D;
    private const byte LegacyMikaschiAttributeName = 0x16;
    private const byte LegacyMikaschiAttributeGroundSpeed = 0x1A;

    private static readonly HashSet<byte> KnownItemAttributes =
    [
        AttributeServerId,
        AttributeClientId,
        AttributeName,
        AttributeGroundSpeed,
        AttributeSpriteHash,
        AttributeMinimapColor,
        AttributeMaxReadWriteChars,
        AttributeMaxReadChars,
        AttributeLight,
        AttributeStackOrder,
        AttributeTradeAs
    ];

    public OtbFile Parse(string filePath)
    {
        var raw = File.ReadAllBytes(filePath);
        if (raw.Length < 7)
        {
            throw new InvalidDataException("Plik jest za mały, aby był prawidłowym items.otb.");
        }

        var position = 4; // Czterobajtowy identyfikator pliku; historycznie zawsze 0.
        var root = ReadNode(raw, ref position);
        if (root.Type != 0)
        {
            throw new InvalidDataException($"Nieprawidłowy typ głównego węzła OTB: 0x{root.Type:X2}.");
        }

        var file = ParseRoot(root.Data, out var legacyMikaschiEncoding);
        foreach (var child in root.Children)
        {
            file.Items.Add(ParseItemNode(child.Type, child.Data, legacyMikaschiEncoding));
        }

        return file;
    }

    public void Save(OtbFile file, string filePath)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Ścieżka OTB nie może być pusta.", nameof(filePath));
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0u);
        writer.Write(NodeStart);
        writer.Write((byte)0);

        var rootData = new List<byte>(148);
        rootData.AddRange(BitConverter.GetBytes(0u));
        rootData.Add(RootAttributeVersion);
        rootData.AddRange(BitConverter.GetBytes((ushort)140));
        rootData.AddRange(BitConverter.GetBytes(file.MajorVersion));
        rootData.AddRange(BitConverter.GetBytes(file.MinorVersion));
        rootData.AddRange(BitConverter.GetBytes(file.BuildNumber));
        var description = new byte[128];
        var descriptionBytes = Encoding.ASCII.GetBytes(file.Description ?? string.Empty);
        Buffer.BlockCopy(descriptionBytes, 0, description, 0, Math.Min(description.Length, descriptionBytes.Length));
        rootData.AddRange(description);
        WriteEscaped(writer, rootData);

        foreach (var item in file.Items)
        {
            WriteItemNode(writer, item);
        }

        writer.Write(NodeEnd);
        writer.Flush();
        File.WriteAllBytes(filePath, stream.ToArray());
    }

    private static OtbFile ParseRoot(byte[] data, out bool legacyMikaschiEncoding)
    {
        legacyMikaschiEncoding = false;
        if (data.Length < 4)
        {
            throw new InvalidDataException("Główny węzeł OTB nie zawiera pola flags.");
        }

        var file = new OtbFile();
        var position = 4;
        if (position + 3 <= data.Length && data[position] == RootAttributeVersion)
        {
            position++;
            var length = ReadUInt16(data, ref position, "długość nagłówka wersji");
            if (length < 12 || position + length > data.Length)
            {
                throw new InvalidDataException($"Nieprawidłowa długość nagłówka wersji OTB: {length}.");
            }

            var end = position + length;
            file.MajorVersion = ReadUInt32(data, ref position, "major version");
            file.MinorVersion = ReadUInt32(data, ref position, "minor version");
            file.BuildNumber = ReadUInt32(data, ref position, "build number");
            if (position < end)
            {
                file.Description = Encoding.ASCII.GetString(data, position, end - position).TrimEnd('\0');
            }

            return file;
        }

        // Zgodność z krótkotrwałym, niekanonicznym zapisem starszej wersji
        // Mikaschi Studio Tools: flags + trzy liczby + 128 bajtów opisu.
        if (data.Length >= 16)
        {
            legacyMikaschiEncoding = true;
            file.MajorVersion = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4));
            file.MinorVersion = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));
            file.BuildNumber = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12, 4));
            if (data.Length > 16)
            {
                file.Description = Encoding.ASCII.GetString(data, 16, Math.Min(128, data.Length - 16)).TrimEnd('\0');
            }

            return file;
        }

        throw new InvalidDataException("Brak atrybutu wersji w głównym węźle OTB.");
    }

    private static OtbItem ParseItemNode(byte nodeType, byte[] body, bool legacyMikaschiEncoding)
    {
        if (body.Length < 4)
        {
            throw new InvalidDataException($"Węzeł itemu typu 0x{nodeType:X2} nie zawiera pola flags.");
        }

        var item = new OtbItem
        {
            ItemType = legacyMikaschiEncoding
                ? nodeType switch
                {
                    1 => OtbItemType.Ground,
                    2 => OtbItemType.Container,
                    3 => OtbItemType.Fluid,
                    4 => OtbItemType.Splash,
                    5 => OtbItemType.Deprecated,
                    _ => OtbItemType.None
                }
                : nodeType switch
            {
                1 => OtbItemType.Ground,
                2 => OtbItemType.Container,
                11 => OtbItemType.Splash,
                12 => OtbItemType.Fluid,
                14 => OtbItemType.Deprecated,
                _ => OtbItemType.None
            },
            Flags = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0, 4))
        };

        var position = 4;
        while (position < body.Length)
        {
            var attribute = body[position++];
            if (position + 2 > body.Length)
            {
                throw new InvalidDataException($"Ucięta długość atrybutu 0x{attribute:X2} itemu {item.ServerId}.");
            }

            var length = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(position, 2));
            position += 2;
            if (position + length > body.Length)
            {
                throw new InvalidDataException($"Ucięty atrybut 0x{attribute:X2} itemu {item.ServerId}.");
            }

            var value = body.AsSpan(position, length);
            position += length;
            switch (attribute)
            {
                case AttributeServerId when length >= 2:
                    item.ServerId = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    break;
                case AttributeClientId when length >= 2:
                    item.ClientId = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    break;
                case AttributeName:
                    item.Name = Encoding.UTF8.GetString(value).TrimEnd('\0');
                    break;
                case LegacyMikaschiAttributeName when legacyMikaschiEncoding:
                    item.Name = Encoding.Latin1.GetString(value).TrimEnd('\0');
                    break;
                case AttributeGroundSpeed when length >= 2:
                    item.Speed = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    break;
                case LegacyMikaschiAttributeGroundSpeed when legacyMikaschiEncoding && length >= 2:
                    item.Speed = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    break;
                case AttributeSpriteHash:
                    item.SpriteHash = value.ToArray();
                    break;
                case AttributeMinimapColor when length >= 2:
                    item.MinimapColor = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    break;
                case AttributeMaxReadWriteChars when length >= 2:
                    item.MaxReadWriteChars = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    break;
                case AttributeMaxReadChars when length >= 2:
                    item.MaxReadChars = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    break;
                case AttributeLight when length >= 4:
                    item.LightLevel = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    item.LightColor = BinaryPrimitives.ReadUInt16LittleEndian(value[2..]);
                    break;
                case AttributeStackOrder when length >= 1:
                    item.StackOrder = value[0];
                    break;
                case AttributeTradeAs when length >= 2:
                    item.TradeAs = BinaryPrimitives.ReadUInt16LittleEndian(value);
                    break;
                default:
                    item.RawAttributes[attribute] = value.ToArray();
                    break;
            }
        }

        if (item.ItemType != OtbItemType.Deprecated && item.SpriteHash.Length == 0)
        {
            item.SpriteHash = new byte[16];
        }

        return item;
    }

    private static void WriteItemNode(BinaryWriter writer, OtbItem item)
    {
        writer.Write(NodeStart);
        writer.Write(item.ItemType switch
        {
            OtbItemType.Ground => (byte)1,
            OtbItemType.Container => (byte)2,
            OtbItemType.Splash => (byte)11,
            OtbItemType.Fluid => (byte)12,
            OtbItemType.Deprecated => (byte)14,
            _ => (byte)0
        });

        var body = new List<byte>(96);
        body.AddRange(BitConverter.GetBytes(item.Flags));
        AddAttribute(body, AttributeServerId, BitConverter.GetBytes(item.ServerId));

        if (item.ItemType != OtbItemType.Deprecated)
        {
            AddAttribute(body, AttributeClientId, BitConverter.GetBytes(item.ClientId));
            AddAttribute(body, AttributeSpriteHash,
                item.SpriteHash is { Length: > 0 } ? item.SpriteHash : new byte[16]);

            if (item.MinimapColor != 0)
                AddAttribute(body, AttributeMinimapColor, BitConverter.GetBytes(item.MinimapColor));
            if (item.MaxReadWriteChars != 0)
                AddAttribute(body, AttributeMaxReadWriteChars, BitConverter.GetBytes(item.MaxReadWriteChars));
            if (item.MaxReadChars != 0)
                AddAttribute(body, AttributeMaxReadChars, BitConverter.GetBytes(item.MaxReadChars));
            if (item.LightLevel != 0 || item.LightColor != 0)
            {
                AddAttribute(body, AttributeLight,
                    BitConverter.GetBytes(item.LightLevel).Concat(BitConverter.GetBytes(item.LightColor)).ToArray());
            }
            if (item.ItemType == OtbItemType.Ground)
                AddAttribute(body, AttributeGroundSpeed, BitConverter.GetBytes(item.Speed));
            if (item.StackOrder != 0)
                AddAttribute(body, AttributeStackOrder, [item.StackOrder]);
            if (item.TradeAs != 0)
                AddAttribute(body, AttributeTradeAs, BitConverter.GetBytes(item.TradeAs));
            if (!string.IsNullOrEmpty(item.Name))
                AddAttribute(body, AttributeName, Encoding.UTF8.GetBytes(item.Name));

            foreach (var (attribute, value) in item.RawAttributes)
            {
                if (!KnownItemAttributes.Contains(attribute))
                {
                    AddAttribute(body, attribute, value);
                }
            }
        }

        WriteEscaped(writer, body);
        writer.Write(NodeEnd);
    }

    private static void AddAttribute(List<byte> body, byte attribute, byte[] value)
    {
        if (value.Length > ushort.MaxValue)
        {
            throw new InvalidDataException($"Atrybut 0x{attribute:X2} przekracza limit 65535 bajtów.");
        }

        body.Add(attribute);
        body.AddRange(BitConverter.GetBytes((ushort)value.Length));
        body.AddRange(value);
    }

    private static OtbNode ReadNode(byte[] raw, ref int position)
    {
        if (position >= raw.Length || raw[position++] != NodeStart)
        {
            throw new InvalidDataException($"Oczekiwano początku węzła OTB na pozycji {position - 1}.");
        }

        var type = ReadEscapedByte(raw, ref position);
        var data = new List<byte>();
        var children = new List<OtbNode>();
        while (position < raw.Length)
        {
            var value = raw[position++];
            switch (value)
            {
                case Escape:
                    if (position >= raw.Length)
                        throw new InvalidDataException("Plik OTB kończy się po znaku ucieczki.");
                    data.Add(raw[position++]);
                    break;
                case NodeStart:
                    position--;
                    children.Add(ReadNode(raw, ref position));
                    break;
                case NodeEnd:
                    return new OtbNode(type, data.ToArray(), children);
                default:
                    data.Add(value);
                    break;
            }
        }

        throw new InvalidDataException("Niezamknięty węzeł w pliku OTB.");
    }

    private static byte ReadEscapedByte(byte[] raw, ref int position)
    {
        if (position >= raw.Length)
            throw new InvalidDataException("Nieoczekiwany koniec pliku OTB.");
        var value = raw[position++];
        if (value != Escape) return value;
        if (position >= raw.Length)
            throw new InvalidDataException("Plik OTB kończy się po znaku ucieczki.");
        return raw[position++];
    }

    private static ushort ReadUInt16(byte[] data, ref int position, string field)
    {
        if (position + 2 > data.Length)
            throw new InvalidDataException($"Ucięte pole {field} w pliku OTB.");
        var value = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position, 2));
        position += 2;
        return value;
    }

    private static uint ReadUInt32(byte[] data, ref int position, string field)
    {
        if (position + 4 > data.Length)
            throw new InvalidDataException($"Ucięte pole {field} w pliku OTB.");
        var value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position, 4));
        position += 4;
        return value;
    }

    private static void WriteEscaped(BinaryWriter writer, IEnumerable<byte> data)
    {
        foreach (var value in data)
        {
            if (value is NodeStart or NodeEnd or Escape)
                writer.Write(Escape);
            writer.Write(value);
        }
    }

    private sealed record OtbNode(byte Type, byte[] Data, IReadOnlyList<OtbNode> Children);
}
