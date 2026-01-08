import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:flutter_single_instance/flutter_single_instance.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 确保应用单实例运行
// Debug/Profile 模式跳过单实例检查，允许与 Release 版本共存
// Release 模式强制单实例
Future<void> ensureSingleInstance() async {
  // Debug 和 Profile 模式允许多实例（与 Release 共存）
  if (kDebugMode || kProfileMode) {
    final mode = kDebugMode ? 'Debug' : 'Profile';
    Logger.info("$mode 模式，跳过单实例检查");
    return;
  }

  // Release 模式：强制单实例
  if (!await FlutterSingleInstance().isFirstInstance()) {
    Logger.info("检测到新 Release 实例，禁止启动");
    final err = await FlutterSingleInstance().focus();
    if (err != null) {
      Logger.error("聚焦运行实例时出错：$err");
    }
    exit(0);
  }

  Logger.info("单实例检查通过（Release 模式）");
}
