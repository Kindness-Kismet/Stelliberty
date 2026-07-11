using Stelliberty.Domain.Subscriptions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionChainProxyRuntimeApplier
{
    public string Apply(string content, Subscription subscription)
    {
        if (subscription.DisabledBuiltinChainProxyNames.Count == 0 && subscription.CustomChainProxies.Count == 0)
        {
            return content;
        }

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(content));
            if (stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return content;
            }

            var proxies = ReadMappingSequence(root, "proxies");
            var proxyGroups = ReadMappingSequence(root, "proxy-groups");
            var result = BuildRuntimeConfig(proxies, proxyGroups, subscription);
            Set(root, "proxies", result.Proxies);
            if (HasMappingSequence(root, "proxy-groups"))
            {
                Set(root, "proxy-groups", result.ProxyGroups);
            }

            using var writer = new StringWriter();
            stream.Save(writer, assignAnchors: false);
            return writer.ToString();
        }
        catch (YamlException)
        {
            return content;
        }
    }

    private sealed record RuntimeConfigBuildResult(YamlSequenceNode Proxies, YamlSequenceNode ProxyGroups);

    private sealed record CustomProxyGroupEntry(string LeafNodeName, string DisplayName);

    private static RuntimeConfigBuildResult BuildRuntimeConfig(
        IReadOnlyList<YamlMappingNode> proxies,
        IReadOnlyList<YamlMappingNode> proxyGroups,
        Subscription subscription)
    {
        var disabledNames = subscription.DisabledBuiltinChainProxyNames.ToHashSet(StringComparer.Ordinal);
        // 内置链式代理是带 dialer-proxy 的覆写后节点。
        var activeProxies = proxies
            .Where(proxy => !IsDisabledBuiltinProxy(proxy, disabledNames))
            .ToList();
        var proxyByName = activeProxies
            .GroupBy(proxy => Scalar(proxy, "name"), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var occupiedNames = proxyByName.Keys
            .Concat(proxyGroups.Select(group => Scalar(group, "name")))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        var runtimeProxies = activeProxies.Select(Clone).ToList();
        var customGroupEntries = new List<CustomProxyGroupEntry>();

        foreach (var customProxy in subscription.CustomChainProxies)
        {
            var runtimeDialerProxies = BuildRuntimeDialerProxies(proxyByName, occupiedNames, customProxy);
            if (runtimeDialerProxies.Count > 0)
            {
                customGroupEntries.Add(new CustomProxyGroupEntry(
                    LastValidNodeName(customProxy),
                    customProxy.DisplayName.Trim()));
            }

            foreach (var runtimeProxy in runtimeDialerProxies)
            {
                runtimeProxies.Add(runtimeProxy);
            }
        }

        var disabledBuiltinNames = proxies
            .Where(proxy => IsDisabledBuiltinProxy(proxy, disabledNames))
            .Select(proxy => Scalar(proxy, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        return new RuntimeConfigBuildResult(
            new YamlSequenceNode(runtimeProxies),
            BuildProxyGroups(proxyGroups, disabledBuiltinNames, customGroupEntries));
    }

    private static IReadOnlyList<YamlMappingNode> BuildRuntimeDialerProxies(
        IReadOnlyDictionary<string, YamlMappingNode> proxyByName,
        HashSet<string> occupiedNames,
        SubscriptionCustomChainProxy customProxy)
    {
        var nodeNames = customProxy.NodeNames
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        var displayName = customProxy.DisplayName.Trim();
        if (nodeNames.Count < 2 || string.IsNullOrWhiteSpace(displayName) || occupiedNames.Contains(displayName))
        {
            return [];
        }

        if (nodeNames.Any(name => !proxyByName.ContainsKey(name)))
        {
            return [];
        }

        var result = new List<YamlMappingNode>();
        var previousName = nodeNames[0];
        for (var index = 1; index < nodeNames.Count; index++)
        {
            var runtimeName = index == nodeNames.Count - 1
                ? displayName
                : ReserveInternalProxyName(customProxy, index, occupiedNames);
            var runtimeProxy = Clone(proxyByName[nodeNames[index]]);
            SetScalar(runtimeProxy, "name", runtimeName);
            SetScalar(runtimeProxy, "dialer-proxy", previousName);

            result.Add(runtimeProxy);
            if (index == nodeNames.Count - 1)
            {
                occupiedNames.Add(runtimeName);
            }

            previousName = runtimeName;
        }

        return result;
    }

    private static YamlSequenceNode BuildProxyGroups(
        IReadOnlyList<YamlMappingNode> proxyGroups,
        HashSet<string> disabledBuiltinNames,
        IReadOnlyList<CustomProxyGroupEntry> customGroupEntries)
    {
        var groups = new List<YamlMappingNode>();
        foreach (var group in proxyGroups)
        {
            var clone = Clone(group);
            if (clone.Children.TryGetValue(new YamlScalarNode("proxies"), out var proxiesNode)
                && proxiesNode is YamlSequenceNode proxies)
            {
                clone.Children[new YamlScalarNode("proxies")] = BuildProxyGroupEntries(proxies, disabledBuiltinNames, customGroupEntries);
            }

            groups.Add(clone);
        }

        return new YamlSequenceNode(groups);
    }

    private static YamlSequenceNode BuildProxyGroupEntries(
        YamlSequenceNode proxies,
        HashSet<string> disabledBuiltinNames,
        IReadOnlyList<CustomProxyGroupEntry> customGroupEntries)
    {
        var entries = new List<YamlNode>();
        foreach (var entry in proxies.Children)
        {
            var name = entry.ToString();
            if (disabledBuiltinNames.Contains(name))
            {
                continue;
            }

            entries.Add(entry);
            foreach (var customEntry in customGroupEntries.Where(item => string.Equals(item.LeafNodeName, name, StringComparison.Ordinal)))
            {
                // 自定义链加入最后一跳所在分组，保证仍可选择。
                if (!ContainsScalar(entries, customEntry.DisplayName))
                {
                    entries.Add(new YamlScalarNode(customEntry.DisplayName));
                }
            }
        }

        return new YamlSequenceNode(entries);
    }

    private static bool IsDisabledBuiltinProxy(YamlMappingNode proxy, HashSet<string> disabledNames)
    {
        return disabledNames.Contains(Scalar(proxy, "name"))
            && !string.IsNullOrWhiteSpace(Scalar(proxy, "dialer-proxy"));
    }

    private static string ReserveInternalProxyName(
        SubscriptionCustomChainProxy customProxy,
        int index,
        HashSet<string> occupiedNames)
    {
        var stem = $"__stelliberty_chain_{NameSegment(customProxy.Id, customProxy.DisplayName)}_{index}";
        var name = stem;
        var suffix = 2;
        while (occupiedNames.Contains(name))
        {
            name = $"{stem}_{suffix}";
            suffix++;
        }

        occupiedNames.Add(name);
        return name;
    }

    private static string NameSegment(string id, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(id) ? fallback : id;
        var chars = source
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_')
            .ToArray();
        return chars.Length == 0 ? "custom" : new string(chars);
    }

    private static string LastValidNodeName(SubscriptionCustomChainProxy customProxy)
    {
        return customProxy.NodeNames
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Last();
    }

    private static IReadOnlyList<YamlMappingNode> ReadMappingSequence(YamlMappingNode root, string key)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode(key), out var value) || value is not YamlSequenceNode sequence)
        {
            return [];
        }

        return sequence.Children.OfType<YamlMappingNode>().ToList();
    }

    private static bool HasMappingSequence(YamlMappingNode root, string key)
    {
        return root.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlSequenceNode;
    }

    private static void Set(YamlMappingNode root, string key, YamlSequenceNode value)
    {
        root.Children[new YamlScalarNode(key)] = value;
    }

    private static string Scalar(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value.ToString() : string.Empty;
    }

    private static void SetScalar(YamlMappingNode mapping, string key, string value)
    {
        mapping.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);
    }

    private static bool ContainsScalar(IEnumerable<YamlNode> nodes, string value)
    {
        return nodes.Any(node => string.Equals(node.ToString(), value, StringComparison.Ordinal));
    }

    private static YamlMappingNode Clone(YamlMappingNode mapping)
    {
        var clone = new YamlMappingNode();
        foreach (var child in mapping.Children)
        {
            clone.Children.Add(child.Key, child.Value);
        }

        return clone;
    }
}
