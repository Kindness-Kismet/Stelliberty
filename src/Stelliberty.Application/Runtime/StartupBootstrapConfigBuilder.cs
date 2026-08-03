using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Subscriptions;

namespace Stelliberty.Application.Runtime;

// 启动引导在缺少选择或生成失败时回退为空配置。
public sealed class StartupBootstrapConfigBuilder(
    IAppSettingsStore settingsStore,
    ISubscriptionSelectionStore selectionStore,
    SelectedRuntimeFallbackGenerator fallbackGenerator,
    SubscriptionFailureRecorder failureRecorder)
{
    // 引导早于 hub 接管，所以管道端点必须写进 YAML。
    private const string EmptyBootstrapYamlTemplate = """
mixed-port: {MixedPort}
allow-lan: false
mode: rule
log-level: {LogLevel}
{ControllerKey}: {CorePipe}
proxies: []
proxy-groups: []
rules: []
""";

    public string Build(string corePipe, bool isTunRuntimeAvailable = true)
    {
        try
        {
            var settings = settingsStore.Load();
            var runtimeParams = RuntimeConfigParams.FromSettings(settings) with
            {
                IsTunEnabled = settings.IsTunEnabled && isTunRuntimeAvailable
            };
            var selectedSubscriptionId = selectionStore.GetCurrentSubscriptionId();
            if (string.IsNullOrWhiteSpace(selectedSubscriptionId))
            {
                AppLogger.Info("No subscription selected; starting the core with an empty config");
                return BuildEmptyYaml(runtimeParams.MixedPort, runtimeParams.ClashCoreLogLevel, corePipe);
            }

            try
            {
                var request = new SelectedSubscriptionRuntimeRequest([], runtimeParams);
                var result = fallbackGenerator.Generate(selectedSubscriptionId, request);
                AppLogger.Info($"Starting the core with the selected subscription: {result.Runtime.Subscription.Name}");
                return result.Runtime.RuntimeConfigContent;
            }
            catch (Exception exception)
            {
                selectionStore.SetCurrentSubscriptionId(null);
                failureRecorder.MarkFailed(selectedSubscriptionId, exception.Message);
                AppLogger.Warning($"Startup config generation failed; using an empty config: {exception.Message}");
                return BuildEmptyYaml(runtimeParams.MixedPort, runtimeParams.ClashCoreLogLevel, corePipe);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup config generation failed; using an empty config: {exception.Message}");
            return BuildDefaultEmptyYaml(corePipe);
        }
    }

    // 宿主在设置存储或构造不可用时复用这个降级配置。
    public static string BuildDefaultEmptyYaml(string corePipe)
    {
        return BuildEmptyYaml(AppSettings.DefaultMixedPort, "info", corePipe);
    }

    private static string BuildEmptyYaml(int mixedPort, string logLevel, string corePipe)
    {
        return EmptyBootstrapYamlTemplate
            .Replace("{MixedPort}", mixedPort.ToString(), StringComparison.Ordinal)
            .Replace("{LogLevel}", logLevel, StringComparison.Ordinal)
            .Replace("{ControllerKey}", ControllerKey(), StringComparison.Ordinal)
            .Replace("{CorePipe}", corePipe, StringComparison.Ordinal);
    }

    private static string ControllerKey()
    {
        return OperatingSystem.IsWindows() ? "external-controller-pipe" : "external-controller-unix";
    }
}
