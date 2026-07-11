using Stelliberty.Domain.Connections;
namespace Stelliberty.Application.Connections;

public sealed class ConnectionOperations(IReadOnlyList<ConnectionInfo> connections)
{
    public ConnectionOperationResult CloseConnection(string connectionId)
    {
        if (connections.All(item => item.Id != connectionId))
        {
            throw new InvalidOperationException($"Connection not found: {connectionId}");
        }

        return CloseConnection(connectionId, isMonitoringPaused: false);
    }

    public ConnectionOperationResult CloseConnection(string connectionId, bool isMonitoringPaused)
    {
        if (connections.All(item => item.Id != connectionId))
        {
            throw new InvalidOperationException($"Connection not found: {connectionId}");
        }

        return new ConnectionOperationResult(
            ConnectionListState.Initial with { Connections = connections.Where(item => item.Id != connectionId).ToList(), IsMonitoringPaused = isMonitoringPaused },
            new ConnectionCloseRequest(ConnectionCloseMode.Single, connectionId),
            [connectionId],
            HasClosedAllConnections: false);
    }

    public ConnectionOperationResult CloseAllConnections()
    {
        return CloseAllConnections(isMonitoringPaused: false);
    }

    public ConnectionOperationResult CloseAllConnections(bool isMonitoringPaused)
    {
        return new ConnectionOperationResult(
            ConnectionListState.Initial with { IsMonitoringPaused = isMonitoringPaused },
            new ConnectionCloseRequest(ConnectionCloseMode.All),
            connections.Select(item => item.Id).ToList(),
            HasClosedAllConnections: true);
    }

    public ConnectionDetail ShowDetail(string connectionId)
    {
        var connection = connections.FirstOrDefault(item => item.Id == connectionId)
            ?? throw new InvalidOperationException($"Connection not found: {connectionId}");
        return new ConnectionDetail(connection);
    }
}
