use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", content = "data")]
pub enum ServiceCommand {
    Status,
    Heartbeat,
    Logs {
        lines: usize,
    },
    StartCore {
        mihomo_path: String,
        config_path: String,
        data_core_dir: String,
    },
    StopCore,
    RestartCore,
    Shutdown,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", content = "data")]
pub enum ServiceResponse {
    Success {
        message: Option<String>,
    },
    Error {
        code: String,
        message: String,
    },
    Status {
        service_name: String,
        version: String,
        uptime_seconds: u64,
        last_heartbeat_seconds: Option<u64>,
        core_state: String,
        core_pid: Option<u32>,
        core_last_error: Option<String>,
    },
    Logs {
        lines: Vec<String>,
    },
    HeartbeatAck,
}
