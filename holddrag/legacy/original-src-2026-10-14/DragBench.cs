using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag
{
    /// <summary>
    /// 拖动流畅度基准（--dragbench）。
    ///
    /// 目的：把"一卡一卡、不跟鼠标轨迹"变成客观数字。
    /// 方法：模拟 125Hz 的鼠标移动事件流（与真实钩子等价的入口），
    ///       匀速拖一个真实窗口走 600px 直线，同时用独立线程以 1kHz
    ///       采样窗口矩形。窗口位置每变一次记一个"更新点"，统计：
    ///         · 更新点数量（健康目标理想 ≈ 事件数；慢目标 = 目标真实消化能力，这是同步写的设计上限，不再是缺陷）
    ///         · 相邻更新的间隔（平均 / P95 / 最大 —— 最大间隔就是"卡的那一下"）
    ///         · 更新点数 ÷ 时长 = 实际有效帧率
    /// </summary>
    internal static class DragBench
    {
        private class Sample
        {
            public long T;
            public int X, Y;
        }

        public static void Run()
        {
            string dir = Path.Combine(Path.GetTempPath(), "holddrag-bench-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var lines = new List<string>();

            try
            {
                // ★ 默认关掉逐帧日志：Verbose 会在热路径上做字符串拼接 + 文件 I/O，
                //   本身就会制造停顿，测出来的"卡"是日志的卡不是拖动的卡。
                //   要看每帧 期望/实际 对比时加 --verbose。
                foreach (var a0 in Environment.GetCommandLineArgs())
                    if (a0 == "--verbose") Log.Verbose = true;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }

                WindowActions.AllowOwnProcessAsTargetForTest = true;
                WindowActions.ForcedTargetForTest = IntPtr.Zero;
                WindowActions.ResetFrameStats();

                var store = new ConfigStore(dir);
                store.Load();
                var c = store.Current;
                c.TriggerKeyId = "K:0x0D";
                c.RequireHold = true;
                c.HoldMs = 100;
                c.ActionLeft = Actions.Move;
                c.ActionRight = Actions.Resize;
                c.ActionMiddle = Actions.Nothing;
                c.ShowActivationFrame = true;
                c.BringToFront = false;
                c.MovePreviewMode = PreviewModes.FullWin;   // 实测实时移动路径
                c.MoveRate = Math.Max(1, c.MoveRate);
                c.ResizeRate = Math.Max(1, c.ResizeRate);

                using (var win = new SlowTargetForm())
                {
                    win.FormBorderStyle = FormBorderStyle.Sizable;
                    win.StartPosition = FormStartPosition.Manual;
                    win.ShowInTaskbar = false;
                    win.TopMost = true;
                    win.Bounds = new System.Drawing.Rectangle(200, 200, 500, 380);
                    win.Show();
                    Application.DoEvents();

                    var coord = new Coordinator(store);
                    coord.PrecreateOverlays();
                    Application.DoEvents();

                    IntPtr h = win.Handle;

                    // 采样线程：1kHz 记录窗口矩形，只记变化点
                    var samples = new List<Sample>();
                    var ringSamples = new List<(long, int, int)>();
                    IntPtr ringH = coord.Drag.Frame.Handle;   // 第四十轮：拖动中的视觉反馈改用抗锯齿分层框
                    var sampleStop = new ManualResetEventSlim(false);
                    // 全基准共用一个时钟：采样线程和驱动线程都按它打点，
                    // 这样才能算出"拖动开始后第 T 毫秒，窗口本该在哪"（滞后分析要用）。
                    var clock = Stopwatch.StartNew();
                    long dragStartMs = -1;
                    // 驱动线程真实投递每个事件的时刻（共享时钟打点，含最后的 up）。
                    // 滞后分析按它反查"T 时刻的理想位置"—— 驱动线程的调度抖动
                    // 不会再污染指标（旧版线性模型在健康目标上测出 -162ms 假滞后）。
                    var evTimes = new List<long>();
                    var sampler = new Thread(delegate ()
                    {
                        Native.RECT last = default(Native.RECT);
                        bool has = false;
                        while (!sampleStop.IsSet)
                        {
                            Native.RECT r;
                            if (Native.GetWindowRect(h, out r) && (!has || r.Left != last.Left || r.Top != last.Top))
                            {
                                lock (samples) samples.Add(new Sample { T = clock.ElapsedMilliseconds, X = r.Left, Y = r.Top });
                                last = r; has = true;
                            }

                            // 同时追环：预览框是否真的在动
                            {
                                Native.RECT rr;
                                if (Native.GetWindowRect(ringH, out rr))
                                    lock (ringSamples) ringSamples.Add((clock.ElapsedMilliseconds, rr.Left, rr.Top));
                            }
                            Thread.Sleep(1);
                        }
                    });                    sampler.IsBackground = true;
                    sampler.Start();

                    // ── 拖动：匀速直线 600px，125Hz，2 秒 ──
                    // ★ 驱动必须在后台线程：主线程要专职泵目标窗口的消息。
                    //   （第一版驱动占着主线程，同步 SetWindowPos 等目标处理消息，
                    //   而目标的消息循环停摆 → 2 秒只动了 2 次 —— 正是
                    //   "目标不泵消息 = 同步写卡死"的现场演示。）
                    Native.RECT r0;
                    Native.GetWindowRect(h, out r0);
                    var grab = new Native.POINT(r0.Left + r0.Width / 2, r0.Top + r0.Height / 2);

                    // ★ 靶窗必须真的在抓取点最上层 ★
                    //   曾经跑出过一次"帧完美跟随、靶窗一步没动"的诡异 FAIL：
                    //   自检刚拖动过的 Chrome 全屏置顶窗压在靶窗上方，
                    //   WindowFromPoint 抓到的是它 —— 会话全程正常，采样的却是
                    //   没人管的靶窗。没有这道检查，这种误测会以"性能回归"的
                    //   面貌骗人。先激活再核一遍，核不上就明确中止。
                    if (Native.WindowFromPoint(grab) != h)
                    {
                        try { win.Activate(); Application.DoEvents(); } catch { }
                        if (Native.WindowFromPoint(grab) != h)
                        {
                            lines.Add("[FAIL] 抓取点 (" + grab.X + "," + grab.Y
                                + ") 最上层的不是靶窗（句柄 " + h.ToInt64().ToString("X")
                                + "，实际 " + Native.WindowFromPoint(grab).ToInt64().ToString("X")
                                + "）—— 先关掉全屏 / 置顶的其它窗口再跑基准，否则测的是别人。");
                            sampleStop.Set();
                            sampler.Join(500);
                            string pre = string.Join(Environment.NewLine, lines.ToArray());
                            string appDirX = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
                            try { File.WriteAllText(Path.Combine(appDirX, "dragbench.txt"), pre); } catch { }
                            foreach (var l in lines) Console.WriteLine(l);
                            Console.Out.Flush();
                            Environment.Exit(0);
                        }
                    }

                    coord.Trigger.SimulateKeyDown();
                    coord.Trigger.ForceHoldElapsed();

                    long dragMs = 0;
                    // 可用 --hz= N --ms= N --slow= N 覆盖；默认 125Hz × 2s × 健康目标
                    int hz = 125, dur = 2000, slowMs = 0, hangMs = 0, hangN = 1;
                    foreach (var a in Environment.GetCommandLineArgs())
                    {
                        if (a.StartsWith("--hz=")) int.TryParse(a.Substring(5), out hz);
                        if (a.StartsWith("--ms=")) int.TryParse(a.Substring(5), out dur);
                        if (a.StartsWith("--slow=")) int.TryParse(a.Substring(7), out slowMs);
                        if (a.StartsWith("--hang=")) int.TryParse(a.Substring(7), out hangMs);
                        if (a.StartsWith("--hangn=")) int.TryParse(a.Substring(8), out hangN);
                    }
                if (hz < 30) hz = 30;
                if (slowMs < 0) slowMs = 0;
                if (slowMs > 200) slowMs = 200;
                win.SlowMs = slowMs;     // 武装"慢速目标"（--slow=N，默认 0 = 健康目标）
                // --hang=N --hangn=K：前 K 帧睡 N 毫秒（N>250 即触发停顿保险），
                // 之后恢复健康 —— 验证"目标彻底卡死→放弃跟手→松手仍精确落位"整条链路。
                win.HangMs = hangMs;
                win.HangLeft = hangN < 1 ? 1 : hangN;
                int hangCfgN = win.HangLeft;   // 存档配置值：WndProc 会消耗 HangLeft，诊断行要显示"配了几帧"
                int steps = hz * dur / 1000;
                int intervalUs = 1000000 / hz;
                    // ★ 基准必须同步真实光标：End() 用 GetCursorPos 取"松手时光标
                    //   在哪"做最终定位，而旧版驱动只发合成钩子事件、不动真实光标 ——
                    //   松手瞬间窗口被拽到真实光标处（x=150 离群值、-162ms 假滞后
                    //   全是它污染的）。现在每个事件都同步真实光标，测完恢复原状。
                    Native.POINT realCursor;
                    Native.GetCursorPos(out realCursor);
                    var driverDone = new ManualResetEventSlim(false);
                    var driverThread = new Thread(delegate ()
                    {
                        var down = Ms(grab);
                        Native.SetCursorPos(grab.X, grab.Y);   // 按下即到位
                        coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                        dragStartMs = clock.ElapsedMilliseconds;   // 滞后分析的零点

                        int total = 600;                  // 总位移
                        // ★ 精确节拍：Thread.Sleep 受 15.6ms 定时器精度影响（基准路径
                        //   没调 timeBeginPeriod），会把 125Hz 驱动变成 64Hz ——
                        //   第一版测出的"55Hz 瓶颈"其实是驱动的假象。用混合等待消除。
                        try { Native.TimeBeginPeriod(1); } catch { }
                        var driver = Stopwatch.StartNew();
                        for (int i = 1; i <= steps; i++)
                        {
                            long targetUs = (long)i * intervalUs;   // 微秒级节拍
                            while (driver.ElapsedTicks * 1000000L / Stopwatch.Frequency < targetUs)
                                Thread.SpinWait(60);

                            var mv = Ms(new Native.POINT(grab.X + total * i / steps, grab.Y + total * i / 2 / steps));
                            Native.SetCursorPos(mv.pt.X, mv.pt.Y);   // 真实光标跟着合成事件走
                            evTimes.Add(clock.ElapsedMilliseconds);   // 真实投递时刻（共享时钟）
                            coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                        }

                        var up = Ms(new Native.POINT(grab.X + total, grab.Y + total / 2));
                        Native.SetCursorPos(up.pt.X, up.pt.Y);        // 松手点 = 最后一个事件点
                        evTimes.Add(clock.ElapsedMilliseconds);
                        coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                        dragMs = driver.ElapsedMilliseconds;
                        driverDone.Set();
                    });
                    driverThread.IsBackground = true;
                    driverThread.Start();

                    // 主线程泵消息，直到驱动结束 + 工作线程收尾
                    while (!driverDone.IsSet) { Application.DoEvents(); Thread.Sleep(1); }
                    Thread.Sleep(150);
                    for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(5); }

                    coord.Trigger.SimulateKeyUp(null);
                    sampleStop.Set();
                    sampler.Join(500);
                    Native.SetCursorPos(realCursor.X, realCursor.Y);   // 恢复用户光标

                    // ── 最终落位断言 ──
                    // End() 按"松手时光标位置 + 抓取偏移"落位。合成事件流下
                    // 松手点 = (grab + 600, grab + 300) → 窗口应精确落在
                    // 起点 + (600,300)。这条断言专防"松手弹回/差一截"类
                    // 收尾竞争（过期帧补写机制的正确性检查）。
                    Native.RECT fin;
                    Native.GetWindowRect(h, out fin);
                    int expX = r0.Left + 600, expY = r0.Top + 300;
                    bool landed = Math.Abs(fin.Left - expX) <= 2 && Math.Abs(fin.Top - expY) <= 2;
                    lines.Add(string.Format("最终落位：({0},{1}) 期望≈({2},{3}) {4}",
                        fin.Left, fin.Top, expX, expY,
                        landed ? "[PASS] 松手精确落位，无弹回" : "[FAIL] 落点偏差！"));

                    // ── 分析 ──
                    {
                        var rs = ringSamples;
                        int moves = 0;
                        for (int i2 = 1; i2 < rs.Count; i2++)
                            if (rs[i2].Item2 != rs[i2 - 1].Item2 || rs[i2].Item3 != rs[i2 - 1].Item3) moves++;
                        lines.Add("[diag] 环采样 " + rs.Count + " 次，位置变化 " + moves + " 次");
                        if (rs.Count > 0)
                            lines.Add("[diag] 环首/末位置 (" + rs[0].Item2 + "," + rs[0].Item3 + ") → ("
                                    + rs[rs.Count - 1].Item2 + "," + rs[rs.Count - 1].Item3 + ")");
                    }
                    // 视觉反馈（抗锯齿分层框）状态诊断：拖动期间它是否真的显示过
                    lines.Add("[diag] frame shown=" + coord.Drag.Frame.IsShown);
                    Native.RECT ringNow;
                    Native.GetWindowRect(coord.Drag.Frame.Handle, out ringNow);
                    lines.Add("[diag] frame rect=" + ringNow.Left + "," + ringNow.Top + ","
                            + ringNow.Right + "," + ringNow.Bottom);
                    List<Sample> pts;
                    lock (samples) pts = new List<Sample>(samples);

                    lines.Add("拖动时长 " + dragMs + " ms，驱动事件 " + steps + " 个（" + hz + "Hz），会话数 " + coord.Drag.SessionCount);
                    lines.Add("[diag] 最慢帧 _onMove=" + DragWorker.MaxMoveMs + " ms | 最长空转=" + DragWorker.MaxIdleMs
                        + " ms | 单帧最多合并=" + DragWorker.MaxDrained
                        + (slowMs > 0 ? " | 目标窗口每帧 sleep " + slowMs + " ms" : "")
                        + (hangMs > 0 ? " | 前 " + hangCfgN + " 帧 sleep " + hangMs + " ms（模拟彻底卡死）" : ""));
                    lines.Add("[帧写入] 写入=" + WindowActions.FramesWritten
                        + " 无需写=" + WindowActions.FramesNoop
                        + " 放弃跟手=" + WindowActions.FramesAbandoned
                        + " | 单帧同步写入 平均 "
                        + (WindowActions.FramesWritten > 0
                            ? (WindowActions.TotalWriteMs / WindowActions.FramesWritten).ToString("F1") : "?")
                        + " ms 最大 " + WindowActions.MaxWriteMs.ToString("F1") + " ms"
                        + " | 事件 " + steps + " 个 → 写入率 "
                        + (steps > 0 ? (WindowActions.FramesWritten * 100.0 / steps).ToString("F1") : "?") + "%");
                    lines.Add("窗口位置更新点 " + pts.Count + " 个 → 有效更新率 "
                        + (dragMs > 0 ? (pts.Count * 1000.0 / dragMs).ToString("F1") : "?") + " Hz");

                    if (pts.Count >= 3)
                    {
                        var gaps = new List<long>();
                        for (int i = 1; i < pts.Count; i++) gaps.Add(pts[i].T - pts[i - 1].T);
                        gaps.Sort();
                        double avg = 0; for (int i = 0; i < gaps.Count; i++) avg += gaps[i];
                        avg /= gaps.Count;
                        long p95 = gaps[(int)Math.Min(gaps.Count - 1, gaps.Count * 95 / 100)];
                        long max = gaps[gaps.Count - 1];

                        lines.Add(string.Format("更新间隔：平均 {0:F1} ms | P95 {1} ms | 最大 {2} ms",
                            avg, p95, max));
                        lines.Add("→ 最大间隔就是肉眼看到的『卡的那一下』；健康目标理想值 ≈ " + (1000 / hz)
                            + "ms（事件流节拍）" + (slowMs > 0
                                ? "；慢目标理想值 ≈ " + slowMs + "ms（目标每帧真实耗时，同步写按此节奏背压，不应再出现 122ms 级饿死尖峰）"
                                : ""));

                        // 事件到窗口的延迟：最后一点的时间 vs 驱动结束
                        lines.Add("最后一个更新点在拖动开始后 " + pts[pts.Count - 1].T + " ms（驱动共 " + dragMs + " ms）");

                        // ══ 跟手性（滞后）分析 ══
                        // "不跟手"必须变成数字：拖动中任意时刻，把窗口实际位置和
                        // "光标此刻对应的位置"相比，折算成毫秒。分段对比前 20% 与后 20%：
                        // 后者显著大于前者 = 窗口越拖越落后（队列堆积/饿死的特征）。
                        //
                        // ★ 期望位置按【驱动线程真实投递每个事件的时刻】反查 ★
                        //   T 时刻的理想位置 = 最后一个已投递事件对应的位置。
                        //   旧版用线性模型（匀速假设），驱动线程的调度抖动会直接污染
                        //   指标（健康目标也测出平均 -31.9ms、后段 -162ms 的假滞后）。
                        //   以"事件"为单位折算毫秒：落后一个事件 = 1000/hz ms，
                        //   这是事件驱动跟踪的固有下限，不是缺陷。
                        if (dragStartMs >= 0 && dragMs > 0 && evTimes.Count > 0)
                        {
                            double pxPerEv = 600.0 / steps;
                            double msPerEv = 1000.0 / hz;
                            double sumLag = 0, sumFirst = 0, sumLast = 0, maxLag = 0;
                            int n = 0, nFirst = 0, nLast = 0;
                            long cut = (long)(dragMs * 0.2);
                            int evIdx = 0;
                            // 离群值明细：最大滞后落在哪个采样点上（期望/实际/事件号）
                            var worst = new List<Tuple<double, long, int, double, int>>();
                            foreach (var p in pts)
                            {
                                double el = p.T - dragStartMs;
                                if (el < 0) continue;
                                if (el > dragMs) break;               // pts 按时间有序
                                while (evIdx < evTimes.Count && evTimes[evIdx] - dragStartMs <= el) evIdx++;
                                double expect = r0.Left + pxPerEv * Math.Min(evIdx, steps);
                                double lagMs = (expect - p.X) / pxPerEv * msPerEv;
                                if (lagMs > maxLag) maxLag = lagMs;
                                worst.Add(Tuple.Create(lagMs, p.T, p.X, expect, evIdx));
                                sumLag += lagMs; n++;
                                if (el <= cut) { sumFirst += lagMs; nFirst++; }
                                else if (el >= dragMs - cut) { sumLast += lagMs; nLast++; }
                            }
                            worst.Sort((a, b) => b.Item1.CompareTo(a.Item1));
                            for (int wi = 0; wi < Math.Min(3, worst.Count); wi++)
                            {
                                var w = worst[wi];
                                if (w.Item1 > 50)
                                    lines.Add(string.Format("  [离群] 滞后 {0:F0} ms @ 采样T={1}ms 实际x={2} 期望x={3:F0} 已投递事件={4}",
                                        w.Item1, w.Item2, w.Item3, w.Item4, w.Item5));
                            }
                            lines.Add(string.Format(
                                "跟手性：平均落后 {0:F1} ms | 最大 {1:F0} ms | 前20%段 {2:F1} ms → 后20%段 {3:F1} ms"
                                + "（后段 ≫ 前段 = 越拖越落后；1 个事件 = {4:F1} ms，是固有下限）",
                                n > 0 ? sumLag / n : 0, maxLag,
                                nFirst > 0 ? sumFirst / nFirst : 0, nLast > 0 ? sumLast / nLast : 0, msPerEv));
                        }

                        // 轨迹还原：每 100ms 打一个点，肉眼看是否线性
                        // （稀疏数据下同一个采样点会被连续多个 100ms 步命中，
                        //   去重后只打一次，避免"卡死期"刷屏）
                        lines.Add("轨迹抽样（每 100ms）：");
                        int si = 0;
                        long lastPrintedT = long.MinValue;
                        for (long t = 0; t <= dragMs; t += 100)
                        {
                            while (si < pts.Count && pts[si].T < t) si++;
                            if (si < pts.Count && pts[si].T != lastPrintedT)
                            {
                                lines.Add(string.Format("  t={0,5}ms  x={1,5} y={2,5}", pts[si].T, pts[si].X, pts[si].Y));
                                lastPrintedT = pts[si].T;
                            }
                        }

                        // 环的轨迹抽样：拖动环必须跟着窗口走（它慢一帧可以，停住不行）
                        lines.Add("环轨迹抽样（每 200ms）：");
                        int ri = 0;
                        for (long t = 0; t <= dragMs; t += 200)
                        {
                            while (ri < ringSamples.Count && ringSamples[ri].Item1 < t) ri++;
                            if (ri < ringSamples.Count)
                                lines.Add(string.Format("  t={0,5}ms  ring=({1,5},{2,5})",
                                    ringSamples[ri].Item1, ringSamples[ri].Item2, ringSamples[ri].Item3));
                        }
                    }
                    else
                    {
                        lines.Add("!! 采样点过少，链路可能没跑通");
                    }

                    coord.Dispose();
                }
            }
            catch (Exception ex)
            {
                lines.Add("EXC " + ex);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            string text = string.Join(Environment.NewLine, lines.ToArray());
            string appDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
            try { File.WriteAllText(Path.Combine(appDir, "dragbench.txt"), text); } catch { }
            foreach (var l in lines) Console.WriteLine(l);
            try { Console.Out.Flush(); } catch { }
            Environment.Exit(0);
        }

        private static Native.MSLLHOOKSTRUCT Ms(Native.POINT p)
        {
            var ms = new Native.MSLLHOOKSTRUCT();
            ms.pt = p;
            return ms;
        }

        /// <summary>
        /// 慢速目标窗口：WndProc 在 WM_WINDOWPOSCHANGING 上 sleep，模拟
        /// "处理一帧要 N 毫秒"的重型应用（浏览器 / 资源管理器 / Electron 应用
        /// 都落在这个量级：10~30ms 一帧是常态）。
        ///
        /// HangMs/HangLeft：前 HangLeft 帧睡 HangMs 毫秒（>250ms 即触发
        /// HoldDrag 的"停顿保险"），之后恢复健康 —— 用来验证目标彻底卡死时
        /// "放弃跟手但松手仍精确落位"的链路。
        ///
        /// 为什么基准必须有它：此前 dragbench 的靶子永远是"自己进程里响应最快
        /// 的窗口"，于是任何针对慢目标的缺陷都测不出来 —— 第十九版的回执机制正是
        /// 这样漏过验证的：在健康目标上 1:1 跟手，到用户真实环境的重型窗口上
        /// 直接饿死成 1/8 事件率，表现为"越拖越卡、不跟手"。
        /// </summary>
        private sealed class SlowTargetForm : Form
        {
            public int SlowMs;
            public int HangMs;        // >0 时前 HangLeft 帧睡这么久（模拟彻底卡死）
            public int HangLeft;

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_WINDOWPOSCHANGING)
                {
                    if (HangLeft > 0) { HangLeft--; if (HangMs > 0) Thread.Sleep(HangMs); }
                    else if (SlowMs > 0) Thread.Sleep(SlowMs);
                }
                base.WndProc(ref m);
            }
        }
    }
}
