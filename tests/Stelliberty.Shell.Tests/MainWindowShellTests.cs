using Stelliberty.Application.Localization;
using Stelliberty.Application.Connections;
using Stelliberty.Application.Overrides;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Proxies;
using Stelliberty.Application.Rules;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Application.Updates;
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.Proxies;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.ViewModels;
using Xunit;

namespace Stelliberty.Shell.Tests;

public sealed class MainWindowShellTests
{
    private static readonly TimeSpan AsyncTestTimeout = TimeSpan.FromSeconds(5);

    [Fact(DisplayName = "Home subscription statistics ignore another subscription runtime")]
    public void HomeSubscriptionStatisticsIgnoreAnotherSubscriptionRuntime()
    {
        var subscriptions = new SubscriptionPageViewModel();
        subscriptions.AddSubscription(new SubscriptionItemViewModel("sub-1", "One", "one.yaml", true));
        using var viewModel = CreateViewModel(subscriptionPage: subscriptions);

        viewModel.ProxyPage.LoadConfig(SampleProxyConfig(), subscriptionId: "sub-2");

        Assert.Equal("-", subscriptions.HomeCardGroupCountText);
        Assert.Equal("-", subscriptions.HomeCardNodeCountText);
        Assert.Equal("-", subscriptions.HomeCardAverageDelayText);

        viewModel.ProxyPage.BindLoadedConfigToSubscription("sub-1");

        Assert.NotEqual("-", subscriptions.HomeCardGroupCountText);
        Assert.NotEqual("-", subscriptions.HomeCardNodeCountText);
    }

    [Fact(DisplayName = "Settings command resets the subpage and shows the settings page")]
    public void SettingsCommandResetsSubPageAndShowsSettingsPage()
    {
        using var viewModel = CreateViewModel();
        viewModel.Settings.SubPage = SettingsSubPage.Theme;

        viewModel.ShowSettingsCommand.Execute(null);

        Assert.Equal(NavigationPage.Settings, viewModel.CurrentPage);
        Assert.Equal(SettingsSubPage.Root, viewModel.Settings.SubPage);
        Assert.True(viewModel.IsSettingsSelected);
        Assert.True(viewModel.Settings.IsRootVisible);
        Assert.False(viewModel.IsHomeSelected);
        Assert.False(viewModel.Settings.IsBackVisible);
    }

    [Fact(DisplayName = "Settings back command uses the Clash feature intermediate page")]
    public void SettingsBackCommandUsesClashFeatureIntermediatePage()
    {
        using var viewModel = CreateViewModel();
        viewModel.ShowSettingsCommand.Execute(null);

        viewModel.Settings.ShowDnsCommand.Execute(null);
        viewModel.Settings.BackCommand.Execute(null);

        Assert.Equal(SettingsSubPage.ClashFeatures, viewModel.Settings.SubPage);
        Assert.True(viewModel.Settings.IsClashFeaturesVisible);
        Assert.True(viewModel.Settings.IsBackVisible);

        viewModel.Settings.BackCommand.Execute(null);
        Assert.Equal(SettingsSubPage.Root, viewModel.Settings.SubPage);

        viewModel.Settings.ShowLanguageCommand.Execute(null);
        viewModel.Settings.BackCommand.Execute(null);

        Assert.Equal(SettingsSubPage.Root, viewModel.Settings.SubPage);
        Assert.True(viewModel.Settings.IsRootVisible);
    }

