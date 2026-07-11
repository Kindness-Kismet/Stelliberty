use anyhow::{Result, anyhow};
use features_test::{
    EMPTY_CONFIG, launch_via_ipc, prepare_test_dir, read_asset, sync_shared_assets,
};
use hub::capabilities::overrides;

#[tokio::main(flavor = "multi_thread")]
async fn main() -> Result<()> {
    sync_shared_assets()?;
    let dir = prepare_test_dir("js_override")?;

    let js_code = read_asset("override.js")?;
    let merged = overrides::apply_js(EMPTY_CONFIG, &js_code)?;
    if merged.trim().is_empty() {
        return Err(anyhow!("JS override output is empty"));
    }

    let handle = launch_via_ipc(&dir, &merged).await?;
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
