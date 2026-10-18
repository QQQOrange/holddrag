using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag
{
    internal static class Program
    {
        private const string MutexName = @"Local\HoldDrag.SingleInstance.v1";
        private const string ShowEventName = @"Local\HoldDrag.ShowSettings.v1";

        public const string ArgNoElevate = "--no-elevate";
        public const string ArgSelfTest = "--selftest";
        public const string ArgSmoke = "--smoke";
        public const string ArgTrace = "--trace";
        public const string ArgStress = "--stress";

        /// <summary>冒烟模式：正常启动全套组件，数秒后自动打开设置窗口并退出，用于自动验证。</summary>
        public static bool SmokeMode { get; private set; }

        /// <summary>供托盘菜单判断是否需要显示"以管理员身份重启"。</summary>
        public static bool LaunchedWithoutElevation { get; private set; }

        [STAThread]
        private static void Main(string[] args)
        {
            // ══════ ★★★ 必须是整个程序的第一条语句 ★★★ ══════
            //
            // DPI 感知必须在【任何窗口被创建之前】确定，一旦确定就再也改不了。
            //
            // 清单（app.manifest）里虽然已经声明了 PerMonitorV2，但实测发现
            // 同一个 exe 在不同目录运行会得到不同的感知级别 —— 清单可能被
            // 兼容性设置 / 组策略 / 旧系统覆盖掉。所以这里再显式设一次作为保险。
            //
            // 为什么这么要紧：本程序全程使用物理像素坐标。
            // 进程一旦是"DPI 不感知"的，系统会把传给 API 的坐标再缩放一次：
            //   屏幕 3840x2160 @200%，进程以为只有 1920x1080，
            //   钩子给的 (2182,1440) 被再放大到 (4364,2880) → 跑到屏幕外
            //   → WindowFromPoint 返回空 → FindTarget 返回 0
            //   → 长按触发键后左键右键完全没反应。整个核心功能失效。
            bool dpiCtxSet = false;
            string dpiSetErr = "";
            int dpiSetErrCode = 0;
            try
            {
                dpiCtxSet = Native.SetProcessDpiAwarenessContext(
                    Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                dpiSetErrCode = Marshal.GetLastWin32Error();
            }
            catch (Exception dpiEx)
            {
                // 设不上不影响运行：清单还在兜着，CoordSpace 还有第三层兜底
                dpiSetErr = dpiEx.GetType().Name + ": " + dpiEx.Message;
            }

            // ★ DPI 现状必须立刻核实：清单理论上兜底，但实测出现过
            //   "清单在、SetProcessDpiAwarenessContext 失败、感知级别=0" 的组合。
            //   如果真是 0（不感知），用 SetProcessDpiAware（系统感知）抢一次 ——
            //   系统感知虽然不完美（混合 DPI 多屏会虚化），但主屏 200% 下是 1:1 的，
            //   坐标不再被量化到 2px，字体也不再被拉伸模糊。
            int dpiNow = Native.DPI_AWARENESS_INVALID;
            try
            {
                dpiNow = Native.GetAwarenessFromDpiAwarenessContext(
                    Native.GetThreadDpiAwarenessContext());
            }
            catch { }

            if (dpiNow == 0)  // 不感知 → 抢救
            {
                try
                {
                    if (Native.SetProcessDpiAware())
                    {
                        dpiNow = 1;   // 系统感知
                        Log.Info("★ 进程原本不感知，已用 SetProcessDpiAware 抢救为系统感知");
                    }
                }
                catch (Exception ex2) { dpiSetErr += " | SetProcessDpiAware: " + ex2.Message; }
            }

            bool selfTest = HasArg(args, ArgSelfTest);
            bool stress = HasArg(args, ArgStress);
            bool dragBench = HasArg(args, "--dragbench");
            bool frameProbe = HasArg(args, "--probe");
            bool noElevate = HasArg(args, ArgNoElevate);
            SmokeMode = HasArg(args, ArgSmoke);

            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "HoldDrag");
            try { Directory.CreateDirectory(dir); } catch { }
            Log.Init(dir);

            Log.Info("SetProcessDpiAwarenessContext(PerMonitorV2) => " + dpiCtxSet
                + (dpiCtxSet ? "" : " errCode=" + dpiSetErrCode + (dpiSetErr.Length > 0 ? " ex=" + dpiSetErr : ""))
                + " | 当前感知=" + dpiNow);


            // 坐标空间初始化：万一前两层都没生效（进程仍是不感知），
            // 这里会把"钩子给的物理坐标"换算进进程自己的空间，功能依然可用。
            CoordSpace.Init();

            // --trace 打开逐帧拖动日志。默认关闭：拖动时每帧都要写盘，开着会拖慢手感。
            // 手感类问题光靠推理争不出结论，让用户复现一次、把日志发回来最快。
            Log.Verbose = HasArg(args, ArgTrace) || selfTest || stress;

            Log.Info("==== 启动 ==== 参数: " + (args.Length > 0 ? string.Join(" ", args) : "(无)"));

            if (selfTest || stress || dragBench || frameProbe)
            {
                // 自检里要跑"真实窗口 + 合成事件"的端到端拖动模拟，
                // 所以视觉样式与 DPI 感知要按正常启动那样先配好，否则测的不是真实环境。
                // ★ 顺序同主流程：SetHighDpiMode 必须最先调，否则会被锁成默认值。
                try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch (Exception ex) { Log.Error(ex); }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (selfTest) SelfTest.Run(dir);
                else if (dragBench) DragBench.Run();
                else if (frameProbe) FrameProbe.Run();
                else StressTest.Run(dir);
                return;
            }

            // ══ ★ 顺序很重要：SetHighDpiMode 必须是第一个被调用的 ★ ══
            //
            // 它必须在【任何窗口被创建之前】调用。而 EnableVisualStyles() 内部
            // 会去碰主题/公共控件，足以让 DPI 感知被提前锁定成默认值（SystemAware）。
            // 一旦锁定，后面再调 SetHighDpiMode 就只会返回 false 且【什么都不做】——
            // 日志里长期出现的 "SetHighDpiMode(PerMonitorV2) => False" 就是这个原因。
            //
            // 本程序全程用物理像素坐标（钩子拿到的光标坐标就是物理坐标），
            // 如果不是 PerMonitorV2，坐标空间就可能和系统对不上，
            // 进而让 WindowFromPoint 之类的命中测试出错。所以这里必须调对顺序。
            try
            {
                bool ok = Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Log.Info("SetHighDpiMode(PerMonitorV2) => " + ok);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // ★ 把"进程眼里的屏幕有多大"记下来 ★
            //
            // 这条日志是排查"按了没反应"的关键依据：
            //   · 若虚拟桌面 = 3840x2160（物理尺寸），说明进程是 DPI 感知的，
            //     钩子给的物理坐标可以直接用；
            //   · 若只有 1920x1080（被虚拟化过），说明进程是 DPI 不感知的，
            //     钩子给的物理坐标会被系统再缩放一次，
            //     WindowFromPoint 之类的命中测试必然对不上。
            // 这两种情况的修法完全不同，没有这条日志就只能靠猜。
            try
            {
                var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                int cx = Native.GetSystemMetrics(Native.SM_CXSCREEN);
                int cy = Native.GetSystemMetrics(Native.SM_CYSCREEN);

                int aware = Native.DPI_AWARENESS_INVALID;
                try
                {
                    aware = Native.GetAwarenessFromDpiAwarenessContext(
                        Native.GetThreadDpiAwarenessContext());
                }
                catch (Exception ex) { Log.Error(ex); }

                string awareName;
                switch (aware)
                {
                    case Native.DPI_AWARENESS_UNAWARE: awareName = "不感知(0)★危险"; break;
                    case Native.DPI_AWARENESS_SYSTEM_AWARE: awareName = "系统感知(1)"; break;
                    case Native.DPI_AWARENESS_PER_MONITOR_AWARE: awareName = "每显示器感知(2)"; break;
                    default: awareName = "查不到(" + aware + ")"; break;
                }

                Log.Info("DPI 感知级别 = " + awareName
                    + " | SM_CXSCREEN=" + cx + "x" + cy
                    + " | 虚拟桌面=" + vs.Width + "x" + vs.Height
                    + " | 主屏工作区=" + Screen.PrimaryScreen.WorkingArea.Width
                    + "x" + Screen.PrimaryScreen.WorkingArea.Height);
            }
            catch (Exception ex) { Log.Error(ex); }

            // 开发期视觉核对入口：把设置窗口在窄 / 宽两种尺寸下各截一张图。
            // ══════ 测试用"外部窗口宿主" ══════
            //
            //  为什么需要它：排查"全屏转窗口没有外框"时，用例一直跑在【我们自己进程】
            //  的窗口上，还开着 AllowOwnProcessAsTargetForTest / ForcedTargetForTest
            //  两个后门 —— 全屏判定、黑名单、真实命中测试整条被绕过，
            //  于是这条链路上真正会出问题的地方一次都没走到。
            //  这个模式让本程序以【另一个进程】的身份开一个普通窗口，
            //  自检就能把它当成"别家的应用"来走真实链路（句柄/PID 都是外部的）。
            if (HasArg(args, "--windowhost"))
            {
                RunWindowHost();
                return;
            }

            // 布局类改动光靠断言不够，必须用眼睛确认一遍间距、对齐、有没有重叠。
            // 截图存到 %TEMP%\holddrag-visual\，不碰用户配置。
            if (HasArg(args, "--visualcheck"))
            {
                string vdir = Path.Combine(Path.GetTempPath(), "holddrag-visual");
                VisualCheck.Run(vdir);
                StressTest.HardExit(0);
                return;
            }

            // 诊断入口：把外框预览在五种样式下各渲一张，用于定位"选了形状但预览不变"。
            if (HasArg(args, "--previewtest"))
            {
                string pdir = Path.Combine(Path.GetTempPath(), "holddrag-previewtest");
                SettingsForm.DumpPreviewStyles(pdir);
                StressTest.HardExit(0);
                return;
            }

            // ── 配置必须先读 ──
            // 因为"要不要提权"本身就是配置项（默认要，用户可以关）。
            // 早先的写法是先提权再读配置，等于把这个开关写死在启动流程里了。
            var store = new ConfigStore(dir);
            store.Load();

            bool admin = IsAdministrator();
            LaunchedWithoutElevation = !admin;

            if (!noElevate && !SmokeMode && store.Current.RunAsAdmin && !admin)
            {
                if (RelaunchElevated(args))
                {
                    Log.Info("已请求提权重启，本进程退出");
                    return;
                }

                // 用户在 UAC 上点了"否"，或者提权失败。
                // 按用户要求：降级成普通权限继续跑，只是拖不动提权窗口而已。
                Log.Info("未获得管理员权限，降级为普通权限继续运行");
                if (store.Current.WarnNonAdmin && AskToRestartElevated(args))
                {
                    return;
                }
            }

            // ── 单实例 ──
            bool createdNew;
            var mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                Log.Info("已有实例在运行，通知它把设置窗口显示出来");
                try
                {
                    using (var ev = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        ev.Set();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
                return;
            }

            EventWaitHandle showEvent = null;
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }

            // ── 把系统计时精度提到 1ms ──
            // 拖动要按屏幕刷新率节流（60Hz=16.7ms、144Hz=6.9ms），而 Windows 默认的
            // 定时器精度是 15.6ms —— 不提高的话 Thread.Sleep(3) 会睡满 15ms，
            // 节流直接退化成 60Hz 上限，高刷屏上反而更不跟手。
            bool timerRaised = false;
            try { timerRaised = Native.TimeBeginPeriod(1) == 0; }
            catch (Exception ex) { Log.Error(ex); }

            try
            {
                using (var ctx = new TrayContext(dir, store, showEvent))
                {
                    Application.Run(ctx);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                MessageBox.Show("程序运行中发生未处理异常，详情见日志：\r\n" + Log.Path + "\r\n\r\n" + ex.Message,
                    "长按拖窗", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (timerRaised) { try { Native.TimeEndPeriod(1); } catch { } }
                GC.KeepAlive(mutex);
            }
        }

        /// <summary>
        /// 测试宿主：开一个普通可见窗口并一直泵消息，供自检当作"外部应用"使用。
        /// 标题固定，自检靠标题找到它。进程被宿主 Kill 掉即结束。
        /// </summary>
        internal const string WindowHostTitle = "HoldDrag 外部测试窗口";

        private static void RunWindowHost()
        {
            try
            {
                Application.EnableVisualStyles();
                var f = new Form();
                f.Text = WindowHostTitle;
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(120, 120);
                f.Size = new System.Drawing.Size(420, 300);
                f.ShowInTaskbar = true;
                Application.Run(f);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            foreach (var a in args)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return false;
            }
        }

        /// <summary>
        /// 提示当前不是管理员权限，并问要不要提权重启。
        /// 返回 true 表示已经拉起了新的提权实例（本进程应当退出）。
        /// 用户点"继续使用"或者第二次 UAC 又被拒，都返回 false，程序照常以普通权限跑。
        /// </summary>
        private static bool AskToRestartElevated(string[] args)
        {
            try
            {
                var r = MessageBox.Show(
                    "当前正在以【普通用户权限】运行。\r\n\r\n" +
                    "这个状态下：\r\n" +
                    "  · 日常拖动浏览器、编辑器、资源管理器等都正常；\r\n" +
                    "  · 但拖不动以管理员身份运行的窗口（例如任务管理器、部分安装程序）。\r\n\r\n" +
                    "建议改用管理员权限重新打开。现在重启吗？\r\n" +
                    "（选\"否\"就保持现状继续使用，功能不受影响）",
                    "长按拖窗 · 权限提示",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1);

                if (r != DialogResult.Yes) return false;

                if (RelaunchElevated(args))
                {
                    Log.Info("用户选择以管理员权限重启，本进程退出");
                    return true;
                }

                Log.Info("二次提权仍被拒绝，继续以普通权限运行");
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return false;
            }
        }

        private static bool RelaunchElevated(string[] args)
        {
            try
            {
                var forward = new List<string>();
                foreach (var a in args)
                {
                    if (!string.Equals(a, ArgNoElevate, StringComparison.OrdinalIgnoreCase)) forward.Add(a);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = AppPaths.ExePath,
                    Arguments = string.Join(" ", forward.ToArray()),
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                // 用户在 UAC 弹窗点了"否"会走到这里
                Log.Info("提权被拒绝： " + ex.Message);
                return false;
            }
        }
    }
}
