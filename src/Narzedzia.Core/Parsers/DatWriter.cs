using System.Text;
using Narzedzia.Core.Models;

namespace Narzedzia.Core.Parsers;

/// <summary>Zapisuje wszystkie formaty DAT obsługiwane przez DatParser.</summary>
public static class DatWriter
{
    private static readonly IReadOnlyDictionary<(DatMetadataFormat Format, DatParser.FlagKind Kind), byte> FlagValues =
        BuildFlagValues();

    public static void Write(string filePath, DatFile file, DatParserOptions? options = null)
    {
        using var stream = File.Create(filePath);
        Write(stream, file, options);
    }

    public static void Write(Stream stream, DatFile file, DatParserOptions? options = null)
    {
        options ??= DatParserOptions.Default;
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(file.Signature);
        writer.Write(file.ItemsMaxId);
        writer.Write(file.OutfitsMaxId);
        writer.Write(file.EffectsMaxId);
        writer.Write(file.MissilesMaxId);

        WriteThings(writer, file.Items, options, DatThingCategory.Items);
        WriteThings(writer, file.Outfits, options, DatThingCategory.Outfits);
        WriteThings(writer, file.Effects, options, DatThingCategory.Effects);
        WriteThings(writer, file.Missiles, options, DatThingCategory.Missiles);
    }

    private static void WriteThings(
        BinaryWriter writer,
        IReadOnlyList<DatThingType> things,
        DatParserOptions options,
        DatThingCategory category)
    {
        foreach (var thing in things)
        {
            WriteFlags(writer, thing, options.MetadataFormat);

            IReadOnlyList<DatThingFrameGroup> groups;
            if (thing.FrameGroups.Count == 0)
            {
                groups = [CreateMinimalGroup()];
            }
            else if (options.FrameGroups && category == DatThingCategory.Outfits)
            {
                groups = thing.FrameGroups;
            }
            else
            {
                groups = [thing.FrameGroups[0]];
            }

            if (options.FrameGroups && category == DatThingCategory.Outfits)
            {
                if (groups.Count > byte.MaxValue)
                {
                    throw new InvalidDataException("Liczba frame groups przekracza 255.");
                }

                writer.Write((byte)groups.Count);
            }

            foreach (var group in groups)
            {
                if (options.FrameGroups && category == DatThingCategory.Outfits)
                {
                    writer.Write((byte)group.GroupType);
                }

                WriteTexturePatterns(writer, group, options);
            }
        }
    }

    private static DatThingFrameGroup CreateMinimalGroup() => new()
    {
        Width = 1,
        Height = 1,
        Layers = 1,
        PatternX = 1,
        PatternY = 1,
        PatternZ = 1,
        Frames = 1,
        SpriteIds = [0u]
    };

