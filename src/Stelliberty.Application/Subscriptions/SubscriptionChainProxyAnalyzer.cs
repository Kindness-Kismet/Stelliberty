using Stelliberty.Domain.Subscriptions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

public sealed class SubscriptionChainProxyAnalyzer
{
    public IReadOnlyList<string> AnalyzeBuiltinChainProxyNames(string content)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(content));
            if (stream.Documents[0].RootNode is not YamlMappingNode root
                || !root.Children.TryGetValue(new YamlScalarNode("proxies"), out var proxiesNode)
                || proxiesNode is not YamlSequenceNode proxies)
            {
                return [];
            }

            return proxies
                .OfType<YamlMappingNode>()
                .Where(proxy => HasNonEmptyScalar(proxy, "dialer-proxy"))
                .Select(proxy => Scalar(proxy, "name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch (YamlException)
        {
            return [];
        }
    }

    private static bool HasNonEmptyScalar(YamlMappingNode mapping, string key)
    {
        return !string.IsNullOrWhiteSpace(Scalar(mapping, key));
    }

    private static string Scalar(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value.ToString() : string.Empty;
    }
}
