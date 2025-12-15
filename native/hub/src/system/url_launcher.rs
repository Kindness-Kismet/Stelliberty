// URL 启动器：使用系统默认浏览器打开 URL

use rinf::{DartSignal, RustSignal};
use serde::{Deserialize, Serialize};

// Dart → Rust：打开 URL
#[derive(Deserialize, DartSignal)]
pub struct OpenUrl {
    pub url: String,
}

// Rust → Dart：打开 URL 结果
#[derive(Serialize, RustSignal)]
pub struct OpenUrlResult {
    pub success: bool,
    pub error_message: Option<String>,
}

impl OpenUrl {
    // 在系统默认浏览器中打开 URL
    //
    // 目的：提供跨平台的 URL 打开能力
    pub fn handle(&self) {
        log::info!("收到打开 URL 请求：{}", self.url);

        let (success, error_message) = match open_url(&self.url) {
            Ok(()) => (true, None),
            Err(err) => {
                log::error!("打开 URL 失败：{}", err);
                (false, Some(err))
            }
        };

        let response = OpenUrlResult {
            success,
            error_message,
        };

        response.send_signal_to_dart();
    }
}

// 打开 URL
pub fn open_url(url: &str) -> Result<(), String> {
    log::info!("尝试在浏览器中打开 URL：{}", url);

    webbrowser::open(url).map_err(|e| {
        let error_msg = format!("打开 URL 失败：{}", e);
        log::error!("{}", error_msg);
        error_msg
    })?;

    log::info!("成功打开 URL");
    Ok(())
}
