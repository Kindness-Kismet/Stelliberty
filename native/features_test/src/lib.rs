use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use anyhow::{Context, Result, anyhow};
use hub::infra::core_runtime::CoreRuntime;
use hub::infra::ipc::{IpcServer, MethodHandler, Outgoing};
use hub::infra::paths::HubPaths;
#[cfg(unix)]
use interprocess::local_socket::{GenericFilePath, ToFsName};
#[cfg(windows)]
use interprocess::local_socket::{GenericNamespaced, ToNsName};
use interprocess::local_socket::{
    tokio::Stream as TokioIpcStream, traits::tokio::Stream as TokioStreamExt,
};
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::sync::{broadcast, mpsc};

pub const EMPTY_CONFIG: &str =
    "mixed-port: 0\nmode: rule\nlog-level: silent\nproxies: []\nproxy-groups: []\nrules: []\n";

pub fn repo_root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .parent()
        .and_then(Path::parent)
        .map(Path::to_path_buf)
        .unwrap_or_else(|| PathBuf::from("."))
}

pub fn workspace_root() -> PathBuf {
    repo_root().join("build").join("test_workspace")
}

pub fn assets_dir() -> PathBuf {
    repo_root().join("scripts").join("assets")
}

pub fn host_rid() -> &'static str {
    if cfg!(all(target_os = "windows", target_arch = "x86_64")) {
        "win-x64"
    } else if cfg!(all(target_os = "windows", target_arch = "aarch64")) {
        "win-arm64"
    } else if cfg!(all(target_os = "linux", target_arch = "x86_64")) {
        "linux-x64"
    } else if cfg!(all(target_os = "linux", target_arch = "aarch64")) {
        "linux-arm64"
    } else if cfg!(all(target_os = "macos", target_arch = "x86_64")) {
        "macos-x64"
    } else if cfg!(all(target_os = "macos", target_arch = "aarch64")) {
        "macos-arm64"
    } else {
        "unknown"
    }
}

pub fn pre_assets_dir() -> PathBuf {
    repo_root()
        .join("build")
        .join("pre_assets")
        .join(host_rid())
}

pub fn core_binary_name() -> &'static str {
    if cfg!(target_os = "windows") {
        "clash-mihomo-core.exe"
    } else {
        "clash-mihomo-core"
    }
}

pub fn sync_shared_assets() -> Result<()> {
    let workspace = workspace_root();
    std::fs::create_dir_all(&workspace)
        .with_context(|| format!("Failed to create test workspace: {}", workspace.display()))?;

    let source_dir = pre_assets_dir();
    if !source_dir.exists() {
        return Err(anyhow!(
            "Prebuilt assets are missing: {}\nRun this first: python scripts/prebuild.py",
            source_dir.display()
        ));
    }

    for entry in std::fs::read_dir(&source_dir)
        .with_context(|| format!("Failed to read prebuilt assets: {}", source_dir.display()))?
    {
        let entry = entry.context("Failed to enumerate prebuilt assets")?;
        let from = entry.path();
        if !from.is_file() {
            continue;
        }
        let file_name = from
            .file_name()
            .ok_or_else(|| anyhow!("Invalid file name: {}", from.display()))?;
        let to = workspace.join(file_name);
        copy_if_changed(&from, &to)?;
    }
    Ok(())
}

fn copy_if_changed(from: &Path, to: &Path) -> Result<()> {
    if let (Ok(dst), Ok(src)) = (std::fs::metadata(to), std::fs::metadata(from))
        && src.len() == dst.len()
    {
        return Ok(());
    }
    std::fs::copy(from, to).with_context(|| {
        format!(
            "Failed to copy asset: {} -> {}",
            from.display(),
            to.display()
        )
    })?;
    Ok(())
}

pub fn prepare_test_dir(name: &str) -> Result<PathBuf> {
    let dir = workspace_root().join(name);
    if dir.exists() {
        std::fs::remove_dir_all(&dir)
            .with_context(|| format!("Failed to clean old test directory: {}", dir.display()))?;
    }
    std::fs::create_dir_all(&dir)
        .with_context(|| format!("Failed to create test directory: {}", dir.display()))?;
    Ok(dir)
}

