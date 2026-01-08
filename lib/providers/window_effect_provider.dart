import 'dart:io';
import 'package:flutter/material.dart';
import 'package:flutter_acrylic/flutter_acrylic.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 窗口效果枚举
enum AppWindowEffect { none, acrylic, mica, tabbed }

class WindowEffectProvider extends ChangeNotifier {
  AppWindowEffect _windowEffect = AppWindowEffect.none;
  Brightness _brightness = Brightness.light;

  AppWindowEffect get windowEffect => _windowEffect;

  Future<void> initialize() async {
    final savedEffectName = AppPreferences.instance.getWindowEffect();
    _windowEffect = AppWindowEffect.values.firstWhere(
      (e) => e.name == savedEffectName,
      orElse: () => AppWindowEffect.none,
    );
    await _applyWindowEffect();
    notifyListeners();
  }

  Future<void> setWindowEffect(AppWindowEffect effect) async {
    if (_windowEffect == effect) return;

    _windowEffect = effect;
    await AppPreferences.instance.setWindowEffect(effect.name);
    await _applyWindowEffect();
    notifyListeners();
  }

  void updateBrightness(Brightness brightness) {
    if (_brightness == brightness) return;

    _brightness = brightness;
    _applyWindowEffect();
    notifyListeners();
  }

  Color? get windowEffectBackgroundColor {
    switch (_windowEffect) {
      case AppWindowEffect.none:
        return null;
      case AppWindowEffect.acrylic:
        final isDark = _brightness == Brightness.dark;
        return isDark
            ? Colors.black.withAlpha(100)
            : Colors.white.withAlpha(100);
      case AppWindowEffect.mica:
      case AppWindowEffect.tabbed:
        return Colors.transparent;
    }
  }

  Future<void> _applyWindowEffect() async {
    // 窗口效果仅在桌面平台可用，移动平台直接返回
    if (Platform.isAndroid || Platform.isIOS) {
      Logger.debug('移动平台不支持窗口效果，跳过设置');
      return;
    }

    final windowEffect = switch (_windowEffect) {
      AppWindowEffect.mica => WindowEffect.mica,
      AppWindowEffect.acrylic => WindowEffect.acrylic,
      AppWindowEffect.tabbed => WindowEffect.tabbed,
      AppWindowEffect.none => WindowEffect.disabled,
    };

    final isDark = _brightness == Brightness.dark;

    try {
      if (_windowEffect == AppWindowEffect.none) {
        await Window.setEffect(effect: windowEffect);
      } else {
        await Window.setEffect(effect: windowEffect, dark: isDark);
      }
    } catch (e) {
      Logger.error('加载窗口效果失败：$e');
    }
  }
}
