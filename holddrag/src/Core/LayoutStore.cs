// HoldDrag · Core/LayoutStore.cs
//
// 工作区布局：把当前所有可见窗口的位置/大小存成一份命名快照，之后一键恢复。
//
// 匹配策略是"三级回退"：exe + 标题完全一致 → exe 一致（取第一个）→ 跳过。
// 这样即使用户中间改过标题（比如浏览器换了标签页），也还能大致恢复。

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace HoldDrag;

internal static class LayoutStore
{
	internal sealed class Entry
	{
		public string Exe { get; set; } = "";

		public string Title { get; set; } = "";

		public int Left { get; set; }

		public int Top { get; set; }

		public int Width { get; set; }

		public int Height { get; set; }

		public bool Maximized { get; set; }
	}

	internal sealed class Layout
	{
		public string Name { get; set; } = "";

		public string SavedAt { get; set; } = "";

		public List<Entry> Windows { get; set; } = new List<Entry>();
	}

	private static readonly object Gate = new object();

	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public static string FilePath
	{
		get
		{
			try
			{
				return Path.Combine(AppPaths.DataDir, "layouts.json");
			}
			catch
			{
				return "layouts.json";
			}
		}
	}

	public static List<Layout> LoadAll()
	{
		lock (Gate)
		{
			try
			{
				if (!File.Exists(FilePath))
				{
					return new List<Layout>();
				}
				List<Layout> list = JsonSerializer.Deserialize<List<Layout>>(File.ReadAllText(FilePath), Options);
				return list ?? new List<Layout>();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				return new List<Layout>();
			}
		}
	}

	public static bool SaveAll(List<Layout> layouts)
	{
		lock (Gate)
		{
			try
			{
				string directoryName = Path.GetDirectoryName(FilePath);
				if (!string.IsNullOrEmpty(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				string text = JsonSerializer.Serialize(layouts, Options);
				File.WriteAllText(FilePath, text);
				return true;
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				return false;
			}
		}
	}

	/// <summary>把当前桌面上的窗口收进一份布局。</summary>
	public static Layout Capture(string name)
	{
		Layout layout = new Layout
		{
			Name = name,
			SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
		};
		try
		{
			Native.EnumWindows(delegate(nint hwnd, nint lParam)
			{
				try
				{
					if (!Native.IsWindowVisible(hwnd))
					{
						return true;
					}
					// 最小化窗口的 GetWindowRect 是 -32000 附近，存下来再恢复会把窗口丢到屏幕外
					if (Native.IsIconic(hwnd))
					{
						return true;
					}
					if ((Native.GetWindowLong(hwnd, -20) & 0x80) != 0)
					{
						return true;
					}
					int windowTextLength = Native.GetWindowTextLengthW(hwnd);
					if (windowTextLength <= 0)
					{
						return true;
					}
					Native.GetWindowThreadProcessId(hwnd, out var lpdwProcessId);
					if (lpdwProcessId == (uint)Environment.ProcessId)
					{
						return true;
					}
					string text = WindowActions.ProcessExeNameByPid(lpdwProcessId);
					if (string.IsNullOrEmpty(text))
					{
						return true;
					}
					string text2 = WindowActions.TitleOf(hwnd);
					if (!Native.GetWindowRect(hwnd, out var lpRect))
					{
						return true;
					}
					if (lpRect.Width < 40 || lpRect.Height < 40)
					{
						return true;
					}
					layout.Windows.Add(new Entry
					{
						Exe = text,
						Title = text2,
						Left = lpRect.Left,
						Top = lpRect.Top,
						Width = lpRect.Width,
						Height = lpRect.Height,
						Maximized = Native.IsZoomed(hwnd)
					});
				}
				catch
				{
				}
				return true;
			}, IntPtr.Zero);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return layout;
	}

	/// <summary>保存一份布局（同名覆盖）。</summary>
	public static Layout Save(string name)
	{
		Layout layout = Capture(name);
		List<Layout> list = LoadAll();
		list.RemoveAll((Layout l) => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
		list.Add(layout);
		SaveAll(list);
		return layout;
	}

	public static bool Delete(string name)
	{
		List<Layout> list = LoadAll();
		int num = list.RemoveAll((Layout l) => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
		if (num > 0)
		{
			SaveAll(list);
		}
		return num > 0;
	}

	/// <summary>恢复一份布局，返回成功移动的窗口数。</summary>
	public static int Restore(Layout layout)
	{
		if (layout == null || layout.Windows.Count == 0)
		{
			return 0;
		}
		List<(nint Hwnd, string Exe, string Title)> list = new List<(nint, string, string)>();
		try
		{
			Native.EnumWindows(delegate(nint hwnd, nint lParam)
			{
				try
				{
					if (Native.IsWindowVisible(hwnd) && (Native.GetWindowLong(hwnd, -20) & 0x80) == 0 && Native.GetWindowTextLengthW(hwnd) > 0)
					{
						Native.GetWindowThreadProcessId(hwnd, out var lpdwProcessId);
						if (lpdwProcessId != (uint)Environment.ProcessId)
						{
							string text = WindowActions.ProcessExeNameByPid(lpdwProcessId);
							if (!string.IsNullOrEmpty(text))
							{
								list.Add((hwnd, text, WindowActions.TitleOf(hwnd)));
							}
						}
					}
				}
				catch
				{
				}
				return true;
			}, IntPtr.Zero);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return 0;
		}
		HashSet<nint> hashSet = new HashSet<nint>();
		int num = 0;
		foreach (Entry window in layout.Windows)
		{
			nint num2 = IntPtr.Zero;
			foreach (var item2 in list)
			{
				if (hashSet.Contains(item2.Hwnd))
				{
					continue;
				}
				if (string.Equals(item2.Exe, window.Exe, StringComparison.OrdinalIgnoreCase) && string.Equals(item2.Title, window.Title, StringComparison.Ordinal))
				{
					num2 = item2.Hwnd;
					break;
				}
			}
			if (num2 == IntPtr.Zero)
			{
				foreach (var item3 in list)
				{
					if (!hashSet.Contains(item3.Hwnd) && string.Equals(item3.Exe, window.Exe, StringComparison.OrdinalIgnoreCase))
					{
						num2 = item3.Hwnd;
						break;
					}
				}
			}
			if (num2 == IntPtr.Zero)
			{
				continue;
			}
			try
			{
				hashSet.Add(num2);
				if (window.Maximized)
				{
					Native.ShowWindow(num2, 3);
				}
				else
				{
					bool flag = Native.IsZoomed(num2);
					if (flag)
					{
						Native.ShowWindow(num2, 9);
					}
					WindowActions.SetDragRect(num2, new Native.RECT(window.Left, window.Top, window.Left + window.Width, window.Top + window.Height));
				}
				num++;
			}
			catch (Exception ex2)
			{
				Log.Error(ex2);
			}
		}
		return num;
	}
}
