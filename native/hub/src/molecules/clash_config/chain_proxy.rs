use rinf::{DartSignal, RustSignal, SignalPiece};
use serde::{Deserialize, Serialize};
use serde_yaml_ng::{Mapping, Value as YamlValue};

#[derive(Debug, Clone, Serialize, Deserialize, SignalPiece)]
pub struct ChainProxyCustomConfig {
    pub display_name: String,
    pub node_names: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize, DartSignal)]
pub struct BuildChainProxyConfigRequest {
    pub request_id: String,
    pub raw_config: String,
    pub fallback_builtin_chain_proxy_names: Vec<String>,
    pub disabled_builtin_chain_proxy_names: Vec<String>,
    pub custom_chain_proxies: Vec<ChainProxyCustomConfig>,
}

#[derive(Debug, Clone, Serialize, Deserialize, RustSignal)]
pub struct BuildChainProxyConfigResponse {
    pub request_id: String,
    pub is_successful: bool,
    pub config_content: String,
    pub builtin_chain_proxy_names: Vec<String>,
    pub error_message: String,
}

struct ChainProxyRuntimeConfig {
    config_content: String,
    builtin_chain_proxy_names: Vec<String>,
}

impl BuildChainProxyConfigRequest {
    fn handle(self) -> BuildChainProxyConfigResponse {
        match build_chain_proxy_config(&self) {
            Ok(result) => BuildChainProxyConfigResponse {
                request_id: self.request_id,
                is_successful: true,
                config_content: result.config_content,
                builtin_chain_proxy_names: result.builtin_chain_proxy_names,
                error_message: String::new(),
            },
            Err(e) => {
                log::error!("[{}] 生成链式基础配置失败：{}", self.request_id, e);
                BuildChainProxyConfigResponse {
                    request_id: self.request_id,
                    is_successful: false,
                    config_content: String::new(),
                    builtin_chain_proxy_names: Vec::new(),
                    error_message: e,
                }
            }
        }
    }
}

fn build_chain_proxy_config(
    request: &BuildChainProxyConfigRequest,
) -> Result<ChainProxyRuntimeConfig, String> {
    let mut config: YamlValue = serde_yaml_ng::from_str(&request.raw_config)
        .map_err(|e| format!("解析链式基础配置失败：{}", e))?;

    let Some(root) = config.as_mapping_mut() else {
        return Ok(ChainProxyRuntimeConfig {
            config_content: request.raw_config.clone(),
            builtin_chain_proxy_names: request.fallback_builtin_chain_proxy_names.clone(),
        });
    };

    let proxies = extract_mapping_sequence(root, "proxies");
    let proxy_groups = extract_mapping_sequence(root, "proxy-groups");
    let builtin_chain_proxy_names = collect_builtin_chain_proxy_names(&proxies);
    let filtered_proxies = filter_proxies(
        &proxies,
        &builtin_chain_proxy_names,
        &request.disabled_builtin_chain_proxy_names,
    );
    let mut filtered_proxy_groups =
        filter_proxy_groups(&proxy_groups, &request.custom_chain_proxies);

    for custom_proxy in &request.custom_chain_proxies {
        if let Some(group) = build_runtime_relay_group(&proxies, custom_proxy) {
            filtered_proxy_groups.push(group);
        }
    }

    root.insert(
        yaml_key("proxies"),
        YamlValue::Sequence(
            filtered_proxies
                .into_iter()
                .map(YamlValue::Mapping)
                .collect(),
        ),
    );
    root.insert(
        yaml_key("proxy-groups"),
        YamlValue::Sequence(
            filtered_proxy_groups
                .into_iter()
                .map(YamlValue::Mapping)
                .collect(),
        ),
    );

    let config_content =
        serde_yaml_ng::to_string(&config).map_err(|e| format!("序列化链式基础配置失败：{}", e))?;

    Ok(ChainProxyRuntimeConfig {
        config_content,
        builtin_chain_proxy_names,
    })
}

