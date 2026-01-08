import 'dart:io';

// 平台检测辅助工具
// 用于在移动端和桌面端之间进行条件编译和功能适配
class PlatformHelper {
  // 是否为桌面平台（Windows、macOS、Linux）
  static bool get isDesktop =>
      Platform.isWindows || Platform.isMacOS || Platform.isLinux;

  // 是否为移动平台（Android、iOS）
  static bool get isMobile => Platform.isAndroid || Platform.isIOS;

  // 是否需要窗口管理（桌面平台特有）
  static bool get needsWindowManagement => isDesktop;

  // 是否需要系统托盘（桌面平台特有）
  static bool get needsSystemTray => isDesktop;

  // 是否支持拖放文件（桌面平台特有）
  static bool get supportsFileDrop => isDesktop;

  // 是否支持窗口效果（毛玻璃等，桌面平台特有）
  static bool get supportsWindowEffects => isDesktop;

  // 是否支持单实例检测（主要用于桌面）
  static bool get supportsSingleInstance => isDesktop;
}
