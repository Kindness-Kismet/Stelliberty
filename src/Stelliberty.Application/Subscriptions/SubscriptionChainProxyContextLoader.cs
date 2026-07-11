using Stelliberty.Application.Overrides;
using Stelliberty.Application.Runtime;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

// 按运行时顺序应用覆写，让对话框贴近最终配置。
public sealed class SubscriptionChainProxyContextLoader(
    ISubscriptionStore subscriptionStore,
    IConfigOverrideEngine overrideEngine,
    IOverrideStore? overrideStore = null)
{
    private readonly SubscriptionOverrideResolver _overrideResolver = new(overrideStore);

    public SubscriptionChainProxyContext Load(string subscriptionId)
    {
        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId)
            ?? throw new InvalidOperationException($"Selected subscription not found: {subscriptionId}");
        var resolvedConfig = ApplyOverrides(subscriptionStore.ReadContent(subscription.Id), _overrideResolver.Resolve(subscription));
        return Parse(resolvedConfig);
    }

    private string ApplyOverrides(string content, IReadOnlyList<RuntimeOverride> overrides)
    {
        var current = content;
        foreach (var runtimeOverride in overrides)
        {
            current = overrideEngine.Apply(current, runtimeOverride);
        }

        return current;
    }

    private static SubscriptionChainProxyContext Parse(string content)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(content));
            if (stream.Documents.Count == 0
                || stream.Documents[0].RootNode is not YamlMappingNode root
                || !root.Children.TryGetValue(new YamlScalarNode("proxies"), out var proxiesNode)
                || proxiesNode is not YamlSequenceNode proxies)
            {
                return new SubscriptionChainProxyContext([], []);
            }

            var builtinNames = new List<string>();
            var candidates = new List<ChainProxyNodeOption>();
            foreach (var proxy in proxies.OfType<YamlMappingNode>())
            {
                var name = Scalar(proxy, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // dialer-proxy 标记内置链式节点，不代表自定义跳点。
                if (!string.IsNullOrWhiteSpace(Scalar(proxy, "dialer-proxy")))
                {
                    builtinNames.Add(name);
                    continue;
                }

                var type = Scalar(proxy, "type");
                if (!string.IsNullOrWhiteSpace(type))
                {
                    candidates.Add(new ChainProxyNodeOption(name, type));
                }
            }

            return new SubscriptionChainProxyContext(
                builtinNames.Distinct(StringComparer.Ordinal).ToList(),
                candidates);
        }
        catch (YamlException)
        {
            return new SubscriptionChainProxyContext([], []);
        }
    }

    private static string Scalar(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value.ToString() : string.Empty;
    }
}
