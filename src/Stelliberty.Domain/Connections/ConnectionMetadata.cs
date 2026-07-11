namespace Stelliberty.Domain.Connections;

public sealed record ConnectionMetadata(
    string Type = "",
    string Network = "",
    string SourceIp = "",
    string SourcePort = "",
    IReadOnlyList<string>? SourceGeoIp = null,
    string SourceIpAsn = "",
    string DestinationIp = "",
    string DestinationPort = "",
    IReadOnlyList<string>? DestinationGeoIp = null,
    string DestinationIpAsn = "",
    string Host = "",
    string SniffHost = "",
    string Process = "",
    string ProcessPath = "",
    int? Uid = null,
    string InboundIp = "",
    string InboundPort = "",
    string InboundName = "",
    string InboundUser = "",
    int Dscp = 0,
    string RemoteDestination = "",
    string DnsMode = "",
    string SpecialProxy = "",
    string SpecialRules = "")
{
    public IReadOnlyList<string> SourceGeoIp { get; init; } = SourceGeoIp ?? [];

    public IReadOnlyList<string> DestinationGeoIp { get; init; } = DestinationGeoIp ?? [];

    public string Description
    {
        get
        {
            var host = DisplayHost;
            return $"{Network}://{host}:{DestinationPort}";
        }
    }

    public string DisplayHost
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SniffHost))
            {
                return SniffHost;
            }

            if (!string.IsNullOrWhiteSpace(Host))
            {
                return Host;
            }

            return !string.IsNullOrWhiteSpace(DestinationIp) ? DestinationIp : RemoteDestination;
        }
    }
}
