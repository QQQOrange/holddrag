// HoldDrag · App/AppPaths.cs

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

internal static class AppPaths
{
	/// <summary>
	/// 用户手里那个 exe（外层启动器）的路径，由启动器用 <c>--host</c> 传进来；直接跑主程序时为空。
	/// </summary>
	internal static string HostPath;

	/// <summary>
	/// 「用户双击的那个文件」：有启动器时是启动器，没有时就是自己。
	/// 自动更新要替换它（否则只换了 %LOCALAPPDATA% 里的副本，下次启动又跑回旧版），
	/// 开机自启也要注册它（否则绕开启动器，缺 .NET 10 时就没有中文提示了）。
	/// </summary>
	public static string EntryPath
	{
		get
		{
			return string.IsNullOrEmpty(HostPath) ? ExePath : HostPath;
		}
	}

	/// <summary>
	/// 数据目录：默认 %APPDATA%\HoldDrag；如果 exe 同目录下有 <c>portable.txt</c>，
	/// 就改成 exe 旁边的 <c>data\</c>（U 盘 / 绿色版）。启动器与主程序都用这一份逻辑。
	/// </summary>
	public static string DataDir
	{
		get
		{
			try
			{
				string directoryName = Path.GetDirectoryName(EntryPath);
				if (!string.IsNullOrEmpty(directoryName))
				{
					string path = Path.Combine(directoryName, "portable.txt");
					if (File.Exists(path))
					{
						string text = Path.Combine(directoryName, "data");
						Directory.CreateDirectory(text);
						return text;
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
		}
	}

	public static bool IsPortable
	{
		get
		{
			try
			{
				string directoryName = Path.GetDirectoryName(EntryPath);
				return !string.IsNullOrEmpty(directoryName) && File.Exists(Path.Combine(directoryName, "portable.txt"));
			}
			catch
			{
				return false;
			}
		}
	}

	public static string ExePath
	{
		get
		{
			try
			{
				string processPath = Environment.ProcessPath;
				if (!string.IsNullOrEmpty(processPath) && !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
				{
					return processPath;
				}
				Assembly entryAssembly = Assembly.GetEntryAssembly();
				string text = ((entryAssembly != null) ? entryAssembly.GetName().Name : "HoldDrag");
				if (string.IsNullOrEmpty(text))
				{
					text = "HoldDrag";
				}
				string text2 = Path.Combine(AppContext.BaseDirectory, text + ".exe");
				if (File.Exists(text2))
				{
					return text2;
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			return Application.ExecutablePath;
		}
	}
}
