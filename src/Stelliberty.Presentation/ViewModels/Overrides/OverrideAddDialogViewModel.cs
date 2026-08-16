using Stelliberty.Application.Localization;
using Stelliberty.Domain.Overrides;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed record OverrideAddRemoteRequestedEventArgs(
    string Name,
    string SourceLocation,
    OverrideFormat Format,
    OverrideUpdateProxyMode UpdateProxyMode);

public sealed record OverrideAddLocalRequestedEventArgs(
    string Name,
    string SourceLocation,
    OverrideFormat Format);

public sealed record OverrideAddCreateBlankRequestedEventArgs(
    string Name,
    OverrideFormat Format);

public sealed class OverrideAddDialogViewModel : OverrideDialogBase
{
    private readonly DialogCloseResetScheduler _closeReset = new();
    private bool _isDialogVisible;
    private bool _isSubmitting;
    private bool _hasClipboardText;
    private OverrideAddMethod _selectedAddMethod = OverrideAddMethod.Remote;

    public OverrideAddDialogViewModel(ILocalizationService? localization = null)
        : base(localization)
    {
        ShowCommand = new RelayCommand(Open);
        SelectRemoteMethodCommand = new RelayCommand(() => SelectAddMethod(OverrideAddMethod.Remote));
        SelectBlankMethodCommand = new RelayCommand(() => SelectAddMethod(OverrideAddMethod.Blank));
        SelectLocalMethodCommand = new RelayCommand(() => SelectAddMethod(OverrideAddMethod.Local));
    }

    public event EventHandler<OverrideAddRemoteRequestedEventArgs>? RemoteRequested;

    public event EventHandler<OverrideAddLocalRequestedEventArgs>? LocalRequested;

    public event EventHandler<OverrideAddCreateBlankRequestedEventArgs>? CreateBlankRequested;

    public bool IsDialogVisible => _isDialogVisible;

    public bool IsSubmitting => _isSubmitting;

    public OverrideAddMethod SelectedAddMethod
    {
        get => _selectedAddMethod;
        set => SetProperty(ref _selectedAddMethod, value);
    }

    public bool IsRemoteMethodSelected => _selectedAddMethod == OverrideAddMethod.Remote;

    public bool IsBlankMethodSelected => _selectedAddMethod == OverrideAddMethod.Blank;

    public bool IsLocalMethodSelected => _selectedAddMethod == OverrideAddMethod.Local;

    public bool IsRemoteAddressFieldVisible => IsRemoteMethodSelected;

    public bool IsLocalFileFieldVisible => IsLocalMethodSelected;

    public bool IsProxyModeVisible => IsRemoteMethodSelected;

    public bool IsUrlPasteButtonVisible => IsRemoteMethodSelected && string.IsNullOrWhiteSpace(SourceLocation);

    public bool CanPasteUrlFromClipboard => IsUrlPasteButtonVisible && _hasClipboardText && !IsSubmitting;

    public override bool CanSubmit => !_isSubmitting;

    protected override bool IsRemoteSource => IsRemoteMethodSelected;

    protected override bool IsLocalSourceRequired => IsLocalMethodSelected;

    public RelayCommand ShowCommand { get; }

    public RelayCommand SelectRemoteMethodCommand { get; }

    public RelayCommand SelectBlankMethodCommand { get; }

    public RelayCommand SelectLocalMethodCommand { get; }

