import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_acrylic/flutter_acrylic.dart';
import 'package:provider/provider.dart';
import 'package:rinf/rinf.dart';
import 'package:bitsdojo_window/bitsdojo_window.dart';

// App Core & Services
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/services/dns_service.dart';
import 'package:stelliberty/clash/services/override_service.dart';
import 'package:stelliberty/clash/core/state_hub.dart';
import 'package:stelliberty/clash/core/service_state.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/utils/windows_injector.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/tray/tray_manager.dart';

// Providers
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/connection_provider.dart';
import 'package:stelliberty/clash/providers/subscription_provider.dart';
import 'package:stelliberty/clash/providers/log_provider.dart';
import 'package:stelliberty/clash/providers/override_provider.dart';
import 'package:stelliberty/clash/providers/service_provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/providers/theme_provider.dart';
import 'package:stelliberty/providers/window_effect_provider.dart';
import 'package:stelliberty/providers/app_update_provider.dart';
import 'package:stelliberty/clash/data/override_model.dart' as app_override;
import 'package:stelliberty/src/bindings/signals/signals.dart';

// UI & Utils
import 'package:stelliberty/src/bindings/bindings.dart';
import 'package:stelliberty/ui/basic.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/utils/single_instance.dart';
import 'package:stelliberty/utils/window_state.dart';
import 'package:stelliberty/dev_test/test_manager.dart';
import 'package:window_manager/window_manager.dart';

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
    await initializeRust(assignRustSignal);
    await TestManager.runTest(testType);
    return;
  }

  // 确保应用单实例运行
  await ensureSingleInstance();

  // 初始化 Rust 后端通信
  await initializeRust(assignRustSignal);

  // 【关键】先初始化基础服务（路径、配置），再初始化语言
  await initializeBaseServices();

  // 加载语言设置（必须在托盘初始化之前，避免托盘显示英文）
  await initializeLanguage();

  // 初始化其它应用服务（日志、窗口、托盘等，此时语言已就绪）
  final appDataPath = await initializeOtherServices();

  // 创建并初始化所有 Providers
  final providers = await createProvidersWithErrorHandling();

  // 建立 Provider 依赖关系
  await setupProviderDependencies(providers, appDataPath);

  // 启动 Clash 核心
  startClash(
    providers.clashProvider,
    providers.subscriptionProvider,
    appDataPath,
  );

  // 设置托盘管理器
  setupTrayManager(providers.clashProvider, providers.subscriptionProvider);

  // 启动时更新
  scheduleStartupUpdate(providers.subscriptionProvider);

  // Windows 平台：注入键盘事件修复器（修复 Win+V 剪贴板历史问题）
  if (Platform.isWindows) {
    WindowsInjector.instance.injectKeyData();
  }

  // 启动 Flutter UI
  runApp(
    MultiProvider(
      providers: [
        // --- StateManagers (供 UI 监听) ---
        ChangeNotifierProvider.value(value: ServiceStateManager.instance),
        // --- Core Providers ---
        ChangeNotifierProvider.value(value: ClashManager.instance),
        ChangeNotifierProvider.value(value: providers.clashProvider),
        ChangeNotifierProvider.value(value: providers.subscriptionProvider),
        ChangeNotifierProvider.value(value: providers.overrideProvider),
        ChangeNotifierProvider(
          create: (context) =>
              ConnectionProvider(context.read<ClashProvider>()),
        ),
        ChangeNotifierProvider.value(value: providers.logProvider),
        // --- Business Logic Providers ---
        Provider.value(value: providers.serviceProvider),
        // --- UI Providers ---
        ChangeNotifierProvider(create: (_) => ContentProvider()),
        ChangeNotifierProvider.value(value: providers.themeProvider),
        ChangeNotifierProvider.value(value: providers.windowEffectProvider),
        ChangeNotifierProvider.value(value: providers.appUpdateProvider),
      ],
      child: TranslationProvider(child: const BasicLayout()),
    ),
  );
  // 加载窗口状态
  if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
    doWhenWindowReady(() async {
      await WindowStateManager.loadAndApplyState(forceSilent: isSilentStart);
    });
  }
}

