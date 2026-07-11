using Stelliberty.Domain.Proxies;

namespace Stelliberty.Application.Proxies;

// 降级延迟值让核心延迟测试不可用时，选择流程仍能继续。
public static class ProxyDelayFallback
{
    public static ProxyDelayResult TestNodes(ProxyConfig config, IReadOnlyList<string> proxyNames)
    {
        var delays = new Dictionary<string, int>(StringComparer.Ordinal);
        var tested = new List<string>();
        foreach (var proxyName in proxyNames)
        {
            if (!config.TryGetEntryDelay(proxyName, out var delay))
            {
                continue;
            }

            tested.Add(proxyName);
            delays[proxyName] = NextDelay(delay);
        }

        return new ProxyDelayResult(config.WithEntryDelays(delays), tested, [], []);
    }

    private static int NextDelay(int? currentDelay)
    {
        return currentDelay is null or < 0 ? 1 : currentDelay.Value + 1;
    }
}
