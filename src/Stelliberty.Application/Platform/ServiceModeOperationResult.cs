namespace Stelliberty.Application.Platform;

public sealed record ServiceModeOperationResult(
    ServiceModeOperationType Type,
    string Message,
    bool RequiresRestart = false)
{
    public bool IsSuccess => Type == ServiceModeOperationType.Succeeded;

    public bool IsCanceled => Type == ServiceModeOperationType.Cancelled;

    public static ServiceModeOperationResult Success(string message, bool requiresRestart = false)
    {
        return new ServiceModeOperationResult(ServiceModeOperationType.Succeeded, message, requiresRestart);
    }

    public static ServiceModeOperationResult Canceled(string message)
    {
        return new ServiceModeOperationResult(ServiceModeOperationType.Cancelled, message);
    }

    public static ServiceModeOperationResult Failed(string message)
    {
        return new ServiceModeOperationResult(ServiceModeOperationType.Failed, message);
    }
}
