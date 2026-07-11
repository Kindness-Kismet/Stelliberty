namespace Stelliberty.Presentation.ViewModels;

public sealed class WebDavBackupItemViewModel(
    string fileName,
    string displayName,
    string detailText,
    string automationToken) : ViewModelBase
{
    private bool _isRestoring;
    private bool _isDeleting;
    private bool _isInteractionLocked;

    public string Id => FileName;

    public string FileName { get; } = fileName;

    public string DisplayName { get; } = displayName;

    public string DetailText { get; } = detailText;

    public bool HasDetailText => !string.IsNullOrWhiteSpace(DetailText);

    public string RestoreAutomationId => $"Settings.WebDavBackupDialog.Item.{automationToken}.RestoreButton";

    public string DeleteAutomationId => $"Settings.WebDavBackupDialog.Item.{automationToken}.DeleteButton";

    public bool IsRestoring
    {
        get => _isRestoring;
        private set
        {
            if (SetProperty(ref _isRestoring, value))
            {
                RefreshDerivedState();
            }
        }
    }

    public bool IsDeleting
    {
        get => _isDeleting;
        private set
        {
            if (SetProperty(ref _isDeleting, value))
            {
                RefreshDerivedState();
            }
        }
    }

    public bool IsBusy => IsRestoring || IsDeleting;

    public bool IsRestoreIconVisible => !IsRestoring;

    public bool IsDeleteIconVisible => !IsDeleting;

    public bool CanAct => !_isInteractionLocked && !IsBusy;

    public double ItemOpacity => CanAct || IsBusy ? 1 : 0.55;

    public void SetInteractionLocked(bool isLocked)
    {
        if (SetProperty(ref _isInteractionLocked, isLocked, nameof(CanAct)))
        {
            OnPropertyChanged(nameof(ItemOpacity));
        }
    }

    public void SetRestoring(bool isRestoring)
    {
        IsRestoring = isRestoring;
    }

    public void SetDeleting(bool isDeleting)
    {
        IsDeleting = isDeleting;
    }

    private void RefreshDerivedState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(IsRestoreIconVisible));
        OnPropertyChanged(nameof(IsDeleteIconVisible));
        OnPropertyChanged(nameof(CanAct));
        OnPropertyChanged(nameof(ItemOpacity));
    }
}
