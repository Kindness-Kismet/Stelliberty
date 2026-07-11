using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class UnsupportedSystemProxyService : ISystemProxyService
{
    public SystemProxyOperationResult Enable(SystemProxyApplicationRequest request)
    {
        return new SystemProxyOperationResult(false, "System proxy is not supported on the current platform");
    }

    public SystemProxyOperationResult Disable()
    {
        return new SystemProxyOperationResult(false, "System proxy is not supported on the current platform");
    }
}
