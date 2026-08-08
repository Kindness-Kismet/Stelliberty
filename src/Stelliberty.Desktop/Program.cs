using System.Text;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Desktop.Services;
using Stelliberty.Infrastructure.Diagnostics;

namespace Stelliberty.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        AppLogger.Configure(new CapturedAppLogger(DesktopApplicationLayout.RunningLogFilePath));
        DependencyDirectoryService.Configure();
        AppRuntime.Run(args);
    }
}
