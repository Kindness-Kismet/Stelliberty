namespace Stelliberty.Application.Platform;

public sealed record NetworkConnectionInfo(NetworkConnectionType Type, string Name)
{
    public static NetworkConnectionInfo Disconnected { get; } = new(NetworkConnectionType.Disconnected, string.Empty);
}
