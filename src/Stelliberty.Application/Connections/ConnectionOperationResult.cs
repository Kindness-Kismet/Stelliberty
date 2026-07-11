using Stelliberty.Domain.Connections;
namespace Stelliberty.Application.Connections;

public sealed record ConnectionOperationResult(
    ConnectionListState State,
    ConnectionCloseRequest Request,
    IReadOnlyList<string> ClosedConnectionIds,
    bool HasClosedAllConnections);
