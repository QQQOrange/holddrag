//! 拖动引擎：会话管理 + 工作线程（合并只留最新位置）+ 同步写窗口 + 单次扫描的吸附对齐。

use crate::config::Config;
use std::sync::atomic::{AtomicU32, Ordering};
use windows::Win32::Foundation::*;
use windows::Win32::Graphics::Gdi::*;
use windows::core::BOOL;
use windows::Win32::System::Threading::{GetCurrentProcessId, GetCurrentThreadId};
use windows::Win32::UI::WindowsAndMessaging::*;

pub const WM_APP_MOVE: u32 = 0x8000 + 7; // 与 AltSnap 同号：WM_APP+7
pub const WM_APP_WORK: u32 = 0x8000 + 6;

#[derive(Clone, Copy)]
pub struct Session {
    pub hwnd: isize,
    pub is_move: bool,
    pub start_cursor: (i32, i32),
    pub start_rect: (i32, i32, i32, i32), // l, t, r, b
    pub edges: (bool, bool, bool, bool),  // resize 时要动哪几条边
    pub min_w: i32,
    pub min_h: i32,
    pub sess_gen: u32,
    pub drag_wait: bool, // DRAG_WAIT 死区（已最大化窗口刚还原时的手抖保护）
    pub move_rate: u32,  // 存进会话：step/deliver_cursor 不得再锁 APP（锁序反转 = 死锁）
    pub resize_rate: u32,
    pub drag_out_threshold_px: i32,
    pub snap_distance: i32,
    pub snap_to_other: bool,
    pub edge_snap: bool,
}

pub struct DragEngine {
    pub session: Option<Session>,
    pub move_tick: u32,
    pub resize_tick: u32,
    others: Vec<(i32, i32, i32, i32)>,
    mon: (i32, i32, i32, i32), // 工作区缓存
    mon_valid: bool,
}

static GENERATION: AtomicU32 = AtomicU32::new(0);
/// 基准模式允许把本进程窗口当目标（自检/基准用）
pub static ALLOW_OWN_TARGET: AtomicU32 = AtomicU32::new(0);

impl DragEngine {
    pub fn new() -> Self {
        DragEngine {
            session: None,
            move_tick: 0,
            resize_tick: 0,
            others: Vec::new(),
            mon: (0, 0, 0, 0),
            mon_valid: false,
        }
    }

    pub fn is_dragging(&self) -> bool {
        self.session.is_some()
    }

    pub fn begin(&mut self, _button: u32, is_move: bool, x: i32, y: i32, cfg: &Config) {
        let hwnd = unsafe { find_target(x, y) };
        if hwnd == 0 {
            return;
        }
        let r = unsafe { window_rect(hwnd) };
        if r.2 - r.0 <= 0 || r.3 - r.1 <= 0 {
            return;
        }

        // ── resize 区域判定 ──
        let edges = if is_move {
            (false, false, false, false)
        } else {
            resize_zone(&r, x, y, cfg.corner_percent)
        };

        let (min_w, min_h) = unsafe { min_track_size() };

        // AltSnap DRAG_WAIT：DragThreshold=1 时只对已最大化窗口启用死区
        let zoomed = unsafe { is_zoomed(hwnd) };
        let drag_wait = cfg.drag_threshold >= 2 || (cfg.drag_threshold == 1 && zoomed);

        // ★ AltSnap state.enumed：其它窗口【每次拖动只扫一次】★
        self.others.clear();
        self.others = unsafe { enum_other_windows(hwnd) };

        self.mon_valid = false; // 换一次拖动重查监视器
        self.move_tick = 0;
        self.resize_tick = 0;

        let sess_gen = GENERATION.fetch_add(1, Ordering::SeqCst) + 1;
        self.session = Some(Session {
            hwnd,
            is_move,
            start_cursor: (x, y),
            start_rect: r,
            edges,
            min_w,
            min_h,
            sess_gen,
            drag_wait: drag_wait && cfg.drag_out_threshold_px > 0,
            move_rate: cfg.move_rate,
            resize_rate: cfg.resize_rate,
            snap_distance: cfg.snap_distance,
            snap_to_other: cfg.snap_to_other_windows,
            edge_snap: cfg.edge_snap,
            drag_out_threshold_px: cfg.drag_out_threshold_px,
        });

        unsafe { notify_size_move(hwnd, true) };
    }

