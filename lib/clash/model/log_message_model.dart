import 'package:flutter/material.dart';
import 'package:stelliberty/i18n/i18n.dart';

// Clash 核心日志消息模型
class ClashLogMessage {
  final String type; // INFO, WARNING, ERROR, DEBUG
  final String payload; // 日志内容
  final DateTime timestamp; // 时间戳

  ClashLogMessage({
    required this.type,
    required this.payload,
    DateTime? timestamp,
  }) : timestamp = timestamp ?? DateTime.now();

  // 从 JSON 解析
  factory ClashLogMessage.fromJson(Map<String, dynamic> json) {
    return ClashLogMessage(
      type: json['type'] as String? ?? 'INFO',
      payload: json['payload'] as String? ?? '',
    );
  }

  // 获取日志级别颜色
  ClashLogLevel get level {
    final typeUpper = type.toUpperCase();
    if (typeUpper.contains('ERROR') || typeUpper.contains('FATAL')) {
      return ClashLogLevel.error;
    } else if (typeUpper.contains('WARN')) {
      return ClashLogLevel.warning;
    } else if (typeUpper.contains('DEBUG')) {
      return ClashLogLevel.debug;
    } else {
      return ClashLogLevel.info;
    }
  }

  // 格式化时间戳
  String get formattedTime {
    return '${timestamp.hour.toString().padLeft(2, '0')}:'
        '${timestamp.minute.toString().padLeft(2, '0')}:'
        '${timestamp.second.toString().padLeft(2, '0')}';
  }
}

// Clash 核心日志级别枚举
enum ClashLogLevel {
  debug,
  info,
  warning,
  error,
  silent, // 不显示任何日志
}

extension ClashLogLevelExtension on ClashLogLevel {
  // 转换为 Clash API 参数
  String toApiParam() {
    switch (this) {
      case ClashLogLevel.debug:
        return 'debug';
      case ClashLogLevel.info:
        return 'info';
      case ClashLogLevel.warning:
        return 'warning';
      case ClashLogLevel.error:
        return 'error';
      case ClashLogLevel.silent:
        return 'silent';
    }
  }

  // 获取显示名称
  String getDisplayName(BuildContext context) {
    final trans = context.translate;
    switch (this) {
      case ClashLogLevel.debug:
        return trans.log_level.debug;
      case ClashLogLevel.info:
        return trans.log_level.info;
      case ClashLogLevel.warning:
        return trans.log_level.warning;
      case ClashLogLevel.error:
        return trans.log_level.error;
      case ClashLogLevel.silent:
        return trans.log_level.silent;
    }
  }

  // 从字符串解析
  static ClashLogLevel fromString(String level) {
    switch (level.toLowerCase()) {
      case 'debug':
        return ClashLogLevel.debug;
      case 'info':
        return ClashLogLevel.info;
      case 'warning':
        return ClashLogLevel.warning;
      case 'error':
        return ClashLogLevel.error;
      case 'silent':
        return ClashLogLevel.silent;
      default:
        return ClashLogLevel.info;
    }
  }
}
