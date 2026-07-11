using System.Runtime.CompilerServices;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsAppBehaviorViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly IAppBehaviorService? _service;
    private string _statusText = string.Empty;

    public SettingsAppBehaviorViewModel(
        AppSettings settings,
        IAppSettingsStore settingsStore,
        ILocalizationService localization,
        IAppBehaviorService? service)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _service = service;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public string SilentStartText => _localization.GetString("Settings.AppBehavior.SilentStart");

    public string SilentStartDescriptionText => _localization.GetString("Settings.AppBehavior.SilentStart.Description");

    public string MinimizeToTrayText => _localization.GetString("Settings.AppBehavior.MinimizeToTray");

    public string TrayDoubleClickText => _localization.GetString("Settings.AppBehavior.TrayDoubleClick");

    public string TrayDoubleClickDescriptionText => _localization.GetString("Settings.AppBehavior.TrayDoubleClick.Description");

    public string LazyModeText => _localization.GetString("Settings.AppBehavior.LazyMode");

    public string LazyModeDescriptionText => _localization.GetString("Settings.AppBehavior.LazyMode.Description");

    public string StartupText => _localization.GetString("Settings.AppBehavior.Startup");

    public IReadOnlyList<string> Items =>
    [
        SilentStartText,
        MinimizeToTrayText,
        TrayDoubleClickText,
        LazyModeText,
        StartupText,
    ];

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                OnPropertyChanged(nameof(IsStatusVisible));
            }
        }
    }

    public bool IsStatusVisible => !string.IsNullOrWhiteSpace(StatusText);

    public bool IsSilentStartEnabled
    {
        get => _settings.IsSilentStartEnabled;
        set => Apply(_settings.IsSilentStartEnabled, value, next => _settings.IsSilentStartEnabled = next);
    }

    public bool IsMinimizeToTrayEnabled
    {
        get => _settings.IsMinimizeToTrayEnabled;
        set => Apply(_settings.IsMinimizeToTrayEnabled, value, next => _settings.IsMinimizeToTrayEnabled = next);
    }

    public bool IsLazyModeEnabled
    {
        get => _settings.IsLazyModeEnabled;
        set => Apply(_settings.IsLazyModeEnabled, value, next => _settings.IsLazyModeEnabled = next);
    }

    public bool IsTrayDoubleClickEnabled
    {
        get => _settings.IsTrayDoubleClickEnabled;
        set => Apply(_settings.IsTrayDoubleClickEnabled, value, next => _settings.IsTrayDoubleClickEnabled = next);
    }

    public bool IsAutoStartEnabled
    {
        get => _settings.IsAutoStartEnabled;
        set => Apply(_settings.IsAutoStartEnabled, value, next => _settings.IsAutoStartEnabled = next);
    }

    public void RefreshFromSettings()
    {
        OnPropertyChanged(string.Empty);
        _service?.Apply(BuildRequest());
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void Apply<T>(T currentValue, T nextValue, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, nextValue))
        {
            return;
        }

        assign(nextValue);
        try
        {
            if (ShouldApplyPlatformBehavior(propertyName))
            {
                _service?.Apply(BuildRequest());
            }

            _settingsStore.Save(_settings);
            StatusText = _localization.GetString("Settings.AppBehavior.Applied");
        }
        catch (Exception exception)
        {
            assign(currentValue);
            StatusText = exception.Message;
            AppLogger.Warning($"App behavior apply failed: {exception.Message}");
        }

        OnPropertyChanged(propertyName);
    }

    private AppBehaviorApplicationRequest BuildRequest() => new(
        _settings.IsSilentStartEnabled,
        _settings.IsMinimizeToTrayEnabled,
        _settings.IsLazyModeEnabled,
        _settings.IsAutoStartEnabled);

    private bool ShouldApplyPlatformBehavior(string? propertyName)
    {
        return propertyName == nameof(IsAutoStartEnabled)
            || (propertyName == nameof(IsSilentStartEnabled) && _settings.IsAutoStartEnabled);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SilentStartText));
        OnPropertyChanged(nameof(SilentStartDescriptionText));
        OnPropertyChanged(nameof(MinimizeToTrayText));
        OnPropertyChanged(nameof(TrayDoubleClickText));
        OnPropertyChanged(nameof(TrayDoubleClickDescriptionText));
        OnPropertyChanged(nameof(LazyModeText));
        OnPropertyChanged(nameof(LazyModeDescriptionText));
        OnPropertyChanged(nameof(StartupText));
        OnPropertyChanged(nameof(Items));
    }
}
