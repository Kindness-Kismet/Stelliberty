using System.Text;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Rules;
using YamlDotNet.RepresentationModel;

namespace Stelliberty.Application.Rules;

public sealed record RuleEditorItem(
    string Id,
    string Type,
    string Payload,
    string Proxy,
    string Options,
    string Source,
    bool IsBuiltIn,
    bool IsEnabled,
    int RuleCount = 0)
{
    public string Key => RuleKey.Create(Type, Payload, Proxy, Options);
    public string MatchKey => RuleKey.CreateMatch(Type, Payload, Options);
    public string OrderId => IsBuiltIn ? RuleOrderKey.Builtin(Key) : RuleOrderKey.Custom(Id);

    public EditableRule ToEditableRule() => new(Id, Type, Payload, Proxy, Options, IsEnabled);
}

public sealed record RuleEditorSnapshot(
    string SubscriptionId,
    IReadOnlyList<RuleEditorItem> Items,
    IReadOnlyList<RuleTemplate> Templates,
    bool HasSubscription);

public enum RuleOverrideError
{
    InvalidRule,
    DuplicateCustomRule,
    DuplicateBuiltinRule,
    SubscriptionNotFound,
}

public sealed class RuleOverrideException(RuleOverrideError error) : InvalidOperationException(error.ToString())
{
    public RuleOverrideError Error { get; } = error;
}

