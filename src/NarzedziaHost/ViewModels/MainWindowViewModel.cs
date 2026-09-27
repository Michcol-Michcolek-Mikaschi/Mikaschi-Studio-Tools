using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Narzedzia.Contracts;
using Narzedzia.Contracts.Localization;

namespace NarzedziaHost.ViewModels;

public sealed record LanguageOption(AppLanguage Language, string Code, string DisplayName);

public partial class MainWindowViewModel : ObservableObject
{
    private readonly Dictionary<IModule, Control> _moduleViews = new();
    private readonly AppLanguagePreferencesStore _languagePreferencesStore;
    private LanguageOption _selectedLanguage;

    [ObservableProperty]
    private IModule? _selectedModule;

    [ObservableProperty]
    private Control? _currentView;

    public ObservableCollection<IModule> Modules { get; } = new();

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new(AppLanguage.Polish, "PL", "Polski"),
        new(AppLanguage.English, "ENG", "English"),
        new(AppLanguage.Spanish, "ES", "Español")
    ];

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            LocalizationManager.SetLanguage(value.Language);
            _languagePreferencesStore.TrySave(value.Language);
            OnPropertyChanged(nameof(CurrentLanguageCode));
            OnPropertyChanged(nameof(SelectedModule));
            OnPropertyChanged(nameof(CurrentView));
        }
    }

    public string CurrentLanguageCode => SelectedLanguage.Code;

    public MainWindowViewModel(AppLanguagePreferencesStore languagePreferencesStore)
    {
        _languagePreferencesStore = languagePreferencesStore;
        _selectedLanguage = AvailableLanguages.First(option =>
            option.Language == LocalizationManager.CurrentLanguage);
    }

    public void LoadModules(IEnumerable<IModule> modules)
    {
        Modules.Clear();
        _moduleViews.Clear();
        foreach (var module in modules)
        {
            module.OnLoad();
            Modules.Add(module);
        }
        SelectedModule = Modules.FirstOrDefault();
    }

    partial void OnSelectedModuleChanged(IModule? value)
    {
        if (value is null)
        {
            CurrentView = null;
            return;
        }

        if (!_moduleViews.TryGetValue(value, out var view))
        {
            view = value.CreateMainView();
            _moduleViews[value] = view;
        }

        CurrentView = view;
    }
}
