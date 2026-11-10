// HoldDrag · Platform/Native.cs

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

internal static class Native
{
	internal struct POINT(int x, int y)
	{
		public int X = x;

		public int Y = y;

		public override string ToString()
		{
			return X + "," + Y;
		}
	}

	internal struct RECT(int l, int t, int r, int b)
	{
		public int Left = l;

		public int Top = t;

		public int Right = r;

		public int Bottom = b;

		public int Width => Right - Left;

		public int Height => Bottom - Top;

		public bool IsEmpty
		{
			get
			{
				if (Width > 0)
				{
					return Height <= 0;
				}
				return true;
			}
		}

		public override string ToString()
		{
			return Left + "," + Top + " " + Width + "x" + Height;
		}
	}

	internal struct KBDLLHOOKSTRUCT
	{
		public uint vkCode;

		public uint scanCode;

		public uint flags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct MSLLHOOKSTRUCT
	{
		public POINT pt;

		public uint mouseData;

		public uint flags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public uint dwFlags;
	}

	internal struct WINDOWPLACEMENT
	{
		public int length;

		public int flags;

		public int showCmd;

		public POINT ptMinPosition;

		public POINT ptMaxPosition;

		public RECT rcNormalPosition;
	}

	internal struct MSG
	{
		public nint hwnd;

		public uint message;

		public nint wParam;

		public nint lParam;

		public uint time;

		public int ptX;

		public int ptY;
	}

	internal struct INPUT
	{
		public uint type;

		public INPUTUNION u;
	}

	[StructLayout(LayoutKind.Explicit)]
	internal struct INPUTUNION
	{
		[FieldOffset(0)]
		public MOUSEINPUT mi;

		[FieldOffset(0)]
		public KEYBDINPUT ki;

		[FieldOffset(0)]
		public HARDWAREINPUT hi;
	}

	internal struct MOUSEINPUT
	{
		public int dx;

		public int dy;

