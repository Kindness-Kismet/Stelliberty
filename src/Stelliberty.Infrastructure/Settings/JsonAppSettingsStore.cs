using System.Text.Json;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Infrastructure.Storage;

namespace Stelliberty.Infrastructure.Settings;

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private readonly string _settingsPath;

    public JsonAppSettingsStore(IPlatformDirectories platformDirectories)
    {
        Directory.CreateDirectory(platformDirectories.AppDataDirectory);
        _settingsPath = platformDirectories.SettingsFilePath;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            var settings = new AppSettings();
            Save(settings);
            return settings;
        }

        // 损坏文件先备份成 .corrupt 再回默认，原配置可救回
        return Normalize(JsonFileRecovery.ReadOrRecover<AppSettings>(_settingsPath) ?? new AppSettings());
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(_settingsPath, json);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var defaults = new AppSettings();
        settings.Language ??= defaults.Language;
        settings.Theme ??= defaults.Theme;
        settings.AccentColorMode ??= defaults.AccentColorMode;
        settings.AccentColor ??= defaults.AccentColor;
        settings.WindowEffect ??= defaults.WindowEffect;
        settings.WindowToggleHotkey ??= defaults.WindowToggleHotkey;
        settings.SystemProxyToggleHotkey ??= defaults.SystemProxyToggleHotkey;
        settings.TunToggleHotkey ??= defaults.TunToggleHotkey;
        settings.AppUpdateCheckInterval ??= defaults.AppUpdateCheckInterval;
        settings.AppUpdateChannel ??= defaults.AppUpdateChannel;
        if (!string.Equals(settings.AppUpdateChannel, "stable", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(settings.AppUpdateChannel, "beta", StringComparison.OrdinalIgnoreCase))
        {
            settings.AppUpdateChannel = defaults.AppUpdateChannel;
        }
        else
        {
            settings.AppUpdateChannel = settings.AppUpdateChannel.Trim().ToLowerInvariant();
        }
        settings.IgnoredUpdateVersion ??= defaults.IgnoredUpdateVersion;
        settings.WebDavUrl ??= defaults.WebDavUrl;
        settings.WebDavUserName ??= defaults.WebDavUserName;
        settings.WebDavPassword ??= defaults.WebDavPassword;
        settings.WebDavRemoteDirectory ??= defaults.WebDavRemoteDirectory;
        if (settings.WebDavBackupIntervalHours <= 0)
        {
            settings.WebDavBackupIntervalHours = defaults.WebDavBackupIntervalHours;
        }

        if (settings.WebDavBackupRetentionCount <= 0)
        {
            settings.WebDavBackupRetentionCount = defaults.WebDavBackupRetentionCount;
        }

        settings.LastCoreVersion ??= defaults.LastCoreVersion;
        settings.DelayTestUrl ??= defaults.DelayTestUrl;
        settings.LanAuthenticationUserName ??= defaults.LanAuthenticationUserName;
        settings.LanAuthenticationPassword ??= defaults.LanAuthenticationPassword;
        settings.LanAllowedIps ??= defaults.LanAllowedIps;
        settings.LanDisallowedIps ??= defaults.LanDisallowedIps;
        settings.SkipAuthPrefixes ??= defaults.SkipAuthPrefixes;
        settings.ExternalControllerAddress ??= defaults.ExternalControllerAddress;
#if DEBUG
        if (settings.ExternalControllerAddress == "127.0.0.1:9090")
        {
            settings.ExternalControllerAddress = defaults.ExternalControllerAddress;
        }
#endif
        settings.ExternalControllerSecret ??= defaults.ExternalControllerSecret;
        settings.ProxyHost ??= defaults.ProxyHost;
        settings.SystemProxyBypass ??= defaults.SystemProxyBypass;
        settings.PacScript ??= defaults.PacScript;
        settings.TunStack ??= defaults.TunStack;
        settings.TunDevice ??= defaults.TunDevice;
        settings.TunDnsHijack ??= defaults.TunDnsHijack;
        settings.TunRouteExcludeAddresses ??= defaults.TunRouteExcludeAddresses;
        settings.DnsListen ??= defaults.DnsListen;
        settings.DnsEnhancedMode ??= defaults.DnsEnhancedMode;
        settings.FakeIpRange ??= defaults.FakeIpRange;
        settings.NameServers ??= defaults.NameServers;
        settings.FallbackNameServers ??= defaults.FallbackNameServers;
        settings.ProxyServerNameServers ??= defaults.ProxyServerNameServers;
        settings.DefaultNameServers ??= defaults.DefaultNameServers;
        settings.FakeIpFilters ??= defaults.FakeIpFilters;
        settings.FallbackFilterGeoIpCode ??= defaults.FallbackFilterGeoIpCode;
        settings.Hosts ??= defaults.Hosts;
        settings.DirectNameServers ??= defaults.DirectNameServers;
        settings.NameServerPolicy ??= defaults.NameServerPolicy;
        settings.FakeIpFilterMode ??= defaults.FakeIpFilterMode;
        settings.FallbackFilterIpCidrs ??= defaults.FallbackFilterIpCidrs;
        settings.FallbackFilterDomains ??= defaults.FallbackFilterDomains;
        settings.GeoDataLoader ??= defaults.GeoDataLoader;
        settings.FindProcessMode ??= defaults.FindProcessMode;
        settings.CoreLogLevel ??= defaults.CoreLogLevel;
        return settings;
    }
}
