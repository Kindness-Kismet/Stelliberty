using Stelliberty.Application.Diagnostics;
using Stelliberty.Native.Generated;
using FfiBootstrapResult = Stelliberty.Native.Generated.BootstrapResult;

namespace Stelliberty.Native.Hub;

public static class HubBootstrap
{
    private static bool _started;
    private static bool _isShutdownRequested;
    private static readonly object Gate = new();

    public static BootstrapResult Start(BootstrapOptions options)
    {
        lock (Gate)
        {
            if (_isShutdownRequested)
            {
                return BootstrapResult.Failure("Hub shutdown has started.");
            }

            var isResume = _started;
            try
            {
                using FfiBootstrapResult ffi = Interop.hub_bootstrap(
                    options.PipeName.Utf8(),
                    options.CorePath.Utf8(),
                    options.DataCoreDir.Utf8(),
                    options.UserDataDir.Utf8(),
                    options.CorePipe.Utf8(),
                    options.BootstrapYaml.Utf8());
                var message = ffi.message.String;
                if (!ffi.ok.Is)
                {
                    AppLogger.Error($"Hub startup failed: {message}");
                    return BootstrapResult.Failure(message);
                }
                _started = true;
                AppLogger.Info(isResume
                    ? $"Normal-mode core resumed: {message}"
                    : $"Hub started: {message}");
                return BootstrapResult.Success(message);
            }
            catch (Exception ex)
            {
                AppLogger.Error(ex, "Hub startup exception");
                return BootstrapResult.Failure(ex.Message);
            }
        }
    }

    public static BootstrapResult StartCore()
    {
        lock (Gate)
        {
            if (_isShutdownRequested)
            {
                return BootstrapResult.Failure("Hub shutdown has started.");
            }

            if (!_started)
            {
                return BootstrapResult.Failure("Hub is not initialized.");
            }

            return StartCoreLocked();
        }
    }

    public static BootstrapResult StopCore()
    {
        lock (Gate)
        {
            if (!_started)
            {
                return BootstrapResult.Failure("Hub is not initialized.");
            }

            try
            {
                using FfiBootstrapResult ffi = Interop.hub_bootstrap_stop_core();
                var message = ffi.message.String;
                return ffi.ok.Is
                    ? BootstrapResult.Success(message)
                    : BootstrapResult.Failure(message);
            }
            catch (Exception exception)
            {
                AppLogger.Error(exception, "normal core shutdown exception");
                return BootstrapResult.Failure(exception.Message);
            }
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            if (_isShutdownRequested)
            {
                return;
            }

            _isShutdownRequested = true;
            try
            {
                if (_started)
                {
                    Interop.hub_shutdown();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning($"Hub shutdown exception ignored: {ex.Message}");
            }
            finally
            {
                _started = false;
            }
        }
    }

    private static BootstrapResult StartCoreLocked()
    {
        try
        {
            using FfiBootstrapResult ffi = Interop.hub_bootstrap_start_core();
            var message = ffi.message.String;
            return ffi.ok.Is
                ? BootstrapResult.Success(message)
                : BootstrapResult.Failure(message);
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "normal core startup exception");
            return BootstrapResult.Failure(exception.Message);
        }
    }
}
