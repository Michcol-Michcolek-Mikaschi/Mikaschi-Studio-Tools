using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.ObjectBuilder.Services;

public sealed record LegacyRenderedThing(byte[] Pixels, int Width, int Height);

public static class LegacyThingRenderer
{
    public static LegacyRenderedThing? Render(
        DatThingType thing,
        LegacySpriteStore sprites,
        int groupIndex,
        int direction,
        int addon,
        int patternZ,
        int frame,
        int subtype = -1)
    {
        if (thing.FrameGroups.Count == 0)
        {
            return null;
        }

        var group = thing.FrameGroups[Math.Clamp(groupIndex, 0, thing.FrameGroups.Count - 1)];
        var widthTiles = Math.Max(1, (int)group.Width);
        var heightTiles = Math.Max(1, (int)group.Height);
        var layers = Math.Max(1, (int)group.Layers);
        var patternXCount = Math.Max(1, (int)group.PatternX);
        var patternYCount = Math.Max(1, (int)group.PatternY);
        var patternZCount = Math.Max(1, (int)group.PatternZ);
        var frames = Math.Max(1, (int)group.Frames);

        var patternPosition = LegacyDirectionMapping.ToPatternPosition(
            thing.Category,
            direction,
            addon,
            patternXCount,
            patternYCount);
        var safePatternZ = Math.Clamp(patternZ, 0, patternZCount - 1);
        var safeFrame = Math.Clamp(frame, 0, frames - 1);

        var canvasWidth = widthTiles * 32;
        var canvasHeight = heightTiles * 32;
        var canvas = new byte[canvasWidth * canvasHeight * 4];

        for (var layer = 0; layer < layers; layer++)
        {
            for (var tileY = 0; tileY < heightTiles; tileY++)
            {
                for (var tileX = 0; tileX < widthTiles; tileX++)
                {
                    var index = subtype >= 0 && widthTiles <= 1 && heightTiles <= 1
                        ? group.SpriteIds.Length <= 1 ? 0 : subtype % group.SpriteIds.Length
                        : DatParser.CalculateSpriteIndex(
                            group,
                            tileX,
                            tileY,
                            layer,
                            patternPosition.X,
                            patternPosition.Y,
                            safePatternZ,
                            safeFrame);

                    if (index < 0 || index >= group.SpriteIds.Length)
                    {
                        continue;
                    }

                    var spriteId = group.SpriteIds[index];
                    var tilePixels = sprites.GetSpritePixels(spriteId);
                    if (tilePixels is null)
                    {
                        continue;
                    }

                    var drawX = (widthTiles - tileX - 1) * 32;
                    var drawY = (heightTiles - tileY - 1) * 32;
                    AlphaBlendTile(canvas, canvasWidth, canvasHeight, tilePixels, drawX, drawY);
                }
            }
        }

        return new LegacyRenderedThing(canvas, canvasWidth, canvasHeight);
    }

    private static void AlphaBlendTile(byte[] canvas, int canvasWidth, int canvasHeight, byte[] tile, int x, int y)
    {
        for (var row = 0; row < 32; row++)
        {
            var dstY = y + row;
            if (dstY < 0 || dstY >= canvasHeight)
            {
                continue;
            }

            for (var col = 0; col < 32; col++)
            {
                var dstX = x + col;
                if (dstX < 0 || dstX >= canvasWidth)
                {
                    continue;
                }

                var srcIndex = (row * 32 + col) * 4;
                var srcA = tile[srcIndex + 3];
                if (srcA == 0)
                {
                    continue;
                }

                var dstIndex = (dstY * canvasWidth + dstX) * 4;
                canvas[dstIndex + 0] = tile[srcIndex + 0];
                canvas[dstIndex + 1] = tile[srcIndex + 1];
                canvas[dstIndex + 2] = tile[srcIndex + 2];
                canvas[dstIndex + 3] = srcA;
            }
        }
    }
}
