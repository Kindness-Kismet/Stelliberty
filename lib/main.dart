import 'dart:async';
import 'dart:io';
import 'dart:typed_data';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:provider/single_child_widget.dart';
import 'package:bitsdojo_window/bitsdojo_window.dart';
import 'package:window_manager/window_manager.dart';
import 'package:flutter_acrylic/flutter_acrylic.dart';
import 'package:rinf/rinf.dart';
import 'package:stelliberty/atomic/platform_helper.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/services/window_state_service.dart';
import 'package:stelliberty/services/single_instance_sevice.dart';
import 'package:stelliberty/services/windows_injector_service.dart';
import 'package:stelliberty/services/hotkey_service.dart';
import 'package:stelliberty/services/power_event_service.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/tray/tray_manager.dart';
import 'package:stelliberty/providers/theme_provider.dart';
import 'package:stelliberty/providers/language_provider.dart';
import 'package:stelliberty/providers/window_effect_provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/providers/app_update_provider.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/services/override_service.dart';
import 'package:stelliberty/clash/services/dns_service.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/connection_provider.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/clash/providers/core_log_provider.dart';
import 'package:stelliberty/clash/providers/override_provider.dart';
import 'package:stelliberty/clash/providers/service_provider.dart';
import 'package:stelliberty/clash/model/override_model.dart' as app_override;
import 'package:stelliberty/src/bindings/bindings.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/ui/basic.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/dev_test/test_manager.dart';

void main(List<String> args) async {
  // 确保 Flutter 绑定已初始化
  WidgetsFlutterBinding.ensureInitialized();

  // 检查是否为自启动
  final isSilentStart = args.contains('--silent-start');
  if (isSilentStart) {
    Logger.info('检测到自启动参数 --silent-start，将强制静默启动');
  }

  // 🧪 测试模式检查
  final testType = TestManager.testType;
  if (testType != null) {
    Logger.info('🧪 检测到测试模式: $testType');
    await AppInitializer.initialize(
      assignRustSignal: assignRustSignal,
      args: args,
    );
    await TestManager.runTest(testType);
    return;
  }

  // 应用初始化
  await AppInitializer.initialize(
    assignRustSignal: assignRustSignal,
    args: args,
  );

  // 创建并初始化所有 Providers
  final providers = await ProviderSetup.createProviders();

  // 建立 Provider 依赖关系
  await ProviderSetup.setupDependencies(providers);

  // 启动 Clash 核心（不阻塞 UI）
  ProviderSetup.startClash(providers);

  // 设置托盘管理器（仅桌面平台）
  ProviderSetup.setupTray(providers);

  // 启动时更新（不阻塞 UI 启动）
  ProviderSetup.scheduleStartupUpdate(providers);

  // 启动 Flutter UI
  runApp(
    MultiProvider(
      providers: ProviderSetup.getProviderWidgets(providers),
      child: TranslationProvider(child: const BasicLayout()),
    ),
  );

  // 加载窗口状态（仅桌面平台）
  if (PlatformHelper.needsWindowManagement) {
    doWhenWindowReady(() async {
      await WindowStateManager.loadAndApplyState(forceSilent: isSilentStart);
    });
  }
}

// ============================================================================
// 应用初始化
// ============================================================================

// 应用初始化编排
// 负责按正确顺序初始化所有服务和组件
class AppInitializer {
  // 主初始化流程
  static Future<void> initialize({
    required Map<String, void Function(Uint8List, Uint8List)> assignRustSignal,
    required List<String> args,
  }) async {
    // 确保应用单实例运行（仅桌面平台）
    if (PlatformHelper.supportsSingleInstance) {
      await ensureSingleInstance();
    }

    // 初始化 Rust 后端通信
    await initializeRust(assignRustSignal);

    // 初始化基础服务（路径、配置）
    await _initializeBaseServices();

    // 初始化其他应用服务（日志、窗口等）
    await _initializeOtherServices();

    // Windows 平台：注入键盘事件修复器
    if (Platform.isWindows) {
      WindowsInjector.instance.injectKeyData();
    }

    Logger.info('应用初始化完成');
  }

  // 初始化基础服务（路径、配置存储）
  // 必须最先执行，其他服务依赖这些基础服务
  static Future<void> _initializeBaseServices() async {
    // 先初始化路径服务（其他服务依赖它）
    await PathService.instance.initialize();

    // 再并行初始化配置服务（它们依赖路径服务）
    await Future.wait([
      AppPreferences.instance.init(),
      ClashPreferences.instance.init(),
    ]);
  }

  // 初始化其他应用服务（日志、窗口、托盘、DNS等）
  static Future<void> _initializeOtherServices() async {
    final appDataPath = PathService.instance.appDataPath;

    // 初始化日志系统（依赖路径服务）
    await Logger.initialize();

    // 同步应用日志开关状态到 Rust 端
    final appLogEnabled = AppPreferences.instance.getAppLogEnabled();
    SetAppLogEnabled(isEnabled: appLogEnabled).sendSignalToRust();
    Logger.info('应用日志开关已同步到 Rust 端: $appLogEnabled');

    // 并行初始化其他服务（窗口、DNS）
    await Future.wait([
      _initializeWindowServices(),
      DnsService.instance.initialize(appDataPath),
    ]);
  }

