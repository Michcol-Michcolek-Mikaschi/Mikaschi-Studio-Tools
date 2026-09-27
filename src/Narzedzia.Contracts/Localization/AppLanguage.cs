using System.Globalization;

namespace Narzedzia.Contracts.Localization;

public enum AppLanguage
{
    Polish,
    English,
    Spanish
}

public static class LocalizationManager
{
    private static AppLanguage _currentLanguage = AppLanguage.Polish;

    public static event EventHandler? LanguageChanged;

    public static AppLanguage CurrentLanguage => _currentLanguage;

    public static bool IsEnglish => _currentLanguage == AppLanguage.English;

    public static bool IsSpanish => _currentLanguage == AppLanguage.Spanish;

    public static void ToggleLanguage() => SetLanguage(_currentLanguage switch
    {
        AppLanguage.Polish => AppLanguage.English,
        AppLanguage.English => AppLanguage.Spanish,
        _ => AppLanguage.Polish
    });

    public static void SetLanguage(AppLanguage language)
    {
        if (!Enum.IsDefined(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "Unsupported application language.");
        }

        var changed = _currentLanguage != language;
        _currentLanguage = language;
        var culture = CultureInfo.GetCultureInfo(language switch
        {
            AppLanguage.English => "en-US",
            AppLanguage.Spanish => "es-ES",
            _ => "pl-PL"
        });
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        if (changed)
        {
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static string Translate(string? sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return sourceText ?? string.Empty;
        }

        return _currentLanguage switch
        {
            AppLanguage.English => PolishEnglishTranslations.Translate(sourceText),
            AppLanguage.Spanish => PolishSpanishTranslations.Translate(sourceText),
            _ => SourcePolishTranslations.Translate(sourceText)
        };
    }

    public static string TranslateToEnglish(string? polishText) =>
        string.IsNullOrEmpty(polishText)
            ? polishText ?? string.Empty
            : PolishEnglishTranslations.Translate(polishText);

    public static string TranslateToSpanish(string? sourceText) =>
        string.IsNullOrEmpty(sourceText)
            ? sourceText ?? string.Empty
            : PolishSpanishTranslations.Translate(sourceText);

    public static string TranslateToPolish(string? sourceText) =>
        string.IsNullOrEmpty(sourceText)
            ? sourceText ?? string.Empty
            : SourcePolishTranslations.Translate(sourceText);
}
