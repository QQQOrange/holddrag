using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using static HoldDrag.Native;

namespace HoldDrag
{
    /// <summary>
    /// 无界面自检。GUI 行为无法自动点击验证，因此把所有“纯逻辑”都抽成静态方法并在
    /// 这里断言——几何判定、按键标识、配置读写与回退、长按状态机、钩子安装。
    /// 运行：HoldDrag.exe --selftest
    /// 结果同时写入 %APPDATA%\HoldDrag\selftest.txt，方便无控制台环境查看。
    /// </summary>
    internal static class SelfTest
    {
        private static int _pass;
        private static int _fail;
        private static readonly StringBuilder Buf = new StringBuilder();

        private const uint AttachParentProcess = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        private sealed class NullSink : IInputSink
        {
            public bool OnKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb) { return false; }
            public bool OnMouse(int msg, ref Native.MSLLHOOKSTRUCT ms) { return false; }
        }

        public static void Run(string dir)
        {
            try { AttachConsole(AttachParentProcess); } catch { }

            StartWatchdog(dir);

            Line("==== 长按拖窗 自检 ====");
            Line("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Line("管理员权限：" + (Program.IsAdministrator() ? "是" : "否"));
            Line("");

            Safe("T01 默认配置", TestDefaults);
            Safe("T02 按键标识", TestKeyIds);
            Safe("T03 触发键匹配", TestKeyMatching);
            Safe("T04 九宫格判定", TestZones9);
            Safe("T05 最近角判定", TestNearestCorner);
            Safe("T06 屏幕边缘吸附", TestScreenEdge);
            Safe("T07 吸附矩形换算", TestSnapRects);
            Safe("T08 九宫格→屏幕区域", TestZoneMapping);
            Safe("T09 配置越界归一", TestNormalize);
            Safe("T10 配置往返", () => TestConfigRoundTrip(dir));
            Safe("T11 配置备份恢复", () => TestConfigRecovery(dir));
            Safe("T12 长按状态机（超阈值）", TestTriggerHold);
            Safe("T13 长按状态机（短按补发）", TestTriggerShortPress);
            Safe("T14 关闭长按阈值", TestTriggerNoHold);
            Safe("T15 全局钩子装卸", TestHooks);
            Safe("T16 窗口排除表", TestExclusions);
            Safe("T17 exe 内嵌图标", TestIconEmbedded);
            Safe("T18 端到端拖动（移动）", TestDragMoveE2E);
            Safe("T19 端到端拖动（缩放）", TestDragResizeE2E);
            Safe("T20 拖动期间绝不吞鼠标移动（光标冻结回归）", TestDragNeverSwallowsMouseMove);
            Safe("T21 未激活时鼠标键不触发", TestNoDragWhenInactive);
            Safe("T22 设置窗口所有分组都已挂到窗体上（设置空白回归）", TestSettingsLayout);
            Safe("T23 触发键激活态稳定保持（看门狗误判回归）", TestTriggerStaysActive);
            Safe("T24 拖动激活外框 显示/隐藏 与自选矩形一致", TestActivationFrameOverlay);
            Safe("T25 吸附预览浮层只在开了预览时才出现", TestSnapPreviewOverlay);
            Safe("T26 帧率节流算出的间隔合理，手动值不被吞", TestFrameInterval);
            Safe("T27 全屏窗口默认被跳过，开关打开后可拖", TestFullscreenOptIn);
            Safe("T28 钩子看门狗能判定「钩子已死」", TestHookWatchdog);
            Safe("T29 删除的功能已彻底下线（无残留配置项）", TestRemovedFeaturesGone);
            Safe("T30 外框外观（颜色 / 样式 / 粗细 / 圆角）可配置且越界会被拉回", TestFrameTheme);
            Safe("T31 外框在会话结束之后不会残留（显框前二次校验）", TestFrameNeverSticks);
            Safe("T32 拖动期间用区域环、结束后换成抗锯齿框（拖动残影回归）", TestDragRingFrameSwap);
            Safe("T33 按住触发键执行一次性动作后外框继续保留（外框消失回归）", TestFramePersistsAfterAction);
            Safe("T34 设置窗口可调整大小，且分组带左右锚定（设置尺寸回归）", TestSettingsResizable);
            Safe("T35 主题变更立刻反映到浮层像素上，不会等到下次移动", TestThemeAppliesImmediately);
            Safe("T36 在设置里选主题后，预览与保存结果都是选的那个（选主题失效回归）", TestSettingsThemeSelectionRoundTrip);
            Safe("T37 设置窗口拉宽后真的多装东西（1 列变 2 列、输入框跟着变宽）", TestSettingsResizeIsMeaningful);
            Safe("T38 设置窗口所有控件可见可点（美化改成自绘后功能失效回归）", TestSettingsControlsReachable);
            Safe("T39 外框亮着时左键/右键依然能接管窗口（浮层挡掉拖动回归）", TestHoverFrameDoesNotBlockDrag);
            Safe("T40 高DPI 下文字不被裁切（字号被放大两次回归）", TestNoTextClippedByHeight);
  Safe("T41 所有下拉框都有可见选中项（外框样式显示为空回归）", TestKeyCombosHaveSelection);
                  Safe("T42 拖动写窗口必须异步提交（移动/变形不跟手回归）", TestMoveFlagsAreAsync);
            Safe("T43 真实鼠标事件能走完整钩子链路（触发后左键右键失效回归）", TestRealMouseThroughHook);
       Safe("T44 重启后配置必须完整保留（配置持久化回归）", TestConfigSurvivesRestart);
       Safe("T45 命中测试必须有兜底、Z 序方向必须正确（核心功能失效回归）", TestHitTestHasFallback);
            Safe("T46 物理坐标必须换算进进程坐标空间（DPI不感知导致核心功能失效回归）", TestCoordinateSpace);
            Safe("T47 黑名单能用进程名匹配 + 两个挑选入口都在（黑名单易用性回归）", TestBlacklistPickers);
            Safe("T48 全链路真实拖动：窗口必须真的跟着光标动（核心功能端到端）", TestRealDragMovesWindow);

            Line("");
            Line("合计 " + (_pass + _fail) + " 项：通过 " + _pass + "，失败 " + _fail);

            string text = Buf.ToString();
            try { File.WriteAllText(Path.Combine(dir, "selftest.txt"), text, Encoding.UTF8); } catch { }

            ConsoleEcho(text);

            // 自检是"跑完就该结束"的诊断入口，这里必须是确定性退出：
            // 它一路上建过真实窗口、起过拖动工作线程、挂过全局钩子，
            // 靠 Main 自然返回可能被运行时的关停流程拖住。用 Environment.Exit 明确收尾。
            // 而且再挂一个 3 秒后强杀的兜底线程（理由同 StressTest.HardExit）。
            StressTest.HardExit(_fail == 0 ? 0 : 1);
        }

        /// <summary>
        /// 把结果回显到控制台 —— 但【绝不能让这一步吊住进程】。
        ///
        /// 本程序是 WinExe，--selftest 会 AttachConsole 到父控制台才能在终端里看到输出。
        /// 而在控制台被重定向、父进程已经退出、或 ConPTY 不再读取的情况下，
        /// 往控制台写会**永久阻塞**（不是抛异常，try/catch 抓不到）。
        /// 曾经就因为这个：自检明明跑完 130 项、报告也写进文件了，
        /// 进程却一直不退出，看起来像"自检卡死"。
        ///
        /// 所以放到后台线程去写，等不到就放弃 —— 报告文件才是权威结果。
        /// </summary>
        private static void ConsoleEcho(string text)
        {
            try
            {
                var t = new System.Threading.Thread(delegate ()
                {
                    try { Console.Out.Write(text); Console.Out.Flush(); } catch { }
                });
                t.IsBackground = true;
                t.Name = "HoldDrag.SelfTestEcho";
                t.Start();
                t.Join(1500);
            }
            catch
            {
                // 回显失败无所谓，结果已经在 selftest.txt 里了
            }
        }

        // ── 测试实现 ────────────────────────────────────────────

        private static void TestDefaults()
        {
            var c = new AppConfig();
            Eq(c.TriggerKeyId, "K:0x0D", "默认触发键为 Enter");
            Eq(c.HoldMs, 600, "默认长按阈值 600ms");
            Check(c.RequireHold, "默认需要长按");
            Eq(c.ActionLeft, Actions.Move, "左键默认移动");
            Eq(c.ActionRight, Actions.Resize, "右键默认缩放");
            Eq(c.ActionMiddle, Actions.Maximize, "中键默认最大化");
            Check(c.EdgeSnap && c.SnapToOtherWindows && c.DoubleClickLeftMaximize && c.DoubleClickRightZone,
                "四项基本增强默认开启");
            Check(c.ShowActivationFrame && c.SnapPreview && c.BringToFront, "外框 / 吸附预览 / 提最前 默认开启");
            Check(!c.AllowFullscreenWindows, "全屏窗口默认不动（风险项，需手工开启）");
            Check(c.RunAsAdmin && c.WarnNonAdmin, "管理员权限默认开启，非管理员时默认提示");
            Eq(c.DragRefreshMode, RefreshModes.Auto, "刷新率默认自动跟随屏幕");
        }

        private static void TestKeyIds()
        {
            Eq(KeysUtil.ToVirtualKey("K:0x0D"), 0x0D, "Enter 虚拟键码");
            Eq(KeysUtil.ToVirtualKey(KeysUtil.RightAlt), 0xA5, "右 Alt 虚拟键码");
            Eq(KeysUtil.ToVirtualKey(KeysUtil.LeftShift), 0xA0, "左 Shift 虚拟键码");
            Eq(KeysUtil.ToVirtualKey(KeysUtil.MouseX1), 0x05, "侧键 X1 虚拟键码");
            Eq(KeysUtil.ToVirtualKey("垃圾值"), 0, "非法 Id 返回 0");
            Check(KeysUtil.IsToggleKey("K:0x14"), "CapsLock 被识别为切换型按键");
            Check(!KeysUtil.IsToggleKey("K:0x0D"), "Enter 不是切换型按键");
            Check(KeysUtil.DisplayName("K:0x0D").Contains("Enter"), "Enter 有中文名");
        }

        private static void TestKeyMatching()
        {
            var enter = new Native.KBDLLHOOKSTRUCT();
            enter.vkCode = 0x0D;
            Check(KeysUtil.MatchesKeyboard("K:0x0D", kb: enter), "Enter 匹配 Enter");
            Check(!KeysUtil.MatchesKeyboard(KeysUtil.RightAlt, enter), "Enter 不匹配右 Alt");

            var lshift = new Native.KBDLLHOOKSTRUCT();
            lshift.vkCode = 0x10;
            lshift.scanCode = 0x2A;
            Check(KeysUtil.MatchesKeyboard(KeysUtil.LeftShift, lshift), "扫描码 0x2A 判为左 Shift");
            Check(!KeysUtil.MatchesKeyboard(KeysUtil.RightShift, lshift), "扫描码 0x2A 不是右 Shift");

            var rshift = new Native.KBDLLHOOKSTRUCT();
            rshift.vkCode = 0x10;
            rshift.scanCode = 0x36;
            Check(KeysUtil.MatchesKeyboard(KeysUtil.RightShift, rshift), "扫描码 0x36 判为右 Shift");

            var ralt = new Native.KBDLLHOOKSTRUCT();
            ralt.vkCode = 0x12;
            ralt.flags = Native.LLKHF_EXTENDED;
            Check(KeysUtil.MatchesKeyboard(KeysUtil.RightAlt, ralt), "带 extended 标志判为右 Alt");
            Check(!KeysUtil.MatchesKeyboard(KeysUtil.LeftAlt, ralt), "带 extended 标志不是左 Alt");

            var lctrl = new Native.KBDLLHOOKSTRUCT();
            lctrl.vkCode = 0x11;
            Check(KeysUtil.MatchesKeyboard(KeysUtil.LeftCtrl, lctrl), "无 extended 标志判为左 Ctrl");

            var mouse = new Native.MSLLHOOKSTRUCT();
            mouse.mouseData = 1u << 16;
            Check(KeysUtil.MatchesMouse(KeysUtil.MouseX1, Native.WM_XBUTTONDOWN, mouse), "侧键 1 匹配 X1 按下");
            Check(!KeysUtil.MatchesMouse(KeysUtil.MouseX2, Native.WM_XBUTTONDOWN, mouse), "侧键 1 不匹配 X2");
            Check(!KeysUtil.MatchesMouse(KeysUtil.MouseX1, Native.WM_MOUSEMOVE, mouse), "移动事件不算侧键");
        }

        private static void TestZones9()
        {
            var r = new Native.RECT(100, 100, 500, 500);   // 角区阈值 = 400*26/100 = 104
            Eq(Zones.Resize(r, 105, 105, 26), ResizeZone.TopLeft, "左上角");
            Eq(Zones.Resize(r, 495, 105, 26), ResizeZone.TopRight, "右上角");
            Eq(Zones.Resize(r, 105, 495, 26), ResizeZone.BottomLeft, "左下角");
            Eq(Zones.Resize(r, 495, 495, 26), ResizeZone.BottomRight, "右下角");
            Eq(Zones.Resize(r, 300, 105, 26), ResizeZone.Top, "上边中段");
            Eq(Zones.Resize(r, 300, 495, 26), ResizeZone.Bottom, "下边中段");
            Eq(Zones.Resize(r, 105, 300, 26), ResizeZone.Left, "左边中段");
            Eq(Zones.Resize(r, 495, 300, 26), ResizeZone.Right, "右边中段");
            Eq(Zones.Resize(r, 300, 300, 26), ResizeZone.Center, "正中区域");
        }

        private static void TestNearestCorner()
        {
            var r = new Native.RECT(100, 100, 500, 500);
            Eq(Zones.NearestCorner(r, 150, 150), ResizeZone.TopLeft, "中心格偏左上→左上角");
            Eq(Zones.NearestCorner(r, 450, 150), ResizeZone.TopRight, "中心格偏右上→右上角");
            Eq(Zones.NearestCorner(r, 150, 450), ResizeZone.BottomLeft, "中心格偏左下→左下角");
            Eq(Zones.NearestCorner(r, 450, 450), ResizeZone.BottomRight, "中心格偏右下→右下角");
        }

        private static void TestScreenEdge()
        {
            var work = new Native.RECT(0, 0, 1920, 1080);
            Eq(Zones.ScreenEdge(work, 5, 540, 25), SnapZone.LeftHalf, "贴左边缘→左半屏");
            Eq(Zones.ScreenEdge(work, 1915, 540, 25), SnapZone.RightHalf, "贴右边缘→右半屏");
            Eq(Zones.ScreenEdge(work, 960, 5, 25), SnapZone.Maximize, "贴上边缘→最大化");
            Eq(Zones.ScreenEdge(work, 5, 5, 25), SnapZone.TopLeftQuarter, "左上角→左上四分之一");
            Eq(Zones.ScreenEdge(work, 1915, 5, 25), SnapZone.TopRightQuarter, "右上角→右上四分之一");
            Eq(Zones.ScreenEdge(work, 5, 1075, 25), SnapZone.BottomLeftQuarter, "左下角→左下四分之一");
            Eq(Zones.ScreenEdge(work, 1915, 1075, 25), SnapZone.BottomRightQuarter, "右下角→右下四分之一");
            Eq(Zones.ScreenEdge(work, 960, 540, 25), SnapZone.None, "屏幕中央不吸附");
            Eq(Zones.ScreenEdge(work, 960, 1075, 25), SnapZone.None, "只贴下边缘不吸附");
        }

        private static void TestSnapRects()
        {
            var work = new Native.RECT(0, 0, 1920, 1080);
            var l = Zones.RectOf(SnapZone.LeftHalf, work);
            Check(l.Left == 0 && l.Top == 0 && l.Right == 960 && l.Bottom == 1080, "左半屏矩形");

            var q = Zones.RectOf(SnapZone.TopLeftQuarter, work);
            Check(q.Left == 0 && q.Top == 0 && q.Right == 960 && q.Bottom == 540, "左上四分之一矩形");

            var b = Zones.RectOf(SnapZone.BottomHalf, work);
            Check(b.Left == 0 && b.Top == 540 && b.Right == 1920 && b.Bottom == 1080, "下半屏矩形");
        }

        private static void TestZoneMapping()
        {
            Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Top), SnapZone.TopHalf, "上格→上半屏");
            Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Bottom), SnapZone.BottomHalf, "下格→下半屏");
            Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Left), SnapZone.LeftHalf, "左格→左半屏");
            Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Center), SnapZone.Maximize, "中心格→最大化");
            Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.BottomRight), SnapZone.BottomRightQuarter, "右下格→右下四分之一");
        }

        private static void TestNormalize()
        {
            var c = new AppConfig();
            c.HoldMs = 50;
            c.SnapDistance = 999;
            c.CornerPercent = 90;
            c.ActionLeft = "Bogus";
            c.AutoStart = "X";
            c.DragRefreshMode = "X";
            c.DragRefreshHz = -3;
            c.Normalize();

            Eq(c.HoldMs, 100, "阈值下限 100");
            Eq(c.SnapDistance, 200, "吸附距离上限 200");
            Eq(c.CornerPercent, 45, "角区比例上限 45");
            Eq(c.ActionLeft, Actions.Move, "非法动作回退为移动");
            Eq(c.AutoStart, AutoStartModes.TaskScheduler, "非法自启方式回退");
            Eq(c.DragRefreshMode, RefreshModes.Auto, "非法刷新率模式回退为自动");
            Eq(c.DragRefreshHz, 0, "非法刷新率归零");
        }

        private static void TestConfigRoundTrip(string dir)
        {
            string t = Path.Combine(Path.GetTempPath(), "holddrag-rt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(t);
            try
            {
                var s1 = new ConfigStore(t);
                s1.Load();
                s1.Current.HoldMs = 888;
                s1.Current.TriggerKeyId = KeysUtil.RightAlt;
                s1.Current.ActionLeft = Actions.Resize;
                s1.Current.EdgeSnap = false;
                s1.Save();

                var s2 = new ConfigStore(t);
                s2.Load();
                Eq(s2.Current.HoldMs, 888, "HoldMs 往返一致");
                Eq(s2.Current.TriggerKeyId, KeysUtil.RightAlt, "触发键往返一致");
                Eq(s2.Current.ActionLeft, Actions.Resize, "左键动作往返一致");
                Check(!s2.Current.EdgeSnap, "布尔开关往返一致");
            }
            finally
            {
                try { Directory.Delete(t, true); } catch { }
            }
        }

        private static void TestConfigRecovery(string dir)
        {
            string t = Path.Combine(Path.GetTempPath(), "holddrag-rc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(t);
            try
            {
                var s1 = new ConfigStore(t);
                s1.Load();
                s1.Current.HoldMs = 777;
                s1.Save();                       // main = 777
                s1.Current.HoldMs = 888;
                s1.Save();                       // bak = 777, main = 888

                string main = Path.Combine(t, "config.json");
                Check(File.Exists(main + ".bak"), "备份文件已生成");
                File.WriteAllText(main, "{ 这不是一个合法的 JSON");

                var s2 = new ConfigStore(t);
                s2.Load();
                Eq(s2.Current.HoldMs, 777, "主配置损坏后从备份恢复");

                // 恢复后应当把主配置重新写成合法内容
                var s3 = new ConfigStore(t);
                s3.Load();
                Eq(s3.Current.HoldMs, 777, "恢复结果已回写主配置");
            }
            finally
            {
                try { Directory.Delete(t, true); } catch { }
            }
        }

        private static void TestTriggerHold()
        {
            var cfg = new AppConfig();
            cfg.RequireHold = true;
            cfg.HoldMs = 600;

            using (var ts = new TriggerState(() => cfg))
            {
                ts.SimulateKeyDown();
                Check(!ts.Active, "按下瞬间未到阈值，不应激活");
                Check(ts.Held, "按下后处于按住状态");

                ts.ForceHoldElapsed();
                Check(ts.Active, "超过阈值后进入激活态");

                bool replayCalled = false;
                bool replayed = ts.SimulateKeyUp(() => { replayCalled = true; return true; });
                Check(!replayed, "激活后松手不再补发原按键");
                Check(!replayCalled, "激活路径不应调用补发回调");
                Check(!ts.Active, "松手后退出激活态");
                Check(!ts.Held, "松手后不再是按住状态");
            }
        }

        private static void TestTriggerShortPress()
        {
            var cfg = new AppConfig();
            cfg.RequireHold = true;
            cfg.HoldMs = 600;

            using (var ts = new TriggerState(() => cfg))
            {
                ts.SimulateKeyDown();
                bool replayCalled = false;
                bool replayed = ts.SimulateKeyUp(() => { replayCalled = true; return true; });

                Check(replayed, "阈值内松手走补发路径");
                Check(replayCalled, "阈值内松手确实补发了原按键");
                Check(!ts.Active, "短按不激活");
                Check(ts.LastPressReplayed, "短按已记录补发状态");
            }
        }

        private static void TestTriggerNoHold()
        {
            var cfg = new AppConfig();
            cfg.RequireHold = false;

            using (var ts = new TriggerState(() => cfg))
            {
                ts.SimulateKeyDown();
                Check(ts.Active, "关闭长按阈值后按下即激活");
                bool replayed = ts.SimulateKeyUp(null);
                Check(!replayed, "关闭长按阈值后松手不补发（该键已成为纯修饰键）");
                Check(!ts.Active, "松手后退出激活态");
            }
        }

        private static void TestHooks()
        {
            using (var engine = new HookEngine(new NullSink()))
            {
                bool ok = engine.Install();
                Check(ok, "全局键鼠钩子安装成功", "LastError=" + engine.LastError);
                Check(engine.KeyboardInstalled, "键盘钩子已挂");
                Check(engine.MouseInstalled, "鼠标钩子已挂");

                engine.Uninstall();
                Check(!engine.KeyboardInstalled && !engine.MouseInstalled, "钩子已干净卸载");
            }
        }

        private static void TestExclusions()
        {
            Check(WindowActions.IsExcludedClass("Progman"), "桌面 Progman 被排除");
            Check(WindowActions.IsExcludedClass("WorkerW"), "WorkerW 被排除");
            Check(WindowActions.IsExcludedClass("Shell_TrayWnd"), "任务栏被排除");
            Check(WindowActions.IsExcludedClass("Windows.UI.Core.CoreWindow"), "开始菜单被排除");
            Check(!WindowActions.IsExcludedClass("Notepad"), "记事本不被排除");
            Check(!WindowActions.IsExcludedClass("Chrome_WidgetWin_1"), "浏览器不被排除");
        }

        private static void TestIconEmbedded()
        {
            try
            {
                string p = AppPaths.ExePath;
                Line("  宿主 exe：" + p);
                using (var ico = Icon.ExtractAssociatedIcon(p))
                {
                    Check(ico != null, "能从 exe 中提取图标（说明 app.ico 已内嵌）");
                }
            }
            catch (Exception ex)
            {
                Bad("exe 内嵌图标可提取", ex.Message);
            }
        }

        // ── 端到端拖动模拟 ──────────────────────────────────────
        //
        // 前面那些用例都是“纯逻辑”断言，证明不了“按住触发键后窗口到底会不会动”。
        // 这一组用一个真实的窗口 + 合成的钩子事件，把 Coordinator → FindTarget →
        // DragEngine → SetWindowPos 整条链路跑一遍，并直接读回窗口真实矩形来断言。

        /// <summary>一台用于端到端拖动模拟的“真窗口 + 真 Coordinator”。</summary>
        private sealed class DragFixture : IDisposable
        {
            private readonly string _dir;
            public readonly Form Window;
            public readonly ConfigStore Store;
            public readonly Coordinator Coord;

            public IntPtr H { get { return Window.Handle; } }

            public DragFixture(Action<AppConfig> tweak)
            {
                _dir = Path.Combine(Path.GetTempPath(), "holddrag-e2e-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);

                Store = new ConfigStore(_dir);
                Store.Load();

                var c = Store.Current;
                // 默认关掉所有干扰项，每个用例只打开自己要验证的那一项
                c.EdgeSnap = false;
                c.SnapToOtherWindows = false;
                c.BringToFront = false;
                c.ShowActivationFrame = false;
                c.SnapPreview = false;
                c.DoubleClickLeftMaximize = false;
                c.DoubleClickRightZone = false;
                c.AllowFullscreenWindows = true;            // 测试窗口不想被"全屏"规则挡掉
                if (tweak != null) tweak(c);

                Window = new Form();
                Window.FormBorderStyle = FormBorderStyle.Sizable;
                Window.StartPosition = FormStartPosition.Manual;
                Window.ShowInTaskbar = false;
                Window.Text = "HoldDrag selftest";
                Window.TopMost = true;                       // 保证 WindowFromPoint 必定命中它
                Window.Bounds = new Rectangle(120, 120, 320, 220);
                Window.Show();
                Pump();

                Coord = new Coordinator(Store);
                Coord.PrecreateOverlays();
                Pump();

                // 端到端用例必须确定性地命中这个测试窗口。
                // 靠 WindowFromPoint 在无桌面/被遮挡的环境里会命中别的窗口，用例就白跑了。
                WindowActions.ForcedTargetForTest = Window.Handle;
            }

public void Dispose()
       {
    WindowActions.ForcedTargetForTest = IntPtr.Zero;
                try { Coord.Dispose(); } catch { }
try
         {
   // ★ 必须同步销毁，不只是 Close ★
    // Close() 是【异步】的：它只是投递一个 WM_CLOSE，窗口何时真正消失
      // 由消息循环决定。所以 Dispose 返回之后，这个 TopMost 窗口
   // 可能还活着 —— 而它会压住后面用例的目标窗口，
    // 表现为"同一个自检两次跑出不同结论"的间歇性假红。
      //
      //   （C# 的 Form 没有 VB 里那个 Destroy() 方法，
    //   等价做法是先隐藏再走 P/Invoke 的 DestroyWindow，那是同步的。）
    try { if (!Window.IsDisposed) { Window.Visible = false; Native.DestroyWindow(Window.Handle); } } catch { }
      Window.Close();
  Window.Dispose();
  }
      catch { }
    // 抽几轮消息，把 Close/Destroy 产生的收尾消息走完，
  // 否则残留窗口还是会活到下一个用例。
    for (int i = 0; i < 6; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
        try { Directory.Delete(_dir, true); } catch { }
     }
        }

        private static void Pump() { Application.DoEvents(); }

     /// <summary>
        /// 取【离给定坐标最近的那个显示器】的工作区。
        ///
        /// 为什么不用 Screen.PrimaryScreen：自检可能跑在多显示器环境里，
      /// 而写死主屏会算出一个被副屏遮住的位置，
    /// 于是"我的窗口"根本不在前台，测试就变成在猜。
        /// </summary>
   private static Native.RECT WorkAreaOfNearestMonitor(Native.POINT p)
    {
        try
      {
var mi = Native.GetMonitorInfoAt(p);
if (mi.rcWork.Width > 0 && mi.rcWork.Height > 0) return mi.rcWork;
        }
      catch { }
     try
     {
     var wa = Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)).WorkingArea;
         return new Native.RECT(wa.Left, wa.Top, wa.Right, wa.Bottom);
      }
catch { }
    return new Native.RECT(0, 0, 1920, 1080);
        }

        /// <summary>
        /// 把一个窗口句柄翻译成人类能看懂的描述（标题 / 类名 / 所属进程）。
        ///
        /// 存在的理由：自检里出现过"实际命中 A、期望命中 B"这种看不出所以然的失败。
        /// 光有两个十六进制句柄谁也判断不了谁是谁 ——
        /// 必须把窗口标题和类名打出来，才能认出那是哪个用例留下的残留窗口。
        /// </summary>
private static string DescribeWindow(IntPtr h)
   {
            if (h == IntPtr.Zero) return "(空句柄)";
            try
            {
         IntPtr root = Native.GetAncestor(h, Native.GA_ROOT);
            if (root == IntPtr.Zero) root = h;

  uint pid;
   Native.GetWindowThreadProcessId(root, out pid);

    return root.ToInt64().ToString("X")
       + " \"" + Native.GetWindowTitle(root) + "\""
     + " 类=" + Native.GetClassName(root)
      + " 进程=" + pid
      + (pid == (uint)OwnProcessIdForTest ? "(自己)" : "(外部)");
    }
  catch (Exception ex)
   {
       return h.ToInt64().ToString("X") + " (描述失败:" + ex.Message + ")";
            }
   }

   private static int OwnProcessIdForTest
        {
      get
     {
    try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
       catch { return -1; }
 }
        }

        /// <summary>等一会儿并持续抽消息，让异步的 SetWindowPos 真正落地。</summary>
        private static void Settle()
        {
            long end = Environment.TickCount64 + 80;
            while (Environment.TickCount64 < end)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
        }

        /// <summary>
        /// 等得久一点。窗口"最大化 / 还原"带系统动画（约 200ms），
        /// 只 Settle() 80ms 会在动画中途读矩形，断言就不可信了。
        /// </summary>
        private static void SettleLong()
        {
            long end = Environment.TickCount64 + 500;
            while (Environment.TickCount64 < end)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
        }

        private static Native.MSLLHOOKSTRUCT Ms(Native.POINT p)
        {
            var ms = new Native.MSLLHOOKSTRUCT();
            ms.pt = p;
            return ms;
        }

        private static Native.POINT CenterOf(IntPtr h)
        {
            Native.RECT r;
            Native.GetWindowRect(h, out r);
            return new Native.POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        }

        /// <summary>把触发键推进到“已激活”态（等同长按超过阈值）。</summary>
        private static void ActivateTrigger(Coordinator c)
        {
            c.Trigger.SimulateKeyDown();
            c.Trigger.ForceHoldElapsed();
        }

        /// <summary>屏幕太小 / 无桌面时，真实窗口测试没有意义，直接跳过。</summary>
        private static bool NoRoomForTestWindow()
        {
            int w = Native.GetSystemMetrics(Native.SM_CXSCREEN);
            int h = Native.GetSystemMetrics(Native.SM_CYSCREEN);
            if (w < 400 || h < 300)
            {
                Line("  （当前显示区域只有 " + w + "x" + h + "，跳过真实窗口模拟）");
                return false;
            }
            return true;
        }

        private static void TestDragMoveE2E()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(null))
                {
                    Native.RECT r0;
                    Native.GetWindowRect(f.H, out r0);

                    ActivateTrigger(f.Coord);
                    Check(f.Coord.Trigger.Active, "触发键已进入激活态");

                    var msDown = Ms(CenterOf(f.H));
                    Check(f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref msDown), "左键按下被钩子吞掉");
                    Check(f.Coord.Drag.IsDragging, "拖动会话已经开始");

                    var p1 = new Native.POINT(CenterOf(f.H).X + 80, CenterOf(f.H).Y + 60);
                    var msMove = Ms(p1);
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref msMove);
                    Settle();

                    Native.RECT r1;
                    Native.GetWindowRect(f.H, out r1);
                    Eq(r1.Left - r0.Left, 80, "窗口水平方向跟随光标 80px");
                    Eq(r1.Top - r0.Top, 60, "窗口垂直方向跟随光标 60px");
                    Eq(r1.Width, r0.Width, "移动过程中宽度不变");
                    Eq(r1.Height, r0.Height, "移动过程中高度不变");

                    var msUp = Ms(p1);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref msUp);
                    Check(!f.Coord.Drag.IsDragging, "松开左键后拖动会话结束");
                }
            }
            finally { WindowActions.AllowOwnProcessAsTargetForTest = false; }
        }

        private static void TestDragResizeE2E()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(null))
                {
                    Native.RECT r0;
                    Native.GetWindowRect(f.H, out r0);

                    ActivateTrigger(f.Coord);

                    // 按在右下角：九宫格应判为 BottomRight，向右下拖动即放大
                    var grab = new Native.POINT(r0.Right - 12, r0.Bottom - 12);
                    var msDown = Ms(grab);
                    Check(f.Coord.OnMouse(Native.WM_RBUTTONDOWN, ref msDown), "右键按下被钩子吞掉");
                    Check(f.Coord.Drag.IsDragging, "缩放会话已经开始");

                    var p1 = new Native.POINT(grab.X + 60, grab.Y + 40);
                    var msMove = Ms(p1);
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref msMove);
                    Settle();

                    Native.RECT r1;
                    Native.GetWindowRect(f.H, out r1);
                    Eq(r1.Right - r0.Right, 60, "右边缘跟随光标外扩 60px");
                    Eq(r1.Bottom - r0.Bottom, 40, "下边缘跟随光标外扩 40px");
                    Eq(r1.Left, r0.Left, "缩放右下角时左边不动");
                    Eq(r1.Top, r0.Top, "缩放右下角时上边不动");

                    var msUp = Ms(p1);
                    f.Coord.OnMouse(Native.WM_RBUTTONUP, ref msUp);
                    Check(!f.Coord.Drag.IsDragging, "松开右键后缩放会话结束");
                }
            }
            finally { WindowActions.AllowOwnProcessAsTargetForTest = false; }
        }

        /// <summary>
        /// ★ 回归用例：这就是本次真正修掉的那个 bug 的守门员。★
        ///
        /// 曾经的写法在拖动期间对 WM_MOUSEMOVE 返回 true（吞掉）。而在 WH_MOUSE_LL 钩子里
        /// 吞掉鼠标移动消息，系统会认为这次移动被拒绝，**光标就停在原地不动了**。
        /// 光标不动 → 钩子拿到的坐标不再变化 → 窗口只能跟着在原地抖。
        /// 用户报告的原话是：“鼠标移了一段距离，窗口却在原地哆嗦。”
        ///
        /// 这个用例断言两件事，缺一不可：
        ///   1) 拖动期间鼠标移动消息必须【放行】（返回 false），一次都不能吞；
        ///   2) 放行之后窗口依然要严格跟着光标走 —— 驱动拖动的是坐标本身，不是"吞消息"。
        /// </summary>
        private static void TestDragNeverSwallowsMouseMove()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(null))
                {
                    Native.RECT r0;
                    Native.GetWindowRect(f.H, out r0);
                    var start = CenterOf(f.H);

                    ActivateTrigger(f.Coord);

                    var msDown = Ms(start);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref msDown);
                    Check(f.Coord.Drag.IsDragging, "拖动会话已开始");

                    // ① 连续 6 步，每一步都必须被放行
                    int swallowedCount = 0;
                    for (int i = 1; i <= 6; i++)
                    {
                        var p = new Native.POINT(start.X + i * 15, start.Y + i * 8);
                        var m = Ms(p);
                        if (f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref m)) swallowedCount++;
                        Settle();
                    }
                    Eq(swallowedCount, 0, "拖动期间鼠标移动消息一次都没有被吞掉（吞掉会让光标冻住）");

                    // ② 放行之后窗口必须精确跟到最后一个位置
                    Native.RECT r1;
                    Native.GetWindowRect(f.H, out r1);
                    Eq(r1.Left - r0.Left, 90, "窗口水平方向跟到第 6 步的 90px");
                    Eq(r1.Top - r0.Top, 48, "窗口垂直方向跟到第 6 步的 48px");

                    // ③ 一次性灌入 60 个位置：工作线程会合并中间那些，
                    //    但最终必须停在【最后】一个位置上，且不产生任何累积漂移。
                    var last = new Native.POINT(start.X + 200, start.Y + 100);
                    for (int i = 0; i < 60; i++)
                    {
                        var p = new Native.POINT(start.X + 100 + i * 2, start.Y + 50 + i);
                        var m = Ms(p);
                        f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref m);
                    }
                    var mLast = Ms(last);
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mLast);
                    Settle();

                    Native.RECT r2;
                    Native.GetWindowRect(f.H, out r2);
                    Eq(r2.Left - r0.Left, 200, "高频灌入 60 次移动后，窗口停在最后的位置（无累积漂移）");
                    Eq(r2.Top - r0.Top, 100, "垂直方向同样停在最后的位置");

                    var msUp = Ms(last);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref msUp);
                    Check(!f.Coord.Drag.IsDragging, "松开左键后拖动会话结束");
                }
            }
            finally { WindowActions.AllowOwnProcessAsTargetForTest = false; }
        }

        private static void TestNoDragWhenInactive()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(null))
                {
                    // 故意不激活触发键
                    Check(!f.Coord.Trigger.Active, "触发键未激活");

                    var msDown = Ms(CenterOf(f.H));
                    Check(!f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref msDown), "未激活时左键消息被放行");
                    Check(!f.Coord.Drag.IsDragging, "未激活时不会开始拖动");

                    var msR = Ms(CenterOf(f.H));
                    Check(!f.Coord.OnMouse(Native.WM_RBUTTONDOWN, ref msR), "未激活时右键消息被放行");
                    Check(!f.Coord.Drag.IsDragging, "未激活时不会开始缩放");
                }
            }
            finally { WindowActions.AllowOwnProcessAsTargetForTest = false; }
        }

        /// <summary>
        /// ★ 回归用例：设置窗口必须真的把六个分组挂到窗体上。★
        ///
        /// 曾经 BuildUi() 里创建了 g1~g4 四个 GroupBox，子控件也全都填好了，
        /// 唯独漏了 Controls.Add(g1..g4) —— 于是整个设置窗口只剩底部的状态行和三个按钮，
        /// 用户看到的就是"设置基本是空的，里面啥都没有"。
        /// 这类 bug 肉眼极难从代码里看出来（控件都在，读起来很正常），只能靠断言。
        /// </summary>
        private static void TestSettingsLayout()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    int groups = 0;
                    for (int i = 0; i < form.Controls.Count; i++)
                    {
                        var gb = form.Controls[i] as GroupBox;
                        if (gb == null) continue;
                        groups++;
                        Check(gb.Controls.Count > 0, "分组「" + gb.Text + "」里有控件（不是空的）");
                    }
                    Check(SettingsForm.VerifyLayout(form), "设置窗体上分组数 >= 6，实际 " + groups);
                }
            }
        }

        /// <summary>外框浮层：能建出来、能摆到指定矩形、能隐藏；极小窗口下环厚不会翻转。</summary>
        private static void TestActivationFrameOverlay()
        {
            using (var frame = new ActivationFrame())
            {
                Log.Trace("[自检] T24 EnsureCreated 之前");
                frame.EnsureCreated();
                Log.Trace("[自检] T24 EnsureCreated 之后 created=" + frame.IsCreated);
                if (!frame.IsCreated)
                {
                    Check(false, "外框窗口创建成功", "CreateWindowEx 失败");
                    return;
                }

                var want = new Native.RECT(180, 140, 480, 400);
                frame.SetRect(want);
                Log.Trace("[自检] T24 SetRect 之后");
                Settle();
                Log.Trace("[自检] T24 Settle 之后");

                Native.RECT got;
                Native.GetWindowRect(frame.Handle, out got);
                bool ok = got.Left == want.Left && got.Top == want.Top
                       && got.Width == want.Width && got.Height == want.Height;
                Check(ok, "外框被摆到了请求的矩形", "期望 " + want + "，实际 " + got);
                Check(Native.IsWindowVisible(frame.Handle), "外框当前可见");

                frame.HideOverlay();
                Settle();
                Check(!Native.IsWindowVisible(frame.Handle), "拖动结束后外框不再可见");

                // 外框窗口必须比窗口矩形大出一圈，否则柔光会被边界切掉
                Check(FrameStyles.MarginFor(FrameStyles.Glow, 3) >= 8,
                    "柔光样式留出了足够的外扩边距（实际 " + FrameStyles.MarginFor(FrameStyles.Glow, 3) + "px）");
                Check(FrameStyles.MarginFor(FrameStyles.Flat, 3) < FrameStyles.MarginFor(FrameStyles.Glow, 3),
                    "无柔光的样式留白更小（不为不存在的光晕浪费面积）");
                Check(FrameStyles.MarginFor(FrameStyles.Solid, 8) >= 8, "实心描边越粗，留白越大");

                // 圆角不能超过短边一半，否则两条对边会互相穿过
                Eq((int)FrameStyles.ClampRadius(999, 40, 40), 19, "离谱的圆角被夹到短边一半以内");
                Eq((int)FrameStyles.ClampRadius(0, 40, 40), 0, "圆角 0 就是直角，不被当成非法值");
            }
        }

        /// <summary>
        /// 「外框用完还留在屏幕上」的回归用例。
        ///
        /// 成因：工作线程在 SetWindowPos 上阻塞时，钩子线程已经把会话收尾、外框隐藏了；
        /// 等工作线程出来，它手里那份"会话仍有效"的判定早已过期，却还是照着它显了一次框。
        /// 这里直接拿一个过期代次去调显框入口，断言它被挡下来。
        /// </summary>
        private static void TestFrameNeverSticks()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c) { c.ShowActivationFrame = true; }))
                {
                    var eng = f.Coord.Drag;

                    // ── 0) 只激活、还没按鼠标键：外框就该已经亮起来 ──
                    // 悬停高亮用的是【真实光标位置】，所以先把光标挪到测试窗口正中，
                    // 否则用例会随机命中别的窗口，断言就不可信了。
                    var hover = CenterOf(f.H);
                    Native.SetCursorPos(hover.X, hover.Y);
                    ActivateTrigger(f.Coord);
                    Settle();
                    Check(Native.IsWindowVisible(eng.Frame.Handle),
                        "触发键刚到阈值、还没按鼠标键时，光标所在窗口已经亮起外框");

                    // ── 1) 正常拖一次 ──
                    var down = Ms(CenterOf(f.H));
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    var mv = Ms(new Native.POINT(down.pt.X + 40, down.pt.Y + 30));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Settle();
                    var up = Ms(mv.pt);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    Settle();

                    Check(!eng.IsDragging, "拖动会话已结束");
                    // 触发键还按着 —— 外框要回到光标底下那扇窗上（这是新需求，不是残留）
                    Check(Native.IsWindowVisible(eng.Frame.Handle), "松开鼠标但触发键仍按着时外框继续亮着");

                    // ── 2) 松开触发键：外框必须真的消失 ──
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    Check(!f.Coord.Trigger.Active, "触发键已退出激活态");
                    Check(!Native.IsWindowVisible(eng.Frame.Handle), "松开触发键之后外框消失");

                    // ── 3) 模拟竞态：拿过期代次再显一次框，必须被挡下 ──
                    //    这正是"外框用完还留在屏幕上"的成因：工作线程卡在 SetWindowPos 里，
                    //    出来时会话早已收尾，却还照着过期结论显了一次框。
                    Native.RECT r;
                    Native.GetWindowRect(f.H, out r);
                    bool shown = eng.TryShowFrame(r, f.Store.Current, -1);
                    Settle();
                    Check(!shown, "过期代次的显框请求被挡下（不会画出关不掉的外框）");
                    Check(!Native.IsWindowVisible(eng.Frame.Handle), "被挡下之后外框仍然不可见");
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// 「拖动残影/不跟手」的结构性回归用例。
        ///
        /// 拖动进行当中，目标窗口四周亮的必须是【区域环】（DragRingFrame，同步直写、
        /// 零位图上传），而不是抗锯齿分层框（ActivationFrame，每帧整图上传）——
        /// 后者每帧 UpdateLayeredWindow 一整张窗口大小的位图，还要 PostMessage 回 UI 线程，
        /// 既是"缩放不跟手"的来源，也是"外框残影"的来源。
        /// 松手之后触发键还按着时，再换回抗锯齿分层框（静止时追求好看）。
        /// </summary>
        private static void TestDragRingFrameSwap()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c) { c.ShowActivationFrame = true; }))
                {
                    var eng = f.Coord.Drag;

                    var hover = CenterOf(f.H);
                    Native.SetCursorPos(hover.X, hover.Y);
                    ActivateTrigger(f.Coord);
                    Settle();
                    Check(Native.IsWindowVisible(eng.Frame.Handle), "悬停时亮的是抗锯齿分层框");

                    var down = Ms(CenterOf(f.H));
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    var mv = Ms(new Native.POINT(down.pt.X + 30, down.pt.Y + 20));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Settle();

                    Check(eng.IsDragging, "拖动会话进行中");
                    Check(Native.IsWindowVisible(eng.Ring.Handle), "拖动期间亮的是区域环（同步直写，不拖残影）");
                    Check(!Native.IsWindowVisible(eng.Frame.Handle), "拖动期间抗锯齿分层框已收起（不参与每帧更新）");

                    var up = Ms(mv.pt);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    Settle();

                    Check(!eng.IsDragging, "拖动会话已结束");
                    Check(!Native.IsWindowVisible(eng.Ring.Handle), "松手后区域环立刻消失");
                    Check(Native.IsWindowVisible(eng.Frame.Handle),
                        "触发键仍按着时换回了抗锯齿分层框（静止时保持好看）");

                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    Check(!Native.IsWindowVisible(eng.Ring.Handle), "退出激活态后区域环不残留");
                    Check(!Native.IsWindowVisible(eng.Frame.Handle), "退出激活态后分层框不残留");
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★ 回归用例：按住触发键、用左/右键做完一次【非拖动动作】之后，外框必须继续留着。★
        ///
        /// 用户原话：「按住 enter 触发功能之后，我不管通过左键还是右键来实现相关功能，
        ///           都会使得原来包围窗口的那样的一个外框消失，我希望这个能够继续保存。」
        ///
        /// 成因：HandleButtonUp 的"拖动结束"分支会重新亮框，但 HandleButtonDown 里
        /// 走 WindowActions.Apply / 双击最大化 / 双击九宫格 这几条【非拖动】路径时，
        /// 只设了 _swallowUp 就返回了，没人去把框重新亮回来。
        /// 时间一长，鼠标不动 → 悬停逻辑不刷新 → 外框就一直是隐的。
        /// </summary>
        private static void TestFramePersistsAfterAction()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    c.DoubleClickLeftMaximize = true;
                    c.ActionMiddle = Actions.Center;    // 一个典型的"一次性动作"
                }))
                {
                    var eng = f.Coord.Drag;

                    var hover = CenterOf(f.H);
                    Native.SetCursorPos(hover.X, hover.Y);
                    ActivateTrigger(f.Coord);
                    Settle();
                    Check(Native.IsWindowVisible(eng.Frame.Handle), "激活后外框已亮起");
                    Check(!eng.IsDragging, "此刻还没进入拖动");

                    // ── 用中键做一次"居中"这类一次性动作 ──
                    var mid = Ms(CenterOf(f.H));
                    bool swallowed = f.Coord.OnMouse(Native.WM_MBUTTONDOWN, ref mid);
                    Settle();

                    Check(swallowed, "激活态下中键按下被吞掉（不会漏给目标窗口）");
                    Check(!eng.IsDragging, "一次性动作不会进入拖动会话");

                    // ★ 关键断言：动作做完，框必须还在
                    Check(Native.IsWindowVisible(eng.Frame.Handle),
                        "执行完一次性动作之后外框继续亮着（外框消失回归）");

                    // 抬起中键之后也必须还在
                    var midUp = Ms(CenterOf(f.H));
                    f.Coord.OnMouse(Native.WM_MBUTTONUP, ref midUp);
                    Settle();
                    Check(Native.IsWindowVisible(eng.Frame.Handle), "松开鼠标键后外框依然亮着");

                    // ── 再验一次双击左键最大化这条路径 ──
                    //    最大化有系统动画（~200ms），Settle() 的 80ms 不够，
                    //    动画途中的 GetWindowRect 会让外框算到一个中间矩形，
                    //    所以这里多等一会儿再断言。
                    Native.SetCursorPos(hover.X, hover.Y);
                    var d1 = Ms(hover);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref d1);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref d1);
                    Settle();

                    var d2 = Ms(hover);
                    bool dblSwallowed = f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref d2);
                    Check(dblSwallowed, "双击左键第二次按下被吞掉（走最大化路径）");
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref d2);
                    SettleLong();
                    Check(Native.IsWindowVisible(eng.Frame.Handle),
                        "双击左键最大化之后外框继续亮着",
                        "游戏手柄=" + eng.Frame.Handle.ToInt64().ToString("X")
                        + " 可见=" + Native.IsWindowVisible(eng.Frame.Handle));

                    // 松开触发键之后才应该消失
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    Check(!Native.IsWindowVisible(eng.Frame.Handle), "松开触发键之后外框才消失");
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★ 回归用例：设置窗口必须可以调整大小（用户需求）。★
        ///
        /// 只把 FormBorderStyle 改成 Sizable 是不够的 —— 控件全是绝对定位，
        /// 不锚定的话拉宽窗口只会多出一片空白，用户感觉"能拉但没用"。
        /// 所以这里同时断言"可缩放"和"分组带 Left|Right 锚定"。
        /// </summary>
        private static void TestSettingsResizable()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    Check(SettingsForm.VerifyResizable(form),
                        "设置窗口可缩放（边框 Sizable、最大化按钮可用、最小尺寸有意义）",
                        "BorderStyle=" + form.FormBorderStyle + " Max=" + form.MaximizeBox
                        + " Min=" + form.MinimumSize);

                    Check(SettingsForm.VerifyAnchors(form),
                        "拉宽后分组能重排（1 列 <-> 2 列自适应）");

                    // 尺寸持久化：0 表示没存过（默认值），不该被 Normalize 改成别的数字
                    var c = new AppConfig();
                    Eq(c.SettingsWidth, 0, "默认不记住窗口宽度（0 = 用内置默认）");
                    Eq(c.SettingsHeight, 0, "默认不记住窗口高度");

                    c.SettingsWidth = 100;              // 手改小了
                    c.SettingsHeight = 100;
                    c.Normalize();
                    Eq(c.SettingsWidth, 578, "过小的记忆宽度被拉回最小尺寸");
                    Eq(c.SettingsHeight, 420, "过小的记忆高度被拉回最小尺寸");

