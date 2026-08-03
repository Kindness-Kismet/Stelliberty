using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Desktop.Services;
using Stelliberty.Infrastructure.Overrides;
using Stelliberty.Infrastructure.Platform;
using Stelliberty.Infrastructure.Runtime;
using Stelliberty.Infrastructure.Settings;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Native.Hub;

namespace Stelliberty.Desktop;

// 只编排普通模式启动；启动配置策略归 Application。
internal static class HubStartupCoordinator
{
    // 端点标识沿用 mihomo，确保升级后仍能连接旧服务托管的核心。
#if DEBUG

    public static readonly string PipeName = BuildHubEndpoint(AppMetadata.PipePrefix + "_core_dev");
    public static readonly string CorePipe = BuildCoreEndpoint(AppMetadata.PipePrefix + "_mihomo_dev");
#else
    public static readonly string PipeName = BuildHubEndpoint(AppMetadata.PipePrefix + "_core_prod");
    public static readonly string CorePipe = BuildCoreEndpoint(AppMetadata.PipePrefix + "_mihomo_prod");
#endif

    private static readonly object StartGate = new();
    private static Task<BootstrapResult>? _startTask;

    public static Task<BootstrapResult> EnsureStartedAsync()
    {
        lock (StartGate)
        {
            _startTask ??= Task.Run(Start);
            return _startTask;
        }
    }

    public static Task<BootstrapResult> StopCoreAsync(CancellationToken cancellationToken)
    {
        return Task.Run(HubBootstrap.StopCore, cancellationToken);
    }

    public static Task<BootstrapResult> ResumeCoreAsync(CancellationToken cancellationToken)
    {
        return Task.Run(Start, cancellationToken);
    }

    public static BootstrapResult Start()
    {
        var result = HubBootstrap.Start(CreateBootstrapOptions());
        if (!result.Ok)
        {
            AppLogger.Warning("Normal-mode core startup failed; app is running without core support");
        }
        return result;
    }

    public static BootstrapOptions CreateBootstrapOptions()
    {
        return new BootstrapOptions(
            PipeName: PipeName,
            CorePath: DesktopApplicationLayout.CoreBinaryPath,
            DataCoreDir: DesktopApplicationLayout.CoreDirectory,
            UserDataDir: DesktopApplicationLayout.AppDataDirectory,
            CorePipe: CorePipe,
            BootstrapYaml: BuildInitialBootstrapYaml(CanNormalModeUseTun()));
    }

    public static ServiceModeCoreHostRequest CreateServiceModeCoreHostRequest()
    {
        return new ServiceModeCoreHostRequest(
            DesktopApplicationLayout.CoreBinaryPath,
            DesktopApplicationLayout.CoreDirectory,
            BuildInitialServiceModeConfigPath());
    }

    public static string BuildInitialServiceModeConfigPath()
    {
        var content = BuildInitialBootstrapYaml(canUseTun: true);
        return WriteServiceModeActiveConfig(content);
    }

    public static string WriteServiceModeActiveConfig(string content)
    {
        Directory.CreateDirectory(DesktopApplicationLayout.RuntimeDirectory);
        var path = Path.Combine(DesktopApplicationLayout.RuntimeDirectory, "_service_active.yaml");
        File.WriteAllText(path, InjectCorePipe(content));
        return path;
    }

    private static string BuildInitialBootstrapYaml(bool canUseTun)
    {
        try
        {
            var platformDirectories = new DesktopPlatformDirectories();
            var settingsStore = new JsonAppSettingsStore(platformDirectories);
            var selectionStore = new FileSubscriptionSelectionStore(platformDirectories.AppDataDirectory);
            var subscriptionStore = new FileSubscriptionStore(platformDirectories.AppDataDirectory);
            var overrideStore = new FileOverrideStore(platformDirectories.AppDataDirectory);
            var runtimeStore = new FileRuntimeConfigStore(platformDirectories.RuntimeDirectory);
            var builder = new StartupBootstrapConfigBuilder(
                settingsStore,
                selectionStore,
                new SelectedRuntimeFallbackGenerator(
                    subscriptionStore,
                    new SubscriptionOverrideSelectionUpdater(subscriptionStore),
                    new SelectedSubscriptionRuntimeGenerator(
                        subscriptionStore,
                        selectionStore,
                        new RuntimeConfigGenerator(new HubOverrideEngine()),
                        overrideStore,
                        runtimeStore)),
                new SubscriptionFailureRecorder(subscriptionStore));
            return builder.Build(CorePipe, canUseTun);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup config generation failed; starting core with an empty config: {exception.Message}");
            return StartupBootstrapConfigBuilder.BuildDefaultEmptyYaml(CorePipe);
        }
    }

    private static string InjectCorePipe(string content)
    {
        return ServiceModeRuntimeConfigWriter.Write(content, CorePipe);
    }

    private static bool CanNormalModeUseTun()
    {
        return AppSettingsNormalizer.CanUseTun(new SystemProcessPrivilegeProbe().Detect(), hasServiceTunHost: false);
    }

    private static string BuildCoreEndpoint(string name)
    {
        return OperatingSystem.IsWindows()
            ? $@"\\.\pipe\{name}"
            : Path.Combine(Path.GetTempPath(), $"{name}.sock");
    }

    private static string BuildHubEndpoint(string name)
    {
        return OperatingSystem.IsWindows()
            ? name
            : Path.Combine(Path.GetTempPath(), $"{name}.sock");
    }
}
