using Narzedzia.Core.Appearances;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Services;

public sealed record AppearanceRenderOptions(
    int GroupIndex,
    int Direction,
    int Addon,
    int PatternZ,
    int Frame,
    bool BlendLayers,
    bool FullAddons,
    bool ColorizeOutfit = true,
    int HeadColor = 0,
    int BodyColor = 0,
    int LegsColor = 0,
    int FeetColor = 0);

public sealed record AppearanceSpritePart(
    int Index,
    uint SpriteId,
    int Layer,
    int TileX,
    int TileY,
    int PatternX,
    int PatternY,
    int PatternZ,
    int Frame);

public sealed record RenderedSpriteImage(byte[] Pixels, int Width, int Height);

public static class AppearanceTextureLayout
{
    public static TextureCursorContext ResolveCursorContext(
        SpriteInfo spriteInfo,
        int groupIndex,
        int direction,
        int addon,
        int patternZ,
        int frame,
        int layer,
        double pointerX,
        double pointerY,
        double surfaceWidth,
        double surfaceHeight,
        int renderedWidth,
        int renderedHeight)
    {
        var layout = AppearanceLayoutInfo.From(spriteInfo);
        var safeLayer = Math.Clamp(layer, 0, layout.Layers - 1);
        var safeDirection = Math.Clamp(direction, 0, layout.PatternX - 1);
        var safeAddon = Math.Clamp(addon, 0, layout.PatternY - 1);
        var safePatternZ = Math.Clamp(patternZ, 0, layout.PatternZ - 1);
        var safeFrame = PositiveModulo(frame, layout.Frames);

        var imageRect = GetUniformImageRect(
            surfaceWidth,
            surfaceHeight,
            renderedWidth <= 0 ? layout.TileWidth : renderedWidth,
            renderedHeight <= 0 ? layout.TileHeight : renderedHeight);

        var localX = imageRect.Width <= 0 ? 0 : (pointerX - imageRect.X) / imageRect.Width;
        var localY = imageRect.Height <= 0 ? 0 : (pointerY - imageRect.Y) / imageRect.Height;
        localX = Math.Clamp(localX, 0, 0.999999d);
        localY = Math.Clamp(localY, 0, 0.999999d);

        var visualColumn = Math.Clamp((int)Math.Floor(localX * layout.TileWidth), 0, layout.TileWidth - 1);
        var visualRow = Math.Clamp((int)Math.Floor(localY * layout.TileHeight), 0, layout.TileHeight - 1);
        var tileX = layout.TileWidth - visualColumn - 1;
        var tileY = layout.TileHeight - visualRow - 1;
        var slotIndex = GetSpriteIndex(
            spriteInfo,
            tileX,
            tileY,
            safeLayer,
            safeDirection,
            safeAddon,
            safePatternZ,
            safeFrame);

        return new TextureCursorContext(
            groupIndex,
            safeDirection,
            safeAddon,
            safePatternZ,
            safeFrame,
            safeLayer,
            tileX,
            tileY,
            slotIndex);
    }

    public static int GetSpriteIndex(
        FrameGroup frameGroup,
        int layer,
        int patternX,
        int patternY,
        int patternZ,
        int frame)
    {
        var spriteInfo = frameGroup.SpriteInfo
            ?? throw new ArgumentException("FrameGroup nie ma SpriteInfo.", nameof(frameGroup));

        var layout = AppearanceLayoutInfo.From(spriteInfo);

        var safeFrame = PositiveModulo(frame, layout.Frames);
        var safeZ = Math.Clamp(patternZ, 0, layout.PatternZ - 1);
        var safeY = Math.Clamp(patternY, 0, layout.PatternY - 1);
        var safeX = Math.Clamp(patternX, 0, layout.PatternX - 1);
        var safeLayer = Math.Clamp(layer, 0, layout.Layers - 1);

        var index = safeFrame;
        index = index * layout.PatternZ + safeZ;
        index = index * layout.PatternY + safeY;
        index = index * layout.PatternX + safeX;
        index = index * layout.Layers + safeLayer;

        return index;
    }

