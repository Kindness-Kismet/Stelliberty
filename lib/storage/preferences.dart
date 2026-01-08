import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:stelliberty/storage/dev_preferences.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 通用应用持久化配置管理,管理主题、窗口、语言等应用级配置
class AppPreferences {
  AppPreferences._();

  static AppPreferences? _instance;
  static AppPreferences get instance => _instance ??= AppPreferences._();

  dynamic _prefs; // SharedPreferences 或 DeveloperPreferences

  // 检查是否为 Dev 模式
  static bool get isDevMode => kDebugMode || kProfileMode;

  // 初始化
  Future<void> init() async {
    if (isDevMode) {
      // Dev 模式：使用开发者偏好 JSON 配置
      await DeveloperPreferences.instance.init();
      _prefs = DeveloperPreferences.instance;
    } else {
      // Release 模式：使用系统 SharedPreferences
      _prefs = await SharedPreferences.getInstance();
    }
  }

  // 确保 SharedPreferences 已初始化
  void _ensureInit() {
    if (_prefs == null) {
      throw Exception('AppPreferences 未初始化，请先调用 init()');
    }
  }

  // ==================== 存储键 ====================
  static const String _kThemeMode = 'theme_mode';
  static const String _kThemeColorIndex = 'theme_color_index';
  static const String _kWindowEffect = 'window_effect';
  static const String _kWindowPositionX = 'window_position_x';
  static const String _kWindowPositionY = 'window_position_y';
  static const String _kWindowWidth = 'window_width';
  static const String _kWindowHeight = 'window_height';
  static const String _kIsMaximized = 'is_maximized';
  static const String _kLanguageMode = 'language_mode';
  static const String _kAutoStartEnabled = 'auto_start_enabled';
  static const String _kSilentStartEnabled = 'silent_start_enabled';
  static const String _kMinimizeToTray = 'minimize_to_tray';
  static const String _kAppLogEnabled = 'app_log_enabled';
  static const String _kAppAutoUpdate = 'app_auto_update';
  static const String _kAppUpdateInterval = 'app_update_interval';
  static const String _kLastAppUpdateCheckTime = 'last_app_update_check_time';
  static const String _kIgnoredUpdateVersion = 'ignored_update_version';
  static const String _kProxyGroupExpandedStates =
      'proxy_group_expanded_states';
  static const String _kHotkeyEnabled = 'hotkey_enabled';
  static const String _kHotkeyToggleProxy = 'hotkey_toggle_proxy';
  static const String _kHotkeyToggleTun = 'hotkey_toggle_tun';
  static const String _kHotkeyShowWindow = 'hotkey_show_window';
  static const String _kHotkeyExitApp = 'hotkey_exit_app';

  // ==================== 主题配置 ====================

  // 获取主题模式
  ThemeMode getThemeMode() {
    _ensureInit();
    final mode = _prefs!.getString(_kThemeMode);
    switch (mode) {
      case 'light':
        return ThemeMode.light;
      case 'dark':
        return ThemeMode.dark;
      case 'system':
      default:
        return ThemeMode.system;
    }
  }

  // 保存主题模式
  Future<void> setThemeMode(ThemeMode mode) async {
    _ensureInit();
    String value;
    switch (mode) {
      case ThemeMode.light:
        value = 'light';
        break;
      case ThemeMode.dark:
        value = 'dark';
        break;
      case ThemeMode.system:
        value = 'system';
        break;
    }
    await _prefs!.setString(_kThemeMode, value);
  }

  // 获取主题颜色索引
  int getThemeColorIndex() {
    _ensureInit();
    return _prefs!.getInt(_kThemeColorIndex) ?? 0;
  }

  // 保存主题颜色索引
  Future<void> setThemeColorIndex(int index) async {
    _ensureInit();
    await _prefs!.setInt(_kThemeColorIndex, index);
  }

  // ==================== 窗口配置 ====================

  // 获取窗口效果
  String getWindowEffect() {
    _ensureInit();
    return _prefs!.getString(_kWindowEffect) ?? 'disabled';
  }

  // 保存窗口效果
  Future<void> setWindowEffect(String effect) async {
    _ensureInit();
    await _prefs!.setString(_kWindowEffect, effect);
  }

