using FluentAssertions;
using Google.Protobuf;
using Modules.ObjectBuilder.Services;
using Narzedzia.Core.Compression;
using Narzedzia.Core.Interchange;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;
using Narzedzia.Core.Tibia12;
using System.Text;
using Xunit;

namespace Modules.ObjectBuilder.Tests;

public sealed class LegacyObdCodecTests
{
    [Fact]
    public void Read_ImportsObjectBuilderObdV1_WithVersionedFlagsAndDefaultDurations()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var raw = new MemoryStream();
            using (var writer = new BinaryWriter(raw, Encoding.Latin1, leaveOpen: true))
            {
                writer.Write((ushort)860);
                var category = Encoding.Latin1.GetBytes("effect");
                writer.Write((ushort)category.Length);
                writer.Write(category);
                writer.Write((byte)0x15); // Has Light w MetadataReader5 (8.55–9.86)
                writer.Write((ushort)3);
                writer.Write((ushort)156);
                writer.Write((byte)0x17); // Translucent w MetadataReader5
                writer.Write((byte)0xFF);
                writer.Write((byte)1); // width
                writer.Write((byte)1); // height
                writer.Write((byte)1); // layers
                writer.Write((byte)1); // pattern X
                writer.Write((byte)1); // pattern Y
                writer.Write((byte)1); // pattern Z
                writer.Write((byte)2); // animations
                for (var index = 0; index < 2; index++)
                {
                    writer.Write((uint)(100 + index));
                    writer.Write((uint)4);
                    writer.Write(new byte[] { 0xAA, 0x11, 0x22, (byte)(0x30 + index) });
                }
            }

            File.WriteAllBytes(path, LzmaDataCodec.Compress(raw.ToArray()));

            var document = new LegacyObdCodec().Read(path);

            document.ObdVersion.Should().Be(100);
            document.ClientVersion.Should().Be(860);
            document.Thing.Category.Should().Be(DatThingCategory.Effects);
            document.Thing.HasLight.Should().BeTrue();
            document.Thing.LightLevel.Should().Be(3);
            document.Thing.LightColor.Should().Be(156);
            document.Thing.IsTranslucent.Should().BeTrue();
            document.Thing.FirstGroup!.Frames.Should().Be(2);
            document.Thing.FirstGroup.FrameDurations.Should().OnlyContain(duration =>
                duration.Min == 100 && duration.Max == 100);
            document.SpritePixelsByGroup[0].Should().HaveCount(2);
            document.SpritePixelsByGroup[0][0][..4].Should().Equal(0x30, 0x22, 0x11, 0xAA);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteAndRead_ItemV2_PreservesFlagsOffsetLayoutAndPixelsByDefault()
    {
        using var fixture = SpriteStoreFixture.Create();
        var thing = CreateThing(DatThingCategory.Items, fixture.SpriteId);
        thing.IsGround = true;
        thing.GroundSpeed = 150;
        thing.IsUnpassable = true;
        thing.HasLight = true;
        thing.LightLevel = 7;
        thing.LightColor = 215;
        thing.HasOffset = true;
        thing.OffsetX = -6;
        thing.OffsetY = 10;
        var path = Path.Combine(fixture.Directory, "item.obd");
        var codec = new LegacyObdCodec();

        codec.Write(path, thing, fixture.Store);
        var restored = codec.Read(path);

        restored.ObdVersion.Should().Be(200);
        restored.Thing.Category.Should().Be(DatThingCategory.Items);
        restored.Thing.IsGround.Should().BeTrue();
        restored.Thing.GroundSpeed.Should().Be(150);
        restored.Thing.IsUnpassable.Should().BeTrue();
        restored.Thing.HasLight.Should().BeTrue();
        restored.Thing.LightColor.Should().Be(215);
        restored.Thing.HasOffset.Should().BeTrue();
        restored.Thing.OffsetX.Should().Be(-6);
        restored.Thing.OffsetY.Should().Be(10);
        restored.Thing.FirstGroup!.SpriteIds.Should().Equal(fixture.SpriteId);
        restored.SpritePixelsByGroup[0][0].Should().Equal(fixture.Pixels);
    }

