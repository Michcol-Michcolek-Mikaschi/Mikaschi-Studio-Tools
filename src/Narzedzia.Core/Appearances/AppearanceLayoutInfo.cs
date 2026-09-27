using Narzedzia.Core.Tibia12;

namespace Narzedzia.Core.Appearances;

public sealed record AppearanceLayoutInfo(
    int TileWidth,
    int TileHeight,
    int Layers,
    int PatternX,
    int PatternY,
    int PatternZ,
    int Frames)
{
    public int TotalSprites => TileWidth * TileHeight * Layers * PatternX * PatternY * PatternZ * Frames;

    public static AppearanceLayoutInfo From(SpriteInfo info)
    {
        static int One(uint value) => value == 0 ? 1 : (int)value;

        var usesExplicitPatternFields =
            info.PatternX > 0 || info.PatternY > 0 || info.PatternZ > 0 ||
            info.PatternLayers > 0;

        var frames = info.Animation?.SpritePhase.Count > 0
            ? info.Animation.SpritePhase.Count
            : One(info.PatternFrames);

        if (usesExplicitPatternFields)
        {
            return new AppearanceLayoutInfo(
                TileWidth: One(info.PatternWidth),
                TileHeight: One(info.PatternHeight),
                Layers: One(info.PatternLayers == 0 ? info.Layers : info.PatternLayers),
                PatternX: One(info.PatternX),
                PatternY: One(info.PatternY),
                PatternZ: One(info.PatternZ),
                Frames: frames);
        }

        return new AppearanceLayoutInfo(
            TileWidth: 1,
            TileHeight: 1,
            Layers: One(info.Layers),
            PatternX: One(info.PatternWidth),
            PatternY: One(info.PatternHeight),
            PatternZ: One(info.PatternDepth),
            Frames: frames);
    }
}
