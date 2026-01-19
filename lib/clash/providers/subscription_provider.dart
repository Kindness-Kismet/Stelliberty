import 'dart:async';
import 'dart:io';
import 'package:flutter/widgets.dart';
import 'package:stelliberty/clash/state/subscription_states.dart';
import 'package:stelliberty/clash/model/subscription_model.dart';
import 'package:stelliberty/clash/model/override_model.dart' as app_override;
import 'package:stelliberty/clash/services/subscription_service.dart';
import 'package:stelliberty/clash/services/override_service.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/providers/override_provider.dart';
import 'package:stelliberty/clash/manager/clash_manager.dart';
import 'package:stelliberty/clash/manager/subscription_manager.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';

// 订阅状态管理
class SubscriptionProvider extends ChangeNotifier {
  late final SubscriptionManager _manager;

  // 订阅状态
  SubscriptionState _state = SubscriptionState.idle();
  SubscriptionState get subscriptionState => _state;

  // ClashProvider 引用（用于配置切换时重新加载代理信息）
  ClashProvider? _clashProvider;

  // 自动更新定时器
  Timer? _autoUpdateTimer;

  // 启动时更新是否已完成
  bool _isStartupUpdateDone = false;

  // 自动更新并发保护标记
  bool _isAutoUpdateInProgress = false;

  // 订阅列表
  List<Subscription> _subscriptions = [];
  List<Subscription> get subscriptions => List.unmodifiable(_subscriptions);

  // 当前选中的订阅 ID
  String? _currentSubscriptionId;
  String? get currentSubscriptionId => _currentSubscriptionId;

  // 当前选中的订阅
  Subscription? get currentSubscription {
    if (_currentSubscriptionId == null) return null;
    try {
      return _subscriptions.firstWhere((s) => s.id == _currentSubscriptionId);
    } catch (e) {
      assert(() {
        Logger.debug('当前订阅查找失败：$e');
        return true;
      }());
      return null;
    }
  }

  // 状态访问器
  bool get isLoading => _state.isLoading;
  bool get isSwitchingSubscription => _state.isSwitching;
  int get updateProgress => _state.updateCurrent;
  int get updateTotal => _state.updateTotal;
  bool get isUpdating => _state.isUpdating;
  bool get isBatchUpdatingSubscriptions => _state.isBatchUpdating;
  String? get errorMessage => _state.errorMessage;

  // 检查指定订阅是否正在更新
  bool isSubscriptionUpdating(String subscriptionId) {
    return _state.isSubscriptionUpdating(subscriptionId);
  }

  // 构造函数（接收共享的 OverrideService 实例）
  SubscriptionProvider(OverrideService overrideService) {
    final service = SubscriptionService();
    _manager = SubscriptionManager(
      service: service,
      isCoreRunning: () => ClashManager.instance.isCoreRunning,
      getMixedPort: () => ClashPreferences.instance.getMixedPort(),
    );
    _manager.setOverrideService(overrideService);
  }

  // 更新状态并通知监听器
  void _updateState(SubscriptionState nextState) {
    _state = nextState;
    notifyListeners();
  }

  // 设置 ClashProvider 引用
  // 用于在订阅切换时通知 ClashProvider 重新加载配置
  void setClashProvider(ClashProvider clashProvider) {
    _clashProvider = clashProvider;
    Logger.debug('已设置 ClashProvider 引用到 SubscriptionProvider');
  }

  // 设置覆写获取回调
  // 从 OverrideProvider 获取覆写配置
  void setOverrideGetter(
    Future<List<app_override.OverrideConfig>> Function(List<String>) getter,
  ) {
    _manager.setOverrideGetter(getter);
  }

  // 读取订阅配置文件
  Future<String> readSubscriptionConfig(Subscription subscription) async {
    return await _manager.readSubscriptionConfig(subscription);
  }

  // 处理当前订阅的覆写失败
  // 当启动失败时调用，禁用当前订阅的所有覆写并记录失败 ID
  Future<void> handleOverridesFailed() async {
    if (currentSubscription == null) {
      Logger.warning('没有当前订阅，跳过覆写失败处理');
      return;
    }

    final subscription = currentSubscription!;

    // 如果没有覆写，直接返回
    if (subscription.overrideIds.isEmpty) {
      Logger.info('当前订阅没有覆写，跳过处理');
      return;
    }

    Logger.error('覆写导致启动失败，执行回退');
    Logger.error('订阅：${subscription.name}');
    Logger.error('失败的覆写 ID：${subscription.overrideIds}');

    // 记录失败的覆写 ID 并清空当前覆写
    final index = _subscriptions.indexWhere((s) => s.id == subscription.id);
    if (index != -1) {
      _subscriptions[index] = subscription.copyWith(
        overrideIds: [], // 清空覆写
        failedOverrideIds: subscription.overrideIds, // 记录失败的覆写
      );

      // 保存到持久化存储
      await _manager.saveSubscriptionList(_subscriptions);

      // 通知 UI 更新
      notifyListeners();

      Logger.info('已禁用订阅 ${subscription.name} 的所有覆写');
      Logger.info('失败的覆写 ID已记录：${subscription.overrideIds}');
    }

    Logger.info('覆写回退完成');
  }

  // 清理订阅中无效的覆写 ID 引用
  // 用于在初始化时移除已删除的覆写引用
  Future<void> cleanupInvalidOverrideReferences(
    Future<List<app_override.OverrideConfig>> Function(List<String>)
    getOverrides,
  ) async {
    Logger.debug('开始清理订阅中的无效覆写引用...');

    // 性能优化：先收集所有需要检查的覆写 ID，一次性查询
    final allOverrideIds = <String>{};
    for (final subscription in _subscriptions) {
      allOverrideIds.addAll(subscription.overrideIds);
    }

    if (allOverrideIds.isEmpty) {
      Logger.debug('没有订阅使用覆写，跳过清理');
      return;
    }

    // 一次性查询所有覆写
    final allExistingOverrides = await getOverrides(allOverrideIds.toList());
    final validIds = allExistingOverrides.map((o) => o.id).toSet();

    Logger.debug('查询到 ${validIds.length}/${allOverrideIds.length} 个有效覆写');

    // 遍历订阅并更新
    bool hasChanges = false;
    for (int i = 0; i < _subscriptions.length; i++) {
      final subscription = _subscriptions[i];
      if (subscription.overrideIds.isEmpty) continue;

      // 过滤出有效的 ID
      final filteredIds = subscription.overrideIds
          .where((id) => validIds.contains(id))
          .toList();

      // 找出无效的 ID
      final invalidIds = subscription.overrideIds
          .where((id) => !validIds.contains(id))
          .toList();

      if (invalidIds.isNotEmpty) {
        Logger.info(
          '订阅 ${subscription.name} 包含 ${invalidIds.length} 个无效覆写引用: $invalidIds',
        );
        _subscriptions[i] = subscription.copyWith(overrideIds: filteredIds);
        hasChanges = true;
      }
    }

    if (hasChanges) {
      await _manager.saveSubscriptionList(_subscriptions);
      Logger.info('已清理订阅中的无效覆写引用');
    } else {
      Logger.debug('没有发现无效的覆写引用');
    }
  }

