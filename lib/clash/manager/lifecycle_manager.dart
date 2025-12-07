import 'dart:async';
import 'dart:io';
import 'package:stelliberty/clash/network/api_client.dart';
import 'package:stelliberty/clash/services/process_service.dart';
import 'package:stelliberty/clash/config/config_injector.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/services/traffic_monitor.dart';
import 'package:stelliberty/clash/services/log_service.dart';
import 'package:stelliberty/clash/services/geo_service.dart';
import 'package:stelliberty/clash/providers/service_provider.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/clash/core/core_state.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/utils/logger.dart';

// Clash 启动模式
enum ClashStartMode {
  // 普通模式（应用直接启动进程）
  sidecar,

  // 服务模式（通过服务启动）
  service,
}

// Clash 生命周期管理器
// 负责 Clash 核心的启动、停止、重启
class LifecycleManager {
  final ProcessService _processService;
  final ClashApiClient _apiClient;
  final TrafficMonitor _trafficMonitor;
  final ClashLogService _logService;
  final Function() _notifyListeners;
  final Function() _refreshAllStatusBatch;

  // 核心状态管理器
  final CoreStateManager _coreStateManager = CoreStateManager.instance;

  // 回退标记（标记是否正在进行覆写回退）
  bool _isFallbackRetry = false;

  // 当前启动模式
  ClashStartMode? _currentStartMode;
  ClashStartMode? get currentStartMode => _currentStartMode;

  // 原始订阅文件路径（用于回退和重启）
  String? _originalConfigPath;
  String? get currentConfigPath => _originalConfigPath;

  // 更新当前配置路径（用于热重载后同步路径）
  void updateConfigPath(String? configPath) {
    if (configPath != null && configPath.isNotEmpty) {
      _originalConfigPath = configPath;
      Logger.debug('配置路径已更新：$configPath');
    }
  }

  // 启动时实际使用的端口列表（用于停止时准确释放）
  List<int>? _actualPortsUsed;

  // 防抖定时器
  Timer? _restartDebounceTimer;
  // 服务心跳定时器
  Timer? _serviceHeartbeatTimer;

  // 状态缓存
  String _version = 'Unknown';
  String get version => _version;

  // 运行状态（通过状态管理器获取）
  bool get isCoreRunning => _coreStateManager.currentState.isRunning;
  bool get isRestarting =>
      _coreStateManager.currentState == CoreState.restarting;
  bool get isStarting => _coreStateManager.currentState == CoreState.starting;
  bool get isStopping => _coreStateManager.currentState == CoreState.stopping;

  LifecycleManager({
    required ProcessService processService,
    required ClashApiClient apiClient,
    required TrafficMonitor trafficMonitor,
    required ClashLogService logService,
    required Function() notifyListeners,
    required Function() refreshAllStatusBatch,
  }) : _processService = processService,
       _apiClient = apiClient,
       _trafficMonitor = trafficMonitor,
       _logService = logService,
       _notifyListeners = notifyListeners,
       _refreshAllStatusBatch = refreshAllStatusBatch;

