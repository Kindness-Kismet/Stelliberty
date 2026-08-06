using System.Text;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Infrastructure.Diagnostics;

namespace Stelliberty.Tray;

internal static class Program
{
    public static async Task<int> Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        AppLogger.Configure(new CapturedAppLogger(TrayApplicationLayout.RunningLogFilePath));
        return await TrayRuntime.RunAsync().ConfigureAwait(false);
    }
}
