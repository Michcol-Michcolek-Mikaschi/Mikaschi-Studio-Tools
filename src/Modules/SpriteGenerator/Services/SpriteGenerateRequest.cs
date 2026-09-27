namespace Modules.SpriteGenerator.Services;

public sealed class SpriteGenerateRequest
{
    public required string Prompt { get; init; }
    public string NegativePrompt { get; init; } =
        "blurry, low quality, jpeg artifacts, watermark, text, signature, deformed";
    public required int Width { get; init; }
    public required int Height { get; init; }
    public int FrameCount { get; init; } = 1;
    /// <summary>Seed bazowy; każda kolejna klatka używa seed + offset dla spójności.</summary>
    public long Seed { get; init; } = 0;
    public int Steps { get; init; } = 20;
    public double CfgScale { get; init; } = 7.0;
    public string SamplerName { get; init; } = "euler";
    public string Scheduler { get; init; } = "normal";
    /// <summary>Nazwa modelu (checkpoint) — musi istnieć w ComfyUI/models/checkpoints/.</summary>
    public string ModelName { get; init; } = "sd_xl_base_1.0.safetensors";
}

public sealed class SpriteGenerateResult
{
    public IReadOnlyList<byte[]> Frames { get; init; } = Array.Empty<byte[]>();
    public string ErrorMessage { get; init; } = string.Empty;
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage) && Frames.Count > 0;
}

public interface ISpriteAiClient : IDisposable
{
    Task<bool> IsAvailableAsync(CancellationToken ct);
    Task<SpriteGenerateResult> GenerateAsync(SpriteGenerateRequest request, IProgress<string>? progress, CancellationToken ct);
}
