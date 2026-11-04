// HoldDrag · Platform/Log.cs

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

internal static class Log
{
	private static readonly object Gate = new object();

	private static string _path = string.Empty;

	private static StreamWriter _writer;

	public static string Path => _path;

	public static bool Verbose { get; set; }

	public static void Init(string dir)
	{
		try
		{
			_path = System.IO.Path.Combine(dir, "holddrag.log");
		}
		catch
		{
			_path = string.Empty;
			return;
		}
		// 日志文件被占用（例如同时开了两个实例）时改用带进程号的文件名，不要因此整个会话不记日志。
		try
		{
			if (!CanOpenForAppend(_path))
			{
				_path = System.IO.Path.Combine(dir, "holddrag." + Process.GetCurrentProcess().Id + ".log");
			}
		}
		catch
		{
		}
		// 清理失败只影响"清不掉旧日志"，绝不能让下面的 _path 变成空串——
		// 旧实现把 Trim/Cleanup 和 _path 初始化放在同一个 try 里，一次清理异常就把本进程的日志全废了。
		Trim();
		CleanupOldLogs(dir);
	}

	private static bool CanOpenForAppend(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return true;
			}
			using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
			{
				return true;
			}
		}
		catch
		{
			return false;
		}
	}

	public static void Info(string msg)
	{
		Write("INFO", msg);
	}

	public static void Trace(string msg)
	{
		if (Verbose)
		{
			Write("TRC ", msg);
		}
	}

	public static void Error(string msg)
	{
		Write("ERR ", msg);
	}

	public static void Error(Exception ex)
	{
		Write("ERR ", (ex != null) ? ex.ToString() : "null exception");
	}

	private static void Write(string level, string msg)
	{
		if (string.IsNullOrEmpty(_path))
		{
			return;
		}
		// v1.3：所有线程一律走异步队列。
		// 旧版只有钩子线程异步，UI/拖动线程仍是"写一行 + Flush 一次"——
		// Flush 是强制落盘的系统调用（几毫秒级），设置页一旦连续记日志，
		// UI 线程就被磁盘卡住（表现为设置窗口操作有顿挫感）。
		// 队列有 4096 行上限 + 退出前 FlushAsyncQueue 兜底，不会丢关键日志。
		EnqueueAsync(Stamp(level, msg));
	}

	/// <summary>
	/// 当前线程是否为钩子回调线程。钩子线程由 HookEngine 起名 "HoldDrag.HookThread"，
	/// 用它来区分"绝对不能阻塞"的写入路径。
	/// </summary>
	internal static bool IsHookThread => Thread.CurrentThread.Name == "HoldDrag.HookThread";

	private static readonly ConcurrentQueue<string> AsyncQueue = new ConcurrentQueue<string>();

	/// <summary>后台落盘线程是否在跑。线程退出时置回 false，保证"线程意外死掉后还能被重新拉起"。</summary>
	private static volatile bool _drainRunning;

	/// <summary>自检用：已入队的行数（证明钩子线程确实走了异步路径而不是同步落盘）。</summary>
	internal static long AsyncEnqueuedForTest;

	/// <summary>自检用：已由后台线程写入文件的行数（证明确实落盘了，没有被丢掉）。</summary>
	internal static long AsyncWrittenForTest;

	/// <summary>自检用：当前队列积压行数。</summary>
	internal static int AsyncPendingForTest => AsyncQueue.Count;

	/// <summary>自检用：诊断信息（路径是否就绪、writer 是否打开、落盘线程是否在跑）。</summary>
	internal static string AsyncDiagnosticsForTest => $"path={(string.IsNullOrEmpty(_path) ? "(空)" : "ok")} writer={(_writer != null)} drain={_drainRunning} enq={Interlocked.Read(ref AsyncEnqueuedForTest)} wr={Interlocked.Read(ref AsyncWrittenForTest)} batch={Interlocked.Read(ref AsyncBatchCallsForTest)} pending={AsyncQueue.Count} lastErr={_lastAsyncError}";

	/// <summary>自检用：WriteBatch 被调用的次数。</summary>
	internal static long AsyncBatchCallsForTest;

	/// <summary>自检用：后台写入最后一次失败原因。</summary>
	internal static volatile string _lastAsyncError;

	/// <summary>自检用：后台写入是否因"宿主环境不允许写日志目录"而失败（沙箱 / 安全策略）。</summary>
	internal static volatile bool AsyncWriteDeniedForTest;

	/// <summary>把一行日志塞进队列；队列积压超过上限时丢弃最旧的，避免内存无界增长。</summary>
	private static void EnqueueAsync(string line)
	{
		try
		{
			// 上限 4096 行：诊断日志本就只服务排查，宁可丢少量也不能让钩子线程等。
			// 行结尾的 \r\n 在入队时补上（Stamp 只产出本体，避免再拼一次串）。
			if (AsyncQueue.Count > 4096)
			{
				AsyncQueue.TryDequeue(out var _);
			}
			AsyncQueue.Enqueue(line + "\r\n");
			Interlocked.Increment(ref AsyncEnqueuedForTest);
			StartDrainIfNeeded();
		}
		catch
		{
		}
	}

	/// <summary>
	/// 确保后台落盘线程在跑。用 _drainRunning 标志而不是 CompareExchange 一次性开关：
	/// 线程若因异常退出，下一次写日志还能把它重新拉起来（否则异步日志会永久卡死）。
	/// </summary>
	private static void StartDrainIfNeeded()
	{
		if (_drainRunning)
		{
			return;
		}
		lock (AsyncQueue)
		{
			if (_drainRunning)
			{
				return;
			}
			_drainRunning = true;
			Thread thread = new Thread(DrainAsync)
			{
				IsBackground = true,
				Name = "HoldDrag.LogWriter"
			};
			thread.Start();
		}
	}

	/// <summary>后台落盘线程：批量取出、合并写入，只 Flush 一次。</summary>
	private static void DrainAsync()
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder(8192);
			while (true)
			{
				try
				{
					if (!AsyncQueue.TryDequeue(out var result))
					{
						Thread.Sleep(40);
						continue;
					}
					stringBuilder.Clear();
					stringBuilder.Append(result);
					while (stringBuilder.Length < 8192 && AsyncQueue.TryDequeue(out var result2))
					{
						stringBuilder.Append(result2);
					}
					WriteBatch(stringBuilder.ToString());
				}
				catch (Exception ex)
				{
					Log.Error(ex);
					Thread.Sleep(200);
				}
			}
		}
		finally
		{
			// 线程无论因何结束都要放掉标志，让后续日志能重新拉起它
			_drainRunning = false;
		}
	}

	/// <summary>把一批日志写进文件并计入"已落盘"计数。异常一律吞掉（日志失败不能影响主功能）。</summary>
	private static void WriteBatch(string batch)
	{
		if (string.IsNullOrEmpty(batch))
		{
			return;
		}
		Interlocked.Increment(ref AsyncBatchCallsForTest);
		lock (Gate)
		{
			try
			{
				if (string.IsNullOrEmpty(_path))
				{
					return;
				}
				EnsureWriter();
				if (_writer == null)
				{
					File.AppendAllText(_path, batch, Encoding.UTF8);
				}
				else
				{
					_writer.Write(batch);
					_writer.Flush();
				}
				Interlocked.Increment(ref AsyncWrittenForTest);
			}
			catch (Exception ex)
			{
				_lastAsyncError = ex.GetType().Name + ": " + ex.Message;
				if (ex is UnauthorizedAccessException || ex is IOException)
				{
					AsyncWriteDeniedForTest = true;
				}
				CloseWriter();
			}
		}
	}

	/// <summary>退出前把异步队列里剩下的日志写完（最多等 1 秒）。</summary>
	public static void FlushAsyncQueue(int timeoutMs = 1000)
	{
		try
		{
			long num = Environment.TickCount64;
			while (!AsyncQueue.IsEmpty && Environment.TickCount64 - num < timeoutMs)
			{
				Thread.Sleep(20);
			}
		}
		catch
		{
		}
	}

	private static string Stamp(string level, string msg)
	{
		// v1.3：热路径零分配版。旧版 DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
		// 每行都分配一个 DateTime + 一个 string + 格式化器；诊断日志高频时
		//（拖动期间每次鼠标键事件都记一行）这就是稳定的 GC 压力源。
		// 现在只分配最终那一个 string。
		DateTime now = DateTime.Now;
		return string.Create(25 + level.Length + msg.Length, (now, level, msg), static (span, state) =>
		{
			DateTime dateTime = state.now;
			int num = 0;
			span[num++] = (char)(48 + dateTime.Year / 1000);
			span[num++] = (char)(48 + dateTime.Year / 100 % 10);
			span[num++] = (char)(48 + dateTime.Year / 10 % 10);
			span[num++] = (char)(48 + dateTime.Year % 10);
			span[num++] = '-';
			span[num++] = (char)(48 + dateTime.Month / 10);
			span[num++] = (char)(48 + dateTime.Month % 10);
			span[num++] = '-';
			span[num++] = (char)(48 + dateTime.Day / 10);
			span[num++] = (char)(48 + dateTime.Day % 10);
			span[num++] = ' ';
			span[num++] = (char)(48 + dateTime.Hour / 10);
			span[num++] = (char)(48 + dateTime.Hour % 10);
			span[num++] = ':';
			span[num++] = (char)(48 + dateTime.Minute / 10);
			span[num++] = (char)(48 + dateTime.Minute % 10);
			span[num++] = ':';
			span[num++] = (char)(48 + dateTime.Second / 10);
			span[num++] = (char)(48 + dateTime.Second % 10);
			span[num++] = '.';
			span[num++] = (char)(48 + dateTime.Millisecond / 100);
			span[num++] = (char)(48 + dateTime.Millisecond / 10 % 10);
			span[num++] = (char)(48 + dateTime.Millisecond % 10);
			span[num++] = ' ';
			state.level.AsSpan().CopyTo(span.Slice(num));
			num += state.level.Length;
			span[num++] = ' ';
			state.msg.AsSpan().CopyTo(span.Slice(num));
		});
	}

	private static void EnsureWriter()
	{
		if (_writer == null)
		{
			_writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
			_writer.AutoFlush = false;
		}
	}

	private static void CloseWriter()
	{
		try
		{
			if (_writer != null)
			{
				_writer.Dispose();
			}
		}
		catch
		{
		}
		_writer = null;
	}

	private static void CleanupOldLogs(string dir)
	{
		try
		{
			string[] files = Directory.GetFiles(dir, "holddrag*.log");
			if (files.Length <= 10)
			{
				return;
			}
			Array.Sort(files, (string a, string b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
			for (int num = 10; num < files.Length; num++)
			{
				if (!string.Equals(files[num], _path, StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						File.Delete(files[num]);
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}

	private static void Trim()
	{
		try
		{
			FileInfo fileInfo = new FileInfo(_path);
			if (fileInfo.Exists && fileInfo.Length > 524288)
			{
				CloseWriter();
				File.Delete(_path);
			}
		}
		catch
		{
		}
	}

	internal static void FlushAndClose()
	{
		lock (Gate)
		{
			CloseWriter();
		}
	}

	public static void Shutdown()
	{
		lock (Gate)
		{
			CloseWriter();
		}
	}
}
