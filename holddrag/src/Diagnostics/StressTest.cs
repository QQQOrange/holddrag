// HoldDrag · Diagnostics/StressTest.cs

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

internal static class StressTest
{
	private struct Snap
	{
		public uint Handles;

		public uint Gdi;

		public uint User;

		public long PrivateMb;

		public long WorkingMb;

		public string Text()
		{
			return "句柄=" + Handles + " GDI=" + Gdi + " USER=" + User + " 私有=" + PrivateMb + "MB 工作集=" + WorkingMb + "MB";
		}
	}

	private sealed class Fixture : IDisposable
	{
		private readonly string _dir;

		public readonly Form Window;

		public readonly ConfigStore Store;

		public readonly Coordinator Coord;

		public nint H => Window.Handle;

		public Fixture()
		{
			_dir = Path.Combine(Path.GetTempPath(), "holddrag-stress-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
			Store = new ConfigStore(_dir);
			Store.Load();
			AppConfig current = Store.Current;
			current.ShowActivationFrame = true;
			current.AllowFullscreenWindows = true;
			current.BringToFront = true;
			current.DragRefreshMode = "Custom";
			current.DragRefreshHz = 500;
			Window = new Form();
			Window.FormBorderStyle = FormBorderStyle.Sizable;
			Window.StartPosition = FormStartPosition.Manual;
			Window.ShowInTaskbar = false;
			Window.Text = "HoldDrag stress";
			Window.TopMost = true;
			Window.Bounds = new Rectangle(140, 140, 340, 240);
			Window.Show();
			Application.DoEvents();
			Coord = new Coordinator(Store);
			Coord.PrecreateOverlays();
			Application.DoEvents();
		}

		public void Dispose()
		{
			try
			{
				Coord.Dispose();
			}
			catch
			{
			}
			try
			{
				Window.Close();
				Window.Dispose();
			}
			catch
			{
			}
			try
			{
				Directory.Delete(_dir, recursive: true);
			}
			catch
			{
			}
		}
	}

	private const int WarmupDrags = 40;

	private const int TotalDrags = 320;

	private const int SampleEvery = 40;

	private static readonly StringBuilder Buf = new StringBuilder();

	private static int _fail;

	private static string _reportDir;

	private static Process _proc;

	public static void Run(string dir)
	{
		Line("==== 长按拖窗 泄漏压力测试 ====");
		Line("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		Line("拖动会话：预热 " + 40 + " 次，正式 " + 320 + " 次，每 " + 40 + " 次采样一次");
		Line("");
		StartWatchdog(dir);
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		try
		{
			Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
		}
		catch
		{
		}
		if (SystemInformation.VirtualScreen.Width < 400)
		{
			Line("当前显示区域过小，跳过。");
			Finish(dir);
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		WindowActions.ForcedTargetForTest = IntPtr.Zero;
		try
		{
			using Fixture fixture = new Fixture();
			WindowActions.ForcedTargetForTest = fixture.H;
			DoDrag(fixture, 0, resize: false);
			Line("链路自检：一次完整拖动会话跑通");
			for (int i = 0; i < 40; i++)
			{
				DoDrag(fixture, i, resize: false);
				DoDrag(fixture, i, resize: true);
			}
			Collect();
			Snap snap = Sample();
			Line("预热完成，基准：" + snap.Text());
			Line("");
			long num = 0L;
			long num2 = 0L;
			long num3 = 0L;
			long val = 0L;
			for (int j = 1; j * 40 <= 320; j++)
			{
				for (int k = 0; k < 40; k++)
				{
					int n = j * 40 + k;
					DoDrag(fixture, n, resize: false);
					DoDrag(fixture, n, resize: true);
				}
				Collect();
				Snap snap2 = Sample();
				Line("第 " + j * 40 * 2 + " 次拖动后：" + snap2.Text());
				num = Math.Max(num, (long)snap2.Handles - (long)snap.Handles);
				num2 = Math.Max(num2, (long)snap2.Gdi - (long)snap.Gdi);
				num3 = Math.Max(num3, (long)snap2.User - (long)snap.User);
				val = Math.Max(val, snap2.PrivateMb - snap.PrivateMb);
			}
			long num4 = long.MaxValue;
			Snap snap3 = default;
			for (int l = 0; l < 8; l++)
			{
				CollectFull();
				Snap snap4 = Sample();
				long num5 = snap4.PrivateMb - snap.PrivateMb;
				if (num5 < num4)
				{
					num4 = num5;
					snap3 = snap4;
				}
			}
			Snap snap5 = snap3;
			Line("");
			Line("收干净后（含 LOH 压缩，取 8 轮最低值）：" + snap5.Text() + "，相对基准 " + ((num4 >= 0) ? "+" : "") + num4 + " MB");
			Line("");
			Check(num2 <= 80, "GDI 对象没有随拖动次数增长", "峰值增量 " + num2);
			Check(num3 <= 60, "USER 对象没有随拖动次数增长", "峰值增量 " + num3);
			Check(num <= 120, "系统句柄没有随拖动次数增长", "峰值增量 " + num);
			Check(num4 <= 25, "私有内存没有随拖动次数增长（看收干净后的底）", "底增量 " + num4 + " MB（过程中峰值 +" + val + " MB，属托管堆正常浮动）");
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			_fail++;
			Line("[FAIL] 压力测试自身抛异常 :: " + ex.Message);
		}
		finally
		{
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
		Line("");
		Line((_fail == 0) ? "结论：未发现泄漏迹象" : "结论：存在可疑增长，见上面 [FAIL] 行");
		Finish(dir);
	}

	private static void StartWatchdog(string dir)
	{
		_reportDir = dir;
		try
		{
			Thread thread = new Thread(() =>
			{
				Thread.Sleep(240000);
				try
				{
					Buf.Append("\r\n\r\n!! 压测总超时（240 秒），报告到此为止。\r\n");
					File.WriteAllText(Path.Combine(_reportDir, "stresstest.txt"), Buf.ToString(), Encoding.UTF8);
				}
				catch
				{
				}
				Environment.Exit(2);
			});
			thread.IsBackground = true;
			thread.Name = "HoldDrag.StressWatchdog";
			thread.Start();
		}
		catch
		{
		}
	}

	private static bool DoDrag(Fixture f, int n, bool resize)
	{
		Native.GetWindowRect(f.H, out var lpRect);
		Native.POINT p = (resize ? new Native.POINT(lpRect.Right - 10, lpRect.Bottom - 10) : new Native.POINT(lpRect.Left + lpRect.Width / 2, lpRect.Top + lpRect.Height / 2));
		f.Coord.Trigger.SimulateKeyDown();
		f.Coord.Trigger.ForceHoldElapsed();
		Native.MSLLHOOKSTRUCT ms = Ms(p);
		f.Coord.OnMouse(resize ? 516 : 513, ref ms);
		for (int i = 1; i <= 8; i++)
		{
			int num = ((n % 2 == 0) ? i : (-i)) * 5;
			int num2 = (((n + i) % 2 == 0) ? i : (-i)) * 4;
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + num, p.Y + num2));
			f.Coord.OnMouse(512, ref ms2);
			Application.DoEvents();
		}
		Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(p.X + 6, p.Y + 6));
		f.Coord.OnMouse(resize ? 517 : 514, ref ms3);
		Application.DoEvents();
		f.Coord.Trigger.SimulateKeyUp(null);
		Application.DoEvents();
		return true;
	}

