using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Modules.ObjectBuilder.ViewModels;
using Narzedzia.Contracts.Localization;

namespace Modules.ObjectBuilder.Views;

public partial class LegacyExportWindow : Window
{
    private readonly LocalizationScope _localizationScope;

    public LegacyExportWindow()
        : this("object", 1)
    {
    }

    public LegacyExportWindow(string defaultName, int selectedCount)
    {
        InitializeComponent();
        _localizationScope = LocalizationScope.Attach(this);
        Closed += (_, _) => _localizationScope.Dispose();
        FormatComboBox.SelectionChanged += FormatComboBox_SelectionChanged;
        UpdateFormatOptions();
        BaseNameTextBox.Text = defaultName;
        OutputFolderTextBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        SelectionSummaryText.Text = selectedCount == 1
            ? "Zostanie wyeksportowany 1 zaznaczony obiekt."
            : $"Zostanie wyeksportowanych obiektów: {selectedCount}.";
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz folder eksportu"),
            AllowMultiple = false
        });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
        {
            OutputFolderTextBox.Text = path;
        }
    }

    private void FormatComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateFormatOptions();
    }

    private void UpdateFormatOptions()
    {
        var supportsTransparency = FormatComboBox.SelectedIndex is 0 or 1;
        TransparentBackgroundCheckBox.IsEnabled = supportsTransparency;
        if (!supportsTransparency) TransparentBackgroundCheckBox.IsChecked = false;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        var name = BaseNameTextBox.Text?.Trim();
        var folder = OutputFolderTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(folder)) return;

        var format = FormatComboBox.SelectedIndex switch
        {
            0 => LegacyExportFormat.Png,
            1 => LegacyExportFormat.Bmp,
            2 => LegacyExportFormat.Jpg,
            3 => LegacyExportFormat.Obd,
            4 => LegacyExportFormat.Aec,
            _ => LegacyExportFormat.Png
        };
        Close(new LegacyExportOptions(
            name,
            folder,
            format,
            TransparentBackgroundCheckBox.IsChecked == true));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);
}
