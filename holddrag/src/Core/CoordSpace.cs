// HoldDrag · Core/CoordSpace.cs

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

internal static class CoordSpace
{
	private static double _scale = 1.0;

	private static bool _initialized;

	public static double Scale => _scale;

	public static bool IsIdentity => _scale == 1.0;

	public static void Init()
	{
		if (_initialized)
		{
			return;
		}
		_initialized = true;
		try
		{
			int num = -1;
			try
			{
				num = Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext());
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			if (num != 0)
			{
				_scale = 1.0;
				return;
			}
			int systemMetrics = Native.GetSystemMetrics(0);
			int num2 = PhysicalScreenWidth();
			if (systemMetrics > 0 && num2 > 0 && num2 != systemMetrics)
			{
				_scale = (double)systemMetrics / (double)num2;
				Log.Info("★ 进程是 DPI 不感知的，坐标将按 " + _scale.ToString("0.####") + " 换算（逻辑宽 " + systemMetrics + " / 物理宽 " + num2 + "）");
			}
			else
			{
				Log.Info("★ 进程是 DPI 不感知的，但查不到物理分辨率，坐标保持原样（逻辑宽 " + systemMetrics + "，物理宽 " + num2 + "）");
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
			_scale = 1.0;
		}
	}

	private static int PhysicalScreenWidth()
	{
		try
		{
			nint dC = Native.GetDC(IntPtr.Zero);
			if (dC == IntPtr.Zero)
			{
				return 0;
			}
			try
			{
				return Native.GetDeviceCaps(dC, 118);
			}
			finally
			{
				Native.ReleaseDC(IntPtr.Zero, dC);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return 0;
	}

	internal static void SetScaleForTest(double s)
	{
		_initialized = true;
		_scale = ((s > 0.0) ? s : 1.0);
	}

	public static Native.POINT ToProcess(Native.POINT pt)
	{
		if (_scale == 1.0)
		{
			return pt;
		}
		return new Native.POINT((int)Math.Round((double)pt.X * _scale), (int)Math.Round((double)pt.Y * _scale));
	}

	public static Native.POINT ToPhysical(Native.POINT pt)
	{
		if (_scale == 1.0)
		{
			return pt;
		}
		return new Native.POINT((int)Math.Round((double)pt.X / _scale), (int)Math.Round((double)pt.Y / _scale));
	}
}
