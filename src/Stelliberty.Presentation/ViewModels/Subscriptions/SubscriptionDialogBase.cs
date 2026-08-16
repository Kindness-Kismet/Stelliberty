using System.Globalization;
using System.Windows.Input;
using Stelliberty.Application.Localization;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.Commands;
using Stelliberty.Presentation.Validation;

namespace Stelliberty.Presentation.ViewModels;

// 订阅添加/编辑共享字段与输入机制；导入类型由子类固定或切换。
public abstract class SubscriptionDialogBase : ViewModelBase, IDisposable
{
    private readonly RelayCommand _confirmCommand;

    protected readonly ILocalizationService? Localization;

    protected string _name = string.Empty;
    protected string _url = string.Empty;
    protected string _userAgent = string.Empty;
    protected string _ageSecretKey = string.Empty;
    protected bool _isUserAgentEditing;
    protected int _autoTestDelayIntervalMinutes;
    protected string _autoTestDelayIntervalMinutesText = string.Empty;
    protected string _autoTestDelayIntervalMinutesError = string.Empty;
    protected bool _isAutoTestDelayIntervalEditing;
    protected SubscriptionAutoUpdateMode _selectedAutoUpdateMode;
    protected int _autoUpdateIntervalMinutes;
    protected string _autoUpdateIntervalMinutesText = "0";
    protected string _autoUpdateIntervalMinutesError = string.Empty;
    protected SubscriptionUpdateProxyMode _selectedUpdateProxyMode;
    protected bool _hasClipboardText;
    protected bool _hasAttemptedSubmit;
    protected string _nameError = string.Empty;
    protected string _urlError = string.Empty;

    protected SubscriptionDialogBase(ILocalizationService? localization)
    {
        Localization = localization;
        if (Localization is not null)
        {
            Localization.LanguageChanged += HandleLanguageChanged;
        }
        SelectDirectProxyModeCommand = new RelayCommand(() => SelectProxyMode(SubscriptionUpdateProxyMode.Direct));
        SelectSystemProxyModeCommand = new RelayCommand(() => SelectProxyMode(SubscriptionUpdateProxyMode.SystemProxy));
        SelectCoreProxyModeCommand = new RelayCommand(() => SelectProxyMode(SubscriptionUpdateProxyMode.Core));
        _confirmCommand = new RelayCommand(Confirm, () => CanSubmit);
        ConfirmCommand = _confirmCommand;
        CancelCommand = new RelayCommand(Cancel);
        PropertyChanged += HandlePropertyChanged;
    }

    public event EventHandler? DialogStateChanged;

