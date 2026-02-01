// Android JNI 桥接：初始化 ndk-context，使 Rust crate 能访问 Android 环境
// 仅 Android 平台编译

#[cfg(target_os = "android")]
mod initializer;

#[cfg(target_os = "android")]
pub use initializer::*;
