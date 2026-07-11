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

    let (control_tx, control_rx) = mpsc::channel::<()>();
    let event_handler = move |control_event| -> ServiceControlHandlerResult {
        match control_event {
            ServiceControl::Stop => {
                logging::info("Windows service stop control received");
                let _ = control_tx.send(());
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
                controls_accepted: ServiceControlAccept::STOP,
                exit_code: ServiceExitCode::Win32(0),
                checkpoint: 0,
                wait_hint: Duration::default(),
                process_id: None,
            })
            .context("Failed to set the service running status")?;

        tokio::select! {
            result = server => {
                result.context("Service IPC task failed")??;
            }
            _ = control => {
                logging::info("Windows service control task ended");
            }
        }
        heartbeat.abort();
        state.stop_core().await;
        logging::info("Windows service stopped");

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

        Ok::<(), anyhow::Error>(())
    })
}
