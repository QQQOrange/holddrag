// HoldDrag · Core/DragWorker.cs

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

internal sealed class DragWorker : IDisposable
{
	public const uint WM_DOMOUSEMOVE = 32775u;

	public const uint WM_DOWORK = 32774u;

	private readonly Action<Native.POINT> _onMove;

	private readonly ConcurrentQueue<Action> _work = new ConcurrentQueue<Action>();

	private Thread _thread;

	private uint _threadId;

	private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(initialState: false);

	private volatile int _frameIntervalMs;

	public static long MaxMoveMs;

	public static long MaxIdleMs;

	public static int MaxDrained;

	private volatile bool _disposed;

	public int FrameIntervalMs
	{
		get
		{
			return _frameIntervalMs;
		}
		set
		{
			_frameIntervalMs = ((value >= 0) ? value : 0);
		}
	}

	public bool IsRunning
	{
		get
		{
			if (_thread != null && _ready.IsSet)
			{
				return !_disposed;
			}
			return false;
		}
	}

	public static void ResetStats()
	{
		MaxMoveMs = 0L;
		MaxIdleMs = 0L;
		MaxDrained = 0;
	}

	public DragWorker(Action<Native.POINT> onMove)
	{
		_onMove = onMove;
	}

	public void Start()
	{
		if (_thread == null)
		{
			_thread = new Thread(ThreadMain);
			_thread.IsBackground = true;
			_thread.Priority = ThreadPriority.AboveNormal;
			_thread.Name = "HoldDrag.DragWorker";
			_thread.Start();
			if (!_ready.Wait(3000))
			{
				Log.Error("拖动工作线程启动超时");
			}
		}
	}

	public void PostMove(Native.POINT pt)
	{
		if (!_disposed && _threadId != 0 && !Native.PostThreadMessageW(_threadId, 32775u, new IntPtr(pt.X), new IntPtr(pt.Y)))
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			if (lastWin32Error != 0)
			{
				Log.Trace("投递鼠标移动失败，Win32 错误码 " + lastWin32Error);
			}
		}
	}

	public void Post(Action work)
	{
		if (work != null && !_disposed && _threadId != 0)
		{
			_work.Enqueue(work);
			if (!Native.PostThreadMessageW(_threadId, 32774u, IntPtr.Zero, IntPtr.Zero))
			{
				// 投递失败（线程消息队列满）时不能 TryDequeue：队列是 FIFO，
				// 出队的会是最早那条而不是刚入队的这条，丢旧的留新的，语义反了。
				// 让活计留在队列里，下次投递成功时一起处理。
				Log.Trace("投递任务失败（线程消息队列可能已满），任务已留在队列中");
			}
		}
	}

	private void ThreadMain()
	{
		_threadId = Native.GetCurrentThreadId();
		Native.PeekMessageW(out var _, IntPtr.Zero, 0u, 0u, 0u);
		_ready.Set();
		try
		{
			while (!_disposed)
			{
				long tickCount = Environment.TickCount64;
				int messageW = Native.GetMessageW(out var lpMsg2, IntPtr.Zero, 0u, 0u);
				long num = Environment.TickCount64 - tickCount;
				if (num > MaxIdleMs)
				{
					MaxIdleMs = num;
				}
				if (messageW == 0 || messageW < 0)
				{
					break;
				}
				if (lpMsg2.hwnd != IntPtr.Zero)
				{
					continue;
				}
				if (lpMsg2.message == 18)
				{
					break;
				}
				if (lpMsg2.message == 32774)
				{
					Action result;
					while (_work.TryDequeue(out result))
					{
						try
						{
							result();
						}
						catch (Exception ex)
						{
							Log.Error(ex);
						}
					}
				}
				else
				{
					if (lpMsg2.message != 32775)
					{
						continue;
					}
					int num2 = 0;
					Native.MSG lpMsg3;
					while (Native.PeekMessageW(out lpMsg3, IntPtr.Zero, 32775u, 32775u, 1u))
					{
						lpMsg2 = lpMsg3;
						num2++;
					}
					if (num2 > MaxDrained)
					{
						MaxDrained = num2;
					}
					if (num2 > 0 && Log.Verbose)
					{
						Log.Trace("合并掉了 " + num2 + " 个过期的鼠标移动位置");
					}
					Native.POINT obj = new Native.POINT((int)((IntPtr)lpMsg2.wParam).ToInt64(), (int)((IntPtr)lpMsg2.lParam).ToInt64());
					long tickCount2 = Environment.TickCount64;
					try
					{
						_onMove(obj);
					}
					catch (Exception ex2)
					{
						Log.Error(ex2);
					}
					long num3 = Environment.TickCount64 - tickCount2;
					if (num3 > MaxMoveMs)
					{
						MaxMoveMs = num3;
					}
					int frameIntervalMs = _frameIntervalMs;
					if (frameIntervalMs > 0)
					{
						long num4 = frameIntervalMs - (Environment.TickCount64 - tickCount2);
						if (num4 > 1)
						{
							Thread.Sleep((int)num4);
						}
						else if (num4 == 1)
						{
							Thread.Sleep(1);
						}
					}
				}
			}
		}
		catch (Exception ex3)
		{
			Log.Error(ex3);
		}
		finally
		{
			_ready.Set();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		try
		{
			if (_threadId != 0)
			{
				Native.PostThreadMessageW(_threadId, 18u, IntPtr.Zero, IntPtr.Zero);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		try
		{
			if (_thread != null && _thread.IsAlive)
			{
				_thread.Join(1000);
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		_thread = null;
		_threadId = 0u;
		Action result;
		while (_work.TryDequeue(out result))
		{
		}
	}
}
