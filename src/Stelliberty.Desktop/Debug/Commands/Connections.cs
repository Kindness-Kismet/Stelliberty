#if DEBUG
using Stelliberty.Domain.Connections;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteConnectionsCommandAsync(MainWindow window, string command)
    {
        var page = RequireViewModel(window).ConnectionPage;
        var spec = command["connections.".Length..].Trim();
        if (string.Equals(spec, "refresh", StringComparison.OrdinalIgnoreCase))
        {
            await page.RefreshConnectionsAsync();
            return ConnectionState(page);
        }

        if (string.Equals(spec, "toggle pause", StringComparison.OrdinalIgnoreCase))
        {
            page.TogglePause();
            return ConnectionState(page);
        }

        if (string.Equals(spec, "close all", StringComparison.OrdinalIgnoreCase))
        {
            await page.CloseAllConnectionsAsync();
            return ConnectionState(page);
        }

        if (spec.StartsWith("close ", StringComparison.OrdinalIgnoreCase))
        {
            await page.CloseConnectionAsync(spec["close ".Length..].Trim());
            return ConnectionState(page);
        }

        if (spec.StartsWith("filter ", StringComparison.OrdinalIgnoreCase))
        {
            page.SetFilterLevel(ParseConnectionFilter(spec["filter ".Length..].Trim()));
            return ConnectionState(page);
        }

        if (spec.StartsWith("get detail ", StringComparison.OrdinalIgnoreCase))
        {
            page.ShowDetail(spec["get detail ".Length..].Trim());
            return page.SelectedConnectionDetailText;
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return ConnectionState(page);
        }

        throw new InvalidOperationException($"Unknown connections command: {command}");
    }

    private static string ConnectionState(ConnectionPageViewModel page)
    {
        return string.Join(";", [
            $"total={page.TotalConnectionCount}",
            $"filtered={page.FilteredConnectionCount}",
            $"paused={page.IsMonitoringPaused.ToString().ToLowerInvariant()}",
            $"filter={page.FilterLevel}",
            $"closedAll={page.HasClosedAllConnections.ToString().ToLowerInvariant()}",
            $"closed={string.Join(',', page.ClosedConnectionIds)}",
            $"ids={string.Join(',', page.Connections.Select(connection => connection.Id))}"
        ]);
    }

    private static ConnectionFilterLevel ParseConnectionFilter(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "direct" => ConnectionFilterLevel.Direct,
            "proxy" => ConnectionFilterLevel.Proxy,
            _ => ConnectionFilterLevel.All
        };
    }
}
#endif
