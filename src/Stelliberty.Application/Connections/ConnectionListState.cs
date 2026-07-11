using Stelliberty.Domain.Connections;
namespace Stelliberty.Application.Connections;

public sealed record ConnectionListState(
    IReadOnlyList<ConnectionInfo> Connections,
    bool IsMonitoringPaused,
    ConnectionFilterLevel FilterLevel,
    string SearchKeyword,
    DateTimeOffset SampledAt = default)
{
    public static ConnectionListState Initial { get; } = new([], false, ConnectionFilterLevel.All, string.Empty);
}