	private static Native.MSLLHOOKSTRUCT Ms(Native.POINT p)
	{
		return new Native.MSLLHOOKSTRUCT
		{
			pt = p
		};
	}

	private static void Collect()
	{
		try
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}
		catch
		{
		}
	}

	private static void CollectFull()
	{
		try
		{
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
			GC.WaitForPendingFinalizers();
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
			Thread.Sleep(600);
		}
		catch
		{
		}
	}

	private static Snap Sample()
	{
		Snap result = default;
		try
		{
			nint currentProcess = Native.GetCurrentProcess();
			result.Gdi = Native.GetGuiResources(currentProcess, 0u);
			result.User = Native.GetGuiResources(currentProcess, 1u);
			result.Handles = (Native.GetProcessHandleCount(currentProcess, out var pdwHandleCount) ? pdwHandleCount : 0u);
			if (_proc == null)
			{
				_proc = Process.GetCurrentProcess();
			}
			_proc.Refresh();
			result.PrivateMb = _proc.PrivateMemorySize64 / 1048576;
			result.WorkingMb = _proc.WorkingSet64 / 1048576;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return result;
	}

	private static void Check(bool ok, string name, string detail = null)
	{
		if (ok)
		{
			Line("  [PASS] " + name + ((detail != null) ? ("（" + detail + "）") : ""));
			return;
		}
		_fail++;
		Line("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : (" :: " + detail)));
	}

	private static void Line(string s)
	{
		Buf.Append(s);
		Buf.Append("\r\n");
	}

	private static void Finish(string dir)
	{
		Log.Shutdown();
		string text = Buf.ToString();
		try
		{
			File.WriteAllText(Path.Combine(dir, "stresstest.txt"), text, Encoding.UTF8);
		}
		catch
		{
		}
		try
		{
			Thread thread = new Thread(() =>
			{
				try
				{
					Console.Out.Write(text);
					Console.Out.Flush();
				}
				catch
				{
				}
			});
			thread.IsBackground = true;
			thread.Name = "HoldDrag.StressEcho";
			thread.Start();
			thread.Join(1500);
		}
		catch
		{
		}
		HardExit((_fail != 0) ? 1 : 0);
	}

	internal static void HardExit(int code)
	{
		Environment.ExitCode = code;
		try
		{
			Console.Out.Flush();
		}
		catch
		{
		}
		try
		{
			Log.Info("[诊断] 结束，退出码 " + code);
		}
		catch
		{
		}
		try
		{
			Native.TerminateProcess(Native.GetCurrentProcess(), (uint)code);
		}
		catch
		{
		}
		Environment.Exit(code);
	}
}
