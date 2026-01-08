import 'dart:convert';
import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 开发者偏好管理器（Dev 模式专用）
// 使用 JSON 文件存储在运行目录，方便开发调试
class DeveloperPreferences {
  DeveloperPreferences._();

  static DeveloperPreferences? _instance;
  static DeveloperPreferences get instance =>
      _instance ??= DeveloperPreferences._();

  Map<String, dynamic> _data = {};
  File? _file;
  bool _isInitialized = false;

  // 检查是否为 Dev 模式
  static bool get isDevMode => kDebugMode || kProfileMode;

  // 初始化
  Future<void> init() async {
    if (!isDevMode) {
      Logger.warning('非 Dev 模式，不应使用 DeveloperPreferences');
      return;
    }

    final dataPath = PathService.instance.appDataPath;
    _file = File('$dataPath/shared_preferences_dev.json');

    // 读取配置文件
    if (await _file!.exists()) {
      try {
        final content = await _file!.readAsString();
        _data = json.decode(content) as Map<String, dynamic>;
        Logger.info('Dev 模式配置已加载：${_file!.path}');
      } catch (e) {
        Logger.error('读取 Dev 配置失败：$e，将使用默认配置');
        _data = {};
      }
    } else {
      Logger.info('Dev 配置文件不存在，创建新文件：${_file!.path}');
      _data = {};
      // 立即创建默认配置文件
      await _save();
    }

    _isInitialized = true;
  }

  // 确保已初始化
  void _ensureInit() {
    if (!_isInitialized) {
      throw Exception('DeveloperPreferences 未初始化，请先调用 init()');
    }
  }

  // 保存到文件
  Future<void> _save() async {
    if (_file == null) return;

    try {
      await _file!.writeAsString(
        const JsonEncoder.withIndent('  ').convert(_data),
      );
    } catch (e) {
      Logger.error('保存 Dev 配置失败：$e');
      rethrow; // 重新抛出异常，让调用者知道保存失败
    }
  }

  // 基础操作方法

  String? getString(String key) {
    _ensureInit();
    return _data[key] as String?;
  }

  Future<void> setString(String key, String value) async {
    _ensureInit();
    _data[key] = value;
    await _save();
  }

  int? getInt(String key) {
    _ensureInit();
    return _data[key] as int?;
  }

  Future<void> setInt(String key, int value) async {
    _ensureInit();
    _data[key] = value;
    await _save();
  }

  double? getDouble(String key) {
    _ensureInit();
    return _data[key] as double?;
  }

  Future<void> setDouble(String key, double value) async {
    _ensureInit();
    _data[key] = value;
    await _save();
  }

  bool? getBool(String key) {
    _ensureInit();
    return _data[key] as bool?;
  }

  Future<void> setBool(String key, bool value) async {
    _ensureInit();
    _data[key] = value;
    await _save();
  }

  List<String>? getStringList(String key) {
    _ensureInit();
    final list = _data[key] as List?;
    return list?.cast<String>();
  }

  Future<void> setStringList(String key, List<String> value) async {
    _ensureInit();
    _data[key] = value;
    await _save();
  }

  Future<void> remove(String key) async {
    _ensureInit();
    _data.remove(key);
    await _save();
  }

  bool containsKey(String key) {
    _ensureInit();
    return _data.containsKey(key);
  }

  Future<void> clear() async {
    _ensureInit();
    _data.clear();
    await _save();
  }

  Set<String> getKeys() {
    _ensureInit();
    return _data.keys.toSet();
  }

  dynamic get(String key) {
    _ensureInit();
    return _data[key];
  }
}
