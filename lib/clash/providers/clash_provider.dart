import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/data/clash_model.dart';
import 'package:stelliberty/clash/data/traffic_data_model.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/clash/utils/config_parser.dart';
import 'package:stelliberty/clash/services/config_watcher.dart';
import 'package:stelliberty/clash/services/config_management_service.dart';
import 'package:stelliberty/clash/services/delay_test_service.dart';
import 'package:stelliberty/clash/utils/delay_tester.dart';

// Clash 状态 Provider
// 管理 Clash 的运行状态、代理列表等
//
// 注意：使用 ClashManager 单例实例，确保全局只有一个 Clash 进程
class ClashProvider extends ChangeNotifier {
  // 使用 ClashManager 单例实例
  ClashManager get _clashManager => ClashManager.instance;

  // 配置管理服务
  late final ConfigManagementService _configService;

  // 公开 ClashManager 用于外部访问
  ClashManager get clashManager => _clashManager;

  // 公开配置管理服务供 UI 层使用
  ConfigManagementService get configService => _configService;

  // 运行状态
  bool get isRunning => _clashManager.isRunning;

  // 当前出站模式
  String get mode => _clashManager.mode;

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
    final mode = _clashManager.mode;

    // 根据模式过滤代理组
    _cachedProxyGroups = switch (mode) {
      'direct' => [], // 直连模式：不显示任何代理组
      'global' =>
        _allProxyGroups // 全局模式：显示所有非隐藏组 + GLOBAL 组
            .where((group) => !group.hidden || group.name == 'GLOBAL')
            .toList(),
      _ =>
        _allProxyGroups // 规则模式（默认）：只显示非隐藏的代理组，过滤掉 GLOBAL
            .where((group) => !group.hidden)
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
  bool _isBatchTesting = false;

  // 获取是否正在批量测试延迟
  bool get isBatchTesting => _isBatchTesting;

  // UI 更新节流：记录上次通知时间
  DateTime? _lastNotifyTime;
  // UI 更新节流间隔（毫秒）
  static const int _notifyThrottleMs = 100;

  // selectedMap 内存缓存：记录每个代理组当前选中的节点
  final Map<String, String> _selectedMap = {};

  Map<String, String> get selectedMap => _selectedMap;

  String? _selectedGroupName;
  String? get selectedGroupName => _selectedGroupName;
  ProxyGroup? get selectedGroup {
    if (_selectedGroupName == null) return null;
    return proxyGroups.firstWhere(
      (group) => group.name == _selectedGroupName,
      orElse: () => proxyGroups.first,
    );
  }

  // 加载状态
  bool _isLoading = false;
  bool get isLoading => _isLoading;

  // 并发保护：使用 Completer 确保同一时间只有一个加载操作
  Completer<void>? _loadProxiesCompleter;

  // 错误信息
  String? _errorMessage;
  String? get errorMessage => _errorMessage;

  // 配置文件监听器
  ConfigWatcher? _configWatcher;

  // 是否启用配置文件重载
  bool _enableConfigReload = true;
  bool get enableConfigReload => _enableConfigReload;

  // 判断代理组类型是否支持手动选择
  static bool _isSelectableGroupType(String type) {
    final lowerType = type.toLowerCase();
    return lowerType == 'selector' ||
        lowerType == 'select' ||
        lowerType == 'urltest' ||
        lowerType == 'fallback';
  }

  ClashProvider() {
    // 初始化服务类
    _configService = ConfigManagementService(_clashManager);

    // 监听 ClashManager 的变化
    _clashManager.addListener(_onClashManagerChanged);
  }

  // 初始化（加载配置文件中的代理信息）
  Future<void> initialize(String? configPath) async {
    if (configPath == null || configPath.isEmpty) {
      Logger.info('没有可用的配置文件，ClashProvider 初始化完成（空状态）');
      return;
    }

    Logger.info('ClashProvider 初始化：加载配置文件 $configPath');
    await _loadProxiesFromConfig(configPath);
  }

  // 从配置文件加载代理信息（用于未启动时显示）
  // [configPath] 配置文件路径
  // [restoreSelections] 是否恢复已保存的节点选择（默认 true）
  Future<void> _loadProxiesFromConfig([
    String? configPath,
    bool restoreSelections = true,
  ]) async {
    if (configPath == null || configPath.isEmpty) {
      Logger.warning('未提供配置文件路径，跳过加载');
      return;
    }

    Logger.debug('开始从配置文件加载代理信息: $configPath');

    try {
      // 从文件系统加载配置文件
      final config = await ConfigParser.loadConfigFromFile(configPath);
      final parsedConfig = ConfigParser.parseConfig(config);

      // 更新代理节点和代理组
      _proxyNodes = parsedConfig.proxyNodes;
      _proxyNodesUpdateCount++;
      _allProxyGroups = parsedConfig.proxyGroups;
      _invalidateCache();

      // 恢复已保存的节点选择（如果需要）
      if (restoreSelections) {
        await _restoreProxySelections();
      }

      // 默认选中第一个可见的代理组
      if (_selectedGroupName == null && proxyGroups.isNotEmpty) {
        _selectedGroupName = proxyGroups.first.name;
      }

      // 清除之前的错误信息（成功加载后应该清除错误状态）
      _errorMessage = null;

      Logger.debug(
        '从配置文件加载了 ${_allProxyGroups.length} 个代理组和 ${_proxyNodes.length} 个代理节点',
      );
      notifyListeners();
    } catch (e) {
      Logger.error('从配置文件加载代理信息失败：$e');
      _errorMessage = '从配置文件加载代理信息失败：$e';
      notifyListeners();
    }
  }

  // 公共方法：从订阅配置文件加载代理信息（用于预览模式）
  // 预览模式下不恢复节点选择，因为 Clash 还未加载该配置
  Future<void> loadProxiesFromSubscription(String configPath) async {
    await _loadProxiesFromConfig(configPath, false);
  }

  // ClashManager 状态变化时触发
  void _onClashManagerChanged() {
    // 清除缓存，因为模式可能已变化
    _invalidateCache();
    notifyListeners();
  }

  // 检查延迟测试是否可用
  // 用于 UI 显示提示信息
  bool get isDelayTestAvailable => DelayTester.isAvailable;

  // 获取延迟测试状态描述（用于调试和用户提示）
  String getDelayTestStatus() {
    if (!isRunning) {
      return 'Clash 未运行，请先启动 Clash';
    }

    if (!isDelayTestAvailable) {
      return 'Clash API 未就绪，请稍候或重启 Clash';
    }

    return '延迟测试已就绪';
  }

  // 启动 Clash 核心（不触碰系统代理）
  // 调用者需要自行决定是否启用系统代理
  Future<bool> start({String? configPath}) async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      // 获取覆写配置（如果有回调）
      final overrides = _clashManager.getOverrides();

      final success = await _clashManager.startCore(
        configPath: configPath,
        overrides: overrides,
      );

      if (success) {
        // 初始化 DelayTester 的 API 客户端
        final apiClient = _clashManager.apiClient;
        if (apiClient != null) {
          DelayTester.setApiClient(apiClient);
        } else {
          Logger.error('无法获取 Clash API 客户端，统一延迟测试不可用');
        }

        // 启动后必须从 API 重新加载代理列表
        Logger.info('Clash 已启动，从 API 重新加载代理列表');
        await loadProxies();

        // 如果启用了配置重载且指定了配置文件路径，启动配置文件监听
        if (_enableConfigReload &&
            configPath != null &&
            configPath.isNotEmpty) {
          await _startConfigWatcher(configPath);
        }
      } else {
        Logger.error('Clash 启动失败');
      }
      return success;
    } catch (e) {
      _errorMessage = '启动 Clash 失败：$e';
      Logger.error(_errorMessage!);
      return false;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  // 停止 Clash 核心（不触碰系统代理）
  // 调用者需要自行决定是否禁用系统代理
  Future<bool> stop({String? configPath}) async {
    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      // 先停止配置文件监听
      await _stopConfigWatcher();

      final success = await _clashManager.stopCore();

      // 停止后不需要重新加载配置文件
      // 本地状态已经是最新的，保持不变即可

      return success;
    } catch (e) {
      _errorMessage = '停止 Clash 失败：$e';
      Logger.error(_errorMessage!);
      return false;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  // 同步本地选择到 Clash（启动时调用）
  // 将本地状态中的所有代理选择应用到 Clash API
  Future<void> _syncProxyGroupSelections() async {
    if (!isRunning) return;

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

    // 创建新的 Completer
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

  // 实际的加载逻辑
  Future<void> _doLoadProxies() async {
    Logger.info('开始加载代理列表');

    // 【性能监控】总耗时
    final totalStopwatch = Stopwatch()..start();

    if (!isRunning) {
      Logger.info('Clash 未在运行，无法加载代理列表');
      return;
    }

    Logger.debug(
      '加载前状态：代理组=${_allProxyGroups.length}，节点=${_proxyNodes.length}',
    );

    _isLoading = true;
    _errorMessage = null;
    notifyListeners();

    try {
      // 【性能监控】API 调用耗时
      final apiStopwatch = Stopwatch()..start();

      // 添加重试逻辑，避免因 Clash API 繁忙导致超时
      Map<String, dynamic>? proxies;
      int retryCount = 0;
      const maxRetries = 2;

      while (retryCount <= maxRetries) {
        try {
          proxies = await _clashManager.getProxies();
          break; // 成功则跳出循环
        } catch (e) {
          retryCount++;
          if (retryCount <= maxRetries) {
            Logger.warning('获取代理数据失败（尝试 $retryCount/$maxRetries），1秒后重试：$e');
            await Future.delayed(const Duration(seconds: 1));
          } else {
            Logger.error('获取代理数据失败，已重试 $maxRetries 次：$e');
            rethrow;
          }
        }
      }

      if (proxies == null) {
        throw Exception('获取代理数据失败');
      }

      apiStopwatch.stop();
      Logger.debug(
        '从 Clash API 获取代理数据完成：${proxies.length} 项（耗时：${apiStopwatch.elapsedMilliseconds}ms，重试次数：$retryCount）',
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
        final globalNode = _proxyNodes['GLOBAL'];
        if (globalNode != null && globalNode.isGroup) {
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

          final node = _proxyNodes[groupName];
          if (node == null || !node.isGroup) continue;

          _allProxyGroups.add(ProxyGroup.fromJson(groupName, proxyData));
          addedGroups.add(groupName);
        }
      }

      // 阶段 3：补充遗漏的代理组
      proxies.forEach((name, data) {
        if (addedGroups.contains(name)) return;

        final node = _proxyNodes[name];
        if (node == null || !node.isGroup) return;

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

      Logger.info(
        '加载完成: ${_allProxyGroups.length} 个代理组（${proxyGroups.length} 可见），${_proxyNodes.length} 个节点',
      );

      // 【性能监控】同步选择耗时
      final syncStopwatch = Stopwatch()..start();
      await _syncProxyGroupSelections();
      syncStopwatch.stop();
      Logger.debug('同步代理选择完成（耗时：${syncStopwatch.elapsedMilliseconds}ms）');
    } catch (e) {
      _errorMessage = '加载代理列表失败：$e';
      Logger.error(_errorMessage!);
    } finally {
      _isLoading = false;
      notifyListeners();

      totalStopwatch.stop();
      Logger.info('加载代理列表完成（总耗时：${totalStopwatch.elapsedMilliseconds}ms）');
    }
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
      _errorMessage = '该代理组类型 (${group.type}) 不支持手动切换';
      notifyListeners();
      return false;
    }

    _allProxyGroups[groupIndex] = group.copyWith(now: proxyName);
    _invalidateCache();
    notifyListeners();
    Logger.debug('本地状态已更新：$groupName -> $proxyName');

    // 更新 selectedMap 缓存
    _selectedMap[groupName] = proxyName;

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
    if (isRunning) {
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

    _selectedMap.clear();

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
      if (selected == null && isRunning && group.now != null) {
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
        Logger.debug('恢复节点选择: ${group.name} -> $selected');
      }

      // 更新代理组的 now 字段
      if (selected != null && selected != group.now) {
        _allProxyGroups[i] = group.copyWith(now: selected);
      }

      // 同时更新 selectedMap
      if (selected != null) {
        _selectedMap[group.name] = selected;
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
    _enableConfigReload = enabled;
    Logger.info('配置文件重载：${enabled ? "已启用" : "已禁用"}');
  }

  // 启动配置文件监听
  Future<void> _startConfigWatcher(String configPath) async {
    // 先停止旧的监听器
    await _stopConfigWatcher();

    // 创建新的监听器
    _configWatcher = ConfigWatcher(
      onReload: () async {
        Logger.info('检测到配置文件变化，重新生成运行时配置并重载…');

        // 1. 重新生成 runtime_config.yaml 并重载 Clash 配置
        final reloadSuccess = await _clashManager.reloadConfig(
          configPath: configPath,
        );

        if (reloadSuccess) {
          // 2. 重新加载代理列表（显示新节点）
          await loadProxies();
        } else {
          Logger.error('配置重载失败，跳过代理列表更新');
        }
      },
      debounceMs: 1000, // 1秒防抖
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

  ProxyNode? getProxyNode(String name) {
    return _proxyNodes[name];
  }

  // ========== 延迟测试方法 ==========

  // 递归解析代理节点名称
  String resolveProxyNodeName(
    String proxyName, {
    int maxDepth = 20,
    Set<String>? visited,
  }) {
    return DelayTestService.resolveProxyNodeName(
      proxyName,
      _proxyNodes,
      _allProxyGroups,
      _selectedMap,
      maxDepth: maxDepth,
      visited: visited,
    );
  }

  // 测试代理延迟（支持代理组）
  Future<int> testProxyDelay(
    String proxyName, [
    String? testUrl,
    bool notify = true,
  ]) async {
    final delay = await DelayTestService.testProxyDelay(
      proxyName,
      _proxyNodes,
      _allProxyGroups,
      _selectedMap,
      testUrl: testUrl,
    );

    if (notify) {
      // 在最新的 _proxyNodes 上更新延迟值
      final node = _proxyNodes[proxyName];
      if (node != null) {
        _proxyNodes[proxyName] = node.copyWith(delay: delay);
      }

      // 创建新的 Map 实例以触发 UI 更新
      _proxyNodes = Map<String, ProxyNode>.from(_proxyNodes);
      _proxyNodesUpdateCount++;
      notifyListeners();
    }

    return delay;
  }

  // 批量测试代理组中所有节点的延迟
  Future<void> testGroupDelays(String groupName, [String? testUrl]) async {
    if (_isBatchTesting) {
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
    _isBatchTesting = true;
    _testingNodes.clear();
    _testingNodes.addAll(proxyNames);
    _lastNotifyTime = null; // 重置节流计时器
    notifyListeners();

    // 标记是否有待通知的更新
    bool hasPendingUpdates = false;

    try {
      await DelayTestService.testGroupDelays(
        groupName,
        _proxyNodes,
        _allProxyGroups,
        _selectedMap,
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
              (_lastNotifyTime == null ||
                  now.difference(_lastNotifyTime!).inMilliseconds >=
                      _notifyThrottleMs)) {
            // 仅在有更新时才创建新 Map（触发 Selector 重建）
            _proxyNodes = Map<String, ProxyNode>.from(_proxyNodes);
            _proxyNodesUpdateCount++;
            notifyListeners();
            _lastNotifyTime = now;
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
      _isBatchTesting = false;
      _lastNotifyTime = null;
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

  // ========== 系统代理和 TUN 模式控制 ==========

  /// 获取 TUN 模式状态
  bool get tunEnabled => _clashManager.tunEnable;

  /// 切换 TUN 模式
  Future<bool> setTunMode(bool enabled) async {
    try {
      Logger.info('切换虚拟网卡模式：${enabled ? "启用" : "禁用"}');
      return await _clashManager.setTunEnable(enabled);
    } catch (e) {
      Logger.error('切换虚拟网卡模式失败：$e');
      return false;
    }
  }

  /// 启用系统代理
  Future<void> enableSystemProxy() async {
    try {
      Logger.info('启用系统代理');
      await _clashManager.enableSystemProxy();
    } catch (e) {
      Logger.error('启用系统代理失败：$e');
      rethrow;
    }
  }

  /// 禁用系统代理
  Future<void> disableSystemProxy() async {
    try {
      Logger.info('禁用系统代理');
      await _clashManager.disableSystemProxy();
    } catch (e) {
      Logger.error('禁用系统代理失败：$e');
      rethrow;
    }
  }

  @override
  void dispose() {
    _stopConfigWatcher();
    _clashManager.removeListener(_onClashManagerChanged);
    super.dispose();
  }
}
