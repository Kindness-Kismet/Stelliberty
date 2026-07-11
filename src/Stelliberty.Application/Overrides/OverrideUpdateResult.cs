using Stelliberty.Domain.Overrides;
namespace Stelliberty.Application.Overrides;

public sealed record OverrideUpdateResult(
    IReadOnlyList<string> UpdatedOverrideIds,
    IReadOnlyList<string> SkippedOverrideIds);
