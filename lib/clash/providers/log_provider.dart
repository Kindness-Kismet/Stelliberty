import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:stelliberty/clash/data/log_message_model.dart';
import 'package:stelliberty/clash/services/log_service.dart';
import 'package:stelliberty/utils/logger.dart';

// 日志状态管理
// 统一管理所有日志相关的状态，确保切换页面时数据不丢失
class LogProvider extends ChangeNotifier {
  final List<ClashLogMessage> _logs = [];
  final List<ClashLogMessage> _pendingLogs = [];

  StreamSubscription<ClashLogMessage>? _logSubscription;
  Timer? _batchUpdateTimer;
  Timer? _searchDebounceTimer; // 搜索防抖定时器

  static const _batchUpdateInterval = Duration(milliseconds: 200);
  static const _maxBatchInterval = Duration(milliseconds: 500); // 最大间隔 500ms
  static const _batchThreshold = 1;
  static const _maxLogsCount = 2000; // 最多保留 2000 条日志

  // 过滤和控制状态
  bool _isPaused = false;
  ClashLogLevel? _filterLevel;
  String _searchKeyword = '';

  // 加载状态（用于异步加载历史日志）
  bool _isLoading = false;

  // 过滤结果缓存
  List<ClashLogMessage>? _cachedFilteredLogs;
  String? _cacheKey;

  // 上次刷新时间（用于动态批量更新）
  DateTime _lastFlushTime = DateTime.now();

  // Getters
  List<ClashLogMessage> get logs => List.unmodifiable(_logs);
  bool get isPaused => _isPaused;
  ClashLogLevel? get filterLevel => _filterLevel;
  String get searchKeyword => _searchKeyword;
  bool get isLoading => _isLoading;

  // 获取过滤后的日志列表（带缓存优化）
  List<ClashLogMessage> get filteredLogs {
    // 生成缓存键（基于过滤条件和日志数量）
    final cacheKey = '${_filterLevel}_${_searchKeyword}_${_logs.length}';

    // 如果缓存有效，直接返回
    if (_cachedFilteredLogs != null && _cacheKey == cacheKey) {
      return _cachedFilteredLogs!;
    }

    // 重新计算过滤结果
    _cachedFilteredLogs = _logs.where((log) {
      // 级别过滤
      if (_filterLevel != null && log.level != _filterLevel) {
        return false;
      }
      // 搜索关键词过滤
      if (_searchKeyword.isNotEmpty) {
        final keyword = _searchKeyword.toLowerCase();
        return log.payload.toLowerCase().contains(keyword) ||
            log.type.toLowerCase().contains(keyword);
      }
      return true;
    }).toList();

    _cacheKey = cacheKey;
    return _cachedFilteredLogs!;
  }

  // 清除缓存（在过滤条件变化时调用）
  void _invalidateCache() {
    _cachedFilteredLogs = null;
    _cacheKey = null;
  }

  // 初始化 Provider
  void initialize() {
    Logger.info('LogProvider: 开始初始化');

    // 启动批量更新定时器
    _startBatchUpdateTimer();

    // 订阅日志流
    _subscribeToLogStream();

    // 异步加载历史日志（不阻塞 UI）
    _loadHistoryLogsAsync();

    Logger.info('LogProvider: 初始化完成（历史日志异步加载中）');
  }

  // 异步加载历史日志（不阻塞 UI）
  Future<void> _loadHistoryLogsAsync() async {
    _isLoading = true;
    notifyListeners(); // 立即通知 UI 进入加载状态

    // 使用 microtask 确保 UI 先渲染
    await Future.microtask(() {});

    // LogService 不再维护历史缓存，直接标记加载完成
    // 日志将在用户打开页面后实时接收
    Logger.info('LogProvider: 初始化完成，等待实时日志');

    _isLoading = false;
    notifyListeners(); // 通知 UI 加载完成
  }

  // 订阅日志流
  void _subscribeToLogStream() {
    _logSubscription = ClashLogService.instance.logStream.listen(
      (log) {
        if (!_isPaused) {
          _pendingLogs.add(log);
        }
      },
      onError: (error) {
        Logger.error('LogProvider: 日志流错误：$error');
      },
      onDone: () {
        Logger.warning('LogProvider: 日志流已关闭');
      },
    );
    Logger.info('LogProvider: 已订阅日志流');
  }

  // 启动批量更新定时器（动态批量优化，保证即时性）
  void _startBatchUpdateTimer() {
    _batchUpdateTimer = Timer.periodic(_batchUpdateInterval, (_) {
      // 动态批量策略：
      // 1. 累积足够日志（>=5条）立即更新
      // 2. 或超过最大间隔（500ms）强制更新
      // 3. 保证日志即时显示的同时减少高频更新
      final shouldFlush =
          _pendingLogs.length >= _batchThreshold ||
          (_pendingLogs.isNotEmpty && _shouldFlushPending());

      if (shouldFlush) {
        _logs.addAll(_pendingLogs);

        // 限制日志数量
        while (_logs.length > _maxLogsCount) {
          _logs.removeAt(0);
        }

        _pendingLogs.clear();
        _invalidateCache(); // 清除缓存
        _lastFlushTime = DateTime.now();
        notifyListeners();
      }
    });
    Logger.info(
      'LogProvider: 批量更新定时器已启动 (间隔: ${_batchUpdateInterval.inMilliseconds}ms, 阈值: $_batchThreshold条, 最大延迟: ${_maxBatchInterval.inMilliseconds}ms)',
    );
  }

  // 检查是否应该刷新待处理日志（超时强制刷新）
  bool _shouldFlushPending() {
    // 如果超过最大间隔未刷新，强制刷新保证即时性
    return DateTime.now().difference(_lastFlushTime) > _maxBatchInterval;
  }

  // 清空日志
  void clearLogs() {
    _logs.clear();
    _pendingLogs.clear();
    _invalidateCache(); // 清除缓存
    notifyListeners();
    Logger.info('LogProvider: 日志已清空');
  }

  // 切换暂停状态
  void togglePause() {
    _isPaused = !_isPaused;
    notifyListeners();
    Logger.info('LogProvider: 暂停状态 = $_isPaused');
  }

  // 设置过滤级别
  void setFilterLevel(ClashLogLevel? level) {
    _filterLevel = level;
    _invalidateCache(); // 清除缓存
    notifyListeners();
    Logger.debug('LogProvider: 过滤级别已设置为 $level');
  }

  // 设置搜索关键词（带防抖）
  void setSearchKeyword(String keyword) {
    // 取消之前的防抖定时器
    _searchDebounceTimer?.cancel();

    // 设置新的防抖定时器（300ms 后才触发过滤）
    _searchDebounceTimer = Timer(const Duration(milliseconds: 300), () {
      _searchKeyword = keyword;
      _invalidateCache(); // 清除缓存
      notifyListeners();
    });
  }

  // 复制所有日志
  String copyAllLogs() {
    return _logs
        .map((log) => '[${log.formattedTime}] [${log.type}] ${log.payload}')
        .join('\n');
  }

  @override
  void dispose() {
    Logger.info('LogProvider: 开始清理资源');
    _batchUpdateTimer?.cancel();
    _logSubscription?.cancel();
    _searchDebounceTimer?.cancel(); // 取消搜索防抖定时器
    super.dispose();
  }
}