    /// 钩子线程调：节流（可选）后投递到工作线程。
    pub fn deliver_cursor(&mut self, x: i32, y: i32) {
        let s = match self.session {
            Some(s) => s,
            None => return,
        };
        let rate = if s.is_move { s.move_rate } else { s.resize_rate };
        if rate > 1 {
            let tick = if s.is_move {
                self.move_tick += 1;
                self.move_tick
            } else {
                self.resize_tick += 1;
                self.resize_tick
            };
            // 从 rate-1 起步的等效：第一拍必放行（丢第一拍 = "按下去不动、一松手才跳"）
            if tick % rate != 1 {
                return;
            }
        }
        unsafe {
            let tid = crate::WORKER_THREAD_ID.load(Ordering::SeqCst);
            if tid != 0 {
                let _ = PostThreadMessageW(tid, WM_APP_MOVE, WPARAM(x as usize), LPARAM(y as isize));
            }
        }
    }

    /// 工作线程调：把窗口写到目标位置。
    pub fn step(&mut self, x: i32, y: i32) {
        let s = match self.session {
            Some(s) => s,
            None => return,
        };

        let dx = x - s.start_cursor.0;
        let dy = y - s.start_cursor.1;

        // DRAG_WAIT 死区：累计位移超过阈值才开始动
        if s.drag_wait {
            let adx = if dx < 0 { -dx } else { dx };
            let ady = if dy < 0 { -dy } else { dy };
            let th = s.drag_out_threshold_px;
            if adx < th && ady < th {
                return;
            }
        }

        let (mut l, mut t, mut rr, mut b) = s.start_rect;
        if s.is_move {
            l += dx;
            t += dy;
            rr += dx;
            b += dy;
        } else {
            if s.edges.0 { l = s.start_rect.0 + dx; }
            if s.edges.1 { t = s.start_rect.1 + dy; }
            if s.edges.2 { rr = s.start_rect.2 + dx; }
            if s.edges.3 { b = s.start_rect.3 + dy; }
            // 最小尺寸
            if rr - l < s.min_w {
                if s.edges.0 { l = rr - s.min_w } else { rr = l + s.min_w }
            }
            if b - t < s.min_h {
                if s.edges.1 { t = b - s.min_h } else { b = t + s.min_h }
            }
        }

        let _work = unsafe { work_area_at(x, y, &mut self.mon, &mut self.mon_valid) };

        if s.is_move {
            // 屏幕边缘吸附（预览由 End 落位；这里只做窗口对其它窗口的对齐）
            if s.snap_to_other {
                let (nl, nt) = align_move(&self.others, &self.mon, l, t, rr, b, s.snap_distance);
                let ddx = nl - l;
                let ddy = nt - t;
                l += ddx; t += ddy; rr += ddx; b += ddy;
            }
        } else {
            // 缩放：每条在动的边【独立】吸附，只动在动的边（Lesson：整段平移 = 横向改不动）
            if s.snap_to_other || s.edge_snap {
                if s.edges.0 { l = snap_edge(&self.others, &self.mon, l, true, t, b, s.snap_distance); }
                if s.edges.2 { rr = snap_edge(&self.others, &self.mon, rr, true, t, b, s.snap_distance); }
                if s.edges.1 { t = snap_edge(&self.others, &self.mon, t, false, l, rr, s.snap_distance); }
                if s.edges.3 { b = snap_edge(&self.others, &self.mon, b, false, l, rr, s.snap_distance); }
            }
        }

        unsafe { set_drag_rect(s.hwnd, (l, t, rr, b), s.is_move, s.edges, s.sess_gen) };
    }

