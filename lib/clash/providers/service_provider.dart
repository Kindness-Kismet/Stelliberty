import 'dart:async';
import 'package:stelliberty/clash/state/service_states.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/tray/tray_manager.dart';

// Clash 服务模式业务逻辑管理
class ServiceProvider {
  static final ServiceProvider _instance = ServiceProvider._internal();
  factory ServiceProvider() => _instance;
  ServiceProvider._internal();

  // 服务状态
  ServiceState _serviceState = ServiceState.unknown;
  ServiceState get serviceState => _serviceState;

  // 更新服务状态
  void _updateServiceState(ServiceState newState) {
    if (_serviceState == newState) return;

    final previousState = _serviceState;
    _serviceState = newState;
    Logger.debug('服务状态变化：${previousState.name} -> ${newState.name}');
  }

  // 最后的操作结果
  String? _lastOperationError;
  bool? _lastOperationSuccess;

  ServiceState get status => _serviceState;
  bool get isServiceModeInstalled => _serviceState.isServiceModeInstalled;
  bool get isServiceModeRunning => _serviceState.isServiceModeRunning;
  bool get isServiceModeProcessing => _serviceState.isServiceModeProcessing;
  String? get lastOperationError => _lastOperationError;
  bool? get lastOperationSuccess => _lastOperationSuccess;

  // 从状态字符串更新状态
  void _updateFromStatusString(String statusStr) {
    final ServiceState newState;
    switch (statusStr.toLowerCase()) {
      case 'running':
        newState = ServiceState.running;
        break;
      case 'stopped':
        newState = ServiceState.installed;
        break;
      case 'not_installed':
        newState = ServiceState.notInstalled;
        break;
      default:
        newState = ServiceState.unknown;
    }
    _updateServiceState(newState);
  }

  // 清除最后的操作结果
  void clearLastOperationResult() {
    _lastOperationError = null;
    _lastOperationSuccess = null;
  }

  // 初始化服务状态
  Future<void> initialize() async {
    await refreshStatus();
  }

  // 刷新服务状态
  Future<void> refreshStatus() async {
    try {
      // 发送获取状态请求
      GetServiceStatus().sendSignalToRust();

      // 等待响应
      final signal = await ServiceStatusResponse.rustSignalStream.first.timeout(
        const Duration(seconds: 5),
        onTimeout: () {
          Logger.warning('获取服务状态超时');
          throw TimeoutException('获取服务状态超时');
        },
      );

      final statusStr = signal.message.status;

      // 使用状态管理器更新状态
      _updateFromStatusString(statusStr);
    } catch (e) {
      Logger.error('获取服务状态失败：$e');
      _updateServiceState(ServiceState.unknown); // '刷新失败：$e');
    }
  }

  // 安装服务
  // 返回 true 表示成功，false 表示失败
  Future<bool> installService() async {
    if (isServiceModeProcessing) return false;

    _updateServiceState(ServiceState.installing); // '用户请求安装服务');
    _lastOperationSuccess = null;
    _lastOperationError = null;

    try {
      Logger.info('开始安装服务...');

      // 记录安装前的核心运行状态（用于安装成功后自动重启）
      final wasRunningBefore = ClashManager.instance.isCoreRunning;
      final currentConfigPath = ClashManager.instance.currentConfigPath;

      // 发送安装请求（Rust 端会处理停止核心的逻辑）
      InstallService().sendSignalToRust();

      // 等待响应
      final signal = await ServiceOperationResult.rustSignalStream.first
          .timeout(
            const Duration(seconds: 30),
            onTimeout: () {
              throw Exception('安装服务超时（30秒）');
            },
          );

      if (signal.message.isSuccessful) {
        Logger.info('服务安装成功');
        _lastOperationSuccess = true;

        // 立即更新本地状态
        _updateServiceState(ServiceState.installed); // '服务安装成功');

        // 等待服务完全就绪后刷新状态
        await Future.delayed(const Duration(seconds: 2));
        await refreshStatus();

        // 手动触发托盘菜单更新（服务安装后 TUN 菜单应变为可用）
        AppTrayManager().updateTrayMenuManually();

        // 如果安装前核心在运行，以服务模式重启
        Logger.debug(
          '安装后检查重启条件：wasRunningBefore=$wasRunningBefore, currentConfigPath=$currentConfigPath',
        );

        if (!wasRunningBefore) {
          Logger.info('安装前核心未运行，不自动启动');
          return true;
        }

        // 以服务模式重启核心
        try {
          final configDesc = currentConfigPath != null
              ? '使用配置：$currentConfigPath'
              : '使用默认配置';
          Logger.info('以服务模式重启核心（$configDesc）...');

          // 确保核心进程完全停止后再以服务模式启动
          await ClashManager.instance.stopCore();

          final overrides = ClashManager.instance.getOverrides();
          await ClashManager.instance.startCore(
            configPath: currentConfigPath,
            overrides: overrides,
          );

          Logger.info('已切换到服务模式');
        } catch (e) {
          Logger.error('以服务模式启动失败：$e');
          if (currentConfigPath == null) {
            Logger.warning('服务模式已安装，但无法自动启动核心，请手动启动');
          }
        }

        return true;
      } else {
        final error = signal.message.errorMessage ?? '未知错误';
        Logger.error('服务安装失败：$error');
        _lastOperationSuccess = false;
        _lastOperationError = error;

        // 安装失败，恢复到之前的状态
        await refreshStatus();
        return false;
      }
    } catch (e) {
      Logger.error('安装服务异常：$e');
      _lastOperationSuccess = false;
      _lastOperationError = e.toString();

      // 异常情况，恢复到之前的状态
      await refreshStatus();
      return false;
    }
  }

