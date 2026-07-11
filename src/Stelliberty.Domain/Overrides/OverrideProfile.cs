namespace Stelliberty.Domain.Overrides;

public sealed record OverrideProfile(
    string Id,
    string Name,
    OverrideSourceType SourceType,
    OverrideFormat Format,
    string SourceLocation,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUpdatedAt = null,
    OverrideUpdateProxyMode UpdateProxyMode = OverrideUpdateProxyMode.Direct);