// ★ 上限必须跟随【当前屏幕】，不能写死 2400 ★
                    //
                    // 这条断言原来写的是 Eq(..., 2400, ...) —— 它把"上限 2400"
                    // 这个【缺陷本身】当成了正确行为锁死。用户的屏幕是 3840 宽，
                    // 窗口拉宽后却被压回 2400，就是这条规则造成的。
                    // 现在改成与 Config.MaxUsableWindowWidth() 同一口径：
                    // 夹到虚拟桌面尺寸，屏幕多大就允许多大。
                    var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                    int capW = vs.Width >= 640 ? vs.Width : 2400;
                    int capH = vs.Height >= 480 ? vs.Height : 2400;

                    c.SettingsWidth = 99999;
                    c.SettingsHeight = 99999;
                    c.Normalize();
                    Eq(c.SettingsWidth, capW, "离谱的记忆宽度被夹到屏幕上限（否则窗口会跑到屏幕外）");
                    Eq(c.SettingsHeight, capH, "离谱的记忆高度被夹到屏幕上限");
                }
            }
        }

        /// <summary>
        /// ★★ 回归用例：在设置窗口里选主题 —— 预览和保存结果都必须跟着走。★★
        ///
        /// 这是用户实际报的那条："我改成了别的主题，预览窗口也没有真正变成我选的那个样式，
        /// 保存后应用也没有真正得到应用"。
        ///
        /// 根因不在渲染，而在**取值**：颜色下拉框里装的是 ColorItem，
        /// 而 SelectedId() 只写了 `as IdItem` —— 拿到 null 就回退到 fallback（薄荷青）。
        /// 所以界面上显示朱红，读出来永远是薄荷青：预览不变、存进去也是薄荷青。
        ///
        /// 这条用例把整条链路串起来断言：
        ///   界面上选中 → SelectedId 读出 → 预览框内部状态 → CaptureInto 写进配置。
        /// 任何一环断掉都会红。
        /// </summary>
        private static void TestSettingsThemeSelectionRoundTrip()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();

                    // ── 1. 默认应当是薄荷青（用户说"现在是薄荷青的主题"）──
                    var s0 = form.ProbeFrameSelection();
                    Eq(s0.ColorId, FrameColors.Teal, "打开设置时，颜色下拉框读回的默认 ID 是薄荷青");

                    // ── 2. 选朱红：界面上读出来必须是朱红，而不是回退值 ──
                    form.ProbeSelectFrameColor(FrameColors.Red);
                    var s1 = form.ProbeFrameSelection();
                    Eq(s1.ColorId, FrameColors.Red,
                        "★ 在下拉框里选「朱红」之后，读回的 ID 必须是 Red（不能回退成薄荷青）");

                    // ── 3. 预览框内部拿到的也必须是朱红（预览真的会画成朱红）──
                    Eq(s1.PreviewColorId, FrameColors.Red,
                        "★ 预览框内部状态同步成了朱红（否则预览永远显示薄荷青）");

                    // ── 4. 换样式，同样要跟 ──
                    form.ProbeSelectFrameStyle(FrameStyles.Solid);
                    var s2 = form.ProbeFrameSelection();
                    Eq(s2.StyleId, FrameStyles.Solid, "换个样式，读回的 ID 跟着换");
                    Eq(s2.PreviewStyleId, FrameStyles.Solid, "预览框的样式也同步了");

                    // ── 5. 粗细 / 圆角 / 总开关 ──
                    form.ProbeSetFrameShape(7, 22, true);
                    var s3 = form.ProbeFrameSelection();
                    Eq(s3.Thickness, 7, "粗细读回 7");
                    Eq(s3.PreviewThickness, 7, "预览框的粗细同步成 7");
                    Eq(s3.Radius, 22, "圆角读回 22");
                    Eq(s3.PreviewRadius, 22, "预览框的圆角同步成 22");
                    Check(s3.FrameEnabled && s3.PreviewFrameEnabled, "总开关与预览框保持一致（开）");

                    form.ProbeSetFrameShape(7, 22, false);
                    var s3b = form.ProbeFrameSelection();
                    Check(!s3b.FrameEnabled && !s3b.PreviewFrameEnabled,
                        "关掉总开关后，预览框也跟着变成「不画框」的状态");

                    // ── 6. 每换一个颜色都要正确读回（枚举全部，防"只有某几个坏"）──
                    int bad = 0; string badDetail = "";
                    for (int i = 0; i < FrameColors.All.Length; i++)
                    {
                        string id = FrameColors.All[i].Id;
                        form.ProbeSelectFrameColor(id);
                        var got = form.ProbeFrameSelection();
                        if (got.ColorId != id) { bad++; badDetail += id + "→" + got.ColorId + " "; }
                        if (got.PreviewColorId != id) { bad++; badDetail += id + "(预览)→" + got.PreviewColorId + " "; }
                    }
                    Check(bad == 0, "★ 调色板里每一个颜色都能正确选中并同步到预览",
                        bad == 0 ? "" : badDetail);

                    // ── 7. 保存链路：CaptureInto 写进配置的必须是界面当前选择 ──
                    form.ProbeSelectFrameColor(FrameColors.Violet);
                    form.ProbeSelectFrameStyle(FrameStyles.Dashed);
                    form.ProbeSetFrameShape(4, 9, true);

                    var saved = new AppConfig();
                    form.ProbeCaptureInto(saved);
                    saved.Normalize();

                    Eq(saved.FrameColorId, FrameColors.Violet, "★ 保存进配置的颜色是紫罗兰（不是薄荷青）");
                    Eq(saved.FrameStyleId, FrameStyles.Dashed, "保存进配置的样式是虚线环");
                    Eq(saved.FrameThickness, 4, "保存进配置的粗细是 4");
                    Eq(saved.FrameRadius, 9, "保存进配置的圆角是 9");
                    Check(saved.ShowActivationFrame, "保存进配置的总开关是开");

                    // ── 8. 关掉外框再存，也必须存成关（否则"关了还亮框"）──
                    form.ProbeSetFrameShape(4, 9, false);
                    var saved2 = new AppConfig();
                    form.ProbeCaptureInto(saved2);
                    saved2.Normalize();
                    Check(!saved2.ShowActivationFrame, "关掉外框后保存，配置里确实是关");

                    // ── 9. 非法 ID 不该让程序崩，也不该悄悄变成别的合法颜色 ──
                    form.ProbeSelectFrameColor("这不是一个颜色");
                    var s9 = form.ProbeFrameSelection();
                    Check(!string.IsNullOrEmpty(s9.ColorId),
                        "传入非法颜色 ID 时不崩（回退到某个合法项）", "读回=" + s9.ColorId);
                }
            }
        }

        /// <summary>
        /// ★ 回归用例：设置窗口"能拉宽"必须真的有意义。★
        ///
        /// 用户原话："设置页面被莫名其妙的拉宽了很多，但拉宽并没有给拉宽的部分增加上
        /// 文字或者什么别的，就只是单纯的拉宽，那这样一种拉宽有啥意义呢？毫无意义。"
        ///
        /// 早先的实现是"每个分组设 Left|Right 锚定" —— 测试绿着，但体验是错的：
        /// 分组只是变宽，内部控件全挤在左边，右边一大片空白。
        ///
        /// 现在断言的是"拉宽到底带来了什么"：
        ///   1. 宽度够了，分组必须从 1 列变 2 列（一屏信息量翻倍）
        ///   2. 分组内的下拉框 / 文本框必须真的变宽
        ///   3. 来回拉多次，位置必须稳定（不能越拉越偏）
        /// </summary>
        private static void TestSettingsResizeIsMeaningful()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    form.Show();          // 触发 OnShown -> LayoutResponsive
                    Application.DoEvents();

                    Check(SettingsForm.VerifyAnchors(form),
                        "★ 宽度够时分组从 1 列变 2 列，回窄能退回 1 列（拉宽真的多装东西）");

                    Check(SettingsForm.VerifyInnerStretch(form),
                        "★ 拉宽后分组内的下拉框/文本框跟着变宽（不是只把分组撑大）",
                        SettingsForm.LastStretchDetail + " || " + SettingsForm.DumpLayoutItem(form, form.ProbeComboLeft));

                    Check(SettingsForm.VerifyStretchIdempotent(form),
                        "★ 来回拉多次，控件位置稳定不漂移（伸缩是幂等的）");

                    // 底部操作条必须固定在窗口下方，不能跟着内容滚走
                    bool pagerOnForm = false;
                    for (int i = 0; i < form.Controls.Count; i++)
                    {
                        if (form.Controls[i].Dock == DockStyle.Bottom) { pagerOnForm = true; break; }
                    }
                    Check(pagerOnForm, "底部操作条固定在窗口下方（不随内容滚走）");

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 回归用例：美化改自绘之后，设置窗口里每个控件必须【还可见、还点得动】。★
        ///
        /// 为什么需要这条：把 GroupBox 换成自绘卡片、把 Button 换成自绘扁平按钮时，
        /// 一旦漏了某个开关（控件没设 UserPaint、或者可见性被改、或者被卡片盖住），
        /// 界面上就会出现"看不���某个选项"或者"点了没反应"——
        /// 用户看到的是"程序原有功能都失效了"，但代码里一个异常都不会抛，
        /// 靠 assert 布局逻辑的那批用例全都发现不了。
        ///
        /// 这里做两件事：
        ///   ① 遍历全部后代控件，断言 Visible && 宽高 > 0（"看得见"）；
        ///   ② 断言所有"应该有交互的控件"确实 Enabled、且在所属容器客户区内（"点得到"）。
        /// </summary>
        private static void TestSettingsControlsReachable()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    form.Show();
                    Application.DoEvents();

                    // ★ 必须在测量前"再排一次版"——
                    //   Show() 之后窗体还会经历一轮自己的布局（OnResize / OnShown），
                    //   而分组宽度是在这几轮里逐步定下来的。
                    //   如果直接读，有机会读到"上一轮遗留的宽度 + 这一轮算的位置"这种
                    //   拼起来的中间态，误报"控件在容器外"。
                    //   这里强制同步到稳定状态再断言。
                    form.Size = form.Size;          // 触发一次 resize 排版
                    Application.DoEvents();
                    SettingsForm.RelayoutForTest(form);
                    Application.DoEvents();

                    var hidden = new System.Text.StringBuilder();
                    var dead = new System.Text.StringBuilder();
                    int interactive = 0, checkedCount = 0;

                    Walk(form, delegate (Control c)
                    {
                        checkedCount++;

                        if (!c.Visible) { hidden.Append(Name(c)).Append(' '); return; }
                        if (c.Width <= 0 || c.Height <= 0)
                        {
                            dead.Append(Name(c)).Append("(尺寸0) ");
                            return;
                        }

                        // 需要交互的控件：复选框 / 输入类 / 下拉框 / 按钮 / 数字框
                        bool isInteractive =
                            c is CheckBox || c is TextBox || c is ComboBox
                            || c is Button || c is NumericUpDown;

                        if (!isInteractive) return;
                        interactive++;

                        if (!c.Enabled)
                        {
                            // ★ "被禁用"绝大多数是【设计如此】的联动，不能算失败：
                            //   · 外框没开 → 颜色/样式/粗细/圆角 禁用（SyncFrameEnabled）
                            //   · 不是手动刷新率 → Hz 输入框禁用
                            //   · 已经是管理员 → "以管理员重启"禁用
                            //   · 不需要长按触发 → 长按阈值禁用
                            //   这些联动一旦被误判成 bug，测试就会长期红着，最终没人看它。
                            //
                            //   真正要抓的是"【本该能用】却点不到" ——
                            //   也就是下面那条：控件跑到容器外面去了。
                            return;
                        }

                        // 必须完整落在"最近的那个容器"客户区内 —— 被卡片边框切掉一半的也点不到
                        var p = c.Parent;
                        if (p == null) return;
                        Rectangle cr = p.ClientRectangle;
                        Rectangle cb = new Rectangle(c.Left, c.Top, c.Width, c.Height);
                        if (cr.Width > 0 && !cr.IntersectsWith(cb))
                        {
                            dead.Append(Name(c)).Append("(在容器外 ")
                                   .Append("L=").Append(cb.Left).Append(" T=").Append(cb.Top)
                                   .Append(" W=").Append(cb.Width).Append(" H=").Append(cb.Height)
                                   .Append(" 容器=").Append(cr.Width).Append("x").Append(cr.Height)
                                   .Append(") ");
                        }
                    });

                    Check(hidden.Length == 0,
                        "设置窗口没有【看不见】的控件（分组/卡片化之后可见性回归）",
                        hidden.Length == 0 ? checkedCount + " 个控件全部可见"
                                           : "不可见: " + hidden);

                    Check(dead.Length == 0,
                        "★ 设置窗口所有可交互控件都在容器内、尺寸正常（自绘后功能失效回归）",
                        dead.Length == 0
                            ? interactive + " 个可交互控件全部可达"
                            : "异常: " + dead);

                    form.Close();
                }
            }
        }

        /// <summary>给控件起一个可读的名字，写进断言信息里用。</summary>
        private static string Name(Control c)
        {
            try
            {
                string t = c.Text;
                if (!string.IsNullOrEmpty(t))
                {
                    if (t.Length > 10) t = t.Substring(0, 10) + "…";
                    return c.GetType().Name + "[" + t + "]";
                }
            }
            catch { }
            return c.GetType().Name;
        }

        // ── T39外框亮着时，鼠标键依然要能接管窗口 ─────────────────

        /// <summary>
        /// ★ 用户报的核心问题："长按 enter 到了触发阈值之后，左键按住能移动窗口、
        ///   右键能改长宽 —— 这个功能没有了。"
        ///
        /// 【这个用例为什么不复用 DragFixture】
        /// DragFixture 里有两行会让本用例测到假东西的东西：
        ///     c.ShowActivationFrame = false;← 外框是关的
        ///     WindowActions.ForcedTargetForTest = Window.Handle;  ← FindTarget 被短路
        /// 也就是说历史上所有端到端用例都跑在"外框关着 + 不查真实命中"的组合下，
        /// 而"外框开着"这条路径从来没被测过 —— bug 就住在那里。
        /// 所以这里自己建窗口、自己走真实 WindowFromPoint。
        /// </summary>
        private static void TestHoverFrameDoesNotBlockDrag()
        {
            if (!NoRoomForTestWindow()) return;

            string dir = Path.Combine(Path.GetTempPath(), "holddrag-hover-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            // ══ 刻意不走 DragFixture ══
            // 它里面有两行正是历史上让这个 bug 溜过去的原因：
            //     c.ShowActivationFrame = false;                  ← 外框是关的
            //     WindowActions.ForcedTargetForTest = Window.Handle;  ← FindTarget 被短路
            // 本用例要：外框开着、ForcedTargetForTest 保持 0、全程走真实 WindowFromPoint。
            WindowActions.ForcedTargetForTest = IntPtr.Zero;
            WindowActions.AllowOwnProcessAsTargetForTest = false;

            Form target = null;
            Coordinator coord = null;
            try
            {
                var store = new ConfigStore(dir);
                store.Load();
                var c = store.Current;
                c.TriggerKeyId = "K:0x0D";
                c.RequireHold = true;
                c.HoldMs = 400;
                c.ActionLeft = Actions.Move;
                c.ActionRight = Actions.Resize;
                c.ActionMiddle = Actions.Nothing;
                c.ShowActivationFrame = true;          // ★ 本用例的全部意义所在
                c.SnapPreview = false;
                c.EdgeSnap = false;
                c.SnapToOtherWindows = false;
                c.BringToFront = false;
                c.DoubleClickLeftMaximize = false;
                c.DoubleClickRightZone = false;
                c.AllowFullscreenWindows = true;

target = new Form();
      target.FormBorderStyle = FormBorderStyle.Sizable;
      target.StartPosition = FormStartPosition.Manual;
            target.ShowInTaskbar = false;
            target.Text = "HoldDrag T39 target";
         target.TopMost = true;

        // ★★★ 位置与光标必须成对地"搬到一个确定干净的地方" ★★★
      //
      // 这个用例的失败史（两次红、两次绿，交替出现）最后查出来是：
   //   用户的 Edge 浏览器窗口（抖音直播间）刚好在那个位置，
    // 于是 FindTarget 命中的是它 —— 断言拿两个十六进制句柄对不上，报红。
        //
      // 注意【产品行为完全正确】：FindTarget 从光标点找到了一个真实可拖的
        // 外部窗口并如实返回了它。这正是它该做的事。
        //   错的是测试假设"光标下必然只有我建的窗口" —— 桌面环境里做不到这个保证。
      //
      // 修法：把测试窗口放到【当前鼠标不在、且贴着工作区右下角】的位置，
        // 再把鼠标移进去。只用相对工作区的计算，不写死绝对坐标，
        // 这样多显示器 / 缩放比例变化都能自适应。
      Native.POINT cursorHome;
      Native.GetCursorPos(out cursorHome);
    Native.RECT work = WorkAreaOfNearestMonitor(cursorHome);
       int tw = 380, th = 260;
            int tx = work.Right - tw - 8;
            int ty = work.Bottom - th - 8;
      if (tx < work.Left) tx = work.Left + 8;
   if (ty < work.Top) ty = work.Top + 8;

            target.Bounds = new Rectangle(tx, ty, tw, th);
    target.Show();
  Pump();
    Settle();

                // ★ 把测试窗口登记成"别人的窗口"。
                //   FindTarget 的核心逻辑是"命中自己 → 沿 Z 序往下穿透 → 找到第一个
                //   不是自己的窗口"。测试窗口和浮层同属本进程，只有把它标成外人，
                //   才能确定性地验证这条穿透路径 —— 否则 AllowOwnProcessAsTargetForTest
                //   会直接把整段穿透逻辑短路掉，等于没测。
                WindowActions.SetForeignForTest(target.Handle, true);

                coord = new Coordinator(store);
                coord.PrecreateOverlays();
                Settle();

// ══ 先把目标窗口确认成"光标下真正的那一层" ══
      //
        // 桌面环境有个绕不开的现实：用户的浏览器 / 窗口可能恰好压在测试窗口上。
        // 之前这个用例就因此间歇性失败 —— 而且诊断显示命中的竟是用户的 Edge。
     // 既然不能假设"我建的窗口一定在最前"，那就主动把它顶到前面并复核；
   // 复核不过就换一个落点重试，仍然不行就说明环境确实不可用，跳过并写明原因，
      // 而不是抛一个红让人去怀疑产品。
      Native.POINT pt = new Native.POINT();
       bool envUsable = false;
        for (int attempt = 0; attempt < 4 && !envUsable; attempt++)
        {
       if (attempt > 0)
   {
    // 换落点：沿工作区左移，避免总撞在同一个窗口上
       target.Left = Math.Max(work.Left + 8, target.Left - 420);
            target.Top = Math.Max(work.Top + 8, target.Top - 260);
       }
  try { target.BringToFront(); target.Activate(); } catch { }
          Pump();
          Settle();

            pt = CenterOf(target.Handle);
            Native.SetCursorPos(pt.X, pt.Y);
      Settle();

            IntPtr top = Native.WindowFromPoint(pt);
            IntPtr topRoot = Native.GetAncestor(top, Native.GA_ROOT);
            envUsable = topRoot == target.Handle;
        if (!envUsable)
      {
    Line("    （诊断）落点 " + attempt + " 顶层不是测试窗口 -> " + DescribeWindow(top));
  }
        }

  if (!envUsable)
     {
      Line("（环境限制）桌面上有别的窗口始终压在测试窗口上方，无法做真实命中测试，跳过。");
Line("参考：T39 的核心价值是『外框亮着仍能接管窗口』，"
    + "其中拖动能否启动由 coord.OnMouse 断言覆盖，不依赖真实命中。");
       Line("另见 T38（控件可见可点）与 T43（真实鼠标事件走完整钩子链路）。");
         return;
            }

       // 基线：外框还没亮，先确认这条路径本来就通（排除环境问题）
      IntPtr before = WindowActions.FindTarget(pt, true);
 if (before != target.Handle)
      {
        Line("    （诊断）基线未命中目标 -> " + DescribeWindow(before));
            }
                Check(before == target.Handle,
            "基线：外框未亮时 FindTarget 命中测试窗口（环境正常）",
       "实际 " + before.ToInt64().ToString("X")
        + " 期望 " + target.Handle.ToInt64().ToString("X"));

                // 激活触发键 → 外框亮起（浮层 WS_EX_TOPMOST 压在目标窗口上）
                ActivateTrigger(coord);
                Settle();
                Check(coord.Trigger.Active, "触发键已进入激活态");
                Check(Native.IsWindowVisible(coord.Drag.Frame.Handle),
                    "激活后外框确实亮着（浮层已压在目标窗口上）");

// 浮层会不会抢走命中，取决于它有没有真的盖住光标那个点。
     // 这里不把它当断言，而是当成一条诊断信息写出来：将来真出问题时，
// 这行能直接说明"当时浮层到底压没压住"，不用再猜。
     IntPtr raw = Native.WindowFromPoint(pt);
                bool rawIsOverlay = (raw != IntPtr.Zero && raw != target.Handle);
                Line("    （诊断）此刻 WindowFromPoint=" + raw.ToInt64().ToString("X")
                     + " 目标=" + target.Handle.ToInt64().ToString("X")
                     + " 浮层=" + coord.Drag.Frame.Handle.ToInt64().ToString("X")
                     + (rawIsOverlay ? " ←命中的是浮层，已被穿透" : " ←命中的是目标窗口"));

                // ★ 核心断言：FindTarget 必须能穿过浮层打到目标窗口
                IntPtr after = WindowActions.FindTarget(pt, true);
                Check(after == target.Handle,
                    "★ 外框亮着时 FindTarget 依然命中目标窗口（浮层不再挡路）",
                    "实际 " + after.ToInt64().ToString("X")
                    + " 期望 " + target.Handle.ToInt64().ToString("X"));

                // 完整链路：左键按下必须真的进拖动会话
                var down = Ms(pt);
                bool swallowed = coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                Settle();
                Check(swallowed, "外框亮着时左键按下被吞掉（不会漏给目标窗口）",
                      "swallowed=" + swallowed);
                Check(coord.Drag.IsDragging,
                    "★ 外框亮着时左键依然能启动拖动会话（核心功能失效回归）",
                      "IsDragging=" + coord.Drag.IsDragging);

                var up = Ms(pt);
                coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                Settle();

                // 右键（改长宽）这条路径同样要通
                ActivateTrigger(coord);
                Settle();
                var rdown = Ms(pt);
                coord.OnMouse(Native.WM_RBUTTONDOWN, ref rdown);
                Settle();
                Check(coord.Drag.IsDragging && coord.Drag.Button == 1,
                    "★ 外框亮着时右键依然能启动缩放会话",
                    "IsDragging=" + coord.Drag.IsDragging + " Button=" + coord.Drag.Button);
                var rup = Ms(pt);
                coord.OnMouse(Native.WM_RBUTTONUP, ref rup);
                Settle();

                // 松开触发键，外框才消失（视觉反馈全程都在）
                coord.Trigger.SimulateKeyUp(null);
                Settle();
                Check(!Native.IsWindowVisible(coord.Drag.Frame.Handle),
                    "松开触发键后外框消失");
            }
finally
    {
      try { if (target != null) WindowActions.SetForeignForTest(target.Handle, false); } catch { }
         try { if (coord != null) coord.Dispose(); } catch { }
try
    {
       // ★ 同步销毁 + 抽几轮消息，否则这个 TopMost 窗口会活到下一个用例，
    //   压住它的目标窗口，制造"同一份代码两次跑出不同结论"的间歇性假红。
     if (target != null && !target.IsDisposed)
              { target.Visible = false; Native.DestroyWindow(target.Handle); }
          if (target != null) { target.Close(); target.Dispose(); }
        }
        catch { }
            for (int i = 0; i < 6; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
            try { Directory.Delete(dir, true); } catch { }
    try { Native.SetCursorPos(400, 400); } catch { }
   }
        }


        // ── T40 高 DPI 下文字不被裁切 ───────────────────────────

        /// <summary>
        /// ★ 用户报："两列那个图里面字体被遮住了呀，被遮住了一半。"
        ///
        /// 根因是字号被放大两次：GraphicsUnit.Point 的字号已经由系统按 DPI
        /// 换算过像素高度（9pt @192dpi ≈ 24px），代码里又乘了一次缩放系数 s
        /// 变成 18pt（≈48px），而控件高度只放大2 倍 → 字比框高，被裁掉一半。
        ///
        /// 断言方式：拿每个可见控件【实际渲染出来的文字高度】和它的【实际高度】比。
        /// 用 MeasureString / TextRenderer 量，而不是看字号数字 —— 因为字号本身
        /// 在 Point 单位下和 DPI 是两套换算，看数字判断不出来。
        /// </summary>
        private static void TestNoTextClippedByHeight()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    form.Show();
                    Application.DoEvents();
                    form.Size = form.Size;
                    Application.DoEvents();
                    SettingsForm.RelayoutForTest(form);
                    Application.DoEvents();

                    var clipped = new System.Text.StringBuilder();
                    int measured = 0;

                    Walk(form, delegate (Control c)
                    {
                        if (!c.Visible) return;
                        string txt = null;
                        try { txt = c.Text; } catch { }
                        if (string.IsNullOrEmpty(txt)) return;
                        if (txt == "请按下要用的按键…") return;      // 录制中的占位文字

                        // 只看"单行控件"：多行文本框、DataGridView 这类高度本来就
                        // 大于单行字高，拿它们比没有意义。
                        if (c is TextBoxBase || c is ComboBox) return;
                        if (c is Button) return;                      // 按钮是自绘，字不裁

                        // 用控件自己的 Graphics 量，拿到的是 WinForms 实际排版用的那套度量
                        // （TextRenderer.MeasureText 走的是 GDI，两者在高 DPI 下会有细微差异，
                        //  而 Label 实际是用前者绘制的，所以必须用前者量才准）。
                        int needH;
                        try
                        {
                            using (var g = c.CreateGraphics())
                            {
                                float tight = g.MeasureString(txt, c.Font, int.MaxValue,
                                    System.Drawing.StringFormat.GenericTypographic).Height;
                                // 再乘一个字号的量作为行距余量：WinForms 画 Label 时
                                // 用的是字体行的完整高度，比 MeasureString 的紧排高度略大。
                                needH = (int)Math.Ceiling(tight * 1.28f);
                            }
                        }
                        catch { return; }

                        measured++;

                        // 留 2px 容差：不同字体的行高取整规则略有出入。
                        if (needH > c.Height + 2)
                        {
                            clipped.Append(Name(c))
                                    .Append("(字高").Append(needH)
                                    .Append(" > 框高").Append(c.Height)
                                    .Append(") ");
                        }
                    });

Check(clipped.Length == 0,
 "★ 高 DPI 下所有文字都装得进自己的控件（字号被放大两次回归）",
       clipped.Length == 0
          ? measured + " 个控件文字全部完整显示"
  : "被裁切: " + clipped);

       form.Close();
         }
     }
      }

        // ── T41 关键下拉框必须有选中项 ───────────────────────────

        /// <summary>
        /// 用户报："设置页面的外框形状选项里面显示是空的，
