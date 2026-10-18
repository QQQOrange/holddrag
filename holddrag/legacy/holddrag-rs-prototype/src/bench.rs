//! 拖动基准（--dragbench）：与 C# 版同方法 —— 模拟 125Hz 事件流拖动，
//! 1kHz 采样窗口矩形，输出有效更新率 / 间隔分布。

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Instant;
use windows::core::w;
use windows::Win32::Foundation::*;
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::UI::WindowsAndMessaging::*;

pub fn run() {
    unsafe {
        let hmod = GetModuleHandleW(None).unwrap_or_default();
        let class_name = w!("HoldDragBenchWnd");

        let wc = WNDCLASSW {
            lpfnWndProc: Some(bench_wndproc),
            hInstance: hmod.into(),
            lpszClassName: class_name,
            ..Default::default()
        };
        RegisterClassW(&wc);

        let hwnd = CreateWindowExW(
            WINDOW_EX_STYLE::default(),
            class_name,
            w!("bench"),
            WS_OVERLAPPEDWINDOW | WS_VISIBLE,
            200, 200, 500, 380,
            None, None, Some(hmod.into()), None,
        )
        .expect("创建基准窗口失败");

        crate::engine::ALLOW_OWN_TARGET.store(1, std::sync::atomic::Ordering::SeqCst);
        let cfg = crate::config::Config::default();
        {
            let mut g = crate::APP.lock().unwrap();
            *g = Some(crate::App {
                config: Mutex::new(cfg.clone()),
                trigger: Mutex::new(crate::trigger::TriggerState::new()),
                engine: Mutex::new(crate::engine::DragEngine::new()),
            });
        }
        crate::engine::start_worker();

        let mut r = RECT::default();
        let _ = GetWindowRect(hwnd, &mut r);
        let grab = (r.left + (r.right - r.left) / 2, r.top + (r.bottom - r.top) / 2);

        {
            let g = crate::APP.lock().unwrap();
            let app = g.as_ref().unwrap();
            let mut engine = app.engine.lock().unwrap();
            engine.begin(0, true, grab.0, grab.1, &cfg);
        }

        let done = Arc::new(AtomicBool::new(false));
        let samples: Arc<Mutex<Vec<(u64, i32, i32)>>> = Arc::new(Mutex::new(Vec::new()));

        // 采样线程：1kHz 记录窗口矩形变化点
        {
            let done2 = done.clone();
            let samples2 = samples.clone();
            let hwnd_isize = hwnd.0 as isize;
            std::thread::spawn(move || {
                let t0 = Instant::now();
                let mut last: Option<(i32, i32)> = None;
                loop {
                    if done2.load(Ordering::SeqCst) { return; }
                    let mut r = RECT::default();
                    if GetWindowRect(HWND(hwnd_isize as *mut _), &mut r).is_ok() {
                        let mut s = samples2.lock().unwrap();
                        let changed = match last {
                            Some((x, y)) => x != r.left || y != r.top,
                            None => true,
                        };
                        if changed {
                            s.push((t0.elapsed().as_millis() as u64, r.left, r.top));
                            last = Some((r.left, r.top));
                        }
                    }
                    std::thread::sleep(std::time::Duration::from_millis(1));
                }
            });
        }

        // 驱动线程：125Hz × 2s，600×300px 对角线
        {
            let done2 = done.clone();
            std::thread::spawn(move || {
                let t0 = Instant::now();
                let steps = 250usize;
                for i in 1..=steps {
                    let target = i as u64 * 8;
                    loop {
                        let el = (t0.elapsed().as_millis() as u64).min(target);
                        if el >= target { break; }
                        if target - el > 2 {
                            std::thread::sleep(std::time::Duration::from_millis(target - el - 2));
                        } else {
                            std::hint::spin_loop();
                        }
                    }
                    let x = grab.0 + 600 * i as i32 / steps as i32;
                    let y = grab.1 + 300 * i as i32 / steps as i32;
                    crate::with_app(|app| {
                        let mut engine = app.engine.lock().unwrap();
                        engine.deliver_cursor(x, y);
                    });
                }
                crate::with_app(|app| {
                    let mut engine = app.engine.lock().unwrap();
                    engine.end(0, 0);
                });
                done2.store(true, Ordering::SeqCst);
            });
        }

        // 主线程泵消息 3 秒
        let t0 = Instant::now();
        let mut msg: MSG = std::mem::zeroed();
        while t0.elapsed().as_millis() < 3000 {
            while PeekMessageW(&mut msg, None, 0, 0, PM_REMOVE).as_bool() {
                let _ = TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
            std::thread::sleep(std::time::Duration::from_millis(1));
        }

        // 分析
        let pts = samples.lock().unwrap().clone();
        let mut out = String::new();
        out.push_str("拖动 2s，驱动 250 事件（125Hz）\n");
        out.push_str(&format!(
            "窗口位置更新点 {} 个 → 有效更新率 {:.1} Hz\n",
            pts.len(),
            pts.len() as f64 / 2.0
        ));
        if pts.len() >= 3 {
            let mut gaps: Vec<u64> = Vec::new();
            for w in pts.windows(2) {
                gaps.push(w[1].0.saturating_sub(w[0].0));
            }
            gaps.sort_unstable();
            let avg = gaps.iter().sum::<u64>() as f64 / gaps.len() as f64;
            let p95 = gaps[(gaps.len() as f64 * 0.95) as usize % gaps.len()];
            let max = *gaps.last().unwrap();
            out.push_str(&format!(
                "更新间隔：平均 {:.1} ms | P95 {} ms | 最大 {} ms\n",
                avg, p95, max
            ));
        }
        let path = std::env::var("APPDATA").unwrap() + "\\HoldDragRs\\dragbench.txt";
        let _ = std::fs::write(&path, &out);
        println!("{}", out);
        std::process::exit(0);
    }
}

unsafe extern "system" fn bench_wndproc(hwnd: HWND, msg: u32, wparam: WPARAM, lparam: LPARAM) -> LRESULT {
    DefWindowProcW(hwnd, msg, wparam, lparam)
}
