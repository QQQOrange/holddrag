//! 托盘图标 + 主消息循环（UI 线程只干这个，绝不碰钩子）。

use windows::core::{w, PCWSTR};
use windows::Win32::Foundation::*;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::Shell::*;
use windows::Win32::UI::WindowsAndMessaging::*;

const WM_APP_TRAY: u32 = WM_APP + 100;
const IDM_OPEN_CONFIG: u32 = 1;
const IDM_EXIT: u32 = 2;

static mut CONFIG_PATH: Option<String> = None;

pub fn run_tray(cfg: &crate::config::Config, smoke: bool, _hooks: crate::hooks::Hooks) -> ! {
    unsafe {
        let hmod = GetModuleHandleW(None).unwrap_or_default();
        let class_name = w!("HoldDragRsTray");

        let wc = WNDCLASSW {
            lpfnWndProc: Some(tray_wndproc),
            hInstance: hmod.into(),
            lpszClassName: class_name,
            ..Default::default()
        };
        let _ = RegisterClassW(&wc);

        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            class_name,
            w!("HoldDragRs"),
            WINDOW_STYLE::default(),
            0, 0, 0, 0,
            None,
            None,
            Some(hmod.into()),
            None,
        )
        .expect("创建托盘窗口失败");

        let path = crate::config::Config::path();
        let mut wide: Vec<u16> = path.as_os_str().to_string_lossy().encode_utf16().collect();
        wide.push(0);

        // 托盘图标：用系统标准图标占位（以后可换自绘）
        let mut nid = NOTIFYICONDATAW {
            cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
            hWnd: hwnd,
            uID: 1,
            uFlags: NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage: WM_APP_TRAY,
            hIcon: LoadIconW(None, IDI_APPLICATION).unwrap_or_default(),
            ..Default::default()
        };
        nid.szTip[..8].copy_from_slice(&[
            b'H' as u16, b'o' as u16, b'l' as u16, b'd' as u16,
            b'D' as u16, b'r' as u16, b'a' as u16, 0,
        ]);
        Shell_NotifyIconW(NIM_ADD, &nid);

        CONFIG_PATH = Some(String::from_utf16_lossy(&wide[..wide.len() - 1]));

        if smoke {
            let h2 = hwnd.0 as isize;
            std::thread::spawn(move || {
                std::thread::sleep(std::time::Duration::from_secs(2));
                unsafe { PostMessageW(Some(HWND(h2 as *mut _)), WM_APP_TRAY, WPARAM(0), LPARAM(IDM_EXIT as isize)); }
            });
        }

        let mut msg: MSG = std::mem::zeroed();
        while GetMessageW(&mut msg, None, 0, 0).as_bool() {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
        std::process::exit(0);
    }
}

unsafe extern "system" fn tray_wndproc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    match msg {
        WM_APP_TRAY => {
            if lparam.0 as u32 == WM_RBUTTONUP || lparam.0 as u32 == WM_LBUTTONUP {
                show_menu(hwnd);
            }
            LRESULT(0)
        }
        WM_COMMAND => {
            match wparam.0 as u32 & 0xFFFF {
                IDM_OPEN_CONFIG => {
                    if let Some(p) = (unsafe { std::ptr::read(&raw const CONFIG_PATH) }).clone() {
                        open_in_notepad(&p);
                    }
                    LRESULT(0)
                }
                IDM_EXIT => {
                    crate::hooks::unhook_all();
                    Shell_NotifyIconW(NIM_DELETE, &NOTIFYICONDATAW {
                        cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
                        hWnd: hwnd,
                        uID: 1,
                        ..Default::default()
                    });
                    PostQuitMessage(0);
                    LRESULT(0)
                }
                _ => LRESULT(0),
            }
        }
        WM_DESTROY => {
            PostQuitMessage(0);
            LRESULT(0)
        }
        _ => DefWindowProcW(hwnd, msg, wparam, lparam),
    }
}

unsafe fn show_menu(hwnd: HWND) {
    let menu = CreatePopupMenu().expect("CreatePopupMenu");
    AppendMenuW(menu, MF_STRING, IDM_OPEN_CONFIG as usize, w!("设置(&E)（打开配置文件）"));
    AppendMenuW(menu, MF_SEPARATOR, 0, PCWSTR::null());
    AppendMenuW(menu, MF_STRING, IDM_EXIT as usize, w!("退出(&X)"));

    let mut pt = POINT::default();
    let _ = GetCursorPos(&mut pt);
    let _ = SetForegroundWindow(hwnd); // 菜单能收 WM_COMMAND 的经典前置
    TrackPopupMenuEx(menu, TPM_RIGHTBUTTON.0, pt.x, pt.y, hwnd, None);
    DestroyMenu(menu);
}

unsafe fn open_in_notepad(path: &str) {
    let mut cmd: Vec<u16> = "notepad.exe ".encode_utf16().collect();
    cmd.extend(path.encode_utf16());
    cmd.push(0);
    // 简单方式：WinExec / CreateProcess — 用 ShellExecuteW
    use windows::Win32::UI::Shell::ShellExecuteW;
    let mut exe: Vec<u16> = "notepad.exe\0".encode_utf16().collect();
    let mut p: Vec<u16> = path.encode_utf16().collect();
    p.push(0);
    ShellExecuteW(
        None,
        PCWSTR::null(),
        PCWSTR(exe.as_ptr()),
        PCWSTR(p.as_ptr()),
        PCWSTR::null(),
        SW_SHOW,
    );
}