    public static int GetSpriteIndex(
        SpriteInfo spriteInfo,
        int tileX,
        int tileY,
        int layer,
        int patternX,
        int patternY,
        int patternZ,
        int frame)
    {
        var layout = AppearanceLayoutInfo.From(spriteInfo);

        var safeTileX = Math.Clamp(tileX, 0, layout.TileWidth - 1);
        var safeTileY = Math.Clamp(tileY, 0, layout.TileHeight - 1);
        var safeLayer = Math.Clamp(layer, 0, layout.Layers - 1);
        var safePatternX = Math.Clamp(patternX, 0, layout.PatternX - 1);
        var safePatternY = Math.Clamp(patternY, 0, layout.PatternY - 1);
        var safePatternZ = Math.Clamp(patternZ, 0, layout.PatternZ - 1);
        var safeFrame = PositiveModulo(frame, layout.Frames);

        var index = safeFrame;
        index = index * layout.PatternZ + safePatternZ;
        index = index * layout.PatternY + safePatternY;
        index = index * layout.PatternX + safePatternX;
        index = index * layout.Layers + safeLayer;
        index = index * layout.TileHeight + safeTileY;
        index = index * layout.TileWidth + safeTileX;
        return index;
    }

    public static IReadOnlyList<AppearanceSpritePart> EnumerateSpriteSlots(
        Appearance appearance,
        int groupIndex)
    {
        var group = GetFrameGroup(appearance, groupIndex);
        var spriteInfo = group?.SpriteInfo;
        if (spriteInfo is null)
        {
            return [];
        }

        var layout = AppearanceLayoutInfo.From(spriteInfo);
        var tileWidth = layout.TileWidth;
        var tileHeight = layout.TileHeight;
        var patternXCount = layout.PatternX;
        var patternYCount = layout.PatternY;
        var patternZCount = layout.PatternZ;
        var layers = layout.Layers;
        var frames = layout.Frames;

        var result = new List<AppearanceSpritePart>(spriteInfo.SpriteId.Count);
        for (var i = 0; i < spriteInfo.SpriteId.Count; i++)
        {
            var rest = i;
            var tileX = rest % tileWidth;
            rest /= tileWidth;
            var tileY = rest % tileHeight;
            rest /= tileHeight;
            var layer = rest % layers;
            rest /= layers;
            var patternX = rest % patternXCount;
            rest /= patternXCount;
            var patternY = rest % patternYCount;
            rest /= patternYCount;
            var patternZ = rest % patternZCount;
            rest /= patternZCount;
            var frame = rest % frames;

            result.Add(new AppearanceSpritePart(
                i,
                spriteInfo.SpriteId[i],
                layer,
                tileX,
                tileY,
                patternX,
                patternY,
                patternZ,
                frame));
        }

        return result;
    }

    public static IReadOnlyList<AppearanceSpritePart> EnumerateVisibleSprites(
        Appearance appearance,
        AppearanceRenderOptions options)
    {
        var group = GetFrameGroup(appearance, options.GroupIndex);
        var spriteInfo = group?.SpriteInfo;
        if (group is null || spriteInfo is null)
        {
            return [];
        }

        var layout = AppearanceLayoutInfo.From(spriteInfo);
        var tileWidth = layout.TileWidth;
        var tileHeight = layout.TileHeight;
        var patternXCount = layout.PatternX;
        var patternYCount = layout.PatternY;
        var patternZCount = layout.PatternZ;
        var layers = layout.Layers;
        var frame = Math.Clamp(options.Frame, 0, GetFrameCount(spriteInfo) - 1);
        var patternZ = Math.Clamp(options.PatternZ, 0, patternZCount - 1);
        var result = new List<AppearanceSpritePart>();

        if (appearance.AppearanceType == APPEARANCE_TYPE.AppearanceOutfit)
        {
            var direction = Math.Clamp(options.Direction, 0, patternXCount - 1);
            var firstAddon = options.FullAddons ? 0 : Math.Clamp(options.Addon, 0, patternYCount - 1);
            var lastAddon = options.FullAddons ? patternYCount - 1 : firstAddon;
            var lastLayer = options.BlendLayers ? layers - 1 : 0;

            for (var addon = firstAddon; addon <= lastAddon; addon++)
            {
                for (var layer = 0; layer <= lastLayer; layer++)
                {
                    for (var tileY = 0; tileY < tileHeight; tileY++)
                    {
                        for (var tileX = 0; tileX < tileWidth; tileX++)
                        {
                            AddPart(group, result, layer, tileX, tileY, direction, addon, patternZ, frame);
                        }
                    }
                }
            }

            return result;
        }

        var visibleLayers = options.BlendLayers ? layers : 1;

        if (tileWidth == 1 && tileHeight == 1)
        {
            for (var patternY = 0; patternY < patternYCount; patternY++)
            {
                for (var patternX = 0; patternX < patternXCount; patternX++)
                {
                    for (var layer = 0; layer < visibleLayers; layer++)
                    {
                        AddPart(group, result, layer, 0, 0, patternX, patternY, patternZ, frame);
                    }
                }
            }

            return result;
        }

        var selectedPatternX = Math.Clamp(options.Direction, 0, patternXCount - 1);
        var firstPatternY = options.FullAddons ? 0 : Math.Clamp(options.Addon, 0, patternYCount - 1);
        var lastPatternY = options.FullAddons ? patternYCount - 1 : firstPatternY;
        for (var patternY = firstPatternY; patternY <= lastPatternY; patternY++)
        {
            for (var layer = 0; layer < visibleLayers; layer++)
            {
                for (var tileY = 0; tileY < tileHeight; tileY++)
                {
                    for (var tileX = 0; tileX < tileWidth; tileX++)
                    {
                        AddPart(group, result, layer, tileX, tileY, selectedPatternX, patternY, patternZ, frame);
                    }
                }
            }
        }

        return result;
    }

