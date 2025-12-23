// Clash 代理核心管理
//
// 支持两种模式：
// 1. 直接进程管理模式 - 直接启动 Clash 核心进程
// 2. 服务模式 - 通过系统服务以管理员权限运行 Clash 核心，支持 TUN 模式

use rinf::{DartSignal, RustSignal};
use tokio::spawn;

pub mod config;
pub mod core_updater;
pub mod delay_test;
pub mod network;
pub mod overrides;
pub mod process;

#[cfg(any(target_os = "windows", target_os = "linux", target_os = "macos"))]
pub mod service;

pub use process::{ClashProcessResult, StartClashProcess, StopClashProcess};

#[cfg(any(target_os = "windows", target_os = "linux", target_os = "macos"))]
pub use service::{
    GetServiceStatus, GetServiceVersion, InstallService, SendServiceHeartbeat, StartClash,
    StopClash, UninstallService,
};

pub mod subscription;

/// 初始化 Clash 模块
///
/// 启动所有 Clash 相关的消息监听器
pub fn init() {
    log::info!("初始化 Clash 消息监听器");

    // IPC 网络通信
    network::init_rest_api_listeners();

    // 直接进程管理模式

    // 启动 Clash 进程
    spawn(async {
        let receiver = StartClashProcess::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            if let Err(e) = tokio::task::spawn_blocking(move || {
                message.handle();
            })
            .await
            {
                log::error!("启动进程的任务执行失败（可能线程池耗尽）：{}", e);
                // 向 Dart 发送错误响应
                ClashProcessResult {
                    is_successful: false,
                    error_message: Some(format!("任务执行失败：{}", e)),
                    pid: None,
                }
                .send_signal_to_dart();
            }
        }
    });

    // 停止 Clash 进程
    spawn(async {
        let receiver = StopClashProcess::get_dart_signal_receiver();
        while let Some(dart_signal) = receiver.recv().await {
            let message = dart_signal.message;
            if let Err(e) = tokio::task::spawn_blocking(move || {
                message.handle();
            })
            .await
            {
                log::error!("停止进程的任务执行失败（可能线程池耗尽）：{}", e);
                // 向 Dart 发送错误响应
                ClashProcessResult {
                    is_successful: false,
                    error_message: Some(format!("任务执行失败：{}", e)),
                    pid: None,
                }
                .send_signal_to_dart();
            }
        }
    });

    #[cfg(any(target_os = "windows", target_os = "linux", target_os = "macos"))]
    {
        // 获取服务状态
        spawn(async {
            let receiver = GetServiceStatus::get_dart_signal_receiver();
            while let Some(dart_signal) = receiver.recv().await {
                let message = dart_signal.message;
                tokio::spawn(async move {
                    message.handle().await;
                });
            }
        });

        // 安装服务
        spawn(async {
            let receiver = InstallService::get_dart_signal_receiver();
            while let Some(dart_signal) = receiver.recv().await {
                let message = dart_signal.message;
                tokio::spawn(async move {
                    message.handle().await;
                });
            }
        });

        // 卸载服务
        spawn(async {
            let receiver = UninstallService::get_dart_signal_receiver();
            while let Some(dart_signal) = receiver.recv().await {
                let message = dart_signal.message;
                tokio::spawn(async move {
                    message.handle().await;
                });
            }
        });

        // 通过服务启动 Clash
        spawn(async {
            let receiver = StartClash::get_dart_signal_receiver();
            while let Some(dart_signal) = receiver.recv().await {
                let message = dart_signal.message;
                tokio::spawn(async move {
                    message.handle().await;
                });
            }
        });

        // 通过服务停止 Clash
        spawn(async {
            let receiver = StopClash::get_dart_signal_receiver();
            while let Some(dart_signal) = receiver.recv().await {
                let message = dart_signal.message;
                tokio::spawn(async move {
                    message.handle().await;
                });
            }
        });

        // 向服务发送心跳
        spawn(async {
            let receiver = SendServiceHeartbeat::get_dart_signal_receiver();
            while let Some(dart_signal) = receiver.recv().await {
                let message = dart_signal.message;
                tokio::spawn(async move {
                    message.handle().await;
                });
            }
        });

        // 获取服务版本号
        spawn(async {
            let receiver = GetServiceVersion::get_dart_signal_receiver();
            while let Some(dart_signal) = receiver.recv().await {
                let message = dart_signal.message;
                tokio::spawn(async move {
                    message.handle().await;
                });
            }
        });
    }

    // 启动配置覆写监听器
    overrides::init_message_listeners();

    // 启动配置生成监听器
    config::init_message_listeners();

    // 启动订阅管理监听器
    subscription::init_message_listeners();

    // 启动核心更新监听器
    core_updater::init_message_listeners();

    // 启动延迟测试监听器
    delay_test::init_message_listeners();
}