public sealed class RuleOverrideService(
    ISubscriptionStore subscriptionStore,
    ISubscriptionSelectionStore selectionStore,
    IRuleOverrideStore overrideStore,
    RuleParser parser)
{
    public RuleEditorSnapshot LoadCurrent()
    {
        var subscriptionId = selectionStore.GetCurrentSubscriptionId();
        if (string.IsNullOrWhiteSpace(subscriptionId))
        {
            return new RuleEditorSnapshot(string.Empty, [], [], false);
        }

        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null)
        {
            return new RuleEditorSnapshot(subscriptionId, [], [], false);
        }

        var parsedRules = parser.Parse(subscriptionStore.ReadContent(subscriptionId))
            .Where(rule => !string.Equals(rule.Source, "rule-providers", StringComparison.Ordinal))
            .ToList();
        var matchCounts = parsedRules
            .GroupBy(rule => RuleKey.CreateMatch(rule.Type, rule.Payload, rule.Options), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var baseline = parsedRules
            .GroupBy(rule => RuleKey.Create(rule.Type, rule.Payload, rule.Proxy, rule.Options), StringComparer.Ordinal)
            .Select(group => group.First() with
            {
                RuleCount = matchCounts[RuleKey.CreateMatch(group.First().Type, group.First().Payload, group.First().Options)]
            })
            .ToList();
        var set = overrideStore.Load(subscriptionId);
        var seenCustomKeys = new HashSet<string>(StringComparer.Ordinal);
        var builtinItems = baseline
            .Select((rule, index) => new RuleEditorItem(
                $"builtin-{index + 1}",
                rule.Type,
                rule.Payload,
                rule.Proxy,
                rule.Options,
                string.IsNullOrWhiteSpace(rule.Source) ? "subscription" : rule.Source,
                true,
                !set.DisabledBuiltinRuleKeys.Contains(RuleKey.Create(rule.Type, rule.Payload, rule.Proxy, rule.Options)),
                rule.RuleCount))
            .ToList();
        var customItems = set.CustomRules
                .Where(rule => seenCustomKeys.Add(rule.Key))
                .Select(rule => new RuleEditorItem(rule.Id, rule.Type, rule.Payload, rule.Proxy, rule.Options, "local", false, rule.IsEnabled))
                .ToList();
        var items = MergeRuleOrder(builtinItems, customItems, set.RuleOrder);

        return new RuleEditorSnapshot(subscriptionId, items, set.Templates, true);
    }

    public void Save(
        string subscriptionId,
        IReadOnlyList<EditableRule> customRules,
        IReadOnlySet<string> disabledBuiltinRuleKeys,
        IReadOnlyList<string>? ruleOrder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentNullException.ThrowIfNull(customRules);
        ArgumentNullException.ThrowIfNull(disabledBuiltinRuleKeys);
        ValidateCustomRules(subscriptionId, customRules, disabledBuiltinRuleKeys, ruleOrder ?? []);
        overrideStore.Save(new RuleOverrideSet(subscriptionId, customRules.ToList(), disabledBuiltinRuleKeys, RuleOrder: ruleOrder?.ToList() ?? []));
    }

    public void UpsertTemplate(RuleTemplate template) => overrideStore.UpsertTemplate(template);

    public void DeleteTemplate(string templateId) => overrideStore.DeleteTemplate(templateId);

    public string Apply(string subscriptionId, string configContent)
    {
        var set = overrideStore.Load(subscriptionId);
        if (set.CustomRules.Count == 0 && set.DisabledBuiltinRuleKeys.Count == 0 && set.RuleOrder.Count == 0)
        {
            return configContent;
        }

        var stream = new YamlStream();
        stream.Load(new StringReader(configContent));
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidOperationException("Rule override base config must be a YAML mapping");
        }

        var rulesKey = new YamlScalarNode("rules");
        var existing = root.Children.TryGetValue(rulesKey, out var node)
            ? node is YamlSequenceNode sequence
                ? ReadRules(sequence)
                : throw new InvalidOperationException("Rules node must be a YAML sequence")
            : [];
        var builtinRules = existing
            .Select(rule => new OrderedRule(RuleOrderKey.Builtin(ParseKey(rule)), rule))
            .Where(rule => !set.DisabledBuiltinRuleKeys.Contains(ParseOrderKey(rule.OrderId)))
            .ToList();
        var customRules = set.CustomRules
            .Where(rule => rule.IsEnabled)
            .Select(rule => new OrderedRule(RuleOrderKey.Custom(rule.Id), rule.Render()))
            .ToList();
        var ordered = MergeRuleOrder(builtinRules, customRules, set.RuleOrder).Select(rule => rule.Rule).ToList();
        root.Children[rulesKey] = new YamlSequenceNode(ordered.Select(rule => (YamlNode)new YamlScalarNode(rule)));

        using var writer = new StringWriter(new StringBuilder(), System.Globalization.CultureInfo.InvariantCulture);
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    private static List<RuleEditorItem> MergeRuleOrder(
        IReadOnlyList<RuleEditorItem> builtinItems,
        IReadOnlyList<RuleEditorItem> customItems,
        IReadOnlyList<string> ruleOrder)
    {
        if (ruleOrder.Count == 0)
        {
            return DefaultEditorOrder(builtinItems, customItems);
        }

        var byOrderId = builtinItems.Concat(customItems)
            .GroupBy(item => item.OrderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var ordered = new List<RuleEditorItem>();
        foreach (var orderId in ruleOrder)
        {
            if (byOrderId.Remove(orderId, out var item))
            {
                ordered.Add(item);
            }
        }

        ordered.AddRange(DefaultEditorOrder(
            byOrderId.Values.Where(item => item.IsBuiltIn).ToList(),
            byOrderId.Values.Where(item => !item.IsBuiltIn).ToList()));
        return ordered;
    }

    private static List<RuleEditorItem> DefaultEditorOrder(IReadOnlyList<RuleEditorItem> builtinItems, IReadOnlyList<RuleEditorItem> customItems)
    {
        var ordered = builtinItems.ToList();
        var matchIndex = ordered.FindIndex(item => string.Equals(item.Type, "MATCH", StringComparison.OrdinalIgnoreCase));
        ordered.InsertRange(matchIndex < 0 ? ordered.Count : matchIndex, customItems);
        return ordered;
    }

    private static List<OrderedRule> MergeRuleOrder(
        IReadOnlyList<OrderedRule> builtinRules,
        IReadOnlyList<OrderedRule> customRules,
        IReadOnlyList<string> ruleOrder)
    {
        if (ruleOrder.Count == 0)
        {
            return DefaultRuntimeOrder(builtinRules, customRules);
        }

        var byOrderId = builtinRules.Concat(customRules)
            .GroupBy(rule => rule.OrderId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var ordered = new List<OrderedRule>();
        foreach (var orderId in ruleOrder)
        {
            if (byOrderId.Remove(orderId, out var rule))
            {
                ordered.Add(rule);
            }
        }

        ordered.AddRange(DefaultRuntimeOrder(
            byOrderId.Values.Where(rule => rule.OrderId.StartsWith("builtin:", StringComparison.Ordinal)).ToList(),
            byOrderId.Values.Where(rule => rule.OrderId.StartsWith("custom:", StringComparison.Ordinal)).ToList()));
        return ordered;
    }

    private static List<OrderedRule> DefaultRuntimeOrder(IReadOnlyList<OrderedRule> builtinRules, IReadOnlyList<OrderedRule> customRules)
    {
        var ordered = builtinRules.ToList();
        var matchIndex = ordered.FindIndex(rule => string.Equals(rule.Rule.Split(',')[0].Trim(), "MATCH", StringComparison.OrdinalIgnoreCase));
        ordered.InsertRange(matchIndex < 0 ? ordered.Count : matchIndex, customRules);
        return ordered;
    }

    private static string ParseOrderKey(string orderId)
        => orderId.StartsWith("builtin:", StringComparison.Ordinal) ? orderId[8..] : orderId;

    private void ValidateCustomRules(string subscriptionId, IReadOnlyList<EditableRule> customRules, IReadOnlySet<string> disabledBuiltinRuleKeys, IReadOnlyList<string> ruleOrder)
    {
        foreach (var rule in customRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || string.IsNullOrWhiteSpace(rule.Type) || string.IsNullOrWhiteSpace(rule.Proxy))
            {
                throw new RuleOverrideException(RuleOverrideError.InvalidRule);
            }

            if (!string.Equals(rule.Type.Trim(), "MATCH", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(rule.Payload))
            {
                throw new RuleOverrideException(RuleOverrideError.InvalidRule);
            }
        }

        var duplicates = customRules
            .GroupBy(rule => rule.MatchKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null)
        {
            throw new RuleOverrideException(RuleOverrideError.DuplicateCustomRule);
        }

        var subscription = subscriptionStore.LoadSubscriptions().FirstOrDefault(item => item.Id == subscriptionId);
        if (subscription is null)
        {
            throw new RuleOverrideException(RuleOverrideError.SubscriptionNotFound);
        }

        var builtinKeys = parser.Parse(subscriptionStore.ReadContent(subscriptionId))
            .Where(rule => !string.Equals(rule.Source, "rule-providers", StringComparison.Ordinal))
            .Where(rule => !disabledBuiltinRuleKeys.Contains(RuleKey.Create(rule.Type, rule.Payload, rule.Proxy, rule.Options)))
            .Select(rule => RuleKey.CreateMatch(rule.Type, rule.Payload, rule.Options))
            .ToHashSet(StringComparer.Ordinal);
        var duplicateBuiltin = customRules.FirstOrDefault(rule => builtinKeys.Contains(rule.MatchKey));
        if (duplicateBuiltin is not null)
        {
            throw new RuleOverrideException(RuleOverrideError.DuplicateBuiltinRule);
        }

        if (disabledBuiltinRuleKeys.Any(key => string.IsNullOrWhiteSpace(key)) || ruleOrder.Any(string.IsNullOrWhiteSpace))
        {
            throw new RuleOverrideException(RuleOverrideError.InvalidRule);
        }
    }

    private static string ParseKey(string rule)
    {
        var parts = rule.Split(',', StringSplitOptions.None);
        if (parts.Length < 2)
        {
            return RuleKey.Create(parts.FirstOrDefault() ?? string.Empty, string.Empty, string.Empty, string.Empty);
        }

        var type = parts[0].Trim();
        if (string.Equals(type, "MATCH", StringComparison.OrdinalIgnoreCase))
        {
            return RuleKey.Create(type, string.Empty, parts[1], parts.Length > 2 ? string.Join(',', parts.Skip(2)) : string.Empty);
        }

        return RuleKey.Create(type, parts[1], parts.Length > 2 ? parts[2] : string.Empty, parts.Length > 3 ? string.Join(',', parts.Skip(3)) : string.Empty);
    }

    private static List<string> ReadRules(YamlSequenceNode sequence)
    {
        if (sequence.Children.Any(item => item is not YamlScalarNode))
        {
            throw new InvalidOperationException("Rules must be YAML scalar values");
        }

        return sequence.Children.Cast<YamlScalarNode>().Select(item => item.Value ?? string.Empty).ToList();
    }

    private sealed record OrderedRule(string OrderId, string Rule);
}
