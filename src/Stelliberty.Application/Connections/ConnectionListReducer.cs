using Stelliberty.Domain.Connections;
namespace Stelliberty.Application.Connections;

public sealed class ConnectionListReducer
{
    // 最小采样窗口用于压住连续轮询造成的突刺。
    private const double MinSampleSeconds = 0.25;

    public ConnectionListState TogglePause(ConnectionListState state)
    {
        return state with { IsMonitoringPaused = !state.IsMonitoringPaused };
    }

    // mihomo 只暴露累计字节，速率按每个 Id 的差值计算。
    public ConnectionListState ApplyIncoming(ConnectionListState state, IReadOnlyList<ConnectionInfo> connections, DateTimeOffset sampledAt)
    {
        if (state.IsMonitoringPaused)
        {
            return state;
        }

        var connectionsWithSpeed = ComputeSpeeds(state, connections, sampledAt);
        return state with { Connections = connectionsWithSpeed, SampledAt = sampledAt };
    }

    private static IReadOnlyList<ConnectionInfo> ComputeSpeeds(ConnectionListState state, IReadOnlyList<ConnectionInfo> connections, DateTimeOffset sampledAt)
    {
        var elapsed = (sampledAt - state.SampledAt).TotalSeconds;
        if (state.SampledAt == default || elapsed <= 0 || state.Connections.Count == 0)
        {
            return connections;
        }

        var window = Math.Max(elapsed, MinSampleSeconds);
        var previous = new Dictionary<string, ConnectionInfo>(state.Connections.Count, StringComparer.Ordinal);
        foreach (var connection in state.Connections)
        {
            previous[connection.Id] = connection;
        }

        var result = new List<ConnectionInfo>(connections.Count);
        foreach (var connection in connections)
        {
            if (!previous.TryGetValue(connection.Id, out var last))
            {
                result.Add(connection);
                continue;
            }

            var uploadSpeed = (long)(Math.Max(0L, connection.Upload - last.Upload) / window);
            var downloadSpeed = (long)(Math.Max(0L, connection.Download - last.Download) / window);
            result.Add(connection with { UploadSpeed = uploadSpeed, DownloadSpeed = downloadSpeed });
        }

        return result;
    }
}
