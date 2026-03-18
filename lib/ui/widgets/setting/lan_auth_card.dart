import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import 'package:stelliberty/clash/providers/clash_provider.dart';
import 'package:stelliberty/storage/clash_preferences.dart';
import 'package:stelliberty/ui/common/modern_feature_card.dart';
import 'package:stelliberty/ui/common/modern_switch.dart';
import 'package:stelliberty/ui/common/modern_text_field.dart';
import 'package:stelliberty/services/log_print_service.dart';
import 'package:stelliberty/i18n/i18n.dart';

class LanAuthCard extends StatefulWidget {
  const LanAuthCard({super.key});

  @override
  State<LanAuthCard> createState() => _LanAuthCardState();
}

class _LanAuthCardState extends State<LanAuthCard> {
  late bool _isEnabled;
  late final TextEditingController _usernameController;
  late final TextEditingController _passwordController;
  bool _isSaving = false;

  @override
  void initState() {
    super.initState();
    final prefs = ClashPreferences.instance;
    _isEnabled = prefs.getAllowLan();
    _usernameController = TextEditingController(
      text: prefs.getLanAuthUsername(),
    );
    _passwordController = TextEditingController(
      text: prefs.getLanAuthPassword(),
    );
  }

  @override
  void dispose() {
    _usernameController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _saveConfig() async {
    if (_isSaving) return;
    setState(() => _isSaving = true);

    try {
      final clashProvider = Provider.of<ClashProvider>(
        context,
        listen: false,
      );
      await clashProvider.setLanAuthentication(
        _usernameController.text.trim(),
        _passwordController.text.trim(),
      );
    } catch (e) {
      Logger.error('保存局域网认证失败：$e');
    } finally {
      if (mounted) {
        setState(() => _isSaving = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final clashProvider = Provider.of<ClashProvider>(context, listen: false);
    final trans = context.translate;

    return ModernFeatureCard(
      isSelected: false,
      onTap: () {},
      isHoverEnabled: false,
      isTapEnabled: false,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              const Icon(Icons.lan),
              const SizedBox(
                width: ModernFeatureCardSpacing.featureIconToTextSpacing,
              ),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      trans.clash_features.network_settings.allow_lan.title,
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    Text(
                      trans.clash_features.network_settings.allow_lan.subtitle,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 12),
              ModernSwitch(
                value: _isEnabled,
                onChanged: (value) async {
                  setState(() => _isEnabled = value);
                  await clashProvider.setAllowLan(value);
                },
              ),
            ],
          ),
          const SizedBox(height: 16),
          Text(
            trans.clash_features.network_settings.lan_auth.title,
            style: Theme.of(context).textTheme.titleSmall,
          ),
          const SizedBox(height: 4),
          Text(
            trans.clash_features.network_settings.lan_auth.subtitle,
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 16),
          ModernTextField(
            controller: _usernameController,
            keyboardType: TextInputType.text,
            labelText: trans.clash_features.network_settings.lan_auth.username,
            minLines: 1,
          ),
          const SizedBox(height: 12),
          ModernTextField(
            controller: _passwordController,
            keyboardType: TextInputType.text,
            labelText: trans.clash_features.network_settings.lan_auth.password,
            shouldObscureText: true,
            minLines: 1,
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
                label: Text(_isSaving ? '...' : trans.common.save),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
