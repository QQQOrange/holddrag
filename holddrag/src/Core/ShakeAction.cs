// HoldDrag · Core/ShakeAction.cs

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

internal static class ShakeAction
{
	private static readonly object Gate = new object();

	private static readonly List<nint> MinimizedByUs = new List<nint>();

	private static readonly int SelfPid = Process.GetCurrentProcess().Id;

	public static bool OthersMinimized
	{
		get
		{
			lock (Gate)
			{
				return MinimizedByUs.Count > 0;
			}
		}
	}

	public static void ToggleMinimizeOthers(nint keepHwnd)
	{
		Thread thread = new Thread(() =>
		{
			Run(keepHwnd);
		});
		thread.IsBackground = true;
		thread.Name = "HoldDrag.ShakeAction";
		thread.Start();
	}

	private static void Run(nint keepHwnd)
	{
		lock (Gate)
		{
			if (MinimizedByUs.Count > 0)
			{
				int num = 0;
				for (int i = 0; i < MinimizedByUs.Count; i++)
				{
					nint num2 = MinimizedByUs[i];
					if (num2 != IntPtr.Zero && Native.IsWindow(num2))
					{
						Native.ShowWindowAsync(num2, 9);
						num++;
					}
				}
				MinimizedByUs.Clear();
				Log.Info("晃窗：还原 " + num + " 扇窗口");
				return;
			}
		}
		List<nint> found = new List<nint>();
		try
		{
			Native.EnumWindows((nint h, nint l) =>
			{
				try
				{
					if (h == keepHwnd)
					{
						return true;
					}
					if (!Native.IsWindow(h))
					{
						return true;
					}
					if (!Native.IsWindowVisible(h))
					{
						return true;
					}
					long windowLong = Native.GetWindowLong(h, -16);
					if ((windowLong & 0x20000000) != 0L)
					{
						return true;
					}
					if ((windowLong & 0x10000000) == 0L)
					{
						return true;
					}
					Native.GetWindowThreadProcessId(h, out var lpdwProcessId);
					if (lpdwProcessId == (uint)SelfPid)
					{
						return true;
					}
					if (!Native.GetWindowRect(h, out var lpRect))
					{
						return true;
					}
					if (lpRect.Width <= 0 || lpRect.Height <= 0)
					{
						return true;
					}
					found.Add(h);
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
				}
				return true;
			}, IntPtr.Zero);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		int num3 = 0;
		for (int num4 = 0; num4 < found.Count; num4++)
		{
			Native.ShowWindowAsync(found[num4], 6);
			num3++;
		}
		lock (Gate)
		{
			MinimizedByUs.AddRange(found);
		}
		Log.Info("晃窗：最小化 " + num3 + " 扇窗口（保留当前窗口）");
	}
}
