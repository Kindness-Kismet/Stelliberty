// 覆写处理器
// 处理配置覆写（YAML 合并 + JavaScript 执行）

use super::js_executor::JsExecutor;
use super::yaml_merger::YamlMerger;
use crate::clash::subscription::ProxyParser;
use rinf::{DartSignal, RustSignal, SignalPiece};
use serde::{Deserialize, Serialize};

// 覆写格式
#[derive(Deserialize, Serialize, SignalPiece, Clone, Copy, Debug)]
pub enum OverrideFormat {
    Yaml = 0,
    Javascript = 1,
}

// 覆写配置
#[derive(Debug, Deserialize, Serialize, SignalPiece, Clone)]
pub struct OverrideConfig {
    pub id: String,
    pub name: String,
    pub format: OverrideFormat,
    pub content: String,
}

// Dart → Rust：应用覆写请求
#[derive(Deserialize, DartSignal)]
pub struct ApplyOverridesRequest {
    pub base_config_content: String,
    pub overrides: Vec<OverrideConfig>,
}

// Rust → Dart：应用覆写响应
#[derive(Serialize, RustSignal)]
pub struct ApplyOverridesResponse {
    pub is_successful: bool,
    pub result_config: String,
    pub error_message: String,
    pub logs: Vec<String>,
}

// Dart → Rust：解析订阅请求
#[derive(Deserialize, DartSignal)]
pub struct ParseSubscriptionRequest {
    pub request_id: String, // 请求标识符，用于响应匹配
    pub content: String,
}

// Rust → Dart：解析订阅响应
#[derive(Serialize, RustSignal)]
pub struct ParseSubscriptionResponse {
    pub request_id: String, // 请求标识符，用于请求匹配
    pub is_successful: bool,
    pub parsed_config: String,
    pub error_message: String,
}

impl ApplyOverridesRequest {
    pub fn handle(self) {
        log::info!("收到应用覆写请求，覆写数量：{}", self.overrides.len());

        let mut processor = match OverrideProcessor::new() {
            Ok(p) => p,
            Err(e) => {
                log::error!("初始化覆写处理器失败：{}", e);
                let response = ApplyOverridesResponse {
                    is_successful: false,
                    result_config: String::new(),
                    error_message: format!("初始化处理器失败：{}", e),
                    logs: vec![],
                };
                response.send_signal_to_dart();
                return;
            }
        };

        // 先解析订阅内容为标准 Clash 配置
        let parsed_config = match ProxyParser::parse_subscription(&self.base_config_content) {
            Ok(config) => config,
            Err(e) => {
                log::error!("订阅解析失败：{}", e);
                let response = ApplyOverridesResponse {
                    is_successful: false,
                    result_config: String::new(),
                    error_message: format!("订阅解析失败：{}", e),
                    logs: vec![],
                };
                response.send_signal_to_dart();
                return;
            }
        };

        log::info!("订阅解析成功，配置长度：{}字节", parsed_config.len());

        match processor.apply_overrides(&parsed_config, self.overrides) {
            Ok(result) => {
                log::info!("覆写处理成功");
                let response = ApplyOverridesResponse {
                    is_successful: true,
                    result_config: result,
                    error_message: String::new(),
                    logs: vec!["处理成功".to_string()],
                };
                response.send_signal_to_dart();
            }
            Err(e) => {
                log::error!("覆写处理失败：{}", e);
                let response = ApplyOverridesResponse {
                    is_successful: false,
                    result_config: String::new(),
                    error_message: e,
                    logs: vec![],
                };
                response.send_signal_to_dart();
            }
        }
    }
}

impl ParseSubscriptionRequest {
    // 处理订阅解析请求
    pub fn handle(self) {
        log::info!(
            "收到订阅解析请求 [{}]，内容长度：{}字节",
            self.request_id,
            self.content.len()
        );

        match ProxyParser::parse_subscription(&self.content) {
            Ok(parsed_config) => {
                log::info!(
                    "订阅解析成功 [{}]，配置长度：{}字节",
                    self.request_id,
                    parsed_config.len()
                );
                let response = ParseSubscriptionResponse {
                    request_id: self.request_id,
                    is_successful: true,
                    parsed_config,
                    error_message: String::new(),
                };
                response.send_signal_to_dart();
            }
            Err(e) => {
                log::error!("订阅解析失败 [{}]：{}", self.request_id, e);
                let response = ParseSubscriptionResponse {
                    request_id: self.request_id,
                    is_successful: false,
                    parsed_config: String::new(),
                    error_message: e,
                };
                response.send_signal_to_dart();
            }
        }
    }
}

// 覆写处理器
pub struct OverrideProcessor {
    yaml_merger: YamlMerger,
    js_executor: JsExecutor,
}

impl OverrideProcessor {
    // 创建新的覆写处理器
    //
    // 目的：初始化 YAML 合并器和 JavaScript 执行器
    pub fn new() -> Result<Self, String> {
        let yaml_merger = YamlMerger::new();
        let js_executor =
            JsExecutor::new().map_err(|e| format!("初始化 JavaScript 引擎失败：{}", e))?;

        Ok(Self {
            yaml_merger,
            js_executor,
        })
    }

    // 应用所有覆写到基础配置
    //
    // 目的：按顺序应用每个覆写，返回最终配置
    pub fn apply_overrides(
        &mut self,
        base_config: &str,
        overrides: Vec<OverrideConfig>,
    ) -> Result<String, String> {
        let mut current_config = base_config.to_string();

        for (i, override_cfg) in overrides.iter().enumerate() {
            log::info!(
                "[{}] 应用覆写：{}（{:?}）",
                i,
                override_cfg.name,
                override_cfg.format
            );

            current_config = match override_cfg.format {
                OverrideFormat::Yaml => self
                    .yaml_merger
                    .apply(&current_config, &override_cfg.content)
                    .map_err(|e| format!("YAML 覆写失败：{}", e))?,
                OverrideFormat::Javascript => self
                    .js_executor
                    .apply(&current_config, &override_cfg.content)
                    .map_err(|e| format!("JavaScript 覆写失败：{}", e))?,
            };

            log::info!("[{}] 覆写应用成功", i);
        }

        Ok(current_config)
    }
}
