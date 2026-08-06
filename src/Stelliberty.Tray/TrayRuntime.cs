using Stelliberty.Application.Diagnostics;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Infrastructure.Proxies;

namespace Stelliberty.Tray;

internal static class TrayRuntime
{
    public static async Task<int> RunAsync()
    {
        using var singleInstance = new TraySingleInstance();
        if (!singleInstance.OwnsInstance)
        {
            AppLogger.Info("Tray is already running; duplicate process exits");
            return 0;
        }

        using var lifetime = new TrayLifetime();
        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            lifetime.RequestStop();
        }

        void OnProcessExit(object? sender, EventArgs args) => lifetime.RequestStop();

        try
        {
            var uiSessions = new UiSessionManager(new DesktopUiLauncher());
            var coreLogs = new CoreLogJournal();
            await using var coreRuntime = new TrayCoreRuntimeHost(coreLogs);
            await using var runtimeMonitor = new RuntimeTrafficMonitor(
                coreRuntime,
                new PipeCoreProxyClient(TrayCoreEndpoints.Core));
            using var router = new TrayRequestRouter(
                lifetime,
                uiSessions,
                coreRuntime,
                coreLogs,
                runtimeMonitor);
            await using var server = new TrayIpcServer(
                TrayEndpoint.Current,
                router.HandleAsync,
                router.OnConnectionClosedAsync);
            runtimeMonitor.Start(lifetime.StoppingToken);
            server.Start(lifetime.StoppingToken);
            AppLogger.Info($"Tray startup: pid={Environment.ProcessId} endpoint={TrayEndpoint.Current}");
            await server.Completion.ConfigureAwait(false);
            AppLogger.Info("Tray shutdown");
            return 0;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Tray startup failed");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        }
    }
}
