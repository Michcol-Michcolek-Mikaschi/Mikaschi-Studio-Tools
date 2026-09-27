using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Modules.SpriteGenerator.Services;

/// <summary>
/// Klient REST API dla ComfyUI (domyślnie http://127.0.0.1:8188).
/// Wysyła workflow JSON typu txt2img, pollue /history, ściąga wynikowe PNG przez /view.
/// </summary>
public sealed class ComfyUiClient : ISpriteAiClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _clientId = Guid.NewGuid().ToString("N");
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public string BaseUrl { get; }

    public ComfyUiClient(string baseUrl = "http://127.0.0.1:8188")
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(BaseUrl + "/"), Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync("system_stats", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<SpriteGenerateResult> GenerateAsync(
        SpriteGenerateRequest request,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        try
        {
            var frames = new List<byte[]>(request.FrameCount);
            for (var i = 0; i < request.FrameCount; i++)
            {
                progress?.Report($"Generowanie klatki {i + 1}/{request.FrameCount}...");
                var workflow = BuildTxt2ImgWorkflow(request, frameIndex: i);
                var promptId = await SubmitPromptAsync(workflow, ct).ConfigureAwait(false);
                var images = await WaitForResultAsync(promptId, progress, ct).ConfigureAwait(false);
                if (images.Count == 0)
                {
                    return new SpriteGenerateResult { ErrorMessage = $"Klatka {i + 1}: brak wyniku z ComfyUI." };
                }
                frames.AddRange(images);
            }
            return new SpriteGenerateResult { Frames = frames };
        }
        catch (Exception ex)
        {
            return new SpriteGenerateResult { ErrorMessage = ex.Message };
        }
    }

    /// <summary>Workflow txt2img — minimalny graph (Checkpoint → CLIPEncode×2 → EmptyLatent → KSampler → VAEDecode → SaveImage).</summary>
    private static Dictionary<string, object> BuildTxt2ImgWorkflow(SpriteGenerateRequest req, int frameIndex)
    {
        // Seed offsetowany na klatkę dla wariantów; warto użyć podobnego seeda
        // dla spójności (kolejne klatki niedaleko siebie w przestrzeni seedów).
        var seed = req.Seed == 0
            ? Random.Shared.NextInt64(1, long.MaxValue)
            : req.Seed + frameIndex;

        return new Dictionary<string, object>
        {
            ["4"] = new
            {
                class_type = "CheckpointLoaderSimple",
                inputs = new { ckpt_name = req.ModelName }
            },
            ["5"] = new
            {
                class_type = "EmptyLatentImage",
                inputs = new { width = req.Width, height = req.Height, batch_size = 1 }
            },
            ["6"] = new
            {
                class_type = "CLIPTextEncode",
                inputs = new { text = req.Prompt, clip = new object[] { "4", 1 } }
            },
            ["7"] = new
            {
                class_type = "CLIPTextEncode",
                inputs = new { text = req.NegativePrompt, clip = new object[] { "4", 1 } }
            },
            ["3"] = new
            {
                class_type = "KSampler",
                inputs = new
                {
                    seed,
                    steps = req.Steps,
                    cfg = req.CfgScale,
                    sampler_name = req.SamplerName,
                    scheduler = req.Scheduler,
                    denoise = 1.0,
                    model = new object[] { "4", 0 },
                    positive = new object[] { "6", 0 },
                    negative = new object[] { "7", 0 },
                    latent_image = new object[] { "5", 0 }
                }
            },
            ["8"] = new
            {
                class_type = "VAEDecode",
                inputs = new { samples = new object[] { "3", 0 }, vae = new object[] { "4", 2 } }
            },
            ["9"] = new
            {
                class_type = "SaveImage",
                inputs = new { filename_prefix = "narzedzia_sprite", images = new object[] { "8", 0 } }
            },
        };
    }

    private async Task<string> SubmitPromptAsync(Dictionary<string, object> workflow, CancellationToken ct)
    {
        var body = new { prompt = workflow, client_id = _clientId };
        using var resp = await _http.PostAsJsonAsync("prompt", body, ct).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"ComfyUI /prompt zwrócił {(int)resp.StatusCode}: {json}");
        var parsed = JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
        if (parsed.TryGetProperty("prompt_id", out var pid) && pid.ValueKind == JsonValueKind.String)
            return pid.GetString()!;
        throw new InvalidOperationException("ComfyUI /prompt: brak prompt_id w odpowiedzi.");
    }

    private async Task<List<byte[]>> WaitForResultAsync(string promptId, IProgress<string>? progress, CancellationToken ct)
    {
        // Polling /history co 500ms, max 5 min
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500, ct).ConfigureAwait(false);

            using var resp = await _http.GetAsync($"history/{promptId}", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) continue;
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<JsonElement>(json, JsonOpts);
            if (!parsed.TryGetProperty(promptId, out var entry)) continue;

            // entry.outputs.{node}.images[] = [{filename, subfolder, type}]
            if (!entry.TryGetProperty("outputs", out var outputs)) continue;
            var images = new List<byte[]>();
            foreach (var nodeProp in outputs.EnumerateObject())
            {
                if (!nodeProp.Value.TryGetProperty("images", out var imgArr)) continue;
                foreach (var img in imgArr.EnumerateArray())
                {
                    var filename = img.GetProperty("filename").GetString() ?? string.Empty;
                    var subfolder = img.TryGetProperty("subfolder", out var sf) ? (sf.GetString() ?? string.Empty) : string.Empty;
                    var type = img.TryGetProperty("type", out var tp) ? (tp.GetString() ?? "output") : "output";
                    if (string.IsNullOrEmpty(filename)) continue;
                    var bytes = await DownloadImageAsync(filename, subfolder, type, ct).ConfigureAwait(false);
                    if (bytes is not null) images.Add(bytes);
                }
            }
            return images;
        }
        throw new TimeoutException($"ComfyUI timeout — prompt {promptId} nie zakończony w 5 min.");
    }

    private async Task<byte[]?> DownloadImageAsync(string filename, string subfolder, string type, CancellationToken ct)
    {
        var url = $"view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={Uri.EscapeDataString(type)}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public void Dispose() => _http.Dispose();
}