    private static void WriteFlags(BinaryWriter writer, DatThingType thing, DatMetadataFormat format)
    {
        var flags = new List<(byte Value, Action Payload)>();

        void Add(DatParser.FlagKind kind, bool enabled, Action? payload = null)
        {
            if (!enabled)
            {
                return;
            }

            var rawFlag = FindRawFlag(format, kind);
            if (rawFlag is byte value)
            {
                flags.Add((value, payload ?? (() => { })));
            }
        }

        Add(DatParser.FlagKind.Ground, thing.IsGround, () => writer.Write(thing.GroundSpeed));
        Add(DatParser.FlagKind.GroundBorder, thing.IsGroundBorder);
        Add(DatParser.FlagKind.OnBottom, thing.IsOnBottom);
        Add(DatParser.FlagKind.OnTop, thing.IsOnTop);
        Add(DatParser.FlagKind.Container, thing.IsContainer);
        Add(DatParser.FlagKind.Stackable, thing.IsStackable);
        Add(DatParser.FlagKind.ForceUse, thing.ForceUse);
        Add(DatParser.FlagKind.MultiUse, thing.IsMultiUse);
        Add(DatParser.FlagKind.HasCharges, thing.HasCharges);
        Add(DatParser.FlagKind.Writable, thing.IsWritable, () => writer.Write(thing.MaxReadWriteChars));
        Add(DatParser.FlagKind.WritableOnce, thing.IsWritableOnce, () => writer.Write(thing.MaxReadChars));
        Add(DatParser.FlagKind.FluidContainer, thing.IsFluidContainer);
        Add(DatParser.FlagKind.Fluid, thing.IsFluid);
        Add(DatParser.FlagKind.Unpassable, thing.IsUnpassable);
        Add(DatParser.FlagKind.Unmoveable, thing.IsUnmoveable);
        Add(DatParser.FlagKind.BlockMissile, thing.BlockMissile);
        Add(DatParser.FlagKind.BlockPathfinder, thing.BlockPathfinder);
        Add(DatParser.FlagKind.NoMoveAnimation, thing.NoMoveAnimation);
        Add(DatParser.FlagKind.Pickupable, thing.IsPickupable);
        Add(DatParser.FlagKind.Hangable, thing.IsHangable);
        Add(DatParser.FlagKind.Vertical, thing.IsVertical);
        Add(DatParser.FlagKind.Horizontal, thing.IsHorizontal);
        Add(DatParser.FlagKind.Rotatable, thing.IsRotatable);
        Add(DatParser.FlagKind.Light, thing.HasLight, () =>
        {
            writer.Write(thing.LightLevel);
            writer.Write(thing.LightColor);
        });
        Add(DatParser.FlagKind.DontHide, thing.DontHide);
        Add(DatParser.FlagKind.Translucent, thing.IsTranslucent);
        Add(DatParser.FlagKind.FloorChange, thing.FloorChange);

        var offsetKind = format is DatMetadataFormat.Versions710To730 or DatMetadataFormat.Versions740To750
            ? DatParser.FlagKind.OffsetFixed
            : DatParser.FlagKind.Offset;
        Add(offsetKind, thing.HasOffset, () =>
        {
            if (offsetKind == DatParser.FlagKind.Offset)
            {
                writer.Write(thing.OffsetX);
                writer.Write(thing.OffsetY);
            }
        });

        Add(DatParser.FlagKind.Elevation, thing.HasElevation, () => writer.Write(thing.Elevation));
        Add(DatParser.FlagKind.LyingObject, thing.IsLyingObject);
        Add(DatParser.FlagKind.AnimateAlways, thing.AnimateAlways);
        Add(DatParser.FlagKind.MiniMap, thing.HasMiniMapColor, () => writer.Write(thing.MiniMapColor));
        Add(DatParser.FlagKind.LensHelp, thing.HasLensHelp, () => writer.Write(thing.LensHelp));
        Add(DatParser.FlagKind.FullGround, thing.IsFullGround);
        Add(DatParser.FlagKind.IgnoreLook, thing.IgnoreLook);
        Add(DatParser.FlagKind.Cloth, thing.HasCloth, () => writer.Write(thing.ClothSlot));
        Add(DatParser.FlagKind.Market, thing.HasMarketInfo, () => WriteMarket(writer, thing));
        Add(DatParser.FlagKind.DefaultAction, thing.HasDefaultAction, () => writer.Write(thing.DefaultAction));
        Add(DatParser.FlagKind.Wrappable, thing.IsWrappable);
        Add(DatParser.FlagKind.Unwrappable, thing.IsUnwrappable);
        Add(DatParser.FlagKind.TopEffect, thing.IsTopEffect);
        Add(DatParser.FlagKind.Bones, thing.HasBones, () =>
        {
            for (var index = 0; index < 8; index++)
            {
                writer.Write(index < thing.BoneOffsets.Length ? thing.BoneOffsets[index] : (short)0);
            }
        });
        Add(DatParser.FlagKind.Usable, thing.IsUsable);

        foreach (var flag in flags.OrderBy(item => item.Value))
        {
            writer.Write(flag.Value);
            flag.Payload();
        }

        writer.Write((byte)0xFF);
    }

