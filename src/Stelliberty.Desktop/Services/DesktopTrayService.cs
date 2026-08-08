using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop.Services;

internal sealed class DesktopTrayService : IDisposable
{
    private WindowIcon? _icon;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private IControlledApplicationLifetime? _controlledLifetime;
    private MainWindow? _window;
    private MainWindowViewModel? _viewModel;
    private HomePageViewModel? _homePage;
    private ILocalizationService? _localization;
    private TrayIcon? _trayIcon;
    private TrayIconState _iconState = TrayIconState.Disabled;
    private long _lastTrayClickTick;
    private TrayMenuState? _appliedMenuState;
    private bool _isStateUpdateScheduled;
    private static readonly TimeSpan TrayDoubleClickThreshold = TimeSpan.FromMilliseconds(500);

    private NativeMenuItem? _showItem;
    private NativeMenuItem? _copyItem;
    private NativeMenuItem? _copyPowerShellItem;
    private NativeMenuItem? _copyCmdItem;
    private NativeMenuItem? _copyBashItem;
    private NativeMenuItem? _outboundItem;
    private NativeMenuItem? _outboundRuleItem;
    private NativeMenuItem? _outboundGlobalItem;
    private NativeMenuItem? _outboundDirectItem;
    private NativeMenuItem? _systemProxyItem;
    private NativeMenuItem? _tunItem;
    private NativeMenuItem? _restartCoreItem;
    private NativeMenuItem? _exitItem;

    // 只有这些变更需要重算菜单启用和勾选状态。
    private static readonly HashSet<string> MenuAffectingProps =
    [
        nameof(HomePageViewModel.IsSystemProxyEnabled),
        nameof(HomePageViewModel.IsTunEnabled),
        nameof(HomePageViewModel.IsTunToggleEnabled),
        nameof(HomePageViewModel.IsCoreRunning),
        nameof(HomePageViewModel.IsCoreInteractive),
        nameof(HomePageViewModel.CanRestartCore),
        nameof(HomePageViewModel.IsRuleOutboundSelected),
        nameof(HomePageViewModel.IsGlobalOutboundSelected),
        nameof(HomePageViewModel.IsDirectOutboundSelected),
    ];

    private readonly record struct TrayMenuState(
        bool IsRuleOutboundSelected,
        bool IsGlobalOutboundSelected,
        bool IsDirectOutboundSelected,
        bool IsSystemProxyEnabled,
        bool IsTunEnabled,
        bool IsTunToggleEnabled,
        bool CanRestartCore,
        bool IsCoreRunning,
        bool IsCoreInteractive,
        bool IsShowEnabled,
        TrayIconState IconState);

    public void Attach(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window,
        MainWindowViewModel viewModel,
        ILocalizationService localization)
    {
        Dispose();

        _desktop = desktop;
        _controlledLifetime = desktop;
        _window = window;
        _viewModel = viewModel;
        _homePage = viewModel.HomePage;
        _localization = localization;
        _iconState = ResolveTrayIconState();
        _icon = TrayIconFactory.Create(_iconState);

        _showItem = new NativeMenuItem();
        _showItem.Click += OnShowClicked;

        _copyPowerShellItem = new NativeMenuItem();
        _copyPowerShellItem.Click += OnCopyPowerShellClicked;
        _copyCmdItem = new NativeMenuItem();
        _copyCmdItem.Click += OnCopyCmdClicked;
        _copyBashItem = new NativeMenuItem();
        _copyBashItem.Click += OnCopyBashClicked;
        _copyItem = new NativeMenuItem
        {
            Menu = new NativeMenu { _copyPowerShellItem, _copyCmdItem, _copyBashItem }
        };

        _outboundRuleItem = new NativeMenuItem { ToggleType = MenuItemToggleType.Radio };
        _outboundRuleItem.Click += OnOutboundRuleClicked;
        _outboundGlobalItem = new NativeMenuItem { ToggleType = MenuItemToggleType.Radio };
        _outboundGlobalItem.Click += OnOutboundGlobalClicked;
        _outboundDirectItem = new NativeMenuItem { ToggleType = MenuItemToggleType.Radio };
        _outboundDirectItem.Click += OnOutboundDirectClicked;
        _outboundItem = new NativeMenuItem
        {
            Menu = new NativeMenu { _outboundRuleItem, _outboundGlobalItem, _outboundDirectItem }
        };

        _systemProxyItem = new NativeMenuItem { ToggleType = MenuItemToggleType.CheckBox };
        _systemProxyItem.Click += OnSystemProxyClicked;
        _tunItem = new NativeMenuItem { ToggleType = MenuItemToggleType.CheckBox };
        _tunItem.Click += OnTunClicked;
        _restartCoreItem = new NativeMenuItem();
        _restartCoreItem.Click += OnRestartCoreClicked;
        _exitItem = new NativeMenuItem();
        _exitItem.Click += OnExitClicked;

        _trayIcon = new TrayIcon
        {
            Icon = _icon,
            Menu = new NativeMenu
            {
                _showItem,
                new NativeMenuItemSeparator(),
                _copyItem,
                new NativeMenuItemSeparator(),
                _outboundItem,
                new NativeMenuItemSeparator(),
                _systemProxyItem,
                _tunItem,
                _restartCoreItem,
                new NativeMenuItemSeparator(),
                _exitItem,
            },
            IsVisible = true
        };
        _trayIcon.Clicked += OnTrayIconClicked;

        UpdateText();
        UpdateState();
        TrayIcon.SetIcons(Avalonia.Application.Current!, [_trayIcon]);

        localization.LanguageChanged += OnLanguageChanged;
        _homePage.PropertyChanged += OnHomeStateChanged;
        window.PropertyChanged += OnWindowPropertyChanged;
        _controlledLifetime.Exit += OnApplicationExit;
    }