  // 获取窗口位置
  Offset? getWindowPosition() {
    _ensureInit();
    final x = _prefs!.getDouble(_kWindowPositionX);
    final y = _prefs!.getDouble(_kWindowPositionY);
    if (x != null && y != null) {
      return Offset(x, y);
    }
    return null;
  }

  // 保存窗口位置
  Future<void> setWindowPosition(Offset position) async {
    _ensureInit();
    await _prefs!.setDouble(_kWindowPositionX, position.dx);
    await _prefs!.setDouble(_kWindowPositionY, position.dy);
  }

  // 获取窗口大小
  Size? getWindowSize() {
    _ensureInit();
    final width = _prefs!.getDouble(_kWindowWidth);
    final height = _prefs!.getDouble(_kWindowHeight);
    if (width != null && height != null) {
      return Size(width, height);
    }
    return null;
  }

  // 保存窗口大小
  Future<void> setWindowSize(Size size) async {
    _ensureInit();
    await _prefs!.setDouble(_kWindowWidth, size.width);
    await _prefs!.setDouble(_kWindowHeight, size.height);
  }

  // 获取窗口是否最大化
  bool getIsMaximized() {
    _ensureInit();
    return _prefs!.getBool(_kIsMaximized) ?? false;
  }

  // 保存窗口最大化状态
  Future<void> setIsMaximized(bool isMaximized) async {
    _ensureInit();
    await _prefs!.setBool(_kIsMaximized, isMaximized);
  }

  // ==================== 语言配置 ====================

  // 获取语言模式
  String getLanguageMode() {
    _ensureInit();
    return _prefs!.getString(_kLanguageMode) ?? 'system';
  }

  // 保存语言模式
  Future<void> setLanguageMode(String mode) async {
    _ensureInit();
    await _prefs!.setString(_kLanguageMode, mode);
  }

  // ==================== 应用行为配置 ====================

  // 获取开机自启动状态
  bool getAutoStartEnabled() {
    _ensureInit();
    return _prefs!.getBool(_kAutoStartEnabled) ?? false;
  }

  // 保存开机自启动状态
  Future<void> setAutoStartEnabled(bool enabled) async {
    _ensureInit();
    await _prefs!.setBool(_kAutoStartEnabled, enabled);
  }

  // 获取静默启动状态
  bool getSilentStartEnabled() {
    _ensureInit();
    return _prefs!.getBool(_kSilentStartEnabled) ?? false;
  }

  // 保存静默启动状态
  Future<void> setSilentStartEnabled(bool enabled) async {
    _ensureInit();
    await _prefs!.setBool(_kSilentStartEnabled, enabled);
  }

  // 获取最小化到托盘状态
  bool getMinimizeToTray() {
    _ensureInit();
    return _prefs!.getBool(_kMinimizeToTray) ?? false; // 默认禁用
  }

  // 保存最小化到托盘状态
  Future<void> setMinimizeToTray(bool enabled) async {
    _ensureInit();
    await _prefs!.setBool(_kMinimizeToTray, enabled);
  }

  // 获取应用日志启用状态
  bool getAppLogEnabled() {
    _ensureInit();
    return _prefs!.getBool(_kAppLogEnabled) ?? false; // 默认禁用
  }

  // 保存应用日志启用状态
  Future<void> setAppLogEnabled(bool enabled) async {
    _ensureInit();
    await _prefs!.setBool(_kAppLogEnabled, enabled);
  }

  // ==================== 快捷键配置 ====================

  // 获取全局快捷键启用状态
  bool getHotkeyEnabled() {
    _ensureInit();
    return _prefs!.getBool(_kHotkeyEnabled) ?? false;
  }

  // 保存全局快捷键启用状态
  Future<void> setHotkeyEnabled(bool enabled) async {
    _ensureInit();
    await _prefs!.setBool(_kHotkeyEnabled, enabled);
  }

  // 获取切换系统代理快捷键
  String? getHotkeyToggleProxy() {
    _ensureInit();
    return _prefs!.getString(_kHotkeyToggleProxy);
  }

  // 保存切换系统代理快捷键
  Future<void> setHotkeyToggleProxy(String? hotkey) async {
    _ensureInit();
    if (hotkey == null || hotkey.isEmpty) {
      await _prefs!.remove(_kHotkeyToggleProxy);
    } else {
      await _prefs!.setString(_kHotkeyToggleProxy, hotkey);
    }
  }

