using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class SystemProcessPrivilegeProbe : IProcessPrivilegeProbe
{
    public ProcessRunMode Detect()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return DetectWindows();
            }

            return IsUnixRoot() ? ProcessRunMode.Administrator : ProcessRunMode.Normal;
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Process privilege probe failed: {exception.Message}");
            return ProcessRunMode.Normal;
        }
    }

    [SupportedOSPlatform("windows")]
    private static ProcessRunMode DetectWindows()
    {
        using var identity = WindowsIdentity.GetCurrent();

        if (identity.IsSystem || IsServiceAccount(identity))
        {
            return ProcessRunMode.Service;
        }

        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator)
            ? ProcessRunMode.Administrator
            : ProcessRunMode.Normal;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsServiceAccount(WindowsIdentity identity)
    {
        var user = identity.User;
        return user is not null
            && (user.IsWellKnown(WellKnownSidType.LocalServiceSid)
                || user.IsWellKnown(WellKnownSidType.NetworkServiceSid));
    }

    private static bool IsUnixRoot()
    {

        return GetEuid() == 0;
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEuid();
}
