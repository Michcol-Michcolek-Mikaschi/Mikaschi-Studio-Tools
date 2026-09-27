using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.ObjectBuilder.Services;

public sealed record LegacyRenderedSheet(byte[] Pixels, int Width, int Height);

/// <summary>Eksport pełnego arkusza obiektu w takim samym układzie jak Object Builder.</summary>
public sealed class LegacyThingExportService
{
    public void ExportImage(string path, DatThingType thing, LegacySpriteStore sprites, bool transparentBackground)
    {
        var sheet = RenderSheet(thing, sprites, transparentBackground);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var image = Image.LoadPixelData<Bgra32>(sheet.Pixels, sheet.Width, sheet.Height);
        image.Save(path);
    }

    public LegacyRenderedSheet RenderSheet(DatThingType thing, LegacySpriteStore sprites, bool transparentBackground)
    {
        ArgumentNullException.ThrowIfNull(thing);
        ArgumentNullException.ThrowIfNull(sprites);

        var groups = thing.FrameGroups.Count > 0 ? thing.FrameGroups : [CreateDefaultGroup()];
        var totalColumns = groups.Max(group =>
            Math.Max(1, (int)group.PatternZ) * Math.Max(1, (int)group.PatternX) * Math.Max(1, (int)group.Layers));
        var maxWidthTiles = groups.Max(group => Math.Max(1, (int)group.Width));
        var maxHeightTiles = groups.Max(group => Math.Max(1, (int)group.Height));
        var totalRows = groups.Sum(group => Math.Max(1, (int)group.Frames) * Math.Max(1, (int)group.PatternY));

        var textureWidth = checked(maxWidthTiles * SprParser.SpriteWidth);
        var textureHeight = checked(maxHeightTiles * SprParser.SpriteHeight);
        var canvasWidth = checked(totalColumns * textureWidth);
        var canvasHeight = checked(totalRows * textureHeight);
        var canvas = new byte[checked(canvasWidth * canvasHeight * 4)];
        FillBackground(canvas, transparentBackground);

        var groupRowOffset = 0;
        foreach (var group in groups)
        {
            var width = Math.Max(1, (int)group.Width);
            var height = Math.Max(1, (int)group.Height);
            var layers = Math.Max(1, (int)group.Layers);
            var patternX = Math.Max(1, (int)group.PatternX);
            var patternY = Math.Max(1, (int)group.PatternY);
            var patternZ = Math.Max(1, (int)group.PatternZ);
            var frames = Math.Max(1, (int)group.Frames);

            for (var frame = 0; frame < frames; frame++)
            for (var z = 0; z < patternZ; z++)
            for (var y = 0; y < patternY; y++)
            for (var x = 0; x < patternX; x++)
            for (var layer = 0; layer < layers; layer++)
            {
                var textureIndex = ((((frame * patternZ + z) * patternY + y) * patternX + x) * layers + layer);
                var textureX = textureIndex % totalColumns * textureWidth;
                var textureY = (textureIndex / totalColumns + groupRowOffset) * textureHeight;

                for (var tileY = 0; tileY < height; tileY++)
                for (var tileX = 0; tileX < width; tileX++)
                {
                    var spriteIndex = DatParser.CalculateSpriteIndex(group, tileX, tileY, layer, x, y, z, frame);
                    var spriteId = spriteIndex < group.SpriteIds.Length ? group.SpriteIds[spriteIndex] : 0u;
                    var tile = sprites.GetSpritePixels(spriteId);
                    if (tile is null) continue;

                    var drawX = textureX + (width - tileX - 1) * SprParser.SpriteWidth;
                    var drawY = textureY + (height - tileY - 1) * SprParser.SpriteHeight;
                    BlendTile(canvas, canvasWidth, tile, drawX, drawY);
                }
            }

            groupRowOffset += frames * patternY;
        }

        return new LegacyRenderedSheet(canvas, canvasWidth, canvasHeight);
    }

    private static void FillBackground(byte[] pixels, bool transparent)
    {
        if (transparent) return;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 255;
            pixels[offset + 1] = 0;
            pixels[offset + 2] = 255;
            pixels[offset + 3] = 255;
        }
    }

    private static void BlendTile(byte[] canvas, int canvasWidth, byte[] tile, int destinationX, int destinationY)
    {
        for (var y = 0; y < SprParser.SpriteHeight; y++)
        for (var x = 0; x < SprParser.SpriteWidth; x++)
        {
            var sourceOffset = (y * SprParser.SpriteWidth + x) * 4;
            var sourceAlpha = tile[sourceOffset + 3];
            if (sourceAlpha == 0) continue;

            var destinationOffset = ((destinationY + y) * canvasWidth + destinationX + x) * 4;
            if (sourceAlpha == byte.MaxValue || canvas[destinationOffset + 3] == 0)
            {
                Buffer.BlockCopy(tile, sourceOffset, canvas, destinationOffset, 4);
                continue;
            }

            var alpha = sourceAlpha / 255d;
            for (var channel = 0; channel < 3; channel++)
            {
                canvas[destinationOffset + channel] = (byte)Math.Clamp(
                    Math.Round(tile[sourceOffset + channel] * alpha + canvas[destinationOffset + channel] * (1 - alpha)),
                    0,
                    255);
            }
            canvas[destinationOffset + 3] = byte.MaxValue;
        }
    }

    private static DatThingFrameGroup CreateDefaultGroup() => new()
    {
        Width = 1,
        Height = 1,
        ExactSize = 32,
        Layers = 1,
        PatternX = 1,
        PatternY = 1,
        PatternZ = 1,
        Frames = 1,
        SpriteIds = [0]
    };
}
