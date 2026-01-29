import 'dart:async';
import 'dart:io';
import 'package:flutter/services.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 开机自启动服务：跨平台单例实现。
// 桌面端（Windows/macOS/Linux）通过 Rust 实现；Android 通过 MethodChannel 实现。
class AutoStartService {
  // 私有构造函数，防止外部实例化
  AutoStartService._();

  // 单例实例
  static final AutoStartService instance = AutoStartService._();

  // Android 平台方法通道
  static const _androidChannel = MethodChannel(
    'io.github.stelliberty/auto_start',
  );

  // 状态缓存
  bool? _cachedStatus;

  // 获取自启动状态（优先从缓存，无缓存时查询持久化配置）
  bool getCachedStatus() {
    return _cachedStatus ?? AppPreferences.instance.getAutoStartEnabled();
  }

  // 从平台查询自启动状态
  Future<bool> getStatus() async {
    // Android 平台使用 MethodChannel
    if (Platform.isAndroid) {
      return _getStatusAndroid();
    }

    // 桌面平台使用 Rust 信号
    return _getStatusDesktop();
  }

  // Android 平台获取状态
  Future<bool> _getStatusAndroid() async {
    try {
      final enabled = await _androidChannel.invokeMethod<bool>('getStatus');
      final status = enabled ?? false;
      _cachedStatus = status;
      return status;
    } catch (e) {
      Logger.error('Android 获取自启动状态失败: $e');
      return getCachedStatus();
    }
  }

  // 桌面平台从 Rust 端获取状态
  Future<bool> _getStatusDesktop() async {
    try {
      // 创建 Completer 等待 Rust 响应
      final completer = Completer<bool>();

      // 订阅 Rust 信号流
      final subscription = AutoStartStatusResult.rustSignalStream.listen((
        result,
      ) {
        if (!completer.isCompleted) {
          if (result.message.errorMessage != null) {
            Logger.warning('获取状态失败：${result.message.errorMessage}');
            completer.complete(false);
          } else {
            completer.complete(result.message.isEnabled);
          }
        }
      });

      // 发送请求到 Rust
      GetAutoStartStatus().sendSignalToRust();

      // 等待响应（5 秒超时）
      final status = await completer.future.timeout(
        const Duration(seconds: 5),
        onTimeout: () {
          Logger.error('获取状态超时');
          return getCachedStatus(); // 超时返回缓存值
        },
      );

      // 停止监听信号流
      await subscription.cancel();

      // 更新缓存和持久化
      _cachedStatus = status;
      await AppPreferences.instance.setAutoStartEnabled(status);

      return status;
    } catch (e) {
      Logger.error('获取状态出错：$e');
      return getCachedStatus(); // 异常返回缓存值
    }
  }

  // 设置开机自启动状态
  Future<bool> setStatus(bool enabled) async {
    // Android 平台使用 MethodChannel
    if (Platform.isAndroid) {
      return _setStatusAndroid(enabled);
    }

    // 桌面平台使用 Rust 信号
    return _setStatusDesktop(enabled);
  }

  // Android 平台设置状态
  Future<bool> _setStatusAndroid(bool enabled) async {
    try {
      final success = await _androidChannel.invokeMethod<bool>('setStatus', {
        'enabled': enabled,
      });
      if (success == true) {
        _cachedStatus = enabled;
        Logger.info('Android 开机自启动已${enabled ? '启用' : '禁用'}');
        return true;
      }
      return false;
    } catch (e) {
      Logger.error('Android 设置自启动状态失败: $e');
      return false;
    }
  }

  // 桌面平台设置状态
  Future<bool> _setStatusDesktop(bool enabled) async {
    try {
      // 创建 Completer 等待 Rust 响应
      final completer = Completer<bool>();

      // 订阅 Rust 信号流
      final subscription = AutoStartStatusResult.rustSignalStream.listen((
        result,
      ) {
        if (!completer.isCompleted) {
          if (result.message.errorMessage != null) {
            Logger.error('设置状态失败：${result.message.errorMessage}');
            completer.complete(false);
          } else {
            Logger.info('开机自启动已${result.message.isEnabled ? '启用' : '禁用'}');
            completer.complete(result.message.isEnabled == enabled);
          }
        }
      });

      // 发送请求到 Rust
      SetAutoStartStatus(isEnabled: enabled).sendSignalToRust();

      // 等待响应（5 秒超时）
      final success = await completer.future.timeout(
        const Duration(seconds: 5),
        onTimeout: () {
          Logger.error('设置状态超时');
          return false;
        },
      );

      // 停止监听信号流
      await subscription.cancel();

      // 设置成功后更新缓存和持久化
      if (success) {
        _cachedStatus = enabled;
        await AppPreferences.instance.setAutoStartEnabled(enabled);
      }

      return success;
    } catch (e) {
      Logger.error('设置状态出错：$e');
      return false;
    }
  }

  // 切换开机自启动状态
  Future<bool> toggle() async {
    final currentStatus = await getStatus();
    return await setStatus(!currentStatus);
  }
}
