using Stelliberty.Application.Localization;
using Stelliberty.Application.Rules;
using Stelliberty.Domain.Rules;

namespace Stelliberty.Presentation.ViewModels;

public sealed record RuleRowViewModel(int Index, RuleItem Rule, ILocalizationService? Localization = null)
{
    public string IndexText => Index.ToString();

    public string RowAutomationId => $"Rules.Row.{Index}";

    public string PayloadAutomationId => $"Rules.Row.{Index}.PayloadText";

    public string TypeAutomationId => $"Rules.Row.{Index}.TypeText";

    public string OptionsAutomationId => $"Rules.Row.{Index}.OptionsText";

    public string ProxyAutomationId => $"Rules.Row.{Index}.ProxyText";

    public string SourceAutomationId => $"Rules.Row.{Index}.SourceText";

    public string RuleCountAutomationId => $"Rules.Row.{Index}.CountText";

    public string Type => Rule.Type;

    public string Payload => string.IsNullOrWhiteSpace(Rule.Payload) ? "-" : Rule.Payload;

    public string Proxy => Rule.Proxy;

    public string Policy => Rule.Proxy;

    public string Options => string.IsNullOrWhiteSpace(Rule.Options) ? string.Empty : Rule.Options;

    public string SourceText => string.IsNullOrWhiteSpace(Rule.Source) ? string.Empty : string.Format(Localize("Rules.Row.Source"), Rule.Source);

    public string RuleCountText => Rule.RuleCount <= 0 ? string.Empty : string.Format(Localize("Rules.Row.Count"), Rule.RuleCount);

    public string TypePillTag
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Type))
            {
                return "other";
            }
            if (Type.StartsWith("DOMAIN", StringComparison.Ordinal))
            {
                return "domain";
            }
            if (Type.StartsWith("IP-", StringComparison.Ordinal) || Type.StartsWith("GEOIP", StringComparison.Ordinal) || Type.StartsWith("SRC-IP", StringComparison.Ordinal))
            {
                return "ip";
            }
            if (Type.StartsWith("RULE-SET", StringComparison.Ordinal) || Type.StartsWith("RULE-PROVIDER", StringComparison.Ordinal))
            {
                return "ruleset";
            }
            return "other";
        }
    }

    private string Localize(string key) => Localization?.GetString(key) ?? key;
}
