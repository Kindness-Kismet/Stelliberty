using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;

namespace Stelliberty.Infrastructure.Proxies;

public sealed class FileRuntimeProxyConfigProvider(ProxyConfigLoader loader) : IProxyConfigProvider
{
    public Task<ProxyConfig> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(loader.LoadConfig, cancellationToken);
    }
}
