using FluentAssertions;
using Narzedzia.Core.Interchange;
using Narzedzia.Core.Models;
using Narzedzia.Core.Tibia12;

namespace Narzedzia.Core.Tests;

public sealed class LegacyAppearanceConverterTests
{
    [Fact]
    public void LegacyTwoByTwo_RoundTripThroughAppearance_PreservesTileOrderAndFlags()
    {
        var sourceTiles = new Dictionary<uint, byte[]>
        {
            [1] = SolidTile(10, 11, 12),
            [2] = SolidTile(20, 21, 22),
            [3] = SolidTile(30, 31, 32),
            [4] = SolidTile(40, 41, 42)
        };
        var source = new DatThingType
        {
            Id = 100,
            Category = DatThingCategory.Items,
            IsGround = true,
            GroundSpeed = 150,
            IsUnpassable = true,
            HasLight = true,
            LightLevel = 7,
            LightColor = 215,
            HasOffset = true,
            OffsetX = -3,
            OffsetY = 5
        };
        source.FrameGroups.Add(new DatThingFrameGroup
        {
            Width = 2,
            Height = 2,
            ExactSize = 64,
            Layers = 1,
            PatternX = 1,
            PatternY = 1,
            PatternZ = 1,
            Frames = 1,
            SpriteIds = [1, 2, 3, 4]
        });

        var modern = LegacyAppearanceConverter.ToAppearance(
            source,
            id => sourceTiles.GetValueOrDefault(id));

        modern.Appearance.FrameGroup[0].SpriteInfo.SpriteId.Should().HaveCount(1);
        var compositeId = modern.Appearance.FrameGroup[0].SpriteInfo.SpriteId[0];
        var composite = modern.Sprites[compositeId];
        composite.Width.Should().Be(64);
        composite.Height.Should().Be(64);
        Pixel(composite.Pixels, 64, 0, 0).Should().Equal(40, 41, 42, 255);
        Pixel(composite.Pixels, 64, 32, 0).Should().Equal(30, 31, 32, 255);
        Pixel(composite.Pixels, 64, 0, 32).Should().Equal(20, 21, 22, 255);
        Pixel(composite.Pixels, 64, 32, 32).Should().Equal(10, 11, 12, 255);

        var restored = LegacyAppearanceConverter.ToLegacy(
            modern.Appearance,
            APPEARANCE_TYPE.AppearanceObject,
            id => modern.Sprites.GetValueOrDefault(id));

        restored.Thing.FirstGroup!.Width.Should().Be(2);
        restored.Thing.FirstGroup.Height.Should().Be(2);
        restored.Thing.IsGround.Should().BeTrue();
        restored.Thing.GroundSpeed.Should().Be(150);
        restored.Thing.IsUnpassable.Should().BeTrue();
        restored.Thing.LightColor.Should().Be(215);
        restored.Thing.OffsetX.Should().Be(-3);
        restored.Thing.OffsetY.Should().Be(5);
        for (var index = 0; index < 4; index++)
        {
            var restoredId = restored.Thing.FirstGroup.SpriteIds[index];
            restored.TileSprites[restoredId].Should().Equal(sourceTiles[(uint)index + 1]);
        }
    }

    [Fact]
    public void AppearanceAnimation_ToLegacy_PreservesGroupsPatternsAndTiming()
    {
        var appearance = new Appearance
        {
            Id = 55,
            AppearanceType = APPEARANCE_TYPE.AppearanceOutfit,
            FrameGroup =
            {
                new FrameGroup
                {
                    FixedFrameGroup = FIXED_FRAME_GROUP.OutfitMoving,
                    SpriteInfo = new SpriteInfo
                    {
                        PatternWidth = 4,
                        PatternHeight = 3,
                        PatternDepth = 1,
                        Layers = 2,
                        SpriteId = { Enumerable.Repeat(7u, 4 * 3 * 2 * 2) },
                        Animation = new SpriteAnimation
                        {
                            Synchronized = true,
                            LoopType = ANIMATION_LOOP_TYPE.Pingpong,
                            RandomStartPhase = true,
                            SpritePhase =
                            {
                                new SpritePhase { DurationMin = 80, DurationMax = 100 },
                                new SpritePhase { DurationMin = 120, DurationMax = 160 }
                            }
                        }
                    }
                }
            }
        };
        var payload = new AecSpritePayload(SolidTile(1, 2, 3), 32, 32);

        var legacy = LegacyAppearanceConverter.ToLegacy(
            appearance,
            APPEARANCE_TYPE.AppearanceOutfit,
            _ => payload);

        legacy.Thing.Category.Should().Be(DatThingCategory.Outfits);
        legacy.Thing.FirstGroup!.GroupType.Should().Be(1);
        legacy.Thing.FirstGroup.PatternX.Should().Be(4);
        legacy.Thing.FirstGroup.PatternY.Should().Be(3);
        legacy.Thing.FirstGroup.Layers.Should().Be(2);
        legacy.Thing.FirstGroup.Frames.Should().Be(2);
        legacy.Thing.FirstGroup.AnimationMode.Should().Be(1);
        legacy.Thing.FirstGroup.LoopCount.Should().Be(-1);
        legacy.Thing.FirstGroup.StartFrame.Should().Be(-1);
        legacy.Thing.FirstGroup.FrameDurations[1].Min.Should().Be(120);
        legacy.Thing.FirstGroup.FrameDurations[1].Max.Should().Be(160);
    }

