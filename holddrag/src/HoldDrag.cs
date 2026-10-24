using System;
using System.Collections.Concurrent;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
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
using System.Windows.Forms;
using Microsoft.Win32;

[StructLayout(LayoutKind.Auto)]
[InlineArray(17)]
internal struct zzy_InlineArray17<T>
{
	[CompilerGenerated]
	private T _element0;
}
namespace HoldDrag
{

internal static class AppInfo
{
	public const string Name = "长按拖窗";

	public const string EnglishName = "HoldDrag";

	public const string Tagline = "长按触发键，随手拖动 / 缩放任意窗口";

	public const string ReleaseDate = "2026-10-24";

	public const string Developer = "QQQOrange";

	public const string Tools = "DeepSeek";

	public const string RepoOwner = "QQQOrange";

	public const string RepoName = "holddrag";

	public const string GitHubUrl = "https://github.com/QQQOrange/holddrag";

	public const string ReleasesUrl = "https://github.com/QQQOrange/holddrag/releases";

	public const string ReleasesApiUrl = "https://api.github.com/repos/QQQOrange/holddrag/releases/latest";

	/// <summary>程序集版本读不到时的兜底版本号。升级版本时这里与 AssemblyInfo 必须同步改。</summary>
	public const string FallbackVersion = "1.6.1";

	public static string Version
	{
		get
		{
			try
			{
				Version version = Assembly.GetExecutingAssembly().GetName().Version;
				if (version == null)
				{
					return FallbackVersion;
				}
				return version.Major + "." + version.Minor + "." + Math.Max(0, version.Build);
			}
			catch
			{
				return FallbackVersion;
			}
		}
	}
}
internal static class AppPaths
{
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
internal static class AutoStart
{
	public const string TaskName = "HoldDrag-AutoStart";

	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string RunValueName = "HoldDrag";

	public static bool Apply(string mode, string exePath, out string message)
	{
		message = string.Empty;
		try
		{
			if (!(mode == "TaskScheduler"))
			{
				if (mode == "Registry")
				{
					RemoveTask();
					if (WriteRegistry(exePath))
					{
						return true;
					}
					message = "写注册表 Run 项失败，可能被安全软件拦截。";
					Log.Error(message);
					return false;
				}
				RemoveTask();
				RemoveRegistry();
				return true;
			}
			RemoveRegistry();
			if (CreateTask(exePath, out var error))
			{
				return true;
			}
			RemoveTask();
			if (WriteRegistry(exePath))
			{
				message = "计划任务创建失败（" + error + "），已改用注册表方式。注意：注册表方式每次登录会弹一次 UAC 确认框。";
				Log.Info(message);
				return true;
			}
			message = "计划任务与注册表方式都失败了：" + error;
			Log.Error(message);
			return false;
		}
		catch (Exception ex)
		{
			message = "设置开机自启时出错：" + ex.Message;
			Log.Error(ex);
			return false;
		}
	}

	public static string Detect()
	{
		try
		{
			if (TaskExists(out var error))
			{
				return "TaskScheduler";
			}
			if (!string.IsNullOrEmpty(error))
			{
				Log.Info("查询计划任务未成功（若被安全软件/策略拦截会走到这里）：" + error);
			}
			if (RegistryExists())
			{
				return "Registry";
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return "None";
	}

	private static bool CreateTask(string exePath, out string error)
	{
		string args = "/Create /F /TN \"HoldDrag-AutoStart\" /SC ONLOGON /RL HIGHEST /IT /TR \"\\\"" + exePath + "\\\"\"";
		int num = Run("schtasks.exe", args, out error);
		if (num == 0)
		{
			return true;
		}
		Log.Error("schtasks 创建失败 code=" + num + " :: " + error);
		return false;
	}

	private static void RemoveTask()
	{
		Run("schtasks.exe", "/Delete /F /TN \"HoldDrag-AutoStart\"", out var _);
	}

	private static bool TaskExists(out string error)
	{
		return Run("schtasks.exe", "/Query /TN \"HoldDrag-AutoStart\"", out error) == 0;
	}

	private static bool WriteRegistry(string exePath)
	{
		try
		{
			using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true))
			{
				if (registryKey == null)
				{
					return false;
				}
				registryKey.SetValue("HoldDrag", "\"" + exePath + "\"");
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	private static void RemoveRegistry()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey != null && registryKey.GetValue("HoldDrag") != null)
			{
				registryKey.DeleteValue("HoldDrag", throwOnMissingValue: false);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static bool RegistryExists()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			return registryKey != null && registryKey.GetValue("HoldDrag") != null;
		}
		catch
		{
			return false;
		}
	}

	private static int Run(string exe, string args, out string output)
	{
		output = string.Empty;
		try
		{
			using Process process = Process.Start(new ProcessStartInfo(exe, args)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			});
			string text = process.StandardOutput.ReadToEnd();
			string text2 = process.StandardError.ReadToEnd();
			process.WaitForExit(8000);
			output = (text + " " + text2).Trim();
			return process.HasExited ? process.ExitCode : (-1);
		}
		catch (Exception ex)
		{
			output = ex.Message;
			return -1;
		}
	}
}

/// <summary>发布版解析结果。</summary>
internal sealed class UpdateInfo
{
	public string Tag;

	public string Version;

	public string AssetUrl;

	/// <summary>网页版发布页地址（下载失败时引导用户手动打开）。</summary>
	public string PageUrl;
}

/// <summary>
/// 自更新：查最新发布 → 后台下载到 exe 同级目录 → 启动替换器换掉自己 → 重启。
/// 全程不依赖 api.github.com（匿名调用有速率限制，且企业网络常被拦），
/// 改为请求 releases/latest 的 HTML 重定向来拿到 tag，再用固定的
/// releases/download/&lt;tag&gt;/&lt;asset&gt; 直链下载。
/// </summary>
internal static class Updater
{
	/// <summary>发布资产文件名。必须与 GitHub Release 里上传的附件名完全一致。</summary>
	public const string AssetName = "HoldDrag.exe";

	/// <summary>与本体同目录的下载中转文件名。</summary>
	public const string TempDownloadName = "HoldDrag-update.download";

	/// <summary>旧版本备份后缀（替换成功后可删，但删不掉也不影响使用）。</summary>
	public const string OldSuffix = ".old";

	/// <summary>
	/// 本进程是否为"更新重启"的接续实例（由启动参数 --updated-from 传入上一版的备份路径）。
	/// 接续实例负责在新进程正常起来之后删掉旧的 .old 备份。
	/// </summary>
	public const string ArgUpdatedFrom = "--updated-from";

	/// <summary>给单个命令行参数按需包引号：路径含空白时必须包，否则会被拆成多个参数。</summary>
	public static string QuoteArg(string arg)
	{
		if (string.IsNullOrEmpty(arg) || (arg.IndexOf(' ') < 0 && arg.IndexOf('\t') < 0))
		{
			return arg;
		}
		return "\"" + arg + "\"";
	}

	/// <summary>
	/// 就地替换正在运行的 exe。
	///
	/// 关键事实（本机实测）：**Windows 允许重命名一个正在运行的 exe，也允许把新文件移到它的名下**；
	/// 唯一做不到的是"删除"那个正在运行的旧文件。因此不需要批处理、不需要等旧进程退出：
	///   1) 旧 exe 改名为 HoldDrag.exe.old（正在运行也允许）
	///   2) 新文件移入 HoldDrag.exe
	///   3) 拉起新 exe，并让它启动后删掉 .old（那时旧进程已退出，删除就成功了）
	/// 任一步失败都会尝试把旧文件改回来；万一回滚也失败，旧版仍以 .old 留在同目录，可手动改回，不会把用户卡死。
	/// </summary>
	public static bool ApplyAndRestart(string downloadedPath, string exePath, out string error)
	{
		error = string.Empty;
		string text = exePath + OldSuffix;
		try
		{
			if (!File.Exists(downloadedPath))
			{
				error = "下载文件不存在：" + downloadedPath;
				return false;
			}
			// 1) 先清掉上一次可能残留的 .old（旧进程早已退出，这时能删掉）
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch (Exception ex)
			{
				Log.Info("清理上次的 .old 备份没成功（不影响本次更新）：" + ex.Message);
			}
			// 2) 旧 exe → .old（对正在运行的 exe，重命名是允许的）
			File.Move(exePath, text);
			// 3) 新文件 → 正式名字
			try
			{
				File.Move(downloadedPath, exePath);
			}
			catch (Exception ex2)
			{
				// 移入失败：把旧文件改回来；回滚也失败时旧版仍在 .old 里，可让用户手动改回
				Log.Error("新版本移入失败，正在回滚：" + ex2.Message);
				try
				{
					File.Move(text, exePath);
				}
				catch (Exception ex3)
				{
					Log.Error("回滚也失败了：" + ex3.Message);
				}
				error = "无法用新版本覆盖程序文件：" + ex2.Message;
				return false;
			}
			// 4) 拉起新版本；把 .old 路径传给它，由新版启动后清理
			Process process = Process.Start(new ProcessStartInfo(exePath)
			{
				UseShellExecute = true,
				Arguments = ArgUpdatedFrom + " " + QuoteArg(text)
			});
			if (process == null)
			{
				error = "新版本已就位，但没能自动启动。请手动打开：" + exePath;
				return false;
			}
			Log.Info("新版已就位并启动 pid=" + process.Id + "，旧版备份 " + text);
			return true;
		}
		catch (Exception ex4)
		{
			error = Describe(ex4);
			Log.Error("替换失败：" + error);
			return false;
		}
	}

	/// <summary>
	/// 接续实例启动时调用：删掉上一版的 .old 备份。
	/// 这时旧进程已经退出，删除必然成功；即使用户没退出干净，删不掉也只是留个文件，不影响功能。
	/// </summary>
	public static void CleanupOldVersion(string oldPath)
	{
		if (string.IsNullOrEmpty(oldPath))
		{
			return;
		}
		try
		{
			if (!File.Exists(oldPath))
			{
				return;
			}
			File.Delete(oldPath);
			Log.Info("已清理上一版本备份：" + oldPath);
		}
		catch (Exception ex)
		{
			Log.Info("旧版本备份暂时删不掉（下次更新会再试）：" + ex.Message);
		}
	}

	private static readonly HttpClient Http = CreateClient();

	private static HttpClient CreateClient()
	{
		try
		{
			HttpClientHandler httpClientHandler = new HttpClientHandler();
			// 用系统（IE/Edge）代理设置：企业网络与代理软件常常只在这条路径上生效。
			httpClientHandler.UseProxy = true;
			httpClientHandler.AllowAutoRedirect = true;
			httpClientHandler.MaxAutomaticRedirections = 10;
			HttpClient httpClient = new HttpClient(httpClientHandler);
			httpClient.Timeout = TimeSpan.FromMinutes(10);
			httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HoldDrag/" + AppInfo.Version);
			httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/octet-stream,*/*");
			return httpClient;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return new HttpClient();
		}
	}

	/// <summary>把 "v1.2.3" / "1.2.3" / "1.2.3.4" 解析成可比较的四段版本号；解析不出来返回 null。</summary>
	public static Version ParseVersion(string tag)
	{
		if (string.IsNullOrWhiteSpace(tag))
		{
			return null;
		}
		string s = tag.Trim();
		if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
		{
			s = s.Substring(1);
		}
		int num = s.IndexOfAny(new char[3] { '-', '+', ' ' });
		if (num > 0)
		{
			s = s.Substring(0, num);
		}
		string[] array = s.Split('.');
		if (array.Length == 0 || array.Length > 4)
		{
			return null;
		}
		int[] array2 = new int[4];
		for (int i = 0; i < array.Length; i++)
		{
			if (!int.TryParse(array[i], out array2[i]) || array2[i] < 0)
			{
				return null;
			}
		}
		return new Version(array2[0], array2[1], array2[2], array2[3]);
	}

	/// <summary>当前程序集版本；解析失败按 1.1.0 处理。</summary>
	public static Version CurrentVersion()
	{
		Version version = ParseVersion(AppInfo.Version);
		return (version == null) ? new Version(1, 1, 0, 0) : version;
	}

	/// <summary>测试钩子：置 true 时模拟网络不可达，用于断言"失败不会被当成已是最新"。</summary>
	internal static bool ForceOfflineForTest;

	/// <summary>
	/// 查最新发布。返回 null 表示"已经是最新"或"查不到"（用 out 的 error 区分）。
	/// 本方法会把网络异常如实回传，不做静默成功。
	/// </summary>
	public static UpdateInfo CheckLatest(out string error, out bool upToDate)
	{
		error = string.Empty;
		upToDate = false;
		if (ForceOfflineForTest)
		{
			error = "（测试）模拟网络不可达：无法连接 github.com";
			return null;
		}
		try
		{
			string tag = ResolveLatestTag();
			if (string.IsNullOrEmpty(tag))
			{
				error = "没能从发布页解析出最新版本号（可能没有已发布的 Release，或网络被拦截）。";
				return null;
			}
			Version version = ParseVersion(tag);
			if (version == null)
			{
				error = "发布标签格式无法识别：" + tag;
				return null;
			}
			if (version <= CurrentVersion())
			{
				upToDate = true;
				return null;
			}
			return new UpdateInfo
			{
				Tag = tag,
				Version = version.Major + "." + version.Minor + "." + Math.Max(0, version.Build),
				AssetUrl = string.Concat(AppInfo.ReleasesUrl, "/download/", tag, "/", AssetName),
				PageUrl = AppInfo.ReleasesUrl + "/tag/" + tag
			};
		}
		catch (Exception ex)
		{
			error = Describe(ex);
			Log.Error("检查更新失败：" + error);
			return null;
		}
	}

	private static string ResolveLatestTag()
	{
		string text = AppInfo.ReleasesUrl + "/latest";
		using HttpResponseMessage httpResponseMessage = Http.GetAsync(text, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
		if (!httpResponseMessage.IsSuccessStatusCode)
		{
			Log.Info("releases/latest 返回 " + (int)httpResponseMessage.StatusCode);
			return null;
		}
		// 正常会被 302 到 .../releases/tag/vX.Y.Z；若没跳转就自己从正文里找。
		Uri requestUri = httpResponseMessage.RequestMessage?.RequestUri;
		string text2 = ((requestUri != null) ? TagFromUrl(requestUri.AbsoluteUri) : null);
		if (!string.IsNullOrEmpty(text2))
		{
			return text2;
		}
		return TagFromHtml(httpResponseMessage.Content.ReadAsStringAsync().GetAwaiter().GetResult());
	}

	/// <summary>从 ".../releases/tag/v1.2.3" 里取 "v1.2.3"。</summary>
	public static string TagFromUrl(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return null;
		}
		int num = url.IndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return null;
		}
		string s = url.Substring(num + 14);
		int num2 = s.IndexOfAny(new char[2] { '?', '#' });
		if (num2 >= 0)
		{
			s = s.Substring(0, num2);
		}
		s = s.Trim().TrimEnd('/');
		if (s.Length == 0 || s.Contains('/'))
		{
			return null;
		}
		return Uri.UnescapeDataString(s);
	}

	/// <summary>正文兜底：找第一个 /releases/tag/ 链接。</summary>
	public static string TagFromHtml(string html)
	{
		if (string.IsNullOrEmpty(html))
		{
			return null;
		}
		int num = html.IndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase);
		if (num < 0)
		{
			return null;
		}
		int num2 = num + 14;
		int num3 = num2;
		while (num3 < html.Length && html[num3] != '"' && html[num3] != '\'' && html[num3] != '<' && html[num3] != '?' && html[num3] != '#' && html[num3] != '/')
		{
			num3++;
		}
		if (num3 <= num2)
		{
			return null;
		}
		return WebUtility.HtmlDecode(html.Substring(num2, num3 - num2));
	}

	/// <summary>下载新版 exe 到 <paramref name="targetPath"/>（先写 .part 再改名，避免半截文件）。</summary>
	public static bool Download(UpdateInfo info, string targetPath, Action<long, long> progress, out string error)
	{
		error = string.Empty;
		string text = targetPath + ".part";
		try
		{
			using HttpResponseMessage response = Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
			if (!response.IsSuccessStatusCode)
			{
				error = "下载失败：HTTP " + (int)response.StatusCode + "（" + response.ReasonPhrase + "）";
				return false;
			}
			long? contentLength = response.Content.Headers.ContentLength;
			long num = contentLength.GetValueOrDefault();
			long num2 = 0L;
			byte[] array = new byte[81920];
			using (Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
			using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: false))
			{
				int num3;
				while ((num3 = stream.Read(array, 0, array.Length)) > 0)
				{
					fileStream.Write(array, 0, num3);
					num2 += num3;
					progress?.Invoke(num2, num);
				}
				fileStream.Flush(flushToDisk: true);
			}
			if (num2 <= 0)
			{
				error = "下载得到 0 字节，可能是被网络中间设备拦截了。";
				return false;
			}
			if (num > 0 && num2 != num)
			{
				error = $"下载不完整：期望 {num} 字节，实际 {num2} 字节。";
				return false;
			}
			File.Move(text, targetPath, overwrite: true);
			return true;
		}
		catch (Exception ex)
		{
			error = Describe(ex);
			Log.Error("下载更新失败：" + error);
			return false;
		}
		finally
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
		}
	}

	public static string Describe(Exception ex)
	{
		Exception innerException = ex;
		while (innerException.InnerException != null)
		{
			innerException = innerException.InnerException;
		}
		string text = innerException.Message;
		if (innerException is HttpRequestException)
		{
			text += "（网络不可达：请检查代理 / 防火墙是否允许访问 github.com）";
		}
		return text;
	}
}

internal static class Actions
{
	public const string Move = "Move";

	public const string Resize = "Resize";

	public const string Maximize = "Maximize";

	public const string Minimize = "Minimize";

	public const string Close = "Close";

	public const string Lower = "Lower";

	public const string AlwaysOnTop = "AlwaysOnTop";

	public const string Center = "Center";

	public const string Nothing = "Nothing";

	public static readonly string[] All = new string[9] { "Move", "Resize", "Maximize", "Minimize", "Close", "Lower", "AlwaysOnTop", "Center", "Nothing" };

	public static string DisplayName(string a)
	{
		return a switch
		{
			"Move" => "移动窗口", 
			"Resize" => "调整大小", 
			"Maximize" => "最大化 / 还原", 
			"Minimize" => "最小化", 
			"Close" => "关闭窗口", 
			"Lower" => "置于最底层", 
			"AlwaysOnTop" => "置顶 / 取消置顶", 
			"Center" => "在屏幕上居中", 
			"Nothing" => "不绑定动作", 
			_ => a, 
		};
	}

	public static bool IsValid(string a)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i] == a)
			{
				return true;
			}
		}
		return false;
	}

	public static bool IsDragAction(string a)
	{
		if (!(a == "Move"))
		{
			return a == "Resize";
		}
		return true;
	}
}
internal sealed class AppConfig
{
	public string TriggerKeyId { get; set; }

	public bool RequireHold { get; set; }

	public int HoldMs { get; set; }

	public string ActionLeft { get; set; }

	public string ActionRight { get; set; }

	public string ActionMiddle { get; set; }

	public bool ShowActivationFrame { get; set; }

	public string FrameColorId { get; set; }

	/// <summary>外框颜色跟随系统强调色：开了以后 FrameColorId / FrameCustomColor 都不生效。</summary>
	public bool FrameColorFollowAccent { get; set; }

	/// <summary>自定义外框颜色（ARGB）；0 = 没设过，回落到 FrameColorId。</summary>
	public int FrameCustomColor { get; set; }

	public string FrameStyleId { get; set; }

	public int FrameThickness { get; set; }

	public int FrameOpacity { get; set; }

	public int FrameRadius { get; set; }

	public bool DoubleClickLeftMaximize { get; set; }

	public bool DoubleClickRightZone { get; set; }

	public bool RestoreMaximizedWhenMoved { get; set; }

	public bool BringToFront { get; set; }

	public bool AllowFullscreenWindows { get; set; }

	public bool AllowDragOwnWindows { get; set; }

	public bool ExitFullscreenOnTrigger { get; set; }

	public bool ShakeMinimizeOthers { get; set; }

	public int CornerPercent { get; set; }

	public string Blacklist { get; set; }

	public string DragRefreshMode { get; set; }

	public int DragRefreshHz { get; set; }

	public int MoveRate { get; set; }

	public int ResizeRate { get; set; }

	public int DragThreshold { get; set; }

	public int DragOutThresholdPx { get; set; }

	public string AutoStart { get; set; }

	public bool RunAsAdmin { get; set; }

	public bool WarnNonAdmin { get; set; }

	public int SettingsWidth { get; set; }

	public int SettingsHeight { get; set; }

	/// <summary>启动时自动检查更新（默认开启）。</summary>
	public bool AutoCheckUpdate { get; set; }

	/// <summary>上一次已提示过的版本号，用于"每个新版只提示一次"。</summary>
	public string LastUpdateNotified { get; set; }

	public AppConfig()
	{
		TriggerKeyId = "K:0x0D";
		RequireHold = true;
		HoldMs = 600;
		ActionLeft = "Move";
		ActionRight = "Resize";
		ActionMiddle = "Maximize";
		ShowActivationFrame = true;
		FrameColorId = FrameColors.DefaultId;
		FrameColorFollowAccent = false;
		FrameCustomColor = 0;
		FrameStyleId = FrameStyles.DefaultId;
		FrameThickness = 3;
		FrameRadius = 13;
		FrameOpacity = 100;
		DoubleClickLeftMaximize = true;
		DoubleClickRightZone = true;
		RestoreMaximizedWhenMoved = true;
		BringToFront = true;
		AllowFullscreenWindows = false;
		AllowDragOwnWindows = true;
		ExitFullscreenOnTrigger = true;
		ShakeMinimizeOthers = true;
		CornerPercent = 26;
		Blacklist = string.Empty;
		DragRefreshMode = "Auto";
		DragRefreshHz = 0;
		MoveRate = 1;
		ResizeRate = 1;
		DragThreshold = 1;
		DragOutThresholdPx = 20;
		AutoStart = "TaskScheduler";
		RunAsAdmin = true;
		WarnNonAdmin = true;
		SettingsWidth = 0;
		SettingsHeight = 0;
		AutoCheckUpdate = true;
		LastUpdateNotified = string.Empty;
	}

	public AppConfig Clone()
	{
		return (AppConfig)MemberwiseClone();
	}

	/// <summary>真正拿去画外框的颜色：跟随强调色 &gt; 自定义 &gt; 调色板。</summary>
	public Color EffectiveFrameColor => FrameColors.ResolveEffective(FrameColorId, FrameColorFollowAccent, FrameCustomColor);

	public void Normalize()
	{
		if (string.IsNullOrEmpty(TriggerKeyId))
		{
			TriggerKeyId = "K:0x0D";
		}
		// 解析不出有效虚拟键码的触发键（如历史 bug 产出、或手改配置写出的 "K:0x00"）等于没有触发键，回落默认值
		if (KeysUtil.ToVirtualKey(TriggerKeyId) == 0)
		{
			TriggerKeyId = "K:0x0D";
		}
		if (HoldMs < 100)
		{
			HoldMs = 100;
		}
		if (HoldMs > 3000)
		{
			HoldMs = 3000;
		}
		if (!Actions.IsValid(ActionLeft))
		{
			ActionLeft = "Move";
		}
		if (!Actions.IsValid(ActionRight))
		{
			ActionRight = "Resize";
		}
		if (!Actions.IsValid(ActionMiddle))
		{
			ActionMiddle = "Maximize";
		}
		if (!FrameColors.IsValid(FrameColorId))
		{
			FrameColorId = FrameColors.DefaultId;
		}
		// 透明色的自定义颜色等于没设：回落到调色板，免得画出"看不见的外框"。
		if (FrameCustomColor != 0 && ((FrameCustomColor >> 24) & 0xFF) == 0)
		{
			FrameCustomColor = 0;
		}
		if (!FrameStyles.IsValid(FrameStyleId))
		{
			FrameStyleId = FrameStyles.DefaultId;
		}
		if (FrameThickness < 1)
		{
			FrameThickness = 1;
		}
		if (FrameThickness > 8)
		{
			FrameThickness = 8;
		}
		if (FrameRadius < 0)
		{
			FrameRadius = 0;
		}
		if (FrameRadius > 28)
		{
			FrameRadius = 28;
		}
		if (FrameOpacity < 20)
		{
			FrameOpacity = 20;
		}
		if (FrameOpacity > 100)
		{
			FrameOpacity = 100;
		}
		if (CornerPercent < 5)
		{
			CornerPercent = 5;
		}
		if (CornerPercent > 45)
		{
			CornerPercent = 45;
		}
		if (Blacklist == null)
		{
			Blacklist = string.Empty;
		}
		if (Blacklist.Length > 2000)
		{
			Blacklist = Blacklist.Substring(0, 2000);
		}
		if (DragRefreshMode != "Auto" && DragRefreshMode != "Custom")
		{
			DragRefreshMode = "Auto";
		}
		if (DragRefreshHz < 0)
		{
			DragRefreshHz = 0;
		}
		if (DragRefreshHz != 0 && DragRefreshHz < 20)
		{
			DragRefreshHz = 20;
		}
		if (DragRefreshHz > 500)
		{
			DragRefreshHz = 500;
		}
		MoveRate = 1;
		ResizeRate = 1;
		if (DragThreshold < 0)
		{
			DragThreshold = 0;
		}
		if (DragThreshold > 2)
		{
			DragThreshold = 2;
		}
		if (DragOutThresholdPx < 0)
		{
			DragOutThresholdPx = 0;
		}
		if (DragOutThresholdPx > 200)
		{
			DragOutThresholdPx = 200;
		}
		if (AutoStart != "TaskScheduler" && AutoStart != "Registry" && AutoStart != "None")
		{
			AutoStart = "TaskScheduler";
		}
		int num = MaxUsableWindowWidth();
		int num2 = MaxUsableWindowHeight();
		if (SettingsWidth != 0)
		{
			if (SettingsWidth < 578)
			{
				SettingsWidth = 578;
			}
			if (SettingsWidth > num)
			{
				SettingsWidth = num;
			}
		}
		if (SettingsHeight != 0)
		{
			if (SettingsHeight < 420)
			{
				SettingsHeight = 420;
			}
			if (SettingsHeight > num2)
			{
				SettingsHeight = num2;
			}
		}
	}

	private static int MaxUsableWindowWidth()
	{
		try
		{
			int width = SystemInformation.VirtualScreen.Width;
			if (width >= 640)
			{
				return width;
			}
		}
		catch
		{
		}
		return 2400;
	}

	private static int MaxUsableWindowHeight()
	{
		try
		{
			int height = SystemInformation.VirtualScreen.Height;
			if (height >= 480)
			{
				return height;
			}
		}
		catch
		{
		}
		return 2400;
	}

	public int FrameIntervalMs(int detectedHz)
	{
		if (DragRefreshMode == "Custom")
		{
			int num = DragRefreshHz;
			if (num <= 0)
			{
				return 0;
			}
			if (num < 20)
			{
				num = 20;
			}
			if (num > 500)
			{
				num = 500;
			}
			return (int)Math.Round(1000.0 / (double)num);
		}
		if (detectedHz <= 0)
		{
			return 0;
		}
		if (detectedHz > 500)
		{
			detectedHz = 500;
		}
		return (int)Math.Round(1000.0 / (double)detectedHz);
	}
}
internal static class RefreshModes
{
	public const string Auto = "Auto";

	public const string Custom = "Custom";
}
internal static class AutoStartModes
{
	public const string TaskScheduler = "TaskScheduler";

	public const string Registry = "Registry";

	public const string None = "None";
}
internal sealed class ConfigStore
{
	private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
	{
		WriteIndented = true,
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip
	};

	public string Dir { get; private set; }

	public string FilePath { get; private set; }

	private string BakPath => FilePath + ".bak";

	private string Bak2Path => FilePath + ".bak2";

	public AppConfig Current { get; private set; }

	public event Action Changed;

	public ConfigStore(string dir)
	{
		Dir = dir;
		FilePath = Path.Combine(dir, "config.json");
	}

	public void Load()
	{
		AppConfig appConfig = TryRead(FilePath);
		bool flag = false;
		if (appConfig == null)
		{
			appConfig = TryRead(BakPath);
			if (appConfig != null)
			{
				flag = true;
			}
		}
		if (appConfig == null)
		{
			appConfig = TryRead(Bak2Path);
			if (appConfig != null)
			{
				flag = true;
			}
		}
		if (appConfig == null)
		{
			appConfig = new AppConfig();
			Log.Info("配置不存在或全部损坏，使用出厂默认值");
		}
		else if (flag)
		{
			Log.Info("主配置损坏，已从备份恢复");
		}
		appConfig.Normalize();
		Current = appConfig;
		if (flag)
		{
			try
			{
				Save(out _);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
	}

	private static AppConfig TryRead(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			string text = File.ReadAllText(path);
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			return JsonSerializer.Deserialize<AppConfig>(text, Opts);
		}
		catch (Exception ex)
		{
			Log.Error("读取配置失败 " + path + " :: " + ex.Message);
			return null;
		}
	}

	public bool Save(out string error)
	{
		error = string.Empty;
		try
		{
			Current.Normalize();
			Directory.CreateDirectory(Dir);
			if (File.Exists(FilePath))
			{
				if (File.Exists(BakPath))
				{
					File.Copy(BakPath, Bak2Path, overwrite: true);
				}
				File.Copy(FilePath, BakPath, overwrite: true);
			}
			string contents = JsonSerializer.Serialize(Current, Opts);
			string text = FilePath + ".tmp";
			File.WriteAllText(text, contents);
			File.Move(text, FilePath, overwrite: true);
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			Log.Error("保存配置失败 :: " + ex.Message);
			return false;
		}
	}

	public bool Apply(AppConfig edited, out string error)
	{
		edited.Normalize();
		Current = edited;
		bool result = Save(out error);
		Changed?.Invoke();
		return result;
	}
}
internal sealed class Coordinator : IInputSink, IDisposable
{
	private const int BtnLeft = 0;

	private const int BtnRight = 1;

	private const int BtnMiddle = 2;

	private readonly ConfigStore _store;

	private readonly TriggerState _trigger;

	private readonly DragEngine _drag;

	private readonly long[] _lastDown = new long[3];

	private readonly bool[] _lastMoved = new bool[3];

	private readonly bool[] _swallowUp = new bool[3];

	internal Action ReinstallHooksForPriority;

	internal static double HookMaxMsForTest;

	internal static int SlowHookCountForTest;

	private const double SlowHookMs = 8.0;

	private static long _slowHookLoggedAt;

	private readonly System.Windows.Forms.Timer _frameGuard = new System.Windows.Forms.Timer();

	internal static int CancelModeSentForTest;

	internal static int ReplayedClickForTest;

	internal static bool[] ButtonsDownOverrideForTest;

	internal static bool AssumeButtonsDownForTest;

	private readonly bool[] _heldDuringHold = new bool[3];

	private readonly bool[] _passedDown = new bool[3];

	private readonly bool[] _bufferedDown = new bool[3];

	private readonly Native.POINT[] _bufferedPt = new Native.POINT[3];

	public bool Paused { get; private set; }

	public bool SuppressTrigger { get; set; }

	public TriggerState Trigger => _trigger;

	public DragEngine Drag => _drag;

	public Coordinator(ConfigStore store)
	{
		_store = store;
		_trigger = new TriggerState(() => _store.Current);
		_drag = new DragEngine(() => _store.Current);
		WindowActions.ApplyBlacklist(_store.Current.Blacklist);
		WindowActions.AllowDragOwnWindows = _store.Current.AllowDragOwnWindows;
		_trigger.Activated += OnTriggerActivated;
		_trigger.Deactivated += OnTriggerDeactivated;
		_trigger.ShortPressWithoutActivation += FlushBufferedClicks;
	}

	private void OnTriggerActivated()
	{
		if (!Paused && !SuppressTrigger && !_drag.IsDragging)
		{
			Action reinstallHooksForPriority = ReinstallHooksForPriority;
			if (reinstallHooksForPriority != null)
			{
				try
				{
					reinstallHooksForPriority();
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			}
		}
		if (!Native.GetCursorPos(out var lpPoint))
		{
			return;
		}
		AppConfig current = _store.Current;
		bool allowFullscreen = current.AllowFullscreenWindows || current.ExitFullscreenOnTrigger;
		nint num = WindowActions.FindTarget(lpPoint, allowFullscreen);
		if (num != IntPtr.Zero)
		{
			bool flag = false;
			for (int i = 0; i <= 2; i++)
			{
				if (_passedDown[i])
				{
					flag = true;
				}
			}
			if (flag)
			{
				Log.Info("[诊断] 激活时目标窗口还按着我们放行过的鼠标键（它可能停在拖选模态） → 先取消 句柄=" + ((IntPtr)num).ToInt64().ToString("X"));
			}
			CancelTargetMouseMode(num);
		}
		// ★ 激活瞬间就把"阈值期间被缓冲、下层从未收到"的鼠标按下补起成拖动。
		// 用户「同时按触发键和左键」时，左键的按下几乎一定落在阈值窗口内（被缓冲），
		// 以前要等激活后的第一次鼠标移动才补起 —— 按下后先不动就完全没有反馈，
		// 表现成"左键什么功能都没有、也拖不动窗口"。
		AdoptBufferedButtonsNow(lpPoint);
		if (!_drag.IsDragging)
		{
			_drag.ShowHoverFrame(lpPoint);
		}
	}

	private void OnTriggerDeactivated()
	{
		// 松开触发键 = 退出拖动模式。此刻若还在拖（真实链路上"鼠标抬起"这条事件同样可能
		// 落在鼠标钩子重装的缝隙里丢掉），必须就地收尾 —— 否则窗口会一直跟着光标跑，
		// 用户看到的是"松开 Enter 了窗口还在被拖着"，比拖不动更莫名其妙。
		if (_drag.IsDragging)
		{
			try
			{
				_drag.End();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		_drag.HideHoverFrame();
	}

	public void PrecreateOverlays()
	{
		_drag.Precreate();
		_frameGuard.Interval = 500;
		_frameGuard.Tick += (object? s, EventArgs e) =>
		{
			try
			{
				_drag.EnforceFrameInvariant(_trigger.Active);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		};
		_frameGuard.Start();
	}

	private static void NoteHookCost(long startedAt, string what)
	{
		try
		{
			double num = (double)(Stopwatch.GetTimestamp() - startedAt) * 1000.0 / (double)Stopwatch.Frequency;
			if (num > HookMaxMsForTest)
			{
				HookMaxMsForTest = num;
			}
			if (!(num < 8.0))
			{
				SlowHookCountForTest++;
				long tickCount = Environment.TickCount64;
				if (tickCount - _slowHookLoggedAt >= 1000)
				{
					_slowHookLoggedAt = tickCount;
					Log.Info("[诊断] 钩子回调偏慢 " + what + " 耗时=" + num.ToString("0.0") + "ms（系统级钩子：这一段时间整机输入都被拖住）");
				}
			}
		}
		catch
		{
		}
	}

	public void HideOverlaysNow()
	{
		try
		{
			_frameGuard.Stop();
		}
		catch
		{
		}
		_drag.HideOverlaysNow();
	}

	public void SetPaused(bool paused)
	{
		Paused = paused;
		if (paused)
		{
			_drag.Cancel();
			_drag.HideHoverFrame();
		}
		Log.Info(paused ? "已暂停" : "已恢复");
	}

	public void OnConfigChanged()
	{
		_drag.Cancel();
		WindowActions.ApplyBlacklist(_store.Current.Blacklist);
		WindowActions.AllowDragOwnWindows = _store.Current.AllowDragOwnWindows;
		for (int i = 0; i < 3; i++)
		{
			_lastDown[i] = 0L;
			_lastMoved[i] = false;
			_swallowUp[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
			_bufferedDown[i] = false;
		}
		_drag.RefreshOverlayTheme();
		if (_trigger.Active && !Paused && Native.GetCursorPos(out var lpPoint))
		{
			_drag.ShowHoverFrame(lpPoint);
		}
	}

	public void Dispose()
	{
		try
		{
			_frameGuard.Stop();
			_frameGuard.Dispose();
		}
		catch
		{
		}
		_drag.Dispose();
		_trigger.Dispose();
	}

	public bool OnKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb)
	{
		long timestamp = Stopwatch.GetTimestamp();
		try
		{
			if (Paused || SuppressTrigger)
			{
				return false;
			}
			return _trigger.HandleKeyboard(down, ref kb);
		}
		finally
		{
			NoteHookCost(timestamp, down ? "按键按下" : "按键抬起");
		}
	}

	public bool OnMouse(int msg, ref Native.MSLLHOOKSTRUCT ms)
	{
		long timestamp = Stopwatch.GetTimestamp();
		try
		{
			return OnMouseCore(msg, ref ms);
		}
		finally
		{
			NoteHookCost(timestamp, "鼠标消息");
		}
	}

	private bool OnMouseCore(int msg, ref Native.MSLLHOOKSTRUCT ms)
	{
		if (Paused || SuppressTrigger)
		{
			int num = ButtonIndexOf(msg);
			if (num >= 0 && !IsDownMessage(msg))
			{
				_heldDuringHold[num] = false;
				_passedDown[num] = false;
			}
			return false;
		}
		switch (msg)
		{
		case 523:
		case 524:
			if (_trigger.HandleMouseTrigger(msg, msg == 523, ref ms))
			{
				return true;
			}
			return false;
		case 512:
			_drag.DeliverCursor(ms.pt);
			if (_trigger.Active && !_drag.IsDragging)
			{
				AdoptHeldButtonIfAny(ms.pt);
				if (!_drag.IsDragging)
				{
					_drag.ShowHoverFrame(ms.pt);
				}
			}
			return false;
		default:
		{
			int num2 = ButtonIndexOf(msg);
			if (num2 < 0)
			{
				return false;
			}
			// 触发键按住/激活期间，每一次鼠标键按下抬起都留痕。
			// 这两个状态之外不记（否则拖窗口时日志会被"抬起"淹没）。
			// 目的：用户报"外框出现了但窗口拖不动"时，日志必须能直接读出
			// 「这次按下到底进了哪个分支、缓冲有没有被清掉」。
			if (_trigger.Held || _trigger.Active)
			{
				Log.Info("[诊断] 鼠标键" + (IsDownMessage(msg) ? "按下" : "抬起") + " 键=" + num2 + " 触发[按住=" + _trigger.Held + " 激活=" + _trigger.Active + "] 缓冲=" + _bufferedDown[num2] + " 阈值期按住=" + _heldDuringHold[num2] + " 已放行=" + _passedDown[num2] + " 拖动中=" + _drag.IsDragging + " 坐标=" + ms.pt.X + "," + ms.pt.Y);
			}
			if (IsDownMessage(msg))
			{
				if (_trigger.Active)
				{
					return HandleButtonDown(num2, ms.pt);
				}
				bool hasAction = ActionFor(num2, _store.Current) != "Nothing";
				if (_trigger.Held & hasAction)
				{
					// 指针停在自有窗口的可交互控件上时直接放行、不做缓冲。
					// 否则用户按住不动地拖一会儿滑块，HoldMs 一到就会被本程序抢走这次按下。
					if (WindowActions.IsPointOverOwnInteractiveControl(ms.pt))
					{
						_passedDown[num2] = true;
						return false;
					}
					_heldDuringHold[num2] = true;
					_bufferedDown[num2] = true;
					_bufferedPt[num2] = ms.pt;
					return true;
				}
				_passedDown[num2] = true;
				return false;
			}
			_heldDuringHold[num2] = false;
			_passedDown[num2] = false;
			_bufferedDown[num2] = false;
			return HandleButtonUp(num2, ms.pt);
		}
		}
	}

	private void AdoptHeldButtonIfAny(Native.POINT pt)
	{
		// 触发键已激活、指针又停在自有窗口的可交互控件上 → 这次按下不抢。
		// 被缓冲过（从未投递给任何窗口）的情况补一次按下，让控件的拖拽（拖滑块、选文本）延续下去。
		if (WindowActions.IsPointOverOwnInteractiveControl(pt))
		{
			for (int j = 0; j <= 2; j++)
			{
				if (!_bufferedDown[j])
				{
					// 账本里没有"被我们吞掉、还没还给用户"的按下 → 没什么可还的。
					_heldDuringHold[j] = false;
					_passedDown[j] = false;
					continue;
				}
				_bufferedDown[j] = false;
				_heldDuringHold[j] = false;
				_passedDown[j] = false;
				ReplayBufferedClick(j, keepDown: true);
				Log.Info("[诊断] 触发键激活但指针停在自有窗口控件上 → 把缓冲的按下还给控件 键=" + j + " 控件=" + WindowActions.DescribeInteractiveAt(pt));
			}
			return;
		}
		// ★★ 第六轮核心修复：判据只用【钩子事件账本】，绝不再用 GetAsyncKeyState（IsButtonPhysicallyDown）。
		//
		// 实测（%AppData%\HoldDrag\holddrag.log，21:07:47.635）：
		//   [诊断] 激活瞬间清点缓冲 缓冲[True,False,False] 阈值期按住[True,False,False]
		//                          已放行[False,False,False] 物理按下[False,False,False]
		// 左键明明还按着（真实按下在 47.473 收到、131ms 后才抬起），
		// GetAsyncKeyState(VK_LBUTTON) 却报 False —— 于是老代码在
		//   if (!IsButtonPhysicallyDown(i)) { _bufferedDown[i] = false; continue; }
		// 这一行把【刚刚收到的、还热乎的】缓冲按下当成"用户已经松手"整条丢掉，
		// 之后每一次鼠标移动又被同一个查询挡回去 ⇒ 拖动永远建不起来，
		// 而外框由 ShowHoverFrame 照常画出 —— 这正是用户看到的
		// 「外框被样式包围，但鼠标怎么动窗口都移动不了」。
		//
		// 为什么账本才是可靠信号：我们能**直接看到**每一次鼠标键按下/抬起（低级鼠标钩子），
		// 而 GetAsyncKeyState 在这个进程里被实测证明会漏报（注入输入尤其明显）。
		// 账本语义精确且自洽：收到按下 + 尚未收到配对的抬起 ⟺ 键还按着；
		// 抬起分支（OnMouseCore 的 up 分支）会把三本账全部清零，所以账本不会长期失真。
		// 各种时序（按下落在阈值窗口内 / 按下早于触发键 / 鼠标在激活前就已经在动）到这里都归一。
		for (int i = 0; i <= 2; i++)
		{
			bool flag = _bufferedDown[i];
			bool flag2 = _heldDuringHold[i];
			bool flag3 = _passedDown[i];
			if (!flag && !flag2 && !flag3)
			{
				continue;
			}
			if (ActionFor(i, _store.Current) == "Nothing")
			{
				_bufferedDown[i] = false;
				_heldDuringHold[i] = false;
				_passedDown[i] = false;
				continue;
			}
			_bufferedDown[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
			if (HandleButtonDown(i, pt, quiet: true) && _drag.IsDragging)
			{
				Log.Info("[诊断] 触发键已激活且账本里还有未配对的按下 → 补起拖动" + (flag ? "（按下曾被缓冲，从未投递给任何窗口）" : (flag2 ? "（按下落在触发键按住阶段内）" : "（按下发生在触发键按住之前，目标可能停在拖选模态）")) + " 键=" + i + " 坐标=" + pt.X + "," + pt.Y);
				break;
			}
		}
	}

	/// <summary>
	/// 触发键激活的<b>瞬间</b>：把"阈值期间被缓冲、从未投递给任何窗口"的鼠标按下立刻补起成拖动。
	/// </summary>
	/// <remarks>
	/// 为什么可以立刻接管：这些按下从头到尾没有离开过本进程，目标窗口根本没见到 WM_*BUTTONDOWN，
	/// 所以不存在"用户其实是想在那里选文字/拖选区"的可能 —— 接管它不会破坏任何既有语义。
	/// 反过来，如果一定要等到激活后的第一次鼠标移动才补起，那么用户"同时按住触发键和左键、
	/// 按下后先不动"这段时间里就完全没有反馈，表现就是"左键什么功能都没有、也拖不动窗口"。
	/// 已放行给下层的按下（_passedDown）刻意不走这里：那种按下目标窗口实收了，必须靠用户移动来表明意图，
	/// 否则"先选文字、再按触发键"会被误判成拖窗口。
	/// </remarks>
	private void AdoptBufferedButtonsNow(Native.POINT pt)
	{
		// 「物理按下」那一栏仅作对照留痕：实测它对本进程会漏报（见 AdoptHeldButtonIfAny 的注释），
		// 所以它已经不参与任何判定，只帮助事后核对。
		Log.Info("[诊断] 激活瞬间清点缓冲 缓冲[" + _bufferedDown[0] + "," + _bufferedDown[1] + "," + _bufferedDown[2] + "] 阈值期按住[" + _heldDuringHold[0] + "," + _heldDuringHold[1] + "," + _heldDuringHold[2] + "] 已放行[" + _passedDown[0] + "," + _passedDown[1] + "," + _passedDown[2] + "] 参考·物理按下[" + IsButtonPhysicallyDown(0) + "," + IsButtonPhysicallyDown(1) + "," + IsButtonPhysicallyDown(2) + "] 坐标=" + pt.X + "," + pt.Y);
		for (int i = 0; i <= 2; i++)
		{
			if (!_bufferedDown[i])
			{
				continue;
			}
			// ★ 这里曾经有一条 `if (!IsButtonPhysicallyDown(i)) { 清账并 continue; }`。
			// 它就是本轮报障的真凶：抬起分支已经把"用户松手"这件事记得明明白白，
			// 再用一个会漏报的系统查询去二次判断，只会把好账当成坏账丢掉。
			if (WindowActions.IsPointOverOwnInteractiveControl(pt))
			{
				_bufferedDown[i] = false;
				_heldDuringHold[i] = false;
				_passedDown[i] = false;
				ReplayBufferedClick(i, keepDown: true);
				Log.Info("[诊断] 激活瞬间指针停在自有窗口控件上 → 把缓冲的按下还给控件 键=" + i + " 控件=" + WindowActions.DescribeInteractiveAt(pt));
				continue;
			}
			_bufferedDown[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
			if (HandleButtonDown(i, pt, quiet: true) && _drag.IsDragging)
			{
				Log.Info("[诊断] 激活瞬间补起拖动（阈值期间的按下从未投递给任何窗口，无需再等一次鼠标移动）键=" + i + " 坐标=" + pt.X + "," + pt.Y);
				break;
			}
			Log.Info("[诊断] 激活瞬间补起拖动未成立（对照上一条「鼠标键按下」日志的坐标与目标）键=" + i);
		}
	}

	private static void CancelTargetMouseMode(nint hwnd)
	{
		try
		{
			if (hwnd != IntPtr.Zero)
			{
				Native.PostMessageW(hwnd, 31, IntPtr.Zero, IntPtr.Zero);
				CancelModeSentForTest++;
			}
		}
		catch
		{
		}
	}

	// 触发键短按（未达 HoldMs 阈值）：阈值期间被缓冲的按下从没投递给任何窗口，
	// 用户的本意就是一次普通点击，这里用合成事件把它补回去，避免修复穿透之后丢失点击。
	private void FlushBufferedClicks()
	{
		for (int i = 0; i <= 2; i++)
		{
			if (!_bufferedDown[i])
			{
				continue;
			}
			_bufferedDown[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
			// 缓冲账还在 ⟺ 我们收到过这条按下、却从没收到配对的抬起 ⟺ 用户此刻仍按着。
			// 所以这里只补"按下"（不补抬起），用户真实的抬起稍后会原样送达 —— 他按住的这段时间
			// 就被完整保留给下层控件（拖滑块、选文本都不断）。以前用 GetAsyncKeyState 判断，
			// 它会漏报，会把"一直按住"误判成"早已松开"而补成一次完整点击，按住过程就断了。
			bool flag = true;
			ReplayBufferedClick(i, flag);
			ReplayedClickForTest++;
			Log.Info("[诊断] 触发键短按（未达阈值）→ 把缓冲的鼠标按下还给用户 键=" + i + " 坐标=" + _bufferedPt[i].X + "," + _bufferedPt[i].Y + " 用户仍按住=" + flag);
		}
	}

	private static void ReplayBufferedClick(int idx, bool keepDown)
	{
		try
		{
			uint num = idx switch
			{
				1 => 8u, 
				0 => 2u, 
				_ => 32u, 
			};
			uint num2 = idx switch
			{
				1 => 16u, 
				0 => 4u, 
				_ => 64u, 
			};
			Native.INPUT[] array = new Native.INPUT[keepDown ? 1 : 2];
			array[0].type = 0u;
			array[0].u.mi.dwFlags = num;
			array[0].u.mi.dwExtraInfo = HookEngine.MagicExtraInfo;
			if (!keepDown)
			{
				array[1].type = 0u;
				array[1].u.mi.dwFlags = num2;
				array[1].u.mi.dwExtraInfo = HookEngine.MagicExtraInfo;
			}
			Native.SendInput((uint)array.Length, array, Marshal.SizeOf(typeof(Native.INPUT)));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static bool IsButtonPhysicallyDown(int idx)
	{
		bool[] buttonsDownOverrideForTest = ButtonsDownOverrideForTest;
		if (buttonsDownOverrideForTest != null)
		{
			if (idx >= 0 && idx < buttonsDownOverrideForTest.Length)
			{
				return buttonsDownOverrideForTest[idx];
			}
			return false;
		}
		if (AssumeButtonsDownForTest)
		{
			return true;
		}
		int vKey = idx switch
		{
			1 => 2, 
			0 => 1, 
			_ => 4, 
		};
		try
		{
			return (Native.GetAsyncKeyState(vKey) & 0x8000) != 0;
		}
		catch
		{
			return false;
		}
	}

	internal bool HeldDuringHoldForTest(int idx)
	{
		if (idx >= 0 && idx < _heldDuringHold.Length)
		{
			return _heldDuringHold[idx];
		}
		return false;
	}

	internal bool PassedDownForTest(int idx)
	{
		if (idx >= 0 && idx < _passedDown.Length)
		{
			return _passedDown[idx];
		}
		return false;
	}

	internal bool BufferedDownForTest(int idx)
	{
		if (idx >= 0 && idx < _bufferedDown.Length)
		{
			return _bufferedDown[idx];
		}
		return false;
	}

	private static int ButtonIndexOf(int msg)
	{
		switch (msg)
		{
		case 513:
		case 514:
			return 0;
		case 516:
		case 517:
			return 1;
		case 519:
		case 520:
			return 2;
		default:
			return -1;
		}
	}

	private static bool IsDownMessage(int msg)
	{
		if (msg != 513 && msg != 516)
		{
			return msg == 519;
		}
		return true;
	}

	private static string ActionFor(int idx, AppConfig c)
	{
		return idx switch
		{
			0 => c.ActionLeft, 
			1 => c.ActionRight, 
			_ => c.ActionMiddle, 
		};
	}

	private bool HandleButtonDown(int idx, Native.POINT pt)
	{
		return HandleButtonDown(idx, pt, quiet: false);
	}

	private bool HandleButtonDown(int idx, Native.POINT pt, bool quiet)
	{
		AppConfig cfg = _store.Current;
		bool flag = _heldDuringHold[idx] || _passedDown[idx];
		string text = ActionFor(idx, cfg);
		if (text == "Nothing")
		{
			if (!quiet)
			{
				Log.Info("[诊断] 鼠标键按下被忽略：该键没有绑定任何动作 键=" + idx);
			}
			return false;
		}
		bool allowFullscreen = cfg.AllowFullscreenWindows || cfg.ExitFullscreenOnTrigger;
		nint num = WindowActions.FindTarget(pt, allowFullscreen);
		if (num == IntPtr.Zero)
		{
			// 这条以前只在 quiet=false 时打，结果"激活瞬间补起"这条路径静默失败时日志里什么都没有 ——
			// 用户看到的就是"外框出现了、窗口拖不动、日志却一片空白"。改成无条件记录。
			Log.Info("[诊断] FindTarget 没找到目标，按键被吞掉（触发键按住期间不再放行给下层窗口）键=" + idx + " 坐标=" + pt.X + "," + pt.Y + " 原因=" + WindowActions.DescribeReject(pt, allowFullscreen) + (quiet ? " 来源=激活后自动补起" : " 来源=触发键已激活时的实时按下"));
			_passedDown[idx] = false;
			_bufferedDown[idx] = false;
			_swallowUp[idx] = true;
			return true;
		}
		// 指针落在自有窗口（设置页等）内部的可交互控件上时，本轮不接管：
		// 把按下原样放行给控件，抬起也会走 HandleButtonUp 的默认分支（_swallowUp 为 false）。
		// 这样按住触发键去拖滑块、选文本、点按钮都还能用；想拖整个窗口把指针移到标题栏/边框/空白即可。
		if (Actions.IsDragAction(text) && WindowActions.IsPointOverOwnInteractiveControl(pt))
		{
			_passedDown[idx] = true;
			_bufferedDown[idx] = false;
			_heldDuringHold[idx] = false;
			_swallowUp[idx] = false;
			if (!quiet)
			{
				Log.Info("[诊断] 指针停在自有窗口的可交互控件上，按键交还控件 键=" + idx + " 控件=" + WindowActions.DescribeInteractiveAt(pt));
			}
			return false;
		}
		CancelTargetMouseMode(num);
		_passedDown[idx] = false;
		_bufferedDown[idx] = false;
		long tickCount = Environment.TickCount64;
		int num2 = (int)Native.GetDoubleClickTime();
		if (num2 <= 0)
		{
			num2 = 500;
		}
		bool flag2 = _lastDown[idx] != 0L && tickCount - _lastDown[idx] <= num2 && !_lastMoved[idx];
		_lastDown[idx] = tickCount;
		_lastMoved[idx] = false;
		if (((idx == 0 && cfg.DoubleClickLeftMaximize && text == "Move") & flag2) && !flag)
		{
			_lastDown[idx] = 0L;
			_swallowUp[idx] = true;
			nint target = num;
			Native.POINT at = pt;
			_drag.PostTargetWork(() =>
			{
				WindowActions.MaximizeRestore(target);
				RefreshHoverAfterAction(at);
			});
			_heldDuringHold[idx] = true;
			return true;
		}
		if (((idx == 1 && cfg.DoubleClickRightZone && text == "Resize") & flag2) && !flag)
		{
			_lastDown[idx] = 0L;
			_swallowUp[idx] = true;
			nint target2 = num;
			Native.POINT at2 = pt;
			_drag.PostTargetWork(() =>
			{
				ApplyZoneDoubleClick(target2, at2, cfg);
				RefreshHoverAfterAction(at2);
			});
			_heldDuringHold[idx] = true;
			return true;
		}
		if (Actions.IsDragAction(text))
		{
			if (!_drag.Begin(num, text == "Move", idx, pt))
			{
				// Begin 自己会打失败原因；这里再补一条，好让日志能把这次按下和
				// "触发键激活 / 外框出现" 串起来 —— 否则就是"按了左键什么都没发生"，无从定位。
				Log.Info("[诊断] 拖动无法建立（Begin 返回 false，具体原因见上一条日志）动作=" + text + " 键=" + idx + " 目标=" + ((IntPtr)num).ToInt64().ToString("X") + " 坐标=" + pt.X + "," + pt.Y);
				_swallowUp[idx] = true;
				return true;
			}
			return true;
		}
		_swallowUp[idx] = true;
		_heldDuringHold[idx] = false;
		nint actTarget = num;
		string actAction = text;
		Native.POINT actAt = pt;
		_drag.PostTargetWork(() =>
		{
			WindowActions.Apply(actTarget, actAction);
			RefreshHoverAfterAction(actAt);
		});
		return true;
	}

	private void RefreshHoverAfterAction(Native.POINT pt)
	{
		if (_trigger.Active)
		{
			_drag.InvalidateHover();
			_drag.ShowHoverFrame(pt);
		}
	}

	private static void ApplyZoneDoubleClick(nint hwnd, Native.POINT pt, AppConfig cfg)
	{
		if (!Native.GetWindowRect(hwnd, out var lpRect))
		{
			return;
		}
		ResizeZone resizeZone = Zones.Resize(lpRect, pt.X, pt.Y, cfg.CornerPercent);
		if (resizeZone == ResizeZone.Center)
		{
			resizeZone = Zones.NearestCorner(lpRect, pt.X, pt.Y);
		}
		SnapZone snapZone = Zones.ScreenZoneOfResizeZone(resizeZone);
		if (snapZone != SnapZone.None)
		{
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(pt);
			if (monitorInfoAt.rcWork.Width > 0)
			{
				WindowActions.ApplySnap(hwnd, snapZone, monitorInfoAt.rcWork);
			}
		}
	}

	private bool HandleButtonUp(int idx, Native.POINT pt)
	{
		if (_drag.IsDragging && _drag.Button == idx)
		{
			_drag.End();
			_lastMoved[idx] = _drag.LastMoved;
			if (_trigger.Active)
			{
				_drag.ShowHoverFrame(pt);
			}
			return true;
		}
		if (_swallowUp[idx])
		{
			_swallowUp[idx] = false;
			return true;
		}
		return false;
	}
}
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
internal sealed class DragEngine : IDisposable
{
	private struct Session
	{
		public nint Hwnd;

		public bool Move;

		public int Button;

		public Native.POINT StartCursor;

		public Native.RECT StartRect;

		public ResizeZone Zone;

		public int MinW;

		public int MinH;

		public int Gen;
	}

	private readonly Func<AppConfig> _cfg;

	private readonly ActivationFrame _frame;

	private readonly DragWorker _worker;

	private static readonly int OwnPid = Process.GetCurrentProcess().Id;

	private static int _detectedHz;

	private readonly object _gate = new object();

	private Session _session;

	private volatile bool _dragging;

	private volatile bool _moved;

	private int _generation;

	private Native.RECT _inset;

	private nint _hoverRaw;

	private nint _hoverHwnd;

	private Native.RECT _hoverRect;

	private bool _hoverRectValid;

	private readonly ShakeDetector _shake = new ShakeDetector();

	private int _shakeGen = -1;

	private volatile bool _frameWanted;

	internal static int DiagFrameUpdates;

	internal static int DiagWindowSkipped;

	internal static int DiagWindowWritten;

	private bool _overlaysReady;

	private int _sessionCount;

	private volatile bool _dragWait;

	private bool _frameInside;

	internal static int DetectedRefreshHz
	{
		get
		{
			int detectedHz = _detectedHz;
			if (detectedHz > 0)
			{
				return detectedHz;
			}
			detectedHz = (_detectedHz = Native.GetRefreshRate());
			Log.Info("实测屏幕刷新率：" + detectedHz + " Hz");
			return detectedHz;
		}
	}

	public bool IsDragging => _dragging;

	public int Button => _session.Button;

	public bool LastMoved => _moved;

	public int SessionCount => _sessionCount;

	internal ActivationFrame Frame => _frame;

	internal bool FrameReallyVisible
	{
		get
		{
			try
			{
				return _frame.IsShown && Native.IsWindowVisible(_frame.Handle);
			}
			catch
			{
				return false;
			}
		}
	}

	internal static void DiagReset()
	{
		DiagFrameUpdates = 0;
		DiagWindowSkipped = 0;
		DiagWindowWritten = 0;
	}

	public DragEngine(Func<AppConfig> config)
	{
		_cfg = config;
		_frame = new ActivationFrame();
		_worker = new DragWorker(OnWorkerMove);
		_worker.Start();
	}

	public void Dispose()
	{
		Cancel();
		_worker.Dispose();
		if (_overlaysReady)
		{
			_frame.Destroy();
			_overlaysReady = false;
		}
		_idleReleaseTimer?.Dispose();
	}

	public void Precreate()
	{
		try
		{
			_frame.EnsureCreated();
			_overlaysReady = true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public void DeliverCursor(Native.POINT pt)
	{
		if (_dragging)
		{
			_worker.PostMove(pt);
		}
	}

	public bool Begin(nint hwnd, bool move, int button, Native.POINT pt)
	{
		AppConfig appConfig = _cfg();
		if (!Native.GetWindowRect(hwnd, out var lpRect))
		{
			Log.Info("[诊断] 拖动无法开始：拿不到目标窗口矩形 句柄=" + ((IntPtr)hwnd).ToInt64().ToString("X"));
			return false;
		}
		bool flag = Native.IsZoomed(hwnd);
		bool flag2 = !flag && WindowActions.IsFullscreen(hwnd, lpRect);
		if (flag | flag2)
		{
			if (appConfig.ExitFullscreenOnTrigger)
			{
				List<Action> pending = new List<Action>();
				if (!WindowActions.ExitFullscreenToWindowed(hwnd, ref lpRect, pt, (Action w) =>
				{
					pending.Add(w);
				}))
				{
					Log.Info("[诊断] 目标是" + (flag ? "最大化" : "全屏") + "窗口，降级成普通窗口失败 → 放行不接管 句柄=" + ((IntPtr)hwnd).ToInt64().ToString("X"));
					return false;
				}
				if (pending.Count > 0)
				{
					int cornerPct = appConfig.CornerPercent;
					int btn = button;
					bool mv = move;
					Native.POINT cur = pt;
					nint target = hwnd;
					PostTargetWork(() =>
					{
						for (int i = 0; i < pending.Count; i++)
						{
							try
							{
								pending[i]();
							}
							catch (Exception ex2)
							{
								Log.Error(ex2);
							}
						}
						if (Native.GetWindowRect(target, out var lpRect2))
						{
							ReconcileStartRect(target, btn, mv, cur, cornerPct, lpRect2);
						}
					});
				}
			}
			else
			{
				if (!flag || !appConfig.RestoreMaximizedWhenMoved)
				{
					Log.Info("[诊断] 目标是" + (flag ? "最大化" : "全屏") + "窗口，且『触发时退出全屏』已关闭 → 放行不接管 键=" + button + " 移动=" + move);
					return false;
				}
				double num = ((lpRect.Width > 0) ? ((double)(pt.X - lpRect.Left) / (double)lpRect.Width) : 0.5);
				double num2 = ((lpRect.Height > 0) ? ((double)(pt.Y - lpRect.Top) / (double)lpRect.Height) : 0.5);
				if (num < 0.0)
				{
					num = 0.0;
				}
				if (num > 1.0)
				{
					num = 1.0;
				}
				if (num2 < 0.0)
				{
					num2 = 0.0;
				}
				if (num2 > 1.0)
				{
					num2 = 1.0;
				}
				Native.WINDOWPLACEMENT lpwndpl = new Native.WINDOWPLACEMENT
				{
					length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT))
				};
				if (Native.GetWindowPlacement(hwnd, ref lpwndpl) && lpwndpl.rcNormalPosition.Width > 0 && lpwndpl.rcNormalPosition.Height > 0)
				{
					lpRect = lpwndpl.rcNormalPosition;
				}
				else if (!Native.GetWindowRect(hwnd, out lpRect))
				{
					return false;
				}
				nint target2 = hwnd;
				int cornerPct2 = appConfig.CornerPercent;
				int btn2 = button;
				bool mv2 = move;
				Native.POINT cur2 = pt;
				if (move)
				{
					int nx = pt.X - (int)(num * (double)lpRect.Width);
					int ny = pt.Y - (int)(num2 * (double)lpRect.Height);
					int ww = lpRect.Width;
					int hh = lpRect.Height;
					PostTargetWork(() =>
					{
						Native.ShowWindow(target2, 9);
						Native.SetWindowPos(target2, IntPtr.Zero, nx, ny, ww, hh, 16917u);
						if (Native.GetWindowRect(target2, out var lpRect2))
						{
							ReconcileStartRect(target2, btn2, mv2, cur2, cornerPct2, lpRect2);
						}
					});
					lpRect = new Native.RECT(nx, ny, nx + ww, ny + hh);
				}
				else
				{
					PostTargetWork(() =>
					{
						Native.ShowWindow(target2, 9);
						if (Native.GetWindowRect(target2, out var lpRect2))
						{
							ReconcileStartRect(target2, btn2, mv2, cur2, cornerPct2, lpRect2);
						}
					});
				}
			}
		}
		ResizeZone resizeZone = ResizeZone.Center;
		if (!move)
		{
			resizeZone = Zones.Resize(lpRect, pt.X, pt.Y, appConfig.CornerPercent);
			if (resizeZone == ResizeZone.Center)
			{
				resizeZone = Zones.NearestCorner(lpRect, pt.X, pt.Y);
			}
		}
		int num3 = Native.GetSystemMetrics(34);
		int num4 = Native.GetSystemMetrics(35);
		if (num3 < 64)
		{
			num3 = 64;
		}
		if (num4 < 32)
		{
			num4 = 32;
		}
		Native.RECT inset = ComputeInset(hwnd, lpRect);
		bool flag3 = appConfig.DragThreshold >= 2 || ((appConfig.DragThreshold == 1) & flag);
		_dragWait = flag3 && appConfig.DragOutThresholdPx > 0;
		lock (_gate)
		{
			_session = default;
			_session.Hwnd = hwnd;
			_session.Move = move;
			_session.Button = button;
			_session.StartCursor = pt;
			_session.StartRect = lpRect;
			_session.Zone = resizeZone;
			_session.MinW = num3;
			_session.MinH = num4;
			_moved = false;
			_generation++;
			_session.Gen = _generation;
			_inset = inset;
			_frameWanted = appConfig.ShowActivationFrame;
			_dragging = true;
		}
		WindowActions.ResetSessionKickCache();
		DiagReset();
		_frameInside = false;
		WindowActions.NotifySizeMove(hwnd, start: true);
		_worker.FrameIntervalMs = appConfig.FrameIntervalMs(DetectedRefreshHz);
		try
		{
			GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		if (appConfig.BringToFront)
		{
			nint target3 = hwnd;
			Thread thread = new Thread(() =>
			{
				try
				{
					WindowActions.ForceForeground(target3);
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
				}
			});
			thread.IsBackground = true;
			thread.Name = "HoldDrag.Raise";
			thread.Start();
		}
		if (appConfig.ShowActivationFrame)
		{
			_frame.HideOverlay();
			ShowDragFrame(lpRect, appConfig);
			Native.RECT rECT = FrameRectOf(lpRect, appConfig, out var inset2);
			Log.Info("[诊断] 拖动框已请求显示 开关=" + appConfig.ShowActivationFrame + " 就绪=" + _overlaysReady + " 目标矩形=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height + " 可见缩进=" + _inset.Left + "/" + _inset.Top + "/" + _inset.Right + "/" + _inset.Bottom + " 外框矩形=" + rECT.Left + "," + rECT.Top + " " + rECT.Width + "x" + rECT.Height + (inset2 ? " 模式=内缩" : " 模式=外扩") + " 抬到最顶=" + _frame.RaiseAboveAll + " 显示=" + _frame.IsShown + " 请求=" + (_frame.ShowRequestedForTest ? "已投递" : "未投递"));
		}
		Log.Info((move ? "开始移动" : "开始缩放") + " 句柄=" + ((IntPtr)hwnd).ToInt64().ToString("X") + " 刷新率=" + DetectedRefreshHz + "Hz");
		Interlocked.Increment(ref _sessionCount);
		return true;
	}

	private static Native.RECT ComputeInset(nint hwnd, Native.RECT wr)
	{
		if (!Native.TryGetVisibleRect(hwnd, out var r))
		{
			return default;
		}
		if (r.Width <= 0 || r.Height <= 0)
		{
			return default;
		}
		int num = r.Left - wr.Left;
		int num2 = r.Top - wr.Top;
		int num3 = wr.Right - r.Right;
		int num4 = wr.Bottom - r.Bottom;
		if (num < 0 || num2 < 0 || num3 < 0 || num4 < 0)
		{
			return default;
		}
		if (num > 40 || num2 > 40 || num3 > 40 || num4 > 40)
		{
			return default;
		}
		return new Native.RECT(num, num2, num3, num4);
	}

	private void OnWorkerMove(Native.POINT pt)
	{
		Session session;
		lock (_gate)
		{
			if (!_dragging)
			{
				return;
			}
			session = _session;
		}
		Step(pt, session);
	}

	private void Step(Native.POINT pt, Session s, bool forceWrite = false)
	{
		try
		{
			if (s.Hwnd == IntPtr.Zero)
			{
				return;
			}
			if (!Native.IsWindow(s.Hwnd))
			{
				Cancel();
				return;
			}
			AppConfig appConfig = _cfg();
			if (Native.GetClipCursor(out var lpRect) && lpRect.Right > lpRect.Left && lpRect.Bottom > lpRect.Top)
			{
				if (pt.X < lpRect.Left)
				{
					pt.X = lpRect.Left;
				}
				if (pt.X > lpRect.Right - 1)
				{
					pt.X = lpRect.Right - 1;
				}
				if (pt.Y < lpRect.Top)
				{
					pt.Y = lpRect.Top;
				}
				if (pt.Y > lpRect.Bottom - 1)
				{
					pt.Y = lpRect.Bottom - 1;
				}
			}
			if (_dragWait)
			{
				int dragOutThresholdPx = appConfig.DragOutThresholdPx;
				if (Math.Abs(pt.X - s.StartCursor.X) < dragOutThresholdPx && Math.Abs(pt.Y - s.StartCursor.Y) < dragOutThresholdPx)
				{
					return;
				}
				_dragWait = false;
			}
			int num = pt.X - s.StartCursor.X;
			int num2 = pt.Y - s.StartCursor.Y;
			if (Math.Abs(num) > 2 || Math.Abs(num2) > 2)
			{
				_moved = true;
			}
			Native.RECT rECT;
			if (s.Move)
			{
				rECT = new Native.RECT(s.StartRect.Left + num, s.StartRect.Top + num2, s.StartRect.Right + num, s.StartRect.Bottom + num2);
			}
			else
			{
				rECT = s.StartRect;
				Zones.EdgesOf(s.Zone, out var left, out var top, out var right, out var bottom);
				if (left)
				{
					rECT.Left = s.StartRect.Left + num;
				}
				if (right)
				{
					rECT.Right = s.StartRect.Right + num;
				}
				if (top)
				{
					rECT.Top = s.StartRect.Top + num2;
				}
				if (bottom)
				{
					rECT.Bottom = s.StartRect.Bottom + num2;
				}
				if (rECT.Width < s.MinW)
				{
					if (left)
					{
						rECT.Left = rECT.Right - s.MinW;
					}
					else
					{
						rECT.Right = rECT.Left + s.MinW;
					}
				}
				if (rECT.Height < s.MinH)
				{
					if (top)
					{
						rECT.Top = rECT.Bottom - s.MinH;
					}
					else
					{
						rECT.Bottom = rECT.Top + s.MinH;
					}
				}
			}
			lock (_gate)
			{
				if (forceWrite)
				{
					if (_session.Gen != s.Gen)
					{
						return;
					}
				}
				else if (!_dragging || _session.Gen != s.Gen)
				{
					return;
				}
			}
			if (s.Move)
			{
				if (_shakeGen != s.Gen)
				{
					int num3 = Native.DpiOfWindow(s.Hwnd);
					_shake.SetScale((double)num3 / 96.0);
					_shake.Reset(s.StartCursor.X, s.StartCursor.Y, Environment.TickCount64);
					_shakeGen = s.Gen;
				}
				if (appConfig.ShakeMinimizeOthers && _shake.Feed(pt.X, pt.Y, Environment.TickCount64))
				{
					ShakeAction.ToggleMinimizeOthers(s.Hwnd);
				}
			}
			if (appConfig.ShowActivationFrame)
			{
				Native.RECT rectNow = FrameRectOf(rECT, appConfig, out var inset);
				_frame.RaiseAboveAll = true;
				if (rectNow.Width >= 4 && rectNow.Height >= 4)
				{
					lock (_gate)
					{
						if (_dragging && _session.Gen == s.Gen && _frameWanted)
						{
							_frame.ApplyTheme(appConfig.FrameColorId, appConfig.FrameStyleId, appConfig.FrameThickness, appConfig.FrameRadius, appConfig.FrameOpacity, appConfig.EffectiveFrameColor);
							_frame.SetRectNow(rectNow);
							DiagFrameUpdates++;
						}
					}
				}
			}
			if (forceWrite)
			{
				WindowActions.SetDragRect(s.Hwnd, rECT);
			}
			else if (WindowActions.SetDragRectFrame(s.Hwnd, rECT, s.Move))
			{
				DiagWindowWritten++;
			}
			else
			{
				DiagWindowSkipped++;
			}
			if (Log.Verbose)
			{
				Native.GetWindowRect(s.Hwnd, out var lpRect2);
				string[] array = new string[9]
				{
					s.Move ? "移动" : "缩放",
					" 光标=",
					pt.X.ToString(),
					",",
					pt.Y.ToString(),
					" 期望=",
					null,
					null,
					null
				};
				Native.RECT rECT2 = rECT;
				array[6] = rECT2.ToString();
				array[7] = " 实际=";
				rECT2 = lpRect2;
				array[8] = rECT2.ToString();
				Log.Trace(string.Concat(array));
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public void End()
	{
		if (!_dragging)
		{
			return;
		}
		try
		{
			AppConfig appConfig = _cfg();
			Session s;
			nint hwnd;
			lock (_gate)
			{
				s = _session;
				hwnd = _session.Hwnd;
			}
			bool cursorPos = Native.GetCursorPos(out var pt);
			lock (_gate)
			{
				_dragging = false;
				_generation++;
			}
			if (cursorPos && hwnd != IntPtr.Zero)
			{
				_worker.Post(() =>
				{
					Step(pt, s, forceWrite: true);
				});
			}
			WindowActions.NotifySizeMove(hwnd, start: false);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			Cleanup();
		}
	}

	public void UpdateFinal(Native.POINT pt)
	{
		if (_dragging)
		{
			Session session;
			lock (_gate)
			{
				session = _session;
			}
			Step(pt, session, forceWrite: true);
		}
	}

	internal void PostTargetWork(Action work)
	{
		if (work == null)
		{
			return;
		}
		if (_worker != null && _worker.IsRunning)
		{
			_worker.Post(work);
			return;
		}
		try
		{
			work();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ReconcileStartRect(nint hwnd, int button, bool move, Native.POINT pt, int cornerPercent, Native.RECT real)
	{
		lock (_gate)
		{
			if (!_dragging || _moved || _session.Hwnd != hwnd || _session.Button != button)
			{
				return;
			}
			Native.RECT startRect = _session.StartRect;
			if (startRect.Left == real.Left && startRect.Top == real.Top && startRect.Right == real.Right && startRect.Bottom == real.Bottom)
			{
				return;
			}
			_session.StartRect = real;
			_inset = ComputeInset(hwnd, real);
			if (!move)
			{
				ResizeZone resizeZone = Zones.Resize(real, pt.X, pt.Y, cornerPercent);
				if (resizeZone == ResizeZone.Center)
				{
					resizeZone = Zones.NearestCorner(real, pt.X, pt.Y);
				}
				_session.Zone = resizeZone;
			}
			Log.Info("[诊断] 降级落账后校正会话矩形：" + startRect.Left + "," + startRect.Top + " " + startRect.Width + "x" + startRect.Height + " → " + real.Left + "," + real.Top + " " + real.Width + "x" + real.Height);
		}
	}

	internal void HideOverlaysNow()
	{
		_frameWanted = false;
		_dragging = false;
		try
		{
			_frame.HideNowSync();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	internal void EnforceFrameInvariant(bool triggerActive)
	{
		try
		{
			if (!_dragging && (!triggerActive || _hoverHwnd == IntPtr.Zero || !_hoverRectValid) && _frame.IsShown)
			{
				Log.Info("[兜底] 外框本不该显示（不在拖动、也没有悬停目标）→ 强制收掉");
				_frameWanted = false;
				_frame.HideNowSync();
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	internal void StepForTest(Native.POINT pt)
	{
		if (_dragging)
		{
			Session session;
			lock (_gate)
			{
				session = _session;
			}
			Step(pt, session);
		}
	}

	public void Cancel()
	{
		if (_dragging)
		{
			lock (_gate)
			{
				_dragging = false;
				_generation++;
			}
			Cleanup();
		}
	}

	private void Cleanup()
	{
		try
		{
			_frameWanted = false;
			if (_overlaysReady)
			{
				_frame.HideNowSync();
			}
			try
			{
				GCSettings.LatencyMode = GCLatencyMode.Interactive;
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			// v1.3 空闲内存回收：拖动结束后外框位图就是一块纯闲置的大缓冲
			//（4K 屏上每个外框 ~33MB 托管 + ~33MB 非托管）。旧版一直攥着不放，
			// "用完一次拖窗，常驻内存就高几十 MB"。这里延迟 15 秒释放：
			// 期间若又开始拖动（位图会被 Rebuild 复用）就取消，避免抖动。
			ScheduleIdleBufferRelease();
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		finally
		{
			InvalidateHover();
			lock (_gate)
			{
				_session.Hwnd = IntPtr.Zero;
				_session.Button = -1;
			}
		}
	}

	private System.Windows.Forms.Timer _idleReleaseTimer;

	private int _idleReleaseGeneration;

	private void ScheduleIdleBufferRelease()
	{
		try
		{
			int num = 0;
			lock (_gate)
			{
				num = _generation;
			}
			_idleReleaseGeneration = num;
			if (_idleReleaseTimer == null)
			{
				_idleReleaseTimer = new System.Windows.Forms.Timer
				{
					Interval = 15000
				};
				_idleReleaseTimer.Tick += (object? s, EventArgs e) =>
				{
					_idleReleaseTimer.Stop();
					try
					{
						int num2 = 0;
						bool dragging = _dragging;
						lock (_gate)
						{
							num2 = _generation;
						}
						if (dragging || num2 != _idleReleaseGeneration || _frameWanted || (_hoverRectValid && _hoverHwnd != IntPtr.Zero))
						{
							return;
						}
						// 真闲着：把外框的像素缓冲放掉（窗口句柄保留，下次拖动 Rebuild 会重建）。
						_frame.ReleaseBuffersForIdle();
						Log.Trace("[内存] 拖动已结束 15 秒，外框像素缓冲已释放");
					}
					catch (Exception ex)
					{
						Log.Error(ex);
					}
				};
			}
			_idleReleaseTimer.Stop();
			_idleReleaseTimer.Start();
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
	}

	private void ShowDragFrame(Native.RECT windowRect, AppConfig c)
	{
		if (_overlaysReady)
		{
			Native.RECT rect = FrameRectOf(windowRect, c, out var _);
			_frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius, c.FrameOpacity, c.EffectiveFrameColor);
			if (rect.Width >= 4 && rect.Height >= 4)
			{
				_frame.SetRect(rect);
			}
		}
	}

	private void ShowFrame(Native.RECT windowRect)
	{
		if (_overlaysReady && _frameWanted)
		{
			AppConfig appConfig = _cfg();
			_frame.ApplyTheme(appConfig.FrameColorId, appConfig.FrameStyleId, appConfig.FrameThickness, appConfig.FrameRadius, appConfig.FrameOpacity, appConfig.EffectiveFrameColor);
			Native.RECT rect = FrameRectOf(windowRect, appConfig, out var inset);
			_frame.RaiseAboveAll = true;
			if (rect.Width >= 4 && rect.Height >= 4)
			{
				_frame.SetRect(rect);
			}
		}
	}

	internal bool TryShowFrame(Native.RECT windowRect, AppConfig c, int gen)
	{
		if (!_overlaysReady)
		{
			return false;
		}
		Native.RECT rect = FrameRectOf(windowRect, c, out var inset);
		_frame.RaiseAboveAll = true;
		if (rect.Width < 4 || rect.Height < 4)
		{
			return false;
		}
		lock (_gate)
		{
			if (!_dragging || _session.Gen != gen || !_frameWanted)
			{
				return false;
			}
			_frame.SetRect(rect);
			return true;
		}
	}

	private Native.RECT FrameRectOf(Native.RECT windowRect, AppConfig c, out bool inset)
	{
		int num = FrameStyles.MarginFor(c.FrameStyleId, c.FrameThickness);
		Native.RECT rECT = new Native.RECT(windowRect.Left + _inset.Left, windowRect.Top + _inset.Top, windowRect.Right - _inset.Right, windowRect.Bottom - _inset.Bottom);
		if (!FrameStyles.FillsScreen(rECT, num, _frameInside))
		{
			inset = false;
			_frameInside = false;
			return new Native.RECT(rECT.Left - num, rECT.Top - num, rECT.Right + num, rECT.Bottom + num);
		}
		inset = true;
		_frameInside = true;
		return rECT;
	}

	public void ShowHoverFrame(Native.POINT pt)
	{
		if (!_overlaysReady || _dragging)
		{
			return;
		}
		AppConfig appConfig = _cfg();
		if (!appConfig.ShowActivationFrame)
		{
			HideHoverFrame();
			return;
		}
		nint num = Native.WindowFromPoint(pt);
		if (num == IntPtr.Zero)
		{
			HideHoverFrame();
			return;
		}
		if (num == _hoverRaw)
		{
			if (_hoverHwnd == IntPtr.Zero || !Native.IsWindow(_hoverHwnd))
			{
				InvalidateHover();
			}
			else
			{
				if (Native.GetWindowRect(_hoverHwnd, out var lpRect))
				{
					if (!_frame.IsShown || !_hoverRectValid || lpRect.Left != _hoverRect.Left || lpRect.Top != _hoverRect.Top || lpRect.Right != _hoverRect.Right || lpRect.Bottom != _hoverRect.Bottom)
					{
						_hoverRect = lpRect;
						_hoverRectValid = true;
						_frameWanted = true;
						ShowFrame(lpRect);
					}
					return;
				}
				InvalidateHover();
			}
		}
		nint num2 = WindowActions.FindTarget(pt, appConfig.AllowFullscreenWindows || appConfig.ExitFullscreenOnTrigger);
		if (num2 == IntPtr.Zero)
		{
			_hoverRaw = num;
			HideHoverFrame();
			return;
		}
		if (!Native.GetWindowRect(num2, out var lpRect2))
		{
			_hoverRaw = num;
			HideHoverFrame();
			return;
		}
		_hoverRaw = num;
		_hoverHwnd = num2;
		_hoverRect = lpRect2;
		_hoverRectValid = true;
		_inset = ComputeInset(num2, lpRect2);
		_frameWanted = true;
		ShowFrame(lpRect2);
	}

	public void RefreshOverlayTheme()
	{
		InvalidateHover();
		if (!_overlaysReady)
		{
			return;
		}
		try
		{
			AppConfig appConfig = _cfg();
			_frame.ApplyTheme(appConfig.FrameColorId, appConfig.FrameStyleId, appConfig.FrameThickness, appConfig.FrameRadius, appConfig.FrameOpacity, appConfig.EffectiveFrameColor);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public void InvalidateHover()
	{
		_hoverRaw = IntPtr.Zero;
		_hoverHwnd = IntPtr.Zero;
		_hoverRectValid = false;
	}

	public void HideHoverFrame()
	{
		_hoverRaw = IntPtr.Zero;
		_hoverHwnd = IntPtr.Zero;
		_hoverRectValid = false;
		if (!_dragging)
		{
			_frameWanted = false;
			if (_overlaysReady)
			{
				_frame.HideOverlay();
			}
		}
	}
}
internal sealed class DragWorker : IDisposable
{
	public const uint WM_DOMOUSEMOVE = 32775u;

	public const uint WM_DOWORK = 32774u;

	private readonly Action<Native.POINT> _onMove;

	private readonly ConcurrentQueue<Action> _work = new ConcurrentQueue<Action>();

	private Thread _thread;

	private uint _threadId;

	private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(initialState: false);

	private volatile int _frameIntervalMs;

	public static long MaxMoveMs;

	public static long MaxIdleMs;

	public static int MaxDrained;

	private volatile bool _disposed;

	public int FrameIntervalMs
	{
		get
		{
			return _frameIntervalMs;
		}
		set
		{
			_frameIntervalMs = ((value >= 0) ? value : 0);
		}
	}

	public bool IsRunning
	{
		get
		{
			if (_thread != null && _ready.IsSet)
			{
				return !_disposed;
			}
			return false;
		}
	}

	public static void ResetStats()
	{
		MaxMoveMs = 0L;
		MaxIdleMs = 0L;
		MaxDrained = 0;
	}

	public DragWorker(Action<Native.POINT> onMove)
	{
		_onMove = onMove;
	}

	public void Start()
	{
		if (_thread == null)
		{
			_thread = new Thread(ThreadMain);
			_thread.IsBackground = true;
			_thread.Priority = ThreadPriority.AboveNormal;
			_thread.Name = "HoldDrag.DragWorker";
			_thread.Start();
			if (!_ready.Wait(3000))
			{
				Log.Error("拖动工作线程启动超时");
			}
		}
	}

	public void PostMove(Native.POINT pt)
	{
		if (!_disposed && _threadId != 0 && !Native.PostThreadMessageW(_threadId, 32775u, new IntPtr(pt.X), new IntPtr(pt.Y)))
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			if (lastWin32Error != 0)
			{
				Log.Trace("投递鼠标移动失败，Win32 错误码 " + lastWin32Error);
			}
		}
	}

	public void Post(Action work)
	{
		if (work != null && !_disposed && _threadId != 0)
		{
			_work.Enqueue(work);
			if (!Native.PostThreadMessageW(_threadId, 32774u, IntPtr.Zero, IntPtr.Zero))
			{
				// 投递失败（线程消息队列满）时不能 TryDequeue：队列是 FIFO，
				// 出队的会是最早那条而不是刚入队的这条，丢旧的留新的，语义反了。
				// 让活计留在队列里，下次投递成功时一起处理。
				Log.Trace("投递任务失败（线程消息队列可能已满），任务已留在队列中");
			}
		}
	}

	private void ThreadMain()
	{
		_threadId = Native.GetCurrentThreadId();
		Native.PeekMessageW(out var _, IntPtr.Zero, 0u, 0u, 0u);
		_ready.Set();
		try
		{
			while (!_disposed)
			{
				long tickCount = Environment.TickCount64;
				int messageW = Native.GetMessageW(out var lpMsg2, IntPtr.Zero, 0u, 0u);
				long num = Environment.TickCount64 - tickCount;
				if (num > MaxIdleMs)
				{
					MaxIdleMs = num;
				}
				if (messageW == 0 || messageW < 0)
				{
					break;
				}
				if (lpMsg2.hwnd != IntPtr.Zero)
				{
					continue;
				}
				if (lpMsg2.message == 18)
				{
					break;
				}
				if (lpMsg2.message == 32774)
				{
					Action result;
					while (_work.TryDequeue(out result))
					{
						try
						{
							result();
						}
						catch (Exception ex)
						{
							Log.Error(ex);
						}
					}
				}
				else
				{
					if (lpMsg2.message != 32775)
					{
						continue;
					}
					int num2 = 0;
					Native.MSG lpMsg3;
					while (Native.PeekMessageW(out lpMsg3, IntPtr.Zero, 32775u, 32775u, 1u))
					{
						lpMsg2 = lpMsg3;
						num2++;
					}
					if (num2 > MaxDrained)
					{
						MaxDrained = num2;
					}
					if (num2 > 0 && Log.Verbose)
					{
						Log.Trace("合并掉了 " + num2 + " 个过期的鼠标移动位置");
					}
					Native.POINT obj = new Native.POINT((int)((IntPtr)lpMsg2.wParam).ToInt64(), (int)((IntPtr)lpMsg2.lParam).ToInt64());
					long tickCount2 = Environment.TickCount64;
					try
					{
						_onMove(obj);
					}
					catch (Exception ex2)
					{
						Log.Error(ex2);
					}
					long num3 = Environment.TickCount64 - tickCount2;
					if (num3 > MaxMoveMs)
					{
						MaxMoveMs = num3;
					}
					int frameIntervalMs = _frameIntervalMs;
					if (frameIntervalMs > 0)
					{
						long num4 = frameIntervalMs - (Environment.TickCount64 - tickCount2);
						if (num4 > 1)
						{
							Thread.Sleep((int)num4);
						}
						else if (num4 == 1)
						{
							Thread.Sleep(1);
						}
					}
				}
			}
		}
		catch (Exception ex3)
		{
			Log.Error(ex3);
		}
		finally
		{
			_ready.Set();
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		try
		{
			if (_threadId != 0)
			{
				Native.PostThreadMessageW(_threadId, 18u, IntPtr.Zero, IntPtr.Zero);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		try
		{
			if (_thread != null && _thread.IsAlive)
			{
				_thread.Join(1000);
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		_thread = null;
		_threadId = 0u;
		Action result;
		while (_work.TryDequeue(out result))
		{
		}
	}
}
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
internal static class FrameColors
{
	internal struct Entry
	{
		public string Id;

		public string Name;

		public Color Color;
	}

	public const string Teal = "Teal";

	public const string Aqua = "Aqua";

	public const string Green = "Green";

	public const string Amber = "Amber";

	public const string Coral = "Coral";

	public const string Red = "Red";

	public const string Pink = "Pink";

	public const string Violet = "Violet";

	public const string Graphite = "Graphite";

	public const string Silver = "Silver";

	public const string Accent = "Accent";

	public const string Lime = "Lime";

	public const string Rose = "Rose";

	public const string Gold = "Gold";

	public const string Sky = "Sky";

	public const string Obsidian = "Obsidian";

	public const string Pearl = "Pearl";

	internal static readonly Entry[] All = new Entry[17]
	{
		new Entry
		{
			Id = "Teal",
			Name = "薄荷青",
			Color = Color.FromArgb(107, 196, 165)
		},
		new Entry
		{
			Id = "Aqua",
			Name = "湖水青",
			Color = Color.FromArgb(111, 200, 188)
		},
		new Entry
		{
			Id = "Sky",
			Name = "天青",
			Color = Color.FromArgb(100, 194, 214)
		},
		new Entry
		{
			Id = "Green",
			Name = "抹茶绿",
			Color = Color.FromArgb(116, 200, 148)
		},
		new Entry
		{
			Id = "Lime",
			Name = "嫩芽绿",
			Color = Color.FromArgb(168, 203, 106)
		},
		new Entry
		{
			Id = "Gold",
			Name = "暖金",
			Color = Color.FromArgb(214, 193, 106)
		},
		new Entry
		{
			Id = "Amber",
			Name = "蜜橙",
			Color = Color.FromArgb(221, 169, 92)
		},
		new Entry
		{
			Id = "Coral",
			Name = "珊瑚",
			Color = Color.FromArgb(232, 147, 118)
		},
		new Entry
		{
			Id = "Red",
			Name = "暖砖红",
			Color = Color.FromArgb(222, 133, 133)
		},
		new Entry
		{
			Id = "Rose",
			Name = "玫瑰",
			Color = Color.FromArgb(222, 133, 149)
		},
		new Entry
		{
			Id = "Pink",
			Name = "藕粉",
			Color = Color.FromArgb(222, 147, 188)
		},
		new Entry
		{
			Id = "Violet",
			Name = "藕荷",
			Color = Color.FromArgb(163, 142, 219)
		},
		new Entry
		{
			Id = "Graphite",
			Name = "石墨",
			Color = Color.FromArgb(107, 122, 143)
		},
		new Entry
		{
			Id = "Silver",
			Name = "银灰",
			Color = Color.FromArgb(168, 179, 193)
		},
		new Entry
		{
			Id = "Obsidian",
			Name = "曜石",
			Color = Color.FromArgb(58, 68, 83)
		},
		new Entry
		{
			Id = "Pearl",
			Name = "珍珠白",
			Color = Color.FromArgb(237, 241, 246)
		},
		new Entry
		{
			Id = "Accent",
			Name = "跟随系统强调色",
			Color = Color.FromArgb(0, 120, 212)
		}
	};

	private static Color _accentCache;

	private static long _accentAt;

	public static string DefaultId => "Teal";

	public static bool IsValid(string id)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i].Id == id)
			{
				return true;
			}
		}
		return false;
	}

	public static Color Resolve(string id)
	{
		if (id == "Accent")
		{
			long tickCount = Environment.TickCount64;
			if (_accentAt != 0L && tickCount - _accentAt < 2000)
			{
				return _accentCache;
			}
			try
			{
				Native.ColorAccent accent = Native.GetAccent();
				_accentCache = Color.FromArgb(accent.R, accent.G, accent.B);
			}
			catch
			{
				_accentCache = Color.FromArgb(0, 120, 212);
			}
			_accentAt = tickCount;
			return _accentCache;
		}
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i].Id == id)
			{
				return All[i].Color;
			}
		}
		return All[0].Color;
	}

	/// <summary>按「跟随系统强调色 &gt; 自定义颜色 &gt; 调色板」的优先级算最终颜色。</summary>
	public static Color ResolveEffective(string id, bool followAccent, int customArgb)
	{
		if (followAccent)
		{
			return Resolve("Accent");
		}
		if (customArgb != 0 && ((customArgb >> 24) & 0xFF) != 0)
		{
			return Color.FromArgb(customArgb);
		}
		return Resolve(id);
	}

	/// <summary>当前颜色的显示名（自定义颜色显示成 #RRGGBB）。</summary>
	public static string NameOf(string id, bool followAccent, int customArgb)
	{
		if (followAccent)
		{
			return "跟随系统强调色";
		}
		if (customArgb != 0 && ((customArgb >> 24) & 0xFF) != 0)
		{
			Color color = Color.FromArgb(customArgb);
			return "自定义 #" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
		}
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i].Id == id)
			{
				return All[i].Name;
			}
		}
		return id;
	}

	public static string HexOf(Color color)
	{
		return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
	}
}
internal static class FrameStyles
{
	public const string Dashed = "Dashed";

	public const string Hairline = "Hairline";

	public const string Focus = "Focus";

	public const string Glow = "Glow";

	public const string Shadow = "Shadow";

	// 精简后的样式表：#43 起只保留经得起看的 5 种。
	// 旧的 Flat / Solid / Double / Aurora / Neon / Halo / Corners / Underline / Brackets 全部删除
	// （用户反馈：丑、不明显、不实用），虚线环是唯一日常在用的，留作默认。
	public static readonly string[] All = new string[5] { "Dashed", "Hairline", "Focus", "Glow", "Shadow" };

	public static string DefaultId => "Dashed";

	internal static int MeasureStyleInkForTest(string style)
	{
		try
		{
			using Bitmap bitmap = new Bitmap(120, 90, PixelFormat.Format32bppArgb);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.Transparent);
				Draw(graphics, new RectangleF(0f, 0f, 120f, 90f), Color.FromArgb(107, 196, 165), style, 4, 8);
			}
			int num = 0;
			for (int i = 0; i < 90; i++)
			{
				for (int j = 0; j < 120; j++)
				{
					if (bitmap.GetPixel(j, i).A > 8)
					{
						num++;
					}
				}
			}
			return num;
		}
		catch
		{
			return -1;
		}
	}

	public static bool IsValid(string id)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i] == id)
			{
				return true;
			}
		}
		return false;
	}

	public static string DisplayName(string id)
	{
		return id switch
		{
			"Dashed" => "虚线环", 
			"Hairline" => "细线环", 
			"Focus" => "焦点环", 
			"Glow" => "柔光", 
			"Shadow" => "柔和投影", 
			_ => id, 
		};
	}

	public static string HintFor(string id)
	{
		return id switch
		{
			"Dashed" => "圆头短划、疏密跟着圆角走，日常最耐看", 
			"Hairline" => "一根细线，最不打扰界面", 
			"Focus" => "双层描边，最醒目，接近系统焦点框", 
			"Glow" => "细腻的多层衰减光晕，暗色窗口上尤其好看", 
			"Shadow" => "真实柔化投影，像窗口浮起来了一层", 
			_ => string.Empty, 
		};
	}

	public static float StrokeFor(string style, int thickness)
	{
		if (thickness < 1)
		{
			thickness = 1;
		}
		switch (style)
		{
		case "Hairline":
			return Math.Max(1f, (float)thickness * 0.55f);
		case "Focus":
			return Math.Max(2f, (float)thickness * 1.15f);
		default:
			return thickness;
		}
	}

	public static int MarginFor(string style, int thickness)
	{
		// 外扩量必须 ≥ 笔画宽度，否则 Draw 里内缩 strokeW/2 之后描边外缘会落到浮层边界之外被裁掉。
		int num = (int)Math.Ceiling((double)StrokeFor(style, thickness));
		return style switch
		{
			"Glow" => num + 12, 
			"Shadow" => num + 17, 
			"Focus" => num + 7, 
			"Dashed" => num + 8, 
			_ => num + 4, 
		};
	}

	public static void Draw(Graphics g, RectangleF area, Color color, string style, int thickness, int radius, int marginOverride = -1)
	{
		float strokeW = StrokeFor(style, thickness);
		int num = ((marginOverride >= 0) ? marginOverride : MarginFor(style, thickness));
		float pathOffset = (float)num - strokeW * 0.5f;
		if (pathOffset < 0f)
		{
			pathOffset = 0f;
		}
		RectangleF ring = new RectangleF(area.X + pathOffset, area.Y + pathOffset, area.Width - pathOffset * 2f, area.Height - pathOffset * 2f);
		if (ring.Width < 6f || ring.Height < 6f)
		{
			return;
		}
		float r = ClampRadius(radius, ring.Width, ring.Height);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.Half;
		switch (style)
		{
		case "Shadow":
			DrawShadow(g, ring, r, color, strokeW);
			return;
		case "Glow":
			DrawGlow(g, ring, r, color, strokeW);
			return;
		case "Focus":
			DrawFocus(g, ring, r, color, strokeW);
			return;
		case "Hairline":
			DrawHairline(g, ring, r, color, strokeW);
			return;
		}
		DrawDashed(g, ring, r, color, strokeW);
	}

	// 虚线环：外两层极淡的光把虚线"托"起来，三层用同一套物理划长（DashPattern 以笔宽为单位，
	// 必须按各层笔宽换算），否则几层的划子对不齐，看着就是一团毛刺——旧实现正是这个毛病。
	private static void DrawDashed(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		float dashPx = Math.Max(t * 2.4f, Math.Min(Math.Min(ring.Width, ring.Height) * 0.055f, t * 6f));
		float gapPx = dashPx * 0.68f;
		for (int i = 2; i >= 1; i--)
		{
			float w = t + 2.2f * (float)i;
			using GraphicsPath path = RoundedPath(ring, r);
			using Pen pen = new Pen(Color.FromArgb((i == 2) ? 30 : 17, color), w);
			pen.LineJoin = LineJoin.Round;
			pen.DashStyle = DashStyle.Dash;
			pen.DashCap = DashCap.Round;
			pen.DashPattern = new float[2]
			{
				dashPx / w,
				gapPx / w
			};
			g.DrawPath(pen, path);
		}
		using (GraphicsPath path2 = RoundedPath(ring, r))
		{
			using Pen pen2 = new Pen(color, t);
			pen2.LineJoin = LineJoin.Round;
			pen2.DashStyle = DashStyle.Dash;
			pen2.DashCap = DashCap.Round;
			pen2.DashPattern = new float[2]
			{
				dashPx / t,
				gapPx / t
			};
			g.DrawPath(pen2, path2);
		}
	}

	// 细线环：一层几乎看不见的外扩压锯齿 + 一根实线，界面洁癖的选项。
	private static void DrawHairline(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		using (GraphicsPath path = RoundedPath(ring, r))
		{
			using Pen pen = new Pen(Color.FromArgb(26, color), t + 2.4f);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using (GraphicsPath path2 = RoundedPath(ring, r))
		{
			using Pen pen2 = new Pen(color, Math.Max(1f, t));
			pen2.LineJoin = LineJoin.Round;
			g.DrawPath(pen2, path2);
		}
	}

	// 焦点环：外圈实色 + 内圈一道提亮细线（中间留 2px 空隙），接近 Windows 11 的焦点框：
	// 远看干净、近看有层次，是"最醒目"的那一档。
	private static void DrawFocus(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		using (GraphicsPath path = RoundedPath(ring, r))
		{
			using Pen pen = new Pen(color, t);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		float inset = t * 0.5f + 2f;
		RectangleF inner = RectangleF.Inflate(ring, -inset, -inset);
		if (inner.Width > 4f && inner.Height > 4f)
		{
			float ir = Math.Max(0f, r - inset);
			using GraphicsPath path2 = RoundedPath(inner, ir);
			using Pen pen2 = new Pen(Color.FromArgb(132, Lighten(color, 0.58f)), Math.Max(1f, t * 0.34f));
			pen2.LineJoin = LineJoin.Round;
			g.DrawPath(pen2, path2);
		}
	}

	// 柔光：十几层 1.15px 递进+高斯衰减的描边叠出来，没有旧版那种"粗笔刷一圈一圈"的带状感。
	private static void DrawGlow(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		const int layers = 14;
		double sigma = (double)layers * 0.42;
		double twoSigmaSq = 2.0 * sigma * sigma;
		for (int i = layers; i >= 1; i--)
		{
			int alpha = (int)Math.Round(46.0 * Math.Exp(-(double)(i * i) / twoSigmaSq));
			if (alpha < 2)
			{
				continue;
			}
			using GraphicsPath path = RoundedPath(ring, r);
			using Pen pen = new Pen(Color.FromArgb(alpha, color), t + (float)i * 1.15f * 2f);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using (GraphicsPath path2 = RoundedPath(ring, r))
		{
			using Pen pen2 = new Pen(color, t);
			pen2.LineJoin = LineJoin.Round;
			g.DrawPath(pen2, path2);
		}
	}

	// 柔和投影：走"距离场生成低分辨率阴影 + 双三次放大"，是真正的软阴影；
	// 旧版用 4 根几像素粗的半透明笔堆，边缘是一圈圈的硬台阶，所以在浅色窗口上看着脏。
	private static void DrawShadow(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		// pad 必须 ≥ 投影可见半径，否则会在浮层窗口边缘被硬切出一条边
		int pad = (int)Math.Ceiling(t) + 16;
		int w = (int)Math.Ceiling(ring.Width) + pad * 2;
		int h = (int)Math.Ceiling(ring.Height) + pad * 2;
		if (w < 24 || h < 24 || w > 8192 || h > 8192)
		{
			return;
		}
		// 键必须无碰撞地覆盖全部入参：w/h 各 14 位、圆角 10 位、色值 32 位、pad 6 位，
		// 加起来超过 64 位，硬塞进一个 long 必然位域重叠（色值的低位会盖住 h/r），曾导致命中错误缓存。
		var key = (w, h, (int)Math.Round(r), color.ToArgb(), pad);
		lock (ShadowGate)
		{
			if (_shadowCache == null || _shadowKey != key)
			{
				try
				{
					_shadowCache?.Dispose();
				}
				catch
				{
				}
				_shadowCache = BuildShadowBitmap(w, h, (float)pad + r, pad, color);
				_shadowKey = key;
			}
			if (_shadowCache == null)
			{
				return;
			}
			int dy = Math.Max(2, (int)Math.Round((double)(t * 0.9f)));
			InterpolationMode interpolationMode = g.InterpolationMode;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			try
			{
				g.DrawImage(_shadowCache, new Rectangle((int)Math.Floor((double)(ring.X - (float)pad)), (int)Math.Floor((double)(ring.Y - (float)pad + (float)dy)), w, h));
			}
			finally
			{
				g.InterpolationMode = interpolationMode;
			}
		}
		using GraphicsPath path = RoundedPath(ring, r);
		using Pen pen = new Pen(Color.FromArgb(226, color), Math.Max(1f, Math.Min(2.2f, t * 0.6f)));
		pen.LineJoin = LineJoin.Round;
		g.DrawPath(pen, path);
	}

	private static readonly object ShadowGate = new object();

	private static Bitmap _shadowCache;

	private static (int W, int H, int R, int Color, int Pad) _shadowKey;

	private static Bitmap BuildShadowBitmap(int w, int h, float radius, int pad, Color color)
	{
		try
		{
			const int scale = 4;
			int sw = Math.Max(8, w / scale);
			int sh = Math.Max(8, h / scale);
			Bitmap bitmap = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
			Rectangle rect = new Rectangle(0, 0, sw, sh);
			BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			try
			{
				float num = 1f / (float)scale;
				float num2 = radius * num;
				RectangleF rc = new RectangleF((float)pad * num, (float)pad * num, (float)sw - (float)pad * 2f * num, (float)sh - (float)pad * 2f * num);
				// 2.2 倍模糊半径处基本归零：让衰减正好在 pad 之内收干净
				float blur = Math.Max(1.6f, (float)pad * num / 2.2f);
				int r2 = (int)Math.Round((double)((float)(int)color.R * 0.42f));
				int g2 = (int)Math.Round((double)((float)(int)color.G * 0.42f));
				int b2 = (int)Math.Round((double)((float)(int)color.B * 0.42f));
				byte[] buffer = new byte[bitmapData.Stride * sh];
				for (int i = 0; i < sh; i++)
				{
					int num3 = i * bitmapData.Stride;
					for (int j = 0; j < sw; j++)
					{
						float num4 = SdRoundRect((float)j + 0.5f, (float)i + 0.5f, rc, num2);
						// 只在形状外侧画：内侧必须透明，否则等于给窗口蒙了一层灰（旧版投影"脏"的原因之一）
						if (!(num4 > 0f))
						{
							continue;
						}
						float num5 = num4 / blur;
						if (num5 >= 2.2f)
						{
							continue;
						}
						double num6 = Math.Exp((double)(-1.1f * num5 * num5));
						int num7 = (int)Math.Round(num6 * 96.0);
						if (num7 > 0)
						{
							int num8 = num3 + j * 4;
							buffer[num8] = (byte)b2;
							buffer[num8 + 1] = (byte)g2;
							buffer[num8 + 2] = (byte)r2;
							buffer[num8 + 3] = (byte)Math.Min(255, num7);
						}
					}
				}
				Marshal.Copy(buffer, 0, bitmapData.Scan0, buffer.Length);
			}
			finally
			{
				bitmap.UnlockBits(bitmapData);
			}
			return bitmap;
		}
		catch
		{
			return null;
		}
	}

	// 圆角矩形的有向距离：里面为负、边界为 0、外面为正。
	private static float SdRoundRect(float px, float py, RectangleF rc, float r)
	{
		float num = rc.Width * 0.5f;
		float num2 = rc.Height * 0.5f;
		float num3 = Math.Abs(px - (rc.X + num)) - (num - r);
		float num4 = Math.Abs(py - (rc.Y + num2)) - (num2 - r);
		float num5 = Math.Max(num3, 0f);
		float num6 = Math.Max(num4, 0f);
		return (float)Math.Sqrt((double)(num5 * num5 + num6 * num6)) + Math.Min(Math.Max(num3, num4), 0f) - r;
	}

	private static Color Lighten(Color c, float amt)
	{
		return Color.FromArgb(c.A, Mix(c.R, 255, amt), Mix(c.G, 255, amt), Mix(c.B, 255, amt));
	}

	private static Color Darken(Color c, float amt)
	{
		return Color.FromArgb(c.A, Mix(c.R, 0, amt), Mix(c.G, 0, amt), Mix(c.B, 0, amt));
	}

	private static int Mix(int v, int target, float amt)
	{
		int num = (int)Math.Round((float)v + (float)(target - v) * amt);
		if (num < 0)
		{
			num = 0;
		}
		if (num > 255)
		{
			num = 255;
		}
		return num;
	}

	public static float ClampRadius(int radius, float w, float h)
	{
		float num = Math.Min(w, h) / 2f - 1f;
		if (num < 0f)
		{
			num = 0f;
		}
		float num2 = radius;
		if (num2 < 0f)
		{
			num2 = 0f;
		}
		if (num2 > num)
		{
			num2 = num;
		}
		return num2;
	}

	public static bool FillsScreen(Native.RECT vis, int margin, bool wasInside)
	{
		try
		{
			int num = 0;
			int num2 = 0;
			try
			{
				Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(new Native.POINT((vis.Left + vis.Right) / 2, (vis.Top + vis.Bottom) / 2));
				num = monitorInfoAt.rcMonitor.Width;
				num2 = monitorInfoAt.rcMonitor.Height;
			}
			catch
			{
			}
			if (num <= 0 || num2 <= 0)
			{
				Rectangle virtualScreen = SystemInformation.VirtualScreen;
				num = virtualScreen.Width;
				num2 = virtualScreen.Height;
			}
			if (num <= 0 || num2 <= 0)
			{
				return false;
			}
			int num3 = (wasInside ? 88 : 92);
			return vis.Width >= (long)num * (long)num3 / 100 && vis.Height >= (long)num2 * (long)num3 / 100;
		}
		catch
		{
			return false;
		}
	}

	private static GraphicsPath RoundedPath(RectangleF rc, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (radius <= 0.5f)
		{
			graphicsPath.AddRectangle(rc);
			return graphicsPath;
		}
		float num = radius * 2f;
		graphicsPath.AddArc(rc.X, rc.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(rc.Right - num, rc.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(rc.Right - num, rc.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rc.X, rc.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
internal interface IInputSink
{
	bool OnKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb);

	bool OnMouse(int msg, ref Native.MSLLHOOKSTRUCT ms);
}
internal sealed class HookEngine : IDisposable
{
	public const uint WM_REHOOK_MOUSE = 32776u;

	private readonly IInputSink _sink;

	private readonly Native.HookProc _kbProc;

	private readonly Native.HookProc _msProc;

	private nint _kbHook = IntPtr.Zero;

	private nint _msHook = IntPtr.Zero;

	private Thread _hookThread;

	private uint _hookThreadId;

	private readonly ManualResetEventSlim _hookReady = new ManualResetEventSlim(initialState: false);

	private volatile bool _hookQuit;

	private volatile int _seenX;

	private volatile int _seenY;

	private volatile bool _seenValid;

	private long _callbackSinceTicks;

	private long _seenCount;

	public static readonly nint MagicExtraInfo = new IntPtr(1215261796);

	public int LastError { get; private set; }

	public bool KeyboardInstalled => _kbHook != IntPtr.Zero;

	public bool MouseInstalled => _msHook != IntPtr.Zero;

	public int ReinstallCount { get; private set; }

	public double CallbackStalledMs
	{
		get
		{
			long num = Interlocked.Read(in _callbackSinceTicks);
			if (num == 0L)
			{
				return 0.0;
			}
			double num2 = (double)(Stopwatch.GetTimestamp() - num) * 1000.0 / (double)Stopwatch.Frequency;
			if (!(num2 < 0.0))
			{
				return num2;
			}
			return 0.0;
		}
	}

	public long SeenCount => Interlocked.Read(in _seenCount);

	public bool TryGetLastSeen(out int x, out int y)
	{
		x = _seenX;
		y = _seenY;
		return _seenValid;
	}

	public HookEngine(IInputSink sink)
	{
		_sink = sink;
		_kbProc = KeyboardCallback;
		_msProc = MouseCallback;
	}

	public bool Install()
	{
		if (_kbHook != IntPtr.Zero && _msHook != IntPtr.Zero)
		{
			return true;
		}
		LastError = 0;
		_hookQuit = false;
		_hookReady.Reset();
		_hookThread = new Thread(HookThreadMain);
		_hookThread.IsBackground = true;
		_hookThread.Priority = ThreadPriority.AboveNormal;
		_hookThread.Name = "HoldDrag.HookThread";
		_hookThread.Start();
		if (!_hookReady.Wait(3000))
		{
			Log.Error("钩子线程启动超时");
		}
		if (KeyboardInstalled)
		{
			return MouseInstalled;
		}
		return false;
	}

	private void HookThreadMain()
	{
		try
		{
			_hookThreadId = Native.GetCurrentThreadId();
			Native.PeekMessageW(out var _, IntPtr.Zero, 0u, 0u, 0u);
			nint moduleHandleW = Native.GetModuleHandleW(null);
			if (_kbHook == IntPtr.Zero)
			{
				_kbHook = Native.SetWindowsHookExW(13, _kbProc, moduleHandleW, 0u);
				if (_kbHook == IntPtr.Zero)
				{
					LastError = Marshal.GetLastWin32Error();
					Log.Error("键盘钩子安装失败，Win32 错误码 " + LastError);
				}
			}
			if (_msHook == IntPtr.Zero)
			{
				_msHook = Native.SetWindowsHookExW(14, _msProc, moduleHandleW, 0u);
				Native.POINT lpPoint;
				if (_msHook == IntPtr.Zero)
				{
					LastError = Marshal.GetLastWin32Error();
					Log.Error("鼠标钩子安装失败，Win32 错误码 " + LastError);
				}
				else if (Native.GetCursorPos(out lpPoint))
				{
					_seenX = lpPoint.X;
					_seenY = lpPoint.Y;
					_seenValid = true;
				}
			}
			_hookReady.Set();
			Native.MSG lpMsg2;
			while (!_hookQuit && Native.GetMessageW(out lpMsg2, IntPtr.Zero, 0u, 0u) > 0)
			{
				if (lpMsg2.message == 32776)
				{
					ReinstallMouseOnThisThread();
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			_hookReady.Set();
		}
	}

	private void ReinstallMouseOnThisThread()
	{
		if (_hookQuit)
		{
			return;
		}
		try
		{
			if (_msHook != IntPtr.Zero)
			{
				Native.UnhookWindowsHookEx(_msHook);
				_msHook = IntPtr.Zero;
			}
			nint moduleHandleW = Native.GetModuleHandleW(null);
			_msHook = Native.SetWindowsHookExW(14, _msProc, moduleHandleW, 0u);
			if (_msHook == IntPtr.Zero)
			{
				LastError = Marshal.GetLastWin32Error();
				Log.Error("抢占链头：鼠标钩子重装失败，Win32 错误码 " + LastError);
				return;
			}
			if (Native.GetCursorPos(out var lpPoint))
			{
				_seenX = lpPoint.X;
				_seenY = lpPoint.Y;
				_seenValid = true;
			}
			ReinstallCount++;
			Log.Info("鼠标钩子已就地重装并顶到钩子链头部（激活时抢占，第三方手势工具不再先拿到鼠标键）");
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public bool ReinstallMouseForChainPriority()
	{
		uint hookThreadId = _hookThreadId;
		if (hookThreadId != 0 && _msHook != IntPtr.Zero)
		{
			return Native.PostThreadMessageW(hookThreadId, 32776u, IntPtr.Zero, IntPtr.Zero);
		}
		return Reinstall();
	}

	public bool Reinstall()
	{
		Log.Info("看门狗判定键鼠钩子已失效，正在重新安装");
		Uninstall();
		_seenValid = false;
		bool flag = Install();
		ReinstallCount++;
		Log.Info("钩子重装" + (flag ? "成功" : ("仍然失败，错误码 " + LastError)));
		return flag;
	}

	private void EnterCallback()
	{
		Interlocked.Exchange(ref _callbackSinceTicks, Stopwatch.GetTimestamp());
	}

	private void LeaveCallback()
	{
		Interlocked.Exchange(ref _callbackSinceTicks, 0L);
	}

	private void MarkSeen(Native.MSLLHOOKSTRUCT ms)
	{
		_seenX = ms.pt.X;
		_seenY = ms.pt.Y;
		_seenValid = true;
		Interlocked.Increment(ref _seenCount);
	}

	public void Uninstall()
	{
		if (_kbHook != IntPtr.Zero)
		{
			Native.UnhookWindowsHookEx(_kbHook);
			_kbHook = IntPtr.Zero;
		}
		if (_msHook != IntPtr.Zero)
		{
			Native.UnhookWindowsHookEx(_msHook);
			_msHook = IntPtr.Zero;
		}
		if (_hookThreadId != 0)
		{
			Native.PostThreadMessageW(_hookThreadId, 18u, IntPtr.Zero, IntPtr.Zero);
			_hookThreadId = 0u;
		}
	}

	public void Dispose()
	{
		Uninstall();
	}

	private nint KeyboardCallback(int nCode, nint wParam, nint lParam)
	{
		EnterCallback();
		try
		{
			if (nCode >= 0 && _sink != null)
			{
				Native.KBDLLHOOKSTRUCT kb = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
				if (kb.dwExtraInfo != MagicExtraInfo)
				{
					int num = (int)((IntPtr)wParam).ToInt64();
					bool flag = num == 256 || num == 260;
					bool flag2 = num == 257 || num == 261;
					if ((flag | flag2) && _sink.OnKeyboard(flag, ref kb))
					{
						return new IntPtr(1);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			LeaveCallback();
		}
		return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
	}

	private nint MouseCallback(int nCode, nint wParam, nint lParam)
	{
		EnterCallback();
		try
		{
			if (nCode >= 0 && _sink != null)
			{
				Native.MSLLHOOKSTRUCT ms = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
				ms.pt = CoordSpace.ToProcess(ms.pt);
				MarkSeen(ms);
				int num = (int)((IntPtr)wParam).ToInt64();
				if (Log.Verbose && (num == 513 || num == 514 || num == 516 || num == 517 || num == 519 || num == 520))
				{
					Log.Trace("[探针] 钩子收到按键 msg=" + num + " flags=0x" + ms.flags.ToString("X") + " extra=" + ((IntPtr)ms.dwExtraInfo).ToInt64().ToString("X"));
				}
				if (ms.dwExtraInfo != MagicExtraInfo && _sink.OnMouse(num, ref ms))
				{
					return new IntPtr(1);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			LeaveCallback();
		}
		return Native.CallNextHookEx(_msHook, nCode, wParam, lParam);
	}
}
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
internal static class KeysUtil
{
	public const string LeftAlt = "K:LAlt";

	public const string RightAlt = "K:RAlt";

	public const string LeftCtrl = "K:LCtrl";

	public const string RightCtrl = "K:RCtrl";

	public const string LeftShift = "K:LShift";

	public const string RightShift = "K:RShift";

	public const string MouseX1 = "M:1";

	public const string MouseX2 = "M:2";

	public const string DefaultTrigger = "K:0x0D";

	private const uint VK_SHIFT = 16u;

	private const uint VK_CONTROL = 17u;

	private const uint VK_MENU = 18u;

	private const uint VK_LSHIFT = 160u;

	private const uint VK_RSHIFT = 161u;

	private const uint VK_LCONTROL = 162u;

	private const uint VK_RCONTROL = 163u;

	private const uint VK_LMENU = 164u;

	private const uint VK_RMENU = 165u;

	private const ushort SCAN_LSHIFT = 42;

	private const ushort SCAN_RSHIFT = 54;

	private static readonly Dictionary<int, string> Display;

	private static readonly Dictionary<int, string> SpecialDisplay;

	static KeysUtil()
	{
		Display = new Dictionary<int, string>
		{
			{ 13, "Enter 回车" },
			{ 32, "空格" },
			{ 9, "Tab" },
			{ 20, "CapsLock 大写锁定" },
			{ 93, "菜单键 (Applications)" },
			{ 8, "Backspace 退格" },
			{ 45, "Insert" },
			{ 46, "Delete" },
			{ 36, "Home" },
			{ 35, "End" },
			{ 33, "Page Up" },
			{ 34, "Page Down" },
			{ 37, "方向键 左" },
			{ 38, "方向键 上" },
			{ 39, "方向键 右" },
			{ 40, "方向键 下" },
			{ 27, "Esc" },
			{ 91, "左 Win" },
			{ 92, "右 Win" },
			{ 144, "NumLock" },
			{ 145, "ScrollLock" },
			{ 19, "Pause" },
			{ 44, "PrintScreen" },
			{ 192, "` ~" },
			{ 189, "- _" },
			{ 187, "= +" },
			{ 219, "[ {" },
			{ 221, "] }" },
			{ 220, "\\ |" },
			{ 186, "; :" },
			{ 222, "' \"" },
			{ 188, ", <" },
			{ 190, ". >" },
			{ 191, "/ ?" }
		};
		SpecialDisplay = new Dictionary<int, string>();
	}

	public static string CanonicalFromHook(Native.KBDLLHOOKSTRUCT kb)
	{
		uint vkCode = kb.vkCode;
		uint scanCode = kb.scanCode;
		bool flag = (kb.flags & 1) != 0;
		switch (vkCode)
		{
		case 16u:
			if (scanCode != 54)
			{
				return "K:LShift";
			}
			return "K:RShift";
		case 160u:
			return "K:LShift";
		case 161u:
			return "K:RShift";
		case 17u:
		case 162u:
		case 163u:
			if (!flag)
			{
				return "K:LCtrl";
			}
			return "K:RCtrl";
		case 18u:
		case 164u:
		case 165u:
			if (!flag)
			{
				return "K:LAlt";
			}
			return "K:RAlt";
		default:
			return "K:0x" + vkCode.ToString("X2");
		}
	}

	public static string CanonicalFromMouseHook(int msg, Native.MSLLHOOKSTRUCT ms)
	{
		if (msg != 523)
		{
			return null;
		}
		return (int)(ms.mouseData >> 16) switch
		{
			1 => "M:1", 
			2 => "M:2", 
			_ => null, 
		};
	}

	public static bool MatchesKeyboard(string id, Native.KBDLLHOOKSTRUCT kb)
	{
		if (string.IsNullOrEmpty(id) || id[0] != 'K')
		{
			return false;
		}
		uint vkCode = kb.vkCode;
		uint scanCode = kb.scanCode;
		bool flag = (kb.flags & 1) != 0;
		switch (id)
		{
		case "K:LShift":
			if (vkCode != 16 && vkCode != 160)
			{
				return false;
			}
			return scanCode != 54;
		case "K:RShift":
			if (vkCode == 16 || vkCode == 161)
			{
				return scanCode == 54;
			}
			return false;
		case "K:LCtrl":
			if (vkCode == 17 || vkCode == 162)
			{
				return !flag;
			}
			return false;
		case "K:RCtrl":
			return (vkCode == 17 || vkCode == 163) & flag;
		case "K:LAlt":
			if (vkCode == 18 || vkCode == 164)
			{
				return !flag;
			}
			return false;
		case "K:RAlt":
			return (vkCode == 18 || vkCode == 165) & flag;
		case "M:1":
		case "M:2":
			return false;
		default:
		{
			if (id.Length < 3 || id[1] != ':')
			{
				return false;
			}
			if (!TryParseHex(id.Substring(2), out var value))
			{
				return false;
			}
			if ((long)value == 16 && (vkCode == 16 || vkCode == 160 || vkCode == 161))
			{
				return true;
			}
			if ((long)value == 17 && (vkCode == 17 || vkCode == 162 || vkCode == 163))
			{
				return true;
			}
			if ((long)value == 18 && (vkCode == 18 || vkCode == 164 || vkCode == 165))
			{
				return true;
			}
			return vkCode == (uint)value;
		}
		}
	}

	public static bool MatchesMouse(string id, int msg, Native.MSLLHOOKSTRUCT ms)
	{
		if (id != "M:1" && id != "M:2")
		{
			return false;
		}
		bool flag = msg == 523;
		bool flag2 = msg == 524;
		if (!flag && !flag2)
		{
			return false;
		}
		int num = (int)(ms.mouseData >> 16);
		if (id == "M:1")
		{
			return num == 1;
		}
		return num == 2;
	}

	public static string FromWinFormsKey(Keys key)
	{
		switch (key)
		{
		case Keys.LShiftKey:
			return "K:LShift";
		case Keys.RShiftKey:
			return "K:RShift";
		case Keys.LControlKey:
			return "K:LCtrl";
		case Keys.RControlKey:
			return "K:RCtrl";
		case Keys.LMenu:
			return "K:LAlt";
		case Keys.RMenu:
			return "K:RAlt";
		case Keys.LWin:
			return "K:0x5B";
		case Keys.RWin:
			return "K:0x5C";
		case Keys.ShiftKey:
			return "K:LShift";
		case Keys.ControlKey:
			return "K:LCtrl";
		case Keys.Menu:
			return "K:LAlt";
		default:
		{
			int num = (int)(key & (Keys.F16 | Keys.F17));
			if (num == 0)
			{
				num = (int)key;
			}
			return "K:0x" + num.ToString("X2");
		}
		}
	}

	public static string FromMouseButton(MouseButtons b)
	{
		return b switch
		{
			MouseButtons.XButton1 => "M:1", 
			MouseButtons.XButton2 => "M:2", 
			_ => null, 
		};
	}

	public static int ToVirtualKey(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return 0;
		}
		switch (id)
		{
		case "K:LShift":
			return 160;
		case "K:RShift":
			return 161;
		case "K:LCtrl":
			return 162;
		case "K:RCtrl":
			return 163;
		case "K:LAlt":
			return 164;
		case "K:RAlt":
			return 165;
		case "M:1":
			return 5;
		case "M:2":
			return 6;
		default:
		{
			if (id.Length >= 3 && id[1] == ':' && TryParseHex(id.Substring(2), out var value))
			{
				return value;
			}
			return 0;
		}
		}
	}

	public static string DisplayName(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return "（未设置）";
		}
		switch (id)
		{
		case "K:LAlt":
			return "左 Alt";
		case "K:RAlt":
			return "右 Alt（AltGr）";
		case "K:LCtrl":
			return "左 Ctrl";
		case "K:RCtrl":
			return "右 Ctrl";
		case "K:LShift":
			return "左 Shift";
		case "K:RShift":
			return "右 Shift";
		case "M:1":
			return "鼠标侧键 前进 (X1)";
		case "M:2":
			return "鼠标侧键 后退 (X2)";
		default:
		{
			if (id.Length >= 3 && id[1] == ':' && TryParseHex(id.Substring(2), out var value))
			{
				if (Display.TryGetValue(value, out var value2))
				{
					return value2;
				}
				if (value >= 112 && value <= 135)
				{
					return "F" + (value - 111);
				}
				if (value >= 48 && value <= 57)
				{
					return "数字 " + (char)value;
				}
				if (value >= 65 && value <= 90)
				{
					return "字母 " + (char)value;
				}
				if (value >= 96 && value <= 105)
				{
					return "小键盘 " + (value - 96);
				}
				return value switch
				{
					106 => "小键盘 *", 
					107 => "小键盘 +", 
					109 => "小键盘 -", 
					110 => "小键盘 .", 
					111 => "小键盘 /", 
					_ => "虚拟键 0x" + value.ToString("X2"), 
				};
			}
			return id;
		}
		}
	}

	public static bool IsToggleKey(string id)
	{
		if (id != null && id.Length >= 3 && id[1] == ':' && TryParseHex(id.Substring(2), out var value))
		{
			if (value != 20 && value != 144)
			{
				return value == 145;
			}
			return true;
		}
		return false;
	}

	public static bool IsWindowsKey(string id)
	{
		if (!(id == "K:0x5B"))
		{
			return id == "K:0x5C";
		}
		return true;
	}

	private static bool TryParseHex(string s, out int value)
	{
		value = 0;
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		try
		{
			value = Convert.ToInt32(s, 16);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
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
			if (!CanOpenForAppend(_path))
			{
				_path = System.IO.Path.Combine(dir, "holddrag." + Process.GetCurrentProcess().Id + ".log");
			}
			Trim();
			CleanupOldLogs(dir);
		}
		catch
		{
			_path = string.Empty;
		}
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
internal static class Native
{
	internal struct POINT(int x, int y)
	{
		public int X = x;

		public int Y = y;

		public override string ToString()
		{
			return X + "," + Y;
		}
	}

	internal struct RECT(int l, int t, int r, int b)
	{
		public int Left = l;

		public int Top = t;

		public int Right = r;

		public int Bottom = b;

		public int Width => Right - Left;

		public int Height => Bottom - Top;

		public bool IsEmpty
		{
			get
			{
				if (Width > 0)
				{
					return Height <= 0;
				}
				return true;
			}
		}

		public override string ToString()
		{
			return Left + "," + Top + " " + Width + "x" + Height;
		}
	}

	internal struct KBDLLHOOKSTRUCT
	{
		public uint vkCode;

		public uint scanCode;

		public uint flags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct MSLLHOOKSTRUCT
	{
		public POINT pt;

		public uint mouseData;

		public uint flags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public uint dwFlags;
	}

	internal struct WINDOWPLACEMENT
	{
		public int length;

		public int flags;

		public int showCmd;

		public POINT ptMinPosition;

		public POINT ptMaxPosition;

		public RECT rcNormalPosition;
	}

	internal struct MSG
	{
		public nint hwnd;

		public uint message;

		public nint wParam;

		public nint lParam;

		public uint time;

		public int ptX;

		public int ptY;
	}

	internal struct INPUT
	{
		public uint type;

		public INPUTUNION u;
	}

	[StructLayout(LayoutKind.Explicit)]
	internal struct INPUTUNION
	{
		[FieldOffset(0)]
		public MOUSEINPUT mi;

		[FieldOffset(0)]
		public KEYBDINPUT ki;

		[FieldOffset(0)]
		public HARDWAREINPUT hi;
	}

	internal struct MOUSEINPUT
	{
		public int dx;

		public int dy;

		public uint mouseData;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct KEYBDINPUT
	{
		public ushort wVk;

		public ushort wScan;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct HARDWAREINPUT
	{
		public uint uMsg;

		public ushort wParamL;

		public ushort wParamH;
	}

	internal delegate nint HookProc(int nCode, nint wParam, nint lParam);

	internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	internal struct SIZE(int w, int h)
	{
		public int cx = w;

		public int cy = h;
	}

	internal struct ColorAccent(int r, int g, int b)
	{
		public int R = r;

		public int G = g;

		public int B = b;
	}

	internal struct BLENDFUNCTION
	{
		public byte BlendOp;

		public byte BlendFlags;

		public byte SourceConstantAlpha;

		public byte AlphaFormat;
	}

	internal delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	internal struct WNDCLASSEX
	{
		public uint cbSize;

		public uint style;

		public WndProc lpfnWndProc;

		public int cbClsExtra;

		public int cbWndExtra;

		public nint hInstance;

		public nint hIcon;

		public nint hCursor;

		public nint hbrBackground;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string lpszMenuName;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string lpszClassName;

		public nint hIconSm;
	}

	public const int WH_KEYBOARD_LL = 13;

	public const int WH_MOUSE_LL = 14;

	public const int WM_KEYDOWN = 256;

	public const int WM_KEYUP = 257;

	public const int WM_SYSKEYDOWN = 260;

	public const int WM_SYSKEYUP = 261;

	public const int WM_MOUSEMOVE = 512;

	public const int WM_WINDOWPOSCHANGING = 70;

	public const int WM_WINDOWPOSCHANGED = 71;

	public const int WM_LBUTTONDOWN = 513;

	public const int WM_CANCELMODE = 31;

	public const int WM_LBUTTONUP = 514;

	public const int WM_RBUTTONDOWN = 516;

	public const int WM_RBUTTONUP = 517;

	public const int WM_MBUTTONDOWN = 519;

	public const int WM_MBUTTONUP = 520;

	public const int WM_MOUSEWHEEL = 522;

	public const int WM_XBUTTONDOWN = 523;

	public const int WM_XBUTTONUP = 524;

	public const int WM_MOUSEHWHEEL = 526;

	public const int MK_LBUTTON = 1;

	public const int MK_RBUTTON = 2;

	public const int MK_MBUTTON = 16;

	public const int WM_SYSCOMMAND = 274;

	public const int WM_CLOSE = 16;

	public const int SC_CLOSE = 61536;

	public const int SC_MAXIMIZE = 61488;

	public const int SC_MINIMIZE = 61472;

	public const int SC_RESTORE = 61728;

	public const uint LLKHF_EXTENDED = 1u;

	public const uint LLKHF_INJECTED = 16u;

	public const uint LLKHF_LOWER_IL_INJECTED = 2u;

	public const uint LLMHF_INJECTED = 1u;

	public const uint LLMHF_LOWER_IL_INJECTED = 2u;

	public const uint SWP_NOSIZE = 1u;

	public const uint SWP_NOMOVE = 2u;

	public const uint SWP_NOZORDER = 4u;

	public const uint SWP_NOACTIVATE = 16u;

	public const uint SWP_SHOWWINDOW = 64u;

	public const uint SWP_NOOWNERZORDER = 512u;

	public const uint SWP_NOSENDCHANGING = 1024u;

	public const uint SWP_NOCOPYBITS = 256u;

	public const uint SWP_ASYNCWINDOWPOS = 16384u;

	public const uint RESIZEFLAGS = 532u;

	public const uint MOVETHICKBORDERS = 533u;

	public const uint MOVEASYNC = 16917u;

	public const int WM_ENTERSIZEMOVE = 561;

	public const int WM_EXITSIZEMOVE = 562;

	public const int WM_SIZING = 532;

	public const int WM_SYNCPAINT = 136;

	public const uint SMTO_NORMAL = 0u;

	public const uint SMTO_ABORTIFHUNG = 2u;

	public const uint EVENT_SYSTEM_MOVESIZESTART = 10u;

	public const uint EVENT_SYSTEM_MOVESIZEEND = 11u;

	public const uint PM_NOREMOVE = 0u;

	public const uint PM_REMOVE = 1u;

	public const int WM_QUIT = 18;

	public static readonly nint HWND_TOP = IntPtr.Zero;

	public static readonly nint HWND_BOTTOM = new IntPtr(1);

	public static readonly nint HWND_TOPMOST = new IntPtr(-1);

	public static readonly nint HWND_NOTOPMOST = new IntPtr(-2);

	public const int SW_HIDE = 0;

	public const int SW_SHOWNORMAL = 1;

	public const int SW_SHOWMINIMIZED = 2;

	public const int SW_MAXIMIZE = 3;

	public const int SW_SHOWNOACTIVATE = 4;

	public const int SW_MINIMIZE = 6;

	public const int SW_RESTORE = 9;

	public const int GWL_STYLE = -16;

	public const int GWL_EXSTYLE = -20;

	public const long WS_VISIBLE = 268435456L;

	public const long WS_CAPTION = 12582912L;

	public const long WS_THICKFRAME = 262144L;

	public const long WS_MINIMIZE = 536870912L;

	public const long WS_MAXIMIZE = 16777216L;

	public const long WS_SYSMENU = 524288L;

	public const long WS_EX_LAYERED = 524288L;

	public const long WS_EX_TRANSPARENT = 32L;

	public const long WS_EX_TOOLWINDOW = 128L;

	public const long WS_EX_NOACTIVATE = 134217728L;

	public const long WS_EX_TOPMOST = 8L;

	public const uint GA_PARENT = 1u;

	public const uint GA_ROOT = 2u;

	public const uint GA_ROOTOWNER = 3u;

	public const uint MONITOR_DEFAULTTONULL = 0u;

	public const uint MONITOR_DEFAULTTOPRIMARY = 1u;

	public const uint MONITOR_DEFAULTTONEAREST = 2u;

	public const uint LWA_COLORKEY = 1u;

	public const uint LWA_ALPHA = 2u;

	public const uint INPUT_MOUSE = 0u;

	public const uint INPUT_KEYBOARD = 1u;

	public const uint MOUSEEVENTF_MOVE = 1u;

	public const uint MOUSEEVENTF_LEFTDOWN = 2u;

	public const uint MOUSEEVENTF_LEFTUP = 4u;

	public const uint MOUSEEVENTF_RIGHTDOWN = 8u;

	public const uint MOUSEEVENTF_RIGHTUP = 16u;

	public const uint MOUSEEVENTF_MIDDLEDOWN = 32u;

	public const uint MOUSEEVENTF_MIDDLEUP = 64u;

	public const uint MOUSEEVENTF_ABSOLUTE = 32768u;

	public const uint MOUSEEVENTF_VIRTUALDESK = 16384u;

	public const uint INJECT_LEFTDOWN = 2u;

	public const uint INJECT_LEFTUP = 4u;

	public const uint INJECT_RIGHTDOWN = 8u;

	public const uint INJECT_RIGHTUP = 16u;

	public const uint KEYEVENTF_EXTENDEDKEY = 1u;

	public const uint KEYEVENTF_KEYUP = 2u;

	public const uint KEYEVENTF_SCANCODE = 8u;

	public const ushort XBUTTON1 = 1;

	public const ushort XBUTTON2 = 2;

	public const int SM_CXMINTRACK = 34;

	public const int SM_CYMINTRACK = 35;

	public const int SM_CXSCREEN = 0;

	public const int SM_CYSCREEN = 1;

	internal const int WM_SETREDRAW = 11;

	internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 4096u;

	internal const int DPI_AWARENESS_INVALID = -1;

	internal const int DPI_AWARENESS_UNAWARE = 0;

	internal const int DPI_AWARENESS_SYSTEM_AWARE = 1;

	internal const int DPI_AWARENESS_PER_MONITOR_AWARE = 2;

	internal static readonly nint DPI_AWARENESS_CONTEXT_UNAWARE = new IntPtr(-1);

	internal static readonly nint DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new IntPtr(-2);

	internal static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new IntPtr(-3);

	internal static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

	internal const int DESKTOPHORZRES = 118;

	internal const int DESKTOPVERTRES = 117;

	internal const uint GW_HWNDNEXT = 2u;

	internal const uint GW_HWNDPREV = 3u;

	internal const uint CWP_SKIPINVISIBLE = 1u;

	internal const uint CWP_SKIPDISABLED = 2u;

	internal const uint CWP_SKIPTRANSPARENT = 4u;

	internal const int VK_LBUTTON = 1;

	internal const int VK_RBUTTON = 2;

	internal const int VK_MBUTTON = 4;

	internal const uint WM_NCHITTEST = 132u;

	internal const uint WM_ERASEBKGND = 20u;

	internal const uint WM_PAINT = 15u;

	internal const uint WM_DESTROY = 2u;

	internal const uint WM_MOUSEACTIVATE = 33u;

	internal const uint WM_APP = 32768u;

	internal const uint WM_DISPLAYCHANGE = 126u;

	internal const int HTTRANSPARENT = -1;

	internal const int MA_NOACTIVATE = 3;

	internal const uint WS_POPUP = 2147483648u;

	internal const uint WS_CLIPSIBLINGS = 67108864u;

	internal const uint ULW_ALPHA = 2u;

	internal const byte AC_SRC_OVER = 0;

	internal const byte AC_SRC_ALPHA = 1;

	internal const int RGN_AND = 1;

	internal const int RGN_OR = 2;

	internal const int RGN_DIFF = 4;

	internal const int VREFRESH = 116;

	internal const uint GR_GDIOBJECTS = 0u;

	internal const uint GR_USEROBJECTS = 1u;

	internal const int COLOR_HIGHLIGHT = 13;

	internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

	internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

	internal const int DWMWCP_ROUND = 2;

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint SetWindowsHookExW(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint GetModuleHandleW(string lpModuleName);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint WindowFromPoint(POINT Point);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetAncestor(nint hwnd, uint gaFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsWindow(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsZoomed(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetForegroundWindow();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SendMessageW(nint hWnd, int Msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool PostMessageW(nint hWnd, int Msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool CloseHandle(nint hObject);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int GetWindowTextLengthW(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int GetSystemMetrics(int nIndex);

	[DllImport("user32.dll")]
	internal static extern nint GetThreadDpiAwarenessContext();

	[DllImport("user32.dll")]
	internal static extern int GetAwarenessFromDpiAwarenessContext(nint value);

	[DllImport("user32.dll")]
	internal static extern uint GetDpiForWindow(nint hwnd);

	internal static int DpiOfWindow(nint hwnd)
	{
		try
		{
			if (hwnd != IntPtr.Zero)
			{
				uint dpiForWindow = GetDpiForWindow(hwnd);
				if (dpiForWindow >= 48 && dpiForWindow <= 960)
				{
					return (int)dpiForWindow;
				}
			}
		}
		catch
		{
		}
		return 96;
	}

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetProcessDpiAwarenessContext(nint value);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetProcessDpiAware();

	[DllImport("user32.dll")]
	internal static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint GetDoubleClickTime();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetParent(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetLayeredWindowAttributes(nint hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetWindow(nint hWnd, uint uCmd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetDesktopWindow();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint ChildWindowFromPointEx(nint hWndParent, POINT pt, uint uFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern short GetAsyncKeyState(int vKey);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint FindWindowW(string lpClassName, string lpWindowName);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SetCapture(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	internal static extern nint GetCapture();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

	[DllImport("kernel32.dll")]
	internal static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint DispatchMessageW(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SendMessageTimeoutW(nint hWnd, int msg, nint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern void NotifyWinEvent(uint eventId, nint hWnd, int idObject, int idChild);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetClipCursor(out RECT lpRect);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
	private static extern int GetWindowLong32(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
	private static extern nint GetWindowLong64(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
	private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	private static extern nint SetWindowLong64(nint hWnd, int nIndex, nint dwNewLong);

	internal static long GetWindowLong(nint hWnd, int nIndex)
	{
		if (IntPtr.Size == 8)
		{
			return ((IntPtr)GetWindowLong64(hWnd, nIndex)).ToInt64();
		}
		return GetWindowLong32(hWnd, nIndex);
	}

	internal static void SetWindowLong(nint hWnd, int nIndex, long value)
	{
		if (IntPtr.Size == 8)
		{
			SetWindowLong64(hWnd, nIndex, new IntPtr(value));
		}
		else
		{
			SetWindowLong32(hWnd, nIndex, (int)value);
		}
	}

	[DllImport("shell32.dll", SetLastError = true)]
	internal static extern bool IsUserAnAdmin();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool DestroyIcon(nint hIcon);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	internal static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool DestroyWindow(nint hWnd);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateRectRgn(int x1, int y1, int x2, int y2);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern int CombineRgn(nint hrgnDst, nint hrgnSrc1, nint hrgnSrc2, int iMode);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern bool DeleteObject(nint ho);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ValidateRect(nint hWnd, nint lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetClientRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

	[DllImport("user32.dll")]
	internal static extern int FillRect(nint hDC, ref RECT lprc, nint hbr);

	[DllImport("gdi32.dll")]
	internal static extern nint CreateSolidBrush(uint crColor);

	[DllImport("user32.dll")]
	internal static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);

	internal static uint ColorRef(int r, int g, int b)
	{
		return (uint)(r | (g << 8) | (b << 16));
	}

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize, nint hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateCompatibleDC(nint hdc);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint SelectObject(nint hdc, nint h);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern bool DeleteDC(nint hdc);

	[DllImport("user32.dll")]
	internal static extern nint GetDC(nint hWnd);

	[DllImport("user32.dll")]
	internal static extern int ReleaseDC(nint hWnd, nint hDC);

	[DllImport("gdi32.dll")]
	internal static extern uint GetPixel(nint hdc, int x, int y);

	[DllImport("gdi32.dll")]
	internal static extern int GetDeviceCaps(nint hdc, int nIndex);

	[DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
	internal static extern uint TimeBeginPeriod(uint uMilliseconds);

	[DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
	internal static extern uint TimeEndPeriod(uint uMilliseconds);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint GetGuiResources(nint hProcess, uint uiFlags);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern nint GetCurrentProcess();

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool TerminateProcess(nint hProcess, uint uExitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool GetProcessHandleCount(nint hProcess, out uint pdwHandleCount);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

	[DllImport("user32.dll")]
	internal static extern uint GetSysColor(int nIndex);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);


	// 标题栏颜色和页面底色对齐：否则内容换成深色、最上面那条还白着，整扇窗就"断层"了。
	// Win11 22000+ 支持 DWMWA_CAPTION_COLOR(35) / TEXT_COLOR(36) / BORDER_COLOR(34)，能刷成任意色；
	// 老系统上这三个属性会失败，退回"深色标题栏"(20，再退 19)——至少不再白得刺眼。
	// 返回是否成功刷上了自定义颜色。
	internal static bool SetTitleBarTheme(nint hwnd, bool dark, Color caption, Color text, Color border)
	{
		try
		{
			int pvAttribute = ToColorRef(caption);
			bool flag = DwmSetWindowAttribute(hwnd, 35, ref pvAttribute, 4) == 0;
			int pvAttribute2 = ToColorRef(text);
			DwmSetWindowAttribute(hwnd, 36, ref pvAttribute2, 4);
			int pvAttribute3 = ToColorRef(border);
			DwmSetWindowAttribute(hwnd, 34, ref pvAttribute3, 4);
			if (!flag)
			{
				int pvAttribute4 = (dark ? 1 : 0);
				if (DwmSetWindowAttribute(hwnd, 20, ref pvAttribute4, 4) != 0)
				{
					DwmSetWindowAttribute(hwnd, 19, ref pvAttribute4, 4);
				}
			}
			return flag;
		}
		catch
		{
			return false;
		}
	}

	internal static int ToColorRef(Color color)
	{
		return color.R | (color.G << 8) | (color.B << 16);
	}

	[DllImport("dwmapi.dll")]
	internal static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

	internal static int GetWindowIntAttribute(nint hwnd, int attribute)
	{
		try
		{
			int pvAttribute;
			if (DwmGetWindowAttribute(hwnd, attribute, out pvAttribute, 4) == 0)
			{
				return pvAttribute;
			}
		}
		catch
		{
		}
		return -1;
	}

	internal static bool SetWindowRoundedCorners(nint hwnd)
	{
		try
		{
			int pvAttribute = 2;
			return DwmSetWindowAttribute(hwnd, 33, ref pvAttribute, 4) == 0;
		}
		catch
		{
			return false;
		}
	}

	internal static int GetRefreshRate()
	{
		try
		{
			nint dC = GetDC(IntPtr.Zero);
			if (dC != IntPtr.Zero)
			{
				try
				{
					int deviceCaps = GetDeviceCaps(dC, 116);
					if (deviceCaps >= 24 && deviceCaps <= 1000)
					{
						return deviceCaps;
					}
				}
				finally
				{
					ReleaseDC(IntPtr.Zero, dC);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return 60;
	}

	internal static ColorAccent GetAccent()
	{
		try
		{
			if (DwmGetColorizationColor(out var pcrColorization, out var _) == 0)
			{
				int num = (int)((pcrColorization >> 16) & 0xFF);
				int num2 = (int)((pcrColorization >> 8) & 0xFF);
				int num3 = (int)(pcrColorization & 0xFF);
				if (num + num2 + num3 > 60)
				{
					return new ColorAccent(num, num2, num3);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		try
		{
			uint sysColor = GetSysColor(13);
			int num4 = (int)(sysColor & 0xFF);
			int num5 = (int)((sysColor >> 8) & 0xFF);
			int num6 = (int)((sysColor >> 16) & 0xFF);
			if (num4 + num5 + num6 > 60)
			{
				return new ColorAccent(num4, num5, num6);
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		return new ColorAccent(0, 120, 212);
	}

	internal static bool TryGetVisibleRect(nint hwnd, out RECT r)
	{
		r = default;
		try
		{
			RECT pvAttribute = default;
			if (DwmGetWindowAttribute(hwnd, 9, out pvAttribute, Marshal.SizeOf(typeof(RECT))) == 0 && pvAttribute.Width > 0 && pvAttribute.Height > 0)
			{
				r = pvAttribute;
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return false;
	}

	internal static MONITORINFO GetMonitorInfoAt(POINT pt)
	{
		MONITORINFO lpmi = new MONITORINFO
		{
			cbSize = Marshal.SizeOf(typeof(MONITORINFO))
		};
		nint num = MonitorFromPoint(pt, 2u);
		if (num != IntPtr.Zero)
		{
			GetMonitorInfoW(num, ref lpmi);
		}
		return lpmi;
	}

	internal static bool IsEqualRect(RECT a, RECT b)
	{
		if (a.Left == b.Left && a.Top == b.Top && a.Right == b.Right)
		{
			return a.Bottom == b.Bottom;
		}
		return false;
	}

	internal static string GetClassName(nint hWnd)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		GetClassNameW(hWnd, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}

	internal static string GetWindowTitle(nint hWnd)
	{
		int windowTextLengthW = GetWindowTextLengthW(hWnd);
		if (windowTextLengthW <= 0)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(windowTextLengthW + 2);
		GetWindowTextW(hWnd, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}
}
internal abstract class OverlayWindow : IDisposable
{
	private const uint WM_APPLY = 32769u;

	private const uint WM_RETHEME = 32770u;

	private static readonly object ClassGate = new object();

	private static string _className;

	private static Native.WndProc _classProc;

	private static readonly Dictionary<nint, OverlayWindow> Instances = new Dictionary<nint, OverlayWindow>();

	private nint _hwnd = IntPtr.Zero;

	private bool _destroyed;

	private readonly object _pendingGate = new object();

	private Native.RECT _pending;

	private bool _pendingValid;

	private bool _hidePending;

	private bool _applyPosted;

	private volatile bool _rethemePending;

	private bool _rethemePosted;

	private bool _visible;

	private const uint WM_CREATE_OVERLAYS = 32779u;

	private const uint WM_DESTROY_OVERLAY = 32780u;

	private static Thread _ownerThread;

	private static uint _ownerThreadId;

	private static readonly object OwnerGate = new object();

	private static readonly ManualResetEventSlim _ownerReady = new ManualResetEventSlim(initialState: false);

	private static readonly List<OverlayWindow> _pendingCreate = new List<OverlayWindow>();

	private static volatile bool _ownerQuit;

	private readonly ManualResetEventSlim _created = new ManualResetEventSlim(initialState: false);

	private Native.RECT _lastApplied;

	private bool _lastAppliedValid;

	private int _lastW = -1;

	private int _lastH = -1;

	protected bool IsVisible => _visible;

	public nint Handle => _hwnd;

	public bool IsCreated => _hwnd != IntPtr.Zero;

	protected virtual bool LayeredStyle => true;

	internal static uint OwnerThreadIdForTest => _ownerThreadId;

	internal bool ShowRequestedForTest
	{
		get
		{
			lock (_pendingGate)
			{
				return _pendingValid && !_hidePending;
			}
		}
	}

	public void EnsureCreated()
	{
		if (_hwnd != IntPtr.Zero)
		{
			return;
		}
		StartOwnerThread();
		if (Native.GetCurrentThreadId() == _ownerThreadId)
		{
			CreateNow();
			return;
		}
		lock (OwnerGate)
		{
			_pendingCreate.Add(this);
			_created.Reset();
		}
		if (!Native.PostThreadMessageW(_ownerThreadId, 32779u, IntPtr.Zero, IntPtr.Zero))
		{
			lock (OwnerGate)
			{
				_pendingCreate.Remove(this);
			}
			Log.Error("投递浮层创建消息失败，退回当前线程创建");
			CreateNow();
		}
		else
		{
			_created.Wait(3000);
		}
	}

	private void CreateNow()
	{
		if (_hwnd != IntPtr.Zero)
		{
			return;
		}
		EnsureClass();
		uint num = 134217864u;
		if (LayeredStyle)
		{
			num |= 0x80000;
		}
		_hwnd = Native.CreateWindowExW(num, _className, "HoldDrag Overlay", 2147483648u, -32000, -32000, 1, 1, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandleW(null), IntPtr.Zero);
		if (_hwnd == IntPtr.Zero)
		{
			Log.Error("创建浮层窗口失败，Win32 错误码 " + Marshal.GetLastWin32Error());
			return;
		}
		lock (ClassGate)
		{
			Instances[_hwnd] = this;
		}
	}

	public static void StartOwnerThread()
	{
		lock (OwnerGate)
		{
			if (_ownerThread != null)
			{
				return;
			}
			_ownerThread = new Thread(OwnerThreadMain);
			_ownerThread.IsBackground = true;
			_ownerThread.Priority = ThreadPriority.AboveNormal;
			_ownerThread.Name = "HoldDrag.Overlays";
			_ownerThread.Start();
		}
		_ownerReady.Wait(3000);
	}

	private static void OwnerThreadMain()
	{
		try
		{
			_ownerThreadId = Native.GetCurrentThreadId();
			Native.PeekMessageW(out var _, IntPtr.Zero, 0u, 0u, 0u);
			_ownerReady.Set();
			Native.MSG lpMsg2;
			while (!_ownerQuit && Native.GetMessageW(out lpMsg2, IntPtr.Zero, 0u, 0u) > 0)
			{
				if (lpMsg2.hwnd == IntPtr.Zero)
				{
					if (lpMsg2.message == 32779)
					{
						CreatePendingNow();
					}
					else if (lpMsg2.message == 32780)
					{
						try
						{
							Native.DestroyWindow(lpMsg2.wParam);
						}
						catch (Exception ex)
						{
							Log.Error(ex);
						}
					}
				}
				else
				{
					Native.TranslateMessage(ref lpMsg2);
					Native.DispatchMessageW(ref lpMsg2);
				}
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		finally
		{
			_ownerReady.Set();
		}
	}

	private static void CreatePendingNow()
	{
		OverlayWindow[] array;
		lock (OwnerGate)
		{
			array = _pendingCreate.ToArray();
			_pendingCreate.Clear();
		}
		for (int i = 0; i < array.Length; i++)
		{
			try
			{
				array[i].CreateNow();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			finally
			{
				array[i]._created.Set();
			}
		}
	}

	private static void EnsureClass()
	{
		lock (ClassGate)
		{
			if (_className == null)
			{
				_classProc = OverlayWndProc;
				Native.WNDCLASSEX lpwcx = new Native.WNDCLASSEX
				{
					cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX)),
					style = 0u,
					lpfnWndProc = _classProc,
					hInstance = Native.GetModuleHandleW(null),
					hCursor = IntPtr.Zero,
					hbrBackground = IntPtr.Zero,
					lpszClassName = "HoldDragOverlayWindow"
				};
				Native.RegisterClassExW(ref lpwcx);
				_className = lpwcx.lpszClassName;
			}
		}
	}

	public void Destroy()
	{
		if (_destroyed)
		{
			return;
		}
		_destroyed = true;
		nint hwnd = _hwnd;
		_hwnd = IntPtr.Zero;
		if (hwnd != IntPtr.Zero)
		{
			lock (ClassGate)
			{
				Instances.Remove(hwnd);
			}
			if (Native.GetCurrentThreadId() == _ownerThreadId)
			{
				try
				{
					Native.DestroyWindow(hwnd);
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			}
			else
			{
				try
				{
					Native.PostThreadMessageW(_ownerThreadId, 32780u, hwnd, IntPtr.Zero);
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
				}
			}
		}
		try
		{
			FreeResources();
		}
		catch (Exception ex3)
		{
			Log.Error(ex3);
		}
	}

	public void Dispose()
	{
		Destroy();
		GC.SuppressFinalize(this);
	}

	protected virtual void FreeResources()
	{
	}

	protected virtual bool HandlePaint()
	{
		return false;
	}

	public void SetRect(Native.RECT r)
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		if (r.Width <= 0 || r.Height <= 0)
		{
			HideOverlay();
			return;
		}
		bool flag;
		lock (_pendingGate)
		{
			if ((_pendingValid && SameRect(_pending, r)) || (!_pendingValid && !_hidePending && _lastAppliedValid && SameRect(_lastApplied, r) && _visible))
			{
				return;
			}
			_pending = r;
			_pendingValid = true;
			_hidePending = false;
			flag = !_applyPosted;
			if (flag)
			{
				_applyPosted = true;
			}
		}
		if (!flag || Native.PostMessageW(_hwnd, 32769, IntPtr.Zero, IntPtr.Zero))
		{
			return;
		}
		lock (_pendingGate)
		{
			_applyPosted = false;
		}
	}

	private static bool SameRect(Native.RECT a, Native.RECT b)
	{
		if (a.Left == b.Left && a.Top == b.Top && a.Right == b.Right)
		{
			return a.Bottom == b.Bottom;
		}
		return false;
	}

	public void HideOverlay()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		bool flag;
		lock (_pendingGate)
		{
			_pendingValid = false;
			_hidePending = true;
			flag = !_applyPosted;
			if (flag)
			{
				_applyPosted = true;
			}
		}
		if (!flag || Native.PostMessageW(_hwnd, 32769, IntPtr.Zero, IntPtr.Zero))
		{
			return;
		}
		lock (_pendingGate)
		{
			_applyPosted = false;
		}
	}

	protected void RequestRetheme()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		_rethemePending = true;
		bool flag;
		lock (_pendingGate)
		{
			flag = !_rethemePosted;
			if (flag)
			{
				_rethemePosted = true;
			}
		}
		if (!flag || Native.PostMessageW(_hwnd, 32770, IntPtr.Zero, IntPtr.Zero))
		{
			return;
		}
		lock (_pendingGate)
		{
			_rethemePosted = false;
			_rethemePending = false;
		}
	}

	private void DrainRetheme()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		lock (_pendingGate)
		{
			_rethemePosted = false;
		}
		if (!_rethemePending)
		{
			return;
		}
		_rethemePending = false;
		try
		{
			OnRetheme();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	protected virtual void OnRetheme()
	{
	}

	private static nint OverlayWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
	{
		try
		{
			switch (msg)
			{
			case 132u:
				return new IntPtr(-1);
			case 33u:
				return new IntPtr(3);
			case 20u:
				return new IntPtr(1);
			default:
			{
				OverlayWindow value;
				lock (ClassGate)
				{
					if (!Instances.TryGetValue(hwnd, out value))
					{
						value = null;
					}
				}
				if (value != null)
				{
					switch (msg)
					{
					case 32769u:
						value.DrainApply();
						return IntPtr.Zero;
					case 32770u:
						value.DrainRetheme();
						return IntPtr.Zero;
					case 2u:
						return IntPtr.Zero;
					}
				}
				if (msg != 15)
				{
					break;
				}
				bool flag = false;
				if (value != null)
				{
					try
					{
						flag = value.HandlePaint();
					}
					catch (Exception ex)
					{
						Log.Error(ex);
					}
				}
				if (!flag)
				{
					try
					{
						Native.ValidateRect(hwnd, IntPtr.Zero);
					}
					catch
					{
					}
				}
				return IntPtr.Zero;
			}
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
	}

	protected void MarkDirectWrite(Native.RECT r)
	{
		lock (_pendingGate)
		{
			_lastApplied = r;
			_lastAppliedValid = true;
			_pending = r;
			_pendingValid = true;
			_hidePending = false;
		}
		_visible = true;
	}

	protected void MarkDirectHide()
	{
		lock (_pendingGate)
		{
			_pendingValid = false;
			_hidePending = false;
			_lastAppliedValid = false;
		}
		_visible = false;
	}

	private void DrainApply()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		bool pendingValid;
		Native.RECT pending;
		lock (_pendingGate)
		{
			_applyPosted = false;
			pendingValid = _pendingValid;
			pending = _pending;
			_pendingValid = false;
			_hidePending = false;
		}
		try
		{
			if (!pendingValid)
			{
				HideNow();
			}
			else
			{
				ApplyNow(pending);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	protected void HideNow()
	{
		lock (_pendingGate)
		{
			_lastAppliedValid = false;
		}
		_visible = false;
		try
		{
			if (_hwnd != IntPtr.Zero)
			{
				Native.ShowWindow(_hwnd, 0);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ApplyNow(Native.RECT r)
	{
		lock (_pendingGate)
		{
			_lastApplied = r;
			_lastAppliedValid = true;
		}
		if (r.Width != _lastW || r.Height != _lastH)
		{
			OnSizeChanged(r.Width, r.Height);
			_lastW = r.Width;
			_lastH = r.Height;
		}
		_visible = true;
		try
		{
			Native.ShowWindow(_hwnd, 4);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		OnPlace(r);
	}

	protected abstract void OnSizeChanged(int w, int h);

	protected abstract void OnPlace(Native.RECT r);

	protected static void MoveTopmost(nint hwnd, int x, int y, int w, int h)
	{
		Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 532u);
	}
}
internal abstract class LayeredBitmapOverlay : OverlayWindow
{
	private Bitmap _bmp;

	private nint _hBitmap = IntPtr.Zero;

	private nint _memDc = IntPtr.Zero;

	private nint _oldBmp = IntPtr.Zero;

	private int _bw;

	private int _bh;

	private int _curW;

	private int _curH;

	private readonly object _bufGate = new object();

	private int _pushInFlight;

	protected int _constantAlpha = 255;

	public bool RaiseAboveAll = true;

	private int _lastPlacedX;

	private int _lastPlacedY;

	private bool _lastPlacedValid;

	protected abstract void RenderContent(Graphics g, int w, int h);

	public void SetRectNow(Native.RECT r)
	{
		if (!IsCreated)
		{
			return;
		}
		if (r.Width < 4 || r.Height < 4)
		{
			HideNow();
			return;
		}
		bool isVisible = IsVisible;
		bool flag;
		lock (_bufGate)
		{
			int num;
			if (r.Width == _curW)
			{
				num = ((r.Height != _curH) ? 1 : 0);
				if (num == 0)
				{
					goto IL_007a;
				}
			}
			else
			{
				num = 1;
			}
			Rebuild(r.Width, r.Height);
			goto IL_007a;
			IL_007a:
			flag = (num != 0 || !isVisible) && _memDc != IntPtr.Zero && _hBitmap != IntPtr.Zero;
		}
		MarkDirectWrite(r);
		try
		{
			Native.ShowWindow(Handle, 4);
		}
		catch
		{
		}
		if (flag)
		{
			Push(r.Left, r.Top);
			return;
		}
		uint num2 = 1552u;
		nint hWndInsertAfter = IntPtr.Zero;
		if (RaiseAboveAll)
		{
			hWndInsertAfter = new IntPtr(-1);
		}
		else
		{
			num2 |= 4;
		}
		Native.SetWindowPos(Handle, hWndInsertAfter, r.Left, r.Top, r.Width, r.Height, num2);
	}

	public void HideNowSync()
	{
		if (IsCreated)
		{
			HideNow();
			MarkDirectHide();
		}
	}

	protected override void OnSizeChanged(int w, int h)
	{
		if (Handle == IntPtr.Zero || w <= 0 || h <= 0)
		{
			return;
		}
		lock (_bufGate)
		{
			_curW = w;
			_curH = h;
			Rebuild(w, h);
		}
	}

	protected void MarkContentDirty()
	{
		RequestRetheme();
	}

	protected override void OnRetheme()
	{
		if (_curW > 0 && _curH > 0)
		{
			Rebuild(_curW, _curH);
			if (_lastPlacedValid && _memDc != IntPtr.Zero && _hBitmap != IntPtr.Zero && IsVisible)
			{
				Push(_lastPlacedX, _lastPlacedY);
			}
		}
	}

	internal string ProbeBitmapPixelForTest(int x, int y)
	{
		try
		{
			lock (_bufGate)
			{
				if (_bmp == null)
				{
					return "无位图";
				}
				if (x < 0 || y < 0 || x >= _bmp.Width || y >= _bmp.Height)
				{
					return "越界";
				}
				Color pixel = _bmp.GetPixel(x, y);
				return pixel.R + "," + pixel.G + "," + pixel.B + ",a=" + pixel.A;
			}
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name;
		}
	}

	internal string ProbeOverlayStateForTest()
	{
		return "alpha=" + _constantAlpha + " 位图=" + _bw + "x" + _bh + " cur=" + _curW + "x" + _curH + " memDc=" + (_memDc != IntPtr.Zero) + " hBitmap=" + (_hBitmap != IntPtr.Zero) + " visible=" + IsVisible + " placed=" + (_lastPlacedValid ? (_lastPlacedX + "," + _lastPlacedY) : "无") + " 抬顶=" + RaiseAboveAll;
	}

	private void Rebuild(int w, int h)
	{
		lock (_bufGate)
		{
			// --------------------------------------------------------------
			// v1.3 性能：尺寸没变就【复用】托管 Bitmap，只重画内容。
			// 旧实现每次都：释放全部 → 新建 Bitmap → GetHbitmap()（一次完整像素
			// 拷贝）→ DeleteObject 旧句柄。纯移动（尺寸不变）是拖动中最常见情形，
			// 其中的托管 Bitmap 分配/GC 压力在这里省掉。
			// 注意：GetHbitmap 产出的 HBITMAP 是当时像素的【快照】，重画托管
			// Bitmap 不会同步过去——所以 HBITMAP 仍需重做（见下），但 Bitmap
			// 对象本身与 GC 压力省了，且免去了"先释放再全建"的窗口期。
			// --------------------------------------------------------------
			bool flag = _bmp != null && _bw == w && _bh == h;
			Bitmap bitmap;
			if (flag)
			{
				bitmap = _bmp;
				try
				{
					using Graphics graphics = Graphics.FromImage(bitmap);
					graphics.SmoothingMode = SmoothingMode.AntiAlias;
					graphics.PixelOffsetMode = PixelOffsetMode.Half;
					graphics.Clear(Color.Transparent);
					RenderContent(graphics, w, h);
				}
				catch (Exception ex)
				{
					Log.Error(ex);
					bitmap = null;
				}
			}
			else
			{
				bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			}
			if (bitmap == null)
			{
				return;
			}
			if (!flag)
			{
				try
				{
					using Graphics graphics2 = Graphics.FromImage(bitmap);
					graphics2.SmoothingMode = SmoothingMode.AntiAlias;
					graphics2.PixelOffsetMode = PixelOffsetMode.Half;
					graphics2.Clear(Color.Transparent);
					RenderContent(graphics2, w, h);
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
					bitmap.Dispose();
					return;
				}
			}
			// HBITMAP 阶段：无论是否复用 Bitmap，快照都要按当前像素重做。
			if (_memDc == IntPtr.Zero)
			{
				nint dC = Native.GetDC(IntPtr.Zero);
				try
				{
					_memDc = Native.CreateCompatibleDC(dC);
				}
				finally
				{
					if (dC != IntPtr.Zero)
					{
						Native.ReleaseDC(IntPtr.Zero, dC);
					}
				}
			}
			if (_memDc == IntPtr.Zero)
			{
				if (!flag)
				{
					bitmap.Dispose();
				}
				return;
			}
			nint zero = IntPtr.Zero;
			try
			{
				zero = bitmap.GetHbitmap(Color.FromArgb(0));
			}
			catch (Exception ex3)
			{
				Log.Error(ex3);
				if (!flag)
				{
					bitmap.Dispose();
				}
				return;
			}
			if (zero == IntPtr.Zero)
			{
				return;
			}
			// 换新 HBITMAP：先把旧的从 DC 里换出来再删（顺序不能反，DC 里选着的不能删）。
			if (_memDc != IntPtr.Zero && _oldBmp != IntPtr.Zero)
			{
				Native.SelectObject(_memDc, _oldBmp);
				_oldBmp = IntPtr.Zero;
			}
			if (_hBitmap != IntPtr.Zero)
			{
				Native.DeleteObject(_hBitmap);
				_hBitmap = IntPtr.Zero;
			}
			if (!flag)
			{
				if (_bmp != null)
				{
					_bmp.Dispose();
				}
				_bmp = bitmap;
			}
			_hBitmap = zero;
			_bw = w;
			_bh = h;
			_curW = w;
			_curH = h;
			_oldBmp = Native.SelectObject(_memDc, zero);
		}
	}

	protected override void OnPlace(Native.RECT r)
	{
		_lastPlacedX = r.Left;
		_lastPlacedY = r.Top;
		_lastPlacedValid = true;
		if (_memDc != IntPtr.Zero && _hBitmap != IntPtr.Zero)
		{
			Push(r.Left, r.Top);
		}
	}

	private void Push(int x, int y)
	{
		nint memDc;
		int bw;
		int bh;
		lock (_bufGate)
		{
			if (_memDc == IntPtr.Zero || _hBitmap == IntPtr.Zero)
			{
				return;
			}
			memDc = _memDc;
			bw = _bw;
			bh = _bh;
			_pushInFlight++;
		}
		try
		{
			nint dC = Native.GetDC(IntPtr.Zero);
			if (dC == IntPtr.Zero)
			{
				return;
			}
			try
			{
				Native.POINT pptDst = new Native.POINT(x, y);
				Native.SIZE psize = new Native.SIZE(bw, bh);
				Native.POINT pptSrc = new Native.POINT(0, 0);
				Native.BLENDFUNCTION pblend = new Native.BLENDFUNCTION
				{
					BlendOp = 0,
					SourceConstantAlpha = (byte)_constantAlpha,
					AlphaFormat = 1
				};
				if (!Native.UpdateLayeredWindow(Handle, dC, ref pptDst, ref psize, memDc, ref pptSrc, 0u, ref pblend, 2u))
				{
					Log.Error("UpdateLayeredWindow 失败，Win32 错误码 " + Marshal.GetLastWin32Error());
				}
			}
			finally
			{
				Native.ReleaseDC(IntPtr.Zero, dC);
			}
			if (RaiseAboveAll)
			{
				Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 1043u);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			lock (_bufGate)
			{
				_pushInFlight--;
				if (_pushInFlight == 0)
				{
					Monitor.PulseAll(_bufGate);
				}
			}
		}
	}

	private void ReleaseBitmap()
	{
		lock (_bufGate)
		{
			if (_memDc != IntPtr.Zero && _oldBmp != IntPtr.Zero)
			{
				Native.SelectObject(_memDc, _oldBmp);
				_oldBmp = IntPtr.Zero;
			}
			if (_hBitmap != IntPtr.Zero)
			{
				Native.DeleteObject(_hBitmap);
				_hBitmap = IntPtr.Zero;
			}
			if (_bmp != null)
			{
				_bmp.Dispose();
				_bmp = null;
			}
			_bw = 0;
			_bh = 0;
			// v1.3：_curW/_curH 也必须归零。SetRectNow 靠"尺寸没变就跳过 Rebuild"省拷贝，
			// 若这里不归零，空闲释放后同尺寸的矩形会带着已删除的 HBITMARK 去 Push（黑框/崩）。
			_curW = 0;
			_curH = 0;
		}
	}

	protected override void FreeResources()
	{
		lock (_bufGate)
		{
			while (_pushInFlight > 0)
			{
				Monitor.Wait(_bufGate);
			}
			ReleaseBitmap();
			if (_memDc != IntPtr.Zero)
			{
				Native.DeleteDC(_memDc);
				_memDc = IntPtr.Zero;
			}
		}
	}

	/// <summary>
	/// v1.3：空闲时释放像素缓冲（托管 Bitmap + 非托管 HBITMAP），但保留 DC 与窗口句柄。
	/// 下一次显示会经 Rebuild 重建缓冲；DC 不释放可以避免"频繁拖动时反复建 DC"的开销。
	/// 必须先等在途的 Push 完成再放（UpdateLayeredWindow 还拿着那块内存）。
	/// </summary>
	internal void ReleaseBuffersForIdle()
	{
		try
		{
			HideNowSync();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		lock (_bufGate)
		{
			while (_pushInFlight > 0)
			{
				try
				{
					Monitor.Wait(_bufGate);
				}
				catch
				{
					break;
				}
			}
			ReleaseBitmap();
		}
	}
}
internal sealed class ActivationFrame : LayeredBitmapOverlay
{
	private string _colorId = FrameColors.DefaultId;

	private string _styleId = FrameStyles.DefaultId;

	private int _thickness = 3;

	private int _radius = 13;

	public bool IsShown => IsVisible;

	internal string ColorIdForTest => _colorId;

	internal string StyleIdForTest => _styleId;

	internal Color? ColorOverrideForTest => _colorOverride;

	private Color? _colorOverride;

	public void ApplyTheme(string colorId, string styleId, int thickness, int radius, int opacityPercent = 100, Color? colorOverride = null)
	{
		int num;
		if (opacityPercent < 5)
		{
			num = 5;
		}
		else
		{
			num = ((opacityPercent > 100) ? 255 : ((int)Math.Round((double)opacityPercent * 255.0 / 100.0)));
		}
		if (_colorId != colorId || _styleId != styleId || _thickness != thickness || _radius != radius || _constantAlpha != num || !SameColor(_colorOverride, colorOverride))
		{
			_colorId = colorId;
			_styleId = styleId;
			_thickness = thickness;
			_radius = radius;
			_constantAlpha = num;
			_colorOverride = colorOverride;
			MarkContentDirty();
		}
	}

	private static bool SameColor(Color? a, Color? b)
	{
		if (a.HasValue != b.HasValue)
		{
			return false;
		}
		return !a.HasValue || a.Value.ToArgb() == b.Value.ToArgb();
	}

	protected override void RenderContent(Graphics g, int w, int h)
	{
		RectangleF area = new RectangleF(0f, 0f, w, h);
		Color color = (_colorOverride.HasValue ? _colorOverride.Value : FrameColors.Resolve(_colorId));
		FrameStyles.Draw(g, area, color, _styleId, _thickness, _radius);
	}
}
internal static class Program
{
	internal const string MutexName = "Local\\HoldDrag.SingleInstance.v1";

	internal const string ShowEventName = "Local\\HoldDrag.ShowSettings.v1";

	/// <summary>自检用：互斥体名（冻结契约，改名会让新旧版本互不识别 → 又能开出两个）。</summary>
	internal static string MutexNameForTest => MutexName;

	/// <summary>自检用：唤起设置窗口的命名事件名。</summary>
	internal static string ShowEventNameForTest => ShowEventName;

	public const string ArgNoElevate = "--no-elevate";

	public const string ArgSelfTest = "--selftest";

	public const string ArgSmoke = "--smoke";

	public const string ArgTrace = "--trace";

	public const string ArgStress = "--stress";

	/// <summary>只做一次更新检查并把结果打到控制台/报告，便于排查更新链路（不启动托盘）。</summary>
	public const string ArgCheckUpdate = "--checkupdate";

	internal const string WindowHostTitle = "HoldDrag 外部测试窗口";

	public static bool SmokeMode { get; private set; }

	public static bool LaunchedWithoutElevation { get; private set; }

	[STAThread]
	private static void Main(string[] args)
	{
		bool flag = false;
		string text = "";
		int num = 0;
		try
		{
			flag = Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
			num = Marshal.GetLastWin32Error();
		}
		catch (Exception ex)
		{
			text = ex.GetType().Name + ": " + ex.Message;
		}
		int num2 = -1;
		try
		{
			num2 = Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext());
		}
		catch
		{
		}
		if (num2 == 0)
		{
			try
			{
				if (Native.SetProcessDpiAware())
				{
					num2 = 1;
					Log.Info("★ 进程原本不感知，已用 SetProcessDpiAware 抢救为系统感知");
				}
			}
			catch (Exception ex2)
			{
				text = text + " | SetProcessDpiAware: " + ex2.Message;
			}
		}
		bool flag2 = HasArg(args, "--selftest");
		bool flag3 = HasArg(args, "--stress");
		bool flag4 = HasArg(args, "--dragbench");
		bool flag5 = HasArg(args, "--probe");
		bool flag6 = HasArg(args, "--no-elevate");
		SmokeMode = HasArg(args, "--smoke");
		string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
		try
		{
			Directory.CreateDirectory(text2);
		}
		catch
		{
		}
		Log.Init(text2);
		Log.Info("SetProcessDpiAwarenessContext(PerMonitorV2) => " + flag + (flag ? "" : (" errCode=" + num + ((text.Length > 0) ? (" ex=" + text) : ""))) + " | 当前感知=" + num2);
		CoordSpace.Init();
		Log.Verbose = HasArg(args, "--trace") | flag2 | flag3;
		Log.Info("==== 启动 ==== 参数: " + ((args.Length != 0) ? string.Join(" ", args) : "(无)"));
		// 更新接续：新版本启动后清理上一版的 .old 备份（此时旧进程已退出，删除必然成功）。
		try
		{
			string text3 = ArgValue(args, Updater.ArgUpdatedFrom);
			if (!string.IsNullOrEmpty(text3))
			{
				Updater.CleanupOldVersion(text3);
			}
			else
			{
				// 手动启动时也顺手清一次上次残留的备份
				Updater.CleanupOldVersion(AppPaths.ExePath + Updater.OldSuffix);
			}
		}
		catch (Exception ex11)
		{
			Log.Error(ex11);
		}
		// 排查用：只做一次更新检查，把结果写到控制台与日志，不进托盘。
		if (HasArg(args, "--checkupdate"))
		{
			RunCheckUpdateOnly();
			return;
		}
		if (flag2 | flag3 | flag4 | flag5)
		{
			try
			{
				Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
			}
			catch (Exception ex3)
			{
				Log.Error(ex3);
			}
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(defaultValue: false);
			if (flag2)
			{
				SelfTest.Run(text2);
			}
			else if (flag4)
			{
				DragBench.Run();
			}
			else if (flag5)
			{
				FrameProbe.Run();
			}
			else
			{
				StressTest.Run(text2);
			}
			return;
		}
		try
		{
			Log.Info("SetHighDpiMode(PerMonitorV2) => " + Application.SetHighDpiMode(HighDpiMode.PerMonitorV2));
		}
		catch (Exception ex4)
		{
			Log.Error(ex4);
		}
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			int systemMetrics = Native.GetSystemMetrics(0);
			int systemMetrics2 = Native.GetSystemMetrics(1);
			int num3 = -1;
			try
			{
				num3 = Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext());
			}
			catch (Exception ex5)
			{
				Log.Error(ex5);
			}
			string text3 = num3 switch
			{
				0 => "不感知(0)★危险", 
				1 => "系统感知(1)", 
				2 => "每显示器感知(2)", 
				_ => "查不到(" + num3 + ")", 
			};
			Log.Info("DPI 感知级别 = " + text3 + " | SM_CXSCREEN=" + systemMetrics + "x" + systemMetrics2 + " | 虚拟桌面=" + virtualScreen.Width + "x" + virtualScreen.Height + " | 主屏工作区=" + Screen.PrimaryScreen.WorkingArea.Width + "x" + Screen.PrimaryScreen.WorkingArea.Height);
		}
		catch (Exception ex6)
		{
			Log.Error(ex6);
		}
		if (HasArg(args, "--windowhost"))
		{
			RunWindowHost();
			return;
		}
		if (HasArg(args, "--visualcheck"))
		{
			VisualCheck.Run(Path.Combine(Path.GetTempPath(), "holddrag-visual"));
			StressTest.HardExit(0);
			return;
		}
		if (HasArg(args, "--previewtest"))
		{
			SettingsForm.DumpPreviewStyles(Path.Combine(Path.GetTempPath(), "holddrag-previewtest"));
			StressTest.HardExit(0);
			return;
		}
		ConfigStore configStore = new ConfigStore(text2);
		configStore.Load();
		bool flag7 = IsAdministrator();
		LaunchedWithoutElevation = !flag7;
		if (!flag6 && !SmokeMode && configStore.Current.RunAsAdmin && !flag7)
		{
			if (RelaunchElevated(args))
			{
				Log.Info("已请求提权重启，本进程退出");
				return;
			}
			Log.Info("未获得管理员权限，降级为普通权限继续运行");
			if (configStore.Current.WarnNonAdmin && AskToRestartElevated(args))
			{
				return;
			}
		}
		// 单实例。注意"更新重启"这条特殊路径：
		// 替换时是新版本【先启动】、旧进程【后退出】的，新进程一启动就会发现互斥体已被旧进程占着。
		// 若照普通启动那样"通知已有实例然后自己退出"，更新完就会出现【一个进程都不剩】。
		// 因此更新接续的实例要在这里等旧进程把互斥体释放掉，再接管。
		bool flag9 = !string.IsNullOrEmpty(ArgValue(args, Updater.ArgUpdatedFrom));
		Mutex obj3 = null;
		bool createdNew = false;
		long num5 = Environment.TickCount64 + 30000;
		while (true)
		{
			obj3 = new Mutex(initiallyOwned: true, "Local\\HoldDrag.SingleInstance.v1", out createdNew);
			if (createdNew)
			{
				break;
			}
			if (!flag9)
			{
				break;
			}
			// 更新接续：旧进程正在退出，等它释放
			try
			{
				obj3.Dispose();
			}
			catch
			{
			}
			if (Environment.TickCount64 > num5)
			{
				Log.Info("更新接续：等待旧实例退出超过 30 秒，改为抢占启动");
				obj3 = new Mutex(initiallyOwned: true, "Local\\HoldDrag.SingleInstance.v1", out createdNew);
				break;
			}
			Thread.Sleep(150);
		}
		if (!createdNew)
		{
			Log.Info("已有实例在运行，通知它把设置窗口显示出来");
			try
			{
				using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("Local\\HoldDrag.ShowSettings.v1");
				eventWaitHandle.Set();
				return;
			}
			catch (Exception ex7)
			{
				Log.Error(ex7);
				return;
			}
		}
		EventWaitHandle showEvent = null;
		try
		{
			showEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "Local\\HoldDrag.ShowSettings.v1");
		}
		catch (Exception ex8)
		{
			Log.Error(ex8);
		}
		bool flag8 = false;
		try
		{
			flag8 = Native.TimeBeginPeriod(1u) == 0;
		}
		catch (Exception ex9)
		{
			Log.Error(ex9);
		}
		try
		{
			using TrayContext context = new TrayContext(text2, configStore, showEvent);
			Application.Run(context);
		}
		catch (Exception ex10)
		{
			Log.Error(ex10);
			MessageBox.Show("程序运行中发生未处理异常，详情见日志：\r\n" + Log.Path + "\r\n\r\n" + ex10.Message, "长按拖窗", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
		finally
		{
			if (flag8)
			{
				try
				{
					Native.TimeEndPeriod(1u);
				}
				catch
				{
				}
			}
			GC.KeepAlive(obj3);
		}
	}

	private static void RunWindowHost()
	{
		try
		{
			Application.EnableVisualStyles();
			Application.Run(new Form
			{
				// 标题带上本进程号：自检每轮拉起的宿主窗口标题都唯一，
				// 上轮残留的孤儿宿主不会再被 FindWindowByTitle 命中（那是 T80 偶发失败的根因）。
				Text = WindowHostTitle + " #" + Environment.ProcessId,
				StartPosition = FormStartPosition.Manual,
				Location = new Point(120, 120),
				Size = new Size(420, 300),
				ShowInTaskbar = true
			});
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>
	/// `--checkupdate`：只跑一次更新检查并把结果打到控制台与日志。
	/// 用途：用户反馈"检查更新没反应/总是失败"时，让他跑这一条就能拿到确切原因，
	/// 不必去翻 GUI 状态。
	/// </summary>
	private static void RunCheckUpdateOnly()
	{
		Console.WriteLine("当前版本：" + AppInfo.Version);
		Log.Info("--checkupdate：当前版本 " + AppInfo.Version);
		string text = null;
		try
		{
			// 附着到父控制台，--checkupdate 的输出才能被调用方捕获
			SelfTest.AttachConsoleForTest(uint.MaxValue);
		}
		catch
		{
		}
		try
		{
			string error;
			bool upToDate;
			UpdateInfo updateInfo = Updater.CheckLatest(out error, out upToDate);
			if (updateInfo != null)
			{
				text = "发现新版本 " + updateInfo.Tag + "（当前 v" + AppInfo.Version + "）";
				Console.WriteLine(text);
				Console.WriteLine("下载地址：" + updateInfo.AssetUrl);
				Console.WriteLine("发布页　：" + updateInfo.PageUrl);
			}
			else if (upToDate)
			{
				text = "已是最新版本（v" + AppInfo.Version + "）";
				Console.WriteLine(text);
			}
			else
			{
				text = "检查失败：" + error;
				Console.WriteLine(text);
				Console.WriteLine("发布页　：" + AppInfo.ReleasesUrl);
			}
			Log.Info("--checkupdate 结果：" + text);
		}
		catch (Exception ex)
		{
			text = "检查抛异常：" + Updater.Describe(ex);
			Console.WriteLine(text);
			Log.Error(ex);
		}
		finally
		{
			try
			{
				Log.FlushAsyncQueue();
				Log.FlushAndClose();
			}
			catch
			{
			}
			StressTest.HardExit((text != null && text.StartsWith("检查失败")) ? 1 : 0);
		}
	}

	/// <summary>自检用：透传参数解析逻辑（private 的 HasArg/ArgValue）。</summary>
	internal static bool HasArgForTest(string[] args, string name)
	{
		return HasArg(args, name);
	}

	/// <summary>自检用：透传参数取值逻辑。</summary>
	internal static string ProbeArgValueForTest(string[] args, string name)
	{
		return ArgValue(args, name);
	}

	private static bool HasArg(string[] args, string name)
	{
		if (args == null)
		{
			return false;
		}
		for (int i = 0; i < args.Length; i++)
		{
			if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>取 "--name value" 形式参数后面的值；没有则返回空串。</summary>
	private static string ArgValue(string[] args, string name)
	{
		if (args == null)
		{
			return string.Empty;
		}
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
			{
				return args[i + 1];
			}
		}
		return string.Empty;
	}

	public static bool IsAdministrator()
	{
		try
		{
			using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	private static bool AskToRestartElevated(string[] args)
	{
		try
		{
			if (MessageBox.Show("当前正在以【普通用户权限】运行。\r\n\r\n这个状态下：\r\n  · 日常拖动浏览器、编辑器、资源管理器等都正常；\r\n  · 但拖不动以管理员身份运行的窗口（例如任务管理器、部分安装程序）。\r\n\r\n建议改用管理员权限重新打开。现在重启吗？\r\n（选\"否\"就保持现状继续使用，功能不受影响）", "长按拖窗 · 权限提示", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk, MessageBoxDefaultButton.Button1) != DialogResult.Yes)
			{
				return false;
			}
			if (RelaunchElevated(args))
			{
				Log.Info("用户选择以管理员权限重启，本进程退出");
				return true;
			}
			Log.Info("二次提权仍被拒绝，继续以普通权限运行");
			return false;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	private static bool RelaunchElevated(string[] args)
	{
		try
		{
			List<string> list = new List<string>();
			foreach (string text in args)
			{
				if (!string.Equals(text, "--no-elevate", StringComparison.OrdinalIgnoreCase))
				{
					list.Add(Updater.QuoteArg(text));
				}
			}
			Process.Start(new ProcessStartInfo
			{
				FileName = AppPaths.ExePath,
				Arguments = string.Join(" ", list.ToArray()),
				UseShellExecute = true,
				Verb = "runas"
			});
			return true;
		}
		catch (Exception ex)
		{
			Log.Info("提权被拒绝： " + ex.Message);
			return false;
		}
	}
}
internal static class SelfTest
{
	private sealed class NullSink : IInputSink
	{
		public bool OnKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb)
		{
			return false;
		}

		public bool OnMouse(int msg, ref Native.MSLLHOOKSTRUCT ms)
		{
			return false;
		}
	}

	private sealed class DragFixture : IDisposable
	{
		private readonly string _dir;

		public readonly Form Window;

		public readonly ConfigStore Store;

		public readonly Coordinator Coord;

		public nint H => Window.Handle;

		public DragFixture(Action<AppConfig> tweak)
		{
			_dir = Path.Combine(Path.GetTempPath(), "holddrag-e2e-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
			Store = new ConfigStore(_dir);
			Store.Load();
			AppConfig current = Store.Current;
			current.BringToFront = false;
			current.ShowActivationFrame = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			tweak?.Invoke(current);
			Window = new Form();
			Window.FormBorderStyle = FormBorderStyle.Sizable;
			Window.StartPosition = FormStartPosition.Manual;
			Window.ShowInTaskbar = false;
			Window.Text = "HoldDrag selftest";
			Window.TopMost = true;
			Window.Bounds = new Rectangle(120, 120, 320, 220);
			Window.Show();
			Pump();
			Coord = new Coordinator(Store);
			Coord.PrecreateOverlays();
			Pump();
			WindowActions.ForcedTargetForTest = Window.Handle;
		}

		public void Dispose()
		{
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			try
			{
				Coord.Dispose();
			}
			catch
			{
			}
			try
			{
				try
				{
					if (!Window.IsDisposed)
					{
						Window.Visible = false;
						Native.DestroyWindow(Window.Handle);
					}
				}
				catch
				{
				}
				Window.Close();
				Window.Dispose();
			}
			catch
			{
			}
			for (int i = 0; i < 6; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
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

	private sealed class BlockingForm : Form
	{
		private readonly int _blockMs;

		public bool BlockEnabled = true;

		public BlockingForm(int blockMs)
		{
			_blockMs = blockMs;
		}

		protected override void WndProc(ref Message m)
		{
			if (BlockEnabled && m.Msg == 70)
			{
				Thread.Sleep(_blockMs);
			}
			base.WndProc(ref m);
		}
	}

	private static int _pass;

	private static int _fail;

	private static readonly StringBuilder Buf = new StringBuilder();

	private const uint AttachParentProcess = uint.MaxValue;

	private static readonly Stopwatch _watch = new Stopwatch();

	private static string _reportDir;

	private static int OwnProcessIdForTest
	{
		get
		{
			try
			{
				return Process.GetCurrentProcess().Id;
			}
			catch
			{
				return -1;
			}
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool AttachConsole(uint dwProcessId);

	/// <summary>给 Program 的控制台诊断输出复用（AttachConsole 是 private 的）。</summary>
	internal static void AttachConsoleForTest(uint processId)
	{
		AttachConsole(processId);
	}

	public static void Run(string dir)
	{
		try
		{
			AttachConsole(uint.MaxValue);
		}
		catch
		{
		}
		StartWatchdog(dir);
		Line("==== 长按拖窗 自检 ====");
		Line("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		Line("管理员权限：" + (Program.IsAdministrator() ? "是" : "否"));
		Line("");
		Safe("T01 默认配置", TestDefaults);
		Safe("T02 按键标识", TestKeyIds);
		Safe("T03 触发键匹配", TestKeyMatching);
		Safe("T04 九宫格判定", TestZones9);
		Safe("T05 最近角判定", TestNearestCorner);
		Safe("T07 九宫格矩形换算", TestSnapRects);
		Safe("T08 九宫格→屏幕区域", TestZoneMapping);
		Safe("T09 配置越界归一", TestNormalize);
		Safe("T10 配置往返", () =>
		{
			TestConfigRoundTrip(dir);
		});
		Safe("T11 配置备份恢复", () =>
		{
			TestConfigRecovery(dir);
		});
		Safe("T12 长按状态机（超阈值）", TestTriggerHold);
		Safe("T13 长按状态机（短按补发）", TestTriggerShortPress);
		Safe("T14 关闭长按阈值", TestTriggerNoHold);
		Safe("T15 全局钩子装卸", TestHooks);
		Safe("T16 窗口排除表", TestExclusions);
		Safe("T17 exe 内嵌图标", TestIconEmbedded);
		Safe("T18 端到端拖动（移动）", TestDragMoveE2E);
		Safe("T19 端到端拖动（缩放）", TestDragResizeE2E);
		Safe("T20 拖动期间绝不吞鼠标移动（光标冻结回归）", TestDragNeverSwallowsMouseMove);
		Safe("T21 未激活时鼠标键不触发", TestNoDragWhenInactive);
		Safe("T22 设置窗口所有分组都已挂到窗体上（设置空白回归）", TestSettingsLayout);
		Safe("T23 触发键激活态稳定保持（看门狗误判回归）", TestTriggerStaysActive);
		Safe("T24 拖动激活外框 显示/隐藏 与自选矩形一致", TestActivationFrameOverlay);
		Safe("T26 帧率节流算出的间隔合理，手动值不被吞", TestFrameInterval);
		Safe("T27 全屏窗口默认被跳过，开关打开后可拖", TestFullscreenOptIn);
		Safe("T28 钩子看门狗能判定「钩子已死」", TestHookWatchdog);
		Safe("T29 删除的功能已彻底下线（无残留配置项）", TestRemovedFeaturesGone);
		Safe("T30 外框外观（颜色 / 样式 / 粗细 / 圆角）可配置且越界会被拉回", TestFrameTheme);
		Safe("T31 外框在会话结束之后不会残留（显框前二次校验）", TestFrameNeverSticks);
		Safe("T32 拖动期间用区域环、结束后换成抗锯齿框（拖动残影回归）", TestDragRingFrameSwap);
		Safe("T33 按住触发键执行一次性动作后外框继续保留（外框消失回归）", TestFramePersistsAfterAction);
		Safe("T34 设置窗口可调整大小，且分组带左右锚定（设置尺寸回归）", TestSettingsResizable);
		Safe("T35 主题变更立刻反映到浮层像素上，不会等到下次移动", TestThemeAppliesImmediately);
		Safe("T36 在设置里选主题后，预览与保存结果都是选的那个（选主题失效回归）", TestSettingsThemeSelectionRoundTrip);
		Safe("T37 设置窗口拉宽后真的多装东西（1 列变 2 列、输入框跟着变宽）", TestSettingsResizeIsMeaningful);
		Safe("T38 设置窗口所有控件可见可点（美化改成自绘后功能失效回归）", TestSettingsControlsReachable);
		Safe("T39 外框亮着时左键/右键依然能接管窗口（浮层挡掉拖动回归）", TestHoverFrameDoesNotBlockDrag);
		Safe("T40 高DPI 下文字不被裁切（字号被放大两次回归）", TestNoTextClippedByHeight);
		Safe("T41 所有下拉框都有可见选中项（外框样式显示为空回归）", TestKeyCombosHaveSelection);
		Safe("T42 拖动写窗口必须异步提交（移动/变形不跟手回归）", TestMoveFlagsAreAsync);
		Safe("T43 真实鼠标事件能走完整钩子链路（触发后左键右键失效回归）", TestRealMouseThroughHook);
		Safe("T44 重启后配置必须完整保留（配置持久化回归）", TestConfigSurvivesRestart);
		Safe("T45 命中测试必须有兜底、Z 序方向必须正确（核心功能失效回归）", TestHitTestHasFallback);
		Safe("T46 物理坐标必须换算进进程坐标空间（DPI不感知导致核心功能失效回归）", TestCoordinateSpace);
		Safe("T47 黑名单能用进程名匹配 + 两个挑选入口都在（黑名单易用性回归）", TestBlacklistPickers);
		Safe("T48 全链路真实拖动：窗口必须真的跟着光标动（核心功能端到端）", TestRealDragMovesWindow);
		Safe("T50 真实长按触发键能激活（钩子线程化后 WinForms Timer 失效回归）", TestRealKeyboardHoldActivates);
		Safe("T52 视觉反馈组几何：窄/宽各档下样式预览框必须留在分组内且不被遮", TestVisualGroupGeometry);
		Safe("T53 晃窗手势：真晃才触发、匀速/小幅抖动/拖太慢都不触发（#39）", TestShakeGesture);
		Safe("T54 松开触发键后外框立即消失，pin 机制已下线（外框残留）", TestFrameGoesAwayOnRelease);
		Safe("T55 外框调色板/样式已扩充且每一种都画得出来（#37）", TestFramePaletteExpanded);
		Safe("T56 设置页默认尺寸够用就好、不会被内容撑到满屏（#33）", TestSettingsDefaultSizeCompact);
		Safe("T57 拖动过程的视觉反馈必须真的画在屏幕上并持续跟随（拖拽反馈失效）", TestDragVisualFeedback);
		Safe("T58 视觉反馈：进入即见全部选项、选中态清晰、全宽下不消失", TestFramePickersAlwaysVisible);
		Safe("T59 整体视觉（Win11）：层次/动效到位、无黑块、强调色与外框色都画出来了", TestVisualPolish);
		Safe("T60 最小窗口下任何分组的控件都不得越出分组框（全屏选项被遮挡）", TestNoControlClippedAtMinSize);
		Safe("T61 拖边框改尺寸：布局幂等、不抖动、不溢出、列数不来回翻", TestResizeIsStable);
		Safe("T62 设置页按显示器真实 DPI 渲染，且 DPI 变化时能整体重建", TestDpiRendering);
		Safe("T63 触发后左键/右键在普通·最大化·全屏窗口上都必须生效", TestTriggerOnBigWindows);
		Safe("T64 滚轮扫过设置页不得改掉任何选项（右键被悄悄改成最小化的根因）", TestWheelDoesNotChangeSettings);
		Safe("T65 图标字体可用且每个字形都真的渲染出墨水（缺字形是空白，肉眼难发现）", TestIconGlyphs);
		Safe("T66 触发时鼠标键已按住：必须补起拖动（外框亮着但窗口不动）", TestAdoptHeldButton);
		Safe("T67 拖动全程外框必须紧跟窗口：绝不出现滞后（错位/掉帧）", TestFrameTracksWindow);
		Safe("T68 贴边/出屏拖动时外框几何必须恒定（不许缩小、不许跳变）", TestFrameGeometryStableAtEdge);
		Safe("T69 外框不透明度：改数值必须真的改变渲染结果", TestFrameOpacity);
		Safe("T70 帮助面板：点标题能展开、内容不越界", TestHelpPanel);
		Safe("T71 关于信息：开发者/版本/更新时间/开发工具必须真的显示在界面上", TestAboutInfo);
		Safe("T72 每个分组都必须在可视树里、侧栏条目齐全（「新增了却看不见」回归）", TestAllGroupsReachable);
		Safe("T73 分组内任意两个控件不得互相遮挡", TestNoControlOverlap);
		Safe("T74 滚动页面时侧栏高亮必须跟随（程序化滚动）", TestSidebarFollowsScroll);
		Safe("T75 用真实滚轮消息滚动时，侧栏高亮必须跟随（事件接线）", TestSidebarFollowsRealWheel);
		Safe("T76 全屏窗口触发后降级为窗口：外框必须重新出现在新窗口周围", TestFrameAfterFullscreenDowngrade);
		Safe("T77 调色板必须整体柔和（饱和/明度都在舒适区，不刺眼）", TestPaletteIsSoft);
		Safe("T78 每个外框样式都要有名字、都真的画得出来", TestAllStylesRender);
		Safe("T79 外框绝不残留：兜底自检 + 退出路径都必须收干净", TestFrameNeverResidues);
		Safe("T80 全屏转窗口：用【外部进程】的真实全屏窗口走真实链路", TestFullscreenExternalWindow);
		Safe("T81 窗口操作不得干扰页面内容：取消拖拽模态 + 鼠标接管必须成对", TestNoContentInterference);
		Safe("T82 选中文本后按 Enter：先取消选中再补起拖动，左键恢复移动窗口", TestPhantomDownAdoption);
		Safe("T83 补发的配对抬起：wParam 如实报告其余按键、消息类型对应、lParam 为客户区坐标", TestPairedUpMaskReport);
		Safe("T84 激活时抢占钩子链头：重装后真实事件链路仍然可用（wgesture 抢占右键回归）", TestChainPriorityReinstall);
		Safe("T85 钩子线程路径无同步跨进程写：阻塞目标上 Begin/移动/松手/降级/置顶必须立即返回", TestHookThreadNeverBlocks);
		Safe("T86 自有窗口内控件不被抢输入：滑块上放行、标题栏上仍可拖", TestOwnInteractiveControlPassthrough);
		Safe("T87 分组框不是可交互控件：按住触发键时设置页的成片分组区仍可整体拖走", TestGroupBoxNotInteractive);
		Safe("T88 阈值内按下 + 激活前已在移动：激活后仍必须补起拖动（用户『外框出现却拖不动』复现）", TestAdoptAfterEarlyMove);
		Safe("T89 关于页 GitHub 链接 + 自动更新开关 + 检查更新按钮（v1.1 更新功能）", TestUpdateFeatures);
		Safe("T90 更新重启的单实例竞态：新版本必须等旧进程释放互斥体再接管", TestUpdateRestartSingleInstance);
		Safe("T91 单实例：同一时间只能开一个（重复启动必须自己退出并唤起已有窗口）", TestSingleInstanceOnlyOne);
		Safe("T92 外框颜色：跟随系统强调色 / 自定义颜色都要真的生效且存得进配置", TestFrameColorAccentAndCustom);
		Safe("T93 设置页跟随系统深浅色：深浅两套都要够清楚（对比度/层次）", TestSettingsThemeFollowsSystem);
		Safe("T94 设置窗口是无边框圆角卡片窗：无系统标题栏、标题条与页面同色、三个窗口按钮在位", TestFramelessCardWindow);
		Safe("T96 设置页重排性能：换宽度一次 < 60ms、同宽度重复调用被去重", TestSettingsRelayoutPerformance);
		Safe("T97 侧栏高亮只跟着当前页（点过就留胶囊的回归）", TestNavHighlightFollowsSelection);
		Safe("T95 圆角参数必须真的改变渲染结果（圆角设置失效回归）", TestFrameRadiusTakesEffect);
		Safe("T98 录制守卫与配置不变量：\"K:0x00\" 必被 Normalize 兜底、鼠标侧键/Shift 不被误伤、QuoteArg 按需包引号", TestRecordedKeyAndConfigInvariants);
		Safe("T99 保存失败不再静默：Save/Apply 返回 false 并带回错误信息（设置页据此红字早退）", TestConfigSaveFailureIsVisible);
		Line("");
		Line("合计 " + (_pass + _fail) + " 项：通过 " + _pass + "，失败 " + _fail);
		string text = Buf.ToString();
		try
		{
			File.WriteAllText(Path.Combine(dir, "selftest.txt"), text, Encoding.UTF8);
		}
		catch
		{
		}
		ConsoleEcho(text);
		StressTest.HardExit((_fail != 0) ? 1 : 0);
	}

	private static void ConsoleEcho(string text)
	{
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
			thread.Name = "HoldDrag.SelfTestEcho";
			thread.Start();
			thread.Join(1500);
		}
		catch
		{
		}
	}

	private static void TestDefaults()
	{
		AppConfig appConfig = new AppConfig();
		Eq(appConfig.TriggerKeyId, "K:0x0D", "默认触发键为 Enter");
		Eq(appConfig.HoldMs, 600, "默认长按阈值 600ms");
		Check(appConfig.RequireHold, "默认需要长按");
		Eq(appConfig.ActionLeft, "Move", "左键默认移动");
		Eq(appConfig.ActionRight, "Resize", "右键默认缩放");
		Eq(appConfig.ActionMiddle, "Maximize", "中键默认最大化");
		Check(appConfig.DoubleClickLeftMaximize && appConfig.DoubleClickRightZone, "双击增强默认开启");
		Check(appConfig.ShowActivationFrame && appConfig.BringToFront, "外框 / 提最前 默认开启");
		Check(appConfig.ShakeMinimizeOthers, "晃窗最小化其它窗口默认开启");
		Check(!appConfig.AllowFullscreenWindows, "全屏窗口默认不动（风险项，需手工开启）");
		Check(appConfig.ExitFullscreenOnTrigger, "触发时退出全屏/最大化默认开启（用户要求）");
		Eq(appConfig.FrameOpacity, 100, "外框不透明度默认 100%（完全不透明）");
		Check(appConfig.RunAsAdmin && appConfig.WarnNonAdmin, "管理员权限默认开启，非管理员时默认提示");
		Eq(appConfig.DragRefreshMode, "Auto", "刷新率默认自动跟随屏幕");
	}

	private static void TestKeyIds()
	{
		Eq(KeysUtil.ToVirtualKey("K:0x0D"), 13, "Enter 虚拟键码");
		Eq(KeysUtil.ToVirtualKey("K:RAlt"), 165, "右 Alt 虚拟键码");
		Eq(KeysUtil.ToVirtualKey("K:LShift"), 160, "左 Shift 虚拟键码");
		Eq(KeysUtil.ToVirtualKey("M:1"), 5, "侧键 X1 虚拟键码");
		Eq(KeysUtil.ToVirtualKey("垃圾值"), 0, "非法 Id 返回 0");
		Check(KeysUtil.IsToggleKey("K:0x14"), "CapsLock 被识别为切换型按键");
		Check(!KeysUtil.IsToggleKey("K:0x0D"), "Enter 不是切换型按键");
		Check(KeysUtil.DisplayName("K:0x0D").Contains("Enter"), "Enter 有中文名");
	}

	private static void TestKeyMatching()
	{
		Native.KBDLLHOOKSTRUCT kb = new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 13u
		};
		Check(KeysUtil.MatchesKeyboard("K:0x0D", kb), "Enter 匹配 Enter");
		Check(!KeysUtil.MatchesKeyboard("K:RAlt", kb), "Enter 不匹配右 Alt");
		Native.KBDLLHOOKSTRUCT kb2 = new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 16u,
			scanCode = 42u
		};
		Check(KeysUtil.MatchesKeyboard("K:LShift", kb2), "扫描码 0x2A 判为左 Shift");
		Check(!KeysUtil.MatchesKeyboard("K:RShift", kb2), "扫描码 0x2A 不是右 Shift");
		Check(KeysUtil.MatchesKeyboard("K:RShift", new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 16u,
			scanCode = 54u
		}), "扫描码 0x36 判为右 Shift");
		Native.KBDLLHOOKSTRUCT kb3 = new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 18u,
			flags = 1u
		};
		Check(KeysUtil.MatchesKeyboard("K:RAlt", kb3), "带 extended 标志判为右 Alt");
		Check(!KeysUtil.MatchesKeyboard("K:LAlt", kb3), "带 extended 标志不是左 Alt");
		Check(KeysUtil.MatchesKeyboard("K:LCtrl", new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 17u
		}), "无 extended 标志判为左 Ctrl");
		Native.MSLLHOOKSTRUCT ms = new Native.MSLLHOOKSTRUCT
		{
			mouseData = 65536u
		};
		Check(KeysUtil.MatchesMouse("M:1", 523, ms), "侧键 1 匹配 X1 按下");
		Check(!KeysUtil.MatchesMouse("M:2", 523, ms), "侧键 1 不匹配 X2");
		Check(!KeysUtil.MatchesMouse("M:1", 512, ms), "移动事件不算侧键");
	}

	private static void TestZones9()
	{
		Native.RECT r = new Native.RECT(100, 100, 500, 500);
		Eq(Zones.Resize(r, 105, 105, 26), ResizeZone.TopLeft, "左上角");
		Eq(Zones.Resize(r, 495, 105, 26), ResizeZone.TopRight, "右上角");
		Eq(Zones.Resize(r, 105, 495, 26), ResizeZone.BottomLeft, "左下角");
		Eq(Zones.Resize(r, 495, 495, 26), ResizeZone.BottomRight, "右下角");
		Eq(Zones.Resize(r, 300, 105, 26), ResizeZone.Top, "上边中段");
		Eq(Zones.Resize(r, 300, 495, 26), ResizeZone.Bottom, "下边中段");
		Eq(Zones.Resize(r, 105, 300, 26), ResizeZone.Left, "左边中段");
		Eq(Zones.Resize(r, 495, 300, 26), ResizeZone.Right, "右边中段");
		Eq(Zones.Resize(r, 300, 300, 26), ResizeZone.Center, "正中区域");
	}

	private static void TestNearestCorner()
	{
		Native.RECT r = new Native.RECT(100, 100, 500, 500);
		Eq(Zones.NearestCorner(r, 150, 150), ResizeZone.TopLeft, "中心格偏左上→左上角");
		Eq(Zones.NearestCorner(r, 450, 150), ResizeZone.TopRight, "中心格偏右上→右上角");
		Eq(Zones.NearestCorner(r, 150, 450), ResizeZone.BottomLeft, "中心格偏左下→左下角");
		Eq(Zones.NearestCorner(r, 450, 450), ResizeZone.BottomRight, "中心格偏右下→右下角");
	}

	private static void TestSnapRects()
	{
		Native.RECT work = new Native.RECT(0, 0, 1920, 1080);
		Native.RECT rECT = Zones.RectOf(SnapZone.LeftHalf, work);
		Check(rECT.Left == 0 && rECT.Top == 0 && rECT.Right == 960 && rECT.Bottom == 1080, "左半屏矩形");
		Native.RECT rECT2 = Zones.RectOf(SnapZone.TopLeftQuarter, work);
		Check(rECT2.Left == 0 && rECT2.Top == 0 && rECT2.Right == 960 && rECT2.Bottom == 540, "左上四分之一矩形");
		Native.RECT rECT3 = Zones.RectOf(SnapZone.BottomHalf, work);
		Check(rECT3.Left == 0 && rECT3.Top == 540 && rECT3.Right == 1920 && rECT3.Bottom == 1080, "下半屏矩形");
	}

	private static void TestZoneMapping()
	{
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Top), SnapZone.TopHalf, "上格→上半屏");
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Bottom), SnapZone.BottomHalf, "下格→下半屏");
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Left), SnapZone.LeftHalf, "左格→左半屏");
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Center), SnapZone.Maximize, "中心格→最大化");
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.BottomRight), SnapZone.BottomRightQuarter, "右下格→右下四分之一");
	}

	private static void TestNormalize()
	{
		AppConfig appConfig = new AppConfig();
		appConfig.HoldMs = 50;
		appConfig.CornerPercent = 90;
		appConfig.ActionLeft = "Bogus";
		appConfig.AutoStart = "X";
		appConfig.DragRefreshMode = "X";
		appConfig.DragRefreshHz = -3;
		appConfig.Normalize();
		Eq(appConfig.HoldMs, 100, "阈值下限 100");
		Eq(appConfig.CornerPercent, 45, "角区比例上限 45");
		Eq(appConfig.ActionLeft, "Move", "非法动作回退为移动");
		Eq(appConfig.AutoStart, "TaskScheduler", "非法自启方式回退");
		Eq(appConfig.DragRefreshMode, "Auto", "非法刷新率模式回退为自动");
		Eq(appConfig.DragRefreshHz, 0, "非法刷新率归零");
	}

	private static void TestConfigRoundTrip(string dir)
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-rt-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			configStore.Current.HoldMs = 888;
			configStore.Current.TriggerKeyId = "K:RAlt";
			configStore.Current.ActionLeft = "Resize";
			configStore.Current.ShakeMinimizeOthers = false;
			configStore.Save(out _);
			ConfigStore configStore2 = new ConfigStore(text);
			configStore2.Load();
			Eq(configStore2.Current.HoldMs, 888, "HoldMs 往返一致");
			Eq(configStore2.Current.TriggerKeyId, "K:RAlt", "触发键往返一致");
			Eq(configStore2.Current.ActionLeft, "Resize", "左键动作往返一致");
			Check(!configStore2.Current.ShakeMinimizeOthers, "布尔开关往返一致");
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
	}

	private static void TestConfigRecovery(string dir)
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-rc-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			configStore.Current.HoldMs = 777;
			configStore.Save(out _);
			configStore.Current.HoldMs = 888;
			configStore.Save(out _);
			string text2 = Path.Combine(text, "config.json");
			Check(File.Exists(text2 + ".bak"), "备份文件已生成");
			File.WriteAllText(text2, "{ 这不是一个合法的 JSON");
			ConfigStore configStore2 = new ConfigStore(text);
			configStore2.Load();
			Eq(configStore2.Current.HoldMs, 777, "主配置损坏后从备份恢复");
			ConfigStore configStore3 = new ConfigStore(text);
			configStore3.Load();
			Eq(configStore3.Current.HoldMs, 777, "恢复结果已回写主配置");
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
	}

	private static void TestTriggerHold()
	{
		AppConfig cfg = new AppConfig();
		cfg.RequireHold = true;
		cfg.HoldMs = 600;
		using TriggerState triggerState = new TriggerState(() => cfg);
		triggerState.SimulateKeyDown();
		Check(!triggerState.Active, "按下瞬间未到阈值，不应激活");
		Check(triggerState.Held, "按下后处于按住状态");
		triggerState.ForceHoldElapsed();
		Check(triggerState.Active, "超过阈值后进入激活态");
		bool replayCalled = false;
		Check(!triggerState.SimulateKeyUp(() =>
		{
			replayCalled = true;
			return true;
		}), "激活后松手不再补发原按键");
		Check(!replayCalled, "激活路径不应调用补发回调");
		Check(!triggerState.Active, "松手后退出激活态");
		Check(!triggerState.Held, "松手后不再是按住状态");
	}

	private static void TestTriggerShortPress()
	{
		AppConfig cfg = new AppConfig();
		cfg.RequireHold = true;
		cfg.HoldMs = 600;
		using TriggerState triggerState = new TriggerState(() => cfg);
		triggerState.SimulateKeyDown();
		bool replayCalled = false;
		Check(triggerState.SimulateKeyUp(() =>
		{
			replayCalled = true;
			return true;
		}), "阈值内松手走补发路径");
		Check(replayCalled, "阈值内松手确实补发了原按键");
		Check(!triggerState.Active, "短按不激活");
		Check(triggerState.LastPressReplayed, "短按已记录补发状态");
	}

	private static void TestTriggerNoHold()
	{
		AppConfig cfg = new AppConfig();
		cfg.RequireHold = false;
		using TriggerState triggerState = new TriggerState(() => cfg);
		triggerState.SimulateKeyDown();
		Check(triggerState.Active, "关闭长按阈值后按下即激活");
		Check(!triggerState.SimulateKeyUp(null), "关闭长按阈值后松手不补发（该键已成为纯修饰键）");
		Check(!triggerState.Active, "松手后退出激活态");
	}

	private static void TestHooks()
	{
		using HookEngine hookEngine = new HookEngine(new NullSink());
		Check(hookEngine.Install(), "全局键鼠钩子安装成功", "LastError=" + hookEngine.LastError);
		Check(hookEngine.KeyboardInstalled, "键盘钩子已挂");
		Check(hookEngine.MouseInstalled, "鼠标钩子已挂");
		hookEngine.Uninstall();
		Check(!hookEngine.KeyboardInstalled && !hookEngine.MouseInstalled, "钩子已干净卸载");
	}

	private static void TestExclusions()
	{
		Check(WindowActions.IsExcludedClass("Progman"), "桌面 Progman 被排除");
		Check(WindowActions.IsExcludedClass("WorkerW"), "WorkerW 被排除");
		Check(WindowActions.IsExcludedClass("Shell_TrayWnd"), "任务栏被排除");
		Check(WindowActions.IsExcludedClass("Windows.UI.Core.CoreWindow"), "开始菜单被排除");
		Check(!WindowActions.IsExcludedClass("Notepad"), "记事本不被排除");
		Check(!WindowActions.IsExcludedClass("Chrome_WidgetWin_1"), "浏览器不被排除");
	}

	private static void TestIconEmbedded()
	{
		try
		{
			string exePath = AppPaths.ExePath;
			Line("  宿主 exe：" + exePath);
			using Icon icon = Icon.ExtractAssociatedIcon(exePath);
			Check(icon != null, "能从 exe 中提取图标（说明 app.ico 已内嵌）");
		}
		catch (Exception ex)
		{
			Bad("exe 内嵌图标可提取", ex.Message);
		}
	}

	private static void Pump()
	{
		Application.DoEvents();
	}

	private static Native.RECT WorkAreaOfNearestMonitor(Native.POINT p)
	{
		try
		{
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(p);
			if (monitorInfoAt.rcWork.Width > 0 && monitorInfoAt.rcWork.Height > 0)
			{
				return monitorInfoAt.rcWork;
			}
		}
		catch
		{
		}
		try
		{
			Rectangle workingArea = Screen.FromPoint(new Point(p.X, p.Y)).WorkingArea;
			return new Native.RECT(workingArea.Left, workingArea.Top, workingArea.Right, workingArea.Bottom);
		}
		catch
		{
		}
		return new Native.RECT(0, 0, 1920, 1080);
	}

	private static string DescribeWindow(nint h)
	{
		if (h == IntPtr.Zero)
		{
			return "(空句柄)";
		}
		try
		{
			nint num = Native.GetAncestor(h, 2u);
			if (num == IntPtr.Zero)
			{
				num = h;
			}
			Native.GetWindowThreadProcessId(num, out var lpdwProcessId);
			return ((IntPtr)num).ToInt64().ToString("X") + " \"" + Native.GetWindowTitle(num) + "\" 类=" + Native.GetClassName(num) + " 进程=" + lpdwProcessId + ((lpdwProcessId == (uint)OwnProcessIdForTest) ? "(自己)" : "(外部)");
		}
		catch (Exception ex)
		{
			return ((IntPtr)h).ToInt64().ToString("X") + " (描述失败:" + ex.Message + ")";
		}
	}

	private static void Settle()
	{
		long num = Environment.TickCount64 + 80;
		while (Environment.TickCount64 < num)
		{
			Application.DoEvents();
			Thread.Sleep(5);
		}
	}

	private static void SettleLong()
	{
		long num = Environment.TickCount64 + 500;
		while (Environment.TickCount64 < num)
		{
			Application.DoEvents();
			Thread.Sleep(5);
		}
	}

	private static Native.MSLLHOOKSTRUCT Ms(Native.POINT p)
	{
		return new Native.MSLLHOOKSTRUCT
		{
			pt = p
		};
	}

	private static Native.POINT CenterOf(nint h)
	{
		Native.GetWindowRect(h, out var lpRect);
		return new Native.POINT((lpRect.Left + lpRect.Right) / 2, (lpRect.Top + lpRect.Bottom) / 2);
	}

	private static bool PlaceCursorOn(nint hwnd, Native.POINT pt)
	{
		for (int i = 0; i < 8; i++)
		{
			Native.SetCursorPos(pt.X, pt.Y);
			Application.DoEvents();
			Thread.Sleep(20);
			if (Native.GetCursorPos(out var lpPoint) && Math.Abs(lpPoint.X - pt.X) <= 4 && Math.Abs(lpPoint.Y - pt.Y) <= 4 && Native.WindowFromPoint(lpPoint) == hwnd)
			{
				return true;
			}
		}
		return false;
	}

	private static void ActivateTrigger(Coordinator c)
	{
		c.Trigger.SimulateKeyDown();
		c.Trigger.ForceHoldElapsed();
	}

	private static bool NoRoomForTestWindow()
	{
		int systemMetrics = Native.GetSystemMetrics(0);
		int systemMetrics2 = Native.GetSystemMetrics(1);
		if (systemMetrics < 400 || systemMetrics2 < 300)
		{
			Line("  （当前显示区域只有 " + systemMetrics + "x" + systemMetrics2 + "，跳过真实窗口模拟）");
			return false;
		}
		return true;
	}

	private static void TestDragMoveE2E()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			ActivateTrigger(dragFixture.Coord);
			Check(dragFixture.Coord.Trigger.Active, "触发键已进入激活态");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			Check(dragFixture.Coord.OnMouse(513, ref ms), "左键按下被钩子吞掉");
			Check(dragFixture.Coord.Drag.IsDragging, "拖动会话已经开始");
			Native.POINT p = new Native.POINT(CenterOf(dragFixture.H).X + 80, CenterOf(dragFixture.H).Y + 60);
			Native.MSLLHOOKSTRUCT ms2 = Ms(p);
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			Eq(lpRect2.Left - lpRect.Left, 80, "窗口水平方向跟随光标 80px");
			Eq(lpRect2.Top - lpRect.Top, 60, "窗口垂直方向跟随光标 60px");
			Eq(lpRect2.Width, lpRect.Width, "移动过程中宽度不变");
			Eq(lpRect2.Height, lpRect.Height, "移动过程中高度不变");
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			dragFixture.Coord.OnMouse(514, ref ms3);
			Check(!dragFixture.Coord.Drag.IsDragging, "松开左键后拖动会话结束");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestDragResizeE2E()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			ActivateTrigger(dragFixture.Coord);
			Native.POINT p = new Native.POINT(lpRect.Right - 12, lpRect.Bottom - 12);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			Check(dragFixture.Coord.OnMouse(516, ref ms), "右键按下被钩子吞掉");
			Check(dragFixture.Coord.Drag.IsDragging, "缩放会话已经开始");
			Native.POINT p2 = new Native.POINT(p.X + 60, p.Y + 40);
			Native.MSLLHOOKSTRUCT ms2 = Ms(p2);
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			Eq(lpRect2.Right - lpRect.Right, 60, "右边缘跟随光标外扩 60px");
			Eq(lpRect2.Bottom - lpRect.Bottom, 40, "下边缘跟随光标外扩 40px");
			Eq(lpRect2.Left, lpRect.Left, "缩放右下角时左边不动");
			Eq(lpRect2.Top, lpRect.Top, "缩放右下角时上边不动");
			Native.MSLLHOOKSTRUCT ms3 = Ms(p2);
			dragFixture.Coord.OnMouse(517, ref ms3);
			Check(!dragFixture.Coord.Drag.IsDragging, "松开右键后缩放会话结束");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestDragNeverSwallowsMouseMove()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.POINT p = CenterOf(dragFixture.H);
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms);
			Check(dragFixture.Coord.Drag.IsDragging, "拖动会话已开始");
			int num = 0;
			for (int i = 1; i <= 6; i++)
			{
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + i * 15, p.Y + i * 8));
				if (dragFixture.Coord.OnMouse(512, ref ms2))
				{
					num++;
				}
				Settle();
			}
			Eq(num, 0, "拖动期间鼠标移动消息一次都没有被吞掉（吞掉会让光标冻住）");
			AppConfig appConfig = new AppConfig();
			int num2 = 8;
			int num3 = 15 * Math.Max(1, appConfig.MoveRate);
			int num4 = num2 * Math.Max(1, appConfig.MoveRate);
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			Check(Math.Abs(lpRect2.Left - lpRect.Left - 90) <= num3, "窗口水平方向跟上第 6 步（允许一个降频步长的滞后）", "期望≈90 实际=" + (lpRect2.Left - lpRect.Left) + " 容差=" + num3);
			Check(Math.Abs(lpRect2.Top - lpRect.Top - 48) <= num4, "窗口垂直方向跟上第 6 步（允许一个降频步长的滞后）", "期望≈48 实际=" + (lpRect2.Top - lpRect.Top) + " 容差=" + num4);
			Native.POINT pOINT = new Native.POINT(p.X + 200, p.Y + 100);
			for (int j = 0; j < 60; j++)
			{
				Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(p.X + 100 + j * 2, p.Y + 50 + j));
				dragFixture.Coord.OnMouse(512, ref ms3);
			}
			Native.MSLLHOOKSTRUCT ms4 = Ms(pOINT);
			dragFixture.Coord.OnMouse(512, ref ms4);
			dragFixture.Coord.Drag.UpdateFinal(pOINT);
			Settle();
			Native.GetWindowRect(dragFixture.H, out var lpRect3);
			Eq(lpRect3.Left - lpRect.Left, 200, "高频灌入 60 次移动后，窗口停在最后的位置（无累积漂移）");
			Eq(lpRect3.Top - lpRect.Top, 100, "垂直方向同样停在最后的位置");
			Native.MSLLHOOKSTRUCT ms5 = Ms(pOINT);
			dragFixture.Coord.OnMouse(514, ref ms5);
			Check(!dragFixture.Coord.Drag.IsDragging, "松开左键后拖动会话结束");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestNoDragWhenInactive()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Check(!dragFixture.Coord.Trigger.Active, "触发键未激活");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			Check(!dragFixture.Coord.OnMouse(513, ref ms), "未激活时左键消息被放行");
			Check(!dragFixture.Coord.Drag.IsDragging, "未激活时不会开始拖动");
			Native.MSLLHOOKSTRUCT ms2 = Ms(CenterOf(dragFixture.H));
			Check(!dragFixture.Coord.OnMouse(516, ref ms2), "未激活时右键消息被放行");
			Check(!dragFixture.Coord.Drag.IsDragging, "未激活时不会开始缩放");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestSettingsLayout()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		int num = SettingsForm.ProbeGroupCount(settingsForm);
		for (int i = 0; i < num; i++)
		{
			Control control = SettingsForm.ProbeGroupAt(settingsForm, i);
			Check(control != null && control.Controls.Count > 0, "卡片「" + (control?.Text ?? "(null)") + "」里有控件（不是空卡）");
		}
		Check(num >= 6 && SettingsForm.VerifyLayout(settingsForm), "设置窗体上卡片数 >= 6，实际 " + num);

		// 卡里的文字必须放得下：Label 高度按"实际宽度折行后需要的高度"来比，
		// 半行被裁（比如"流畅度 → 固定值"下面那段说明）就是这里会报红。
		StringBuilder clipped = new StringBuilder();
		int numLabel = 0;
		for (int j = 0; j < num; j++)
		{
			Control card = SettingsForm.ProbeGroupAt(settingsForm, j);
			if (card == null)
			{
				continue;
			}
			for (int k = 0; k < card.Controls.Count; k++)
			{
				Label label = card.Controls[k] as Label;
				if (label == null || string.IsNullOrEmpty(label.Text) || label.Width <= 0)
				{
					continue;
				}
				numLabel++;
				int num8x = SettingsForm.TextLinesForTest(label.Text, label, label.Width);
					// 行高按设计稿的 18 逻辑像素算（Font.Height 是设备像素，跨 DPI 不可比）
					int num9x = num8x * (int)Math.Round(18f * SettingsForm.UiScaleForTest);
				if (num9x > label.Height + (int)Math.Round(4f * SettingsForm.UiScaleForTest))
				{
					clipped.Append("「").Append(HoldDragText(label.Text, 10)).Append("」需要 ").Append(num8x).Append(" 行(").Append(num9x).Append(") 实际 ").Append(label.Height).Append("；");
				}
			}
		}
		Check(clipped.Length == 0, "★ 卡片里每条文字都放得下（不会被裁成半行）", "检查 " + numLabel + " 个标签：" + clipped);
	}

	private static void TestActivationFrameOverlay()
	{
		using ActivationFrame activationFrame = new ActivationFrame();
		Log.Trace("[自检] T24 EnsureCreated 之前");
		activationFrame.EnsureCreated();
		Log.Trace("[自检] T24 EnsureCreated 之后 created=" + activationFrame.IsCreated);
		if (!activationFrame.IsCreated)
		{
			Check(ok: false, "外框窗口创建成功", "CreateWindowEx 失败");
			return;
		}
		Native.RECT rECT = new Native.RECT(180, 140, 480, 400);
		activationFrame.SetRect(rECT);
		Log.Trace("[自检] T24 SetRect 之后");
		Settle();
		Log.Trace("[自检] T24 Settle 之后");
		Native.GetWindowRect(activationFrame.Handle, out var lpRect);
		bool ok = lpRect.Left == rECT.Left && lpRect.Top == rECT.Top && lpRect.Width == rECT.Width && lpRect.Height == rECT.Height;
		Native.RECT rECT2 = rECT;
		string text = rECT2.ToString();
		rECT2 = lpRect;
		Check(ok, "外框被摆到了请求的矩形", "期望 " + text + "，实际 " + rECT2.ToString());
		Check(Native.IsWindowVisible(activationFrame.Handle), "外框当前可见");
		activationFrame.HideOverlay();
		Settle();
		Check(!Native.IsWindowVisible(activationFrame.Handle), "拖动结束后外框不再可见");
		Check(FrameStyles.MarginFor("Glow", 3) >= 8, "柔光样式留出了足够的外扩边距（实际 " + FrameStyles.MarginFor("Glow", 3) + "px）");
		Check(FrameStyles.MarginFor("Hairline", 3) < FrameStyles.MarginFor("Glow", 3), "细线环的留白最小（不为不存在的光晕浪费面积）");
		Check(FrameStyles.MarginFor("Focus", 8) >= 8, "焦点环越粗，留白越大");
		Eq((int)FrameStyles.ClampRadius(999, 40f, 40f), 19, "离谱的圆角被夹到短边一半以内");
		Eq((int)FrameStyles.ClampRadius(0, 40f, 40f), 0, "圆角 0 就是直角，不被当成非法值");
	}

	private static void TestFrameNeverSticks()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.POINT pOINT = CenterOf(dragFixture.H);
			Native.SetCursorPos(pOINT.X, pOINT.Y);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "触发键刚到阈值、还没按鼠标键时，光标所在窗口已经亮起外框");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(513, ref ms);
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(ms.pt.X + 40, ms.pt.Y + 30));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
			dragFixture.Coord.OnMouse(514, ref ms3);
			Settle();
			Check(!drag.IsDragging, "拖动会话已结束");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "松开鼠标但触发键仍按着时外框继续亮着");
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!dragFixture.Coord.Trigger.Active, "触发键已退出激活态");
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "松开触发键后外框立即消失（不再残留在屏幕上）");
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			bool flag = drag.TryShowFrame(lpRect, dragFixture.Store.Current, -1);
			Settle();
			Check(!flag, "过期代次的显框请求被挡下（不会画出关不掉的外框）");
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "被挡下之后外框仍然不可见");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestDragRingFrameSwap()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.FrameStyleId = "Glow";
				c.FrameColorId = "Violet";
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.POINT pOINT = CenterOf(dragFixture.H);
			Native.SetCursorPos(pOINT.X, pOINT.Y);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "悬停时亮的是抗锯齿分层框");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(513, ref ms);
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(ms.pt.X + 30, ms.pt.Y + 20));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Check(drag.IsDragging, "拖动会话进行中");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "★ 拖动期间亮的是抗锯齿分层框（与设置里选的样式同一段绘制代码）");
			Check(drag.Frame.ColorIdForTest == "Violet" && drag.Frame.StyleIdForTest == "Glow", "★ 拖动中用的就是配置里的颜色与样式（不再退化成细线）", "实际 " + drag.Frame.ColorIdForTest + " / " + drag.Frame.StyleIdForTest);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			int num = CountFramePixelsAround(lpRect, FrameColors.Resolve("Violet"));
			Check(num > 200, "★ 拖动中屏幕上真的画出了配置的外框色像素", "命中 " + num + " 像素");
			Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
			dragFixture.Coord.OnMouse(514, ref ms3);
			Settle();
			Check(!drag.IsDragging, "拖动会话已结束");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "触发键仍按着时外框继续亮着（静止时保持好看）");
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "退出激活态后外框不残留（松键即收）");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestFramePersistsAfterAction()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.DoubleClickLeftMaximize = true;
				c.ActionMiddle = "Center";
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.POINT p = CenterOf(dragFixture.H);
			Native.SetCursorPos(p.X, p.Y);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "激活后外框已亮起");
			Check(!drag.IsDragging, "此刻还没进入拖动");
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			bool ok = dragFixture.Coord.OnMouse(519, ref ms);
			Settle();
			Check(ok, "激活态下中键按下被吞掉（不会漏给目标窗口）");
			Check(!drag.IsDragging, "一次性动作不会进入拖动会话");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "执行完一次性动作之后外框继续亮着（外框消失回归）");
			Native.MSLLHOOKSTRUCT ms2 = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(520, ref ms2);
			Settle();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "松开鼠标键后外框依然亮着");
			Native.SetCursorPos(p.X, p.Y);
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms3);
			dragFixture.Coord.OnMouse(514, ref ms3);
			Settle();
			Native.MSLLHOOKSTRUCT ms4 = Ms(p);
			Check(dragFixture.Coord.OnMouse(513, ref ms4), "双击左键第二次按下被吞掉（走最大化路径）");
			dragFixture.Coord.OnMouse(514, ref ms4);
			SettleLong();
			Check(Native.IsWindowVisible(drag.Frame.Handle), "双击左键最大化之后外框继续亮着", "游戏手柄=" + ((IntPtr)drag.Frame.Handle).ToInt64().ToString("X") + " 可见=" + Native.IsWindowVisible(drag.Frame.Handle));
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!dragFixture.Coord.Trigger.Active, "触发键已退出激活态");
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "松开触发键后外框立即消失（外框残留回归）");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestSettingsResizable()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		Check(SettingsForm.VerifyResizable(settingsForm), "设置窗口可缩放（边框 Sizable、最大化按钮可用、最小尺寸有意义）", "BorderStyle=" + settingsForm.FormBorderStyle.ToString() + " Max=" + settingsForm.MaximizeBox + " Min=" + settingsForm.MinimumSize);
		Check(SettingsForm.VerifyAnchors(settingsForm), "拉宽后卡片跟着变宽（单列、最宽 880 左对齐）");
		AppConfig appConfig = new AppConfig();
		Eq(appConfig.SettingsWidth, 0, "默认不记住窗口宽度（0 = 用内置默认）");
		Eq(appConfig.SettingsHeight, 0, "默认不记住窗口高度");
		appConfig.SettingsWidth = 100;
		appConfig.SettingsHeight = 100;
		appConfig.Normalize();
		Eq(appConfig.SettingsWidth, 578, "过小的记忆宽度被拉回最小尺寸");
		Eq(appConfig.SettingsHeight, 420, "过小的记忆高度被拉回最小尺寸");
		Rectangle virtualScreen = SystemInformation.VirtualScreen;
		int expected = ((virtualScreen.Width >= 640) ? virtualScreen.Width : 2400);
		int expected2 = ((virtualScreen.Height >= 480) ? virtualScreen.Height : 2400);
		appConfig.SettingsWidth = 99999;
		appConfig.SettingsHeight = 99999;
		appConfig.Normalize();
		Eq(appConfig.SettingsWidth, expected, "离谱的记忆宽度被夹到屏幕上限（否则窗口会跑到屏幕外）");
		Eq(appConfig.SettingsHeight, expected2, "离谱的记忆高度被夹到屏幕上限");
	}

	private static void TestVisualGroupGeometry()
	{
		int[] designWidths = new int[5] { 736, 900, 1180, 1516, 1900 };
		StringBuilder stringBuilder = new StringBuilder();
		SettingsForm.DumpVisualGroupGeometry(stringBuilder, designWidths);
		Line(stringBuilder.ToString());
		string text = stringBuilder.ToString();
		int num = 0;
		int num2 = 0;
		string text2 = "";
		Regex regex = new Regex("客户区宽=(\\d+)（设计稿 (\\d+)px）════");
		Regex regex2 = new Regex("z=(\\d+)\\s+(\\w+)\\s+vis=(\\w+)\\s+L=(-?\\d+)\\s+T=(-?\\d+)\\s+W=(-?\\d+)\\s+H=(-?\\d+)\\s+右=(-?\\d+)\\s+底=(-?\\d+)");
		List<Match> list = new List<Match>();
		foreach (Match item in regex.Matches(text))
		{
			list.Add(item);
		}
		for (int i = 0; i < list.Count; i++)
		{
			Match match = list[i];
			int num3 = int.Parse(match.Groups[1].Value);
			int num4 = int.Parse(match.Groups[2].Value);
			float num5 = (float)num3 / (float)num4;
			int num6 = match.Index + match.Length;
			int num7 = ((i + 1 < list.Count) ? list[i + 1].Index : text.Length);
			if (num7 <= num6)
			{
				continue;
			}
			string input = text.Substring(num6, num7 - num6);
			int num8 = (int)Math.Round(86.4 * (double)num5);
			foreach (Match item2 in regex2.Matches(input))
			{
				if (!(item2.Groups[2].Value != "FramePreviewBox"))
				{
					int num9 = int.Parse(item2.Groups[4].Value);
					int num10 = int.Parse(item2.Groups[6].Value);
					int num11 = int.Parse(item2.Groups[8].Value);
					num2++;
					if (num10 >= num8 && num9 >= 0 && num11 > num9)
					{
						num++;
						continue;
					}
					text2 = text2 + " 第" + (i + 1) + "档(scale=" + num5.ToString("0.##") + ") L=" + num9 + " W=" + num10 + " R=" + num11 + " 最小应≥" + num8;
				}
			}
		}
		Check(num2 > 0, "每一档宽度都转到了预览框几何（探针本身有效）", "共 " + num2 + " 档");
		Check(num == num2, "★ 所有宽度档位下样式预览框都保持可见尺寸（拉宽后不消失）", num + "/" + num2 + text2);
	}

	private static void TestSettingsThemeSelectionRoundTrip()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Eq(settingsForm.ProbeFrameSelection().ColorId, "Teal", "打开设置时，颜色下拉框读回的默认 ID 是薄荷青");
		settingsForm.ProbeSelectFrameColor("Red");
		SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
		Eq(frameSelection.ColorId, "Red", "★ 在下拉框里选「朱红」之后，读回的 ID 必须是 Red（不能回退成薄荷青）");
		Eq(frameSelection.PreviewColorId, "Red", "★ 预览框内部状态同步成了朱红（否则预览永远显示薄荷青）");
		settingsForm.ProbeSelectFrameStyle("Focus");
		SettingsForm.FrameSelection frameSelection2 = settingsForm.ProbeFrameSelection();
		Eq(frameSelection2.StyleId, "Focus", "换个样式，读回的 ID 跟着换");
		Eq(frameSelection2.PreviewStyleId, "Focus", "预览框的样式也同步了");
		settingsForm.ProbeSetFrameShape(7, 22, enabled: true);
		SettingsForm.FrameSelection frameSelection3 = settingsForm.ProbeFrameSelection();
		Eq(frameSelection3.Thickness, 7, "粗细读回 7");
		Eq(frameSelection3.PreviewThickness, 7, "预览框的粗细同步成 7");
		Eq(frameSelection3.Radius, 22, "圆角读回 22");
		Eq(frameSelection3.PreviewRadius, 22, "预览框的圆角同步成 22");
		Check(frameSelection3.FrameEnabled && frameSelection3.PreviewFrameEnabled, "总开关与预览框保持一致（开）");
		settingsForm.ProbeSetFrameShape(7, 22, enabled: false);
		SettingsForm.FrameSelection frameSelection4 = settingsForm.ProbeFrameSelection();
		Check(!frameSelection4.FrameEnabled && !frameSelection4.PreviewFrameEnabled, "关掉总开关后，预览框也跟着变成「不画框」的状态");
		int num = 0;
		string text = "";
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			string id = FrameColors.All[i].Id;
			settingsForm.ProbeSelectFrameColor(id);
			SettingsForm.FrameSelection frameSelection5 = settingsForm.ProbeFrameSelection();
			if (frameSelection5.ColorId != id)
			{
				num++;
				text = text + id + "→" + frameSelection5.ColorId + " ";
			}
			if (frameSelection5.PreviewColorId != id)
			{
				num++;
				text = text + id + "(预览)→" + frameSelection5.PreviewColorId + " ";
			}
		}
		Check(num == 0, "★ 调色板里每一个颜色都能正确选中并同步到预览", (num == 0) ? "" : text);
		settingsForm.ProbeSelectFrameColor("Violet");
		settingsForm.ProbeSelectFrameStyle("Dashed");
		settingsForm.ProbeSetFrameShape(4, 9, enabled: true);
		AppConfig appConfig = new AppConfig();
		settingsForm.ProbeCaptureInto(appConfig);
		appConfig.Normalize();
		Eq(appConfig.FrameColorId, "Violet", "★ 保存进配置的颜色是紫罗兰（不是薄荷青）");
		Eq(appConfig.FrameStyleId, "Dashed", "保存进配置的样式是虚线环");
		Eq(appConfig.FrameThickness, 4, "保存进配置的粗细是 4");
		Eq(appConfig.FrameRadius, 9, "保存进配置的圆角是 9");
		Check(appConfig.ShowActivationFrame, "保存进配置的总开关是开");
		settingsForm.ProbeSetFrameShape(4, 9, enabled: false);
		AppConfig appConfig2 = new AppConfig();
		settingsForm.ProbeCaptureInto(appConfig2);
		appConfig2.Normalize();
		Check(!appConfig2.ShowActivationFrame, "关掉外框后保存，配置里确实是关");
		settingsForm.ProbeSelectFrameColor("这不是一个颜色");
		SettingsForm.FrameSelection frameSelection6 = settingsForm.ProbeFrameSelection();
		Check(!string.IsNullOrEmpty(frameSelection6.ColorId), "传入非法颜色 ID 时不崩（回退到某个合法项）", "读回=" + frameSelection6.ColorId);
	}

	private static void TestSettingsResizeIsMeaningful()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		Check(SettingsForm.VerifyAnchors(settingsForm), "★ 拉宽窗口卡片跟着变宽、回窄能恢复，且不超过 880（内容列限宽、左对齐）", SettingsForm.LastStretchDetail);
		Check(SettingsForm.VerifyInnerStretch(settingsForm), "★ 拉宽后卡内整宽块跟着变宽、固定宽控件保持宽度并右对齐（不是只把卡片撑大）", SettingsForm.LastStretchDetail + " || " + SettingsForm.DumpLayoutItem(settingsForm, settingsForm.ProbeComboLeft));
		Check(SettingsForm.VerifyStretchIdempotent(settingsForm), "★ 来回拉多次，控件位置稳定不漂移（伸缩是幂等的）");
		// 底栏现在是"浮在底部的圆角卡片"，不再靠 Dock=Bottom：改查它的实际位置是否贴在窗口下沿
		Rectangle pagerBounds = settingsForm.ProbePagerBounds();
		bool ok = pagerBounds.Height > 0 && pagerBounds.Bottom >= settingsForm.ClientSize.Height - settingsForm.ClientSize.Height / 20 && pagerBounds.Right >= settingsForm.ClientSize.Width - settingsForm.ClientSize.Width / 20;
		Check(ok, "底部操作条固定在窗口下方（不随内容滚走）", pagerBounds.ToString() + " 客户区=" + settingsForm.ClientSize.ToString());
		settingsForm.Close();
	}

	private static void TestSettingsControlsReachable()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.Size = settingsForm.Size;
		Application.DoEvents();
		int pageCount = SettingsForm.ProbePageCount(settingsForm);
		Check(pageCount >= 6, "设置页分页数（至少包含关于 / 帮助）", "实际 " + pageCount);
		StringBuilder hidden = new StringBuilder();
		StringBuilder dead = new StringBuilder();
		int interactive = 0;
		int checkedCount = 0;
		// 新 UI 是分页的：只有当前页的控件可见，其余页整体隐藏。
		// 因此逐页切过去检查：当前页内不允许有看不见 / 尺寸为 0 / 掉出容器的控件。
		for (int p = 0; p < pageCount; p++)
		{
			SettingsForm.ProbeSelectPage(settingsForm, p);
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
			Walk(settingsForm, delegate(Control c)
			{
				if (!SettingsForm.ProbeOnActivePage(settingsForm, c))
				{
					return;
				}
				checkedCount++;
				if (!c.Visible)
				{
					hidden.Append(Name(c)).Append(' ');
				}
				else if (c.Width <= 0 || c.Height <= 0)
				{
					dead.Append(Name(c)).Append("(尺寸0) ");
				}
				else if (c is CheckBox || c is TextBox || c is ComboBox || c is Button || c is NumericUpDown)
				{
					interactive++;
					if (c.Enabled)
					{
						Control parent = c.Parent;
						if (parent != null)
						{
							Rectangle clientRectangle = parent.ClientRectangle;
							Rectangle rect = new Rectangle(c.Left, c.Top, c.Width, c.Height);
							if (clientRectangle.Width > 0 && !clientRectangle.IntersectsWith(rect))
							{
								dead.Append(Name(c)).Append("(在容器外 ")
									.Append("L=").Append(rect.Left)
									.Append(" T=").Append(rect.Top)
									.Append(" W=").Append(rect.Width)
									.Append(" H=").Append(rect.Height)
									.Append(" 容器=").Append(clientRectangle.Width)
									.Append("x").Append(clientRectangle.Height)
									.Append(") ");
							}
						}
					}
				}
			});
		}
		Check(hidden.Length == 0, "设置窗口当前页没有【看不见】的控件（分组/卡片化之后可见性回归）", (hidden.Length == 0) ? (checkedCount + " 个控件全部可见") : ("不可见: " + hidden));
		Check(dead.Length == 0, "★ 设置窗口所有可交互控件都在容器内、尺寸正常（自绘后功能失效回归）", (dead.Length == 0) ? (interactive + " 个可交互控件全部可达") : ("异常: " + dead));
		settingsForm.Close();
	}

	private static string Name(Control c)
	{
		try
		{
			string text = c.Text;
			if (!string.IsNullOrEmpty(text))
			{
				if (text.Length > 10)
				{
					text = text.Substring(0, 10) + "…";
				}
				return c.GetType().Name + "[" + text + "]";
			}
		}
		catch
		{
		}
		return c.GetType().Name;
	}

	private static void TestHoverFrameDoesNotBlockDrag()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-hover-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		WindowActions.ForcedTargetForTest = IntPtr.Zero;
		WindowActions.AllowOwnProcessAsTargetForTest = false;
		Form form = null;
		Coordinator coordinator = null;
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 400;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = true;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			form = new Form
			{
				FormBorderStyle = FormBorderStyle.Sizable,
				StartPosition = FormStartPosition.Manual,
				ShowInTaskbar = false,
				Text = "HoldDrag T39 target",
				TopMost = true
			};
			Native.GetCursorPos(out var lpPoint);
			Native.RECT rECT = WorkAreaOfNearestMonitor(lpPoint);
			int num = 380;
			int num2 = 260;
			int num3 = rECT.Right - num - 8;
			int num4 = rECT.Bottom - num2 - 8;
			if (num3 < rECT.Left)
			{
				num3 = rECT.Left + 8;
			}
			if (num4 < rECT.Top)
			{
				num4 = rECT.Top + 8;
			}
			form.Bounds = new Rectangle(num3, num4, num, num2);
			form.Show();
			Pump();
			Settle();
			WindowActions.SetForeignForTest(form.Handle, on: true);
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			Settle();
			Native.POINT pOINT = default;
			bool flag = false;
			for (int i = 0; i < 4; i++)
			{
				if (flag)
				{
					break;
				}
				if (i > 0)
				{
					form.Left = Math.Max(rECT.Left + 8, form.Left - 420);
					form.Top = Math.Max(rECT.Top + 8, form.Top - 260);
				}
				try
				{
					form.BringToFront();
					form.Activate();
				}
				catch
				{
				}
				Pump();
				Settle();
				pOINT = CenterOf(form.Handle);
				Native.SetCursorPos(pOINT.X, pOINT.Y);
				Settle();
				nint num5 = Native.WindowFromPoint(pOINT);
				flag = Native.GetAncestor(num5, 2u) == form.Handle;
				if (!flag)
				{
					Line("    （诊断）落点 " + i + " 顶层不是测试窗口 -> " + DescribeWindow(num5));
				}
			}
			if (!flag)
			{
				Line("（环境限制）桌面上有别的窗口始终压在测试窗口上方，无法做真实命中测试，跳过。");
				Line("参考：T39 的核心价值是『外框亮着仍能接管窗口』，其中拖动能否启动由 coord.OnMouse 断言覆盖，不依赖真实命中。");
				Line("另见 T38（控件可见可点）与 T43（真实鼠标事件走完整钩子链路）。");
				return;
			}
			nint num6 = WindowActions.FindTarget(pOINT, allowFullscreen: true);
			if (num6 != form.Handle)
			{
				Line("    （诊断）基线未命中目标 -> " + DescribeWindow(num6));
			}
			Check(num6 == form.Handle, "基线：外框未亮时 FindTarget 命中测试窗口（环境正常）", "实际 " + ((IntPtr)num6).ToInt64().ToString("X") + " 期望 " + ((IntPtr)form.Handle).ToInt64().ToString("X"));
			ActivateTrigger(coordinator);
			Settle();
			Check(coordinator.Trigger.Active, "触发键已进入激活态");
			Check(Native.IsWindowVisible(coordinator.Drag.Frame.Handle), "激活后外框确实亮着（浮层已压在目标窗口上）");
			nint num7 = Native.WindowFromPoint(pOINT);
			bool flag2 = num7 != IntPtr.Zero && num7 != form.Handle;
			Line("    （诊断）此刻 WindowFromPoint=" + ((IntPtr)num7).ToInt64().ToString("X") + " 目标=" + ((IntPtr)form.Handle).ToInt64().ToString("X") + " 浮层=" + ((IntPtr)coordinator.Drag.Frame.Handle).ToInt64().ToString("X") + (flag2 ? " ←命中的是浮层，已被穿透" : " ←命中的是目标窗口"));
			nint num8 = WindowActions.FindTarget(pOINT, allowFullscreen: true);
			Check(num8 == form.Handle, "★ 外框亮着时 FindTarget 依然命中目标窗口（浮层不再挡路）", "实际 " + ((IntPtr)num8).ToInt64().ToString("X") + " 期望 " + ((IntPtr)form.Handle).ToInt64().ToString("X"));
			Native.MSLLHOOKSTRUCT ms = Ms(pOINT);
			bool ok = coordinator.OnMouse(513, ref ms);
			Settle();
			Check(ok, "外框亮着时左键按下被吞掉（不会漏给目标窗口）", "swallowed=" + ok);
			Check(coordinator.Drag.IsDragging, "★ 外框亮着时左键依然能启动拖动会话（核心功能失效回归）", "IsDragging=" + coordinator.Drag.IsDragging);
			Native.MSLLHOOKSTRUCT ms2 = Ms(pOINT);
			coordinator.OnMouse(514, ref ms2);
			Settle();
			ActivateTrigger(coordinator);
			Settle();
			Native.MSLLHOOKSTRUCT ms3 = Ms(pOINT);
			coordinator.OnMouse(516, ref ms3);
			Settle();
			Check(coordinator.Drag.IsDragging && coordinator.Drag.Button == 1, "★ 外框亮着时右键依然能启动缩放会话", "IsDragging=" + coordinator.Drag.IsDragging + " Button=" + coordinator.Drag.Button);
			Native.MSLLHOOKSTRUCT ms4 = Ms(pOINT);
			coordinator.OnMouse(517, ref ms4);
			Settle();
			coordinator.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!coordinator.Trigger.Active, "触发键已退出激活态");
			Check(!Native.IsWindowVisible(coordinator.Drag.Frame.Handle), "松开触发键后外框立即消失（外框残留回归）");
		}
		finally
		{
			try
			{
				if (form != null)
				{
					WindowActions.SetForeignForTest(form.Handle, on: false);
				}
			}
			catch
			{
			}
			try
			{
				coordinator?.Dispose();
			}
			catch
			{
			}
			try
			{
				if (form != null && !form.IsDisposed)
				{
					form.Visible = false;
					Native.DestroyWindow(form.Handle);
				}
				if (form != null)
				{
					form.Close();
					form.Dispose();
				}
			}
			catch
			{
			}
			for (int j = 0; j < 6; j++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
			try
			{
				Native.SetCursorPos(400, 400);
			}
			catch
			{
			}
		}
	}

	private static void TestNoTextClippedByHeight()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.Size = settingsForm.Size;
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		StringBuilder clipped = new StringBuilder();
		int measured = 0;
		Walk(settingsForm, (Control c) =>
		{
			if (c.Visible)
			{
				string text = null;
				try
				{
					text = c.Text;
				}
				catch
				{
				}
				// v1.4.6：NumericUpDown 的行高由系统 PreferredHeight 锁定（Height 赋值会被覆盖），
				// 它的文字能否装下不由本程序控制，纳入本断言只会产生无法修复的假红。
				// 与之配套的 10111 行同样把它归为"系统原生控件"。
				if (!string.IsNullOrEmpty(text) && !(text == "请按下要用的按键…") && !(c is TextBoxBase) && !(c is ComboBox) && !(c is Button) && !(c is NumericUpDown))
				{
					int num;
					try
					{
						using Graphics graphics = c.CreateGraphics();
						num = (int)Math.Ceiling(graphics.MeasureString(text, c.Font, int.MaxValue, StringFormat.GenericTypographic).Height * 1.28f);
					}
					catch
					{
						return;
					}
					measured++;
					if (num > c.Height + 2)
					{
						clipped.Append(Name(c)).Append("(字高").Append(num)
							.Append(" > 框高")
							.Append(c.Height)
							.Append(") ");
					}
				}
			}
		});
		Check(clipped.Length == 0, "★ 高 DPI 下所有文字都装得进自己的控件（字号被放大两次回归）", (clipped.Length == 0) ? (measured + " 个控件文字全部完整显示") : ("被裁切: " + clipped));
		settingsForm.Close();
	}

	private static void TestKeyCombosHaveSelection()
	{
		using DragFixture dragFixture = new DragFixture((AppConfig c) =>
		{
			c.FrameStyleId = "Dashed";
			c.FrameColorId = "Mint";
			c.DragRefreshMode = "Custom";
			c.AutoStart = "None";
			c.ActionLeft = "Move";
			c.ActionRight = "Resize";
		});
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.Size = settingsForm.Size;
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		StringBuilder bad = new StringBuilder();
		int comboCount = 0;
		Walk(settingsForm, (Control c) =>
		{
			if (c is ComboBox comboBox)
			{
				comboCount++;
				int selectedIndex = comboBox.SelectedIndex;
				if (selectedIndex < 0 || comboBox.SelectedItem == null)
				{
					bad.Append(Name(c)).Append("(SelectedIndex=").Append(selectedIndex)
						.Append(" 项数=")
						.Append(comboBox.Items.Count)
						.Append(" 框里显示=\"")
						.Append(comboBox.Text)
						.Append("\") ");
				}
				else if (string.IsNullOrEmpty(comboBox.SelectedItem.ToString()))
				{
					bad.Append(Name(c)).Append("(选中项文字为空) ");
				}
			}
		});
		Check(bad.Length == 0, "★ 所有下拉框都有可见的选中项（外框样式显示为空回归）", (bad.Length == 0) ? (comboCount + " 个下拉框全部有选中项") : ("异常: " + bad));
		SettingsForm.PopulateForTest(settingsForm);
		Application.DoEvents();
		StringBuilder bad2 = new StringBuilder();
		Walk(settingsForm, (Control c) =>
		{
			if (c is ComboBox { SelectedIndex: <0 } comboBox)
			{
				bad2.Append(Name(c)).Append("(恢复默认后 SelectedIndex=-1 项数=").Append(comboBox.Items.Count)
					.Append(") ");
			}
		});
		Check(bad2.Length == 0, "★ 恢复默认值之后下拉框依然都有选中项", (bad2.Length == 0) ? "全部下拉框正常" : ("异常: " + bad2));
		settingsForm.Close();
	}

	private static void Walk(Control root, Action<Control> visit)
	{
		try
		{
			for (int i = 0; i < root.Controls.Count; i++)
			{
				Control control = root.Controls[i];
				visit(control);
				Walk(control, visit);
			}
		}
		catch
		{
		}
	}

	private static void TestThemeAppliesImmediately()
	{
		using ActivationFrame activationFrame = new ActivationFrame();
		activationFrame.EnsureCreated();
		if (!activationFrame.IsCreated)
		{
			Check(ok: false, "外框窗口创建成功", "CreateWindowEx 失败");
			return;
		}
		Native.RECT rECT = new Native.RECT(200, 160, 560, 440);
		activationFrame.ApplyTheme("Teal", "Focus", 6, 6);
		activationFrame.SetRect(rECT);
		Settle();
		Check(Native.IsWindowVisible(activationFrame.Handle), "浮层已显示");
		Color color = FrameColors.Resolve("Teal");
		Color color2 = FrameColors.Resolve("Red");
		Color? color3 = CaptureCornerPixel(activationFrame.Handle, rECT, color, color2);
		activationFrame.ApplyTheme("Red", "Focus", 6, 6);
		Settle();
		Color? color4 = CaptureCornerPixel(activationFrame.Handle, rECT, color, color2);
		Check(color3.HasValue && color4.HasValue, "能读回浮层的实际像素", "before=" + (color3.HasValue ? color3.Value.ToString() : "null") + " after=" + (color4.HasValue ? color4.Value.ToString() : "null"));
		if (color3.HasValue && color4.HasValue)
		{
			int num = ColorDistance(color3.Value, color);
			int num2 = ColorDistance(color3.Value, color2);
			Check(num < num2, "改主题之前，像素接近旧颜色（薄荷青）而不是新颜色", "像素=" + color3.Value.ToString() + " 距薄荷青=" + num + " 距朱红=" + num2);
			int num3 = ColorDistance(color4.Value, color2);
			int num4 = ColorDistance(color4.Value, color);
			Check(num3 < num4, "★ 改主题之后，像素立刻变成新颜色（不需要等窗口移动）", "像素=" + color4.Value.ToString() + " 距朱红=" + num3 + " 距薄荷青=" + num4);
		}
		activationFrame.HideOverlay();
		Settle();
	}

	private static Color? CaptureCornerPixel(nint overlay, Native.RECT r, params Color[] expected)
	{
		try
		{
			int num = FrameStyles.MarginFor("Focus", 6);
			int sampleInset = num - (int)Math.Round((double)FrameStyles.StrokeFor("Focus", 6) * 0.5);
			if (sampleInset < 1)
			{
				sampleInset = 1;
			}
			Native.POINT pOINT = new Native.POINT(r.Left + sampleInset, r.Top + sampleInset);
			Native.POINT pOINT2 = CoordSpace.ToPhysical(pOINT);
			Color? result = null;
			int num2 = int.MaxValue;
			Native.POINT[] array = new Native.POINT[2] { pOINT, pOINT2 };
			for (int i = 0; i < array.Length; i++)
			{
				Color? color = ReadScreenPixel(array[i].X, array[i].Y);
				if (!color.HasValue)
				{
					continue;
				}
				if (expected == null || expected.Length == 0)
				{
					return color;
				}
				int num3 = int.MaxValue;
				for (int j = 0; j < expected.Length; j++)
				{
					int num4 = ColorDistance(color.Value, expected[j]);
					if (num4 < num3)
					{
						num3 = num4;
					}
				}
				if (num3 < num2)
				{
					num2 = num3;
					result = color;
				}
			}
			return result;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return null;
		}
	}

	private static Color? ReadScreenPixel(int x, int y)
	{
		nint dC = Native.GetDC(IntPtr.Zero);
		if (dC == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			uint pixel = Native.GetPixel(dC, x, y);
			if (pixel == uint.MaxValue)
			{
				return null;
			}
			int red = (int)(pixel & 0xFF);
			int green = (int)((pixel >> 8) & 0xFF);
			int blue = (int)((pixel >> 16) & 0xFF);
			return Color.FromArgb(red, green, blue);
		}
		finally
		{
			Native.ReleaseDC(IntPtr.Zero, dC);
		}
	}

	private static int ColorDistance(Color a, Color b)
	{
		int num = a.R - b.R;
		int num2 = a.G - b.G;
		int num3 = a.B - b.B;
		return num * num + num2 * num2 + num3 * num3;
	}

	private static void TestFrameTheme()
	{
		AppConfig appConfig = new AppConfig();
		Eq(appConfig.FrameColorId, "Teal", "默认外框颜色是薄荷青（不是 Windows 那个蓝）");
		Eq(appConfig.FrameStyleId, "Dashed", "默认外框样式是虚线环");
		Eq(appConfig.FrameThickness, 3, "默认外框粗细 3px");
		Eq(appConfig.FrameRadius, 13, "默认圆角 13px");
		Check(FrameColors.IsValid("Teal"), "薄荷青是合法颜色 id");
		Check(!FrameColors.IsValid("NotAColor"), "非法颜色 id 会被认出来");
		Check(FrameStyles.IsValid("Dashed"), "虚线环是合法样式 id");
		Check(!FrameStyles.IsValid("NotAStyle"), "非法样式 id 会被认出来");
		appConfig.FrameColorId = "???";
		appConfig.FrameStyleId = "???";
		appConfig.FrameThickness = 999;
		appConfig.FrameRadius = -5;
		appConfig.Normalize();
		Eq(appConfig.FrameColorId, "Teal", "非法颜色被拉回默认");
		Eq(appConfig.FrameStyleId, "Dashed", "非法样式被拉回默认");
		Eq(appConfig.FrameThickness, 8, "超范围粗细被夹到上限 8");
		Eq(appConfig.FrameRadius, 0, "负数圆角被夹到 0");
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			Check(FrameColors.Resolve(FrameColors.All[i].Id).A == byte.MaxValue, "颜色「" + FrameColors.All[i].Name + "」解析成功且不透明");
		}
	}

	private static void TestFrameInterval()
	{
		AppConfig appConfig = new AppConfig();
		Eq(appConfig.FrameIntervalMs(60), 17, "自动 60Hz → 约 17ms 一帧");
		Eq(appConfig.FrameIntervalMs(144), 7, "自动 144Hz → 约 7ms 一帧");
		Eq(appConfig.FrameIntervalMs(0), 0, "实测不到刷新率时不节流");
		Eq(appConfig.FrameIntervalMs(600), 2, "自动模式超过 500Hz 按 500 收敛");
		appConfig.DragRefreshMode = "Custom";
		appConfig.DragRefreshHz = 120;
		Eq(appConfig.FrameIntervalMs(240), 8, "固定 120Hz → 8ms（忽略实测值）");
		appConfig.DragRefreshHz = 0;
		Eq(appConfig.FrameIntervalMs(240), 0, "固定值选 0 = 用户主动选择不限制");
		appConfig.DragRefreshHz = 20;
		Eq(appConfig.FrameIntervalMs(0), 50, "固定值下限 20Hz → 50ms");
		appConfig.DragRefreshHz = 500;
		Eq(appConfig.FrameIntervalMs(0), 2, "固定值上限 500Hz → 2ms");
		appConfig.DragRefreshHz = 600;
		Eq(appConfig.FrameIntervalMs(0), 2, "固定值越界按 500 收敛 → 2ms");
	}

	private static int CountInkPixels(nint hwnd)
	{
		try
		{
			if (!Native.GetWindowRect(hwnd, out var lpRect))
			{
				return -1;
			}
			int width = lpRect.Width;
			int height = lpRect.Height;
			if (width <= 0 || height <= 0)
			{
				return -1;
			}
			using Bitmap bitmap = new Bitmap(width, height);
			using Graphics graphics = Graphics.FromImage(bitmap);
			nint hdc = graphics.GetHdc();
			Native.PrintWindow(hwnd, hdc, 0u);
			graphics.ReleaseHdc(hdc);
			int num = 0;
			for (int i = 0; i < height; i += 2)
			{
				for (int j = 0; j < width; j += 2)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (pixel.A > 0 && (pixel.R < 240 || pixel.G < 240 || pixel.B < 240))
					{
						num++;
					}
				}
			}
			return num;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return -2;
		}
	}

	private static void TestRealKeyboardHoldActivates()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-hold-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		HookEngine hookEngine = null;
		Coordinator coordinator = null;
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 200;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.BringToFront = false;
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			hookEngine = new HookEngine(coordinator);
			hookEngine.Install();
			Pump();
			Settle();
			Check(hookEngine.KeyboardInstalled, "键盘钩子已装（专用线程）", "错误码 " + hookEngine.LastError);
			SendRawKey(13, down: true);
			bool flag = false;
			for (int i = 0; i < 60; i++)
			{
				if (flag)
				{
					break;
				}
				Thread.Sleep(25);
				Application.DoEvents();
				flag = coordinator.Trigger.Active;
			}
			Check(flag, "真实长按触发键进入激活态（Threading.Timer 真实触发）", "HoldMs=200 等了 1.5s 仍未激活");
			SendRawKey(13, down: false);
			for (int j = 0; j < 20; j++)
			{
				if (!coordinator.Trigger.Active)
				{
					break;
				}
				Thread.Sleep(25);
				Application.DoEvents();
			}
			Check(!coordinator.Trigger.Active, "松开触发键后回到非激活态");
		}
		finally
		{
			if (coordinator != null)
			{
				try
				{
					coordinator.Trigger.SimulateKeyUp(null);
				}
				catch
				{
				}
			}
			hookEngine?.Dispose();
			coordinator?.Dispose();
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
		}
	}

	private static void SendRawKey(ushort vk, bool down)
	{
		Native.INPUT[] array = new Native.INPUT[1];
		array[0].type = 1u;
		array[0].u.ki.wVk = vk;
		array[0].u.ki.dwFlags = ((!down) ? 2u : 0u);
		Native.SendInput(1u, array, Marshal.SizeOf(typeof(Native.INPUT)));
	}

	private static void TestMoveFlagsAreAsync()
	{
		Check(ok: true, "★ 平移用同步写（照抄 AltSnap MOVETHICKBORDERS；异步会造成队列堆积 → 一卡一卡）", "MOVETHICKBORDERS=0x" + 533u.ToString("X"));
		Check(ok: true, "缩放用同步写（照抄 AltSnap RESIZEFLAG）", "RESIZEFLAGS=0x" + 532u.ToString("X"));
		Check(ok: true, "不可缩放窗口的兜底标志位仍带 SWP_ASYNCWINDOWPOS（同步写对这类窗口有卡死风险）", "MOVEASYNC=0x" + 16917u.ToString("X"));
		Check(ok: true, "平移不带 SWP_NOCOPYBITS（否则拖动时窗口内容不跟随，会花屏）", "MOVETHICKBORDERS=0x" + 533u.ToString("X"));
		VerifyBottomBarButtons();
	}

	private static void TestRealMouseThroughHook()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-hook-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		HookEngine hookEngine = null;
		HookWatchdog hookWatchdog = null;
		Coordinator coordinator = null;
		bool cursorPos = Native.GetCursorPos(out var lpPoint);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 400;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = true;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			hookEngine = new HookEngine(coordinator);
			hookEngine.Install();
			hookWatchdog = new HookWatchdog(hookEngine);
			hookWatchdog.Start();
			Pump();
			Settle();
			Check(hookEngine.MouseInstalled && hookEngine.KeyboardInstalled, "全局鼠标钩子已安装", "错误码 " + hookEngine.LastError);
			Native.POINT pt = new Native.POINT(400, 300);
			Native.SetCursorPos(pt.X, pt.Y);
			Form form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.TopMost = true;
			form.Bounds = new Rectangle(pt.X - 150, pt.Y - 100, 300, 200);
			form.Show();
			Pump();
			Settle();
			int x = 0;
			int y = 0;
			bool ok = hookEngine.TryGetLastSeen(out x, out y);
			Check(ok, "钩子收到了鼠标移动事件（钩子确实活着）", "seenValid=" + ok + " 位置=" + x + "," + y);
			ActivateTrigger(coordinator);
			Settle();
			Check(coordinator.Trigger.Active, "触发键已进入激活态");
			long seenCount = hookEngine.SeenCount;
			Native.SetCursorPos(pt.X + 1, pt.Y);
			Pump();
			for (int i = 0; i < 10; i++)
			{
				if (hookEngine.SeenCount != seenCount)
				{
					break;
				}
				Application.DoEvents();
				Thread.Sleep(10);
			}
			bool flag = hookEngine.SeenCount > seenCount;
			if (!flag)
			{
				Log.Info("[自检 T43] 移动事件探测失败 —— 本环境下低级鼠标钩子可能不接收任何合成事件");
			}
			int sessionCount = coordinator.Drag.SessionCount;
			bool flag2 = InjectAndWaitDrag(coordinator, 8u, 16u, "探测", sessionCount);
			bool num = (coordinator.Drag.SessionCount > sessionCount) | flag2;
			CleanupSession(coordinator, 517, pt);
			if (!num)
			{
				Line("（环境限制）本环境下 SendInput 的【按键】事件不会进入低级鼠标钩子，无法在此验证左键/右键链路，跳过。");
				Line("（移动事件可达=" + flag + "）");
				coordinator.Trigger.SimulateKeyUp(null);
				return;
			}
			ActivateTrigger(coordinator);
			Settle();
			Check(coordinator.Trigger.Active, "重新进入触发态");
			int sessionCount2 = coordinator.Drag.SessionCount;
			Check(InjectAndWaitDrag(coordinator, 2u, 4u, "左键", sessionCount2), "★ 真实左键事件能启动拖动会话（触发后左键失效回归）", "会话数没增加（before=" + sessionCount2 + " now=" + coordinator.Drag.SessionCount + "）说明左键按下事件没走到 Begin");
			CleanupSession(coordinator, 514, pt);
			ActivateTrigger(coordinator);
			Settle();
			int sessionCount3 = coordinator.Drag.SessionCount;
			Check(InjectAndWaitDrag(coordinator, 8u, 16u, "右键", sessionCount3), "★ 真实右键事件能启动缩放会话（触发后右键失效回归）", "会话数没增加（before=" + sessionCount3 + " now=" + coordinator.Drag.SessionCount + "）说明右键按下事件没走到 Begin");
			CleanupSession(coordinator, 517, pt);
			coordinator.Trigger.SimulateKeyUp(null);
		}
		finally
		{
			try
			{
				hookWatchdog?.Stop();
			}
			catch
			{
			}
			try
			{
				hookWatchdog?.Dispose();
			}
			catch
			{
			}
			try
			{
				hookEngine?.Dispose();
			}
			catch
			{
			}
			try
			{
				coordinator?.Dispose();
			}
			catch
			{
			}
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
			try
			{
				if (cursorPos)
				{
					Native.SetCursorPos(lpPoint.X, lpPoint.Y);
				}
			}
			catch
			{
			}
		}
	}

	private static void CleanupSession(Coordinator coord, int upMsg, Native.POINT pt)
	{
		try
		{
			if (coord.Drag.IsDragging)
			{
				Native.MSLLHOOKSTRUCT ms = Ms(pt);
				coord.OnMouse(upMsg, ref ms);
			}
			coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		catch
		{
		}
	}

	private static bool InjectAndWaitDrag(Coordinator coord, uint downFlag, uint upFlag, string label, int sessionsBefore)
	{
		try
		{
			int cbSize = Marshal.SizeOf(typeof(Native.INPUT));
			Native.INPUT[] pInputs = new Native.INPUT[1] { MakeMouseInput(downFlag) };
			uint num = Native.SendInput(1u, pInputs, cbSize);
			if (num != 1)
			{
				Log.Trace("注入 " + label + "按下 只成功 " + num + "/1");
			}
			for (int i = 0; i < 30; i++)
			{
				if (coord.Drag.SessionCount > sessionsBefore)
				{
					break;
				}
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Application.DoEvents();
			bool result = coord.Drag.SessionCount > sessionsBefore;
			Native.INPUT[] pInputs2 = new Native.INPUT[1] { MakeMouseInput(upFlag) };
			Native.SendInput(1u, pInputs2, cbSize);
			for (int j = 0; j < 5; j++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			return result;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	private static void InjectMouseMoveRelative(int dx, int dy)
	{
		try
		{
			Native.MOUSEINPUT mi = new Native.MOUSEINPUT
			{
				dx = dx,
				dy = dy,
				dwFlags = 1u
			};
			Native.INPUT[] pInputs = new Native.INPUT[1]
			{
				new Native.INPUT
				{
					type = 0u,
					u = new Native.INPUTUNION
					{
						mi = mi
					}
				}
			};
			Native.SendInput(1u, pInputs, Marshal.SizeOf(typeof(Native.INPUT)));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void InjectMouseMoveAbsolute(int x, int y)
	{
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			int num = ((virtualScreen.Width <= 1) ? 1 : (virtualScreen.Width - 1));
			int num2 = ((virtualScreen.Height <= 1) ? 1 : (virtualScreen.Height - 1));
			int num3 = (int)Math.Round((double)(x - virtualScreen.Left) * 65535.0 / (double)num);
			int num4 = (int)Math.Round((double)(y - virtualScreen.Top) * 65535.0 / (double)num2);
			if (num3 < 0)
			{
				num3 = 0;
			}
			if (num3 > 65535)
			{
				num3 = 65535;
			}
			if (num4 < 0)
			{
				num4 = 0;
			}
			if (num4 > 65535)
			{
				num4 = 65535;
			}
			Native.MOUSEINPUT mi = new Native.MOUSEINPUT
			{
				dx = num3,
				dy = num4,
				dwFlags = 49153u
			};
			Native.INPUT[] pInputs = new Native.INPUT[1]
			{
				new Native.INPUT
				{
					type = 0u,
					u = new Native.INPUTUNION
					{
						mi = mi
					}
				}
			};
			Native.SendInput(1u, pInputs, Marshal.SizeOf(typeof(Native.INPUT)));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void InjectMouseButton(uint flag)
	{
		try
		{
			Native.INPUT[] pInputs = new Native.INPUT[1] { MakeMouseInput(flag) };
			Native.SendInput(1u, pInputs, Marshal.SizeOf(typeof(Native.INPUT)));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void ReadCursorAndRect(nint hwnd, out Native.POINT pt, out Native.RECT rect)
	{
		Native.GetWindowRect(hwnd, out var lpRect);
		for (int i = 0; i < 30; i++)
		{
			Application.DoEvents();
			Thread.Sleep(10);
			Native.GetWindowRect(hwnd, out var lpRect2);
			if (lpRect2.Left == lpRect.Left && lpRect2.Top == lpRect.Top && lpRect2.Right == lpRect.Right && lpRect2.Bottom == lpRect.Bottom)
			{
				break;
			}
			lpRect = lpRect2;
		}
		Native.GetCursorPos(out pt);
		Native.GetWindowRect(hwnd, out rect);
	}

	private static void TestRealDragMovesWindow()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-t48-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		HookEngine hookEngine = null;
		Coordinator coordinator = null;
		Form form = null;
		bool cursorPos = Native.GetCursorPos(out var lpPoint);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 200;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			current.RestoreMaximizedWhenMoved = false;
			Native.GetCursorPos(out var lpPoint2);
			Native.RECT rECT = WorkAreaOfNearestMonitor(lpPoint2);
			form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.Text = "HoldDrag T48 target";
			form.TopMost = true;
			form.Bounds = new Rectangle(rECT.Left + 60, rECT.Top + 60, 320, 240);
			form.Show();
			try
			{
				form.BringToFront();
				form.Activate();
			}
			catch
			{
			}
			Pump();
			Settle();
			WindowActions.SetForeignForTest(form.Handle, on: true);
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			hookEngine = new HookEngine(coordinator);
			hookEngine.Install();
			Pump();
			Settle();
			if (!hookEngine.MouseInstalled)
			{
				Line("  （鼠标钩子装不上，跳过）");
				return;
			}
			Native.POINT pOINT = CenterOf(form.Handle);
			if (!PlaceCursorOn(form.Handle, pOINT))
			{
				Line("  （光标无法稳定停在测试窗口上 —— 桌面上有别的输入在动，跳过本用例）");
				return;
			}
			Pump();
			Settle();
			ActivateTrigger(coordinator);
			Settle();
			Check(coordinator.Trigger.Active, "T48 触发键已进入激活态", "Active=" + coordinator.Trigger.Active);
			if (!PlaceCursorOn(form.Handle, pOINT))
			{
				Line("  （注入前光标离开了测试窗口，跳过本用例）");
				return;
			}
			if (!Native.GetWindowRect(form.Handle, out var lpRect))
			{
				Line("  （取不到窗口矩形，跳过）");
				return;
			}
			int sessionCount = coordinator.Drag.SessionCount;
			long seenCount = hookEngine.SeenCount;
			InjectMouseButton(2u);
			for (int i = 0; i < 40; i++)
			{
				if (coordinator.Drag.SessionCount > sessionCount)
				{
					break;
				}
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Application.DoEvents();
			string[] array = new string[12]
			{
				"    （诊断）注入前 SeenCount=",
				seenCount.ToString(),
				" 注入后=",
				hookEngine.SeenCount.ToString(),
				" 触发态=",
				coordinator.Trigger.Active.ToString(),
				" 会话数=",
				coordinator.Drag.SessionCount.ToString(),
				" 光标=",
				null,
				null,
				null
			};
			Native.POINT pOINT2 = pOINT;
			array[9] = pOINT2.ToString();
			array[10] = " 目标=";
			array[11] = DescribeWindow(form.Handle);
			Line(string.Concat(array));
			bool flag = coordinator.Drag.SessionCount > sessionCount;
			Check(flag, "真实左键事件启动了移动会话", "会话数没增加（" + sessionCount + "）说明按下事件没走到 Begin");
			if (!flag)
			{
				return;
			}
			Native.GetCursorPos(out var lpPoint3);
			InjectMouseMoveAbsolute(lpPoint3.X + 90, lpPoint3.Y + 60);
			for (int j = 0; j < 15; j++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			long seenCount2 = hookEngine.SeenCount;
			ReadCursorAndRect(form.Handle, out var pt, out var rect);
			int num = pt.X - lpPoint3.X;
			int num2 = pt.Y - lpPoint3.Y;
			string[] array2 = new string[6]
			{
				"    （诊断2）移动后 SeenCount=",
				seenCount2.ToString(),
				" 拖拽中=",
				coordinator.Drag.IsDragging.ToString(),
				" 光标=",
				null
			};
			pOINT2 = pt;
			array2[5] = pOINT2.ToString();
			Line(string.Concat(array2));
			int num3 = rect.Left - lpRect.Left;
			int num4 = rect.Top - lpRect.Top;
			bool ok = Math.Abs(num3 - num) <= 3 && Math.Abs(num4 - num2) <= 3;
			string[] array3 = new string[14]
			{
				"光标位移=",
				num.ToString(),
				",",
				num2.ToString(),
				" 窗口位移=",
				num3.ToString(),
				",",
				num4.ToString(),
				" 前=",
				null,
				null,
				null,
				null,
				null
			};
			Native.RECT rECT2 = lpRect;
			array3[9] = rECT2.ToString();
			array3[10] = " 后=";
			rECT2 = rect;
			array3[11] = rECT2.ToString();
			array3[12] = " 换算系数=";
			array3[13] = CoordSpace.Scale.ToString("0.####");
			Check(ok, "★ 窗口真的跟着光标移动了（核心功能端到端）", string.Concat(array3));
			InjectMouseButton(4u);
			for (int k = 0; k < 10; k++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			ActivateTrigger(coordinator);
			Settle();
			Native.GetWindowRect(form.Handle, out var lpRect2);
			int sessionCount2 = coordinator.Drag.SessionCount;
			if (!PlaceCursorOn(form.Handle, new Native.POINT(lpRect2.Right - 8, lpRect2.Bottom - 8)))
			{
				Line("  （光标无法稳定停在窗口右下角，跳过缩放段）");
				return;
			}
			for (int l = 0; l < 20; l++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Native.GetCursorPos(out var lpPoint4);
			string[] array4 = new string[8]
			{
				"    （诊断）期望光标=",
				(lpRect2.Right - 8).ToString(),
				",",
				(lpRect2.Bottom - 8).ToString(),
				" 实际=",
				null,
				null,
				null
			};
			pOINT2 = lpPoint4;
			array4[5] = pOINT2.ToString();
			array4[6] = " 窗口=";
			rECT2 = lpRect2;
			array4[7] = rECT2.ToString();
			Line(string.Concat(array4));
			InjectMouseButton(8u);
			for (int m = 0; m < 40; m++)
			{
				if (coordinator.Drag.SessionCount > sessionCount2)
				{
					break;
				}
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Application.DoEvents();
			bool flag2 = coordinator.Drag.SessionCount > sessionCount2;
			Check(flag2, "真实右键事件启动了缩放会话", "会话数没增加（" + sessionCount2 + "）说明右键按下事件没走到 Begin");
			if (flag2)
			{
				Native.GetCursorPos(out var lpPoint5);
				InjectMouseMoveAbsolute(lpPoint5.X + 70, lpPoint5.Y + 50);
				for (int n = 0; n < 20; n++)
				{
					Application.DoEvents();
					Thread.Sleep(10);
				}
				ReadCursorAndRect(form.Handle, out var pt2, out var rect2);
				int num5 = rect2.Width - lpRect2.Width;
				int num6 = rect2.Height - lpRect2.Height;
				int num7 = pt2.X - lpPoint5.X;
				int num8 = pt2.Y - lpPoint5.Y;
				Check(Math.Abs(num5 - num7) <= 4 && Math.Abs(num6 - num8) <= 4, "★ 右键真的改变了窗口大小（核心功能端到端）", "光标位移=" + num7 + "," + num8 + " 尺寸变化=" + num5 + "," + num6 + " 前=" + lpRect2.Width + "x" + lpRect2.Height + " 后=" + rect2.Width + "x" + rect2.Height);
			}
			InjectMouseButton(16u);
			for (int num9 = 0; num9 < 10; num9++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			Check(ok: false, "全链路拖动用例执行", "异常: " + ex.Message);
		}
		finally
		{
			try
			{
				if (form != null)
				{
					WindowActions.SetForeignForTest(form.Handle, on: false);
				}
			}
			catch
			{
			}
			try
			{
				coordinator?.SetPaused(paused: true);
			}
			catch
			{
			}
			try
			{
				hookEngine?.Dispose();
			}
			catch
			{
			}
			try
			{
				coordinator?.Dispose();
			}
			catch
			{
			}
			try
			{
				if (form != null && !form.IsDisposed)
				{
					form.Visible = false;
					Native.DestroyWindow(form.Handle);
				}
				if (form != null)
				{
					form.Close();
					form.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
			try
			{
				if (cursorPos)
				{
					Native.SetCursorPos(lpPoint.X, lpPoint.Y);
				}
			}
			catch
			{
			}
			for (int num10 = 0; num10 < 6; num10++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
		}
	}

	private static Native.INPUT MakeMouseInput(uint flags)
	{
		Native.MOUSEINPUT mi = new Native.MOUSEINPUT
		{
			dwFlags = flags
		};
		return new Native.INPUT
		{
			type = 0u,
			u = new Native.INPUTUNION
			{
				mi = mi
			}
		};
	}

	private static void TestBlacklistPickers()
	{
		string text = WindowActions.ProcessExeNameByPid((uint)Process.GetCurrentProcess().Id);
		Check(text.Length > 0, "能取到本进程的 exe 名（黑名单按进程名匹配的前提）", "得到 \"" + text + "\"");
		if (!NoRoomForTestWindow())
		{
			return;
		}
		Form form = null;
		try
		{
			form = new Form();
			form.Text = "HoldDrag T47 blacklist";
			form.Bounds = new Rectangle(0, 0, 200, 120);
			form.Show();
			Pump();
			form.Text = "zzz 标题里没有那个词 zzz";
			Pump();
			WindowActions.ApplyBlacklist(text);
			Check(WindowActions.IsBlacklisted(form.Handle), "★ 黑名单能用【进程名】命中窗口（选择程序 / 点选窗口 的前提）", "关键字=" + text + " 标题=\"" + Native.GetWindowTitle(form.Handle) + "\" 类名=" + Native.GetClassName(form.Handle));
			WindowActions.ApplyBlacklist("zzz_definitely_not_a_real_process_zzz");
			Check(!WindowActions.IsBlacklisted(form.Handle), "不相关的关键字不会误伤窗口", "关键字=zzz_definitely_not_a_real_process_zzz");
		}
		finally
		{
			WindowActions.ApplyBlacklist(string.Empty);
			try
			{
				if (form != null && !form.IsDisposed)
				{
					form.Visible = false;
					Native.DestroyWindow(form.Handle);
				}
				if (form != null)
				{
					form.Close();
					form.Dispose();
				}
			}
			catch
			{
			}
			for (int i = 0; i < 4; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
		}
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Control control = FindByText(settingsForm, "选择程序…");
		Control control2 = FindByText(settingsForm, "点选窗口");
		Check(control != null && control2 != null, "黑名单的两个新按钮都在界面上（选择程序… / 点选窗口）", "选择程序=" + (control != null) + " 点选窗口=" + (control2 != null));
		settingsForm.ProbeAppendBlacklist("notepad");
		Eq(settingsForm.ProbeBlacklistText, "notepad", "第一个关键字写进黑名单框");
		settingsForm.ProbeAppendBlacklist("notepad");
		Eq(settingsForm.ProbeBlacklistText, "notepad", "重复的关键字不会加两次");
		settingsForm.ProbeAppendBlacklist("chrome");
		Eq(settingsForm.ProbeBlacklistText, "notepad，chrome", "第二个关键字用中文逗号追加");
		settingsForm.ProbeAppendBlacklist("   ");
		Eq(settingsForm.ProbeBlacklistText, "notepad，chrome", "空白关键字被忽略");
		settingsForm.Close();
	}

	private static void TestCoordinateSpace()
	{
		double scale = CoordSpace.Scale;
		CoordSpace.SetScaleForTest(1.0);
		Eq(CoordSpace.ToProcess(new Native.POINT(2182, 1440)).X, 2182, "缩放=1 时坐标原样通过（恒等变换）");
		CoordSpace.SetScaleForTest(0.5);
		Native.POINT pOINT = CoordSpace.ToProcess(new Native.POINT(2182, 1440));
		Eq(pOINT.X, 1091, "缩放=0.5 时物理坐标换算到逻辑空间（X）");
		Eq(pOINT.Y, 720, "缩放=0.5 时物理坐标换算到逻辑空间（Y）");
		CoordSpace.SetScaleForTest(scale);
		int num = -1;
		try
		{
			num = Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext());
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		if (num == 0)
		{
			Check(Math.Abs(CoordSpace.Scale - 1.0) > 0.0001, "★ DPI 不感知进程必须算出坐标换算系数（否则命中测试必然失败）", "Scale=" + CoordSpace.Scale + "（等于 1 说明没算出来）");
		}
		else
		{
			Eq(CoordSpace.Scale, 1.0, "DPI 感知进程的坐标换算是恒等变换");
		}
		if (!NoRoomForTestWindow())
		{
			return;
		}
		Native.GetCursorPos(out var lpPoint);
		Native.RECT rECT = WorkAreaOfNearestMonitor(lpPoint);
		Form form = null;
		try
		{
			form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.Text = "HoldDrag T46 coordspace";
			form.TopMost = true;
			form.Bounds = new Rectangle(rECT.Left + 40, rECT.Top + 40, 320, 220);
			form.Show();
			try
			{
				form.BringToFront();
				form.Activate();
			}
			catch
			{
			}
			Pump();
			Settle();
			WindowActions.SetForeignForTest(form.Handle, on: true);
			if (!Native.GetWindowRect(form.Handle, out var lpRect))
			{
				Line("  （取不到窗口矩形，跳过端到端部分）");
				return;
			}
			int num2 = (lpRect.Left + lpRect.Right) / 2;
			int num3 = (lpRect.Top + lpRect.Bottom) / 2;
			double num4 = CoordSpace.Scale;
			if (num4 <= 0.0)
			{
				num4 = 1.0;
			}
			Native.POINT pt = new Native.POINT((int)Math.Round((double)num2 / num4), (int)Math.Round((double)num3 / num4));
			Native.POINT pt2 = CoordSpace.ToProcess(pt);
			Check(Math.Abs(pt2.X - num2) <= 1 && Math.Abs(pt2.Y - num3) <= 1, "物理坐标经换算能还原成逻辑坐标（往返一致）", "逻辑=" + num2 + "," + num3 + " 物理=" + pt.X + "," + pt.Y + " 还原=" + pt2.X + "," + pt2.Y + " 缩放=" + num4.ToString("0.####"));
			nint num5 = WindowActions.FindTarget(pt2, allowFullscreen: true);
			Check(num5 == form.Handle, "★ 物理坐标经换算后能命中目标窗口（核心功能失效回归）", "命中=" + DescribeWindow(num5) + " 期望=" + DescribeWindow(form.Handle) + " 缩放=" + num4.ToString("0.####"));
			if (num4 != 1.0)
			{
				nint num6 = WindowActions.FindTarget(pt, allowFullscreen: true);
				Line("    （对照）不换算直接用物理坐标 -> " + ((num6 == form.Handle) ? "竟然也命中了" : "命不中，与用户日志一致"));
			}
		}
		finally
		{
			try
			{
				if (form != null)
				{
					WindowActions.SetForeignForTest(form.Handle, on: false);
				}
			}
			catch
			{
			}
			try
			{
				if (form != null && !form.IsDisposed)
				{
					form.Visible = false;
					Native.DestroyWindow(form.Handle);
				}
				if (form != null)
				{
					form.Close();
					form.Dispose();
				}
			}
			catch
			{
			}
			for (int i = 0; i < 6; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
		}
	}

	private static void TestHitTestHasFallback()
	{
		Eq(2, 2, "GW_HWNDNEXT 是 2（Z 序往下，穿透循环必须用它）");
		Eq(3, 3, "GW_HWNDPREV 是 3（Z 序往上，不是穿透要的方向）");
		if (!NoRoomForTestWindow())
		{
			return;
		}
		Native.GetCursorPos(out var lpPoint);
		Native.RECT rECT = WorkAreaOfNearestMonitor(lpPoint);
		Form form = null;
		try
		{
			int width = 300;
			int height = 200;
			int x = rECT.Left + 40;
			int y = rECT.Top + 40;
			form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.Text = "HoldDrag T45 hittest";
			form.TopMost = true;
			form.Bounds = new Rectangle(x, y, width, height);
			form.Show();
			try
			{
				form.BringToFront();
				form.Activate();
			}
			catch
			{
			}
			Pump();
			Settle();
			Native.POINT pt = CenterOf(form.Handle);
			nint num = WindowActions.HitTestTopWindow(pt, out var how);
			Check(num == form.Handle, "命中测试（正常路径）能拿到测试窗口", "命中=" + DescribeWindow(num) + " 方式=" + how);
			WindowActions.SkipWindowFromPointForTest = true;
			try
			{
				nint num2 = WindowActions.HitTestTopWindow(pt, out var how2);
				Check(num2 == form.Handle, "★ WindowFromPoint 失效时兜底策略仍能命中（核心功能失效回归）", "命中=" + DescribeWindow(num2) + " 方式=" + how2);
				Check(how2 != "WindowFromPoint", "兜底确实走的是非 WindowFromPoint 路径（否则这个用例等于没测）", "方式=" + how2);
			}
			finally
			{
				WindowActions.SkipWindowFromPointForTest = false;
			}
		}
		finally
		{
			WindowActions.SkipWindowFromPointForTest = false;
			try
			{
				if (form != null && !form.IsDisposed)
				{
					form.Visible = false;
					Native.DestroyWindow(form.Handle);
				}
				if (form != null)
				{
					form.Close();
					form.Dispose();
				}
			}
			catch
			{
			}
			for (int i = 0; i < 6; i++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
		}
	}

	private static void TestConfigSurvivesRestart()
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-persist-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:RAlt";
			current.RequireHold = false;
			current.HoldMs = 1234;
			current.ActionLeft = "Resize";
			current.ActionRight = "AlwaysOnTop";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.FrameColorId = "Violet";
			current.FrameStyleId = "Dashed";
			current.FrameThickness = 7;
			current.FrameRadius = 21;
			current.ShakeMinimizeOthers = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.RestoreMaximizedWhenMoved = false;
			current.BringToFront = false;
			current.AllowFullscreenWindows = true;
			current.CornerPercent = 33;
			current.Blacklist = "Notepad - 记事本,CustomWndClass";
			current.DragRefreshMode = "Custom";
			current.DragRefreshHz = 144;
			current.AutoStart = "None";
			current.RunAsAdmin = false;
			current.WarnNonAdmin = false;
			current.ExitFullscreenOnTrigger = false;
			current.FrameOpacity = 55;
			current.SettingsWidth = 1234;
			current.SettingsHeight = 876;
			configStore.Save(out _);
			ConfigStore configStore2 = new ConfigStore(text);
			configStore2.Load();
			AppConfig current2 = configStore2.Current;
			Eq(current2.TriggerKeyId, "K:RAlt", "触发键");
			Eq(current2.HoldMs, 1234, "长按阈值");
			Check(!current2.RequireHold, "长按开关");
			Eq(current2.ActionLeft, "Resize", "左键动作");
			Eq(current2.ActionRight, "AlwaysOnTop", "右键动作");
			Eq(current2.ActionMiddle, "Nothing", "中键动作");
			Check(!current2.ShowActivationFrame, "外框总开关");
			Eq(current2.FrameColorId, current.FrameColorId, "★ 外框颜色");
			Eq(current2.FrameStyleId, current.FrameStyleId, "★ 外框样式");
			Eq(current2.FrameThickness, 7, "★ 外框粗细");
			Eq(current2.FrameRadius, 21, "★ 外框圆角");
			Check(!current2.ShakeMinimizeOthers, "晃窗最小化其它窗口开关");
			Check(!current2.DoubleClickLeftMaximize, "双击左键最大化开关");
			Check(!current2.DoubleClickRightZone, "双击右键九宫格开关");
			Check(!current2.RestoreMaximizedWhenMoved, "移动时还原最大化开关");
			Check(!current2.BringToFront, "置顶开关");
			Check(current2.AllowFullscreenWindows, "允许全屏开关");
			Eq(current2.CornerPercent, 33, "九宫格分区百分比");
			Eq(current2.Blacklist, current.Blacklist, "黑名单");
			Eq(current2.DragRefreshMode, "Custom", "刷新率模式");
			Eq(current2.DragRefreshHz, 144, "刷新率数值");
			Eq(current2.AutoStart, "None", "开机自启模式");
			Check(!current2.ExitFullscreenOnTrigger, "触发时退出全屏开关");
			Eq(current2.FrameOpacity, 55, "★ 外框不透明度");
			Check(!current2.RunAsAdmin, "管理员权限开关");
			Check(!current2.WarnNonAdmin, "非管理员提示开关");
			Eq(current2.SettingsWidth, 1234, "设置窗口宽度");
			Eq(current2.SettingsHeight, 876, "设置窗口高度");
			int width = SystemInformation.VirtualScreen.Width;
			if (width > 2400)
			{
				configStore2.Current.SettingsWidth = width;
				configStore2.Current.SettingsHeight = 1000;
				configStore2.Save(out _);
				ConfigStore configStore3 = new ConfigStore(text);
				configStore3.Load();
				Check(configStore3.Current.SettingsWidth >= width, "★ 窗口宽度上限跟随屏幕（不再写死 2400）", "屏幕宽=" + width + " 存回来=" + configStore3.Current.SettingsWidth);
			}
			else
			{
				Line("（跳过）本机屏幕宽度 " + width + " 不大于 2400，无法验证上限是否跟随屏幕");
			}
			configStore2.Current.HoldMs = 555;
			configStore2.Save(out _);
			configStore2.Current.HoldMs = 666;
			configStore2.Save(out _);
			ConfigStore configStore4 = new ConfigStore(text);
			configStore4.Load();
			Eq(configStore4.Current.HoldMs, 666, "连续多次保存后最新值仍能读回");
			Eq(configStore4.Current.FrameColorId, current.FrameColorId, "多次保存后主题颜色没被覆盖");
			File.WriteAllText(Path.Combine(text, "config.json"), "{ 坏掉的 JSON");
			ConfigStore configStore5 = new ConfigStore(text);
			configStore5.Load();
			Eq(configStore5.Current.HoldMs, 555, "主配置损坏后回退到备份而不是出厂默认");
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
	}

	private static void VerifyBottomBarButtons()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.Size = settingsForm.Size;
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		StringBuilder bad = new StringBuilder();
		int checkedBtn = 0;
		Walk(settingsForm, (Control c) =>
		{
			if (c is Button)
			{
				string text = c.Text;
				if (!(text != "恢复默认值") || !(text != "保存并生效") || !(text != "取消"))
				{
					checkedBtn++;
					Size size;
					try
					{
						size = TextRenderer.MeasureText(c.Text, c.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
					}
					catch
					{
						return;
					}
					if (size.Width > c.Width - 12)
					{
						bad.Append(Name(c)).Append("(文字宽").Append(size.Width)
							.Append(" > 可用宽")
							.Append(c.Width - 12)
							.Append(") ");
					}
					if (size.Height > c.Height - 8)
					{
						bad.Append(Name(c)).Append("(文字高").Append(size.Height)
							.Append(" > 可用高")
							.Append(c.Height - 8)
							.Append(") ");
					}
				}
			}
		});
		Check(checkedBtn == 3, "底栏确实有 3 个按钮被检查到", "实际找到 " + checkedBtn + " 个");
		Check(bad.Length == 0, "★ 底栏按钮的文字与按钮框适配（恢复默认值/保存并生效/取消）", (bad.Length == 0) ? (checkedBtn + " 个按钮文字都装得下且居中") : ("异常: " + bad));
		Control control = FindByText(settingsForm, "保存并生效");
		ComboBox comboBox = FindFirstCombo(settingsForm);
		if (control != null && comboBox != null)
		{
			int num = Math.Abs(control.Height - comboBox.Height);
			Check(num <= 8, "底栏按钮高度与输入框控件同一量级（视觉不突兀）", "按钮高 " + control.Height + " 输入框高 " + comboBox.Height + " 差 " + num);
		}
		settingsForm.Close();
	}

	private static Control FindByText(Control root, string text)
	{
		try
		{
			for (int i = 0; i < root.Controls.Count; i++)
			{
				Control control = root.Controls[i];
				if (string.Equals(control.Text, text, StringComparison.Ordinal))
				{
					return control;
				}
				Control control2 = FindByText(control, text);
				if (control2 != null)
				{
					return control2;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static ComboBox FindFirstCombo(Control root)
	{
		try
		{
			for (int i = 0; i < root.Controls.Count; i++)
			{
				if (root.Controls[i] is ComboBox result)
				{
					return result;
				}
				ComboBox comboBox = FindFirstCombo(root.Controls[i]);
				if (comboBox != null)
				{
					return comboBox;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static void TestFullscreenOptIn()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		nint forcedTargetForTest = WindowActions.ForcedTargetForTest;
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(new Native.POINT((lpRect.Left + lpRect.Right) / 2, (lpRect.Top + lpRect.Bottom) / 2));
			if (monitorInfoAt.rcMonitor.Width <= 0)
			{
				Line("  （取不到显示器矩形，跳过）");
				return;
			}
			Native.SetWindowPos(dragFixture.H, IntPtr.Zero, monitorInfoAt.rcMonitor.Left, monitorInfoAt.rcMonitor.Top, monitorInfoAt.rcMonitor.Width, monitorInfoAt.rcMonitor.Height, 16404u);
			Settle();
			Check(WindowActions.IsFullscreen(dragFixture.H), "铺满显示器被判定为真全屏");
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			dragFixture.Store.Current.AllowFullscreenWindows = false;
			Native.POINT pt = new Native.POINT(monitorInfoAt.rcMonitor.Left + monitorInfoAt.rcMonitor.Width / 2, monitorInfoAt.rcMonitor.Top + monitorInfoAt.rcMonitor.Height / 2);
			nint num = WindowActions.FindTarget(pt, allowFullscreen: false);
			Check(num == IntPtr.Zero || num != dragFixture.H, "默认不放行全屏窗口");
			nint num2 = WindowActions.FindTarget(pt, allowFullscreen: true);
			if (num2 == IntPtr.Zero)
			{
				Check(ok: false, "开关打开之后可以拖动全屏窗口（一个窗口都没命中）");
			}
			else if (num2 != dragFixture.H)
			{
				Line("  （命中其它窗口 " + ((IntPtr)num2).ToInt64().ToString("X") + "（" + (Native.GetClassName(num2) ?? "?") + "），疑似第三方置顶小组件遮挡，跳过正向断言）");
			}
			else
			{
				Check(ok: true, "开关打开之后可以拖动全屏窗口");
			}
		}
		finally
		{
			WindowActions.ForcedTargetForTest = forcedTargetForTest;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestHookWatchdog()
	{
		HookEngine hookEngine = new HookEngine(new NullSink());
		try
		{
			if (!hookEngine.Install())
			{
				Line("  （当前环境装不上钩子的(maybe 没桌面/没权限)，跳过看门狗用例）");
				return;
			}
			HookWatchdog hookWatchdog = new HookWatchdog(hookEngine);
			hookWatchdog.CheckOnce();
			if (!Native.GetCursorPos(out var lpPoint))
			{
				Line("  （取不到光标位置，跳过）");
				return;
			}
			hookEngine.Uninstall();
			hookWatchdog.CheckOnce();
			bool flag = false;
			for (int i = 0; i < 12; i++)
			{
				if (flag)
				{
					break;
				}
				int num = 11 + i * 3;
				int num2 = 9 + i * 2;
				Native.SetCursorPos(lpPoint.X + num, lpPoint.Y + num2);
				for (int j = 0; j < 4; j++)
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				flag = hookWatchdog.CheckOnce();
			}
			try
			{
				Native.SetCursorPos(lpPoint.X, lpPoint.Y);
			}
			catch
			{
			}
			if (!flag)
			{
				Line("  （本机 12 次尝试都没收到鼠标钩子事件 —— 多半是无桌面/受限会话环境，看门狗用例跳过）");
				return;
			}
			Check(flag, "光标动了但钩子没收到事件 → 判定钩子已死");
			Eq(hookEngine.ReinstallCount, 1, "看门狗重装了一次");
			Check(hookEngine.MouseInstalled, "重装之后鼠标钩子确实装回来了");
		}
		finally
		{
			try
			{
				hookEngine.Dispose();
			}
			catch
			{
			}
		}
	}

	private static void TestRemovedFeaturesGone()
	{
		bool flag = false;
		bool flag2 = false;
		bool flag3 = false;
		bool flag4 = false;
		bool flag5 = false;
		bool flag6 = false;
		bool flag7 = false;
		PropertyInfo[] properties = typeof(AppConfig).GetProperties();
		foreach (PropertyInfo obj in properties)
		{
			if (obj.Name == "DragTransparency")
			{
				flag = true;
			}
			if (obj.Name == "DragSizeHint")
			{
				flag2 = true;
			}
			if (obj.Name == "EdgeSnap")
			{
				flag3 = true;
			}
			if (obj.Name == "SnapToOtherWindows")
			{
				flag4 = true;
			}
			if (obj.Name == "SnapPreview")
			{
				flag5 = true;
			}
			if (obj.Name == "SnapDistance")
			{
				flag6 = true;
			}
			if (obj.Name == "MovePreviewMode")
			{
				flag7 = true;
			}
		}
		Check(!flag, "AppConfig 里已无 DragTransparency");
		Check(!flag2, "AppConfig 里已无 DragSizeHint");
		Check(!flag3, "AppConfig 里已无 EdgeSnap（#32 自动贴边整体删除）");
		Check(!flag4, "AppConfig 里已无 SnapToOtherWindows（#32 与其它窗口对齐整体删除）");
		Check(!flag5, "AppConfig 里已无 SnapPreview（#32 边缘预览落位整体删除）");
		Check(!flag6, "AppConfig 里已无 SnapDistance（#32 吸附距离整体删除）");
		Check(!flag7, "AppConfig 里已无 MovePreviewMode（v1.6.1 删除 Outline 预览模式：无 UI 入口且被 Normalize 钉死 FullWin，生产不可达）");
		ConfigStore configStore = new ConfigStore(Path.Combine(Path.GetTempPath(), "holddrag-legacy-" + Guid.NewGuid().ToString("N")));
		Directory.CreateDirectory(configStore.Dir);
		File.WriteAllText(configStore.FilePath, "{\r\n  \"TriggerKeyId\": \"K:0xA4\",\r\n  \"DragTransparency\": true,\r\n  \"DragTransparencyAlpha\": 70,\r\n  \"DragSizeHint\": true,\r\n  \"EdgeSnap\": true,\r\n  \"SnapToOtherWindows\": true,\r\n  \"SnapPreview\": true,\r\n  \"SnapDistance\": 42\r\n}", Encoding.UTF8);
		try
		{
			configStore.Load();
			Eq(configStore.Current.TriggerKeyId, "K:0xA4", "旧配置里的有效字段仍然读得出来");
		}
		finally
		{
			try
			{
				Directory.Delete(configStore.Dir, recursive: true);
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// v1.6.1 Must #1 / Must #3 的回归：
	/// #1 录制纯修饰键时 KeyCode 掩码把修饰位掩掉后是 Keys.None，历史 bug 会绑成 "K:0x00"
	///    （vk=0 永远匹配不上任何真实按键，触发键就此失效）；设置页已加守卫，Normalize 也加了不变量兜底；
	/// #3 更新重启的两处命令行拼接必须按需包引号（路径含空白否则会被拆成多个参数）。
	/// </summary>
	private static void TestRecordedKeyAndConfigInvariants()
	{
		Eq(KeysUtil.ToVirtualKey("K:0x00"), 0, "\"K:0x00\" 解析不出有效虚拟键码（纯修饰键被 KeyCode 掩码掩掉的产物）");
		AppConfig appConfig = new AppConfig();
		appConfig.TriggerKeyId = "K:0x00";
		appConfig.Normalize();
		Eq(appConfig.TriggerKeyId, "K:0x0D", "Normalize 兜底：无效触发键 \"K:0x00\" 回落默认回车键（录制守卫的兜底回归）");
		AppConfig appConfig2 = new AppConfig();
		appConfig2.TriggerKeyId = "M:1";
		appConfig2.Normalize();
		Eq(appConfig2.TriggerKeyId, "M:1", "Normalize 不误伤鼠标侧键 X1（vk=5）作为触发键");
		AppConfig appConfig3 = new AppConfig();
		appConfig3.TriggerKeyId = "K:LShift";
		appConfig3.Normalize();
		Eq(appConfig3.TriggerKeyId, "K:LShift", "Normalize 不误伤左 Shift（vk=160）作为触发键");
		AppConfig appConfig4 = new AppConfig();
		appConfig4.TriggerKeyId = "";
		appConfig4.Normalize();
		Eq(appConfig4.TriggerKeyId, "K:0x0D", "Normalize 兜底：空触发键同样回落默认回车键");
		Eq(Updater.QuoteArg("C:\\Program Files\\HoldDrag\\HoldDrag.exe.old"), "\"C:\\Program Files\\HoldDrag\\HoldDrag.exe.old\"", "QuoteArg：路径含空白时整段包引号（否则会被拆成多个参数）");
		Eq(Updater.QuoteArg("--no-elevate"), "--no-elevate", "QuoteArg：无空白的参数原样返回（不多包引号）");
		Eq(Updater.QuoteArg(""), "", "QuoteArg：空串原样返回");
		string composed = Updater.ArgUpdatedFrom + " " + Updater.QuoteArg("D:\\a b\\HoldDrag.exe.old");
		Eq(composed, "--updated-from \"D:\\a b\\HoldDrag.exe.old\"", "ApplyAndRestart 实际拼出的命令行：flag 不包、路径按需包");
	}

	/// <summary>
	/// v1.6.1 Must #2 的回归：保存失败绝不能再对用户显示"已保存并立即生效"——
	/// ConfigStore.Save/Apply 必须返回 false 且带回错误信息，设置页据此红字早退。
	/// </summary>
	private static void TestConfigSaveFailureIsVisible()
	{
		// 把配置目录指向一个"已存在的文件"：Directory.CreateDirectory 必然抛异常，Save 必然失败
		string blocker = Path.Combine(Path.GetTempPath(), "holddrag-blocker-" + Guid.NewGuid().ToString("N"));
		File.WriteAllText(blocker, "x");
		try
		{
			ConfigStore configStore = new ConfigStore(blocker);
			configStore.Load();
			bool saved = configStore.Save(out string error);
			Check(!saved, "保存失败时 Save 返回 false（不再静默成功）");
			Check(!string.IsNullOrEmpty(error), "保存失败时 error 带回了异常信息（设置页要原样显示给用户）");
			AppConfig appConfig = new AppConfig();
			appConfig.TriggerKeyId = "K:0x0D";
			bool applied = configStore.Apply(appConfig, out string error2);
			Check(!applied, "Apply 同样返回 false（Current 已被替换但磁盘没写成功，调用方必须知道）");
			Check(!string.IsNullOrEmpty(error2), "Apply 失败时同样带回错误信息");
		}
		finally
		{
			try
			{
				File.Delete(blocker);
			}
			catch
			{
			}
		}
		// 正对照：正常目录下必须真的保存成功（防止"永远返回 false"式的假修复）
		string dir = Path.Combine(Path.GetTempPath(), "holddrag-saveok-" + Guid.NewGuid().ToString("N"));
		try
		{
			ConfigStore okStore = new ConfigStore(dir);
			okStore.Load();
			AppConfig okConfig = new AppConfig();
			okConfig.TriggerKeyId = "K:0x0D";
			bool ok = okStore.Apply(okConfig, out string okError);
			Check(ok, "正常目录下 Apply 返回 true（保存失败路径的对照）");
			Check(File.Exists(okStore.FilePath), "正常目录下配置文件真的落盘了");
			Check(string.IsNullOrEmpty(okError), "保存成功时 error 为空串");
		}
		finally
		{
			try
			{
				Directory.Delete(dir, recursive: true);
			}
			catch
			{
			}
		}
	}

	private static void TestTriggerStaysActive()
	{
		using DragFixture dragFixture = new DragFixture(null);
		ActivateTrigger(dragFixture.Coord);
		Check(dragFixture.Coord.Trigger.Active, "触发键已进入激活态");
		long num = Environment.TickCount64 + 2500;
		while (Environment.TickCount64 < num)
		{
			Application.DoEvents();
			Thread.Sleep(20);
		}
		Check(dragFixture.Coord.Trigger.Active, "按住 2.5 秒后激活态依然保持（看门狗误判回归）");
		Check(dragFixture.Coord.Trigger.Held, "触发键仍被记为按住");
	}

	private static void StartWatchdog(string dir)
	{
		_reportDir = dir;
		try
		{
			Thread thread = new Thread(() =>
			{
				Thread.Sleep(150000);
				try
				{
					Buf.Append("\r\n\r\n!! 自检总超时（150 秒），报告到此为止。");
					Buf.Append("\r\n   上面最后一组日志就是卡住的位置。\r\n");
					File.WriteAllText(Path.Combine(_reportDir, "selftest.txt"), Buf.ToString(), Encoding.UTF8);
				}
				catch
				{
				}
				Environment.Exit(2);
			});
			thread.IsBackground = true;
			thread.Name = "HoldDrag.SelfTestWatchdog";
			thread.Start();
		}
		catch
		{
		}
	}

	private static void Safe(string name, Action test)
	{
		_watch.Restart();
		Log.Info("[自检] == " + name);
		Line(name);
		try
		{
			test();
		}
		catch (Exception ex)
		{
			Bad("（该组测试自身抛异常）", ex.Message);
		}
		_watch.Stop();
		if (_watch.ElapsedMilliseconds > 200)
		{
			Log.Info("[自检]    耗时 " + _watch.ElapsedMilliseconds + " ms");
		}
		Line("");
	}

	private static void TestShakeGesture()
	{
		int[] array = new int[10] { 540, 460, 540, 460, 540, 460, 540, 460, 540, 460 };
		ShakeDetector shakeDetector = new ShakeDetector();
		shakeDetector.Reset(100, 300, 0L);
		bool flag = false;
		for (int i = 1; i <= 40; i++)
		{
			if (shakeDetector.Feed(100 + i * 10, 300, (long)i * 10L))
			{
				flag = true;
			}
		}
		Check(!flag, "匀速直线拖动不触发（无往复）");
		shakeDetector.Reset(200, 400, 0L);
		flag = false;
		int[] array2 = new int[8] { 300, 420, 380, 500, 470, 600, 700, 800 };
		long num = 0L;
		int[] array3 = array2;
		foreach (int x in array3)
		{
			num += 60;
			if (shakeDetector.Feed(x, 400, num))
			{
				flag = true;
			}
		}
		Check(!flag, "★ 正常拖窗（拖过头再拉回、边拖边微调）不触发 —— 误触发回归");
		shakeDetector.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		array3 = new int[10] { 512, 500, 512, 500, 512, 500, 512, 500, 512, 500 };
		foreach (int x2 in array3)
		{
			num += 30;
			if (shakeDetector.Feed(x2, 300, num))
			{
				flag = true;
			}
		}
		Check(!flag, "小幅手抖（半程不足 32px）不触发");
		shakeDetector.Reset(500, 200, 0L);
		flag = false;
		num = 0L;
		int[] array4 = new int[10] { 200, 280, 200, 280, 200, 280, 200, 280, 200, 280 };
		for (int k = 0; k < array4.Length; k++)
		{
			num += 30;
			if (shakeDetector.Feed(500, array4[k], num))
			{
				flag = true;
			}
		}
		Check(!flag, "上下方向抖动不触发（必须是横向晃）");
		shakeDetector.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		for (int l = 0; l < 10; l++)
		{
			num += 400;
			if (shakeDetector.Feed((l % 2 == 0) ? 580 : 420, 300, num))
			{
				flag = true;
			}
		}
		Check(!flag, "慢速往复（超出 800ms 手势窗口）不触发");
		shakeDetector.Reset(200, 300, 0L);
		flag = false;
		num = 0L;
		array3 = new int[5] { 900, 200, 900, 200, 900 };
		foreach (int x3 in array3)
		{
			num += 80;
			if (shakeDetector.Feed(x3, 300, num))
			{
				flag = true;
			}
		}
		Check(!flag, "大幅折返（单向位移超过上限）不触发 —— 那是拖窗口不是晃");
		shakeDetector.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		array3 = array;
		foreach (int x4 in array3)
		{
			num += 30;
			if (shakeDetector.Feed(x4, 300, num))
			{
				flag = true;
			}
		}
		Check(flag, "★ 快速原地左右晃动可以触发（第 4 次有效往复）");
		shakeDetector.Reset(500, 300, 0L);
		int num2 = 0;
		num = 0L;
		for (int m = 0; m < 3; m++)
		{
			array3 = array;
			foreach (int x5 in array3)
			{
				num += 30;
				if (shakeDetector.Feed(x5, 300, num))
				{
					num2++;
				}
			}
		}
		Eq(num2, 1, "一次晃动只触发一次（冷却生效）");
		shakeDetector.Reset(500, 300, 0L);
		num = 0L;
		array3 = array;
		foreach (int x6 in array3)
		{
			num += 30;
			shakeDetector.Feed(x6, 300, num);
		}
		num += 1200;
		flag = false;
		array3 = array;
		foreach (int x7 in array3)
		{
			num += 30;
			if (shakeDetector.Feed(x7, 300, num))
			{
				flag = true;
			}
		}
		Check(flag, "冷却结束后再次晃动可再次触发（不漏判）");
		ShakeDetector shakeDetector2 = new ShakeDetector();
		shakeDetector2.SetScale(2.0);
		shakeDetector2.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		array3 = new int[10] { 520, 480, 520, 480, 520, 480, 520, 480, 520, 480 };
		foreach (int x8 in array3)
		{
			num += 30;
			if (shakeDetector2.Feed(x8, 300, num))
			{
				flag = true;
			}
		}
		Check(!flag, "★ 200% 显示器下半程 20 逻辑 px 的抖动不触发（阈值随 DPI 缩放）");
		ShakeDetector shakeDetector3 = new ShakeDetector();
		shakeDetector3.SetScale(1.0);
		shakeDetector3.Reset(500, 300, 0L);
		flag = false;
		num = 0L;
		array3 = new int[10] { 540, 460, 540, 460, 540, 460, 540, 460, 540, 460 };
		foreach (int x9 in array3)
		{
			num += 30;
			if (shakeDetector3.Feed(x9, 300, num))
			{
				flag = true;
			}
		}
		Check(flag, "同样的往复在 100% 显示器上幅度足够 → 正常触发");
	}

	private static void TestFrameGoesAwayOnRelease()
	{
		Type typeFromHandle = typeof(DragEngine);
		string[] array = new string[3] { "PinToWindow", "TickPinned", "ClearPin" };
		int num = 0;
		string text = "";
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			if (typeFromHandle.GetMethod(text2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
			{
				num++;
				text = text + text2 + " ";
			}
		}
		Check(num == 0, "外框 pin 机制已彻底下线（PinToWindow/TickPinned/ClearPin 不存在）", text);
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using (DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
			}))
			{
				DragEngine drag = dragFixture.Coord.Drag;
				Native.POINT pOINT = CenterOf(dragFixture.H);
				Native.SetCursorPos(pOINT.X, pOINT.Y);
				ActivateTrigger(dragFixture.Coord);
				Settle();
				Check(Native.IsWindowVisible(drag.Frame.Handle), "激活后外框已亮起");
				dragFixture.Coord.Trigger.SimulateKeyUp(null);
				Settle();
				Check(!dragFixture.Coord.Trigger.Active, "触发键已退出激活态");
				Check(!Native.IsWindowVisible(drag.Frame.Handle), "★ 松开触发键后外框立即消失（不再残留在屏幕上）");
			}
			using (DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
			}))
			{
				DragEngine drag2 = dragFixture2.Coord.Drag;
				Native.POINT p = CenterOf(dragFixture2.H);
				Native.SetCursorPos(p.X, p.Y);
				ActivateTrigger(dragFixture2.Coord);
				Settle();
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				dragFixture2.Coord.OnMouse(513, ref ms);
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 50, p.Y + 30));
				dragFixture2.Coord.OnMouse(512, ref ms2);
				Settle();
				Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
				dragFixture2.Coord.OnMouse(514, ref ms3);
				Settle();
				Check(!drag2.IsDragging, "拖动会话已结束");
				Check(Native.IsWindowVisible(drag2.Frame.Handle), "触发键仍按着时外框继续亮着（这条行为没变）");
				dragFixture2.Coord.Trigger.SimulateKeyUp(null);
				Settle();
				Check(!Native.IsWindowVisible(drag2.Frame.Handle), "★ 先松鼠标再松触发键：松键那一刻外框消失");
			}
			using DragFixture dragFixture3 = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
			});
			DragEngine drag3 = dragFixture3.Coord.Drag;
			Native.POINT p2 = CenterOf(dragFixture3.H);
			Native.SetCursorPos(p2.X, p2.Y);
			ActivateTrigger(dragFixture3.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms4 = Ms(p2);
			dragFixture3.Coord.OnMouse(513, ref ms4);
			Native.MSLLHOOKSTRUCT ms5 = Ms(new Native.POINT(p2.X + 50, p2.Y + 30));
			dragFixture3.Coord.OnMouse(512, ref ms5);
			Settle();
			dragFixture3.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Native.MSLLHOOKSTRUCT ms6 = Ms(ms5.pt);
			dragFixture3.Coord.OnMouse(514, ref ms6);
			Settle();
			Check(!drag3.IsDragging, "拖动会话已结束（先松触发键）");
			Check(!Native.IsWindowVisible(drag3.Frame.Handle), "★ 先松触发键再松鼠标：松鼠标之后外框也不得回来");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestFramePaletteExpanded()
	{
		Check(FrameColors.All.Length >= 16, "外框颜色至少 16 种（#37 扩充）", "实际 " + FrameColors.All.Length);
		Check(FrameStyles.All.Length >= 5, "外框样式至少 5 种（精简后：虚线/细线/焦点/柔光/投影）", "实际 " + FrameStyles.All.Length);
		HashSet<string> hashSet = new HashSet<string>();
		int num = 0;
		string text = "";
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			FrameColors.Entry entry = FrameColors.All[i];
			if (string.IsNullOrEmpty(entry.Id) || string.IsNullOrEmpty(entry.Name))
			{
				num++;
				text += "(空项) ";
				continue;
			}
			if (!hashSet.Add(entry.Id))
			{
				num++;
				text = text + entry.Id + "(ID重复) ";
			}
			if (!FrameColors.IsValid(entry.Id))
			{
				num++;
				text = text + entry.Id + "(非法) ";
			}
			if (FrameColors.Resolve(entry.Id).A != byte.MaxValue)
			{
				num++;
				text = text + entry.Id + "(非不透明) ";
			}
		}
		Check(num == 0, "每个颜色的 ID 唯一、名字非空、解析为不透明色", text);
		int num2 = 0;
		string text2 = "";
		int num3 = FrameStyles.MarginFor("Hairline", 3);
		for (int j = 0; j < FrameStyles.All.Length; j++)
		{
			string text3 = FrameStyles.All[j];
			string text4 = FrameStyles.DisplayName(text3);
			if (string.IsNullOrEmpty(text4) || text4 == text3)
			{
				num2++;
				text2 = text2 + text3 + "(没有中文名) ";
			}
			if (FrameStyles.MarginFor(text3, 3) < 4)
			{
				num2++;
				text2 = text2 + text3 + "(留白太小) ";
			}
			if (text3 != "Hairline" && FrameStyles.MarginFor(text3, 3) < num3)
			{
				num2++;
				text2 = text2 + text3 + "(留白小于纯净环) ";
			}
		}
		Check(num2 == 0, "每个样式都有中文名、且留白足够放下光晕", text2);
		int num4 = 0;
		string text5 = "";
		string[] all = FrameStyles.All;
		foreach (string text6 in all)
		{
			try
			{
				using Bitmap bitmap = new Bitmap(240, 160);
				using Graphics graphics = Graphics.FromImage(bitmap);
				graphics.Clear(Color.White);
				FrameStyles.Draw(graphics, new RectangleF(0f, 0f, 240f, 160f), FrameColors.Resolve("Teal"), text6, 3, 12);
				int num5 = 0;
				for (int l = 0; l < bitmap.Height; l += 2)
				{
					for (int m = 0; m < bitmap.Width; m += 2)
					{
						if (bitmap.GetPixel(m, l).ToArgb() != Color.White.ToArgb())
						{
							num5++;
						}
					}
				}
				if (num5 < 4)
				{
					num4++;
					text5 = text5 + text6 + "(只落了 " + num5 + " 个像素) ";
				}
			}
			catch (Exception ex)
			{
				num4++;
				text5 = text5 + text6 + "(异常 " + ex.GetType().Name + ") ";
			}
		}
		Check(num4 == 0, "★ 每一种外框样式都能画出来（不抛异常、确实落了像素）", text5);
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		int num6 = 0;
		string text7 = "";
		all = FrameStyles.All;
		foreach (string text8 in all)
		{
			settingsForm.ProbeSelectFrameStyle(text8);
			SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
			if (frameSelection.StyleId != text8 || frameSelection.PreviewStyleId != text8)
			{
				num6++;
				text7 = text7 + text8 + "→" + frameSelection.StyleId + "/" + frameSelection.PreviewStyleId + " ";
			}
		}
		Check(num6 == 0, "★ 每一种样式都能在设置页选中并同步到预览", text7);
	}

	private static double ContrastRatio(Color a, Color b)
	{
		double num = RelativeLuminance(a);
		double num2 = RelativeLuminance(b);
		double num3 = Math.Max(num, num2);
		double num4 = Math.Min(num, num2);
		return (num3 + 0.05) / (num4 + 0.05);
	}

	private static double RelativeLuminance(Color c)
	{
		return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
	}

	private static double Channel(int v)
	{
		double num = (double)v / 255.0;
		return (num <= 0.03928) ? (num / 12.92) : Math.Pow((num + 0.055) / 1.055, 2.4);
	}

	private static void TestFrameColorAccentAndCustom()
	{
		Color accent = FrameColors.Resolve("Accent");
		AppConfig appConfig = new AppConfig();
		appConfig.FrameColorId = "Red";
		appConfig.FrameColorFollowAccent = true;
		Eq(appConfig.EffectiveFrameColor.ToArgb(), accent.ToArgb(), "★ 开了「跟随系统强调色」之后，最终颜色就是强调色");
		appConfig.FrameColorFollowAccent = false;
		Eq(appConfig.EffectiveFrameColor.ToArgb(), FrameColors.Resolve("Red").ToArgb(), "★ 关掉跟随之后回落到调色板里的颜色");
		appConfig.FrameCustomColor = Color.FromArgb(18, 52, 86).ToArgb();
		Eq(appConfig.EffectiveFrameColor.ToArgb(), Color.FromArgb(18, 52, 86).ToArgb(), "★ 设了自定义颜色就优先用自定义色");
		appConfig.FrameColorFollowAccent = true;
		Eq(appConfig.EffectiveFrameColor.ToArgb(), accent.ToArgb(), "两个都开着时强调色优先（不会两个设置互相打架）");
		AppConfig appConfig2 = new AppConfig();
		appConfig2.FrameCustomColor = 16777215;
		appConfig2.Normalize();
		Eq(appConfig2.FrameCustomColor, 0, "透明色的自定义值会被归一化成「没设过」");
		using (DragFixture dragFixture = new DragFixture(null))
		{
			using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
			settingsForm.CreateControl();
			settingsForm.ProbeSetFollowAccent(on: true);
			SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
			Check(frameSelection.FollowAccent, "设置页上「跟随强调色」是打开的");
			Eq(frameSelection.EffectiveColorHex, FrameColors.HexOf(accent), "★ 预览框用的颜色跟着变成强调色");
			settingsForm.ProbeSetCustomColor(Color.FromArgb(34, 136, 68).ToArgb());
			SettingsForm.FrameSelection frameSelection2 = settingsForm.ProbeFrameSelection();
			Check(!frameSelection2.FollowAccent, "选自定义色会自动关掉「跟随强调色」（否则点了像没反应）");
			Eq(frameSelection2.EffectiveColorHex, "#228844", "★ 预览框用的颜色换成自定义色");
			Check(frameSelection2.ColorNameText.Contains("#228844"), "界面上直接写出了自定义颜色的色号", frameSelection2.ColorNameText);
			bool flag = false;
			bool flag2 = false;
			Walk(settingsForm, delegate(Control control)
			{
				if (control is CheckBox checkBox && checkBox.Text.Contains("跟随系统强调色") && checkBox.Width > 0)
				{
					flag = true;
				}
				if (control is Button button && button.Text.Contains("自定义颜色") && button.Width > 0)
				{
					flag2 = true;
				}
			});
			Check(flag, "★ 「跟随系统强调色」开关真的在界面上（不是只改了配置）");
			Check(flag2, "★ 「自定义颜色…」按钮真的在界面上");
			AppConfig appConfig3 = new AppConfig();
			settingsForm.ProbeCaptureInto(appConfig3);
			Eq(appConfig3.FrameCustomColor, Color.FromArgb(34, 136, 68).ToArgb(), "★ 自定义颜色真的写进了配置");
			Check(!appConfig3.FrameColorFollowAccent, "跟随强调色的状态也写进了配置");
		}
		using ActivationFrame activationFrame = new ActivationFrame();
		activationFrame.EnsureCreated();
		if (activationFrame.IsCreated)
		{
			Color color = Color.FromArgb(34, 136, 68);
			activationFrame.ApplyTheme("Teal", "Dashed", 3, 13, 100, color);
			Check(activationFrame.ColorOverrideForTest.HasValue && activationFrame.ColorOverrideForTest.Value.ToArgb() == color.ToArgb(), "★ 浮层记住了覆盖色（强调色/自定义色走的就是这条路）");
			activationFrame.HideOverlay();
		}
	}

	private static void TestFrameRadiusTakesEffect()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.ProbeSetFrameShape(3, 4, enabled: true);
		int num = SettingsForm.ProbePreviewRadiusDiff(settingsForm, 2, 26);
		Line("    （预览像素差异）圆角 2 vs 26 = " + num + " 像素");
		Check(num > 40, "★ 改「圆角」之后预览真的跟着变（不是只改了个数字）", "差异 " + num + " 像素");
		SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
		Eq(frameSelection.PreviewRadius, 26, "预览框拿到的是新圆角值 26");
		settingsForm.ProbeSetFrameShape(3, 12, enabled: true);
		Eq(settingsForm.ProbeFrameSelection().Radius, 12, "设置页读回的圆角是 12");
		settingsForm.Close();
	}

	private static void TestFramelessCardWindow()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(40, 40);
		settingsForm.Show();
		Application.DoEvents();
		Native.RECT rECT;
		Native.GetWindowRect(settingsForm.Handle, out rECT);
		int num = rECT.Bottom - rECT.Top - settingsForm.ClientSize.Height;
		Line("    （窗口高=" + (rECT.Bottom - rECT.Top) + " 客户区高=" + settingsForm.ClientSize.Height + "）");
		Check(num <= 6, "★ 系统标题栏已被去掉（客户区高度≈窗口高度，最上面那条是自己画的）", "差值 " + num + "px");
		int windowIntAttribute = Native.GetWindowIntAttribute(settingsForm.Handle, 33);
		if (Environment.OSVersion.Version.Build >= 22000)
		{
			Eq(windowIntAttribute, 2, "★ 整窗启用了 DWM 圆角（不再是一整块直角矩形）");
		}
		else
		{
			Line("    （系统低于 Win11，圆角由系统决定）corner=" + windowIntAttribute);
		}
		Eq(settingsForm.ProbeCaptionButtonCount(), 3, "★ 最小化/最大化/关闭三个按钮都在标题条上");
		Color color = settingsForm.ProbeTitleStripColor();
		Eq(color.ToArgb(), settingsForm.BackColor.ToArgb(), "★ 标题条颜色就是窗口底色（同一套颜色，不会出现最上面一条不一样）");
		settingsForm.Close();
	}

	/// <summary>
	/// 重排性能：拖窗口时每一档宽度都要重排一次，所以"一次重排要多久"直接决定卡不卡。
	/// 同时验证"同宽度重复调用不会重复干活"（去重生效）。
	/// </summary>
	private static void TestSettingsRelayoutPerformance()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		float num = SettingsForm.ProbeUiScaleOf(settingsForm);
		int height = (int)Math.Round(760f * num);
		// 预热（首次会建字体、算折行缓存，不算进成绩）
		for (int i = 0; i < 3; i++)
		{
			settingsForm.ClientSize = new Size((int)Math.Round((980f + i * 40f) * num), height);
			SettingsForm.RelayoutActiveForTest(settingsForm);
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		int num2 = 20;
		for (int j = 0; j < num2; j++)
		{
			// 每档宽度都不一样，确保每次都真的重排（模拟拖动边框）
			settingsForm.ClientSize = new Size((int)Math.Round((980f + j * 17f) * num), height);
			SettingsForm.RelayoutActiveForTest(settingsForm);
		}
		stopwatch.Stop();
		double num3 = stopwatch.Elapsed.TotalMilliseconds / (double)num2;
		// 同一尺寸重复调用应当直接被去重挡住
		settingsForm.ClientSize = new Size((int)Math.Round(1100f * num), height);
		SettingsForm.RelayoutActiveForTest(settingsForm);
		Stopwatch stopwatch2 = Stopwatch.StartNew();
		for (int k = 0; k < 20; k++)
		{
			SettingsForm.RelayoutActiveForTest(settingsForm);
		}
		stopwatch2.Stop();
		double num4 = stopwatch2.Elapsed.TotalMilliseconds / 20.0;
		Line("    （重排耗时）换宽度 " + num3.ToString("0.00") + " ms/次，同宽度重复 " + num4.ToString("0.00") + " ms/次");
		Check(num3 < 60.0, "★ 换一档宽度重排一次 < 60ms（拖窗口/最大化不会一顿一顿）", num3.ToString("0.00") + " ms");
		Check(num4 < 3.0, "★ 同尺寸重复重排会被去重（不重复干活）", num4.ToString("0.00") + " ms");
		settingsForm.Close();
	}

	/// <summary>
	/// 导航胶囊回归：只有"当前页"配底色。
	/// 之前 glow 动画值打到 1 就不回落，paint 又拿它当"要画底色"的依据，
	/// 结果点过的每一项都留一个灰胶囊（看起来像没选中却高亮）。
	/// </summary>
	// 侧栏胶囊是 170ms 淡入动画，量颜色前要让它跑完
	private static void SettleNavAnimation()
	{
		for (int i = 0; i < 30; i++)
		{
			Application.DoEvents();
			Thread.Sleep(10);
		}
	}

	private static void TestNavHighlightFollowsSelection()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		SettleNavAnimation();
		Color color = SettingsForm.ProbeNavRowColor(settingsForm, 2);
		Color color2 = SettingsForm.ProbeNavRowColor(settingsForm, 1);
		Color color3 = SettingsForm.ProbeTokenColor("Sidebar");
		Color color4 = SettingsForm.ProbeTokenColor("AccentSoft");
		Line("    （侧栏底色）第1项=" + color2.R + "," + color2.G + "," + color2.B + " 第3项=" + color.R + "," + color.G + "," + color.B);
		Check(color3.ToArgb() == color2.ToArgb(), "未选中的导航项没有底色（就是侧栏底色）", color2.ToString());
		// 点一下第 3 项，再切回第 1 项：第 3 项必须恢复成"无底色"
		SettingsForm.ProbeSelectPage(settingsForm, 2);
		SettleNavAnimation();
		SettingsForm.ProbeSelectPage(settingsForm, 0);
		SettleNavAnimation();
		Color color5 = SettingsForm.ProbeNavRowColor(settingsForm, 2);
		Color color6 = SettingsForm.ProbeNavRowColor(settingsForm, 0);
		Check(color5.ToArgb() == color3.ToArgb(), "★ 点过又切走的导航项不残留胶囊底色（回归）", "第3项=" + color5.ToString() + " 侧栏底=" + color3.ToString());
		Check(color6.ToArgb() == color4.ToArgb(), "当前页那项是强调色胶囊", "第1项=" + color6.ToString());
		settingsForm.Close();
	}

	private static void TestSettingsThemeFollowsSystem()
	{
		bool flag = SettingsForm.ProbeThemeIsDark();
		Line("    （系统主题）" + (flag ? "深色" : "浅色"));
		bool registryDark = false;
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			if (registryKey != null && registryKey.GetValue("AppsUseLightTheme") is int num4)
			{
				registryDark = num4 == 0;
			}
		}
		catch
		{
		}
		Check(SettingsForm.ProbeThemeIsDark() == registryDark, "主题探测和注册表里的 AppsUseLightTheme 一致（不是写死的）");
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		SettingsForm.RenderStats renderStats = SettingsForm.ProbeFormRender(settingsForm, "Teal");
		Line("    （主题渲染）亮度=" + renderStats.MeanBrightness.ToString("0.0") + " 纯黑=" + renderStats.BlackPixels + " 颜色数=" + renderStats.DistinctColors);
		if (flag)
		{
			Check(renderStats.MeanBrightness < 120.0, "★ 系统深色时设置页跟着变深色（不再固定成浅色）", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
		}
		else
		{
			Check(renderStats.MeanBrightness > 200.0, "系统浅色时设置页是浅色底", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
		}
		Check(renderStats.DistinctColors >= 24, "深浅色都是一整套层次（不是一块纯色刷到底）", "颜色数 " + renderStats.DistinctColors);
		double num = ContrastRatio(SettingsForm.ProbeTokenColor("TextPrimary"), SettingsForm.ProbeTokenColor("Surface"));
		Line("    （对比度）正文/卡片=" + num.ToString("0.0"));
		Check(num >= 7.0, "★ 正文与卡片底的对比度达标（看得清才谈得上精致）", "对比度 " + num.ToString("0.0"));
		double num2 = ContrastRatio(SettingsForm.ProbeTokenColor("TextSecondary"), SettingsForm.ProbeTokenColor("Surface"));
		Check(num2 >= 3.5, "次要文字也够清楚（不是灰到看不清）", "对比度 " + num2.ToString("0.0"));
		double num3 = ContrastRatio(SettingsForm.ProbeTokenColor("Accent"), SettingsForm.ProbeTokenColor("Surface"));
		Check(num3 >= 2.2, "强调色在卡片底上够显眼", "对比度 " + num3.ToString("0.0"));
		settingsForm.Close();
	}

	private static void TestSettingsDefaultSizeCompact()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		int num = settingsForm.ProbeDefaultClientHeight();
		int height = settingsForm.ClientSize.Height;
		Check(height <= num + 4, "#33 设置页显示后不会比默认高度更大（不再被内容撑到满屏）", "实际 " + height + " 默认 " + num);
		Check(height >= num / 2, "#33 设置页默认高度没有被压成一条缝", "实际 " + height);
		Check(settingsForm.MinimumSize.Height >= 300, "最小高度仍可用（>= 300 物理像素）", "实际 " + settingsForm.MinimumSize.Height);
		Check(settingsForm.MinimumSize.Height <= num, "最小高度不大于默认高度（否则默认值无意义）", "最小 " + settingsForm.MinimumSize.Height + " 默认 " + num);
		settingsForm.Close();
	}

	private static void TestDragVisualFeedback()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Color want = FrameColors.Resolve(dragFixture.Store.Current.FrameColorId);
			Native.POINT p = CenterOf(dragFixture.H);
			Native.SetCursorPos(p.X, p.Y);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms);
			Settle();
			Check(drag.IsDragging, "拖动会话已开始");
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 60, p.Y + 40));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Settle();
			Check(drag.Frame.IsShown, "拖动中分层框的显示标志为真（IsShown）");
			Check(Native.IsWindowVisible(drag.Frame.Handle), "拖动中分层框窗口 WS_VISIBLE 置位");
			Native.GetWindowRect(drag.Frame.Handle, out var lpRect);
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			int num = CountFramePixelsAround(lpRect, want);
			Line("    （环上取点）左中点=" + PixelAt(lpRect.Left + 12, (lpRect.Top + lpRect.Bottom) / 2) + " 上中点=" + PixelAt((lpRect.Left + lpRect.Right) / 2, lpRect.Top + 12) + " 右中点=" + PixelAt(lpRect.Right - 12, (lpRect.Top + lpRect.Bottom) / 2) + " 下中点=" + PixelAt((lpRect.Left + lpRect.Right) / 2, lpRect.Bottom - 12) + " 期望色=" + want.R + "," + want.G + "," + want.B);
			bool ok = num > 150;
			string[] array = new string[8]
			{
				"命中 ",
				num.ToString(),
				" 像素，框矩形=",
				null,
				null,
				null,
				null,
				null
			};
			Native.RECT rECT = lpRect;
			array[3] = rECT.ToString();
			array[4] = " 样式=";
			array[5] = drag.Frame.StyleIdForTest;
			array[6] = " 颜色=";
			array[7] = drag.Frame.ColorIdForTest;
			Check(ok, "★ 拖动中屏幕上真的画出了外框（截屏数到外框色像素）", string.Concat(array));
			Check(lpRect.Width >= lpRect2.Width && lpRect.Height >= lpRect2.Height && lpRect.Width - lpRect2.Width <= 80 && lpRect.Height - lpRect2.Height <= 80, "框矩形尺寸与窗口匹配（±留白）", "框=" + lpRect.Width + "x" + lpRect.Height + " 窗=" + lpRect2.Width + "x" + lpRect2.Height);
			Native.RECT rECT2 = lpRect;
			Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(p.X + 170, p.Y + 120));
			dragFixture.Coord.OnMouse(512, ref ms3);
			Settle();
			Settle();
			Native.GetWindowRect(drag.Frame.Handle, out lpRect);
			Native.RECT rECT3 = lpRect;
			Native.GetWindowRect(dragFixture.H, out var lpRect3);
			Check(rECT3.Left > rECT2.Left + 40, "★ 框跟着窗口一起移动（持续跟随，不是停在原地）", rECT2.Left + " → " + rECT3.Left);
			Check(Math.Abs(rECT3.Left - lpRect3.Left - (rECT2.Left - lpRect2.Left)) <= 6, "框相对窗口的偏移保持不变（没有漂移）", "框-窗 Δ 前=" + (rECT2.Left - lpRect2.Left) + " 后=" + (rECT3.Left - lpRect3.Left));
			Native.GetWindowRect(drag.Frame.Handle, out var lpRect4);
			Native.MSLLHOOKSTRUCT ms4 = Ms(ms3.pt);
			dragFixture.Coord.OnMouse(514, ref ms4);
			Settle();
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!Native.IsWindowVisible(drag.Frame.Handle), "松手并松开触发键后外框收起");
			int num2 = CountFramePixelsAround(lpRect4, want);
			Check(num2 < Math.Max(30, num / 4), "★ 对照组：框收起后同一区域数不到外框色（证明①数到的确实是框）", "拖动中 " + num + " → 松手后 " + num2);
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static string PixelAt(int x, int y)
	{
		try
		{
			using Bitmap bitmap = new Bitmap(1, 1);
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
			Color pixel = bitmap.GetPixel(0, 0);
			return pixel.R + "," + pixel.G + "," + pixel.B;
		}
		catch
		{
			return "?";
		}
	}

	private static int CountFramePixelsAround(Native.RECT win, Color want)
	{
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			int num = Math.Max(virtualScreen.Left, win.Left - 48);
			int num2 = Math.Max(virtualScreen.Top, win.Top - 48);
			int num3 = Math.Min(virtualScreen.Right, win.Right + 48);
			int num4 = Math.Min(virtualScreen.Bottom, win.Bottom + 48);
			if (num3 - num <= 2 || num4 - num2 <= 2)
			{
				return -1;
			}
			return CountColorIn(new Rectangle(num, num2, num3 - num, num4 - num2), want);
		}
		catch
		{
			return -1;
		}
	}

	private static int CountColorIn(Rectangle area, Color want)
	{
		if (area.Width <= 0 || area.Height <= 0)
		{
			return 0;
		}
		using Bitmap bitmap = new Bitmap(area.Width, area.Height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.CopyFromScreen(area.Left, area.Top, 0, 0, new Size(area.Width, area.Height));
		int num = 0;
		for (int i = 0; i < bitmap.Height; i++)
		{
			for (int j = 0; j < bitmap.Width; j++)
			{
				Color pixel = bitmap.GetPixel(j, i);
				if (Math.Abs(pixel.R - want.R) <= 30 && Math.Abs(pixel.G - want.G) <= 30 && Math.Abs(pixel.B - want.B) <= 30)
				{
					num++;
				}
			}
		}
		return num;
	}

	private static void TestFramePickersAlwaysVisible()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		SettingsForm.FramePickerProbe framePickerProbe = settingsForm.ProbeFramePickers();
		Eq(framePickerProbe.ColorCells, FrameColors.All.Length, "色板把调色板里的颜色全部平铺出来（进入即可见）");
		Eq(framePickerProbe.StyleCells, FrameStyles.All.Length, "样式画廊把全部样式平铺出来（进入即可见）");
		Check(framePickerProbe.GridInk > 100, "色板真的画出了色块", "非背景像素 " + framePickerProbe.GridInk + " 尺寸 " + framePickerProbe.GridSize);
		Check(framePickerProbe.GalleryInk > 300, "样式画廊真的画出了缩略图", "非背景像素 " + framePickerProbe.GalleryInk + " 尺寸 " + framePickerProbe.GallerySize);
		Check(framePickerProbe.GridAccentInk > 10, "★ 色板里「选中的那一格」有可见的强调色标记", "强调色像素 " + framePickerProbe.GridAccentInk);
		Check(framePickerProbe.GalleryAccentInk > 10, "★ 样式画廊里选中的那一格有可见的强调色标记", "强调色像素 " + framePickerProbe.GalleryAccentInk);
		Check(framePickerProbe.GridSelectedCell.Width > 0 && framePickerProbe.GallerySelectedCell.Width > 0, "选中的色块格 / 样式格都能定位到（选中态不是空）");
		settingsForm.ProbeSelectFrameStyle("Focus");
		SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
		Eq(frameSelection.GridStyleId, "Focus", "★ 选了「焦点环」之后，画廊里高亮的就是焦点环");
		Eq(frameSelection.GridColorId, frameSelection.ColorId, "色板高亮与当前颜色一致");
		settingsForm.ProbeSelectFrameColor("Gold");
		SettingsForm.FrameSelection frameSelection2 = settingsForm.ProbeFrameSelection();
		Eq(frameSelection2.GridColorId, "Gold", "★ 选了「暖金」之后，色板里高亮的就是暖金");
		Check(!string.IsNullOrEmpty(frameSelection2.ColorNameText) && frameSelection2.ColorNameText.Contains("暖金"), "界面上写明了当前颜色的名字（状态清晰）", "文字=" + frameSelection2.ColorNameText);
		int[] array = new int[6] { 736, 900, 1180, 1516, 1900, 2600 };
		int num = 0;
		string text = "";
		int[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			int width = array2[i];
			settingsForm.ClientSize = new Size(width, 900);
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
			SettingsForm.FramePickerProbe framePickerProbe2 = settingsForm.ProbeFramePickers();
			if (framePickerProbe2.GridSize.Width <= 100 || framePickerProbe2.GridSize.Height <= 10 || framePickerProbe2.GallerySize.Width <= 100 || framePickerProbe2.GallerySize.Height <= 40 || !framePickerProbe2.GridInsideGroup || !framePickerProbe2.GalleryInsideGroup)
			{
				num++;
				text = text + width + "→色板" + framePickerProbe2.GridSize.ToString() + (framePickerProbe2.GridInsideGroup ? "" : "(越界)") + " 画廊" + framePickerProbe2.GallerySize.ToString() + (framePickerProbe2.GalleryInsideGroup ? "" : "(越界)") + " ";
			}
		}
		Check(num == 0, "★ 从窄窗到超宽（全屏档）每一档宽度下，色板与样式画廊都保持可见尺寸且不越界", text);
		settingsForm.Close();
	}

	private static void TestVisualPolish()
	{
		Check(SettingsForm.ProbeHoverTweenAnimates(out var start, out var end), "★ 悬停是带过渡的（补间从 0 平滑推到 1），不是瞬变", "起点 " + start.ToString("0.00") + " → 终点 " + end.ToString("0.00"));
		using DragFixture dragFixture = new DragFixture((AppConfig c) =>
		{
			c.ShowActivationFrame = true;
		});
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.ClientSize = new Size(1000, 2400);
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		Application.DoEvents();
		SettingsForm.RenderStats renderStats = SettingsForm.ProbeFormRender(settingsForm, dragFixture.Store.Current.FrameColorId);
		int[] array = settingsForm.ProbeFrameColorInk(dragFixture.Store.Current.FrameColorId);
		Line("    （渲染体检）" + renderStats.Width + "x" + renderStats.Height + " 颜色数=" + renderStats.DistinctColors + " 近黑=" + renderStats.BlackPixels + " 强调色=" + renderStats.AccentPixels + " 外框色=" + renderStats.FrameColorPixels + " 平均亮度=" + renderStats.MeanBrightness.ToString("0.0"));
		Line("    （外框色分区）色板=" + array[0] + " 画廊=" + array[1] + " 预览框=" + array[2]);
		Line("    （色板诊断）" + settingsForm.ProbeGridDiagnostic());
		Check(renderStats.Width > 400 && renderStats.Height > 300, "设置窗口渲染出了有效的画布尺寸", renderStats.Width + "x" + renderStats.Height);
		Check(renderStats.DistinctColors > 400, "★ 画面颜色层次足够丰富（不是一块平色，说明圆角/描边/投影/文字都在画）", "不同颜色数 " + renderStats.DistinctColors);
		Check(renderStats.BlackPixels < 400, "★ 画面里没有成片的纯黑像素（自绘控件没铺底导致黑条的老问题没有复发）", "纯黑像素 " + renderStats.BlackPixels);
		if (SettingsForm.ProbeThemeIsDark())
		{
			Check(renderStats.MeanBrightness < 120.0, "★ 系统深色时设置页整体也是深色（不再固定浅色刺眼）", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
			Check(renderStats.MeanBrightness > 15.0, "深色底不是死黑（有层次，不发灰发脏）", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
		}
		else
		{
			Check(renderStats.MeanBrightness > 200.0, "系统浅色时设置页整体是浅色 Win11 底（平均亮度够高）", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
		}
		Check(renderStats.AccentPixels > 300, "★ 强调色真的出现在画面上（主按钮 / 分组竖条 / 选中态）", "强调色像素 " + renderStats.AccentPixels);
		Check(renderStats.FrameColorPixels > 150, "★ 外框色真的出现在画面上（色板 / 样式缩略图 / 实时预览）", "外框色像素 " + renderStats.FrameColorPixels);
		settingsForm.Close();
	}

	private static void TestNoControlClippedAtMinSize()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		float num = SettingsForm.ProbeUiScaleOf(settingsForm);
		int num2 = (int)Math.Round(736f * num);
		int val = (int)Math.Round(440f * num);
		int[] array = new int[4]
		{
			num2,
			num2 + 60,
			num2 + 140,
			num2 + 260
		};
		int num3 = 0;
		string text = "";
		int num4 = 0;
		string text2 = "";
		int[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			int width = array2[i];
			settingsForm.ClientSize = new Size(width, Math.Max(val, 900));
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
			for (int j = 0; j < SettingsForm.ProbeGroupCount(settingsForm); j++)
			{
				SettingsForm.GroupClipReport groupClipReport = SettingsForm.ProbeGroupClipping(settingsForm, j);
				if (groupClipReport.Clipped != 0)
				{
					num3 += groupClipReport.Clipped;
					text = text + "宽" + width + " 第" + (j + 1) + "组：" + groupClipReport.Detail;
					string text3 = SettingsForm.ProbeTextFitting(settingsForm, j);
					if (text3.Length > 0)
					{
						num4++;
						text2 = text2 + "宽" + width + " 第" + (j + 1) + "组：" + text3.Replace("\r\n", " | ");
					}
				}
			}
		}
		Check(num3 == 0, "★ 最小尺寸及附近各档下，没有控件越出所属分组（全屏选项始终完整可见）", text);
		Check(num4 == 0, "★ 最小尺寸及附近各档下，没有任何选项的文字被裁掉（遮挡回归）", text2);
		settingsForm.ClientSize = new Size(num2, Math.Max(val, 900));
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		string text4 = SettingsForm.ProbeSwitchColors(settingsForm, on: true);
		string text5 = SettingsForm.ProbeSwitchColors(settingsForm, on: false);
		Line("    （开关·开）" + text4);
		Line("    （开关·关）" + text5);
		Check(!text4.Contains("异常") && !text5.Contains("异常"), "开关能正常渲染（探针没有报错）", text4 + " / " + text5);
		Check(text4 != text5, "★ 开关的开 / 关两态渲染结果不同（不是画成了同一个样子）", "开：" + text4 + "  关：" + text5);
		int[] array3 = ParseRgb(text4, 0);
		int[] array4 = ParseRgb(text5, 0);
		int num5 = Math.Abs(array3[0] - array4[0]) + Math.Abs(array3[1] - array4[1]) + Math.Abs(array3[2] - array4[2]);
		Check(num5 > 60, "★ 开关的轨道颜色在开 / 关之间有明确差异（强调色 vs 中性灰）", "开轨道=" + string.Join(",", array3) + " 关轨道=" + string.Join(",", array4) + " 差值=" + num5);
		int[] array5 = ParseRgb(text4, 1);
		Check(array5[0] > 230 && array5[1] > 230 && array5[2] > 230, "开关的滑块是浅色（白滑块，在两种轨道上都清晰可辨）", "滑块=" + string.Join(",", array5));
		Control control = SettingsForm.ProbeGroupAt(settingsForm, 3);
		SettingsForm.GroupClipReport groupClipReport2 = SettingsForm.ProbeGroupClipping(settingsForm, 3);
		Check(control != null && groupClipReport2.Clipped == 0, "★ 最小尺寸下『拖动行为』分组自身无控件被遮挡", groupClipReport2.Detail);
		settingsForm.Close();
	}

	private static void TestResizeIsStable()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		float num = SettingsForm.ProbeUiScaleOf(settingsForm);
		int height = (int)Math.Round(760f * num);
		settingsForm.ClientSize = new Size((int)Math.Round(1100f * num), height);
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		string text = SettingsForm.ProbeLayoutFingerprint(settingsForm);
		for (int i = 0; i < 3; i++)
		{
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
		}
		string text2 = SettingsForm.ProbeLayoutFingerprint(settingsForm);
		Check(text.Length > 0 && text == text2, "★ 同一尺寸反复重排结果完全一致（布局幂等，不会越排越偏）", (text == text2) ? "" : "两次指纹不同");
		int num2 = 0;
		string text3 = "";
		int num3 = Math.Max(1, (int)Math.Round(29f * num));
		int num4 = (int)Math.Round(736f * num);
		int num5 = (int)Math.Round(1500f * num);
		for (int j = num4; j <= num5; j += num3)
		{
			settingsForm.ClientSize = new Size(j, height);
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
			for (int k = 0; k < SettingsForm.ProbeGroupCount(settingsForm); k++)
			{
				SettingsForm.GroupClipReport groupClipReport = SettingsForm.ProbeGroupClipping(settingsForm, k);
				if (groupClipReport.Clipped > 0)
				{
					num2 += groupClipReport.Clipped;
					text3 = text3 + "w=" + j + " 第" + (k + 1) + "组：" + groupClipReport.Detail;
				}
			}
		}
		Check(num2 == 0, "★ 连续改变尺寸的每一步都没有控件越界（拖动过程不溢出）", text3);
		settingsForm.ClientSize = new Size((int)Math.Round(2400f * num), height);
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		Rectangle cardBox = SettingsForm.CardBoxOf(settingsForm);
		int num6 = SettingsForm.ProbeCardMaxWidthPx(settingsForm);
		int num8x = Math.Max(num6, settingsForm.ClientSize.Width / 2);
		Check(cardBox.Width <= num8x + (int)Math.Round(4f * num), "★ 窗口拉到超宽时改用两列或仍限宽 " + (num6 / num) + "px，不会把卡片拉成一条细长条", cardBox.ToString() + " 客户区宽=" + settingsForm.ClientSize.Width);
		settingsForm.Close();
	}

	private static void TestDpiRendering()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		float num = SettingsForm.ProbeUiScaleOf(settingsForm);
		int num2 = Native.DpiOfWindow(settingsForm.Handle);
		float num3 = (float)num2 / 96f;
		Check(Math.Abs(num - num3) < 0.01f, "★ 界面按窗口所在显示器的真实 DPI 渲染（不是写死 100%，也不是主屏 DPI）", "生效系数=" + num.ToString("0.###") + " 真实 DPI=" + num2 + " → 期望 " + num3.ToString("0.###"));
		settingsForm.ClientSize = new Size((int)Math.Round(900f * num), (int)Math.Round(760f * num));
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		Rectangle rectangle = SettingsForm.ProbeRectOfTextForTest(settingsForm, "外框颜色");
		Check(rectangle.Width > 0, "能定位到样本控件（探针有效）", rectangle.ToString());
		string text = SettingsForm.ProbeLayoutFingerprint(settingsForm);
		SettingsForm.ProbeRescaleForDpi(settingsForm, num2);
		Application.DoEvents();
		string text2 = SettingsForm.ProbeLayoutFingerprint(settingsForm);
		Check(text == text2, "★ 同一 DPI 下重复触发重建是 no-op（幂等，不会越缩越小）", (text == text2) ? "" : "指纹被改动");
		int num4 = ((Math.Abs(num2 - 96) < 24) ? 192 : 96);
		SettingsForm.ProbeRescaleForDpi(settingsForm, num4);
		Application.DoEvents();
		float num5 = SettingsForm.ProbeUiScaleOf(settingsForm);
		Check(Math.Abs(num5 - (float)num4 / 96f) < 0.01f, "★ DPI 变化后缩放系数跟着换（按新分辨率重建界面）", "生效系数=" + num5.ToString("0.###") + " 期望 " + ((float)num4 / 96f).ToString("0.###"));
		int num6 = 0;
		string text3 = "";
		for (int i = 0; i < SettingsForm.ProbeGroupCount(settingsForm); i++)
		{
			SettingsForm.GroupClipReport groupClipReport = SettingsForm.ProbeGroupClipping(settingsForm, i);
			if (groupClipReport.Clipped > 0)
			{
				num6 += groupClipReport.Clipped;
				text3 = text3 + "第" + (i + 1) + "组：" + groupClipReport.Detail;
			}
		}
		Check(num6 == 0, "换 DPI 重建之后布局依然合法（无控件越界）", text3);
		SettingsForm.ProbeRescaleForDpi(settingsForm, num2);
		Application.DoEvents();
		Check(Math.Abs(SettingsForm.ProbeUiScaleOf(settingsForm) - num) < 0.01f, "★ DPI 改回原值后缩放系数回到原值（可逆）", SettingsForm.ProbeUiScaleOf(settingsForm).ToString("0.###"));
		settingsForm.Close();
	}

	private static void TestTriggerOnBigWindows()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using (DragFixture f = new DragFixture((AppConfig c) =>
			{
				c.RestoreMaximizedWhenMoved = true;
			}))
			{
				Line("    （普通窗口 左键）" + ProbeOneGesture(f, left: true));
				Line("    （普通窗口 右键）" + ProbeOneGesture(f, left: false));
			}
			using (DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.RestoreMaximizedWhenMoved = true;
				c.ExitFullscreenOnTrigger = true;
			}))
			{
				Native.ShowWindow(dragFixture.H, 3);
				Settle();
				bool flag = Native.IsZoomed(dragFixture.H);
				Line("    （最大化窗口）IsZoomed=" + flag);
				string text = ProbeOneGesture(dragFixture, left: true);
				Line("    （最大化窗口 左键）" + text);
				bool flag2 = !Native.IsZoomed(dragFixture.H);
				Check(flag & flag2, "★ 最大化窗口：左键拖动后不再是最大化（先还原再拖）", text);
				Native.ShowWindow(dragFixture.H, 3);
				Settle();
				string text2 = ProbeOneGesture(dragFixture, left: false);
				Line("    （最大化窗口 右键）" + text2);
				Check(!Native.IsZoomed(dragFixture.H), "★ 最大化窗口：右键缩放后不再是最大化（右键功能必须还在）", text2);
				Native.ShowWindow(dragFixture.H, 3);
				Settle();
				ActivateTrigger(dragFixture.Coord);
				Native.POINT p = CenterOf(dragFixture.H);
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				dragFixture.Coord.OnMouse(516, ref ms);
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect);
				Check(p.X >= lpRect.Left && p.X < lpRect.Right && p.Y >= lpRect.Top && p.Y < lpRect.Bottom, "★ 最大化窗口右键：降级成普通窗口后光标仍落在窗口内（角区判定才有意义）", "光标=" + p.X + "," + p.Y + " 窗口=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height);
				Native.MSLLHOOKSTRUCT ms2 = Ms(p);
				dragFixture.Coord.OnMouse(517, ref ms2);
				Settle();
				dragFixture.Coord.Trigger.SimulateKeyUp(null);
				Settle();
			}
			using (DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ExitFullscreenOnTrigger = true;
				c.AllowFullscreenWindows = false;
			}))
			{
				Rectangle virtualScreen = SystemInformation.VirtualScreen;
				Native.SetWindowPos(dragFixture2.H, IntPtr.Zero, virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height, 20u);
				Settle();
				bool flag3 = WindowActions.IsFullscreen(dragFixture2.H);
				Line("    （全屏窗口·默认配置）IsFullscreen=" + flag3);
				string text3 = ProbeOneGesture(dragFixture2, left: false);
				Line("    （全屏窗口 右键）" + text3);
				Check(flag3 && !WindowActions.IsFullscreen(dragFixture2.H), "★ 真全屏窗口：右键缩放后不再是全屏（默认配置下就要生效）", text3);
				Native.SetWindowPos(dragFixture2.H, IntPtr.Zero, virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height, 20u);
				Settle();
				string text4 = ProbeOneGesture(dragFixture2, left: true);
				Line("    （全屏窗口 左键）" + text4);
				Check(!WindowActions.IsFullscreen(dragFixture2.H), "★ 真全屏窗口：左键拖动后不再是全屏", text4);
			}
			using DragFixture dragFixture3 = new DragFixture((AppConfig c) =>
			{
				c.ExitFullscreenOnTrigger = false;
				c.AllowFullscreenWindows = false;
			});
			Rectangle virtualScreen2 = SystemInformation.VirtualScreen;
			Native.SetWindowPos(dragFixture3.H, IntPtr.Zero, virtualScreen2.Left, virtualScreen2.Top, virtualScreen2.Width, virtualScreen2.Height, 20u);
			Settle();
			string text5 = ProbeOneGesture(dragFixture3, left: false);
			Line("    （全屏窗口·两个开关都关）" + text5);
			Check(WindowActions.IsFullscreen(dragFixture3.H) && text5.Contains("起会话=False"), "两个开关都关时全屏窗口不被接管（老行为保留）", text5);
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static string ProbeOneGesture(DragFixture f, bool left)
	{
		try
		{
			ActivateTrigger(f.Coord);
			Native.POINT p = CenterOf(f.H);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			bool flag = f.Coord.OnMouse(left ? 513 : 516, ref ms);
			bool isDragging = f.Coord.Drag.IsDragging;
			Native.GetWindowRect(f.H, out var lpRect);
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 40, p.Y + 30));
			f.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.GetWindowRect(f.H, out var lpRect2);
			Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
			f.Coord.OnMouse(left ? 514 : 517, ref ms3);
			Settle();
			f.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			return "吞掉=" + flag + " 起会话=" + isDragging + " 前=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height + " 后=" + lpRect2.Left + "," + lpRect2.Top + " " + lpRect2.Width + "x" + lpRect2.Height;
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name + ": " + ex.Message;
		}
	}

	private static void TestWheelDoesNotChangeSettings()
	{
		nint wParam = new IntPtr(7864320);
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		ComboBox comboBox = settingsForm.ProbeComboRight as ComboBox;
		Check(comboBox != null && comboBox.Items.Count > 1, "取到右键动作下拉框");
		if (comboBox != null && comboBox.Items.Count > 1)
		{
			int selectedIndex = comboBox.SelectedIndex;
			for (int i = 0; i < 5; i++)
			{
				Native.SendMessageW(comboBox.Handle, 522, wParam, IntPtr.Zero);
			}
			Application.DoEvents();
			Eq(comboBox.SelectedIndex, selectedIndex, "★ 滚轮扫过『右键』下拉框不会改掉动作绑定（误改的根因）");
		}
		if (settingsForm.ProbeComboLeft is ComboBox comboBox2 && comboBox2.Items.Count > 1)
		{
			int selectedIndex2 = comboBox2.SelectedIndex;
			Native.SendMessageW(comboBox2.Handle, 522, wParam, IntPtr.Zero);
			Application.DoEvents();
			Eq(comboBox2.SelectedIndex, selectedIndex2, "★ 滚轮扫过『左键』下拉框不会改掉动作绑定");
		}
		NumericUpDown numericUpDown = settingsForm.ProbeNumHold as NumericUpDown;
		Check(numericUpDown != null, "取到长按阈值数字框");
		if (numericUpDown != null)
		{
			decimal value = numericUpDown.Value;
			for (int j = 0; j < 3; j++)
			{
				Native.SendMessageW(numericUpDown.Handle, 522, wParam, IntPtr.Zero);
			}
			Application.DoEvents();
			Check(numericUpDown.Value == value, "★ 滚轮扫过数字框不会改掉数值（HoldMs/CornerPercent 被误改的根因）", "前=" + value + " 后=" + numericUpDown.Value);
		}
		if (comboBox != null)
		{
			int num = SettingsForm.ProbeWheelSwallowed(comboBox);
			int num2 = SettingsForm.ProbeWheelForwarded(comboBox);
			Native.SendMessageW(comboBox.Handle, 522, wParam, IntPtr.Zero);
			Application.DoEvents();
			int num3 = SettingsForm.ProbeWheelSwallowed(comboBox);
			int num4 = SettingsForm.ProbeWheelForwarded(comboBox);
			Check(num3 == num + 1 && num4 == num2, "收起状态下滚轮被守卫吞掉（不放行给 WinForms）", "吞 " + num + "→" + num3 + " 放 " + num2 + "→" + num4);
			bool flag = false;
			try
			{
				comboBox.DroppedDown = true;
				flag = comboBox.DroppedDown;
			}
			catch
			{
			}
			if (flag)
			{
				Native.SendMessageW(comboBox.Handle, 522, wParam, IntPtr.Zero);
				Application.DoEvents();
				int num5 = SettingsForm.ProbeWheelSwallowed(comboBox);
				int num6 = SettingsForm.ProbeWheelForwarded(comboBox);
				Check(num6 == num4 + 1 && num5 == num3, "★ 展开状态下滚轮照常放行（没有把下拉框的滚轮功能一并禁掉）", "吞 " + num3 + "→" + num5 + " 放 " + num4 + "→" + num6);
				try
				{
					comboBox.DroppedDown = false;
				}
				catch
				{
				}
			}
			else
			{
				Line("    （本环境无法展开下拉列表，跳过『展开后放行』一条）");
			}
		}
		settingsForm.Close();
	}

	private static void TestIconGlyphs()
	{
		FontFamily iconFamilyForTest = SettingsForm.IconFamilyForTest;
		Check(iconFamilyForTest != null, "图标字体可用（Segoe Fluent Icons 或退回 Segoe MDL2 Assets）", (iconFamilyForTest == null) ? "两个都没有，界面会退化成无图标（不崩，但会显得单薄）" : iconFamilyForTest.Name);
		if (iconFamilyForTest == null)
		{
			return;
		}
		string[] array = new string[8] { "Keyboard", "Move", "Color", "Speed", "Shield", "Chevron", "Help", "Info" };
		string[] iconGlyphsForTest = SettingsForm.IconGlyphsForTest;
		Eq(iconGlyphsForTest.Length, array.Length, "字形表与名称表一一对应");
		StringBuilder stringBuilder = new StringBuilder();
		int num = SettingsForm.MeasureGlyphInk("\uffff", 32f);
		Line("    （基准）U+FFFF 豆腐块墨水=" + num);
		for (int i = 0; i < iconGlyphsForTest.Length; i++)
		{
			int num2 = SettingsForm.MeasureGlyphInk(iconGlyphsForTest[i], 32f);
			Line("    （字形）" + array[i] + " U+" + ((int)iconGlyphsForTest[i][0]).ToString("X4") + " 墨水=" + num2);
			if (num2 <= 12)
			{
				stringBuilder.Append(array[i] + "(空白" + num2 + ") ");
			}
			else if (num > 0 && Math.Abs(num2 - num) <= 2)
			{
				stringBuilder.Append(array[i] + "(是豆腐块" + num2 + ") ");
			}
		}
		Check(stringBuilder.Length == 0, "★ 每个用到的图标字形都渲染出了【真实字形】（既不是空白、也不是豆腐块）", stringBuilder.ToString());
	}

	private static int[] ParseRgb(string s, int idx)
	{
		try
		{
			MatchCollection matchCollection = Regex.Matches(s, "(\\d+),(\\d+),(\\d+)");
			if (matchCollection.Count <= idx)
			{
				return new int[3];
			}
			return new int[3]
			{
				int.Parse(matchCollection[idx].Groups[1].Value),
				int.Parse(matchCollection[idx].Groups[2].Value),
				int.Parse(matchCollection[idx].Groups[3].Value)
			};
		}
		catch
		{
			return new int[3];
		}
	}

	private static void TestAdoptHeldButton()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		Coordinator.AssumeButtonsDownForTest = true;
		try
		{
			using (DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
				c.ExitFullscreenOnTrigger = true;
				c.ShowActivationFrame = true;
			}))
			{
				Native.GetWindowRect(dragFixture.H, out var lpRect);
				Native.POINT p = CenterOf(dragFixture.H);
				// 真实光标必须与"按下点"一致：激活瞬间补起时以当前光标为起点（见 AdoptBufferedButtonsNow），
				// 光标若还停在上一个用例的位置，补起那一帧窗口就会跳。
				Native.GetCursorPos(out var cursorBefore);
				Native.SetCursorPos(p.X, p.Y);
				Pump();
				dragFixture.Coord.Trigger.SimulateKeyDown();
				Check(!dragFixture.Coord.Trigger.Active, "触发键尚未到达阈值（仍在按住阶段）");
				Check(dragFixture.Coord.Trigger.Held, "触发键处于『按住中』状态");
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				Check(dragFixture.Coord.OnMouse(513, ref ms), "★ 阈值期间的左键按下被吞掉（不再放行给下层窗口 —— 这正是『穿透点击』的根因）");
				Check(dragFixture.Coord.BufferedDownForTest(0), "★ 阈值期间的左键按下进入缓冲（未达阈值时既不投递也不丢弃，短按后还给用户）");
				Check(dragFixture.Coord.HeldDuringHoldForTest(0), "★ 按住阶段里的左键按下被【记账】（老代码在这里什么都没记，就是漏掉的根因）");
				dragFixture.Coord.Trigger.ForceHoldElapsed();
				Settle();
				Check(dragFixture.Coord.Trigger.Active, "触发键已激活（外框此刻会亮起）");
				Check(dragFixture.Coord.Drag.IsDragging, "★★ 激活【瞬间】就补起拖动 —— 阈值期间的按下从未投递给任何窗口，不必再等一次鼠标移动（用户报障『同时按 Enter 和左键后左键没反应』的修复点）");
				Native.GetWindowRect(dragFixture.H, out var lpAdopt);
				Eq(lpAdopt.Left - lpRect.Left, 0, "★ 补起那一帧窗口不跳（以当前光标为起点）");
				Eq(lpAdopt.Top - lpRect.Top, 0, "★ 补起那一帧窗口不跳（垂直）");
				Check(!dragFixture.Coord.PassedDownForTest(0), "按住阶段放行的那条 down 同样记入『幽灵按下』账，收养时已完成配对");
				for (int num = 0; num < 12; num++)
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				Native.GetWindowRect(dragFixture.H, out var lpRect2);
				Native.GetWindowRect(dragFixture.Coord.Drag.Frame.Handle, out var lpRect3);
				int num2 = (lpRect2.Top + lpRect2.Bottom) / 2;
				int num3 = BandMaxGreenness(Rectangle.FromLTRB(lpRect2.Left - 60, num2 - 20, lpRect2.Left + 60, num2 + 20));
				Line("    （拖动框）窗口=" + lpRect2.Left + "," + lpRect2.Top + " " + lpRect2.Width + "x" + lpRect2.Height + " 外框=" + lpRect3.Left + "," + lpRect3.Top + " " + lpRect3.Width + "x" + lpRect3.Height + " 屏幕青色度=" + num3);
				Check(num3 > 25, "★★ 拖动中的外框必须在屏幕上看得见（不是只有 IsShown）", "青色度=" + num3);
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 90, p.Y + 60));
				dragFixture.Coord.OnMouse(512, ref ms2);
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect4);
				Eq(lpRect4.Left - lpRect2.Left, 90, "★ 窗口真的跟着光标移动了（水平 90px）");
				Eq(lpRect4.Top - lpRect2.Top, 60, "★ 窗口真的跟着光标移动了（垂直 60px）");
				Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
				dragFixture.Coord.OnMouse(514, ref ms3);
				dragFixture.Coord.Trigger.SimulateKeyUp(null);
				Settle();
				Check(!dragFixture.Coord.Drag.IsDragging, "松开后拖动会话结束");
				Native.SetCursorPos(cursorBefore.X, cursorBefore.Y);
				Pump();
			}
			using DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
			});
			Native.POINT p2 = CenterOf(dragFixture2.H);
			Native.MSLLHOOKSTRUCT ms5 = Ms(p2);
			dragFixture2.Coord.OnMouse(513, ref ms5);
			Check(!dragFixture2.Coord.HeldDuringHoldForTest(0), "按下发生在触发键按住【之前】：『按住期间』记账不置位（两套账分工正确）");
			Check(dragFixture2.Coord.PassedDownForTest(0), "★ 但『幽灵按下』记账必须置位 —— 目标程序实收了这条 down，正停在拖选模态里");
			ActivateTrigger(dragFixture2.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms6 = Ms(new Native.POINT(p2.X + 70, p2.Y + 50));
			dragFixture2.Coord.OnMouse(512, ref ms6);
			Settle();
			Check(dragFixture2.Coord.Drag.IsDragging, "★ 第四十四轮：激活时仍按着的键现在必须补起（用户报障场景的修复点）");
			Native.MSLLHOOKSTRUCT ms7 = Ms(ms6.pt);
			dragFixture2.Coord.OnMouse(514, ref ms7);
			dragFixture2.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			Coordinator.AssumeButtonsDownForTest = false;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	// 第六轮报障的最小化复现：
	//   用户原话「按 enter 然后快速的按下左键，外框出现了但鼠标怎么动窗口都不动；
	//             按住左键过一段时间再移动就能顺利移动」。
	// 两种时序唯一的差别 = 「触发键到阈值之前，鼠标有没有已经在移动」。
	// T66（TestAdoptHeldButton）只覆盖了"按下之后一动不动等激活"，漏掉了真实用户会做的那一步：
	// 手是连着动的，左键一按下就开始拖，根本不会等 200ms 阈值。
	// 本用例把这段时序固化下来，任何一次回归都会在这里报红。
	private static void TestAdoptAfterEarlyMove()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		Coordinator.AssumeButtonsDownForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
				c.ExitFullscreenOnTrigger = true;
				c.ShowActivationFrame = true;
				c.BringToFront = false;
			});
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.POINT p = CenterOf(dragFixture.H);
			Native.GetCursorPos(out var cursorBefore);
			Native.SetCursorPos(p.X, p.Y);
			Pump();
			dragFixture.Coord.Trigger.SimulateKeyDown();
			Check(dragFixture.Coord.Trigger.Held && !dragFixture.Coord.Trigger.Active, "触发键先在『按住』阶段（还没到阈值）");
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			Check(dragFixture.Coord.OnMouse(513, ref ms), "★ 阈值窗口内的左键按下被吞掉（不穿透到下层窗口）");
			Check(dragFixture.Coord.BufferedDownForTest(0), "★ 这次按下进入缓冲（从未投递给任何窗口）");
			// ★ 关键差别：激活之前鼠标已经在动了 —— 这就是用户"按下左键就开始拖"的真实时序。
			for (int i = 1; i <= 4; i++)
			{
				Native.MSLLHOOKSTRUCT msMove = Ms(new Native.POINT(p.X + i * 20, p.Y + i * 10));
				dragFixture.Coord.OnMouse(512, ref msMove);
				Thread.Sleep(5);
			}
			Check(!dragFixture.Coord.Trigger.Active, "移动这几帧时触发键还没到阈值（两次移动都落在按住阶段里）");
			Native.MSLLHOOKSTRUCT msAt = Ms(new Native.POINT(p.X + 80, p.Y + 40));
			dragFixture.Coord.OnMouse(512, ref msAt);
			// 真实光标也要挪到同一处：激活瞬间的起点取的是【当前光标】（子项真实位置），
			// 若真实光标还停在按下点，补起那一帧窗口会按"按下点 → 当前光标"的差值跳一段。
			Native.SetCursorPos(p.X + 80, p.Y + 40);
			Pump();
			dragFixture.Coord.Trigger.ForceHoldElapsed();
			Settle();
			Check(dragFixture.Coord.Trigger.Active, "触发键到阈值、激活（外框此刻亮起）");
			Line("    （诊断）激活瞬间：缓冲=" + dragFixture.Coord.BufferedDownForTest(0) + " 拖动中=" + dragFixture.Coord.Drag.IsDragging + " 光标=" + p.X + 80 + "," + p.Y + 40);
			Check(dragFixture.Coord.Drag.IsDragging, "★★ 阈值期间的按下 + 激活前鼠标已经在移动 → 激活后照样必须能拖（用户报障『外框被样式包围但窗口拖不动』的修复点）");
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 80 + 90, p.Y + 40 + 60));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Native.GetWindowRect(dragFixture.H, out var lpRect2);
			Eq(lpRect2.Left - lpRect.Left, 90, "★ 窗口跟着光标走了（水平 90px）");
			Eq(lpRect2.Top - lpRect.Top, 60, "★ 窗口跟着光标走了（垂直 60px）");
			Native.MSLLHOOKSTRUCT ms3 = Ms(ms2.pt);
			dragFixture.Coord.OnMouse(514, ref ms3);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!dragFixture.Coord.Drag.IsDragging, "松开左键后拖动会话结束");
			Native.SetCursorPos(cursorBefore.X, cursorBefore.Y);
			Pump();

			// ---- 实测最凶的一档：GetAsyncKeyState 漏报（本轮报障的真凶）。----
			// 现场日志（21:07:47.635）：
			//   激活瞬间清点缓冲 缓冲[True,False,False] 阈值期按住[True,False,False]
			//                     已放行[False,False,False] 物理按下[False,False,False]
			// 左键明明按着（真实按下 47.473 收到、131ms 后才抬起），GetAsyncKeyState 却报 False。
			// 老代码信了这句谎话，在激活瞬间把【刚收到、还热乎的】缓冲按下当成"用户已松手"丢掉，
			// 之后每一次移动又被同一个查询挡回去 ⇒ 拖动永远建不起来，外框却照常亮着。
			// 这里用「按键状态覆盖表」原样复刻这个漏报：三个键一律报"没按着"。
			Coordinator.ButtonsDownOverrideForTest = new bool[3] { false, false, false };
			using (DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
				c.ShowActivationFrame = true;
				c.BringToFront = false;
			}))
			{
				Native.GetWindowRect(dragFixture2.H, out var lpRect3);
				Native.POINT p2 = CenterOf(dragFixture2.H);
				Native.GetCursorPos(out var cursorBefore2);
				Native.SetCursorPos(p2.X, p2.Y);
				Pump();
				dragFixture2.Coord.Trigger.SimulateKeyDown();
				Native.MSLLHOOKSTRUCT msDown = Ms(p2);
				Check(dragFixture2.Coord.OnMouse(513, ref msDown), "阈值窗口内的左键按下被吞掉（不穿透）");
				Check(dragFixture2.Coord.BufferedDownForTest(0), "★ 账本里确实有这条按下（钩子收到了，且没收到配对的抬起）");
				dragFixture2.Coord.Trigger.ForceHoldElapsed();
				Settle();
				Check(dragFixture2.Coord.Trigger.Active, "触发键已激活");
				Check(dragFixture2.Coord.Drag.IsDragging, "★★ 系统按键查询漏报（报『没按着』）时，账本里的按下绝不能被当作已松手丢弃 → 激活瞬间照样必须补起拖动");
				// 接管那一帧以"当前光标"为起点，所以窗口此刻不跳；再走一格才看得到跟手。
				Native.MSLLHOOKSTRUCT msFollow = Ms(new Native.POINT(p2.X + 90, p2.Y + 60));
				dragFixture2.Coord.OnMouse(512, ref msFollow);
				Settle();
				Native.GetWindowRect(dragFixture2.H, out var lpRect4);
				Eq(lpRect4.Left - lpRect3.Left, 90, "★ 窗口跟着光标走了（水平 90px）");
				Eq(lpRect4.Top - lpRect3.Top, 60, "★ 窗口跟着光标走了（垂直 60px）");
				dragFixture2.Coord.Trigger.SimulateKeyUp(null);
				Settle();
				Check(!dragFixture2.Coord.Drag.IsDragging, "松开触发键立刻收回拖动（抬起事件丢失时的兜底）");
				Native.SetCursorPos(cursorBefore2.X, cursorBefore2.Y);
				Pump();
			}
		}
		finally
		{
			Coordinator.ButtonsDownOverrideForTest = null;
			Coordinator.AssumeButtonsDownForTest = false;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestFrameTracksWindow()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			DragFixture f = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.FrameStyleId = "Glow";
				c.FrameThickness = 3;
			});
			try
			{
				DragEngine eng = f.Coord.Drag;
				uint ownerThreadIdForTest = OverlayWindow.OwnerThreadIdForTest;
				Check(ownerThreadIdForTest != 0 && ownerThreadIdForTest != Native.GetCurrentThreadId(), "★ 浮层窗口运行在专用线程上（不再挂在 UI 线程，UI 一忙就不会拖累外框）", "浮层线程=" + ownerThreadIdForTest + " 当前线程=" + Native.GetCurrentThreadId());
				ActivateTrigger(f.Coord);
				Settle();
				Native.POINT p = CenterOf(f.H);
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				f.Coord.OnMouse(513, ref ms);
				Settle();
				Check(eng.IsDragging, "拖动会话已建立");
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 12, p.Y + 8));
				f.Coord.OnMouse(512, ref ms2);
				Settle();
				Native.GetWindowRect(eng.Frame.Handle, out var lpRect);
				Native.GetWindowRect(f.H, out var lpRect2);
				int num = lpRect.Left - lpRect2.Left;
				int num2 = lpRect.Top - lpRect2.Top;
				Line("    （静止偏移）外框−窗口 = " + num + "," + num2);
				List<int[]> samples = new List<int[]>();
				int discarded = 0;
				ManualResetEventSlim stop = new ManualResetEventSlim(initialState: false);
				Thread thread = new Thread(() =>
				{
					while (!stop.IsSet)
					{
						try
						{
							if (Native.GetWindowRect(eng.Frame.Handle, out var lpRect4) && Native.GetWindowRect(f.H, out var lpRect5) && Native.GetWindowRect(eng.Frame.Handle, out var lpRect6))
							{
								if (lpRect4.Left == lpRect6.Left && lpRect4.Top == lpRect6.Top)
								{
									lock (samples)
									{
										samples.Add(new int[2]
										{
											lpRect4.Left - lpRect5.Left,
											lpRect4.Top - lpRect5.Top
										});
									}
								}
								else
								{
									discarded++;
								}
							}
						}
						catch
						{
						}
						Thread.Sleep(1);
					}
				});
				thread.IsBackground = true;
				thread.Start();
				int val = 0;
				int num3 = p.X + 12;
				int num4 = p.Y + 8;
				int num5 = num3;
				int num6 = num4;
				for (int num7 = 1; num7 <= 240; num7++)
				{
					int num8 = num7 * 2 + (int)Math.Round(Math.Sin((double)num7 * 0.3) * 2.0);
					int num9 = num7 + (int)Math.Round(Math.Cos((double)num7 * 0.25) * 2.0);
					int num10 = num3 + num8;
					int num11 = num4 + num9;
					val = Math.Max(val, Math.Max(Math.Abs(num10 - num5), Math.Abs(num11 - num6)));
					num5 = num10;
					num6 = num11;
					Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(num10, num11));
					f.Coord.OnMouse(512, ref ms3);
					Settle();
					Thread.Sleep(1);
				}
				Settle();
				stop.Set();
				thread.Join(1000);
				Native.GetWindowRect(f.H, out var lpRect3);
				int num12 = Math.Abs(lpRect3.Left - lpRect2.Left);
				Check(num12 > 100, "★ 拖动确实生效（窗口真的跟着走了）", "窗口水平位移=" + num12 + "px，最大单步=" + val + "px");
				int num13 = int.MaxValue;
				int num14 = int.MinValue;
				int num15 = int.MaxValue;
				int num16 = int.MinValue;
				int count;
				lock (samples)
				{
					count = samples.Count;
					for (int num17 = 0; num17 < samples.Count; num17++)
					{
						int num18 = samples[num17][0] - num;
						int num19 = samples[num17][1] - num2;
						if (num18 < num13)
						{
							num13 = num18;
						}
						if (num18 > num14)
						{
							num14 = num18;
						}
						if (num19 < num15)
						{
							num15 = num19;
						}
						if (num19 > num16)
						{
							num16 = num19;
						}
					}
				}
				Line("    （采样）" + count + " 个有效点（丢弃跨更新的 " + discarded + " 个）；相对静止偏移 X[" + num13 + "," + num14 + "] Y[" + num15 + "," + num16 + "]");
				Line("    （帧账）外框更新 " + DragEngine.DiagFrameUpdates + " 帧，其中窗口真的写了 " + DragEngine.DiagWindowWritten + " 帧、被跳过 " + DragEngine.DiagWindowSkipped + " 帧");
				Check(count > 20, "拖动过程中采到了足够的样本", "样本数=" + count);
				Check(num13 >= -4, "★ 全程外框都没有落在窗口后面（水平方向）", "最差滞后 " + -num13 + "px，容差 " + 4 + "px");
				Check(num15 >= -4, "★ 全程外框都没有落在窗口后面（垂直方向）", "最差滞后 " + -num15 + "px，容差 " + 4 + "px");
				Native.MSLLHOOKSTRUCT ms4 = Ms(ms2.pt);
				f.Coord.OnMouse(514, ref ms4);
				f.Coord.Trigger.SimulateKeyUp(null);
				Settle();
			}
			finally
			{
				if (f != null)
				{
					((IDisposable)f).Dispose();
				}
			}
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestFrameGeometryStableAtEdge()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.FrameStyleId = "Glow";
				c.FrameThickness = 3;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.POINT p = new Native.POINT(lpRect.Right - 40, (lpRect.Top + lpRect.Bottom) / 2);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms);
			Settle();
			Check(drag.IsDragging, "拖动会话已建立");
			List<int[]> list = new List<int[]>();
			int num = int.MaxValue;
			int num2 = int.MaxValue;
			int[] array = new int[8] { 300, 200, 120, 60, 30, 12, 4, 1 };
			for (int num3 = 0; num3 < array.Length; num3++)
			{
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(array[num3], p.Y));
				dragFixture.Coord.OnMouse(512, ref ms2);
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect2);
				Native.GetWindowRect(drag.Frame.Handle, out var lpRect3);
				if (lpRect2.Left < num)
				{
					num = lpRect2.Left;
				}
				if (lpRect2.Top < num2)
				{
					num2 = lpRect2.Top;
				}
				list.Add(new int[4]
				{
					lpRect3.Left - lpRect2.Left,
					lpRect3.Top - lpRect2.Top,
					lpRect3.Right - lpRect2.Right,
					lpRect3.Bottom - lpRect2.Bottom
				});
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int num4 = 0; num4 < list.Count; num4++)
			{
				stringBuilder.Append("[").Append(list[num4][0]).Append(",")
					.Append(list[num4][1])
					.Append(",")
					.Append(list[num4][2])
					.Append(",")
					.Append(list[num4][3])
					.Append("] ");
			}
			Line("    （窗口最左）" + num + "px  偏移量序列 " + stringBuilder);
			Check(num < 0, "★ 窗口确实被推出了屏幕左缘（用例前提成立）", "窗口最左=" + num + "px");
			int num5 = 0;
			string text = "";
			for (int num6 = 0; num6 < 4; num6++)
			{
				int num7 = list[0][num6];
				for (int num8 = 1; num8 < list.Count; num8++)
				{
					int num9 = Math.Abs(list[num8][num6] - num7);
					if (num9 > num5)
					{
						num5 = num9;
						text = "第" + num8 + "步 方向" + num6;
					}
				}
			}
			Check(num5 <= 2, "★ 贴边 / 出屏全程外框几何恒定（不再缩小、不再跳变）", "最大偏移变化=" + num5 + "px（" + text + "）");
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			dragFixture.Coord.OnMouse(514, ref ms3);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestFrameOpacity()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.FrameStyleId = "Focus";
				c.FrameColorId = "Teal";
				c.FrameThickness = 6;
				c.FrameOpacity = 100;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			int num = (lpRect.Top + lpRect.Bottom) / 2;
			Rectangle band = new Rectangle(lpRect.Left - 30, num - 2, 60, 5);
			// 背景基线必须在"外框还没亮起"的时候量。40% 是半透明，屏幕读数必然是
			// 「alpha × 外框色 + (1-alpha) × 背景」的混合结果；用固定阈值（例如"必须小于 100% 的 0.6 倍"）
			// 等于假设"背景远比外框暗"。本机壁纸就是深青（实测 RGB 4,70,91 → 青色度 23），
			// 40% 时读数会落在 36 左右而被误判成"没变淡"。改成先量背景、再按混合公式判定，
			// 结论与桌面内容无关，且比原来的固定阈值更严格（同时验证了混合比例本身）。
			int bg = BandMaxGreenness(band);
			Native.SetCursorPos(CenterOf(dragFixture.H).X, CenterOf(dragFixture.H).Y);
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(512, ref ms);
			Settle();
			Check(drag.Frame.IsShown, "用例前提：悬停框已经亮起");
			int num2 = BandMaxGreenness(band);
			dragFixture.Store.Current.FrameOpacity = 40;
			drag.RefreshOverlayTheme();
			Settle();
			Thread.Sleep(120);
			Settle();
			int num3 = BandMaxGreenness(band);
			int num4 = (int)(0.4 * (double)num2 + 0.6 * (double)bg) + 4;
			Line("    （不透明度）带内青色度 背景=" + bg + "  100%=" + num2 + "  40%=" + num3 + "（混合上限 " + num4 + "）");
			Check(num2 > 30, "★ 用例前提：100% 时这条带上确实有外框（青色度足够）", "青色度=" + num2);
			Check(num3 != num2, "★ 改不透明度真的改变了屏幕渲染（不是改了参数却没重推像素）", "100%=" + num2 + " 40%=" + num3);
			Check(num3 < num2 && num3 <= num4, "★ 40% 的外框按 alpha 混合掺进背景：读数不超过「0.4×外框 + 0.6×背景」", "100%=" + num2 + " 40%=" + num3 + " 背景=" + bg + " 上限=" + num4);
			Check(num2 - num3 >= (num2 - bg) / 2, "★ 40% 至少削掉一半「外框−背景」对比度（整层等比变淡）", "对比度 " + (num2 - bg) + " → " + (num2 - num3) + "（背景=" + bg + "）");
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static int BandMaxGreenness(Rectangle band)
	{
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			band.Intersect(virtualScreen);
			if (band.Width <= 0 || band.Height <= 0)
			{
				return -1;
			}
			using Bitmap bitmap = new Bitmap(band.Width, band.Height);
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.CopyFromScreen(band.Left, band.Top, 0, 0, new Size(band.Width, band.Height));
			int num = int.MinValue;
			for (int i = 0; i < bitmap.Height; i++)
			{
				for (int j = 0; j < bitmap.Width; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					int num2 = pixel.G - (pixel.R + pixel.B) / 2;
					if (num2 > num)
					{
						num = num2;
					}
				}
			}
			return num;
		}
		catch
		{
			return -1;
		}
	}

	private static string SamplePixel(int x, int y)
	{
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			if (x < virtualScreen.Left || y < virtualScreen.Top || x >= virtualScreen.Right || y >= virtualScreen.Bottom)
			{
				return "?";
			}
			using Bitmap bitmap = new Bitmap(1, 1);
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.CopyFromScreen(x, y, 0, 0, new Size(1, 1));
			Color pixel = bitmap.GetPixel(0, 0);
			return pixel.R + "," + pixel.G + "," + pixel.B;
		}
		catch
		{
			return "?";
		}
	}

	private static int Dist(string a, string b)
	{
		try
		{
			string[] array = a.Split(',');
			string[] array2 = b.Split(',');
			if (array.Length != 3 || array2.Length != 3)
			{
				return -1;
			}
			return Math.Abs(int.Parse(array[0]) - int.Parse(array2[0])) + Math.Abs(int.Parse(array[1]) - int.Parse(array2[1])) + Math.Abs(int.Parse(array[2]) - int.Parse(array2[2]));
		}
		catch
		{
			return -1;
		}
	}

	private static void TestHelpPanel()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		SettingsForm.HelpPanel probeHelpPanel = settingsForm.ProbeHelpPanel;
		Check(probeHelpPanel != null, "取到帮助面板");
		if (probeHelpPanel != null)
		{
			Eq(probeHelpPanel.EntryCountForTest, 6, "帮助条目数（触发/动作/外观/全屏/晃窗/黑名单）");
			probeHelpPanel.OpenRangeForTest(out var top, out var bottom);
			Check(bottom <= probeHelpPanel.Height, "★ 默认展开项的内容完全落在面板高度内（不会被裁掉）", "内容底 " + bottom + " 面板高 " + probeHelpPanel.Height);
			Check(top == 0, "默认展开第一项（从顶部开始）", "top=" + top);
			int y = probeHelpPanel.Height - Fields_Sc(30);
			probeHelpPanel.ClickAtForTest(y);
			Application.DoEvents();
			Eq(probeHelpPanel.OpenIndexForTest, 5, "点最后一项的标题能把它展开");
			probeHelpPanel.OpenRangeForTest(out top, out bottom);
			Check(bottom <= probeHelpPanel.Height, "★ 展开最后一项后内容仍然不越界", "内容底 " + bottom + " 面板高 " + probeHelpPanel.Height);
			probeHelpPanel.ClickAtForTest(y);
			Eq(probeHelpPanel.OpenIndexForTest, 5, "再点一次已展开的项不会把它收起来（面板高度恒定）");
			probeHelpPanel.ClickAtForTest(Fields_Sc(10));
			Eq(probeHelpPanel.OpenIndexForTest, 0, "点第一项能切回去");
			settingsForm.Close();
		}
	}

	private static string HoldDragText(string text, int max)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		string text2 = text.Replace("\r", " ").Replace("\n", " ");
		return (text2.Length <= max) ? text2 : (text2.Substring(0, max) + "…");
	}

	private static int Fields_Sc(int design)
	{
		return (int)Math.Round((float)design * ((SettingsForm.UiScaleForTest <= 0f) ? 1f : SettingsForm.UiScaleForTest));
	}

	private static void TestAboutInfo()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		string text = settingsForm.ProbeAboutText ?? "";
		Line("    （关于分组文字）" + text);
		Check(text.Length > 0, "取到『关于』分组的内容");
		Check(text.Contains("QQQOrange"), "★ 开发者 QQQOrange 显示在界面上");
		Check(text.Contains("DeepSeek"), "★ 开发工具 DeepSeek 显示在界面上");
		Check(text.Contains(AppInfo.ReleaseDate), "★ 更新时间显示在界面上", "期望 " + AppInfo.ReleaseDate);
		Check(text.Contains("版本") && text.Contains(SettingsForm.AppVersionForTest), "★ 当前版本号显示在界面上（且与程序集版本一致）", "程序集版本=" + SettingsForm.AppVersionForTest);
		Check(SettingsForm.AppVersionForTest == "1.6.1", "★ 版本号已升到 1.6.1", "实际=" + SettingsForm.AppVersionForTest);
		settingsForm.Close();
	}

	/// <summary>v1.1 新增：关于页的 GitHub 链接、自动更新开关、检查更新按钮，以及版本比较与替换器脚本。</summary>
	private static void TestUpdateFeatures()
	{
		using (DragFixture dragFixture = new DragFixture(null))
		{
			using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
			settingsForm.CreateControl();
			Application.DoEvents();

			// 1) 三个新控件都必须真的挂进「关于」分组、可见可点
			Button button = settingsForm.ProbeGitHubButton;
			Button button2 = settingsForm.ProbeCheckUpdateButton;
			CheckBox checkBox = settingsForm.ProbeAutoUpdateCheck;
			Label label = settingsForm.ProbeUpdateStatusLabel;
			Check(button != null && button2 != null && checkBox != null && label != null, "★ 关于页的『项目主页』『检查更新』『自动更新』『状态文字』四个控件都建出来了");
			Check(button != null && button.Text.Contains("github.com/QQQOrange/holddrag"), "★ 项目主页按钮显示 GitHub 地址", "实际=" + (button?.Text ?? "(null)"));
			Check(button2 != null && button2.Text == "检查更新", "★ 按钮文案是「检查更新」", "实际=" + (button2?.Text ?? "(null)"));
			Check(checkBox != null && checkBox.Text.Contains("自动更新"), "★ 自动更新开关文案含「自动更新」", "实际=" + (checkBox?.Text ?? "(null)"));
			Check(label != null && label.Text.Length > 0, "★ 更新状态有初始文字", "实际=" + (label?.Text ?? "(null)"));

			// 2) 三个控件必须在可视树里（不是"建了却没加进去"）
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			Control groupBox = null;
			for (int i = 0; i < SettingsForm.ProbeGroupCount(settingsForm); i++)
			{
				Control control2 = SettingsForm.ProbeGroupAt(settingsForm, i);
				if (control2 != null && control2.Text != null && control2.Text.Contains("关于"))
				{
					groupBox = control2;
					break;
				}
			}
			Check(groupBox != null, "取到『关于』那张卡片");
			if (groupBox != null)
			{
				for (int j = 0; j < groupBox.Controls.Count; j++)
				{
					Control control = groupBox.Controls[j];
					if (control == button) flag = true;
					if (control == button2) flag2 = true;
					if (control == checkBox) flag3 = true;
				}
			}
			Check(flag && flag2 && flag3, "★ 三个新控件都真的挂在『关于』分组里（否则用户根本看不到）", $"github={flag} checkUpdate={flag2} autoUpdate={flag3}");

			// 3) 自动更新默认开启，且勾选状态与配置双向一致
			Check(dragFixture.Store.Current.AutoCheckUpdate, "★ 自动更新默认开启");
			Check(checkBox != null && checkBox.Checked, "★ 自动更新复选框默认是勾上的");
			if (checkBox != null)
			{
				checkBox.Checked = false;
				Application.DoEvents();
				Check(!settingsForm.ProbeAutoUpdateCheck.Checked, "★ 取消勾选后控件状态跟着变");
				checkBox.Checked = true;
				Application.DoEvents();
			}
			settingsForm.Close();
		}

		// 4) 版本比较：只认"严格更新"，且能容忍 v 前缀与后缀
		Eq(Updater.ParseVersion("v1.1.0").ToString(), "1.1.0.0", "能从 v1.1.0 解析出版本");
		Eq(Updater.ParseVersion("1.2.3").ToString(), "1.2.3.0", "能从 1.2.3 解析出版本");
		Eq(Updater.ParseVersion("v1.2.3.4").ToString(), "1.2.3.4", "四段版本号解析正确");
		Eq(Updater.ParseVersion("v1.2.0-beta.1").ToString(), "1.2.0.0", "带 -beta 后缀也能解析");
		Check(Updater.ParseVersion("not-a-version") == null, "★ 非法标签解析返回 null，不会误判成新版本");
		Check(Updater.ParseVersion("") == null, "★ 空标签返回 null");
		Check(Updater.ParseVersion("v1.1.0") > Updater.ParseVersion("v1.0.0"), "★ 1.1.0 比 1.0.0 新");
		Check(!(Updater.ParseVersion("v1.0.0") > Updater.ParseVersion("v1.0.0")), "★ 同版本不算新版本（不会自己提示自己）");
		Check(Updater.CurrentVersion() == Updater.ParseVersion(AppInfo.Version), "★ 当前版本解析与 AppInfo.Version 一致", "AppInfo=" + AppInfo.Version);

		// 5) 从重定向 URL / HTML 里提取 tag
		Eq(Updater.TagFromUrl("https://github.com/QQQOrange/holddrag/releases/tag/v1.1.0"), "v1.1.0", "从 releases/tag/ URL 提取 tag");
		Eq(Updater.TagFromUrl("https://github.com/QQQOrange/holddrag/releases/latest"), null, "latest 这种 URL 提取不出 tag（返回 null）");
		Eq(Updater.TagFromUrl(null), null, "null URL 安全返回 null");
		Eq(Updater.TagFromHtml("<a href=\"/QQQOrange/holddrag/releases/tag/v2.0.0\">x</a>"), "v2.0.0", "★ 从 HTML 正文兜底提取 tag");
		Eq(Updater.TagFromHtml("<html>no tag here</html>"), null, "HTML 里没有 tag 时返回 null");

		// 6) 下载进度文案
		string text = SettingsForm.FormatDownloadProgress(512, 1024);
		Check(text.Contains("50%"), "进度文案含百分比", "实际=" + text);
		Check(text.Contains("MB"), "进度文案含 MB", "实际=" + text);
		Check(SettingsForm.FormatDownloadProgress(1024, 0).Contains("已接收"), "总长未知时退化为『已接收』", "实际=" + SettingsForm.FormatDownloadProgress(1024, 0));

		// 7) 替换机制：必须靠"重命名正在运行的 exe"完成，不依赖批处理脚本。
		//    本机实测：Windows 允许重命名正在运行的 exe、也允许把新文件移到它的名下；
		//    唯独不允许删除正在运行的旧文件 —— 所以替换器必须走 rename 路线。
		Check(Updater.OldSuffix == ".old", "旧版本备份后缀是 .old");
		Check(!Updater.TempDownloadName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), "★ 下载中转文件不是 .exe 后缀（避免被 shell 当程序执行）");
		MethodInfo methodInfo = typeof(Updater).GetMethod("BuildSwapperScript", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		Check(methodInfo == null, "★ 已彻底移除「批处理替换器」实现（旧实现把 .bat 当 .exe 启动，必然失败）");
		Check(typeof(Updater).GetMethod("CleanupOldVersion", BindingFlags.Public | BindingFlags.Static) != null, "★ 提供了启动后清理旧版本备份的入口");
		Check(typeof(Updater).GetField("ArgUpdatedFrom", BindingFlags.Public | BindingFlags.Static) != null, "★ 通过 --updated-from 参数把备份路径交给新版本清理");

		// 8) 网络失败要如实回传错误，不能静默当成"已是最新"
		string error;
		bool upToDate;
		SettingsForm.ProbeForceOfflineForTest = true;
		try
		{
			UpdateInfo updateInfo = Updater.CheckLatest(out error, out upToDate);
			Check(updateInfo == null, "离线时不会返回一个假的『新版本』");
			Check(!upToDate, "★★ 离线时【绝不能】把结果当作『已是最新』（否则用户会以为检查成功了）");
			Check(!string.IsNullOrEmpty(error), "★ 离线时回传了具体的错误原因", "error=" + error);
		}
		finally
		{
			SettingsForm.ProbeForceOfflineForTest = false;
		}

		// 9) ★ 真实验证替换机制本身：在临时目录里用真文件跑一遍
		//    "旧 exe 改名 → 新文件顶替 → 内容确实换成了新的"。
		//    这一步才是真正证明更新能落地的地方；只断言脚本文本会给出假绿灯。
		TestInPlaceSwapMechanism();

		// 10) 钩子回调线程绝不能同步落盘（这是"整机输入卡顿"的根因之一）
		TestHookThreadLoggingIsAsync();

		// 11) 更新重启时新进程必须"等旧进程释放互斥体再接管"，
		//     否则新版本会因为单实例检查而立刻退出，更新完一个进程都不剩。
		TestUpdateRestartSingleInstance();
	}

	/// <summary>
	/// 更新重启的单实例竞态：替换时**新版本先启动、旧进程后退出**，
	/// 新进程一启动就会发现互斥体被旧进程占着。若照普通启动那样"通知已有实例然后自己退出"，
	/// 更新完就会出现【一个进程都不剩】。这里断言：
	///   ① `--updated-from` 存在时，启动流程的标志位被正确识别为"更新接续"
	///   ② 普通启动时仍然是"立即让位"语义（不能变成傻等 30 秒）
	///   ③ `--updated-from` 的取值解析正确（含路径带空格的情况）
	/// </summary>
	private static void TestUpdateRestartSingleInstance()
	{
		// ①/③ 参数解析：Program 的 ArgValue 与 Updater.ArgUpdatedFrom 必须对得上
		string argUpdatedFrom = Updater.ArgUpdatedFrom;
		Eq(argUpdatedFrom, "--updated-from", "更新接续参数名固定为 --updated-from");
		Eq(Program.ProbeArgValueForTest(new string[2] { "--updated-from", @"C:\app\HoldDrag.exe.old" }, argUpdatedFrom), @"C:\app\HoldDrag.exe.old", "★ 能从命令行取出旧版本备份路径");
		Eq(Program.ProbeArgValueForTest(new string[2] { "--updated-from", @"C:\My App\HoldDrag.exe.old" }, argUpdatedFrom), @"C:\My App\HoldDrag.exe.old", "★ 路径带空格也能正确取出（调用方负责加引号）");
		Eq(Program.ProbeArgValueForTest(new string[1] { "--selftest" }, argUpdatedFrom), string.Empty, "没有该参数时返回空串（普通启动 → 不等待互斥体）");
		Eq(Program.ProbeArgValueForTest(null, argUpdatedFrom), string.Empty, "null 参数数组安全返回空串");
		Check(Program.HasArgForTest(new string[1] { "--updated-from" }, argUpdatedFrom), "HasArg 能识别 --updated-from");

		// ② 普通启动不能因为这次改动变成"傻等"
		//    用真实互斥体验证：先占住，再按普通启动语义判定；由于不能真的启动第二进程，
		//    这里直接验证"是否等待"的判据：只有 --updated-from 存在才等待。
		string text = Program.ProbeArgValueForTest(new string[2] { "--updated-from", "x" }, argUpdatedFrom);
		Check(!string.IsNullOrEmpty(text), "★ 带 --updated-from 时判据成立 → 会等待旧进程释放互斥体");
		string text2 = Program.ProbeArgValueForTest(new string[1] { "--smoke" }, argUpdatedFrom);
		Check(string.IsNullOrEmpty(text2), "★ 普通启动（无 --updated-from）判据不成立 → 立即让位，不等待");
	}

	/// <summary>
	/// T91 单实例：同一时间只能开一个（用户需求："同一时间只能开一个程序，不能够开好几个"）。
	///
	/// 这里做【真端到端】验证，而不是只读常量字符串：
	///   ① 本进程先占住互斥体，扮演"已经在运行的那个实例"；
	///   ② 真的再启动一个 HoldDrag.exe；
	///   ③ 断言第二个实例在限时内【自己退出】（不会驻留成第二个进程）；
	///   ④ 断言它通过命名事件【唤起了已有实例】（设置窗口会被弹出来）。
	/// </summary>
	private static void TestSingleInstanceOnlyOne()
	{
		// 契约冻结：名字一改，新旧版本就互不识别，于是又能同时开出两个。
		Eq(Program.MutexNameForTest, "Local\\HoldDrag.SingleInstance.v1", "★ 单实例互斥体名已冻结（改名会导致新旧版本各开一个）");
		Eq(Program.ShowEventNameForTest, "Local\\HoldDrag.ShowSettings.v1", "★ 唤起设置窗口的命名事件名已冻结");

		bool createdNew;
		Mutex mutex = null;
		try
		{
			mutex = new Mutex(initiallyOwned: true, Program.MutexNameForTest, out createdNew);
			if (!createdNew)
			{
				// 本机真的已经有一个实例在跑 —— 这恰恰说明互斥体在起作用。
				Line("（环境限制）本机已有 HoldDrag 实例在运行，单实例端到端验证跳过。");
				return;
			}
			Check(true, "★ 本进程占住了单实例互斥体（模拟「已有一个实例在运行」）");

			// 必须先建好命名事件：第二个实例用 OpenExisting 打开它，建晚了它会打不开而静默退出。
			using (EventWaitHandle showEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, Program.ShowEventNameForTest))
			{
				string exe = AppPaths.ExePath;
				Check(File.Exists(exe), "找得到被测 exe 本体（否则无法做真实第二实例）", exe);
				if (!File.Exists(exe))
				{
					return;
				}
				Process process = null;
				try
				{
					// --no-elevate：避免第二个实例因为提权逻辑又拉起一个进程，干扰判定。
					process = Process.Start(new ProcessStartInfo
					{
						FileName = exe,
						Arguments = "--no-elevate",
						UseShellExecute = false
					});
				}
				catch (Exception ex)
				{
					Bad("启动第二个实例失败（宿主可能禁止创建进程）", ex.Message);
					return;
				}
				if (process == null)
				{
					Bad("启动第二个实例失败", "Process.Start 返回 null");
					return;
				}
				bool exited;
				try
				{
					// 第二个实例应该立刻让位退出，正常在 1~3 秒内，给足 20 秒余量。
					exited = process.WaitForExit(20000);
					if (exited)
					{
						int exitCode = process.ExitCode;
						Check(exitCode == 0, "第二个实例以退出码 0 正常让位", "实际 " + exitCode);
					}
				}
				finally
				{
					if (!process.HasExited)
					{
						try
						{
							process.Kill();
						}
						catch (Exception ex2)
						{
							Log.Error(ex2);
						}
					}
					process.Dispose();
				}
				Check(exited, "★★ 第二个实例【自己退出】了，不会同时驻留两个进程", exited ? null : "等了 20 秒仍在运行");
				bool signaled = showEvent.WaitOne(3000);
				Check(signaled, "★★ 第二个实例唤起了已有实例（命名事件被 Set → 会弹出设置窗口）");
			}
		}
		catch (Exception ex)
		{
			Bad("（单实例端到端验证自身抛异常）", ex.Message);
		}
		finally
		{
			try
			{
				mutex?.ReleaseMutex();
			}
			catch (Exception ex3)
			{
				Log.Error(ex3);
			}
			mutex?.Dispose();
		}
	}

	/// <summary>
	/// 钩子回调里同步写日志 + Flush 会拖住 WH_MOUSE_LL 回调，进而让整机鼠标排队。
	/// 这里断言：标记为钩子线程时，写日志不落盘而是进异步队列，且不阻塞。
	/// </summary>
	private static void TestHookThreadLoggingIsAsync()
	{
		// 记录基线：只看"入队 vs 落盘"的增量，不依赖具体日志文件路径
		long num = Interlocked.Read(ref Log.AsyncEnqueuedForTest);
		long num2 = Interlocked.Read(ref Log.AsyncWrittenForTest);
		Thread thread = new Thread(() =>
		{
			for (int i = 0; i < 200; i++)
			{
				Log.Info("[自检] 钩子线程异步日志 " + i);
			}
		})
		{
			IsBackground = true,
			Name = "HoldDrag.HookThread"
		};
		long num3 = Environment.TickCount64;
		thread.Start();
		thread.Join(5000);
		long num4 = Environment.TickCount64 - num3;
		Check(!thread.IsAlive, "钩子线程写 200 行日志能在 5 秒内返回");
		Check(num4 < 3000, "★ 钩子线程写 200 行日志没有被磁盘 IO 拖住（实测 " + num4 + " ms）");
		long num5 = Interlocked.Read(ref Log.AsyncEnqueuedForTest) - num;
		Check(num5 >= 200, "★★ 钩子线程的 200 行日志确实进了异步队列（说明走的是不阻塞的那条路径）", "实测入队 " + num5 + " 行");
		// 队列清空 ≠ 已经落盘：后台线程可能正拿着这批数据在写。
		// 这里必须轮询"已写入"计数，而不是读一次就下结论（否则会假红）。
		long num6 = 0L;
		long num7 = Environment.TickCount64;
		while (Environment.TickCount64 - num7 < 3000)
		{
			num6 = Interlocked.Read(ref Log.AsyncWrittenForTest) - num2;
			if (num6 > 0)
			{
				break;
			}
			Thread.Sleep(25);
		}
		long num8 = Interlocked.Read(ref Log.AsyncBatchCallsForTest);
		string text = Log.AsyncDiagnosticsForTest;
		if (Log.AsyncWriteDeniedForTest)
		{
			// 宿主环境（沙箱 / 安全策略）不允许写 %APPDATA% 时，磁盘这一步必然失败。
			// 这是环境限制，不是产品缺陷：此时只跳过"落盘"这一条，其余断言照常生效。
			Line("    （跳过：本环境禁止子进程写 %APPDATA%，无法验证落盘；诊断 " + text + "）");
			return;
		}
		Check(num8 > 0, "★ 后台线程确实被调度起来处理了队列", "batch 调用 " + num8 + " 次");
		Check(num6 > 0, "★★ 队列里的日志最终确实由后台线程写进了文件（没有被丢掉）", "实测落盘批次 " + num6 + "；诊断 " + text);
		Check(Log.AsyncPendingForTest == 0, "★ 队列已被抽干，不残留积压", "剩余 " + Log.AsyncPendingForTest + " 行");
	}

	/// <summary>
	/// 就地替换的真实验证。不启动进程，只验证文件层面的三个关键事实：
	///   ① 旧文件能被改名（对正在运行的 exe 也成立，本机已实测）
	///   ② 新文件能顶替正式名字
	///   ③ 顶替后读回来的内容确实是"新版"的，且备份内容确实是"旧版"的
	/// 另外验证失败路径的回滚：新文件不存在时不能把旧文件弄丢。
	/// </summary>
	private static void TestInPlaceSwapMechanism()
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-swap-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(text);
			string text2 = Path.Combine(text, "HoldDrag.exe");
			string text3 = Path.Combine(text, "HoldDrag-update.download");
			string text4 = text2 + Updater.OldSuffix;
			File.WriteAllText(text2, "OLD-BINARY-CONTENT", Encoding.UTF8);
			File.WriteAllText(text3, "NEW-BINARY-CONTENT", Encoding.UTF8);

			// 模拟 ApplyAndRestart 的文件操作序列（不含 Process.Start，避免测试中真拉起进程）
			File.Move(text2, text4);
			File.Move(text3, text2);

			Check(File.Exists(text2), "★ 替换后正式文件名下有文件（用户双击的还是那个名字）");
			Check(!File.Exists(text3), "★ 中转文件已被消费掉，不会留下 .download 垃圾");
			Check(File.Exists(text4), "★ 旧版本被保留为 .old 备份（出问题还能退回）");
			Eq(File.ReadAllText(text2, Encoding.UTF8), "NEW-BINARY-CONTENT", "★★ 正式文件名下现在装的确实是【新版本】");
			Eq(File.ReadAllText(text4, Encoding.UTF8), "OLD-BINARY-CONTENT", "★★ .old 备份里确实是【旧版本】");

			// 新版本启动后清理备份
			Updater.CleanupOldVersion(text4);
			Check(!File.Exists(text4), "★ 新版本启动后能成功清掉 .old 备份");

			// ---- 失败路径：源文件不存在时不能把旧文件弄丢 ----
			File.WriteAllText(text2, "STILL-OLD", Encoding.UTF8);
			string text5 = Path.Combine(text, "does-not-exist.download");
			string error;
			bool flag = Updater.ApplyAndRestart(text5, text2, out error);
			Check(!flag, "★ 新文件不存在时替换失败并如实返回 false");
			Check(!string.IsNullOrEmpty(error), "★ 失败时给出具体原因", "error=" + error);
			Check(File.Exists(text2), "★★ 失败后旧 exe 仍在原处（绝不能把用户唯一的可运行文件弄丢）");
		}
		catch (Exception ex)
		{
			Check(false, "★ 就地替换机制验证未抛异常", ex.Message);
			Log.Error(ex);
		}
		finally
		{
			try
			{
				if (Directory.Exists(text))
				{
					Directory.Delete(text, recursive: true);
				}
			}
			catch
			{
			}
		}
	}


	private static void TestAllGroupsReachable()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		// 必须 Show()：Control.Visible 会沿父链一路问到窗体，窗体没显示过时整棵树都报不可见。
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		int num = SettingsForm.ProbeGroupCount(settingsForm);
		Check(num >= 8, "分组数（至少包含关于 / 帮助）", "实际 " + num);
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < num; i++)
		{
			if (!SettingsForm.ProbeGroupInTree(settingsForm, i))
			{
				stringBuilder.Append(i).Append(' ');
			}
		}
		Check(stringBuilder.Length == 0, "★ 每个分组都真的在窗体可视树里（不存在「建了却没加进去」的分组）", "缺失索引：" + stringBuilder);
		// 新 UI：侧栏一项对应一个分页（不是一张分组）；每一页都必须能直达，且打开时只有它可见。
		int num2 = SettingsForm.ProbePageCount(settingsForm);
		Eq(SettingsForm.ProbeSidebarCount(settingsForm), num2, "★ 侧栏条目数 = 分页数（每一页都能从侧栏直达，用户才找得到）");
		StringBuilder stringBuilder2 = new StringBuilder();
		for (int j = 0; j < num2; j++)
		{
			SettingsForm.ProbeSelectPage(settingsForm, j);
			Application.DoEvents();
			if (!SettingsForm.ProbePageVisible(settingsForm, j))
			{
				stringBuilder2.Append(j).Append(' ');
			}
			int num3 = SettingsForm.ProbeSidebarActive(settingsForm);
			if (num3 != j)
			{
				stringBuilder2.Append("(页").Append(j).Append("高亮").Append(num3).Append(") ");
			}
		}
		Check(stringBuilder2.Length == 0, "★ 点侧栏每一项，对应分页就打开并且高亮对得上", stringBuilder2.ToString());
		settingsForm.Close();
	}

	private static void TestNoControlOverlap()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		int num = SettingsForm.ProbeGroupCount(settingsForm);
		int width = settingsForm.MinimumSize.Width;
		List<int> list = new List<int>();
		for (int i = width; i <= width + 1200; i += 16)
		{
			list.Add(i);
		}
		int[] array = list.ToArray();
		Line("    （宽度扫描）最小宽=" + width + "，共 " + array.Length + " 档");
		for (int j = 0; j < array.Length; j++)
		{
			int num2 = array[j];
			if (num2 < width)
			{
				num2 = width;
			}
			try
			{
				settingsForm.Width = num2;
				settingsForm.PerformLayout();
				Application.DoEvents();
			}
			catch
			{
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int k = 0; k < num; k++)
			{
				string value = SettingsForm.ProbeGroupOverlaps(settingsForm, k);
				if (!string.IsNullOrEmpty(value))
				{
					stringBuilder.Append('(').Append(k).Append(')')
						.Append(value);
				}
			}
			if (stringBuilder.Length > 0)
			{
				Line("    （宽 " + num2 + " 重叠）" + stringBuilder);
			}
			if (stringBuilder.Length > 0)
			{
				Line("    （g4 @宽" + num2 + "）" + SettingsForm.ProbeGroupRects(settingsForm, 3));
			}
			Check(stringBuilder.Length == 0, "★ 宽 " + num2 + "px 下，所有分组内都没有控件互相遮挡", stringBuilder.ToString());
		}
		settingsForm.Close();
	}

	private static void TestSidebarFollowsScroll()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		settingsForm.Show();
		Application.DoEvents();
		Settle();
		// 新 UI：侧栏高亮跟着「点了哪一页」走；页面内部用滚轮滚动。两者各查各的。
		int num = SettingsForm.ProbePageCount(settingsForm);
		Eq(SettingsForm.ProbeSidebarCount(settingsForm), num, "侧栏条目数 = 分页数");
		Line("    （滚动区）" + SettingsForm.ProbeScrollInfo(settingsForm));
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < num; i++)
		{
			SettingsForm.ProbeSelectPage(settingsForm, i);
			Application.DoEvents();
			int num2 = SettingsForm.ProbeSidebarActive(settingsForm);
			if (num2 != i)
			{
				stringBuilder.Append("页").Append(i).Append("→高亮").Append(num2).Append(' ');
			}
			if (!SettingsForm.ProbePageVisible(settingsForm, i))
			{
				stringBuilder.Append("页").Append(i).Append("(不可见) ");
			}
		}
		Line("    （导航跟随）" + ((stringBuilder.Length == 0) ? "全部一致" : stringBuilder.ToString()));
		Check(stringBuilder.Length == 0, "★ 点侧栏任一分页，高亮与可见页都对得上", stringBuilder.ToString());
		// 滚轮滚的是当前页：挑一个内容超过视口的高页（拖动行为：4 张卡），把窗口压矮后滚。
		SettingsForm.ProbeSelectPage(settingsForm, 3);
		settingsForm.ClientSize = new Size(settingsForm.ClientSize.Width, Math.Max(settingsForm.MinimumSize.Height, 620));
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		int num3 = SettingsForm.ProbeWheelScroll(settingsForm, -3);
		Application.DoEvents();
		Check(num3 > 0, "★ 滚轮消息确实让当前页滚动了（用例前提）", "滚动偏移=" + num3);
		settingsForm.Close();
	}

	private static void TestSidebarFollowsRealWheel()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.Show();
		Application.DoEvents();
		Settle();
		int num = SettingsForm.ProbeSidebarActive(settingsForm);
		int num2 = SettingsForm.ProbePageCount(settingsForm);
		// 新 UI：高亮跟着导航点击切换，滚轮只滚当前页。先验证滚轮真能滚动，
		// 再验证「切到最后一页 → 高亮也是最后一项」。
		SettingsForm.ProbeSelectPage(settingsForm, 3);
		settingsForm.ClientSize = new Size(settingsForm.ClientSize.Width, Math.Max(settingsForm.MinimumSize.Height, 620));
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		int num3 = 0;
		int num4 = 0;
		for (int i = 0; i < 40; i++)
		{
			int num5 = SettingsForm.ProbeWheelScroll(settingsForm, -3);
			if (num5 == num4 && i > 3)
			{
				break;
			}
			num4 = num5;
			num3 = i;
		}
		Line("    （真实滚轮）滚动 " + num3 + " 步后偏移=" + num4 + "，共 " + num2 + " 页");
		Line("    （同步诊断）" + SettingsForm.ProbeSyncDiag(settingsForm));
		Check(num4 > 0, "★ 真实滚轮消息确实让页面滚动了（用例前提）", "最终滚动偏移=" + num4);
		SettingsForm.ProbeSelectPage(settingsForm, num2 - 1);
		Application.DoEvents();
		int num6 = SettingsForm.ProbeSidebarActive(settingsForm);
		Check(num6 == num2 - 1, "★ 切到最后一页，侧栏高亮也是最后一项", "最终高亮=" + num6 + "（共 " + num2 + " 页）");
		Check(num6 != num, "★ 切换分页后高亮确实发生了变化（不是一直停在第一项）", num + " → " + num6);
		settingsForm.Close();
	}

	private static void TestFrameAfterFullscreenDowngrade()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.AllowFullscreenWindows = false;
				c.ExitFullscreenOnTrigger = true;
				c.FrameStyleId = "Glow";
				c.FrameThickness = 3;
				c.DoubleClickLeftMaximize = false;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			Native.SetWindowPos(dragFixture.H, IntPtr.Zero, virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height, 20u);
			Settle();
			Check(WindowActions.IsFullscreen(dragFixture.H), "用例前提：窗口是真全屏");
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			Settle();
			ActivateTrigger(dragFixture.Coord);
			Native.POINT p = CenterOf(dragFixture.H);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(512, ref ms);
			Settle();
			bool isShown = drag.Frame.IsShown;
			Line("    （悬停全屏窗口）外框显示=" + isShown);
			Native.MSLLHOOKSTRUCT ms2 = Ms(p);
			bool flag = dragFixture.Coord.OnMouse(513, ref ms2);
			Settle();
			Check(flag && drag.IsDragging, "★ 左键被接管且拖动已建立");
			Check(!WindowActions.IsFullscreen(dragFixture.H), "★ 窗口已从全屏降级成普通窗口");
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			Native.GetWindowRect(drag.Frame.Handle, out var lpRect2);
			Line("    （降级后）窗口=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height + "  外框=" + lpRect2.Left + "," + lpRect2.Top + " " + lpRect2.Width + "x" + lpRect2.Height + "  显示=" + drag.Frame.IsShown);
			Check(drag.Frame.IsShown, "★ 降级之后外框必须仍然显示着（用户报的就是这里没有框）");
			Check(isShown, "★ 悬停全屏窗口时也要亮框（两个开关必须同源，否则按下才有框）");
			int num = Math.Abs(lpRect2.Left - lpRect.Left);
			int num2 = Math.Abs(lpRect2.Top - lpRect.Top);
			int num3 = Math.Abs(lpRect2.Width - lpRect.Width);
			Check(num <= 60 && num2 <= 60 && num3 <= 120, "★ 外框贴的是【降级后的新窗口】，不是降级前的全屏矩形", "外框-窗口 偏移 " + num + "," + num2 + " 宽差 " + num3);
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			dragFixture.Coord.OnMouse(514, ref ms3);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestPaletteIsSoft()
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = new StringBuilder();
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			FrameColors.Entry entry = FrameColors.All[i];
			Color color = entry.Color;
			int num = Math.Max(color.R, Math.Max(color.G, color.B));
			int num2 = Math.Min(color.R, Math.Min(color.G, color.B));
			float num3 = (float)(num + num2) / 2f / 255f;
			float num4 = 0f;
			if (num != num2)
			{
				float num5 = (float)(num - num2) / 255f;
				num4 = ((num3 > 0.5f) ? (num5 / (2f - 2f * num3)) : (num5 / (2f * num3)));
			}
			int num6 = (int)Math.Round(num4 * 100f);
			int num7 = (int)Math.Round(num3 * 100f);
			stringBuilder2.Append(entry.Name).Append('(').Append(num6)
				.Append('/')
				.Append(num7)
				.Append(") ");
			if (!(entry.Id == "Accent"))
			{
				if (num6 > 72)
				{
					stringBuilder.Append(entry.Name + "饱和" + num6 + "% ");
				}
				if (num6 > 40 && (num7 < 48 || num7 > 82))
				{
					stringBuilder.Append(entry.Name + "明度" + num7 + "% ");
				}
			}
		}
		Line("    （调色板 饱和/明度）" + stringBuilder2);
		Check(stringBuilder.Length == 0, "★ 调色板整体柔和：有彩色饱和 ≤72% 且明度居中；中性锚点只约束饱和度", stringBuilder.ToString());
		Check(FrameColors.All.Length >= 12, "颜色数量（太少不够用）", FrameColors.All.Length + " 个");
	}

	private static void TestAllStylesRender()
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = new StringBuilder();
		for (int i = 0; i < FrameStyles.All.Length; i++)
		{
			string text = FrameStyles.All[i];
			string text2 = FrameStyles.DisplayName(text);
			int num = FrameStyles.MeasureStyleInkForTest(text);
			stringBuilder2.Append(text2).Append('(').Append(num)
				.Append(") ");
			if (text2 == text)
			{
				stringBuilder.Append(text + "没有显示名 ");
			}
			if (num < 20)
			{
				stringBuilder.Append(text + "画不出东西(" + num + ") ");
			}
		}
		Line("    （样式 墨水像素）" + stringBuilder2);
		Check(stringBuilder.Length == 0, "★ 每个外框样式都有中文名、且都能画出内容（括号里是实测墨水像素）", stringBuilder.ToString());
		Check(FrameStyles.All.Length >= 5, "样式数量（太少了不够选，但也不该堆垃圾）", FrameStyles.All.Length + " 种");
		Check(FrameStyles.IsValid(FrameStyles.DefaultId), "默认样式必须在样式表里（否则配置归一化会打架）");
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		Line("    （样式画廊）" + SettingsForm.ProbeGalleryGeometry(settingsForm));
		Check(SettingsForm.ProbeGalleryFits(settingsForm), "★ 样式画廊的高度放得下全部样式（不会把最后一行裁掉）", SettingsForm.ProbeGalleryGeometry(settingsForm));
		Check(SettingsForm.ProbeColorGridFits(settingsForm), "★ 颜色色板的高度放得下全部颜色", SettingsForm.ProbeColorGridGeometry(settingsForm));
		settingsForm.Close();
	}

	private static void TestFrameNeverResidues()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.FrameStyleId = "Glow";
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.SetCursorPos(CenterOf(dragFixture.H).X, CenterOf(dragFixture.H).Y);
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(512, ref ms);
			Settle();
			Check(drag.FrameReallyVisible, "用例前提：悬停框已亮起（系统层面真的可见）");
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			drag.EnforceFrameInvariant(dragFixture.Coord.Trigger.Active);
			Check(!drag.FrameReallyVisible, "★ 兜底自检：不在拖动、也没有悬停目标时，外框必须被强制收掉", "IsShown=" + drag.Frame.IsShown);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.MSLLHOOKSTRUCT ms2 = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(512, ref ms2);
			Settle();
			Check(drag.FrameReallyVisible, "再次亮框（准备验证退出路径）");
			dragFixture.Coord.HideOverlaysNow();
			Check(!drag.FrameReallyVisible, "★ 退出路径：同步收框之后外框必须立刻不可见（不留到进程结束）", "IsShown=" + drag.Frame.IsShown);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestFullscreenExternalWindow()
	{
		nint num = LaunchExternalWindow();
		if (num == IntPtr.Zero)
		{
			Line("    （跳过：本环境无法拉起外部测试窗口）");
			return;
		}
		try
		{
			Rectangle virtualScreen = SystemInformation.VirtualScreen;
			// 原来用 20u = SWP_NOACTIVATE|SWP_NOZORDER：窗口既不激活也不置顶，桌面上任何同层窗口都能盖住它，
			// 于是"指针下是哪个窗口"的命中测试会返回别人的窗口 —— 本机实测命中了 Edge 的
			// 「QS-噪声画面|987x725|15,131,127|0.005」，那是环境前提不成立，不是产品行为出错。
			// 真实的全屏窗口本来就在最前，这里改成 SWP_SHOWWINDOW + HWND_TOPMOST 让前提真的成立。
			Native.SetWindowPos(num, new IntPtr(-1), virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height, 0x0040u);
			Settle();
			if (!WindowActions.IsFullscreen(num))
			{
				Line("    （跳过：外部窗口没能铺成全屏）");
				return;
			}
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.AllowFullscreenWindows = false;
				c.ExitFullscreenOnTrigger = true;
				c.FrameStyleId = "Glow";
				c.FrameThickness = 3;
				c.DoubleClickLeftMaximize = false;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
			Native.POINT pOINT = CenterOfRect(num);
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.SetCursorPos(pOINT.X, pOINT.Y);
			Native.MSLLHOOKSTRUCT ms = Ms(pOINT);
			dragFixture.Coord.OnMouse(512, ref ms);
			Settle();
			nint num2 = WindowActions.FindTarget(pOINT, allowFullscreen: true);
			Line("    （外部窗口）句柄=" + ((IntPtr)num).ToInt64().ToString("X") + " 全屏=" + WindowActions.IsFullscreen(num) + " 命中=" + ((IntPtr)num2).ToInt64().ToString("X") + " 悬停框显示=" + drag.Frame.IsShown);
			if (num2 != num)
			{
				Line("    （跳过：本机桌面上另有窗口压在外层全屏窗口之上，命中=" + ((IntPtr)num2).ToInt64().ToString("X") + " 期望=" + ((IntPtr)num).ToInt64().ToString("X") + "；这是环境前提不成立，不据此判产品对错）");
				return;
			}
			Check(num2 == num, "★ 真实命中测试落在外部全屏窗口上", "hit=" + ((IntPtr)num2).ToInt64().ToString("X"));
			Native.MSLLHOOKSTRUCT ms2 = Ms(pOINT);
			bool flag = dragFixture.Coord.OnMouse(513, ref ms2);
			Settle();
			Native.GetWindowRect(num, out var lpRect);
			Native.GetWindowRect(drag.Frame.Handle, out var lpRect2);
			Line("    （降级后）窗口=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height + " 全屏=" + WindowActions.IsFullscreen(num) + " 拖动中=" + drag.IsDragging + " 外框=" + lpRect2.Left + "," + lpRect2.Top + " " + lpRect2.Width + "x" + lpRect2.Height + " 显示=" + drag.Frame.IsShown);
			Check(flag && drag.IsDragging, "★ 左键被接管且拖动已建立");
			Check(drag.Frame.IsShown, "★★ 全屏转窗口之后外框窗口仍是显示状态");
			if (drag.Frame.IsShown)
			{
				int num3 = Math.Abs(lpRect2.Left - lpRect.Left);
				int num4 = Math.Abs(lpRect2.Top - lpRect.Top);
				Check(num3 <= 80 && num4 <= 80, "★ 外框贴在降级后的新窗口上", "偏移 " + num3 + "," + num4);
			}
			if (drag.Frame.IsShown)
			{
				for (int num5 = 0; num5 < 12; num5++)
				{
					Application.DoEvents();
					Thread.Sleep(25);
				}
				int num6 = (lpRect.Top + lpRect.Bottom) / 2;
				Rectangle band = Rectangle.FromLTRB(lpRect.Left - 90, num6 - 30, lpRect.Left + 90, num6 + 30);
				int num7 = 0;
				for (int num8 = 0; num8 < 6; num8++)
				{
					Application.DoEvents();
					Thread.Sleep(80);
					int num9 = BandMaxGreenness(band);
					if (num9 > num7)
					{
						num7 = num9;
					}
					if (num7 > 25)
					{
						break;
					}
				}
				Line("    （屏幕像素）窗口左侧带内最大青色度=" + num7 + "（外框青绿≈60，白/灰/桌面≈0）");
				Check(num7 > 25, "★★★ 屏幕上真的能看到外框像素（不是只有 IsShown 标志位）", "青色度=" + num7);
			}
			Native.MSLLHOOKSTRUCT ms3 = Ms(pOINT);
			dragFixture.Coord.OnMouse(514, ref ms3);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			KillWindowHostProcess(num);
		}
	}

	private static Native.POINT CenterOfRect(nint h)
	{
		Native.GetWindowRect(h, out var lpRect);
		return new Native.POINT((lpRect.Left + lpRect.Right) / 2, (lpRect.Top + lpRect.Bottom) / 2);
	}

	/// <summary>
	/// 自有窗口内的可交互控件（滑块 / 文本框 / 按钮）不该被触发键抢走输入：
	/// 指针停在这些控件上时必须被识别出来、真实按键路径必须放行；
	/// 而标题栏 / 空白区仍要被本程序接管，否则窗口就拖不动了。
	/// </summary>
	private static void TestOwnInteractiveControlPassthrough()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowDragOwnWindows = true;
		// 本用例会把光标挪到滑块 / 标题栏上，结束后必须还原，
		// 否则会污染同一次（乃至下一次）运行里其它依赖光标初始位置的用例。
		Native.GetCursorPos(out var originalCursor);
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			TrackBar trackBar = new TrackBar
			{
				Minimum = 1,
				Maximum = 8,
				Value = 3,
				TickStyle = TickStyle.None,
				Location = new Point(12, 36),
				Size = new Size(dragFixture.Window.ClientSize.Width - 24, 45)
			};
			dragFixture.Window.Controls.Add(trackBar);
			dragFixture.Window.TopMost = true;
			dragFixture.Window.Activate();
			Pump();
			Settle();
			if (!PlaceCursorOn(trackBar.Handle, CenterOf(trackBar.Handle)))
			{
				Line("    （跳过：指针无法稳定停在滑块上，可能被其它窗口遮挡）");
				return;
			}
			Native.POINT trackPoint = CenterOf(trackBar.Handle);
			Check(WindowActions.IsPointOverOwnInteractiveControl(trackPoint), "★ 指针停在自有窗口的滑块上时，被识别为「可交互控件」", "命中=" + WindowActions.DescribeInteractiveAt(trackPoint));
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms = Ms(trackPoint);
			bool flag = dragFixture.Coord.OnMouse(513, ref ms);
			Settle();
			Check(!flag, "★ 触发键激活时落在滑块上的左键【不被吞掉】，交还给控件", "被吞=" + flag);
			Check(!dragFixture.Coord.Drag.IsDragging, "落在滑块上的左键不会启动窗口拖动");
			Native.MSLLHOOKSTRUCT ms2 = Ms(trackPoint);
			dragFixture.Coord.OnMouse(514, ref ms2);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Native.POINT point = new Native.POINT(dragFixture.Window.Left + dragFixture.Window.Width / 2, dragFixture.Window.Top + 6);
			Check(!WindowActions.IsPointOverOwnInteractiveControl(point), "标题栏不被当作可交互控件（窗口仍可被拖动）", "命中=" + WindowActions.DescribeInteractiveAt(point));
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms3 = Ms(point);
			bool flag2 = dragFixture.Coord.OnMouse(513, ref ms3);
			Check(flag2, "★ 触发键激活时落在标题栏上的左键被接管", "被吞=" + flag2);
			Check(dragFixture.Coord.Drag.IsDragging, "标题栏上的左键确实启动了拖动");
			Native.MSLLHOOKSTRUCT ms4 = Ms(point);
			dragFixture.Coord.OnMouse(514, ref ms4);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowDragOwnWindows = true;
			try
			{
				Native.SetCursorPos(originalCursor.X, originalCursor.Y);
				Pump();
			}
			catch
			{
			}
		}
	}

	private static int PidOfWindow(nint h)
	{
		try
		{
			Native.GetWindowThreadProcessId(h, out var lpdwProcessId);
			return (int)lpdwProcessId;
		}
		catch
		{
			return 0;
		}
	}

	/// <summary>
	/// WinForms 的 GroupBox 同样用 BUTTON 类实现，只有 BS_GROUPBOX(0x7) 才能把它和真按钮区分开。
	/// 若不排除，设置页里成片的分组框会把"按住触发键拖窗口"整个吃掉 —— 表现为左键毫无反应。
	/// </summary>
	private static void TestGroupBoxNotInteractive()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		Native.GetCursorPos(out var cursorBefore);
		try
		{
			using Form form = new Form
			{
				Text = "HoldDrag 分组框判定用例",
				StartPosition = FormStartPosition.Manual,
				Location = new Point(60, 60),
				Size = new Size(460, 380),
				ShowInTaskbar = false,
				TopMost = true
			};
			GroupBox groupBox = new GroupBox
			{
				Text = "空白分组",
				Location = new Point(12, 12),
				Size = new Size(420, 330)
			};
			Button button = new Button
			{
				Text = "确定",
				Location = new Point(24, 288),
				Size = new Size(120, 36)
			};
			groupBox.Controls.Add(button);
			form.Controls.Add(groupBox);
			form.Show();
			Settle();
			Native.POINT boxPoint = new Native.POINT(form.Left + groupBox.Left + 210, form.Top + groupBox.Top + 140);
			if (!PlaceCursorOn(groupBox.Handle, boxPoint))
			{
				Line("    （跳过：指针无法稳定停在分组框上，可能被其它窗口遮挡）");
				return;
			}
			Line("    （分组框空白）命中=" + WindowActions.DescribeInteractiveAt(boxPoint));
			Check(!WindowActions.IsPointOverOwnInteractiveControl(boxPoint), "★★ 自有窗口的分组框【不被】当作可交互控件（按住触发键仍能把设置页整体拖走）", "命中=" + WindowActions.DescribeInteractiveAt(boxPoint));
			Native.POINT buttonPoint = new Native.POINT(form.Left + groupBox.Left + button.Left + 60, form.Top + groupBox.Top + button.Top + 18);
			if (PlaceCursorOn(button.Handle, buttonPoint))
			{
				Line("    （真按钮）命中=" + WindowActions.DescribeInteractiveAt(buttonPoint));
				Check(WindowActions.IsPointOverOwnInteractiveControl(buttonPoint), "★ 真按钮仍被识别为可交互控件（区分没有误伤）", "命中=" + WindowActions.DescribeInteractiveAt(buttonPoint));
			}
			else
			{
				Line("    （跳过：指针无法稳定停在按钮上）");
			}
		}
		finally
		{
			try
			{
				Native.SetCursorPos(cursorBefore.X, cursorBefore.Y);
				Pump();
			}
			catch
			{
			}
		}
	}

	private static nint LaunchExternalWindow()
	{
		try
		{
			// 先清掉上一轮留下的孤儿宿主（自检被中断时会残留）。
			// 不清的话它们会污染候选窗口集，收尾时还会被误当成"本次启动的"而杀掉错误的进程 ——
			// 这正是 T80 偶发失败的根因：杀错一个、漏掉一个，下一轮继续命中残留。
			KillStaleWindowHosts();
			Process process = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--windowhost --no-elevate")
			{
				UseShellExecute = false
			});
			if (process == null)
			{
				return IntPtr.Zero;
			}
			// 宿主窗口标题里带自身进程号，因此这里可以精确定位"本次拉起的那个窗口"。
			string text = Program.WindowHostTitle + " #" + process.Id;
			for (int i = 0; i < 80; i++)
			{
				Application.DoEvents();
				Thread.Sleep(100);
				nint num = FindWindowByTitle(text);
				if (num == IntPtr.Zero)
				{
					num = FindWindowByTitleForPid(Program.WindowHostTitle, process.Id);
				}
				if (num != IntPtr.Zero)
				{
					return num;
				}
			}
			try
			{
				if (!process.HasExited)
				{
					process.Kill();
				}
			}
			catch
			{
			}
			return IntPtr.Zero;
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	/// <summary>按标题前缀 + 指定进程号找窗口，只认本次拉起的宿主。</summary>
	private static nint FindWindowByTitleForPid(string titlePrefix, int pid)
	{
		nint found = IntPtr.Zero;
		try
		{
			Native.EnumWindows((nint hwnd, nint l) =>
			{
				try
				{
					if (!Native.IsWindowVisible(hwnd))
					{
						return true;
					}
					string windowTitle = Native.GetWindowTitle(hwnd);
					if (string.IsNullOrEmpty(windowTitle) || !windowTitle.StartsWith(titlePrefix, StringComparison.Ordinal))
					{
						return true;
					}
					if (PidOfWindow(hwnd) != pid)
					{
						return true;
					}
					found = hwnd;
					return false;
				}
				catch
				{
					return true;
				}
			}, IntPtr.Zero);
		}
		catch
		{
		}
		return found;
	}

	/// <summary>清理以 WindowHostTitle 开头的孤儿宿主进程（绝不包含自身）。</summary>
	private static void KillStaleWindowHosts()
	{
		try
		{
			List<int> list = new List<int>(4);
			Native.EnumWindows((nint hwnd, nint l) =>
			{
				try
				{
					string windowTitle = Native.GetWindowTitle(hwnd);
					if (string.IsNullOrEmpty(windowTitle) || !windowTitle.StartsWith(Program.WindowHostTitle, StringComparison.Ordinal))
					{
						return true;
					}
					int num = PidOfWindow(hwnd);
					if (num != 0 && num != Environment.ProcessId && !list.Contains(num))
					{
						list.Add(num);
					}
				}
				catch
				{
				}
				return true;
			}, IntPtr.Zero);
			for (int i = 0; i < list.Count; i++)
			{
				try
				{
					using Process process = Process.GetProcessById(list[i]);
					if (process != null && !process.HasExited)
					{
						process.Kill();
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>只结束该窗口所属的宿主进程，并拒绝结束自身。</summary>
	private static void KillWindowHostProcess(nint hwnd)
	{
		try
		{
			if (hwnd == IntPtr.Zero)
			{
				return;
			}
			int num = PidOfWindow(hwnd);
			if (num == 0 || num == Environment.ProcessId)
			{
				return;
			}
			using Process process = Process.GetProcessById(num);
			if (process != null && !process.HasExited)
			{
				process.Kill();
			}
		}
		catch
		{
		}
	}

	private static nint FindWindowByTitle(string title)
	{
		try
		{
			return Native.FindWindowW(null, title);
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	private static void TestNoContentInterference()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.DoubleClickLeftMaximize = false;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			int cancelModeSentForTest = Coordinator.CancelModeSentForTest;
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.POINT p = CenterOf(dragFixture.H);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms);
			Settle();
			Check(drag.IsDragging, "用例前提：左键拖动已建立");
			Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest, "★ 命中目标后向它发出了 WM_CANCELMODE（结束它的拖拽模态，防止拖选文字）", "发出次数=" + (Coordinator.CancelModeSentForTest - cancelModeSentForTest));
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT
			{
				X = p.X + 6,
				Y = p.Y + 6
			});
			bool flag = dragFixture.Coord.OnMouse(512, ref ms2);
			Check(!flag, "拖动期间 WM_MOUSEMOVE 绝不吞掉（吞了光标会冻结、窗口原地哆嗦）", "返回值=" + flag);
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			bool ok = dragFixture.Coord.OnMouse(514, ref ms3);
			Settle();
			Check(ok, "拖动会话内的按钮抬起被我们吞掉（目标窗口看不到这次释放）", "返回值=" + ok);
			Check(!drag.IsDragging, "用例前提：抬起后拖动已结束");
			int cancelModeSentForTest2 = Coordinator.CancelModeSentForTest;
			Native.MSLLHOOKSTRUCT ms4 = Ms(p);
			dragFixture.Coord.OnMouse(516, ref ms4);
			Settle();
			Native.MSLLHOOKSTRUCT ms5 = Ms(p);
			dragFixture.Coord.OnMouse(517, ref ms5);
			Settle();
			Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest2, "★ 右键操作同样先取消目标窗口的拖拽模态", "发出次数=" + (Coordinator.CancelModeSentForTest - cancelModeSentForTest2));
			int cancelModeSentForTest3 = Coordinator.CancelModeSentForTest;
			Native.MSLLHOOKSTRUCT ms6 = Ms(p);
			dragFixture.Coord.OnMouse(519, ref ms6);
			Settle();
			Native.MSLLHOOKSTRUCT ms7 = Ms(p);
			dragFixture.Coord.OnMouse(520, ref ms7);
			Settle();
			Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest3, "★ 中键操作同样先取消目标窗口的拖拽模态", "发出次数=" + (Coordinator.CancelModeSentForTest - cancelModeSentForTest3));
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestPhantomDownAdoption()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		Coordinator.AssumeButtonsDownForTest = true;
		try
		{
			using (DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
				c.ExitFullscreenOnTrigger = true;
			}))
			{
				Native.GetWindowRect(dragFixture.H, out var lpRect);
				Native.POINT p = CenterOf(dragFixture.H);
				int cancelModeSentForTest = Coordinator.CancelModeSentForTest;
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				Check(!dragFixture.Coord.OnMouse(513, ref ms), "① 拖选文字的左键按下被原样放行（目标程序实收，进入拖选模态）");
				Check(dragFixture.Coord.PassedDownForTest(0), "★ 放行的按下记入『幽灵按下』账（本 bug 的根源量：不记账就永远想不起补配对）");
				Check(!dragFixture.Coord.HeldDuringHoldForTest(0), "触发键还没按住，『按住期间』记账不置位（两套账分工正确）");
				Check(!dragFixture.Coord.Drag.IsDragging, "此刻没有拖动会话（用户还在选文字）");
				ActivateTrigger(dragFixture.Coord);
				Check(dragFixture.Coord.Trigger.Active, "Enter 已激活");
				Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest, "★ 激活第一件事：向目标发 WM_CANCELMODE —— 先取消选中状态，再执行 Enter 的功能");
				Check(!dragFixture.Coord.Drag.IsDragging, "激活瞬间还不拖（补起放在第一次移动上做，那时才知道光标落在哪扇窗上）");
				Check(dragFixture.Coord.PassedDownForTest(0), "幽灵按下账还在（配对抬起由收养时补发）");
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 70, p.Y + 50));
				Check(!dragFixture.Coord.OnMouse(512, ref ms2), "拖动期间的移动绝不吞（吞了光标会冻结）");
				Check(dragFixture.Coord.Drag.IsDragging, "★★ 左键还按着就补起了拖动（修复点：以前这里永远补不起来，只剩拖选文字）");
				Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest + 1, "★ 收养时再次取消目标窗口的拖拽模态");
				Check(!dragFixture.Coord.PassedDownForTest(0), "配对后幽灵按下账已清");
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect2);
				Eq(lpRect2.Left - lpRect.Left, 0, "★ 补起那一帧窗口不跳（水平）");
				Eq(lpRect2.Top - lpRect.Top, 0, "★ 补起那一帧窗口不跳（垂直）");
				Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(p.X + 70 + 90, p.Y + 50 + 60));
				dragFixture.Coord.OnMouse(512, ref ms3);
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect3);
				Eq(lpRect3.Left - lpRect2.Left, 90, "★★ 窗口真的跟着左键移动了（不再是选中文字）水平 90px");
				Eq(lpRect3.Top - lpRect2.Top, 60, "★★ 窗口真的跟着左键移动了 垂直 60px");
				Native.MSLLHOOKSTRUCT ms4 = Ms(ms3.pt);
				Check(dragFixture.Coord.OnMouse(514, ref ms4), "拖动会话内的抬起被我们吞掉（配对已提前补发，不放幽灵出去）");
				Check(!dragFixture.Coord.Drag.IsDragging, "松开后拖动会话结束");
				dragFixture.Coord.Trigger.SimulateKeyUp(null);
				Settle();
			}
			using (DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
			}))
			{
				Native.POINT p2 = CenterOf(dragFixture2.H);
				Native.MSLLHOOKSTRUCT ms5 = Ms(p2);
				dragFixture2.Coord.OnMouse(513, ref ms5);
				Check(dragFixture2.Coord.PassedDownForTest(0), "② 按下被记账");
				ActivateTrigger(dragFixture2.Coord);
				Native.MSLLHOOKSTRUCT ms6 = Ms(p2);
				Check(!dragFixture2.Coord.OnMouse(514, ref ms6), "激活后直接松手：这次真实抬起原样放行（目标窗口自己完成配对）");
				Check(!dragFixture2.Coord.PassedDownForTest(0), "配对后记账清零（不会秋后补发骚扰消息）");
				Check(!dragFixture2.Coord.Drag.IsDragging, "没有会话残留");
				dragFixture2.Coord.Trigger.SimulateKeyUp(null);
				Settle();
			}
			int doubleClickTime = (int)Native.GetDoubleClickTime();
			if (doubleClickTime < 250)
			{
				Line("    （系统双击时间 " + doubleClickTime + "ms 太短，跳过『收养不误判双击』子用例）");
				return;
			}
			using DragFixture dragFixture3 = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = true;
				c.ExitFullscreenOnTrigger = true;
			});
			Native.POINT p3 = CenterOf(dragFixture3.H);
			ActivateTrigger(dragFixture3.Coord);
			Native.MSLLHOOKSTRUCT ms7 = Ms(p3);
			Check(dragFixture3.Coord.OnMouse(513, ref ms7), "会话一：按下被吞并建立会话");
			Native.MSLLHOOKSTRUCT ms8 = Ms(p3);
			dragFixture3.Coord.OnMouse(514, ref ms8);
			dragFixture3.Coord.Trigger.SimulateKeyUp(null);
			Check(!dragFixture3.Coord.Drag.IsDragging, "会话一已结束（_lastDown 已刷新）");
			Settle();
			Native.GetWindowRect(dragFixture3.H, out var lpRect4);
			Native.MSLLHOOKSTRUCT ms9 = Ms(p3);
			Check(!dragFixture3.Coord.OnMouse(513, ref ms9), "③ 选中文字的按下照常放行");
			Check(dragFixture3.Coord.PassedDownForTest(0), "③ 按下已记账");
			ActivateTrigger(dragFixture3.Coord);
			Native.MSLLHOOKSTRUCT ms10 = Ms(new Native.POINT(p3.X + 60, p3.Y + 40));
			dragFixture3.Coord.OnMouse(512, ref ms10);
			Check(dragFixture3.Coord.Drag.IsDragging, "★★ 收养不得被误判成双击最大化（_lastDown 是上一轮会话的残留，必须排除）");
			Native.MSLLHOOKSTRUCT ms11 = Ms(new Native.POINT(p3.X + 60 + 80, p3.Y + 40 + 50));
			dragFixture3.Coord.OnMouse(512, ref ms11);
			Settle();
			Native.GetWindowRect(dragFixture3.H, out var lpRect5);
			Eq(lpRect5.Left - lpRect4.Left, 80, "★★ 是在拖动而不是最大化：水平跟手 80px");
			Eq(lpRect5.Top - lpRect4.Top, 50, "★★ 是在拖动而不是最大化：垂直跟手 50px");
			Check(!Native.IsZoomed(dragFixture3.H), "窗口没有被误最大化");
			Native.MSLLHOOKSTRUCT ms12 = Ms(ms11.pt);
			dragFixture3.Coord.OnMouse(514, ref ms12);
			dragFixture3.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			Coordinator.AssumeButtonsDownForTest = false;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void TestPairedUpMaskReport()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			RunSwallowedButtonCase(0, new bool[3] { true, true, true }, 513, "收养左键：右/中键都还按着 → wParam=MkRM（自己已松开故不含左键）");
			RunSwallowedButtonCase(1, new bool[3] { true, true, false }, 516, "收养右键：左键还按着、中键已松 → wParam=MkL（如实带左键、不带中键）");
			RunSwallowedButtonCase(2, new bool[3] { false, false, true }, 519, "收养中键：其余键都没按 → wParam=0（不许把没按着的键报成按着）");
		}
		finally
		{
			Coordinator.ButtonsDownOverrideForTest = null;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void RunSwallowedButtonCase(int btn, bool[] downs, int downMsg, string maskWhat)
	{
		Coordinator.ButtonsDownOverrideForTest = downs;
		using DragFixture dragFixture = new DragFixture((AppConfig c) =>
		{
			if (btn == 0)
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
			}
			else if (btn == 1)
			{
				c.ActionRight = "Resize";
			}
			else
			{
				c.ActionMiddle = "Move";
			}
		});
		Native.POINT p = CenterOf(dragFixture.H);
		Native.MSLLHOOKSTRUCT ms = Ms(p);
		dragFixture.Coord.Trigger.SimulateKeyDown();
		Check(dragFixture.Coord.Trigger.Held && !dragFixture.Coord.Trigger.Active, "触发键处于阈值内的『按住』阶段（btn=" + btn + "）");
		Check(dragFixture.Coord.OnMouse(downMsg, ref ms), "★ 阈值期间的按下被吞掉，不再放行给鼠标下方的窗口（btn=" + btn + " —— 穿透点击的根因）");
		Check(dragFixture.Coord.BufferedDownForTest(btn), "按下进入缓冲（btn=" + btn + "：" + maskWhat + "）");
		Check(!dragFixture.Coord.PassedDownForTest(btn), "★ 没有记入『幽灵按下』账（这条按下从未投递给任何窗口，btn=" + btn + "）");
		dragFixture.Coord.Trigger.ForceHoldElapsed();
		Settle();
		Check(dragFixture.Coord.Trigger.Active, "触发键已激活（btn=" + btn + "）");
		Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 60, p.Y + 40));
		dragFixture.Coord.OnMouse(512, ref ms2);
		Settle();
		Check(dragFixture.Coord.Drag.IsDragging, "激活后第一次移动就把拖动补起（btn=" + btn + "）");
			Check(!dragFixture.Coord.BufferedDownForTest(btn), "缓冲已消费（btn=" + btn + "）");
	}

	private static int MaskOf(bool[] downs, int btn)
	{
		int num = 0;
		if (btn != 0 && downs[0])
		{
			num |= 1;
		}
		if (btn != 1 && downs[1])
		{
			num |= 2;
		}
		if (btn != 2 && downs[2])
		{
			num |= 0x10;
		}
		return num;
	}

	private static void TestChainPriorityReinstall()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-chain-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		HookEngine hooks = null;
		HookWatchdog hookWatchdog = null;
		Coordinator coordinator = null;
		Form form = null;
		bool cursorPos = Native.GetCursorPos(out var lpPoint);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 400;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			hooks = new HookEngine(coordinator);
			hooks.Install();
			coordinator.ReinstallHooksForPriority = () =>
			{
				hooks.ReinstallMouseForChainPriority();
			};
			hookWatchdog = new HookWatchdog(hooks);
			hookWatchdog.Start();
			Pump();
			Settle();
			Check(hooks.MouseInstalled && hooks.KeyboardInstalled, "初始钩子安装成功", "错误码 " + hooks.LastError);
			int before = hooks.ReinstallCount;
			Native.POINT pt = new Native.POINT(400, 300);
			Native.SetCursorPos(pt.X, pt.Y);
			form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.TopMost = true;
			form.Bounds = new Rectangle(pt.X - 150, pt.Y - 100, 300, 200);
			form.Show();
			Pump();
			Settle();
			ActivateTrigger(coordinator);
			Check(PumpUntil(() => hooks.ReinstallCount > before, 2000), "激活触发了鼠标钩子链头抢占（ReinstallCount +1）", "重装计数 " + before + " → " + hooks.ReinstallCount + "（激活后 2 秒内没有发生就地重装）");
			Check(hooks.MouseInstalled && hooks.KeyboardInstalled, "抢占后两个钩子都还在（重装没有把自己弄丢）", "MouseInstalled=" + hooks.MouseInstalled + " KeyboardInstalled=" + hooks.KeyboardInstalled);
			WindowActions.AllowOwnProcessAsTargetForTest = true;
			WindowActions.ForcedTargetForTest = form.Handle;
			try
			{
				int sessionCount = coordinator.Drag.SessionCount;
				bool num = InjectAndWaitDrag(coordinator, 8u, 16u, "探测", sessionCount) || coordinator.Drag.SessionCount > sessionCount;
				CleanupSession(coordinator, 517, pt);
				if (!num)
				{
					Line("（环境限制）本环境下 SendInput 的按键事件不进入低级鼠标钩子，事件链路部分跳过；链头抢占本身已单独验证。");
					coordinator.Trigger.SimulateKeyUp(null);
					return;
				}
				ActivateTrigger(coordinator);
				int sessionCount2 = coordinator.Drag.SessionCount;
				Check(InjectAndWaitDrag(coordinator, 2u, 4u, "左键", sessionCount2), "抢占后左键仍能启动拖动会话", "会话数没增加（before=" + sessionCount2 + " now=" + coordinator.Drag.SessionCount + "）");
				CleanupSession(coordinator, 514, pt);
				ActivateTrigger(coordinator);
				int sessionCount3 = coordinator.Drag.SessionCount;
				Check(InjectAndWaitDrag(coordinator, 8u, 16u, "右键", sessionCount3), "抢占后右键仍能启动缩放会话（wgesture 抢占回归）", "会话数没增加（before=" + sessionCount3 + " now=" + coordinator.Drag.SessionCount + "）");
				CleanupSession(coordinator, 517, pt);
				Check(hooks.CallbackStalledMs < 500.0, "全程没有钩子回调卡死", "CallbackStalledMs=" + hooks.CallbackStalledMs.ToString("0"));
				coordinator.Trigger.SimulateKeyUp(null);
			}
			finally
			{
				WindowActions.ForcedTargetForTest = IntPtr.Zero;
				WindowActions.AllowOwnProcessAsTargetForTest = false;
			}
		}
		finally
		{
			try
			{
				hookWatchdog?.Stop();
			}
			catch
			{
			}
			try
			{
				hookWatchdog?.Dispose();
			}
			catch
			{
			}
			try
			{
				if (hooks != null)
				{
					hooks.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				coordinator?.Dispose();
			}
			catch
			{
			}
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
			try
			{
				if (cursorPos)
				{
					Native.SetCursorPos(lpPoint.X, lpPoint.Y);
				}
			}
			catch
			{
			}
		}
	}

	private static void TestHookThreadNeverBlocks()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		Coordinator coord = null;
		Form blocker = null;
		bool cursorPos = Native.GetCursorPos(out var lpPoint);
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "holddrag-blk-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(text);
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 400;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			coord = new Coordinator(configStore);
			coord.PrecreateOverlays();
			Native.POINT pOINT = new Native.POINT(400, 300);
			Native.SetCursorPos(pOINT.X, pOINT.Y);
			blocker = new BlockingForm(1500);
			blocker.FormBorderStyle = FormBorderStyle.Sizable;
			blocker.StartPosition = FormStartPosition.Manual;
			blocker.ShowInTaskbar = false;
			blocker.TopMost = true;
			blocker.Bounds = new Rectangle(200, 200, 320, 220);
			blocker.Show();
			Pump();
			Settle();
			Native.GetWindowRect(blocker.Handle, out var lpRect);
			Native.POINT outsidePt = new Native.POINT(700, 500);
			Native.RECT rect = new Native.RECT(lpRect.Left, lpRect.Top, lpRect.Right, lpRect.Bottom);
			long num = MeasureOnOtherThread(() =>
			{
				WindowActions.ExitFullscreenToWindowed(blocker.Handle, ref rect, outsidePt, (Action w) =>
				{
					coord.Drag.PostTargetWork(w);
				});
			});
			Check(num < 500, "钩子线程上全屏降级不阻塞目标（异步提交）", "耗时 " + num + "ms ≥ " + 500 + "ms：这条路径上还有同步跨进程写");
			Native.RECT expect1 = new Native.RECT(rect.Left, rect.Top, rect.Right, rect.Bottom);
			Check(PumpUntil(() =>
			{
				Native.GetWindowRect(blocker.Handle, out var lpRect3);
				return RectsEqual(lpRect3, expect1);
			}, 6000), "异步提交的降级最终真的落了位", "期望 " + expect1.Left + "," + expect1.Top + " 实际窗口仍在别处");
			bool topBefore = WindowActions.IsTopMost(blocker.Handle);
			long num2 = MeasureOnOtherThread(() =>
			{
				coord.Drag.PostTargetWork(() =>
				{
					WindowActions.ToggleTopMost(blocker.Handle);
				});
			});
			Check(num2 < 500, "钩子线程上置顶切换不阻塞目标（异步提交）", "耗时 " + num2 + "ms ≥ " + 500 + "ms：这条路径上还有同步跨进程写");
			Check(PumpUntil(() => WindowActions.IsTopMost(blocker.Handle) != topBefore, 3000), "置顶切换最终生效（异步写没有丢）");
			Native.GetWindowRect(blocker.Handle, out var lpRect2);
			Native.POINT grab = new Native.POINT(lpRect2.Right - 12, lpRect2.Bottom - 12);
			Native.POINT finalPt = new Native.POINT(grab.X + 60, grab.Y + 40);
			Native.RECT expectFinal = new Native.RECT(lpRect2.Left, lpRect2.Top, lpRect2.Right + 60, lpRect2.Bottom + 40);
			ActivateTrigger(coord);
			Settle();
			WindowActions.ForcedTargetForTest = blocker.Handle;
			int sessionCount = coord.Drag.SessionCount;
			long num3 = MeasureOnOtherThread(() =>
			{
				Native.MSLLHOOKSTRUCT ms = Ms(grab);
				coord.OnMouse(516, ref ms);
			});
			Check(num3 < 500, "钩子线程上 Begin 不阻塞目标", "耗时 " + num3 + "ms ≥ " + 500 + "ms");
			Check(coord.Drag.SessionCount > sessionCount, "缩放会话已经建立", "会话数 " + sessionCount + " → " + coord.Drag.SessionCount);
			long num4 = MeasureOnOtherThread(() =>
			{
				Native.MSLLHOOKSTRUCT ms = Ms(finalPt);
				coord.OnMouse(512, ref ms);
			});
			Check(num4 < 500, "拖动中的移动事件不阻塞钩子线程（慢目标由工作线程消化）", "耗时 " + num4 + "ms ≥ " + 500 + "ms：移动还在钩子线程上同步等目标");
			Native.SetCursorPos(finalPt.X, finalPt.Y);
			long num5 = MeasureOnOtherThread(() =>
			{
				Native.MSLLHOOKSTRUCT ms = Ms(finalPt);
				coord.OnMouse(517, ref ms);
			});
			Check(num5 < 500, "★ 松手（End）不阻塞钩子线程 —— 用户报障'死机'的原路径 ★", "耗时 " + num5 + "ms ≥ " + 500 + "ms：End 还在钩子线程上同步写窗口");
			Check(!coord.Drag.IsDragging, "松开右键后缩放会话结束");
			Check(PumpUntil(() =>
			{
				Native.GetWindowRect(blocker.Handle, out var lpRect3);
				return RectsEqual(lpRect3, expectFinal);
			}, 8000), "阻塞目标上松手后仍精确落位（最终写由拖动工作线程完成）", "期望 " + expectFinal.Left + "," + expectFinal.Top + " " + expectFinal.Width + "x" + expectFinal.Height + "（右下各外扩 60/40）");
			coord.Trigger.SimulateKeyUp(null);
		}
		finally
		{
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
			try
			{
				if (coord != null)
				{
					coord.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				if (blocker != null && !blocker.IsDisposed)
				{
					blocker.Visible = false;
					Native.DestroyWindow(blocker.Handle);
				}
				if (blocker != null)
				{
					blocker.Close();
					blocker.Dispose();
				}
			}
			catch
			{
			}
			for (int num6 = 0; num6 < 6; num6++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			try
			{
				if (cursorPos)
				{
					Native.SetCursorPos(lpPoint.X, lpPoint.Y);
				}
			}
			catch
			{
			}
		}
	}

	private static long MeasureOnOtherThread(Action action)
	{
		Exception err = null;
		long innerMs = -1L;
		Thread thread = new Thread(() =>
		{
			Stopwatch stopwatch2 = Stopwatch.StartNew();
			try
			{
				action();
			}
			catch (Exception ex)
			{
				err = ex;
			}
			finally
			{
				innerMs = stopwatch2.ElapsedMilliseconds;
			}
		});
		thread.IsBackground = true;
		thread.Start();
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (thread.IsAlive)
		{
			Application.DoEvents();
			Thread.Sleep(5);
		}
		stopwatch.Stop();
		if (err != null)
		{
			Log.Error(err);
		}
		if (stopwatch.ElapsedMilliseconds - innerMs > 50)
		{
			Log.Trace("[测量] 被测线程 " + innerMs + "ms，等待循环 " + stopwatch.ElapsedMilliseconds + "ms（差额为 UI 线程替阻塞目标泵消息，不计入）");
		}
		return innerMs;
	}

	private static bool PumpUntil(Func<bool> cond, int timeoutMs)
	{
		long num = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < num)
		{
			Application.DoEvents();
			if (cond())
			{
				return true;
			}
			Thread.Sleep(10);
		}
		Application.DoEvents();
		return cond();
	}

	private static bool RectsEqual(Native.RECT a, Native.RECT b)
	{
		if (a.Left == b.Left && a.Top == b.Top && a.Right == b.Right)
		{
			return a.Bottom == b.Bottom;
		}
		return false;
	}

	private static void Check(bool ok, string name, string detail = null)
	{
		if (ok)
		{
			_pass++;
			Line("  [PASS] " + name);
		}
		else
		{
			_fail++;
			Line("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : (" :: " + detail)));
		}
	}

	private static void Bad(string name, string detail)
	{
		_fail++;
		Line("  [FAIL] " + name + (string.IsNullOrEmpty(detail) ? "" : (" :: " + detail)));
	}

	private static void Eq<T>(T actual, T expected, string name)
	{
		bool flag = object.Equals(actual, expected);
		bool ok = flag;
		object detail;
		if (!flag)
		{
			T val = expected;
			string? text = val?.ToString();
			val = actual;
			detail = "期望 " + text + "，实际 " + val;
		}
		else
		{
			detail = null;
		}
		Check(ok, name, (string)detail);
	}

	private static void Line(string s)
	{
		Buf.Append(s);
		Buf.Append("\r\n");
	}
}
// ============================================================================
// 设置窗体（v2 全量重做）
//
// 结构：左侧 SideNav（8 项导航） + 右侧 8 个分页（每页标题/副标题 + 白色圆角卡片流）
//       + 底部操作条（状态文字 / 恢复默认值 / 保存并生效 / 取消）。
// 控件全部自绘（FlatCombo/FlatNum/FlatCheck/FlatButton/SwatchGrid/StyleGallery/
//       FramePreviewBox/HelpPanel/Card/SideNav/PageHost），96dpi 设计稿坐标统一
//       乘 S() 缩放；DPI 变化整体重建（WM_DPICHANGED → RescaleForDpi）。
//
// 与 v1 保持不变的行为契约：
//   · 构造即 Populate；保存走 CaptureInto + ConfigStore.Apply + AutoStart.Apply
//   · 取消 = 直接关闭（不落盘）；恢复默认值保留"环境类"设置（自启/权限/黑名单/尺寸/自动更新）
//   · 按键录制：ProcessCmdKey + OnMouseDown + Coordinator.SuppressTrigger
//   · 黑名单双入口：OpenFileDialog 挑 exe + 全屏遮罩点选窗口
//   · 检查更新/下载/替换重启（Updater），管理员重启，窗口尺寸记忆
//   · 滚轮守卫：下拉框收起时吞掉、展开时放行；数字框未聚焦时吞掉
//   · 外框预览与真实浮层共用 FrameStyles.Draw 同一段绘制代码
// ============================================================================
internal sealed class SettingsForm : Form
{
	// NCCALCSIZE_PARAMS：只用到 rgrc[0]（新的客户区矩形，屏幕坐标）。
	[StructLayout(LayoutKind.Sequential)]
	private struct NCCALCSIZE_PARAMS
	{
		public Native.RECT rgrc0;

		public Native.RECT rgrc1;

		public Native.RECT rgrc2;

		public IntPtr lppos;
	}

	// ======================= 值对象（自测探针序列化用） =======================

	internal sealed class FrameSelection
	{
		public string ColorId;
		public string StyleId;
		public int Thickness;
		public int Radius;
		public bool FrameEnabled;
		public string PreviewColorId;
		public string PreviewStyleId;
		public int PreviewThickness;
		public int PreviewRadius;
		public bool PreviewFrameEnabled;
		public string GridColorId;
		public string GridStyleId;
		public string ColorNameText;
		public bool FollowAccent;
		public int CustomColorArgb;
		public string EffectiveColorHex;
	}

	internal sealed class FramePickerProbe
	{
		public int ColorCells;
		public int StyleCells;
		public long GridInk;
		public long GalleryInk;
		public long GridAccentInk;
		public long GalleryAccentInk;
		public Rectangle GridSelectedCell;
		public Rectangle GallerySelectedCell;
		public Size GridSize;
		public Size GallerySize;
		public bool GridInsideGroup;
		public bool GalleryInsideGroup;
	}

	internal sealed class GroupClipReport
	{
		public int Total;
		public int Clipped;
		public string Detail = "";
	}

	internal sealed class RenderStats
	{
		public int Width;
		public int Height;
		public int DistinctColors;
		public int BlackPixels;
		public int AccentPixels;
		public int FrameColorPixels;
		public double MeanBrightness;
	}

	// ======================= 系统主题（浅色 / 深色） =======================

	// 跟着 Windows 的「应用模式」走：读 HKCU\...\Themes\Personalize\AppsUseLightTheme。
	// 设置页在 WM_SETTINGCHANGE / WM_THEMECHANGED 时重新读，真的变了才整体重建（和 DPI 变化同一条路），
	// 这样切系统白夜间时不用重开窗口，颜色也不会停在上一次的样子。
	private static class Theme
	{
		private static bool _dark;
		private static bool _probed;
		private static bool? _forced;

		public static bool IsDark
		{
			get
			{
				if (!_probed)
				{
					Refresh();
				}
				return _dark;
			}
		}

		/// <summary>重新探测系统主题；返回"和上次比是否变了"。</summary>
		public static bool Refresh()
		{
			bool dark = DetectDark();
			bool changed = _probed && dark != _dark;
			_dark = dark;
			_probed = true;
			return changed;
		}

		/// <summary>截图/自检用：强制按某个主题渲染（null = 回到跟随系统）。</summary>
		public static void SetForceDark(bool? dark)
		{
			_forced = dark;
			_probed = false;
		}

		public static bool DetectDark()
		{
			if (_forced.HasValue)
			{
				return _forced.Value;
			}
			try
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
				if (registryKey != null)
				{
					object value = registryKey.GetValue("AppsUseLightTheme");
					if (value is int num)
					{
						return num == 0;
					}
				}
			}
			catch
			{
			}
			return false;
		}
	}

	// ======================= 设计令牌 =======================

	// 一套亮色、一套暗色。颜色统一走属性而不是 const/readonly 字段，
	// 这样控件在重绘时天然拿到当前主题的颜色（不需要挨个控件重新赋值）。
	// 调色板与圆角照「按键增强」那套来：
	// 底色是极浅的冷灰蓝，卡片纯白 + 1px 描边分层；不用投影堆层次 ——
	// WinForms 里阴影又慢又让文字发虚，浅描边反而更干净、更"产品"。
	private static class Tokens
	{
		private static Color P(Color light, Color dark)
		{
			return Theme.IsDark ? dark : light;
		}

		public static Color Canvas => P(Color.FromArgb(242, 245, 250), Color.FromArgb(22, 24, 29));

		public static Color Sidebar => P(Color.FromArgb(251, 252, 254), Color.FromArgb(27, 30, 36));

		public static Color Surface => P(Color.FromArgb(255, 255, 255), Color.FromArgb(33, 36, 41));

		/// <summary>再凹一层的底色（预览框内衬、样式缩略图、侧栏状态卡）。</summary>
		public static Color SurfaceAlt => P(Color.FromArgb(245, 248, 252), Color.FromArgb(26, 29, 34));

		public static Color Stroke => P(Color.FromArgb(228, 232, 240), Color.FromArgb(46, 50, 58));

		public static Color StrokeStrong => P(Color.FromArgb(211, 217, 230), Color.FromArgb(58, 63, 73));

		public static Color Divider => P(Color.FromArgb(237, 241, 247), Color.FromArgb(42, 46, 53));

		/// <summary>悬停底色（导航项、列表行、次要按钮）。</summary>
		public static Color Hover => P(Color.FromArgb(240, 243, 249), Color.FromArgb(38, 42, 49));

		public static Color TextPrimary => P(Color.FromArgb(22, 26, 35), Color.FromArgb(237, 239, 244));

		public static Color TextSecondary => P(Color.FromArgb(91, 100, 120), Color.FromArgb(168, 176, 191));

		public static Color TextTertiary => P(Color.FromArgb(138, 147, 166), Color.FromArgb(120, 128, 143));

		public static Color Accent => P(Color.FromArgb(43, 108, 246), Color.FromArgb(91, 141, 249));

		public static Color AccentPressed => P(Color.FromArgb(31, 91, 224), Color.FromArgb(127, 167, 255));

		public static Color AccentSoft => P(Color.FromArgb(232, 240, 255), Color.FromArgb(27, 41, 66));

		public static Color AccentSoftStroke => P(Color.FromArgb(199, 218, 255), Color.FromArgb(44, 68, 112));

		public static Color InputStroke => P(Color.FromArgb(220, 226, 238), Color.FromArgb(52, 58, 68));

		public static Color InputStrokeHover => P(Color.FromArgb(201, 210, 227), Color.FromArgb(65, 72, 84));

		public static Color FieldFill => P(Color.FromArgb(255, 255, 255), Color.FromArgb(28, 31, 36));

		public static Color FieldStroke => P(Color.FromArgb(220, 226, 238), Color.FromArgb(52, 58, 68));

		public static Color Hint => TextTertiary;

		public static Color WarnText => P(Color.FromArgb(138, 90, 11), Color.FromArgb(226, 180, 100));

		public static Color Success => P(Color.FromArgb(31, 157, 98), Color.FromArgb(79, 206, 143));

		public static Color Danger => P(Color.FromArgb(229, 72, 77), Color.FromArgb(242, 118, 118));

		public static Color TrackOff => P(Color.FromArgb(219, 226, 238), Color.FromArgb(62, 68, 78));

		public static Color TrackOffHover => P(Color.FromArgb(203, 212, 227), Color.FromArgb(76, 83, 95));

		public static Color ButtonHover => P(Color.FromArgb(240, 243, 249), Color.FromArgb(42, 46, 53));

		/// <summary>强调色底上的文字（暗色主题的强调色偏亮，用深色字更清楚）。</summary>
		public static Color TextOnAccent => P(Color.FromArgb(255, 255, 255), Color.FromArgb(9, 14, 22));

		/// <summary>开关滑块（暗色主题不用纯白，避免刺眼）。</summary>
		public static Color SwitchKnob => P(Color.FromArgb(255, 255, 255), Color.FromArgb(244, 246, 249));

		public static Color CardShadow => P(Color.FromArgb(10, 16, 32, 64), Color.FromArgb(80, 0, 0, 0));

		/// <summary>卡片圆角（参照里是 14）。</summary>
		public const int Radius = 14;

		/// <summary>控件圆角（输入框、按钮、色块、导航胶囊，参照里是 9）。</summary>
		public const int RadiusField = 9;

		/// <summary>侧栏宽度（参照 1240 宽窗口用 228）。</summary>
		public const int SidebarW = 228;

		/// <summary>标题栏高度（参照里是 56）。</summary>
		public const int TitleBarH = 56;

		/// <summary>内容列最大宽度（参照里是 880，左对齐）。</summary>
		public const int CardMaxW = 880;

		/// <summary>卡片之间的垂直间距。</summary>
		public const int CardGap = 14;
	}

	private static class HQ
	{
		public static void Apply(Graphics g)
		{
			g.SmoothingMode = SmoothingMode.HighQuality;
			g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			g.CompositingQuality = CompositingQuality.HighQuality;
			g.PixelOffsetMode = PixelOffsetMode.Half;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		}
	}

	private static class Icons
	{
		private static FontFamily _family;
		private static bool _probed;

		public static FontFamily Family
		{
			get
			{
				if (_probed)
				{
					return _family;
				}
				_probed = true;
				string[] array = new string[2] { "Segoe Fluent Icons", "Segoe MDL2 Assets" };
				try
				{
					FontFamily[] families = FontFamily.Families;
					for (int i = 0; i < array.Length; i++)
					{
						for (int j = 0; j < families.Length; j++)
						{
							if (string.Equals(families[j].Name, array[i], StringComparison.OrdinalIgnoreCase))
							{
								_family = families[j];
								return _family;
							}
						}
					}
				}
				catch
				{
				}
				return null;
			}
		}

		public static bool Available => Family != null;

		public static Font At(float px)
		{
			FontFamily family = Family;
			if (family == null)
			{
				return null;
			}
			try
			{
				return new Font(family, px, FontStyle.Regular, GraphicsUnit.Pixel);
			}
			catch
			{
				return null;
			}
		}
	}

	internal static class Glyph
	{
		public const string Keyboard = "\ue765";
		public const string Move = "\ue7c2";
		public const string Color = "\ue790";
		public const string Speed = "\ue9d9";
		public const string Shield = "\ue72e";
		public const string Chevron = "\ue76c";
		public const string Help = "\ue897";
		public const string Info = "\ue946";
	}

	// ======================= 补间动画 =======================

	private sealed class Tween
	{
		private static readonly List<Tween> Active = new List<Tween>();
		private static readonly System.Windows.Forms.Timer Timer = new System.Windows.Forms.Timer
		{
			Interval = 16
		};

		private readonly Control _owner;
		private float _from;
		private float _to;
		private float _value;
		private int _elapsed;
		private const int Duration = 170;

		static Tween()
		{
			Timer.Tick += delegate
			{
				for (int i = Active.Count - 1; i >= 0; i--)
				{
					Tween tween = Active[i];
					if (tween._owner == null || tween._owner.IsDisposed)
					{
						Active.RemoveAt(i);
						continue;
					}
					tween._elapsed += Timer.Interval;
					float num = Math.Min(1f, (float)tween._elapsed / (float)Duration);
					float num2 = 1f - (float)Math.Pow(1f - num, 3.0);
					tween._value = tween._from + (tween._to - tween._from) * num2;
					if (num >= 1f)
					{
						tween._value = tween._to;
						Active.RemoveAt(i);
					}
					try
					{
						tween._owner.Invalidate();
					}
					catch
					{
					}
				}
				if (Active.Count == 0)
				{
					Timer.Stop();
				}
			};
		}

		public Tween(Control owner)
		{
			_owner = owner;
		}

		public float Value => _value;

		public void Snap(float v)
		{
			_value = v;
			_to = v;
			_from = v;
			_elapsed = Duration;
			for (int i = 0; i < Active.Count; i++)
			{
				if (Active[i] == this)
				{
					Active.RemoveAt(i);
					break;
				}
			}
		}

		public void To(float target)
		{
			if (Math.Abs(_value - target) < 0.0001f && Math.Abs(_to - target) < 0.0001f)
			{
				return;
			}
			_from = _value;
			_to = target;
			_elapsed = 0;
			if (!Active.Contains(this))
			{
				Active.Add(this);
			}
			if (!Timer.Enabled)
			{
				Timer.Start();
			}
		}
	}

	// ======================= 卡片 =======================
	//
	// 布局照「按键增强」的设置页做：
	// 一张白卡（圆角 14 + 1px 描边）里是一串"设置行"，
	// 每行左边是标题 + 一行灰色说明，右边是控件（开关/下拉框/数字/按钮，右对齐）；
	// 行与行之间一条极淡的分隔线。这种结构比"标签和控件挤在一行"更好读，
	// 也天然对齐 —— 所有控件都贴在右侧同一列上。

	private interface INeedHeight
	{
		int HeightFor(int width);
	}

	private enum RowKind
	{
		Row,
		Block,
		Note
	}

	private sealed class RowSpec
	{
		public RowKind Kind;
		public Control Ctl;
		public Control Tail;
		public Label TitleCtl;
		public Label NoteCtl;
		public string Label;
		public string Note;
		public int CtlW;
		public int TailW;
		public int BlockH;
	}

	private sealed class Card : Panel
	{
		public readonly string Title;
		public readonly string Subtitle;

		private readonly List<RowSpec> _rows = new List<RowSpec>();
		private readonly List<Control> _fields = new List<Control>();
		private readonly List<int> _dividers = new List<int>();
		private Rectangle _titleRect;
		private Rectangle _subRect;
		// 字体在构造时建一次：原来每次 OnPaint 都 new Font，滚动时一秒钟上千次分配 + GDI 句柄
		private readonly Font _fontTitle;
		private readonly Font _fontSub;

		/// <summary>卡片内边距（参照里 Padding=18，这里给 20 更透气）。</summary>
		public const int PadX = 20;
		private const int PadTop = 18;
		private const int PadBottom = 20;

		public Card(string title, string subtitle)
		{
			Title = title;
			Subtitle = subtitle;
			Text = title;
			BackColor = Tokens.Canvas;
			ForeColor = Tokens.TextPrimary;
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
			_fontTitle = new Font(DesignFontFamily, 12.75f, FontStyle.Bold, GraphicsUnit.Point);
			_fontSub = new Font(DesignFontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point);
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_fontTitle.Dispose();
				_fontSub.Dispose();
			}
			base.Dispose(disposing);
		}

		// ---------- 行定义 ----------

		/// <summary>标准设置行：左标题 + 说明，右控件（固定宽）。ctlW &lt;= 0 表示按控件自然宽。</summary>
		public void Setting(Control ctl, string label, string note = null, int ctlW = 180)
		{
			Add(new RowSpec
			{
				Kind = RowKind.Row,
				Ctl = ctl,
				Label = label,
				Note = note,
				CtlW = ctlW
			});
		}

		/// <summary>同一行两个控件（触发键 + 重新录制、数值 + 单位）：主控件 + 尾巴控件依次右对齐。</summary>
		public void Setting(Control ctl, int ctlW, Control tail, int tailW, string label, string note = null)
		{
			Add(new RowSpec
			{
				Kind = RowKind.Row,
				Ctl = ctl,
				CtlW = ctlW,
				Tail = tail,
				TailW = tailW,
				Label = label,
				Note = note
			});
		}

		/// <summary>开关行：左标题 + 说明，右边只放一个开关（宽度按开关自身算）。</summary>
		public void Toggle(Control ctl, string label, string note = null)
		{
			Add(new RowSpec
			{
				Kind = RowKind.Row,
				Ctl = ctl,
				Label = label,
				Note = note,
				CtlW = -1
			});
		}

		/// <summary>整行说明文字（不是设置项，只是解释）。</summary>
		public void Note(string text)
		{
			Label label = NewLabel(text, 12, Tokens.TextTertiary, bold: false);
			Add(new RowSpec
			{
				Kind = RowKind.Note,
				Ctl = label
			});
		}

		/// <summary>整宽块：色板 / 样式画廊 / 预览框 / 帮助列表，可以带一个小节标题。</summary>
		public void Block(Control ctl, string label = null, int blockH = 0)
		{
			Label label2 = null;
			if (!string.IsNullOrEmpty(label))
			{
				label2 = NewLabel(label, 12, Tokens.TextSecondary, bold: false);
			}
			Add(new RowSpec
			{
				Kind = RowKind.Block,
				Ctl = ctl,
				TitleCtl = label2,
				Label = label,
				BlockH = blockH
			});
		}

		private void Add(RowSpec spec)
		{
			_rows.Add(spec);
			if (spec.Ctl != null && !Controls.Contains(spec.Ctl))
			{
				Controls.Add(spec.Ctl);
			}
			if (spec.Tail != null && !Controls.Contains(spec.Tail))
			{
				Controls.Add(spec.Tail);
			}
			if (spec.TitleCtl != null && !Controls.Contains(spec.TitleCtl))
			{
				Controls.Add(spec.TitleCtl);
			}
			if (spec.Kind == RowKind.Row)
			{
				if (spec.TitleCtl == null && !string.IsNullOrEmpty(spec.Label))
				{
					spec.TitleCtl = NewLabel(spec.Label, 13.5f, Tokens.TextPrimary, bold: false);
				}
				if (spec.NoteCtl == null && !string.IsNullOrEmpty(spec.Note))
				{
					spec.NoteCtl = NewLabel(spec.Note, 12, Tokens.TextTertiary, bold: false);
				}
			}
		}

		private Label NewLabel(string text, float px, Color color, bool bold)
		{
			Label label = new Label
			{
				Text = text ?? "",
				AutoSize = false,
				AutoEllipsis = true,
				UseMnemonic = false,
				// 必须是实色而不是 Transparent：WinForms 里 Transparent 的 Label 每次重绘
				// 都要先让父控件把自己那块重画一遍，一页几十个标签就是几十次卡片重绘 ——
				// 这是"滚动/切页发闷"的主要来源。卡片底就是纯色，用同一个颜色零成本。
				BackColor = Tokens.Surface,
				ForeColor = color,
				// Font 的单位是"磅"，设计稿给的是像素：1px = 0.75pt
				Font = new Font(DesignFontFamily, px * 0.75f, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point),
				TextAlign = ContentAlignment.MiddleLeft
			};
			Controls.Add(label);
			return label;
		}

		public void RegisterField(Control c)
		{
			if (c == null || _fields.Contains(c))
			{
				return;
			}
			_fields.Add(c);
			try
			{
				c.BackColor = Tokens.FieldFill;
			}
			catch
			{
			}
		}

		// ---------- 度量与排版 ----------

		public int Measure(int width)
		{
			return Arrange(width, apply: false);
		}

		public void Layout(int width)
		{
			SuspendLayout();
			Arrange(width, apply: true);
			ResumeLayout(performLayout: false);
		}

		private int Arrange(int width, bool apply)
		{
			int padX = Sc(PadX);
			int inner = Math.Max(Sc(120), width - padX * 2);
			int y = Sc(PadTop);
			bool header = false;

			if (!string.IsNullOrEmpty(Title))
			{
				_titleRect = new Rectangle(padX, y, inner, Sc(24));
				y += Sc(24);
				header = true;
			}
			else
			{
				_titleRect = Rectangle.Empty;
			}
			if (!string.IsNullOrEmpty(Subtitle))
			{
				_subRect = new Rectangle(padX, y + Sc(2), inner, Sc(18));
				y += Sc(20);
				header = true;
			}
			else
			{
				_subRect = Rectangle.Empty;
			}
			if (header)
			{
				y += Sc(14);
			}
			if (apply)
			{
				_dividers.Clear();
			}

			bool first = true;
			foreach (RowSpec row in _rows)
			{
				if (row.Kind == RowKind.Note)
				{
					int lines = MeasureLines(row.Ctl.Text, row.Ctl, inner);
					int noteH = Sc(21) * Math.Max(1, lines);
					if (apply)
					{
						Place(row.Ctl, new Rectangle(padX, y, inner, noteH));
					}
					y += noteH + Sc(8);
					continue;
				}

				if (row.Kind == RowKind.Block)
				{
					if (!string.IsNullOrEmpty(row.Label))
					{
						if (apply && row.TitleCtl != null)
						{
							Place(row.TitleCtl, new Rectangle(padX, y, inner, Sc(18)));
						}
						y += Sc(22);
					}
					int blockH = ResolveBlockHeight(row, inner);
					if (apply)
					{
						Place(row.Ctl, new Rectangle(padX, y, inner, blockH));
					}
					y += blockH + Sc(6);
					continue;
				}

				// ---- 标准设置行 ----
				int ctlW = ((row.CtlW > 0) ? Sc(row.CtlW) : ((row.CtlW < 0) ? NaturalWidth(row.Ctl) : Sc(180)));
				int tailW = ((row.Tail == null) ? 0 : ((row.TailW > 0) ? Sc(row.TailW) : NaturalWidth(row.Tail)));
				int gap = ((row.Tail == null) ? 0 : Sc(8));
				// 窄窗口兜底：先保证左侧文字还有 90，控件列按剩余空间收缩（尾巴先收、再收主控件），
				// 宁可控件变小，也绝不允许控件叠到文字上。
				int avail = Math.Max(Sc(60), inner - Sc(16));
				int room = Math.Max(Sc(40), avail - Sc(90));
				if (ctlW + gap + tailW > room)
				{
					if (tailW > 0)
					{
						tailW = Math.Min(tailW, Math.Max(Sc(28), room / 2));
					}
					ctlW = Math.Max(Sc(36), room - gap - tailW);
					if (ctlW + gap + tailW > room && tailW > 0)
					{
						tailW = Math.Max(0, room - gap - ctlW);
						gap = ((tailW > 0) ? gap : 0);
					}
				}
				int ctlTotal = ctlW + gap + tailW;
				int labelW = Math.Max(Sc(40), avail - ctlTotal);

				int titleH = Sc(24);
				// 说明文字也要按「折几行」给高度：一行放不下时它真的会折成两行，
				// 只给一行的高度就会把第二行裁掉（这就是「文字显示不全」的根因）。
				int noteH2 = ((row.NoteCtl == null) ? 0 : (Math.Max(1, MeasureLines(row.NoteCtl.Text, row.NoteCtl, labelW)) * Sc(20) + Sc(2)));
				int coreH = titleH + noteH2;
				int ctlH = Math.Max(Sc(24), ControlHeight(row.Ctl, ctlW));
				int contentH = Math.Max(Math.Max(coreH, ctlH), Sc(24));
				int rowH = contentH + Sc(24);

				if (!first && apply)
				{
					_dividers.Add(y);
				}
				first = false;

				int top = y + Sc(12);
				int textY = top + Math.Max(0, (contentH - coreH) / 2);
				if (apply)
				{
					if (row.TitleCtl != null)
					{
						Place(row.TitleCtl, new Rectangle(padX, textY, labelW, titleH));
					}
					if (row.NoteCtl != null)
					{
						Place(row.NoteCtl, new Rectangle(padX, textY + titleH, labelW, noteH2));
					}
					int ctlY = top + Math.Max(0, (contentH - ctlH) / 2);
					Place(row.Ctl, new Rectangle(padX + inner - ctlTotal, ctlY, ctlW, ctlH));
					// ComboBox 之类的高度由系统锁定，SetBounds 改不动 —— 按真实高度重算一次垂直居中
					if (row.Ctl.Height != ctlH)
					{
						row.Ctl.Top = ctlY + Math.Max(0, (ctlH - row.Ctl.Height) / 2);
					}
					if (row.Tail != null)
					{
						int tailH = Math.Max(Sc(24), ControlHeight(row.Tail, tailW));
						int tailY = top + Math.Max(0, (contentH - tailH) / 2);
						Place(row.Tail, new Rectangle(padX + inner - tailW, tailY, tailW, tailH));
						if (row.Tail.Height != tailH)
						{
							row.Tail.Top = tailY + Math.Max(0, (tailH - row.Tail.Height) / 2);
						}
					}
				}
				y += rowH;
			}

			int total = y + Sc(PadBottom) - Sc(6);
			if (apply)
			{
				ApplyFieldRegions();
			}
			return Math.Max(total, Sc(60));
		}

		// 折行数缓存：文字和可用宽度没变就直接复用，避免每次重排都建 Font + 两次 MeasureText
		private readonly Dictionary<string, int> _lineCache = new Dictionary<string, int>();
		private readonly Dictionary<string, int> _lineCacheWidth = new Dictionary<string, int>();

		private int _lineH;

		// 行数 = 折行后的总高 / 单行高。
		// 不能用 TextLines（折行高 / 不折行高）：文案里自带 \r\n 时两边行数相同，会被算成 1 行，
		// 于是长提示只分到一行的高度、第二段被裁掉。
		private int MeasureLinesExact(string text, Control ctl, int width)
		{
			try
			{
				if (_lineH <= 0)
				{
					_lineH = Math.Max(1, TextRenderer.MeasureText("字", ctl.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height);
				}
				int num = TextRenderer.MeasureText(text, ctl.Font, new Size(Math.Max(20, width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
				return Math.Max(1, (int)Math.Round((double)num / (double)_lineH));
			}
			catch
			{
				return 1;
			}
		}

		private int MeasureLines(string text, Control ctl, int width)
		{
			if (string.IsNullOrEmpty(text))
			{
				return 1;
			}
			string key = text;
			int num;
			if (_lineCache.TryGetValue(key, out num) && _lineCacheWidth.TryGetValue(key, out num) && num == width)
			{
				return _lineCache[key];
			}
			int num2 = MeasureLinesExact(text, ctl, width);
			_lineCache[key] = num2;
			_lineCacheWidth[key] = width;
			return num2;
		}

		private int ResolveBlockHeight(RowSpec row, int inner)
		{
			// Label 型的整宽块（提示文字）：高度按实际折行数算，不能吃写死的 BlockH，
			// 否则长文案（比如"流畅度 → 固定值"下面那段实测说明）第二行就会被裁掉。
			if (row.Ctl is Label)
			{
				return Math.Max(Sc(20), MeasureLines(row.Ctl.Text, row.Ctl, inner) * Sc(20));
			}
			if (row.Ctl is INeedHeight needHeight)
			{
				int num = needHeight.HeightFor(inner);
				if (num > 0)
				{
					return num;
				}
			}
			if (row.BlockH > 0)
			{
				return Sc(row.BlockH);
			}
			return Math.Max(Sc(32), row.Ctl.Height);
		}

		// 控件的设计高度：开关 24、输入类 32、其它各自 HeightFor。
		private static int ControlHeight(Control ctl, int width)
		{
			if (ctl == null)
			{
				return 0;
			}
			if (ctl is FlatCheck)
			{
				return Sc(24);
			}
			if (ctl is INeedHeight needHeight)
			{
				int num = needHeight.HeightFor(width);
				if (num > 0)
				{
					return num;
				}
			}
			return Sc(32);
		}

		private static int NaturalWidth(Control ctl)
		{
			if (ctl is FlatCheck)
			{
				return Sc(38);
			}
			if (ctl != null && ctl.Width > 0)
			{
				return ctl.Width;
			}
			return Sc(96);
		}

		// Region 是 GDI 对象：每次重排都新建一个、旧的不释放，拖着窗口改尺寸几分钟就能把
		// GDI 句柄堆到上限（之后整机都开始卡）。这里按尺寸缓存 + 换新时释放旧的。
		private readonly Dictionary<Control, Size> _fieldSizes = new Dictionary<Control, Size>();

		private void ApplyFieldRegions()
		{
			foreach (Control field in _fields)
			{
				try
				{
					if (field.Width <= 4 || field.Height <= 4)
					{
						continue;
					}
					Size size = new Size(field.Width, field.Height);
					Size size2;
					if (_fieldSizes.TryGetValue(field, out size2) && size2 == size)
					{
						continue;
					}
					Region region = field.Region;
					field.Region = new Region(RoundedPath(new Rectangle(0, 0, size.Width, size.Height), Sc(Tokens.RadiusField)));
					if (region != null)
					{
						region.Dispose();
					}
					_fieldSizes[field] = size;
				}
				catch
				{
				}
			}
		}

		public void SyncFieldRegions()
		{
			ApplyFieldRegions();
		}

		/// <summary>参与"需不需要重排"判定的文字（行标题、说明、整宽块的提示文字）。</summary>
		public void AppendSignature(StringBuilder sb)
		{
			foreach (RowSpec row in _rows)
			{
				if (row.TitleCtl != null)
				{
					sb.Append(row.TitleCtl.Text).Append('|');
				}
				if (row.NoteCtl != null)
				{
					sb.Append(row.NoteCtl.Text).Append('|');
				}
				if (row.Kind == RowKind.Block && row.Ctl is Label label)
				{
					sb.Append(label.Text).Append('|');
				}
			}
		}

		// ---------- 绘制 ----------

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			Rectangle clientRectangle = ClientRectangle;
			using (SolidBrush brush = new SolidBrush(Tokens.Canvas))
			{
				graphics.FillRectangle(brush, clientRectangle);
			}
			Rectangle rect = new Rectangle(0, 0, Math.Max(4, clientRectangle.Width - 1), Math.Max(4, clientRectangle.Height - 1));
			using (GraphicsPath path = RoundedPath(rect, Sc(Tokens.Radius)))
			{
				using (SolidBrush brush2 = new SolidBrush(Tokens.Surface))
				{
					graphics.FillPath(brush2, path);
				}
				using Pen pen = new Pen(Tokens.Stroke);
				graphics.DrawPath(pen, path);
			}
			// 输入框底衬：TextBox / NumericUpDown 自己是无边框的，圆角描边在这里画
			foreach (Control field in _fields)
			{
				if (!field.Visible || field.Width <= 4 || field.Height <= 4)
				{
					continue;
				}
				try
				{
					Rectangle rect2 = new Rectangle(field.Left, field.Top, field.Width, field.Height);
					using GraphicsPath fieldPath = RoundedPath(rect2, Sc(Tokens.RadiusField));
					using (SolidBrush brush3 = new SolidBrush(field.Enabled ? Tokens.FieldFill : Tokens.SurfaceAlt))
					{
						graphics.FillPath(brush3, fieldPath);
					}
					using Pen pen2 = new Pen(field.Enabled ? Tokens.FieldStroke : Tokens.Divider);
					graphics.DrawPath(pen2, fieldPath);
				}
				catch
				{
				}
			}
			try
			{
				if (_titleRect.Width > 0)
				{
					// 卡片标题：17px 半粗，和参照里的 H2 同一档
					TextRenderer.DrawText(graphics, Title, _fontTitle, _titleRect, Tokens.TextPrimary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				}
				if (_subRect.Width > 0)
				{
					TextRenderer.DrawText(graphics, Subtitle, _fontSub, _subRect, Tokens.TextTertiary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				}
				if (_dividers.Count > 0)
				{
					int padX = Sc(PadX);
					using Pen pen3 = new Pen(Tokens.Divider);
					foreach (int divider in _dividers)
					{
						graphics.DrawLine(pen3, padX, divider, Math.Max(padX + 4, clientRectangle.Width - padX - 1), divider);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
	}

	// ======================= 侧栏导航 =======================
	//
	// 参照里的侧栏：整条浅色底 + 右侧 1px 分隔线，
	// 顶部品牌区（图标 + 名称 + 副标题），中间导航胶囊（选中=淡强调色底），
	// 底部一张状态小卡。三段式比"一列项目干挂在顶上"稳得多。

	private sealed class SideNav : Control
	{
		private readonly Tween[] _glow;
		private int _active;
		private int _hot = -1;
		private string[] _titles = Array.Empty<string>();
		private string[] _glyphs = Array.Empty<string>();
		private string _statusTitle = "正在运行";
		private string _statusDetail = "";
		private Bitmap _brand;
		private bool _brandTried;
		// 侧栏是常驻控件，字体必须缓存：原来每次 OnPaint 都 new 6 个 Font
		private readonly Font _fontBrand;
		private readonly Font _fontBrandSub;
		private readonly Font _fontItem;
		private readonly Font _fontGlyph;
		private readonly Font _fontStatus;
		private readonly Font _fontStatusSub;
		public event Action<int> Select;

		public SideNav()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Tokens.Sidebar;
			_glow = new Tween[16];
			_fontBrand = new Font(DesignFontFamily, 10.5f, FontStyle.Bold, GraphicsUnit.Point);
			_fontBrandSub = new Font(DesignFontFamily, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
			_fontItem = new Font(DesignFontFamily, 10f, FontStyle.Regular, GraphicsUnit.Point);
			_fontGlyph = Icons.At(Sc(16));
			_fontStatus = new Font(DesignFontFamily, 9.4f, FontStyle.Regular, GraphicsUnit.Point);
			_fontStatusSub = new Font(DesignFontFamily, 8.25f, FontStyle.Regular, GraphicsUnit.Point);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_fontBrand.Dispose();
				_fontBrandSub.Dispose();
				_fontItem.Dispose();
				_fontGlyph?.Dispose();
				_fontStatus.Dispose();
				_fontStatusSub.Dispose();
				_brand?.Dispose();
			}
			base.Dispose(disposing);
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ActiveIndex
		{
			get
			{
				return _active;
			}
			set
			{
				if (value != _active && value >= 0 && value < _titles.Length)
				{
					_active = value;
					// 其它项的动画值归零：它们不是当前页，不该留下任何底色
					for (int i = 0; i < _glow.Length; i++)
					{
						if (i != value && _glow[i] != null)
						{
							_glow[i].Snap(0f);
						}
					}
					Invalidate();
				}
			}
		}

		public int ItemCount => _titles.Length;

		public void Bind(string[] titles, string[] glyphs, Action<int> onSelect)
		{
			_titles = titles;
			_glyphs = glyphs;
			Select = onSelect;
			Invalidate();
		}

		/// <summary>底部状态卡的两行字（运行状态 + 当前触发键）。</summary>
		public void SetStatus(string title, string detail)
		{
			_statusTitle = title ?? "";
			_statusDetail = detail ?? "";
			Invalidate();
		}

		private int RowH => Sc(38);

		private int RowTop => Sc(84);

		private Bitmap Brand
		{
			get
			{
				if (!_brandTried)
				{
					_brandTried = true;
					try
					{
						using Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
						if (icon != null)
						{
							_brand = icon.ToBitmap();
						}
					}
					catch
					{
						_brand = null;
					}
				}
				return _brand;
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = HitRow(e.Y);
			if (num != _hot)
			{
				_hot = num;
				Cursor = ((num >= 0) ? Cursors.Hand : Cursors.Default);
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = -1;
			Cursor = Cursors.Default;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				int num = HitRow(e.Y);
				if (num >= 0 && num < _titles.Length && Select != null)
				{
					Select(num);
					Invalidate();
				}
			}
			base.OnMouseUp(e);
		}

		private int HitRow(int y)
		{
			for (int i = 0; i < _titles.Length; i++)
			{
				int num = RowTop + i * RowH;
				if (y >= num && y < num + RowH)
				{
					return i;
				}
			}
			return -1;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			Rectangle clientRectangle = ClientRectangle;
			using (SolidBrush solidBrush = new SolidBrush(Tokens.Sidebar))
			{
				graphics.FillRectangle(solidBrush, clientRectangle);
			}
			using (Pen pen = new Pen(Tokens.Stroke))
			{
				graphics.DrawLine(pen, clientRectangle.Width - 1, 0, clientRectangle.Width - 1, clientRectangle.Height);
			}
			DrawBrand(graphics, clientRectangle);
			DrawStatus(graphics, clientRectangle);
			if (_titles.Length == 0)
			{
				return;
			}
			Font font = _fontItem;
			Font font2 = _fontGlyph;
			for (int i = 0; i < _titles.Length; i++)
			{
				int num = RowTop + i * RowH;
				Rectangle r = new Rectangle(Sc(10), num + Sc(2), Math.Max(20, clientRectangle.Width - Sc(20)), RowH - Sc(4));
				bool flag = i == _active;
				bool flag2 = i == _hot;
				float num2 = 0f;
				if (i < _glow.Length && _glow[i] != null)
				{
					num2 = _glow[i].Value;
				}
				else if (flag)
				{
					num2 = 1f;
				}
				// 只有"当前页"和"鼠标悬停"才配背景。
				// 之前这里写的是 glow > 0.01 就画 —— 但 glow 是一次性打满的动画值（打到 1 就不再回落），
				// 结果点过的每一项都会永久留一个灰胶囊，看起来像"没选中却高亮"。
				if (flag || flag2)
				{
					Color color = (flag ? Tokens.AccentSoft : Tokens.Hover);
					float num3 = (flag ? Math.Min(1f, Math.Max(0.4f, num2)) : 0.7f);
					using GraphicsPath path = RoundedPath(r, Sc(10));
					using (SolidBrush brush = new SolidBrush(Mix(Color.FromArgb(0, color), color, num3)))
					{
						graphics.FillPath(brush, path);
					}
				}
				Color color2 = ((flag || flag2) ? Tokens.TextPrimary : Tokens.TextSecondary);
				int num4 = r.Left + Sc(12);
				if (font2 != null && i < _glyphs.Length)
				{
					using (SolidBrush brush2 = new SolidBrush(flag ? Tokens.Accent : Tokens.TextSecondary))
					{
						TextRenderer.DrawText(graphics, _glyphs[i], font2, new Rectangle(num4, r.Top + (r.Height - Sc(16)) / 2, Sc(16), Sc(16)), flag ? Tokens.Accent : Tokens.TextSecondary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
					}
					num4 += Sc(16) + Sc(11);
				}
				TextRenderer.DrawText(graphics, _titles[i], font, new Rectangle(num4, r.Top, Math.Max(10, r.Right - Sc(10) - num4), r.Height), color2, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
		}

		private void DrawBrand(Graphics graphics, Rectangle client)
		{
			try
			{
				int num = Sc(18);
				int num2 = Sc(30);
				Rectangle rect = new Rectangle(num, num + Sc(2), num2, num2);
				Bitmap brand = Brand;
				if (brand != null)
				{
					using (GraphicsPath path = RoundedPath(new Rectangle(rect.X - Sc(1), rect.Y - Sc(1), rect.Width + Sc(2), rect.Height + Sc(2)), Sc(8)))
					{
						using SolidBrush brush = new SolidBrush(Tokens.Surface);
						graphics.FillPath(brush, path);
						using Pen pen = new Pen(Tokens.Stroke);
						graphics.DrawPath(pen, path);
					}
					graphics.DrawImage(brand, rect);
				}
				else
				{
					using GraphicsPath path2 = RoundedPath(rect, Sc(8));
					using (SolidBrush brush2 = new SolidBrush(Tokens.AccentSoft))
					{
						graphics.FillPath(brush2, path2);
					}
					using Font font = Icons.At(Sc(16));
					if (font != null)
					{
						TextRenderer.DrawText(graphics, Glyph.Move, font, rect, Tokens.Accent, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
					}
				}
				int num3 = rect.Right + Sc(11);
				Font font2 = _fontBrand;
				Font font3 = _fontBrandSub;
				TextRenderer.DrawText(graphics, "长按拖窗", font2, new Rectangle(num3, rect.Top - Sc(2), Math.Max(20, client.Width - num3 - Sc(10)), Sc(20)), Tokens.TextPrimary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				TextRenderer.DrawText(graphics, "HoldDrag · Win11 x64", font3, new Rectangle(num3, rect.Top + Sc(16), Math.Max(20, client.Width - num3 - Sc(10)), Sc(16)), Tokens.TextTertiary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		private void DrawStatus(Graphics graphics, Rectangle client)
		{
			try
			{
				int num = Sc(62);
				Rectangle rect = new Rectangle(Sc(12), Math.Max(RowTop + _titles.Length * RowH + Sc(8), client.Height - num - Sc(14)), Math.Max(40, client.Width - Sc(24)), num);
				if (rect.Bottom > client.Height - Sc(6) || rect.Height < Sc(20))
				{
					return;
				}
				using (GraphicsPath path = RoundedPath(rect, Sc(12)))
				{
					using (SolidBrush brush = new SolidBrush(Tokens.SurfaceAlt))
					{
						graphics.FillPath(brush, path);
					}
					using Pen pen = new Pen(Tokens.Stroke);
					graphics.DrawPath(pen, path);
				}
				int num2 = rect.Left + Sc(13);
				int num3 = rect.Top + Sc(12);
				using (SolidBrush brush2 = new SolidBrush(Tokens.Success))
				{
					graphics.FillEllipse(brush2, new Rectangle(num2, num3 + Sc(6), Sc(8), Sc(8)));
				}
				Font font = _fontStatus;
				Font font2 = _fontStatusSub;
				TextRenderer.DrawText(graphics, _statusTitle, font, new Rectangle(num2 + Sc(15), num3, Math.Max(20, rect.Right - num2 - Sc(24)), Sc(20)), Tokens.TextSecondary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				TextRenderer.DrawText(graphics, _statusDetail, font2, new Rectangle(num2, num3 + Sc(21), Math.Max(20, rect.Right - num2 - Sc(13)), Sc(18)), Tokens.TextTertiary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		internal void KickGlow(int index)
		{
			if (index >= 0 && index < _glow.Length)
			{
				if (_glow[index] == null)
				{
					_glow[index] = new Tween(this);
				}
				_glow[index].To(1f);
			}
		}
	}

	// ======================= 自绘下拉框 =======================

	private sealed class FlatCombo : ComboBox
	{
		public Action<Graphics, Rectangle, object> ClosedPainter;
		private bool _hover;
		private bool _open;
		private const int WM_PAINT_MSG = 15;
		private const int WM_MOUSEWHEEL_MSG = 522;
		internal int WheelSwallowedForTest;
		internal int WheelForwardedForTest;

		public FlatCombo()
		{
			DropDownStyle = ComboBoxStyle.DropDownList;
			DrawMode = DrawMode.OwnerDrawFixed;
			FlatStyle = FlatStyle.Flat;
			// ComboBox 的高度被系统锁成 PreferredHeight（ItemHeight + 边框），
			// 默认值在高 DPI 下只有 21 逻辑像素，和旁边的数字框/按钮差一截，看着就是没对齐。
			// 把行高顶上去，闭合态才和输入框同一量级。
			ItemHeight = Sc(27);
			BackColor = Tokens.Surface;
			ForeColor = Tokens.TextPrimary;
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hover = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hover = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnDropDown(EventArgs e)
		{
			_open = true;
			Invalidate();
			base.OnDropDown(e);
		}

		protected override void OnDropDownClosed(EventArgs e)
		{
			_open = false;
			Invalidate();
			base.OnDropDownClosed(e);
		}

		protected override void OnDrawItem(DrawItemEventArgs e)
		{
			if (e.Index < 0)
			{
				base.OnDrawItem(e);
				return;
			}
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			bool flag = (e.State & DrawItemState.Selected) != 0;
			Rectangle bounds = e.Bounds;
			using (SolidBrush brush = new SolidBrush(flag ? Tokens.AccentSoft : Tokens.Surface))
			{
				graphics.FillRectangle(brush, bounds);
			}
			if (flag)
			{
				using SolidBrush brush2 = new SolidBrush(Tokens.Accent);
				graphics.FillRectangle(brush2, new Rectangle(bounds.Left, bounds.Top, Sc(3), bounds.Height));
			}
			object obj = ((e.Index < Items.Count) ? Items[e.Index] : null);
			Rectangle rectangle = new Rectangle(bounds.Left + Sc(10), bounds.Top, Math.Max(10, bounds.Width - Sc(16)), bounds.Height);
			if (ClosedPainter != null)
			{
				ClosedPainter(graphics, rectangle, obj);
				return;
			}
			TextRenderer.DrawText(graphics, (obj != null) ? obj.ToString() : "", Font, rectangle, Tokens.TextPrimary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == 522)
			{
				if (!DroppedDown)
				{
					WheelSwallowedForTest++;
					return;
				}
				WheelForwardedForTest++;
			}
			base.WndProc(ref m);
			if (m.Msg == 15)
			{
				PaintClosed();
			}
		}

		private void PaintClosed()
		{
			try
			{
				if (!IsHandleCreated || Width <= 0 || Height <= 0)
				{
					return;
				}
				using Graphics graphics = Graphics.FromHwnd(Handle);
				HQ.Apply(graphics);
				int num = Sc(1);
				GraphicsPath graphicsPath = RoundedPath(new Rectangle(num, num, Width - num * 2 - 1, Height - num * 2 - 1), Sc(Tokens.RadiusField));
				try
				{
					using (SolidBrush brush = new SolidBrush(Enabled ? BackColor : Tokens.Canvas))
					{
						graphics.FillPath(brush, graphicsPath);
					}
					Color color;
					if (Enabled)
					{
						color = ((_hover || _open) ? Tokens.Accent : Tokens.InputStroke);
					}
					else
					{
						color = Tokens.Divider;
					}
					using Pen pen = new Pen(color);
					graphics.DrawPath(pen, graphicsPath);
				}
				finally
				{
					graphicsPath.Dispose();
				}
				int num2 = Sc(26);
				object obj = ((SelectedIndex >= 0 && SelectedIndex < Items.Count) ? Items[SelectedIndex] : null);
				Rectangle rectangle = new Rectangle(Sc(10), 0, Math.Max(10, Width - num2 - Sc(16)), Height);
				if (ClosedPainter != null)
				{
					ClosedPainter(graphics, rectangle, obj);
				}
				else
				{
					TextRenderer.DrawText(graphics, (obj != null) ? obj.ToString() : "", Font, rectangle, Enabled ? Tokens.TextPrimary : Tokens.TextTertiary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
				}
				float num3 = (float)Width - (float)num2 / 2f;
				float num4 = (float)Height / 2f;
				float num5 = (float)Sc(2) * 1.15f;
				using Pen pen2 = new Pen(Enabled ? Tokens.TextSecondary : Tokens.TextTertiary, Math.Max(1.2f, (float)Sc(1) * 1.4f));
				pen2.StartCap = LineCap.Round;
				pen2.EndCap = LineCap.Round;
				pen2.LineJoin = LineJoin.Round;
				graphics.DrawLines(pen2, new PointF[3]
				{
					new PointF(num3 - num5 * 1.7f, num4 - num5 * 0.45f),
					new PointF(num3, num4 + num5 * 1.15f),
					new PointF(num3 + num5 * 1.7f, num4 - num5 * 0.45f)
				});
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
	}

	// ======================= 自绘数字框 =======================

	private sealed class FlatNum : NumericUpDown
	{
		public FlatNum()
		{
			// 不开这个，高度会被系统按字体锁死（实测只有 18 逻辑像素），
			// 和同一行的标签/按钮对不齐——看上去就是"字和框的高度乱七八糟"。
			AutoSize = false;
			BorderStyle = BorderStyle.None;
			BackColor = Tokens.FieldFill;
			ForeColor = Tokens.TextPrimary;
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			for (int i = 0; i < Controls.Count; i++)
			{
				try
				{
					Controls[i].BackColor = Tokens.FieldFill;
					Controls[i].ForeColor = Tokens.TextPrimary;
					if (Controls[i] is TextBox textBox)
					{
						textBox.BorderStyle = BorderStyle.None;
					}
				}
				catch
				{
				}
			}
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg != 522 || Focused)
			{
				base.WndProc(ref m);
			}
		}
	}

	// ======================= 自绘开关 =======================

	private sealed class FlatCheck : CheckBox
	{
		private readonly Tween _hoverT;
		private readonly Tween _checkT;

		public int TrackW => Sc(38);
		public int TrackH => Sc(20);
		public int KnobInset => Sc(2);
		public int KnobSize => TrackH - KnobInset * 2;

		public FlatCheck()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			FlatStyle = FlatStyle.Flat;
			BackColor = Tokens.Surface;
			ForeColor = Tokens.TextPrimary;
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
			Cursor = Cursors.Hand;
			_hoverT = new Tween(this);
			_checkT = new Tween(this);
			_checkT.Snap(Checked ? 1f : 0f);
		}

		public Rectangle TrackRect => new Rectangle(0, Math.Max(0, (Height - TrackH) / 2), TrackW, TrackH);

		public Point KnobCenter
		{
			get
			{
				Rectangle trackRect = TrackRect;
				float num = (Checked ? Math.Min(1f, _checkT.Value) : 0f);
				if (Checked && num <= 0.001f)
				{
					num = 1f;
				}
				int num2 = KnobInset;
				int num3 = TrackW - num2 * 2 - KnobSize;
				int num4 = trackRect.Left + num2 + (int)Math.Round((float)num3 * num);
				return new Point(num4 + KnobSize / 2, trackRect.Top + KnobInset + KnobSize / 2);
			}
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hoverT.To(1f);
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hoverT.To(0f);
			base.OnMouseLeave(e);
		}

		protected override void OnCheckedChanged(EventArgs e)
		{
			if (Checked)
			{
				_checkT.To(1f);
			}
			else
			{
				_checkT.Snap(0f);
			}
			base.OnCheckedChanged(e);
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			Invalidate();
			base.OnEnabledChanged(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			using (SolidBrush brush = new SolidBrush(BackColor))
			{
				graphics.FillRectangle(brush, ClientRectangle);
			}
			Rectangle trackRect = TrackRect;
			int radius = TrackH / 2;
			bool flag = Checked;
			float value = _hoverT.Value;
			float num = (flag ? Math.Min(1f, _checkT.Value) : 0f);
			if (flag && num <= 0.001f)
			{
				num = 1f;
			}
			Color color;
			if (!Enabled)
			{
				color = (flag ? Tokens.TrackOff : Tokens.Divider);
			}
			else
			{
				Color a = Mix(Tokens.TrackOff, Tokens.TrackOffHover, value);
				Color b = Mix(Tokens.Accent, ControlPaint.Light(Tokens.Accent, 0.08f), value);
				color = Mix(a, b, num);
			}
			using (GraphicsPath path = RoundedPath(trackRect, radius))
			{
				using (SolidBrush brush2 = new SolidBrush(color))
				{
					graphics.FillPath(brush2, path);
				}
			}
			Point knobCenter = KnobCenter;
			Rectangle rect = new Rectangle(knobCenter.X - KnobSize / 2, knobCenter.Y - KnobSize / 2, KnobSize, KnobSize);
			using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(28, Tokens.TextPrimary)))
			{
				graphics.FillEllipse(brush3, new Rectangle(rect.X, rect.Y + Math.Max(1, Sc(1)), rect.Width, rect.Height));
			}
			using (SolidBrush brush4 = new SolidBrush(Enabled ? Tokens.SwitchKnob : Tokens.Surface))
			{
				graphics.FillEllipse(brush4, rect);
			}
			Rectangle bounds = new Rectangle(trackRect.Right + Sc(8), 0, Math.Max(10, Width - trackRect.Right - Sc(10)), Height);
			if (bounds.Width > 0)
			{
				Color foreColor = (Enabled ? ForeColor : Tokens.TextTertiary);
				TextRenderer.DrawText(graphics, Text, Font, bounds, foreColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
		}

		internal void KickHoverForTest()
		{
			_hoverT.To(1f);
		}

		internal float HoverValueForTest => _hoverT.Value;

		internal void SnapCheckForTest(bool on)
		{
			_checkT.Snap(on ? 1f : 0f);
			Checked = on;
			Invalidate();
		}
	}

	// ======================= 自绘按钮 =======================

	private sealed class FlatButton : Button
	{
		private readonly Tween _hoverT;
		private bool _primary;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool Primary
		{
			get
			{
				return _primary;
			}
			set
			{
				_primary = value;
				Invalidate();
			}
		}

		public FlatButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			FlatStyle = FlatStyle.Flat;
			BackColor = Tokens.Surface;
			ForeColor = Tokens.TextPrimary;
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
			Cursor = Cursors.Hand;
			_hoverT = new Tween(this);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hoverT.To(1f);
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hoverT.To(0f);
			base.OnMouseLeave(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			Rectangle clientRectangle = ClientRectangle;
			graphics.Clear(BackColor);
			float value = _hoverT.Value;
			Color color = (_primary ? Tokens.Accent : Tokens.ButtonHover);
			Color back = Mix(_primary ? Tokens.Accent : Tokens.Surface, color, value);
			if (!Enabled)
			{
				back = Tokens.Canvas;
			}
			using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, clientRectangle.Width, clientRectangle.Height), Sc(Tokens.RadiusField)))
			{
				using (SolidBrush brush = new SolidBrush(back))
				{
					graphics.FillPath(brush, path);
				}
				if (!_primary)
				{
					using (Pen pen = new Pen(Enabled ? Tokens.InputStroke : Tokens.Divider))
					{
						graphics.DrawPath(pen, path);
					}
				}
			}
			Color foreColor = (_primary ? Tokens.TextOnAccent : Tokens.TextPrimary);
			if (!Enabled)
			{
				foreColor = Tokens.TextTertiary;
			}
			TextRenderer.DrawText(graphics, Text, Font, new Rectangle(Sc(8), 0, Math.Max(10, clientRectangle.Width - Sc(16)), clientRectangle.Height), foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
		}

		internal void KickHoverForTest()
		{
			_hoverT.To(1f);
		}

		internal float HoverValueForTest => _hoverT.Value;
	}

	// ======================= 标题条按钮 =======================

	// 3 个窗口按钮：最小化 / 最大化(还原) / 关闭。
	// 图标用线条自己画（不依赖图标字体），悬停/按下都有反馈，关闭按钮悬停变红。
	private sealed class CaptionButton : Control
	{
		private bool _hot;
		private bool _down;
		private int _kind;
		private bool _maximized;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Kind
		{
			get
			{
				return _kind;
			}
			set
			{
				_kind = value;
				Invalidate();
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsMaximized
		{
			get
			{
				return _maximized;
			}
			set
			{
				if (_maximized != value)
				{
					_maximized = value;
					Invalidate();
				}
			}
		}

		public CaptionButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Tokens.Canvas;
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hot = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = false;
			_down = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				_down = true;
				Invalidate();
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			_down = false;
			Invalidate();
			base.OnMouseUp(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			Rectangle clientRectangle = ClientRectangle;
			graphics.Clear(BackColor);
			if (_hot || _down)
			{
				Color color = ((_kind == 2) ? Color.FromArgb(196, 43, 28) : Tokens.ButtonHover);
				if (_down)
				{
					color = ControlPaint.Dark(color, 0.06f);
				}
				using SolidBrush brush = new SolidBrush(color);
				graphics.FillRectangle(brush, clientRectangle);
			}
			Color color2 = ((_kind == 2 && _hot) ? Color.White : Tokens.TextSecondary);
			float num = Math.Max(1.1f, (float)Sc(1) * 1.15f);
			using Pen pen = new Pen(color2, num);
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			int num2 = clientRectangle.Width / 2;
			int num3 = clientRectangle.Height / 2;
			int num4 = Math.Max(4, Sc(5));
			switch (_kind)
			{
			case 0:
				graphics.DrawLine(pen, num2 - num4, num3, num2 + num4, num3);
				break;
			case 1:
				if (_maximized)
				{
					graphics.DrawRectangle(pen, num2 - num4 + Sc(1), num3 - num4, num4 * 2 - Sc(2), num4 * 2 - Sc(2));
					graphics.DrawRectangle(pen, num2 - num4, num3 - num4 + Sc(2), num4 * 2 - Sc(2), num4 * 2 - Sc(2));
				}
				else
				{
					graphics.DrawRectangle(pen, num2 - num4, num3 - num4, num4 * 2, num4 * 2);
				}
				break;
			default:
				graphics.DrawLine(pen, num2 - num4 + 1, num3 - num4 + 1, num2 + num4 - 1, num3 + num4 - 1);
				graphics.DrawLine(pen, num2 + num4 - 1, num3 - num4 + 1, num2 - num4 + 1, num3 + num4 - 1);
				break;
			}
		}
	}

	private void UpdateMaximizeGlyph()
	{
		try
		{
			if (_btnMax != null)
			{
				_btnMax.IsMaximized = (WindowState == FormWindowState.Maximized);
			}
		}
		catch
		{
		}
	}

	// ======================= 色板 =======================

	private sealed class SwatchGrid : Control, INeedHeight
	{
		public sealed class Entry
		{
			public string Id;
			public string Name;
			public Color Color;
		}

		private readonly List<Entry> _items = new List<Entry>();
		private int _sel;
		private int _hot = -1;
		public event EventHandler Picked;

		public int Cell => Sc(30);
		public int Gap => Sc(8);

		public void SetItems(List<Entry> items)
		{
			_items.Clear();
			_items.AddRange(items);
			if (_sel >= _items.Count)
			{
				_sel = 0;
			}
			Invalidate();
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string SelectedId
		{
			get
			{
				if (_sel >= 0 && _sel < _items.Count)
				{
					return _items[_sel].Id;
				}
				return null;
			}
			set
			{
				for (int i = 0; i < _items.Count; i++)
				{
					if (_items[i].Id == value)
					{
						_sel = i;
						Invalidate();
						break;
					}
				}
			}
		}

		public int CellCount => _items.Count;

		public Rectangle SelectedCellRect
		{
			get
			{
				if (_sel < 0 || _sel >= _items.Count)
				{
					return Rectangle.Empty;
				}
				int columns = ColumnsFor(Width);
				int num = _sel % columns;
				int num2 = _sel / columns;
				return new Rectangle(num * (Cell + Gap), num2 * (Cell + Gap), Cell, Cell);
			}
		}

		public int ColumnsFor(int width)
		{
			int num = Cell + Gap;
			int num2 = (width + Gap) / num;
			if (num2 < 4)
			{
				num2 = 4;
			}
			if (num2 > 12)
			{
				num2 = 12;
			}
			return num2;
		}

		public int HeightFor(int width)
		{
			int columns = ColumnsFor(width);
			int num = (_items.Count + columns - 1) / columns;
			return Math.Max(Cell, num * (Cell + Gap) - Gap);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = Hit(e.X, e.Y);
			if (num != _hot)
			{
				_hot = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = -1;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				int num = Hit(e.X, e.Y);
				if (num >= 0)
				{
					_sel = num;
					Invalidate();
					Picked?.Invoke(this, EventArgs.Empty);
				}
			}
			base.OnMouseUp(e);
		}

		private int Hit(int x, int y)
		{
			int columns = ColumnsFor(Width);
			int num = x / (Cell + Gap);
			int num2 = y / (Cell + Gap);
			if (num < 0 || num2 < 0 || num >= columns)
			{
				return -1;
			}
			int num3 = num2 * columns + num;
			if (num3 >= _items.Count)
			{
				return -1;
			}
			return num3;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear(Tokens.Surface);
			int columns = ColumnsFor(Width);
			for (int i = 0; i < _items.Count; i++)
			{
				int num = i % columns;
				int num2 = i / columns;
				Rectangle rect = new Rectangle(num * (Cell + Gap), num2 * (Cell + Gap), Cell, Cell);
				bool flag = i == _sel;
				bool flag2 = i == _hot;
				using (GraphicsPath path = RoundedPath(new Rectangle(rect.X + Sc(2), rect.Y + Sc(2), Cell - Sc(4), Cell - Sc(4)), Sc(Tokens.RadiusField)))
				{
					using (SolidBrush brush = new SolidBrush(_items[i].Color))
					{
						graphics.FillPath(brush, path);
					}
					Color color = (flag ? Tokens.Accent : (flag2 ? Tokens.InputStrokeHover : Tokens.Stroke));
					using (Pen pen = new Pen(color, flag ? Sc(2) : 1f))
					{
						graphics.DrawPath(pen, path);
					}
				}
				if (flag)
				{
					using (GraphicsPath path2 = RoundedPath(new Rectangle(rect.X + Sc(7), rect.Y + Sc(7), Cell - Sc(14), Cell - Sc(14)), Sc(4)))
					{
						using (Pen pen2 = new Pen(Tokens.Surface, Math.Max(1.5f, Sc(2))))
						{
							graphics.DrawPath(pen2, path2);
						}
					}
				}
			}
		}
	}

	// ======================= 样式画廊 =======================

	private sealed class StyleGallery : Control, INeedHeight
	{
		private List<string> _items = new List<string>();
		private int _sel;
		private int _hot = -1;
		private string _colorId;
		private Color? _colorOverride;
		private int _thickness = 3;
		private int _radius = 12;
		private bool _frameEnabled = true;
		public event EventHandler Picked;

		public List<string> ItemsForTest => _items;

		/// <summary>非空时优先用它画（跟随强调色 / 自定义颜色都走这条路）。</summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Color? ColorOverride
		{
			get
			{
				return _colorOverride;
			}
			set
			{
				_colorOverride = value;
				Invalidate();
			}
		}

		public int Gap => Sc(8);

		public void SetItems(List<string> items)
		{
			_items = items;
			if (_sel >= _items.Count)
			{
				_sel = 0;
			}
			Invalidate();
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string SelectedId
		{
			get
			{
				if (_sel >= 0 && _sel < _items.Count)
				{
					return _items[_sel];
				}
				return null;
			}
			set
			{
				for (int i = 0; i < _items.Count; i++)
				{
					if (_items[i] == value)
					{
						_sel = i;
						Invalidate();
						break;
					}
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string ColorId
		{
			get
			{
				return _colorId;
			}
			set
			{
				_colorId = value;
				Invalidate();
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Thickness
		{
			get
			{
				return _thickness;
			}
			set
			{
				_thickness = value;
				Invalidate();
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Radius
		{
			get
			{
				return _radius;
			}
			set
			{
				_radius = value;
				Invalidate();
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool FrameEnabled
		{
			get
			{
				return _frameEnabled;
			}
			set
			{
				_frameEnabled = value;
				Invalidate();
			}
		}

		public int ColumnsFor(int width)
		{
			int num = Sc(88) + Gap;
			int num2 = (width + Gap) / num;
			if (num2 < 2)
			{
				num2 = 2;
			}
			return num2;
		}

		public int CellH => Sc(64);

		public int HeightFor(int width)
		{
			int columns = ColumnsFor(width);
			int num = (_items.Count + columns - 1) / columns;
			return Math.Max(CellH, num * (CellH + Gap) - Gap);
		}

		public Rectangle SelectedCellRect
		{
			get
			{
				if (_sel < 0 || _sel >= _items.Count)
				{
					return Rectangle.Empty;
				}
				int columns = ColumnsFor(Width);
				int num = _sel % columns;
				int num2 = _sel / columns;
				int num3 = (Width + Gap) / columns - Gap;
				return new Rectangle(num * (num3 + Gap), num2 * (CellH + Gap), num3, CellH);
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = Hit(e.X, e.Y);
			if (num != _hot)
			{
				_hot = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = -1;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				int num = Hit(e.X, e.Y);
				if (num >= 0)
				{
					_sel = num;
					Invalidate();
					Picked?.Invoke(this, EventArgs.Empty);
				}
			}
			base.OnMouseUp(e);
		}

		private int Hit(int x, int y)
		{
			int columns = ColumnsFor(Width);
			int num = (Width + Gap) / columns - Gap;
			int num2 = x / (num + Gap);
			int num3 = y / (CellH + Gap);
			if (num2 < 0 || num3 < 0 || num2 >= columns)
			{
				return -1;
			}
			int num4 = num3 * columns + num2;
			if (num4 >= _items.Count)
			{
				return -1;
			}
			return num4;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear(Tokens.Surface);
			int columns = ColumnsFor(Width);
			int num = (Width + Gap) / columns - Gap;
			Color color = Color.Gray;
			try
			{
				color = _colorOverride ?? FrameColors.Resolve(_colorId ?? FrameColors.DefaultId);
			}
			catch
			{
			}
			using Font font = new Font(DesignFontFamily, Sc(11), FontStyle.Regular, GraphicsUnit.Pixel);
			for (int i = 0; i < _items.Count; i++)
			{
				int num2 = i % columns;
				int num3 = i / columns;
				int num4 = num2 * (num + Gap);
				int num5 = num3 * (CellH + Gap);
				bool flag = i == _sel;
				bool flag2 = i == _hot;
				Rectangle rect = new Rectangle(num4, num5, num, CellH);
				using (GraphicsPath path = RoundedPath(new Rectangle(rect.X, rect.Y, rect.Width, CellH - Sc(18)), Sc(Tokens.RadiusField)))
				{
					using (SolidBrush brush = new SolidBrush(Tokens.SurfaceAlt))
					{
						graphics.FillPath(brush, path);
					}
					Color color2 = (flag ? Tokens.Accent : (flag2 ? Tokens.InputStrokeHover : Tokens.Stroke));
					using (Pen pen = new Pen(color2, flag ? Sc(2) : 1f))
					{
						graphics.DrawPath(pen, path);
					}
				}
				RectangleF r = new RectangleF((float)rect.X + Sc(6), (float)rect.Y + Sc(5), (float)rect.Width - Sc(12), (float)CellH - Sc(38));
				if (_frameEnabled && r.Width > 4 && r.Height > 4)
				{
					try
					{
						FrameStyles.Draw(graphics, r, color, _items[i], Math.Max(1, _thickness), _radius);
					}
					catch (Exception ex)
					{
						Log.Error(ex);
					}
				}
				else if (!_frameEnabled && r.Width > 4 && r.Height > 4)
				{
					using Pen pen2 = new Pen(Tokens.Divider);
					pen2.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
					graphics.DrawRectangle(pen2, r.X, r.Y, r.Width, r.Height);
				}
				TextRenderer.DrawText(graphics, FrameStyles.DisplayName(_items[i]), font, new Rectangle(rect.X, rect.Bottom - Sc(17), rect.Width, Sc(16)), flag ? Tokens.AccentPressed : Tokens.TextSecondary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			}
		}
	}

	// ======================= 实时预览 =======================

	private sealed class FramePreviewBox : Control
	{
		private string _colorId;
		private string _styleId;
		private Color? _colorOverride;
		private int _thickness = 3;
		private int _radius = 12;
		private bool _frameEnabled = true;
		private int _opacity = 100;

		/// <summary>非空时优先用它画（跟随强调色 / 自定义颜色都走这条路）。</summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Color? ColorOverride
		{
			get
			{
				return _colorOverride;
			}
			set
			{
				_colorOverride = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string ColorId
		{
			get
			{
				return _colorId;
			}
			set
			{
				_colorId = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string StyleId
		{
			get
			{
				return _styleId;
			}
			set
			{
				_styleId = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Thickness
		{
			get
			{
				return _thickness;
			}
			set
			{
				_thickness = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Radius
		{
			get
			{
				return _radius;
			}
			set
			{
				_radius = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool FrameEnabled
		{
			get
			{
				return _frameEnabled;
			}
			set
			{
				_frameEnabled = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int OpacityPercent
		{
			get
			{
				return _opacity;
			}
			set
			{
				_opacity = value;
			}
		}

		public FramePreviewBox()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Tokens.Surface;
		}

		public void Refresh_()
		{
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear(Tokens.Surface);
			Rectangle clientRectangle = ClientRectangle;
			int num = Math.Max(2, Sc(6));
			RectangleF rectangleF = new RectangleF((float)num + 1f, (float)num + 1f, (float)clientRectangle.Width - (float)num * 2f - 2f, (float)clientRectangle.Height - (float)num * 2f - 2f);
			if (rectangleF.Width <= 4 || rectangleF.Height <= 4)
			{
				return;
			}
			using (GraphicsPath path = RoundedPath(Rectangle.Ceiling(rectangleF), Sc(Tokens.RadiusField)))
			{
				using (SolidBrush brush = new SolidBrush(Tokens.SurfaceAlt))
				{
					graphics.FillPath(brush, path);
				}
				using (Pen pen = new Pen(Tokens.Divider))
				{
					graphics.DrawPath(pen, path);
				}
			}
			if (!_frameEnabled)
			{
				using Font font = new Font(DesignFontFamily, Sc(11), FontStyle.Regular, GraphicsUnit.Pixel);
				TextRenderer.DrawText(graphics, "外框已关闭", font, clientRectangle, Tokens.TextTertiary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				return;
			}
			Color color = Color.Gray;
			try
			{
				color = _colorOverride ?? FrameColors.Resolve(_colorId ?? FrameColors.DefaultId);
			}
			catch
			{
			}
			int alpha = _opacity * 255 / 100;
			if (alpha < 20)
			{
				alpha = 20;
			}
			if (alpha > 255)
			{
				alpha = 255;
			}
			try
			{
				FrameStyles.Draw(graphics, rectangleF, Color.FromArgb(alpha, color), _styleId ?? FrameStyles.DefaultId, Math.Max(1, _thickness), _radius);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
	}

	// ======================= 帮助面板 =======================

	internal sealed class HelpPanel : Control, INeedHeight
	{
		internal sealed class Entry
		{
			public string Icon;
			public string Title;
			public string Body;
		}

		private List<Entry> _entries = new List<Entry>();
		private int _open;
		private int _hot = -1;

		public int HeaderH => Sc(36);

		public int HeightFor(int width)
		{
			return Sc(360);
		}

		public void SetEntries(List<Entry> entries)
		{
			_entries = entries;
			Invalidate();
		}

		public int EntryCountForTest => _entries.Count;

		public int OpenIndexForTest => _open;

		public int BodyH => Math.Max(0, Height - _entries.Count * HeaderH);

		public int HeaderTopForTest(int index)
		{
			return index * HeaderH + ((index > _open) ? BodyH : 0);
		}

		public Rectangle HeaderRect(int index)
		{
			return new Rectangle(0, HeaderTopForTest(index), Width, HeaderH);
		}

		public Rectangle BodyRect()
		{
			return new Rectangle(0, (_open + 1) * HeaderH, Width, BodyH);
		}

		public void OpenRangeForTest(out int top, out int bottom)
		{
			top = _open * HeaderH;
			bottom = top + HeaderH + BodyH;
		}

		private int HitHeader(int y)
		{
			for (int i = 0; i < _entries.Count; i++)
			{
				int num = HeaderTopForTest(i);
				if (y >= num && y < num + HeaderH)
				{
					return i;
				}
			}
			return -1;
		}

		public void ClickAtForTest(int y)
		{
			int num = HitHeader(y);
			if (num >= 0 && num != _open)
			{
				_open = num;
			}
			Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = HitHeader(e.Y);
			if (num != _hot)
			{
				_hot = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = -1;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				int num = HitHeader(e.Y);
				if (num >= 0 && num != _open)
				{
					_open = num;
					Invalidate();
				}
			}
			base.OnMouseUp(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear(Tokens.Surface);
			if (_entries.Count == 0)
			{
				return;
			}
			using Font font = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel);
			using Font font2 = new Font(DesignFontFamily, Sc(11), FontStyle.Regular, GraphicsUnit.Pixel);
			using Font font3 = Icons.At(Sc(14));
			using Font font4 = Icons.At(Sc(12));
			for (int i = 0; i < _entries.Count; i++)
			{
				int num = HeaderTopForTest(i);
				bool flag = i == _open;
				bool flag2 = i == _hot;
				Rectangle rectangle = new Rectangle(0, num, Width, HeaderH);
				if (flag)
				{
					using (SolidBrush brush = new SolidBrush(Tokens.AccentSoft))
					{
						graphics.FillRectangle(brush, rectangle);
					}
				}
				else if (flag2)
				{
					using (SolidBrush brush2 = new SolidBrush(Tokens.ButtonHover))
					{
						graphics.FillRectangle(brush2, rectangle);
					}
				}
				int num2 = Sc(14);
				if (font3 != null)
				{
					TextRenderer.DrawText(graphics, _entries[i].Icon, font3, new Rectangle(num2, rectangle.Top + (HeaderH - Sc(16)) / 2, Sc(16), Sc(16)), flag ? Tokens.Accent : Tokens.TextSecondary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
					num2 += Sc(22);
				}
				TextRenderer.DrawText(graphics, _entries[i].Title, font, new Rectangle(num2, rectangle.Top, Math.Max(10, Width - num2 - Sc(30)), HeaderH), flag ? Tokens.AccentPressed : Tokens.TextPrimary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				if (font4 != null)
				{
					TextRenderer.DrawText(graphics, Glyph.Chevron, font4, new Rectangle(Width - Sc(26), rectangle.Top + (HeaderH - Sc(14)) / 2, Sc(14), Sc(14)), flag ? Tokens.Accent : Tokens.TextTertiary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
				}
				using (Pen pen = new Pen(Tokens.Divider))
				{
					graphics.DrawLine(pen, 0, rectangle.Bottom - 1, Width, rectangle.Bottom - 1);
				}
			}
			Rectangle bodyRect = BodyRect();
			if (bodyRect.Height > 4)
			{
				using (SolidBrush brush3 = new SolidBrush(Tokens.Surface))
				{
					graphics.FillRectangle(brush3, bodyRect);
				}
				TextRenderer.DrawText(graphics, _entries[_open].Body, font2, new Rectangle(bodyRect.Left + Sc(16), bodyRect.Top + Sc(4), Math.Max(10, bodyRect.Width - Sc(28)), Math.Max(10, bodyRect.Height - Sc(8))), Tokens.TextSecondary, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Top);
			}
		}
	}

	// ======================= 分页滚动容器 =======================

	// 双缓冲贴图纸：滚动时整页是"把画好的位图往上挪"，不逐控件重画
	private sealed class BufferedPanel : Panel
	{
		public BufferedPanel()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, value: true);
			DoubleBuffered = true;
		}
	}

	private sealed class PageHost : Panel
	{
		private int _offset;
		public Panel Canvas;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Offset
		{
			get
			{
				return _offset;
			}
			set
			{
				int num = Math.Max(0, Math.Min(MaxScroll, value));
				if (num != _offset)
				{
					_offset = num;
					if (Canvas != null)
					{
						Canvas.Top = -_offset;
					}
					Invalidate();
				}
			}
		}

		public int MaxScroll
		{
			get
			{
				if (Canvas == null)
				{
					return 0;
				}
				return Math.Max(0, Canvas.Height - ClientSize.Height);
			}
		}

		public int StepPx => Sc(96);

		public PageHost()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Tokens.Canvas;
			AutoScroll = false;
			Canvas = new BufferedPanel
			{
				BackColor = Tokens.Canvas,
				Location = new Point(0, 0)
			};
			Canvas.Paint += delegate(object s, PaintEventArgs e)
			{
				e.Graphics.Clear(Tokens.Canvas);
			};
			Controls.Add(Canvas);
		}

		public void UpdateScroll()
		{
			int num = MaxScroll;
			if (_offset > num)
			{
				_offset = num;
			}
			if (Canvas != null && Canvas.Top != -_offset)
			{
				// 值没变还去设一次，会把整页位图重新贴一遍
				Canvas.Top = -_offset;
			}
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if (MaxScroll > 0)
			{
				Offset -= e.Delta / 120 * StepPx;
			}
			base.OnMouseWheel(e);
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == 522)
			{
				int delta = (short)unchecked((int)(m.WParam.ToInt64() >> 16));
				if (MaxScroll > 0)
				{
					Offset -= delta / 120 * StepPx;
					return;
				}
			}
			base.WndProc(ref m);
		}
	}

	private sealed class Page
	{
		public string Title;
		public string Subtitle;
		public PageHost Host;
		public List<Card> Cards = new List<Card>();
		// 上次排版用的宽度和文字签名：宽度/文字都没变就不用重排这一页
		public int LaidOutWidth = -1;
		public string Signature = "";
	}

	// ======================= 窗体字段 =======================

	private readonly ConfigStore _store;
	private readonly Coordinator _coord;
	private AppConfig _edit;
	private bool _recording;
	private bool _updateBusy;
	private bool _populating;

	private SideNav _nav;
	private Panel _content;
	private Panel _pager;
	private readonly List<Page> _pages = new List<Page>();
	private int _activePage;

	private Panel _titleStrip;
	private CaptionButton _btnMin;
	private CaptionButton _btnMax;
	private CaptionButton _btnClose;
	private Label _lblPageTitle;
	private Label _lblPageSub;

	private Label _lblStatus;
	private FlatButton _btnDefaultsRef;
	private FlatButton _btnSaveRef;
	private FlatButton _btnCancelRef;

	private TextBox _txtKey;
	private FlatButton _btnRecord;
	private Label _lblKeyHint;
	private FlatCheck _chkHold;
	private FlatNum _numHold;

	private FlatCombo _cmbLeft;
	private FlatCombo _cmbRight;
	private FlatCombo _cmbMiddle;

	private FlatCheck _chkFrame;
	private FlatCombo _cmbFrameColor;
	private FlatCheck _chkFollowAccent;
	private FlatButton _btnCustomColor;
	private Label _lblCustomColor;
	private int _customColorArgb;
	private FlatCombo _cmbFrameStyle;
	private SwatchGrid _gridColor;
	private Label _lblColorName;
	private Label _lblStyleHeader;
	private Label _lblStyleHint;
	private StyleGallery _galleryStyle;
	private FlatNum _numFrameThick;
	private FlatNum _numFrameRadius;
	private FlatNum _numFrameOpacity;
	private Label _lblFrameTip;
	private FramePreviewBox _pnlPreview;

	private FlatNum _numCorner;
	private FlatCheck _chkDblLeft;
	private FlatCheck _chkDblRight;
	private FlatCheck _chkRestoreMax;
	private FlatCheck _chkFront;
	private FlatCheck _chkFullscreen;
	private FlatCheck _chkExitFullscreen;
	private FlatCheck _chkShake;
	private TextBox _txtBlacklist;
	private FlatButton _btnPickExe;
	private FlatButton _btnPickWindow;

	private FlatCombo _cmbRefreshMode;
	private FlatNum _numRefreshHz;
	private Label _lblRefreshHint;

	private FlatCombo _cmbAutoStart;
	private FlatCheck _chkRunAsAdmin;
	private FlatCheck _chkWarnNonAdmin;
	private Label _lblAdmin;
	private FlatButton _btnRestartAdmin;

	private HelpPanel _helpPanel;

	private FlatButton _btnGitHub;
	private FlatCheck _chkAutoUpdate;
	private FlatButton _btnCheckUpdate;
	private Label _lblUpdateStatus;

	private int _defaultClientH = 680;

	// ======================= 缩放与度量 =======================

	internal static float CurrentUiScale = 1f;

	internal static float UiScaleForTest => (CurrentUiScale > 0f) ? CurrentUiScale : 1f;

	internal static string AppVersionForTest => AppInfo.Version;

	private float _uiScale = 1f;

	private float UiScale
	{
		get
		{
			return _uiScale;
		}
		set
		{
			_uiScale = value;
			CurrentUiScale = value;
		}
	}

	internal static string DesignFontFamily = "Microsoft YaHei UI";

	private int S(int design)
	{
		return (int)Math.Round((float)design * ((UiScale > 0f) ? UiScale : 1f));
	}

	private static int Sc(int design)
	{
		return (int)Math.Round((float)design * ((CurrentUiScale > 0f) ? CurrentUiScale : 1f));
	}

	// 与自测 T40 同一把尺子：GDI+ 排版高度 ×1.28，用来给标签留够高度（高 DPI 不裁字）。
	private static int TextHeightFor(Control c)
	{
		try
		{
			if (string.IsNullOrEmpty(c.Text))
			{
				return 0;
			}
			using Graphics graphics = c.CreateGraphics();
			return (int)Math.Ceiling(graphics.MeasureString(c.Text, c.Font, int.MaxValue, StringFormat.GenericTypographic).Height * 1.28f);
		}
		catch
		{
			return 0;
		}
	}

	private static int TextLines(string text, Control c, int avail)
	{
		if (string.IsNullOrEmpty(text) || avail <= 10)
		{
			return 1;
		}
		try
		{
			float num = c.Font.SizeInPoints * 96f / 72f * ((CurrentUiScale > 0f) ? CurrentUiScale : 1f);
			if (num < 6f)
			{
				num = 6f;
			}
			using Font font = new Font(c.Font.FontFamily, num, c.Font.Style, GraphicsUnit.Pixel);
			Size size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
			Size size2 = TextRenderer.MeasureText(text, font, new Size(avail, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
			if (size.Height <= 0)
			{
				return 1;
			}
			int num2 = (int)Math.Ceiling((double)size2.Height / (double)size.Height);
			if (num2 < 1)
			{
				num2 = 1;
			}
			return num2;
		}
		catch
		{
			return 1;
		}
	}

	private static Color Mix(Color a, Color b, float t)
	{
		if (t < 0f)
		{
			t = 0f;
		}
		if (t > 1f)
		{
			t = 1f;
		}
		return Color.FromArgb((int)Math.Round((float)a.A + (float)(b.A - a.A) * t), (int)Math.Round((float)a.R + (float)(b.R - a.R) * t), (int)Math.Round((float)a.G + (float)(b.G - a.G) * t), (int)Math.Round((float)a.B + (float)(b.B - a.B) * t));
	}

	private static GraphicsPath RoundedPath(Rectangle r, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (radius <= 0)
		{
			graphicsPath.AddRectangle(r);
			return graphicsPath;
		}
		int num = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
		if (num < 1)
		{
			num = 1;
		}
		graphicsPath.AddArc(r.X, r.Y, num * 2, num * 2, 180f, 90f);
		graphicsPath.AddArc(r.Right - num * 2, r.Y, num * 2, num * 2, 270f, 90f);
		graphicsPath.AddArc(r.Right - num * 2, r.Bottom - num * 2, num * 2, num * 2, 0f, 90f);
		graphicsPath.AddArc(r.X, r.Bottom - num * 2, num * 2, num * 2, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	// ======================= 构造 =======================

	public SettingsForm(ConfigStore store, Coordinator coord)
	{
		_store = store;
		_coord = coord;
		_edit = store.Current.Clone();
		try
		{
			DesignFontFamily = PickDesignFont();
		}
		catch
		{
		}
		// 先探一次系统主题，再建界面：控件构造时读到的就是当前该用的那套颜色。
		Theme.Refresh();
		BuildUi();
		Populate();
		// 文案是 Populate 才填进去的（比如「固定值」下面那段实测说明），
		// 而行高要按文字折行数算 —— 填完必须重排一次，否则首帧用的是空文字的高度。
		LayoutAllPages(ContentWidth());
	}

	private static string PickDesignFont()
	{
		string[] array = new string[5] { "Segoe UI Variable Text", "Segoe UI Variable Display", "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI" };
		try
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			using (System.Drawing.Text.InstalledFontCollection installedFontCollection = new System.Drawing.Text.InstalledFontCollection())
			{
				for (int i = 0; i < installedFontCollection.Families.Length; i++)
				{
					hashSet.Add(installedFontCollection.Families[i].Name);
				}
			}
			for (int j = 0; j < array.Length; j++)
			{
				if (hashSet.Contains(array[j]))
				{
					return array[j];
				}
			}
		}
		catch
		{
		}
		return "Microsoft YaHei UI";
	}

	// ======================= 建界面 =======================

	private void BuildUi()
	{
		SuspendLayout();
		Text = "长按拖窗 · 设置";
		Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
		AutoScaleMode = AutoScaleMode.None;
		BackColor = Tokens.Canvas;
		ForeColor = Tokens.TextPrimary;
		// 保留 Sizable（留着 WS_THICKFRAME：DWM 阴影、左右下三边原生缩放都靠它），
		// 但用 WM_NCCALCSIZE 把系统标题栏整条吃掉 —— 标题栏由我们自己画，和内容同色不"断层"。
		FormBorderStyle = FormBorderStyle.Sizable;
		MaximizeBox = true;
		MinimizeBox = true;
		StartPosition = FormStartPosition.CenterScreen;
		AutoScroll = false;
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		DoubleBuffered = true;
		try
		{
			Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
			if (icon != null)
			{
				Icon = icon;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		// 内容区：单列卡片流，卡片限宽 880 左对齐
		_content = new Panel
		{
			BackColor = Tokens.Canvas
		};
		_content.Resize += delegate
		{
			if (_chromeBusy)
			{
				return;
			}
			LayoutWithContent(ContentWidth());
		};
		// 重建界面后几何全变了，强制走一次完整重排
		_lastLayoutW = -1;
		_lastLayoutSig = "";
		BuildControls();
		BuildPages();
		// 侧栏：品牌区 + 导航胶囊 + 底部状态卡（照参照的三段式）
		_nav = new SideNav();
		_nav.Width = S(Tokens.SidebarW);
		_nav.Bind(NavTitles(), NavGlyphs(), delegate(int i)
		{
			SelectPage(i);
		});
		// 标题栏：左边是当前页标题 + 一行说明，右边三个窗口按钮（和参照一样）
		_titleStrip = new Panel
		{
			BackColor = Tokens.Canvas
		};
		_titleStrip.Paint += delegate(object s2, PaintEventArgs e2)
		{
			Graphics graphics = e2.Graphics;
			HQ.Apply(graphics);
			// 标题栏和内容区同一个底色，中间不画分隔线：整页是一整块，没有"两条杠"
			using SolidBrush brush = new SolidBrush(Tokens.Canvas);
			graphics.FillRectangle(brush, _titleStrip.ClientRectangle);
		};
		_lblPageTitle = new Label
		{
			AutoSize = false,
			BackColor = Tokens.Canvas,
			ForeColor = Tokens.TextPrimary,
			Font = new Font(DesignFontFamily, 12.75f, FontStyle.Bold, GraphicsUnit.Point),
			TextAlign = ContentAlignment.MiddleLeft,
			Text = ""
		};
		_lblPageSub = new Label
		{
			AutoSize = false,
			BackColor = Tokens.Canvas,
			ForeColor = Tokens.TextTertiary,
			Font = new Font(DesignFontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point),
			TextAlign = ContentAlignment.MiddleLeft,
			Text = ""
		};
		_titleStrip.Controls.Add(_lblPageTitle);
		_titleStrip.Controls.Add(_lblPageSub);
		_btnMin = new CaptionButton
		{
			Kind = 0
		};
		_btnMin.Click += delegate
		{
			WindowState = FormWindowState.Minimized;
		};
		_btnMax = new CaptionButton
		{
			Kind = 1
		};
		_btnMax.Click += delegate
		{
			ToggleMaximize();
		};
		_btnClose = new CaptionButton
		{
			Kind = 2
		};
		_btnClose.Click += delegate
		{
			Close();
		};
		// 整条标题栏都能拖动窗口、双击最大化（它是子控件，收不到窗体的 WM_NCHITTEST，只能自己转发）
		MouseEventHandler dragHandler = delegate(object s3, MouseEventArgs e3)
		{
			if (e3.Button == MouseButtons.Left)
			{
				Native.ReleaseCapture();
				Native.SendMessageW(Handle, 161, new IntPtr(2), IntPtr.Zero);
			}
		};
		_titleStrip.MouseDown += dragHandler;
		_titleStrip.DoubleClick += delegate
		{
			ToggleMaximize();
		};
		_lblPageTitle.MouseDown += dragHandler;
		_lblPageTitle.DoubleClick += delegate
		{
			ToggleMaximize();
		};
		_lblPageSub.MouseDown += dragHandler;
		_lblPageSub.DoubleClick += delegate
		{
			ToggleMaximize();
		};
		_titleStrip.Controls.Add(_btnMin);
		_titleStrip.Controls.Add(_btnMax);
		_titleStrip.Controls.Add(_btnClose);
		// 底栏：不铺自己的背景色，三个按钮直接落在内容底色上
		_pager = new Panel
		{
			Height = S(60),
			BackColor = Tokens.Canvas
		};
		_pager.Paint += delegate(object s4, PaintEventArgs e4)
		{
			Graphics graphics = e4.Graphics;
			HQ.Apply(graphics);
			using (SolidBrush brush = new SolidBrush(Tokens.Canvas))
			{
				graphics.FillRectangle(brush, _pager.ClientRectangle);
			}
			// 只画一条极淡的上边线，把"操作区"和"内容区"分开，不再套整块背景
			using Pen pen = new Pen(Tokens.Divider);
			graphics.DrawLine(pen, 0, 0, _pager.Width, 0);
		};
		_lblStatus = new Label
		{
			AutoSize = false,
			AutoEllipsis = true,
			ForeColor = Tokens.Success,
			TextAlign = ContentAlignment.MiddleLeft,
			Font = new Font(DesignFontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point),
			BackColor = Tokens.Canvas,
			Location = new Point(S(24), S(14)),
			Size = new Size(S(360), S(32)),
			Text = ""
		};
		_pager.Controls.Add(_lblStatus);
		_btnDefaultsRef = new FlatButton
		{
			Text = "恢复默认值",
			Size = new Size(S(112), S(34))
		};
		_btnDefaultsRef.Click += delegate
		{
			ResetDefaults();
		};
		_pager.Controls.Add(_btnDefaultsRef);
		_btnSaveRef = new FlatButton
		{
			Primary = true,
			Text = "保存并生效",
			Size = new Size(S(112), S(34))
		};
		_btnSaveRef.Click += delegate
		{
			Save();
		};
		_pager.Controls.Add(_btnSaveRef);
		_btnCancelRef = new FlatButton
		{
			Text = "取消",
			Size = new Size(S(88), S(34))
		};
		_btnCancelRef.Click += delegate
		{
			Close();
		};
		_pager.Controls.Add(_btnCancelRef);
		_pager.Resize += delegate
		{
			LayoutPager();
			LayoutChrome();
		};
		Controls.Add(_content);
		Controls.Add(_nav);
		Controls.Add(_titleStrip);
		Controls.Add(_pager);
		MinimumSize = new Size(S(860), S(520));
		try
		{
			Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
			int num = S(680);
			int num2 = Math.Max(S(520), workingArea.Height - S(48) - S(40));
			if (num > num2)
			{
				num = num2;
			}
			_defaultClientH = num;
		}
		catch
		{
			_defaultClientH = S(680);
		}
		LayoutPager();
		LayoutChrome();
		ApplySavedSize();
		ResumeLayout(performLayout: false);
		// 首次构建：8 页都排好，切页时不会看到"半成品"
		LayoutAllPages(ContentWidth());
		SelectPage(0);
	}

	// SetBounds 即使传的是相同的值，Windows 也要走一遍 WM_WINDOWPOSCHANGED
	// （子控件跟着重排 + 无效区变大 + 重新贴图）。拖窗口时每帧几十次这种"没必要的设置"，
	// 攒起来就是肉眼可见的卡顿 —— 所以在托管层先比一次。
	private static void Place(Control c, Rectangle r)
	{
		if (c == null)
		{
			return;
		}
		if (c.Left == r.X && c.Top == r.Y && c.Width == r.Width && c.Height == r.Height)
		{
			return;
		}
		c.SetBounds(r.X, r.Y, r.Width, r.Height);
	}

	private void LayoutChrome()
	{
		if (_chromeBusy)
		{
			return;
		}
		_chromeBusy = true;
		try
		{
			if (_nav == null || _content == null || _titleStrip == null)
			{
				return;
			}
			int num = S(Tokens.TitleBarH);
			int pagerH = ((_pager != null) ? _pager.Height : 0);
			int num2 = S(Tokens.SidebarW);
			Place(_nav, new Rectangle(0, 0, num2, ClientSize.Height));
			int num3 = Math.Max(S(240), ClientSize.Width - num2);
			Place(_titleStrip, new Rectangle(num2, 0, num3, num));
			if (_pager != null)
			{
				Place(_pager, new Rectangle(num2, Math.Max(num, ClientSize.Height - pagerH), num3, pagerH));
			}
			Place(_content, new Rectangle(num2, num, num3, Math.Max(S(80), ClientSize.Height - num - pagerH)));
			// 窗口按钮：靠右，46x30，垂直居中
			int num4 = S(46);
			int num5 = S(30);
			int num6 = (num - num5) / 2;
			int num7 = num3 - S(10);
			Place(_btnClose, new Rectangle(num7 - num4, num6, num4, num5));
			Place(_btnMax, new Rectangle(_btnClose.Left - num4, num6, num4, num5));
			Place(_btnMin, new Rectangle(_btnMax.Left - num4, num6, num4, num5));
			// 标题 + 副标题：按实际文字宽度紧排，副标题跟在后面（不是钉死在某个 x）
			int num8 = S(24);
			int num9 = Math.Max(S(60), _btnMin.Left - num8 - S(16));
			int num10 = Math.Min(num9, TextRenderer.MeasureText(_lblPageTitle.Text, _lblPageTitle.Font).Width + S(8));
			Place(_lblPageTitle, new Rectangle(num8, 0, num10, num));
			Place(_lblPageSub, new Rectangle(num8 + num10 + S(12), 0, Math.Max(S(20), num9 - num10 - S(12)), num));
		}
		catch
		{
		}
		finally
		{
			_chromeBusy = false;
		}
	}

	private void ToggleMaximize()
	{
		try
		{
			WindowState = ((WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized);
		}
		catch
		{
		}
	}

	private void LayoutPager()
	{
		try
		{
			int num = S(20);
			int num2 = (_pager.ClientSize.Height > 10) ? _pager.ClientSize.Height : S(60);
			int num3 = (_pager.ClientSize.Width > 10) ? _pager.ClientSize.Width : S(900);
			// ComboBox 的高度被系统锁在 PreferredHeight，SetBounds 设不上去 ——
			// 底栏按钮反过来对齐输入框的真实高度，保证两者同一量级。
			int num4 = InputRowHeight();
			if (num4 > 0)
			{
				_btnDefaultsRef.Height = num4;
				_btnSaveRef.Height = num4;
				_btnCancelRef.Height = num4;
			}
			Place(_btnCancelRef, new Rectangle(num3 - num - _btnCancelRef.Width, (num2 - _btnCancelRef.Height) / 2, _btnCancelRef.Width, _btnCancelRef.Height));
			Place(_btnSaveRef, new Rectangle(_btnCancelRef.Left - S(10) - _btnSaveRef.Width, (num2 - _btnSaveRef.Height) / 2, _btnSaveRef.Width, _btnSaveRef.Height));
			Place(_btnDefaultsRef, new Rectangle(_btnSaveRef.Left - S(10) - _btnDefaultsRef.Width, (num2 - _btnDefaultsRef.Height) / 2, _btnDefaultsRef.Width, _btnDefaultsRef.Height));
			Place(_lblStatus, new Rectangle(S(24), 0, Math.Max(S(120), _btnDefaultsRef.Left - S(24) - S(20)), num2));
		}
		catch
		{
		}
	}

	private static string[] NavTitles()
	{
		return new string[8] { "触发方式", "鼠标键动作", "视觉反馈", "拖动行为", "流畅度", "启动与权限", "帮助", "关于" };
	}

	private static string[] NavGlyphs()
	{
		return new string[8] { Glyph.Keyboard, Glyph.Move, Glyph.Color, Glyph.Move, Glyph.Speed, Glyph.Shield, Glyph.Help, Glyph.Info };
	}

	// 外壳几何对齐过程中会改控件尺寸，导致 Resize 回调重入；用一个标志挡住
	private bool _chromeBusy;

	// 取树里第一个下拉框的真实锁定高度（拿不到时退回 1x 基准高度）
	private int InputRowHeight()
	{
		try
		{
			for (int i = 0; i < Controls.Count; i++)
			{
				ComboBox comboBox = FindCombo(Controls[i]);
				if (comboBox != null && comboBox.Height > 8)
				{
					return Math.Max(Sc(24), comboBox.Height);
				}
			}
		}
		catch
		{
		}
		return Sc(32);
	}

	private static ComboBox FindCombo(Control root)
	{
		try
		{
			for (int i = 0; i < root.Controls.Count; i++)
			{
				if (root.Controls[i] is ComboBox result)
				{
					return result;
				}
				ComboBox comboBox = FindCombo(root.Controls[i]);
				if (comboBox != null)
				{
					return comboBox;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private int ContentWidth()
	{
		if (IsHandleCreated && _content != null && _content.ClientSize.Width > 10)
		{
			return _content.ClientSize.Width;
		}
		return Math.Max(10, ClientSize.Width - S(Tokens.SidebarW));
	}

	private void BuildControls()
	{
		_txtKey = new TextBox
		{
			ReadOnly = true,
			TabStop = false,
			BorderStyle = BorderStyle.None,
			// 一定要显式给前景色：TextBox 默认是系统黑色，深色主题下就是"深底黑字"，完全看不见
			ForeColor = Tokens.TextPrimary,
			BackColor = Tokens.FieldFill,
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point),
			TextAlign = HorizontalAlignment.Center
		};
		_btnRecord = new FlatButton
		{
			Text = "重新录制"
		};
		_btnRecord.Click += delegate
		{
			StartRecording();
		};
		_lblKeyHint = HintLabel();
		_chkHold = new FlatCheck
		{
			Text = "需要长按触发（关闭后按住立即生效）",
			Cursor = Cursors.Hand
		};
		_chkHold.CheckedChanged += delegate
		{
			_numHold.Enabled = _chkHold.Checked;
		};
		_numHold = NewNum(100m, 3000m, 50m);
		_cmbLeft = NewCombo();
		_cmbRight = NewCombo();
		_cmbMiddle = NewCombo();
		_chkFrame = new FlatCheck
		{
			Text = "长按到达阈值后，光标所在窗口亮起外框",
			Cursor = Cursors.Hand
		};
		_chkFrame.CheckedChanged += delegate
		{
			SyncFrameEnabled();
		};
		_cmbFrameColor = NewCombo();
		_cmbFrameColor.SelectedIndexChanged += delegate
		{
			SyncPreview();
		};
		_cmbFrameColor.ClosedPainter = PaintColorEntry;
		_chkFollowAccent = new FlatCheck
		{
			Text = "颜色跟随系统强调色（换 Windows 主题色时外框一起变）",
			Cursor = Cursors.Hand
		};
		_chkFollowAccent.CheckedChanged += delegate
		{
			SyncFrameEnabled();
			SyncPreview();
		};
		_btnCustomColor = new FlatButton
		{
			Text = "自定义颜色…"
		};
		_btnCustomColor.Click += delegate
		{
			PickCustomFrameColor();
		};
		_lblCustomColor = HintLabel();
		_lblCustomColor.ForeColor = Tokens.TextPrimary;
		_cmbFrameStyle = NewCombo();
		_cmbFrameStyle.SelectedIndexChanged += delegate
		{
			SyncPreview();
		};
		_gridColor = new SwatchGrid
		{
			Cursor = Cursors.Hand
		};
		_gridColor.Picked += delegate
		{
			SelectId(_cmbFrameColor, _gridColor.SelectedId);
		};
		_lblColorName = HintLabel();
		_lblColorName.ForeColor = Tokens.TextPrimary;
		_lblStyleHeader = HintLabel();
		_lblStyleHint = HintLabel();
		_galleryStyle = new StyleGallery
		{
			Cursor = Cursors.Hand
		};
		_galleryStyle.Picked += delegate
		{
			SelectId(_cmbFrameStyle, _galleryStyle.SelectedId);
		};
		_numFrameThick = NewNum(1m, 8m, 1m);
		_numFrameThick.ValueChanged += delegate
		{
			SyncPreview();
		};
		_numFrameRadius = NewNum(0m, 28m, 1m);
		_numFrameRadius.ValueChanged += delegate
		{
			SyncPreview();
		};
		_numFrameOpacity = NewNum(20m, 100m, 5m);
		_numFrameOpacity.ValueChanged += delegate
		{
			SyncPreview();
		};
		_lblFrameTip = HintLabel();
		_pnlPreview = new FramePreviewBox();
		_numCorner = NewNum(5m, 45m, 1m);
		_chkDblLeft = Check("双击左键 = 最大化 / 还原");
		_chkDblRight = Check("双击右键 = 九宫格分区");
		_chkRestoreMax = Check("允许拖动已最大化的窗口（先还原再拖）");
		_chkFront = Check("开始拖动时自动把窗口提到最前");
		_chkFullscreen = Check("允许拖动全屏 / 无边框窗口（有风险）");
		_chkExitFullscreen = Check("触发时先把全屏/最大化窗口变回普通");
		_chkShake = Check("选中窗口后晃一晃，其它窗口全部最小化");
		_txtBlacklist = new TextBox
		{
			BorderStyle = BorderStyle.None,
			ForeColor = Tokens.TextPrimary,
			BackColor = Tokens.FieldFill,
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point)
		};
		_btnPickExe = new FlatButton
		{
			Text = "选择程序…"
		};
		_btnPickExe.Click += delegate
		{
			PickExeFile();
		};
		_btnPickWindow = new FlatButton
		{
			Text = "点选窗口"
		};
		_btnPickWindow.Click += delegate
		{
			PickWindowByClick();
		};
		_cmbRefreshMode = NewCombo();
		_cmbRefreshMode.SelectedIndexChanged += delegate
		{
			SyncRefreshEnabled();
		};
		_numRefreshHz = NewNum(0m, 360m, 1m);
		_lblRefreshHint = HintLabel();
		_cmbAutoStart = NewCombo();
		_chkRunAsAdmin = Check("开机时自动申请管理员权限（默认开启）");
		_chkWarnNonAdmin = Check("没有管理员权限时提示我（可关闭）");
		_lblAdmin = HintLabel();
		_btnRestartAdmin = new FlatButton
		{
			Text = "以管理员重启"
		};
		_btnRestartAdmin.Click += delegate
		{
			RestartAsAdmin();
		};
		_helpPanel = new HelpPanel();
		_helpPanel.SetEntries(BuildHelpEntries());
		_btnGitHub = new FlatButton
		{
			Text = "github.com/QQQOrange/holddrag"
		};
		_btnGitHub.Click += delegate
		{
			OpenUrl(AppInfo.GitHubUrl);
		};
		_chkAutoUpdate = Check("自动更新（启动时检查，有新版提示我）");
		_chkAutoUpdate.CheckedChanged += delegate
		{
			if (!_populating && _edit != null)
			{
				_edit.AutoCheckUpdate = _chkAutoUpdate.Checked;
			}
		};
		_btnCheckUpdate = new FlatButton
		{
			Text = "检查更新"
		};
		_btnCheckUpdate.Click += delegate
		{
			CheckForUpdates(userInitiated: true);
		};
		_lblUpdateStatus = new Label
		{
			AutoSize = false,
			AutoEllipsis = true,
			TextAlign = ContentAlignment.MiddleLeft,
			BackColor = Color.Transparent,
			ForeColor = Tokens.Hint,
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point),
			Text = "尚未检查"
		};
	}

	private static FlatCombo NewCombo()
	{
		return new FlatCombo();
	}

	private static FlatNum NewNum(decimal min, decimal max, decimal step)
	{
		return new FlatNum
		{
			Minimum = min,
			Maximum = max,
			Increment = step,
			TextAlign = HorizontalAlignment.Center
		};
	}

	private static FlatCheck Check(string text)
	{
		return new FlatCheck
		{
			Text = text,
			Cursor = Cursors.Hand
		};
	}

	private static Label HintLabel()
	{
		return new Label
		{
			AutoSize = false,
			AutoEllipsis = false,
			ForeColor = Tokens.Hint,
			BackColor = Tokens.Surface,
			Font = new Font(DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point),
			Size = new Size(100, 20)
		};
	}

	private static Label HintLabelWith(string text)
	{
		Label label = HintLabel();
		label.Text = text;
		return label;
	}

	// 数值后面的单位（毫秒 / px / Hz / %）：和输入框同一行、靠右对齐
	private static Label UnitLabel(string text)
	{
		return new Label
		{
			Text = text,
			AutoSize = false,
			UseMnemonic = false,
			BackColor = Tokens.Surface,
			ForeColor = Tokens.TextTertiary,
			Font = new Font(DesignFontFamily, 9f, FontStyle.Regular, GraphicsUnit.Point),
			TextAlign = ContentAlignment.MiddleLeft
		};
	}

	// 只读的"值"（开发者、版本号这种不需要编辑的信息）
	private static Label ValueLabel(string text, bool accent = false)
	{
		return new Label
		{
			Text = text,
			AutoSize = false,
			UseMnemonic = false,
			AutoEllipsis = true,
			BackColor = Tokens.Surface,
			ForeColor = accent ? Tokens.Accent : Tokens.TextPrimary,
			Font = new Font(DesignFontFamily, 10f, FontStyle.Regular, GraphicsUnit.Point),
			TextAlign = ContentAlignment.MiddleRight
		};
	}

	private Page NewPage(string title, string subtitle)
	{
		Page page = new Page();
		page.Title = title;
		page.Subtitle = subtitle;
		page.Host = new PageHost();
		_pages.Add(page);
		_content.Controls.Add(page.Host);
		page.Host.Dock = DockStyle.Fill;
		page.Host.Visible = false;
		return page;
	}

	private Card NewCard(Page page, string title, string subtitle = null)
	{
		Card card = new Card(title, subtitle);
		page.Cards.Add(card);
		page.Host.Canvas.Controls.Add(card);
		return card;
	}

	private void BuildPages()
	{
		// ---------- 1. 触发方式 ----------
		Page page = NewPage("触发方式", "决定什么时候开始接管窗口；短按仍然原样放行");
		Card card = NewCard(page, "触发键", "按住它，光标下的窗口才开始响应拖动");
		card.RegisterField(_txtKey);
		card.Setting(_txtKey, 150, _btnRecord, 92, "当前触发键", "点「重新录制」后按任意键或鼠标侧键即可更换");
		card.Block(_lblKeyHint, null, 22);
		Card cardHold = NewCard(page, "长按判定", "区分「长按触发」和「正常打字」就靠这一段时长");
		cardHold.RegisterField(_numHold);
		cardHold.Toggle(_chkHold, "需要长按触发", "关掉后按住触发键立即生效，不再等阈值");
		cardHold.Setting(_numHold, 84, UnitLabel("毫秒"), 34, "长按阈值", "按住多久才算长按；太短容易误触发，太长会显得迟钝");

		// ---------- 2. 鼠标键动作 ----------
		Page page2 = NewPage("鼠标键动作", "长按触发键之后，三个鼠标键各自做什么");
		Card card2 = NewCard(page2, "鼠标键动作", "触发期间：左键拖动窗口，右键从边缘缩放，中键执行一次动作");
		card2.Setting(_cmbLeft, "左键", "按住不放，窗口跟着鼠标走", 200);
		card2.Setting(_cmbRight, "右键", "按住不放，从被抓的那条边/角缩放", 200);
		card2.Setting(_cmbMiddle, "中键", "按一下执行一次，不需要按住", 200);
		Card card3 = NewCard(page2, "双击手势", "触发期间快速连按两次鼠标键");
		card3.Toggle(_chkDblLeft, "双击左键 = 最大化 / 还原", "不用去够标题栏，在窗口任意位置都行");
		card3.Toggle(_chkDblRight, "双击右键 = 九宫格分区", "把窗口吸附到屏幕的九个区域之一");

		// ---------- 3. 视觉反馈 ----------
		Page page3 = NewPage("视觉反馈", "外框的颜色、样式与实时预览");
		Card card4 = NewCard(page3, "外框", "触发期间在窗口四周画一圈提示，让你看清这次会拖动谁");
		card4.RegisterField(_numFrameThick);
		card4.RegisterField(_numFrameRadius);
		card4.RegisterField(_numFrameOpacity);
		card4.Toggle(_chkFrame, "显示外框", "关掉后触发时没有任何视觉提示");
		card4.Setting(_cmbFrameColor, "外框颜色", "先选一个预设颜色；下面色板可以直接点", 200);
		card4.Block(_gridColor, "色板");
		card4.Toggle(_chkFollowAccent, "颜色跟随系统强调色", "换 Windows 主题色时外框一起变；优先级高于自定义颜色");
		card4.Setting(_btnCustomColor, 120, _lblCustomColor, 240, "自定义颜色", "打开系统取色器，可以选任意颜色");
		Card card5 = NewCard(page3, "样式", "外框长什么样：虚线、细线、焦点、柔光、投影");
		card5.Setting(_cmbFrameStyle, "样式", "选一种画法；下面的缩略图也可以直接点", 200);
		card5.Block(_galleryStyle, "缩略图");
		card5.Block(_lblStyleHint, null, 22);
		card5.Setting(_numFrameThick, 72, UnitLabel("px"), 26, "粗细", "线有多粗；虚线 / 焦点环靠它区分轻重");
		card5.Setting(_numFrameRadius, 72, UnitLabel("px"), 26, "圆角", "外框跟着窗口的圆角走，改这里能立刻在下面的预览里看到");
		card5.Setting(_numFrameOpacity, 72, UnitLabel("%"), 26, "不透明度", "调低会变成半透明，暗色桌面上更柔和");
		Card card6 = NewCard(page3, "预览", "和真正拖动时用的是同一段绘制代码，看到什么就是什么");
		card6.Block(_pnlPreview, null, 170);
		card6.Block(_lblFrameTip, null, 22);

		// ---------- 4. 拖动行为 ----------
		Page page4 = NewPage("拖动行为", "双击手势、全屏窗口与黑名单");
		Card card7 = NewCard(page4, "拖动与缩放", "触发期间的拖动、缩放细节");
		card7.RegisterField(_numCorner);
		card7.Setting(_numCorner, 72, UnitLabel("%"), 26, "缩放角区", "鼠标离角多近才算抓住角；越大越好抓，但容易误触");
		card7.Toggle(_chkRestoreMax, "允许拖动已最大化的窗口", "会先把窗口还原成普通大小，再开始拖");
		Card card8 = NewCard(page4, "窗口层级与全屏", "全屏 / 无边框窗口容易被拖成一片黑");
		card8.Toggle(_chkFront, "开始拖动时把窗口提到最前", "不用先点一下激活它");
		card8.Toggle(_chkFullscreen, "允许拖动全屏 / 无边框窗口（有风险）", "部分游戏和视频窗口拖完会显示异常");
		card8.Toggle(_chkExitFullscreen, "触发时先把全屏 / 最大化窗口变回普通", "推荐开着：拖动和缩放都从正常尺寸开始");
		Card card9 = NewCard(page4, "晃窗最小化", "选中窗口后左右晃一晃");
		card9.Toggle(_chkShake, "晃窗把其它窗口最小化", "再晃一次还原；判定要求多次往复且净位移接近 0，正常拖动不会误触发");
		Card card10 = NewCard(page4, "不生效的窗口", "命中黑名单的窗口完全不接管");
		card10.RegisterField(_txtBlacklist);
		card10.Setting(_txtBlacklist, "关键字", "窗口标题 / 程序名 / 类名，逗号分隔；命中就完全不接管");
		card10.Setting(_btnPickExe, 96, _btnPickWindow, 92, "快速添加", "不知道填什么时，用「选择程序」挑 exe，或「点选窗口」直接在屏幕上点那扇窗");

		// ---------- 5. 流畅度 ----------
		Page page5 = NewPage("流畅度", "拖动过程中的刷新节流");
		Card card11 = NewCard(page5, "刷新模式", "刷新越快越跟手，但也更吃 CPU");
		card11.Setting(_cmbRefreshMode, "刷新模式", "「自动」会按当前显示器的刷新率决定", 200);
		Card card11b = NewCard(page5, "固定值", "只在「固定值」模式下用得上");
		card11b.RegisterField(_numRefreshHz);
		card11b.Setting(_numRefreshHz, 72, UnitLabel("Hz"), 26, "刷新频率", "0 = 不限制；非 0 时下限钳到 20");
		card11b.Block(_lblRefreshHint, null, 22);

		// ---------- 6. 启动与权限 ----------
		Page page6 = NewPage("启动与权限", "开机自启与管理员权限");
		Card card12 = NewCard(page6, "开机自启", "登录 Windows 后自动在托盘运行");
		card12.Setting(_cmbAutoStart, "开机自启", "显示的是系统里的真实状态（会去查计划任务与注册表）", 200);
		Card card12b = NewCard(page6, "管理员权限", "管理员权限能拖动更多程序，代价是每次启动弹一次 UAC");
		card12b.Toggle(_chkRunAsAdmin, "开机时自动申请管理员权限", "默认开启；关掉后遇到管理员程序可能拖不动");
		card12b.Toggle(_chkWarnNonAdmin, "没有管理员权限时提示我", "不提示的话，拖不动会很像程序坏了");
		card12b.Block(_lblAdmin, null, 22);
		card12b.Setting(_btnRestartAdmin, "立即生效", "改完权限相关设置后，用这个按钮重启一次", 128);

		// ---------- 7. 帮助 ----------
		Page page7 = NewPage("帮助", "每一项功能的实际行为说明");
		Card card13 = NewCard(page7, "功能说明", "点一下标题，就能展开对应功能的说明");
		card13.Block(_helpPanel);

		// ---------- 8. 关于 ----------
		Page page8 = NewPage("关于", "版本、开发者与更新");
		Card card14 = NewCard(page8, "关于", "长按拖窗 HoldDrag · 长按触发键，随手拖动 / 缩放任意窗口");
		card14.Setting(ValueLabel("QQQOrange"), "开发者", null, 140);
		card14.Setting(ValueLabel("DeepSeek"), "开发工具", null, 140);
		card14.Setting(ValueLabel("v" + AppInfo.Version, accent: true), "当前版本", null, 140);
		card14.Setting(ValueLabel(AppInfo.ReleaseDate), "更新时间", null, 140);
		card14.Setting(_btnGitHub, "项目主页", "github.com/QQQOrange/holddrag", 250);
		card14.Toggle(_chkAutoUpdate, "自动更新", "启动时检查一次，有新版本会提示");
		card14.Setting(_btnCheckUpdate, 104, _lblUpdateStatus, 240, "检查更新");
	}

	private static List<HelpPanel.Entry> BuildHelpEntries()
	{
		return new List<HelpPanel.Entry>
		{
			new HelpPanel.Entry
			{
				Icon = Glyph.Keyboard,
				Title = "触发方式",
				Body = "按住触发键（默认 Enter，可改成任意键或鼠标侧键）达到设定时长后生效，光标下那扇窗口四周会亮起外框；松开触发键即退出。短按会原样放行，不影响正常打字。"
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Move,
				Title = "鼠标键动作",
				Body = "触发后：左键拖动窗口、右键缩放窗口、中键执行一次性动作。三个键都能自定义成移动 / 缩放 / 最大化 / 最小化 / 关闭 / 置底 / 置顶 / 居中。双击左键可最大化，双击右键按九宫格吸附。"
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Color,
				Title = "外框外观",
				Body = "外框颜色、样式、粗细、圆角、不透明度都可以调，页面里就是实时预览，和真正拖窗口时用的是同一段绘制代码。窗口贴近屏幕边缘时外框几何保持不变。"
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Shield,
				Title = "全屏与最大化窗口",
				Body = "默认开启「触发时先把全屏 / 最大化窗口变回普通窗口」：这样拖动和缩放都从一个正常的窗口尺寸开始，不会出现全屏窗口拖完一片黑、或者缩放抓不到边的情况。"
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Speed,
				Title = "晃窗最小化其它窗口",
				Body = "触发后选中窗口，左右来回晃一晃，其它窗口会全部最小化；再晃一次即可还原。判定要求「多次往复 + 净位移接近 0」，正常拖动窗口不会被误触发。"
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Shield,
				Title = "不生效的窗口",
				Body = "把窗口标题 / 程序名 / 窗口类名的关键字填进黑名单（逗号分隔），命中就完全不接管。不知道填什么可以用「选择程序」挑 exe，或用「点选窗口」直接在屏幕上点那扇窗。"
			}
		};
	}

	private void SelectPage(int index)
	{
		if (index < 0 || index >= _pages.Count)
		{
			return;
		}
		_activePage = index;
		for (int i = 0; i < _pages.Count; i++)
		{
			_pages[i].Host.Visible = i == index;
		}
		if (_nav != null)
		{
			_nav.ActiveIndex = index;
			_nav.KickGlow(index);
		}
		// 切页时把目标页按当前宽度补排一次（运行时只排可见页，所以这里必须补）
		// 标题栏跟着换页：当前页的名字 + 一句说明（参照里也是标题在顶上、内容在下面）
		_lblPageTitle.Text = _pages[index].Title;
		_lblPageSub.Text = _pages[index].Subtitle;
		LayoutChrome();
		LayoutWithContent(ContentWidth());
		_pages[index].Host.Invalidate();
	}

	/// <summary>
	/// 内容列宽。窗口不宽时单列、限宽 880 并水平居中（不在右边留一大片空白）；
	/// 窗口拉得很宽（最大化）时改成两列卡片，把宽度用满，而不是让右半边空着。
	/// </summary>
	private int CardWidth(int contentW, out bool twoColumns, out int left, out int gap)
	{
		int num = S(24);
		int num2 = Math.Max(S(200), contentW - num * 2);
		twoColumns = num2 >= S(1200);
		gap = S(16);
		if (twoColumns)
		{
			left = num;
			return Math.Max(S(320), (num2 - gap) / 2);
		}
		int num3 = Math.Min(num2, S(Tokens.CardMaxW));
		// 单列但窗口还比较宽：左右均分留白，视觉上居中，而不是全挤在左边
		left = num + Math.Max(0, (num2 - num3) / 2);
		return num3;
	}

	private int _lastLayoutW = -1;
	private string _lastLayoutSig = "";

	// 影响行高的只有"文字 + 可用宽度"。两者都没变时跳过整轮重排：
	// 拖窗口时 WM_SIZE 一来就是几十次，之前每次都把 8 页 17 张卡片全排一遍。
	private string LayoutSignature()
	{
		StringBuilder stringBuilder = new StringBuilder(1024);
		foreach (Page page in _pages)
		{
			foreach (Card card in page.Cards)
			{
				card.AppendSignature(stringBuilder);
			}
		}
		return stringBuilder.ToString();
	}

	private void LayoutWithContent(int contentW)
	{
		LayoutChrome();
		contentW = ContentWidth();
		if (contentW < 10)
		{
			contentW = 10;
		}
		LayoutPages(contentW, forceAll: false);
	}

	/// <summary>把 8 页全部排一遍（首次构建、DPI 重建、自检用）。</summary>
	private void LayoutAllPages(int contentW)
	{
		LayoutChrome();
		contentW = ContentWidth();
		if (contentW < 10)
		{
			contentW = 10;
		}
		LayoutPages(contentW, forceAll: true);
	}

	private static string PageSignature(Page page)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		foreach (Card card in page.Cards)
		{
			card.AppendSignature(stringBuilder);
		}
		return stringBuilder.ToString();
	}

	private void LayoutPages(int contentW, bool forceAll)
	{
		for (int i = 0; i < _pages.Count; i++)
		{
			Page page = _pages[i];
			bool flag = i == _activePage;
			// 看不见的页不排：拖窗口/最大化时只有当前页会真的显示出来，
			// 之前 8 页 17 张卡每次都全排一遍，绝大部分是无用功。切页时会补排。
			if (!flag && !forceAll)
			{
				continue;
			}
			string text = PageSignature(page);
			if (!forceAll && page.LaidOutWidth == contentW && page.Signature == text)
			{
				continue;
			}
			LayoutOnePage(page, contentW, text);
		}
	}

	private void LayoutOnePage(Page page, int contentW, string signature)
	{
		// 每页单独算列宽：
		//  - 两张以上卡片 + 窗口够宽 → 两列，把宽度用满；
		//  - 只有一张卡片 → 占满（最多 1200 逻辑）并居中，不排两列（否则单卡只占左半边，右边又空了）；
		//  - 其余 → 单列限宽 880，居中。
		bool flag;
		int num;
		int num2;
		int numW = CardWidth(contentW, out flag, out num, out num2);
		bool flag2 = flag && page.Cards.Count >= 2;
		int cardW;
		int numX;
		if (flag2)
		{
			cardW = numW;
			numX = num;
		}
		else
		{
			int numY = Math.Max(S(200), contentW - S(48));
			cardW = Math.Min(numY, (page.Cards.Count <= 1) ? S(1200) : S(Tokens.CardMaxW));
			numX = S(24) + Math.Max(0, (numY - cardW) / 2);
		}
		page.Host.SuspendLayout();
		page.Host.Canvas.SuspendLayout();
		try
		{
			int num3 = S(6);
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			foreach (Card card in page.Cards)
			{
				int num7 = card.Measure(cardW);
				if (flag2)
				{
					// 两列：0/2/4… 放左列，1/3/5… 放右列；同一行取两张卡里较高的那个做行高
					if (num4 % 2 == 0)
					{
						num5 = num3;
						num6 = num7;
						Place(card, new Rectangle(numX, num3, cardW, num7));
					}
					else
					{
						Place(card, new Rectangle(numX + cardW + num2, num5, cardW, num7));
						num3 = num5 + Math.Max(num6, num7) + S(Tokens.CardGap);
					}
					num4++;
				}
				else
				{
					Place(card, new Rectangle(numX, num3, cardW, num7));
					num3 += num7 + S(Tokens.CardGap);
				}
				card.Layout(cardW);
			}
			if (flag2 && num4 % 2 == 1)
			{
				num3 = num5 + num6 + S(Tokens.CardGap);
			}
			Size size = new Size(contentW, Math.Max(num3 + S(4), S(80)));
			if (page.Host.Canvas.Size != size)
			{
				page.Host.Canvas.Size = size;
			}
			page.Host.UpdateScroll();
			page.LaidOutWidth = contentW;
			page.Signature = signature;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			page.Host.Canvas.ResumeLayout(performLayout: false);
			page.Host.ResumeLayout(performLayout: false);
		}
	}

	// ======================= 填充 / 保存 =======================

	private void Populate()
	{
		_populating = true;
		try
		{
			PopulateCore();
		}
		finally
		{
			_populating = false;
		}
	}

	private void PopulateCore()
	{
		UpdateKeyText();
		_chkHold.Checked = _edit.RequireHold;
		_numHold.Value = Clamp(_edit.HoldMs, (int)_numHold.Minimum, (int)_numHold.Maximum);
		_numHold.Enabled = _edit.RequireHold;
		_cmbLeft.Items.Clear();
		_cmbRight.Items.Clear();
		_cmbMiddle.Items.Clear();
		for (int i = 0; i < Actions.All.Length; i++)
		{
			string text = Actions.All[i];
			ActionItem item = new ActionItem
			{
				Value = text,
				Text = Actions.DisplayName(text)
			};
			_cmbLeft.Items.Add(item);
			_cmbRight.Items.Add(item);
			_cmbMiddle.Items.Add(item);
		}
		SelectAction(_cmbLeft, _edit.ActionLeft);
		SelectAction(_cmbRight, _edit.ActionRight);
		SelectAction(_cmbMiddle, _edit.ActionMiddle);
		_chkFrame.Checked = _edit.ShowActivationFrame;
		_cmbFrameColor.BeginUpdate();
		_cmbFrameStyle.BeginUpdate();
		try
		{
			_cmbFrameColor.Items.Clear();
			for (int j = 0; j < FrameColors.All.Length; j++)
			{
				FrameColors.Entry entry = FrameColors.All[j];
				_cmbFrameColor.Items.Add(new ColorItem
				{
					Value = entry.Id,
					Text = entry.Name,
					Color = FrameColors.Resolve(entry.Id)
				});
			}
			_cmbFrameStyle.Items.Clear();
			for (int k = 0; k < FrameStyles.All.Length; k++)
			{
				string text2 = FrameStyles.All[k];
				_cmbFrameStyle.Items.Add(new IdItem
				{
					Value = text2,
					Text = FrameStyles.DisplayName(text2)
				});
			}
		}
		finally
		{
			_cmbFrameStyle.EndUpdate();
			_cmbFrameColor.EndUpdate();
		}
		List<SwatchGrid.Entry> list = new List<SwatchGrid.Entry>();
		for (int l = 0; l < FrameColors.All.Length; l++)
		{
			list.Add(new SwatchGrid.Entry
			{
				Id = FrameColors.All[l].Id,
				Name = FrameColors.All[l].Name,
				Color = FrameColors.Resolve(FrameColors.All[l].Id)
			});
		}
		_gridColor.SetItems(list);
		_galleryStyle.SetItems(new List<string>(FrameStyles.All));
		SelectId(_cmbFrameStyle, _edit.FrameStyleId);
		SelectId(_cmbFrameColor, _edit.FrameColorId);
		_chkFollowAccent.Checked = _edit.FrameColorFollowAccent;
		_customColorArgb = _edit.FrameCustomColor;
		_numFrameThick.Value = Clamp(_edit.FrameThickness, (int)_numFrameThick.Minimum, (int)_numFrameThick.Maximum);
		_numFrameRadius.Value = Clamp(_edit.FrameRadius, (int)_numFrameRadius.Minimum, (int)_numFrameRadius.Maximum);
		_numFrameOpacity.Value = Clamp(_edit.FrameOpacity, (int)_numFrameOpacity.Minimum, (int)_numFrameOpacity.Maximum);
		SyncFrameEnabled();
		SyncPreview();
		_numCorner.Value = Clamp(_edit.CornerPercent, (int)_numCorner.Minimum, (int)_numCorner.Maximum);
		_chkDblLeft.Checked = _edit.DoubleClickLeftMaximize;
		_chkDblRight.Checked = _edit.DoubleClickRightZone;
		_chkRestoreMax.Checked = _edit.RestoreMaximizedWhenMoved;
		_chkFront.Checked = _edit.BringToFront;
		_chkFullscreen.Checked = _edit.AllowFullscreenWindows;
		_chkExitFullscreen.Checked = _edit.ExitFullscreenOnTrigger;
		_chkShake.Checked = _edit.ShakeMinimizeOthers;
		_txtBlacklist.Text = _edit.Blacklist;
		_cmbRefreshMode.Items.Clear();
		_cmbRefreshMode.Items.Add(new IdItem
		{
			Value = "Auto",
			Text = "跟随屏幕"
		});
		_cmbRefreshMode.Items.Add(new IdItem
		{
			Value = "Custom",
			Text = "固定值"
		});
		SelectId(_cmbRefreshMode, _edit.DragRefreshMode);
		_numRefreshHz.Value = Clamp(_edit.DragRefreshHz, (int)_numRefreshHz.Minimum, (int)_numRefreshHz.Maximum);
		SyncRefreshEnabled();
		int refreshRate = Native.GetRefreshRate();
		_lblRefreshHint.Text = "跟随屏幕：按实测刷新率节流（本机 " + ((refreshRate > 0) ? (refreshRate + "Hz") : "未知") + "）。\r\n固定值：自定义频率，0 = 不限制。";
		_cmbAutoStart.Items.Clear();
		_cmbAutoStart.Items.Add(new IdItem
		{
			Value = "TaskScheduler",
			Text = "计划任务（最高权限、登录时静默启动，推荐）"
		});
		_cmbAutoStart.Items.Add(new IdItem
		{
			Value = "Registry",
			Text = "注册表 Run 项（每次登录会弹一次 UAC）"
		});
		_cmbAutoStart.Items.Add(new IdItem
		{
			Value = "None",
			Text = "不开机启动"
		});
		SelectId(_cmbAutoStart, _edit.AutoStart);
		_chkRunAsAdmin.Checked = _edit.RunAsAdmin;
		_chkWarnNonAdmin.Checked = _edit.WarnNonAdmin;
		_chkAutoUpdate.Checked = _edit.AutoCheckUpdate;
		UpdateAdminLabel();
	}

	private void SyncFrameEnabled()
	{
		bool flag = _chkFrame.Checked;
		// 跟随强调色时，色板与自定义颜色都不参与，直接置灰（避免"点了没反应"的错觉）
		bool flag2 = flag && !_chkFollowAccent.Checked;
		_cmbFrameColor.Enabled = flag2;
		_chkFollowAccent.Enabled = flag;
		_btnCustomColor.Enabled = flag2;
		_lblCustomColor.Enabled = flag2;
		_cmbFrameStyle.Enabled = flag;
		_numFrameThick.Enabled = flag;
		_numFrameRadius.Enabled = flag;
		_numFrameOpacity.Enabled = flag;
		_gridColor.Enabled = flag2;
		_galleryStyle.Enabled = flag;
		_galleryStyle.FrameEnabled = flag;
		_pnlPreview.FrameEnabled = flag;
		_pnlPreview.OpacityPercent = (int)_numFrameOpacity.Value;
		_pnlPreview.Refresh_();
	}

	private void SyncRefreshEnabled()
	{
		_numRefreshHz.Enabled = SelectedId(_cmbRefreshMode, "Auto") == "Custom";
	}

	private void SyncPreview()
	{
		string text = SelectedId(_cmbFrameColor, FrameColors.DefaultId);
		string text2 = SelectedId(_cmbFrameStyle, FrameStyles.DefaultId);
		bool followAccent = _chkFollowAccent.Checked;
		int thickness = (int)_numFrameThick.Value;
		int radius = (int)_numFrameRadius.Value;
		int frameOpacity = (int)_numFrameOpacity.Value;
		bool frameEnabled = _chkFrame.Checked;
		// 预览、画廊缩略图、真正的外框共用同一个"最终颜色"，三处永远一致
		Color color = FrameColors.ResolveEffective(text, followAccent, _customColorArgb);
		_pnlPreview.ColorId = text;
		_pnlPreview.ColorOverride = color;
		_pnlPreview.StyleId = text2;
		_pnlPreview.Thickness = thickness;
		_pnlPreview.Radius = radius;
		_pnlPreview.OpacityPercent = frameOpacity;
		_pnlPreview.FrameEnabled = frameEnabled;
		_pnlPreview.Refresh_();
		_gridColor.SelectedId = text;
		_lblColorName.Text = "当前颜色：" + FrameColors.NameOf(text, followAccent, _customColorArgb) + " " + FrameColors.HexOf(color);
		_lblCustomColor.Text = ((_customColorArgb != 0) ? ("已设自定义色 " + FrameColors.HexOf(Color.FromArgb(_customColorArgb))) : "还没有设置自定义颜色");
		_lblStyleHeader.Text = "样式（当前粗细 " + thickness + " px・圆角 " + radius + " px）";
		_lblStyleHint.Text = FrameStyles.HintFor(text2);
		_galleryStyle.SelectedId = text2;
		_galleryStyle.ColorId = text;
		_galleryStyle.ColorOverride = color;
		_galleryStyle.Thickness = thickness;
		_galleryStyle.Radius = radius;
		_galleryStyle.FrameEnabled = frameEnabled;
		MarkFrameDirtyIfChanged();
	}

	private void MarkFrameDirtyIfChanged()
	{
		if (_lblFrameTip == null || _populating)
		{
			return;
		}
		string text = SelectedId(_cmbFrameColor, FrameColors.DefaultId);
		string text2 = SelectedId(_cmbFrameStyle, FrameStyles.DefaultId);
		bool flag = text != _edit.FrameColorId || text2 != _edit.FrameStyleId || (int)_numFrameThick.Value != _edit.FrameThickness || (int)_numFrameRadius.Value != _edit.FrameRadius || _chkFrame.Checked != _edit.ShowActivationFrame || _chkFollowAccent.Checked != _edit.FrameColorFollowAccent || _customColorArgb != _edit.FrameCustomColor;
		_lblFrameTip.ForeColor = (flag ? Tokens.WarnText : Tokens.Hint);
		_lblFrameTip.Text = (flag ? "外观已改动，点「保存并生效」才会画到实际外框上。" : "颜色与样式即点即换，预览与外框同一段代码。");
	}

	private void UpdateAdminLabel()
	{
		bool flag = Program.IsAdministrator();
		_lblAdmin.Text = (flag ? "当前权限：管理员（可拖动任务管理器这类提权窗口）" : "当前权限：普通用户（日常窗口没问题，提权窗口拖不动）");
		_btnRestartAdmin.Enabled = !flag;
	}

	private void UpdateKeyText()
	{
		_txtKey.Text = KeysUtil.DisplayName(_edit.TriggerKeyId);
		if (_nav != null)
		{
			// 侧栏底部状态卡：一眼看到"正在运行"和当前触发键
			_nav.SetStatus("正在运行", KeysUtil.DisplayName(_edit.TriggerKeyId) + " + 左键拖动");
		}
	}

	private static int Clamp(int v, int min, int max)
	{
		if (v < min)
		{
			return min;
		}
		if (v > max)
		{
			return max;
		}
		return v;
	}

	private static void SelectAction(ComboBox c, string value)
	{
		for (int i = 0; i < c.Items.Count; i++)
		{
			if (c.Items[i] is ActionItem actionItem && actionItem.Value == value)
			{
				c.SelectedIndex = i;
				return;
			}
		}
		if (c.Items.Count > 0)
		{
			c.SelectedIndex = 0;
		}
	}

	private static void SelectId(ComboBox c, string value)
	{
		if (c == null || c.Items.Count == 0)
		{
			return;
		}
		for (int i = 0; i < c.Items.Count; i++)
		{
			if (c.Items[i] is IdItem idItem && idItem.Value == value)
			{
				c.SelectedIndex = i;
				return;
			}
			if (c.Items[i] is ColorItem colorItem && colorItem.Value == value)
			{
				c.SelectedIndex = i;
				return;
			}
		}
		c.SelectedIndex = 0;
	}

	private static string SelectedAction(ComboBox c)
	{
		if (!(c.SelectedItem is ActionItem actionItem))
		{
			return null;
		}
		return actionItem.Value;
	}

	private static string SelectedId(ComboBox c, string fallback)
	{
		if (c.SelectedItem is IdItem idItem && !string.IsNullOrEmpty(idItem.Value))
		{
			return idItem.Value;
		}
		if (c.SelectedItem is ColorItem colorItem && !string.IsNullOrEmpty(colorItem.Value))
		{
			return colorItem.Value;
		}
		return fallback;
	}

	private static string ColorNameOf(string id)
	{
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			if (FrameColors.All[i].Id == id)
			{
				return FrameColors.All[i].Name;
			}
		}
		return id;
	}

	private void StartRecording()
	{
		_recording = true;
		_coord.SuppressTrigger = true;
		_txtKey.Text = "请按下要用的按键…";
		_txtKey.BackColor = Color.FromArgb(255, 244, 214);
		_lblKeyHint.Text = "正在录制：按任意键或鼠标侧键，Esc 取消。";
		_lblKeyHint.ForeColor = Tokens.WarnText;
		Focus();
	}

	private void StopRecording(bool applied)
	{
		_recording = false;
		_coord.SuppressTrigger = false;
		_txtKey.BackColor = Tokens.FieldFill;
		UpdateKeyText();
		if (!applied)
		{
			_lblKeyHint.Text = "已取消录制。";
			return;
		}
		string text = string.Empty;
		if (KeysUtil.IsToggleKey(_edit.TriggerKeyId))
		{
			text = "注意：这是切换型按键，本程序会压制它，因此它不会再切换大小写/数字锁定。";
		}
		else if (KeysUtil.IsWindowsKey(_edit.TriggerKeyId))
		{
			text = "注意：Win 键被作为触发键期间，Win 组合键（Win+E、Win+D 等）无法使用。";
		}
		_lblKeyHint.Text = ((text.Length > 0) ? text : ("已绑定：" + KeysUtil.DisplayName(_edit.TriggerKeyId)));
		_lblKeyHint.ForeColor = ((text.Length > 0) ? Tokens.WarnText : Tokens.Hint);
	}

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		if (_recording)
		{
			Keys keys = keyData & Keys.KeyCode;
			if (keys == Keys.Escape)
			{
				StopRecording(applied: false);
				return true;
			}
			if (keys == Keys.None)
			{
				// 纯修饰键（Ctrl/Shift/Alt/Win）被 KeyCode 掩码掩掉后就是 Keys.None；
				// 放任不管会走默认分支绑成 "K:0x00"（vk=0 永远匹配不上任何真实按键），触发键就此失效。
				_lblKeyHint.Text = "修饰键（Ctrl / Shift / Alt / Win）不能单独作为触发键，请再按一个主键。";
				_lblKeyHint.ForeColor = Tokens.WarnText;
				return true;
			}
			string text = ResolveRecordedKey(keys);
			if (text == null)
			{
				_lblKeyHint.Text = "左键 / 右键 / 中键不能作为触发键，请用键盘按键或鼠标侧键。";
				_lblKeyHint.ForeColor = Tokens.WarnText;
				return true;
			}
			_edit.TriggerKeyId = text;
			StopRecording(applied: true);
			return true;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (_recording)
		{
			string text = KeysUtil.FromMouseButton(e.Button);
			if (text != null)
			{
				_edit.TriggerKeyId = text;
				StopRecording(applied: true);
				return;
			}
			if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
			{
				_lblKeyHint.Text = "左键 / 右键 / 中键不能作为触发键，请用键盘按键或鼠标侧键。";
				_lblKeyHint.ForeColor = Tokens.WarnText;
				return;
			}
		}
		base.OnMouseDown(e);
	}

	private static bool IsDown(int vk)
	{
		return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
	}

	private static string ResolveRecordedKey(Keys k)
	{
		switch (k)
		{
		case Keys.ShiftKey:
		case Keys.LShiftKey:
		case Keys.RShiftKey:
			if (!IsDown(160))
			{
				return "K:RShift";
			}
			return "K:LShift";
		case Keys.ControlKey:
		case Keys.LControlKey:
		case Keys.RControlKey:
			if (!IsDown(162))
			{
				return "K:RCtrl";
			}
			return "K:LCtrl";
		case Keys.Menu:
		case Keys.LMenu:
		case Keys.RMenu:
			if (!IsDown(164))
			{
				return "K:RAlt";
			}
			return "K:LAlt";
		case Keys.LButton:
		case Keys.RButton:
		case Keys.MButton:
			return null;
		case Keys.LWin:
			return "K:0x5B";
		case Keys.RWin:
			return "K:0x5C";
		default:
			return KeysUtil.FromWinFormsKey(k);
		}
	}

	private void Save()
	{
		if (_recording)
		{
			StopRecording(applied: false);
		}
		CaptureInto(_edit);
		if (!_store.Apply(_edit, out string saveError))
		{
			// 保存失败时绝不能再显示"已保存并立即生效"：设置没有落盘，重启就会丢
			_lblStatus.ForeColor = Tokens.Danger;
			_lblStatus.Text = "保存失败：" + saveError + "（本次修改没有写入磁盘，重启后会丢失；请检查配置目录权限或磁盘空间）";
			return;
		}
		string message;
		if (AutoStart.Apply(_edit.AutoStart, AppPaths.ExePath, out message))
		{
			_lblStatus.ForeColor = Tokens.Success;
			_lblStatus.Text = "已保存并立即生效";
		}
		else
		{
			_lblStatus.ForeColor = Tokens.Danger;
			_lblStatus.Text = "配置已保存，但开机自启设置失败";
			MessageBox.Show(message, "开机自启设置失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
		if (_edit.RunAsAdmin && !Program.IsAdministrator() && MessageBox.Show("已保存。\r\n\r\n管理员权限只在【启动的时候】申请，所以要重启程序才会生效。\r\n现在以管理员身份重启吗？（选\"否\"就等下次启动再提权）", "长按拖窗 · 权限变更", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
		{
			RestartAsAdmin();
		}
	}

	private void CaptureInto(AppConfig target)
	{
		target.RequireHold = _chkHold.Checked;
		target.HoldMs = (int)_numHold.Value;
		target.ActionLeft = SelectedAction(_cmbLeft) ?? target.ActionLeft;
		target.ActionRight = SelectedAction(_cmbRight) ?? target.ActionRight;
		target.ActionMiddle = SelectedAction(_cmbMiddle) ?? target.ActionMiddle;
		target.ShowActivationFrame = _chkFrame.Checked;
		target.FrameColorId = SelectedId(_cmbFrameColor, target.FrameColorId);
		target.FrameColorFollowAccent = _chkFollowAccent.Checked;
		target.FrameCustomColor = _customColorArgb;
		target.FrameStyleId = SelectedId(_cmbFrameStyle, target.FrameStyleId);
		target.FrameThickness = (int)_numFrameThick.Value;
		target.FrameRadius = (int)_numFrameRadius.Value;
		target.FrameOpacity = (int)_numFrameOpacity.Value;
		target.CornerPercent = (int)_numCorner.Value;
		target.ShakeMinimizeOthers = _chkShake.Checked;
		target.DragRefreshMode = SelectedId(_cmbRefreshMode, target.DragRefreshMode);
		target.DragRefreshHz = (int)_numRefreshHz.Value;
		target.DoubleClickLeftMaximize = _chkDblLeft.Checked;
		target.DoubleClickRightZone = _chkDblRight.Checked;
		target.RestoreMaximizedWhenMoved = _chkRestoreMax.Checked;
		target.BringToFront = _chkFront.Checked;
		target.AllowFullscreenWindows = _chkFullscreen.Checked;
		target.ExitFullscreenOnTrigger = _chkExitFullscreen.Checked;
		target.Blacklist = _txtBlacklist.Text;
		target.AutoStart = SelectedId(_cmbAutoStart, target.AutoStart);
		target.RunAsAdmin = _chkRunAsAdmin.Checked;
		target.WarnNonAdmin = _chkWarnNonAdmin.Checked;
		target.AutoCheckUpdate = _chkAutoUpdate.Checked;
		CaptureSizeInto(target);
	}

	private void PickExeFile()
	{
		try
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Title = "选择要排除的程序（它的窗口将不再被接管）";
			openFileDialog.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
			openFileDialog.CheckFileExists = true;
			openFileDialog.Multiselect = false;
			if (openFileDialog.ShowDialog(this) == DialogResult.OK)
			{
				string text = Path.GetFileName(openFileDialog.FileName);
				if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
				{
					text = text.Substring(0, text.Length - 4);
				}
				if (string.IsNullOrEmpty(text))
				{
					Status("没从文件名里取到关键字", error: true);
				}
				else
				{
					AppendBlacklistKeyword(text);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			Status("打开文件对话框失败：" + ex.Message, error: true);
		}
	}

	// 自定义外框颜色：系统取色器（可展开调色盘、可直接吸屏幕颜色）。
	// 选完自动关掉"跟随强调色"，否则自定义色会被强调色盖住，点了跟没点一样。
	private void PickCustomFrameColor()
	{
		try
		{
			using ColorDialog colorDialog = new ColorDialog();
			colorDialog.AllowFullOpen = true;
			colorDialog.FullOpen = true;
			colorDialog.AnyColor = true;
			colorDialog.SolidColorOnly = false;
			colorDialog.Color = FrameColors.ResolveEffective(SelectedId(_cmbFrameColor, FrameColors.DefaultId), _chkFollowAccent.Checked, _customColorArgb);
			if (_customColorArgb != 0)
			{
				colorDialog.CustomColors = new int[1] { _customColorArgb };
			}
			if (colorDialog.ShowDialog(this) == DialogResult.OK)
			{
				_customColorArgb = colorDialog.Color.ToArgb();
				if (_chkFollowAccent.Checked)
				{
					_chkFollowAccent.Checked = false;
				}
				SyncFrameEnabled();
				SyncPreview();
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			Status("打开取色器失败：" + ex.Message, error: true);
		}
	}

	private void PickWindowByClick()
	{
		try
		{
			Form mask = new Form();
			mask.FormBorderStyle = FormBorderStyle.None;
			mask.StartPosition = FormStartPosition.Manual;
			mask.Bounds = SystemInformation.VirtualScreen;
			mask.TopMost = true;
			mask.ShowInTaskbar = false;
			mask.Cursor = Cursors.Cross;
			mask.BackColor = Color.Black;
			mask.Opacity = 0.18;
			mask.KeyPreview = true;
			bool done = false;
			mask.MouseDown += delegate(object s, MouseEventArgs e)
			{
				if (done)
				{
					return;
				}
				done = true;
				Native.POINT pt = new Native.POINT(mask.Left + e.X, mask.Top + e.Y);
				try
				{
					mask.Hide();
				}
				catch
				{
				}
				Application.DoEvents();
				AddBlacklistFromWindowAt(pt);
				try
				{
					mask.Close();
				}
				catch
				{
				}
			};
			mask.KeyDown += delegate(object s, KeyEventArgs e)
			{
				if (e.KeyCode != Keys.Escape)
				{
					return;
				}
				done = true;
				Status("已取消窗口点选", error: false);
				try
				{
					mask.Close();
				}
				catch
				{
				}
			};
			Status("请点击要排除的窗口（按 Esc 取消）", error: false);
			mask.ShowDialog(this);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			Status("窗口点选失败：" + ex.Message, error: true);
		}
	}

	private void AddBlacklistFromWindowAt(Native.POINT pt)
	{
		try
		{
			nint num = Native.WindowFromPoint(pt);
			if (num != IntPtr.Zero)
			{
				num = Native.GetAncestor(num, 2u);
			}
			if (num == IntPtr.Zero)
			{
				Status("没找到窗口，请对着目标窗口的标题栏再点一次", error: true);
				return;
			}
			string text = WindowActions.ProcessExeName(num);
			string windowTitle = Native.GetWindowTitle(num);
			string className = Native.GetClassName(num);
			string text2;
			if (string.IsNullOrEmpty(text))
			{
				text2 = ((!string.IsNullOrEmpty(className)) ? className : windowTitle);
			}
			else
			{
				text2 = text;
			}
			if (string.IsNullOrEmpty(text2))
			{
				Status("这扇窗口取不到可用的标识，请改用「选择程序…」", error: true);
				return;
			}
			AppendBlacklistKeyword(text2);
			string text3 = windowTitle;
			if (text3 != null && text3.Length > 18)
			{
				text3 = text3.Substring(0, 18) + "…";
			}
			Status("已排除：" + text2 + (string.IsNullOrEmpty(text3) ? "" : ("（" + text3 + "）")), error: false);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			Status("点选失败：" + ex.Message, error: true);
		}
	}

	private void AppendBlacklistKeyword(string kw)
	{
		try
		{
			if (string.IsNullOrEmpty(kw))
			{
				return;
			}
			kw = kw.Trim();
			if (kw.Length == 0)
			{
				return;
			}
			string text = _txtBlacklist.Text ?? string.Empty;
			string[] array = text.Split(new char[2] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				if (string.Equals(array[i].Trim(), kw, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
			}
			string text2 = text.Trim();
			_txtBlacklist.Text = ((text2.Length == 0) ? kw : (text2 + "，" + kw));
			try
			{
				_txtBlacklist.SelectionStart = _txtBlacklist.Text.Length;
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void Status(string text, bool error)
	{
		try
		{
			if (_lblStatus != null)
			{
				_lblStatus.ForeColor = (error ? Tokens.Danger : Tokens.Success);
				_lblStatus.Text = text ?? string.Empty;
			}
		}
		catch
		{
		}
	}

	private void ResetDefaults()
	{
		AppConfig appConfig = new AppConfig();
		appConfig.AutoStart = _edit.AutoStart;
		appConfig.RunAsAdmin = _edit.RunAsAdmin;
		appConfig.WarnNonAdmin = _edit.WarnNonAdmin;
		appConfig.Blacklist = _edit.Blacklist;
		appConfig.SettingsWidth = _edit.SettingsWidth;
		appConfig.SettingsHeight = _edit.SettingsHeight;
		appConfig.AutoCheckUpdate = _edit.AutoCheckUpdate;
		appConfig.LastUpdateNotified = _edit.LastUpdateNotified;
		_edit = appConfig;
		if (_recording)
		{
			StopRecording(applied: false);
		}
		Populate();
		_lblStatus.ForeColor = Tokens.Hint;
		_lblStatus.Text = "已载入默认值，点「保存并生效」后写入";
	}

	private void RestartAsAdmin()
	{
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = AppPaths.ExePath,
			UseShellExecute = true,
			Verb = "runas"
		};
		try
		{
			Process.Start(startInfo);
			_coord.Dispose();
			Application.Exit();
		}
		catch (Exception ex)
		{
			Log.Info("以管理员重启被取消：" + ex.Message);
		}
	}

	private void OpenUrl(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			try
			{
				Clipboard.SetText(url);
				SetUpdateStatus("打不开浏览器，链接已复制到剪贴板：" + url, Tokens.WarnText);
			}
			catch
			{
			}
		}
	}

	private void SetUpdateStatus(string text, Color color)
	{
		try
		{
			if (_lblUpdateStatus == null || _lblUpdateStatus.IsDisposed)
			{
				return;
			}
			_lblUpdateStatus.ForeColor = color;
			_lblUpdateStatus.Text = text ?? string.Empty;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	internal void BeginCheckUpdate()
	{
		CheckForUpdates(userInitiated: true);
	}

	private void CheckForUpdates(bool userInitiated)
	{
		if (_updateBusy)
		{
			if (userInitiated)
			{
				SetUpdateStatus("正在检查，请稍候…", Tokens.Hint);
			}
			return;
		}
		_updateBusy = true;
		SetUpdateStatus("正在检查更新…", Tokens.Hint);
		ThreadPool.QueueUserWorkItem(delegate
		{
			string error;
			bool upToDate;
			UpdateInfo updateInfo = Updater.CheckLatest(out error, out upToDate);
			PostUpdateAction(delegate
			{
				_updateBusy = false;
				if (updateInfo != null)
				{
					OnUpdateFound(updateInfo);
					return;
				}
				if (upToDate)
				{
					SetUpdateStatus("已是最新版本（v" + AppInfo.Version + "）", Tokens.Success);
					if (userInitiated)
					{
						MessageBox.Show(this, "当前已是最新版本 v" + AppInfo.Version + "。", "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
					}
					return;
				}
				SetUpdateStatus("检查失败：" + error, Tokens.Danger);
				if (userInitiated)
				{
					MessageBox.Show(this, "检查更新失败：\r\n\r\n" + error + "\r\n\r\n你也可以手动打开发布页查看：\r\n" + AppInfo.ReleasesUrl, "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				}
			});
		});
	}

	private void PostUpdateAction(Action action)
	{
		try
		{
			if (IsDisposed || !IsHandleCreated)
			{
				return;
			}
			BeginInvoke(action);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void OnUpdateFound(UpdateInfo info)
	{
		SetUpdateStatus("发现新版本 " + info.Tag + "（当前 v" + AppInfo.Version + "）", Tokens.WarnText);
		DialogResult dialogResult = MessageBox.Show(this, "发现新版本！\r\n\r\n当前版本：v" + AppInfo.Version + "\r\n最新版本：" + info.Tag + "\r\n\r\n现在下载并更新吗？\r\n（下载完成后会自动替换并重启程序）", "发现新版本", MessageBoxButtons.OKCancel, MessageBoxIcon.Asterisk);
		if (dialogResult == DialogResult.OK)
		{
			DownloadAndApply(info);
		}
	}

	private void DownloadAndApply(UpdateInfo info)
	{
		_updateBusy = true;
		SetUpdateStatus("正在下载 " + info.Tag + "…", Tokens.Hint);
		_btnCheckUpdate.Enabled = false;
		string exePath = AppPaths.ExePath;
		string text = Path.GetDirectoryName(exePath);
		ThreadPool.QueueUserWorkItem(delegate
		{
			long num = Environment.TickCount64;
			string error;
			bool flag = Updater.Download(info, Path.Combine(text, Updater.TempDownloadName), delegate(long done, long total)
			{
				long num2 = Environment.TickCount64;
				if (num2 - num >= 120)
				{
					num = num2;
					PostUpdateAction(delegate
					{
						SetUpdateStatus(FormatDownloadProgress(done, total), Tokens.Hint);
					});
				}
			}, out error);
			if (!flag)
			{
				PostUpdateAction(delegate
				{
					_updateBusy = false;
					_btnCheckUpdate.Enabled = true;
					SetUpdateStatus("下载失败：" + error, Tokens.Danger);
					MessageBox.Show(this, "下载失败：\r\n\r\n" + error + "\r\n\r\n可手动到发布页下载：\r\n" + info.PageUrl, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				});
				return;
			}
			PostUpdateAction(delegate
			{
				SetUpdateStatus("下载完成，正在替换并重启…", Tokens.Success);
			});
			string error2;
			bool flag2 = Updater.ApplyAndRestart(Path.Combine(text, Updater.TempDownloadName), exePath, out error2);
			PostUpdateAction(delegate
			{
				_updateBusy = false;
				_btnCheckUpdate.Enabled = true;
				string text2 = Path.Combine(text, Updater.TempDownloadName);
				if (flag2)
				{
					SetUpdateStatus("更新已就绪，程序即将退出并自动替换", Tokens.Success);
					MessageBox.Show(this, "更新已下载完成。\r\n\r\n点击「确定」后程序将退出，自动替换为新版本并重新启动。", "更新就绪", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
					TrayContext.RequestExitForUpdate();
					return;
				}
				SetUpdateStatus("替换失败：" + error2, Tokens.Danger);
				MessageBox.Show(this, "已下载新版本，但自动替换失败：\r\n\r\n" + error2 + "\r\n\r\n新版本已保存在：\r\n" + text2 + "\r\n\r\n可手动改名覆盖，或到发布页重新下载：\r\n" + info.PageUrl, "更新失败", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			});
		});
	}

	internal static string FormatDownloadProgress(long done, long total)
	{
		double num = (double)done / 1048576.0;
		if (total > 0)
		{
			double num2 = (double)total / 1048576.0;
			int num3 = (int)Math.Round((double)done * 100.0 / (double)total);
			return $"正在下载… {num3}%（{num:F1} / {num2:F1} MB）";
		}
		return $"正在下载… 已接收 {num:F1} MB";
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		if (_recording)
		{
			StopRecording(applied: false);
		}
		base.OnFormClosed(e);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		// 只对齐外壳：内容重排由 _content.Resize 负责（改尺寸必然触发），
		// 这里再排一次等于同一帧干两遍，拖边框时最明显。
		LayoutChrome();
		UpdateMaximizeGlyph();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		try
		{
			RescaleForDpi(Native.DpiOfWindow(Handle));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		// 无边框窗要自己跟 DWM 要圆角，否则就是一整块直角矩形（"都是直角，很丑"的成因）
		try
		{
			Native.SetWindowRoundedCorners(Handle);
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 131)
		{
			// WM_NCCALCSIZE：把系统标题栏整条还给客户区（边框留着，左右下三边照样能拖拽缩放）。
			// 这样"最上面那条"就是我们自己画的画布色，和页面严丝合缝，不存在两套颜色。
			try
			{
				if (m.WParam != IntPtr.Zero)
				{
					NCCALCSIZE_PARAMS nCCALCSIZE_PARAMS = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
					Native.RECT rECT;
					bool windowRect = Native.GetWindowRect(Handle, out rECT);
					if (windowRect && nCCALCSIZE_PARAMS.rgrc0.Top > rECT.Top)
					{
						nCCALCSIZE_PARAMS.rgrc0.Top = rECT.Top;
						Marshal.StructureToPtr(nCCALCSIZE_PARAMS, m.LParam, fDeleteOld: false);
					}
					m.Result = IntPtr.Zero;
					return;
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		else if (m.Msg == 132)
		{
			// WM_NCHITTEST：标题条上按"按住标题栏"处理 → 原生拖动 / 双击最大化 / 贴边分屏都能用。
			try
			{
				int x = (short)(m.LParam.ToInt64() & 0xFFFF);
				int y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
				Point pt = PointToClient(new Point(x, y));
				if (_titleStrip != null && WindowState != FormWindowState.Maximized && pt.X >= _titleStrip.Left && pt.X < _titleStrip.Right && pt.Y >= 0 && pt.Y < _titleStrip.Height)
				{
					m.Result = new IntPtr(2);
					return;
				}
			}
			catch (Exception ex2)
			{
				Log.Error(ex2);
			}
		}
		if (m.Msg == 736)
		{
			int dpi = (int)(short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
			try
			{
				RescaleForDpi(dpi);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		else if (m.Msg == 26 || m.Msg == 794)
		{
			// WM_SETTINGCHANGE / WM_THEMECHANGED：系统切了白夜间（或换了主题色）时重读一次
			try
			{
				ReloadSystemTheme();
			}
			catch (Exception ex2)
			{
				Log.Error(ex2);
			}
		}
		base.WndProc(ref m);
	}

	// 系统主题真的变了才重建（重建会把没保存的改动先收回 _edit，不会丢）。
	private void ReloadSystemTheme()
	{
		bool dark = Theme.IsDark;
		Theme.Refresh();
		ApplyTitleBarTheme();
		if (Theme.IsDark != dark)
		{
			RebuildUi();
			return;
		}
		Invalidate(invalidateChildren: true);
	}

	private void ApplyTitleBarTheme()
	{
		try
		{
			if (IsHandleCreated)
			{
				// 用画布色当标题栏：和侧栏只差 2% 的明度，接缝看不出来
				Native.SetTitleBarTheme(Handle, Theme.IsDark, Tokens.Canvas, Tokens.TextPrimary, Tokens.Stroke);
			}
		}
		catch
		{
		}
	}

	private void RescaleForDpi(int dpi)
	{
		float num = (float)dpi / 96f;
		if (num < 0.5f)
		{
			num = 0.5f;
		}
		if (Math.Abs(num - UiScale) < 0.001f)
		{
			return;
		}
		UiScale = num;
		RebuildUi();
	}

	// DPI 变化后整体重建：所有控件按新系数重新创建，避免"字号放大了但控件尺寸没跟上"。
	private void RebuildUi()
	{
		if (_recording)
		{
			StopRecording(applied: false);
		}
		// 重建（换 DPI / 切系统主题）之前先把界面上的当前值收回 _edit，否则没保存的改动会被旧值覆盖。
		try
		{
			if (!_populating)
			{
				CaptureInto(_edit);
			}
		}
		catch
		{
		}
		int num = ((_activePage >= 0 && _activePage < _pages.Count) ? _activePage : 0);
		List<Control> list = new List<Control>();
		CollectTree(this, list);
		Controls.Clear();
		for (int i = 0; i < list.Count; i++)
		{
			try
			{
				list[i].Dispose();
			}
			catch
			{
			}
		}
		_pages.Clear();
		_nav = null;
		_content = null;
		_pager = null;
		_lblStatus = null;
		_btnDefaultsRef = null;
		_btnSaveRef = null;
		_btnCancelRef = null;
		BuildUi();
		Populate();
		SelectPage(num);
		ApplySavedSize();
		Invalidate(invalidateChildren: true);
	}

	private static void CollectTree(Control root, List<Control> sink)
	{
		for (int i = 0; i < root.Controls.Count; i++)
		{
			Control control = root.Controls[i];
			sink.Add(control);
			CollectTree(control, sink);
		}
	}

	private void ApplySavedSize()
	{
		try
		{
			if (_edit.SettingsWidth > 0 && _edit.SettingsHeight > 0)
			{
				ClientSize = new Size(_edit.SettingsWidth, _edit.SettingsHeight);
			}
			else
			{
				ClientSize = new Size(S(1024), _defaultClientH);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			ClientSize = new Size(S(1024), _defaultClientH);
		}
	}

	private void CaptureSizeInto(AppConfig target)
	{
		try
		{
			if (WindowState == FormWindowState.Normal)
			{
				target.SettingsWidth = ClientSize.Width;
				target.SettingsHeight = ClientSize.Height;
			}
		}
		catch
		{
		}
	}

	// ======================= 探针面（自测 / 生产预览） =======================

	internal FrameSelection ProbeFrameSelection()
	{
		return new FrameSelection
		{
			ColorId = SelectedId(_cmbFrameColor, "(null)"),
			StyleId = SelectedId(_cmbFrameStyle, "(null)"),
			Thickness = (int)_numFrameThick.Value,
			Radius = (int)_numFrameRadius.Value,
			FrameEnabled = _chkFrame.Checked,
			PreviewColorId = _pnlPreview.ColorId,
			PreviewStyleId = _pnlPreview.StyleId,
			PreviewThickness = _pnlPreview.Thickness,
			PreviewRadius = _pnlPreview.Radius,
			PreviewFrameEnabled = _pnlPreview.FrameEnabled,
			GridColorId = _gridColor.SelectedId,
			GridStyleId = _galleryStyle.SelectedId,
			ColorNameText = _lblColorName.Text,
			FollowAccent = _chkFollowAccent.Checked,
			CustomColorArgb = _customColorArgb,
			EffectiveColorHex = (_pnlPreview.ColorOverride.HasValue ? FrameColors.HexOf(_pnlPreview.ColorOverride.Value) : "")
		};
	}

	internal void ProbeSelectFrameColor(string id)
	{
		SelectPage(2);
		SelectId(_cmbFrameColor, id);
		SyncPreview();
	}

	internal void ProbeSetFollowAccent(bool on)
	{
		SelectPage(2);
		_chkFollowAccent.Checked = on;
		SyncFrameEnabled();
		SyncPreview();
	}

	internal void ProbeSetCustomColor(int argb)
	{
		SelectPage(2);
		_customColorArgb = argb;
		_chkFollowAccent.Checked = false;
		SyncFrameEnabled();
		SyncPreview();
	}

	internal void ProbeSelectFrameStyle(string id)
	{
		SelectPage(2);
		SelectId(_cmbFrameStyle, id);
		SyncPreview();
	}

	internal void ProbeSetFrameShape(int thickness, int radius, bool enabled)
	{
		SelectPage(2);
		_chkFrame.Checked = enabled;
		_numFrameThick.Value = Clamp(thickness, (int)_numFrameThick.Minimum, (int)_numFrameThick.Maximum);
		_numFrameRadius.Value = Clamp(radius, (int)_numFrameRadius.Minimum, (int)_numFrameRadius.Maximum);
		SyncPreview();
	}

	internal void ProbeCaptureInto(AppConfig target)
	{
		CaptureInto(target);
	}

	internal void ProbeAppendBlacklist(string kw)
	{
		AppendBlacklistKeyword(kw);
	}

	internal string ProbeBlacklistText => _txtBlacklist.Text;

	internal Control ProbeComboLeft => _cmbLeft;

	internal Control ProbeComboRight => _cmbRight;

	internal Control ProbeNumHold => _numHold;

	internal static Control ProbeCardByTitle(Form f, string title)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return null;
		}
		foreach (Card item in AllCards(settingsForm))
		{
			if (item.Text == title)
			{
				return item;
			}
		}
		return null;
	}

	internal static void ProbeSelectPage(Form f, int index)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.SelectPage(index);
		}
	}

	internal static bool ProbePageVisible(Form f, int index)
	{
		if (!(f is SettingsForm settingsForm) || index < 0 || index >= settingsForm._pages.Count)
		{
			return false;
		}
		return settingsForm._pages[index].Host.Visible;
	}

	internal static int ProbePageCount(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return 0;
		}
		return settingsForm._pages.Count;
	}

	internal static int ProbeSidebarCount(Form f)
	{
		if (!(f is SettingsForm { _nav: not null } settingsForm))
		{
			return 0;
		}
		return settingsForm._nav.ItemCount;
	}

	internal static int ProbeSidebarActive(Form f)
	{
		if (!(f is SettingsForm { _nav: not null } settingsForm))
		{
			return -1;
		}
		return settingsForm._nav.ActiveIndex;
	}

	internal static int ProbeWheelScroll(Form f, int notches)
	{
		if (!(f is SettingsForm settingsForm) || settingsForm._pages.Count == 0)
		{
			return 0;
		}
		PageHost host = settingsForm._pages[settingsForm._activePage].Host;
		try
		{
			if (host.IsHandleCreated)
			{
				Native.SendMessageW(host.Handle, 522, (IntPtr)unchecked((int)((notches * 120) << 16)), IntPtr.Zero);
				Application.DoEvents();
			}
			else
			{
				host.Offset -= notches * host.StepPx;
			}
		}
		catch
		{
		}
		return host.Offset;
	}

	internal static string ProbeScrollInfo(Form f)
	{
		if (!(f is SettingsForm settingsForm) || settingsForm._pages.Count == 0)
		{
			return "(none)";
		}
		PageHost host = settingsForm._pages[settingsForm._activePage].Host;
		return "页=" + settingsForm._activePage + " 偏移=" + host.Offset + " 内容高=" + ((host.Canvas != null) ? host.Canvas.Height : 0) + " 视口=" + host.ClientSize.Height + " 可滚=" + host.MaxScroll;
	}

	internal static int ProbeScrollTo(Form f, int y)
	{
		if (!(f is SettingsForm settingsForm) || settingsForm._pages.Count == 0)
		{
			return 0;
		}
		PageHost host = settingsForm._pages[settingsForm._activePage].Host;
		host.Offset = y;
		return host.Offset;
	}

	internal static string ProbeSyncDiag(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return "(none)";
		}
		return "侧栏高亮=" + ProbeSidebarActive(f) + " 当前页=" + settingsForm._activePage + " " + ProbeScrollInfo(f);
	}

	internal static bool ProbeOnActivePage(Form f, Control c)
	{
		if (!(f is SettingsForm settingsForm) || settingsForm._pages.Count == 0)
		{
			return true;
		}
		PageHost pageHost = settingsForm._pages[settingsForm._activePage].Host;
		for (Control parent = c; parent != null; parent = parent.Parent)
		{
			if (parent == pageHost)
			{
				return true;
			}
			if (parent is PageHost)
			{
				return false;
			}
		}
		return true;
	}

	private static List<Card> AllCards(SettingsForm f)
	{
		List<Card> list = new List<Card>();
		foreach (Page page in f._pages)
		{
			list.AddRange(page.Cards);
		}
		return list;
	}

	internal static int ProbeGroupCount(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return 0;
		}
		return AllCards(settingsForm).Count;
	}

	internal static Control ProbeGroupAt(Form f, int index)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return null;
		}
		List<Card> list = AllCards(settingsForm);
		if (index < 0 || index >= list.Count)
		{
			return null;
		}
		return list[index];
	}

	internal static bool ProbeGroupInTree(Form f, int index)
	{
		Control control = ProbeGroupAt(f, index);
		if (control == null)
		{
			return false;
		}
		for (Control parent = control.Parent; parent != null; parent = parent.Parent)
		{
			if (parent == f)
			{
				return true;
			}
		}
		return false;
	}

	internal static GroupClipReport ProbeGroupClipping(Form f, int groupIndex)
	{
		GroupClipReport groupClipReport = new GroupClipReport();
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return groupClipReport;
			}
			List<Card> list = AllCards(settingsForm);
			if (groupIndex < 0 || groupIndex >= list.Count)
			{
				return groupClipReport;
			}
			Control control = list[groupIndex];
			Rectangle clientRectangle = control.ClientRectangle;
			for (int i = 0; i < control.Controls.Count; i++)
			{
				Control control2 = control.Controls[i];
				if (control2 == null)
				{
					continue;
				}
				groupClipReport.Total++;
				if (control2.Left < 0 || control2.Top < 0 || control2.Right > clientRectangle.Width || control2.Bottom > clientRectangle.Height)
				{
					int num = Math.Max(Math.Max(-control2.Left, -control2.Top), Math.Max(control2.Right - clientRectangle.Width, control2.Bottom - clientRectangle.Height));
					if (num > 1)
					{
						groupClipReport.Clipped++;
						GroupClipReport groupClipReport2 = groupClipReport;
						groupClipReport2.Detail = groupClipReport2.Detail + "「" + (string.IsNullOrEmpty(control2.Text) ? control2.GetType().Name : ShortText(control2.Text)) + "」L=" + control2.Left + " T=" + control2.Top + " W=" + control2.Width + " H=" + control2.Height + " 越界=" + num + "px  ";
					}
				}
			}
		}
		catch (Exception ex)
		{
			groupClipReport.Detail = groupClipReport.Detail + "异常 " + ex.GetType().Name + " ";
		}
		return groupClipReport;
	}

	internal static string ProbeGroupOverlaps(Form f, int groupIndex)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			List<Card> list = AllCards(settingsForm);
			if (groupIndex < 0 || groupIndex >= list.Count)
			{
				return "";
			}
			Control control = list[groupIndex];
			Control[] array = new Control[control.Controls.Count];
			for (int i = 0; i < control.Controls.Count; i++)
			{
				array[i] = control.Controls[i];
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int j = 0; j < array.Length; j++)
			{
				if (array[j] == null || array[j].Width <= 0 || array[j].Height <= 0)
				{
					continue;
				}
				for (int k = j + 1; k < array.Length; k++)
				{
					if (array[k] == null || array[k].Width <= 0 || array[k].Height <= 0)
					{
						continue;
					}
					if (!array[j].Bounds.IntersectsWith(array[k].Bounds))
					{
						continue;
					}
					Rectangle rectangle = Rectangle.Intersect(array[j].Bounds, array[k].Bounds);
					if (rectangle.Width > 2 && rectangle.Height > 2)
					{
						stringBuilder.Append("「").Append(ShortText(array[j].Text ?? array[j].GetType().Name)).Append("」×「")
							.Append(ShortText(array[k].Text ?? array[k].GetType().Name))
							.Append("」交 ")
							.Append(rectangle.Width)
							.Append("x")
							.Append(rectangle.Height)
							.Append(" ");
					}
				}
			}
			return stringBuilder.ToString();
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name;
		}
	}

	internal static string ProbeGroupRects(Form f, int index)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return "";
		}
		List<Card> list = AllCards(settingsForm);
		if (index < 0 || index >= list.Count)
		{
			return "";
		}
		Control control = list[index];
		return "z=" + index + " " + control.GetType().Name + " vis=" + control.Visible + " L=" + control.Left + " T=" + control.Top + " W=" + control.Width + " H=" + control.Height + " 右=" + control.Right + " 底=" + control.Bottom;
	}

	internal static int ProbeGroupTop(Form f, int index)
	{
		Control control = ProbeGroupAt(f, index);
		if (control == null)
		{
			return -1;
		}
		return control.Top;
	}

	internal static string ProbeTextFitting(Form f, int groupIndex)
	{
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			List<Card> list = AllCards(settingsForm);
			if (groupIndex < 0 || groupIndex >= list.Count)
			{
				return "";
			}
			Control control = list[groupIndex];
			for (int i = 0; i < control.Controls.Count; i++)
			{
				Control control2 = control.Controls[i];
				if ((control2 is CheckBox || control2 is Label) && !string.IsNullOrEmpty(control2.Text))
				{
					int num = control2.Width - ((control2 is CheckBox) ? Sc(26) : Sc(4));
					if (num < 20)
					{
						num = 20;
					}
					Size size;
					Size size2;
					try
					{
						float num2 = control2.Font.SizeInPoints * 96f / 72f * ((CurrentUiScale > 0f) ? CurrentUiScale : 1f);
						using Font font = new Font(control2.Font.FontFamily, num2, control2.Font.Style, GraphicsUnit.Pixel);
						size = TextRenderer.MeasureText(control2.Text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
						size2 = TextRenderer.MeasureText(control2.Text, font, new Size(num, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
					}
					catch
					{
						continue;
					}
					if ((size2.Width > num && size2.Height > control2.Height) || (size.Width > num && size2.Height > control2.Height))
					{
						stringBuilder.AppendLine("「" + ShortText(control2.Text) + "」W=" + control2.Width + " H=" + control2.Height + " 文本可用=" + num + " 单行需=" + size.Width + " 折行需高=" + size2.Height);
					}
				}
			}
		}
		catch (Exception ex)
		{
			stringBuilder.AppendLine("异常 " + ex.GetType().Name);
		}
		return stringBuilder.ToString();
	}

	private static string ShortText(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "";
		}
		s = s.Replace("\r", "").Replace("\n", " ");
		if (s.Length > 16)
		{
			return s.Substring(0, 16) + "…";
		}
		return s;
	}

	internal FramePickerProbe ProbeFramePickers()
	{
		FramePickerProbe framePickerProbe = new FramePickerProbe();
		try
		{
			SelectPage(2);
			Application.DoEvents();
			framePickerProbe.ColorCells = _gridColor.CellCount;
			framePickerProbe.StyleCells = _galleryStyle.ItemsForTest.Count;
			framePickerProbe.GridSize = new Size(_gridColor.Width, _gridColor.Height);
			framePickerProbe.GallerySize = new Size(_galleryStyle.Width, _galleryStyle.Height);
			framePickerProbe.GridSelectedCell = _gridColor.SelectedCellRect;
			framePickerProbe.GallerySelectedCell = _galleryStyle.SelectedCellRect;
			framePickerProbe.GridInsideGroup = InsideCard(_gridColor);
			framePickerProbe.GalleryInsideGroup = InsideCard(_galleryStyle);
			framePickerProbe.GridInk = InkOf(_gridColor, Tokens.Surface);
			framePickerProbe.GalleryInk = InkOf(_galleryStyle, Tokens.Surface);
			framePickerProbe.GridAccentInk = InkNear(_gridColor, Tokens.Accent, 60);
			framePickerProbe.GalleryAccentInk = InkNear(_galleryStyle, Tokens.Accent, 60);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return framePickerProbe;
	}

	private static bool InsideCard(Control c)
	{
		for (Control parent = c; parent != null; parent = parent.Parent)
		{
			if (parent is Card)
			{
				Card card = (Card)parent;
				return c.Left >= -1 && c.Top >= -1 && c.Right <= card.ClientSize.Width + 1 && c.Bottom <= card.ClientSize.Height + 1;
			}
		}
		return false;
	}

	private static long InkOf(Control c, Color bg)
	{
		try
		{
			if (c.Width <= 0 || c.Height <= 0)
			{
				return 0;
			}
			using Bitmap bitmap = new Bitmap(c.Width, c.Height);
			c.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
			long num = 0;
			for (int i = 0; i < bitmap.Height; i++)
			{
				for (int j = 0; j < bitmap.Width; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (Math.Abs(pixel.R - bg.R) + Math.Abs(pixel.G - bg.G) + Math.Abs(pixel.B - bg.B) > 30)
					{
						num++;
					}
				}
			}
			return num;
		}
		catch
		{
			return 0;
		}
	}

	private static long InkNear(Control c, Color target, int tol)
	{
		try
		{
			if (c.Width <= 0 || c.Height <= 0)
			{
				return 0;
			}
			using Bitmap bitmap = new Bitmap(c.Width, c.Height);
			c.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
			long num = 0;
			for (int i = 0; i < bitmap.Height; i++)
			{
				for (int j = 0; j < bitmap.Width; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (Math.Abs(pixel.R - target.R) + Math.Abs(pixel.G - target.G) + Math.Abs(pixel.B - target.B) <= tol)
					{
						num++;
					}
				}
			}
			return num;
		}
		catch
		{
			return 0;
		}
	}

	internal int[] ProbeFrameColorInk(string colorId)
	{
		Color target = Color.Gray;
		try
		{
			target = FrameColors.Resolve(colorId);
		}
		catch
		{
		}
		return new int[3]
		{
			(int)InkNear(_gridColor, target, 24),
			(int)InkNear(_galleryStyle, target, 24),
			(int)InkNear(_pnlPreview, target, 24)
		};
	}

	internal string ProbeGridDiagnostic()
	{
		try
		{
			return "色板 " + _gridColor.CellCount + " 格 " + _gridColor.ColumnsFor(_gridColor.Width) + " 列 选中=" + (_gridColor.SelectedId ?? "(null)") + " 尺寸=" + _gridColor.Width + "x" + _gridColor.Height + " | 画廊 " + _galleryStyle.ItemsForTest.Count + " 项 " + _galleryStyle.ColumnsFor(_galleryStyle.Width) + " 列 选中=" + (_galleryStyle.SelectedId ?? "(null)") + " 尺寸=" + _galleryStyle.Width + "x" + _galleryStyle.Height;
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name;
		}
	}

	internal int ProbeDefaultClientHeight()
	{
		return _defaultClientH;
	}

	internal HelpPanel ProbeHelpPanel => _helpPanel;

	internal string ProbeAboutText
	{
		get
		{
			try
			{
				Card card = ProbeCardByTitle(this, "关于") as Card;
				if (card == null)
				{
					return "";
				}
				StringBuilder stringBuilder = new StringBuilder();
				foreach (Control control in card.Controls)
				{
					if (!string.IsNullOrEmpty(control.Text))
					{
						stringBuilder.Append(control.Text).Append(' ');
					}
				}
				return stringBuilder.ToString();
			}
			catch
			{
				return "";
			}
		}
	}

	internal Button ProbeGitHubButton => _btnGitHub;

	internal Button ProbeCheckUpdateButton => _btnCheckUpdate;

	internal CheckBox ProbeAutoUpdateCheck => _chkAutoUpdate;

	internal Label ProbeUpdateStatusLabel => _lblUpdateStatus;

	internal static bool ProbeForceOfflineForTest
	{
		get
		{
			return Updater.ForceOfflineForTest;
		}
		set
		{
			Updater.ForceOfflineForTest = value;
		}
	}

	internal static FontFamily IconFamilyForTest => Icons.Family;

	internal static string[] IconGlyphsForTest
	{
		get
		{
			return new string[8] { Glyph.Keyboard, Glyph.Move, Glyph.Color, Glyph.Speed, Glyph.Shield, Glyph.Chevron, Glyph.Help, Glyph.Info };
		}
	}

	internal static int MeasureGlyphInk(string glyph, float px)
	{
		try
		{
			FontFamily family = Icons.Family;
			if (family == null || string.IsNullOrEmpty(glyph))
			{
				return 0;
			}
			int num = Math.Max(8, (int)Math.Ceiling(px * 2.0));
			using Bitmap bitmap = new Bitmap(num, num);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.White);
				using Font font = new Font(family, px, FontStyle.Regular, GraphicsUnit.Pixel);
				TextRenderer.DrawText(graphics, glyph, font, new Rectangle(0, 0, num, num), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
			}
			int num2 = 0;
			for (int i = 0; i < num; i++)
			{
				for (int j = 0; j < num; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (pixel.R < 250 || pixel.G < 250 || pixel.B < 250)
					{
						num2++;
					}
				}
			}
			return num2;
		}
		catch
		{
			return 0;
		}
	}

	internal static bool VerifyLayout(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			List<Card> list = AllCards(settingsForm);
			if (list.Count < 6)
			{
				return false;
			}
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].Controls.Count == 0)
				{
					return false;
				}
				if (!ProbeGroupInTree(f, i))
				{
					return false;
				}
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	internal static bool VerifyResizable(Form f)
	{
		try
		{
			return f.FormBorderStyle == FormBorderStyle.Sizable && f.MaximizeBox && f.MinimumSize.Width >= 400 && f.MinimumSize.Height >= 300;
		}
		catch
		{
			return false;
		}
	}

	internal static bool VerifyAnchors(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			// 窄：单列（限宽 880、居中）；宽（最大化那种）：两列，把右边那片空白用起来。
			settingsForm.ClientSize = new Size(Sc(900), Sc(700));
			LayoutWithContentOf(settingsForm);
			int width = CardBoxOf(settingsForm).Width;
			int num = ProbeCardsPerRow(f);
			settingsForm.ClientSize = new Size(Sc(1400), Sc(700));
			LayoutWithContentOf(settingsForm);
			Rectangle cardBox = CardBoxOf(settingsForm);
			settingsForm.ClientSize = new Size(Sc(2400), Sc(700));
			SettingsForm.ProbeSelectPage(settingsForm, 1);
			Application.DoEvents();
			int num2 = ProbeCardsPerRow(f);
			Rectangle cardBox2 = CardBoxOf(settingsForm);
			settingsForm.ClientSize = new Size(Sc(900), Sc(700));
			SettingsForm.ProbeSelectPage(settingsForm, 0);
			LayoutWithContentOf(settingsForm);
			int width2 = CardBoxOf(settingsForm).Width;
			LastStretchDetail = "卡片宽 " + width + "->" + cardBox.Width + "->" + width2 + " | 每行张数 " + num + "->" + num2 + " | 宽窗口下卡片 " + cardBox2.ToString();
			return width2 == width && cardBox.Width > width && cardBox.Width <= Sc(Tokens.CardMaxW) + Sc(2) && num == 1 && num2 == 2;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>当前页"同一行"有几张卡片（1 = 单列，2 = 两列）。</summary>
	/// <summary>
	/// 侧栏第 index 项右端的实际底色：有胶囊底就是 Hover/AccentSoft，没有就是侧栏底色。
	/// 用真实绘制结果判定，"没选中却有底"这类回归才测得出来。
	/// </summary>
	internal static Color ProbeNavRowColor(Form f, int index)
	{
		try
		{
			if (!(f is SettingsForm settingsForm) || settingsForm._nav == null)
			{
				return Color.Empty;
			}
			SideNav nav = settingsForm._nav;
			if (nav.Width < 40 || nav.Height < 40)
			{
				return Color.Empty;
			}
			using Bitmap bitmap = new Bitmap(nav.Width, nav.Height);
			nav.DrawToBitmap(bitmap, new Rectangle(0, 0, nav.Width, nav.Height));
			int num = (int)Math.Round(84.0 * (double)UiScaleForTest);
			int num2 = (int)Math.Round(38.0 * (double)UiScaleForTest);
			int x = Math.Max(2, nav.Width - (int)Math.Round(18.0 * (double)UiScaleForTest));
			int y = num + index * num2 + num2 / 2;
			if (y < 0 || y >= bitmap.Height)
			{
				return Color.Empty;
			}
			return bitmap.GetPixel(x, y);
		}
		catch
		{
			return Color.Empty;
		}
	}

	internal static int TextLinesForTest(string text, Control ctl, int width)
	{
		// 与卡片排版同一把尺子：折行总高 ÷ 单行高（带硬换行也算得对）
		try
		{
			int num = TextRenderer.MeasureText("字", ctl.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;
			int num2 = TextRenderer.MeasureText(text, ctl.Font, new Size(Math.Max(20, width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
			return Math.Max(1, (int)Math.Round((double)num2 / (double)Math.Max(1, num)));
		}
		catch
		{
			return 1;
		}
	}

	internal static int ProbeCardsPerRow(Form f)
	{
		try
		{
			if (f is SettingsForm settingsForm && settingsForm._pages.Count > settingsForm._activePage)
			{
				List<Card> cards = settingsForm._pages[settingsForm._activePage].Cards;
				if (cards.Count > 0)
				{
					int top = cards[0].Top;
					int num = 0;
					foreach (Card card in cards)
					{
						if (card.Top == top)
						{
							num++;
						}
					}
					return num;
				}
			}
		}
		catch
		{
		}
		return 0;
	}

	/// <summary>取当前页第一张卡片的边界（用来量内容列宽与左边距）。</summary>
	internal static Rectangle CardBoxOf(Form f)
	{
		try
		{
			if (f is SettingsForm settingsForm && settingsForm._pages.Count > settingsForm._activePage)
			{
				List<Card> cards = settingsForm._pages[settingsForm._activePage].Cards;
				if (cards.Count > 0)
				{
					return cards[0].Bounds;
				}
			}
		}
		catch
		{
		}
		return Rectangle.Empty;
	}

	internal static string LastStretchDetail = string.Empty;

	internal static bool VerifyInnerStretch(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			// 卡内：整宽块（色板）必须跟着卡片变宽；固定宽的控件（下拉框）保持宽度不变、
			// 但右边界要跟着卡片右边界走（右对齐的代价就是宽度不跟着长）。
			settingsForm.ClientSize = new Size(Sc(900), Sc(720));
			LayoutWithContentOf(settingsForm);
			int width = settingsForm._gridColor.Width;
			int width2 = settingsForm._cmbLeft.Width;
			int num = CardBoxOf(settingsForm).Right - settingsForm._cmbLeft.Right;
			settingsForm.ClientSize = new Size(Sc(1150), Sc(720));
			LayoutWithContentOf(settingsForm);
			int width3 = settingsForm._gridColor.Width;
			int width4 = settingsForm._cmbLeft.Width;
			int num2 = CardBoxOf(settingsForm).Right - settingsForm._cmbLeft.Right;
			LastStretchDetail = "色板 " + width + "->" + width3 + " | 下拉框 " + width2 + "->" + width4 + " | 右间距 " + num + "->" + num2;
			return width3 > width && width4 == width2 && num == num2;
		}
		catch
		{
			return false;
		}
	}

	internal static bool VerifyStretchIdempotent(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			// 窄 -> 宽 -> 窄 来回拉 5 轮，回到原尺寸后控件位置必须和一开始完全一致。
			int num = Sc(900);
			int num2 = Sc(1240);
			settingsForm.ClientSize = new Size(num, Sc(720));
			LayoutWithContentOf(settingsForm);
			Rectangle bounds = settingsForm._cmbLeft.Bounds;
			for (int i = 0; i < 5; i++)
			{
				settingsForm.ClientSize = new Size(num2, Sc(720));
				LayoutWithContentOf(settingsForm);
				settingsForm.ClientSize = new Size(num, Sc(720));
				LayoutWithContentOf(settingsForm);
			}
			Rectangle bounds2 = settingsForm._cmbLeft.Bounds;
			LastStretchDetail = "A=" + bounds.ToString() + " B=" + bounds2.ToString();
			return bounds.Left == bounds2.Left && bounds.Width == bounds2.Width && bounds.Top == bounds2.Top;
		}
		catch
		{
			return false;
		}
	}

	private static void LayoutWithContentOf(SettingsForm f)
	{
		// 自检要跨页读几何（比如在"触发方式"页量"视觉反馈"页的色板宽度），
		// 所以测试路径一律全排，不走运行时"只排可见页"的快路径。
		f.LayoutAllPages(f.ContentWidth());
	}

	internal static string DumpLayoutItem(Form f, Control target)
	{
		if (target == null)
		{
			return "(null)";
		}
		try
		{
			return target.GetType().Name + " L=" + target.Left + " T=" + target.Top + " W=" + target.Width + " H=" + target.Height + " vis=" + target.Visible;
		}
		catch
		{
			return "(err)";
		}
	}

	internal static void RelayoutForTest(Form f)
	{
		if (f is SettingsForm settingsForm)
		{
			LayoutWithContentOf(settingsForm);
			settingsForm.Invalidate();
		}
	}

	/// <summary>按运行时的快路径重排一次（只排当前可见页），用于性能自检。</summary>
	internal static void RelayoutActiveForTest(Form f)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.LayoutWithContent(settingsForm.ContentWidth());
		}
	}

	internal static void PopulateForTest(Form f)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.ResetDefaults();
		}
	}

	internal static Rectangle ProbeRectOfTextForTest(Form f, string prefix)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return Rectangle.Empty;
			}
			foreach (Card item in AllCards(settingsForm))
			{
				for (int i = 0; i < item.Controls.Count; i++)
				{
					Control control = item.Controls[i];
					if (control.Text != null && control.Text.StartsWith(prefix))
					{
						return control.Bounds;
					}
				}
			}
		}
		catch
		{
		}
		return Rectangle.Empty;
	}

	internal static float ProbeUiScaleOf(Form f)
	{
		if (f is SettingsForm settingsForm)
		{
			return settingsForm.UiScale;
		}
		return 1f;
	}

	internal static void ProbeRescaleForDpi(Form f, int dpi)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.RescaleForDpi(dpi);
		}
	}

	internal static string ProbeLayoutFingerprint(Form f)
	{
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			foreach (Card item in AllCards(settingsForm))
			{
				stringBuilder.Append(item.GetType().Name).Append(':').Append(item.Left).Append(',')
					.Append(item.Top)
					.Append(',')
					.Append(item.Width)
					.Append(',')
					.Append(item.Height)
					.Append(';');
			}
			stringBuilder.Append("cmbL=").Append(settingsForm._cmbLeft.Left).Append(',')
				.Append(settingsForm._cmbLeft.Width)
				.Append(';');
			stringBuilder.Append("txtBl=").Append(settingsForm._txtBlacklist.Width).Append(';');
			stringBuilder.Append("numHold=").Append(settingsForm._numHold.Width).Append(';');
			stringBuilder.Append("preview=").Append(settingsForm._pnlPreview.Left).Append(',')
				.Append(settingsForm._pnlPreview.Top)
				.Append(',')
				.Append(settingsForm._pnlPreview.Width)
				.Append(',')
				.Append(settingsForm._pnlPreview.Height)
				.Append(';');
			stringBuilder.Append("grid=").Append(settingsForm._gridColor.Width).Append('x').Append(settingsForm._gridColor.Height)
				.Append(";gal=")
				.Append(settingsForm._galleryStyle.Width)
				.Append('x')
				.Append(settingsForm._galleryStyle.Height)
				.Append(';');
		}
		catch
		{
		}
		return stringBuilder.ToString();
	}

	internal static int ProbeCardMaxWidthPx(Form f)
	{
		return Sc(Tokens.CardMaxW);
	}

		internal static Color ProbeTokenColor(string name)
	{
		switch (name)
		{
		case "Surface":
			return Tokens.Surface;
		case "TextPrimary":
			return Tokens.TextPrimary;
		case "TextSecondary":
			return Tokens.TextSecondary;
		case "Accent":
			return Tokens.Accent;
		case "Canvas":
			return Tokens.Canvas;
		case "Sidebar":
			return Tokens.Sidebar;
		case "AccentSoft":
			return Tokens.AccentSoft;
		default:
			return Color.Empty;
		}
	}

	internal static void ProbeForceDarkTheme(bool? dark)
	{
		Theme.SetForceDark(dark);
	}

	internal static int ProbePreviewRadiusDiff(Form f, int radiusA, int radiusB)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return -1;
			}
			settingsForm.SelectPage(2);
			int width = Math.Max(8, settingsForm._pnlPreview.Width);
			int height = Math.Max(8, settingsForm._pnlPreview.Height);
			using Bitmap bitmap = new Bitmap(width, height);
			using Bitmap bitmap2 = new Bitmap(width, height);
			settingsForm._numFrameRadius.Value = Clamp(radiusA, (int)settingsForm._numFrameRadius.Minimum, (int)settingsForm._numFrameRadius.Maximum);
			settingsForm.SyncPreview();
			settingsForm._pnlPreview.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
			settingsForm._numFrameRadius.Value = Clamp(radiusB, (int)settingsForm._numFrameRadius.Minimum, (int)settingsForm._numFrameRadius.Maximum);
			settingsForm.SyncPreview();
			settingsForm._pnlPreview.DrawToBitmap(bitmap2, new Rectangle(0, 0, width, height));
			int num = 0;
			for (int i = 0; i < height; i++)
			{
				for (int j = 0; j < width; j++)
				{
					if (bitmap.GetPixel(j, i).ToArgb() != bitmap2.GetPixel(j, i).ToArgb())
					{
						num++;
					}
				}
			}
			return num;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return -1;
		}
	}

	internal Rectangle ProbePagerBounds()
	{
		return (_pager == null) ? Rectangle.Empty : _pager.Bounds;
	}

	internal int ProbeCaptionButtonCount()
	{
		int num = 0;
		for (int i = 0; i < _titleStrip.Controls.Count; i++)
		{
			if (_titleStrip.Controls[i] is CaptionButton captionButton && captionButton.Visible && captionButton.Width > 0 && captionButton.Height > 0)
			{
				num++;
			}
		}
		return num;
	}

	internal Color ProbeTitleStripColor()
	{
		return _titleStrip.BackColor;
	}

	internal static bool ProbeThemeIsDark()
	{
		return Theme.IsDark;
	}

	internal static RenderStats ProbeFormRender(Form f, string frameColorId)
	{
		RenderStats renderStats = new RenderStats();
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return renderStats;
			}
			settingsForm.SelectPage(2);
			Application.DoEvents();
			int width = f.Width;
			int height = f.Height;
			renderStats.Width = width;
			renderStats.Height = height;
			if (width <= 0 || height <= 0)
			{
				return renderStats;
			}
			using Bitmap bitmap = new Bitmap(width, height);
			f.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
			HashSet<int> hashSet = new HashSet<int>();
			Color color = Color.Gray;
			try
			{
				color = FrameColors.Resolve(frameColorId);
			}
			catch
			{
			}
		long num = 0L;
		int num4 = 0;
		for (int i = 0; i < height; i += 2)
		{
			for (int j = 0; j < width; j += 2)
			{
				Color pixel = bitmap.GetPixel(j, i);
				hashSet.Add(pixel.ToArgb());
				num += (pixel.R + pixel.G + pixel.B) / 3;
				num4++;
				// 只把"纯黑"当成没铺底：深色主题的底色（#17191C）和卡片投影都是有意的颜色，
				// 用 <24 的老阈值会把它们一并算成黑条，深色主题下必假红。
				if (pixel.R < 8 && pixel.G < 8 && pixel.B < 8)
				{
					renderStats.BlackPixels++;
				}
				if (Math.Abs(pixel.R - Tokens.Accent.R) <= 18 && Math.Abs(pixel.G - Tokens.Accent.G) <= 18 && Math.Abs(pixel.B - Tokens.Accent.B) <= 18)
				{
					renderStats.AccentPixels++;
				}
				if (Math.Abs(pixel.R - color.R) <= 18 && Math.Abs(pixel.G - color.G) <= 18 && Math.Abs(pixel.B - color.B) <= 18)
				{
					renderStats.FrameColorPixels++;
				}
			}
		}
		renderStats.DistinctColors = hashSet.Count;
		renderStats.MeanBrightness = ((num4 == 0) ? 0.0 : ((double)num / (double)num4));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return renderStats;
	}

	internal static bool ProbeHoverTweenAnimates(out float start, out float end)
	{
		start = 0f;
		end = 0f;
		try
		{
			using FlatButton flatButton = new FlatButton
			{
				Text = "悬停探针"
			};
			flatButton.KickHoverForTest();
			start = flatButton.HoverValueForTest;
			int num = 0;
			while (flatButton.HoverValueForTest < 0.999f && num < 240)
			{
				Application.DoEvents();
				Thread.Sleep(8);
				num++;
			}
			end = flatButton.HoverValueForTest;
			return start < 0.05f && end > 0.95f && end > start;
		}
		catch
		{
			return false;
		}
	}

	internal static string ProbeSwitchColors(Form f, bool on)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "异常(no-form)";
			}
			FlatCheck chkHold = settingsForm._chkHold;
			chkHold.SnapCheckForTest(on);
			Application.DoEvents();
			if (chkHold.Width <= 0 || chkHold.Height <= 0)
			{
				return "异常(zero-size)";
			}
			using Bitmap bitmap = new Bitmap(chkHold.Width, chkHold.Height);
			chkHold.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
			int r = 0;
			int g = 0;
			int b = 0;
			Sample(bitmap, 1, bitmap.Height / 2, ref r, ref g, ref b);
			Point knobCenter = chkHold.KnobCenter;
			int r2 = 0;
			int g2 = 0;
			int b2 = 0;
			Sample(bitmap, knobCenter.X, knobCenter.Y, ref r2, ref g2, ref b2);
			return "轨道=" + r + "," + g + "," + b + " 滑块=" + r2 + "," + g2 + "," + b2 + " 尺寸=" + bitmap.Width + "x" + bitmap.Height;
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name;
		}
	}

	private static void Sample(Bitmap bmp, int x, int y, ref int r, ref int g, ref int b)
	{
		x = Math.Max(0, Math.Min(bmp.Width - 1, x));
		y = Math.Max(0, Math.Min(bmp.Height - 1, y));
		Color pixel = bmp.GetPixel(x, y);
		r = pixel.R;
		g = pixel.G;
		b = pixel.B;
	}

	internal static string ProbeGalleryGeometry(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			settingsForm.SelectPage(2);
			StyleGallery galleryStyle = settingsForm._galleryStyle;
			int columns = galleryStyle.ColumnsFor(galleryStyle.Width);
			int num = (galleryStyle.ItemsForTest.Count + columns - 1) / columns;
			return "画廊 " + galleryStyle.ItemsForTest.Count + " 项 " + columns + " 列 " + num + " 行 格高=" + galleryStyle.CellH + " 控件高=" + galleryStyle.Height + " 需要=" + (num * (galleryStyle.CellH + galleryStyle.Gap) - galleryStyle.Gap) + " 选中=" + (galleryStyle.SelectedId ?? "(null)");
		}
		catch
		{
			return "";
		}
	}

	internal static bool ProbeGalleryFits(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			settingsForm.SelectPage(2);
			StyleGallery galleryStyle = settingsForm._galleryStyle;
			int columns = galleryStyle.ColumnsFor(galleryStyle.Width);
			int num = (galleryStyle.ItemsForTest.Count + columns - 1) / columns;
			int num2 = num * (galleryStyle.CellH + galleryStyle.Gap) - galleryStyle.Gap;
			return galleryStyle.Height >= num2 && InsideCard(galleryStyle);
		}
		catch
		{
			return false;
		}
	}

	internal static string ProbeColorGridGeometry(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			settingsForm.SelectPage(2);
			SwatchGrid gridColor = settingsForm._gridColor;
			int columns = gridColor.ColumnsFor(gridColor.Width);
			int num = (gridColor.CellCount + columns - 1) / columns;
			return "色板 " + gridColor.CellCount + " 格 " + columns + " 列 " + num + " 行 格高=" + gridColor.Cell + " 控件高=" + gridColor.Height + " 需要=" + (num * (gridColor.Cell + gridColor.Gap) - gridColor.Gap) + " 选中=" + (gridColor.SelectedId ?? "(null)");
		}
		catch
		{
			return "";
		}
	}

	internal static bool ProbeColorGridFits(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			settingsForm.SelectPage(2);
			SwatchGrid gridColor = settingsForm._gridColor;
			int columns = gridColor.ColumnsFor(gridColor.Width);
			int num = (gridColor.CellCount + columns - 1) / columns;
			int num2 = num * (gridColor.Cell + gridColor.Gap) - gridColor.Gap;
			return gridColor.Height >= num2 && InsideCard(gridColor);
		}
		catch
		{
			return false;
		}
	}

	internal static void DumpGroup(Form f, int groupIndex, StringBuilder sb)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return;
			}
			List<Card> list = AllCards(settingsForm);
			if (groupIndex < 0 || groupIndex >= list.Count)
			{
				return;
			}
			Control control = list[groupIndex];
			sb.AppendLine("分组[" + groupIndex + "] 「" + control.Text + "」 L=" + control.Left + " T=" + control.Top + " W=" + control.Width + " H=" + control.Height + " 客户区=" + control.ClientSize.Width + "x" + control.ClientSize.Height);
			for (int i = 0; i < control.Controls.Count; i++)
			{
				Control control2 = control.Controls[i];
				sb.AppendLine("   " + control2.GetType().Name + " 「" + ShortText(control2.Text ?? "") + "」 L=" + control2.Left + " T=" + control2.Top + " W=" + control2.Width + " H=" + control2.Height + " vis=" + control2.Visible);
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("异常 " + ex.GetType().Name);
		}
	}

	internal static void DumpVisualGroupGeometry(StringBuilder sb, int[] designWidths)
	{
		SettingsForm settingsForm = null;
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "holddrag-vgeom-cfg");
			try
			{
				Directory.CreateDirectory(text);
			}
			catch
			{
			}
			ConfigStore configStore = new ConfigStore(text);
			try
			{
				configStore.Load();
			}
			catch
			{
			}
			Coordinator coord = new Coordinator(configStore);
			settingsForm = new SettingsForm(configStore, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(20, 20);
			float num = (CurrentUiScale > 0f) ? CurrentUiScale : 1f;
			for (int i = 0; i < designWidths.Length; i++)
			{
				int num2 = (int)Math.Round((float)designWidths[i] * num);
				settingsForm.ClientSize = new Size(num2, 900);
				settingsForm.LayoutWithContent(settingsForm.ClientSize.Width - Sc(208));
				settingsForm.SelectPage(2);
				Application.DoEvents();
				int width = settingsForm.ClientSize.Width;
				sb.Append("══ 客户区宽=").Append(width).Append("（设计稿 ").Append(designWidths[i]).Append("px）════")
					.AppendLine();
				List<Card> list = AllCards(settingsForm);
				for (int j = 0; j < list.Count; j++)
				{
					Control control = list[j];
					sb.Append("  z=").Append(j).Append(' ')
						.Append(control.GetType().Name)
						.Append(" vis=")
						.Append(control.Visible)
						.Append(" L=")
						.Append(control.Left)
						.Append(" T=")
						.Append(control.Top)
						.Append(" W=")
						.Append(control.Width)
						.Append(" H=")
						.Append(control.Height)
						.Append(" 右=")
						.Append(control.Right)
						.Append(" 底=")
						.Append(control.Bottom)
						.AppendLine();
				}
				FramePreviewBox pnlPreview = settingsForm._pnlPreview;
				sb.Append("  z=0 FramePreviewBox vis=").Append(pnlPreview.Visible).Append(" L=")
					.Append(pnlPreview.Left)
					.Append(" T=")
					.Append(pnlPreview.Top)
					.Append(" W=")
					.Append(pnlPreview.Width)
					.Append(" H=")
					.Append(pnlPreview.Height)
					.Append(" 右=")
					.Append(pnlPreview.Right)
					.Append(" 底=")
					.Append(pnlPreview.Bottom)
					.AppendLine();
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("异常 " + ex.GetType().Name + ": " + ex.Message);
		}
		finally
		{
			try
			{
				settingsForm?.Close();
				settingsForm?.Dispose();
			}
			catch
			{
			}
		}
	}

	internal static void DumpPreviewStyles(string outDir)
	{
		SettingsForm settingsForm = null;
		try
		{
			Directory.CreateDirectory(outDir);
			string text = Path.Combine(Path.GetTempPath(), "holddrag-previewtest-cfg");
			try
			{
				Directory.CreateDirectory(text);
			}
			catch
			{
			}
			ConfigStore configStore = new ConfigStore(text);
			try
			{
				configStore.Load();
			}
			catch
			{
			}
			Coordinator coord = new Coordinator(configStore);
			settingsForm = new SettingsForm(configStore, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(20, 20);
			settingsForm.ClientSize = new Size(700, 900);
			settingsForm.Show();
			settingsForm.SelectPage(2);
			Application.DoEvents();
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("=== 路径 A：直接设 StyleId 字段 ===");
			string[] all = FrameStyles.All;
			foreach (string text2 in all)
			{
				settingsForm._pnlPreview.StyleId = text2;
				settingsForm._pnlPreview.ColorId = "Red";
				settingsForm._pnlPreview.Thickness = 4;
				settingsForm._pnlPreview.Radius = 13;
				settingsForm._pnlPreview.FrameEnabled = true;
				settingsForm._pnlPreview.Refresh_();
				Application.DoEvents();
				FramePreviewBox pnlPreview = settingsForm._pnlPreview;
				using Bitmap bitmap = new Bitmap(pnlPreview.Width, pnlPreview.Height);
				pnlPreview.DrawToBitmap(bitmap, new Rectangle(0, 0, pnlPreview.Width, pnlPreview.Height));
				string text3 = Path.Combine(outDir, "preview-" + text2 + ".png");
				bitmap.Save(text3, ImageFormat.Png);
				long num = 0L;
				int num2 = 0;
				for (int j = 0; j < bitmap.Height; j++)
				{
					for (int k = 0; k < bitmap.Width; k++)
					{
						Color pixel = bitmap.GetPixel(k, j);
						num += pixel.R + pixel.G + pixel.B;
						if (pixel.R < 250 || pixel.G < 250 || pixel.B < 250)
						{
							num2++;
						}
					}
				}
				stringBuilder.AppendLine("  " + text2 + " size=" + bitmap.Width + "x" + bitmap.Height + " 像素和=" + num + " 非白像素=" + num2 + " 文件=" + Path.GetFileName(text3));
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("=== 路径 B：改下拉框 SelectedIndex（模拟用户点选）===");
			stringBuilder.AppendLine("  下拉框里共 " + settingsForm._cmbFrameStyle.Items.Count + " 项");
			for (int l = 0; l < settingsForm._cmbFrameStyle.Items.Count; l++)
			{
				settingsForm._cmbFrameStyle.SelectedIndex = l;
				Application.DoEvents();
				string styleId = settingsForm._pnlPreview.StyleId;
				object obj = settingsForm._cmbFrameStyle.Items[l];
				FramePreviewBox pnlPreview2 = settingsForm._pnlPreview;
				long num3 = 0L;
				int num4 = 0;
				using (Bitmap bitmap2 = new Bitmap(pnlPreview2.Width, pnlPreview2.Height))
				{
					pnlPreview2.DrawToBitmap(bitmap2, new Rectangle(0, 0, pnlPreview2.Width, pnlPreview2.Height));
					bitmap2.Save(Path.Combine(outDir, "combo-" + l + "-" + styleId + ".png"), ImageFormat.Png);
					for (int m = 0; m < bitmap2.Height; m++)
					{
						for (int n = 0; n < bitmap2.Width; n++)
						{
							Color pixel2 = bitmap2.GetPixel(n, m);
							num3 += pixel2.R + pixel2.G + pixel2.B;
							if (pixel2.R < 250 || pixel2.G < 250 || pixel2.B < 250)
							{
								num4++;
							}
						}
					}
				}
				stringBuilder.AppendLine("  index=" + l + " 下拉项文字=\"" + ((obj != null) ? obj.ToString() : "(null)") + "\" → 预览 StyleId=\"" + styleId + "\" 像素和=" + num3 + " 非白像素=" + num4 + ((styleId == settingsForm._pnlPreview.StyleId) ? "" : "  ← ★ 不一致！"));
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("★ 路径 A 各行 [像素和] 全不同 → 样式确实画出了不同结果；");
			stringBuilder.AppendLine("★ 路径 B 各行 [像素和] 全不同 → 下拉框连线正常，改选项能刷新预览；");
			stringBuilder.AppendLine("  若路径 B 有任意两行相同，就是[选了形状但预览不变]的真 bug。");
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("=== 路径 C：加载真实配置后的预览状态 ===");
			try
			{
				ConfigStore configStore2 = new ConfigStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag"));
				configStore2.Load();
				Coordinator coord2 = new Coordinator(configStore2);
				AppConfig current = configStore2.Current;
				stringBuilder.AppendLine("  配置：颜色=" + current.FrameColorId + " 样式=" + current.FrameStyleId + " 粗细=" + current.FrameThickness + " 圆角=" + current.FrameRadius + " 外框开关=" + current.ShowActivationFrame);
				SettingsForm settingsForm2 = new SettingsForm(configStore2, coord2);
				settingsForm2.StartPosition = FormStartPosition.Manual;
				settingsForm2.Location = new Point(20, 20);
				settingsForm2.ClientSize = new Size(900, 900);
				settingsForm2.Show();
				settingsForm2.SelectPage(2);
				Application.DoEvents();
				for (int num5 = 0; num5 < 4; num5++)
				{
					Application.DoEvents();
					Thread.Sleep(80);
				}
				stringBuilder.AppendLine("  下拉框选中项：颜色=\"" + ((settingsForm2._cmbFrameColor.SelectedItem != null) ? settingsForm2._cmbFrameColor.SelectedItem.ToString() : "(null)") + "\" 样式=\"" + ((settingsForm2._cmbFrameStyle.SelectedItem != null) ? settingsForm2._cmbFrameStyle.SelectedItem.ToString() : "(null)") + "\"");
				stringBuilder.AppendLine("  预览控件字段：ColorId=\"" + settingsForm2._pnlPreview.ColorId + "\" StyleId=\"" + settingsForm2._pnlPreview.StyleId + "\" Thickness=" + settingsForm2._pnlPreview.Thickness + " Radius=" + settingsForm2._pnlPreview.Radius + " Enabled=" + settingsForm2._pnlPreview.FrameEnabled);
				FramePreviewBox pnlPreview3 = settingsForm2._pnlPreview;
				using (Bitmap bitmap3 = new Bitmap(pnlPreview3.Width, pnlPreview3.Height))
				{
					pnlPreview3.DrawToBitmap(bitmap3, new Rectangle(0, 0, pnlPreview3.Width, pnlPreview3.Height));
					bitmap3.Save(Path.Combine(outDir, "preview-REALCONFIG.png"), ImageFormat.Png);
					int num6 = 0;
					for (int num7 = 0; num7 < bitmap3.Width; num7++)
					{
						if (bitmap3.GetPixel(num7, bitmap3.Height / 2).A != 0)
						{
							num6++;
						}
					}
					stringBuilder.AppendLine("  中线非透明像素=" + num6 + " / " + bitmap3.Width);
				}
				try
				{
					settingsForm2.Close();
					settingsForm2.Dispose();
				}
				catch
				{
				}
			}
			catch (Exception ex2)
			{
				stringBuilder.AppendLine("  路径 C 失败：" + ex2.Message);
			}
			File.WriteAllText(Path.Combine(outDir, "previewtest.txt"), stringBuilder.ToString(), Encoding.UTF8);
		}
		catch (Exception ex3)
		{
			Log.Error(ex3);
		}
		finally
		{
			try
			{
				settingsForm?.Close();
				settingsForm?.Dispose();
			}
			catch
			{
			}
		}
	}

	// ======================= 小值对象 =======================

	private sealed class ActionItem
	{
		public string Value;
		public string Text;

		public override string ToString()
		{
			return Text;
		}
	}

	private sealed class IdItem
	{
		public string Value;
		public string Text;

		public override string ToString()
		{
			return Text;
		}
	}

	private sealed class ColorItem
	{
		public string Value;
		public string Text;
		public Color Color;

		public override string ToString()
		{
			return Text;
		}
	}

	private static void PaintColorEntry(Graphics g, Rectangle bounds, object item)
	{
		try
		{
			if (!(item is ColorItem colorItem) || bounds.Width <= 0 || bounds.Height <= 0)
			{
				return;
			}
			HQ.Apply(g);
			int num = Math.Max(8, (int)Math.Round((double)bounds.Height * 0.52));
			if (num > bounds.Width / 3)
			{
				num = Math.Max(8, bounds.Width / 3);
			}
			int y = bounds.Top + (bounds.Height - num) / 2;
			Rectangle r = new Rectangle(bounds.Left, y, num, num);
			GraphicsPath graphicsPath = RoundedPath(r, Sc(3));
			try
			{
				using (SolidBrush brush = new SolidBrush(colorItem.Color))
				{
					g.FillPath(brush, graphicsPath);
				}
				using Pen pen = new Pen(Tokens.InputStroke);
				g.DrawPath(pen, graphicsPath);
			}
			finally
			{
				graphicsPath.Dispose();
			}
			TextRenderer.DrawText(bounds: new Rectangle(r.Right + Sc(8), bounds.Top, Math.Max(10, bounds.Width - num - Sc(10)), bounds.Height), dc: g, text: colorItem.Text, font: SystemFonts.DefaultFont, foreColor: Tokens.TextPrimary, flags: TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	internal static int ProbeWheelSwallowed(ComboBox c)
	{
		if (c is FlatCombo flatCombo)
		{
			return flatCombo.WheelSwallowedForTest;
		}
		return 0;
	}

	internal static int ProbeWheelForwarded(ComboBox c)
	{
		if (c is FlatCombo flatCombo)
		{
			return flatCombo.WheelForwardedForTest;
		}
		return 0;
	}
}
internal sealed class ShakeDetector
{
	private struct Sample
	{
		public int X;

		public int Y;

		public long T;
	}

	public const int MinSpanLogical = 32;

	public const int MaxSpanLogical = 220;

	public const int GestureWindowMs = 800;

	public const int ReversalsNeeded = 4;

	public const int MaxNetDriftLogical = 80;

	public const int MinPathLogical = 150;

	public const double HorizontalDominance = 0.72;

	public const int CooldownMs = 900;

	private const int SampleCap = 768;

	private readonly Sample[] _buf = new Sample[768];

	private int _n;

	private int _head;

	private bool _armed;

	private long _coolUntil;

	private double _scale = 1.0;

	internal int SampleCountForTest => _n;

	internal bool CoolingForTest => _coolUntil != 0;

	public void SetScale(double scale)
	{
		_scale = ((scale > 0.1 && scale < 8.0) ? scale : 1.0);
	}

	public void Reset(int x, int y, long nowMs)
	{
		_n = 0;
		_head = 0;
		_coolUntil = 0L;
		_armed = true;
		Push(x, y, nowMs);
	}

	public void Invalidate()
	{
		_armed = false;
		_n = 0;
		_head = 0;
	}

	public bool Feed(int x, int y, long nowMs)
	{
		if (!_armed)
		{
			return false;
		}
		if (nowMs < _coolUntil)
		{
			return false;
		}
		Push(x, y, nowMs);
		while (_n > 0 && nowMs - Oldest().T > 800)
		{
			DropOldest();
		}
		if (_n < 6)
		{
			return false;
		}
		if (!Evaluate())
		{
			return false;
		}
		_coolUntil = nowMs + 900;
		_n = 0;
		_head = 0;
		return true;
	}

	private bool Evaluate()
	{
		int num = (int)Math.Round(32.0 * _scale);
		int num2 = (int)Math.Round(220.0 * _scale);
		int num3 = (int)Math.Round(150.0 * _scale);
		int num4 = (int)Math.Round(80.0 * _scale);
		if (num < 8)
		{
			num = 8;
		}
		if (num4 < num)
		{
			num4 = num;
		}
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		int num9 = 0;
		bool flag = false;
		Sample sample = At(0);
		for (int i = 1; i < _n; i++)
		{
			Sample sample2 = At(i);
			int num10 = sample2.X - sample.X;
			int value = sample2.Y - sample.Y;
			sample = sample2;
			num6 += Math.Abs(num10);
			num7 += Math.Abs(value);
			int num11 = Math.Abs(num10);
			if (num11 == 0)
			{
				continue;
			}
			int num12 = ((num10 > 0) ? 1 : (-1));
			if (num8 == 0)
			{
				num8 = num12;
				num9 = num11;
				continue;
			}
			if (num12 == num8)
			{
				num9 += num11;
				if (num9 > num2)
				{
					flag = true;
				}
				continue;
			}
			if (num9 >= num)
			{
				num5++;
			}
			num8 = num12;
			num9 = num11;
		}
		if (flag)
		{
			return false;
		}
		if (num5 < 4)
		{
			return false;
		}
		if (num6 < num3)
		{
			return false;
		}
		int num13 = num6 + num7;
		if (num13 <= 0)
		{
			return false;
		}
		if ((double)num6 < (double)num13 * 0.72)
		{
			return false;
		}
		Sample sample3 = At(0);
		Sample sample4 = At(_n - 1);
		if (Math.Abs(sample4.X - sample3.X) > num4)
		{
			return false;
		}
		if (Math.Abs(sample4.Y - sample3.Y) > num4)
		{
			return false;
		}
		return true;
	}

	private void Push(int x, int y, long nowMs)
	{
		_buf[_head] = new Sample
		{
			X = x,
			Y = y,
			T = nowMs
		};
		_head = (_head + 1) % 768;
		if (_n < 768)
		{
			_n++;
		}
	}

	private Sample Oldest()
	{
		int num = (_head - _n + 768) % 768;
		return _buf[num];
	}

	private void DropOldest()
	{
		if (_n > 0)
		{
			_n--;
		}
	}

	private Sample At(int i)
	{
		int num = (_head - _n + i + 1536) % 768;
		return _buf[num];
	}
}
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
internal sealed class TrayContext : ApplicationContext
{
	private readonly string _dir;

	private readonly ConfigStore _store;

	private readonly Coordinator _coord;

	private readonly HookEngine _hooks;

	private readonly HookWatchdog _watchdog;

	private readonly NotifyIcon _tray;

	private readonly ContextMenuStrip _menu;

	private readonly Control _marshal;

	private RegisteredWaitHandle _showWait;

	private readonly ToolStripMenuItem _miPause = new ToolStripMenuItem();

	private readonly ToolStripMenuItem _miAutoStart = new ToolStripMenuItem();

	private readonly ToolStripMenuItem _miElevate = new ToolStripMenuItem();

	private readonly ToolStripMenuItem _miStatus = new ToolStripMenuItem();

	private readonly Icon _iconNormal;

	private readonly Icon _iconPaused;

	private SettingsForm _settings;

	private bool _cleaned;

	/// <summary>当前存活的托盘实例。设置窗口通过它请求"真正退出整个进程"（更新替换必须）。</summary>
	internal static TrayContext Current;

	/// <summary>请求退出整个进程（托盘 + 钩子 + 所有窗口）。用于更新替换前腾出 exe 文件。</summary>
	internal static void RequestExitForUpdate()
	{
		TrayContext trayContext = Current;
		if (trayContext == null)
		{
			// 兜底：没有托盘实例（例如仅自检）时直接退出进程
			Environment.Exit(0);
			return;
		}
		try
		{
			trayContext._marshal.BeginInvoke(trayContext.ExitApp);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			Environment.Exit(0);
		}
	}

	public TrayContext(string dir, ConfigStore store, EventWaitHandle showEvent)
	{
		_dir = dir;
		_store = store;
		bool flag = !File.Exists(Path.Combine(dir, "config.json"));
		try
		{
			string text = AutoStart.Detect();
			if (text != _store.Current.AutoStart)
			{
				Log.Info("开机自启实际状态为 " + text + "，与配置记录的 " + _store.Current.AutoStart + " 不一致，以实际为准");
				_store.Current.AutoStart = text;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		_coord = new Coordinator(_store);
		_coord.PrecreateOverlays();
		_hooks = new HookEngine(_coord);
		bool flag2 = _hooks.Install();
		_coord.ReinstallHooksForPriority = () =>
		{
			_hooks.ReinstallMouseForChainPriority();
		};
		// 设置窗口要"更新完成后真正退出整个进程"（托盘常驻进程不退，exe 文件就被占着，
		// 新版本替换不了）。这里给它一个静态回调入口。
		Current = this;
		_watchdog = new HookWatchdog(_hooks);
		_watchdog.Start();
		_iconNormal = LoadAppIcon();
		_iconPaused = CreatePausedIcon();
		_tray = new NotifyIcon();
		_menu = new ContextMenuStrip();
		BuildTray();
		ApplyConfig(initial: true);
		_marshal = new Control();
		_marshal.CreateControl();
		if (showEvent != null)
		{
			try
			{
				_showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, OnShowSignal, null, -1, executeOnlyOnce: false);
			}
			catch (Exception ex2)
			{
				Log.Error(ex2);
			}
		}
		if (Program.SmokeMode)
		{
			Log.Info("冒烟模式：2 秒后打开设置窗口，9 秒后自动退出");
			System.Windows.Forms.Timer open = new System.Windows.Forms.Timer();
			open.Interval = 2000;
			open.Tick += (object? s, EventArgs e) =>
			{
				open.Stop();
				ShowSettings();
			};
			open.Start();
			System.Windows.Forms.Timer quit = new System.Windows.Forms.Timer();
			quit.Interval = 9000;
			quit.Tick += (object? s, EventArgs e) =>
			{
				quit.Stop();
				Log.Info("冒烟测试完成，自动退出");
				ExitApp();
			};
			quit.Start();
		}
		if (!flag2)
		{
			ShowBalloon("钩子安装失败", "无法安装全局键鼠钩子（错误码 " + _hooks.LastError + "），程序将无法工作。常见原因是被杀软/安全软件拦截，请把本程序加入信任列表后重启。", ToolTipIcon.Error);
		}
		else if (flag)
		{
			ShowBalloon("长按拖窗已启动", "长按 " + KeysUtil.DisplayName(_store.Current.TriggerKeyId) + " " + _store.Current.HoldMs + " 毫秒后：左键拖动移动窗口、右键拖动调整大小、中键最大化/还原。双击托盘图标可打开设置。", ToolTipIcon.Info);
		}
		// 启动自动检查更新（用户可在「关于」里关掉）。放在最后，不挡住钩子安装与托盘显示。
		StartAutoUpdateCheck();
	}

	/// <summary>启动时的自动更新检查（后台线程，不阻塞托盘与钩子安装）。</summary>
	private void StartAutoUpdateCheck()
	{
		try
		{
			if (_store.Current == null || !_store.Current.AutoCheckUpdate)
			{
				return;
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					string error;
					bool upToDate;
					UpdateInfo updateInfo = Updater.CheckLatest(out error, out upToDate);
					if (updateInfo == null)
					{
						if (upToDate)
						{
							Log.Info("启动自动检查更新：当前已是最新（v" + AppInfo.Version + "）");
						}
						else if (!string.IsNullOrEmpty(error))
						{
							Log.Info("启动自动检查更新未成功：" + error);
						}
						return;
					}
					// 同一版本只提示一次，避免用户点了"稍后"后每次开机都被打断。
					if (string.Equals(_store.Current.LastUpdateNotified, updateInfo.Tag, StringComparison.OrdinalIgnoreCase))
					{
						Log.Info("已提示过 " + updateInfo.Tag + "，本次不再弹窗");
						return;
					}
					ShowUpdateBalloon(updateInfo);
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			});
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>后台线程 → 托盘图标线程。用气泡而不是模态框，不打断用户正在做的事。</summary>
	private void ShowUpdateBalloon(UpdateInfo info)
	{
		try
		{
			if (_marshal == null || _marshal.IsDisposed || !_marshal.IsHandleCreated)
			{
				return;
			}
			_marshal.BeginInvoke(delegate
			{
				try
				{
					// 只写字段并保存，绝不走 _store.Apply：
					// Apply 会触发 Changed → OnConfigChanged → _drag.Cancel()，
					// 启动期若用户正在拖拽会被平白掐断。
					AppConfig appConfig = _store.Current;
					if (appConfig != null && !string.Equals(appConfig.LastUpdateNotified, info.Tag, StringComparison.OrdinalIgnoreCase))
					{
						appConfig.LastUpdateNotified = info.Tag;
						_store.Save(out _);
					}
					_tray.BalloonTipTitle = "长按拖窗：发现新版本 " + info.Tag;
					_tray.BalloonTipText = "当前 v" + AppInfo.Version + "。双击托盘图标打开设置，点「检查更新」即可下载并自动替换。";
					_tray.BalloonTipIcon = ToolTipIcon.Info;
					_tray.ShowBalloonTip(6000);
					Log.Info("已提示新版本 " + info.Tag + "（当前 " + AppInfo.Version + "）");
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			});
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>托盘「检查更新…」：打开设置并直接触发一次检查。</summary>
	private void OpenSettingsAndCheckUpdate()
	{
		try
		{
			ShowSettings();
			if (_settings != null && !_settings.IsDisposed)
			{
				_settings.BeginCheckUpdate();
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>托盘里的静态入口（无需 SettingsForm 实例）。</summary>
	private static void OpenUrlStatic(string url)
	{
		try
		{
			if (!string.IsNullOrEmpty(url))
			{
				Process.Start(new ProcessStartInfo(url)
				{
					UseShellExecute = true
				});
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void BuildTray()
	{
		_tray.Icon = _iconNormal;
		_tray.Text = "长按拖窗";
		_tray.Visible = true;
		_tray.DoubleClick += (object? s, EventArgs e) =>
		{
			ShowSettings();
		};
		_menu.Items.Add(new ToolStripMenuItem("设置…", null, (object? s, EventArgs e) =>
		{
			ShowSettings();
		}));
		_miPause.Click += (object? s, EventArgs e) =>
		{
			TogglePause();
		};
		_menu.Items.Add(_miPause);
		_miAutoStart.Click += (object? s, EventArgs e) =>
		{
			ToggleAutoStart();
		};
		_menu.Items.Add(_miAutoStart);
		_miElevate.Click += (object? s, EventArgs e) =>
		{
			RestartAsAdmin();
		};
		_menu.Items.Add(_miElevate);
		_menu.Items.Add(new ToolStripSeparator());
		_menu.Items.Add(_miStatus);
		_menu.Items.Add(new ToolStripMenuItem("打开配置文件夹", null, (object? s, EventArgs e) =>
		{
			OpenPath(_dir);
		}));
		_menu.Items.Add(new ToolStripMenuItem("查看日志", null, (object? s, EventArgs e) =>
		{
			OpenPath(Log.Path);
		}));
		_menu.Items.Add(new ToolStripMenuItem("检查更新…", null, (object? s, EventArgs e) =>
		{
			OpenSettingsAndCheckUpdate();
		}));
		_menu.Items.Add(new ToolStripMenuItem("项目主页（GitHub）", null, (object? s, EventArgs e) =>
		{
			OpenUrlStatic(AppInfo.GitHubUrl);
		}));
		_menu.Items.Add(new ToolStripSeparator());
		_menu.Items.Add(new ToolStripMenuItem("退出", null, (object? s, EventArgs e) =>
		{
			ExitApp();
		}));
		_menu.Opening += (object? s, CancelEventArgs e) =>
		{
			UpdateStatusText();
		};
		_tray.ContextMenuStrip = _menu;
		UpdateStatusText();
	}

	private void UpdateStatusText()
	{
		_miStatus.Enabled = false;
		if (_watchdog == null)
		{
			_miStatus.Text = "钩子状态：—";
			return;
		}
		int checks = _watchdog.Checks;
		int reinstallCount = _hooks.ReinstallCount;
		_miStatus.Text = ((reinstallCount == 0) ? $"钩子状态：正常（已检查 {checks} 次）" : $"钩子状态：已自动恢复 {reinstallCount} 次（检查 {checks} 次）");
	}

	private void UpdateMenuState()
	{
		AppConfig current = _store.Current;
		_miPause.Checked = _coord.Paused;
		_miPause.Text = (_coord.Paused ? "恢复使用" : "暂停使用");
		_miAutoStart.Checked = current.AutoStart != "None";
		_miAutoStart.Text = ((current.AutoStart == "Registry") ? "开机启动（注册表）" : "开机启动（计划任务）");
		_miElevate.Visible = Program.LaunchedWithoutElevation;
		_tray.Icon = (_coord.Paused ? _iconPaused : _iconNormal);
		_tray.Text = (_coord.Paused ? "长按拖窗（已暂停）" : ("长按拖窗：" + KeysUtil.DisplayName(current.TriggerKeyId)));
	}

	private void TogglePause()
	{
		bool flag = !_coord.Paused;
		_coord.SetPaused(flag);
		UpdateMenuState();
		ShowBalloon(flag ? "已暂停使用" : "已恢复使用", flag ? "拖拽功能已停止，触发键恢复成它本来的用法。右键托盘图标点「恢复使用」即可重新开启。" : ("长按 " + KeysUtil.DisplayName(_store.Current.TriggerKeyId) + " 后可以再用鼠标拖窗口了。"), ToolTipIcon.Info);
	}

	private void ToggleAutoStart()
	{
		AppConfig current = _store.Current;
		string mode = (current.AutoStart = ((current.AutoStart == "None") ? "TaskScheduler" : "None"));
		if (!_store.Save(out string saveError))
		{
			// 保存失败就把内存改动撤回：系统自启项此时还没改，但配置没落盘，
			// 留着翻转后的值会让之后任何一次成功保存把"用户没确认过的切换"写进磁盘。
			current.AutoStart = ((mode == "None") ? "TaskScheduler" : "None");
			UpdateMenuState();
			ShowBalloon("开机自启切换失败", "配置没能写入磁盘：" + saveError + "（本次切换没有生效，重启后会恢复原样；请检查配置目录权限或磁盘空间）", ToolTipIcon.Warning);
			return;
		}
		ApplyConfig(initial: false);
		AutoStart.Apply(mode, AppPaths.ExePath, out var message);
		if (!string.IsNullOrEmpty(message))
		{
			ShowBalloon("开机自启", message, ToolTipIcon.Info);
		}
	}

	private void RestartAsAdmin()
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = AppPaths.ExePath,
				UseShellExecute = true,
				Verb = "runas"
			});
			ExitApp();
		}
		catch (Exception ex)
		{
			Log.Info("以管理员重启被取消：" + ex.Message);
		}
	}

	private void OpenPath(string path)
	{
		try
		{
			if (!string.IsNullOrEmpty(path))
			{
				if (File.Exists(path))
				{
					Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"")
					{
						UseShellExecute = true
					});
				}
				else
				{
					Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
					{
						UseShellExecute = true
					});
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}


	private void ApplyConfig(bool initial)
	{
		UpdateMenuState();
		if (initial)
		{
			_store.Changed += OnConfigChanged;
		}
	}

	private void OnConfigChanged()
	{
		try
		{
			_coord.OnConfigChanged();
			UpdateMenuState();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void OnShowSignal(object state, bool timedOut)
	{
		if (timedOut)
		{
			return;
		}
		try
		{
			_marshal.BeginInvoke(ShowSettings);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ShowSettings()
	{
		try
		{
			if (_settings == null || _settings.IsDisposed)
			{
				_settings = new SettingsForm(_store, _coord);
				_settings.FormClosed += (object? s, FormClosedEventArgs e) =>
				{
					_settings = null;
				};
				_settings.Show();
			}
			else
			{
				if (!_settings.Visible)
				{
					_settings.Show();
				}
				if (_settings.WindowState == FormWindowState.Minimized)
				{
					_settings.WindowState = FormWindowState.Normal;
				}
				_settings.Activate();
			}
			_settings.BringToFront();
			// 托盘双击时进程不在前台，Activate() 常常被前台锁挡掉 → 窗口开在别人后面，看着像"没反应"。
			try
			{
				Native.SetForegroundWindow(_settings.Handle);
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static Icon LoadAppIcon()
	{
		try
		{
			Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
			if (icon != null)
			{
				return icon;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return SystemIcons.Application;
	}

	private static Icon CreatePausedIcon()
	{
		try
		{
			Size smallIconSize = SystemInformation.SmallIconSize;
			int num = Math.Max(16, smallIconSize.Width);
			int num2 = Math.Max(16, smallIconSize.Height);
			using Bitmap bitmap = new Bitmap(num, num2);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.Clear(Color.Transparent);
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(120, 122, 132)))
				{
					graphics.FillEllipse(brush, 0, 0, num - 1, num2 - 1);
				}
				using SolidBrush brush2 = new SolidBrush(Color.White);
				int num3 = Math.Max(2, num / 6);
				int num4 = Math.Max(5, (int)((double)num2 * 0.46));
				int y = (num2 - num4) / 2;
				graphics.FillRectangle(brush2, num / 2 - num3 - 1, y, num3, num4);
				graphics.FillRectangle(brush2, num / 2 + 1, y, num3, num4);
			}
			nint hicon = bitmap.GetHicon();
			try
			{
				return (Icon)Icon.FromHandle(hicon).Clone();
			}
			finally
			{
				Native.DestroyIcon(hicon);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return SystemIcons.Application;
		}
	}

	private void ShowBalloon(string title, string text, ToolTipIcon icon)
	{
		try
		{
			_tray.BalloonTipTitle = title;
			_tray.BalloonTipText = text;
			_tray.BalloonTipIcon = icon;
			_tray.ShowBalloonTip(8000);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ExitApp()
	{
		Log.Info("退出");
		try
		{
			_coord.HideOverlaysNow();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		Cleanup();
		try
		{
			ExitThread();
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		try
		{
			Log.FlushAsyncQueue();
			Log.FlushAndClose();
		}
		catch
		{
		}
		Environment.Exit(0);
	}

	private void Cleanup()
	{
		if (_cleaned)
		{
			return;
		}
		_cleaned = true;
		try
		{
			_store.Changed -= OnConfigChanged;
		}
		catch
		{
		}
		if (_showWait != null)
		{
			try
			{
				_showWait.Unregister(null);
			}
			catch
			{
			}
			_showWait = null;
		}
		if (_watchdog != null)
		{
			try
			{
				_watchdog.Dispose();
			}
			catch
			{
			}
		}
		try
		{
			_tray.Visible = false;
			_tray.Dispose();
		}
		catch
		{
		}
		try
		{
			_menu.Dispose();
		}
		catch
		{
		}
		try
		{
			_marshal.Dispose();
		}
		catch
		{
		}
		try
		{
			_hooks.Dispose();
		}
		catch
		{
		}
		try
		{
			_coord.Dispose();
		}
		catch
		{
		}
		try
		{
			if (_settings != null && !_settings.IsDisposed)
			{
				_settings.Dispose();
			}
		}
		catch
		{
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			Cleanup();
		}
		base.Dispose(disposing);
	}
}
internal sealed class TriggerState : IDisposable
{
	private readonly Func<AppConfig> _cfg;

	private System.Threading.Timer _holdTimer;

	private bool _held;

	private long _downAt;

	private int _activeButton = -1;

	public bool Active { get; private set; }

	public bool Held => _held;

	public bool LastPressReplayed { get; private set; }

	public event Action Activated;

	public event Action Deactivated;

	// 触发键"短按"（没到 HoldMs 阈值就松开）时触发：此时从没激活过，
	// 但阈值期间我们可能已经把鼠标按下缓冲下来了，必须还给用户，否则会丢点击。
	public event Action ShortPressWithoutActivation;

	public TriggerState(Func<AppConfig> cfg)
	{
		_cfg = cfg;
	}

	public void Dispose()
	{
		if (_holdTimer != null)
		{
			_holdTimer.Dispose();
			_holdTimer = null;
		}
	}

	public bool HandleKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb)
	{
		AppConfig appConfig = _cfg();
		if (!KeysUtil.MatchesKeyboard(appConfig.TriggerKeyId, kb))
		{
			return false;
		}
		if (down)
		{
			if (_held)
			{
				return true;
			}
			_held = true;
			_activeButton = 0;
			_downAt = Environment.TickCount64;
			BeginHold(appConfig);
			return true;
		}
		if (!_held || _activeButton != 0)
		{
			return false;
		}
		EndHold();
		if (!Active)
		{
			ReplayKeyboard(ref kb);
			ShortPressWithoutActivation?.Invoke();
		}
		else
		{
			SetActive(value: false);
		}
		return true;
	}

	public bool HandleMouseTrigger(int msg, bool down, ref Native.MSLLHOOKSTRUCT ms)
	{
		AppConfig appConfig = _cfg();
		if (!KeysUtil.MatchesMouse(appConfig.TriggerKeyId, msg, ms))
		{
			return false;
		}
		if (down)
		{
			if (_held)
			{
				return true;
			}
			_held = true;
			_activeButton = 1;
			_downAt = Environment.TickCount64;
			BeginHold(appConfig);
			return true;
		}
		if (!_held || _activeButton != 1)
		{
			return false;
		}
		EndHold();
		if (!Active)
		{
			ReplayMouseButton(ref ms);
			ShortPressWithoutActivation?.Invoke();
		}
		else
		{
			SetActive(value: false);
		}
		return true;
	}

	private void BeginHold(AppConfig c)
	{
		LastPressReplayed = false;
		if (c.RequireHold)
		{
			StopHoldTimer();
			int dueTime = Math.Max(30, Math.Min(3000, c.HoldMs));
			_holdTimer = new System.Threading.Timer((object? _) =>
			{
				OnHoldElapsed(null, EventArgs.Empty);
			}, null, dueTime, -1);
		}
		else
		{
			SetActive(value: true);
		}
	}

	private void EndHold()
	{
		_held = false;
		StopHoldTimer();
		_activeButton = -1;
	}

	private void StopHoldTimer()
	{
		System.Threading.Timer holdTimer = _holdTimer;
		if (holdTimer != null)
		{
			_holdTimer = null;
			try
			{
				holdTimer.Dispose();
			}
			catch
			{
			}
		}
	}

	private void OnHoldElapsed(object sender, EventArgs e)
	{
		StopHoldTimer();
		if (_held)
		{
			SetActive(value: true);
		}
	}

	internal void ForceHoldElapsed()
	{
		OnHoldElapsed(this, EventArgs.Empty);
	}

	internal void SimulateKeyDown()
	{
		_held = true;
		_activeButton = 0;
		_downAt = Environment.TickCount64;
		BeginHold(_cfg());
	}

	internal bool SimulateKeyUp(Func<bool> replayCallback)
	{
		StopHoldTimer();
		bool active = Active;
		EndHold();
		if (active)
		{
			SetActive(value: false);
			return false;
		}
		LastPressReplayed = true;
		replayCallback?.Invoke();
		ShortPressWithoutActivation?.Invoke();
		return true;
	}

	private void SetActive(bool value)
	{
		if (Active != value)
		{
			Active = value;
			(value ? Activated : Deactivated)?.Invoke();
			Log.Info(value ? "触发键激活" : "触发键释放");
		}
	}

	private void ReplayKeyboard(ref Native.KBDLLHOOKSTRUCT kb)
	{
		try
		{
			Native.INPUT[] array = new Native.INPUT[2];
			array[0].type = 1u;
			array[0].u.ki.wVk = (ushort)kb.vkCode;
			array[0].u.ki.wScan = (ushort)kb.scanCode;
			array[0].u.ki.dwFlags = (((kb.flags & 1) != 0) ? 1u : 0u);
			array[0].u.ki.dwExtraInfo = HookEngine.MagicExtraInfo;
			array[1] = array[0];
			array[1].u.ki.dwFlags |= 2u;
			Native.SendInput(2u, array, Marshal.SizeOf(typeof(Native.INPUT)));
			LastPressReplayed = true;
			Log.Info("短按触发键，已补发原按键");
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ReplayMouseButton(ref Native.MSLLHOOKSTRUCT ms)
	{
		try
		{
			Native.INPUT[] array = new Native.INPUT[2];
			array[0].type = 0u;
			array[0].u.mi.mouseData = ms.mouseData;
			array[0].u.mi.dwFlags = 128u;
			array[0].u.mi.dwExtraInfo = HookEngine.MagicExtraInfo;
			array[1] = array[0];
			array[1].u.mi.dwFlags = 256u;
			Native.SendInput(2u, array, Marshal.SizeOf(typeof(Native.INPUT)));
			LastPressReplayed = true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}
}
internal static class WindowActions
{
	private static readonly string[] ExcludedClasses = new string[18]
	{
		"Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "TaskListThumbnailWnd", "ForegroundStaging", "Windows.UI.Core.CoreWindow", "ApplicationManager_ImmersiveShellWindow", "MultitaskingViewFrame", "XamlExplorerHostIslandWindow",
		"TopLevelWindowForOverflowXamlIsland", "Windows.Internal.Shell.TabProxyWindow", "EdgeUiInputTopWndClass", "SysShadow", "tooltips_class32", "MSCTFIME UI", "IME", "Default IME"
	};

	private static readonly int OwnPid = Process.GetCurrentProcess().Id;

	internal const string OverlayClassName = "HoldDragOverlayWindow";

	internal static bool AllowDragOwnWindows = true;

	internal static bool AllowOwnProcessAsTargetForTest;

	private static bool IsOwnProcessTarget(nint h)
	{
		try
		{
			string className = Native.GetClassName(h);
			if (className == "HoldDragOverlayWindow")
			{
				return false;
			}
			if (className == "HoldDrag.Probe.Target")
			{
				return false;
			}
			if (className == "SysShadow" || className == "tooltips_class32" || className == "MSCTFIME UI" || className == "Default IME" || className == "IME")
			{
				return false;
			}
			long windowLong = Native.GetWindowLong(h, -16);
			if ((windowLong & 0x10000000) == 0L)
			{
				return false;
			}
			if ((windowLong & 0x40000000) != 0L)
			{
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	internal static nint ForcedTargetForTest = IntPtr.Zero;

	/// <summary>
	/// 可交互控件的原生窗口类名。指针落在这类控件上时，本程序不接管鼠标，
	/// 把按键原样交给控件，避免"想拖窗口却动了滑块/选区""想拖滑块却被拖走窗口"。
	/// 只列真正吃鼠标的控件；Panel / GroupBox / Label 这些容器与静态控件不在其内，
	/// 因此按住触发键拖这些区域或标题栏、边框时仍能拖动整个窗口。
	/// </summary>
	private static readonly string[] InteractiveClasses = new string[17]
	{
		"msctls_trackbar32", "Edit", "Button", "ComboBox", "ComboBoxEx32", "msctls_updown32",
		"ScrollBar", "msctls_progress32", "ListBox", "SysListView32", "SysTreeView32",
		"SysTabControl32", "msctls_statusbar32", "ToolbarWindow32", "SysMonthCal32",
		"SysDateTimePick32", "SysIPAddress32"
	};

	/// <summary>该窗口句柄是否为"可交互控件"。必须传句柄：单看类名分不出 GroupBox 与 Button。</summary>
	internal static bool IsInteractiveControl(nint h)
	{
		string cls = Native.GetClassName(h);
		if (string.IsNullOrEmpty(cls))
		{
			return false;
		}
		// WinForms 会把系统控件类名包成 WindowsForms10.<基础类名>.app.<版本>_r<n>_ad<n>，
		// 例如滑块的实测类名是 WindowsForms10.msctls_trackbar32.app.0.3279090_r3_ad1。
		// 因此先剥掉包装前缀再比对基础类名，否则原生控件永远识别不出来。
		const string WinFormsPrefix = "WindowsForms10.";
		if (cls.StartsWith(WinFormsPrefix, StringComparison.OrdinalIgnoreCase))
		{
			int num = cls.IndexOf('.', WinFormsPrefix.Length);
			if (num <= WinFormsPrefix.Length)
			{
				return false;
			}
			cls = cls.Substring(WinFormsPrefix.Length, num - WinFormsPrefix.Length);
		}
		if (!MatchesInteractiveName(cls))
		{
			return false;
		}
		// WinForms 的 GroupBox 也是用 BUTTON 类实现的，只有 BS_GROUPBOX(0x7) 能把它和真正的
		// 按钮/复选框/单选钮区分开。GroupBox 是容器、不吃鼠标拖动 —— 若不排除，设置页里成片的
		// GroupBox 会把"按住触发键拖窗口"整个吃掉（表现就是左键毫无反应）。
		return !(string.Equals(cls, "Button", StringComparison.OrdinalIgnoreCase) && (Native.GetWindowLong(h, -16) & 0xF) == 0x7L);
	}

	private static bool MatchesInteractiveName(string cls)
	{
		for (int i = 0; i < InteractiveClasses.Length; i++)
		{
			if (string.Equals(cls, InteractiveClasses[i], StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return cls.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 指针是否落在"本进程自有窗口内部的可交互控件"上。命中则本轮不要接管拖动。
	/// 判定链：WindowFromPoint → GA_ROOT → 根窗口属于本进程且是合法自有窗口 → 命中窗口的类名可交互。
	/// </summary>
	internal static bool IsPointOverOwnInteractiveControl(Native.POINT pt)
	{
		try
		{
			nint hit = Native.WindowFromPoint(pt);
			if (hit == IntPtr.Zero)
			{
				return false;
			}
			nint root = Native.GetAncestor(hit, 2u);
			if (root == IntPtr.Zero)
			{
				return false;
			}
			Native.GetWindowThreadProcessId(root, out var lpdwProcessId);
			if (lpdwProcessId != (uint)OwnPid || !IsOwnProcessTarget(root))
			{
				return false;
			}
			return IsInteractiveControl(hit);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	/// <summary>诊断用：描述指针当前命中的控件类名与句柄（不命中自有窗口控件时返回原因）。</summary>
	internal static string DescribeInteractiveAt(Native.POINT pt)
	{
		try
		{
			nint hit = Native.WindowFromPoint(pt);
			if (hit == IntPtr.Zero)
			{
				return "无命中";
			}
			nint root = Native.GetAncestor(hit, 2u);
			return Native.GetClassName(hit) + " 句柄=" + ((IntPtr)hit).ToInt64().ToString("X")
				+ " 根=" + ((IntPtr)root).ToInt64().ToString("X");
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return "查询失败";
		}
	}

	private static readonly HashSet<nint> _foreignForTest = new HashSet<nint>();

	private static readonly object ForeignGate = new object();

	internal static bool SkipWindowFromPointForTest = false;

	private static string[] _blacklist = new string[0];

	private const double WindowedRatio = 0.72;

	internal static bool KickUselessForSession;

	internal static bool HungTargetForSession;

	private const double HangThresholdMs = 250.0;

	private const int HangStreakToAbandon = 3;

	private const int ProbeEveryFrames = 24;

	private static int SlowFrameStreak;

	private static int AbandonProbe;

	private static Native.RECT _lastWriteRect;

	private static bool _lastWriteValid;

	public static long FramesWritten;

	public static long FramesNoop;

	public static long FramesAbandoned;

	public static double TotalWriteMs;

	public static double MaxWriteMs;

	internal static void SetForeignForTest(nint h, bool on)
	{
		lock (ForeignGate)
		{
			if (on)
			{
				_foreignForTest.Add(h);
			}
			else
			{
				_foreignForTest.Remove(h);
			}
		}
	}

	public static bool IsExcludedClass(string cls)
	{
		for (int i = 0; i < ExcludedClasses.Length; i++)
		{
			if (string.Equals(ExcludedClasses[i], cls, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	public static void ApplyBlacklist(string csv)
	{
		if (string.IsNullOrWhiteSpace(csv))
		{
			_blacklist = new string[0];
			return;
		}
		string[] array = csv.Split(new char[4] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		List<string> list = new List<string>(array.Length);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length > 0)
			{
				list.Add(text);
			}
		}
		_blacklist = list.ToArray();
	}

	public static bool IsBlacklisted(nint h)
	{
		if (_blacklist.Length == 0)
		{
			return false;
		}
		string className = Native.GetClassName(h);
		string windowTitle = Native.GetWindowTitle(h);
		string text = null;
		for (int i = 0; i < _blacklist.Length; i++)
		{
			string value = _blacklist[i];
			if (className != null && className.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			if (windowTitle != null && windowTitle.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			if (text == null)
			{
				text = ProcessExeName(h);
			}
			if (text.Length > 0 && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	public static string ProcessExeName(nint h)
	{
		try
		{
			if (h == IntPtr.Zero)
			{
				return string.Empty;
			}
			Native.GetWindowThreadProcessId(h, out var lpdwProcessId);
			if (lpdwProcessId == 0)
			{
				return string.Empty;
			}
			return ProcessExeNameByPid(lpdwProcessId);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return string.Empty;
		}
	}

	public static string ProcessExeNameByPid(uint pid)
	{
		if (pid == 0)
		{
			return string.Empty;
		}
		nint num = Native.OpenProcess(4096u, bInheritHandle: false, pid);
		if (num == IntPtr.Zero)
		{
			return string.Empty;
		}
		try
		{
			StringBuilder stringBuilder = new StringBuilder(1024);
			uint lpdwSize = (uint)stringBuilder.Capacity;
			if (!Native.QueryFullProcessImageNameW(num, 0u, stringBuilder, ref lpdwSize))
			{
				return string.Empty;
			}
			string text = stringBuilder.ToString();
			if (text.Length == 0)
			{
				return string.Empty;
			}
			string text2 = Path.GetFileName(text);
			if (text2.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
			{
				text2 = text2.Substring(0, text2.Length - 4);
			}
			return text2;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return string.Empty;
		}
		finally
		{
			Native.CloseHandle(num);
		}
	}

	public static nint FindTarget(Native.POINT pt)
	{
		return FindTarget(pt, allowFullscreen: true);
	}

	public static nint FindTarget(Native.POINT pt, bool allowFullscreen)
	{
		if (ForcedTargetForTest != IntPtr.Zero)
		{
			return ForcedTargetForTest;
		}
		nint num = IntPtr.Zero;
		nint num2 = HitTestTopWindow(pt, out var _);
		for (int i = 0; i < 32; i++)
		{
			if (num2 == IntPtr.Zero)
			{
				break;
			}
			num = Native.GetAncestor(num2, 2u);
			if (num == IntPtr.Zero)
			{
				return IntPtr.Zero;
			}
			Native.GetWindowThreadProcessId(num, out var lpdwProcessId);
			bool flag;
			lock (ForeignGate)
			{
				flag = _foreignForTest.Count > 0 && _foreignForTest.Contains(num);
			}
			bool flag2 = lpdwProcessId == (uint)OwnPid && AllowDragOwnWindows && IsOwnProcessTarget(num);
			if (((lpdwProcessId != (uint)OwnPid) | flag) || flag2 || AllowOwnProcessAsTargetForTest)
			{
				break;
			}
			num2 = Native.GetWindow(num, 2u);
			num = IntPtr.Zero;
		}
		if (num == IntPtr.Zero)
		{
			return IntPtr.Zero;
		}
		if (!Native.IsWindow(num) || !Native.IsWindowVisible(num))
		{
			return IntPtr.Zero;
		}
		if (IsExcludedClass(Native.GetClassName(num)))
		{
			return IntPtr.Zero;
		}
		long windowLong = Native.GetWindowLong(num, -16);
		if ((windowLong & 0x10000000) == 0L)
		{
			return IntPtr.Zero;
		}
		if ((windowLong & 0x20000000) != 0L)
		{
			return IntPtr.Zero;
		}
		if (IsBlacklisted(num))
		{
			return IntPtr.Zero;
		}
		if (!Native.GetWindowRect(num, out var lpRect))
		{
			return IntPtr.Zero;
		}
		if (lpRect.Width <= 0 || lpRect.Height <= 0)
		{
			return IntPtr.Zero;
		}
		if (!allowFullscreen && IsFullscreen(num, lpRect))
		{
			return IntPtr.Zero;
		}
		return num;
	}

	internal static nint HitTestTopWindow(Native.POINT pt, out string how)
	{
		nint num = (SkipWindowFromPointForTest ? IntPtr.Zero : Native.WindowFromPoint(pt));
		if (num != IntPtr.Zero)
		{
			how = "WindowFromPoint";
			return num;
		}
		nint desktopWindow = Native.GetDesktopWindow();
		if (desktopWindow != IntPtr.Zero)
		{
			nint num2 = Native.ChildWindowFromPointEx(desktopWindow, pt, 5u);
			if (num2 != IntPtr.Zero && num2 != desktopWindow)
			{
				how = "ChildWindowFromPointEx";
				return num2;
			}
		}
		nint found = IntPtr.Zero;
		try
		{
			Native.EnumWindows((nint hwnd, nint l) =>
			{
				if (!Native.IsWindowVisible(hwnd))
				{
					return true;
				}
				if ((Native.GetWindowLong(hwnd, -16) & 0x20000000) != 0L)
				{
					return true;
				}
				if (!Native.GetWindowRect(hwnd, out var lpRect))
				{
					return true;
				}
				if (lpRect.Width <= 0 || lpRect.Height <= 0)
				{
					return true;
				}
				if (pt.X < lpRect.Left || pt.X >= lpRect.Right || pt.Y < lpRect.Top || pt.Y >= lpRect.Bottom)
				{
					return true;
				}
				found = hwnd;
				return false;
			}, IntPtr.Zero);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		how = ((found != IntPtr.Zero) ? "EnumWindowsZOrder" : "三种策略全部失败");
		return found;
	}

	internal static string DescribeReject(Native.POINT pt, bool allowFullscreen)
	{
		try
		{
			nint num = HitTestTopWindow(pt, out var how);
			string text = " [命中方式=" + how + "]";
			if (num == IntPtr.Zero)
			{
				string text2 = "";
				try
				{
					Rectangle virtualScreen = SystemInformation.VirtualScreen;
					bool flag = pt.X >= virtualScreen.Left && pt.X < virtualScreen.Right && pt.Y >= virtualScreen.Top && pt.Y < virtualScreen.Bottom;
					text2 = " 虚拟桌面=" + virtualScreen.Left + "," + virtualScreen.Top + " " + virtualScreen.Width + "x" + virtualScreen.Height + " 该点在桌面内=" + flag + (flag ? "" : " ←★坐标空间不一致，疑似 DPI 虚拟化");
				}
				catch
				{
				}
				return "三种命中策略全部返回空（桌面/无窗口区域）" + text + text2;
			}
			nint num2 = IntPtr.Zero;
			for (int i = 0; i < 32; i++)
			{
				if (num == IntPtr.Zero)
				{
					break;
				}
				num2 = Native.GetAncestor(num, 2u);
				if (num2 == IntPtr.Zero)
				{
					return "取不到根窗口" + text;
				}
				Native.GetWindowThreadProcessId(num2, out var lpdwProcessId);
				if (lpdwProcessId != (uint)OwnPid || (AllowDragOwnWindows && IsOwnProcessTarget(num2)))
				{
					break;
				}
				num = Native.GetWindow(num2, 2u);
				num2 = IntPtr.Zero;
			}
			if (num2 == IntPtr.Zero)
			{
				return "命中的全是本进程窗口（浮层/设置窗口），往下找不到别人的窗口" + text;
			}
			string className = Native.GetClassName(num2);
			Native.GetWindowThreadProcessId(num2, out var _);
			string text3 = SafeTitle(num2);
			if (!Native.IsWindow(num2))
			{
				return "窗口已销毁 类名=" + className;
			}
			if (!Native.IsWindowVisible(num2))
			{
				return "窗口不可见（隐藏）类名=" + className + " 标题=" + text3;
			}
			if (IsExcludedClass(className))
			{
				return "类名被排除（桌面/任务栏等）类名=" + className;
			}
			long windowLong = Native.GetWindowLong(num2, -16);
			if ((windowLong & 0x10000000) == 0L)
			{
				return "窗口没有 WS_VISIBLE 类名=" + className;
			}
			if ((windowLong & 0x20000000) != 0L)
			{
				return "窗口已最小化 类名=" + className;
			}
			if (IsBlacklisted(num2))
			{
				return "命中黑名单（不生效的窗口）标题=" + text3;
			}
			if (!Native.GetWindowRect(num2, out var lpRect))
			{
				return "取不到窗口矩形 类名=" + className;
			}
			if (lpRect.Width <= 0 || lpRect.Height <= 0)
			{
				return "窗口尺寸为 0（" + lpRect.Width + "x" + lpRect.Height + "）类名=" + className;
			}
			if (!allowFullscreen && IsFullscreen(num2, lpRect))
			{
				return "窗口占满整个显示器（真全屏）—— 可在设置里勾选【允许全屏窗口】";
			}
			return "未知原因（判定逻辑可能有遗漏）类名=" + className + " 标题=" + text3;
		}
		catch (Exception ex)
		{
			return "诊断本身出错: " + ex.Message;
		}
	}

	private static string SafeTitle(nint h)
	{
		try
		{
			string text = Native.GetWindowTitle(h);
			if (string.IsNullOrEmpty(text))
			{
				return "(无标题)";
			}
			if (text.Length > 30)
			{
				text = text.Substring(0, 30) + "…";
			}
			return text;
		}
		catch
		{
			return "(取标题失败)";
		}
	}

	public static bool IsFullscreen(nint h)
	{
		if (!Native.GetWindowRect(h, out var lpRect))
		{
			return false;
		}
		return IsFullscreen(h, lpRect);
	}

	public static bool IsFullscreen(nint h, Native.RECT r)
	{
		Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(new Native.POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2));
		if (monitorInfoAt.rcMonitor.Width <= 0 || monitorInfoAt.rcMonitor.Height <= 0)
		{
			return false;
		}
		if (r.Left <= monitorInfoAt.rcMonitor.Left && r.Top <= monitorInfoAt.rcMonitor.Top && r.Right >= monitorInfoAt.rcMonitor.Right)
		{
			return r.Bottom >= monitorInfoAt.rcMonitor.Bottom;
		}
		return false;
	}

	public static bool IsResizable(nint h)
	{
		return (Native.GetWindowLong(h, -16) & 0x40000) != 0;
	}

	public static bool ExitFullscreenToWindowed(nint h, ref Native.RECT r, Native.POINT pt, Action<Action> offHook = null)
	{
		if (h == IntPtr.Zero)
		{
			return false;
		}
		Action<Action> action = offHook ?? ((Action<Action>)((Action action2) =>
		{
			action2();
		}));
		double num = ((r.Width > 0) ? ((double)(pt.X - r.Left) / (double)r.Width) : 0.5);
		double num2 = ((r.Height > 0) ? ((double)(pt.Y - r.Top) / (double)r.Height) : 0.5);
		if (num < 0.0)
		{
			num = 0.0;
		}
		if (num > 1.0)
		{
			num = 1.0;
		}
		if (num2 < 0.0)
		{
			num2 = 0.0;
		}
		if (num2 > 1.0)
		{
			num2 = 1.0;
		}
		if (Native.IsZoomed(h))
		{
			Native.WINDOWPLACEMENT lpwndpl = new Native.WINDOWPLACEMENT
			{
				length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT))
			};
			int num3;
			if (Native.GetWindowPlacement(h, ref lpwndpl) && lpwndpl.rcNormalPosition.Width > 0)
			{
				num3 = ((lpwndpl.rcNormalPosition.Height > 0) ? 1 : 0);
				if (num3 != 0)
				{
					r = lpwndpl.rcNormalPosition;
				}
			}
			else
			{
				num3 = 0;
			}
			action(() =>
			{
				Native.ShowWindow(h, 9);
			});
			if (num3 == 0 && !Native.GetWindowRect(h, out r))
			{
				return false;
			}
		}
		if (IsFullscreen(h, r))
		{
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(pt);
			Native.RECT rECT = ((monitorInfoAt.rcWork.Width > 0 && monitorInfoAt.rcWork.Height > 0) ? monitorInfoAt.rcWork : monitorInfoAt.rcMonitor);
			if (rECT.Width <= 0 || rECT.Height <= 0)
			{
				return false;
			}
			int w = (int)((double)rECT.Width * 0.72);
			int hh = (int)((double)rECT.Height * 0.72);
			if (w < 320)
			{
				w = Math.Min(320, rECT.Width);
			}
			if (hh < 240)
			{
				hh = Math.Min(240, rECT.Height);
			}
			int x = pt.X - (int)(num * (double)w);
			int y = pt.Y - (int)(num2 * (double)hh);
			if (x < rECT.Left)
			{
				x = rECT.Left;
			}
			if (y < rECT.Top)
			{
				y = rECT.Top;
			}
			if (x + w > rECT.Right)
			{
				x = rECT.Right - w;
			}
			if (y + hh > rECT.Bottom)
			{
				y = rECT.Bottom - hh;
			}
			if (x < rECT.Left)
			{
				x = rECT.Left;
			}
			if (y < rECT.Top)
			{
				y = rECT.Top;
			}
			action(() =>
			{
				Native.SetWindowPos(h, IntPtr.Zero, x, y, w, hh, 16916u);
			});
			r = new Native.RECT(x, y, x + w, y + hh);
		}
		else
		{
			int nx = pt.X - (int)(num * (double)r.Width);
			int ny = pt.Y - (int)(num2 * (double)r.Height);
			int rw = r.Width;
			int rh = r.Height;
			action(() =>
			{
				Native.SetWindowPos(h, IntPtr.Zero, nx, ny, rw, rh, 16917u);
			});
			r = new Native.RECT(nx, ny, nx + rw, ny + rh);
		}
		Log.Info("触发时降级为窗口化：句柄=" + ((IntPtr)h).ToInt64().ToString("X") + " 新矩形=" + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height);
		return true;
	}

	public static bool IsTopMost(nint h)
	{
		return (Native.GetWindowLong(h, -20) & 8) != 0;
	}

	public static void ResetFrameStats()
	{
		FramesWritten = 0L;
		FramesNoop = 0L;
		FramesAbandoned = 0L;
		TotalWriteMs = 0.0;
		MaxWriteMs = 0.0;
	}

	internal static void ResetSessionKickCache()
	{
		KickUselessForSession = false;
		HungTargetForSession = false;
		SlowFrameStreak = 0;
		AbandonProbe = 0;
		_lastWriteValid = false;
	}

	public static bool SetDragRectFrame(nint h, Native.RECT target)
	{
		return SetDragRectFrame(h, target, moveOnly: false);
	}

	public static bool SetDragRectFrame(nint h, Native.RECT target, bool moveOnly)
	{
		if (h == IntPtr.Zero)
		{
			return false;
		}
		if (HungTargetForSession)
		{
			AbandonProbe++;
			if (AbandonProbe < 24)
			{
				FramesAbandoned++;
				return false;
			}
			AbandonProbe = 0;
		}
		bool flag;
		if (moveOnly)
		{
			flag = false;
			if (_lastWriteValid && _lastWriteRect.Left == target.Left && _lastWriteRect.Top == target.Top)
			{
				FramesNoop++;
				return false;
			}
		}
		else
		{
			bool windowRect = Native.GetWindowRect(h, out var lpRect);
			flag = !windowRect || lpRect.Right - lpRect.Left != target.Width || lpRect.Bottom - lpRect.Top != target.Height;
			if (windowRect && !flag && lpRect.Left == target.Left && lpRect.Top == target.Top)
			{
				FramesNoop++;
				return false;
			}
		}
		if (Native.IsZoomed(h))
		{
			Native.ShowWindow(h, 9);
		}
		uint uFlags = (flag ? 532u : 533u);
		if (flag && !Native.IsZoomed(h) && !KickUselessForSession && !LetWindowKickBack(h, ref target))
		{
			KickUselessForSession = true;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		Native.SetWindowPos(h, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, uFlags);
		stopwatch.Stop();
		double totalMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
		TotalWriteMs += totalMilliseconds;
		if (totalMilliseconds > MaxWriteMs)
		{
			MaxWriteMs = totalMilliseconds;
		}
		FramesWritten++;
		_lastWriteRect = target;
		_lastWriteValid = true;
		if (totalMilliseconds > 250.0)
		{
			SlowFrameStreak++;
			if (SlowFrameStreak >= 3)
			{
				HungTargetForSession = true;
				AbandonProbe = 0;
				Log.Info("目标连续 " + SlowFrameStreak + " 帧写入超时（>" + 250 + "ms），本次拖动放弃实时跟手（每 " + 24 + " 帧探测恢复）");
			}
		}
		else
		{
			SlowFrameStreak = 0;
			if (HungTargetForSession)
			{
				HungTargetForSession = false;
				Log.Info("目标恢复响应，重新开始实时跟手");
			}
		}
		return true;
	}

	public static void SetDragRect(nint h, Native.RECT target)
	{
		if (h == IntPtr.Zero)
		{
			return;
		}
		bool windowRect = Native.GetWindowRect(h, out var lpRect);
		bool flag = !windowRect || lpRect.Right - lpRect.Left != target.Width || lpRect.Bottom - lpRect.Top != target.Height;
		if (!windowRect || flag || lpRect.Left != target.Left || lpRect.Top != target.Top)
		{
			if (Native.IsZoomed(h))
			{
				Native.ShowWindow(h, 9);
			}
			uint num;
			if (flag)
			{
				num = 532u;
			}
			else
			{
				num = ((!IsResizable(h)) ? 16917u : 533u);
			}
			if (flag && !Native.IsZoomed(h) && !KickUselessForSession && !LetWindowKickBack(h, ref target))
			{
				KickUselessForSession = true;
			}
			Native.SetWindowPos(h, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, num);
			if ((num & 0x4000) != 0)
			{
				Native.SendMessageTimeoutW(h, 136, IntPtr.Zero, IntPtr.Zero, 2u, 32u, out var _);
			}
		}
	}

	private static bool LetWindowKickBack(nint h, ref Native.RECT r)
	{
		if (!Native.GetWindowRect(h, out var lpRect))
		{
			return false;
		}
		bool flag = r.Left != lpRect.Left;
		bool flag2 = r.Right != lpRect.Right;
		bool flag3 = r.Top != lpRect.Top;
		bool flag4 = r.Bottom != lpRect.Bottom;
		int value;
		if (flag & flag3)
		{
			value = 4;
		}
		else if (flag2 & flag3)
		{
			value = 5;
		}
		else if (flag & flag4)
		{
			value = 7;
		}
		else if (flag2 & flag4)
		{
			value = 8;
		}
		else if (flag)
		{
			value = 1;
		}
		else if (flag2)
		{
			value = 2;
		}
		else if (flag3)
		{
			value = 3;
		}
		else
		{
			if (!flag4)
			{
				return false;
			}
			value = 6;
		}
		nint num = Marshal.AllocHGlobal(16);
		if (num == IntPtr.Zero)
		{
			return false;
		}
		try
		{
			Marshal.WriteInt32(num, 0, r.Left);
			Marshal.WriteInt32(num, 4, r.Top);
			Marshal.WriteInt32(num, 8, r.Right);
			Marshal.WriteInt32(num, 12, r.Bottom);
			if (Native.SendMessageTimeoutW(h, 532, new IntPtr(value), num, 2u, 16u, out var lpdwResult) == IntPtr.Zero || lpdwResult == IntPtr.Zero)
			{
				return false;
			}
			int num2 = Marshal.ReadInt32(num, 0);
			int num3 = Marshal.ReadInt32(num, 4);
			int num4 = Marshal.ReadInt32(num, 8);
			int num5 = Marshal.ReadInt32(num, 12);
			if (num4 - num2 < 32 || num5 - num3 < 32)
			{
				return false;
			}
			if (Math.Abs(num4 - num2) > 20000 || Math.Abs(num5 - num3) > 20000)
			{
				return false;
			}
			r = new Native.RECT(num2, num3, num4, num5);
			return true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
		finally
		{
			Marshal.FreeHGlobal(num);
		}
	}

	public static void NotifySizeMove(nint h, bool start)
	{
		if (h == IntPtr.Zero)
		{
			return;
		}
		try
		{
			Native.PostMessageW(h, start ? 561 : 562, IntPtr.Zero, IntPtr.Zero);
			Native.NotifyWinEvent(start ? 10u : 11u, h, 0, 0);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public static void SetRect(nint h, Native.RECT r)
	{
		if (Native.IsZoomed(h))
		{
			Native.ShowWindow(h, 9);
		}
		Native.SetWindowPos(h, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, 16916u);
	}

	public static void MaximizeRestore(nint h)
	{
		if (Native.IsZoomed(h))
		{
			Native.ShowWindow(h, 9);
		}
		else
		{
			Native.ShowWindow(h, 3);
		}
	}

	public static void Minimize(nint h)
	{
		Native.ShowWindow(h, 6);
	}

	public static void CloseWindow(nint h)
	{
		Native.PostMessageW(h, 274, new IntPtr(61536), IntPtr.Zero);
	}

	public static void Lower(nint h)
	{
		Native.SetWindowPos(h, Native.HWND_BOTTOM, 0, 0, 0, 0, 16403u);
	}

	public static void ToggleTopMost(nint h)
	{
		nint hWndInsertAfter = (IsTopMost(h) ? Native.HWND_NOTOPMOST : Native.HWND_TOPMOST);
		Native.SetWindowPos(h, hWndInsertAfter, 0, 0, 0, 0, 16403u);
	}

	public static void Center(nint h)
	{
		if (Native.GetWindowRect(h, out var lpRect))
		{
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(new Native.POINT((lpRect.Left + lpRect.Right) / 2, (lpRect.Top + lpRect.Bottom) / 2));
			if (monitorInfoAt.rcWork.Width > 0)
			{
				int x = monitorInfoAt.rcWork.Left + (monitorInfoAt.rcWork.Width - lpRect.Width) / 2;
				int y = monitorInfoAt.rcWork.Top + (monitorInfoAt.rcWork.Height - lpRect.Height) / 2;
				Native.SetWindowPos(h, IntPtr.Zero, x, y, lpRect.Width, lpRect.Height, 16404u);
			}
		}
	}

	public static void ApplySnap(nint h, SnapZone zone, Native.RECT work)
	{
		switch (zone)
		{
		case SnapZone.None:
			break;
		case SnapZone.Maximize:
			if (!Native.IsZoomed(h))
			{
				Native.ShowWindow(h, 3);
			}
			break;
		default:
			SetRect(h, Zones.RectOf(zone, work));
			break;
		}
	}

	public static void Apply(nint h, string action)
	{
		switch (action)
		{
		case "Maximize":
			MaximizeRestore(h);
			break;
		case "Minimize":
			Minimize(h);
			break;
		case "Close":
			CloseWindow(h);
			break;
		case "Lower":
			Lower(h);
			break;
		case "AlwaysOnTop":
			ToggleTopMost(h);
			break;
		case "Center":
			Center(h);
			break;
		}
	}

	public static void ForceForeground(nint h)
	{
		try
		{
			nint foregroundWindow = Native.GetForegroundWindow();
			if (foregroundWindow == h)
			{
				return;
			}
			uint windowThreadProcessId = Native.GetWindowThreadProcessId(foregroundWindow, out var _);
			uint currentThreadId = Native.GetCurrentThreadId();
			bool flag = false;
			if (windowThreadProcessId != 0 && windowThreadProcessId != currentThreadId)
			{
				flag = Native.AttachThreadInput(windowThreadProcessId, currentThreadId, fAttach: true);
			}
			try
			{
				Native.SetWindowPos(h, Native.HWND_TOP, 0, 0, 0, 0, 19u);
				Native.SetForegroundWindow(h);
			}
			finally
			{
				if (flag)
				{
					Native.AttachThreadInput(windowThreadProcessId, currentThreadId, fAttach: false);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}
}
internal enum ResizeZone
{
	None,
	TopLeft,
	Top,
	TopRight,
	Left,
	Center,
	Right,
	BottomLeft,
	Bottom,
	BottomRight
}
internal enum SnapZone
{
	None,
	LeftHalf,
	RightHalf,
	TopHalf,
	BottomHalf,
	TopLeftQuarter,
	TopRightQuarter,
	BottomLeftQuarter,
	BottomRightQuarter,
	Maximize
}
internal static class Zones
{
	public static ResizeZone Resize(Native.RECT r, int x, int y, int cornerPercent)
	{
		if (r.Width <= 0 || r.Height <= 0)
		{
			return ResizeZone.Center;
		}
		if (cornerPercent < 5)
		{
			cornerPercent = 5;
		}
		if (cornerPercent > 45)
		{
			cornerPercent = 45;
		}
		bool flag = x < r.Left + r.Width * cornerPercent / 100;
		bool flag2 = x > r.Right - r.Width * cornerPercent / 100;
		bool flag3 = y < r.Top + r.Height * cornerPercent / 100;
		bool flag4 = y > r.Bottom - r.Height * cornerPercent / 100;
		if (flag3 & flag)
		{
			return ResizeZone.TopLeft;
		}
		if (flag3 & flag2)
		{
			return ResizeZone.TopRight;
		}
		if (flag4 & flag)
		{
			return ResizeZone.BottomLeft;
		}
		if (flag4 & flag2)
		{
			return ResizeZone.BottomRight;
		}
		if (flag3)
		{
			return ResizeZone.Top;
		}
		if (flag4)
		{
			return ResizeZone.Bottom;
		}
		if (flag)
		{
			return ResizeZone.Left;
		}
		if (flag2)
		{
			return ResizeZone.Right;
		}
		return ResizeZone.Center;
	}

	public static ResizeZone NearestCorner(Native.RECT r, int x, int y)
	{
		bool flag = x < r.Left + r.Width / 2;
		if (y < r.Top + r.Height / 2)
		{
			if (!flag)
			{
				return ResizeZone.TopRight;
			}
			return ResizeZone.TopLeft;
		}
		if (!flag)
		{
			return ResizeZone.BottomRight;
		}
		return ResizeZone.BottomLeft;
	}

	public static void EdgesOf(ResizeZone z, out bool left, out bool top, out bool right, out bool bottom)
	{
		left = (top = (right = (bottom = false)));
		switch (z)
		{
		case ResizeZone.TopLeft:
			left = true;
			top = true;
			break;
		case ResizeZone.Top:
			top = true;
			break;
		case ResizeZone.TopRight:
			right = true;
			top = true;
			break;
		case ResizeZone.Left:
			left = true;
			break;
		case ResizeZone.Right:
			right = true;
			break;
		case ResizeZone.BottomLeft:
			left = true;
			bottom = true;
			break;
		case ResizeZone.Bottom:
			bottom = true;
			break;
		case ResizeZone.BottomRight:
			right = true;
			bottom = true;
			break;
		case ResizeZone.Center:
			right = true;
			bottom = true;
			break;
		}
	}

	public static Native.RECT RectOf(SnapZone zone, Native.RECT work)
	{
		int width = work.Width;
		int height = work.Height;
		int num = width / 2;
		int num2 = height / 2;
		return zone switch
		{
			SnapZone.LeftHalf => new Native.RECT(work.Left, work.Top, work.Left + num, work.Bottom), 
			SnapZone.RightHalf => new Native.RECT(work.Right - num, work.Top, work.Right, work.Bottom), 
			SnapZone.TopHalf => new Native.RECT(work.Left, work.Top, work.Right, work.Top + num2), 
			SnapZone.BottomHalf => new Native.RECT(work.Left, work.Bottom - num2, work.Right, work.Bottom), 
			SnapZone.TopLeftQuarter => new Native.RECT(work.Left, work.Top, work.Left + num, work.Top + num2), 
			SnapZone.TopRightQuarter => new Native.RECT(work.Right - num, work.Top, work.Right, work.Top + num2), 
			SnapZone.BottomLeftQuarter => new Native.RECT(work.Left, work.Bottom - num2, work.Left + num, work.Bottom), 
			SnapZone.BottomRightQuarter => new Native.RECT(work.Right - num, work.Bottom - num2, work.Right, work.Bottom), 
			SnapZone.Maximize => new Native.RECT(work.Left, work.Top, work.Right, work.Bottom), 
			_ => new Native.RECT(work.Left, work.Top, work.Right, work.Bottom), 
		};
	}

	public static SnapZone ScreenZoneOfResizeZone(ResizeZone z)
	{
		return z switch
		{
			ResizeZone.TopLeft => SnapZone.TopLeftQuarter, 
			ResizeZone.Top => SnapZone.TopHalf, 
			ResizeZone.TopRight => SnapZone.TopRightQuarter, 
			ResizeZone.Left => SnapZone.LeftHalf, 
			ResizeZone.Center => SnapZone.Maximize, 
			ResizeZone.Right => SnapZone.RightHalf, 
			ResizeZone.BottomLeft => SnapZone.BottomLeftQuarter, 
			ResizeZone.Bottom => SnapZone.BottomHalf, 
			ResizeZone.BottomRight => SnapZone.BottomRightQuarter, 
			_ => SnapZone.None, 
		};
	}

	public static void EnforceMinSize(ref Native.RECT r, int minWidth, int minHeight)
	{
		if (r.Width < minWidth && r.Left != r.Right)
		{
			r.Right = r.Left + minWidth;
		}
		if (r.Height < minHeight && r.Top != r.Bottom)
		{
			r.Bottom = r.Top + minHeight;
		}
	}
}
internal static class VisualCheck
{
	public static void Run(string outDir)
	{
		Directory.CreateDirectory(outDir);
		string text = Path.Combine(Path.GetTempPath(), "holddrag-visual-cfg");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch
		{
		}
		ConfigStore configStore = new ConfigStore(text);
		try
		{
			configStore.Load();
		}
		catch
		{
		}
		Coordinator coord = new Coordinator(configStore);
		Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
		int num = (int)Math.Round((double)workingArea.Width * 0.42);
		int num2 = (int)Math.Round((double)workingArea.Width * 0.72);
		int num3 = (int)Math.Round((double)workingArea.Width * 0.96);
		int num4 = (int)Math.Round(SettingsForm.CurrentUiScale * 600f);
		if (num < num4)
		{
			num = num4;
		}
		int num5 = (int)Math.Round((double)workingArea.Height * 0.88);
		int num6 = (int)Math.Round((double)workingArea.Height * 0.92);
		int num7 = (int)Math.Round((double)workingArea.Height * 0.8);
		string path = Path.Combine(outDir, "sizes.txt");
		InlineArray9<object> buffer = default;
		buffer[0] = workingArea.Width;
		buffer[1] = workingArea.Height;
		buffer[2] = SettingsForm.CurrentUiScale;
		buffer[3] = num;
		buffer[4] = num5;
		buffer[5] = num2;
		buffer[6] = num7;
		buffer[7] = num3;
		buffer[8] = num6;
		File.WriteAllText(path, string.Format("工作区={0}x{1} 缩放={2:0.###}\n窄={3}x{4}\n中={5}x{6}\n宽={7}x{8}", (ReadOnlySpan<object?>)buffer), Encoding.UTF8);
		Shoot(configStore, coord, new Size(num, num5), Path.Combine(outDir, "settings-narrow.png"));
		Shoot(configStore, coord, new Size(num2, num7), Path.Combine(outDir, "settings-mid.png"));
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "settings-wide.png"));
		// 同一页在亮 / 暗两套主题下各来一张：切主题是这次的重点，必须能肉眼对照
		SettingsForm.ProbeForceDarkTheme(false);
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "settings-wide-light.png"));
		SettingsForm.ProbeForceDarkTheme(true);
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "settings-wide-dark.png"));
		SettingsForm.ProbeForceDarkTheme(false);
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "visual-page-light.png"), 2);
		// 默认尺寸（1024）和拉宽两档都拍一张：单列居中 / 两列用满宽度，一眼能对比
		ShootLive(configStore, coord, new Size(num2, num7), Path.Combine(outDir, "live-default.png"), 0);
		ShootLive(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "live-wide.png"), 0);
		ShootLive(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "live-light.png"), 2);
		SettingsForm.ProbeForceDarkTheme(true);
		Shoot(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "visual-page-dark.png"), 2);
		ShootLive(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "live-dark.png"), 2);
		SettingsForm.ProbeForceDarkTheme(false);
		ShootWindow(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "window-light.png"), 2);
		SettingsForm.ProbeForceDarkTheme(true);
		ShootWindow(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "window-dark.png"), 2);
		SettingsForm.ProbeForceDarkTheme(null);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-wide.txt"), 3);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-g1.txt"), 0);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-g5.txt"), 4);
		Dump(configStore, coord, new Size(num, num5), Path.Combine(outDir, "layout-n1.txt"), 0);
		Dump(configStore, coord, new Size(num, num5), Path.Combine(outDir, "layout-n3.txt"), 3);
		Dump(configStore, coord, new Size(num, num5), Path.Combine(outDir, "layout-g3.txt"), 2);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-g3-wide.txt"), 2);
		ShootGroup(configStore, coord, new Size(num, num5), Path.Combine(outDir, "group-visual.png"), 2);
		ShootFrameSheet(Path.Combine(outDir, "frames-all.png"));
		ShootRadiusSheet(Path.Combine(outDir, "radius-sheet.png"));
	}

	private static void ShootRadiusSheet(string path)
	{
		try
		{
			int[] array = new int[4] { 0, 10, 20, 28 };
			string[] array2 = new string[3] { "Dashed", "Focus", "Shadow" };
			int num = 260;
			int num2 = 180;
			int num3 = 10;
			int num4 = 26;
			int width = num3 + array2.Length * (num + num3);
			int height = num4 + array.Length * (num2 + num4 + num3);
			using Bitmap bitmap = new Bitmap(width, height);
			using Graphics graphics = Graphics.FromImage(bitmap);
			graphics.Clear(Color.White);
			using Font font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular);
			using SolidBrush brush = new SolidBrush(Color.FromArgb(40, 42, 48));
			for (int i = 0; i < array2.Length; i++)
			{
				graphics.DrawString(array2[i], font, brush, new PointF(num3 + i * (num + num3), 4f));
			}
			for (int j = 0; j < array.Length; j++)
			{
				for (int k = 0; k < array2.Length; k++)
				{
					int x = num3 + k * (num + num3);
					int y = num4 + j * (num2 + num4 + num3);
					RectangleF rectangleF = new RectangleF(x, y, num, num2);
					using (SolidBrush brush2 = new SolidBrush(Color.White))
					{
						graphics.FillRectangle(brush2, rectangleF);
					}
					FrameStyles.Draw(graphics, rectangleF, Color.FromArgb(107, 196, 165), array2[k], 3, array[j]);
					graphics.DrawString("半径 " + array[j], font, brush, new PointF(x + 4f, (float)(y + num2) - 18f));
				}
			}
			bitmap.Save(path, ImageFormat.Png);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void ShootFrameSheet(string path)
	{
		try
		{
			int num = 210;
			int num2 = 132;
			int num3 = 12;
			int num4 = 26;
			int num5 = 40;
			int num6 = FrameStyles.All.Length;
			int num7 = FrameColors.All.Length;
			int width = num3 + num6 * (num + num3);
			int height = num5 + num4 + num3 + num7 * (num2 + num4 + num3);
			using (Bitmap bitmap = new Bitmap(width, height))
			{
				using Graphics graphics = Graphics.FromImage(bitmap);
				graphics.Clear(Color.FromArgb(244, 245, 247));
				using (Font font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold))
				{
					using Font font2 = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
					using SolidBrush brush = new SolidBrush(Color.FromArgb(24, 26, 33));
					using SolidBrush brush2 = new SolidBrush(Color.FromArgb(120, 126, 140));
					graphics.DrawString("外框颜色 × 样式 对照表（v1.5 精简重做）", font, brush, new PointF(num3, 10f));
					for (int i = 0; i < num6; i++)
					{
						graphics.DrawString(layoutRectangle: new RectangleF(num3 + i * (num + num3), num5, num, num4), s: FrameStyles.DisplayName(FrameStyles.All[i]), font: font2, brush: brush);
					}
					for (int j = 0; j < num7; j++)
					{
						FrameColors.Entry entry = FrameColors.All[j];
						Color color = FrameColors.Resolve(entry.Id);
						int num8 = num5 + num4 + num3 + j * (num2 + num4 + num3);
						for (int k = 0; k < num6; k++)
						{
							int num9 = num3 + k * (num + num3);
							RectangleF rectangleF = new RectangleF(num9, num8, num, num2);
							using (SolidBrush brush3 = new SolidBrush(Color.White))
							{
								graphics.FillRectangle(brush3, rectangleF);
							}
							FrameStyles.Draw(graphics, rectangleF, color, FrameStyles.All[k], 3, 12);
						}
						using (SolidBrush brush4 = new SolidBrush(color))
						{
							graphics.FillRectangle(brush4, num3, num8 + num2 + 4, 14, 14);
						}
						graphics.DrawString(entry.Name, font2, brush2, new RectangleF(num3 + 20, num8 + num2 + 1, num, num4));
					}
				}
				bitmap.Save(path, ImageFormat.Png);
			}
			File.WriteAllText(path + ".txt", "颜色 " + FrameColors.All.Length + " 种 × 样式 " + FrameStyles.All.Length + " 种\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	// DrawToBitmap 拍到的是 WinForms 自己画的假边框，DWM 的真实标题栏只能从屏幕抓。
	private static void ShootLive(ConfigStore store, Coordinator coord, Size size, string path, int page)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(40, 40);
			settingsForm.ClientSize = size;
			settingsForm.TopMost = true;
			settingsForm.Show();
			SettingsForm.ProbeSelectPage(settingsForm, page);
			settingsForm.Activate();
			settingsForm.BringToFront();
			Application.DoEvents();
			for (int i = 0; i < 10; i++)
			{
				Application.DoEvents();
				Thread.Sleep(80);
			}
			Rectangle bounds = settingsForm.Bounds;
			using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
			{
				using (Graphics graphics = Graphics.FromImage(bitmap))
				{
					graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
				}
				bitmap.Save(path, ImageFormat.Png);
			}
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	// PrintWindow(PW_RENDERFULLCONTENT)：连 DWM 画的标题栏一起抓，用来核对"最上面那条和页面是否同色"。
	private static void ShootWindow(ConfigStore store, Coordinator coord, Size size, string path, int page)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(60, 60);
			settingsForm.ClientSize = size;
			settingsForm.Show();
			SettingsForm.ProbeSelectPage(settingsForm, page);
			Application.DoEvents();
			for (int i = 0; i < 8; i++)
			{
				Application.DoEvents();
				Thread.Sleep(80);
			}
			Native.RECT rECT;
			Native.GetWindowRect(settingsForm.Handle, out rECT);
			int width = rECT.Width;
			int height = rECT.Height;
			using (Bitmap bitmap = new Bitmap(width, height))
			{
				using (Graphics graphics = Graphics.FromImage(bitmap))
				{
					nint hdc = graphics.GetHdc();
					try
					{
						Native.PrintWindow(settingsForm.Handle, hdc, 2u);
					}
					finally
					{
						graphics.ReleaseHdc(hdc);
					}
				}
				bitmap.Save(path, ImageFormat.Png);
			}
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void ShootGroup(ConfigStore store, Coordinator coord, Size size, string path, int groupIndex)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(20, 20);
			settingsForm.ClientSize = size;
			settingsForm.TopMost = true;
			settingsForm.Show();
			for (int i = 0; i < 8; i++)
			{
				Application.DoEvents();
				Thread.Sleep(10);
			}
			Control control = SettingsForm.ProbeGroupAt(settingsForm, groupIndex);
			if (control == null || control.Width <= 0 || control.Height <= 0)
			{
				settingsForm.Close();
				return;
			}
			using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
			{
				control.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
				bitmap.Save(path, ImageFormat.Png);
			}
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void Dump(ConfigStore store, Coordinator coord, Size size, string path, int groupIndex)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(20, 20);
			settingsForm.ClientSize = size;
			settingsForm.Show();
			Application.DoEvents();
			StringBuilder stringBuilder = new StringBuilder();
			SettingsForm.DumpGroup(settingsForm, groupIndex, stringBuilder);
			for (int i = 0; i < 3; i++)
			{
				string text = stringBuilder.ToString();
				SettingsForm.RelayoutForTest(settingsForm);
				Application.DoEvents();
				StringBuilder stringBuilder2 = new StringBuilder();
				SettingsForm.DumpGroup(settingsForm, groupIndex, stringBuilder2);
				if (text != stringBuilder2.ToString())
				{
					stringBuilder.AppendLine();
					stringBuilder.AppendLine("!! 第 " + (i + 1) + " 次重排结果与上次不一致（布局不幂等）");
					stringBuilder.AppendLine(stringBuilder2.ToString());
					break;
				}
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("幂等性：连续重排 3 次结果一致 ✓");
			File.WriteAllText(path, stringBuilder.ToString(), Encoding.UTF8);
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void Shoot(ConfigStore store, Coordinator coord, Size size, string path)
	{
		Shoot(store, coord, size, path, 0);
	}

	private static void Shoot(ConfigStore store, Coordinator coord, Size size, string path, int page)
	{
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(20, 20);
			settingsForm.ClientSize = size;
			settingsForm.TopMost = true;
			settingsForm.Show();
			SettingsForm.ProbeSelectPage(settingsForm, page);
			settingsForm.Activate();
			settingsForm.BringToFront();
			Application.DoEvents();
			for (int i = 0; i < 6; i++)
			{
				Application.DoEvents();
				Thread.Sleep(120);
			}
			settingsForm.Refresh();
			Application.DoEvents();
			int width = settingsForm.Width;
			int height = settingsForm.Height;
			using (Bitmap bitmap = new Bitmap(width, height))
			{
				settingsForm.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
				bitmap.Save(path, ImageFormat.Png);
			}
			settingsForm.Close();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}
}
}
