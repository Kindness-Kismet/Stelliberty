using Stelliberty.Application.Tray;

namespace Stelliberty.Application.Proxies;

public interface IRuntimeSnapshotClient
{
    Task<TrayRuntimeSnapshot> GetRuntimeSnapshotAsync(CancellationToken cancellationToken = default);

    Task ResetRuntimeTrafficAsync(CancellationToken cancellationToken = default);
}
