using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Tests;

public sealed class AppearanceTextureLayoutTests
{
    [Fact]
    public void GetSpriteIndex_MatchesOriginalDatEditorOrdering()
    {
        var group = new FrameGroup
        {
            SpriteInfo = new SpriteInfo
            {
                PatternWidth = 4,
                PatternHeight = 3,
                PatternDepth = 2,
                Layers = 2,
                Animation = AnimationWithFrames(5)
            }
        };

        var index = AppearanceTextureLayout.GetSpriteIndex(
            group,
            layer: 1,
            patternX: 2,
            patternY: 1,
            patternZ: 1,
            frame: 3);

        index.Should().Be((((3 * 2 + 1) * 3 + 1) * 4 + 2) * 2 + 1);
    }

    [Fact]
    public void EnumerateVisibleSprites_ForObjectUsesSelectedFrameAndTextureGrid()
    {
        var appearance = AppearanceWithSpriteInfo(
            APPEARANCE_TYPE.AppearanceObject,
            new SpriteInfo
            {
                PatternWidth = 2,
                PatternHeight = 2,
                PatternDepth = 1,
                Layers = 1,
                Animation = AnimationWithFrames(2)
            },
            Enumerable.Range(100, 8).Select(id => (uint)id).ToArray());

        var parts = AppearanceTextureLayout.EnumerateVisibleSprites(
            appearance,
            new AppearanceRenderOptions(GroupIndex: 0, Direction: 0, Addon: 0, PatternZ: 0, Frame: 1, BlendLayers: true, FullAddons: false));

        parts.Select(part => part.SpriteId).Should().Equal(104u, 105u, 106u, 107u);
        parts.Select(part => (part.PatternX, part.PatternY)).Should().Equal((0, 0), (1, 0), (0, 1), (1, 1));
    }

    [Fact]
    public void EnumerateVisibleSprites_ForOutfitCanBlendAllAddonLayers()
    {
        var appearance = AppearanceWithSpriteInfo(
            APPEARANCE_TYPE.AppearanceOutfit,
            new SpriteInfo
            {
                PatternWidth = 4,
                PatternHeight = 3,
                PatternDepth = 1,
                Layers = 2,
                Animation = AnimationWithFrames(1)
            },
            Enumerable.Range(1, 24).Select(id => (uint)id).ToArray());

        var parts = AppearanceTextureLayout.EnumerateVisibleSprites(
            appearance,
            new AppearanceRenderOptions(GroupIndex: 0, Direction: 2, Addon: 0, PatternZ: 0, Frame: 0, BlendLayers: true, FullAddons: true));

        parts.Should().HaveCount(6);
        parts.Select(part => (part.Layer, part.PatternX, part.PatternY)).Should().Equal(
            (0, 2, 0),
            (1, 2, 0),
            (0, 2, 1),
            (1, 2, 1),
            (0, 2, 2),
            (1, 2, 2));
    }

    [Fact]
    public void ResolveCursorContext_ForTwoByTwoTextureUsesVisualTileUnderPointer()
    {
        var spriteInfo = new SpriteInfo
        {
            PatternWidth = 2,
            PatternHeight = 2,
            PatternLayers = 1,
            PatternX = 4,
            PatternY = 1,
            PatternZ = 1,
            PatternFrames = 2
        };
        spriteInfo.SpriteId.Add(Enumerable.Range(1, 32).Select(id => (uint)id));

        var context = AppearanceTextureLayout.ResolveCursorContext(
            spriteInfo,
            groupIndex: 0,
            direction: 1,
            addon: 0,
            patternZ: 0,
            frame: 1,
            layer: 0,
            pointerX: 150,
            pointerY: 50,
            surfaceWidth: 200,
            surfaceHeight: 200,
            renderedWidth: 128,
            renderedHeight: 128);

        context.TileX.Should().Be(0);
        context.TileY.Should().Be(1);
        context.SlotIndex.Should().Be(22);
    }

    [Fact]
    public void GetSpriteIndex_ForExplicitTwoByTwoOutfitMatchesLegacyOrdering()
    {
        var spriteInfo = new SpriteInfo
        {
            PatternWidth = 2,
            PatternHeight = 2,
            PatternLayers = 2,
            PatternX = 4,
            PatternY = 3,
            PatternZ = 1,
            PatternFrames = 2
        };

        var index = AppearanceTextureLayout.GetSpriteIndex(
            spriteInfo,
            tileX: 1,
            tileY: 0,
            layer: 1,
            patternX: 3,
            patternY: 2,
            patternZ: 0,
            frame: 1);

        index.Should().Be((((((1 * 1 + 0) * 3 + 2) * 4 + 3) * 2 + 1) * 2 + 0) * 2 + 1);
    }

