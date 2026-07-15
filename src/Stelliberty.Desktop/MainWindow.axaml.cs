using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
#if DEBUG
using HotAvalonia;
#endif
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Settings;
using Stelliberty.Desktop.Controls;
using Stelliberty.Desktop.Services;
using Stelliberty.Desktop.Views;
using Stelliberty.Desktop.Views.Dialogs;
using Stelliberty.Presentation.ViewModels;
using AppNavigationPage = Stelliberty.Presentation.ViewModels.NavigationPage;

namespace Stelliberty.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly WindowAppearanceService _windowAppearanceService = new();
    private readonly WindowStateService _windowStateService;
    private readonly SystemAccentColorService _systemAccentColorService = new();
    private readonly Dictionary<AppNavigationPage, ContentControl> _pageHosts = new();
    private bool _pageHostsReady;
    private ContentControl? _visiblePageHost;
    private MainWindowViewModel? _attachedViewModel;
    private AccentColorPickerView? _activeAccentPicker;
    private bool _isShutdownRequested;
    private bool _hasOpened;
    private long _warmupVersion;
#if DEBUG
    private long _navigationDebugVersion;
    private long _hotReloadRecoveryVersion;
    private long _warmupStartedAt;
#endif

    private static readonly AppNavigationPage[] WarmupOrder =
    [
        AppNavigationPage.Proxy,
        AppNavigationPage.Connections,
        AppNavigationPage.Rules,
        AppNavigationPage.Overrides,
        AppNavigationPage.CoreLogs,
        AppNavigationPage.Home,
        AppNavigationPage.Subscriptions,
        AppNavigationPage.Settings,
    ];

    public MainWindow()
        : this(null, null)
    {
    }

    public MainWindow(IAppSettingsStore? settingsStore, AppSettings? settings)
    {
        _windowStateService = new WindowStateService(settingsStore, settings);
        InitializeComponent();
        ApplyPlatformWindowDecorations();
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnWindowPropertyChanged;
        Opened += OnOpened;
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        _windowStateService.Attach(this);
        UpdateWindowStateVisuals();
#if DEBUG
        AttachFpsCounter();
#endif
    }

    private void ApplyPlatformWindowDecorations()
    {
        if (OperatingSystem.IsMacOS())
        {
            // macOS 保留系统标题栏按钮，避免与自绘窗口控制重复。
            CaptionButtons.IsVisible = false;
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            WindowDecorations = Avalonia.Controls.WindowDecorations.BorderOnly;
        }
    }
#if DEBUG
    // 在标题栏按钮前插入 Debug 标识与 FPS 计数器。
    private void AttachFpsCounter()
    {
        var devBadge = new Border
        {
            Classes = { "debug-badge" },
            Child = new TextBlock
            {
                Classes = { "debug-badge-text" },
                Text = "Dev",
            }
        };
        AutomationProperties.SetAutomationId(devBadge, "TitleBar.DevBadge");
        TitleBarLayout.Children.Insert(0, devBadge);

        var fps = new FpsCounter
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
        };
        AutomationProperties.SetAutomationId(fps, "TitleBar.FpsCounter");
        fps.Bind(FpsCounter.ForegroundProperty, this.GetResourceObservable("AppTextSecondaryBrush"));
        CaptionButtons.Children.Insert(0, fps);
    }
