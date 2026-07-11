using Stelliberty.Domain.CoreLogs;
namespace Stelliberty.Application.CoreLogs;

public sealed record CoreLogState(
    IReadOnlyList<CoreLogMessage> Logs,
    bool IsMonitoringPaused,
    CoreLogLevel? FilterLevel,
    string SearchKeyword)
{
    public static CoreLogState Initial { get; } = new([], false, null, string.Empty);
}
