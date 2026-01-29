import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:path/path.dart' as path;
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/storage/preferences.dart';

// 日志级别枚举
enum LogLevel { debug, info, warning, error }

// 日志记录器，支持控制台输出和文件持久化
// 可通过应用设置启用文件日志，调试和发布模式格式有别
class Logger {
  // 日志文件路径
  static String? _logFilePath;

  // 写入缓冲区和锁，防止并发写入冲突
  static final List<String> _logBuffer = [];
  static bool _isWriting = false;

  // 文件大小上限 10MB
  static const int _maxLogFileSize = 10 * 1024 * 1024;

  // 缓冲区容量上限，防止内存泄漏
  static const int _maxBufferSize = 1000;

  // 初始化日志系统，在应用启动时调用
  static Future<void> initialize() async {
    try {
      final logDirPath = PathService.instance.logDirPath;

      // 确保日志目录存在
      final logDir = Directory(logDirPath);
      if (!await logDir.exists()) {
        await logDir.create(recursive: true);
      }

      _logFilePath = path.join(logDirPath, 'running.logs');
    } catch (e) {
      // 初始化失败时静默处理，不影响应用运行
      if (kDebugMode) {
        print('[DartLog] 应用日志系统初始化失败: $e');
      }
    }
  }

  // 记录调试级别日志
  static void debug(Object message) {
    _log(LogLevel.debug, message);
  }

  // 记录信息级别日志
  static void info(Object message) {
    _log(LogLevel.info, message);
  }

  // 记录警告级别日志
  static void warning(Object message) {
    _log(LogLevel.warning, message);
  }

  // 记录错误级别日志
  static void error(Object message) {
    _log(LogLevel.error, message);
  }

  // 内部日志记录实现
  static void _log(LogLevel level, Object message) {
    // 1. 格式化时间戳为 YYYY/MM/DD HH:mm:ss
    final now = DateTime.now();
    final timestamp =
        '${now.year}/'
        '${now.month.toString().padLeft(2, '0')}/'
        '${now.day.toString().padLeft(2, '0')} '
        '${now.hour.toString().padLeft(2, '0')}:'
        '${now.minute.toString().padLeft(2, '0')}:'
        '${now.second.toString().padLeft(2, '0')}';

    // 2. 提取调用栈信息（仅调试模式）
    String location = "unknown";
    if (kDebugMode) {
      final stackTrace = StackTrace.current;
      final frames = stackTrace.toString().split('\n');

      // 3. 定位真实调用者栈帧
      if (frames.length > 2) {
        // 跳过 _log() 和 info()/error() 等包装方法
        final callerFrame = frames[2];

        // 4. 正则提取文件路径
        final match = RegExp(
          r'\((package:.+\.dart):\d+:\d+\)',
        ).firstMatch(callerFrame);
        if (match != null && match.groupCount >= 1) {
          final packagePath = match.group(1)!;

          // 5. 截取 package:xxx/ 后的路径
          final firstSlashIndex = packagePath.indexOf('/');
          if (firstSlashIndex != -1) {
            final fullPath = packagePath.substring(firstSlashIndex + 1);
            // 斜杠替换为点，格式化为 lib.xxx.xxx.dart
            location = fullPath.replaceAll('/', '.');
          }
        }
      }
    }

    // 6. 根据日志级别添加前缀
    final prefix = _getLevelPrefix(level);
    final prefixPlain = _getLevelPrefixPlain(level);

    // 7. 组装日志消息
    String consoleLog;
    String fileLog;

    if (kDebugMode) {
      // 调试模式包含文件路径
      consoleLog = '$prefix $timestamp $location >> $message';
      fileLog = '$prefixPlain $timestamp $location >> $message';
    } else {
      // 发布模式移除文件路径
      consoleLog = '$prefix $timestamp $message';
      fileLog = '$prefixPlain $timestamp $message';
    }

    // 8. 输出到控制台（仅调试模式）
    if (kDebugMode) {
      print(consoleLog);
    }

    // 9. 异步写入文件（若已启用）
    _writeToFile(fileLog);
  }

  // 将日志行写入文件
  static void _writeToFile(String logLine) {
    // 检查是否启用文件日志
    try {
      final enabled = AppPreferences.instance.getAppLogEnabled();
      if (!enabled || _logFilePath == null) {
        return;
      }
    } catch (e) {
      // preferences 未初始化时静默返回
      return;
    }

    // 添加到缓冲区
    _logBuffer.add('$logLine\n');

    // 缓冲区超限时移除日志
    while (_logBuffer.length > _maxBufferSize) {
      _logBuffer.removeAt(0);
      if (kDebugMode) {
        print('[DartLog] 缓冲区已满，丢弃日志');
      }
    }

    // 无写入任务时启动刷新
    if (!_isWriting) {
      _flushLogs();
    }
  }

