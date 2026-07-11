using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;

namespace Stelliberty.Infrastructure.Platform;

public sealed class UnsupportedAppBehaviorService : IAppBehaviorService
{
    public void Apply(AppBehaviorApplicationRequest request)
    {
        AppLogger.Info($"App behavior request recorded: autoStart={request.IsAutoStartEnabled}");
    }
}
