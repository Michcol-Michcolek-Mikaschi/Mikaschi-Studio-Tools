using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modules.SpriteGenerator.Services;

namespace Modules.SpriteGenerator.ViewModels;

public partial class SpriteGeneratorViewModel : ObservableObject
{
    private readonly SpriteSheetAssembler _assembler = new();
    private ISpriteAiClient? _client;
    private byte[]? _lastSheetPng;
    private List<byte[]>? _lastFrames;

    public IReadOnlyList<SpriteCategoryPreset> Categories { get; } = SpriteCategoryPreset.All;
    public IReadOnlyList<string> Samplers { get; } = new[] { "euler", "euler_ancestral", "dpmpp_2m", "dpmpp_2m_sde", "ddim" };
    public IReadOnlyList<string> Schedulers { get; } = new[] { "normal", "karras", "exponential", "sgm_uniform" };

    [ObservableProperty] private SpriteCategoryPreset _selectedCategory = SpriteCategoryPreset.Outfit;
    [ObservableProperty] private string _prompt = string.Empty;
    [ObservableProperty] private string _negativePrompt =
        "blurry, low quality, jpeg artifacts, watermark, text, signature, deformed";
    [ObservableProperty] private string _modelName = "sd_xl_base_1.0.safetensors";
    [ObservableProperty] private int _width = 64;
    [ObservableProperty] private int _height = 64;
    [ObservableProperty] private int _frameCount = 4;
    [ObservableProperty] private int _steps = 20;
    [ObservableProperty] private double _cfgScale = 7.0;
    [ObservableProperty] private string _sampler = "euler";
    [ObservableProperty] private string _scheduler = "normal";
    [ObservableProperty] private long _seed = 0;
    [ObservableProperty] private int _targetTileSize = 32;
    [ObservableProperty] private string _serverUrl = "http://127.0.0.1:8188";
    [ObservableProperty] private string _serverStatus = "Niesprawdzony — kliknij \"Test ComfyUI\".";
    [ObservableProperty] private string _generateStatus = "Gotowy.";
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private Bitmap? _sheetPreview;
    public ObservableCollection<Bitmap> FramePreviews { get; } = new();

    public bool HasSheet => _lastSheetPng is not null;

    partial void OnSelectedCategoryChanged(SpriteCategoryPreset value)
    {
        Width = value.Width;
        Height = value.Height;
        FrameCount = value.FramesPerAnimation * Math.Max(1, value.Directions);
        TargetTileSize = value == SpriteCategoryPreset.Outfit ? 32 : value.Width;
        if (string.IsNullOrWhiteSpace(Prompt) && !string.IsNullOrEmpty(value.PromptHint))
        {
            Prompt = value.PromptHint;
        }
    }

    [RelayCommand]
    private async Task TestServer()
    {
        ServerStatus = "Sprawdzanie...";
        _client?.Dispose();
        _client = new ComfyUiClient(ServerUrl);
        var ok = await _client.IsAvailableAsync(CancellationToken.None);
        ServerStatus = ok
            ? $"✓ ComfyUI dostępny pod {ServerUrl}"
            : $"✗ Brak odpowiedzi z {ServerUrl}. Uruchom ComfyUI (domyślny port 8188).";
    }

    [RelayCommand]
    private async Task Generate()
    {
        if (IsGenerating) return;
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            GenerateStatus = "Podaj prompt.";
            return;
        }
        _client ??= new ComfyUiClient(ServerUrl);
        IsGenerating = true;
        FramePreviews.Clear();
        SheetPreview = null;
        _lastSheetPng = null;
        _lastFrames = null;
        OnPropertyChanged(nameof(HasSheet));

        try
        {
            var req = new SpriteGenerateRequest
            {
                Prompt = Prompt,
                NegativePrompt = NegativePrompt,
                Width = Width,
                Height = Height,
                FrameCount = FrameCount,
                Seed = Seed,
                Steps = Steps,
                CfgScale = CfgScale,
                SamplerName = Sampler,
                Scheduler = Scheduler,
                ModelName = ModelName,
            };

            var progress = new Progress<string>(s => GenerateStatus = s);
            var result = await _client.GenerateAsync(req, progress, CancellationToken.None);

            if (!result.IsSuccess)
            {
                GenerateStatus = $"Błąd: {result.ErrorMessage}";
                return;
            }

            _lastFrames = result.Frames.ToList();
            foreach (var frameBytes in _lastFrames)
            {
                using var ms = new MemoryStream(frameBytes);
                FramePreviews.Add(new Bitmap(ms));
            }

            GenerateStatus = $"Składanie sprite sheet ({_lastFrames.Count} klatek → tile {TargetTileSize}px)...";
            _lastSheetPng = _assembler.Assemble(_lastFrames, TargetTileSize);
            using (var sheetMs = new MemoryStream(_lastSheetPng))
            {
                SheetPreview = new Bitmap(sheetMs);
            }
            OnPropertyChanged(nameof(HasSheet));
            GenerateStatus = $"✓ Wygenerowano {_lastFrames.Count} klatek, sheet {SheetPreview.PixelSize.Width}×{SheetPreview.PixelSize.Height}px.";
        }
        catch (Exception ex)
        {
            GenerateStatus = $"Błąd: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    public byte[]? GetLastSheetPng() => _lastSheetPng;
}
