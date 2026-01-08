import 'dart:io';
import 'dart:async';
import 'package:stelliberty/services/log_print_service.dart';

// 配置文件监听服务
// 监听配置文件变化并触发重载回调
class ConfigWatcher {
  File? _configFile;
  StreamSubscription<FileSystemEvent>? _watchSubscription;
  Timer? _debounceTimer;
  DateTime? _lastModified;

  // 暂停标志：为 true 时忽略文件变化事件
  bool _isPaused = false;

  // 重载回调函数
  final Future<void> Function() onReload;

  // 防抖延迟（毫秒），避免短时间内多次触发
  final int debounceMs;

  ConfigWatcher({required this.onReload, this.debounceMs = 1000});

  // 暂停监听（不停止订阅，只是忽略事件）
  void pause() {
    _isPaused = true;
    _debounceTimer?.cancel();
    Logger.debug('配置文件监听已暂停');
  }

  // 恢复监听
  Future<void> resume() async {
    _isPaused = false;
    // 更新最后修改时间，避免恢复后立即触发
    await _updateLastModified();
    Logger.debug('配置文件监听已恢复');
  }

  // 更新最后修改时间
  Future<void> _updateLastModified() async {
    if (_configFile != null && await _configFile!.exists()) {
      _lastModified = await _configFile!.lastModified();
    }
  }

  // 开始监听配置文件
  Future<void> watch(String configPath) async {
    try {
      _configFile = File(configPath);

      if (!await _configFile!.exists()) {
        Logger.warning('配置文件不存在，无法监听：$configPath');
        return;
      }

      // 记录初始修改时间
      _lastModified = await _configFile!.lastModified();
      Logger.info('开始监听配置文件：$configPath');

      // 监听配置文件所在目录
      final directory = _configFile!.parent;
      final stream = directory.watch(events: FileSystemEvent.all);

      _watchSubscription = stream.listen(
        (event) {
          // 只关注目标配置文件的修改事件
          if (event.path == _configFile!.path &&
              event.type == FileSystemEvent.modify) {
            _onFileChanged();
          }
        },
        onError: (error) {
          Logger.error('配置文件监听出错：$error');
        },
      );

      Logger.info('配置文件监听器已启动');
    } catch (e) {
      Logger.error('启动配置文件监听失败：$e');
    }
  }

  // 停止监听
  Future<void> stop() async {
    _debounceTimer?.cancel();
    await _watchSubscription?.cancel();
    _watchSubscription = null;
    _configFile = null;
    Logger.info('配置文件监听器已停止');
  }

  // 文件变化处理（带防抖）
  void _onFileChanged() {
    // 暂停期间忽略文件变化
    if (_isPaused) {
      return;
    }

    // 取消之前的定时器
    _debounceTimer?.cancel();

    // 设置新的防抖定时器
    _debounceTimer = Timer(Duration(milliseconds: debounceMs), () async {
      try {
        // 检查文件是否真的被修改（通过修改时间判断）
        if (_configFile == null || !await _configFile!.exists()) {
          Logger.warning('配置文件已不存在');
          return;
        }

        final currentModified = await _configFile!.lastModified();
        if (_lastModified != null &&
            currentModified.isAtSameMomentAs(_lastModified!)) {
          // 修改时间没变，可能是误触发
          return;
        }

        _lastModified = currentModified;
        Logger.info('检测到配置文件变化，准备重载…');

        // 触发重载回调
        await onReload();

        Logger.info('配置文件重载完成');
      } catch (e) {
        Logger.error('配置文件重载失败：$e');
      }
    });
  }

  // 手动触发重载（用于测试）
  Future<void> reload() async {
    try {
      Logger.info('手动触发配置文件重载…');
      await onReload();
      Logger.info('配置文件重载完成');
    } catch (e) {
      Logger.error('配置文件重载失败：$e');
    }
  }

  // 是否正在监听
  bool get isWatching => _watchSubscription != null;
}