  // 初始化 Provider
  Future<void> initialize() async {
    _updateState(SubscriptionState.loading());

    try {
      // 初始化服务
      await _manager.initialize();

      // 初始化覆写服务（共享实例，已在 main.dart 初始化）
      // OverrideService 已在构造函数中设置到 Manager

      // 设置覆写获取回调（需要从 OverrideProvider 获取）
      // 注意：此时 OverrideProvider 可能还未初始化，所以在 main.dart 中设置

      // 加载订阅列表
      _subscriptions = await _manager.loadSubscriptionList();

      // 尝试恢复上次选中的订阅
      final savedSubscriptionId = ClashPreferences.instance
          .getCurrentSubscriptionId();

      if (savedSubscriptionId != null &&
          _subscriptions.any((s) => s.id == savedSubscriptionId)) {
        _currentSubscriptionId = savedSubscriptionId;
        Logger.info('恢复上次选中的订阅：$savedSubscriptionId');
      }
      // 其他情况保持无选中状态，使用默认配置

      Logger.info('订阅 Provider 初始化成功，共 ${_subscriptions.length} 个订阅');

      // 启动动态自动更新定时器
      _restartAutoUpdateTimer();

      _updateState(SubscriptionState.idle());
    } catch (e) {
      // 初始化失败时，设置错误消息以便 UI 显示
      final errorMsg = '初始化订阅失败: $e';
      Logger.error(errorMsg);
      _subscriptions = []; // 确保订阅列表为空
      _updateState(
        _state.copyWith(
          errorState: SubscriptionErrorState.initializationError,
          errorMessage: errorMsg,
        ),
      );
    }
  }

  // 添加订阅
  Future<bool> addSubscription({
    required String name,
    required String url,
    AutoUpdateMode autoUpdateMode = AutoUpdateMode.disabled,
    int intervalMinutes = 60,
    bool shouldUpdateOnStartup = false,
    bool downloadNow = true,
    SubscriptionProxyMode proxyMode = SubscriptionProxyMode.direct,
    String? userAgent,
  }) async {
    // 不清除全局错误，单个订阅操作不影响全局状态

    try {
      // URL 验证
      if (!_isValidUrl(url)) {
        final errorMsg = '无效的订阅链接格式';
        Logger.error('$errorMsg：$url');
        // 注意：不设置全局错误，只记录日志
        return false;
      }

      // 如果未指定 userAgent，使用全局默认值
      final effectiveUserAgent =
          userAgent ?? ClashPreferences.instance.getDefaultUserAgent();

      final subscription = Subscription(
        id: DateTime.now().millisecondsSinceEpoch.toString(),
        name: name,
        url: url,
        autoUpdateMode: autoUpdateMode,
        intervalMinutes: intervalMinutes,
        shouldUpdateOnStartup: shouldUpdateOnStartup,
        proxyMode: proxyMode,
        userAgent: effectiveUserAgent,
      );

      // 如果需要立即下载
      if (downloadNow) {
        Logger.info('立即下载新订阅：$name');
        final updatedSubscription = await _manager.downloadSubscription(
          subscription.copyWith(isUpdating: true),
        );
        _subscriptions.add(updatedSubscription);
      } else {
        _subscriptions.add(subscription);
      }

      // 保存订阅列表
      await _manager.saveSubscriptionList(_subscriptions);

      // 如果是第一个订阅，自动选中
      if (_subscriptions.length == 1) {
        _currentSubscriptionId = subscription.id;
        await ClashPreferences.instance.setCurrentSubscriptionId(
          _currentSubscriptionId,
        );

        // 如果核心正在运行（使用 default.yaml），需要应用真实配置
        final clashProvider = _clashProvider;
        if (clashProvider != null && clashProvider.isCoreRunning) {
          Logger.info('首个订阅已添加，准备应用配置...');
          final configPath = getSubscriptionConfigPath();
          if (configPath != null) {
            try {
              // 订阅切换，使用重载（避免连接中断）
              Logger.info('订阅切换，尝试重载配置');
              final reloadSuccess = await clashProvider.clashManager
                  .reloadConfig(configPath: configPath);

              if (!reloadSuccess) {
                Logger.warning('重载失败，降级为重启核心');
                await clashProvider.clashManager.restartCore();
              } else {
                // 重载成功后，从 Clash API 重新加载代理列表
                await clashProvider.loadProxies();
              }
            } catch (e) {
              Logger.error('应用配置失败：$e');
              // 配置应用失败不影响订阅添加的成功状态
            }
          }
        }
      }

      notifyListeners();

      // 重新计算定时器间隔（新订阅可能启用了自动更新）
      _restartAutoUpdateTimer();

      Logger.info('添加订阅成功：$name');
      return true;
    } catch (e) {
      // 不设置全局错误，只记录日志
      Logger.error('添加订阅失败：$name - $e');
      return false;
    }
  }

  // 分类错误类型
  // 分类错误类型
  SubscriptionErrorState _classifyError(String errorMsg) {
    final lowerError = errorMsg.toLowerCase();

    // 网络相关错误
    if (lowerError.contains('socketexception') ||
        lowerError.contains('failed host lookup') ||
        lowerError.contains('network is unreachable') ||
        lowerError.contains('no route to host')) {
      return SubscriptionErrorState.network;
    }

    // 超时错误
    if (lowerError.contains('timeout') || lowerError.contains('timed out')) {
      return SubscriptionErrorState.timeout;
    }

    // HTTP 错误
    if (lowerError.contains('http 4') || lowerError.contains('http 5')) {
      if (lowerError.contains('404')) {
        return SubscriptionErrorState.notFound;
      }
      if (lowerError.contains('403') || lowerError.contains('401')) {
        return SubscriptionErrorState.forbidden;
      }
      return SubscriptionErrorState.serverError;
    }

    // 配置格式错误
    if (lowerError.contains('配置文件') ||
        lowerError.contains('格式') ||
        lowerError.contains('解析') ||
        lowerError.contains('yaml') ||
        lowerError.contains('proxies')) {
      return SubscriptionErrorState.formatError;
    }

    // 证书错误
    if (lowerError.contains('certificate') ||
        lowerError.contains('handshake')) {
      return SubscriptionErrorState.certificate;
    }

    // 其他未知错误
    return SubscriptionErrorState.unknown;
  }

