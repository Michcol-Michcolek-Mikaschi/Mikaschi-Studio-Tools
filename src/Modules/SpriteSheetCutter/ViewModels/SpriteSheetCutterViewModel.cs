using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modules.SpriteSheetCutter.Services;
using Narzedzia.Contracts.Localization;

namespace Modules.SpriteSheetCutter.ViewModels;

public partial class SpriteSheetCutterViewModel : ObservableObject, IDisposable
{
    private readonly SpriteSheetCutterService _service;
    private CancellationTokenSource? _operationCancellation;

    public SpriteSheetCutterViewModel()
        : this(new SpriteSheetCutterService())
    {
    }

    internal SpriteSheetCutterViewModel(SpriteSheetCutterService service)
    {
        _service = service;
        StatusText = T("Wybierz arkusz sprite'ów do przecięcia.");
    }

    public IReadOnlyList<SpriteSizePreset> Presets => SpriteSizePresetCatalog.All;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CutCommand))]
    private string _inputFilePath = string.Empty;

    [ObservableProperty]
    private string _inputFileName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CutCommand))]
    private SpriteSizePreset _selectedPreset = SpriteSizePresetCatalog.Default;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private Bitmap? _previewImage;

    [ObservableProperty]
    private string _previewInfo = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private int _completedFrames;

    [ObservableProperty]
    private int _totalFrames;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CutCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    public bool HasInputFile => !string.IsNullOrWhiteSpace(InputFilePath);
    public bool HasPreview => PreviewImage is not null;

    public void SetInputFile(string path)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        InputFilePath = Path.GetFullPath(path);
        InputFileName = Path.GetFileName(InputFilePath);
        OutputFolder = SpriteSheetCutterService.BuildOutputFolder(InputFilePath);
        ClearPreview();
        ProgressValue = 0;
        CompletedFrames = 0;
        TotalFrames = 0;
        StatusText = string.Format(T("Wybrano: {0}"), InputFileName);
        OnPropertyChanged(nameof(HasInputFile));
    }

    public void ReportInvalidDrop()
    {
        if (!IsBusy)
        {
            StatusText = T("Upuść dokładnie jeden lokalny plik PNG.");
        }
    }

    partial void OnSelectedPresetChanged(SpriteSizePreset value)
    {
        ClearPreview();
        StatusText = HasInputFile
            ? string.Format(T("Wybrano rozmiar sprite'a: {0}"), value.Label)
            : T("Wybierz arkusz sprite'ów do przecięcia.");
    }

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task PreviewAsync()
    {
        await RunOperationAsync(async cancellationToken =>
        {
            StatusText = T("Tworzenie podglądu…");
            var preview = await Task.Run(
                () => _service.CreatePreview(
                    InputFilePath,
                    SelectedPreset.Width,
                    SelectedPreset.Height,
                    cancellationToken: cancellationToken),
                cancellationToken);

            using var stream = new MemoryStream(preview.PngData, writable: false);
            var bitmap = new Bitmap(stream);
            PreviewImage?.Dispose();
            PreviewImage = bitmap;
            OnPropertyChanged(nameof(HasPreview));

            PreviewInfo = string.Format(
                T("Arkusz: {0}×{1}px | Sprite: {2}×{3}px | Siatka: {4}×{5} | Klatki: {6}"),
                preview.SourceWidth,
                preview.SourceHeight,
                preview.SpriteWidth,
                preview.SpriteHeight,
                preview.Columns,
                preview.Rows,
                preview.FrameCount);
            StatusText = T("Podgląd jest gotowy.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanProcess))]
    private async Task CutAsync()
    {
        await RunOperationAsync(async cancellationToken =>
        {
            ProgressValue = 0;
            CompletedFrames = 0;
            TotalFrames = 0;
            StatusText = T("Cięcie arkusza sprite'ów…");

            var progress = new Progress<SpriteCutProgress>(update =>
            {
                CompletedFrames = update.Completed;
                TotalFrames = update.Total;
                ProgressValue = update.Total == 0
                    ? 0
                    : update.Completed * 100d / update.Total;
                StatusText = string.Format(
                    T("Wycinanie klatki {0}/{1}…"),
                    update.Completed,
                    update.Total);
            });

            var result = await Task.Run(
                () => _service.CutSingle(
                    InputFilePath,
                    SelectedPreset.Width,
                    SelectedPreset.Height,
                    progress,
                    cancellationToken),
                cancellationToken);

            CompletedFrames = result.TotalFrames;
            TotalFrames = result.TotalFrames;
            ProgressValue = 100;
            OutputFolder = result.OutputFolder;
            StatusText = string.Format(
                T("Gotowe: wycięto {0} klatek. Zapisano w: {1}"),
                result.TotalFrames,
                result.OutputFolder);
        });
    }

    private bool CanProcess() => IsBusy is false && HasInputFile;

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        if (!CanProcess())
        {
            return;
        }

        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        IsBusy = true;

        try
        {
            await operation(_operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = T("Anulowano operację.");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(T("Błąd: {0}"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operationCancellation?.Cancel();

    private bool CanCancel() => IsBusy;

    private void ClearPreview()
    {
        PreviewImage?.Dispose();
        PreviewImage = null;
        PreviewInfo = string.Empty;
        OnPropertyChanged(nameof(HasPreview));
    }

    public void Dispose()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        ClearPreview();
    }

    private static string T(string source) => LocalizationManager.Translate(source);
}
