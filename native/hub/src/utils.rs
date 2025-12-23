pub mod init_logger;

pub fn init() {
    // 初始化日志系统
    init_logger::setup_logger();
    init_logger::init_message_listener();
}
