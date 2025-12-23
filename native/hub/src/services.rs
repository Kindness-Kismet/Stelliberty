pub mod path_service;

pub fn init() {
    // 初始化路径服务（必须最先初始化）
    path_service::init();
}