  // 更新订阅
  Future<bool> updateSubscription(String subscriptionId) async {
    // 不清除全局错误，单个订阅更新不影响全局状态
    final index = _subscriptions.indexWhere((s) => s.id == subscriptionId);

    if (index == -1) {
      Logger.error('更新失败：订阅不存在 (ID：$subscriptionId)');
      return false;
    }

    final subscription = _subscriptions[index];

    try {
      if (subscription.isLocalFile) {
        Logger.info('本地订阅无需更新：${subscription.name}');
        _subscriptions[index] = subscription.copyWith(
          isUpdating: false,
          lastError: null, // 清除可能存在的旧错误
        );
        notifyListeners();
        return true;
      }

      // 添加到更新中列表
      _updateState(
        _state.copyWith(updatingIds: {..._state.updatingIds, subscriptionId}),
      );

      // 设置更新状态并清除错误
      _subscriptions[index] = subscription.copyWith(
        isUpdating: true,
        lastError: null,
      );
      notifyListeners();

      // 下载订阅
      final updatedSubscription = await _manager.downloadSubscription(
        subscription,
      );

      // 更新列表（确保清除错误信息和配置失败标记）
      _subscriptions[index] = updatedSubscription.copyWith(
        lastError: null,
        hasConfigLoadFailed: false, // 更新成功后清除配置失败标记
      );
      await _manager.saveSubscriptionList(_subscriptions);

      // 更新订阅后重新加载配置
      if (subscriptionId == _currentSubscriptionId) {
        Logger.info('当前订阅已更新，开始重新加载配置...');
        // 暂停 ConfigWatcher，避免重复触发重载
        _clashProvider?.pauseConfigWatcher();
        try {
          await _reloadCurrentSubscriptionConfig(reason: '订阅更新');
        } finally {
          await _clashProvider?.resumeConfigWatcher();
        }
      }
      Logger.info('更新订阅成功：${subscription.name}');

      // 注意：不在这里重启定时器，避免批量更新时频繁重启
      // 定时器会在 autoUpdateSubscriptions() 完成后统一重启

      return true;
    } catch (e) {
      final rawError = e.toString();
      Logger.error('更新订阅失败：${subscription.name} - $rawError');

      // 分析错误类型并保存
      final errorType = _classifyError(rawError);
      Logger.info('错误类型：$errorType');

      // 判断是否为永久性错误（需要禁用自动更新）
      final isPermanentError =
          errorType == SubscriptionErrorState.notFound ||
          errorType == SubscriptionErrorState.forbidden ||
          errorType == SubscriptionErrorState.formatError;

      if (isPermanentError) {
        // 永久性错误：禁用自动更新（需要用户手动修复）
        Logger.warning('检测到永久性错误，已禁用自动更新：${errorType.name}');
        _subscriptions[index] = subscription.copyWith(
          isUpdating: false,
          lastError: errorType.name,
          autoUpdateMode: AutoUpdateMode.disabled,
        );
      } else {
        // 临时性错误：更新时间戳，按正常间隔重试
        Logger.info('临时性错误，将按正常间隔重试：${errorType.name}');
        _subscriptions[index] = subscription.copyWith(
          isUpdating: false,
          lastError: errorType.name,
          lastUpdatedAt: DateTime.now(),
        );
      }
      await _manager.saveSubscriptionList(_subscriptions);

      return false;
    } finally {
      // 从更新中列表移除
      _updateState(
        _state.copyWith(
          updatingIds: _state.updatingIds
              .where((id) => id != subscriptionId)
              .toSet(),
        ),
      );
    }
  }

  // 批量更新所有订阅
  // 使用真正的并发更新，并提供进度通知
  Future<List<String>> updateAllSubscriptions() async {
    // 不清除全局错误，批量操作不影响全局状态
    final errors = <String>[];

    // 设置批量更新状态
    _updateState(SubscriptionState.batchUpdating(_subscriptions.length));

    if (_subscriptions.isEmpty) {
      _updateState(SubscriptionState.idle());
      return errors;
    }

    Logger.info('开始并发更新 ${_subscriptions.length} 个订阅');

    // 使用并发限制，避免过载
    const concurrency = ClashDefaults.subscriptionUpdateConcurrency;
    final semaphore = _Semaphore(concurrency);

    // 创建所有更新任务（真正的并发）
    final updateFutures = _subscriptions.map((subscription) async {
      // 获取信号量许可
      await semaphore.acquire();

      try {
        // 跳过正在更新的订阅
        if (_state.isSubscriptionUpdating(subscription.id)) {
          Logger.debug('跳过正在更新的订阅：${subscription.name}');
          _updateState(
            _state.copyWith(updateCurrent: _state.updateCurrent + 1),
          );
          return null;
        }

        final success = await updateSubscription(subscription.id);

        // 更新进度
        _updateState(_state.copyWith(updateCurrent: _state.updateCurrent + 1));

        Logger.debug(
          '订阅更新进度: ${_state.updateCurrent}/${_state.updateTotal} (${subscription.name})',
        );

        // 如果失败，从订阅对象中获取错误信息
        if (!success) {
          final index = _subscriptions.indexWhere(
            (s) => s.id == subscription.id,
          );
          if (index != -1 && _subscriptions[index].lastError != null) {
            return '${subscription.name}: ${_subscriptions[index].lastError}';
          }
          return '${subscription.name}: 更新失败';
        }
        return null;
      } finally {
        // 释放信号量
        semaphore.release();
      }
    }).toList();

    // 等待所有任务完成
    final results = await Future.wait(updateFutures);

    // 收集错误
    for (final error in results) {
      if (error != null) {
        errors.add(error);
      }
    }

    // 重置进度和批量更新状态
    _updateState(SubscriptionState.idle());

    Logger.info(
      '批量更新完成: 成功=${_subscriptions.length - errors.length}, 失败=${errors.length}',
    );

    // 批量更新包含当前订阅时重新加载配置
    if (_currentSubscriptionId != null &&
        _subscriptions.any((s) => s.id == _currentSubscriptionId)) {
      Logger.info('批量更新包含当前订阅，重新加载配置...');
      _clashProvider?.pauseConfigWatcher();
      try {
        await _reloadCurrentSubscriptionConfig(reason: '批量更新包含当前订阅');
      } finally {
        await _clashProvider?.resumeConfigWatcher();
      }
    }

    return errors;
  }

