import 'package:flutter/material.dart';
import 'package:stelliberty/clash/model/subscription_model.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/option_selector.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 代理模式选择器
// 用于订阅对话框和覆写对话框的代理模式选择
class ProxyModeSelector extends StatelessWidget {
  final SubscriptionProxyMode selectedValue;
  final ValueChanged<SubscriptionProxyMode> onChanged;

  const ProxyModeSelector({
    super.key,
    required this.selectedValue,
    required this.onChanged,
  });

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return OptionSelectorWidget<SubscriptionProxyMode>(
      title: trans.subscription_dialog.proxy_mode_title,
      titleIcon: Icons.public,
      isHorizontal: true,
      options: [
        OptionItem(
          value: SubscriptionProxyMode.direct,
          title: trans.subscription_dialog.proxy_mode_direct_title,
          subtitle: trans.subscription_dialog.proxy_mode_direct,
        ),
        OptionItem(
          value: SubscriptionProxyMode.system,
          title: trans.subscription_dialog.proxy_mode_system_title,
          subtitle: trans.subscription_dialog.proxy_mode_system,
        ),
        OptionItem(
          value: SubscriptionProxyMode.core,
          title: trans.subscription_dialog.proxy_mode_core_title,
          subtitle: trans.subscription_dialog.proxy_mode_core,
        ),
      ],
      selectedValue: selectedValue,
      onChanged: onChanged,
    );
  }
}
