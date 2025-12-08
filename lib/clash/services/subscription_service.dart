import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'package:http/http.dart' as http;
import 'package:http/io_client.dart';
import 'package:stelliberty/clash/data/subscription_model.dart';
import 'package:stelliberty/clash/data/override_model.dart' as app_override;
import 'package:stelliberty/clash/services/override_service.dart';
import 'package:stelliberty/clash/services/override_applicator.dart';
import 'package:stelliberty/clash/services/dns_service.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/services/path_service.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';

// 订阅服务
// 负责订阅的下载、保存、验证等操作
class SubscriptionService {
  // 覆写应用器
  OverrideApplicator? _overrideApplicator;

  // 覆写配置获取回调
  Future<List<app_override.OverrideConfig>> Function(List<String>)?
  _getOverridesByIds;

  // 设置覆写服务
  void setOverrideService(OverrideService service) {
    _overrideApplicator = OverrideApplicator(service);
    Logger.info('覆写服务已设置到 SubscriptionService');
  }

  // 设置覆写配置获取回调
  void setOverrideGetter(
    Future<List<app_override.OverrideConfig>> Function(List<String>) getter,
  ) {
    _getOverridesByIds = getter;
    Logger.info('覆写获取器已设置到 SubscriptionService');
  }

  // 获取覆写配置
  // 通过覆写 ID 列表获取完整的覆写配置
  Future<List<app_override.OverrideConfig>> getOverridesByIds(
    List<String> ids,
  ) async {
    if (_getOverridesByIds == null) {
      Logger.warning('覆写配置获取回调未设置');
      return [];
    }
    return await _getOverridesByIds!(ids);
  }

  // 初始化服务
  // baseDir 参数保留以向后兼容，但实际不再使用
  Future<void> initialize(String baseDir) async {
    // 目录创建已由 PathService 统一管理
    // 这里只需记录日志
    final subscriptionDir = PathService.instance.subscriptionsDir;
    Logger.info('订阅服务初始化完成，路径：$subscriptionDir');
  }

  // 下载订阅配置
  // 返回更新后的订阅对象
  Future<Subscription> downloadSubscription(Subscription subscription) async {
    Logger.info('开始下载订阅：${subscription.name} (${subscription.url})');

    // 判断 Clash 是否运行
    final isClashRunning = ClashManager.instance.isCoreRunning;

    // 确定实际使用的代理模式
    final effectiveProxyMode = isClashRunning
        ? subscription.proxyMode
        : SubscriptionProxyMode.direct;

    if (!isClashRunning &&
        subscription.proxyMode != SubscriptionProxyMode.direct) {
      Logger.warning(
        'Clash 未运行，强制使用直连模式（用户配置: ${subscription.proxyMode.displayName}）',
      );
    }

    Logger.info('代理模式：${effectiveProxyMode.displayName}');

    // 根据代理模式创建 HTTP 客户端
    final client = _createHttpClient(effectiveProxyMode);

    try {
      // 获取订阅的 User-Agent
      final userAgent = subscription.userAgent;

      // 发送 HTTP GET 请求
      final response = await client
          .get(Uri.parse(subscription.url), headers: {'User-Agent': userAgent})
          .timeout(
            Duration(seconds: ClashDefaults.subscriptionDownloadTimeout),
          );

      if (response.statusCode != 200) {
        throw Exception(
          'HTTP ${response.statusCode}: ${response.reasonPhrase}',
        );
      }

      // 解析订阅信息
      final subscriptionInfoHeader = response.headers['subscription-userinfo'];
      final info = SubscriptionInfo.fromHeader(subscriptionInfoHeader);

      // 获取配置内容（原始内容）
      String configContent = utf8.decode(response.bodyBytes);

      // 使用 ProxyParser 解析订阅内容（支持标准 YAML、Base64 编码、纯文本代理链接）
      // 创建 Completer 等待解析结果
      final completer = Completer<String>();
      StreamSubscription? streamListener;

      try {
        // 订阅 Rust 信号流
        streamListener = ParseSubscriptionResponse.rustSignalStream.listen((
          result,
        ) {
          if (!completer.isCompleted) {
            if (result.message.success) {
              completer.complete(result.message.parsedConfig);
            } else {
              completer.completeError(Exception(result.message.errorMessage));
            }
          }
        });

        // 发送解析请求到 Rust
        final parseRequest = ParseSubscriptionRequest(content: configContent);
        parseRequest.sendSignalToRust();

        // 等待解析结果
        final parsedConfigContent = await completer.future.timeout(
          const Duration(seconds: 30),
          onTimeout: () {
            throw Exception('订阅解析超时');
          },
        );

        // 验证配置文件
        _validateConfig(parsedConfigContent);

        // 【重要】保存原始订阅文件，不应用任何覆写
        // 覆写将在生成 runtime_config.yaml 时应用
        final configPath = PathService.instance.getSubscriptionConfigPath(
          subscription.id,
        );
        final configFile = File(configPath);
        // 确保父目录存在
        await configFile.parent.create(recursive: true);
        await configFile.writeAsString(parsedConfigContent);

        Logger.info('订阅下载成功（已保存原始配置）：${subscription.name}');

        // 返回更新后的订阅
        return subscription.copyWith(
          lastUpdateTime: DateTime.now(),
          info: info,
          isUpdating: false,
        );
      } finally {
        // 停止监听信号流（即使发生异常）
        await streamListener?.cancel();
      }
    } catch (e) {
      Logger.error('下载订阅失败：${subscription.name} - $e');
      rethrow;
    } finally {
      // 确保客户端被关闭
      client.close();
    }
  }

