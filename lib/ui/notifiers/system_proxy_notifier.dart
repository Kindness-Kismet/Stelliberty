import 'dart:async';
import 'package:flutter/material.dart';
import 'package:rinf/rinf.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/services/system_proxy_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 系统代理配置的业务逻辑
// 负责代理主机、绕过规则、PAC 脚本配置，以及网络接口管理
class SystemProxyNotifier extends ChangeNotifier {
  final ClashManager _clashManager;

  // UI 控制器
  late TextEditingController proxyHostController;
  late TextEditingController bypassController;
  late TextEditingController pacScriptController;

  // UI 状态
  bool _useDefaultBypass = true;
  bool _usePacMode = false;
  String _selectedHost = '127.0.0.1';
  bool _isLoading = true;

  // 网络接口状态
  List<String> _availableHosts = ['127.0.0.1', 'localhost'];
  bool _isNetworkInitialized = false;
  StreamSubscription<RustSignalPack<NetworkInterfacesInfo>>?
  _networkSubscription;

  SystemProxyNotifier({ClashManager? clashManager})
    : _clashManager = clashManager ?? ClashManager.instance {
    _initialize();
  }

  // ========== Getters ==========

  bool get useDefaultBypass => _useDefaultBypass;
  bool get usePacMode => _usePacMode;
  String get selectedHost => _selectedHost;
  bool get isLoading => _isLoading;
  List<String> get availableHosts => List.unmodifiable(_availableHosts);

  // ========== 初始化 ==========

  void _initialize() {
    _loadSettings();
    _initializeNetworkInterfaces();

    // 标记加载完成
    Future.delayed(const Duration(milliseconds: 100), () {
      _isLoading = false;
      notifyListeners();
    });
  }

  // 加载系统代理配置
  void _loadSettings() {
    final prefs = ClashPreferences.instance;

    final proxyHost = prefs.getProxyHost();
    proxyHostController = TextEditingController(text: proxyHost);
    _selectedHost = proxyHost;

    _useDefaultBypass = prefs.getUseDefaultBypass();
    _usePacMode = prefs.getSystemProxyPacMode();

    bypassController = TextEditingController(
      text: _useDefaultBypass
          ? SystemProxy.getDefaultBypassRules()
          : (prefs.getSystemProxyBypass() ??
                SystemProxy.getDefaultBypassRules()),
    );

    pacScriptController = TextEditingController(
      text: prefs.getSystemProxyPacScript(),
    );

    Logger.info(
      '系统代理配置已加载: host=$proxyHost, usePac=$_usePacMode, useDefaultBypass=$_useDefaultBypass',
    );
  }

  // ========== 网络接口管理 ==========

  // 初始化网络接口
  void _initializeNetworkInterfaces() {
    if (_isNetworkInitialized) {
      Logger.warning('网络接口已初始化，跳过');
      return;
    }

    Logger.info('初始化网络接口');

    _loadCachedInterfaces();
    _subscribeToNetworkInterfaceUpdates();
    _requestNetworkInterfaceUpdate();

    _isNetworkInitialized = true;
    Logger.info('网络接口初始化完成');
  }

  // 从缓存加载网络接口
  void _loadCachedInterfaces() {
    if (NetworkInterfacesInfo.latestRustSignal == null) {
      Logger.info('没有缓存的网络接口信息');
      return;
    }

    final cached = NetworkInterfacesInfo.latestRustSignal!.message;
    Logger.info('发现缓存的网络接口信息：${cached.addresses.length} 个地址');

    _updateNetworkAddresses(cached.addresses);
    Logger.info('已从缓存更新网络接口：$_availableHosts');
  }

  // 订阅网络接口更新
  void _subscribeToNetworkInterfaceUpdates() {
    _networkSubscription = NetworkInterfacesInfo.rustSignalStream.listen((
      signal,
    ) {
      final addresses = signal.message.addresses;
      Logger.info('收到网络接口更新信号：${addresses.length} 个地址');

      _updateNetworkAddresses(addresses);
      Logger.info('网络接口列表已更新：$_availableHosts');
    });

    Logger.info('已订阅网络接口更新');
  }

