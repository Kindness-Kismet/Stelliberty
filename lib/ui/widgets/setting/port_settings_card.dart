import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/config/clash_defaults.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_text_field.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/services/log_print_service.dart';

// 端口设置配置卡片
class PortSettingsCard extends StatefulWidget {
  const PortSettingsCard({super.key});

  @override
  State<PortSettingsCard> createState() => _PortSettingsCardState();
}

class _PortSettingsCardState extends State<PortSettingsCard> {
  late final TextEditingController _mixedPortController;
  late final TextEditingController _socksPortController;
  late final TextEditingController _httpPortController;
  late final ClashProvider _clashProvider;

  // 错误状态
  String? _mixedPortError;
  String? _socksPortError;
  String? _httpPortError;

  // 保存状态
  bool _isSaving = false;

  @override
  void initState() {
    super.initState();
    _clashProvider = Provider.of<ClashProvider>(context, listen: false);
    final configState = _clashProvider.configState;
    _mixedPortController = TextEditingController(
      text: configState.mixedPort.toString(),
    );
    _socksPortController = TextEditingController(
      text: configState.socksPort?.toString() ?? '',
    );
    _httpPortController = TextEditingController(
      text: configState.httpPort?.toString() ?? '',
    );
  }

  @override
  void dispose() {
    _mixedPortController.dispose();
    _socksPortController.dispose();
    _httpPortController.dispose();
    super.dispose();
  }

  // 验证端口号
  // [value] 端口字符串
  // [allowEmpty] 是否允许为空（用于可选端口）
  String? _validatePort(String value, {bool allowEmpty = false}) {
    final trans = context.translate;
    if (value.isEmpty) {
      return allowEmpty ? null : trans.port_settings.port_error;
    }

    final port = int.tryParse(value);
    if (port == null) {
      return trans.port_settings.port_invalid;
    }

    if (port < 1 || port > 65535) {
      return trans.port_settings.port_range;
    }

    return null;
  }

  // 处理混合端口验证
  bool _validateMixedPort() {
    final value = _mixedPortController.text;
    final error = _validatePort(value);
    if (error != null) {
      setState(() => _mixedPortError = error);
      return false;
    }
    setState(() => _mixedPortError = null);
    return true;
  }

  // 处理 SOCKS 端口验证
  bool _validateSocksPort() {
    final value = _socksPortController.text;
    if (value.isEmpty) {
      setState(() => _socksPortError = null);
      return true;
    }
    final error = _validatePort(value, allowEmpty: true);
    if (error != null) {
      setState(() => _socksPortError = error);
      return false;
    }
    setState(() => _socksPortError = null);
    return true;
  }

  // 处理 HTTP 端口验证
  bool _validateHttpPort() {
    final value = _httpPortController.text;
    if (value.isEmpty) {
      setState(() => _httpPortError = null);
      return true;
    }
    final error = _validatePort(value, allowEmpty: true);
    if (error != null) {
      setState(() => _httpPortError = error);
      return false;
    }
    setState(() => _httpPortError = null);
    return true;
  }

  // 统一保存配置
  Future<void> _saveConfig() async {
    final trans = context.translate;
    if (_isSaving) return;

    // 验证所有端口
    final mixedValid = _validateMixedPort();
    final socksValid = _validateSocksPort();
    final httpValid = _validateHttpPort();

    if (!mixedValid || !socksValid || !httpValid) {
      return;
    }

    setState(() => _isSaving = true);

    try {
      // 保存混合端口
      final mixedPort = int.parse(_mixedPortController.text);
      _clashProvider.configService.setMixedPort(mixedPort);

      // 保存 SOCKS 端口
      if (_socksPortController.text.isEmpty) {
        _clashProvider.configService.setSocksPort(null);
      } else {
        final socksPort = int.parse(_socksPortController.text);
        _clashProvider.configService.setSocksPort(socksPort);
      }

      // 保存 HTTP 端口
      if (_httpPortController.text.isEmpty) {
        _clashProvider.configService.setHttpPort(null);
      } else {
        final httpPort = int.parse(_httpPortController.text);
        _clashProvider.configService.setHttpPort(httpPort);
      }

      if (mounted) {
        ModernToast.success(trans.port_settings.save_success);
      }
    } catch (e) {
      Logger.error('保存端口配置失败: $e');
      if (mounted) {
        ModernToast.error(
          trans.port_settings.save_failed.replaceAll('{error}', e.toString()),
        );
      }
    } finally {
      if (mounted) {
        setState(() => _isSaving = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final trans = context.translate;

    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      isHoverEnabled: false,
      isTapEnabled: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // 标题区域
          Row(
            children: [
              const Icon(Icons.settings_ethernet_outlined),
              const SizedBox(
                width: ModernFeatureCardSpacing.featureIconToTextSpacing,
              ),
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    trans.clash_features.port_settings.title,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  Text(
                    trans.clash_features.port_settings.subtitle,
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ),
            ],
          ),
          const SizedBox(height: 16),
          // 端口输入区域
          ModernTextField(
            controller: _mixedPortController,
            keyboardType: TextInputType.number,
            labelText: trans.clash_features.port_settings.mixed_port,
            hintText: ClashDefaults.mixedPort.toString(),
            errorText: _mixedPortError,
            minLines: 1,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(5),
            ],
          ),
          const SizedBox(height: 12),
          ModernTextField(
            controller: _socksPortController,
            keyboardType: TextInputType.number,
            labelText: trans.clash_features.port_settings.socks_port,
            hintText: trans.clash_features.port_settings.empty_to_disable,
            errorText: _socksPortError,
            minLines: 1,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(5),
            ],
          ),
          const SizedBox(height: 12),
          ModernTextField(
            controller: _httpPortController,
            keyboardType: TextInputType.number,
            labelText: trans.clash_features.port_settings.http_port,
            hintText: trans.clash_features.port_settings.empty_to_disable,
            errorText: _httpPortError,
            minLines: 1,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(5),
            ],
          ),
          const SizedBox(height: 16),
          // 保存按钮
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              FilledButton.icon(
                onPressed: _isSaving ? null : _saveConfig,
                icon: _isSaving
                    ? const SizedBox(
                        width: 18,
                        height: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.save, size: 18),
                label: Text(
                  _isSaving ? trans.port_settings.saving : trans.common.save,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
