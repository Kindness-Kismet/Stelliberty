namespace Stelliberty.Application.Updates;

public sealed class AppUpdateAutoCheckRunner(
    AppUpdateAutoCheckScheduler scheduler,
    Action<AppUpdateAutoCheckResult> applyResult)
{
    public async Task<AppUpdateAutoCheckResult> RunStartupCheckAsync()
    {
        var result = await Task.Run(scheduler.CheckOnStartup);
        applyResult(result);
        return result;
    }

    public async Task<AppUpdateAutoCheckResult> RunDueCheckAsync()
    {
        var result = await Task.Run(scheduler.CheckWhenDue);
        if (result.WasChecked)
        {
            applyResult(result);
        }

        return result;
    }
}
