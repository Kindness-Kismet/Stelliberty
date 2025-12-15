// 网络接口信息查询
//
// 目的：提供跨平台的系统网络信息获取能力

use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};
use std::net::IpAddr;

// Dart → Rust：获取网络接口信息
#[derive(Deserialize, DartSignal)]
pub struct GetNetworkInterfaces;

// Rust → Dart：网络接口信息
#[derive(Serialize, RustSignal)]
pub struct NetworkInterfacesInfo {
    pub addresses: Vec<String>,
    pub hostname: Option<String>,
}

impl GetNetworkInterfaces {
    // 收集系统网络接口信息
    //
    // 目的：为前端提供可用的网络地址列表，用于显示本机访问地址
    pub fn handle(&self) {
        log::info!("收到获取网络接口请求");

        let mut addresses = vec!["127.0.0.1".to_string(), "localhost".to_string()];

        let hostname = get_hostname();

        if let Some(ref host) = hostname
            && host != "localhost"
            && host != "127.0.0.1"
        {
            addresses.push(format!("{}.local", host));
        }

        match get_network_addresses() {
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

// 获取系统主机名
//
// 目的：为网络地址列表提供友好的主机标识
pub fn get_hostname() -> Option<String> {
    use std::process::Command;

    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x08000000;

        let output = Command::new("hostname")
            .creation_flags(CREATE_NO_WINDOW)
            .output()
            .ok()?;

        if output.status.success() {
            String::from_utf8(output.stdout)
                .ok()
                .map(|s| s.trim().to_string())
        } else {
            None
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        let output = Command::new("hostname").output().ok()?;

        if output.status.success() {
            String::from_utf8(output.stdout)
                .ok()
                .map(|s| s.trim().to_string())
        } else {
            None
        }
    }
}

// 检查是否为 APIPA 地址
//
// 目的：过滤无效的自动分配地址(169.254.x.x)，这些地址表示网络接口未正常连接
fn is_apipa_address(ip: &std::net::Ipv4Addr) -> bool {
    let octets = ip.octets();
    octets[0] == 169 && octets[1] == 254
}

// 获取所有活动网络接口的 IP 地址
//
// 目的：为前端提供可用的本机网络地址列表，过滤无效和内部地址
pub fn get_network_addresses() -> Result<Vec<String>, String> {
    #[cfg(not(target_os = "android"))]
    {
        use network_interface::NetworkInterface;
        use network_interface::NetworkInterfaceConfig;

        let interfaces =
            NetworkInterface::show().map_err(|e| format!("无法获取网络接口：{}", e))?;

        log::debug!("network-interface 返回了{}个接口", interfaces.len());

        let mut addresses = Vec::new();

        for iface in interfaces {
            let has_valid_ipv4 = iface.addr.iter().any(|addr| {
                if let IpAddr::V4(ipv4) = addr.ip() {
                    !ipv4.is_loopback() && !is_apipa_address(&ipv4)
                } else {
                    false
                }
            });

            for addr in &iface.addr {
                match addr.ip() {
                    IpAddr::V4(ipv4) => {
                        if !ipv4.is_loopback() && !is_apipa_address(&ipv4) {
                            addresses.push(ipv4.to_string());
                        }
                    }
                    IpAddr::V6(ipv6) => {
                        if !ipv6.is_loopback() && has_valid_ipv4 {
                            addresses.push(ipv6.to_string());
                        }
                    }
                }
            }
        }

        log::info!("获取到{}个网络地址", addresses.len());
        Ok(addresses)
    }

    #[cfg(target_os = "android")]
    {
        Ok(Vec::new())
    }
}