fn extract_mapping_sequence(root: &Mapping, key: &str) -> Vec<Mapping> {
    root.get(yaml_key(key))
        .and_then(|value| value.as_sequence())
        .map(|items| {
            items
                .iter()
                .filter_map(|item| item.as_mapping().cloned())
                .collect()
        })
        .unwrap_or_default()
}

fn collect_builtin_chain_proxy_names(proxies: &[Mapping]) -> Vec<String> {
    proxies
        .iter()
        .filter_map(|proxy| {
            let dialer_proxy = string_field(proxy, "dialer-proxy")?;
            if dialer_proxy.is_empty() {
                return None;
            }

            let name = string_field(proxy, "name")?;
            if name.is_empty() {
                return None;
            }

            Some(name.to_string())
        })
        .collect()
}

fn filter_proxies(
    proxies: &[Mapping],
    builtin_chain_proxy_names: &[String],
    disabled_builtin_chain_proxy_names: &[String],
) -> Vec<Mapping> {
    proxies
        .iter()
        .filter(|proxy| {
            let Some(name) = string_field(proxy, "name") else {
                return true;
            };
            if name.is_empty() {
                return true;
            }

            if builtin_chain_proxy_names
                .iter()
                .any(|builtin| builtin == name)
            {
                return !disabled_builtin_chain_proxy_names
                    .iter()
                    .any(|disabled| disabled == name);
            }

            true
        })
        .cloned()
        .collect()
}

fn filter_proxy_groups(
    proxy_groups: &[Mapping],
    custom_chain_proxies: &[ChainProxyCustomConfig],
) -> Vec<Mapping> {
    proxy_groups
        .iter()
        .filter(|group| {
            let Some(name) = string_field(group, "name") else {
                return true;
            };
            if name.is_empty() {
                return true;
            }

            !custom_chain_proxies
                .iter()
                .any(|custom_proxy| custom_proxy.display_name == name)
        })
        .cloned()
        .collect()
}

fn build_runtime_relay_group(
    proxies: &[Mapping],
    custom_proxy: &ChainProxyCustomConfig,
) -> Option<Mapping> {
    if custom_proxy.node_names.len() < 2 {
        return None;
    }

    for node_name in &custom_proxy.node_names {
        if !has_proxy_named(proxies, node_name) {
            return None;
        }
    }

    let mut group = Mapping::new();
    group.insert(
        yaml_key("name"),
        YamlValue::String(custom_proxy.display_name.clone()),
    );
    group.insert(yaml_key("type"), YamlValue::String("relay".to_string()));
    group.insert(
        yaml_key("proxies"),
        YamlValue::Sequence(
            custom_proxy
                .node_names
                .iter()
                .map(|name| YamlValue::String(name.clone()))
                .collect(),
        ),
    );
    Some(group)
}

fn has_proxy_named(proxies: &[Mapping], name: &str) -> bool {
    proxies
        .iter()
        .any(|proxy| string_field(proxy, "name") == Some(name))
}

fn string_field<'a>(mapping: &'a Mapping, key: &str) -> Option<&'a str> {
    mapping.get(yaml_key(key)).and_then(|value| value.as_str())
}

fn yaml_key(key: &str) -> YamlValue {
    YamlValue::String(key.to_string())
}

pub fn init() {
    use tokio::spawn;

    spawn(async {
        let receiver = BuildChainProxyConfigRequest::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            let request_id = message.request_id.clone();
            tokio::spawn(async move {
                match tokio::task::spawn_blocking(move || message.handle()).await {
                    Ok(response) => response.send_signal_to_dart(),
                    Err(e) => {
                        log::error!("[{}] 链式基础配置任务失败：{}", request_id, e);
                        BuildChainProxyConfigResponse {
                            request_id,
                            is_successful: false,
                            config_content: String::new(),
                            builtin_chain_proxy_names: Vec::new(),
                            error_message: format!("链式基础配置任务失败：{}", e),
                        }
                        .send_signal_to_dart();
                    }
                }
            });
        }
    });
}
