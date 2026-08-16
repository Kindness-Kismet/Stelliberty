using System.Windows.Input;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionFileEditCompletedEventArgs(string SubscriptionId, string Content);

public sealed class SubscriptionFileEditorViewModel : ViewModelBase
{
    private readonly DialogCloseResetScheduler _closeReset = new();

    private string? _subscriptionId;
    private bool _isDialogVisible;
    private string _content = string.Empty;

    public SubscriptionFileEditorViewModel()
    {
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    public event EventHandler<SubscriptionFileEditCompletedEventArgs>? Confirmed;

    public event EventHandler? DialogStateChanged;

    public string? DialogSubscriptionId => _subscriptionId;

    public bool IsDialogVisible => _isDialogVisible;

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public void Open(string subscriptionId, string content)
    {
        _closeReset.Cancel();
        _subscriptionId = subscriptionId;
        _isDialogVisible = true;
        Content = content;
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

    private void Confirm()
    {
        if (_subscriptionId is null)
        {
            return;
        }

        var args = new SubscriptionFileEditCompletedEventArgs(_subscriptionId, _content);
        BeginClose();
        Confirmed?.Invoke(this, args);
    }

    private void Cancel()
    {
        BeginClose();
    }

    private void Reset()
    {
        _isDialogVisible = false;
        _subscriptionId = null;
        _content = string.Empty;
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

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(DialogSubscriptionId));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(IsDialogVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
