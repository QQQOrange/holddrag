// HoldDrag · Input/HookEngine.cs

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

internal sealed class HookEngine : IDisposable
{
	public const uint WM_REHOOK_MOUSE = 32776u;

	private readonly IInputSink _sink;

	private readonly Native.HookProc _kbProc;

	private readonly Native.HookProc _msProc;

	private nint _kbHook = IntPtr.Zero;

	private nint _msHook = IntPtr.Zero;

	private Thread _hookThread;

	private uint _hookThreadId;

	private readonly ManualResetEventSlim _hookReady = new ManualResetEventSlim(initialState: false);

	private volatile bool _hookQuit;

	private volatile int _seenX;

	private volatile int _seenY;

	private volatile bool _seenValid;

	private long _callbackSinceTicks;

	private long _seenCount;

	public static readonly nint MagicExtraInfo = new IntPtr(1215261796);

	public int LastError { get; private set; }

	public bool KeyboardInstalled => _kbHook != IntPtr.Zero;

	public bool MouseInstalled => _msHook != IntPtr.Zero;

	public int ReinstallCount { get; private set; }

	public double CallbackStalledMs
	{
		get
		{
			long num = Interlocked.Read(in _callbackSinceTicks);
			if (num == 0L)
			{
				return 0.0;
			}
			double num2 = (double)(Stopwatch.GetTimestamp() - num) * 1000.0 / (double)Stopwatch.Frequency;
			if (!(num2 < 0.0))
			{
				return num2;
			}
			return 0.0;
		}
	}

	public long SeenCount => Interlocked.Read(in _seenCount);

	public bool TryGetLastSeen(out int x, out int y)
	{
		x = _seenX;
		y = _seenY;
		return _seenValid;
	}

	public HookEngine(IInputSink sink)
	{
		_sink = sink;
		_kbProc = KeyboardCallback;
		_msProc = MouseCallback;
	}

	public bool Install()
	{
		if (_kbHook != IntPtr.Zero && _msHook != IntPtr.Zero)
		{
			return true;
		}
		LastError = 0;
		_hookQuit = false;
		_hookReady.Reset();
		_hookThread = new Thread(HookThreadMain);
		_hookThread.IsBackground = true;
		_hookThread.Priority = ThreadPriority.AboveNormal;
		_hookThread.Name = "HoldDrag.HookThread";
		_hookThread.Start();
		if (!_hookReady.Wait(3000))
		{
			Log.Error("钩子线程启动超时");
		}
		if (KeyboardInstalled)
		{
			return MouseInstalled;
		}
		return false;
	}

