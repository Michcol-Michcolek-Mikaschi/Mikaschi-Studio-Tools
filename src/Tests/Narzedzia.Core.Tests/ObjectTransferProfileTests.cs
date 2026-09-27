using FluentAssertions;
using Google.Protobuf;
using Narzedzia.Core.Interchange;
using Narzedzia.Core.Models;
using Narzedzia.Core.Tibia12;

namespace Narzedzia.Core.Tests;

public sealed class ObjectTransferProfileTests
{
    [Fact]
    public void LegacyObject_PreservesLayoutSpritesAndOffset_ButDropsGameplayFlags()
    {
        var source = new DatThingType
        {
            Id = 77,
            Category = DatThingCategory.Outfits,
            IsUnpassable = true,
            IsUnmoveable = true,
            HasLight = true,
            LightLevel = 8,
            HasOffset = true,
            OffsetX = -5,
            OffsetY = 9,
            FrameGroups =
            {
                new DatThingFrameGroup
                {
                    Width = 1,
                    Height = 1,
                    PatternX = 4,
                    PatternY = 1,
                    PatternZ = 1,
                    Layers = 1,
                    Frames = 3,
                    SpriteIds = Enumerable.Range(1, 12).Select(value => (uint)value).ToArray()
                }
            }
        };

        var result = ObjectTransferProfile.Create(source);

        result.Id.Should().Be(77);
        result.Category.Should().Be(DatThingCategory.Outfits);
        result.HasOffset.Should().BeTrue();
        result.OffsetX.Should().Be(-5);
        result.OffsetY.Should().Be(9);
        result.IsUnpassable.Should().BeFalse();
        result.IsUnmoveable.Should().BeFalse();
        result.HasLight.Should().BeFalse();
        result.FrameGroups.Should().ContainSingle();
        result.FrameGroups[0].Frames.Should().Be(3);
        result.FrameGroups[0].SpriteIds.Should().Equal(source.FrameGroups[0].SpriteIds);
        result.FrameGroups[0].Should().NotBeSameAs(source.FrameGroups[0]);
    }

    [Fact]
    public void ModernObject_PreservesLayoutSpriteDataAndShift_ButDropsOtherFlags()
    {
        var source = new Appearance
        {
            Id = 88,
            AppearanceType = APPEARANCE_TYPE.AppearanceEffect,
            Name = "nie eksportuj",
            Description = "nie eksportuj",
            Flags = new AppearanceFlags
            {
                Unpass = true,
                Container = true,
                Shift = new AppearanceFlagShift
                {
                    X = unchecked((uint)-7),
                    Y = 11
                }
            },
            FrameGroup =
            {
                new FrameGroup { SpriteInfo = new SpriteInfo { SpriteId = { 123u } } }
            },
            SpriteData = { ByteString.CopyFrom([1, 2, 3, 4]) }
        };

        var result = ObjectTransferProfile.Create(source);

        result.Id.Should().Be(88);
        result.AppearanceType.Should().Be(APPEARANCE_TYPE.AppearanceEffect);
        result.Name.Should().BeEmpty();
        result.Description.Should().BeEmpty();
        result.Flags.Should().NotBeNull();
        result.Flags.Unpass.Should().BeFalse();
        result.Flags.Container.Should().BeFalse();
        unchecked((int)result.Flags.Shift!.X).Should().Be(-7);
        result.Flags.Shift.Y.Should().Be(11);
        result.FrameGroup.Should().ContainSingle();
        result.FrameGroup[0].SpriteInfo.SpriteId.Should().Equal(123u);
        result.SpriteData.Should().ContainSingle();
    }

    [Fact]
    public void AecCodec_FlaglessMode_DropsFlagsOnExportAndImport_ButKeepsShift()
    {
        var source = new Appearance
        {
            Id = 99,
            AppearanceType = APPEARANCE_TYPE.AppearanceEffect,
            Flags = new AppearanceFlags
            {
                Unpass = true,
                AnimateAlways = true,
                Shift = new AppearanceFlagShift
                {
                    X = unchecked((uint)-2),
                    Y = 4
                }
            },
            FrameGroup =
            {
                new FrameGroup { SpriteInfo = new SpriteInfo { SpriteId = { 55u } } }
            }
        };

        var exported = AecContainerCodec.BuildExportContainer(
            [source],
            _ => new AecSpritePayload(new byte[32 * 32 * 4], 32, 32),
            preserveFlags: false);
        var stored = exported.Effect.Single();

        stored.Flags.Unpass.Should().BeFalse();
        stored.Flags.AnimateAlways.Should().BeFalse();
        unchecked((int)stored.Flags.Shift!.X).Should().Be(-2);
        stored.Flags.Shift.Y.Should().Be(4);

        stored.Flags.Unpass = true;
        var imported = AecContainerCodec.ReadEmbeddedAppearances(
            exported,
            preserveFlags: false).Single().Appearance;

        imported.Flags.Unpass.Should().BeFalse();
        imported.Flags.AnimateAlways.Should().BeFalse();
        unchecked((int)imported.Flags.Shift!.X).Should().Be(-2);
        imported.Flags.Shift.Y.Should().Be(4);
    }
}
