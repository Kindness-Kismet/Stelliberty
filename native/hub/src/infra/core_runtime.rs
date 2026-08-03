use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use anyhow::{Context, Result};
use serde::Serialize;
use tokio::sync::{Mutex, broadcast};
use tokio::task::JoinHandle;

use crate::infra::core_api::{ApiError, CoreApiClient};
use crate::infra::ipc::{ErrorBody, MethodHandler, Outgoing};
use crate::infra::paths::HubPaths;
use crate::infra::process::{self, ChildHandle};
use crate::util::yaml_diff;

#[derive(Debug, Clone, Copy, Serialize)]
#[serde(rename_all = "snake_case")]
pub enum CoreState {
    Starting,
    Running,
    Stopping,
    Stopped,
    Crashed,
    Unavailable,
}

pub struct CoreRuntime {
    paths: HubPaths,
    core_pipe: String,
    api: CoreApiClient,

    state: Arc<Mutex<CoreInner>>,
    lifecycle: Arc<Mutex<()>>,
    events: broadcast::Sender<Outgoing>,
}

struct CoreInner {
    state: CoreState,
    child: Option<ChildHandle>,
    log_task: Option<JoinHandle<()>>,
    desired_yaml: String,
    last_active_yaml: Option<serde_yaml_ng::Value>,
    last_error: Option<String>,
    // 世代号隔离过期后台任务，防止旧启动流程回写新状态。
    generation: u64,
}

struct PendingCoreConfig {
    desired_yaml: String,
    active_yaml: serde_yaml_ng::Value,
}

fn broadcast_state(events: &broadcast::Sender<Outgoing>, inner: &CoreInner) {
    let pid = inner.child.as_ref().map(|c| c.pid);
    let data = serde_json::json!({
        "state": inner.state,
        "pid": pid,
        "reason": inner.last_error.clone(),
    });
    let _ = events.send(Outgoing::Event {
        event: "core.state_changed".into(),
        data,
    });
}

fn broadcast_core_log(events: &broadcast::Sender<Outgoing>, line: String) {
    let data = serde_json::json!({ "line": line });
    let _ = events.send(Outgoing::Event {
        event: "core_logs.entry".into(),
        data,
    });
}

fn spawn_core_log_stream(
    api: CoreApiClient,
    events: broadcast::Sender<Outgoing>,
    level: String,
) -> JoinHandle<()> {
    tokio::spawn(async move {
        if let Err(e) = api
            .stream_logs(&level, move |line| broadcast_core_log(&events, line))
            .await
        {
            tracing::warn!("Core log stream ended: {e}");
        }
    })
}

fn read_log_level(yaml: &serde_yaml_ng::Value) -> String {
    let level = yaml
        .as_mapping()
        .and_then(|map| map.get(serde_yaml_ng::Value::String("log-level".into())))
        .and_then(serde_yaml_ng::Value::as_str)
        .unwrap_or("info")
        .to_ascii_lowercase();
    match level.as_str() {
        "debug" | "info" | "warning" | "error" | "silent" => level,
        _ => "info".into(),
    }
}

fn replace_log_stream(
    inner: &mut CoreInner,
    api: CoreApiClient,
    events: broadcast::Sender<Outgoing>,
    level: &str,
) {
    if let Some(previous) = inner.log_task.take() {
        previous.abort();
    }

    if level == "silent" {
        return;
    }

    inner.log_task = Some(spawn_core_log_stream(api, events, level.to_owned()));
}

impl CoreRuntime {
    pub fn new(
        paths: HubPaths,
        core_pipe: String,
        bootstrap_yaml: String,
        events: broadcast::Sender<Outgoing>,
    ) -> Result<Self> {
        let api = CoreApiClient::new(&core_pipe);
        Ok(Self {
            paths,
            core_pipe,
            api,
            state: Arc::new(Mutex::new(CoreInner {
                state: CoreState::Unavailable,
                child: None,
                log_task: None,
                desired_yaml: bootstrap_yaml,
                last_active_yaml: None,
                last_error: None,
                generation: 0,
            })),
            lifecycle: Arc::new(Mutex::new(())),
            events,
        })
    }

    async fn start_if_needed_locked(&self) -> Result<()> {
        let (already_running, has_child) = {
            let inner = self.state.lock().await;
            (
                matches!(inner.state, CoreState::Running | CoreState::Starting),
                inner.child.is_some(),
            )
        };
        if already_running {
            return Ok(());
        }
        if has_child {
            self.stop_core_process().await?;
        }

        self.start_initial_locked().await
    }

