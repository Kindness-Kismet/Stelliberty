namespace Stelliberty.Domain.Proxies;

public sealed record ProxySelectionResult(
    ProxyConfig Config,
    ProxyChangeRequest ChangeRequest,
    bool ShouldCloseConnections);
