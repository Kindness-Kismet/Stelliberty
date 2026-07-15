using System.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Updates;
using Stelliberty.Presentation.ViewModels;
using Xunit;

namespace Stelliberty.Settings.Tests;

public sealed class SettingsPageBusinessTests
{
    [Fact(DisplayName = "App settings normalizer revokes TUN when current host has no permission")]
    public void AppSettingsNormalizerRevokesTunWhenCurrentHostHasNoPermission()
    {
        var settings = new AppSettings { IsTunEnabled = true };

        var changed = AppSettingsNormalizer.RevokeTunIfUnavailable(settings, ProcessRunMode.Normal, hasServiceTunHost: false);

        Assert.True(changed);
        Assert.False(settings.IsTunEnabled);
        Assert.False(AppSettingsNormalizer.EffectiveTunEnabled(settings, ProcessRunMode.Normal, hasServiceTunHost: false));
    }

    [Fact(DisplayName = "App settings normalizer keeps TUN for administrator host")]
    public void AppSettingsNormalizerKeepsTunForAdministratorHost()
    {
        var settings = new AppSettings { IsTunEnabled = true };

        var changed = AppSettingsNormalizer.RevokeTunIfUnavailable(settings, ProcessRunMode.Administrator, hasServiceTunHost: false);

        Assert.False(changed);
        Assert.True(settings.IsTunEnabled);
        Assert.True(AppSettingsNormalizer.EffectiveTunEnabled(settings, ProcessRunMode.Administrator, hasServiceTunHost: false));
    }

    [Fact(DisplayName = "App settings normalizer keeps TUN for service core host")]
    public void AppSettingsNormalizerKeepsTunForServiceCoreHost()
    {
        var settings = new AppSettings { IsTunEnabled = true };

        var changed = AppSettingsNormalizer.RevokeTunIfUnavailable(settings, ProcessRunMode.Normal, hasServiceTunHost: true);
        var effective = AppSettingsNormalizer.EffectiveTunEnabled(settings, ProcessRunMode.Normal, hasServiceTunHost: true);

        Assert.False(changed);
        Assert.True(effective);
        Assert.True(settings.IsTunEnabled);
    }

    [Fact(DisplayName = "app behavior saves and applies platform request")]
    public void AppBehaviorSavesAndAppliesPlatformRequest()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var service = new FakeAppBehaviorService();
        var viewModel = new SettingsAppBehaviorViewModel(settings, store, new FakeLocalizationService(), service);

        viewModel.IsLazyModeEnabled = true;
        viewModel.SetAutoStartEnabled(true);

