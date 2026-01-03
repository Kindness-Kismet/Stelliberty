import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:stelliberty/utils/logger.dart';

// 订阅操作状态枚举
enum SubscriptionOperationState {
  // 空闲状态
  idle,

  // 正在加载
  loading,

  // 正在切换订阅
  switching,

  // 正在更新单个订阅
  updating,

  // 正在批量更新
  batchUpdating,

  // 正在自动更新
  autoUpdating,
}

// 订阅错误状态枚举
enum SubscriptionErrorState {
  // 无错误
  none,

  // 初始化错误
  initializationError,

  // 网络错误
  networkError,

  // 配置错误
  configError,

  // 文件系统错误
  fileSystemError,

  // 未知错误
  unknownError,
}

// 订阅状态扩展方法
extension SubscriptionOperationStateExtension on SubscriptionOperationState {
  // 是否为空闲状态
  bool get isIdle => this == SubscriptionOperationState.idle;

  // 是否正在加载
  bool get isLoading => this == SubscriptionOperationState.loading;

  // 是否正在切换订阅
  bool get isSwitching => this == SubscriptionOperationState.switching;

  // 是否正在更新（任何类型的更新）
  bool get isUpdating =>
      this == SubscriptionOperationState.updating ||
      this == SubscriptionOperationState.batchUpdating ||
      this == SubscriptionOperationState.autoUpdating;

  // 是否正在批量更新
  bool get isBatchUpdating => this == SubscriptionOperationState.batchUpdating;

  // 是否正在自动更新
  bool get isAutoUpdating => this == SubscriptionOperationState.autoUpdating;

  // 是否为忙碌状态（非空闲）
  bool get isBusy => !isIdle;
}

// 订阅状态变化事件
class SubscriptionStateChangeEvent {
  final SubscriptionOperationState previousOperationState;
  final SubscriptionOperationState currentOperationState;
  final SubscriptionErrorState previousErrorState;
  final SubscriptionErrorState currentErrorState;
  final DateTime timestamp;
  final String? reason;

  const SubscriptionStateChangeEvent({
    required this.previousOperationState,
    required this.currentOperationState,
    required this.previousErrorState,
    required this.currentErrorState,
    required this.timestamp,
    this.reason,
  });

  @override
  String toString() {
    final operationChanged = previousOperationState != currentOperationState;
    final errorChanged = previousErrorState != currentErrorState;

    final parts = <String>[];
    if (operationChanged) {
      parts.add(
        '操作状态：${previousOperationState.name} -> ${currentOperationState.name}',
      );
    }
    if (errorChanged) {
      parts.add('错误状态：${previousErrorState.name} -> ${currentErrorState.name}');
    }

    final changes = parts.join('，');
    return '订阅状态变化事件($changes${reason != null ? '，原因：$reason' : ''})';
  }
}

// 更新进度信息
class UpdateProgress {
  final int current;
  final int total;
  final String? currentItemName;

  const UpdateProgress({
    required this.current,
    required this.total,
    this.currentItemName,
  });

  // 是否正在更新
  bool get isUpdating => total > 0 && current < total;

  // 更新进度百分比（0-100）
  double get percentage => total > 0 ? (current / total * 100) : 0.0;

  // 是否已完成
  bool get isCompleted => total > 0 && current >= total;

  @override
  String toString() {
    return 'UpdateProgress($current/$total${currentItemName != null ? '，当前：$currentItemName' : ''})';
  }
}

// 订阅状态管理器
class SubscriptionStateManager extends ChangeNotifier {
  static final SubscriptionStateManager _instance =
      SubscriptionStateManager._internal();
  static SubscriptionStateManager get instance => _instance;
  SubscriptionStateManager._internal();

  // 当前状态
  SubscriptionOperationState _operationState = SubscriptionOperationState.idle;
  SubscriptionErrorState _errorState = SubscriptionErrorState.none;
  String? _errorMessage;
  UpdateProgress _updateProgress = const UpdateProgress(current: 0, total: 0);

  // 单个订阅更新状态追踪
  final Set<String> _updatingSubscriptionIds = <String>{};

  // 状态变化事件流
  final StreamController<SubscriptionStateChangeEvent> _stateChangeController =
      StreamController<SubscriptionStateChangeEvent>.broadcast();

  // 当前操作状态
  SubscriptionOperationState get operationState => _operationState;

  // 当前错误状态
  SubscriptionErrorState get errorState => _errorState;

  // 错误消息
  String? get errorMessage => _errorMessage;

  // 更新进度
  UpdateProgress get updateProgress => _updateProgress;

  // 状态变化事件流
  Stream<SubscriptionStateChangeEvent> get stateChangeStream =>
      _stateChangeController.stream;

  // 便捷方法 - 是否为空闲状态
  bool get isIdle => _operationState.isIdle;

  // 便捷方法 - 是否正在加载
  bool get isLoading => _operationState.isLoading;

  // 便捷方法 - 是否正在更新
  bool get isUpdating => _operationState.isUpdating;

  // 便捷方法 - 是否正在批量更新
  bool get isBatchUpdating => _operationState.isBatchUpdating;

  // 便捷方法 - 是否正在自动更新
  bool get isAutoUpdating => _operationState.isAutoUpdating;

  // 便捷方法 - 是否正在切换订阅
  bool get isSwitchingSubscription => _operationState.isSwitching;

  // 便捷方法 - 是否为忙碌状态
  bool get isBusy => _operationState.isBusy;

  // 便捷方法 - 是否有错误
  bool get hasError => _errorState != SubscriptionErrorState.none;

