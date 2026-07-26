using System.Runtime.CompilerServices;
using System.Threading;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Settings;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SettingsCoreConfigViewModel : ViewModelBase, IDisposable
{
    private const int MinNumericValue = 1;
    private const int MaxNumericValue = 65535;

    private readonly AppSettings _settings;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ILocalizationService _localization;
    private readonly Action<string, string> _requestRuntimeRefresh;
    private readonly Action<bool> _applyTunStateToHome;
    private readonly Action? _systemProxyEndpointChanged;
    private readonly SynchronizationContext? _uiContext;
    private readonly List<string> _changeAreas = [];
    private readonly List<string> _coreLogLevelChangeRequests = [];
    private string _mixedPortText = string.Empty;
    private string _socksPortText = string.Empty;
    private string _httpPortText = string.Empty;
    private string _tunMtuText = string.Empty;
    private string _tcpKeepAliveIntervalText = string.Empty;

    public event EventHandler<(string Message, ToastType Type)>? ToastRequested;

    public SettingsCoreConfigViewModel(
        AppSettings settings,
        IAppSettingsStore settingsStore,
        ILocalizationService localization,
        Action<string, string> requestRuntimeRefresh,
        Action<bool> applyTunStateToHome,
        Action? systemProxyEndpointChanged = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _localization = localization;
        _requestRuntimeRefresh = requestRuntimeRefresh;
        _applyTunStateToHome = applyTunStateToHome;
        _systemProxyEndpointChanged = systemProxyEndpointChanged;
        _uiContext = SynchronizationContext.Current;
        RefreshNumericTextFields();
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<string> ChangeAreas => _changeAreas;

    public IReadOnlyList<string> CoreLogLevelChangeRequests => _coreLogLevelChangeRequests;

    public void RefreshFromSettings()
    {
        RefreshNumericTextFields();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
    }

    private bool SetWithArea<T>(
        T currentValue,
        T nextValue,
        Action<T> assign,
        string changeArea,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, nextValue))
        {
            return false;
        }

        assign(nextValue);
        _settingsStore.Save(_settings);
        RecordChangeArea(changeArea);
        _requestRuntimeRefresh("Core runtime config refreshed", "Core runtime config refresh failed");
        OnPropertyChanged(propertyName);
        return true;
    }

    private bool SetTrimmedStringWithArea(string currentValue, string nextValue, Action<string> assign, string changeArea, [CallerMemberName] string? propertyName = null)
    {
        return SetWithArea(currentValue, nextValue.Trim(), assign, changeArea, propertyName);
    }

    private bool SetLocalWithArea<T>(T currentValue, T nextValue, Action<T> assign, string changeArea, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, nextValue))
        {
            return false;
        }

        assign(nextValue);
        _settingsStore.Save(_settings);
        RecordChangeArea(changeArea);
        OnPropertyChanged(propertyName);
        return true;
    }

    private bool SetIntWithArea(
        int currentValue,
        string text,
        Action<int> assign,
        string changeArea,
        Action<string> assignText,
        [CallerMemberName] string? propertyName = null)
    {
        return SetIntInRangeWithArea(currentValue, text, MinNumericValue, MaxNumericValue, assign, changeArea, assignText, propertyName);
    }

    private bool SetNullableIntWithArea(
        int? currentValue,
        string text,
        string nullText,
        Action<int?> assign,
        string changeArea,
        Action<string> assignText,
        [CallerMemberName] string? propertyName = null)
    {
        return SetNullableIntInRangeWithArea(currentValue, text, nullText, MinNumericValue, MaxNumericValue, assign, changeArea, assignText, propertyName);
    }

    private bool SetIntInRangeWithArea(
        int currentValue,
        string text,
        int min,
        int max,
        Action<int> assign,
        string changeArea,
        Action<string> assignText,
        [CallerMemberName] string? propertyName = null)
    {
        assignText(text);
        if (TryParseIntInRange(text, min, max, out var nextValue))
        {
            return CommitNumericInput(
                text,
                nextValue.ToString(),
                assignText,
                () => SetWithArea(currentValue, nextValue, assign, changeArea, propertyName),
                propertyName);
        }

        RejectNumericInput(currentValue.ToString(), assignText, propertyName);
        return false;
    }

    private bool SetNullableIntInRangeWithArea(
        int? currentValue,
        string text,
        string nullText,
        int min,
        int max,
        Action<int?> assign,
        string changeArea,
        Action<string> assignText,
        [CallerMemberName] string? propertyName = null)
    {
        assignText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return CommitNumericInput(
                text,
                nullText,
                assignText,
                () => SetWithArea(currentValue, null, assign, changeArea, propertyName),
                propertyName);
        }

        if (TryParseIntInRange(text, min, max, out var nextValue))
        {
            return CommitNumericInput(
                text,
                nextValue.ToString(),
                assignText,
                () => SetWithArea(currentValue, nextValue, assign, changeArea, propertyName),
                propertyName);
        }

        RejectNumericInput(currentValue?.ToString() ?? nullText, assignText, propertyName);
        return false;
    }

    private static bool TryParseIntInRange(string text, int min, int max, out int value)
    {
        return int.TryParse(text, out value) && value >= min && value <= max;
    }

    private bool CommitNumericInput(
        string originalText,
        string normalizedText,
        Action<string> assignText,
        Func<bool> commit,
        string? propertyName)
    {
        assignText(normalizedText);
        var changed = commit();
        if (!changed && !string.Equals(originalText, normalizedText, StringComparison.Ordinal))
        {
            RefreshInputText(normalizedText, assignText, propertyName);
        }

        return changed;
    }

    private void RejectNumericInput(string restoredText, Action<string> assignText, string? propertyName)
    {
        RefreshInputText(restoredText, assignText, propertyName);
        ToastRequested?.Invoke(this, (_localization.GetString("Settings.Toast.InvalidNumberRestored"), ToastType.Warning));
    }

    private void RefreshInputText(string text, Action<string> assignText, string? propertyName)
    {
        if (_uiContext is null)
        {
            assignText(text);
            OnPropertyChanged(propertyName);
            return;
        }

        // 延迟绑定会吞同步回写，下一轮 UI 循环强制刷新。
        _uiContext.Post(_ =>
        {
            assignText(text);
            OnPropertyChanged(propertyName);
        }, null);
    }

    private void RefreshNumericTextFields()
    {
        _mixedPortText = _settings.MixedPort.ToString();
        _socksPortText = _settings.SocksPort?.ToString() ?? string.Empty;
        _httpPortText = _settings.HttpPort?.ToString() ?? string.Empty;
        _tunMtuText = CurrentTunMtuText();
        _tcpKeepAliveIntervalText = _settings.TcpKeepAliveInterval.ToString();
    }

    private string CurrentTunMtuText()
    {
        return (_settings.TunMtu ?? AppSettings.DefaultTunMtu).ToString();
    }

    private bool SetStringListWithArea(IReadOnlyList<string> currentValue, string text, Action<IReadOnlyList<string>> assign, string changeArea, [CallerMemberName] string? propertyName = null)
    {
        var nextValue = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (currentValue.SequenceEqual(nextValue))
        {
            return false;
        }

        assign(nextValue);
        _settingsStore.Save(_settings);
        RecordChangeArea(changeArea);
        _requestRuntimeRefresh("Core runtime config refreshed", "Core runtime config refresh failed");
        OnPropertyChanged(propertyName);
        return true;
    }

    private void RecordChangeArea(string area)
    {
        if (!_changeAreas.Contains(area, StringComparer.Ordinal))
        {
            _changeAreas.Add(area);
            OnPropertyChanged(nameof(ChangeAreas));
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(NetworkDelaySectionText));
        OnPropertyChanged(nameof(NetworkLanSectionText));
        OnPropertyChanged(nameof(NetworkConnectionSectionText));
        OnPropertyChanged(nameof(NetworkUnifiedDelayText));
        OnPropertyChanged(nameof(NetworkDelayUrlText));
        OnPropertyChanged(nameof(NetworkAllowLanText));
        OnPropertyChanged(nameof(NetworkLanAuthText));
        OnPropertyChanged(nameof(NetworkLanAuthUserNameText));
        OnPropertyChanged(nameof(NetworkLanAuthPasswordText));
        OnPropertyChanged(nameof(NetworkIpv6Text));
        OnPropertyChanged(nameof(NetworkTcpConcurrentText));
        OnPropertyChanged(nameof(NetworkLanAllowedIpsText));
        OnPropertyChanged(nameof(NetworkLanDisallowedIpsText));
        OnPropertyChanged(nameof(NetworkSkipAuthPrefixesText));
        OnPropertyChanged(nameof(NetworkItems));
        OnPropertyChanged(nameof(PortProxySectionText));
        OnPropertyChanged(nameof(PortControllerSectionText));
        OnPropertyChanged(nameof(PortMixedText));
        OnPropertyChanged(nameof(PortSocksText));
        OnPropertyChanged(nameof(PortHttpText));
        OnPropertyChanged(nameof(PortControllerAddressText));
        OnPropertyChanged(nameof(PortControllerSecretText));
        OnPropertyChanged(nameof(PortControllerEnabledText));
        OnPropertyChanged(nameof(PortControlItems));
        OnPropertyChanged(nameof(SystemTunText));
        OnPropertyChanged(nameof(SystemTunStackText));
        OnPropertyChanged(nameof(SystemTunDeviceText));
        OnPropertyChanged(nameof(SystemTunAutoRouteText));
        OnPropertyChanged(nameof(SystemTunAutoRedirectText));
        OnPropertyChanged(nameof(SystemTunAutoDetectInterfaceText));
        OnPropertyChanged(nameof(SystemTunStrictRouteText));
        OnPropertyChanged(nameof(SystemTunDnsHijackText));
        OnPropertyChanged(nameof(SystemTunRouteExcludeText));
        OnPropertyChanged(nameof(SystemTunDisableIcmpForwardingText));
        OnPropertyChanged(nameof(SystemTunMtuText));
        OnPropertyChanged(nameof(SystemTunMtuDescriptionText));
        OnPropertyChanged(nameof(DnsOverrideText));
        OnPropertyChanged(nameof(DnsEnableText));
        OnPropertyChanged(nameof(DnsListenText));
        OnPropertyChanged(nameof(DnsModeText));
        OnPropertyChanged(nameof(DnsNameserverText));
        OnPropertyChanged(nameof(DnsFallbackText));
        OnPropertyChanged(nameof(DnsFakeIpText));
        OnPropertyChanged(nameof(DnsRespectRulesText));
        OnPropertyChanged(nameof(DnsProxyServerNameserverText));
        OnPropertyChanged(nameof(DnsDefaultNameserverText));
        OnPropertyChanged(nameof(DnsFakeIpFilterText));
        OnPropertyChanged(nameof(DnsFallbackFilterGeoIpCodeText));
        OnPropertyChanged(nameof(DnsHostsText));
        OnPropertyChanged(nameof(DnsIpv6Text));
        OnPropertyChanged(nameof(DnsUseHostsText));
        OnPropertyChanged(nameof(DnsUseSystemHostsText));
        OnPropertyChanged(nameof(DnsDirectNameserverText));
        OnPropertyChanged(nameof(DnsNameServerPolicyText));
        OnPropertyChanged(nameof(DnsPreferH3Text));
        OnPropertyChanged(nameof(DnsFakeIpFilterModeText));
        OnPropertyChanged(nameof(DnsDirectNameServerFollowPolicyText));
        OnPropertyChanged(nameof(DnsFallbackFilterGeoIpText));
        OnPropertyChanged(nameof(DnsFallbackFilterIpCidrText));
        OnPropertyChanged(nameof(DnsFallbackFilterDomainText));
        OnPropertyChanged(nameof(DnsItems));
        OnPropertyChanged(nameof(DnsEnhancedModeOptions));
        OnPropertyChanged(nameof(SelectedDnsEnhancedModeOption));
        OnPropertyChanged(nameof(FakeIpFilterModeOptions));
        OnPropertyChanged(nameof(SelectedFakeIpFilterModeOption));
        OnPropertyChanged(nameof(PerformanceGeoLoaderText));
        OnPropertyChanged(nameof(PerformanceFindProcessText));
        OnPropertyChanged(nameof(PerformanceKeepAliveText));
        OnPropertyChanged(nameof(PerformanceKeepAliveIntervalText));
        OnPropertyChanged(nameof(PerformanceItems));
        OnPropertyChanged(nameof(GeoDataLoaderOptions));
        OnPropertyChanged(nameof(SelectedGeoDataLoaderOption));
        OnPropertyChanged(nameof(FindProcessModeOptions));
        OnPropertyChanged(nameof(SelectedFindProcessModeOption));
        OnPropertyChanged(nameof(CoreLogLevelText));
        OnPropertyChanged(nameof(CoreLogItems));
        OnPropertyChanged(nameof(CoreLogLevelOptions));
        OnPropertyChanged(nameof(SelectedCoreLogLevelOption));
    }
}
