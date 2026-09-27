using Narzedzia.Core.Appearances;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Services;

public static class AppearanceTextureMutator
{
    public static void ResizeSpriteIds(SpriteInfo info, int expectedCount, uint fillSpriteId)
    {
        expectedCount = Math.Max(0, expectedCount);

        while (info.SpriteId.Count > expectedCount)
        {
            info.SpriteId.RemoveAt(info.SpriteId.Count - 1);
        }

        while (info.SpriteId.Count < expectedCount)
        {
            info.SpriteId.Add(fillSpriteId);
        }
    }

    public static void SetLayerCount(SpriteInfo info, int layers, uint fillSpriteId = 0)
    {
        var safe = ToPositiveUInt(layers);
        if (UsesExplicitPatternFields(info))
        {
            info.PatternLayers = safe;
        }
        else
        {
            info.Layers = safe;
        }

        ResizeToCurrentLayout(info, fillSpriteId);
    }

    public static void SetPatternX(SpriteInfo info, int value, uint fillSpriteId = 0)
    {
        SetPatternField(info, value, axis: 'x');
        ResizeToCurrentLayout(info, fillSpriteId);
    }

    public static void SetPatternY(SpriteInfo info, int value, uint fillSpriteId = 0)
    {
        SetPatternField(info, value, axis: 'y');
        ResizeToCurrentLayout(info, fillSpriteId);
    }

    public static void SetPatternZ(SpriteInfo info, int value, uint fillSpriteId = 0)
    {
        SetPatternField(info, value, axis: 'z');
        ResizeToCurrentLayout(info, fillSpriteId);
    }

    public static void SetTileWidth(SpriteInfo info, int value, uint fillSpriteId = 0)
    {
        EnsureExplicitLayoutFields(info);
        info.PatternWidth = ToPositiveUInt(value);
        ResizeToCurrentLayout(info, fillSpriteId);
    }

    public static void SetTileHeight(SpriteInfo info, int value, uint fillSpriteId = 0)
    {
        EnsureExplicitLayoutFields(info);
        info.PatternHeight = ToPositiveUInt(value);
        ResizeToCurrentLayout(info, fillSpriteId);
    }

    public static void SetFrameCount(SpriteInfo info, int frames, uint fillSpriteId = 0)
    {
        var safe = ToPositiveUInt(frames);
        info.PatternFrames = safe;

        if (info.Animation is not null)
        {
            while (info.Animation.SpritePhase.Count > safe)
            {
                info.Animation.SpritePhase.RemoveAt(info.Animation.SpritePhase.Count - 1);
            }

            while (info.Animation.SpritePhase.Count < safe)
            {
                info.Animation.SpritePhase.Add(new SpritePhase { DurationMin = 100, DurationMax = 100 });
            }
        }

        ResizeToCurrentLayout(info, fillSpriteId);
    }

    public static void SetBoundingSquare(SpriteInfo info, int boundingSquare)
    {
        info.BoundingSquare = boundingSquare < 0 ? 0u : (uint)boundingSquare;
    }

    public static void SetOpaque(SpriteInfo info, bool isOpaque)
    {
        info.IsOpaque = isOpaque;
    }

