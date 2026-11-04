// HoldDrag · Input/HookWatchdog.cs

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

internal sealed class HookWatchdog : IDisposable
{
	private readonly HookEngine _hooks;

	private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

	private Native.POINT _prevCursor;

	private int _prevSeenX;

	private int _prevSeenY;

	private bool _hasPrev;

	private const int RequiredStrikes = 3;

	private const int CooldownMs = 5000;

	private const double CallbackStallAlertMs = 2000.0;

	private bool _stallAlerted;

	private int _strikes;

	private long _lastRescueAt;

	private long _prevSeenCount;

	public int Checks { get; private set; }

	public int Rescues { get; private set; }

	public HookWatchdog(HookEngine hooks)
	{
		_hooks = hooks;
		_timer.Interval = 1000;
		_timer.Tick += OnTick;
	}

	public void Start()
	{
		_hasPrev = false;
		_prevSeenX = 0;
		_prevSeenY = 0;
		Checks = 0;
		Rescues = 0;
		_timer.Start();
	}

	public void Stop()
	{
		try
		{
			_timer.Stop();
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		try
		{
			_timer.Stop();
			_timer.Dispose();
		}
		catch
		{
		}
	}

	private void OnTick(object sender, EventArgs e)
	{
		try
		{
			CheckOnce();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public bool CheckOnce()
	{
		Checks++;
		CheckCallbackStall();
		if (!Native.GetCursorPos(out var lpPoint))
		{
			return false;
		}
		long seenCount = _hooks.SeenCount;
		if (seenCount != _prevSeenCount)
		{
			_prevSeenCount = seenCount;
			_prevCursor = lpPoint;
			_strikes = 0;
			return false;
		}
		bool flag = _hooks.TryGetLastSeen(out var x, out var y);
		if (!_hasPrev || !flag)
		{
			_prevCursor = lpPoint;
			_prevSeenX = x;
			_prevSeenY = y;
			_prevSeenCount = seenCount;
			_hasPrev = true;
			return false;
		}
		bool num = lpPoint.X != _prevCursor.X || lpPoint.Y != _prevCursor.Y;
		bool flag2 = x != _prevSeenX || y != _prevSeenY;
		_prevCursor = lpPoint;
		_prevSeenX = x;
		_prevSeenY = y;
		if (num && !flag2)
		{
			_strikes++;
		}
		else
		{
			_strikes = 0;
		}
		if (_strikes < 3)
		{
			return false;
		}
		long tickCount = Environment.TickCount64;
		if (tickCount - _lastRescueAt < 5000)
		{
			return false;
		}
		_strikes = 0;
		_lastRescueAt = tickCount;
		Rescues++;
		_hooks.Reinstall();
		_hasPrev = false;
		return true;
	}

	private void CheckCallbackStall()
	{
		try
		{
			double callbackStalledMs = _hooks.CallbackStalledMs;
			if (callbackStalledMs >= 2000.0)
			{
				if (!_stallAlerted)
				{
					_stallAlerted = true;
					Log.Error("钩子回调已卡住 " + (int)callbackStalledMs + " ms —— 低级钩子回调正在阻塞，全系统键鼠事件都会排在它后面（整机输入无响应）。最常见的原因：钩子线程上出现了没有上限的同步跨进程窗口调用，请检查 DragEngine/WindowActions。");
				}
			}
			else if (_stallAlerted && callbackStalledMs == 0.0)
			{
				_stallAlerted = false;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}
}