  // 卸载服务
  // 返回 true 表示成功，false 表示失败
  Future<bool> uninstallService() async {
    if (isServiceModeProcessing) return false;

    _updateServiceState(ServiceState.uninstalling); // '用户请求卸载服务');
    _lastOperationSuccess = null;
    _lastOperationError = null;

    try {
      Logger.info('开始卸载服务...');

      // 记录卸载前的核心运行状态（用于卸载成功后自动重启）
      final wasRunningBefore = ClashManager.instance.isCoreRunning;
      final currentConfigPath = ClashManager.instance.currentConfigPath;

      // 检查并禁用虚拟网卡（普通模式不支持虚拟网卡，需提前禁用并持久化）
      if (ClashPreferences.instance.getTunEnable()) {
        Logger.info('检测到虚拟网卡已启用，卸载服务前先禁用虚拟网卡...');
        try {
          await ClashManager.instance.setTunEnabled(false);
          Logger.info('虚拟网卡已禁用并持久化');
        } catch (e) {
          Logger.error('禁用虚拟网卡失败：$e');
          // 继续卸载流程
        }
      }

      // 发送卸载请求（Rust 端会处理停止核心的逻辑）
      UninstallService().sendSignalToRust();

      // 等待响应
      final signal = await ServiceOperationResult.rustSignalStream.first
          .timeout(
            const Duration(seconds: 30),
            onTimeout: () {
              throw Exception('卸载服务超时（30秒）');
            },
          );

      if (signal.message.isSuccessful) {
        Logger.info('服务卸载成功');
        _lastOperationSuccess = true;

        // 停止服务心跳定时器（Rust 端已停止核心，但 Dart 端的心跳定时器还在运行）
        ClashManager.instance.stopServiceHeartbeat();

        // 立即更新本地状态并通知 UI
        _updateServiceState(ServiceState.notInstalled); // '服务卸载成功');

        // 手动触发托盘菜单更新（服务卸载后 TUN 菜单应变为不可用）
        AppTrayManager().updateTrayMenuManually();

        // 如果卸载前核心在运行，以普通模式重启
        if (wasRunningBefore && currentConfigPath != null) {
          Logger.info('以普通模式重启核心...');
          try {
            // 重置 Dart 端状态后再以普通模式启动
            await ClashManager.instance.stopCore();

            final overrides = ClashManager.instance.getOverrides();
            await ClashManager.instance.startCore(
              configPath: currentConfigPath,
              overrides: overrides,
            );
            Logger.info('已切换到普通模式');
          } catch (e) {
            Logger.error('以普通模式启动失败：$e');
          }
        }

        return true;
      } else {
        final error = signal.message.errorMessage ?? '未知错误';
        Logger.error('服务卸载失败：$error');
        _lastOperationSuccess = false;
        _lastOperationError = error;

        // 卸载失败，恢复到之前的状态
        await refreshStatus();
        return false;
      }
    } catch (e) {
      Logger.error('卸载服务异常：$e');
      _lastOperationSuccess = false;
      _lastOperationError = e.toString();

      // 异常情况，恢复到之前的状态
      await refreshStatus();
      return false;
    }
  }
}
