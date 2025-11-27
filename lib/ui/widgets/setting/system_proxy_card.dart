import 'dart:io';
import 'package:flutter/material.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_dropdown_menu.dart';
import 'package:stelliberty/ui/common/modern_text_field.dart';
import 'package:stelliberty/ui/widgets/modern_multiline_text_field.dart';
import 'package:stelliberty/ui/notifiers/system_proxy_notifier.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 系统代理配置卡片
class SystemProxyCard extends StatefulWidget {
  const SystemProxyCard({super.key});

  @override
  State<SystemProxyCard> createState() => _SystemProxyCardState();
}

class _SystemProxyCardState extends State<SystemProxyCard> {
  late SystemProxyNotifier _viewModel;

  @override
  void initState() {
    super.initState();
    _viewModel = SystemProxyNotifier();
  }

  @override
  void dispose() {
    _viewModel.dispose();
    super.dispose();
  }

  String _getBypassHelperText() {
    if (Platform.isWindows) {
      return context.translate.systemProxy.bypassHelper;
    } else if (Platform.isLinux) {
      return context.translate.systemProxy.bypassHelperLinux;
    } else if (Platform.isMacOS) {
      return context.translate.systemProxy.bypassHelperMac;
    } else {
      return context.translate.systemProxy.bypassHelper;
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return ListenableBuilder(
      listenable: _viewModel,
      builder: (context, _) {
        return ModernFeatureCard(
          isSelected: false,
          onTap: () {},
          enableHover: false,
          enableTap: false,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // 标题行
                Row(
                  children: [
                    const Icon(Icons.settings_ethernet_rounded),
                    const SizedBox(width: 12),
                    Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          context.translate.systemProxy.configTitle,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ],
                    ),
                  ],
                ),

                const SizedBox(height: 16),
                const Divider(height: 1),
                const SizedBox(height: 16),

                // 骨架屏或真实内容
                if (_viewModel.isLoading)
                  _buildSkeleton(theme)
                else
                  ..._buildRealContent(theme),
              ],
            ),
          ),
        );
      },
    );
  }

  // 构建骨架屏
  Widget _buildSkeleton(ThemeData theme) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // 代理主机骨架
        Row(
          children: [
            Expanded(
              flex: 3,
              child: Container(
                height: 72,
                decoration: BoxDecoration(
                  color: theme.colorScheme.surfaceContainerHighest.withAlpha(
                    100,
                  ),
                  borderRadius: BorderRadius.circular(8),
                ),
              ),
            ),
            const SizedBox(width: 12),
            Container(
              width: 80,
              height: 36,
              decoration: BoxDecoration(
                color: theme.colorScheme.surfaceContainerHighest.withAlpha(100),
                borderRadius: BorderRadius.circular(8),
              ),
            ),
          ],
        ),
        const SizedBox(height: 16),
        // PAC 模式开关骨架
        Container(
          height: 60,
          decoration: BoxDecoration(
            color: theme.colorScheme.surfaceContainerHighest.withAlpha(100),
            borderRadius: BorderRadius.circular(8),
          ),
        ),
        const SizedBox(height: 12),
        // 绕过规则骨架
        Container(
          height: 120,
          decoration: BoxDecoration(
            color: theme.colorScheme.surfaceContainerHighest.withAlpha(100),
            borderRadius: BorderRadius.circular(8),
          ),
        ),
      ],
    );
  }

  // 构建真实内容
  List<Widget> _buildRealContent(ThemeData theme) {
    return [
      // 代理主机（输入框内嵌下拉按钮）
      ModernDropdownMenu<String>(
        items: _viewModel.availableHosts,
        selectedItem: _viewModel.selectedHost,
        onSelected: _viewModel.selectHost,
        itemToString: (host) => host,
        child: ModernTextField(
          controller: _viewModel.proxyHostController,
          labelText: context.translate.systemProxy.proxyHost,
          hintText: context.translate.systemProxy.proxyHostHint,
          helperText: context.translate.systemProxy.proxyHostHelper,
          onChanged: _viewModel.saveProxyHost,
          showDropdownIcon: true,
  
        ),
      ),
      const SizedBox(height: 16),

      // PAC 模式开关
      SwitchListTile(
        title: Text(context.translate.systemProxy.pacMode),
        subtitle: Text(context.translate.systemProxy.pacModeDesc),
        value: _viewModel.usePacMode,
        onChanged: _viewModel.togglePacMode,
        contentPadding: EdgeInsets.zero,
      ),
      const SizedBox(height: 12),

      // 根据模式显示不同的配置
      if (!_viewModel.usePacMode) ...[
        // 使用默认绕过规则开关
        SwitchListTile(
          title: Text(context.translate.systemProxy.useDefaultBypass),
          subtitle: Text(context.translate.systemProxy.useDefaultBypassDesc),
          value: _viewModel.useDefaultBypass,
          onChanged: _viewModel.toggleUseDefaultBypass,
          contentPadding: EdgeInsets.zero,
        ),
        const SizedBox(height: 12),

        // 绕过地址编辑器
        ModernMultilineTextField(
          controller: _viewModel.bypassController,
          labelText: context.translate.systemProxy.bypassLabel,
          helperText: _getBypassHelperText(),
          height: 120,
          enabled: !_viewModel.useDefaultBypass,
          contentPadding: const EdgeInsets.only(
            left: 10,
            right: 9,
            top: 2,
            bottom: 2,
          ),
          scrollbarRightPadding: 0.0,
          onChanged: _viewModel.saveBypassRules,
        ),
      ] else ...[
        // PAC 脚本标签
        Text(
          context.translate.systemProxy.pacScriptLabel,
          style: theme.textTheme.titleSmall,
        ),
        const SizedBox(height: 8),
        // PAC 脚本编辑器
        ModernMultilineTextField(
          controller: _viewModel.pacScriptController,
          helperText: context.translate.systemProxy.pacScriptHelper,
          height: 250,
          contentPadding: const EdgeInsets.only(
            left: 10,
            right: 7,
            top: 2,
            bottom: 2,
          ),
          scrollbarRightPadding: 0.0,
          onChanged: _viewModel.savePacScript,
        ),
        const SizedBox(height: 8),
        // 恢复默认按钮
        TextButton.icon(
          onPressed: _viewModel.restoreDefaultPacScript,
          icon: const Icon(Icons.restart_alt, size: 18),
          label: Text(context.translate.clashFeatures.testUrl.restoreDefault),
        ),
      ],
    ];
  }
}