  // 启动 Clash 核心（不触碰系统代理）
  //
  // 参数：
  // - configPath: 配置文件路径（可选，为空时使用保存的原始路径）
  // - overrides: 覆写配置列表（由 ClashManager 通过回调获取）
  // - enableFallback: 是否启用覆写失败回退（默认 true）
  // - onOverridesFailed: 覆写失败时的回调（用于禁用覆写）
  Future<bool> startCore({
    String? configPath,
    List<OverrideConfig> overrides = const [],
    bool enableFallback = true,
    Future<void> Function()? onOverridesFailed,
    required int mixedPort, // 混合端口
    required bool ipv6,
    required bool tunEnable,
    required String tunStack,
    required String tunDevice,
    required bool tunAutoRoute,
    required bool tunAutoRedirect,
    required bool tunAutoDetectInterface,
    required List<String> tunDnsHijack,
    required bool tunStrictRoute,
    required List<String> tunRouteExcludeAddress,
    required bool tunDisableIcmpForwarding,
    required int tunMtu,
    required bool allowLan,
    required bool tcpConcurrent,
    required String geodataLoader,
    required String findProcessMode,
    required String clashCoreLogLevel,
    required String externalController,
    required bool unifiedDelay,
    required String outboundMode,
    int? socksPort,
    int? httpPort, // 单独 HTTP 端口（可选）
  }) async {
    if (isStarting) {
      Logger.warning('Clash 正在启动中，请勿重复调用');
      return false;
    }

    if (isRestarting) {
      Logger.warning('Clash 正在重启中，请勿手动启动');
      return false;
    }

    if (isCoreRunning) {
      Logger.info('Clash 已在运行');
      return true;
    }

    _coreStateManager.setStarting(reason: '开始启动核心');

    try {
      // 如果传入的 configPath 为空，尝试使用原始订阅路径（重启场景）
      if ((configPath == null || configPath.isEmpty) &&
          _originalConfigPath != null &&
          _originalConfigPath!.isNotEmpty) {
        // 检查原始配置文件是否存在
        final originalFile = File(_originalConfigPath!);
        if (await originalFile.exists()) {
          Logger.info('重启时未提供配置路径，使用原始订阅路径：$_originalConfigPath');
          configPath = _originalConfigPath;
        } else {
          Logger.warning('原始订阅配置文件已不存在：$_originalConfigPath，将使用默认配置或等待新配置');
          _originalConfigPath = null; // 清空过期的路径
        }
      }

      // ⚠️ 保存原始订阅路径（用于重启和回退）
      if (configPath != null && configPath.isNotEmpty) {
        _originalConfigPath = configPath;
      }

      // 生成运行时配置（支持无配置路径时使用默认配置）
      final generatedConfigPath = await ConfigInjector.injectCustomConfigParams(
        configPath: configPath,
        overrides: overrides,
        httpPort: mixedPort, // 传递混合端口给配置注入器
        ipv6: ipv6,
        tunEnable: tunEnable,
        tunStack: tunStack,
        tunDevice: tunDevice,
        tunAutoRoute: tunAutoRoute,
        tunAutoRedirect: tunAutoRedirect,
        tunAutoDetectInterface: tunAutoDetectInterface,
        tunDnsHijack: tunDnsHijack,
        tunStrictRoute: tunStrictRoute,
        tunRouteExcludeAddress: tunRouteExcludeAddress,
        tunDisableIcmpForwarding: tunDisableIcmpForwarding,
        tunMtu: tunMtu,
        allowLan: allowLan,
        tcpConcurrent: tcpConcurrent,
        geodataLoader: geodataLoader,
        findProcessMode: findProcessMode,
        clashCoreLogLevel: clashCoreLogLevel,
        externalController: externalController,
        externalControllerSecret: ClashPreferences.instance
            .getExternalControllerSecret(),
        unifiedDelay: unifiedDelay,
        outboundMode: outboundMode,
      );

      if (generatedConfigPath == null) {
        throw Exception('配置生成失败');
      }

      final runtimeConfigPath = generatedConfigPath;

      // 检查服务是否可用
      final serviceAvailable = _checkServiceAvailable();

      bool startSuccess = false;

      if (serviceAvailable) {
        // 服务模式启动
        Logger.info('使用服务模式启动 Clash 核心');
        startSuccess = await _startWithService(
          runtimeConfigPath,
          externalController,
        );
      } else {
        // 普通模式启动
        Logger.info('使用普通模式启动 Clash 核心');
        startSuccess = await _startWithSidecar(
          runtimeConfigPath,
          mixedPort, // 混合端口
          socksPort,
          httpPort, // 单独 HTTP 端口
          externalController,
        );
      }

      // 处理启动失败或配置验证失败的情况
      bool needFallback = false; // 标记是否需要回退

      if (startSuccess) {
        // 启动成功，验证配置是否正确加载
        final configValid = await _validateStartupConfig();

        if (!configValid &&
            enableFallback &&
            !_isFallbackRetry &&
            overrides.isNotEmpty) {
          Logger.error('配置验证失败，可能由覆写导致，尝试禁用覆写后重试');
          needFallback = true;
          startSuccess = false; // 标记为失败
        }
      } else if (enableFallback && !_isFallbackRetry && overrides.isNotEmpty) {
        // 启动失败且有覆写
        needFallback = true;
      }

      // 如果需要回退，执行回退逻辑（只调用一次回调）
      if (needFallback) {
        Logger.error('启动失败，检测到有覆写配置，执行回退');

        // 标记为回退重试
        _isFallbackRetry = true;

        // 确保核心已停止（可能已经停止或根本没启动成功）
        if (isCoreRunning) {
          await stopCore();
        }

        // 调用覆写失败回调（禁用当前订阅的覆写） - 只调用一次
        if (onOverridesFailed != null) {
          Logger.warning('调用覆写失败回调，禁用所有覆写');
          await onOverridesFailed();
        }

        // 等待一段时间确保资源释放
        await Future.delayed(const Duration(milliseconds: 500));

        // 重置状态，允许递归调用
        _coreStateManager.setStopped(reason: '回退准备重启');

        // 重新启动（不带覆写，且禁用回退以避免无限循环）
        Logger.info('使用无覆写配置重新启动核心');
        startSuccess = await startCore(
          configPath: configPath,
          overrides: const [], // 不使用覆写
          mixedPort: mixedPort, // 混合端口
          ipv6: ipv6,
          tunEnable: tunEnable,
          tunStack: tunStack,
          tunDevice: tunDevice,
          tunAutoRoute: tunAutoRoute,
          tunAutoRedirect: tunAutoRedirect,
          tunAutoDetectInterface: tunAutoDetectInterface,
          tunDnsHijack: tunDnsHijack,
          tunStrictRoute: tunStrictRoute,
          tunRouteExcludeAddress: tunRouteExcludeAddress,
          tunDisableIcmpForwarding: tunDisableIcmpForwarding,
          tunMtu: tunMtu,
          allowLan: allowLan,
          tcpConcurrent: tcpConcurrent,
          geodataLoader: geodataLoader,
          findProcessMode: findProcessMode,
          clashCoreLogLevel: clashCoreLogLevel,
          externalController: externalController,
          unifiedDelay: unifiedDelay,
          outboundMode: outboundMode,
          socksPort: socksPort,
          httpPort: httpPort, // 单独 HTTP 端口
          enableFallback: false, // 禁用回退以避免递归
          onOverridesFailed: null,
        );

        // 重置回退标记
        _isFallbackRetry = false;

        if (startSuccess) {
          Logger.info('回退成功：无覆写配置启动成功');
        } else {
          Logger.error('回退失败：即使没有覆写也无法启动');
        }
      }

      return startSuccess;
    } catch (e) {
      Logger.error('启动 Clash 失败：$e');
      _coreStateManager.setStopped(reason: '启动失败');
      _actualPortsUsed = null;
      return false;
    } finally {
      // 如果还在启动状态但没有成功，设置为停止状态
      if (_coreStateManager.currentState == CoreState.starting) {
        _coreStateManager.setStopped(reason: '启动未完成');
      }
    }
  }

