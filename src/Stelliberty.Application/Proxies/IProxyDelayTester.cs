using Stelliberty.Domain.Proxies;
namespace Stelliberty.Application.Proxies;

public interface IProxyDelayTester
{
    Task<int> TestDelayAsync(string proxyName, CancellationToken cancellationToken = default);
}
