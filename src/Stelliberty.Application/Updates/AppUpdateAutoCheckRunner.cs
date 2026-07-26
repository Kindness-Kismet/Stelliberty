namespace Stelliberty.Application.Updates;

public sealed class AppUpdateAutoCheckRunner(
    AppUpdateAutoCheckScheduler scheduler,
    Action<AppUpdateAutoCheckResult> applyResult)
{
    public async Task<AppUpdateAutoCheckResult> RunStartupCheckAsync()
    {
        var result = await scheduler.CheckOnStartupAsync();
        applyResult(result);
        return result;
    }

    public async Task<AppUpdateAutoCheckResult> RunDueCheckAsync()
    {
        var result = await scheduler.CheckWhenDueAsync();
        if (result.WasChecked)
        {
            applyResult(result);
        }

        return result;
    }
}
