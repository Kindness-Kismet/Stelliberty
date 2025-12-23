// 应用文件路径管理服务，单例模式
// 负责管理所有目录和文件路径，避免路径逻辑分散

use once_cell::sync::Lazy;
use std::path::PathBuf;
use std::sync::RwLock;

// 路径服务单例
pub static PATH_SERVICE: Lazy<RwLock<PathService>> = Lazy::new(|| {
    let service = PathService::new().unwrap_or_else(|e| {
        eprintln!("[PathService] 初始化失败：{}，使用降级路径", e);
        PathService::fallback()
    });
    RwLock::new(service)
});

// 路径服务结构
#[allow(dead_code)]
pub struct PathService {
    // 可执行文件所在目录
    exe_dir: PathBuf,

    // 应用数据根目录（便携模式：<exe_dir>/data）
    app_data_dir: PathBuf,

    // 服务相关路径（私有目录，需要持久化）
    service_private_dir: PathBuf,
    service_private_exe: PathBuf,

    // 服务相关路径（assets 目录，随应用打包）
    assets_service_dir: PathBuf,
    assets_service_exe: PathBuf,

    // 日志文件路径
    log_file: PathBuf,

    // Windows 特有：自启动任务目录
    #[cfg(target_os = "windows")]
    tasks_dir: PathBuf,
}

impl PathService {
    // 创建路径服务实例
    pub fn new() -> Result<Self, String> {
        let current_exe = std::env::current_exe()
            .map_err(|e| format!("无法获取当前可执行文件路径：{}", e))?;

        let exe_dir = current_exe
            .parent()
            .ok_or_else(|| "无法获取可执行文件所在目录".to_string())?
            .to_path_buf();

        // 应用数据根目录（便携模式）
        let app_data_dir = exe_dir.join("data");

        // 服务私有目录（平台相关）
        let service_private_dir = Self::get_service_private_dir()?;

        // 服务二进制文件名
        #[cfg(target_os = "windows")]
        let service_exe_name = "stelliberty-service.exe";
        #[cfg(not(target_os = "windows"))]
        let service_exe_name = "stelliberty-service";

        let service_private_exe = service_private_dir.join(service_exe_name);

        // Assets 中的服务二进制路径
        let assets_service_dir = app_data_dir
            .join("flutter_assets")
            .join("assets")
            .join("service");
        let assets_service_exe = assets_service_dir.join(service_exe_name);

        // 日志文件路径
        let log_file = app_data_dir.join("running.logs");

        // Windows 自启动任务目录
        #[cfg(target_os = "windows")]
        let tasks_dir = {
            let appdata = std::env::var("APPDATA")
                .map_err(|e| format!("无法获取 APPDATA 环境变量：{}", e))?;
            PathBuf::from(appdata).join("Stelliberty").join("tasks")
        };

        Ok(Self {
            exe_dir,
            app_data_dir,
            service_private_dir,
            service_private_exe,
            assets_service_dir,
            assets_service_exe,
            log_file,
            #[cfg(target_os = "windows")]
            tasks_dir,
        })
    }

    // 获取服务私有目录（平台相关）
    fn get_service_private_dir() -> Result<PathBuf, String> {
        #[cfg(target_os = "windows")]
        {
            let appdata = std::env::var("APPDATA")
                .map_err(|e| format!("无法获取 APPDATA 环境变量：{}", e))?;
            Ok(PathBuf::from(appdata).join("stelliberty").join("service"))
        }

        #[cfg(target_os = "linux")]
        {
            let home = std::env::var("HOME")
                .map_err(|e| format!("无法获取 HOME 环境变量：{}", e))?;
            Ok(PathBuf::from(home)
                .join(".local")
                .join("share")
                .join("stelliberty")
                .join("service"))
        }

        #[cfg(target_os = "macos")]
        {
            let home = std::env::var("HOME")
                .map_err(|e| format!("无法获取 HOME 环境变量：{}", e))?;
            Ok(PathBuf::from(home)
                .join("Library")
                .join("Application Support")
                .join("Stelliberty")
                .join("service"))
        }

        #[cfg(not(any(target_os = "windows", target_os = "linux", target_os = "macos")))]
        {
            Err("不支持的操作系统".to_string())
        }
    }

    // 降级路径（初始化失败时使用）
    fn fallback() -> Self {
        let current_dir = std::env::current_dir().unwrap_or_else(|_| PathBuf::from("."));

        Self {
            exe_dir: current_dir.clone(),
            app_data_dir: current_dir.join("data"),
            service_private_dir: current_dir.join("service"),
            service_private_exe: current_dir.join("service").join("stelliberty-service"),
            assets_service_dir: current_dir.join("data").join("flutter_assets").join("assets").join("service"),
            assets_service_exe: current_dir.join("data").join("flutter_assets").join("assets").join("service").join("stelliberty-service"),
            log_file: current_dir.join("data").join("running.logs"),
            #[cfg(target_os = "windows")]
            tasks_dir: current_dir.join("tasks"),
        }
    }

