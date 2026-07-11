use anyhow::{Result, anyhow};
use features_test::{EMPTY_CONFIG, launch_via_ipc, prepare_test_dir, sync_shared_assets};

#[tokio::main(flavor = "multi_thread")]
async fn main() -> Result<()> {
    sync_shared_assets()?;
    let dir = prepare_test_dir("empty_start")?;

    let handle = launch_via_ipc(&dir, EMPTY_CONFIG).await?;
    let snap = handle.status().await?;
    let state = snap
        .get("state")
        .and_then(|v| v.as_str())
        .ok_or_else(|| anyhow!("status is missing the state field"))?;
    if state != "running" {
        handle.shutdown().await?;
        return Err(anyhow!("core state={state}, expected running"));
    }
    println!("  IPC status state=running pid={}", snap["pid"]);
    handle.shutdown().await?;
    Ok(())
}