  // 获取切换 TUN 模式快捷键
  String? getHotkeyToggleTun() {
    _ensureInit();
    return _prefs!.getString(_kHotkeyToggleTun);
  }

  // 保存切换 TUN 模式快捷键
  Future<void> setHotkeyToggleTun(String? hotkey) async {
    _ensureInit();
    if (hotkey == null || hotkey.isEmpty) {
      await _prefs!.remove(_kHotkeyToggleTun);
    } else {
      await _prefs!.setString(_kHotkeyToggleTun, hotkey);
    }
  }

  // 获取显示/隐藏窗口快捷键
  String? getHotkeyShowWindow() {
    _ensureInit();
    return _prefs!.getString(_kHotkeyShowWindow);
  }

  // 保存显示/隐藏窗口快捷键
  Future<void> setHotkeyShowWindow(String? hotkey) async {
    _ensureInit();
    if (hotkey == null || hotkey.isEmpty) {
      await _prefs!.remove(_kHotkeyShowWindow);
    } else {
      await _prefs!.setString(_kHotkeyShowWindow, hotkey);
    }
  }

  // 获取退出应用快捷键
  String? getHotkeyExitApp() {
    _ensureInit();
    return _prefs!.getString(_kHotkeyExitApp);
  }

  // 保存退出应用快捷键
  Future<void> setHotkeyExitApp(String? hotkey) async {
    _ensureInit();
    if (hotkey == null || hotkey.isEmpty) {
      await _prefs!.remove(_kHotkeyExitApp);
    } else {
      await _prefs!.setString(_kHotkeyExitApp, hotkey);
    }
  }

  // ==================== 应用更新配置 ====================

  // 获取应用自动更新启用状态
  bool getAppAutoUpdate() {
    _ensureInit();
    return _prefs!.getBool(_kAppAutoUpdate) ?? false; // 默认禁用
  }

  // 保存应用自动更新启用状态
  Future<void> setAppAutoUpdate(bool enabled) async {
    _ensureInit();
    await _prefs!.setBool(_kAppAutoUpdate, enabled);
  }

  // 获取应用更新检测间隔
  String getAppUpdateInterval() {
    _ensureInit();
    return _prefs!.getString(_kAppUpdateInterval) ?? 'startup'; // 默认每次启动
  }

  // 保存应用更新检测间隔
  Future<void> setAppUpdateInterval(String interval) async {
    _ensureInit();
    await _prefs!.setString(_kAppUpdateInterval, interval);
  }

  // 获取上次应用更新检查时间
  DateTime? getLastAppUpdateCheckTime() {
    _ensureInit();
    final timeStr = _prefs!.getString(_kLastAppUpdateCheckTime);
    if (timeStr != null) {
      try {
        return DateTime.parse(timeStr);
      } catch (e) {
        return null;
      }
    }
    return null;
  }

  // 保存上次应用更新检查时间
  Future<void> setLastAppUpdateCheckTime(DateTime time) async {
    _ensureInit();
    await _prefs!.setString(_kLastAppUpdateCheckTime, time.toIso8601String());
  }

  // 获取已忽略的更新版本
  String? getIgnoredUpdateVersion() {
    _ensureInit();
    return _prefs!.getString(_kIgnoredUpdateVersion);
  }

  // 保存已忽略的更新版本
  Future<void> setIgnoredUpdateVersion(String version) async {
    _ensureInit();
    await _prefs!.setString(_kIgnoredUpdateVersion, version);
  }

  // 清除已忽略的更新版本
  Future<void> clearIgnoredUpdateVersion() async {
    _ensureInit();
    await _prefs!.remove(_kIgnoredUpdateVersion);
  }

  // ==================== 通用存储方法 ====================
  // 用于业务代码需要保存自定义键值的场景（如 UI 状态、缓存等）

  // 获取字符串值
  String? getString(String key) {
    _ensureInit();
    return _prefs!.getString(key);
  }

  // 保存字符串值
  Future<void> setString(String key, String value) async {
    _ensureInit();
    await _prefs!.setString(key, value);
  }

  // 获取双精度浮点数值
  double? getDouble(String key) {
    _ensureInit();
    return _prefs!.getDouble(key);
  }

