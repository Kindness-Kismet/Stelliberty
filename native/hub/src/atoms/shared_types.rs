// 原子层共享类型定义
// 用于存放跨层共享的基础类型

use rinf::SignalPiece;
use serde::{Deserialize, Serialize};

// 覆写格式
#[derive(Deserialize, Serialize, SignalPiece, Clone, Copy, Debug)]
pub enum OverrideFormat {
    Yaml = 0,
    Javascript = 1,
}

// 覆写配置
#[derive(Debug, Deserialize, Serialize, SignalPiece, Clone)]
pub struct OverrideConfig {
    pub id: String,
    pub name: String,
    pub format: OverrideFormat,
    pub content: String,
}
