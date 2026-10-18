using System;
using System.Collections.Generic;
using System.Threading;

namespace HoldDrag
{
    /// <summary>
    /// 晃窗手势识别器 —— 严格版（第四十轮重写）。
    ///
    /// ══════════════════════════════════════════════════════════════════
    ///  为什么必须重写：用户反馈"目前只要拖动窗口就会触发，属于误触发"。
    ///
    ///  旧实现的三个致命参数：
    ///    · MinSpanPx = 24 —— 而且是【物理像素】。用户屏幕 200%，
    ///      24 物理像素只有 12 个逻辑像素 —— 拖动时手抖一下就够了。
    ///    · WindowMs = 2000 —— 两秒的时间窗太长。正常拖窗口"拖过去、
    ///      过头了、拉回来、再微调"本来就有 3 次方向反转，
    ///      于是"认真拖一扇窗"就满足了触发条件。
    ///    · 只看 X 的增量方向，不看【净位移】—— 而净位移才是
    ///      "原地晃" 和 "把窗口拖走" 最本质的区别。
    ///
    ///  Aero Shake 的判定要点（Windows 自带那套）正是：
    ///    ① 极短时间内完成多次往复（快）；
    ///    ② 每次往复的幅度够大（不是抖动噪声）；
    ///    ③ 往复幅度彼此接近、且【净位移接近 0】—— 窗口基本留在原地；
    ///    ④ 以水平方向为主。
    ///  本实现把这四条全部落成硬约束，并全部以【逻辑像素】给阈值。
    /// ══════════════════════════════════════════════════════════════════
    ///
    /// 实现方式是【滑动窗口 + 一次性评估】，而不是旧的增量状态机：
    ///   · 增量状态机很难表达"净位移"（要额外维护锚点，且窗口过期时的
    ///     重置语义很容易写出漏判）；
    ///   · 滑动窗口每次只评估"最近 GestureWindowMs 内的轨迹"，
    ///     起点、终点、路径长度、净位移都是现成的，判据一目了然，
    ///     也天然满足"手势必须在这段时间内完成"。
    ///
    /// 线程：只在拖动工作线程上被调用，无锁。
    /// </summary>
    internal sealed class ShakeDetector
    {
        // ══════ 阈值：全部以【逻辑像素 / 毫秒】给出 ══════
        //   内部按 DPI 换算成物理像素。这一点是旧实现最大的错 ——
        //   写死物理像素的阈值在高 DPI 屏上等于把门槛缩小了好几倍。

        /// <summary>一次有效往复的最小幅度（逻辑像素）。低于它视为手抖噪声。</summary>
        public const int MinSpanLogical = 32;

        /// <summary>
        /// 单次单向位移的上限（逻辑像素）。超过它说明用户在"把窗口拖走"，
        /// 而不是原地晃 —— 整段轨迹直接作废。
        /// 这是区分"晃动"和"拖动"最强的一条判据。
        /// </summary>
        public const int MaxSpanLogical = 220;

        /// <summary>整个手势必须在这段时间内完成（ms）。Aero Shake 是"快抖"。</summary>
        public const int GestureWindowMs = 800;

        /// <summary>需要的有效往复次数（4 次 = 两个完整来回）。</summary>
        public const int ReversalsNeeded = 4;

        /// <summary>净位移上限（逻辑像素）：晃完窗口必须基本回到原处。</summary>
        public const int MaxNetDriftLogical = 80;

        /// <summary>轨迹总长度下限（逻辑像素）：太短的"抖"不算手势。</summary>
        public const int MinPathLogical = 150;

        /// <summary>横向位移占总路径的最低占比。上下抖窗口不该触发。</summary>
        public const double HorizontalDominance = 0.72;

        /// <summary>触发后的冷却（ms）：期间不再识别，避免一次晃连续触发。</summary>
        public const int CooldownMs = 900;

        /// <summary>滑动窗口最多保留的采样点数（高回报率鼠标 1000Hz × 0.8s = 800）。</summary>
        private const int SampleCap = 768;

        private struct Sample
        {
            public int X;
            public int Y;
            public long T;
        }

        private readonly Sample[] _buf = new Sample[SampleCap];
        private int _n;        // 有效样本数
        private int _head;     // 下一个写入位置
        private bool _armed;
        private long _coolUntil;
        private double _scale = 1.0;   // 物理像素 / 逻辑像素

        /// <summary>按显示器 DPI 设置换算系数（物理像素 = 逻辑像素 × scale）。</summary>
        public void SetScale(double scale)
        {
            _scale = (scale > 0.1 && scale < 8.0) ? scale : 1.0;
        }

