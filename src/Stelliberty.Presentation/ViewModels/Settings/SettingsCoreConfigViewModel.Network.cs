namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SettingsCoreConfigViewModel
{
    public string NetworkDelaySectionText => _localization.GetString("Settings.Network.Section.Delay");
    public string NetworkLanSectionText => _localization.GetString("Settings.Network.Section.Lan");
    public string NetworkConnectionSectionText => _localization.GetString("Settings.Network.Section.Connection");
    public string NetworkUnifiedDelayText => _localization.GetString("Settings.Network.UnifiedDelay");
    public string NetworkDelayUrlText => _localization.GetString("Settings.Network.DelayUrl");
    public string NetworkAllowLanText => _localization.GetString("Settings.Network.AllowLan");
    public string NetworkLanAuthText => _localization.GetString("Settings.Network.LanAuth");
    public string NetworkLanAuthUserNameText => _localization.GetString("Settings.Network.LanAuthUserName");
    public string NetworkLanAuthPasswordText => _localization.GetString("Settings.Network.LanAuthPassword");
    public string NetworkIpv6Text => _localization.GetString("Settings.Network.Ipv6");
    public string NetworkTcpConcurrentText => _localization.GetString("Settings.Network.TcpConcurrent");
    public string NetworkLanAllowedIpsText => _localization.GetString("Settings.Network.LanAllowedIps");
    public string NetworkLanDisallowedIpsText => _localization.GetString("Settings.Network.LanDisallowedIps");
    public string NetworkSkipAuthPrefixesText => _localization.GetString("Settings.Network.SkipAuthPrefixes");

    public IReadOnlyList<string> NetworkItems =>
    [
        NetworkUnifiedDelayText,
        NetworkDelayUrlText,
        NetworkLanAuthText,
        NetworkLanAllowedIpsText,
        NetworkLanDisallowedIpsText,
        NetworkSkipAuthPrefixesText,
        NetworkIpv6Text,
        NetworkTcpConcurrentText
    ];

    public bool IsUnifiedDelayEnabled
    {
        get => _settings.IsUnifiedDelayEnabled;
        set => SetWithArea(_settings.IsUnifiedDelayEnabled, value, next => _settings.IsUnifiedDelayEnabled = next, "Network");
    }

    public string DelayTestUrl
    {
        get => _settings.DelayTestUrl;
        set => SetDelayTestUrl(value);
    }

    public bool IsAllowLanEnabled
    {
        get => _settings.IsAllowLanEnabled;
        set => SetWithArea(_settings.IsAllowLanEnabled, value, next => _settings.IsAllowLanEnabled = next, "Network");
    }

    public string LanAuthenticationUserName
    {
        get => _settings.LanAuthenticationUserName;
        set => SetWithArea(_settings.LanAuthenticationUserName, value, next => _settings.LanAuthenticationUserName = next, "Network");
    }

    public string LanAuthenticationPassword
    {
        get => _settings.LanAuthenticationPassword;
        set => SetWithArea(_settings.LanAuthenticationPassword, value, next => _settings.LanAuthenticationPassword = next, "Network");
    }

    public string LanAllowedIpsText
    {
        get => string.Join(Environment.NewLine, _settings.LanAllowedIps);
        set => SetStringListWithArea(_settings.LanAllowedIps, value, next => _settings.LanAllowedIps = next, "Network");
    }

    public string LanDisallowedIpsText
    {
        get => string.Join(Environment.NewLine, _settings.LanDisallowedIps);
        set => SetStringListWithArea(_settings.LanDisallowedIps, value, next => _settings.LanDisallowedIps = next, "Network");
    }

    public string SkipAuthPrefixesText
    {
        get => string.Join(Environment.NewLine, _settings.SkipAuthPrefixes);
        set => SetStringListWithArea(_settings.SkipAuthPrefixes, value, next => _settings.SkipAuthPrefixes = next, "Network");
    }

    public bool IsIpv6Enabled
    {
        get => _settings.IsIpv6Enabled;
        set => SetWithArea(_settings.IsIpv6Enabled, value, next => _settings.IsIpv6Enabled = next, "Network");
    }

    public bool IsTcpConcurrentEnabled
    {
        get => _settings.IsTcpConcurrentEnabled;
        set => SetWithArea(_settings.IsTcpConcurrentEnabled, value, next => _settings.IsTcpConcurrentEnabled = next, "Network");
    }

    private void SetDelayTestUrl(string value)
    {
        var normalizedValue = value.Trim();
        if (!Uri.TryCreate(normalizedValue, UriKind.Absolute, out var uri) || !IsHttpDelayTestUri(uri))
        {
            return;
        }

        SetLocalWithArea(_settings.DelayTestUrl, normalizedValue, next => _settings.DelayTestUrl = next, "Network");
    }

    private static bool IsHttpDelayTestUri(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
