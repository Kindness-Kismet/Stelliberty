using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

internal sealed class SubscriptionChainProxyTopology
{
    private static readonly TimeSpan FilterRegexTimeout = TimeSpan.FromSeconds(1);
    private readonly Dictionary<string, HashSet<string>> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _includeAllProxyGroupFilters = new(StringComparer.Ordinal);
    private readonly HashSet<string> _proxyGroupNames = new(StringComparer.Ordinal);

    public static SubscriptionChainProxyTopology Create(
        IReadOnlyList<YamlMappingNode> proxies,
        IReadOnlyList<YamlMappingNode> proxyGroups,
        IReadOnlySet<string>? excludedNames = null)
    {
        var topology = new SubscriptionChainProxyTopology();
        foreach (var proxy in proxies)
        {
            var name = Scalar(proxy, "name");
            if (string.IsNullOrWhiteSpace(name) || excludedNames?.Contains(name) == true)
            {
                continue;
            }

            topology.AddVertex(name);
            var dialerProxy = Scalar(proxy, "dialer-proxy");
            if (!string.IsNullOrWhiteSpace(dialerProxy))
            {
                topology.AddEdge(name, dialerProxy);
            }
        }

        foreach (var proxyGroup in proxyGroups)
        {
            var name = Scalar(proxyGroup, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            topology._proxyGroupNames.Add(name);
            topology.AddVertex(name);
            if (IsTrue(proxyGroup, "include-all") || IsTrue(proxyGroup, "include-all-proxies"))
            {
                var filter = Scalar(proxyGroup, "filter");
                topology._includeAllProxyGroupFilters[name] = filter;
                foreach (var proxy in proxies)
                {
                    var proxyName = Scalar(proxy, "name");
                    if (!string.IsNullOrWhiteSpace(proxyName)
                        && excludedNames?.Contains(proxyName) != true
                        && IncludesProxy(filter, proxyName))
                    {
                        topology.AddEdge(name, proxyName);
                    }
                }
            }

            if (!proxyGroup.Children.TryGetValue(new YamlScalarNode("proxies"), out var membersNode)
                || membersNode is not YamlSequenceNode members)
            {
                continue;
            }

            foreach (var member in members.Children)
            {
                var memberName = member.ToString();
                if (!string.IsNullOrWhiteSpace(memberName) && excludedNames?.Contains(memberName) != true)
                {
                    topology.AddEdge(name, memberName);
                }
            }
        }

        return topology;
    }

    public IReadOnlyList<string> ReachableProxyGroupNames(string source)
        => _proxyGroupNames.Where(name => CanReach(source, name)).ToList();

    public bool WouldCreateCustomChainCycle(
        string proxyGroupName,
        string firstHopName,
        IReadOnlyList<string> generatedProxyNames)
        => CanReach(firstHopName, proxyGroupName)
            || _includeAllProxyGroupFilters.Any(entry =>
                generatedProxyNames.Any(name => IncludesProxy(entry.Value, name))
                && CanReach(firstHopName, entry.Key));

    public bool HasDialerProxyCycle(string proxyName, string dialerProxyName)
        => CanReach(dialerProxyName, proxyName);

    public void AddCustomChain(
        string proxyGroupName,
        string displayName,
        string firstHopName,
        IReadOnlyList<string> generatedProxyNames)
    {
        AddEdge(proxyGroupName, displayName);
        foreach (var entry in _includeAllProxyGroupFilters)
        {
            foreach (var generatedProxyName in generatedProxyNames.Where(name => IncludesProxy(entry.Value, name)))
            {
                AddEdge(entry.Key, generatedProxyName);
            }
        }

        foreach (var generatedProxyName in generatedProxyNames)
        {
            AddEdge(generatedProxyName, firstHopName);
        }
    }

    private bool CanReach(string source, string target)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(source);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return true;
            }

            if (_edges.TryGetValue(current, out var targets))
            {
                foreach (var next in targets)
                {
                    pending.Push(next);
                }
            }
        }

        return false;
    }

    private void AddVertex(string name)
    {
        if (!_edges.ContainsKey(name))
        {
            _edges[name] = new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private void AddEdge(string source, string target)
    {
        AddVertex(source);
        AddVertex(target);
        _edges[source].Add(target);
    }

    private static string Scalar(YamlMappingNode mapping, string key)
        => mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value.ToString() : string.Empty;

    private static bool IsTrue(YamlMappingNode mapping, string key)
        => bool.TryParse(Scalar(mapping, key), out var value) && value;

    private static bool IncludesProxy(string filter, string proxyName)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return true;
        }

        try
        {
            // Mihomo 用反引号分隔多个 .NET 正则，命中任意一个即纳入代理。
            return filter.Split('`').Any(pattern =>
                Regex.IsMatch(proxyName, pattern, RegexOptions.None, FilterRegexTimeout));
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }
}
