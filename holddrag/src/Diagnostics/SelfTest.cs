// HoldDrag · Diagnostics/SelfTest.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HoldDrag;

internal static partial class SelfTest
{
	private sealed class NullSink : IInputSink
	{
		public bool OnKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb)
		{
			return false;
		}

		public bool OnMouse(int msg, ref Native.MSLLHOOKSTRUCT ms)
		{
			return false;
		}
	}

	private sealed class DragFixture : IDisposable
	{
		private readonly string _dir;

		public readonly Form Window;

		public readonly ConfigStore Store;

		public readonly Coordinator Coord;

		public nint H => Window.Handle;

		public DragFixture(Action<AppConfig> tweak)
		{
			_dir = Path.Combine(Path.GetTempPath(), "holddrag-e2e-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
			Store = new ConfigStore(_dir);
			Store.Load();
			AppConfig current = Store.Current;
			current.BringToFront = false;
			current.ShowActivationFrame = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			tweak?.Invoke(current);
			Window = new Form();
			Window.FormBorderStyle = FormBorderStyle.Sizable;
			Window.StartPosition = FormStartPosition.Manual;
			Window.ShowInTaskbar = false;
			Window.Text = "HoldDrag selftest";
			Window.TopMost = true;
			Window.Bounds = new Rectangle(120, 120, 320, 220);
			Window.Show();
			Pump();
			Coord = new Coordinator(Store);
			Coord.PrecreateOverlays();
			Pump();
			WindowActions.ForcedTargetForTest = Window.Handle;
		}

		public void Dispose()
		{
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			try
			{
				Coord.Dispose();
			}
			catch
			{
			}
			try
			{
				try
				{
					if (!Window.IsDisposed)
					{
						Window.Visible = false;
						Native.DestroyWindow(Window.Handle);
					}
				}
				catch
				{
				}
				Window.Close();
				Window.Dispose();
			}
			catch
			{
			}
			for (int i = 0; i < 6; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			try
			{
				Directory.Delete(_dir, recursive: true);
			}
			catch
			{
			}
		}
	}

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
			if (BlockEnabled && m.Msg == 70)
			{
				Thread.Sleep(_blockMs);
			}
			base.WndProc(ref m);
		}
	}

	private static int _pass;

	private static int _fail;

	private static readonly StringBuilder Buf = new StringBuilder();

	private const uint AttachParentProcess = uint.MaxValue;

	private static readonly Stopwatch _watch = new Stopwatch();

	private static string _reportDir;

	private static int OwnProcessIdForTest
	{
		get
		{
			try
			{
				return Process.GetCurrentProcess().Id;
			}
			catch
			{
				return -1;
			}
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool AttachConsole(uint dwProcessId);

	/// <summary>给 Program 的控制台诊断输出复用（AttachConsole 是 private 的）。</summary>
	internal static void AttachConsoleForTest(uint processId)
	{
		AttachConsole(processId);
	}

	public static void Run(string dir)
	{
		try
		{
			AttachConsole(uint.MaxValue);
		}
		catch
		{
		}
		_only = Environment.GetEnvironmentVariable("HOLDDRAG_SELFTEST_ONLY");
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
		Safe("T10 配置往返", () =>
		{
			TestConfigRoundTrip(dir);
		});
		Safe("T11 配置备份恢复", () =>
		{
			TestConfigRecovery(dir);
		});
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
		Safe("T52 视觉反馈组几何：窄/宽各档下样式预览框必须留在分组内且不被遮", TestVisualGroupGeometry);
		Safe("T53 晃窗手势：真晃才触发、匀速/小幅抖动/拖太慢都不触发（#39）", TestShakeGesture);
		Safe("T54 松开触发键后外框立即消失，pin 机制已下线（外框残留）", TestFrameGoesAwayOnRelease);
		Safe("T55 外框调色板/样式已扩充且每一种都画得出来（#37）", TestFramePaletteExpanded);
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
		Safe("T86 自有窗口内控件不被抢输入：滑块上放行、标题栏上仍可拖", TestOwnInteractiveControlPassthrough);
		Safe("T87 分组框不是可交互控件：按住触发键时设置页的成片分组区仍可整体拖走", TestGroupBoxNotInteractive);
		Safe("T88 阈值内按下 + 激活前已在移动：激活后仍必须补起拖动（用户『外框出现却拖不动』复现）", TestAdoptAfterEarlyMove);
		Safe("T89 关于页 GitHub 链接 + 自动更新开关 + 检查更新按钮（v1.1 更新功能）", TestUpdateFeatures);
		Safe("T90 更新重启的单实例竞态：新版本必须等旧进程释放互斥体再接管", TestUpdateRestartSingleInstance);
		Safe("T91 单实例：同一时间只能开一个（重复启动必须自己退出并唤起已有窗口）", TestSingleInstanceOnlyOne);
		Safe("T92 外框颜色：跟随系统强调色 / 自定义颜色都要真的生效且存得进配置", TestFrameColorAccentAndCustom);
		Safe("T93 设置页跟随系统深浅色：深浅两套都要够清楚（对比度/层次）", TestSettingsThemeFollowsSystem);
		Safe("T94 设置窗口是无边框圆角卡片窗：无系统标题栏、标题条与页面同色、三个窗口按钮在位", TestFramelessCardWindow);
		Safe("T96 设置页重排性能：换宽度一次 < 60ms、同宽度重复调用被去重", TestSettingsRelayoutPerformance);
		Safe("T97 侧栏高亮只跟着当前页（点过就留胶囊的回归）", TestNavHighlightFollowsSelection);
		Safe("T95 圆角参数必须真的改变渲染结果（圆角设置失效回归）", TestFrameRadiusTakesEffect);
		Safe("T98 录制守卫与配置不变量：\"K:0x00\" 必被 Normalize 兜底、鼠标侧键/Shift 不被误伤、QuoteArg 按需包引号", TestRecordedKeyAndConfigInvariants);
		Safe("T99 保存失败不再静默：Save/Apply 返回 false 并带回错误信息（设置页据此红字早退）", TestConfigSaveFailureIsVisible);
		Safe("T100 GitHub 令牌：整理/鉴权头/失效与限额判定/配置往返/无令牌只提示不下载", TestGitHubTokenHandling);
		Safe("T101 点按切换模式：点一下抓住、再点一下放下，出厂默认仍是长按", TestToggleMode);
		Safe("T102 合成按键欠账：注入过的「只按下」必须在抬起丢失时被补发，绝不留下系统级卡键", TestInjectedDownLedger);
		Line("");
		Line("合计 " + (_pass + _fail) + " 项：通过 " + _pass + "，失败 " + _fail);
		string text = Buf.ToString();
		try
		{
			File.WriteAllText(Path.Combine(dir, "selftest.txt"), text, Encoding.UTF8);
		}
		catch
		{
		}
		ConsoleEcho(text);
		StressTest.HardExit((_fail != 0) ? 1 : 0);
	}

	private static void ConsoleEcho(string text)
	{
		try
		{
			Thread thread = new Thread(() =>
			{
				try
				{
					Console.Out.Write(text);
					Console.Out.Flush();
				}
				catch
				{
				}
			});
			thread.IsBackground = true;
			thread.Name = "HoldDrag.SelfTestEcho";
			thread.Start();
			thread.Join(1500);
		}
		catch
		{
		}
	}
















	private static void TestIconEmbedded()
	{
		try
		{
			string exePath = AppPaths.ExePath;
			Line("  宿主 exe：" + exePath);
			using Icon icon = Icon.ExtractAssociatedIcon(exePath);
			Check(icon != null, "能从 exe 中提取图标（说明 app.ico 已内嵌）");
		}
		catch (Exception ex)
		{
			Bad("exe 内嵌图标可提取", ex.Message);
		}
	}

	private static void Pump()
	{
		Application.DoEvents();
	}

	private static Native.RECT WorkAreaOfNearestMonitor(Native.POINT p)
	{
		try
		{
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(p);
			if (monitorInfoAt.rcWork.Width > 0 && monitorInfoAt.rcWork.Height > 0)
			{
				return monitorInfoAt.rcWork;
			}
		}
		catch
		{
		}
		try
		{
			Rectangle workingArea = Screen.FromPoint(new Point(p.X, p.Y)).WorkingArea;
			return new Native.RECT(workingArea.Left, workingArea.Top, workingArea.Right, workingArea.Bottom);
		}
		catch
		{
		}
		return new Native.RECT(0, 0, 1920, 1080);
	}

	private static string DescribeWindow(nint h)
	{
		if (h == IntPtr.Zero)
		{
			return "(空句柄)";
		}
		try
		{
			nint num = Native.GetAncestor(h, 2u);
			if (num == IntPtr.Zero)
			{
				num = h;
			}
			Native.GetWindowThreadProcessId(num, out var lpdwProcessId);
			return ((IntPtr)num).ToInt64().ToString("X") + " \"" + Native.GetWindowTitle(num) + "\" 类=" + Native.GetClassName(num) + " 进程=" + lpdwProcessId + ((lpdwProcessId == (uint)OwnProcessIdForTest) ? "(自己)" : "(外部)");
		}
		catch (Exception ex)
		{
			return ((IntPtr)h).ToInt64().ToString("X") + " (描述失败:" + ex.Message + ")";
		}
	}

	private static void Settle()
	{
		long num = Environment.TickCount64 + 80;
		while (Environment.TickCount64 < num)
		{
			Application.DoEvents();
			Thread.Sleep(5);
		}
	}

	private static void SettleLong()
	{
		long num = Environment.TickCount64 + 500;
		while (Environment.TickCount64 < num)
		{
			Application.DoEvents();
			Thread.Sleep(5);
		}
	}

	private static Native.MSLLHOOKSTRUCT Ms(Native.POINT p)
	{
		return new Native.MSLLHOOKSTRUCT
		{
			pt = p
		};
	}

	private static Native.POINT CenterOf(nint h)
	{
		Native.GetWindowRect(h, out var lpRect);
		return new Native.POINT((lpRect.Left + lpRect.Right) / 2, (lpRect.Top + lpRect.Bottom) / 2);
	}

	private static bool PlaceCursorOn(nint hwnd, Native.POINT pt)
	{
		for (int i = 0; i < 8; i++)
		{
			Native.SetCursorPos(pt.X, pt.Y);
			Application.DoEvents();
			Thread.Sleep(20);
			if (Native.GetCursorPos(out var lpPoint) && Math.Abs(lpPoint.X - pt.X) <= 4 && Math.Abs(lpPoint.Y - pt.Y) <= 4 && Native.WindowFromPoint(lpPoint) == hwnd)
			{
				return true;
			}
		}
		return false;
	}

	private static void ActivateTrigger(Coordinator c)
	{
		c.Trigger.SimulateKeyDown();
		c.Trigger.ForceHoldElapsed();
	}

	private static bool NoRoomForTestWindow()
	{
		int systemMetrics = Native.GetSystemMetrics(0);
		int systemMetrics2 = Native.GetSystemMetrics(1);
		if (systemMetrics < 400 || systemMetrics2 < 300)
		{
			Line("  （当前显示区域只有 " + systemMetrics + "x" + systemMetrics2 + "，跳过真实窗口模拟）");
			return false;
		}
		return true;
	}




	private static void TestNoDragWhenInactive()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Check(!dragFixture.Coord.Trigger.Active, "触发键未激活");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			Check(!dragFixture.Coord.OnMouse(513, ref ms), "未激活时左键消息被放行");
			Check(!dragFixture.Coord.Drag.IsDragging, "未激活时不会开始拖动");
			Native.MSLLHOOKSTRUCT ms2 = Ms(CenterOf(dragFixture.H));
			Check(!dragFixture.Coord.OnMouse(516, ref ms2), "未激活时右键消息被放行");
			Check(!dragFixture.Coord.Drag.IsDragging, "未激活时不会开始缩放");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}











	private static string Name(Control c)
	{
		try
		{
			string text = c.Text;
			if (!string.IsNullOrEmpty(text))
			{
				if (text.Length > 10)
				{
					text = text.Substring(0, 10) + "…";
				}
				return c.GetType().Name + "[" + text + "]";
			}
		}
		catch
		{
		}
		return c.GetType().Name;
	}




	private static void Walk(Control root, Action<Control> visit)
	{
		try
		{
			for (int i = 0; i < root.Controls.Count; i++)
			{
				Control control = root.Controls[i];
				visit(control);
				Walk(control, visit);
			}
		}
		catch
		{
		}
	}


	private static Color? CaptureCornerPixel(nint overlay, Native.RECT r, params Color[] expected)
	{
		try
		{
			int num = FrameStyles.MarginFor("Focus", 6);
			int sampleInset = num - (int)Math.Round((double)FrameStyles.StrokeFor("Focus", 6) * 0.5);
			if (sampleInset < 1)
			{
				sampleInset = 1;
			}
			Native.POINT pOINT = new Native.POINT(r.Left + sampleInset, r.Top + sampleInset);
			Native.POINT pOINT2 = CoordSpace.ToPhysical(pOINT);
			Color? result = null;
			int num2 = int.MaxValue;
			Native.POINT[] array = new Native.POINT[2] { pOINT, pOINT2 };
			for (int i = 0; i < array.Length; i++)
			{
				Color? color = ReadScreenPixel(array[i].X, array[i].Y);
				if (!color.HasValue)
				{
					continue;
				}
				if (expected == null || expected.Length == 0)
				{
					return color;
				}
				int num3 = int.MaxValue;
				for (int j = 0; j < expected.Length; j++)
				{
					int num4 = ColorDistance(color.Value, expected[j]);
					if (num4 < num3)
					{
						num3 = num4;
					}
				}
				if (num3 < num2)
				{
					num2 = num3;
					result = color;
				}
			}
			return result;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return null;
		}
	}

	private static Color? ReadScreenPixel(int x, int y)
	{
		nint dC = Native.GetDC(IntPtr.Zero);
		if (dC == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			uint pixel = Native.GetPixel(dC, x, y);
			if (pixel == uint.MaxValue)
			{
				return null;
			}
			int red = (int)(pixel & 0xFF);
			int green = (int)((pixel >> 8) & 0xFF);
			int blue = (int)((pixel >> 16) & 0xFF);
			return Color.FromArgb(red, green, blue);
		}
		finally
		{
			Native.ReleaseDC(IntPtr.Zero, dC);
		}
	}

	private static int ColorDistance(Color a, Color b)
	{
		int num = a.R - b.R;
		int num2 = a.G - b.G;
		int num3 = a.B - b.B;
		return num * num + num2 * num2 + num3 * num3;
	}



	private static int CountInkPixels(nint hwnd)
	{
		try
		{
			if (!Native.GetWindowRect(hwnd, out var lpRect))
			{
				return -1;
			}
			int width = lpRect.Width;
			int height = lpRect.Height;
			if (width <= 0 || height <= 0)
			{
				return -1;
			}
			using Bitmap bitmap = new Bitmap(width, height);
			using Graphics graphics = Graphics.FromImage(bitmap);
			nint hdc = graphics.GetHdc();
			Native.PrintWindow(hwnd, hdc, 0u);
			graphics.ReleaseHdc(hdc);
			int num = 0;
			for (int i = 0; i < height; i += 2)
			{
				for (int j = 0; j < width; j += 2)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (pixel.A > 0 && (pixel.R < 240 || pixel.G < 240 || pixel.B < 240))
					{
						num++;
					}
				}
			}
			return num;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return -2;
		}
	}


	private static void SendRawKey(ushort vk, bool down)
	{
		Native.INPUT[] array = new Native.INPUT[1];
		array[0].type = 1u;
		array[0].u.ki.wVk = vk;
		array[0].u.ki.dwFlags = ((!down) ? 2u : 0u);
		Native.SendInput(1u, array, Marshal.SizeOf(typeof(Native.INPUT)));
	}



	private static void CleanupSession(Coordinator coord, int upMsg, Native.POINT pt)
	{
		try
		{
			if (coord.Drag.IsDragging)
			{
				Native.MSLLHOOKSTRUCT ms = Ms(pt);
				coord.OnMouse(upMsg, ref ms);
			}
			coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		catch
		{
		}
	}


	/// <summary>
	/// 带超时的 SendInput。
	///
	/// 为什么需要：SendInput 要等整条低级钩子链返回才回来。机器上只要有一个第三方钩子程序卡住
	/// （实测：某个鼠标增强工具无响应），注入就永远不返回，自检会以「整进程卡死」收场 ——
	/// 用户看到的是「自检卡在 T43」，实际是环境问题。这里改成后台线程注入 + 超时，
	/// 卡住就放弃并如实报告，不再把整个自检拖死。
	/// </summary>
	private static bool SendInputWithTimeout(Native.INPUT[] inputs, int timeoutMs)
	{
		bool done = false;
		int cbSize = Marshal.SizeOf(typeof(Native.INPUT));
		ThreadPool.QueueUserWorkItem(delegate
		{
			try
			{
				Native.SendInput((uint)inputs.Length, inputs, cbSize);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			finally
			{
				Volatile.Write(ref done, true);
			}
		});
		long num = Environment.TickCount64 + timeoutMs;
		while (!Volatile.Read(ref done) && Environment.TickCount64 < num)
		{
			Application.DoEvents();
			Thread.Sleep(10);
		}
		return Volatile.Read(ref done);
	}

	private static bool InjectAndWaitDrag(Coordinator coord, uint downFlag, uint upFlag, string label, int sessionsBefore)
	{
		try
		{
			int cbSize = Marshal.SizeOf(typeof(Native.INPUT));
			Native.INPUT[] pInputs = new Native.INPUT[1] { MakeMouseInput(downFlag) };
			if (!SendInputWithTimeout(pInputs, 2000))
			{
				Line("    （跳过：注入" + label + "按下 2 秒都没返回 —— 机器上有第三方钩子卡住了，这是环境问题）");
				return false;
			}
			for (int i = 0; i < 30; i++)
			{
				if (coord.Drag.SessionCount > sessionsBefore)
				{
					break;
				}
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Application.DoEvents();
			bool result = coord.Drag.SessionCount > sessionsBefore;
			Native.INPUT[] pInputs2 = new Native.INPUT[1] { MakeMouseInput(upFlag) };
			SendInputWithTimeout(pInputs2, 2000);
			for (int j = 0; j < 5; j++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			return result;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	private static void InjectMouseMoveRelative(int dx, int dy)
	{
		try
		{
			Native.MOUSEINPUT mi = new Native.MOUSEINPUT
			{
				dx = dx,
				dy = dy,
				dwFlags = 1u
			};
			Native.INPUT[] pInputs = new Native.INPUT[1]
			{
				new Native.INPUT
				{
					type = 0u,
					u = new Native.INPUTUNION
					{
						mi = mi
					}
				}
			};
			Native.SendInput(1u, pInputs, Marshal.SizeOf(typeof(Native.INPUT)));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void InjectMouseMoveAbsolute(int x, int y)
	{
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			int num = ((virtualScreen.Width <= 1) ? 1 : (virtualScreen.Width - 1));
			int num2 = ((virtualScreen.Height <= 1) ? 1 : (virtualScreen.Height - 1));
			int num3 = (int)Math.Round((double)(x - virtualScreen.Left) * 65535.0 / (double)num);
			int num4 = (int)Math.Round((double)(y - virtualScreen.Top) * 65535.0 / (double)num2);
			if (num3 < 0)
			{
				num3 = 0;
			}
			if (num3 > 65535)
			{
				num3 = 65535;
			}
			if (num4 < 0)
			{
				num4 = 0;
			}
			if (num4 > 65535)
			{
				num4 = 65535;
			}
			Native.MOUSEINPUT mi = new Native.MOUSEINPUT
			{
				dx = num3,
				dy = num4,
				dwFlags = 49153u
			};
			Native.INPUT[] pInputs = new Native.INPUT[1]
			{
				new Native.INPUT
				{
					type = 0u,
					u = new Native.INPUTUNION
					{
						mi = mi
					}
				}
			};
			Native.SendInput(1u, pInputs, Marshal.SizeOf(typeof(Native.INPUT)));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void InjectMouseButton(uint flag)
	{
		try
		{
			Native.INPUT[] pInputs = new Native.INPUT[1] { MakeMouseInput(flag) };
			Native.SendInput(1u, pInputs, Marshal.SizeOf(typeof(Native.INPUT)));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void ReadCursorAndRect(nint hwnd, out Native.POINT pt, out Native.RECT rect)
	{
		Native.GetWindowRect(hwnd, out var lpRect);
		for (int i = 0; i < 30; i++)
		{
			Application.DoEvents();
			Thread.Sleep(10);
			Native.GetWindowRect(hwnd, out var lpRect2);
			if (lpRect2.Left == lpRect.Left && lpRect2.Top == lpRect.Top && lpRect2.Right == lpRect.Right && lpRect2.Bottom == lpRect.Bottom)
			{
				break;
			}
			lpRect = lpRect2;
		}
		Native.GetCursorPos(out pt);
		Native.GetWindowRect(hwnd, out rect);
	}


	private static Native.INPUT MakeMouseInput(uint flags)
	{
		Native.MOUSEINPUT mi = new Native.MOUSEINPUT
		{
			dwFlags = flags
		};
		return new Native.INPUT
		{
			type = 0u,
			u = new Native.INPUTUNION
			{
				mi = mi
			}
		};
	}





	private static void VerifyBottomBarButtons()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.Size = settingsForm.Size;
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		StringBuilder bad = new StringBuilder();
		int checkedBtn = 0;
		Walk(settingsForm, (Control c) =>
		{
			if (c is Button)
			{
				string text = c.Text;
				if (!(text != "恢复默认值") || !(text != "保存并生效") || !(text != "取消"))
				{
					checkedBtn++;
					Size size;
					try
					{
						size = TextRenderer.MeasureText(c.Text, c.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
					}
					catch
					{
						return;
					}
					if (size.Width > c.Width - 12)
					{
						bad.Append(Name(c)).Append("(文字宽").Append(size.Width)
							.Append(" > 可用宽")
							.Append(c.Width - 12)
							.Append(") ");
					}
					if (size.Height > c.Height - 8)
					{
						bad.Append(Name(c)).Append("(文字高").Append(size.Height)
							.Append(" > 可用高")
							.Append(c.Height - 8)
							.Append(") ");
					}
				}
			}
		});
		Check(checkedBtn == 3, "底栏确实有 3 个按钮被检查到", "实际找到 " + checkedBtn + " 个");
		Check(bad.Length == 0, "★ 底栏按钮的文字与按钮框适配（恢复默认值/保存并生效/取消）", (bad.Length == 0) ? (checkedBtn + " 个按钮文字都装得下且居中") : ("异常: " + bad));
		Control control = FindByText(settingsForm, "保存并生效");
		ComboBox comboBox = FindFirstCombo(settingsForm);
		if (control != null && comboBox != null)
		{
			int num = Math.Abs(control.Height - comboBox.Height);
			Check(num <= 8, "底栏按钮高度与输入框控件同一量级（视觉不突兀）", "按钮高 " + control.Height + " 输入框高 " + comboBox.Height + " 差 " + num);
		}
		settingsForm.Close();
	}

	private static Control FindByText(Control root, string text)
	{
		try
		{
			for (int i = 0; i < root.Controls.Count; i++)
			{
				Control control = root.Controls[i];
				if (string.Equals(control.Text, text, StringComparison.Ordinal))
				{
					return control;
				}
				Control control2 = FindByText(control, text);
				if (control2 != null)
				{
					return control2;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static ComboBox FindFirstCombo(Control root)
	{
		try
		{
			for (int i = 0; i < root.Controls.Count; i++)
			{
				if (root.Controls[i] is ComboBox result)
				{
					return result;
				}
				ComboBox comboBox = FindFirstCombo(root.Controls[i]);
				if (comboBox != null)
				{
					return comboBox;
				}
			}
		}
		catch
		{
		}
		return null;
	}



	private static void TestRemovedFeaturesGone()
	{
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		bool flag4 = false;
		bool flag5 = false;
		bool flag6 = false;
		bool flag7 = false;
		PropertyInfo[] properties = typeof(AppConfig).GetProperties();
		foreach (PropertyInfo obj in properties)
		{
			if (obj.Name == "DragTransparency")
			{
				flag = true;
			}
			if (obj.Name == "DragSizeHint")
			{
				flag2 = true;
			}
			if (obj.Name == "EdgeSnap")
			{
				flag3 = true;
			}
			if (obj.Name == "SnapToOtherWindows")
			{
				flag4 = true;
			}
			if (obj.Name == "SnapPreview")
			{
				flag5 = true;
			}
			if (obj.Name == "SnapDistance")
			{
				flag6 = true;
			}
			if (obj.Name == "MovePreviewMode")
			{
				flag7 = true;
			}
		}
		Check(!flag, "AppConfig 里已无 DragTransparency");
		Check(!flag2, "AppConfig 里已无 DragSizeHint");
		Check(!flag3, "AppConfig 里已无 EdgeSnap（#32 自动贴边整体删除）");
		Check(!flag4, "AppConfig 里已无 SnapToOtherWindows（#32 与其它窗口对齐整体删除）");
		Check(!flag5, "AppConfig 里已无 SnapPreview（#32 边缘预览落位整体删除）");
		Check(!flag6, "AppConfig 里已无 SnapDistance（#32 吸附距离整体删除）");
		Check(!flag7, "AppConfig 里已无 MovePreviewMode（v1.6.1 删除 Outline 预览模式：无 UI 入口且被 Normalize 钉死 FullWin，生产不可达）");
		ConfigStore configStore = new ConfigStore(Path.Combine(Path.GetTempPath(), "holddrag-legacy-" + Guid.NewGuid().ToString("N")));
		Directory.CreateDirectory(configStore.Dir);
		File.WriteAllText(configStore.FilePath, "{\r\n  \"TriggerKeyId\": \"K:0xA4\",\r\n  \"DragTransparency\": true,\r\n  \"DragTransparencyAlpha\": 70,\r\n  \"DragSizeHint\": true,\r\n  \"EdgeSnap\": true,\r\n  \"SnapToOtherWindows\": true,\r\n  \"SnapPreview\": true,\r\n  \"SnapDistance\": 42\r\n}", Encoding.UTF8);
		try
		{
			configStore.Load();
			Eq(configStore.Current.TriggerKeyId, "K:0xA4", "旧配置里的有效字段仍然读得出来");
		}
		finally
		{
			try
			{
				Directory.Delete(configStore.Dir, recursive: true);
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// v1.6.1 Must #1 / Must #3 的回归：
	/// #1 录制纯修饰键时 KeyCode 掩码把修饰位掩掉后是 Keys.None，历史 bug 会绑成 "K:0x00"
	///    （vk=0 永远匹配不上任何真实按键，触发键就此失效）；设置页已加守卫，Normalize 也加了不变量兜底；
	/// #3 更新重启的两处命令行拼接必须按需包引号（路径含空白否则会被拆成多个参数）。
	/// </summary>

	/// <summary>
	/// v1.6.1 Must #2 的回归：保存失败绝不能再对用户显示"已保存并立即生效"——
	/// ConfigStore.Save/Apply 必须返回 false 且带回错误信息，设置页据此红字早退。
	/// </summary>

	/// <summary>
	/// v1.7.0 的回归：GitHub 令牌的整理、鉴权头、令牌失效/限额判定、配置往返，
	/// 以及"没填令牌只提示、不自动下载"的决策（UI 分支没法在自检里点，所以把决策抽成纯函数）。
	/// </summary>


	private static void StartWatchdog(string dir)
	{
		_reportDir = dir;
		try
		{
			Thread thread = new Thread(() =>
			{
				// 240 秒：拆分后的用例变多，机器忙时 150 秒会把「慢」误报成「卡住」
				Thread.Sleep(240000);
				try
				{
					Buf.Append("\r\n\r\n!! 自检总超时（240 秒），报告到此为止。");
					Buf.Append("\r\n   上面最后一组日志就是卡住的位置。\r\n");
					File.WriteAllText(Path.Combine(_reportDir, "selftest.txt"), Buf.ToString(), Encoding.UTF8);
				}
				catch
				{
				}
				Environment.Exit(2);
			});
			thread.IsBackground = true;
			thread.Name = "HoldDrag.SelfTestWatchdog";
			thread.Start();
		}
		catch
		{
		}
	}

	/// <summary>
	/// 只跑名字里含这个片段的用例（环境变量 HOLDDRAG_SELFTEST_ONLY）。
	/// 用途：本机环境有问题（比如第三方钩子卡住导致注入类用例必失败）时，
	/// 单独复跑某个用例定位，不必等整套跑完。
	/// </summary>
	private static string _only;

	private static void Safe(string name, Action test)
	{
		if (!string.IsNullOrEmpty(_only) && name.IndexOf(_only, StringComparison.OrdinalIgnoreCase) < 0)
		{
			return;
		}
		_watch.Restart();
		Log.Info("[自检] == " + name);
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

	private static void TestShakeGesture()
	{
		int[] array = new int[10] { 540, 460, 540, 460, 540, 460, 540, 460, 540, 460 };
		ShakeDetector shakeDetector = new ShakeDetector();
		shakeDetector.Reset(100, 300, 0L);
		bool flag = false;
		for (int i = 1; i <= 40; i++)
		{
			if (shakeDetector.Feed(100 + i * 10, 300, (long)i * 10L))
			{
				flag = true;
			}
		}
		Check(!flag, "匀速直线拖动不触发（无往复）");
		shakeDetector.Reset(200, 400, 0L);
		flag = false;
		int[] array2 = new int[8] { 300, 420, 380, 500, 470, 600, 700, 800 };
		long num = 0L;
		int[] array3 = array2;
		foreach (int x in array3)
		{
			num += 60;
			if (shakeDetector.Feed(x, 400, num))
			{
				flag = true;
			}
		}
		Check(!flag, "★ 正常拖窗（拖过头再拉回、边拖边微调）不触发 —— 误触发回归");
		shakeDetector.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		array3 = new int[10] { 512, 500, 512, 500, 512, 500, 512, 500, 512, 500 };
		foreach (int x2 in array3)
		{
			num += 30;
			if (shakeDetector.Feed(x2, 300, num))
			{
				flag = true;
			}
		}
		Check(!flag, "小幅手抖（半程不足 32px）不触发");
		shakeDetector.Reset(500, 200, 0L);
		flag = false;
		num = 0L;
		int[] array4 = new int[10] { 200, 280, 200, 280, 200, 280, 200, 280, 200, 280 };
		for (int k = 0; k < array4.Length; k++)
		{
			num += 30;
			if (shakeDetector.Feed(500, array4[k], num))
			{
				flag = true;
			}
		}
		Check(!flag, "上下方向抖动不触发（必须是横向晃）");
		shakeDetector.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		for (int l = 0; l < 10; l++)
		{
			num += 400;
			if (shakeDetector.Feed((l % 2 == 0) ? 580 : 420, 300, num))
			{
				flag = true;
			}
		}
		Check(!flag, "慢速往复（超出 800ms 手势窗口）不触发");
		shakeDetector.Reset(200, 300, 0L);
		flag = false;
		num = 0L;
		array3 = new int[5] { 900, 200, 900, 200, 900 };
		foreach (int x3 in array3)
		{
			num += 80;
			if (shakeDetector.Feed(x3, 300, num))
			{
				flag = true;
			}
		}
		Check(!flag, "大幅折返（单向位移超过上限）不触发 —— 那是拖窗口不是晃");
		shakeDetector.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		array3 = array;
		foreach (int x4 in array3)
		{
			num += 30;
			if (shakeDetector.Feed(x4, 300, num))
			{
				flag = true;
			}
		}
		Check(flag, "★ 快速原地左右晃动可以触发（第 4 次有效往复）");
		shakeDetector.Reset(500, 300, 0L);
		int num2 = 0;
		num = 0L;
		for (int m = 0; m < 3; m++)
		{
			array3 = array;
			foreach (int x5 in array3)
			{
				num += 30;
				if (shakeDetector.Feed(x5, 300, num))
				{
					num2++;
				}
			}
		}
		Eq(num2, 1, "一次晃动只触发一次（冷却生效）");
		shakeDetector.Reset(500, 300, 0L);
		num = 0L;
		array3 = array;
		foreach (int x6 in array3)
		{
			num += 30;
			shakeDetector.Feed(x6, 300, num);
		}
		num += 1200;
		flag = false;
		array3 = array;
		foreach (int x7 in array3)
		{
			num += 30;
			if (shakeDetector.Feed(x7, 300, num))
			{
				flag = true;
			}
		}
		Check(flag, "冷却结束后再次晃动可再次触发（不漏判）");
		ShakeDetector shakeDetector2 = new ShakeDetector();
		shakeDetector2.SetScale(2.0);
		shakeDetector2.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		array3 = new int[10] { 520, 480, 520, 480, 520, 480, 520, 480, 520, 480 };
		foreach (int x8 in array3)
		{
			num += 30;
			if (shakeDetector2.Feed(x8, 300, num))
			{
				flag = true;
			}
		}
		Check(!flag, "★ 200% 显示器下半程 20 逻辑 px 的抖动不触发（阈值随 DPI 缩放）");
		ShakeDetector shakeDetector3 = new ShakeDetector();
		shakeDetector3.SetScale(1.0);
		shakeDetector3.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		array3 = new int[10] { 540, 460, 540, 460, 540, 460, 540, 460, 540, 460 };
		foreach (int x9 in array3)
		{
			num += 30;
			if (shakeDetector3.Feed(x9, 300, num))
			{
				flag = true;
			}
		}
		Check(flag, "同样的往复在 100% 显示器上幅度足够 → 正常触发");
	}



	private static double ContrastRatio(Color a, Color b)
	{
		double num = RelativeLuminance(a);
		double num2 = RelativeLuminance(b);
		double num3 = Math.Max(num, num2);
		double num4 = Math.Min(num, num2);
		return (num3 + 0.05) / (num4 + 0.05);
	}

	private static double RelativeLuminance(Color c)
	{
		return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
	}

	private static double Channel(int v)
	{
		double num = (double)v / 255.0;
		return (num <= 0.03928) ? (num / 12.92) : Math.Pow((num + 0.055) / 1.055, 2.4);
	}




	/// <summary>
	/// 重排性能：拖窗口时每一档宽度都要重排一次，所以"一次重排要多久"直接决定卡不卡。
	/// 同时验证"同宽度重复调用不会重复干活"（去重生效）。
	/// </summary>

	/// <summary>
	/// 导航胶囊回归：只有"当前页"配底色。
	/// 之前 glow 动画值打到 1 就不回落，paint 又拿它当"要画底色"的依据，
	/// 结果点过的每一项都留一个灰胶囊（看起来像没选中却高亮）。
	/// </summary>
	// 侧栏胶囊是 170ms 淡入动画，量颜色前要让它跑完
	private static void SettleNavAnimation()
	{
		for (int i = 0; i < 30; i++)
		{
			Application.DoEvents();
			Thread.Sleep(10);
		}
	}





	private static string PixelAt(int x, int y)
	{
		try
		{
			using Bitmap bitmap = new Bitmap(1, 1);
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
			Color pixel = bitmap.GetPixel(0, 0);
			return pixel.R + "," + pixel.G + "," + pixel.B;
		}
		catch
		{
			return "?";
		}
	}

	private static int CountFramePixelsAround(Native.RECT win, Color want)
	{
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			int num = Math.Max(virtualScreen.Left, win.Left - 48);
			int num2 = Math.Max(virtualScreen.Top, win.Top - 48);
			int num3 = Math.Min(virtualScreen.Right, win.Right + 48);
			int num4 = Math.Min(virtualScreen.Bottom, win.Bottom + 48);
			if (num3 - num <= 2 || num4 - num2 <= 2)
			{
				return -1;
			}
			return CountColorIn(new Rectangle(num, num2, num3 - num, num4 - num2), want);
		}
		catch
		{
			return -1;
		}
	}

	private static int CountColorIn(Rectangle area, Color want)
	{
		if (area.Width <= 0 || area.Height <= 0)
		{
			return 0;
		}
		using Bitmap bitmap = new Bitmap(area.Width, area.Height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.CopyFromScreen(area.Left, area.Top, 0, 0, new Size(area.Width, area.Height));
		int num = 0;
		for (int i = 0; i < bitmap.Height; i++)
		{
			for (int j = 0; j < bitmap.Width; j++)
			{
				Color pixel = bitmap.GetPixel(j, i);
				if (Math.Abs(pixel.R - want.R) <= 30 && Math.Abs(pixel.G - want.G) <= 30 && Math.Abs(pixel.B - want.B) <= 30)
				{
					num++;
				}
			}
		}
		return num;
	}







	private static string ProbeOneGesture(DragFixture f, bool left)
	{
		try
		{
			ActivateTrigger(f.Coord);
			Native.POINT p = CenterOf(f.H);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			bool flag = f.Coord.OnMouse(left ? 513 : 516, ref ms);
			bool isDragging = f.Coord.Drag.IsDragging;
			Native.GetWindowRect(f.H, out var lpRect);
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 40, p.Y + 30));
			f.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.GetWindowRect(f.H, out var lpRect2);
			Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
			f.Coord.OnMouse(left ? 514 : 517, ref ms3);
			Settle();
			f.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			return "吞掉=" + flag + " 起会话=" + isDragging + " 前=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height + " 后=" + lpRect2.Left + "," + lpRect2.Top + " " + lpRect2.Width + "x" + lpRect2.Height;
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name + ": " + ex.Message;
		}
	}



	private static int[] ParseRgb(string s, int idx)
	{
		try
		{
			MatchCollection matchCollection = Regex.Matches(s, "(\\d+),(\\d+),(\\d+)");
			if (matchCollection.Count <= idx)
			{
				return new int[3];
			}
			return new int[3]
			{
				int.Parse(matchCollection[idx].Groups[1].Value),
				int.Parse(matchCollection[idx].Groups[2].Value),
				int.Parse(matchCollection[idx].Groups[3].Value)
			};
		}
		catch
		{
			return new int[3];
		}
	}


	// 第六轮报障的最小化复现：
	//   用户原话「按 enter 然后快速的按下左键，外框出现了但鼠标怎么动窗口都不动；
	//             按住左键过一段时间再移动就能顺利移动」。
	// 两种时序唯一的差别 = 「触发键到阈值之前，鼠标有没有已经在移动」。
	// T66（TestAdoptHeldButton）只覆盖了"按下之后一动不动等激活"，漏掉了真实用户会做的那一步：
	// 手是连着动的，左键一按下就开始拖，根本不会等 200ms 阈值。
	// 本用例把这段时序固化下来，任何一次回归都会在这里报红。




	private static int BandMaxGreenness(Rectangle band)
	{
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			band.Intersect(virtualScreen);
			if (band.Width <= 0 || band.Height <= 0)
			{
				return -1;
			}
			using Bitmap bitmap = new Bitmap(band.Width, band.Height);
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.CopyFromScreen(band.Left, band.Top, 0, 0, new Size(band.Width, band.Height));
			int num = int.MinValue;
			for (int i = 0; i < bitmap.Height; i++)
			{
				for (int j = 0; j < bitmap.Width; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					int num2 = pixel.G - (pixel.R + pixel.B) / 2;
					if (num2 > num)
					{
						num = num2;
					}
				}
			}
			return num;
		}
		catch
		{
			return -1;
		}
	}

	private static string SamplePixel(int x, int y)
	{
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			if (x < virtualScreen.Left || y < virtualScreen.Top || x >= virtualScreen.Right || y >= virtualScreen.Bottom)
			{
				return "?";
			}
			using Bitmap bitmap = new Bitmap(1, 1);
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
			Color pixel = bitmap.GetPixel(0, 0);
			return pixel.R + "," + pixel.G + "," + pixel.B;
		}
		catch
		{
			return "?";
		}
	}

	private static int Dist(string a, string b)
	{
		try
		{
			string[] array = a.Split(',');
			string[] array2 = b.Split(',');
			if (array.Length != 3 || array2.Length != 3)
			{
				return -1;
			}
			return Math.Abs(int.Parse(array[0]) - int.Parse(array2[0])) + Math.Abs(int.Parse(array[1]) - int.Parse(array2[1])) + Math.Abs(int.Parse(array[2]) - int.Parse(array2[2]));
		}
		catch
		{
			return -1;
		}
	}


	private static string HoldDragText(string text, int max)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		string text2 = text.Replace("\r", " ").Replace("\n", " ");
		return (text2.Length <= max) ? text2 : (text2.Substring(0, max) + "…");
	}

	private static int Fields_Sc(int design)
	{
		return (int)Math.Round((float)design * ((SettingsForm.UiScaleForTest <= 0f) ? 1f : SettingsForm.UiScaleForTest));
	}


	/// <summary>v1.1 新增：关于页的 GitHub 链接、自动更新开关、检查更新按钮，以及版本比较与替换器脚本。</summary>

	/// <summary>
	/// 更新重启的单实例竞态：替换时**新版本先启动、旧进程后退出**，
	/// 新进程一启动就会发现互斥体被旧进程占着。若照普通启动那样"通知已有实例然后自己退出"，
	/// 更新完就会出现【一个进程都不剩】。这里断言：
	///   ① `--updated-from` 存在时，启动流程的标志位被正确识别为"更新接续"
	///   ② 普通启动时仍然是"立即让位"语义（不能变成傻等 30 秒）
	///   ③ `--updated-from` 的取值解析正确（含路径带空格的情况）
	/// </summary>

	/// <summary>
	/// T91 单实例：同一时间只能开一个（用户需求："同一时间只能开一个程序，不能够开好几个"）。
	///
	/// 这里做【真端到端】验证，而不是只读常量字符串：
	///   ① 本进程先占住互斥体，扮演"已经在运行的那个实例"；
	///   ② 真的再启动一个 HoldDrag.exe；
	///   ③ 断言第二个实例在限时内【自己退出】（不会驻留成第二个进程）；
	///   ④ 断言它通过命名事件【唤起了已有实例】（设置窗口会被弹出来）。
	/// </summary>

	/// <summary>
	/// 钩子回调里同步写日志 + Flush 会拖住 WH_MOUSE_LL 回调，进而让整机鼠标排队。
	/// 这里断言：标记为钩子线程时，写日志不落盘而是进异步队列，且不阻塞。
	/// </summary>

	/// <summary>
	/// 就地替换的真实验证。不启动进程，只验证文件层面的三个关键事实：
	///   ① 旧文件能被改名（对正在运行的 exe 也成立，本机已实测）
	///   ② 新文件能顶替正式名字
	///   ③ 顶替后读回来的内容确实是"新版"的，且备份内容确实是"旧版"的
	/// 另外验证失败路径的回滚：新文件不存在时不能把旧文件弄丢。
	/// </summary>











	private static Native.POINT CenterOfRect(nint h)
	{
		Native.GetWindowRect(h, out var lpRect);
		return new Native.POINT((lpRect.Left + lpRect.Right) / 2, (lpRect.Top + lpRect.Bottom) / 2);
	}

	/// <summary>
	/// 自有窗口内的可交互控件（滑块 / 文本框 / 按钮）不该被触发键抢走输入：
	/// 指针停在这些控件上时必须被识别出来、真实按键路径必须放行；
	/// 而标题栏 / 空白区仍要被本程序接管，否则窗口就拖不动了。
	/// </summary>

	private static int PidOfWindow(nint h)
	{
		try
		{
			Native.GetWindowThreadProcessId(h, out var lpdwProcessId);
			return (int)lpdwProcessId;
		}
		catch
		{
			return 0;
		}
	}

	/// <summary>
	/// WinForms 的 GroupBox 同样用 BUTTON 类实现，只有 BS_GROUPBOX(0x7) 才能把它和真按钮区分开。
	/// 若不排除，设置页里成片的分组框会把"按住触发键拖窗口"整个吃掉 —— 表现为左键毫无反应。
	/// </summary>

	private static nint LaunchExternalWindow()
	{
		try
		{
			// 先清掉上一轮留下的孤儿宿主（自检被中断时会残留）。
			// 不清的话它们会污染候选窗口集，收尾时还会被误当成"本次启动的"而杀掉错误的进程 ——
			// 这正是 T80 偶发失败的根因：杀错一个、漏掉一个，下一轮继续命中残留。
			KillStaleWindowHosts();
			Process process = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--windowhost --no-elevate")
			{
				UseShellExecute = false
			});
			if (process == null)
			{
				return IntPtr.Zero;
			}
			// 宿主窗口标题里带自身进程号，因此这里可以精确定位"本次拉起的那个窗口"。
			string text = Program.WindowHostTitle + " #" + process.Id;
			for (int i = 0; i < 80; i++)
			{
				Application.DoEvents();
				Thread.Sleep(100);
				nint num = FindWindowByTitle(text);
				if (num == IntPtr.Zero)
				{
					num = FindWindowByTitleForPid(Program.WindowHostTitle, process.Id);
				}
				if (num != IntPtr.Zero)
				{
					return num;
				}
			}
			try
			{
				if (!process.HasExited)
				{
					process.Kill();
				}
			}
			catch
			{
			}
			return IntPtr.Zero;
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	/// <summary>按标题前缀 + 指定进程号找窗口，只认本次拉起的宿主。</summary>
	private static nint FindWindowByTitleForPid(string titlePrefix, int pid)
	{
		nint found = IntPtr.Zero;
		try
		{
			Native.EnumWindows((nint hwnd, nint l) =>
			{
				try
				{
					if (!Native.IsWindowVisible(hwnd))
					{
						return true;
					}
					string windowTitle = Native.GetWindowTitle(hwnd);
					if (string.IsNullOrEmpty(windowTitle) || !windowTitle.StartsWith(titlePrefix, StringComparison.Ordinal))
					{
						return true;
					}
					if (PidOfWindow(hwnd) != pid)
					{
						return true;
					}
					found = hwnd;
					return false;
				}
				catch
				{
					return true;
				}
			}, IntPtr.Zero);
		}
		catch
		{
		}
		return found;
	}

	/// <summary>清理以 WindowHostTitle 开头的孤儿宿主进程（绝不包含自身）。</summary>
	private static void KillStaleWindowHosts()
	{
		try
		{
			List<int> list = new List<int>(4);
			Native.EnumWindows((nint hwnd, nint l) =>
			{
				try
				{
					string windowTitle = Native.GetWindowTitle(hwnd);
					if (string.IsNullOrEmpty(windowTitle) || !windowTitle.StartsWith(Program.WindowHostTitle, StringComparison.Ordinal))
					{
						return true;
					}
					int num = PidOfWindow(hwnd);
					if (num != 0 && num != Environment.ProcessId && !list.Contains(num))
					{
						list.Add(num);
					}
				}
				catch
				{
				}
				return true;
			}, IntPtr.Zero);
			for (int i = 0; i < list.Count; i++)
			{
				try
				{
					using Process process = Process.GetProcessById(list[i]);
					if (process != null && !process.HasExited)
					{
						process.Kill();
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>只结束该窗口所属的宿主进程，并拒绝结束自身。</summary>
	private static void KillWindowHostProcess(nint hwnd)
	{
		try
		{
			if (hwnd == IntPtr.Zero)
			{
				return;
			}
			int num = PidOfWindow(hwnd);
			if (num == 0 || num == Environment.ProcessId)
			{
				return;
			}
			using Process process = Process.GetProcessById(num);
			if (process != null && !process.HasExited)
			{
				process.Kill();
			}
		}
		catch
		{
		}
	}

	private static nint FindWindowByTitle(string title)
	{
		try
		{
			return Native.FindWindowW(null, title);
		}
		catch
		{
			return IntPtr.Zero;
		}
	}




	private static void RunSwallowedButtonCase(int btn, bool[] downs, int downMsg, string maskWhat)
	{
		Coordinator.ButtonsDownOverrideForTest = downs;
		using DragFixture dragFixture = new DragFixture((AppConfig c) =>
		{
			if (btn == 0)
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
			}
			else if (btn == 1)
			{
				c.ActionRight = "Resize";
			}
			else
			{
				c.ActionMiddle = "Move";
			}
		});
		Native.POINT p = CenterOf(dragFixture.H);
		Native.MSLLHOOKSTRUCT ms = Ms(p);
		dragFixture.Coord.Trigger.SimulateKeyDown();
		Check(dragFixture.Coord.Trigger.Held && !dragFixture.Coord.Trigger.Active, "触发键处于阈值内的『按住』阶段（btn=" + btn + "）");
		Check(dragFixture.Coord.OnMouse(downMsg, ref ms), "★ 阈值期间的按下被吞掉，不再放行给鼠标下方的窗口（btn=" + btn + " —— 穿透点击的根因）");
		Check(dragFixture.Coord.BufferedDownForTest(btn), "按下进入缓冲（btn=" + btn + "：" + maskWhat + "）");
		Check(!dragFixture.Coord.PassedDownForTest(btn), "★ 没有记入『幽灵按下』账（这条按下从未投递给任何窗口，btn=" + btn + "）");
		dragFixture.Coord.Trigger.ForceHoldElapsed();
		Settle();
		Check(dragFixture.Coord.Trigger.Active, "触发键已激活（btn=" + btn + "）");
		Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 60, p.Y + 40));
		dragFixture.Coord.OnMouse(512, ref ms2);
		Settle();
		Check(dragFixture.Coord.Drag.IsDragging, "激活后第一次移动就把拖动补起（btn=" + btn + "）");
			Check(!dragFixture.Coord.BufferedDownForTest(btn), "缓冲已消费（btn=" + btn + "）");
	}

	private static int MaskOf(bool[] downs, int btn)
	{
		int num = 0;
		if (btn != 0 && downs[0])
		{
			num |= 1;
		}
		if (btn != 1 && downs[1])
		{
			num |= 2;
		}
		if (btn != 2 && downs[2])
		{
			num |= 0x10;
		}
		return num;
	}



	private static long MeasureOnOtherThread(Action action)
	{
		Exception err = null;
		long innerMs = -1L;
		Thread thread = new Thread(() =>
		{
			Stopwatch stopwatch2 = Stopwatch.StartNew();
			try
			{
				action();
			}
			catch (Exception ex)
			{
				err = ex;
			}
			finally
			{
				innerMs = stopwatch2.ElapsedMilliseconds;
			}
		});
		thread.IsBackground = true;
		thread.Start();
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (thread.IsAlive)
		{
			Application.DoEvents();
			Thread.Sleep(5);
		}
		stopwatch.Stop();
		if (err != null)
		{
			Log.Error(err);
		}
		if (stopwatch.ElapsedMilliseconds - innerMs > 50)
		{
			Log.Trace("[测量] 被测线程 " + innerMs + "ms，等待循环 " + stopwatch.ElapsedMilliseconds + "ms（差额为 UI 线程替阻塞目标泵消息，不计入）");
		}
		return innerMs;
	}

	private static bool PumpUntil(Func<bool> cond, int timeoutMs)
	{
		long num = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < num)
		{
			Application.DoEvents();
			if (cond())
			{
				return true;
			}
			Thread.Sleep(10);
		}
		Application.DoEvents();
		return cond();
	}

	private static bool RectsEqual(Native.RECT a, Native.RECT b)
	{
		if (a.Left == b.Left && a.Top == b.Top && a.Right == b.Right)
		{
			return a.Bottom == b.Bottom;
		}
		return false;
	}

	private static void Check(bool ok, string name, string detail = null)
	{
		if (ok)
		{
			_pass++;
			Line("  [PASS] " + name);
		}
		else
		{
			_fail++;
			Line("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : (" :: " + detail)));
		}
	}

	private static void Bad(string name, string detail)
	{
		_fail++;
		Line("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : (" :: " + detail)));
	}

	private static void Eq<T>(T actual, T expected, string name)
	{
		bool flag = object.Equals(actual, expected);
		bool ok = flag;
		object detail;
		if (!flag)
		{
			T val = expected;
			string? text = val?.ToString();
			val = actual;
			detail = "期望 " + text + "，实际 " + val;
		}
		else
		{
			detail = null;
		}
		Check(ok, name, (string)detail);
	}

	private static void Line(string s)
	{
		Buf.Append(s);
		Buf.Append("\r\n");
	}
}
