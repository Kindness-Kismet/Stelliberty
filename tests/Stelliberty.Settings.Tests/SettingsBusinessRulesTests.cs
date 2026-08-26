using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Updates;
using Xunit;

namespace Stelliberty.Settings.Tests;

public sealed class SettingsBusinessRulesTests
{
    [Fact(DisplayName = "App settings normalizer revokes TUN when current host has no permission")]
    public void AppSettingsNormalizerRevokesTunWhenCurrentHostHasNoPermission()
    {
        var settings = new AppSettings { IsTunEnabled = true };

        var changed = AppSettingsNormalizer.RevokeTunIfUnavailable(
            settings,
            ProcessRunMode.Normal,
            hasServiceTunHost: false);

        Assert.True(changed);
        Assert.False(settings.IsTunEnabled);
        Assert.False(AppSettingsNormalizer.EffectiveTunEnabled(
            settings,
            ProcessRunMode.Normal,
            hasServiceTunHost: false));
    }

    [Fact(DisplayName = "App settings normalizer keeps TUN for administrator host")]
    public void AppSettingsNormalizerKeepsTunForAdministratorHost()
    {
        var settings = new AppSettings { IsTunEnabled = true };

        var changed = AppSettingsNormalizer.RevokeTunIfUnavailable(
            settings,
            ProcessRunMode.Administrator,
            hasServiceTunHost: false);

        Assert.False(changed);
        Assert.True(settings.IsTunEnabled);
        Assert.True(AppSettingsNormalizer.EffectiveTunEnabled(
            settings,
            ProcessRunMode.Administrator,
            hasServiceTunHost: false));
    }

    [Fact(DisplayName = "App settings normalizer keeps TUN for service core host")]
    public void AppSettingsNormalizerKeepsTunForServiceCoreHost()
    {
        var settings = new AppSettings { IsTunEnabled = true };

        var changed = AppSettingsNormalizer.RevokeTunIfUnavailable(
            settings,
            ProcessRunMode.Normal,
            hasServiceTunHost: true);

        Assert.False(changed);
        Assert.True(settings.IsTunEnabled);
        Assert.True(AppSettingsNormalizer.EffectiveTunEnabled(
            settings,
            ProcessRunMode.Normal,
            hasServiceTunHost: true));
    }

    [Fact(DisplayName = "System proxy request builds platform bypass rules and PAC script")]
    public void SystemProxyRequestBuildsPlatformBypassRulesAndPacScript()
    {
        var settings = new AppSettings
        {
            ProxyHost = "0.0.0.0",
            MixedPort = 7890,
            SystemProxyBypass = " localhost ; 127.* ; ; <local> ",
            IsPacModeEnabled = true,
            PacScript = "return \"PROXY ${getProxyHost()}:${ClashDefaults.httpPort}; DIRECT\";"
        };

        var windows = SystemProxyApplicationRequest.Build(settings, SystemProxyPlatform.Windows);
        settings.SystemProxyBypass = " localhost, 127.0.0.1,,*.local ";
        var linux = SystemProxyApplicationRequest.Build(settings, SystemProxyPlatform.Linux);

        Assert.Equal(["localhost", "127.*", "<local>"], windows.BypassRules);
        Assert.Equal(["localhost", "127.0.0.1", "*.local"], linux.BypassRules);
        Assert.True(windows.IsPacModeEnabled);
        Assert.Equal("return \"PROXY 0.0.0.0:7890; DIRECT\";", windows.PacScript);
        Assert.Equal("0.0.0.0", windows.Host);
        Assert.Equal(7890, windows.Port);
    }

    [Fact(DisplayName = "System proxy PAC request keeps hardcoded script ports")]
    public void SystemProxyPacRequestKeepsHardcodedScriptPorts()
    {
        const string customScript = """
            function FindProxyForURL(url, host) {
                return "PROXY 127.0.0.1:2000; SOCKS5 127.0.0.1:2000; DIRECT";
            }
            """;
        var settings = new AppSettings
        {
            ProxyHost = "127.0.0.1",
            MixedPort = 2001,
            IsPacModeEnabled = true,
            PacScript = customScript
        };

        var request = SystemProxyApplicationRequest.Build(settings, SystemProxyPlatform.Windows);

        Assert.Equal(customScript, request.PacScript);
    }

    [Fact(DisplayName = "System proxy PAC request replaces placeholders with latest endpoint")]
    public void SystemProxyPacRequestReplacesPlaceholdersWithLatestEndpoint()
    {
        var settings = new AppSettings
        {
            ProxyHost = "192.168.1.10",
            MixedPort = 2001,
            IsPacModeEnabled = true,
            PacScript = "return \"PROXY ${getProxyHost()}:${ClashDefaults.httpPort}; DIRECT\";"
        };

        var request = SystemProxyApplicationRequest.Build(settings, SystemProxyPlatform.Windows);

        Assert.Equal("return \"PROXY 192.168.1.10:2001; DIRECT\";", request.PacScript);
    }

    [Fact(DisplayName = "App update release selector separates stable and beta channels")]
    public void AppUpdateReleaseSelectorSeparatesStableAndBetaChannels()
    {
        var releases = new[]
        {
            new AppUpdateReleaseInfo("v2.0.0", "https://example.test/v2.0.0", IsPreRelease: false),
            new AppUpdateReleaseInfo("v2.0.1-beta1", "https://example.test/v2.0.1-beta1", IsPreRelease: true),
            new AppUpdateReleaseInfo("v2.0.1-beta2", "https://example.test/v2.0.1-beta2", IsPreRelease: true),
            new AppUpdateReleaseInfo("v2.0.1", "https://example.test/v2.0.1", IsPreRelease: false),
            new AppUpdateReleaseInfo("v2.0.2-beta1", "https://example.test/v2.0.2-beta1", IsPreRelease: true),
            new AppUpdateReleaseInfo("v2.0.3-beta1", "https://example.test/v2.0.3-beta1", IsPreRelease: false),
            new AppUpdateReleaseInfo("draft", "https://example.test/draft", IsPreRelease: false, IsDraft: true),
        };

        Assert.True(AppVersionComparer.IsNewer("2.0.1-beta2", "2.0.1-beta1"));
        Assert.True(AppVersionComparer.IsNewer("2.0.1", "2.0.1-beta2"));
        Assert.False(AppVersionComparer.IsNewer("2.0.1-beta1", "2.0.1"));

        Assert.Equal("v2.0.1", AppUpdateReleaseSelector.Select(releases, "stable", "2.0.0")?.Version);
        Assert.Equal("v2.0.3-beta1", AppUpdateReleaseSelector.Select(releases, "beta", "2.0.0")?.Version);
        Assert.Null(AppUpdateReleaseSelector.Select(releases, "stable", "2.0.1"));
        Assert.Null(AppUpdateReleaseSelector.Select(releases, "beta", "2.0.3-beta1"));
    }
}
