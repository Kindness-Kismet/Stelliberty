import 'dart:async';
import 'package:flutter/material.dart';
import 'package:stelliberty/services/app_update_service.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 应用更新 Provider
// 管理自动更新检测、定时器和更新状态
class AppUpdateProvider extends ChangeNotifier {
  Timer? _updateCheckTimer;
  DateTime? _lastCheckTime;
  bool _isChecking = false;
  AppUpdateInfo? _latestUpdateInfo;
  bool _isDialogShown = false;

  AppUpdateInfo? get latestUpdateInfo => _latestUpdateInfo;
  bool get isChecking => _isChecking;
  bool get isDialogShown => _isDialogShown;

  // 初始化 Provider
  Future<void> initialize() async {
    Logger.info('初始化 AppUpdateProvider...');

    // 加载上次检查时间
    _lastCheckTime = AppPreferences.instance.getLastAppUpdateCheckTime();
    Logger.debug('上次检查更新时间: $_lastCheckTime');

    // 启动自动更新调度
    await _scheduleNextCheck();
  }

  // 调度下一次检查
  Future<void> _scheduleNextCheck() async {
    final autoUpdate = AppPreferences.instance.getAppAutoUpdate();

    if (!autoUpdate) {
      Logger.debug('自动更新已禁用，取消定时器');
      _updateCheckTimer?.cancel();
      _updateCheckTimer = null;
      return;
    }

    final interval = AppPreferences.instance.getAppUpdateInterval();
    Logger.debug('自动更新已启用，检测间隔: $interval');

    switch (interval) {
      case 'startup':
        // 每次启动时检查
        Logger.info('配置为启动时检查，3秒后执行');
        _updateCheckTimer?.cancel();
        _updateCheckTimer = Timer(const Duration(seconds: 3), () async {
          await _performCheck(isAutoCheck: true);
        });
        break;

      case '1day':
        await _schedulePeriodicCheck(const Duration(days: 1));
        break;

      case '7days':
        await _schedulePeriodicCheck(const Duration(days: 7));
        break;

      case '14days':
        await _schedulePeriodicCheck(const Duration(days: 14));
        break;

      default:
        Logger.warning('未知的更新间隔配置: $interval');
        break;
    }
  }

  // 调度定期检查
  Future<void> _schedulePeriodicCheck(Duration interval) async {
    // 检查是否需要立即执行
    if (_lastCheckTime == null) {
      Logger.info('从未检查过更新，立即执行首次检查');
      await _performCheck(isAutoCheck: true);
      _scheduleTimer(interval);
      return;
    }

    final nextCheckTime = _lastCheckTime!.add(interval);
    final now = DateTime.now();

    if (now.isAfter(nextCheckTime)) {
      // 已经过期，立即检查
      Logger.info('检查时间已到，立即执行');
      await _performCheck(isAutoCheck: true);
      _scheduleTimer(interval);
    } else {
      // 还没到时间，计算剩余时间
      final remaining = nextCheckTime.difference(now);
      Logger.info('下次检查时间: $nextCheckTime (${_formatDuration(remaining)}后)');
      _scheduleTimer(remaining);
    }
  }

  // 设置定时器
  void _scheduleTimer(Duration delay) {
    _updateCheckTimer?.cancel();
    _updateCheckTimer = Timer(delay, () async {
      await _performCheck(isAutoCheck: true);
      // 检查完成后，重新调度下一次
      await _scheduleNextCheck();
    });
    Logger.debug('定时器已设置: ${_formatDuration(delay)}后执行');
  }

