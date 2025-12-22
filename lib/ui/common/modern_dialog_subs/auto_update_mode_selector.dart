import 'package:flutter/material.dart';
import 'package:stelliberty/clash/data/subscription_model.dart';
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
    final trans = context.translate.subscriptionDialog;

    return OptionSelectorWidget<AutoUpdateMode>(
      title: trans.autoUpdateTitle,
      titleIcon: Icons.update,
      isHorizontal: true,
      options: [
        OptionItem(
          value: AutoUpdateMode.disabled,
          title: trans.autoUpdateDisabled,
          subtitle: trans.autoUpdateDisabledDesc,
        ),
        OptionItem(
          value: AutoUpdateMode.onStartup,
          title: trans.autoUpdateOnStartup,
          subtitle: trans.autoUpdateOnStartupDesc,
        ),
        OptionItem(
          value: AutoUpdateMode.interval,
          title: trans.autoUpdateInterval,
          subtitle: trans.autoUpdateIntervalDesc,
        ),
      ],
      selectedValue: selectedValue,
      onChanged: onChanged,
    );
  }
}
