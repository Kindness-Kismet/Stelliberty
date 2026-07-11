namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SettingsCoreConfigViewModel
{
    public string CoreLogLevelText => _localization.GetString("Settings.CoreLog.Level");

    public IReadOnlyList<string> CoreLogItems =>
    [
        CoreLogLevelText
    ];

    public IReadOnlyList<SelectionOption<string>> CoreLogLevelOptions =>
    [
        new("silent", _localization.GetString("Settings.CoreLog.Level.Silent")),
        new("error", _localization.GetString("Settings.CoreLog.Level.Error")),
        new("warning", _localization.GetString("Settings.CoreLog.Level.Warning")),
        new("info", _localization.GetString("Settings.CoreLog.Level.Info")),
        new("debug", _localization.GetString("Settings.CoreLog.Level.Debug"))
    ];

    public SelectionOption<string> SelectedCoreLogLevelOption
    {
        get => CoreLogLevelOptions.FirstOrDefault(option => option.Value == _settings.CoreLogLevel)
            ?? CoreLogLevelOptions[0];
        set => SetCoreLogLevel(value.Value);
    }

    // 回写跳过 Options/Selected，避免重设 ItemsSource 撤销 ComboBox 选择。
    private void SetCoreLogLevel(string nextValue)
    {
        if (string.Equals(_settings.CoreLogLevel, nextValue, StringComparison.Ordinal))
        {
            return;
        }

        _settings.CoreLogLevel = nextValue;
        _settingsStore.Save(_settings);
        _coreLogLevelChangeRequests.Add(nextValue);
        _requestRuntimeRefresh("Core log level runtime config refreshed", "Core log level runtime config refresh failed");
        OnPropertyChanged(nameof(CoreLogLevelChangeRequests));
    }
}
