using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Runtime;
using Stelliberty.Native.Generated;

namespace Stelliberty.Native.Hub;

public sealed class HubOverrideEngine : IConfigOverrideEngine
{
    private const string ErrorPrefix = "ERR:";

    public string Apply(string baseConfigContent, RuntimeOverride runtimeOverride)
    {
        return runtimeOverride.Format == OverrideFormat.Yaml
            ? ApplyYaml(baseConfigContent, runtimeOverride.Content, runtimeOverride.Name)
            : ApplyJs(baseConfigContent, runtimeOverride.Content, runtimeOverride.Name);
    }

    private static string ApplyYaml(string baseConfigContent, string overrideContent, string overrideName)
    {
        AppLogger.Info($"Applying Rust YAML override: {overrideName}");
        return Apply(
            Interop.hub_overrides_apply_yaml,
            baseConfigContent,
            overrideContent,
            $"YAML override execution failed: {overrideName}");
    }

    private static string ApplyJs(string baseConfigContent, string scriptContent, string overrideName)
    {
        AppLogger.Info($"Executing Rust JS override: {overrideName}");
        return Apply(
            Interop.hub_overrides_apply_js,
            baseConfigContent,
            scriptContent,
            $"JS override execution failed: {overrideName}");
    }

    private static string Apply(
        Func<Utf8String, Utf8String, Utf8String> ffiApply,
        string baseConfigContent,
        string overrideContent,
        string failureMessage)
    {
        try
        {
            using var output = ffiApply(baseConfigContent.Utf8(), overrideContent.Utf8());
            var result = output.String;
            if (result.StartsWith(ErrorPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{failureMessage}：{result[ErrorPrefix.Length..]}");
            }

            return result;
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException(failureMessage, exception);
        }
    }
}