    [Fact(DisplayName = "Language change refreshes shell text and spacing")]
    public void LanguageChangeRefreshesShellTextAndSpacing()
    {
        var localization = new FakeLocalizationService();
        using var viewModel = CreateViewModel(localization);
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changedProperties.Add(args.PropertyName);
            }
        };
        viewModel.Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changedProperties.Add(args.PropertyName);
            }
        };
        viewModel.ShowSettingsCommand.Execute(null);
        viewModel.Settings.ShowCoreLogCommand.Execute(null);

        localization.SetLanguage(AppLanguage.En);

        Assert.Equal(0, viewModel.NavLabelLetterSpacing);
        Assert.Equal("Nav.Home:En", viewModel.HomeNavText);
        Assert.Equal("Settings.Header.CoreLog:En", viewModel.Settings.HeaderText);
        Assert.Contains(nameof(MainWindowViewModel.NavLabelLetterSpacing), changedProperties);
        Assert.Contains(nameof(MainWindowViewModel.HomeNavText), changedProperties);
        Assert.Contains(nameof(SettingsPageViewModel.HeaderText), changedProperties);

        localization.SetLanguage(AppLanguage.ZhHans);

        Assert.Equal(6, viewModel.NavLabelLetterSpacing);
        Assert.Equal("Nav.Home:ZhHans", viewModel.HomeNavText);
    }

    [Fact(DisplayName = "Home outbound mode change updates proxy page visible groups")]
    public void HomeOutboundModeChangeUpdatesProxyPageVisibleGroups()
    {
        using var viewModel = CreateViewModel();
        viewModel.ProxyPage.LoadConfig(SampleProxyConfig());

        Assert.Equal(["Select"], viewModel.ProxyPage.VisibleGroups.Select(group => group.Name));

        viewModel.HomePage.ApplyOutboundMode(OutboundMode.Global);

        Assert.Equal(["GLOBAL"], viewModel.ProxyPage.VisibleGroups.Select(group => group.Name));
        Assert.Equal("GLOBAL", viewModel.ProxyPage.SelectedGroup?.Name);

        viewModel.HomePage.ApplyOutboundMode(OutboundMode.Direct);

        Assert.Empty(viewModel.ProxyPage.VisibleGroups);
        Assert.Null(viewModel.ProxyPage.SelectedGroup);
        Assert.True(viewModel.ProxyPage.IsEmptyVisible);
    }

    [Fact(DisplayName = "Runtime refresh persists external outbound mode and updates proxy groups")]
    public async Task RuntimeRefreshPersistsExternalOutboundModeAndUpdatesProxyGroups()
    {
        var settings = new AppSettings();
        var settingsStore = new FakeSettingsStore(settings);
        var core = new FakeProxyCoreClient
        {
            RuntimeStats = new CoreRuntimeStats(
                UploadSpeed: 0,
                DownloadSpeed: 0,
                UploadTotal: 0,
                DownloadTotal: 0,
                ConnectionCount: 0,
                Memory: 1024,
                HasTrafficRate: true),
            OutboundMode = OutboundMode.Global
        };
        using var viewModel = CreateViewModel(settingsStore: settingsStore, homeProxyClient: core);
        viewModel.ProxyPage.LoadConfig(SampleProxyConfig());

        viewModel.OnHomeRuntimeTick();
        await WaitUntilAsync(() => core.OutboundModeReadCount == 1 && viewModel.HomePage.OutboundMode == HomeOutboundMode.Global);

        Assert.Equal("Global", settings.OutboundMode);
        Assert.Equal(1, settingsStore.SaveCount);
        Assert.Equal(["GLOBAL"], viewModel.ProxyPage.VisibleGroups.Select(group => group.Name));
        Assert.Equal("GLOBAL", viewModel.ProxyPage.SelectedGroup?.Name);
    }

    [Fact(DisplayName = "Startup without TUN permission revokes persisted TUN")]
    public void StartupWithoutTunPermissionRevokesPersistedTun()
    {
        var settings = new AppSettings { IsTunEnabled = true };
        var settingsStore = new FakeSettingsStore(settings);

        using var viewModel = CreateViewModel(
            settingsStore: settingsStore,
            processPrivilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Normal));

        Assert.False(settings.IsTunEnabled);
        Assert.False(viewModel.HomePage.IsTunEnabled);
        Assert.True(viewModel.IsToastVisible);
        Assert.Equal(ToastType.Warning, viewModel.ToastType);
        Assert.Equal("Home.Toast.TunDisabledByPermission:System", viewModel.ToastMessage);
        Assert.Equal(1, settingsStore.SaveCount);
    }

    [Fact(DisplayName = "Startup with administrator permission keeps persisted TUN")]
    public void StartupWithAdministratorPermissionKeepsPersistedTun()
    {
        var settings = new AppSettings { IsTunEnabled = true };
        var settingsStore = new FakeSettingsStore(settings);

        using var viewModel = CreateViewModel(
            settingsStore: settingsStore,
            processPrivilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Administrator));

        Assert.True(settings.IsTunEnabled);
        Assert.True(viewModel.HomePage.IsTunEnabled);
        Assert.Equal(0, settingsStore.SaveCount);
    }

    [Fact(DisplayName = "Startup with service TUN host keeps persisted TUN")]
    public void StartupWithServiceTunHostKeepsPersistedTun()
    {
        var settings = new AppSettings { IsTunEnabled = true };
        var settingsStore = new FakeSettingsStore(settings);

        using var viewModel = CreateViewModel(
            settingsStore: settingsStore,
            processPrivilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Normal),
            initialServiceModeStatus: new ServiceModeStatus(ServiceModeState.Running, "running"));

        Assert.True(settings.IsTunEnabled);
        Assert.True(viewModel.HomePage.IsTunEnabled);
        Assert.Equal(0, settingsStore.SaveCount);
    }

    [Fact(DisplayName = "Manual update no-update result uses common toast")]
    public async Task ManualUpdateNoUpdateResultUsesCommonToast()
    {
        using var viewModel = CreateViewModel(
            updateChecker: new FakeAppUpdateChecker(new AppUpdateCheckResult(false, null, "Already on the latest version")));

        await viewModel.Update.CheckAsync();

        Assert.True(viewModel.IsToastVisible);
        Assert.Equal(ToastType.Info, viewModel.ToastType);
        Assert.Equal("Settings.Update.Toast.NoUpdate:System", viewModel.ToastMessage);
    }

    [Fact(DisplayName = "Proxy node selection close all event clears connection page")]
    public async Task ProxyNodeSelectionCloseAllEventClearsConnectionPage()
    {
        var core = new FakeProxyCoreClient();
        var proxyPage = new ProxyPageViewModel(coreClient: core, selectionService: new ProxySelectionService(core));
        var connectionPage = new ConnectionPageViewModel(core);
        using var viewModel = CreateViewModel(proxyPage: proxyPage, connectionPage: connectionPage);
        viewModel.ProxyPage.LoadConfig(SampleProxyConfig());
        viewModel.ConnectionPage.ApplyIncoming(
        [
            new ConnectionInfo("c1", Metadata: new ConnectionMetadata(Network: "tcp", Host: "example.com", DestinationPort: "443")),
            new ConnectionInfo("c2", Metadata: new ConnectionMetadata(Network: "udp", Host: "dns.example", DestinationPort: "53"))
        ]);
        viewModel.ConnectionPage.ShowDetailCommand.Execute("c1");
        viewModel.ProxyPage.SelectGroup("Select");

        await viewModel.ProxyPage.SelectNodeAsync("KR");

        Assert.Empty(viewModel.ConnectionPage.Connections);
        Assert.True(viewModel.ConnectionPage.HasClosedAllConnections);
        Assert.False(viewModel.ConnectionPage.IsDetailVisible);
        Assert.Equal(new ProxyChangeRequest("Select", "KR"), Assert.Single(core.ChangeRequests));
        Assert.Equal(ConnectionCloseMode.All, Assert.Single(core.CloseRequests).Mode);
    }

    [Fact(DisplayName = "Showing connections page refreshes connections immediately")]
    public async Task ShowingConnectionsPageRefreshesConnectionsImmediately()
    {
        var core = new FakeProxyCoreClient
        {
            Connections =
            [
                new ConnectionInfo("c1", Metadata: new ConnectionMetadata(Network: "tcp", Host: "example.com", DestinationPort: "443"))
            ],
            TrafficRate = new CoreTrafficRate(UploadSpeed: 12, DownloadSpeed: 34)
        };
        using var viewModel = CreateViewModel(connectionPage: new ConnectionPageViewModel(core));

        viewModel.ShowConnectionsCommand.Execute(null);
        await WaitUntilAsync(() => core.ConnectionReadCount == 1 && viewModel.ConnectionPage.Connections.Count == 1);

        Assert.Equal(NavigationPage.Connections, viewModel.CurrentPage);
        Assert.True(viewModel.IsConnectionsSelected);
        Assert.Equal("c1", viewModel.ConnectionPage.Connections.Single().Id);
        Assert.Equal(1, core.TrafficReadCount);
    }

    [Fact(DisplayName = "Showing proxy page starts external selection sync")]
    public async Task ShowingProxyPageStartsExternalSelectionSync()
    {
        var config = SampleProxyConfig();
        var synced = config with
        {
            Groups = config.Groups
                .Select(group => group.Name == "Select" ? group with { Now = "KR" } : group)
                .ToList()
        };
        var provider = new FakeProxyConfigProvider(synced);
        var proxyPage = new ProxyPageViewModel(primaryConfigProvider: provider);
        proxyPage.LoadConfig(config);
        using var viewModel = CreateViewModel(proxyPage: proxyPage);

        viewModel.ShowProxyCommand.Execute(null);
        await WaitUntilAsync(() => provider.LoadCount == 1 && viewModel.ProxyPage.SelectedGroup?.DisplaySelectionName == "KR");

        Assert.Equal(NavigationPage.Proxy, viewModel.CurrentPage);
        Assert.True(viewModel.IsProxySelected);
        Assert.Equal("KR", viewModel.ProxyPage.VisibleNodeRows.Single(row => row.Name == "KR").Name);
        Assert.True(viewModel.ProxyPage.VisibleNodeRows.Single(row => row.Name == "KR").IsSelected);
    }

    [Fact(DisplayName = "Leaving the proxy page cancels in-flight external selection sync")]
    public async Task LeavingProxyPageCancelsInFlightExternalSelectionSync()
    {
        var config = SampleProxyConfig();
        var synced = config with
        {
            Groups = config.Groups
                .Select(group => group.Name == "Select" ? group with { Now = "KR" } : group)
                .ToList()
        };
        var provider = new BlockingProxyConfigProvider(synced);
        var proxyPage = new ProxyPageViewModel(primaryConfigProvider: provider);
        proxyPage.LoadConfig(config);
        using var viewModel = CreateViewModel(proxyPage: proxyPage);

        viewModel.ShowProxyCommand.Execute(null);
        try
        {
            await provider.Started.Task.WaitAsync(AsyncTestTimeout);
            // 等待导航节流结束，确保切页触发取消链路。
            await Task.Delay(200);
            viewModel.ShowHomeCommand.Execute(null);
            await WaitUntilAsync(() => provider.CancellationObserved);
            provider.Release.TrySetResult();
            await Task.Delay(80);

            Assert.Equal(NavigationPage.Home, viewModel.CurrentPage);
            Assert.Equal("JP", viewModel.ProxyPage.SelectedGroup?.DisplaySelectionName);
            Assert.False(viewModel.ProxyPage.VisibleNodeRows.Single(row => row.Name == "KR").IsSelected);
        }
        finally
        {
            provider.Release.TrySetResult();
        }
    }

    [Fact(DisplayName = "Runtime tick refreshes only visible page and honors connection pause")]
    public async Task RuntimeTickRefreshesOnlyVisiblePageAndHonorsConnectionPause()
    {
        var core = new FakeProxyCoreClient
        {
            RuntimeStats = new CoreRuntimeStats(
                UploadSpeed: 10,
                DownloadSpeed: 20,
                UploadTotal: 100,
                DownloadTotal: 200,
                ConnectionCount: 2,
                Memory: 1024,
                HasTrafficRate: true),
            OutboundMode = OutboundMode.Global,
            Version = "v1.2.3",
            Connections =
            [
                new ConnectionInfo("c1", Metadata: new ConnectionMetadata(Network: "tcp", Host: "example.com", DestinationPort: "443"))
            ],
            TrafficRate = new CoreTrafficRate(UploadSpeed: 50, DownloadSpeed: 60)
        };
        using var viewModel = CreateViewModel(
            connectionPage: new ConnectionPageViewModel(core),
            homeProxyClient: core);

        viewModel.OnHomeRuntimeTick();
        await WaitUntilAsync(() => core.RuntimeStatsReadCount == 1
            && core.OutboundModeReadCount == 1
            && viewModel.HomePage.CoreVersionValueText == "v1.2.3");

        Assert.Equal("v1.2.3", viewModel.HomePage.CoreVersionValueText);
        Assert.Empty(viewModel.ConnectionPage.Connections);
        Assert.Equal(0, core.ConnectionReadCount);

        await Task.Delay(60);
        viewModel.CurrentPage = NavigationPage.Connections;
        viewModel.OnHomeRuntimeTick();
        await WaitUntilAsync(() => core.ConnectionReadCount == 1 && viewModel.ConnectionPage.Connections.Count == 1);

        Assert.Equal("c1", viewModel.ConnectionPage.Connections.Single().Id);

        viewModel.ConnectionPage.TogglePauseCommand.Execute(null);
        viewModel.OnHomeRuntimeTick();
        await Task.Delay(80);

        Assert.Equal(1, core.ConnectionReadCount);
        Assert.Equal(1, core.RuntimeStatsReadCount);
    }

    [Fact(DisplayName = "Core log batch flush keeps latest pending logs only")]
    public async Task CoreLogBatchFlushKeepsLatestPendingLogsOnly()
    {
        var coreManager = new FakeCoreManager();
        using var viewModel = CreateViewModel(coreManager: coreManager);

        for (var index = 1; index <= 6; index++)
        {
            coreManager.RaiseLog(new CoreLogMessage("INFO", $"log-{index}", DateTimeOffset.UnixEpoch.AddSeconds(index)));
        }

        await WaitUntilAsync(() => viewModel.CoreLogPage.TotalLogCount == 4);

        Assert.Equal(["log-3", "log-4", "log-5", "log-6"], viewModel.CoreLogPage.Logs.Select(log => log.Payload));
    }

    [Fact(DisplayName = "Core stopped clears pending core logs before delayed flush")]
    public async Task CoreStoppedClearsPendingCoreLogsBeforeDelayedFlush()
    {
        var coreManager = new FakeCoreManager();
        using var viewModel = CreateViewModel(coreManager: coreManager);

        coreManager.RaiseLog(new CoreLogMessage("ERROR", "stale", DateTimeOffset.UnixEpoch));
        coreManager.RaiseState(new CoreSnapshot(CoreState.Stopped, null, string.Empty, null));
        await Task.Delay(TimeSpan.FromMilliseconds(850));

        Assert.Empty(viewModel.CoreLogPage.Logs);
        Assert.False(viewModel.CoreLogPage.IsCoreRunning);
    }

    [Fact(DisplayName = "Disposing shell ignores delayed core events and keeps core ownership external")]
    public async Task DisposingShellIgnoresDelayedCoreEventsAndKeepsCoreOwnershipExternal()
    {
        var coreManager = new FakeCoreManager();
        var viewModel = CreateViewModel(coreManager: coreManager);

        coreManager.RaiseLog(new CoreLogMessage("INFO", "pending", DateTimeOffset.UnixEpoch));
        viewModel.Dispose();
        coreManager.RaiseLog(new CoreLogMessage("INFO", "late", DateTimeOffset.UnixEpoch));
        coreManager.RaiseState(new CoreSnapshot(CoreState.Stopped, null, string.Empty, null));
        await Task.Delay(TimeSpan.FromMilliseconds(850));

        Assert.Empty(viewModel.CoreLogPage.Logs);
        Assert.Equal(0, coreManager.DisposeCount);
    }

    [Fact(DisplayName = "Subscription update refreshes runtime only for current subscription")]
    public async Task SubscriptionUpdateRefreshesRuntimeOnlyForCurrentSubscription()
    {
        var subscriptionStore = new FakeSubscriptionStore([Subscription("current"), Subscription("background")]);
        var selectionStore = new FakeSubscriptionSelectionStore("current");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            subscriptionSelectionStore: selectionStore);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        using var viewModel = CreateViewModel(
            subscriptionPage: subscriptionPage,
            runtimeFallbackGenerator: CreateRuntimeFallbackGenerator(subscriptionStore, selectionStore, runtimeStore),
            runtimeStore: runtimeStore);

        subscriptionPage.ApplySubscriptionUpdateResult(new SubscriptionUpdateResult(["background"], []));

        Assert.Equal(0, runtimeStore.SaveCount);

        subscriptionPage.ApplySubscriptionUpdateResult(new SubscriptionUpdateResult(["current"], []));
        await WaitUntilAsync(() => runtimeStore.SaveCount == 1);

        Assert.Equal(["current"], runtimeStore.SavedSubscriptionIds);
    }

    [Fact(DisplayName = "Mixed port runtime apply reapplies enabled system proxy after core accepts config")]
    public async Task MixedPortRuntimeApplyReappliesEnabledSystemProxyAfterCoreAcceptsConfig()
    {
        var settings = new AppSettings { MixedPort = 7890 };
        var settingsStore = new FakeSettingsStore(settings);
        var systemProxy = new FakeSystemProxyService();
        var subscriptionStore = new FakeSubscriptionStore([Subscription("current")]);
        var selectionStore = new FakeSubscriptionSelectionStore("current");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var coreManager = new FakeCoreManager();
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            subscriptionSelectionStore: selectionStore);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        using var viewModel = CreateViewModel(
            subscriptionPage: subscriptionPage,
            runtimeFallbackGenerator: CreateRuntimeFallbackGenerator(subscriptionStore, selectionStore, runtimeStore),
            runtimeStore: runtimeStore,
            coreManager: coreManager,
            settingsStore: settingsStore,
            systemProxyService: systemProxy);
        viewModel.HomePage.IsSystemProxyEnabled = true;

        viewModel.CoreConfig.MixedPortText = "7891";
        await WaitUntilAsync(() => coreManager.ApplyRequests.Count == 1 && systemProxy.LastEnablePort == 7891);

        Assert.Equal(7891, systemProxy.EnableRequests.Last().Port);
        Assert.Equal("current", coreManager.ApplyRequests.Single().SubscriptionId);
    }

    [Fact(DisplayName = "Mixed port runtime apply converges to latest endpoint after rapid changes")]
    public async Task MixedPortRuntimeApplyConvergesToLatestEndpointAfterRapidChanges()
    {
        var firstApplyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstApply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new AppSettings { MixedPort = 7890 };
        var settingsStore = new FakeSettingsStore(settings);
        var systemProxy = new FakeSystemProxyService();
        var subscriptionStore = new FakeSubscriptionStore([Subscription("current")]);
        var selectionStore = new FakeSubscriptionSelectionStore("current");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var applyCount = 0;
        var coreManager = new FakeCoreManager
        {
            ApplyHandler = async _ =>
            {
                applyCount++;
                if (applyCount == 1)
                {
                    firstApplyStarted.SetResult();
                    await releaseFirstApply.Task.WaitAsync(AsyncTestTimeout);
                }

                return new CoreApplyConfigResult(CoreApplyMode.Reload, 100);
            }
        };
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            subscriptionSelectionStore: selectionStore);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        using var viewModel = CreateViewModel(
            subscriptionPage: subscriptionPage,
            runtimeFallbackGenerator: CreateRuntimeFallbackGenerator(subscriptionStore, selectionStore, runtimeStore),
            runtimeStore: runtimeStore,
            coreManager: coreManager,
            settingsStore: settingsStore,
            systemProxyService: systemProxy);
        viewModel.HomePage.IsSystemProxyEnabled = true;

        viewModel.CoreConfig.MixedPortText = "7891";
        await firstApplyStarted.Task.WaitAsync(AsyncTestTimeout);
        viewModel.CoreConfig.MixedPortText = "7892";
        releaseFirstApply.SetResult();
        await WaitUntilAsync(() => coreManager.ApplyRequests.Count == 2 && systemProxy.LastEnablePort == 7892);

        Assert.Equal(7892, systemProxy.EnableRequests.Last().Port);
        Assert.Contains("mixed-port: 7892", runtimeStore.SavedRuntimeConfigContents.Last(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Mixed port runtime apply updates empty runtime when no subscription is selected")]
    public async Task MixedPortRuntimeApplyUpdatesEmptyRuntimeWhenNoSubscriptionIsSelected()
    {
        var settings = new AppSettings { MixedPort = 7890 };
        var settingsStore = new FakeSettingsStore(settings);
        var systemProxy = new FakeSystemProxyService();
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var coreManager = new FakeCoreManager();
        using var viewModel = CreateViewModel(
            runtimeStore: runtimeStore,
            coreManager: coreManager,
            settingsStore: settingsStore,
            systemProxyService: systemProxy);
        viewModel.HomePage.IsSystemProxyEnabled = true;

        viewModel.CoreConfig.MixedPortText = "7891";
        await WaitUntilAsync(() => coreManager.ApplyRequests.Count == 1 && systemProxy.LastEnablePort == 7891);

        Assert.Equal(1, runtimeStore.SaveEmptyCount);
        Assert.Equal(string.Empty, coreManager.ApplyRequests.Single().SubscriptionId);
        Assert.Equal(7891, systemProxy.EnableRequests.Last().Port);
    }

    [Fact(DisplayName = "Override update refreshes runtime only when current subscription uses override")]
    public async Task OverrideUpdateRefreshesRuntimeOnlyWhenCurrentSubscriptionUsesOverride()
    {
        var subscriptionStore = new FakeSubscriptionStore(
        [
            Subscription("current") with { OverrideIds = ["used"] },
            Subscription("background") with { OverrideIds = ["unused"] }
        ]);
        var selectionStore = new FakeSubscriptionSelectionStore("current");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            subscriptionSelectionStore: selectionStore);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        var overridePage = new OverridePageViewModel();
        using var viewModel = CreateViewModel(
            subscriptionPage: subscriptionPage,
            overridePage: overridePage,
            runtimeFallbackGenerator: CreateRuntimeFallbackGenerator(subscriptionStore, selectionStore, runtimeStore));

        overridePage.ApplyOverrideUpdateResult(new OverrideUpdateResult(["unused"], []));

        Assert.Equal(0, runtimeStore.SaveCount);

        overridePage.ApplyOverrideUpdateResult(new OverrideUpdateResult(["used"], []));
        await WaitUntilAsync(() => runtimeStore.SaveCount == 1);

        Assert.Equal(["current"], runtimeStore.SavedSubscriptionIds);
    }

    [Fact(DisplayName = "Runtime generation failure reverts to empty config and clears current subscription")]
    public async Task RuntimeGenerationFailureRevertsToEmptyConfigAndClearsCurrentSubscription()
    {
        var subscriptionStore = new FakeSubscriptionStore([Subscription("broken")]);
        subscriptionStore.FailReadContentIds.Add("broken");
        var selectionStore = new FakeSubscriptionSelectionStore("broken");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            subscriptionSelectionStore: selectionStore);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        using var viewModel = CreateViewModel(
            subscriptionPage: subscriptionPage,
            runtimeFallbackGenerator: CreateRuntimeFallbackGenerator(subscriptionStore, selectionStore, runtimeStore),
            runtimeStore: runtimeStore);

        subscriptionPage.ApplySubscriptionUpdateResult(new SubscriptionUpdateResult(["broken"], []));
        await WaitUntilAsync(() => runtimeStore.SaveEmptyCount == 1);

        var broken = subscriptionStore.LoadSubscriptions().Single();
        Assert.Null(subscriptionPage.CurrentSubscriptionId);
        Assert.Null(selectionStore.GetCurrentSubscriptionId());
        Assert.Contains("Selected subscription content is missing or unreadable", broken.LastError, StringComparison.Ordinal);
        Assert.Equal("empty.runtime.yaml", runtimeStore.LastEmptyRuntimePath);
        Assert.Equal(0, runtimeStore.SaveCount);
    }

    [Fact(DisplayName = "Pending restart crash disables overrides and retries runtime before empty fallback")]
    public async Task PendingRestartCrashDisablesOverridesAndRetriesRuntimeBeforeEmptyFallback()
    {
        var subscriptionStore = new FakeSubscriptionStore([Subscription("current") with { OverrideIds = ["override-a"] }]);
        var selectionStore = new FakeSubscriptionSelectionStore("current");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var coreManager = new FakeCoreManager { ApplyMode = CoreApplyMode.Restart };
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            overrideSelectionUpdater: new SubscriptionOverrideSelectionUpdater(subscriptionStore),
            subscriptionSelectionStore: selectionStore);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        using var viewModel = CreateViewModel(
            subscriptionPage: subscriptionPage,
            runtimeFallbackGenerator: CreateRuntimeFallbackGenerator(subscriptionStore, selectionStore, runtimeStore),
            runtimeStore: runtimeStore,
            coreManager: coreManager);

        subscriptionPage.ApplySubscriptionUpdateResult(new SubscriptionUpdateResult(["current"], []));
        await WaitUntilAsync(() => coreManager.ApplyRequests.Count == 1);

        coreManager.RaiseState(new CoreSnapshot(CoreState.Crashed, 100, "127.0.0.1:9090", "override failed"));
        await WaitUntilAsync(() => runtimeStore.SaveCount == 2 && coreManager.ApplyRequests.Count == 2);

        Assert.Equal("current", subscriptionPage.CurrentSubscriptionId);
        Assert.Equal("current", selectionStore.GetCurrentSubscriptionId());
        Assert.Empty(subscriptionStore.LoadSubscriptions().Single().OverrideIds);
        Assert.Equal(0, runtimeStore.SaveEmptyCount);
        Assert.Equal(["current", "current"], runtimeStore.SavedSubscriptionIds);
        Assert.All(coreManager.ApplyRequests, request => Assert.Equal("current", request.SubscriptionId));
    }

    [Fact(DisplayName = "Pending restart crash without overrides reverts to empty runtime")]
    public async Task PendingRestartCrashWithoutOverridesRevertsToEmptyRuntime()
    {
        var subscriptionStore = new FakeSubscriptionStore([Subscription("current")]);
        var selectionStore = new FakeSubscriptionSelectionStore("current");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var coreManager = new FakeCoreManager { ApplyMode = CoreApplyMode.Restart };
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            subscriptionSelectionStore: selectionStore);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        using var viewModel = CreateViewModel(
            subscriptionPage: subscriptionPage,
            runtimeFallbackGenerator: CreateRuntimeFallbackGenerator(subscriptionStore, selectionStore, runtimeStore),
            runtimeStore: runtimeStore,
            coreManager: coreManager);

        subscriptionPage.ApplySubscriptionUpdateResult(new SubscriptionUpdateResult(["current"], []));
        await WaitUntilAsync(() => coreManager.ApplyRequests.Count == 1);

        coreManager.RaiseState(new CoreSnapshot(CoreState.Crashed, 100, "127.0.0.1:9090", "bad config"));
        await WaitUntilAsync(() => runtimeStore.SaveEmptyCount == 1 && coreManager.ApplyRequests.Count == 2);

        var current = subscriptionStore.LoadSubscriptions().Single();
        Assert.Null(subscriptionPage.CurrentSubscriptionId);
        Assert.Null(selectionStore.GetCurrentSubscriptionId());
        Assert.Contains("bad config", current.LastError, StringComparison.Ordinal);
        Assert.Equal("empty.runtime.yaml", runtimeStore.LastEmptyRuntimePath);
        Assert.Equal(new CoreApplyConfigRequest("empty.runtime.yaml", string.Empty), coreManager.ApplyRequests.Last());
    }

    [Fact(DisplayName = "Deleting last current subscription converges core to empty runtime")]
    public async Task DeletingLastCurrentSubscriptionConvergesCoreToEmptyRuntime()
    {
        var subscriptionStore = new FakeSubscriptionStore([Subscription("current")]);
        var selectionStore = new FakeSubscriptionSelectionStore("current");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var coreManager = new FakeCoreManager();
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            subscriptionDeleter: new SubscriptionDeleter(subscriptionStore, selectionStore),
            subscriptionSelectionStore: selectionStore);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        using var viewModel = CreateViewModel(
            subscriptionPage: subscriptionPage,
            runtimeStore: runtimeStore,
            coreManager: coreManager);

        subscriptionPage.ShowDeleteDialogCommand.Execute("current");
        subscriptionPage.ConfirmDeleteCommand.Execute(null);
        await WaitUntilAsync(() => runtimeStore.SaveEmptyCount == 1 && coreManager.ApplyRequests.Count == 1);

        Assert.Null(subscriptionPage.CurrentSubscriptionId);
        Assert.Null(selectionStore.GetCurrentSubscriptionId());
        Assert.Equal("empty.runtime.yaml", runtimeStore.LastEmptyRuntimePath);
        Assert.Equal(new CoreApplyConfigRequest("empty.runtime.yaml", string.Empty), Assert.Single(coreManager.ApplyRequests));
    }

    [Fact(DisplayName = "Current subscription edits refresh runtime config")]
    public async Task CurrentSubscriptionEditsRefreshRuntimeConfig()
    {
        var subscriptionStore = new FakeSubscriptionStore([Subscription("current")]);
        var selectionStore = new FakeSubscriptionSelectionStore("current");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            subscriptionSelectionStore: selectionStore);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        using var viewModel = CreateViewModel(
            subscriptionPage: subscriptionPage,
            runtimeFallbackGenerator: CreateRuntimeFallbackGenerator(subscriptionStore, selectionStore, runtimeStore),
            runtimeStore: runtimeStore);

        subscriptionPage.EditFileCommand.Execute("current");
        subscriptionPage.FileEditor.Content = "proxies: []\nproxy-groups: []\nrules: []\n";
        subscriptionPage.FileEditor.ConfirmCommand.Execute(null);
        await WaitUntilAsync(() => runtimeStore.SaveCount == 1);

        subscriptionPage.ShowEditDialogCommand.Execute("current");
        subscriptionPage.EditDialog.Name = "Current Changed";
        subscriptionPage.EditDialog.ConfirmCommand.Execute(null);
        await WaitUntilAsync(() => runtimeStore.SaveCount == 2);

        subscriptionPage.ShowChainProxyDialogCommand.Execute("current");
        await WaitUntilAsync(() => !subscriptionPage.ChainProxy.IsLoading);
        subscriptionPage.ChainProxy.SaveCommand.Execute(null);
        await WaitUntilAsync(() => runtimeStore.SaveCount == 3);

        Assert.Equal("proxies: []\nproxy-groups: []\nrules: []\n", subscriptionStore.ReadContent("current"));
        Assert.Equal("Current Changed", subscriptionStore.LoadSubscriptions().Single().Name);
        Assert.Equal(["current", "current", "current"], runtimeStore.SavedSubscriptionIds);
    }

    [Fact(DisplayName = "Provider sync refreshes proxy and rules without runtime refresh")]
    public async Task ProviderSyncRefreshesProxyAndRulesWithoutRuntimeRefresh()
    {
        var subscriptionStore = new FakeSubscriptionStore([Subscription("current")]);
        subscriptionStore.SaveContent("current", ProviderConfig("remote", "file"));
        var selectionStore = new FakeSubscriptionSelectionStore("current");
        var runtimeStore = new FakeSelectedSubscriptionRuntimeStore();
        var providerLoader = new SelectedSubscriptionProviderCatalogLoader(
            subscriptionStore,
            selectionStore,
            new SubscriptionProviderParser(),
            new FakeSubscriptionProviderSyncer());
        var subscriptionPage = new SubscriptionPageViewModel(
            subscriptionStore: subscriptionStore,
            subscriptionSelectionStore: selectionStore,
            providerCatalogLoader: providerLoader);
        subscriptionPage.LoadSubscriptions(subscriptionStore.LoadSubscriptions());
        var proxyProvider = new FakeProxyConfigProvider(SampleProxyConfig());
        var proxyPage = new ProxyPageViewModel(primaryConfigProvider: proxyProvider);
        var ruleSource = new FakeRuleConfigSource(
            """
            rules:
              - DOMAIN-SUFFIX,example.com,PROXY
            """);
        var rulePage = new RulePageViewModel(new RuleListLoader(ruleSource, new RuleParser(), () => true));
        using var viewModel = CreateViewModel(
            proxyPage: proxyPage,
            rulePage: rulePage,
            subscriptionPage: subscriptionPage,
            runtimeFallbackGenerator: CreateRuntimeFallbackGenerator(subscriptionStore, selectionStore, runtimeStore),
            runtimeStore: runtimeStore);

        await subscriptionPage.Provider.ShowAsync("current");
        await subscriptionPage.Provider.SyncProviderAsync("remote");
        await WaitUntilAsync(() => proxyProvider.LoadCount == 1 && ruleSource.ReadCount == 1);

        Assert.Equal(["remote"], subscriptionPage.Provider.SyncedProviderNames);
        Assert.True(rulePage.HasRequestedRefresh);
        Assert.Equal(["Select"], proxyPage.VisibleGroups.Select(group => group.Name));
        Assert.Equal(0, runtimeStore.SaveCount);
        Assert.Equal(0, runtimeStore.SaveEmptyCount);
    }

    private static MainWindowViewModel CreateViewModel(
        FakeLocalizationService? localization = null,
        ProxyPageViewModel? proxyPage = null,
        ConnectionPageViewModel? connectionPage = null,
        RulePageViewModel? rulePage = null,
        IProxyCoreClient? homeProxyClient = null,
        ICoreManager? coreManager = null,
        SubscriptionPageViewModel? subscriptionPage = null,
        OverridePageViewModel? overridePage = null,
        SelectedRuntimeFallbackGenerator? runtimeFallbackGenerator = null,
        ISelectedSubscriptionRuntimeStore? runtimeStore = null,
        FakeSettingsStore? settingsStore = null,
        ISystemProxyService? systemProxyService = null,
        IProcessPrivilegeProbe? processPrivilegeProbe = null,
        ServiceModeStatus? initialServiceModeStatus = null,
        IAppUpdateChecker? updateChecker = null)
    {
        var settings = settingsStore?.Load() ?? new AppSettings();
        return new MainWindowViewModel(
            settingsStore ?? new FakeSettingsStore(settings),
            localization ?? new FakeLocalizationService(),
            proxyPage: proxyPage,
            connectionPage: connectionPage,
            rulePage: rulePage,
            subscriptionPage: subscriptionPage,
            overridePage: overridePage,
            homeProxyClient: homeProxyClient,
            coreManager: coreManager,
            systemProxyService: systemProxyService,
            updateChecker: updateChecker,
            runtimeFallbackGenerator: runtimeFallbackGenerator,
            runtimeStore: runtimeStore,
            initialSettings: settings,
            processPrivilegeProbe: processPrivilegeProbe,
            initialServiceModeStatus: initialServiceModeStatus);
    }

    private static SelectedRuntimeFallbackGenerator CreateRuntimeFallbackGenerator(
        ISubscriptionStore subscriptionStore,
        ISubscriptionSelectionStore selectionStore,
        ISelectedSubscriptionRuntimeStore runtimeStore)
    {
        return new SelectedRuntimeFallbackGenerator(
            subscriptionStore,
            new SubscriptionOverrideSelectionUpdater(subscriptionStore),
            new SelectedSubscriptionRuntimeGenerator(
                subscriptionStore,
                selectionStore,
                new RuntimeConfigGenerator(),
                runtimeStore: runtimeStore));
    }

    private static Subscription Subscription(string id)
    {
        return new Subscription(id, id, $"https://sub.example/{id}.yaml", false, DateTimeOffset.UnixEpoch);
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow + AsyncTestTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate());
    }

    private static ProxyConfig SampleProxyConfig()
    {
        var nodes = new Dictionary<string, ProxyNode>(StringComparer.Ordinal)
        {
            ["JP"] = new("JP", "ss", Delay: 90, Server: "jp.example", Port: 443),
            ["KR"] = new("KR", "vmess", Delay: 60, Server: "kr.example", Port: 443)
        };
        var groups = new[]
        {
            new ProxyGroup("GLOBAL", ProxyGroupTypes.Select, "JP", ["JP", "KR"]),
            new ProxyGroup("Select", ProxyGroupTypes.Select, "JP", ["JP", "KR"])
        };
        return new ProxyConfig(groups, nodes, OutboundMode.Rule);
    }

    private static string ProviderConfig(string remoteProviderName, string fileProviderName)
    {
        return $"""
            proxy-providers:
              {remoteProviderName}:
                type: http
                path: ./{remoteProviderName}.yaml
                proxies:
                  - name: remote-node
              {fileProviderName}:
                type: file
                path: ./{fileProviderName}.yaml
                proxies:
                  - name: file-node
            """;
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

    private sealed class FakePrivilegeProbe(ProcessRunMode mode) : IProcessPrivilegeProbe
    {
        public ProcessRunMode Detect() => mode;
    }

    private sealed class FakeAppUpdateChecker(AppUpdateCheckResult result) : IAppUpdateChecker
    {
        public AppUpdateCheckResult CheckForUpdates() => result;
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.ZhHans;

        public AppLanguage EffectiveLanguage => CurrentLanguage;

        public event EventHandler? LanguageChanged;

        public void SetLanguage(AppLanguage language)
        {
            CurrentLanguage = language;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key)
        {
            return $"{key}:{CurrentLanguage}";
        }
    }

    private sealed class FakeProxyConfigProvider(ProxyConfig config) : IProxyConfigProvider
    {
        public int LoadCount { get; private set; }

        public Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(config);
        }
    }

    private sealed class FakeRuleConfigSource(string content) : IRuleConfigSource
    {
        public int ReadCount { get; private set; }

        public string ReadRuntimeConfig()
        {
            ReadCount++;
            return content;
        }
    }

    private sealed class FakeSystemProxyService : ISystemProxyService
    {
        private readonly object _gate = new();
        private readonly List<SystemProxyApplicationRequest> _enableRequests = [];

        public int EnableCount
        {
            get { lock (_gate) return _enableRequests.Count; }
        }

        public int? LastEnablePort
        {
            get { lock (_gate) return _enableRequests.LastOrDefault()?.Port; }
        }

        public IReadOnlyList<SystemProxyApplicationRequest> EnableRequests
        {
            get { lock (_gate) return [.. _enableRequests]; }
        }

        public SystemProxyOperationResult Enable(SystemProxyApplicationRequest request)
        {
            lock (_gate)
            {
                _enableRequests.Add(request);
            }

            return new SystemProxyOperationResult(true, "enabled");
        }

        public SystemProxyOperationResult Disable()
        {
            return new SystemProxyOperationResult(true, "disabled");
        }
    }

    private sealed class FakeSubscriptionProviderSyncer : ISubscriptionProviderSyncer
    {
        public List<string> SyncRequests { get; } = [];

        public Task SyncAsync(SubscriptionProvider provider, CancellationToken cancellationToken = default)
        {
            SyncRequests.Add(provider.Name);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingProxyConfigProvider(ProxyConfig config) : IProxyConfigProvider
    {
        private int _cancellationObserved;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved => Volatile.Read(ref _cancellationObserved) == 1;

        public async Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            using var registration = cancellationToken.Register(() => Volatile.Write(ref _cancellationObserved, 1));
            Started.TrySetResult();
            await Release.Task;
            return config;
        }
    }

    private sealed class FakeProxyCoreClient : IProxyCoreClient
    {
        public List<ProxyChangeRequest> ChangeRequests { get; } = [];
        public List<ConnectionCloseRequest> CloseRequests { get; } = [];
        public IReadOnlyList<ConnectionInfo> Connections { get; init; } = [];
        public CoreRuntimeStats? RuntimeStats { get; init; }
        public CoreTrafficRate? TrafficRate { get; init; }
        public OutboundMode? OutboundMode { get; init; }
        public string? Version { get; init; }
        public int ConnectionReadCount { get; private set; }
        public int RuntimeStatsReadCount { get; private set; }
        public int TrafficReadCount { get; private set; }
        public int OutboundModeReadCount { get; private set; }
        public int VersionReadCount { get; private set; }

        public Task<IReadOnlyList<ConnectionInfo>> GetConnectionsAsync(CancellationToken cancellationToken = default)
        {
            ConnectionReadCount++;
            return Task.FromResult(Connections);
        }

        public Task<bool> ChangeProxyAsync(ProxyChangeRequest request, CancellationToken cancellationToken = default)
        {
            ChangeRequests.Add(request);
            return Task.FromResult(true);
        }

        public Task<bool> ClearProxySelectionAsync(string groupName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CloseConnectionsAsync(ConnectionCloseRequest request, CancellationToken cancellationToken = default)
        {
            CloseRequests.Add(request);
            return Task.FromResult(true);
        }

        public Task<ProxyRuntimeSnapshot> GetProxiesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProxyRuntimeSnapshot([]));
        }

        public Task<OutboundMode?> GetOutboundModeAsync(CancellationToken cancellationToken = default)
        {
            OutboundModeReadCount++;
            return Task.FromResult(OutboundMode);
        }

        public Task<bool> SetOutboundModeAsync(OutboundMode mode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            VersionReadCount++;
            return Task.FromResult(Version);
        }

        public Task<CoreRuntimeStats?> GetRuntimeStatsAsync(CancellationToken cancellationToken = default)
        {
            RuntimeStatsReadCount++;
            return Task.FromResult(RuntimeStats);
        }

        public Task<CoreTrafficRate?> GetTrafficAsync(CancellationToken cancellationToken = default)
        {
            TrafficReadCount++;
            return Task.FromResult(TrafficRate);
        }
    }

    private sealed class FakeCoreManager : ICoreManager, IDisposable
    {
        public event EventHandler<CoreSnapshot>? StateChanged;

        public event EventHandler<CoreLogMessage>? CoreLogReceived;

        public List<CoreApplyConfigRequest> ApplyRequests { get; } = [];

        public int DisposeCount { get; private set; }

        public CoreApplyMode ApplyMode { get; init; } = CoreApplyMode.Reload;

        public Func<CoreApplyConfigRequest, Task<CoreApplyConfigResult>>? ApplyHandler { get; init; }

        public Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CoreSnapshot(CoreState.Running, 100, "127.0.0.1:9090", null));
        }

        public Task<CoreApplyConfigResult> ApplyConfigAsync(CoreApplyConfigRequest request, CancellationToken cancellationToken = default)
        {
            ApplyRequests.Add(request);
            if (ApplyHandler is not null)
            {
                return ApplyHandler(request);
            }

            return Task.FromResult(new CoreApplyConfigResult(ApplyMode, 100));
        }

        public Task RestartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCount++;
        }

        public void RaiseLog(CoreLogMessage message)
        {
            CoreLogReceived?.Invoke(this, message);
        }

        public void RaiseState(CoreSnapshot snapshot)
        {
            StateChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakeSubscriptionSelectionStore(string? initial = null) : ISubscriptionSelectionStore
    {
        private string? _currentSubscriptionId = initial;

        public string? GetCurrentSubscriptionId()
        {
            return _currentSubscriptionId;
        }

        public void SetCurrentSubscriptionId(string? subscriptionId)
        {
            _currentSubscriptionId = subscriptionId;
        }
    }

    private sealed class FakeSubscriptionStore(IReadOnlyList<Subscription> subscriptions) : ISubscriptionStore
    {
        private readonly List<Subscription> _subscriptions = subscriptions.ToList();
        private readonly Dictionary<string, string> _configs = subscriptions.ToDictionary(
            subscription => subscription.Id,
            _ => "proxies: []\nproxy-groups: []\nrules: []\n",
            StringComparer.Ordinal);

        public HashSet<string> FailReadContentIds { get; } = new(StringComparer.Ordinal);

        public void Save(Subscription subscription, string originalContent)
        {
            _subscriptions.Add(subscription);
            _configs[subscription.Id] = originalContent;
        }

        public void UpdateSubscription(Subscription subscription)
        {
            var index = _subscriptions.FindIndex(item => item.Id == subscription.Id);
            if (index >= 0)
            {
                _subscriptions[index] = subscription;
            }
        }

        public void SaveSubscriptions(IReadOnlyList<Subscription> subscriptions)
        {
            _subscriptions.Clear();
            _subscriptions.AddRange(subscriptions);
        }

        public void SaveContent(string subscriptionId, string originalContent)
        {
            _configs[subscriptionId] = originalContent;
        }

        public IReadOnlyList<Subscription> LoadSubscriptions()
        {
            return _subscriptions.ToList();
        }

        public string ReadContent(string subscriptionId)
        {
            if (FailReadContentIds.Contains(subscriptionId))
            {
                throw new IOException("config missing");
            }

            return _configs[subscriptionId];
        }

        public string GetContentPath(string subscriptionId)
        {
            return $"{subscriptionId}.yaml";
        }

        public void Delete(string subscriptionId)
        {
            _subscriptions.RemoveAll(item => item.Id == subscriptionId);
            _configs.Remove(subscriptionId);
        }
    }

    private sealed class FakeSelectedSubscriptionRuntimeStore : ISelectedSubscriptionRuntimeStore
    {
        public int SaveCount { get; private set; }

        public int SaveEmptyCount { get; private set; }

        public string? LastEmptyRuntimePath { get; private set; }

        public List<string> SavedSubscriptionIds { get; } = [];

        public List<string> SavedRuntimeConfigContents { get; } = [];

        public SelectedSubscriptionRuntimePaths Save(Subscription subscription, string originalContent, string runtimeConfigContent)
        {
            SaveCount++;
            SavedSubscriptionIds.Add(subscription.Id);
            SavedRuntimeConfigContents.Add(runtimeConfigContent);
            return new SelectedSubscriptionRuntimePaths($"{subscription.Id}.{SaveCount}.original.yaml", $"{subscription.Id}.{SaveCount}.runtime.yaml");
        }

        public string SaveEmpty(string runtimeConfigContent)
        {
            SaveEmptyCount++;
            SavedRuntimeConfigContents.Add(runtimeConfigContent);
            LastEmptyRuntimePath = "empty.runtime.yaml";
            return LastEmptyRuntimePath;
        }

        public string ReadRuntimeConfig(string subscriptionId)
        {
            return "proxies: []\nproxy-groups: []\nrules: []\n";
        }

        public void Delete(string subscriptionId)
        {
        }
    }
}
