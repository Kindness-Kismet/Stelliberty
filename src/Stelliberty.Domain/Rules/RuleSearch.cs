namespace Stelliberty.Domain.Rules;

public sealed class RuleSearch
{
    public IReadOnlyList<RuleItem> Filter(IReadOnlyList<RuleItem> rules, string keyword)
    {
        var normalizedKeyword = keyword.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            return rules;
        }

        return rules
            .Where(rule => Contains(rule.Type, normalizedKeyword)
                || Contains(rule.Payload, normalizedKeyword)
                || Contains(rule.Proxy, normalizedKeyword)
                || Contains(rule.Options, normalizedKeyword)
                || Contains(rule.Source, normalizedKeyword)
                || Contains(FormatRuleCount(rule.RuleCount), normalizedKeyword))
            .ToList();
    }

    private static bool Contains(string value, string keyword)
    {
        return value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatRuleCount(int ruleCount)
    {
        return ruleCount <= 0 ? string.Empty : $"{ruleCount} rules";
    }
}