    public event EventHandler<DialogInputField>? InputFocusRequested;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                if (_hasAttemptedSubmit)
                {
                    ValidateName();
                }
            }
        }
    }

    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value))
            {
                OnUrlChanged();
                if (_hasAttemptedSubmit)
                {
                    ValidateUrl();
                }
            }
        }
    }

    public string UserAgent
    {
        get => _userAgent;
        set
        {
            if (SetProperty(ref _userAgent, value))
            {
                OnPropertyChanged(nameof(UserAgentText));
            }
        }
    }

    public string UserAgentText
    {
        get => FormatUserAgentText();
        set
        {
            var text = value ?? string.Empty;
            if (!_isUserAgentEditing && string.Equals(text.Trim(), Localize("Common.Default"), StringComparison.Ordinal))
            {
                text = SubscriptionDefaults.UserAgent;
            }

            if (_userAgent == text)
            {
                return;
            }

            _userAgent = text;
            OnPropertyChanged(nameof(UserAgent));
            OnPropertyChanged(nameof(UserAgentText));
        }
    }

    public string AgeSecretKey
    {
        get => _ageSecretKey;
        set => SetProperty(ref _ageSecretKey, value ?? string.Empty);
    }

    public bool IsAutoTestDelayEnabled => AutoTestDelayIntervalMinutes > 0;

    public int AutoTestDelayIntervalMinutes
    {
        get => _autoTestDelayIntervalMinutes;
        set => SetAutoTestDelayIntervalMinutes(Math.Max(0, value));
    }

    public string AutoTestDelayIntervalMinutesText
    {
        get => _autoTestDelayIntervalMinutesText;
        set
        {
            if (_autoTestDelayIntervalMinutesText == value)
            {
                return;
            }

            _autoTestDelayIntervalMinutesText = value ?? string.Empty;
            if (TryParseMinuteInput(_autoTestDelayIntervalMinutesText, out var minutes))
            {
                _autoTestDelayIntervalMinutes = minutes;
                _autoTestDelayIntervalMinutesError = string.Empty;
            }
            else if (_hasAttemptedSubmit)
            {
                _autoTestDelayIntervalMinutesError = Localize("Subscriptions.Validation.Minutes");
            }
            else
            {
                _autoTestDelayIntervalMinutesError = string.Empty;
            }

            NotifyAutoTestDelayIntervalChanged();
        }
    }

    public string AutoTestDelayIntervalMinutesError => _autoTestDelayIntervalMinutesError;

    public bool IsAutoTestDelayIntervalErrorVisible => !string.IsNullOrEmpty(_autoTestDelayIntervalMinutesError);

    public IReadOnlyList<SelectionOption<SubscriptionAutoUpdateMode>> AutoUpdateModeOptions =>
    [
        new(SubscriptionAutoUpdateMode.Disabled, Localize("Subscriptions.AutoUpdate.Disabled")),
        new(SubscriptionAutoUpdateMode.Startup, Localize("Subscriptions.AutoUpdate.Startup")),
        new(SubscriptionAutoUpdateMode.Interval, Localize("Subscriptions.AutoUpdate.Interval"))
    ];

    public SubscriptionAutoUpdateMode SelectedAutoUpdateMode
    {
        get => _selectedAutoUpdateMode;
        set
        {
            if (SetProperty(ref _selectedAutoUpdateMode, value))
            {
                if (!IsAutoUpdateIntervalEnabled)
                {
                    _autoUpdateIntervalMinutesError = string.Empty;
                }
                else if (_hasAttemptedSubmit)
                {
                    ValidateAutoUpdateInterval();
                }

                OnPropertyChanged(nameof(IsAutoUpdateIntervalEnabled));
                OnPropertyChanged(nameof(AutoUpdateIntervalMinutesError));
                OnPropertyChanged(nameof(IsAutoUpdateIntervalErrorVisible));
            }
        }
    }

    public SelectionOption<SubscriptionAutoUpdateMode> SelectedAutoUpdateModeOption
    {
        get => AutoUpdateModeOptions.First(option => option.Value == SelectedAutoUpdateMode);
        set => SelectedAutoUpdateMode = value.Value;
    }

    public int AutoUpdateIntervalMinutes
    {
        get => _autoUpdateIntervalMinutes;
        set => SetAutoUpdateIntervalMinutes(Math.Max(0, value));
    }

    public string AutoUpdateIntervalMinutesText
    {
        get => _autoUpdateIntervalMinutesText;
        set
        {
            if (_autoUpdateIntervalMinutesText == value)
            {
                return;
            }

            _autoUpdateIntervalMinutesText = value ?? string.Empty;
            if (TryParseMinuteInput(_autoUpdateIntervalMinutesText, out var minutes))
            {
                _autoUpdateIntervalMinutes = minutes;
                _autoUpdateIntervalMinutesError = string.Empty;
            }
            else if (_hasAttemptedSubmit)
            {
                _autoUpdateIntervalMinutesError = Localize("Subscriptions.Validation.Minutes");
            }
            else
            {
                _autoUpdateIntervalMinutesError = string.Empty;
            }

            NotifyAutoUpdateIntervalChanged();
        }
    }

    public string AutoUpdateIntervalMinutesError => _autoUpdateIntervalMinutesError;

    // 间隔错误只在间隔模式显示；禁用字段保持静默。
    public bool IsAutoUpdateIntervalErrorVisible =>
        IsAutoUpdateIntervalEnabled && !string.IsNullOrEmpty(_autoUpdateIntervalMinutesError);

    // 间隔输入只在间隔模式可交互。
    public bool IsAutoUpdateIntervalEnabled => _selectedAutoUpdateMode == SubscriptionAutoUpdateMode.Interval;

    public string NameError => _nameError;

    public bool IsNameErrorVisible => !string.IsNullOrEmpty(_nameError);

    public string UrlError => _urlError;

    public bool IsUrlErrorVisible => !string.IsNullOrEmpty(_urlError);

    public SubscriptionUpdateProxyMode SelectedUpdateProxyMode
    {
        get => _selectedUpdateProxyMode;
        set => SetProperty(ref _selectedUpdateProxyMode, value);
    }

    public bool IsDirectProxyModeSelected => SelectedUpdateProxyMode == SubscriptionUpdateProxyMode.Direct;

    public bool IsSystemProxyModeSelected => SelectedUpdateProxyMode == SubscriptionUpdateProxyMode.SystemProxy;

    public bool IsCoreProxyModeSelected => SelectedUpdateProxyMode == SubscriptionUpdateProxyMode.Core;

    // 粘贴按钮仅在远程语境且 URL 为空时出现。
    public bool IsUrlPasteButtonVisible => IsRemoteContext && string.IsNullOrWhiteSpace(_url);

    public bool CanPasteUrlFromClipboard => IsUrlPasteButtonVisible && _hasClipboardText && !IsPasteBlocked;

    public abstract bool CanSubmit { get; }

    // 子类给出当前是否处于远程输入语境；决定粘贴按钮显隐。
    protected abstract bool IsRemoteContext { get; }

    // 子类可临时阻止粘贴（如提交中）；默认放行。
    protected virtual bool IsPasteBlocked => false;

    public ICommand SelectDirectProxyModeCommand { get; }

    public ICommand SelectSystemProxyModeCommand { get; }

    public ICommand SelectCoreProxyModeCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public void SetClipboardTextAvailable(bool hasText)
    {
        SetProperty(ref _hasClipboardText, hasText, nameof(CanPasteUrlFromClipboard));
    }

    public void PasteUrl(string value)
    {
        if (IsRemoteContext && string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(value))
        {
            Url = value.Trim();
        }
    }

    public void BeginUserAgentEdit()
    {
        if (_isUserAgentEditing)
        {
            return;
        }

        _isUserAgentEditing = true;
        OnPropertyChanged(nameof(UserAgentText));
    }

    public void EndUserAgentEdit()
    {
        if (!_isUserAgentEditing)
        {
            return;
        }

        _isUserAgentEditing = false;
        if (string.IsNullOrWhiteSpace(_userAgent))
        {
            _userAgent = SubscriptionDefaults.UserAgent;
            OnPropertyChanged(nameof(UserAgent));
        }

        OnPropertyChanged(nameof(UserAgentText));
    }

    public void BeginAutoTestDelayIntervalEdit()
    {
        if (_isAutoTestDelayIntervalEditing)
        {
            return;
        }

        _isAutoTestDelayIntervalEditing = true;
        RefreshAutoTestDelayIntervalDisplay();
    }

    public void EndAutoTestDelayIntervalEdit()
    {
        if (!_isAutoTestDelayIntervalEditing)
        {
            return;
        }

        _isAutoTestDelayIntervalEditing = false;
        RefreshAutoTestDelayIntervalDisplay();
    }

    public virtual void Dispose()
    {
        PropertyChanged -= HandlePropertyChanged;
        if (Localization is not null)
        {
            Localization.LanguageChanged -= HandleLanguageChanged;
        }
    }

    protected abstract void Confirm();

    protected abstract void Cancel();

    // 子类改完广播前刷新自身字段；语言切换时也复用。
    protected abstract void RaiseStateChanged();

    // 从已知配置批量重置共享字段，不逐项通知。
    protected void ResetSharedState(
        string name,
        string url,
        string userAgent,
        string ageSecretKey,
        int autoTestDelayIntervalMinutes,
        SubscriptionAutoUpdateMode autoUpdateMode,
        int autoUpdateIntervalMinutes,
        SubscriptionUpdateProxyMode updateProxyMode)
    {
        _name = name;
        _url = url;
        _userAgent = userAgent;
        _ageSecretKey = ageSecretKey;
        _isUserAgentEditing = false;
        _autoTestDelayIntervalMinutes = Math.Max(0, autoTestDelayIntervalMinutes);
        _isAutoTestDelayIntervalEditing = false;
        _autoTestDelayIntervalMinutesText = FormatAutoTestDelayIntervalText(_autoTestDelayIntervalMinutes);
        _autoTestDelayIntervalMinutesError = string.Empty;
        _selectedAutoUpdateMode = autoUpdateMode;
        _autoUpdateIntervalMinutes = Math.Max(0, autoUpdateIntervalMinutes);
        _autoUpdateIntervalMinutesText = _autoUpdateIntervalMinutes.ToString(CultureInfo.InvariantCulture);
        _autoUpdateIntervalMinutesError = string.Empty;
        _selectedUpdateProxyMode = updateProxyMode;
        _hasAttemptedSubmit = false;
        _nameError = string.Empty;
        _urlError = string.Empty;
    }

    // 广播共享字段属性，不触发 DialogStateChanged。
    protected void RaiseSharedStateChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(UserAgent));
        OnPropertyChanged(nameof(UserAgentText));
        OnPropertyChanged(nameof(AgeSecretKey));
        OnPropertyChanged(nameof(AutoTestDelayIntervalMinutes));
        OnPropertyChanged(nameof(AutoTestDelayIntervalMinutesText));
        OnPropertyChanged(nameof(AutoTestDelayIntervalMinutesError));
        OnPropertyChanged(nameof(IsAutoTestDelayIntervalErrorVisible));
        OnPropertyChanged(nameof(IsAutoTestDelayEnabled));
        OnPropertyChanged(nameof(SelectedAutoUpdateMode));
        OnPropertyChanged(nameof(SelectedAutoUpdateModeOption));
        OnPropertyChanged(nameof(AutoUpdateIntervalMinutes));
        OnPropertyChanged(nameof(AutoUpdateIntervalMinutesText));
        OnPropertyChanged(nameof(AutoUpdateIntervalMinutesError));
        OnPropertyChanged(nameof(IsAutoUpdateIntervalErrorVisible));
        OnPropertyChanged(nameof(IsAutoUpdateIntervalEnabled));
        OnPropertyChanged(nameof(SelectedUpdateProxyMode));
        OnPropertyChanged(nameof(IsDirectProxyModeSelected));
        OnPropertyChanged(nameof(IsSystemProxyModeSelected));
        OnPropertyChanged(nameof(IsCoreProxyModeSelected));
        OnPropertyChanged(nameof(IsUrlPasteButtonVisible));
        OnPropertyChanged(nameof(CanPasteUrlFromClipboard));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(IsNameErrorVisible));
        OnPropertyChanged(nameof(UrlError));
        OnPropertyChanged(nameof(IsUrlErrorVisible));
    }

    protected void NotifyDialogStateChanged()
    {
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    protected bool ValidateSharedInputs(bool isLocal)
    {
        _hasAttemptedSubmit = true;
        ValidateName();
        ValidateUrl();
        ValidateAutoTestDelayInterval();
        if (isLocal || !IsAutoUpdateIntervalEnabled)
        {
            _autoUpdateIntervalMinutesError = string.Empty;
            OnPropertyChanged(nameof(AutoUpdateIntervalMinutesError));
            OnPropertyChanged(nameof(IsAutoUpdateIntervalErrorVisible));
        }
        else
        {
            ValidateAutoUpdateInterval();
        }

        return !IsNameErrorVisible
            && !IsUrlErrorVisible
            && !IsAutoTestDelayIntervalErrorVisible
            && !IsAutoUpdateIntervalErrorVisible;
    }

    protected void RequestInputFocus(DialogInputField field)
    {
        InputFocusRequested?.Invoke(this, field);
    }

    protected void ClearUrlError()
    {
        if (string.IsNullOrEmpty(_urlError))
        {
            return;
        }

        _urlError = string.Empty;
        OnPropertyChanged(nameof(UrlError));
        OnPropertyChanged(nameof(IsUrlErrorVisible));
    }

    protected void RefreshUrlValidation() => ValidateUrl();

    protected string NormalizeUserAgent()
    {
        return IsDefaultUserAgent(_userAgent) ? SubscriptionDefaults.UserAgent : _userAgent.Trim();
    }

    // URL 变化刷新粘贴按钮显隐；子类可扩展。
    protected virtual void OnUrlChanged()
    {
        OnPropertyChanged(nameof(IsUrlPasteButtonVisible));
        OnPropertyChanged(nameof(CanPasteUrlFromClipboard));
    }

    // 语言切换时子类刷新自身广播；基类已刷新延迟显示与 UA 文本。
    protected virtual void OnLanguageChanged()
    {
        RaiseStateChanged();
    }

    private void SelectProxyMode(SubscriptionUpdateProxyMode mode)
    {
        _selectedUpdateProxyMode = mode;
        OnPropertyChanged(nameof(SelectedUpdateProxyMode));
        OnPropertyChanged(nameof(IsDirectProxyModeSelected));
        OnPropertyChanged(nameof(IsSystemProxyModeSelected));
        OnPropertyChanged(nameof(IsCoreProxyModeSelected));
    }

    private void SetAutoTestDelayIntervalMinutes(int minutes)
    {
        var text = FormatAutoTestDelayIntervalText(minutes);
        if (_autoTestDelayIntervalMinutes == minutes
            && _autoTestDelayIntervalMinutesText == text
            && string.IsNullOrEmpty(_autoTestDelayIntervalMinutesError))
        {
            return;
        }

        _autoTestDelayIntervalMinutes = minutes;
        _autoTestDelayIntervalMinutesText = text;
        _autoTestDelayIntervalMinutesError = string.Empty;
        NotifyAutoTestDelayIntervalChanged();
    }

    private void SetAutoUpdateIntervalMinutes(int minutes)
    {
        var text = minutes.ToString(CultureInfo.InvariantCulture);
        if (_autoUpdateIntervalMinutes == minutes
            && _autoUpdateIntervalMinutesText == text
            && string.IsNullOrEmpty(_autoUpdateIntervalMinutesError))
        {
            return;
        }

        _autoUpdateIntervalMinutes = minutes;
        _autoUpdateIntervalMinutesText = text;
        _autoUpdateIntervalMinutesError = string.Empty;
        NotifyAutoUpdateIntervalChanged();
    }

    private void NotifyAutoTestDelayIntervalChanged()
    {
        OnPropertyChanged(nameof(AutoTestDelayIntervalMinutes));
        OnPropertyChanged(nameof(AutoTestDelayIntervalMinutesText));
        OnPropertyChanged(nameof(AutoTestDelayIntervalMinutesError));
        OnPropertyChanged(nameof(IsAutoTestDelayIntervalErrorVisible));
        OnPropertyChanged(nameof(IsAutoTestDelayEnabled));
    }

    private void RefreshAutoTestDelayIntervalDisplay()
    {
        if (!TryParseMinuteInput(_autoTestDelayIntervalMinutesText, out _))
        {
            return;
        }

        var text = FormatAutoTestDelayIntervalText(_autoTestDelayIntervalMinutes);
        if (_autoTestDelayIntervalMinutesText == text)
        {
            return;
        }

        _autoTestDelayIntervalMinutesText = text;
        OnPropertyChanged(nameof(AutoTestDelayIntervalMinutesText));
    }

    private string FormatAutoTestDelayIntervalText(int minutes)
    {
        if (minutes > 0)
        {
            return minutes.ToString(CultureInfo.InvariantCulture);
        }

        return _isAutoTestDelayIntervalEditing ? "0" : Localize("Common.Disable");
    }

    private void NotifyAutoUpdateIntervalChanged()
    {
        OnPropertyChanged(nameof(AutoUpdateIntervalMinutes));
        OnPropertyChanged(nameof(AutoUpdateIntervalMinutesText));
        OnPropertyChanged(nameof(AutoUpdateIntervalMinutesError));
        OnPropertyChanged(nameof(IsAutoUpdateIntervalErrorVisible));
    }

    private bool TryParseMinuteInput(string value, out int minutes)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), Localize("Common.Disable"), StringComparison.Ordinal))
        {
            minutes = 0;
            return true;
        }

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes)
            && minutes >= 0;
    }

    private void ValidateName()
    {
        _nameError = string.IsNullOrWhiteSpace(_name)
            ? Localize("Subscriptions.Validation.NameRequired")
            : string.Empty;
        OnPropertyChanged(nameof(NameError));
        OnPropertyChanged(nameof(IsNameErrorVisible));
    }

    private void ValidateUrl()
    {
        _urlError = IsRemoteContext && !HttpUrlValidator.IsHttpUrl(_url)
            ? Localize("Subscriptions.Validation.Url")
            : string.Empty;
        OnPropertyChanged(nameof(UrlError));
        OnPropertyChanged(nameof(IsUrlErrorVisible));
    }

    private void ValidateAutoTestDelayInterval()
    {
        if (TryParseMinuteInput(_autoTestDelayIntervalMinutesText, out var minutes))
        {
            _autoTestDelayIntervalMinutes = minutes;
            _autoTestDelayIntervalMinutesError = string.Empty;
        }
        else
        {
            _autoTestDelayIntervalMinutesError = Localize("Subscriptions.Validation.Minutes");
        }

        NotifyAutoTestDelayIntervalChanged();
    }

    private void ValidateAutoUpdateInterval()
    {
        if (TryParseMinuteInput(_autoUpdateIntervalMinutesText, out var minutes))
        {
            _autoUpdateIntervalMinutes = minutes;
            _autoUpdateIntervalMinutesError = string.Empty;
        }
        else
        {
            _autoUpdateIntervalMinutesError = Localize("Subscriptions.Validation.Minutes");
        }

        NotifyAutoUpdateIntervalChanged();
    }

    private string FormatUserAgentText()
    {
        if (_isUserAgentEditing)
        {
            return string.IsNullOrWhiteSpace(_userAgent) ? SubscriptionDefaults.UserAgent : _userAgent;
        }

        return IsDefaultUserAgent(_userAgent) ? Localize("Common.Default") : _userAgent;
    }

    // Avalonia 仅在命令事件后重算可执行状态；属性通知需同步转发。
    private void HandlePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CanSubmit))
        {
            _confirmCommand.RaiseCanExecuteChanged();
        }
    }

    private static bool IsDefaultUserAgent(string userAgent)
    {
        return string.IsNullOrWhiteSpace(userAgent)
            || string.Equals(userAgent.Trim(), SubscriptionDefaults.UserAgent, StringComparison.Ordinal);
    }

    private void HandleLanguageChanged(object? sender, EventArgs args)
    {
        RefreshAutoTestDelayIntervalDisplay();
        OnPropertyChanged(nameof(UserAgentText));
        OnPropertyChanged(nameof(AutoUpdateModeOptions));
        OnPropertyChanged(nameof(SelectedAutoUpdateModeOption));
        if (_hasAttemptedSubmit)
        {
            ValidateName();
            ValidateUrl();
            ValidateAutoTestDelayInterval();
            if (IsAutoUpdateIntervalEnabled)
            {
                ValidateAutoUpdateInterval();
            }
        }

        OnLanguageChanged();
    }

    protected string Localize(string key) => Localization?.GetString(key) ?? key;
}
