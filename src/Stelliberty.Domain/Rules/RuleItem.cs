namespace Stelliberty.Domain.Rules;

public sealed record RuleItem(string Type, string Payload, string Proxy, string Options = "", string Source = "", int RuleCount = 0);