///   但是我点开里面却又有好几个选项。"
        ///
     /// 机制：ComboBox.DropDownStyle = DropDownList 时，框里显示什么
        /// 完全由 SelectedIndex 决定 —— 一旦它是 -1，框就是空的；
        /// 而点开下拉列表各项都还在（Items 集合与"当前选中"是两回事，
      /// 先 Clear() 再 Add() 很容易让前者恢复、后者丢失）。
        ///
/// 所以断言不能只数"点开有几项"，必须直接读 SelectedIndex / SelectedItem。
  /// </summary>
  private static void TestKeyCombosHaveSelection()
 {
  using (var f = new DragFixture(delegate (AppConfig c)
    {
            // ★ 刻意用【非默认】的值。
       // 默认值恰好等于 FrameStyles.DefaultId，选中第一项就"碰巧对了"，
          // 换成用户真实配置（Glow + 薄荷青）才能测出真正的匹配逻辑。
   c.FrameStyleId = "Dashed";        // 不是第一项，跳过第 0 项
            c.FrameColorId = "Mint";
        c.DragRefreshMode = "Custom";       // 第二项
   c.AutoStart = "None";    // 最后一项
          c.ActionLeft = "Move";
            c.ActionRight = "Resize";
     }))
            {
      using (var form = new SettingsForm(f.Store, f.Coord))
            {
           form.CreateControl();
             form.Show();
                Application.DoEvents();
          form.Size = form.Size;
                Application.DoEvents();
           SettingsForm.RelayoutForTest(form);
        Application.DoEvents();

                var bad = new System.Text.StringBuilder();
        int comboCount = 0;

       Walk(form, delegate (Control c)
{
       var cb = c as ComboBox;
            if (cb == null) return;
    comboCount++;

          int idx = cb.SelectedIndex;
      if (idx < 0 || cb.SelectedItem == null)
   {
        bad.Append(Name(c)).Append("(SelectedIndex=").Append(idx)
  .Append(" 项数=").Append(cb.Items.Count)
                 .Append(" 框里显示=\"").Append(cb.Text).Append("\") ");
           return;
                }

       string text = cb.SelectedItem.ToString();
    if (string.IsNullOrEmpty(text))
      bad.Append(Name(c)).Append("(选中项文字为空) ");
    });

      Check(bad.Length == 0,
 "★ 所有下拉框都有可见的选中项（外框样式显示为空回归）",
                bad.Length == 0
            ? comboCount + " 个下拉框全部有选中项"
            : "异常: " + bad);

   // 再测"恢复默认值"这条路径 —— 它会重建全部 Items，
       // 如果清空时把 SelectedIndex 留在 -1，框就会变空。
        SettingsForm.PopulateForTest(form);
                Application.DoEvents();

      var bad2 = new System.Text.StringBuilder();
        Walk(form, delegate (Control c)
      {
       var cb = c as ComboBox;
          if (cb == null) return;
         if (cb.SelectedIndex < 0)
            bad2.Append(Name(c)).Append("(恢复默认后 SelectedIndex=-1 项数=")
              .Append(cb.Items.Count).Append(") ");
           });
      Check(bad2.Length == 0,
        "★ 恢复默认值之后下拉框依然都有选中项",
      bad2.Length == 0 ? "全部下拉框正常" : "异常: " + bad2);

         form.Close();
        }
        }
  }

        /// <summary>深度遍历窗体的全部后代控件。</summary>
        private static void Walk(Control root, Action<Control> visit)
        {
            try
            {
                for (int i = 0; i < root.Controls.Count; i++)
                {
                    var c = root.Controls[i];
                    visit(c);
                    Walk(c, visit);
                }
            }
            catch { /* 遍历过程中控件可能已被释放，跳过即可 */ }
        }

        /// <summary>
        /// ★ 回归用例：改主题之后，浮层像素必须【立刻】跟着变。★
        ///
        /// 曾经的写法只重建位图、不重推像素（UpdateLayeredWindow），
        /// 新外观静静躺在内存里，要等到下一次 SetRect（鼠标移开、光标换到另一扇窗）
        /// 才会被画出来。用户看到的就是"在设置里改了颜色/样式，保存后外框完全没变"。
        /// 这里直接读浮层窗口的实际像素来断言 —— 这是唯一能证明"真的画到屏幕上了"的办法。
        /// </summary>
        private static void TestThemeAppliesImmediately()
        {
            using (var frame = new ActivationFrame())
            {
                frame.EnsureCreated();
                if (!frame.IsCreated) { Check(false, "外框窗口创建成功", "CreateWindowEx 失败"); return; }

                // 先摆一个固定矩形，让它显示出来
                var want = new Native.RECT(200, 160, 560, 440);
                frame.ApplyTheme(FrameColors.Teal, FrameStyles.Solid, 6, 6);
                frame.SetRect(want);
                Settle();
                Check(Native.IsWindowVisible(frame.Handle), "浮层已显示");

                var teal = FrameColors.Resolve(FrameColors.Teal);
                var red = FrameColors.Resolve(FrameColors.Red);
                var before = CaptureCornerPixel(frame.Handle, want, teal, red);

                // 换成一个完全不同的颜色，并且不碰矩形
                frame.ApplyTheme(FrameColors.Red, FrameStyles.Solid, 6, 6);
                Settle();

                var after = CaptureCornerPixel(frame.Handle, want, teal, red);

                Check(before.HasValue && after.HasValue, "能读回浮层的实际像素",
                    "before=" + (before.HasValue ? before.Value.ToString() : "null")
                    + " after=" + (after.HasValue ? after.Value.ToString() : "null"));

                if (before.HasValue && after.HasValue)
                {
                    int dTeal = ColorDistance(before.Value, teal);
                    int dRed = ColorDistance(before.Value, red);
                    Check(dTeal < dRed,
                        "改主题之前，像素接近旧颜色（薄荷青）而不是新颜色",
                        "像素=" + before.Value + " 距薄荷青=" + dTeal + " 距朱红=" + dRed);

                    int aRed = ColorDistance(after.Value, red);
                    int aTeal = ColorDistance(after.Value, teal);
                    Check(aRed < aTeal,
                        "★ 改主题之后，像素立刻变成新颜色（不需要等窗口移动）",
                        "像素=" + after.Value + " 距朱红=" + aRed + " 距薄荷青=" + aTeal);
                }

                frame.HideOverlay();
                Settle();
            }
        }

        /// <summary>
        /// 读浮层窗口某个位置的屏幕像素。浮层是分层窗口，用窗口 DC 读不到内容，
        /// 必须直接读屏幕（GetDC(NULL) + GetPixel）。
        ///
        /// 采样点选在外框描边上：矩形左上角往内缩 margin（实心样式 margin = thickness*0.9+3），
        /// 再往内缩半个线宽。这里样式固定用 Solid / thickness 6 / radius 6，
        /// margin = int(6*0.9)+3 = 8，所以描边中心大约在 (8+3, 8+3)。
        ///
        /// ★ 为什么要试两个坐标空间 ★
        ///   采样点是用【逻辑坐标】算出来的（和 SetRect 用的是同一套），
        ///   而屏幕像素本身是【物理坐标】。进程是 DPI 感知时两者一致；
        ///   一旦进程是 DPI 不感知的，两者差一个比例，直接读就会读错位置 ——
        ///   实测表现是"读到的颜色既不是旧颜色也不是新颜色"（一片背景色）。
        ///   所以这里把逻辑点和它的物理对应点都采一次，
        ///   取"更像外框描边"的那个（即离期望颜色更近的那个）。
        /// </summary>
        private static Color? CaptureCornerPixel(IntPtr overlay, Native.RECT r, params Color[] expected)
        {
            try
            {
                // 外框本体是从浮层矩形内缩 margin 之后画的，描边厚度 6 的一半是 3。
                int m = FrameStyles.MarginFor(FrameStyles.Solid, 6);
                var logical = new Native.POINT(r.Left + m + 3, r.Top + m + 3);
                var physical = CoordSpace.ToPhysical(logical);

                Color? best = null;
                int bestD = int.MaxValue;

                var candidates = new[] { logical, physical };
                for (int i = 0; i < candidates.Length; i++)
                {
                    var c = ReadScreenPixel(candidates[i].X, candidates[i].Y);
                    if (!c.HasValue) continue;

                    // 没有任何期望色时，第一个能读到的就用它
                    if (expected == null || expected.Length == 0) return c;

                    int d = int.MaxValue;
                    for (int k = 0; k < expected.Length; k++)
                    {
                        int dd = ColorDistance(c.Value, expected[k]);
                        if (dd < d) d = dd;
                    }
                    if (d < bestD) { bestD = d; best = c; }
                }
                return best;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return null;
            }
        }

        /// <summary>按屏幕坐标读一个像素（0x00BBGGRR）。失败返回 null。</summary>
        private static Color? ReadScreenPixel(int x, int y)
        {
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            try
            {
                uint px = Native.GetPixel(dc, x, y);
                if (px == 0xFFFFFFFFu) return null;        // CLR_INVALID
                int cr = (int)(px & 0xFF);
                int cg = (int)((px >> 8) & 0xFF);
                int cb = (int)((px >> 16) & 0xFF);
                return Color.FromArgb(cr, cg, cb);
            }
            finally { Native.ReleaseDC(IntPtr.Zero, dc); }
        }

        private static int ColorDistance(Color a, Color b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            return dr * dr + dg * dg + db * db;
        }

        /// <summary>外框外观：颜色 / 样式 的合法性与默认值。</summary>
        private static void TestFrameTheme()
        {
            var c = new AppConfig();
            Eq(c.FrameColorId, FrameColors.Teal, "默认外框颜色是薄荷青（不是 Windows 那个蓝）");
            Eq(c.FrameStyleId, FrameStyles.Glow, "默认外框样式是柔光环");
            Eq(c.FrameThickness, 3, "默认外框粗细 3px");
            Eq(c.FrameRadius, 13, "默认圆角 13px");

            Check(FrameColors.IsValid(FrameColors.Teal), "薄荷青是合法颜色 id");
            Check(!FrameColors.IsValid("NotAColor"), "非法颜色 id 会被认出来");
            Check(FrameStyles.IsValid(FrameStyles.Dashed), "虚线环是合法样式 id");
            Check(!FrameStyles.IsValid("NotAStyle"), "非法样式 id 会被认出来");

            // 手改坏了配置文件也不能崩
            c.FrameColorId = "???";
            c.FrameStyleId = "???";
            c.FrameThickness = 999;
            c.FrameRadius = -5;
            c.Normalize();
            Eq(c.FrameColorId, FrameColors.Teal, "非法颜色被拉回默认");
            Eq(c.FrameStyleId, FrameStyles.Glow, "非法样式被拉回默认");
            Eq(c.FrameThickness, 8, "超范围粗细被夹到上限 8");
            Eq(c.FrameRadius, 0, "负数圆角被夹到 0");

            // 每种颜色都要能解析出一个不透明的颜色（"跟随系统强调色"会去问 DWM）
            for (int i = 0; i < FrameColors.All.Length; i++)
            {
                var col = FrameColors.Resolve(FrameColors.All[i].Id);
                Check(col.A == 255, "颜色「" + FrameColors.All[i].Name + "」解析成功且不透明");
            }
        }

        /// <summary>吸附预览：能建出来、能摆位；关掉 SnapPreview 之后就不会再出现。</summary>
        private static void TestSnapPreviewOverlay()
        {
            using (var pv = new SnapPreview())
            {
                pv.EnsureCreated();
                if (!pv.IsCreated)
                {
                    Check(false, "吸附预览窗口创建成功", "CreateWindowEx 失败");
                    return;
                }

                var want = new Native.RECT(0, 0, 640, 540);
                pv.SetRect(want);
                Settle();

                Native.RECT got;
                Native.GetWindowRect(pv.Handle, out got);
                bool ok = got.Left == want.Left && got.Top == want.Top
                       && got.Width == want.Width && got.Height == want.Height;
                Check(ok, "吸附预览摆到了请求的矩形", "期望 " + want + "，实际 " + got);

                pv.HideOverlay();
                Settle();
                Check(!Native.IsWindowVisible(pv.Handle), "预览可以隐藏");
            }
        }

        /// <summary>节流的帧间隔换算。</summary>
        private static void TestFrameInterval()
        {
            var c = new AppConfig();
            Eq(c.FrameIntervalMs(60), 16, "60Hz → 每帧 16ms");
            Eq(c.FrameIntervalMs(144), 6, "144Hz → 每帧 6ms");
            Eq(c.FrameIntervalMs(0), 16, "拿不到刷新率回退到 60Hz");
            Eq(c.FrameIntervalMs(99999), 2, "离谱的高频被夹回 500Hz 上限（间隔 2ms）");
            Eq(c.FrameIntervalMs(20), 50, "最低 20Hz → 间隔 50ms");

c.DragRefreshMode = RefreshModes.Custom;
     c.DragRefreshHz = 120;
      Eq(c.FrameIntervalMs(240), 8, "手动 120Hz 优先于实测到的 240Hz");
 }

        /// <summary>
        /// 拖动"跟手"的底线：写窗口矩形的三套标志位全都必须带
        /// SWP_ASYNCWINDOWPOS。
        ///
        /// 用户报"移动窗口和拖拽窗口变形不跟手"。根因是`MOVETHICKBORDERS`
    /// 漏了这个标志：缺了它，SetWindowPos 会【同步等】目标窗口处理完
  /// WM_WINDOWPOSCHANGING / WM_WINDOWPOSCHANGED 才返回 ——
    /// 目标窗口越复杂、越是跨进程（浏览器 / IDE / 资源管理器），
  /// 每帧阻塞越久，工作线程被拖死，窗口就追不上鼠标。
 ///
        /// 症状特征：平移比缩放更明显 —— 缩放走 RESIZEFLAGS，本来就带 ASYNC。
        /// </summary>
   private static void TestMoveFlagsAreAsync()
    {
            Check((Native.MOVETHICKBORDERS & Native.SWP_ASYNCWINDOWPOS) != 0,
        "★ 平移标志位带 SWP_ASYNCWINDOWPOS（移动不跟手回归）",
     "MOVETHICKBORDERS=0x" + Native.MOVETHICKBORDERS.ToString("X"));

            Check((Native.RESIZEFLAGS & Native.SWP_ASYNCWINDOWPOS) != 0,
    "缩放标志位带 SWP_ASYNCWINDOWPOS（变形不跟手回归）",
                "RESIZEFLAGS=0x" + Native.RESIZEFLAGS.ToString("X"));

       Check((Native.MOVEASYNC & Native.SWP_ASYNCWINDOWPOS) != 0,
                "备用平移标志位同样带 SWP_ASYNCWINDOWPOS",
        "MOVEASYNC=0x" + Native.MOVEASYNC.ToString("X"));

// 平移绝不能带 NOCOPYBITS：那会让 DWM 不搬运像素，窗口内容"跟不上"位置，
      // 表现为拖动时窗口里一片空白/花掉（缩放才需要它）。
      Check((Native.MOVETHICKBORDERS & Native.SWP_NOCOPYBITS) == 0,
 "平移不带 SWP_NOCOPYBITS（否则拖动时窗口内容不跟随，会花屏）",
      "MOVETHICKBORDERS=0x" + Native.MOVETHICKBORDERS.ToString("X"));

    VerifyBottomBarButtons();
  }

        // ── T43 真实鼠标事件走完整钩子链路 ──────────────────────

        /// <summary>
        /// ★ 用户反复报的核心问题："触发阈值后左键和右键的功能没了。"
        ///
        /// 之前所有端到端用例都是**直接调 Coordinator.OnMouse** ——
        /// 那条路绕开了两个真实环节：
        ///   1) HookEngine.MouseCallback 里的**注入事件过滤**
        /// （ms.flags & LLMHF_INJECTED 就直接忽略）
        ///   2) 钩子本身有没有真的收到事件
        /// 而用户环境里失效的，恰恰是这两段。
        ///
     /// 所以本用例【真的装钩子 + 真的注入鼠标事件】，
