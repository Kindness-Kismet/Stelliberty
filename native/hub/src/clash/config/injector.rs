// Clash 运行时参数注入器
//
// 负责将运行时参数注入到 Clash 配置中，替代 Dart 端的 ConfigInjector

use serde_yaml_ng::{Mapping, Value as YamlValue};

use super::runtime_params::RuntimeConfigParams;

// 注入运行时参数到 Clash 配置
//
// 将所有运行时参数（端口、TUN、DNS 等）注入到配置中
// 并修复可能出现的 YAML 解析问题（如科学计数法字符串）
pub fn inject_runtime_params(
    yaml_content: &str,
    params: &RuntimeConfigParams,
) -> Result<String, String> {
    // 1. 解析 YAML
    let mut config: YamlValue = serde_yaml_ng::from_str(yaml_content).map_err(|e| {
        log::error!("解析配置失败：{}", e);
        format!("解析配置失败：{}", e)
    })?;

    let config_map = config.as_mapping_mut().ok_or_else(|| {
        log::error!("配置根节点不是 Map");
        "配置根节点必须是 Map".to_string()
    })?;

    // 2. 注入 IPC 端点（Named Pipe/Unix Socket）
    // Debug/Profile 模式使用 _dev 后缀，避免与 Release 模式冲突
    #[cfg(windows)]
    {
        #[cfg(debug_assertions)]
        let pipe_path = r"\\.\pipe\stelliberty_dev".to_string();
        #[cfg(not(debug_assertions))]
        let pipe_path = r"\\.\pipe\stelliberty".to_string();

        config_map.insert(
            YamlValue::String("external-controller-pipe".to_string()),
            YamlValue::String(pipe_path.clone()),
        );
        log::info!("注入 Windows Named Pipe：{}", pipe_path);
    }

    #[cfg(unix)]
    {
        #[cfg(debug_assertions)]
        let socket_path = "/tmp/stelliberty_dev.sock".to_string();
        #[cfg(not(debug_assertions))]
        let socket_path = "/tmp/stelliberty.sock".to_string();

        config_map.insert(
            YamlValue::String("external-controller-unix".to_string()),
            YamlValue::String(socket_path.clone()),
        );
        log::info!("注入 Unix Socket：{}", socket_path);
    }

    // 3. 注入外部控制器配置（HTTP API）
    if let Some(ref external_controller) = params.external_controller {
        if !external_controller.is_empty() {
            // 用户启用了外部控制器，注入地址
            config_map.insert(
                YamlValue::String("external-controller".to_string()),
                YamlValue::String(external_controller.clone()),
            );
            log::info!("外部控制器已启用：{}", external_controller);

            // 注入 secret（如果配置了）
            if let Some(ref secret) = params.external_controller_secret {
                if !secret.is_empty() {
                    config_map.insert(
                        YamlValue::String("secret".to_string()),
                        YamlValue::String(secret.clone()),
                    );
                    log::info!("外部控制器 Secret 已设置");
                } else {
                    config_map.remove(YamlValue::String("secret".to_string()));
                    log::info!("外部控制器 Secret 为空");
                }
            } else {
                config_map.remove(YamlValue::String("secret".to_string()));
            }
        } else {
            config_map.remove(YamlValue::String("external-controller".to_string()));
            config_map.remove(YamlValue::String("secret".to_string()));
            log::info!("外部控制器已禁用（仅使用 IPC）");
        }
    } else {
        config_map.remove(YamlValue::String("external-controller".to_string()));
        config_map.remove(YamlValue::String("secret".to_string()));
    }

    // 4. 注入端口配置
    config_map.insert(
        YamlValue::String("mixed-port".to_string()),
        YamlValue::Number(params.http_port.into()),
    );

    // 5. 注入 bind-address（根据 allow_lan 动态设置）
    // - allow_lan 为 false 时：bind-address 为 '127.0.0.1'（仅本地，双重保护）
    // - allow_lan 为 true 时：bind-address 为 '0.0.0.0'（所有接口，允许局域网）
    let bind_address = if params.allow_lan {
        "0.0.0.0".to_string()
    } else {
        "127.0.0.1".to_string()
    };

    config_map.insert(
        YamlValue::String("bind-address".to_string()),
        YamlValue::String(bind_address.clone()),
    );

    log::info!(
        "注入 bind-address：{}（allow_lan={}）",
        bind_address,
        params.allow_lan
    );

    // 移除单独的 port 和 socks-port，避免端口冲突
    config_map.remove(YamlValue::String("port".to_string()));
    config_map.remove(YamlValue::String("socks-port".to_string()));

    // 6. 注入出站模式
    config_map.insert(
        YamlValue::String("mode".to_string()),
        YamlValue::String(params.outbound_mode.clone()),
    );

    // 7. 注入统一延迟
    config_map.insert(
        YamlValue::String("unified-delay".to_string()),
        YamlValue::Bool(params.unified_delay),
    );

    // 8. 注入 TCP Keep-Alive 配置
    if params.keep_alive_enabled {
        if let Some(interval) = params.keep_alive_interval {
            config_map.insert(
                YamlValue::String("keep-alive-interval".to_string()),
                YamlValue::Number(interval.into()),
            );
        }
    } else {
        config_map.remove(YamlValue::String("keep-alive-interval".to_string()));
    }

    // 9. 注入 TUN 模式配置（始终注入完整配置，只切换 enable 字段）
    log::debug!(
        "🔍 Rust 收到的 TUN 参数：enable={}，stack={}，device={}，auto_route={}，auto_redirect={}，auto_detect_interface={}，strict_route={}，disable_icmp_forwarding={}，mtu={}，route_exclude_address={:?}",
        params.tun_enable,
        params.tun_stack,
        params.tun_device,
        params.tun_auto_route,
        params.tun_auto_redirect,
        params.tun_auto_detect_interface,
        params.tun_strict_route,
        params.tun_disable_icmp_forwarding,
        params.tun_mtu,
        params.tun_route_exclude_address
    );

    let mut tun_config = Mapping::new();
    tun_config.insert(
        YamlValue::String("enable".to_string()),
        YamlValue::Bool(params.tun_enable),
    );
    tun_config.insert(
        YamlValue::String("stack".to_string()),
        YamlValue::String(params.tun_stack.clone()),
    );
    tun_config.insert(
        YamlValue::String("device".to_string()),
        YamlValue::String(params.tun_device.clone()),
    );
    tun_config.insert(
        YamlValue::String("auto-route".to_string()),
        YamlValue::Bool(params.tun_auto_route),
    );
    tun_config.insert(
        YamlValue::String("auto-redirect".to_string()),
        YamlValue::Bool(params.tun_auto_redirect),
    );
    tun_config.insert(
        YamlValue::String("auto-detect-interface".to_string()),
        YamlValue::Bool(params.tun_auto_detect_interface),
    );

    // DNS 劫持列表
    let dns_hijack: Vec<YamlValue> = params
        .tun_dns_hijack
        .iter()
        .map(|s| YamlValue::String(s.clone()))
        .collect();
    tun_config.insert(
        YamlValue::String("dns-hijack".to_string()),
        YamlValue::Sequence(dns_hijack),
    );

    tun_config.insert(
        YamlValue::String("strict-route".to_string()),
        YamlValue::Bool(params.tun_strict_route),
    );

    // 排除网段列表
    if !params.tun_route_exclude_address.is_empty() {
        let route_exclude: Vec<YamlValue> = params
            .tun_route_exclude_address
            .iter()
            .map(|s| YamlValue::String(s.clone()))
            .collect();
        tun_config.insert(
            YamlValue::String("route-exclude-address".to_string()),
            YamlValue::Sequence(route_exclude),
        );
    }

    tun_config.insert(
        YamlValue::String("mtu".to_string()),
        YamlValue::Number(params.tun_mtu.into()),
    );

    // ICMP 转发控制（注意：配置项是 disable，所以需要取反逻辑）
    tun_config.insert(
        YamlValue::String("disable-icmp-forwarding".to_string()),
        YamlValue::Bool(params.tun_disable_icmp_forwarding),
    );

    config_map.insert(
        YamlValue::String("tun".to_string()),
        YamlValue::Mapping(tun_config),
    );

    log::info!("TUN 配置已注入（enable={}）", params.tun_enable);

    // TUN 启用时，注入基本的 DNS 配置
    if params.tun_enable {
        inject_dns_config(config_map, params)?;
    }

    // 9. 序列化为 YAML
    let yaml_string = serde_yaml_ng::to_string(&config).map_err(|e| {
        log::error!("序列化配置失败：{}", e);
        format!("序列化配置失败：{}", e)
    })?;

    Ok(yaml_string)
}

