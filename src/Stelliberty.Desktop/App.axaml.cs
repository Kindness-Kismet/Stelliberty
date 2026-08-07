using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Stelliberty.Application.Diagnostics;
using Stelliberty.Application.Localization;
using Stelliberty.Application.Overrides;
using Stelliberty.Domain.Overrides;
using Stelliberty.Application.Platform;
using Stelliberty.Application.Proxies;
using Stelliberty.Domain.Proxies;
using Stelliberty.Application.Rules;
using Stelliberty.Domain.Rules;
using Stelliberty.Application.Runtime;
using Stelliberty.Application.Settings;
using Stelliberty.Application.Updates;
using Stelliberty.Desktop.Services;
using Stelliberty.Infrastructure.Tray;
using Stelliberty.Infrastructure.Core;
using Stelliberty.Infrastructure.DataManagement;
using Stelliberty.Infrastructure.Diagnostics;
using Stelliberty.Infrastructure.Localization;
using Stelliberty.Infrastructure.Overrides;
using Stelliberty.Infrastructure.Platform;
using Stelliberty.Infrastructure.Proxies;
using Stelliberty.Infrastructure.Rules;
using Stelliberty.Infrastructure.Runtime;
using Stelliberty.Infrastructure.Settings;
using Stelliberty.Infrastructure.Subscriptions;
using Stelliberty.Infrastructure.Updates;
using Stelliberty.Application.Subscriptions;
using Stelliberty.Domain.Subscriptions;
using Stelliberty.Desktop.Controls;
using Stelliberty.Desktop.Debug;
using Stelliberty.Desktop.Localization;
using Stelliberty.Native;
using Stelliberty.Native.Hub;
using Stelliberty.Presentation.ViewModels;

namespace Stelliberty.Desktop;

public sealed partial class App : Avalonia.Application
{
    private DesktopTraySession? _traySession;
    private MainWindow? _mainWindow;
    private SessionEndCleanupService? _sessionEndCleanup;
    private DispatcherTimer? _appUpdateAutoCheckTimer;
    private DispatcherTimer? _subscriptionAutoDelayTimer;
    private DispatcherTimer? _subscriptionAutoUpdateTimer;
    private DispatcherTimer? _homeRuntimeTimer;
    private DispatcherTimer? _webDavBackupTimer;
    private int _isOsShutdownRequested;
    private bool _isServiceModeCoreHostActive;
    // 主动退出最多等待服务核心 5 秒，普通核心在 Rust 侧使用相同总预算。
    private static readonly TimeSpan CoreShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InitialServiceModeTunWaitTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan InitialServiceModeStatusPollInterval = TimeSpan.FromMilliseconds(250);

