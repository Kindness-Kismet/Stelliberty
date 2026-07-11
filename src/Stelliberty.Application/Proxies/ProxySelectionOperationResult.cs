using Stelliberty.Domain.Proxies;
using Stelliberty.Application.Connections;
using Stelliberty.Domain.Connections;

namespace Stelliberty.Application.Proxies;

public sealed record ProxySelectionOperationResult(
    ProxySelectionResult Selection,
    ConnectionCloseRequest ConnectionCloseRequest,
    IReadOnlyList<string> ClosedConnectionIds,
    bool HasClosedAllConnections);
