using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace HoldDrag
{
    /// <summary>
    /// 句柄 / 内存泄漏压力测试。运行：HoldDrag.exe --stress
    ///
    /// 为什么必须有这一条：
    ///   本程序是长期驻留的托盘程序，一次拖动只做几毫秒的事，看起来"泄漏一点也没关系"。
    ///   但用户每天要拖几百次：
    ///     · 漏一个 GDI 对象/次，三天就把进程的 GDI 句柄池（默认 10000）耗光，
    ///       到那时整个程序连菜单都画不出来，而崩溃点是别的地方 —— 极难排查；
    ///     · 同理，每次拖动如果留下一个窗口、一个区域、一个位图，
    ///       用户感受到的就是"多滑几下就越来越卡"。
    ///
    /// 所以这里连续跑几百次真实的拖动会话（移动 + 缩放交替），
    /// 每隔一段采样 GDI 对象数 / USER 对象数 / 系统句柄数 / 私有字节，
    /// 用【实测数据】断言它没有单调增长，而不是嘴上说"应该没问题"。
    ///
    /// 注意点：
    ///   · 前若干次先当"预热"丢掉 —— JIT、窗体首次绘制、浮层首次建劣质容器
    ///     都会一次性涨一点，那不是泄漏；
    ///   · 每次采样前先强制一次完整 GC，否则被回收还没跑的垃圾会被误算成泄漏。
    /// </summary>
    internal static class StressTest
    {
        private struct Snap
        {
            public uint Handles;
            public uint Gdi;
            public uint User;
            public long PrivateMb;
            public long WorkingMb;

            public string Text()
            {
                return "句柄=" + Handles + " GDI=" + Gdi + " USER=" + User
                     + " 私有=" + PrivateMb + "MB 工作集=" + WorkingMb + "MB";
            }
        }

        private const int WarmupDrags = 40;
        private const int TotalDrags = 320;
        private const int SampleEvery = 40;

        private static readonly StringBuilder Buf = new StringBuilder();
        private static int _fail;

        public static void Run(string dir)
        {
            Line("==== 长按拖窗 泄漏压力测试 ====");
            Line("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line("拖动会话：预热 " + WarmupDrags + " 次，正式 " + TotalDrags + " 次，每 " + SampleEvery + " 次采样一次");
            Line("");

            StartWatchdog(dir);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }

            if (SystemInformation.VirtualScreen.Width < 400)
            {
                Line("当前显示区域过小，跳过。");
                Finish(dir);
                return;
            }

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            WindowActions.ForcedTargetForTest = IntPtr.Zero;

            try
            {
                using (var f = new Fixture())
                {
                    WindowActions.ForcedTargetForTest = f.H;

                    DoDrag(f, 0, false);                       // 先跑一次，确保整条链路是通的
                    Line("链路自检：一次完整拖动会话跑通");

                    for (int i = 0; i < WarmupDrags; i++)
                    {
                        DoDrag(f, i, false);
                        DoDrag(f, i, true);
                    }
                    Collect();
                    var baseSnap = Sample();
                    Line("预热完成，基准：" + baseSnap.Text());
                    Line("");

                    long maxDHandles = 0, maxDGdi = 0, maxDUser = 0, maxDPriv = 0;
                    for (int round = 1; round * SampleEvery <= TotalDrags; round++)
                    {
                        for (int k = 0; k < SampleEvery; k++)
                        {
                            int n = round * SampleEvery + k;
                            DoDrag(f, n, false);
                            DoDrag(f, n, true);
                        }
                        Collect();
                        var s = Sample();
                        Line("第 " + (round * SampleEvery * 2) + " 次拖动后：" + s.Text());

                        maxDHandles = Math.Max(maxDHandles, (long)s.Handles - (long)baseSnap.Handles);
                        maxDGdi = Math.Max(maxDGdi, (long)s.Gdi - (long)baseSnap.Gdi);
                        maxDUser = Math.Max(maxDUser, (long)s.User - (long)baseSnap.User);
                        maxDPriv = Math.Max(maxDPriv, s.PrivateMb - baseSnap.PrivateMb);
                    }

                    // ★ 内存不能用"过程中的峰值"判定，必须看"收干净之后的底"。
                    //
                    //   原因：.NET 的托管堆是按代分段的，gen2 段一旦涨上去，
                    //   即使对象都回收了，进程私有字节也不会立刻掉回来 ——
                    //   要等下一次 GC 决定压缩。压测这种密集分配场景，
                    //   中途抓到 120MB+ 的瞬时值纯属正常，
                    //   而同一次运行里下一轮又掉回 64MB，一看就知道不是泄漏。
                    //
                    //   真正的泄漏特征只有一个：**收干净之后底还在往上抬**，
                    //   也就是最后几次采样的最低值明显高于基准。
                    //   所以这里用"末尾采样最小值 - 基准"来判定，
                    //   并且先做一次最强力的回收（含 LOH 压缩）把浮动的部分压掉。
                    // ★ 单次采样会踩到 GC 锯齿的"高点"，判定就会随机失败。
                    //   这里连做几轮"回收 → 采样"，取【最低值】当"底"。
                    //   理由：底 = 无论怎么浮动都能回落到的那个值。
                    //   真正的泄漏会让每一轮都回不去，最小值照样往上抬；
                    //   而锯齿只会让某些轮偏高，最小值始终稳。
//   实测同一份代码连续跑：单次采样在 -52MB / +77MB 之间跳，
                //   取最小值后稳定在 +0 ~ +3MB。
                    //
                    // ★ 但 3 轮还是不够（踩过）：GC 把页归还给操作系统有延迟，
                    //   有时候连着 3 轮都落在"还没还完"的窗口里，
                    //   于是同一份代码两次跑分别报 +1MB 和 +61MB —— 随机假红。
                    //   这里改成 8 轮，并且每轮回收后多等一会儿，
                    //   让"底"真的是底。
                    long floorDelta = long.MaxValue;
                    Snap best = default(Snap);
                    for (int k = 0; k < 8; k++)
                    {
                        CollectFull();
                        var snapK = Sample();
                        long dK = snapK.PrivateMb - baseSnap.PrivateMb;
                        if (dK < floorDelta) { floorDelta = dK; best = snapK; }
                    }
                    var finalSnap = best;

                    Line("");
                    Line("收干净后（含 LOH 压缩，取 8 轮最低值）：" + finalSnap.Text()
                       + "，相对基准 " + (floorDelta >= 0 ? "+" : "") + floorDelta + " MB");

                    Line("");
                    Check(maxDGdi <= 80, "GDI 对象没有随拖动次数增长", "峰值增量 " + maxDGdi);
                    Check(maxDUser <= 60, "USER 对象没有随拖动次数增长", "峰值增量 " + maxDUser);
                    Check(maxDHandles <= 120, "系统句柄没有随拖动次数增长", "峰值增量 " + maxDHandles);
                    Check(floorDelta <= 25, "私有内存没有随拖动次数增长（看收干净后的底）",
                          "底增量 " + floorDelta + " MB（过程中峰值 +" + maxDPriv + " MB，属托管堆正常浮动）");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _fail++;
                Line("[FAIL] 压力测试自身抛异常 :: " + ex.Message);
            }
            finally
            {
                WindowActions.ForcedTargetForTest = IntPtr.Zero;
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }

            Line("");
            Line(_fail == 0 ? "结论：未发现泄漏迹象" : "结论：存在可疑增长，见上面 [FAIL] 行");
            Finish(dir);
        }

        private static string _reportDir;

        /// <summary>
        /// 总看门狗：压测跑超过 240 秒就把已有结果写成报告并退出。
        /// 压测里每轮都在动真实窗口，万一某个 P/Invoke 卡住，没有它就会永远挂着、
        /// 报告一个字都写不出来 —— 那就等于没有测。
        /// </summary>
        private static void StartWatchdog(string dir)
        {
            _reportDir = dir;
            try
            {
                var t = new System.Threading.Thread(delegate ()
                {
                    System.Threading.Thread.Sleep(240000);
                    try
                    {
                        Buf.Append("\r\n\r\n!! 压测总超时（240 秒），报告到此为止。\r\n");
                        File.WriteAllText(Path.Combine(_reportDir, "stresstest.txt"),
                            Buf.ToString(), Encoding.UTF8);
                    }
                    catch { }
                    Environment.Exit(2);
                });
                t.IsBackground = true;
                t.Name = "HoldDrag.StressWatchdog";
                t.Start();
            }
            catch { }
        }

        // ── 驱动 ────────────────────────────────────────────────

        private static bool DoDrag(Fixture f, int n, bool resize)
        {
            Native.RECT r;
            Native.GetWindowRect(f.H, out r);

            Native.POINT grab = resize
                ? new Native.POINT(r.Right - 10, r.Bottom - 10)     // 右下角 → 双向缩放
                : new Native.POINT(r.Left + r.Width / 2, r.Top + r.Height / 2);

            f.Coord.Trigger.SimulateKeyDown();
            f.Coord.Trigger.ForceHoldElapsed();

            var down = Ms(grab);
            f.Coord.OnMouse(resize ? Native.WM_RBUTTONDOWN : Native.WM_LBUTTONDOWN, ref down);

            for (int k = 1; k <= 8; k++)
            {
                int sx = (n % 2 == 0 ? k : -k) * 5;
                int sy = ((n + k) % 2 == 0 ? k : -k) * 4;
                var mv = Ms(new Native.POINT(grab.X + sx, grab.Y + sy));
                f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                Application.DoEvents();
            }

            var up = Ms(new Native.POINT(grab.X + 6, grab.Y + 6));
            f.Coord.OnMouse(resize ? Native.WM_RBUTTONUP : Native.WM_LBUTTONUP, ref up);

            Application.DoEvents();
            // 松开触发键：不做的话下一次 SimulateKeyDown 会因为 _held=true 直接返回，会话根本起不来
            f.Coord.Trigger.SimulateKeyUp(null);
            Application.DoEvents();
            return true;
        }

        private static Native.MSLLHOOKSTRUCT Ms(Native.POINT p)
        {
            var ms = new Native.MSLLHOOKSTRUCT();
            ms.pt = p;
            return ms;
        }

        // ── 采样 ────────────────────────────────────────────────

        private static void Collect()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch { }
        }

        /// <summary>
        /// 最强力的一次回收：连大对象堆（LOH）一起压缩，
        /// 把"暂时占着但已经没用"的内存彻底还回去。
        ///
        /// 用它测出来的"底"才是判断泄漏的依据 —— 见 Run() 里那段说明。
        /// </summary>
        private static void CollectFull()
        {
            try
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                    System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;

                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced,
                           blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced,
                           blocking: true, compacting: true);

// 交给系统一点时间把归还的页真正反映到进程计数上。
      // ★ 300ms 不够：踩过"连做 3 轮都落在还没还完的窗口里"的坑，
              //   页从进程私有内存里真正消失要更久，这里给 600ms。
    System.Threading.Thread.Sleep(600);
            }
            catch { }
        }

        private static Process _proc;

        private static Snap Sample()
        {
            var s = new Snap();
            try
            {
                IntPtr hp = Native.GetCurrentProcess();
                s.Gdi = Native.GetGuiResources(hp, Native.GR_GDIOBJECTS);
                s.User = Native.GetGuiResources(hp, Native.GR_USEROBJECTS);

                uint h;
                s.Handles = Native.GetProcessHandleCount(hp, out h) ? h : 0;

                if (_proc == null) _proc = Process.GetCurrentProcess();
                _proc.Refresh();
                s.PrivateMb = _proc.PrivateMemorySize64 / (1024 * 1024);
                s.WorkingMb = _proc.WorkingSet64 / (1024 * 1024);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            return s;
        }

        // ── 测试窗口 ────────────────────────────────────────────

        private sealed class Fixture : IDisposable
        {
            private readonly string _dir;
            public readonly Form Window;
            public readonly ConfigStore Store;
            public readonly Coordinator Coord;

            public IntPtr H { get { return Window.Handle; } }

            public Fixture()
            {
                _dir = Path.Combine(Path.GetTempPath(), "holddrag-stress-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);

                Store = new ConfigStore(_dir);
                Store.Load();

                var c = Store.Current;
                c.MovePreviewMode = PreviewModes.FullWin; // 压测要压"真实写窗口"路径
                c.ShowActivationFrame = true;      // 外框每帧都在动，这是最容易漏 GDI 对象的地方
                c.AllowFullscreenWindows = true;
                c.BringToFront = true;
                // 把帧预算压到最小，否则几百次拖动会因为节流跑上好几分钟
                c.DragRefreshMode = RefreshModes.Custom;
                c.DragRefreshHz = 500;

                Window = new Form();
                Window.FormBorderStyle = FormBorderStyle.Sizable;
                Window.StartPosition = FormStartPosition.Manual;
                Window.ShowInTaskbar = false;
                Window.Text = "HoldDrag stress";
                Window.TopMost = true;
                Window.Bounds = new Rectangle(140, 140, 340, 240);
                Window.Show();
                Application.DoEvents();

                Coord = new Coordinator(Store);
                Coord.PrecreateOverlays();
                Application.DoEvents();
            }

            public void Dispose()
            {
                try { Coord.Dispose(); } catch { }
                try { Window.Close(); Window.Dispose(); } catch { }
                try { Directory.Delete(_dir, true); } catch { }
            }
        }

        // ── 输出 ────────────────────────────────────────────────

        private static void Check(bool ok, string name, string detail = null)
        {
            if (ok) { Line("  [PASS] " + name + (detail != null ? "（" + detail + "）" : "")); }
            else { _fail++; Line("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : " :: " + detail)); }
        }

        private static void Line(string s)
        {
            Buf.Append(s);
            Buf.Append("\r\n");
        }

        private static void Finish(string dir)
        {
            // 关掉常驻日志句柄再统计/退出，免得它被算进句柄基线。
            Log.Shutdown();

            string text = Buf.ToString();
            try { File.WriteAllText(Path.Combine(dir, "stresstest.txt"), text, Encoding.UTF8); } catch { }

            try
            {
                var t = new System.Threading.Thread(delegate ()
                {
                    try { Console.Out.Write(text); Console.Out.Flush(); } catch { }
                });
                t.IsBackground = true;
                t.Name = "HoldDrag.StressEcho";
                t.Start();
                t.Join(1500);
            }
            catch { }

            HardExit(_fail == 0 ? 0 : 1);
        }

        /// <summary>
        /// 确定性退出。
        ///
        /// Environment.Exit 理论上就够了，但实测出现过：报告明明写完了、进程却一直不退出。
        /// 压测这一路建过真实窗口、动过前台、起过工作线程，任何一个卡住都可能拖住退出流程。
        /// 所以再挂一个 3 秒后自杀的兜底线程 —— 诊断入口绝不能"跑完还挂着"，
        /// 那会让自动化验证全部卡死，看起来就像程序坏了。
        /// </summary>
        internal static void HardExit(int code)
        {
            Environment.ExitCode = code;

            // 报告已经落盘，先把该冲的冲掉。
            try { Console.Out.Flush(); } catch { }
            try { Log.Info("[诊断] 结束，退出码 " + code); } catch { }

            // ★ 直接 TerminateProcess（并带上退出码），不再指望 Environment.Exit。
            //
            // 之前挂一个"3 秒后自杀"的兜底线程是不够的，实测进程照样一直挂着。
            // 原因是 Environment.Exit 在真正 ExitProcess 之前会【挂起其它线程】，
            // 那个兜底线程根本等不到被调度；而它自己要 flush 控制台缓冲、
            // 跑 ProcessExit 处理器，这条路径上任何一处卡住，进程就再也出不去。
            // 诊断入口必须"跑完就死"，否则所有自动化验证都会卡死，
            // 所以这里用最硬的方式收尾，并且用 TerminateProcess 保住退出码的语义。
            try { Native.TerminateProcess(Native.GetCurrentProcess(), (uint)code); } catch { }

            Environment.Exit(code);      // 万一 TerminateProcess 失败，再退回正常路径
        }
    }
}
