namespace Stelliberty.Domain.Rules;

public sealed record EditableRule(
    string Id,
    string Type,
    string Payload,
    string Proxy,
    string Options = "",
    bool IsEnabled = true)
{
    public string Key => RuleKey.Create(Type, Payload, Proxy, Options);
    public string MatchKey => RuleKey.CreateMatch(Type, Payload, Options);

    public string Render()
    {
        var parts = new List<string> { Type.Trim() };
        if (!string.Equals(Type.Trim(), "MATCH", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(Payload.Trim());
        }

        parts.Add(Proxy.Trim());
        if (!string.IsNullOrWhiteSpace(Options))
        {
            parts.Add(Options.Trim());
        }

        return string.Join(',', parts);
    }
}

public sealed record RuleOverrideSet(
    string SubscriptionId,
    IReadOnlyList<EditableRule>? CustomRules = null,
    IReadOnlySet<string>? DisabledBuiltinRuleKeys = null,
    IReadOnlyList<RuleTemplate>? Templates = null,
    IReadOnlyList<string>? RuleOrder = null)
{
    public IReadOnlyList<EditableRule> CustomRules { get; init; } = CustomRules ?? [];

    public IReadOnlySet<string> DisabledBuiltinRuleKeys { get; init; } = DisabledBuiltinRuleKeys ?? new HashSet<string>(StringComparer.Ordinal);

    public IReadOnlyList<RuleTemplate> Templates { get; init; } = Templates ?? [];

    public IReadOnlyList<string> RuleOrder { get; init; } = RuleOrder ?? [];
}

public sealed record RuleTemplate(
    string Id,
    string Name,
    IReadOnlyList<EditableRule>? Rules = null)
{
    public IReadOnlyList<EditableRule> Rules { get; init; } = Rules ?? [];
}

public static class RuleKey
{
    public static string Create(string type, string payload, string proxy, string options)
        => string.Join('', [
            Normalize(type),
            Normalize(payload),
            Normalize(proxy),
            Normalize(options),
        ]);

    public static string Normalize(string value)
        => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    public static string CreateMatch(string type, string payload, string options)
        => string.Join('', [Normalize(type), Normalize(payload)]);
}

public static class RuleOrderKey
{
    public static string Builtin(string key) => $"builtin:{key}";

    public static string Custom(string id) => $"custom:{id}";
}