    public static FrameGroup? GetFrameGroup(Appearance appearance, int groupIndex)
    {
        if (appearance.FrameGroup.Count == 0)
        {
            return null;
        }

        return appearance.FrameGroup[Math.Clamp(groupIndex, 0, appearance.FrameGroup.Count - 1)];
    }

    public static int GetPatternWidth(SpriteInfo spriteInfo) => AppearanceLayoutInfo.From(spriteInfo).PatternX;

    public static int GetPatternHeight(SpriteInfo spriteInfo) => AppearanceLayoutInfo.From(spriteInfo).PatternY;

    public static int GetPatternDepth(SpriteInfo spriteInfo) => AppearanceLayoutInfo.From(spriteInfo).PatternZ;

    public static int GetLayers(SpriteInfo spriteInfo) => AppearanceLayoutInfo.From(spriteInfo).Layers;

    public static int GetFrameCount(SpriteInfo spriteInfo)
    {
        if (spriteInfo.Animation?.SpritePhase.Count > 0)
        {
            return spriteInfo.Animation.SpritePhase.Count;
        }

        return AppearanceLayoutInfo.From(spriteInfo).Frames;
    }

    private static void AddPart(
        FrameGroup group,
        ICollection<AppearanceSpritePart> result,
        int layer,
        int tileX,
        int tileY,
        int patternX,
        int patternY,
        int patternZ,
        int frame)
    {
        var spriteInfo = group.SpriteInfo;
        var index = GetSpriteIndex(spriteInfo, tileX, tileY, layer, patternX, patternY, patternZ, frame);
        if (index >= 0 && index < spriteInfo.SpriteId.Count)
        {
            result.Add(new AppearanceSpritePart(
                index,
                spriteInfo.SpriteId[index],
                layer,
                tileX,
                tileY,
                patternX,
                patternY,
                patternZ,
                frame));
        }
    }

    private static int PositiveModulo(int value, int modulo)
    {
        if (modulo <= 1)
        {
            return 0;
        }

        var result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    private static PreviewRect GetUniformImageRect(
        double surfaceWidth,
        double surfaceHeight,
        int renderedWidth,
        int renderedHeight)
    {
        var safeSurfaceWidth = Math.Max(1, surfaceWidth);
        var safeSurfaceHeight = Math.Max(1, surfaceHeight);
        var safeRenderedWidth = Math.Max(1, renderedWidth);
        var safeRenderedHeight = Math.Max(1, renderedHeight);

        var surfaceAspect = safeSurfaceWidth / safeSurfaceHeight;
        var renderedAspect = safeRenderedWidth / (double)safeRenderedHeight;
        if (surfaceAspect > renderedAspect)
        {
            var height = safeSurfaceHeight;
            var width = height * renderedAspect;
            return new PreviewRect((safeSurfaceWidth - width) / 2d, 0, width, height);
        }

        var rectWidth = safeSurfaceWidth;
        var rectHeight = rectWidth / renderedAspect;
        return new PreviewRect(0, (safeSurfaceHeight - rectHeight) / 2d, rectWidth, rectHeight);
    }

    private readonly record struct PreviewRect(double X, double Y, double Width, double Height);
}