  // 保存双精度浮点数值
  Future<void> setDouble(String key, double value) async {
    _ensureInit();
    await _prefs!.setDouble(key, value);
  }

  // 获取整数值
  int? getInt(String key) {
    _ensureInit();
    return _prefs!.getInt(key);
  }

  // 保存整数值
  Future<void> setInt(String key, int value) async {
    _ensureInit();
    await _prefs!.setInt(key, value);
  }

  // 获取布尔值
  bool? getBool(String key) {
    _ensureInit();
    return _prefs!.getBool(key);
  }

  // 保存布尔值
  Future<void> setBool(String key, bool value) async {
    _ensureInit();
    await _prefs!.setBool(key, value);
  }

  // 删除指定键
  Future<void> remove(String key) async {
    _ensureInit();
    await _prefs!.remove(key);
  }

  // 检查键是否存在
  bool containsKey(String key) {
    _ensureInit();
    return _prefs!.containsKey(key);
  }

  // 获取所有存储的配置
  Map<String, dynamic> getAllSettings() {
    _ensureInit();
    final keys = [
      _kThemeMode,
      _kThemeColorIndex,
      _kWindowEffect,
      _kWindowPositionX,
      _kWindowPositionY,
      _kWindowWidth,
      _kWindowHeight,
      _kIsMaximized,
      _kLanguageMode,
      _kAutoStartEnabled,
      _kSilentStartEnabled,
      _kMinimizeToTray,
      _kAppLogEnabled,
      _kAppAutoUpdate,
      _kAppUpdateInterval,
      _kLastAppUpdateCheckTime,
      _kIgnoredUpdateVersion,
      _kHotkeyEnabled,
      _kHotkeyToggleProxy,
      _kHotkeyToggleTun,
    ];

    final Map<String, dynamic> settings = {};
    for (final key in keys) {
      if (_prefs!.containsKey(key)) {
        settings[key] = _prefs!.get(key);
      }
    }
    return settings;
  }

  // 重置所有应用配置到默认值
  Future<void> resetToDefaults() async {
    _ensureInit();
    final keys = [
      _kThemeMode,
      _kThemeColorIndex,
      _kWindowEffect,
      _kWindowPositionX,
      _kWindowPositionY,
      _kWindowWidth,
      _kWindowHeight,
      _kIsMaximized,
      _kLanguageMode,
      _kAutoStartEnabled,
      _kSilentStartEnabled,
      _kMinimizeToTray,
      _kAppLogEnabled,
      _kAppAutoUpdate,
      _kAppUpdateInterval,
      _kLastAppUpdateCheckTime,
      _kHotkeyEnabled,
      _kHotkeyToggleProxy,
      _kHotkeyToggleTun,
    ];

    for (final key in keys) {
      await _prefs!.remove(key);
    }
  }

  // ==================== 代理组折叠状态 ====================

  // 获取代理组折叠状态
  // 返回 Map<String, bool>，key 为代理组名称，value 为是否展开
  Map<String, bool> getProxyGroupExpandedStates() {
    _ensureInit();
    final jsonString = _prefs!.getString(_kProxyGroupExpandedStates);
    if (jsonString == null || jsonString.isEmpty) {
      return {};
    }

    try {
      // 使用简单的格式：groupName1:true,groupName2:false
      final result = <String, bool>{};
      final pairs = jsonString.split(',');
      for (final pair in pairs) {
        final parts = pair.split(':');
        if (parts.length == 2) {
          result[parts[0]] = parts[1] == 'true';
        }
      }
      return result;
    } catch (e) {
      Logger.error('解析折叠状态失败: $e');
      return {};
    }
  }

  // 保存代理组折叠状态
  Future<void> setProxyGroupExpandedStates(Map<String, bool> states) async {
    _ensureInit();
    // 将 Map 转换为简单的字符串格式：groupName1:true,groupName2:false
    final encoded = states.entries.map((e) => '${e.key}:${e.value}').join(',');
    await _prefs!.setString(_kProxyGroupExpandedStates, encoded);
  }

  // 保存单个代理组的折叠状态
  Future<void> setProxyGroupExpanded(String groupName, bool isExpanded) async {
    final states = getProxyGroupExpandedStates();
    states[groupName] = isExpanded;
    await setProxyGroupExpandedStates(states);
  }
}
