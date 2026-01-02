import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_text_field.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/utils/logger.dart';

// TCP 保持活动配置卡片
class KeepAliveCard extends StatefulWidget {
  const KeepAliveCard({super.key});

  @override
  State<KeepAliveCard> createState() => _KeepAliveCardState();
}

class _KeepAliveCardState extends State<KeepAliveCard> {
  late bool _keepAliveEnabled;
  late final TextEditingController _keepAliveIntervalController;
  bool _isSaving = false;

  @override
  void initState() {
    super.initState();
    final clashManager = ClashManager.instance;
    _keepAliveEnabled = clashManager.isKeepAliveEnabled;
    _keepAliveIntervalController = TextEditingController(
      text: clashManager.keepAliveInterval.toString(),
    );
  }

  @override
  void dispose() {
    _keepAliveIntervalController.dispose();
    super.dispose();
  }

  // 保存配置
  Future<void> _saveConfig() async {
    final trans = context.translate;
    if (_isSaving) return;

    final interval = int.tryParse(_keepAliveIntervalController.text);
    if (interval == null || interval <= 0) {
      if (mounted) {
        ModernToast.error(
          context,
          trans.clash_features.keep_alive.interval_error,
        );
      }
      return;
    }

    setState(() => _isSaving = true);

    try {
      final clashProvider = Provider.of<ClashProvider>(context, listen: false);
      await ClashPreferences.instance.setKeepAliveInterval(interval);
      clashProvider.configService.setKeepAlive(_keepAliveEnabled);

      if (mounted) {
        ModernToast.success(
          context,
          trans.clash_features.keep_alive.save_success,
        );
      }
    } catch (e) {
      Logger.error('保存 TCP 保持活动配置失败: $e');
      if (mounted) {
        ModernToast.error(
          context,
          trans.clash_features.keep_alive.save_failed.replaceAll(
            '{error}',
            e.toString(),
          ),
        );
      }
    } finally {
      if (mounted) {
        setState(() => _isSaving = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final trans = context.translate;
    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      isHoverEnabled: false,
      isTapEnabled: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // 开关行
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              // 左侧图标和标题
              Row(
                children: [
                  const Icon(Icons.timer_outlined),
                  const SizedBox(
                    width: ModernFeatureCardSpacing.featureIconToTextSpacing,
                  ),
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        trans.clash_features.keep_alive.title,
                        style: theme.textTheme.titleMedium,
                      ),
                      Text(
                        trans.clash_features.keep_alive.subtitle,
                        style: theme.textTheme.bodySmall,
                      ),
                    ],
                  ),
                ],
              ),
              // 右侧开关
              ModernSwitch(
                value: _keepAliveEnabled,
                onChanged: (value) async {
                  setState(() => _keepAliveEnabled = value);
                  final clashProvider = Provider.of<ClashProvider>(
                    context,
                    listen: false,
                  );
                  await ClashPreferences.instance.setKeepAliveEnabled(value);
                  if (!mounted) return;
                  clashProvider.configService.setKeepAlive(_keepAliveEnabled);
                },
              ),
            ],
          ),

          // 间隔输入框（固定展开）
          const SizedBox(height: 16),
          const Divider(height: 1),
          const SizedBox(height: 16),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                trans.clash_features.keep_alive.interval_label,
                style: theme.textTheme.titleSmall,
              ),
              Row(
                children: [
                  SizedBox(
                    width: 80,
                    child: ModernTextField(
                      controller: _keepAliveIntervalController,
                      keyboardType: TextInputType.number,
                      hintText: '30',
                      height: 36,
                      inputFormatters: [
                        FilteringTextInputFormatter.digitsOnly,
                        LengthLimitingTextInputFormatter(4),
                      ],
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(
                    trans.clash_features.keep_alive.interval_unit,
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: theme.colorScheme.onSurface.withAlpha(150),
                    ),
                  ),
                ],
              ),
            ],
          ),
          const SizedBox(height: 16),
          // 保存按钮
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              FilledButton.icon(
                onPressed: _isSaving ? null : _saveConfig,
                icon: _isSaving
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.save, size: 18),
                label: Text(
                  _isSaving
                      ? trans.clash_features.keep_alive.saving
                      : trans.common.save,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
