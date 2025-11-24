// 网络配置与接口管理
//
// 目的：为应用提供系统代理控制和网络接口信息查询能力

use rinf::DartSignal;
use tokio::spawn;

pub mod interfaces;
pub mod messages;
pub mod proxy;

#[allow(unused_imports)]
pub use interfaces::{get_hostname, get_network_addresses};
#[allow(unused_imports)]
pub use messages::{
    DisableSystemProxy, EnableSystemProxy, GetNetworkInterfaces, GetSystemProxy,
    NetworkInterfacesInfo, SystemProxyInfo, SystemProxyResult,
};
#[allow(unused_imports)]
pub use proxy::{ProxyInfo, ProxyResult, disable_proxy, enable_proxy, get_proxy_info};

// 初始化网络模块
//
// 启动所有网络功能的消息监听器，建立网络配置请求的响应通道
pub fn init() {
    spawn(async {
        let receiver = EnableSystemProxy::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle().await;
        }
        log::info!("启用代理消息通道已关闭，退出监听器");
    });

    // 监听禁用代理信号
    spawn(async {
        let receiver = DisableSystemProxy::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle().await;
        }
        log::info!("禁用代理消息通道已关闭，退出监听器");
    });

    // 监听获取系统代理状态信号
    spawn(async {
        let receiver = GetSystemProxy::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle().await;
        }
        log::info!("获取系统代理状态消息通道已关闭，退出监听器");
    });

    // 监听获取网络接口信号
    spawn(async {
        let receiver = GetNetworkInterfaces::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            dart_signal.message.handle();
        }
        log::info!("获取网络接口消息通道已关闭，退出监听器");
    });
}
