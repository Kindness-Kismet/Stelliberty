namespace Stelliberty.Domain.Connections;

public sealed class ConnectionFilter
{
    private const string DirectProxy = "DIRECT";

    public IReadOnlyList<ConnectionInfo> Apply(
        IReadOnlyList<ConnectionInfo> connections,
        ConnectionFilterLevel level,
        string searchKeyword)
    {
        IEnumerable<ConnectionInfo> filtered = level switch
        {
            ConnectionFilterLevel.Direct => connections.Where(connection => connection.ProxyNode == DirectProxy),
            ConnectionFilterLevel.Proxy => connections.Where(connection => connection.ProxyNode != DirectProxy),
            _ => connections
        };

        var normalizedKeyword = searchKeyword.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            filtered = filtered.Where(connection => Matches(connection, normalizedKeyword));
        }

        return filtered.ToList();
    }

    private static bool Matches(ConnectionInfo connection, string keyword)
    {
        var metadata = connection.Metadata;
        return Contains(metadata.Description, keyword)
            || Contains(metadata.SourceIp, keyword)
            || Contains(metadata.DestinationIp, keyword)
            || Contains(metadata.Host, keyword)
            || Contains(metadata.SniffHost, keyword)
            || Contains(connection.ProxyNode, keyword)
            || Contains(connection.Rule, keyword)
            || Contains(connection.RulePayload, keyword)
            || Contains(metadata.Process, keyword)
            || Contains(metadata.ProcessPath, keyword)
            || Contains(metadata.RemoteDestination, keyword)
            || Contains(metadata.InboundName, keyword)
            || connection.Chains.Any(chain => Contains(chain, keyword));
    }

    private static bool Contains(string value, string keyword)
    {
        return value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
