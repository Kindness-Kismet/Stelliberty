using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

public sealed record ProxyFixedSelectionReleaseResult(
    ProxyConfig Config,
    IReadOnlyList<string> ReleasedGroupNames)
{
    public bool HasChanges => ReleasedGroupNames.Count > 0;
}
