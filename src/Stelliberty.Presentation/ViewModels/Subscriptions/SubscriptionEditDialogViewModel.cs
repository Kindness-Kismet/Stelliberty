using Stelliberty.Application.Localization;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionEditCompletedEventArgs(
    string SubscriptionId,
    string Name,
    string Url,
    string UserAgent,
    int AutoTestDelayIntervalMinutes,
    SubscriptionAutoUpdateMode AutoUpdateMode,
    int AutoUpdateIntervalMinutes,
    SubscriptionUpdateProxyMode UpdateProxyMode,
    string AgeSecretKey = "");

public sealed class SubscriptionEditDialogViewModel : SubscriptionDialogBase
{
    private readonly DialogCloseResetScheduler _closeReset = new();

    private string? _subscriptionId;
    private bool _isDialogVisible;
    private bool _isLocalFile;

    public SubscriptionEditDialogViewModel(ILocalizationService? localization = null)
        : base(localization)
    {
    }

    public event EventHandler<SubscriptionEditCompletedEventArgs>? Confirmed;

    public string? DialogSubscriptionId => _subscriptionId;

    public bool IsDialogVisible => _isDialogVisible;

    public bool IsForRemoteSubscription => _subscriptionId is not null && !_isLocalFile;

    // 仅远程可见，本地分支不可达
    public string SourceLabel => Localize("Subscriptions.Field.Url");

    public string SourcePlaceholder => Localize("Subscriptions.Placeholder.Url");

    public override bool CanSubmit => _subscriptionId is not null;

    // 远程订阅编辑时才是远程语境；本地订阅无 URL 粘贴。
    protected override bool IsRemoteContext => IsForRemoteSubscription;

    public void Open(SubscriptionItemViewModel subscription)
    {
        _closeReset.Cancel();
        _subscriptionId = subscription.Id;
        _isDialogVisible = true;
        _isLocalFile = subscription.IsLocalFile;
        ResetSharedState(
            name: subscription.Name,
            url: subscription.SourceLocation,
            userAgent: subscription.UserAgent,
            ageSecretKey: subscription.IsLocalFile ? string.Empty : subscription.AgeSecretKey,
            autoTestDelayIntervalMinutes: subscription.AutoTestDelayIntervalMinutes,
            autoUpdateMode: subscription.AutoUpdateMode,
            autoUpdateIntervalMinutes: subscription.AutoUpdateIntervalMinutes,
            updateProxyMode: subscription.UpdateProxyMode);
        RaiseStateChanged();
    }

    public void Close()
    {
        BeginClose();
    }

    public void ClearForSubscription(string subscriptionId)
    {
        if (_subscriptionId == subscriptionId)
        {
            BeginClose();
        }
    }

    protected override void Confirm()
    {
        if (!CanSubmit)
        {
            return;
        }

        if (!ValidateSharedInputs(_isLocalFile))
        {
            FocusFirstInvalidInput();
            return;
        }

        var args = new SubscriptionEditCompletedEventArgs(
            _subscriptionId!,
            _name.Trim(),
            _url.Trim(),
            _isLocalFile ? string.Empty : NormalizeUserAgent(),
            _autoTestDelayIntervalMinutes,
            _isLocalFile ? SubscriptionAutoUpdateMode.Disabled : _selectedAutoUpdateMode,
            _isLocalFile ? 0 : _autoUpdateIntervalMinutes,
            _isLocalFile ? SubscriptionUpdateProxyMode.Direct : _selectedUpdateProxyMode,
            _isLocalFile ? string.Empty : _ageSecretKey.Trim());
        BeginClose();
        Confirmed?.Invoke(this, args);
    }

    protected override void Cancel()
    {
        BeginClose();
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SourcePlaceholder));
        RaiseStateChanged();
    }

    private void Reset()
    {
        _isDialogVisible = false;
        _subscriptionId = null;
        _isLocalFile = false;
        ResetSharedState(
            name: string.Empty,
            url: string.Empty,
            userAgent: string.Empty,
            ageSecretKey: string.Empty,
            autoTestDelayIntervalMinutes: 0,
            autoUpdateMode: SubscriptionAutoUpdateMode.Disabled,
            autoUpdateIntervalMinutes: 0,
            updateProxyMode: SubscriptionUpdateProxyMode.Direct);
        RaiseStateChanged();
    }

    private void BeginClose()
    {
        if (!_isDialogVisible)
        {
            return;
        }

        _isDialogVisible = false;
        RaiseStateChanged();
        _closeReset.Run(() => !_isDialogVisible, Reset);
    }

    protected override void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(DialogSubscriptionId));
        OnPropertyChanged(nameof(IsDialogVisible));
        OnPropertyChanged(nameof(IsForRemoteSubscription));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SourcePlaceholder));
        RaiseSharedStateChanged();
        NotifyDialogStateChanged();
    }

    private void FocusFirstInvalidInput()
    {
        if (IsNameErrorVisible)
        {
            RequestInputFocus(DialogInputField.Name);
            return;
        }

        if (IsUrlErrorVisible)
        {
            RequestInputFocus(DialogInputField.Source);
            return;
        }

        if (IsAutoTestDelayIntervalErrorVisible)
        {
            RequestInputFocus(DialogInputField.AutoTestDelayInterval);
            return;
        }

        RequestInputFocus(DialogInputField.AutoUpdateInterval);
    }
}