  // 检查是否有启用间隔更新的订阅
  bool _hasIntervalUpdateSubscriptions() {
    return _subscriptions.any(
      (s) => s.autoUpdateMode == AutoUpdateMode.interval && !s.isLocalFile,
    );
  }

  // 启动/重启自动更新定时器（固定 1 分钟检查间隔）
  void _restartAutoUpdateTimer() {
    // 取消现有定时器
    _autoUpdateTimer?.cancel();
    _autoUpdateTimer = null;

    // 如果没有需要自动更新的订阅，停止定时器
    if (!_hasIntervalUpdateSubscriptions()) {
      Logger.info('无启用间隔更新的订阅，定时器已停止');
      return;
    }

    // 固定每分钟检查一次（简单高效）
    _autoUpdateTimer = Timer.periodic(const Duration(minutes: 1), (_) {
      _checkAndAutoUpdate();
    });

    // 2 秒后立即检查一次，避免错过已过期的订阅
    Future.delayed(const Duration(seconds: 2), () {
      if (_autoUpdateTimer != null) {
        _checkAndAutoUpdate();
      }
    });

    Logger.info('自动更新定时器已启动（固定检查间隔：1 分钟）');
  }

  // 检查并执行自动更新
  void _checkAndAutoUpdate() async {
    // 防止并发执行
    if (_isAutoUpdateInProgress) {
      Logger.debug('自动更新正在处理中，跳过本次检查');
      return;
    }

    // 防止重复执行
    if (_state.operationState.isAutoUpdating) {
      Logger.debug('自动更新正在执行中，跳过本次检查');
      return;
    }

    _isAutoUpdateInProgress = true;
    try {
      // 过滤出需要更新的订阅
      final needUpdateSubscriptions = _subscriptions
          .where((s) => s.shouldUpdate)
          .toList();

      if (needUpdateSubscriptions.isEmpty) {
        Logger.debug('定时检查：没有订阅需要更新');
        return;
      }

      Logger.info('定时检查：发现 ${needUpdateSubscriptions.length} 个订阅需要更新');

      _updateState(
        _state.copyWith(
          operationState: SubscriptionOperationState.autoUpdating,
        ),
      );
      try {
        await autoUpdateSubscriptions();
      } catch (e) {
        Logger.error('自动更新订阅失败：$e');
        _updateState(
          _state.copyWith(
            errorState: SubscriptionErrorState.unknown,
            errorMessage: '自动更新失败: $e',
          ),
        );
      } finally {
        _updateState(SubscriptionState.idle());
      }
    } finally {
      _isAutoUpdateInProgress = false;
    }
  }

  // 自动更新需要更新的订阅
  // 使用并发更新以提高效率
  Future<void> autoUpdateSubscriptions() async {
    Logger.info('开始自动更新订阅...');

    // 过滤出需要更新的订阅
    final needUpdateSubscriptions = _subscriptions
        .where((s) => s.shouldUpdate)
        .toList();

    if (needUpdateSubscriptions.isEmpty) {
      Logger.info('没有需要更新的订阅');
      return;
    }

    Logger.info('发现 ${needUpdateSubscriptions.length} 个订阅需要更新');

    // 使用并发更新，限制并发数
    const concurrency = ClashDefaults.subscriptionUpdateConcurrency;

    for (int i = 0; i < needUpdateSubscriptions.length; i += concurrency) {
      final batch = needUpdateSubscriptions.skip(i).take(concurrency).toList();

      // 并发更新一批订阅
      await Future.wait(
        batch.map((subscription) => updateSubscription(subscription.id)),
      );
    }

    Logger.info('自动更新订阅完成');

    // 批量更新完成后重新计算定时器（避免单个更新时频繁重启）
    _restartAutoUpdateTimer();
  }

  // 执行启动时更新（确保只执行一次）
  Future<void> performStartupUpdate() async {
    if (_isStartupUpdateDone) {
      Logger.debug('启动时更新已执行过，跳过');
      return;
    }
    _isStartupUpdateDone = true;

    // 找到所有启用了"启动时更新"的订阅（排除本地文件）
    final startupUpdateSubscriptions = _subscriptions
        .where((s) => s.shouldUpdateOnStartup && !s.isLocalFile)
        .toList();

    if (startupUpdateSubscriptions.isEmpty) {
      Logger.info('没有启用启动时更新的订阅');
      return;
    }

    Logger.info('发现 ${startupUpdateSubscriptions.length} 个启用启动时更新的订阅');

    // 使用并发更新提升性能，限制并发数为 3
    const concurrency = 3;

    for (int i = 0; i < startupUpdateSubscriptions.length; i += concurrency) {
      final batch = startupUpdateSubscriptions.skip(i).take(concurrency);

      // 并发更新一批订阅
      final batchFutures = batch.map((subscription) async {
        Logger.info('启动时更新订阅：${subscription.name}');
        await updateSubscription(subscription.id);
      });

      // 等待当前批次完成后再处理下一批
      await Future.wait(batchFutures);
    }

    Logger.info('启动时更新完成');
  }

  // 导入本地文件并创建订阅
  Future<bool> importLocalFile({
    required String name,
    required String filePath,
  }) async {
    try {
      // 调用 Manager 解析文件
      final parsedConfig = await _manager.importLocalFile(filePath);

      // 创建本地订阅
      return await addLocalSubscription(
        name: name,
        filePath: filePath,
        content: parsedConfig,
      );
    } catch (e) {
      Logger.error('导入本地文件失败：$e');
      return false;
    }
  }

