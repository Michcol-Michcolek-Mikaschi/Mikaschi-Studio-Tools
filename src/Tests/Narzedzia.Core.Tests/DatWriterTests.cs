using FluentAssertions;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Narzedzia.Core.Tests;

public sealed class DatWriterTests
{
    [Fact]
    public void DatParser_ParseAuto_ReportsEncryptedEnc3FileClearly()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path,
            [
                (byte)'E', (byte)'N', (byte)'C', (byte)'3',
                1, 2, 3, 4, 5, 6, 7, 8
            ]);

            var act = () => DatParser.ParseAuto(path);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*zaszyfrowany (ENC3)*klucz*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    public static TheoryData<DatMetadataFormat, uint> LegacyFormats => new()
    {
        { DatMetadataFormat.Versions710To730, 0x3DFF4B2A },
        { DatMetadataFormat.Versions740To750, 0x41BF619C },
        { DatMetadataFormat.Versions755To772, 0x437B2B8F },
        { DatMetadataFormat.Versions780To854, 0x44CE4743 },
        { DatMetadataFormat.Versions855To986, 0x4C2C7993 },
        { DatMetadataFormat.Versions1010AndNewer, 0x51E3F8C3 }
    };

    [Fact]
    public void LegacyFrameGroupIndex_MatchesObjectBuilderFrameGroup()
    {
        var group = new DatThingFrameGroup
        {
            Width = 2,
            Height = 2,
            Layers = 2,
            PatternX = 4,
            PatternY = 3,
            PatternZ = 2,
            Frames = 2
        };

        var index = DatParser.CalculateSpriteIndex(
            group,
            tileX: 1,
            tileY: 0,
            layer: 1,
            patternX: 2,
            patternY: 1,
            patternZ: 0,
            frame: 1);

        index.Should().Be((((((1 * 2 + 0) * 3 + 1) * 4 + 2) * 2 + 1) * 2 + 0) * 2 + 1);
    }

    [Fact]
    public void DatWriter_WritesFileThatCanBeParsedBack()
    {
        var dat = new DatFile
        {
            Signature = 0x12345678,
            ItemsMaxId = 100,
            OutfitsMaxId = 1,
            EffectsMaxId = 1,
            MissilesMaxId = 1
        };

        dat.Items.Add(new DatThingType
        {
            Id = 100,
            Category = DatThingCategory.Items,
            IsGround = true,
            GroundSpeed = 120,
            IsUsable = true,
            FrameGroups =
            {
                new DatThingFrameGroup
                {
                    Width = 1,
                    Height = 1,
                    Layers = 1,
                    PatternX = 1,
                    PatternY = 1,
                    PatternZ = 1,
                    Frames = 1,
                    SpriteIds = [1234u]
                }
            }
        });

        dat.Outfits.Add(CreateMinimalThing(1, DatThingCategory.Outfits, 2001u));
        dat.Effects.Add(CreateMinimalThing(1, DatThingCategory.Effects, 3001u));
        dat.Missiles.Add(CreateMinimalThing(1, DatThingCategory.Missiles, 4001u));

        using var stream = new MemoryStream();
        DatWriter.Write(stream, dat, DatParserOptions.WithFrameGroups);
        stream.Position = 0;

        var parsed = DatParser.Parse(stream, DatParserOptions.WithFrameGroups);
        parsed.Signature.Should().Be(dat.Signature);
        parsed.Items.Should().HaveCount(1);
        parsed.Outfits.Should().HaveCount(1);
        parsed.Effects.Should().HaveCount(1);
        parsed.Missiles.Should().HaveCount(1);

        parsed.Items[0].IsGround.Should().BeTrue();
        parsed.Items[0].GroundSpeed.Should().Be(120);
        parsed.Items[0].IsUsable.Should().BeTrue();
        parsed.Items[0].FrameGroups.Should().HaveCount(1);
        parsed.Items[0].FrameGroups[0].SpriteIds.Should().ContainSingle().Which.Should().Be(1234u);
    }

    [Fact]
    public void DatParserAndWriter_PreserveReservedObjectWithZeroWidth()
    {
        var dat = CreateSmallDat(0x4B28B8A9);
        var group = dat.Items[0].FrameGroups[0];
        group.Width = 0;
        group.Height = 3;
        group.ExactSize = 64;
        group.Layers = 1;
        group.PatternX = 1;
        group.PatternY = 1;
        group.PatternZ = 1;
        group.Frames = 1;
        group.SpriteIds = [];
        var options = new DatParserOptions
        {
            ExtendedSprites = true,
            ImprovedAnimations = false,
            FrameGroups = false,
            MetadataFormat = DatMetadataFormat.Versions780To854
        };

        using var stream = new MemoryStream();
        DatWriter.Write(stream, dat, options);
        stream.Position = 0;
        var parsed = DatParser.Parse(stream, options);

        stream.Position.Should().Be(stream.Length);
        parsed.Items[0].FirstGroup!.Width.Should().Be(0);
        parsed.Items[0].FirstGroup!.Height.Should().Be(3);
        parsed.Items[0].FirstGroup!.ExactSize.Should().Be(64);
        parsed.Items[0].FirstGroup!.SpriteIds.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(LegacyFormats))]
    public void DatWriter_RoundTripsEveryObjectBuilderMetadataFormat(
        DatMetadataFormat format,
        uint signature)
    {
        var dat = CreateSmallDat(signature);
        dat.Items[0].IsGround = true;
        dat.Items[0].GroundSpeed = 150;
        dat.Items[0].IsWritable = true;
        dat.Items[0].MaxReadWriteChars = 255;
        dat.Items[0].IsUnpassable = true;
        dat.Items[0].HasLight = true;
        dat.Items[0].LightLevel = 7;
        dat.Items[0].LightColor = 215;
        dat.Items[0].HasOffset = true;
        dat.Items[0].OffsetX = format is DatMetadataFormat.Versions710To730 or DatMetadataFormat.Versions740To750
            ? (short)8
            : (short)-3;
        dat.Items[0].OffsetY = format is DatMetadataFormat.Versions710To730 or DatMetadataFormat.Versions740To750
            ? (short)8
            : (short)5;

        var options = new DatParserOptions
        {
            ExtendedSprites = false,
            ImprovedAnimations = false,
            FrameGroups = false,
            MetadataFormat = format
        };

        using var stream = new MemoryStream();
        DatWriter.Write(stream, dat, options);
        stream.Position = 0;
        var parsed = DatParser.Parse(stream, options);

        stream.Position.Should().Be(stream.Length);
        parsed.Items.Should().ContainSingle();
        parsed.Items[0].GroundSpeed.Should().Be(150);
        parsed.Items[0].MaxReadWriteChars.Should().Be(255);
        parsed.Items[0].IsUnpassable.Should().BeTrue();
        parsed.Items[0].LightColor.Should().Be(215);
        parsed.Items[0].OffsetX.Should().Be(dat.Items[0].OffsetX);
        parsed.Items[0].OffsetY.Should().Be(dat.Items[0].OffsetY);
        parsed.Items[0].FirstGroup!.SpriteIds.Should().ContainSingle().Which.Should().Be(1234u);
    }

    [Fact]
    public void DatParser_ParseAuto_DetectsCustomExtended860Files()
    {
        var dat = CreateSmallDat(0x4C2C7993);
        dat.Items[0].HasLight = true;
        dat.Items[0].LightColor = 156;
        dat.Items[0].LightLevel = 3;
        dat.Items[0].HasMiniMapColor = true;
        dat.Items[0].MiniMapColor = 129;
        dat.Items[0].IsTranslucent = true;
        dat.Items[0].FrameGroups[0].Frames = 2;
        dat.Items[0].FrameGroups[0].SpriteIds = [1234u, 5678u];
        var actualOptions = new DatParserOptions
        {
            ExtendedSprites = true,
            ImprovedAnimations = false,
            FrameGroups = false,
            MetadataFormat = DatMetadataFormat.Versions855To986
        };

        var path = Path.GetTempFileName();
        try
        {
            DatWriter.Write(path, dat, actualOptions);

            var result = DatParser.ParseAuto(path, DatParserOptions.WithFrameGroups);

            result.Options.MetadataFormat.Should().Be(DatMetadataFormat.Versions855To986);
            result.Options.ExtendedSprites.Should().BeTrue();
            result.Options.ImprovedAnimations.Should().BeFalse();
            result.Options.FrameGroups.Should().BeFalse();
            result.File.Items[0].HasLight.Should().BeTrue();
            result.File.Items[0].LightColor.Should().Be(156);
            result.File.Items[0].LightLevel.Should().Be(3);
            result.File.Items[0].HasMiniMapColor.Should().BeTrue();
            result.File.Items[0].MiniMapColor.Should().Be(129);
            result.File.Items[0].IsTranslucent.Should().BeTrue();
            result.File.Items[0].FirstGroup!.SpriteIds.Should().Equal(1234u, 5678u);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0x3DFF4B2A, DatMetadataFormat.Versions710To730)]
    [InlineData(0x4C2C7993, DatMetadataFormat.Versions855To986)]
    [InlineData(0x51E3F8C3, DatMetadataFormat.Versions1010AndNewer)]
    [InlineData(0x000042A3, DatMetadataFormat.Versions1010AndNewer)]
    public void DatParser_GuessesMetadataFormatFromSignature(
        uint signature,
        DatMetadataFormat expected)
    {
        DatParser.GuessMetadataFormat(signature).Should().Be(expected);
    }

    private static DatFile CreateSmallDat(uint signature)
    {
        var dat = new DatFile
        {
            Signature = signature,
            ItemsMaxId = 100,
            OutfitsMaxId = 1,
            EffectsMaxId = 1,
            MissilesMaxId = 1
        };

        dat.Items.Add(CreateMinimalThing(100, DatThingCategory.Items, 1234u));
        dat.Outfits.Add(CreateMinimalThing(1, DatThingCategory.Outfits, 2001u));
        dat.Effects.Add(CreateMinimalThing(1, DatThingCategory.Effects, 3001u));
        dat.Missiles.Add(CreateMinimalThing(1, DatThingCategory.Missiles, 4001u));
        return dat;
    }

    private static DatThingType CreateMinimalThing(uint id, DatThingCategory category, uint spriteId)
    {
        return new DatThingType
        {
            Id = id,
            Category = category,
            FrameGroups =
            {
                new DatThingFrameGroup
                {
                    GroupType = category == DatThingCategory.Outfits ? 0 : 0,
                    Width = 1,
                    Height = 1,
                    Layers = 1,
                    PatternX = 1,
                    PatternY = 1,
                    PatternZ = 1,
                    Frames = 1,
                    SpriteIds = [spriteId]
                }
            }
        };
    }
}
