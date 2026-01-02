import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/providers/window_effect_provider.dart';

import 'package:stelliberty/i18n/i18n.dart';
import 'sidebar_item.dart';

class HomeSidebar extends StatelessWidget {
  const HomeSidebar({super.key});

  @override
  Widget build(BuildContext context) {
    final provider = Provider.of<ContentProvider>(context);
    final currentView = provider.currentView;
    final trans = context.translate;

    return Consumer<WindowEffectProvider>(
      builder: (context, windowEffectProvider, child) {
        return Container(
          width: 220,
          color: windowEffectProvider.windowEffectBackgroundColor,
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 24.0),
                child: Center(
                  child: Text(
                    trans.common.app_name,
                    style: TextStyle(
                      fontSize: 20,
                      fontWeight: FontWeight.bold,
                      color: Theme.of(context).colorScheme.primary,
                    ),
                  ),
                ),
              ),
              Expanded(
                child: ListView(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 10,
                    vertical: 10,
                  ),
                  children: [
                    HomeSidebarItem(
                      icon: Icons.home_rounded,
                      title: trans.sidebar.home,
                      isSelected: currentView == ContentView.home,
                      onTap: () => provider.switchView(ContentView.home),
                    ),
                    HomeSidebarItem(
                      icon: Icons.wifi,
                      title: trans.sidebar.proxy,
                      isSelected: currentView == ContentView.proxy,
                      onTap: () => provider.switchView(ContentView.proxy),
                    ),
                    HomeSidebarItem(
                      icon: Icons.swap_horiz_rounded,
                      title: trans.sidebar.connections,
                      isSelected: currentView == ContentView.connections,
                      onTap: () => provider.switchView(ContentView.connections),
                    ),
                    HomeSidebarItem(
                      icon: Icons.description_rounded,
                      title: trans.sidebar.logs,
                      isSelected: currentView == ContentView.logs,
                      onTap: () => provider.switchView(ContentView.logs),
                    ),
                    HomeSidebarItem(
                      icon: Icons.storage,
                      title: trans.sidebar.subscriptions,
                      isSelected:
                          currentView == ContentView.subscriptions ||
                          currentView == ContentView.overrides,
                      onTap: () =>
                          provider.switchView(ContentView.subscriptions),
                    ),
                  ],
                ),
              ),
              // 设置按钮单独放在底部
              Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 10,
                  vertical: 10,
                ),
                child: HomeSidebarItem(
                  icon: Icons.settings_rounded,
                  title: trans.common.settings,
                  isSelected: currentView.name.startsWith('settings'),
                  onTap: () =>
                      provider.switchView(ContentView.settingsOverview),
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}