  // 根据代理模式创建 HTTP 客户端
  http.Client _createHttpClient(SubscriptionProxyMode proxyMode) {
    switch (proxyMode) {
      case SubscriptionProxyMode.direct:
        // 直连：使用默认客户端
        Logger.debug('使用直连模式');
        return http.Client();

      case SubscriptionProxyMode.system:
        // 系统代理：使用系统环境变量配置的代理
        Logger.debug('使用系统代理模式');
        final httpClient = HttpClient();
        httpClient.findProxy = HttpClient.findProxyFromEnvironment;
        return IOClient(httpClient);

      case SubscriptionProxyMode.core:
        // 核心代理：使用 Clash 的混合端口作为代理
        final mixedPort = ClashPreferences.instance.getMixedPort();
        final proxyUrl = 'PROXY 127.0.0.1:$mixedPort';
        Logger.debug('使用核心代理模式：$proxyUrl');

        final httpClient = HttpClient();
        httpClient.findProxy = (uri) => proxyUrl;
        return IOClient(httpClient);
    }
  }

  // 验证配置文件格式
  // 增强验证：检查 YAML 基本语法和必需字段
  void _validateConfig(String content) {
    if (content.isEmpty) {
      throw Exception('配置文件为空');
    }

    // 1. 检查必需字段
    final hasProxies = content.contains('proxies:');
    final hasProxyGroups = content.contains('proxy-groups:');

    if (!hasProxies && !hasProxyGroups) {
      throw Exception('配置文件格式不正确，缺少 proxies 或 proxy-groups 字段');
    }

    // 2. 基本的 YAML 语法检查
    // 检查是否有明显的格式错误
    final lines = content.split('\n');
    for (int i = 0; i < lines.length; i++) {
      final line = lines[i];
      final trimmed = line.trimLeft();

      // 跳过空行和注释
      if (trimmed.isEmpty || trimmed.startsWith('#')) {
        continue;
      }

      // 检查缩进是否使用了 Tab（YAML 不允许）
      if (line.startsWith('\t')) {
        throw Exception('配置文件格式错误：第 ${i + 1} 行使用了 Tab 缩进，YAML 只允许空格缩进');
      }
    }

    // 3. 检查是否有基本的代理节点（如果有 proxies 字段）
    if (hasProxies) {
      // 简单检查：proxies 后面应该有内容
      final proxiesIndex = content.indexOf('proxies:');
      final afterProxies = content
          .substring(proxiesIndex + 'proxies:'.length)
          .trim();
      if (afterProxies.isEmpty || afterProxies.startsWith('proxy-groups:')) {
        throw Exception('配置文件 proxies 字段为空，没有有效的代理节点');
      }
    }

    Logger.info('配置文件验证通过（${lines.length} 行）');
  }

