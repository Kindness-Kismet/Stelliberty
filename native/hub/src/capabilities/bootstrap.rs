use std::path::PathBuf;
use std::sync::mpsc::RecvTimeoutError;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use anyhow::anyhow;
use interoptopus::ffi;
use once_cell::sync::OnceCell;
use tokio::runtime::Builder;
use tokio::sync::{broadcast, mpsc};

use crate::infra::core_runtime::CoreRuntime;
use crate::infra::{
    ipc::{IpcServer, MethodHandler},
    paths::HubPaths,
    runtime,
};

struct HubInstance {
    shutdown_tx: Mutex<Option<mpsc::Sender<()>>>,
    core: Arc<CoreRuntime>,
}

static INSTANCE: OnceCell<HubInstance> = OnceCell::new();

enum CoreTask {
    StartConfirmed,
    ActivateConfig(String),
    Stop,
}

fn init_tracing() {
    if std::env::var_os("RUST_LOG").is_none() {
        return;
    }

    let _ = tracing_subscriber::fmt()
        .with_env_filter(tracing_subscriber::EnvFilter::from_default_env())
        .try_init();
}

fn run_core_task(core: Arc<CoreRuntime>, task: CoreTask, timeout: Duration) -> anyhow::Result<()> {
    let handle = runtime::handle().ok_or_else(|| anyhow!("tokio runtime is not installed"))?;
    let (done_tx, done_rx) = std::sync::mpsc::channel();
    handle.spawn(async move {
        let result = match task {
            CoreTask::StartConfirmed => core.start_core().await,
            CoreTask::ActivateConfig(config) => core.start_core_with_config(config).await,
            CoreTask::Stop => core.stop_core().await,
        };
        let _ = done_tx.send(result);
    });

    match done_rx.recv_timeout(timeout) {
        Ok(result) => result,
        Err(RecvTimeoutError::Timeout) => Err(anyhow!("core task timed out")),
        Err(RecvTimeoutError::Disconnected) => Err(anyhow!("core task was interrupted")),
    }
}

#[ffi]
#[repr(C)]
pub struct BootstrapResult {
    pub ok: bool,
    pub message: ffi::String,
}

impl BootstrapResult {
    fn ok() -> Self {
        Self {
            ok: true,
            message: ffi::String::from_string("ok".into()),
        }
    }

    fn err(msg: impl Into<String>) -> Self {
        Self {
            ok: false,
            message: ffi::String::from_string(msg.into()),
        }
    }
}

fn run_bootstrap(
    pipe_name: String,
    core_path: PathBuf,
    data_core_dir: PathBuf,
    user_data_dir: PathBuf,
    core_pipe: String,
    bootstrap_yaml: String,
) -> anyhow::Result<HubInstance> {
    init_tracing();
    let rt = Builder::new_multi_thread().enable_all().build()?;
    let _ = runtime::install(rt);
    let handle =
        runtime::handle().ok_or_else(|| anyhow::anyhow!("tokio runtime is not installed"))?;

    let paths = HubPaths::new(user_data_dir, core_path, data_core_dir);
    paths.ensure_dirs()?;

    handle.block_on(async {
        let (events_tx, _) = broadcast::channel(128);
        let core = Arc::new(CoreRuntime::new(
            paths,
            core_pipe,
            bootstrap_yaml,
            events_tx.clone(),
        )?);
        let core_for_instance = core.clone();
        let handler: Arc<dyn MethodHandler> = core.clone();
        let (server, _events, shutdown_tx) = IpcServer::with_events(handler, events_tx);
        // IPC 先接收请求，FFI 再同步返回首次核心启动结果。
        let server_clone = server.clone();
        let pipe_clone = pipe_name.clone();
        tokio::spawn(async move {
            if let Err(e) = server_clone.serve(&pipe_clone).await {
                tracing::error!("ipc server exited: {e}");
            }
        });
        if let Err(e) = core.start_core().await {
            let _ = shutdown_tx.try_send(());
            return Err(e);
        }
        Ok::<HubInstance, anyhow::Error>(HubInstance {
            shutdown_tx: Mutex::new(Some(shutdown_tx)),
            core: core_for_instance,
        })
    })
}

#[ffi]
pub fn hub_bootstrap(
    pipe_name: ffi::String,
    core_path: ffi::String,
    data_core_dir: ffi::String,
    user_data_dir: ffi::String,
    core_pipe: ffi::String,
    bootstrap_yaml: ffi::String,
) -> BootstrapResult {
    let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let pipe = pipe_name.as_str().to_owned();
        let core = PathBuf::from(core_path.as_str());
        let data_core = PathBuf::from(data_core_dir.as_str());
        let user_data = PathBuf::from(user_data_dir.as_str());
        let core_pipe = core_pipe.as_str().to_owned();
        let boot = bootstrap_yaml.as_str().to_owned();
        if let Some(inst) = INSTANCE.get() {
            // Hub 路径和 IPC 生命周期固定，恢复核心时刷新当前启动配置。
            return match run_core_task(
                inst.core.clone(),
                CoreTask::ActivateConfig(boot),
                Duration::from_secs(10),
            ) {
                Ok(()) => BootstrapResult::ok(),
                Err(e) => BootstrapResult::err(format!("Core startup failed: {e:#}")),
            };
        }
        match run_bootstrap(pipe, core, data_core, user_data, core_pipe, boot) {
            Ok(inst) => {
                let _ = INSTANCE.set(inst);
                BootstrapResult::ok()
            }
            Err(e) => BootstrapResult::err(format!("bootstrap failed: {e:#}")),
        }
    }));
    match result {
        Ok(br) => br,
        Err(p) => BootstrapResult::err(format!("bootstrap panic：{p:?}")),
    }
}

#[ffi]
pub fn hub_start_core() -> BootstrapResult {
    match std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let Some(inst) = INSTANCE.get() else {
            return BootstrapResult::err("Hub is not initialized");
        };
        match run_core_task(
            inst.core.clone(),
            CoreTask::StartConfirmed,
            Duration::from_secs(10),
        ) {
            Ok(()) => BootstrapResult::ok(),
            Err(e) => BootstrapResult::err(format!("Core startup failed: {e:#}")),
        }
    })) {
        Ok(result) => result,
        Err(panic) => BootstrapResult::err(format!("core startup panic：{panic:?}")),
    }
}

#[ffi]
pub fn hub_stop_core() -> BootstrapResult {
    match std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let Some(inst) = INSTANCE.get() else {
            return BootstrapResult::err("Hub is not initialized");
        };
        // 普通模式核心停止总预算固定为 5 秒。
        match run_core_task(inst.core.clone(), CoreTask::Stop, Duration::from_secs(5)) {
            Ok(()) => BootstrapResult::ok(),
            Err(e) => BootstrapResult::err(format!("Core shutdown failed: {e:#}")),
        }
    })) {
        Ok(result) => result,
        Err(panic) => BootstrapResult::err(format!("core shutdown panic：{panic:?}")),
    }
}

#[ffi]
pub fn hub_shutdown() {
    // 先停核心再停 IPC，避免特权子进程滞留。
    let _ = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        if let Some(inst) = INSTANCE.get() {
            let _ = hub_stop_core();

            if let Ok(mut guard) = inst.shutdown_tx.lock()
                && let Some(tx) = guard.take()
            {
                let _ = tx.try_send(());
            }
        }
    }));
}
