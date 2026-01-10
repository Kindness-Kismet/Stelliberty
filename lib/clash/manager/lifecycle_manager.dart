import 'dart:async';
import 'package:stelliberty/clash/network/api_client.dart';
import 'package:stelliberty/clash/services/process_service.dart';
import 'package:stelliberty/clash/manager/process_manager.dart';
import 'package:stelliberty/clash/config/config_injector.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/services/traffic_monitor.dart';
import 'package:stelliberty/clash/services/core_log_service.dart';
import 'package:stelliberty/clash/services/geo_service.dart';
import 'package:stelliberty/clash/manager/service_manager.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/clash/state/core_states.dart';
import 'package:stelliberty/atomic/permission_checker.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/services/log_print_service.dart';

// Clash 生命周期管理器
// 负责 Clash 核心的启动、停止、重启
class LifecycleManager {
  final ProcessService _processService;
  late final ProcessManager _processManager;
  final ClashApiClient _apiClient;
  final TrafficMonitor _trafficMonitor;
  final ClashLogService _logService;
  final Function() _notifyListeners;

  // 状态变化回调
  Function(CoreState)? _onCoreStateChanged;
  Function(String)? _onCoreVersionChanged;
  Function(String?)? _onConfigPathChanged;
  Function(ClashStartMode?)? _onStartModeChanged;

  // 设置状态变化回调
  void setOnCoreStateChanged(Function(CoreState)? callback) {
    _onCoreStateChanged = callback;
  }

  void setOnCoreVersionChanged(Function(String)? callback) {
    _onCoreVersionChanged = callback;
  }

  void setOnConfigPathChanged(Function(String?)? callback) {
    _onConfigPathChanged = callback;
  }

  void setOnStartModeChanged(Function(ClashStartMode?)? callback) {
    _onStartModeChanged = callback;
  }

  // 核心状态（由 Manager 直接管理）
  CoreState _coreState = CoreState.stopped;
  CoreState get coreState => _coreState;

  // 更新核心状态并通知
  void _updateCoreState(CoreState newState) {
    if (_coreState == newState) return;

    final previousState = _coreState;
    _coreState = newState;
    Logger.debug('核心状态变化：${previousState.name} -> ${newState.name}');
    _onCoreStateChanged?.call(newState);
    _notifyListeners();
  }

  // 回退标记（防止无限递归）
  bool _isFallbackRetry = false;

  // 当前启动模式
  ClashStartMode? _currentStartMode;
  ClashStartMode? get currentStartMode => _currentStartMode;

  // 原始订阅文件路径（用于回退和重启）
  String? _originalConfigPath;
  String? get currentConfigPath => _originalConfigPath;

  // 更新当前配置路径（用于重载后同步路径）
  void updateConfigPath(String? configPath) {
    if (configPath == null || configPath.isEmpty) return;

    _originalConfigPath = configPath;
    _onConfigPathChanged?.call(configPath);
    Logger.debug('配置路径已更新：$configPath');
  }

  // 启动时实际使用的端口列表（用于停止时准确释放）
  List<int>? _actualPortsUsed;

  // 服务心跳定时器
  Timer? _serviceHeartbeatTimer;

  // 状态缓存
  String _coreVersion = 'Unknown';
  String get coreVersion => _coreVersion;

  // 运行状态
  bool get isCoreRunning => _coreState.isRunning;
  bool get isCoreRestarting => _coreState == CoreState.restarting;
  bool get isCoreStarting => _coreState == CoreState.starting;
  bool get isCoreStopping => _coreState == CoreState.stopping;

  LifecycleManager({
    required ProcessService processService,
    required ClashApiClient apiClient,
    required TrafficMonitor trafficMonitor,
    required ClashLogService logService,
    Function()? notifyListeners,
  }) : _processService = processService,
       _apiClient = apiClient,
       _trafficMonitor = trafficMonitor,
       _logService = logService,
       _notifyListeners = notifyListeners ?? (() {}) {
    _processManager = ProcessManager(service: _processService);
  }

