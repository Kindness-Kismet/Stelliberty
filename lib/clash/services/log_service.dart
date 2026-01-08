import 'dart:async';
import 'package:stelliberty/clash/model/log_message_model.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// Clash 核心日志服务
// 通过 IPC WebSocket（Rust 信号）获取实时日志数据
class ClashLogService {
  static final ClashLogService instance = ClashLogService._();
  ClashLogService._() {
    // 在构造时就创建 broadcast StreamController，确保外部可以随时订阅
    _controller = StreamController<ClashLogMessage>.broadcast();
  }

  StreamSubscription? _logSubscription;
  late StreamController<ClashLogMessage> _controller;
  bool _isMonitoring = false;
  ClashLogLevel _currentLogLevel = ClashLogLevel.info;

  // 日志数据流（供外部监听）
  Stream<ClashLogMessage> get logStream => _controller.stream;

  // 是否正在监控
  bool get isMonitoring => _isMonitoring;

  // 当前日志级别
  ClashLogLevel get currentLogLevel => _currentLogLevel;

  // 开始监控日志（IPC 模式）
  // 注意：不再需要 baseUrl 参数，IPC 通信由 Rust 处理
  Future<void> startMonitoring([String? _]) async {
    if (_isMonitoring) {
      return;
    }

    _isMonitoring = true;

    // 从设置中读取日志等级
    final logLevelString = ClashPreferences.instance.getCoreLogLevel();
    _currentLogLevel = ClashLogLevelExtension.fromString(logLevelString);

    // 如果日志级别是 silent，不启动监控
    if (_currentLogLevel == ClashLogLevel.silent) {
      Logger.info('日志级别为 silent，不启动日志监控');
      _isMonitoring = false;
      return;
    }

    Logger.info('开始 Clash 日志监控 (IPC 模式，级别：${_currentLogLevel.toApiParam()})');

    // 监听来自 Rust 的日志数据
    _logSubscription = IpcLogData.rustSignalStream.listen((signal) {
      _handleLogData(signal.message);
    });

    // 发送启动日志监控信号到 Rust
    const StartLogStream().sendSignalToRust();
  }

  // 停止监控日志
  Future<void> stopMonitoring() async {
    if (!_isMonitoring) {
      return;
    }

    Logger.info('停止 Clash 日志监控');
    _isMonitoring = false;

    // 发送停止信号到 Rust
    const StopLogStream().sendSignalToRust();

    // 取消 Rust 流订阅
    await _logSubscription?.cancel();
    _logSubscription = null;

    // 不关闭 StreamController，保持流活动以便外部持续订阅
    // StreamController 在整个应用生命周期内保持活动
  }

  // 更新日志级别（无需重启连接，核心会自动调整输出）
  Future<void> updateLogLevel(ClashLogLevel level) async {
    if (_currentLogLevel == level) {
      return;
    }

    final oldLevel = _currentLogLevel;
    _currentLogLevel = level;

    Logger.info('日志级别已更新：${oldLevel.toApiParam()} -> ${level.toApiParam()}');

    // 注意：
    // 1. 日志级别通过 API 的 setLogLevel() 已经热更新到核心
    // 2. WebSocket 连接保持活动，无需重启
    // 3. 核心会自动根据新的日志级别过滤输出
    // 4. 只有在 silent <-> 其他级别切换时，才需要启停监控

    if (oldLevel == ClashLogLevel.silent && level != ClashLogLevel.silent) {
      // 从 silent 切换到其他级别：启动监控
      Logger.info('从 silent 切换到 ${level.toApiParam()}，启动日志监控');
      await startMonitoring();
    } else if (oldLevel != ClashLogLevel.silent &&
        level == ClashLogLevel.silent) {
      // 从其他级别切换到 silent：停止监控
      Logger.info('切换到 silent，停止日志监控');
      await stopMonitoring();
    } else {
      // 其他级别之间切换：无需任何操作，核心自动调整
      Logger.info('日志级别热更新完成，连接保持活动');
    }
  }

  // 处理来自 Rust 的日志数据
  void _handleLogData(IpcLogData data) {
    try {
      // 将 Rust 日志数据转换为 ClashLogMessage
      final logMessage = ClashLogMessage(
        type: data.logType,
        payload: data.payload,
      );

      // 直接推送到流（由 LogProvider 负责缓存）
      _controller.add(logMessage);
    } catch (e) {
      Logger.error('处理日志数据失败：$e');
    }
  }

  // 清理资源
  void dispose() {
    stopMonitoring();
  }
}
