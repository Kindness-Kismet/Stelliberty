using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Settings;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsLanguageViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private AppLanguage _selectedLanguage;

    public SettingsLanguageViewModel(AppSettings settings, IAppSettingsStore settingsStore, ILocalizationService localization)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _selectedLanguage = ParseEnum(settings.Language, AppLanguage.System);
        _localization.SetLanguage(_selectedLanguage);
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public string FieldLabelText => _localization.GetString("Settings.Field.Language");

    public IReadOnlyList<SelectionOption<AppLanguage>> Options =>
    [
        new(AppLanguage.System, _localization.GetString("Language.Option.System")),
        new(AppLanguage.ZhHans, _localization.GetString("Language.Option.ZhHans")),
        new(AppLanguage.ZhHant, _localization.GetString("Language.Option.ZhHant")),
        new(AppLanguage.En, _localization.GetString("Language.Option.En")),
    ];

    public SelectionOption<AppLanguage> SelectedOption
    {
        get => Options.First(option => option.Value == _selectedLanguage);
        set
        {
            if (value.Value == _selectedLanguage)
            {
                return;
            }

            _selectedLanguage = value.Value;
            _settings.Language = value.Value.ToString();
            _settingsStore.Save(_settings);
            _localization.SetLanguage(value.Value);
            AppLogger.Info($"Language switched to {value.DisplayName}");
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        SelectedOption = Options.First(option => option.Value == language);
    }

    public void RefreshFromSettings()
    {
        var restoredLanguage = ParseEnum(_settings.Language, AppLanguage.System);
        if (restoredLanguage == _selectedLanguage)
        {
            OnLanguageChanged(this, EventArgs.Empty);
            return;
        }

        _selectedLanguage = restoredLanguage;
        _localization.SetLanguage(restoredLanguage);
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(FieldLabelText));
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(SelectedOption));
    }

    private static T ParseEnum<T>(string value, T fallback) where T : struct
    {
        return Enum.TryParse<T>(value, out var parsed) ? parsed : fallback;
    }
}
