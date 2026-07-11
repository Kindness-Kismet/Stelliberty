using System.Runtime.CompilerServices;
using System.Windows.Input;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Updates;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SettingsUpdateViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly Func<DateTimeOffset> _now;
    private readonly AppUpdateAutoCheckScheduler? _scheduler;
    private readonly TimeSpan _manualCheckMinimumDuration;
    private readonly RelayCommand _checkCommand;
    private string _lastOperation = string.Empty;
    private string _latestVersion = string.Empty;
    private string _latestReleaseUrl = string.Empty;
    private string _statusText = string.Empty;
    private bool _isChecking;
    private bool _isUpdateDialogVisible;
    private static readonly TimeSpan DefaultManualCheckMinimumDuration = TimeSpan.FromMilliseconds(600);

    public SettingsUpdateViewModel(
        AppSettings settings,
        IAppSettingsStore settingsStore,
        ILocalizationService localization,
        Func<DateTimeOffset> now,
        IAppUpdateChecker? checker,
        TimeSpan? manualCheckMinimumDuration = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _now = now;
        _manualCheckMinimumDuration = manualCheckMinimumDuration ?? DefaultManualCheckMinimumDuration;
        // 手动和自动检查共用设置，所以时间戳立即刷新 UI。
        _scheduler = checker is null ? null : new AppUpdateAutoCheckScheduler(checker, () => settings, settingsStore.Save, now);
        _localization.LanguageChanged += OnLanguageChanged;
        _checkCommand = new RelayCommand(() => _ = CheckAsync(), () => !IsChecking);
        CheckCommand = _checkCommand;
        IgnoreLatestVersionCommand = new RelayCommand(IgnoreLatestVersion);
        CloseUpdateDialogCommand = new RelayCommand(() => IsUpdateDialogVisible = false);
    }

    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public string AutoCheckText => _localization.GetString("Settings.Update.AutoCheck");

    public string OnStartupText => _localization.GetString("Settings.Update.OnStartup");

    public string FixedIntervalText => _localization.GetString("Settings.Update.FixedInterval");

    public string ManualText => _localization.GetString("Settings.Update.Manual");

    public string ChannelText => _localization.GetString("Settings.Update.Channel");

    public IReadOnlyList<string> Items =>
    [
        AutoCheckText,
        OnStartupText,
        FixedIntervalText,
        ManualText,
        ChannelText,
    ];

    public bool IsAutoCheckEnabled
    {
        get => _settings.IsAutoCheckUpdateEnabled;
        set => SetSetting(_settings.IsAutoCheckUpdateEnabled, value, next => _settings.IsAutoCheckUpdateEnabled = next);
    }

    public IReadOnlyList<SelectionOption<string>> CheckIntervalOptions =>
    [
        new("startup", OnStartupText),
        new("1day", _localization.GetString("Settings.Update.Interval.OneDay")),
        new("7days", _localization.GetString("Settings.Update.Interval.SevenDays")),
        new("14days", _localization.GetString("Settings.Update.Interval.FourteenDays")),
    ];

    public SelectionOption<string> SelectedCheckIntervalOption
    {
        get => CheckIntervalOptions.FirstOrDefault(option => option.Value == _settings.AppUpdateCheckInterval)
            ?? CheckIntervalOptions[0];
        set => SetSetting(_settings.AppUpdateCheckInterval, value.Value, next => _settings.AppUpdateCheckInterval = next);
    }

    public IReadOnlyList<SelectionOption<string>> ChannelOptions =>
    [
        new("stable", _localization.GetString("Settings.Update.Channel.Stable")),
    ];

    public SelectionOption<string> SelectedChannelOption
    {
        get => ChannelOptions.FirstOrDefault(option => option.Value == _settings.AppUpdateChannel)
            ?? ChannelOptions[0];
        set => SetSetting(_settings.AppUpdateChannel, value.Value, next => _settings.AppUpdateChannel = next);
    }

    public string LastOperation
    {
        get => _lastOperation;
        private set => SetProperty(ref _lastOperation, value);
    }

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

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (SetProperty(ref _isChecking, value))
            {
                _checkCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsUpdateDialogVisible
    {
        get => _isUpdateDialogVisible;
        private set => SetProperty(ref _isUpdateDialogVisible, value);
    }

    public bool CanIgnoreLatestVersion => !string.IsNullOrWhiteSpace(_latestVersion)
        && !string.Equals(_latestVersion, _settings.IgnoredUpdateVersion, StringComparison.Ordinal);

    public bool CanOpenLatestRelease => !string.IsNullOrWhiteSpace(_latestReleaseUrl);

    public string AutoCheckStateText => _localization.GetString(IsAutoCheckEnabled ? "Common.Enabled" : "Common.Disabled");

    public string CheckIntervalText => SelectedCheckIntervalOption.DisplayName;

    public string LastCheckText => _settings.LastAppUpdateCheckTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? _localization.GetString("Settings.Update.NotChecked");

    public string LatestVersionText => string.IsNullOrWhiteSpace(_latestVersion) ? _localization.GetString("Settings.Update.NoUpdate") : _latestVersion;

    public string LatestReleaseUrl => _latestReleaseUrl;

    public string IgnoredVersionText => string.IsNullOrWhiteSpace(_settings.IgnoredUpdateVersion) ? _localization.GetString("Common.None") : _settings.IgnoredUpdateVersion;

    public ICommand CheckCommand { get; }

    public ICommand IgnoreLatestVersionCommand { get; }

    public ICommand CloseUpdateDialogCommand { get; }

    public void ApplyAutoCheckResult(AppUpdateAutoCheckResult result)
    {
        if (result.WasChecked)
        {
            LastOperation = "AutoCheck";
            StatusText = result.Message;
        }
    }

    public void RefreshFromSettings()
    {
        OnPropertyChanged(string.Empty);
        RaiseUpdateSummaryChanged();
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    public async Task CheckAsync()
    {
        if (IsChecking)
        {
            return;
        }

        LastOperation = "ManualCheck";
        IsChecking = true;
        try
        {
            var result = await RunManualCheckAsync();
            ApplyCheckResult(result);
            RaiseManualCheckToast(result);
        }
        catch (Exception exception)
        {
            var result = new AppUpdateCheckResult(false, null, exception.Message, IsFailure: true);
            ApplyCheckResult(result);
            RaiseManualCheckToast(result);
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void ApplyCheckResult(AppUpdateCheckResult? result)
    {
        _latestVersion = result?.HasUpdate == true ? result.LatestVersion ?? string.Empty : string.Empty;
        _latestReleaseUrl = result?.ReleaseUrl ?? string.Empty;
        IsUpdateDialogVisible = result?.HasUpdate == true;
        StatusText = result?.Message ?? string.Empty;
        RaiseUpdateSummaryChanged();
    }

    private async Task<AppUpdateCheckResult?> RunManualCheckAsync()
    {
        var checkTask = _scheduler is null
            ? Task.FromResult<AppUpdateCheckResult?>(null)
            : Task.Run<AppUpdateCheckResult?>(_scheduler.CheckManually);
        var durationTask = Task.Delay(_manualCheckMinimumDuration);
        await Task.WhenAll(checkTask, durationTask);
        return await checkTask;
    }

    private void RaiseManualCheckToast(AppUpdateCheckResult? result)
    {
        if (result?.HasUpdate == true)
        {
            return;
        }

        if (result is null)
        {
            ToastRequested?.Invoke(this, (_localization.GetString("Settings.Update.Toast.CheckUnavailable"), ToastType.Error));
            return;
        }

        if (result.IsFailure)
        {
            ToastRequested?.Invoke(this, (_localization.GetString("Settings.Update.Toast.CheckFailed"), ToastType.Error));
            return;
        }

        ToastRequested?.Invoke(this, (_localization.GetString("Settings.Update.Toast.NoUpdate"), ToastType.Info));
    }

    private void IgnoreLatestVersion()
    {
        if (string.IsNullOrWhiteSpace(_latestVersion))
        {
            return;
        }

        _settings.IgnoredUpdateVersion = _latestVersion;
        _settingsStore.Save(_settings);
        IsUpdateDialogVisible = false;
        StatusText = string.Format(_localization.GetString("Settings.Update.IgnoredVersion.Status"), _latestVersion);
        RaiseUpdateSummaryChanged();
    }

    private void SetSetting<T>(T currentValue, T nextValue, Action<T> assign, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, nextValue))
        {
            return;
        }

        assign(nextValue);
        _settingsStore.Save(_settings);
        OnPropertyChanged(propertyName);
        RaiseUpdateSummaryChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AutoCheckText));
        OnPropertyChanged(nameof(OnStartupText));
        OnPropertyChanged(nameof(FixedIntervalText));
        OnPropertyChanged(nameof(ManualText));
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(CheckIntervalOptions));
        OnPropertyChanged(nameof(SelectedCheckIntervalOption));
        OnPropertyChanged(nameof(ChannelOptions));
        OnPropertyChanged(nameof(SelectedChannelOption));
        OnPropertyChanged(nameof(ChannelText));
        RaiseUpdateSummaryChanged();
    }

    private void RaiseUpdateSummaryChanged()
    {
        OnPropertyChanged(nameof(CanIgnoreLatestVersion));
        OnPropertyChanged(nameof(CanOpenLatestRelease));
        OnPropertyChanged(nameof(AutoCheckStateText));
        OnPropertyChanged(nameof(CheckIntervalText));
        OnPropertyChanged(nameof(LastCheckText));
        OnPropertyChanged(nameof(LatestVersionText));
        OnPropertyChanged(nameof(LatestReleaseUrl));
        OnPropertyChanged(nameof(IgnoredVersionText));
    }
}
