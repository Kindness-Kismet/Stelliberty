namespace Stelliberty.Application.Runtime;

// 区分"核心确实处于某状态"和"这次没看到核心"：后者不得当作状态变化，否则会被误判成重启。
public sealed record CoreObservation
{
    private CoreObservation(CoreState? state, int? pid, string? lastError, string? unobservedReason)
    {
        State = state;
        Pid = pid;
        LastError = lastError;
        UnobservedReason = unobservedReason;
    }

    public CoreState? State { get; }

    public int? Pid { get; }

    public string? LastError { get; }

    public string? UnobservedReason { get; }

    public bool IsObserved => State is not null;

    public static CoreObservation Observed(CoreState state, int? pid, string? lastError)
    {
        return new CoreObservation(state, pid, lastError, null);
    }

    public static CoreObservation Unobserved(string reason)
    {
        return new CoreObservation(null, null, null, reason);
    }
}
