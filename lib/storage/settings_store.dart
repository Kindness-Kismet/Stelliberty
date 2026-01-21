import 'dart:convert';
import 'dart:io';
import 'package:stelliberty/services/log_print_service.dart';

// 设置偏好 JSON 存储
class SettingsStore {
  SettingsStore._();

  static SettingsStore? _instance;
  static SettingsStore get instance => _instance ??= SettingsStore._();

  Map<String, dynamic> _data = {};
  File? _file;
  String? _filePath;
  bool _isInitialized = false;
  bool _isInitializing = false;

  // 初始化并加载配置文件
  Future<void> init(String filePath) async {
    if (_isInitialized || _isInitializing) {
      if (_filePath != null && _filePath != filePath) {
        throw Exception('SettingsStore 已使用其他路径初始化');
      }
      return;
    }

    _isInitializing = true;
    _filePath = filePath;

    try {
      _file = File(filePath);
      await _loadFromFile();

      _isInitialized = true;
    } finally {
      _isInitializing = false;
    }
  }

  Future<void> reload() async {
    if (!_isInitialized) {
      throw Exception('SettingsStore 未初始化，请先调用 init()');
    }
    await _reload();
  }

  Future<void> _reload() async {
    if (_isInitializing) return;
    _isInitializing = true;
    try {
      if (_file == null && _filePath != null) {
        _file = File(_filePath!);
      }
      await _loadFromFile();
    } finally {
      _isInitializing = false;
    }
  }

  Future<void> _loadFromFile() async {
    if (_file == null) {
      throw Exception('SettingsStore 未初始化，请先调用 init()');
    }

    if (await _file!.exists()) {
      try {
        final content = await _file!.readAsString();
        _data = json.decode(content) as Map<String, dynamic>;
        Logger.info('设置偏好已加载：${_file!.path}');
      } catch (e) {
        Logger.error('读取设置偏好失败：$e，使用默认配置');
        _data = {};
      }
    } else {
      Logger.info('设置偏好文件不存在，创建新文件：${_file!.path}');
      _data = {};
      await _save();
    }
  }

  // 确保已初始化
  void _ensureInit() {
    if (!_isInitialized) {
      throw Exception('SettingsStore 未初始化，请先调用 init()');
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
      Logger.error('保存设置偏好失败：$e');
      rethrow;
    }
  }

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