  // 添加本地订阅
  Future<bool> addLocalSubscription({
    required String name,
    required String filePath,
    required String content,
  }) async {
    // 不清除全局错误，单个操作不影响全局状态

    try {
      // 创建本地订阅对象（url 为空，表示本地文件）
      final subscription =
          Subscription.create(
            name: name,
            url: '', // 本地文件无 URL
          ).copyWith(
            autoUpdateMode: AutoUpdateMode.disabled, // 本地文件不支持自动更新
            isLocalFile: true, // 标记为本地文件
          );

      // 保存配置文件内容到订阅目录
      await _manager.saveLocalSubscription(subscription, content);

      // 添加到订阅列表
      _subscriptions.add(subscription);

      // 保存订阅列表
      await _manager.saveSubscriptionList(_subscriptions);

      // 如果是第一个订阅，自动选中
      if (_subscriptions.length == 1) {
        _currentSubscriptionId = subscription.id;
        await ClashPreferences.instance.setCurrentSubscriptionId(
          _currentSubscriptionId,
        );

        // 如果核心正在运行（使用 default.yaml），需要应用真实配置
        final clashProvider = _clashProvider;
        if (clashProvider != null && clashProvider.isCoreRunning) {
          Logger.info('首个本地订阅已添加，准备应用配置...');
          final configPath = getSubscriptionConfigPath();
          if (configPath != null) {
            try {
              // 订阅切换，使用重载（避免连接中断）
              Logger.info('订阅切换，尝试重载配置（本地订阅）');
              final reloadSuccess = await clashProvider.clashManager
                  .reloadConfig(configPath: configPath);

              if (!reloadSuccess) {
                Logger.warning('重载失败，降级为重启核心');
                await clashProvider.clashManager.restartCore();
              } else {
                // 重载成功后，从 Clash API 重新加载代理列表
                await clashProvider.loadProxies();
              }
            } catch (e) {
              Logger.error('应用配置失败：$e');
            }
          }
        }
      }

      notifyListeners();

      // 本地订阅不支持自动更新，但仍需重新计算定时器
      _restartAutoUpdateTimer();

      Logger.info('添加本地订阅成功：$name');
      return true;
    } catch (e) {
      // 不设置全局错误，只记录日志
      Logger.error('添加本地订阅失败：$name - $e');
      return false;
    }
  }

  // 删除订阅
  Future<bool> deleteSubscription(String subscriptionId) async {
    // 不清除全局错误，单个操作不影响全局状态

    try {
      final subscription = _subscriptions.firstWhere(
        (s) => s.id == subscriptionId,
      );

      // 检查是否删除的是当前选中的订阅
      final isDeletingCurrentSubscription =
          _currentSubscriptionId == subscriptionId;
      final wasRunning = ClashManager.instance.isCoreRunning;

      // 删除配置文件
      await _manager.deleteSubscription(subscription);

      // 从列表中移除
      _subscriptions.removeWhere((s) => s.id == subscriptionId);

      // 如果删除的是当前选中的订阅
      if (isDeletingCurrentSubscription) {
        _currentSubscriptionId = _subscriptions.isNotEmpty
            ? _subscriptions.first.id
            : null;
        // 持久化保存新的订阅 ID
        await ClashPreferences.instance.setCurrentSubscriptionId(
          _currentSubscriptionId,
        );

        // 如果核心正在运行，重载配置（订阅或默认配置）
        if (wasRunning) {
          Logger.info(
            '删除了当前订阅，重载${_currentSubscriptionId != null ? "新订阅配置" : "默认配置"}',
          );
          final clashProvider = _clashProvider;
          if (clashProvider != null) {
            try {
              // 获取新的配置路径（如果有剩余订阅）或使用 null（默认配置）
              final configPath = getSubscriptionConfigPath();
              final reloadSuccess = await clashProvider.clashManager
                  .reloadConfig(configPath: configPath);

              if (!reloadSuccess) {
                Logger.warning('重载失败，降级为重启核心');
                await ClashManager.instance.restartCore();
              } else {
                // 重载成功后，从 Clash API 重新加载代理列表
                await clashProvider.loadProxies();
              }
            } catch (e) {
              Logger.error('重载配置失败，尝试重启核心：$e');
              await ClashManager.instance.restartCore();
            }
          }
        }
      }

      // 保存订阅列表
      await _manager.saveSubscriptionList(_subscriptions);

      notifyListeners();

      // 重新计算定时器间隔（订阅减少可能影响最短间隔）
      _restartAutoUpdateTimer();

      Logger.info('删除订阅成功：${subscription.name}');
      return true;
    } catch (e) {
      // 不设置全局错误，只记录日志
      Logger.error('删除订阅失败 (ID：$subscriptionId) - $e');
      return false;
    }
  }

  // 重新排序订阅列表
  Future<void> reorderSubscriptions(int oldIndex, int newIndex) async {
    if (oldIndex < newIndex) {
      newIndex -= 1;
    }

    final subscription = _subscriptions.removeAt(oldIndex);
    _subscriptions.insert(newIndex, subscription);

    await _manager.saveSubscriptionList(_subscriptions);
    notifyListeners();

    Logger.info('订阅排序已更新：${subscription.name} 从 $oldIndex 移动到 $newIndex');
  }

  // 从所有订阅中移除指定的覆写 ID 引用
  // 用于在删除覆写时清理订阅配置
  Future<void> removeOverrideFromAllSubscriptions(String overrideId) async {
    Logger.info('从所有订阅中移除覆写引用：$overrideId');

    bool hasChanges = false;
    for (int i = 0; i < _subscriptions.length; i++) {
      final subscription = _subscriptions[i];
      if (subscription.overrideIds.contains(overrideId)) {
        Logger.debug('从订阅 ${subscription.name} 中移除覆写引用');
        final nextOverrideIds = subscription.overrideIds
            .where((id) => id != overrideId)
            .toList();
        _subscriptions[i] = subscription.copyWith(overrideIds: nextOverrideIds);
        hasChanges = true;
      }
    }

    if (hasChanges) {
      await _manager.saveSubscriptionList(_subscriptions);
      notifyListeners();
      Logger.info('已从订阅中移除覆写引用');
    } else {
      Logger.debug('没有订阅使用该覆写');
    }
  }

  // 如果当前订阅使用了指定覆写，重载配置
  // 用于在覆写内容更新时自动应用新配置
  Future<void> _reloadSubscriptionIfOverrideUsed(String overrideId) async {
    if (_currentSubscriptionId == null) {
      Logger.debug('没有当前订阅，无需重载');
      return;
    }

    final subscriptionIndex = _subscriptions.indexWhere(
      (s) => s.id == _currentSubscriptionId,
    );
    if (subscriptionIndex == -1) {
      Logger.error('当前订阅不存在 (ID: $_currentSubscriptionId)');
      return;
    }

    final currentSubscription = _subscriptions[subscriptionIndex];

    if (!currentSubscription.overrideIds.contains(overrideId)) {
      Logger.debug('当前订阅未使用该覆写，无需重载');
      return;
    }

    Logger.info('当前订阅使用了更新的覆写，重新加载配置');
    _clashProvider?.pauseConfigWatcher();
    try {
      await _reloadCurrentSubscriptionConfig(reason: '覆写内容更新');
    } finally {
      await _clashProvider?.resumeConfigWatcher();
    }
  }

