import 'dart:io';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';
import 'package:stelliberty/ui/constants/spacing.dart';

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
    final trans = context.translate;

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
                  message: trans.clash_features.back_to_settings,
                  icon: Icons.arrow_back,
                  isFilled: false,
                  onPressed: () =>
                      provider.switchView(ContentView.settingsOverview),
                ),
                const SizedBox(width: 8),
                Text(
                  trans.clash_features.title,
                  style: theme.textTheme.headlineMedium?.copyWith(
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ],
            ),
          ),
          // 分类列表
          Expanded(
            child: Padding(
              padding: SpacingConstants.scrollbarPadding,
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
                          .clash_features
                          .navigation
                          .network_settings
                          .title,
                    ),
                    subtitle: Text(
                      context
                          .translate
                          .clash_features
                          .navigation
                          .network_settings
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
                          .clash_features
                          .navigation
                          .port_control
                          .title,
                    ),
                    subtitle: Text(
                      context
                          .translate
                          .clash_features
                          .navigation
                          .port_control
                          .subtitle,
                    ),
                    onTap: () => provider.switchView(
                      ContentView.settingsClashPortControl,
                    ),
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
                          .clash_features
                          .navigation
                          .system_integration
                          .title,
                    ),
                    subtitle: Text(
                      Platform.isWindows
                          ? context
                                .translate
                                .clash_features
                                .navigation
                                .system_integration
                                .subtitle_windows
                          : context
                                .translate
                                .clash_features
                                .navigation
                                .system_integration
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
                      context
                          .translate
                          .clash_features
                          .navigation
                          .dns_config
                          .title,
                    ),
                    subtitle: Text(
                      context
                          .translate
                          .clash_features
                          .navigation
                          .dns_config
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
                          .clash_features
                          .navigation
                          .performance
                          .title,
                    ),
                    subtitle: Text(
                      context
                          .translate
                          .clash_features
                          .navigation
                          .performance
                          .subtitle,
                    ),
                    onTap: () => provider.switchView(
                      ContentView.settingsClashPerformance,
                    ),
                    splashColor: Colors.transparent,
                  ),
                  ListTile(
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(8),
                    ),
                    leading: const Icon(Icons.bug_report),
                    title: Text(
                      context
                          .translate
                          .clash_features
                          .navigation
                          .logs_debug
                          .title,
                    ),
                    subtitle: Text(
                      context
                          .translate
                          .clash_features
                          .navigation
                          .logs_debug
                          .subtitle,
                    ),
                    onTap: () =>
                        provider.switchView(ContentView.settingsClashLogsDebug),
                    splashColor: Colors.transparent,
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