#endif

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        DetachViewModel();

        if (DataContext is MainWindowViewModel viewModel)
        {
            _attachedViewModel = viewModel;
            _attachedViewModel.Theme.CustomAccentRequested += OnCustomAccentRequested;
            _attachedViewModel.PropertyChanged += OnAttachedViewModelPropertyChanged;
            _windowAppearanceService.Attach(this, viewModel.Theme);
            if (_hasOpened)
            {
                _systemAccentColorService.Attach(this, viewModel.Theme);
            }
            InitializePageHosts();
        }
    }

    private void DetachViewModel()
    {
        if (_attachedViewModel is null)
        {
            return;
        }

        _attachedViewModel.Theme.CustomAccentRequested -= OnCustomAccentRequested;
        _attachedViewModel.PropertyChanged -= OnAttachedViewModelPropertyChanged;
        _windowAppearanceService.Dispose();
        _systemAccentColorService.Dispose();
        _attachedViewModel = null;
    }

    private void OnOpened(object? sender, EventArgs args)
    {
#if DEBUG
        var openedAt = Stopwatch.GetTimestamp();
        AppLogger.Info($"[StartupTrace] Main window opened pendingWarmup={HasPendingWarmup()}");
        Dispatcher.UIThread.Post(
            () => AppLogger.Info($"[StartupTrace] Main window first background turn elapsed={Stopwatch.GetElapsedTime(openedAt).TotalMilliseconds:0.0}ms"),
            DispatcherPriority.Background);
#endif
        _hasOpened = true;
        if (DataContext is MainWindowViewModel viewModel)
        {
            _systemAccentColorService.Attach(this, viewModel.Theme);
            StartWarmup();
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled
            || args.Key != Key.Escape
            || args.KeyModifiers != KeyModifiers.None
            || DialogHost.IsOpen
            || DataContext is not MainWindowViewModel viewModel
            || viewModel.CurrentPage != AppNavigationPage.Settings
            || !viewModel.Settings.IsBackVisible)
        {
            return;
        }

        if (viewModel.Settings.BackCommand.CanExecute(null))
        {
            viewModel.Settings.BackCommand.Execute(null);
            args.Handled = true;
        }
    }

    private void OnAttachedViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MainWindowViewModel.CurrentPage)
            || sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        AnimatePageTransition(viewModel.CurrentPage);
#if DEBUG
        ScheduleNavigationDebugLog(viewModel.CurrentPage);