  // 刷新缓冲区到文件
  static Future<void> _flushLogs() async {
    if (_isWriting || _logBuffer.isEmpty || _logFilePath == null) {
      return;
    }

    _isWriting = true;

    try {
      // 复制并清空缓冲区
      final logsToWrite = List<String>.from(_logBuffer);
      _logBuffer.clear();

      final file = File(_logFilePath!);

      // 检查文件大小，超限时轮转
      if (await file.exists()) {
        final size = await file.length();
        if (size > _maxLogFileSize) {
          // 重命名为备份文件
          final backupPath = '$_logFilePath.backup';
          final backupFile = File(backupPath);

          // 删除旧备份（若存在）
          if (await backupFile.exists()) {
            try {
              await backupFile.delete();
            } catch (e) {
              if (kDebugMode) {
                print('[DartLog] 删除应用日志旧备份失败: $e');
              }
            }
          }

          // 重命名当前文件为备份（失败时静默忽略）
          try {
            await file.rename(backupPath);
            if (kDebugMode) {
              print('[DartLog] 应用日志文件超过 10MB，已轮转到 running.logs.old');
            }
          } catch (e) {
            // Rust 进程可能正在写入，静默忽略
            if (kDebugMode) {
              print('[DartLog] 应用日志轮转失败（可能正被占用）: $e');
            }
          }

          // 新文件写入轮转提示
          final now = DateTime.now();
          final timestamp =
              '${now.year}/'
              '${now.month.toString().padLeft(2, '0')}/'
              '${now.day.toString().padLeft(2, '0')} '
              '${now.hour.toString().padLeft(2, '0')}:'
              '${now.minute.toString().padLeft(2, '0')}:'
              '${now.second.toString().padLeft(2, '0')}';
          await File(_logFilePath!).writeAsString(
            '[DartInfo] $timestamp >> 应用日志文件已达到 ${(size / 1024 / 1024).toStringAsFixed(2)} MB，已轮转到 running.logs.old\n',
            mode: FileMode.write,
            flush: true,
          );
        }
      }

      // 追加写入日志（OS 保证原子性）
      await file.writeAsString(
        logsToWrite.join(),
        mode: FileMode.append,
        flush: true,
      );
    } catch (e) {
      // 写入失败静默处理
      if (kDebugMode) {
        print('[DartLog] 写入应用日志文件失败: $e');
      }
    } finally {
      _isWriting = false;

      // 缓冲区有新内容时继续刷新
      if (_logBuffer.isNotEmpty) {
        _flushLogs();
      }
    }
  }

  // 清空日志文件
  static Future<void> clearLogFile() async {
    if (_logFilePath == null) return;

    try {
      final file = File(_logFilePath!);
      if (await file.exists()) {
        await file.writeAsString('');
      }
    } catch (e) {
      if (kDebugMode) {
        print('[DartLog] 清空应用日志文件失败: $e');
      }
    }
  }

  // 获取日志文件路径
  static String? getLogFilePath() => _logFilePath;

  // 获取日志级别前缀（带 ANSI 颜色，用于控制台）
  static String _getLevelPrefix(LogLevel level) {
    // ANSI 转义码
    const String green = '\x1B[32m';
    const String yellow = '\x1B[33m';
    const String red = '\x1B[31m';
    const String cyan = '\x1B[36m';
    const String reset = '\x1B[0m';

    switch (level) {
      case LogLevel.debug:
        return '$cyan[DartDebug]$reset';
      case LogLevel.info:
        return '$green[DartInfo]$reset';
      case LogLevel.warning:
        return '$yellow[DartWarn]$reset';
      case LogLevel.error:
        return '$red[DartError]$reset';
    }
  }

  // 获取日志级别前缀（纯文本，用于文件）
  static String _getLevelPrefixPlain(LogLevel level) {
    switch (level) {
      case LogLevel.debug:
        return '[DartDebug]';
      case LogLevel.info:
        return '[DartInfo]';
      case LogLevel.warning:
        return '[DartWarn]';
      case LogLevel.error:
        return '[DartError]';
    }
  }
}
