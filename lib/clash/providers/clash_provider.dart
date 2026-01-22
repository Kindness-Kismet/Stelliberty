import 'dart:async';
import 'package:flutter/widgets.dart';
import 'package:stelliberty/clash/manager/clash_manager.dart';
import 'package:stelliberty/clash/state/core_states.dart';
import 'package:stelliberty/clash/state/config_states.dart';
import 'package:stelliberty/clash/model/clash_model.dart';
import 'package:stelliberty/clash/model/traffic_data_model.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/clash/services/config_watcher.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart' as signals;

// Clash 状态管理：通过 ClashManager 单例维护全局核心状态。
// 向 UI 暴露可观察状态与常用操作。
class ClashProvider extends ChangeNotifier with WidgetsBindingObserver {
  ClashManager get _clashManager => ClashManager.instance;

  // ClashManager 实例
  ClashManager get clashManager => _clashManager;

  // 核心状态
  CoreState _coreState = CoreState.stopped;
  CoreState get coreState => _coreState;
  DateTime? _coreStartedAt;
  DateTime? get coreStartedAt => _coreStartedAt;

  String _coreVersion = 'Unknown';
  String get coreVersion => _coreVersion;

  String? _currentConfigPath;
  String? get currentConfigPath => _currentConfigPath;

  ClashStartMode? _currentStartMode;
  ClashStartMode? get currentStartMode => _currentStartMode;

  // 系统代理状态
  bool _isSystemProxyEnabled = false;
  bool get isSystemProxyEnabled => _isSystemProxyEnabled;

  void _updateSystemProxyState(bool enabled) {
    if (_isSystemProxyEnabled == enabled) return;

    _isSystemProxyEnabled = enabled;
    Logger.debug('系统代理状态更新：$enabled');
    notifyListeners();
  }

  // 配置状态
  ConfigState _configState = const ConfigState();
  ConfigState get configState => _configState;

  // 从持久化刷新配置状态
  void refreshConfigState() {
    _configState = ConfigState.fromPreferences(ClashPreferences.instance);
    Logger.debug('配置状态已从持久化刷新');
    notifyListeners();
  }

  // 内部同步配置状态
  void _syncConfigFromManager() {
    _configState = ConfigState.fromPreferences(ClashPreferences.instance);
    Logger.debug('配置状态已从持久化同步');
  }

  bool get isAllowLanEnabled => _configState.isAllowLanEnabled;
  bool get isIpv6Enabled => _configState.isIpv6Enabled;
  bool get isTcpConcurrentEnabled => _configState.isTcpConcurrentEnabled;
  bool get isUnifiedDelayEnabled => _configState.isUnifiedDelayEnabled;
  String get geodataLoader => _configState.geodataLoader;
  String get findProcessMode => _configState.findProcessMode;
  String get clashCoreLogLevel => _configState.clashCoreLogLevel;
  String get externalController => _configState.externalController;
  bool get isExternalControllerEnabled =>
      _configState.isExternalControllerEnabled;
  String get testUrl => _configState.testUrl;
  String get outboundMode => _configState.outboundMode;
  bool get isTunEnabled => _configState.isTunEnabled;
  String get tunStack => _configState.tunStack;
  String get tunDevice => _configState.tunDevice;
  bool get isTunAutoRouteEnabled => _configState.isTunAutoRouteEnabled;
  bool get isTunAutoRedirectEnabled => _configState.isTunAutoRedirectEnabled;
  bool get isTunAutoDetectInterfaceEnabled =>
      _configState.isTunAutoDetectInterfaceEnabled;
  List<String> get tunDnsHijacks => _configState.tunDnsHijacks;
  bool get isTunStrictRouteEnabled => _configState.isTunStrictRouteEnabled;
  List<String> get tunRouteExcludeAddresses =>
      _configState.tunRouteExcludeAddresses;
  bool get isTunIcmpForwardingDisabled =>
      _configState.isTunIcmpForwardingDisabled;
  int get tunMtu => _configState.tunMtu;
  int get mixedPort => _configState.mixedPort;
  int? get socksPort => _configState.socksPort;
  int? get httpPort => _configState.httpPort;

  // 运行状态（基于 CoreState）
  bool get isCoreRunning => _coreState.isRunning;
  bool get isCoreRestarting => _coreState == CoreState.restarting;
  bool get isCoreStarting => _coreState == CoreState.starting;
  bool get isCoreStopping => _coreState == CoreState.stopping;

  // 更新核心状态
  void _updateCoreState(CoreState nextState) {
    if (_coreState == nextState) return;

    final wasRunning = _coreState.isRunning;
    _coreState = nextState;

    if (!wasRunning && nextState.isRunning) {
      _coreStartedAt = DateTime.now();
    } else if (wasRunning && !nextState.isRunning) {
      _coreStartedAt = null;
    }

    // 核心启动成功后，刷新配置状态
    if (nextState == CoreState.running) {
      _syncConfigFromManager();
    }

    notifyListeners();
  }

  // 更新核心版本
  void _updateCoreVersion(String version) {
    if (_coreVersion == version) return;

    _coreVersion = version;
    notifyListeners();
  }

  // 更新当前配置路径
  void _updateCurrentConfigPath(String? configPath) {
    if (_currentConfigPath == configPath) return;

    _currentConfigPath = configPath;
    notifyListeners();
  }

  // 其他状态

  // 流量数据流（转发自 ClashManager）
  Stream<TrafficData>? get trafficStream => _clashManager.trafficStream;

  // 代理组列表（所有）
  List<ProxyGroup> _allProxyGroups = [];

  List<ProxyGroup> get allProxyGroups => _allProxyGroups;

  // 可见的代理组列表（缓存）
  List<ProxyGroup>? _cachedProxyGroups;

