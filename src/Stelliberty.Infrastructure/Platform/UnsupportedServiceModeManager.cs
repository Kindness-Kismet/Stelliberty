using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class UnsupportedServiceModeManager : IServiceModeManager
{
    private static readonly ServiceModeStatus UnsupportedStatus = new(
        ServiceModeState.Unsupported,
        "Service mode is not supported on the current platform");

    public Task<ServiceModeStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(UnsupportedStatus);
    }

    public Task<ServiceModeOperationResult> InstallOrUpdateAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ServiceModeOperationResult.Failed(UnsupportedStatus.Message));
    }

    public Task<ServiceModeOperationResult> UninstallAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ServiceModeOperationResult.Failed(UnsupportedStatus.Message));
    }

    public Task<ServiceModeOperationResult> StartCoreHostAsync(ServiceModeCoreHostRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(ServiceModeOperationResult.Failed(UnsupportedStatus.Message));
    }

    public Task<ServiceModeOperationResult> StopCoreHostAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ServiceModeOperationResult.Failed(UnsupportedStatus.Message));
    }

    public Task<ServiceModeOperationResult> RestartCoreHostAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ServiceModeOperationResult.Failed(UnsupportedStatus.Message));
    }

    public Task<ServiceModeOperationResult> SendHeartbeatAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ServiceModeOperationResult.Failed(UnsupportedStatus.Message));
    }
}
