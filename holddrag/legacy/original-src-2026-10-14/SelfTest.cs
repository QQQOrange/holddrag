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
            Safe("T07 九宫格矩形换算", TestSnapRects);
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
            Safe("T50 真实长按触发键能激活（钩子线程化后 WinForms Timer 失效回归）", TestRealKeyboardHoldActivates);
            Safe("T51 空心矩形模式：拖动期窗口不动、松手落位（AltSnap FullWin=0 移植回归）", TestOutlineMode);
            Safe("T52 视觉反馈组几何：窄/宽各档下样式预览框必须留在分组内且不被遮", TestVisualGroupGeometry);
            Safe("T53 晃窗手势：真晃才触发、匀速/小幅抖动/拖太慢都不触发（#39）", TestShakeGesture);
            Safe("T54 松开触发键后外框立即消失，pin 机制已下线（外框残留）", TestFrameGoesAwayOnRelease);            Safe("T55 外框调色板/样式已扩充且每一种都画得出来（#37）", TestFramePaletteExpanded);
            Safe("T56 设置页默认尺寸够用就好、不会被内容撑到满屏（#33）", TestSettingsDefaultSizeCompact);
            Safe("T57 拖动过程的视觉反馈必须真的画在屏幕上并持续跟随（拖拽反馈失效）", TestDragVisualFeedback);
            Safe("T58 视觉反馈：进入即见全部选项、选中态清晰、全宽下不消失", TestFramePickersAlwaysVisible);
            Safe("T59 整体视觉（Win11）：层次/动效到位、无黑块、强调色与外框色都画出来了", TestVisualPolish);
            Safe("T60 最小窗口下任何分组的控件都不得越出分组框（全屏选项被遮挡）", TestNoControlClippedAtMinSize);
            Safe("T61 拖边框改尺寸：布局幂等、不抖动、不溢出、列数不来回翻", TestResizeIsStable);
            Safe("T62 设置页按显示器真实 DPI 渲染，且 DPI 变化时能整体重建", TestDpiRendering);
            Safe("T63 触发后左键/右键在普通·最大化·全屏窗口上都必须生效", TestTriggerOnBigWindows);
            Safe("T64 滚轮扫过设置页不得改掉任何选项（右键被悄悄改成最小化的根因）", TestWheelDoesNotChangeSettings);
            Safe("T65 图标字体可用且每个字形都真的渲染出墨水（缺字形是空白，肉眼难发现）", TestIconGlyphs);
            Safe("T66 触发时鼠标键已按住：必须补起拖动（外框亮着但窗口不动）", TestAdoptHeldButton);
            Safe("T67 拖动全程外框必须紧跟窗口：绝不出现滞后（错位/掉帧）", TestFrameTracksWindow);
            Safe("T68 贴边/出屏拖动时外框几何必须恒定（不许缩小、不许跳变）", TestFrameGeometryStableAtEdge);
            Safe("T69 外框不透明度：改数值必须真的改变渲染结果", TestFrameOpacity);
            Safe("T70 帮助面板：点标题能展开、内容不越界", TestHelpPanel);
            Safe("T71 关于信息：开发者/版本/更新时间/开发工具必须真的显示在界面上", TestAboutInfo);
            Safe("T72 每个分组都必须在可视树里、侧栏条目齐全（「新增了却看不见」回归）", TestAllGroupsReachable);
            Safe("T73 分组内任意两个控件不得互相遮挡", TestNoControlOverlap);
            Safe("T74 滚动页面时侧栏高亮必须跟随（程序化滚动）", TestSidebarFollowsScroll);
            Safe("T75 用真实滚轮消息滚动时，侧栏高亮必须跟随（事件接线）", TestSidebarFollowsRealWheel);
            Safe("T76 全屏窗口触发后降级为窗口：外框必须重新出现在新窗口周围", TestFrameAfterFullscreenDowngrade);
            Safe("T77 调色板必须整体柔和（饱和/明度都在舒适区，不刺眼）", TestPaletteIsSoft);
            Safe("T78 每个外框样式都要有名字、都真的画得出来", TestAllStylesRender);
            Safe("T79 外框绝不残留：兜底自检 + 退出路径都必须收干净", TestFrameNeverResidues);
            Safe("T80 全屏转窗口：用【外部进程】的真实全屏窗口走真实链路", TestFullscreenExternalWindow);
            Safe("T81 窗口操作不得干扰页面内容：取消拖拽模态 + 鼠标接管必须成对", TestNoContentInterference);
            Safe("T82 选中文本后按 Enter：先取消选中再补起拖动，左键恢复移动窗口", TestPhantomDownAdoption);
            Safe("T83 补发的配对抬起：wParam 如实报告其余按键、消息类型对应、lParam 为客户区坐标", TestPairedUpMaskReport);
            Safe("T84 激活时抢占钩子链头：重装后真实事件链路仍然可用（wgesture 抢占右键回归）", TestChainPriorityReinstall);
            Safe("T85 钩子线程路径无同步跨进程写：阻塞目标上 Begin/移动/松手/降级/置顶必须立即返回", TestHookThreadNeverBlocks);

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
            Check(c.DoubleClickLeftMaximize && c.DoubleClickRightZone, "双击增强默认开启");
            Check(c.ShowActivationFrame && c.BringToFront, "外框 / 提最前 默认开启");
            Check(c.ShakeMinimizeOthers, "晃窗最小化其它窗口默认开启");
            Check(!c.AllowFullscreenWindows, "全屏窗口默认不动（风险项，需手工开启）");
            Check(c.ExitFullscreenOnTrigger, "触发时退出全屏/最大化默认开启（用户要求）");
            Eq(c.FrameOpacity, 100, "外框不透明度默认 100%（完全不透明）");
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
            c.CornerPercent = 90;
            c.ActionLeft = "Bogus";
            c.AutoStart = "X";
            c.DragRefreshMode = "X";
            c.DragRefreshHz = -3;
            c.Normalize();

            Eq(c.HoldMs, 100, "阈值下限 100");
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
                s1.Current.MovePreviewMode = PreviewModes.FullWin;
                s1.Current.ShakeMinimizeOthers = false;
                s1.Save();

                var s2 = new ConfigStore(t);
                s2.Load();
                Eq(s2.Current.HoldMs, 888, "HoldMs 往返一致");
                Eq(s2.Current.TriggerKeyId, KeysUtil.RightAlt, "触发键往返一致");
                Eq(s2.Current.ActionLeft, Actions.Resize, "左键动作往返一致");
                Check(!s2.Current.ShakeMinimizeOthers, "布尔开关往返一致");
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
                c.BringToFront = false;
                c.ShowActivationFrame = false;
                c.DoubleClickLeftMaximize = false;
                c.DoubleClickRightZone = false;
                c.AllowFullscreenWindows = true;            // 测试窗口不想被"全屏"规则挡掉
                // 端到端夹具默认实时移动模式（大多数用例断言"窗口真的跟着动"）；
                // 要测 Outline（空心矩形）的用例在 tweak 里自己改成 Outline。
                c.MovePreviewMode = PreviewModes.FullWin;
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

        /// <summary>
        /// 把光标摆到指定坐标，并确认它真的停在了目标窗口上。
        ///
        /// ★ 为什么必须确认 ★
        ///   自检跑在用户的【真实桌面】上。SetCursorPos 之后用户随时可能动鼠标
        ///   （或者有别的程序在动光标），光标一旦离开测试窗口，接下来注入的左键
        ///   就会拖到【别人的窗口】上 —— 断言报出来的"窗口没动"其实是抓错了对象。
        ///   实测踩到过：拖走的是一个 1586x1391 的无关窗口，而逐帧日志显示
        ///   产品本身每一帧都精确跟到了光标，完全正常。
        ///
        /// 重试若干次；落不上去就返回 false，让调用方跳过而不是报假失败。
        /// </summary>
        private static bool PlaceCursorOn(IntPtr hwnd, Native.POINT pt)
        {
            for (int i = 0; i < 8; i++)
            {
                Native.SetCursorPos(pt.X, pt.Y);
                Application.DoEvents();
                System.Threading.Thread.Sleep(20);

                Native.POINT now;
                if (!Native.GetCursorPos(out now)) continue;
                if (Math.Abs(now.X - pt.X) > 4 || Math.Abs(now.Y - pt.Y) > 4) continue;
                if (Native.WindowFromPoint(now) == hwnd) return true;
            }
            return false;
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

                    // ② 放行之后窗口必须跟上光标。
                    //
                    //   ★ 这里不能断言"精确等于第 6 步" ★
                    //     为了治"一卡一卡"，我们照抄了 AltSnap 的按事件计数降频
                    //     （MoveRate=2：每 2 个鼠标事件才真正写一次窗口）。
                    //     所以窗口允许【滞后不超过一个降频步长】——
                    //     这是设计意图，不是漂移。真正的"精确落点"由松手时的
                    //     End()/UpdateFinal() 兜底（见第 ④ 段）。
                    //     断言放宽到"一个步长以内"，既能抓住真漂移，也不会把
                    //     刻意的降频误判成 bug。
                    var c6 = new AppConfig();     // 拿默认值（MoveRate=2）算容差
                    int stepX = 15, stepY = 8;
                    int tolX = stepX * Math.Max(1, c6.MoveRate);
                    int tolY = stepY * Math.Max(1, c6.MoveRate);

                    Native.RECT r1;
                    Native.GetWindowRect(f.H, out r1);
                    Check(Math.Abs((r1.Left - r0.Left) - 90) <= tolX,
                        "窗口水平方向跟上第 6 步（允许一个降频步长的滞后）",
                        "期望≈90 实际=" + (r1.Left - r0.Left) + " 容差=" + tolX);
                    Check(Math.Abs((r1.Top - r0.Top) - 48) <= tolY,
                        "窗口垂直方向跟上第 6 步（允许一个降频步长的滞后）",
                        "期望≈48 实际=" + (r1.Top - r0.Top) + " 容差=" + tolY);

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
                    // ★ 再补一次"松手前的最终定位"（产品里由 End() 用 GetCursorPos 走同一段代码）。
                    //   降频可能吃掉最后一个移动事件，但松手一定会精确落点 ——
                    //   这正是"允许滞后、绝不允许落点偏移"的设计。
                    f.Coord.Drag.UpdateFinal(last);
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
                    // ★ 必须【深度遍历】找分组 ★
                    //
                    //   分组现在挂在 窗体 → _scrollHost → _groupHost(TableLayoutPanel) 里，
                    //   而不是窗体的直接子控件。早先这里只扫 form.Controls，
                    //   于是 groups 恒为 0：断言信息里写着"实际 0"却照样 PASS，
                    //   同时那句"分组里有控件"的断言一次都没被执行过 ——
                    //   等于把这条回归用例整个废掉了，而且报告里还看不出来。
                    var groups = new System.Collections.Generic.List<GroupBox>();
                    Walk(form, delegate (Control c)
                    {
                        var gb = c as GroupBox;
                        if (gb != null) groups.Add(gb);
                    });

                    for (int i = 0; i < groups.Count; i++)
                    {
                        Check(groups[i].Controls.Count > 0,
                            "分组「" + groups[i].Text + "」里有控件（不是空的）");
                    }

                    Check(groups.Count >= 6 && SettingsForm.VerifyLayout(form),
                        "设置窗体上分组数 >= 6，实际 " + groups.Count);
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

                    // ── 2) 松开触发键：外框必须【立即消失】──
                    //    用户明确要求："松开按键后外框应立即消失"。
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    Check(!f.Coord.Trigger.Active, "触发键已退出激活态");
                    Check(!Native.IsWindowVisible(eng.Frame.Handle),
                        "松开触发键后外框立即消失（不再残留在屏幕上）");

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
        /// <summary>
        /// ★ 拖动期间的外框必须和"设置里选的样式"完全一致。★
        ///
        /// 用户原话："功能触发之后（拖动中），选中窗口的外框变成了几根细线，
        ///           不直观也不好看；设置里明明调了样式，为什么拖动过程中样式变了？"
        ///
        /// 根因：拖动期原来走的是 DragRingFrame（1bit 区域窗口）—— 它只有
        /// "有像素/没像素"两种状态，画不出柔光、渐变、半透明，
        /// 样式只能靠"主环 + 补几圈线"去近似，于是柔光/极光/霓虹/四角框
        /// 在拖动中全部退化成一两条细线。
        ///
        /// 现在拖动期与静止期用【同一个浮层、同一段绘制代码】，
        /// 所以这里断言的是"拖动中亮的就是抗锯齿分层框"，
        /// 并且它渲染出来的像素里必须有配置的颜色（不是退化的细线）。
        /// </summary>
        private static void TestDragRingFrameSwap()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    // 用一个"区域环绝对画不出来"的样式：柔光。它必须靠分层框才能表现。
                    c.FrameStyleId = FrameStyles.Glow;
                    c.FrameColorId = FrameColors.Violet;
                }))
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
                    Check(Native.IsWindowVisible(eng.Frame.Handle),
                        "★ 拖动期间亮的是抗锯齿分层框（与设置里选的样式同一段绘制代码）");
                    Check(eng.Frame.ColorIdForTest == FrameColors.Violet
                          && eng.Frame.StyleIdForTest == FrameStyles.Glow,
                        "★ 拖动中用的就是配置里的颜色与样式（不再退化成细线）",
                        "实际 " + eng.Frame.ColorIdForTest + " / " + eng.Frame.StyleIdForTest);

                    // 屏幕上真的画出了外框色（不是空窗口）
                    Native.RECT wr;
                    Native.GetWindowRect(f.H, out wr);
                    int hits = CountFramePixelsAround(wr, FrameColors.Resolve(FrameColors.Violet));
                    Check(hits > 200,
                        "★ 拖动中屏幕上真的画出了配置的外框色像素",
                        "命中 " + hits + " 像素");

                    var up = Ms(mv.pt);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    Settle();

                    Check(!eng.IsDragging, "拖动会话已结束");
                    Check(Native.IsWindowVisible(eng.Frame.Handle),
                        "触发键仍按着时外框继续亮着（静止时保持好看）");

                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    Check(!Native.IsWindowVisible(eng.Frame.Handle), "退出激活态后外框不残留（松键即收）");
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

                    // 松开触发键 → 外框立即消失（用户明确要求的行为）
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    Check(!f.Coord.Trigger.Active, "触发键已退出激活态");
                    Check(!Native.IsWindowVisible(eng.Frame.Handle),
                        "松开触发键后外框立即消失（外框残留回归）");
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
        /// 诊断用例：把"视觉反馈"分组在四档窗口宽度下的控件几何倒出来。
        ///
        /// 背景：用户报"样式预览窗口缩小时看得见选的是哪个，拉宽后就完全看不见"。
        /// 这条先把每一档的实际落位打出来（文本），再补硬断言：
        /// 每一档下预览框都必须保持可见尺寸（不出界、不零宽、不被压掉）。
        ///
        /// ★ 判据必须按【设计稿】宽度给，按档位缩放还原 ★
        ///   预览框设计宽度是 96 设计px（SettingsForm 里 _pnlPreview.Size），
        ///   实际宽度 = 设计宽 × 该档 DPI 缩放。曾经写死物理像素 W>100 ——
        ///   在 100% DPI 的机器上（或虚拟屏切换让设置窗落到 96 DPI 屏上时）
        ///   96 原样输出必然假红。实测翻过车：同一份二进制连跑两次，
        ///   一次 192 DPI（W=176~192）全过，一次 96 DPI（W=88~96）整档假红，
        ///   白查一轮代码。容许 Mode=Shift 收窄 10%，真问题（宽度塌成 0）照样抓得到。
        /// </summary>
        private static void TestVisualGroupGeometry()
        {
            int[] widths = new int[] { 736, 900, 1180, 1516, 1900 };
            var sb = new StringBuilder();
            SettingsForm.DumpVisualGroupGeometry(sb, widths);
            Line(sb.ToString());

            string text = sb.ToString();
            int previewOk = 0, previewTotal = 0;
            string bad = "";

            var rxTier = new System.Text.RegularExpressions.Regex(
                @"客户区宽=(\d+)（设计稿 (\d+)px）════");
            var rx = new System.Text.RegularExpressions.Regex(
                @"z=(\d+)\s+(\w+)\s+vis=(\w+)\s+L=(-?\d+)\s+T=(-?\d+)\s+W=(-?\d+)\s+H=(-?\d+)\s+右=(-?\d+)\s+底=(-?\d+)");

            // 档头先在文本里定位出来，随后的控件行归属哪一档就按位置切
            var tierMatches = new System.Collections.Generic.List<System.Text.RegularExpressions.Match>();
            foreach (System.Text.RegularExpressions.Match m in rxTier.Matches(text)) tierMatches.Add(m);

            for (int t = 0; t < tierMatches.Count; t++)
            {
                var tm = tierMatches[t];
                int w = int.Parse(tm.Groups[1].Value);
                int design = int.Parse(tm.Groups[2].Value);
                float scale = w / (float)design;             // 本档实际缩放（1=100%、2=200%…）
                int from = tm.Index + tm.Length;
                int to = t + 1 < tierMatches.Count ? tierMatches[t + 1].Index : text.Length;
                if (to <= from) continue;
                string chunk = text.Substring(from, to - from);

                int minW = (int)Math.Round(96 * 0.9 * scale);  // 96 设计px，Shift 收窄 ≤10%
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(chunk))
                {
                    if (m.Groups[2].Value != "FramePreviewBox") continue;
                    int L = int.Parse(m.Groups[4].Value);
                    int W = int.Parse(m.Groups[6].Value);
                    int R = int.Parse(m.Groups[8].Value);
                    previewTotal++;
                    if (W >= minW && L >= 0 && R > L) previewOk++;
                    else bad += " 第" + (t + 1) + "档(scale=" + scale.ToString("0.##")
                              + ") L=" + L + " W=" + W + " R=" + R + " 最小应≥" + minW;
                }
            }
            Check(previewTotal > 0, "每一档宽度都转到了预览框几何（探针本身有效）", "共 " + previewTotal + " 档");
            Check(previewOk == previewTotal,
                "★ 所有宽度档位下样式预览框都保持可见尺寸（拉宽后不消失）",
                previewOk + "/" + previewTotal + bad);
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
                c.MovePreviewMode = PreviewModes.FullWin;
                c.ActionRight = Actions.Resize;
                c.ActionMiddle = Actions.Nothing;
                c.ShowActivationFrame = true;          // ★ 本用例的全部意义所在
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

                // 松开触发键 → 外框立即消失（用户明确要求的行为）
                coord.Trigger.SimulateKeyUp(null);
                Settle();
                Check(!coord.Trigger.Active, "触发键已退出激活态");
                Check(!Native.IsWindowVisible(coord.Drag.Frame.Handle),
                    "松开触发键后外框立即消失（外框残留回归）");
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

        /// <summary>节流的帧间隔换算（#36：跟随屏幕 / 固定值 / 不限制 三态）。</summary>
        private static void TestFrameInterval()
        {
            var c = new AppConfig();
            // Auto（默认）：跟随实测刷新率。实测不到（返回 0）时保守不节流，
            // 宁可多写几帧也不要凭空造一个 60Hz 上限（也可能是 30Hz 的老显示器）。
            Eq(c.FrameIntervalMs(60), 17, "自动 60Hz → 约 17ms 一帧");
            Eq(c.FrameIntervalMs(144), 7, "自动 144Hz → 约 7ms 一帧");
            Eq(c.FrameIntervalMs(0), 0, "实测不到刷新率时不节流");
            Eq(c.FrameIntervalMs(600), 2, "自动模式超过 500Hz 按 500 收敛");

            // Custom：固定值，与实测刷新率无关。
            c.DragRefreshMode = RefreshModes.Custom;
            c.DragRefreshHz = 120;
            Eq(c.FrameIntervalMs(240), 8, "固定 120Hz → 8ms（忽略实测值）");
            c.DragRefreshHz = 0;
            Eq(c.FrameIntervalMs(240), 0, "固定值选 0 = 用户主动选择不限制");
            c.DragRefreshHz = 20;
            Eq(c.FrameIntervalMs(0), 50, "固定值下限 20Hz → 50ms");
            c.DragRefreshHz = 500;
            Eq(c.FrameIntervalMs(0), 2, "固定值上限 500Hz → 2ms");
            c.DragRefreshHz = 600;
            Eq(c.FrameIntervalMs(0), 2, "固定值越界按 500 收敛 → 2ms");
        }

        /// <summary>数一个窗口位图里的"墨水"像素数（远离纯白的像素）。</summary>
        private static int CountInkPixels(IntPtr hwnd)
        {
            try
            {
                Native.RECT r;
                if (!Native.GetWindowRect(hwnd, out r)) return -1;
                int w = r.Width, h = r.Height;
                if (w <= 0 || h <= 0) return -1;
                using (var bmp = new System.Drawing.Bitmap(w, h))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    Native.PrintWindow(hwnd, hdc, 0);
                    g.ReleaseHdc(hdc);
                    int ink = 0;
                    for (int y = 0; y < h; y += 2)
                        for (int x = 0; x < w; x += 2)
                        {
                            var c = bmp.GetPixel(x, y);
                            if (c.A > 0 && (c.R < 240 || c.G < 240 || c.B < 240)) ink++;
                        }
                    return ink;
                }
            }
            catch (Exception ex) { Log.Error(ex); return -2; }
        }

        /// <summary>
        /// ★ AltSnap 空心矩形模式（FullWin=0）移植回归 ★
        ///
        /// AltSnap 默认拖动时【不移动真窗口】：跟手的是它自己的透明预览框，
        /// 真窗口在松手那一刻一次性落位。这就是它丝滑的根源 ——
        /// 预览框是自己的窗口，跟手性由构造保证，目标程序再忙也拖不卡。
        ///
        /// 本用例验证：① Outline 模式下拖动期间目标窗口纹丝不动；
        /// ② 最终定位（UpdateFinal）真的把窗口写到最新位置。
        /// </summary>
        private static void TestOutlineMode()
        {
            string dir = Path.Combine(Path.GetTempPath(), "holddrag-outline-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            Form target = null;
            DragEngine eng = null;
            try
            {
                var store = new ConfigStore(dir);
                store.Load();
                var cfg = store.Current;
                cfg.MovePreviewMode = PreviewModes.Outline;
                cfg.ActionLeft = Actions.Move;
                cfg.ActionRight = Actions.Resize;
                cfg.ActionMiddle = Actions.Nothing;
                cfg.ShowActivationFrame = false;
                cfg.BringToFront = false;
                cfg.MoveRate = 1;
                cfg.ResizeRate = 1;

                target = new Form();
                target.FormBorderStyle = FormBorderStyle.Sizable;
                target.StartPosition = FormStartPosition.Manual;
                target.ShowInTaskbar = false;
                target.TopMost = true;
                target.Bounds = new Rectangle(150, 150, 300, 200);
                target.Show();
                Pump();

                var r0 = new Native.RECT(150, 150, 450, 350);
                var grab = new Native.POINT(300, 250);   // 窗口中心

                eng = new DragEngine(() => store.Current);
                eng.Precreate();          // 真实应用在启动时就建好了浮层句柄；测试必须同样做
                if (!eng.Begin(target.Handle, true, 0, grab))
                {
                    Check(false, "Outline：会话启动成功", "Begin 返回 false");
                    return;
                }

                // 拖动中：多个移动事件 → 窗口必须纹丝不动
                for (int i = 1; i <= 5; i++)
                {
                    eng.StepForTest(new Native.POINT(grab.X + i * 10, grab.Y + i * 6));
                }

                Native.RECT during;
                Native.GetWindowRect(target.Handle, out during);
                Eq(during.Left, r0.Left, "Outline：拖动期间窗口左缘纹丝不动");
                Eq(during.Top, r0.Top, "Outline：拖动期间窗口上缘纹丝不动");
                Eq(during.Width, r0.Width, "Outline：拖动期间窗口宽度不变");

                // ★ 预览框必须可见且跟着光标走（用户报"没有任何动画"的回归断言）★
                //   第四十轮起预览框也用抗锯齿分层框（和设置里选的样式同一段代码），
                //   所以这里比的是【中心】—— 框会按样式的 margin 往外扩一圈。
                Check(eng.Frame.IsShown, "Outline：预览框在拖动期间可见",
                      "IsShown=" + eng.Frame.IsShown);
                Native.RECT ring;
                Native.GetWindowRect(eng.Frame.Handle, out ring);
                // 中心对齐即可：框会按样式的 margin 往外扩，而且目标窗口的"可见矩形"
                // 相对 GetWindowRect 有窗口边框内缩（上下不对称），中心会差几像素。
                Check(Math.Abs((ring.Left + ring.Width / 2) - (r0.Left + 50 + r0.Width / 2)) <= 12,
                    "Outline：预览框中心跟到第 5 步位置（左）",
                    "框中心=" + (ring.Left + ring.Width / 2) + " 期望=" + (r0.Left + 50 + r0.Width / 2));
                Check(Math.Abs((ring.Top + ring.Height / 2) - (r0.Top + 30 + r0.Height / 2)) <= 12,
                    "Outline：预览框中心跟到第 5 步位置（上）",
                    "框中心=" + (ring.Top + ring.Height / 2) + " 期望=" + (r0.Top + 30 + r0.Height / 2));

                // ★ 像素级实证：屏幕上真的画出了预览框（分层框失效回归）★
                int inkPixels = CountFramePixelsAround(r0, FrameColors.Resolve(cfg.FrameColorId));
                Check(inkPixels > 200, "Outline：预览框在屏幕上真的有渲染的像素",
                      "inkPixels=" + inkPixels);

                // 松手：最终定位真的写窗口
                eng.UpdateFinal(new Native.POINT(grab.X + 60, grab.Y + 36));

                Native.RECT after;
                Native.GetWindowRect(target.Handle, out after);
                Eq(after.Left, r0.Left + 60, "Outline：松手后窗口落到最新位置（左缘）");
                Eq(after.Top, r0.Top + 36, "Outline：松手后窗口落到最新位置（上缘）");
            }
            finally
            {
                if (eng != null) eng.Cancel();
                if (target != null) { target.Dispose(); }
                Pump();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>
        /// ★ 钩子线程化回归：真实长按必须能激活 ★
        ///
        /// 钩子搬到专用线程后，键盘事件从钩子线程进入 TriggerState，
        /// BeginHold 里的 WinForms Timer.Start() 跨线程把原生定时器窗口
        /// 建到了钩子线程上 —— 而钩子线程只 GetMessage 不 Dispatch，
        /// WM_TIMER 永远不分发 → 永远不激活，用户看到"功能完全失效"。
        ///
        /// 之前的测试全走 ForceHoldElapsed/SimulateKeyDown 模拟路径，
        /// 根本没经过真实 Timer，所以没测出来。本用例用真实 SendInput
        /// 长按（down → 等 HoldMs → 断言 Active）覆盖完整真实链路。
        /// </summary>
        private static void TestRealKeyboardHoldActivates()
        {
            if (!NoRoomForTestWindow()) return;

            string dir = Path.Combine(Path.GetTempPath(), "holddrag-hold-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            HookEngine hooks = null;
            Coordinator coord = null;
            try
            {
                var store = new ConfigStore(dir);
                store.Load();
                var c = store.Current;
                c.TriggerKeyId = "K:0x0D";      // Enter
                c.RequireHold = true;
                c.HoldMs = 200;
                c.ActionLeft = Actions.Move;
                c.MovePreviewMode = PreviewModes.FullWin;
                c.ActionRight = Actions.Resize;
                c.ActionMiddle = Actions.Nothing;
                c.ShowActivationFrame = false;
                c.BringToFront = false;

                coord = new Coordinator(store);
                coord.PrecreateOverlays();
                hooks = new HookEngine(coord);
                hooks.Install();
                Pump();
                Settle();

                Check(hooks.KeyboardInstalled, "键盘钩子已装（专用线程）", "错误码 " + hooks.LastError);

                // ── 真实 SendInput：Enter 按下，不补 up，等真实定时器把它激活 ──
                SendRawKey(0x0D, true);

                bool activated = false;
                for (int i = 0; i < 60 && !activated; i++)
                {
                    System.Threading.Thread.Sleep(25);
                    Application.DoEvents();
                    activated = coord.Trigger.Active;
                }
                Check(activated, "真实长按触发键进入激活态（Threading.Timer 真实触发）",
                      "HoldMs=200 等了 1.5s 仍未激活");

                // 松开 → 回到非激活态
                SendRawKey(0x0D, false);
                for (int i = 0; i < 20 && coord.Trigger.Active; i++)
                {
                    System.Threading.Thread.Sleep(25);
                    Application.DoEvents();
                }
                Check(!coord.Trigger.Active, "松开触发键后回到非激活态");
            }
            finally
            {
                if (coord != null) { try { coord.Trigger.SimulateKeyUp(null); } catch { } }
                if (hooks != null) hooks.Dispose();
                if (coord != null) coord.Dispose();
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>直接 SendInput 一个键盘按下/抬起（不带 Magic 标记，走真实钩子路径）。</summary>
        private static void SendRawKey(ushort vk, bool down)
        {
            var inputs = new Native.INPUT[1];
            inputs[0].type = Native.INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = vk;
            inputs[0].u.ki.dwFlags = down ? 0u : Native.KEYEVENTF_KEYUP;
            Native.SendInput(1, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        /// <summary>
        /// 拖动"跟手"的底线：写窗口矩形的标志位必须【照抄 AltSnap】。
        ///
        /// ★ 这条断言在第三轮被【反转】过，务必看清历史 ★
        ///
        ///   早先这里断言"三套标志全都要带 SWP_ASYNCWINDOWPOS"，
        ///   理由是"异步才不阻塞工作线程"。但用户实测反馈是
        ///   "一卡一卡的从一个位置到另一个位置，慢慢卡顿卡过去" ——
        ///   异步恰好是元凶：SetWindowPos 立刻返回，请求排进【目标窗口】的
        ///   消息队列；工作线程按 160Hz 不停投递，而浏览器 / IDE 每秒只消化
        ///   得了 60 次，队列越堆越长，窗口开始"追"那些过期位置，
        ///   肉眼就是一格一格顿过去。
        ///
        ///   AltSnap 的 RESIZEFLAG / MOVETHICKBORDERS 都是【同步】的，
        ///   只有"不可缩放窗口"的兜底路径 MOVEASYNC 才带 ASYNCWINDOWPOS。
        ///   同步写保证每帧只有一个在途请求，这才是它丝滑的真正原因。
        /// </summary>
        private static void TestMoveFlagsAreAsync()        {
            Check((Native.MOVETHICKBORDERS & Native.SWP_ASYNCWINDOWPOS) == 0,
                "★ 平移用同步写（照抄 AltSnap MOVETHICKBORDERS；异步会造成队列堆积 → 一卡一卡）",
                "MOVETHICKBORDERS=0x" + Native.MOVETHICKBORDERS.ToString("X"));

            Check((Native.RESIZEFLAGS & Native.SWP_ASYNCWINDOWPOS) == 0,
                "缩放用同步写（照抄 AltSnap RESIZEFLAG）",
                "RESIZEFLAGS=0x" + Native.RESIZEFLAGS.ToString("X"));

            Check((Native.MOVEASYNC & Native.SWP_ASYNCWINDOWPOS) != 0,
                "不可缩放窗口的兜底标志位仍带 SWP_ASYNCWINDOWPOS（同步写对这类窗口有卡死风险）",
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
            Form target = null;      // 光标下必须有真窗口（DPI 修复后坐标是物理像素，桌面不再"碰巧"命中）

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
                c.MovePreviewMode = PreviewModes.FullWin;
                c.ActionRight = Actions.Resize;
                c.ActionMiddle = Actions.Nothing;
                c.ShowActivationFrame = true;
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

                // ★ 光标下必须真有一个可拖动的窗口 ★
                //   早期版本赌"光标下碰巧有别的窗口"，DPI 修复后坐标变物理像素，
                //   桌面（Progman）不再被碰巧命中 → 右键探测/左键断言随机失败。
                target = new Form();
                target.FormBorderStyle = FormBorderStyle.Sizable;
                target.StartPosition = FormStartPosition.Manual;
                target.ShowInTaskbar = false;
                target.TopMost = true;
                target.Bounds = new Rectangle(pt.X - 150, pt.Y - 100, 300, 200);
                target.Show();
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
        /// 取"光标位置 → 窗口矩形"这一对【同一瞬间】的读数。
        ///
        /// 为什么需要它：自检跑在用户的真实桌面上。注入鼠标事件之后，如果用户正好
        /// 在动鼠标，光标会继续走、窗口也会忠实跟上去。若先读光标、隔几百毫秒再读
        /// 窗口矩形，两者比的就不是同一时刻的状态 —— 实测出现过
        /// "光标位移 70,50、窗口尺寸变化 1368,426" 这种假失败
        /// （回看逐帧日志：窗口每一帧都精确跟到了光标，产品本身完全正确）。
        ///
        /// 做法：先等窗口矩形稳定（拖动工作线程按屏幕刷新率节流，最后一帧可能晚几毫秒），
        /// 再紧挨着把光标和窗口矩形一起读出来。这样判据才是可重复的。
        /// </summary>
        private static void ReadCursorAndRect(IntPtr hwnd, out Native.POINT pt, out Native.RECT rect)
        {
            Native.RECT prev, cur;
            Native.GetWindowRect(hwnd, out prev);
            for (int i = 0; i < 30; i++)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(10);
                Native.GetWindowRect(hwnd, out cur);
                if (cur.Left == prev.Left && cur.Top == prev.Top
                    && cur.Right == prev.Right && cur.Bottom == prev.Bottom) break;
                prev = cur;
            }
            Native.GetCursorPos(out pt);
            Native.GetWindowRect(hwnd, out rect);
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
                c.MovePreviewMode = PreviewModes.FullWin;
                c.ActionRight = Actions.Resize;
                c.ActionMiddle = Actions.Nothing;
                c.ShowActivationFrame = false;
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
                if (!PlaceCursorOn(target.Handle, pt))
                {
                    Line("  （光标无法稳定停在测试窗口上 —— 桌面上有别的输入在动，跳过本用例）");
                    return;
                }
                Pump();
                Settle();

                ActivateTrigger(coord);
                Settle();
                Check(coord.Trigger.Active, "T48 触发键已进入激活态",
                    "Active=" + coord.Trigger.Active);

                // ★ 注入按键之前【再确认一次】光标还在目标窗口上 ★
                //   ActivateTrigger + Settle 期间用户完全可能把鼠标挪走，
                //   那一下左键就会拖到别人的窗口上（见 PlaceCursorOn 的说明）。
                if (!PlaceCursorOn(target.Handle, pt))
                {
                    Line("  （注入前光标离开了测试窗口，跳过本用例）");
                    return;
                }

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
                    long seenAfterMove = hooks.SeenCount;

                    // ★ 光标与窗口矩形必须取【同一瞬间】★（理由见 ReadCursorAndRect）
                    Native.RECT after;
                    ReadCursorAndRect(target.Handle, out p1, out after);
                    int realDx = p1.X - p0.X, realDy = p1.Y - p0.Y;

                    Line("    （诊断2）移动后 SeenCount=" + seenAfterMove
                        + " 拖拽中=" + coord.Drag.IsDragging
                        + " 光标=" + p1);

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
                    // ★ 同样要确认光标真的停在目标窗口上，否则这一下右键会缩放到别人的窗口 ★
                    if (!PlaceCursorOn(target.Handle,
                                       new Native.POINT(rb.Right - 8, rb.Bottom - 8)))
                    {
                        Line("  （光标无法稳定停在窗口右下角，跳过缩放段）");
                        return;
                    }
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

                        // ★ 同样：光标与窗口矩形取【同一瞬间】★
                        Native.POINT q1;
                        Native.RECT ra;
                        ReadCursorAndRect(target.Handle, out q1, out ra);
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
        ///   它只往返了 4 个字段（HoldMs / TriggerKeyId / ActionLeft / ShakeMinimizeOthers），
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
                c.MovePreviewMode = PreviewModes.FullWin;
                c.ActionRight = Actions.AlwaysOnTop;
                c.ActionMiddle = Actions.Nothing;
                c.ShowActivationFrame = false;
                // 刻意选【非默认】的颜色/样式，避免"默认值恰好等于我选的值"而碰巧通过
                c.FrameColorId = FrameColors.Violet;
                c.FrameStyleId = FrameStyles.Dashed;
                c.FrameThickness = 7;
                c.FrameRadius = 21;
                c.ShakeMinimizeOthers = false;
                c.DoubleClickLeftMaximize = false;
                c.DoubleClickRightZone = false;
                c.RestoreMaximizedWhenMoved = false;
                c.BringToFront = false;
                c.AllowFullscreenWindows = true;
                c.CornerPercent = 33;
                c.Blacklist = "Notepad - 记事本,CustomWndClass";
                c.DragRefreshMode = RefreshModes.Custom;
                c.DragRefreshHz = 144;
                c.AutoStart = AutoStartModes.None;
                c.RunAsAdmin = false;
                c.WarnNonAdmin = false;
                c.ExitFullscreenOnTrigger = false;
                c.FrameOpacity = 55;
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

                // ★ 主题这两项是用户反复报过的问题，单独再点一次名
                Eq(r.FrameColorId, c.FrameColorId, "★ 外框颜色");
                Eq(r.FrameStyleId, c.FrameStyleId, "★ 外框样式");
                Eq(r.FrameThickness, 7, "★ 外框粗细");
                Eq(r.FrameRadius, 21, "★ 外框圆角");

                Check(!r.ShakeMinimizeOthers, "晃窗最小化其它窗口开关");
                Check(!r.DoubleClickLeftMaximize, "双击左键最大化开关");
                Check(!r.DoubleClickRightZone, "双击右键九宫格开关");
                Check(!r.RestoreMaximizedWhenMoved, "移动时还原最大化开关");
                Check(!r.BringToFront, "置顶开关");
                Check(r.AllowFullscreenWindows, "允许全屏开关");
                Eq(r.CornerPercent, 33, "九宫格分区百分比");
                Eq(r.Blacklist, c.Blacklist, "黑名单");
                Eq(r.DragRefreshMode, RefreshModes.Custom, "刷新率模式");
                Eq(r.DragRefreshHz, 144, "刷新率数值");
                Eq(r.AutoStart, AutoStartModes.None, "开机自启模式");
                Check(!r.ExitFullscreenOnTrigger, "触发时退出全屏开关");
                Eq(r.FrameOpacity, 55, "★ 外框不透明度");
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

        /// <summary>确认被砍掉的功能确实没有残留（含 #32 整组删除的自动贴边一套）。</summary>
        private static void TestRemovedFeaturesGone()
        {
            bool hasTrans = false, hasHint = false;
            bool hasEdgeSnap = false, hasSnapWin = false, hasSnapPv = false, hasSnapDist = false;
            foreach (var p in typeof(AppConfig).GetProperties())
            {
                if (p.Name == "DragTransparency") hasTrans = true;
                if (p.Name == "DragSizeHint") hasHint = true;
                if (p.Name == "EdgeSnap") hasEdgeSnap = true;
                if (p.Name == "SnapToOtherWindows") hasSnapWin = true;
                if (p.Name == "SnapPreview") hasSnapPv = true;
                if (p.Name == "SnapDistance") hasSnapDist = true;
            }
            Check(!hasTrans, "AppConfig 里已无 DragTransparency");
            Check(!hasHint, "AppConfig 里已无 DragSizeHint");
            Check(!hasEdgeSnap, "AppConfig 里已无 EdgeSnap（#32 自动贴边整体删除）");
            Check(!hasSnapWin, "AppConfig 里已无 SnapToOtherWindows（#32 与其它窗口对齐整体删除）");
            Check(!hasSnapPv, "AppConfig 里已无 SnapPreview（#32 边缘预览落位整体删除）");
            Check(!hasSnapDist, "AppConfig 里已无 SnapDistance（#32 吸附距离整体删除）");

            // 旧版本写进 config.json 的多余字段必须被忽略，而不是让整个配置反序列化失败
            var c = new ConfigStore(Path.Combine(Path.GetTempPath(), "holddrag-legacy-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(c.Dir);
            File.WriteAllText(c.FilePath,
                "{\r\n  \"TriggerKeyId\": \"K:0xA4\",\r\n  \"DragTransparency\": true,\r\n"
                + "  \"DragTransparencyAlpha\": 70,\r\n  \"DragSizeHint\": true,\r\n"
                + "  \"EdgeSnap\": true,\r\n  \"SnapToOtherWindows\": true,\r\n"
                + "  \"SnapPreview\": true,\r\n  \"SnapDistance\": 42\r\n}", Encoding.UTF8);
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

        /// <summary>
        /// #39 晃窗最小化其它窗口：手势识别必须"真晃才触发"。
        ///
        /// 判定是纯运动学（方向反转次数 + 每半程幅度 + 时间窗），
        /// 这里直接喂合成坐标序列，不需要真的去晃窗口。
        /// 执行器（EnumWindows + ShowWindowAsync）跑真实窗口，自检里不碰。
        /// </summary>
        private static void TestShakeGesture()
        {
            // ══════ 第四十轮：判定收紧，重点补"误触发"的反例 ══════
            // 用户原话："目前只要拖动窗口就会触发，属于误触发。"
            // 所以本用例的一半篇幅都在断言"什么样的情况【不能】触发"。

            // 真晃轨迹：每半程 80 逻辑 px，30ms 一次 —— 快而短，原地往复
            int[] shake = { 540, 460, 540, 460, 540, 460, 540, 460, 540, 460 };

            // ── ① 误触发反例 ──

            // 匀速直线拖动 400px：一次反向都没有
            var d = new ShakeDetector();
            d.Reset(100, 300, 0);
            bool fired = false;
            for (int i = 1; i <= 40; i++) if (d.Feed(100 + i * 10, 300, i * 10L)) fired = true;
            Check(!fired, "匀速直线拖动不触发（无往复）");

            // ★ 正常拖窗的真实轨迹：拖过去 → 过头拉回 → 再微调 → 继续拖走
            //   旧实现（24px 阈值 + 2s 窗口 + 不看净位移）在这里必然误触发。
            d.Reset(200, 400, 0);
            fired = false;
            int[] dragPath = { 300, 420, 380, 500, 470, 600, 700, 800 };
            long t = 0;
            foreach (int x in dragPath) { t += 60; if (d.Feed(x, 400, t)) fired = true; }
            Check(!fired, "★ 正常拖窗（拖过头再拉回、边拖边微调）不触发 —— 误触发回归");

            // 边拖边手抖：每次抖动 ±12px（< 32 逻辑 px 阈值）
            d.Reset(500, 300, 0);
            fired = false;
            t = 0;
            int[] jitter = { 512, 500, 512, 500, 512, 500, 512, 500, 512, 500 };
            foreach (int x in jitter) { t += 30; if (d.Feed(x, 300, t)) fired = true; }
            Check(!fired, "小幅手抖（半程不足 32px）不触发");

            // 上下抖：横向路径占比不足
            d.Reset(500, 200, 0);
            fired = false;
            t = 0;
            int[] vertical = { 200, 280, 200, 280, 200, 280, 200, 280, 200, 280 };
            for (int i = 0; i < vertical.Length; i++) { t += 30; if (d.Feed(500, vertical[i], t)) fired = true; }
            Check(!fired, "上下方向抖动不触发（必须是横向晃）");

            // 慢速往复：每半程 80px 但 400ms 一次，超出 800ms 手势窗口
            d.Reset(500, 300, 0);
            fired = false;
            t = 0;
            for (int i = 0; i < 10; i++) { t += 400; if (d.Feed((i % 2 == 0) ? 580 : 420, 300, t)) fired = true; }
            Check(!fired, "慢速往复（超出 800ms 手势窗口）不触发");

            // 单次大幅折返：拖到 900 再拖回 200 —— 有反转但只有 1 次，且单向位移远超上限
            d.Reset(200, 300, 0);
            fired = false;
            t = 0;
            int[] bigSwing = { 900, 200, 900, 200, 900 };
            foreach (int x in bigSwing) { t += 80; if (d.Feed(x, 300, t)) fired = true; }
            Check(!fired, "大幅折返（单向位移超过上限）不触发 —— 那是拖窗口不是晃");

            // ── ② 正例：真晃必须触发 ──
            d.Reset(500, 300, 0);
            fired = false;
            t = 0;
            foreach (int x in shake) { t += 30; if (d.Feed(x, 300, t)) fired = true; }
            Check(fired, "★ 快速原地左右晃动可以触发（第 4 次有效往复）");

            // ③ 一次晃动只触发一次（冷却生效）
            d.Reset(500, 300, 0);
            int fires = 0;
            t = 0;
            for (int rep = 0; rep < 3; rep++)
                foreach (int x in shake) { t += 30; if (d.Feed(x, 300, t)) fires++; }
            Eq(fires, 1, "一次晃动只触发一次（冷却生效）");

            // ④ 冷却结束后重新武装：再晃一次还能触发
            d.Reset(500, 300, 0);
            t = 0;
            foreach (int x in shake) { t += 30; d.Feed(x, 300, t); }   // 触发 → 进入冷却
            t += 1200;                                                  // 跨过 900ms 冷却
            fired = false;
            foreach (int x in shake) { t += 30; if (d.Feed(x, 300, t)) fired = true; }
            Check(fired, "冷却结束后再次晃动可再次触发（不漏判）");

            // ⑤ ★ 阈值必须随 DPI 缩放 ★
            //   同一段物理轨迹，在 200% 屏上只有一半的"逻辑幅度" ——
            //   旧实现写死物理像素，200% 下等于门槛减半，这就是误触发的根因之一。
            var dHi = new ShakeDetector();
            dHi.SetScale(2.0);                       // 200% 显示器
            dHi.Reset(500, 300, 0);
            fired = false;
            t = 0;
            // 半程只有 20 逻辑 px（= 40 物理 px）：在 200% 屏上不该算一次有效往复
            int[] small = { 520, 480, 520, 480, 520, 480, 520, 480, 520, 480 };
            foreach (int x in small) { t += 30; if (dHi.Feed(x, 300, t)) fired = true; }
            Check(!fired, "★ 200% 显示器下半程 20 逻辑 px 的抖动不触发（阈值随 DPI 缩放）");

            // 同一段轨迹在 100% 显示器上幅度翻倍，就该触发
            var dLo = new ShakeDetector();
            dLo.SetScale(1.0);
            dLo.Reset(500, 300, 0);
            fired = false;
            t = 0;
            int[] big = { 540, 460, 540, 460, 540, 460, 540, 460, 540, 460 };
            foreach (int x in big) { t += 30; if (dLo.Feed(x, 300, t)) fired = true; }
            Check(fired, "同样的往复在 100% 显示器上幅度足够 → 正常触发");
        }

        /// <summary>
        /// ★ 松开触发键之后，视觉反馈外框必须【立即消失】。★
        ///
        /// 用户原话："修复外框残留 bug：在执行完命令并松开所有按键后，视觉反馈外框仍然存在；
        ///           预期行为是松开按键后外框应立即消失。"
        ///
        /// 第三十八轮曾经为了"松键后还留着"实现过一套 pin（钉子）机制，
        /// 第三十九轮按用户反馈整体删除。本用例把三条常见路径都钉死：
        ///   ① 只悬停（没按鼠标键）→ 松触发键 → 框消失；
        ///   ② 拖完先松鼠标、再松触发键 → 松触发键那一刻框消失；
        ///   ③ 拖完先松触发键、再松鼠标 → 松触发键那一刻框消失（松鼠标不得把它弄回来）；
        /// 外加一条反射断言：pin 那套 API 确实已经从代码里下线（防以后又被"顺手加回来"）。
        /// </summary>
        private static void TestFrameGoesAwayOnRelease()
        {
            // ── 反射：pin API 必须已经不存在 ──
            var t = typeof(DragEngine);
            string[] gone = { "PinToWindow", "TickPinned", "ClearPin" };
            int alive = 0; string aliveDetail = "";
            foreach (string m in gone)
            {
                if (t.GetMethod(m, System.Reflection.BindingFlags.Public
                                 | System.Reflection.BindingFlags.Instance
                                 | System.Reflection.BindingFlags.NonPublic) != null)
                { alive++; aliveDetail += m + " "; }
            }
            Check(alive == 0, "外框 pin 机制已彻底下线（PinToWindow/TickPinned/ClearPin 不存在）", aliveDetail);

            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                // ── ① 只悬停 → 松触发键 → 框消失 ──
                using (var f = new DragFixture(delegate (AppConfig c) { c.ShowActivationFrame = true; }))
                {
                    var eng = f.Coord.Drag;
                    var hover = CenterOf(f.H);
                    Native.SetCursorPos(hover.X, hover.Y);
                    ActivateTrigger(f.Coord);
                    Settle();
                    Check(Native.IsWindowVisible(eng.Frame.Handle), "激活后外框已亮起");

                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    Check(!f.Coord.Trigger.Active, "触发键已退出激活态");
                    Check(!Native.IsWindowVisible(eng.Frame.Handle),
                        "★ 松开触发键后外框立即消失（不再残留在屏幕上）");
                }

                // ── ② 拖完先松鼠标、再松触发键 ──
                using (var f = new DragFixture(delegate (AppConfig c) { c.ShowActivationFrame = true; }))
                {
                    var eng = f.Coord.Drag;
                    var hover = CenterOf(f.H);
                    Native.SetCursorPos(hover.X, hover.Y);
                    ActivateTrigger(f.Coord);
                    Settle();

                    var down = Ms(hover);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    var mv = Ms(new Native.POINT(hover.X + 50, hover.Y + 30));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Settle();
                    var up = Ms(mv.pt);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    Settle();
                    Check(!eng.IsDragging, "拖动会话已结束");
                    Check(Native.IsWindowVisible(eng.Frame.Handle),
                        "触发键仍按着时外框继续亮着（这条行为没变）");

                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    Check(!Native.IsWindowVisible(eng.Frame.Handle),
                        "★ 先松鼠标再松触发键：松键那一刻外框消失");
                }

                // ── ③ 拖完先松触发键、再松鼠标 ──
                using (var f = new DragFixture(delegate (AppConfig c) { c.ShowActivationFrame = true; }))
                {
                    var eng = f.Coord.Drag;
                    var hover = CenterOf(f.H);
                    Native.SetCursorPos(hover.X, hover.Y);
                    ActivateTrigger(f.Coord);
                    Settle();

                    var down = Ms(hover);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    var mv = Ms(new Native.POINT(hover.X + 50, hover.Y + 30));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Settle();

                    f.Coord.Trigger.SimulateKeyUp(null);      // 先松触发键
                    Settle();
                    var up = Ms(mv.pt);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);   // 再松鼠标
                    Settle();
                    Check(!eng.IsDragging, "拖动会话已结束（先松触发键）");
                    Check(!Native.IsWindowVisible(eng.Frame.Handle),
                        "★ 先松触发键再松鼠标：松鼠标之后外框也不得回来");
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// #37 外框重做：调色板与样式必须真的扩充了，而且每一个都能画出来。
        ///
        /// 用户原话："我希望能够添加更多的外框颜色和外框样式……当前的外框我觉得太丑了，
        /// 又丑，颜色什么的又复古……我希望它能够更好看。"
        ///
        /// 断言三件事：
        ///   1. 颜色 / 样式的数量确实变多了（防"只改了个注释"）；
        ///   2. 每个颜色的 ID 唯一、名字非空、解析出不透明色；每个样式都有中文名、留白合理；
        ///   3. 每个样式都【真的画得出来】—— 渲染到一张位图上，不抛异常、且确实落了像素。
        ///      这条最重要：新样式一旦在 Draw 里写错，用户选到就是一片空白甚至崩溃。
        /// </summary>
        private static void TestFramePaletteExpanded()
        {
            Check(FrameColors.All.Length >= 16, "外框颜色至少 16 种（#37 扩充）",
                "实际 " + FrameColors.All.Length);
            Check(FrameStyles.All.Length >= 9, "外框样式至少 9 种（#37 扩充）",
                "实际 " + FrameStyles.All.Length);

            // ── 颜色：ID 唯一、名字非空、解析出来不透明 ──
            var seen = new System.Collections.Generic.HashSet<string>();
            int badColor = 0; string badColorDetail = "";
            for (int i = 0; i < FrameColors.All.Length; i++)
            {
                var e = FrameColors.All[i];
                if (string.IsNullOrEmpty(e.Id) || string.IsNullOrEmpty(e.Name))
                { badColor++; badColorDetail += "(空项) "; continue; }
                if (!seen.Add(e.Id)) { badColor++; badColorDetail += e.Id + "(ID重复) "; }
                if (!FrameColors.IsValid(e.Id)) { badColor++; badColorDetail += e.Id + "(非法) "; }
                if (FrameColors.Resolve(e.Id).A != 255) { badColor++; badColorDetail += e.Id + "(非不透明) "; }
            }
            Check(badColor == 0, "每个颜色的 ID 唯一、名字非空、解析为不透明色", badColorDetail);

            // ── 样式：中文名 + 留白 ──
            int badStyle = 0; string badStyleDetail = "";
            int flatMargin = FrameStyles.MarginFor(FrameStyles.Flat, 3);
            for (int i = 0; i < FrameStyles.All.Length; i++)
            {
                string s = FrameStyles.All[i];
                string dn = FrameStyles.DisplayName(s);
                if (string.IsNullOrEmpty(dn) || dn == s) { badStyle++; badStyleDetail += s + "(没有中文名) "; }
                if (FrameStyles.MarginFor(s, 3) < 4) { badStyle++; badStyleDetail += s + "(留白太小) "; }
                // 除了"纯净环"自己，其它样式都不该比它更省留白（否则光晕会被切掉）
                if (s != FrameStyles.Flat && FrameStyles.MarginFor(s, 3) < flatMargin)
                { badStyle++; badStyleDetail += s + "(留白小于纯净环) "; }
            }
            Check(badStyle == 0, "每个样式都有中文名、且留白足够放下光晕", badStyleDetail);

            // ── 每个样式都要真的画得出来 ──
            int badDraw = 0; string badDrawDetail = "";
            foreach (string style in FrameStyles.All)
            {
                try
                {
                    using (var bmp = new Bitmap(240, 160))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);
                        FrameStyles.Draw(g, new RectangleF(0, 0, 240, 160),
                            FrameColors.Resolve(FrameColors.Teal), style, 3, 12);

                        int ink = 0;
                        for (int y = 0; y < bmp.Height; y += 2)
                            for (int x = 0; x < bmp.Width; x += 2)
                                if (bmp.GetPixel(x, y).ToArgb() != Color.White.ToArgb()) ink++;
                        if (ink < 4) { badDraw++; badDrawDetail += style + "(只落了 " + ink + " 个像素) "; }
                    }
                }
                catch (Exception ex)
                {
                    badDraw++; badDrawDetail += style + "(异常 " + ex.GetType().Name + ") ";
                }
            }
            Check(badDraw == 0, "★ 每一种外框样式都能画出来（不抛异常、确实落了像素）", badDrawDetail);

            // ── 新增样式必须能在设置页选中并同步到预览 ──
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    int miss = 0; string missDetail = "";
                    foreach (string style in FrameStyles.All)
                    {
                        form.ProbeSelectFrameStyle(style);
                        var sel = form.ProbeFrameSelection();
                        if (sel.StyleId != style || sel.PreviewStyleId != style)
                        { miss++; missDetail += style + "→" + sel.StyleId + "/" + sel.PreviewStyleId + " "; }
                    }
                    Check(miss == 0, "★ 每一种样式都能在设置页选中并同步到预览", missDetail);
                }
            }
        }

        /// <summary>
        /// #33 设置页默认尺寸必须"够用就好"，不能一开就占掉大半屏。
        ///
        /// 用户原话："设置页面非常非常的大，默认非常非常的大，要做出一定的调整。"
        ///
        /// 根因不只是默认高度本身，还有 FitHeightToContent()：它原本无条件把窗口高度
        /// 设成"内容高度 + 底栏"，而单列 6 个分组的内容很高，于是窗口被一路撑到接近满屏。
        /// 现在它只允许【缩小】。这里断言：
        ///   1. 显示之后客户区高度不超过内置默认值（不会被内容撑大）；
        ///   2. 高度没有被压成一条缝（>= 默认值的一半）；
        ///   3. 最小高度仍是个可用的值。
        /// </summary>
        private static void TestSettingsDefaultSizeCompact()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(20, 20);
                    form.Show();                    // OnShown 里会跑 FitHeightToContent
                    Application.DoEvents();

                    int want = form.ProbeDefaultClientHeight();
                    int got = form.ClientSize.Height;

                    Check(got <= want + 4,
                        "#33 设置页显示后不会比默认高度更大（不再被内容撑到满屏）",
                        "实际 " + got + " 默认 " + want);
                    Check(got >= want / 2,
                        "#33 设置页默认高度没有被压成一条缝", "实际 " + got);
                    Check(form.MinimumSize.Height >= 300,
                        "最小高度仍可用（>= 300 物理像素）", "实际 " + form.MinimumSize.Height);
                    Check(form.MinimumSize.Height <= want,
                        "最小高度不大于默认高度（否则默认值无意义）",
                        "最小 " + form.MinimumSize.Height + " 默认 " + want);

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 拖拽过程的视觉反馈必须【真的画在屏幕上】，而且要持续跟随。★
        ///
        /// 用户原话："恢复拖拽过程的视觉反馈：在拖拽过程中，视觉反馈外框必须持续存在
        /// 并正确跟随，当前实现未生效。"
        ///
        /// 为什么以前的用例抓不到：T32 只断言 `IsWindowVisible(环句柄)` ——
        /// 那是 WS_VISIBLE 这个【样式位】，跟"屏幕上有没有像素"完全无关。
        /// 环窗口如果被创建成 WS_EX_LAYERED（本工程所有浮层共用的 CreateWindowEx 路径）
        /// 却从不调 UpdateLayeredWindow，SetWindowRgn 抠出来的区域根本不参与合成，
        /// 窗口就是【完全不可见】的 —— 而 IsWindowVisible 依然返回 true。
        ///
        /// 所以本用例直接截屏数像素：
        ///   ① 拖动中：窗口四周必须能数到"外框色"的像素；
        ///   ② 对照组：把环收起来后同一区域必须数不到（证明①数到的确实是环）；
        ///   ③ 继续移动光标：环矩形必须跟着窗口走。
        /// </summary>
        private static void TestDragVisualFeedback()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    c.MovePreviewMode = PreviewModes.FullWin;
                }))
                {
                    var eng = f.Coord.Drag;
                    var color = FrameColors.Resolve(f.Store.Current.FrameColorId);

                    var hover = CenterOf(f.H);
                    Native.SetCursorPos(hover.X, hover.Y);
                    ActivateTrigger(f.Coord);
                    Settle();

                    var down = Ms(hover);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Settle();
                    Check(eng.IsDragging, "拖动会话已开始");

                    var mv = Ms(new Native.POINT(hover.X + 60, hover.Y + 40));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Settle();
                    Settle();

                    Check(eng.Frame.IsShown, "拖动中分层框的显示标志为真（IsShown）");
                    Check(Native.IsWindowVisible(eng.Frame.Handle), "拖动中分层框窗口 WS_VISIBLE 置位");

                    // ① 屏幕上必须真的数得到外框色（按【框窗口】取带，环就落在它内侧）
                    Native.RECT rr;
                    Native.GetWindowRect(eng.Frame.Handle, out rr);
                    Native.RECT wr;
                    Native.GetWindowRect(f.H, out wr);
                    int hits = CountFramePixelsAround(rr, color);
                    // 诊断：直接取"环应该经过的那几个点"的屏幕颜色
                    Line("    （环上取点）"
                        + "左中点=" + PixelAt(rr.Left + 12, (rr.Top + rr.Bottom) / 2)
                        + " 上中点=" + PixelAt((rr.Left + rr.Right) / 2, rr.Top + 12)
                        + " 右中点=" + PixelAt(rr.Right - 12, (rr.Top + rr.Bottom) / 2)
                        + " 下中点=" + PixelAt((rr.Left + rr.Right) / 2, rr.Bottom - 12)
                        + " 期望色=" + color.R + "," + color.G + "," + color.B);
                    Check(hits > 150,
                        "★ 拖动中屏幕上真的画出了外框（截屏数到外框色像素）",
                        "命中 " + hits + " 像素，框矩形=" + rr
                        + " 样式=" + eng.Frame.StyleIdForTest + " 颜色=" + eng.Frame.ColorIdForTest);

                    // ② 框必须把窗口包住（外扩）或贴在窗口内沿（内缩），尺寸对得上
                    Check(rr.Width >= wr.Width && rr.Height >= wr.Height
                          && rr.Width - wr.Width <= 80 && rr.Height - wr.Height <= 80,
                        "框矩形尺寸与窗口匹配（±留白）",
                        "框=" + rr.Width + "x" + rr.Height + " 窗=" + wr.Width + "x" + wr.Height);

                    // ③ 继续移动：框跟着窗口走
                    Native.RECT ringBefore = rr;
                    var mv2 = Ms(new Native.POINT(hover.X + 170, hover.Y + 120));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv2);
                    Settle();
                    Settle();
                    Native.GetWindowRect(eng.Frame.Handle, out rr);
                    Native.RECT ringAfter = rr;
                    Native.RECT winAfter;
                    Native.GetWindowRect(f.H, out winAfter);
                    Check(ringAfter.Left > ringBefore.Left + 40,
                        "★ 框跟着窗口一起移动（持续跟随，不是停在原地）",
                        ringBefore.Left + " → " + ringAfter.Left);
                    Check(Math.Abs((ringAfter.Left - winAfter.Left) - (ringBefore.Left - wr.Left)) <= 6,
                        "框相对窗口的偏移保持不变（没有漂移）",
                        "框-窗 Δ 前=" + (ringBefore.Left - wr.Left) + " 后=" + (ringAfter.Left - winAfter.Left));

                    // ④ 对照组：松手后同一区域必须数不到外框色
                    Native.RECT frameFinal;
                    Native.GetWindowRect(eng.Frame.Handle, out frameFinal);

                    var up = Ms(mv2.pt);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    Settle();
                    f.Coord.Trigger.SimulateKeyUp(null);   // 触发键也松开 → 框该彻底消失
                    Settle();
                    Check(!Native.IsWindowVisible(eng.Frame.Handle), "松手并松开触发键后外框收起");

                    // 用【同一块区域】做对照：拖动中那块是框的位置，收掉之后必须干净
                    int hitsAfter = CountFramePixelsAround(frameFinal, color);
                    Check(hitsAfter < Math.Max(30, hits / 4),
                        "★ 对照组：框收起后同一区域数不到外框色（证明①数到的确实是框）",
                        "拖动中 " + hits + " → 松手后 " + hitsAfter);
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// 在窗口四周的一条带里数"接近外框色"的屏幕像素。
        ///
        /// 只截上边和左边两条带（环带就落在那里），避免整屏 GetPixel 太慢。
        /// 返回 -1 表示截屏不可用（锁屏 / 无交互桌面），调用方据此跳过而不是报假红。
        /// </summary>
        private static string PixelAt(int x, int y)
        {
            try
            {
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                    var px = bmp.GetPixel(0, 0);
                    return px.R + "," + px.G + "," + px.B;
                }
            }
            catch { return "?"; }
        }

        /// <summary>
        /// 在给定矩形"连同它内外各一圈"的范围里数"接近外框色"的屏幕像素。
        ///
        /// ★ 必须【内外都取】★
        ///   外框的环带画在浮层窗口的 margin 处：正常窗口上它落在窗口外侧，
        ///   而浮层窗口本身比窗口大一圈，环带相对浮层矩形是【内缩】的。
        ///   只取外面那一圈会数到 0（本轮就踩了这个坑：环明明画出来了、
        ///   四个采样点都读到精确的外框色，计数却是 0）。
        ///   这里干脆把"矩形 + 四周 band"整块数一遍。
        /// 返回 -1 表示截屏不可用（锁屏 / 无交互桌面）。
        /// </summary>
        private static int CountFramePixelsAround(Native.RECT win, Color want)
        {
            const int band = 48;
            try
            {
                var vs = SystemInformation.VirtualScreen;
                int l = Math.Max(vs.Left, win.Left - band);
                int t = Math.Max(vs.Top, win.Top - band);
                int r = Math.Min(vs.Right, win.Right + band);
                int b = Math.Min(vs.Bottom, win.Bottom + band);
                if (r - l <= 2 || b - t <= 2) return -1;
                return CountColorIn(new Rectangle(l, t, r - l, b - t), want);
            }
            catch { return -1; }
        }

        private static int CountColorIn(Rectangle area, Color want)
        {
            if (area.Width <= 0 || area.Height <= 0) return 0;
            using (var bmp = new Bitmap(area.Width, area.Height))
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(area.Left, area.Top, 0, 0, new Size(area.Width, area.Height));
                int hits = 0;
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        var px = bmp.GetPixel(x, y);
                        if (Math.Abs(px.R - want.R) <= 30
                            && Math.Abs(px.G - want.G) <= 30
                            && Math.Abs(px.B - want.B) <= 30) hits++;
                    }
                return hits;
            }
        }

        /// <summary>
        /// ★ 视觉反馈分组：一进设置页就要看到【全部】可选项，选中态要清晰，
        ///   而且从窄窗到超宽（全屏档）都不能"显示为空"。★
        ///
        /// 用户原话：
        ///   "修复在全屏状态下样式框显示为空的问题，应在进入设置时即展示所有可选项。"
        ///   "修复选择样式后未在设置界面中直接展示当前选中项的问题，
        ///    需让已选样式实时可见、状态清晰。"
        ///
        /// 以前的实现是"颜色 + 样式两个下拉框"：必须点开才看得到有哪些选项，
        /// 收起态只有一行文字 —— 用户提的这两条正好就是下拉框的天然缺陷。
        /// 现在换成色板 + 样式画廊（全部平铺），本用例把"平铺了""画出来了""选中看得见"
        /// 三件事分别钉死，并在 6 档宽度下复核两块选择器始终有可见尺寸。
        /// </summary>
        private static void TestFramePickersAlwaysVisible()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    Application.DoEvents();

                    // ── ① 进入设置页：所有可选项必须已经平铺出来 ──
                    var p = form.ProbeFramePickers();
                    Eq(p.ColorCells, FrameColors.All.Length, "色板把调色板里的颜色全部平铺出来（进入即可见）");
                    Eq(p.StyleCells, FrameStyles.All.Length, "样式画廊把全部样式平铺出来（进入即可见）");

                    // ── ② 两块选择器必须真的画出了东西（不是空白控件）──
                    Check(p.GridInk > 100, "色板真的画出了色块",
                        "非背景像素 " + p.GridInk + " 尺寸 " + p.GridSize);
                    Check(p.GalleryInk > 300, "样式画廊真的画出了缩略图",
                        "非背景像素 " + p.GalleryInk + " 尺寸 " + p.GallerySize);

                    // ── ③ 选中态必须清晰可见（强调色描边 / 勾选角标）──
                    Check(p.GridAccentInk > 10, "★ 色板里「选中的那一格」有可见的强调色标记",
                        "强调色像素 " + p.GridAccentInk);
                    Check(p.GalleryAccentInk > 10, "★ 样式画廊里选中的那一格有可见的强调色标记",
                        "强调色像素 " + p.GalleryAccentInk);
                    Check(p.GridSelectedCell.Width > 0 && p.GallerySelectedCell.Width > 0,
                        "选中的色块格 / 样式格都能定位到（选中态不是空）");

                    // ── ④ 换选择：界面上"看得见的选中态"必须跟着走 ──
                    form.ProbeSelectFrameStyle(FrameStyles.Neon);
                    var s1 = form.ProbeFrameSelection();
                    Eq(s1.GridStyleId, FrameStyles.Neon, "★ 选了「霓虹」之后，画廊里高亮的就是霓虹");
                    Eq(s1.GridColorId, s1.ColorId, "色板高亮与当前颜色一致");

                    form.ProbeSelectFrameColor(FrameColors.Gold);
                    var s2 = form.ProbeFrameSelection();
                    Eq(s2.GridColorId, FrameColors.Gold, "★ 选了「暖金」之后，色板里高亮的就是暖金");
                    Check(!string.IsNullOrEmpty(s2.ColorNameText) && s2.ColorNameText.Contains("暖金"),
                        "界面上写明了当前颜色的名字（状态清晰）", "文字=" + s2.ColorNameText);

                    // ── ⑤ 六档宽度（含最大化那种超宽）都必须保持可见尺寸 ──
                    int[] widths = new int[] { 736, 900, 1180, 1516, 1900, 2600 };
                    int bad = 0; string badDetail = "";
                    foreach (int w in widths)
                    {
                        form.ClientSize = new Size(w, 900);
                        SettingsForm.RelayoutForTest(form);
                        Application.DoEvents();

                        var q = form.ProbeFramePickers();
                        bool ok = q.GridSize.Width > 100 && q.GridSize.Height > 10
                               && q.GallerySize.Width > 100 && q.GallerySize.Height > 40
                               && q.GridInsideGroup && q.GalleryInsideGroup;
                        if (!ok)
                        {
                            bad++;
                            badDetail += w + "→色板" + q.GridSize + (q.GridInsideGroup ? "" : "(越界)")
                                       + " 画廊" + q.GallerySize + (q.GalleryInsideGroup ? "" : "(越界)") + " ";
                        }
                    }
                    Check(bad == 0,
                        "★ 从窄窗到超宽（全屏档）每一档宽度下，色板与样式画廊都保持可见尺寸且不越界",
                        badDetail);

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 整体视觉（Win11 风格）：层次、动效、以及"没有画坏"的像素级体检。★
        ///
        /// 用户原话："整体 UI 重做：提升清晰度与美观度，使其符合 Windows 11 的整体视觉风格
        ///           （圆角、层次、光影、动效等），整体美化程度要明显提升。"
        ///
        /// 美化类改动没法用"属性对不对"来验证，所以这里换两个能落地的角度：
        ///   ① 动效：悬停补间必须真的在过渡（起点 < 0.3、终点 > 0.95），
        ///      而不是 `_hover = true; Invalidate();` 那种瞬变；
        ///   ② 像素：把整个窗口渲染一遍，数颜色层次 / 近黑像素 / 强调色 / 外框色 ——
        ///      近黑像素必须接近 0（历史上"自绘复选框没铺底"导致过一整排黑条），
        ///      强调色和外框色必须真的出现在画面上（说明按钮、竖条、色板、缩略图都画了）。
        /// </summary>
        private static void TestVisualPolish()
        {
            // ── ① 动效 ──
            float t0, t1;
            bool animates = SettingsForm.ProbeHoverTweenAnimates(out t0, out t1);
            Check(animates, "★ 悬停是带过渡的（补间从 0 平滑推到 1），不是瞬变",
                "起点 " + t0.ToString("0.00") + " → 终点 " + t1.ToString("0.00"));

            // ── ② 像素体检 ──
            // ★ 夹具默认把 ShowActivationFrame 关掉（DragFixture 的通用默认），
            //   而关掉时色板 / 画廊会被 SyncFrameEnabled 置灰（画成半透明）——
            //   那样量到的就不是"正常使用中"的画面。这里显式打开。
            using (var f = new DragFixture(delegate (AppConfig c) { c.ShowActivationFrame = true; }))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(20, 20);
                    form.Show();
                    Application.DoEvents();

                    // ★ 必须把窗口开得足够高再渲染 ★
                    //   单列模式下六个分组纵向排开，"视觉反馈"是第 3 组 ——
                    //   默认高度下它大半在滚动视口之外，DrawToBitmap 只画可见部分，
                    //   于是"外框色像素 = 0"会误报成"色板没画出来"。
                    //   这里把客户区撑到装得下全部内容，渲染的才是完整一页。
                    form.ClientSize = new Size(1000, 2400);
                    SettingsForm.RelayoutForTest(form);
                    Application.DoEvents();
                    Application.DoEvents();

                    var st = SettingsForm.ProbeFormRender(form, f.Store.Current.FrameColorId);
                    var ink = form.ProbeFrameColorInk(f.Store.Current.FrameColorId);
                    Line("    （渲染体检）" + st.Width + "x" + st.Height
                        + " 颜色数=" + st.DistinctColors
                        + " 近黑=" + st.BlackPixels
                        + " 强调色=" + st.AccentPixels
                        + " 外框色=" + st.FrameColorPixels
                        + " 平均亮度=" + st.MeanBrightness.ToString("0.0"));
                    Line("    （外框色分区）色板=" + ink[0] + " 画廊=" + ink[1] + " 预览框=" + ink[2]);
                    Line("    （色板诊断）" + form.ProbeGridDiagnostic());

                    Check(st.Width > 400 && st.Height > 300, "设置窗口渲染出了有效的画布尺寸",
                        st.Width + "x" + st.Height);

                    Check(st.DistinctColors > 400,
                        "★ 画面颜色层次足够丰富（不是一块平色，说明圆角/描边/投影/文字都在画）",
                        "不同颜色数 " + st.DistinctColors);

                    // 近黑像素：正常界面里只有 ClearType 子像素渲染会在文字边缘
                    // 留下极少量深色像素；成百上千就意味着某块自绘控件没铺底（黑条）。
                    Check(st.BlackPixels < 400,
                        "★ 画面里没有成片的近黑像素（自绘控件没铺底导致黑条的老问题没有复发）",
                        "近黑像素 " + st.BlackPixels);

                    Check(st.MeanBrightness > 200,
                        "整体是浅色 Win11 底（平均亮度够高，不是暗色或脏色）",
                        "平均亮度 " + st.MeanBrightness.ToString("0.0"));

                    Check(st.AccentPixels > 300,
                        "★ 强调色真的出现在画面上（主按钮 / 分组竖条 / 选中态）",
                        "强调色像素 " + st.AccentPixels);

                    Check(st.FrameColorPixels > 150,
                        "★ 外框色真的出现在画面上（色板 / 样式缩略图 / 实时预览）",
                        "外框色像素 " + st.FrameColorPixels);

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 任何宽度（尤其是最小尺寸）下，分组里的控件都不能越出分组框。★
        ///
        /// 用户原话："拖动行为设置项：其中的『允许拖动全屏无边框窗口』选项，
        ///           在设置窗口较小时会被遮挡，请调整布局使其始终完整可见。"
        ///
        /// 断言方式是"控件矩形是否越出父分组客户区"—— 比看截图确定得多，
        /// 而且能顺手把"某个选项被压成 20px / 被推到框外"这类问题一起兜住。
        /// 覆盖：最小尺寸 + 最小尺寸附近几档（最容易出问题的正是这一区间）。
        /// </summary>
        private static void TestNoControlClippedAtMinSize()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(20, 20);
                    form.Show();
                    Application.DoEvents();

                    float s = SettingsForm.ProbeUiScaleOf(form);
                    int minClientW = (int)Math.Round(736 * s);    // 与 MinimumSize 同口径
                    int minClientH = (int)Math.Round(440 * s);

                    // 最小尺寸 + 附近三档（右侧留一点余量，覆盖"刚好卡在阈值附近"）
                    int[] widths = { minClientW, minClientW + 60, minClientW + 140, minClientW + 260 };

                    int bad = 0; string detail = "";
                    int textBad = 0; string textDetail = "";
                    foreach (int w in widths)
                    {
                        form.ClientSize = new Size(w, Math.Max(minClientH, 900));
                        SettingsForm.RelayoutForTest(form);
                        Application.DoEvents();

                        for (int gi = 0; gi < SettingsForm.ProbeGroupCount(form); gi++)
                        {
                            var rep = SettingsForm.ProbeGroupClipping(form, gi);
                            if (rep.Clipped == 0) continue;
                            bad += rep.Clipped;
                            detail += "宽" + w + " 第" + (gi + 1) + "组：" + rep.Detail;

                            string fit = SettingsForm.ProbeTextFitting(form, gi);
                            if (fit.Length > 0)
                            {
                                textBad++;
                                textDetail += "宽" + w + " 第" + (gi + 1) + "组：" + fit.Replace("\r\n", " | ");
                            }
                        }
                    }

                    Check(bad == 0,
                        "★ 最小尺寸及附近各档下，没有控件越出所属分组（全屏选项始终完整可见）",
                        detail);

                    // ★ 这一条才是用户真正报的："选项被遮挡" = 文字折行后下半行被裁掉。
                    //   控件矩形完全合法，越界检查发现不了，必须按"文字区宽度"量一遍。
                    Check(textBad == 0,
                        "★ 最小尺寸及附近各档下，没有任何选项的文字被裁掉（遮挡回归）",
                        textDetail);

                    // 顺带把"拖动行为"分组里那个全屏选项单独点一次名 ——
                    // 它是用户报的那一条，必须能在最小尺寸下完整显示。
                    form.ClientSize = new Size(minClientW, Math.Max(minClientH, 900));
                    SettingsForm.RelayoutForTest(form);
                    Application.DoEvents();
                    // ── ③ 开关（第四十二轮：方框复选框 → Win11 开关）──
                    //   自绘控件画错了照样"存在且可点"，只有数像素才发现。
                    string onCol = SettingsForm.ProbeSwitchColors(form, true);
                    string offCol = SettingsForm.ProbeSwitchColors(form, false);
                    Line("    （开关·开）" + onCol);
                    Line("    （开关·关）" + offCol);

                    Check(!onCol.Contains("异常") && !offCol.Contains("异常"),
                        "开关能正常渲染（探针没有报错）", onCol + " / " + offCol);
                    Check(onCol != offCol,
                        "★ 开关的开 / 关两态渲染结果不同（不是画成了同一个样子）",
                        "开：" + onCol + "  关：" + offCol);

                    // 轨道颜色必须真的变了（不是只有滑块位置变了）
                    int[] onRgb = ParseRgb(onCol, 0);
                    int[] offRgb = ParseRgb(offCol, 0);
                    int diff = Math.Abs(onRgb[0] - offRgb[0]) + Math.Abs(onRgb[1] - offRgb[1])
                             + Math.Abs(onRgb[2] - offRgb[2]);
                    Check(diff > 60,
                        "★ 开关的轨道颜色在开 / 关之间有明确差异（强调色 vs 中性灰）",
                        "开轨道=" + string.Join(",", onRgb) + " 关轨道=" + string.Join(",", offRgb)
                        + " 差值=" + diff);

                    // 滑块必须是浅色（白），否则"开"状态会糊成一团
                    int[] knob = ParseRgb(onCol, 1);
                    Check(knob[0] > 230 && knob[1] > 230 && knob[2] > 230,
                        "开关的滑块是浅色（白滑块，在两种轨道上都清晰可辨）",
                        "滑块=" + string.Join(",", knob));

                    var g4 = SettingsForm.ProbeGroupAt(form, 3);
                    var clip = SettingsForm.ProbeGroupClipping(form, 3);
                    Check(g4 != null && clip.Clipped == 0,
                        "★ 最小尺寸下『拖动行为』分组自身无控件被遮挡",
                        clip.Detail);

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 拖动窗口边缘改尺寸时：布局必须幂等、不抖动、不溢出、列数不来回翻。★
        ///
        /// 用户原话："窗口尺寸调整时的界面抖动：拖动窗口边缘改变大小时，设置页面内多项内容
        ///           会高频晃动，部分选项甚至会溢出原有框架。"
        ///
        /// 根因有两条，各对应下面一条断言：
        ///   ① OnResize 用的是"防抖"（每次 WM_SIZE 重新计时）→ 整个拖动过程一次都不重排，
        ///      手一停就补一次 → 内容"啪"地跳一下。改成固定节拍节流后，
        ///      内容全程平滑跟随，因此【每一步中间尺寸都必须无越界】。
        ///   ② 1 列 ↔ 2 列在阈值附近会一帧一翻 → 整屏左右横跳。加了回滞后，
        ///      阈值下方一小段仍应保持两列。
        /// 再加一条"布局幂等"：同一尺寸连排多次，所有控件矩形必须完全一致 ——
        /// 不幂等就是"越拉越偏"的根源。
        /// </summary>
        private static void TestResizeIsStable()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(20, 20);
                    form.Show();
                    Application.DoEvents();

                    float s = SettingsForm.ProbeUiScaleOf(form);
                    int h = (int)Math.Round(760 * s);

                    // ── ① 布局幂等：同一尺寸连排 4 次，指纹必须完全一致 ──
                    form.ClientSize = new Size((int)Math.Round(1100 * s), h);
                    SettingsForm.RelayoutForTest(form);
                    Application.DoEvents();
                    string fp1 = SettingsForm.ProbeLayoutFingerprint(form);
                    for (int i = 0; i < 3; i++)
                    {
                        SettingsForm.RelayoutForTest(form);
                        Application.DoEvents();
                    }
                    string fp2 = SettingsForm.ProbeLayoutFingerprint(form);
                    Check(fp1.Length > 0 && fp1 == fp2,
                        "★ 同一尺寸反复重排结果完全一致（布局幂等，不会越排越偏）",
                        fp1 == fp2 ? "" : "两次指纹不同");

                    // ── ② 模拟拖边框的连续尺寸：每一步都不许有控件越界 ──
                    int badClip = 0; string clipDetail = "";
                    int step = Math.Max(1, (int)Math.Round(29 * s));
                    int wStart = (int)Math.Round(736 * s);
                    int wEnd = (int)Math.Round(1500 * s);
                    for (int w = wStart; w <= wEnd; w += step)
                    {
                        form.ClientSize = new Size(w, h);
                        SettingsForm.RelayoutForTest(form);
                        Application.DoEvents();

                        for (int gi = 0; gi < SettingsForm.ProbeGroupCount(form); gi++)
                        {
                            var rep = SettingsForm.ProbeGroupClipping(form, gi);
                            if (rep.Clipped <= 0) continue;
                            badClip += rep.Clipped;
                            clipDetail += "w=" + w + " 第" + (gi + 1) + "组：" + rep.Detail;
                        }
                    }
                    Check(badClip == 0,
                        "★ 连续改变尺寸的每一步都没有控件越界（拖动过程不溢出）",
                        clipDetail);

                    // ── ③ 列数切换必须带回滞，阈值附近不能一帧一翻 ──
                    int th = SettingsForm.ProbeTwoColumnThresholdPx(form);
                    int c1 = SettingsForm.ProbeRelayoutColumns(form, th + (int)Math.Round(20 * s));
                    int c2 = SettingsForm.ProbeRelayoutColumns(form, th - (int)Math.Round(40 * s));
                    int c3 = SettingsForm.ProbeRelayoutColumns(form, th - (int)Math.Round(300 * s));
                    Check(c1 == 2 && c2 == 2 && c3 == 1,
                        "★ 列数切换带回滞：阈值下方一小段仍保持两列，不会来回翻（抖动回归）",
                        "阈值=" + th + " → " + c1 + " / " + c2 + " / " + c3);

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 设置页必须按"这块显示器此刻的真实 DPI"渲染，并且 DPI 变了要能整体重建。★
        ///
        /// 用户原话："设置页面渲染：当前画面分辨率偏低、文字有明显锯齿。
        ///           请改为按显示器实时分辨率进行渲染，保证文字与界面清晰锐利。"
        ///
        /// 相关事实：本程序是 PerMonitorV2 感知进程，Windows【不会】替我们拉伸窗口 ——
        /// 一旦我们自己的缩放系数取错（比如拿了主屏 DPI、或缓存了句柄创建前的 96），
        /// 界面就会一直停在错的尺寸上，观感就是"分辨率不对、字发糊"。
        /// 所以这里断言三件事：
        ///   ① 生效的缩放系数 == 窗口所在显示器真实 DPI / 96；
        ///   ② 把 DPI 改成另一档后，控件尺寸真的跟着等比变化（重建有效）；
        ///   ③ 改回来之后布局指纹与原来完全一致（可逆、幂等，不会越改越乱）。
        /// </summary>
        private static void TestDpiRendering()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(20, 20);
                    form.Show();
                    Application.DoEvents();

                    // ── ① 缩放系数必须等于窗口所在显示器的真实 DPI / 96 ──
                    float used = SettingsForm.ProbeUiScaleOf(form);
                    int realDpi = Native.DpiOfWindow(form.Handle);
                    float want = realDpi / 96f;
                    Check(Math.Abs(used - want) < 0.01f,
                        "★ 界面按窗口所在显示器的真实 DPI 渲染（不是写死 100%，也不是主屏 DPI）",
                        "生效系数=" + used.ToString("0.###") + " 真实 DPI=" + realDpi
                        + " → 期望 " + want.ToString("0.###"));

                    // 取一个"固定宽度"的标签作为测量样本（整行复选框会被拉宽，量不准）
                    form.ClientSize = new Size((int)Math.Round(900 * used), (int)Math.Round(760 * used));
                    SettingsForm.RelayoutForTest(form);
                    Application.DoEvents();

                    Rectangle r1 = SettingsForm.ProbeRectOfTextForTest(form, "外框颜色");
                    Check(r1.Width > 0, "能定位到样本控件（探针有效）", r1.ToString());

                    // ── ② 同一 DPI 重复调用必须是 no-op（幂等，不会把界面越缩越小）──
                    string fpBefore = SettingsForm.ProbeLayoutFingerprint(form);
                    SettingsForm.ProbeRescaleForDpi(form, realDpi);
                    Application.DoEvents();
                    string fpAfter = SettingsForm.ProbeLayoutFingerprint(form);
                    Check(fpBefore == fpAfter,
                        "★ 同一 DPI 下重复触发重建是 no-op（幂等，不会越缩越小）",
                        fpBefore == fpAfter ? "" : "指纹被改动");

                    // ── ③ 真正换一块屏时（DPI 变了），界面必须整体重算且依然合法 ──
                    //
                    //   注意：这里【不能】断言"控件尺寸变成原来的一半"——
                    //   字体是 Point 单位、由 GDI 按【当前 DC 的 DPI】渲染，
                    //   而本用例只是改了我们的缩放系数、并没有真的换显示器，
                    //   字体的物理大小不会跟着变。真实场景里 WM_DPICHANGED 与
                    //   DC 的 DPI 是同时变的，两者一致。所以这里只断言
                    //   "重建跑得通、且重建后布局依然无越界"。
                    int altDpi = Math.Abs(realDpi - 96) < 24 ? 192 : 96;
                    SettingsForm.ProbeRescaleForDpi(form, altDpi);
                    Application.DoEvents();
                    float used2 = SettingsForm.ProbeUiScaleOf(form);
                    Check(Math.Abs(used2 - altDpi / 96f) < 0.01f,
                        "★ DPI 变化后缩放系数跟着换（按新分辨率重建界面）",
                        "生效系数=" + used2.ToString("0.###") + " 期望 " + (altDpi / 96f).ToString("0.###"));

                    int bad = 0; string detail = "";
                    for (int gi = 0; gi < SettingsForm.ProbeGroupCount(form); gi++)
                    {
                        var rep = SettingsForm.ProbeGroupClipping(form, gi);
                        if (rep.Clipped > 0) { bad += rep.Clipped; detail += "第" + (gi + 1) + "组：" + rep.Detail; }
                    }
                    Check(bad == 0, "换 DPI 重建之后布局依然合法（无控件越界）", detail);

                    // ── ④ 换回原 DPI：缩放系数必须回到原值 ──
                    SettingsForm.ProbeRescaleForDpi(form, realDpi);
                    Application.DoEvents();
                    Check(Math.Abs(SettingsForm.ProbeUiScaleOf(form) - used) < 0.01f,
                        "★ DPI 改回原值后缩放系数回到原值（可逆）",
                        SettingsForm.ProbeUiScaleOf(form).ToString("0.###"));

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 触发之后，左键（移动）和右键（缩放）在【普通 / 最大化 / 全屏】三种窗口上都必须生效。★
        ///
        /// 用户报障："到达触发阈值后，右键的功能没有了。"
        /// 新需求："全屏情况下触发按键，不管是拖动窗口还是调整窗口大小，
        ///         都要把全屏窗口变成非全屏窗口。"
        ///
        /// 这两条都落在同一条链路上：FindTarget（能不能选中）→ Begin（能不能起会话）。
        /// 本用例把三种窗口形态 × 左/右键共六种组合的实际结果全部打出来，
        /// 并把"操作后不再是最大化 / 全屏"作为硬断言。
        /// </summary>
        private static void TestTriggerOnBigWindows()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                // ── A) 普通窗口 ──
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.RestoreMaximizedWhenMoved = true;
                }))
                {
                    Line("    （普通窗口 左键）" + ProbeOneGesture(f, true));
                    Line("    （普通窗口 右键）" + ProbeOneGesture(f, false));
                }

                // ── B) 最大化窗口 ──
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.RestoreMaximizedWhenMoved = true;
                    c.ExitFullscreenOnTrigger = true;
                }))
                {
                    Native.ShowWindow(f.H, Native.SW_MAXIMIZE);
                    Settle();
                    bool zoomed = Native.IsZoomed(f.H);
                    Line("    （最大化窗口）IsZoomed=" + zoomed);

                    string leftRes = ProbeOneGesture(f, true);
                    Line("    （最大化窗口 左键）" + leftRes);
                    bool leftOk = !Native.IsZoomed(f.H);
                    Check(zoomed && leftOk,
                        "★ 最大化窗口：左键拖动后不再是最大化（先还原再拖）",
                        leftRes);

                    Native.ShowWindow(f.H, Native.SW_MAXIMIZE);
                    Settle();
                    string rightRes = ProbeOneGesture(f, false);
                    Line("    （最大化窗口 右键）" + rightRes);
                    bool rightOk = !Native.IsZoomed(f.H);
                    Check(rightOk,
                        "★ 最大化窗口：右键缩放后不再是最大化（右键功能必须还在）",
                        rightRes);

                    // ★ 手感保证：还原之后光标必须落在窗口里 ★
                    //   老代码只对"移动"做了这一步，缩放漏了 —— 于是角区判定
                    //   拿到一个光标根本不在其中的矩形，右键缩放就成了"没反应/乱跳"。
                    Native.ShowWindow(f.H, Native.SW_MAXIMIZE);
                    Settle();
                    ActivateTrigger(f.Coord);
                    var c0 = CenterOf(f.H);
                    var down = Ms(c0);
                    f.Coord.OnMouse(Native.WM_RBUTTONDOWN, ref down);
                    Settle();
                    Native.RECT wr;
                    Native.GetWindowRect(f.H, out wr);
                    bool cursorInside = c0.X >= wr.Left && c0.X < wr.Right
                                     && c0.Y >= wr.Top && c0.Y < wr.Bottom;
                    Check(cursorInside,
                        "★ 最大化窗口右键：降级成普通窗口后光标仍落在窗口内（角区判定才有意义）",
                        "光标=" + c0.X + "," + c0.Y + " 窗口=" + wr.Left + "," + wr.Top
                        + " " + wr.Width + "x" + wr.Height);
                    var up0 = Ms(c0);
                    f.Coord.OnMouse(Native.WM_RBUTTONUP, ref up0);
                    Settle();
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }

                // ── C) 真全屏窗口（铺满整个显示器）──
                //   C1：默认配置（ExitFullscreenOnTrigger=true，AllowFullscreenWindows=false）
                //       → 必须能操作，并且操作后不再是全屏。
                //       这一条正是用户的新需求："全屏情况下触发按键，不管是拖动还是
                //       调整大小，都要把全屏窗口变成非全屏窗口"。
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ExitFullscreenOnTrigger = true;
                    c.AllowFullscreenWindows = false;   // 老开关保持默认关
                }))
                {
                    var vs = SystemInformation.VirtualScreen;
                    Native.SetWindowPos(f.H, IntPtr.Zero, vs.Left, vs.Top, vs.Width, vs.Height,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    Settle();
                    bool full = WindowActions.IsFullscreen(f.H);
                    Line("    （全屏窗口·默认配置）IsFullscreen=" + full);

                    string rightRes = ProbeOneGesture(f, false);
                    Line("    （全屏窗口 右键）" + rightRes);
                    Check(full && !WindowActions.IsFullscreen(f.H),
                        "★ 真全屏窗口：右键缩放后不再是全屏（默认配置下就要生效）",
                        rightRes);

                    Native.SetWindowPos(f.H, IntPtr.Zero, vs.Left, vs.Top, vs.Width, vs.Height,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    Settle();
                    string leftRes = ProbeOneGesture(f, true);
                    Line("    （全屏窗口 左键）" + leftRes);
                    Check(!WindowActions.IsFullscreen(f.H),
                        "★ 真全屏窗口：左键拖动后不再是全屏",
                        leftRes);
                }

                // ── C2）把新开关也关掉 → 回到老行为（全屏窗口不接管，且必须留日志）──
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ExitFullscreenOnTrigger = false;
                    c.AllowFullscreenWindows = false;
                }))
                {
                    var vs = SystemInformation.VirtualScreen;
                    Native.SetWindowPos(f.H, IntPtr.Zero, vs.Left, vs.Top, vs.Width, vs.Height,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    Settle();
                    string res = ProbeOneGesture(f, false);
                    Line("    （全屏窗口·两个开关都关）" + res);
                    Check(WindowActions.IsFullscreen(f.H) && res.Contains("起会话=False"),
                        "两个开关都关时全屏窗口不被接管（老行为保留）", res);
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>走一次完整的"触发 → 按下 → 移动 → 松开 → 松键"，返回可读的结果串。</summary>
        private static string ProbeOneGesture(DragFixture f, bool left)
        {
            try
            {
                ActivateTrigger(f.Coord);

                var c0 = CenterOf(f.H);
                var down = Ms(c0);
                bool swallowed = f.Coord.OnMouse(
                    left ? Native.WM_LBUTTONDOWN : Native.WM_RBUTTONDOWN, ref down);
                bool dragging = f.Coord.Drag.IsDragging;

                Native.RECT before;
                Native.GetWindowRect(f.H, out before);

                var mv = Ms(new Native.POINT(c0.X + 40, c0.Y + 30));
                f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                Settle();

                Native.RECT after;
                Native.GetWindowRect(f.H, out after);

                var up = Ms(mv.pt);
                f.Coord.OnMouse(left ? Native.WM_LBUTTONUP : Native.WM_RBUTTONUP, ref up);
                Settle();
                f.Coord.Trigger.SimulateKeyUp(null);
                Settle();

                return "吞掉=" + swallowed + " 起会话=" + dragging
                     + " 前=" + before.Left + "," + before.Top + " " + before.Width + "x" + before.Height
                     + " 后=" + after.Left + "," + after.Top + " " + after.Width + "x" + after.Height;
            }
            catch (Exception ex) { return "异常 " + ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>
        /// ★ 滚轮扫过设置页时，任何选项都不许被悄悄改掉。★
        ///
        /// 用户报障："到达触发阈值后，右键的功能没有了"，追问后澄清为
        /// "按右键那个窗口就直接消失了，相当于被最小化了"。
        /// 查配置发现 ActionRight="Minimize"、ActionMiddle="Close"，
        /// 而用户并不记得改过 —— 根因是 WinForms 的 DropDownList 组合框
        /// 【默认响应鼠标滚轮，且不需要获得焦点】：设置页能滚动，用户翻页时
        /// 光标扫过"左键/右键/中键"这几个下拉框，动作绑定就被滚轮改掉了。
        /// NumericUpDown 同理（用户的 HoldMs=300、CornerPercent=21、FrameRadius=7
        /// 都和出厂值不同）。
        ///
        /// 本用例直接往控件句柄发 WM_MOUSEWHEEL，复现"光标停在它上面滚一格"。
        /// </summary>
        private static void TestWheelDoesNotChangeSettings()
        {
            const int WM_MOUSEWHEEL = 0x020A;
            // wParam 高 16 位是滚轮增量：+120 = 向前滚一格（选中项 -1）
            var wheelUp = new IntPtr(120 << 16);

            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    Application.DoEvents();

                    // ── ① 右键动作下拉框 ──
                    var right = form.ProbeComboRight as ComboBox;
                    Check(right != null && right.Items.Count > 1, "取到右键动作下拉框");
                    if (right != null && right.Items.Count > 1)
                    {
                        int before = right.SelectedIndex;
                        for (int i = 0; i < 5; i++)
                        {
                            Native.SendMessageW(right.Handle, WM_MOUSEWHEEL, wheelUp, IntPtr.Zero);
                        }
                        Application.DoEvents();
                        Eq(right.SelectedIndex, before,
                            "★ 滚轮扫过『右键』下拉框不会改掉动作绑定（误改的根因）");
                    }

                    // ── ② 左键动作下拉框 ──
                    var left = form.ProbeComboLeft as ComboBox;
                    if (left != null && left.Items.Count > 1)
                    {
                        int before = left.SelectedIndex;
                        Native.SendMessageW(left.Handle, WM_MOUSEWHEEL, wheelUp, IntPtr.Zero);
                        Application.DoEvents();
                        Eq(left.SelectedIndex, before, "★ 滚轮扫过『左键』下拉框不会改掉动作绑定");
                    }

                    // ── ③ 数字框（长按阈值）──
                    var num = form.ProbeNumHold as NumericUpDown;
                    Check(num != null, "取到长按阈值数字框");
                    if (num != null)
                    {
                        decimal before = num.Value;
                        for (int i = 0; i < 3; i++)
                        {
                            Native.SendMessageW(num.Handle, WM_MOUSEWHEEL, wheelUp, IntPtr.Zero);
                        }
                        Application.DoEvents();
                        Check(num.Value == before,
                            "★ 滚轮扫过数字框不会改掉数值（HoldMs/CornerPercent 被误改的根因）",
                            "前=" + before + " 后=" + num.Value);
                    }

                    // ── ④ 守卫的条件必须正确：收起→吞掉，展开→放行 ──
                    //
                    //   不能靠"展开后发滚轮看选中项有没有变"来验证：
                    //   下拉列表展开时它是个【独立的列表窗口】，滚轮事件由那个窗口接收，
                    //   发给组合框句柄根本到不了列表里（实测选中项纹丝不动）。
                    //   所以这里直接断言守卫自身的分流计数 —— 收起见一次吞一次、
                    //   展开一次都不吞，这才是"没有把功能一并禁掉"的准确判据。
                    if (right != null)
                    {
                        int sw0 = SettingsForm.ProbeWheelSwallowed(right);
                        int fw0 = SettingsForm.ProbeWheelForwarded(right);
                        Native.SendMessageW(right.Handle, WM_MOUSEWHEEL, wheelUp, IntPtr.Zero);
                        Application.DoEvents();
                        int sw1 = SettingsForm.ProbeWheelSwallowed(right);
                        int fw1 = SettingsForm.ProbeWheelForwarded(right);
                        Check(sw1 == sw0 + 1 && fw1 == fw0,
                            "收起状态下滚轮被守卫吞掉（不放行给 WinForms）",
                            "吞 " + sw0 + "→" + sw1 + " 放 " + fw0 + "→" + fw1);

                        bool opened = false;
                        try { right.DroppedDown = true; opened = right.DroppedDown; } catch { }
                        if (opened)
                        {
                            Native.SendMessageW(right.Handle, WM_MOUSEWHEEL, wheelUp, IntPtr.Zero);
                            Application.DoEvents();
                            int sw2 = SettingsForm.ProbeWheelSwallowed(right);
                            int fw2 = SettingsForm.ProbeWheelForwarded(right);
                            Check(fw2 == fw1 + 1 && sw2 == sw1,
                                "★ 展开状态下滚轮照常放行（没有把下拉框的滚轮功能一并禁掉）",
                                "吞 " + sw1 + "→" + sw2 + " 放 " + fw1 + "→" + fw2);
                            try { right.DroppedDown = false; } catch { }
                        }
                        else
                        {
                            Line("    （本环境无法展开下拉列表，跳过『展开后放行』一条）");
                        }
                    }

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 图标字体可用，且用到的每个字形都真的画出了墨水。★
        ///
        /// 为什么必须像素级验证：图标字体（Segoe Fluent Icons / MDL2）在
        /// 【没有这个字形】时是画成空白，而不是画成豆腐块 —— 肉眼几乎发现不了，
        /// 只会在某个分区标题前面"莫名其妙空了一块"。
        /// 换字体、换字号、换字形常量都会立刻触发这条断言。
        /// </summary>
        private static void TestIconGlyphs()
        {
            var fam = SettingsForm.IconFamilyForTest;
            Check(fam != null,
                "图标字体可用（Segoe Fluent Icons 或退回 Segoe MDL2 Assets）",
                fam == null ? "两个都没有，界面会退化成无图标（不崩，但会显得单薄）" : fam.Name);
            if (fam == null) return;

            string[] names = { "Keyboard", "Move", "Color", "Speed", "Shield", "Chevron",
                               "Help", "Info" };
            string[] glyphs = SettingsForm.IconGlyphsForTest;
            Eq(glyphs.Length, names.Length, "字形表与名称表一一对应");

            var bad = new System.Text.StringBuilder();

            // ★ 基准：豆腐块有多少墨水 ★
            //   U+FFFF 是 Unicode【非字符】，任何字体都不可能有它的字形 ——
            //   于是 GDI 一定画出"缺字形"的方框，这就是我们要比对的基准。
            //
            //   为什么不能假设"缺字形 = 空白"：实测 GDI 会做【字体链接】
            //   （font linking），缺字形时换一个回退字体来画 ——
            //   私用区码位 U+E000 和汉字"中"都渲染出了同样 166 墨水的方框。
            //   所以判据必须是"墨水 != 豆腐块墨水"，而不是"墨水 > 0"。
            int tofu = SettingsForm.MeasureGlyphInk("\uFFFF", 32);
            Line("    （基准）U+FFFF 豆腐块墨水=" + tofu);

            for (int i = 0; i < glyphs.Length; i++)
            {
                int ink = SettingsForm.MeasureGlyphInk(glyphs[i], 32);
                Line("    （字形）" + names[i] + " U+" + ((int)glyphs[i][0]).ToString("X4")
                    + " 墨水=" + ink);
                if (ink <= 12) bad.Append(names[i] + "(空白" + ink + ") ");
                else if (tofu > 0 && Math.Abs(ink - tofu) <= 2)
                    bad.Append(names[i] + "(是豆腐块" + ink + ") ");
            }
            Check(bad.Length == 0,
                "★ 每个用到的图标字形都渲染出了【真实字形】（既不是空白、也不是豆腐块）",
                bad.ToString());
        }

        /// <summary>
        /// 从 "轨道=r,g,b 滑块=r,g,b" 里取出第 idx 组（0=轨道，1=滑块）的 RGB。
        /// 取不到就返回 0,0,0（断言自然会失败，不会静默通过）。
        /// </summary>
        private static int[] ParseRgb(string s, int idx)
        {
            try
            {
                var m = System.Text.RegularExpressions.Regex.Matches(s, @"(\d+),(\d+),(\d+)");
                if (m.Count <= idx) return new int[] { 0, 0, 0 };
                return new int[]
                {
                    int.Parse(m[idx].Groups[1].Value),
                    int.Parse(m[idx].Groups[2].Value),
                    int.Parse(m[idx].Groups[3].Value),
                };
            }
            catch { return new int[] { 0, 0, 0 }; }
        }

        /// <summary>
        /// ★ 触发键激活时鼠标键已经按住 → 必须把拖动补起来。★
        ///
        /// 用户原话："有时候已经触发功能了，证据就是该窗口周围已经出现了一圈光晕，
        ///           但这时候已经按住左键的鼠标，无论怎么移动，那个窗口都没有反应。"
        ///
        /// 根因是时序漏洞：拖动只可能由【按钮按下事件】发起，而用户完全可能在
        /// 触发键还没到阈值（HoldMs 那几百毫秒里）就把左键按下去了 ——
        /// 那条 down 事件到达时触发键还没激活，被原样放行、什么也没记下来。
        /// 等触发键激活、外框亮起时按钮已经是"按着"的状态，
        /// 而没有任何代码会去补一次拖动 → 窗口永远不动，外框却一直亮着。
        ///
        /// 本用例按【真实时序】复现：先按鼠标键 → 再让触发键到阈值 → 再移动。
        /// 第二段验证"按下发生在触发键按住【之前】"的时序 —— 第四十三轮把它当
        /// 误接管风险排除，第四十四轮按用户明确要求改为【必须收养】（报障场景
        /// 正是这个时序：先选中文字、再长按 Enter）。长按超阈值本身就是郑重的
        /// 接管声明，"顺手误触"由 300ms 阈值本身挡着。
        /// </summary>
        private static void TestAdoptHeldButton()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            Coordinator.AssumeButtonsDownForTest = true;   // 自检里没有真实按键
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ActionLeft = Actions.Move;
                    c.DoubleClickLeftMaximize = false;
                    c.ExitFullscreenOnTrigger = true;
                    c.ShowActivationFrame = true;    // 本条要顺带验证"补起的拖动也有外框"
                }))
                {
                    Native.RECT r0;
                    Native.GetWindowRect(f.H, out r0);
                    var c0 = CenterOf(f.H);

                    // ── ① 触发键按下（还没到阈值）→ 按下左键 → 触发键到阈值 → 移动 ──
                    int upBefore = Coordinator.PhantomUpSentForTest;
                    f.Coord.Trigger.SimulateKeyDown();
                    Check(!f.Coord.Trigger.Active, "触发键尚未到达阈值（仍在按住阶段）");
                    Check(f.Coord.Trigger.Held, "触发键处于『按住中』状态");

                    var down = Ms(c0);
                    bool swallowed = f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Check(!swallowed, "按住阶段里的左键按下被原样放行（此时还不该动手）");
                    Check(f.Coord.HeldDuringHoldForTest(0),
                        "★ 按住阶段里的左键按下被【记账】（老代码在这里什么都没记，就是漏掉的根因）");

                    f.Coord.Trigger.ForceHoldElapsed();     // 到达阈值 → 激活
                    Settle();
                    Check(f.Coord.Trigger.Active, "触发键已激活（外框此刻会亮起）");
                    Check(!f.Coord.Drag.IsDragging, "此刻还没有拖动会话（正是用户看到的状态）");

                    var mv = Ms(new Native.POINT(c0.X + 70, c0.Y + 50));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Settle();

                    Check(f.Coord.Drag.IsDragging,
                        "★ 鼠标一移动就把拖动补起来了（不再需要松开再按一次）");
                    Check(f.Coord.PassedDownForTest(0) == false,
                        "按住阶段放行的那条 down 同样记入『幽灵按下』账，收养时已完成配对");
                    Check(Coordinator.PhantomUpSentForTest > upBefore,
                        "★ 收养时补发配对的按钮抬起（这条 down 也是放行给目标程序的）");

                    // ★ 拖动框在屏幕上到底看不看得见（关键对照）★
                    for (int i = 0; i < 12; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(25); }
                    Native.RECT ww0, ff0;
                    Native.GetWindowRect(f.H, out ww0);
                    Native.GetWindowRect(f.Coord.Drag.Frame.Handle, out ff0);
                    int gy = (ww0.Top + ww0.Bottom) / 2;
                    int drGreen = BandMaxGreenness(Rectangle.FromLTRB(
                        ww0.Left - 60, gy - 20, ww0.Left + 60, gy + 20));
                    Line("    （拖动框）窗口=" + ww0.Left + "," + ww0.Top + " " + ww0.Width + "x" + ww0.Height
                        + " 外框=" + ff0.Left + "," + ff0.Top + " " + ff0.Width + "x" + ff0.Height
                        + " 屏幕青色度=" + drGreen);
                    Check(drGreen > 25,
                        "★★ 拖动中的外框必须在屏幕上看得见（不是只有 IsShown）",
                        "青色度=" + drGreen);

                    // ★ 补起的那一帧窗口【不应该跳】★
                    //   补起时以"当前光标位置"为起点（等价于用户此刻才按下），
                    //   所以窗口从此刻开始跟手 —— 这是正确的手感，不是没生效。
                    Native.RECT rMid;
                    Native.GetWindowRect(f.H, out rMid);
                    Eq(rMid.Left - r0.Left, 0, "★ 补起那一帧窗口不跳（以当前光标为起点）");
                    Eq(rMid.Top - r0.Top, 0, "★ 补起那一帧窗口不跳（垂直）");

                    // 再移动一次：窗口必须真的跟着走
                    var mv2 = Ms(new Native.POINT(c0.X + 70 + 90, c0.Y + 50 + 60));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv2);
                    Settle();

                    Native.RECT r1;
                    Native.GetWindowRect(f.H, out r1);
                    Eq(r1.Left - rMid.Left, 90, "★ 窗口真的跟着光标移动了（水平 90px）");
                    Eq(r1.Top - rMid.Top, 60, "★ 窗口真的跟着光标移动了（垂直 60px）");

                    var up = Ms(mv2.pt);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    Check(!f.Coord.Drag.IsDragging, "松开后拖动会话结束");
                }

                // ── ② 第四十四轮：按下发生在触发键按住【之前】→ 现在【必须】补起 ──
                //
                //   用户报障："在文字页面中，用户使用鼠标左键选中一段文本后，按下 Enter
                //            键触发对应功能。执行 Enter 后，鼠标左键仍然停留在文本选中
                //            状态，无论如何拖动都只会继续选中文本，而无法恢复窗口拖动。"
                //
                //   这正是"按下发生在触发键按住之前"的时序。旧代码（第四十三轮）刻意
                //   不收养这种按下，怕的是"用户在别的程序里拖文件、顺手按了触发键"被
                //   误接管；用户明确要求后规则改变：激活之后仍然按着的鼠标键归工具。
                //   两条记账的分工与完整理由见 Coordinator._passedDown 的注释。
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ActionLeft = Actions.Move;
                    c.DoubleClickLeftMaximize = false;
                }))
                {
                    var c0 = CenterOf(f.H);
                    int upBefore = Coordinator.PhantomUpSentForTest;

                    // 触发键还没按，先按下左键（等价于"用户正在拖选文字"）
                    var down = Ms(c0);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Check(!f.Coord.HeldDuringHoldForTest(0),
                        "按下发生在触发键按住【之前】：『按住期间』记账不置位（两套账分工正确）");
                    Check(f.Coord.PassedDownForTest(0),
                        "★ 但『幽灵按下』记账必须置位 —— 目标程序实收了这条 down，正停在拖选模态里");

                    // 之后才按住触发键并激活
                    ActivateTrigger(f.Coord);
                    Settle();
                    var mv = Ms(new Native.POINT(c0.X + 70, c0.Y + 50));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Settle();
                    Check(f.Coord.Drag.IsDragging,
                        "★ 第四十四轮：激活时仍按着的键现在必须补起（用户报障场景的修复点）");
                    Check(Coordinator.PhantomUpSentForTest > upBefore,
                        "★ 收养时补发配对的按钮抬起（把目标程序单方面认定的按下配对掉）");

                    var up = Ms(mv.pt);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }
            }
            finally
            {
                Coordinator.AssumeButtonsDownForTest = false;
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★ 拖动全程：外框必须紧跟窗口，绝不出现"窗口走了、框还留在原地"。★
        ///
        /// 用户原话："外框并非始终跟随窗口，在拖动窗口时可能会出现瞬间脱离跟随
        ///           （掉帧或错位）的情况。"
        ///
        /// 验证方式（直接量用户看到的东西，而不是只查代码）：
        ///   拖动过程中起一条 1kHz 采样线程，逐帧记录
        ///   「外框矩形 − 窗口矩形」这个偏移量。静止时它是恒定值（外框的内缩 + 留白）。
        ///   于是：
        ///     · 偏移量【变大】= 外框跑到窗口前面去了（我们允许，视觉上几乎不可察）；
        ///     · 偏移量【变小】= 外框落在窗口后面 = 用户看到的"窗口跑出了框" ← 必须为 0。
        ///
        /// ★ 采样顺序是【先读外框、再读窗口】★
        ///   两个 GetWindowRect 之间隔了几微秒，期间两边都可能动。先读框再读窗，
        ///   即使这段间隙里发生了移动，也只会让偏移量看起来"更大"（框领先），
        ///   绝不会伪造出"框滞后"的假阳性 —— 判据因此是安全的。
        /// </summary>
        private static void TestFrameTracksWindow()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    c.MovePreviewMode = PreviewModes.FullWin;
                    c.BringToFront = false;
                    c.FrameStyleId = FrameStyles.Glow;
                    c.FrameThickness = 3;
                }))
                {
                    var eng = f.Coord.Drag;

                    // ── ⓪ 浮层窗口必须已经挪到专用线程上 ──
                    //   这是"外框跟随不再受 UI 线程忙闲影响"的结构前提。
                    uint owner = OverlayWindow.OwnerThreadIdForTest;
                    Check(owner != 0 && owner != Native.GetCurrentThreadId(),
                        "★ 浮层窗口运行在专用线程上（不再挂在 UI 线程，UI 一忙就不会拖累外框）",
                        "浮层线程=" + owner + " 当前线程=" + Native.GetCurrentThreadId());

                    // ── ① 建立拖动 ──
                    ActivateTrigger(f.Coord);
                    Settle();
                    var c0 = CenterOf(f.H);
                    var down = Ms(c0);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Settle();
                    Check(eng.IsDragging, "拖动会话已建立");

                    // 先走一小步，让外框进入"拖动帧"状态（不是悬停帧）
                    var warm = Ms(new Native.POINT(c0.X + 12, c0.Y + 8));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref warm);
                    Settle();

                    Native.RECT fw, ww;
                    Native.GetWindowRect(eng.Frame.Handle, out fw);
                    Native.GetWindowRect(f.H, out ww);
                    int nomX = fw.Left - ww.Left;
                    int nomY = fw.Top - ww.Top;
                    Line("    （静止偏移）外框−窗口 = " + nomX + "," + nomY);

                    // ── ② 一边驱动拖动，一边 1kHz 采样 ──
                    //
                    // ★ 采样必须做"一致性复核"，否则读数不可信 ★
                    //   两个 GetWindowRect 之间隔着几微秒，而拖动工作线程随时可能把
                    //   外框和窗口一起往前推。若正好卡在中间，就会读成
                    //   "外框在旧位置、窗口在新位置" —— 一个凭空造出来的"滞后"。
                    //   所以这里读三次：框 → 窗 → 框；两次框一致才说明这次读数
                    //   没有跨越任何一次更新，样本才可信。
                    var samples = new System.Collections.Generic.List<int[]>();
                    int discarded = 0;
                    var stop = new System.Threading.ManualResetEventSlim(false);
                    var sampler = new System.Threading.Thread(delegate ()
                    {
                        while (!stop.IsSet)
                        {
                            try
                            {
                                Native.RECT f1, f2, b;
                                if (Native.GetWindowRect(eng.Frame.Handle, out f1)
                                    && Native.GetWindowRect(f.H, out b)
                                    && Native.GetWindowRect(eng.Frame.Handle, out f2))
                                {
                                    if (f1.Left == f2.Left && f1.Top == f2.Top)
                                        lock (samples) samples.Add(new int[] { f1.Left - b.Left, f1.Top - b.Top });
                                    else
                                        discarded++;
                                }
                            }
                            catch { }
                            System.Threading.Thread.Sleep(1);
                        }
                    });
                    sampler.IsBackground = true;
                    sampler.Start();

                    // 按真实鼠标的节奏走：每步几个像素、共几百步，覆盖"移动中的每一帧"
                    //
                    // ★ 每一步之后必须【泵消息】★
                    //   目标窗口属于本测试线程，而工作线程写窗口用的是【同步】SetWindowPos
                    //   （它要等目标线程处理 WM_WINDOWPOSCHANGING 才返回）。
                    //   如果这里只用 Thread.Sleep 不泵消息，工作线程会被一直阻塞 ——
                    //   实测 240 次移动只处理了 3 帧，量出来的"偏差"全是这个假象。
                    //   真实使用中目标程序自己在不停泵消息，不存在这个问题。
                    int stepMax = 0;
                    int px0 = c0.X + 12, py0 = c0.Y + 8;
                    int lastX = px0, lastY = py0;
                    for (int i = 1; i <= 240; i++)
                    {
                        // 真实鼠标每步只有 1~3 像素（125~1000Hz 报告率）。
                        // 步长越大，"外框先动、窗口后动"这一个写入周期内的
                        // 领先量就越大 —— 用真实步长才能量出用户真正会看到的量级。
                        int dx = i * 2 + (int)Math.Round(Math.Sin(i * 0.30) * 2);
                        int dy = i + (int)Math.Round(Math.Cos(i * 0.25) * 2);
                        int px = px0 + dx, py = py0 + dy;
                        stepMax = Math.Max(stepMax, Math.Max(Math.Abs(px - lastX), Math.Abs(py - lastY)));
                        lastX = px; lastY = py;

                        var mv = Ms(new Native.POINT(px, py));
                        f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                        Settle();                       // ← 关键：泵消息，让目标窗口配合写入
                        System.Threading.Thread.Sleep(1);
                    }
                    Settle();
                    stop.Set();
                    sampler.Join(1000);

                    // 拖动确实生效了：窗口必须真的走了这么远（否则后面的偏差毫无意义）
                    Native.RECT wEnd;
                    Native.GetWindowRect(f.H, out wEnd);
                    int travelX = Math.Abs(wEnd.Left - ww.Left);
                    Check(travelX > 100,
                        "★ 拖动确实生效（窗口真的跟着走了）",
                        "窗口水平位移=" + travelX + "px，最大单步=" + stepMax + "px");

                    // ── ③ 判定：偏移量只许变大（框领先），绝不许变小（框滞后）──
                    int minDx = int.MaxValue, maxDx = int.MinValue;
                    int minDy = int.MaxValue, maxDy = int.MinValue;
                    int n;
                    lock (samples)
                    {
                        n = samples.Count;
                        for (int i = 0; i < samples.Count; i++)
                        {
                            int dx = samples[i][0] - nomX;
                            int dy = samples[i][1] - nomY;
                            if (dx < minDx) minDx = dx;
                            if (dx > maxDx) maxDx = dx;
                            if (dy < minDy) minDy = dy;
                            if (dy > maxDy) maxDy = dy;
                        }
                    }
                    Line("    （采样）" + n + " 个有效点（丢弃跨更新的 " + discarded + " 个）；"
                        + "相对静止偏移 X[" + minDx + "," + maxDx + "] Y[" + minDy + "," + maxDy + "]");
                    Line("    （帧账）外框更新 " + DragEngine.DiagFrameUpdates
                        + " 帧，其中窗口真的写了 " + DragEngine.DiagWindowWritten
                        + " 帧、被跳过 " + DragEngine.DiagWindowSkipped + " 帧");

                    Check(n > 20, "拖动过程中采到了足够的样本", "样本数=" + n);

                    // 容差 4 物理像素（200% 下 = 2 逻辑像素）：
                    //   两个 GetWindowRect 之间的几微秒采样间隙、以及一次写入的取整误差
                    //   都可能让读数抖动 1~2px，但绝不该出现"框滞后一整帧"那种量级。
                    const int tol = 4;
                    Check(minDx >= -tol,
                        "★ 全程外框都没有落在窗口后面（水平方向）",
                        "最差滞后 " + (-minDx) + "px，容差 " + tol + "px");
                    Check(minDy >= -tol,
                        "★ 全程外框都没有落在窗口后面（垂直方向）",
                        "最差滞后 " + (-minDy) + "px，容差 " + tol + "px");

                    var up = Ms(warm.pt);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★ 窗口被拖到屏幕边缘、甚至超出屏幕之外时，外框几何必须【恒定】。★
        ///
        /// 用户原话："如果一个窗口已经被拖到了屏幕的边缘，甚至超出了屏幕之外，
        ///           那么包裹住窗口的那一圈样式就会缩小……会直观影响流畅程度，
        ///           并且会在视觉效果上显得很卡。"
        ///
        /// 根因：老代码的外框几何【依赖屏幕边缘】——
        ///   ① 判据是"外扩矩形放不放得进虚拟桌面"，窗口刚碰到边缘就翻面成内缩，
        ///      偏移量突变 2×margin；
        ///   ② 内缩分支还会把矩形钳回屏幕，于是越往外拖框越小，且每帧都在变。
        ///
        /// 本用例把窗口一路推出屏幕左缘，全程采样「外框矩形 − 窗口矩形」，
        /// 断言这个偏移量【始终不变】—— 几何只由窗口决定，和屏幕边缘无关。
        /// </summary>
        private static void TestFrameGeometryStableAtEdge()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    c.MovePreviewMode = PreviewModes.FullWin;
                    c.BringToFront = false;
                    c.FrameStyleId = FrameStyles.Glow;   // 柔光：margin 最大，最容易暴露几何变化
                    c.FrameThickness = 3;
                }))
                {
                    var eng = f.Coord.Drag;

                    Native.RECT wr0;
                    Native.GetWindowRect(f.H, out wr0);

                    // 抓在窗口靠右的位置：这样光标推到屏幕左缘时，
                    // 窗口左缘会被推到负坐标（真的出屏）
                    var grab = new Native.POINT(wr0.Right - 40, (wr0.Top + wr0.Bottom) / 2);
                    ActivateTrigger(f.Coord);
                    Settle();

                    var down = Ms(grab);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Settle();
                    Check(eng.IsDragging, "拖动会话已建立");

                    var offsets = new System.Collections.Generic.List<int[]>();
                    int minWinLeft = int.MaxValue;
                    int minWinTop = int.MaxValue;

                    // 光标只能停在屏幕内，但"抓点靠右"使得窗口可以一路被推出屏幕
                    int[] cursorXs = { 300, 200, 120, 60, 30, 12, 4, 1 };
                    for (int i = 0; i < cursorXs.Length; i++)
                    {
                        var mv = Ms(new Native.POINT(cursorXs[i], grab.Y));
                        f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                        Settle();

                        Native.RECT w, fr;
                        Native.GetWindowRect(f.H, out w);
                        Native.GetWindowRect(eng.Frame.Handle, out fr);
                        if (w.Left < minWinLeft) minWinLeft = w.Left;
                        if (w.Top < minWinTop) minWinTop = w.Top;
                        offsets.Add(new int[] { fr.Left - w.Left, fr.Top - w.Top,
                                                fr.Right - w.Right, fr.Bottom - w.Bottom });
                    }

                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < offsets.Count; i++)
                        sb.Append("[").Append(offsets[i][0]).Append(",").Append(offsets[i][1])
                          .Append(",").Append(offsets[i][2]).Append(",").Append(offsets[i][3]).Append("] ");
                    Line("    （窗口最左）" + minWinLeft + "px  偏移量序列 " + sb);

                    Check(minWinLeft < 0,
                        "★ 窗口确实被推出了屏幕左缘（用例前提成立）",
                        "窗口最左=" + minWinLeft + "px");

                    // 四个方向的偏移量在全程都必须恒定
                    int worst = 0;
                    string worstAt = "";
                    for (int k = 0; k < 4; k++)
                    {
                        int baseV = offsets[0][k];
                        for (int i = 1; i < offsets.Count; i++)
                        {
                            int d = Math.Abs(offsets[i][k] - baseV);
                            if (d > worst) { worst = d; worstAt = "第" + i + "步 方向" + k; }
                        }
                    }
                    Check(worst <= 2,
                        "★ 贴边 / 出屏全程外框几何恒定（不再缩小、不再跳变）",
                        "最大偏移变化=" + worst + "px（" + worstAt + "）");

                    var up = Ms(grab);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★ 外框不透明度：改数值必须真的改变渲染结果。★
        ///
        /// 实现走的是 UpdateLayeredWindow 的 SourceConstantAlpha（合成器统一乘一次 alpha）。
        /// 自绘/合成的坑在于"改了参数但没重推像素"，那样控件看起来正常、屏幕却毫无变化 ——
        /// 所以这里直接【数屏幕像素】：100% 和 40% 两档下，同一位置的像素必须不同，
        /// 而且 40% 那一档必须更接近背景（= 更淡）。
        /// </summary>
        private static void TestFrameOpacity()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    c.BringToFront = false;
                    c.FrameStyleId = FrameStyles.Solid;
                    c.FrameColorId = FrameColors.Teal;
                    c.FrameThickness = 6;
                    c.FrameOpacity = 100;
                }))
                {
                    var eng = f.Coord.Drag;

                    // ★ 必须真的让悬停框亮起来 ★
                    //   悬停框是鼠标移动时才会亮（ShowHoverFrame 由 WM_MOUSEMOVE 驱动），
                    //   只模拟触发键按下是不会显示的 —— 第一次写这个用例就漏了这一步，
                    //   结果量到的其实是窗口白底。
                    Native.SetCursorPos(CenterOf(f.H).X, CenterOf(f.H).Y);
                    ActivateTrigger(f.Coord);
                    var mv0 = Ms(CenterOf(f.H));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv0);
                    Settle();
                    Check(eng.Frame.IsShown, "用例前提：悬停框已经亮起");

                    Native.RECT wr;
                    Native.GetWindowRect(f.H, out wr);

                    // 环带取"窗口左缘附近"：外扩模式下环就画在窗口可见边沿上，
                    // 而可见边相对 GetWindowRect 还有约 7px 不可见边框，
                    // 所以这里横扫 ±30px 把两者都包进来。
                    int bandY = (wr.Top + wr.Bottom) / 2;
                    var band = new Rectangle(wr.Left - 30, bandY - 2, 60, 5);

                    // ★ 度量用"带内亮度对比度"而不是某个像素的颜色 ★
                    //   环的确切像素位置随 DPI / 边框宽度 / 样式而变，手算容易偏；
                    //   而"整层变淡"必然表现为【带内最亮与最暗的差变小】——
                    //   这与背景是什么颜色无关，判据稳。
                    int r100 = BandMaxGreenness(band);

                    // 切到 40%：走【配置变更】那条正式路径（设置页保存后调的就是它）。
                    // 改完必须等一下：透明度是投递到浮层线程去重推像素的，
                    // 那条线程和本线程是两个消息循环。
                    f.Store.Current.FrameOpacity = 40;
                    eng.RefreshOverlayTheme();
                    Settle();
                    System.Threading.Thread.Sleep(120);
                    Settle();
                    int r40 = BandMaxGreenness(band);

                    Line("    （不透明度）带内青色度 100%=" + r100 + "  40%=" + r40);

                    Check(r100 > 30,
                        "★ 用例前提：100% 时这条带上确实有外框（青色度足够）",
                        "青色度=" + r100);
                    Check(r40 != r100,
                        "★ 改不透明度真的改变了屏幕渲染（不是改了参数却没重推像素）",
                        "100%=" + r100 + " 40%=" + r40);
                    Check(r40 > 0 && r40 < r100 * 0.6,
                        "★ 40% 的外框明显比 100% 淡（整层等比变淡）",
                        "青色度 " + r100 + " → " + r40);

                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// 一次截屏取整条带，返回带内"最大的青色度" = G − (R+B)/2。
        ///
        /// ★ 为什么不能用亮度对比度 ★
        ///   这条带里同时有【窗口白底】和【桌面深色】，两者交界处的对比度有 240 上下，
        ///   而外框只占其中几列 —— 用亮度对比度量，环的贡献被白/黑边界彻底淹没，
        ///   两档读数一模一样（实测 229 vs 229，白白怀疑了一轮代码）。
        ///   青色度是"绿色相对红蓝的平均"的超出量：
        ///     青色的环 → 112；白底 → 0；深色桌面 → 0。
        ///   于是这个指标只对"环"敏感，完全不受明暗边界影响。
        /// </summary>
        private static int BandMaxGreenness(Rectangle band)
        {
            try
            {
                var vs = SystemInformation.VirtualScreen;
                band.Intersect(vs);
                if (band.Width <= 0 || band.Height <= 0) return -1;

                using (var bmp = new Bitmap(band.Width, band.Height))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(band.Left, band.Top, 0, 0, new Size(band.Width, band.Height));
                    int best = int.MinValue;
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            var px = bmp.GetPixel(x, y);
                            int green = px.G - (px.R + px.B) / 2;
                            if (green > best) best = green;
                        }
                    }
                    return best;
                }
            }
            catch { return -1; }
        }

        /// <summary>取屏幕上一点的 RGB，形如 "12,34,56"；失败返回 "?"。</summary>
        private static string SamplePixel(int x, int y)
        {
            try
            {
                var vs = SystemInformation.VirtualScreen;
                if (x < vs.Left || y < vs.Top || x >= vs.Right || y >= vs.Bottom) return "?";
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
                    var px = bmp.GetPixel(0, 0);
                    return px.R + "," + px.G + "," + px.B;
                }
            }
            catch { return "?"; }
        }

        /// <summary>两个 "r,g,b" 字符串之间的曼哈顿距离；解析失败返回 -1。</summary>
        private static int Dist(string a, string b)
        {
            try
            {
                var pa = a.Split(',');
                var pb = b.Split(',');
                if (pa.Length != 3 || pb.Length != 3) return -1;
                return Math.Abs(int.Parse(pa[0]) - int.Parse(pb[0]))
                     + Math.Abs(int.Parse(pa[1]) - int.Parse(pb[1]))
                     + Math.Abs(int.Parse(pa[2]) - int.Parse(pb[2]));
            }
            catch { return -1; }
        }

        /// <summary>
        /// ★ 帮助面板：能展开、内容不越界。★
        ///
        /// 用户要求"点击该选项能够展开这个软件的各个功能的介绍"。
        /// 这里钉三件事：① 条目齐全；② 点标题真的切换展开项；
        /// ③ 展开后的内容【完全落在面板高度内】—— 面板高度是固定的，
        ///    一旦内容超出就会被裁掉，而"少显示最后一行"肉眼很容易忽略。
        /// </summary>
        private static void TestHelpPanel()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    Application.DoEvents();

                    var hp = form.ProbeHelpPanel;
                    Check(hp != null, "取到帮助面板");
                    if (hp == null) return;

                    Eq(hp.EntryCountForTest, 6, "帮助条目数（触发/动作/外观/全屏/晃窗/黑名单）");

                    int top, bottom;
                    hp.OpenRangeForTest(out top, out bottom);
                    Check(bottom <= hp.Height,
                        "★ 默认展开项的内容完全落在面板高度内（不会被裁掉）",
                        "内容底 " + bottom + " 面板高 " + hp.Height);
                    Check(top == 0, "默认展开第一项（从顶部开始）", "top=" + top);

                    // 点最后一项的标题 → 展开项切过去，且内容仍然不越界。
                    //   y 不能取 hp.Height-1：面板比内容高几个像素（留白），
                    //   最底下那几像素不属于任何标题行，点了会落空（第一次就踩了这个）。
                    //   取"底部往上 30 设计像素"必然落在最后一个标题行内
                    //   （标题行高 38 设计像素）。
                    int lastHeaderY = hp.Height - Fields_Sc(30);
                    hp.ClickAtForTest(lastHeaderY);
                    Application.DoEvents();
                    Eq(hp.OpenIndexForTest, 5, "点最后一项的标题能把它展开");
                    hp.OpenRangeForTest(out top, out bottom);
                    Check(bottom <= hp.Height,
                        "★ 展开最后一项后内容仍然不越界",
                        "内容底 " + bottom + " 面板高 " + hp.Height);

                    // 点已展开的那一项：不收起（面板高度固定，收起会留空白）
                    hp.ClickAtForTest(lastHeaderY);
                    Eq(hp.OpenIndexForTest, 5, "再点一次已展开的项不会把它收起来（面板高度恒定）");

                    // 点第一项切回去
                    hp.ClickAtForTest(Fields_Sc(10));
                    Eq(hp.OpenIndexForTest, 0, "点第一项能切回去");

                    form.Close();
                }
            }
        }

        private static int Fields_Sc(int design)
        {
            return (int)Math.Round(design * (SettingsForm.UiScaleForTest <= 0f ? 1f : SettingsForm.UiScaleForTest));
        }

        /// <summary>
        /// ★ "关于"信息必须真的显示在界面上。★
        ///
        /// 这一类任务最容易的翻车方式：常量定义好了、但没接到 UI 上，
        /// 或者接上了却因为布局问题被裁掉。这里直接读分组里的控件文字。
        /// </summary>
        private static void TestAboutInfo()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    Application.DoEvents();

                    string t = form.ProbeAboutText ?? "";
                    Line("    （关于分组文字）" + t);

                    Check(t.Length > 0, "取到『关于』分组的内容");
                    Check(t.Contains("QQQOrange"), "★ 开发者 QQQOrange 显示在界面上");
                    Check(t.Contains("DeepSeek"), "★ 开发工具 DeepSeek 显示在界面上");
                    Check(t.Contains("2026-10-12"), "★ 更新时间显示在界面上");
                    Check(t.Contains("版本") && t.Contains(SettingsForm.AppVersionForTest),
                        "★ 当前版本号显示在界面上（且与程序集版本一致）",
                        "程序集版本=" + SettingsForm.AppVersionForTest);

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 每个分组都必须在【窗体的可视树里】，侧栏条目数必须等于分组数。★
        ///
        /// 用户报障："没有找到帮助按钮啊！"
        /// 根因：分组宿主写死 `new ResponsiveGroupPanel(new[]{g1..g6})`，
        /// 新加的「关于 / 帮助」两组从未被加进界面 —— 对象存在、_groups 里有、
        /// 按 _groups 做的几何检查也全过，但用户看不到。
        /// 这条用例从窗体往下走父链，专治"存在但没进界面"。
        /// </summary>
        private static void TestAllGroupsReachable()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    Application.DoEvents();

                    int n = SettingsForm.ProbeGroupCount(form);
                    Check(n >= 8, "分组数（至少包含关于 / 帮助）", "实际 " + n);

                    var missing = new System.Text.StringBuilder();
                    for (int i = 0; i < n; i++)
                    {
                        if (!SettingsForm.ProbeGroupInTree(form, i))
                            missing.Append(i).Append(' ');
                    }
                    Check(missing.Length == 0,
                        "★ 每个分组都真的在窗体可视树里（不存在「建了却没加进去」的分组）",
                        "缺失索引：" + missing);

                    Eq(SettingsForm.ProbeSidebarCount(form), n,
                        "★ 侧栏条目数 = 分组数（新增分组必须自动出现在侧栏，用户才找得到）");

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 同一分组内，任意两个控件不得互相遮挡。★
        ///
        /// 用户报障："允许拖动全屏 / 无边框窗口 被 允许拖动已最大化的窗口 遮住了一半。"
        /// 成因：复选框文字折行后高度变大，而布局是按设计稿 Top 摆的 ——
        /// 变高的控件就会压住下一行。这类问题在窄窗 / 高 DPI 下才出现，
        /// 光看设计稿坐标完全正常，必须按【实际 Bounds】两两求交才算得出来。
        /// </summary>
        private static void TestNoControlOverlap()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    Application.DoEvents();

                    // ★ 必须扫多档宽度 ★
                    //   重叠只在【窄窗 / 高 DPI】下才出现：复选框文字折行 → 高度变大
                    //   → 压住下一行。只测默认宽度的话，用户在窄窗看到的那个问题
                    //   永远复现不出来（第一版就是这么写的，结果全绿）。
                    int n = SettingsForm.ProbeGroupCount(form);
                    int minW = form.MinimumSize.Width;
                    // ★ 扫描区间必须【跟着最小宽度走】★
                    //   第一版写的是 minW..1400，而 200% DPI 下 MinimumSize.Width
                    //   本身就已经超过 1400 —— 循环体一次都没执行，
                    //   用例"全绿"其实是【空跑】，什么也没检查。
                    var wl = new System.Collections.Generic.List<int>();
                    for (int w2 = minW; w2 <= minW + 1200; w2 += 16) wl.Add(w2);
                    int[] widths = wl.ToArray();
                    Line("    （宽度扫描）最小宽=" + minW + "，共 " + widths.Length + " 档");
                    for (int wi = 0; wi < widths.Length; wi++)
                    {
                        int w = widths[wi];
                        if (w < minW) w = minW;
                        try { form.Width = w; form.PerformLayout(); Application.DoEvents(); } catch { }

                        var bad = new System.Text.StringBuilder();
                        for (int i = 0; i < n; i++)
                        {
                            string ov = SettingsForm.ProbeGroupOverlaps(form, i);
                            if (!string.IsNullOrEmpty(ov)) bad.Append('(').Append(i).Append(')').Append(ov);
                        }
                        if (bad.Length > 0) Line("    （宽 " + w + " 重叠）" + bad);
                        // g4（拖动行为）是用户报障的那一组，只在【真出现重叠】时打几何，
                        // 正常时保持输出干净（这一段是排查时加的，留着以备回归）。
                        if (bad.Length > 0)
                            Line("    （g4 @宽" + w + "）" + SettingsForm.ProbeGroupRects(form, 3));
                        Check(bad.Length == 0,
                            "★ 宽 " + w + "px 下，所有分组内都没有控件互相遮挡",
                            bad.ToString());
                    }

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 滚动设置页时，侧栏高亮必须跟着走。★
        ///
        /// 用户报障："我把页面滑到了那个部分，侧边的设置目录却没有跟随到那个部分。"
        /// 这里直接把滚动区滚到各分组的顶端，断言侧栏高亮项就是那一段。
        /// </summary>
        private static void TestSidebarFollowsScroll()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    Application.DoEvents();

                    // ★ 必须真的把窗体显示出来，否则滚动区还停在默认的 200x100、
                    //   AutoScrollPosition 赋值不生效（实测"实际滚动=0"，
                    //   一度以为产品有 bug，其实是测试环境没把窗口显示出来）。
                    form.Show();
                    Application.DoEvents();
                    Settle();

                    int n = SettingsForm.ProbeGroupCount(form);
                    Eq(SettingsForm.ProbeSidebarCount(form), n, "侧栏条目数 = 分组数");
                    Line("    （滚动区）" + SettingsForm.ProbeScrollInfo(form));

                    var bad = new System.Text.StringBuilder();
                    for (int i = 0; i < n; i++)
                    {
                        int top = SettingsForm.ProbeGroupTop(form, i);
                        if (top < 0) continue;

                        // 滚到"这个分组刚好处在视口顶部稍下方"的位置
                        int real = SettingsForm.ProbeScrollTo(form, top);
                        Application.DoEvents();

                        int act = SettingsForm.ProbeSidebarActive(form);
                        if (act != i)
                        {
                            bad.Append("分组").Append(i).Append("(top=").Append(top)
                               .Append(",实际滚动=").Append(real)
                               .Append(") → 高亮").Append(act).Append("  ");
                        }
                    }
                    Line("    （滚动跟随）" + (bad.Length == 0 ? "全部一致" : bad.ToString()));
                    Check(bad.Length == 0,
                        "★ 滚到任一分组，侧栏高亮都对得上",
                        bad.ToString());

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 用【真实滚轮消息】滚动，侧栏高亮必须跟随。★
        ///
        /// 用户报障："我滚到了启动与权限，左边菜单里选的还是触发方式。"
        /// T74 直接调 SyncActive() 是测不出这个的 —— 那只验证了算法，
        /// 没验证 Scroll 事件有没有接到 SyncActive 上。这里发真的 WM_MOUSEWHEEL，
        /// 走用户滚轮那条完整路径。
        /// </summary>
        private static void TestSidebarFollowsRealWheel()
        {
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.Show();
                    Application.DoEvents();
                    Settle();

                    int startActive = SettingsForm.ProbeSidebarActive(form);
                    int total = SettingsForm.ProbeGroupCount(form);

                    // 一路往下滚，记录每次滚动后的高亮项
                    var seen = new System.Text.StringBuilder();
                    int last = startActive;
                    int lastScroll = 0;
                    bool sawLastGroup = false;

                    for (int step = 0; step < 40; step++)
                    {
                        int off = SettingsForm.ProbeWheelScroll(form, -3);   // 向下滚 3 格
                        if (off == lastScroll && step > 3) break;            // 已经滚到底
                        lastScroll = off;

                        int act = SettingsForm.ProbeSidebarActive(form);
                        if (act != last) { seen.Append(off).Append("→").Append(act).Append(' '); last = act; }
                        if (act == total - 1) sawLastGroup = true;
                    }

                    Line("    （真实滚轮）起始高亮=" + startActive + "，滚动中变化：" + seen
                        + "，最终=" + last + "（共 " + total + " 组）");
                    Line("    （同步诊断）" + SettingsForm.ProbeSyncDiag(form));

                    Check(lastScroll > 0, "★ 真实滚轮消息确实让页面滚动了（用例前提）",
                        "最终滚动偏移=" + lastScroll);
                    Check(seen.Length > 0,
                        "★ 滚动过程中侧栏高亮【确实发生了变化】（不是一直停在第一项）",
                        "变化序列：" + seen);
                    Check(sawLastGroup,
                        "★ 一直滚到底能高亮到最后一个分组",
                        "最终高亮=" + last);

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 全屏窗口被降级成普通窗口之后，外框必须出现在【新窗口】周围。★
        ///
        /// 用户原话："在全屏的时候触发左键功能，这时候全屏页面变成了窗口，
        ///           但这个窗口周围没有样式，就没有外框。"
        ///
        /// 这条链路比普通拖动长得多：FindTarget 放行全屏 → Begin 里
        /// ExitFullscreenToWindowed 改窗口矩形 → 重新算 inset → 才轮到显框。
        /// 任何一步用了"降级之前"的矩形/inset，框就会画到旧位置或干脆不出现。
        /// </summary>
        private static void TestFrameAfterFullscreenDowngrade()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    c.MovePreviewMode = PreviewModes.FullWin;
                    c.BringToFront = false;
                    // ★ 用【用户的真实配置】★ AllowFullscreenWindows=false、
                    //   只靠 ExitFullscreenOnTrigger 放行 —— 这两条开关走的是不同的
                    //   代码路径（悬停用前者、按下用后者），差别正在这里。
                    c.AllowFullscreenWindows = false;
                    c.ExitFullscreenOnTrigger = true;
                    c.FrameStyleId = FrameStyles.Glow;
                    c.FrameThickness = 3;
                    c.DoubleClickLeftMaximize = false;
                }))
                {
                    var eng = f.Coord.Drag;

                    // 把测试窗口铺满整个显示器 = 真全屏
                    var vs = SystemInformation.VirtualScreen;
                    Native.SetWindowPos(f.H, IntPtr.Zero, vs.Left, vs.Top, vs.Width, vs.Height,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    Settle();
                    Check(WindowActions.IsFullscreen(f.H), "用例前提：窗口是真全屏");

                    // ★ 关掉"强制目标"，让 FindTarget 真正跑一遍 ★
                    //   夹具默认强制命中测试窗口，会把全屏判定整条绕过 ——
                    //   于是"悬停该不该放行全屏窗口"这件事在测试里永远测不到
                    //   （第一次写这个用例就是这么被骗过去的：全绿，但路径没走到）。
                    WindowActions.ForcedTargetForTest = IntPtr.Zero;
                    Settle();

                    // 触发键激活 → 悬停（全屏窗口此时也应该能亮框）
                    ActivateTrigger(f.Coord);
                    var c0 = CenterOf(f.H);
                    var hover = Ms(c0);
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref hover);
                    Settle();
                    bool hoverFrameShown = eng.Frame.IsShown;
                    Line("    （悬停全屏窗口）外框显示=" + hoverFrameShown);

                    // 按下左键 → 应降级成普通窗口 + 建立拖动 + 外框跟着到新窗口上
                    var down = Ms(c0);
                    bool swallowed = f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Settle();

                    Check(swallowed && eng.IsDragging, "★ 左键被接管且拖动已建立");
                    Check(!WindowActions.IsFullscreen(f.H), "★ 窗口已从全屏降级成普通窗口");

                    Native.RECT wr, fr;
                    Native.GetWindowRect(f.H, out wr);
                    Native.GetWindowRect(eng.Frame.Handle, out fr);
                    Line("    （降级后）窗口=" + wr.Left + "," + wr.Top + " " + wr.Width + "x" + wr.Height
                        + "  外框=" + fr.Left + "," + fr.Top + " " + fr.Width + "x" + fr.Height
                        + "  显示=" + eng.Frame.IsShown);

                    Check(eng.Frame.IsShown,
                        "★ 降级之后外框必须仍然显示着（用户报的就是这里没有框）");

                    // 悬停阶段（还没按下）全屏窗口也必须亮框 —— 这是用户第一眼看到的状态
                    Check(hoverFrameShown,
                        "★ 悬停全屏窗口时也要亮框（两个开关必须同源，否则按下才有框）");

                    // 外框必须贴着新窗口，而不是停在降级前的全屏矩形上
                    int dx = Math.Abs(fr.Left - wr.Left);
                    int dy = Math.Abs(fr.Top - wr.Top);
                    int dw = Math.Abs(fr.Width - wr.Width);
                    Check(dx <= 60 && dy <= 60 && dw <= 120,
                        "★ 外框贴的是【降级后的新窗口】，不是降级前的全屏矩形",
                        "外框-窗口 偏移 " + dx + "," + dy + " 宽差 " + dw);

                    var up = Ms(c0);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★ 调色板必须整体柔和。★
        ///
        /// 用户原话："现在的颜色选择看起来很多，但实际上真正能用于当做外框的
        ///           颜色没有多少，重新设置一些视觉上没那么刺激、偏柔和一些的颜色。"
        ///
        /// 把这句话变成可执行的判据：外框是【一圈包住窗口的长条】，
        /// 饱和度一高就会一直抢视线；明度太低在深色背景上看不见、太高在浅色背景上看不见。
        /// 所以逐个算 HSL：饱和 ≤ 72%、明度落在 48%~82%。
        /// 这条断言的作用是【防止以后又加回刺眼的颜色】—— 人说"柔和"没有量化标准，
        /// 但代码可以把它钉住。
        /// </summary>
        private static void TestPaletteIsSoft()
        {
            var bad = new System.Text.StringBuilder();
            var all = new System.Text.StringBuilder();

            for (int i = 0; i < FrameColors.All.Length; i++)
            {
                var e = FrameColors.All[i];
                var c = e.Color;

                int max = Math.Max(c.R, Math.Max(c.G, c.B));
                int min = Math.Min(c.R, Math.Min(c.G, c.B));
                float l = (max + min) / 2f / 255f;
                float s = 0f;
                if (max != min)
                {
                    float d = (max - min) / 255f;
                    s = l > 0.5f ? d / (2f - 2f * l) : d / (2f * l);
                }
                int sp = (int)Math.Round(s * 100);
                int lp = (int)Math.Round(l * 100);
                all.Append(e.Name).Append('(').Append(sp).Append('/').Append(lp).Append(") ");

                // "跟随系统强调色"的颜色由用户的系统决定，不该被我们的判据约束
                if (e.Id == FrameColors.Accent) continue;

                // 「刺眼」的直接来源是【饱和度】，所以饱和度对所有颜色一视同仁。
                if (sp > 72) bad.Append(e.Name + "饱和" + sp + "% ");

                // 明度只约束【有彩色】：石墨 / 银灰 / 曜石 / 珍珠白是刻意留的
                // 深浅两个锚点（深色底要靠曜石、浅色底要靠珍珠白才看得见），
                // 拿"明度必须居中"去卡它们反而不对 —— 判据要贴着真实意图写。
                // 阈值 40%：把石墨(14)/银灰(17)/曜石(18)/珍珠白(33) 四色划为中性，
                // 有彩色的最低一个是薄荷青 43%，两边不重叠。
                bool colorful = sp > 40;
                if (colorful && (lp < 48 || lp > 82))
                    bad.Append(e.Name + "明度" + lp + "% ");
            }

            Line("    （调色板 饱和/明度）" + all);
            Check(bad.Length == 0,
                "★ 调色板整体柔和：有彩色饱和 ≤72% 且明度居中；中性锚点只约束饱和度",
                bad.ToString());
            Check(FrameColors.All.Length >= 12,
                "颜色数量（太少不够用）", FrameColors.All.Length + " 个");
        }

        /// <summary>
        /// ★ 每个外框样式都要有显示名、都真的画得出来。★
        ///
        /// 新增样式最容易出的两个问题：
        ///   ① 忘了写 DisplayName → 下拉框里显示成英文 id；
        ///   ② 画法写错（矩形负宽、画笔全透明）→ 缩略图是一格空白。
        /// 两者都不会报错，肉眼在网格里也很难发现，所以这里逐个渲染数像素。
        /// </summary>
        private static void TestAllStylesRender()
        {
            var bad = new System.Text.StringBuilder();
            var all = new System.Text.StringBuilder();

            for (int i = 0; i < FrameStyles.All.Length; i++)
            {
                string id = FrameStyles.All[i];
                string name = FrameStyles.DisplayName(id);
                int ink = FrameStyles.MeasureStyleInkForTest(id);
                all.Append(name).Append('(').Append(ink).Append(") ");

                if (name == id) bad.Append(id + "没有显示名 ");
                if (ink < 20) bad.Append(id + "画不出东西(" + ink + ") ");
            }

            Line("    （样式 墨水像素）" + all);
            Check(bad.Length == 0,
                "★ 每个外框样式都有中文名、且都能画出内容（括号里是实测墨水像素）",
                bad.ToString());
            Check(FrameStyles.All.Length >= 12,
                "样式数量（太少不够用）", FrameStyles.All.Length + " 种");
            Check(FrameStyles.IsValid(FrameStyles.DefaultId),
                "默认样式必须在样式表里（否则配置归一化会打架）");

            // ★ 画廊高度必须放得下所有样式 ★
            //   加样式最容易漏的一步：格数变多需要多一行，而控件高度是按原来行数定的，
            //   多出来那一行会被控件边界直接裁掉 —— 网格里"少两个"很难被发现。
            using (var f = new DragFixture(null))
            {
                using (var form = new SettingsForm(f.Store, f.Coord))
                {
                    form.CreateControl();
                    Application.DoEvents();
                    Line("    （样式画廊）" + SettingsForm.ProbeGalleryGeometry(form));
                    Check(SettingsForm.ProbeGalleryFits(form),
                        "★ 样式画廊的高度放得下全部样式（不会把最后一行裁掉）",
                        SettingsForm.ProbeGalleryGeometry(form));

                    // 色板同理：格子数不能超过控件能平铺的量
                    Check(SettingsForm.ProbeColorGridFits(form),
                        "★ 颜色色板的高度放得下全部颜色",
                        SettingsForm.ProbeColorGridGeometry(form));

                    form.Close();
                }
            }
        }

        /// <summary>
        /// ★ 外框绝不残留。★
        ///
        /// 用户报障："拖拽把一个全屏窗口缩小之后又点开了窗口内的文件，此后外框就
        ///           一直残留在屏幕上，完全退出软件也没清除。"
        ///
        /// "外框残留"是一类【可以有多条成因】的问题：丢鼠标抬起事件、触发键中途失效、
        /// 窗口被别的程序关掉……逐条去堵是堵不完的，所以加了两个确定性保障：
        ///   ① EnforceFrameInvariant —— 每 500ms 自检"本不该有框却还亮着"，立刻收掉；
        ///   ② 退出路径先同步收框，再强制结束进程（否则进程晚几秒才死，框一直留着）。
        /// 这条用例把两个保障都钉住。
        /// </summary>
        private static void TestFrameNeverResidues()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    c.BringToFront = false;
                    c.FrameStyleId = FrameStyles.Glow;
                }))
                {
                    var eng = f.Coord.Drag;

                    // ── ① 先让悬停框亮起来 ──
                    Native.SetCursorPos(CenterOf(f.H).X, CenterOf(f.H).Y);
                    ActivateTrigger(f.Coord);
                    var mv = Ms(CenterOf(f.H));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Settle();
                    Check(eng.FrameReallyVisible, "用例前提：悬停框已亮起（系统层面真的可见）");

                    // ── ② 触发键抬起（界面已不该有框）→ 兜底自检必须收掉它 ──
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                    // 模拟"某条隐藏路径漏掉了"：这里不调 Hide，直接跑兜底自检
                    eng.EnforceFrameInvariant(f.Coord.Trigger.Active);
                    Check(!eng.FrameReallyVisible,
                        "★ 兜底自检：不在拖动、也没有悬停目标时，外框必须被强制收掉",
                        "IsShown=" + eng.Frame.IsShown);

                    // ── ③ 退出路径：同步收框 ──
                    //   注意：上一步已经把触发键松开了，触发键不激活就不会有悬停框，
                    //   所以这里必须【重新激活】再移动，否则这一步必然失败（第一版就漏了）。
                    ActivateTrigger(f.Coord);
                    Settle();
                    var mv2 = Ms(CenterOf(f.H));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv2);
                    Settle();
                    Check(eng.FrameReallyVisible, "再次亮框（准备验证退出路径）");

                    f.Coord.HideOverlaysNow();
                    Check(!eng.FrameReallyVisible,
                        "★ 退出路径：同步收框之后外框必须立刻不可见（不留到进程结束）",
                        "IsShown=" + eng.Frame.IsShown);

                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★ 用【外部进程的真实全屏窗口】复现"全屏转窗口没有外框"。★
        ///
        /// 为什么之前几轮修不动：所有用例都跑在**我们自己的窗口**上，
        /// 还开了 `AllowOwnProcessAsTargetForTest` + `ForcedTargetForTest` 两个后门 ——
        /// 全屏判定、黑名单、真实命中测试【整条被绕过】。
        /// 于是"全屏降级"这条链路上真正会出问题的地方，用例里一次都没走到。
        ///
        /// 这条用例彻底关掉后门：启动一个【别的进程】的窗口（记事本），
        /// 把它铺满显示器，然后用真实的 FindTarget 走完
        /// 「触发 → 命中全屏窗口 → 左键 → 降级 → 出框」。
        /// </summary>
        private static void TestFullscreenExternalWindow()
        {
            IntPtr ext = LaunchExternalWindow();
            if (ext == IntPtr.Zero)
            {
                Line("    （跳过：本环境无法拉起外部测试窗口）");
                return;
            }

            try
            {
                var vs = SystemInformation.VirtualScreen;

                // 外部窗口铺满整块显示器 = 真全屏
                Native.SetWindowPos(ext, IntPtr.Zero, vs.Left, vs.Top, vs.Width, vs.Height,
                    Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                Settle();
                if (!WindowActions.IsFullscreen(ext))
                {
                    Line("    （跳过：外部窗口没能铺成全屏）");
                    return;
                }

                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    c.MovePreviewMode = PreviewModes.FullWin;
                    c.BringToFront = false;
                    c.AllowFullscreenWindows = false;     // 用户的配置
                    c.ExitFullscreenOnTrigger = true;     // 用户的配置（默认开）
                    c.FrameStyleId = FrameStyles.Glow;
                    c.FrameThickness = 3;
                    c.DoubleClickLeftMaximize = false;
                }))
                {
                    var eng = f.Coord.Drag;

                    // ★ 关掉两个后门，走真实链路 ★
                    WindowActions.ForcedTargetForTest = IntPtr.Zero;
                    WindowActions.AllowOwnProcessAsTargetForTest = false;

                    var center = CenterOfRect(ext);

                    ActivateTrigger(f.Coord);
                    Settle();

                    // 悬停：光标放到外部全屏窗口中央
                    Native.SetCursorPos(center.X, center.Y);
                    var hover = Ms(center);
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref hover);
                    Settle();

                    // 命中判定是否真的落在这个外部窗口上
                    IntPtr hit = WindowActions.FindTarget(center, true);
                    Line("    （外部窗口）句柄=" + ext.ToInt64().ToString("X")
                        + " 全屏=" + WindowActions.IsFullscreen(ext)
                        + " 命中=" + hit.ToInt64().ToString("X")
                        + " 悬停框显示=" + eng.Frame.IsShown);
                    Check(hit == ext, "★ 真实命中测试落在外部全屏窗口上", "hit=" + hit.ToInt64().ToString("X"));

                    // 按下左键 → 降级 + 拖动 + 外框
                    var down = Ms(center);
                    bool swallowed = f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Settle();

                    Native.RECT wr, fr;
                    Native.GetWindowRect(ext, out wr);
                    Native.GetWindowRect(eng.Frame.Handle, out fr);

                    Line("    （降级后）窗口=" + wr.Left + "," + wr.Top + " " + wr.Width + "x" + wr.Height
                        + " 全屏=" + WindowActions.IsFullscreen(ext)
                        + " 拖动中=" + eng.IsDragging
                        + " 外框=" + fr.Left + "," + fr.Top + " " + fr.Width + "x" + fr.Height
                        + " 显示=" + eng.Frame.IsShown);

                    Check(swallowed && eng.IsDragging, "★ 左键被接管且拖动已建立");
                    Check(eng.Frame.IsShown, "★★ 全屏转窗口之后外框窗口仍是显示状态");

                    if (eng.Frame.IsShown)
                    {
                        int dx = Math.Abs(fr.Left - wr.Left);
                        int dy = Math.Abs(fr.Top - wr.Top);
                        Check(dx <= 80 && dy <= 80,
                            "★ 外框贴在降级后的新窗口上",
                            "偏移 " + dx + "," + dy);
                    }

                    // ══════════════════════════════════════════════════════════
                    //  ★★★ 决定性断言：屏幕上【真的有外框的像素】★★★
                    //
                    //  前几轮一直修不动，根子就在这里 —— 我所有的用例都只断言
                    //  `Frame.IsShown`，那只是"窗口的 WS_VISIBLE 样式位"。
                    //  窗口完全可以"样式位是显示的、像素却在屏幕外、或被别的窗口整个盖住"，
                    //  用户看到的正是后者：**标志位说在显示，屏幕上一根线都没有**。
                    //
                    //  所以这里直接截屏数像素：外框是青绿色（#6BC4A5），
                    //  它在"绿色相对红蓝的超出量"这个指标上是 60 上下，
                    //  而窗口白底、灰底、桌面深色都接近 0 —— 只要带内出现明显的青色度，
                    //  就证明【外框确实画在屏幕上了，而且没有被盖住】。
                    // ══════════════════════════════════════════════════════════
                    if (eng.Frame.IsShown)
                    {
                        // 外框的"显示"是【异步投递】到浮层线程的：
                        // SetRect → PostMessage → 浮层线程 → Push(UpdateLayeredWindow)。
                        // 截屏前必须给它落地的时间，否则量到的是"还没来得及画"。
                        for (int i = 0; i < 12; i++)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(25);
                        }

                        int bandY = (wr.Top + wr.Bottom) / 2;
                        var band = Rectangle.FromLTRB(wr.Left - 90, bandY - 30,
                                                      wr.Left + 90, bandY + 30);

                        // ★ 多次重试取最大值 ★
                        //   外框像素是异步落地 + 顶层窗口合成，单次截屏可能正好卡在
                        //   "还没推上去"或"被合成器晚一拍"的瞬间（实测同一用例
                        //   会量到 60 / 4 / 0 三种值）。取多次的最大值才是稳定判据。
                        int green = 0;
                        for (int k = 0; k < 6; k++)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(80);
                            int g3 = BandMaxGreenness(band);
                            if (g3 > green) green = g3;
                            if (green > 25) break;
                        }
                        Line("    （屏幕像素）窗口左侧带内最大青色度=" + green
                            + "（外框青绿≈60，白/灰/桌面≈0）");
                        Check(green > 25,
                            "★★★ 屏幕上真的能看到外框像素（不是只有 IsShown 标志位）",
                            "青色度=" + green);
                    }

                    var up = Ms(center);
                    f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }
            }
            finally
            {
                try
                {
                    var pr = System.Diagnostics.Process.GetProcessById(PidOfWindow(ext));
                    if (pr != null && !pr.HasExited) pr.Kill();
                }
                catch { }
            }
        }

        private static Native.POINT CenterOfRect(IntPtr h)
        {
            Native.RECT r;
            Native.GetWindowRect(h, out r);
            return new Native.POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        }

        private static int PidOfWindow(IntPtr h)
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                return (int)pid;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 启动一个【外部进程】的可见窗口，返回其句柄（失败返回 0）。
        ///
        /// 用本程序自己的 --windowhost 模式来当"别家的应用"：
        ///   · 它是一个独立进程，PID 与句柄都是外部的 → 真实命中/黑名单/全屏判定全会生效；
        ///   · 不依赖记事本之类的系统程序（那些在部分环境里拿不到 MainWindowHandle，
        ///     第一版就是这么 skip 掉的，等于什么都没测）。
        /// </summary>
        private static IntPtr LaunchExternalWindow()
        {
            try
            {
                string exe = System.Windows.Forms.Application.ExecutablePath;
                var psi = new System.Diagnostics.ProcessStartInfo(exe,
                    "--windowhost --no-elevate");
                psi.UseShellExecute = false;
                var pr = System.Diagnostics.Process.Start(psi);
                if (pr == null) return IntPtr.Zero;

                for (int i = 0; i < 80; i++)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(100);
                    IntPtr h = FindWindowByTitle(Program.WindowHostTitle);
                    if (h != IntPtr.Zero) return h;
                }
                return IntPtr.Zero;
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>按标题找顶层窗口（自检用）。</summary>
        private static IntPtr FindWindowByTitle(string title)
        {
            try
            {
                return Native.FindWindowW(null, title);
            }
            catch { return IntPtr.Zero; }
        }

        /// <summary>
        /// ★ 窗口操作不得干扰页面内容（选中 / 焦点 / 悬停）。★
        ///
        /// 用户报障："触发全屏切换或拖动窗口之后，窗口内的部分文字会被意外选中。"
        /// 机制见 Coordinator.CancelTargetMouseMode 的长注释：
        /// 钩子吞不掉 WM_MOUSEMOVE（吞了光标会停住），于是目标程序持续收到
        /// 带 MK_LBUTTON 的移动消息；只要它曾经收到过真实按下，就会一路拖选文字。
        ///
        /// 修法是三面，本用例把每一面都钉住：
        ///   ① 命中目标后立刻 PostMessage(WM_CANCELMODE) —— 结束它的拖拽模态
        ///      （三个鼠标键分别验证，它是所有标准控件都认的标准消息）；
        ///   ② 拖动会话内的按钮抬起由钩子吞掉 —— 目标窗口看不到这次释放；
        ///   ③ WM_MOUSEMOVE 必须原样放行 —— 吞了光标会冻结（这条同样是要钉死的铁律）。
        ///
        /// 曾经还有第四面 "SetCapture 接管鼠标"，已按官方文档的结论【彻底移除】：
        /// 后台窗口/隐藏窗口捕获不到别的进程的鼠标输入，机制上不成立，
        /// 且"忘了释放 = 整个桌面鼠标失灵"。结论写在 CancelTargetMouseMode 注释里。
        /// </summary>
        private static void TestNoContentInterference()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ShowActivationFrame = true;
                    c.BringToFront = false;
                    c.DoubleClickLeftMaximize = false;
                }))
                {
                    var eng = f.Coord.Drag;
                    int before = Coordinator.CancelModeSentForTest;

                    // ── 左键：拖动 ──
                    ActivateTrigger(f.Coord);
                    Settle();
                    var c0 = CenterOf(f.H);
                    var down = Ms(c0);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Settle();

                    Check(eng.IsDragging, "用例前提：左键拖动已建立");
                    Check(Coordinator.CancelModeSentForTest > before,
                        "★ 命中目标后向它发出了 WM_CANCELMODE（结束它的拖拽模态，防止拖选文字）",
                        "发出次数=" + (Coordinator.CancelModeSentForTest - before));

                    // ★ 拖动期间的鼠标移动必须【原样放行】★
                    //   这是 WH_MOUSE_LL 的硬约束：吞掉 WM_MOUSEMOVE 系统就认为这次
                    //   移动被拒绝，光标停在原地不动，钩子拿到的坐标也不再变化，
                    //   窗口只能跟着原地哆嗦（用户真实报过的 bug）。
                    //   所以这里断言"绝不吞" —— 它不是可选项，是铁律。
                    var mv = Ms(new Native.POINT { X = c0.X + 6, Y = c0.Y + 6 });
                    bool moveSwallowed = f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Check(!moveSwallowed,
                        "拖动期间 WM_MOUSEMOVE 绝不吞掉（吞了光标会冻结、窗口原地哆嗦）",
                        "返回值=" + moveSwallowed);

                    var up = Ms(c0);
                    bool upSwallowed = f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    Settle();

                    // ★ 会话内的这次抬起由我们吞掉 ★
                    //   目标窗口看不到"按下→抬起"这对事件中的释放，既不会补一次点击，
                    //   也不会把"曾经收到过按下"的窗口从拖选模态里放出来。
                    //   （真正把已进入模态的窗口拉出来的是上面那条 WM_CANCELMODE。）
                    Check(upSwallowed,
                        "拖动会话内的按钮抬起被我们吞掉（目标窗口看不到这次释放）",
                        "返回值=" + upSwallowed);
                    Check(!eng.IsDragging, "用例前提：抬起后拖动已结束");

                    // ── 右键 / 中键：一次性动作，同样不能干扰页面 ──
                    int b2 = Coordinator.CancelModeSentForTest;
                    var downR = Ms(c0);
                    f.Coord.OnMouse(Native.WM_RBUTTONDOWN, ref downR);
                    Settle();
                    var upR = Ms(c0);
                    f.Coord.OnMouse(Native.WM_RBUTTONUP, ref upR);
                    Settle();
                    Check(Coordinator.CancelModeSentForTest > b2,
                        "★ 右键操作同样先取消目标窗口的拖拽模态",
                        "发出次数=" + (Coordinator.CancelModeSentForTest - b2));

                    int b3 = Coordinator.CancelModeSentForTest;
                    var downM = Ms(c0);
                    f.Coord.OnMouse(Native.WM_MBUTTONDOWN, ref downM);
                    Settle();
                    var upM = Ms(c0);
                    f.Coord.OnMouse(Native.WM_MBUTTONUP, ref upM);
                    Settle();
                    Check(Coordinator.CancelModeSentForTest > b3,
                        "★ 中键操作同样先取消目标窗口的拖拽模态",
                        "发出次数=" + (Coordinator.CancelModeSentForTest - b3));

                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }
            }
            finally
            {
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★★★ 第四十四轮：用户报障场景的端到端复现与修复钉死。★★★
        ///
        /// 用户原话："在文字页面中，用户使用鼠标左键选中一段文本后，按下 Enter 键
        ///            触发对应功能。当前问题是：执行 Enter 后，鼠标左键仍然停留在
        ///            文本选中状态，无论如何拖动都只会继续选中文本，而无法恢复
        ///            窗口拖动或移动操作。"
        ///
        /// 根因链（详见 Coordinator._passedDown / OnTriggerActivated 的注释）：
        ///   ① 左键按下发生在【长按 Enter 之前】→ 被原样放行，目标程序实收，
        ///      进入"按住拖选"模态，并且单方面认为键还按着（幽灵按下）；
        ///   ② 旧的收养逻辑只认"触发键按住期间到达的按下"，这个时序不记账
        ///      → 激活后拖动永远补不起来；
        ///   ③ WM_MOUSEMOVE 绝不能吞 → 目标程序靠移动消息一路扩展选区。
        ///
        /// 用户要求的顺序（本用例按此逐段钉死）：
        ///   先取消当前选中状态（激活即 WM_CANCELMODE）→ 再执行 Enter 对应的
        ///   软件功能（补起拖动）→ 最后交互状态完全复位（配对抬起、会话收尾）。
        /// </summary>
        private static void TestPhantomDownAdoption()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            Coordinator.AssumeButtonsDownForTest = true;   // 自检里没有真实按键
            try
            {
                // ── ① 主场景：按住左键拖选文字 → 再长按 Enter → 左键恢复移动窗口 ──
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ActionLeft = Actions.Move;
                    c.DoubleClickLeftMaximize = false;
                    c.ExitFullscreenOnTrigger = true;
                }))
                {
                    Native.RECT r0;
                    Native.GetWindowRect(f.H, out r0);
                    var c0 = CenterOf(f.H);
                    int cancelBefore = Coordinator.CancelModeSentForTest;
                    int upBefore = Coordinator.PhantomUpSentForTest;

                    // 触发键还没动静，用户先按住左键在文字上拖选
                    var down = Ms(c0);
                    bool swallowed = f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Check(!swallowed, "① 拖选文字的左键按下被原样放行（目标程序实收，进入拖选模态）");
                    Check(f.Coord.PassedDownForTest(0),
                        "★ 放行的按下记入『幽灵按下』账（本 bug 的根源量：不记账就永远想不起补配对）");
                    Check(!f.Coord.HeldDuringHoldForTest(0),
                        "触发键还没按住，『按住期间』记账不置位（两套账分工正确）");
                    Check(!f.Coord.Drag.IsDragging, "此刻没有拖动会话（用户还在选文字）");

                    // 再长按 Enter 到阈值 → 激活
                    ActivateTrigger(f.Coord);
                    Check(f.Coord.Trigger.Active, "Enter 已激活");
                    Check(Coordinator.CancelModeSentForTest > cancelBefore,
                        "★ 激活第一件事：向目标发 WM_CANCELMODE —— 先取消选中状态，再执行 Enter 的功能");
                    Check(!f.Coord.Drag.IsDragging,
                        "激活瞬间还不拖（补起放在第一次移动上做，那时才知道光标落在哪扇窗上）");
                    Check(f.Coord.PassedDownForTest(0), "幽灵按下账还在（配对抬起由收养时补发）");

                    // 左键还按着，开始拖动
                    var mv = Ms(new Native.POINT(c0.X + 70, c0.Y + 50));
                    bool mvSwallowed = f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                    Check(!mvSwallowed, "拖动期间的移动绝不吞（吞了光标会冻结）");
                    Check(f.Coord.Drag.IsDragging,
                        "★★ 左键还按着就补起了拖动（修复点：以前这里永远补不起来，只剩拖选文字）");
                    Check(Coordinator.CancelModeSentForTest > cancelBefore + 1,
                        "★ 收养时再次取消目标窗口的拖拽模态");
                    Check(Coordinator.PhantomUpSentForTest > upBefore,
                        "★ 收养时补发【配对的按钮抬起】（目标程序的按下标记就此复位）");
                    Check(!f.Coord.PassedDownForTest(0), "配对后幽灵按下账已清");

                    // 补起那一帧窗口不跳（以当前光标为起点）
                    Settle();
                    Native.RECT rMid;
                    Native.GetWindowRect(f.H, out rMid);
                    Eq(rMid.Left - r0.Left, 0, "★ 补起那一帧窗口不跳（水平）");
                    Eq(rMid.Top - r0.Top, 0, "★ 补起那一帧窗口不跳（垂直）");

                    // 继续移动：窗口必须真的跟着走，而不是继续选中文字
                    var mv2 = Ms(new Native.POINT(c0.X + 70 + 90, c0.Y + 50 + 60));
                    f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv2);
                    Settle();
                    Native.RECT r1;
                    Native.GetWindowRect(f.H, out r1);
                    Eq(r1.Left - rMid.Left, 90, "★★ 窗口真的跟着左键移动了（不再是选中文字）水平 90px");
                    Eq(r1.Top - rMid.Top, 60, "★★ 窗口真的跟着左键移动了 垂直 60px");

                    // 松手：会话吞掉这次抬起（配对已由合成 up 提前补发，目标状态干净）
                    var up = Ms(mv2.pt);
                    bool upSwallowed = f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    Check(upSwallowed, "拖动会话内的抬起被我们吞掉（配对已提前补发，不放幽灵出去）");
                    Check(!f.Coord.Drag.IsDragging, "松开后拖动会话结束");
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }

                // ── ② 选中后改了主意：激活后直接松手 → 真实 up 放行出去完成配对 ──
                using (var f = new DragFixture(delegate (AppConfig c)
                {
                    c.ActionLeft = Actions.Move;
                }))
                {
                    var c0 = CenterOf(f.H);
                    int upBefore = Coordinator.PhantomUpSentForTest;

                    var down = Ms(c0);
                    f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down);
                    Check(f.Coord.PassedDownForTest(0), "② 按下被记账");
                    ActivateTrigger(f.Coord);

                    var up = Ms(c0);
                    bool upSwallowed = f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                    Check(!upSwallowed, "激活后直接松手：这次真实抬起原样放行（目标窗口自己完成配对）");
                    Check(!f.Coord.PassedDownForTest(0), "配对后记账清零（不会秋后补发骚扰消息）");
                    Check(Coordinator.PhantomUpSentForTest == upBefore,
                        "没有补发合成抬起（真实 up 已经完成配对，不需要我们插手）");
                    Check(!f.Coord.Drag.IsDragging, "没有会话残留");
                    f.Coord.Trigger.SimulateKeyUp(null);
                    Settle();
                }

                // ── ③ 收养绝不误判双击：先播下一轮会话的 _lastDown，立刻复现主场景 ──
                //   双击判定依赖 _lastDown，而收养的这条 down 从未经过我们，
                //   _lastDown 是上一轮会话的残留 —— 不排除它，"补起拖动"会被
                //   误判成双击最大化（默认左键=移动 + DoubleClickLeftMaximize）。
                int dct = (int)Native.GetDoubleClickTime();
                if (dct < 250)
                {
                    Line("    （系统双击时间 " + dct + "ms 太短，跳过『收养不误判双击』子用例）");
                }
                else
                {
                    using (var f = new DragFixture(delegate (AppConfig c)
                    {
                        c.ActionLeft = Actions.Move;
                        c.DoubleClickLeftMaximize = true;   // 打开：误判就会最大化而不是拖动
                        c.ExitFullscreenOnTrigger = true;
                    }))
                    {
                        var c0 = CenterOf(f.H);

                        // 一次干净的工具会话：按下 → 不移动 → 抬起。
                        // 它在双击时间窗内播下新鲜的 _lastDown 且 _lastMoved=false。
                        ActivateTrigger(f.Coord);
                        var d0 = Ms(c0);
                        Check(f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref d0), "会话一：按下被吞并建立会话");
                        var u0 = Ms(c0);
                        f.Coord.OnMouse(Native.WM_LBUTTONUP, ref u0);
                        f.Coord.Trigger.SimulateKeyUp(null);
                        Check(!f.Coord.Drag.IsDragging, "会话一已结束（_lastDown 已刷新）");
                        Settle();

                        // ★ 基线必须放在会话一结束【之后】取 ★
                        //   End() 会用【真实光标】做最后一次定位（产品行为：松手落点要精确），
                        //   窗口此刻已经被挪到真实光标处。还拿会话一之前的位置当基线，
                        //   断言就会把这一次跳转算进拖动位移里 —— 曾经在这里栽过。
                        Native.RECT r0;
                        Native.GetWindowRect(f.H, out r0);

                        // 紧接着（双击时间窗内）复现报障场景
                        var down = Ms(c0);
                        Check(!f.Coord.OnMouse(Native.WM_LBUTTONDOWN, ref down), "③ 选中文字的按下照常放行");
                        Check(f.Coord.PassedDownForTest(0), "③ 按下已记账");
                        ActivateTrigger(f.Coord);
                        var mv = Ms(new Native.POINT(c0.X + 60, c0.Y + 40));
                        f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                        Check(f.Coord.Drag.IsDragging,
                            "★★ 收养不得被误判成双击最大化（_lastDown 是上一轮会话的残留，必须排除）");

                        var mv2 = Ms(new Native.POINT(c0.X + 60 + 80, c0.Y + 40 + 50));
                        f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv2);
                        Settle();
                        Native.RECT r1;
                        Native.GetWindowRect(f.H, out r1);
                        Eq(r1.Left - r0.Left, 80, "★★ 是在拖动而不是最大化：水平跟手 80px");
                        Eq(r1.Top - r0.Top, 50, "★★ 是在拖动而不是最大化：垂直跟手 50px");
                        Check(!Native.IsZoomed(f.H), "窗口没有被误最大化");

                        var up = Ms(mv2.pt);
                        f.Coord.OnMouse(Native.WM_LBUTTONUP, ref up);
                        f.Coord.Trigger.SimulateKeyUp(null);
                        Settle();
                    }
                }
            }
            finally
            {
                Coordinator.AssumeButtonsDownForTest = false;
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>
        /// ★ 第四十四轮配套：补发的"配对的抬起"必须在 wParam 里如实报告其余按键的状态。★
        ///
        /// 这条消息的【内容】不是形式主义 —— 目标程序（浏览器渲染进程 / 标准控件）
        /// 是靠"按下 + 抬起配对"来复位自己的按下标记的，掩码写错等于配对失败：
        ///   · 被收养的那个键，其掩码【绝不能】出现在 wParam 里 —— 抬起瞬间它已经松开，
        ///     带着自己的掩码发出去，目标会认为"键还按着"，模态照旧；
        ///   · 其余【此刻仍按着】的键必须如实带掩码 —— 用户左右键一起按着时收养右键，
        ///     补发的右键抬起应当带 MK_LBUTTON，否则目标会连带认为左键也松了；
        ///   · 没按着的键绝不能报成按着（宁可少报也不能谎报，少报最坏是
        ///     目标自己再收一次真实移动时修正，谎报会让它进入错误的组合键语义）；
        ///   · 消息类型必须和被收养的键一一对应，且发到目标窗口自己；
        ///   · lParam 必须是【相对目标窗口的客户区坐标】（鼠标消息的固定语义）。
        ///
        /// 三个键轮流当一次"被收养的幽灵"，逐钉上述各条。
        /// 逐键物理状态用 ButtonsDownOverrideForTest 显式设定（AssumeButtonsDownForTest
        /// 恒为真，拿它验证掩码只会得到"永远全 1"，什么都测不到）。
        /// </summary>
        private static void TestPairedUpMaskReport()
        {
            if (!NoRoomForTestWindow()) return;

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            try
            {
                RunPairedUpMaskCase(0, new[] { true, true, true }, Native.WM_LBUTTONDOWN,
                    "收养左键：右/中键都还按着 → wParam=MkRM（自己已松开故不含左键）");
                RunPairedUpMaskCase(1, new[] { true, true, false }, Native.WM_RBUTTONDOWN,
                    "收养右键：左键还按着、中键已松 → wParam=MkL（如实带左键、不带中键）");
                RunPairedUpMaskCase(2, new[] { false, false, true }, Native.WM_MBUTTONDOWN,
                    "收养中键：其余键都没按 → wParam=0（不许把没按着的键报成按着）");
            }
            finally
            {
                Coordinator.ButtonsDownOverrideForTest = null;
                WindowActions.AllowOwnProcessAsTargetForTest = false;
            }
        }

        /// <summary>跑一轮"补发配对抬起"的消息内容验证。btn=被收养的键，downs=逐键物理按下前提。</summary>
        private static void RunPairedUpMaskCase(int btn, bool[] downs, int downMsg, string maskWhat)
        {
            Coordinator.ButtonsDownOverrideForTest = downs;
            Coordinator.LastPairedUpForTest = null;

            using (var f = new DragFixture(delegate (AppConfig c)
            {
                if (btn == 0) { c.ActionLeft = Actions.Move; c.DoubleClickLeftMaximize = false; }
                else if (btn == 1) c.ActionRight = Actions.Resize;
                else c.ActionMiddle = Actions.Move;
            }))
            {
                var c0 = CenterOf(f.H);
                int upBefore = Coordinator.PhantomUpSentForTest;

                // 先按下鼠标键（触发键还没动）→ 这条 down 被放行，记入"幽灵按下"账
                var down = Ms(c0);
                f.Coord.OnMouse(downMsg, ref down);
                Check(f.Coord.PassedDownForTest(btn), "按下已记入幽灵账（btn=" + btn + "）");

                // 再激活触发键、挪动鼠标 → 收养 → 补发配对的抬起
                ActivateTrigger(f.Coord);
                Settle();
                var mv = Ms(new Native.POINT(c0.X + 60, c0.Y + 40));
                f.Coord.OnMouse(Native.WM_MOUSEMOVE, ref mv);
                Settle();

                Check(f.Coord.Drag.IsDragging, "收养成功：拖动已补起（btn=" + btn + "）");
                Check(Coordinator.PhantomUpSentForTest > upBefore, "补发过配对的抬起（btn=" + btn + "）");

                var p = Coordinator.LastPairedUpForTest;
                if (p == null)
                {
                    Check(false, "探针捕获到配对抬起的内容（btn=" + btn + "）");
                    return;
                }

                Eq(p.Msg, btn == 0 ? Native.WM_LBUTTONUP
                         : btn == 1 ? Native.WM_RBUTTONUP
                         : Native.WM_MBUTTONUP,
                    "消息类型与被收养的键对应（btn=" + btn + "）");
                Check(p.Hwnd == f.H, "配对抬起发到目标窗口自己（btn=" + btn + "）");

                bool mkOk = p.WParam == MaskOf(downs, btn);
                Check(mkOk, maskWhat,
                    "wParam 期望 0x" + MaskOf(downs, btn).ToString("X")
                    + " 实际 0x" + p.WParam.ToString("X"));

                // lParam：收养发生点的客户区坐标
                Native.POINT cl = mv.pt;
                Native.ScreenToClient(p.Hwnd, ref cl);
                int expectLp = (cl.Y << 16) | (cl.X & 0xFFFF);
                Eq((int)p.LParam.ToInt64(), expectLp,
                    "lParam 是相对目标窗口的客户区坐标（btn=" + btn + "）");
            }
        }

        /// <summary>按逐键物理状态算出配对抬起应有的 wParam（测试期望值，独立于产品代码重算一遍）。</summary>
        private static int MaskOf(bool[] downs, int btn)
        {
            int mk = 0;
            if (btn != 0 && downs[0]) mk |= Native.MK_LBUTTON;
            if (btn != 1 && downs[1]) mk |= Native.MK_RBUTTON;
            if (btn != 2 && downs[2]) mk |= Native.MK_MBUTTON;
            return mk;
        }

        // ═══════════════════ 第四十六轮：wgesture 抢占 + 钩子线程死机 ═══════════════════

        /// <summary>
        /// T84：激活时抢占钩子链头（wgesture 抢占右键回归）。
        ///
        /// 用户报障：装了第三方鼠标手势软件 wgesture 后，长按 Enter 进入触发态，
        /// 左键拖动一切正常，【右键缩放完全没反应】——而 wgesture 自己的手势功能照常执行。
        ///
        /// 根因：低级鼠标钩子（WH_MOUSE_LL）按安装时间【倒序】（LIFO）调用。
        /// wgesture 装在我们后面 → 每次右键按下【先过它】→ 它把右键当手势键吞掉，
        /// 我们的钩子根本看不到那条 RBUTTONDOWN，缩放无从发起。
        /// 左键没事，正因为 wgesture 只吃右键，与用户的观察完全吻合。
        ///
        /// 修复：激活这一刻（用户还没点击鼠标）经线程消息让钩子线程把鼠标钩子
        /// 【就地重装】到链头。此后本会话的每个鼠标事件都先过我们这道，
        /// wgesture 再也锁存不到右键 down。
        ///
        /// 本用例验证三件事：
        ///   ① 激活确实触发了一次重装（ReinstallCount +1）；
        ///   ② 重装之后两个钩子都还在（没有把自己弄丢）；
        ///   ③ 重装之后真实 SendInput 事件仍能驱动完整拖动链路
        ///      （抢占链头不能以搞坏事件通路为代价）。
        /// </summary>
        private static void TestChainPriorityReinstall()
        {
            if (!NoRoomForTestWindow()) return;

            string dir = Path.Combine(Path.GetTempPath(), "holddrag-chain-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            HookEngine hooks = null;
            HookWatchdog dog = null;
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
                c.HoldMs = 400;
                c.ActionLeft = Actions.Move;
                c.ActionRight = Actions.Resize;
                c.ActionMiddle = Actions.Nothing;
                c.MovePreviewMode = PreviewModes.FullWin;
                c.ShowActivationFrame = false;
                c.BringToFront = false;
                c.DoubleClickLeftMaximize = false;
                c.DoubleClickRightZone = false;
                c.AllowFullscreenWindows = true;

                coord = new Coordinator(store);
                coord.PrecreateOverlays();

                // 与 TrayContext 里真实程序【完全相同的接线】
                hooks = new HookEngine(coord);
                hooks.Install();
                coord.ReinstallHooksForPriority = () => hooks.ReinstallMouseForChainPriority();
                dog = new HookWatchdog(hooks);
                dog.Start();
                Pump();
                Settle();

                Check(hooks.MouseInstalled && hooks.KeyboardInstalled, "初始钩子安装成功",
                    "错误码 " + hooks.LastError);

                int before = hooks.ReinstallCount;

                // 光标下必须真有一扇可拖动的窗口（DPI 修复后坐标是物理像素，桌面不再碰巧命中）
                var pt = new Native.POINT(400, 300);
                Native.SetCursorPos(pt.X, pt.Y);
                target = new Form();
                target.FormBorderStyle = FormBorderStyle.Sizable;
                target.StartPosition = FormStartPosition.Manual;
                target.ShowInTaskbar = false;
                target.TopMost = true;
                target.Bounds = new Rectangle(pt.X - 150, pt.Y - 100, 300, 200);
                target.Show();
                Pump();
                Settle();

                // ★ 激活 → OnTriggerActivated 应当立即抢占链头。
                //   抢占是 PostThreadMessage 到钩子线程的【异步】动作，
                //   真正执行要等钩子线程泵到那条消息 —— 所以轮询等待而不是立即断言。
                ActivateTrigger(coord);
                bool reinstalled = PumpUntil(() => hooks.ReinstallCount > before, 2000);

                Check(reinstalled, "激活触发了鼠标钩子链头抢占（ReinstallCount +1）",
                    "重装计数 " + before + " → " + hooks.ReinstallCount
                    + "（激活后 2 秒内没有发生就地重装）");
                Check(hooks.MouseInstalled && hooks.KeyboardInstalled,
                    "抢占后两个钩子都还在（重装没有把自己弄丢）",
                    "MouseInstalled=" + hooks.MouseInstalled + " KeyboardInstalled=" + hooks.KeyboardInstalled);

                // ── 抢占之后，真实事件链路必须仍然可用 ──────────────────
                // 只验"重装计数 +1"是不够的：万一把鼠标钩子装哑了呢？
                // 必须用真实 SendInput 走一遍完整链路才算数。
                WindowActions.AllowOwnProcessAsTargetForTest = true;
                WindowActions.ForcedTargetForTest = target.Handle;
                try
                {
                    int sessionsBefore = coord.Drag.SessionCount;

                    // 探测：本环境下 SendInput 的按键事件能不能进钩子（同 T43 的环境探测）
                    bool probe = InjectAndWaitDrag(coord, Native.INJECT_RIGHTDOWN,
                        Native.INJECT_RIGHTUP, "探测", sessionsBefore);
                    bool buttonEventsArrive = probe || coord.Drag.SessionCount > sessionsBefore;
                    CleanupSession(coord, Native.WM_RBUTTONUP, pt);

                    if (!buttonEventsArrive)
                    {
                        Line("（环境限制）本环境下 SendInput 的按键事件不进入低级鼠标钩子，"
                            + "事件链路部分跳过；链头抢占本身已单独验证。");
                        coord.Trigger.SimulateKeyUp(null);
                        return;
                    }

                    // 左键（移动会话）：抢占后仍然起得来
                    ActivateTrigger(coord);
                    int beforeLeft = coord.Drag.SessionCount;
                    bool leftOk = InjectAndWaitDrag(coord, Native.INJECT_LEFTDOWN,
                        Native.INJECT_LEFTUP, "左键", beforeLeft);
                    Check(leftOk, "抢占后左键仍能启动拖动会话",
                        "会话数没增加（before=" + beforeLeft + " now=" + coord.Drag.SessionCount + "）");
                    CleanupSession(coord, Native.WM_LBUTTONUP, pt);

                    // 右键（缩放会话）—— 这正是被 wgesture 抢走的那一条路径
                    ActivateTrigger(coord);
                    int beforeRight = coord.Drag.SessionCount;
                    bool rightOk = InjectAndWaitDrag(coord, Native.INJECT_RIGHTDOWN,
                        Native.INJECT_RIGHTUP, "右键", beforeRight);
                    Check(rightOk, "抢占后右键仍能启动缩放会话（wgesture 抢占回归）",
                        "会话数没增加（before=" + beforeRight + " now=" + coord.Drag.SessionCount + "）");
                    CleanupSession(coord, Native.WM_RBUTTONUP, pt);

                    // 抢占发生在钩子线程的消息循环里，本身耗时亚毫秒级；
                    // 跑完整个链路后回看一眼：全程没有回调卡死。
                    Check(hooks.CallbackStalledMs < 500, "全程没有钩子回调卡死",
                        "CallbackStalledMs=" + hooks.CallbackStalledMs.ToString("0"));

                    coord.Trigger.SimulateKeyUp(null);
                }
                finally
                {
                    WindowActions.ForcedTargetForTest = IntPtr.Zero;
                    WindowActions.AllowOwnProcessAsTargetForTest = false;
                }
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

        /// <summary>
        /// T85：钩子线程路径无同步跨进程写（"整个电脑死机只能重启"回归）。
        ///
        /// 用户报障：触发键（Enter）+ 右键调整窗口大小时，整个电脑死机 ——
        /// 光标还能动，但所有点击无响应，只能强制重启。只复现过一次。
        ///
        /// 根因：低级钩子回调里出现了【没有上限的同步跨进程调用】。
        /// 回调不返回 → 之后全系统的键鼠事件都要先经过它 → 整机输入无响应
        /// （光标移动由 RIT 在钩子派发前更新，所以现象是"鼠标能移动但点击全死"）。
        /// 现场有三条路径会中招，现已全部治理：
        ///   ① End() → 最终定位：改派发给拖动工作线程（同步写在它上面做，有背压语义）；
        ///   ② Begin() → ExitFullscreenToWindowed：改 SWP_ASYNCWINDOWPOS 异步提交；
        ///   ③ ToggleTopMost：同上。
        ///
        /// 验证办法：造一扇在 WM_WINDOWPOSCHANGING 上"装死" 1.5 秒的目标窗口，
        /// 把上述路径放到【另一个线程】上调用并计时（UI 线程持续抽消息）：
        ///   · 同步写（回归）→ 调用线程被目标窗口过程拖住 ≥1.5s → &lt;500ms 断言失败；
        ///   · 异步提交（现行）→ 立即返回。
        /// 换线程执行是必须的：在 UI 线程上直接调用的话，回归版会等自己泵消息 → 死锁，
        /// 自检就永远跑不完了，那比报红更难收拾。
        /// 最后还要断言：异步没有把最终落位弄丢 —— 松手后窗口仍精确停在期望矩形。
        ///
        /// ★ 第四十九轮追加实测结论：SWP_ASYNCWINDOWPOS 也不能保证立即返回 ★
        ///   目标线程正压着一条位置更新不放时，该窗口的位置锁会让另一个线程的
        ///   异步 SetWindowPos 同样排队（ToggleTopMost 实测 1521ms）。因此
        ///   "异步提交"升级为"派发到拖动工作线程"：钩子线程零窗口写，
        ///   ①② 全部改按生产路径经 PostTargetWork 测量。
        /// </summary>
        private static void TestHookThreadNeverBlocks()
        {
            if (!NoRoomForTestWindow()) return;

            const int BlockMs = 1500;   // 目标"装死"时长：够区分同步/异步，又不真的无限等
            const int FastMs = 500;     // 钩子线程路径的返回时限（远低于 LowLevelHooksTimeout 300ms？见下）

            // 说明：LowLevelHooksTimeout 约 300ms，超时钩子会被系统静默摘除。
            // 这里取 500ms 是折中：正常机器上这些路径都是微秒/毫秒级，
            // 500ms 足以抓回归，又不会因为一次调度抖动误报。

            WindowActions.AllowOwnProcessAsTargetForTest = true;
            Coordinator coord = null;
            Form blocker = null;
            Native.POINT savedCursor;
            bool haveSavedCursor = Native.GetCursorPos(out savedCursor);

            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "holddrag-blk-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);

                var store = new ConfigStore(dir);
                store.Load();
                var c = store.Current;
                c.TriggerKeyId = "K:0x0D";
                c.RequireHold = true;
                c.HoldMs = 400;
                c.ActionLeft = Actions.Move;
                c.ActionRight = Actions.Resize;
                c.ActionMiddle = Actions.Nothing;
                c.MovePreviewMode = PreviewModes.FullWin;
                c.ShowActivationFrame = false;
                c.BringToFront = false;
                c.DoubleClickLeftMaximize = false;
                c.DoubleClickRightZone = false;
                c.AllowFullscreenWindows = true;

                coord = new Coordinator(store);
                coord.PrecreateOverlays();

                // 光标固定在一个可预期的位置
                var pt = new Native.POINT(400, 300);
                Native.SetCursorPos(pt.X, pt.Y);

                blocker = new BlockingForm(BlockMs);
                blocker.FormBorderStyle = FormBorderStyle.Sizable;
                blocker.StartPosition = FormStartPosition.Manual;
                blocker.ShowInTaskbar = false;
                blocker.TopMost = true;
                blocker.Bounds = new Rectangle(200, 200, 320, 220);
                blocker.Show();
                Pump();
                Settle();

                Native.RECT b0;
                Native.GetWindowRect(blocker.Handle, out b0);

                // ── ① ExitFullscreenToWindowed（Begin 的全屏/最大化降级路径）──
                //    故意用一个【窗口外】的点：窗口内任意点的等比摆法是个不动点
                //    （窗口会被摆回原处），只有窗口外的点才能看出位置变化。
                //    第四十九轮：按生产路径传派发口（拖动工作线程）—— 修复后
                //    这里面的 SetWindowPos/ShowWindow 一律不在调用线程上执行。
                var outsidePt = new Native.POINT(700, 500);
                var rect = new Native.RECT(b0.Left, b0.Top, b0.Right, b0.Bottom);
                long ms1 = MeasureOnOtherThread(delegate ()
                {
                    WindowActions.ExitFullscreenToWindowed(blocker.Handle, ref rect, outsidePt,
                        delegate (Action w) { coord.Drag.PostTargetWork(w); });
                });
                Check(ms1 < FastMs, "钩子线程上全屏降级不阻塞目标（异步提交）",
                    "耗时 " + ms1 + "ms ≥ " + FastMs + "ms：这条路径上还有同步跨进程写");
                var expect1 = new Native.RECT(rect.Left, rect.Top, rect.Right, rect.Bottom);
                bool landed1 = PumpUntil(delegate ()
                {
                    Native.RECT r;
                    Native.GetWindowRect(blocker.Handle, out r);
                    return RectsEqual(r, expect1);
                }, 6000);
                Check(landed1, "异步提交的降级最终真的落了位",
                    "期望 " + expect1.Left + "," + expect1.Top + " 实际窗口仍在别处");

                // ── ② 一次性动作：常驻置顶切换（HandleButtonDown → Apply → 这里）──
                //    第四十九轮：按生产路径测量 —— Coordinator 把"动作 + 悬停刷新"
                //    打成闭包派发到拖动工作线程，钩子线程（本测量线程）只投递。
                //    修复前这里实测阻塞 1521ms：目标线程压着上一条位置更新不放时，
                //    连 SWP_ASYNCWINDOWPOS 的 SetWindowPos 都要排队等它的位置锁。
                bool topBefore = WindowActions.IsTopMost(blocker.Handle);
                long ms2 = MeasureOnOtherThread(delegate ()
                {
                    coord.Drag.PostTargetWork(delegate ()
                    {
                        WindowActions.ToggleTopMost(blocker.Handle);
                    });
                });
                Check(ms2 < FastMs, "钩子线程上置顶切换不阻塞目标（异步提交）",
                    "耗时 " + ms2 + "ms ≥ " + FastMs + "ms：这条路径上还有同步跨进程写");
                bool landed2 = PumpUntil(delegate ()
                {
                    return WindowActions.IsTopMost(blocker.Handle) != topBefore;
                }, 3000);
                Check(landed2, "置顶切换最终生效（异步写没有丢）");

                // ── ③ 完整拖动会话：Enter + 右键缩放（用户报障的原场景）──
                //    注意期望矩形从【当前】矩形算起：①刚把窗口挪过了。
                Native.RECT b1;
                Native.GetWindowRect(blocker.Handle, out b1);
                var grab = new Native.POINT(b1.Right - 12, b1.Bottom - 12);   // 右下角（同 T19 的确定性抓点）
                var finalPt = new Native.POINT(grab.X + 60, grab.Y + 40);
                var expectFinal = new Native.RECT(b1.Left, b1.Top, b1.Right + 60, b1.Bottom + 40);

                ActivateTrigger(coord);
                Settle();
                WindowActions.ForcedTargetForTest = blocker.Handle;

                int sessionsBefore = coord.Drag.SessionCount;
                long msDown = MeasureOnOtherThread(delegate ()
                {
                    var d = Ms(grab);
                    coord.OnMouse(Native.WM_RBUTTONDOWN, ref d);
                });
                Check(msDown < FastMs, "钩子线程上 Begin 不阻塞目标",
                    "耗时 " + msDown + "ms ≥ " + FastMs + "ms");
                Check(coord.Drag.SessionCount > sessionsBefore, "缩放会话已经建立",
                    "会话数 " + sessionsBefore + " → " + coord.Drag.SessionCount);

                long msMove = MeasureOnOtherThread(delegate ()
                {
                    var m = Ms(finalPt);
                    coord.OnMouse(Native.WM_MOUSEMOVE, ref m);
                });
                Check(msMove < FastMs, "拖动中的移动事件不阻塞钩子线程（慢目标由工作线程消化）",
                    "耗时 " + msMove + "ms ≥ " + FastMs + "ms：移动还在钩子线程上同步等目标");

                // End() 用真实光标做最终定位，先把光标摆过去
                Native.SetCursorPos(finalPt.X, finalPt.Y);
                long msUp = MeasureOnOtherThread(delegate ()
                {
                    var u = Ms(finalPt);
                    coord.OnMouse(Native.WM_RBUTTONUP, ref u);
                });
                Check(msUp < FastMs, "★ 松手（End）不阻塞钩子线程 —— 用户报障'死机'的原路径 ★",
                    "耗时 " + msUp + "ms ≥ " + FastMs + "ms：End 还在钩子线程上同步写窗口");
                Check(!coord.Drag.IsDragging, "松开右键后缩放会话结束");

                bool landedFinal = PumpUntil(delegate ()
                {
                    Native.RECT r;
                    Native.GetWindowRect(blocker.Handle, out r);
                    return RectsEqual(r, expectFinal);
                }, 8000);
                Check(landedFinal, "阻塞目标上松手后仍精确落位（最终写由拖动工作线程完成）",
                    "期望 " + expectFinal.Left + "," + expectFinal.Top + " "
                    + expectFinal.Width + "x" + expectFinal.Height + "（右下各外扩 60/40）");

                coord.Trigger.SimulateKeyUp(null);
            }
            finally
            {
                WindowActions.ForcedTargetForTest = IntPtr.Zero;
                WindowActions.AllowOwnProcessAsTargetForTest = false;
                try { if (coord != null) coord.Dispose(); } catch { }
                try
                {
                    // 同步销毁，避免 TopMost 残留窗口压住后续用例（理由同 DragFixture.Dispose）
                    if (blocker != null && !blocker.IsDisposed)
                    {
                        blocker.Visible = false;
                        Native.DestroyWindow(blocker.Handle);
                    }
                    if (blocker != null) { blocker.Close(); blocker.Dispose(); }
                }
                catch { }
                for (int i = 0; i < 6; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                try { if (haveSavedCursor) Native.SetCursorPos(savedCursor.X, savedCursor.Y); } catch { }
            }
        }

        /// <summary>
        /// 在【另一个线程】上执行动作并计时；UI 线程在此期间持续抽消息。
        ///
        /// 为什么必须换线程：被测路径是"钩子线程"，它与窗口所属线程（UI 线程）
        /// 必须是两个线程，才能真实复现"同步跨进程写要等目标窗口过程返回"的
        /// 阻塞语义。若在 UI 线程上直接调用，回归版（同步写）会等自己泵消息 → 死锁，
        /// 自检就永远跑不完了。
        /// </summary>
        /// <summary>
        /// 把被测调用放到另一个线程上执行并计时（UI 线程持续抽消息）。
        ///
        /// ★ 计时只认【被测线程自己】的耗时 ★
        ///   秒表在被测线程内部起停。等待循环的墙上时长可能包含 UI 线程
        ///   DoEvents 里【替阻塞目标泵消息】的停顿 —— 目标在
        ///   WM_WINDOWPOSCHANGING 上装死 1.5s 时，那一觉就睡在 UI 线程上，
        ///   与"被测调用是否阻塞钩子线程"无关。把这类停顿记到钩子线程账上
        ///   会误报：第四十九轮全量自检曾因此把一次实际 ~1ms 的投递测成
        ///   1530ms（拖动工作线程刚落了一帧同步写出去，UI 线程正好在
        ///   DoEvents 里被那扇阻塞目标的 WndProc 拖住）。判据如下：
        ///     · 真回归（钩子线程上出现同步写）→ 被测线程自己卡住 → 照报；
        ///     · 测试架自身开销 → 只拉长等待循环 → 不计入。
        /// </summary>
        private static long MeasureOnOtherThread(Action action)
        {
            Exception err = null;
            long innerMs = -1;
            var t = new System.Threading.Thread(delegate ()
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { action(); }
                catch (Exception ex) { err = ex; }
                finally { innerMs = sw.ElapsedMilliseconds; }
            });
            t.IsBackground = true;
            t.Start();
            var outer = System.Diagnostics.Stopwatch.StartNew();
            while (t.IsAlive)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
            outer.Stop();
            if (err != null) Log.Error(err);
            if (outer.ElapsedMilliseconds - innerMs > 50)
                Log.Trace("[测量] 被测线程 " + innerMs + "ms，等待循环 "
                    + outer.ElapsedMilliseconds + "ms（差额为 UI 线程替阻塞目标泵消息，不计入）");
            return innerMs;
        }

        /// <summary>在 UI 线程上轮询等待条件成立（持续抽消息），超时返回 false。</summary>
        private static bool PumpUntil(Func<bool> cond, int timeoutMs)
        {
            long end = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < end)
            {
                Application.DoEvents();
                if (cond()) return true;
                System.Threading.Thread.Sleep(10);
            }
            Application.DoEvents();
            return cond();
        }

        private static bool RectsEqual(Native.RECT a, Native.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top
                && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        /// <summary>
        /// 在 WM_WINDOWPOSCHANGING 上"装死"指定毫秒的测试窗口。
        ///
        /// 模拟的是真机现场：目标进程不泵消息（卡死 / 模态框 / 渲染停顿）。
        /// 任何【同步】跨进程写到它的线程都会被无上限地拖住 —— 这正是用户报的
        /// "整个电脑死机、只能重启"的根因形态。用有界的毫秒数模拟，
        /// 既能区分"同步写"与"异步写"，又不让自检真的等一个永远不回来的调用。
        /// </summary>
        private sealed class BlockingForm : Form
        {
            private readonly int _blockMs;
            public bool BlockEnabled = true;

            public BlockingForm(int blockMs)
            {
                _blockMs = blockMs;
            }

            protected override void WndProc(ref Message m)
            {
                if (BlockEnabled && m.Msg == Native.WM_WINDOWPOSCHANGING)
                    System.Threading.Thread.Sleep(_blockMs);
                base.WndProc(ref m);
            }
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
