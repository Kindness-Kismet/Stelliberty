#if DEBUG
using Stelliberty.Domain.CoreLogs;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static Task<string?> ExecuteCoreLogsCommandAsync(MainWindow window, string command)
    {
        var page = RequireViewModel(window).CoreLogPage;
        var spec = command["core-logs.".Length..].Trim();
        if (string.Equals(spec, "toggle pause", StringComparison.OrdinalIgnoreCase))
        {
            page.TogglePause();
            return Task.FromResult<string?>(LogState(page));
        }

        if (string.Equals(spec, "clear", StringComparison.OrdinalIgnoreCase))
        {
            page.ClearLogs();
            return Task.FromResult<string?>(LogState(page));
        }

        if (spec.StartsWith("filter ", StringComparison.OrdinalIgnoreCase))
        {
            page.SetFilterLevel(ParseLogLevel(spec["filter ".Length..].Trim()));
            return Task.FromResult<string?>(LogState(page));
        }

        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(LogState(page));
        }

        throw new InvalidOperationException($"Unknown core log command: {command}");
    }

    private static string LogState(CoreLogPageViewModel page)
    {
        return string.Join(";", [
            $"total={page.TotalLogCount}",
            $"filtered={page.FilteredLogCount}",
            $"paused={page.IsMonitoringPaused.ToString().ToLowerInvariant()}",
            $"filter={page.FilterLevel?.ToString() ?? "All"}",
            $"warnings={page.WarningLogCount}",
            $"errors={page.ErrorLogCount}",
            $"running={page.IsCoreRunning.ToString().ToLowerInvariant()}"
        ]);
    }

    private static CoreLogLevel? ParseLogLevel(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "debug" => CoreLogLevel.Debug,
            "info" => CoreLogLevel.Info,
            "warning" or "warn" => CoreLogLevel.Warning,
            "error" => CoreLogLevel.Error,
            _ => null
        };
    }
}
#endif
