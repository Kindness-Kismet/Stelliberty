namespace Stelliberty.Application.Platform;

public interface IServiceModeManager
{
    Task<ServiceModeStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> InstallOrUpdateAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> UninstallAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> StartCoreHostAsync(ServiceModeCoreHostRequest request, CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> StopCoreHostAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> RestartCoreHostAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> SendHeartbeatAsync(CancellationToken cancellationToken = default);
}
