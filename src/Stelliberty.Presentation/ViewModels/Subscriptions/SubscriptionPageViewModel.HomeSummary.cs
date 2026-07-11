namespace Stelliberty.Presentation.ViewModels;

// 运行统计仅接受当前订阅，切换期间保持空状态。
public sealed partial class SubscriptionPageViewModel
{
    private int? _homeCardGroupCount;
    private int? _homeCardNodeCount;
    private int? _homeCardAverageDelay;

    public string HomeCardNameText => CurrentSubscription?.Name ?? Localize("Home.Subscription.Empty");

    public string HomeCardTypeText => CurrentSubscription?.TypeText
        ?? Localize("Subscriptions.Type.Local");

    public string HomeCardTypeTag => CurrentSubscription?.TypePillTag ?? "local";

    public bool HomeCardIsLocal => CurrentSubscription?.IsLocalFile ?? true;

    public bool HomeCardIsRemote => !HomeCardIsLocal;

    public string HomeCardSourceFormatText => CurrentSubscription?.SourceFormatText
        ?? Localize("Subscriptions.SourceFormat.Standard");

    public string HomeCardSourceFormatTag => CurrentSubscription?.SourceFormatPillTag ?? "success";

    public string HomeCardUpdatedText => CurrentSubscription?.LastUpdatedText
        ?? Localize("Common.NotUpdated");

    public string HomeCardExpireText => CurrentSubscription is null
        ? Localize("Common.Unknown")
        : CurrentSubscription.ExpireText;

    public string HomeCardTrafficText => CurrentSubscription is { HasTrafficInfo: true }
        ? CurrentSubscription.TrafficText
        : Localize("Subscriptions.Traffic.Unavailable");

    public double HomeCardTrafficRatio => CurrentSubscription?.TrafficUsageRatio ?? 0;

    public bool HomeCardHasTrafficInfo => CurrentSubscription?.HasTrafficInfo == true;

    public bool HomeCardHasContent => CurrentSubscription is not null;

    public string HomeCardGroupCountText => _homeCardGroupCount?.ToString() ?? "-";

    public string HomeCardNodeCountText => _homeCardNodeCount?.ToString() ?? "-";

    public string HomeCardAverageDelayText => _homeCardAverageDelay is { } delay ? $"{delay} ms" : "-";

    public void SetHomeCardRuntimeStats(int? groupCount, int? nodeCount, int? averageDelay)
    {
        _homeCardGroupCount = groupCount;
        _homeCardNodeCount = nodeCount;
        _homeCardAverageDelay = averageDelay;
        OnPropertyChanged(nameof(HomeCardGroupCountText));
        OnPropertyChanged(nameof(HomeCardNodeCountText));
        OnPropertyChanged(nameof(HomeCardAverageDelayText));
    }

    public void ClearHomeCardRuntimeStats() => SetHomeCardRuntimeStats(null, null, null);

    private void NotifyHomeCardPresentationChanged()
    {
        OnPropertyChanged(nameof(HomeCardNameText));
        OnPropertyChanged(nameof(HomeCardTypeText));
        OnPropertyChanged(nameof(HomeCardTypeTag));
        OnPropertyChanged(nameof(HomeCardIsLocal));
        OnPropertyChanged(nameof(HomeCardIsRemote));
        OnPropertyChanged(nameof(HomeCardSourceFormatText));
        OnPropertyChanged(nameof(HomeCardSourceFormatTag));
        OnPropertyChanged(nameof(HomeCardUpdatedText));
        OnPropertyChanged(nameof(HomeCardExpireText));
        OnPropertyChanged(nameof(HomeCardTrafficText));
        OnPropertyChanged(nameof(HomeCardTrafficRatio));
        OnPropertyChanged(nameof(HomeCardHasTrafficInfo));
        OnPropertyChanged(nameof(HomeCardHasContent));
    }
}
