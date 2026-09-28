using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modules.SpriteResizer.Services;
using Narzedzia.Contracts.Localization;

namespace Modules.SpriteResizer.ViewModels;

public partial class SpriteResizerViewModel : ObservableObject
{
    private readonly SpriteResizerService _service;
    private CancellationTokenSource? _operationCancellation;

    public SpriteResizerViewModel()
        : this(new SpriteResizerService())
    {
    }

    internal SpriteResizerViewModel(SpriteResizerService service)
    {
        _service = service;
        StatusText = T("Wybierz pliki PNG do przetworzenia.");
    }

    public ObservableCollection<SpriteInputFileViewModel> InputFiles { get; } = [];
    public ObservableCollection<string> StatusEntries { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResizeCommand))]
    private int _targetWidth = 64;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResizeCommand))]
    private int _targetHeight = 64;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private int _completedFiles;

    [ObservableProperty]
    private int _totalFiles;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResizeCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    public string SelectionSummary => string.Format(
        T("Wybrano plików: {0}"),
        InputFiles.Count);

    public void SetInputFiles(IEnumerable<string> paths)
    {
        if (IsBusy)
        {
            return;
        }

        InputFiles.Clear();
        foreach (var path in NormalizeInputFiles(paths))
        {
            InputFiles.Add(new SpriteInputFileViewModel(Path.GetFileName(path), path));
        }

        StatusEntries.Clear();
        StatusText = InputFiles.Count == 0
            ? T("Wybierz pliki PNG do przetworzenia.")
            : SelectionSummary;
        OnPropertyChanged(nameof(SelectionSummary));
        ResizeCommand.NotifyCanExecuteChanged();
    }

    public SpriteInputFilesUpdate AddInputFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (IsBusy)
        {
            return new SpriteInputFilesUpdate(0, 0, 0);
        }

        var existingPaths = InputFiles
            .Select(file => file.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var acceptedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var rejected = 0;
        var duplicates = 0;

        foreach (var candidate in paths)
        {
            if (!TryNormalizeInputFile(candidate, out var normalizedPath))
            {
                rejected++;
                continue;
            }

            if (!acceptedPaths.Add(normalizedPath) || !existingPaths.Add(normalizedPath))
            {
                duplicates++;
                continue;
            }

            InputFiles.Add(new SpriteInputFileViewModel(Path.GetFileName(normalizedPath), normalizedPath));
            added++;
        }

        var result = new SpriteInputFilesUpdate(added, duplicates, rejected);
        StatusEntries.Clear();
        StatusText = result switch
        {
            { Added: > 0, Rejected: > 0 } => string.Format(
                T("Dodano plików PNG: {0}. Pominięto niepoprawnych: {1}."),
                result.Added,
                result.Rejected),
            { Added: > 0, Duplicates: > 0 } => string.Format(
                T("Dodano plików PNG: {0}. Pominięto duplikatów: {1}."),
                result.Added,
                result.Duplicates),
            { Added: > 0 } => string.Format(T("Dodano plików PNG: {0}."), result.Added),
            { Duplicates: > 0, Rejected: 0 } => T("Wszystkie przeciągnięte pliki PNG są już na liście."),
            _ => T("Nie dodano plików. Upuść lokalne pliki PNG.")
        };

        OnPropertyChanged(nameof(SelectionSummary));
        ResizeCommand.NotifyCanExecuteChanged();
        return result;
    }

    public static bool IsSupportedInputFile(string? path) =>
        TryNormalizeInputFile(path, out _);

    private static IEnumerable<string> NormalizeInputFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var acceptedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in paths)
        {
            if (TryNormalizeInputFile(candidate, out var path) && acceptedPaths.Add(path))
            {
                yield return path;
            }
        }
    }

    private static bool TryNormalizeInputFile(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalizedPath = Path.GetFullPath(path);
            return File.Exists(normalizedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanResize))]
    private async Task ResizeAsync()
    {
        if (!CanResize())
        {
            return;
        }

        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        var cancellationToken = _operationCancellation.Token;

        IsBusy = true;
        CompletedFiles = 0;
        TotalFiles = InputFiles.Count;
        ProgressValue = 0;
        StatusEntries.Clear();
        StatusText = T("Przetwarzanie sprite'ów…");

        try
        {
            var selectedPaths = InputFiles.Select(file => file.FullPath).ToArray();
            var progress = new Progress<SpriteResizeProgress>(update =>
            {
                CompletedFiles = update.Completed;
                ProgressValue = update.Total == 0
                    ? 0
                    : update.Completed * 100d / update.Total;

                var entry = update.ErrorMessage is null
                    ? string.Format(T("[{0}/{1}] Przetworzono: {2}"), update.Completed, update.Total, update.FileName)
                    : string.Format(T("[{0}/{1}] Błąd: {2} — {3}"), update.Completed, update.Total, update.FileName, update.ErrorMessage);
                StatusEntries.Add(entry);
                StatusText = entry;
            });

            var result = await Task.Run(
                () => _service.ResizeFiles(
                    selectedPaths,
                    TargetWidth,
                    TargetHeight,
                    progress,
                    cancellationToken),
                cancellationToken);

            StatusText = string.Format(
                T("Gotowe: {0} OK, {1} błędów. Pliki zapisano obok oryginałów."),
                result.Succeeded,
                result.Failed);
            StatusEntries.Add(StatusText);
        }
        catch (OperationCanceledException)
        {
            StatusText = T("Anulowano przetwarzanie.");
            StatusEntries.Add(StatusText);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(T("Błąd: {0}"), ex.Message);
            StatusEntries.Add(StatusText);
        }
        finally
        {
            IsBusy = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }
    }

    private bool CanResize() =>
        !IsBusy &&
        InputFiles.Count > 0 &&
        TargetWidth > 0 &&
        TargetHeight > 0;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operationCancellation?.Cancel();

    private bool CanCancel() => IsBusy;

    private static string T(string source) => LocalizationManager.Translate(source);
}

public sealed record SpriteInputFileViewModel(string Name, string FullPath);

public readonly record struct SpriteInputFilesUpdate(int Added, int Duplicates, int Rejected);
