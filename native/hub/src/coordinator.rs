// L2 协调层模块入口

pub mod clash_coordinator;
pub mod system_coordinator;

pub use clash_coordinator::{cleanup_network_resources, ClashCoordinator};
pub use system_coordinator::SystemCoordinator;

pub fn init_all() {
    clash_coordinator::init();
    system_coordinator::init();
    log::info!("协调层初始化完成");
}

pub fn cleanup() {
    log::info!("清理协调层资源");
    clash_coordinator::cleanup();
}