  // 读取订阅配置文件内容
  Future<String> readSubscriptionConfig(Subscription subscription) async {
    final configPath = PathService.instance.getSubscriptionConfigPath(
      subscription.id,
    );
    final configFile = File(configPath);

    if (!await configFile.exists()) {
      throw Exception('配置文件不存在: $configPath');
    }

    return await configFile.readAsString();
  }

  // 读取应用覆写后的订阅配置
  // 这是提供给 Clash 核心使用的最终配置
  // 注意：订阅文件已包含 DNS 和规则覆写，此方法仅用于兼容性读取
  Future<String> readSubscriptionConfigWithOverrides(
    Subscription subscription,
  ) async {
    // 直接读取订阅配置（已包含所有覆写）
    return await readSubscriptionConfig(subscription);
  }

  // 检查订阅配置文件是否存在
  Future<bool> subscriptionExists(Subscription subscription) async {
    final configPath = PathService.instance.getSubscriptionConfigPath(
      subscription.id,
    );
    return await File(configPath).exists();
  }

  // 删除订阅配置文件
  Future<void> deleteSubscription(Subscription subscription) async {
    final configPath = PathService.instance.getSubscriptionConfigPath(
      subscription.id,
    );
    final configFile = File(configPath);

    if (await configFile.exists()) {
      await configFile.delete();
      Logger.info('已删除订阅配置：${subscription.name}');
    }

    // 清理该订阅相关的所有节点选择持久化数据
    await ClashPreferences.instance.clearProxySelectionsForSubscription(
      subscription.id,
    );
    Logger.info('已清理订阅 ${subscription.name} 的所有节点选择数据');
  }

  // 保存订阅列表到 JSON
  Future<void> saveSubscriptionList(List<Subscription> subscriptions) async {
    final listPath = PathService.instance.subscriptionListPath;
    final listFile = File(listPath);

    final jsonData = {
      'subscriptions': subscriptions.map((s) => s.toJson()).toList(),
      'version': 1,
    };

    await listFile.writeAsString(json.encode(jsonData));
    Logger.info('已保存订阅列表，共 ${subscriptions.length} 个订阅');
  }

  // 从 JSON 加载订阅列表
  Future<List<Subscription>> loadSubscriptionList() async {
    final listPath = PathService.instance.subscriptionListPath;
    final listFile = File(listPath);

    if (!await listFile.exists()) {
      Logger.info('订阅列表文件不存在，返回空列表');
      return [];
    }

    try {
      final content = await listFile.readAsString();
      final jsonData = json.decode(content) as Map<String, dynamic>;
      final subscriptionsJson = jsonData['subscriptions'] as List;

      final subscriptions = subscriptionsJson
          .map((json) => Subscription.fromJson(json))
          .toList();

      // 验证订阅文件是否存在，并移除无效的订阅
      final validSubscriptions = <Subscription>[];
      bool hasInvalidSubscriptions = false;
      for (final sub in subscriptions) {
        if (await subscriptionExists(sub)) {
          validSubscriptions.add(sub);
        } else {
          Logger.warning('订阅配置文件不存在，已移除：${sub.name}');
          hasInvalidSubscriptions = true;
        }
      }

      // 如果有无效订阅被移除，则更新 list.json
      if (hasInvalidSubscriptions) {
        await saveSubscriptionList(validSubscriptions);
        Logger.info('订阅列表已更新，移除了无效的订阅');
      }

      Logger.info('已加载订阅列表，共 ${validSubscriptions.length} 个订阅');
      return validSubscriptions;
    } catch (e) {
      Logger.error('加载订阅列表失败：$e');
      return [];
    }
  }

  // 保存本地订阅文件
  // 将本地文件内容保存到订阅目录
  Future<void> saveLocalSubscription(
    Subscription subscription,
    String content,
  ) async {
    // 验证配置文件格式
    _validateConfig(content);

    // 【重要】保存原始订阅文件，不应用任何覆写
    // 覆写将在生成 runtime_config.yaml 时应用
    final configPath = PathService.instance.getSubscriptionConfigPath(
      subscription.id,
    );
    final configFile = File(configPath);

    // 确保父目录存在
    await configFile.parent.create(recursive: true);
    await configFile.writeAsString(content);

    Logger.info('本地订阅保存成功（已保存原始配置）：${subscription.name}');
  }

