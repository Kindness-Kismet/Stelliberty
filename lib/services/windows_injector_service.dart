import 'dart:async';
import 'dart:ui';

// Windows 键盘事件注入器
// 用于修复 Flutter 在 Windows 11 上 Win+V 剪贴板历史无法使用的问题
class WindowsInjector {
  static WindowsInjector get instance => _instance;
  static final WindowsInjector _instance = WindowsInjector._();

  static const Duration _injectInterval = Duration(milliseconds: 100);
  static const int _maxInjectAttempts = 50;
  static const int _physicalControlLeft = 0x700e0;
  static const int _physicalV = 0x70019;
  static const int _logicalControlLeft = 0x200000100;
  static const int _logicalV = 0x76;
  static const int _oldTargetPhysical = 0x1600000000;

  bool _startInjectKeyData = false;
  bool _hasInjectedVDown = false;
  bool _hasInjectedVUp = false;
  int _remainingInjectAttempts = 0;
  Timer? _injectTimer;
  KeyDataCallback? _delegateCallback;
  late final KeyDataCallback _patchedCallback = _handleKeyData;

  WindowsInjector._();

  // 注入键盘数据拦截器
  void injectKeyData() {
    _injectTimer?.cancel();
    _remainingInjectAttempts = _maxInjectAttempts;
    _injectTimer = Timer.periodic(_injectInterval, (_) => _injectKeyData());
    _injectKeyData();
  }

  // 执行键盘数据注入
  void _injectKeyData() {
    final callback = PlatformDispatcher.instance.onKeyData;
    if (callback != null && !identical(callback, _patchedCallback)) {
      _delegateCallback = callback;
      PlatformDispatcher.instance.onKeyData = _patchedCallback;
    }

    _remainingInjectAttempts--;
    if (_remainingInjectAttempts > 0) {
      return;
    }

    _injectTimer?.cancel();
    _injectTimer = null;
  }

  bool _handleKeyData(KeyData data) {
    final callback = _delegateCallback;
    if (callback == null) {
      return false;
    }

    final physical = data.physical;
    final logical = data.logical;
    final type = data.type;
    final synthesized = data.synthesized;
    final isTargetKey =
        physical == _oldTargetPhysical && logical == _logicalControlLeft;
    final isTargetV = physical == _oldTargetPhysical && logical == _logicalV;

    if (!isTargetKey && !isTargetV && !_startInjectKeyData) {
      return callback(data);
    }

    if (!_startInjectKeyData &&
        isTargetKey &&
        type == KeyEventType.down &&
        !synthesized) {
      _startInjectKeyData = true;
      _hasInjectedVDown = false;
      _hasInjectedVUp = false;
      data = KeyData(
        timeStamp: data.timeStamp,
        type: KeyEventType.down,
        physical: _physicalControlLeft,
        logical: _logicalControlLeft,
        character: null,
        synthesized: false,
      );
      return callback(data);
    }

    if (_startInjectKeyData &&
        physical == 0 &&
        logical == 0 &&
        type == KeyEventType.down &&
        !synthesized) {
      return true;
    }

    if (_startInjectKeyData && isTargetKey) {
      if (type == KeyEventType.up && synthesized && !_hasInjectedVUp) {
        return true;
      }

      if (type == KeyEventType.up && synthesized && _hasInjectedVUp) {
        _startInjectKeyData = false;
        _hasInjectedVDown = false;
        _hasInjectedVUp = false;
        data = KeyData(
          timeStamp: data.timeStamp,
          type: KeyEventType.up,
          physical: _physicalControlLeft,
          logical: _logicalControlLeft,
          character: null,
          synthesized: false,
        );
        return callback(data);
      }

      return true;
    }

    if (_startInjectKeyData && isTargetV) {
      if (type == KeyEventType.down && !synthesized) {
        _hasInjectedVDown = true;
        data = KeyData(
          timeStamp: data.timeStamp,
          type: KeyEventType.down,
          physical: _physicalV,
          logical: _logicalV,
          character: null,
          synthesized: false,
        );
        return callback(data);
      }

      if (type == KeyEventType.up && !synthesized && _hasInjectedVDown) {
        _hasInjectedVUp = true;
        data = KeyData(
          timeStamp: data.timeStamp,
          type: KeyEventType.up,
          physical: _physicalV,
          logical: _logicalV,
          character: null,
          synthesized: false,
        );
        return callback(data);
      }

      return true;
    }

    _startInjectKeyData = false;
    _hasInjectedVDown = false;
    _hasInjectedVUp = false;
    return callback(data);
  }
}
