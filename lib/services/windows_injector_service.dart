import 'dart:ui';

// Windows 键盘事件注入器
// 用于修复 Flutter 在 Windows 11 上 Win+V 剪贴板历史无法使用的问题
class WindowsInjector {
  static WindowsInjector get instance => _instance;
  static final WindowsInjector _instance = WindowsInjector._();

  bool _startInjectKeyData = false;

  WindowsInjector._();

  // 注入键盘数据拦截器
  void injectKeyData() {
    // 等待 500 毫秒后注入 KeyData 回调
    Future.delayed(const Duration(milliseconds: 500), _injectKeyData);
  }

  // 执行键盘数据注入
  void _injectKeyData() {
    final KeyDataCallback? callback = PlatformDispatcher.instance.onKeyData;
    if (callback == null) {
      // 获取内置回调失败，跳过注入
      return;
    }
    PlatformDispatcher.instance.onKeyData = (data) {
      // 缓存字段值，避免重复访问
      final physical = data.physical;
      final logical = data.logical;
      final type = data.type;
      final synthesized = data.synthesized;

      // 检查是否为目标按键
      final isTargetKey = physical == 0x1600000000 && logical == 0x200000100;

      // 快速路径：非目标按键且未在序列中
      if (!isTargetKey && !_startInjectKeyData) {
        return callback(data);
      }

      // 序列开始：目标按键 Down 未合成
      if (!_startInjectKeyData &&
          isTargetKey &&
          type == KeyEventType.down &&
          !synthesized) {
        _startInjectKeyData = true;
        // 修改为 Control Left 键按下事件
        data = KeyData(
          timeStamp: data.timeStamp,
          type: KeyEventType.down,
          physical: 0x700e0,
          logical: 0x200000100,
          character: null,
          synthesized: false,
        );
        return callback(data);
      }

      // 序列中的无效事件：跳过
      if (_startInjectKeyData &&
          physical == 0 &&
          logical == 0 &&
          type == KeyEventType.down &&
          !synthesized) {
        return true;
      }

      // 序列中：目标按键处理
      if (_startInjectKeyData && isTargetKey) {
        if (type == KeyEventType.up && !synthesized) {
          // 修改为 V 键按下事件
          data = KeyData(
            timeStamp: data.timeStamp,
            type: KeyEventType.down,
            physical: 0x70019,
            logical: 0x76,
            character: null,
            synthesized: false,
          );
        } else if (type == KeyEventType.down && synthesized) {
          // 修改为 V 键释放事件
          data = KeyData(
            timeStamp: data.timeStamp,
            type: KeyEventType.up,
            physical: 0x70019,
            logical: 0x76,
            character: null,
            synthesized: false,
          );
        } else if (type == KeyEventType.up && synthesized) {
          // 修改为 Control Left 键释放事件，序列结束
          _startInjectKeyData = false;
          data = KeyData(
            timeStamp: data.timeStamp,
            type: KeyEventType.up,
            physical: 0x700e0,
            logical: 0x200000100,
            character: null,
            synthesized: false,
          );
        }
        return callback(data);
      }

      // 其他情况：重置状态
      _startInjectKeyData = false;
      return callback(data);
    };
  }
}
