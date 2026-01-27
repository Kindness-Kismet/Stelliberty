// Clash 运行时参数注入器

use serde_yaml_ng::{Mapping, Value as YamlValue};

use super::runtime_params::RuntimeConfigParams;

// 注入运行时参数到 Clash 配置
pub fn inject_runtime_params(
    yaml_content: &str,
    params: &RuntimeConfigParams,
) -> Result<String, String> {
    // 解析 YAML
    let mut config: YamlValue = serde_yaml_ng::from_str(yaml_content).map_err(|e| {
        log::error!("解析配置失败：{}", e);
        format!("解析配置失败：{}", e)
    })?;

    let config_map = config.as_mapping_mut().ok_or_else(|| {
        log::error!("配置根节点不是 Map");
        "配置根节点必须是 Map".to_string()
    })?;

    // 注入 IPC 端点
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
        log::info!("注入 Named Pipe：{}", pipe_path);
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

    // 注入外部控制器
    if let Some(ref external_controller) = params.external_controller {
        if !external_controller.is_empty() {
            config_map.insert(
                YamlValue::String("external-controller".to_string()),
                YamlValue::String(external_controller.clone()),
            );
            log::info!("外部控制器：{}", external_controller);

            if let Some(ref secret) = params.external_controller_secret {
                if !secret.is_empty() {
                    config_map.insert(
                        YamlValue::String("secret".to_string()),
                        YamlValue::String(secret.clone()),
                    );
                } else {
                    config_map.remove(YamlValue::String("secret".to_string()));
                }
            } else {
                config_map.remove(YamlValue::String("secret".to_string()));
            }
        } else {
            config_map.remove(YamlValue::String("external-controller".to_string()));
            config_map.remove(YamlValue::String("secret".to_string()));
        }
    } else {
        config_map.remove(YamlValue::String("external-controller".to_string()));
        config_map.remove(YamlValue::String("secret".to_string()));
    }

    // 注入端口
    config_map.insert(
        YamlValue::String("mixed-port".to_string()),
        YamlValue::Number(params.mixed_port.into()),
    );
    config_map.insert(
        YamlValue::String("port".to_string()),
        YamlValue::Number(params.http_port.into()),
    );
    config_map.insert(
        YamlValue::String("socks-port".to_string()),
        YamlValue::Number(params.socks_port.into()),
    );

    // 注入 bind-address
    let bind_address = if params.is_allow_lan_enabled {
        "0.0.0.0"
    } else {
        "127.0.0.1"
    };
    config_map.insert(
        YamlValue::String("bind-address".to_string()),
        YamlValue::String(bind_address.to_string()),
    );
    log::info!("bind-address：{}", bind_address);

    // 注入出站模式
    config_map.insert(
        YamlValue::String("mode".to_string()),
        YamlValue::String(params.outbound_mode.clone()),
    );

    // 注入 IPv6
    config_map.insert(
        YamlValue::String("ipv6".to_string()),
        YamlValue::Bool(params.is_ipv6_enabled),
    );

    // 注入 TCP 并发
    config_map.insert(
        YamlValue::String("tcp-concurrent".to_string()),
        YamlValue::Bool(params.is_tcp_concurrent_enabled),
    );

    // 注入统一延迟
    config_map.insert(
        YamlValue::String("unified-delay".to_string()),
        YamlValue::Bool(params.is_unified_delay_enabled),
    );

    // 注入查找进程模式
    config_map.insert(
        YamlValue::String("find-process-mode".to_string()),
        YamlValue::String(params.find_process_mode.clone()),
    );

    // 注入 GeoData 加载器
    config_map.insert(
        YamlValue::String("geodata-loader".to_string()),
        YamlValue::String(params.geodata_loader.clone()),
    );

    // 注入日志级别
    config_map.insert(
        YamlValue::String("log-level".to_string()),
        YamlValue::String(params.clash_core_log_level.clone()),
    );

    // 注入 Keep-Alive
    if params.is_keep_alive_enabled {
        if let Some(interval) = params.keep_alive_interval {
            config_map.insert(
                YamlValue::String("keep-alive-interval".to_string()),
                YamlValue::Number(interval.into()),
            );
        }
    } else {
        config_map.remove(YamlValue::String("keep-alive-interval".to_string()));
    }

    // 注入 TUN 配置
    log::debug!(
        "TUN 参数：enabled={}, stack={}, device={}, mtu={}",
        params.is_tun_enabled,
        params.tun_stack,
        params.tun_device,
        params.tun_mtu
    );

    let mut tun_config = Mapping::new();
    tun_config.insert(
        YamlValue::String("enable".to_string()),
        YamlValue::Bool(params.is_tun_enabled),
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
        YamlValue::Bool(params.is_tun_auto_route_enabled),
    );
    tun_config.insert(
        YamlValue::String("auto-redirect".to_string()),
        YamlValue::Bool(params.is_tun_auto_redirect_enabled),
    );
    tun_config.insert(
        YamlValue::String("auto-detect-interface".to_string()),
        YamlValue::Bool(params.is_tun_auto_detect_interface_enabled),
    );

    let dns_hijack: Vec<YamlValue> = params
        .tun_dns_hijacks
        .iter()
        .map(|s| YamlValue::String(s.clone()))
        .collect();
    tun_config.insert(
        YamlValue::String("dns-hijack".to_string()),
        YamlValue::Sequence(dns_hijack),
    );
    tun_config.insert(
        YamlValue::String("strict-route".to_string()),
        YamlValue::Bool(params.is_tun_strict_route_enabled),
    );

    if !params.tun_route_exclude_addresses.is_empty() {
        let route_exclude: Vec<YamlValue> = params
            .tun_route_exclude_addresses
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
    tun_config.insert(
        YamlValue::String("disable-icmp-forwarding".to_string()),
        YamlValue::Bool(params.is_tun_icmp_forwarding_disabled),
    );

    config_map.insert(
        YamlValue::String("tun".to_string()),
        YamlValue::Mapping(tun_config),
    );
    log::info!("TUN 配置已注入（enabled={}）", params.is_tun_enabled);

    // 注入 DNS（优先级：用户覆写 > TUN 默认 > 不注入）
    if params.is_dns_override_enabled {
        inject_user_dns_override(config_map, params)?;
    } else if params.is_tun_enabled {
        inject_dns_config(config_map, params)?;
    }

    // 序列化输出
    let yaml_string = serde_yaml_ng::to_string(&config).map_err(|e| {
        log::error!("序列化配置失败：{}", e);
        format!("序列化配置失败：{}", e)
    })?;

    Ok(yaml_string)
}

// 注入 TUN 模式默认 DNS 配置
fn inject_dns_config(config_map: &mut Mapping, params: &RuntimeConfigParams) -> Result<(), String> {
    let existing_dns = config_map
        .get(YamlValue::String("dns".to_string()))
        .and_then(|v| v.as_mapping())
        .cloned();

    let mut dns_config = existing_dns.unwrap_or_else(Mapping::new);

    let enhanced_mode = dns_config
        .get(YamlValue::String("enhanced-mode".to_string()))
        .and_then(|v| v.as_str())
        .unwrap_or("fake-ip");

    // 非 fake-ip 模式且已配置，跳过
    if enhanced_mode != "fake-ip"
        && dns_config.contains_key(YamlValue::String("enhanced-mode".to_string()))
    {
        return Ok(());
    }

    dns_config.insert(
        YamlValue::String("enable".to_string()),
        YamlValue::Bool(true),
    );
    dns_config.insert(
        YamlValue::String("ipv6".to_string()),
        YamlValue::Bool(params.is_ipv6_enabled),
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

    // 默认 nameserver
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

    // 默认 default-nameserver
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

    Ok(())
}

// 注入用户自定义 DNS 覆写
fn inject_user_dns_override(
    config_map: &mut Mapping,
    params: &RuntimeConfigParams,
) -> Result<(), String> {
    let dns_content = match &params.dns_override_content {
        Some(content) if !content.is_empty() => content,
        _ => {
            log::warn!("DNS 覆写已启用但内容为空");
            return Ok(());
        }
    };

    let user_config: YamlValue = serde_yaml_ng::from_str(dns_content).map_err(|e| {
        log::error!("解析用户 DNS 配置失败：{}", e);
        format!("解析用户 DNS 配置失败：{}", e)
    })?;

    let user_config_map = user_config.as_mapping().ok_or_else(|| {
        log::error!("用户 DNS 配置根节点不是 Map");
        "用户 DNS 配置根节点必须是 Map".to_string()
    })?;

    if let Some(dns_value) = user_config_map.get(YamlValue::String("dns".to_string())) {
        config_map.insert(YamlValue::String("dns".to_string()), dns_value.clone());
        log::info!("用户 DNS 覆写已注入");
    } else {
        log::warn!("用户 DNS 配置中无 dns 字段");
    }

    if let Some(hosts_value) = user_config_map.get(YamlValue::String("hosts".to_string())) {
        config_map.insert(YamlValue::String("hosts".to_string()), hosts_value.clone());
        log::info!("用户 Hosts 覆写已注入");
    }

    Ok(())
}
