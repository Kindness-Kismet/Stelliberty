import 'package:stelliberty/ui/constants/spacing.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/providers/app_update_provider.dart';
import 'package:stelliberty/storage/preferences.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/common/modern_dropdown_button.dart';
import 'package:stelliberty/ui/common/modern_dropdown_menu.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/ui/widgets/app_update_dialog.dart';
import 'package:stelliberty/services/log_print_service.dart';

class AppUpdateSettingsPage extends StatefulWidget {
  const AppUpdateSettingsPage({super.key});

  @override
  State<AppUpdateSettingsPage> createState() => _AppUpdateSettingsPageState();
}

class _AppUpdateSettingsPageState extends State<AppUpdateSettingsPage> {
  final _scrollController = ScrollController();
  final _releaseNotesScrollController = ScrollController();

  // 直接从 AppPreferences 获取初始值
  bool _autoUpdate = AppPreferences.instance.getAppAutoUpdate();
  String _checkInterval = AppPreferences.instance.getAppUpdateInterval();

  @override
  void initState() {
    super.initState();
    Logger.info('初始化 AppUpdateSettingsPage');
  }

  @override
  void dispose() {
    _scrollController.dispose();
    _releaseNotesScrollController.dispose();
    super.dispose();
  }

  Future<void> _saveAutoUpdate(bool value) async {
    setState(() {
      _autoUpdate = value;
    });
    await AppPreferences.instance.setAppAutoUpdate(value);
    Logger.info('自动更新设置已更新: $value');

    // 通知 Provider 重新调度
    if (mounted) {
      final updateProvider = context.read<AppUpdateProvider>();
      await updateProvider.restartSchedule();
    }
  }

  Future<void> _saveCheckInterval(String value) async {
    setState(() {
      _checkInterval = value;
    });
    await AppPreferences.instance.setAppUpdateInterval(value);

    // 获取对应的显示文本
    final intervalText = _getIntervalDisplayText(value);
    Logger.info('更新检测时间已设置为: $intervalText');

    // 通知 Provider 重新调度
    if (mounted) {
      final updateProvider = context.read<AppUpdateProvider>();
      await updateProvider.restartSchedule();
    }
  }

  String _getIntervalDisplayText(String interval) {
    final trans = context.translate;

    switch (interval) {
      case 'startup':
        return trans.app_update.interval_on_startup;
      case '1day':
        return trans.app_update.interval_1_day;
      case '7days':
        return trans.app_update.interval_7_days;
      case '14days':
        return trans.app_update.interval_14_days;
      default:
        return interval;
    }
  }

  Future<void> _checkForUpdate() async {
    final trans = context.translate;
    final updateProvider = context.read<AppUpdateProvider>();

    // 手动检查更新（不会触发全局监听器）
    final updateInfo = await updateProvider.checkForUpdate();

    if (!mounted) return;

    if (updateInfo != null) {
      if (updateInfo.hasUpdate) {
        // 直接显示对话框，手动检查不会设置 Provider 的 latestUpdateInfo
        await AppUpdateDialog.show(context, updateInfo);
      } else {
        // 已是最新版本
        ModernToast.success(trans.app_update.up_to_date);
      }
    } else {
      // 检查失败
      ModernToast.error(trans.app_update.network_error);
    }
  }

  @override
  Widget build(BuildContext context) {
    final provider = Provider.of<ContentProvider>(context, listen: false);
    final trans = context.translate;

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
                onPressed: () =>
                    provider.switchView(ContentView.settingsOverview),
              ),
              const SizedBox(width: 8),
              Text(
                trans.app_update.settings,
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
                  // 自动更新开关（带检查更新按钮）
                  _buildAutoUpdateCard(),
                  const SizedBox(height: 16),

                  // 更新检测时间选择
                  _buildIntervalCard(),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }

  // 构建自动更新卡片（带检查更新按钮）
  Widget _buildAutoUpdateCard() {
    return Consumer<AppUpdateProvider>(
      builder: (context, updateProvider, child) {
        final trans = context.translate;
        final isChecking = updateProvider.isChecking;

        return ModernFeatureLayoutCard(
          icon: Icons.new_releases_outlined,
          title: trans.app_update.auto_update_title,
          subtitle: trans.app_update.auto_update_description,
          trailingLeadingButton: IconButton(
            icon: isChecking
                ? SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Theme.of(context).colorScheme.primary,
                    ),
                  )
                : const Icon(Icons.refresh, size: 20),
            tooltip: trans.app_update.check_now,
            onPressed: isChecking ? null : _checkForUpdate,
          ),
          trailing: ModernSwitch(
            value: _autoUpdate,
            onChanged: _saveAutoUpdate,
          ),
          isHoverEnabled: true,
          isTapEnabled: false,
        );
      },
    );
  }

  // 构建更新间隔选择卡片（使用自定义下拉菜单）
  Widget _buildIntervalCard() {
    final trans = context.translate;

    return ModernFeatureLayoutCard(
      icon: Icons.schedule_outlined,
      title: trans.app_update.check_interval_title,
      subtitle: trans.app_update.check_interval_description,
      trailing: _buildIntervalDropdown(),
      isHoverEnabled: true,
      isTapEnabled: false,
    );
  }

  // 构建间隔下拉菜单
  Widget _buildIntervalDropdown() {
    final intervals = ['startup', '1day', '7days', '14days'];

    return ModernDropdownMenu<String>(
      items: intervals,
      selectedItem: _checkInterval,
      itemToString: (interval) => _getIntervalDisplayText(interval),
      onSelected: (value) {
        _saveCheckInterval(value);
      },
      child: _IntervalDropdownButton(
        text: _getIntervalDisplayText(_checkInterval),
      ),
    );
  }
}

// 间隔下拉按钮组件（带悬停状态）
class _IntervalDropdownButton extends StatefulWidget {
  final String text;

  const _IntervalDropdownButton({required this.text});

  @override
  State<_IntervalDropdownButton> createState() =>
      _IntervalDropdownButtonState();
}

class _IntervalDropdownButtonState extends State<_IntervalDropdownButton> {
  bool _isHovering = false;

  @override
  Widget build(BuildContext context) {
    return MouseRegion(
      onEnter: (_) => setState(() => _isHovering = true),
      onExit: (_) => setState(() => _isHovering = false),
      child: CustomDropdownButton(text: widget.text, isHovering: _isHovering),
    );
  }
}
