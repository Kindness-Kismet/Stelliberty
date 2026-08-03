use anyhow::{Context, Result};
use tokio::sync::oneshot;

use crate::ipc::{ServiceState, run_server};
use crate::logging;

pub fn run_as_service() -> Result<()> {
    logging::info("Service entry started");
    #[cfg(windows)]
    {
        windows_service_entry()
    }

    #[cfg(not(windows))]
    {
        run_foreground()
    }
}

pub fn run_foreground() -> Result<()> {
    logging::info("Foreground service started");
    let runtime = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .context("Failed to create the service runtime")?;

    runtime.block_on(async {
        let (shutdown_tx, shutdown_rx) = oneshot::channel();
        let state = ServiceState::new(shutdown_tx);
        let server_state = state.clone();
        let heartbeat_state = state.clone();
        let heartbeat = tokio::spawn(async move {
            heartbeat_state.monitor_heartbeat().await;
        });

        let result = tokio::select! {
            result = run_server(server_state, shutdown_rx) => result,
            result = tokio::signal::ctrl_c() => {
                logging::info("Foreground stop signal received");
                result.context("Failed to listen for the stop signal")?;
                Ok(())
            }
        };
        heartbeat.abort();
        state.stop_core().await;
        logging::info("Foreground service stopped");
        result
    })
}

#[cfg(windows)]
fn windows_service_entry() -> Result<()> {
    windows_service::service_dispatcher::start(crate::channel::service_name(), ffi_service_main)
        .context("Failed to start the Windows service dispatcher")
}

#[cfg(windows)]
windows_service::define_windows_service!(ffi_service_main, service_main_windows);

#[cfg(windows)]
fn service_main_windows(_arguments: Vec<std::ffi::OsString>) {
    if let Err(error) = run_windows_service() {
        logging::error(format!("Windows service failed: {error:#}"));
        eprintln!("Service failed: {error:?}");
    }
}

#[cfg(windows)]
fn run_windows_service() -> Result<()> {
    use std::sync::mpsc;
    use std::time::Duration;
    use windows_service::service::{
        ServiceControl, ServiceControlAccept, ServiceExitCode, ServiceState as WinServiceState,
        ServiceStatus, ServiceType,
    };
    use windows_service::service_control_handler::{self, ServiceControlHandlerResult};

    const SERVICE_TYPE: ServiceType = ServiceType::OWN_PROCESS;

    let (control_tx, control_rx) = mpsc::channel::<&'static str>();
    let event_handler = move |control_event| -> ServiceControlHandlerResult {
        match control_event {
            ServiceControl::Stop => {
                let _ = control_tx.send("stop");
                ServiceControlHandlerResult::NoError
            }
            ServiceControl::Shutdown => {
                let _ = control_tx.send("os-shutdown");
                ServiceControlHandlerResult::NoError
            }
            ServiceControl::Interrogate => ServiceControlHandlerResult::NoError,
            _ => ServiceControlHandlerResult::NotImplemented,
        }
    };

    let status_handle =
        service_control_handler::register(crate::channel::service_name(), event_handler)
            .context("Failed to register the service control handler")?;

    status_handle
        .set_service_status(ServiceStatus {
            service_type: SERVICE_TYPE,
            current_state: WinServiceState::StartPending,
            controls_accepted: ServiceControlAccept::empty(),
            exit_code: ServiceExitCode::Win32(0),
            checkpoint: 0,
            wait_hint: Duration::from_secs(5),
            process_id: None,
        })
        .context("Failed to set the service start-pending status")?;

    let runtime = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
        .context("Failed to create the service runtime")?;

    runtime.block_on(async {
        logging::info("Windows service is running");
        let (shutdown_tx, shutdown_rx) = oneshot::channel();
        let state = ServiceState::new(shutdown_tx);
        let server_state = state.clone();
        let heartbeat_state = state.clone();
        let server = tokio::spawn(run_server(server_state, shutdown_rx));
        let heartbeat = tokio::spawn(async move {
            heartbeat_state.monitor_heartbeat().await;
        });
        let control = tokio::task::spawn_blocking(move || control_rx.recv());

        status_handle
            .set_service_status(ServiceStatus {
                service_type: SERVICE_TYPE,
                current_state: WinServiceState::Running,
                controls_accepted: ServiceControlAccept::STOP | ServiceControlAccept::SHUTDOWN,
                exit_code: ServiceExitCode::Win32(0),
                checkpoint: 0,
                wait_hint: Duration::default(),
                process_id: None,
            })
            .context("Failed to set the service running status")?;

        let (shutdown_source, service_result) = tokio::select! {
            result = server => {
                let result = result
                    .context("Service IPC task failed")
                    .and_then(|server_result| server_result);
                ("ipc", result)
            }
            result = control => {
                let source = result.map_or("control-task-failed", |control_result| {
                    control_result.unwrap_or("control-channel-closed")
                });
                (source, Ok(()))
            }
        };
        logging::info(format!(
            "Windows service shutdown started (origin: {shutdown_source})"
        ));
        // 核心退出最多等待 5 秒，向 SCM 申报 8 秒停止预算。
        let stop_pending_result = status_handle
            .set_service_status(ServiceStatus {
                service_type: SERVICE_TYPE,
                current_state: WinServiceState::StopPending,
                controls_accepted: ServiceControlAccept::empty(),
                exit_code: ServiceExitCode::Win32(0),
                checkpoint: 1,
                wait_hint: Duration::from_secs(8),
                process_id: None,
            })
            .context("Failed to set the service stop-pending status");
        heartbeat.abort();
        state.stop_core().await;
        logging::info(format!(
            "Windows service shutdown completed (origin: {shutdown_source})"
        ));
        if let Err(error) = stop_pending_result {
            logging::warn(format!(
                "Failed to report Windows service stop-pending status: {error:#}"
            ));
        }

        status_handle
            .set_service_status(ServiceStatus {
                service_type: SERVICE_TYPE,
                current_state: WinServiceState::Stopped,
                controls_accepted: ServiceControlAccept::empty(),
                exit_code: ServiceExitCode::Win32(0),
                checkpoint: 0,
                wait_hint: Duration::default(),
                process_id: None,
            })
            .context("Failed to set the service stopped status")?;

        service_result
    })
}