        Assert.Equal(2, store.SaveCount);
        Assert.Equal(1, service.ApplyCount);
        Assert.True(service.LastRequest?.IsLazyModeEnabled);
        Assert.True(service.LastRequest?.IsAutoStartEnabled);
    }

    [Fact(DisplayName = "app behavior rolls back when platform request fails")]
    public void AppBehaviorRollsBackWhenPlatformRequestFails()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var service = new FakeAppBehaviorService { ShouldFail = true };
        var viewModel = new SettingsAppBehaviorViewModel(settings, store, new FakeLocalizationService(), service);

        viewModel.SetAutoStartEnabled(true);

        Assert.False(settings.IsAutoStartEnabled);
        Assert.False(viewModel.IsAutoStartEnabled);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(1, service.ApplyCount);
    }

    [Fact(DisplayName = "App behavior keeps autostart enabled when disabling is denied")]
    public void AppBehaviorKeepsAutoStartEnabledWhenDisablingIsDenied()
    {
        var settings = new AppSettings { IsAutoStartEnabled = true };
        var store = new FakeSettingsStore(settings);
        var service = new FakeAppBehaviorService { ShouldFail = true };
        var viewModel = new SettingsAppBehaviorViewModel(settings, store, new FakeLocalizationService(), service);

        viewModel.SetAutoStartEnabled(false);

        Assert.True(settings.IsAutoStartEnabled);
        Assert.True(viewModel.IsAutoStartEnabled);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(1, service.ApplyCount);
    }

    [Fact(DisplayName = "App behavior refresh does not recreate platform startup entry")]
    public void AppBehaviorRefreshDoesNotRecreatePlatformStartupEntry()
    {
        var settings = new AppSettings { IsAutoStartEnabled = true };
        var store = new FakeSettingsStore(settings);
        var service = new FakeAppBehaviorService();
        var viewModel = new SettingsAppBehaviorViewModel(settings, store, new FakeLocalizationService(), service);

        viewModel.RefreshFromSettings();

        Assert.Equal(0, service.ApplyCount);
        Assert.Equal(0, store.SaveCount);
        Assert.True(viewModel.IsAutoStartEnabled);
    }

    [Fact(DisplayName = "Changing silent start does not reconfigure platform startup")]
    public void ChangingSilentStartDoesNotReconfigurePlatformStartup()
    {
        var settings = new AppSettings { IsAutoStartEnabled = true };
        var store = new FakeSettingsStore(settings);
        var service = new FakeAppBehaviorService();
        var viewModel = new SettingsAppBehaviorViewModel(settings, store, new FakeLocalizationService(), service);

        viewModel.IsSilentStartEnabled = true;

        Assert.Equal(0, service.ApplyCount);
        Assert.True(settings.IsSilentStartEnabled);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact(DisplayName = "Core config port changes request runtime refresh")]
    public void CoreConfigPortChangesRequestRuntimeRefresh()
    {
        var settings = new AppSettings { ExternalControllerAddress = "127.0.0.1:9090", ExternalControllerSecret = "<external-controller-secret-old>" };
        var store = new FakeSettingsStore(settings);
        var refreshRequests = new List<string>();
        var viewModel = new SettingsCoreConfigViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            (success, _) => refreshRequests.Add(success),
            _ => { });

        viewModel.MixedPortText = "7890";
        viewModel.ExternalControllerAddress = " 127.0.0.1:9091 ";
        viewModel.ExternalControllerSecret = " <external-controller-secret> ";

        Assert.Equal(7890, settings.MixedPort);
        Assert.Equal("127.0.0.1:9091", settings.ExternalControllerAddress);
        Assert.Equal("<external-controller-secret>", settings.ExternalControllerSecret);
        Assert.Equal(3, store.SaveCount);
        Assert.Equal(3, refreshRequests.Count);
        Assert.Equal(["PortControl"], viewModel.ChangeAreas);
    }

    [Fact(DisplayName = "Core config optional ports clear on blank and ignore invalid text")]
    public void CoreConfigOptionalPortsClearOnBlankAndIgnoreInvalidText()
    {
        var settings = new AppSettings { SocksPort = 1080, HttpPort = 8080 };
        var store = new FakeSettingsStore(settings);
        var refreshRequests = new List<string>();
        var viewModel = new SettingsCoreConfigViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            (success, _) => refreshRequests.Add(success),
            _ => { });

        viewModel.SocksPortText = " ";
        viewModel.HttpPortText = "abc";

        Assert.Null(settings.SocksPort);
        Assert.Equal("", viewModel.SocksPortText);
        Assert.Equal(8080, settings.HttpPort);
        Assert.Equal("8080", viewModel.HttpPortText);
        Assert.Equal(1, store.SaveCount);
        Assert.Single(refreshRequests);
        Assert.Equal(["PortControl"], viewModel.ChangeAreas);
    }

    [Fact(DisplayName = "Core config apply TUN from home persists and notifies home state")]
    public void CoreConfigApplyTunFromHomePersistsAndNotifiesHomeState()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var applied = new List<bool>();
        var viewModel = new SettingsCoreConfigViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            (_, _) => { },
            applied.Add);

        viewModel.ApplyTunFromHome(true);

        Assert.True(settings.IsTunEnabled);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal([true], applied);
        Assert.Equal(["Tun"], viewModel.ChangeAreas);
    }

    [Fact(DisplayName = "Core config network delay URL validates before local save")]
    public void CoreConfigNetworkDelayUrlValidatesBeforeLocalSave()
    {
        var settings = new AppSettings { DelayTestUrl = "https://old.example/generate_204" };
        var store = new FakeSettingsStore(settings);
        var refreshRequests = new List<string>();
        var viewModel = new SettingsCoreConfigViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            (success, _) => refreshRequests.Add(success),
            _ => { });

        viewModel.DelayTestUrl = "not a url";
        Assert.Equal("https://old.example/generate_204", settings.DelayTestUrl);
        Assert.Equal(0, store.SaveCount);

        viewModel.DelayTestUrl = "mailto:test@example.com";
        Assert.Equal("https://old.example/generate_204", settings.DelayTestUrl);
        Assert.Equal(0, store.SaveCount);

        viewModel.DelayTestUrl = " https://new.example/delay ";

        Assert.Equal("https://new.example/delay", settings.DelayTestUrl);
        Assert.Equal(1, store.SaveCount);
        Assert.Empty(refreshRequests);
        Assert.Equal(["Network"], viewModel.ChangeAreas);
    }

    [Fact(DisplayName = "Core config DNS list normalizes lines and avoids duplicate refresh")]
    public void CoreConfigDnsListNormalizesLinesAndAvoidsDuplicateRefresh()
    {
        var settings = new AppSettings { NameServers = ["1.1.1.1"] };
        var store = new FakeSettingsStore(settings);
        var refreshRequests = new List<string>();
        var viewModel = new SettingsCoreConfigViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            (success, _) => refreshRequests.Add(success),
            _ => { });

        viewModel.NameServersText = " 1.1.1.1 \n\n 8.8.8.8 \r\n";
        viewModel.NameServersText = "1.1.1.1\n8.8.8.8";

        Assert.Equal(["1.1.1.1", "8.8.8.8"], settings.NameServers);
        Assert.Equal(1, store.SaveCount);
        Assert.Single(refreshRequests);
        Assert.Equal(["Dns"], viewModel.ChangeAreas);
    }

    [Fact(DisplayName = "Core config performance integer input ignores invalid text")]
    public void CoreConfigPerformanceIntegerInputIgnoresInvalidText()
    {
        var settings = new AppSettings { TcpKeepAliveInterval = 15 };
        var store = new FakeSettingsStore(settings);
        var refreshRequests = new List<string>();
        var viewModel = new SettingsCoreConfigViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            (success, _) => refreshRequests.Add(success),
            _ => { });

        viewModel.TcpKeepAliveIntervalText = "abc";
        Assert.Equal(15, settings.TcpKeepAliveInterval);
        Assert.Equal(0, store.SaveCount);

        viewModel.TcpKeepAliveIntervalText = "45";

        Assert.Equal(45, settings.TcpKeepAliveInterval);
        Assert.Equal(1, store.SaveCount);
        Assert.Single(refreshRequests);
        Assert.Equal(["Performance"], viewModel.ChangeAreas);
    }

    [Fact(DisplayName = "Core config log level records dedicated runtime refresh request")]
    public void CoreConfigLogLevelRecordsDedicatedRuntimeRefreshRequest()
    {
        var settings = new AppSettings { CoreLogLevel = "info" };
        var store = new FakeSettingsStore(settings);
        var refreshRequests = new List<string>();
        var viewModel = new SettingsCoreConfigViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            (success, _) => refreshRequests.Add(success),
            _ => { });

        viewModel.SelectedCoreLogLevelOption = viewModel.CoreLogLevelOptions.Single(option => option.Value == "debug");
        viewModel.SelectedCoreLogLevelOption = viewModel.CoreLogLevelOptions.Single(option => option.Value == "debug");

        Assert.Equal("debug", settings.CoreLogLevel);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(["debug"], viewModel.CoreLogLevelChangeRequests);
        Assert.Single(refreshRequests);
        Assert.Empty(viewModel.ChangeAreas);
    }

    [Fact(DisplayName = "Platform integration settings notify system proxy reapply")]
    public void SystemIntegrationSettingsNotifySystemProxyReapply()
    {
        var settings = new AppSettings { ProxyHost = "127.0.0.1", MixedPort = 7890 };
        var store = new FakeSettingsStore(settings);
        var reapplyCount = 0;
        var viewModel = new SettingsSystemIntegrationViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            null,
            new FakeHostDetector(),
            SystemProxyPlatform.Windows,
            () => reapplyCount++);

        viewModel.ProxyHost = "0.0.0.0";
        viewModel.IsPacModeEnabled = true;

        Assert.Equal(2, store.SaveCount);
        Assert.Equal(2, reapplyCount);
        Assert.Contains("0.0.0.0", viewModel.PacScript, StringComparison.Ordinal);
        Assert.True(viewModel.IsPacScriptVisible);
        Assert.False(viewModel.IsSystemProxyBypassVisible);
        Assert.Contains("device.local", viewModel.SystemProxyHostCandidates);
        Assert.Contains("192.168.1.2", viewModel.SystemProxyHostCandidates);
    }

    [Fact(DisplayName = "Platform integration uses default bypass when custom bypass is empty")]
    public void SystemIntegrationUsesDefaultBypassWhenCustomBypassIsEmpty()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var viewModel = new SettingsSystemIntegrationViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            null,
            null,
            SystemProxyPlatform.Windows);

        Assert.Contains("<local>", viewModel.SystemProxyBypass, StringComparison.Ordinal);

        viewModel.SystemProxyBypass = "custom.local";

        Assert.Equal("custom.local", viewModel.SystemProxyBypass);

        viewModel.RestoreDefaultSystemProxyBypassCommand.Execute(null);

        Assert.Equal("", settings.SystemProxyBypass);
        Assert.Contains("<local>", viewModel.SystemProxyBypass, StringComparison.Ordinal);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact(DisplayName = "Platform integration restore default PAC script persists empty template once")]
    public void SystemIntegrationRestoreDefaultPacScriptPersistsEmptyTemplateOnce()
    {
        var settings = new AppSettings
        {
            ProxyHost = "127.0.0.1",
            MixedPort = 7890,
            IsPacModeEnabled = true,
            PacScript = "return \"DIRECT\";"
        };
        var store = new FakeSettingsStore(settings);
        var reapplyCount = 0;
        var viewModel = new SettingsSystemIntegrationViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            null,
            null,
            SystemProxyPlatform.Windows,
            () => reapplyCount++);

        viewModel.RestoreDefaultPacScriptCommand.Execute(null);
        viewModel.RestoreDefaultPacScriptCommand.Execute(null);

        Assert.Equal("", settings.PacScript);
        Assert.Contains("127.0.0.1:7890", viewModel.PacScript, StringComparison.Ordinal);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, reapplyCount);
        Assert.Equal(["SystemIntegration"], viewModel.ChangeAreas);
    }

    [Fact(DisplayName = "Platform integration refreshes system proxy host candidates from latest detection")]
    public void SystemIntegrationRefreshesSystemProxyHostCandidatesFromLatestDetection()
    {
        var settings = new AppSettings();
        var detector = new FakeHostDetector();
        var viewModel = new SettingsSystemIntegrationViewModel(
            settings,
            new FakeSettingsStore(settings),
            new FakeLocalizationService(),
            null,
            detector,
            SystemProxyPlatform.Windows);

        detector.NextResult = new SystemProxyHostDetectionResult("laptop", ["10.0.0.2", "fe80::abcd%7", "127.0.0.1"]);
        viewModel.RefreshSystemProxyHostCandidatesCommand.Execute(null);

        Assert.Equal(["127.0.0.1", "localhost", "laptop.local", "10.0.0.2", "fe80::abcd"], viewModel.SystemProxyHostCandidates);
    }

    [Fact(DisplayName = "System proxy application request builds platform bypass and PAC script")]
    public void SystemProxyApplicationRequestBuildsPlatformBypassAndPacScript()
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

    [Fact(DisplayName = "UWP loopback item hides duplicate identity rows and matches search")]
    public void UwpLoopbackItemHidesDuplicateIdentityRowsAndMatchesSearch()
    {
        var repeated = new UwpLoopbackItemViewModel(new UwpLoopbackPackage("Package.App", "Package.App", false, "Package.App"));
        var rich = new UwpLoopbackItemViewModel(new UwpLoopbackPackage("Contoso.Photo", "Gallery", true, "PhotoContainer", "S-1-15-2-1"));

        Assert.False(repeated.ShowPackageFamilyName);
        Assert.False(repeated.ShowAppContainerName);
        Assert.True(rich.ShowPackageFamilyName);
        Assert.True(rich.ShowAppContainerName);
        Assert.True(rich.HasSid);
        Assert.True(rich.Matches("photo"));
        Assert.True(rich.Matches("Gallery"));
        Assert.False(rich.Matches("mail"));
    }

    [Fact(DisplayName = "UWP loopback selection actions only affect filtered items")]
    public async Task UwpLoopbackSelectionActionsOnlyAffectFilteredItems()
    {
        var settings = new AppSettings();
        var service = new FakeUwpLoopbackService(
        [
            new UwpLoopbackPackage("App.Camera", "Camera", false),
            new UwpLoopbackPackage("App.Mail", "Mail", false),
            new UwpLoopbackPackage("App.Store", "Store", true),
            new UwpLoopbackPackage("Tool.Camera", "Camera Tool", false)
        ]);
        var viewModel = new SettingsSystemIntegrationViewModel(
            settings,
            new FakeSettingsStore(settings),
            new FakeLocalizationService(),
            service,
            null);

        viewModel.ShowUwpLoopbackDialogCommand.Execute(null);
        await WaitForUwpPackagesAsync(viewModel, 4);
        viewModel.UwpSearchText = "camera";

        viewModel.SelectAllUwpCommand.Execute(null);

        Assert.Equal(["App.Camera", "Tool.Camera"], viewModel.UwpLoopbackItems.Select(item => item.PackageFamilyName));
        Assert.True(viewModel.AllUwpItems.Single(item => item.PackageFamilyName == "App.Camera").IsSelected);
        Assert.True(viewModel.AllUwpItems.Single(item => item.PackageFamilyName == "Tool.Camera").IsSelected);
        Assert.False(viewModel.AllUwpItems.Single(item => item.PackageFamilyName == "App.Mail").IsSelected);
        Assert.True(viewModel.AllUwpItems.Single(item => item.PackageFamilyName == "App.Store").IsSelected);

        viewModel.InvertUwpSelectionCommand.Execute(null);

        Assert.False(viewModel.AllUwpItems.Single(item => item.PackageFamilyName == "App.Camera").IsSelected);
        Assert.False(viewModel.AllUwpItems.Single(item => item.PackageFamilyName == "Tool.Camera").IsSelected);
        Assert.False(viewModel.AllUwpItems.Single(item => item.PackageFamilyName == "App.Mail").IsSelected);
        Assert.True(viewModel.AllUwpItems.Single(item => item.PackageFamilyName == "App.Store").IsSelected);
    }

    [Fact(DisplayName = "UWP loopback save failure keeps pending items and raises admin toast")]
    public async Task UwpLoopbackSaveFailureKeepsPendingItemsAndRaisesAdminToast()
    {
        var settings = new AppSettings();
        var service = new FakeUwpLoopbackService(
        [
            new UwpLoopbackPackage("App.Camera", "Camera", false),
            new UwpLoopbackPackage("App.Mail", "Mail", true)
        ]);
        service.NextBatchResult = new UwpLoopbackBatchResult(
            false,
            "Access denied",
            [new UwpLoopbackPackage("App.Replaced", "Replaced", true)]);
        var viewModel = new SettingsSystemIntegrationViewModel(
            settings,
            new FakeSettingsStore(settings),
            new FakeLocalizationService(),
            service,
            null);
        var toasts = new List<(string Message, ToastType Type)>();
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

        viewModel.ShowUwpLoopbackDialogCommand.Execute(null);
        await WaitForUwpPackagesAsync(viewModel, 2);
        Assert.True(viewModel.SetUwpItemSelected("App.Camera", true));

        viewModel.SaveUwpLoopbackCommand.Execute(null);

        Assert.Equal(["App.Camera", "App.Mail"], service.LastEnabledPackageFamilyNames);
        Assert.Equal("", viewModel.UwpLoopbackStatusText);
        Assert.False(viewModel.IsUwpLoopbackStatusVisible);
        var toast = Assert.Single(toasts);
        Assert.Equal("Settings.System.UwpLoopback.Toast.AdminRequired", toast.Message);
        Assert.Equal(ToastType.Error, toast.Type);
        Assert.Equal(["App.Camera", "App.Mail"], viewModel.AllUwpItems.Select(item => item.PackageFamilyName));
        Assert.True(viewModel.AllUwpItems.Single(item => item.PackageFamilyName == "App.Camera").IsSelected);
    }

    [Fact(DisplayName = "UWP loopback save success keeps list and filter without refreshing snapshot")]
    public async Task UwpLoopbackSaveSuccessKeepsListWithoutRefresh()
    {
        var settings = new AppSettings();
        var service = new FakeUwpLoopbackService(
        [
            new UwpLoopbackPackage("App.Camera", "Camera", false),
            new UwpLoopbackPackage("App.Mail", "Mail", true)
        ]);
        // 服务返回的新快照带 App.Store，用来验证保存后不采纳、只保留原列表和滚动位置
        service.NextBatchResult = new UwpLoopbackBatchResult(
            true,
            "Saved successfully",
            [
                new UwpLoopbackPackage("App.Camera", "Camera", true),
                new UwpLoopbackPackage("App.Mail", "Mail", false),
                new UwpLoopbackPackage("App.Store", "Store", true)
            ]);
        var viewModel = new SettingsSystemIntegrationViewModel(
            settings,
            new FakeSettingsStore(settings),
            new FakeLocalizationService(),
            service,
            null);
        var toasts = new List<(string Message, ToastType Type)>();
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

        viewModel.ShowUwpLoopbackDialogCommand.Execute(null);
        await WaitForUwpPackagesAsync(viewModel, 2);
        viewModel.UwpSearchText = "mail";
        Assert.True(viewModel.SetUwpItemSelected("App.Camera", true));

        viewModel.SaveUwpLoopbackCommand.Execute(null);

        Assert.Equal(["App.Camera", "App.Mail"], service.LastEnabledPackageFamilyNames);
        Assert.Equal("", viewModel.UwpLoopbackStatusText);
        Assert.False(viewModel.IsUwpLoopbackStatusVisible);
        // 列表和筛选保持不变，服务快照里的 App.Store 不进入列表
        Assert.Equal(["App.Camera", "App.Mail"], viewModel.AllUwpItems.Select(item => item.PackageFamilyName));
        Assert.Equal(["App.Mail"], viewModel.UwpLoopbackItems.Select(item => item.PackageFamilyName));
        var toast = Assert.Single(toasts);
        Assert.Equal("Settings.System.UwpLoopback.Toast.Saved", toast.Message);
        Assert.Equal(ToastType.Success, toast.Type);
    }

    [Fact(DisplayName = "UWP loopback missing service shows an empty dialog and saving is a no-op")]
    public async Task UwpLoopbackMissingServiceShowsEmptyDialogAndSaveIsNoOp()
    {
        var settings = new AppSettings();
        var viewModel = new SettingsSystemIntegrationViewModel(
            settings,
            new FakeSettingsStore(settings),
            new FakeLocalizationService(),
            null,
            null);

        viewModel.ShowUwpLoopbackDialogCommand.Execute(null);
        await WaitForUwpStatusAsync(viewModel, "Settings.System.UwpLoopback.Empty");
        viewModel.SaveUwpLoopbackCommand.Execute(null);

        Assert.True(viewModel.IsUwpLoopbackDialogVisible);
        Assert.Empty(viewModel.AllUwpItems);
        Assert.Empty(viewModel.UwpLoopbackItems);
        Assert.Equal("Settings.System.UwpLoopback.Empty", viewModel.UwpLoopbackStatusText);
        Assert.True(viewModel.IsUwpLoopbackStatusVisible);
    }

    [Fact(DisplayName = "UWP loopback close before load completes drops loaded packages")]
    public async Task UwpLoopbackCloseBeforeLoadCompletesDropsLoadedPackages()
    {
        var settings = new AppSettings();
        var service = new BlockingUwpLoopbackService(
        [
            new UwpLoopbackPackage("App.Camera", "Camera", false)
        ]);
        var viewModel = new SettingsSystemIntegrationViewModel(
            settings,
            new FakeSettingsStore(settings),
            new FakeLocalizationService(),
            service,
            null);

        viewModel.ShowUwpLoopbackDialogCommand.Execute(null);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.CloseUwpLoopbackDialogCommand.Execute(null);
        service.Release.SetResult();
        await service.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        Assert.False(viewModel.IsUwpLoopbackDialogVisible);
        Assert.Empty(viewModel.AllUwpItems);
        Assert.Empty(viewModel.UwpLoopbackItems);
        Assert.Equal("Settings.System.UwpLoopback.Loading", viewModel.UwpLoopbackStatusText);
    }

    [Fact(DisplayName = "Theme settings persist theme and custom accent changes")]
    public void ThemeSettingsPersistThemeAndCustomAccentChanges()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var viewModel = new SettingsThemeViewModel(settings, store, new FakeLocalizationService());
        var themeChanges = new List<AppTheme>();
        var accentChanges = 0;
        viewModel.ThemeChanged += (_, theme) => themeChanges.Add(theme);
        viewModel.AccentColorChanged += (_, _) => accentChanges++;

        viewModel.SelectedOption = viewModel.Options.First(option => option.Value == AppTheme.Dark);
        viewModel.ConfirmCustomAccentColor("#112233");

        Assert.Equal("Dark", settings.Theme);
        Assert.Equal("Custom", settings.AccentColorMode);
        Assert.Equal("#112233", settings.AccentColor);
        Assert.Equal([AppTheme.Dark], themeChanges);
        Assert.Equal(1, accentChanges);
        Assert.True(store.SaveCount >= 2);
    }

    [Fact(DisplayName = "Theme accent mode request and repeated selection avoid extra save")]
    public void ThemeAccentModeRequestAndRepeatedSelectionAvoidExtraSave()
    {
        var settings = new AppSettings { AccentColorMode = "Custom", AccentColor = "#112233" };
        var store = new FakeSettingsStore(settings);
        var viewModel = new SettingsThemeViewModel(settings, store, new FakeLocalizationService());
        var requestCount = 0;
        var accentChanges = 0;
        viewModel.CustomAccentRequested += (_, _) => requestCount++;
        viewModel.AccentColorChanged += (_, _) => accentChanges++;

        viewModel.EditCustomAccentColorCommand.Execute(null);
        viewModel.SelectedAccentModeOption = viewModel.AccentModeOptions.Single(option => option.Value == AccentColorMode.Custom);
        viewModel.ConfirmCustomAccentColor("#112233");

        Assert.Equal(1, requestCount);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, accentChanges);

        viewModel.SelectedAccentModeOption = viewModel.AccentModeOptions.Single(option => option.Value == AccentColorMode.System);

        Assert.Equal("System", settings.AccentColorMode);
        Assert.Equal("#112233", settings.AccentColor);
        Assert.False(viewModel.IsCustomAccentMode);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, accentChanges);

        viewModel.CustomAccentColor = "#445566";

        Assert.Equal("Custom", settings.AccentColorMode);
        Assert.Equal("#445566", settings.AccentColor);
        Assert.True(viewModel.IsCustomAccentMode);
        Assert.Equal(2, store.SaveCount);
        Assert.Equal(2, accentChanges);
    }

    [Fact(DisplayName = "Theme window effect persists changes and ignores repeated selection")]
    public void ThemeWindowEffectPersistsChangesAndIgnoresRepeatedSelection()
    {
        var settings = new AppSettings { WindowEffect = "None" };
        var store = new FakeSettingsStore(settings);
        var viewModel = new SettingsThemeViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            new FakeWindowEffectCapability(WindowEffect.None, WindowEffect.Mica, WindowEffect.Acrylic));
        var effects = new List<WindowEffect>();
        viewModel.WindowEffectChanged += (_, effect) => effects.Add(effect);

        Assert.True(viewModel.IsWindowEffectSupported);

        viewModel.SelectedWindowEffectOption = viewModel.WindowEffectOptions.Single(option => option.Value == WindowEffect.Mica);
        viewModel.SelectedWindowEffectOption = viewModel.WindowEffectOptions.Single(option => option.Value == WindowEffect.Mica);

        Assert.Equal("Mica", settings.WindowEffect);
        Assert.Equal(WindowEffect.Mica, viewModel.SelectedWindowEffect);
        Assert.Equal([WindowEffect.Mica], effects);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact(DisplayName = "Theme window effect exposes only supported macOS options")]
    public void ThemeWindowEffectExposesOnlySupportedMacOSOptions()
    {
        var settings = new AppSettings { WindowEffect = "None" };
        var store = new FakeSettingsStore(settings);
        var viewModel = new SettingsThemeViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            new FakeWindowEffectCapability(WindowEffect.None, WindowEffect.Blur));

        Assert.True(viewModel.IsWindowEffectSupported);
        Assert.Equal([WindowEffect.None, WindowEffect.Blur], viewModel.WindowEffectOptions.Select(option => option.Value));

        viewModel.SelectedWindowEffectOption = viewModel.WindowEffectOptions.Single(option => option.Value == WindowEffect.Blur);

        Assert.Equal("Blur", settings.WindowEffect);
        Assert.Equal(WindowEffect.Blur, viewModel.SelectedWindowEffect);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact(DisplayName = "Theme window effect falls back when stored value is unsupported")]
    public void ThemeWindowEffectFallsBackWhenStoredValueIsUnsupported()
    {
        var settings = new AppSettings { WindowEffect = "Mica" };
        var store = new FakeSettingsStore(settings);
        var viewModel = new SettingsThemeViewModel(
            settings,
            store,
            new FakeLocalizationService(),
            new FakeWindowEffectCapability(WindowEffect.None, WindowEffect.Blur));

        Assert.Equal(WindowEffect.None, viewModel.SelectedWindowEffect);
        Assert.Equal("None", settings.WindowEffect);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact(DisplayName = "Data management runs backup restore and captures failure status")]
    public void DataManagementRunsBackupRestoreAndCapturesFailureStatus()
    {
        var service = new FakeDataManagementService();
        var viewModel = new SettingsDataManagementViewModel(service, new FakeLocalizationService());

        viewModel.CreateBackupCommand.Execute(null);
        viewModel.ShowRestoreLatestDialogCommand.Execute(null);
        viewModel.SelectMergeModeCommand.Execute(null);
        viewModel.ConfirmRestoreCommand.Execute(null);
        service.NextException = new InvalidOperationException("zip is corrupt");
        viewModel.ShowRestoreLatestDialogCommand.Execute(null);
        viewModel.ConfirmRestoreCommand.Execute(null);

        Assert.Equal("Restore", viewModel.LastOperation);
        Assert.Null(service.LastRestorePath);
        Assert.Equal(DataRestoreMode.Merge, service.LastRestoreMode);
        Assert.Equal(1, service.BackupCount);
        Assert.Equal(1, service.RestoreCount);
    }

    [Fact(DisplayName = "Data management restore dialog cancel keeps service untouched")]
    public void DataManagementRestoreDialogCancelKeepsServiceUntouched()
    {
        var service = new FakeDataManagementService();
        var viewModel = new SettingsDataManagementViewModel(service, new FakeLocalizationService());

        viewModel.ShowRestoreLatestDialogCommand.Execute(null);
        viewModel.SelectMergeModeCommand.Execute(null);

        Assert.True(viewModel.IsRestoreDialogVisible);
        Assert.True(viewModel.IsMergeSelected);
        Assert.Equal("Settings.Data.RestoreDialog.LatestTarget", viewModel.RestoreTargetText);

        viewModel.CancelRestoreCommand.Execute(null);

        Assert.False(viewModel.IsRestoreDialogVisible);
        Assert.Equal(0, service.RestoreCount);
        Assert.Equal("", viewModel.LastOperation);

        viewModel.ShowRestoreLatestDialogCommand.Execute(null);

        Assert.True(viewModel.IsRestoreDialogVisible);
        Assert.True(viewModel.IsOverwriteSelected);
        Assert.Equal("Settings.Data.RestoreDialog.LatestTarget", viewModel.RestoreTargetText);
    }

    [Fact(DisplayName = "Data management restore confirm after cancel does not run service")]
    public void DataManagementRestoreConfirmAfterCancelDoesNotRunService()
    {
        var service = new FakeDataManagementService();
        var viewModel = new SettingsDataManagementViewModel(service, new FakeLocalizationService());

        viewModel.ShowRestoreLatestDialogCommand.Execute(null);
        viewModel.SelectMergeModeCommand.Execute(null);
        viewModel.CancelRestoreCommand.Execute(null);
        viewModel.ConfirmRestoreCommand.Execute(null);

        Assert.Equal(0, service.RestoreCount);
        Assert.Equal("", viewModel.LastOperation);
        Assert.False(viewModel.IsRestoreDialogVisible);
    }

    [Fact(DisplayName = "Data management WebDAV settings persist and backup reports status")]
    public async Task DataManagementWebDavSettingsPersistAndBackupReportsStatus()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var webDavService = new FakeWebDavDataBackupService();
        var now = DateTimeOffset.UnixEpoch.AddHours(10);
        var viewModel = new SettingsDataManagementViewModel(
            new FakeDataManagementService(),
            new FakeLocalizationService(),
            settings,
            store,
            webDavService,
            () => now);

        viewModel.IsWebDavBackupEnabled = true;
        viewModel.WebDavUrl = "https://webdav.example/dav";
        viewModel.WebDavUserName = "test-user";
        viewModel.WebDavPassword = "<webdav-password>";
        viewModel.WebDavRemoteDirectory = "test-data/backups";
        viewModel.WebDavBackupIntervalHoursText = "6";
        viewModel.WebDavBackupRetentionCountText = "3";
        await viewModel.TestWebDavConnectionAsync();
        await viewModel.CreateWebDavBackupAsync();

        Assert.True(settings.IsWebDavBackupEnabled);
        Assert.Equal("https://webdav.example/dav", webDavService.LastSettings?.Url);
        Assert.Equal("test-data/backups", webDavService.LastSettings?.RemoteDirectory);
        Assert.Equal(3, webDavService.LastSettings?.RetentionCount);
        Assert.Equal(now, settings.LastWebDavBackupTime);
        Assert.True(store.SaveCount >= 8);
        Assert.Equal(1, webDavService.TestCount);
        Assert.Equal(1, webDavService.BackupCount);
        Assert.Equal("WebDavBackup", viewModel.LastOperation);
        Assert.Equal("Settings.Data.WebDav.Toast.BackupCreated", viewModel.WebDavStatusText);
    }

    [Fact(DisplayName = "Data management WebDAV restore uses dialog mode")]
    public async Task DataManagementWebDavRestoreUsesDialogMode()
    {
        var settings = new AppSettings { WebDavUrl = "https://webdav.example/dav" };
        var webDavService = new FakeWebDavDataBackupService();
        var viewModel = new SettingsDataManagementViewModel(
            new FakeDataManagementService(),
            new FakeLocalizationService(),
            settings,
            new FakeSettingsStore(settings),
            webDavService);

        viewModel.ShowWebDavRestoreLatestDialogCommand.Execute(null);
        viewModel.SelectMergeModeCommand.Execute(null);
        await viewModel.ConfirmRestoreAsync();

        Assert.False(viewModel.IsRestoreDialogVisible);
        Assert.Equal("WebDavRestore", viewModel.LastOperation);
        Assert.Equal(1, webDavService.RestoreCount);
        Assert.Equal(DataRestoreMode.Merge, webDavService.LastRestoreMode);
        Assert.Equal("Settings.Data.Toast.RestoreCompleted", viewModel.WebDavStatusText);
    }

    [Fact(DisplayName = "Language settings fallback and selection persist language")]
    public void LanguageSettingsFallbackAndSelectionPersistLanguage()
    {
        var settings = new AppSettings { Language = "broken" };
        var store = new FakeSettingsStore(settings);
        var localization = new FakeLocalizationService();
        var viewModel = new SettingsLanguageViewModel(settings, store, localization);

        Assert.Equal(AppLanguage.System, localization.CurrentLanguage);
        Assert.Equal(AppLanguage.System, viewModel.SelectedOption.Value);

        viewModel.SetLanguage(AppLanguage.En);

        Assert.Equal("En", settings.Language);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(AppLanguage.En, localization.CurrentLanguage);
        Assert.True(localization.SetLanguageCount >= 2);
    }

    [Fact(DisplayName = "Update settings manual check and ignore persist latest version")]
    public async Task UpdateSettingsManualCheckAndIgnorePersistLatestVersion()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(2);
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var checker = new FakeAppUpdateChecker(new AppUpdateCheckResult(true, "v9.9.9", "New version found"));
        var viewModel = new SettingsUpdateViewModel(settings, store, new FakeLocalizationService(), () => now, checker, TimeSpan.Zero);

        await viewModel.CheckAsync();

        Assert.Equal("ManualCheck", viewModel.LastOperation);
        Assert.Equal("New version found", viewModel.StatusText);
        Assert.Equal("v9.9.9", viewModel.LatestVersionText);
        Assert.False(viewModel.IsChecking);
        Assert.True(viewModel.CanIgnoreLatestVersion);
        Assert.Equal(now, settings.LastAppUpdateCheckTime);
        Assert.Equal(1, store.SaveCount);

        viewModel.IgnoreLatestVersionCommand.Execute(null);

        Assert.Equal("v9.9.9", settings.IgnoredUpdateVersion);
        Assert.False(viewModel.CanIgnoreLatestVersion);
        Assert.Equal("v9.9.9", viewModel.IgnoredVersionText);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact(DisplayName = "Update settings no update clears ignore candidate and settings save only on change")]
    public async Task UpdateSettingsNoUpdateClearsIgnoreCandidateAndSettingsSaveOnlyOnChange()
    {
        var settings = new AppSettings { IsAutoCheckUpdateEnabled = false, AppUpdateCheckInterval = "startup" };
        var store = new FakeSettingsStore(settings);
        var checker = new FakeAppUpdateChecker(new AppUpdateCheckResult(false, null, "Already on the latest version"));
        var viewModel = new SettingsUpdateViewModel(settings, store, new FakeLocalizationService(), () => DateTimeOffset.UnixEpoch, checker, TimeSpan.Zero);
        var toasts = new List<(string Message, ToastType Type)>();
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

        await viewModel.CheckAsync();

        Assert.Equal("ManualCheck", viewModel.LastOperation);
        Assert.Equal("Already on the latest version", viewModel.StatusText);
        Assert.False(viewModel.CanIgnoreLatestVersion);
        Assert.Equal("Settings.Update.NoUpdate", viewModel.LatestVersionText);
        Assert.Equal([("Settings.Update.Toast.NoUpdate", ToastType.Info)], toasts);
        Assert.Equal(1, store.SaveCount);

        viewModel.IgnoreLatestVersionCommand.Execute(null);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal("", settings.IgnoredUpdateVersion);

        viewModel.IsAutoCheckEnabled = true;
        viewModel.IsAutoCheckEnabled = true;
        viewModel.SelectedCheckIntervalOption = viewModel.CheckIntervalOptions.Single(option => option.Value == "7days");
        viewModel.SelectedCheckIntervalOption = viewModel.CheckIntervalOptions.Single(option => option.Value == "7days");

        Assert.True(settings.IsAutoCheckUpdateEnabled);
        Assert.Equal("7days", settings.AppUpdateCheckInterval);
        Assert.Equal(3, store.SaveCount);
        Assert.Equal("Common.Enabled", viewModel.AutoCheckStateText);
        Assert.Equal("Settings.Update.Interval.SevenDays", viewModel.CheckIntervalText);
    }

    [Fact(DisplayName = "Update settings applies only checked auto check result")]
    public void UpdateSettingsAppliesOnlyCheckedAutoCheckResult()
    {
        var settings = new AppSettings();
        var viewModel = new SettingsUpdateViewModel(settings, new FakeSettingsStore(settings), new FakeLocalizationService(), () => DateTimeOffset.UnixEpoch, null);

        viewModel.ApplyAutoCheckResult(new AppUpdateAutoCheckResult(false, false, "Not due yet"));
        Assert.Equal("", viewModel.LastOperation);
        Assert.Equal("", viewModel.StatusText);

        viewModel.ApplyAutoCheckResult(new AppUpdateAutoCheckResult(true, true, "Auto check found a new version"));
        Assert.Equal("AutoCheck", viewModel.LastOperation);
        Assert.Equal("Auto check found a new version", viewModel.StatusText);
    }

    [Fact(DisplayName = "Update settings manual check without checker keeps empty result")]
    public async Task UpdateSettingsManualCheckWithoutCheckerKeepsEmptyResult()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var viewModel = new SettingsUpdateViewModel(settings, store, new FakeLocalizationService(), () => DateTimeOffset.UnixEpoch, null, TimeSpan.Zero);
        var toasts = new List<(string Message, ToastType Type)>();
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

        await viewModel.CheckAsync();

        Assert.Equal("ManualCheck", viewModel.LastOperation);
        Assert.Equal("", viewModel.StatusText);
        Assert.False(viewModel.IsStatusVisible);
        Assert.Equal("Settings.Update.NoUpdate", viewModel.LatestVersionText);
        Assert.Equal("Settings.Update.NotChecked", viewModel.LastCheckText);
        Assert.False(viewModel.CanIgnoreLatestVersion);
        Assert.Equal([("Settings.Update.Toast.CheckUnavailable", ToastType.Error)], toasts);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact(DisplayName = "Update settings manual check failure raises error toast")]
    public async Task UpdateSettingsManualCheckFailureRaisesErrorToast()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var checker = new FakeAppUpdateChecker(new AppUpdateCheckResult(false, null, "network unavailable", IsFailure: true));
        var viewModel = new SettingsUpdateViewModel(settings, store, new FakeLocalizationService(), () => DateTimeOffset.UnixEpoch, checker, TimeSpan.Zero);
        var toasts = new List<(string Message, ToastType Type)>();
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

        await viewModel.CheckAsync();

        Assert.Equal("network unavailable", viewModel.StatusText);
        Assert.Equal([("Settings.Update.Toast.CheckFailed", ToastType.Error)], toasts);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact(DisplayName = "Update settings manual check keeps spinner visible for minimum duration")]
    public async Task UpdateSettingsManualCheckKeepsSpinnerVisibleForMinimumDuration()
    {
        var minimumDuration = TimeSpan.FromMilliseconds(80);
        var settings = new AppSettings();
        var checker = new FakeAppUpdateChecker(new AppUpdateCheckResult(false, null, "Already on the latest version"));
        var viewModel = new SettingsUpdateViewModel(
            settings,
            new FakeSettingsStore(settings),
            new FakeLocalizationService(),
            () => DateTimeOffset.UnixEpoch,
            checker,
            minimumDuration);

        var stopwatch = Stopwatch.StartNew();
        var checkTask = viewModel.CheckAsync();

        Assert.True(viewModel.IsChecking);
        await checkTask;
        Assert.False(viewModel.IsChecking);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(60));
    }

    [Fact(DisplayName = "Auto update scheduler treats unknown interval as startup only")]
    public void AutoUpdateSchedulerTreatsUnknownIntervalAsStartupOnly()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(10);
        var settings = new AppSettings
        {
            IsAutoCheckUpdateEnabled = true,
            AppUpdateCheckInterval = "broken",
            LastAppUpdateCheckTime = now.AddDays(-9)
        };
        var store = new FakeSettingsStore(settings);
        var checker = new FakeAppUpdateChecker(new AppUpdateCheckResult(true, "v9.9.9", "New version found"));
        var scheduler = new AppUpdateAutoCheckScheduler(checker, () => settings, store.Save, () => now);

        var startup = scheduler.CheckOnStartup();
        var due = scheduler.CheckWhenDue();

        Assert.True(startup.WasChecked);
        Assert.True(startup.HasUpdate);
        Assert.Equal(now, settings.LastAppUpdateCheckTime);
        Assert.Equal(1, checker.CheckCount);
        Assert.Equal(1, store.SaveCount);
        Assert.False(due.WasChecked);
        Assert.Equal("The current setting only checks at startup", due.Message);
    }

    private static async Task WaitForUwpPackagesAsync(SettingsSystemIntegrationViewModel viewModel, int expectedCount)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (viewModel.AllUwpItems.Count == expectedCount)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Equal(expectedCount, viewModel.AllUwpItems.Count);
    }

    private static async Task WaitForUwpStatusAsync(SettingsSystemIntegrationViewModel viewModel, string expectedStatus)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (viewModel.UwpLoopbackStatusText == expectedStatus)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Equal(expectedStatus, viewModel.UwpLoopbackStatusText);
    }

    private sealed class FakeSettingsStore(AppSettings settings) : IAppSettingsStore
    {
        public int SaveCount { get; private set; }

        public AppSettings Load() => settings;

        public void Save(AppSettings settings)
        {
            SaveCount++;
        }
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.ZhHans;
        public int SetLanguageCount { get; private set; }

        public AppLanguage EffectiveLanguage => CurrentLanguage;

        public event EventHandler? LanguageChanged;

        public void SetLanguage(AppLanguage language)
        {
            SetLanguageCount++;
            CurrentLanguage = language;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key) => key;
    }

    private sealed class FakeAppBehaviorService : IAppBehaviorService
    {
        public int ApplyCount { get; private set; }
        public AppBehaviorApplicationRequest? LastRequest { get; private set; }
        public bool ShouldFail { get; init; }

        public void Apply(AppBehaviorApplicationRequest request)
        {
            ApplyCount++;
            if (ShouldFail)
            {
                throw new InvalidOperationException("app behavior failed");
            }

            LastRequest = request;
        }
    }

    private sealed class FakeHostDetector : ISystemProxyHostDetector
    {
        public SystemProxyHostDetectionResult NextResult { get; set; } = new("device", ["192.168.1.2", "fe80::1%12"]);

        public SystemProxyHostDetectionResult Detect()
        {
            return NextResult;
        }
    }

    private sealed class FakeWindowEffectCapability(params WindowEffect[] supportedEffects) : IWindowEffectCapability
    {
        public IReadOnlyList<WindowEffect> SupportedEffects { get; } = supportedEffects;
    }

    private sealed class FakeUwpLoopbackService(IReadOnlyList<UwpLoopbackPackage> packages) : IUwpLoopbackService
    {
        public UwpLoopbackBatchResult? NextBatchResult { get; set; }

        public IReadOnlyList<string> LastEnabledPackageFamilyNames { get; private set; } = [];

        public IReadOnlyList<UwpLoopbackPackage> LoadPackages()
        {
            return packages;
        }

        public UwpLoopbackOperationResult SetLoopback(string packageFamilyName, bool isEnabled)
        {
            return new UwpLoopbackOperationResult(true, "ok", packages.FirstOrDefault(package => package.PackageFamilyName == packageFamilyName));
        }

        public UwpLoopbackBatchResult SetLoopbackBatch(IReadOnlyCollection<string> enabledPackageFamilyNames)
        {
            LastEnabledPackageFamilyNames = enabledPackageFamilyNames.ToArray();
            return NextBatchResult ?? new UwpLoopbackBatchResult(true, "ok", packages);
        }
    }

    private sealed class BlockingUwpLoopbackService(IReadOnlyList<UwpLoopbackPackage> packages) : IUwpLoopbackService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<UwpLoopbackPackage> LoadPackages()
        {
            Started.SetResult();
            Release.Task.Wait(TimeSpan.FromSeconds(2));
            Completed.SetResult();
            return packages;
        }

        public UwpLoopbackOperationResult SetLoopback(string packageFamilyName, bool isEnabled)
        {
            return new UwpLoopbackOperationResult(true, "ok", null);
        }

        public UwpLoopbackBatchResult SetLoopbackBatch(IReadOnlyCollection<string> enabledPackageFamilyNames)
        {
            return new UwpLoopbackBatchResult(true, "ok", packages);
        }
    }

    private sealed class FakeDataManagementService : IDataManagementService
    {
        public int BackupCount { get; private set; }
        public int RestoreCount { get; private set; }
        public string? LastRestorePath { get; private set; }
        public DataRestoreMode LastRestoreMode { get; private set; }
        public Exception? NextException { get; set; }

        public DataManagementOperationResult CreateBackup()
        {
            ThrowIfNeeded();
            BackupCount++;
            return new DataManagementOperationResult(true, "backup ok");
        }

        public DataManagementOperationResult CreateBackup(string backupPath)
        {
            ThrowIfNeeded();
            BackupCount++;
            return new DataManagementOperationResult(true, "backup file ok");
        }

        public DataManagementOperationResult RestoreBackup(DataRestoreMode mode)
        {
            ThrowIfNeeded();
            RestoreCount++;
            LastRestorePath = null;
            LastRestoreMode = mode;
            return new DataManagementOperationResult(true, "restore latest ok");
        }

        public DataManagementOperationResult RestoreBackup(string backupPath, DataRestoreMode mode)
        {
            ThrowIfNeeded();
            RestoreCount++;
            LastRestorePath = backupPath;
            LastRestoreMode = mode;
            return new DataManagementOperationResult(true, "restore file ok");
        }

        private void ThrowIfNeeded()
        {
            if (NextException is null)
            {
                return;
            }

            var exception = NextException;
            NextException = null;
            throw exception;
        }
    }

    private sealed class FakeWebDavDataBackupService : IWebDavDataBackupService
    {
        public int TestCount { get; private set; }
        public int BackupCount { get; private set; }
        public int RestoreCount { get; private set; }
        public WebDavBackupSettings? LastSettings { get; private set; }
        public DataRestoreMode LastRestoreMode { get; private set; }

        public Task<DataManagementOperationResult> TestConnectionAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
        {
            TestCount++;
            LastSettings = settings;
            return Task.FromResult(new DataManagementOperationResult(true, "webdav test ok"));
        }

        public Task<DataManagementOperationResult> CreateBackupAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
        {
            BackupCount++;
            LastSettings = settings;
            return Task.FromResult(new DataManagementOperationResult(true, "webdav backup ok"));
        }

        public Task<IReadOnlyList<RemoteBackupEntry>> ListBackupsAsync(WebDavBackupSettings settings, CancellationToken cancellationToken)
        {
            LastSettings = settings;
            IReadOnlyList<RemoteBackupEntry> entries = Array.Empty<RemoteBackupEntry>();
            return Task.FromResult(entries);
        }

        public Task<DataManagementOperationResult> RestoreBackupAsync(
            WebDavBackupSettings settings,
            string fileName,
            DataRestoreMode mode,
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            LastSettings = settings;
            LastRestoreMode = mode;
            return Task.FromResult(new DataManagementOperationResult(true, "webdav restore ok"));
        }

        public Task<DataManagementOperationResult> RestoreLatestBackupAsync(
            WebDavBackupSettings settings,
            DataRestoreMode mode,
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            LastSettings = settings;
            LastRestoreMode = mode;
            return Task.FromResult(new DataManagementOperationResult(true, "webdav restore ok"));
        }

        public Task<DataManagementOperationResult> DeleteBackupAsync(
            WebDavBackupSettings settings,
            string fileName,
            CancellationToken cancellationToken)
        {
            LastSettings = settings;
            return Task.FromResult(new DataManagementOperationResult(true, "webdav delete ok"));
        }
    }


    [Fact(DisplayName = "Update channel setting persists stable and beta values")]
    public void UpdateChannelSettingPersistsStableAndBetaValues()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore(settings);
        var viewModel = new SettingsUpdateViewModel(settings, store, new FakeLocalizationService(), () => DateTimeOffset.UnixEpoch, null);

        Assert.Equal("stable", viewModel.SelectedChannelOption.Value);

        viewModel.SelectedChannelOption = viewModel.ChannelOptions.Single(option => option.Value == "beta");

        Assert.Equal("beta", settings.AppUpdateChannel);
        Assert.Equal(1, store.SaveCount);

        viewModel.SelectedChannelOption = viewModel.ChannelOptions.Single(option => option.Value == "stable");
        Assert.Equal("stable", settings.AppUpdateChannel);
        Assert.Equal(2, store.SaveCount);
    }

    [Fact(DisplayName = "App update release selector separates stable and beta channels without network")]
    public void AppUpdateReleaseSelectorSeparatesStableAndBetaChannelsWithFakeReleases()
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

        var stableFrom200 = AppUpdateReleaseSelector.Select(releases, "stable", "2.0.0");
        Assert.NotNull(stableFrom200);
        Assert.Equal("v2.0.1", stableFrom200!.Version);

        var betaFrom200 = AppUpdateReleaseSelector.Select(releases, "beta", "2.0.0");
        Assert.NotNull(betaFrom200);
        Assert.Equal("v2.0.3-beta1", betaFrom200!.Version);

        var stableFrom201 = AppUpdateReleaseSelector.Select(releases, "stable", "2.0.1");
        Assert.Null(stableFrom201);

        var betaFromLatest = AppUpdateReleaseSelector.Select(releases, "beta", "2.0.3-beta1");
        Assert.Null(betaFromLatest);
    }

    private sealed class FakeAppUpdateChecker(AppUpdateCheckResult result) : IAppUpdateChecker
    {
        public int CheckCount { get; private set; }

        public AppUpdateCheckResult CheckForUpdates()
        {
            CheckCount++;
            return result;
        }
    }
}
