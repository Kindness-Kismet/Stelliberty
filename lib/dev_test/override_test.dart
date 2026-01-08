import 'dart:io';
import 'package:path/path.dart' as path;
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// 覆写测试
//
// 测试 YAML 和 JavaScript 覆写功能
class OverrideTest {
  // 运行覆写测试流程
  static Future<void> run() async {
    Logger.info('覆写测试启动');

    try {
      // 1. 检查测试目录
      final testDir = Directory('assets/test');
      final configFile = File(path.join(testDir.path, 'config', 'test.yaml'));
      final overDir = Directory(path.join(testDir.path, 'override'));
      final outputDir = Directory(path.join(testDir.path, 'output'));
      final outputFile = File(path.join(outputDir.path, 'final.yaml'));

      if (!testDir.existsSync()) {
        Logger.error('Test 目录不存在');
        _printUsage();
        exit(1);
      }

      if (!configFile.existsSync()) {
        Logger.error('测试配置文件不存在: ${configFile.path}');
        exit(1);
      }

      // 创建输出目录
      if (!outputDir.existsSync()) {
        outputDir.createSync(recursive: true);
      }

      // 2. 读取基础配置
      Logger.info('📄 读取基础配置: ${configFile.path}');
      String rawContent = await configFile.readAsString();
      Logger.info('基础配置长度: ${rawContent.length} 字节');

      // 3. 检查并解析订阅格式（支持 base64）
      Logger.info('检查订阅格式...');
      String currentConfig = await _parseSubscriptionContent(rawContent);
      Logger.info('解析后配置长度: ${currentConfig.length} 字节');

      // 4. 扫描并应用覆写
      if (!overDir.existsSync()) {
        Logger.warning('override 目录不存在，跳过覆写');
      } else {
        final overrideFiles = await _scanOverrideFiles(overDir);

        if (overrideFiles.isEmpty) {
          Logger.warning('未发现覆写文件');
        } else {
          currentConfig = await _applyOverrides(currentConfig, overrideFiles);
        }
      }

      // 5. 写入最终配置
      Logger.info('写入最终配置: ${outputFile.path}');
      await outputFile.writeAsString(currentConfig);
      Logger.info('最终配置已保存');

      // 6. 启动 Clash 核心测试
      await _testWithClashCore(outputFile);

      Logger.info('覆写测试完成');

      exit(0);
    } catch (e, stack) {
      Logger.error('覆写测试失败: $e');
      Logger.error('堆栈: $stack');
      exit(1);
    }
  }

  // 解析订阅内容（支持 base64 编码）
  static Future<String> _parseSubscriptionContent(String content) async {
    // 检查是否为 base64 订阅
    final trimmed = content.trim();
    final isBase64 =
        !trimmed.contains('\n') &&
        trimmed.length > 50 &&
        RegExp(r'^[A-Za-z0-9+/=]+$').hasMatch(trimmed);

    if (!isBase64) {
      Logger.info('检测到标准 YAML 配置');
      return content;
    }

    Logger.info('检测到 Base64 编码订阅，调用 Rust 解析...');

    // 调用 Rust 解析订阅
    final request = ParseSubscriptionRequest(
      requestId: 'test-parse-${DateTime.now().millisecondsSinceEpoch}',
      content: content,
    );
    request.sendSignalToRust();

    // 等待响应
    final response = await ParseSubscriptionResponse.rustSignalStream.first
        .timeout(
          const Duration(seconds: 30),
          onTimeout: () {
            throw Exception('Rust 订阅解析超时（30秒）');
          },
        );

    final result = response.message;

    if (!result.isSuccessful) {
      throw Exception('订阅解析失败: ${result.errorMessage}');
    }

    Logger.info('Base64 订阅解析成功');
    return result.parsedConfig;
  }

  // 扫描覆写文件
  static Future<List<File>> _scanOverrideFiles(Directory overDir) async {
    final files = <File>[];
    await for (final entity in overDir.list()) {
      if (entity is File) {
        final ext = path.extension(entity.path).toLowerCase();
        if (ext == '.yaml' || ext == '.yml' || ext == '.js') {
          files.add(entity);
        }
      }
    }

    // 按文件名排序
    files.sort((a, b) => a.path.compareTo(b.path));

    Logger.info('发现 ${files.length} 个覆写文件');
    for (var i = 0; i < files.length; i++) {
      Logger.info('   [${i + 1}] ${path.basename(files[i].path)}');
    }

    return files;
  }