    // 获取可执行文件所在目录
    pub fn exe_dir(&self) -> &PathBuf {
        &self.exe_dir
    }

    // 获取应用数据根目录
    pub fn app_data_dir(&self) -> &PathBuf {
        &self.app_data_dir
    }

    // 获取服务私有目录
    pub fn service_private_dir(&self) -> &PathBuf {
        &self.service_private_dir
    }

    // 获取私有目录中的服务二进制路径
    pub fn service_private_exe(&self) -> &PathBuf {
        &self.service_private_exe
    }

    // 获取 assets 中的服务目录
    pub fn assets_service_dir(&self) -> &PathBuf {
        &self.assets_service_dir
    }

    // 获取 assets 中的服务二进制路径
    pub fn assets_service_exe(&self) -> &PathBuf {
        &self.assets_service_exe
    }

    // 获取日志文件路径
    pub fn log_file(&self) -> &PathBuf {
        &self.log_file
    }

    // 获取自启动任务目录（仅 Windows）
    #[cfg(target_os = "windows")]
    pub fn tasks_dir(&self) -> &PathBuf {
        &self.tasks_dir
    }

    // 确保所有必要的目录存在
    pub fn ensure_dirs(&self) -> Result<(), String> {
        let dirs = vec![
            &self.app_data_dir,
            &self.service_private_dir,
            #[cfg(target_os = "windows")]
            &self.tasks_dir,
        ];

        for dir in dirs {
            if !dir.exists() {
                std::fs::create_dir_all(dir)
                    .map_err(|e| format!("无法创建目录 {}：{}", dir.display(), e))?;
                log::debug!("已创建目录：{}", dir.display());
            }
        }

        Ok(())
    }
}

// 便捷访问函数

// 获取可执行文件所在目录
#[allow(dead_code)]
pub fn exe_dir() -> PathBuf {
    PATH_SERVICE
        .read()
        .map(|s| s.exe_dir().clone())
        .unwrap_or_else(|_| PathBuf::from("."))
}

// 获取应用数据根目录
#[allow(dead_code)]
pub fn app_data_dir() -> PathBuf {
    PATH_SERVICE
        .read()
        .map(|s| s.app_data_dir().clone())
        .unwrap_or_else(|_| PathBuf::from("data"))
}

// 获取服务私有目录
pub fn service_private_dir() -> PathBuf {
    PATH_SERVICE
        .read()
        .map(|s| s.service_private_dir().clone())
        .unwrap_or_else(|_| PathBuf::from("service"))
}

// 获取私有目录中的服务二进制路径
pub fn service_private_exe() -> PathBuf {
    PATH_SERVICE
        .read()
        .map(|s| s.service_private_exe().clone())
        .unwrap_or_else(|_| PathBuf::from("stelliberty-service"))
}

// 获取 assets 中的服务目录
#[allow(dead_code)]
pub fn assets_service_dir() -> PathBuf {
    PATH_SERVICE
        .read()
        .map(|s| s.assets_service_dir().clone())
        .unwrap_or_else(|_| PathBuf::from("assets/service"))
}

// 获取 assets 中的服务二进制路径
pub fn assets_service_exe() -> PathBuf {
    PATH_SERVICE
        .read()
        .map(|s| s.assets_service_exe().clone())
        .unwrap_or_else(|_| PathBuf::from("stelliberty-service"))
}

// 获取日志文件路径
pub fn log_file() -> PathBuf {
    PATH_SERVICE
        .read()
        .map(|s| s.log_file().clone())
        .unwrap_or_else(|_| PathBuf::from("running.logs"))
}

// 获取自启动任务目录（仅 Windows）
#[cfg(target_os = "windows")]
pub fn tasks_dir() -> PathBuf {
    PATH_SERVICE
        .read()
        .map(|s| s.tasks_dir().clone())
        .unwrap_or_else(|_| PathBuf::from("tasks"))
}

// 初始化路径服务（预加载单例，创建必要目录）
pub fn init() {
    Lazy::force(&PATH_SERVICE);

    // 创建必要的目录
    if let Err(e) = ensure_dirs() {
        log::error!("创建必要目录失败：{}", e);
    }

    log::debug!("PathService 已初始化");
}

// 确保所有必要的目录存在
#[allow(dead_code)]
pub fn ensure_dirs() -> Result<(), String> {
    PATH_SERVICE
        .read()
        .map_err(|e| format!("无法获取路径服务锁：{}", e))?
        .ensure_dirs()
}
