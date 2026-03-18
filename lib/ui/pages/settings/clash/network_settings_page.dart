import 'package:stelliberty/ui/constants/spacing.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/widgets/setting/lan_auth_card.dart';
import 'package:stelliberty/services/log_print_service.dart';

class NetworkSettingsPage extends StatefulWidget {
  const NetworkSettingsPage({super.key});

  @override
  State<NetworkSettingsPage> createState() => _NetworkSettingsPageState();
}

class _NetworkSettingsPageState extends State<NetworkSettingsPage> {
  final _scrollController = ScrollController();
  late bool _unifiedDelay;
  late bool _ipv6;
  late bool _tcpConcurrent;

  @override
  void initState() {
    super.initState();
    Logger.info('初始化 NetworkSettingsPage');
    _loadSettings();
  }

  void _loadSettings() {
    final prefs = ClashPreferences.instance;
    _unifiedDelay = prefs.getUnifiedDelayEnabled();
    _ipv6 = prefs.getIpv6();
    _tcpConcurrent = prefs.getTcpConcurrent();
  }

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final provider = Provider.of<ContentProvider>(context, listen: false);
    final clashProvider = Provider.of<ClashProvider>(context, listen: false);
    final theme = Theme.of(context);
    final trans = context.translate;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
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
                trans.clash_features.network_settings.page_title,
                style: theme.textTheme.titleLarge,
              ),
            ],
          ),
        ),
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
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _buildSwitchCard(
                    context: context,
                    icon: Icons.speed,
                    title: trans.clash_features.network_settings.unified_delay.title,
                    subtitle: trans.clash_features.network_settings.unified_delay.subtitle,
                    value: _unifiedDelay,
                    onChanged: (value) {
                      setState(() => _unifiedDelay = value);
                      clashProvider.setUnifiedDelay(value);
                    },
                  ),
                  const SizedBox(height: 16),
                  const LanAuthCard(),
                  const SizedBox(height: 16),
                  _buildSwitchCard(
                    context: context,
                    icon: Icons.language,
                    title: trans.clash_features.network_settings.ipv6.title,
                    subtitle: trans.clash_features.network_settings.ipv6.subtitle,
                    value: _ipv6,
                    onChanged: (value) {
                      setState(() => _ipv6 = value);
                      clashProvider.setIpv6(value);
                    },
                  ),
                  const SizedBox(height: 16),
                  _buildSwitchCard(
                    context: context,
                    icon: Icons.multiple_stop,
                    title: trans.clash_features.network_settings.tcp_concurrent.title,
                    subtitle: trans.clash_features.network_settings.tcp_concurrent.subtitle,
                    value: _tcpConcurrent,
                    onChanged: (value) {
                      setState(() => _tcpConcurrent = value);
                      clashProvider.setTcpConcurrent(value);
                    },
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildSwitchCard({
    required BuildContext context,
    required IconData icon,
    required String title,
    required String subtitle,
    required bool value,
    required ValueChanged<bool> onChanged,
  }) {
    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      isHoverEnabled: true,
      isTapEnabled: false,
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Row(
            children: [
              Icon(icon),
              const SizedBox(
                width: ModernFeatureCardSpacing.featureIconToTextSpacing,
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: Theme.of(context).textTheme.titleMedium),
                  Text(
                    subtitle,
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                      color: Theme.of(
                        context,
                      ).colorScheme.onSurface.withAlpha(153),
                    ),
                  ),
                ],
              ),
            ],
          ),
          ModernSwitch(value: value, onChanged: onChanged),
        ],
      ),
    );
  }
}
