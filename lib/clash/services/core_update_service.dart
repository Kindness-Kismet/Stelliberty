import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:http/http.dart' as http;
import 'package:archive/archive.dart';
import 'package:path/path.dart' as p;
import 'package:stelliberty/clash/core/core_channel.dart';
import 'package:stelliberty/utils/logger.dart';

// 更新进度回调：progress (0.0-1.0)，message (当前步骤描述)
typedef ProgressCallback = void Function(double progress, String message);

// 核心更新服务：从 GitHub 下载最新的 Mihomo 核心并替换现有核心
class CoreUpdateService {
  static const String _githubRepo = "MetaCubeX/mihomo";
  static const String _apiBaseUrl = "https://api.github.com/repos";

  // 获取当前安装的核心版本
  static Future<String?> getCurrentCoreVersion({
    required CoreChannel channel,
    String? customPath,
  }) async {
    try {
      final corePath = await getExistingCorePath(
        channel,
        customPath: customPath,
      );

      if (corePath == null) {
        Logger.warning('核心文件不存在，无法获取版本');
        return null;
      }

      // 执行核心文件获取版本信息
      final result = await Process.run(corePath, ['-v']).timeout(
        const Duration(seconds: 3),
        onTimeout: () {
          Logger.warning('获取核心版本超时');
          throw TimeoutException('获取核心版本超时');
        },
      );

      Logger.info('核心版本命令退出码：${result.exitCode}');
      final stdout = result.stdout.toString().trim();
      final stderr = result.stderr.toString().trim();
      Logger.info('核心版本输出 (stdout)：$stdout');
      if (stderr.isNotEmpty) {
        Logger.info('核心版本输出 (stderr)：$stderr');
      }

      if (result.exitCode == 0) {
        // 尝试多种版本号格式匹配
        // 格式1: "Mihomo version x.x.x"
        // 格式2: "Meta version x.x.x"
        // 格式3: "version x.x.x"
        // 格式4: "v1.2.3" 或 "1.2.3"

        // 先尝试匹配 "version x.x.x" 格式
        final versionPattern = r'version\s+v?(\d+\.\d+\.\d+)';
        var versionMatch = RegExp(
          versionPattern,
          caseSensitive: false,
        ).firstMatch(stdout);
        if (versionMatch != null) {
          final version = versionMatch.group(1)!;
          Logger.info('成功解析核心版本：$version');
          return version;
        }

        // 再尝试匹配纯版本号 "v1.2.3" 或 "1.2.3"
        final pureVersionPattern = r'v?(\d+\.\d+\.\d+)';
        versionMatch = RegExp(pureVersionPattern).firstMatch(stdout);
        if (versionMatch != null) {
          final version = versionMatch.group(1)!;
          Logger.info('成功解析核心版本（纯数字格式）：$version');
          return version;
        }

        Logger.warning('无法从输出中解析版本号');
      }

      return null;
    } catch (e) {
      Logger.warning('获取当前核心版本失败：$e');
      return null;
    }
  }

  // 比较两个版本号，返回：-1（v1<v2）, 0（v1==v2）, 1（v1>v2）
  static int compareVersions(String v1, String v2) {
    // 移除可能的 'v' 前缀
    final vPrefixPattern = RegExp(r'^v');
    v1 = v1.replaceFirst(vPrefixPattern, '');
    v2 = v2.replaceFirst(vPrefixPattern, '');

    final parts1 = v1.split('.').map((e) => int.tryParse(e) ?? 0).toList();
    final parts2 = v2.split('.').map((e) => int.tryParse(e) ?? 0).toList();

    final maxLength = parts1.length > parts2.length
        ? parts1.length
        : parts2.length;

    for (int i = 0; i < maxLength; i++) {
      final p1 = i < parts1.length ? parts1[i] : 0;
      final p2 = i < parts2.length ? parts2[i] : 0;

      if (p1 < p2) return -1;
      if (p1 > p2) return 1;
    }

    return 0;
  }

