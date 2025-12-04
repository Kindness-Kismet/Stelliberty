import 'package:flutter/material.dart';
import 'package:system_theme/system_theme.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 类型安全的主题模式枚举
enum AppThemeMode {
  auto('auto'),
  light('light'),
  dark('dark');

  const AppThemeMode(this.value);

  // 持久化存储的字符串值
  final String value;

  // 获取本地化显示名称
  String get displayName {
    switch (this) {
      case AppThemeMode.auto:
        return translate.theme.system;
      case AppThemeMode.light:
        return translate.theme.light;
      case AppThemeMode.dark:
        return translate.theme.dark;
    }
  }

  // 将 Flutter 的 ThemeMode 转换为 AppThemeMode
  static AppThemeMode fromThemeMode(ThemeMode mode) {
    switch (mode) {
      case ThemeMode.light:
        return AppThemeMode.light;
      case ThemeMode.dark:
        return AppThemeMode.dark;
      case ThemeMode.system:
        return AppThemeMode.auto;
    }
  }

  // 将 AppThemeMode 转换为 Flutter 的 ThemeMode
  ThemeMode toThemeMode() {
    switch (this) {
      case AppThemeMode.light:
        return ThemeMode.light;
      case AppThemeMode.dark:
        return ThemeMode.dark;
      case AppThemeMode.auto:
        return ThemeMode.system;
    }
  }

  // 从存储字符串解析为 AppThemeMode 枚举
  static AppThemeMode fromString(String value) {
    switch (value) {
      case 'light':
        return AppThemeMode.light;
      case 'dark':
        return AppThemeMode.dark;
      case 'auto':
      default:
        return AppThemeMode.auto;
    }
  }
}

// 主题颜色选项通用接口
sealed class ThemeColorOption {}

// 静态不变的颜色选项
class StaticThemeColor extends ThemeColorOption {
  final Color color;
  StaticThemeColor(this.color);
}

// 动态颜色选项，跟随操作系统强调色
class SystemAccentThemeColor extends ThemeColorOption {}

// 用户可选的精选主题颜色列表
final List<ThemeColorOption> colorOptions = [
  SystemAccentThemeColor(), // 系统强调色
  // 红色色系
  StaticThemeColor(Colors.pink.shade300),
  StaticThemeColor(Colors.pink),
  StaticThemeColor(Colors.red.shade400),
  StaticThemeColor(Colors.red),
  // 橙色系
  StaticThemeColor(Colors.deepOrange.shade300),
  StaticThemeColor(Colors.deepOrange),
  StaticThemeColor(Colors.orange.shade400),
  StaticThemeColor(Colors.orange),
  StaticThemeColor(Colors.amber.shade600),
  // 黄色系
  StaticThemeColor(Colors.yellow.shade600),
  // 绿色系
  StaticThemeColor(Colors.lime.shade600),
  StaticThemeColor(Colors.lightGreen.shade400),
  StaticThemeColor(Colors.lightGreen),
  StaticThemeColor(Colors.green),
  StaticThemeColor(Colors.green.shade700),
  // 青色系
  StaticThemeColor(Colors.teal.shade300),
  StaticThemeColor(Colors.teal),
  StaticThemeColor(Colors.cyan.shade400),
  StaticThemeColor(Colors.cyan),
  // 蓝色系
  StaticThemeColor(Colors.lightBlue.shade300),
  StaticThemeColor(Colors.lightBlue),
  StaticThemeColor(Colors.blue),
  StaticThemeColor(Colors.blue.shade700),
  // 靛蓝/紫色系
  StaticThemeColor(Colors.indigo.shade300),
  StaticThemeColor(Colors.indigo),
  StaticThemeColor(Colors.deepPurple.shade300),
  StaticThemeColor(Colors.deepPurple),
  StaticThemeColor(Colors.purple.shade400),
  StaticThemeColor(Colors.purple),
  // 中性色
  StaticThemeColor(Colors.blueGrey),
  StaticThemeColor(Colors.brown),
];

