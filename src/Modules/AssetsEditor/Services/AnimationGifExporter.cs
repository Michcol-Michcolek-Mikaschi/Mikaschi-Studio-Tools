using Narzedzia.Core.Tibia12;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.AssetsEditor.Services;

/// <summary>
/// Faza 7a — eksport animacji outfitu/efektu/pocisku do GIF.
/// Iteruje po klatkach (SpriteAnimation.SpritePhase) renderując każdą przez `IAssetsService.RenderAppearance`,
/// łączy w wielo-klatkowy GIF z delays pobranymi z `SpritePhase.DurationMin/Max` (średnia).
/// </summary>
public sealed class AnimationGifExporter
{
    private readonly IAssetsService _assets;

    public AnimationGifExporter(IAssetsService assets)
    {
        _assets = assets;
    }

    /// <summary>Eksportuje animację bieżącego kierunku/addonu/mountu do pliku GIF.</summary>
    public ExportReport Export(Appearance appearance, AppearanceRenderOptions baseOptions, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(outputPath);

        var frameGroup = AppearanceTextureLayout.GetFrameGroup(appearance, baseOptions.GroupIndex)
            ?? throw new InvalidOperationException("Appearance nie ma FrameGroup do eksportu.");
        var spriteInfo = frameGroup.SpriteInfo
            ?? throw new InvalidOperationException("FrameGroup nie ma SpriteInfo.");

        var frameCount = AppearanceTextureLayout.GetFrameCount(spriteInfo);
        var phases = spriteInfo.Animation?.SpritePhase;

        Image<Bgra32>? gif = null;
        int width = 0, height = 0, written = 0;

        try
        {
            for (var f = 0; f < frameCount; f++)
            {
                var opt = baseOptions with { Frame = f };
                var rendered = _assets.RenderAppearance(appearance, opt);
                if (rendered is null) continue;

                using var frameImg = Image.LoadPixelData<Bgra32>(
                    rendered.Pixels, rendered.Width, rendered.Height);

                var delayHundredths = ResolveFrameDelay(phases, f);

                if (gif is null)
                {
                    width = rendered.Width;
                    height = rendered.Height;
                    gif = new Image<Bgra32>(width, height);

                    // Zastąp domyślną pustą root frame zawartością frame 0
                    gif.Frames.AddFrame(frameImg.Frames.RootFrame);
                    gif.Frames.RemoveFrame(0);

                    var meta = gif.Frames.RootFrame.Metadata.GetGifMetadata();
                    meta.FrameDelay = delayHundredths;
                    written++;
                }
                else
                {
                    if (rendered.Width != width || rendered.Height != height)
                    {
                        continue; // pomijamy klatki z innym rozmiarem (rzadko, ale defensywnie)
                    }
                    var added = gif.Frames.AddFrame(frameImg.Frames.RootFrame);
                    var meta = added.Metadata.GetGifMetadata();
                    meta.FrameDelay = delayHundredths;
                    written++;
                }
            }

            if (gif is null || written == 0)
            {
                throw new InvalidOperationException("Brak renderowanych klatek do eksportu.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            gif.SaveAsGif(outputPath, new GifEncoder
            {
                ColorTableMode = GifColorTableMode.Local,
            });

            return new ExportReport(FramesWritten: written, Width: width, Height: height, Path: outputPath);
        }
        finally
        {
            gif?.Dispose();
        }
    }

    /// <summary>
    /// Delay w setnych sekundy (jednostka GIF). Wartość pobierana ze średniej
    /// `DurationMin/Max` w milisekundach; minimum 2 (20ms) wymagane przez większość przeglądarek.
    /// </summary>
    private static int ResolveFrameDelay(IList<SpritePhase>? phases, int frameIndex)
    {
        if (phases is null || frameIndex >= phases.Count)
        {
            return 10; // 100ms default
        }
        var p = phases[frameIndex];
        var avgMs = p.HasDurationMin && p.HasDurationMax
            ? (long)((p.DurationMin + p.DurationMax) / 2)
            : (long)(p.HasDurationMin ? p.DurationMin : (p.HasDurationMax ? p.DurationMax : 100u));

        var hundredths = (int)Math.Max(2, avgMs / 10);
        return hundredths;
    }

    public sealed record ExportReport(int FramesWritten, int Width, int Height, string Path);
}
