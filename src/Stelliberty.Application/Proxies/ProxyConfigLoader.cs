using Stelliberty.Domain.Proxies;
namespace Stelliberty.Application.Proxies;

public sealed class ProxyConfigLoader(
    IProxyConfigSource source,
    ProxyConfigParser parser)
{
    public ProxyConfig LoadConfig()
    {
        return parser.Parse(source.ReadRuntimeConfig());
    }
}
