import 'dart:async';
import 'dart:collection';
import 'package:stelliberty/clash/data/traffic_data_model.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// Clash 流量监控服务
// 通过 IPC WebSocket（Rust 信号）获取实时流量数据
class TrafficMonitor {
  static final TrafficMonitor instance = TrafficMonitor._();
  TrafficMonitor._();

  StreamController<TrafficData>? _controller;
  StreamSubscription? _rustStreamSubscription;
  bool _isMonitoring = false;

  // 累计流量统计
  int _totalUpload = 0;
  int _totalDownload = 0;
  DateTime? _lastTimestamp;

  // 缓存最后一次的流量数据，避免组件重建时显示零值
  TrafficData? _trafficDataCache;

  // 波形图历史数据（全局存储，避免页面切换时重置）
  final List<double> _uploadHistory = List.generate(30, (_) => 0.0);
  final List<double> _downloadHistory = List.generate(30, (_) => 0.0);

  // 流量数据流（供外部监听）
  Stream<TrafficData>? get trafficStream => _controller?.stream;

  // 获取波形图历史数据（只读视图，零复制）
  List<double> get uploadHistory => UnmodifiableListView(_uploadHistory);
  List<double> get downloadHistory => UnmodifiableListView(_downloadHistory);

  // 是否正在监控
  bool get isMonitoring => _isMonitoring;

  // 获取当前累计流量
  int get totalUpload => _totalUpload;
  int get totalDownload => _totalDownload;

  // 获取最后一次的流量数据（用于组件初始化）
  TrafficData? get lastTrafficData => _trafficDataCache;

  // 重置累计流量
  void resetTotalTraffic() {
    _totalUpload = 0;
    _totalDownload = 0;
    _lastTimestamp = null;
    _trafficDataCache = null;
    // 清空波形图历史数据
    _uploadHistory.fillRange(0, _uploadHistory.length, 0);
    _downloadHistory.fillRange(0, _downloadHistory.length, 0);
    Logger.info('累计流量已重置');
  }

  // 开始监控流量（IPC 模式）
  // 注意：不再需要 baseUrl 参数，IPC 通信由 Rust 处理
  Future<void> startMonitoring([String? _]) async {
    if (_isMonitoring) return;

    _isMonitoring = true;

    // 创建流控制器
    _controller ??= StreamController<TrafficData>.broadcast();

    // 监听来自 Rust 的流量数据
    _rustStreamSubscription = IpcTrafficData.rustSignalStream.listen((signal) {
      _handleTrafficData(signal.message);
    });

    // 发送启动流量监控信号到 Rust
    const StartTrafficStream().sendSignalToRust();
    Logger.info('流量监控已启动 (IPC 模式)');
  }

  // 停止监控流量
  Future<void> stopMonitoring() async {
    if (!_isMonitoring) return;

    _isMonitoring = false;

    // 发送停止信号到 Rust
    const StopTrafficStream().sendSignalToRust();

    // 取消 Rust 流订阅
    await _rustStreamSubscription?.cancel();
    _rustStreamSubscription = null;

    // 关闭流控制器
    await _controller?.close();
    _controller = null;

    Logger.info('流量监控已停止');
  }

  // 处理来自 Rust 的流量数据
  void _handleTrafficData(IpcTrafficData data) {
    try {
      // 将 Uint64 转换为 int
      final uploadInt = data.upload.toInt();
      final downloadInt = data.download.toInt();

      // 累计流量统计（基于时间间隔估算）
      final now = DateTime.now();
      if (_lastTimestamp != null) {
        final interval =
            now.difference(_lastTimestamp!).inMilliseconds / 1000.0;
        // 使用当前速度和时间间隔估算流量增量
        if (interval > 0 && interval < 10) {
          // 只在合理的时间间隔内累加（避免异常值）
          _totalUpload += (uploadInt * interval).round();
          _totalDownload += (downloadInt * interval).round();
        }
      }
      _lastTimestamp = now;

      // 创建 TrafficData 对象
      final trafficData = TrafficData(
        upload: uploadInt,
        download: downloadInt,
        timestamp: now,
        totalUpload: _totalUpload,
        totalDownload: _totalDownload,
      );

      // 更新波形图历史数据
      _uploadHistory.removeAt(0);
      _uploadHistory.add(uploadInt / 1024.0); // KB/s
      _downloadHistory.removeAt(0);
      _downloadHistory.add(downloadInt / 1024.0); // KB/s

      // 缓存最后的数据
      _trafficDataCache = trafficData;

      // 推送到流
      _controller?.add(trafficData);
    } catch (e) {
      Logger.error('处理流量数据失败：$e');
    }
  }

  // 清理资源
  void dispose() {
    stopMonitoring();
  }
}
