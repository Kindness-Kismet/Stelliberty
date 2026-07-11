namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SettingsCoreConfigViewModel
{
    public string PortProxySectionText => _localization.GetString("Settings.Port.Section.Proxy");
    public string PortControllerSectionText => _localization.GetString("Settings.Port.Section.Controller");
    public string PortMixedText => _localization.GetString("Settings.Port.Mixed");
    public string PortSocksText => _localization.GetString("Settings.Port.Socks");
    public string PortHttpText => _localization.GetString("Settings.Port.Http");
    public string PortControllerAddressText => _localization.GetString("Settings.Port.ControllerAddress");
    public string PortControllerSecretText => _localization.GetString("Settings.Port.ControllerSecret");
    public string PortControllerEnabledText => _localization.GetString("Settings.Port.ControllerEnabled");

    public IReadOnlyList<string> PortControlItems =>
    [
        PortMixedText,
        PortSocksText,
        PortHttpText,
        PortControllerAddressText,
        PortControllerSecretText
    ];

    public string MixedPortText
    {
        get => _mixedPortText;
        set => SetIntWithArea(
            _settings.MixedPort,
            value,
            next =>
            {
                _settings.MixedPort = next;
                _systemProxyEndpointChanged?.Invoke();
            },
            "PortControl",
            next => _mixedPortText = next);
    }

    public string SocksPortText
    {
        get => _socksPortText;
        set => SetNullableIntWithArea(_settings.SocksPort, value, string.Empty, next => _settings.SocksPort = next, "PortControl", next => _socksPortText = next);
    }

    public string HttpPortText
    {
        get => _httpPortText;
        set => SetNullableIntWithArea(_settings.HttpPort, value, string.Empty, next => _settings.HttpPort = next, "PortControl", next => _httpPortText = next);
    }

    public bool IsExternalControllerEnabled
    {
        get => _settings.IsExternalControllerEnabled;
        set => SetWithArea(_settings.IsExternalControllerEnabled, value, next => _settings.IsExternalControllerEnabled = next, "PortControl");
    }

    public string ExternalControllerAddress
    {
        get => _settings.ExternalControllerAddress;
        set => SetTrimmedStringWithArea(_settings.ExternalControllerAddress, value, next => _settings.ExternalControllerAddress = next, "PortControl");
    }

    public string ExternalControllerSecret
    {
        get => _settings.ExternalControllerSecret;
        set => SetTrimmedStringWithArea(_settings.ExternalControllerSecret, value, next => _settings.ExternalControllerSecret = next, "PortControl");
    }
}