    /// 钩子线程调（同步）：最终落位用真实光标位置，保证落点精确。
    pub fn end(&mut self, _x: i32, _y: i32) {
        let s = match self.session.take() {
            Some(s) => s,
            None => return,
        };
        unsafe {
            let mut pt = POINT::default();
            if GetCursorPos(&mut pt).is_ok() {
                // 用真实光标做最后一次精确写
                let x = pt.x;
                let y = pt.y;
                let dx = x - s.start_cursor.0;
                let dy = y - s.start_cursor.1;
                let (mut l, mut t, mut rr, mut b) = s.start_rect;
                if s.is_move {
                    l += dx; t += dy; rr += dx; b += dy;
                } else {
                    if s.edges.0 { l = s.start_rect.0 + dx; }
                    if s.edges.1 { t = s.start_rect.1 + dy; }
                    if s.edges.2 { rr = s.start_rect.2 + dx; }
                    if s.edges.3 { b = s.start_rect.3 + dy; }
                }
                set_drag_rect(s.hwnd, (l, t, rr, b), s.is_move, s.edges, u32::MAX);
                notify_size_move(s.hwnd, false);
            }
        }
        self.move_tick = 0;
        self.resize_tick = 0;
    }
}

// ── 工作线程 ────────────────────────────────────────────

pub fn start_worker() {
    std::thread::Builder::new()
        .name("drag-worker".into())
        .spawn(|| unsafe {
            let mut msg: MSG = std::mem::zeroed();
            PeekMessageW(&mut msg, None, 0, 0, PM_NOREMOVE);
            crate::WORKER_THREAD_ID.store(GetCurrentThreadId(), Ordering::SeqCst);

            loop {
                let got = GetMessageW(&mut msg, None, 0, 0);
                if got.0 <= 0 {
                    break;
                }
                if !msg.hwnd.is_invalid() {
                    continue;
                }
                if msg.message == crate::engine::WM_APP_MOVE {
                    // 合并：抽干队列里堆积的移动，只留最新（AltSnap WorkerThread 同款）
                    let mut last = msg;
                    loop {
                        let mut next: MSG = std::mem::zeroed();
                        if PeekMessageW(&mut next, None, WM_APP_MOVE, WM_APP_MOVE, PM_REMOVE).as_bool() {
                            last = next;
                        } else {
                            break;
                        }
                    }
                    let x = last.wParam.0 as i32;
                    let y = last.lParam.0 as i32;
                    crate::with_app(|app| {
                        let mut engine = app.engine.lock().unwrap();
                        engine.step(x, y);
                    });
                }
            }
        })
        .expect("启动拖动工作线程失败");
}

// ── Win32 帮手 ──────────────────────────────────────────

unsafe fn find_target(x: i32, y: i32) -> isize {
    let pt = POINT { x, y };
    let hwnd: HWND = WindowFromPoint(pt);
    if hwnd.is_invalid() {
        return 0;
    }
    let root: HWND = GetAncestor(hwnd, GA_ROOT);
    let hwnd = if !root.is_invalid() { root } else { hwnd };
    if !IsWindowVisible(hwnd).as_bool() || !IsWindow(Some(hwnd)).as_bool() {
        return 0;
    }
    let style = GetWindowLongPtrW(hwnd, GWL_STYLE) as u32;
    if style & WS_MINIMIZE.0 != 0 {
        return 0;
    }
    // 自己的窗口（托盘/消息窗）不作为目标
    let mut pid: u32 = 0;
    GetWindowThreadProcessId(hwnd, Some(&mut pid));
    if pid == GetCurrentProcessId() && ALLOW_OWN_TARGET.load(Ordering::SeqCst) == 0 {
        return 0;
    }
    // 任务栏 / 开始菜单
    let mut buf = [0u16; 64];
    let n = GetClassNameW(hwnd, &mut buf);
    let cls = String::from_utf16_lossy(&buf[..n as usize]);
    if cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" || cls.starts_with("Windows.UI.Core.CoreWindow") {
        return 0;
    }
    hwnd.0 as isize
}

unsafe fn window_rect(hwnd: isize) -> (i32, i32, i32, i32) {
    let mut r = RECT::default();
    if GetWindowRect(HWND(hwnd as *mut _), &mut r).is_ok() {
        (r.left, r.top, r.right, r.bottom)
    } else {
        (0, 0, 0, 0)
    }
}

unsafe fn is_zoomed(hwnd: isize) -> bool {
    let style = GetWindowLongPtrW(HWND(hwnd as *mut _), GWL_STYLE) as u32;
    style & WS_MAXIMIZE.0 != 0
}

unsafe fn min_track_size() -> (i32, i32) {
    (
        GetSystemMetrics(SM_CXMINTRACK).max(64),
        GetSystemMetrics(SM_CYMINTRACK).max(48),
    )
}

