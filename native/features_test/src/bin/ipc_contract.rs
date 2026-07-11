use std::path::Path;
use std::time::Duration;

use anyhow::{Result, anyhow};
use features_test::{
    EMPTY_CONFIG, HubHandle, launch_via_ipc, prepare_test_dir, sync_shared_assets,
};
use serde_json::Value;

#[tokio::main(flavor = "multi_thread")]
async fn main() -> Result<()> {
    sync_shared_assets()?;
    let dir = prepare_test_dir("ipc_contract")?;

    let handle = launch_via_ipc(&dir, EMPTY_CONFIG).await?;
    let contract_result = verify_contract(&handle, &dir).await;
    let shutdown_result = handle.shutdown().await;
    contract_result?;
    shutdown_result?;
    Ok(())
}

async fn verify_contract(handle: &HubHandle, dir: &Path) -> Result<()> {
    let status = handle.status().await?;
    require_string(&status, "state", "running")?;
    require_positive_i64(&status, "pid")?;
    require_non_empty_string(&status, "external_controller")?;
    require_null_or_string(&status, "last_error")?;
    println!("  status contract fields are complete");

    let missing_path = handle
        .request_error(
            "core.apply_config",
            serde_json::json!({ "subscription_id": "contract" }),
        )
        .await?;
    require_error_code(&missing_path, "hub.internal")?;

    let invalid_yaml = dir.join("invalid.yaml");
    std::fs::write(&invalid_yaml, "proxy-groups: [")?;
    let invalid = handle
        .request_error(
            "core.apply_config",
            serde_json::json!({
                "runtime_yaml_path": invalid_yaml.display().to_string(),
                "subscription_id": "contract",
            }),
        )
        .await?;
    require_error_code(&invalid, "core.yaml_invalid")?;
    println!("  apply_config error code is stable");

    let valid_yaml = dir.join("valid.yaml");
    std::fs::write(&valid_yaml, EMPTY_CONFIG)?;
    let apply = handle
        .request(
            "core.apply_config",
            serde_json::json!({
                "runtime_yaml_path": valid_yaml.display().to_string(),
                "subscription_id": "contract",
            }),
        )
        .await?;
    require_apply_mode(&apply)?;
    require_positive_i64(&apply, "pid")?;
    println!("  apply_config success response is stable");

    let stop = handle.request("core.stop", serde_json::json!({})).await?;
    require_empty_object(&stop)?;
    let stopped = handle.status().await?;
    require_string(&stopped, "state", "stopped")?;
    require_null(&stopped, "pid")?;

    let start = handle.request("core.start", serde_json::json!({})).await?;
    require_positive_i64(&start, "pid")?;
    wait_running(handle).await?;

    let restart = handle
        .request("core.restart", serde_json::json!({}))
        .await?;
    require_positive_i64(&restart, "pid")?;
    wait_running(handle).await?;
    println!("  start/stop/restart methods are stable");

    let unknown = handle
        .request_error("core.unknown", serde_json::json!({}))
        .await?;
    require_error_code(&unknown, "hub.internal")?;
    Ok(())
}

async fn wait_running(handle: &HubHandle) -> Result<()> {
    let deadline = tokio::time::Instant::now() + Duration::from_secs(12);
    loop {
        let status = handle.status().await?;
        let state = get_string(&status, "state")?;
        if state == "running" {
            require_positive_i64(&status, "pid")?;
            return Ok(());
        }
        if state == "crashed" {
            return Err(anyhow!("core entered crashed: {status}"));
        }
        if tokio::time::Instant::now() >= deadline {
            return Err(anyhow!("Timed out waiting for core running: {status}"));
        }
        tokio::time::sleep(Duration::from_millis(200)).await;
    }
}

fn require_apply_mode(value: &Value) -> Result<()> {
    let mode = get_string(value, "mode")?;
    if mode == "reload" || mode == "restart" {
        return Ok(());
    }
    Err(anyhow!("mode={mode}, expected reload or restart"))
}

fn require_error_code(value: &Value, expected: &str) -> Result<()> {
    require_string(value, "code", expected)?;
    require_non_empty_string(value, "message")
}

fn require_empty_object(value: &Value) -> Result<()> {
    match value.as_object() {
        Some(map) if map.is_empty() => Ok(()),
        _ => Err(anyhow!("Expected an empty object, got {value}")),
    }
}

fn require_null(value: &Value, field: &str) -> Result<()> {
    match value.get(field) {
        Some(Value::Null) => Ok(()),
        _ => Err(anyhow!("Field {field} expected null, got {value}")),
    }
}

fn require_null_or_string(value: &Value, field: &str) -> Result<()> {
    match value.get(field) {
        Some(Value::Null) => Ok(()),
        Some(Value::String(_)) => Ok(()),
        _ => Err(anyhow!(
            "Field {field} expected null or string, got {value}"
        )),
    }
}

fn require_non_empty_string(value: &Value, field: &str) -> Result<()> {
    let actual = get_string(value, field)?;
    if actual.is_empty() {
        return Err(anyhow!("Field {field} must not be empty"));
    }
    Ok(())
}

fn require_string(value: &Value, field: &str, expected: &str) -> Result<()> {
    let actual = get_string(value, field)?;
    if actual == expected {
        return Ok(());
    }
    Err(anyhow!("Field {field}={actual}, expected {expected}"))
}

fn require_positive_i64(value: &Value, field: &str) -> Result<i64> {
    let actual = value
        .get(field)
        .and_then(Value::as_i64)
        .ok_or_else(|| anyhow!("Field {field} is missing or not a number: {value}"))?;
    if actual <= 0 {
        return Err(anyhow!(
            "Field {field}={actual}, expected a positive number"
        ));
    }
    Ok(actual)
}

fn get_string<'a>(value: &'a Value, field: &str) -> Result<&'a str> {
    value
        .get(field)
        .and_then(Value::as_str)
        .ok_or_else(|| anyhow!("Field {field} is missing or not a string: {value}"))
}
