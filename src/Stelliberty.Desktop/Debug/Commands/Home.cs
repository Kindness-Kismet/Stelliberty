#if DEBUG
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Debug;

internal static partial class DebugCommands
{
    private static Task<string?> ExecuteHomeCommandAsync(MainWindow window, string command)
    {
        var page = RequireViewModel(window).HomePage;
        var spec = command["home.".Length..].Trim();
        if (string.Equals(spec, "state", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(HomeState(page));
        }

        if (string.Equals(spec, "refresh runtime", StringComparison.OrdinalIgnoreCase))
        {
            page.RefreshRuntime();
            return Task.FromResult<string?>(HomeState(page));
        }

        if (string.Equals(spec, "refresh network", StringComparison.OrdinalIgnoreCase))
        {
            page.RefreshNetworkConnection();
            return Task.FromResult<string?>(HomeState(page));
        }

        if (spec.StartsWith("set outbound ", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteOutboundCommand(page, spec["set outbound ".Length..].Trim());
            return Task.FromResult<string?>(HomeState(page));
        }

        if (spec.StartsWith("select takeover ", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteTakeoverCommand(page, spec["select takeover ".Length..].Trim());
            return Task.FromResult<string?>(HomeState(page));
        }

        if (spec.StartsWith("copy terminal-proxy ", StringComparison.OrdinalIgnoreCase))
        {
            page.CopyTerminalProxyCommand(ParseTerminalShell(spec["copy terminal-proxy ".Length..].Trim()));
            return Task.FromResult<string?>(HomeState(page));
        }

        if (string.Equals(spec, "toggle system-proxy", StringComparison.OrdinalIgnoreCase))
        {
            page.ToggleSystemProxyCommand.Execute(null);
            return Task.FromResult<string?>(HomeState(page));
        }

        if (spec.StartsWith("set tun ", StringComparison.OrdinalIgnoreCase))
        {
            page.IsTunEnabled = ParseBool(spec["set tun ".Length..].Trim());
            return Task.FromResult<string?>(HomeState(page));
        }

        if (string.Equals(spec, "reset traffic", StringComparison.OrdinalIgnoreCase))
        {
            page.ResetTrafficCommand.Execute(null);
            return Task.FromResult<string?>(HomeState(page));
        }

        if (string.Equals(spec, "restart core", StringComparison.OrdinalIgnoreCase))
        {
            page.RestartCoreCommand.Execute(null);
            return Task.FromResult<string?>(HomeState(page));
        }

        if (string.Equals(spec, "update core", StringComparison.OrdinalIgnoreCase))
        {
            page.RefreshCoreCommand.Execute(null);
            return Task.FromResult<string?>(HomeState(page));
        }

        throw new InvalidOperationException($"Unknown home command: {command}");
    }

    private static string HomeState(HomePageViewModel page)
    {
        return string.Join(";", [
            $"core={page.IsCoreRunning.ToString().ToLowerInvariant()}",
            $"systemProxy={page.IsSystemProxyEnabled.ToString().ToLowerInvariant()}",
            $"tun={page.IsTunEnabled.ToString().ToLowerInvariant()}",
            $"canTun={page.CanToggleTun.ToString().ToLowerInvariant()}",
            $"serviceMode={page.ServiceModeState}",
            $"coreHost={page.CoreHostMode}",
            $"privilege={page.PrivilegeModeText}",
            $"takeover={(page.IsTakeoverTunTabSelected ? "tun" : "proxy")}",
            $"outbound={page.OutboundMode}",
            $"connections={page.ActiveConnectionsValueText}",
            $"uptime={page.UptimeValueText}",
            $"upload={page.UploadTotalValueText}",
            $"download={page.DownloadTotalValueText}",
            $"network={page.NetworkTypeText}",
            $"updating={page.IsCoreUpdating.ToString().ToLowerInvariant()}",
            $"restarting={page.IsCoreRestarting.ToString().ToLowerInvariant()}"
        ]);
    }

    private static void ExecuteOutboundCommand(HomePageViewModel page, string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "rule":
                page.SetRuleOutboundCommand.Execute(null);
                break;
            case "global":
                page.SetGlobalOutboundCommand.Execute(null);
                break;
            case "direct":
                page.SetDirectOutboundCommand.Execute(null);
                break;
            default:
                throw new InvalidOperationException($"Unknown outbound mode: {value}");
        }
    }

    private static void ExecuteTakeoverCommand(HomePageViewModel page, string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "proxy":
                page.SelectTakeoverProxyTabCommand.Execute(null);
                break;
            case "tun":
                page.SelectTakeoverTunTabCommand.Execute(null);
                break;
            default:
                throw new InvalidOperationException($"Unknown takeover tab: {value}");
        }
    }

    private static TerminalShell ParseTerminalShell(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "powershell" => TerminalShell.PowerShell,
            "cmd" => TerminalShell.Cmd,
            "bash" => TerminalShell.Bash,
            _ => throw new InvalidOperationException($"Unknown terminal shell: {value}"),
        };
    }
}
#endif