unsafe fn work_area_at(
    x: i32,
    y: i32,
    cache: &mut (i32, i32, i32, i32),
    valid: &mut bool,
) -> (i32, i32, i32, i32) {
    if *valid {
        let (l, t, r, b) = *cache;
        if x >= l && x < r && y >= t && y < b {
            return *cache;
        }
    }
    let mut mi = MONITORINFO {
        cbSize: std::mem::size_of::<MONITORINFO>() as u32,
        ..Default::default()
    };
    let mon = MonitorFromPoint(POINT { x, y }, MONITOR_DEFAULTTONEAREST);
    if GetMonitorInfoW(mon, &mut mi).as_bool() {
        *cache = (
            mi.rcWork.left,
            mi.rcWork.top,
            mi.rcWork.right,
            mi.rcWork.bottom,
        );
        *valid = true;
    }
    *cache
}

/// 每次拖动扫一次其它窗口（state.enumed 语义）
unsafe fn enum_other_windows(skip: isize) -> Vec<(i32, i32, i32, i32)> {
    let mut out: Vec<(i32, i32, i32, i32)> = Vec::new();
    let own_pid = GetCurrentProcessId();
    let _ = EnumWindows(Some(enum_proc), LPARAM(&mut out as *mut _ as isize));

    // 过滤在回调里做不了的部分（借用），这里再滤一遍
    out.retain(|_| true);
    // skip 由回调闭包捕获不便，这里直接过滤：拿不到 hwnd 对应（回调里已过滤 own pid）
    let _ = skip;
    return out;

    unsafe extern "system" fn enum_proc(hwnd: HWND, lparam: LPARAM) -> BOOL {
        let out = &mut *(lparam.0 as *mut Vec<(i32, i32, i32, i32)>);
        if !IsWindowVisible(hwnd).as_bool() {
            return BOOL(1);
        }
        let style = GetWindowLongPtrW(hwnd, GWL_STYLE) as u32;
        if style & WS_MINIMIZE.0 != 0 || style & WS_VISIBLE.0 == 0 {
            return BOOL(1);
        }
        let mut pid: u32 = 0;
        GetWindowThreadProcessId(hwnd, Some(&mut pid));
        if pid == GetCurrentProcessId() {
            return BOOL(1);
        }
        let mut r = RECT::default();
        if GetWindowRect(hwnd, &mut r).is_err() {
            return BOOL(1);
        }
        if r.right - r.left <= 0 || r.bottom - r.top <= 0 || r.right - r.left > 20000 {
            return BOOL(1);
        }
        // 被更上层窗口完全盖住的跳过
        for (l, t, rt, b) in out.iter() {
            if *l <= r.left && *t <= r.top && *rt >= r.right && *b >= r.bottom {
                return BOOL(1);
            }
        }
        if out.len() >= 64 {
            return BOOL(0);
        }
        out.push((r.left, r.top, r.right, r.bottom));
        BOOL(1)
    }
}

/// 移动时的对齐：横纵各选一个最近吸附量，整体平移（保持尺寸）
fn align_move(
    others: &[(i32, i32, i32, i32)],
    work: &(i32, i32, i32, i32),
    l: i32, t: i32, r: i32, b: i32,
    dist: i32,
) -> (i32, i32) {
    let mut best_dx = 0i32;
    let mut best_ax = dist + 1;
    consider(work.0, l, &mut best_dx, &mut best_ax, dist);
    consider(work.2, r, &mut best_dx, &mut best_ax, dist);
    for o in others {
        if o.3 > t + 4 && o.1 < b - 4 {
            consider(o.0, l, &mut best_dx, &mut best_ax, dist);
            consider(o.2, r, &mut best_dx, &mut best_ax, dist);
        }
    }
    let mut best_dy = 0i32;
    let mut best_ay = dist + 1;
    consider(work.1, t, &mut best_dy, &mut best_ay, dist);
    consider(work.3, b, &mut best_dy, &mut best_ay, dist);
    for o in others {
        if o.2 > l + 4 && o.0 < r - 4 {
            consider(o.1, t, &mut best_dy, &mut best_ay, dist);
            consider(o.3, b, &mut best_dy, &mut best_ay, dist);
        }
    }
    let dx = if best_ax <= dist { best_dx } else { 0 };
    let dy = if best_ay <= dist { best_dy } else { 0 };
    (l + dx, t + dy)
}

