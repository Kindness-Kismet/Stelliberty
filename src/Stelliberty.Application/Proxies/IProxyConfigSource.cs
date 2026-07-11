using Stelliberty.Domain.Proxies;
namespace Stelliberty.Application.Proxies;

public interface IProxyConfigSource
{
    string ReadRuntimeConfig();
}
