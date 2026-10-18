//! 钩子：低级鼠标 + 键盘钩子，安装在【专用线程】上（ Lesson from C# 版：UI 线程装钩子，
//! UI 一忙全系统鼠标事件排队 —— 微软文档明确要求专用线程）。

use crate::{config::Action, with_app};
use windows::Win32::Foundation::*;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::Threading::GetCurrentThreadId;
use windows::Win32::UI::Input::KeyboardAndMouse::*;
use windows::Win32::UI::WindowsAndMessaging::*;

pub struct HookResult {
    pub mouse: bool,
    pub keyboard: bool,
    pub last_error: u32,
}

static HOOK_THREAD_ID: std::sync::atomic::AtomicU32 = std::sync::atomic::AtomicU32::new(0);

pub struct Hooks {
    pub mouse: bool,
    pub keyboard: bool,
    pub last_error: u32,
}

impl Hooks {
    pub fn install() -> Hooks {
        // 专用线程：建消息队列 → 装钩子 → 纯 GetMessage 泵
        let (tx, rx) = std::sync::mpsc::channel::<HookResult>();

        std::thread::Builder::new()
            .name("hook-thread".into())
            .spawn(move || unsafe {
                let mut msg: MSG = std::mem::zeroed();
                PeekMessageW(&mut msg, None, 0, 0, PM_NOREMOVE); // 强制建队列
                HOOK_THREAD_ID.store(GetCurrentThreadId(), std::sync::atomic::Ordering::SeqCst);

                let hmod = GetModuleHandleW(None).unwrap_or_default();
                let mouse = SetWindowsHookExW(WH_MOUSE_LL, Some(mouse_proc), Some(hmod.into()), 0);
                let keyboard = SetWindowsHookExW(WH_KEYBOARD_LL, Some(keyboard_proc), Some(hmod.into()), 0);
                let last_error = GetLastError().0;

                let _ = tx.send(HookResult {
                    mouse: mouse.is_ok(),
                    keyboard: keyboard.is_ok(),
                    last_error: if mouse.is_err() || keyboard.is_err() { last_error } else { 0 },
                });

                loop {
                    if GetMessageW(&mut msg, None, 0, 0).as_bool() == false {
                        break;
                    }
                    // 纯泵：不做别的
                }
            })
            .expect("启动钩子线程失败");

        match rx.recv_timeout(std::time::Duration::from_secs(3)) {
            Ok(r) => Hooks { mouse: r.mouse, keyboard: r.keyboard, last_error: r.last_error },
            Err(_) => Hooks { mouse: false, keyboard: false, last_error: 1460 },
        }
    }
}

/// 短按触发键：把原按键补发回去（保证 Enter 等正常功能不受影响）
pub fn replay_key(vk: u32) {
    unsafe {
        let mut input: INPUT = std::mem::zeroed();
        input.r#type = INPUT_KEYBOARD;
        input.Anonymous.ki.wVk = VIRTUAL_KEY(vk as u16);
        let mut inputs = [input; 2];
        inputs[1].Anonymous.ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(&inputs, std::mem::size_of::<INPUT>() as i32);
    }
}

pub fn unhook_all() {
    unsafe {
        let tid = HOOK_THREAD_ID.load(std::sync::atomic::Ordering::SeqCst);
        if tid != 0 {
            PostThreadMessageW(tid, WM_QUIT, WPARAM(0), LPARAM(0));
        }
    }
}

unsafe extern "system" fn mouse_proc(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    if code >= 0 {
        let ms = *(lparam.0 as *const MSLLHOOKSTRUCT);
        let msg = wparam.0 as u32;
        let consumed = handle_mouse(msg, &ms);
        if consumed {
            return LRESULT(1);
        }
    }
    CallNextHookEx(None, code, wparam, lparam)
}

unsafe extern "system" fn keyboard_proc(code: i32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    if code >= 0 {
        let kb = *(lparam.0 as *const KBDLLHOOKSTRUCT);
        let down = wparam.0 as u32 == WM_KEYDOWN || wparam.0 as u32 == WM_SYSKEYDOWN;
        let up = wparam.0 as u32 == WM_KEYUP || wparam.0 as u32 == WM_SYSKEYUP;
        if down || up {
            let consumed = handle_keyboard(down, kb.vkCode, kb.time);
            if consumed {
                return LRESULT(1);
            }
        }
    }
    CallNextHookEx(None, code, wparam, lparam)
}

/// 返回 true = 吞掉事件
fn handle_mouse(msg: u32, ms: &MSLLHOOKSTRUCT) -> bool {
    let pt = (ms.pt.x, ms.pt.y);
    with_app(|app| {
        let mut trigger = app.trigger.lock().unwrap();

        // 鼠标侧键触发键（XBUTTON）走 trigger；普通键走 action
        if msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP {
            return trigger.handle_mouse_trigger(msg == WM_XBUTTONDOWN, pt);
        }

        if msg == WM_MOUSEMOVE {
            // ★ 绝不能吞 WM_MOUSEMOVE：吞掉光标就停在原地。
            let mut engine = app.engine.lock().unwrap();
            if engine.is_dragging() {
                engine.deliver_cursor(pt.0, pt.1);
            }
            return false;
        }

        let button = match msg {
            WM_LBUTTONDOWN | WM_LBUTTONUP => Some(0u32),
            WM_RBUTTONDOWN | WM_RBUTTONUP => Some(1),
            WM_MBUTTONDOWN | WM_MBUTTONUP => Some(2),
            _ => None,
        };
        let button = match button {
            Some(b) => b,
            None => return false,
        };
        let down = msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN;

        if !trigger.active() {
            return false;
        }

        let action = {
            let cfg = app.config.lock().unwrap();
            cfg.action_of_button(button)
        };
        match action {
            Action::Nothing => false,
            _ => {
                let mut engine = app.engine.lock().unwrap();
                if down {
                    if engine.is_dragging() {
                        return false;
                    }
                    let cfg = app.config.lock().unwrap().clone();
                    engine.begin(button, action == Action::Move, pt.0, pt.1, &cfg);
                    true
                } else {
                    engine.end(pt.0, pt.1);
                    true
                }
            }
        }
    })
    .unwrap_or(false)
}

/// 返回 true = 吞掉事件
fn handle_keyboard(down: bool, vk: u32, time: u32) -> bool {
    with_app(|app| {
        if crate::PAUSED.load(std::sync::atomic::Ordering::SeqCst) {
            return false;
        }
        let cfg = app.config.lock().unwrap().clone();
        if vk != cfg.trigger_vk {
            return false;
        }
        let mut trigger = app.trigger.lock().unwrap();
        trigger.handle_trigger_key(down, vk, time, &cfg)
    })
    .unwrap_or(false)
}
