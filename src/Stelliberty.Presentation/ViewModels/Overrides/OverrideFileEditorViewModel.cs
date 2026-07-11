using System.Windows.Input;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed record OverrideFileEditCompletedEventArgs(string OverrideId, string Content);

public sealed class OverrideFileEditorViewModel : ViewModelBase
{
    private readonly DialogCloseResetScheduler _closeReset = new();

    private string? _overrideId;
    private bool _isDialogVisible;
    private string _content = string.Empty;
    private string _syntaxLanguage = "plaintext";

    public OverrideFileEditorViewModel()
    {
        ConfirmCommand = new RelayCommand(Confirm);
        CancelCommand = new RelayCommand(Cancel);
    }

    public event EventHandler<OverrideFileEditCompletedEventArgs>? Confirmed;

    public event EventHandler? DialogStateChanged;

    public string? OverrideId => _overrideId;

    public bool IsDialogVisible => _isDialogVisible;

    public string Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    public string SyntaxLanguage
    {
        get => _syntaxLanguage;
        private set => SetProperty(ref _syntaxLanguage, value);
    }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public void Open(string overrideId, string content, string syntaxLanguage = "plaintext")
    {
        _closeReset.Cancel();
        _overrideId = overrideId;
        _isDialogVisible = true;
        Content = content;
        SyntaxLanguage = syntaxLanguage;
        OnPropertyChanged(nameof(OverrideId));
        OnPropertyChanged(nameof(IsDialogVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Close()
    {
        BeginClose();
    }

    public void ClearForOverride(string overrideId)
    {
        if (_overrideId == overrideId)
        {
            BeginClose();
        }
    }

    private void Confirm()
    {
        if (_overrideId is null)
        {
            return;
        }

        var confirmedId = _overrideId;
        var confirmedContent = _content;
        BeginClose();
        Confirmed?.Invoke(this, new OverrideFileEditCompletedEventArgs(confirmedId, confirmedContent));
    }

    private void Cancel()
    {
        BeginClose();
    }

    private void Reset()
    {
        _isDialogVisible = false;
        _overrideId = null;
        _content = string.Empty;
        _syntaxLanguage = "plaintext";
        OnPropertyChanged(nameof(OverrideId));
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(SyntaxLanguage));
        OnPropertyChanged(nameof(IsDialogVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BeginClose()
    {
        if (!_isDialogVisible)
        {
            return;
        }

        _isDialogVisible = false;
        OnPropertyChanged(nameof(IsDialogVisible));
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
        _closeReset.Run(() => !_isDialogVisible, Reset);
    }
}
