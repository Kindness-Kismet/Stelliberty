using Stelliberty.Application.Diagnostics;
using Stelliberty.Native.Generated;
using FfiBootstrapResult = Stelliberty.Native.Generated.BootstrapResult;

namespace Stelliberty.Native.Hub;

public static class HubBootstrap
{
    private static bool _started;
    private static readonly object Gate = new();

    public static BootstrapResult Start(BootstrapOptions options)
    {
        lock (Gate)
        {
            if (_started)
            {
                return BootstrapResult.Success("already initialized");
            }
            try
            {
                using FfiBootstrapResult ffi = Interop.hub_bootstrap(
                    options.PipeName.Utf8(),
                    options.MihomoPath.Utf8(),
                    options.DataCoreDir.Utf8(),
                    options.UserDataDir.Utf8(),
                    options.MihomoPipe.Utf8(),
                    options.BootstrapYaml.Utf8());
                var message = ffi.message.String;
                if (!ffi.ok.Is)
                {
                    AppLogger.Error($"hub startup failed: {message}");
                    return BootstrapResult.Failure(message);
                }
                _started = true;
                AppLogger.Info($"hub started: {message}");
                return BootstrapResult.Success(message);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "hub startup exception");
                return BootstrapResult.Failure(ex.Message);
            }
        }
    }

    public static void Shutdown()
    {
        try
        {
            Interop.hub_shutdown();
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"hub shutdown exception ignored: {ex.Message}");
        }
    }
}
