// HoldDrag · Core/AppRules.cs
//
// 按程序规则：对某个 exe 单独指定行为，比"黑名单"细一档。
//
// 配置格式（分号分隔，大小写不敏感）：
//   wps.exe=Ignore; code.exe=MoveOnly; game.exe=NoFrame
//
// 规则类型：
//   Ignore      完全不接管（等同黑名单）
//   MoveOnly    只允许移动，右键缩放不接管
//   ResizeOnly  只允许缩放，左键移动不接管
//   NoFrame     接管但**不画外框**（比如全屏视频、绘图软件）

using System;
using System.Collections.Generic;

namespace HoldDrag;

internal static class AppRules
{
	internal const string Ignore = "Ignore";

	internal const string MoveOnly = "MoveOnly";

	internal const string ResizeOnly = "ResizeOnly";

	internal const string NoFrame = "NoFrame";

	internal static readonly string[] All = new string[4] { "Ignore", "MoveOnly", "ResizeOnly", "NoFrame" };

	private static readonly object Gate = new object();

	private static Dictionary<string, string> _map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<(nint Hwnd, long Tick), string> Cache = new Dictionary<(nint, long), string>();

	private static long _cacheTick;

	public static string DisplayName(string mode)
	{
		return mode switch
		{
			"Ignore" => Loc.T("完全不接管"), 
			"MoveOnly" => Loc.T("只允许移动"), 
			"ResizeOnly" => Loc.T("只允许缩放"), 
			"NoFrame" => Loc.T("接管但不画外框"), 
			_ => mode, 
		};
	}

	/// <summary>解析并保存规则表；非法条目直接丢弃（不让手写错误把功能整体搞坏）。</summary>
	public static void Apply(string spec)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrWhiteSpace(spec))
		{
			string[] array = spec.Split(new char[2] { ';', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string text in array)
			{
				string[] array2 = text.Split(new char[1] { '=' }, 2);
				if (array2.Length != 2)
				{
					continue;
				}
				string text2 = array2[0].Trim();
				string text3 = array2[1].Trim();
				if (text2.Length == 0 || !IsValid(text3))
				{
					continue;
				}
				dictionary[text2] = NormalizeMode(text3);
			}
		}
		lock (Gate)
		{
			_map = dictionary;
			Cache.Clear();
		}
	}

	public static bool IsValid(string mode)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (string.Equals(All[i], mode, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	public static string NormalizeMode(string mode)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (string.Equals(All[i], mode, StringComparison.OrdinalIgnoreCase))
			{
				return All[i];
			}
		}
		return Ignore;
	}

	public static int Count
	{
		get
		{
			lock (Gate)
			{
				return _map.Count;
			}
		}
	}

	/// <summary>把规范写回成一行（保存到配置用）。</summary>
	public static string Normalize(string spec)
	{
		Apply(spec);
		lock (Gate)
		{
			List<string> list = new List<string>();
			foreach (KeyValuePair<string, string> item in _map)
			{
				list.Add(item.Key + "=" + item.Value);
			}
			list.Sort(StringComparer.OrdinalIgnoreCase);
			return string.Join(";", list);
		}
	}

	public static string ModeForExe(string exe)
	{
		if (string.IsNullOrEmpty(exe))
		{
			return null;
		}
		lock (Gate)
		{
			if (_map.TryGetValue(exe, out var value))
			{
				return value;
			}
		}
		return null;
	}

	/// <summary>窗口对应的规则（带 2 秒缓存：FindTarget 在鼠标移动时会被高频调用）。</summary>
	public static string ModeForWindow(nint hwnd)
	{
		if (hwnd == IntPtr.Zero)
		{
			return null;
		}
		long tickCount = Environment.TickCount64;
		lock (Gate)
		{
			if (Cache.TryGetValue((hwnd, tickCount / 2000), out var value))
			{
				return value;
			}
		}
		string text = null;
		try
		{
			string text2 = WindowActions.ProcessExeName(hwnd);
			text = ModeForExe(text2);
		}
		catch
		{
		}
		lock (Gate)
		{
			if (_cacheTick != tickCount / 2000)
			{
				_cacheTick = tickCount / 2000;
				Cache.Clear();
			}
			Cache[(hwnd, tickCount / 2000)] = text;
		}
		return text;
	}

	public static bool ShouldIgnore(nint hwnd)
	{
		return string.Equals(ModeForWindow(hwnd), Ignore, StringComparison.OrdinalIgnoreCase);
	}

	public static bool AllowsMove(nint hwnd)
	{
		return !string.Equals(ModeForWindow(hwnd), ResizeOnly, StringComparison.OrdinalIgnoreCase);
	}

	public static bool AllowsResize(nint hwnd)
	{
		return !string.Equals(ModeForWindow(hwnd), MoveOnly, StringComparison.OrdinalIgnoreCase);
	}

	public static bool AllowsFrame(nint hwnd)
	{
		return !string.Equals(ModeForWindow(hwnd), NoFrame, StringComparison.OrdinalIgnoreCase);
	}
}
