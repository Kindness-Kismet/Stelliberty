using Stelliberty.Domain.Overrides;
namespace Stelliberty.Application.Overrides;

public sealed record OverrideDeleteResult(string DeletedOverrideId, IReadOnlyList<string> AffectedSubscriptionIds);
