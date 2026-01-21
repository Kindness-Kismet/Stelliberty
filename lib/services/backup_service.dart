import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:stelliberty/atomic/platform_helper.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// 备份相关异常类型
enum BackupErrorType {
  fileNotFound,
  invalidFormat,
  versionMismatch,
  dataIncomplete,
  operationInProgress,
  timeout,
  unknown,
}

class BackupException implements Exception {
  final BackupErrorType type;
  final String message;
  final Object? originalError;

  BackupException({
    required this.type,
    required this.message,
    this.originalError,
  });

  factory BackupException.fileNotFound(String path) {
    return BackupException(
      type: BackupErrorType.fileNotFound,
      message: '备份文件不存在：$path',
    );
  }

  factory BackupException.invalidFormat() {
    return BackupException(
      type: BackupErrorType.invalidFormat,
      message: '备份文件格式错误',
    );
  }

  factory BackupException.versionMismatch(
    String backupVersion,
    String appVersion,
  ) {
    return BackupException(
      type: BackupErrorType.versionMismatch,
      message: '备份版本不匹配：备份版本 $backupVersion，应用版本 $appVersion',
    );
  }

  factory BackupException.dataIncomplete() {
    return BackupException(
      type: BackupErrorType.dataIncomplete,
      message: '备份数据不完整',
    );
  }

  factory BackupException.operationInProgress() {
    return BackupException(
      type: BackupErrorType.operationInProgress,
      message: '正在进行备份或还原操作，请稍后再试',
    );
  }

  factory BackupException.timeout() {
    return BackupException(type: BackupErrorType.timeout, message: '备份操作超时');
  }

  factory BackupException.unknown(Object error) {
    return BackupException(
      type: BackupErrorType.unknown,
      message: '未知错误：$error',
      originalError: error,
    );
  }

  @override
  String toString() => message;
}

// 备份服务
class BackupService {
  BackupService._();
  static final BackupService instance = BackupService._();

  static const String backupVersion = '1.0.0';
  static const String backupExtension = '.stelliberty';
  static const Set<String> _excludedPreferenceKeys = {
    'auto_start_enabled',
    'clash_tun_enable',
    'clash_tun_stack',
    'clash_tun_device',
    'clash_tun_auto_route',
    'clash_tun_auto_redirect',
    'clash_tun_auto_detect_interface',
    'clash_tun_dns_hijack',
    'clash_tun_strict_route',
    'clash_tun_route_exclude_address',
    'clash_tun_disable_icmp_forwarding',
    'clash_tun_mtu',
  };
  static const Set<String> _mobileExcludedPreferenceKeys = {
    'window_effect',
    'window_position_x',
    'window_position_y',
    'window_width',
    'window_height',
    'is_maximized',
    'silent_start_enabled',
    'minimize_to_tray',
    'app_auto_update',
    'app_update_interval',
    'last_app_update_check_time',
    'ignored_update_version',
    'hotkey_enabled',
    'hotkey_toggle_proxy',
    'hotkey_toggle_tun',
    'hotkey_show_window',
    'hotkey_exit_app',
    'clash_proxy_host',
    'clash_system_proxy_bypass',
    'clash_use_default_bypass',
    'clash_system_proxy_pac_mode',
    'clash_system_proxy_pac_script',
  };

  // 并发控制标志
  bool _isOperating = false;

