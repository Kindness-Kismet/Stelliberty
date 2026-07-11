namespace Stelliberty.Domain.Proxies;

public enum OutboundMode
{
    Rule,
    Global,
    Direct,
}

public static class OutboundModeParser
{
    public static OutboundMode? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "rule" => OutboundMode.Rule,
            "global" => OutboundMode.Global,
            "direct" => OutboundMode.Direct,
            _ => null,
        };
    }
}
