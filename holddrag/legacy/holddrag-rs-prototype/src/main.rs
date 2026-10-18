//! HoldDrag-rs —— AltSnap 式按住触发键拖动/缩放窗口，Rust 实现。
//!
//! 重写原则：把 C# 版踩过的每一个坑都按正确姿势写：
//!   1. 低级钩子住在【专用线程】（微软文档要求；UI 线程一忙全系统鼠标排队）
//!   2. 长按计时用【线程无关】的机制（生成号 + 工作线程 sleep），绝不用绑定消息循环的定时器
//!   3. 写窗口用【同步】SetWindowPos（AltSnap 同款；异步会让目标窗口队列堆积过期位置）
//!   4. 防堆积靠【合并只留最新位置】（PeekMessage 抽干），不做定时节流（Thread.Sleep 会量化）
//!   5. 吸附只作用于【正在动的那条边】（移动语义才整体平移）
//!   6. 热路径零分配、零日志

mod config;
mod engine;
mod hooks;
mod trigger;
mod tray;
mod bench;

use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};
use std::sync::Mutex;

pub static PAUSED: AtomicBool = AtomicBool::new(false);

/// 进程级共享状态。钩子线程 / 工作线程 / UI 线程都要碰。
pub struct App {
    pub config: Mutex<config::Config>,
    pub trigger: Mutex<trigger::TriggerState>,
    pub engine: Mutex<engine::DragEngine>,
}

pub static APP: Mutex<Option<App>> = Mutex::new(None);

pub fn with_app<R>(f: impl FnOnce(&App) -> R) -> Option<R> {
    let guard = APP.lock().ok()?;
    let app = guard.as_ref()?;
    Some(f(app))
}

/// 冒烟模式：--smoke 启动后自动退出
pub static SMOKE: AtomicBool = AtomicBool::new(false);

/// 工作线程 id（engine 填写）
pub static WORKER_THREAD_ID: AtomicU32 = AtomicU32::new(0);

fn main() {
    let args: Vec<String> = std::env::args().collect();
    let smoke = args.iter().any(|a| a == "--smoke");
    let bench = args.iter().any(|a| a == "--dragbench");
    SMOKE.store(smoke, Ordering::SeqCst);

    unsafe {
        // DPI 感知必须最先设置
        use windows::Win32::UI::HiDpi::*;
        let _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }

    if bench {
        bench::run();
        return;
    }

    let cfg = config::Config::load();

    // 初始化共享状态
    {
        let mut g = APP.lock().unwrap();
        *g = Some(App {
            config: Mutex::new(cfg.clone()),
            trigger: Mutex::new(trigger::TriggerState::new()),
            engine: Mutex::new(engine::DragEngine::new()),
        });
    }

    engine::start_worker();

    let hooks = hooks::Hooks::install();
    if !hooks.mouse || !hooks.keyboard {
        eprintln!("钩子安装失败：mouse={} keyboard={} err={}",
            hooks.mouse, hooks.keyboard, hooks.last_error);
    }

    tray::run_tray(&cfg, smoke, hooks);
}
