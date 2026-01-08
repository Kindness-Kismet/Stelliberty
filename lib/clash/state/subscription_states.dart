// 订阅状态定义

// ============================================
// 订阅操作状态
// ============================================

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

// 订阅错误状态（详细分类）
enum SubscriptionErrorState {
  // 无错误
  none,

  // 网络连接错误
  network,

  // 超时
  timeout,

  // 404 未找到
  notFound,

  // 403/401 访问被拒绝
  forbidden,

  // 服务器错误（5xx）
  serverError,

  // 配置格式错误
  formatError,

  // 证书错误
  certificate,

  // 初始化错误
  initializationError,

  // 文件系统错误
  fileSystemError,

  // 未知错误
  unknown,
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

// ============================================
// 订阅状态数据类
// ============================================

class SubscriptionState {
  final SubscriptionOperationState operationState;
  final SubscriptionErrorState errorState;
  final String? errorMessage;
  final int updateCurrent;
  final int updateTotal;
  final Set<String> updatingIds;

  const SubscriptionState({
    this.operationState = SubscriptionOperationState.idle,
    this.errorState = SubscriptionErrorState.none,
    this.errorMessage,
    this.updateCurrent = 0,
    this.updateTotal = 0,
    this.updatingIds = const {},
  });

  bool get isIdle => operationState.isIdle;
  bool get isLoading => operationState.isLoading;
  bool get isSwitching => operationState.isSwitching;
  bool get isUpdating => operationState.isUpdating;
  bool get isBatchUpdating => operationState.isBatchUpdating;
  bool get isBusy => operationState.isBusy;
  bool get hasError => errorState != SubscriptionErrorState.none;
  bool get isUpdateInProgress => updateTotal > 0 && updateCurrent < updateTotal;
  double get updatePercentage =>
      updateTotal > 0 ? (updateCurrent / updateTotal * 100) : 0.0;

  // 检查指定订阅是否正在更新
  bool isSubscriptionUpdating(String subscriptionId) {
    return updatingIds.contains(subscriptionId);
  }

  // 不可变更新方法
  SubscriptionState copyWith({
    SubscriptionOperationState? operationState,
    SubscriptionErrorState? errorState,
    String? errorMessage,
    int? updateCurrent,
    int? updateTotal,
    Set<String>? updatingIds,
    bool clearError = false,
  }) {
    return SubscriptionState(
      operationState: operationState ?? this.operationState,
      errorState: clearError
          ? SubscriptionErrorState.none
          : (errorState ?? this.errorState),
      errorMessage: clearError ? null : (errorMessage ?? this.errorMessage),
      updateCurrent: updateCurrent ?? this.updateCurrent,
      updateTotal: updateTotal ?? this.updateTotal,
      updatingIds: updatingIds ?? this.updatingIds,
    );
  }

  // 便捷工厂方法
  static SubscriptionState idle() => const SubscriptionState();

  static SubscriptionState loading() => const SubscriptionState(
    operationState: SubscriptionOperationState.loading,
  );

  static SubscriptionState switching() => const SubscriptionState(
    operationState: SubscriptionOperationState.switching,
  );

  static SubscriptionState updating() => const SubscriptionState(
    operationState: SubscriptionOperationState.updating,
  );

  static SubscriptionState batchUpdating(int total) => SubscriptionState(
    operationState: SubscriptionOperationState.batchUpdating,
    updateTotal: total,
    updateCurrent: 0,
  );

  static SubscriptionState error(
    SubscriptionErrorState errorState,
    String? message,
  ) => SubscriptionState(errorState: errorState, errorMessage: message);
}