  // 启动 Clash 核心（不触碰系统代理）
  //
  // 参数：
  // - configPath: 配置文件路径（可选，为空时使用保存的原始路径）
  // - overrides: 覆写配置列表（由 ClashManager 通过回调获取）
  // - enableFallback: 是否启用自动回退（默认 true）
  // - onOverridesFailed: 覆写失败时的回调（用于禁用覆写）
  // - onThirdLevelFallback: 使用默认配置启动成功时的回调（用于清除 currentSubscription）
  Future<bool> startCore({
    String? configPath,
    List<OverrideConfig> overrides = const [],
    bool enableFallback = true,
    Future<void> Function()? onOverridesFailed,
    Future<void> Function()? onThirdLevelFallback,
    required int mixedPort, // 混合端口
    required bool isIpv6Enabled,
    required bool isTunEnabled,
    required String tunStack,
    required String tunDevice,
    required bool isTunAutoRouteEnabled,
    required bool isTunAutoRedirectEnabled,
    required bool isTunAutoDetectInterfaceEnabled,
    required List<String> tunDnsHijacks,
    required bool isTunStrictRouteEnabled,
    required List<String> tunRouteExcludeAddresses,
    required bool isTunIcmpForwardingDisabled,
    required int tunMtu,
    required bool isAllowLanEnabled,
    required bool isTcpConcurrentEnabled,
    required String geodataLoader,
    required String findProcessMode,
    required String clashCoreLogLevel,
    required String externalController,
    required bool isUnifiedDelayEnabled,
    required String outboundMode,
    int? socksPort,
    int? httpPort, // 单独 HTTP 端口（可选）
  }) async {
    if (isCoreStarting) {
      Logger.warning('Clash 正在启动中，请勿重复调用');
      return false;
    }

    if (isCoreRestarting) {
      Logger.warning('Clash 正在重启中，请勿手动启动');
      return false;
    }

    if (isCoreRunning) {
      Logger.info('Clash 已在运行');
      return true;
    }

    _updateCoreState(CoreState.starting); // '开始启动核心');

    try {
      // 检查 TUN 权限（如果 TUN 已启用）
      bool actualTunEnabled = isTunEnabled;
      if (isTunEnabled) {
        final hasTunPermission = await _checkTunPermission();
        if (!hasTunPermission) {
          Logger.warning('TUN 已启用但没有权限，自动禁用 TUN');
          actualTunEnabled = false;
        }
      }
      // 生成运行时配置（支持无配置路径时使用默认配置）
      final generatedConfigPath = await ConfigInjector.injectCustomConfigParams(
        configPath: configPath,
        overrides: overrides,
        mixedPort: mixedPort,
        isIpv6Enabled: isIpv6Enabled,
        isTunEnabled: actualTunEnabled,
        tunStack: tunStack,
        tunDevice: tunDevice,
        isTunAutoRouteEnabled: isTunAutoRouteEnabled,
        isTunAutoRedirectEnabled: isTunAutoRedirectEnabled,
        isTunAutoDetectInterfaceEnabled: isTunAutoDetectInterfaceEnabled,
        tunDnsHijacks: tunDnsHijacks,
        isTunStrictRouteEnabled: isTunStrictRouteEnabled,
        tunRouteExcludeAddresses: tunRouteExcludeAddresses,
        isTunIcmpForwardingDisabled: isTunIcmpForwardingDisabled,
        tunMtu: tunMtu,
        isAllowLanEnabled: isAllowLanEnabled,
        isTcpConcurrentEnabled: isTcpConcurrentEnabled,
        geodataLoader: geodataLoader,
        findProcessMode: findProcessMode,
        clashCoreLogLevel: clashCoreLogLevel,
        externalController: externalController,
        externalControllerSecret: ClashPreferences.instance
            .getExternalControllerSecret(),
        isUnifiedDelayEnabled: isUnifiedDelayEnabled,
        outboundMode: outboundMode,
      );

      if (generatedConfigPath == null) {
        throw Exception('配置生成失败');
      }

      final runtimeConfigPath = generatedConfigPath;

      // 检查服务是否可用
      final serviceAvailable = _checkServiceAvailable();

      bool isStartSuccessful = false;

      if (serviceAvailable) {
        // 服务模式启动
        final serviceState = ServiceManager.instance.cachedState;
        Logger.info('使用服务模式启动 Clash 核心（状态：$serviceState）');
        isStartSuccessful = await _startWithService(
          runtimeConfigPath,
          externalController,
          configPath,
        );
      } else {
        // 普通模式启动
        Logger.info('使用普通模式启动 Clash 核心');
        isStartSuccessful = await _startWithSidecar(
          runtimeConfigPath,
          mixedPort, // 混合端口
          socksPort,
          httpPort, // 单独 HTTP 端口
          externalController,
          configPath,
        );
      }

      // 处理启动失败或配置验证失败的情况
      bool shouldFallback = false; // 标记是否需要回退

      if (isStartSuccessful) {
        // 启动成功，验证配置是否正确加载
        final configValid = await _validateStartupConfig();

        if (!configValid &&
            enableFallback &&
            !_isFallbackRetry &&
            overrides.isNotEmpty) {
          Logger.error('配置验证失败，可能由覆写导致，尝试禁用覆写后重试');
          shouldFallback = true;
          isStartSuccessful = false; // 标记为失败
        }
      } else if (enableFallback && !_isFallbackRetry && overrides.isNotEmpty) {
        // 启动失败且有覆写
        shouldFallback = true;
      }

      // 如果需要回退,执行回退逻辑(只调用一次回调)
      if (shouldFallback) {
        Logger.error('启动失败,检测到有覆写配置,执行回退');

        // 标记为回退重试
        _isFallbackRetry = true;

        try {
          // 确保核心已停止(可能已经停止或根本没启动成功)
          if (isCoreRunning) {
            await stopCore();
          }

          // 调用覆写失败回调(禁用当前订阅的覆写) - 只调用一次
          if (onOverridesFailed != null) {
            Logger.warning('调用覆写失败回调,禁用所有覆写');
            await onOverridesFailed();
          }

          // 等待一段时间确保资源释放
          await Future.delayed(const Duration(milliseconds: 500));

          // 重置状态,允许递归调用
          _updateCoreState(CoreState.stopped); // '回退准备重启');

          // 重新启动(不带覆写,且禁用回退以避免无限循环)
          Logger.info('使用无覆写配置重新启动核心');
          isStartSuccessful = await startCore(
            configPath: configPath,
            overrides: const [], // 不使用覆写
            mixedPort: mixedPort, // 混合端口
            isIpv6Enabled: isIpv6Enabled,
            isTunEnabled: isTunEnabled,
            tunStack: tunStack,
            tunDevice: tunDevice,
            isTunAutoRouteEnabled: isTunAutoRouteEnabled,
            isTunAutoRedirectEnabled: isTunAutoRedirectEnabled,
            isTunAutoDetectInterfaceEnabled: isTunAutoDetectInterfaceEnabled,
            tunDnsHijacks: tunDnsHijacks,
            isTunStrictRouteEnabled: isTunStrictRouteEnabled,
            tunRouteExcludeAddresses: tunRouteExcludeAddresses,
            isTunIcmpForwardingDisabled: isTunIcmpForwardingDisabled,
            tunMtu: tunMtu,
            isAllowLanEnabled: isAllowLanEnabled,
            isTcpConcurrentEnabled: isTcpConcurrentEnabled,
            geodataLoader: geodataLoader,
            findProcessMode: findProcessMode,
            clashCoreLogLevel: clashCoreLogLevel,
            externalController: externalController,
            isUnifiedDelayEnabled: isUnifiedDelayEnabled,
            outboundMode: outboundMode,
            socksPort: socksPort,
            httpPort: httpPort, // 单独 HTTP 端口
            enableFallback: false, // 禁用回退以避免递归
            onOverridesFailed: null,
          );

          if (isStartSuccessful) {
            Logger.info('回退成功：无覆写配置启动成功');
          } else {
            Logger.error('回退失败：即使没有覆写也无法启动');
          }
        } finally {
          // 重置回退标记(确保异常时也能重置)
          _isFallbackRetry = false;
        }
      }

      // 最终回退：如果配置文件启动失败，尝试使用默认配置
      if (!isStartSuccessful &&
          enableFallback &&
          !_isFallbackRetry &&
          configPath != null &&
          configPath.isNotEmpty) {
        Logger.error('配置文件启动失败，尝试使用默认配置回退');

        isStartSuccessful = await _fallbackToDefaultConfig(
          mixedPort: mixedPort,
          isIpv6Enabled: isIpv6Enabled,
          isTunEnabled: isTunEnabled,
          tunStack: tunStack,
          tunDevice: tunDevice,
          isTunAutoRouteEnabled: isTunAutoRouteEnabled,
          isTunAutoRedirectEnabled: isTunAutoRedirectEnabled,
          isTunAutoDetectInterfaceEnabled: isTunAutoDetectInterfaceEnabled,
          tunDnsHijacks: tunDnsHijacks,
          isTunStrictRouteEnabled: isTunStrictRouteEnabled,
          tunRouteExcludeAddresses: tunRouteExcludeAddresses,
          isTunIcmpForwardingDisabled: isTunIcmpForwardingDisabled,
          tunMtu: tunMtu,
          isAllowLanEnabled: isAllowLanEnabled,
          isTcpConcurrentEnabled: isTcpConcurrentEnabled,
          geodataLoader: geodataLoader,
          findProcessMode: findProcessMode,
          clashCoreLogLevel: clashCoreLogLevel,
          externalController: externalController,
          isUnifiedDelayEnabled: isUnifiedDelayEnabled,
          outboundMode: outboundMode,
          socksPort: socksPort,
          httpPort: httpPort,
          onDefaultConfigSuccess: onThirdLevelFallback,
        );
      }

      return isStartSuccessful;
    } catch (e) {
      Logger.error('启动 Clash 失败：$e');

      // 如果配置文件导致异常,尝试使用默认配置回退
      if (enableFallback &&
          !_isFallbackRetry &&
          configPath != null &&
          configPath.isNotEmpty) {
        Logger.error('配置文件导致启动异常，尝试使用默认配置回退');

        final isStartSuccessful = await _fallbackToDefaultConfig(
          mixedPort: mixedPort,
          isIpv6Enabled: isIpv6Enabled,
          isTunEnabled: isTunEnabled,
          tunStack: tunStack,
          tunDevice: tunDevice,
          isTunAutoRouteEnabled: isTunAutoRouteEnabled,
          isTunAutoRedirectEnabled: isTunAutoRedirectEnabled,
          isTunAutoDetectInterfaceEnabled: isTunAutoDetectInterfaceEnabled,
          tunDnsHijacks: tunDnsHijacks,
          isTunStrictRouteEnabled: isTunStrictRouteEnabled,
          tunRouteExcludeAddresses: tunRouteExcludeAddresses,
          isTunIcmpForwardingDisabled: isTunIcmpForwardingDisabled,
          tunMtu: tunMtu,
          isAllowLanEnabled: isAllowLanEnabled,
          isTcpConcurrentEnabled: isTcpConcurrentEnabled,
          geodataLoader: geodataLoader,
          findProcessMode: findProcessMode,
          clashCoreLogLevel: clashCoreLogLevel,
          externalController: externalController,
          isUnifiedDelayEnabled: isUnifiedDelayEnabled,
          outboundMode: outboundMode,
          socksPort: socksPort,
          httpPort: httpPort,
          onDefaultConfigSuccess: onThirdLevelFallback,
        );

        return isStartSuccessful;
      }

      // 无法回退,直接返回失败
      _updateCoreState(CoreState.stopped); // '启动失败');
      _actualPortsUsed = null;
      return false;
    } finally {
      // 如果还在启动状态但没有成功，设置为停止状态
      if (_coreState == CoreState.starting) {
        _updateCoreState(CoreState.stopped); // '启动未完成');
      }
    }
  }

