// 分子层共享类型定义
// 从 atoms 层重新导出基础类型，并添加分子层特有的类型

use rinf::SignalPiece;
use serde::{Deserialize, Serialize};

// 从 atoms 层重新导出
pub use crate::atoms::shared_types::{OverrideConfig, OverrideFormat};

// 代理模式（分子层特有）
#[derive(Deserialize, Serialize, Clone, Copy, Debug, SignalPiece)]
pub enum ProxyMode {
    Direct = 0, // 直连
    System = 1, // 系统代理
    Core = 2,   // Clash 核心代理
}
