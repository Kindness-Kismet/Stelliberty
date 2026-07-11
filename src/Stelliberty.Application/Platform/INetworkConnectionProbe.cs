namespace Stelliberty.Application.Platform;

public interface INetworkConnectionProbe
{
    NetworkConnectionInfo Detect();
}