  // 创建备份
  Future<String> createBackup(String targetPath) async {
    // 检查是否正在进行其他操作
    if (_isOperating) {
      throw BackupException.operationInProgress();
    }

    _isOperating = true;

    try {
      // 使用 Rust 层创建备份
      final completer = Completer<BackupOperationResult>();
      StreamSubscription? subscription;

      try {
        // 订阅 Rust 响应流
        subscription = BackupOperationResult.rustSignalStream.listen((result) {
          if (!completer.isCompleted) {
            completer.complete(result.message);
          }
        });

        // 获取应用版本
        final packageInfo = await PackageInfo.fromPlatform();

        // 获取所有路径
        final pathService = PathService.instance;
        final preferencesPath = _resolvePreferencesPath(pathService);
        if (PlatformHelper.isMobile) {
          await _exportSharedPreferences(preferencesPath);
        }

        // 发送创建备份请求到 Rust
        final request = CreateBackupRequest(
          targetPath: targetPath,
          appVersion: packageInfo.version,
          preferencesPath: preferencesPath,
          subscriptionsDir: pathService.subscriptionsDir,
          subscriptionsListPath: pathService.subscriptionListPath,
          overridesDir: pathService.overridesDir,
          overridesListPath: pathService.overrideListPath,
          dnsConfigPath: pathService.dnsConfigPath,
          pacFilePath: pathService.pacFilePath,
        );
        request.sendSignalToRust();

        // 等待备份结果
        final result = await completer.future.timeout(
          const Duration(seconds: 60),
          onTimeout: () => throw BackupException.timeout(),
        );

        if (!result.isSuccessful) {
          final errorMessage = result.errorMessage ?? '备份创建失败';
          throw _mapMessageToBackupException(errorMessage);
        }

        return result.message;
      } finally {
        await subscription?.cancel();
      }
    } catch (e) {
      Logger.error('创建备份失败：$e');
      if (e is BackupException) {
        rethrow;
      }
      throw _toBackupException(e);
    } finally {
      _isOperating = false;
    }
  }

  // 还原备份
  Future<void> restoreBackup(String backupPath) async {
    // 检查是否正在进行其他操作
    if (_isOperating) {
      throw BackupException.operationInProgress();
    }

    _isOperating = true;

    try {
      // 使用 Rust 层还原备份
      final completer = Completer<BackupOperationResult>();
      StreamSubscription? subscription;

      try {
        // 订阅 Rust 响应流
        subscription = BackupOperationResult.rustSignalStream.listen((result) {
          if (!completer.isCompleted) {
            completer.complete(result.message);
          }
        });

        // 获取所有路径
        final pathService = PathService.instance;
        final preferencesPath = _resolvePreferencesPath(pathService);

        // 发送还原备份请求到 Rust
        final request = RestoreBackupRequest(
          backupPath: backupPath,
          preferencesPath: preferencesPath,
          subscriptionsDir: pathService.subscriptionsDir,
          subscriptionsListPath: pathService.subscriptionListPath,
          overridesDir: pathService.overridesDir,
          overridesListPath: pathService.overrideListPath,
          dnsConfigPath: pathService.dnsConfigPath,
          pacFilePath: pathService.pacFilePath,
        );
        request.sendSignalToRust();

        // 等待还原结果
        final result = await completer.future.timeout(
          const Duration(seconds: 15),
          onTimeout: () => throw BackupException.timeout(),
        );

        if (!result.isSuccessful) {
          final errorMessage = result.errorMessage ?? '备份还原失败';
          throw _mapMessageToBackupException(errorMessage);
        }

        if (PlatformHelper.isMobile) {
          await _importSharedPreferences(preferencesPath);
        }
      } finally {
        await subscription?.cancel();
      }
    } catch (e) {
      Logger.error('还原备份失败：$e');
      if (e is BackupException) {
        rethrow;
      }
      throw _toBackupException(e);
    } finally {
      _isOperating = false;
    }
  }

  BackupException _toBackupException(Object error) {
    if (error is BackupException) {
      return error;
    }
    return _mapMessageToBackupException(error.toString(), originalError: error);
  }

  String _resolvePreferencesPath(PathService pathService) {
    if (kDebugMode || kProfileMode) {
      return pathService.devPreferencesFilePath;
    }
    return pathService.preferencesFilePath;
  }

  BackupException _mapMessageToBackupException(
    String message, {
    Object? originalError,
  }) {
    final lowerMessage = message.toLowerCase();

    if (_containsAny(message, const ['不存在', '找不到']) ||
        _containsAny(lowerMessage, const [
          'not found',
          'no such file',
          'cannot find',
        ])) {
      return BackupException(
        type: BackupErrorType.fileNotFound,
        message: message,
        originalError: originalError,
      );
    }

    if (_containsAny(message, const ['格式', '解析']) ||
        _containsAny(lowerMessage, const ['format', 'expected', 'json'])) {
      return BackupException.invalidFormat();
    }

    if (_containsAny(message, const ['版本', '不支持']) ||
        _containsAny(lowerMessage, const ['version'])) {
      return BackupException(
        type: BackupErrorType.versionMismatch,
        message: message,
        originalError: originalError,
      );
    }

    if (_containsAny(message, const ['不完整']) ||
        _containsAny(lowerMessage, const ['incomplete'])) {
      return BackupException.dataIncomplete();
    }

    return BackupException.unknown(originalError ?? message);
  }

