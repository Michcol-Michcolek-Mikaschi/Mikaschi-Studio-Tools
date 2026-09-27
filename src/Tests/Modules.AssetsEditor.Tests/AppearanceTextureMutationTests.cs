using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Tests;

public sealed class AppearanceTextureMutationTests
{
    [Fact]
    public void ResizeSpriteSlots_GrowsAndShrinksToExpectedCount()
    {
        var info = new SpriteInfo { PatternWidth = 1, PatternHeight = 1, PatternDepth = 1, Layers = 1, PatternFrames = 1 };
        info.SpriteId.Add(42);

        AppearanceTextureMutator.ResizeSpriteIds(info, expectedCount: 4, fillSpriteId: 0);
        info.SpriteId.Should().Equal(42u, 0u, 0u, 0u);

        AppearanceTextureMutator.ResizeSpriteIds(info, expectedCount: 2, fillSpriteId: 0);
        info.SpriteId.Should().Equal(42u, 0u);
    }

    [Fact]
    public void SetFrameCount_ResizesSpriteIdsAndAnimationPhases()
    {
        var info = new SpriteInfo
        {
            PatternWidth = 1,
            PatternHeight = 1,
            PatternDepth = 1,
            Layers = 1,
            PatternFrames = 1,
            Animation = new SpriteAnimation()
        };
        info.SpriteId.Add(7);

        AppearanceTextureMutator.SetFrameCount(info, 3, fillSpriteId: 0);

        info.PatternFrames.Should().Be(3);
        info.Animation.SpritePhase.Should().HaveCount(3);
        info.SpriteId.Should().Equal(7u, 0u, 0u);
    }

    [Fact]
    public void EnsureLayoutForFirstSpriteDrop_ConvertsNewOutfitToExplicitDirectionalLayout()
    {
        var info = new SpriteInfo
        {
            PatternWidth = 4,
            PatternHeight = 1,
            PatternDepth = 1,
            Layers = 1,
            PatternFrames = 1
        };
        info.SpriteId.Add(0);

        AppearanceTextureMutator.EnsureLayoutForFirstSpriteDrop(
            info,
            APPEARANCE_TYPE.AppearanceOutfit,
            spriteWidth: 64,
            spriteHeight: 64);

        info.PatternWidth.Should().Be(1);
        info.PatternHeight.Should().Be(1);
        info.PatternLayers.Should().Be(1);
        info.PatternX.Should().Be(4);
        info.PatternY.Should().Be(1);
        info.PatternZ.Should().Be(1);
        info.BoundingSquare.Should().Be(64);
        info.SpriteId.Should().Equal(0u, 0u, 0u, 0u);
    }

    [Fact]
    public void AssignSpriteAtContext_GrowsSpriteIdsAndPreservesExistingSlots()
    {
        var info = new SpriteInfo
        {
            PatternWidth = 1,
            PatternHeight = 1,
            PatternLayers = 1,
            PatternX = 4,
            PatternY = 1,
            PatternZ = 1,
            PatternFrames = 2
        };
        info.SpriteId.Add([10u, 11u, 12u, 13u]);

        var context = new TextureCursorContext(
            GroupIndex: 0,
            Direction: 1,
            Addon: 0,
            PatternZ: 0,
            Frame: 1,
            Layer: 0,
            TileX: 0,
            TileY: 0,
            SlotIndex: 5);

        AppearanceTextureMutator.AssignSpriteAtContext(info, context, 900);

        info.SpriteId.Should().Equal(10u, 11u, 12u, 13u, 0u, 900u, 0u, 0u);
    }

    [Fact]
    public void SetTileSize_ConvertsLegacyLayoutToExplicitAndResizesSlots()
    {
        var info = new SpriteInfo
        {
            PatternWidth = 4,
            PatternHeight = 1,
            PatternDepth = 1,
            Layers = 1,
            PatternFrames = 1
        };
        info.SpriteId.Add([1u, 2u, 3u, 4u]);

        AppearanceTextureMutator.SetTileWidth(info, 2);
        AppearanceTextureMutator.SetTileHeight(info, 2);

        info.PatternX.Should().Be(4);
        info.PatternY.Should().Be(1);
        info.PatternZ.Should().Be(1);
        info.PatternWidth.Should().Be(2);
        info.PatternHeight.Should().Be(2);
        info.PatternLayers.Should().Be(1);
        info.SpriteId.Count.Should().Be(16);
    }
}
