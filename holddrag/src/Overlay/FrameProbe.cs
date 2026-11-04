// HoldDrag · Overlay/FrameProbe.cs

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

internal static class FrameProbe
{
	private const uint WS_OVERLAPPEDWINDOW = 13565952u;

	private const uint WS_VISIBLE = 268435456u;

	private static nint _hwnd;

	private static int _slowMs = 20;

	private static volatile bool _armed;

	private static Native.WndProc _proc;

	public static void Run()
	{
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		foreach (string text in commandLineArgs)
		{
			if (text.StartsWith("--slow="))
			{
				int.TryParse(text.Substring(7), out _slowMs);
			}
		}
		if (_slowMs < 0)
		{
			_slowMs = 0;
		}
		if (_slowMs > 200)
		{
			_slowMs = 200;
		}
		try
		{
			Native.TimeBeginPeriod(1u);
		}
		catch
		{
		}
		List<string> lines = new List<string>();
		lines.Add("═══ HoldDrag 帧写入物理探针 ═══");
		lines.Add("目标窗口每帧处理耗时（WM_WINDOWPOSCHANGING 内 sleep）：" + _slowMs + " ms");
		_proc = OnWndProc;
		Native.WNDCLASSEX lpwcx = new Native.WNDCLASSEX
		{
			cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX)),
			lpfnWndProc = _proc,
			hInstance = Native.GetModuleHandleW(null),
			lpszClassName = "HoldDrag.Probe.Target",
			hbrBackground = new IntPtr(2)
		};
		if (Native.RegisterClassExW(ref lpwcx) == 0)
		{
			lines.Add("RegisterClassExW 失败，Win32 错误码 " + Marshal.GetLastWin32Error());
			Finish(lines);
			return;
		}
		_hwnd = Native.CreateWindowExW(0u, "HoldDrag.Probe.Target", "HoldDrag 探针目标（慢速窗口）", 282001408u, 420, 300, 420, 320, IntPtr.Zero, IntPtr.Zero, lpwcx.hInstance, IntPtr.Zero);
		if (_hwnd == IntPtr.Zero)
		{
			lines.Add("CreateWindowExW 失败，Win32 错误码 " + Marshal.GetLastWin32Error());
			Finish(lines);
			return;
		}
		ManualResetEventSlim done = new ManualResetEventSlim(initialState: false);
		Thread thread = new Thread(() =>
		{
			try
			{
				RunTests(lines);
			}
			catch (Exception ex)
			{
				lines.Add("探针异常: " + ex);
			}
			finally
			{
				done.Set();
			}
		});
		thread.IsBackground = true;
		thread.Name = "HoldDrag.FrameProbe.Writer";
		thread.Start();
		while (!done.IsSet)
		{
			Native.MSG lpMsg;
			while (Native.PeekMessageW(out lpMsg, IntPtr.Zero, 0u, 0u, 1u))
			{
				if (lpMsg.message == 18)
				{
					done.Set();
					break;
				}
				Native.TranslateMessage(ref lpMsg);
				Native.DispatchMessageW(ref lpMsg);
			}
			Thread.Sleep(1);
		}
		try
		{
			Native.DestroyWindow(_hwnd);
		}
		catch
		{
		}
		Finish(lines);
	}

	private static void Finish(List<string> lines)
	{
		string contents = string.Join(Environment.NewLine, lines.ToArray());
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
		try
		{
			File.WriteAllText(Path.Combine(path, "probe.txt"), contents);
		}
		catch
		{
		}
		foreach (string line in lines)
		{
			Console.WriteLine(line);
		}
		try
		{
			Console.Out.Flush();
		}
		catch
		{
		}
		try
		{
			Native.TimeEndPeriod(1u);
		}
		catch
		{
		}
		Environment.Exit(0);
	}

	private static nint OnWndProc(nint h, uint msg, nint w, nint l)
	{
		if (_armed && msg == 70 && _slowMs > 0)
		{
			Thread.Sleep(_slowMs);
		}
		return Native.DefWindowProcW(h, msg, w, l);
	}

	private static void RunTests(List<string> lines)
	{
		Thread.Sleep(120);
		_armed = true;
		Native.GetWindowRect(_hwnd, out var lpRect);
		lines.Add("目标窗口初始矩形: " + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height);
		lines.Add("");
		MoveSync(600, 420);
		Thread.Sleep(80);
		int num = 760;
		int num2 = 500;
		Stopwatch stopwatch = Stopwatch.StartNew();
		Native.SetWindowPos(_hwnd, IntPtr.Zero, num, num2, 0, 0, 16917u);
		long num3 = stopwatch.ElapsedTicks * 1000 / Stopwatch.Frequency;
		long num4 = -1L;
		List<KeyValuePair<long, int>> list = new List<KeyValuePair<long, int>>();
		int num5 = int.MinValue;
		while (true)
		{
			long num6 = stopwatch.ElapsedTicks * 1000000 / Stopwatch.Frequency;
			if (num6 > 600000)
			{
				break;
			}
			if (Native.GetWindowRect(_hwnd, out lpRect))
			{
				if (lpRect.Left == num && lpRect.Top == num2)
				{
					num4 = num6;
					break;
				}
				if (lpRect.Left != num5)
				{
					num5 = lpRect.Left;
					list.Add(new KeyValuePair<long, int>(num6 / 1000, lpRect.Left));
				}
			}
			Thread.SpinWait(120);
		}
		lines.Add("T1 异步投递 → GetWindowRect 反映");
		lines.Add("   SetWindowPos(ASYNC) 自身耗时 = " + num3 + " ms（应为 0：立即返回）");
		if (num4 < 0)
		{
			lines.Add("   600ms 内位置从未变成目标 ←★ 回执机制在本类目标上完全失效");
		}
		else
		{
			lines.Add("   位置第一次等于目标 = 投递后 " + ((double)num4 / 1000.0).ToString("F1") + " ms（目标每帧 " + _slowMs + " ms）");
		}
		lines.Add("   轮询中观察到的中间 x 序列: " + string.Join(" → ", list.ConvertAll((KeyValuePair<long, int> p) => p.Value + "@" + p.Key + "ms").ToArray()));
		lines.Add("   ⇒ 若反映耗时≈0：回执永远说\"已消化\"，每事件必投 = 裸异步（队列堆积）");
		lines.Add("   ⇒ 若反映耗时≈" + _slowMs + "ms：回执有效，但慢于事件流时会饿死帧");
		Thread.Sleep(150);
		MoveSync(900, 540);
		Thread.Sleep(80);
		int num7 = 1040;
		int num8 = 600;
		Stopwatch stopwatch2 = Stopwatch.StartNew();
		Native.SetWindowPos(_hwnd, IntPtr.Zero, num7, num8, 0, 0, 533u);
		long num9 = stopwatch2.ElapsedTicks * 1000 / Stopwatch.Frequency;
		Native.GetWindowRect(_hwnd, out lpRect);
		lines.Add("");
		lines.Add("T2 同步写（工作线程阻塞时长）");
		lines.Add("   SetWindowPos(同步) 耗时 = " + num9 + " ms（目标每帧 " + _slowMs + " ms）");
		lines.Add("   返回后 GetWindowRect = " + lpRect.Left + "," + lpRect.Top + ((lpRect.Left == num7 && lpRect.Top == num8) ? "  ✓ 已精确落位" : "  ✗ 未落位"));
		lines.Add("   ⇒ 阻塞时长 = 目标的真实处理耗时；这就是背压的天然节奏");
		Thread.Sleep(150);
		MoveSync(1200, 660);
		Thread.Sleep(80);
		int num10 = 1200;
		Stopwatch stopwatch3 = Stopwatch.StartNew();
		for (int num11 = 1; num11 <= 10; num11++)
		{
			Native.SetWindowPos(_hwnd, IntPtr.Zero, num10 + num11 * 10, 660, 0, 0, 16917u);
			Thread.Sleep(1);
		}
		long num12 = stopwatch3.ElapsedTicks * 1000 / Stopwatch.Frequency;
		List<KeyValuePair<long, int>> list2 = new List<KeyValuePair<long, int>>();
		int num13 = int.MinValue;
		long num14 = -1L;
		int num15 = num10 + 100;
		while (true)
		{
			long num16 = stopwatch3.ElapsedTicks * 1000 / Stopwatch.Frequency;
			if (num16 > 3000)
			{
				break;
			}
			if (Native.GetWindowRect(_hwnd, out lpRect))
			{
				if (lpRect.Left != num13)
				{
					num13 = lpRect.Left;
					list2.Add(new KeyValuePair<long, int>(num16, lpRect.Left));
				}
				if (num14 < 0 && lpRect.Left == num15)
				{
					num14 = num16;
				}
			}
			Thread.Sleep(1);
		}
		lines.Add("");
		lines.Add("T3 连投 10 个异步帧（1ms 一个，最后位置 x=" + num15 + "）");
		lines.Add("   10 次投递总耗时 = " + num12 + " ms");
		lines.Add("   投递结束时窗口在哪: x=" + list2.Count + " 个变化点中的第 0 个 = " + ((list2.Count > 0) ? list2[0].Value.ToString() : "?"));
		lines.Add("   全部落定耗时 = " + ((num14 >= 0) ? (num14 + " ms") : "3s 内未落定") + "（目标每帧 " + _slowMs + " ms × 10 帧 ≈ " + _slowMs * 10 + " ms）");
		lines.Add("   窗口轨迹（变化点）: " + string.Join(" → ", list2.ConvertAll((KeyValuePair<long, int> p) => p.Value + "@" + p.Key + "ms").ToArray()));
		lines.Add("   ⇒ 窗口是否【滑过】了 10 个中间位置？是 = 追过期位置（队列堆积铁证）");
		Thread.Sleep(150);
		MoveSync(1200, 700);
		Thread.Sleep(80);
		Stopwatch stopwatch4 = Stopwatch.StartNew();
		for (int num17 = 1; num17 <= 10; num17++)
		{
			Native.SetWindowPos(_hwnd, IntPtr.Zero, 1200 + num17 * 10, 700, 0, 0, 533u);
		}
		long num18 = stopwatch4.ElapsedTicks * 1000 / Stopwatch.Frequency;
		Native.GetWindowRect(_hwnd, out lpRect);
		lines.Add("");
		lines.Add("T4 连做 10 个同步写（对照 T3）");
		lines.Add("   总耗时 = " + num18 + " ms（≈ 目标每帧 " + _slowMs + " × 10）");
		lines.Add("   结束后位置 = " + lpRect.Left + "," + lpRect.Top + ((lpRect.Left == 1300 && lpRect.Top == 700) ? "  ✓ 精确等于最后一次写入" : "  ✗ 不符"));
		lines.Add("   ⇒ 每次写完窗口都精确在写入位，队列里永远只有一帧，不存在追过期位置");
		Thread.Sleep(100);
	}

	private static void MoveSync(int x, int y)
	{
		Native.SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, 533u);
	}
}