    private static byte? FindRawFlag(DatMetadataFormat format, DatParser.FlagKind kind)
    {
        return FlagValues.TryGetValue((format, kind), out var value) ? value : null;
    }

    private static IReadOnlyDictionary<(DatMetadataFormat Format, DatParser.FlagKind Kind), byte> BuildFlagValues()
    {
        var result = new Dictionary<(DatMetadataFormat, DatParser.FlagKind), byte>();
        foreach (var format in Enum.GetValues<DatMetadataFormat>())
        {
            for (var raw = 0; raw < byte.MaxValue; raw++)
            {
                var kind = DatParser.GetFlagKind(format, (byte)raw);
                if (kind != DatParser.FlagKind.Unknown)
                {
                    result.TryAdd((format, kind), (byte)raw);
                }
            }
        }

        return result;
    }

    private static void WriteMarket(BinaryWriter writer, DatThingType thing)
    {
        writer.Write(thing.MarketCategory);
        writer.Write(thing.MarketTradeAs);
        writer.Write(thing.MarketShowAs);
        var nameBytes = Encoding.Latin1.GetBytes(thing.MarketName);
        if (nameBytes.Length > ushort.MaxValue)
        {
            throw new InvalidDataException("Nazwa market itemu jest za długa.");
        }

        writer.Write((ushort)nameBytes.Length);
        writer.Write(nameBytes);
        writer.Write(thing.MarketRestrictProfession);
        writer.Write(thing.MarketRestrictLevel);
    }

    private static void WriteTexturePatterns(
        BinaryWriter writer,
        DatThingFrameGroup group,
        DatParserOptions options)
    {
        // Nie normalizujemy zerowych wymiarów. Zmodyfikowane klienty używają
        // ich dla pustych, zarezerwowanych obiektów, a Object Builder zachowuje
        // taki rekord 1:1 podczas zapisu.
        var width = group.Width;
        var height = group.Height;
        var layers = group.Layers;
        var patternX = group.PatternX;
        var patternY = group.PatternY;
        var patternZ = group.PatternZ;
        var frames = group.Frames;

        writer.Write(width);
        writer.Write(height);
        if (width > 1 || height > 1)
        {
            // Zero jest prawidłową wartością spotykaną w zmodyfikowanych klientach.
            // Musimy zachować ją 1:1 zamiast normalizować do 32.
            writer.Write(group.ExactSize);
        }

        writer.Write(layers);
        writer.Write(patternX);
        writer.Write(patternY);
        if (options.MetadataFormat is not (DatMetadataFormat.Versions710To730 or DatMetadataFormat.Versions740To750))
        {
            writer.Write(patternZ);
        }

        writer.Write(frames);

        if (options.ImprovedAnimations && frames > 1)
        {
            writer.Write(group.AnimationMode);
            writer.Write(group.LoopCount);
            writer.Write(group.StartFrame);
            for (var index = 0; index < frames; index++)
            {
                var duration = index < group.FrameDurations.Length
                    ? group.FrameDurations[index]
                    : new DatFrameDuration();
                writer.Write(duration.Min);
                writer.Write(duration.Max);
            }
        }

        var effectivePatternZ = options.MetadataFormat is DatMetadataFormat.Versions710To730 or DatMetadataFormat.Versions740To750
            ? 1
            : patternZ;
        var total = checked(width * height * layers * patternX * patternY * effectivePatternZ * frames);
        for (var index = 0; index < total; index++)
        {
            var spriteId = index < group.SpriteIds.Length ? group.SpriteIds[index] : 0u;
            if (options.ExtendedSprites)
            {
                writer.Write(spriteId);
            }
            else
            {
                if (spriteId > ushort.MaxValue)
                {
                    throw new InvalidDataException(
                        $"Sprite ID {spriteId} nie mieści się w klasycznym 16-bitowym formacie.");
                }

                writer.Write((ushort)spriteId);
            }
        }
    }
}
