using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;

namespace Stelliberty.Application.Updates;

public sealed class AppUpdateAutoCheckScheduler(
    IAppUpdateChecker updateChecker,
    Func<AppSettings> loadSettings,
    Action<AppSettings> saveSettings,
    Func<DateTimeOffset> now)
{
    public AppUpdateAutoCheckResult CheckOnStartup()
    {
        var settings = loadSettings();
        if (!settings.IsAutoCheckUpdateEnabled)
        {
            return new AppUpdateAutoCheckResult(false, false, "Automatic update checks are turned off");
        }

        var currentTime = now();
        if (!ShouldCheck(settings, currentTime))
        {
            return new AppUpdateAutoCheckResult(false, false, "The next automatic check is not due yet");
        }

        return RunCheck(settings, currentTime);
    }

    public AppUpdateAutoCheckResult CheckWhenDue()
    {
        var settings = loadSettings();
        if (!settings.IsAutoCheckUpdateEnabled)
        {
            return new AppUpdateAutoCheckResult(false, false, "Automatic update checks are turned off");
        }

        if (IsStartupOnlyInterval(settings.AppUpdateCheckInterval))
        {
            return new AppUpdateAutoCheckResult(false, false, "The current setting only checks at startup");
        }

        var currentTime = now();
        if (!ShouldCheck(settings, currentTime))
        {
            return new AppUpdateAutoCheckResult(false, false, "The next automatic check is not due yet");
        }

        return RunCheck(settings, currentTime);
    }

    // 手动检查忽略开关和到期时间，但仍刷新上次检查时间。
    public AppUpdateCheckResult CheckManually()
    {
        var settings = loadSettings();
        var result = updateChecker.CheckForUpdates();
        settings.LastAppUpdateCheckTime = now();
        saveSettings(settings);
        return result;
    }

    private AppUpdateAutoCheckResult RunCheck(AppSettings settings, DateTimeOffset currentTime)
    {
        var result = updateChecker.CheckForUpdates();
        settings.LastAppUpdateCheckTime = currentTime;
        saveSettings(settings);
        AppLogger.Info($"Automatic app update check: {result.Message}");

        if (result.HasUpdate && string.Equals(result.LatestVersion, settings.IgnoredUpdateVersion, StringComparison.Ordinal))
        {
            return new AppUpdateAutoCheckResult(true, false, $"Ignored version: {result.LatestVersion}");
        }

        return new AppUpdateAutoCheckResult(true, result.HasUpdate, result.Message);
    }

    private static bool ShouldCheck(AppSettings settings, DateTimeOffset currentTime)
    {
        if (IsStartupOnlyInterval(settings.AppUpdateCheckInterval) || settings.LastAppUpdateCheckTime is null)
        {
            return true;
        }

        return TryGetInterval(settings.AppUpdateCheckInterval, out var interval)
            && currentTime - settings.LastAppUpdateCheckTime.Value >= interval;
    }

    private static bool TryGetInterval(string value, out TimeSpan interval)
    {
        interval = value switch
        {
            "1day" => TimeSpan.FromDays(1),
            "7days" => TimeSpan.FromDays(7),
            "14days" => TimeSpan.FromDays(14),
            _ => TimeSpan.Zero
        };
        return interval > TimeSpan.Zero;
    }

    private static bool IsStartupOnlyInterval(string value)
    {
        return value == "startup" || !TryGetInterval(value, out _);
    }
}
