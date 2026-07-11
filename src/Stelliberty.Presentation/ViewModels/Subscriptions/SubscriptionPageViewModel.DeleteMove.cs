using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;

namespace Stelliberty.Presentation.ViewModels;

public sealed partial class SubscriptionPageViewModel
{
    private void ShowDeleteDialog(string? subscriptionId)
    {
        if (FindSubscriptionIndex(subscriptionId) < 0)
        {
            return;
        }

        _deleteDialogSubscriptionId = subscriptionId;
        RaiseMenuStateChanged();
    }

    private void ConfirmDelete()
    {
        if (string.IsNullOrWhiteSpace(_deleteDialogSubscriptionId))
        {
            return;
        }

        var subscriptionId = _deleteDialogSubscriptionId;
        var index = FindSubscriptionIndex(subscriptionId);
        if (index < 0)
        {
            _deleteDialogSubscriptionId = null;
            RaiseMenuStateChanged();
            return;
        }

        var isDeletingCurrentSubscription = string.Equals(_currentSubscriptionId, subscriptionId, StringComparison.Ordinal);
        _subscriptionDeleter?.Delete(subscriptionId);
        _subscriptions.RemoveAt(index);
        ClearSubscriptionReferences(subscriptionId);
        if (_subscriptionSelectionStore is not null)
        {
            _currentSubscriptionId = _subscriptionSelectionStore.GetCurrentSubscriptionId();
        }
        SyncCurrentSubscriptionRows();
        RaiseSubscriptionStateChanged();
        RaiseMenuStateChanged();
        if (isDeletingCurrentSubscription)
        {
            SubscriptionSelected?.Invoke(this, _currentSubscriptionId);
        }
    }

    private void CancelDeleteDialog()
    {
        _deleteDialogSubscriptionId = null;
        RaiseMenuStateChanged();
    }

    private void ClearSubscriptionReferences(string subscriptionId)
    {
        if (_currentSubscriptionId == subscriptionId)
        {
            // 删除当前订阅时清空选择，而不是接管下一个。
            _currentSubscriptionId = null;
        }

        if (_qrCodeSubscriptionId == subscriptionId)
        {
            _qrCodeCloseReset.Cancel();
            _isQrCodeDialogVisible = false;
            _qrCodeSubscriptionId = null;
        }

        OverrideSelector.ClearForSubscription(subscriptionId);

        Provider.ClearForSubscription(subscriptionId);

        ChainProxy.ClearForSubscription(subscriptionId);

        RuntimeConfigDialog.ClearForSubscription(subscriptionId);

        FileEditor.ClearForSubscription(subscriptionId);

        EditDialog.ClearForSubscription(subscriptionId);

        if (_deleteDialogSubscriptionId == subscriptionId)
        {
            _deleteDialogSubscriptionId = null;
        }
    }

    private void MoveSubscription(SubscriptionMoveRequest? request)
    {
        if (request is null)
        {
            return;
        }

        MoveSubscriptionTo(request.SubscriptionId, request.TargetIndex);
    }

    private void MoveSubscriptionUp(string? subscriptionId)
    {
        var index = FindSubscriptionIndex(subscriptionId);
        if (index <= 0)
        {
            return;
        }

        MoveSubscriptionTo(subscriptionId, index - 1);
    }

    private void MoveSubscriptionDown(string? subscriptionId)
    {
        var index = FindSubscriptionIndex(subscriptionId);
        if (index < 0 || index >= _subscriptions.Count - 1)
        {
            return;
        }

        MoveSubscriptionTo(subscriptionId, index + 1);
    }

    private void MoveSubscriptionTo(string? subscriptionId, int targetIndex)
    {
        var index = FindSubscriptionIndex(subscriptionId);
        if (index < 0)
        {
            return;
        }

        var subscription = _subscriptions[index];
        _subscriptions.RemoveAt(index);
        _subscriptions.Insert(Math.Clamp(targetIndex, 0, _subscriptions.Count), subscription);
        PersistSubscriptionOrder();
        RaiseSubscriptionStateChanged();
    }

    private void PersistSubscriptionOrder()
    {
        _reorderer?.SaveOrder(_subscriptions.Select(item => item.Id).ToList());
    }
}