  // 修改订阅信息
  Future<bool> updateSubscriptionInfo({
    required String subscriptionId,
    String? name,
    String? url,
    AutoUpdateMode? autoUpdateMode,
    int? intervalMinutes,
    bool? shouldUpdateOnStartup,
    SubscriptionProxyMode? proxyMode,
    String? userAgent,
  }) async {
    // 不清除全局错误，单个操作不影响全局状态
    final index = _subscriptions.indexWhere((s) => s.id == subscriptionId);

    if (index == -1) {
      Logger.error('修改订阅失败：订阅不存在 (ID：$subscriptionId)');
      return false;
    }

    try {
      final subscription = _subscriptions[index];
      _subscriptions[index] = subscription.copyWith(
        name: name ?? subscription.name,
        url: url ?? subscription.url,
        autoUpdateMode: autoUpdateMode ?? subscription.autoUpdateMode,
        intervalMinutes: intervalMinutes ?? subscription.intervalMinutes,
        shouldUpdateOnStartup:
            shouldUpdateOnStartup ?? subscription.shouldUpdateOnStartup,
        proxyMode: proxyMode ?? subscription.proxyMode,
        userAgent: userAgent ?? subscription.userAgent,
      );

      await _manager.saveSubscriptionList(_subscriptions);
      notifyListeners();

      // 如果修改了自动更新配置，重新计算定时器
      if (autoUpdateMode != null ||
          intervalMinutes != null ||
          shouldUpdateOnStartup != null) {
        _restartAutoUpdateTimer();
      }

      Logger.info('修改订阅信息成功：${_subscriptions[index].name}');
      return true;
    } catch (e) {
      // 不设置全局错误，只记录日志
      Logger.error('修改订阅失败 (ID：$subscriptionId) - $e');
      return false;
    }
  }

  // 更新订阅的覆写配置
  Future<bool> updateSubscriptionOverrides(
    String subscriptionId,
    List<String> overrideIds,
    List<String> overrideSortPreferences,
  ) async {
    final subscriptionIndex = _subscriptions.indexWhere(
      (s) => s.id == subscriptionId,
    );
    if (subscriptionIndex == -1) {
      Logger.error('更新覆写配置失败：订阅不存在 (ID：$subscriptionId)');
      return false;
    }

    try {
      final subscription = _subscriptions[subscriptionIndex];
      final previousOverrideIds = subscription.overrideIds;

      final addedCount = overrideIds
          .where((id) => !previousOverrideIds.contains(id))
          .length;
      final removedCount = previousOverrideIds
          .where((id) => !overrideIds.contains(id))
          .length;
      final hasOverrideChanges = addedCount > 0 || removedCount > 0;

      Logger.info(
        '更新订阅覆写 - ${subscription.name}: '
        '旧=[${previousOverrideIds.join(", ")}], '
        '新=[${overrideIds.join(", ")}], '
        '${hasOverrideChanges ? "需要重载" : "仅排序"}',
      );

      _subscriptions[subscriptionIndex] = subscription.copyWith(
        overrideIds: overrideIds,
        overrideSortPreferences: overrideSortPreferences,
      );

      await _manager.saveSubscriptionList(_subscriptions);
      notifyListeners();

      if (hasOverrideChanges && subscriptionId == _currentSubscriptionId) {
        Logger.info('当前订阅的覆写已更新，重新加载配置');
        _clashProvider?.pauseConfigWatcher();
        try {
          await _reloadCurrentSubscriptionConfig(reason: '覆写配置更新');
        } finally {
          await _clashProvider?.resumeConfigWatcher();
        }
      }

      return true;
    } catch (e) {
      Logger.error('更新覆写配置失败 (ID：$subscriptionId) - $e');
      return false;
    }
  }

  // 保存订阅文件内容并重载配置
  Future<bool> saveSubscriptionFile(
    String subscriptionId,
    String content,
  ) async {
    final subscription = _subscriptions.firstWhere(
      (s) => s.id == subscriptionId,
      orElse: () => throw Exception('订阅不存在'),
    );

    try {
      // 保存文件到订阅目录
      await _manager.saveLocalSubscription(subscription, content);
      Logger.info('订阅文件已保存：${subscription.name}');

      // 清除配置失败标记（无论是否当前选中）
      final index = _subscriptions.indexWhere((s) => s.id == subscriptionId);
      if (index != -1 && _subscriptions[index].hasConfigLoadFailed) {
        _subscriptions[index] = _subscriptions[index].copyWith(
          hasConfigLoadFailed: false,
        );
        await _manager.saveSubscriptionList(_subscriptions);
        notifyListeners();
        Logger.info('已清除订阅 ${subscription.name} 的配置失败标记');
      }

      // 如果是当前选中的订阅，重新加载配置
      if (subscriptionId == _currentSubscriptionId) {
        Logger.info('当前订阅文件已修改，重新加载配置');
        _clashProvider?.pauseConfigWatcher();
        try {
          await _reloadCurrentSubscriptionConfig(reason: '订阅文件编辑');
        } finally {
          await _clashProvider?.resumeConfigWatcher();
        }
      }

      return true;
    } catch (e) {
      Logger.error('保存订阅文件失败：${subscription.name} - $e');
      return false;
    }
  }

  // 选择订阅
  Future<void> selectSubscription(String subscriptionId) async {
    if (!_subscriptions.any((s) => s.id == subscriptionId)) {
      Logger.warning('尝试选择一个不存在的订阅：$subscriptionId');
      return;
    }

    // 设置切换状态
    _updateState(SubscriptionState.switching());

    _currentSubscriptionId = subscriptionId;
    // 保存选择到持久化存储
    await ClashPreferences.instance.setCurrentSubscriptionId(subscriptionId);
    Logger.info('选择订阅：$subscriptionId');

    try {
      // 重新加载配置文件
      await _reloadCurrentSubscriptionConfig(reason: '订阅切换');
    } finally {
      // 清除切换状态
      _updateState(SubscriptionState.idle());
    }
  }

  // 清除当前选中的订阅（用于默认配置启动成功后，避免应用重启时重新加载失败的配置）
  Future<void> clearCurrentSubscription() async {
    if (_currentSubscriptionId == null) {
      Logger.debug('当前没有选中的订阅，无需清除');
      return;
    }

    final previousId = _currentSubscriptionId;
    _currentSubscriptionId = null;

    // 保存到持久化存储
    await ClashPreferences.instance.setCurrentSubscriptionId(null);

    notifyListeners();

    Logger.info('已清除选中的订阅（ID：$previousId），下次启动将使用默认配置');
  }

