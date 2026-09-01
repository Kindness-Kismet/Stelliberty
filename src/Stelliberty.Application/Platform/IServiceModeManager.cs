using Stelliberty.Application.Runtime;

namespace Stelliberty.Application.Platform;

public interface IServiceModeManager
{
    // 安装态与版本：只在启动和用户操作时查，代价高。
    Task<ServiceModeStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    // 核心运行态：高频轮询走这条，失败返回未观测而非"核心已停"。
    Task<CoreObservation> ObserveCoreAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> InstallOrUpdateAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> UninstallAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> StartCoreHostAsync(ServiceModeCoreHostRequest request, CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> StopCoreHostAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> RestartCoreHostAsync(CancellationToken cancellationToken = default);

    Task<ServiceModeOperationResult> SendHeartbeatAsync(CancellationToken cancellationToken = default);
}