		public uint mouseData;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct KEYBDINPUT
	{
		public ushort wVk;

		public ushort wScan;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct HARDWAREINPUT
	{
		public uint uMsg;

		public ushort wParamL;

		public ushort wParamH;
	}

	internal delegate nint HookProc(int nCode, nint wParam, nint lParam);

	internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	internal struct SIZE(int w, int h)
	{
		public int cx = w;

		public int cy = h;
	}

	internal struct ColorAccent(int r, int g, int b)
	{
		public int R = r;

		public int G = g;

		public int B = b;
	}

	internal struct BLENDFUNCTION
	{
		public byte BlendOp;

		public byte BlendFlags;

		public byte SourceConstantAlpha;

		public byte AlphaFormat;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct SECURITY_ATTRIBUTES
	{
		public int nLength;

		public nint lpSecurityDescriptor;

		[MarshalAs(UnmanagedType.Bool)]
		public bool bInheritHandle;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct GUITHREADINFO
	{
		public int cbSize;

		public int flags;

		public nint hwndActive;

		public nint hwndFocus;

		public nint hwndCapture;

		public nint hwndMenuOwner;

		public nint hwndMoveSize;

		public nint hwndCaret;

		public RECT rcCaret;
	}

	internal delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	internal struct WNDCLASSEX
	{
		public uint cbSize;

		public uint style;

		public WndProc lpfnWndProc;

		public int cbClsExtra;

		public int cbWndExtra;

		public nint hInstance;

		public nint hIcon;

		public nint hCursor;

		public nint hbrBackground;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string lpszMenuName;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string lpszClassName;

		public nint hIconSm;
	}

	public const int WH_KEYBOARD_LL = 13;

	public const int WH_MOUSE_LL = 14;

	public const int WM_KEYDOWN = 256;

	public const int WM_KEYUP = 257;

	public const int WM_SYSKEYDOWN = 260;

	public const int WM_SYSKEYUP = 261;

	public const int WM_MOUSEMOVE = 512;

	public const int WM_WINDOWPOSCHANGING = 70;

	public const int WM_WINDOWPOSCHANGED = 71;

	public const int WM_LBUTTONDOWN = 513;

	public const int WM_CANCELMODE = 31;

	public const int WM_LBUTTONUP = 514;

	public const int WM_RBUTTONDOWN = 516;

	public const int WM_RBUTTONUP = 517;

	public const int WM_MBUTTONDOWN = 519;

	public const int WM_MBUTTONUP = 520;

	public const int WM_MOUSEWHEEL = 522;

	public const int WM_XBUTTONDOWN = 523;

	public const int WM_XBUTTONUP = 524;

	public const int WM_MOUSEHWHEEL = 526;

	public const int MK_LBUTTON = 1;

	public const int MK_RBUTTON = 2;

	public const int MK_MBUTTON = 16;

	public const int WM_SYSCOMMAND = 274;

	public const int WM_CLOSE = 16;

	public const int SC_CLOSE = 61536;

	public const int SC_MAXIMIZE = 61488;

	public const int SC_MINIMIZE = 61472;

	public const int SC_RESTORE = 61728;

	public const uint LLKHF_EXTENDED = 1u;

	public const uint LLKHF_INJECTED = 16u;

	public const uint LLKHF_LOWER_IL_INJECTED = 2u;

	public const uint LLMHF_INJECTED = 1u;

	public const uint LLMHF_LOWER_IL_INJECTED = 2u;

	public const uint SWP_NOSIZE = 1u;

	public const uint SWP_NOMOVE = 2u;

	public const uint SWP_NOZORDER = 4u;

	public const uint SWP_NOACTIVATE = 16u;

	public const uint SWP_SHOWWINDOW = 64u;

	public const uint SWP_NOOWNERZORDER = 512u;

	public const uint SWP_NOSENDCHANGING = 1024u;

	public const uint SWP_NOCOPYBITS = 256u;

	public const uint SWP_ASYNCWINDOWPOS = 16384u;

	public const uint RESIZEFLAGS = 532u;

	public const uint MOVETHICKBORDERS = 533u;

	public const uint MOVEASYNC = 16917u;

	public const int WM_ENTERSIZEMOVE = 561;

	public const int WM_EXITSIZEMOVE = 562;

	public const int WM_SIZING = 532;

	public const int WM_SYNCPAINT = 136;

	public const uint SMTO_NORMAL = 0u;

	public const uint SMTO_ABORTIFHUNG = 2u;

	public const uint EVENT_SYSTEM_MOVESIZESTART = 10u;

	public const uint EVENT_SYSTEM_MOVESIZEEND = 11u;

	public const uint PM_NOREMOVE = 0u;

	public const uint PM_REMOVE = 1u;

	public const int WM_QUIT = 18;

	public static readonly nint HWND_TOP = IntPtr.Zero;

	public static readonly nint HWND_BOTTOM = new IntPtr(1);

	public static readonly nint HWND_TOPMOST = new IntPtr(-1);

	public static readonly nint HWND_NOTOPMOST = new IntPtr(-2);

	public const int SW_HIDE = 0;

	public const int SW_SHOWNORMAL = 1;

	public const int SW_SHOWMINIMIZED = 2;

	public const int SW_MAXIMIZE = 3;

	public const int SW_SHOWNOACTIVATE = 4;

	public const int SW_MINIMIZE = 6;

	public const int SW_RESTORE = 9;

	public const int GWL_STYLE = -16;

	public const int GWL_EXSTYLE = -20;

	public const long WS_VISIBLE = 268435456L;

	public const long WS_CAPTION = 12582912L;

	public const long WS_THICKFRAME = 262144L;

	public const long WS_MINIMIZE = 536870912L;

	public const long WS_MAXIMIZE = 16777216L;

	public const long WS_SYSMENU = 524288L;

	public const long WS_EX_LAYERED = 524288L;

	public const long WS_EX_TRANSPARENT = 32L;

	public const long WS_EX_TOOLWINDOW = 128L;

	public const long WS_EX_NOACTIVATE = 134217728L;

	public const long WS_EX_TOPMOST = 8L;

	public const uint GA_PARENT = 1u;

	public const uint GA_ROOT = 2u;

	public const uint GA_ROOTOWNER = 3u;

	public const uint MONITOR_DEFAULTTONULL = 0u;

	public const uint MONITOR_DEFAULTTOPRIMARY = 1u;

	public const uint MONITOR_DEFAULTTONEAREST = 2u;

	public const uint LWA_COLORKEY = 1u;

	public const uint LWA_ALPHA = 2u;

	public const uint INPUT_MOUSE = 0u;

	public const uint INPUT_KEYBOARD = 1u;

	public const uint MOUSEEVENTF_MOVE = 1u;

	public const uint MOUSEEVENTF_LEFTDOWN = 2u;

	public const uint MOUSEEVENTF_LEFTUP = 4u;

	public const uint MOUSEEVENTF_RIGHTDOWN = 8u;

	public const uint MOUSEEVENTF_RIGHTUP = 16u;

	public const uint MOUSEEVENTF_MIDDLEDOWN = 32u;

	public const uint MOUSEEVENTF_MIDDLEUP = 64u;

	public const uint MOUSEEVENTF_ABSOLUTE = 32768u;

	public const uint MOUSEEVENTF_VIRTUALDESK = 16384u;

	public const uint INJECT_LEFTDOWN = 2u;

	public const uint INJECT_LEFTUP = 4u;

	public const uint INJECT_RIGHTDOWN = 8u;

	public const uint INJECT_RIGHTUP = 16u;

	public const uint KEYEVENTF_EXTENDEDKEY = 1u;

	public const uint KEYEVENTF_KEYUP = 2u;

	public const uint KEYEVENTF_SCANCODE = 8u;

	public const ushort XBUTTON1 = 1;

	public const ushort XBUTTON2 = 2;

	public const int SM_CXMINTRACK = 34;

	public const int SM_CYMINTRACK = 35;

	public const int SM_CXSCREEN = 0;

	public const int SM_CYSCREEN = 1;

	internal const int WM_SETREDRAW = 11;

	internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 4096u;

	internal const int DPI_AWARENESS_INVALID = -1;

	internal const int DPI_AWARENESS_UNAWARE = 0;

	internal const int DPI_AWARENESS_SYSTEM_AWARE = 1;

	internal const int DPI_AWARENESS_PER_MONITOR_AWARE = 2;

	internal static readonly nint DPI_AWARENESS_CONTEXT_UNAWARE = new IntPtr(-1);

	internal static readonly nint DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new IntPtr(-2);

	internal static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new IntPtr(-3);

	internal static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

	internal const int DESKTOPHORZRES = 118;

	internal const int DESKTOPVERTRES = 117;

	internal const uint GW_HWNDNEXT = 2u;

	internal const uint GW_HWNDPREV = 3u;

	internal const uint CWP_SKIPINVISIBLE = 1u;

	internal const uint CWP_SKIPDISABLED = 2u;

	internal const uint CWP_SKIPTRANSPARENT = 4u;

	internal const int VK_LBUTTON = 1;

	internal const int VK_RBUTTON = 2;

	internal const int VK_MBUTTON = 4;

	internal const uint WM_NCHITTEST = 132u;

	internal const uint WM_ERASEBKGND = 20u;

	internal const uint WM_PAINT = 15u;

	internal const uint WM_DESTROY = 2u;

	internal const uint WM_MOUSEACTIVATE = 33u;

	internal const uint WM_APP = 32768u;

	internal const uint WM_DISPLAYCHANGE = 126u;

	internal const int HTTRANSPARENT = -1;

	internal const int MA_NOACTIVATE = 3;

	internal const uint WS_POPUP = 2147483648u;

	internal const uint WS_CLIPSIBLINGS = 67108864u;

	internal const uint ULW_ALPHA = 2u;

	internal const byte AC_SRC_OVER = 0;

	internal const byte AC_SRC_ALPHA = 1;

	internal const int RGN_AND = 1;

	internal const int RGN_OR = 2;

	internal const int RGN_DIFF = 4;

	internal const int VREFRESH = 116;

	internal const uint GR_GDIOBJECTS = 0u;

	internal const uint GR_USEROBJECTS = 1u;

	internal const int COLOR_HIGHLIGHT = 13;

	internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

	internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

	internal const int DWMWCP_ROUND = 2;

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint SetWindowsHookExW(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint GetModuleHandleW(string lpModuleName);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint WindowFromPoint(POINT Point);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetAncestor(nint hwnd, uint gaFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsWindow(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsZoomed(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

	/// <summary>
	/// 当前前台窗口的焦点控件是不是文本输入框。
	/// 用来避免把用户正在编辑的 Ctrl+Z 抢去撤销窗口位置。
	/// </summary>
	internal static bool IsTextInputFocused()
	{
		try
		{
			nint foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == IntPtr.Zero)
			{
				return false;
			}
			uint windowThreadProcessId = GetWindowThreadProcessId(foregroundWindow, out var _);
			if (windowThreadProcessId == 0)
			{
				return false;
			}
			GUITHREADINFO gUITHREADINFO = default(GUITHREADINFO);
			gUITHREADINFO.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
			if (!GetGUIThreadInfo(windowThreadProcessId, ref gUITHREADINFO) || gUITHREADINFO.hwndFocus == IntPtr.Zero)
			{
				return false;
			}
			System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder(64);
			GetClassNameW(gUITHREADINFO.hwndFocus, stringBuilder, stringBuilder.Capacity);
			string text = stringBuilder.ToString();
			return text.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0
				|| text.IndexOf("Scintilla", StringComparison.OrdinalIgnoreCase) >= 0
				|| text.IndexOf("TextBox", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch
		{
			return false;
		}
	}

	internal static extern nint GetForegroundWindow();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SendMessageW(nint hWnd, int Msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool PostMessageW(nint hWnd, int Msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool CloseHandle(nint hObject);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int GetWindowTextLengthW(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int GetSystemMetrics(int nIndex);

	[DllImport("user32.dll")]
	internal static extern nint GetThreadDpiAwarenessContext();

	[DllImport("user32.dll")]
	internal static extern int GetAwarenessFromDpiAwarenessContext(nint value);

	[DllImport("user32.dll")]
	internal static extern uint GetDpiForWindow(nint hwnd);

	internal static int DpiOfWindow(nint hwnd)
	{
		try
		{
			if (hwnd != IntPtr.Zero)
			{
				uint dpiForWindow = GetDpiForWindow(hwnd);
				if (dpiForWindow >= 48 && dpiForWindow <= 960)
				{
					return (int)dpiForWindow;
				}
			}
		}
		catch
		{
		}
		return 96;
	}

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetProcessDpiAwarenessContext(nint value);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetProcessDpiAware();

	[DllImport("user32.dll")]
	internal static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint GetDoubleClickTime();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetParent(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetLayeredWindowAttributes(nint hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetWindow(nint hWnd, uint uCmd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetDesktopWindow();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint ChildWindowFromPointEx(nint hWndParent, POINT pt, uint uFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern short GetAsyncKeyState(int vKey);

	/// <summary>
	/// 给控件关掉系统主题绘制。传「 」是官方支持的写法：
	/// ComboBox 自带的下拉按钮会画一块不跟随深色主题的白底方块（浅色下也突兀），
	/// 关掉之后完全由我们自绘，圆角和配色才能跟别的控件一致。
	/// </summary>
	[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
	internal static extern int SetWindowTheme(nint hwnd, string pszSubAppName, string pszSubIdList);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint FindWindowW(string lpClassName, string lpWindowName);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SetCapture(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	internal static extern nint GetCapture();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

	[DllImport("kernel32.dll")]
	internal static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint DispatchMessageW(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SendMessageTimeoutW(nint hWnd, int msg, nint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern void NotifyWinEvent(uint eventId, nint hWnd, int idObject, int idChild);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetClipCursor(out RECT lpRect);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
	private static extern int GetWindowLong32(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
	private static extern nint GetWindowLong64(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
	private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	private static extern nint SetWindowLong64(nint hWnd, int nIndex, nint dwNewLong);

	internal static long GetWindowLong(nint hWnd, int nIndex)
	{
		if (IntPtr.Size == 8)
		{
			return ((IntPtr)GetWindowLong64(hWnd, nIndex)).ToInt64();
		}
		return GetWindowLong32(hWnd, nIndex);
	}

	internal static void SetWindowLong(nint hWnd, int nIndex, long value)
	{
		if (IntPtr.Size == 8)
		{
			SetWindowLong64(hWnd, nIndex, new IntPtr(value));
		}
		else
		{
			SetWindowLong32(hWnd, nIndex, (int)value);
		}
	}

	[DllImport("shell32.dll", SetLastError = true)]
	internal static extern bool IsUserAnAdmin();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool DestroyIcon(nint hIcon);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	internal static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool DestroyWindow(nint hWnd);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateRectRgn(int x1, int y1, int x2, int y2);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern int CombineRgn(nint hrgnDst, nint hrgnSrc1, nint hrgnSrc2, int iMode);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern bool DeleteObject(nint ho);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ValidateRect(nint hWnd, nint lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetClientRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

	[DllImport("user32.dll")]
	internal static extern int FillRect(nint hDC, ref RECT lprc, nint hbr);

	[DllImport("gdi32.dll")]
	internal static extern nint CreateSolidBrush(uint crColor);

	[DllImport("user32.dll")]
	internal static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);

	internal static uint ColorRef(int r, int g, int b)
	{
		return (uint)(r | (g << 8) | (b << 16));
	}

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize, nint hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateCompatibleDC(nint hdc);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint SelectObject(nint hdc, nint h);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern bool DeleteDC(nint hdc);

	[DllImport("user32.dll")]
	internal static extern nint GetDC(nint hWnd);

	[DllImport("user32.dll")]
	internal static extern int ReleaseDC(nint hWnd, nint hDC);

	[DllImport("gdi32.dll")]
	internal static extern uint GetPixel(nint hdc, int x, int y);

	[DllImport("gdi32.dll")]
	internal static extern int GetDeviceCaps(nint hdc, int nIndex);

	[DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
	internal static extern uint TimeBeginPeriod(uint uMilliseconds);

	[DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
	internal static extern uint TimeEndPeriod(uint uMilliseconds);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint GetGuiResources(nint hProcess, uint uiFlags);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern nint GetCurrentProcess();

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool TerminateProcess(nint hProcess, uint uExitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool GetProcessHandleCount(nint hProcess, out uint pdwHandleCount);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

	[DllImport("user32.dll")]
	internal static extern uint GetSysColor(int nIndex);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);


	// 标题栏颜色和页面底色对齐：否则内容换成深色、最上面那条还白着，整扇窗就"断层"了。
	// Win11 22000+ 支持 DWMWA_CAPTION_COLOR(35) / TEXT_COLOR(36) / BORDER_COLOR(34)，能刷成任意色；
	// 老系统上这三个属性会失败，退回"深色标题栏"(20，再退 19)——至少不再白得刺眼。
	// 返回是否成功刷上了自定义颜色。
	internal static bool SetTitleBarTheme(nint hwnd, bool dark, Color caption, Color text, Color border)
	{
		try
		{
			int pvAttribute = ToColorRef(caption);
			bool flag = DwmSetWindowAttribute(hwnd, 35, ref pvAttribute, 4) == 0;
			int pvAttribute2 = ToColorRef(text);
			DwmSetWindowAttribute(hwnd, 36, ref pvAttribute2, 4);
			int pvAttribute3 = ToColorRef(border);
			DwmSetWindowAttribute(hwnd, 34, ref pvAttribute3, 4);
			if (!flag)
			{
				int pvAttribute4 = (dark ? 1 : 0);
				if (DwmSetWindowAttribute(hwnd, 20, ref pvAttribute4, 4) != 0)
				{
					DwmSetWindowAttribute(hwnd, 19, ref pvAttribute4, 4);
				}
			}
			return flag;
		}
		catch
		{
			return false;
		}
	}

	internal static int ToColorRef(Color color)
	{
		return color.R | (color.G << 8) | (color.B << 16);
	}

	[DllImport("dwmapi.dll")]
	internal static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

	internal static int GetWindowIntAttribute(nint hwnd, int attribute)
	{
		try
		{
			int pvAttribute;
			if (DwmGetWindowAttribute(hwnd, attribute, out pvAttribute, 4) == 0)
			{
				return pvAttribute;
			}
		}
		catch
		{
		}
		return -1;
	}

	/// <summary>
	/// 让系统标题栏跟着深色主题走（Win10 20H1+ 是 19 号属性，Win11 改成 20 号）。
	/// 不设的话，深色弹窗顶上会挂一条刺眼的白标题栏 —— 这正是「两个世界」的来源。
	/// </summary>
	internal static bool SetDarkTitleBar(nint hwnd, bool dark)
	{
		try
		{
			int pvAttribute = (dark ? 1 : 0);
			if (DwmSetWindowAttribute(hwnd, 20, ref pvAttribute, 4) == 0)
			{
				return true;
			}
			return DwmSetWindowAttribute(hwnd, 19, ref pvAttribute, 4) == 0;
		}
		catch
		{
			return false;
		}
	}

	internal static bool SetWindowRoundedCorners(nint hwnd)
	{
		try
		{
			int pvAttribute = 2;
			return DwmSetWindowAttribute(hwnd, 33, ref pvAttribute, 4) == 0;
		}
		catch
		{
			return false;
		}
	}

	internal static int GetRefreshRate()
	{
		try
		{
			nint dC = GetDC(IntPtr.Zero);
			if (dC != IntPtr.Zero)
			{
				try
				{
					int deviceCaps = GetDeviceCaps(dC, 116);
					if (deviceCaps >= 24 && deviceCaps <= 1000)
					{
						return deviceCaps;
					}
				}
				finally
				{
					ReleaseDC(IntPtr.Zero, dC);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return 60;
	}

	internal static ColorAccent GetAccent()
	{
		try
		{
			if (DwmGetColorizationColor(out var pcrColorization, out var _) == 0)
			{
				int num = (int)((pcrColorization >> 16) & 0xFF);
				int num2 = (int)((pcrColorization >> 8) & 0xFF);
				int num3 = (int)(pcrColorization & 0xFF);
				if (num + num2 + num3 > 60)
				{
					return new ColorAccent(num, num2, num3);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		try
		{
			uint sysColor = GetSysColor(13);
			int num4 = (int)(sysColor & 0xFF);
			int num5 = (int)((sysColor >> 8) & 0xFF);
			int num6 = (int)((sysColor >> 16) & 0xFF);
			if (num4 + num5 + num6 > 60)
			{
				return new ColorAccent(num4, num5, num6);
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		return new ColorAccent(0, 120, 212);
	}

	internal static bool TryGetVisibleRect(nint hwnd, out RECT r)
	{
		r = default;
		try
		{
			RECT pvAttribute = default;
			if (DwmGetWindowAttribute(hwnd, 9, out pvAttribute, Marshal.SizeOf(typeof(RECT))) == 0 && pvAttribute.Width > 0 && pvAttribute.Height > 0)
			{
				r = pvAttribute;
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return false;
	}

	internal static MONITORINFO GetMonitorInfoAt(POINT pt)
	{
		MONITORINFO lpmi = new MONITORINFO
		{
			cbSize = Marshal.SizeOf(typeof(MONITORINFO))
		};
		nint num = MonitorFromPoint(pt, 2u);
		if (num != IntPtr.Zero)
		{
			GetMonitorInfoW(num, ref lpmi);
		}
		return lpmi;
	}

	internal static bool IsEqualRect(RECT a, RECT b)
	{
		if (a.Left == b.Left && a.Top == b.Top && a.Right == b.Right)
		{
			return a.Bottom == b.Bottom;
		}
		return false;
	}

	internal static string GetClassName(nint hWnd)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		GetClassNameW(hWnd, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}

	internal static string GetWindowTitle(nint hWnd)
	{
		int windowTextLengthW = GetWindowTextLengthW(hWnd);
		if (windowTextLengthW <= 0)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(windowTextLengthW + 2);
		GetWindowTextW(hWnd, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}
}
