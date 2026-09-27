using System.Text;
using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

/// <summary>
/// Układ flag właściwości w pliku Tibia.dat. Kolejne wersje klienta zmieniały
/// numery flag, mimo że ogólny układ pliku pozostawał podobny.
/// </summary>
public enum DatMetadataFormat
{
    Versions710To730 = 1,
    Versions740To750 = 2,
    Versions755To772 = 3,
    Versions780To854 = 4,
    Versions855To986 = 5,
    Versions1010AndNewer = 6
}

public static class DatMetadataFormatExtensions
{
    public static string ToDisplayName(this DatMetadataFormat format) => format switch
    {
        DatMetadataFormat.Versions710To730 => "7.10–7.30",
        DatMetadataFormat.Versions740To750 => "7.40–7.50",
        DatMetadataFormat.Versions755To772 => "7.55–7.72",
        DatMetadataFormat.Versions780To854 => "7.80–8.54",
        DatMetadataFormat.Versions855To986 => "8.55–9.86",
        DatMetadataFormat.Versions1010AndNewer => "10.10+",
        _ => format.ToString()
    };
}

/// <summary>Opcje parsowania pliku .dat, dobierane do wersji klienta Tibia.</summary>
public sealed class DatParserOptions
{
    /// <summary>Sprite ID są 32-bitowe zamiast klasycznych 16-bitowych.</summary>
    public bool ExtendedSprites { get; init; } = true;

    /// <summary>Plik zawiera tryb, pętle i czasy klatek animacji.</summary>
    public bool ImprovedAnimations { get; init; } = true;

    /// <summary>Outfity mają osobne grupy klatek, np. Idle i Walking.</summary>
    public bool FrameGroups { get; init; }

    /// <summary>Wersja układu flag właściwości DAT.</summary>
    public DatMetadataFormat MetadataFormat { get; init; } = DatMetadataFormat.Versions1010AndNewer;

    /// <summary>
    /// Pozwala zwrócić częściowo wczytany plik po nieoczekiwanym końcu danych.
    /// Autodetekcja zawsze używa ścisłego odczytu i nie zwraca częściowego wyniku.
    /// </summary>
    public bool AllowPartial { get; init; }

    public static DatParserOptions Default => new();
    public static DatParserOptions WithFrameGroups => new() { FrameGroups = true };
}

public sealed record DatAutoParseResult(DatFile File, DatParserOptions Options);

/// <summary>
/// Parser klasycznych plików Tibia.dat. Implementuje formaty MetadataReader1–6
/// z oryginalnego projektu Object Builder.
/// </summary>
public static class DatParser
{
    private const uint MinItemId = 100;
    private const uint MinOtherId = 1;
    private const int MaxFrameGroups = 16;
    private const int MaxSpritesPerThing = 65_536;