  // 可见的代理组列表（根据出站模式过滤）
  List<ProxyGroup> get proxyGroups {
    // 如果缓存存在，直接返回
    if (_cachedProxyGroups != null) {
      return _cachedProxyGroups!;
    }

    // 获取当前出站模式
    final outboundMode = _configState.outboundMode;

    // 根据模式过滤代理组
    _cachedProxyGroups = switch (outboundMode) {
      'direct' => [], // 直连模式：不显示任何代理组
      'global' =>
        _allProxyGroups // 全局模式：显示所有非隐藏组 + GLOBAL 组
            .where((group) => !group.isHidden || group.name == 'GLOBAL')
            .toList(),
      _ =>
        _allProxyGroups // 规则模式（默认）：只显示非隐藏的代理组，过滤掉 GLOBAL
            .where((group) => !group.isHidden)
            .where((group) => group.name != 'GLOBAL')
            .toList(),
    };

    return _cachedProxyGroups!;
  }

  // 清除缓存（在数据变化时调用）
  void _invalidateCache() {
    _cachedProxyGroups = null;
  }

  // 所有代理节点
  Map<String, ProxyNode> _proxyNodes = {};

  // 获取所有代理节点（只读）
  Map<String, ProxyNode> get proxyNodes => _proxyNodes;

  // proxyNodes 更新计数器（用于触发 Selector 重建）
  int _proxyNodesUpdateCount = 0;
  int get proxyNodesUpdateCount => _proxyNodesUpdateCount;

  // 正在测试延迟的节点集合
  final Set<String> _testingNodes = {};

  // 获取正在测试延迟的节点集合（只读）
  Set<String> get testingNodes => _testingNodes;

  // 是否正在批量测试延迟
  bool _isBatchTestingDelay = false;

  // 获取是否正在批量测试延迟
  bool get isBatchTestingDelay => _isBatchTestingDelay;

  // UI 更新节流：记录上次通知时间
  DateTime? _lastNotifiedAt;
  // UI 更新节流间隔（毫秒）
  static const int _notifyThrottleMs = 100;

  // 延迟值过期定时器（节点名 → Timer）
  final Map<String, Timer> _delayExpireTimers = {};
  // 延迟值保留时长（5 分钟）
  static const Duration _delayRetentionDuration = Duration(minutes: 5);

  // 批量延迟测试信号订阅（防止泄漏）
  StreamSubscription? _progressSubscription;
  StreamSubscription? _completeSubscription;

  // selections 内存缓存：记录每个代理组当前选中的节点
  final Map<String, String> _selections = {};

  Map<String, String> get selections => _selections;

  String? _selectedGroupName;
  String? get selectedGroupName => _selectedGroupName;
  ProxyGroup? get selectedGroup {
    if (_selectedGroupName == null) return null;
    return proxyGroups.firstWhere(
      (group) => group.name == _selectedGroupName,
      orElse: () => proxyGroups.first,
    );
  }

  // 并发保护：使用 Completer 确保同一时间只有一个加载操作
  Completer<void>? _loadProxiesCompleter;

  // 错误信息
  String? _errorMessage;
  String? get errorMessage => _errorMessage;

  // 配置文件监听器
  ConfigWatcher? _configWatcher;

  // 配置文件重载功能是否已启用
  bool _isConfigReloadEnabled = true;
  bool get isConfigReloadEnabled => _isConfigReloadEnabled;

  // 判断代理组类型是否支持手动选择
  static bool _isSelectableGroupType(String type) {
    final lowerType = type.toLowerCase();
    return lowerType == 'selector' ||
        lowerType == 'select' ||
        lowerType == 'urltest' ||
        lowerType == 'fallback';
  }

  // 判断 IPC 未就绪错误（启动期间或系统唤醒后的短暂状态）。
  static bool _isIpcNotReadyError(String errorMessage) {
    // Windows：os error 2；Linux：os error 111；macOS：os error 61。
    return errorMessage.contains('os error 2') ||
        errorMessage.contains('os error 111') ||
        errorMessage.contains('os error 61') ||
        (errorMessage.contains('系统找不到指定的文件') &&
            errorMessage.contains('pipe')) ||
        (errorMessage.contains('Connection refused') &&
            errorMessage.contains('IPC'));
  }

  ClashProvider() {
    // 初始同步配置状态（从 ConfigManager 拉取）
    _syncConfigFromManager();

    // 设置状态变化回调（从各个 Manager 同步状态到 Provider）
    _clashManager.setStateChangeCallbacks(
      onCoreStateChanged: _updateCoreState,
      onCoreVersionChanged: _updateCoreVersion,
      onConfigPathChanged: _updateCurrentConfigPath,
      onStartModeChanged: (mode) {
        if (_currentStartMode != mode) {
          _currentStartMode = mode;
          notifyListeners();
        }
      },
      onSystemProxyStateChanged: _updateSystemProxyState,
    );

    // Provider 在调用 Manager 方法后手动通知

    // 注册应用生命周期监听
    WidgetsBinding.instance.addObserver(this);
  }

  // 初始化
  Future<void> initialize(String? configPath) async {
    Logger.info('ClashProvider 初始化完成');
  }

  // 应用生命周期状态变化时触发
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    super.didChangeAppLifecycleState(state);