  // 重新加载当前订阅的配置文件
  Future<void> _reloadCurrentSubscriptionConfig({
    String reason = '配置重载',
  }) async {
    final configPath = getSubscriptionConfigPath();
    if (configPath == null) {
      Logger.warning('无法获取配置路径，跳过重载');
      return;
    }

    final clashProvider = _clashProvider;
    if (clashProvider == null) {
      Logger.warning('ClashProvider 未设置，跳过重载');
      return;
    }
    Logger.info('$reason，重新加载配置文件：$configPath');

    // 【性能优化】读取原始订阅文件，使用 Stopwatch 监控
    final readStopwatch = Stopwatch()..start();
    String? originalConfigContent;
    try {
      originalConfigContent = await File(configPath).readAsString();
      readStopwatch.stop();
      Logger.debug(
        '读取配置文件完成: ${originalConfigContent.length} 字符 (耗时: ${readStopwatch.elapsedMilliseconds}ms)',
      );
    } catch (e) {
      readStopwatch.stop();
      Logger.error('读取配置文件失败 (耗时：${readStopwatch.elapsedMilliseconds}ms)：$e');
      originalConfigContent = null;
    }

    // 【新架构】获取覆写列表并转换为 Rust 类型
    List<OverrideConfig> overrides = [];
    if (currentSubscription != null &&
        currentSubscription!.overrideIds.isNotEmpty) {
      try {
        Logger.info('准备应用覆写，ID 数量：${currentSubscription!.overrideIds.length}');
        Logger.info('覆写 ID 列表：${currentSubscription!.overrideIds}');

        final appOverrides = await _manager.getOverridesByIds(
          currentSubscription!.overrideIds,
        );
        Logger.info('从服务获取到 ${appOverrides.length} 个覆写配置');

        // 详细检查每个覆写的 content
        for (final appOverride in appOverrides) {
          final hasContent =
              appOverride.content != null && appOverride.content!.isNotEmpty;
          Logger.debug(
            '覆写 ${appOverride.name} (${appOverride.id}): content=${hasContent ? "有内容(${appOverride.content!.length}字符)" : "无内容"}',
          );
        }

        // 检测无效的覆写 ID（文件已被删除但订阅仍引用）
        final validOverrideIds = appOverrides.map((o) => o.id).toSet();
        final invalidIds = currentSubscription!.overrideIds
            .where((id) => !validOverrideIds.contains(id))
            .toList();

        if (invalidIds.isNotEmpty) {
          Logger.warning('检测到 ${invalidIds.length} 个无效的覆写引用：$invalidIds');
          Logger.info('自动清理无效的覆写引用…');

          // 更新订阅配置，移除无效的覆写 ID
          final validIds = currentSubscription!.overrideIds
              .where((id) => validOverrideIds.contains(id))
              .toList();

          final index = _subscriptions.indexWhere(
            (s) => s.id == currentSubscription!.id,
          );
          if (index != -1) {
            _subscriptions[index] = currentSubscription!.copyWith(
              overrideIds: validIds,
            );
            await _manager.saveSubscriptionList(_subscriptions);
            notifyListeners();
            Logger.info('已清理 ${invalidIds.length} 个无效覆写引用');
          }
        }

        // 转换为 Rust OverrideConfig 类型（过滤掉 content 为空的覆写）
        overrides = appOverrides
            .where((appOverride) {
              final hasContent =
                  appOverride.content != null &&
                  appOverride.content!.isNotEmpty;
              if (!hasContent) {
                Logger.warning(
                  '跳过无内容的覆写: ${appOverride.name} (${appOverride.id})',
                );
              }
              return hasContent;
            })
            .map((appOverride) {
              Logger.debug(
                '转换覆写到 Rust 类型: ${appOverride.name} (${appOverride.format.displayName})',
              );
              return OverrideConfig(
                id: appOverride.id,
                name: appOverride.name,
                format: appOverride.format == app_override.OverrideFormat.yaml
                    ? OverrideFormat.yaml
                    : OverrideFormat.javascript,
                content: appOverride.content!,
              );
            })
            .toList();

        Logger.info('成功转换 ${overrides.length} 个覆写到 Rust 类型');
        if (overrides.isEmpty && appOverrides.isNotEmpty) {
          Logger.warning(
            '虽然获取到 ${appOverrides.length} 个覆写，但转换后为 0（可能 content 为空）',
          );
        }
      } catch (e) {
        Logger.error('获取/转换覆写列表失败：$e');
      }
    } else {
      Logger.info('当前订阅无覆写配置');
    }

    // 如果 Clash 正在运行，尝试重载配置
    if (clashProvider.isCoreRunning) {
      Logger.info('Clash 正在运行，尝试重载配置');

      // 【性能监控】记录重载总耗时
      final reloadStopwatch = Stopwatch()..start();

      final reloadSuccess = await clashProvider.clashManager.reloadConfig(
        configPath: configPath,
        overrides: overrides,
      );

      reloadStopwatch.stop();

      if (!reloadSuccess) {
        Logger.error(
          '配置重载失败 (耗时: ${reloadStopwatch.elapsedMilliseconds}ms)，开始回退流程',
        );

        // 获取当前订阅信息
        final failedSubscription = currentSubscription;
        final subscriptionName = failedSubscription?.name ?? '未知订阅';

        // 标记订阅配置失败并保存
        if (failedSubscription != null) {
          final index = _subscriptions.indexWhere(
            (s) => s.id == failedSubscription.id,
          );
          if (index != -1) {
            _subscriptions[index] = failedSubscription.copyWith(
              hasConfigLoadFailed: true,
            );
            await _manager.saveSubscriptionList(_subscriptions);
            Logger.warning('订阅 $subscriptionName 配置加载失败');
          }
        }

        // 取消选中当前订阅，回退到默认配置
        _currentSubscriptionId = null;
        await ClashPreferences.instance.setCurrentSubscriptionId(null);

        // 统一通知 UI 更新（避免多次 notifyListeners）
        notifyListeners();

        Logger.info('已取消选中失败的订阅：$subscriptionName');

        // 显示配置异常提示
        _showToast((context) {
          final trans = context.translate;
          ModernToast.error(
            trans.subscription.config_abnormal.replaceAll(
              '{name}',
              subscriptionName,
            ),
          );
        });

        // 尝试使用默认配置重载核心
        Logger.info('尝试使用默认配置重载核心');
        final emptyReloadSuccess = await clashProvider.clashManager
            .reloadWithEmptyConfig();

        if (emptyReloadSuccess) {
          Logger.info('默认配置重载成功');
          _showToast((context) {
            final trans = context.translate;
            ModernToast.success(trans.subscription.fallback_to_default_config);
          });
        } else {
          Logger.error('默认配置重载失败，尝试使用默认配置重启核心');

          // 使用默认配置重启核心
          final emptyRestartSuccess = await clashProvider.clashManager
              .restartWithEmptyConfig();

          if (emptyRestartSuccess) {
            Logger.info('默认配置重启成功');
            _showToast((context) {
              final trans = context.translate;
              ModernToast.success(
                trans.subscription.core_restarted_with_default_config,
              );
            });
          } else {
            Logger.error('默认配置重启失败');
            _showToast((context) {
              final trans = context.translate;
              ModernToast.error(trans.subscription.core_restart_failed);
            });
          }
        }
      } else {
        Logger.info('配置重载成功 (耗时: ${reloadStopwatch.elapsedMilliseconds}ms)');

        // 清除配置失败标记
        if (currentSubscription != null &&
            currentSubscription!.hasConfigLoadFailed) {
          final index = _subscriptions.indexWhere(
            (s) => s.id == currentSubscription!.id,
          );
          if (index != -1) {
            _subscriptions[index] = currentSubscription!.copyWith(
              hasConfigLoadFailed: false,
            );
            await _manager.saveSubscriptionList(_subscriptions);
            notifyListeners();
            Logger.info('已清除订阅 ${currentSubscription!.name} 的配置失败标记');
          }
        }

        // 重载成功后，从 Clash API 重新加载代理信息
        final proxyLoadStopwatch = Stopwatch()..start();
        try {
          await clashProvider.loadProxies();
          proxyLoadStopwatch.stop();
          Logger.info(
            '代理信息已从 Clash API 重新加载 (耗时: ${proxyLoadStopwatch.elapsedMilliseconds}ms)',
          );
        } catch (e) {
          proxyLoadStopwatch.stop();
          Logger.error(
            '重新加载代理信息失败 (耗时: ${proxyLoadStopwatch.elapsedMilliseconds}ms): $e',
          );
        }
      }
    } else {
      Logger.warning('Clash 未运行，无法重载配置');
    }
  }