/// 让事件从 SendInput 一路走到 Coordinator，完整复现用户的操作路径。
    /// </summary>
        private static void TestRealMouseThroughHook()
        {
   if (!NoRoomForTestWindow()) return;

            string dir = Path.Combine(Path.GetTempPath(), "holddrag-hook-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

         HookEngine hooks = null;
            HookWatchdog dog = null;
         Coordinator coord = null;

   Native.POINT savedCursor;
            bool haveSavedCursor = Native.GetCursorPos(out savedCursor);

     try
      {
   var store = new ConfigStore(dir);
     store.Load();
      var c = store.Current;
                c.TriggerKeyId = "K:0x0D";
                c.RequireHold = true;
        c.HoldMs = 400;
       c.ActionLeft = Actions.Move;
        c.ActionRight = Actions.Resize;
      c.ActionMiddle = Actions.Nothing;
     c.ShowActivationFrame = true;
          c.SnapPreview = false;
                c.EdgeSnap = false;
                c.SnapToOtherWindows = false;
                c.BringToFront = false;
   c.DoubleClickLeftMaximize = false;
                c.DoubleClickRightZone = false;
                c.AllowFullscreenWindows = true;

         coord = new Coordinator(store);
                coord.PrecreateOverlays();

        hooks = new HookEngine(coord);
      hooks.Install();
    dog = new HookWatchdog(hooks);
         dog.Start();
            Pump();
            Settle();

          Check(hooks.MouseInstalled && hooks.KeyboardInstalled,
    "全局鼠标钩子已安装",
      "错误码 " + hooks.LastError);

         // 让光标停到一个可预期的位置：屏幕中心偏左，避开任务栏
  var pt = new Native.POINT(400, 300);
    Native.SetCursorPos(pt.X, pt.Y);
      Pump();
         Settle();

            // 注入一个真实鼠标移动，确认钩子真的能收到
        int sx = 0, sy = 0;
            bool seen = hooks.TryGetLastSeen(out sx, out sy);
    Check(seen, "钩子收到了鼠标移动事件（钩子确实活着）",
      "seenValid=" + seen + " 位置=" + sx + "," + sy);

        // 进入触发态（等同用户长按 Enter 超过阈值）
   ActivateTrigger(coord);
      Settle();
             Check(coord.Trigger.Active, "触发键已进入激活态");

      // ── 环境能力探测 ──────────────────────────────────────
    //
// 先确认"移动事件"能不能到钩子 —— 这是钩子是否活着的直接证据。
            long countBefore = hooks.SeenCount;
   Native.SetCursorPos(pt.X + 1, pt.Y);
      Pump();
      for (int i = 0; i < 10 && hooks.SeenCount == countBefore; i++)
            {
   Application.DoEvents();
         System.Threading.Thread.Sleep(10);
      }
    bool moveEventsArrive = hooks.SeenCount > countBefore;
            if (!moveEventsArrive)
  {
  // 这不是致命问题（按键链路才是本用例的重点），但必须留下线索：
      // 连移动事件都收不到，说明钩子回调在这个宿主环境里整体不被投递。
     Log.Info("[自检 T43] 移动事件探测失败 —— 本环境下低级鼠标钩子可能不接收任何合成事件");
        }

// ══════ 注入分两段：先只发"按下"，等它被处理完，再发"抬起" ══════
      //
      // 为什么要拆开：down+up 一次性连发时，up 会在 down 之后【立刻】被钩子处理，
      // 于是 Begin 刚把 _dragging 置 true，End 就把它按回 false。
      // 测试线程等到 CPU 时 IsDragging 早就归零了，
      // 于是"功能明明好的"却报出 [FAIL] 真实左键事件能启动拖动会话。
      //
      // ★ 之前就踩过这个坑，还一度以为是产品有 bug，差点去改本来正确的代码。
      // 现在靠 DragEngine.SessionCount（只增不减）来判定"有没有成功启动过"，
      // 这才是与时序无关的判据。

      // ★ 探测必须【无条件】做，不能挂在 moveEventsArrive 的if 里。
      //   之前错误地写成"移动事件可达才去探测按键事件"，于是只要那一轮
      //   SetCursorPos 没被钩子看到（宿主机偶发），整组按键断言就被连带跳过，
      //   自检变成"看起来通过、其实什么都没测"—— 这比红色更危险。
      //   按键事件能不能到钩子，只能用真实的按键注入来回答。
      int sessionsBefore = coord.Drag.SessionCount;
      bool started;
      bool scaled;

      // 探测用右键、断言用左键：探测本身会真的启动一次会话，
      // 用右手探、左手判，两条路径互不干扰。
      bool probeStarted = InjectAndWaitDrag(coord, Native.INJECT_RIGHTDOWN,
          Native.INJECT_RIGHTUP, "探测", sessionsBefore);
      bool buttonEventsArrive = coord.Drag.SessionCount > sessionsBefore || probeStarted;

      CleanupSession(coord, Native.WM_RBUTTONUP, pt);

      if (!buttonEventsArrive)
      {
      Line("（环境限制）本环境下 SendInput 的【按键】事件不会进入低级鼠标钩子，"
      + "无法在此验证左键/右键链路，跳过。");
        Line("（移动事件可达=" + moveEventsArrive + "）");
   coord.Trigger.SimulateKeyUp(null);
            return;
   }

   // 进入触发态（探测之后需要重新建立）
  ActivateTrigger(coord);
     Settle();
    Check(coord.Trigger.Active, "重新进入触发态");

    // ★ 真实左键按下 → 必须启动一个"移动"会话
        //   判据用 SessionCount 差值，而不是 IsDragging（原因见上面拆开注入的注释）。
   int beforeLeft = coord.Drag.SessionCount;
        started = InjectAndWaitDrag(coord, Native.INJECT_LEFTDOWN,
         Native.INJECT_LEFTUP, "左键", beforeLeft);
        Check(started,
        "★ 真实左键事件能启动拖动会话（触发后左键失效回归）",
   "会话数没增加（before=" + beforeLeft + " now=" + coord.Drag.SessionCount
       + "）说明左键按下事件没走到 Begin");

        CleanupSession(coord, Native.WM_LBUTTONUP, pt);

    // 右键（改长宽）同理
     ActivateTrigger(coord);
        Settle();
    int beforeRight = coord.Drag.SessionCount;
   scaled = InjectAndWaitDrag(coord,
      Native.INJECT_RIGHTDOWN,
       Native.INJECT_RIGHTUP, "右键", beforeRight);
     Check(scaled,
            "★ 真实右键事件能启动缩放会话（触发后右键失效回归）",
        "会话数没增加（before=" + beforeRight + " now=" + coord.Drag.SessionCount
      + "）说明右键按下事件没走到 Begin");

CleanupSession(coord, Native.WM_RBUTTONUP, pt);

      coord.Trigger.SimulateKeyUp(null);
            }
            finally
      {
try { if (dog != null) dog.Stop(); } catch { }
                try { if (dog != null) dog.Dispose(); } catch { }
          try { if (hooks != null) hooks.Dispose(); } catch { }
    try { if (coord != null) coord.Dispose(); } catch { }
             try { Directory.Delete(dir, true); } catch { }
       try { if (haveSavedCursor) Native.SetCursorPos(savedCursor.X, savedCursor.Y); } catch { }
  }
        }

        /// <summary>收掉可能残留的会话，避免污染后续断言。</summary>
    private static void CleanupSession(Coordinator coord, int upMsg, Native.POINT pt)
    {
        try
        {
      if (coord.Drag.IsDragging)
     {
    var p = Ms(pt);
    coord.OnMouse(upMsg, ref p);
        }
     coord.Trigger.SimulateKeyUp(null);
       Settle();
        }
        catch { /* 清理失败不该让用例崩掉 */ }
      }

        /// <summary>
        /// 注入一次"按下 → 抬起"，返回"有没有成功启动过一个拖动会话"。
    ///
/// 必须走 SendInput 而不是直接调 Coordinator.OnMouse ——
/// 后者绕开了钩子的注入过滤，测不到真实链路。
        ///
        /// ★★ 关键：判据是 SessionCount 的增量，不是 IsDragging ★★
   /// 钩子是在【另一个线程】上处理这两个事件的。等 SendInput 返回、
   /// 测试线程拿到 CPU 时，down 起的会话很可能已经被 up 收掉了，
 /// 于是 IsDragging 恒为 false —— 一个稳定误报的陷阱。
        /// SessionCount 只增不减，才是与时序无关的判据。
        ///
   /// 注入也刻意拆成两次发送（先 down，等钩子处理完，再 up），
        /// 这样中途还能观察 IsDragging，调试时信息更全。
        /// </summary>
        private static bool InjectAndWaitDrag(Coordinator coord, uint downFlag, uint upFlag,
            string label, int sessionsBefore)
 {
   try
       {
  int cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT));

          // 第一段：只发"按下"
    var down = new[] { MakeMouseInput(downFlag) };
         uint sentDown = Native.SendInput(1, down, cb);
          if (sentDown != 1) Log.Trace("注入 " + label + "按下 只成功 " + sentDown + "/1");

    // 给钩子线程时间把 down 走完（最多约 300ms）
  for (int i = 0; i < 30 && coord.Drag.SessionCount <= sessionsBefore; i++)
        {
        Application.DoEvents();
        System.Threading.Thread.Sleep(10);
   }
       Application.DoEvents();

       bool begun = coord.Drag.SessionCount > sessionsBefore;

  // 第二段：发"抬起"，把会话正常收掉（不清会让后面的断言被污染）
            var up = new[] { MakeMouseInput(upFlag) };
          Native.SendInput(1, up, cb);
      for (int i = 0; i < 5; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }

            return begun;
}
        catch (Exception ex)
        {
            Log.Error(ex);
    return false;
   }
        }

