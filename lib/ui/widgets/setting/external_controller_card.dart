import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/clash/storage/preferences.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/utils/logger.dart';
import 'package:stelliberty/ui/widgets/modern_toast.dart';
import 'package:stelliberty/i18n/i18n.dart';
import 'package:stelliberty/ui/widgets/modern_tooltip.dart';

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
    if (_isSaving) return;

    setState(() {
      _addressError = null;
      _secretError = null;
    });

    final address = _addressController.text.trim();
    final secret = _secretController.text.trim();

    if (address.isEmpty) {
      setState(
        () => _addressError = context.translate.externalController.addressError,
      );
      return;
    }

    if (!_validateAddress(address)) {
      setState(
        () => _addressError =
            context.translate.externalController.addressFormatError,
      );
      return;
    }

    setState(() => _isSaving = true);

    try {
      final prefs = ClashPreferences.instance;
      await prefs.setExternalControllerAddress(address);
      await prefs.setExternalControllerSecret(secret);

      if (mounted) {
        ModernToast.success(
          context,
          context.translate.externalController.saveSuccess,
        );
      }
    } catch (e) {
      Logger.error('保存外部控制器配置失败: $e');
      if (mounted) {
        ModernToast.error(
          context,
          context.translate.externalController.saveFailed.replaceAll(
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

  Future<void> _copyToClipboard(String text, String label) async {
    if (text.isEmpty) return;
    await Clipboard.setData(ClipboardData(text: text));
    if (mounted) {
      ModernToast.success(
        context,
        context.translate.externalController.copied.replaceAll(
          '{label}',
          label,
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      enableHover: false,
      enableTap: false,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Row(
                  children: [
                    const Icon(Icons.settings_remote_rounded),
                    const SizedBox(width: 12),
                    Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          context.translate.externalController.title,
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        Text(
                          context.translate.externalController.description,
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
                    await ClashPreferences.instance
                        .setExternalControllerEnabled(value);
                    if (!mounted) return;
                    clashProvider.configService.setExternalController(
                      _isEnabled,
                    );
                  },
                ),
              ],
            ),
            const SizedBox(height: 16),
            const Divider(height: 1),
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _addressController,
                    enabled: !_isSaving,
                    decoration: InputDecoration(
                      labelText:
                          context.translate.externalController.addressLabel,
                      hintText:
                          context.translate.externalController.addressHint,
                      errorText: _addressError,
                      border: const OutlineInputBorder(),
                      prefixIcon: const Icon(Icons.link_rounded, size: 20),
                      suffixIcon: ModernTooltip(
                        message:
                            context.translate.externalController.copyAddress,
                        child: IconButton(
                          icon: const Icon(
                            Icons.content_copy_rounded,
                            size: 20,
                          ),
                          onPressed: _isSaving
                              ? null
                              : () => _copyToClipboard(
                                  _addressController.text,
                                  context
                                      .translate
                                      .externalController
                                      .copyAddress,
                                ),
                        ),
                      ),
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _secretController,
                    enabled: !_isSaving,
                    obscureText: true,
                    decoration: InputDecoration(
                      labelText:
                          context.translate.externalController.secretLabel,
                      hintText: context.translate.externalController.secretHint,
                      errorText: _secretError,
                      border: const OutlineInputBorder(),
                      prefixIcon: const Icon(Icons.vpn_key_rounded, size: 20),
                      suffixIcon: ModernTooltip(
                        message:
                            context.translate.externalController.copySecret,
                        child: IconButton(
                          icon: const Icon(
                            Icons.content_copy_rounded,
                            size: 20,
                          ),
                          onPressed: _isSaving
                              ? null
                              : () => _copyToClipboard(
                                  _secretController.text,
                                  context
                                      .translate
                                      .externalController
                                      .copySecret,
                                ),
                        ),
                      ),
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),
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
                        ? context.translate.externalController.saving
                        : context.translate.common.save,
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
