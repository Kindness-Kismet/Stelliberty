pub mod init_logger;

pub fn init() {
    init_logger::setup_logger();
    init_logger::init_message_listener();
}