    [Fact]
    public void FlaglessMode_DropsFlagsOnWriteAndRead_ButKeepsOffset()
    {
        using var fixture = SpriteStoreFixture.Create();
        var thing = CreateThing(DatThingCategory.Items, fixture.SpriteId);
        thing.IsGround = true;
        thing.GroundSpeed = 130;
        thing.IsUnpassable = true;
        thing.HasOffset = true;
        thing.OffsetX = -8;
        thing.OffsetY = 12;
        var codec = new LegacyObdCodec();

        var strippedOnWritePath = Path.Combine(fixture.Directory, "flagless-write.obd");
        codec.Write(
            strippedOnWritePath,
            thing,
            fixture.Store,
            preserveFlags: false);
        var strippedOnWrite = codec.Read(strippedOnWritePath);

        strippedOnWrite.Thing.IsGround.Should().BeFalse();
        strippedOnWrite.Thing.IsUnpassable.Should().BeFalse();
        strippedOnWrite.Thing.HasOffset.Should().BeTrue();
        strippedOnWrite.Thing.OffsetX.Should().Be(-8);
        strippedOnWrite.Thing.OffsetY.Should().Be(12);

        var strippedOnReadPath = Path.Combine(fixture.Directory, "flagless-read.obd");
        codec.Write(strippedOnReadPath, thing, fixture.Store);
        var strippedOnRead = codec.Read(strippedOnReadPath, preserveFlags: false);

        strippedOnRead.Thing.IsGround.Should().BeFalse();
        strippedOnRead.Thing.IsUnpassable.Should().BeFalse();
        strippedOnRead.Thing.HasOffset.Should().BeTrue();
        strippedOnRead.Thing.OffsetX.Should().Be(-8);
        strippedOnRead.Thing.OffsetY.Should().Be(12);
    }

    [Fact]
    public void WriteAndRead_OutfitV3_PreservesFrameGroups()
    {
        using var fixture = SpriteStoreFixture.Create();
        var thing = CreateThing(DatThingCategory.Outfits, fixture.SpriteId);
        var walking = CreateGroup(fixture.SpriteId);
        walking.GroupType = 1;
        thing.FrameGroups.Add(walking);
        var path = Path.Combine(fixture.Directory, "outfit.obd");
        var codec = new LegacyObdCodec();

        codec.Write(path, thing, fixture.Store);
        var restored = codec.Read(path);

        restored.ObdVersion.Should().Be(300);
        restored.Thing.FrameGroups.Should().HaveCount(2);
        restored.Thing.FrameGroups[1].GroupType.Should().Be(1);
        restored.SpritePixelsByGroup.Should().HaveCount(2);
    }

    [Fact]
    public void WriteAndRead_ClassicOutfitV2_PreservesThreeFramesInOneGroup()
    {
        using var fixture = SpriteStoreFixture.Create();
        var thing = CreateThing(DatThingCategory.Outfits, fixture.SpriteId);
        var group = thing.FrameGroups[0];
        group.PatternX = 4;
        group.Frames = 3;
        group.SpriteIds = Enumerable.Repeat(fixture.SpriteId, 12).ToArray();
        var path = Path.Combine(fixture.Directory, "classic-outfit.obd");
        var codec = new LegacyObdCodec();

        codec.Write(path, thing, fixture.Store, useFrameGroups: false);
        var restored = codec.Read(path);

        restored.ObdVersion.Should().Be(200);
        restored.UsesFrameGroups.Should().BeFalse();
        restored.Thing.FrameGroups.Should().ContainSingle();
        restored.Thing.FrameGroups[0].PatternX.Should().Be(4);
        restored.Thing.FrameGroups[0].Frames.Should().Be(3);
        restored.Thing.FrameGroups[0].SpriteIds.Should().HaveCount(12);
        restored.SpritePixelsByGroup[0].Should().HaveCount(12);
    }

    [Fact]
    public void RenderSheet_UsesObjectBuilderPatternLayout()
    {
        using var fixture = SpriteStoreFixture.Create();
        var thing = CreateThing(DatThingCategory.Items, fixture.SpriteId);
        thing.FrameGroups[0].Width = 2;
        thing.FrameGroups[0].Height = 2;
        thing.FrameGroups[0].PatternX = 4;
        thing.FrameGroups[0].SpriteIds = Enumerable.Repeat(fixture.SpriteId, 16).ToArray();

        var sheet = new LegacyThingExportService().RenderSheet(thing, fixture.Store, transparentBackground: true);

        sheet.Width.Should().Be(2 * 32 * 4);
        sheet.Height.Should().Be(2 * 32);
        sheet.Pixels.Should().HaveCount(sheet.Width * sheet.Height * 4);
    }

