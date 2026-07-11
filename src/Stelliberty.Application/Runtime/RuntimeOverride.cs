using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;

namespace Stelliberty.Application.Runtime;

public sealed record RuntimeOverride(
    string Id,
    string Name,
    OverrideFormat Format,
    string Content);
