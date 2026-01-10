import 'dart:async';
import 'package:stelliberty/clash/state/service_states.dart';
import 'package:stelliberty/clash/manager/clash_manager.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/tray/tray_manager.dart';

// 服务管理器
// 负责服务模式的业务逻辑
class ServiceManager {
  static final ServiceManager _instance = ServiceManager._internal();
  static ServiceManager get instance => _instance;
  ServiceManager._internal();

  // 缓存的服务状态（供非UI组件查询）
  ServiceState _cachedState = ServiceState.unknown;
  ServiceState get cachedState => _cachedState;

  bool get isServiceModeInstalled => _cachedState.isServiceModeInstalled;
  bool get isServiceModeRunning => _cachedState.isServiceModeRunning;

  // 刷新服务状态
  Future<ServiceState> refreshStatus() async {
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
      _cachedState = _parseStatusString(statusStr);
      return _cachedState;
    } catch (e) {
      Logger.error('获取服务状态失败：$e');
      _cachedState = ServiceState.unknown;
      return _cachedState;
    }
  }

  // 解析状态字符串
  ServiceState _parseStatusString(String statusStr) {
    switch (statusStr.toLowerCase()) {
      case 'running':
        return ServiceState.running;
      case 'stopped':
        return ServiceState.installed;
      case 'not_installed':
        return ServiceState.notInstalled;
      default:
        return ServiceState.unknown;
    }
  }

  // 安装服务
  Future<(bool success, String? error)> installService() async {
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
        // 等待服务完全就绪
        await Future.delayed(const Duration(seconds: 2));

        // 立即刷新服务状态
        await refreshStatus();
        Logger.debug('服务状态已刷新为：$_cachedState');

        // 手动触发托盘菜单更新（服务安装后 TUN 菜单应变为可用）
        AppTrayManager().updateTrayMenuManually();

        // 如果安装前核心在运行，以服务模式重启
        Logger.debug(
          '安装后检查重启条件：wasRunningBefore=$wasRunningBefore, currentConfigPath=$currentConfigPath',
        );

        if (!wasRunningBefore) {
          Logger.info('安装前核心未运行，不自动启动');
          return (true, null);
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

        return (true, null);
      } else {
        final error = signal.message.errorMessage ?? '未知错误';
        Logger.error('服务安装失败：$error');
        return (false, error);
      }
    } catch (e) {
      Logger.error('安装服务异常：$e');
      return (false, e.toString());
    }
  }

  // 卸载服务
  Future<(bool success, String? error)> uninstallService() async {
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
        // 立即刷新服务状态
        await refreshStatus();
        Logger.debug('服务状态已刷新为：$_cachedState');

        // 停止服务心跳定时器
        ClashManager.instance.stopServiceHeartbeat();

        // 强制重置核心状态
        ClashManager.instance.forceResetCoreState();
        Logger.debug('核心状态已强制重置为 stopped');

        // 手动触发托盘菜单更新
        AppTrayManager().updateTrayMenuManually();

        // 如果卸载前核心在运行，以普通模式重启
        if (wasRunningBefore && currentConfigPath != null) {
          Logger.info('以普通模式重启核心...');
          try {
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

        return (true, null);
      } else {
        final error = signal.message.errorMessage ?? '未知错误';
        Logger.error('服务卸载失败：$error');
        return (false, error);
      }
    } catch (e) {
      Logger.error('卸载服务异常：$e');
      return (false, e.toString());
    }
  }
}
