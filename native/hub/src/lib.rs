// Rust 原生模块入口（为 Flutter 提供系统级功能）

mod clash;
mod network;
mod services;
mod system;
mod utils;

use rinf::{dart_shutdown, write_interface};

write_interface!();

#[tokio::main(flavor = "current_thread")]
async fn main() {
    services::init();
    utils::init();
    network::init();
    system::init();
    clash::init();

    dart_shutdown().await;
    clash::process::cleanup();
}