  // 检查服务是否可用
  // 直接使用 ServiceProvider 的缓存状态，避免重复 IPC 调用
  bool _checkServiceAvailable() {
    try {
      // 使用 ServiceManager 单例
      final serviceManager = ServiceManager.instance;
      final isServiceModeInstalled = serviceManager.isServiceModeInstalled;

      // 关键：只要服务已安装（stopped 或 running 都可以），就可以使用服务模式
      // 因为通过 IPC 发送 StartClash 命令时，服务会自动启动
      return isServiceModeInstalled;
    } catch (e) {
      Logger.error('检查服务状态失败：$e');
      return false;
    }
  }

  // 通过服务启动 Clash 核心
  Future<bool> _startWithService(
    String runtimeConfigPath,
    String externalController,
    String? originalConfigPath,
  ) async {
    try {
      final execPath = await ProcessManager.getExecutablePath();
      final clashDataDir = await GeoService.getGeoDataDir();

      // 发送启动命令给服务
      StartClash(
        corePath: execPath,
        configPath: runtimeConfigPath,
        dataDir: clashDataDir,
        externalController: externalController,
      ).sendSignalToRust();

      // 等待服务响应
      // 超时设置为 30 秒：服务需要启动进程、加载配置、初始化 API 等操作
      final signal = await ClashProcessResult.rustSignalStream.first.timeout(
        const Duration(seconds: 30),
        onTimeout: () {
          throw TimeoutException('服务启动核心超时（30秒）');
        },
      );

      if (!signal.message.isSuccessful) {
        final error = signal.message.errorMessage ?? '未知错误';
        throw Exception('服务启动核心失败：$error');
      }

      // 记录启动模式
      _currentStartMode = ClashStartMode.service;
      _onStartModeChanged?.call(ClashStartMode.service);

      // 等待 IPC API 就绪（与普通模式保持一致）
      Logger.info('等待服务模式下的 IPC API 就绪…');
      await _apiClient.waitForReady(
        maxRetries: ClashDefaults.apiReadyMaxRetries,
        retryInterval: Duration(
          milliseconds: ClashDefaults.apiReadyRetryInterval,
        ),
      );

      // 等待 API 就绪并初始化（服务模式使用传递的 externalController 地址）
      return await _initializeAfterStart(
        externalController,
        originalConfigPath,
      );
    } catch (e) {
      Logger.error('服务模式启动失败：$e');
      return false;
    }
  }