    public override void Initialize()
    {
        AppLogger.Debug("Loading Avalonia XAML");
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _traySession = DesktopLaunchContext.TraySession
                ?? throw new InvalidOperationException("Desktop UI tray session is unavailable.");
            _traySession.ActivationRequested += OnTrayActivationRequested;
            _traySession.ToggleRequested += OnTrayToggleRequested;
            _traySession.Disconnected += OnTrayDisconnected;
#if DEBUG
            var startupStartedAt = Stopwatch.GetTimestamp();
            LogStartupTrace("Framework initialization started", startupStartedAt);
#endif
            AppLogger.Info("Creating main window");
            var platformDirectories = new DesktopPlatformDirectories();
            // 代理组图标磁盘缓存，进程重启后免重下。
            RemoteImageCache.Configure(Path.Combine(platformDirectories.AppDataDirectory, "icon-cache"));
            var settingsStore = new JsonAppSettingsStore(platformDirectories);
            var settings = settingsStore.Load();
#if DEBUG
            LogStartupTrace($"Settings loaded silent={settings.IsSilentStartEnabled} tun={settings.IsTunEnabled}", startupStartedAt);
#endif
            var updateChecker = new GitHubAppUpdateChecker(
                () => settingsStore.Load().AppUpdateChannel,
                () =>
                {
                    var currentSettings = settingsStore.Load();
                    return (currentSettings.ProxyHost, currentSettings.MixedPort);
                });
            var systemProxyPlatform = CurrentSystemProxyPlatform();
            IUwpLoopbackService uwpLoopbackService = OperatingSystem.IsWindows()
                ? new WindowsUwpLoopbackService()
                : new UnsupportedUwpLoopbackService();
            var systemProxyHostDetector = new NetworkInterfaceSystemProxyHostDetector();
            LocalSystemProxyController? localSystemProxyController = UsesTrayRuntime
                ? null
                : new LocalSystemProxyController(SystemProxyServiceFactory.Create(
                    systemProxyPlatform,
                    platformDirectories.AppDataDirectory));
            ISystemProxyController systemProxyService = (ISystemProxyController?)localSystemProxyController
                ?? new TraySystemProxyController();
            IServiceModeManager serviceModeManager = UsesTrayRuntime
                ? new TrayServiceModeManager()
                : new ServiceModeManager(new ServiceModePaths(
                    DesktopApplicationLayout.ServiceDirectory,
                    DesktopApplicationLayout.ServiceCommandBinaryPath,
                    DesktopApplicationLayout.ServiceInstalledBinaryPath));
            var coreProcessCleaner = new CoreProcessCleaner(DesktopApplicationLayout.ServiceDirectory);
            var networkConnectionProbe = new SystemNetworkConnectionProbe();
            var processPrivilegeProbe = new SystemProcessPrivilegeProbe();
            IAppBehaviorService appBehaviorService = CreateAppBehaviorService();
            MainWindowViewModel? hotkeyViewModel = null;
            Action<GlobalHotkeyAction> hotkeyActivated = action =>
            {
                switch (action)
                {
                    case GlobalHotkeyAction.ToggleWindow:
                        Dispatcher.UIThread.Post(ToggleMainWindow);
                        break;
                    case GlobalHotkeyAction.ToggleSystemProxy:
                        hotkeyViewModel?.HomePage.ToggleSystemProxyFromHotkey();
                        break;
                    case GlobalHotkeyAction.ToggleTun:
                        hotkeyViewModel?.HomePage.ToggleTunFromHotkey();
                        break;
                }
            };
            IGlobalHotkeyService globalHotkeyService = UsesTrayRuntime
                ? new TrayGlobalHotkeyService()
                : GlobalHotkeyServiceFactory.Create(hotkeyActivated);
            var initialLanguage = AppLanguageParser.Parse(settings.Language);
            var localization = new JsonLocalizationService(initialLanguage);
            LocalizationManager.Initialize(localization);
            var subscriptionStore = new FileSubscriptionStore(platformDirectories.AppDataDirectory);
            var subscriptionSelectionStore = new FileSubscriptionSelectionStore(platformDirectories.AppDataDirectory);
            var proxySelectionStore = new FileProxySelectionStore(platformDirectories.AppDataDirectory);
            var overrideStore = new FileOverrideStore(platformDirectories.AppDataDirectory);
#if DEBUG
            IRemoteOverrideDownloader remoteOverrideDownloader = new RemoteOverrideDownloader();
#else
            IRemoteOverrideDownloader remoteOverrideDownloader = new HttpRemoteOverrideDownloader();
#endif
            var overrideImporter = new OverrideImporter(overrideStore, remoteOverrideDownloader);
            var overrideUpdater = new OverrideUpdater(overrideStore, remoteOverrideDownloader);
            var overrideDeleter = new OverrideDeleter(overrideStore, subscriptionStore);
            var overrideSelectionUpdater = new SubscriptionOverrideSelectionUpdater(subscriptionStore, overrideStore);
            var localSubscriptionImporter = new LocalSubscriptionFileImporter(
                new LocalSubscriptionImporter(subscriptionStore),
                new FileLocalSubscriptionFileReader());
#if DEBUG
            IRemoteSubscriptionDownloader remoteSubscriptionDownloader = new RemoteSubscriptionDownloader(() => (settings.ProxyHost, settings.MixedPort));
#else
            IRemoteSubscriptionDownloader remoteSubscriptionDownloader = new HttpRemoteSubscriptionDownloader(() => (settings.ProxyHost, settings.MixedPort));
#endif
            var subscriptionContentDecryptor = new HubSubscriptionContentDecryptor();
            var remoteSubscriptionImporter = new RemoteSubscriptionImporter(
                subscriptionStore,
                remoteSubscriptionDownloader,
                contentDecryptor: subscriptionContentDecryptor);
            var subscriptionUpdater = new SubscriptionUpdater(
                subscriptionStore,
                remoteSubscriptionDownloader,
                contentDecryptor: subscriptionContentDecryptor);
            var runtimeStore = new FileRuntimeConfigStore(platformDirectories.RuntimeDirectory);
            var selectedSubscriptionRuntimeGenerator = new SelectedSubscriptionRuntimeGenerator(
                subscriptionStore,
                subscriptionSelectionStore,
                new RuntimeConfigGenerator(new HubOverrideEngine()),
                overrideStore,
                runtimeStore);
            var subscriptionDeleter = new SubscriptionDeleter(subscriptionStore, subscriptionSelectionStore, runtimeStore);
            // Provider 同步和状态读取始终走核心管道，保持 Debug 和 Release 路径一致。
            var coreProviderClient = new PipeCoreProviderClient(TrayCoreEndpoints.Core);
            var providerCatalogLoader = new SelectedSubscriptionProviderCatalogLoader(
                subscriptionStore,
                subscriptionSelectionStore,
                new SubscriptionProviderParser(),
                coreProviderClient,
                coreProviderClient);
            ISubscriptionProviderUploader subscriptionProviderUploader = new FileSubscriptionProviderUploader(platformDirectories.CoreDirectory);
            ISubscriptionFileOpener subscriptionFileOpener = new DesktopSubscriptionFileOpener(subscriptionStore.GetContentPath);
            IOverrideFileOpener overrideFileOpener = new DesktopOverrideFileOpener(overrideStore.GetContentPath);
            var clipboardWriter = new DesktopClipboardWriter(desktop);
            var chainProxyContextLoader = new SubscriptionChainProxyContextLoader(subscriptionStore, new HubOverrideEngine(), overrideStore);
            var subscriptionPage = new SubscriptionPageViewModel(
                subscriptionDeleter,
                localSubscriptionImporter,
                remoteSubscriptionImporter,
                subscriptionUpdater,
                subscriptionStore,
                overrideStore,
                overrideSelectionUpdater,
                clipboardWriter,
                subscriptionFileOpener,
                subscriptionProviderUploader,
                providerCatalogLoader,
                subscriptionSelectionStore,
                runtimeStore,
                localization,
                chainProxyContextLoader.Load);
            var overridePage = new OverridePageViewModel(
                overrideDeleter,
                overrideStore,
                overrideImporter,
                overrideUpdater,
                new FileLocalOverrideFileReader(),
                overrideFileOpener,
                localization);
#if DEBUG
            var pipeProxyCoreClient = new PipeCoreProxyClient(TrayCoreEndpoints.Core);
            IProxyCoreClient directProxyCoreClient = new ProxyCoreClient(pipeProxyCoreClient);
            IProxyCoreClient proxyCoreClient = UsesTrayRuntime
                ? new TrayRuntimeProxyCoreClient(directProxyCoreClient)
                : directProxyCoreClient;
            IProxyDelayTester proxyDelayTester = new PipeCoreProxyDelayTester(
                TrayCoreEndpoints.Core,
                () => settings.DelayTestUrl,
                5000);
#else

            IProxyCoreClient directProxyCoreClient = new PipeCoreProxyClient(TrayCoreEndpoints.Core);
            IProxyCoreClient proxyCoreClient = UsesTrayRuntime
                ? new TrayRuntimeProxyCoreClient(directProxyCoreClient)
                : directProxyCoreClient;
            IProxyDelayTester proxyDelayTester = new PipeCoreProxyDelayTester(
                TrayCoreEndpoints.Core,
                () => settings.DelayTestUrl,
                5000);
#endif

            var initialServiceModeStatus = UsesTrayRuntime
                ? ServiceModeStatus.Unavailable(string.Empty)
                : GetInitialServiceModeStatus(serviceModeManager, settings.IsTunEnabled);
#if DEBUG
            LogStartupTrace($"Initial service status ready state={initialServiceModeStatus.State}", startupStartedAt);
#endif
            var coreManager = new SwitchableCoreManager(
                UsesTrayRuntime
                    ? new TrayCoreManager()
                    : CreateCoreManager(initialServiceModeStatus, serviceModeManager));
            ServiceModeSessionSwitcher? serviceModeSessionSwitcher = null;
            if (!UsesTrayRuntime)
            {
                serviceModeSessionSwitcher = new ServiceModeSessionSwitcher(
                    serviceModeManager,
                    coreManager,
                    status => CreateCoreManager(status, serviceModeManager),
                    CreateNormalCoreManager,
                    async token => ToCoreHostResult(await StopNormalCoreAsync(token)),
                    async token => ToCoreHostResult(await ResumeNormalCoreAsync(token)),
                    async (status, token) => ToCoreHostResult(
                        await StartCoreHostAsync(status, serviceModeManager, coreProcessCleaner, token)),
                    isActive => _isServiceModeCoreHostActive = isActive,
                    isServiceModeActive: initialServiceModeStatus.IsRunning);
            }
            else
            {
                _isServiceModeCoreHostActive = initialServiceModeStatus.IsRunning;
            }
            var connectionPage = new ConnectionPageViewModel(proxyCoreClient, localization: localization);
            var proxyConfigSource = new FileRuntimeProxyConfigSource(platformDirectories.RuntimeDirectory, subscriptionSelectionStore);
            var proxyConfigParser = new ProxyConfigParser();
            var proxyConfigLoader = new ProxyConfigLoader(
                proxyConfigSource,
                proxyConfigParser);
            var fileRuntimeProxyConfigProvider = new FileRuntimeProxyConfigProvider(proxyConfigLoader);
            var proxySelectionSyncState = new ProxySelectionSyncState();
            var mihomoApiProxyConfigProvider = new MihomoApiProxyConfigProvider(
                proxyCoreClient,
                new FileRuntimeProxyGroupIconProvider(proxyConfigSource, proxyConfigParser));
            var primaryProxyConfigProvider = new StoredProxySelectionConfigProvider(
                mihomoApiProxyConfigProvider,
                proxySelectionStore,
                subscriptionSelectionStore,
                proxySelectionSyncState,
                importCoreSelections: true,
                pruneInvalidSelections: false);
            var fallbackProxyConfigProvider = new StoredProxySelectionConfigProvider(
                fileRuntimeProxyConfigProvider,
                proxySelectionStore,
                subscriptionSelectionStore,
                pruneInvalidSelections: false);
            var proxySelectionRestorer = new ProxySelectionRestorer(
                coreClient: proxyCoreClient,
                coreConfigProvider: mihomoApiProxyConfigProvider,
                selectedRuntimeConfigProvider: fileRuntimeProxyConfigProvider,
                selectionProvider: primaryProxyConfigProvider,
                syncState: proxySelectionSyncState,
                subscriptionSelectionStore: subscriptionSelectionStore);
            var selectionRestoringCoreManager = new ProxySelectionRestoringCoreManager(
                coreManager,
                proxySelectionRestorer);
            var coreUpdater = new MihomoCoreUpdater(
                DesktopApplicationLayout.CoreBinaryPath,
                selectionRestoringCoreManager);
            var proxyPageLayout = Enum.TryParse<ProxyPageLayout>(settings.ProxyPageLayout, ignoreCase: true, out var parsedProxyLayout)
                ? parsedProxyLayout
                : ProxyPageLayout.Horizontal;
            var proxyNodeSortMode = Enum.TryParse<ProxyNodeSortMode>(settings.ProxyNodeSortMode, ignoreCase: true, out var parsedProxyNodeSortMode)
                ? parsedProxyNodeSortMode
                : ProxyNodeSortMode.Default;
            var proxyPage = new ProxyPageViewModel(
                new ProxyDelayService(proxyDelayTester),
                proxyCoreClient,
                primaryProxyConfigProvider,
                fallbackProxyConfigProvider,
                localization,
                new ProxySelectionService(proxyCoreClient, proxySelectionStore, subscriptionSelectionStore),
                initialLayout: proxyPageLayout,
                persistLayout: layout =>
                {
                    // 复用共享设置实例，避免后续完整保存丢失这个偏好。
                    settings.ProxyPageLayout = layout.ToString();
                    settingsStore.Save(settings);
                },
                initialSortMode: proxyNodeSortMode,
                persistSortMode: sortMode =>
                {
                    settings.ProxyNodeSortMode = sortMode.ToString();
                    settingsStore.Save(settings);
                },
                isPresentationActive: false);
            var rulePage = new RulePageViewModel(new RuleListLoader(
                new FileRuntimeRuleConfigSource(platformDirectories.RuntimeDirectory, subscriptionSelectionStore),
                new RuleParser()),
                localization);
            var coreLogPage = new CoreLogPageViewModel(localization: localization);
            var dataBackupService = new FileDataBackupService(platformDirectories.AppDataDirectory);
            var webDavBackupStore = new WebDavBackupStore();
            var webDavDataBackupService = new WebDavDataBackupService(dataBackupService, webDavBackupStore);
            var viewModel = new MainWindowViewModel(
                settingsStore,
                localization,
                systemProxyService,
                appBehaviorService,
                globalHotkeyService,
                subscriptionPage,
                overridePage,
                proxyPage,
                connectionPage,
                coreLogPage,
                rulePage,
                dataManagementService: dataBackupService,
                webDavDataBackupService: webDavDataBackupService,
                updateChecker: updateChecker,
                uwpLoopbackService: uwpLoopbackService,
                systemProxyHostDetector: systemProxyHostDetector,
                serviceModeManager: serviceModeManager,
                isServiceModeCoreHostActive: () => _isServiceModeCoreHostActive,
                systemProxyRequestFactory: () => SystemProxyApplicationRequest.Build(settingsStore.Load(), systemProxyPlatform),
                runtimeFallbackGenerator: new SelectedRuntimeFallbackGenerator(
                    subscriptionStore,
                    overrideSelectionUpdater,
                    selectedSubscriptionRuntimeGenerator),
                runtimeStore: runtimeStore,
                coreManager: selectionRestoringCoreManager,
                initialSettings: settings,
                windowEffectCapability: new WindowEffectCapability(),
                networkConnectionProbe: networkConnectionProbe,
                homeProxyClient: proxyCoreClient,
                coreUpdater: coreUpdater,
                processPrivilegeProbe: processPrivilegeProbe,
                initialServiceModeStatus: initialServiceModeStatus,
                systemPlatform: systemProxyPlatform,
                clipboardWriter: clipboardWriter,
                serviceModeSessionActivator: serviceModeSessionSwitcher is null
                    ? null
                    : token => selectionRestoringCoreManager.RunCoreResetAsync(
                        "service-mode-activation",
                        serviceModeSessionSwitcher.ActivateAsync,
                        token),
                serviceModeSessionDeactivator: serviceModeSessionSwitcher is null
                    ? null
                    : token => selectionRestoringCoreManager.RunCoreResetAsync(
                        "service-mode-deactivation",
                        serviceModeSessionSwitcher.DeactivateAsync,
                        token),
                serviceModeCoreTransitionStarting: serviceModeSessionSwitcher is null
                    ? null
                    : () => selectionRestoringCoreManager.NotifyCoreResetStarting("service-mode-operation"),
                serviceModeCoreTransitionCompleted: serviceModeSessionSwitcher is null
                    ? null
                    : _ => selectionRestoringCoreManager.RestoreCurrentCoreSelectionsAsync(
                        "service-mode-operation-completion",
                        CancellationToken.None),
                serviceModeCoreHostManagedExternally: UsesTrayRuntime,
                tunAvailabilityManagedExternally: UsesTrayRuntime,
                appLogReader: new FileAppLogReader(DesktopApplicationLayout.RunningLogFilePath),
                appLogExporter: new FileAppLogExporter(DesktopApplicationLayout.RunningLogFilePath));
            hotkeyViewModel = viewModel;
#if DEBUG
            LogStartupTrace("Main view model created", startupStartedAt);
#endif
            var autoUpdateScheduler = new AppUpdateAutoCheckScheduler(updateChecker, settingsStore.Load, settingsStore.Save, () => DateTimeOffset.Now);
            var autoUpdateRunner = new AppUpdateAutoCheckRunner(autoUpdateScheduler, viewModel.Update.ApplyAutoCheckResult);
            var subscriptionAutoUpdate = new SubscriptionAutoUpdateCoordinator(
                new SubscriptionAutoUpdateRunner(subscriptionStore, new SubscriptionAutoUpdatePlanner(), subscriptionUpdater),
                subscriptionPage,
                () => DateTimeOffset.Now);
            var mainWindow = new MainWindow(settingsStore, settings)
            {
                DataContext = viewModel
            };
            _mainWindow = mainWindow;
            mainWindow.CanExitToBackground = true;
            if (_traySession.IsDisconnected)
            {
                mainWindow.RequestUiShutdown();
            }

            mainWindow.PrepareShutdownAsync = async () =>
            {
                await UnregisterTraySessionAsync();
                StopBackgroundServices();
                AppLogger.Info("Background schedulers stopped for shutdown");
                if (!mainWindow.ShouldShutdownTray)
                {
                    return;
                }

                if (localSystemProxyController is not null)
                {
                    await Task.Run(() => localSystemProxyController.Shutdown());
                }
                using var timeout = new CancellationTokenSource(CoreShutdownTimeout);
                if (serviceModeSessionSwitcher is not null)
                {
                    var serviceStopStartedAt = Stopwatch.GetTimestamp();
                    var result = await serviceModeSessionSwitcher.PrepareForShutdownAsync(timeout.Token);
                    if (!result.IsSuccess)
                    {
                        AppLogger.Warning($"Service-mode core stop failed: elapsed={Stopwatch.GetElapsedTime(serviceStopStartedAt).TotalMilliseconds:0}ms message={result.Message}");
                    }
                    else
                    {
                        AppLogger.Info($"Service-mode core stop completed: elapsed={Stopwatch.GetElapsedTime(serviceStopStartedAt).TotalMilliseconds:0}ms message={result.Message}");
                    }
                }

                if (UsesTrayRuntime)
                {
                    await ShutdownTrayAsync();
                }
                else
                {
                    await Task.Run(HubBootstrap.Shutdown);
                }
            };
            mainWindow.OsShutdownDetected = () => Interlocked.Exchange(ref _isOsShutdownRequested, 1);
#if DEBUG
            LogStartupTrace("Main window constructed and bound", startupStartedAt);
#endif

            desktop.MainWindow = mainWindow;

            desktop.ShutdownRequested += (_, _) =>
            {
                var isOsShutdown = Volatile.Read(ref _isOsShutdownRequested) != 0;
                var source = mainWindow.IsShutdownPreparing
                    ? mainWindow.ShouldShutdownTray ? "application" : "ui-session"
                    : isOsShutdown ? "os" : "external";
                AppLogger.Info($"Lifetime cleanup started: origin={source}");
                StopBackgroundServices();
                if (_traySession is not null)
                {
                    _traySession.ActivationRequested -= OnTrayActivationRequested;
                    _traySession.ToggleRequested -= OnTrayToggleRequested;
                    _traySession.Disconnected -= OnTrayDisconnected;
                }
                _traySession = null;
                _mainWindow = null;
                globalHotkeyService.Dispose();
                _sessionEndCleanup?.Dispose();
                _sessionEndCleanup = null;
                viewModel.Dispose();
                var switcherDisposed = serviceModeSessionSwitcher?.TryDisposeForShutdown() ?? true;
                var coreManagerDisposed = coreManager.TryDisposeForShutdown();
                AppLogger.Info($"Core ownership released for shutdown: switcher={switcherDisposed} manager={coreManagerDisposed}");
                DisposeOwnedServices(
                    selectionRestoringCoreManager,
                    proxyCoreClient,
                    proxyDelayTester,
                    coreProviderClient,
                    webDavBackupStore,
                    systemProxyService,
                    serviceModeManager);
                if (UsesTrayRuntime)
                {
                    AppLogger.Info("Desktop core client released; Tray retains runtime ownership");
                }
                else if (OperatingSystem.IsWindows())
                {
                    AppLogger.Info("Lifetime cleanup skipped synchronous hub wait; Job Object owns remaining normal core termination");
                }
                else
                {
                    HubBootstrap.Shutdown();
                }
                AppLogger.Info($"Lifetime cleanup completed: origin={source}");
            };
            if (localSystemProxyController is not null)
            {
                // 直接宿主仍需同步响应系统注销；Tray 模式由后台宿主负责。
                _sessionEndCleanup = new SessionEndCleanupService(
                    () => localSystemProxyController.Shutdown(),
                    isDetected => Interlocked.Exchange(ref _isOsShutdownRequested, isDetected ? 1 : 0));
                _sessionEndCleanup.Start();
            }
            if (!UsesTrayRuntime)
            {
                _ = RegisterGlobalHotkeysAsync(globalHotkeyService, settings);
            }
#if DEBUG
            LogStartupTrace(
                "Background tray session initialized",
                startupStartedAt);
#endif
#if DEBUG
            DebugCommands.Start(mainWindow);
#endif
            // 首屏出现后再启动重活，保持窗口启动响应。
            Dispatcher.UIThread.Post(
                () =>
                {
#if DEBUG
                    LogStartupTrace("Background startup dispatch entered", startupStartedAt);
#endif
                    _ = RunAppUpdateCheckAsync(() => autoUpdateRunner.RunStartupCheckAsync());
                    StartAppUpdateAutoCheckTimer(autoUpdateRunner);
                    StartSubscriptionAutoDelayTimer(viewModel);
                    StartHomeRuntimeTimer(viewModel);
                    StartWebDavBackupTimer(viewModel);
                    _ = InitializeSubscriptionServicesAsync(subscriptionPage, subscriptionAutoUpdate);
                    _ = overridePage.InitializeAsync();
                    _ = StartCoreServicesAsync(initialServiceModeStatus, serviceModeManager, coreProcessCleaner, coreManager, viewModel, proxyPage, rulePage, proxySelectionRestorer);
                },
                DispatcherPriority.Background);
#if DEBUG
            LogStartupTrace("Framework initialization completed", startupStartedAt);
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task UnregisterTraySessionAsync()
    {
        if (_traySession is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await _traySession.UnregisterAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            AppLogger.Warning($"Desktop UI session unregister failed: {exception.Message}");
        }
    }

    private void OnTrayActivationRequested(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(ShowMainWindow);

    private void OnTrayToggleRequested(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(ToggleMainWindow);

    private void OnTrayDisconnected(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(() => _mainWindow?.RequestUiShutdown());

    private void ShowMainWindow()
    {
        if (_mainWindow is not { } mainWindow)
        {
            return;
        }

        mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }
        mainWindow.Activate();
    }

    private void ToggleMainWindow()
    {
        if (_mainWindow is not { } mainWindow)
        {
            return;
        }

        if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized)
        {
            if (mainWindow.CanExitToBackground)
            {
                mainWindow.RequestUiShutdown();
            }
            else
            {
                mainWindow.RequestShutdown();
            }
            return;
        }

        ShowMainWindow();
    }

    private async Task ShutdownTrayAsync()
    {
        if (_traySession is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await _traySession.ShutdownTrayAsync(timeout.Token);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            AppLogger.Warning($"Tray shutdown request failed: {exception.Message}");
        }
    }

    private void StopBackgroundServices()
    {
        StopTimer(ref _appUpdateAutoCheckTimer);
        StopTimer(ref _subscriptionAutoDelayTimer);
        StopTimer(ref _subscriptionAutoUpdateTimer);
        StopTimer(ref _homeRuntimeTimer);
        StopTimer(ref _webDavBackupTimer);
    }

    private static void StopTimer(ref DispatcherTimer? timer)
    {
        if (timer is null)
        {
            return;
        }

        timer.Stop();
        timer = null;
    }

    private static void DisposeOwnedServices(params object?[] services)
    {
        foreach (var service in services)
        {
            if (service is not IDisposable disposable)
            {
                continue;
            }

            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Service dispose failed: {exception.Message}");
            }
        }
    }

    private async Task StartCoreServicesAsync(
        ServiceModeStatus initialServiceModeStatus,
        IServiceModeManager serviceModeManager,
        CoreProcessCleaner coreProcessCleaner,
        SwitchableCoreManager coreManager,
        MainWindowViewModel viewModel,
        ProxyPageViewModel proxyPage,
        RulePageViewModel rulePage,
        ProxySelectionRestorer proxySelectionRestorer)
    {
        try
        {
            var bootstrap = await StartCoreHostAsync(
                initialServiceModeStatus,
                serviceModeManager,
                coreProcessCleaner,
                CancellationToken.None);
            if (bootstrap.Ok)
            {
                await coreManager.EnsureReadyAsync(CancellationToken.None);
            }
            else
            {
                AppLogger.Warning($"Core host startup failed: {bootstrap.Message}");
                viewModel.ShowErrorToast(LocalizationManager.Translate("Common.Error.CoreStartupFailed"));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Core manager startup failed: {exception.Message}");
            viewModel.ShowErrorToast(LocalizationManager.Translate("Common.Error.CoreStartupFailed"));
        }

        try
        {
            await proxySelectionRestorer.RestoreCurrentSubscriptionAsync();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup proxy selection restore failed: {exception.Message}");
        }

        try
        {
            await proxyPage.RefreshProxiesAsync();
            if (viewModel.SubscriptionPage.CurrentSubscriptionId is { } subscriptionId)
            {
                proxyPage.BindLoadedConfigToSubscription(subscriptionId);
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup proxy list refresh failed: {exception.Message}");
        }

        RefreshRulesForStartup(rulePage);
    }

    internal static void RefreshRulesForStartup(RulePageViewModel rulePage)
    {
        try
        {
            rulePage.RefreshRulesCommand.Execute(null);
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"Startup rule list refresh failed: {exception.Message}");
        }
    }

    private async Task<BootstrapResult> StartCoreHostAsync(
        ServiceModeStatus initialServiceModeStatus,
        IServiceModeManager serviceModeManager,
        CoreProcessCleaner coreProcessCleaner,
        CancellationToken cancellationToken)
    {
        if (UsesTrayRuntime)
        {
            return await ResumeNormalCoreAsync(cancellationToken);
        }

        if (initialServiceModeStatus.IsRunning)
        {
            var serviceCleanup = coreProcessCleaner.CleanupForServiceMode(initialServiceModeStatus);
            if (!serviceCleanup.IsSuccess)
            {
                AppLogger.Warning(serviceCleanup.Message);
                return BootstrapResult.Failure(serviceCleanup.Message);
            }

            var result = await serviceModeManager.StartCoreHostAsync(
                HubStartupCoordinator.CreateServiceModeCoreHostRequest(),
                cancellationToken);
            if (result.IsSuccess)
            {
                _isServiceModeCoreHostActive = true;
                AppLogger.Info("Service-mode core started");
                return BootstrapResult.Success(result.Message);
            }

            _isServiceModeCoreHostActive = false;
            AppLogger.Warning($"Service-mode core startup failed; core is unavailable: {result.Message}");
            return BootstrapResult.Failure(result.Message);
        }

        _isServiceModeCoreHostActive = false;
        var cleanup = coreProcessCleaner.CleanupForNormalMode(initialServiceModeStatus);
        if (!cleanup.IsSuccess)
        {
            AppLogger.Warning(cleanup.Message);
            return BootstrapResult.Failure(cleanup.Message);
        }

        return await ResumeNormalCoreAsync(cancellationToken);
    }

    private static ServiceModeStatus GetInitialServiceModeStatus(IServiceModeManager serviceModeManager, bool waitForTunService)
    {
#if DEBUG
        var startedAt = Stopwatch.GetTimestamp();
        AppLogger.Info($"[StartupTrace] Service status probing started waitForTun={waitForTunService}");
#endif
        var status = ProbeServiceModeStatus(serviceModeManager);
        if (!waitForTunService || status.IsRunning || !status.IsInstalled)
        {
#if DEBUG
            AppLogger.Info($"[StartupTrace] Service status probing completed elapsed={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}ms polls=1 state={status.State}");
#endif
            return status;
        }

        // TUN 启动依赖服务核心；登录瞬间服务可能仍在 SCM 启动路径上。
        var stopwatch = Stopwatch.StartNew();
#if DEBUG
        var pollCount = 1;
#endif
        while (stopwatch.Elapsed < InitialServiceModeTunWaitTimeout)
        {
            Thread.Sleep(InitialServiceModeStatusPollInterval);
            status = ProbeServiceModeStatus(serviceModeManager);
#if DEBUG
            pollCount++;
#endif
            if (status.IsRunning || !status.IsInstalled)
            {
#if DEBUG
                AppLogger.Info($"[StartupTrace] Service status probing completed elapsed={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}ms polls={pollCount} state={status.State}");
#endif
                return status;
            }
        }

#if DEBUG
        AppLogger.Info($"[StartupTrace] Service status probing timed out elapsed={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}ms polls={pollCount} state={status.State}");
#endif
        return status;
    }

    private static ServiceModeStatus ProbeServiceModeStatus(IServiceModeManager serviceModeManager)
    {
#if DEBUG
        var startedAt = Stopwatch.GetTimestamp();
#endif
        try
        {
            var status = serviceModeManager.GetStatusAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
#if DEBUG
            AppLogger.Info($"[StartupTrace] Service status probe elapsed={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}ms state={status.State}");
#endif
            return status;
        }
        catch (Exception exception)
        {
#if DEBUG
            AppLogger.Info($"[StartupTrace] Service status probe failed elapsed={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}ms");
#endif
            AppLogger.Warning($"Service-mode status probe failed: {exception.Message}");
            return ServiceModeStatus.Unavailable(exception.Message);
        }
    }

#if DEBUG
    private static void LogStartupTrace(string stage, long startedAt)
    {
        AppLogger.Info($"[StartupTrace] {stage} elapsed={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0.0}ms");
    }
#endif

    private ICoreManager CreateCoreManager(ServiceModeStatus initialServiceModeStatus, IServiceModeManager serviceModeManager)
    {
        if (initialServiceModeStatus.IsRunning)
        {
            return new ServiceModeCoreManager(
                serviceModeManager,
                HubStartupCoordinator.CorePipe,
                DesktopApplicationLayout.CoreBinaryPath,
                DesktopApplicationLayout.CoreDirectory,
                HubStartupCoordinator.WriteServiceModeActiveConfig,
                isActive => _isServiceModeCoreHostActive = isActive);
        }

        return CreateNormalCoreManager();
    }

    private static bool UsesTrayRuntime => DesktopLaunchContext.TraySession is not null;

    private static ICoreManager CreateNormalCoreManager()
    {
        return UsesTrayRuntime
            ? new TrayCoreManager()
            : new IpcCoreManager(TrayCoreEndpoints.Hub);
    }

    private static async Task<BootstrapResult> StopNormalCoreAsync(CancellationToken cancellationToken)
    {
        if (!UsesTrayRuntime)
        {
            return await HubStartupCoordinator.StopCoreAsync(cancellationToken);
        }

        await using var manager = new TrayCoreManager();
        var result = await manager.StopCoreAsync(cancellationToken);
        return result.IsSuccess
            ? BootstrapResult.Success(result.Message)
            : BootstrapResult.Failure(result.Message);
    }

    private static async Task<BootstrapResult> ResumeNormalCoreAsync(CancellationToken cancellationToken)
    {
        if (!UsesTrayRuntime)
        {
            return await HubStartupCoordinator.ResumeCoreAsync(cancellationToken);
        }

        await using var manager = new TrayCoreManager();
        try
        {
            var result = await manager.EnsureReadyAsync(cancellationToken);
            return BootstrapResult.Success(result.Message);
        }
        catch (Exception exception)
        {
            return BootstrapResult.Failure(exception.Message);
        }
    }

    private static CoreHostOperationResult ToCoreHostResult(BootstrapResult result)
    {
        return result.Ok
            ? CoreHostOperationResult.Success(result.Message)
            : CoreHostOperationResult.Failure(result.Message);
    }

    private void StartAppUpdateAutoCheckTimer(AppUpdateAutoCheckRunner runner)
    {
        _appUpdateAutoCheckTimer = new DispatcherTimer
        {
            // 每 30 分钟检查更新，避免频繁访问发布 API。
            Interval = TimeSpan.FromMinutes(30)
        };
        _appUpdateAutoCheckTimer.Tick += async (_, _) => await RunAppUpdateCheckAsync(() => runner.RunDueCheckAsync());
        _appUpdateAutoCheckTimer.Start();
        AppLogger.Info("Scheduled app update checks started");
    }

    private static async Task RunAppUpdateCheckAsync(Func<Task<AppUpdateAutoCheckResult>> check)
    {
        try
        {
            await check();
        }
        catch (Exception exception)
        {
            AppLogger.Warning($"App update scheduler failed: {exception.Message}");
        }
    }

    private void StartSubscriptionAutoDelayTimer(MainWindowViewModel viewModel)
    {
        _subscriptionAutoDelayTimer = new DispatcherTimer
        {
            // 自动延迟测试按到期时间检查，不强制固定频率探测。
            Interval = TimeSpan.FromMinutes(1)
        };
        _subscriptionAutoDelayTimer.Tick += async (_, _) => await viewModel.SubscriptionAutoDelay.RunDueAsync();
        _subscriptionAutoDelayTimer.Start();
        AppLogger.Info("Subscription auto-delay scheduler started");
    }

    private async Task InitializeSubscriptionServicesAsync(
        SubscriptionPageViewModel subscriptionPage,
        SubscriptionAutoUpdateCoordinator autoUpdate)
    {
        await subscriptionPage.InitializeAsync();
        await autoUpdate.RunStartupAsync();
        StartSubscriptionAutoUpdateTimer(autoUpdate);
    }

    private void StartSubscriptionAutoUpdateTimer(SubscriptionAutoUpdateCoordinator autoUpdate)
    {
        _subscriptionAutoUpdateTimer = new DispatcherTimer
        {
            // 最小更新间隔为分钟，按一分钟粒度检查到期订阅。
            Interval = TimeSpan.FromMinutes(1)
        };
        _subscriptionAutoUpdateTimer.Tick += async (_, _) => await autoUpdate.RunDueAsync();
        _subscriptionAutoUpdateTimer.Start();
        AppLogger.Info("Subscription auto-update scheduler started");
    }

    private void StartHomeRuntimeTimer(MainWindowViewModel viewModel)
    {
        _homeRuntimeTimer = new DispatcherTimer
        {
            // 主页运行状态每秒刷新；后台页面跳过实时轮询。
            Interval = TimeSpan.FromSeconds(1)
        };
        _homeRuntimeTimer.Tick += (_, _) => viewModel.OnHomeRuntimeTick();
        _homeRuntimeTimer.Start();
        AppLogger.Info("Home runtime refresh started");
    }

    private void StartWebDavBackupTimer(MainWindowViewModel viewModel)
    {
        _webDavBackupTimer = new DispatcherTimer
        {
            // 定时备份按到期时间检查，避免频繁访问 WebDAV 服务。
            Interval = TimeSpan.FromMinutes(10)
        };
        _webDavBackupTimer.Tick += async (_, _) => await viewModel.DataManagement.CreateScheduledWebDavBackupAsync();
        _webDavBackupTimer.Start();
        _ = viewModel.DataManagement.CreateScheduledWebDavBackupAsync();
        AppLogger.Info("WebDAV backup scheduler started");
    }

    private static SystemProxyPlatform CurrentSystemProxyPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return SystemProxyPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return SystemProxyPlatform.Linux;
        }

        if (OperatingSystem.IsMacOS())
        {
            return SystemProxyPlatform.MacOS;
        }

        return SystemProxyPlatform.Other;
    }

    private static IAppBehaviorService CreateAppBehaviorService()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsAppBehaviorService(DesktopApplicationLayout.TrayBinaryPath);
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxAppBehaviorService(DesktopApplicationLayout.TrayBinaryPath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOSAppBehaviorService(DesktopApplicationLayout.TrayBinaryPath);
        }

        return new UnsupportedAppBehaviorService();
    }

    private static async Task RegisterGlobalHotkeysAsync(
        IGlobalHotkeyService globalHotkeys,
        AppSettings settings)
    {
        foreach (var (action, gesture) in new[]
        {
            (GlobalHotkeyAction.ToggleWindow, settings.WindowToggleHotkey),
            (GlobalHotkeyAction.ToggleSystemProxy, settings.SystemProxyToggleHotkey),
            (GlobalHotkeyAction.ToggleTun, settings.TunToggleHotkey),
        })
        {
            try
            {
                var result = await globalHotkeys.ApplyAsync(action, gesture).ConfigureAwait(true);
                if (!result.IsSuccess)
                {
                    AppLogger.Warning($"Global hotkey startup registration failed: action={action} error={result.Error}");
                }
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Global hotkey startup registration failed: action={action} error={exception.Message}");
            }
        }
    }
}
