using System.Text;
using System.Net.Sockets;
using Avalonia;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Infrastructure.Diagnostics;
using Stelliberty.Infrastructure.Tray;

namespace Stelliberty.Tray;

internal static class Program
{
    private const string SilentStartArgument = "--silent-start";
    private static readonly TimeSpan ExistingInstanceTimeout = TimeSpan.FromSeconds(15);

    internal static bool ActivateUiOnStart { get; private set; }

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        AppLogger.Configure(new CapturedAppLogger(TrayApplicationLayout.RunningLogFilePath));
        ActivateUiOnStart = !args.Contains(SilentStartArgument, StringComparer.Ordinal);
        var avaloniaArguments = args.Where(argument => argument != SilentStartArgument).ToArray();
        using var singleInstance = new TraySingleInstance();
        if (!singleInstance.OwnsInstance)
        {
            return await ActivateExistingInstanceAsync().ConfigureAwait(false);
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(avaloniaArguments);
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TrayApplication>()
            .UsePlatformDetect()
            .LogToTrace();

    private static async Task<int> ActivateExistingInstanceAsync()
    {
        if (!ActivateUiOnStart)
        {
            AppLogger.Info("Tray is already running; silent duplicate process exits");
            return 0;
        }

        using var timeout = new CancellationTokenSource(ExistingInstanceTimeout);
        while (!timeout.IsCancellationRequested)
        {
            await using var client = new TrayIpcClient();
            try
            {
                await client.ConnectAsync(timeout.Token).ConfigureAwait(false);
                await client.HelloAsync(Environment.ProcessId, timeout.Token).ConfigureAwait(false);
                await client.ActivateUiAsync(Environment.ProcessId, timeout.Token).ConfigureAwait(false);
                AppLogger.Info("Existing Tray UI activation requested");
                return 0;
            }
            catch (Exception exception) when (
                !timeout.IsCancellationRequested
                && exception is IOException or SocketException)
            {
                try
                {
                    await Task.Delay(100, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                AppLogger.Error(exception, "Existing Tray UI activation failed");
                return 1;
            }
        }

        AppLogger.Error("Existing Tray UI activation timed out");
        return 1;
    }
}