  // 检查指定订阅是否正在更新
  bool isSubscriptionUpdating(String subscriptionId) {
    return _updatingSubscriptionIds.contains(subscriptionId);
  }

  // 获取正在更新的订阅ID列表
  Set<String> get updatingSubscriptionIds =>
      Set.unmodifiable(_updatingSubscriptionIds);

  // 更新状态
  void _updateState({
    SubscriptionOperationState? operationState,
    SubscriptionErrorState? errorState,
    String? errorMessage,
    UpdateProgress? updateProgress,
    String? reason,
  }) {
    final previousOperationState = _operationState;
    final previousErrorState = _errorState;

    // 更新状态
    if (operationState != null) _operationState = operationState;
    if (errorState != null) _errorState = errorState;
    if (errorMessage != null) _errorMessage = errorMessage;
    if (updateProgress != null) _updateProgress = updateProgress;

    // 清除错误消息（如果错误状态变为none）
    if (_errorState == SubscriptionErrorState.none) {
      _errorMessage = null;
    }

    // 发送状态变化事件
    if (previousOperationState != _operationState ||
        previousErrorState != _errorState) {
      final event = SubscriptionStateChangeEvent(
        previousOperationState: previousOperationState,
        currentOperationState: _operationState,
        previousErrorState: previousErrorState,
        currentErrorState: _errorState,
        timestamp: DateTime.now(),
        reason: reason,
      );

      _stateChangeController.add(event);

      // 注意：状态变化已由 StateHub 统一处理和打印，这里不再重复打印
      // 避免与 StateHub 的日志重复
    }

    notifyListeners();
  }

  // 设置为空闲状态
  void setIdle({String? reason}) {
    _updateState(
      operationState: SubscriptionOperationState.idle,
      errorState: SubscriptionErrorState.none,
      updateProgress: const UpdateProgress(current: 0, total: 0),
      reason: reason ?? '设置为空闲状态',
    );
  }

  // 设置为加载状态
  void setLoading({String? reason}) {
    _updateState(
      operationState: SubscriptionOperationState.loading,
      errorState: SubscriptionErrorState.none,
      reason: reason ?? '开始加载',
    );
  }

  // 设置为更新状态
  void setUpdating({String? reason}) {
    _updateState(
      operationState: SubscriptionOperationState.updating,
      errorState: SubscriptionErrorState.none,
      reason: reason ?? '开始更新',
    );
  }

  // 设置为批量更新状态
  void setBatchUpdating({required int total, String? reason}) {
    _updateState(
      operationState: SubscriptionOperationState.batchUpdating,
      errorState: SubscriptionErrorState.none,
      updateProgress: UpdateProgress(current: 0, total: total),
      reason: reason ?? '开始批量更新',
    );
  }

  // 设置为自动更新状态
  void setAutoUpdating({String? reason}) {
    _updateState(
      operationState: SubscriptionOperationState.autoUpdating,
      errorState: SubscriptionErrorState.none,
      reason: reason ?? '开始自动更新',
    );
  }

  // 设置为切换订阅状态
  void setSwitching({String? reason}) {
    _updateState(
      operationState: SubscriptionOperationState.switching,
      errorState: SubscriptionErrorState.none,
      reason: reason ?? '开始切换订阅',
    );
  }

  // 更新批量更新进度
  void updateBatchProgress({
    required int current,
    String? currentItemName,
    String? reason,
  }) {
    if (!isBatchUpdating) return;

    _updateState(
      updateProgress: UpdateProgress(
        current: current,
        total: _updateProgress.total,
        currentItemName: currentItemName,
      ),
      reason: reason,
    );
  }

  // 设置错误状态
  void setError({
    required SubscriptionErrorState errorState,
    String? errorMessage,
    String? reason,
  }) {
    _updateState(
      operationState: SubscriptionOperationState.idle,
      errorState: errorState,
      errorMessage: errorMessage,
      reason: reason ?? '发生错误',
    );
  }

  // 添加正在更新的订阅ID
  void addUpdatingSubscription(String subscriptionId, {String? reason}) {
    if (_updatingSubscriptionIds.add(subscriptionId)) {
      Logger.debug(
        '订阅状态：添加更新中的订阅 $subscriptionId${reason != null ? '，原因：$reason' : ''}',
      );
      notifyListeners();
    }
  }

  // 移除正在更新的订阅ID
  void removeUpdatingSubscription(String subscriptionId, {String? reason}) {
    if (_updatingSubscriptionIds.remove(subscriptionId)) {
      Logger.debug(
        '订阅状态：移除更新中的订阅 $subscriptionId${reason != null ? '，原因：$reason' : ''}',
      );
      notifyListeners();
    }
  }

  // 清除所有正在更新的订阅ID
  void clearUpdatingSubscriptions({String? reason}) {
    if (_updatingSubscriptionIds.isNotEmpty) {
      _updatingSubscriptionIds.clear();
      Logger.debug('订阅状态：清除所有更新中的订阅${reason != null ? '，原因：$reason' : ''}');
      notifyListeners();
    }
  }

  // 重置状态管理器
  void reset({String? reason}) {
    _updatingSubscriptionIds.clear();
    _updateState(
      operationState: SubscriptionOperationState.idle,
      errorState: SubscriptionErrorState.none,
      updateProgress: const UpdateProgress(current: 0, total: 0),
      reason: reason ?? '重置状态管理器',
    );
  }

  @override
  void dispose() {
    _stateChangeController.close();
    super.dispose();
  }
}
