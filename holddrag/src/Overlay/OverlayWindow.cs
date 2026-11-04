// HoldDrag · Overlay/OverlayWindow.cs

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

internal abstract class OverlayWindow : IDisposable
{
	private const uint WM_APPLY = 32769u;

	private const uint WM_RETHEME = 32770u;

	private static readonly object ClassGate = new object();

	private static string _className;

	private static Native.WndProc _classProc;

	private static readonly Dictionary<nint, OverlayWindow> Instances = new Dictionary<nint, OverlayWindow>();

	private nint _hwnd = IntPtr.Zero;

	private bool _destroyed;

	private readonly object _pendingGate = new object();

	private Native.RECT _pending;

	private bool _pendingValid;

	private bool _hidePending;

	private bool _applyPosted;

	private volatile bool _rethemePending;

	private bool _rethemePosted;

	private bool _visible;

	private const uint WM_CREATE_OVERLAYS = 32779u;

	private const uint WM_DESTROY_OVERLAY = 32780u;

	private static Thread _ownerThread;

	private static uint _ownerThreadId;

	private static readonly object OwnerGate = new object();

	private static readonly ManualResetEventSlim _ownerReady = new ManualResetEventSlim(initialState: false);

	private static readonly List<OverlayWindow> _pendingCreate = new List<OverlayWindow>();

	private static volatile bool _ownerQuit;

	private readonly ManualResetEventSlim _created = new ManualResetEventSlim(initialState: false);

	private Native.RECT _lastApplied;

	private bool _lastAppliedValid;

	private int _lastW = -1;

	private int _lastH = -1;

	protected bool IsVisible => _visible;

	public nint Handle => _hwnd;

	public bool IsCreated => _hwnd != IntPtr.Zero;

	protected virtual bool LayeredStyle => true;

	internal static uint OwnerThreadIdForTest => _ownerThreadId;

	internal bool ShowRequestedForTest
	{
		get
		{
			lock (_pendingGate)
			{
				return _pendingValid && !_hidePending;
			}
		}
	}

	public void EnsureCreated()
	{
		if (_hwnd != IntPtr.Zero)
		{
			return;
		}
		StartOwnerThread();
		if (Native.GetCurrentThreadId() == _ownerThreadId)
		{
			CreateNow();
			return;
		}
		lock (OwnerGate)
		{
			_pendingCreate.Add(this);
			_created.Reset();
		}
		if (!Native.PostThreadMessageW(_ownerThreadId, 32779u, IntPtr.Zero, IntPtr.Zero))
		{
			lock (OwnerGate)
			{
				_pendingCreate.Remove(this);
			}
			Log.Error("投递浮层创建消息失败，退回当前线程创建");
			CreateNow();
		}
		else
		{
			_created.Wait(3000);
		}
	}

	private void CreateNow()
	{
		if (_hwnd != IntPtr.Zero)
		{
			return;
		}
		EnsureClass();
		uint num = 134217864u;
		if (LayeredStyle)
		{
			num |= 0x80000;
		}
		_hwnd = Native.CreateWindowExW(num, _className, "HoldDrag Overlay", 2147483648u, -32000, -32000, 1, 1, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandleW(null), IntPtr.Zero);
		if (_hwnd == IntPtr.Zero)
		{
			Log.Error("创建浮层窗口失败，Win32 错误码 " + Marshal.GetLastWin32Error());
			return;
		}
		lock (ClassGate)
		{
			Instances[_hwnd] = this;
		}
	}

	public static void StartOwnerThread()
	{
		lock (OwnerGate)
		{
			if (_ownerThread != null)
			{
				return;
			}
			_ownerThread = new Thread(OwnerThreadMain);
			_ownerThread.IsBackground = true;
			_ownerThread.Priority = ThreadPriority.AboveNormal;
			_ownerThread.Name = "HoldDrag.Overlays";
			_ownerThread.Start();
		}
		_ownerReady.Wait(3000);
	}

