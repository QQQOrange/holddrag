// HoldDrag · Core/WindowActions.cs

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

internal static class WindowActions
{
	private static readonly string[] ExcludedClasses = new string[18]
	{
		"Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "TaskListThumbnailWnd", "ForegroundStaging", "Windows.UI.Core.CoreWindow", "ApplicationManager_ImmersiveShellWindow", "MultitaskingViewFrame", "XamlExplorerHostIslandWindow",
		"TopLevelWindowForOverflowXamlIsland", "Windows.Internal.Shell.TabProxyWindow", "EdgeUiInputTopWndClass", "SysShadow", "tooltips_class32", "MSCTFIME UI", "IME", "Default IME"
	};

	private static readonly int OwnPid = Process.GetCurrentProcess().Id;

	internal const string OverlayClassName = "HoldDragOverlayWindow";

	internal static bool AllowDragOwnWindows = true;

	internal static bool AllowOwnProcessAsTargetForTest;

	private static bool IsOwnProcessTarget(nint h)
	{
		try
		{
			string className = Native.GetClassName(h);
			if (className == "HoldDragOverlayWindow")
			{
				return false;
			}
			if (className == "HoldDrag.Probe.Target")
			{
				return false;
			}
			if (className == "SysShadow" || className == "tooltips_class32" || className == "MSCTFIME UI" || className == "Default IME" || className == "IME")
			{
				return false;
			}
			long windowLong = Native.GetWindowLong(h, -16);
			if ((windowLong & 0x10000000) == 0L)
			{
				return false;
			}
			if ((windowLong & 0x40000000) != 0L)
			{
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	internal static nint ForcedTargetForTest = IntPtr.Zero;

	/// <summary>
	/// 可交互控件的原生窗口类名。指针落在这类控件上时，本程序不接管鼠标，
	/// 把按键原样交给控件，避免"想拖窗口却动了滑块/选区""想拖滑块却被拖走窗口"。
	/// 只列真正吃鼠标的控件；Panel / GroupBox / Label 这些容器与静态控件不在其内，
	/// 因此按住触发键拖这些区域或标题栏、边框时仍能拖动整个窗口。
	/// </summary>
	private static readonly string[] InteractiveClasses = new string[17]
	{
		"msctls_trackbar32", "Edit", "Button", "ComboBox", "ComboBoxEx32", "msctls_updown32",
		"ScrollBar", "msctls_progress32", "ListBox", "SysListView32", "SysTreeView32",
		"SysTabControl32", "msctls_statusbar32", "ToolbarWindow32", "SysMonthCal32",
		"SysDateTimePick32", "SysIPAddress32"
	};

	/// <summary>该窗口句柄是否为"可交互控件"。必须传句柄：单看类名分不出 GroupBox 与 Button。</summary>
	internal static bool IsInteractiveControl(nint h)
	{
		string cls = Native.GetClassName(h);
		if (string.IsNullOrEmpty(cls))
		{
			return false;
		}
		// WinForms 会把系统控件类名包成 WindowsForms10.<基础类名>.app.<版本>_r<n>_ad<n>，
		// 例如滑块的实测类名是 WindowsForms10.msctls_trackbar32.app.0.3279090_r3_ad1。
		// 因此先剥掉包装前缀再比对基础类名，否则原生控件永远识别不出来。
		const string WinFormsPrefix = "WindowsForms10.";
		if (cls.StartsWith(WinFormsPrefix, StringComparison.OrdinalIgnoreCase))
		{
			int num = cls.IndexOf('.', WinFormsPrefix.Length);
			if (num <= WinFormsPrefix.Length)
			{
				return false;
			}
			cls = cls.Substring(WinFormsPrefix.Length, num - WinFormsPrefix.Length);
		}
		if (!MatchesInteractiveName(cls))
		{
			return false;
		}
		// WinForms 的 GroupBox 也是用 BUTTON 类实现的，只有 BS_GROUPBOX(0x7) 能把它和真正的
		// 按钮/复选框/单选钮区分开。GroupBox 是容器、不吃鼠标拖动 —— 若不排除，设置页里成片的
		// GroupBox 会把"按住触发键拖窗口"整个吃掉（表现就是左键毫无反应）。
		return !(string.Equals(cls, "Button", StringComparison.OrdinalIgnoreCase) && (Native.GetWindowLong(h, -16) & 0xF) == 0x7L);
	}

	private static bool MatchesInteractiveName(string cls)
	{
		for (int i = 0; i < InteractiveClasses.Length; i++)
		{
			if (string.Equals(cls, InteractiveClasses[i], StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return cls.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 指针是否落在"本进程自有窗口内部的可交互控件"上。命中则本轮不要接管拖动。
	/// 判定链：WindowFromPoint → GA_ROOT → 根窗口属于本进程且是合法自有窗口 → 命中窗口的类名可交互。
	/// </summary>
	internal static bool IsPointOverOwnInteractiveControl(Native.POINT pt)
	{
		try
		{
			nint hit = Native.WindowFromPoint(pt);
			if (hit == IntPtr.Zero)
			{
				return false;
			}
			nint root = Native.GetAncestor(hit, 2u);
			if (root == IntPtr.Zero)
			{
				return false;
			}
			Native.GetWindowThreadProcessId(root, out var lpdwProcessId);
			if (lpdwProcessId != (uint)OwnPid || !IsOwnProcessTarget(root))
			{
				return false;
			}
			return IsInteractiveControl(hit);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	/// <summary>诊断用：描述指针当前命中的控件类名与句柄（不命中自有窗口控件时返回原因）。</summary>
	internal static string DescribeInteractiveAt(Native.POINT pt)
	{
		try
		{
			nint hit = Native.WindowFromPoint(pt);
			if (hit == IntPtr.Zero)
			{
				return Loc.T("无命中");
			}
			nint root = Native.GetAncestor(hit, 2u);
			return Native.GetClassName(hit) + Loc.T(" 句柄=") + ((IntPtr)hit).ToInt64().ToString("X")
				+ Loc.T(" 根=") + ((IntPtr)root).ToInt64().ToString("X");
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return Loc.T("查询失败");
		}
	}

	private static readonly HashSet<nint> _foreignForTest = new HashSet<nint>();

	private static readonly object ForeignGate = new object();

	internal static bool SkipWindowFromPointForTest = false;

	private static string[] _blacklist = new string[0];

	private const double WindowedRatio = 0.72;

	internal static bool KickUselessForSession;

	internal static bool HungTargetForSession;

	private const double HangThresholdMs = 250.0;

	private const int HangStreakToAbandon = 3;

	private const int ProbeEveryFrames = 24;

	private static int SlowFrameStreak;

	private static int AbandonProbe;

	private static Native.RECT _lastWriteRect;

	private static bool _lastWriteValid;

	public static long FramesWritten;

	public static long FramesNoop;

	public static long FramesAbandoned;

	public static double TotalWriteMs;

	public static double MaxWriteMs;

	internal static void SetForeignForTest(nint h, bool on)
	{
		lock (ForeignGate)
		{
			if (on)
			{
				_foreignForTest.Add(h);
			}
			else
			{
				_foreignForTest.Remove(h);
			}
		}
	}

	public static bool IsExcludedClass(string cls)
	{
		for (int i = 0; i < ExcludedClasses.Length; i++)
		{
			if (string.Equals(ExcludedClasses[i], cls, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	public static void ApplyBlacklist(string csv)
	{
		if (string.IsNullOrWhiteSpace(csv))
		{
			_blacklist = new string[0];
			return;
		}
		string[] array = csv.Split(new char[4] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		List<string> list = new List<string>(array.Length);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length > 0)
			{
				list.Add(text);
			}
		}
		_blacklist = list.ToArray();
	}

	public static bool IsBlacklisted(nint h)
	{
		if (_blacklist.Length == 0)
		{
			return false;
		}
		string className = Native.GetClassName(h);
		string windowTitle = Native.GetWindowTitle(h);
		string text = null;
		for (int i = 0; i < _blacklist.Length; i++)
		{
			string value = _blacklist[i];
			if (className != null && className.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			if (windowTitle != null && windowTitle.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			if (text == null)
			{
				text = ProcessExeName(h);
			}
			if (text.Length > 0 && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	public static string ProcessExeName(nint h)
	{
		try
		{
			if (h == IntPtr.Zero)
			{
				return string.Empty;
			}
			Native.GetWindowThreadProcessId(h, out var lpdwProcessId);
			if (lpdwProcessId == 0)
			{
				return string.Empty;
			}
			return ProcessExeNameByPid(lpdwProcessId);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return string.Empty;
		}
	}

	public static string ProcessExeNameByPid(uint pid)
	{
		if (pid == 0)
		{
			return string.Empty;
		}
		nint num = Native.OpenProcess(4096u, bInheritHandle: false, pid);
		if (num == IntPtr.Zero)
		{
			return string.Empty;
		}
		try
		{
			StringBuilder stringBuilder = new StringBuilder(1024);
			uint lpdwSize = (uint)stringBuilder.Capacity;
			if (!Native.QueryFullProcessImageNameW(num, 0u, stringBuilder, ref lpdwSize))
			{
				return string.Empty;
			}
			string text = stringBuilder.ToString();
			if (text.Length == 0)
			{
				return string.Empty;
			}
			string text2 = Path.GetFileName(text);
			if (text2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				text2 = text2.Substring(0, text2.Length - 4);
			}
			return text2;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return string.Empty;
		}
		finally
		{
			Native.CloseHandle(num);
		}
	}

	public static nint FindTarget(Native.POINT pt)
	{
		return FindTarget(pt, allowFullscreen: true);
	}

	public static nint FindTarget(Native.POINT pt, bool allowFullscreen)
	{
		if (ForcedTargetForTest != IntPtr.Zero)
		{
			return ForcedTargetForTest;
		}
		nint num = IntPtr.Zero;
		nint num2 = HitTestTopWindow(pt, out var _);
		for (int i = 0; i < 32; i++)
		{
			if (num2 == IntPtr.Zero)
			{
				break;
			}
			// 第 0 轮是 WindowFromPoint 的结果，天然包含光标；
			// 之后都是"沿 Z 序往下找"，找到的窗口未必在光标底下（很可能是别的程序在屏幕另一头的大窗口）。
			// 不校验包含关系就会把不相干的窗口当目标拖走 —— 表现是"光标停在托盘/浮层上，拖的却是别的窗口"。
			if (i > 0 && (!Native.GetWindowRect(num2, out var lpRectCheck) || pt.X < lpRectCheck.Left || pt.X >= lpRectCheck.Right || pt.Y < lpRectCheck.Top || pt.Y >= lpRectCheck.Bottom))
			{
				num = IntPtr.Zero;
				break;
			}
			num = Native.GetAncestor(num2, 2u);
			if (num == IntPtr.Zero)
			{
				return IntPtr.Zero;
			}
			Native.GetWindowThreadProcessId(num, out var lpdwProcessId);
			bool flag;
			lock (ForeignGate)
			{
				flag = _foreignForTest.Count > 0 && _foreignForTest.Contains(num);
			}
			bool flag2 = lpdwProcessId == (uint)OwnPid && AllowDragOwnWindows && IsOwnProcessTarget(num);
			if (((lpdwProcessId != (uint)OwnPid) | flag) || flag2 || AllowOwnProcessAsTargetForTest)
			{
				break;
			}
			num2 = Native.GetWindow(num, 2u);
			num = IntPtr.Zero;
		}
		if (num == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		if (!Native.IsWindow(num) || !Native.IsWindowVisible(num))
		{
			return IntPtr.Zero;
		}
		if (IsExcludedClass(Native.GetClassName(num)))
		{
			return IntPtr.Zero;
		}
		long windowLong = Native.GetWindowLong(num, -16);
		if ((windowLong & 0x10000000) == 0L)
		{
			return IntPtr.Zero;
		}
		if ((windowLong & 0x20000000) != 0L)
		{
			return IntPtr.Zero;
		}
		if (IsBlacklisted(num))
		{
			return IntPtr.Zero;
		}
		if (!Native.GetWindowRect(num, out var lpRect))
		{
			return IntPtr.Zero;
		}
		if (lpRect.Width <= 0 || lpRect.Height <= 0)
		{
			return IntPtr.Zero;
		}
		if (!allowFullscreen && IsFullscreen(num, lpRect))
		{
			return IntPtr.Zero;
		}
		return num;
	}

	internal static nint HitTestTopWindow(Native.POINT pt, out string how)
	{
		nint num = (SkipWindowFromPointForTest ? IntPtr.Zero : Native.WindowFromPoint(pt));
		if (num != IntPtr.Zero)
		{
			how = "WindowFromPoint";
			return num;
		}
		nint desktopWindow = Native.GetDesktopWindow();
		if (desktopWindow != IntPtr.Zero)
		{
			nint num2 = Native.ChildWindowFromPointEx(desktopWindow, pt, 5u);
			if (num2 != IntPtr.Zero && num2 != desktopWindow)
			{
				how = "ChildWindowFromPointEx";
				return num2;
			}
		}
		nint found = IntPtr.Zero;
		try
		{
			Native.EnumWindows((nint hwnd, nint l) =>
			{
				if (!Native.IsWindowVisible(hwnd))
				{
					return true;
				}
				if ((Native.GetWindowLong(hwnd, -16) & 0x20000000) != 0L)
				{
					return true;
				}
				if (!Native.GetWindowRect(hwnd, out var lpRect))
				{
					return true;
				}
				if (lpRect.Width <= 0 || lpRect.Height <= 0)
				{
					return true;
				}
				if (pt.X < lpRect.Left || pt.X >= lpRect.Right || pt.Y < lpRect.Top || pt.Y >= lpRect.Bottom)
				{
					return true;
				}
				found = hwnd;
				return false;
			}, IntPtr.Zero);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		how = ((found != IntPtr.Zero) ? "EnumWindowsZOrder" : Loc.T("三种策略全部失败"));
		return found;
	}

	internal static string DescribeReject(Native.POINT pt, bool allowFullscreen)
	{
		try
		{
			nint num = HitTestTopWindow(pt, out var how);
			string text = Loc.T(" [命中方式=") + how + "]";
			if (num == IntPtr.Zero)
			{
				string text2 = "";
				try
				{
					Rectangle virtualScreen = SystemInformation.VirtualScreen;
					bool flag = pt.X >= virtualScreen.Left && pt.X < virtualScreen.Right && pt.Y >= virtualScreen.Top && pt.Y < virtualScreen.Bottom;
					text2 = Loc.T(" 虚拟桌面=") + virtualScreen.Left + "," + virtualScreen.Top + " " + virtualScreen.Width + "x" + virtualScreen.Height + Loc.T(" 该点在桌面内=") + flag + (flag ? "" : Loc.T(" ←★坐标空间不一致，疑似 DPI 虚拟化"));
				}
				catch
				{
				}
				return Loc.T("三种命中策略全部返回空（桌面/无窗口区域）") + text + text2;
			}
			nint num2 = IntPtr.Zero;
			for (int i = 0; i < 32; i++)
			{
				if (num == IntPtr.Zero)
				{
					break;
				}
				num2 = Native.GetAncestor(num, 2u);
				if (num2 == IntPtr.Zero)
				{
					return Loc.T("取不到根窗口") + text;
				}
				Native.GetWindowThreadProcessId(num2, out var lpdwProcessId);
				if (lpdwProcessId != (uint)OwnPid || (AllowDragOwnWindows && IsOwnProcessTarget(num2)))
				{
					break;
				}
				num = Native.GetWindow(num2, 2u);
				num2 = IntPtr.Zero;
			}
			if (num2 == IntPtr.Zero)
			{
				return Loc.T("命中的全是本进程窗口（浮层/设置窗口），往下找不到别人的窗口") + text;
			}
			string className = Native.GetClassName(num2);
			Native.GetWindowThreadProcessId(num2, out var _);
			string text3 = SafeTitle(num2);
			if (!Native.IsWindow(num2))
			{
				return Loc.T("窗口已销毁 类名=") + className;
			}
			if (!Native.IsWindowVisible(num2))
			{
				return Loc.T("窗口不可见（隐藏）类名=") + className + Loc.T(" 标题=") + text3;
			}
			if (IsExcludedClass(className))
			{
				return Loc.T("类名被排除（桌面/任务栏等）类名=") + className;
			}
			long windowLong = Native.GetWindowLong(num2, -16);
			if ((windowLong & 0x10000000) == 0L)
			{
				return Loc.T("窗口没有 WS_VISIBLE 类名=") + className;
			}
			if ((windowLong & 0x20000000) != 0L)
			{
				return Loc.T("窗口已最小化 类名=") + className;
			}
			if (IsBlacklisted(num2))
			{
				return Loc.T("命中黑名单（不生效的窗口）标题=") + text3;
			}
			if (!Native.GetWindowRect(num2, out var lpRect))
			{
				return Loc.T("取不到窗口矩形 类名=") + className;
			}
			if (lpRect.Width <= 0 || lpRect.Height <= 0)
			{
				return Loc.T("窗口尺寸为 0（") + lpRect.Width + "x" + lpRect.Height + Loc.T("）类名=") + className;
			}
			if (!allowFullscreen && IsFullscreen(num2, lpRect))
			{
				return Loc.T("窗口占满整个显示器（真全屏）—— 可在设置里勾选【允许全屏窗口】");
			}
			return Loc.T("未知原因（判定逻辑可能有遗漏）类名=") + className + Loc.T(" 标题=") + text3;
		}
		catch (Exception ex)
		{
			return Loc.T("诊断本身出错: ") + ex.Message;
		}
	}

	private static string SafeTitle(nint h)
	{
		try
		{
			string text = Native.GetWindowTitle(h);
			if (string.IsNullOrEmpty(text))
			{
				return Loc.T("(无标题)");
			}
			if (text.Length > 30)
			{
				text = text.Substring(0, 30) + "…";
			}
			return text;
		}
		catch
		{
			return Loc.T("(取标题失败)");
		}
	}

	public static bool IsFullscreen(nint h)
	{
		if (!Native.GetWindowRect(h, out var lpRect))
		{
			return false;
		}
		return IsFullscreen(h, lpRect);
	}

	public static bool IsFullscreen(nint h, Native.RECT r)
	{
		Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(new Native.POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2));
		if (monitorInfoAt.rcMonitor.Width <= 0 || monitorInfoAt.rcMonitor.Height <= 0)
		{
			return false;
		}
		if (r.Left <= monitorInfoAt.rcMonitor.Left && r.Top <= monitorInfoAt.rcMonitor.Top && r.Right >= monitorInfoAt.rcMonitor.Right)
		{
			return r.Bottom >= monitorInfoAt.rcMonitor.Bottom;
		}
		return false;
	}

	public static bool IsResizable(nint h)
	{
		return (Native.GetWindowLong(h, -16) & 0x40000) != 0;
	}

	public static bool ExitFullscreenToWindowed(nint h, ref Native.RECT r, Native.POINT pt, Action<Action> offHook = null)
	{
		if (h == IntPtr.Zero)
		{
			return false;
		}
		Action<Action> action = offHook ?? ((Action<Action>)((Action action2) =>
		{
			action2();
		}));
		double num = ((r.Width > 0) ? ((double)(pt.X - r.Left) / (double)r.Width) : 0.5);
		double num2 = ((r.Height > 0) ? ((double)(pt.Y - r.Top) / (double)r.Height) : 0.5);
		if (num < 0.0)
		{
			num = 0.0;
		}
		if (num > 1.0)
		{
			num = 1.0;
		}
		if (num2 < 0.0)
		{
			num2 = 0.0;
		}
		if (num2 > 1.0)
		{
			num2 = 1.0;
		}
		if (Native.IsZoomed(h))
		{
			Native.WINDOWPLACEMENT lpwndpl = new Native.WINDOWPLACEMENT
			{
				length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT))
			};
			int num3;
			if (Native.GetWindowPlacement(h, ref lpwndpl) && lpwndpl.rcNormalPosition.Width > 0)
			{
				num3 = ((lpwndpl.rcNormalPosition.Height > 0) ? 1 : 0);
				if (num3 != 0)
				{
					r = lpwndpl.rcNormalPosition;
				}
			}
			else
			{
				num3 = 0;
			}
			action(() =>
			{
				Native.ShowWindow(h, 9);
			});
			if (num3 == 0 && !Native.GetWindowRect(h, out r))
			{
				return false;
			}
		}
		if (IsFullscreen(h, r))
		{
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(pt);
			Native.RECT rECT = ((monitorInfoAt.rcWork.Width > 0 && monitorInfoAt.rcWork.Height > 0) ? monitorInfoAt.rcWork : monitorInfoAt.rcMonitor);
			if (rECT.Width <= 0 || rECT.Height <= 0)
			{
				return false;
			}
			int w = (int)((double)rECT.Width * 0.72);
			int hh = (int)((double)rECT.Height * 0.72);
			if (w < 320)
			{
				w = Math.Min(320, rECT.Width);
			}
			if (hh < 240)
			{
				hh = Math.Min(240, rECT.Height);
			}
			int x = pt.X - (int)(num * (double)w);
			int y = pt.Y - (int)(num2 * (double)hh);
			if (x < rECT.Left)
			{
				x = rECT.Left;
			}
			if (y < rECT.Top)
			{
				y = rECT.Top;
			}
			if (x + w > rECT.Right)
			{
				x = rECT.Right - w;
			}
			if (y + hh > rECT.Bottom)
			{
				y = rECT.Bottom - hh;
			}
			if (x < rECT.Left)
			{
				x = rECT.Left;
			}
			if (y < rECT.Top)
			{
				y = rECT.Top;
			}
			action(() =>
			{
				Native.SetWindowPos(h, IntPtr.Zero, x, y, w, hh, 16916u);
			});
			r = new Native.RECT(x, y, x + w, y + hh);
		}
		else
		{
			int nx = pt.X - (int)(num * (double)r.Width);
			int ny = pt.Y - (int)(num2 * (double)r.Height);
			int rw = r.Width;
			int rh = r.Height;
			action(() =>
			{
				Native.SetWindowPos(h, IntPtr.Zero, nx, ny, rw, rh, 16917u);
			});
			r = new Native.RECT(nx, ny, nx + rw, ny + rh);
		}
		Log.Info("触发时降级为窗口化：句柄=" + ((IntPtr)h).ToInt64().ToString("X") + " 新矩形=" + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height);
		return true;
	}

	public static bool IsTopMost(nint h)
	{
		return (Native.GetWindowLong(h, -20) & 8) != 0;
	}

	public static void ResetFrameStats()
	{
		FramesWritten = 0L;
		FramesNoop = 0L;
		FramesAbandoned = 0L;
		TotalWriteMs = 0.0;
		MaxWriteMs = 0.0;
	}

	internal static void ResetSessionKickCache()
	{
		KickUselessForSession = false;
		HungTargetForSession = false;
		SlowFrameStreak = 0;
		AbandonProbe = 0;
		_lastWriteValid = false;
	}

	public static bool SetDragRectFrame(nint h, Native.RECT target)
	{
		return SetDragRectFrame(h, target, moveOnly: false);
	}

	public static bool SetDragRectFrame(nint h, Native.RECT target, bool moveOnly)
	{
		if (h == IntPtr.Zero)
		{
			return false;
		}
		if (HungTargetForSession)
		{
			AbandonProbe++;
			if (AbandonProbe < 24)
			{
				FramesAbandoned++;
				return false;
			}
			AbandonProbe = 0;
		}
		bool flag;
		if (moveOnly)
		{
			flag = false;
			if (_lastWriteValid && _lastWriteRect.Left == target.Left && _lastWriteRect.Top == target.Top)
			{
				FramesNoop++;
				return false;
			}
		}
		else
		{
			bool windowRect = Native.GetWindowRect(h, out var lpRect);
			flag = !windowRect || lpRect.Right - lpRect.Left != target.Width || lpRect.Bottom - lpRect.Top != target.Height;
			if (windowRect && !flag && lpRect.Left == target.Left && lpRect.Top == target.Top)
			{
				FramesNoop++;
				return false;
			}
		}
		if (Native.IsZoomed(h))
		{
			Native.ShowWindow(h, 9);
		}
		uint uFlags = (flag ? 532u : 533u);
		if (flag && !Native.IsZoomed(h) && !KickUselessForSession && !LetWindowKickBack(h, ref target))
		{
			KickUselessForSession = true;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		Native.SetWindowPos(h, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, uFlags);
		stopwatch.Stop();
		double totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
		TotalWriteMs += totalMilliseconds;
		if (totalMilliseconds > MaxWriteMs)
		{
			MaxWriteMs = totalMilliseconds;
		}
		FramesWritten++;
		_lastWriteRect = target;
		_lastWriteValid = true;
		if (totalMilliseconds > 250.0)
		{
			SlowFrameStreak++;
			if (SlowFrameStreak >= 3)
			{
				HungTargetForSession = true;
				AbandonProbe = 0;
				Log.Info("目标连续 " + SlowFrameStreak + " 帧写入超时（>" + 250 + "ms），本次拖动放弃实时跟手（每 " + 24 + " 帧探测恢复）");
			}
		}
		else
		{
			SlowFrameStreak = 0;
			if (HungTargetForSession)
			{
				HungTargetForSession = false;
				Log.Info("目标恢复响应，重新开始实时跟手");
			}
		}
		return true;
	}

	/// <summary>
	/// 按「可见矩形」设置窗口位置：自动补上 Win10/11 窗口四周那圈不可见的 resize 边框，
	/// 否则贴左边会漏出 7px 空白（看起来像没贴齐）。
	/// </summary>
	public static bool SetVisibleRect(nint h, Native.RECT visible)
	{
		if (h == IntPtr.Zero || !Native.GetWindowRect(h, out var lpRect))
		{
			return false;
		}
		Native.RECT target = visible;
		if (Native.TryGetVisibleRect(h, out var r))
		{
			target = Tiling.InflateToWindowRect(visible, lpRect, r);
		}
		SetDragRect(h, target);
		return true;
	}


	/// <summary>光标下的目标窗口（托盘快捷操作用；找不到返回 0）。</summary>
	public static nint TargetUnderCursor()
	{
		try
		{
			if (!Native.GetCursorPos(out var lpPoint))
			{
				return IntPtr.Zero;
			}
			return FindTarget(lpPoint, allowFullscreen: true);
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	/// <summary>把光标下的窗口在当前显示器居中（保持大小）。</summary>
	public static bool CenterUnderCursor()
	{
		nint num = TargetUnderCursor();
		if (num == IntPtr.Zero)
		{
			return false;
		}
		Native.POINT pt = default(Native.POINT);
		Native.GetCursorPos(out pt);
		Rectangle workingArea = Screen.FromPoint(new Point(pt.X, pt.Y)).WorkingArea;
		Native.RECT work = new Native.RECT(workingArea.Left, workingArea.Top, workingArea.Right, workingArea.Bottom);
		Native.RECT current;
		if (!Native.TryGetVisibleRect(num, out var r))
		{
			if (!Native.GetWindowRect(num, out current))
			{
				return false;
			}
		}
		else
		{
			current = r;
		}
		return SetVisibleRect(num, Tiling.Centered(work, current));
	}

	/// <summary>把光标下的窗口送到下一块显示器，保持相对位置与大小。</summary>
	public static bool MoveToNextMonitor()
	{
		nint num = TargetUnderCursor();
		if (num == IntPtr.Zero)
		{
			return false;
		}
		Native.RECT r;
		if (!Native.TryGetVisibleRect(num, out var r2))
		{
			if (!Native.GetWindowRect(num, out r))
			{
				return false;
			}
		}
		else
		{
			r = r2;
		}
		Screen[] allScreens = Screen.AllScreens;
		if (allScreens.Length < 2)
		{
			return false;
		}
		Point point = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
		int num2 = 0;
		for (int i = 0; i < allScreens.Length; i++)
		{
			if (allScreens[i].Bounds.Contains(point))
			{
				num2 = i;
				break;
			}
		}
		Screen screen = allScreens[(num2 + 1) % allScreens.Length];
		Rectangle workingArea = allScreens[num2].WorkingArea;
		double num3 = ((workingArea.Width > 0) ? ((double)(r.Left - workingArea.Left) / (double)workingArea.Width) : 0.0);
		double num4 = ((workingArea.Height > 0) ? ((double)(r.Top - workingArea.Top) / (double)workingArea.Height) : 0.0);
		Rectangle workingArea2 = screen.WorkingArea;
		int width = Math.Min(r.Width, workingArea2.Width);
		int height = Math.Min(r.Height, workingArea2.Height);
		int left = workingArea2.Left + (int)Math.Round(num3 * (double)workingArea2.Width);
		int top = workingArea2.Top + (int)Math.Round(num4 * (double)workingArea2.Height);
		left = Math.Max(workingArea2.Left, Math.Min(left, workingArea2.Right - width));
		top = Math.Max(workingArea2.Top, Math.Min(top, workingArea2.Bottom - height));
		return SetVisibleRect(num, new Native.RECT(left, top, left + width, top + height));
	}

	/// <summary>切换光标下窗口的"总在最前"。</summary>
	public static bool ToggleTopmostUnderCursor(out bool nowTopmost)
	{
		nowTopmost = false;
		nint num = TargetUnderCursor();
		if (num == IntPtr.Zero)
		{
			return false;
		}
		long windowLong = Native.GetWindowLong(num, -20);
		bool flag = (windowLong & 8) != 0;
		nint hWndInsertAfter = flag ? new IntPtr(-2) : new IntPtr(-1);
		Native.SetWindowPos(num, hWndInsertAfter, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
		nowTopmost = !flag;
		return true;
	}

	public static void SetDragRect(nint h, Native.RECT target)
	{
		if (h == IntPtr.Zero)
		{
			return;
		}
		bool windowRect = Native.GetWindowRect(h, out var lpRect);
		bool flag = !windowRect || lpRect.Right - lpRect.Left != target.Width || lpRect.Bottom - lpRect.Top != target.Height;
		if (!windowRect || flag || lpRect.Left != target.Left || lpRect.Top != target.Top)
		{
			if (Native.IsZoomed(h))
			{
				Native.ShowWindow(h, 9);
			}
			uint num;
			if (flag)
			{
				num = 532u;
			}
			else
			{
				num = ((!IsResizable(h)) ? 16917u : 533u);
			}
			if (flag && !Native.IsZoomed(h) && !KickUselessForSession && !LetWindowKickBack(h, ref target))
			{
				KickUselessForSession = true;
			}
			Native.SetWindowPos(h, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, num);
			if ((num & 0x4000) != 0)
			{
				Native.SendMessageTimeoutW(h, 136, IntPtr.Zero, IntPtr.Zero, 2u, 32u, out var _);
			}
		}
	}

	private static bool LetWindowKickBack(nint h, ref Native.RECT r)
	{
		if (!Native.GetWindowRect(h, out var lpRect))
		{
			return false;
		}
		bool flag = r.Left != lpRect.Left;
		bool flag2 = r.Right != lpRect.Right;
		bool flag3 = r.Top != lpRect.Top;
		bool flag4 = r.Bottom != lpRect.Bottom;
		int value;
		if (flag & flag3)
		{
			value = 4;
		}
		else if (flag2 & flag3)
		{
			value = 5;
		}
		else if (flag & flag4)
		{
			value = 7;
		}
		else if (flag2 & flag4)
		{
			value = 8;
		}
		else if (flag)
		{
			value = 1;
		}
		else if (flag2)
		{
			value = 2;
		}
		else if (flag3)
		{
			value = 3;
		}
		else
		{
			if (!flag4)
			{
				return false;
			}
			value = 6;
		}
		nint num = Marshal.AllocHGlobal(16);
		if (num == IntPtr.Zero)
		{
			return false;
		}
		try
		{
			Marshal.WriteInt32(num, 0, r.Left);
			Marshal.WriteInt32(num, 4, r.Top);
			Marshal.WriteInt32(num, 8, r.Right);
			Marshal.WriteInt32(num, 12, r.Bottom);
			if (Native.SendMessageTimeoutW(h, 532, new IntPtr(value), num, 2u, 16u, out var lpdwResult) == IntPtr.Zero || lpdwResult == IntPtr.Zero)
			{
				return false;
			}
			int num2 = Marshal.ReadInt32(num, 0);
			int num3 = Marshal.ReadInt32(num, 4);
			int num4 = Marshal.ReadInt32(num, 8);
			int num5 = Marshal.ReadInt32(num, 12);
			if (num4 - num2 < 32 || num5 - num3 < 32)
			{
				return false;
			}
			if (Math.Abs(num4 - num2) > 20000 || Math.Abs(num5 - num3) > 20000)
			{
				return false;
			}
			// 窗口没把矩形"踢"回来 = 它没有最小尺寸限制，这一轮之后就不必每帧再问了
			//（每次探测都要跨进程同步 SendMessage，是拖动里最贵的一次调用）。
			if (num2 == r.Left && num3 == r.Top && num4 == r.Right && num5 == r.Bottom)
			{
				return false;
			}
			r = new Native.RECT(num2, num3, num4, num5);
			return true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
		finally
		{
			Marshal.FreeHGlobal(num);
		}
	}

	public static void NotifySizeMove(nint h, bool start)
	{
		if (h == IntPtr.Zero)
		{
			return;
		}
		try
		{
			Native.PostMessageW(h, start ? 561 : 562, IntPtr.Zero, IntPtr.Zero);
			Native.NotifyWinEvent(start ? 10u : 11u, h, 0, 0);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public static void SetRect(nint h, Native.RECT r)
	{
		if (Native.IsZoomed(h))
		{
			Native.ShowWindow(h, 9);
		}
		Native.SetWindowPos(h, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, 16916u);
	}

	public static void MaximizeRestore(nint h)
	{
		if (Native.IsZoomed(h))
		{
			Native.ShowWindow(h, 9);
		}
		else
		{
			Native.ShowWindow(h, 3);
		}
	}

	public static void Minimize(nint h)
	{
		Native.ShowWindow(h, 6);
	}

	public static void CloseWindow(nint h)
	{
		Native.PostMessageW(h, 274, new IntPtr(61536), IntPtr.Zero);
	}

	public static void Lower(nint h)
	{
		Native.SetWindowPos(h, Native.HWND_BOTTOM, 0, 0, 0, 0, 16403u);
	}

	public static void ToggleTopMost(nint h)
	{
		nint hWndInsertAfter = (IsTopMost(h) ? Native.HWND_NOTOPMOST : Native.HWND_TOPMOST);
		Native.SetWindowPos(h, hWndInsertAfter, 0, 0, 0, 0, 16403u);
	}

	public static void Center(nint h)
	{
		if (Native.GetWindowRect(h, out var lpRect))
		{
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(new Native.POINT((lpRect.Left + lpRect.Right) / 2, (lpRect.Top + lpRect.Bottom) / 2));
			if (monitorInfoAt.rcWork.Width > 0)
			{
				int x = monitorInfoAt.rcWork.Left + (monitorInfoAt.rcWork.Width - lpRect.Width) / 2;
				int y = monitorInfoAt.rcWork.Top + (monitorInfoAt.rcWork.Height - lpRect.Height) / 2;
				Native.SetWindowPos(h, IntPtr.Zero, x, y, lpRect.Width, lpRect.Height, 16404u);
			}
		}
	}

	public static void ApplySnap(nint h, SnapZone zone, Native.RECT work)
	{
		switch (zone)
		{
		case SnapZone.None:
			break;
		case SnapZone.Maximize:
			if (!Native.IsZoomed(h))
			{
				Native.ShowWindow(h, 3);
			}
			break;
		default:
			SetRect(h, Zones.RectOf(zone, work));
			break;
		}
	}

	public static void Apply(nint h, string action)
	{
		switch (action)
		{
		case "Maximize":
			MaximizeRestore(h);
			break;
		case "Minimize":
			Minimize(h);
			break;
		case "Close":
			CloseWindow(h);
			break;
		case "Lower":
			Lower(h);
			break;
		case "AlwaysOnTop":
			ToggleTopMost(h);
			break;
		case "Center":
			Center(h);
			break;
		}
	}

	public static void ForceForeground(nint h)
	{
		try
		{
			nint foregroundWindow = Native.GetForegroundWindow();
			if (foregroundWindow == h)
			{
				return;
			}
			uint windowThreadProcessId = Native.GetWindowThreadProcessId(foregroundWindow, out var _);
			uint currentThreadId = Native.GetCurrentThreadId();
			bool flag = false;
			if (windowThreadProcessId != 0 && windowThreadProcessId != currentThreadId)
			{
				flag = Native.AttachThreadInput(windowThreadProcessId, currentThreadId, fAttach: true);
			}
			try
			{
				Native.SetWindowPos(h, Native.HWND_TOP, 0, 0, 0, 0, 19u);
				Native.SetForegroundWindow(h);
			}
			finally
			{
				if (flag)
				{
					Native.AttachThreadInput(windowThreadProcessId, currentThreadId, fAttach: false);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}
}