  // 执行更新检查
  Future<AppUpdateInfo?> _performCheck({bool isAutoCheck = false}) async {
    if (_isChecking) {
      Logger.debug('更新检查正在进行中，跳过本次请求');
      return null;
    }

    _isChecking = true;
    notifyListeners();

    try {
      final checkType = isAutoCheck ? '自动' : '手动';
      Logger.info('开始$checkType检查应用更新...');

      final updateInfo = await AppUpdateService.instance.checkForUpdate();

      // 更新检查时间
      _lastCheckTime = DateTime.now();
      await AppPreferences.instance.setLastAppUpdateCheckTime(_lastCheckTime!);

      if (updateInfo != null) {
        if (updateInfo.hasUpdate) {
          Logger.info('发现新版本: ${updateInfo.latestVersion}');

          // 自动检查时，检查是否已忽略此版本
          if (isAutoCheck) {
            final ignoredVersion = AppPreferences.instance
                .getIgnoredUpdateVersion();
            if (ignoredVersion != null &&
                ignoredVersion == updateInfo.latestVersion) {
              Logger.info('版本 $ignoredVersion 已被用户忽略，跳过提示');
              _latestUpdateInfo = null;
              _isDialogShown = false;
              return updateInfo;
            }
          }

          _latestUpdateInfo = updateInfo;
          _isDialogShown = false; // 重置对话框状态
        } else {
          Logger.info('已是最新版本: ${updateInfo.currentVersion}');
          _latestUpdateInfo = null;
          _isDialogShown = false;
        }
      } else {
        Logger.warning('更新检查失败');
        _latestUpdateInfo = null;
        _isDialogShown = false;
      }

      return updateInfo;
    } catch (e) {
      Logger.error('更新检查异常: $e');
      return null;
    } finally {
      _isChecking = false;
      notifyListeners();
    }
  }

  // 手动检查更新（供 UI 调用）
  // 手动检查不会设置 latestUpdateInfo，避免触发全局监听器
  Future<AppUpdateInfo?> checkForUpdate() async {
    if (_isChecking) {
      Logger.debug('更新检查正在进行中，跳过本次请求');
      return null;
    }

    _isChecking = true;
    notifyListeners();

    try {
      Logger.info('开始手动检查应用更新...');

      final updateInfo = await AppUpdateService.instance.checkForUpdate();

      // 更新检查时间
      _lastCheckTime = DateTime.now();
      await AppPreferences.instance.setLastAppUpdateCheckTime(_lastCheckTime!);

      // 手动检查：不设置 _latestUpdateInfo，由调用方直接处理
      return updateInfo;
    } catch (e) {
      Logger.error('更新检查异常: $e');
      return null;
    } finally {
      _isChecking = false;
      notifyListeners();
    }
  }

  // 清除更新信息
  void clearUpdateInfo() {
    _latestUpdateInfo = null;
    _isDialogShown = false;
    notifyListeners();
  }

  // 标记对话框已显示
  void markDialogShown() {
    _isDialogShown = true;
    // 不触发 notifyListeners，避免重复触发监听器
  }

  // 忽略当前版本更新
  Future<void> ignoreCurrentVersion() async {
    if (_latestUpdateInfo != null) {
      await AppPreferences.instance.setIgnoredUpdateVersion(
        _latestUpdateInfo!.latestVersion,
      );
      Logger.info('已忽略版本: ${_latestUpdateInfo!.latestVersion}');
      clearUpdateInfo();
    }
  }

  // 重新启动更新调度
  Future<void> restartSchedule() async {
    Logger.info('重新启动更新调度...');
    _updateCheckTimer?.cancel();
    _updateCheckTimer = null;
    await _scheduleNextCheck();
  }

  // 格式化时长
  String _formatDuration(Duration duration) {
    if (duration.inDays > 0) {
      return '${duration.inDays}天';
    } else if (duration.inHours > 0) {
      return '${duration.inHours}小时';
    } else if (duration.inMinutes > 0) {
      return '${duration.inMinutes}分钟';
    } else {
      return '${duration.inSeconds}秒';
    }
  }

  @override
  void dispose() {
    Logger.info('AppUpdateProvider 销毁，取消定时器');
    _updateCheckTimer?.cancel();
    super.dispose();
  }
}