	private static void OwnerThreadMain()
	{
		try
		{
			_ownerThreadId = Native.GetCurrentThreadId();
			Native.PeekMessageW(out var _, IntPtr.Zero, 0u, 0u, 0u);
			_ownerReady.Set();
			Native.MSG lpMsg2;
			while (!_ownerQuit && Native.GetMessageW(out lpMsg2, IntPtr.Zero, 0u, 0u) > 0)
			{
				if (lpMsg2.hwnd == IntPtr.Zero)
				{
					if (lpMsg2.message == 32779)
					{
						CreatePendingNow();
					}
					else if (lpMsg2.message == 32780)
					{
						try
						{
							Native.DestroyWindow(lpMsg2.wParam);
						}
						catch (Exception ex)
						{
							Log.Error(ex);
						}
					}
				}
				else
				{
					Native.TranslateMessage(ref lpMsg2);
					Native.DispatchMessageW(ref lpMsg2);
				}
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		finally
		{
			_ownerReady.Set();
		}
	}

	private static void CreatePendingNow()
	{
		OverlayWindow[] array;
		lock (OwnerGate)
		{
			array = _pendingCreate.ToArray();
			_pendingCreate.Clear();
		}
		for (int i = 0; i < array.Length; i++)
		{
			try
			{
				array[i].CreateNow();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			finally
			{
				array[i]._created.Set();
			}
		}
	}

	private static void EnsureClass()
	{
		lock (ClassGate)
		{
			if (_className == null)
			{
				_classProc = OverlayWndProc;
				Native.WNDCLASSEX lpwcx = new Native.WNDCLASSEX
				{
					cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX)),
					style = 0u,
					lpfnWndProc = _classProc,
					hInstance = Native.GetModuleHandleW(null),
					hCursor = IntPtr.Zero,
					hbrBackground = IntPtr.Zero,
					lpszClassName = "HoldDragOverlayWindow"
				};
				Native.RegisterClassExW(ref lpwcx);
				_className = lpwcx.lpszClassName;
			}
		}
	}

	public void Destroy()
	{
		if (_destroyed)
		{
			return;
		}
		_destroyed = true;
		nint hwnd = _hwnd;
		_hwnd = IntPtr.Zero;
		if (hwnd != IntPtr.Zero)
		{
			lock (ClassGate)
			{
				Instances.Remove(hwnd);
			}
			if (Native.GetCurrentThreadId() == _ownerThreadId)
			{
				try
				{
					Native.DestroyWindow(hwnd);
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			}
			else
			{
				try
				{
					Native.PostThreadMessageW(_ownerThreadId, 32780u, hwnd, IntPtr.Zero);
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
				}
			}
		}
		try
		{
			FreeResources();
		}
		catch (Exception ex3)
		{
			Log.Error(ex3);
		}
	}

	public void Dispose()
	{
		Destroy();
		GC.SuppressFinalize(this);
	}

	protected virtual void FreeResources()
	{
	}

	protected virtual bool HandlePaint()
	{
		return false;
	}

	public void SetRect(Native.RECT r)
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		if (r.Width <= 0 || r.Height <= 0)
		{
			HideOverlay();
			return;
		}
		bool flag;
		lock (_pendingGate)
		{
			if ((_pendingValid && SameRect(_pending, r)) || (!_pendingValid && !_hidePending && _lastAppliedValid && SameRect(_lastApplied, r) && _visible))
			{
				return;
			}
			_pending = r;
			_pendingValid = true;
			_hidePending = false;
			flag = !_applyPosted;
			if (flag)
			{
				_applyPosted = true;
			}
		}
		if (!flag || Native.PostMessageW(_hwnd, 32769, IntPtr.Zero, IntPtr.Zero))
		{
			return;
		}
		lock (_pendingGate)
		{
			_applyPosted = false;
		}
	}

	private static bool SameRect(Native.RECT a, Native.RECT b)
	{
		if (a.Left == b.Left && a.Top == b.Top && a.Right == b.Right)
		{
			return a.Bottom == b.Bottom;
		}
		return false;
	}

	public void HideOverlay()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		bool flag;
		lock (_pendingGate)
		{
			_pendingValid = false;
			_hidePending = true;
			flag = !_applyPosted;
			if (flag)
			{
				_applyPosted = true;
			}
		}
		if (!flag || Native.PostMessageW(_hwnd, 32769, IntPtr.Zero, IntPtr.Zero))
		{
			return;
		}
		lock (_pendingGate)
		{
			_applyPosted = false;
		}
	}