  // 初始化桌面窗口服务（仅桌面平台）
  static Future<void> _initializeWindowServices() async {
    if (!PlatformHelper.needsWindowManagement) {
      return;
    }

    await Window.initialize();
    await windowManager.ensureInitialized();

    if (Platform.isLinux) {
      await windowManager.setTitleBarStyle(TitleBarStyle.hidden);
    } else {
      await Window.hideWindowControls();
    }

    // 阻止窗口直接关闭，拦截关闭事件以执行清理操作
    await windowManager.setPreventClose(true);

    // 初始化窗口监听器，拦截关闭事件
    await AppWindowListener().initialize();

    await AppTrayManager().initialize();
  }
}

// ============================================================================
// Provider 配置
// ============================================================================

// Provider 配置和依赖注入
// 负责创建、初始化、注册所有 Provider
class ProviderSetup {
  // 创建并初始化所有 Providers
  static Future<ProviderBundle> createProviders() async {
    try {
      final appDataPath = PathService.instance.appDataPath;
      return await _createProviders(appDataPath);
    } catch (e, stackTrace) {
      Logger.error('Provider 初始化失败：$e');
      Logger.error('堆栈跟踪：$stackTrace');
      Logger.warning('尝试以降级模式启动…');
      return _createFallbackProviders();
    }
  }

  // 获取 Provider 列表（供 MultiProvider 使用）
  static List<SingleChildWidget> getProviderWidgets(ProviderBundle bundle) {
    return [
      // --- Core Providers ---
      // ClashManager 不再是 ChangeNotifier，移除 Provider
      // 改为通过 ClashProvider.clashManager 访问
      ChangeNotifierProvider.value(value: bundle.clashProvider),
      ChangeNotifierProvider.value(value: bundle.subscriptionProvider),
      ChangeNotifierProvider.value(value: bundle.overrideProvider),
      ChangeNotifierProvider(
        create: (context) => ConnectionProvider(context.read<ClashProvider>()),
      ),
      ChangeNotifierProvider.value(value: bundle.logProvider),
      // --- Business Logic Providers ---
      Provider.value(value: bundle.serviceProvider),
      // --- UI Providers ---
      ChangeNotifierProvider(create: (_) => ContentProvider()),
      ChangeNotifierProvider.value(value: bundle.themeProvider),
      ChangeNotifierProvider.value(value: bundle.languageProvider),
      ChangeNotifierProvider.value(value: bundle.windowEffectProvider),
      ChangeNotifierProvider.value(value: bundle.appUpdateProvider),
    ];
  }

  // 建立 Provider 间的依赖关系
  static Future<void> setupDependencies(ProviderBundle providers) async {
    // 建立双向引用
    providers.subscriptionProvider.setClashProvider(providers.clashProvider);

    // 设置 HotkeyService 的 providers 并初始化
    HotkeyService.instance.setProviders(
      clashProvider: providers.clashProvider,
      subscriptionProvider: providers.subscriptionProvider,
    );
    await HotkeyService.instance.initialize();

    // 初始化电源事件服务
    PowerEventService().init();

    // 设置覆写系统集成
    await providers.subscriptionProvider.setupOverrideIntegration(
      providers.overrideProvider,
    );

    // 设置 ClashManager 的覆写获取回调
    ClashManager.instance.setOverridesGetter(() {
      final currentSub = providers.subscriptionProvider.currentSubscription;
      if (currentSub == null || currentSub.overrideIds.isEmpty) {
        return [];
      }

      final overrides = <OverrideConfig>[];
      for (final id in currentSub.overrideIds) {
        final override = providers.overrideProvider.getOverrideById(id);
        if (override != null &&
            override.content != null &&
            override.content!.isNotEmpty) {
          overrides.add(
            OverrideConfig(
              id: override.id,
              name: override.name,
              format: override.format == app_override.OverrideFormat.yaml
                  ? OverrideFormat.yaml
                  : OverrideFormat.javascript,
              content: override.content!,
            ),
          );
        }
      }
      return overrides;
    });

    // 设置覆写失败回调
    final currentSub = providers.subscriptionProvider.currentSubscription;
    if (currentSub != null && currentSub.overrideIds.isNotEmpty) {
      Logger.debug('检测到当前订阅有覆写，设置覆写失败回调');
      ClashManager.instance.setOnOverridesFailed(() async {
        Logger.warning('检测到覆写失败，开始回退处理');
        await providers.subscriptionProvider.handleOverridesFailed();
      });
    } else {
      Logger.debug('当前订阅无覆写，跳过设置覆写失败回调');
    }

    // 设置默认配置启动成功回调
    ClashManager.instance.setOnThirdLevelFallback(() async {
      Logger.warning('使用默认配置启动成功，清除失败的订阅选择');
      await providers.subscriptionProvider.clearCurrentSubscription();
    });
  }