        /// <summary>新会话开始时调用：清空状态并播种起点。</summary>
        public void Reset(int x, int y, long nowMs)
        {
            _n = 0;
            _head = 0;
            _coolUntil = 0;
            _armed = true;
            Push(x, y, nowMs);
        }

        /// <summary>无效化（会话结束）。下次 Feed 前必须再 Reset。</summary>
        public void Invalidate()
        {
            _armed = false;
            _n = 0;
            _head = 0;
        }

        /// <summary>
        /// 喂入当前光标坐标。返回 true = 这一次触发了"晃窗"。
        /// nowMs 用 Environment.TickCount64。
        /// </summary>
        public bool Feed(int x, int y, long nowMs)
        {
            if (!_armed) return false;
            if (nowMs < _coolUntil) return false;

            Push(x, y, nowMs);

            // 丢掉超出时间窗的旧样本（滑动窗口）
            while (_n > 0 && nowMs - Oldest().T > GestureWindowMs) DropOldest();

            if (_n < 6) return false;

            if (!Evaluate()) return false;

            // 触发：进入冷却，并清空窗口（冷却结束自然重新开始）
            _coolUntil = nowMs + CooldownMs;
            _n = 0;
            _head = 0;
            return true;
        }

        /// <summary>滑动窗口评估：四条硬约束全过才算晃窗。</summary>
        private bool Evaluate()
        {
            int minSpan = (int)Math.Round(MinSpanLogical * _scale);
            int maxSpan = (int)Math.Round(MaxSpanLogical * _scale);
            int minPath = (int)Math.Round(MinPathLogical * _scale);
            int maxNet = (int)Math.Round(MaxNetDriftLogical * _scale);
            if (minSpan < 8) minSpan = 8;
            if (maxNet < minSpan) maxNet = minSpan;

            int reversals = 0;
            int pathX = 0, pathY = 0;
            int curDir = 0, span = 0;
            bool overshoot = false;

            var prev = At(0);
            for (int i = 1; i < _n; i++)
            {
                var s = At(i);
                int dx = s.X - prev.X;
                int dy = s.Y - prev.Y;
                prev = s;

                pathX += Math.Abs(dx);
                pathY += Math.Abs(dy);

                int adx = Math.Abs(dx);
                if (adx == 0) continue;

                int dir = dx > 0 ? 1 : -1;
                if (curDir == 0) { curDir = dir; span = adx; continue; }

                if (dir == curDir)
                {
                    span += adx;
                    // 单向拉得太远 → 这是在拖窗口，不是在晃
                    if (span > maxSpan) overshoot = true;
                    continue;
                }

                // 方向反转：上一段够长才算一次有效往复
                if (span >= minSpan) reversals++;
                curDir = dir;
                span = adx;
            }

            // ① 有"拖走"特征 → 直接否掉（哪怕后面又晃了几下）
            if (overshoot) return false;

            // ② 往复次数不够
            if (reversals < ReversalsNeeded) return false;

            // ③ 横向路径太短
            if (pathX < minPath) return false;

            // ④ 必须是横向为主的抖动
            int path = pathX + pathY;
            if (path <= 0) return false;
            if (pathX < path * HorizontalDominance) return false;

            // ⑤ 净位移必须接近 0（晃完还留在原处）
            var first = At(0);
            var last = At(_n - 1);
            if (Math.Abs(last.X - first.X) > maxNet) return false;
            if (Math.Abs(last.Y - first.Y) > maxNet) return false;

            return true;
        }

        // ── 环形缓冲 ────────────────────────────────────────────

        private void Push(int x, int y, long nowMs)
        {
            _buf[_head] = new Sample { X = x, Y = y, T = nowMs };
            _head = (_head + 1) % SampleCap;
            if (_n < SampleCap) _n++;
        }

        private Sample Oldest()
        {
            int idx = (_head - _n + SampleCap) % SampleCap;
            return _buf[idx];
        }

        private void DropOldest()
        {
            if (_n <= 0) return;
            _n--;
        }

        /// <summary>第 i 个样本（0 = 最旧）。</summary>
        private Sample At(int i)
        {
            int idx = (_head - _n + i + SampleCap * 2) % SampleCap;
            return _buf[idx];
        }

        // ── 自检探针 ────────────────────────────────
        internal int SampleCountForTest { get { return _n; } }
        internal bool CoolingForTest { get { return _coolUntil != 0; } }
    }