	protected void RequestRetheme()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		_rethemePending = true;
		bool flag;
		lock (_pendingGate)
		{
			flag = !_rethemePosted;
			if (flag)
			{
				_rethemePosted = true;
			}
		}
		if (!flag || Native.PostMessageW(_hwnd, 32770, IntPtr.Zero, IntPtr.Zero))
		{
			return;
		}
		lock (_pendingGate)
		{
			_rethemePosted = false;
			_rethemePending = false;
		}
	}

	private void DrainRetheme()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		lock (_pendingGate)
		{
			_rethemePosted = false;
		}
		if (!_rethemePending)
		{
			return;
		}
		_rethemePending = false;
		try
		{
			OnRetheme();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	protected virtual void OnRetheme()
	{
	}

	private static nint OverlayWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
	{
		try
		{
			switch (msg)
			{
			case 132u:
				return new IntPtr(-1);
			case 33u:
				return new IntPtr(3);
			case 20u:
				return new IntPtr(1);
			default:
			{
				OverlayWindow value;
				lock (ClassGate)
				{
					if (!Instances.TryGetValue(hwnd, out value))
					{
						value = null;
					}
				}
				if (value != null)
				{
					switch (msg)
					{
					case 32769u:
						value.DrainApply();
						return IntPtr.Zero;
					case 32770u:
						value.DrainRetheme();
						return IntPtr.Zero;
					case 2u:
						return IntPtr.Zero;
					}
				}
				if (msg != 15)
				{
					break;
				}
				bool flag = false;
				if (value != null)
				{
					try
					{
						flag = value.HandlePaint();
					}
					catch (Exception ex)
					{
						Log.Error(ex);
					}
				}
				if (!flag)
				{
					try
					{
						Native.ValidateRect(hwnd, IntPtr.Zero);
					}
					catch
					{
					}
				}
				return IntPtr.Zero;
			}
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
	}

	protected void MarkDirectWrite(Native.RECT r)
	{
		lock (_pendingGate)
		{
			_lastApplied = r;
			_lastAppliedValid = true;
			_pending = r;
			_pendingValid = true;
			_hidePending = false;
		}
		_visible = true;
	}

	protected void MarkDirectHide()
	{
		lock (_pendingGate)
		{
			_pendingValid = false;
			_hidePending = false;
			_lastAppliedValid = false;
		}
		_visible = false;
	}

	private void DrainApply()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		bool pendingValid;
		Native.RECT pending;
		lock (_pendingGate)
		{
			_applyPosted = false;
			pendingValid = _pendingValid;
			pending = _pending;
			_pendingValid = false;
			_hidePending = false;
		}
		try
		{
			if (!pendingValid)
			{
				HideNow();
			}
			else
			{
				ApplyNow(pending);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	protected void HideNow()
	{
		lock (_pendingGate)
		{
			_lastAppliedValid = false;
		}
		_visible = false;
		try
		{
			if (_hwnd != IntPtr.Zero)
			{
				Native.ShowWindow(_hwnd, 0);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ApplyNow(Native.RECT r)
	{
		lock (_pendingGate)
		{
			_lastApplied = r;
			_lastAppliedValid = true;
		}
		if (r.Width != _lastW || r.Height != _lastH)
		{
			OnSizeChanged(r.Width, r.Height);
			_lastW = r.Width;
			_lastH = r.Height;
		}
		_visible = true;
		try
		{
			Native.ShowWindow(_hwnd, 4);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		OnPlace(r);
	}

	/// <summary>
	/// 让"尺寸缓存"失效。缓冲区被空闲释放 / 重建之后必须调用：
	/// <see cref="ApplyNow"/> 靠 _lastW/_lastH 判断"尺寸没变就跳过重建"，
	/// 缓存不失效就会在一次空闲释放之后，对着同尺寸的窗户永远不再重建外框。
	/// </summary>
	protected void InvalidateSizeCache()
	{
		_lastW = -1;
		_lastH = -1;
	}

	protected abstract void OnSizeChanged(int w, int h);

	protected abstract void OnPlace(Native.RECT r);

	protected static void MoveTopmost(nint hwnd, int x, int y, int w, int h)
	{
		Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 532u);
	}
}
