//! 触发键状态机：按住 hold_ms → 激活；短按 → 补发原按键。
//!
//! 长按计时用【生成号 + 后台线程】，与消息循环完全无关
//! （Lesson：C# 版用 WinForms Timer，钩子线程化后 WM_TIMER 不再被分发，激活永久失效）。

use crate::config::Config;
use std::sync::atomic::{AtomicU32, Ordering};

static GENERATION: AtomicU32 = AtomicU32::new(0);

pub struct TriggerState {
    active: bool,
    held: bool,
    active_button: i32,   // 0=键盘触发键（up 时需要配对吞掉），1=鼠标侧键，-1=无
    down_at: std::time::Instant,
    replay_short_press: bool,
}

impl TriggerState {
    pub fn new() -> Self {
        TriggerState {
            active: false,
            held: false,
            active_button: -1,
            down_at: std::time::Instant::now(),
            replay_short_press: false,
        }
    }

    pub fn active(&self) -> bool {
        self.active
    }

    /// 键盘触发键。返回 true = 吞掉。
    pub fn handle_trigger_key(&mut self, down: bool, vk: u32, _time: u32, cfg: &Config) -> bool {
        if down {
            if self.held {
                return true; // 已按着，重复 down 吞掉
            }
            self.held = true;
            self.active_button = 0;
            self.down_at = std::time::Instant::now();
            self.begin_hold(cfg);
            true
        } else {
            if self.active_button != 0 {
                return false; // 不是我们的按下配对，放行
            }
            let was_active = self.active;
            self.end_hold();
            if was_active {
                self.set_active(false);
                return true; // 激活态松开：吞掉 up（不补发）
            }
            // 短按：补发原按键，保证 Enter 的正常功能不受影响
            crate::hooks::replay_key(vk);
            true
        }
    }

    /// 鼠标侧键触发键。返回 true = 吞掉。
    pub fn handle_mouse_trigger(&mut self, down: bool, _pt: (i32, i32)) -> bool {
        down // 占位：侧键触发暂未启用配置项，直接放行
    }

    fn begin_hold(&mut self, cfg: &Config) {
        if !cfg.require_hold {
            self.set_active(true);
            return;
        }
        let sess_gen = GENERATION.fetch_add(1, Ordering::SeqCst) + 1;
        let dur = std::time::Duration::from_millis(cfg.hold_ms);
        std::thread::spawn(move || {
            std::thread::sleep(dur);
            // 生成号不匹配说明期间已松开/重启，放弃
            if GENERATION.load(Ordering::SeqCst) != sess_gen {
                return;
            }
            crate::with_app(|app| {
                let mut trigger = app.trigger.lock().unwrap();
                if trigger.held && !trigger.active {
                    trigger.set_active(true);
                }
            });
        });
    }

    fn end_hold(&mut self) {
        self.held = false;
        self.active_button = -1;
        GENERATION.fetch_add(1, Ordering::SeqCst); // 作废挂着的计时
    }

    fn set_active(&mut self, value: bool) {
        if self.active == value {
            return;
        }
        self.active = value;
    }
}
