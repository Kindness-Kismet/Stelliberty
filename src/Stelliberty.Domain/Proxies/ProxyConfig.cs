namespace Stelliberty.Domain.Proxies;

public sealed record ProxyConfig(
    IReadOnlyList<ProxyGroup> Groups,
    IReadOnlyDictionary<string, ProxyNode> Nodes,
    OutboundMode? Mode = null)
{

    public IReadOnlyList<ProxyGroup> VisibleGroups => Mode switch
    {
        OutboundMode.Direct => [],
        OutboundMode.Global => GlobalVisibleGroups(),
        _ => Groups.Where(g => !g.IsHidden && !IsGlobal(g)).ToList(),
    };

    private IReadOnlyList<ProxyGroup> GlobalVisibleGroups()
    {
        var global = Groups.FirstOrDefault(IsGlobal);
        return global is null ? [] : [global];
    }

    private static bool IsGlobal(ProxyGroup group) =>
        string.Equals(group.Name, "GLOBAL", StringComparison.OrdinalIgnoreCase);

    public ProxyConfig WithNodeDelay(string proxyName, int delay)
        => WithEntryDelay(proxyName, delay);

    public ProxyConfig WithEntryDelay(string proxyName, int delay)
        => WithEntryDelays(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [proxyName] = delay
        });

    public ProxyConfig WithEntryDelays(IReadOnlyDictionary<string, int> delays)
    {
        if (delays.Count == 0)
        {
            return this;
        }

        var nodes = Nodes.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var groups = Groups.ToList();
        var hasNodeChange = false;
        var hasGroupChange = false;
        foreach (var (proxyName, delay) in delays)
        {
            if (nodes.TryGetValue(proxyName, out var node))
            {
                nodes[proxyName] = node with { Delay = delay };
                hasNodeChange = true;
                continue;
            }

            var groupIndex = groups.FindIndex(group => string.Equals(group.Name, proxyName, StringComparison.Ordinal));
            if (groupIndex >= 0)
            {
                groups[groupIndex] = groups[groupIndex] with { Delay = delay };
                hasGroupChange = true;
            }
        }

        return this with
        {
            Groups = hasGroupChange ? groups : Groups,
            Nodes = hasNodeChange ? nodes : Nodes
        };
    }

    public bool TryGetEntryDelay(string proxyName, out int? delay)
    {
        if (Nodes.TryGetValue(proxyName, out var node))
        {
            delay = node.Delay;
            return true;
        }

        var group = Groups.FirstOrDefault(item => string.Equals(item.Name, proxyName, StringComparison.Ordinal));
        if (group is not null)
        {
            delay = group.Delay;
            return true;
        }

        delay = null;
        return false;
    }

    public bool TryGetResolvedEntryDelay(string proxyName, out int? delay)
    {
        if (Nodes.TryGetValue(proxyName, out var node))
        {
            delay = node.Delay;
            return true;
        }

        var group = Groups.FirstOrDefault(item => string.Equals(item.Name, proxyName, StringComparison.Ordinal));
        if (group is null)
        {
            delay = null;
            return false;
        }

        delay = group.Delay;
        return true;
    }
}