    async fn start_initial_locked(&self) -> Result<()> {
        self.paths.ensure_dirs()?;
        let desired_yaml = {
            let inner = self.state.lock().await;
            inner.desired_yaml.clone()
        };
        // 先写 bootstrap，让活动 YAML 差异基线匹配核心实际配置。
        std::fs::write(&self.paths.bootstrap_yaml, desired_yaml)
            .context("Failed to write bootstrap yaml")?;
        let (initial_yaml, _) = self.rewrite_active_yaml(&self.paths.bootstrap_yaml).await?;
        let log_level = read_log_level(&initial_yaml);
        {
            let mut inner = self.state.lock().await;

            inner.last_active_yaml = Some(initial_yaml);
        }
        self.start_core_process(&self.paths.active_yaml, log_level, None)
            .await
    }

    async fn start_core_process(
        &self,
        yaml_path: &Path,
        log_level: String,
        pending_config: Option<PendingCoreConfig>,
    ) -> Result<()> {
        let generation = {
            let mut inner = self.state.lock().await;
            inner.generation += 1;
            inner.state = CoreState::Starting;
            inner.last_error = None;
            broadcast_state(&self.events, &inner);
            inner.generation
        };
        let child =
            match process::spawn(&self.paths.core_path, yaml_path, &self.paths.data_core_dir) {
                Ok(c) => c,
                Err(e) => {
                    let mut inner = self.state.lock().await;
                    inner.state = CoreState::Crashed;
                    inner.last_error = Some(format!("spawn failed: {e}"));
                    broadcast_state(&self.events, &inner);
                    return Err(e.context("Failed to spawn core"));
                }
            };
        let pid = child.pid;
        // 等待器在 child 转移前创建，后续 future 不再借用句柄。
        let waiter = process::exit_waiter(&child);
        {
            let mut inner = self.state.lock().await;
            inner.child = Some(child);
        }

        match waiter {
            Ok(waiter) => {
                let state = self.state.clone();
                let events = self.events.clone();
                tokio::spawn(async move {
                    let exit_code = waiter.await;
                    let mut inner = state.lock().await;
                    // 退出事件可能来自旧进程，必须同时校验世代和 pid。
                    // 只有当前 starting/running 进程退出，才标记为崩溃。
                    let is_current_child = inner.generation == generation
                        && inner.child.as_ref().map(|c| c.pid) == Some(pid);
                    if !is_current_child {
                        return;
                    }

                    inner.child = None;
                    if matches!(inner.state, CoreState::Starting | CoreState::Running) {
                        inner.state = CoreState::Crashed;
                        inner.last_error = Some(if exit_code == u32::MAX {
                            "Core process exited unexpectedly with an unknown exit code".into()
                        } else {
                            format!("Core process exited unexpectedly with exit code {exit_code}")
                        });
                    }
                    broadcast_state(&events, &inner);
                });
            }
            Err(e) => tracing::warn!("Failed to create core exit watcher: {e}"),
        }

        let api = self.api.clone();
        let state = self.state.clone();
        let events = self.events.clone();
        tokio::spawn(async move {
            let ready = api.wait_ready(50, Duration::from_millis(200)).await;
            let mut inner = state.lock().await;
            // 就绪轮询最多 10 秒，完成时启动流程可能已经过期。
            // 过期结果不能覆盖后续 stop/restart 写入的状态。
            let is_current_start = inner.generation == generation
                && inner.child.as_ref().map(|c| c.pid) == Some(pid)
                && matches!(inner.state, CoreState::Starting);
            if !is_current_start {
                return;
            }

            match ready {
                Ok(_) => {
                    if let Some(config) = pending_config {
                        inner.desired_yaml = config.desired_yaml;
                        inner.last_active_yaml = Some(config.active_yaml);
                    }
                    replace_log_stream(&mut inner, api.clone(), events.clone(), &log_level);
                    inner.state = CoreState::Running;
                    broadcast_state(&events, &inner);
                }
                Err(e) => {
                    inner.state = CoreState::Crashed;
                    inner.last_error = Some(format!("api_timeout: {e}"));
                    broadcast_state(&events, &inner);
                }
            }
        });

        tracing::info!("core started pid={pid}");
        Ok(())
    }

