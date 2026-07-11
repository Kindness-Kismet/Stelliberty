use anyhow::{Result, anyhow};
use features_test::{
    EMPTY_CONFIG, launch_via_ipc, prepare_test_dir, read_asset, sync_shared_assets,
};
use hub::capabilities::overrides;

#[tokio::main(flavor = "multi_thread")]
async fn main() -> Result<()> {
    sync_shared_assets()?;
    let dir = prepare_test_dir("yaml_override")?;

    let override_yaml = read_asset("override.yaml")?;
    let merged = overrides::apply_yaml(EMPTY_CONFIG, &override_yaml)?;
    if !merged.contains("allow-lan") || !merged.contains("DOMAIN-SUFFIX,example.com") {
        return Err(anyhow!(
            "YAML merge output did not contain the expected key\n{merged}"
        ));
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
