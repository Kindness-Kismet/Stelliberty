// 统一的服务运行逻辑（Windows Service / Linux systemd）

use crate::clash::ClashManager;
use crate::ipc::IpcServer;
use crate::service::handler;
use anyhow::Result;
use std::sync::Arc;
use tokio::sync::{RwLock, mpsc};

#[cfg(windows)]
const SERVICE_NAME: &str = "StellibertyService";

// ============ Windows Service 实现 ============

#[cfg(windows)]
use std::ffi::OsString;
#[cfg(windows)]
use std::time::Duration;
#[cfg(windows)]
use windows_service::{
    define_windows_service,
    service::{
        ServiceControl, ServiceControlAccept, ServiceExitCode, ServiceState, ServiceStatus,
        ServiceType,
    },
    service_control_handler::{self, ServiceControlHandlerResult},
    service_dispatcher,
};

#[cfg(windows)]
const SERVICE_TYPE: ServiceType = ServiceType::OWN_PROCESS;

#[cfg(windows)]
pub fn run_as_service() -> Result<(), windows_service::Error> {
    service_dispatcher::start(SERVICE_NAME, ffi_service_main)
}

#[cfg(windows)]
define_windows_service!(ffi_service_main, service_main_windows);

#[cfg(windows)]
fn service_main_windows(_arguments: Vec<OsString>) {
    // 日志已在 main.rs 中初始化
    log::info!("Windows Service 主函数启动");

    if let Err(e) = run_service_windows() {
        log::error!("Service 运行失败: {e:?}");
    }
}

#[cfg(windows)]
fn run_service_windows() -> Result<(), Box<dyn std::error::Error>> {
    let (shutdown_tx, mut shutdown_rx) = mpsc::channel::<()>(1);

    let event_handler = move |control_event| -> ServiceControlHandlerResult {
        match control_event {
            ServiceControl::Stop => {
                log::info!("收到停止信号");
                let _ = shutdown_tx.blocking_send(());
                ServiceControlHandlerResult::NoError
            }
            ServiceControl::Interrogate => ServiceControlHandlerResult::NoError,
            _ => ServiceControlHandlerResult::NotImplemented,
        }
    };

    let status_handle = service_control_handler::register(SERVICE_NAME, event_handler)?;

    status_handle.set_service_status(ServiceStatus {
        service_type: SERVICE_TYPE,
        current_state: ServiceState::StartPending,
        controls_accepted: ServiceControlAccept::empty(),
        exit_code: ServiceExitCode::Win32(0),
        checkpoint: 0,
        wait_hint: Duration::from_secs(5),
        process_id: None,
    })?;

    log::info!("Stelliberty Service 启动中...");

    let runtime = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()?;

    runtime.block_on(async move {
        let clash_manager = Arc::new(RwLock::new(ClashManager::new()));
        let handler = handler::create_handler(clash_manager.clone());
        let mut ipc_server = IpcServer::new(handler);

        let ipc_handle = tokio::spawn(async move {
            if let Err(e) = ipc_server.run().await {
                log::error!("IPC 服务器运行失败: {e}");
            }
        });

        if let Err(e) = status_handle.set_service_status(ServiceStatus {
            service_type: SERVICE_TYPE,
            current_state: ServiceState::Running,
            controls_accepted: ServiceControlAccept::STOP,
            exit_code: ServiceExitCode::Win32(0),
            checkpoint: 0,
            wait_hint: Duration::default(),
            process_id: None,
        }) {
            log::error!("设置服务状态为 Running 失败: {e:?}");
        }

        log::info!("Stelliberty Service 运行中");

        shutdown_rx.recv().await;
        log::info!("正在停止服务...");

        if let Err(e) = status_handle.set_service_status(ServiceStatus {
            service_type: SERVICE_TYPE,
            current_state: ServiceState::StopPending,
            controls_accepted: ServiceControlAccept::empty(),
            exit_code: ServiceExitCode::Win32(0),
            checkpoint: 0,
            wait_hint: Duration::from_secs(8),
            process_id: None,
        }) {
            log::error!("设置服务状态为 StopPending 失败: {e:?}");
        }

        // 添加超时保护：确保在 Windows 强制终止前完成 Clash 清理
        use tokio::time::timeout;

        match timeout(Duration::from_secs(5), async {
            let mut manager = clash_manager.write().await;
            manager.stop()
        })
        .await
        {
            Ok(Ok(())) => {
                log::info!("Clash 已正常停止");
            }
            Ok(Err(e)) => {
                log::error!("停止 Clash 失败: {}, 服务将继续退出", e);
            }
            Err(_) => {
                log::error!("停止 Clash 超时 (5秒)，服务将强制退出");
                // 超时后尝试通过 drop 清理
                drop(clash_manager);
            }
        }

        ipc_handle.abort();
        log::info!("服务已停止");
    });

    status_handle.set_service_status(ServiceStatus {
        service_type: SERVICE_TYPE,
        current_state: ServiceState::Stopped,
        controls_accepted: ServiceControlAccept::empty(),
        exit_code: ServiceExitCode::Win32(0),
        checkpoint: 0,
        wait_hint: Duration::default(),
        process_id: None,
    })?;

    Ok(())
}

// ============ Linux systemd 实现 ============

#[cfg(target_os = "linux")]
use std::time::Duration;

#[cfg(target_os = "linux")]
pub async fn run_service() -> Result<()> {
    log::info!("Stelliberty Service (Linux) 启动中...");

    let (shutdown_tx, mut shutdown_rx) = mpsc::channel::<()>(1);

    // 注册 Unix 信号处理器
    let shutdown_tx_clone = shutdown_tx.clone();
    tokio::spawn(async move {
        use tokio::signal::unix::{SignalKind, signal};

        let mut sigterm = signal(SignalKind::terminate()).expect("无法注册 SIGTERM");
        let mut sigint = signal(SignalKind::interrupt()).expect("无法注册 SIGINT");

        tokio::select! {
            _ = sigterm.recv() => log::info!("收到 SIGTERM 信号"),
            _ = sigint.recv() => log::info!("收到 SIGINT 信号"),
        }

        let _ = shutdown_tx_clone.send(()).await;
    });

    let clash_manager = Arc::new(RwLock::new(ClashManager::new()));
    let handler = handler::create_handler(clash_manager.clone());
    let mut ipc_server = IpcServer::new(handler);

    let ipc_handle = tokio::spawn(async move {
        if let Err(e) = ipc_server.run().await {
            log::error!("IPC 服务器运行失败: {}", e);
        }
    });

    log::info!("Stelliberty Service 运行中");

    shutdown_rx.recv().await;
    log::info!("正在停止服务...");

    // 添加超时保护：确保 Clash 被正确清理
    use tokio::time::timeout;

    match timeout(Duration::from_secs(5), async {
        let mut manager = clash_manager.write().await;
        manager.stop()
    })
    .await
    {
        Ok(Ok(())) => {
            log::info!("Clash 已正常停止");
        }
        Ok(Err(e)) => {
            log::error!("停止 Clash 失败: {}, 服务将继续退出", e);
        }
        Err(_) => {
            log::error!("停止 Clash 超时 (5秒)，服务将强制退出");
            // 超时后尝试通过 drop 清理
            drop(clash_manager);
        }
    }

    ipc_handle.abort();
    log::info!("服务已停止");
    Ok(())
}
