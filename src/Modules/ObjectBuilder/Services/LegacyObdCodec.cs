using System.Text;
using Narzedzia.Core.Compression;
using Narzedzia.Core.Interchange;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.ObjectBuilder.Services;

public sealed record LegacyObdDocument(
    ushort ObdVersion,
    ushort ClientVersion,
    DatThingType Thing,
    IReadOnlyList<byte[][]> SpritePixelsByGroup)
{
    public bool UsesFrameGroups => ObdVersion >= 300 && Thing.Category == DatThingCategory.Outfits;
}

/// <summary>
/// Odczyt i zapis OBD v2/v3 zgodny z Object Builderem. Kodek może zachować
/// właściwości obiektu albo ograniczyć transfer do układu, sprite'ów i offsetu.
/// </summary>
public sealed class LegacyObdCodec
{
    private const ushort ObdVersion1 = 100;
    private const ushort ObdVersion2 = 200;
    private const ushort ObdVersion3 = 300;
    private const ushort ExportClientVersion = 1098;
    private const int SpriteByteCount = 32 * 32 * 4;
    private const int MaxSpritesPerObject = 250_000;

    public LegacyObdDocument Read(string path) => Read(path, preserveFlags: true);

    public LegacyObdDocument Read(string path, bool preserveFlags)
    {
        var compressed = File.ReadAllBytes(path);
        var data = LzmaDataCodec.Decompress(compressed);
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: false);

        var obdVersion = reader.ReadUInt16();
        if (obdVersion >= 710)
        {
            return ReadVersion1(reader, obdVersion, preserveFlags);
        }

        if (obdVersion is not (ObdVersion2 or ObdVersion3))
        {
            throw new InvalidDataException($"Nieobsługiwana wersja OBD: {obdVersion}.");
        }

        var clientVersion = reader.ReadUInt16();
        var categoryRaw = reader.ReadByte();
        if (categoryRaw is < 1 or > 4)
        {
            throw new InvalidDataException($"Nieprawidłowa kategoria OBD: {categoryRaw}.");
        }

        var category = (DatThingCategory)(categoryRaw - 1);
        var texturePosition = reader.ReadUInt32();
        if (texturePosition > stream.Length)
        {
            throw new InvalidDataException("Pozycja tekstur OBD wskazuje poza plik.");
        }

        var thing = new DatThingType { Category = category };
        ReadProperties(reader, thing);

        var groupCount = obdVersion == ObdVersion3 && category == DatThingCategory.Outfits
            ? reader.ReadByte()
            : 1;
        if (groupCount is < 1 or > 8)
        {
            throw new InvalidDataException($"Nieprawidłowa liczba grup klatek OBD: {groupCount}.");
        }

        var pixelsByGroup = new List<byte[][]>(groupCount);
        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var group = new DatThingFrameGroup();
            if (obdVersion == ObdVersion3 && category == DatThingCategory.Outfits)
            {
                group.GroupType = reader.ReadByte();
            }

            ReadFrameGroup(reader, group);
            var total = group.TotalSprites;
            if (total > MaxSpritesPerObject)
            {
                throw new InvalidDataException($"OBD deklaruje zbyt wiele sprite'ów: {total:N0}.");
            }

            group.SpriteIds = new uint[total];
            var groupPixels = new byte[total][];
            for (var index = 0; index < total; index++)
            {
                group.SpriteIds[index] = reader.ReadUInt32();
                var dataSize = obdVersion == ObdVersion3 ? checked((int)reader.ReadUInt32()) : SpriteByteCount;
                if (dataSize is < 0 or > SpriteByteCount)
                {
                    throw new InvalidDataException($"Nieprawidłowy rozmiar sprite'a OBD: {dataSize}.");
                }

                var argb = ReadExact(reader, dataSize, "piksele sprite'a OBD");
                if (argb.Length < SpriteByteCount)
                {
                    Array.Resize(ref argb, SpriteByteCount);
                }

                groupPixels[index] = ConvertArgbToBgra(argb);
            }