    // 当应用从后台恢复时，刷新代理状态以同步外部控制器的节点切换
    if (state == AppLifecycleState.resumed) {
      Logger.debug('应用恢复，刷新代理数据（全局）');
      if (isCoreRunning) {
        // 刷新配置状态
        refreshConfigState();
        // 刷新代理状态以同步外部控制器的节点切换
        refreshProxiesFromClash();
      }
    }
  }

  // 检查延迟测试是否可用
  // 用于 UI 显示提示信息
  bool get isDelayTestAvailable => isCoreRunning;

  // 获取延迟测试状态描述（用于调试和用户提示）
  String getDelayTestStatus() {
    if (!isCoreRunning) {
      return 'Clash 未运行，请先启动 Clash';
    }

    return '延迟测试已就绪';
  }

  // 启动 Clash 核心（不触碰系统代理）
  // 调用者需要自行决定是否启用系统代理
  Future<bool> start({String? configPath}) async {
    _errorMessage = null;

    try {
      // 获取覆写配置（如果有回调）
      final overrides = _clashManager.getOverrides();

      final success = await _clashManager.startCore(
        configPath: configPath,
        overrides: overrides,
      );

      if (success) {
        // 启动后必须从 API 重新加载代理列表
        Logger.info('Clash 已启动，从 API 重新加载代理列表');
        await loadProxies();

        // 获取实际使用的配置路径（可能因回退而与传入的 configPath 不同）
        // 通过回调已经同步到 _currentConfigPath
        final actualConfigPath = _currentConfigPath;

        // 如果启用了配置重载且实际使用了配置文件（非默认配置），启动配置文件监听
        if (_isConfigReloadEnabled &&
            actualConfigPath != null &&
            actualConfigPath.isNotEmpty) {
          await _startConfigWatcher(actualConfigPath);
        } else if (actualConfigPath == null) {
          Logger.info('核心使用默认配置启动，跳过配置文件监听');
        }
      } else {
        Logger.error('Clash 启动失败');
      }
      return success;
    } catch (e) {
      _errorMessage = '启动 Clash 失败：$e';
      Logger.error(_errorMessage!);
      return false;
    }
  }

  // 重启 Clash 核心（保持当前配置）
  // 自动保存当前配置路径并在重启后恢复
  Future<bool> restart() async {
    _errorMessage = null;

    // 保存当前配置路径（必须在 stop 前获取）
    final currentConfigPath = _currentConfigPath;

    try {
      // 先停止配置文件监听
      await _stopConfigWatcher();

      final stopSuccess = await _clashManager.stopCore();
      if (!stopSuccess) {
        _errorMessage = '停止 Clash 失败';
        Logger.error(_errorMessage!);
        return false;
      }

      // 等待端口完全释放
      await Future.delayed(const Duration(milliseconds: 300));

      // 获取覆写配置
      final overrides = _clashManager.getOverrides();

      // 使用保存的配置路径重新启动
      final success = await _clashManager.startCore(
        configPath: currentConfigPath,
        overrides: overrides,
      );

      if (success) {
        Logger.info('Clash 已重启');
        await loadProxies();

        // 获取实际使用的配置路径（通过回调已同步到 _currentConfigPath）
        final actualConfigPath = _currentConfigPath;

        // 如果启用了配置重载且实际使用了配置文件，启动配置文件监听
        if (_isConfigReloadEnabled &&
            actualConfigPath != null &&
            actualConfigPath.isNotEmpty) {
          await _startConfigWatcher(actualConfigPath);
        }
      } else {
        Logger.error('Clash 重启失败');
      }

      return success;
    } catch (e) {
      _errorMessage = '重启 Clash 失败：$e';
      Logger.error(_errorMessage!);
      return false;
    }
  }

  // 停止 Clash 核心（不触碰系统代理）
  // 调用者需要自行决定是否禁用系统代理
  Future<bool> stop({String? configPath}) async {
    _errorMessage = null;

    try {
      // 先停止配置文件监听
      await _stopConfigWatcher();

      final success = await _clashManager.stopCore();

      // 停止后无需重新加载配置文件
      // 本地状态保持不变

      return success;
    } catch (e) {
      _errorMessage = '停止 Clash 失败：$e';
      Logger.error(_errorMessage!);
      return false;
    }
  }

  // 同步本地选择到 Clash（启动时调用）
  // 将本地状态中的所有代理选择应用到 Clash API
  Future<void> _syncProxyGroupSelections() async {
    if (!isCoreRunning) return;

    try {
      Logger.info('应用本地代理选择到 Clash...');
      final errors = <String>[];

      // 应用所有本地选择到 Clash
      for (final group in _allProxyGroups) {
        // 只对支持手动选择的代理组类型进行切换
        if (!_isSelectableGroupType(group.type)) {
          continue;
        }

        if (group.now != null && group.now!.isNotEmpty) {
          try {
            await _clashManager.changeProxy(group.name, group.now!);
          } catch (e) {
            final errorMsg = '应用选择失败 ${group.name}：$e';
            Logger.warning(errorMsg);
            errors.add(errorMsg);
          }
        }
      }

      if (errors.isNotEmpty) {
        _errorMessage = '部分代理选择应用失败：\n${errors.join('\n')}';
        notifyListeners();
      }

      Logger.info('本地代理选择已全部应用到 Clash');
    } catch (e) {
      _errorMessage = '同步代理选择失败：$e';
      Logger.error(_errorMessage!);
      notifyListeners();
    }
  }

  // 加载代理列表
  // 使用 Completer 模式防止并发调用
  Future<void> loadProxies() async {
    // 并发保护：如果已经有加载操作在进行，等待它完成
    if (_loadProxiesCompleter != null) {
      Logger.debug('代理列表正在加载中，等待完成…');
      return _loadProxiesCompleter!.future;
    }

    // 创建 Completer
    _loadProxiesCompleter = Completer<void>();

    try {
      await _doLoadProxies();
      _loadProxiesCompleter!.complete();
    } catch (e) {
      _loadProxiesCompleter!.completeError(e);
      rethrow;
    } finally {
      _loadProxiesCompleter = null;
    }
  }

  // 从 Clash 刷新代理状态（不恢复本地选择，而是将 Clash 的当前状态保存到本地）
  // 用于应用从后台恢复时同步外部控制器的节点切换
  Future<void> refreshProxiesFromClash() async {
    // 并发保护：如果已经有加载操作在进行，等待它完成
    if (_loadProxiesCompleter != null) {
      Logger.debug('代理列表正在加载中，等待完成…');
      return _loadProxiesCompleter!.future;
    }

    // 创建新的 Completer
    _loadProxiesCompleter = Completer<void>();

    try {
      await _doRefreshProxiesFromClash();
      _loadProxiesCompleter!.complete();
    } catch (e) {
      _loadProxiesCompleter!.completeError(e);
      rethrow;
    } finally {
      _loadProxiesCompleter = null;
    }
  }

  // 实际的加载逻辑
  Future<void> _doLoadProxies() async {
    Logger.info('开始加载代理列表');

    // 【性能监控】总耗时
    final totalStopwatch = Stopwatch()..start();

    if (!isCoreRunning) {
      Logger.info('Clash 未在运行，无法加载代理列表');
      return;
    }

    Logger.debug(
      '加载前状态：代理组=${_allProxyGroups.length}，节点=${_proxyNodes.length}',
    );

    _errorMessage = null;

    try {
      // 【性能监控】API 调用耗时
      final apiStopwatch = Stopwatch()..start();

      // 添加重试逻辑，避免因 Clash API 繁忙导致超时
      Map<String, dynamic>? proxies;
      int attemptCount = 0;
      const maxRetries = 2;

      while (attemptCount <= maxRetries) {
        attemptCount++;
        try {
          proxies = await _clashManager.getProxies();
          break; // 成功则跳出循环
        } catch (e) {
          final errorMsg = e.toString();
          final isLastAttempt = attemptCount > maxRetries;

          // 检查是否为 IPC 未就绪错误（启动时的正常情况）
          final isIpcNotReady = _isIpcNotReadyError(errorMsg);

          if (!isLastAttempt) {
            // 还有重试机会
            if (isIpcNotReady) {
              Logger.debug('IPC 尚未就绪（第 $attemptCount 次尝试），1 秒后重试');
            } else {
              Logger.warning('获取代理数据失败（第 $attemptCount 次尝试），1 秒后重试：$e');
            }
            await Future.delayed(const Duration(seconds: 1));
          } else {
            // 最后一次尝试失败
            if (isIpcNotReady) {
              Logger.debug('IPC仍未就绪，稍后自动重试（不显示错误）');
              return; // 静默失败，不设置 errorMessage
            } else {
              Logger.error('获取代理数据失败，已尝试 $attemptCount 次：$e');
              rethrow; // 真正的错误才抛出
            }
          }
        }
      }

      if (proxies == null) {
        throw Exception('获取代理数据失败');
      }

      apiStopwatch.stop();
      Logger.debug(
        '从 Clash API 获取代理数据完成：${proxies.length} 项（耗时：${apiStopwatch.elapsedMilliseconds}ms，尝试次数：$attemptCount）',
      );

      // 【性能监控】解析节点耗时
      final parseStopwatch = Stopwatch()..start();
      _proxyNodes = {};
      proxies.forEach((name, data) {
        final node = ProxyNode.fromJson(name, data);
        _proxyNodes[name] = node;
      });
      _proxyNodesUpdateCount++;
      parseStopwatch.stop();
      Logger.debug(
        '解析节点完成：${_proxyNodes.length} 个（耗时：${parseStopwatch.elapsedMilliseconds}ms）',
      );

      _allProxyGroups = [];
      _invalidateCache();

      final addedGroups = <String>{};
      final globalGroup = proxies['GLOBAL'];
      final hasGlobalAll = globalGroup?['all'] != null;

      // 阶段 1：优先添加 GLOBAL 组（如果存在）
      if (globalGroup != null) {
        // 代理组的特征：有 all 字段
        if (globalGroup['all'] != null) {
          _allProxyGroups.add(ProxyGroup.fromJson('GLOBAL', globalGroup));
          addedGroups.add('GLOBAL');
        }
      }

      // 阶段 2：按 GLOBAL.all 顺序添加其他代理组
      if (hasGlobalAll) {
        final orderedNames = List<String>.from(globalGroup!['all']);
        for (final groupName in orderedNames) {
          if (addedGroups.contains(groupName)) continue;

          final proxyData = proxies[groupName];
          if (proxyData == null) continue;

          // 代理组的特征：有 all 字段
          if (proxyData['all'] == null) continue;

          _allProxyGroups.add(ProxyGroup.fromJson(groupName, proxyData));
          addedGroups.add(groupName);
        }
      }

      // 阶段 3：补充遗漏的代理组
      proxies.forEach((name, data) {
        if (addedGroups.contains(name)) return;

        // 代理组的特征：有 all 字段
        if (data['all'] == null) return;

        _allProxyGroups.add(ProxyGroup.fromJson(name, data));
      });

      Logger.debug('解析完成：${_allProxyGroups.length} 个代理组');

      // 【性能监控】恢复选择耗时
      final restoreStopwatch = Stopwatch()..start();
      await _restoreProxySelections();
      restoreStopwatch.stop();
      Logger.debug('恢复节点选择完成（耗时：${restoreStopwatch.elapsedMilliseconds}ms）');

      // 默认选中第一个可见的代理组
      if (_selectedGroupName == null && proxyGroups.isNotEmpty) {
        _selectedGroupName = proxyGroups.first.name;
      }

      // 【性能监控】同步选择耗时
      final syncStopwatch = Stopwatch()..start();
      await _syncProxyGroupSelections();
      syncStopwatch.stop();
      Logger.debug('同步代理选择完成（耗时：${syncStopwatch.elapsedMilliseconds}ms）');
    } catch (e) {
      _errorMessage = '加载代理列表失败：$e';
      Logger.error(_errorMessage!);
    } finally {
      totalStopwatch.stop();
      Logger.info(
        '加载完成: ${_allProxyGroups.length} 个代理组（${proxyGroups.length} 可见），${_proxyNodes.length} 个节点（耗时：${totalStopwatch.elapsedMilliseconds}ms）',
      );
    }
  }

  // 从 Clash 刷新代理状态（不恢复本地选择，而是将 Clash 的当前状态保存到本地）
  // 用于应用从后台恢复时同步外部控制器的节点切换
  Future<void> _doRefreshProxiesFromClash() async {
    final totalStopwatch = Stopwatch()..start();

    if (!isCoreRunning) {
      Logger.info('Clash 未在运行，无法刷新代理状态');
      return;
    }

    Logger.debug(
      '刷新前状态：代理组=${_allProxyGroups.length}，节点=${_proxyNodes.length}',
    );

    _errorMessage = null;

    try {
      // 从 Clash API 获取代理数据
      final apiStopwatch = Stopwatch()..start();
      Map<String, dynamic>? proxies;
      int attemptCount = 0;
      const maxRetries = 2;

      while (attemptCount <= maxRetries) {
        attemptCount++;
        try {
          proxies = await _clashManager.getProxies();
          break;
        } catch (e) {
          final errorMsg = e.toString();
          final isLastAttempt = attemptCount > maxRetries;

          // 检查是否为 IPC 未就绪或连接失效错误
          final isIpcNotReady = _isIpcNotReadyError(errorMsg);

          if (!isLastAttempt) {
            // 还有重试机会
            if (isIpcNotReady) {
              Logger.debug('IPC 连接失效（第 $attemptCount 次尝试），1 秒后重试');
            } else {
              Logger.warning('获取代理数据失败（第 $attemptCount 次尝试），1 秒后重试：$e');
            }
            await Future.delayed(const Duration(seconds: 1));
          } else {
            // 最后一次尝试失败
            if (isIpcNotReady) {
              Logger.debug('IPC 连接仍失效，稍后自动重试（不显示错误）');
              return; // 静默失败，不设置 errorMessage
            } else {
              Logger.error('获取代理数据失败，已尝试 $attemptCount 次：$e');
              rethrow;
            }
          }
        }
      }

      if (proxies == null) {
        throw Exception('获取代理数据失败');
      }

      apiStopwatch.stop();
      Logger.debug(
        '从 Clash API 获取代理数据完成：${proxies.length} 项（耗时：${apiStopwatch.elapsedMilliseconds}ms，尝试次数：$attemptCount）',
      );

      // 解析节点
      final parseStopwatch = Stopwatch()..start();
      final previousProxyNodes = _proxyNodes; // 保存旧节点数据
      _proxyNodes = {};
      proxies.forEach((name, data) {
        final node = ProxyNode.fromJson(name, data);

        // 保留旧节点的延迟值和过期定时器
        final previousNode = previousProxyNodes[name];
        if (previousNode != null &&
            previousNode.delay != null &&
            previousNode.delay! != 0) {
          _proxyNodes[name] = node.copyWith(delay: previousNode.delay);
          // 注意：定时器中使用节点名查找，因此不需要重新创建定时器
        } else {
          _proxyNodes[name] = node;
        }
      });
      _proxyNodesUpdateCount++;
      parseStopwatch.stop();
      Logger.debug(
        '解析节点完成：${_proxyNodes.length} 个（耗时：${parseStopwatch.elapsedMilliseconds}ms）',
      );

      _allProxyGroups = [];
      _invalidateCache();

      final addedGroups = <String>{};
      final globalGroup = proxies['GLOBAL'];
      final hasGlobalAll = globalGroup?['all'] != null;

      // 阶段 1：优先添加 GLOBAL 组
      if (globalGroup != null) {
        // 代理组的特征：有 all 字段
        if (globalGroup['all'] != null) {
          _allProxyGroups.add(ProxyGroup.fromJson('GLOBAL', globalGroup));
          addedGroups.add('GLOBAL');
        }
      }

      // 阶段 2：按 GLOBAL.all 顺序添加其他代理组
      if (hasGlobalAll) {
        final orderedNames = List<String>.from(globalGroup!['all']);
        for (final groupName in orderedNames) {
          if (addedGroups.contains(groupName)) continue;

          final proxyData = proxies[groupName];
          if (proxyData == null) continue;

          // 代理组的特征：有 all 字段
          if (proxyData['all'] == null) continue;

          _allProxyGroups.add(ProxyGroup.fromJson(groupName, proxyData));
          addedGroups.add(groupName);
        }
      }

      // 阶段 3：补充遗漏的代理组
      proxies.forEach((name, data) {
        if (addedGroups.contains(name)) return;

        // 代理组的特征：有 all 字段
        if (data['all'] == null) return;

        _allProxyGroups.add(ProxyGroup.fromJson(name, data));
      });

      Logger.debug('解析完成：${_allProxyGroups.length} 个代理组');

      // 关键：将 Clash 的当前状态保存到本地存储（反向同步）
      final saveStopwatch = Stopwatch()..start();
      await _saveCurrentClashSelections();
      saveStopwatch.stop();
      Logger.debug(
        '保存 Clash 当前状态到本地完成（耗时：${saveStopwatch.elapsedMilliseconds}ms）',
      );

      // 默认选中第一个可见的代理组
      if (_selectedGroupName == null && proxyGroups.isNotEmpty) {
        _selectedGroupName = proxyGroups.first.name;
      }

      Logger.info(
        '刷新完成: ${_allProxyGroups.length} 个代理组（${proxyGroups.length} 可见），${_proxyNodes.length} 个节点',
      );
    } catch (e) {
      // 设置错误信息
      _errorMessage = '刷新代理状态失败：$e';
      Logger.error(_errorMessage!);
    } finally {
      notifyListeners();

      totalStopwatch.stop();
      Logger.info(
        '从 Clash 刷新代理状态完成（总耗时：${totalStopwatch.elapsedMilliseconds}ms）',
      );
    }
  }

  // 将 Clash 的当前状态保存到本地存储
  Future<void> _saveCurrentClashSelections() async {
    final currentSubscriptionId = ClashPreferences.instance
        .getCurrentSubscriptionId();
    if (currentSubscriptionId == null) {
      Logger.warning('无法保存 Clash 状态：当前订阅 ID 为空');
      return;
    }

    int savedCount = 0;
    _selections.clear();

    for (final group in _allProxyGroups) {
      if (!_isSelectableGroupType(group.type)) {
        continue;
      }

      // 使用 Clash 返回的当前选中节点
      if (group.now != null && group.now!.isNotEmpty) {
        await ClashPreferences.instance.saveProxySelection(
          currentSubscriptionId,
          group.name,
          group.now!,
        );
        _selections[group.name] = group.now!;
        savedCount++;
      }
    }

    Logger.info('节点选择保存完成：保存=$savedCount');
  }

  // 切换代理节点
  // 统一的状态管理：始终先更新本地状态，运行时才同步到 Clash
  Future<bool> changeProxy(String groupName, String proxyName) async {
    final groupIndex = _allProxyGroups.indexWhere((g) => g.name == groupName);
    if (groupIndex == -1) {
      Logger.warning('代理组不存在：$groupName');
      return false;
    }

    final group = _allProxyGroups[groupIndex];

    // 检查代理组类型是否支持手动选择
    if (!_isSelectableGroupType(group.type)) {
      Logger.warning('代理组 $groupName 类型为 ${group.type}，不支持手动切换节点');
      return false;
    }

    _allProxyGroups[groupIndex] = group.copyWith(now: proxyName);
    _invalidateCache();
    notifyListeners();
    Logger.debug('本地状态已更新：$groupName -> $proxyName');

    // 更新 selectedMap 缓存
    _selections[groupName] = proxyName;

    // 保存节点选择到持久化存储
    final currentSubscriptionId = ClashPreferences.instance
        .getCurrentSubscriptionId();
    if (currentSubscriptionId != null) {
      await ClashPreferences.instance.saveProxySelection(
        currentSubscriptionId,
        groupName,
        proxyName,
      );
      Logger.info(
        '节点选择已保存：订阅=$currentSubscriptionId，组=$groupName，节点=$proxyName',
      );
    } else {
      Logger.warning('无法保存节点选择：当前订阅 ID 为空');
    }

    // 2. 如果 Clash 在运行，异步同步到 Clash（不阻塞 UI）
    if (isCoreRunning) {
      try {
        final success = await _clashManager.changeProxy(groupName, proxyName);
        if (success) {
          Logger.debug('已同步到 Clash：$groupName -> $proxyName');
        } else {
          Logger.warning('同步到 Clash 失败，但本地状态已更新');
        }
        return success;
      } catch (e) {
        Logger.warning('同步到 Clash 出错：$e，但本地状态已更新');
        // 不设置 errorMessage，因为本地状态已经更新成功
        return false;
      }
    }

    // 3. 未运行时，只更新本地状态即可
    return true;
  }

  // 恢复已保存的节点选择
  Future<void> _restoreProxySelections() async {
    final currentSubscriptionId = ClashPreferences.instance
        .getCurrentSubscriptionId();
    if (currentSubscriptionId == null) {
      Logger.warning('无法恢复节点选择：当前订阅 ID 为空');
      return;
    }

    int restoredCount = 0;
    int defaultCount = 0;

    _selections.clear();

    for (int i = 0; i < _allProxyGroups.length; i++) {
      final group = _allProxyGroups[i];

      if (!_isSelectableGroupType(group.type)) {
        continue;
      }

      String? selected;

      // 1. 优先从持久化存储恢复
      selected = ClashPreferences.instance.getProxySelection(
        currentSubscriptionId,
        group.name,
      );

      // 2. 如果没有保存记录，且核心正在运行，使用当前状态
      if (selected == null && isCoreRunning && group.now != null) {
        selected = group.now;
      }

      // 3. 验证选择的有效性
      if (selected != null && !group.all.contains(selected)) {
        Logger.warning('保存的节点 $selected 在组 ${group.name} 中不存在');
        selected = null;
      }

      // 4. 回退到默认值
      if (selected == null && group.all.isNotEmpty) {
        selected = group.all.first;
        defaultCount++;
      } else if (selected != null) {
        restoredCount++;
      }

      // 更新代理组的 now 字段
      if (selected != null && selected != group.now) {
        _allProxyGroups[i] = group.copyWith(now: selected);
      }

      // 同时更新 selectedMap
      if (selected != null) {
        _selections[group.name] = selected;
      }
    }

    _invalidateCache();

    if (restoredCount > 0 || defaultCount > 0) {
      Logger.info('节点选择恢复完成：恢复=$restoredCount，默认=$defaultCount');
    }
  }

  // 选择代理组
  void selectGroup(String groupName) {
    _selectedGroupName = groupName;
    notifyListeners();
  }

  // 启用/禁用配置文件重载
  void setConfigReload(bool enabled) {
    _isConfigReloadEnabled = enabled;
    Logger.info('配置文件重载：${enabled ? "已启用" : "已禁用"}');
  }

  // 启动配置文件监听
  Future<void> _startConfigWatcher(String configPath) async {
    // 停止监听器
    await _stopConfigWatcher();

    // 创建监听器
    _configWatcher = ConfigWatcher(
      onReload: () async {
        Logger.info('检测到配置文件变化，重新生成运行时配置并重载…');

        // 1. 重新生成 runtime_config.yaml 并重载 Clash 配置
        final reloadSuccess = await _clashManager.reloadConfig(
          configPath: configPath,
        );

        if (reloadSuccess) {
          // 取消正在进行的延迟测试
          cancelBatchDelayTest();

          // 清空所有延迟结果
          clearAllDelayResults();

          Logger.info('配置已重载，延迟测试已取消并清空结果');

          // 2. 重新加载代理列表（显示新节点）
          await loadProxies();
        } else {
          Logger.error('配置重载失败，跳过代理列表更新');
        }
      },
      debounceMs: 1000, // 1 秒防抖
    );

    await _configWatcher!.watch(configPath);
    Logger.info('配置文件监听已启动：$configPath');
  }

  // 停止配置文件监听
  Future<void> _stopConfigWatcher() async {
    if (_configWatcher != null) {
      await _configWatcher!.stop();
      _configWatcher = null;
      Logger.info('配置文件监听已停止');
    }
  }

  // 暂停配置文件监听（用于订阅更新期间避免重复触发）
  void pauseConfigWatcher() {
    _configWatcher?.pause();
  }

  // 恢复配置文件监听
  Future<void> resumeConfigWatcher() async {
    await _configWatcher?.resume();
  }

  ProxyNode? getProxyNode(String name) {
    return _proxyNodes[name];
  }

  // ========== 延迟测试方法 ==========

  // 测试代理延迟（支持代理组）
  Future<int> testProxyDelay(
    String proxyName, [
    String? testUrl,
    bool notify = true,
  ]) async {
    final node = _proxyNodes[proxyName];
    if (node == null) {
      Logger.warning('代理节点不存在：$proxyName');
      return -1;
    }

    final delay = await _clashManager.testProxyDelayViaRust(
      proxyName,
      testUrl: testUrl,
    );

    if (notify) {
      _proxyNodes[proxyName] = node.copyWith(delay: delay);
      _proxyNodes = Map<String, ProxyNode>.from(_proxyNodes);
      _proxyNodesUpdateCount++;
      notifyListeners();
    }

    return delay;
  }

  // 批量测试代理组中所有节点的延迟
  Future<void> testGroupDelays(String groupName, [String? testUrl]) async {
    if (_isBatchTestingDelay) {
      Logger.warning('批量测试正在进行中，忽略重复请求');
      return;
    }

    final group = _allProxyGroups.firstWhere(
      (g) => g.name == groupName,
      orElse: () => throw Exception('Group not found: $groupName'),
    );

    // 获取所有要测试的代理名称
    final proxyNames = group.all.where((proxyName) {
      final node = _proxyNodes[proxyName];
      return node != null;
    }).toList();

    // 标记批量测试开始
    _isBatchTestingDelay = true;
    _testingNodes.clear();
    _testingNodes.addAll(proxyNames);
    _lastNotifiedAt = null; // 重置节流计时器
    notifyListeners();

    // 标记是否有待通知的更新
    bool hasPendingUpdates = false;

    try {
      await _clashManager.testGroupDelays(
        proxyNames,
        testUrl: testUrl,
        onNodeStart: (nodeName) {
          // 节点开始测试时保持在 testingNodes 中
          // 无需额外操作，因为已经在集合中了
        },
        onNodeComplete: (nodeName, delay) {
          // 节点测试完成，立即更新延迟值
          final node = _proxyNodes[nodeName];
          if (node != null) {
            _proxyNodes[nodeName] = node.copyWith(delay: delay);
            hasPendingUpdates = true; // 标记有更新
          }

          // 从测试集合中移除
          _testingNodes.remove(nodeName);

          // 节流通知 UI 更新（每 100ms 最多一次）
          final now = DateTime.now();
          if (hasPendingUpdates &&
              (_lastNotifiedAt == null ||
                  now.difference(_lastNotifiedAt!).inMilliseconds >=
                      _notifyThrottleMs)) {
            // 仅在有更新时才创建新 Map（触发 Selector 重建）
            _proxyNodes = Map<String, ProxyNode>.from(_proxyNodes);
            _proxyNodesUpdateCount++;
            notifyListeners();
            _lastNotifiedAt = now;
            hasPendingUpdates = false; // 清除待更新标记
          }
        },
      );
    } finally {
      // 确保最后一次更新（包含所有节点的最终结果）
      if (hasPendingUpdates) {
        _proxyNodes = Map<String, ProxyNode>.from(_proxyNodes);
        _proxyNodesUpdateCount++;
      }
      _testingNodes.clear();
      _isBatchTestingDelay = false;
      _lastNotifiedAt = null;
      notifyListeners();
    }
  }

  // 批量测试所有代理节点的延迟
  Future<void> testAllProxiesDelays([String? testUrl]) async {
    if (_isBatchTestingDelay) {
      Logger.warning('批量测试正在进行中，忽略重复请求');
      return;
    }

    // 收集所有代理节点名称（去重）
    final allProxyNames = <String>{};
    for (final group in _allProxyGroups) {
      allProxyNames.addAll(
        group.all.where((proxyName) {
          final node = _proxyNodes[proxyName];
          return node != null;
        }),
      );
    }

    if (allProxyNames.isEmpty) {
      Logger.warning('没有可测试的代理节点');
      return;
    }

    // 标记批量测试开始
    _isBatchTestingDelay = true;
    _testingNodes.clear();
    _testingNodes.addAll(allProxyNames);
    _lastNotifiedAt = null; // 重置节流计时器
    notifyListeners();

    // 标记是否有待通知的更新
    bool hasPendingUpdates = false;

    try {
      // 使用 Rust 层批量测试
      final proxyNames = allProxyNames.toList();

        // 使用延迟测试并发数
        final concurrency = ClashDefaults.delayTestConcurrency;
      final timeoutMs = ClashDefaults.proxyDelayTestTimeout;
      final url = testUrl ?? ClashDefaults.defaultTestUrl;

      Logger.info('开始批量测试所有节点延迟，共 ${proxyNames.length} 个节点，并发数：$concurrency');

      // 取消旧订阅（防止泄漏）
      await _progressSubscription?.cancel();
      await _completeSubscription?.cancel();

      final completer = Completer<void>();

      try {
        // 订阅进度信号（流式更新）
        _progressSubscription = signals.DelayTestProgress.rustSignalStream
            .listen((result) {
              final nodeName = result.message.nodeName;
              final delayMs = result.message.delayMs;

              // 更新节点延迟
              final node = _proxyNodes[nodeName];
              if (node != null) {
                _proxyNodes[nodeName] = node.copyWith(delay: delayMs);
                hasPendingUpdates = true;

                // 如果延迟测试完成（无论成功或超时），设置 5 分钟过期定时器
                if (delayMs != 0) {
                  // 取消过期定时器
                  _delayExpireTimers[nodeName]?.cancel();

                  // 设置过期定时器（5 分钟后清空延迟值）
                  _delayExpireTimers[nodeName] = Timer(
                    _delayRetentionDuration,
                    () {
                      final node = _proxyNodes[nodeName];
                      if (node != null &&
                          node.delay != null &&
                          node.delay! != 0) {
                        _proxyNodes[nodeName] = node.copyWith(delay: 0);
                        _proxyNodes = Map<String, ProxyNode>.from(_proxyNodes);
                        _proxyNodesUpdateCount++;
                        notifyListeners();
                        Logger.debug('节点 $nodeName 延迟值已过期（5 分钟）');
                      }
                      _delayExpireTimers.remove(nodeName);
                    },
                  );
                }
              }

              // 从测试集合中移除
              _testingNodes.remove(nodeName);

              // 节流通知 UI 更新（每 100ms 最多一次）
              final now = DateTime.now();
              if (hasPendingUpdates &&
                  (_lastNotifiedAt == null ||
                      now.difference(_lastNotifiedAt!).inMilliseconds >=
                          _notifyThrottleMs)) {
                // 仅在有更新时才创建新 Map（触发 Selector 重建）
                _proxyNodes = Map<String, ProxyNode>.from(_proxyNodes);
                _proxyNodesUpdateCount++;
                notifyListeners();
                _lastNotifiedAt = now;
                hasPendingUpdates = false;
              }
            });

        // 订阅完成信号
        _completeSubscription = signals.BatchDelayTestComplete.rustSignalStream
            .listen((result) {
              final message = result.message;
              if (message.isSuccessful) {
                Logger.info(
                  '所有节点延迟测试完成，成功：${message.successCount}/${message.totalCount}',
                );
                completer.complete();
              } else {
                Logger.error(
                  '批量延迟测试失败（Rust 层）：${message.errorMessage ?? "未知错误"}',
                );
                completer.completeError(
                  Exception(message.errorMessage ?? '批量延迟测试失败'),
                );
              }
            });

        // 发送批量测试请求到 Rust 层
        signals.BatchDelayTestRequest(
          nodeNames: proxyNames,
          testUrl: url,
          timeoutMs: timeoutMs,
          concurrency: concurrency,
        ).sendSignalToRust();

        // 等待测试完成（最多等待：节点数 × 单个超时 + 10 秒缓冲）
        final maxWaitTime = Duration(
          milliseconds: (proxyNames.length * timeoutMs) + 10000,
        );
        await completer.future.timeout(
          maxWaitTime,
          onTimeout: () {
            throw Exception('批量延迟测试超时');
          },
        );
      } finally {
        // 取消订阅
        await _progressSubscription?.cancel();
        await _completeSubscription?.cancel();
        _progressSubscription = null;
        _completeSubscription = null;
      }
    } finally {
      // 确保最后一次更新（包含所有节点的最终结果）
      if (hasPendingUpdates) {
        _proxyNodes = Map<String, ProxyNode>.from(_proxyNodes);
        _proxyNodesUpdateCount++;
      }
      _testingNodes.clear();
      _isBatchTestingDelay = false;
      _lastNotifiedAt = null;
      notifyListeners();
    }
  }

  // 更新节点延迟
  void updateNodeDelay(String nodeName, int delay) {
    final node = _proxyNodes[nodeName];
    if (node != null) {
      _proxyNodes[nodeName] = node.copyWith(delay: delay);
      _proxyNodesUpdateCount++;
      notifyListeners();
    }
  }

  // 取消批量延迟测试
  void cancelBatchDelayTest() {
    if (_isBatchTestingDelay) {
      Logger.info('取消批量延迟测试');
      _isBatchTestingDelay = false;
      _testingNodes.clear();
      notifyListeners();
    }
  }

  // 清空所有延迟测试结果
  void clearAllDelayResults() {
    Logger.info('清空所有延迟测试结果');

    // 取消所有过期定时器
    for (final timer in _delayExpireTimers.values) {
      timer.cancel();
    }
    _delayExpireTimers.clear();

    // 清空延迟值（包括成功和超时的延迟值）
    for (final nodeName in _proxyNodes.keys.toList()) {
      final node = _proxyNodes[nodeName];
      if (node != null && node.delay != null && node.delay! != 0) {
        _proxyNodes[nodeName] = node.copyWith(delay: 0);
      }
    }

    _proxyNodes = Map<String, ProxyNode>.from(_proxyNodes);
    _proxyNodesUpdateCount++;
    notifyListeners();
  }

  // ========== 系统代理和 TUN 模式控制 ==========

  // 切换 TUN 模式
  Future<bool> setTunMode(bool enabled) async {
    try {
      Logger.info('切换虚拟网卡模式：${enabled ? "启用" : "禁用"}');
      final success = await _clashManager.setTunEnabled(enabled);
      if (success) {
        // 主动从 Manager 同步配置状态
        _syncConfigFromManager();
        notifyListeners();
      }
      return success;
    } catch (e) {
      Logger.error('切换虚拟网卡模式失败：$e');
      return false;
    }
  }

  // 启用系统代理
  Future<void> enableSystemProxy() async {
    try {
      Logger.info('启用系统代理');
      await _clashManager.enableSystemProxy();
    } catch (e) {
      Logger.error('启用系统代理失败：$e');
      rethrow;
    }
  }

  // 禁用系统代理
  Future<void> disableSystemProxy() async {
    try {
      Logger.info('禁用系统代理');
      await _clashManager.disableSystemProxy();
    } catch (e) {
      Logger.error('禁用系统代理失败：$e');
      rethrow;
    }
  }

  // ========== 配置管理方法 ==========

  Future<bool> setAllowLan(bool enabled) async {
    final success = await _clashManager.setAllowLan(enabled);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setIpv6(bool enabled) async {
    final success = await _clashManager.setIpv6(enabled);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setTcpConcurrent(bool enabled) async {
    final success = await _clashManager.setTcpConcurrent(enabled);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setUnifiedDelay(bool enabled) async {
    final success = await _clashManager.setUnifiedDelay(enabled);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setGeodataLoader(String mode) async {
    final success = await _clashManager.setGeodataLoader(mode);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setFindProcessMode(String mode) async {
    final success = await _clashManager.setFindProcessMode(mode);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setClashCoreLogLevel(String level) async {
    final success = await _clashManager.setClashCoreLogLevel(level);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setExternalController(bool enabled) async {
    final success = await _clashManager.setExternalController(enabled);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setKeepAlive(bool enabled) async {
    return await _clashManager.setKeepAlive(enabled);
  }

  Future<bool> setTestUrl(String url) async {
    final success = await _clashManager.setTestUrl(url);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setMixedPort(int port) async {
    final success = await _clashManager.setMixedPort(port);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setSocksPort(int? port) async {
    final success = await _clashManager.setSocksPort(port);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  Future<bool> setHttpPort(int? port) async {
    final success = await _clashManager.setHttpPort(port);
    if (success) {
      _syncConfigFromManager();
      notifyListeners();
    }
    return success;
  }

  @override
  void dispose() {
    _stopConfigWatcher();

    // 移除应用生命周期监听
    WidgetsBinding.instance.removeObserver(this);

    // 清理所有延迟过期定时器
    for (final timer in _delayExpireTimers.values) {
      timer.cancel();
    }
    _delayExpireTimers.clear();

    _progressSubscription?.cancel();
    _completeSubscription?.cancel();

    super.dispose();
  }
}
