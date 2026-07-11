using System.Windows.Input;
using Stelliberty.Application.Localization;
using Stelliberty.Domain.Overrides;
using Stelliberty.Presentation.Commands;

namespace Stelliberty.Presentation.ViewModels;

// 覆写添加/编辑共享字段与格式、代理选择；来源类型由子类固定或切换。
public abstract class OverrideDialogBase : ViewModelBase, IDisposable
{
    private readonly RelayCommand _confirmCommand;

    protected readonly ILocalizationService? Localization;

    protected string _name = string.Empty;
    protected string _sourceLocation = string.Empty;
    protected OverrideFormat _format = OverrideFormat.Yaml;
    protected OverrideUpdateProxyMode _proxyMode;

    protected OverrideDialogBase(ILocalizationService? localization)
    {
        Localization = localization;
        if (Localization is not null)
        {
            Localization.LanguageChanged += HandleLanguageChanged;
        }
        SelectYamlFormatCommand = new RelayCommand(() => SetFormat(OverrideFormat.Yaml));
        SelectJavaScriptFormatCommand = new RelayCommand(() => SetFormat(OverrideFormat.JavaScript));
        SelectDirectProxyModeCommand = new RelayCommand(() => SetProxyMode(OverrideUpdateProxyMode.Direct));
        SelectSystemProxyModeCommand = new RelayCommand(() => SetProxyMode(OverrideUpdateProxyMode.SystemProxy));
        SelectCoreProxyModeCommand = new RelayCommand(() => SetProxyMode(OverrideUpdateProxyMode.Core));
        _confirmCommand = new RelayCommand(Confirm, () => CanSubmit);
        ConfirmCommand = _confirmCommand;
        CancelCommand = new RelayCommand(Cancel);
        PropertyChanged += HandlePropertyChanged;
    }

    public event EventHandler? DialogStateChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public string SourceLocation
    {
        get => _sourceLocation;
        set
        {
            if (SetProperty(ref _sourceLocation, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
                OnSourceLocationChanged();
            }
        }
    }

    public OverrideFormat Format
    {
        get => _format;
        set => SetProperty(ref _format, value);
    }

    public OverrideUpdateProxyMode UpdateProxyMode
    {
        get => _proxyMode;
        set => SetProperty(ref _proxyMode, value);
    }

    public bool IsYamlFormatSelected => Format == OverrideFormat.Yaml;

    public bool IsJavaScriptFormatSelected => Format == OverrideFormat.JavaScript;

    public bool IsDirectProxyModeSelected => UpdateProxyMode == OverrideUpdateProxyMode.Direct;

    public bool IsSystemProxyModeSelected => UpdateProxyMode == OverrideUpdateProxyMode.SystemProxy;

    public bool IsCoreProxyModeSelected => UpdateProxyMode == OverrideUpdateProxyMode.Core;

    public abstract bool CanSubmit { get; }

    public ICommand SelectYamlFormatCommand { get; }

    public ICommand SelectJavaScriptFormatCommand { get; }

    public ICommand SelectDirectProxyModeCommand { get; }

    public ICommand SelectSystemProxyModeCommand { get; }

    public ICommand SelectCoreProxyModeCommand { get; }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public virtual void Dispose()
    {
        PropertyChanged -= HandlePropertyChanged;
        if (Localization is not null)
        {
            Localization.LanguageChanged -= HandleLanguageChanged;
        }
    }

    protected abstract void Confirm();

    protected abstract void Cancel();

    // 子类改完广播前刷新自身字段；语言切换时也复用。
    protected abstract void RaiseStateChanged();

    // 广播格式选择状态。
    protected void RaiseFormatStateChanged()
    {
        OnPropertyChanged(nameof(Format));
        OnPropertyChanged(nameof(IsYamlFormatSelected));
        OnPropertyChanged(nameof(IsJavaScriptFormatSelected));
    }

    // 广播代理模式选择状态。
    protected void RaiseProxyModeStateChanged()
    {
        OnPropertyChanged(nameof(UpdateProxyMode));
        OnPropertyChanged(nameof(IsDirectProxyModeSelected));
        OnPropertyChanged(nameof(IsSystemProxyModeSelected));
        OnPropertyChanged(nameof(IsCoreProxyModeSelected));
    }

    protected void NotifyDialogStateChanged()
    {
        DialogStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // 来源变化的附加通知交给子类（粘贴按钮显隐等）。
    protected virtual void OnSourceLocationChanged()
    {
    }

    // 语言切换时子类刷新自身广播。
    protected virtual void OnLanguageChanged()
    {
    }

    protected string Localize(string key) => Localization?.GetString(key) ?? key;

    private void SetFormat(OverrideFormat format)
    {
        _format = format;
        RaiseFormatStateChanged();
    }

    private void SetProxyMode(OverrideUpdateProxyMode mode)
    {
        _proxyMode = mode;
        RaiseProxyModeStateChanged();
    }

    private void HandleLanguageChanged(object? sender, EventArgs args)
    {
        OnLanguageChanged();
    }

    // Avalonia 仅在命令事件后重算可执行状态；属性通知需同步转发。
    private void HandlePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CanSubmit))
        {
            _confirmCommand.RaiseCanExecuteChanged();
        }
    }
}
