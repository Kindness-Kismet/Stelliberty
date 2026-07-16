using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Native.Hub;

namespace Stelliberty.Desktop.Services;

internal sealed class ServiceModeSessionSwitcher(
    IServiceModeManager serviceModeManager,
    SwitchableCoreManager coreManager,
    Func<ServiceModeStatus, ICoreManager> createServiceCoreManager,
    Func<CancellationToken, Task<BootstrapResult>> stopNormalCore,
    Func<CancellationToken, Task<BootstrapResult>> resumeNormalCore,
    Func<ServiceModeStatus, CancellationToken, Task<BootstrapResult>> startServiceCore,
    Action<bool> setServiceModeCoreHostActive)
{
    public async Task<ServiceModeOperationResult> ActivateAsync(CancellationToken cancellationToken)
    {
        ServiceModeStatus status;
        try
        {
            status = await serviceModeManager.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ServiceModeOperationResult.Canceled("Service mode activation was canceled.");
        }
        catch (Exception exception)
        {
            return ServiceModeOperationResult.Failed(exception.Message);
        }

        if (!status.IsRunning)
        {
            return ServiceModeOperationResult.Failed("Service mode is not running.");
        }

        // 两种核心共用 Mihomo 管道，服务核心启动前必须确认普通核心已停止。
        BootstrapResult stopped;
        try
        {
            stopped = await stopNormalCore(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ServiceModeOperationResult.Canceled("Service mode activation was canceled.");
        }
        catch (Exception exception)
        {
            return ServiceModeOperationResult.Failed(exception.Message);
        }

        if (!stopped.Ok)
        {
            return ServiceModeOperationResult.Failed(stopped.Message);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = await startServiceCore(status, cancellationToken).ConfigureAwait(false);
            if (!started.Ok)
            {
                return await RollBackAsync(ServiceModeOperationResult.Failed(started.Message)).ConfigureAwait(false);
            }

            await coreManager.SwitchAsync(createServiceCoreManager(status), cancellationToken).ConfigureAwait(false);
            setServiceModeCoreHostActive(true);
            return ServiceModeOperationResult.Success("Service mode is active.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await RollBackAsync(ServiceModeOperationResult.Canceled("Service mode activation was canceled.")).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return await RollBackAsync(ServiceModeOperationResult.Failed(exception.Message)).ConfigureAwait(false);
        }
    }

    private async Task<ServiceModeOperationResult> RollBackAsync(ServiceModeOperationResult result)
    {
        setServiceModeCoreHostActive(false);
        try
        {
            var stopped = await serviceModeManager.StopCoreHostAsync(CancellationToken.None).ConfigureAwait(false);
            if (!stopped.IsSuccess)
            {
                AppLogger.Warning($"Service-mode core rollback stop failed: {stopped.Message}");
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Service-mode core rollback stop failed: {exception.Message}");
        }

        try
        {
            var resumed = await resumeNormalCore(CancellationToken.None).ConfigureAwait(false);
            if (!resumed.Ok)
            {
                return ServiceModeOperationResult.Failed($"{result.Message} Normal-mode recovery failed: {resumed.Message}");
            }

            await coreManager.EnsureReadyAsync(CancellationToken.None).ConfigureAwait(false);
            return result;
        }
        catch (Exception exception)
        {
            return ServiceModeOperationResult.Failed($"{result.Message} Normal-mode recovery failed: {exception.Message}");
        }
    }
}
