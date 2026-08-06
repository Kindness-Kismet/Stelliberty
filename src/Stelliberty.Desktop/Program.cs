using System.Text;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Desktop.Services;
using Stelliberty.Infrastructure.Diagnostics;

namespace Stelliberty.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var launch = DesktopLaunchArguments.Parse(args);
        if (launch.Mode == DesktopLaunchMode.Invalid)
        {
            AppLogger.Configure(new CapturedAppLogger());
            AppLogger.Error("Desktop UI launch arguments are invalid");
            return 2;
        }

        ConfigureUiProcess();
        DesktopLaunchContext.TraySessionToken = launch.TraySessionToken;
        AppRuntime.RunUi(
            launch.AvaloniaArguments,
            enforceDesktopSingleInstance: launch.Mode == DesktopLaunchMode.DirectUi);
        return 0;
    }

    private static void ConfigureUiProcess()
    {
        AppLogger.Configure(new CapturedAppLogger(DesktopApplicationLayout.RunningLogFilePath));
        DependencyDirectoryService.Configure();
    }
}