// 统一管理主题模式、颜色和窗口效果的 Provider
class ThemeProvider extends ChangeNotifier {
  AppThemeMode _themeMode = AppThemeMode.auto;
  int _colorIndex = 0;
  Brightness _brightness = Brightness.light;

  // 当前主题模式
  AppThemeMode get themeMode => _themeMode;

  // 当前选择的颜色索引
  int get colorIndex => _colorIndex;

  // 当前选择的颜色选项
  ThemeColorOption get selectedColorOption => colorOptions[_colorIndex];

  // 当前亮度
  Brightness get brightness => _brightness;

  // 初始化 Provider，从本地存储加载并应用用户设置
  Future<void> initialize() async {
    // 加载系统强调色
    await SystemTheme.accentColor.load();

    // 从配置加载设置
    final savedThemeMode = AppPreferences.instance.getThemeMode();
    final savedColorIndex = AppPreferences.instance.getThemeColorIndex();

    // 设置主题模式
    _themeMode = AppThemeMode.fromThemeMode(savedThemeMode);

    // 设置颜色索引
    _colorIndex = savedColorIndex.clamp(0, colorOptions.length - 1);

    // 计算当前亮度
    _updateBrightness();

    notifyListeners();
  }

  // 更新主题模式
  Future<void> setThemeMode(AppThemeMode mode) async {
    if (_themeMode == mode) return;

    _themeMode = mode;
    await AppPreferences.instance.setThemeMode(mode.toThemeMode());

    _updateBrightness();

    // 如果使用系统强调色，重新加载以获取适配当前主题的颜色
    await _reloadSystemAccentColorIfNeeded();

    notifyListeners();
  }

  // 更新颜色索引
  Future<void> setColorIndex(int index) async {
    if (_colorIndex == index) return;

    _colorIndex = index.clamp(0, colorOptions.length - 1);
    await AppPreferences.instance.setThemeColorIndex(_colorIndex);

    // 如果切换到系统强调色，重新加载以获取适配当前主题的颜色
    await _reloadSystemAccentColorIfNeeded();

    notifyListeners();
  }

  // 循环切换到下一个颜色
  Future<void> cycleNextColor() async {
    final nextIndex = (_colorIndex + 1) % colorOptions.length;
    await setColorIndex(nextIndex);
  }

  // 当系统主题发生变化时调用此方法更新亮度
  Future<void> updateBrightness(Brightness brightness) async {
    if (_brightness == brightness) return;

    _brightness = brightness;

    // 如果使用系统强调色，重新加载以获取适配当前亮度的颜色
    await _reloadSystemAccentColorIfNeeded();

    notifyListeners();
  }

  // 获取当前种子颜色
  Color get seedColor {
    return switch (selectedColorOption) {
      SystemAccentThemeColor() => SystemTheme.accentColor.accent,
      StaticThemeColor(color: final presetColor) => presetColor,
    };
  }

  // 获取浅色模式的 ColorScheme
  ColorScheme get lightColorScheme {
    return ColorScheme.fromSeed(
      seedColor: seedColor,
      brightness: Brightness.light,
    );
  }

  // 获取深色模式的 ColorScheme
  ColorScheme get darkColorScheme {
    final scheme = ColorScheme.fromSeed(
      seedColor: seedColor,
      brightness: Brightness.dark,
    );

    // 降低暗色主题强调色亮度，使其更柔和
    return scheme.copyWith(
      primary: Color.lerp(
        scheme.primary,
        const Color.fromARGB(255, 113, 113, 113),
        0.3,
      )!,
    );
  }

  // 根据主题模式更新当前亮度
  void _updateBrightness() {
    switch (_themeMode) {
      case AppThemeMode.light:
        _brightness = Brightness.light;
        break;
      case AppThemeMode.dark:
        _brightness = Brightness.dark;
        break;
      case AppThemeMode.auto:
        _brightness =
            WidgetsBinding.instance.platformDispatcher.platformBrightness;
        break;
    }
  }

  // 如果使用系统强调色，重新加载以获取最新颜色
  Future<void> _reloadSystemAccentColorIfNeeded() async {
    if (selectedColorOption is SystemAccentThemeColor) {
      await SystemTheme.accentColor.load();
    }
  }
}
