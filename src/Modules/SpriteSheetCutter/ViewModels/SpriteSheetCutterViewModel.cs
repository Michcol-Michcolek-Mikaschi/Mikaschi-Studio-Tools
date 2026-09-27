using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modules.SpriteSheetCutter.Services;

namespace Modules.SpriteSheetCutter.ViewModels;

public partial class SpriteSheetCutterViewModel : ObservableObject
{
    private readonly SpriteSheetCutterService _service = new();

    [ObservableProperty] private string _inputFolder = string.Empty;
    [ObservableProperty] private string _outputFolder = string.Empty;
    [ObservableProperty] private int _spriteWidth = 32;
    [ObservableProperty] private int _spriteHeight = 32;
    [ObservableProperty] private string _statusLog = string.Empty;
    [ObservableProperty] private bool _isBusy = false;

    [RelayCommand(CanExecute = nameof(CanCut))]
    private async Task CutAsync()
    {
        IsBusy = true;
        StatusLog = string.Empty;

        try
        {
            var progress = new Progress<string>(msg => StatusLog += msg + Environment.NewLine);
            var result = await Task.Run(() =>
                _service.CutBatch(InputFolder, OutputFolder, SpriteWidth, SpriteHeight, progress: progress));

            StatusLog += $"\nGotowe! Pliki: {result.SucceededFiles}/{result.TotalFiles}, Klatki: {result.TotalFrames}, Błędy: {result.FailedFiles}";
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

    private bool CanCut() =>
        !IsBusy && !string.IsNullOrWhiteSpace(InputFolder) && !string.IsNullOrWhiteSpace(OutputFolder)
        && SpriteWidth > 0 && SpriteHeight > 0;
}
