import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

class ClashFeaturesPage extends StatefulWidget {
  const ClashFeaturesPage({super.key});

  @override
  State<ClashFeaturesPage> createState() => _ClashFeaturesPageState();
}

class _ClashFeaturesPageState extends State<ClashFeaturesPage> {
  @override
  void initState() {
    super.initState();
    Logger.info('初始化 ClashFeaturesPage');
  }

  @override
  Widget build(BuildContext context) {
    final provider = Provider.of<ContentProvider>(context, listen: false);
    final theme = Theme.of(context);

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // 返回按钮和标题
          Padding(
            padding: const EdgeInsets.all(20),
            child: Row(
              children: [
                ModernIconTooltip(
                  message: context.translate.clashFeatures.backToSettings,
                  icon: Icons.arrow_back,
                  filled: false,
                  onPressed: () =>
                      provider.switchView(ContentView.settingsOverview),
                ),
                const SizedBox(width: 8),
                Text(
                  context.translate.clashFeatures.title,
                  style: theme.textTheme.headlineMedium?.copyWith(
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ],
            ),
          ),
          // 分类列表
          Expanded(
            child: ListView(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              children: [
                ListTile(
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                  leading: const Icon(Icons.network_check),
                  title: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .networkSettings
                        .title,
                  ),
                  subtitle: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .networkSettings
                        .subtitle,
                  ),
                  onTap: () => provider.switchView(
                    ContentView.settingsClashNetworkSettings,
                  ),
                  splashColor: Colors.transparent,
                ),
                ListTile(
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                  leading: const Icon(Icons.settings_ethernet),
                  title: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .portControl
                        .title,
                  ),
                  subtitle: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .portControl
                        .subtitle,
                  ),
                  onTap: () =>
                      provider.switchView(ContentView.settingsClashPortControl),
                  splashColor: Colors.transparent,
                ),
                ListTile(
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                  leading: const Icon(Icons.integration_instructions),
                  title: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .systemIntegration
                        .title,
                  ),
                  subtitle: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .systemIntegration
                        .subtitle,
                  ),
                  onTap: () => provider.switchView(
                    ContentView.settingsClashSystemIntegration,
                  ),
                  splashColor: Colors.transparent,
                ),
                ListTile(
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                  leading: const Icon(Icons.dns),
                  title: Text(
                    context.translate.clashFeatures.navigation.dnsConfig.title,
                  ),
                  subtitle: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .dnsConfig
                        .subtitle,
                  ),
                  onTap: () =>
                      provider.switchView(ContentView.settingsClashDnsConfig),
                  splashColor: Colors.transparent,
                ),
                ListTile(
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                  leading: const Icon(Icons.speed),
                  title: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .performance
                        .title,
                  ),
                  subtitle: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .performance
                        .subtitle,
                  ),
                  onTap: () =>
                      provider.switchView(ContentView.settingsClashPerformance),
                  splashColor: Colors.transparent,
                ),
                ListTile(
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                  ),
                  leading: const Icon(Icons.bug_report),
                  title: Text(
                    context.translate.clashFeatures.navigation.logsDebug.title,
                  ),
                  subtitle: Text(
                    context
                        .translate
                        .clashFeatures
                        .navigation
                        .logsDebug
                        .subtitle,
                  ),
                  onTap: () =>
                      provider.switchView(ContentView.settingsClashLogsDebug),
                  splashColor: Colors.transparent,
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