  Future<void> _exportSharedPreferences(String filePath) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final data = <String, dynamic>{};

      for (final key in prefs.getKeys()) {
        if (_shouldSkipPreferenceKey(key)) {
          continue;
        }
        final value = prefs.get(key);
        if (value is bool ||
            value is int ||
            value is double ||
            value is String ||
            value is List<String>) {
          data[key] = value;
        }
      }

      final file = File(filePath);
      await file.parent.create(recursive: true);
      final content = const JsonEncoder.withIndent('  ').convert(data);
      await file.writeAsString(content);
    } catch (e) {
      Logger.error('导出偏好失败：$e');
      throw BackupException.unknown(e);
    }
  }

  Future<void> _importSharedPreferences(String filePath) async {
    final file = File(filePath);
    if (!await file.exists()) {
      throw BackupException.dataIncomplete();
    }

    try {
      final prefs = await SharedPreferences.getInstance();
      final preservedValues = <String, Object>{};
      final preservedKeys = <String>{
        ..._excludedPreferenceKeys,
        ..._mobileExcludedPreferenceKeys,
      };
      for (final key in preservedKeys) {
        final value = prefs.get(key);
        if (value is bool) {
          preservedValues[key] = value;
        } else if (value is int) {
          preservedValues[key] = value;
        } else if (value is double) {
          preservedValues[key] = value;
        } else if (value is String) {
          preservedValues[key] = value;
        } else if (value is List<String>) {
          preservedValues[key] = value;
        }
      }

      final content = await file.readAsString();
      final rawData = json.decode(content);
      if (rawData is! Map<String, dynamic>) {
        throw BackupException.invalidFormat();
      }

      await prefs.clear();
      for (final entry in rawData.entries) {
        final key = entry.key;
        if (_shouldSkipPreferenceKey(key)) {
          continue;
        }
        final value = entry.value;
        if (value is bool) {
          await prefs.setBool(key, value);
        } else if (value is int) {
          await prefs.setInt(key, value);
        } else if (value is double) {
          await prefs.setDouble(key, value);
        } else if (value is String) {
          await prefs.setString(key, value);
        } else if (value is List) {
          final list = value.whereType<String>().toList();
          if (list.length == value.length) {
            await prefs.setStringList(key, list);
          }
        }
      }

      for (final entry in preservedValues.entries) {
        final key = entry.key;
        final value = entry.value;
        if (value is bool) {
          await prefs.setBool(key, value);
        } else if (value is int) {
          await prefs.setInt(key, value);
        } else if (value is double) {
          await prefs.setDouble(key, value);
        } else if (value is String) {
          await prefs.setString(key, value);
        } else if (value is List<String>) {
          await prefs.setStringList(key, value);
        }
      }
    } on BackupException {
      rethrow;
    } catch (e) {
      Logger.error('导入偏好失败：$e');
      throw BackupException.invalidFormat();
    }
  }

  bool _containsAny(String source, List<String> keywords) {
    return keywords.any(source.contains);
  }

  bool _shouldSkipPreferenceKey(String key) {
    if (_excludedPreferenceKeys.contains(key)) {
      return true;
    }
    if (PlatformHelper.isMobile &&
        _mobileExcludedPreferenceKeys.contains(key)) {
      return true;
    }
    return false;
  }

  // 生成备份文件名
  String generateBackupFileName() {
    final now = DateTime.now();
    final timestamp =
        '${now.year}${now.month.toString().padLeft(2, '0')}'
        '${now.day.toString().padLeft(2, '0')}_'
        '${now.hour.toString().padLeft(2, '0')}'
        '${now.minute.toString().padLeft(2, '0')}'
        '${now.second.toString().padLeft(2, '0')}';
    return 'backup_$timestamp$backupExtension';
  }
}
