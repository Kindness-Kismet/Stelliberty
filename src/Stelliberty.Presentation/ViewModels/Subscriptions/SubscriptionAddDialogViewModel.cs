using Stelliberty.Application.Localization;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

public sealed record SubscriptionAddRemoteRequestedEventArgs(
    string Name,
    string Url,
    string UserAgent,
    int AutoTestDelayIntervalMinutes,
    SubscriptionAutoUpdateMode AutoUpdateMode,
    int AutoUpdateIntervalMinutes,
    SubscriptionUpdateProxyMode UpdateProxyMode,
    string AgeSecretKey = "");

public sealed record SubscriptionAddLocalRequestedEventArgs(
    string Name,
    string LocalFilePath,
    int AutoTestDelayIntervalMinutes);

public sealed class SubscriptionAddDialogViewModel : SubscriptionDialogBase
{
    private readonly DialogCloseResetScheduler _closeReset = new();
    private bool _isDialogVisible;
    private bool _isSubmitting;
    private bool _isLocalImportSelected;
    private string _localFilePath = string.Empty;

    public SubscriptionAddDialogViewModel(ILocalizationService? localization = null)
        : base(localization)
    {
        ShowCommand = new RelayCommand(Open);
        SelectRemoteImportCommand = new RelayCommand(() => SelectImportMode(isLocal: false));
        SelectLocalImportCommand = new RelayCommand(() => SelectImportMode(isLocal: true));
    }

    public event EventHandler<SubscriptionAddRemoteRequestedEventArgs>? RemoteRequested;

    public event EventHandler<SubscriptionAddLocalRequestedEventArgs>? LocalRequested;

    public bool IsDialogVisible => _isDialogVisible;

    public bool IsSubmitting => _isSubmitting;

    public bool IsLocalImportSelected => _isLocalImportSelected;

    public bool IsRemoteImportSelected => !_isLocalImportSelected;

    public bool IsRemoteFieldsVisible => IsRemoteImportSelected;

    public bool IsLocalFieldsVisible => IsLocalImportSelected;

    public bool IsRemoteOptionsVisible => IsRemoteImportSelected;

    // 远程语境即选中远程导入；提交中禁止粘贴。
    protected override bool IsRemoteContext => IsRemoteImportSelected;

    protected override bool IsPasteBlocked => _isSubmitting;

    public string LocalFilePath
    {
        get => _localFilePath;
        set
        {
            if (SetProperty(ref _localFilePath, value))
            {
                OnPropertyChanged(nameof(LocalFilePathError));
                OnPropertyChanged(nameof(IsLocalFilePathErrorVisible));
            }
        }
    }

    public override bool CanSubmit => !_isSubmitting;

    public string LocalFilePathError => _hasAttemptedSubmit && _isLocalImportSelected && string.IsNullOrWhiteSpace(_localFilePath)
        ? Localize("Subscriptions.Validation.LocalFileRequired")
        : string.Empty;

    public bool IsLocalFilePathErrorVisible => !string.IsNullOrEmpty(LocalFilePathError);

    public RelayCommand ShowCommand { get; }

    public RelayCommand SelectRemoteImportCommand { get; }

    public RelayCommand SelectLocalImportCommand { get; }

