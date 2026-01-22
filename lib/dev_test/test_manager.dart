import 'package:flutter/foundation.dart';
import 'package:stelliberty/dev_test/delay_test_stream.dart';
import 'package:stelliberty/dev_test/ipc_api_test.dart';
import 'package:stelliberty/dev_test/override_test.dart';

// 开发测试管理器：按 TEST_TYPE 运行指定测试入口。
// 仅在非 Release 模式启用。
class TestManager {
  // 获取测试类型
  static String? get testType {
    // Release 模式禁用测试
    if (kReleaseMode) {
      return null;
    }

    const type = String.fromEnvironment('TEST_TYPE');
    return type.isEmpty ? null : type;
  }

  // 运行指定类型的测试
  static Future<void> runTest(String testType) async {
    // 双重保险：Release 模式拒绝运行
    if (kReleaseMode) {
      throw Exception('测试模式在 Release 模式下不可用');
    }

    switch (testType) {
      case 'override':
        await OverrideTest.run();
        break;
      case 'ipc-api':
        await IpcApiTest.run();
        break;
      case 'delay-test':
        await DelayTestStream.run();
        break;
      default:
        throw Exception('未知的测试类型: $testType');
    }
  }
}
