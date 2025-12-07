import 'package:stelliberty/clash/utils/system_proxy.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/utils/logger.dart';

// Clash 系统代理管理器
// 负责系统代理的启用和禁用
class SystemProxyManager {
  final bool Function() _isCoreRunning;
  final int Function() _getHttpPort;
  final Function() _notifyListeners;

  bool _systemProxyEnabled = false;
  bool get isSystemProxyEnabled => _systemProxyEnabled;

  // 标记当前实例是否真正启用过系统代理（用于多实例场景）
  bool _hasEnabledSystemProxy = false;

  SystemProxyManager({
    required bool Function() isCoreRunning,
    required int Function() getHttpPort,
    required Function() notifyListeners,
  }) : _isCoreRunning = isCoreRunning,
       _getHttpPort = getHttpPort,
       _notifyListeners = notifyListeners;

  // 更新系统代理设置（使用当前配置）
  Future<void> updateSystemProxy() async {
    if (!_isCoreRunning()) {
      Logger.debug('Clash 未运行，跳过系统代理更新');
      return;
    }

    try {
      final prefs = ClashPreferences.instance;
      final proxyHost = prefs.getProxyHost();
      final usePacMode = prefs.getSystemProxyPacMode();
      final bypassRules = prefs.getCurrentBypassRules();
      final bypassList = SystemProxy.parseBypassRules(bypassRules);
      final pacScript = prefs.getSystemProxyPacScript();

      await SystemProxy.disable();
      await Future.delayed(const Duration(milliseconds: 100));

      await SystemProxy.enable(
        host: proxyHost,
        port: _getHttpPort(),
        bypassDomains: bypassList,
        usePacMode: usePacMode,
        pacScript: pacScript,
      );

      _hasEnabledSystemProxy = true;
      if (usePacMode) {
        Logger.info('系统代理已更新 (PAC 模式)');
      } else {
        Logger.info('系统代理已更新：$proxyHost:${_getHttpPort()}');
      }
    } catch (e) {
      Logger.error('更新系统代理失败：$e');
    }
  }

  // 启用系统代理（仅代理，核心已运行）
  Future<bool> enableSystemProxy() async {
    if (!_isCoreRunning()) {
      Logger.error('Clash 未运行，无法启用系统代理');
      return false;
    }

    try {
      final prefs = ClashPreferences.instance;
      final proxyHost = prefs.getProxyHost();
      final usePacMode = prefs.getSystemProxyPacMode();
      final bypassRules = prefs.getCurrentBypassRules();
      final bypassList = SystemProxy.parseBypassRules(bypassRules);
      final pacScript = prefs.getSystemProxyPacScript();

      await SystemProxy.enable(
        host: proxyHost,
        port: _getHttpPort(),
        bypassDomains: bypassList,
        usePacMode: usePacMode,
        pacScript: pacScript,
      );

      _systemProxyEnabled = true;
      _hasEnabledSystemProxy = true;
      _notifyListeners();
      return true;
    } catch (e) {
      Logger.error('启用系统代理失败：$e');
      return false;
    }
  }

  // 禁用系统代理（仅代理，核心继续运行）
  Future<bool> disableSystemProxy() async {
    // 如果当前实例从未启用过系统代理，跳过禁用操作（多实例场景保护）
    if (!_hasEnabledSystemProxy) {
      Logger.debug('当前实例未启用过系统代理，跳过禁用操作');
      if (_systemProxyEnabled) {
        _systemProxyEnabled = false;
        _notifyListeners();
      }
      return true;
    }

    try {
      await SystemProxy.disable();
      _systemProxyEnabled = false;
      _hasEnabledSystemProxy = false;
      _notifyListeners();
      return true;
    } catch (e) {
      Logger.error('禁用系统代理失败：$e');
      return false;
    }
  }
}
