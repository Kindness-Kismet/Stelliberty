namespace Stelliberty.Application.Runtime;

public enum CoreApplyMode
{
    Reload,
    Restart,
}

public sealed record CoreApplyConfigResult(CoreApplyMode Mode, int Pid);
