using Stelliberty.Application.Proxies;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionChainProxyCycleDetector
{
    public bool HasCycle(ProxyRuntimeSnapshot snapshot)
    {
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var indegrees = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in snapshot.Entries)
        {
            AddVertex(entry.Name);
            foreach (var member in entry.All.Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                AddEdge(entry.Name, member);
            }

            if (!string.IsNullOrWhiteSpace(entry.DialerProxy))
            {
                AddEdge(entry.Name, entry.DialerProxy);
            }
        }

        var ready = new Queue<string>(indegrees.Where(item => item.Value == 0).Select(item => item.Key));
        var visitedCount = 0;
        while (ready.TryDequeue(out var vertex))
        {
            visitedCount++;
            foreach (var target in edges[vertex])
            {
                indegrees[target]--;
                if (indegrees[target] == 0)
                {
                    ready.Enqueue(target);
                }
            }
        }

        return visitedCount != indegrees.Count;

        void AddVertex(string name)
        {
            if (!string.IsNullOrWhiteSpace(name) && !edges.ContainsKey(name))
            {
                edges[name] = new HashSet<string>(StringComparer.Ordinal);
                indegrees[name] = 0;
            }
        }

        void AddEdge(string source, string target)
        {
            AddVertex(source);
            AddVertex(target);
            if (edges[source].Add(target))
            {
                indegrees[target]++;
            }
        }
    }
}