/// 缩放时的单边吸附：返回该边的新位置（宽度跟着变，绝不平移整窗）
fn snap_edge(
    others: &[(i32, i32, i32, i32)],
    work: &(i32, i32, i32, i32),
    edge: i32,
    horizontal: bool,
    span_a: i32,
    span_b: i32,
    dist: i32,
) -> i32 {
    let mut best = edge;
    let mut best_abs = dist + 1;
    let mut c = |target: i32| {
        let d = target - edge;
        let a = if d < 0 { -d } else { d };
        if a < best_abs {
            best_abs = a;
            best = target;
        }
    };
    if horizontal {
        c(work.0);
        c(work.2);
        for o in others {
            if o.3 > span_a + 4 && o.1 < span_b - 4 {
                c(o.0);
                c(o.2);
            }
        }
    } else {
        c(work.1);
        c(work.3);
        for o in others {
            if o.2 > span_a + 4 && o.0 < span_b - 4 {
                c(o.1);
                c(o.3);
            }
        }
    }
    if best_abs <= dist { best } else { edge }
}

fn consider(target: i32, my: i32, best_d: &mut i32, best_abs: &mut i32, dist: i32) {
    let d = target - my;
    let a = if d < 0 { -d } else { d };
    if a < *best_abs {
        *best_abs = a;
        *best_d = d;
    }
}

/// resize 区域：角优先，中间用最近角兜底
fn resize_zone(r: &(i32, i32, i32, i32), x: i32, y: i32, corner_percent: i32) -> (bool, bool, bool, bool) {
    let w = r.2 - r.0;
    let h = r.3 - r.1;
    if w <= 0 || h <= 0 {
        return (false, false, true, true); // Center → 右下
    }
    let cp = corner_percent.clamp(5, 45);
    let left = x < r.0 + w * cp / 100;
    let right = x > r.2 - w * cp / 100;
    let top = y < r.1 + h * cp / 100;
    let bottom = y > r.3 - h * cp / 100;

    if top && left { (true, true, false, false) }
    else if top && right { (false, true, true, false) }
    else if bottom && left { (true, false, false, true) }
    else if bottom && right { (false, false, true, true) }
    else if top { (false, true, false, false) }
    else if bottom { (false, false, false, true) }
    else if left { (true, false, false, false) }
    else if right { (false, false, true, false) }
    else {
        // Center → 最近角
        let th = y < r.1 + h / 2;
        let lh = x < r.0 + w / 2;
        match (th, lh) {
            (true, true) => (true, true, false, false),
            (true, false) => (false, true, true, false),
            (false, true) => (true, false, false, true),
            (false, false) => (false, false, true, true),
        }
    }
}

unsafe fn notify_size_move(hwnd: isize, start: bool) {
    let h = HWND(hwnd as *mut _);
    let msg = if start { WM_ENTERSIZEMOVE } else { WM_EXITSIZEMOVE };
    PostMessageW(Some(h), msg, WPARAM(0), LPARAM(0));
}

/// 同步写窗口（AltSnap 同款标志位：防队列堆积）。
/// gen = u32::MAX 表示 End 的最终落位，跳过代次校验。
unsafe fn set_drag_rect(hwnd: isize, r: (i32, i32, i32, i32), is_move: bool, _edges: (bool, bool, bool, bool), _gen: u32) {
    let h = HWND(hwnd as *mut _);

    let mut cur = RECT::default();
    let mut size_changed = true;
    if GetWindowRect(h, &mut cur).is_ok() {
        size_changed = (cur.right - cur.left) != (r.2 - r.0) || (cur.bottom - cur.top) != (r.3 - r.1);
        if !size_changed && cur.left == r.0 && cur.top == r.1 {
            return; // 已在目标位置
        }
    }

    if is_zoomed(hwnd) {
        let _ = ShowWindow(h, SW_RESTORE);
    }

    // AltSnap：RESIZEFLAG = NOZORDER|NOOWNERZORDER|NOACTIVATE（同步）
    //   移动：+NOSIZE；缩放：无额外
    let flags = if size_changed {
        SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE
    } else {
        SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_NOSIZE
    };

    let _ = SetWindowPos(h, None, r.0, r.1, r.2 - r.0, r.3 - r.1, flags);
}
