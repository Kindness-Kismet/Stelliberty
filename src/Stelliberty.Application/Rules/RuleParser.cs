using Stelliberty.Domain.Rules;
using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Rules;

public sealed class RuleParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<RuleItem> Parse(string configContent)
    {
        var apiRules = ParseCoreRulesPayload(configContent);
        if (apiRules is not null)
        {
            return apiRules;
        }

        var root = LoadRoot(configContent);
        if (root is null)
        {
            return [];
        }

        var providerRules = ParseRuleProviders(root);
        var rules = ParseRules(root);
        return providerRules.Concat(rules).ToList();
    }

    private static IReadOnlyList<RuleItem>? ParseCoreRulesPayload(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CoreRulesPayload>(content, JsonOptions);
            return payload?.Rules?.Select(rule => new RuleItem(rule.Type ?? string.Empty, rule.Payload ?? string.Empty, rule.Proxy ?? string.Empty)).ToList() ?? [];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static YamlMappingNode? LoadRoot(string configContent)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(configContent));
            return stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode as YamlMappingNode;
        }
        catch (YamlException)
        {
            return null;
        }
    }

    private static IReadOnlyList<RuleItem> ParseRuleProviders(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("rule-providers"), out var providersNode)
            || providersNode is not YamlMappingNode providers)
        {
            return [];
        }

        return providers.Children
            .Where(pair => pair.Key is YamlScalarNode)
            .Select(pair => ParseRuleProvider((YamlScalarNode)pair.Key, pair.Value))
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Payload))
            .ToList();
    }

    private static RuleItem ParseRuleProvider(YamlScalarNode nameNode, YamlNode providerNode)
    {
        if (providerNode is not YamlMappingNode provider)
        {
            return new RuleItem("RULE-PROVIDER", nameNode.Value ?? string.Empty, string.Empty);
        }

        var type = Scalar(provider, "type");
        var path = Scalar(provider, "path");
        var url = Scalar(provider, "url");
        var location = string.IsNullOrWhiteSpace(path) ? url : path;
        return new RuleItem(
            "RULE-PROVIDER",
            nameNode.Value ?? string.Empty,
            string.Join(' ', new[] { type, location }.Where(value => !string.IsNullOrWhiteSpace(value))),
            Source: "rule-providers",
            RuleCount: ParseInt(Scalar(provider, "ruleCount")));
    }

    private static IReadOnlyList<RuleItem> ParseRules(YamlMappingNode root)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode("rules"), out var rulesNode)
            || rulesNode is not YamlSequenceNode rules)
        {
            return [];
        }

        return rules.Children
            .OfType<YamlScalarNode>()
            .Select(node => ParseRule(node.Value ?? string.Empty))
            .Where(IsValidRule)
            .ToList();
    }

    private static RuleItem ParseRule(string rule)
    {
        var parts = rule.Split(',');
        if (parts.Length < 2)
        {
            return new RuleItem(rule, string.Empty, string.Empty);
        }

        var type = parts[0].Trim();
        if (string.Equals(type, "MATCH", StringComparison.OrdinalIgnoreCase))
        {
            return new RuleItem(type, string.Empty, parts[1].Trim());
        }

        var options = parts.Length > 3 ? string.Join(",", parts.Skip(3).Select(part => part.Trim())) : string.Empty;
        return new RuleItem(type, parts[1].Trim(), parts.Length >= 3 ? parts[2].Trim() : string.Empty, options);
    }

    private static bool IsValidRule(RuleItem rule)
    {
        return !string.IsNullOrWhiteSpace(rule.Type)
            && (string.Equals(rule.Type, "MATCH", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(rule.Payload));
    }

    private static string Scalar(YamlMappingNode mapping, string key)
    {
        return mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) && node is YamlScalarNode scalar
            ? scalar.Value ?? string.Empty
            : string.Empty;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out var result) ? result : 0;
    }

    private sealed class CoreRulesPayload
    {
        public IReadOnlyList<CoreRulePayload> Rules { get; set; } = [];
    }

    private sealed class CoreRulePayload
    {
        public string Type { get; set; } = string.Empty;

        public string Payload { get; set; } = string.Empty;

        public string Proxy { get; set; } = string.Empty;
    }
}
