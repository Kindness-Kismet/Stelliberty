namespace Stelliberty.Native.Hub;

public sealed record BootstrapResult(bool Ok, string Message)
{
    public static BootstrapResult Success(string message = "ok") => new(true, message);
    public static BootstrapResult Failure(string message) => new(false, message);
}
