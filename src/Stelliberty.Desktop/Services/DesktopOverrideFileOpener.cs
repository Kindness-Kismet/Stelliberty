using System.Diagnostics;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Overrides;

namespace Stelliberty.Desktop.Services;

public sealed class DesktopOverrideFileOpener(Func<string, string> resolveContentPath) : IOverrideFileOpener
{
    public void OpenOverrideFile(string overrideId)
    {
        var path = resolveContentPath(overrideId);
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
            AppLogger.Info($"Override file was handed off to the system shell: {overrideId}");
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, $"Override file open failed: {overrideId}");
        }
    }
}