    public void Dispose()
    {
        if (_localization is not null)
        {
            _localization.LanguageChanged -= OnLanguageChanged;
        }

        if (_homePage is not null)
        {
            _homePage.PropertyChanged -= OnHomeStateChanged;
        }

        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
        }

        if (_controlledLifetime is not null)
        {
            _controlledLifetime.Exit -= OnApplicationExit;
        }

        if (_showItem is not null) _showItem.Click -= OnShowClicked;
        if (_copyPowerShellItem is not null) _copyPowerShellItem.Click -= OnCopyPowerShellClicked;
        if (_copyCmdItem is not null) _copyCmdItem.Click -= OnCopyCmdClicked;
        if (_copyBashItem is not null) _copyBashItem.Click -= OnCopyBashClicked;
        if (_outboundRuleItem is not null) _outboundRuleItem.Click -= OnOutboundRuleClicked;
        if (_outboundGlobalItem is not null) _outboundGlobalItem.Click -= OnOutboundGlobalClicked;
        if (_outboundDirectItem is not null) _outboundDirectItem.Click -= OnOutboundDirectClicked;
        if (_systemProxyItem is not null) _systemProxyItem.Click -= OnSystemProxyClicked;
        if (_tunItem is not null) _tunItem.Click -= OnTunClicked;
        if (_restartCoreItem is not null) _restartCoreItem.Click -= OnRestartCoreClicked;
        if (_exitItem is not null) _exitItem.Click -= OnExitClicked;
        if (_trayIcon is not null) _trayIcon.Clicked -= OnTrayIconClicked;

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }

        _desktop = null;
        _controlledLifetime = null;
        _window = null;
        _viewModel = null;
        _homePage = null;
        _localization = null;
        _trayIcon = null;
        _appliedMenuState = null;
        _isStateUpdateScheduled = false;
        _showItem = null;
        _copyItem = null;
        _copyPowerShellItem = null;
        _copyCmdItem = null;
        _copyBashItem = null;
        _outboundItem = null;
        _outboundRuleItem = null;
        _outboundGlobalItem = null;
        _outboundDirectItem = null;
        _systemProxyItem = null;
        _tunItem = null;
        _restartCoreItem = null;
        _exitItem = null;
    }

    private void OnShowClicked(object? sender, EventArgs args)
    {
        // 原生菜单关闭后再显示窗口，避免首次显示与菜单收起争抢窗口激活。
        Dispatcher.UIThread.Post(ShowMainWindow, DispatcherPriority.Background);
    }

    private void OnTrayIconClicked(object? sender, EventArgs args)
    {
        if (_viewModel?.AppBehavior.IsTrayDoubleClickEnabled != true)
        {
            _lastTrayClickTick = 0;
            ToggleMainWindowVisibility();
            return;
        }

        if (!ConsumeTrayDoubleClick())
        {
            return;
        }

        ToggleMainWindowVisibility();
    }

    private bool ConsumeTrayDoubleClick()
    {
        var current = Stopwatch.GetTimestamp();
        if (_lastTrayClickTick == 0)
        {
            _lastTrayClickTick = current;
            return false;
        }

        var elapsed = Stopwatch.GetElapsedTime(_lastTrayClickTick, current);
        _lastTrayClickTick = elapsed <= TrayDoubleClickThreshold ? 0 : current;
        return elapsed <= TrayDoubleClickThreshold;
    }

    public void ToggleMainWindowVisibility()
    {
        if (_window is null)
        {
            return;
        }

        if (_window.IsVisible && _window.WindowState != WindowState.Minimized)
        {
            _window.Hide();
            return;
        }

        ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (_window is null)
        {
            return;
        }
#if DEBUG
        var startedAt = Stopwatch.GetTimestamp();
        AppLogger.Info($"[StartupTrace] Tray window show started visible={_window.IsVisible} state={_window.WindowState}");
#endif
        if (_desktop is not null)
        {
            if (_desktop.MainWindow is null)
            {
                _desktop.MainWindow = _window;
            }
            _desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
        }
        var wasMinimized = _window.WindowState == WindowState.Minimized;
        _window.Show();
        if (wasMinimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
#if DEBUG
        AppLogger.Info($"[StartupTrace] Tray window show returned elapsed={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}ms visible={_window.IsVisible} state={_window.WindowState}");
#endif
    }

    private void OnCopyPowerShellClicked(object? sender, EventArgs args) => _homePage?.CopyTerminalProxyCommand(TerminalShell.PowerShell);

    private void OnCopyCmdClicked(object? sender, EventArgs args) => _homePage?.CopyTerminalProxyCommand(TerminalShell.Cmd);

    private void OnCopyBashClicked(object? sender, EventArgs args) => _homePage?.CopyTerminalProxyCommand(TerminalShell.Bash);

    private void OnOutboundRuleClicked(object? sender, EventArgs args)
    {
        if (_homePage?.IsCoreInteractive != true) return;
        _homePage.SetRuleOutboundCommand.Execute(null);
    }

    private void OnOutboundGlobalClicked(object? sender, EventArgs args)
    {
        if (_homePage?.IsCoreInteractive != true) return;
        _homePage.SetGlobalOutboundCommand.Execute(null);
    }

    private void OnOutboundDirectClicked(object? sender, EventArgs args)
    {
        if (_homePage?.IsCoreInteractive != true) return;
        _homePage.SetDirectOutboundCommand.Execute(null);
    }

    private void OnSystemProxyClicked(object? sender, EventArgs args)
    {
        if (_homePage is null)
        {
            return;
        }
        _homePage.ToggleSystemProxyCommand.Execute(null);
    }

    private void OnTunClicked(object? sender, EventArgs args)
    {
        if (_homePage is null || !_homePage.IsTunToggleEnabled)
        {
            return;
        }
        _homePage.IsTunEnabled = !_homePage.IsTunEnabled;
    }

    private void OnRestartCoreClicked(object? sender, EventArgs args)
    {
        if (_homePage?.CanRestartCore != true)
        {
            return;
        }
        _homePage.RestartCoreCommand.Execute(null);
    }

    private void OnExitClicked(object? sender, EventArgs args)
    {
        _window?.RequestShutdown();
    }

    private void OnLanguageChanged(object? sender, EventArgs args) => UpdateText();

    private void OnHomeStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is null || MenuAffectingProps.Contains(args.PropertyName))
        {
            ScheduleStateUpdate();
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        // 窗口可见性和最小化状态会影响显示主窗口是否可用。
        if (args.Property == Visual.IsVisibleProperty || args.Property == Window.WindowStateProperty)
        {
            ScheduleStateUpdate();
        }
    }

    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs args) => Dispose();

    private void UpdateText()
    {
        if (_localization is null)
        {
            return;
        }
        if (_showItem is not null) _showItem.Header = _localization.GetString("Tray.Show");
        if (_copyItem is not null) _copyItem.Header = _localization.GetString("Tray.CopyTerminalProxy");
        if (_copyPowerShellItem is not null) _copyPowerShellItem.Header = _localization.GetString("Tray.Terminal.PowerShell");
        if (_copyCmdItem is not null) _copyCmdItem.Header = _localization.GetString("Tray.Terminal.Cmd");
        if (_copyBashItem is not null) _copyBashItem.Header = _localization.GetString("Tray.Terminal.Bash");
        if (_outboundItem is not null) _outboundItem.Header = _localization.GetString("Tray.OutboundMode");
        if (_outboundRuleItem is not null) _outboundRuleItem.Header = _localization.GetString("Tray.RuleMode");
        if (_outboundGlobalItem is not null) _outboundGlobalItem.Header = _localization.GetString("Tray.GlobalMode");
        if (_outboundDirectItem is not null) _outboundDirectItem.Header = _localization.GetString("Tray.DirectMode");
        if (_systemProxyItem is not null) _systemProxyItem.Header = _localization.GetString("Tray.SystemProxy");
        if (_tunItem is not null) _tunItem.Header = _localization.GetString("Tray.VirtualNic");
        if (_restartCoreItem is not null) _restartCoreItem.Header = _localization.GetString("Tray.RestartCore");
        if (_exitItem is not null) _exitItem.Header = _localization.GetString("Tray.Exit");
    }

    private void UpdateState()
    {
        if (_homePage is null)
        {
            return;
        }

        var state = new TrayMenuState(
            _homePage.IsRuleOutboundSelected,
            _homePage.IsGlobalOutboundSelected,
            _homePage.IsDirectOutboundSelected,
            _homePage.IsSystemProxyEnabled,
            _homePage.IsTunEnabled,
            _homePage.IsTunToggleEnabled,
            _homePage.CanRestartCore,
            _homePage.IsCoreRunning,
            _homePage.IsCoreInteractive,
            !(_window?.IsVisible == true && _window.WindowState != WindowState.Minimized),
            ResolveTrayIconState());
        // 托盘菜单仅接受实际变化，避免运行轮询重复刷新。
        if (state == _appliedMenuState)
        {
            return;
        }
        _appliedMenuState = state;

        // 出站模式依赖运行时 API，重启或更新期间要禁用。
        if (_outboundRuleItem is not null)
        {
            _outboundRuleItem.IsChecked = state.IsRuleOutboundSelected;
            _outboundRuleItem.IsEnabled = state.IsCoreInteractive;
        }
        if (_outboundGlobalItem is not null)
        {
            _outboundGlobalItem.IsChecked = state.IsGlobalOutboundSelected;
            _outboundGlobalItem.IsEnabled = state.IsCoreInteractive;
        }
        if (_outboundDirectItem is not null)
        {
            _outboundDirectItem.IsChecked = state.IsDirectOutboundSelected;
            _outboundDirectItem.IsEnabled = state.IsCoreInteractive;
        }

        // 启用需要稳定核心；更新失败后仍必须允许关闭。
        if (_systemProxyItem is not null)
        {
            _systemProxyItem.IsChecked = state.IsSystemProxyEnabled;
        }
        // 切换 TUN 需要权限和稳定核心。
        if (_tunItem is not null)
        {
            _tunItem.IsChecked = state.IsTunEnabled;
            _tunItem.IsEnabled = state.IsTunToggleEnabled;
        }
        if (_restartCoreItem is not null) _restartCoreItem.IsEnabled = state.CanRestartCore;
        if (_copyItem is not null) _copyItem.IsEnabled = state.IsCoreRunning;
        if (_showItem is not null)
        {
            _showItem.IsEnabled = state.IsShowEnabled;
        }

        UpdateIconState(state.IconState);
    }

    private void ScheduleStateUpdate()
    {
        // 同一批属性通知只刷新一次菜单。
        if (_isStateUpdateScheduled)
        {
            return;
        }

        _isStateUpdateScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isStateUpdateScheduled = false;
            UpdateState();
        }, DispatcherPriority.Background);
    }

    private void UpdateIconState(TrayIconState state)
    {
        if (_trayIcon is null || _iconState == state)
        {
            return;
        }

        _iconState = state;
        _icon = TrayIconFactory.Create(state);
        _trayIcon.Icon = _icon;
    }

    private TrayIconState ResolveTrayIconState()
    {
        if (_homePage?.IsCoreRunning != true)
        {
            return TrayIconState.Disabled;
        }

        return (_homePage.IsSystemProxyEnabled, _homePage.IsTunEnabled) switch
        {
            (true, true) => TrayIconState.ProxyTunEnabled,
            (true, false) => TrayIconState.ProxyEnabled,
            (false, true) => TrayIconState.TunEnabled,
            _ => TrayIconState.Disabled,
        };
    }
}