#endif
    }

    // 将页面映射到持久宿主；XAML 按导航顺序堆叠宿主。
    private void InitializePageHosts()
    {
        if (_pageHostsReady || _attachedViewModel is null)
        {
            return;
        }

        _pageHosts[AppNavigationPage.Home] = HomePageHost;
        _pageHosts[AppNavigationPage.Proxy] = ProxyPageHost;
        _pageHosts[AppNavigationPage.Connections] = ConnectionsPageHost;
        _pageHosts[AppNavigationPage.CoreLogs] = CoreLogsPageHost;
        _pageHosts[AppNavigationPage.Rules] = RulesPageHost;
        _pageHosts[AppNavigationPage.Subscriptions] = SubscriptionsPageHost;
        _pageHosts[AppNavigationPage.Overrides] = OverridesPageHost;
        _pageHosts[AppNavigationPage.Settings] = SettingsPageHost;
        _pageHostsReady = true;

        ShowInitialPage(_attachedViewModel.CurrentPage);
    }

    private void StartWarmup()
    {
#if DEBUG
        _warmupStartedAt = Stopwatch.GetTimestamp();
        AppLogger.Info($"[StartupTrace] Window warmup started pages={WarmupOrder.Length}");
#endif
        StartWarmup(++_warmupVersion);
    }

    private void StartWarmup(long version)
    {
        if (version == _warmupVersion && _pageHostsReady && HasPendingWarmup())
        {
            WarmupFrom(0, version);
        }
    }

    private void WarmupFrom(int index, long version)
    {
        if (version != _warmupVersion || index >= WarmupOrder.Length)
        {
#if DEBUG
            if (version == _warmupVersion && index >= WarmupOrder.Length)
            {
                AppLogger.Info($"[StartupTrace] Window warmup completed elapsed={Stopwatch.GetElapsedTime(_warmupStartedAt).TotalMilliseconds:0.0}ms");
            }
#endif
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (version != _warmupVersion)
                {
                    return;
                }

                var page = WarmupOrder[index];
#if DEBUG
                var pageStartedAt = Stopwatch.GetTimestamp();
#endif
                EnsurePageLoaded(page);
                if (_pageHosts.TryGetValue(page, out var host))
                {
                    host.UpdateLayout();
                }
#if DEBUG
                AppLogger.Info($"[StartupTrace] Window warmup page={page} elapsed={Stopwatch.GetElapsedTime(pageStartedAt).TotalMilliseconds:0.0}ms total={Stopwatch.GetElapsedTime(_warmupStartedAt).TotalMilliseconds:0.0}ms");
#endif

                WarmupFrom(index + 1, version);
            },
            DispatcherPriority.Background);
    }

    private bool HasPendingWarmup()
        => _pageHosts.Values.Any(host => host.Content is null);

    private void EnsurePageLoaded(AppNavigationPage page)
    {
        if (!_pageHostsReady
            || !_pageHosts.TryGetValue(page, out var host)
            || host.Content is not null
            || !TryGetPageConverter(out var converter))
        {
            return;
        }

        converter.Preload(page);
        host.Content = converter.TryGetView(page);
    }

    // 首页直接显示无动画；其余宿主复位到隐藏的下浮起始态。
    private void ShowInitialPage(AppNavigationPage page)
    {
        if (!_pageHosts.TryGetValue(page, out var host))
        {
            return;
        }

        foreach (var other in _pageHosts.Values)
        {
            other.Transitions = null;
            other.ZIndex = 0;
            other.IsHitTestVisible = false;
            other.Opacity = 0;
            other.RenderTransform = PageTransition.EnterFromTransform;
        }

        EnsurePageLoaded(page);
        host.ZIndex = 1;
        host.IsHitTestVisible = true;
        host.Opacity = 1;
        host.RenderTransform = PageTransition.RestTransform;
        _visiblePageHost = host;
    }

    // 旧页快速淡出、新页淡入上浮；起始态须先无动画落位，再注入过渡才生效。
    private void AnimatePageTransition(AppNavigationPage page)
    {
        if (!_pageHostsReady || !_pageHosts.TryGetValue(page, out var nextHost))
        {
            return;
        }

        EnsurePageLoaded(page);
        if (ReferenceEquals(nextHost, _visiblePageHost))
        {
            return;
        }

        var previousHost = _visiblePageHost;
        _visiblePageHost = nextHost;

        if (previousHost is not null)
        {
            previousHost.Transitions = PageTransition.CreateLeaveTransitions();
            previousHost.ZIndex = 0;
            previousHost.IsHitTestVisible = false;
            previousHost.Opacity = 0;
        }

        nextHost.Transitions = null;
        nextHost.ZIndex = 1;
        nextHost.IsHitTestVisible = true;
        nextHost.Opacity = 0;
        nextHost.RenderTransform = PageTransition.EnterFromTransform;

        Dispatcher.UIThread.Post(
            () =>
            {
                // 期间又切走则放弃，避免覆盖新目标的进入动画。
                if (!ReferenceEquals(_visiblePageHost, nextHost))
                {
                    return;
                }

                nextHost.Transitions = PageTransition.CreateEnterTransitions();
                nextHost.Opacity = 1;
                nextHost.RenderTransform = PageTransition.RestTransform;
            },
            DispatcherPriority.Render);
    }

    private bool TryGetPageConverter(out PageToViewConverter converter)
    {
        if (TryGetResource("PageToView", ActualThemeVariant, out var resource)
            && resource is PageToViewConverter pageToView)
        {
            converter = pageToView;
            return true;
        }

        converter = null!;
        return false;
    }

