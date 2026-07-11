namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SettingsCoreConfigViewModel
{
    public string DnsOverrideText => _localization.GetString("Settings.Dns.Override");
    public string DnsEnableText => _localization.GetString("Settings.Dns.Enable");
    public string DnsListenText => _localization.GetString("Settings.Dns.Listen");
    public string DnsModeText => _localization.GetString("Settings.Dns.Mode");
    public string DnsNameserverText => _localization.GetString("Settings.Dns.Nameserver");
    public string DnsFallbackText => _localization.GetString("Settings.Dns.Fallback");
    public string DnsFakeIpText => _localization.GetString("Settings.Dns.FakeIp");
    public string DnsRespectRulesText => _localization.GetString("Settings.Dns.RespectRules");
    public string DnsProxyServerNameserverText => _localization.GetString("Settings.Dns.ProxyServerNameserver");
    public string DnsDefaultNameserverText => _localization.GetString("Settings.Dns.DefaultNameserver");
    public string DnsFakeIpFilterText => _localization.GetString("Settings.Dns.FakeIpFilter");
    public string DnsFallbackFilterGeoIpCodeText => _localization.GetString("Settings.Dns.FallbackFilterGeoIpCode");
    public string DnsHostsText => _localization.GetString("Settings.Dns.Hosts");
    public string DnsIpv6Text => _localization.GetString("Settings.Dns.Ipv6");
    public string DnsUseHostsText => _localization.GetString("Settings.Dns.UseHosts");
    public string DnsUseSystemHostsText => _localization.GetString("Settings.Dns.UseSystemHosts");
    public string DnsDirectNameserverText => _localization.GetString("Settings.Dns.DirectNameserver");
    public string DnsNameServerPolicyText => _localization.GetString("Settings.Dns.NameServerPolicy");
    public string DnsPreferH3Text => _localization.GetString("Settings.Dns.PreferH3");
    public string DnsFakeIpFilterModeText => _localization.GetString("Settings.Dns.FakeIpFilterMode");
    public string DnsDirectNameServerFollowPolicyText => _localization.GetString("Settings.Dns.DirectNameServerFollowPolicy");
    public string DnsFallbackFilterGeoIpText => _localization.GetString("Settings.Dns.FallbackFilterGeoIp");
    public string DnsFallbackFilterIpCidrText => _localization.GetString("Settings.Dns.FallbackFilterIpCidr");
    public string DnsFallbackFilterDomainText => _localization.GetString("Settings.Dns.FallbackFilterDomain");

    public IReadOnlyList<string> DnsItems =>
    [
        DnsEnableText,
        DnsListenText,
        DnsModeText,
        DnsNameserverText,
        DnsFallbackText,
        DnsFakeIpText,
        DnsDefaultNameserverText,
        DnsFakeIpFilterText,
        DnsFallbackFilterGeoIpCodeText,
        DnsHostsText,
        DnsIpv6Text,
        DnsUseHostsText,
        DnsUseSystemHostsText,
        DnsDirectNameserverText,
        DnsNameServerPolicyText,
        DnsPreferH3Text,
        DnsFakeIpFilterModeText,
        DnsDirectNameServerFollowPolicyText,
        DnsFallbackFilterGeoIpText,
        DnsFallbackFilterIpCidrText,
        DnsFallbackFilterDomainText
    ];

    public bool IsDnsOverrideEnabled
    {
        get => _settings.IsDnsOverrideEnabled;
        set => SetWithArea(_settings.IsDnsOverrideEnabled, value, next => _settings.IsDnsOverrideEnabled = next, "Dns");
    }

    public bool IsDnsEnabled
    {
        get => _settings.IsDnsEnabled;
        set => SetWithArea(_settings.IsDnsEnabled, value, next => _settings.IsDnsEnabled = next, "Dns");
    }

    public string DnsListen
    {
        get => _settings.DnsListen;
        set => SetTrimmedStringWithArea(_settings.DnsListen, value, next => _settings.DnsListen = next, "Dns");
    }

    public string DnsEnhancedMode
    {
        get => _settings.DnsEnhancedMode;
        set => SetWithArea(_settings.DnsEnhancedMode, value, next => _settings.DnsEnhancedMode = next, "Dns");
    }

    public IReadOnlyList<SelectionOption<string>> DnsEnhancedModeOptions =>
    [
        new("normal", _localization.GetString("Settings.Dns.Mode.Normal")),
        new("fake-ip", _localization.GetString("Settings.Dns.Mode.FakeIp")),
        new("redir-host", _localization.GetString("Settings.Dns.Mode.RedirHost")),
        new("hosts", _localization.GetString("Settings.Dns.Mode.Hosts"))
    ];

    public SelectionOption<string> SelectedDnsEnhancedModeOption
    {
        get => DnsEnhancedModeOptions.FirstOrDefault(option => option.Value == _settings.DnsEnhancedMode)
            ?? DnsEnhancedModeOptions[1];
        set => DnsEnhancedMode = value.Value;
    }

    public string FakeIpRange
    {
        get => _settings.FakeIpRange;
        set => SetTrimmedStringWithArea(_settings.FakeIpRange, value, next => _settings.FakeIpRange = next, "Dns");
    }

    public bool IsDnsRespectRulesEnabled
    {
        get => _settings.IsDnsRespectRulesEnabled;
        set => SetWithArea(_settings.IsDnsRespectRulesEnabled, value, next => _settings.IsDnsRespectRulesEnabled = next, "Dns");
    }

    public string NameServersText
    {
        get => string.Join(Environment.NewLine, _settings.NameServers);
        set => SetStringListWithArea(_settings.NameServers, value, next => _settings.NameServers = next, "Dns");
    }

    public string FallbackNameServersText
    {
        get => string.Join(Environment.NewLine, _settings.FallbackNameServers);
        set => SetStringListWithArea(_settings.FallbackNameServers, value, next => _settings.FallbackNameServers = next, "Dns");
    }

    public string ProxyServerNameServersText
    {
        get => string.Join(Environment.NewLine, _settings.ProxyServerNameServers);
        set => SetStringListWithArea(_settings.ProxyServerNameServers, value, next => _settings.ProxyServerNameServers = next, "Dns");
    }

    public string DefaultNameServersText
    {
        get => string.Join(Environment.NewLine, _settings.DefaultNameServers);
        set => SetStringListWithArea(_settings.DefaultNameServers, value, next => _settings.DefaultNameServers = next, "Dns");
    }

    public string FakeIpFiltersText
    {
        get => string.Join(Environment.NewLine, _settings.FakeIpFilters);
        set => SetStringListWithArea(_settings.FakeIpFilters, value, next => _settings.FakeIpFilters = next, "Dns");
    }

    public string FallbackFilterGeoIpCode
    {
        get => _settings.FallbackFilterGeoIpCode;
        set => SetTrimmedStringWithArea(_settings.FallbackFilterGeoIpCode, value, next => _settings.FallbackFilterGeoIpCode = next, "Dns");
    }

    public string HostsText
    {
        get => string.Join(Environment.NewLine, _settings.Hosts);
        set => SetStringListWithArea(_settings.Hosts, value, next => _settings.Hosts = next, "Dns");
    }

    public bool IsDnsIpv6Enabled
    {
        get => _settings.IsDnsIpv6Enabled;
        set => SetWithArea(_settings.IsDnsIpv6Enabled, value, next => _settings.IsDnsIpv6Enabled = next, "Dns");
    }

    public bool IsDnsUseHostsEnabled
    {
        get => _settings.IsDnsUseHostsEnabled;
        set => SetWithArea(_settings.IsDnsUseHostsEnabled, value, next => _settings.IsDnsUseHostsEnabled = next, "Dns");
    }

    public bool IsDnsUseSystemHostsEnabled
    {
        get => _settings.IsDnsUseSystemHostsEnabled;
        set => SetWithArea(_settings.IsDnsUseSystemHostsEnabled, value, next => _settings.IsDnsUseSystemHostsEnabled = next, "Dns");
    }

    public string DirectNameServersText
    {
        get => string.Join(Environment.NewLine, _settings.DirectNameServers);
        set => SetStringListWithArea(_settings.DirectNameServers, value, next => _settings.DirectNameServers = next, "Dns");
    }

    public string NameServerPolicyText
    {
        get => string.Join(Environment.NewLine, _settings.NameServerPolicy);
        set => SetStringListWithArea(_settings.NameServerPolicy, value, next => _settings.NameServerPolicy = next, "Dns");
    }

    public bool IsDnsPreferH3Enabled
    {
        get => _settings.IsDnsPreferH3Enabled;
        set => SetWithArea(_settings.IsDnsPreferH3Enabled, value, next => _settings.IsDnsPreferH3Enabled = next, "Dns");
    }

    public string FakeIpFilterMode
    {
        get => _settings.FakeIpFilterMode;
        set => SetWithArea(_settings.FakeIpFilterMode, value, next => _settings.FakeIpFilterMode = next, "Dns");
    }

    public IReadOnlyList<SelectionOption<string>> FakeIpFilterModeOptions =>
    [
        new("blacklist", _localization.GetString("Settings.Dns.FakeIpFilterMode.Blacklist")),
        new("whitelist", _localization.GetString("Settings.Dns.FakeIpFilterMode.Whitelist"))
    ];

    public SelectionOption<string> SelectedFakeIpFilterModeOption
    {
        get => FakeIpFilterModeOptions.FirstOrDefault(option => option.Value == _settings.FakeIpFilterMode)
            ?? FakeIpFilterModeOptions[0];
        set => FakeIpFilterMode = value.Value;
    }

    public bool IsDirectNameServerFollowPolicyEnabled
    {
        get => _settings.IsDirectNameServerFollowPolicyEnabled;
        set => SetWithArea(_settings.IsDirectNameServerFollowPolicyEnabled, value, next => _settings.IsDirectNameServerFollowPolicyEnabled = next, "Dns");
    }

    public bool IsFallbackFilterGeoIpEnabled
    {
        get => _settings.IsFallbackFilterGeoIpEnabled;
        set => SetWithArea(_settings.IsFallbackFilterGeoIpEnabled, value, next => _settings.IsFallbackFilterGeoIpEnabled = next, "Dns");
    }

    public string FallbackFilterIpCidrsText
    {
        get => string.Join(Environment.NewLine, _settings.FallbackFilterIpCidrs);
        set => SetStringListWithArea(_settings.FallbackFilterIpCidrs, value, next => _settings.FallbackFilterIpCidrs = next, "Dns");
    }

    public string FallbackFilterDomainsText
    {
        get => string.Join(Environment.NewLine, _settings.FallbackFilterDomains);
        set => SetStringListWithArea(_settings.FallbackFilterDomains, value, next => _settings.FallbackFilterDomains = next, "Dns");
    }
}