  // 获取订阅配置文件路径
  String? getSubscriptionConfigPath() {
    if (currentSubscription == null) {
      return null;
    }

    // 返回当前选中订阅的配置文件路径
    return PathService.instance.getSubscriptionConfigPath(
      currentSubscription!.id,
    );
  }

  // 验证配置文件是否存在
  Future<bool> validateSubscriptionConfig() async {
    final configPath = getSubscriptionConfigPath();
    if (configPath == null) {
      Logger.warning('无法验证配置文件：配置路径为空');
      return false;
    }

    try {
      final file = File(configPath);
      final exists = await file.exists();

      if (!exists) {
        Logger.warning('订阅配置文件不存在：$configPath');
      }

      return exists;
    } catch (e) {
      Logger.error('验证配置文件失败：$e');
      return false;
    }
  }

  // 验证 URL 是否有效
  bool _isValidUrl(String url) {
    if (url.trim().isEmpty) {
      return false;
    }

    try {
      final uri = Uri.parse(url);
      // 检查是否有协议（http/https）
      if (!uri.hasScheme || (uri.scheme != 'http' && uri.scheme != 'https')) {
        return false;
      }
      // 检查是否有主机名
      if (!uri.hasAuthority || uri.host.isEmpty) {
        return false;
      }
      // 检查主机名是否合法（至少包含一个点，或者是 localhost/127.0.0.1）
      if (uri.host != 'localhost' &&
          uri.host != '127.0.0.1' &&
          !uri.host.contains('.')) {
        return false;
      }
      return true;
    } catch (e) {
      Logger.error('URL 解析失败：$url - $e');
      return false;
    }
  }

  // 设置与覆写系统的集成（回调和清理）
  Future<void> setupOverrideIntegration(
    OverrideProvider overrideProvider,
  ) async {
    // 1. 设置覆写获取回调
    setOverrideGetter((ids) async {
      Logger.debug('获取覆写配置 (setOverrideGetter 回调)');
      Logger.debug('请求的覆写 ID 列表：$ids');

      final overrides = <app_override.OverrideConfig>[];
      for (final id in ids) {
        Logger.debug('尝试获取覆写：$id');
        final override = overrideProvider.getOverrideById(id);
        if (override != null) {
          Logger.debug(
            '找到覆写: ${override.name} (${override.format.displayName})',
          );
          overrides.add(override);
        } else {
          Logger.warning('覆写不存在：$id');
        }
      }

      Logger.debug('共找到 ${overrides.length} 个覆写配置');
      return overrides;
    });

    // 2. 设置覆写删除回调（清理订阅引用）
    overrideProvider.setOnOverrideDeleted((overrideId) async {
      Logger.debug('覆写被删除，清理订阅引用：$overrideId');
      await removeOverrideFromAllSubscriptions(overrideId);
    });

    // 2.5 设置覆写内容更新回调（重载使用该覆写的当前订阅）
    overrideProvider.setOnOverrideContentUpdated((overrideId) async {
      try {
        Logger.debug('覆写内容已更新：$overrideId');
        await _reloadSubscriptionIfOverrideUsed(overrideId);
      } catch (e) {
        Logger.error('重载配置失败：$e');
        // 不抛出异常，避免影响覆写文件保存操作
      }
    });

    // 3. 清理无效的覆写引用（启动时一次性清理）
    await cleanupInvalidOverrideReferences((ids) async {
      final overrides = <app_override.OverrideConfig>[];
      for (final id in ids) {
        final override = overrideProvider.getOverrideById(id);
        if (override != null) {
          overrides.add(override);
        }
      }
      return overrides;
    });
  }

  // Toast 辅助方法：获取 context 并显示消息
  void _showToast(void Function(BuildContext context) show) {
    final context = ModernToast.navigatorKey.currentContext;
    if (context != null && context.mounted) {
      show(context);
    }
  }

  @override
  void dispose() {
    // 取消自动更新定时器
    _autoUpdateTimer?.cancel();
    Logger.debug('自动更新定时器已取消');

    super.dispose();
  }
}

// 简单的信号量实现，用于限制并发数
class _Semaphore {
  int _currentCount;
  final List<Completer<void>> _waitQueue = [];

  _Semaphore(int maxCount) : _currentCount = maxCount;

  // 获取许可（如果没有可用许可则等待）
  Future<void> acquire() async {
    if (_currentCount > 0) {
      _currentCount--;
      return;
    }

    final completer = Completer<void>();
    _waitQueue.add(completer);
    return completer.future;
  }

  // 释放许可
  void release() {
    if (_waitQueue.isNotEmpty) {
      final completer = _waitQueue.removeAt(0);
      completer.complete();
    } else {
      _currentCount++;
    }
  }
}
