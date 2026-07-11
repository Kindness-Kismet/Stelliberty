using Stelliberty.Domain.Connections;
namespace Stelliberty.Application.Connections;

public sealed record ConnectionCloseRequest(ConnectionCloseMode Mode, string? ConnectionId = null);
