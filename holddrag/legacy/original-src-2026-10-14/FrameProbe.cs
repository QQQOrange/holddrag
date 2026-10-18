using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace HoldDrag
{
    /// <summary>
    /// 帧写入物理探针（--probe）。
    ///
    /// 为什么需要它：第十九版把"每帧同步写"改成了"每帧异步投递 + GetWindowRect 回执"，
    /// 用户实测【越拖越卡、不跟手】。争论"异步投递后 GetWindowRect 会不会立即反映新位置"
    /// 是没有意义的 —— 文档没写清楚，代码推理各说各话。这个探针把三个物理事实直接测出来：
    ///
    ///   T1 异步投递后，GetWindowRect 多久反映新位置？
    ///       若 ≈ 0ms：回执机制永远判定"已消化"，等于裸异步 —— 请求堆进目标队列，
    ///                窗口追着一串过期位置跑 = "越拖越落后"。
    ///       若 ≈ 目标处理耗时：回执有效，但目标慢于事件流时会【跳过】帧
    ///                 （最坏 1/8 事件率在写窗口）= "不跟手"。
    ///        两种都坏，但坏法完全不同，修法也就不同 —— 所以必须先测。
    ///
    ///   T2 同步写在慢目标上阻塞多久？（= 目标的真实处理耗时，也是背压的天然节奏）
    ///
    ///   T3 往慢目标连投 10 个异步帧：窗口是"平滑滑过所有中间位置"（追过期位置），
    ///        还是直接到最新？（前者就是队列堆积的铁证）
    ///
    ///   T4 连做 10 个同步写：总耗时与落位精确性（对照 T3）。
    ///
    /// 目标窗口是一个【裸 Win32 窗口】，WndProc 里只在 WM_WINDOWPOSCHANGING 上
    ///  sleep --slow 毫秒（默认 20ms），模拟"一帧要处理 20ms"的重型应用
    /// （浏览器、资源管理器、Electron 应用都落在这个量级）。
    /// </summary>
    internal static class FrameProbe
    {
        private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const uint WS_VISIBLE = 0x10000000;

        private static IntPtr _hwnd;
        private static int _slowMs = 20;
        private static volatile bool _armed;
        // 委托必须存字段：否则被 GC 回收后非托管侧留野指针，直接崩进程
        private static Native.WndProc _proc;

        public static void Run()
        {
            foreach (var a in Environment.GetCommandLineArgs())
            {
                if (a.StartsWith("--slow=")) int.TryParse(a.Substring(7), out _slowMs);
            }
            if (_slowMs < 0) _slowMs = 0;
            if (_slowMs > 200) _slowMs = 200;

            try { Native.TimeBeginPeriod(1); } catch { }

            var lines = new List<string>();
            lines.Add("═══ HoldDrag 帧写入物理探针 ═══");
            lines.Add("目标窗口每帧处理耗时（WM_WINDOWPOSCHANGING 内 sleep）：" + _slowMs + " ms");

            _proc = OnWndProc;
            var wc = new Native.WNDCLASSEX();
            wc.cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX));
            wc.lpfnWndProc = _proc;
            wc.hInstance = Native.GetModuleHandleW(null);
            wc.lpszClassName = "HoldDrag.Probe.Target";
            wc.hbrBackground = new IntPtr(2);      // COLOR_BACKGROUND 画刷，无所谓，能看见就行
            ushort atom = Native.RegisterClassExW(ref wc);
            if (atom == 0)
            {
                lines.Add("RegisterClassExW 失败，Win32 错误码 " + Marshal.GetLastWin32Error());
                Finish(lines);
                return;
            }

            _hwnd = Native.CreateWindowExW(0, "HoldDrag.Probe.Target", "HoldDrag 探针目标（慢速窗口）",
                WS_OVERLAPPEDWINDOW | WS_VISIBLE, 420, 300, 420, 320,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                lines.Add("CreateWindowExW 失败，Win32 错误码 " + Marshal.GetLastWin32Error());
                Finish(lines);
                return;
            }

            var done = new ManualResetEventSlim(false);
            var worker = new Thread(delegate ()
            {
                try { RunTests(lines); }
                catch (Exception ex) { lines.Add("探针异常: " + ex); }
                finally { done.Set(); }
            });
            worker.IsBackground = true;
            worker.Name = "HoldDrag.FrameProbe.Writer";
            worker.Start();

            // 主线程专职泵目标窗口的消息（它就是"目标程序的线程"）
            while (!done.IsSet)
            {
                Native.MSG msg;
                while (Native.PeekMessageW(out msg, IntPtr.Zero, 0, 0, Native.PM_REMOVE))
                {
                    if (msg.message == Native.WM_QUIT) { done.Set(); break; }
                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessageW(ref msg);
                }
                Thread.Sleep(1);
            }

            try { Native.DestroyWindow(_hwnd); } catch { }
            Finish(lines);
        }

        private static void Finish(List<string> lines)
        {
            string text = string.Join(Environment.NewLine, lines.ToArray());
            string appDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
            try { File.WriteAllText(Path.Combine(appDir, "probe.txt"), text); } catch { }
            foreach (var l in lines) Console.WriteLine(l);
            try { Console.Out.Flush(); } catch { }
            try { Native.TimeEndPeriod(1); } catch { }
            Environment.Exit(0);
        }

        // ── 目标窗口的 WndProc：只在 WM_WINDOWPOSCHANGING 上装睡 ──
        private static IntPtr OnWndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (_armed && msg == Native.WM_WINDOWPOSCHANGING && _slowMs > 0)
                Thread.Sleep(_slowMs);
            return Native.DefWindowProcW(h, msg, w, l);
        }

        private static void RunTests(List<string> lines)
        {
            // 等窗口把创建消息消化完，再武装"慢速"
            Thread.Sleep(120);
            _armed = true;

            Native.RECT r;
            Native.GetWindowRect(_hwnd, out r);
            lines.Add("目标窗口初始矩形: " + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height);
            lines.Add("");

            // ══ T1：异步投递后，GetWindowRect 多久反映新位置？ ══
            {
                MoveSync(600, 420); Thread.Sleep(80);   // 基线 + 彻底落定

                int tx = 760, ty = 500;
                var sw = Stopwatch.StartNew();
                Native.SetWindowPos(_hwnd, IntPtr.Zero, tx, ty, 0, 0,
                    Native.MOVETHICKBORDERS | Native.SWP_ASYNCWINDOWPOS);
                long postMs = sw.ElapsedTicks * 1000L / Stopwatch.Frequency;

                // 自旋轮询：拿到"位置第一次等于目标"的时刻
                long reflectUs = -1;
                var trajectory = new List<KeyValuePair<long, int>>();
                int lastX = int.MinValue;
                while (true)
                {
                    long us = sw.ElapsedTicks * 1000000L / Stopwatch.Frequency;
                    if (us > 600000) break;                     // 600ms 上限
                    if (Native.GetWindowRect(_hwnd, out r))
                    {
                        if (r.Left == tx && r.Top == ty) { reflectUs = us; break; }
                        if (r.Left != lastX)
                        {
                            lastX = r.Left;
                            trajectory.Add(new KeyValuePair<long, int>(us / 1000, r.Left));
                        }
                    }
                    Thread.SpinWait(120);
                }
                lines.Add("T1 异步投递 → GetWindowRect 反映");
                lines.Add("   SetWindowPos(ASYNC) 自身耗时 = " + postMs + " ms（应为 0：立即返回）");
                if (reflectUs < 0)
                    lines.Add("   600ms 内位置从未变成目标 ←★ 回执机制在本类目标上完全失效");
                else
                    lines.Add("   位置第一次等于目标 = 投递后 " + (reflectUs / 1000.0).ToString("F1")
                        + " ms（目标每帧 " + _slowMs + " ms）");
                lines.Add("   轮询中观察到的中间 x 序列: " + string.Join(" → ",
                    trajectory.ConvertAll(p => p.Value + "@" + p.Key + "ms").ToArray()));
                lines.Add("   ⇒ 若反映耗时≈0：回执永远说\"已消化\"，每事件必投 = 裸异步（队列堆积）");
                lines.Add("   ⇒ 若反映耗时≈" + _slowMs + "ms：回执有效，但慢于事件流时会饿死帧");
            }

            Thread.Sleep(150);

            // ═══ T2：同步写阻塞多久 ═══
            {
                MoveSync(900, 540); Thread.Sleep(80);
                int tx = 1040, ty = 600;
                var sw = Stopwatch.StartNew();
                Native.SetWindowPos(_hwnd, IntPtr.Zero, tx, ty, 0, 0, Native.MOVETHICKBORDERS);
                long ms = sw.ElapsedTicks * 1000L / Stopwatch.Frequency;
                Native.GetWindowRect(_hwnd, out r);
                lines.Add("");
                lines.Add("T2 同步写（工作线程阻塞时长）");
                lines.Add("   SetWindowPos(同步) 耗时 = " + ms + " ms（目标每帧 " + _slowMs + " ms）");
                lines.Add("   返回后 GetWindowRect = " + r.Left + "," + r.Top
                    + (r.Left == tx && r.Top == ty ? "  ✓ 已精确落位" : "  ✗ 未落位"));
                lines.Add("   ⇒ 阻塞时长 = 目标的真实处理耗时；这就是背压的天然节奏");
            }

            Thread.Sleep(150);

            // ═══ T3：连投 10 个异步帧（模拟事件流快于目标） ═══
            {
                MoveSync(1200, 660); Thread.Sleep(80);
                int x0 = 1200;
                var sw = Stopwatch.StartNew();
                for (int i = 1; i <= 10; i++)
                {
                    Native.SetWindowPos(_hwnd, IntPtr.Zero, x0 + i * 10, 660, 0, 0,
                        Native.MOVETHICKBORDERS | Native.SWP_ASYNCWINDOWPOS);
                    Thread.Sleep(1);                       // 1ms 一个事件（=1000Hz 事件流）
                }
                long postTotalMs = sw.ElapsedTicks * 1000L / Stopwatch.Frequency;

                // 投完后持续观察窗口轨迹
                var traj = new List<KeyValuePair<long, int>>();
                int lastX = int.MinValue;
                long settleMs = -1;
                int finalX = x0 + 100;
                while (true)
                {
                    long ms = sw.ElapsedTicks * 1000L / Stopwatch.Frequency;
                    if (ms > 3000) break;
                    if (Native.GetWindowRect(_hwnd, out r))
                    {
                        if (r.Left != lastX)
                        {
                            lastX = r.Left;
                            traj.Add(new KeyValuePair<long, int>(ms, r.Left));
                        }
                        if (settleMs < 0 && r.Left == finalX) settleMs = ms;
                    }
                    Thread.Sleep(1);
                }
                lines.Add("");
                lines.Add("T3 连投 10 个异步帧（1ms 一个，最后位置 x=" + finalX + "）");
                lines.Add("   10 次投递总耗时 = " + postTotalMs + " ms");
                lines.Add("   投递结束时窗口在哪: x=" + traj.Count + " 个变化点中的第 0 个 = "
                    + (traj.Count > 0 ? traj[0].Value.ToString() : "?"));
                lines.Add("   全部落定耗时 = " + (settleMs >= 0 ? settleMs + " ms" : "3s 内未落定")
                    + "（目标每帧 " + _slowMs + " ms × 10 帧 ≈ " + (_slowMs * 10) + " ms）");
                lines.Add("   窗口轨迹（变化点）: " + string.Join(" → ",
                    traj.ConvertAll(p => p.Value + "@" + p.Key + "ms").ToArray()));
                lines.Add("   ⇒ 窗口是否【滑过】了 10 个中间位置？是 = 追过期位置（队列堆积铁证）");
            }

            Thread.Sleep(150);

            // ═══ T4：连做 10 个同步写（对照） ═══
            {
                MoveSync(1200, 700); Thread.Sleep(80);
                var sw = Stopwatch.StartNew();
                for (int i = 1; i <= 10; i++)
                {
                    Native.SetWindowPos(_hwnd, IntPtr.Zero, 1200 + i * 10, 700, 0, 0, Native.MOVETHICKBORDERS);
                }
                long totalMs = sw.ElapsedTicks * 1000L / Stopwatch.Frequency;
                Native.GetWindowRect(_hwnd, out r);
                lines.Add("");
                lines.Add("T4 连做 10 个同步写（对照 T3）");
                lines.Add("   总耗时 = " + totalMs + " ms（≈ 目标每帧 " + _slowMs + " × 10）");
                lines.Add("   结束后位置 = " + r.Left + "," + r.Top
                    + (r.Left == 1300 && r.Top == 700 ? "  ✓ 精确等于最后一次写入" : "  ✗ 不符"));
                lines.Add("   ⇒ 每次写完窗口都精确在写入位，队列里永远只有一帧，不存在追过期位置");
            }

            Thread.Sleep(100);
        }

        private static void MoveSync(int x, int y)
        {
            Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, Native.MOVETHICKBORDERS);
        }
    }
}
