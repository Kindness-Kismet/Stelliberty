use anyhow::{Result, anyhow};
use features_test::{
    EMPTY_CONFIG, launch_via_ipc, prepare_test_dir, read_asset, sync_shared_assets,
};
use hub::capabilities::overrides;

#[tokio::main(flavor = "multi_thread")]
async fn main() -> Result<()> {
    sync_shared_assets()?;
    let dir = prepare_test_dir("combo_override")?;

    let yaml_over = read_asset("override.yaml")?;
    let js_over = read_asset("override.js")?;
    let after_yaml = overrides::apply_yaml(EMPTY_CONFIG, &yaml_over)?;
    if !after_yaml.contains("allow-lan") {
        return Err(anyhow!("YAML stage is missing allow-lan"));
    }
    let after_js = overrides::apply_js(&after_yaml, &js_over)?;

    let handle = launch_via_ipc(&dir, &after_js).await?;
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