  // 通过普通模式启动 Clash 核心
  Future<bool> _startWithSidecar(
    String runtimeConfigPath,
    int mixedPort,
    int? socksPort,
    int? httpPort,
    String externalController,
    String? originalConfigPath,
  ) async {
    try {
      final execPath = await ProcessManager.getExecutablePath();

      final portsToCheck = <int>[ClashDefaults.apiPort, mixedPort];
      if (socksPort != null) {
        portsToCheck.add(socksPort);
      }
      if (httpPort != null) {
        portsToCheck.add(httpPort);
      }

      _actualPortsUsed = List.from(portsToCheck);

      // 并行启动：进程启动的同时开始轮询 API
      Logger.info('并行启动 Clash 进程和 API 轮询…');

      await Future.wait([
        // 任务 1: 启动进程
        _processManager.startProcess(
          executablePath: execPath,
          configPath: runtimeConfigPath,
          apiHost: ClashDefaults.apiHost,
          apiPort: ClashDefaults.apiPort,
          portsToCheck: portsToCheck,
        ),

        // 任务 2: 立即开始轮询 API（不等进程启动完成）
        Future.delayed(
          Duration.zero,
          () => _apiClient.waitForReady(
            maxRetries: ClashDefaults.apiReadyMaxRetries,
            retryInterval: Duration(
              milliseconds: ClashDefaults.apiReadyRetryInterval,
            ),
          ),
        ),
      ]);

      Logger.info('Clash 进程和 API 均已就绪');

      // 记录启动模式
      _currentStartMode = ClashStartMode.sidecar;
      _onStartModeChanged?.call(ClashStartMode.sidecar);

      // 完成后续初始化（获取版本、启动监控）
      return await _initializeAfterStart(
        externalController,
        originalConfigPath,
      );
    } catch (e) {
      Logger.error('普通模式启动失败：$e');

      // 确保进程被终止（API 超时时进程可能还在运行）
      try {
        final portsToRelease = _actualPortsUsed ?? <int>[ClashDefaults.apiPort];
        await _processManager.stopProcess(
          timeout: Duration(seconds: ClashDefaults.processKillTimeout),
          portsToRelease: portsToRelease,
        );
      } catch (stopError) {
        Logger.warning('清理失败的进程时出错：$stopError');
      }

      _actualPortsUsed = null;
      return false;
    }
  }

