import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
import 'package:provider/provider.dart';
import 'package:system_theme/system_theme.dart';
import 'package:device_info_plus/device_info_plus.dart';
import 'package:stelliberty/providers/window_effect_provider.dart';
import 'package:stelliberty/providers/theme_provider.dart';
import 'package:stelliberty/ui/common/modern_dropdown_menu.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_dropdown_button.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 一个允许用户选择和管理应用主题（模式和颜色）的设置组件。
//
// 包含一个用于选择浅色/暗色/系统模式的下拉菜单，
// 以及一个用于选择应用主色调的颜色网格。
class ThemeSelector extends StatefulWidget {
  const ThemeSelector({super.key});

  @override
  State<ThemeSelector> createState() => _ThemeSelectorState();
}

class _ThemeSelectorState extends State<ThemeSelector> {
  bool _isHoveringOnThemeModeMenu = false;
  bool _isHoveringOnEffectMenu = false;
  late Future<bool> _isWin11OrGreater;

  @override
  void initState() {
    super.initState();
    _isWin11OrGreater = _checkWindows11OrGreater();
  }

  // 检查当前系统是否为 Windows 11 或更高版本
  //
  // Windows 11 的主版本号是 10，构建号 (build number) >= 22000
  Future<bool> _checkWindows11OrGreater() async {
    // 如果不是 Windows 平台，直接返回 false
    if (defaultTargetPlatform != TargetPlatform.windows) {
      return false;
    }

    try {
      final deviceInfo = await DeviceInfoPlugin().windowsInfo;
      // Windows 11 的 buildNumber 从 22000 开始
      return deviceInfo.buildNumber >= 22000;
    } catch (e) {
      // 如果获取信息失败，则保守地返回 false
      Logger.error('获取 Windows 系统信息失败：$e');
      return false;
    }
  }

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return Consumer2<ThemeProvider, WindowEffectProvider>(
      builder: (context, themeProvider, windowEffectProvider, _) {
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // 主题模式选择
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 16),
              child: ModernFeatureCard(
                isSelected: false,
                onTap: () {},
                isHoverEnabled: false,
                isTapEnabled: false,
                child: Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  crossAxisAlignment: CrossAxisAlignment.center,
                  children: [
                    Row(
                      children: [
                        const Icon(Icons.palette_outlined),
                        const SizedBox(
                          width:
                              ModernFeatureCardSpacing.featureIconToTextSpacing,
                        ),
                        Text(
                          trans.theme.title,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ],
                    ),
                    MouseRegion(
                      onEnter: (_) =>
                          setState(() => _isHoveringOnThemeModeMenu = true),
                      onExit: (_) =>
                          setState(() => _isHoveringOnThemeModeMenu = false),
                      child: ModernDropdownMenu<AppThemeMode>(
                        items: AppThemeMode.values,
                        selectedItem: themeProvider.themeMode,
                        onSelected: themeProvider.setThemeMode,
                        itemToString: (mode) => mode.displayName,
                        child: CustomDropdownButton(
                          text: themeProvider.themeMode.displayName,
                          isHovering: _isHoveringOnThemeModeMenu,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),

            // 窗口效果选择（仅 Win11+）
            FutureBuilder<bool>(
              future: _isWin11OrGreater,
              builder: (context, snapshot) {
                if (snapshot.connectionState == ConnectionState.done &&
                    snapshot.data == true) {
                  return Padding(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 32,
                      vertical: 16,
                    ),
                    child: ModernFeatureCard(
                      isSelected: false,
                      onTap: () {},
                      isHoverEnabled: false,
                      isTapEnabled: false,
                      child: Row(
                        mainAxisAlignment: MainAxisAlignment.spaceBetween,
                        crossAxisAlignment: CrossAxisAlignment.center,
                        children: [
                          Row(
                            children: [
                              const Icon(Icons.blur_on_outlined),
                              const SizedBox(
                                width: ModernFeatureCardSpacing
                                    .featureIconToTextSpacing,
                              ),
                              Text(
                                trans.theme.effect,
                                style: Theme.of(context).textTheme.titleMedium,
                              ),
                            ],
                          ),
                          MouseRegion(
                            onEnter: (_) =>
                                setState(() => _isHoveringOnEffectMenu = true),
                            onExit: (_) =>
                                setState(() => _isHoveringOnEffectMenu = false),
                            child: ModernDropdownMenu<AppWindowEffect>(
                              items: AppWindowEffect.values,
                              selectedItem: windowEffectProvider.windowEffect,
                              onSelected: (effect) {
                                windowEffectProvider.setWindowEffect(effect);
                              },
                              itemToString: (effect) {
                                switch (effect) {
                                  case AppWindowEffect.mica:
                                    return trans.theme.effect_mica;
                                  case AppWindowEffect.acrylic:
                                    return context
                                        .translate
                                        .theme
                                        .effect_acrylic;
                                  case AppWindowEffect.tabbed:
                                    return trans.theme.effect_tabbed;
                                  default:
                                    return context
                                        .translate
                                        .theme
                                        .effect_default;
                                }
                              },
                              child: CustomDropdownButton(
                                text: (effect) {
                                  switch (effect) {
                                    case AppWindowEffect.mica:
                                      return trans.theme.effect_mica;
                                    case AppWindowEffect.acrylic:
                                      return context
                                          .translate
                                          .theme
                                          .effect_acrylic;
                                    case AppWindowEffect.tabbed:
                                      return context
                                          .translate
                                          .theme
                                          .effect_tabbed;
                                    default:
                                      return context
                                          .translate
                                          .theme
                                          .effect_default;
                                  }
                                }(windowEffectProvider.windowEffect),
                                isHovering: _isHoveringOnEffectMenu,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  );
                }
                return const SizedBox.shrink();
              },
            ),

            // 主题颜色选择
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 32),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    trans.theme.color,
                    style: Theme.of(context).textTheme.titleMedium?.copyWith(
                      color: Theme.of(context).colorScheme.onSurface,
                    ),
                  ),
                  const SizedBox(height: 12),
                  _buildColorSelector(context, themeProvider),
                ],
              ),
            ),
          ],
        );
      },
    );
  }

  // 移除未使用的方法，功能已经内联到 build 方法中

  // 构建显示所有可选主题颜色的网格。
  Widget _buildColorSelector(
    BuildContext context,
    ThemeProvider themeProvider,
  ) {
    return Wrap(
      spacing: 12,
      runSpacing: 12,
      children: List.generate(colorOptions.length, (i) {
        return _buildColorOption(
          context,
          i,
          themeProvider.colorIndex,
          themeProvider,
        );
      }),
    );
  }

  // 构建单个颜色选项，它是一个可点击的圆形色块。
  Widget _buildColorOption(
    BuildContext context,
    int index,
    int selectedIndex,
    ThemeProvider themeProvider,
  ) {
    final colorOption = colorOptions[index];
    final isSelected = index == selectedIndex;

    final Widget child = switch (colorOption) {
      StaticThemeColor() => Container(
        width: 40,
        height: 40,
        decoration: BoxDecoration(
          color: colorOption.color,
          shape: BoxShape.circle,
          boxShadow: [
            BoxShadow(
              color: Colors.black.withAlpha(66),
              blurRadius: 4,
              offset: const Offset(0, 2),
            ),
          ],
        ),
      ),
      SystemAccentThemeColor() => Container(
        width: 40,
        height: 40,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          border: Border.all(
            color: Theme.of(context).colorScheme.outline,
            width: 2,
          ),
        ),
        child: Icon(Icons.auto_fix_high, color: SystemTheme.accentColor.accent),
      ),
    };

    return ModernFeatureCard(
      isSelected: isSelected,
      borderRadius: 12,
      onTap: () {
        themeProvider.setColorIndex(index);
      },
      padding: EdgeInsets.zero,
      child: Container(
        width: 60,
        height: 60,
        alignment: Alignment.center,
        child: Stack(
          alignment: Alignment.center,
          children: [
            child, // 显示颜色圆圈或特殊图标
            // --- 选中时的对勾图标 ---
            if (isSelected)
              Align(
                alignment: Alignment.topRight,
                child: Container(
                  width: 20,
                  height: 20,
                  decoration: BoxDecoration(
                    color: Theme.of(context).colorScheme.primary,
                    shape: BoxShape.circle,
                  ),
                  child: const Icon(Icons.check, color: Colors.white, size: 14),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
