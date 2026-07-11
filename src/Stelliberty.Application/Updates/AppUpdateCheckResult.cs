namespace Stelliberty.Application.Updates;

public sealed record AppUpdateCheckResult(
    bool HasUpdate,
    string? LatestVersion,
    string Message,
    string? ReleaseUrl = null,
    bool IsFailure = false);