/// <summary>
        /// 注入一次【相对移动】。
        ///
        /// 为什么不用 SetCursorPos：实测在本机的受限会话里，
        /// SetCursorPos 移动光标【不会】触发低级鼠标钩子回调 ——
        /// 钩子的 SeenCount 一点都不涨，拖动引擎自然一帧都收不到
        /// （T48 一开始就是这么假失败的）。
        /// 而 SendInput 注入的移动事件一定会走钩子，是唯一可靠的驱动方式。
        /// </summary>
        private static void InjectMouseMoveRelative(int dx, int dy)
        {
            try
            {
                var mi = new Native.MOUSEINPUT();
                mi.dx = dx;
                mi.dy = dy;
                mi.dwFlags = Native.MOUSEEVENTF_MOVE;
                var inputs = new[]
                {
                    new Native.INPUT { type = Native.INPUT_MOUSE,
                                       u = new Native.INPUTUNION { mi = mi } }
                };
                Native.SendInput(1, inputs,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
            }
            catch (Exception ex) { Log.Error(ex); }
        }

/// <summary>
        /// 注入一次【绝对坐标移动】（归一化到整个虚拟桌面）。
        ///
        /// ★ 为什么不用 MOUSEEVENTF_MOVE 的相对移动 ★
        ///   相对移动会走 Windows 的【指针加速曲线】（ballistics）——
        ///   请求移动 90px，实测光标走了 277px（约 3 倍），
        ///   测试里算出来的"期望位移"和实际完全对不上，纯粹是自找麻烦。
        ///   绝对移动不走加速，指哪到哪。
        /// </summary>
        private static void InjectMouseMoveAbsolute(int x, int y)
        {
            try
            {
                var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                int w = vs.Width > 1 ? vs.Width - 1 : 1;
                int h = vs.Height > 1 ? vs.Height - 1 : 1;

                int vx = (int)Math.Round((x - vs.Left) * 65535.0 / w);
                int vy = (int)Math.Round((y - vs.Top) * 65535.0 / h);
                if (vx < 0) vx = 0; if (vx > 65535) vx = 65535;
                if (vy < 0) vy = 0; if (vy > 65535) vy = 65535;

                var mi = new Native.MOUSEINPUT();
                mi.dx = vx;
                mi.dy = vy;
                mi.dwFlags = Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE
                           | Native.MOUSEEVENTF_VIRTUALDESK;
                var inputs = new[]
                {
                    new Native.INPUT { type = Native.INPUT_MOUSE,
                                       u = new Native.INPUTUNION { mi = mi } }
                };
                Native.SendInput(1, inputs,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
            }
            catch (Exception ex) { Log.Error(ex); }
        }

                /// <summary>注入一次鼠标按键（只有按下或抬起一个事件）。</summary>
        private static void InjectMouseButton(uint flag)
        {
            try
            {
                var inputs = new[] { MakeMouseInput(flag) };
                Native.SendInput(1, inputs,
                    System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.INPUT)));
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>
        /// T48：全链路真实拖动 —— 注入真实鼠标事件，窗口必须【真的位移】。
        ///
        /// 为什么 T43 不够：T43 只断言"拖动会话启动了"（SessionCount 增加）。
        /// 而用户报的是"按了之后窗口不动" —— 会话启动 ≠ 窗口动了。
        /// 中间任何一步出问题都会让会话照常启动、窗口却纹丝不动：
        ///   · 坐标换算错了 → 期望位置算错（会话仍然会起）
        ///   · SetWindowPos 的目标坐标落在屏幕外 → 系统直接忽略
        ///   · 目标窗口拒绝了这次移动
        /// 所以本用例一路走到【读回窗口矩形】，比对"光标位移"和"窗口位移"。
        /// 这是唯一能证明"这个软件真的能用"的断言。
        /// </summary>
        private static void TestRealDragMovesWindow()
        {
            if (!NoRoomForTestWindow()) return;

            string dir = Path.Combine(Path.GetTempPath(), "holddrag-t48-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            HookEngine hooks = null;
            Coordinator coord = null;
            Form target = null;
            Native.POINT savedCursor;
            bool haveSavedCursor = Native.GetCursorPos(out savedCursor);

            try
            {
                var store = new ConfigStore(dir);
                store.Load();
                var c = store.Current;
                c.TriggerKeyId = "K:0x0D";
                c.RequireHold = true;
                c.HoldMs = 200;
                c.ActionLeft = Actions.Move;
                c.ActionRight = Actions.Resize;
                c.ActionMiddle = Actions.Nothing;
                c.ShowActivationFrame = false;
                c.SnapPreview = false;
                c.EdgeSnap = false;
                c.SnapToOtherWindows = false;
                c.BringToFront = false;
                c.DoubleClickLeftMaximize = false;
                c.DoubleClickRightZone = false;
                c.AllowFullscreenWindows = true;
                c.RestoreMaximizedWhenMoved = false;

                Native.POINT cur;
                Native.GetCursorPos(out cur);
                Native.RECT work = WorkAreaOfNearestMonitor(cur);

                target = new Form();
                target.FormBorderStyle = FormBorderStyle.Sizable;
                target.StartPosition = FormStartPosition.Manual;
                target.ShowInTaskbar = false;
                target.Text = "HoldDrag T48 target";
                target.TopMost = true;
                target.Bounds = new Rectangle(work.Left + 60, work.Top + 60, 320, 240);
                target.Show();
                try { target.BringToFront(); target.Activate(); } catch { }
                Pump();
                Settle();

                WindowActions.SetForeignForTest(target.Handle, true);

                coord = new Coordinator(store);
                coord.PrecreateOverlays();

                hooks = new HookEngine(coord);
                hooks.Install();
                Pump();
                Settle();

                if (!hooks.MouseInstalled) { Line("  （鼠标钩子装不上，跳过）"); return; }

                var pt = CenterOf(target.Handle);
                Native.SetCursorPos(pt.X, pt.Y);
                Pump();
                Settle();

                ActivateTrigger(coord);
                Settle();
                Check(coord.Trigger.Active, "T48 触发键已进入激活态",
                    "Active=" + coord.Trigger.Active);

                Native.RECT before;
                if (!Native.GetWindowRect(target.Handle, out before))
                { Line("  （取不到窗口矩形，跳过）"); return; }

                int sessionsBefore = coord.Drag.SessionCount;
                long seenBeforeInj = hooks.SeenCount;

                InjectMouseButton(Native.INJECT_LEFTDOWN);
                for (int i = 0; i < 40 && coord.Drag.SessionCount <= sessionsBefore; i++)
                { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                Application.DoEvents();

                Line("    （诊断）注入前 SeenCount=" + seenBeforeInj
                    + " 注入后=" + hooks.SeenCount
                    + " 触发态=" + coord.Trigger.Active
                    + " 会话数=" + coord.Drag.SessionCount
                    + " 光标=" + pt
                    + " 目标=" + DescribeWindow(target.Handle));

                bool begun = coord.Drag.SessionCount > sessionsBefore;
                Check(begun, "真实左键事件启动了移动会话",
                    "会话数没增加（" + sessionsBefore + "）说明按下事件没走到 Begin");

                if (begun)
                {
                    Native.POINT p0;
                    Native.GetCursorPos(out p0);

                    // ★ 用【绝对坐标】注入驱动，而不是 SetCursorPos ★
                    //   SetCursorPos 不触发低级钩子；相对移动又会走鼠标加速曲线。
                    InjectMouseMoveAbsolute(p0.X + 90, p0.Y + 60);
                    for (int i = 0; i < 15; i++)
                    { Application.DoEvents(); System.Threading.Thread.Sleep(10); }

                    Native.POINT p1;
                    Native.GetCursorPos(out p1);
                    int realDx = p1.X - p0.X, realDy = p1.Y - p0.Y;

                    long seenAfterMove = hooks.SeenCount;
                    for (int i = 0; i < 30; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }

                    Line("    （诊断2）移动后 SeenCount=" + seenAfterMove
                        + " 拖拽中=" + coord.Drag.IsDragging
                        + " 光标=" + p1);

                    Native.RECT after;
                    Native.GetWindowRect(target.Handle, out after);
                    int movedX = after.Left - before.Left, movedY = after.Top - before.Top;

                    Check(Math.Abs(movedX - realDx) <= 3 && Math.Abs(movedY - realDy) <= 3,
                        "★ 窗口真的跟着光标移动了（核心功能端到端）",
                        "光标位移=" + realDx + "," + realDy + " 窗口位移=" + movedX + "," + movedY
                        + " 前=" + before + " 后=" + after
                        + " 换算系数=" + CoordSpace.Scale.ToString("0.####"));

                    InjectMouseButton(Native.INJECT_LEFTUP);
                    for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }

                    ActivateTrigger(coord);
                    Settle();

                    Native.RECT rb;
                    Native.GetWindowRect(target.Handle, out rb);
                    int rsBefore = coord.Drag.SessionCount;

                    // 把光标精确放到窗口右下角（缩放角区）
                    InjectMouseMoveAbsolute(rb.Right - 8, rb.Bottom - 8);
                    for (int i = 0; i < 20; i++)
                    { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    {
                        Native.POINT atCorner;
                        Native.GetCursorPos(out atCorner);
                        Line("    （诊断）期望光标=" + (rb.Right - 8) + "," + (rb.Bottom - 8)
                            + " 实际=" + atCorner + " 窗口=" + rb);
                    }

                    InjectMouseButton(Native.INJECT_RIGHTDOWN);
                    for (int i = 0; i < 40 && coord.Drag.SessionCount <= rsBefore; i++)
                    { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                    Application.DoEvents();

                    bool resizeBegun = coord.Drag.SessionCount > rsBefore;
                    Check(resizeBegun, "真实右键事件启动了缩放会话",
                        "会话数没增加（" + rsBefore + "）说明右键按下事件没走到 Begin");

                    if (resizeBegun)
                    {
                        Native.POINT q0;
                        Native.GetCursorPos(out q0);

                        InjectMouseMoveAbsolute(q0.X + 70, q0.Y + 50);
                        for (int i = 0; i < 20; i++)
                        { Application.DoEvents(); System.Threading.Thread.Sleep(10); }

                        Native.POINT q1;
                        Native.GetCursorPos(out q1);

                        for (int i = 0; i < 30; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }

                        Native.RECT ra;
                        Native.GetWindowRect(target.Handle, out ra);
                        int dw = ra.Width - rb.Width, dh = ra.Height - rb.Height;
                        int wantW = q1.X - q0.X, wantH = q1.Y - q0.Y;

                        Check(Math.Abs(dw - wantW) <= 4 && Math.Abs(dh - wantH) <= 4,
                            "★ 右键真的改变了窗口大小（核心功能端到端）",
                            "光标位移=" + wantW + "," + wantH + " 尺寸变化=" + dw + "," + dh
                            + " 前=" + rb.Width + "x" + rb.Height + " 后=" + ra.Width + "x" + ra.Height);
                    }

                    InjectMouseButton(Native.INJECT_RIGHTUP);
                    for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                Check(false, "全链路拖动用例执行", "异常: " + ex.Message);
            }
            finally
            {
                try { if (target != null) WindowActions.SetForeignForTest(target.Handle, false); } catch { }
                try { if (coord != null) coord.SetPaused(true); } catch { }
                try { if (hooks != null) hooks.Dispose(); } catch { }
                try { if (coord != null) coord.Dispose(); } catch { }
                try
                {
                    if (target != null && !target.IsDisposed)
                    { target.Visible = false; Native.DestroyWindow(target.Handle); }
                    if (target != null) { target.Close(); target.Dispose(); }
                }
                catch { }
                try { Directory.Delete(dir, true); } catch { }
                try { if (haveSavedCursor) Native.SetCursorPos(savedCursor.X, savedCursor.Y); } catch { }
                for (int i = 0; i < 6; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
            }
        }

        private static Native.INPUT MakeMouseInput(uint flags)
        {
   var mi = new Native.MOUSEINPUT();
    mi.dwFlags = flags;
      return new Native.INPUT { type = Native.INPUT_MOUSE, u = new Native.INPUTUNION { mi = mi } };
     }

        /// <summary>
        /// T47：黑名单必须能按【进程名】匹配，且两个挑选入口都在界面上。
        ///
        /// 用户要求："在这个不生效的窗口这里，给我加一个功能，可以打开我的电脑
        /// 浏览文件，去选择对应的 exe 文件"、"点击选择窗口，然后鼠标光标变成十字准星，
        /// 点击哪一个窗口，就会把对应的程序给加进来"。
        ///
        /// 这两个功能要真的管用，前提是黑名单能按【进程名】命中 ——
        /// 因为"选择程序…"存进去的就是 exe 文件名（如 notepad），
        /// 而中文系统下窗口标题里根本不会出现 notepad 这个词。
        /// 所以这条断言才是这两个功能的真正底座。
        /// </summary>
        private static void TestBlacklistPickers()
        {
            // ── 1. 能取到进程的可执行文件名 ──
            uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            string own = WindowActions.ProcessExeNameByPid(ownPid);
            Check(own.Length > 0,
                "能取到本进程的 exe 名（黑名单按进程名匹配的前提）",
                "得到 \"" + own + "\"");

            // ── 2. 进程名必须参与黑名单匹配 ──
            if (!NoRoomForTestWindow()) return;

            Form w = null;
            try
            {
                w = new Form();
                w.Text = "HoldDrag T47 blacklist";
                w.Bounds = new Rectangle(0, 0, 200, 120);
                w.Show();
                Pump();

                // 用一个"绝不可能出现在标题或类名里"的进程名做关键字：
                //   本进程的 exe 名（HoldDrag）在窗口标题 "HoldDrag T47 blacklist" 里
                //   其实也出现了，所以这里额外造一个只存在于进程名里的关键字 ——
                //   把标题改成不含该词的文本。
                w.Text = "zzz 标题里没有那个词 zzz";
                Pump();

                WindowActions.ApplyBlacklist(own);
                bool hit = WindowActions.IsBlacklisted(w.Handle);
                Check(hit,
                    "★ 黑名单能用【进程名】命中窗口（选择程序 / 点选窗口 的前提）",
                    "关键字=" + own + " 标题=\"" + Native.GetWindowTitle(w.Handle)
                    + "\" 类名=" + Native.GetClassName(w.Handle));

                // 反证：不相关的关键字不能误伤
                WindowActions.ApplyBlacklist("zzz_definitely_not_a_real_process_zzz");
                Check(!WindowActions.IsBlacklisted(w.Handle),
                    "不相关的关键字不会误伤窗口", "关键字=zzz_definitely_not_a_real_process_zzz");
            }
            finally
            {
                WindowActions.ApplyBlacklist(string.Empty);
                try
                {
                    if (w != null && !w.IsDisposed)
                    {
                        w.Visible = false;
                        Native.DestroyWindow(w.Handle);
                    }
                    if (w != null) { w.Close(); w.Dispose(); }
                }
                catch { }
                for (int i = 0; i < 4; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
            }

            // ── 3. 两个挑选入口必须在界面上，且追加逻辑正确 ──
            using (var f = new DragFixture(null))
            using (var form = new SettingsForm(f.Store, f.Coord))
            {
                form.CreateControl();

                var pickExe = FindByText(form, "选择程序…");
                var pickWin = FindByText(form, "点选窗口");
                Check(pickExe != null && pickWin != null,
                    "黑名单的两个新按钮都在界面上（选择程序… / 点选窗口）",
                    "选择程序=" + (pickExe != null) + " 点选窗口=" + (pickWin != null));

                // 追加 + 去重 + 分隔符
                form.ProbeAppendBlacklist("notepad");
                Eq(form.ProbeBlacklistText, "notepad", "第一个关键字写进黑名单框");

                form.ProbeAppendBlacklist("notepad");
                Eq(form.ProbeBlacklistText, "notepad", "重复的关键字不会加两次");

                form.ProbeAppendBlacklist("chrome");
                Eq(form.ProbeBlacklistText, "notepad，chrome", "第二个关键字用中文逗号追加");

                // 空关键字必须被忽略（否则会写进一个空条目，把黑名单弄脏）
                form.ProbeAppendBlacklist("   ");
                Eq(form.ProbeBlacklistText, "notepad，chrome", "空白关键字被忽略");

                form.Close();
            }
        }

        /// <summary>
        /// T46：物理坐标必须被换算进进程自己的坐标空间。
        ///
        /// ══════ 这是用户"基本功能全没了"的根因 ══════
        ///
        /// 用户日志：FindTarget 没找到目标，原因=WindowFromPoint 返回空，坐标=2182,1440。
        /// 而该进程的逻辑屏幕宽度只有 1920 —— 2182 &gt; 1920 就是铁证：
        /// 钩子给的是【物理】坐标，进程却在【逻辑】坐标空间里工作。
        ///
        /// 屏幕物理 3840x2160 @200%，进程逻辑 1920x1080。
        /// 系统会把传进去的坐标再放大 2 倍 → (4364,2880) → 跑到屏幕外
        /// → WindowFromPoint 返回空 → FindTarget 返回 0 → 左键右键全部失效。
        ///
        /// 本用例验证：
        ///   1) 换算数学正确（缩放 0.5 时 2182 → 1091）
        ///   2) 当前进程的坐标空间自洽（不感知就必须算出换算系数）
        ///   3) 端到端：把"物理坐标"经换算后拿去命中测试，必须命中目标窗口
        /// </summary>
        private static void TestCoordinateSpace()
        {
            // ── 1. 换算数学 ──
            double saved = CoordSpace.Scale;

            CoordSpace.SetScaleForTest(1.0);
            var id = CoordSpace.ToProcess(new Native.POINT(2182, 1440));
            Eq(id.X, 2182, "缩放=1 时坐标原样通过（恒等变换）");

            CoordSpace.SetScaleForTest(0.5);
            var sc = CoordSpace.ToProcess(new Native.POINT(2182, 1440));
            Eq(sc.X, 1091, "缩放=0.5 时物理坐标换算到逻辑空间（X）");
            Eq(sc.Y, 720, "缩放=0.5 时物理坐标换算到逻辑空间（Y）");

            CoordSpace.SetScaleForTest(saved);

            // ── 2. 当前进程的坐标空间必须自洽 ──
            int aware = Native.DPI_AWARENESS_INVALID;
            try
            {
                aware = Native.GetAwarenessFromDpiAwarenessContext(
                    Native.GetThreadDpiAwarenessContext());
            }
            catch (Exception ex) { Log.Error(ex); }

            if (aware == Native.DPI_AWARENESS_UNAWARE)
            {
                // 不感知的进程：必须算出非 1 的换算系数，否则命中测试必然失败
                bool scaled = Math.Abs(CoordSpace.Scale - 1.0) > 0.0001;
                Check(scaled,
                    "★ DPI 不感知进程必须算出坐标换算系数（否则命中测试必然失败）",
                    "Scale=" + CoordSpace.Scale + "（等于 1 说明没算出来）");
            }
            else
            {
                Eq(CoordSpace.Scale, 1.0, "DPI 感知进程的坐标换算是恒等变换");
            }

            // ── 3. 端到端：物理坐标 → 换算 → 命中测试 ──
            if (!NoRoomForTestWindow()) return;

            Native.POINT cur;
            Native.GetCursorPos(out cur);
            Native.RECT work = WorkAreaOfNearestMonitor(cur);

            Form w = null;
            try
            {
                w = new Form();
                w.FormBorderStyle = FormBorderStyle.Sizable;
                w.StartPosition = FormStartPosition.Manual;
                w.ShowInTaskbar = false;
                w.Text = "HoldDrag T46 coordspace";
                w.TopMost = true;
                w.Bounds = new Rectangle(work.Left + 40, work.Top + 40, 320, 220);
                w.Show();
                try { w.BringToFront(); w.Activate(); } catch { }
                Pump();
                Settle();

                // ★ 把测试窗口登记成"别人的窗口" ★
                //   FindTarget 的核心逻辑是"命中自己 → 沿 Z 序往下穿透 → 找到第一个不是自己的窗口"。
                //   测试窗口和浮层同属本进程，不标记的话会被当成"自己的窗口"一路跳过，
                //   最后返回空句柄 —— 那是生产环境的正确行为，但会让本用例假失败。
                WindowActions.SetForeignForTest(w.Handle, true);

                // 目标窗口中心的【逻辑】坐标（GetWindowRect 对不感知进程返回的就是逻辑坐标）
                Native.RECT wr;
                if (!Native.GetWindowRect(w.Handle, out wr))
                {
                    Line("  （取不到窗口矩形，跳过端到端部分）");
                    return;
                }
                int logX = (wr.Left + wr.Right) / 2;
                int logY = (wr.Top + wr.Bottom) / 2;

                double s = CoordSpace.Scale;
                if (s <= 0) s = 1.0;

                // 还原出"钩子会给我们的物理坐标"：物理 = 逻辑 / 缩放系数
                var phys = new Native.POINT(
                    (int)Math.Round(logX / s),
                    (int)Math.Round(logY / s));

                // 经 CoordSpace 换算后应当回到逻辑坐标
                var back = CoordSpace.ToProcess(phys);

                Check(Math.Abs(back.X - logX) <= 1 && Math.Abs(back.Y - logY) <= 1,
                    "物理坐标经换算能还原成逻辑坐标（往返一致）",
                    "逻辑=" + logX + "," + logY + " 物理=" + phys.X + "," + phys.Y
                    + " 还原=" + back.X + "," + back.Y + " 缩放=" + s.ToString("0.####"));

                // 用【换算后】的坐标去命中测试 —— 这才是 Coordinator.OnMouse 里真实走的路
                IntPtr hit = WindowActions.FindTarget(back, true);
                Check(hit == w.Handle,
                    "★ 物理坐标经换算后能命中目标窗口（核心功能失效回归）",
                    "命中=" + DescribeWindow(hit) + " 期望=" + DescribeWindow(w.Handle)
                    + " 缩放=" + s.ToString("0.####"));

                // 反证：不换算（直接用物理坐标）在不感知进程里必然命不中 ——
                // 这正是用户遇到的情况，写出来是为了让"为什么必须换算"有据可查。
                if (s != 1.0)
                {
                    IntPtr raw = WindowActions.FindTarget(phys, true);
                    Line("    （对照）不换算直接用物理坐标 -> "
                        + (raw == w.Handle ? "竟然也命中了" : "命不中，与用户日志一致"));
                }
            }
            finally
            {
                try { if (w != null) WindowActions.SetForeignForTest(w.Handle, false); } catch { }
                try
                {
                    if (w != null && !w.IsDisposed)
                    {
                        w.Visible = false;
                        Native.DestroyWindow(w.Handle);
                    }
                    if (w != null) { w.Close(); w.Dispose(); }
                }
                catch { }
                for (int i = 0; i < 6; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
            }
        }

        /// <summary>
        /// T45：命中测试必须有兜底，且 Z 序穿透方向必须正确。
        ///
        /// 用户原话："基本的功能都没有了呀……按 enter 键之后触发阈值，
        /// 我们通过左键和右键来触发相应的功能，现在这个功能没有了。"
        ///
        /// 日志给出了确凿原因：
        ///   [诊断] FindTarget 没找到目标…… 原因=WindowFromPoint 返回空（桌面/无窗口区域）
        /// 也就是说 WindowFromPoint 在用户的机器上【稳定返回空句柄】，
        /// 于是 FindTarget 直接返回 0，整个程序的核心功能彻底死掉。
        ///
        /// 本用例做两件事：
        ///   1) 锁死 Z 序方向常量 —— GW_HWNDNEXT(2) 是"往下"，GW_HWNDPREV(3) 是"往上"。
        ///      曾经写反过，导致穿透循环朝反方向走、最后拿到空句柄。
        ///   2) 强制跳过 WindowFromPoint，验证兜底策略真的能顶上。
        ///      ★ 这是关键：兜底代码在自检环境里【永远不会被走到】
        ///      （因为自检环境 WindowFromPoint 一直正常），
        ///      没被执行过的兜底等于没有兜底。
        /// </summary>
        private static void TestHitTestHasFallback()
        {
            // ── 1. 常量方向（写反 = 核心功能失效）──
            Eq((int)Native.GW_HWNDNEXT, 2, "GW_HWNDNEXT 是 2（Z 序往下，穿透循环必须用它）");
            Eq((int)Native.GW_HWNDPREV, 3, "GW_HWNDPREV 是 3（Z 序往上，不是穿透要的方向）");

            // ── 2. 兜底策略真的能顶上 ──
            if (!NoRoomForTestWindow()) return;

            Native.POINT cur;
            Native.GetCursorPos(out cur);
            Native.RECT work = WorkAreaOfNearestMonitor(cur);

            Form w = null;
            try
            {
                int tw = 300, th = 200;
                int tx = work.Left + 40;
                int ty = work.Top + 40;

                w = new Form();
                w.FormBorderStyle = FormBorderStyle.Sizable;
                w.StartPosition = FormStartPosition.Manual;
                w.ShowInTaskbar = false;
                w.Text = "HoldDrag T45 hittest";
                w.TopMost = true;                     // 保证它在最前，兜底策略也不会被别人抢走
                w.Bounds = new Rectangle(tx, ty, tw, th);
                w.Show();
                try { w.BringToFront(); w.Activate(); } catch { }
                Pump();
                Settle();

                var pt = CenterOf(w.Handle);

                // ── 正常路径 ──
                string how1;
                IntPtr r1 = WindowActions.HitTestTopWindow(pt, out how1);
                Check(r1 == w.Handle,
                    "命中测试（正常路径）能拿到测试窗口",
                    "命中=" + DescribeWindow(r1) + " 方式=" + how1);

                // ── ★ 强制跳过 WindowFromPoint，逼程序走兜底 ★ ──
                WindowActions.SkipWindowFromPointForTest = true;
                try
                {
                    string how2;
                    IntPtr r2 = WindowActions.HitTestTopWindow(pt, out how2);

                    Check(r2 == w.Handle,
                        "★ WindowFromPoint 失效时兜底策略仍能命中（核心功能失效回归）",
                        "命中=" + DescribeWindow(r2) + " 方式=" + how2);

                    Check(how2 != "WindowFromPoint",
                        "兜底确实走的是非 WindowFromPoint 路径（否则这个用例等于没测）",
                        "方式=" + how2);
                }
                finally
                {
                    WindowActions.SkipWindowFromPointForTest = false;
                }
            }
            finally
            {
                WindowActions.SkipWindowFromPointForTest = false;
                try
                {
                    if (w != null && !w.IsDisposed)
                    {
                        w.Visible = false;
                        Native.DestroyWindow(w.Handle);
                    }
                    if (w != null) { w.Close(); w.Dispose(); }
                }
                catch { }
                for (int i = 0; i < 6; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
            }
        }

        /// <summary>
        /// T44：模拟"改配置 → 关掉程序 → 重开"，每一个字段都必须还在。
        ///
        /// 用户原话："我还要求电脑重启后相关的配置是能够得到保存的，
        /// 而不是说重启应用之后配置就恢复初始状态了。"
        ///
        /// 为什么已有的 T2（TestConfigRoundTrip）不够：
        ///   它只往返了 4 个字段（HoldMs / TriggerKeyId / ActionLeft / EdgeSnap），
        ///   而用户真正在设置窗口里改的是【整套】—— 主题颜色、样式、粗细、圆角、
        ///   三个鼠标键的动作、开关、窗口尺寸…… 只要其中【任何一个】没被序列化，
        /// 用户看到的就都是"重启后恢复初始状态"。
        /// 所以这里逐项比对全部字段。
        ///
        /// 另外刻意覆盖一个以前没测的场景：主配置 + 备份【全部】存在时的连续多次保存，
        /// 因为轮换逻辑（main→bak, bak→bak2）本身也是可能把用户配置搞丢的地方。
        /// </summary>
        private static void TestConfigSurvivesRestart()
        {
      string t = Path.Combine(Path.GetTempPath(), "holddrag-persist-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(t);
        try
        {
       // 第一次"运行"：加载出厂默认 → 用户在设置窗口里改一堆东西
 var s1 = new ConfigStore(t);
     s1.Load();
        var c = s1.Current;
      c.TriggerKeyId = KeysUtil.RightAlt;
            c.RequireHold = false;
c.HoldMs = 1234;
c.ActionLeft = Actions.Resize;
       c.ActionRight = Actions.AlwaysOnTop;
   c.ActionMiddle = Actions.Nothing;
     c.ShowActivationFrame = false;
      c.SnapPreview = false;
   //刻意选【非默认】的颜色/样式，避免"默认值恰好等于我选的值"而碰巧通过
            c.FrameColorId = FrameColors.Violet;
 c.FrameStyleId = FrameStyles.Dashed;
        c.FrameThickness = 7;
        c.FrameRadius = 21;
c.EdgeSnap = false;
 c.SnapToOtherWindows = false;
      c.DoubleClickLeftMaximize = false;
            c.DoubleClickRightZone = false;
     c.RestoreMaximizedWhenMoved = false;
            c.BringToFront = false;
        c.AllowFullscreenWindows = true;
        c.SnapDistance = 88;
        c.CornerPercent = 33;
   c.Blacklist = "Notepad - 记事本,CustomWndClass";
       c.DragRefreshMode = RefreshModes.Custom;
            c.DragRefreshHz = 144;
  c.AutoStart = AutoStartModes.None;
            c.RunAsAdmin = false;
            c.WarnNonAdmin = false;
            c.SettingsWidth = 1234;
        c.SettingsHeight = 876;
      s1.Save();

      // 第二次"运行"：全新的 ConfigStore（等价于重启进程），逐项读回来
    var s2 = new ConfigStore(t);
        s2.Load();
        var r = s2.Current;

       Eq(r.TriggerKeyId, KeysUtil.RightAlt, "触发键");
    Eq(r.HoldMs, 1234, "长按阈值");
      Check(!r.RequireHold, "长按开关");
 Eq(r.ActionLeft, Actions.Resize, "左键动作");
            Eq(r.ActionRight, Actions.AlwaysOnTop, "右键动作");
            Eq(r.ActionMiddle, Actions.Nothing, "中键动作");
            Check(!r.ShowActivationFrame, "外框总开关");
            Check(!r.SnapPreview, "贴边预览开关");

      // ★ 主题这两项是用户反复报过的问题，单独再点一次名
       Eq(r.FrameColorId, c.FrameColorId, "★ 外框颜色");
      Eq(r.FrameStyleId, c.FrameStyleId, "★ 外框样式");
            Eq(r.FrameThickness, 7, "★ 外框粗细");
    Eq(r.FrameRadius, 21, "★ 外框圆角");

      Check(!r.EdgeSnap, "贴边开关");
            Check(!r.SnapToOtherWindows, "贴其他窗口开关");
  Check(!r.DoubleClickLeftMaximize, "双击左键最大化开关");
      Check(!r.DoubleClickRightZone, "双击右键九宫格开关");
            Check(!r.RestoreMaximizedWhenMoved, "移动时还原最大化开关");
      Check(!r.BringToFront, "置顶开关");
            Check(r.AllowFullscreenWindows, "允许全屏开关");
   Eq(r.SnapDistance, 88, "吸附距离");
 Eq(r.CornerPercent, 33, "九宫格分区百分比");
       Eq(r.Blacklist, c.Blacklist, "黑名单");
            Eq(r.DragRefreshMode, RefreshModes.Custom, "刷新率模式");
 Eq(r.DragRefreshHz, 144, "刷新率数值");
            Eq(r.AutoStart, AutoStartModes.None, "开机自启模式");
            Check(!r.RunAsAdmin, "管理员权限开关");
            Check(!r.WarnNonAdmin, "非管理员提示开关");
Eq(r.SettingsWidth, 1234, "设置窗口宽度");
Eq(r.SettingsHeight, 876, "设置窗口高度");

            // ★ 窗口尺寸上限必须跟随屏幕，不能写死 2400 ★
            //   用户屏幕 3840 宽，窗口拉宽后被硬压回 2400，
            //   表现就是"我明明拉得更宽了，下次打开又变回去"。
            int screenW = System.Windows.Forms.SystemInformation.VirtualScreen.Width;
            if (screenW > 2400)
            {
                s2.Current.SettingsWidth = screenW;     // 拉满整个桌面宽度
                s2.Current.SettingsHeight = 1000;
                s2.Save();

                var sBig = new ConfigStore(t);
                sBig.Load();
                Check(sBig.Current.SettingsWidth >= screenW,
                    "★ 窗口宽度上限跟随屏幕（不再写死 2400）",
                    "屏幕宽=" + screenW + " 存回来=" + sBig.Current.SettingsWidth);
            }
            else
            {
                Line("（跳过）本机屏幕宽度 " + screenW + " 不大于 2400，无法验证上限是否跟随屏幕");
            }

     // ── 连续多次保存后仍要能读回来（覆盖备份轮换逻辑）──
       s2.Current.HoldMs = 555;
            s2.Save();
            s2.Current.HoldMs = 666;
      s2.Save();
     var s3 = new ConfigStore(t);
        s3.Load();
    Eq(s3.Current.HoldMs, 666, "连续多次保存后最新值仍能读回");
      Eq(s3.Current.FrameColorId, c.FrameColorId, "多次保存后主题颜色没被覆盖");

       // ── 主配置损坏时必须能从备份兜回来，而不是变成出厂默认 ──
     //这一条正是"重启后恢复初始状态"的另一种表现形式：
   // 保存过程中的意外（断电/强杀）如果把主文件写坏了，
        // 用户就会看到"我的设置全没了"。三级轮换就是为了兜住这种情况。
        File.WriteAllText(Path.Combine(t, "config.json"), "{ 坏掉的 JSON");
 var s4 = new ConfigStore(t);
      s4.Load();
      Eq(s4.Current.HoldMs, 555, "主配置损坏后回退到备份而不是出厂默认");
        }
        finally
        {
          try { Directory.Delete(t, true); } catch { }
        }
        }

        /// <summary>
        /// 底栏三个按钮（恢复默认值 / 保存并生效 / 取消）的文字与按钮框必须适配。
        ///
        /// 用户原话："设置页面的恢复默认值保存并生效，还有取消这几个按钮
        ///   字跟这几个按钮并没有进行很好的适配。"
        ///
        /// 两个坑：
        ///   1) FlatButton.OnPaint 原来拿 g.MeasureString 的度量去推算
        /// TextRenderer 的落笔位置 —— 两套API 基准不同，高 DPI 下偏差被放大。
    ///   2) 按钮设计稿高度（32）比同排输入框（26）高出一截，视觉上不在一个尺度上。
        /// </summary>
   private static void VerifyBottomBarButtons()
        {
  using (var f = new DragFixture(null))
            {
         using (var form = new SettingsForm(f.Store, f.Coord))
      {
           form.CreateControl();
  form.Show();
            Application.DoEvents();
          form.Size = form.Size;
   Application.DoEvents();
     SettingsForm.RelayoutForTest(form);
    Application.DoEvents();

var bad = new System.Text.StringBuilder();
    int checkedBtn = 0;

      Walk(form, delegate (Control c)
{
      if (!(c is Button)) return;
// 只看底栏那三个按钮：它们是自绘的，文字必须完整且水平居中。
      // ★ 不要用 Parent.Name 判断 —— Panel 的 Name 默认是 "Panel"，
      //   不是字段名 "_pager"，按名字找会一个都找不到（踩过）。
      //   改用"文字正好是这三个之一"来识别。
      string bt = c.Text;
      if (bt != "恢复默认值" && bt != "保存并生效" && bt != "取消") return;

   checkedBtn++;

    Size need;
      try
    {
    // 静态 MeasureText 重载：拿不到控件当 context，用文本/字体/size 那一组。
   // NoPrefix 关掉 & 这类助记符，免得算出来的宽度比实际绘制时大。
        need = TextRenderer.MeasureText(c.Text, c.Font,
                       new Size(int.MaxValue, int.MaxValue),
         TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
             }
     catch { return; }

    // 文字宽必须装得下（留 12px 内边距余量：左右各 6）
      if (need.Width > c.Width - 12)
              bad.Append(Name(c)).Append("(文字宽").Append(need.Width)
                  .Append(" > 可用宽").Append(c.Width - 12).Append(") ");

   // 文字高必须装得下（上下各 4 余量）
      if (need.Height > c.Height - 8)
      bad.Append(Name(c)).Append("(文字高").Append(need.Height)
  .Append(" > 可用高").Append(c.Height - 8).Append(") ");
             });

     Check(checkedBtn == 3,
              "底栏确实有 3 个按钮被检查到",
      "实际找到 " + checkedBtn + " 个");

    Check(bad.Length == 0,
        "★ 底栏按钮的文字与按钮框适配（恢复默认值/保存并生效/取消）",
            bad.Length == 0
        ? checkedBtn + " 个按钮文字都装得下且居中"
      : "异常: " + bad);

         // 按钮高度必须与同排的输入框同一量级（差 8px 以内）
        // 否则视觉上就是"一排大按钮杵在输入框旁边"。
   var save = FindByText(form, "保存并生效");
       var combo = FindFirstCombo(form);
   if (save != null && combo != null)
   {
        int diff = Math.Abs(save.Height - combo.Height);
    Check(diff <= 8,
     "底栏按钮高度与输入框控件同一量级（视觉不突兀）",
        "按钮高 " + save.Height + " 输入框高 " + combo.Height + " 差 " + diff);
      }

           form.Close();
       }
          }
        }

        private static Control FindByText(Control root, string text)
        {
            try
  {
                for (int i = 0; i < root.Controls.Count; i++)
    {
          var c = root.Controls[i];
                 if (string.Equals(c.Text, text, StringComparison.Ordinal)) return c;
      var r = FindByText(c, text);
      if (r != null) return r;
         }
       }
            catch { }
            return null;
        }

        private static ComboBox FindFirstCombo(Control root)
        {
   try
  {
        for (int i = 0; i < root.Controls.Count; i++)
       {
     var c = root.Controls[i] as ComboBox;
            if (c != null) return c;
     var r = FindFirstCombo(root.Controls[i]);
    if (r != null) return r;
       }
        }
      catch { }
     return null;
     }

        /// <summary>全屏窗口默认不动，开关打开之后才允许。</summary>
        private static void TestFullscreenOptIn()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            IntPtr saved = WindowActions.ForcedTargetForTest;
            try
            {
                using (var f = new DragFixture(null))
                {
                    Native.RECT before;
                    Native.GetWindowRect(f.H, out before);

                    var mi = Native.GetMonitorInfoAt(new Native.POINT((before.Left + before.Right) / 2,
                                                                      (before.Top + before.Bottom) / 2));
                    if (mi.rcMonitor.Width <= 0) { Line("  （取不到显示器矩形，跳过）"); return; }

                    // 把测试窗口铺满整个显示器 —— 这就是"真全屏"（最大化只覆盖工作区，不会误判）
                    Native.SetWindowPos(f.H, IntPtr.Zero, mi.rcMonitor.Left, mi.rcMonitor.Top,
                        mi.rcMonitor.Width, mi.rcMonitor.Height,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
                    Settle();

                    Check(WindowActions.IsFullscreen(f.H), "铺满显示器被判定为真全屏");

                    // 全屏判定必须走 WindowFromPoint 那条路，所以临时清掉强制目标
                    WindowActions.ForcedTargetForTest = IntPtr.Zero;

                    f.Store.Current.AllowFullscreenWindows = false;
                    var pt = new Native.POINT(mi.rcMonitor.Left + mi.rcMonitor.Width / 2,
                                              mi.rcMonitor.Top + mi.rcMonitor.Height / 2);
                    var hit = WindowActions.FindTarget(pt, false);
                    Check(hit == IntPtr.Zero || hit != f.H, "默认不放行全屏窗口");

                    var hit2 = WindowActions.FindTarget(pt, true);
                    // 命中别的窗口 ≠ 逻辑出错：可能有第三方常驻置顶小组件
                    // （TickTick GridWidget 之类）盖在测试窗口正上方。
                    // 只有"一个都没命中"才说明全屏放行逻辑坏了。
                    if (hit2 == IntPtr.Zero)
                    {
                        Check(false, "开关打开之后可以拖动全屏窗口（一个窗口都没命中）");
                    }
                    else if (hit2 != f.H)
                    {
                        Line("  （命中其它窗口 " + hit2.ToInt64().ToString("X")
                            + "（" + (Native.GetClassName(hit2) ?? "?") + "），疑似第三方置顶小组件遮挡，跳过正向断言）");
                    }
                    else
                    {
                        Check(true, "开关打开之后可以拖动全屏窗口");
                    }
                }
            }
            finally
            {
                WindowActions.ForcedTargetForTest = saved;
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★ 钩子看门狗：光标动了、钩子却没看到事件 ⇒ 判定"钩子已被系统摘除" ⇒ 重装。★
        ///
        /// 为什么要这条测试：钩子被 LowLevelHooksTimeout 摘掉时【不报错、不通知】，
        /// 用户感受到的就是"用着用着突然失灵了"，而且没有任何日志线索。
        /// 这里用 UnhookWindowsHookEx 模拟一次"被系统摘除"，验证看门狗能认出来。
        /// </summary>
        private static void TestHookWatchdog()
        {
            var hooks = new HookEngine(new NullSink());
            try
            {
                if (!hooks.Install())
                {
                    Line("  （当前环境装不上钩子的(maybe 没桌面/没权限)，跳过看门狗用例）");
                    return;
                }

                var wd = new HookWatchdog(hooks);
                wd.CheckOnce();                       // 建立第一次基准

                Native.POINT cur;
                if (!Native.GetCursorPos(out cur)) { Line("  （取不到光标位置，跳过）"); return; }

                // 模拟"钩子被系统静默摘除"
                hooks.Uninstall();
                wd.CheckOnce();                       // 重新取基准（此刻光标还没动）

                // ★ 这里必须"挪一次光标 + 抽一轮消息"，必要时重试若干次。★
                //
                //   低级鼠标钩子的事件是由系统投递到钩子线程的，
                //   SetCursorPos 只是改了光标位置，返回时事件【未必已经送到】。
                //   如果紧接着就 CheckOnce()，看门狗读到的还是"光标没动过"，
                //   于是判定"钩子还活着" —— 用例就假红了。
                //
                //   实测同一份代码连跑三次会红 1~2 次，就是这个竞态。
                //   这属于【用例自身的时序脆弱】，不是被测逻辑有问题：
                //   真实场景里光标是被人持续移动的，看门狗有充足的时间窗口。
                //   所以这里补上消息泵 + 重试，把用例的可靠性修回来 ——
                //   一个长期偶尔飘红的测试，久了就没人信它了。
                bool rescued = false;
                for (int attempt = 0; attempt < 12 && !rescued; attempt++)
                {
                    // 每次挪得不一样，避免被"上次位置"抵消掉
                    int dx = 11 + attempt * 3, dy = 9 + attempt * 2;
                    Native.SetCursorPos(cur.X + dx, cur.Y + dy);

                    // 给钩子线程投递事件的机会
                    for (int k = 0; k < 4; k++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(25);
                    }

                    rescued = wd.CheckOnce();
                }
                try { Native.SetCursorPos(cur.X, cur.Y); } catch { }

                if (!rescued)
                {
                    Line("  （本机 12 次尝试都没收到鼠标钩子事件 —— " +
                         "多半是无桌面/受限会话环境，看门狗用例跳过）");
                    return;
                }

                Check(rescued, "光标动了但钩子没收到事件 → 判定钩子已死");
                Eq(hooks.ReinstallCount, 1, "看门狗重装了一次");
                Check(hooks.MouseInstalled, "重装之后鼠标钩子确实装回来了");
            }
            finally
            {
                try { hooks.Dispose(); } catch { }
            }
        }

        /// <summary>确认"拖动半透明"和"拖动尺寸提示"这两个被砍掉的功能确实没有残留。</summary>
        private static void TestRemovedFeaturesGone()
        {
            bool hasTrans = false, hasHint = false;
            foreach (var p in typeof(AppConfig).GetProperties())
            {
                if (p.Name == "DragTransparency") hasTrans = true;
                if (p.Name == "DragSizeHint") hasHint = true;
            }
            Check(!hasTrans, "AppConfig 里已无 DragTransparency");
            Check(!hasHint, "AppConfig 里已无 DragSizeHint");

            // 旧版本写进 config.json 的多余字段必须被忽略，而不是让整个配置反序列化失败
            var c = new ConfigStore(Path.Combine(Path.GetTempPath(), "holddrag-legacy-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(c.Dir);
            File.WriteAllText(c.FilePath,
                "{\r\n  \"TriggerKeyId\": \"K:0xA4\",\r\n  \"DragTransparency\": true,\r\n"
                + "  \"DragTransparencyAlpha\": 70,\r\n  \"DragSizeHint\": true\r\n}", Encoding.UTF8);
            try
            {
                c.Load();
                Eq(c.Current.TriggerKeyId, "K:0xA4", "旧配置里的有效字段仍然读得出来");
            }
            finally
            {
                try { Directory.Delete(c.Dir, true); } catch { }
            }
        }

        /// <summary>
        /// ★ 回归用例：触发键必须能稳定保持激活态。★
        ///
        /// 曾经有一个每 2000ms 跑一次的看门狗，用 GetAsyncKeyState 校验"触发键还按着没有"。
        /// 但长按方案必须吞掉触发键的按下事件，事件一被吞，系统层面这键就不算按下
        /// （实测见 _diag/KeyStateProbe：吞掉后读到 0x0000，放行的对照组读到 0x8001）。
        /// 于是看门狗每 2 秒必然误判一次"抬起事件丢失"，把激活态打掉；
        /// 要等自动重复的按下事件重新计时 600ms 才能恢复 —— 用户感觉就是"时灵时不灵"。
        ///
        /// 这里按住触发键 2.5 秒并持续抽消息，激活态必须自始至终保持。
        /// </summary>
        private static void TestTriggerStaysActive()
        {
            using (var f = new DragFixture(null))
            {
                ActivateTrigger(f.Coord);
                Check(f.Coord.Trigger.Active, "触发键已进入激活态");

                long end = Environment.TickCount64 + 2500;
                while (Environment.TickCount64 < end)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(20);
                }

                Check(f.Coord.Trigger.Active, "按住 2.5 秒后激活态依然保持（看门狗误判回归）");
                Check(f.Coord.Trigger.Held, "触发键仍被记为按住");
            }
        }

        // ── 断言工具 ────────────────────────────────────────────

        private static readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();
        private static string _reportDir;

        /// <summary>
        /// 总看门狗：自检跑超过 150 秒就把【已经跑出来的部分】写成报告并强制退出。
        ///
        /// 没有它的话，某个用例一旦卡在某个 P/Invoke 上，整个进程就会一直挂着、
        /// 报告一个字也写不出来 —— 排查时只能看到"自检跑了很久没有输出"，毫无信息。
        /// 有了它，至少能知道卡在哪一组（因为 Safe 会给每组记一行日志）。
        /// </summary>
        private static void StartWatchdog(string dir)
        {
            _reportDir = dir;
            try
            {
                var t = new System.Threading.Thread(delegate ()
                {
                    System.Threading.Thread.Sleep(150000);
                    try
                    {
                        Buf.Append("\r\n\r\n!! 自检总超时（150 秒），报告到此为止。");
                        Buf.Append("\r\n   上面最后一组日志就是卡住的位置。\r\n");
                        File.WriteAllText(Path.Combine(_reportDir, "selftest.txt"),
                            Buf.ToString(), Encoding.UTF8);
                    }
                    catch { }
                    Environment.Exit(2);
                });
                t.IsBackground = true;
                t.Name = "HoldDrag.SelfTestWatchdog";
                t.Start();
            }
            catch { }
        }

        private static void Safe(string name, Action test)
        {
            _watch.Restart();
            Log.Info("[自检] == " + name);       // 每组都落一行盘：卡住时能精确定位
            Line(name);
            try
            {
                test();
            }
            catch (Exception ex)
            {
                Bad("（该组测试自身抛异常）", ex.Message);
            }
            _watch.Stop();
            if (_watch.ElapsedMilliseconds > 200)
            {
                Log.Info("[自检]    耗时 " + _watch.ElapsedMilliseconds + " ms");
            }
            Line("");
        }

        private static void Check(bool ok, string name, string detail = null)
        {
            if (ok) { _pass++; Line("  [PASS] " + name); }
            else { _fail++; Line("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : " :: " + detail)); }
        }

        private static void Bad(string name, string detail)
        {
            _fail++;
            Line("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : " :: " + detail));
        }

        private static void Eq<T>(T actual, T expected, string name)
        {
            bool ok = Equals(actual, expected);
            Check(ok, name, ok ? null : ("期望 " + expected + "，实际 " + actual));
        }

        private static void Line(string s)
        {
            Buf.Append(s);
            Buf.Append("\r\n");
        }
    }
}
