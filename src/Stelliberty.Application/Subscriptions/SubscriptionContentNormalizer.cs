using Stelliberty.Domain.Subscriptions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Subscriptions;

public sealed partial class SubscriptionContentNormalizer
{
    public SubscriptionSourceFormat DetectSourceFormat(string content)
    {
        var trimmed = content.Trim();
        if (IsClashYaml(trimmed))
        {
            return SubscriptionSourceFormat.StandardClash;
        }

        var normalizedInput = DecodeBase64Subscription(trimmed);
        return !ReferenceEquals(normalizedInput, trimmed) && IsClashYaml(normalizedInput)
            ? SubscriptionSourceFormat.StandardClash
            : SubscriptionSourceFormat.NonStandard;
    }

    public string Normalize(string content)
    {
        var trimmed = content.Trim();
        if (IsClashYaml(trimmed))
        {
            return content;
        }

        var yamlProxies = ParseYamlProxies(trimmed);
        if (yamlProxies.Count > 0)
        {
            return GenerateClashConfig(yamlProxies);
        }

        var normalizedInput = DecodeBase64Subscription(trimmed);
        if (!ReferenceEquals(normalizedInput, trimmed) && IsClashYaml(normalizedInput))
        {
            return normalizedInput;
        }

        yamlProxies = ParseYamlProxies(normalizedInput);
        if (yamlProxies.Count > 0)
        {
            return GenerateClashConfig(yamlProxies);
        }

        var proxies = ParseProxyLinks(normalizedInput);
        if (proxies.Count == 0)
        {
            return content;
        }

        return GenerateClashConfig(proxies);
    }

    private static bool IsClashYaml(string content)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(content));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                return false;
            }

            // 完整 Clash 配置原样返回，以保留 providers。
            return root.Children.ContainsKey(new YamlScalarNode("proxy-groups"))
                || root.Children.ContainsKey(new YamlScalarNode("proxy-providers"))
                || (root.Children.ContainsKey(new YamlScalarNode("proxies"))
                    && root.Children.ContainsKey(new YamlScalarNode("rules")));
        }
        catch (YamlException)
        {
            return false;
        }
    }

    private static IReadOnlyList<YamlMappingNode> ParseProxyLinks(string content)
    {
        var proxies = new List<YamlMappingNode>();
        foreach (var line in content.Split('\n'))
        {
            var proxy = ParseProxyLink(line.Trim());
            if (proxy is not null)
            {
                proxies.Add(proxy);
            }
        }

        return proxies;
    }

    private static IReadOnlyList<YamlMappingNode> ParseYamlProxies(string content)
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
                return [];
            }

            return proxies.Children.OfType<YamlMappingNode>().ToList();
        }
        catch (YamlException)
        {
            return [];
        }
    }

    private static string DecodeBase64Subscription(string content)
    {
        var compact = new string(content.Where(character => !char.IsWhiteSpace(character)).ToArray());
        // 短文本容易误判为 base64，超过 50 个字符才解码。
        if (compact.Length <= 50 || compact.Any(character => !IsBase64Character(character)))
        {
            return content;
        }

        return DecodeBase64Text(compact, content);
    }

    private static YamlMappingNode? ParseProxyLink(string link)
    {
        if (string.IsNullOrWhiteSpace(link) || link.StartsWith('#'))
        {
            return null;
        }

        if (link.StartsWith("ss://", StringComparison.Ordinal))
        {
            return ParseShadowsocks(link);
        }

        if (link.StartsWith("vmess://", StringComparison.Ordinal))
        {
            return ParseVmess(link);
        }

        if (link.StartsWith("trojan://", StringComparison.Ordinal))
        {
            return ParseTrojan(link);
        }

        if (link.StartsWith("hysteria2://", StringComparison.Ordinal) || link.StartsWith("hy2://", StringComparison.Ordinal))
        {
            return ParseHysteria2(link);
        }

        if (link.StartsWith("hysteria://", StringComparison.Ordinal))
        {
            return ParseHysteria(link);
        }

        if (link.StartsWith("tuic://", StringComparison.Ordinal))
        {
            return ParseTuic(link);
        }

        if (link.StartsWith("http://", StringComparison.Ordinal) || link.StartsWith("https://", StringComparison.Ordinal))
        {
            return ParseHttp(link);
        }

        if (link.StartsWith("socks://", StringComparison.Ordinal) || link.StartsWith("socks5://", StringComparison.Ordinal))
        {
            return ParseSocks(link);
        }

        if (link.StartsWith("ssr://", StringComparison.Ordinal))
        {
            return ParseShadowsocksR(link);
        }

        if (link.StartsWith("vless://", StringComparison.Ordinal))
        {
            return ParseVless(link);
        }

        return null;
    }

    private static string GenerateClashConfig(IReadOnlyList<YamlMappingNode> proxies)
    {
        var proxyNames = proxies
            .Select(proxy => proxy.Children[new YamlScalarNode("name")].ToString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        var proxyGroup = new YamlMappingNode();
        Set(proxyGroup, "name", "PROXY");
        Set(proxyGroup, "type", "select");
        Set(proxyGroup, "proxies", Sequence(proxyNames));
        var autoGroup = new YamlMappingNode();
        // 默认健康检查使用 generate_204，每 300 秒一次。
        Set(autoGroup, "name", "AUTO");
        Set(autoGroup, "type", "url-test");
        Set(autoGroup, "proxies", Sequence(proxyNames));
        Set(autoGroup, "url", "https://www.gstatic.com/generate_204");
        Set(autoGroup, "interval", "300");
        var root = new YamlMappingNode();
        Set(root, "proxies", new YamlSequenceNode(proxies));
        Set(root, "proxy-groups", new YamlSequenceNode(proxyGroup, autoGroup));
        Set(root, "rules", Sequence(["MATCH,PROXY"]));
        var stream = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }
}