    [Theory]
    [InlineData(0, 0)] // North → patternX=0
    [InlineData(1, 1)] // East  → patternX=1
    [InlineData(2, 2)] // South → patternX=2
    [InlineData(3, 3)] // West  → patternX=3
    public void DirectionToPatternX_CanonicalOrder_MatchesWpf(int direction, int expectedPatternX)
    {
        // Wzorzec zgodności z DatEditor.xaml.cs:989-999 (WPF):
        // up=0 / right=1 / down=2 / left=3 — i mapuje bezpośrednio na patternX
        var group = new FrameGroup
        {
            SpriteInfo = new SpriteInfo
            {
                PatternWidth = 4,
                PatternHeight = 1,
                PatternDepth = 1,
                Layers = 1,
            },
        };

        var index = AppearanceTextureLayout.GetSpriteIndex(
            group, layer: 0, patternX: direction, patternY: 0, patternZ: 0, frame: 0);

        index.Should().Be(expectedPatternX,
            "kierunek (N=0,E=1,S=2,W=3) musi mapować bezpośrednio na patternX");
    }

    [Fact]
    public void TextureDirection_AssignDifferentSprites_PerDirection_DoesNotLoseOthers()
    {
        // Symulacja round-trip: user assignuje sprite #100 na N, #200 na E, potem wraca do N.
        // Każdy direction ma własny slot — none-overlapping write.
        var spriteInfo = new SpriteInfo
        {
            PatternWidth = 4,
            PatternHeight = 1,
            PatternDepth = 1,
            Layers = 1,
        };
        spriteInfo.SpriteId.Add(new uint[] { 0, 0, 0, 0 });

        var group = new FrameGroup { SpriteInfo = spriteInfo };

        var idxN = AppearanceTextureLayout.GetSpriteIndex(group, 0, 0, 0, 0, 0);
        var idxE = AppearanceTextureLayout.GetSpriteIndex(group, 0, 1, 0, 0, 0);
        var idxS = AppearanceTextureLayout.GetSpriteIndex(group, 0, 2, 0, 0, 0);
        var idxW = AppearanceTextureLayout.GetSpriteIndex(group, 0, 3, 0, 0, 0);

        spriteInfo.SpriteId[idxN] = 100;
        spriteInfo.SpriteId[idxE] = 200;
        spriteInfo.SpriteId[idxS] = 300;
        spriteInfo.SpriteId[idxW] = 400;

        // Re-czytanie po dowolnym przełączaniu kierunku — wartości stabilne.
        spriteInfo.SpriteId[AppearanceTextureLayout.GetSpriteIndex(group, 0, 0, 0, 0, 0)].Should().Be(100);
        spriteInfo.SpriteId[AppearanceTextureLayout.GetSpriteIndex(group, 0, 1, 0, 0, 0)].Should().Be(200);
        spriteInfo.SpriteId[AppearanceTextureLayout.GetSpriteIndex(group, 0, 2, 0, 0, 0)].Should().Be(300);
        spriteInfo.SpriteId[AppearanceTextureLayout.GetSpriteIndex(group, 0, 3, 0, 0, 0)].Should().Be(400);
    }

    private static Appearance AppearanceWithSpriteInfo(
        APPEARANCE_TYPE type,
        SpriteInfo spriteInfo,
        IReadOnlyList<uint> spriteIds)
    {
        spriteInfo.SpriteId.Add(spriteIds);

        return new Appearance
        {
            Id = 1,
            AppearanceType = type,
            FrameGroup =
            {
                new FrameGroup
                {
                    FixedFrameGroup = type == APPEARANCE_TYPE.AppearanceOutfit
                        ? FIXED_FRAME_GROUP.OutfitIdle
                        : FIXED_FRAME_GROUP.ObjectInitial,
                    SpriteInfo = spriteInfo
                }
            }
        };
    }

    private static SpriteAnimation AnimationWithFrames(int frames)
    {
        var animation = new SpriteAnimation
        {
            LoopType = ANIMATION_LOOP_TYPE.Infinite
        };

        for (var i = 0; i < frames; i++)
        {
            animation.SpritePhase.Add(new SpritePhase { DurationMin = 100, DurationMax = 100 });
        }

        return animation;
    }
}
