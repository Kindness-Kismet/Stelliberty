// 网络配置消息协议
//
// 目的：定义系统代理和网络接口查询的通信接口

use super::interfaces;
use super::proxy;
use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};

// Dart → Rust：启用系统代理
#[derive(Deserialize, DartSignal)]
pub struct EnableSystemProxy {
    pub host: String,
    pub port: u16,
    pub bypass_domains: Vec<String>,
    pub use_pac_mode: bool,
    pub pac_script: String,
    pub pac_file_path: String,
}

// Dart → Rust：禁用系统代理
#[derive(Deserialize, DartSignal)]
pub struct DisableSystemProxy;

// Dart → Rust：获取系统代理状态
#[derive(Deserialize, DartSignal)]
pub struct GetSystemProxy;

// Dart → Rust：获取网络接口信息
#[derive(Deserialize, DartSignal)]
pub struct GetNetworkInterfaces;

// Rust → Dart：代理操作结果
#[derive(Serialize, RustSignal)]
pub struct SystemProxyResult {
    pub success: bool,
    pub error_message: Option<String>,
}

// Rust → Dart：系统代理状态信息
#[derive(Serialize, RustSignal)]
pub struct SystemProxyInfo {
    pub enabled: bool,
    pub server: Option<String>,
}

// Rust → Dart：网络接口信息
#[derive(Serialize, RustSignal)]
pub struct NetworkInterfacesInfo {
    pub addresses: Vec<String>,
    pub hostname: Option<String>,
}

impl EnableSystemProxy {
    // 执行启用代理操作
    //
    // 目的：配置系统级代理设置，使所有网络流量经过指定代理服务器
    pub async fn handle(self) {
        if self.use_pac_mode {
            log::info!("收到启用代理请求 (PAC 模式)");
        } else {
            log::info!("收到启用代理请求：{}:{}", self.host, self.port);
        }

        let result = proxy::enable_proxy(
            &self.host,
            self.port,
            self.bypass_domains,
            self.use_pac_mode,
            &self.pac_script,
            &self.pac_file_path,
        )
        .await;

        let response = match result {
            proxy::ProxyResult::Success => SystemProxyResult {
                success: true,
                error_message: None,
            },
            proxy::ProxyResult::Error(msg) => {
                log::error!("启用代理失败：{}", msg);
                SystemProxyResult {
                    success: false,
                    error_message: Some(msg),
                }
            }
        };

        response.send_signal_to_dart();
    }
}

impl DisableSystemProxy {
    // 执行禁用代理操作
    //
    // 目的：移除系统代理配置，恢复直连网络访问
    pub async fn handle(&self) {
        log::info!("收到禁用代理请求");

        let result = proxy::disable_proxy().await;

        let response = match result {
            proxy::ProxyResult::Success => SystemProxyResult {
                success: true,
                error_message: None,
            },
            proxy::ProxyResult::Error(msg) => {
                log::error!("禁用代理失败：{}", msg);
                SystemProxyResult {
                    success: false,
                    error_message: Some(msg),
                }
            }
        };

        response.send_signal_to_dart();
    }
}

impl GetSystemProxy {
    // 查询当前系统代理状态
    //
    // 目的：获取系统代理的启用状态和配置信息
    pub async fn handle(&self) {
        log::info!("收到获取系统代理状态请求");

        let proxy_info = proxy::get_proxy_info().await;

        let response = SystemProxyInfo {
            enabled: proxy_info.enabled,
            server: proxy_info.server,
        };

        response.send_signal_to_dart();
    }
}

impl GetNetworkInterfaces {
    // 收集系统网络接口信息
    //
    // 目的：为前端提供可用的网络地址列表，用于显示本机访问地址
    pub fn handle(&self) {
        log::info!("收到获取网络接口请求");

        let mut addresses = vec!["127.0.0.1".to_string(), "localhost".to_string()];

        let hostname = interfaces::get_hostname();

        if let Some(ref host) = hostname
            && host != "localhost"
            && host != "127.0.0.1"
        {
            addresses.push(format!("{}.local", host));
        }

        match interfaces::get_network_addresses() {
            Ok(mut addrs) => {
                addresses.append(&mut addrs);
            }
            Err(e) => {
                log::warn!("获取网络接口失败：{}", e);
            }
        }

        addresses.sort();
        addresses.dedup();

        let clean_addresses = addresses
            .iter()
            .map(|addr| {
                if let Some(percent_pos) = addr.find('%') {
                    addr[..percent_pos].to_string()
                } else {
                    addr.clone()
                }
            })
            .collect();

        log::debug!("最终地址列表：{:?}", clean_addresses);

        let response = NetworkInterfacesInfo {
            addresses: clean_addresses,
            hostname,
        };

        response.send_signal_to_dart();
    }
}