  // 启动后初始化（获取版本、启动监控服务）
  Future<bool> _initializeAfterStart(
    String externalController,
    String? configPath,
  ) async {
    try {
      // API 已在并行启动时就绪，但 Named Pipe 可能还需要一点时间创建
      // 等待 IPC 就绪（通过重试获取版本号）
      final version = await _waitForIpcReady();
      if (version != null) {
        _coreVersion = version;
        _onCoreVersionChanged?.call(version);
      } else {
        Logger.warning('未能通过 IPC 获取版本号');
        _coreVersion = 'Unknown';
        _onCoreVersionChanged?.call('Unknown');
      }

      // 构建 API base URL
      final baseUrl = externalController.isNotEmpty
          ? 'http://$externalController'
          : 'http://${ClashDefaults.apiHost}:${ClashDefaults.apiPort}';

      try {
        await _trafficMonitor.startMonitoring(baseUrl);
      } catch (e) {
        Logger.error('启动流量监控失败：$e');
      }

      try {
        await _logService.startMonitoring(baseUrl);
      } catch (e) {
        Logger.error('启动日志服务失败：$e');
      }

      // 标记为运行状态
      _updateCoreState(CoreState.running); // '核心启动成功');

      // 更新当前配置路径（启动成功后才更新，失败则不更新）
      _originalConfigPath = configPath;
      _onConfigPathChanged?.call(configPath);
      Logger.debug('更新当前配置路径：${configPath ?? "null（使用默认配置）"}');

      // 服务模式下启动心跳定时器
      if (_currentStartMode == ClashStartMode.service) {
        startServiceHeartbeat();
      }

      _notifyListeners();
      Logger.info(
        'Clash 核心启动成功（${_currentStartMode == ClashStartMode.service ? "服务模式" : "普通模式"}）',
      );
      return true;
    } catch (e) {
      Logger.error('初始化失败：$e');
      _updateCoreState(CoreState.stopped); // '初始化失败');
      return false;
    }
  }

