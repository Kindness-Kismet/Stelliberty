using Stelliberty.Domain.Subscriptions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionChainProxyRuntimeApplier
{
    public string Apply(string content, Subscription subscription)
    {
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
            if (subscription.DisabledBuiltinChainProxyNames.Count == 0
                && subscription.CustomChainProxies.All(item => !item.IsEnabled)
                && proxies.All(proxy => string.IsNullOrWhiteSpace(Scalar(proxy, "dialer-proxy"))))
            {
                return content;
            }

            var result = BuildRuntimeConfig(proxies, proxyGroups, subscription);
            if (!result.Validation.IsValid)
            {
                throw new SubscriptionChainProxyCycleException(result.Validation);
            }
            if (subscription.DisabledBuiltinChainProxyNames.Count == 0
                && subscription.CustomChainProxies.All(item => !item.IsEnabled))
            {
                return content;
            }

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

    public SubscriptionChainProxyValidationResult ValidateCycles(string content, Subscription subscription)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(content));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return SubscriptionChainProxyValidationResult.Valid;
            }

            var result = BuildRuntimeConfig(
                ReadMappingSequence(root, "proxies"),
                ReadMappingSequence(root, "proxy-groups"),
                subscription);
            return result.Validation;
        }
        catch (YamlException)
        {
            return SubscriptionChainProxyValidationResult.Valid;
        }
    }

    private sealed record RuntimeConfigBuildResult(
        YamlSequenceNode Proxies,
        YamlSequenceNode ProxyGroups,
        SubscriptionChainProxyValidationResult Validation);

    private sealed record RuntimeDialerProxyBuildResult(
        IReadOnlyList<YamlMappingNode> Proxies,
        bool HasCycle);

    private sealed record CustomProxyGroupEntry(string ProxyGroupName, string DisplayName);

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
        var topology = SubscriptionChainProxyTopology.Create(activeProxies, proxyGroups, disabledNames);
        var cyclicBuiltinNames = activeProxies
            .Select(proxy => new
            {
                Name = Scalar(proxy, "name"),
                DialerProxy = Scalar(proxy, "dialer-proxy")
            })
            .Where(proxy => !string.IsNullOrWhiteSpace(proxy.Name)
                && !string.IsNullOrWhiteSpace(proxy.DialerProxy)
                && topology.HasDialerProxyCycle(proxy.Name, proxy.DialerProxy))
            .Select(proxy => proxy.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var cyclicCustomChains = new List<SubscriptionCustomChainProxyCycle>();

        foreach (var customProxy in subscription.CustomChainProxies.Where(item => item.IsEnabled))
        {
            var customBuild = BuildRuntimeDialerProxies(
                proxyByName,
                proxyGroups,
                occupiedNames,
                topology,
                customProxy);
            var runtimeDialerProxies = customBuild.Proxies;
            if (customBuild.HasCycle)
            {
                cyclicCustomChains.Add(new SubscriptionCustomChainProxyCycle(
                    customProxy.Id,
                    customProxy.DisplayName.Trim()));
            }

            if (runtimeDialerProxies.Count > 0)
            {
                customGroupEntries.Add(new CustomProxyGroupEntry(
                    customProxy.ProxyGroupName.Trim(),
                    customProxy.DisplayName.Trim()));
                topology.AddCustomChain(
                    customProxy.ProxyGroupName.Trim(),
                    customProxy.DisplayName.Trim(),
                    FirstValidHopName(customProxy),
                    runtimeDialerProxies.Select(proxy => Scalar(proxy, "name")).ToList());
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
            BuildProxyGroups(proxyGroups, disabledBuiltinNames, customGroupEntries),
            new SubscriptionChainProxyValidationResult(cyclicBuiltinNames, cyclicCustomChains));
    }

    private static RuntimeDialerProxyBuildResult BuildRuntimeDialerProxies(
        IReadOnlyDictionary<string, YamlMappingNode> proxyByName,
        IReadOnlyList<YamlMappingNode> proxyGroups,
        HashSet<string> occupiedNames,
        SubscriptionChainProxyTopology topology,
        SubscriptionCustomChainProxy customProxy)
    {
        var hops = customProxy.Hops
            .Where(hop => !string.IsNullOrWhiteSpace(hop.Name))
            .Select(hop => hop with { Name = hop.Name.Trim() })
            .ToList();
        var displayName = customProxy.DisplayName.Trim();
        var proxyGroupName = customProxy.ProxyGroupName.Trim();
        if (hops.Count < 2
            || string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(proxyGroupName)
            || occupiedNames.Contains(displayName)
            || !proxyGroups.Any(group => Scalar(group, "name") == proxyGroupName)
            || hops.Skip(1).Any(hop => hop.Kind != SubscriptionChainProxyHopKind.Proxy)
            || hops.Count(hop => hop.Kind == SubscriptionChainProxyHopKind.ProxyGroup) > 1)
        {
            return new RuntimeDialerProxyBuildResult([], false);
        }

        var firstHop = hops[0];
        if ((firstHop.Kind == SubscriptionChainProxyHopKind.ProxyGroup
                && !proxyGroups.Any(group => Scalar(group, "name") == firstHop.Name))
            || (firstHop.Kind == SubscriptionChainProxyHopKind.Proxy
                && !proxyByName.ContainsKey(firstHop.Name)))
        {
            return new RuntimeDialerProxyBuildResult([], false);
        }

        if (hops.Skip(1).Any(hop => !proxyByName.ContainsKey(hop.Name)))
        {
            return new RuntimeDialerProxyBuildResult([], false);
        }

        var plannedOccupiedNames = occupiedNames.ToHashSet(StringComparer.Ordinal);
        plannedOccupiedNames.Add(displayName);
        var runtimeNames = new List<string>();
        for (var index = 1; index < hops.Count; index++)
        {
            runtimeNames.Add(index == hops.Count - 1
                ? displayName
                : ReserveInternalProxyName(customProxy, index, plannedOccupiedNames));
        }

        if (topology.WouldCreateCustomChainCycle(proxyGroupName, firstHop.Name, runtimeNames))
        {
            return new RuntimeDialerProxyBuildResult([], true);
        }

        occupiedNames.UnionWith(runtimeNames);
        var result = new List<YamlMappingNode>();
        var previousName = firstHop.Name;
        for (var index = 1; index < hops.Count; index++)
        {
            var runtimeName = runtimeNames[index - 1];
            var runtimeProxy = Clone(proxyByName[hops[index].Name]);
            SetScalar(runtimeProxy, "name", runtimeName);
            SetScalar(runtimeProxy, "dialer-proxy", previousName);

            result.Add(runtimeProxy);
            previousName = runtimeName;
        }

        return new RuntimeDialerProxyBuildResult(result, false);
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
                clone.Children[new YamlScalarNode("proxies")] = BuildProxyGroupEntries(
                    Scalar(group, "name"),
                    proxies,
                    disabledBuiltinNames,
                    customGroupEntries);
            }
            else
            {
                var entries = BuildProxyGroupEntries(
                    Scalar(group, "name"),
                    new YamlSequenceNode(),
                    disabledBuiltinNames,
                    customGroupEntries);
                if (entries.Children.Count > 0)
                {
                    clone.Children[new YamlScalarNode("proxies")] = entries;
                }
            }

            groups.Add(clone);
        }

        return new YamlSequenceNode(groups);
    }

    private static YamlSequenceNode BuildProxyGroupEntries(
        string proxyGroupName,
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
        }

        foreach (var customEntry in customGroupEntries.Where(item => string.Equals(item.ProxyGroupName, proxyGroupName, StringComparison.Ordinal)))
        {
            // 自定义链只挂到用户明确选择的代理组。
            if (!ContainsScalar(entries, customEntry.DisplayName))
            {
                entries.Add(new YamlScalarNode(customEntry.DisplayName));
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

    private static string FirstValidHopName(SubscriptionCustomChainProxy customProxy)
    {
        return customProxy.Hops
            .Select(hop => hop.Name.Trim())
            .First(name => !string.IsNullOrWhiteSpace(name));
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