  // 下载核心文件，成功返回新版本号和解压后的核心字节
  // 返回 (version, coreBytes) 元组，调用方负责停止核心后替换文件
  static Future<(String, List<int>)> downloadCore({
    required CoreChannel channel,
    ProgressCallback? onProgress,
  }) async {
    if (channel == CoreChannel.custom) {
      throw ArgumentError('自定义渠道不支持自动下载');
    }
    try {
      onProgress?.call(0.0, '获取版本信息');

      // 1. 获取当前平台和架构
      final platform = _getCurrentPlatform();
      final arch = _getCurrentArch();

      // 2. 获取最新版本信息
      final releaseInfo = await getLatestRelease(channel: channel);
      final version = releaseInfo['tag_name'] as String;
      Logger.info('发现新版本：$version');

      onProgress?.call(0.1, '找到版本: $version');

      // 3. 查找对应平台的资源
      final asset = _findAsset(releaseInfo, platform, arch);
      if (asset == null) {
        throw Exception('未找到适用于 $platform-$arch 的核心文件');
      }

      final downloadUrl = asset['browser_download_url'] as String;
      final fileName = asset['name'] as String;

      onProgress?.call(0.2, '开始下载');

      // 4. 下载核心文件
      final coreBytes = await _downloadCore(
        downloadUrl,
        onProgress: (downloaded, total) {
          if (total > 0) {
            final progress = 0.2 + (downloaded / total) * 0.6;
            final mbDownloaded = (downloaded / 1024 / 1024).toStringAsFixed(1);
            final mbTotal = (total / 1024 / 1024).toStringAsFixed(1);
            onProgress?.call(progress, '下载中 $mbDownloaded/$mbTotal MB');
          }
        },
      );

      onProgress?.call(0.8, '解压文件');

      // 5. 解压核心文件
      final extractedBytes = await _extractCore(fileName, coreBytes);

      onProgress?.call(1.0, '下载完成');

      Logger.info('核心下载成功：$version');
      return (version, extractedBytes);
    } catch (e) {
      Logger.error('核心下载失败：$e');
      rethrow;
    }
  }

  // 替换核心文件（在核心停止后调用）
  static Future<void> replaceCore({
    required String coreDir,
    required List<int> coreBytes,
  }) async {
    try {
      await _replaceCore(coreDir, coreBytes);
      Logger.info('核心替换成功');
    } catch (e) {
      Logger.error('核心替换失败：$e');
      rethrow;
    }
  }

  // 获取最新的 Release 信息
  static Future<Map<String, dynamic>> getLatestRelease({
    CoreChannel channel = CoreChannel.stable,
  }) async {
    if (channel == CoreChannel.beta) {
      return _getLatestPrerelease();
    }

    return _getLatestStableRelease();
  }

  static Future<Map<String, dynamic>> _getLatestStableRelease() async {
    final url = Uri.parse('$_apiBaseUrl/$_githubRepo/releases/latest');

    try {
      final response = await http
          .get(url)
          .timeout(
            const Duration(seconds: 10),
            onTimeout: () => throw TimeoutException('获取版本信息超时'),
          );

      if (response.statusCode != 200) {
        throw Exception('获取版本信息失败: HTTP ${response.statusCode}');
      }

      return json.decode(response.body) as Map<String, dynamic>;
    } catch (e) {
      throw Exception('无法连接到 GitHub: $e');
    }
  }

  static Future<Map<String, dynamic>> _getLatestPrerelease() async {
    final url = Uri.parse('$_apiBaseUrl/$_githubRepo/releases?per_page=10');

    try {
      final response = await http
          .get(url)
          .timeout(
            const Duration(seconds: 10),
            onTimeout: () => throw TimeoutException('获取测试版信息超时'),
          );

      if (response.statusCode != 200) {
        throw Exception('获取测试版信息失败: HTTP ${response.statusCode}');
      }

      final releases = (json.decode(response.body) as List<dynamic>)
          .cast<Map<String, dynamic>?>();
      final prerelease = releases.firstWhere(
        (item) => item != null && item['prerelease'] == true,
        orElse: () => null,
      );

      if (prerelease == null) {
        throw Exception('未找到测试版发布');
      }

      return Map<String, dynamic>.from(prerelease);
    } catch (e) {
      throw Exception('无法获取测试版信息: $e');
    }
  }

