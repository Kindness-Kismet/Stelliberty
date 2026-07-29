using Stelliberty.Domain.Proxies;
namespace Stelliberty.Application.Proxies;

public sealed record ProxyDelayResult(
    ProxyConfig Config,
    IReadOnlyList<string> TestedNodeNames,
    IReadOnlyList<string> SkippedNodeNames,
    IReadOnlyList<string> FailedNodeNames);

public sealed record ProxyDelayProgress(string ProxyName, int Delay, bool IsCompleted = true);