  // 验证启动后的配置是否正确
  // 检测覆写导致的配置错误（例如：代理组引用不存在的节点）
  Future<bool> _validateStartupConfig() async {
    try {
      Logger.debug('验证启动配置…');

      // 1. 检查核心是否能正常响应基本 API 调用
      try {
        await _apiClient.getVersion().timeout(
          const Duration(seconds: 3),
          onTimeout: () => throw TimeoutException('获取版本超时'),
        );
        Logger.debug('API 响应验证成功');
      } catch (e) {
        Logger.error('API 响应验证失败：$e');
        return false;
      }

      // 2. 检查是否能成功获取基本配置（最重要的验证）
      final config = await _apiClient.getConfig().timeout(
        const Duration(seconds: 5),
        onTimeout: () => throw TimeoutException('获取配置超时'),
      );

      if (config.isEmpty) {
        Logger.error('配置验证失败：配置为空');
        return false;
      }

      // 仅验证核心配置，不验证代理列表
      // 代理数据由 ClashProvider 异步加载
      // 配置验证的职责是确保核心能正常工作
      // 代理列表的验证由 ClashProvider 负责

      Logger.info('配置验证成功（核心功能验证）');
      return true;
    } catch (e) {
      Logger.error('配置验证失败：$e');
      return false;
    }
  }

  // 等待 IPC (Named Pipe) 就绪
  // Clash 核心启动后，Named Pipe 的创建需要 1-2 秒
  // 返回获取到的版本号（如果成功）
  Future<String?> _waitForIpcReady() async {
    for (int i = 0; i < ClashDefaults.ipcReadyMaxRetries; i++) {
      try {
        // 尝试调用一个简单的 API 来检查 IPC 是否可用
        final version = await _apiClient.getVersion();
        Logger.debug('IPC 已就绪（第 ${i + 1} 次尝试），版本：$version');
        return version; // IPC 可用，返回版本号
      } catch (e) {
        if (i < ClashDefaults.ipcReadyMaxRetries - 1) {
          // 还有重试机会，继续等待
          await Future.delayed(
            Duration(milliseconds: ClashDefaults.ipcReadyRetryInterval),
          );
        } else {
          // 最后一次尝试也失败了
          Logger.warning('等待 IPC 就绪超时，继续初始化（可能导致部分功能不可用）');
        }
      }
    }
    return null;
  }

