import 'dart:io';
import 'package:stelliberty/clash/data/override_model.dart';
import 'package:stelliberty/clash/data/subscription_model.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart' as signals;
import 'package:stelliberty/clash/config/clash_defaults.dart';

// 覆写文件服务
class OverrideService {
  // 覆写目录路径（从 PathService 获取）
  String get overridesDir => PathService.instance.overridesDir;

  // 初始化覆写目录
  Future<void> initialize() async {
    // 目录创建已由 PathService 统一管理
    // 这里只需记录日志
    Logger.info('覆写服务初始化完成，路径：$overridesDir');
  }

  // 保存本地覆写（复制文件内容）
  Future<String> saveLocalOverride(
    OverrideConfig config,
    String sourceFilePath,
  ) async {
    try {
      final sourceFile = File(sourceFilePath);
      if (!await sourceFile.exists()) {
        throw Exception('源文件不存在: $sourceFilePath');
      }

      // 读取源文件内容
      final content = await sourceFile.readAsString();

      // 保存到覆写目录
      final targetPath = _getOverridePath(config.id, config.format);
      final targetFile = File(targetPath);
      await targetFile.writeAsString(content);

      Logger.info('本地覆写已保存：${config.name} -> $targetPath');
      return content;
    } catch (e) {
      Logger.error('保存本地覆写失败：${config.name} - $e');
      rethrow;
    }
  }

  // 保存覆写内容（直接使用提供的内容）
  Future<void> saveOverrideContent(
    OverrideConfig config,
    String content,
  ) async {
    try {
      // 保存到覆写目录
      final targetPath = _getOverridePath(config.id, config.format);
      final targetFile = File(targetPath);
      await targetFile.writeAsString(content);

      Logger.info('覆写内容已保存：${config.name} -> $targetPath');
    } catch (e) {
      Logger.error('保存覆写内容失败：${config.name} - $e');
      rethrow;
    }
  }

  // 下载远程覆写（支持三种代理模式）
  Future<String> downloadRemoteOverride(OverrideConfig config) async {
    if (config.url == null || config.url!.isEmpty) {
      throw Exception('远程 URL 为空');
    }

    // 判断 Clash 是否运行
    final isClashRunning = ClashManager.instance.isCoreRunning;

    // 确定实际使用的代理模式
    final effectiveProxyMode = isClashRunning
        ? config.proxyMode
        : SubscriptionProxyMode.direct;

    if (!isClashRunning && config.proxyMode != SubscriptionProxyMode.direct) {
      Logger.warning('Clash 未运行，强制使用直连模式（用户配置：${config.proxyMode.value}）');
    }

    try {
      // 获取默认 User-Agent
      final userAgent = ClashPreferences.instance.getDefaultUserAgent();
      final mixedPort = ClashPreferences.instance.getMixedPort();

      // 转换代理模式
      final proxyMode = _convertProxyMode(effectiveProxyMode);

      // 发送下载请求到 Rust 层
      signals.DownloadOverrideRequest(
        url: config.url!,
        proxyMode: proxyMode,
        userAgent: userAgent,
        timeoutSeconds: signals.Uint64(BigInt.from(ClashDefaults.overrideDownloadTimeout)),
        mixedPort: mixedPort,
      ).sendSignalToRust();

      // 等待 Rust 响应
      final responseStream = signals.DownloadOverrideResponse.rustSignalStream;
      final response = await responseStream.first;

      if (!response.message.success) {
        throw Exception(response.message.errorMessage ?? '下载失败');
      }

      final content = response.message.content;
      if (content.isEmpty) {
        throw Exception('下载的内容为空');
      }

      // 保存到本地
      final targetPath = _getOverridePath(config.id, config.format);
      final targetFile = File(targetPath);
      await targetFile.writeAsString(content);

      Logger.debug('覆写已保存至：$targetPath');
      return content;
    } catch (e) {
      Logger.error('下载远程覆写失败：${config.name} - $e');
      rethrow;
    }
  }

  // 转换代理模式枚举
  signals.ProxyMode _convertProxyMode(SubscriptionProxyMode mode) {
    switch (mode) {
      case SubscriptionProxyMode.direct:
        return signals.ProxyMode.direct;
      case SubscriptionProxyMode.system:
        return signals.ProxyMode.system;
      case SubscriptionProxyMode.core:
        return signals.ProxyMode.core;
    }
  }

  // 读取覆写文件内容
  Future<String> getOverrideContent(String id, OverrideFormat format) async {
    try {
      final filePath = _getOverridePath(id, format);
      final file = File(filePath);

      if (!await file.exists()) {
        Logger.warning('覆写文件不存在：$filePath');
        return '';
      }

      return await file.readAsString();
    } catch (e) {
      Logger.error('读取覆写文件失败：$id - $e');
      return '';
    }
  }

  // 删除覆写文件
  Future<void> deleteOverride(String id, OverrideFormat format) async {
    try {
      final filePath = _getOverridePath(id, format);
      final file = File(filePath);

      if (await file.exists()) {
        await file.delete();
        Logger.info('覆写文件已删除：$filePath');
      }
    } catch (e) {
      Logger.error('删除覆写文件失败：$id - $e');
      rethrow;
    }
  }

  // 检查覆写文件是否存在
  Future<bool> overrideExists(String id, OverrideFormat format) async {
    final filePath = _getOverridePath(id, format);
    return await File(filePath).exists();
  }

  // 获取覆写文件路径
  String _getOverridePath(String id, OverrideFormat format) {
    final ext = format == OverrideFormat.yaml ? 'yaml' : 'js';
    return PathService.instance.getOverridePath(id, ext);
  }
}
