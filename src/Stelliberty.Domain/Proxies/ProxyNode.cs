namespace Stelliberty.Domain.Proxies;

public sealed record ProxyNode(
    string Name,
    string Type,
    int? Delay = null,
    string? Server = null,
    int? Port = null);