            thing.FrameGroups.Add(group);
            pixelsByGroup.Add(groupPixels);
        }

        return new LegacyObdDocument(
            obdVersion,
            clientVersion,
            preserveFlags ? thing : ObjectTransferProfile.Create(thing),
            pixelsByGroup);
    }

    private static LegacyObdDocument ReadVersion1(
        BinaryReader reader,
        ushort clientVersion,
        bool preserveFlags)
    {
        var categoryLength = reader.ReadUInt16();
        var categoryName = Encoding.Latin1.GetString(
            ReadExact(reader, categoryLength, "kategoria OBD v1"));
        var category = categoryName switch
        {
            "item" => DatThingCategory.Items,
            "outfit" => DatThingCategory.Outfits,
            "effect" => DatThingCategory.Effects,
            "missile" => DatThingCategory.Missiles,
            _ => throw new InvalidDataException($"Nieprawidłowa kategoria OBD v1: {categoryName}.")
        };

        var thing = new DatThingType { Category = category };
        DatParser.ReadThingProperties(
            reader,
            LegacyObjectBuilderProtocolCatalog.GetMetadataFormat(clientVersion),
            thing);

        var group = new DatThingFrameGroup();
        ReadFrameGroup(reader, group, readAnimationMetadata: false);
        if (group.Frames > 1)
        {
            var duration = GetObjectBuilderDefaultDuration(category);
            group.FrameDurations = Enumerable.Range(0, group.Frames)
                .Select(_ => new DatFrameDuration { Min = duration, Max = duration })
                .ToArray();
        }

        var total = group.TotalSprites;
        if (total > MaxSpritesPerObject)
        {
            throw new InvalidDataException($"OBD v1 deklaruje zbyt wiele sprite'ów: {total:N0}.");
        }

        group.SpriteIds = new uint[total];
        var groupPixels = new byte[total][];
        for (var index = 0; index < total; index++)
        {
            group.SpriteIds[index] = reader.ReadUInt32();
            var dataSize = checked((int)reader.ReadUInt32());
            if (dataSize is < 0 or > SpriteByteCount)
            {
                throw new InvalidDataException($"Nieprawidłowy rozmiar sprite'a OBD v1: {dataSize}.");
            }

            var argb = ReadExact(reader, dataSize, "piksele sprite'a OBD v1");
            if (argb.Length < SpriteByteCount)
            {
                Array.Resize(ref argb, SpriteByteCount);
            }

            groupPixels[index] = ConvertArgbToBgra(argb);
        }

        if (reader.BaseStream.Position != reader.BaseStream.Length)
        {
            throw new InvalidDataException(
                $"Po odczycie OBD v1 zostało {reader.BaseStream.Length - reader.BaseStream.Position} nieprzetworzonych bajtów.");
        }

        thing.FrameGroups.Add(group);
        return new LegacyObdDocument(
            ObdVersion1,
            clientVersion,
            preserveFlags ? thing : ObjectTransferProfile.Create(thing),
            [groupPixels]);
    }

    public void Write(
        string path,
        DatThingType thing,
        LegacySpriteStore sprites,
        bool useFrameGroups = true,
        bool preserveFlags = true)
    {
        ArgumentNullException.ThrowIfNull(sprites);
        Write(path, thing, sprites.GetSpritePixels, useFrameGroups, preserveFlags);
    }

    public void Write(
        string path,
        DatThingType thing,
        Func<uint, byte[]?> readSpritePixels,
        bool useFrameGroups = true,
        bool preserveFlags = true)
    {
        ArgumentNullException.ThrowIfNull(thing);
        ArgumentNullException.ThrowIfNull(readSpritePixels);
        if (!preserveFlags) thing = ObjectTransferProfile.Create(thing);

        var obdVersion = thing.Category == DatThingCategory.Outfits && useFrameGroups
            ? ObdVersion3
            : ObdVersion2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Latin1, leaveOpen: true);

        writer.Write(obdVersion);
        writer.Write(ExportClientVersion);
        writer.Write((byte)((int)thing.Category + 1));
        var texturePositionOffset = stream.Position;
        writer.Write(0u);
        WriteProperties(writer, thing);

        var texturePosition = checked((uint)stream.Position);
        var afterProperties = stream.Position;
        stream.Position = texturePositionOffset;
        writer.Write(texturePosition);
        stream.Position = afterProperties;

        IReadOnlyList<DatThingFrameGroup> groups = thing.FrameGroups.Count > 0
            ? thing.FrameGroups
            : [CreateDefaultGroup()];

        if (obdVersion == ObdVersion3)
        {
            if (groups.Count > byte.MaxValue) throw new InvalidDataException("OBD może zawierać najwyżej 255 grup klatek.");
            writer.Write((byte)groups.Count);
        }
        else
        {
            groups = [groups[0]];
        }

        foreach (var group in groups)
        {
            if (obdVersion == ObdVersion3) writer.Write((byte)group.GroupType);
            WriteFrameGroup(writer, group);

            var total = group.TotalSprites;
            for (var index = 0; index < total; index++)
            {
                var spriteId = index < group.SpriteIds.Length ? group.SpriteIds[index] : 0u;
                var bgra = readSpritePixels(spriteId) ?? new byte[SpriteByteCount];
                var argb = ConvertBgraToArgb(bgra);
                writer.Write(spriteId);
                if (obdVersion == ObdVersion3) writer.Write((uint)argb.Length);
                writer.Write(argb);
            }
        }

        writer.Flush();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, LzmaDataCodec.Compress(stream.ToArray()));
    }

    private static void ReadProperties(BinaryReader reader, DatThingType thing)
    {
        while (true)
        {
            var flag = reader.ReadByte();
            switch (flag)
            {
                case 0xFF: return;
                case 0x00: thing.IsGround = true; thing.GroundSpeed = reader.ReadUInt16(); break;
                case 0x01: thing.IsGroundBorder = true; break;
                case 0x02: thing.IsOnBottom = true; break;
                case 0x03: thing.IsOnTop = true; break;
                case 0x04: thing.IsContainer = true; break;
                case 0x05: thing.IsStackable = true; break;
                case 0x06: thing.ForceUse = true; break;
                case 0x07: thing.IsMultiUse = true; break;
                case 0x08: thing.IsWritable = true; thing.MaxReadWriteChars = reader.ReadUInt16(); break;
                case 0x09: thing.IsWritableOnce = true; thing.MaxReadChars = reader.ReadUInt16(); break;
                case 0x0A: thing.IsFluidContainer = true; break;
                case 0x0B: thing.IsFluid = true; break;
                case 0x0C: thing.IsUnpassable = true; break;
                case 0x0D: thing.IsUnmoveable = true; break;
                case 0x0E: thing.BlockMissile = true; break;
                case 0x0F: thing.BlockPathfinder = true; break;
                case 0x10: thing.NoMoveAnimation = true; break;
                case 0x11: thing.IsPickupable = true; break;
                case 0x12: thing.IsHangable = true; break;
                case 0x13: thing.IsVertical = true; break;
                case 0x14: thing.IsHorizontal = true; break;
                case 0x15: thing.IsRotatable = true; break;
                case 0x16: thing.HasLight = true; thing.LightLevel = reader.ReadUInt16(); thing.LightColor = reader.ReadUInt16(); break;
                case 0x17: thing.DontHide = true; break;
                case 0x18: thing.IsTranslucent = true; break;
                case 0x19: thing.HasOffset = true; thing.OffsetX = reader.ReadInt16(); thing.OffsetY = reader.ReadInt16(); break;
                case 0x1A: thing.HasElevation = true; thing.Elevation = reader.ReadUInt16(); break;
                case 0x1B: thing.IsLyingObject = true; break;
                case 0x1C: thing.AnimateAlways = true; break;
                case 0x1D: thing.HasMiniMapColor = true; thing.MiniMapColor = reader.ReadUInt16(); break;
                case 0x1E: thing.HasLensHelp = true; thing.LensHelp = reader.ReadUInt16(); break;
                case 0x1F: thing.IsFullGround = true; break;
                case 0x20: thing.IgnoreLook = true; break;
                case 0x21: thing.HasCloth = true; thing.ClothSlot = reader.ReadUInt16(); break;
                case 0x22: ReadMarket(reader, thing); break;
                case 0x23: thing.HasDefaultAction = true; thing.DefaultAction = reader.ReadUInt16(); break;
                case 0x24: thing.IsWrappable = true; break;
                case 0x25: thing.IsUnwrappable = true; break;
                case 0x26: thing.IsTopEffect = true; break;
                case 0xFC: thing.HasCharges = true; break;
                case 0xFD: thing.FloorChange = true; break;
                case 0xFE: thing.IsUsable = true; break;
                default: throw new InvalidDataException($"Nieznana flaga OBD: 0x{flag:X2}.");
            }
        }
    }

    private static void WriteProperties(BinaryWriter writer, DatThingType thing)
    {
        if (thing.IsGround) { writer.Write((byte)0x00); writer.Write(thing.GroundSpeed); }
        else if (thing.IsGroundBorder) writer.Write((byte)0x01);
        else if (thing.IsOnBottom) writer.Write((byte)0x02);
        else if (thing.IsOnTop) writer.Write((byte)0x03);

        WriteFlag(writer, 0x04, thing.IsContainer);
        WriteFlag(writer, 0x05, thing.IsStackable);
        WriteFlag(writer, 0x06, thing.ForceUse);
        WriteFlag(writer, 0x07, thing.IsMultiUse);
        if (thing.IsWritable) { writer.Write((byte)0x08); writer.Write(thing.MaxReadWriteChars); }
        if (thing.IsWritableOnce) { writer.Write((byte)0x09); writer.Write(thing.MaxReadChars); }
        WriteFlag(writer, 0x0A, thing.IsFluidContainer);
        WriteFlag(writer, 0x0B, thing.IsFluid);
        WriteFlag(writer, 0x0C, thing.IsUnpassable);
        WriteFlag(writer, 0x0D, thing.IsUnmoveable);
        WriteFlag(writer, 0x0E, thing.BlockMissile);
        WriteFlag(writer, 0x0F, thing.BlockPathfinder);
        WriteFlag(writer, 0x10, thing.NoMoveAnimation);
        WriteFlag(writer, 0x11, thing.IsPickupable);
        WriteFlag(writer, 0x12, thing.IsHangable);
        WriteFlag(writer, 0x13, thing.IsVertical);
        WriteFlag(writer, 0x14, thing.IsHorizontal);
        WriteFlag(writer, 0x15, thing.IsRotatable);
        if (thing.HasLight) { writer.Write((byte)0x16); writer.Write(thing.LightLevel); writer.Write(thing.LightColor); }
        WriteFlag(writer, 0x17, thing.DontHide);
        WriteFlag(writer, 0x18, thing.IsTranslucent);
        if (thing.HasOffset) { writer.Write((byte)0x19); writer.Write(thing.OffsetX); writer.Write(thing.OffsetY); }
        if (thing.HasElevation) { writer.Write((byte)0x1A); writer.Write(thing.Elevation); }
        WriteFlag(writer, 0x1B, thing.IsLyingObject);
        WriteFlag(writer, 0x1C, thing.AnimateAlways);
        if (thing.HasMiniMapColor) { writer.Write((byte)0x1D); writer.Write(thing.MiniMapColor); }
        if (thing.HasLensHelp) { writer.Write((byte)0x1E); writer.Write(thing.LensHelp); }
        WriteFlag(writer, 0x1F, thing.IsFullGround);
        WriteFlag(writer, 0x20, thing.IgnoreLook);
        if (thing.HasCloth) { writer.Write((byte)0x21); writer.Write(thing.ClothSlot); }
        if (thing.HasMarketInfo) { writer.Write((byte)0x22); WriteMarket(writer, thing); }
        if (thing.HasDefaultAction) { writer.Write((byte)0x23); writer.Write(thing.DefaultAction); }
        WriteFlag(writer, 0x24, thing.IsWrappable);
        WriteFlag(writer, 0x25, thing.IsUnwrappable);
        WriteFlag(writer, 0x26, thing.IsTopEffect);
        WriteFlag(writer, 0xFC, thing.HasCharges);
        WriteFlag(writer, 0xFD, thing.FloorChange);
        WriteFlag(writer, 0xFE, thing.IsUsable);
        writer.Write((byte)0xFF);
    }

    private static void ReadFrameGroup(
        BinaryReader reader,
        DatThingFrameGroup group,
        bool readAnimationMetadata = true)
    {
        group.Width = ReadDimension(reader, "szerokość");
        group.Height = ReadDimension(reader, "wysokość");
        group.ExactSize = group.Width > 1 || group.Height > 1 ? reader.ReadByte() : (byte)32;
        group.Layers = ReadDimension(reader, "warstwy");
        group.PatternX = ReadDimension(reader, "pattern X");
        group.PatternY = ReadDimension(reader, "pattern Y");
        group.PatternZ = ReadDimension(reader, "pattern Z");
        group.Frames = ReadDimension(reader, "klatki");

        if (group.Frames <= 1 || !readAnimationMetadata) return;
        group.AnimationMode = reader.ReadByte();
        group.LoopCount = reader.ReadInt32();
        group.StartFrame = reader.ReadSByte();
        group.FrameDurations = new DatFrameDuration[group.Frames];
        for (var index = 0; index < group.Frames; index++)
        {
            group.FrameDurations[index] = new DatFrameDuration
            {
                Min = reader.ReadUInt32(),
                Max = reader.ReadUInt32()
            };
        }
    }

    private static void WriteFrameGroup(BinaryWriter writer, DatThingFrameGroup group)
    {
        var width = NormalizeDimension(group.Width);
        var height = NormalizeDimension(group.Height);
        var frames = NormalizeDimension(group.Frames);
        writer.Write(width);
        writer.Write(height);
        if (width > 1 || height > 1) writer.Write(group.ExactSize);
        writer.Write(NormalizeDimension(group.Layers));
        writer.Write(NormalizeDimension(group.PatternX));
        writer.Write(NormalizeDimension(group.PatternY));
        writer.Write(NormalizeDimension(group.PatternZ));
        writer.Write(frames);

        if (frames <= 1) return;
        writer.Write(group.AnimationMode);
        writer.Write(group.LoopCount);
        writer.Write(group.StartFrame);
        for (var index = 0; index < frames; index++)
        {
            var duration = index < group.FrameDurations.Length
                ? group.FrameDurations[index]
                : new DatFrameDuration { Min = 100, Max = 100 };
            writer.Write(duration.Min);
            writer.Write(duration.Max);
        }
    }

    private static void ReadMarket(BinaryReader reader, DatThingType thing)
    {
        thing.HasMarketInfo = true;
        thing.MarketCategory = reader.ReadUInt16();
        thing.MarketTradeAs = reader.ReadUInt16();
        thing.MarketShowAs = reader.ReadUInt16();
        var length = reader.ReadUInt16();
        thing.MarketName = Encoding.Latin1.GetString(ReadExact(reader, length, "nazwa rynku OBD"));
        thing.MarketRestrictProfession = reader.ReadUInt16();
        thing.MarketRestrictLevel = reader.ReadUInt16();
    }

    private static void WriteMarket(BinaryWriter writer, DatThingType thing)
    {
        writer.Write(thing.MarketCategory);
        writer.Write(thing.MarketTradeAs);
        writer.Write(thing.MarketShowAs);
        var name = Encoding.Latin1.GetBytes(thing.MarketName ?? string.Empty);
        if (name.Length > ushort.MaxValue) throw new InvalidDataException("Nazwa rynku w OBD jest za długa.");
        writer.Write((ushort)name.Length);
        writer.Write(name);
        writer.Write(thing.MarketRestrictProfession);
        writer.Write(thing.MarketRestrictLevel);
    }

    private static byte[] ReadExact(BinaryReader reader, int count, string section)
    {
        var data = reader.ReadBytes(count);
        if (data.Length != count) throw new EndOfStreamException($"Niepełna sekcja: {section}.");
        return data;
    }

    private static byte ReadDimension(BinaryReader reader, string field)
    {
        var value = reader.ReadByte();
        return value == 0 ? throw new InvalidDataException($"Pole {field} w OBD ma wartość 0.") : value;
    }

    private static byte NormalizeDimension(byte value) => value == 0 ? (byte)1 : value;
    private static void WriteFlag(BinaryWriter writer, byte flag, bool enabled) { if (enabled) writer.Write(flag); }

    private static uint GetObjectBuilderDefaultDuration(DatThingCategory category) => category switch
    {
        DatThingCategory.Items => 500,
        DatThingCategory.Outfits => 300,
        DatThingCategory.Effects => 100,
        DatThingCategory.Missiles => 75,
        _ => 0
    };

    private static byte[] ConvertArgbToBgra(byte[] argb)
    {
        var bgra = new byte[SpriteByteCount];
        for (var offset = 0; offset < SpriteByteCount; offset += 4)
        {
            bgra[offset] = argb[offset + 3];
            bgra[offset + 1] = argb[offset + 2];
            bgra[offset + 2] = argb[offset + 1];
            bgra[offset + 3] = argb[offset];
        }
        return bgra;
    }

    private static byte[] ConvertBgraToArgb(byte[] bgra)
    {
        if (bgra.Length != SpriteByteCount) throw new InvalidDataException("Nieprawidłowy rozmiar kafla BGRA.");
        var argb = new byte[SpriteByteCount];
        for (var offset = 0; offset < SpriteByteCount; offset += 4)
        {
            argb[offset] = bgra[offset + 3];
            argb[offset + 1] = bgra[offset + 2];
            argb[offset + 2] = bgra[offset + 1];
            argb[offset + 3] = bgra[offset];
        }
        return argb;
    }

    private static DatThingFrameGroup CreateDefaultGroup() => new()
    {
        Width = 1,
        Height = 1,
        ExactSize = 32,
        Layers = 1,
        PatternX = 1,
        PatternY = 1,
        PatternZ = 1,
        Frames = 1,
        SpriteIds = [0]
    };
}
