namespace Stelliberty.Application.Proxies;

public interface IProxyRuntimeSnapshotSource
{
    ProxyRuntimeSnapshot? LastSnapshot { get; }
}
