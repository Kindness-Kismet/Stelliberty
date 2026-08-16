using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Domain.Overrides;

namespace Stelliberty.Presentation.ViewModels;

public sealed record OverrideEditCompletedEventArgs(
    string OverrideId,
    string Name,
    string SourceLocation,
    OverrideFormat Format,
    OverrideUpdateProxyMode UpdateProxyMode);

public sealed class OverrideEditDialogViewModel : OverrideDialogBase
{
    private readonly DialogCloseResetScheduler _closeReset = new();

    private string? _overrideId;
    private bool _isDialogVisible;
    private bool _isLocalFile;

    public OverrideEditDialogViewModel(ILocalizationService? localization = null)
        : base(localization)
    {
    }

    public event EventHandler<OverrideEditCompletedEventArgs>? Confirmed;

    public string? OverrideId => _overrideId;

    public bool IsDialogVisible => _isDialogVisible;

    // 本地含空创建与直接导入两种，统一按 IsLocalFile 隐藏路径与代理。
    public bool IsForLocalOverride => _overrideId is not null && _isLocalFile;

    public bool IsForRemoteOverride => _overrideId is not null && !_isLocalFile;

    public string SourceLabel => Localize(_isLocalFile ? "Overrides.Field.LocalPath" : "Overrides.Field.Url");

    public string SourcePlaceholder => Localize(_isLocalFile ? "Overrides.Placeholder.LocalPath" : "Overrides.Placeholder.Url");

    public override bool CanSubmit => _overrideId is not null;

    protected override bool IsRemoteSource => IsForRemoteOverride;

    public void Open(OverrideItemViewModel item)
    {
        _closeReset.Cancel();
        _overrideId = item.Id;
        _isDialogVisible = true;
        _isLocalFile = item.IsLocalFile;
        _name = item.Name;
        _sourceLocation = item.SourceLocation;
        _format = item.Format;
        _proxyMode = item.UpdateProxyMode;
        ResetValidation();
        RaiseStateChanged();
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

    protected override void Confirm()
    {
        if (!CanSubmit)
        {
            return;
        }

        if (!ValidateInputs())
        {
            RequestInputFocus(IsNameErrorVisible ? DialogInputField.Name : DialogInputField.Source);
            return;
        }

        var args = new OverrideEditCompletedEventArgs(_overrideId!, _name, _sourceLocation, _format, _proxyMode);
        BeginClose();
        Confirmed?.Invoke(this, args);
        AppLogger.Info($"Override edit submitted: {args.Name}");
    }

    protected override void Cancel()
    {
        BeginClose();
    }

    protected override void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SourcePlaceholder));
    }

    private void Reset()
    {
        _isDialogVisible = false;
        _overrideId = null;
        _isLocalFile = false;
        _name = string.Empty;
        _sourceLocation = string.Empty;
        _format = OverrideFormat.Yaml;
        _proxyMode = OverrideUpdateProxyMode.Direct;
        ResetValidation();
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
        OnPropertyChanged(nameof(OverrideId));
        OnPropertyChanged(nameof(IsDialogVisible));
        OnPropertyChanged(nameof(IsForLocalOverride));
        OnPropertyChanged(nameof(IsForRemoteOverride));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(SourcePlaceholder));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(SourceLocation));
        RaiseFormatStateChanged();
        RaiseProxyModeStateChanged();
        OnPropertyChanged(nameof(CanSubmit));
        RaiseValidationStateChanged();
        NotifyDialogStateChanged();
    }
}
