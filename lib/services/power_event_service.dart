import 'dart:async';

import 'package:stelliberty/clash/manager/manager.dart';
import 'package:stelliberty/src/bindings/signals/signals.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 电源事件服务：监听休眠/唤醒，自动重启核心以恢复状态
class PowerEventService {
  static final PowerEventService _instance = PowerEventService._internal();
  factory PowerEventService() => _instance;
  PowerEventService._internal();

  StreamSubscription? _subscription;

  DateTime? _lastRestartAt;
  static const _restartCooldown = Duration(seconds: 10);

  bool _isRestarting = false;
  bool _shouldRestoreAfterResume = false;

  void init() {
    if (_subscription != null) {
      Logger.warning('电源事件服务已初始化，跳过重复初始化');
      return;
    }

    Logger.info('初始化电源事件服务');

    _subscription = SystemPowerEvent.rustSignalStream.listen(
      (signal) {
        _handlePowerEvent(signal.message);
      },
      onError: (Object e, StackTrace stackTrace) {
        Logger.error('电源事件流异常：$e\n$stackTrace');
      },
      onDone: () {
        Logger.warning('电源事件流已关闭');
        _subscription = null;
      },
    );
  }

  void _handlePowerEvent(SystemPowerEvent event) {
    if (event.eventType == PowerEventType.suspend) {
      _shouldRestoreAfterResume = ClashManager.instance.isCoreRunning;
      Logger.info('系统休眠，记录核心状态：${_shouldRestoreAfterResume ? "运行中" : "未运行"}');
      return;
    }

    if (event.eventType == PowerEventType.resumeAutomatic ||
        event.eventType == PowerEventType.resumeSuspend) {
      if (!_shouldRestoreAfterResume) {
        Logger.debug('系统唤醒，核心此前未运行，跳过恢复');
        return;
      }

      unawaited(_restartCore());
    }
  }

  Future<void> _restartCore() async {
    if (_isRestarting) {
      Logger.warning('核心重启进行中，跳过');
      return;
    }

    final now = DateTime.now();
    if (_lastRestartAt != null &&
        now.difference(_lastRestartAt!) < _restartCooldown) {
      Logger.warning(
        '核心重启冷却中（距上次 ${now.difference(_lastRestartAt!).inSeconds} 秒）',
      );
      return;
    }

    _isRestarting = true;
    _shouldRestoreAfterResume = false;

    try {
      final manager = ClashManager.instance;

      if (manager.isCoreRestarting) {
        Logger.warning('核心正在重启中，跳过系统唤醒恢复');
        return;
      }

      _lastRestartAt = now;

      if (manager.isCoreRunning) {
        Logger.info('系统唤醒，开始重启核心以恢复状态');

        final isRestartSuccess = await manager.restartCore();
        if (!isRestartSuccess) {
          Logger.error('系统唤醒核心重启失败：重启返回失败');
          return;
        }

        Logger.info('系统唤醒核心重启完成');
        return;
      }

      Logger.info('系统唤醒，核心未运行，开始启动核心以恢复状态');

      final isStartSuccess = await manager.startCore(
        configPath: manager.currentConfigPath,
        overrides: manager.getOverrides(),
      );
      if (!isStartSuccess) {
        Logger.error('系统唤醒核心启动失败：启动返回失败');
        return;
      }

      Logger.info('系统唤醒核心启动完成');
    } catch (e) {
      Logger.error('系统唤醒核心恢复失败：$e');
    } finally {
      _isRestarting = false;
    }
  }

  void dispose() {
    _subscription?.cancel();
    _subscription = null;
  }
}
