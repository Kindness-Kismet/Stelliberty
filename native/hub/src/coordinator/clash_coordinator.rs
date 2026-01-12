// Clash 协调器：编排所有 Clash 相关操作

use crate::molecules::{
    clash_config, clash_network, clash_process, core_update, delay_testing, override_processing,
    subscription_management,
};

pub struct ClashCoordinator;

impl Default for ClashCoordinator {
    fn default() -> Self {
        Self
    }
}

impl ClashCoordinator {
    pub fn new() -> Self {
        Self
    }
}

// 初始化 Clash 协调器
pub fn init() {
    // 初始化进程管理
    clash_process::init_listeners();

    // 初始化配置管理
    clash_config::init_listeners();

    // 初始化网络管理
    clash_network::init_listeners();

    // 初始化覆写处理
    override_processing::init_listeners();

    // 初始化订阅管理
    subscription_management::init_listeners();

    // 初始化核心更新
    core_update::init_listeners();

    // 初始化延迟测试
    delay_testing::init_listeners();
}

// 清理资源
pub fn cleanup() {
    clash_process::cleanup();
}

// 清理所有网络资源（供其他分子通过协调层调用）
pub async fn cleanup_network_resources() {
    clash_network::cleanup_all_network_resources().await;
}