	private void HookThreadMain()
	{
		try
		{
			_hookThreadId = Native.GetCurrentThreadId();
			Native.PeekMessageW(out var _, IntPtr.Zero, 0u, 0u, 0u);
			nint moduleHandleW = Native.GetModuleHandleW(null);
			if (_kbHook == IntPtr.Zero)
			{
				_kbHook = Native.SetWindowsHookExW(13, _kbProc, moduleHandleW, 0u);
				if (_kbHook == IntPtr.Zero)
				{
					LastError = Marshal.GetLastWin32Error();
					Log.Error("键盘钩子安装失败，Win32 错误码 " + LastError);
				}
			}
			if (_msHook == IntPtr.Zero)
			{
				_msHook = Native.SetWindowsHookExW(14, _msProc, moduleHandleW, 0u);
				Native.POINT lpPoint;
				if (_msHook == IntPtr.Zero)
				{
					LastError = Marshal.GetLastWin32Error();
					Log.Error("鼠标钩子安装失败，Win32 错误码 " + LastError);
				}
				else if (Native.GetCursorPos(out lpPoint))
				{
					_seenX = lpPoint.X;
					_seenY = lpPoint.Y;
					_seenValid = true;
				}
			}
			_hookReady.Set();
			Native.MSG lpMsg2;
			while (!_hookQuit && Native.GetMessageW(out lpMsg2, IntPtr.Zero, 0u, 0u) > 0)
			{
				if (lpMsg2.message == 32776)
				{
					ReinstallMouseOnThisThread();
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			_hookReady.Set();
		}
	}

	private void ReinstallMouseOnThisThread()
	{
		if (_hookQuit)
		{
			return;
		}
		try
		{
			if (_msHook != IntPtr.Zero)
			{
				Native.UnhookWindowsHookEx(_msHook);
				_msHook = IntPtr.Zero;
			}
			nint moduleHandleW = Native.GetModuleHandleW(null);
			_msHook = Native.SetWindowsHookExW(14, _msProc, moduleHandleW, 0u);
			if (_msHook == IntPtr.Zero)
			{
				LastError = Marshal.GetLastWin32Error();
				Log.Error("抢占链头：鼠标钩子重装失败，Win32 错误码 " + LastError);
				return;
			}
			if (Native.GetCursorPos(out var lpPoint))
			{
				_seenX = lpPoint.X;
				_seenY = lpPoint.Y;
				_seenValid = true;
			}
			ReinstallCount++;
			Log.Info("鼠标钩子已就地重装并顶到钩子链头部（激活时抢占，第三方手势工具不再先拿到鼠标键）");
			// 这里刻意【不】触发 HooksReinstalled：这是激活瞬间「抢占链头」的计划内重装，
			// 一瞬间完成、不丢事件。若当成钩子出事，Coordinator 会在刚激活的同一刻把触发状态复位，
			// 用户看到的就是「按了触发键，外框闪一下就没了、鼠标完全拖不动」。
			// 只有 Reinstall()（看门狗救援，真的会丢事件）才广播。
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public bool ReinstallMouseForChainPriority()
	{
		uint hookThreadId = _hookThreadId;
		if (hookThreadId != 0 && _msHook != IntPtr.Zero)
		{
			return Native.PostThreadMessageW(hookThreadId, 32776u, IntPtr.Zero, IntPtr.Zero);
		}
		return Reinstall();
	}

	public bool Reinstall()
	{
		Log.Info("看门狗判定键鼠钩子已失效，正在重新安装");
		Uninstall();
		_seenValid = false;
		bool flag = Install();
		ReinstallCount++;
		Log.Info("钩子重装" + (flag ? "成功" : ("仍然失败，错误码 " + LastError)));
		if (flag)
		{
			RaiseReinstalled();
		}
		return flag;
	}

	/// <summary>
	/// 钩子重装完成后触发。重装期间的按键/鼠标事件全部丢失，
	/// 依赖"按下与抬起成对到达"的状态（触发键按住态、拖动会话）必须借此复位。
	/// </summary>
	internal event Action HooksReinstalled;

	private void RaiseReinstalled()
	{
		try
		{
			HooksReinstalled?.Invoke();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void EnterCallback()
	{
		Interlocked.Exchange(ref _callbackSinceTicks, Stopwatch.GetTimestamp());
	}

	private void LeaveCallback()
	{
		Interlocked.Exchange(ref _callbackSinceTicks, 0L);
	}

	private void MarkSeen(Native.MSLLHOOKSTRUCT ms)
	{
		_seenX = ms.pt.X;
		_seenY = ms.pt.Y;
		_seenValid = true;
		Interlocked.Increment(ref _seenCount);
	}

	public void Uninstall()
	{
		// 先立"正在退出"的旗子，队列里已经排着的 WM_REHOOK_MOUSE 才会被丢掉。
		// 不立的话，那个消息会被即将结束的钩子线程处理，把鼠标钩子重新装在一条马上要消失的线程上：
		// 句柄非零、钩子实际已死，下一次 Install() 看到 _msHook != 0 就跳过安装，于是拖动彻底失灵。
		_hookQuit = true;
		if (_kbHook != IntPtr.Zero)
		{
			Native.UnhookWindowsHookEx(_kbHook);
			_kbHook = IntPtr.Zero;
		}
		if (_msHook != IntPtr.Zero)
		{
			Native.UnhookWindowsHookEx(_msHook);
			_msHook = IntPtr.Zero;
		}
		if (_hookThreadId != 0)
		{
			Native.PostThreadMessageW(_hookThreadId, 18u, IntPtr.Zero, IntPtr.Zero);
			_hookThreadId = 0u;
		}
	}

	public void Dispose()
	{
		Uninstall();
	}

	private nint KeyboardCallback(int nCode, nint wParam, nint lParam)
	{
		EnterCallback();
		try
		{
			if (nCode >= 0 && _sink != null)
			{
				Native.KBDLLHOOKSTRUCT kb = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
				if (kb.dwExtraInfo != MagicExtraInfo)
				{
					int num = (int)((IntPtr)wParam).ToInt64();
					bool flag = num == 256 || num == 260;
					bool flag2 = num == 257 || num == 261;
					if ((flag | flag2) && _sink.OnKeyboard(flag, ref kb))
					{
						return new IntPtr(1);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			LeaveCallback();
		}
		return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
	}

	private nint MouseCallback(int nCode, nint wParam, nint lParam)
	{
		EnterCallback();
		try
		{
			if (nCode >= 0 && _sink != null)
			{
				Native.MSLLHOOKSTRUCT ms = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
				ms.pt = CoordSpace.ToProcess(ms.pt);
				MarkSeen(ms);
				int num = (int)((IntPtr)wParam).ToInt64();
				if (Log.Verbose && (num == 513 || num == 514 || num == 516 || num == 517 || num == 519 || num == 520))
				{
					Log.Trace("[探针] 钩子收到按键 msg=" + num + " flags=0x" + ms.flags.ToString("X") + " extra=" + ((IntPtr)ms.dwExtraInfo).ToInt64().ToString("X"));
				}
				if (ms.dwExtraInfo != MagicExtraInfo && _sink.OnMouse(num, ref ms))
				{
					return new IntPtr(1);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			LeaveCallback();
		}
		return Native.CallNextHookEx(_msHook, nCode, wParam, lParam);
	}
}