// 注入 DNS 配置（TUN 模式需要）
fn inject_dns_config(config_map: &mut Mapping, params: &RuntimeConfigParams) -> Result<(), String> {
    // 获取现有 DNS 配置（如果有）
    let existing_dns = config_map
        .get(YamlValue::String("dns".to_string()))
        .and_then(|v| v.as_mapping())
        .cloned();

    let mut dns_config = existing_dns.unwrap_or_else(Mapping::new);

    // 获取当前的 enhanced-mode
    let current_mode = dns_config
        .get(YamlValue::String("enhanced-mode".to_string()))
        .and_then(|v| v.as_str())
        .unwrap_or("fake-ip");

    // 只有在 enhanced-mode 是 fake-ip 或未设置时才修改 DNS 配置
    if current_mode == "fake-ip"
        || !dns_config.contains_key(YamlValue::String("enhanced-mode".to_string()))
    {
        // 注入基本 DNS 配置
        dns_config.insert(
            YamlValue::String("enable".to_string()),
            YamlValue::Bool(true),
        );
        dns_config.insert(
            YamlValue::String("ipv6".to_string()),
            YamlValue::Bool(params.ipv6),
        );

        if !dns_config.contains_key(YamlValue::String("enhanced-mode".to_string())) {
            dns_config.insert(
                YamlValue::String("enhanced-mode".to_string()),
                YamlValue::String("fake-ip".to_string()),
            );
        }

        if !dns_config.contains_key(YamlValue::String("fake-ip-range".to_string())) {
            dns_config.insert(
                YamlValue::String("fake-ip-range".to_string()),
                YamlValue::String("198.18.0.1/16".to_string()),
            );
        }

        // 如果用户配置没有 nameserver，添加默认值
        if !dns_config.contains_key(YamlValue::String("nameserver".to_string()))
            || dns_config
                .get(YamlValue::String("nameserver".to_string()))
                .and_then(|v| v.as_sequence())
                .is_none_or(|s| s.is_empty())
        {
            let nameservers = vec![
                YamlValue::String("8.8.8.8".to_string()),
                YamlValue::String("https://doh.pub/dns-query".to_string()),
                YamlValue::String("https://dns.alidns.com/dns-query".to_string()),
            ];
            dns_config.insert(
                YamlValue::String("nameserver".to_string()),
                YamlValue::Sequence(nameservers),
            );
        }

        // 如果用户配置没有 default-nameserver，添加默认值
        if !dns_config.contains_key(YamlValue::String("default-nameserver".to_string()))
            || dns_config
                .get(YamlValue::String("default-nameserver".to_string()))
                .and_then(|v| v.as_sequence())
                .is_none_or(|s| s.is_empty())
        {
            let default_nameservers = vec![
                YamlValue::String("system".to_string()),
                YamlValue::String("223.6.6.6".to_string()),
                YamlValue::String("8.8.8.8".to_string()),
            ];
            dns_config.insert(
                YamlValue::String("default-nameserver".to_string()),
                YamlValue::Sequence(default_nameservers),
            );
        }

        config_map.insert(
            YamlValue::String("dns".to_string()),
            YamlValue::Mapping(dns_config),
        );
    }

    Ok(())
}