#if DEBUG

    [AvaloniaHotReload]
    private void OnHotReloaded()
    {
        ScheduleHotReloadRecovery();
    }

    private void ScheduleHotReloadRecovery()
    {
        var version = System.Threading.Interlocked.Increment(ref _hotReloadRecoveryVersion);
        Dispatcher.UIThread.Post(
            () => Dispatcher.UIThread.Post(
                () => ApplyHotReloadRecovery(version),
                DispatcherPriority.Background),
            DispatcherPriority.Render);
    }

    private void ApplyHotReloadRecovery(long version)
    {
        if (version != System.Threading.Volatile.Read(ref _hotReloadRecoveryVersion))
        {
            return;
        }

        try
        {
            _windowAppearanceService.Reapply();
            _systemAccentColorService.Reapply();
            RebuildNavigationViewsAfterHotReload();
            AppLogger.Info("Hot reload recovered theme, accent color, and page view cache");
        }
        catch (Exception exception)
        {
            AppLogger.Error(exception, "Hot reload recovery failed");
        }
    }

    private void RebuildNavigationViewsAfterHotReload()
    {
        if (_attachedViewModel is null || !TryGetPageConverter(out var converter))
        {
            return;
        }

        // 热重载会替换视觉树和资源。
        // 宿主与缓存视图必须重新绑定，避免继续引用旧资源。
        ClearPageHostContents();
        converter.ClearCache();
        _pageHostsReady = false;
        _pageHosts.Clear();
        _visiblePageHost = null;
        InitializePageHosts();
        StartWarmup();
    }

    private void ScheduleNavigationDebugLog(AppNavigationPage page)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var version = ++_navigationDebugVersion;
        Dispatcher.UIThread.Post(
            () => Dispatcher.UIThread.Post(
                () => LogNavigationDebug(page, startedAt, version),
                DispatcherPriority.Background),
            DispatcherPriority.Render);
    }

    private void LogNavigationDebug(AppNavigationPage page, long startedAt, long version)
    {
        if (version != _navigationDebugVersion
            || _attachedViewModel is null
            || _attachedViewModel.CurrentPage != page)
        {
            return;
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var controls = CountPageControls();
        AppLogger.Info($"Navigation complete: page={FormatPageDebugName(page)} elapsed={elapsedMs:0.0}ms controls={controls.Total} visible={controls.Visible} automation={controls.Automation} {BuildPageDebugState(page, _attachedViewModel)}");
    }

    private (int Total, int Visible, int Automation) CountPageControls()
    {
        if (_attachedViewModel is not null
            && GetNavigationPageView(_attachedViewModel.CurrentPage) is Control currentPage)
        {
            var controls = currentPage.GetVisualDescendants().OfType<Control>().Append(currentPage).ToList();
            return (
                controls.Count,
                controls.Count(IsControlEffectivelyVisible),
                controls.Count(control => !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control))));
        }

        return (0, 0, 0);
    }

    private Control? GetNavigationPageView(AppNavigationPage page)
        => _pageHosts.TryGetValue(page, out var host) ? host.Content as Control : null;

    private static string BuildPageDebugState(AppNavigationPage page, MainWindowViewModel viewModel)
    {
        return page switch
        {
            AppNavigationPage.Proxy => $"groups={viewModel.ProxyPage.VisibleGroups.Count} nodes={viewModel.ProxyPage.VisibleNodeRows.Count}",
            AppNavigationPage.Connections => $"connections={viewModel.ConnectionPage.TotalConnectionCount} filtered={viewModel.ConnectionPage.FilteredConnectionCount}",
            AppNavigationPage.CoreLogs => $"core_logs={viewModel.CoreLogPage.TotalLogCount} filtered={viewModel.CoreLogPage.FilteredLogCount}",
            AppNavigationPage.Rules => $"rules={viewModel.RulePage.Rules.Count} filtered={viewModel.RulePage.FilteredRules.Count}",
            AppNavigationPage.Subscriptions => $"subscriptions={viewModel.SubscriptionPage.TotalSubscriptionCount} current={viewModel.SubscriptionPage.CurrentSubscriptionId ?? string.Empty}",
            AppNavigationPage.Overrides => $"overrides={viewModel.OverridePage.Overrides.Count}",
            _ => string.Empty
        };
    }

    private static string FormatPageDebugName(AppNavigationPage page) => page.ToString();

    private static bool IsControlEffectivelyVisible(Control control)
    {
        for (var current = control; current is not null; current = current.GetVisualParent<Control>())
        {
            if (!current.IsVisible)
            {
                return false;
            }
        }

        return true;
    }
