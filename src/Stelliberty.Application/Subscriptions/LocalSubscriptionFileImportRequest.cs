using Stelliberty.Domain.Subscriptions;
namespace Stelliberty.Application.Subscriptions;

public sealed record LocalSubscriptionFileImportRequest(
    string Name,
    string FilePath,
    int AutoTestDelayIntervalMinutes = 0);