    public void Open()
    {
        _closeReset.Cancel();
        _isDialogVisible = true;
        _isSubmitting = false;
        _isLocalImportSelected = false;
        _localFilePath = string.Empty;
        ResetSharedState(
            name: string.Empty,
            url: string.Empty,
            userAgent: SubscriptionDefaults.UserAgent,
            ageSecretKey: string.Empty,
            autoTestDelayIntervalMinutes: 0,
            autoUpdateMode: SubscriptionAutoUpdateMode.Disabled,
            autoUpdateIntervalMinutes: 0,
            updateProxyMode: SubscriptionUpdateProxyMode.Direct);
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

    protected override void Confirm()
    {
        if (_isSubmitting)
        {
            return;
        }

        var sharedInputsValid = ValidateSharedInputs(_isLocalImportSelected);
        OnPropertyChanged(nameof(LocalFilePathError));
        OnPropertyChanged(nameof(IsLocalFilePathErrorVisible));
        if (!sharedInputsValid || IsLocalFilePathErrorVisible)
        {
            FocusFirstInvalidInput();
            return;
        }

        BeginSubmit();
        if (_isLocalImportSelected)
        {
            ConfirmLocal();
        }
        else
        {
            ConfirmRemote();
        }
    }

    protected override void Cancel()
    {
        Close();
    }

    private void Reset()
    {
        _isSubmitting = false;
        _isLocalImportSelected = false;
        _localFilePath = string.Empty;
        ResetSharedState(
            name: string.Empty,
            url: string.Empty,
            userAgent: SubscriptionDefaults.UserAgent,
            ageSecretKey: string.Empty,
            autoTestDelayIntervalMinutes: 0,
            autoUpdateMode: SubscriptionAutoUpdateMode.Disabled,
            autoUpdateIntervalMinutes: 0,
            updateProxyMode: SubscriptionUpdateProxyMode.Direct);
        RaiseStateChanged();
    }

    private void SelectImportMode(bool isLocal)
    {
        if (_isLocalImportSelected == isLocal)
        {
            OnPropertyChanged(nameof(IsLocalImportSelected));
            OnPropertyChanged(nameof(IsRemoteImportSelected));
            OnPropertyChanged(nameof(IsUrlPasteButtonVisible));
            OnPropertyChanged(nameof(CanPasteUrlFromClipboard));
            return;
        }

        _isLocalImportSelected = isLocal;
        if (isLocal)
        {
            _selectedAutoUpdateMode = SubscriptionAutoUpdateMode.Disabled;
            _autoUpdateIntervalMinutes = 0;
            _autoUpdateIntervalMinutesText = "0";
            _autoUpdateIntervalMinutesError = string.Empty;
            _selectedUpdateProxyMode = SubscriptionUpdateProxyMode.Direct;
            _ageSecretKey = string.Empty;
            ClearUrlError();
        }
        else if (_hasAttemptedSubmit)
        {
            RefreshUrlValidation();
        }

        RaiseStateChanged();
    }

    private void ConfirmRemote()
    {
        RemoteRequested?.Invoke(this, new SubscriptionAddRemoteRequestedEventArgs(
            _name.Trim(),
            _url.Trim(),
            NormalizeUserAgent(),
            _autoTestDelayIntervalMinutes,
            _selectedAutoUpdateMode,
            _autoUpdateIntervalMinutes,
            _selectedUpdateProxyMode,
            _ageSecretKey.Trim()));
    }

    private void ConfirmLocal()
    {
        LocalRequested?.Invoke(this, new SubscriptionAddLocalRequestedEventArgs(
            _name.Trim(),
            _localFilePath.Trim(),
            _autoTestDelayIntervalMinutes));
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

        if (IsLocalFilePathErrorVisible)
        {
            RequestInputFocus(DialogInputField.LocalFile);
            return;
        }

        if (IsAutoTestDelayIntervalErrorVisible)
        {
            RequestInputFocus(DialogInputField.AutoTestDelayInterval);
            return;
        }

        RequestInputFocus(DialogInputField.AutoUpdateInterval);
    }

    protected override void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(IsDialogVisible));
        OnPropertyChanged(nameof(IsSubmitting));
        OnPropertyChanged(nameof(IsLocalImportSelected));
        OnPropertyChanged(nameof(IsRemoteImportSelected));
        OnPropertyChanged(nameof(IsRemoteFieldsVisible));
        OnPropertyChanged(nameof(IsLocalFieldsVisible));
        OnPropertyChanged(nameof(IsRemoteOptionsVisible));
        OnPropertyChanged(nameof(IsUrlPasteButtonVisible));
        OnPropertyChanged(nameof(CanPasteUrlFromClipboard));
        OnPropertyChanged(nameof(LocalFilePath));
        OnPropertyChanged(nameof(LocalFilePathError));
        OnPropertyChanged(nameof(IsLocalFilePathErrorVisible));
        RaiseSharedStateChanged();
        NotifyDialogStateChanged();
    }
}
