using Stelliberty.Domain.Proxies;
namespace Stelliberty.Application.Proxies;

public sealed record ProxyRuntimeSnapshot(IReadOnlyList<ProxyRuntimeEntry> Entries);

public sealed record ProxyRuntimeEntry(
    string Name,
    string Type,
    string? Now,
    string? Fixed,
    IReadOnlyList<string> All,
    bool IsHidden);
