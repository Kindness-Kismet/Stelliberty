import 'dart:io';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/providers/content_provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/ui/constants/spacing.dart';

export 'behavior_settings_page.dart';

class SettingsOverviewPage extends StatefulWidget {
  const SettingsOverviewPage({super.key});

  @override
  State<SettingsOverviewPage> createState() => _SettingsOverviewPageState();
}

class _SettingsOverviewPageState extends State<SettingsOverviewPage> {
  String _version = '';
  bool _versionLoaded = false;

  @override
  void initState() {
    super.initState();
    Logger.info('初始化 SettingsOverviewPage');
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_versionLoaded) {
      _versionLoaded = true;
      _loadVersion();
    }
  }

  Future<void> _loadVersion() async {
    final trans = context.translate;
    final packageInfo = await PackageInfo.fromPlatform();
    if (!mounted) return;
    setState(() {
      _version = '${trans.about.version} v${packageInfo.version}';
    });
  }

  @override
  Widget build(BuildContext context) {
    final provider = Provider.of<ContentProvider>(context, listen: false);
    final theme = Theme.of(context);
    final trans = context.translate;

    return Scaffold(
      backgroundColor: Colors.transparent,
      body: Padding(
        padding: SpacingConstants.scrollbarPadding,
        child: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            Text(
              trans.common.settings,
              style: theme.textTheme.headlineMedium?.copyWith(
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 20),
            ListTile(
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(8),
              ),
              leading: const Icon(Icons.palette_outlined),
              title: Text(trans.theme.title),
              subtitle: Text(trans.theme.description),
              onTap: () => provider.switchView(ContentView.settingsAppearance),
              // 只移除点击时的水波纹扩散效果，保留悬停效果
              splashColor: Colors.transparent,
            ),
            ListTile(
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(8),
              ),
              leading: const Icon(Icons.language_outlined),
              title: Text(trans.language.title),
              subtitle: Text(trans.language.description),
              onTap: () => provider.switchView(ContentView.settingsLanguage),
              // 只移除点击时的水波纹扩散效果，保留悬停效果
              splashColor: Colors.transparent,
            ),
            ListTile(
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(8),
              ),
              leading: const Icon(Icons.settings_suggest_outlined),
              title: Text(trans.clash_features.title),
              subtitle: Text(trans.clash_features.description),
              onTap: () =>
                  provider.switchView(ContentView.settingsClashFeatures),
              // 只移除点击时的水波纹扩散效果，保留悬停效果
              splashColor: Colors.transparent,
            ),
            ListTile(
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(8),
              ),
              leading: const Icon(Icons.apps_outlined),
              title: Text(trans.behavior.title),
              subtitle: Text(trans.behavior.description),
              onTap: () => provider.switchView(ContentView.settingsBehavior),
              // 只移除点击时的水波纹扩散效果，保留悬停效果
              splashColor: Colors.transparent,
            ),
            ListTile(
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(8),
              ),
              leading: const Icon(Icons.backup_outlined),
              title: Text(trans.backup.title),
              subtitle: Text(trans.backup.description),
              onTap: () => provider.switchView(ContentView.settingsBackup),
              splashColor: Colors.transparent,
            ),
            // 应用更新选项只在 Windows 平台显示
            if (Platform.isWindows)
              ListTile(
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(8),
                ),
                leading: const Icon(Icons.new_releases_outlined),
                title: Text(trans.app_update.title),
                subtitle: Text(trans.app_update.description),
                onTap: () => provider.switchView(ContentView.settingsAppUpdate),
                // 只移除点击时的水波纹扩散效果，保留悬停效果
                splashColor: Colors.transparent,
              ),
            ListTile(
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(8),
              ),
              leading: const Icon(Icons.info_outline),
              title: Text(trans.about.title),
              subtitle: Text(_version.isEmpty ? '…' : _version),
              onTap: null,
              splashColor: Colors.transparent,
            ),
          ],
        ),
      ),
    );
  }
}
