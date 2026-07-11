using System.Windows.Input;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed class SubscriptionRuntimeConfigDialogViewModel : ViewModelBase
{
    private readonly DialogCloseResetScheduler _closeReset = new();

    private string? _subscriptionId;
    private bool _isDialogVisible;
    private string _title = string.Empty;
    private string _content = string.Empty;

    public SubscriptionRuntimeConfigDialogViewModel()
    {
        CloseCommand = new RelayCommand(Close);
    }

    public event EventHandler? DialogStateChanged;

    public string? DialogSubscriptionId => _subscriptionId;

    public string Title => _title;

    public string Content => _content;

    public bool IsDialogVisible => _isDialogVisible;

    public ICommand CloseCommand { get; }

    public void Open(string subscriptionId, string title, string content)
    {
        _closeReset.Cancel();
        _subscriptionId = subscriptionId;
        _isDialogVisible = true;
        _title = title;
        _content = content;
        RaiseStateChanged();
    }

    public void Close()
    {
        if (!_isDialogVisible)
        {
            return;
        }

        _isDialogVisible = false;
        RaiseStateChanged();
        _closeReset.Run(() => !_isDialogVisible, Reset);
    }

    private void Reset()
    {
        _isDialogVisible = false;
        _subscriptionId = null;
        _title = string.Empty;
        _content = string.Empty;
        RaiseStateChanged();
    }

    public void ClearForSubscription(string subscriptionId)
    {
        if (_subscriptionId == subscriptionId)
        {
            Close();
        }
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(DialogSubscriptionId));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(IsDialogVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
