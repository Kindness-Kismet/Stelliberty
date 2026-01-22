// IPC 客户端原子模块：提供基础 IPC 通信能力。
// 支持轻量连接复用以降低请求开销。

mod client;

pub use client::{IpcClient, IpcHttpResponse};
