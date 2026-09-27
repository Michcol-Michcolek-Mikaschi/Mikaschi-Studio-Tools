using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modules.SpriteResizer.Services;

namespace Modules.SpriteResizer.ViewModels;

public partial class SpriteResizerViewModel : ObservableObject
{
    private readonly SpriteResizerService _service = new();

    [ObservableProperty] private string _inputFolder = string.Empty;
    [ObservableProperty] private string _outputFolder = string.Empty;
    [ObservableProperty] private int _targetWidth = 64;
    [ObservableProperty] private int _targetHeight = 64;
    [ObservableProperty] private string _statusLog = string.Empty;
    [ObservableProperty] private bool _isBusy = false;

    [RelayCommand(CanExecute = nameof(CanResize))]
    private async Task ResizeAsync()
    {
        IsBusy = true;
        StatusLog = string.Empty;

        try
        {
            var progress = new Progress<string>(msg => StatusLog += msg + Environment.NewLine);
            var result = await Task.Run(() =>
                _service.ResizeBatch(InputFolder, OutputFolder, TargetWidth, TargetHeight, progress));

            StatusLog += $"\nGotowe! Przetworzono: {result.Succeeded}/{result.Total}, Błędy: {result.Failed}";
        }
        catch (Exception ex)
        {
            StatusLog += $"\nBłąd: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanResize() =>
        !IsBusy && !string.IsNullOrWhiteSpace(InputFolder) && !string.IsNullOrWhiteSpace(OutputFolder)
        && TargetWidth > 0 && TargetHeight > 0;
}
