// HoldDrag · Diagnostics/DragBench.cs

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

internal static class DragBench
{
	private class Sample
	{
		public long T;

		public int X;

		public int Y;
	}

	private sealed class SlowTargetForm : Form
	{
		public int SlowMs;

		public int HangMs;

		public int HangLeft;

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == 70)
			{
				if (HangLeft > 0)
				{
					HangLeft--;
					if (HangMs > 0)
					{
						Thread.Sleep(HangMs);
					}
				}
				else if (SlowMs > 0)
				{
					Thread.Sleep(SlowMs);
				}
			}
			base.WndProc(ref m);
		}
	}

	public static void Run()
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-bench-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		List<string> list = new List<string>();
		try
		{
			string[] commandLineArgs = Environment.GetCommandLineArgs();
			for (int i = 0; i < commandLineArgs.Length; i++)
			{
				if (commandLineArgs[i] == "--verbose")
				{
					Log.Verbose = true;
				}
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(defaultValue: false);
			try
			{
				Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
			}
			catch
			{
			}
			WindowActions.AllowOwnProcessAsTargetForTest = true;
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			WindowActions.ResetFrameStats();
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 100;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = true;
			current.BringToFront = false;
			current.MoveRate = Math.Max(1, current.MoveRate);
			current.ResizeRate = Math.Max(1, current.ResizeRate);
			using SlowTargetForm slowTargetForm = new SlowTargetForm();
			slowTargetForm.FormBorderStyle = FormBorderStyle.Sizable;
			slowTargetForm.StartPosition = FormStartPosition.Manual;
			slowTargetForm.ShowInTaskbar = false;
			slowTargetForm.TopMost = true;
			slowTargetForm.Bounds = new Rectangle(200, 200, 500, 380);
			slowTargetForm.Show();
			Application.DoEvents();
			Coordinator coord = new Coordinator(configStore);
			coord.PrecreateOverlays();
			Application.DoEvents();
			nint h = slowTargetForm.Handle;
			List<Sample> samples = new List<Sample>();
			List<(long, int, int)> ringSamples = new List<(long, int, int)>();
			nint ringH = coord.Drag.Frame.Handle;
			ManualResetEventSlim sampleStop = new ManualResetEventSlim(initialState: false);
			Stopwatch clock = Stopwatch.StartNew();
			long dragStartMs = -1L;
			List<long> evTimes = new List<long>();
			Thread thread = new Thread(() =>
			{
				Native.RECT rECT = default;
				bool flag2 = false;
				while (!sampleStop.IsSet)
				{
					if (Native.GetWindowRect(h, out var lpRect4) && (!flag2 || lpRect4.Left != rECT.Left || lpRect4.Top != rECT.Top))
					{
						lock (samples)
						{
							samples.Add(new Sample
							{
								T = clock.ElapsedMilliseconds,
								X = lpRect4.Left,
								Y = lpRect4.Top
							});
						}
						rECT = lpRect4;
						flag2 = true;
					}
					if (Native.GetWindowRect(ringH, out var lpRect5))
					{
						lock (ringSamples)
						{
							ringSamples.Add((clock.ElapsedMilliseconds, lpRect5.Left, lpRect5.Top));
						}
					}
					Thread.Sleep(1);
				}
			});
			thread.IsBackground = true;
			thread.Start();
			Native.GetWindowRect(h, out var lpRect);
			Native.POINT grab = new Native.POINT(lpRect.Left + lpRect.Width / 2, lpRect.Top + lpRect.Height / 2);
			if (Native.WindowFromPoint(grab) != h)
			{
				try
				{
					slowTargetForm.Activate();
					Application.DoEvents();
				}
				catch
				{
				}
				if (Native.WindowFromPoint(grab) != h)
				{
					list.Add("[FAIL] 抓取点 (" + grab.X + "," + grab.Y + ") 最上层的不是靶窗（句柄 " + ((IntPtr)h).ToInt64().ToString("X") + "，实际 " + ((IntPtr)Native.WindowFromPoint(grab)).ToInt64().ToString("X") + "）—— 先关掉全屏 / 置顶的其它窗口再跑基准，否则测的是别人。");
					sampleStop.Set();
					thread.Join(500);
					string contents = string.Join(Environment.NewLine, list.ToArray());
					string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
					try
					{
						File.WriteAllText(Path.Combine(path, "dragbench.txt"), contents);
					}
					catch
					{
					}
					foreach (string item in list)
					{
						Console.WriteLine(item);
					}
					Console.Out.Flush();
					Environment.Exit(0);
				}
			}
			coord.Trigger.SimulateKeyDown();
			coord.Trigger.ForceHoldElapsed();
			long dragMs = 0L;
			int result = 125;
			int result2 = 2000;
			int result3 = 0;
			int result4 = 0;
			int result5 = 1;
			commandLineArgs = Environment.GetCommandLineArgs();
			foreach (string text2 in commandLineArgs)
			{
				if (text2.StartsWith("--hz="))
				{
					int.TryParse(text2.Substring(5), out result);
				}
				if (text2.StartsWith("--ms="))
				{
					int.TryParse(text2.Substring(5), out result2);
				}
				if (text2.StartsWith("--slow="))
				{
					int.TryParse(text2.Substring(7), out result3);
				}
				if (text2.StartsWith("--hang="))
				{
					int.TryParse(text2.Substring(7), out result4);
				}
				if (text2.StartsWith("--hangn="))
				{
					int.TryParse(text2.Substring(8), out result5);
				}
			}
			if (result < 30)
			{
				result = 30;
			}
			if (result3 < 0)
			{
				result3 = 0;
			}
			if (result3 > 200)
			{
				result3 = 200;
			}
			slowTargetForm.SlowMs = result3;
			slowTargetForm.HangMs = result4;
			slowTargetForm.HangLeft = ((result5 < 1) ? 1 : result5);
			int hangLeft = slowTargetForm.HangLeft;
			int steps = result * result2 / 1000;
			int intervalUs = 1000000 / result;
			Native.GetCursorPos(out var lpPoint);
			ManualResetEventSlim driverDone = new ManualResetEventSlim(initialState: false);
			Thread thread2 = new Thread(() =>
			{
				Native.MSLLHOOKSTRUCT ms = Ms(grab);
				Native.SetCursorPos(grab.X, grab.Y);
				coord.OnMouse(513, ref ms);
				dragStartMs = clock.ElapsedMilliseconds;
				int num31 = 600;
				try
				{
					Native.TimeBeginPeriod(1u);
				}
				catch
				{
				}
				Stopwatch stopwatch = Stopwatch.StartNew();
				for (int j = 1; j <= steps; j++)
				{
					long num32 = (long)j * (long)intervalUs;
					while (stopwatch.ElapsedTicks * 1000000 / Stopwatch.Frequency < num32)
					{
						Thread.SpinWait(60);
					}
					Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(grab.X + num31 * j / steps, grab.Y + num31 * j / 2 / steps));
					Native.SetCursorPos(ms2.pt.X, ms2.pt.Y);
					evTimes.Add(clock.ElapsedMilliseconds);
					coord.OnMouse(512, ref ms2);
				}
				Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(grab.X + num31, grab.Y + num31 / 2));
				Native.SetCursorPos(ms3.pt.X, ms3.pt.Y);
				evTimes.Add(clock.ElapsedMilliseconds);
				coord.OnMouse(514, ref ms3);
				dragMs = stopwatch.ElapsedMilliseconds;
				driverDone.Set();
			});
			thread2.IsBackground = true;
			thread2.Start();
			while (!driverDone.IsSet)
			{
				Application.DoEvents();
				Thread.Sleep(1);
			}
			Thread.Sleep(150);
			for (int num = 0; num < 30; num++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			coord.Trigger.SimulateKeyUp(null);
			sampleStop.Set();
			thread.Join(500);
			Native.SetCursorPos(lpPoint.X, lpPoint.Y);
			Native.GetWindowRect(h, out var lpRect2);
			int num2 = lpRect.Left + 600;
			int num3 = lpRect.Top + 300;
			bool flag = Math.Abs(lpRect2.Left - num2) <= 2 && Math.Abs(lpRect2.Top - num3) <= 2;
			InlineArray5<object> buffer = default;
			buffer[0] = lpRect2.Left;
			buffer[1] = lpRect2.Top;
			buffer[2] = num2;
			buffer[3] = num3;
			buffer[4] = (flag ? "[PASS] 松手精确落位，无弹回" : "[FAIL] 落点偏差！");
			list.Add(string.Format("最终落位：({0},{1}) 期望≈({2},{3}) {4}", (ReadOnlySpan<object?>)buffer));
			List<(long, int, int)> list2 = ringSamples;
			int num4 = 0;
			for (int num5 = 1; num5 < list2.Count; num5++)
			{
				if (list2[num5].Item2 != list2[num5 - 1].Item2 || list2[num5].Item3 != list2[num5 - 1].Item3)
				{
					num4++;
				}
			}
			list.Add("[diag] 环采样 " + list2.Count + " 次，位置变化 " + num4 + " 次");
			if (list2.Count > 0)
			{
				list.Add("[diag] 环首/末位置 (" + list2[0].Item2 + "," + list2[0].Item3 + ") → (" + list2[list2.Count - 1].Item2 + "," + list2[list2.Count - 1].Item3 + ")");
			}
			list.Add("[diag] frame shown=" + coord.Drag.Frame.IsShown);
			Native.GetWindowRect(coord.Drag.Frame.Handle, out var lpRect3);
			list.Add("[diag] frame rect=" + lpRect3.Left + "," + lpRect3.Top + "," + lpRect3.Right + "," + lpRect3.Bottom);
			List<Sample> list3;
			lock (samples)
			{
				list3 = new List<Sample>(samples);
			}
			list.Add("拖动时长 " + dragMs + " ms，驱动事件 " + steps + " 个（" + result + "Hz），会话数 " + coord.Drag.SessionCount);
			list.Add("[diag] 最慢帧 _onMove=" + DragWorker.MaxMoveMs + " ms | 最长空转=" + DragWorker.MaxIdleMs + " ms | 单帧最多合并=" + DragWorker.MaxDrained + ((result3 > 0) ? (" | 目标窗口每帧 sleep " + result3 + " ms") : "") + ((result4 > 0) ? (" | 前 " + hangLeft + " 帧 sleep " + result4 + " ms（模拟彻底卡死）") : ""));
			list.Add("[帧写入] 写入=" + WindowActions.FramesWritten + " 无需写=" + WindowActions.FramesNoop + " 放弃跟手=" + WindowActions.FramesAbandoned + " | 单帧同步写入 平均 " + ((WindowActions.FramesWritten > 0) ? (WindowActions.TotalWriteMs / (double)WindowActions.FramesWritten).ToString("F1") : "?") + " ms 最大 " + WindowActions.MaxWriteMs.ToString("F1") + " ms | 事件 " + steps + " 个 → 写入率 " + ((steps > 0) ? ((double)WindowActions.FramesWritten * 100.0 / (double)steps).ToString("F1") : "?") + "%");
			list.Add("窗口位置更新点 " + list3.Count + " 个 → 有效更新率 " + ((dragMs > 0) ? ((double)list3.Count * 1000.0 / (double)dragMs).ToString("F1") : "?") + " Hz");
			if (list3.Count >= 3)
			{
				List<long> list4 = new List<long>();
				for (int num6 = 1; num6 < list3.Count; num6++)
				{
					list4.Add(list3[num6].T - list3[num6 - 1].T);
				}
				list4.Sort();
				double num7 = 0.0;
				for (int num8 = 0; num8 < list4.Count; num8++)
				{
					num7 += (double)list4[num8];
				}
				num7 /= (double)list4.Count;
				long num9 = list4[Math.Min(list4.Count - 1, list4.Count * 95 / 100)];
				long num10 = list4[list4.Count - 1];
				list.Add($"更新间隔：平均 {num7:F1} ms | P95 {num9} ms | 最大 {num10} ms");
				list.Add("→ 最大间隔就是肉眼看到的『卡的那一下』；健康目标理想值 ≈ " + 1000 / result + "ms（事件流节拍）" + ((result3 > 0) ? ("；慢目标理想值 ≈ " + result3 + "ms（目标每帧真实耗时，同步写按此节奏背压，不应再出现 122ms 级饿死尖峰）") : ""));
				list.Add("最后一个更新点在拖动开始后 " + list3[list3.Count - 1].T + " ms（驱动共 " + dragMs + " ms）");
				if (dragStartMs >= 0 && dragMs > 0 && evTimes.Count > 0)
				{
					double num11 = 600.0 / (double)steps;
					double num12 = 1000.0 / (double)result;
					double num13 = 0.0;
					double num14 = 0.0;
					double num15 = 0.0;
					double num16 = 0.0;
					int num17 = 0;
					int num18 = 0;
					int num19 = 0;
					long num20 = (long)((double)dragMs * 0.2);
					int num21 = 0;
					List<Tuple<double, long, int, double, int>> list5 = new List<Tuple<double, long, int, double, int>>();
					foreach (Sample item2 in list3)
					{
						double num22 = item2.T - dragStartMs;
						if (!(num22 < 0.0))
						{
							if (num22 > (double)dragMs)
							{
								break;
							}
							for (; num21 < evTimes.Count && (double)(evTimes[num21] - dragStartMs) <= num22; num21++)
							{
							}
							double num23 = (double)lpRect.Left + num11 * (double)Math.Min(num21, steps);
							double num24 = (num23 - (double)item2.X) / num11 * num12;
							if (num24 > num16)
							{
								num16 = num24;
							}
							list5.Add(Tuple.Create(num24, item2.T, item2.X, num23, num21));
							num13 += num24;
							num17++;
							if (num22 <= (double)num20)
							{
								num14 += num24;
								num18++;
							}
							else if (num22 >= (double)(dragMs - num20))
							{
								num15 += num24;
								num19++;
							}
						}
					}
					list5.Sort((Tuple<double, long, int, double, int> a, Tuple<double, long, int, double, int> b) => b.Item1.CompareTo(a.Item1));
					for (int num25 = 0; num25 < Math.Min(3, list5.Count); num25++)
					{
						Tuple<double, long, int, double, int> tuple = list5[num25];
						if (tuple.Item1 > 50.0)
						{
							InlineArray5<object> buffer2 = default;
							buffer2[0] = tuple.Item1;
							buffer2[1] = tuple.Item2;
							buffer2[2] = tuple.Item3;
							buffer2[3] = tuple.Item4;
							buffer2[4] = tuple.Item5;
							list.Add(string.Format("  [离群] 滞后 {0:F0} ms @ 采样T={1}ms 实际x={2} 期望x={3:F0} 已投递事件={4}", (ReadOnlySpan<object?>)buffer2));
						}
					}
					InlineArray5<object> buffer3 = default;
					buffer3[0] = ((num17 > 0) ? (num13 / (double)num17) : 0.0);
					buffer3[1] = num16;
					buffer3[2] = ((num18 > 0) ? (num14 / (double)num18) : 0.0);
					buffer3[3] = ((num19 > 0) ? (num15 / (double)num19) : 0.0);
					buffer3[4] = num12;
					list.Add(string.Format("跟手性：平均落后 {0:F1} ms | 最大 {1:F0} ms | 前20%段 {2:F1} ms → 后20%段 {3:F1} ms（后段 ≫ 前段 = 越拖越落后；1 个事件 = {4:F1} ms，是固有下限）", (ReadOnlySpan<object?>)buffer3));
				}
				list.Add("轨迹抽样（每 100ms）：");
				int num26 = 0;
				long num27 = long.MinValue;
				for (long num28 = 0L; num28 <= dragMs; num28 += 100)
				{
					for (; num26 < list3.Count && list3[num26].T < num28; num26++)
					{
					}
					if (num26 < list3.Count && list3[num26].T != num27)
					{
						list.Add($"  t={list3[num26].T,5}ms  x={list3[num26].X,5} y={list3[num26].Y,5}");
						num27 = list3[num26].T;
					}
				}
				list.Add("环轨迹抽样（每 200ms）：");
				int num29 = 0;
				for (long num30 = 0L; num30 <= dragMs; num30 += 200)
				{
					for (; num29 < ringSamples.Count && ringSamples[num29].Item1 < num30; num29++)
					{
					}
					if (num29 < ringSamples.Count)
					{
						list.Add($"  t={ringSamples[num29].Item1,5}ms  ring=({ringSamples[num29].Item2,5},{ringSamples[num29].Item3,5})");
					}
				}
			}
			else
			{
				list.Add("!! 采样点过少，链路可能没跑通");
			}
			coord.Dispose();
		}
		catch (Exception ex)
		{
			list.Add("EXC " + ex);
		}
		finally
		{
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
		}
		string contents2 = string.Join(Environment.NewLine, list.ToArray());
		string path2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
		try
		{
			File.WriteAllText(Path.Combine(path2, "dragbench.txt"), contents2);
		}
		catch
		{
		}
		foreach (string item3 in list)
		{
			Console.WriteLine(item3);
		}
		try
		{
			Console.Out.Flush();
		}
		catch
		{
		}
		Environment.Exit(0);
	}

	private static Native.MSLLHOOKSTRUCT Ms(Native.POINT p)
	{
		return new Native.MSLLHOOKSTRUCT
		{
			pt = p
		};
	}
}