  // 批量更新所有需要更新的订阅
  Future<List<String>> autoUpdateSubscriptions(
    List<Subscription> subscriptions,
  ) async {
    final errors = <String>[];

    for (final subscription in subscriptions) {
      if (!subscription.needsUpdate) {
        Logger.info('订阅无需更新：${subscription.name}');
        continue;
      }

      try {
        await downloadSubscription(subscription);
      } catch (e) {
        final errorMsg = '${subscription.name}: $e';
        errors.add(errorMsg);
        Logger.error('自动更新订阅失败：$errorMsg');
      }
    }

    return errors;
  }

  // 应用所有覆写（DNS 覆写 → 规则覆写）
  // 确保规则覆写优先级高于 DNS 覆写
  //
  // 【重要】此方法用于在生成 runtime_config.yaml 时应用覆写
  // 不会修改原始订阅文件
  Future<String> applyAllOverrides(
    String baseConfig,
    Subscription subscription,
  ) async {
    Logger.debug('开始应用覆写');
    Logger.debug('订阅名称：${subscription.name}');
    Logger.debug('订阅覆写 ID 列表：${subscription.overrideIds}');

    String result = baseConfig;

    try {
      // 步骤 1：应用 DNS 覆写（如果启用）
      final dnsOverrideEnabled = ClashPreferences.instance
          .getDnsOverrideEnabled();
      Logger.debug('DNS 覆写启用状态：$dnsOverrideEnabled');

      if (dnsOverrideEnabled) {
        final dnsService = DnsService.instance;
        if (dnsService.configExists()) {
          Logger.info('应用 DNS 覆写到订阅：${subscription.name}');
          final dnsConfig = await dnsService.loadDnsConfig();
          if (dnsConfig != null && _overrideApplicator != null) {
            final dnsMap = dnsConfig.toMap();
            Logger.debug('DNS 配置：${dnsMap.keys.toList()}');
            // 将 DNS 配置作为 YAML 字符串应用
            result = await _overrideApplicator!.applyYamlOverride(
              result,
              dnsMap,
            );
            Logger.info('DNS 覆写应用成功');
          }
        } else {
          Logger.warning('DNS 覆写已启用但配置不存在');
        }
      } else {
        Logger.debug('跳过 DNS 覆写（未启用）');
      }

      // 步骤 2：应用规则覆写（如果有）
      Logger.debug('检查规则覆写...');
      Logger.debug('- overrideIds 数量：${subscription.overrideIds.length}');
      Logger.debug(
        '- _overrideApplicator 是否为空: ${_overrideApplicator == null}',
      );
      Logger.debug('- _getOverridesByIds 是否为空：${_getOverridesByIds == null}');

      if (subscription.overrideIds.isNotEmpty &&
          _overrideApplicator != null &&
          _getOverridesByIds != null) {
        Logger.info('准备应用规则覆写到订阅：${subscription.name}');
        Logger.debug('覆写 ID 列表：${subscription.overrideIds}');

        // 从覆写 ID 列表获取完整的覆写配置
        final overrides = await _getOverridesByIds!(subscription.overrideIds);
        Logger.debug('获取到 ${overrides.length} 个覆写配置');

        if (overrides.isNotEmpty) {
          for (var i = 0; i < overrides.length; i++) {
            final override = overrides[i];
            Logger.debug(
              '覆写 ${i + 1}: ${override.name} (${override.format.displayName})',
            );
          }

          result = await _overrideApplicator!.applyOverrides(result, overrides);
          Logger.info('规则覆写应用成功：${overrides.length} 个覆写');
        } else {
          Logger.warning('overrideIds 非空，但未获取到任何覆写配置');
        }
      } else {
        Logger.debug('跳过规则覆写（未配置或服务未初始化）');
      }

      Logger.debug('覆写应用完成');
      return result;
    } catch (e) {
      Logger.error('应用覆写失败，返回原始配置：$e');
      return baseConfig;
    }
  }
}