  // 检查服务是否可用
  // 直接使用 ServiceProvider 的缓存状态，避免重复 IPC 调用
  bool _checkServiceAvailable() {
    try {
      // 使用 ServiceProvider 单例
      final serviceProvider = ServiceProvider();
      final isInstalled = serviceProvider.isInstalled;
      final status = serviceProvider.status;

      Logger.debug('检查服务状态：已安装=$isInstalled，状态=$status');

      // 关键：只要服务已安装（stopped 或 running 都可以），就可以使用服务模式
      // 因为通过 IPC 发送 StartClash 命令时，服务会自动启动
      return isInstalled;
    } catch (e) {
      Logger.error('检查服务状态失败：$e');
      return false;
    }
  }

  // 通过服务启动 Clash 核心
  Future<bool> _startWithService(
    String configPath,
    String externalController,
  ) async {
    try {
      final execPath = await ProcessService.getExecutablePath();
      final clashDataDir = await GeoService.getGeoDataDir();

      // 发送启动命令给服务
      StartClash(
        corePath: execPath,
        configPath: configPath,
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

      if (!signal.message.success) {
        final error = signal.message.errorMessage ?? '未知错误';
        throw Exception('服务启动核心失败：$error');
      }

      // 记录启动模式
      _currentStartMode = ClashStartMode.service;

      // 等待 IPC API 就绪（与普通模式保持一致）
      Logger.info('等待服务模式下的 IPC API 就绪…');
      await _apiClient.waitForReady(
        maxRetries: ClashDefaults.apiReadyMaxRetries,
        retryInterval: Duration(
          milliseconds: ClashDefaults.apiReadyRetryInterval,
        ),
      );

      // 等待 API 就绪并初始化（服务模式使用传递的 externalController 地址）
      return await _initializeAfterStart(externalController);
    } catch (e) {
      Logger.error('服务模式启动失败：$e');
      return false;
    }
  }

  // 通过普通模式启动 Clash 核心
  Future<bool> _startWithSidecar(
    String configPath,
    int mixedPort,
    int? socksPort,
    int? httpPort,
    String externalController,
  ) async {
    try {
      final execPath = await ProcessService.getExecutablePath();

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
        _processService.start(
          executablePath: execPath,
          configPath: configPath,
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

      // 完成后续初始化（获取版本、启动监控）
      return await _initializeAfterStart(externalController);
    } catch (e) {
      Logger.error('普通模式启动失败：$e');

      // 确保进程被终止（API 超时时进程可能还在运行）
      try {
        final portsToRelease = _actualPortsUsed ?? <int>[ClashDefaults.apiPort];
        await _processService.stop(
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
  Future<bool> _initializeAfterStart(String externalController) async {
    try {
      // API 已在并行启动时就绪，但 Named Pipe 可能还需要一点时间创建
      // 等待 IPC 就绪（通过重试获取版本号）
      final version = await _waitForIpcReady();
      if (version != null) {
        _version = version;
      } else {
        Logger.warning('未能通过 IPC 获取版本号');
        _version = 'Unknown';
      }

      try {
        await _refreshAllStatusBatch();
      } catch (e) {
        Logger.error('获取配置状态失败：$e');
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
      _coreStateManager.setRunning(reason: '核心启动成功');

      // 服务模式下启动心跳定时器
      if (_currentStartMode == ClashStartMode.service) {
        _startServiceHeartbeat();
      }

      _notifyListeners();
      Logger.info(
        'Clash 核心启动成功（${_currentStartMode == ClashStartMode.service ? "服务模式" : "普通模式"}）',
      );
      return true;
    } catch (e) {
      Logger.error('初始化失败：$e');
      _coreStateManager.setStopped(reason: '初始化失败');
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

      // 注意：不再验证代理列表，因为：
      // 1. 代理数据由 ClashProvider 异步加载，时序不确定
      // 2. 配置验证的职责是确保核心能正常工作，而不是验证业务数据
      // 3. 代理列表的验证应该由 ClashProvider 负责

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

  // 停止 Clash 核心（不触碰系统代理）
  Future<bool> stopCore() async {
    if (isStopping) {
      Logger.warning('Clash 正在停止中，请勿重复调用');
      return false;
    }

    if (isRestarting) {
      Logger.warning('Clash 正在重启中，请勿手动停止');
      return false;
    }

    if (!isCoreRunning) {
      Logger.info('Clash 未在运行');
      return true;
    }

    _coreStateManager.setStopping(reason: '开始停止核心');

    try {
      cancelPendingRestart();

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
        _stopServiceHeartbeat(); // 停止心跳定时器
        await _stopWithService();
      } else {
        Logger.info('使用普通模式停止核心');
        await _stopWithSidecar();
      }

      // 清理状态
      _coreStateManager.setStopped(reason: '核心已停止');
      _actualPortsUsed = null;
      _version = 'Unknown';
      _currentStartMode = null;

      _notifyListeners();
      Logger.info('Clash 核心已停止');
      return true;
    } catch (e) {
      Logger.error('停止 Clash 失败：$e');
      return false;
    } finally {
      // 确保状态正确
      if (_coreStateManager.currentState == CoreState.stopping) {
        _coreStateManager.setStopped(reason: '停止操作完成');
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

      if (!signal.message.success) {
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

    await _processService.stop(
      timeout: Duration(seconds: ClashDefaults.processKillTimeout),
      portsToRelease: portsToRelease,
    );
  }

  // 重启 Clash（用于应用启动前配置参数）
  Future<void> restartToApplyConfig({
    Duration? debounceDelay,
    String reason = '应用配置更改',
    List<OverrideConfig> overrides = const [],
    required Future<bool> Function() startCallback,
  }) async {
    final actualDelay =
        debounceDelay ??
        Duration(milliseconds: ClashDefaults.restartDebounceMs);

    _restartDebounceTimer?.cancel();

    _restartDebounceTimer = Timer(actualDelay, () async {
      await _performRestart(reason, startCallback);
    });

    Logger.info('已安排重启 Clash（${actualDelay.inMilliseconds}ms 后）：$reason');
  }

  // 执行实际的重启操作
  Future<void> _performRestart(
    String reason,
    Future<bool> Function() startCallback,
  ) async {
    if (isRestarting) {
      Logger.warning('Clash 正在重启中，跳过重复请求');
      return;
    }

    if (!isCoreRunning) {
      Logger.info('Clash 未在运行，无需重启');
      return;
    }

    if (_originalConfigPath == null) {
      Logger.error('无法重启：未找到配置文件路径');
      return;
    }

    _coreStateManager.setRestarting(reason: reason);
    _notifyListeners();

    try {
      Logger.info('重启 Clash - 原因：$reason');

      Logger.info('正在停止 Clash 核心…');
      final stopSuccess = await stopCore();
      if (!stopSuccess) {
        throw Exception('停止 Clash 核心失败');
      }

      await Future.delayed(
        Duration(milliseconds: ClashDefaults.restartIntervalMs),
      );

      Logger.info('正在重新启动 Clash 核心…');
      final startSuccess = await startCallback();
      if (!startSuccess) {
        throw Exception('重新启动 Clash 核心失败');
      }

      Logger.info('Clash 核心重启成功（系统代理状态未变更）');
    } catch (e) {
      Logger.error('Clash 重启失败：$e');
    } finally {
      // 确保状态正确 - 如果还在重启状态，根据实际情况设置
      if (_coreStateManager.currentState == CoreState.restarting) {
        // 注意：这里不能用 isRunning 判断，因为当前状态还是 restarting
        // 需要通过其他方式判断是否真正在运行，比如检查启动模式
        if (_currentStartMode != null) {
          _coreStateManager.setRunning(reason: '重启完成');
        } else {
          _coreStateManager.setStopped(reason: '重启失败');
        }
      }
      _notifyListeners();
    }
  }

  // 取消待执行的重启
  void cancelPendingRestart() {
    if (_restartDebounceTimer != null) {
      Logger.info('已取消待执行的 Clash 重启');
    }
    _restartDebounceTimer?.cancel();
    _restartDebounceTimer = null;
  }

  // 启动服务心跳定时器（仅服务模式使用）
  void _startServiceHeartbeat() {
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
  void _stopServiceHeartbeat() {
    if (_serviceHeartbeatTimer != null) {
      _serviceHeartbeatTimer!.cancel();
      _serviceHeartbeatTimer = null;
      Logger.info('服务心跳定时器已停止');
    }
  }

  void dispose() {
    _restartDebounceTimer?.cancel();
    _serviceHeartbeatTimer?.cancel();
  }
}
