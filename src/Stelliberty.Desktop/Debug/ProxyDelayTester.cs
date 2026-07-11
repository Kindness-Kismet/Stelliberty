#if DEBUG
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Desktop.Debug;

internal sealed class ProxyDelayTester : IProxyDelayTester
{
    public Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default)
    {
        AppLogger.Info($"Debug proxy delay test used local result: {proxyName}");
        return Task.FromResult(50);
    }
}
#endif
