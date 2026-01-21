import 'package:flutter/foundation.dart';
import 'package:stelliberty/services/auto_start_service.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 应用行为设置状态管理
class BehaviorSettingsProvider extends ChangeNotifier {
  bool _autoStartEnabled = false;
  bool _silentStartEnabled = false;
  bool _minimizeToTray = false;
  bool _appLogEnabled = false;
  bool _isLoading = true;

  bool get autoStartEnabled => _autoStartEnabled;
  bool get silentStartEnabled => _silentStartEnabled;
  bool get minimizeToTray => _minimizeToTray;
  bool get appLogEnabled => _appLogEnabled;
  bool get isLoading => _isLoading;

  BehaviorSettingsProvider() {
    _initialize();
  }

  // 初始化
  Future<void> _initialize() async {
    _loadSettings();
    await _refreshAutoStartStatus();
    _isLoading = false;
    notifyListeners();
  }

  // 从本地存储加载设置
  void _loadSettings() {
    final prefs = AppPreferences.instance;
    _autoStartEnabled = prefs.getAutoStartEnabled();
    _silentStartEnabled = prefs.getSilentStartEnabled();
    _minimizeToTray = prefs.getMinimizeToTray();
    _appLogEnabled = prefs.getAppLogEnabled();
  }

  // 从 Rust 端刷新开机自启动真实状态
  Future<void> _refreshAutoStartStatus() async {
    final status = await AutoStartService.instance.getStatus();
    _autoStartEnabled = status;
  }

  // 更新开机自启动设置
  Future<bool> updateAutoStart(bool value) async {
    final oldValue = _autoStartEnabled;
    _autoStartEnabled = value;
    notifyListeners();

    final success = await AutoStartService.instance.setStatus(value);
    if (!success) {
      _autoStartEnabled = oldValue;
      notifyListeners();
      return false;
    }

    return true;
  }

  // 更新静默启动设置
  Future<void> updateSilentStart(bool value) async {
    _silentStartEnabled = value;
    notifyListeners();

    await AppPreferences.instance.setSilentStartEnabled(value);
    Logger.info('静默启动已${value ? '启用' : '禁用'}');
  }

  // 更新最小化到托盘设置
  Future<void> updateMinimizeToTray(bool value) async {
    _minimizeToTray = value;
    notifyListeners();

    await AppPreferences.instance.setMinimizeToTray(value);
    Logger.info('最小化到托盘已${value ? '启用' : '禁用'}');
  }

  // 更新应用日志设置
  Future<bool> updateAppLog(bool value) async {
    final oldValue = _appLogEnabled;
    _appLogEnabled = value;
    notifyListeners();

    try {
      await AppPreferences.instance.setAppLogEnabled(value);
      SetAppLogEnabled(isEnabled: value).sendSignalToRust();
      Logger.info('应用日志已${value ? '启用' : '禁用'}');
      return true;
    } catch (e) {
      _appLogEnabled = oldValue;
      notifyListeners();
      Logger.error('保存应用日志设置失败: $e');
      return false;
    }
  }

  Future<void> applyRestoredSettings() async {
    _loadSettings();
    await _refreshAutoStartStatus();
    notifyListeners();

    await updateAppLog(_appLogEnabled);
  }
}
