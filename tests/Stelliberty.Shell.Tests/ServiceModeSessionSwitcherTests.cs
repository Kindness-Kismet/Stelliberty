using Stelliberty.Application.CoreLogs;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Rules;
using Stelliberty.Application.Runtime;
using Stelliberty.Desktop;
using Stelliberty.Infrastructure.Core;
using Stelliberty.Domain.CoreLogs;
using Xunit;

namespace Stelliberty.Shell.Tests;

public sealed class ServiceModeSessionSwitcherTests
{
    private static readonly TimeSpan AsyncTimeout = TimeSpan.FromSeconds(5);

    [Fact(DisplayName = "Startup rule refresh contains source failure")]
    public void StartupRuleRefreshContainsSourceFailure()
    {
        var page = new Stelliberty.Presentation.ViewModels.RulePageViewModel(
            new RuleListLoader(new ThrowingRuleConfigSource(), new RuleParser()));

        App.RefreshRulesForStartup(page);

        Assert.True(page.HasRequestedRefresh);
        Assert.Empty(page.Rules);
    }

    [Fact(DisplayName = "Service session switch stops normal core before service core takes over")]
    public async Task ServiceSessionSwitchStopsNormalCoreBeforeServiceCoreTakesOver()
    {
        var order = new List<string>();
        var normal = new FakeCoreManager(RunningSnapshot(10));
        var service = new FakeCoreManager(RunningSnapshot(20), _ =>
        {
            order.Add("ready-service");
            return Task.FromResult(RunningSnapshot(20));
        });
        using var coreManager = new SwitchableCoreManager(normal);
        var serviceMode = new FakeServiceModeManager();
        var isServiceCoreActive = false;
        var switcher = CreateSwitcher(
            serviceMode,
            coreManager,
            service,
            order,
            resumeNormalCore: _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            setActive: value => isServiceCoreActive = value);

        var result = await switcher.ActivateAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["stop-normal", "start-service", "ready-service"], order);
        Assert.True(isServiceCoreActive);
        Assert.Equal(1, normal.DisposeCount);
        Assert.Equal(0, serviceMode.StopCoreCount);
        Assert.Equal(20, (await coreManager.GetSnapshotAsync()).Pid);
    }

    [Fact(DisplayName = "Service startup failure stops service core and restores normal core")]
    public async Task ServiceStartupFailureStopsServiceCoreAndRestoresNormalCore()
    {
        var order = new List<string>();
        var normal = new FakeCoreManager(RunningSnapshot(10));
        var recoveredNormal = new FakeCoreManager(RunningSnapshot(10), _ =>
        {
            order.Add("ready-normal");
            return Task.FromResult(RunningSnapshot(10));
        });
        using var coreManager = new SwitchableCoreManager(normal);
        var serviceMode = new FakeServiceModeManager();
        var resumeCount = 0;
        var isServiceCoreActive = true;
        var switcher = new ServiceModeSessionSwitcher(
            serviceMode,
            coreManager,
            _ => new FakeCoreManager(RunningSnapshot(20)),
            () => recoveredNormal,
            _ =>
            {
                order.Add("stop-normal");
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            _ =>
            {
                resumeCount++;
                order.Add("resume-normal");
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            (_, _) =>
            {
                order.Add("start-service");
                return Task.FromResult(CoreHostOperationResult.Failure("start failed"));
            },
            value => isServiceCoreActive = value);

        var result = await switcher.ActivateAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("start failed", result.Message);
        Assert.Equal(1, serviceMode.StopCoreCount);
        Assert.Equal(1, resumeCount);
        Assert.False(isServiceCoreActive);
        Assert.Equal(1, normal.DisposeCount);
        Assert.Equal(0, recoveredNormal.DisposeCount);
        Assert.Equal(["stop-normal", "start-service", "resume-normal", "ready-normal"], order);
    }

    [Fact(DisplayName = "Canceled service switch rolls back after normal core stops")]
    public async Task CanceledServiceSwitchRollsBackAfterNormalCoreStops()
    {
        using var cancellation = new CancellationTokenSource();
        var normal = new FakeCoreManager(RunningSnapshot(10));
        using var coreManager = new SwitchableCoreManager(normal);
        var serviceMode = new FakeServiceModeManager();
        var resumeCount = 0;
        var switcher = new ServiceModeSessionSwitcher(
            serviceMode,
            coreManager,
            _ => new FakeCoreManager(RunningSnapshot(20)),
            () => new FakeCoreManager(RunningSnapshot(10)),
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ =>
            {
                resumeCount++;
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            _ => { });

        var result = await switcher.ActivateAsync(cancellation.Token);

        Assert.True(result.IsCanceled);
        Assert.Equal(1, serviceMode.StopCoreCount);
        Assert.Equal(1, resumeCount);
        Assert.Equal(1, normal.DisposeCount);
    }

    [Fact(DisplayName = "Normal core stop failure still restores a verified normal session")]
    public async Task NormalCoreStopFailureStillRestoresVerifiedNormalSession()
    {
        var normal = new FakeCoreManager(RunningSnapshot(10));
        var recoveredNormal = new FakeCoreManager(RunningSnapshot(11));
        using var coreManager = new SwitchableCoreManager(normal);
        var serviceMode = new FakeServiceModeManager();
        var resumeCount = 0;
        var startCount = 0;
        var switcher = new ServiceModeSessionSwitcher(
            serviceMode,
            coreManager,
            _ => new FakeCoreManager(RunningSnapshot(20)),
            () => recoveredNormal,
            _ => Task.FromResult(CoreHostOperationResult.Failure("stop failed")),
            _ =>
            {
                resumeCount++;
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            (_, _) =>
            {
                startCount++;
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            _ => { });

        var result = await switcher.ActivateAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("stop failed", result.Message);
        Assert.Equal(1, serviceMode.StopCoreCount);
        Assert.Equal(1, resumeCount);
        Assert.Equal(0, startCount);
        Assert.Equal(1, normal.DisposeCount);
        Assert.Equal(11, (await coreManager.GetSnapshotAsync()).Pid);
    }

    [Fact(DisplayName = "Rollback never resumes normal core while service core stop is unconfirmed")]
    public async Task RollbackNeverResumesNormalCoreWhileServiceCoreStopIsUnconfirmed()
    {
        var normal = new FakeCoreManager(RunningSnapshot(10));
        using var coreManager = new SwitchableCoreManager(normal);
        var serviceMode = new FakeServiceModeManager
        {
            StopCoreResult = ServiceModeOperationResult.Failed("service stop failed")
        };
        var resumeCount = 0;
        var isServiceCoreActive = true;
        var switcher = new ServiceModeSessionSwitcher(
            serviceMode,
            coreManager,
            _ => new FakeCoreManager(RunningSnapshot(20)),
            () => new FakeCoreManager(RunningSnapshot(10)),
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ =>
            {
                resumeCount++;
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            (_, _) => Task.FromResult(CoreHostOperationResult.Failure("start failed")),
            value => isServiceCoreActive = value);

        var result = await switcher.ActivateAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Normal-mode recovery was skipped", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, resumeCount);
        Assert.True(isServiceCoreActive);
        Assert.Equal(0, normal.DisposeCount);
    }

    [Fact(DisplayName = "Service session transition blocks concurrent core operations")]
    public async Task ServiceSessionTransitionBlocksConcurrentCoreOperations()
    {
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normal = new FakeCoreManager(RunningSnapshot(10));
        var service = new FakeCoreManager(RunningSnapshot(20));
        using var coreManager = new SwitchableCoreManager(normal);
        var switcher = new ServiceModeSessionSwitcher(
            new FakeServiceModeManager(),
            coreManager,
            _ => service,
            () => new FakeCoreManager(RunningSnapshot(10)),
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            async (_, token) =>
            {
                startEntered.TrySetResult();
                await releaseStart.Task.WaitAsync(token);
                return CoreHostOperationResult.Success("ok");
            },
            _ => { });

        var activation = switcher.ActivateAsync(CancellationToken.None);
        await startEntered.Task.WaitAsync(AsyncTimeout);
        var concurrentSnapshot = coreManager.GetSnapshotAsync();

        await Task.Delay(50);
        Assert.False(concurrentSnapshot.IsCompleted);

        releaseStart.TrySetResult();
        Assert.True((await activation.WaitAsync(AsyncTimeout)).IsSuccess);
        Assert.Equal(20, (await concurrentSnapshot.WaitAsync(AsyncTimeout)).Pid);
    }

    [Fact(DisplayName = "Uninstalled service session switches directly back to normal core")]
    public async Task UninstalledServiceSessionSwitchesDirectlyBackToNormalCore()
    {
        var service = new FakeCoreManager(RunningSnapshot(20));
        var normal = new FakeCoreManager(RunningSnapshot(10));
        using var coreManager = new SwitchableCoreManager(service);
        var serviceMode = new FakeServiceModeManager();
        var isServiceCoreActive = true;
        var switcher = new ServiceModeSessionSwitcher(
            serviceMode,
            coreManager,
            _ => new FakeCoreManager(RunningSnapshot(20)),
            () => normal,
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            (_, _) => Task.FromResult(CoreHostOperationResult.Success("ok")),
            value => isServiceCoreActive = value);

        var result = await switcher.DeactivateAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(isServiceCoreActive);
        Assert.Equal(0, serviceMode.StopCoreCount);
        Assert.Equal(1, service.DisposeCount);
        Assert.Equal(10, (await coreManager.GetSnapshotAsync()).Pid);
    }

    [Fact(DisplayName = "Failed normal recovery preserves the service session marker for retry")]
    public async Task FailedNormalRecoveryPreservesServiceSessionMarkerForRetry()
    {
        var service = new FakeCoreManager(RunningSnapshot(20));
        using var coreManager = new SwitchableCoreManager(service);
        var isServiceCoreActive = true;
        var switcher = new ServiceModeSessionSwitcher(
            new FakeServiceModeManager(),
            coreManager,
            _ => new FakeCoreManager(RunningSnapshot(20)),
            () => new FakeCoreManager(RunningSnapshot(10)),
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ => Task.FromResult(CoreHostOperationResult.Failure("resume failed")),
            (_, _) => Task.FromResult(CoreHostOperationResult.Success("ok")),
            value => isServiceCoreActive = value);

        var result = await switcher.DeactivateAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(isServiceCoreActive);
        Assert.Equal(0, service.DisposeCount);
        Assert.Equal(20, (await coreManager.GetSnapshotAsync()).Pid);
    }

    [Fact(DisplayName = "Started normal core keeps ownership when its first readiness probe fails")]
    public async Task StartedNormalCoreKeepsOwnershipWhenFirstReadinessProbeFails()
    {
        var service = new FakeCoreManager(RunningSnapshot(20));
        var normal = new FakeCoreManager(
            RunningSnapshot(10),
            _ => throw new InvalidOperationException("normal unavailable"));
        using var coreManager = new SwitchableCoreManager(service);
        var isServiceCoreActive = true;
        var switcher = new ServiceModeSessionSwitcher(
            new FakeServiceModeManager(),
            coreManager,
            _ => new FakeCoreManager(RunningSnapshot(20)),
            () => normal,
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            (_, _) => Task.FromResult(CoreHostOperationResult.Success("ok")),
            value => isServiceCoreActive = value);

        var result = await switcher.DeactivateAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(isServiceCoreActive);
        Assert.Equal(1, service.DisposeCount);
        Assert.Equal(0, normal.DisposeCount);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coreManager.GetSnapshotAsync());
        Assert.Equal("normal unavailable", exception.Message);
    }

    [Fact(DisplayName = "Normal manager switch completes after uninstall cancellation arrives")]
    public async Task NormalManagerSwitchCompletesAfterUninstallCancellationArrives()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new FakeCoreManager(RunningSnapshot(20));
        var normal = new FakeCoreManager(RunningSnapshot(10), token =>
        {
            Assert.False(token.CanBeCanceled);
            return Task.FromResult(RunningSnapshot(10));
        });
        using var coreManager = new SwitchableCoreManager(service);
        var switcher = new ServiceModeSessionSwitcher(
            new FakeServiceModeManager(),
            coreManager,
            _ => new FakeCoreManager(RunningSnapshot(20)),
            () => normal,
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ =>
            {
                cancellation.Cancel();
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            (_, _) => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ => { });

        var result = await switcher.DeactivateAsync(cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.DisposeCount);
        Assert.Equal(10, (await coreManager.GetSnapshotAsync()).Pid);
    }

    [Fact(DisplayName = "Disposed session switcher rejects later operations as canceled")]
    public async Task DisposedSessionSwitcherRejectsLaterOperationsAsCanceled()
    {
        using var coreManager = new SwitchableCoreManager(new FakeCoreManager(RunningSnapshot(10)));
        var switcher = CreateSwitcher(
            new FakeServiceModeManager(),
            coreManager,
            new FakeCoreManager(RunningSnapshot(20)),
            [],
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ => { });
        switcher.Dispose();

        var result = await switcher.ActivateAsync(CancellationToken.None);

        Assert.True(result.IsCanceled);
    }

    [Fact(DisplayName = "Session switch disposal waits until cancellation rollback finishes")]
    public async Task SessionSwitchDisposalWaitsUntilCancellationRollbackFinishes()
    {
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rollbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRollback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normal = new FakeCoreManager(RunningSnapshot(10));
        using var coreManager = new SwitchableCoreManager(normal);
        var serviceMode = new FakeServiceModeManager
        {
            StopCoreHandler = async _ =>
            {
                rollbackEntered.TrySetResult();
                await releaseRollback.Task;
                return ServiceModeOperationResult.Success("stopped");
            }
        };
        var resumeCount = 0;
        var switcher = new ServiceModeSessionSwitcher(
            serviceMode,
            coreManager,
            _ => new FakeCoreManager(RunningSnapshot(20)),
            () => new FakeCoreManager(RunningSnapshot(10)),
            _ => Task.FromResult(CoreHostOperationResult.Success("ok")),
            _ =>
            {
                resumeCount++;
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            async (_, token) =>
            {
                startEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return CoreHostOperationResult.Success("ok");
            },
            _ => { });

        var activation = switcher.ActivateAsync(CancellationToken.None);
        await startEntered.Task.WaitAsync(AsyncTimeout);
        var dispose = Task.Run(switcher.Dispose);
        await rollbackEntered.Task.WaitAsync(AsyncTimeout);

        Assert.False(dispose.IsCompleted);

        releaseRollback.TrySetResult();
        await dispose.WaitAsync(AsyncTimeout);
        Assert.True((await activation.WaitAsync(AsyncTimeout)).IsCanceled);
        Assert.Equal(1, resumeCount);
    }

    [Fact(DisplayName = "Failed candidate does not leak state or replace current core")]
    public async Task FailedCandidateDoesNotLeakStateOrReplaceCurrentCore()
    {
        var normal = new FakeCoreManager(RunningSnapshot(10));
        Action? candidateStateSource = null;
        var candidate = new FakeCoreManager(RunningSnapshot(20), _ =>
        {
            candidateStateSource?.Invoke();
            throw new InvalidOperationException("not ready");
        });
        candidateStateSource = () => candidate.EmitState(new CoreSnapshot(CoreState.Crashed, 20, "service", "failed"));
        using var manager = new SwitchableCoreManager(normal);
        var states = new List<CoreSnapshot>();
        manager.StateChanged += (_, state) => states.Add(state);

        using (var transition = await manager.BeginTransitionAsync())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => transition.SwitchAsync(candidate));
        }

        Assert.Empty(states);
        Assert.Equal(1, candidate.DisposeCount);
        Assert.Equal(0, normal.DisposeCount);
        Assert.Equal(10, (await manager.GetSnapshotAsync()).Pid);
    }

    [Fact(DisplayName = "Dispose waits for active core operation")]
    public async Task DisposeWaitsForActiveCoreOperation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var normal = new FakeCoreManager(RunningSnapshot(10), async _ =>
        {
            started.TrySetResult();
            await release.Task;
            return RunningSnapshot(10);
        });
        var manager = new SwitchableCoreManager(normal);

        var operation = manager.GetSnapshotAsync();
        await started.Task;
        var dispose = manager.DisposeAsync().AsTask();

        Assert.False(dispose.IsCompleted);

        release.TrySetResult();
        await operation;
        await dispose;

        Assert.Equal(1, normal.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => manager.GetSnapshotAsync());
    }

    private static ServiceModeSessionSwitcher CreateSwitcher(
        FakeServiceModeManager serviceMode,
        SwitchableCoreManager coreManager,
        ICoreManager serviceCore,
        List<string> order,
        Func<CancellationToken, Task<CoreHostOperationResult>> resumeNormalCore,
        Action<bool> setActive)
    {
        return new ServiceModeSessionSwitcher(
            serviceMode,
            coreManager,
            _ => serviceCore,
            () => new FakeCoreManager(RunningSnapshot(10)),
            _ =>
            {
                order.Add("stop-normal");
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            resumeNormalCore,
            (_, _) =>
            {
                order.Add("start-service");
                return Task.FromResult(CoreHostOperationResult.Success("ok"));
            },
            setActive);
    }

    private static CoreSnapshot RunningSnapshot(int pid)
    {
        return new CoreSnapshot(CoreState.Running, pid, "pipe", null);
    }

    private sealed class FakeCoreManager(
        CoreSnapshot snapshot,
        Func<CancellationToken, Task<CoreSnapshot>>? getSnapshot = null) : ICoreManager, IDisposable
    {
        public int DisposeCount { get; private set; }

        public event EventHandler<CoreSnapshot>? StateChanged;

        public event EventHandler<CoreLogMessage>? CoreLogReceived
        {
            add { }
            remove { }
        }

        public Task<CoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return getSnapshot?.Invoke(cancellationToken) ?? Task.FromResult(snapshot);
        }

        public Task<CoreApplyConfigResult> ApplyConfigAsync(
            CoreApplyConfigRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CoreApplyConfigResult(CoreApplyMode.Reload, snapshot.Pid ?? 0));
        }

        public Task RestartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void EmitState(CoreSnapshot state)
        {
            StateChanged?.Invoke(this, state);
        }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private sealed class FakeServiceModeManager : IServiceModeManager
    {
        public int StopCoreCount { get; private set; }
        public ServiceModeOperationResult StopCoreResult { get; init; } = ServiceModeOperationResult.Success("stopped");
        public Func<CancellationToken, Task<ServiceModeOperationResult>>? StopCoreHandler { get; init; }

        public Task<ServiceModeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ServiceModeStatus(
                ServiceModeState.Running,
                "running",
                CoreState: "running",
                CorePid: 20));
        }

        public Task<ServiceModeOperationResult> InstallOrUpdateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceModeOperationResult.Success("installed"));

        public Task<ServiceModeOperationResult> UninstallAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceModeOperationResult.Success("uninstalled"));

        public Task<ServiceModeOperationResult> StartCoreHostAsync(
            ServiceModeCoreHostRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceModeOperationResult.Success("started"));

        public Task<ServiceModeOperationResult> StopCoreHostAsync(CancellationToken cancellationToken = default)
        {
            StopCoreCount++;
            return StopCoreHandler?.Invoke(cancellationToken) ?? Task.FromResult(StopCoreResult);
        }

        public Task<ServiceModeOperationResult> RestartCoreHostAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceModeOperationResult.Success("restarted"));

        public Task<ServiceModeOperationResult> SendHeartbeatAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceModeOperationResult.Success("heartbeat"));
    }

    private sealed class ThrowingRuleConfigSource : IRuleConfigSource
    {
        public string ReadRuntimeConfig()
        {
            throw new IOException("rules unavailable");
        }
    }
}
