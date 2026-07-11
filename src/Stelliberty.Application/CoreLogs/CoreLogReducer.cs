using Stelliberty.Domain.CoreLogs;
namespace Stelliberty.Application.CoreLogs;

public sealed class CoreLogReducer
{
    private const int MaxLogCount = 2000;

    public CoreLogState TogglePause(CoreLogState state)
    {
        return state with { IsMonitoringPaused = !state.IsMonitoringPaused };
    }

    public CoreLogState Append(CoreLogState state, IReadOnlyList<CoreLogMessage> logs)
    {
        if (state.IsMonitoringPaused || logs.Count == 0)
        {
            return state;
        }

        var keep = Math.Min(state.Logs.Count + logs.Count, MaxLogCount);
        var nextLogs = new List<CoreLogMessage>(keep);
        var dropExisting = Math.Max(0, state.Logs.Count + logs.Count - MaxLogCount);
        for (var index = dropExisting; index < state.Logs.Count; index++)
        {
            nextLogs.Add(state.Logs[index]);
        }

        var dropNew = Math.Max(0, logs.Count - MaxLogCount);
        for (var index = dropNew; index < logs.Count; index++)
        {
            nextLogs.Add(logs[index]);
        }

        return state with { Logs = nextLogs };
    }

    public CoreLogState Clear(CoreLogState state)
    {
        return state with { Logs = [] };
    }

    public CoreLogState SetFilterLevel(CoreLogState state, CoreLogLevel? level)
    {
        return state with { FilterLevel = level };
    }

    public CoreLogState SetSearchKeyword(CoreLogState state, string keyword)
    {
        return state with { SearchKeyword = keyword };
    }

    public CoreLogState ResetForStoppedCore(CoreLogState state)
    {
        return CoreLogState.Initial;
    }
}
