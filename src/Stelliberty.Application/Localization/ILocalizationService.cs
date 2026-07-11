namespace Stelliberty.Application.Localization;

public interface ILocalizationService
{
    AppLanguage CurrentLanguage { get; }

    AppLanguage EffectiveLanguage { get; }

    event EventHandler? LanguageChanged;

    void SetLanguage(AppLanguage language);

    string GetString(string key);
}