  // 查找匹配的资源文件
  static Map<String, dynamic>? _findAsset(
    Map<String, dynamic> releaseInfo,
    String platform,
    String arch,
  ) {
    final assets = releaseInfo['assets'] as List;
    final keyword = '$platform-$arch';

    for (final asset in assets) {
      final name = asset['name'] as String;
      if (name.contains(keyword)) {
        return asset as Map<String, dynamic>;
      }
    }

    return null;
  }

  // 下载核心文件（支持进度回调，使用系统代理）
  static Future<List<int>> _downloadCore(
    String url, {
    Function(int downloaded, int total)? onProgress,
  }) async {
    HttpClient? client;
    try {
      // 创建 HttpClient，默认使用系统代理设置
      client = HttpClient();

      final request = await client.getUrl(Uri.parse(url));
      final response = await request.close().timeout(
        const Duration(seconds: 30),
        onTimeout: () => throw TimeoutException('下载超时'),
      );

      if (response.statusCode != 200) {
        throw Exception('下载失败: HTTP ${response.statusCode}');
      }

      final contentLength = response.contentLength;
      final bytes = <int>[];

      await for (final chunk in response) {
        bytes.addAll(chunk);
        onProgress?.call(bytes.length, contentLength);
      }

      return bytes;
    } catch (e) {
      throw Exception('下载失败: $e');
    } finally {
      client?.close();
    }
  }

  // 解压核心文件
  static Future<List<int>> _extractCore(
    String fileName,
    List<int> fileBytes,
  ) async {
    try {
      if (fileName.endsWith('.zip')) {
        final archive = ZipDecoder().decodeBytes(fileBytes);
        final coreFile = archive.firstWhere(
          (file) =>
              file.isFile &&
              (file.name.endsWith('.exe') || !file.name.contains('.')),
          orElse: () => throw Exception('压缩包中未找到可执行文件'),
        );
        return coreFile.content as List<int>;
      } else if (fileName.endsWith('.gz')) {
        return GZipDecoder().decodeBytes(fileBytes);
      } else {
        throw Exception('不支持的文件格式: $fileName');
      }
    } catch (e) {
      throw Exception('解压失败: $e');
    }
  }

  // 替换核心文件：备份旧核心 → 写入新核心 → 设置权限 → 失败时自动回滚
  static Future<void> _replaceCore(String coreDir, List<int> coreBytes) async {
    final platform = _getCurrentPlatform();
    final coreName = platform == 'windows' ? 'clash-core.exe' : 'clash-core';
    final coreFile = File(p.join(coreDir, coreName));
    final backupFile = File(p.join(coreDir, '${coreName}_old'));

    await Directory(coreDir).create(recursive: true);

    try {
      // 1. 备份旧核心
      if (await coreFile.exists()) {
        await coreFile.rename(backupFile.path);
      }

      // 2. 写入新核心
      await coreFile.writeAsBytes(coreBytes);

      // 3. 设置可执行权限（Linux/macOS）
      if (platform != 'windows') {
        final result = await Process.run('chmod', ['+x', coreFile.path]);
        if (result.exitCode != 0) {
          Logger.warning('设置可执行权限失败：${result.stderr}');
        }
      }
    } catch (e) {
      // 如果失败，尝试恢复备份
      if (await backupFile.exists()) {
        try {
          if (await coreFile.exists()) {
            await coreFile.delete();
          }
          await backupFile.rename(coreFile.path);
          Logger.info('已恢复旧核心');
        } catch (restoreError) {
          Logger.error('恢复备份失败：$restoreError');
        }
      }

      throw Exception('替换核心失败: $e');
    }
  }

  // 获取核心文件目录（指定渠道）
  static Future<String> getCoreDirectory(CoreChannel channel) async {
    if (channel == CoreChannel.custom) {
      throw ArgumentError('自定义渠道没有固定目录');
    }

    return _getChannelDirectory(channel);
  }