  // 启动 Clash 核心（不阻塞 UI）
  static void startClash(ProviderBundle providers) {
    final configPath = providers.subscriptionProvider
        .getSubscriptionConfigPath();

    unawaited(
      providers.clashProvider.start(configPath: configPath).catchError((e) {
        Logger.error('Clash 核心启动失败：$e');
        return false;
      }),
    );
  }

  // 设置托盘管理器（仅桌面平台）
  static void setupTray(ProviderBundle providers) {
    if (!PlatformHelper.needsSystemTray) {
      return;
    }

    AppTrayManager().setClashProvider(providers.clashProvider);
    AppTrayManager().setSubscriptionProvider(providers.subscriptionProvider);
  }

  // 启动时更新（不阻塞 UI 启动流程）
  static void scheduleStartupUpdate(ProviderBundle providers) {
    Logger.info('触发启动时更新检查');
    unawaited(providers.subscriptionProvider.performStartupUpdate());
  }

  // ========================================================================
  // 内部实现
  // ========================================================================

  // 创建并初始化所有 Providers
  static Future<ProviderBundle> _createProviders(String appDataPath) async {
    // 创建共享的 OverrideService 实例
    final overrideService = OverrideService();
    await overrideService.initialize();

    // 创建 Provider 实例
    final themeProvider = ThemeProvider();
    final windowEffectProvider = WindowEffectProvider();
    final languageProvider = LanguageProvider();
    final subscriptionProvider = SubscriptionProvider(overrideService);
    final overrideProvider = OverrideProvider(overrideService);
    final clashProvider = ClashProvider();
    final logProvider = LogProvider();
    final serviceProvider = ServiceProvider();
    final appUpdateProvider = AppUpdateProvider();

    // 并行初始化无依赖的 Providers
    final initFutures = [
      themeProvider.initialize(),
      windowEffectProvider.initialize(),
      languageProvider.initialize(),
      subscriptionProvider.initialize(),
      overrideProvider.initialize(),
      appUpdateProvider.initialize(),
    ];

    // 服务模式仅在桌面平台可用
    if (Platform.isWindows || Platform.isMacOS || Platform.isLinux) {
      initFutures.add(serviceProvider.initialize());
    }

    await Future.wait(initFutures);

    // 初始化有依赖的 Providers
    final currentConfig = subscriptionProvider.getSubscriptionConfigPath();
    await clashProvider.initialize(currentConfig);
    logProvider.initialize();

    return ProviderBundle(
      themeProvider: themeProvider,
      windowEffectProvider: windowEffectProvider,
      languageProvider: languageProvider,
      subscriptionProvider: subscriptionProvider,
      overrideProvider: overrideProvider,
      clashProvider: clashProvider,
      logProvider: logProvider,
      serviceProvider: serviceProvider,
      appUpdateProvider: appUpdateProvider,
    );
  }

  // 创建降级模式的 Providers
  static Future<ProviderBundle> _createFallbackProviders() async {
    // 确保基础路径服务可用
    try {
      await PathService.instance.initialize();
    } catch (e) {
      Logger.error('路径服务初始化失败：$e');
    }

    // 创建共享的 OverrideService 实例
    final overrideService = OverrideService();
    try {
      await overrideService.initialize();
      Logger.info('降级模式：OverrideService 初始化成功');
    } catch (e) {
      Logger.warning('降级模式：OverrideService 初始化失败，但继续运行：$e');
    }

    // 创建最基本的 Providers
    return ProviderBundle(
      themeProvider: ThemeProvider(),
      windowEffectProvider: WindowEffectProvider(),
      languageProvider: LanguageProvider(),
      subscriptionProvider: SubscriptionProvider(overrideService),
      overrideProvider: OverrideProvider(overrideService),
      clashProvider: ClashProvider(),
      logProvider: LogProvider(),
      serviceProvider: ServiceProvider(),
      appUpdateProvider: AppUpdateProvider(),
    );
  }
}

// ========================================================================
// Provider 集合类型定义
// ========================================================================

// 应用所有 Provider 的集合
class ProviderBundle {
  final ThemeProvider themeProvider;
  final WindowEffectProvider windowEffectProvider;
  final LanguageProvider languageProvider;
  final SubscriptionProvider subscriptionProvider;
  final OverrideProvider overrideProvider;
  final ClashProvider clashProvider;
  final LogProvider logProvider;
  final ServiceProvider serviceProvider;
  final AppUpdateProvider appUpdateProvider;

  const ProviderBundle({
    required this.themeProvider,
    required this.windowEffectProvider,
    required this.languageProvider,
    required this.subscriptionProvider,
    required this.overrideProvider,
    required this.clashProvider,
    required this.logProvider,
    required this.serviceProvider,
    required this.appUpdateProvider,
  });
}