    [Fact]
    public void AecAndObdExchange_TwoByTwoObject_RemainsCompatible()
    {
        var tiles = new Dictionary<uint, byte[]>
        {
            [1] = SolidTile(10),
            [2] = SolidTile(20),
            [3] = SolidTile(30),
            [4] = SolidTile(40)
        };
        var source = new DatThingType
        {
            Id = 100,
            Category = DatThingCategory.Items,
            IsUnpassable = true,
            HasOffset = true,
            OffsetX = -4,
            OffsetY = 6,
            FrameGroups =
            {
                new DatThingFrameGroup
                {
                    Width = 2, Height = 2, ExactSize = 64, Layers = 1,
                    PatternX = 1, PatternY = 1, PatternZ = 1, Frames = 1,
                    SpriteIds = [1, 2, 3, 4]
                }
            }
        };

        var modern = LegacyAppearanceConverter.ToAppearance(source, id => tiles.GetValueOrDefault(id));
        var aec = AecContainerCodec.BuildExportContainer(
            [modern.Appearance],
            id => modern.Sprites.GetValueOrDefault(id));
        using var serialized = new MemoryStream();
        aec.WriteTo(serialized);
        var parsed = Appearances.Parser.ParseFrom(serialized.ToArray());
        var embedded = AecContainerCodec.ReadEmbeddedAppearances(parsed).Single();
        var legacy = LegacyAppearanceConverter.ToLegacy(
            embedded.Appearance,
            APPEARANCE_TYPE.AppearanceObject,
            id => embedded.Sprites[(int)id]);

        var directory = Path.Combine(Path.GetTempPath(), "exchange-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "item.obd");
            var codec = new LegacyObdCodec();
            codec.Write(path, legacy.Thing,
                id => legacy.TileSprites.TryGetValue(id, out var pixels) ? pixels : null);
            var restored = codec.Read(path);

            restored.Thing.IsUnpassable.Should().BeTrue();
            restored.Thing.HasOffset.Should().BeTrue();
            restored.Thing.OffsetX.Should().Be(-4);
            restored.Thing.OffsetY.Should().Be(6);
            restored.Thing.FirstGroup!.Width.Should().Be(2);
            restored.Thing.FirstGroup.Height.Should().Be(2);
            restored.SpritePixelsByGroup[0].Should().HaveCount(4);
            restored.SpritePixelsByGroup[0][0][0].Should().Be(10);
            restored.SpritePixelsByGroup[0][1][0].Should().Be(20);
            restored.SpritePixelsByGroup[0][2][0].Should().Be(30);
            restored.SpritePixelsByGroup[0][3][0].Should().Be(40);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DatThingType CreateThing(DatThingCategory category, uint spriteId)
    {
        var thing = new DatThingType { Category = category };
        thing.FrameGroups.Add(CreateGroup(spriteId));
        return thing;
    }

    private static DatThingFrameGroup CreateGroup(uint spriteId) => new()
    {
        Width = 1,
        Height = 1,
        ExactSize = 32,
        Layers = 1,
        PatternX = 1,
        PatternY = 1,
        PatternZ = 1,
        Frames = 1,
        SpriteIds = [spriteId]
    };

    private static byte[] SolidTile(byte value)
    {
        var pixels = new byte[SprParser.SpriteDataSize];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = value;
            pixels[offset + 1] = (byte)(value + 1);
            pixels[offset + 2] = (byte)(value + 2);
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private sealed class SpriteStoreFixture : IDisposable
    {
        private SpriteStoreFixture(string directory, LegacySpriteStore store, byte[] pixels)
        {
            Directory = directory;
            Store = store;
            Pixels = pixels;
        }

        public string Directory { get; }
        public LegacySpriteStore Store { get; }
        public byte[] Pixels { get; }
        public uint SpriteId => 1;

        public static SpriteStoreFixture Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "objectbuilder-tests-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "Tibia.spr");
            var pixels = new byte[SprParser.SpriteDataSize];
            pixels[0] = 0x33;
            pixels[1] = 0x22;
            pixels[2] = 0x11;
            pixels[3] = 0x88;
            var file = new SprFile
            {
                Signature = 0x12345678,
                ExtendedSprites = false,
                TransparentSprites = true,
                Sprites = [SprParser.CompressSpriteFromBgra(pixels, transparentSprites: true)]
            };
            new SprParser().Save(file, path);
            var store = new LegacySpriteStore();
            store.Load(path, extendedSprites: false, transparentSprites: true);
            return new SpriteStoreFixture(directory, store, pixels);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
