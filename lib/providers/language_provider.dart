import 'package:flutter/material.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/storage/preferences.dart';

// 语言模式枚举
enum AppLanguageMode {
  system('system'),
  zh('zh'),
  zhTw('zh_TW'),
  en('en');

  const AppLanguageMode(this.value);

  // 持久化存储的字符串值
  final String value;

  // 获取本地化显示名称
  String displayName(BuildContext context) {
    final trans = context.translate;

    switch (this) {
      case AppLanguageMode.system:
        return trans.language.mode_system;
      case AppLanguageMode.zh:
        return trans.language.mode_zh;
      case AppLanguageMode.zhTw:
        return trans.language.mode_zh_tw;
      case AppLanguageMode.en:
        return trans.language.mode_en;
    }
  }

  // 转换为 AppLocale
  AppLocale? toAppLocale() {
    switch (this) {
      case AppLanguageMode.system:
        return null; // 使用系统语言
      case AppLanguageMode.zh:
        return AppLocale.zhCn;
      case AppLanguageMode.zhTw:
        return AppLocale.zhTw;
      case AppLanguageMode.en:
        return AppLocale.en;
    }
  }

  // 从存储字符串解析
  static AppLanguageMode fromString(String value) {
    for (final mode in AppLanguageMode.values) {
      if (mode.value == value) return mode;
    }
    return AppLanguageMode.system;
  }

  // 从 AppLocale 转换
  static AppLanguageMode fromAppLocale(AppLocale locale) {
    switch (locale) {
      case AppLocale.en:
        return AppLanguageMode.en;
      case AppLocale.zhCn:
        return AppLanguageMode.zh;
      case AppLocale.zhTw:
        return AppLanguageMode.zhTw;
    }
  }
}

// 统一管理应用语言的 Provider
class LanguageProvider extends ChangeNotifier {
  AppLanguageMode _languageMode = AppLanguageMode.system;
  AppLocale _currentLocale = AppLocale.en;

  // 当前语言模式
  AppLanguageMode get languageMode => _languageMode;

  // 当前实际使用的 locale
  AppLocale get currentLocale => _currentLocale;

  // 初始化 Provider，从本地存储加载语言设置
  Future<void> initialize() async {
    final savedLanguage = AppPreferences.instance.getLanguageMode();
    _languageMode = AppLanguageMode.fromString(savedLanguage);

    // 应用语言设置
    await _applyLanguageMode(_languageMode);

    notifyListeners();
  }

  // 设置语言模式
  Future<void> setLanguageMode(AppLanguageMode mode) async {
    if (_languageMode == mode) return;

    _languageMode = mode;

    // 保存到本地存储
    await AppPreferences.instance.setLanguageMode(mode.value);

    // 应用语言设置
    await _applyLanguageMode(mode);

    notifyListeners();
  }

  // 应用语言模式
  Future<void> _applyLanguageMode(AppLanguageMode mode) async {
    final locale = mode.toAppLocale();

    if (locale == null) {
      // 使用系统语言
      final deviceLocale = await LocaleSettings.useDeviceLocale();
      _currentLocale = deviceLocale;
    } else {
      // 使用指定语言
      await LocaleSettings.setLocale(locale);
      _currentLocale = locale;
    }
  }

  // 获取所有可用的语言模式
  List<AppLanguageMode> get availableLanguages => AppLanguageMode.values;

  // 检查是否为系统语言模式
  bool get isSystemMode => _languageMode == AppLanguageMode.system;
}
