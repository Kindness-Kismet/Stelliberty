namespace Stelliberty.Application.Rules;

public enum RuleTypeBucket
{
    All,
    Domain,
    Ip,
    RuleSet,
    Other,
}

public static class RuleTypeClassifier
{
    public static RuleTypeBucket Classify(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return RuleTypeBucket.Other;
        }

        if (type.StartsWith("DOMAIN", StringComparison.Ordinal))
        {
            return RuleTypeBucket.Domain;
        }

        if (type.StartsWith("IP-", StringComparison.Ordinal) || type.StartsWith("GEOIP", StringComparison.Ordinal) || type.StartsWith("SRC-IP", StringComparison.Ordinal))
        {
            return RuleTypeBucket.Ip;
        }

        if (type.StartsWith("RULE-SET", StringComparison.Ordinal) || type.StartsWith("RULE-PROVIDER", StringComparison.Ordinal))
        {
            return RuleTypeBucket.RuleSet;
        }

        return RuleTypeBucket.Other;
    }

    public static bool MatchesBucket(string type, RuleTypeBucket bucket)
    {
        return bucket == RuleTypeBucket.All || Classify(type) == bucket;
    }
}
