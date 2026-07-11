using Stelliberty.Domain.Proxies;
namespace Stelliberty.Application.Proxies;

public interface IProxyConfigProvider
{
    Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default);
}
