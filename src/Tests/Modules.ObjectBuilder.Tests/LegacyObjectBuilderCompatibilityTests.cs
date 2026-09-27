using FluentAssertions;
using Modules.ObjectBuilder.Services;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;
using Xunit;

namespace Modules.ObjectBuilder.Tests;

public sealed class LegacyObjectBuilderCompatibilityTests
{
    public static TheoryData<int, uint, uint> EveryKnownProtocol()
    {
        var data = new TheoryData<int, uint, uint>();
        foreach (var protocol in LegacyObjectBuilderProtocolCatalog.All)
        {
            data.Add(protocol.ClientVersion, protocol.DatSignature, protocol.SprSignature);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryKnownProtocol))]
    public void Catalog_ResolvesEveryOriginalObjectBuilderDatAndSprSignature(
        int clientVersion,
        uint datSignature,
        uint sprSignature)
    {
        var format = LegacyObjectBuilderProtocolCatalog.GetMetadataFormat(clientVersion);

        var resolved = LegacyObjectBuilderProtocolCatalog.Resolve(format, datSignature, sprSignature);

        resolved.DatSignature.Should().Be(datSignature);
        resolved.SprSignature.Should().Be(sprSignature);
        resolved.MetadataFormat.Should().Be(format);
        LegacyObjectBuilderProtocolCatalog.AreSignaturesCompatible(datSignature, sprSignature).Should().BeTrue();
    }

    [Fact]
    public void Catalog_RejectsMismatchedKnownDatAndSprProtocols_ButAllowsCustomSignatures()
    {
        LegacyObjectBuilderProtocolCatalog.AreSignaturesCompatible(0x3DFF4B2A, 0x4C220594)
            .Should().BeFalse();
        LegacyObjectBuilderProtocolCatalog.AreSignaturesCompatible(0xDEADBEEF, 0x4C220594)
            .Should().BeTrue();
        LegacyObjectBuilderProtocolCatalog.AreSignaturesCompatible(0x4C28B721, 0xDEADBEEF)
            .Should().BeTrue();
    }

    [Fact]
    public void PairValidator_AllowsMixedSpriteIdTableWidths_WithWarning()
    {
        var dat = CreateCompleteDat(0, LegacyObjectBuilderCapabilities.For(
            DatMetadataFormat.Versions780To854, 0));
        var spr = new SprFile
        {
            Signature = 0,
            ExtendedSprites = true,
            Sprites = Enumerable.Range(0, 1004).Select(_ => new SpriteEntry()).ToList()
        };

        var result = LegacyAssetPairValidator.Validate(dat, new DatParserOptions
        {
            ExtendedSprites = false,
            MetadataFormat = DatMetadataFormat.Versions780To854
        }, spr);

        result.IsCompatible.Should().BeTrue();
        result.Warnings.Should().ContainSingle(message => message.Contains("16-bitowych") && message.Contains("32-bitowa"));
    }

    [Fact]
    public void PairValidator_WarnsWhenDatReferencesMissingSprite()
    {
        var dat = CreateCompleteDat(0, LegacyObjectBuilderCapabilities.For(
            DatMetadataFormat.Versions780To854, 0));
        var spr = new SprFile
        {
            Signature = 0,
            ExtendedSprites = false,
            Sprites = Enumerable.Range(0, 900).Select(_ => new SpriteEntry()).ToList()
        };

        var result = LegacyAssetPairValidator.Validate(dat, new DatParserOptions
        {
            ExtendedSprites = false,
            MetadataFormat = DatMetadataFormat.Versions780To854
        }, spr);

        result.IsCompatible.Should().BeTrue();
        result.Warnings.Should().ContainSingle(message => message.Contains("sprite ID 1003") && message.Contains("900"));
    }

    [Fact]
    public void PairValidator_RejectsKnownMismatchedSignatures()
    {
        var dat = CreateCompleteDat(0x3DFF4B2A, LegacyObjectBuilderCapabilities.For(
            DatMetadataFormat.Versions710To730, 0x3DFF4B2A));
        var spr = new SprFile
        {
            Signature = 0x4C220594,
            ExtendedSprites = false
        };

        var result = LegacyAssetPairValidator.Validate(dat, new DatParserOptions
        {
            ExtendedSprites = false,
            MetadataFormat = DatMetadataFormat.Versions710To730
        }, spr);

        result.IsCompatible.Should().BeFalse();
        result.Error.Should().Contain("różnych protokołów");
    }

    [Theory]
    [InlineData(954, false, false, false)]
    [InlineData(960, true, false, false)]
    [InlineData(1041, true, false, false)]
    [InlineData(1050, true, true, false)]
    [InlineData(1056, true, true, false)]
    [InlineData(1057, true, true, true)]
    public void Catalog_UsesTheSameClientFeatureBoundariesAsObjectBuilder(
        int clientVersion,
        bool extendedSprites,
        bool improvedAnimations,
        bool frameGroups)
    {
        var protocol = LegacyObjectBuilderProtocolCatalog.All.First(version => version.ClientVersion == clientVersion);

        protocol.ExtendedSprites.Should().Be(extendedSprites);
        protocol.ImprovedAnimations.Should().Be(improvedAnimations);
        protocol.FrameGroups.Should().Be(frameGroups);
    }

    [Theory]
    [MemberData(nameof(EveryKnownProtocol))]
    public void EveryProtocol_RoundTripsItsCompleteSupportedPropertiesSet(
        int clientVersion,
        uint datSignature,
        uint sprSignature)
    {
        _ = sprSignature;
        var format = LegacyObjectBuilderProtocolCatalog.GetMetadataFormat(clientVersion);
        var protocol = LegacyObjectBuilderProtocolCatalog.Resolve(format, datSignature);
        var capabilities = LegacyObjectBuilderCapabilities.For(format, datSignature);
        var options = new DatParserOptions
        {
            ExtendedSprites = protocol.ExtendedSprites,
            ImprovedAnimations = protocol.ImprovedAnimations,
            FrameGroups = protocol.FrameGroups,
            MetadataFormat = format
        };
        var source = CreateCompleteDat(datSignature, capabilities);

        using var stream = new MemoryStream();
        DatWriter.Write(stream, source, options);
        stream.Position = 0;

        var parsed = DatParser.Parse(stream, options);

        stream.Position.Should().Be(stream.Length);
        var item = parsed.Items.Should().ContainSingle().Subject;
        item.IsGround.Should().BeTrue();
        item.GroundSpeed.Should().Be(140);
        item.IsOnBottom.Should().BeTrue();
        item.IsOnTop.Should().BeTrue();
        item.IsContainer.Should().BeTrue();
        item.IsStackable.Should().BeTrue();
        item.ForceUse.Should().BeTrue();
        item.IsMultiUse.Should().BeTrue();
        item.IsWritable.Should().BeTrue();
        item.MaxReadWriteChars.Should().Be(123);
        item.IsWritableOnce.Should().BeTrue();
        item.MaxReadChars.Should().Be(321);
        item.IsFluidContainer.Should().BeTrue();
        item.IsFluid.Should().BeTrue();
        item.IsUnpassable.Should().BeTrue();
        item.IsUnmoveable.Should().BeTrue();
        item.BlockMissile.Should().BeTrue();
        item.BlockPathfinder.Should().BeTrue();
        item.IsPickupable.Should().BeTrue();
        item.IsRotatable.Should().BeTrue();
        item.HasLight.Should().BeTrue();
        item.LightColor.Should().Be(156);
        item.LightLevel.Should().Be(3);
        item.HasOffset.Should().BeTrue();
        item.OffsetX.Should().Be(capabilities.OffsetMinimum == 8 ? (short)8 : (short)-5);
        item.OffsetY.Should().Be(capabilities.OffsetMinimum == 8 ? (short)8 : (short)7);
        item.HasElevation.Should().BeTrue();
        item.Elevation.Should().Be(12);
        item.IsLyingObject.Should().BeTrue();
        item.HasMiniMapColor.Should().BeTrue();
        item.MiniMapColor.Should().Be(129);
        item.HasLensHelp.Should().BeTrue();
        item.LensHelp.Should().Be(1104);
        item.IsFullGround.Should().BeTrue();
        item.IsGroundBorder.Should().Be(capabilities.GroundBorder);
        item.IsHangable.Should().Be(capabilities.WallHooks);
        item.IsHorizontal.Should().Be(capabilities.WallHooks);
        item.IsVertical.Should().Be(capabilities.WallHooks);
        item.DontHide.Should().Be(capabilities.DontHide);
        item.IgnoreLook.Should().Be(capabilities.DontHide);
        item.IsTranslucent.Should().Be(capabilities.Translucent);
        item.HasCharges.Should().Be(capabilities.Charges);
        item.FloorChange.Should().Be(capabilities.FloorChange);
        item.HasCloth.Should().Be(capabilities.Equip);
        item.HasMarketInfo.Should().Be(capabilities.Market);
        item.NoMoveAnimation.Should().Be(capabilities.NoMoveAnimation);
        item.HasDefaultAction.Should().Be(capabilities.DefaultAction);
        item.IsUsable.Should().Be(capabilities.Useable);
        item.IsWrappable.Should().Be(capabilities.CanPersistWrapping);
        item.IsUnwrappable.Should().Be(capabilities.CanPersistWrapping);
        item.HasBones.Should().Be(capabilities.Bones);

        parsed.Outfits.Should().ContainSingle().Which.AnimateAlways.Should().BeTrue();
        parsed.Effects.Should().ContainSingle().Which.IsTopEffect.Should().Be(capabilities.CanPersistTopEffect);
        parsed.Missiles.Should().ContainSingle();
    }

    [Fact]
    public void Client854_ShowsOnlyPropertiesSupportedByOriginalObjectBuilder()
    {
        var capabilities = LegacyObjectBuilderCapabilities.For(
            DatMetadataFormat.Versions780To854,
            0x4B1E_2CAA);

        capabilities.GroundBorder.Should().BeTrue();
        capabilities.WallHooks.Should().BeTrue();
        capabilities.DontHide.Should().BeTrue();
        capabilities.Charges.Should().BeTrue();
        capabilities.FloorChange.Should().BeTrue();
        capabilities.Bones.Should().BeTrue();
        capabilities.PatternZ.Should().BeTrue();
        capabilities.OffsetMinimum.Should().Be(-256);
        capabilities.OffsetMaximum.Should().Be(256);

        capabilities.Translucent.Should().BeFalse();
        capabilities.Equip.Should().BeFalse();
        capabilities.Market.Should().BeFalse();
        capabilities.NoMoveAnimation.Should().BeFalse();
        capabilities.DefaultAction.Should().BeFalse();
        capabilities.Useable.Should().BeFalse();
        capabilities.Wrapping.Should().BeFalse();
        capabilities.TopEffect.Should().BeFalse();
    }

    [Theory]
    [InlineData(0x4B98FF53u, false, false, false)] // 8.55
    [InlineData(0x4C28B721u, true, false, false)]  // 8.60
    [InlineData(0x4DBAA20Bu, true, true, false)]   // 9.00
    [InlineData(0x4EE71DE5u, true, true, true)]    // 9.40
    public void Client855To986_UsesExactFeatureThresholds(
        uint signature,
        bool translucent,
        bool equip,
        bool market)
    {
        var capabilities = LegacyObjectBuilderCapabilities.For(
            DatMetadataFormat.Versions855To986,
            signature);

        capabilities.Translucent.Should().Be(translucent);
        capabilities.Equip.Should().Be(equip);
        capabilities.Market.Should().Be(market);
    }

    [Theory]
    [InlineData(0x51E3F8C3u, false, false)] // 10.10
    [InlineData(0x526A5068u, true, false)]  // 10.21
    [InlineData(0x00004086u, true, true)]   // 10.92
    public void Client1010AndNewer_UsesActionAndWrappingThresholds(
        uint signature,
        bool action,
        bool wrapping)
    {
        var capabilities = LegacyObjectBuilderCapabilities.For(
            DatMetadataFormat.Versions1010AndNewer,
            signature);

        capabilities.NoMoveAnimation.Should().BeTrue();
        capabilities.DefaultAction.Should().Be(action);
        capabilities.Useable.Should().Be(action);
        capabilities.Wrapping.Should().Be(wrapping);
        capabilities.TopEffect.Should().Be(wrapping);
    }

    [Fact]
    public void Client750_UsesFixedOffsetAndEarlyFlags()
    {
        var capabilities = LegacyObjectBuilderCapabilities.For(
            DatMetadataFormat.Versions740To750,
            0x42F8_1973);

        capabilities.OffsetMinimum.Should().Be(8);
        capabilities.OffsetMaximum.Should().Be(8);
        capabilities.FloorChange.Should().BeTrue();
        capabilities.Wrapping.Should().BeTrue();
        capabilities.TopEffect.Should().BeTrue();
        capabilities.GroundBorder.Should().BeFalse();
        capabilities.WallHooks.Should().BeFalse();
    }

    [Theory]
    [InlineData(0x437B2B8Fu, true, false, true, false)]   // 7.55: UI pokazuje, writer3 nie zapisuje
    [InlineData(0x44CE4743u, true, true, true, false)]    // 7.80: writer4 zapisuje wrapping, nie Top Effect
    [InlineData(0x459E7B73u, true, true, true, false)]    // 7.92
    [InlineData(0x467FD7E6u, false, false, false, false)] // 8.00
    [InlineData(0x00004086u, true, true, true, true)]     // 10.92
    public void Compatibility_DistinguishesOriginalUiFromDatWriterSupport(
        uint signature,
        bool wrappingVisible,
        bool wrappingPersisted,
        bool topEffectVisible,
        bool topEffectPersisted)
    {
        var format = DatParser.GuessMetadataFormat(signature);

        var capabilities = LegacyObjectBuilderCapabilities.For(format, signature);

        capabilities.Wrapping.Should().Be(wrappingVisible);
        capabilities.CanPersistWrapping.Should().Be(wrappingPersisted);
        capabilities.TopEffect.Should().Be(topEffectVisible);
        capabilities.CanPersistTopEffect.Should().Be(topEffectPersisted);
    }

    [Theory]
    [InlineData(0x42F81973u, 750, 8, 8)]
    [InlineData(0x437B2B8Fu, 755, -256, 256)]
    [InlineData(0x4B98FF53u, 855, -256, 256)]
    [InlineData(0x4C28B721u, 860, -256, 256)]
    [InlineData(0x4DBAA20Bu, 900, -256, 256)]
    [InlineData(0x4EE71DE5u, 940, -256, 256)]
    [InlineData(0x51E3F8C3u, 1010, -256, 256)]
    [InlineData(0x526A5068u, 1021, -256, 256)]
    [InlineData(0x00004086u, 1092, -256, 256)]
    public void Compatibility_UsesExactPropertyBoundaryVersion(
        uint signature,
        int expectedVersion,
        int offsetMinimum,
        int offsetMaximum)
    {
        var capabilities = LegacyObjectBuilderCapabilities.For(
            DatParser.GuessMetadataFormat(signature),
            signature);

        capabilities.ClientVersion.Should().Be(expectedVersion);
        capabilities.OffsetMinimum.Should().Be(offsetMinimum);
        capabilities.OffsetMaximum.Should().Be(offsetMaximum);
        capabilities.Translucent.Should().Be(expectedVersion >= 860);
        capabilities.Equip.Should().Be(expectedVersion >= 900);
        capabilities.Market.Should().Be(expectedVersion >= 940);
        capabilities.NoMoveAnimation.Should().Be(expectedVersion >= 1010);
        capabilities.DefaultAction.Should().Be(expectedVersion >= 1021);
        capabilities.Useable.Should().Be(expectedVersion >= 1021);
    }

    private static DatFile CreateCompleteDat(uint signature, LegacyObjectBuilderCapabilities capabilities)
    {
        var fixedOffset = capabilities.OffsetMinimum == 8;
        var dat = new DatFile
        {
            Signature = signature,
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
            GroundSpeed = 140,
            IsGroundBorder = capabilities.GroundBorder,
            IsOnBottom = true,
            IsOnTop = true,
            IsContainer = true,
            IsStackable = true,
            ForceUse = true,
            IsMultiUse = true,
            HasCharges = capabilities.Charges,
            IsWritable = true,
            MaxReadWriteChars = 123,
            IsWritableOnce = true,
            MaxReadChars = 321,
            IsFluidContainer = true,
            IsFluid = true,
            IsUnpassable = true,
            IsUnmoveable = true,
            BlockMissile = true,
            BlockPathfinder = true,
            NoMoveAnimation = capabilities.NoMoveAnimation,
            IsPickupable = true,
            IsHangable = capabilities.WallHooks,
            IsHorizontal = capabilities.WallHooks,
            IsVertical = capabilities.WallHooks,
            IsRotatable = true,
            HasLight = true,
            LightLevel = 3,
            LightColor = 156,
            DontHide = capabilities.DontHide,
            IsTranslucent = capabilities.Translucent,
            FloorChange = capabilities.FloorChange,
            HasOffset = true,
            OffsetX = fixedOffset ? (short)8 : (short)-5,
            OffsetY = fixedOffset ? (short)8 : (short)7,
            HasElevation = true,
            Elevation = 12,
            IsLyingObject = true,
            HasMiniMapColor = true,
            MiniMapColor = 129,
            HasLensHelp = true,
            LensHelp = 1104,
            IsFullGround = true,
            IgnoreLook = capabilities.DontHide,
            HasCloth = capabilities.Equip,
            ClothSlot = 6,
            HasMarketInfo = capabilities.Market,
            MarketCategory = 4,
            MarketTradeAs = 100,
            MarketShowAs = 100,
            MarketName = "Object Builder",
            MarketRestrictProfession = 3,
            MarketRestrictLevel = 80,
            IsWrappable = capabilities.CanPersistWrapping,
            IsUnwrappable = capabilities.CanPersistWrapping,
            IsUsable = capabilities.Useable,
            HasDefaultAction = capabilities.DefaultAction,
            DefaultAction = 2,
            HasBones = capabilities.Bones,
            BoneOffsets = [1, 2, 3, 4, 5, 6, 7, 8],
            FrameGroups = { CreateGroup(1000) }
        });
        dat.Outfits.Add(new DatThingType
        {
            Id = 1,
            Category = DatThingCategory.Outfits,
            HasLight = true,
            LightLevel = 4,
            LightColor = 42,
            HasOffset = true,
            OffsetX = fixedOffset ? (short)8 : (short)-2,
            OffsetY = fixedOffset ? (short)8 : (short)3,
            AnimateAlways = true,
            HasBones = capabilities.Bones,
            BoneOffsets = [8, 7, 6, 5, 4, 3, 2, 1],
            FrameGroups = { CreateGroup(1001) }
        });
        dat.Effects.Add(new DatThingType
        {
            Id = 1,
            Category = DatThingCategory.Effects,
            HasLight = true,
            LightLevel = 5,
            LightColor = 84,
            HasOffset = true,
            OffsetX = fixedOffset ? (short)8 : (short)-1,
            OffsetY = fixedOffset ? (short)8 : (short)1,
            IsTopEffect = capabilities.CanPersistTopEffect,
            HasBones = capabilities.Bones,
            BoneOffsets = [2, 4, 6, 8, 10, 12, 14, 16],
            FrameGroups = { CreateGroup(1002) }
        });
        dat.Missiles.Add(new DatThingType
        {
            Id = 1,
            Category = DatThingCategory.Missiles,
            FrameGroups = { CreateGroup(1003) }
        });
        return dat;
    }

    private static DatThingFrameGroup CreateGroup(uint spriteId) => new()
    {
        Width = 1,
        Height = 1,
        Layers = 1,
        PatternX = 1,
        PatternY = 1,
        PatternZ = 1,
        Frames = 1,
        SpriteIds = [spriteId]
    };
}