  // 应用所有覆写
  static Future<String> _applyOverrides(
    String baseConfig,
    List<File> overrideFiles,
  ) async {
    final overrideConfigs = <OverrideConfig>[];

    // 准备覆写配置
    for (var i = 0; i < overrideFiles.length; i++) {
      final file = overrideFiles[i];
      final fileName = path.basename(file.path);
      final content = await file.readAsString();
      final ext = path.extension(file.path).toLowerCase();

      Logger.info('📌 [${i + 1}/${overrideFiles.length}] 准备: $fileName');

      overrideConfigs.add(
        OverrideConfig(
          id: fileName,
          name: fileName,
          format: ext == '.js'
              ? OverrideFormat.javascript
              : OverrideFormat.yaml,
          content: content,
        ),
      );
    }

    // 调用 Rust 处理所有覆写
    Logger.info('调用 Rust 处理 ${overrideConfigs.length} 个覆写...');

    final request = ApplyOverridesRequest(
      baseConfigContent: baseConfig,
      overrides: overrideConfigs,
    );

    // 发送请求到 Rust
    request.sendSignalToRust();

    // 等待响应（设置超时）
    final response = await ApplyOverridesResponse.rustSignalStream.first
        .timeout(
          const Duration(seconds: 30),
          onTimeout: () {
            throw Exception('Rust 覆写处理超时（30秒）');
          },
        );

    final result = response.message;

    if (!result.isSuccessful) {
      throw Exception('Rust 覆写处理失败: ${result.errorMessage}');
    }

    Logger.info('Rust 覆写处理成功');
    Logger.info('   最终配置长度: ${result.resultConfig.length} 字节');

    return result.resultConfig;
  }

  // 使用 Clash 核心测试配置
  static Future<void> _testWithClashCore(File configFile) async {
    Logger.info('启动 Clash 核心测试');

    final clashPath = _findClashExecutable();
    if (clashPath == null) {
      Logger.error('未找到 Clash 可执行文件');
      throw Exception('未找到 Clash 可执行文件');
    }

    // 开发模式下直接使用 assets 目录中的数据
    final geoDataDir = path.join('assets', 'clash-core', 'data');

    // 验证数据目录存在
    if (!Directory(geoDataDir).existsSync()) {
      Logger.error('Geodata 目录不存在: $geoDataDir');
      throw Exception('Geodata 目录不存在');
    }

    Logger.info('📍 Clash: $clashPath');
    Logger.info('📍 配置: ${configFile.absolute.path}');
    Logger.info('📍 数据目录: $geoDataDir');
    Logger.info('⏳ 启动中，按 Ctrl+C 停止...');

    final process = await Process.start(clashPath, [
      '-f',
      configFile.absolute.path,
      '-d',
      geoDataDir,
    ], mode: ProcessStartMode.inheritStdio);

    final exitCode = await process.exitCode;

    if (exitCode == 0) {
      Logger.info('Clash 正常退出');
    } else {
      Logger.error('Clash 异常退出，退出码: $exitCode');
      throw Exception('Clash 异常退出');
    }
  }

  // 查找 Clash 可执行文件（使用与 ProcessService 相同的逻辑）
  static String? _findClashExecutable() {
    final String fileName;
    if (Platform.isWindows) {
      fileName = 'clash-core.exe';
    } else if (Platform.isMacOS || Platform.isLinux) {
      fileName = 'clash-core';
    } else {
      return null;
    }

    // 获取可执行文件所在目录
    final exeDir = path.dirname(Platform.resolvedExecutable);

    // 构建 flutter_assets/assets/clash-core 路径
    final executablePath = path.join(
      exeDir,
      'data',
      'flutter_assets',
      'assets',
      'clash-core',
      fileName,
    );

    final executableFile = File(executablePath);

    if (executableFile.existsSync()) {
      return executablePath;
    }

    // 开发模式下的备用路径（直接在 assets 目录）
    final devPath = path.join('assets', 'clash-core', fileName);
    if (File(devPath).existsSync()) {
      return devPath;
    }

    return null;
  }

  // 打印使用说明
  static void _printUsage() {
    Logger.info('');
    Logger.info('覆写测试需要以下目录结构：');
    Logger.info('');
    Logger.info('assets/test/');
    Logger.info('├── config/');
    Logger.info('│   └── test.yaml       # 基础配置文件（支持标准 YAML 或 Base64）');
    Logger.info('├── override/');
    Logger.info('│   ├── 01_dns.yaml    # YAML 覆写（可选）');
    Logger.info('│   ├── 02_proxy.js    # JavaScript 覆写（可选）');
    Logger.info('│   └── ...            # 更多覆写文件');
    Logger.info('└── output/');
    Logger.info('    └── final.yaml     # 最终输出（自动生成）');
    Logger.info('');
  }
}
