import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:hotkey_manager/hotkey_manager.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/hotkey_service.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 全局快捷键设置卡片
class HotkeySettingsCard extends StatefulWidget {
  const HotkeySettingsCard({super.key});

  @override
  State<HotkeySettingsCard> createState() => _HotkeySettingsCardState();
}

class _HotkeySettingsCardState extends State<HotkeySettingsCard> {
  bool _isEnabled = false;
  String? _toggleProxyHotkey;
  String? _toggleTunHotkey;
  String? _showWindowHotkey;
  String? _exitAppHotkey;

  // 录制状态
  bool _isRecordingProxy = false;
  bool _isRecordingTun = false;
  bool _isRecordingShowWindow = false;
  bool _isRecordingExitApp = false;

  @override
  void initState() {
    super.initState();
    _loadSettings();
  }

  void _loadSettings() {
    final prefs = AppPreferences.instance;
    setState(() {
      _isEnabled = prefs.getHotkeyEnabled();
      _toggleProxyHotkey = prefs.getHotkeyToggleProxy();
      _toggleTunHotkey = prefs.getHotkeyToggleTun();
      _showWindowHotkey = prefs.getHotkeyShowWindow();
      _exitAppHotkey = prefs.getHotkeyExitApp();
    });
  }

  Future<void> _toggleEnabled(bool value) async {
    final oldValue = _isEnabled;
    setState(() {
      _isEnabled = value;
    });

    try {
      final service = HotkeyService.instance;
      await service.setEnabled(value);
      Logger.info('全局快捷键已${value ? "启用" : "禁用"}');
    } catch (e) {
      setState(() {
        _isEnabled = oldValue;
      });
      Logger.error('切换全局快捷键状态失败: $e');
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(context.translate.behavior.hotkey_set_failed)),
        );
      }
    }
  }

  // 将 HotKey 对象转换为字符串
  String _hotKeyToString(HotKey hotKey) {
    final parts = <String>[];

    if (hotKey.modifiers != null) {
      for (final modifier in hotKey.modifiers!) {
        switch (modifier) {
          case HotKeyModifier.control:
            parts.add('Ctrl');
            break;
          case HotKeyModifier.alt:
            parts.add('Alt');
            break;
          case HotKeyModifier.shift:
            parts.add('Shift');
            break;
          case HotKeyModifier.meta:
            parts.add('Win');
            break;
          case HotKeyModifier.capsLock:
            break;
          case HotKeyModifier.fn:
            break;
        }
      }
    }

    final keyName = _keyToString(hotKey.key);
    if (keyName != null) {
      parts.add(keyName);
    }

    return parts.join('+');
  }

  String? _keyToString(KeyboardKey key) {
    // 处理 PhysicalKeyboardKey（通过 USB HID 码）
    if (key is PhysicalKeyboardKey) {
      final usb = key.usbHidUsage;

      // F1-F12: 0x0007003a-0x00070045
      if (usb >= 0x0007003a && usb <= 0x00070045) {
        return 'F${usb - 0x00070039}';
      }

      // 数字 1-9: 0x0007001e-0x00070026
      if (usb >= 0x0007001e && usb <= 0x00070026) {
        return '${usb - 0x0007001d}';
      }
      // 数字 0: 0x00070027
      if (usb == 0x00070027) return '0';

      // 字母 A-Z: 0x00070004-0x0007001d
      if (usb >= 0x00070004 && usb <= 0x0007001d) {
        return String.fromCharCode(65 + (usb - 0x00070004));
      }

      // 特殊键
      switch (usb) {
        case 0x0007002c:
          return 'Space';
        case 0x00070028:
          return 'Enter';
        case 0x00070029:
          return 'Esc';
        case 0x0007002b:
          return 'Tab';
        case 0x0007002a:
          return 'Backspace';
        case 0x0007004c:
          return 'Delete';
        case 0x0007004a:
          return 'Home';
        case 0x0007004d:
          return 'End';
        case 0x0007004b:
          return 'PageUp';
        case 0x0007004e:
          return 'PageDown';
        case 0x00070052:
          return 'Up';
        case 0x00070051:
          return 'Down';
        case 0x00070050:
          return 'Left';
        case 0x0007004f:
          return 'Right';
      }
    }

    // 处理 LogicalKeyboardKey
    if (key == LogicalKeyboardKey.f1) return 'F1';
    if (key == LogicalKeyboardKey.f2) return 'F2';
    if (key == LogicalKeyboardKey.f3) return 'F3';
    if (key == LogicalKeyboardKey.f4) return 'F4';
    if (key == LogicalKeyboardKey.f5) return 'F5';
    if (key == LogicalKeyboardKey.f6) return 'F6';
    if (key == LogicalKeyboardKey.f7) return 'F7';
    if (key == LogicalKeyboardKey.f8) return 'F8';
    if (key == LogicalKeyboardKey.f9) return 'F9';
    if (key == LogicalKeyboardKey.f10) return 'F10';
    if (key == LogicalKeyboardKey.f11) return 'F11';
    if (key == LogicalKeyboardKey.f12) return 'F12';
    if (key == LogicalKeyboardKey.digit0) return '0';
    if (key == LogicalKeyboardKey.digit1) return '1';
    if (key == LogicalKeyboardKey.digit2) return '2';
    if (key == LogicalKeyboardKey.digit3) return '3';
    if (key == LogicalKeyboardKey.digit4) return '4';
    if (key == LogicalKeyboardKey.digit5) return '5';
    if (key == LogicalKeyboardKey.digit6) return '6';
    if (key == LogicalKeyboardKey.digit7) return '7';
    if (key == LogicalKeyboardKey.digit8) return '8';
    if (key == LogicalKeyboardKey.digit9) return '9';
    if (key == LogicalKeyboardKey.keyA) return 'A';
    if (key == LogicalKeyboardKey.keyB) return 'B';
    if (key == LogicalKeyboardKey.keyC) return 'C';
    if (key == LogicalKeyboardKey.keyD) return 'D';
    if (key == LogicalKeyboardKey.keyE) return 'E';
    if (key == LogicalKeyboardKey.keyF) return 'F';
    if (key == LogicalKeyboardKey.keyG) return 'G';
    if (key == LogicalKeyboardKey.keyH) return 'H';
    if (key == LogicalKeyboardKey.keyI) return 'I';
    if (key == LogicalKeyboardKey.keyJ) return 'J';
    if (key == LogicalKeyboardKey.keyK) return 'K';
    if (key == LogicalKeyboardKey.keyL) return 'L';
    if (key == LogicalKeyboardKey.keyM) return 'M';
    if (key == LogicalKeyboardKey.keyN) return 'N';
    if (key == LogicalKeyboardKey.keyO) return 'O';
    if (key == LogicalKeyboardKey.keyP) return 'P';
    if (key == LogicalKeyboardKey.keyQ) return 'Q';
    if (key == LogicalKeyboardKey.keyR) return 'R';
    if (key == LogicalKeyboardKey.keyS) return 'S';
    if (key == LogicalKeyboardKey.keyT) return 'T';
    if (key == LogicalKeyboardKey.keyU) return 'U';
    if (key == LogicalKeyboardKey.keyV) return 'V';
    if (key == LogicalKeyboardKey.keyW) return 'W';
    if (key == LogicalKeyboardKey.keyX) return 'X';
    if (key == LogicalKeyboardKey.keyY) return 'Y';
    if (key == LogicalKeyboardKey.keyZ) return 'Z';
    if (key == LogicalKeyboardKey.space) return 'Space';
    if (key == LogicalKeyboardKey.enter) return 'Enter';
    if (key == LogicalKeyboardKey.escape) return 'Esc';
    if (key == LogicalKeyboardKey.tab) return 'Tab';
    if (key == LogicalKeyboardKey.backspace) return 'Backspace';
    if (key == LogicalKeyboardKey.delete) return 'Delete';
    if (key == LogicalKeyboardKey.home) return 'Home';
    if (key == LogicalKeyboardKey.end) return 'End';
    if (key == LogicalKeyboardKey.pageUp) return 'PageUp';
    if (key == LogicalKeyboardKey.pageDown) return 'PageDown';
    if (key == LogicalKeyboardKey.arrowUp) return 'Up';
    if (key == LogicalKeyboardKey.arrowDown) return 'Down';
    if (key == LogicalKeyboardKey.arrowLeft) return 'Left';
    if (key == LogicalKeyboardKey.arrowRight) return 'Right';

    return null;
  }

  bool _isModifierKey(KeyboardKey? key) {
    if (key == null) return true;

    // 检查 LogicalKeyboardKey 修饰键
    if (key == LogicalKeyboardKey.controlLeft ||
        key == LogicalKeyboardKey.controlRight ||
        key == LogicalKeyboardKey.shiftLeft ||
        key == LogicalKeyboardKey.shiftRight ||
        key == LogicalKeyboardKey.altLeft ||
        key == LogicalKeyboardKey.altRight ||
        key == LogicalKeyboardKey.metaLeft ||
        key == LogicalKeyboardKey.metaRight) {
      return true;
    }

    // 检查 PhysicalKeyboardKey 修饰键（通过 USB HID 码）
    if (key is PhysicalKeyboardKey) {
      final usbCode = key.usbHidUsage;
      // 0x000700e0-0x000700e7 是修饰键的 USB HID 码
      if (usbCode >= 0x000700e0 && usbCode <= 0x000700e7) {
        return true;
      }
    }

    return false;
  }

  // 开始录制切换代理快捷键
  Future<void> _startRecordingProxy() async {
    await HotkeyService.instance.unregisterHotkeys();
    setState(() {
      _isRecordingProxy = true;
      _isRecordingTun = false;
      _isRecordingShowWindow = false;
      _isRecordingExitApp = false;
    });
  }

  // 开始录制切换 TUN 快捷键
  Future<void> _startRecordingTun() async {
    await HotkeyService.instance.unregisterHotkeys();
    setState(() {
      _isRecordingTun = true;
      _isRecordingProxy = false;
      _isRecordingShowWindow = false;
      _isRecordingExitApp = false;
    });
  }

  // 开始录制显示/隐藏窗口快捷键
  Future<void> _startRecordingShowWindow() async {
    await HotkeyService.instance.unregisterHotkeys();
    setState(() {
      _isRecordingShowWindow = true;
      _isRecordingProxy = false;
      _isRecordingTun = false;
      _isRecordingExitApp = false;
    });
  }

  // 开始录制退出应用快捷键
  Future<void> _startRecordingExitApp() async {
    await HotkeyService.instance.unregisterHotkeys();
    setState(() {
      _isRecordingExitApp = true;
      _isRecordingProxy = false;
      _isRecordingTun = false;
      _isRecordingShowWindow = false;
    });
  }

  // 处理切换代理快捷键录制完成
  Future<void> _onProxyHotkeyRecorded(HotKey hotKey) async {
    Logger.debug(
      '录制到快捷键 - modifiers: ${hotKey.modifiers}, key: ${hotKey.key}, keyLabel: ${hotKey.key.keyLabel}',
    );

    // 检查是否只按了修饰键（没有实际按键）
    if (_isModifierKey(hotKey.key)) {
      Logger.debug('忽略：只按了修饰键，没有实际按键');
      return;
    }

    final hotkeyStr = _hotKeyToString(hotKey);
    Logger.debug('转换后的快捷键字符串: $hotkeyStr');

    if (hotkeyStr.isEmpty) {
      Logger.debug('忽略：快捷键字符串为空');
      return;
    }

    setState(() {
      _isRecordingProxy = false;
      _toggleProxyHotkey = hotkeyStr;
    });

    try {
      final success = await HotkeyService.instance.setToggleProxyHotkey(
        hotkeyStr,
      );
      if (success && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(context.translate.behavior.hotkey_set_success),
          ),
        );
      }
    } catch (e) {
      Logger.error('设置切换代理快捷键失败: $e');
      if (mounted) {
        setState(() {
          _toggleProxyHotkey = null;
        });
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(context.translate.behavior.hotkey_set_failed)),
        );
      }
    }
  }

  // 处理切换 TUN 快捷键录制完成
  Future<void> _onTunHotkeyRecorded(HotKey hotKey) async {
    if (_isModifierKey(hotKey.key)) {
      return;
    }

    final hotkeyStr = _hotKeyToString(hotKey);
    if (hotkeyStr.isEmpty) {
      return;
    }

    setState(() {
      _isRecordingTun = false;
      _toggleTunHotkey = hotkeyStr;
    });

    try {
      final success = await HotkeyService.instance.setToggleTunHotkey(
        hotkeyStr,
      );
      if (success && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(context.translate.behavior.hotkey_set_success),
          ),
        );
      }
    } catch (e) {
      Logger.error('设置切换 TUN 快捷键失败: $e');
      if (mounted) {
        setState(() {
          _toggleTunHotkey = null;
        });
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(context.translate.behavior.hotkey_set_failed)),
        );
      }
    }
  }

  // 处理显示/隐藏窗口快捷键录制完成
  Future<void> _onShowWindowHotkeyRecorded(HotKey hotKey) async {
    if (_isModifierKey(hotKey.key)) {
      return;
    }

    final hotkeyStr = _hotKeyToString(hotKey);
    if (hotkeyStr.isEmpty) {
      return;
    }

    setState(() {
      _isRecordingShowWindow = false;
      _showWindowHotkey = hotkeyStr;
    });

    try {
      final success = await HotkeyService.instance.setShowWindowHotkey(
        hotkeyStr,
      );
      if (success && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(context.translate.behavior.hotkey_set_success),
          ),
        );
      }
    } catch (e) {
      Logger.error('设置显示/隐藏窗口快捷键失败: $e');
      if (mounted) {
        setState(() {
          _showWindowHotkey = null;
        });
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(context.translate.behavior.hotkey_set_failed)),
        );
      }
    }
  }

  // 处理退出应用快捷键录制完成
  Future<void> _onExitAppHotkeyRecorded(HotKey hotKey) async {
    if (_isModifierKey(hotKey.key)) {
      return;
    }

    final hotkeyStr = _hotKeyToString(hotKey);
    if (hotkeyStr.isEmpty) {
      return;
    }

    setState(() {
      _isRecordingExitApp = false;
      _exitAppHotkey = hotkeyStr;
    });

    try {
      final success = await HotkeyService.instance.setExitAppHotkey(hotkeyStr);
      if (success && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(context.translate.behavior.hotkey_set_success),
          ),
        );
      }
    } catch (e) {
      Logger.error('设置退出应用快捷键失败: $e');
      if (mounted) {
        setState(() {
          _exitAppHotkey = null;
        });
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(context.translate.behavior.hotkey_set_failed)),
        );
      }
    }
  }

  Future<void> _clearProxyHotkey() async {
    setState(() {
      _toggleProxyHotkey = null;
    });

    try {
      await HotkeyService.instance.setToggleProxyHotkey(null);
      Logger.info('切换代理快捷键已清除');
    } catch (e) {
      Logger.error('清除切换代理快捷键失败: $e');
    }
  }

  Future<void> _clearTunHotkey() async {
    setState(() {
      _toggleTunHotkey = null;
    });

    try {
      await HotkeyService.instance.setToggleTunHotkey(null);
      Logger.info('切换 TUN 快捷键已清除');
    } catch (e) {
      Logger.error('清除切换 TUN 快捷键失败: $e');
    }
  }

  Future<void> _clearShowWindowHotkey() async {
    setState(() {
      _showWindowHotkey = null;
    });

    try {
      await HotkeyService.instance.setShowWindowHotkey(null);
      Logger.info('显示/隐藏窗口快捷键已清除');
    } catch (e) {
      Logger.error('清除显示/隐藏窗口快捷键失败: $e');
    }
  }

  Future<void> _clearExitAppHotkey() async {
    setState(() {
      _exitAppHotkey = null;
    });

    try {
      await HotkeyService.instance.setExitAppHotkey(null);
      Logger.info('退出应用快捷键已清除');
    } catch (e) {
      Logger.error('清除退出应用快捷键失败: $e');
    }
  }

  Widget _buildHotkeyRow({
    required String title,
    required String description,
    required String? currentHotkey,
    required bool isRecording,
    required VoidCallback onRecord,
    required VoidCallback onClear,
    required Function(HotKey) onHotKeyRecorded,
  }) {
    return Padding(
      padding: const EdgeInsets.only(top: 12.0),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: Theme.of(context).textTheme.bodyMedium),
                const SizedBox(height: 4),
                Text(
                  description,
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: Theme.of(
                      context,
                    ).colorScheme.onSurface.withAlpha(153),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 16),
          Row(
            children: [
              if (isRecording)
                Container(
                  constraints: const BoxConstraints(minWidth: 120),
                  padding: const EdgeInsets.symmetric(
                    horizontal: 12,
                    vertical: 8,
                  ),
                  decoration: BoxDecoration(
                    border: Border.all(
                      color: Theme.of(context).colorScheme.primary,
                    ),
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: HotKeyRecorder(onHotKeyRecorded: onHotKeyRecorded),
                )
              else
                InkWell(
                  onTap: onRecord,
                  borderRadius: BorderRadius.circular(8),
                  child: Container(
                    constraints: const BoxConstraints(minWidth: 120),
                    padding: const EdgeInsets.symmetric(
                      horizontal: 12,
                      vertical: 8,
                    ),
                    decoration: BoxDecoration(
                      border: Border.all(
                        color: Theme.of(
                          context,
                        ).colorScheme.onSurface.withAlpha(76),
                      ),
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Text(
                      currentHotkey ??
                          context.translate.behavior.hotkey_not_set,
                      textAlign: TextAlign.center,
                      style: Theme.of(context).textTheme.bodyMedium,
                    ),
                  ),
                ),
              const SizedBox(width: 8),
              IconButton(
                icon: const Icon(Icons.clear, size: 20),
                onPressed: currentHotkey != null ? onClear : null,
                tooltip: context.translate.behavior.hotkey_clear,
              ),
            ],
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    if (!HotkeyService.isDesktopPlatform) {
      return const SizedBox.shrink();
    }

    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      isHoverEnabled: true,
      isTapEnabled: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              Row(
                children: [
                  const Icon(Icons.keyboard_rounded),
                  const SizedBox(
                    width: ModernFeatureCardSpacing.featureIconToTextSpacing,
                  ),
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        context.translate.behavior.hotkey_title,
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      Text(
                        context.translate.behavior.hotkey_description,
                        style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: Theme.of(
                            context,
                          ).colorScheme.onSurface.withAlpha(153),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
              ModernSwitch(value: _isEnabled, onChanged: _toggleEnabled),
            ],
          ),
          if (_isEnabled) ...[
            const SizedBox(height: 16),
            const Divider(height: 1),
            _buildHotkeyRow(
              title: context.translate.behavior.hotkey_toggle_proxy,
              description: context.translate.behavior.hotkey_toggle_proxy_desc,
              currentHotkey: _toggleProxyHotkey,
              isRecording: _isRecordingProxy,
              onRecord: _startRecordingProxy,
              onClear: _clearProxyHotkey,
              onHotKeyRecorded: _onProxyHotkeyRecorded,
            ),
            _buildHotkeyRow(
              title: context.translate.behavior.hotkey_toggle_tun,
              description: context.translate.behavior.hotkey_toggle_tun_desc,
              currentHotkey: _toggleTunHotkey,
              isRecording: _isRecordingTun,
              onRecord: _startRecordingTun,
              onClear: _clearTunHotkey,
              onHotKeyRecorded: _onTunHotkeyRecorded,
            ),
            _buildHotkeyRow(
              title: context.translate.behavior.hotkey_show_window,
              description: context.translate.behavior.hotkey_show_window_desc,
              currentHotkey: _showWindowHotkey,
              isRecording: _isRecordingShowWindow,
              onRecord: _startRecordingShowWindow,
              onClear: _clearShowWindowHotkey,
              onHotKeyRecorded: _onShowWindowHotkeyRecorded,
            ),
            _buildHotkeyRow(
              title: context.translate.behavior.hotkey_exit_app,
              description: context.translate.behavior.hotkey_exit_app_desc,
              currentHotkey: _exitAppHotkey,
              isRecording: _isRecordingExitApp,
              onRecord: _startRecordingExitApp,
              onClear: _clearExitAppHotkey,
              onHotKeyRecorded: _onExitAppHotkeyRecorded,
            ),
          ],
        ],
      ),
    );
  }
}
