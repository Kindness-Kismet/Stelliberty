// Clash 网络管理分子模块

pub mod connection;
pub mod handlers;
pub mod ipc_client;
pub mod ws_client;

#[cfg(windows)]
pub use connection::connect_named_pipe;
#[cfg(unix)]
pub use connection::connect_unix_socket;
pub use handlers::{
    IpcDeleteRequest, IpcGetRequest, IpcLogData, IpcPatchRequest, IpcPostRequest, IpcPutRequest,
    IpcResponse, IpcTrafficData, StartLogStream, StartTrafficStream, StopLogStream,
    StopTrafficStream, StreamResult, cleanup_all_network_resources, init_rest_api_listeners,
    internal_ipc_get, start_connection_pool_health_check,
};
pub use ipc_client::{HttpResponse, IpcClient};
pub use ws_client::WebSocketClient;

pub fn init_listeners() {
    log::info!("初始化 Clash 网络管理监听器");
    init_rest_api_listeners();
}
