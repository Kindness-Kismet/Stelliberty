#if DEBUG
using Stelliberty.Application.Platform;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static async Task<string?> ExecuteServiceCommandAsync(MainWindow window, string command)
    {
        var page = RequireViewModel(window).HomePage;
        var spec = command["service.".Length..].Trim();
        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            var status = await page.RefreshServiceModeAsync();
            return ServiceState(page, status);
        }

        if (string.Equals(spec, "install", StringComparison.OrdinalIgnoreCase))
        {
            var result = await page.InstallOrUpdateServiceModeAsync();
            var status = await page.RefreshServiceModeAsync();
            return ServiceResult(page, result, status);
        }

        if (string.Equals(spec, "update", StringComparison.OrdinalIgnoreCase))
        {
            var result = await page.InstallOrUpdateServiceModeAsync();
            var status = await page.RefreshServiceModeAsync();
            return ServiceResult(page, result, status);
        }

        if (string.Equals(spec, "repair", StringComparison.OrdinalIgnoreCase))
        {
            var currentStatus = await page.RefreshServiceModeAsync();
            if (currentStatus.State != ServiceModeState.NeedsRepair)
            {
                return ServiceResult(
                    page,
                    ServiceModeOperationResult.Failed($"Service mode does not need repair: {currentStatus.State}"),
                    currentStatus);
            }

            var result = await page.UninstallServiceModeAsync();
            var status = await page.RefreshServiceModeAsync();
            return ServiceResult(page, result, status);
        }

        if (string.Equals(spec, "uninstall", StringComparison.OrdinalIgnoreCase))
        {
            var result = await page.UninstallServiceModeAsync();
            var status = await page.RefreshServiceModeAsync();
            return ServiceResult(page, result, status);
        }

        throw new InvalidOperationException($"Unknown service mode command: {command}");
    }

    private static string ServiceState(HomePageViewModel page, ServiceModeStatus status)
    {
        return string.Join(";", [
            $"state={status.State}",
            $"text={ServiceStatusText(status)}",
            $"message={status.Message}",
            $"version={status.InstalledVersion ?? "unknown"}",
            $"available={status.AvailableVersion ?? "unknown"}",
            $"update={page.IsServiceModeUpdateAvailable.ToString().ToLowerInvariant()}",
            $"heartbeat={status.LastHeartbeatAge?.TotalSeconds.ToString("0") ?? "none"}",
            $"core={status.CoreState ?? "unknown"}",
            $"pid={status.CorePid?.ToString() ?? "none"}",
            $"canToggle={page.CanToggleServiceMode.ToString().ToLowerInvariant()}",
            $"busy={page.IsServiceModeBusy.ToString().ToLowerInvariant()}"
        ]);
    }

    private static string ServiceResult(HomePageViewModel page, ServiceModeOperationResult result, ServiceModeStatus status)
    {
        return string.Join(";", [
            $"result={result.Type}",
            $"message={result.Message}",
            $"requiresRestart={result.RequiresRestart.ToString().ToLowerInvariant()}",
            ServiceState(page, status)
        ]);
    }

    private static string ServiceStatusText(ServiceModeStatus status)
    {
        return status.State switch
        {
            ServiceModeState.NeedsRepair => "Service mode needs repair",
            ServiceModeState.Running => "Service mode is running",
            ServiceModeState.Stopped => "Service mode is stopped",
            ServiceModeState.NotInstalled => "Service mode is not installed",
            _ => "Service mode state is unknown",
        };
    }
}
#endif
