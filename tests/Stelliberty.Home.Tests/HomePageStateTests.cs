using Stelliberty.Application.Connections;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Proxies;
using Stelliberty.Application.Runtime;
using Stelliberty.Domain.Connections;
using Stelliberty.Domain.Proxies;
using Stelliberty.Presentation.ViewModels;
using Xunit;

namespace Stelliberty.Home.Tests;

public sealed class HomePageStateTests
{
    private static readonly TimeSpan AsyncTestTimeout = TimeSpan.FromSeconds(5);

    [Fact(DisplayName = "System proxy toggle does not depend on core running")]
    public async Task SystemProxyToggleDoesNotDependOnCoreRunning()
    {
        var service = new FakeSystemProxyService();
        var viewModel = new HomePageViewModel(
            systemProxyService: service,
            systemProxyRequestFactory: Request);
        viewModel.ApplyCoreRunning(false);

        viewModel.IsSystemProxyEnabled = true;

        Assert.True(viewModel.IsSystemProxyEnabled);
        await WaitUntilAsync(() => service.EnableCount == 1);
        Assert.Equal(1, service.EnableCount);
    }

    [Fact(DisplayName = "System proxy toggle updates UI before platform apply")]
    public async Task SystemProxyToggleUpdatesUiBeforePlatformApply()
    {
        var service = new FakeSystemProxyService { BlockEnable = true };
        var viewModel = new HomePageViewModel(
            systemProxyService: service,
            systemProxyRequestFactory: Request);

        viewModel.IsSystemProxyEnabled = true;

        Assert.True(viewModel.IsSystemProxyEnabled);
        Assert.True(service.EnableStarted.Wait(AsyncTestTimeout));
        Assert.True(viewModel.IsSystemProxyEnabled);
        service.ReleaseEnable.Set();
        await WaitUntilAsync(() => service.EnableCount == 1);
    }

    [Fact(DisplayName = "System proxy failure rolls back latest optimistic state")]
    public async Task SystemProxyFailureRollsBackLatestOptimisticState()
    {
        var service = new FakeSystemProxyService
        {
            NextEnableSuccess = false,
            BlockDisable = true,
        };
        var viewModel = new HomePageViewModel(
            systemProxyService: service,
            systemProxyRequestFactory: Request);

        viewModel.IsSystemProxyEnabled = true;
        Assert.True(viewModel.IsSystemProxyEnabled);
        await WaitUntilAsync(() => !viewModel.IsSystemProxyEnabled);

        service.NextEnableSuccess = true;
        viewModel.IsSystemProxyEnabled = true;
        await WaitUntilAsync(() => viewModel.IsSystemProxyEnabled && service.EnableCount == 2);
        service.NextDisableSuccess = false;
        viewModel.IsSystemProxyEnabled = false;

        Assert.True(service.DisableStarted.Wait(AsyncTestTimeout));
        Assert.False(viewModel.IsSystemProxyEnabled);
        service.ReleaseDisable.Set();
        await WaitUntilAsync(() => viewModel.IsSystemProxyEnabled);
        Assert.True(viewModel.IsSystemProxyEnabled);
    }

    [Fact(DisplayName = "Stale system proxy failure does not rollback newer state")]
    public async Task StaleSystemProxyFailureDoesNotRollbackNewerState()
    {
        var service = new FakeSystemProxyService
        {
            BlockEnable = true,
            NextEnableSuccess = false
        };
        var viewModel = new HomePageViewModel(
            systemProxyService: service,
            systemProxyRequestFactory: Request);

        viewModel.IsSystemProxyEnabled = true;
        Assert.True(service.EnableStarted.Wait(AsyncTestTimeout));

        viewModel.IsSystemProxyEnabled = false;
        service.ReleaseEnable.Set();

        await WaitUntilAsync(() => service.DisableCount == 1);
        Assert.False(viewModel.IsSystemProxyEnabled);
    }

    [Fact(DisplayName = "Reapply system proxy settings only updates platform when enabled")]
    public async Task ReapplySystemProxySettingsOnlyUpdatesPlatformWhenEnabled()
    {
        var service = new FakeSystemProxyService();
        var port = 7890;
        var viewModel = new HomePageViewModel(
            systemProxyService: service,
            systemProxyRequestFactory: () => new SystemProxyApplicationRequest("127.0.0.1", port, [], false, null));

        viewModel.ReapplySystemProxySettings();

        Assert.Equal(0, service.EnableCount);

        viewModel.IsSystemProxyEnabled = true;
        await WaitUntilAsync(() => service.EnableCount == 1);
        port = 7891;
        viewModel.ReapplySystemProxySettings();
        await WaitUntilAsync(() => service.EnableCount == 2);

        Assert.True(viewModel.IsSystemProxyEnabled);
        Assert.Equal(2, service.EnableCount);
        Assert.Equal(7891, service.EnableRequests.Last().Port);
    }

