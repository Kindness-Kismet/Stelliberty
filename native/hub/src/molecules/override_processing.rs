// 覆写处理分子模块

pub mod downloader;

// 内部使用
mod processor;

pub use downloader::{DownloadOverrideRequest, DownloadOverrideResponse};
pub use processor::{
    ApplyOverridesRequest, ApplyOverridesResponse, ParseSubscriptionRequest,
    ParseSubscriptionResponse,
};

// 从分子层共享类型导入
pub use super::{OverrideConfig, OverrideFormat};

// 从 atoms 层重新导出 OverrideProcessor（供其他分子使用）
pub use crate::atoms::OverrideProcessor;

pub fn init_listeners() {
    processor::init_dart_signal_listeners();
    downloader::init_dart_signal_listeners();
}
