import 'package:stelliberty/ui/constants/spacing.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_dropdown_menu.dart';
import 'package:stelliberty/ui/common/modern_dropdown_button.dart';
import 'package:stelliberty/ui/widgets/setting/keep_alive_card.dart';
import 'package:stelliberty/utils/logger.dart';

class PerformancePage extends StatefulWidget {
  const PerformancePage({super.key});

  @override
  State<PerformancePage> createState() => _PerformancePageState();
}

class _PerformancePageState extends State<PerformancePage> {
  final _scrollController = ScrollController();
  late String _geodataLoader;
  late String _findProcessMode;
  bool _isHoveringOnGeodataMenu = false;
  bool _isHoveringOnProcessMenu = false;

  @override
  void initState() {
    super.initState();
    Logger.info('初始化 PerformancePage');
    _loadSettings();
  }

  void _loadSettings() {
    final prefs = ClashPreferences.instance;
    _geodataLoader = prefs.getGeodataLoader();
    _findProcessMode = prefs.getFindProcessMode();
  }

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  String _getGeodataLoaderDisplayName(BuildContext context, String value) {
    switch (value) {
      case 'standard':
        return context
            .translate
            .clash_features
            .performance
            .geodata_loader
            .standard;
      case 'memconservative':
        return context
            .translate
            .clash_features
            .performance
            .geodata_loader
            .memconservative;
      default:
        return value;
    }
  }

  String _getFindProcessModeDisplayName(BuildContext context, String value) {
    final trans = context.translate;

    switch (value) {
      case 'off':
        return trans.clash_features.performance.find_process.off;
      case 'strict':
        return trans.clash_features.performance.find_process.strict;
      case 'always':
        return trans.clash_features.performance.find_process.always;
      default:
        return value;
    }
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
                trans.clash_features.performance.page_title,
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
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // GEO 数据加载模式
                  ModernFeatureCard(
                    isSelected: false,
                    onTap: () {},
                    isHoverEnabled: true,
                    isTapEnabled: false,
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Expanded(
                          child: Row(
                            children: [
                              const Icon(Icons.public),
                              const SizedBox(
                                width: ModernFeatureCardSpacing
                                    .featureIconToTextSpacing,
                              ),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      context
                                          .translate
                                          .clash_features
                                          .performance
                                          .geodata_loader
                                          .title,
                                      style: theme.textTheme.titleMedium,
                                    ),
                                    Text(
                                      context
                                          .translate
                                          .clash_features
                                          .performance
                                          .geodata_loader
                                          .subtitle,
                                      style: theme.textTheme.bodySmall
                                          ?.copyWith(
                                            color: theme.colorScheme.onSurface
                                                .withAlpha(153),
                                          ),
                                    ),
                                  ],
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 16),
                        MouseRegion(
                          onEnter: (_) =>
                              setState(() => _isHoveringOnGeodataMenu = true),
                          onExit: (_) =>
                              setState(() => _isHoveringOnGeodataMenu = false),
                          child: ModernDropdownMenu<String>(
                            items: const ['standard', 'memconservative'],
                            selectedItem: _geodataLoader,
                            onSelected: (value) {
                              setState(() => _geodataLoader = value);
                              clashProvider.configService.setGeodataLoader(
                                value,
                              );
                            },
                            itemToString: (val) =>
                                _getGeodataLoaderDisplayName(context, val),
                            child: CustomDropdownButton(
                              text: _getGeodataLoaderDisplayName(
                                context,
                                _geodataLoader,
                              ),
                              isHovering: _isHoveringOnGeodataMenu,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 16),

                  // 查找进程模式
                  ModernFeatureCard(
                    isSelected: false,
                    onTap: () {},
                    isHoverEnabled: true,
                    isTapEnabled: false,
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Expanded(
                          child: Row(
                            children: [
                              const Icon(Icons.search),
                              const SizedBox(
                                width: ModernFeatureCardSpacing
                                    .featureIconToTextSpacing,
                              ),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      context
                                          .translate
                                          .clash_features
                                          .performance
                                          .find_process
                                          .title,
                                      style: theme.textTheme.titleMedium,
                                    ),
                                    Text(
                                      context
                                          .translate
                                          .clash_features
                                          .performance
                                          .find_process
                                          .subtitle,
                                      style: theme.textTheme.bodySmall
                                          ?.copyWith(
                                            color: theme.colorScheme.onSurface
                                                .withAlpha(153),
                                          ),
                                    ),
                                  ],
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 16),
                        MouseRegion(
                          onEnter: (_) =>
                              setState(() => _isHoveringOnProcessMenu = true),
                          onExit: (_) =>
                              setState(() => _isHoveringOnProcessMenu = false),
                          child: ModernDropdownMenu<String>(
                            items: const ['off', 'strict', 'always'],
                            selectedItem: _findProcessMode,
                            onSelected: (value) {
                              setState(() => _findProcessMode = value);
                              clashProvider.configService.setFindProcessMode(
                                value,
                              );
                            },
                            itemToString: (val) =>
                                _getFindProcessModeDisplayName(context, val),
                            child: CustomDropdownButton(
                              text: _getFindProcessModeDisplayName(
                                context,
                                _findProcessMode,
                              ),
                              isHovering: _isHoveringOnProcessMenu,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 16),

                  // TCP 保持活动
                  const KeepAliveCard(),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}
