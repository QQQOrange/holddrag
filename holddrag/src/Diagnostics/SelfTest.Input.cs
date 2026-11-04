// HoldDrag · Diagnostics/SelfTest.Input.cs

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
	private static void TestTriggerHold()
	{
		AppConfig cfg = new AppConfig();
		cfg.RequireHold = true;
		cfg.HoldMs = 600;
		using TriggerState triggerState = new TriggerState(() => cfg);
		triggerState.SimulateKeyDown();
		Check(!triggerState.Active, "按下瞬间未到阈值，不应激活");
		Check(triggerState.Held, "按下后处于按住状态");
		triggerState.ForceHoldElapsed();
		Check(triggerState.Active, "超过阈值后进入激活态");
		bool replayCalled = false;
		Check(!triggerState.SimulateKeyUp(() =>
		{
			replayCalled = true;
			return true;
		}), "激活后松手不再补发原按键");
		Check(!replayCalled, "激活路径不应调用补发回调");
		Check(!triggerState.Active, "松手后退出激活态");
		Check(!triggerState.Held, "松手后不再是按住状态");
	}
	private static void TestTriggerShortPress()
	{
		AppConfig cfg = new AppConfig();
		cfg.RequireHold = true;
		cfg.HoldMs = 600;
		using TriggerState triggerState = new TriggerState(() => cfg);
		triggerState.SimulateKeyDown();
		bool replayCalled = false;
		Check(triggerState.SimulateKeyUp(() =>
		{
			replayCalled = true;
			return true;
		}), "阈值内松手走补发路径");
		Check(replayCalled, "阈值内松手确实补发了原按键");
		Check(!triggerState.Active, "短按不激活");
		Check(triggerState.LastPressReplayed, "短按已记录补发状态");
	}
	private static void TestTriggerNoHold()
	{
		AppConfig cfg = new AppConfig();
		cfg.RequireHold = false;
		using TriggerState triggerState = new TriggerState(() => cfg);
		triggerState.SimulateKeyDown();
		Check(triggerState.Active, "关闭长按阈值后按下即激活");
		Check(!triggerState.SimulateKeyUp(null), "关闭长按阈值后松手不补发（该键已成为纯修饰键）");
		Check(!triggerState.Active, "松手后退出激活态");
	}

	/// <summary>
	/// 点按切换模式（v1.10.0）：点一下抓住、再点一下放下。
	/// 与"长按"模式的区别只有状态机走向——用户不再需要一直按着键。
	/// </summary>
	private static void TestToggleMode()
	{
		AppConfig cfg = new AppConfig();
		cfg.TriggerMode = "Toggle";
		cfg.RequireHold = true;
		cfg.HoldMs = 600;
		Native.KBDLLHOOKSTRUCT kb = new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 13u
		};
		using TriggerState triggerState = new TriggerState(() => cfg);
		int activated = 0;
		int deactivated = 0;
		triggerState.Activated += delegate
		{
			activated++;
		};
		triggerState.Deactivated += delegate
		{
			deactivated++;
		};
		Check(triggerState.HandleKeyboard(true, ref kb), "点按模式：按下被吞掉（不当作文字输入）");
		Check(triggerState.Active, "点按模式：点一下立即激活（不需要等长按阈值）");
		Check(activated == 1, "点按模式：激活事件只广播一次");
		Check(triggerState.HandleKeyboard(false, ref kb), "点按模式：抬起被吞掉");
		Check(triggerState.Active, "点按模式：松手之后仍然保持激活");
		Check(!triggerState.Held, "点按模式：松手后不处于按住状态");
		Check(triggerState.HandleKeyboard(true, ref kb), "点按模式：第二次按下被吞掉");
		Check(!triggerState.Active, "点按模式：再点一下放下");
		Check(deactivated == 1, "点按模式：释放事件只广播一次");
		triggerState.HandleKeyboard(false, ref kb);
		triggerState.Reset("自检");
		Check(!triggerState.Active && !triggerState.Held, "点按模式：Reset 之后状态干净");
		// 默认配置必须仍是长按模式，否则老用户会被悄悄改变行为
		Check(new AppConfig().TriggerMode == "Hold", "出厂默认仍是长按模式");
	}

	/// <summary>
	/// 合成按下的「欠账」机制（v1.10.1 修的核心 bug）。
	///
	/// 触发键短按时，程序会把阈值期间被吞掉的左键按下**只补一个 down** 还给下层，
	/// 依赖「用户真实的 up 稍后送达」来配对。这个 up 一旦丢失
	/// （钩子被看门狗重装、暂停、UAC 安全桌面、程序退出），Windows 里就会永久留着一个
	/// 「按着」的左键：用户明明没按，所有程序都在拖选 —— 就是「左键一直长按没停过」的报障。
	/// 本用例锁住两件事：注入必须记账；抬起丢失时必须归还。
	/// </summary>
	private static void TestInjectedDownLedger()
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-selftest-inject");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch (Exception)
		{
		}
		// 只跑账本，不真的往系统注入鼠标（否则自检会在用户桌面上点出真实点击）
		Coordinator.SuppressSyntheticMouseForTest = true;
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			configStore.Current.ActionLeft = "Move";
			configStore.Current.TriggerKeyId = "K:0x0D";
			using (Coordinator coordinator = new Coordinator(configStore))
			{
				Native.MSLLHOOKSTRUCT ms = Ms(new Native.POINT(400, 300));
				coordinator.Trigger.SimulateKeyDown();
				Check(coordinator.OnMouse(513, ref ms), "阈值期间的左键按下被吞掉（从未投递给任何窗口）");
				Check(coordinator.BufferedDownForTest(0), "按下进了缓冲账");
				coordinator.Trigger.SimulateKeyUp(null);
				Check(coordinator.InjectedDownMaskForTest == 1, "短按补发只注入 down → 必须记账", "掩码=" + coordinator.InjectedDownMaskForTest);
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(400, 300));
				coordinator.OnMouse(514, ref ms2);
				Check(coordinator.InjectedDownMaskForTest == 0, "真实抬起到达 → 销账，之后不会再补发第二次 up");
			}
			using (Coordinator coordinator2 = new Coordinator(configStore))
			{
				Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(400, 300));
				coordinator2.Trigger.SimulateKeyDown();
				coordinator2.OnMouse(513, ref ms3);
				coordinator2.Trigger.SimulateKeyUp(null);
				Check(coordinator2.InjectedDownMaskForTest == 1, "再次注入同样记账", "掩码=" + coordinator2.InjectedDownMaskForTest);
				coordinator2.OnHooksReinstalled();
				Check(coordinator2.InjectedDownMaskForTest == 0, "钩子重装（缝隙里的抬起必丢）→ 欠账必须立刻归还，系统不许留卡键");
			}
		}
		finally
		{
			Coordinator.SuppressSyntheticMouseForTest = false;
			try
			{
				Directory.Delete(text, true);
			}
			catch (Exception)
			{
			}
		}
	}


	/// <summary>
	/// 抢占链头的「就地重装」绝不能广播 HooksReinstalled（v1.10.2 回归锁）。
	///
	/// 激活瞬间会做一次就地重装把鼠标钩子顶到链头。若这次重装也广播事件，
	/// 订阅方会按「钩子出过事 = 事件丢了」处理并立刻复位触发状态 ——
	/// 用户看到的就是「按住触发键，外框闪一下就没了、窗口完全拖不动」（v1.10.1 的真实事故）。
	/// 只有看门狗式 Reinstall()（真的会丢事件）才允许广播。
	/// </summary>
	private static void TestChainPriorityReinstallIsSilent()
	{
		using HookEngine hookEngine = new HookEngine(new NullSink());
		Check(hookEngine.Install(), "钩子安装成功", "LastError=" + hookEngine.LastError);
		int fired = 0;
		hookEngine.HooksReinstalled += delegate
		{
			fired++;
		};
		hookEngine.ReinstallMouseForChainPriority();
		for (int i = 0; i < 60; i++)
		{
			Application.DoEvents();
			Thread.Sleep(10);
		}
		Check(fired == 0, "★ 抢占链头的就地重装不得广播 HooksReinstalled（否则刚激活就被复位，核心拖动失效）", "广播了 " + fired + " 次");
		hookEngine.Reinstall();
		for (int i = 0; i < 60; i++)
		{
			Application.DoEvents();
			Thread.Sleep(10);
		}
		Check(fired == 1, "看门狗式重装必须广播 HooksReinstalled（那种重装真的会丢事件）", "广播了 " + fired + " 次");
	}
	private static void TestHooks()
	{
		using HookEngine hookEngine = new HookEngine(new NullSink());
		Check(hookEngine.Install(), "全局键鼠钩子安装成功", "LastError=" + hookEngine.LastError);
		Check(hookEngine.KeyboardInstalled, "键盘钩子已挂");
		Check(hookEngine.MouseInstalled, "鼠标钩子已挂");
		hookEngine.Uninstall();
		Check(!hookEngine.KeyboardInstalled && !hookEngine.MouseInstalled, "钩子已干净卸载");
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
	private static void TestDragMoveE2E()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			ActivateTrigger(dragFixture.Coord);
			Check(dragFixture.Coord.Trigger.Active, "触发键已进入激活态");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			Check(dragFixture.Coord.OnMouse(513, ref ms), "左键按下被钩子吞掉");
			Check(dragFixture.Coord.Drag.IsDragging, "拖动会话已经开始");
			Native.POINT p = new Native.POINT(CenterOf(dragFixture.H).X + 80, CenterOf(dragFixture.H).Y + 60);
			Native.MSLLHOOKSTRUCT ms2 = Ms(p);
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			Eq(lpRect2.Left - lpRect.Left, 80, "窗口水平方向跟随光标 80px");
			Eq(lpRect2.Top - lpRect.Top, 60, "窗口垂直方向跟随光标 60px");
			Eq(lpRect2.Width, lpRect.Width, "移动过程中宽度不变");
			Eq(lpRect2.Height, lpRect.Height, "移动过程中高度不变");
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			dragFixture.Coord.OnMouse(514, ref ms3);
			Check(!dragFixture.Coord.Drag.IsDragging, "松开左键后拖动会话结束");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestDragResizeE2E()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			ActivateTrigger(dragFixture.Coord);
			Native.POINT p = new Native.POINT(lpRect.Right - 12, lpRect.Bottom - 12);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			Check(dragFixture.Coord.OnMouse(516, ref ms), "右键按下被钩子吞掉");
			Check(dragFixture.Coord.Drag.IsDragging, "缩放会话已经开始");
			Native.POINT p2 = new Native.POINT(p.X + 60, p.Y + 40);
			Native.MSLLHOOKSTRUCT ms2 = Ms(p2);
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			Eq(lpRect2.Right - lpRect.Right, 60, "右边缘跟随光标外扩 60px");
			Eq(lpRect2.Bottom - lpRect.Bottom, 40, "下边缘跟随光标外扩 40px");
			Eq(lpRect2.Left, lpRect.Left, "缩放右下角时左边不动");
			Eq(lpRect2.Top, lpRect.Top, "缩放右下角时上边不动");
			Native.MSLLHOOKSTRUCT ms3 = Ms(p2);
			dragFixture.Coord.OnMouse(517, ref ms3);
			Check(!dragFixture.Coord.Drag.IsDragging, "松开右键后缩放会话结束");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestDragNeverSwallowsMouseMove()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.POINT p = CenterOf(dragFixture.H);
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms);
			Check(dragFixture.Coord.Drag.IsDragging, "拖动会话已开始");
			int num = 0;
			for (int i = 1; i <= 6; i++)
			{
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + i * 15, p.Y + i * 8));
				if (dragFixture.Coord.OnMouse(512, ref ms2))
				{
					num++;
				}
				Settle();
			}
			Eq(num, 0, "拖动期间鼠标移动消息一次都没有被吞掉（吞掉会让光标冻住）");
			AppConfig appConfig = new AppConfig();
			int num2 = 8;
			int num3 = 15 * Math.Max(1, appConfig.MoveRate);
			int num4 = num2 * Math.Max(1, appConfig.MoveRate);
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			Check(Math.Abs(lpRect2.Left - lpRect.Left - 90) <= num3, "窗口水平方向跟上第 6 步（允许一个降频步长的滞后）", "期望≈90 实际=" + (lpRect2.Left - lpRect.Left) + " 容差=" + num3);
			Check(Math.Abs(lpRect2.Top - lpRect.Top - 48) <= num4, "窗口垂直方向跟上第 6 步（允许一个降频步长的滞后）", "期望≈48 实际=" + (lpRect2.Top - lpRect.Top) + " 容差=" + num4);
			Native.POINT pOINT = new Native.POINT(p.X + 200, p.Y + 100);
			for (int j = 0; j < 60; j++)
			{
				Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(p.X + 100 + j * 2, p.Y + 50 + j));
				dragFixture.Coord.OnMouse(512, ref ms3);
			}
			Native.MSLLHOOKSTRUCT ms4 = Ms(pOINT);
			dragFixture.Coord.OnMouse(512, ref ms4);
			dragFixture.Coord.Drag.UpdateFinal(pOINT);
			Settle();
			Native.GetWindowRect(dragFixture.H, out var lpRect3);
			Eq(lpRect3.Left - lpRect.Left, 200, "高频灌入 60 次移动后，窗口停在最后的位置（无累积漂移）");
			Eq(lpRect3.Top - lpRect.Top, 100, "垂直方向同样停在最后的位置");
			Native.MSLLHOOKSTRUCT ms5 = Ms(pOINT);
			dragFixture.Coord.OnMouse(514, ref ms5);
			Check(!dragFixture.Coord.Drag.IsDragging, "松开左键后拖动会话结束");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestDragRingFrameSwap()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.FrameStyleId = "Glow";
				c.FrameColorId = "Violet";
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.POINT pOINT = CenterOf(dragFixture.H);
			Native.SetCursorPos(pOINT.X, pOINT.Y);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "悬停时亮的是抗锯齿分层框");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(513, ref ms);
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(ms.pt.X + 30, ms.pt.Y + 20));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Check(drag.IsDragging, "拖动会话进行中");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "★ 拖动期间亮的是抗锯齿分层框（与设置里选的样式同一段绘制代码）");
			Check(drag.Frame.ColorIdForTest == "Violet" && drag.Frame.StyleIdForTest == "Glow", "★ 拖动中用的就是配置里的颜色与样式（不再退化成细线）", "实际 " + drag.Frame.ColorIdForTest + " / " + drag.Frame.StyleIdForTest);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			int num = CountFramePixelsAround(lpRect, FrameColors.Resolve("Violet"));
			Check(num > 200, "★ 拖动中屏幕上真的画出了配置的外框色像素", "命中 " + num + " 像素");
			Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
			dragFixture.Coord.OnMouse(514, ref ms3);
			Settle();
			Check(!drag.IsDragging, "拖动会话已结束");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "触发键仍按着时外框继续亮着（静止时保持好看）");
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "退出激活态后外框不残留（松键即收）");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestRealKeyboardHoldActivates()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-hold-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		HookEngine hookEngine = null;
		Coordinator coordinator = null;
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 200;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.BringToFront = false;
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			hookEngine = new HookEngine(coordinator);
			hookEngine.Install();
			Pump();
			Settle();
			Check(hookEngine.KeyboardInstalled, "键盘钩子已装（专用线程）", "错误码 " + hookEngine.LastError);
			SendRawKey(13, down: true);
			bool flag = false;
			for (int i = 0; i < 60; i++)
			{
				if (flag)
				{
					break;
				}
				Thread.Sleep(25);
				Application.DoEvents();
				flag = coordinator.Trigger.Active;
			}
			Check(flag, "真实长按触发键进入激活态（Threading.Timer 真实触发）", "HoldMs=200 等了 1.5s 仍未激活");
			SendRawKey(13, down: false);
			for (int j = 0; j < 20; j++)
			{
				if (!coordinator.Trigger.Active)
				{
					break;
				}
				Thread.Sleep(25);
				Application.DoEvents();
			}
			Check(!coordinator.Trigger.Active, "松开触发键后回到非激活态");
		}
		finally
		{
			if (coordinator != null)
			{
				try
				{
					coordinator.Trigger.SimulateKeyUp(null);
				}
				catch
				{
				}
			}
			hookEngine?.Dispose();
			coordinator?.Dispose();
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
		}
	}
	private static void TestMoveFlagsAreAsync()
	{
		Check(ok: true, "★ 平移用同步写（照抄 AltSnap MOVETHICKBORDERS；异步会造成队列堆积 → 一卡一卡）", "MOVETHICKBORDERS=0x" + 533u.ToString("X"));
		Check(ok: true, "缩放用同步写（照抄 AltSnap RESIZEFLAG）", "RESIZEFLAGS=0x" + 532u.ToString("X"));
		Check(ok: true, "不可缩放窗口的兜底标志位仍带 SWP_ASYNCWINDOWPOS（同步写对这类窗口有卡死风险）", "MOVEASYNC=0x" + 16917u.ToString("X"));
		Check(ok: true, "平移不带 SWP_NOCOPYBITS（否则拖动时窗口内容不跟随，会花屏）", "MOVETHICKBORDERS=0x" + 533u.ToString("X"));
		VerifyBottomBarButtons();
	}
	private static void TestRealMouseThroughHook()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-hook-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		HookEngine hookEngine = null;
		HookWatchdog hookWatchdog = null;
		Coordinator coordinator = null;
		bool cursorPos = Native.GetCursorPos(out var lpPoint);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 400;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = true;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			hookEngine = new HookEngine(coordinator);
			hookEngine.Install();
			hookWatchdog = new HookWatchdog(hookEngine);
			hookWatchdog.Start();
			Pump();
			Settle();
			Check(hookEngine.MouseInstalled && hookEngine.KeyboardInstalled, "全局鼠标钩子已安装", "错误码 " + hookEngine.LastError);
			Native.POINT pt = new Native.POINT(400, 300);
			Native.SetCursorPos(pt.X, pt.Y);
			Form form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.TopMost = true;
			form.Bounds = new Rectangle(pt.X - 150, pt.Y - 100, 300, 200);
			form.Show();
			Pump();
			Settle();
			int x = 0;
			int y = 0;
			bool ok = hookEngine.TryGetLastSeen(out x, out y);
			Check(ok, "钩子收到了鼠标移动事件（钩子确实活着）", "seenValid=" + ok + " 位置=" + x + "," + y);
			ActivateTrigger(coordinator);
			Settle();
			Check(coordinator.Trigger.Active, "触发键已进入激活态");
			long seenCount = hookEngine.SeenCount;
			Native.SetCursorPos(pt.X + 1, pt.Y);
			Pump();
			for (int i = 0; i < 10; i++)
			{
				if (hookEngine.SeenCount != seenCount)
				{
					break;
				}
				Application.DoEvents();
				Thread.Sleep(10);
			}
			bool flag = hookEngine.SeenCount > seenCount;
			if (!flag)
			{
				Log.Info("[自检 T43] 移动事件探测失败 —— 本环境下低级鼠标钩子可能不接收任何合成事件");
			}
			int sessionCount = coordinator.Drag.SessionCount;
			bool flag2 = InjectAndWaitDrag(coordinator, 8u, 16u, "探测", sessionCount);
			bool num = (coordinator.Drag.SessionCount > sessionCount) | flag2;
			CleanupSession(coordinator, 517, pt);
			if (!num)
			{
				Line("（环境限制）本环境下 SendInput 的【按键】事件不会进入低级鼠标钩子，无法在此验证左键/右键链路，跳过。");
				Line("（移动事件可达=" + flag + "）");
				coordinator.Trigger.SimulateKeyUp(null);
				return;
			}
			ActivateTrigger(coordinator);
			Settle();
			Check(coordinator.Trigger.Active, "重新进入触发态");
			int sessionCount2 = coordinator.Drag.SessionCount;
			Check(InjectAndWaitDrag(coordinator, 2u, 4u, "左键", sessionCount2), "★ 真实左键事件能启动拖动会话（触发后左键失效回归）", "会话数没增加（before=" + sessionCount2 + " now=" + coordinator.Drag.SessionCount + "）说明左键按下事件没走到 Begin");
			CleanupSession(coordinator, 514, pt);
			ActivateTrigger(coordinator);
			Settle();
			int sessionCount3 = coordinator.Drag.SessionCount;
			Check(InjectAndWaitDrag(coordinator, 8u, 16u, "右键", sessionCount3), "★ 真实右键事件能启动缩放会话（触发后右键失效回归）", "会话数没增加（before=" + sessionCount3 + " now=" + coordinator.Drag.SessionCount + "）说明右键按下事件没走到 Begin");
			CleanupSession(coordinator, 517, pt);
			coordinator.Trigger.SimulateKeyUp(null);
		}
		finally
		{
			try
			{
				hookWatchdog?.Stop();
			}
			catch
			{
			}
			try
			{
				hookWatchdog?.Dispose();
			}
			catch
			{
			}
			try
			{
				hookEngine?.Dispose();
			}
			catch
			{
			}
			try
			{
				coordinator?.Dispose();
			}
			catch
			{
			}
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
			try
			{
				if (cursorPos)
				{
					Native.SetCursorPos(lpPoint.X, lpPoint.Y);
				}
			}
			catch
			{
			}
		}
	}
	private static void TestRealDragMovesWindow()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-t48-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		HookEngine hookEngine = null;
		Coordinator coordinator = null;
		Form form = null;
		bool cursorPos = Native.GetCursorPos(out var lpPoint);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 200;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			current.RestoreMaximizedWhenMoved = false;
			Native.GetCursorPos(out var lpPoint2);
			Native.RECT rECT = WorkAreaOfNearestMonitor(lpPoint2);
			form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.Text = "HoldDrag T48 target";
			form.TopMost = true;
			form.Bounds = new Rectangle(rECT.Left + 60, rECT.Top + 60, 320, 240);
			form.Show();
			try
			{
				form.BringToFront();
				form.Activate();
			}
			catch
			{
			}
			Pump();
			Settle();
			WindowActions.SetForeignForTest(form.Handle, on: true);
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			hookEngine = new HookEngine(coordinator);
			hookEngine.Install();
			Pump();
			Settle();
			if (!hookEngine.MouseInstalled)
			{
				Line("  （鼠标钩子装不上，跳过）");
				return;
			}
			Native.POINT pOINT = CenterOf(form.Handle);
			if (!PlaceCursorOn(form.Handle, pOINT))
			{
				Line("  （光标无法稳定停在测试窗口上 —— 桌面上有别的输入在动，跳过本用例）");
				return;
			}
			Pump();
			Settle();
			ActivateTrigger(coordinator);
			Settle();
			Check(coordinator.Trigger.Active, "T48 触发键已进入激活态", "Active=" + coordinator.Trigger.Active);
			if (!PlaceCursorOn(form.Handle, pOINT))
			{
				Line("  （注入前光标离开了测试窗口，跳过本用例）");
				return;
			}
			if (!Native.GetWindowRect(form.Handle, out var lpRect))
			{
				Line("  （取不到窗口矩形，跳过）");
				return;
			}
			int sessionCount = coordinator.Drag.SessionCount;
			long seenCount = hookEngine.SeenCount;
			InjectMouseButton(2u);
			for (int i = 0; i < 40; i++)
			{
				if (coordinator.Drag.SessionCount > sessionCount)
				{
					break;
				}
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Application.DoEvents();
			string[] array = new string[12]
			{
				"    （诊断）注入前 SeenCount=",
				seenCount.ToString(),
				" 注入后=",
				hookEngine.SeenCount.ToString(),
				" 触发态=",
				coordinator.Trigger.Active.ToString(),
				" 会话数=",
				coordinator.Drag.SessionCount.ToString(),
				" 光标=",
				null,
				null,
				null
			};
			Native.POINT pOINT2 = pOINT;
			array[9] = pOINT2.ToString();
			array[10] = " 目标=";
			array[11] = DescribeWindow(form.Handle);
			Line(string.Concat(array));
			bool flag = coordinator.Drag.SessionCount > sessionCount;
			Check(flag, "真实左键事件启动了移动会话", "会话数没增加（" + sessionCount + "）说明按下事件没走到 Begin");
			if (!flag)
			{
				return;
			}
			Native.GetCursorPos(out var lpPoint3);
			InjectMouseMoveAbsolute(lpPoint3.X + 90, lpPoint3.Y + 60);
			for (int j = 0; j < 15; j++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			long seenCount2 = hookEngine.SeenCount;
			ReadCursorAndRect(form.Handle, out var pt, out var rect);
			int num = pt.X - lpPoint3.X;
			int num2 = pt.Y - lpPoint3.Y;
			string[] array2 = new string[6]
			{
				"    （诊断2）移动后 SeenCount=",
				seenCount2.ToString(),
				" 拖拽中=",
				coordinator.Drag.IsDragging.ToString(),
				" 光标=",
				null
			};
			pOINT2 = pt;
			array2[5] = pOINT2.ToString();
			Line(string.Concat(array2));
			int num3 = rect.Left - lpRect.Left;
			int num4 = rect.Top - lpRect.Top;
			bool ok = Math.Abs(num3 - num) <= 3 && Math.Abs(num4 - num2) <= 3;
			string[] array3 = new string[14]
			{
				"光标位移=",
				num.ToString(),
				",",
				num2.ToString(),
				" 窗口位移=",
				num3.ToString(),
				",",
				num4.ToString(),
				" 前=",
				null,
				null,
				null,
				null,
				null
			};
			Native.RECT rECT2 = lpRect;
			array3[9] = rECT2.ToString();
			array3[10] = " 后=";
			rECT2 = rect;
			array3[11] = rECT2.ToString();
			array3[12] = " 换算系数=";
			array3[13] = CoordSpace.Scale.ToString("0.####");
			Check(ok, "★ 窗口真的跟着光标移动了（核心功能端到端）", string.Concat(array3));
			InjectMouseButton(4u);
			for (int k = 0; k < 10; k++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			ActivateTrigger(coordinator);
			Settle();
			Native.GetWindowRect(form.Handle, out var lpRect2);
			int sessionCount2 = coordinator.Drag.SessionCount;
			if (!PlaceCursorOn(form.Handle, new Native.POINT(lpRect2.Right - 8, lpRect2.Bottom - 8)))
			{
				Line("  （光标无法稳定停在窗口右下角，跳过缩放段）");
				return;
			}
			for (int l = 0; l < 20; l++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Native.GetCursorPos(out var lpPoint4);
			string[] array4 = new string[8]
			{
				"    （诊断）期望光标=",
				(lpRect2.Right - 8).ToString(),
				",",
				(lpRect2.Bottom - 8).ToString(),
				" 实际=",
				null,
				null,
				null
			};
			pOINT2 = lpPoint4;
			array4[5] = pOINT2.ToString();
			array4[6] = " 窗口=";
			rECT2 = lpRect2;
			array4[7] = rECT2.ToString();
			Line(string.Concat(array4));
			InjectMouseButton(8u);
			for (int m = 0; m < 40; m++)
			{
				if (coordinator.Drag.SessionCount > sessionCount2)
				{
					break;
				}
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Application.DoEvents();
			bool flag2 = coordinator.Drag.SessionCount > sessionCount2;
			Check(flag2, "真实右键事件启动了缩放会话", "会话数没增加（" + sessionCount2 + "）说明右键按下事件没走到 Begin");
			if (flag2)
			{
				Native.GetCursorPos(out var lpPoint5);
				InjectMouseMoveAbsolute(lpPoint5.X + 70, lpPoint5.Y + 50);
				for (int n = 0; n < 20; n++)
				{
					Application.DoEvents();
					Thread.Sleep(10);
				}
				ReadCursorAndRect(form.Handle, out var pt2, out var rect2);
				int num5 = rect2.Width - lpRect2.Width;
				int num6 = rect2.Height - lpRect2.Height;
				int num7 = pt2.X - lpPoint5.X;
				int num8 = pt2.Y - lpPoint5.Y;
				Check(Math.Abs(num5 - num7) <= 4 && Math.Abs(num6 - num8) <= 4, "★ 右键真的改变了窗口大小（核心功能端到端）", "光标位移=" + num7 + "," + num8 + " 尺寸变化=" + num5 + "," + num6 + " 前=" + lpRect2.Width + "x" + lpRect2.Height + " 后=" + rect2.Width + "x" + rect2.Height);
			}
			InjectMouseButton(16u);
			for (int num9 = 0; num9 < 10; num9++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			Check(ok: false, "全链路拖动用例执行", "异常: " + ex.Message);
		}
		finally
		{
			try
			{
				if (form != null)
				{
					WindowActions.SetForeignForTest(form.Handle, on: false);
				}
			}
			catch
			{
			}
			try
			{
				coordinator?.SetPaused(paused: true);
			}
			catch
			{
			}
			try
			{
				hookEngine?.Dispose();
			}
			catch
			{
			}
			try
			{
				coordinator?.Dispose();
			}
			catch
			{
			}
			try
			{
				if (form != null && !form.IsDisposed)
				{
					form.Visible = false;
					Native.DestroyWindow(form.Handle);
				}
				if (form != null)
				{
					form.Close();
					form.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
			try
			{
				if (cursorPos)
				{
					Native.SetCursorPos(lpPoint.X, lpPoint.Y);
				}
			}
			catch
			{
			}
			for (int num10 = 0; num10 < 6; num10++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
		}
	}
	private static void TestBlacklistPickers()
	{
		string text = WindowActions.ProcessExeNameByPid((uint)Process.GetCurrentProcess().Id);
		Check(text.Length > 0, "能取到本进程的 exe 名（黑名单按进程名匹配的前提）", "得到 \"" + text + "\"");
		if (!NoRoomForTestWindow())
		{
			return;
		}
		Form form = null;
		try
		{
			form = new Form();
			form.Text = "HoldDrag T47 blacklist";
			form.Bounds = new Rectangle(0, 0, 200, 120);
			form.Show();
			Pump();
			form.Text = "zzz 标题里没有那个词 zzz";
			Pump();
			WindowActions.ApplyBlacklist(text);
			Check(WindowActions.IsBlacklisted(form.Handle), "★ 黑名单能用【进程名】命中窗口（选择程序 / 点选窗口 的前提）", "关键字=" + text + " 标题=\"" + Native.GetWindowTitle(form.Handle) + "\" 类名=" + Native.GetClassName(form.Handle));
			WindowActions.ApplyBlacklist("zzz_definitely_not_a_real_process_zzz");
			Check(!WindowActions.IsBlacklisted(form.Handle), "不相关的关键字不会误伤窗口", "关键字=zzz_definitely_not_a_real_process_zzz");
		}
		finally
		{
			WindowActions.ApplyBlacklist(string.Empty);
			try
			{
				if (form != null && !form.IsDisposed)
				{
					form.Visible = false;
					Native.DestroyWindow(form.Handle);
				}
				if (form != null)
				{
					form.Close();
					form.Dispose();
				}
			}
			catch
			{
			}
			for (int i = 0; i < 4; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
		}
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Control control = FindByText(settingsForm, "选择程序…");
		Control control2 = FindByText(settingsForm, "点选窗口");
		Check(control != null && control2 != null, "黑名单的两个新按钮都在界面上（选择程序… / 点选窗口）", "选择程序=" + (control != null) + " 点选窗口=" + (control2 != null));
		settingsForm.ProbeAppendBlacklist("notepad");
		Eq(settingsForm.ProbeBlacklistText, "notepad", "第一个关键字写进黑名单框");
		settingsForm.ProbeAppendBlacklist("notepad");
		Eq(settingsForm.ProbeBlacklistText, "notepad", "重复的关键字不会加两次");
		settingsForm.ProbeAppendBlacklist("chrome");
		Eq(settingsForm.ProbeBlacklistText, "notepad，chrome", "第二个关键字用中文逗号追加");
		settingsForm.ProbeAppendBlacklist("   ");
		Eq(settingsForm.ProbeBlacklistText, "notepad，chrome", "空白关键字被忽略");
		settingsForm.Close();
	}
	private static void TestHookWatchdog()
	{
		HookEngine hookEngine = new HookEngine(new NullSink());
		try
		{
			if (!hookEngine.Install())
			{
				Line("  （当前环境装不上钩子的(maybe 没桌面/没权限)，跳过看门狗用例）");
				return;
			}
			HookWatchdog hookWatchdog = new HookWatchdog(hookEngine);
			hookWatchdog.CheckOnce();
			if (!Native.GetCursorPos(out var lpPoint))
			{
				Line("  （取不到光标位置，跳过）");
				return;
			}
			hookEngine.Uninstall();
			hookWatchdog.CheckOnce();
			bool flag = false;
			for (int i = 0; i < 12; i++)
			{
				if (flag)
				{
					break;
				}
				int num = 11 + i * 3;
				int num2 = 9 + i * 2;
				Native.SetCursorPos(lpPoint.X + num, lpPoint.Y + num2);
				for (int j = 0; j < 4; j++)
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				flag = hookWatchdog.CheckOnce();
			}
			try
			{
				Native.SetCursorPos(lpPoint.X, lpPoint.Y);
			}
			catch
			{
			}
			if (!flag)
			{
				Line("  （本机 12 次尝试都没收到鼠标钩子事件 —— 多半是无桌面/受限会话环境，看门狗用例跳过）");
				return;
			}
			Check(flag, "光标动了但钩子没收到事件 → 判定钩子已死");
			Eq(hookEngine.ReinstallCount, 1, "看门狗重装了一次");
			Check(hookEngine.MouseInstalled, "重装之后鼠标钩子确实装回来了");
		}
		finally
		{
			try
			{
				hookEngine.Dispose();
			}
			catch
			{
			}
		}
	}
	private static void TestTriggerStaysActive()
	{
		using DragFixture dragFixture = new DragFixture(null);
		ActivateTrigger(dragFixture.Coord);
		Check(dragFixture.Coord.Trigger.Active, "触发键已进入激活态");
		long num = Environment.TickCount64 + 2500;
		while (Environment.TickCount64 < num)
		{
			Application.DoEvents();
			Thread.Sleep(20);
		}
		Check(dragFixture.Coord.Trigger.Active, "按住 2.5 秒后激活态依然保持（看门狗误判回归）");
		Check(dragFixture.Coord.Trigger.Held, "触发键仍被记为按住");
	}
	private static void TestDragVisualFeedback()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Color want = FrameColors.Resolve(dragFixture.Store.Current.FrameColorId);
			Native.POINT p = CenterOf(dragFixture.H);
			Native.SetCursorPos(p.X, p.Y);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms);
			Settle();
			Check(drag.IsDragging, "拖动会话已开始");
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 60, p.Y + 40));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Settle();
			Check(drag.Frame.IsShown, "拖动中分层框的显示标志为真（IsShown）");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "拖动中分层框窗口 WS_VISIBLE 置位");
			Native.GetWindowRect(drag.Frame.Handle, out var lpRect);
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			int num = CountFramePixelsAround(lpRect, want);
			Line("    （环上取点）左中点=" + PixelAt(lpRect.Left + 12, (lpRect.Top + lpRect.Bottom) / 2) + " 上中点=" + PixelAt((lpRect.Left + lpRect.Right) / 2, lpRect.Top + 12) + " 右中点=" + PixelAt(lpRect.Right - 12, (lpRect.Top + lpRect.Bottom) / 2) + " 下中点=" + PixelAt((lpRect.Left + lpRect.Right) / 2, lpRect.Bottom - 12) + " 期望色=" + want.R + "," + want.G + "," + want.B);
			bool ok = num > 150;
			string[] array = new string[8]
			{
				"命中 ",
				num.ToString(),
				" 像素，框矩形=",
				null,
				null,
				null,
				null,
				null
			};
			Native.RECT rECT = lpRect;
			array[3] = rECT.ToString();
			array[4] = " 样式=";
			array[5] = drag.Frame.StyleIdForTest;
			array[6] = " 颜色=";
			array[7] = drag.Frame.ColorIdForTest;
			Check(ok, "★ 拖动中屏幕上真的画出了外框（截屏数到外框色像素）", string.Concat(array));
			Check(lpRect.Width >= lpRect2.Width && lpRect.Height >= lpRect2.Height && lpRect.Width - lpRect2.Width <= 80 && lpRect.Height - lpRect2.Height <= 80, "框矩形尺寸与窗口匹配（±留白）", "框=" + lpRect.Width + "x" + lpRect.Height + " 窗=" + lpRect2.Width + "x" + lpRect2.Height);
			Native.RECT rECT2 = lpRect;
			Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(p.X + 170, p.Y + 120));
			dragFixture.Coord.OnMouse(512, ref ms3);
			Settle();
			Settle();
			Native.GetWindowRect(drag.Frame.Handle, out lpRect);
			Native.RECT rECT3 = lpRect;
			Native.GetWindowRect(dragFixture.H, out var lpRect3);
			Check(rECT3.Left > rECT2.Left + 40, "★ 框跟着窗口一起移动（持续跟随，不是停在原地）", rECT2.Left + " → " + rECT3.Left);
			Check(Math.Abs(rECT3.Left - lpRect3.Left - (rECT2.Left - lpRect2.Left)) <= 6, "框相对窗口的偏移保持不变（没有漂移）", "框-窗 Δ 前=" + (rECT2.Left - lpRect2.Left) + " 后=" + (rECT3.Left - lpRect3.Left));
			Native.GetWindowRect(drag.Frame.Handle, out var lpRect4);
			Native.MSLLHOOKSTRUCT ms4 = Ms(ms3.pt);
			dragFixture.Coord.OnMouse(514, ref ms4);
			Settle();
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "松手并松开触发键后外框收起");
			int num2 = CountFramePixelsAround(lpRect4, want);
			Check(num2 < Math.Max(30, num / 4), "★ 对照组：框收起后同一区域数不到外框色（证明①数到的确实是框）", "拖动中 " + num + " → 松手后 " + num2);
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestTriggerOnBigWindows()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using (DragFixture f = new DragFixture((AppConfig c) =>
			{
				c.RestoreMaximizedWhenMoved = true;
			}))
			{
				Line("    （普通窗口 左键）" + ProbeOneGesture(f, left: true));
				Line("    （普通窗口 右键）" + ProbeOneGesture(f, left: false));
			}
			using (DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.RestoreMaximizedWhenMoved = true;
				c.ExitFullscreenOnTrigger = true;
			}))
			{
				Native.ShowWindow(dragFixture.H, 3);
				Settle();
				bool flag = Native.IsZoomed(dragFixture.H);
				Line("    （最大化窗口）IsZoomed=" + flag);
				string text = ProbeOneGesture(dragFixture, left: true);
				Line("    （最大化窗口 左键）" + text);
				bool flag2 = !Native.IsZoomed(dragFixture.H);
				Check(flag & flag2, "★ 最大化窗口：左键拖动后不再是最大化（先还原再拖）", text);
				Native.ShowWindow(dragFixture.H, 3);
				Settle();
				string text2 = ProbeOneGesture(dragFixture, left: false);
				Line("    （最大化窗口 右键）" + text2);
				Check(!Native.IsZoomed(dragFixture.H), "★ 最大化窗口：右键缩放后不再是最大化（右键功能必须还在）", text2);
				Native.ShowWindow(dragFixture.H, 3);
				Settle();
				ActivateTrigger(dragFixture.Coord);
				Native.POINT p = CenterOf(dragFixture.H);
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				dragFixture.Coord.OnMouse(516, ref ms);
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect);
				Check(p.X >= lpRect.Left && p.X < lpRect.Right && p.Y >= lpRect.Top && p.Y < lpRect.Bottom, "★ 最大化窗口右键：降级成普通窗口后光标仍落在窗口内（角区判定才有意义）", "光标=" + p.X + "," + p.Y + " 窗口=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height);
				Native.MSLLHOOKSTRUCT ms2 = Ms(p);
				dragFixture.Coord.OnMouse(517, ref ms2);
				Settle();
				dragFixture.Coord.Trigger.SimulateKeyUp(null);
				Settle();
			}
			using (DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ExitFullscreenOnTrigger = true;
				c.AllowFullscreenWindows = false;
			}))
			{
				Rectangle virtualScreen = SystemInformation.VirtualScreen;
				Native.SetWindowPos(dragFixture2.H, IntPtr.Zero, virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height, 20u);
				Settle();
				bool flag3 = WindowActions.IsFullscreen(dragFixture2.H);
				Line("    （全屏窗口·默认配置）IsFullscreen=" + flag3);
				string text3 = ProbeOneGesture(dragFixture2, left: false);
				Line("    （全屏窗口 右键）" + text3);
				Check(flag3 && !WindowActions.IsFullscreen(dragFixture2.H), "★ 真全屏窗口：右键缩放后不再是全屏（默认配置下就要生效）", text3);
				Native.SetWindowPos(dragFixture2.H, IntPtr.Zero, virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height, 20u);
				Settle();
				string text4 = ProbeOneGesture(dragFixture2, left: true);
				Line("    （全屏窗口 左键）" + text4);
				Check(!WindowActions.IsFullscreen(dragFixture2.H), "★ 真全屏窗口：左键拖动后不再是全屏", text4);
			}
			using DragFixture dragFixture3 = new DragFixture((AppConfig c) =>
			{
				c.ExitFullscreenOnTrigger = false;
				c.AllowFullscreenWindows = false;
			});
			Rectangle virtualScreen2 = SystemInformation.VirtualScreen;
			Native.SetWindowPos(dragFixture3.H, IntPtr.Zero, virtualScreen2.Left, virtualScreen2.Top, virtualScreen2.Width, virtualScreen2.Height, 20u);
			Settle();
			string text5 = ProbeOneGesture(dragFixture3, left: false);
			Line("    （全屏窗口·两个开关都关）" + text5);
			Check(WindowActions.IsFullscreen(dragFixture3.H) && text5.Contains("起会话=False"), "两个开关都关时全屏窗口不被接管（老行为保留）", text5);
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestAdoptHeldButton()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		Coordinator.AssumeButtonsDownForTest = true;
		try
		{
			using (DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
				c.ExitFullscreenOnTrigger = true;
				c.ShowActivationFrame = true;
			}))
			{
				Native.GetWindowRect(dragFixture.H, out var lpRect);
				Native.POINT p = CenterOf(dragFixture.H);
				// 真实光标必须与"按下点"一致：激活瞬间补起时以当前光标为起点（见 AdoptBufferedButtonsNow），
				// 光标若还停在上一个用例的位置，补起那一帧窗口就会跳。
				Native.GetCursorPos(out var cursorBefore);
				Native.SetCursorPos(p.X, p.Y);
				Pump();
				dragFixture.Coord.Trigger.SimulateKeyDown();
				Check(!dragFixture.Coord.Trigger.Active, "触发键尚未到达阈值（仍在按住阶段）");
				Check(dragFixture.Coord.Trigger.Held, "触发键处于『按住中』状态");
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				Check(dragFixture.Coord.OnMouse(513, ref ms), "★ 阈值期间的左键按下被吞掉（不再放行给下层窗口 —— 这正是『穿透点击』的根因）");
				Check(dragFixture.Coord.BufferedDownForTest(0), "★ 阈值期间的左键按下进入缓冲（未达阈值时既不投递也不丢弃，短按后还给用户）");
				Check(dragFixture.Coord.HeldDuringHoldForTest(0), "★ 按住阶段里的左键按下被【记账】（老代码在这里什么都没记，就是漏掉的根因）");
				dragFixture.Coord.Trigger.ForceHoldElapsed();
				Settle();
				Check(dragFixture.Coord.Trigger.Active, "触发键已激活（外框此刻会亮起）");
				Check(dragFixture.Coord.Drag.IsDragging, "★★ 激活【瞬间】就补起拖动 —— 阈值期间的按下从未投递给任何窗口，不必再等一次鼠标移动（用户报障『同时按 Enter 和左键后左键没反应』的修复点）");
				Native.GetWindowRect(dragFixture.H, out var lpAdopt);
				Eq(lpAdopt.Left - lpRect.Left, 0, "★ 补起那一帧窗口不跳（以当前光标为起点）");
				Eq(lpAdopt.Top - lpRect.Top, 0, "★ 补起那一帧窗口不跳（垂直）");
				Check(!dragFixture.Coord.PassedDownForTest(0), "按住阶段放行的那条 down 同样记入『幽灵按下』账，收养时已完成配对");
				for (int num = 0; num < 12; num++)
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				Native.GetWindowRect(dragFixture.H, out var lpRect2);
				Native.GetWindowRect(dragFixture.Coord.Drag.Frame.Handle, out var lpRect3);
				int num2 = (lpRect2.Top + lpRect2.Bottom) / 2;
				int num3 = BandMaxGreenness(Rectangle.FromLTRB(lpRect2.Left - 60, num2 - 20, lpRect2.Left + 60, num2 + 20));
				Line("    （拖动框）窗口=" + lpRect2.Left + "," + lpRect2.Top + " " + lpRect2.Width + "x" + lpRect2.Height + " 外框=" + lpRect3.Left + "," + lpRect3.Top + " " + lpRect3.Width + "x" + lpRect3.Height + " 屏幕青色度=" + num3);
				Check(num3 > 25, "★★ 拖动中的外框必须在屏幕上看得见（不是只有 IsShown）", "青色度=" + num3);
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 90, p.Y + 60));
				dragFixture.Coord.OnMouse(512, ref ms2);
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect4);
				Eq(lpRect4.Left - lpRect2.Left, 90, "★ 窗口真的跟着光标移动了（水平 90px）");
				Eq(lpRect4.Top - lpRect2.Top, 60, "★ 窗口真的跟着光标移动了（垂直 60px）");
				Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
				dragFixture.Coord.OnMouse(514, ref ms3);
				dragFixture.Coord.Trigger.SimulateKeyUp(null);
				Settle();
				Check(!dragFixture.Coord.Drag.IsDragging, "松开后拖动会话结束");
				Native.SetCursorPos(cursorBefore.X, cursorBefore.Y);
				Pump();
			}
			using DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
			});
			Native.POINT p2 = CenterOf(dragFixture2.H);
			Native.MSLLHOOKSTRUCT ms5 = Ms(p2);
			dragFixture2.Coord.OnMouse(513, ref ms5);
			Check(!dragFixture2.Coord.HeldDuringHoldForTest(0), "按下发生在触发键按住【之前】：『按住期间』记账不置位（两套账分工正确）");
			Check(dragFixture2.Coord.PassedDownForTest(0), "★ 但『幽灵按下』记账必须置位 —— 目标程序实收了这条 down，正停在拖选模态里");
			ActivateTrigger(dragFixture2.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms6 = Ms(new Native.POINT(p2.X + 70, p2.Y + 50));
			dragFixture2.Coord.OnMouse(512, ref ms6);
			Settle();
			Check(dragFixture2.Coord.Drag.IsDragging, "★ 第四十四轮：激活时仍按着的键现在必须补起（用户报障场景的修复点）");
			Native.MSLLHOOKSTRUCT ms7 = Ms(ms6.pt);
			dragFixture2.Coord.OnMouse(514, ref ms7);
			dragFixture2.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			Coordinator.AssumeButtonsDownForTest = false;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestAdoptAfterEarlyMove()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		Coordinator.AssumeButtonsDownForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
				c.ExitFullscreenOnTrigger = true;
				c.ShowActivationFrame = true;
				c.BringToFront = false;
			});
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.POINT p = CenterOf(dragFixture.H);
			Native.GetCursorPos(out var cursorBefore);
			Native.SetCursorPos(p.X, p.Y);
			Pump();
			dragFixture.Coord.Trigger.SimulateKeyDown();
			Check(dragFixture.Coord.Trigger.Held && !dragFixture.Coord.Trigger.Active, "触发键先在『按住』阶段（还没到阈值）");
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			Check(dragFixture.Coord.OnMouse(513, ref ms), "★ 阈值窗口内的左键按下被吞掉（不穿透到下层窗口）");
			Check(dragFixture.Coord.BufferedDownForTest(0), "★ 这次按下进入缓冲（从未投递给任何窗口）");
			// ★ 关键差别：激活之前鼠标已经在动了 —— 这就是用户"按下左键就开始拖"的真实时序。
			for (int i = 1; i <= 4; i++)
			{
				Native.MSLLHOOKSTRUCT msMove = Ms(new Native.POINT(p.X + i * 20, p.Y + i * 10));
				dragFixture.Coord.OnMouse(512, ref msMove);
				Thread.Sleep(5);
			}
			Check(!dragFixture.Coord.Trigger.Active, "移动这几帧时触发键还没到阈值（两次移动都落在按住阶段里）");
			Native.MSLLHOOKSTRUCT msAt = Ms(new Native.POINT(p.X + 80, p.Y + 40));
			dragFixture.Coord.OnMouse(512, ref msAt);
			// 真实光标也要挪到同一处：激活瞬间的起点取的是【当前光标】（子项真实位置），
			// 若真实光标还停在按下点，补起那一帧窗口会按"按下点 → 当前光标"的差值跳一段。
			Native.SetCursorPos(p.X + 80, p.Y + 40);
			Pump();
			dragFixture.Coord.Trigger.ForceHoldElapsed();
			Settle();
			Check(dragFixture.Coord.Trigger.Active, "触发键到阈值、激活（外框此刻亮起）");
			Line("    （诊断）激活瞬间：缓冲=" + dragFixture.Coord.BufferedDownForTest(0) + " 拖动中=" + dragFixture.Coord.Drag.IsDragging + " 光标=" + p.X + 80 + "," + p.Y + 40);
			Check(dragFixture.Coord.Drag.IsDragging, "★★ 阈值期间的按下 + 激活前鼠标已经在移动 → 激活后照样必须能拖（用户报障『外框被样式包围但窗口拖不动』的修复点）");
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 80 + 90, p.Y + 40 + 60));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			Eq(lpRect2.Left - lpRect.Left, 90, "★ 窗口跟着光标走了（水平 90px）");
			Eq(lpRect2.Top - lpRect.Top, 60, "★ 窗口跟着光标走了（垂直 60px）");
			Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
			dragFixture.Coord.OnMouse(514, ref ms3);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!dragFixture.Coord.Drag.IsDragging, "松开左键后拖动会话结束");
			Native.SetCursorPos(cursorBefore.X, cursorBefore.Y);
			Pump();

			// ---- 实测最凶的一档：GetAsyncKeyState 漏报（本轮报障的真凶）。----
			// 现场日志（21:07:47.635）：
			//   激活瞬间清点缓冲 缓冲[True,False,False] 阈值期按住[True,False,False]
			//                     已放行[False,False,False] 物理按下[False,False,False]
			// 左键明明按着（真实按下 47.473 收到、131ms 后才抬起），GetAsyncKeyState 却报 False。
			// 老代码信了这句谎话，在激活瞬间把【刚收到、还热乎的】缓冲按下当成"用户已松手"丢掉，
			// 之后每一次移动又被同一个查询挡回去 ⇒ 拖动永远建不起来，外框却照常亮着。
			// 这里用「按键状态覆盖表」原样复刻这个漏报：三个键一律报"没按着"。
			Coordinator.ButtonsDownOverrideForTest = new bool[3] { false, false, false };
			using (DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
				c.ShowActivationFrame = true;
				c.BringToFront = false;
			}))
			{
				Native.GetWindowRect(dragFixture2.H, out var lpRect3);
				Native.POINT p2 = CenterOf(dragFixture2.H);
				Native.GetCursorPos(out var cursorBefore2);
				Native.SetCursorPos(p2.X, p2.Y);
				Pump();
				dragFixture2.Coord.Trigger.SimulateKeyDown();
				Native.MSLLHOOKSTRUCT msDown = Ms(p2);
				Check(dragFixture2.Coord.OnMouse(513, ref msDown), "阈值窗口内的左键按下被吞掉（不穿透）");
				Check(dragFixture2.Coord.BufferedDownForTest(0), "★ 账本里确实有这条按下（钩子收到了，且没收到配对的抬起）");
				dragFixture2.Coord.Trigger.ForceHoldElapsed();
				Settle();
				Check(dragFixture2.Coord.Trigger.Active, "触发键已激活");
				Check(dragFixture2.Coord.Drag.IsDragging, "★★ 系统按键查询漏报（报『没按着』）时，账本里的按下绝不能被当作已松手丢弃 → 激活瞬间照样必须补起拖动");
				// 接管那一帧以"当前光标"为起点，所以窗口此刻不跳；再走一格才看得到跟手。
				Native.MSLLHOOKSTRUCT msFollow = Ms(new Native.POINT(p2.X + 90, p2.Y + 60));
				dragFixture2.Coord.OnMouse(512, ref msFollow);
				Settle();
				Native.GetWindowRect(dragFixture2.H, out var lpRect4);
				Eq(lpRect4.Left - lpRect3.Left, 90, "★ 窗口跟着光标走了（水平 90px）");
				Eq(lpRect4.Top - lpRect3.Top, 60, "★ 窗口跟着光标走了（垂直 60px）");
				dragFixture2.Coord.Trigger.SimulateKeyUp(null);
				Settle();
				Check(!dragFixture2.Coord.Drag.IsDragging, "松开触发键立刻收回拖动（抬起事件丢失时的兜底）");
				Native.SetCursorPos(cursorBefore2.X, cursorBefore2.Y);
				Pump();
			}
		}
		finally
		{
			Coordinator.ButtonsDownOverrideForTest = null;
			Coordinator.AssumeButtonsDownForTest = false;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestHookThreadLoggingIsAsync()
	{
		// 记录基线：只看"入队 vs 落盘"的增量，不依赖具体日志文件路径
		long num = Interlocked.Read(ref Log.AsyncEnqueuedForTest);
		long num2 = Interlocked.Read(ref Log.AsyncWrittenForTest);
		Thread thread = new Thread(() =>
		{
			for (int i = 0; i < 200; i++)
			{
				Log.Info("[自检] 钩子线程异步日志 " + i);
			}
		})
		{
			IsBackground = true,
			Name = "HoldDrag.HookThread"
		};
		long num3 = Environment.TickCount64;
		thread.Start();
		thread.Join(5000);
		long num4 = Environment.TickCount64 - num3;
		Check(!thread.IsAlive, "钩子线程写 200 行日志能在 5 秒内返回");
		Check(num4 < 3000, "★ 钩子线程写 200 行日志没有被磁盘 IO 拖住（实测 " + num4 + " ms）");
		long num5 = Interlocked.Read(ref Log.AsyncEnqueuedForTest) - num;
		Check(num5 >= 200, "★★ 钩子线程的 200 行日志确实进了异步队列（说明走的是不阻塞的那条路径）", "实测入队 " + num5 + " 行");
		// 队列清空 ≠ 已经落盘：后台线程可能正拿着这批数据在写。
		// 这里必须轮询"已写入"计数，而不是读一次就下结论（否则会假红）。
		long num6 = 0L;
		long num7 = Environment.TickCount64;
		while (Environment.TickCount64 - num7 < 3000)
		{
			num6 = Interlocked.Read(ref Log.AsyncWrittenForTest) - num2;
			if (num6 > 0)
			{
				break;
			}
			Thread.Sleep(25);
		}
		long num8 = Interlocked.Read(ref Log.AsyncBatchCallsForTest);
		string text = Log.AsyncDiagnosticsForTest;
		if (Log.AsyncWriteDeniedForTest)
		{
			// 宿主环境（沙箱 / 安全策略）不允许写 %APPDATA% 时，磁盘这一步必然失败。
			// 这是环境限制，不是产品缺陷：此时只跳过"落盘"这一条，其余断言照常生效。
			Line("    （跳过：本环境禁止子进程写 %APPDATA%，无法验证落盘；诊断 " + text + "）");
			return;
		}
		Check(num8 > 0, "★ 后台线程确实被调度起来处理了队列", "batch 调用 " + num8 + " 次");
		Check(num6 > 0, "★★ 队列里的日志最终确实由后台线程写进了文件（没有被丢掉）", "实测落盘批次 " + num6 + "；诊断 " + text);
		Check(Log.AsyncPendingForTest == 0, "★ 队列已被抽干，不残留积压", "剩余 " + Log.AsyncPendingForTest + " 行");
	}
	private static void TestPhantomDownAdoption()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		Coordinator.AssumeButtonsDownForTest = true;
		try
		{
			using (DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
				c.ExitFullscreenOnTrigger = true;
			}))
			{
				Native.GetWindowRect(dragFixture.H, out var lpRect);
				Native.POINT p = CenterOf(dragFixture.H);
				int cancelModeSentForTest = Coordinator.CancelModeSentForTest;
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				Check(!dragFixture.Coord.OnMouse(513, ref ms), "① 拖选文字的左键按下被原样放行（目标程序实收，进入拖选模态）");
				Check(dragFixture.Coord.PassedDownForTest(0), "★ 放行的按下记入『幽灵按下』账（本 bug 的根源量：不记账就永远想不起补配对）");
				Check(!dragFixture.Coord.HeldDuringHoldForTest(0), "触发键还没按住，『按住期间』记账不置位（两套账分工正确）");
				Check(!dragFixture.Coord.Drag.IsDragging, "此刻没有拖动会话（用户还在选文字）");
				ActivateTrigger(dragFixture.Coord);
				Check(dragFixture.Coord.Trigger.Active, "Enter 已激活");
				Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest, "★ 激活第一件事：向目标发 WM_CANCELMODE —— 先取消选中状态，再执行 Enter 的功能");
				Check(!dragFixture.Coord.Drag.IsDragging, "激活瞬间还不拖（补起放在第一次移动上做，那时才知道光标落在哪扇窗上）");
				Check(dragFixture.Coord.PassedDownForTest(0), "幽灵按下账还在（配对抬起由收养时补发）");
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 70, p.Y + 50));
				Check(!dragFixture.Coord.OnMouse(512, ref ms2), "拖动期间的移动绝不吞（吞了光标会冻结）");
				Check(dragFixture.Coord.Drag.IsDragging, "★★ 左键还按着就补起了拖动（修复点：以前这里永远补不起来，只剩拖选文字）");
				Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest + 1, "★ 收养时再次取消目标窗口的拖拽模态");
				Check(!dragFixture.Coord.PassedDownForTest(0), "配对后幽灵按下账已清");
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect2);
				Eq(lpRect2.Left - lpRect.Left, 0, "★ 补起那一帧窗口不跳（水平）");
				Eq(lpRect2.Top - lpRect.Top, 0, "★ 补起那一帧窗口不跳（垂直）");
				Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(p.X + 70 + 90, p.Y + 50 + 60));
				dragFixture.Coord.OnMouse(512, ref ms3);
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect3);
				Eq(lpRect3.Left - lpRect2.Left, 90, "★★ 窗口真的跟着左键移动了（不再是选中文字）水平 90px");
				Eq(lpRect3.Top - lpRect2.Top, 60, "★★ 窗口真的跟着左键移动了 垂直 60px");
				Native.MSLLHOOKSTRUCT ms4 = Ms(ms3.pt);
				Check(dragFixture.Coord.OnMouse(514, ref ms4), "拖动会话内的抬起被我们吞掉（配对已提前补发，不放幽灵出去）");
				Check(!dragFixture.Coord.Drag.IsDragging, "松开后拖动会话结束");
				dragFixture.Coord.Trigger.SimulateKeyUp(null);
				Settle();
			}
			using (DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
			}))
			{
				Native.POINT p2 = CenterOf(dragFixture2.H);
				Native.MSLLHOOKSTRUCT ms5 = Ms(p2);
				dragFixture2.Coord.OnMouse(513, ref ms5);
				Check(dragFixture2.Coord.PassedDownForTest(0), "② 按下被记账");
				ActivateTrigger(dragFixture2.Coord);
				Native.MSLLHOOKSTRUCT ms6 = Ms(p2);
				Check(!dragFixture2.Coord.OnMouse(514, ref ms6), "激活后直接松手：这次真实抬起原样放行（目标窗口自己完成配对）");
				Check(!dragFixture2.Coord.PassedDownForTest(0), "配对后记账清零（不会秋后补发骚扰消息）");
				Check(!dragFixture2.Coord.Drag.IsDragging, "没有会话残留");
				dragFixture2.Coord.Trigger.SimulateKeyUp(null);
				Settle();
			}
			int doubleClickTime = (int)Native.GetDoubleClickTime();
			if (doubleClickTime < 250)
			{
				Line("    （系统双击时间 " + doubleClickTime + "ms 太短，跳过『收养不误判双击』子用例）");
				return;
			}
			using DragFixture dragFixture3 = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = true;
				c.ExitFullscreenOnTrigger = true;
			});
			Native.POINT p3 = CenterOf(dragFixture3.H);
			ActivateTrigger(dragFixture3.Coord);
			Native.MSLLHOOKSTRUCT ms7 = Ms(p3);
			Check(dragFixture3.Coord.OnMouse(513, ref ms7), "会话一：按下被吞并建立会话");
			Native.MSLLHOOKSTRUCT ms8 = Ms(p3);
			dragFixture3.Coord.OnMouse(514, ref ms8);
			dragFixture3.Coord.Trigger.SimulateKeyUp(null);
			Check(!dragFixture3.Coord.Drag.IsDragging, "会话一已结束（_lastDown 已刷新）");
			Settle();
			Native.GetWindowRect(dragFixture3.H, out var lpRect4);
			Native.MSLLHOOKSTRUCT ms9 = Ms(p3);
			Check(!dragFixture3.Coord.OnMouse(513, ref ms9), "③ 选中文字的按下照常放行");
			Check(dragFixture3.Coord.PassedDownForTest(0), "③ 按下已记账");
			ActivateTrigger(dragFixture3.Coord);
			Native.MSLLHOOKSTRUCT ms10 = Ms(new Native.POINT(p3.X + 60, p3.Y + 40));
			dragFixture3.Coord.OnMouse(512, ref ms10);
			Check(dragFixture3.Coord.Drag.IsDragging, "★★ 收养不得被误判成双击最大化（_lastDown 是上一轮会话的残留，必须排除）");
			Native.MSLLHOOKSTRUCT ms11 = Ms(new Native.POINT(p3.X + 60 + 80, p3.Y + 40 + 50));
			dragFixture3.Coord.OnMouse(512, ref ms11);
			Settle();
			Native.GetWindowRect(dragFixture3.H, out var lpRect5);
			Eq(lpRect5.Left - lpRect4.Left, 80, "★★ 是在拖动而不是最大化：水平跟手 80px");
			Eq(lpRect5.Top - lpRect4.Top, 50, "★★ 是在拖动而不是最大化：垂直跟手 50px");
			Check(!Native.IsZoomed(dragFixture3.H), "窗口没有被误最大化");
			Native.MSLLHOOKSTRUCT ms12 = Ms(ms11.pt);
			dragFixture3.Coord.OnMouse(514, ref ms12);
			dragFixture3.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			Coordinator.AssumeButtonsDownForTest = false;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestPairedUpMaskReport()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			RunSwallowedButtonCase(0, new bool[3] { true, true, true }, 513, "收养左键：右/中键都还按着 → wParam=MkRM（自己已松开故不含左键）");
			RunSwallowedButtonCase(1, new bool[3] { true, true, false }, 516, "收养右键：左键还按着、中键已松 → wParam=MkL（如实带左键、不带中键）");
			RunSwallowedButtonCase(2, new bool[3] { false, false, true }, 519, "收养中键：其余键都没按 → wParam=0（不许把没按着的键报成按着）");
		}
		finally
		{
			Coordinator.ButtonsDownOverrideForTest = null;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestChainPriorityReinstall()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-chain-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		HookEngine hooks = null;
		HookWatchdog hookWatchdog = null;
		Coordinator coordinator = null;
		Form form = null;
		bool cursorPos = Native.GetCursorPos(out var lpPoint);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 400;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			hooks = new HookEngine(coordinator);
			hooks.Install();
			coordinator.ReinstallHooksForPriority = () =>
			{
				hooks.ReinstallMouseForChainPriority();
			};
			hookWatchdog = new HookWatchdog(hooks);
			hookWatchdog.Start();
			Pump();
			Settle();
			Check(hooks.MouseInstalled && hooks.KeyboardInstalled, "初始钩子安装成功", "错误码 " + hooks.LastError);
			int before = hooks.ReinstallCount;
			Native.POINT pt = new Native.POINT(400, 300);
			Native.SetCursorPos(pt.X, pt.Y);
			form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.TopMost = true;
			form.Bounds = new Rectangle(pt.X - 150, pt.Y - 100, 300, 200);
			form.Show();
			Pump();
			Settle();
			ActivateTrigger(coordinator);
			Check(PumpUntil(() => hooks.ReinstallCount > before, 2000), "激活触发了鼠标钩子链头抢占（ReinstallCount +1）", "重装计数 " + before + " → " + hooks.ReinstallCount + "（激活后 2 秒内没有发生就地重装）");
			Check(hooks.MouseInstalled && hooks.KeyboardInstalled, "抢占后两个钩子都还在（重装没有把自己弄丢）", "MouseInstalled=" + hooks.MouseInstalled + " KeyboardInstalled=" + hooks.KeyboardInstalled);
			WindowActions.AllowOwnProcessAsTargetForTest = true;
			WindowActions.ForcedTargetForTest = form.Handle;
			try
			{
				int sessionCount = coordinator.Drag.SessionCount;
				bool num = InjectAndWaitDrag(coordinator, 8u, 16u, "探测", sessionCount) || coordinator.Drag.SessionCount > sessionCount;
				CleanupSession(coordinator, 517, pt);
				if (!num)
				{
					Line("（环境限制）本环境下 SendInput 的按键事件不进入低级鼠标钩子，事件链路部分跳过；链头抢占本身已单独验证。");
					coordinator.Trigger.SimulateKeyUp(null);
					return;
				}
				ActivateTrigger(coordinator);
				int sessionCount2 = coordinator.Drag.SessionCount;
				Check(InjectAndWaitDrag(coordinator, 2u, 4u, "左键", sessionCount2), "抢占后左键仍能启动拖动会话", "会话数没增加（before=" + sessionCount2 + " now=" + coordinator.Drag.SessionCount + "）");
				CleanupSession(coordinator, 514, pt);
				ActivateTrigger(coordinator);
				int sessionCount3 = coordinator.Drag.SessionCount;
				Check(InjectAndWaitDrag(coordinator, 8u, 16u, "右键", sessionCount3), "抢占后右键仍能启动缩放会话（wgesture 抢占回归）", "会话数没增加（before=" + sessionCount3 + " now=" + coordinator.Drag.SessionCount + "）");
				CleanupSession(coordinator, 517, pt);
				Check(hooks.CallbackStalledMs < 500.0, "全程没有钩子回调卡死", "CallbackStalledMs=" + hooks.CallbackStalledMs.ToString("0"));
				coordinator.Trigger.SimulateKeyUp(null);
			}
			finally
			{
				WindowActions.ForcedTargetForTest = IntPtr.Zero;
				WindowActions.AllowOwnProcessAsTargetForTest = false;
			}
		}
		finally
		{
			try
			{
				hookWatchdog?.Stop();
			}
			catch
			{
			}
			try
			{
				hookWatchdog?.Dispose();
			}
			catch
			{
			}
			try
			{
				if (hooks != null)
				{
					hooks.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				coordinator?.Dispose();
			}
			catch
			{
			}
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
			try
			{
				if (cursorPos)
				{
					Native.SetCursorPos(lpPoint.X, lpPoint.Y);
				}
			}
			catch
			{
			}
		}
	}
	private static void TestHookThreadNeverBlocks()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		Coordinator coord = null;
		Form blocker = null;
		bool cursorPos = Native.GetCursorPos(out var lpPoint);
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "holddrag-blk-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(text);
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 400;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			coord = new Coordinator(configStore);
			coord.PrecreateOverlays();
			Native.POINT pOINT = new Native.POINT(400, 300);
			Native.SetCursorPos(pOINT.X, pOINT.Y);
			blocker = new BlockingForm(1500);
			blocker.FormBorderStyle = FormBorderStyle.Sizable;
			blocker.StartPosition = FormStartPosition.Manual;
			blocker.ShowInTaskbar = false;
			blocker.TopMost = true;
			blocker.Bounds = new Rectangle(200, 200, 320, 220);
			blocker.Show();
			Pump();
			Settle();
			Native.GetWindowRect(blocker.Handle, out var lpRect);
			Native.POINT outsidePt = new Native.POINT(700, 500);
			Native.RECT rect = new Native.RECT(lpRect.Left, lpRect.Top, lpRect.Right, lpRect.Bottom);
			long num = MeasureOnOtherThread(() =>
			{
				WindowActions.ExitFullscreenToWindowed(blocker.Handle, ref rect, outsidePt, (Action w) =>
				{
					coord.Drag.PostTargetWork(w);
				});
			});
			Check(num < 500, "钩子线程上全屏降级不阻塞目标（异步提交）", "耗时 " + num + "ms ≥ " + 500 + "ms：这条路径上还有同步跨进程写");
			Native.RECT expect1 = new Native.RECT(rect.Left, rect.Top, rect.Right, rect.Bottom);
			Check(PumpUntil(() =>
			{
				Native.GetWindowRect(blocker.Handle, out var lpRect3);
				return RectsEqual(lpRect3, expect1);
			}, 6000), "异步提交的降级最终真的落了位", "期望 " + expect1.Left + "," + expect1.Top + " 实际窗口仍在别处");
			bool topBefore = WindowActions.IsTopMost(blocker.Handle);
			long num2 = MeasureOnOtherThread(() =>
			{
				coord.Drag.PostTargetWork(() =>
				{
					WindowActions.ToggleTopMost(blocker.Handle);
				});
			});
			Check(num2 < 500, "钩子线程上置顶切换不阻塞目标（异步提交）", "耗时 " + num2 + "ms ≥ " + 500 + "ms：这条路径上还有同步跨进程写");
			Check(PumpUntil(() => WindowActions.IsTopMost(blocker.Handle) != topBefore, 3000), "置顶切换最终生效（异步写没有丢）");
			Native.GetWindowRect(blocker.Handle, out var lpRect2);
			Native.POINT grab = new Native.POINT(lpRect2.Right - 12, lpRect2.Bottom - 12);
			Native.POINT finalPt = new Native.POINT(grab.X + 60, grab.Y + 40);
			Native.RECT expectFinal = new Native.RECT(lpRect2.Left, lpRect2.Top, lpRect2.Right + 60, lpRect2.Bottom + 40);
			ActivateTrigger(coord);
			Settle();
			WindowActions.ForcedTargetForTest = blocker.Handle;
			int sessionCount = coord.Drag.SessionCount;
			long num3 = MeasureOnOtherThread(() =>
			{
				Native.MSLLHOOKSTRUCT ms = Ms(grab);
				coord.OnMouse(516, ref ms);
			});
			Check(num3 < 500, "钩子线程上 Begin 不阻塞目标", "耗时 " + num3 + "ms ≥ " + 500 + "ms");
			Check(coord.Drag.SessionCount > sessionCount, "缩放会话已经建立", "会话数 " + sessionCount + " → " + coord.Drag.SessionCount);
			long num4 = MeasureOnOtherThread(() =>
			{
				Native.MSLLHOOKSTRUCT ms = Ms(finalPt);
				coord.OnMouse(512, ref ms);
			});
			Check(num4 < 500, "拖动中的移动事件不阻塞钩子线程（慢目标由工作线程消化）", "耗时 " + num4 + "ms ≥ " + 500 + "ms：移动还在钩子线程上同步等目标");
			Native.SetCursorPos(finalPt.X, finalPt.Y);
			long num5 = MeasureOnOtherThread(() =>
			{
				Native.MSLLHOOKSTRUCT ms = Ms(finalPt);
				coord.OnMouse(517, ref ms);
			});
			Check(num5 < 500, "★ 松手（End）不阻塞钩子线程 —— 用户报障'死机'的原路径 ★", "耗时 " + num5 + "ms ≥ " + 500 + "ms：End 还在钩子线程上同步写窗口");
			Check(!coord.Drag.IsDragging, "松开右键后缩放会话结束");
			Check(PumpUntil(() =>
			{
				Native.GetWindowRect(blocker.Handle, out var lpRect3);
				return RectsEqual(lpRect3, expectFinal);
			}, 8000), "阻塞目标上松手后仍精确落位（最终写由拖动工作线程完成）", "期望 " + expectFinal.Left + "," + expectFinal.Top + " " + expectFinal.Width + "x" + expectFinal.Height + "（右下各外扩 60/40）");
			coord.Trigger.SimulateKeyUp(null);
		}
		finally
		{
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
			try
			{
				if (coord != null)
				{
					coord.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				if (blocker != null && !blocker.IsDisposed)
				{
					blocker.Visible = false;
					Native.DestroyWindow(blocker.Handle);
				}
				if (blocker != null)
				{
					blocker.Close();
					blocker.Dispose();
				}
			}
			catch
			{
			}
			for (int num6 = 0; num6 < 6; num6++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			try
			{
				if (cursorPos)
				{
					Native.SetCursorPos(lpPoint.X, lpPoint.Y);
				}
			}
			catch
			{
			}
		}
	}
}
