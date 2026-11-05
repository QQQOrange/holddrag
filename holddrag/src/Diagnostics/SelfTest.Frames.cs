// HoldDrag · Diagnostics/SelfTest.Frames.cs

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
	private static void TestActivationFrameOverlay()
	{
		using ActivationFrame activationFrame = new ActivationFrame();
		Log.Trace("[自检] T24 EnsureCreated 之前");
		activationFrame.EnsureCreated();
		Log.Trace("[自检] T24 EnsureCreated 之后 created=" + activationFrame.IsCreated);
		if (!activationFrame.IsCreated)
		{
			Check(ok: false, "外框窗口创建成功", "CreateWindowEx 失败");
			return;
		}
		Native.RECT rECT = new Native.RECT(180, 140, 480, 400);
		activationFrame.SetRect(rECT);
		Log.Trace("[自检] T24 SetRect 之后");
		Settle();
		Log.Trace("[自检] T24 Settle 之后");
		Native.GetWindowRect(activationFrame.Handle, out var lpRect);
		bool ok = lpRect.Left == rECT.Left && lpRect.Top == rECT.Top && lpRect.Width == rECT.Width && lpRect.Height == rECT.Height;
		Native.RECT rECT2 = rECT;
		string text = rECT2.ToString();
		rECT2 = lpRect;
		Check(ok, "外框被摆到了请求的矩形", "期望 " + text + "，实际 " + rECT2.ToString());
		Check(Native.IsWindowVisible(activationFrame.Handle), "外框当前可见");
		activationFrame.HideOverlay();
		Settle();
		Check(!Native.IsWindowVisible(activationFrame.Handle), "拖动结束后外框不再可见");
		Check(FrameStyles.MarginFor("Glow", 3) >= 8, "柔光样式留出了足够的外扩边距（实际 " + FrameStyles.MarginFor("Glow", 3) + "px）");
		Check(FrameStyles.MarginFor("Hairline", 3) < FrameStyles.MarginFor("Glow", 3), "细线环的留白最小（不为不存在的光晕浪费面积）");
		Check(FrameStyles.MarginFor("Focus", 8) >= 8, "焦点环越粗，留白越大");
		Eq((int)FrameStyles.ClampRadius(999, 40f, 40f), 19, "离谱的圆角被夹到短边一半以内");
		Eq((int)FrameStyles.ClampRadius(0, 40f, 40f), 0, "圆角 0 就是直角，不被当成非法值");
	}
	private static void TestFrameNeverSticks()
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
			Native.POINT pOINT = CenterOf(dragFixture.H);
			Native.SetCursorPos(pOINT.X, pOINT.Y);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "触发键刚到阈值、还没按鼠标键时，光标所在窗口已经亮起外框");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(513, ref ms);
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(ms.pt.X + 40, ms.pt.Y + 30));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
			dragFixture.Coord.OnMouse(514, ref ms3);
			Settle();
			Check(!drag.IsDragging, "拖动会话已结束");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "松开鼠标但触发键仍按着时外框继续亮着");
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!dragFixture.Coord.Trigger.Active, "触发键已退出激活态");
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "松开触发键后外框立即消失（不再残留在屏幕上）");
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			bool flag = drag.TryShowFrame(lpRect, dragFixture.Store.Current, -1);
			Settle();
			Check(!flag, "过期代次的显框请求被挡下（不会画出关不掉的外框）");
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "被挡下之后外框仍然不可见");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestFramePersistsAfterAction()
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
				c.DoubleClickLeftMaximize = true;
				c.ActionMiddle = "Center";
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.POINT p = CenterOf(dragFixture.H);
			Native.SetCursorPos(p.X, p.Y);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "激活后外框已亮起");
			Check(!drag.IsDragging, "此刻还没进入拖动");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			bool ok = dragFixture.Coord.OnMouse(519, ref ms);
			Settle();
			Check(ok, "激活态下中键按下被吞掉（不会漏给目标窗口）");
			Check(!drag.IsDragging, "一次性动作不会进入拖动会话");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "执行完一次性动作之后外框继续亮着（外框消失回归）");
			Native.MSLLHOOKSTRUCT ms2 = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(520, ref ms2);
			Settle();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "松开鼠标键后外框依然亮着");
			Native.SetCursorPos(p.X, p.Y);
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms3);
			dragFixture.Coord.OnMouse(514, ref ms3);
			Settle();
			Native.MSLLHOOKSTRUCT ms4 = Ms(p);
			Check(dragFixture.Coord.OnMouse(513, ref ms4), "双击左键第二次按下被吞掉（走最大化路径）");
			dragFixture.Coord.OnMouse(514, ref ms4);
			SettleLong();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "双击左键最大化之后外框继续亮着", "游戏手柄=" + ((IntPtr)drag.Frame.Handle).ToInt64().ToString("X") + " 可见=" + Native.IsWindowVisible(drag.Frame.Handle));
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!dragFixture.Coord.Trigger.Active, "触发键已退出激活态");
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "松开触发键后外框立即消失（外框残留回归）");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestFrameTheme()
	{
		AppConfig appConfig = new AppConfig();
		Eq(appConfig.FrameColorId, "Teal", "默认外框颜色是薄荷青（不是 Windows 那个蓝）");
		Eq(appConfig.FrameStyleId, "Dashed", "默认外框样式是虚线环");
		Eq(appConfig.FrameThickness, 3, "默认外框粗细 3px");
		Eq(appConfig.FrameRadius, 13, "默认圆角 13px");
		Check(FrameColors.IsValid("Teal"), "薄荷青是合法颜色 id");
		Check(!FrameColors.IsValid("NotAColor"), "非法颜色 id 会被认出来");
		Check(FrameStyles.IsValid("Dashed"), "虚线环是合法样式 id");
		Check(!FrameStyles.IsValid("NotAStyle"), "非法样式 id 会被认出来");
		appConfig.FrameColorId = "???";
		appConfig.FrameStyleId = "???";
		appConfig.FrameThickness = 999;
		appConfig.FrameRadius = -5;
		appConfig.Normalize();
		Eq(appConfig.FrameColorId, "Teal", "非法颜色被拉回默认");
		Eq(appConfig.FrameStyleId, "Dashed", "非法样式被拉回默认");
		Eq(appConfig.FrameThickness, 8, "超范围粗细被夹到上限 8");
		Eq(appConfig.FrameRadius, 0, "负数圆角被夹到 0");
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			Check(FrameColors.Resolve(FrameColors.All[i].Id).A == byte.MaxValue, "颜色「" + FrameColors.All[i].Name + "」解析成功且不透明");
		}
	}
	private static void TestFrameInterval()
	{
		AppConfig appConfig = new AppConfig();
		Eq(appConfig.FrameIntervalMs(60), 17, "自动 60Hz → 约 17ms 一帧");
		Eq(appConfig.FrameIntervalMs(144), 7, "自动 144Hz → 约 7ms 一帧");
		Eq(appConfig.FrameIntervalMs(0), 0, "实测不到刷新率时不节流");
		Eq(appConfig.FrameIntervalMs(600), 2, "自动模式超过 500Hz 按 500 收敛");
		appConfig.DragRefreshMode = "Custom";
		appConfig.DragRefreshHz = 120;
		Eq(appConfig.FrameIntervalMs(240), 8, "固定 120Hz → 8ms（忽略实测值）");
		appConfig.DragRefreshHz = 0;
		Eq(appConfig.FrameIntervalMs(240), 0, "固定值选 0 = 用户主动选择不限制");
		appConfig.DragRefreshHz = 20;
		Eq(appConfig.FrameIntervalMs(0), 50, "固定值下限 20Hz → 50ms");
		appConfig.DragRefreshHz = 500;
		Eq(appConfig.FrameIntervalMs(0), 2, "固定值上限 500Hz → 2ms");
		appConfig.DragRefreshHz = 600;
		Eq(appConfig.FrameIntervalMs(0), 2, "固定值越界按 500 收敛 → 2ms");
	}
	private static void TestCoordinateSpace()
	{
		double scale = CoordSpace.Scale;
		CoordSpace.SetScaleForTest(1.0);
		Eq(CoordSpace.ToProcess(new Native.POINT(2182, 1440)).X, 2182, "缩放=1 时坐标原样通过（恒等变换）");
		CoordSpace.SetScaleForTest(0.5);
		Native.POINT pOINT = CoordSpace.ToProcess(new Native.POINT(2182, 1440));
		Eq(pOINT.X, 1091, "缩放=0.5 时物理坐标换算到逻辑空间（X）");
		Eq(pOINT.Y, 720, "缩放=0.5 时物理坐标换算到逻辑空间（Y）");
		CoordSpace.SetScaleForTest(scale);
		int num = -1;
		try
		{
			num = Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext());
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		if (num == 0)
		{
			Check(Math.Abs(CoordSpace.Scale - 1.0) > 0.0001, "★ DPI 不感知进程必须算出坐标换算系数（否则命中测试必然失败）", "Scale=" + CoordSpace.Scale + "（等于 1 说明没算出来）");
		}
		else
		{
			Eq(CoordSpace.Scale, 1.0, "DPI 感知进程的坐标换算是恒等变换");
		}
		if (!NoRoomForTestWindow())
		{
			return;
		}
		Native.GetCursorPos(out var lpPoint);
		Native.RECT rECT = WorkAreaOfNearestMonitor(lpPoint);
		Form form = null;
		try
		{
			form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.Text = "HoldDrag T46 coordspace";
			form.TopMost = true;
			form.Bounds = new Rectangle(rECT.Left + 40, rECT.Top + 40, 320, 220);
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
			if (!Native.GetWindowRect(form.Handle, out var lpRect))
			{
				Line("  （取不到窗口矩形，跳过端到端部分）");
				return;
			}
			int num2 = (lpRect.Left + lpRect.Right) / 2;
			int num3 = (lpRect.Top + lpRect.Bottom) / 2;
			double num4 = CoordSpace.Scale;
			if (num4 <= 0.0)
			{
				num4 = 1.0;
			}
			Native.POINT pt = new Native.POINT((int)Math.Round((double)num2 / num4), (int)Math.Round((double)num3 / num4));
			Native.POINT pt2 = CoordSpace.ToProcess(pt);
			Check(Math.Abs(pt2.X - num2) <= 1 && Math.Abs(pt2.Y - num3) <= 1, "物理坐标经换算能还原成逻辑坐标（往返一致）", "逻辑=" + num2 + "," + num3 + " 物理=" + pt.X + "," + pt.Y + " 还原=" + pt2.X + "," + pt2.Y + " 缩放=" + num4.ToString("0.####"));
			nint num5 = WindowActions.FindTarget(pt2, allowFullscreen: true);
			Check(num5 == form.Handle, "★ 物理坐标经换算后能命中目标窗口（核心功能失效回归）", "命中=" + DescribeWindow(num5) + " 期望=" + DescribeWindow(form.Handle) + " 缩放=" + num4.ToString("0.####"));
			if (num4 != 1.0)
			{
				nint num6 = WindowActions.FindTarget(pt, allowFullscreen: true);
				Line("    （对照）不换算直接用物理坐标 -> " + ((num6 == form.Handle) ? "竟然也命中了" : "命不中，与用户日志一致"));
			}
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
			for (int i = 0; i < 6; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
		}
	}
	private static void TestHitTestHasFallback()
	{
		Eq(2, 2, "GW_HWNDNEXT 是 2（Z 序往下，穿透循环必须用它）");
		Eq(3, 3, "GW_HWNDPREV 是 3（Z 序往上，不是穿透要的方向）");
		if (!NoRoomForTestWindow())
		{
			return;
		}
		Native.GetCursorPos(out var lpPoint);
		Native.RECT rECT = WorkAreaOfNearestMonitor(lpPoint);
		Form form = null;
		try
		{
			int width = 300;
			int height = 200;
			int x = rECT.Left + 40;
			int y = rECT.Top + 40;
			form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.Text = "HoldDrag T45 hittest";
			form.TopMost = true;
			form.Bounds = new Rectangle(x, y, width, height);
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
			Native.POINT pt = CenterOf(form.Handle);
			nint num = WindowActions.HitTestTopWindow(pt, out var how);
			Check(num == form.Handle, "命中测试（正常路径）能拿到测试窗口", "命中=" + DescribeWindow(num) + " 方式=" + how);
			WindowActions.SkipWindowFromPointForTest = true;
			try
			{
				nint num2 = WindowActions.HitTestTopWindow(pt, out var how2);
				Check(num2 == form.Handle, "★ WindowFromPoint 失效时兜底策略仍能命中（核心功能失效回归）", "命中=" + DescribeWindow(num2) + " 方式=" + how2);
				Check(how2 != "WindowFromPoint", "兜底确实走的是非 WindowFromPoint 路径（否则这个用例等于没测）", "方式=" + how2);
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
			for (int i = 0; i < 6; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
		}
	}
	private static void TestFullscreenOptIn()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		nint forcedTargetForTest = WindowActions.ForcedTargetForTest;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(new Native.POINT((lpRect.Left + lpRect.Right) / 2, (lpRect.Top + lpRect.Bottom) / 2));
			if (monitorInfoAt.rcMonitor.Width <= 0)
			{
				Line("  （取不到显示器矩形，跳过）");
				return;
			}
			Native.SetWindowPos(dragFixture.H, IntPtr.Zero, monitorInfoAt.rcMonitor.Left, monitorInfoAt.rcMonitor.Top, monitorInfoAt.rcMonitor.Width, monitorInfoAt.rcMonitor.Height, 16404u);
			Settle();
			Check(WindowActions.IsFullscreen(dragFixture.H), "铺满显示器被判定为真全屏");
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			dragFixture.Store.Current.AllowFullscreenWindows = false;
			Native.POINT pt = new Native.POINT(monitorInfoAt.rcMonitor.Left + monitorInfoAt.rcMonitor.Width / 2, monitorInfoAt.rcMonitor.Top + monitorInfoAt.rcMonitor.Height / 2);
			nint num = WindowActions.FindTarget(pt, allowFullscreen: false);
			Check(num == IntPtr.Zero || num != dragFixture.H, "默认不放行全屏窗口");
			nint num2 = WindowActions.FindTarget(pt, allowFullscreen: true);
			if (num2 == IntPtr.Zero)
			{
				Check(ok: false, "开关打开之后可以拖动全屏窗口（一个窗口都没命中）");
			}
			else if (num2 != dragFixture.H)
			{
				Line("  （命中其它窗口 " + ((IntPtr)num2).ToInt64().ToString("X") + "（" + (Native.GetClassName(num2) ?? "?") + "），疑似第三方置顶小组件遮挡，跳过正向断言）");
			}
			else
			{
				Check(ok: true, "开关打开之后可以拖动全屏窗口");
			}
		}
		finally
		{
			WindowActions.ForcedTargetForTest = forcedTargetForTest;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestFrameGoesAwayOnRelease()
	{
		Type typeFromHandle = typeof(DragEngine);
		string[] array = new string[3] { "PinToWindow", "TickPinned", "ClearPin" };
		int num = 0;
		string text = "";
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			if (typeFromHandle.GetMethod(text2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
			{
				num++;
				text = text + text2 + " ";
			}
		}
		Check(num == 0, "外框 pin 机制已彻底下线（PinToWindow/TickPinned/ClearPin 不存在）", text);
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using (DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
			}))
			{
				DragEngine drag = dragFixture.Coord.Drag;
				Native.POINT pOINT = CenterOf(dragFixture.H);
				Native.SetCursorPos(pOINT.X, pOINT.Y);
				ActivateTrigger(dragFixture.Coord);
				Settle();
				Check(Native.IsWindowVisible(drag.Frame.Handle), "激活后外框已亮起");
				dragFixture.Coord.Trigger.SimulateKeyUp(null);
				Settle();
				Check(!dragFixture.Coord.Trigger.Active, "触发键已退出激活态");
				Check(!Native.IsWindowVisible(drag.Frame.Handle), "★ 松开触发键后外框立即消失（不再残留在屏幕上）");
			}
			using (DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
			}))
			{
				DragEngine drag2 = dragFixture2.Coord.Drag;
				Native.POINT p = CenterOf(dragFixture2.H);
				Native.SetCursorPos(p.X, p.Y);
				ActivateTrigger(dragFixture2.Coord);
				Settle();
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				dragFixture2.Coord.OnMouse(513, ref ms);
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 50, p.Y + 30));
				dragFixture2.Coord.OnMouse(512, ref ms2);
				Settle();
				Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
				dragFixture2.Coord.OnMouse(514, ref ms3);
				Settle();
				Check(!drag2.IsDragging, "拖动会话已结束");
				Check(Native.IsWindowVisible(drag2.Frame.Handle), "触发键仍按着时外框继续亮着（这条行为没变）");
				dragFixture2.Coord.Trigger.SimulateKeyUp(null);
				Settle();
				Check(!Native.IsWindowVisible(drag2.Frame.Handle), "★ 先松鼠标再松触发键：松键那一刻外框消失");
			}
			using DragFixture dragFixture3 = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
			});
			DragEngine drag3 = dragFixture3.Coord.Drag;
			Native.POINT p2 = CenterOf(dragFixture3.H);
			Native.SetCursorPos(p2.X, p2.Y);
			ActivateTrigger(dragFixture3.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms4 = Ms(p2);
			dragFixture3.Coord.OnMouse(513, ref ms4);
			Native.MSLLHOOKSTRUCT ms5 = Ms(new Native.POINT(p2.X + 50, p2.Y + 30));
			dragFixture3.Coord.OnMouse(512, ref ms5);
			Settle();
			dragFixture3.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Native.MSLLHOOKSTRUCT ms6 = Ms(ms5.pt);
			dragFixture3.Coord.OnMouse(514, ref ms6);
			Settle();
			Check(!drag3.IsDragging, "拖动会话已结束（先松触发键）");
			Check(!Native.IsWindowVisible(drag3.Frame.Handle), "★ 先松触发键再松鼠标：松鼠标之后外框也不得回来");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestFramePaletteExpanded()
	{
		Check(FrameColors.All.Length >= 16, "外框颜色至少 16 种（#37 扩充）", "实际 " + FrameColors.All.Length);
		Check(FrameStyles.All.Length >= 5, "外框样式至少 5 种（精简后：虚线/细线/焦点/柔光/投影）", "实际 " + FrameStyles.All.Length);
		HashSet<string> hashSet = new HashSet<string>();
		int num = 0;
		string text = "";
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			FrameColors.Entry entry = FrameColors.All[i];
			if (string.IsNullOrEmpty(entry.Id) || string.IsNullOrEmpty(entry.Name))
			{
				num++;
				text += "(空项) ";
				continue;
			}
			if (!hashSet.Add(entry.Id))
			{
				num++;
				text = text + entry.Id + "(ID重复) ";
			}
			if (!FrameColors.IsValid(entry.Id))
			{
				num++;
				text = text + entry.Id + "(非法) ";
			}
			if (FrameColors.Resolve(entry.Id).A != byte.MaxValue)
			{
				num++;
				text = text + entry.Id + "(非不透明) ";
			}
		}
		Check(num == 0, "每个颜色的 ID 唯一、名字非空、解析为不透明色", text);
		int num2 = 0;
		string text2 = "";
		int num3 = FrameStyles.MarginFor("Hairline", 3);
		for (int j = 0; j < FrameStyles.All.Length; j++)
		{
			string text3 = FrameStyles.All[j];
			string text4 = FrameStyles.DisplayName(text3);
			if (string.IsNullOrEmpty(text4) || text4 == text3)
			{
				num2++;
				text2 = text2 + text3 + "(没有中文名) ";
			}
			if (FrameStyles.MarginFor(text3, 3) < 4)
			{
				num2++;
				text2 = text2 + text3 + "(留白太小) ";
			}
			if (text3 != "Hairline" && FrameStyles.MarginFor(text3, 3) < num3)
			{
				num2++;
				text2 = text2 + text3 + "(留白小于纯净环) ";
			}
		}
		Check(num2 == 0, "每个样式都有中文名、且留白足够放下光晕", text2);
		int num4 = 0;
		string text5 = "";
		string[] all = FrameStyles.All;
		foreach (string text6 in all)
		{
			try
			{
				using Bitmap bitmap = new Bitmap(240, 160);
				using Graphics graphics = Graphics.FromImage(bitmap);
				graphics.Clear(Color.White);
				FrameStyles.Draw(graphics, new RectangleF(0f, 0f, 240f, 160f), FrameColors.Resolve("Teal"), text6, 3, 12);
				int num5 = 0;
				for (int l = 0; l < bitmap.Height; l += 2)
				{
					for (int m = 0; m < bitmap.Width; m += 2)
					{
						if (bitmap.GetPixel(m, l).ToArgb() != Color.White.ToArgb())
						{
							num5++;
						}
					}
				}
				if (num5 < 4)
				{
					num4++;
					text5 = text5 + text6 + "(只落了 " + num5 + " 个像素) ";
				}
			}
			catch (Exception ex)
			{
				num4++;
				text5 = text5 + text6 + "(异常 " + ex.GetType().Name + ") ";
			}
		}
		Check(num4 == 0, "★ 每一种外框样式都能画出来（不抛异常、确实落了像素）", text5);
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		int num6 = 0;
		string text7 = "";
		all = FrameStyles.All;
		foreach (string text8 in all)
		{
			settingsForm.ProbeSelectFrameStyle(text8);
			SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
			if (frameSelection.StyleId != text8 || frameSelection.PreviewStyleId != text8)
			{
				num6++;
				text7 = text7 + text8 + "→" + frameSelection.StyleId + "/" + frameSelection.PreviewStyleId + " ";
			}
		}
		Check(num6 == 0, "★ 每一种样式都能在设置页选中并同步到预览", text7);
	}
	private static void TestFrameColorAccentAndCustom()
	{
		Color accent = FrameColors.Resolve("Accent");
		AppConfig appConfig = new AppConfig();
		appConfig.FrameColorId = "Red";
		appConfig.FrameColorFollowAccent = true;
		Eq(appConfig.EffectiveFrameColor.ToArgb(), accent.ToArgb(), "★ 开了「跟随系统强调色」之后，最终颜色就是强调色");
		appConfig.FrameColorFollowAccent = false;
		Eq(appConfig.EffectiveFrameColor.ToArgb(), FrameColors.Resolve("Red").ToArgb(), "★ 关掉跟随之后回落到调色板里的颜色");
		appConfig.FrameCustomColor = Color.FromArgb(18, 52, 86).ToArgb();
		Eq(appConfig.EffectiveFrameColor.ToArgb(), Color.FromArgb(18, 52, 86).ToArgb(), "★ 设了自定义颜色就优先用自定义色");
		appConfig.FrameColorFollowAccent = true;
		Eq(appConfig.EffectiveFrameColor.ToArgb(), accent.ToArgb(), "两个都开着时强调色优先（不会两个设置互相打架）");
		AppConfig appConfig2 = new AppConfig();
		appConfig2.FrameCustomColor = 16777215;
		appConfig2.Normalize();
		Eq(appConfig2.FrameCustomColor, 0, "透明色的自定义值会被归一化成「没设过」");
		using (DragFixture dragFixture = new DragFixture(null))
		{
			using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
			settingsForm.CreateControl();
			settingsForm.ProbeSetFollowAccent(on: true);
			SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
			Check(frameSelection.FollowAccent, "设置页上「跟随强调色」是打开的");
			Eq(frameSelection.EffectiveColorHex, FrameColors.HexOf(accent), "★ 预览框用的颜色跟着变成强调色");
			settingsForm.ProbeSetCustomColor(Color.FromArgb(34, 136, 68).ToArgb());
			SettingsForm.FrameSelection frameSelection2 = settingsForm.ProbeFrameSelection();
			Check(!frameSelection2.FollowAccent, "选自定义色会自动关掉「跟随强调色」（否则点了像没反应）");
			Eq(frameSelection2.EffectiveColorHex, "#228844", "★ 预览框用的颜色换成自定义色");
			Check(frameSelection2.ColorNameText.Contains("#228844"), "界面上直接写出了自定义颜色的色号", frameSelection2.ColorNameText);
			bool flag = false;
			bool flag2 = false;
			Walk(settingsForm, delegate(Control control)
			{
				if (control is CheckBox checkBox && checkBox.Text.Contains("跟随系统强调色") && checkBox.Width > 0)
				{
					flag = true;
				}
				if (control is Button button && button.Text.Contains("自定义颜色") && button.Width > 0)
				{
					flag2 = true;
				}
			});
			Check(flag, "★ 「跟随系统强调色」开关真的在界面上（不是只改了配置）");
			Check(flag2, "★ 「自定义颜色…」按钮真的在界面上");
			AppConfig appConfig3 = new AppConfig();
			settingsForm.ProbeCaptureInto(appConfig3);
			Eq(appConfig3.FrameCustomColor, Color.FromArgb(34, 136, 68).ToArgb(), "★ 自定义颜色真的写进了配置");
			Check(!appConfig3.FrameColorFollowAccent, "跟随强调色的状态也写进了配置");
		}
		using ActivationFrame activationFrame = new ActivationFrame();
		activationFrame.EnsureCreated();
		if (activationFrame.IsCreated)
		{
			Color color = Color.FromArgb(34, 136, 68);
			activationFrame.ApplyTheme("Teal", "Dashed", 3, 13, 100, color);
			Check(activationFrame.ColorOverrideForTest.HasValue && activationFrame.ColorOverrideForTest.Value.ToArgb() == color.ToArgb(), "★ 浮层记住了覆盖色（强调色/自定义色走的就是这条路）");
			activationFrame.HideOverlay();
		}
	}
	private static void TestFrameRadiusTakesEffect()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.ProbeSetFrameShape(3, 4, enabled: true);
		int num = SettingsForm.ProbePreviewRadiusDiff(settingsForm, 2, 26);
		Line("    （预览像素差异）圆角 2 vs 26 = " + num + " 像素");
		Check(num > 40, "★ 改「圆角」之后预览真的跟着变（不是只改了个数字）", "差异 " + num + " 像素");
		SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
		Eq(frameSelection.PreviewRadius, 26, "预览框拿到的是新圆角值 26");
		settingsForm.ProbeSetFrameShape(3, 12, enabled: true);
		Eq(settingsForm.ProbeFrameSelection().Radius, 12, "设置页读回的圆角是 12");
		settingsForm.Close();
	}
	private static void TestFramelessCardWindow()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(40, 40);
		settingsForm.Show();
		Application.DoEvents();
		Native.RECT rECT;
		Native.GetWindowRect(settingsForm.Handle, out rECT);
		int num = rECT.Bottom - rECT.Top - settingsForm.ClientSize.Height;
		Line("    （窗口高=" + (rECT.Bottom - rECT.Top) + " 客户区高=" + settingsForm.ClientSize.Height + "）");
		Check(num <= 6, "★ 系统标题栏已被去掉（客户区高度≈窗口高度，最上面那条是自己画的）", "差值 " + num + "px");
		int windowIntAttribute = Native.GetWindowIntAttribute(settingsForm.Handle, 33);
		if (Environment.OSVersion.Version.Build >= 22000)
		{
			Eq(windowIntAttribute, 2, "★ 整窗启用了 DWM 圆角（不再是一整块直角矩形）");
		}
		else
		{
			Line("    （系统低于 Win11，圆角由系统决定）corner=" + windowIntAttribute);
		}
		Eq(settingsForm.ProbeCaptionButtonCount(), 3, "★ 最小化/最大化/关闭三个按钮都在标题条上");
		Color color = settingsForm.ProbeTitleStripColor();
		Eq(color.ToArgb(), settingsForm.BackColor.ToArgb(), "★ 标题条颜色就是窗口底色（同一套颜色，不会出现最上面一条不一样）");
		settingsForm.Close();
	}
	private static void TestFramePickersAlwaysVisible()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		SettingsForm.FramePickerProbe framePickerProbe = settingsForm.ProbeFramePickers();
		Eq(framePickerProbe.ColorCells, FrameColors.All.Length, "色板把调色板里的颜色全部平铺出来（进入即可见）");
		Eq(framePickerProbe.StyleCells, FrameStyles.All.Length, "样式画廊把全部样式平铺出来（进入即可见）");
		Check(framePickerProbe.GridInk > 100, "色板真的画出了色块", "非背景像素 " + framePickerProbe.GridInk + " 尺寸 " + framePickerProbe.GridSize);
		Check(framePickerProbe.GalleryInk > 300, "样式画廊真的画出了缩略图", "非背景像素 " + framePickerProbe.GalleryInk + " 尺寸 " + framePickerProbe.GallerySize);
		Check(framePickerProbe.GridAccentInk > 10, "★ 色板里「选中的那一格」有可见的强调色标记", "强调色像素 " + framePickerProbe.GridAccentInk);
		Check(framePickerProbe.GalleryAccentInk > 10, "★ 样式画廊里选中的那一格有可见的强调色标记", "强调色像素 " + framePickerProbe.GalleryAccentInk);
		Check(framePickerProbe.GridSelectedCell.Width > 0 && framePickerProbe.GallerySelectedCell.Width > 0, "选中的色块格 / 样式格都能定位到（选中态不是空）");
		settingsForm.ProbeSelectFrameStyle("Focus");
		SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
		Eq(frameSelection.GridStyleId, "Focus", "★ 选了「焦点环」之后，画廊里高亮的就是焦点环");
		Eq(frameSelection.GridColorId, frameSelection.ColorId, "色板高亮与当前颜色一致");
		settingsForm.ProbeSelectFrameColor("Gold");
		SettingsForm.FrameSelection frameSelection2 = settingsForm.ProbeFrameSelection();
		string goldName = FrameColors.All.First((FrameColors.Entry e) => e.Id == "Gold").Name;
		Eq(frameSelection2.GridColorId, "Gold", "★ 选了「" + goldName + "」之后，色板里高亮的就是它");
		Check(!string.IsNullOrEmpty(frameSelection2.ColorNameText) && frameSelection2.ColorNameText.Contains(goldName), "界面上写明了当前颜色的名字（状态清晰）", "文字=" + frameSelection2.ColorNameText);
		int[] array = new int[6] { 736, 900, 1180, 1516, 1900, 2600 };
		int num = 0;
		string text = "";
		int[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			int width = array2[i];
			settingsForm.ClientSize = new Size(width, 900);
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
			SettingsForm.FramePickerProbe framePickerProbe2 = settingsForm.ProbeFramePickers();
			if (framePickerProbe2.GridSize.Width <= 100 || framePickerProbe2.GridSize.Height <= 10 || framePickerProbe2.GallerySize.Width <= 100 || framePickerProbe2.GallerySize.Height <= 40 || !framePickerProbe2.GridInsideGroup || !framePickerProbe2.GalleryInsideGroup)
			{
				num++;
				text = text + width + "→色板" + framePickerProbe2.GridSize.ToString() + (framePickerProbe2.GridInsideGroup ? "" : "(越界)") + " 画廊" + framePickerProbe2.GallerySize.ToString() + (framePickerProbe2.GalleryInsideGroup ? "" : "(越界)") + " ";
			}
		}
		Check(num == 0, "★ 从窄窗到超宽（全屏档）每一档宽度下，色板与样式画廊都保持可见尺寸且不越界", text);
		settingsForm.Close();
	}
	private static void TestFrameTracksWindow()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			DragFixture f = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.FrameStyleId = "Glow";
				c.FrameThickness = 3;
			});
			try
			{
				DragEngine eng = f.Coord.Drag;
				uint ownerThreadIdForTest = OverlayWindow.OwnerThreadIdForTest;
				Check(ownerThreadIdForTest != 0 && ownerThreadIdForTest != Native.GetCurrentThreadId(), "★ 浮层窗口运行在专用线程上（不再挂在 UI 线程，UI 一忙就不会拖累外框）", "浮层线程=" + ownerThreadIdForTest + " 当前线程=" + Native.GetCurrentThreadId());
				ActivateTrigger(f.Coord);
				Settle();
				Native.POINT p = CenterOf(f.H);
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				f.Coord.OnMouse(513, ref ms);
				Settle();
				Check(eng.IsDragging, "拖动会话已建立");
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 12, p.Y + 8));
				f.Coord.OnMouse(512, ref ms2);
				Settle();
				Native.GetWindowRect(eng.Frame.Handle, out var lpRect);
				Native.GetWindowRect(f.H, out var lpRect2);
				int num = lpRect.Left - lpRect2.Left;
				int num2 = lpRect.Top - lpRect2.Top;
				Line("    （静止偏移）外框−窗口 = " + num + "," + num2);
				List<int[]> samples = new List<int[]>();
				int discarded = 0;
				ManualResetEventSlim stop = new ManualResetEventSlim(initialState: false);
				Thread thread = new Thread(() =>
				{
					while (!stop.IsSet)
					{
						try
						{
							if (Native.GetWindowRect(eng.Frame.Handle, out var lpRect4) && Native.GetWindowRect(f.H, out var lpRect5) && Native.GetWindowRect(eng.Frame.Handle, out var lpRect6))
							{
								if (lpRect4.Left == lpRect6.Left && lpRect4.Top == lpRect6.Top)
								{
									lock (samples)
									{
										samples.Add(new int[2]
										{
											lpRect4.Left - lpRect5.Left,
											lpRect4.Top - lpRect5.Top
										});
									}
								}
								else
								{
									discarded++;
								}
							}
						}
						catch
						{
						}
						Thread.Sleep(1);
					}
				});
				thread.IsBackground = true;
				thread.Start();
				int val = 0;
				int num3 = p.X + 12;
				int num4 = p.Y + 8;
				int num5 = num3;
				int num6 = num4;
				for (int num7 = 1; num7 <= 240; num7++)
				{
					int num8 = num7 * 2 + (int)Math.Round(Math.Sin((double)num7 * 0.3) * 2.0);
					int num9 = num7 + (int)Math.Round(Math.Cos((double)num7 * 0.25) * 2.0);
					int num10 = num3 + num8;
					int num11 = num4 + num9;
					val = Math.Max(val, Math.Max(Math.Abs(num10 - num5), Math.Abs(num11 - num6)));
					num5 = num10;
					num6 = num11;
					Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(num10, num11));
					f.Coord.OnMouse(512, ref ms3);
					Settle();
					Thread.Sleep(1);
				}
				Settle();
				stop.Set();
				thread.Join(1000);
				Native.GetWindowRect(f.H, out var lpRect3);
				int num12 = Math.Abs(lpRect3.Left - lpRect2.Left);
				Check(num12 > 100, "★ 拖动确实生效（窗口真的跟着走了）", "窗口水平位移=" + num12 + "px，最大单步=" + val + "px");
				int num13 = int.MaxValue;
				int num14 = int.MinValue;
				int num15 = int.MaxValue;
				int num16 = int.MinValue;
				int count;
				lock (samples)
				{
					count = samples.Count;
					for (int num17 = 0; num17 < samples.Count; num17++)
					{
						int num18 = samples[num17][0] - num;
						int num19 = samples[num17][1] - num2;
						if (num18 < num13)
						{
							num13 = num18;
						}
						if (num18 > num14)
						{
							num14 = num18;
						}
						if (num19 < num15)
						{
							num15 = num19;
						}
						if (num19 > num16)
						{
							num16 = num19;
						}
					}
				}
				Line("    （采样）" + count + " 个有效点（丢弃跨更新的 " + discarded + " 个）；相对静止偏移 X[" + num13 + "," + num14 + "] Y[" + num15 + "," + num16 + "]");
				Line("    （帧账）外框更新 " + DragEngine.DiagFrameUpdates + " 帧，其中窗口真的写了 " + DragEngine.DiagWindowWritten + " 帧、被跳过 " + DragEngine.DiagWindowSkipped + " 帧");
				Check(count > 20, "拖动过程中采到了足够的样本", "样本数=" + count);
				Check(num13 >= -4, "★ 全程外框都没有落在窗口后面（水平方向）", "最差滞后 " + -num13 + "px，容差 " + 4 + "px");
				Check(num15 >= -4, "★ 全程外框都没有落在窗口后面（垂直方向）", "最差滞后 " + -num15 + "px，容差 " + 4 + "px");
				Native.MSLLHOOKSTRUCT ms4 = Ms(ms2.pt);
				f.Coord.OnMouse(514, ref ms4);
				f.Coord.Trigger.SimulateKeyUp(null);
				Settle();
			}
			finally
			{
				if (f != null)
				{
					((IDisposable)f).Dispose();
				}
			}
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestFrameGeometryStableAtEdge()
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
				c.BringToFront = false;
				c.FrameStyleId = "Glow";
				c.FrameThickness = 3;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.POINT p = new Native.POINT(lpRect.Right - 40, (lpRect.Top + lpRect.Bottom) / 2);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms);
			Settle();
			Check(drag.IsDragging, "拖动会话已建立");
			List<int[]> list = new List<int[]>();
			int num = int.MaxValue;
			int num2 = int.MaxValue;
			int[] array = new int[8] { 300, 200, 120, 60, 30, 12, 4, 1 };
			for (int num3 = 0; num3 < array.Length; num3++)
			{
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(array[num3], p.Y));
				dragFixture.Coord.OnMouse(512, ref ms2);
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect2);
				Native.GetWindowRect(drag.Frame.Handle, out var lpRect3);
				if (lpRect2.Left < num)
				{
					num = lpRect2.Left;
				}
				if (lpRect2.Top < num2)
				{
					num2 = lpRect2.Top;
				}
				list.Add(new int[4]
				{
					lpRect3.Left - lpRect2.Left,
					lpRect3.Top - lpRect2.Top,
					lpRect3.Right - lpRect2.Right,
					lpRect3.Bottom - lpRect2.Bottom
				});
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int num4 = 0; num4 < list.Count; num4++)
			{
				stringBuilder.Append("[").Append(list[num4][0]).Append(",")
					.Append(list[num4][1])
					.Append(",")
					.Append(list[num4][2])
					.Append(",")
					.Append(list[num4][3])
					.Append("] ");
			}
			Line("    （窗口最左）" + num + "px  偏移量序列 " + stringBuilder);
			Check(num < 0, "★ 窗口确实被推出了屏幕左缘（用例前提成立）", "窗口最左=" + num + "px");
			int num5 = 0;
			string text = "";
			for (int num6 = 0; num6 < 4; num6++)
			{
				int num7 = list[0][num6];
				for (int num8 = 1; num8 < list.Count; num8++)
				{
					int num9 = Math.Abs(list[num8][num6] - num7);
					if (num9 > num5)
					{
						num5 = num9;
						text = "第" + num8 + "步 方向" + num6;
					}
				}
			}
			Check(num5 <= 2, "★ 贴边 / 出屏全程外框几何恒定（不再缩小、不再跳变）", "最大偏移变化=" + num5 + "px（" + text + "）");
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			dragFixture.Coord.OnMouse(514, ref ms3);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestFrameOpacity()
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
				c.BringToFront = false;
				c.FrameStyleId = "Focus";
				c.FrameColorId = "Teal";
				c.FrameThickness = 6;
				c.FrameOpacity = 100;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			int num = (lpRect.Top + lpRect.Bottom) / 2;
			Rectangle band = new Rectangle(lpRect.Left - 30, num - 2, 60, 5);
			// 背景基线必须在"外框还没亮起"的时候量。40% 是半透明，屏幕读数必然是
			// 「alpha × 外框色 + (1-alpha) × 背景」的混合结果；用固定阈值（例如"必须小于 100% 的 0.6 倍"）
			// 等于假设"背景远比外框暗"。本机壁纸就是深青（实测 RGB 4,70,91 → 青色度 23），
			// 40% 时读数会落在 36 左右而被误判成"没变淡"。改成先量背景、再按混合公式判定，
			// 结论与桌面内容无关，且比原来的固定阈值更严格（同时验证了混合比例本身）。
			int bg = BandMaxGreenness(band);
			Native.SetCursorPos(CenterOf(dragFixture.H).X, CenterOf(dragFixture.H).Y);
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(512, ref ms);
			Settle();
			Check(drag.Frame.IsShown, "用例前提：悬停框已经亮起");
			int num2 = BandMaxGreenness(band);
			dragFixture.Store.Current.FrameOpacity = 40;
			drag.RefreshOverlayTheme();
			Settle();
			Thread.Sleep(120);
			Settle();
			int num3 = BandMaxGreenness(band);
			int num4 = (int)(0.4 * (double)num2 + 0.6 * (double)bg) + 4;
			Line("    （不透明度）带内青色度 背景=" + bg + "  100%=" + num2 + "  40%=" + num3 + "（混合上限 " + num4 + "）");
			Check(num2 > 30, "★ 用例前提：100% 时这条带上确实有外框（青色度足够）", "青色度=" + num2);
			Check(num3 != num2, "★ 改不透明度真的改变了屏幕渲染（不是改了参数却没重推像素）", "100%=" + num2 + " 40%=" + num3);
			Check(num3 < num2 && num3 <= num4, "★ 40% 的外框按 alpha 混合掺进背景：读数不超过「0.4×外框 + 0.6×背景」", "100%=" + num2 + " 40%=" + num3 + " 背景=" + bg + " 上限=" + num4);
			Check(num2 - num3 >= (num2 - bg) / 2, "★ 40% 至少削掉一半「外框−背景」对比度（整层等比变淡）", "对比度 " + (num2 - bg) + " → " + (num2 - num3) + "（背景=" + bg + "）");
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestFrameAfterFullscreenDowngrade()
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
				c.BringToFront = false;
				c.AllowFullscreenWindows = false;
				c.ExitFullscreenOnTrigger = true;
				c.FrameStyleId = "Glow";
				c.FrameThickness = 3;
				c.DoubleClickLeftMaximize = false;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			Native.SetWindowPos(dragFixture.H, IntPtr.Zero, virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height, 20u);
			Settle();
			Check(WindowActions.IsFullscreen(dragFixture.H), "用例前提：窗口是真全屏");
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			Settle();
			ActivateTrigger(dragFixture.Coord);
			Native.POINT p = CenterOf(dragFixture.H);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(512, ref ms);
			Settle();
			bool isShown = drag.Frame.IsShown;
			Line("    （悬停全屏窗口）外框显示=" + isShown);
			Native.MSLLHOOKSTRUCT ms2 = Ms(p);
			bool flag = dragFixture.Coord.OnMouse(513, ref ms2);
			Settle();
			Check(flag && drag.IsDragging, "★ 左键被接管且拖动已建立");
			Check(!WindowActions.IsFullscreen(dragFixture.H), "★ 窗口已从全屏降级成普通窗口");
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.GetWindowRect(drag.Frame.Handle, out var lpRect2);
			Line("    （降级后）窗口=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height + "  外框=" + lpRect2.Left + "," + lpRect2.Top + " " + lpRect2.Width + "x" + lpRect2.Height + "  显示=" + drag.Frame.IsShown);
			Check(drag.Frame.IsShown, "★ 降级之后外框必须仍然显示着（用户报的就是这里没有框）");
			Check(isShown, "★ 悬停全屏窗口时也要亮框（两个开关必须同源，否则按下才有框）");
			int num = Math.Abs(lpRect2.Left - lpRect.Left);
			int num2 = Math.Abs(lpRect2.Top - lpRect.Top);
			int num3 = Math.Abs(lpRect2.Width - lpRect.Width);
			Check(num <= 60 && num2 <= 60 && num3 <= 120, "★ 外框贴的是【降级后的新窗口】，不是降级前的全屏矩形", "外框-窗口 偏移 " + num + "," + num2 + " 宽差 " + num3);
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			dragFixture.Coord.OnMouse(514, ref ms3);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestPaletteIsSoft()
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = new StringBuilder();
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			FrameColors.Entry entry = FrameColors.All[i];
			Color color = entry.Color;
			int num = Math.Max(color.R, Math.Max(color.G, color.B));
			int num2 = Math.Min(color.R, Math.Min(color.G, color.B));
			float num3 = (float)(num + num2) / 2f / 255f;
			float num4 = 0f;
			if (num != num2)
			{
				float num5 = (float)(num - num2) / 255f;
				num4 = ((num3 > 0.5f) ? (num5 / (2f - 2f * num3)) : (num5 / (2f * num3)));
			}
			int num6 = (int)Math.Round(num4 * 100f);
			int num7 = (int)Math.Round(num3 * 100f);
			stringBuilder2.Append(entry.Name).Append('(').Append(num6)
				.Append('/')
				.Append(num7)
				.Append(") ");
			if (!(entry.Id == "Accent"))
			{
				if (num6 > 72)
				{
					stringBuilder.Append(entry.Name + "饱和" + num6 + "% ");
				}
				if (num6 > 40 && (num7 < 48 || num7 > 82))
				{
					stringBuilder.Append(entry.Name + "明度" + num7 + "% ");
				}
			}
		}
		Line("    （调色板 饱和/明度）" + stringBuilder2);
		Check(stringBuilder.Length == 0, "★ 调色板整体柔和：有彩色饱和 ≤72% 且明度居中；中性锚点只约束饱和度", stringBuilder.ToString());
		Check(FrameColors.All.Length >= 12, "颜色数量（太少不够用）", FrameColors.All.Length + " 个");
	}
	private static void TestFrameNeverResidues()
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
				c.BringToFront = false;
				c.FrameStyleId = "Glow";
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.SetCursorPos(CenterOf(dragFixture.H).X, CenterOf(dragFixture.H).Y);
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(512, ref ms);
			Settle();
			Check(drag.FrameReallyVisible, "用例前提：悬停框已亮起（系统层面真的可见）");
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			drag.EnforceFrameInvariant(dragFixture.Coord.Trigger.Active);
			Check(!drag.FrameReallyVisible, "★ 兜底自检：不在拖动、也没有悬停目标时，外框必须被强制收掉", "IsShown=" + drag.Frame.IsShown);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms2 = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Check(drag.FrameReallyVisible, "再次亮框（准备验证退出路径）");
			dragFixture.Coord.HideOverlaysNow();
			Check(!drag.FrameReallyVisible, "★ 退出路径：同步收框之后外框必须立刻不可见（不留到进程结束）", "IsShown=" + drag.Frame.IsShown);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
	private static void TestFullscreenExternalWindow()
	{
		nint num = LaunchExternalWindow();
		if (num == IntPtr.Zero)
		{
			Line("    （跳过：本环境无法拉起外部测试窗口）");
			return;
		}
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			// 原来用 20u = SWP_NOACTIVATE|SWP_NOZORDER：窗口既不激活也不置顶，桌面上任何同层窗口都能盖住它，
			// 于是"指针下是哪个窗口"的命中测试会返回别人的窗口 —— 本机实测命中了 Edge 的
			// 「QS-噪声画面|987x725|15,131,127|0.005」，那是环境前提不成立，不是产品行为出错。
			// 真实的全屏窗口本来就在最前，这里改成 SWP_SHOWWINDOW + HWND_TOPMOST 让前提真的成立。
			Native.SetWindowPos(num, new IntPtr(-1), virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height, 0x0040u);
			Settle();
			if (!WindowActions.IsFullscreen(num))
			{
				Line("    （跳过：外部窗口没能铺成全屏）");
				return;
			}
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.AllowFullscreenWindows = false;
				c.ExitFullscreenOnTrigger = true;
				c.FrameStyleId = "Glow";
				c.FrameThickness = 3;
				c.DoubleClickLeftMaximize = false;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
			Native.POINT pOINT = CenterOfRect(num);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.SetCursorPos(pOINT.X, pOINT.Y);
			Native.MSLLHOOKSTRUCT ms = Ms(pOINT);
			dragFixture.Coord.OnMouse(512, ref ms);
			Settle();
			nint num2 = WindowActions.FindTarget(pOINT, allowFullscreen: true);
			Line("    （外部窗口）句柄=" + ((IntPtr)num).ToInt64().ToString("X") + " 全屏=" + WindowActions.IsFullscreen(num) + " 命中=" + ((IntPtr)num2).ToInt64().ToString("X") + " 悬停框显示=" + drag.Frame.IsShown);
			if (num2 != num)
			{
				Line("    （跳过：本机桌面上另有窗口压在外层全屏窗口之上，命中=" + ((IntPtr)num2).ToInt64().ToString("X") + " 期望=" + ((IntPtr)num).ToInt64().ToString("X") + "；这是环境前提不成立，不据此判产品对错）");
				return;
			}
			Check(num2 == num, "★ 真实命中测试落在外部全屏窗口上", "hit=" + ((IntPtr)num2).ToInt64().ToString("X"));
			Native.MSLLHOOKSTRUCT ms2 = Ms(pOINT);
			bool flag = dragFixture.Coord.OnMouse(513, ref ms2);
			Settle();
			Native.GetWindowRect(num, out var lpRect);
			Native.GetWindowRect(drag.Frame.Handle, out var lpRect2);
			Line("    （降级后）窗口=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height + " 全屏=" + WindowActions.IsFullscreen(num) + " 拖动中=" + drag.IsDragging + " 外框=" + lpRect2.Left + "," + lpRect2.Top + " " + lpRect2.Width + "x" + lpRect2.Height + " 显示=" + drag.Frame.IsShown);
			Check(flag && drag.IsDragging, "★ 左键被接管且拖动已建立");
			Check(drag.Frame.IsShown, "★★ 全屏转窗口之后外框窗口仍是显示状态");
			if (drag.Frame.IsShown)
			{
				int num3 = Math.Abs(lpRect2.Left - lpRect.Left);
				int num4 = Math.Abs(lpRect2.Top - lpRect.Top);
				Check(num3 <= 80 && num4 <= 80, "★ 外框贴在降级后的新窗口上", "偏移 " + num3 + "," + num4);
			}
			if (drag.Frame.IsShown)
			{
				for (int num5 = 0; num5 < 12; num5++)
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				int num6 = (lpRect.Top + lpRect.Bottom) / 2;
				Rectangle band = Rectangle.FromLTRB(lpRect.Left - 90, num6 - 30, lpRect.Left + 90, num6 + 30);
				int num7 = 0;
				for (int num8 = 0; num8 < 6; num8++)
				{
					Application.DoEvents();
					Thread.Sleep(80);
					int num9 = BandMaxGreenness(band);
					if (num9 > num7)
					{
						num7 = num9;
					}
					if (num7 > 25)
					{
						break;
					}
				}
				Line("    （屏幕像素）窗口左侧带内最大青色度=" + num7 + "（外框青绿≈60，白/灰/桌面≈0）");
				Check(num7 > 25, "★★★ 屏幕上真的能看到外框像素（不是只有 IsShown 标志位）", "青色度=" + num7);
			}
			Native.MSLLHOOKSTRUCT ms3 = Ms(pOINT);
			dragFixture.Coord.OnMouse(514, ref ms3);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			KillWindowHostProcess(num);
		}
	}
}
