use anyhow::{Result, anyhow};
use features_test::{
    EMPTY_CONFIG, launch_via_ipc, prepare_test_dir, read_asset, sync_shared_assets,
};
use hub::capabilities::overrides;

#[tokio::main(flavor = "multi_thread")]
async fn main() -> Result<()> {
    sync_shared_assets()?;
    let dir = prepare_test_dir("config_switch")?;

    let yaml_over = read_asset("override.yaml")?;
    let js_over = read_asset("override.js")?;
    let after_yaml = overrides::apply_yaml(EMPTY_CONFIG, &yaml_over)?;
    let after_js = overrides::apply_js(EMPTY_CONFIG, &js_over)?;

    let yaml_path = dir.join("runtime_yaml.yaml");
    std::fs::write(&yaml_path, &after_yaml)?;
    let js_path = dir.join("runtime_js.yaml");
    std::fs::write(&js_path, &after_js)?;

    let handle = launch_via_ipc(&dir, EMPTY_CONFIG).await?;

    let r1 = handle.apply_config(&yaml_path).await?;
    let mode1 = r1
        .get("mode")
        .and_then(|v| v.as_str())
        .ok_or_else(|| anyhow!("first apply_config response is missing mode"))?;
    println!("  step1 yaml override -> mode={mode1} pid={}", r1["pid"]);
    if mode1 != "reload" {
        handle.shutdown().await?;
        return Err(anyhow!("step1 expected reload, got {mode1}"));
    }

    let r2 = handle.apply_config(&js_path).await?;
    let mode2 = r2
        .get("mode")
        .and_then(|v| v.as_str())
        .ok_or_else(|| anyhow!("second apply_config response is missing mode"))?;
    println!("  step2 js override -> mode={mode2} pid={}", r2["pid"]);
    if mode2 != "reload" {
        handle.shutdown().await?;
        return Err(anyhow!("step2 expected reload, got {mode2}"));
    }

    handle.shutdown().await?;
    Ok(())
}
