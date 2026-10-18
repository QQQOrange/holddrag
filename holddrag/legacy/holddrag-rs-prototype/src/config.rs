//! 配置：serde + json，路径 %APPDATA%\HoldDragRs\config.json。
//! 字段与 C# 版对齐，默认值采用调优后的结论（MoveRate=1：每个事件都投递）。

use serde::{Deserialize, Serialize};
use std::path::PathBuf;

#[derive(Serialize, Deserialize, Clone, Debug)]
#[serde(default)]
pub struct Config {
    pub trigger_vk: u32,          // 触发键 VK，0x0D = Enter
    pub require_hold: bool,
    pub hold_ms: u64,
    pub action_left: String,      // "Move" | "Resize" | "Nothing"
    pub action_right: String,
    pub action_middle: String,
    pub show_activation_frame: bool,
    pub edge_snap: bool,
    pub snap_to_other_windows: bool,
    pub snap_distance: i32,
    pub corner_percent: i32,
    pub move_rate: u32,           // 每几个鼠标事件写一次窗口（1 = 全部）
    pub resize_rate: u32,
    pub drag_threshold: u32,      // 1 = 只对已最大化窗口启用死区
    pub drag_out_threshold_px: i32,
    pub double_click_left_maximize: bool,
}

impl Default for Config {
    fn default() -> Self {
        Config {
            trigger_vk: 0x0D,
            require_hold: true,
            hold_ms: 400,
            action_left: "Move".into(),
            action_right: "Resize".into(),
            action_middle: "Nothing".into(),
            show_activation_frame: false,
            edge_snap: true,
            snap_to_other_windows: true,
            snap_distance: 20,
            corner_percent: 20,
            move_rate: 1,
            resize_rate: 1,
            drag_threshold: 1,
            drag_out_threshold_px: 20,
            double_click_left_maximize: true,
        }
    }
}

impl Config {
    pub fn path() -> PathBuf {
        let mut p = std::env::var("APPDATA")
            .map(PathBuf::from)
            .unwrap_or_else(|_| PathBuf::from("."));
        p.push("HoldDragRs");
        let _ = std::fs::create_dir_all(&p);
        p.push("config.json");
        p
    }

    pub fn load() -> Config {
        let path = Self::path();
        if let Ok(text) = std::fs::read_to_string(&path) {
            match serde_json::from_str::<Config>(&text) {
                Ok(mut c) => {
                    c.normalize();
                    return c;
                }
                Err(e) => eprintln!("配置解析失败，使用默认值：{e}"),
            }
        }
        let mut c = Config::default();
        c.normalize();
        c
    }

    pub fn save(&self) {
        if let Ok(text) = serde_json::to_string_pretty(self) {
            let _ = std::fs::write(Self::path(), text);
        }
    }

    pub fn normalize(&mut self) {
        if self.snap_distance < 1 {
            self.snap_distance = 1;
        }
        if self.snap_distance > 100 {
            self.snap_distance = 100;
        }
        if self.corner_percent < 5 {
            self.corner_percent = 5;
        }
        if self.corner_percent > 45 {
            self.corner_percent = 45;
        }
        if self.move_rate < 1 {
            self.move_rate = 1;
        }
        if self.move_rate > 20 {
            self.move_rate = 20;
        }
        if self.resize_rate < 1 {
            self.resize_rate = 1;
        }
        if self.resize_rate > 20 {
            self.resize_rate = 20;
        }
        if self.hold_ms < 30 {
            self.hold_ms = 30;
        }
        if self.hold_ms > 3000 {
            self.hold_ms = 3000;
        }
    }

    pub fn action_of_button(&self, button: u32) -> Action {
        let s = match button {
            0 => &self.action_left,
            1 => &self.action_right,
            _ => &self.action_middle,
        };
        match s.as_str() {
            "Move" => Action::Move,
            "Resize" => Action::Resize,
            _ => Action::Nothing,
        }
    }
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Action {
    Move,
    Resize,
    Nothing,
}
