using System.Text;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Desktop.Services;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Infrastructure.Diagnostics;

namespace Stelliberty.Desktop;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var launch = DesktopLaunchArguments.Parse(args);
        if (launch.Mode == DesktopLaunchMode.Invalid)
        {
            AppLogger.Configure(new CapturedAppLogger());
            AppLogger.Error("Desktop UI launch arguments are invalid");
            return 2;
        }

        if (launch.Mode is DesktopLaunchMode.TrayUi or DesktopLaunchMode.DirectUi)
        {
            ConfigureUiProcess();
            DesktopLaunchContext.TraySessionToken = launch.TraySessionToken;
            AppRuntime.RunUi(
                launch.AvaloniaArguments,
                enforceDesktopSingleInstance: launch.Mode == DesktopLaunchMode.DirectUi);
            return 0;
        }

        AppLogger.Configure(new CapturedAppLogger());
        try
        {
            await new TrayProcessLauncher().ActivateUiAsync(CancellationToken.None).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Tray activation failed");
            return 1;
        }
    }

    private static void ConfigureUiProcess()
    {
        AppLogger.Configure(new CapturedAppLogger(DesktopApplicationLayout.RunningLogFilePath));
        DependencyDirectoryService.Configure();
    }
}
