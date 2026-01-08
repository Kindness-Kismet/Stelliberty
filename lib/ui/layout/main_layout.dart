import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/atomic/platform_helper.dart';
import 'package:stelliberty/ui/widgets/content_body.dart';
import 'package:stelliberty/ui/pages/settings/appearance_settings_page.dart';
import 'package:stelliberty/ui/pages/settings/language_settings_page.dart';
import 'package:stelliberty/ui/pages/settings/settings_overview_page.dart';
import 'package:stelliberty/ui/pages/settings/clash_features_page.dart';
import 'package:stelliberty/ui/pages/settings/backup_settings_page.dart';
import 'package:stelliberty/ui/pages/settings/app_update_settings_page.dart';
import 'package:stelliberty/ui/pages/settings/clash/network_settings_page.dart';
import 'package:stelliberty/ui/pages/settings/clash/port_control_page.dart';
import 'package:stelliberty/ui/pages/settings/clash/system_integration_page.dart';
import 'package:stelliberty/ui/pages/settings/clash/dns_config_page.dart';
import 'package:stelliberty/ui/pages/settings/clash/performance_page.dart';
import 'package:stelliberty/ui/pages/settings/clash/logs_debug_page.dart';
import 'package:stelliberty/ui/pages/proxy_page.dart';
import 'package:stelliberty/ui/pages/subscription_page.dart';
import 'package:stelliberty/ui/pages/override_page.dart';
import 'package:stelliberty/ui/pages/home_page.dart';
import 'package:stelliberty/ui/pages/connection_page.dart';
import 'package:stelliberty/ui/pages/core_log_page.dart';

import 'sidebar.dart';
import 'mobile_bottom_nav.dart';

// 主页面，根据平台选择不同布局
// 桌面端：侧边栏 + 内容区
// 移动端：底部导航栏 + 内容区
class HomePage extends StatelessWidget {
  const HomePage({super.key});

  @override
  Widget build(BuildContext context) {
    if (PlatformHelper.isMobile) {
      // 移动端布局：底部导航栏 + SafeArea 处理状态栏
      return Scaffold(
        body: SafeArea(child: const _DynamicContentArea()),
        bottomNavigationBar: const MobileBottomNav(),
      );
    } else {
      // 桌面端布局：侧边栏
      return const Row(
        children: [
          HomeSidebar(),
          VerticalDivider(width: 2, thickness: 2),
          Expanded(child: _DynamicContentArea()),
        ],
      );
    }
  }
}

// 根据 ContentProvider 的状态动态构建右侧内容区域
class _DynamicContentArea extends StatelessWidget {
  const _DynamicContentArea();

  @override
  Widget build(BuildContext context) {
    return Consumer<ContentProvider>(
      builder: (context, provider, child) {
        return ContentBody(
          child: AnimatedSwitcher(
            duration: const Duration(milliseconds: 300),
            switchInCurve: Curves.easeInOut,
            switchOutCurve: Curves.easeInOut,
            transitionBuilder: (child, animation) {
              // 使用纯淡入淡出，不带位置变化
              return FadeTransition(opacity: animation, child: child);
            },
            layoutBuilder: (currentChild, previousChildren) {
              // 关键修复：只显示当前页面，忽略旧页面的布局影响
              // 这样避免了 Stack 层叠导致的位置计算问题
              return currentChild ?? const SizedBox.shrink();
            },
            child: _buildContent(provider.currentView),
          ),
        );
      },
    );
  }

  Widget _buildContent(ContentView view) {
    // 为每个页面分配唯一 key，确保 AnimatedSwitcher 能正确识别页面切换
    // 这样旧页面会立即 dispose，避免内存泄漏
    switch (view) {
      case ContentView.home:
        return const HomePageContent(key: ValueKey('home'));
      case ContentView.proxy:
        return const ProxyPage(key: ValueKey('proxy'));
      case ContentView.connections:
        return const ConnectionPageContent(key: ValueKey('connections'));
      case ContentView.subscriptions:
        return const SubscriptionPage(key: ValueKey('subscriptions'));
      case ContentView.overrides:
        return const OverridePage(key: ValueKey('overrides'));
      case ContentView.logs:
        return const LogPage(key: ValueKey('logs'));
      case ContentView.settingsOverview:
        return const SettingsOverviewPage(key: ValueKey('settings_overview'));
      case ContentView.settingsAppearance:
        return const AppearanceSettingsPage(
          key: ValueKey('settings_appearance'),
        );
      case ContentView.settingsBehavior:
        return const BehaviorSettingsPage(key: ValueKey('settings_behavior'));
      case ContentView.settingsLanguage:
        return const LanguageSettingsPage(key: ValueKey('settings_language'));
      case ContentView.settingsClashFeatures:
        return const ClashFeaturesPage(
          key: ValueKey('settings_clash_features'),
        );
      case ContentView.settingsClashNetworkSettings:
        return const NetworkSettingsPage(
          key: ValueKey('settings_clash_network'),
        );
      case ContentView.settingsClashPortControl:
        return const PortControlPage(key: ValueKey('settings_clash_port'));
      case ContentView.settingsClashSystemIntegration:
        return const SystemIntegrationPage(
          key: ValueKey('settings_clash_system'),
        );
      case ContentView.settingsClashDnsConfig:
        return const DnsConfigPage(key: ValueKey('settings_clash_dns'));
      case ContentView.settingsClashPerformance:
        return const PerformancePage(
          key: ValueKey('settings_clash_performance'),
        );
      case ContentView.settingsClashLogsDebug:
        return const LogsDebugPage(key: ValueKey('settings_clash_logs'));
      case ContentView.settingsBackup:
        return const BackupSettingsPage(key: ValueKey('settings_backup'));
      case ContentView.settingsAppUpdate:
        return const AppUpdateSettingsPage(
          key: ValueKey('settings_app_update'),
        );
    }
  }
}