pub fn read_asset(name: &str) -> Result<String> {
    let path = assets_dir().join(name);
    std::fs::read_to_string(&path)
        .with_context(|| format!("Failed to read test sample: {}", path.display()))
}

pub struct HubHandle {
    pub pipe_name: String,
    core: Arc<CoreRuntime>,
    shutdown_tx: mpsc::Sender<()>,
    server_task: tokio::task::JoinHandle<()>,
}

pub async fn launch_via_ipc(test_dir: &Path, bootstrap_yaml: &str) -> Result<HubHandle> {
    let uuid = uuid::Uuid::new_v4();
    let pipe_name = local_ipc_endpoint(test_dir, "ipc", uuid);
    let mihomo_pipe = local_ipc_endpoint(test_dir, "mihomo", uuid);
    let mihomo = workspace_root().join(core_binary_name());
    if !mihomo.exists() {
        return Err(anyhow!(
            "Core binary is missing: {}\nRun this first: python scripts/prebuild.py",
            mihomo.display()
        ));
    }

    let final_bootstrap = inject_mihomo_pipe(bootstrap_yaml, &mihomo_pipe)?;
    let paths = HubPaths::new(test_dir.to_path_buf(), mihomo, workspace_root());
    paths.ensure_dirs()?;

    let (events_tx, mut events_rx) = broadcast::channel(128);
    let core = Arc::new(CoreRuntime::new(
        paths,
        mihomo_pipe,
        final_bootstrap,
        events_tx.clone(),
    )?);
    core.start_core().await?;

    let handler: Arc<dyn MethodHandler> = core.clone();
    let (server, _events, shutdown_tx) = IpcServer::new(handler);
    let server_cl = server.clone();
    let pipe_cl = pipe_name.clone();
    let server_task = tokio::spawn(async move {
        if let Err(e) = server_cl.serve(&pipe_cl).await {
            eprintln!("ipc server exited: {e}");
        }
    });

    let deadline = tokio::time::Instant::now() + Duration::from_secs(20);
    loop {
        if tokio::time::Instant::now() >= deadline {
            let _ = core.stop_core().await;
            stop_server(shutdown_tx, server_task).await;
            return Err(anyhow!("Timed out waiting for core to enter Running"));
        }
        let next = tokio::time::timeout(Duration::from_millis(500), events_rx.recv()).await;
        if let Ok(Ok(Outgoing::Event { event, data })) = next
            && event == "core.state_changed"
        {
            let state = data.get("state").and_then(|v| v.as_str()).unwrap_or("");
            if state == "running" {
                break;
            }
            if state == "crashed" {
                let reason = data
                    .get("reason")
                    .and_then(|v| v.as_str())
                    .unwrap_or("(none)");
                let _ = core.stop_core().await;
                stop_server(shutdown_tx, server_task).await;
                return Err(anyhow!("Core crashed: {reason}"));
            }
        }
    }

    Ok(HubHandle {
        pipe_name,
        core,
        shutdown_tx,
        server_task,
    })
}

fn inject_mihomo_pipe(bootstrap_yaml: &str, mihomo_pipe: &str) -> Result<String> {
    let mut yaml: serde_yaml_ng::Value =
        serde_yaml_ng::from_str(bootstrap_yaml).context("Failed to parse bootstrap yaml")?;
    if let serde_yaml_ng::Value::Mapping(map) = &mut yaml {
        #[cfg(unix)]
        let (insert_key, remove_key) = ("external-controller-unix", "external-controller-pipe");
        #[cfg(windows)]
        let (insert_key, remove_key) = ("external-controller-pipe", "external-controller-unix");
        map.insert(
            serde_yaml_ng::Value::String(insert_key.into()),
            serde_yaml_ng::Value::String(mihomo_pipe.to_owned()),
        );
        map.remove(serde_yaml_ng::Value::String("external-controller".into()));
        map.remove(serde_yaml_ng::Value::String(remove_key.into()));
        map.remove(serde_yaml_ng::Value::String("secret".into()));
    }
    serde_yaml_ng::to_string(&yaml).context("Failed to serialize bootstrap yaml")
}

