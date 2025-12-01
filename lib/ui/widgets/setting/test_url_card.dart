import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_text_field.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

// 测速链接配置卡片
class TestUrlCard extends StatefulWidget {
  const TestUrlCard({super.key});

  @override
  State<TestUrlCard> createState() => _TestUrlCardState();
}

class _TestUrlCardState extends State<TestUrlCard> {
  late final TextEditingController _testUrlController;

  @override
  void initState() {
    super.initState();
    _testUrlController = TextEditingController(
      text: ClashPreferences.instance.getTestUrl(),
    );
  }

  @override
  void dispose() {
    _testUrlController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
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
            // 标题区域
            Row(
              children: [
                const Icon(Icons.speed_outlined),
                const SizedBox(width: 12),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      context.translate.clashFeatures.testUrl.title,
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    Text(
                      context.translate.clashFeatures.testUrl.subtitle,
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
              labelText: context.translate.clashFeatures.testUrl.label,
              hintText: ClashDefaults.defaultTestUrl,
              suffixIcon: Padding(
                padding: const EdgeInsets.only(right: 4),
                child: ModernTooltip(
                  message:
                      context.translate.clashFeatures.testUrl.restoreDefault,
                  child: IconButton(
                    icon: const Icon(Icons.restore),
                    onPressed: () {
                      setState(() {
                        _testUrlController.text = ClashDefaults.defaultTestUrl;
                      });
                      final clashProvider = Provider.of<ClashProvider>(
                        context,
                        listen: false,
                      );
                      clashProvider.configService.setTestUrl(
                        _testUrlController.text,
                      );
                    },
                  ),
                ),
              ),
              onSubmitted: (value) {
                final clashProvider = Provider.of<ClashProvider>(
                  context,
                  listen: false,
                );
                clashProvider.configService.setTestUrl(value);
              },
            ),
          ],
        ),
      ),
    );
  }
}