    [Fact(DisplayName = "TUN toggle requires privilege and interactive core")]
    public async Task TunToggleRequiresPrivilegeAndInteractiveCore()
    {
        var normal = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, privilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Normal), tunStateChanged: _ => throw new InvalidOperationException());
        normal.ApplyCoreRunning(true);
        normal.IsTunEnabled = true;
        Assert.False(normal.IsTunEnabled);

        var changes = new List<bool>();
        var admin = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, privilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Administrator), tunStateChanged: changes.Add);
        admin.IsTunEnabled = true;
        Assert.False(admin.IsTunEnabled);

        admin.ApplyCoreRunning(true);
        admin.IsTunEnabled = true;
        Assert.True(admin.IsTunEnabled);
        await WaitUntilAsync(() => changes.Count == 1);
        Assert.Equal([true], changes);
    }

    [Fact(DisplayName = "TUN toggle updates UI before applying settings")]
    public async Task TunToggleUpdatesUiBeforeApplyingSettings()
    {
        var started = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var changes = new List<bool>();
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            privilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Administrator),
            tunStateChanged: state =>
            {
                started.Set();
                release.Wait(AsyncTestTimeout);
                changes.Add(state);
            });
        viewModel.ApplyCoreRunning(true);

        viewModel.IsTunEnabled = true;

        Assert.True(viewModel.IsTunEnabled);
        Assert.Empty(changes);
        Assert.True(started.Wait(AsyncTestTimeout));
        release.Set();
        await WaitUntilAsync(() => changes.Count == 1);
        Assert.Equal([true], changes);
    }

    [Fact(DisplayName = "TUN apply failure rolls back latest optimistic state")]
    public async Task TunApplyFailureRollsBackLatestOptimisticState()
    {
        using var started = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            privilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Administrator),
            tunStateChanged: _ =>
            {
                started.Set();
                release.Wait(AsyncTestTimeout);
                throw new InvalidOperationException("tun failed");
            });
        viewModel.ApplyCoreRunning(true);

        viewModel.IsTunEnabled = true;

        Assert.True(viewModel.IsTunEnabled);
        Assert.True(started.Wait(AsyncTestTimeout));
        release.Set();
        await WaitUntilAsync(() => !viewModel.IsTunEnabled);
    }

    [Fact(DisplayName = "Stale TUN failure does not rollback newer state")]
    public async Task StaleTunFailureDoesNotRollbackNewerState()
    {
        var firstStarted = new ManualResetEventSlim(false);
        var releaseFirst = new ManualResetEventSlim(false);
        var firstFinished = new ManualResetEventSlim(false);
        var trueCalls = 0;
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            privilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Administrator),
            tunStateChanged: state =>
            {
                if (state && Interlocked.Increment(ref trueCalls) == 1)
                {
                    firstStarted.Set();
                    releaseFirst.Wait(AsyncTestTimeout);
                    firstFinished.Set();
                    throw new InvalidOperationException("tun failed");
                }
            });
        viewModel.ApplyCoreRunning(true);

        viewModel.IsTunEnabled = true;
        Assert.True(firstStarted.Wait(AsyncTestTimeout));
        viewModel.IsTunEnabled = false;
        viewModel.IsTunEnabled = true;
        releaseFirst.Set();

        Assert.True(firstFinished.Wait(AsyncTestTimeout));
        await Task.Delay(50);
        Assert.True(viewModel.IsTunEnabled);
    }

    [Fact(DisplayName = "Apply TUN state bypasses user toggle guards without emitting callback")]
    public void ApplyTunStateBypassesUserToggleGuardsWithoutEmittingCallback()
    {
        var changes = new List<bool>();
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            privilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Normal),
            tunStateChanged: changes.Add);

        viewModel.ApplyTunState(true);
        viewModel.IsTunEnabled = false;

        Assert.True(viewModel.IsTunEnabled);
        Assert.False(viewModel.IsTunToggleEnabled);

        viewModel.ApplyTunState(false);

        Assert.False(viewModel.IsTunEnabled);
        Assert.Empty(changes);
    }

    [Fact(DisplayName = "Network connection display maps unknown Wi-Fi and disconnected state")]
    public void NetworkConnectionDisplayMapsUnknownWifiAndDisconnectedState()
    {
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request);

        viewModel.ApplyNetworkConnection(new NetworkConnectionInfo(NetworkConnectionType.Wifi, " "));

        Assert.True(viewModel.IsNetworkConnected);
        Assert.True(viewModel.IsWifiConnection);
        Assert.False(viewModel.IsWiredConnection);
        Assert.Equal("Wi-Fi", viewModel.NetworkTypeText);
        Assert.Equal("Home.Network.Unknown", viewModel.NetworkNameValueText);
        Assert.Equal("", viewModel.NetworkSignalTag);

        viewModel.ApplyNetworkConnection(NetworkConnectionInfo.Disconnected);

        Assert.False(viewModel.IsNetworkConnected);
        Assert.False(viewModel.IsWifiConnection);
        Assert.False(viewModel.IsWiredConnection);
        Assert.Equal("Home.Network.Disconnected", viewModel.NetworkTypeText);
        Assert.Equal("Home.Network.Disconnected", viewModel.NetworkNameValueText);
        Assert.Equal("danger", viewModel.NetworkSignalTag);
    }

    [Fact(DisplayName = "Outbound mode rolls back when core rejects change")]
    public void OutboundModeRollsBackWhenCoreRejectsChange()
    {
        var client = new FakeProxyCoreClient { SetOutboundModeResult = false };
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, proxyClient: client);
        viewModel.ApplyCoreRunning(true);

        viewModel.SetGlobalOutboundCommand.Execute(null);

        Assert.Equal(OutboundMode.Rule, viewModel.OutboundMode);
        Assert.Equal(OutboundMode.Global, client.LastOutboundMode);
    }

    [Fact(DisplayName = "Outbound mode stays changed when core accepts change")]
    public void OutboundModeStaysChangedWhenCoreAcceptsChange()
    {
        var client = new FakeProxyCoreClient { SetOutboundModeResult = true };
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, proxyClient: client);
        viewModel.ApplyCoreRunning(true);

        viewModel.SetGlobalOutboundCommand.Execute(null);

        Assert.Equal(OutboundMode.Global, viewModel.OutboundMode);
        Assert.Equal(OutboundMode.Global, client.LastOutboundMode);
    }

    [Fact(DisplayName = "Outbound mode does not change when core is not interactive")]
    public void OutboundModeDoesNotChangeWhenCoreIsNotInteractive()
    {
        var client = new FakeProxyCoreClient();
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, proxyClient: client);

        viewModel.SetGlobalOutboundCommand.Execute(null);

        Assert.Equal(OutboundMode.Rule, viewModel.OutboundMode);
        Assert.Null(client.LastOutboundMode);
    }

    [Fact(DisplayName = "Outbound mode stays changed without proxy client when core is interactive")]
    public void OutboundModeStaysChangedWithoutProxyClientWhenCoreIsInteractive()
    {
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request);
        viewModel.ApplyCoreRunning(true);

        viewModel.SetDirectOutboundCommand.Execute(null);

        Assert.Equal(OutboundMode.Direct, viewModel.OutboundMode);
        Assert.True(viewModel.IsDirectOutboundSelected);
    }

    [Fact(DisplayName = "Runtime refresh updates stats mode version and derived traffic rate")]
    public async Task RuntimeRefreshUpdatesStatsModeVersionAndDerivedTrafficRate()
    {
        var now = DateTimeOffset.UnixEpoch;
        var versionChanges = new List<string>();
        var client = new FakeProxyCoreClient
        {
            RuntimeStats = new CoreRuntimeStats(0, 0, 100, 200, 3, 1024, HasTrafficRate: true),
            OutboundModeResult = OutboundMode.Global,
            Version = "mihomo-1"
        };
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            proxyClient: client,
            proxyEndpointProvider: () => "127.0.0.1:7890",
            coreVersionChanged: versionChanges.Add,
            now: () => now);
        viewModel.ApplyCoreRunning(true);

        viewModel.RefreshRuntime();
        await WaitUntilAsync(() => viewModel.ActiveConnectionsValueText == "3");

        Assert.Equal(OutboundMode.Global, viewModel.OutboundMode);
        Assert.Equal("mihomo-1", viewModel.CoreVersionValueText);
        Assert.Equal(["mihomo-1"], versionChanges);
        Assert.Equal("1.0 KB", viewModel.MemoryValueText);
        Assert.Equal("127.0.0.1:7890", viewModel.ProxyAddressValueText);

        now = now.AddSeconds(1);
        client.RuntimeStats = new CoreRuntimeStats(0, 0, 150, 260, 1, 2048, HasTrafficRate: false);
        viewModel.RefreshRuntime();
        await WaitUntilAsync(() => viewModel.ActiveConnectionsValueText == "1");

        Assert.Equal("50 B/s", viewModel.UploadSpeedValueText);
        Assert.Equal("60 B/s", viewModel.DownloadSpeedValueText);
        Assert.Equal("150 B", viewModel.UploadTotalValueText);
        Assert.Equal("260 B", viewModel.DownloadTotalValueText);
        Assert.Equal(60, viewModel.SpeedAxisMax);

        viewModel.ResetTrafficCommand.Execute(null);

        Assert.Equal("0 B", viewModel.UploadTotalValueText);
        Assert.Equal("0 B", viewModel.DownloadTotalValueText);
        Assert.Equal(0, viewModel.SpeedAxisMax);
    }

    [Fact(DisplayName = "Core update invalidates cached version for next runtime refresh")]
    public async Task CoreUpdateInvalidatesCachedVersionForNextRuntimeRefresh()
    {
        var updater = new BlockingCoreUpdater();
        var client = new FakeProxyCoreClient
        {
            RuntimeStats = new CoreRuntimeStats(0, 0, 100, 200, 1, 1024, HasTrafficRate: true),
            OutboundModeResult = OutboundMode.Rule,
            Version = "mihomo-1"
        };
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, proxyClient: client, coreUpdater: updater);
        viewModel.ApplyCoreRunning(true);
        viewModel.RefreshRuntime();
        await WaitUntilAsync(() => viewModel.CoreVersionValueText == "mihomo-1");

        client.Version = "mihomo-2";
        client.RuntimeStats = client.RuntimeStats! with { ConnectionCount = 2 };
        viewModel.RefreshRuntime();
        await WaitUntilAsync(() => viewModel.ActiveConnectionsValueText == "2");

        Assert.Equal("mihomo-1", viewModel.CoreVersionValueText);
        Assert.Equal(1, client.VersionRequestCount);

        viewModel.RefreshCoreCommand.Execute(null);
        await updater.Started.Task.WaitAsync(AsyncTestTimeout);
        Assert.True(viewModel.IsCoreUpdating);
        updater.Release.TrySetResult(new CoreUpdateResult(CoreUpdateStatus.Updated, "mihomo-2", "updated"));
        await WaitUntilAsync(() => !viewModel.IsCoreUpdating);

        client.RuntimeStats = client.RuntimeStats! with { ConnectionCount = 3 };
        viewModel.RefreshRuntime();
        await WaitUntilAsync(() => viewModel.CoreVersionValueText == "mihomo-2");

        Assert.Equal(2, client.VersionRequestCount);
    }

    [Fact(DisplayName = "Core restart blocks duplicate requests until current restart completes")]
    public async Task CoreRestartBlocksDuplicateRequestsUntilCurrentRestartCompletes()
    {
        var restart = new BlockingCoreRestart();
        var toasts = new List<(string Message, ToastType Type)>();
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, coreRestart: restart.RestartAsync);
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);
        viewModel.ApplyCoreRunning(true);

        viewModel.RestartCoreCommand.Execute(null);
        await restart.Started.Task.WaitAsync(AsyncTestTimeout);
        viewModel.RestartCoreCommand.Execute(null);

        Assert.True(viewModel.IsCoreRestarting);
        Assert.False(viewModel.CanRestartCore);
        Assert.Equal(1, restart.RestartCount);

        restart.Release.TrySetResult();
        await WaitUntilAsync(() => !viewModel.IsCoreRestarting);

        Assert.True(viewModel.CanRestartCore);
        Assert.Equal(1, restart.RestartCount);
        Assert.Contains(toasts, toast => toast is { Message: "Home.Toast.CoreRestarted", Type: ToastType.Success });
    }

    [Fact(DisplayName = "Core restart reapplies enabled system proxy with latest endpoint")]
    public async Task CoreRestartReappliesEnabledSystemProxyWithLatestEndpoint()
    {
        var service = new FakeSystemProxyService();
        var restart = new BlockingCoreRestart();
        var port = 7890;
        var viewModel = new HomePageViewModel(
            systemProxyService: service,
            systemProxyRequestFactory: () => new SystemProxyApplicationRequest("127.0.0.1", port, [], false, null),
            coreRestart: restart.RestartAsync);
        viewModel.ApplyCoreRunning(true);
        viewModel.IsSystemProxyEnabled = true;
        await WaitUntilAsync(() => service.EnableCount == 1);

        port = 7891;
        viewModel.RestartCoreCommand.Execute(null);
        await restart.Started.Task.WaitAsync(AsyncTestTimeout);
        restart.Release.TrySetResult();
        await WaitUntilAsync(() => !viewModel.IsCoreRestarting);
        await WaitUntilAsync(() => service.EnableRequests.LastOrDefault()?.Port == 7891);

        Assert.Equal(7891, service.EnableRequests.Last().Port);
    }

    [Fact(DisplayName = "Terminal proxy command writes shell specific command only when core runs")]
    public void TerminalProxyCommandWritesShellSpecificCommandOnlyWhenCoreRuns()
    {
        var clipboard = new FakeClipboardWriter();
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            proxyEndpointProvider: () => "127.0.0.1:7890",
            clipboardWriter: clipboard);

        viewModel.CopyTerminalProxyCommand(TerminalShell.Bash);
        Assert.Empty(clipboard.Writes);

        viewModel.ApplyCoreRunning(true);
        viewModel.CopyTerminalProxyCommand(TerminalShell.PowerShell);
        viewModel.CopyTerminalProxyCommand(TerminalShell.Cmd);

        Assert.Equal("$env:http_proxy=\"http://127.0.0.1:7890\"; $env:https_proxy=\"http://127.0.0.1:7890\"", clipboard.Writes[0]);
        Assert.Equal("set http_proxy=http://127.0.0.1:7890 && set https_proxy=http://127.0.0.1:7890", clipboard.Writes[1]);
    }

    [Fact(DisplayName = "Terminal proxy command does not use unavailable display text as address")]
    public void TerminalProxyCommandDoesNotUseUnavailableDisplayTextAsAddress()
    {
        var clipboard = new FakeClipboardWriter();
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, clipboardWriter: clipboard);

        viewModel.ApplyCoreRunning(true);
        viewModel.CopyTerminalProxyCommand(TerminalShell.Bash);

        Assert.Equal("Home.Value.Unavailable", viewModel.ProxyAddressValueText);
        Assert.Empty(clipboard.Writes);
    }

    [Fact(DisplayName = "Core stopped clears runtime stats")]
    public async Task CoreStoppedClearsRuntimeStats()
    {
        var now = DateTimeOffset.UnixEpoch;
        var client = new FakeProxyCoreClient
        {
            RuntimeStats = new CoreRuntimeStats(UploadSpeed: 10, DownloadSpeed: 20, UploadTotal: 100, DownloadTotal: 200, ConnectionCount: 2, Memory: 1024, HasTrafficRate: true),
            OutboundModeResult = OutboundMode.Rule
        };
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, proxyClient: client, now: () => now);
        viewModel.ApplyCoreRunning(true);
        viewModel.RefreshRuntime();
        await WaitUntilAsync(() => viewModel.ActiveConnectionsValueText == "2");

        viewModel.ApplyCoreRunning(false);

        Assert.Equal("0 B/s", viewModel.UploadSpeedValueText);
        Assert.Equal("0 B/s", viewModel.DownloadSpeedValueText);
        Assert.Equal("0 B", viewModel.UploadTotalValueText);
        Assert.Equal("0 B", viewModel.DownloadTotalValueText);
        Assert.Equal("0", viewModel.ActiveConnectionsValueText);
        Assert.Equal("Home.Value.Unavailable", viewModel.MemoryValueText);
        Assert.Equal(0, viewModel.SpeedAxisMax);
        Assert.All(viewModel.UploadSamples, sample => Assert.Equal(0, sample));
        Assert.All(viewModel.DownloadSamples, sample => Assert.Equal(0, sample));
    }

    [Fact(DisplayName = "Service mode running sends heartbeat and unlocks TUN permission")]
    public async Task ServiceModeRunningSendsHeartbeatAndUnlocksTunPermission()
    {
        var manager = new FakeServiceModeManager
        {
            Status = new ServiceModeStatus(ServiceModeState.Running, "running")
        };
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            serviceModeManager: manager,
            isServiceModeCoreHostActive: () => true,
            privilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Normal));

        await viewModel.RefreshServiceModeAsync();

        Assert.Equal(ServiceModeState.Running, viewModel.ServiceModeState);
        Assert.True(viewModel.CanToggleTun);
        Assert.True(manager.HeartbeatCount >= 1);
        Assert.Equal("service", viewModel.CoreHostMode);
    }

    [Fact(DisplayName = "Service mode status does not unlock TUN while current core is normal")]
    public async Task ServiceModeStatusDoesNotUnlockTunWhileCurrentCoreIsNormal()
    {
        var manager = new FakeServiceModeManager
        {
            Status = new ServiceModeStatus(ServiceModeState.Running, "running")
        };
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            serviceModeManager: manager,
            isServiceModeCoreHostActive: () => false,
            privilegeProbe: new FakePrivilegeProbe(ProcessRunMode.Normal));

        await viewModel.RefreshServiceModeAsync();

        Assert.Equal(ServiceModeState.Running, viewModel.ServiceModeState);
        Assert.False(viewModel.CanToggleTun);
        Assert.Equal("process", viewModel.CoreHostMode);
    }

    [Fact(DisplayName = "Service mode install blocks concurrent operation and refreshes status after completion")]
    public async Task ServiceModeInstallBlocksConcurrentOperationAndRefreshesStatusAfterCompletion()
    {
        var manager = new FakeServiceModeManager
        {
            Status = new ServiceModeStatus(ServiceModeState.NotInstalled, "not installed"),
            BlockInstall = true
        };
        var toasts = new List<(string Message, ToastType Type)>();
        var viewModel = new HomePageViewModel(systemProxyService: new FakeSystemProxyService(), systemProxyRequestFactory: Request, serviceModeManager: manager);
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);
        await viewModel.RefreshServiceModeAsync();

        var installTask = viewModel.InstallOrUpdateServiceModeAsync();
        await manager.InstallStarted.Task.WaitAsync(AsyncTestTimeout);
        var duplicate = await viewModel.UninstallServiceModeAsync();

        Assert.True(viewModel.IsServiceModeBusy);
        Assert.False(viewModel.CanToggleServiceMode);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(1, manager.InstallCount);
        Assert.Equal(0, manager.UninstallCount);

        manager.Status = new ServiceModeStatus(ServiceModeState.Stopped, "installed");
        manager.ReleaseInstall.TrySetResult(ServiceModeOperationResult.Success("installed"));
        var result = await installTask.WaitAsync(AsyncTestTimeout);

        Assert.True(result.IsSuccess);
        Assert.False(viewModel.IsServiceModeBusy);
        Assert.True(viewModel.CanToggleServiceMode);
        Assert.Equal(ServiceModeState.Stopped, viewModel.ServiceModeState);
        Assert.Equal("installed", viewModel.ServiceModeMessage);
        Assert.Contains(toasts, toast => toast is { Message: "Home.Toast.ServiceModeInstallSucceeded", Type: ToastType.Success });
    }

    [Fact(DisplayName = "Service mode install activates the current session without restart")]
    public async Task ServiceModeInstallActivatesCurrentSessionWithoutRestart()
    {
        var manager = new FakeServiceModeManager
        {
            Status = new ServiceModeStatus(ServiceModeState.Running, "running")
        };
        var activationCount = 0;
        var toasts = new List<(string Message, ToastType Type)>();
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            serviceModeManager: manager,
            serviceModeSessionActivator: _ =>
            {
                activationCount++;
                return Task.FromResult(ServiceModeOperationResult.Success("active"));
            });
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

        var result = await viewModel.InstallOrUpdateServiceModeAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.RequiresRestart);
        Assert.Equal(1, activationCount);
        Assert.Contains(toasts, toast => toast is { Message: "Home.Toast.ServiceModeInstallSucceeded", Type: ToastType.Success });
    }

    [Fact(DisplayName = "Installed service reports session activation failure separately")]
    public async Task InstalledServiceReportsSessionActivationFailureSeparately()
    {
        var manager = new FakeServiceModeManager
        {
            Status = new ServiceModeStatus(ServiceModeState.Running, "running")
        };
        var toasts = new List<(string Message, ToastType Type)>();
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            serviceModeManager: manager,
            serviceModeSessionActivator: _ => Task.FromResult(ServiceModeOperationResult.Failed("switch failed")));
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

        var result = await viewModel.InstallOrUpdateServiceModeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("switch failed", result.Message);
        Assert.Contains(toasts, toast => toast is { Message: "Home.Toast.ServiceModeActivationFailed", Type: ToastType.Warning });
        Assert.DoesNotContain(toasts, toast => toast.Message == "Home.Toast.ServiceModeInstallFailed");
    }

    [Fact(DisplayName = "Installed service update reactivates the current service session")]
    public async Task InstalledServiceUpdateReactivatesCurrentServiceSession()
    {
        var manager = new FakeServiceModeManager
        {
            Status = new ServiceModeStatus(ServiceModeState.Running, "running")
        };
        var activationCount = 0;
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            serviceModeManager: manager,
            initialServiceModeStatus: manager.Status,
            serviceModeSessionActivator: _ =>
            {
                activationCount++;
                return Task.FromResult(ServiceModeOperationResult.Success("active"));
            });

        var result = await viewModel.InstallOrUpdateServiceModeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, activationCount);
    }

    [Fact(DisplayName = "Service mode uninstall restores normal session without restart")]
    public async Task ServiceModeUninstallRestoresNormalSessionWithoutRestart()
    {
        var manager = new FakeServiceModeManager
        {
            Status = new ServiceModeStatus(ServiceModeState.NotInstalled, "not installed"),
            UninstallResult = ServiceModeOperationResult.Success("uninstalled")
        };
        var deactivationCount = 0;
        var toasts = new List<(string Message, ToastType Type)>();
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            serviceModeManager: manager,
            isServiceModeCoreHostActive: () => true,
            serviceModeSessionDeactivator: _ =>
            {
                deactivationCount++;
                return Task.FromResult(ServiceModeOperationResult.Success("normal mode active"));
            });
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

        var result = await viewModel.UninstallServiceModeAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.RequiresRestart);
        Assert.Equal(1, deactivationCount);
        Assert.Contains(toasts, toast => toast is { Message: "Home.Toast.ServiceModeUninstallSucceeded", Type: ToastType.Success });
    }

    [Fact(DisplayName = "Uninstalled service reports normal session recovery failure separately")]
    public async Task UninstalledServiceReportsNormalSessionRecoveryFailureSeparately()
    {
        var manager = new FakeServiceModeManager
        {
            Status = new ServiceModeStatus(ServiceModeState.NotInstalled, "not installed"),
            UninstallResult = ServiceModeOperationResult.Success("uninstalled")
        };
        var toasts = new List<(string Message, ToastType Type)>();
        var viewModel = new HomePageViewModel(
            systemProxyService: new FakeSystemProxyService(),
            systemProxyRequestFactory: Request,
            serviceModeManager: manager,
            isServiceModeCoreHostActive: () => true,
            serviceModeSessionDeactivator: _ => Task.FromResult(ServiceModeOperationResult.Failed("resume failed")));
        viewModel.ToastRequested += (_, toast) => toasts.Add(toast);

        var result = await viewModel.UninstallServiceModeAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains(toasts, toast => toast is { Message: "Home.Toast.ServiceModeSessionRecoveryFailed", Type: ToastType.Warning });
        Assert.DoesNotContain(toasts, toast => toast.Message == "Home.Toast.ServiceModeUninstallFailed");
    }

    private static SystemProxyApplicationRequest Request()
    {
        return new SystemProxyApplicationRequest("127.0.0.1", 7890, [], false, null);
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

    private sealed class FakeSystemProxyService : ISystemProxyController
    {
        private readonly object _gate = new();
        private readonly List<SystemProxyApplicationRequest> _enableRequests = [];
        private int _enableCount;
        private int _disableCount;
        private SystemProxyStatus _status = new(false, false);

        public event EventHandler<SystemProxyStatus>? StatusChanged;

        public bool NextEnableSuccess { get; set; } = true;
        public bool NextDisableSuccess { get; set; } = true;
        public bool BlockEnable { get; init; }
        public bool BlockDisable { get; init; }
        public ManualResetEventSlim EnableStarted { get; } = new(false);
        public ManualResetEventSlim ReleaseEnable { get; } = new(false);
        public ManualResetEventSlim DisableStarted { get; } = new(false);
        public ManualResetEventSlim ReleaseDisable { get; } = new(false);
        public int EnableCount
        {
            get { lock (_gate) return _enableCount; }
        }
        public int DisableCount
        {
            get { lock (_gate) return _disableCount; }
        }
        public IReadOnlyList<SystemProxyApplicationRequest> EnableRequests
        {
            get { lock (_gate) return [.. _enableRequests]; }
        }

        public Task<SystemProxyStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_status);
        }

        public Task<SystemProxyApplyResult> SetEnabledAsync(
            bool isEnabled,
            SystemProxyApplicationRequest? request,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Apply(isEnabled, request), cancellationToken);
        }

        private SystemProxyApplyResult Apply(bool isEnabled, SystemProxyApplicationRequest? request)
        {
            if (isEnabled)
            {
                lock (_gate)
                {
                    _enableRequests.Add(request!);
                    _enableCount++;
                }

                EnableStarted.Set();
                if (BlockEnable)
                {
                    ReleaseEnable.Wait(AsyncTestTimeout);
                }

                if (NextEnableSuccess)
                {
                    _status = new SystemProxyStatus(true, true);
                    StatusChanged?.Invoke(this, _status);
                }

                return new SystemProxyApplyResult(
                    NextEnableSuccess,
                    NextEnableSuccess ? "enabled" : "failed",
                    _status);
            }

            lock (_gate)
            {
                _disableCount++;
            }

            DisableStarted.Set();
            if (BlockDisable)
            {
                ReleaseDisable.Wait(AsyncTestTimeout);
            }

            if (NextDisableSuccess)
            {
                _status = new SystemProxyStatus(false, false);
                StatusChanged?.Invoke(this, _status);
            }

            return new SystemProxyApplyResult(
                NextDisableSuccess,
                NextDisableSuccess ? "disabled" : "failed",
                _status);
        }
    }

    private sealed class FakePrivilegeProbe(ProcessRunMode mode) : IProcessPrivilegeProbe
    {
        public ProcessRunMode Detect() => mode;
    }

    private sealed class FakeProxyCoreClient : IProxyCoreClient
    {
        public bool SetOutboundModeResult { get; init; } = true;
        public OutboundMode? LastOutboundMode { get; private set; }
        public CoreRuntimeStats? RuntimeStats { get; set; }
        public OutboundMode? OutboundModeResult { get; set; }
        public string? Version { get; set; }
        public int VersionRequestCount { get; private set; }

        public Task<IReadOnlyList<ConnectionInfo>?> GetConnectionsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ConnectionInfo>?>([]);
        }

        public Task<bool> ChangeProxyAsync(ProxyChangeRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> ClearProxySelectionAsync(string groupName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> CloseConnectionsAsync(ConnectionCloseRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<ProxyRuntimeSnapshot> GetProxiesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProxyRuntimeSnapshot([]));
        }

        public Task<OutboundMode?> GetOutboundModeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OutboundModeResult);
        }

        public Task<bool> SetOutboundModeAsync(OutboundMode mode, CancellationToken cancellationToken = default)
        {
            LastOutboundMode = mode;
            return Task.FromResult(SetOutboundModeResult);
        }

        public Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            VersionRequestCount++;
            return Task.FromResult(Version);
        }

        public Task<CoreRuntimeStats?> GetRuntimeStatsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RuntimeStats);
        }

        public Task<CoreTrafficRate?> GetTrafficAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<CoreTrafficRate?>(null);
        }
    }

    private sealed class BlockingCoreUpdater : ICoreUpdater
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<CoreUpdateResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CoreUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class BlockingCoreRestart
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RestartCount { get; private set; }

        public Task RestartAsync()
        {
            RestartCount++;
            Started.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class FakeClipboardWriter : IClipboardWriter
    {
        public List<string> Writes { get; } = [];

        public void WriteText(string text)
        {
            Writes.Add(text);
        }
    }

    private sealed class FakeServiceModeManager : IServiceModeManager
    {
        public ServiceModeStatus Status { get; set; } = ServiceModeStatus.Unavailable("");
        public bool BlockInstall { get; set; }
        public int HeartbeatCount { get; private set; }
        public int InstallCount { get; private set; }
        public int UninstallCount { get; private set; }
        public ServiceModeOperationResult UninstallResult { get; set; } = ServiceModeOperationResult.Success("uninstalled");
        public TaskCompletionSource InstallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ServiceModeOperationResult> ReleaseInstall { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ServiceModeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Status);

        public async Task<ServiceModeOperationResult> InstallOrUpdateAsync(CancellationToken cancellationToken = default)
        {
            InstallCount++;
            InstallStarted.TrySetResult();
            if (!BlockInstall)
            {
                return ServiceModeOperationResult.Success("installed");
            }

            return await ReleaseInstall.Task.WaitAsync(cancellationToken);
        }

        public Task<ServiceModeOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
        {
            UninstallCount++;
            return Task.FromResult(UninstallResult);
        }

        public Task<ServiceModeOperationResult> StartCoreHostAsync(ServiceModeCoreHostRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceModeOperationResult.Success("started"));

        public Task<ServiceModeOperationResult> StopCoreHostAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceModeOperationResult.Success("stopped"));

        public Task<ServiceModeOperationResult> RestartCoreHostAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceModeOperationResult.Success("restarted"));

        public Task<ServiceModeOperationResult> SendHeartbeatAsync(CancellationToken cancellationToken = default)
        {
            HeartbeatCount++;
            return Task.FromResult(ServiceModeOperationResult.Success("heartbeat"));
        }
    }
}