    public void Open()
    {
        _closeReset.Cancel();
        _isDialogVisible = true;
        _isSubmitting = false;
        _selectedAddMethod = OverrideAddMethod.Remote;
        _name = string.Empty;
        _sourceLocation = string.Empty;
        _format = OverrideFormat.Yaml;
        _proxyMode = OverrideUpdateProxyMode.Direct;
        ResetValidation();
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

    public void BeginSubmit()
    {
        if (_isSubmitting)
        {
            return;
        }

        _isSubmitting = true;
        OnPropertyChanged(nameof(IsSubmitting));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanPasteUrlFromClipboard));
    }

    public void EndSubmit()
    {
        if (!_isSubmitting)
        {
            return;
        }

        _isSubmitting = false;
        OnPropertyChanged(nameof(IsSubmitting));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanPasteUrlFromClipboard));
    }

    public void SetClipboardTextAvailable(bool hasText)
    {
        if (SetProperty(ref _hasClipboardText, hasText, nameof(CanPasteUrlFromClipboard)))
        {
            OnPropertyChanged(nameof(IsUrlPasteButtonVisible));
        }
    }

    public void PasteUrl(string value)
    {
        if (IsRemoteMethodSelected && string.IsNullOrWhiteSpace(SourceLocation) && !string.IsNullOrWhiteSpace(value))
        {
            SourceLocation = value.Trim();
        }
    }

    protected override void OnSourceLocationChanged()
    {
        OnPropertyChanged(nameof(IsUrlPasteButtonVisible));
        OnPropertyChanged(nameof(CanPasteUrlFromClipboard));
    }

    protected override void Confirm()
    {
        if (_isSubmitting)
        {
            return;
        }

        if (!ValidateInputs())
        {
            FocusFirstInvalidInput();
            return;
        }

        BeginSubmit();
        switch (_selectedAddMethod)
        {
            case OverrideAddMethod.Remote:
                RemoteRequested?.Invoke(this, new OverrideAddRemoteRequestedEventArgs(
                    _name.Trim(),
                    _sourceLocation.Trim(),
                    _format,
                    _proxyMode));
                break;
            case OverrideAddMethod.Local:
                LocalRequested?.Invoke(this, new OverrideAddLocalRequestedEventArgs(
                    _name.Trim(),
                    _sourceLocation.Trim(),
                    _format));
                break;
            case OverrideAddMethod.Blank:
                CreateBlankRequested?.Invoke(this, new OverrideAddCreateBlankRequestedEventArgs(
                    _name.Trim(),
                    _format));
                break;
        }
    }

    protected override void Cancel()
    {
        Close();
    }

    private void Reset()
    {
        _isSubmitting = false;
        _selectedAddMethod = OverrideAddMethod.Remote;
        _name = string.Empty;
        _sourceLocation = string.Empty;
        _format = OverrideFormat.Yaml;
        _proxyMode = OverrideUpdateProxyMode.Direct;
        ResetValidation();
        RaiseStateChanged();
    }

    private void SelectAddMethod(OverrideAddMethod method)
    {
        if (_selectedAddMethod == method)
        {
            // 重复点击会恢复单选状态，避免 ToggleButton 漂到未选择。
            RaiseMethodFlags();
            OnPropertyChanged(nameof(IsUrlPasteButtonVisible));
            OnPropertyChanged(nameof(CanPasteUrlFromClipboard));
            return;
        }

        _selectedAddMethod = method;
        // 来源变化会清空路径，避免远程 URL 和本地文件交叉污染。
        _sourceLocation = string.Empty;
        RefreshSourceLocationValidation();
        RaiseStateChanged();
    }

    private void RaiseMethodFlags()
    {
        OnPropertyChanged(nameof(IsRemoteMethodSelected));
        OnPropertyChanged(nameof(IsBlankMethodSelected));
        OnPropertyChanged(nameof(IsLocalMethodSelected));
    }

    protected override void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(IsDialogVisible));
        OnPropertyChanged(nameof(IsSubmitting));
        OnPropertyChanged(nameof(SelectedAddMethod));
        OnPropertyChanged(nameof(IsRemoteMethodSelected));
        OnPropertyChanged(nameof(IsBlankMethodSelected));
        OnPropertyChanged(nameof(IsLocalMethodSelected));
        OnPropertyChanged(nameof(IsRemoteAddressFieldVisible));
        OnPropertyChanged(nameof(IsLocalFileFieldVisible));
        OnPropertyChanged(nameof(IsProxyModeVisible));
        OnPropertyChanged(nameof(IsUrlPasteButtonVisible));
        OnPropertyChanged(nameof(CanPasteUrlFromClipboard));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(SourceLocation));
        RaiseFormatStateChanged();
        RaiseProxyModeStateChanged();
        OnPropertyChanged(nameof(CanSubmit));
        RaiseValidationStateChanged();
        NotifyDialogStateChanged();
    }

    private void FocusFirstInvalidInput()
    {
        if (IsNameErrorVisible)
        {
            RequestInputFocus(DialogInputField.Name);
            return;
        }

        RequestInputFocus(IsLocalMethodSelected ? DialogInputField.LocalFile : DialogInputField.Source);
    }
}
