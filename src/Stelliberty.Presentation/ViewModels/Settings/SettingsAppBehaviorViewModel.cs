using System.Runtime.CompilerServices;
using System.Windows.Input;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsAppBehaviorViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly IAppBehaviorService? _service;
    private readonly IGlobalHotkeyService? _globalHotkeyService;

    public SettingsAppBehaviorViewModel(
        AppSettings settings,
        IAppSettingsStore settingsStore,
        ILocalizationService localization,
        IAppBehaviorService? service,
        IGlobalHotkeyService? globalHotkeyService = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _service = service;
        _globalHotkeyService = globalHotkeyService;
        ToggleAutoStartCommand = new RelayCommand(() => SetAutoStartEnabled(!IsAutoStartEnabled));
        ClearWindowToggleHotkeyCommand = new RelayCommand(() => SetWindowToggleHotkey(string.Empty));
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public string SilentStartText => _localization.GetString("Settings.AppBehavior.SilentStart");

    public string SilentStartDescriptionText => _localization.GetString("Settings.AppBehavior.SilentStart.Description");

    public string MinimizeToTrayText => _localization.GetString("Settings.AppBehavior.MinimizeToTray");

    public string TrayDoubleClickText => _localization.GetString("Settings.AppBehavior.TrayDoubleClick");

    public string TrayDoubleClickDescriptionText => _localization.GetString("Settings.AppBehavior.TrayDoubleClick.Description");

    public string LazyModeText => _localization.GetString("Settings.AppBehavior.LazyMode");

    public string LazyModeDescriptionText => _localization.GetString("Settings.AppBehavior.LazyMode.Description");

    public string StartupText => _localization.GetString("Settings.AppBehavior.Startup");

    public string WindowToggleHotkeyText => _localization.GetString("Settings.AppBehavior.WindowToggleHotkey");

    public string WindowToggleHotkeyDescriptionText => _localization.GetString("Settings.AppBehavior.WindowToggleHotkey.Description");

    public string WindowToggleHotkeyWatermarkText => _localization.GetString("Settings.AppBehavior.WindowToggleHotkey.Watermark");

    public string ClearWindowToggleHotkeyText => _localization.GetString("Settings.AppBehavior.WindowToggleHotkey.Clear");

    public IReadOnlyList<string> Items =>
    [
        SilentStartText,
        MinimizeToTrayText,
        TrayDoubleClickText,
        LazyModeText,
        StartupText,
        WindowToggleHotkeyText,
    ];

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

    public bool IsAutoStartEnabled => _settings.IsAutoStartEnabled;

    public string WindowToggleHotkey => _settings.WindowToggleHotkey;

    public ICommand ToggleAutoStartCommand { get; }

    public ICommand ClearWindowToggleHotkeyCommand { get; }

    public void SetAutoStartEnabled(bool isEnabled)
    {
        if (_settings.IsAutoStartEnabled == isEnabled)
        {
            return;
        }

        try
        {
            _service?.Apply(BuildRequest(isEnabled));
            _settings.IsAutoStartEnabled = isEnabled;
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"App behavior apply failed: {exception.Message}");
        }

        OnPropertyChanged(nameof(IsAutoStartEnabled));
    }

    public void RefreshFromSettings()
    {
        OnPropertyChanged(string.Empty);
    }

    public void SetWindowToggleHotkey(string gesture)
    {
        var nextValue = gesture.Trim();
        var currentValue = _settings.WindowToggleHotkey;
        if (string.Equals(currentValue, nextValue, StringComparison.Ordinal))
        {
            return;
        }

        var result = _globalHotkeyService?.Apply(nextValue) ?? GlobalHotkeyApplyResult.Success();
        if (!result.IsSuccess)
        {
            AppLogger.Warning($"Global window hotkey apply failed: {result.Error}");
            ToastRequested?.Invoke(this, (HotkeyErrorText(result.Error), ToastType.Error));
            return;
        }

        _settings.WindowToggleHotkey = nextValue;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            var restoreResult = _globalHotkeyService?.Apply(currentValue) ?? GlobalHotkeyApplyResult.Success();
            _settings.WindowToggleHotkey = currentValue;
            AppLogger.Warning($"Global window hotkey save failed: {exception.Message}");
            if (!restoreResult.IsSuccess)
            {
                AppLogger.Warning($"Global window hotkey restore failed: {restoreResult.Error}");
            }

            ToastRequested?.Invoke(
                this,
                (_localization.GetString("Settings.AppBehavior.WindowToggleHotkey.Toast.SaveFailed"), ToastType.Error));
            OnPropertyChanged(nameof(WindowToggleHotkey));
            return;
        }

        OnPropertyChanged(nameof(WindowToggleHotkey));
        var toastKey = string.IsNullOrEmpty(nextValue)
            ? "Settings.AppBehavior.WindowToggleHotkey.Toast.Cleared"
            : "Settings.AppBehavior.WindowToggleHotkey.Toast.Registered";
        ToastRequested?.Invoke(this, (_localization.GetString(toastKey), ToastType.Success));
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
            _settingsStore.Save(_settings);
        }
        catch (Exception exception)
        {
            assign(currentValue);
            AppLogger.Warning($"App behavior apply failed: {exception.Message}");
        }

        OnPropertyChanged(propertyName);
    }

    private AppBehaviorApplicationRequest BuildRequest(bool isAutoStartEnabled) => new(
        _settings.IsSilentStartEnabled,
        _settings.IsMinimizeToTrayEnabled,
        _settings.IsLazyModeEnabled,
        isAutoStartEnabled);

    private string HotkeyErrorText(GlobalHotkeyApplyError error)
    {
        var key = error switch
        {
            GlobalHotkeyApplyError.Invalid => "Settings.AppBehavior.WindowToggleHotkey.Toast.Invalid",
            GlobalHotkeyApplyError.Conflict => "Settings.AppBehavior.WindowToggleHotkey.Toast.Conflict",
            GlobalHotkeyApplyError.Unsupported => "Settings.AppBehavior.WindowToggleHotkey.Toast.Unsupported",
            _ => "Settings.AppBehavior.WindowToggleHotkey.Toast.Failed",
        };
        return _localization.GetString(key);
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
        OnPropertyChanged(nameof(WindowToggleHotkeyText));
        OnPropertyChanged(nameof(WindowToggleHotkeyDescriptionText));
        OnPropertyChanged(nameof(WindowToggleHotkeyWatermarkText));
        OnPropertyChanged(nameof(ClearWindowToggleHotkeyText));
        OnPropertyChanged(nameof(Items));
    }

}
