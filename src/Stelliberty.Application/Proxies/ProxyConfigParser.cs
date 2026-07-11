using Stelliberty.Domain.Proxies;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Proxies;

public sealed class ProxyConfigParser
{
    public ProxyConfig Parse(string configContent)
    {
        var root = LoadRoot(configContent);
        var mode = OutboundModeParser.TryParse(OptionalScalar(root, "mode"));
        return new ProxyConfig(ParseGroups(root), ParseNodes(root), mode);
    }

    private static YamlMappingNode LoadRoot(string content)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(content));
            if (stream.Documents.Count == 0)
            {
                return new YamlMappingNode();
            }

            return stream.Documents[0].RootNode as YamlMappingNode
                ?? new YamlMappingNode();
        }
        catch (YamlException)
        {
            return new YamlMappingNode();
        }
    }

    private static IReadOnlyList<ProxyGroup> ParseGroups(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("proxy-groups"), out var node)
            || node is not YamlSequenceNode groups)
        {
            return [];
        }

        return groups.Children
            .OfType<YamlMappingNode>()
            .Select(group => new ProxyGroup(
                Name: Scalar(group, "name"),
                Type: Scalar(group, "type"),
                Now: OptionalScalar(group, "now"),
                All: Sequence(group, "proxies"),
                Fixed: OptionalScalar(group, "fixed"),
                IsHidden: OptionalBool(group, "hidden"),
                Icon: OptionalScalar(group, "icon"),
                Delay: OptionalInt(group, "delay")))
            .Where(group => !string.IsNullOrWhiteSpace(group.Name))
            .ToList();
    }

    private static IReadOnlyDictionary<string, ProxyNode> ParseNodes(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("proxies"), out var node)
            || node is not YamlSequenceNode proxies)
        {
            return new Dictionary<string, ProxyNode>();
        }

        var result = new Dictionary<string, ProxyNode>();
        foreach (var proxy in proxies.Children.OfType<YamlMappingNode>())
        {
            var proxyNode = new ProxyNode(
                Name: Scalar(proxy, "name"),
                Type: Scalar(proxy, "type"),
                Server: OptionalScalar(proxy, "server"),
                Port: OptionalInt(proxy, "port"));
            if (!string.IsNullOrWhiteSpace(proxyNode.Name))
            {
                result[proxyNode.Name] = proxyNode;
            }
        }

        return result;
    }

    private static string Scalar(YamlMappingNode map, string key)
    {
        return OptionalScalar(map, key) ?? string.Empty;
    }

    private static string? OptionalScalar(YamlMappingNode map, string key)
    {
        return map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value.ToString() : null;
    }

    private static int? OptionalInt(YamlMappingNode map, string key)
    {
        return int.TryParse(OptionalScalar(map, key), out var value) ? value : null;
    }

    private static bool OptionalBool(YamlMappingNode map, string key)
    {
        return bool.TryParse(OptionalScalar(map, key), out var value) && value;
    }

    private static IReadOnlyList<string> Sequence(YamlMappingNode map, string key)
    {
        if (!map.Children.TryGetValue(new YamlScalarNode(key), out var value)
            || value is not YamlSequenceNode sequence)
        {
            return [];
        }

        return sequence.Children.Select(item => item.ToString()).ToList();
    }
}