    /// <summary>
    /// 晃窗手势的【执行器】：把其它可见窗口全部最小化；再晃一次把它们还原。
    ///
    /// ★ 关于"直接调用系统原生功能" ★
    ///   查过：Windows **没有**公开 API 能做到"最小化除当前窗口外的所有窗口"。
    ///   Aero Shake 是 Explorer 内部对拖动轨迹的响应，不对第三方开放；
    ///   系统层面能用的最接近的东西是 Shell 的 IShellDispatch::MinimizeAll()，
    ///   但它是"最小化【全部】窗口"—— 会把用户正在拖的这一扇也一起收掉，
    ///   而且没有"只收其它窗口"的开关。
    ///   所以这里沿用"枚举 + SW_MINIMIZE + 记录还原名单"的做法：
    ///   语义上正是 Aero Shake 的效果（当前窗口留下、其它全部收起、再晃还原），
    ///   而且比 MinimizeAll 多一个"可逆"的能力。
    ///
    /// 状态是进程级静态的 —— 还原名单必须跨拖动会话存活（用户晃完松开，
    /// 拖另一扇窗，再晃一次要还能还原上一轮最小化的那批）。
    ///
    /// 线程纪律：整个扫描跑在独立的后台线程上，调用方【即发即弃】绝不等结果 ——
    /// 调用方是拖动工作线程，等它会把跟手直接卡住。EnumWindows / GetWindowRect
    /// 本身不发送消息（挂死的窗口也答得上来），ShowWindowAsync likewise，
    /// 所以这条线程不会被哪个卡死的程序拖住。
    /// </summary>
    internal static class ShakeAction
    {
        private static readonly object Gate = new object();
        private static readonly List<IntPtr> MinimizedByUs = new List<IntPtr>();

        /// <summary>当前是否正处于"其它窗口已被我们最小化"的状态（自检/托盘提示用）。</summary>
        public static bool OthersMinimized { get { lock (Gate) { return MinimizedByUs.Count > 0; } } }

        /// <summary>
        /// 切换：没最小化过 → 全部最小化；已最小化过 → 全部还原。
        /// 立即返回，实际扫描在后台线程上跑，完成后自己写日志。
        /// </summary>
        public static void ToggleMinimizeOthers(IntPtr keepHwnd)
        {
            var t = new Thread(delegate () { Run(keepHwnd); });
            t.IsBackground = true;
            t.Name = "HoldDrag.ShakeAction";
            t.Start();
        }

        private static void Run(IntPtr keepHwnd)
        {
            lock (Gate)
            {
                if (MinimizedByUs.Count > 0)
                {
                    int n = 0;
                    for (int i = 0; i < MinimizedByUs.Count; i++)
                    {
                        var h = MinimizedByUs[i];
                        if (h == IntPtr.Zero) continue;
                        if (!Native.IsWindow(h)) continue;
                        Native.ShowWindowAsync(h, Native.SW_RESTORE);
                        n++;
                    }
                    MinimizedByUs.Clear();
                    Log.Info("晃窗：还原 " + n + " 扇窗口");
                    return;
                }
            }

            // ── 最小化除 keepHwnd 以外的所有可见顶层窗口 ──
            //   写法照抄 WindowActions 的命中测试：内联闭包委托 + IntPtr.Zero，
            //   捕获 keepHwnd / found 两个局部变量（项目里 EnumWindows 都是这个路子）。
            var found = new List<IntPtr>();
            try
            {
                Native.EnumWindows(delegate (IntPtr h, IntPtr l)
                {
                    try
                    {
                        if (h == keepHwnd) return true;                      // 当前窗口不动
                        if (!Native.IsWindow(h)) return true;
                        if (!Native.IsWindowVisible(h)) return true;         // 看不见的不动

                        long style = Native.GetWindowLong(h, Native.GWL_STYLE);
                        if ((style & Native.WS_MINIMIZE) != 0) return true;  // 已经最小化的不记入还原名单
                        if ((style & Native.WS_VISIBLE) == 0) return true;

                        uint pid;
                        Native.GetWindowThreadProcessId(h, out pid);
                        if (pid == (uint)SelfPid) return true;               // 我们自己的浮层 / 隐藏窗口

                        Native.RECT r;
                        if (!Native.GetWindowRect(h, out r)) return true;
                        if (r.Width <= 0 || r.Height <= 0) return true;

                        found.Add(h);
                    }
                    catch (Exception ex) { Log.Error(ex); }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { Log.Error(ex); }

            int count = 0;
            for (int i = 0; i < found.Count; i++)
            {
                Native.ShowWindowAsync(found[i], Native.SW_MINIMIZE);
                count++;
            }
            lock (Gate)
            {
                MinimizedByUs.AddRange(found);
            }
            Log.Info("晃窗：最小化 " + count + " 扇窗口（保留当前窗口）");
        }

        private static readonly int SelfPid = System.Diagnostics.Process.GetCurrentProcess().Id;
    }
}
