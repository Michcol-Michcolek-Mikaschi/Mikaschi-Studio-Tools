using CommunityToolkit.Mvvm.ComponentModel;
using Modules.MapEditor.Services;

namespace Modules.MapEditor.ViewModels;

public partial class CreatureImportSourceViewModel : ObservableObject
{
    public CreatureImportSourceViewModel(CreatureImportSourcePreference preference)
    {
        Path = preference.Path;
        Kind = preference.Kind;
        _isEnabled = preference.Enabled;
        _status = "Oczekuje na wczytanie.";
    }

    public string Path { get; }
    public CreatureImportSourceKind Kind { get; }
    public string Name
    {
        get
        {
            var trimmed = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? Path : name;
        }
    }

    public string KindLabel => Kind == CreatureImportSourceKind.Directory ? "Katalog" : "Plik XML";
    public string CountLabel => CreatureCount == 1 ? "1 stworzenie" : $"{CreatureCount} stworzeń";
    public bool HasWarnings => WarningCount > 0;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private int _creatureCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private string _status;

    partial void OnCreatureCountChanged(int value) => OnPropertyChanged(nameof(CountLabel));
    partial void OnWarningCountChanged(int value) => OnPropertyChanged(nameof(HasWarnings));

    public CreatureImportSourcePreference ToPreference() => new(Path, Kind, IsEnabled);
}
