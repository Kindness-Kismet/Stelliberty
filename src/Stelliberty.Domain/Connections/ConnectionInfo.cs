namespace Stelliberty.Domain.Connections;

public sealed record ConnectionInfo(
    string Id,
    long Upload = 0,
    long Download = 0,
    long UploadSpeed = 0,
    long DownloadSpeed = 0,
    DateTimeOffset Start = default,
    ConnectionMetadata? Metadata = null,
    IReadOnlyList<string>? Chains = null,
    string Rule = "",
    string RulePayload = "")
{
    public ConnectionMetadata Metadata { get; init; } = Metadata ?? new ConnectionMetadata();

    public IReadOnlyList<string> Chains { get; init; } = Chains ?? [];

    public string ProxyGroup => Chains.Count > 0 ? Chains[0] : "DIRECT";

    public string ProxyNode => Chains.Count > 0 ? Chains[^1] : "DIRECT";

    public string LegacyProxyChain => string.Join(" → ", Chains.Reverse());
}
