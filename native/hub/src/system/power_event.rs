// Windows 电源事件监听：休眠/唤醒自动重载 TUN

#[cfg(target_os = "windows")]
use std::sync::atomic::{AtomicBool, Ordering};
#[cfg(target_os = "windows")]
use windows::Win32::Foundation::{HANDLE, HWND, LPARAM, LRESULT, WPARAM};
#[cfg(target_os = "windows")]
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
#[cfg(target_os = "windows")]
use windows::Win32::System::Power::{
    POWERBROADCAST_SETTING, RegisterPowerSettingNotification, UnregisterPowerSettingNotification,
};
#[cfg(target_os = "windows")]
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW, GetMessageW, PostQuitMessage,
    REGISTER_NOTIFICATION_FLAGS, RegisterClassW, TranslateMessage, WINDOW_EX_STYLE,
    WM_POWERBROADCAST, WNDCLASSW, WS_OVERLAPPEDWINDOW,
};
#[cfg(target_os = "windows")]
use windows::core::GUID;

use rinf::{RustSignal, SignalPiece};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize, SignalPiece)]
pub enum PowerEventType {
    Suspend,
    ResumeAutomatic,
    ResumeSuspend,
}

#[derive(Serialize, RustSignal)]
pub struct SystemPowerEvent {
    pub event_type: PowerEventType,
}

// GUID_MONITOR_POWER_ON: 监视器电源状态
#[cfg(target_os = "windows")]
#[allow(dead_code)]
const GUID_MONITOR_POWER_ON: GUID = GUID {
    data1: 0x02731015,
    data2: 0x4510,
    data3: 0x4526,
    data4: [0x99, 0xE6, 0xE5, 0xA1, 0x7E, 0xBD, 0x1A, 0xEA],
};

// GUID_CONSOLE_DISPLAY_STATE: 控制台显示状态
#[cfg(target_os = "windows")]
const GUID_CONSOLE_DISPLAY_STATE: GUID = GUID {
    data1: 0x6FE69556,
    data2: 0x704A,
    data3: 0x47A0,
    data4: [0x8F, 0x24, 0xC2, 0x8D, 0x93, 0x6F, 0xDA, 0x47],
};

#[cfg(target_os = "windows")]
const PBT_APMSUSPEND: u32 = 0x0004;
#[cfg(target_os = "windows")]
const PBT_APMRESUMEAUTOMATIC: u32 = 0x0012;
#[cfg(target_os = "windows")]
const PBT_APMRESUMESUSPEND: u32 = 0x0007;
#[cfg(target_os = "windows")]
const PBT_POWERSETTINGCHANGE: u32 = 0x8013;

#[cfg(target_os = "windows")]
static RUNNING: AtomicBool = AtomicBool::new(false);

#[cfg(target_os = "windows")]
unsafe extern "system" fn window_proc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    match msg {
        WM_POWERBROADCAST => {
            let event_type = wparam.0 as u32;

            match event_type {
                PBT_APMSUSPEND => {
                    log::info!("系统进入休眠");
                    SystemPowerEvent {
                        event_type: PowerEventType::Suspend,
                    }
                    .send_signal_to_dart();
                }

                PBT_APMRESUMEAUTOMATIC => {
                    log::info!("系统自动唤醒");
                    SystemPowerEvent {
                        event_type: PowerEventType::ResumeAutomatic,
                    }
                    .send_signal_to_dart();
                }

                PBT_APMRESUMESUSPEND => {
                    log::info!("用户唤醒系统");
                    SystemPowerEvent {
                        event_type: PowerEventType::ResumeSuspend,
                    }
                    .send_signal_to_dart();
                }

                PBT_POWERSETTINGCHANGE => {
                    let setting = lparam.0 as *const POWERBROADCAST_SETTING;
                    if !setting.is_null() {
                        unsafe {
                            let setting_ref = &*setting;

                            if setting_ref.PowerSetting == GUID_CONSOLE_DISPLAY_STATE {
                                let display_state = if setting_ref.DataLength >= 4 {
                                    u32::from_le_bytes([
                                        setting_ref.Data.first().copied().unwrap_or(0),
                                        setting_ref.Data.get(1).copied().unwrap_or(0),
                                        setting_ref.Data.get(2).copied().unwrap_or(0),
                                        setting_ref.Data.get(3).copied().unwrap_or(0),
                                    ])
                                } else {
                                    0
                                };

                                match display_state {
                                    0 => log::debug!("显示器关闭"),
                                    1 => log::debug!("显示器开启"),
                                    2 => log::debug!("显示器变暗"),
                                    _ => log::debug!("显示器状态未知: {}", display_state),
                                }
                            }
                        }
                    }
                }

                _ => {
                    log::debug!("其他电源事件: 0x{:04X}", event_type);
                }
            }

            LRESULT(0)
        }
        _ => unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) },
    }
}

#[cfg(target_os = "windows")]
pub fn start_power_event_listener() {
    if RUNNING.swap(true, Ordering::SeqCst) {
        log::warn!("电源监听器已运行");
        return;
    }

    log::info!("启动电源监听器");

    std::thread::spawn(|| {
        if let Err(e) = run_event_loop() {
            log::error!("电源事件循环失败: {}", e);
            RUNNING.store(false, Ordering::SeqCst);
        }
    });
}

#[cfg(target_os = "windows")]
fn run_event_loop() -> Result<(), String> {
    unsafe {
        let instance = GetModuleHandleW(None).map_err(|e| format!("获取模块句柄失败: {}", e))?;

        let class_name = windows::core::w!("StellibertyPowerEventClass");

        let wc = WNDCLASSW {
            lpfnWndProc: Some(window_proc),
            hInstance: instance.into(),
            lpszClassName: class_name,
            ..Default::default()
        };

        let atom = RegisterClassW(&wc);
        if atom == 0 {
            return Err("注册窗口类失败".to_string());
        }

        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            class_name,
            windows::core::w!("Stelliberty Power Event Window"),
            WS_OVERLAPPEDWINDOW,
            0,
            0,
            0,
            0,
            None,
            None,
            Some(instance.into()),
            None,
        )
        .map_err(|e| format!("创建窗口失败: {}", e))?;

        let _notify_handle = RegisterPowerSettingNotification(
            HANDLE(hwnd.0),
            &GUID_CONSOLE_DISPLAY_STATE,
            REGISTER_NOTIFICATION_FLAGS(0x00000000),
        )
        .map_err(|e| format!("注册电源通知失败: {}", e))?;

        log::info!("电源监听器就绪");

        let mut msg = windows::Win32::UI::WindowsAndMessaging::MSG::default();
        while GetMessageW(&mut msg, None, 0, 0).as_bool() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }

        log::info!("清理电源监听器");

        if let Err(e) = UnregisterPowerSettingNotification(_notify_handle) {
            log::warn!("注销电源通知失败: {}", e);
        }
        let _ = DestroyWindow(hwnd);
        RUNNING.store(false, Ordering::SeqCst);

        Ok(())
    }
}

#[cfg(target_os = "windows")]
#[allow(dead_code)]
pub fn stop_power_event_listener() {
    if !RUNNING.load(Ordering::SeqCst) {
        return;
    }

    log::info!("停止电源监听器");

    unsafe {
        PostQuitMessage(0);
    }

    RUNNING.store(false, Ordering::SeqCst);
}

#[cfg(not(target_os = "windows"))]
pub fn start_power_event_listener() {}

#[cfg(not(target_os = "windows"))]
pub fn stop_power_event_listener() {}
