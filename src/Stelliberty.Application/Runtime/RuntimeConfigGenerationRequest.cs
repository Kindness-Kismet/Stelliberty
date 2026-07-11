namespace Stelliberty.Application.Runtime;

// PostOverrideTransform 在覆写之后、运行时注入之前执行，链式代理可见最终覆写结果。
public sealed record RuntimeConfigGenerationRequest(
    string BaseConfigContent,
    IReadOnlyList<RuntimeOverride> Overrides,
    RuntimeConfigParams RuntimeParams,
    Func<string, string>? PostOverrideTransform = null);
