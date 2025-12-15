// Clash 配置生成器
//
// 目的：生成运行时配置（应用覆写 + 注入运行时参数）

use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};

use super::runtime_params::RuntimeConfigParams;
use crate::clash::overrides::processor::OverrideConfig;

// Dart → Rust：生成运行时配置请求
#[derive(Debug, Clone, Serialize, Deserialize, DartSignal)]
pub struct GenerateRuntimeConfigRequest {
    // 基础配置内容（来自订阅）
    pub base_config_content: String,

    // 覆写列表
    pub overrides: Vec<OverrideConfig>,

    // 运行时参数
    pub runtime_params: RuntimeConfigParams,
}

// Rust → Dart：生成运行时配置响应
#[derive(Debug, Clone, Serialize, Deserialize, RustSignal)]
pub struct GenerateRuntimeConfigResponse {
    pub success: bool,
    pub result_config: String,
    pub error_message: String,
}

impl GenerateRuntimeConfigRequest {
    // 处理生成运行时配置请求
    pub fn handle(self) -> GenerateRuntimeConfigResponse {
        log::debug!("覆写数量：{}", self.overrides.len());
        log::debug!("运行时参数：{:?}", self.runtime_params);

        match generate_runtime_config_internal(
            &self.base_config_content,
            &self.overrides,
            &self.runtime_params,
        ) {
            Ok(config) => GenerateRuntimeConfigResponse {
                success: true,
                result_config: config,
                error_message: String::new(),
            },
            Err(e) => {
                log::error!("生成运行时配置失败：{}", e);
                GenerateRuntimeConfigResponse {
                    success: false,
                    result_config: String::new(),
                    error_message: e,
                }
            }
        }
    }
}

// 内部处理函数：应用覆写 + 注入运行时参数
fn generate_runtime_config_internal(
    base_content: &str,
    overrides: &[OverrideConfig],
    params: &RuntimeConfigParams,
) -> Result<String, String> {
    // 1. 应用覆写
    let config_after_override = if overrides.is_empty() {
        base_content.to_string()
    } else {
        log::info!("应用 {} 个覆写…", overrides.len());

        // 创建覆写处理器
        let mut processor = crate::clash::overrides::processor::OverrideProcessor::new()
            .map_err(|e| format!("初始化覆写处理器失败：{}", e))?;

        processor.apply_overrides(base_content, overrides.to_vec())?
    };

    // 2. 注入运行时参数
    let final_config = super::injector::inject_runtime_params(&config_after_override, params)?;

    // 3. 输出配置摘要（调试用）
    log_config_summary(&final_config);

    Ok(final_config)
}

// 输出配置摘要到日志
fn log_config_summary(config_yaml: &str) {
    match serde_yaml_ng::from_str::<serde_yaml_ng::Value>(config_yaml) {
        Ok(config) => {
            // 输出端口配置
            if let Some(mixed_port) = config.get("mixed-port").and_then(|v| v.as_i64()) {
                log::debug!("混合端口：{}", mixed_port);
            }

            // 输出 TUN 配置
            if let Some(tun) = config.get("tun").and_then(|v| v.as_mapping())
                && let Some(enabled) = tun.get("enable").and_then(|v| v.as_bool())
            {
                log::info!("TUN 模式：{}", if enabled { "启用" } else { "禁用" });
                if enabled && let Some(stack) = tun.get("stack").and_then(|v| v.as_str()) {
                    log::debug!("└─ 网络栈：{}", stack);
                }
            }

            // 输出代理节点数量
            if let Some(proxies) = config.get("proxies").and_then(|v| v.as_sequence()) {
                log::info!("代理节点：{} 个", proxies.len());
            }

            // 输出代理组数量
            if let Some(groups) = config.get("proxy-groups").and_then(|v| v.as_sequence()) {
                log::info!("代理组：{} 个", groups.len());
            }

            // 输出规则数量
            if let Some(rules) = config.get("rules").and_then(|v| v.as_sequence()) {
                log::info!("路由规则：{} 条", rules.len());
            }
        }
        Err(e) => {
            log::warn!("无法解析配置进行摘要输出：{}", e);
        }
    }
}
