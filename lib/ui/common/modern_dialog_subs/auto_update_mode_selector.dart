import 'package:flutter/material.dart';
import 'package:stelliberty/clash/model/subscription_model.dart';
import 'package:stelliberty/ui/common/modern_dialog_subs/option_selector.dart';
import 'package:stelliberty/i18n/i18n.dart';

// 自动更新模式选择器
// 用于订阅对话框的自动更新模式选择
class AutoUpdateModeSelector extends StatelessWidget {
  final AutoUpdateMode selectedValue;
  final ValueChanged<AutoUpdateMode> onChanged;

  const AutoUpdateModeSelector({
    super.key,
    required this.selectedValue,
    required this.onChanged,
  });

  @override
  Widget build(BuildContext context) {
    final trans = context.translate.subscription_dialog;

    return OptionSelectorWidget<AutoUpdateMode>(
      title: trans.auto_update_title,
      titleIcon: Icons.update,
      isHorizontal: true,
      options: [
        OptionItem(
          value: AutoUpdateMode.disabled,
          title: trans.auto_update_disabled,
          subtitle: trans.auto_update_disabled_desc,
        ),
        OptionItem(
          value: AutoUpdateMode.onStartup,
          title: trans.auto_update_on_startup,
          subtitle: trans.auto_update_on_startup_desc,
        ),
        OptionItem(
          value: AutoUpdateMode.interval,
          title: trans.auto_update_interval,
          subtitle: trans.auto_update_interval_desc,
        ),
      ],
      selectedValue: selectedValue,
      onChanged: onChanged,
    );
  }
}
