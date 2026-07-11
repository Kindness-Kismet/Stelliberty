namespace Stelliberty.Application.Platform;

public sealed record UwpLoopbackBatchResult(bool IsSuccess, string Message, IReadOnlyList<UwpLoopbackPackage> Packages);