  // 确保核心存在，不存在时自动下载/复制，返回核心可执行路径
  static Future<String> ensureCorePath({
    required CoreChannel channel,
    String? customPath,
    ProgressCallback? onProgress,
  }) async {
    if (channel == CoreChannel.custom) {
      if (customPath == null || customPath.isEmpty) {
        throw ArgumentError('自定义核心路径未设置');
      }

      final customFile = File(customPath);
      if (!await customFile.exists()) {
        throw Exception('自定义核心文件不存在: $customPath');
      }

      return customFile.path;
    }

    final coreDir = await _getChannelDirectory(channel);
    final coreFileName = _getCoreFileName();
    final targetPath = p.join(coreDir, coreFileName);
    final targetFile = File(targetPath);

    if (await targetFile.exists()) {
      return targetPath;
    }

    // 尝试从内置核心复制（仅稳定渠道）
    if (channel == CoreChannel.stable) {
      final bundledPath = _getBundledCorePath();
      final bundledFile = File(bundledPath);
      if (await bundledFile.exists()) {
        await Directory(coreDir).create(recursive: true);
        await bundledFile.copy(targetPath);
        return targetPath;
      }
    }

    // 下载核心
    onProgress?.call(0.0, '下载核心中');
    final (_, coreBytes) = await downloadCore(
      channel: channel,
      onProgress: onProgress,
    );
    await _replaceCore(coreDir, coreBytes);
    return targetPath;
  }

  // 获取已存在的核心路径（不触发下载）
  static Future<String?> getExistingCorePath(
    CoreChannel channel, {
    String? customPath,
  }) async {
    if (channel == CoreChannel.custom) {
      if (customPath == null || customPath.isEmpty) return null;
      final customFile = File(customPath);
      return await customFile.exists() ? customFile.path : null;
    }

    final coreDir = await _getChannelDirectory(channel);
    final corePath = p.join(coreDir, _getCoreFileName());
    final coreFile = File(corePath);

    if (await coreFile.exists()) {
      return corePath;
    }

    if (channel == CoreChannel.stable) {
      final bundled = _getBundledCorePath();
      final bundledFile = File(bundled);
      if (await bundledFile.exists()) {
        return bundledFile.path;
      }
    }

    return null;
  }

  // 删除备份的旧核心
  static Future<void> deleteOldCore(String coreDir) async {
    final platform = _getCurrentPlatform();
    final coreName = platform == 'windows' ? 'clash-core.exe' : 'clash-core';
    final backupFile = File(p.join(coreDir, '${coreName}_old'));

    if (await backupFile.exists()) {
      try {
        await backupFile.delete();
      } catch (e) {
        Logger.warning('删除旧核心备份失败：$e');
      }
    }
  }

  static Future<String> _getChannelDirectory(CoreChannel channel) async {
    final dir = p.join(_getDataRoot(), 'clash-cores', channel.storageValue);
    final directory = Directory(dir);
    if (!await directory.exists()) {
      await directory.create(recursive: true);
    }
    return dir;
  }

  static String _getBundledCorePath() {
    final exeDir = p.dirname(Platform.resolvedExecutable);
    return p.join(
      exeDir,
      'data',
      'flutter_assets',
      'assets',
      'clash-core',
      _getCoreFileName(),
    );
  }

  static String _getCoreFileName() {
    final platform = _getCurrentPlatform();
    return platform == 'windows' ? 'clash-core.exe' : 'clash-core';
  }

  static String _getDataRoot() {
    return p.join(p.dirname(Platform.resolvedExecutable), 'data');
  }

  // 获取当前平台
  static String _getCurrentPlatform() {
    if (Platform.isWindows) return 'windows';
    if (Platform.isLinux) return 'linux';
    if (Platform.isMacOS) return 'darwin';
    throw Exception('不支持的平台: ${Platform.operatingSystem}');
  }

  // 获取当前架构（通过 Platform.version 推断）
  static String _getCurrentArch() {
    final is64Bit =
        Platform.version.contains('x64') ||
        Platform.version.contains('aarch64') ||
        !Platform.version.contains('x86');

    if (Platform.isWindows || Platform.isLinux) {
      return is64Bit ? 'amd64' : 'amd64'; // 默认 amd64
    }

    if (Platform.isMacOS) {
      // macOS 可能是 amd64 或 arm64
      return Platform.version.contains('arm64') ||
              Platform.version.contains('aarch64')
          ? 'arm64'
          : 'amd64';
    }

    return 'amd64'; // 默认值
  }
}
