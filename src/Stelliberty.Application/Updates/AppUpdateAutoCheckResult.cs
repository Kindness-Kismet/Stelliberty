namespace Stelliberty.Application.Updates;

public sealed record AppUpdateAutoCheckResult(bool WasChecked, bool HasUpdate, string Message);
