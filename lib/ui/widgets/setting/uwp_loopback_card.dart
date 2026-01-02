import 'dart:io';
import 'package:flutter/material.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/widgets/setting/uwp_loopback_dialog.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

/// UWP 回环管理卡片
/// 仅在 Windows 平台显示
class UwpLoopbackCard extends StatelessWidget {
  const UwpLoopbackCard({super.key});

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    // 仅在 Windows 平台显示
    if (!Platform.isWindows) {
      return const SizedBox.shrink();
    }

    return ModernFeatureLayoutCard(
      icon: Icons.apps,
      title: trans.uwp_loopback.card_title,
      subtitle: trans.uwp_loopback.card_subtitle,
      trailing: ModernTooltip(
        message: trans.uwp_loopback.open_manager,
        child: IconButton(
          icon: const Icon(Icons.open_in_new),
          onPressed: () {
            UwpLoopbackDialog.show(context);
          },
        ),
      ),
      isHoverEnabled: true,
      isTapEnabled: false,
    );
  }
}
