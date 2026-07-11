namespace Stelliberty.Application.Runtime;

public enum CoreUpdateStatus
{
    Updated,
    UpToDate,
    Failed,
}

public sealed record CoreUpdateResult(CoreUpdateStatus Status, string? Version, string Message);

public interface ICoreUpdater
{
    Task<CoreUpdateResult> UpdateAsync(CancellationToken cancellationToken = default);
}
