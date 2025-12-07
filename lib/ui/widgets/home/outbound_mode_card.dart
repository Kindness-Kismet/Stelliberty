import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/tray/tray_manager.dart';
import 'package:stelliberty/ui/widgets/home/base_card.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 出站模式卡片
//
// 提供规则模式、全局模式、直连模式切换
class OutboundModeCard extends StatefulWidget {
  const OutboundModeCard({super.key});

  @override
  State<OutboundModeCard> createState() => _OutboundModeCardState();
}

class _OutboundModeCardState extends State<OutboundModeCard> {
  String _selectedOutboundMode = 'rule';

  @override
  void initState() {
    super.initState();
    _loadCurrentMode();
    // 监听 ClashManager 状态变化
    ClashManager.instance.addListener(_onClashManagerChanged);
  }

  @override
  void dispose() {
    // 移除监听器，防止内存泄漏
    ClashManager.instance.removeListener(_onClashManagerChanged);
    super.dispose();
  }

  // ClashManager 状态变化回调
  void _onClashManagerChanged() {
    if (mounted) {
      final currentOutboundMode = ClashManager.instance.outboundMode;
      if (_selectedOutboundMode != currentOutboundMode) {
        setState(() {
          _selectedOutboundMode = currentOutboundMode;
        });
        Logger.debug('主页出站模式卡片已同步到: $currentOutboundMode');
      }
    }
  }

  Future<void> _loadCurrentMode() async {
    try {
      final outboundMode = ClashManager.instance.outboundMode;
      if (mounted) {
        setState(() {
          _selectedOutboundMode = outboundMode;
        });
      }
    } catch (e) {
      Logger.warning('获取当前模式失败: $e，使用默认值');
      if (mounted) {
        setState(() {
          _selectedOutboundMode = 'rule';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    // 使用 Selector 只监听 isRunning，避免 isLoadingProxies 变化导致的重建
    return Selector<ClashProvider, bool>(
      selector: (_, provider) => provider.isCoreRunning,
      builder: (context, isRunning, child) {
        return BaseCard(
          icon: Icons.alt_route_rounded,
          title: context.translate.proxy.outboundMode,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _buildModeOption(
                context,
                icon: Icons.rule_rounded,
                title: context.translate.proxy.ruleMode,
                outboundMode: 'rule',
                isRunning: isRunning,
              ),

              const SizedBox(height: 8),

              _buildModeOption(
                context,
                icon: Icons.public_rounded,
                title: context.translate.proxy.globalMode,
                outboundMode: 'global',
                isRunning: isRunning,
              ),

              const SizedBox(height: 8),

              _buildModeOption(
                context,
                icon: Icons.phonelink_rounded,
                title: context.translate.proxy.directMode,
                outboundMode: 'direct',
                isRunning: isRunning,
              ),
            ],
          ),
        );
      },
    );
  }

  Widget _buildModeOption(
    BuildContext context, {
    required IconData icon,
    required String title,
    required String outboundMode,
    required bool isRunning,
  }) {
    final colorScheme = Theme.of(context).colorScheme;
    final isSelected = _selectedOutboundMode == outboundMode;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: !isSelected
            ? () => _switchOutboundMode(context, outboundMode, isRunning)
            : null,
        borderRadius: BorderRadius.circular(12),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 200),
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
          decoration: BoxDecoration(
            color: isSelected
                ? colorScheme.primaryContainer.withValues(alpha: 0.6)
                : colorScheme.surfaceContainerHighest.withValues(alpha: 0.2),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(
              color: isSelected
                  ? colorScheme.primary
                  : colorScheme.primary.withValues(alpha: 0.1),
              width: isSelected ? 2 : 1,
            ),
          ),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  title,
                  style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    fontWeight: isSelected ? FontWeight.w600 : FontWeight.w500,
                    color: isSelected
                        ? colorScheme.primary
                        : colorScheme.onSurface.withValues(alpha: 0.7),
                    fontSize: 14,
                  ),
                ),
              ),
              if (isSelected)
                Icon(Icons.check_circle, color: colorScheme.primary, size: 18),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _switchOutboundMode(
    BuildContext context,
    String outboundMode,
    bool isRunning,
  ) async {
    Logger.info('用户切换出站模式: $outboundMode (核心运行: $isRunning)');

    setState(() {
      _selectedOutboundMode = outboundMode;
    });

    try {
      final success = await ClashManager.instance.setOutboundMode(outboundMode);

      if (context.mounted && !success) {
        await _loadCurrentMode();
      }
      // 出站模式切换后手动更新托盘菜单
      AppTrayManager().updateTrayMenuManually();
    } catch (e) {
      Logger.error('切换出站模式失败: $e');
      await _loadCurrentMode();
    }
  }
}
