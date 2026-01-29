import 'package:stelliberty/ui/constants/spacing.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/atomic/platform_helper.dart';
import 'package:stelliberty/clash/providers/behavior_settings_provider.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/ui/widgets/setting/lazy_mode_card.dart';
import 'package:stelliberty/ui/widgets/setting/hotkey_settings_card.dart';

// 应用行为设置页面
class BehaviorSettingsPage extends StatefulWidget {
  const BehaviorSettingsPage({super.key});

  @override
  State<BehaviorSettingsPage> createState() => _BehaviorSettingsPageState();
}

class _BehaviorSettingsPageState extends State<BehaviorSettingsPage> {
  final _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    Logger.info('初始化 BehaviorSettingsPage');
  }

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final contentProvider = Provider.of<ContentProvider>(
      context,
      listen: false,
    );
    final trans = context.translate;

    return Consumer<BehaviorSettingsProvider>(
      builder: (context, provider, _) {
        if (provider.isLoading) {
          return const Center(child: CircularProgressIndicator());
        }

        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // 自定义标题栏
            Padding(
              padding: const EdgeInsets.all(8.0),
              child: Row(
                children: [
                  IconButton(
                    icon: const Icon(Icons.arrow_back),
                    onPressed: () => contentProvider.switchView(
                      ContentView.settingsOverview,
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(
                    trans.behavior.title,
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                ],
              ),
            ),
            // 可滚动内容
            Expanded(
              child: Padding(
                padding: SpacingConstants.scrollbarPadding,
                child: SingleChildScrollView(
                  controller: _scrollController,
                  physics: const BouncingScrollPhysics(),
                  padding: const EdgeInsets.fromLTRB(
                    32,
                    16,
                    32 - SpacingConstants.scrollbarRightCompensation,
                    16,
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      // 开机自启动卡片
                      _buildSwitchCard(
                        icon: Icons.power_settings_new_outlined,
                        title: trans.behavior.auto_start_title,
                        subtitle: trans.behavior.auto_start_description,
                        value: provider.autoStartEnabled,
                        onChanged: provider.updateAutoStart,
                      ),

                      // 静默启动卡片（仅桌面端）
                      if (PlatformHelper.isDesktop) ...[
                        const SizedBox(height: 16),
                        _buildSwitchCard(
                          icon: Icons.visibility_off_outlined,
                          title: trans.behavior.silent_start_title,
                          subtitle: trans.behavior.silent_start_description,
                          value: provider.silentStartEnabled,
                          onChanged: provider.updateSilentStart,
                        ),
                      ],

                      // 最小化到托盘卡片（仅桌面端）
                      if (PlatformHelper.isDesktop) ...[
                        const SizedBox(height: 16),
                        _buildSwitchCard(
                          icon: Icons.remove_circle_outline,
                          title: trans.behavior.minimize_to_tray_title,
                          subtitle: trans.behavior.minimize_to_tray_description,
                          value: provider.minimizeToTray,
                          onChanged: provider.updateMinimizeToTray,
                        ),
                      ],

                      const SizedBox(height: 16),

                      // 应用日志卡片
                      _buildSwitchCard(
                        icon: Icons.description_outlined,
                        title: trans.behavior.app_log_title,
                        subtitle: trans.behavior.app_log_description,
                        value: provider.appLogEnabled,
                        onChanged: provider.updateAppLog,
                      ),

                      const SizedBox(height: 16),

                      // 懒惰模式卡片
                      const LazyModeCard(),

                      const SizedBox(height: 16),

                      // 全局快捷键卡片
                      const HotkeySettingsCard(),
                    ],
                  ),
                ),
              ),
            ),
          ],
        );
      },
    );
  }

  // 构建带开关的卡片
  Widget _buildSwitchCard({
    required IconData icon,
    required String title,
    required String subtitle,
    required bool value,
    required ValueChanged<bool> onChanged,
  }) {
    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      isHoverEnabled: true,
      isTapEnabled: false,
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          // 左侧图标和标题
          Row(
            children: [
              Icon(icon),
              const SizedBox(
                width: ModernFeatureCardSpacing.featureIconToTextSpacing,
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: Theme.of(context).textTheme.titleMedium),
                  Text(
                    subtitle,
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
          // 右侧开关
          ModernSwitch(value: value, onChanged: onChanged),
        ],
      ),
    );
  }
}