    [Fact]
    public void SharedLegacyOutfitAnimation_ToAppearance_CreatesIdleAndWalkingGroups()
    {
        var legacy = new DatThingType
        {
            Id = 525,
            Category = DatThingCategory.Outfits,
            FrameGroups =
            {
                new DatThingFrameGroup
                {
                    Width = 1,
                    Height = 1,
                    Layers = 1,
                    PatternX = 4,
                    PatternY = 1,
                    PatternZ = 1,
                    Frames = 3,
                    SpriteIds = Enumerable.Range(1, 12).Select(id => (uint)id).ToArray()
                }
            }
        };

        var modern = LegacyAppearanceConverter.ToAppearance(
            legacy,
            _ => SolidTile(1, 2, 3),
            sourceUsesFrameGroups: false);

        modern.Appearance.FrameGroup.Should().HaveCount(2);
        var idle = modern.Appearance.FrameGroup[0];
        var walking = modern.Appearance.FrameGroup[1];
        idle.FixedFrameGroup.Should().Be(FIXED_FRAME_GROUP.OutfitIdle);
        idle.SpriteInfo.Animation.Should().BeNull();
        idle.SpriteInfo.SpriteId.Should().Equal(walking.SpriteInfo.SpriteId.Take(4));
        walking.FixedFrameGroup.Should().Be(FIXED_FRAME_GROUP.OutfitMoving);
        walking.SpriteInfo.Animation.SpritePhase.Should().HaveCount(3);
        walking.SpriteInfo.SpriteId.Should().HaveCount(12);
        modern.Warnings.Should().ContainSingle(message => message.Contains("Idle") && message.Contains("Walking"));
    }

    [Fact]
    public void ModernOutfit_ToLegacyWithoutFrameGroups_UsesCompleteWalkingAnimation()
    {
        var appearance = new Appearance
        {
            Id = 5,
            AppearanceType = APPEARANCE_TYPE.AppearanceOutfit,
            FrameGroup =
            {
                new FrameGroup
                {
                    FixedFrameGroup = FIXED_FRAME_GROUP.OutfitIdle,
                    SpriteInfo = new SpriteInfo
                    {
                        PatternWidth = 4,
                        PatternHeight = 1,
                        PatternDepth = 1,
                        Layers = 1,
                        SpriteId = { 1u, 2u, 3u, 4u }
                    }
                },
                new FrameGroup
                {
                    FixedFrameGroup = FIXED_FRAME_GROUP.OutfitMoving,
                    SpriteInfo = new SpriteInfo
                    {
                        PatternWidth = 4,
                        PatternHeight = 1,
                        PatternDepth = 1,
                        Layers = 1,
                        SpriteId = { Enumerable.Range(5, 12).Select(id => (uint)id) },
                        Animation = new SpriteAnimation
                        {
                            SpritePhase =
                            {
                                new SpritePhase { DurationMin = 100, DurationMax = 100 },
                                new SpritePhase { DurationMin = 100, DurationMax = 100 },
                                new SpritePhase { DurationMin = 100, DurationMax = 100 }
                            }
                        }
                    }
                }
            }
        };
        var payload = new AecSpritePayload(SolidTile(1, 2, 3), 32, 32);

        var legacy = LegacyAppearanceConverter.ToLegacy(
            appearance,
            APPEARANCE_TYPE.AppearanceOutfit,
            _ => payload,
            targetSupportsFrameGroups: false);

        legacy.Thing.FrameGroups.Should().ContainSingle();
        legacy.Thing.FirstGroup!.Frames.Should().Be(3);
        legacy.Thing.FirstGroup.SpriteIds.Should().HaveCount(12);
        legacy.Warnings.Should().ContainSingle(message => message.Contains("wspólną animację"));
    }