// ============================================================================
// Provider 集合类型定义
// ============================================================================

// 应用所有 Provider 的集合
class ProviderBundle {
  final ThemeProvider themeProvider;
  final WindowEffectProvider windowEffectProvider;
  final SubscriptionProvider subscriptionProvider;
  final OverrideProvider overrideProvider;
  final ClashProvider clashProvider;
  final LogProvider logProvider;
  final ServiceProvider serviceProvider;
  final AppUpdateProvider appUpdateProvider;

  const ProviderBundle({
    required this.themeProvider,
    required this.windowEffectProvider,
    required this.subscriptionProvider,
    required this.overrideProvider,
    required this.clashProvider,
    required this.logProvider,
    required this.serviceProvider,
    required this.appUpdateProvider,
  });
}

// ============================================================================
// 应用服务初始化（路径、日志、配置、窗口、业务服务等）
// ============================================================================

// 初始化基础服务（路径、配置存储）
// 必须最先执行，其他服务依赖这些基础服务
Future<void> initializeBaseServices() async {
  await Future.wait([
    PathService.instance.initialize(),
    AppPreferences.instance.init(),
    ClashPreferences.instance.init(),
  ]);
}

// 初始化其他应用服务（日志、窗口、托盘、DNS等）
// 在语言初始化之后执行，确保托盘等 UI 组件能使用正确的多语言
Future<String> initializeOtherServices() async {
  final appDataPath = PathService.instance.appDataPath;

  // 初始化日志系统（依赖路径服务）
  await Logger.initialize();

  // 同步应用日志开关状态到 Rust 端（避免重启后状态不一致）
  final appLogEnabled = AppPreferences.instance.getAppLogEnabled();
  SetAppLogEnabled(enabled: appLogEnabled).sendSignalToRust();
  Logger.info('应用日志开关已同步到 Rust 端: $appLogEnabled');

  // 初始化状态中枢（业务逻辑协调）
  StateHub.instance;
  Logger.info('状态中枢已初始化，开始全局状态协调');

  // 并行初始化其他服务（窗口、DNS）
  // 窗口服务包含托盘初始化，此时语言已就绪
  await Future.wait([
    initializeWindowServices(),
    DnsService.instance.initialize(appDataPath),
  ]);

  return appDataPath;
}

// 初始化桌面窗口服务
Future<void> initializeWindowServices() async {
  if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
    await Window.initialize();
    await windowManager.ensureInitialized();
    await Window.hideWindowControls();
    await AppTrayManager().initialize();
  }
}

// 初始化语言设置
Future<void> initializeLanguage() async {
  final savedLanguage = AppPreferences.instance.getLanguageMode();
  const localeMap = {'zh': AppLocale.zhCn, 'en': AppLocale.en};
  final locale = localeMap[savedLanguage];
  if (locale != null) {
    LocaleSettings.setLocale(locale);
  } else {
    LocaleSettings.useDeviceLocale();
  }
}

// ============================================================================
// Provider 工厂（创建、初始化、依赖注入）
// ============================================================================

// 带错误处理的 Provider 创建包装器
Future<ProviderBundle> createProvidersWithErrorHandling() async {
  try {
    final appDataPath = PathService.instance.appDataPath;
    return await createProviders(appDataPath);
  } catch (e, stackTrace) {
    Logger.error('Provider 初始化失败：$e');
    Logger.error('堆栈跟踪：$stackTrace');
    Logger.warning('尝试以降级模式启动…');
    return createFallbackProviders();
  }
}