    async fn stop_core_process(&self) -> Result<()> {
        let (child_opt, log_task) = {
            let mut inner = self.state.lock().await;
            // 主动停止推进世代，旧退出监视器不会把它记成崩溃。
            inner.generation += 1;
            inner.state = CoreState::Stopping;
            broadcast_state(&self.events, &inner);
            (inner.child.take(), inner.log_task.take())
        };
        if let Some(task) = log_task {
            // 先结束日志流，让旧管道客户端句柄释放。
            task.abort();
            let _ = task.await;
        }
        if let Some(child) = child_opt {
            process::shutdown(child, Duration::from_secs(5))
                .await
                .context("Failed to shut down core")?;
        }
        let mut inner = self.state.lock().await;
        inner.state = CoreState::Stopped;
        broadcast_state(&self.events, &inner);
        Ok(())
    }

    pub async fn stop_core(&self) -> Result<()> {
        let _lifecycle = self.lifecycle.lock().await;
        self.stop_core_process().await
    }

    pub async fn start_core(&self) -> Result<()> {
        let _lifecycle = self.lifecycle.lock().await;
        self.start_if_needed_locked().await
    }

    pub async fn start_core_with_config(&self, desired_yaml: String) -> Result<()> {
        let _lifecycle = self.lifecycle.lock().await;
        let has_child = self.state.lock().await.child.is_some();
        if has_child {
            self.stop_core_process().await?;
        }
        self.paths.ensure_dirs()?;
        std::fs::write(&self.paths.bootstrap_yaml, &desired_yaml)
            .context("Failed to write bootstrap yaml")?;
        let (active_yaml, _) = self.rewrite_active_yaml(&self.paths.bootstrap_yaml).await?;
        let log_level = read_log_level(&active_yaml);
        self.start_core_process(
            &self.paths.active_yaml,
            log_level,
            Some(PendingCoreConfig {
                desired_yaml,
                active_yaml,
            }),
        )
        .await
    }

    async fn rewrite_active_yaml(
        &self,
        source_path: &Path,
    ) -> Result<(serde_yaml_ng::Value, String)> {
        let text = std::fs::read_to_string(source_path)
            .with_context(|| format!("Failed to read runtime yaml: {}", source_path.display()))?;
        let mut yaml: serde_yaml_ng::Value =
            serde_yaml_ng::from_str(&text).context("Failed to parse yaml")?;
        if let serde_yaml_ng::Value::Mapping(map) = &mut yaml {
            // 只保留当前平台控制器端点，避免过期内联通道。
            #[cfg(unix)]
            let (insert_key, remove_key) = ("external-controller-unix", "external-controller-pipe");
            #[cfg(windows)]
            let (insert_key, remove_key) = ("external-controller-pipe", "external-controller-unix");
            map.insert(
                serde_yaml_ng::Value::String(insert_key.into()),
                serde_yaml_ng::Value::String(self.core_pipe.clone()),
            );
            map.remove(serde_yaml_ng::Value::String(remove_key.into()));
        }
        let serialized = serde_yaml_ng::to_string(&yaml).context("Failed to serialize yaml")?;
        std::fs::write(&self.paths.active_yaml, &serialized)
            .context("Failed to write _active.yaml")?;
        Ok((yaml, serialized))
    }

