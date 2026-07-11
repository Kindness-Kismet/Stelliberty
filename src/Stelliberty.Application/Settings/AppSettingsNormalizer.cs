using Stelliberty.Application.Platform;

namespace Stelliberty.Application.Settings;

// TUN 权限消失即撤销持久偏好，避免下次误带启动配置。
public static class AppSettingsNormalizer
{
    public static bool RevokeTunIfUnavailable(AppSettings settings, ProcessRunMode runMode, bool hasServiceTunHost)
    {
        if (!settings.IsTunEnabled || CanUseTun(runMode, hasServiceTunHost))
        {
            return false;
        }

        settings.IsTunEnabled = false;
        return true;
    }

    public static bool EffectiveTunEnabled(AppSettings settings, ProcessRunMode runMode, bool hasServiceTunHost)
    {
        return settings.IsTunEnabled && CanUseTun(runMode, hasServiceTunHost);
    }

    public static bool CanUseTun(ProcessRunMode runMode, bool hasServiceTunHost)
    {
        return runMode is ProcessRunMode.Administrator or ProcessRunMode.Service || hasServiceTunHost;
    }
}