  // 请求网络接口更新
  void _requestNetworkInterfaceUpdate() {
    Logger.info('请求网络接口更新');
    const GetNetworkInterfaces().sendSignalToRust();
  }

  // 更新网络地址列表（排除子网掩码）
  void _updateNetworkAddresses(List<String> addresses) {
    _availableHosts = addresses.where((address) {
      if (address.startsWith('255.')) {
        Logger.debug('过滤掉子网掩码：$address');
        return false;
      }
      return true;
    }).toList();

    // 确保至少包含默认地址
    if (!_availableHosts.contains('127.0.0.1')) {
      _availableHosts.insert(0, '127.0.0.1');
    }
    if (!_availableHosts.contains('localhost')) {
      _availableHosts.insert(1, 'localhost');
    }

    notifyListeners();
  }

  // 手动刷新网络接口
  void refreshNetworkInterfaces() {
    Logger.info('手动刷新网络接口列表');
    _requestNetworkInterfaceUpdate();
  }

  // ========== 代理主机管理 ==========

  // 选择代理主机（仅更新 UI，不立即保存）
  void selectHost(String host) {
    proxyHostController.text = host;
    _selectedHost = host;
    notifyListeners();
    Logger.info('从下拉菜单选择主机：$host');
  }

  // ========== 统一保存配置 ==========

  // 保存所有配置
  Future<void> saveConfig() async {
    final prefs = ClashPreferences.instance;

    // 保存代理主机
    await prefs.setProxyHost(proxyHostController.text);
    _selectedHost = proxyHostController.text;

    // 保存绕过规则开关
    await prefs.setUseDefaultBypass(_useDefaultBypass);

    // 保存绕过规则内容
    if (!_useDefaultBypass) {
      await prefs.setSystemProxyBypass(bypassController.text);
    }

    // 保存 PAC 模式
    await prefs.setSystemProxyPacMode(_usePacMode);

    // 保存 PAC 脚本
    if (_usePacMode) {
      await prefs.setSystemProxyPacScript(pacScriptController.text);
    }

    // 如果 Clash 正在运行，重启系统代理以应用新配置
    if (_clashManager.isCoreRunning) {
      await _clashManager.restartSystemProxy();
    }

    Logger.info('系统代理配置已保存');
  }

  // ========== 绕过规则管理 ==========

  // 切换默认绕过规则
  Future<void> toggleUseDefaultBypass(bool value) async {
    _useDefaultBypass = value;

    if (value) {
      bypassController.text = SystemProxy.getDefaultBypassRules();
    } else {
      bypassController.text =
          ClashPreferences.instance.getSystemProxyBypass() ??
          SystemProxy.getDefaultBypassRules();
    }

    notifyListeners();
    await ClashPreferences.instance.setUseDefaultBypass(value);

    // 如果 Clash 正在运行，重启系统代理以应用新配置
    if (_clashManager.isCoreRunning) {
      await _clashManager.restartSystemProxy();
    }

    Logger.info('默认绕过规则：$value');
  }

  // ========== PAC 模式管理 ==========

  // 切换 PAC 模式
  Future<void> togglePacMode(bool value) async {
    _usePacMode = value;
    notifyListeners();
    await ClashPreferences.instance.setSystemProxyPacMode(value);

    // 如果 Clash 正在运行，重启系统代理以应用新配置
    if (_clashManager.isCoreRunning) {
      await _clashManager.restartSystemProxy();
    }

    Logger.info('PAC 模式：${value ? "启用" : "禁用"}');
  }

  // 恢复默认 PAC 脚本（仅更新 UI，不立即保存）
  void restoreDefaultPacScript() {
    final prefs = ClashPreferences.instance;
    final defaultScript = prefs.getDefaultPacScript();
    pacScriptController.text = defaultScript;
    notifyListeners();
    Logger.info('恢复默认 PAC 脚本');
  }

  @override
  void dispose() {
    proxyHostController.dispose();
    bypassController.dispose();
    pacScriptController.dispose();
    _networkSubscription?.cancel();
    _networkSubscription = null;
    _isNetworkInitialized = false;
    Logger.info('销毁 SystemProxyNotifier');
    super.dispose();
  }
}