    pub async fn apply_config(
        &self,
        runtime_yaml_path: PathBuf,
        _subscription_id: String,
    ) -> std::result::Result<serde_json::Value, ErrorBody> {
        let _lifecycle = self.lifecycle.lock().await;
        {
            let inner = self.state.lock().await;
            if matches!(inner.state, CoreState::Stopping | CoreState::Starting) {
                return Err(ErrorBody {
                    code: "core.busy".into(),
                    message: "core is busy".into(),
                });
            }
        }

        let (new_yaml, desired_yaml) = match self.rewrite_active_yaml(&runtime_yaml_path).await {
            Ok(v) => v,
            Err(e) => {
                return Err(ErrorBody {
                    code: "core.yaml_invalid".into(),
                    message: format!("{e:#}"),
                });
            }
        };
        let log_level = read_log_level(&new_yaml);

        let needs_restart = {
            let inner = self.state.lock().await;
            // 核心未运行时没有重载目标；运行中只看启动期字段差异。
            let core_alive = inner.child.is_some() && matches!(inner.state, CoreState::Running);
            !core_alive
                || inner
                    .last_active_yaml
                    .as_ref()
                    .map(|prev| yaml_diff::needs_restart(prev, &new_yaml))
                    .unwrap_or(true)
        };

        if !needs_restart {
            let previous_log_level = {
                let mut inner = self.state.lock().await;
                let previous = inner
                    .last_active_yaml
                    .as_ref()
                    .map(read_log_level)
                    .unwrap_or_else(|| "info".into());
                replace_log_stream(
                    &mut inner,
                    self.api.clone(),
                    self.events.clone(),
                    &log_level,
                );
                previous
            };

            match self.api.put_config_path(&self.paths.active_yaml).await {
                Ok(()) => {
                    let _ = self.api.close_all_connections().await;
                    let mut inner = self.state.lock().await;
                    inner.desired_yaml = desired_yaml;
                    inner.last_active_yaml = Some(new_yaml);
                    let pid = current_pid(&inner)?;
                    return Ok(serde_json::json!({ "mode": "reload", "pid": pid }));
                }
                Err(e) => {
                    // mihomo 配置解析错误不能回退重启，否则会保留坏配置。
                    if let Some(ApiError::YamlInvalid(_)) = e.downcast_ref::<ApiError>() {
                        let mut inner = self.state.lock().await;
                        replace_log_stream(
                            &mut inner,
                            self.api.clone(),
                            self.events.clone(),
                            &previous_log_level,
                        );
                        return Err(ErrorBody {
                            code: "core.yaml_invalid".into(),
                            message: format!("{e:#}"),
                        });
                    }
                    tracing::warn!("reload failed; falling back to restart: {e}");
                }
            }
        }

        if let Err(e) = self.stop_core_process().await {
            return Err(ErrorBody {
                code: "hub.internal".into(),
                message: format!("{e:#}"),
            });
        }
        if let Err(e) = self
            .start_core_process(
                &self.paths.active_yaml,
                log_level,
                Some(PendingCoreConfig {
                    desired_yaml,
                    active_yaml: new_yaml,
                }),
            )
            .await
        {
            return Err(ErrorBody {
                code: "core.spawn_failed".into(),
                message: format!("{e:#}"),
            });
        }
        let inner = self.state.lock().await;
        let pid = current_pid(&inner)?;
        Ok(serde_json::json!({ "mode": "restart", "pid": pid }))
    }

    pub async fn status(&self) -> serde_json::Value {
        let inner = self.state.lock().await;
        serde_json::json!({
            "state": inner.state,
            "pid": inner.child.as_ref().map(|c| c.pid),
            "external_controller": self.core_pipe,
            "last_error": inner.last_error,
        })
    }
}

#[async_trait::async_trait]
impl MethodHandler for CoreRuntime {
    async fn handle(
        &self,
        method: &str,
        params: serde_json::Value,
    ) -> std::result::Result<serde_json::Value, ErrorBody> {
        match method {
            "core.status" => Ok(self.status().await),
            "core.start" => {
                let _lifecycle = self.lifecycle.lock().await;
                self.start_if_needed_locked().await.map_err(|e| ErrorBody {
                    code: "core.spawn_failed".into(),
                    message: format!("{e:#}"),
                })?;
                let inner = self.state.lock().await;
                Ok(serde_json::json!({ "pid": inner.child.as_ref().map(|c| c.pid) }))
            }
            "core.stop" => {
                let _lifecycle = self.lifecycle.lock().await;
                self.stop_core_process().await.map_err(|e| ErrorBody {
                    code: "hub.internal".into(),
                    message: format!("{e:#}"),
                })?;
                Ok(serde_json::json!({}))
            }
            "core.restart" => {
                let _lifecycle = self.lifecycle.lock().await;
                self.stop_core_process().await.map_err(|e| ErrorBody {
                    code: "hub.internal".into(),
                    message: format!("{e:#}"),
                })?;
                self.start_initial_locked().await.map_err(|e| ErrorBody {
                    code: "core.spawn_failed".into(),
                    message: format!("{e:#}"),
                })?;
                let inner = self.state.lock().await;
                Ok(serde_json::json!({ "pid": inner.child.as_ref().map(|c| c.pid) }))
            }
            "core.apply_config" => {
                let path = params
                    .get("runtime_yaml_path")
                    .and_then(|v| v.as_str())
                    .ok_or_else(|| ErrorBody {
                        code: "hub.internal".into(),
                        message: "params is missing runtime_yaml_path".into(),
                    })?;
                let sub_id = params
                    .get("subscription_id")
                    .and_then(|v| v.as_str())
                    .unwrap_or("")
                    .to_string();
                self.apply_config(PathBuf::from(path), sub_id).await
            }
            other => Err(ErrorBody {
                code: "hub.internal".into(),
                message: format!("Unknown method: {other}"),
            }),
        }
    }
}

fn current_pid(inner: &CoreInner) -> std::result::Result<u32, ErrorBody> {
    inner
        .child
        .as_ref()
        .map(|child| child.pid)
        .ok_or_else(|| ErrorBody {
            code: "core.exited".into(),
            message: "Core exited before the operation completed".into(),
        })
}