    public static DatFile Parse(string filePath, DatParserOptions? options = null)
    {
        options ??= DatParserOptions.Default;
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);
        return ParseInternal(reader, options);
    }

    public static DatFile Parse(Stream stream, DatParserOptions? options = null)
    {
        options ??= DatParserOptions.Default;
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return ParseInternal(reader, options);
    }

    /// <summary>
    /// Odczytuje sam blok flag DAT. Jest używany także przez OBD v1, które
    /// zapisuje właściwości dokładnie w układzie metadanych wersji klienta.
    /// </summary>
    public static void ReadThingProperties(
        BinaryReader reader,
        DatMetadataFormat format,
        DatThingType thing)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(thing);
        ReadFlags(reader, format, thing);
    }

    /// <summary>
    /// Rozpoznaje układ flag i trzy funkcje klienta. Podane opcje są próbą
    /// preferowaną, a nie wymuszeniem — parser sprawdza też pozostałe warianty.
    /// </summary>
    public static DatAutoParseResult ParseAuto(string filePath, DatParserOptions? preferred = null)
    {
        preferred ??= DatParserOptions.Default;

        uint signature;
        using (var stream = File.OpenRead(filePath))
        using (var reader = new BinaryReader(stream))
        {
            if (stream.Length < 12)
            {
                throw new InvalidDataException("Plik jest za mały, aby był prawidłowym Tibia.dat.");
            }

            signature = reader.ReadUInt32();
        }

        if (TryGetEncryptionMarker(signature, out var encryptionMarker))
        {
            throw new InvalidDataException(
                $"Plik Tibia.dat jest zaszyfrowany ({encryptionMarker}). Do odczytu potrzebny " +
                "jest klucz lub odszyfrowany plik z tego samego klienta.");
        }

        var likelyFormat = GuessMetadataFormat(signature);
        var formats = Enum.GetValues<DatMetadataFormat>()
            .OrderBy(format => format == likelyFormat ? 0 : 1)
            .ThenByDescending(format => (int)format)
            .ToArray();

        var featureVariants = BuildFeatureVariants(preferred).ToArray();
        Exception? mostUsefulError = null;
        long farthestPosition = -1;

        foreach (var format in formats)
        {
            foreach (var (extended, improved, groups) in featureVariants)
            {
                var options = new DatParserOptions
                {
                    ExtendedSprites = extended,
                    ImprovedAnimations = improved,
                    FrameGroups = groups,
                    MetadataFormat = format,
                    AllowPartial = false
                };

                try
                {
                    using var stream = File.OpenRead(filePath);
                    var file = Parse(stream, options);
                    if (stream.Position != stream.Length)
                    {
                        throw new InvalidDataException(
                            $"Po odczycie zostało {stream.Length - stream.Position} nieprzetworzonych bajtów.");
                    }

                    return new DatAutoParseResult(file, options);
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or OverflowException)
                {
                    var position = ExtractPosition(ex.Message);
                    if (position >= farthestPosition)
                    {
                        farthestPosition = position;
                        mostUsefulError = ex;
                    }
                }
            }
        }

        throw new InvalidDataException(
            "Nie udało się automatycznie rozpoznać formatu Tibia.dat. " +
            "Sprawdzono wszystkie formaty Object Buildera i kombinacje opcji. " +
            "Jeżeli plik pochodzi z chronionego klienta, potrzebny jest odszyfrowany DAT " +
            "lub jego algorytm i klucz. " +
            $"Najdokładniejszy błąd: {mostUsefulError?.Message ?? "brak szczegółów"}",
            mostUsefulError);
    }

    private static bool TryGetEncryptionMarker(uint signature, out string marker)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BitConverter.TryWriteBytes(bytes, signature);
        marker = string.Empty;
        if (bytes[0] != (byte)'E' || bytes[1] != (byte)'N' || bytes[2] != (byte)'C')
        {
            return false;
        }

        var suffix = (char)bytes[3];
        if (!char.IsAsciiLetterOrDigit(suffix))
        {
            return false;
        }

        marker = $"ENC{suffix}";
        return true;
    }

    public static DatMetadataFormat GuessMetadataFormat(uint signature)
    {
        // Klienty 10.71+ zapisują w polu sygnatury małą wartość zamiast unix timestampu.
        if (signature < 0x0010_0000)
        {
            return DatMetadataFormat.Versions1010AndNewer;
        }

        if (signature <= 0x411A_6233) return DatMetadataFormat.Versions710To730;
        if (signature <= 0x42F8_1973) return DatMetadataFormat.Versions740To750;
        if (signature <= 0x439D_5A33) return DatMetadataFormat.Versions755To772;
        if (signature <= 0x4B28_B89E) return DatMetadataFormat.Versions780To854;
        if (signature <= 0x5170_E904) return DatMetadataFormat.Versions855To986;
        return DatMetadataFormat.Versions1010AndNewer;
    }

    private static IEnumerable<(bool Extended, bool Improved, bool Groups)> BuildFeatureVariants(
        DatParserOptions preferred)
    {
        return from extended in new[] { false, true }
               from improved in new[] { false, true }
               from groups in new[] { false, true }
               orderby DifferenceCount(extended, improved, groups, preferred)
               select (extended, improved, groups);
    }

    private static int DifferenceCount(bool extended, bool improved, bool groups, DatParserOptions preferred) =>
        (extended == preferred.ExtendedSprites ? 0 : 1) +
        (improved == preferred.ImprovedAnimations ? 0 : 1) +
        (groups == preferred.FrameGroups ? 0 : 1);

    private static long ExtractPosition(string message)
    {
        const string marker = "pozycja=";
        var start = message.LastIndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return -1;
        }

        start += marker.Length;
        var end = start;
        while (end < message.Length && char.IsDigit(message[end]))
        {
            end++;
        }

        return long.TryParse(message[start..end], out var position) ? position : -1;
    }

    private static DatFile ParseInternal(BinaryReader reader, DatParserOptions options)
    {
        var dat = new DatFile
        {
            Signature = reader.ReadUInt32(),
            ItemsMaxId = reader.ReadUInt16(),
            OutfitsMaxId = reader.ReadUInt16(),
            EffectsMaxId = reader.ReadUInt16(),
            MissilesMaxId = reader.ReadUInt16()
        };

        for (uint id = MinItemId; id <= dat.ItemsMaxId; id++)
        {
            if (!TryReadThing(reader, options, dat, DatThingCategory.Items, id, out var thing)) return dat;
            thing.Id = id;
            dat.Items.Add(thing);
        }

        for (uint id = MinOtherId; id <= dat.OutfitsMaxId; id++)
        {
            if (!TryReadThing(reader, options, dat, DatThingCategory.Outfits, id, out var thing)) return dat;
            thing.Id = id;
            dat.Outfits.Add(thing);
        }

        for (uint id = MinOtherId; id <= dat.EffectsMaxId; id++)
        {
            if (!TryReadThing(reader, options, dat, DatThingCategory.Effects, id, out var thing)) return dat;
            thing.Id = id;
            dat.Effects.Add(thing);
        }

        for (uint id = MinOtherId; id <= dat.MissilesMaxId; id++)
        {
            if (!TryReadThing(reader, options, dat, DatThingCategory.Missiles, id, out var thing)) return dat;
            thing.Id = id;
            dat.Missiles.Add(thing);
        }

        return dat;
    }

    private static bool TryReadThing(
        BinaryReader reader,
        DatParserOptions options,
        DatFile dat,
        DatThingCategory category,
        uint id,
        out DatThingType thing)
    {
        try
        {
            thing = ReadThingWithContext(reader, options, category, id);
            return true;
        }
        catch (InvalidDataException ex) when (options.AllowPartial && ex.InnerException is EndOfStreamException)
        {
            dat.ParseWarning = ex.Message;
            thing = new DatThingType { Category = category, Id = id };
            return false;
        }
    }

    private static DatThingType ReadThingWithContext(
        BinaryReader reader,
        DatParserOptions options,
        DatThingCategory category,
        uint id)
    {
        var start = reader.BaseStream.Position;
        try
        {
            return ReadThing(reader, options, category);
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or OverflowException)
        {
            throw new InvalidDataException(
                $"Błąd odczytu DAT: kategoria={category}, id={id}, start={start}, " +
                $"pozycja={reader.BaseStream.Position}, format={options.MetadataFormat.ToDisplayName()}, " +
                $"opcje ext={options.ExtendedSprites}, anim={options.ImprovedAnimations}, groups={options.FrameGroups}. {ex.Message}",
                ex);
        }
    }

    private static DatThingType ReadThing(
        BinaryReader reader,
        DatParserOptions options,
        DatThingCategory category)
    {
        var thing = new DatThingType { Category = category };
        ReadFlags(reader, options.MetadataFormat, thing);

        var groupCount = 1;
        if (options.FrameGroups && category == DatThingCategory.Outfits)
        {
            groupCount = reader.ReadByte();
            if (groupCount is < 1 or > MaxFrameGroups)
            {
                throw new InvalidDataException($"Nieprawidłowa liczba frame groups: {groupCount}.");
            }
        }

        for (var groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            var group = new DatThingFrameGroup();
            if (options.FrameGroups && category == DatThingCategory.Outfits)
            {
                group.GroupType = reader.ReadByte();
            }

            ReadTexturePatterns(reader, options, group);
            thing.FrameGroups.Add(group);
        }

        return thing;
    }

    private static void ReadFlags(BinaryReader reader, DatMetadataFormat format, DatThingType thing)
    {
        while (true)
        {
            var rawFlag = reader.ReadByte();
            if (rawFlag == 0xFF)
            {
                return;
            }

            switch (GetFlagKind(format, rawFlag))
            {
                case FlagKind.Ground:
                    thing.IsGround = true;
                    thing.GroundSpeed = reader.ReadUInt16();
                    break;
                case FlagKind.GroundBorder: thing.IsGroundBorder = true; break;
                case FlagKind.OnBottom: thing.IsOnBottom = true; break;
                case FlagKind.OnTop: thing.IsOnTop = true; break;
                case FlagKind.Container: thing.IsContainer = true; break;
                case FlagKind.Stackable: thing.IsStackable = true; break;
                case FlagKind.ForceUse: thing.ForceUse = true; break;
                case FlagKind.MultiUse: thing.IsMultiUse = true; break;
                case FlagKind.HasCharges: thing.HasCharges = true; break;
                case FlagKind.Writable:
                    thing.IsWritable = true;
                    thing.MaxReadWriteChars = reader.ReadUInt16();
                    break;
                case FlagKind.WritableOnce:
                    thing.IsWritableOnce = true;
                    thing.MaxReadChars = reader.ReadUInt16();
                    break;
                case FlagKind.FluidContainer: thing.IsFluidContainer = true; break;
                case FlagKind.Fluid: thing.IsFluid = true; break;
                case FlagKind.Unpassable: thing.IsUnpassable = true; break;
                case FlagKind.Unmoveable: thing.IsUnmoveable = true; break;
                case FlagKind.BlockMissile: thing.BlockMissile = true; break;
                case FlagKind.BlockPathfinder: thing.BlockPathfinder = true; break;
                case FlagKind.NoMoveAnimation: thing.NoMoveAnimation = true; break;
                case FlagKind.Pickupable: thing.IsPickupable = true; break;
                case FlagKind.Hangable: thing.IsHangable = true; break;
                case FlagKind.Vertical: thing.IsVertical = true; break;
                case FlagKind.Horizontal: thing.IsHorizontal = true; break;
                case FlagKind.Rotatable: thing.IsRotatable = true; break;
                case FlagKind.Light:
                    thing.HasLight = true;
                    thing.LightLevel = reader.ReadUInt16();
                    thing.LightColor = reader.ReadUInt16();
                    break;
                case FlagKind.DontHide: thing.DontHide = true; break;
                case FlagKind.Translucent: thing.IsTranslucent = true; break;
                case FlagKind.FloorChange: thing.FloorChange = true; break;
                case FlagKind.OffsetFixed:
                    thing.HasOffset = true;
                    thing.OffsetX = 8;
                    thing.OffsetY = 8;
                    break;
                case FlagKind.Offset:
                    thing.HasOffset = true;
                    thing.OffsetX = reader.ReadInt16();
                    thing.OffsetY = reader.ReadInt16();
                    break;
                case FlagKind.Elevation:
                    thing.HasElevation = true;
                    thing.Elevation = reader.ReadUInt16();
                    break;
                case FlagKind.LyingObject: thing.IsLyingObject = true; break;
                case FlagKind.AnimateAlways: thing.AnimateAlways = true; break;
                case FlagKind.MiniMap:
                    thing.HasMiniMapColor = true;
                    thing.MiniMapColor = reader.ReadUInt16();
                    break;
                case FlagKind.LensHelp:
                    thing.HasLensHelp = true;
                    thing.LensHelp = reader.ReadUInt16();
                    break;
                case FlagKind.FullGround: thing.IsFullGround = true; break;
                case FlagKind.IgnoreLook: thing.IgnoreLook = true; break;
                case FlagKind.Cloth:
                    thing.HasCloth = true;
                    thing.ClothSlot = reader.ReadUInt16();
                    break;
                case FlagKind.Market:
                    ReadMarket(reader, thing);
                    break;
                case FlagKind.DefaultAction:
                    thing.HasDefaultAction = true;
                    thing.DefaultAction = reader.ReadUInt16();
                    break;
                case FlagKind.Wrappable: thing.IsWrappable = true; break;
                case FlagKind.Unwrappable: thing.IsUnwrappable = true; break;
                case FlagKind.TopEffect: thing.IsTopEffect = true; break;
                case FlagKind.Bones:
                    thing.HasBones = true;
                    thing.BoneOffsets = Enumerable.Range(0, 8).Select(_ => reader.ReadInt16()).ToArray();
                    break;
                case FlagKind.Usable: thing.IsUsable = true; break;
                default:
                    throw new InvalidDataException(
                        $"Nieznana flaga DAT 0x{rawFlag:X2} dla formatu {format.ToDisplayName()} " +
                        $"na pozycji {reader.BaseStream.Position - 1}.");
            }
        }
    }

    private static void ReadMarket(BinaryReader reader, DatThingType thing)
    {
        thing.HasMarketInfo = true;
        thing.MarketCategory = reader.ReadUInt16();
        thing.MarketTradeAs = reader.ReadUInt16();
        thing.MarketShowAs = reader.ReadUInt16();
        var nameLength = reader.ReadUInt16();
        var nameBytes = reader.ReadBytes(nameLength);
        if (nameBytes.Length != nameLength)
        {
            throw new EndOfStreamException("Niepełna nazwa market itemu.");
        }

        thing.MarketName = Encoding.Latin1.GetString(nameBytes);
        thing.MarketRestrictProfession = reader.ReadUInt16();
        thing.MarketRestrictLevel = reader.ReadUInt16();
    }

    private static void ReadTexturePatterns(
        BinaryReader reader,
        DatParserOptions options,
        DatThingFrameGroup group)
    {
        // Object Builder zachowuje również techniczne, puste rekordy z zerowym
        // wymiarem. Ich iloczyn sprite'ów wynosi zero; nie mogą rozsunąć odczytu.
        group.Width = reader.ReadByte();
        group.Height = reader.ReadByte();
        group.ExactSize = group.Width > 1 || group.Height > 1 ? reader.ReadByte() : (byte)32;
        group.Layers = reader.ReadByte();
        group.PatternX = reader.ReadByte();
        group.PatternY = reader.ReadByte();
        group.PatternZ = options.MetadataFormat is DatMetadataFormat.Versions710To730 or DatMetadataFormat.Versions740To750
            ? (byte)1
            : reader.ReadByte();
        group.Frames = reader.ReadByte();

        if (options.ImprovedAnimations && group.Frames > 1)
        {
            EnsureRemaining(reader, 6L + group.Frames * 8L, "dane animacji");
            group.AnimationMode = reader.ReadByte();
            group.LoopCount = reader.ReadInt32();
            group.StartFrame = reader.ReadSByte();
            group.FrameDurations = new DatFrameDuration[group.Frames];
            for (var i = 0; i < group.Frames; i++)
            {
                group.FrameDurations[i] = new DatFrameDuration
                {
                    Min = reader.ReadUInt32(),
                    Max = reader.ReadUInt32()
                };
            }
        }

        var total = checked(
            (int)group.Width * group.Height * group.Layers * group.PatternX *
            group.PatternY * group.PatternZ * group.Frames);
        if (total > MaxSpritesPerThing)
        {
            throw new InvalidDataException($"Obiekt deklaruje zbyt wiele sprite'ów: {total}.");
        }

        var bytesPerSprite = options.ExtendedSprites ? 4 : 2;
        EnsureRemaining(reader, (long)total * bytesPerSprite, "ID sprite'ów");
        group.SpriteIds = new uint[total];
        for (var index = 0; index < total; index++)
        {
            group.SpriteIds[index] = options.ExtendedSprites
                ? reader.ReadUInt32()
                : reader.ReadUInt16();
        }
    }

    private static void EnsureRemaining(BinaryReader reader, long required, string section)
    {
        if (required < 0 || reader.BaseStream.Length - reader.BaseStream.Position < required)
        {
            throw new EndOfStreamException($"Brakuje danych dla sekcji: {section}.");
        }
    }

    internal static FlagKind GetFlagKind(DatMetadataFormat format, byte flag) => format switch
    {
        DatMetadataFormat.Versions710To730 => flag switch
        {
            0x00 => FlagKind.Ground, 0x01 => FlagKind.OnBottom, 0x02 => FlagKind.OnTop,
            0x03 => FlagKind.Container, 0x04 => FlagKind.Stackable, 0x05 => FlagKind.MultiUse,
            0x06 => FlagKind.ForceUse, 0x07 => FlagKind.Writable, 0x08 => FlagKind.WritableOnce,
            0x09 => FlagKind.FluidContainer, 0x0A => FlagKind.Fluid, 0x0B => FlagKind.Unpassable,
            0x0C => FlagKind.Unmoveable, 0x0D => FlagKind.BlockMissile, 0x0E => FlagKind.BlockPathfinder,
            0x0F => FlagKind.Pickupable, 0x10 => FlagKind.Light, 0x11 => FlagKind.FloorChange,
            0x12 => FlagKind.FullGround, 0x13 => FlagKind.Elevation, 0x14 => FlagKind.OffsetFixed,
            0x16 => FlagKind.MiniMap, 0x17 => FlagKind.Rotatable, 0x18 => FlagKind.LyingObject,
            0x19 => FlagKind.AnimateAlways, 0x1A => FlagKind.LensHelp, 0x24 => FlagKind.Wrappable,
            0x25 => FlagKind.Unwrappable, 0x26 => FlagKind.TopEffect,
            _ => FlagKind.Unknown
        },
        DatMetadataFormat.Versions740To750 => flag switch
        {
            0x00 => FlagKind.Ground, 0x01 => FlagKind.OnBottom, 0x02 => FlagKind.OnTop,
            0x03 => FlagKind.Container, 0x04 => FlagKind.Stackable, 0x05 => FlagKind.MultiUse,
            0x06 => FlagKind.ForceUse, 0x07 => FlagKind.Writable, 0x08 => FlagKind.WritableOnce,
            0x09 => FlagKind.FluidContainer, 0x0A => FlagKind.Fluid, 0x0B => FlagKind.Unpassable,
            0x0C => FlagKind.Unmoveable, 0x0D => FlagKind.BlockMissile, 0x0E => FlagKind.BlockPathfinder,
            0x0F => FlagKind.Pickupable, 0x10 => FlagKind.Light, 0x11 => FlagKind.FloorChange,
            0x12 => FlagKind.FullGround, 0x13 => FlagKind.Elevation, 0x14 => FlagKind.OffsetFixed,
            0x16 => FlagKind.MiniMap, 0x17 => FlagKind.Rotatable, 0x18 => FlagKind.LyingObject,
            0x19 => FlagKind.Hangable, 0x1A => FlagKind.Vertical, 0x1B => FlagKind.Horizontal,
            0x1C => FlagKind.AnimateAlways, 0x1D => FlagKind.LensHelp, 0x24 => FlagKind.Wrappable,
            0x25 => FlagKind.Unwrappable, 0x26 => FlagKind.TopEffect,
            _ => FlagKind.Unknown
        },
        DatMetadataFormat.Versions755To772 => flag switch
        {
            0x00 => FlagKind.Ground, 0x01 => FlagKind.GroundBorder, 0x02 => FlagKind.OnBottom,
            0x03 => FlagKind.OnTop, 0x04 => FlagKind.Container, 0x05 => FlagKind.Stackable,
            0x06 => FlagKind.ForceUse, 0x07 => FlagKind.MultiUse, 0x08 => FlagKind.Writable,
            0x09 => FlagKind.WritableOnce, 0x0A => FlagKind.FluidContainer, 0x0B => FlagKind.Fluid,
            0x0C => FlagKind.Unpassable, 0x0D => FlagKind.Unmoveable, 0x0E => FlagKind.BlockMissile,
            0x0F => FlagKind.BlockPathfinder, 0x10 => FlagKind.Pickupable, 0x11 => FlagKind.Hangable,
            0x12 => FlagKind.Vertical, 0x13 => FlagKind.Horizontal, 0x14 => FlagKind.Rotatable,
            0x15 => FlagKind.Light, 0x17 => FlagKind.FloorChange, 0x18 => FlagKind.Offset,
            0x19 => FlagKind.Elevation, 0x1A => FlagKind.LyingObject, 0x1B => FlagKind.AnimateAlways,
            0x1C => FlagKind.MiniMap, 0x1D => FlagKind.LensHelp, 0x1E => FlagKind.FullGround,
            _ => FlagKind.Unknown
        },
        DatMetadataFormat.Versions780To854 => flag switch
        {
            0x00 => FlagKind.Ground, 0x01 => FlagKind.GroundBorder, 0x02 => FlagKind.OnBottom,
            0x03 => FlagKind.OnTop, 0x04 => FlagKind.Container, 0x05 => FlagKind.Stackable,
            0x06 => FlagKind.ForceUse, 0x07 => FlagKind.MultiUse, 0x08 => FlagKind.HasCharges,
            0x09 => FlagKind.Writable, 0x0A => FlagKind.WritableOnce, 0x0B => FlagKind.FluidContainer,
            0x0C => FlagKind.Fluid, 0x0D => FlagKind.Unpassable, 0x0E => FlagKind.Unmoveable,
            0x0F => FlagKind.BlockMissile, 0x10 => FlagKind.BlockPathfinder, 0x11 => FlagKind.Pickupable,
            0x12 => FlagKind.Hangable, 0x13 => FlagKind.Vertical, 0x14 => FlagKind.Horizontal,
            0x15 => FlagKind.Rotatable, 0x16 => FlagKind.Light, 0x17 => FlagKind.DontHide,
            0x18 => FlagKind.FloorChange, 0x19 => FlagKind.Offset, 0x1A => FlagKind.Elevation,
            0x1B => FlagKind.LyingObject, 0x1C => FlagKind.AnimateAlways, 0x1D => FlagKind.MiniMap,
            0x1E => FlagKind.LensHelp, 0x1F => FlagKind.FullGround, 0x20 => FlagKind.IgnoreLook,
            0x24 => FlagKind.Wrappable, 0x25 => FlagKind.Unwrappable, 0x27 => FlagKind.Bones,
            _ => FlagKind.Unknown
        },
        DatMetadataFormat.Versions855To986 => flag switch
        {
            0x00 => FlagKind.Ground, 0x01 => FlagKind.GroundBorder, 0x02 => FlagKind.OnBottom,
            0x03 => FlagKind.OnTop, 0x04 => FlagKind.Container, 0x05 => FlagKind.Stackable,
            0x06 => FlagKind.ForceUse, 0x07 => FlagKind.MultiUse, 0x08 => FlagKind.Writable,
            0x09 => FlagKind.WritableOnce, 0x0A => FlagKind.FluidContainer, 0x0B => FlagKind.Fluid,
            0x0C => FlagKind.Unpassable, 0x0D => FlagKind.Unmoveable, 0x0E => FlagKind.BlockMissile,
            0x0F => FlagKind.BlockPathfinder, 0x10 => FlagKind.Pickupable, 0x11 => FlagKind.Hangable,
            0x12 => FlagKind.Vertical, 0x13 => FlagKind.Horizontal, 0x14 => FlagKind.Rotatable,
            0x15 => FlagKind.Light, 0x16 => FlagKind.DontHide, 0x17 => FlagKind.Translucent,
            0x18 => FlagKind.Offset, 0x19 => FlagKind.Elevation, 0x1A => FlagKind.LyingObject,
            0x1B => FlagKind.AnimateAlways, 0x1C => FlagKind.MiniMap, 0x1D => FlagKind.LensHelp,
            0x1E => FlagKind.FullGround, 0x1F => FlagKind.IgnoreLook, 0x20 => FlagKind.Cloth,
            0x21 => FlagKind.Market, 0x27 => FlagKind.Bones,
            _ => FlagKind.Unknown
        },
        DatMetadataFormat.Versions1010AndNewer => flag switch
        {
            0x00 => FlagKind.Ground, 0x01 => FlagKind.GroundBorder, 0x02 => FlagKind.OnBottom,
            0x03 => FlagKind.OnTop, 0x04 => FlagKind.Container, 0x05 => FlagKind.Stackable,
            0x06 => FlagKind.ForceUse, 0x07 => FlagKind.MultiUse, 0x08 => FlagKind.Writable,
            0x09 => FlagKind.WritableOnce, 0x0A => FlagKind.FluidContainer, 0x0B => FlagKind.Fluid,
            0x0C => FlagKind.Unpassable, 0x0D => FlagKind.Unmoveable, 0x0E => FlagKind.BlockMissile,
            0x0F => FlagKind.BlockPathfinder, 0x10 => FlagKind.NoMoveAnimation, 0x11 => FlagKind.Pickupable,
            0x12 => FlagKind.Hangable, 0x13 => FlagKind.Vertical, 0x14 => FlagKind.Horizontal,
            0x15 => FlagKind.Rotatable, 0x16 => FlagKind.Light, 0x17 => FlagKind.DontHide,
            0x18 => FlagKind.Translucent, 0x19 => FlagKind.Offset, 0x1A => FlagKind.Elevation,
            0x1B => FlagKind.LyingObject, 0x1C => FlagKind.AnimateAlways, 0x1D => FlagKind.MiniMap,
            0x1E => FlagKind.LensHelp, 0x1F => FlagKind.FullGround, 0x20 => FlagKind.IgnoreLook,
            0x21 => FlagKind.Cloth, 0x22 => FlagKind.Market, 0x23 => FlagKind.DefaultAction,
            0x24 => FlagKind.Wrappable, 0x25 => FlagKind.Unwrappable, 0x26 => FlagKind.TopEffect,
            0x27 => FlagKind.Bones, 0xFE => FlagKind.Usable,
            _ => FlagKind.Unknown
        },
        _ => FlagKind.Unknown
    };

    internal enum FlagKind
    {
        Unknown,
        Ground,
        GroundBorder,
        OnBottom,
        OnTop,
        Container,
        Stackable,
        ForceUse,
        MultiUse,
        HasCharges,
        Writable,
        WritableOnce,
        FluidContainer,
        Fluid,
        Unpassable,
        Unmoveable,
        BlockMissile,
        BlockPathfinder,
        NoMoveAnimation,
        Pickupable,
        Hangable,
        Vertical,
        Horizontal,
        Rotatable,
        Light,
        DontHide,
        Translucent,
        FloorChange,
        OffsetFixed,
        Offset,
        Elevation,
        LyingObject,
        AnimateAlways,
        MiniMap,
        LensHelp,
        FullGround,
        IgnoreLook,
        Cloth,
        Market,
        DefaultAction,
        Wrappable,
        Unwrappable,
        TopEffect,
        Bones,
        Usable
    }

    public static int CalculateSpriteIndex(
        DatThingFrameGroup group,
        int tileX,
        int tileY,
        int layer,
        int patternX,
        int patternY,
        int patternZ,
        int frame)
    {
        var safeWidth = Math.Max(1, (int)group.Width);
        var safeHeight = Math.Max(1, (int)group.Height);
        var safeLayers = Math.Max(1, (int)group.Layers);
        var safePatternX = Math.Max(1, (int)group.PatternX);
        var safePatternY = Math.Max(1, (int)group.PatternY);
        var safePatternZ = Math.Max(1, (int)group.PatternZ);
        var safeFrames = Math.Max(1, (int)group.Frames);

        var safeTileX = Math.Clamp(tileX, 0, safeWidth - 1);
        var safeTileY = Math.Clamp(tileY, 0, safeHeight - 1);
        var safeLayer = Math.Clamp(layer, 0, safeLayers - 1);
        var safeX = Math.Clamp(patternX, 0, safePatternX - 1);
        var safeY = Math.Clamp(patternY, 0, safePatternY - 1);
        var safeZ = Math.Clamp(patternZ, 0, safePatternZ - 1);
        var safeFrame = ((frame % safeFrames) + safeFrames) % safeFrames;

        var index = safeFrame;
        index = index * safePatternZ + safeZ;
        index = index * safePatternY + safeY;
        index = index * safePatternX + safeX;
        index = index * safeLayers + safeLayer;
        index = index * safeHeight + safeTileY;
        index = index * safeWidth + safeTileX;
        return index;
    }
}
