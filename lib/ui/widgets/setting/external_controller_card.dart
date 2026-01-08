import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/common/modern_text_field.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/i18n/i18n.dart';

class ExternalControllerCard extends StatefulWidget {
  const ExternalControllerCard({super.key});

  @override
  State<ExternalControllerCard> createState() => _ExternalControllerCardState();
}

class _ExternalControllerCardState extends State<ExternalControllerCard> {
  late bool _isEnabled;
  late final TextEditingController _addressController;
  late final TextEditingController _secretController;
  bool _isSaving = false;
  String? _addressError;
  String? _secretError;

  @override
  void initState() {
    super.initState();
    final prefs = ClashPreferences.instance;
    _isEnabled = prefs.getExternalControllerEnabled();
    _addressController = TextEditingController(
      text: prefs.getExternalControllerAddress(),
    );
    _secretController = TextEditingController(
      text: prefs.getExternalControllerSecret(),
    );
  }

  @override
  void dispose() {
    _addressController.dispose();
    _secretController.dispose();
    super.dispose();
  }

  bool _validateAddress(String address) {
    if (address.isEmpty) return false;
    final pattern = RegExp(
      r'^(?:(?:\d{1,3}\.){3}\d{1,3}|localhost|[\w.-]+):\d{1,5}$',
    );
    return pattern.hasMatch(address);
  }

  Future<void> _saveConfig() async {
    final trans = context.translate;
    if (_isSaving) return;

    setState(() {
      _addressError = null;
      _secretError = null;
    });

    final address = _addressController.text.trim();
    final secret = _secretController.text.trim();

    if (address.isEmpty) {
      setState(() => _addressError = trans.external_controller.address_error);
      return;
    }

    if (!_validateAddress(address)) {
      setState(
        () => _addressError = trans.external_controller.address_format_error,
      );
      return;
    }

    setState(() => _isSaving = true);

    try {
      final prefs = ClashPreferences.instance;
      await prefs.setExternalControllerAddress(address);
      await prefs.setExternalControllerSecret(secret);

      if (mounted) {
        ModernToast.success(trans.external_controller.save_success);
      }
    } catch (e) {
      Logger.error('保存外部控制器配置失败: $e');
      if (mounted) {
        ModernToast.error(
          trans.external_controller.save_failed.replaceAll(
            '{error}',
            e.toString(),
          ),
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
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Row(
                children: [
                  const Icon(Icons.settings_remote_rounded),
                  const SizedBox(
                    width: ModernFeatureCardSpacing.featureIconToTextSpacing,
                  ),
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        trans.external_controller.title,
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      Text(
                        trans.external_controller.description,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ],
              ),
              ModernSwitch(
                value: _isEnabled,
                onChanged: (value) async {
                  setState(() => _isEnabled = value);
                  final clashProvider = Provider.of<ClashProvider>(
                    context,
                    listen: false,
                  );
                  await ClashPreferences.instance.setExternalControllerEnabled(
                    value,
                  );
                  if (!mounted) return;
                  clashProvider.configService.setExternalController(_isEnabled);
                },
              ),
            ],
          ),
          const SizedBox(height: 16),
          // 外部控制器地址输入框
          ModernTextField(
            controller: _addressController,
            keyboardType: TextInputType.text,
            labelText: trans.external_controller.address_label,
            hintText: trans.external_controller.address_hint,
            errorText: _addressError,
            minLines: 1,
          ),
          const SizedBox(height: 12),
          // Secret 输入框
          ModernTextField(
            controller: _secretController,
            keyboardType: TextInputType.text,
            labelText: trans.external_controller.secret_label,
            hintText: trans.external_controller.secret_hint,
            errorText: _secretError,
            shouldObscureText: true,
            minLines: 1,
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
                  _isSaving
                      ? trans.external_controller.saving
                      : trans.common.save,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