fn local_ipc_endpoint(test_dir: &Path, endpoint_type: &str, uuid: uuid::Uuid) -> String {
    #[cfg(windows)]
    let _ = test_dir;
    #[cfg(unix)]
    {
        let _ = test_dir;
        std::env::temp_dir()
            .join(format!("stb-{endpoint_type}-{}.sock", uuid.simple()))
            .to_string_lossy()
            .into_owned()
    }
    #[cfg(windows)]
    {
        if endpoint_type == "mihomo" {
            format!(r"\\.\pipe\stelliberty.features_test.{endpoint_type}.{uuid}")
        } else {
            format!("stelliberty.features_test.{endpoint_type}.{uuid}")
        }
    }
}

impl HubHandle {
    pub async fn request(
        &self,
        method: &str,
        params: serde_json::Value,
    ) -> Result<serde_json::Value> {
        ipc_call(
            &self.pipe_name,
            serde_json::json!({
                "id": uuid::Uuid::new_v4().to_string(),
                "method": method,
                "params": params,
            }),
        )
        .await
    }

    pub async fn request_error(
        &self,
        method: &str,
        params: serde_json::Value,
    ) -> Result<serde_json::Value> {
        let response = ipc_exchange(
            &self.pipe_name,
            serde_json::json!({
                "id": uuid::Uuid::new_v4().to_string(),
                "method": method,
                "params": params,
            }),
        )
        .await?;
        if let Some(error) = response.get("error") {
            return Ok(error.clone());
        }
        Err(anyhow!(
            "Expected an IPC error, but received a success response: {response}"
        ))
    }

    pub async fn apply_config(&self, runtime_yaml: &Path) -> Result<serde_json::Value> {
        self.request(
            "core.apply_config",
            serde_json::json!({
                "runtime_yaml_path": runtime_yaml.display().to_string(),
                "subscription_id": "features_test",
            }),
        )
        .await
    }

    pub async fn status(&self) -> Result<serde_json::Value> {
        self.request("core.status", serde_json::json!({})).await
    }

    pub async fn shutdown(self) -> Result<()> {
        let HubHandle {
            core,
            shutdown_tx,
            server_task,
            ..
        } = self;
        let stop_result = core.stop_core().await;
        stop_server(shutdown_tx, server_task).await;
        stop_result
    }
}

async fn stop_server(shutdown_tx: mpsc::Sender<()>, mut server_task: tokio::task::JoinHandle<()>) {
    let _ = shutdown_tx.send(()).await;
    tokio::select! {
        _ = &mut server_task => {}
        _ = tokio::time::sleep(Duration::from_secs(5)) => {
            server_task.abort();
            let _ = server_task.await;
        }
    }
}

async fn ipc_call(pipe: &str, request: serde_json::Value) -> Result<serde_json::Value> {
    let response = ipc_exchange(pipe, request).await?;
    if let Some(error) = response.get("error") {
        return Err(anyhow!("ipc error: {error}"));
    }
    Ok(response
        .get("result")
        .cloned()
        .unwrap_or(serde_json::Value::Null))
}

async fn ipc_exchange(pipe: &str, request: serde_json::Value) -> Result<serde_json::Value> {
    #[cfg(unix)]
    let name = pipe
        .to_fs_name::<GenericFilePath>()
        .context("Failed to convert ipc socket name")?;
    #[cfg(windows)]
    let name = pipe
        .to_ns_name::<GenericNamespaced>()
        .context("Failed to convert ipc pipe name")?;
    let stream = TokioIpcStream::connect(name)
        .await
        .context("Failed to connect to ipc")?;
    let (reader, mut writer) = stream.split();
    let mut reader = BufReader::new(reader);
    let line = serde_json::to_string(&request)?;
    writer.write_all(line.as_bytes()).await?;
    writer.write_all(b"\n").await?;
    writer.flush().await?;

    let req_id = request
        .get("id")
        .and_then(|v| v.as_str())
        .ok_or_else(|| anyhow!("Request is missing id"))?;
    let mut response = String::new();
    loop {
        response.clear();
        let n = reader
            .read_line(&mut response)
            .await
            .context("ipc read failed")?;
        if n == 0 {
            return Err(anyhow!("ipc closed early"));
        }
        let parsed: serde_json::Value = serde_json::from_str(response.trim())
            .with_context(|| format!("Failed to parse ipc response: {}", response.trim()))?;
        if parsed.get("id").and_then(|v| v.as_str()) == Some(req_id) {
            return Ok(parsed);
        }
    }
}
