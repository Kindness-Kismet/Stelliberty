import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_text_field.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/utils/logger.dart';

// 延迟测试网址配置卡片
class TestUrlCard extends StatefulWidget {
  const TestUrlCard({super.key});

  @override
  State<TestUrlCard> createState() => _TestUrlCardState();
}

class _TestUrlCardState extends State<TestUrlCard> {
  late final TextEditingController _testUrlController;
  bool _isSaving = false;

  @override
  void initState() {
    super.initState();
    _testUrlController = TextEditingController(
      text: ClashManager.instance.testUrl,
    );
  }

  @override
  void dispose() {
    _testUrlController.dispose();
    super.dispose();
  }

  // 保存配置
  Future<void> _saveConfig() async {
    final trans = context.translate;
    if (_isSaving) return;

    setState(() => _isSaving = true);

    try {
      final clashProvider = Provider.of<ClashProvider>(context, listen: false);
      clashProvider.configService.setTestUrl(_testUrlController.text);

      if (mounted) {
        ModernToast.success(
          context,
          trans.clash_features.test_url.save_success,
        );
      }
    } catch (e) {
      Logger.error('保存延迟测试网址失败: $e');
      if (mounted) {
        ModernToast.error(
          context,
          trans.clash_features.test_url.save_failed.replaceAll(
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
    final trans = context.translate;

    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      isHoverEnabled: false,
      isTapEnabled: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // 标题区域
          Row(
            children: [
              const Icon(Icons.speed_outlined),
              const SizedBox(
                width: ModernFeatureCardSpacing.featureIconToTextSpacing,
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    trans.clash_features.test_url.title,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  Text(
                    trans.clash_features.test_url.subtitle,
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ),
            ],
          ),
          const SizedBox(height: 16),
          // URL 输入区域
          ModernTextField(
            controller: _testUrlController,
            keyboardType: TextInputType.url,
            labelText: trans.clash_features.test_url.label,
            hintText: ClashDefaults.defaultTestUrl,
            minLines: 1,
            suffixIcon: Padding(
              padding: const EdgeInsets.only(right: 4),
              child: ModernTooltip(
                message: trans.clash_features.test_url.restore_default,
                child: IconButton(
                  icon: const Icon(Icons.restore),
                  onPressed: () {
                    setState(() {
                      _testUrlController.text = ClashDefaults.defaultTestUrl;
                    });
                  },
                ),
              ),
            ),
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
                      ? trans.clash_features.test_url.saving
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
