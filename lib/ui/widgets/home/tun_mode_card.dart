import 'dart:io';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/core/service_state.dart';
import 'package:stelliberty/services/permission_service.dart';
import 'package:stelliberty/tray/tray_manager.dart';
import 'package:stelliberty/ui/widgets/home/base_card.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 虚拟网卡模式控制卡片
//
// 提供 TUN 模式开关和状态显示
// 支持服务模式、管理员模式或 root 模式启动
class TunModeCard extends StatefulWidget {
  const TunModeCard({super.key});

  @override
  State<TunModeCard> createState() => _TunModeCardState();
}

class _TunModeCardState extends State<TunModeCard> {
  bool _isElevated = false;

  @override
  void initState() {
    super.initState();
    _checkElevation();
  }

  Future<void> _checkElevation() async {
    final elevated = await PermissionService.isElevated();
    if (mounted) {
      setState(() {
        _isElevated = elevated;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final serviceStateManager = context.watch<ServiceStateManager>();
    final isServiceInstalled = serviceStateManager.isInstalled;

    // TUN 模式可用条件：服务已安装 或 以管理员/root 权限运行
    final canEnableTun = isServiceInstalled || _isElevated;

    // Android 平台不支持 TUN 模式
    if (Platform.isAndroid) {
      return BaseCard(
        icon: Icons.router_outlined,
        title: context.translate.proxy.tunMode,
        // Android 平台禁用开关
        trailing: ModernSwitch(value: false, onChanged: null),
        child: _buildUnsupportedContent(context),
      );
    }

    // 使用 Selector 只在 tunEnabled 变化时重建，避免 isLoadingProxies 变化导致的闪烁
    return Selector<ClashProvider, bool>(
      selector: (_, provider) => provider.tunEnabled,
      builder: (context, tunEnabled, child) {
        return BaseCard(
          icon: Icons.router_outlined,
          title: context.translate.proxy.tunMode,
          // 右边只有开关
          trailing: ModernSwitch(
            value: tunEnabled,
            onChanged: !canEnableTun
                ? null
                : (value) async {
                    await context.read<ClashProvider>().setTunMode(value);
                    // TUN 模式切换后手动更新托盘菜单
                    AppTrayManager().updateTrayMenuManually();
                  },
          ),
          // 下方显示状态指示器
          child: _buildStatusIndicator(context, canEnableTun),
        );
      },
    );
  }

  // 根据平台返回不同的权限要求提示
  String _getPlatformRequirementHint(BuildContext context) {
    if (Platform.isWindows) {
      return context.translate.home.tunRequiresWindows;
    } else if (Platform.isLinux) {
      return context.translate.home.tunRequiresLinux;
    } else if (Platform.isMacOS) {
      return context.translate.home.tunRequiresMacOS;
    }
    return context.translate.proxy.tunRequiresService;
  }

  // 构建状态指示器
  Widget _buildStatusIndicator(BuildContext context, bool canEnableTun) {
    final theme = Theme.of(context);
    final isAvailable = canEnableTun;

    return ModernTooltip(
      message: isAvailable ? '' : _getPlatformRequirementHint(context),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            isAvailable
                ? Icons.check_circle_outline
                : Icons.warning_amber_rounded,
            size: 18,
            color: isAvailable
                ? theme.colorScheme.primary
                : theme.colorScheme.error,
          ),
          const SizedBox(width: 6),
          Text(
            isAvailable
                ? context.translate.home.tunStatusAvailable
                : context.translate.home.tunStatusUnavailable,
            style: theme.textTheme.bodySmall?.copyWith(
              fontSize: 13,
              color: isAvailable
                  ? theme.colorScheme.primary
                  : theme.colorScheme.error,
            ),
          ),
        ],
      ),
    );
  }

  // 构建不支持平台的内容
  Widget _buildUnsupportedContent(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: Theme.of(
          context,
        ).colorScheme.surfaceContainerHighest.withValues(alpha: 0.3),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        children: [
          Icon(
            Icons.info_outline,
            size: 20,
            color: Theme.of(
              context,
            ).colorScheme.onSurface.withValues(alpha: 0.5),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Text(
              context.translate.home.tunNotSupported,
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: Theme.of(
                  context,
                ).colorScheme.onSurface.withValues(alpha: 0.6),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
