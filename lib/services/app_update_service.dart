import 'package:package_info_plus/package_info_plus.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/utils/logger.dart';

// 应用更新信息
class AppUpdateInfo {
  final String currentVersion;
  final String latestVersion;
  final bool hasUpdate;
  final String? downloadUrl;
  final String? releaseNotes;
  final String? htmlUrl;

  AppUpdateInfo({
    required this.currentVersion,
    required this.latestVersion,
    required this.hasUpdate,
    this.downloadUrl,
    this.releaseNotes,
    this.htmlUrl,
  });
}

// 应用更新服务 (Rust 后端包装)
//
// 核心逻辑已迁移至 Rust，提供更高的性能和可靠性
class AppUpdateService {
  AppUpdateService._();

  static final AppUpdateService instance = AppUpdateService._();

  static const String _githubRepo = 'Kindness-Kismet/Stelliberty';

  // 检查更新
  Future<AppUpdateInfo?> checkForUpdate() async {
    try {
      // 获取当前版本
      final packageInfo = await PackageInfo.fromPlatform();
      final currentVersion = packageInfo.version;

      // 发送请求到 Rust 后端
      CheckAppUpdateRequest(
        currentVersion: currentVersion,
        githubRepo: _githubRepo,
      ).sendSignalToRust();

      // 等待 Rust 响应
      final receiver = AppUpdateResult.rustSignalStream;
      final result = await receiver.first;

      // 检查错误
      if (result.message.errorMessage != null &&
          result.message.errorMessage!.isNotEmpty) {
        Logger.error('Rust 更新检查失败: ${result.message.errorMessage}');
        return null;
      }

      // 返回更新信息（无论是否有更新都返回完整信息）
      if (result.message.hasUpdate) {
        Logger.info('发现新版本: ${result.message.latestVersion}');
      } else {
        Logger.info(
          '无需更新 (当前: $currentVersion, 最新: ${result.message.latestVersion})',
        );
      }

      return AppUpdateInfo(
        currentVersion: result.message.currentVersion,
        latestVersion: result.message.latestVersion,
        hasUpdate: result.message.hasUpdate,
        downloadUrl: result.message.downloadUrl.isEmpty
            ? null
            : result.message.downloadUrl,
        releaseNotes: result.message.releaseNotes.isEmpty
            ? null
            : result.message.releaseNotes,
        htmlUrl: result.message.htmlUrl.isEmpty ? null : result.message.htmlUrl,
      );
    } catch (e) {
      Logger.error('检查更新失败: $e');
      return null;
    }
  }
}
