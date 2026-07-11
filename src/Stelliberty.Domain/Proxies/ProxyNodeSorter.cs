namespace Stelliberty.Domain.Proxies;

public sealed class ProxyNodeSorter
{
    public IReadOnlyList<string> FilterAndSort(
        IReadOnlyList<string> proxyNames,
        IReadOnlyDictionary<string, ProxyNode> nodes,
        ProxyNodeSortMode sortMode,
        string searchQuery)
    {
        var normalizedQuery = searchQuery.Trim();
        var filteredNames = string.IsNullOrWhiteSpace(normalizedQuery)
            ? proxyNames.ToList()
            : proxyNames.Where(name => Matches(name, nodes, normalizedQuery)).ToList();

        return sortMode switch
        {
            ProxyNodeSortMode.Name => filteredNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(),
            ProxyNodeSortMode.Delay => filteredNames.OrderBy(name => DelaySortKey(nodes, name)).ToList(),
            _ => filteredNames
        };
    }

    private static bool Matches(string name, IReadOnlyDictionary<string, ProxyNode> nodes, string searchQuery)
    {
        if (!nodes.TryGetValue(name, out var node))
        {
            return name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase);
        }

        return name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)
            || node.Type.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)
            || (node.Server?.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true)
            || (node.Port?.ToString().Contains(searchQuery, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static int DelaySortKey(IReadOnlyDictionary<string, ProxyNode> nodes, string name)
    {
        if (!nodes.TryGetValue(name, out var node) || node.Delay is null || node.Delay < 0)
        {
            return int.MaxValue;
        }

        return node.Delay.Value;
    }
}