    public static void AssignSprite(SpriteInfo info, int slotIndex, uint spriteId)
    {
        if (slotIndex < 0 || slotIndex >= info.SpriteId.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex), slotIndex, "Slot sprite'a jest poza zakresem.");
        }

        info.SpriteId[slotIndex] = spriteId;
    }

    public static void AssignSpriteAtContext(SpriteInfo info, TextureCursorContext context, uint spriteId)
    {
        if (context.SlotIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context), context.SlotIndex, "Slot sprite'a jest poza zakresem.");
        }

        ResizeToCurrentLayout(info, 0);
        if (context.SlotIndex >= info.SpriteId.Count)
        {
            ResizeSpriteIds(info, context.SlotIndex + 1, 0);
        }

        info.SpriteId[context.SlotIndex] = spriteId;
    }

    public static void EnsureLayoutForFirstSpriteDrop(
        SpriteInfo info,
        APPEARANCE_TYPE appearanceType,
        int spriteWidth,
        int spriteHeight)
    {
        if (info.SpriteId.Any(id => id != 0))
        {
            ResizeToCurrentLayout(info, 0);
            return;
        }

        var current = AppearanceLayoutInfo.From(info);
        var hasExplicit = UsesExplicitPatternFields(info);
        var tileWidth = hasExplicit ? current.TileWidth : 1;
        var tileHeight = hasExplicit ? current.TileHeight : 1;
        var patternX = appearanceType == APPEARANCE_TYPE.AppearanceOutfit
            ? Math.Max(4, current.PatternX)
            : Math.Max(1, current.PatternX);
        var patternY = Math.Max(1, current.PatternY);
        var patternZ = Math.Max(1, current.PatternZ);
        var layers = Math.Max(1, current.Layers);
        var frames = Math.Max(1, current.Frames);

        info.PatternWidth = (uint)tileWidth;
        info.PatternHeight = (uint)tileHeight;
        info.PatternLayers = (uint)layers;
        info.PatternX = (uint)patternX;
        info.PatternY = (uint)patternY;
        info.PatternZ = (uint)patternZ;
        info.PatternFrames = (uint)frames;

        var spriteSquare = Math.Max(spriteWidth, spriteHeight);
        if (spriteSquare > 0 && info.BoundingSquare < spriteSquare)
        {
            info.BoundingSquare = (uint)spriteSquare;
        }

        ResizeToCurrentLayout(info, 0);
    }

    public static int SpritesPerFrame(SpriteInfo info)
    {
        var layout = AppearanceLayoutInfo.From(info);
        return Math.Max(1, layout.TotalSprites / Math.Max(1, layout.Frames));
    }

    public static IReadOnlyList<uint> CopyFrame(SpriteInfo info, int frameIndex)
    {
        var spritesPerFrame = SpritesPerFrame(info);
        var start = Math.Clamp(frameIndex, 0, Math.Max(0, AppearanceLayoutInfo.From(info).Frames - 1)) * spritesPerFrame;
        return info.SpriteId.Skip(start).Take(spritesPerFrame).ToArray();
    }

    public static void PasteFrame(SpriteInfo info, int frameIndex, IReadOnlyList<uint> spriteIds)
    {
        var spritesPerFrame = SpritesPerFrame(info);
        var start = Math.Clamp(frameIndex, 0, Math.Max(0, AppearanceLayoutInfo.From(info).Frames - 1)) * spritesPerFrame;
        ResizeToCurrentLayout(info, 0);

        for (var i = 0; i < spritesPerFrame && i < spriteIds.Count && start + i < info.SpriteId.Count; i++)
        {
            info.SpriteId[start + i] = spriteIds[i];
        }
    }

    public static void ClearFrame(SpriteInfo info, int frameIndex)
    {
        var blank = Enumerable.Repeat(0u, SpritesPerFrame(info)).ToArray();
        PasteFrame(info, frameIndex, blank);
    }

    public static void ApplyDurationToAllFrames(SpriteAnimation animation, uint durationMin, uint durationMax)
    {
        foreach (var phase in animation.SpritePhase)
        {
            phase.DurationMin = durationMin;
            phase.DurationMax = durationMax;
        }
    }

    private static void ResizeToCurrentLayout(SpriteInfo info, uint fillSpriteId)
    {
        ResizeSpriteIds(info, AppearanceLayoutInfo.From(info).TotalSprites, fillSpriteId);
    }

    private static void SetPatternField(SpriteInfo info, int value, char axis)
    {
        var safe = ToPositiveUInt(value);
        if (UsesExplicitPatternFields(info))
        {
            switch (axis)
            {
                case 'x': info.PatternX = safe; break;
                case 'y': info.PatternY = safe; break;
                case 'z': info.PatternZ = safe; break;
            }
        }
        else
        {
            switch (axis)
            {
                case 'x': info.PatternWidth = safe; break;
                case 'y': info.PatternHeight = safe; break;
                case 'z': info.PatternDepth = safe; break;
            }
        }
    }

    private static bool UsesExplicitPatternFields(SpriteInfo info) =>
        info.PatternX > 0 || info.PatternY > 0 || info.PatternZ > 0 || info.PatternLayers > 0;

    private static void EnsureExplicitLayoutFields(SpriteInfo info)
    {
        if (UsesExplicitPatternFields(info))
        {
            return;
        }

        var layout = AppearanceLayoutInfo.From(info);
        info.PatternWidth = (uint)layout.TileWidth;
        info.PatternHeight = (uint)layout.TileHeight;
        info.PatternLayers = (uint)layout.Layers;
        info.PatternX = (uint)layout.PatternX;
        info.PatternY = (uint)layout.PatternY;
        info.PatternZ = (uint)layout.PatternZ;
        info.PatternFrames = (uint)layout.Frames;
    }

    private static uint ToPositiveUInt(int value) => value <= 0 ? 1u : (uint)value;
}