// 创建并初始化所有 Providers
Future<ProviderBundle> createProviders(String appDataPath) async {
  // 创建共享的 OverrideService 实例
  final overrideService = OverrideService();
  await overrideService.initialize();

  // 创建 Provider 实例
  final themeProvider = ThemeProvider();
  final windowEffectProvider = WindowEffectProvider();
  final subscriptionProvider = SubscriptionProvider(overrideService);
  final overrideProvider = OverrideProvider(appDataPath, overrideService);
  final clashProvider = ClashProvider();
  final logProvider = LogProvider();
  final serviceProvider = ServiceProvider();
  final appUpdateProvider = AppUpdateProvider();

  // 并行初始化无依赖的 Providers
  await Future.wait([
    themeProvider.initialize(),
    windowEffectProvider.initialize(),
    subscriptionProvider.initialize(appDataPath),
    overrideProvider.initialize(),
    serviceProvider.initialize(),
    appUpdateProvider.initialize(),
  ]);

  // 初始化有依赖的 Providers
  final currentConfig = subscriptionProvider.getSubscriptionConfigPath();
  await clashProvider.initialize(currentConfig);
  logProvider.initialize();

  return ProviderBundle(
    themeProvider: themeProvider,
    windowEffectProvider: windowEffectProvider,
    subscriptionProvider: subscriptionProvider,
    overrideProvider: overrideProvider,
    clashProvider: clashProvider,
    logProvider: logProvider,
    serviceProvider: serviceProvider,
    appUpdateProvider: appUpdateProvider,
  );
}

// 建立 Provider 间的依赖关系
Future<void> setupProviderDependencies(
  ProviderBundle providers,
  String appDataPath,
) async {
  // 建立双向引用
  providers.subscriptionProvider.setClashProvider(providers.clashProvider);

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
    ClashManager.instance.setOverridesFailedCallback(() async {
      Logger.warning('检测到覆写失败，开始回退处理');
      await providers.subscriptionProvider.handleOverridesFailed();
    });
  } else {
    Logger.debug('当前订阅无覆写，跳过设置覆写失败回调');
  }
}

// ============================================================================
// 业务启动（Clash、托盘）
// ============================================================================

// 启动 Clash 核心（不阻塞 UI）
void startClash(
  ClashProvider clashProvider,
  SubscriptionProvider subscriptionProvider,
  String appDataPath,
) {
  // 优先使用订阅配置路径，否则传 null（ClashProvider 会使用内存中的默认配置）
  final configPath = subscriptionProvider.getSubscriptionConfigPath();

  unawaited(
    clashProvider.start(configPath: configPath).catchError((e) {
      Logger.error('Clash 核心启动失败：$e');
      return false;
    }),
  );
}

// 设置托盘管理器
void setupTrayManager(
  ClashProvider clashProvider,
  SubscriptionProvider subscriptionProvider,
) {
  if (Platform.isWindows || Platform.isLinux || Platform.isMacOS) {
    AppTrayManager().setClashProvider(clashProvider);
    AppTrayManager().setSubscriptionProvider(subscriptionProvider);
  }
}

// 调度启动时更新（延迟 3 秒，不阻塞启动流程，支持静默启动）
void scheduleStartupUpdate(SubscriptionProvider subscriptionProvider) {
  unawaited(
    Future.delayed(const Duration(seconds: 3), () async {
      Logger.info('开始执行启动时更新检查');
      await subscriptionProvider.performStartupUpdate();
    }),
  );
}

// ============================================================================
// 错误处理与降级
// ============================================================================

// 创建降级模式的 Providers
Future<ProviderBundle> createFallbackProviders() async {
  // 确保基础路径服务可用
  try {
    await PathService.instance.initialize();
  } catch (e) {
    Logger.error('路径服务初始化失败：$e');
  }

  final appDataPath = PathService.instance.appDataPath;

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
    subscriptionProvider: SubscriptionProvider(overrideService),
    overrideProvider: OverrideProvider(appDataPath, overrideService),
    clashProvider: ClashProvider(),
    logProvider: LogProvider(),
    serviceProvider: ServiceProvider(),
    appUpdateProvider: AppUpdateProvider(),
  );
}
