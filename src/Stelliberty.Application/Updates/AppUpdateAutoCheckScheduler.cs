using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;

namespace Stelliberty.Application.Updates;

public sealed class AppUpdateAutoCheckScheduler(
    IAppUpdateChecker updateChecker,
    Func<AppSettings> loadSettings,
    Action<AppSettings> saveSettings,
    Func<DateTimeOffset> now)
{
    public Task<AppUpdateAutoCheckResult> CheckOnStartupAsync(CancellationToken cancellationToken = default)
    {
        var settings = loadSettings();
        if (!settings.IsAutoCheckUpdateEnabled)
        {
            return Task.FromResult(new AppUpdateAutoCheckResult(false, false, "Automatic update checks are turned off"));
        }

        var currentTime = now();
        if (!ShouldCheck(settings, currentTime))
        {
            return Task.FromResult(new AppUpdateAutoCheckResult(false, false, "The next automatic check is not due yet"));
        }

        return RunCheckAsync(settings, currentTime, cancellationToken);
    }

    public Task<AppUpdateAutoCheckResult> CheckWhenDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = loadSettings();
        if (!settings.IsAutoCheckUpdateEnabled)
        {
            return Task.FromResult(new AppUpdateAutoCheckResult(false, false, "Automatic update checks are turned off"));
        }

        if (IsStartupOnlyInterval(settings.AppUpdateCheckInterval))
        {
            return Task.FromResult(new AppUpdateAutoCheckResult(false, false, "The current setting only checks at startup"));
        }

        var currentTime = now();
        if (!ShouldCheck(settings, currentTime))
        {
            return Task.FromResult(new AppUpdateAutoCheckResult(false, false, "The next automatic check is not due yet"));
        }

        return RunCheckAsync(settings, currentTime, cancellationToken);
    }

    // 手动检查忽略开关和到期时间，但仍刷新上次检查时间。
    public async Task<AppUpdateCheckResult> CheckManuallyAsync(CancellationToken cancellationToken = default)
    {
        var settings = loadSettings();
        var result = await updateChecker.CheckForUpdatesAsync(cancellationToken);
        settings.LastAppUpdateCheckTime = now();
        saveSettings(settings);
        return result;
    }

    private async Task<AppUpdateAutoCheckResult> RunCheckAsync(
        AppSettings settings,
        DateTimeOffset currentTime,
        CancellationToken cancellationToken)
    {
        var result = await updateChecker.CheckForUpdatesAsync(cancellationToken);
        settings.LastAppUpdateCheckTime = currentTime;
        saveSettings(settings);
        AppLogger.Info($"Automatic app update check: {result.Message}");

        if (result.HasUpdate && string.Equals(result.LatestVersion, settings.IgnoredUpdateVersion, StringComparison.Ordinal))
        {
            return new AppUpdateAutoCheckResult(true, false, $"Ignored version: {result.LatestVersion}");
        }

        return new AppUpdateAutoCheckResult(
            true,
            result.HasUpdate,
            result.Message,
            result.LatestVersion,
            result.ReleaseUrl);
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
