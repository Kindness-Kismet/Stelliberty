import 'package:stelliberty/ui/constants/spacing.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/setting/system_proxy_card.dart';
import 'package:stelliberty/ui/widgets/setting/tun_config_card.dart';
import 'package:stelliberty/ui/widgets/setting/uwp_loopback_card.dart';
import 'package:stelliberty/services/log_print_service.dart';

class SystemIntegrationPage extends StatefulWidget {
  const SystemIntegrationPage({super.key});

  @override
  State<SystemIntegrationPage> createState() => _SystemIntegrationPageState();
}

class _SystemIntegrationPageState extends State<SystemIntegrationPage> {
  final _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    Logger.info('初始化 SystemIntegrationPage');
  }

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final provider = Provider.of<ContentProvider>(context, listen: false);
    final theme = Theme.of(context);
    final trans = context.translate;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // 自定义标题栏
        Padding(
          padding: const EdgeInsets.all(8.0),
          child: Row(
            children: [
              IconButton(
                icon: const Icon(Icons.arrow_back),
                onPressed: () =>
                    provider.switchView(ContentView.settingsClashFeatures),
              ),
              const SizedBox(width: 8),
              Text(
                trans.clash_features.system_integration.page_title,
                style: theme.textTheme.titleLarge,
              ),
            ],
          ),
        ),
        // 可滚动内容
        Expanded(
          child: Padding(
            padding: SpacingConstants.scrollbarPadding,
            child: SingleChildScrollView(
              controller: _scrollController,
              physics: const BouncingScrollPhysics(),
              padding: const EdgeInsets.fromLTRB(
                32,
                16,
                32 - SpacingConstants.scrollbarRightCompensation,
                16,
              ),
              child: const Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  SystemProxyCard(),
                  SizedBox(height: 16),
                  TunConfigCard(),
                  SizedBox(height: 16),
                  UwpLoopbackCard(),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}
