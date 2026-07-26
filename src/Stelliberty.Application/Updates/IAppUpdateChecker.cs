namespace Stelliberty.Application.Updates;

public interface IAppUpdateChecker
{
    Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