  // 使用默认配置启动（回退机制的最后手段）
  // 返回 true 表示启动成功，false 表示失败
  Future<bool> _fallbackToDefaultConfig({
    required int mixedPort,
    required bool isIpv6Enabled,
    required bool isTunEnabled,
    required String tunStack,
    required String tunDevice,
    required bool isTunAutoRouteEnabled,
    required bool isTunAutoRedirectEnabled,
    required bool isTunAutoDetectInterfaceEnabled,
    required List<String> tunDnsHijacks,
    required bool isTunStrictRouteEnabled,
    required List<String> tunRouteExcludeAddresses,
    required bool isTunIcmpForwardingDisabled,
    required int tunMtu,
    required bool isAllowLanEnabled,
    required bool isTcpConcurrentEnabled,
    required String geodataLoader,
    required String findProcessMode,
    required String clashCoreLogLevel,
    required String externalController,
    required bool isUnifiedDelayEnabled,
    required String outboundMode,
    int? socksPort,
    int? httpPort,
    Future<void> Function()? onDefaultConfigSuccess,
  }) async {
    _isFallbackRetry = true;

    try {
      // 确保核心已停止
      if (isCoreRunning) {
        Logger.debug('停止核心以准备使用默认配置启动');
        await stopCore();
      } else {
        // 如果核心未运行，确保状态正确
        _updateCoreState(CoreState.stopped); // '准备使用默认配置启动');
        _actualPortsUsed = null;
      }

      // 等待资源释放
      await Future.delayed(const Duration(milliseconds: 500));

      // 使用默认配置启动
      Logger.info('使用默认配置启动核心（无订阅节点）');
      final success = await startCore(
        configPath: null, // 使用默认配置
        overrides: const [], // 不使用覆写
        mixedPort: mixedPort,
        isIpv6Enabled: isIpv6Enabled,
        isTunEnabled: isTunEnabled,
        tunStack: tunStack,
        tunDevice: tunDevice,
        isTunAutoRouteEnabled: isTunAutoRouteEnabled,
        isTunAutoRedirectEnabled: isTunAutoRedirectEnabled,
        isTunAutoDetectInterfaceEnabled: isTunAutoDetectInterfaceEnabled,
        tunDnsHijacks: tunDnsHijacks,
        isTunStrictRouteEnabled: isTunStrictRouteEnabled,
        tunRouteExcludeAddresses: tunRouteExcludeAddresses,
        isTunIcmpForwardingDisabled: isTunIcmpForwardingDisabled,
        tunMtu: tunMtu,
        isAllowLanEnabled: isAllowLanEnabled,
        isTcpConcurrentEnabled: isTcpConcurrentEnabled,
        geodataLoader: geodataLoader,
        findProcessMode: findProcessMode,
        clashCoreLogLevel: clashCoreLogLevel,
        externalController: externalController,
        isUnifiedDelayEnabled: isUnifiedDelayEnabled,
        outboundMode: outboundMode,
        socksPort: socksPort,
        httpPort: httpPort,
        enableFallback: false, // 禁用回退以避免递归
        onOverridesFailed: null,
        onThirdLevelFallback: null, // 内部调用不需要回调
      );

      if (success) {
        Logger.info('默认配置启动成功');

        // 调用成功回调（用于清除 currentSubscription）
        if (onDefaultConfigSuccess != null) {
          try {
            await onDefaultConfigSuccess();
          } catch (e) {
            Logger.error('默认配置启动成功回调执行失败：$e');
          }
        }
      } else {
        Logger.error('默认配置启动失败，这不应该发生！请检查 Clash 核心文件或系统环境');
      }

      return success;
    } finally {
      _isFallbackRetry = false;
    }
  }

