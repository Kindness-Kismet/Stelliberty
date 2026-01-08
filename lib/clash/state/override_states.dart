// 覆写状态定义

// ============================================
// 覆写操作状态
// ============================================

enum OverrideOperationState {
  // 空闲状态
  idle,

  // 正在加载
  loading,

  // 正在更新单个覆写
  updating,

  // 正在批量更新
  batchUpdating,

  // 正在下载远程覆写
  downloading,
}

// 覆写错误状态
enum OverrideErrorState {
  // 无错误
  none,

  // 初始化错误
  initializationError,

  // 网络错误
  networkError,

  // 文件系统错误
  fileSystemError,

  // 格式错误
  formatError,

  // 未知错误
  unknownError,
}

// 覆写状态扩展方法
extension OverrideOperationStateExtension on OverrideOperationState {
  // 是否为空闲状态
  bool get isIdle => this == OverrideOperationState.idle;

  // 是否正在加载
  bool get isLoading => this == OverrideOperationState.loading;

  // 是否正在更新（任何类型的更新）
  bool get isUpdating =>
      this == OverrideOperationState.updating ||
      this == OverrideOperationState.batchUpdating ||
      this == OverrideOperationState.downloading;

  // 是否正在批量更新
  bool get isBatchUpdating => this == OverrideOperationState.batchUpdating;

  // 是否正在下载
  bool get isDownloading => this == OverrideOperationState.downloading;

  // 是否为忙碌状态（非空闲）
  bool get isBusy => !isIdle;
}

// ============================================
// 覆写状态数据类
// ============================================

class OverrideState {
  final OverrideOperationState operationState;
  final OverrideErrorState errorState;
  final String? errorMessage;
  final int updateCurrent;
  final int updateTotal;
  final String? currentItemName;
  final Set<String> updatingIds;

  const OverrideState({
    this.operationState = OverrideOperationState.idle,
    this.errorState = OverrideErrorState.none,
    this.errorMessage,
    this.updateCurrent = 0,
    this.updateTotal = 0,
    this.currentItemName,
    this.updatingIds = const {},
  });

  bool get isIdle => operationState.isIdle;
  bool get isLoading => operationState.isLoading;
  bool get isUpdating => operationState.isUpdating;
  bool get isBatchUpdating => operationState.isBatchUpdating;
  bool get isDownloading => operationState.isDownloading;
  bool get isBusy => operationState.isBusy;
  bool get hasError => errorState != OverrideErrorState.none;
  bool get isUpdateInProgress => updateTotal > 0 && updateCurrent < updateTotal;
  double get updatePercentage =>
      updateTotal > 0 ? (updateCurrent / updateTotal * 100) : 0.0;

  // 检查指定覆写是否正在更新
  bool isOverrideUpdating(String overrideId) {
    return updatingIds.contains(overrideId);
  }

  // 不可变更新方法
  OverrideState copyWith({
    OverrideOperationState? operationState,
    OverrideErrorState? errorState,
    String? errorMessage,
    int? updateCurrent,
    int? updateTotal,
    String? currentItemName,
    Set<String>? updatingIds,
    bool clearError = false,
  }) {
    return OverrideState(
      operationState: operationState ?? this.operationState,
      errorState: clearError
          ? OverrideErrorState.none
          : (errorState ?? this.errorState),
      errorMessage: clearError ? null : (errorMessage ?? this.errorMessage),
      updateCurrent: updateCurrent ?? this.updateCurrent,
      updateTotal: updateTotal ?? this.updateTotal,
      currentItemName: currentItemName ?? this.currentItemName,
      updatingIds: updatingIds ?? this.updatingIds,
    );
  }

  // 便捷工厂方法
  static OverrideState idle() => const OverrideState();

  static OverrideState loading() =>
      const OverrideState(operationState: OverrideOperationState.loading);

  static OverrideState updating() =>
      const OverrideState(operationState: OverrideOperationState.updating);

  static OverrideState downloading() =>
      const OverrideState(operationState: OverrideOperationState.downloading);

  static OverrideState batchUpdating(int total) => OverrideState(
    operationState: OverrideOperationState.batchUpdating,
    updateTotal: total,
    updateCurrent: 0,
  );

  static OverrideState error(OverrideErrorState errorState, String? message) =>
      OverrideState(errorState: errorState, errorMessage: message);
}
