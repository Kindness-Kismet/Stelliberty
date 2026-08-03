use std::path::PathBuf;

#[derive(Debug, Clone)]
pub struct HubPaths {
    pub runtime_dir: PathBuf,
    pub bootstrap_yaml: PathBuf,
    pub active_yaml: PathBuf,
    pub core_path: PathBuf,
    pub data_core_dir: PathBuf,
}

impl HubPaths {
    pub fn new(user_data_dir: PathBuf, core_path: PathBuf, data_core_dir: PathBuf) -> Self {
        let runtime_dir = user_data_dir.join("runtime");

        let bootstrap_yaml = data_core_dir.join("_bootstrap.yaml");
        let active_yaml = data_core_dir.join("_active.yaml");
        Self {
            runtime_dir,
            bootstrap_yaml,
            active_yaml,
            core_path,
            data_core_dir,
        }
    }

    pub fn ensure_dirs(&self) -> std::io::Result<()> {
        // 资源部署拥有 data_core_dir；运行时只创建可写 runtime_dir。
        std::fs::create_dir_all(&self.runtime_dir)?;
        Ok(())
    }
}
