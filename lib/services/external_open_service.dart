import 'dart:io';

import 'package:open_filex/open_filex.dart';
import 'package:path/path.dart' as path;
import 'package:stelliberty/utils/logger.dart';

enum ExternalOpenErrorType {
  fileNotFound,
  directoryNotFound,
  unsupportedPlatform,
  processFailed,
  unknown,
}

class ExternalOpenResult {
  final bool isSuccessful;
  final ExternalOpenErrorType? errorType;
  final String? errorDetails;

  const ExternalOpenResult.success()
    : isSuccessful = true,
      errorType = null,
      errorDetails = null;

  const ExternalOpenResult.failure(this.errorType, {this.errorDetails})
    : isSuccessful = false;
}

// 外部打开服务，调用系统默认程序打开文件或目录
class ExternalOpenService {
  ExternalOpenService._();

  static Future<ExternalOpenResult> openFile(String filePath) async {
    final file = File(filePath);
    if (!await file.exists()) {
      Logger.warning('外部打开失败，文件不存在: ${path.basename(filePath)}');
      return const ExternalOpenResult.failure(
        ExternalOpenErrorType.fileNotFound,
      );
    }

    Logger.info('外部打开文件: ${path.basename(filePath)}');
    final result = await _openPath(filePath);
    if (result.isSuccessful) {
      Logger.info('外部打开成功: ${path.basename(filePath)}');
    }
    return result;
  }

  static Future<ExternalOpenResult> openDirectory(String dirPath) async {
    final dir = Directory(dirPath);
    if (!await dir.exists()) {
      Logger.warning('外部打开失败，目录不存在: ${path.basename(dirPath)}');
      return const ExternalOpenResult.failure(
        ExternalOpenErrorType.directoryNotFound,
      );
    }

    Logger.info('外部打开目录: ${path.basename(dirPath)}');
    final result = await _openPath(dirPath);
    if (result.isSuccessful) {
      Logger.info('外部打开成功: ${path.basename(dirPath)}');
    }
    return result;
  }

  static Future<ExternalOpenResult> _openPath(String targetPath) async {
    try {
      Logger.info('使用 open_filex 打开: ${path.basename(targetPath)}');
      final result = await OpenFilex.open(targetPath);

      final platformName = Platform.isAndroid
          ? 'Android'
          : Platform.isIOS
          ? 'iOS'
          : Platform.isWindows
          ? 'Windows'
          : Platform.isMacOS
          ? 'macOS'
          : Platform.isLinux
          ? 'Linux'
          : '未知平台';

      Logger.debug(
        '$platformName 打开结果: type=${result.type}, message=${result.message}',
      );

      if (result.type == ResultType.done) {
        return const ExternalOpenResult.success();
      } else if (result.type == ResultType.noAppToOpen) {
        Logger.warning('没有应用可以打开此文件');
        return ExternalOpenResult.failure(
          ExternalOpenErrorType.processFailed,
          errorDetails: '没有应用可以打开此文件',
        );
      } else if (result.type == ResultType.fileNotFound) {
        Logger.warning('文件不存在');
        return const ExternalOpenResult.failure(
          ExternalOpenErrorType.fileNotFound,
        );
      } else if (result.type == ResultType.permissionDenied) {
        Logger.warning('权限被拒绝');
        return ExternalOpenResult.failure(
          ExternalOpenErrorType.processFailed,
          errorDetails: '权限被拒绝',
        );
      } else {
        Logger.warning('打开失败: ${result.message}');
        return ExternalOpenResult.failure(
          ExternalOpenErrorType.unknown,
          errorDetails: result.message,
        );
      }
    } catch (e) {
      Logger.error('外部打开异常: $e');
      return ExternalOpenResult.failure(
        ExternalOpenErrorType.unknown,
        errorDetails: e.toString(),
      );
    }
  }
}