  // 停止 Clash 核心（不触碰系统代理）
  Future<bool> stopCore() async {
    if (isCoreStopping) {
      Logger.warning('Clash 正在停止中，请勿重复调用');
      return false;
    }

    if (!isCoreRunning) {
      Logger.info('Clash 未在运行');
      return true;
    }

    _updateCoreState(CoreState.stopping); // '开始停止核心');

    try {
      // 先停止监控服务（优雅关闭 WebSocket 连接）
      // 避免 Clash 核心停止后强制断开连接导致的错误日志
      try {
        await _trafficMonitor.stopMonitoring();
      } catch (e) {
        Logger.error('停止流量监控失败：$e');
      }

      try {
        await _logService.stopMonitoring();
      } catch (e) {
        Logger.error('停止日志服务失败：$e');
      }

      // 再停止 Clash 核心
      if (_currentStartMode == ClashStartMode.service) {
        Logger.info('使用服务模式停止核心');
        stopServiceHeartbeat(); // 停止心跳定时器
        await _stopWithService();
      } else {
        Logger.info('使用普通模式停止核心');
        await _stopWithSidecar();
      }

      // 清理状态
      _updateCoreState(CoreState.stopped); // '核心已停止');
      _actualPortsUsed = null;
      _coreVersion = 'Unknown';
      _currentStartMode = null;
      _onStartModeChanged?.call(null);

      _notifyListeners();
      Logger.info('Clash 核心已停止');
      return true;
    } catch (e) {
      Logger.error('停止 Clash 失败：$e');
      return false;
    } finally {
      // 确保状态正确
      if (_coreState == CoreState.stopping) {
        _updateCoreState(CoreState.stopped); // '停止操作完成');
      }
    }
  }

  // 通过服务停止 Clash 核心
  Future<void> _stopWithService() async {
    try {
      StopClash().sendSignalToRust();

      // 等待服务响应
      // 超时设置为 10 秒：停止操作仅需终止进程，应该较快完成
      final signal = await ClashProcessResult.rustSignalStream.first.timeout(
        const Duration(seconds: 10),
        onTimeout: () {
          throw TimeoutException('服务停止核心超时（10秒）');
        },
      );

      if (!signal.message.isSuccessful) {
        final error = signal.message.errorMessage ?? '未知错误';
        Logger.warning('服务停止核心失败：$error');
      }
    } catch (e) {
      Logger.warning('服务模式停止失败，尝试直接终止进程：$e');
      // 如果服务停止失败，尝试直接终止进程
      await _stopWithSidecar();
    }
  }

  // 通过普通模式停止 Clash 核心
  Future<void> _stopWithSidecar() async {
    List<int> portsToRelease;
    if (_actualPortsUsed != null && _actualPortsUsed!.isNotEmpty) {
      portsToRelease = _actualPortsUsed!;
    } else {
      Logger.warning('未找到启动时的端口记录，使用当前配置的端口');
      portsToRelease = <int>[ClashDefaults.apiPort];
    }

    await _processManager.stopProcess(
      timeout: Duration(seconds: ClashDefaults.processKillTimeout),
      portsToRelease: portsToRelease,
    );
  }

  // 启动服务心跳定时器（仅服务模式使用）
  void startServiceHeartbeat() {
    _serviceHeartbeatTimer?.cancel();

    // 立即发送第一次心跳，避免服务启动后等待30秒导致超时
    Logger.debug('发送服务心跳（立即）');
    SendServiceHeartbeat().sendSignalToRust();

    _serviceHeartbeatTimer = Timer.periodic(const Duration(seconds: 30), (
      timer,
    ) {
      Logger.debug('发送服务心跳');
      SendServiceHeartbeat().sendSignalToRust();
    });
    Logger.info('服务心跳定时器已启动（30秒间隔）');
  }

  // 停止服务心跳定时器
  void stopServiceHeartbeat() {
    if (_serviceHeartbeatTimer != null) {
      _serviceHeartbeatTimer!.cancel();
      _serviceHeartbeatTimer = null;
      Logger.info('服务心跳定时器已停止');
    }
  }

  void dispose() {
    _serviceHeartbeatTimer?.cancel();
  }

  // 检查 TUN 权限
  Future<bool> _checkTunPermission() async {
    try {
      // 检查服务模式是否已安装
      final serviceManager = ServiceManager.instance;
      if (serviceManager.isServiceModeInstalled) {
        return true;
      }

      // 检查是否以管理员/root 权限运行
      final isElevated = await PermissionService.isElevated();
      return isElevated;
    } catch (e) {
      Logger.error('检查 TUN 权限失败：$e');
      return false;
    }
  }

  // 强制重置核心状态（服务安装/卸载时调用）
  void forceResetCoreState() {
    _updateCoreState(CoreState.stopped);
    _actualPortsUsed = null;
  }
}