#endif

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (args.ClickCount == 2)
        {
            ToggleWindowMaximized();
            return;
        }

        BeginMoveDrag(args);
    }

    private void OnMinimizeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        ToggleWindowMaximized();
    }

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        Close();
    }

    private void ToggleWindowMaximized()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateWindowStateVisuals();
    }

    private void UpdateWindowStateVisuals()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeIcon.IsVisible = !isMaximized;
        RestoreIcon.IsVisible = isMaximized;
    }

    public void RequestShutdown()
    {
        _isShutdownRequested = true;
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs args)
    {
        _windowStateService.SaveNow();
        if (!_isShutdownRequested && DataContext is MainWindowViewModel { AppBehavior.IsMinimizeToTrayEnabled: true })
        {
            args.Cancel = true;
            ClearTitleBarHoverState();
            // 先取消关闭按钮红色效果，等界面更新两次再隐藏，避免恢复窗口时闪红。
            RequestAnimationFrame(OnTitleBarStateClearedFrame);
            return;
        }

        base.OnClosing(args);
    }

    protected override void OnClosed(EventArgs e)
    {
        CloseAccentPicker();
        ClearPageHostContents();
        if (TryGetPageConverter(out var converter))
        {
            converter.ClearCache();
        }

        DetachViewModel();
        _windowStateService.Dispose();
        DataContextChanged -= OnDataContextChanged;
        PropertyChanged -= OnWindowPropertyChanged;
        Opened -= OnOpened;
        base.OnClosed(e);
    }

    private void ClearPageHostContents()
    {
        foreach (var host in _pageHosts.Values)
        {
            host.Content = null;
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == WindowStateProperty)
        {
            UpdateWindowStateVisuals();
        }
    }

    private void ClearTitleBarHoverState()
    {
        foreach (var button in CaptionButtons.Children.OfType<Button>())
        {
            var pseudoClasses = (IPseudoClasses)button.Classes;
            pseudoClasses.Set(":pointerover", false);
        }
    }

    private void OnTitleBarStateClearedFrame(TimeSpan _)
    {
        RequestAnimationFrame(HideToTrayOnSecondFrame);
    }

    private void HideToTrayOnSecondFrame(TimeSpan _)
    {
        Hide();
    }

    private void OnCustomAccentRequested(object? sender, EventArgs args)
    {
        if (_attachedViewModel is null)
        {
            return;
        }

        _activeAccentPicker = new AccentColorPickerView
        {
            DataContext = _attachedViewModel,
            InitialColor = Color.Parse(_attachedViewModel.Theme.CustomAccentColor)
        };
        _activeAccentPicker.Confirmed += OnAccentPickerConfirmed;
        _activeAccentPicker.Cancelled += OnAccentPickerCancelled;

        DialogHost.Show(new DialogPanel { DialogContent = _activeAccentPicker });
    }

    private void OnAccentPickerConfirmed(object? sender, EventArgs args)
    {
        if (DataContext is MainWindowViewModel viewModel && _activeAccentPicker is not null)
        {
            var color = _activeAccentPicker.SelectedColor;
            viewModel.Theme.ConfirmCustomAccentColor($"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }

        CloseAccentPicker();
    }

    private void OnAccentPickerCancelled(object? sender, EventArgs args)
    {

        CloseAccentPicker();
    }

    private void CloseAccentPicker()
    {
        if (_activeAccentPicker is not null)
        {
            _activeAccentPicker.Confirmed -= OnAccentPickerConfirmed;
            _activeAccentPicker.Cancelled -= OnAccentPickerCancelled;
            _activeAccentPicker = null;
        }

        DialogHost.Close();
    }
}
