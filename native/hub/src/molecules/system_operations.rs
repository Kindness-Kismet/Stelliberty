// 系统操作分子模块

pub mod app_update;
pub mod auto_start;
pub mod backup;
#[cfg(windows)]
pub mod loopback;
pub mod power_event;
pub mod url_launcher;

pub use app_update::{AppUpdateResult, CheckAppUpdateRequest};
pub use auto_start::{AutoStartStatusResult, GetAutoStartStatus, SetAutoStartStatus};
pub use backup::{BackupOperationResult, CreateBackupRequest, RestoreBackupRequest};

#[cfg(windows)]
pub use loopback::{
    AppContainerInfo, AppContainersComplete, GetAppContainers, SaveLoopbackConfiguration,
    SaveLoopbackConfigurationResult, SetLoopback, SetLoopbackResult,
};
pub use power_event::{
    PowerEventType, SystemPowerEvent, start_power_event_listener, stop_power_event_listener,
};
pub use url_launcher::{OpenUrl, OpenUrlResult};

pub fn init_listeners() {
    log::info!("初始化系统操作监听器");
    app_update::init_dart_signal_listeners();
    auto_start::init_dart_signal_listeners();
    backup::init_dart_signal_listeners();
    #[cfg(windows)]
    loopback::init_dart_signal_listeners();
    url_launcher::init_dart_signal_listeners();

    power_event::start_power_event_listener();
}