    [Fact]
    public void LegacyOutfitAdapter_SplitsAndCollapsesGroupsWithoutChangingWalkingFrames()
    {
        var outfit = new DatThingType
        {
            Id = 525,
            Category = DatThingCategory.Outfits,
            FrameGroups =
            {
                new DatThingFrameGroup
                {
                    Width = 1,
                    Height = 1,
                    Layers = 1,
                    PatternX = 4,
                    PatternY = 1,
                    PatternZ = 1,
                    Frames = 3,
                    SpriteIds = Enumerable.Range(1, 12).Select(id => (uint)id).ToArray()
                }
            }
        };

        LegacyOutfitFrameGroupAdapter.Adapt(
            outfit,
            sourceUsesFrameGroups: false,
            targetSupportsFrameGroups: true).Should().BeTrue();

        outfit.FrameGroups.Should().HaveCount(2);
        outfit.FrameGroups[0].GroupType.Should().Be(0);
        outfit.FrameGroups[0].Frames.Should().Be(1);
        outfit.FrameGroups[0].SpriteIds.Should().Equal(1u, 2u, 3u, 4u);
        outfit.FrameGroups[1].GroupType.Should().Be(1);
        outfit.FrameGroups[1].Frames.Should().Be(3);
        outfit.FrameGroups[1].SpriteIds.Should().Equal(Enumerable.Range(1, 12).Select(id => (uint)id));

        LegacyOutfitFrameGroupAdapter.Adapt(
            outfit,
            sourceUsesFrameGroups: true,
            targetSupportsFrameGroups: false).Should().BeTrue();

        outfit.FrameGroups.Should().ContainSingle();
        outfit.FirstGroup!.GroupType.Should().Be(0);
        outfit.FirstGroup.Frames.Should().Be(3);
        outfit.FirstGroup.SpriteIds.Should().Equal(Enumerable.Range(1, 12).Select(id => (uint)id));
    }

    [Theory]
    [InlineData(DatThingCategory.Items, APPEARANCE_TYPE.AppearanceObject)]
    [InlineData(DatThingCategory.Outfits, APPEARANCE_TYPE.AppearanceOutfit)]
    [InlineData(DatThingCategory.Effects, APPEARANCE_TYPE.AppearanceEffect)]
    [InlineData(DatThingCategory.Missiles, APPEARANCE_TYPE.AppearanceMissile)]
    public void Conversion_PreservesEverySupportedCategory(
        DatThingCategory legacyCategory,
        APPEARANCE_TYPE appearanceCategory)
    {
        var legacy = new DatThingType
        {
            Id = 100,
            Category = legacyCategory,
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
                    SpriteIds = [1]
                }
            }
        };

        var modern = LegacyAppearanceConverter.ToAppearance(legacy, _ => SolidTile(1, 2, 3));
        var restored = LegacyAppearanceConverter.ToLegacy(
            modern.Appearance,
            modern.Appearance.AppearanceType,
            id => modern.Sprites.GetValueOrDefault(id));

        modern.Appearance.AppearanceType.Should().Be(appearanceCategory);
        restored.Thing.Category.Should().Be(legacyCategory);
    }

    [Fact]
    public void AecContainerSection_IsAuthoritativeForLegacyFilesWithoutAppearanceType()
    {
        var legacyAppearance = new Appearance
        {
            Id = 77,
            AppearanceType = APPEARANCE_TYPE.AppearanceObject,
            SpriteData = { Google.Protobuf.ByteString.CopyFrom(SolidTile(1, 2, 3)) },
            FrameGroup = { new FrameGroup { SpriteInfo = new SpriteInfo { SpriteId = { 0u } } } }
        };

        var embedded = AecContainerCodec.ReadEmbeddedAppearances(
            new Narzedzia.Core.Tibia12.Appearances { Effect = { legacyAppearance } }).Single();

        embedded.Appearance.AppearanceType.Should().Be(APPEARANCE_TYPE.AppearanceEffect);
    }

    [Fact]
    public void AecV2Payload_PreservesAmbiguousSpriteDimensions()
    {
        var pixels = new byte[96 * 128 * 4];
        pixels[3] = 255;
        var appearance = new Appearance
        {
            Id = 1,
            AppearanceType = APPEARANCE_TYPE.AppearanceObject,
            FrameGroup = { new FrameGroup { SpriteInfo = new SpriteInfo { SpriteId = { 77u } } } }
        };

        var container = AecContainerCodec.BuildExportContainer(
            [appearance],
            _ => new AecSpritePayload(pixels, 96, 128));
        var restored = AecContainerCodec.ReadEmbeddedAppearances(container).Single();

        restored.Sprites.Should().ContainSingle();
        restored.Sprites[0].Width.Should().Be(96);
        restored.Sprites[0].Height.Should().Be(128);
        restored.Sprites[0].Pixels.Should().Equal(pixels);
    }

    [Fact]
    public void AecCodec_StillReadsLegacyRawBgraPayload()
    {
        var pixels = SolidTile(1, 2, 3);

        var payload = AecContainerCodec.DecodeSpritePayload(pixels);

        payload.Width.Should().Be(32);
        payload.Height.Should().Be(32);
        payload.Pixels.Should().Equal(pixels);
    }

    private static byte[] SolidTile(byte b, byte g, byte r)
    {
        var pixels = new byte[32 * 32 * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = b;
            pixels[offset + 1] = g;
            pixels[offset + 2] = r;
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private static byte[] Pixel(byte[] pixels, int width, int x, int y)
    {
        var offset = (y * width + x) * 4;
        return pixels[offset..(offset + 4)];
    }
}
