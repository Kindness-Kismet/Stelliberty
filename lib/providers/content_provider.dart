import 'package:flutter/material.dart';

// 定义右侧内容区域可显示的视图类型
enum ContentView {
  // 主页相关视图
  home,
  proxy,
  connections,
  logs,
  rules,
  subscriptions,
  overrides,

  // 设置相关视图
  settingsOverview,
  settingsAppearance,
  settingsLanguage,
  settingsClashFeatures,
  settingsBehavior,
  settingsBackup,
  settingsAppUpdate,
  settingsAccessControl,

  // Clash 特性子页面（命名以 settings 开头保持侧边栏选中状态）
  settingsClashNetworkSettings,
  settingsClashPortControl,
  settingsClashSystemIntegration,
  settingsClashDnsConfig,
  settingsClashPerformance,
  settingsClashLogsDebug,
}

// 管理右侧内容区域的视图切换
class ContentProvider extends ChangeNotifier {
  ContentView _currentView = ContentView.home;
  DateTime? _lastSwitchTime;
  static const _switchDebounceMs = 200;

  ContentView get currentView => _currentView;

  bool get _canSwitch {
    if (_lastSwitchTime == null) return true;
    return DateTime.now().difference(_lastSwitchTime!).inMilliseconds >=
        _switchDebounceMs;
  }

  // 切换视图，带防抖
  void switchView(ContentView nextView) {
    if (_currentView == nextView || !_canSwitch) return;
    _currentView = nextView;
    _lastSwitchTime = DateTime.now();
    notifyListeners();
  }

  // 处理返回操作，返回 true 表示已处理
  bool handleBack() {
    final parentView = _getParentView(_currentView);
    if (parentView == null) return false;
    switchView(parentView);
    return true;
  }

  // 获取父视图（用于返回导航）
  ContentView? _getParentView(ContentView view) => switch (view) {
    // 首页无父视图
    ContentView.home => null,

    // 底部导航一级页面 → 首页
    ContentView.proxy ||
    ContentView.subscriptions ||
    ContentView.settingsOverview => ContentView.home,

    // 订阅子页面 → 订阅页
    ContentView.overrides || ContentView.rules => ContentView.subscriptions,

    // 代理子页面 → 代理页
    ContentView.connections => ContentView.proxy,

    // 设置子页面 → 设置概览
    ContentView.settingsAppearance ||
    ContentView.settingsLanguage ||
    ContentView.settingsClashFeatures ||
    ContentView.settingsBehavior ||
    ContentView.settingsBackup ||
    ContentView.settingsAppUpdate ||
    ContentView.settingsAccessControl ||
    ContentView.logs => ContentView.settingsOverview,

    // Clash 特性子页面 → Clash 特性页
    ContentView.settingsClashNetworkSettings ||
    ContentView.settingsClashPortControl ||
    ContentView.settingsClashSystemIntegration ||
    ContentView.settingsClashDnsConfig ||
    ContentView.settingsClashPerformance ||
    ContentView.settingsClashLogsDebug => ContentView.settingsClashFeatures,
  };
}
