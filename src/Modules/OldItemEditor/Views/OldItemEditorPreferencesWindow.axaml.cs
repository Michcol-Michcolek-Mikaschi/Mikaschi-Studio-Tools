using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Modules.OldItemEditor.Services;
using Narzedzia.Contracts.Localization;

namespace Modules.OldItemEditor.Views;

public partial class OldItemEditorPreferencesWindow : Window
{
    private OldItemEditorClientProfile? _detectedProfile;
    private readonly LocalizationScope _localization;

    public OldItemEditorPreferencesWindow()
        : this(OldItemEditorPreferences.Default)
    {
    }

    public OldItemEditorPreferencesWindow(OldItemEditorPreferences preferences)
    {
        InitializeComponent();
        _localization = LocalizationScope.Attach(this);
        Closed += (_, _) => _localization.Dispose();
        DirectoryTextBox.Text = preferences.ClientDirectory;
        ExtendedCheckBox.IsChecked = preferences.ExtendedSprites;
        FrameDurationsCheckBox.IsChecked = preferences.FrameDurations;
        TransparencyCheckBox.IsChecked = preferences.Transparency;
        ValidateDirectory();
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationManager.Translate("Wybierz folder klienta z Tibia.dat i Tibia.spr"),
            AllowMultiple = false
        });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            DirectoryTextBox.Text = path;
    }

    private void DirectoryTextBox_TextChanged(object? sender, TextChangedEventArgs e) => ValidateDirectory();

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        DirectoryTextBox.Text = string.Empty;
        ExtendedCheckBox.IsChecked = false;
        FrameDurationsCheckBox.IsChecked = false;
        TransparencyCheckBox.IsChecked = false;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        if (_detectedProfile is null) return;
        Close(new OldItemEditorPreferences(
            DirectoryTextBox.Text?.Trim() ?? string.Empty,
            ExtendedCheckBox.IsChecked == true,
            FrameDurationsCheckBox.IsChecked == true,
            TransparencyCheckBox.IsChecked == true));
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ValidateDirectory()
    {
        _detectedProfile = null;
        var directory = DirectoryTextBox.Text?.Trim() ?? string.Empty;
        if (!Directory.Exists(directory))
        {
            SetValidation("Nie znaleziono folderu klienta.", false);
            return;
        }

        var datPath = FindClientFile(directory, ".dat");
        var sprPath = FindClientFile(directory, ".spr");
        if (datPath is null || sprPath is null)
        {
            SetValidation("Folder musi zawierać parę Tibia.dat i Tibia.spr.", false);
            return;
        }

        try
        {
            var datSignature = ReadSignature(datPath);
            var sprSignature = ReadSignature(sprPath);
            _detectedProfile = OldItemEditorClientCatalog.FindBySignatures(datSignature, sprSignature);
            if (_detectedProfile is null)
            {
                SetValidation($"Nieobsługiwana wersja • DAT: 0x{datSignature:X8} • SPR: 0x{sprSignature:X8}", false);
                return;
            }

            if (_detectedProfile.ClientVersion >= 960) ExtendedCheckBox.IsChecked = true;
            if (_detectedProfile.ClientVersion >= 1050) FrameDurationsCheckBox.IsChecked = true;
            ExtendedCheckBox.IsEnabled = _detectedProfile.ClientVersion < 960;
            FrameDurationsCheckBox.IsEnabled = _detectedProfile.ClientVersion < 1050;
            SetValidation($"Rozpoznano Tibia {_detectedProfile.DisplayName} • OTB {_detectedProfile.OtbVersion}", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetValidation($"Nie można odczytać plików klienta: {ex.Message}", false);
        }
    }

    private void SetValidation(string message, bool valid)
    {
        ValidationText.Text = message;
        ValidationText.Foreground = new SolidColorBrush(Color.Parse(valid ? "#6EE7B7" : "#FCA5A5"));
        ConfirmButton.IsEnabled = valid;
        if (!valid)
        {
            ExtendedCheckBox.IsEnabled = true;
            FrameDurationsCheckBox.IsEnabled = true;
        }
    }

    private static string? FindClientFile(string directory, string extension)
    {
        var preferred = Path.Combine(directory, "Tibia" + extension);
        if (File.Exists(preferred)) return preferred;
        return Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static uint ReadSignature(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < sizeof(uint)) throw new InvalidDataException("Plik klienta jest za mały.");
        return reader.ReadUInt32();
    }
}
