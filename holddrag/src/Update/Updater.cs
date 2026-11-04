// HoldDrag · Update/Updater.cs

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

	/// <summary>
	/// "接替启动"：新进程要等旧进程退出（释放单实例互斥体）后再接管，而不是发现已有实例就退出。
	/// 更新、提权重启、以管理员身份重启都会带上它——这三条路径都是"新进程先起、旧进程后关"，
	/// 少了它就会出现"更新/重启完成之后托盘里一个进程都没有"。
	/// </summary>
	public const string ArgTakeOver = "--take-over";

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
			// 0) 替换前先验一遍：下载到的必须真的是一个 PE 程序。
			//    没有这一步时，被热点/代理替换成网页的 "下载" 会被直接搬到正式文件名上，程序当场打不开。
			if (!LooksLikeWindowsExe(downloadedPath, out var error0))
			{
				error = error0;
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
				error = Loc.T("无法用新版本覆盖程序文件：") + ex2.Message;
				return false;
			}
			// 4) 拉起新版本；把 .old 路径传给它，由新版启动后清理。
			//    带上 --take-over：新进程会等旧进程真正退出（释放单实例锁）再接管，
			//    否则"新进程先起、旧进程后关"会走到"已有实例在运行"分支，更新完一个进程都不剩。
			Process process = null;
			try
			{
				process = Process.Start(new ProcessStartInfo(exePath)
				{
					UseShellExecute = true,
					Arguments = ArgUpdatedFrom + " " + QuoteArg(text) + " " + ArgTakeOver
				});
			}
			catch (Exception ex4)
			{
				Log.Error("启动新版本失败：" + ex4.Message);
			}
			if (process == null)
			{
				// 起了但没起来：把目录恢复成"能用的旧版"，别让用户手里一个能运行的程序都没有
				RestoreBackup(downloadedPath, exePath, text);
				error = Loc.T("新版本已替换，但没能自动启动，已恢复为原来的版本。请到发布页手动下载：") + AppInfo.ReleasesUrl;
				return false;
			}
			Log.Info("新版已就位并启动 pid=" + process.Id + "，旧版备份 " + text);
			return true;
		}
		catch (Exception ex5)
		{
			error = Describe(ex5);
			Log.Error("替换失败：" + error);
			return false;
		}
	}

	/// <summary>把"已经换上去的新文件"退回下载名、把 .old 换回正式名（失败只记日志）。</summary>
	private static void RestoreBackup(string downloadedPath, string exePath, string oldPath)
	{
		try
		{
			if (File.Exists(exePath) && !File.Exists(downloadedPath))
			{
				File.Move(exePath, downloadedPath);
			}
		}
		catch (Exception ex)
		{
			Log.Error("回滚：新文件退回失败：" + ex.Message);
		}
		try
		{
			if (File.Exists(oldPath))
			{
				File.Move(oldPath, exePath, overwrite: true);
			}
		}
		catch (Exception ex2)
		{
			Log.Error("回滚：旧文件恢复失败（旧版仍在 " + oldPath + "）：" + ex2.Message);
		}
	}

	/// <summary>
	/// 粗验一个文件是不是 Windows 可执行程序：MZ 头 + PE 签名。
	/// 这不是安全校验（不验签名），目的是拦住"下载回来其实是 HTML 错误页 / 半截文件"这类最常见的更新事故。
	/// </summary>
	internal static bool LooksLikeWindowsExe(string path, out string error)
	{
		error = string.Empty;
		try
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
			{
				error = Loc.T("文件不存在：") + path;
				return false;
			}
			using FileStream fileStream = File.OpenRead(path);
			if (fileStream.Length < 4096)
			{
				error = Loc.T("文件太小，不像一个完整的程序：") + fileStream.Length + Loc.T(" 字节");
				return false;
			}
			byte[] array = new byte[64];
			if (fileStream.Read(array, 0, array.Length) < array.Length || array[0] != 77 || array[1] != 90)
			{
				error = Loc.T("下载到的内容不是 Windows 程序（文件头不是 MZ），可能被网络中途替换成了网页。");
				return false;
			}
			int num = BitConverter.ToInt32(array, 60);
			if (num <= 0 || num > fileStream.Length - 4)
			{
				error = Loc.T("下载到的程序文件头损坏（PE 偏移越界）。");
				return false;
			}
			fileStream.Position = num;
			byte[] array2 = new byte[4];
			if (fileStream.Read(array2, 0, 4) < 4 || array2[0] != 80 || array2[1] != 69 || array2[2] != 0 || array2[3] != 0)
			{
				error = Loc.T("下载到的程序文件头损坏（缺少 PE 签名）。");
				return false;
			}
			return true;
		}
		catch (Exception ex)
		{
			error = Loc.T("校验下载文件时出错：") + Describe(ex);
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

	/// <summary>
	/// 当前生效的 GitHub 个人访问令牌（空 = 匿名请求）。由配置加载/保存时写入，后台线程只读。
	/// v1.7.0：填了就带 Authorization 头请求 api.github.com，限额 60 → 5000 次/小时。
	/// </summary>
	internal static volatile string Token;

	/// <summary>是否配了令牌（空串 = 没配）。界面与提示据此决定"自动下载"还是"只给下载链接"。</summary>
	public static bool HasToken => !string.IsNullOrEmpty(Token);

	/// <summary>
	/// 把用户粘进来的东西整理成纯令牌：
	/// 去首尾空白 → 去 "Bearer " / "token " 前缀 → 去历史「用户名:令牌」前缀（Obtainium 老格式）→
	/// 去掉换行/零宽字符 → 限制 255 字符。真实 GitHub 令牌里不含冒号，所以按第一个冒号切是安全的。
	/// </summary>
	public static string NormalizeToken(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return string.Empty;
		}
		string text = raw.Trim();
		if (text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring(7).Trim();
		}
		else if (text.StartsWith("token ", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring(6).Trim();
		}
		int num = text.IndexOf(':');
		if (num > 0)
		{
			text = text.Substring(num + 1).Trim();
		}
		StringBuilder stringBuilder = new StringBuilder(text.Length);
		foreach (char c in text)
		{
			if (!char.IsWhiteSpace(c) && c != '\u200b' && c != '\ufeff' && c != '\u00a0')
			{
				stringBuilder.Append(c);
			}
		}
		text = stringBuilder.ToString();
		if (text.Length > 255)
		{
			text = text.Substring(0, 255);
		}
		return text;
	}

	/// <summary>界面/日志里显示的脱敏令牌：只留前 4 位与末 4 位。</summary>
	public static string MaskToken(string token)
	{
		string text = NormalizeToken(token);
		if (text.Length == 0)
		{
			return Loc.T("（未填写）");
		}
		if (text.Length <= 8)
		{
			return "****";
		}
		return text.Substring(0, 4) + "…" + text.Substring(text.Length - 4);
	}

	/// <summary>Authorization 头的值；空令牌返回 null（= 不带头，走匿名请求）。</summary>
	public static string AuthHeaderValue(string token)
	{
		string text = NormalizeToken(token);
		return string.IsNullOrEmpty(text) ? null : ("token " + text);
	}

	/// <summary>给一次请求带上 GitHub 接口需要的头（令牌可选）。</summary>
	private static void ApplyAuth(HttpRequestMessage request, string token)
	{
		request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
		request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
		string text = AuthHeaderValue(token);
		if (text != null)
		{
			request.Headers.TryAddWithoutValidation("Authorization", text);
		}
	}

	/// <summary>
	/// 401/403 且正文说令牌有问题（判据同 Obtainium：message 含 bad credentials / access token）。
	/// 注意限额 403 的正文是 "API rate limit exceeded"，不含这两个词，所以不会被误判。
	/// </summary>
	public static bool IsAuthRejection(int status, string body)
	{
		if (status != 401 && status != 403)
		{
			return false;
		}
		if (string.IsNullOrEmpty(body))
		{
			return false;
		}
		string text = body.ToLowerInvariant();
		return text.Contains("bad credentials") || text.Contains("access token") || text.Contains("requires authentication");
	}

	/// <summary>限额用尽时给用户看的说明（含重置时间与"已改用网页方式"这个事实）。</summary>
	public static string FormatRateLimitMessage(int waitMinutes, bool withToken)
	{
		int num = Math.Max(1, waitMinutes);
		return Loc.T("GitHub 接口限额已用尽（") + (withToken ? Loc.T("令牌 5000 次/小时") : Loc.T("匿名 60 次/小时")) + Loc.T("），约 ") + num + Loc.T(" 分钟后重置；已自动改用网页方式检查");
	}

	/// <summary>「验证令牌」的结果分类（用于配色与自测断言）。</summary>
	public enum TokenCheckResult
	{
		/// <summary>令牌有效，接口能读到发布信息。</summary>
		Valid,
		/// <summary>没填令牌，无从验证。</summary>
		NotConfigured,
		/// <summary>401：令牌无效或已过期。</summary>
		Invalid,
		/// <summary>403/404：令牌有效但读不到本仓库（fine-grained 没勾这个仓库）。</summary>
		NoAccess,
		/// <summary>令牌有效但额度已用尽（等重置即可）。</summary>
		RateLimited,
		/// <summary>网络不通 / 服务端异常，无法判断令牌好坏。</summary>
		Network
	}

	/// <summary>
	/// 把验证请求的响应归类。纯函数，自测直接打表断言（真实网络没法在自检里跑）。
	/// 顺序很重要：先看限额（403 也可能是限额），再看 401/正文，最后才是 403/404 无权限。
	/// </summary>
	public static TokenCheckResult ClassifyVerifyResponse(int status, string body, bool rateLimited)
	{
		if (status >= 200 && status < 300)
		{
			return TokenCheckResult.Valid;
		}
		if (rateLimited)
		{
			return TokenCheckResult.RateLimited;
		}
		if (status == 401)
		{
			return TokenCheckResult.Invalid;
		}
		if (IsAuthRejection(status, body))
		{
			return TokenCheckResult.Invalid;
		}
		if (status == 403 || status == 404)
		{
			return TokenCheckResult.NoAccess;
		}
		return TokenCheckResult.Network;
	}

	/// <summary>
	/// 真的向 GitHub 问一次：用这个令牌请求「检查更新」实际使用的那条接口（releases/latest）。
	/// 成功时顺带回报读到的最新 tag 与剩余额度；失败按 401 / 403 / 404 / 限额 / 网络分类给出可读原因。
	/// </summary>
	public static TokenCheckResult VerifyToken(string rawToken, out string message)
	{
		message = string.Empty;
		string text = NormalizeToken(rawToken);
		if (string.IsNullOrEmpty(text))
		{
			message = Loc.T("还没填令牌，没什么可验证的");
			return TokenCheckResult.NotConfigured;
		}
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, AppInfo.ReleasesApiUrl);
			ApplyAuth(request, text);
			using HttpResponseMessage response = Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
			string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
			int status = (int)response.StatusCode;
			string rateLimitNote;
			bool rateLimited = TryGetRateLimitWaitMinutes(response, withToken: true, out rateLimitNote);
			TokenCheckResult result = ClassifyVerifyResponse(status, body, rateLimited);
			switch (result)
			{
			case TokenCheckResult.Valid:
			{
				string text2 = null;
				try
				{
					using JsonDocument jsonDocument = JsonDocument.Parse(body);
					JsonElement value;
					if (jsonDocument.RootElement.TryGetProperty("tag_name", out value))
					{
						text2 = value.GetString();
					}
				}
				catch
				{
				}
				string remaining = HeaderValue(response, "x-ratelimit-remaining");
				string limit = HeaderValue(response, "x-ratelimit-limit");
				message = Loc.T("令牌有效") + (string.IsNullOrEmpty(text2) ? Loc.T("（接口有响应）") : (Loc.T("，读到最新版本 ") + text2));
				if (!string.IsNullOrEmpty(remaining) && !string.IsNullOrEmpty(limit))
				{
					message += Loc.T("；接口额度剩余 ") + remaining + "/" + limit;
				}
				break;
			}
			case TokenCheckResult.Invalid:
				message = Loc.T("令牌无效或已过期（HTTP 401）——请重新复制一个令牌；若刚改过权限，重新生成一个再试");
				break;
			case TokenCheckResult.NoAccess:
				message = Loc.T("令牌有效，但读不到本仓库（HTTP ") + status + Loc.T("）——fine-grained 令牌请在 Resource owner → Repositories 里勾选 QQQOrange/holddrag");
				break;
			case TokenCheckResult.RateLimited:
				message = rateLimitNote + Loc.T("（令牌本身没问题，等重置即可）");
				break;
			default:
				message = Loc.T("没法验证：HTTP ") + status + Loc.T("（服务端异常或网络中间设备拦截）");
				break;
			}
			Log.Info("验证令牌：" + result + " | " + message);
			return result;
		}
		catch (Exception ex)
		{
			message = Loc.T("网络不通，没法验证：") + Describe(ex);
			Log.Info("验证令牌失败：" + ex.Message);
			return TokenCheckResult.Network;
		}
	}

	private static string HeaderValue(HttpResponseMessage response, string name)
	{
		IEnumerable<string> values;
		if (!response.Headers.TryGetValues(name, out values))
		{
			return null;
		}
		foreach (string value in values)
		{
			return value.Trim();
		}
		return null;
	}

	/// <summary>响应是否就是"限额用尽"（x-ratelimit-remaining=0 或带 retry-after）。</summary>
	private static bool TryGetRateLimitWaitMinutes(HttpResponseMessage response, bool withToken, out string note)
	{
		note = null;
		if (response.IsSuccessStatusCode)
		{
			return false;
		}
		string text = HeaderValue(response, "x-ratelimit-remaining");
		string text2 = HeaderValue(response, "retry-after");
		if (!string.Equals(text, "0", StringComparison.Ordinal) && string.IsNullOrEmpty(text2))
		{
			return false;
		}
		long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		int waitMinutes = 1;
		long result;
		if (long.TryParse(HeaderValue(response, "x-ratelimit-reset"), out result) && result > num)
		{
			waitMinutes = (int)Math.Ceiling((double)(result - num) / 60.0);
		}
		else
		{
			int result2;
			if (int.TryParse(text2, out result2))
			{
				waitMinutes = (int)Math.Ceiling((double)result2 / 60.0);
			}
		}
		note = FormatRateLimitMessage(waitMinutes, withToken);
		return true;
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
		return CheckLatest(out error, out upToDate, out _);
	}

	/// <summary>
	/// 查最新发布。<paramref name="note"/> 回传"这次是怎么查的"的补充说明（令牌无效 / 限额用尽后改走网页），
	/// 供界面在状态后面如实标注——不静默吞掉降级原因。
	/// </summary>
	public static UpdateInfo CheckLatest(out string error, out bool upToDate, out string note)
	{
		error = string.Empty;
		upToDate = false;
		note = string.Empty;
		if (ForceOfflineForTest)
		{
			error = Loc.T("（测试）模拟网络不可达：无法连接 github.com");
			return null;
		}
		try
		{
			string tag = ResolveLatestTag(out note);
			if (string.IsNullOrEmpty(tag))
			{
				error = Loc.T("没能从发布页解析出最新版本号（可能没有已发布的 Release，或网络被拦截）。") + (string.IsNullOrEmpty(note) ? "" : ("（" + note + "）"));
				return null;
			}
			Version version = ParseVersion(tag);
			if (version == null)
			{
				error = Loc.T("发布标签格式无法识别：") + tag;
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

	/// <summary>
	/// 解析最新版本号。v1.7.0 起的顺序：
	///   1) 配了令牌 → 先问 api.github.com（带 Authorization，返回精确 tag，限额 5000 次/小时）；
	///   2) 接口这一路失败（限额用尽 / 令牌无效 / 网络不通）→ 退回网页 releases/latest。
	/// 网页这条本来就不消耗接口限额，所以"接口被限流"也不会让检查更新直接失败——这就是绕过限额的兜底。
	/// </summary>
	private static string ResolveLatestTag(out string note)
	{
		note = string.Empty;
		string token = Token;
		if (!string.IsNullOrEmpty(token))
		{
			string text;
			string text2 = ResolveLatestTagViaApi(token, out text);
			if (!string.IsNullOrEmpty(text2))
			{
				return text2;
			}
			note = text ?? string.Empty;
			Log.Info("接口路径没取到版本号，改用网页路径：" + note);
		}
		return ResolveLatestTagViaHtml();
	}

	/// <summary>带令牌问接口；任何失败都只回传 note，由调用方决定是否改走网页（不在这里抛）。</summary>
	private static string ResolveLatestTagViaApi(string token, out string note)
	{
		note = string.Empty;
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, AppInfo.ReleasesApiUrl);
			ApplyAuth(request, token);
			using HttpResponseMessage response = Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
			string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
			if (response.IsSuccessStatusCode)
			{
				using JsonDocument jsonDocument = JsonDocument.Parse(text);
				JsonElement value;
				if (jsonDocument.RootElement.TryGetProperty("tag_name", out value))
				{
					string text2 = value.GetString();
					if (!string.IsNullOrWhiteSpace(text2))
					{
						return text2.Trim();
					}
				}
				note = Loc.T("接口返回里没有 tag_name");
				return null;
			}
			string text3;
			if (TryGetRateLimitWaitMinutes(response, !string.IsNullOrEmpty(token), out text3))
			{
				note = text3;
				return null;
			}
			int num = (int)response.StatusCode;
			if (IsAuthRejection(num, text))
			{
				note = Loc.T("令牌无效或没有该仓库的读取权限（HTTP ") + num + Loc.T("），已自动改用匿名方式检查");
				return null;
			}
			note = Loc.T("接口返回 HTTP ") + num;
			return null;
		}
		catch (Exception ex)
		{
			note = Loc.T("接口请求失败：") + Describe(ex);
			Log.Info("api.github.com 请求失败（会改走网页路径）：" + ex.Message);
			return null;
		}
	}

	/// <summary>网页兜底：跟随 releases/latest 的 302 拿 tag；拿不到再从正文里找。</summary>
	private static string ResolveLatestTagViaHtml()
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
	/// <param name="cancel">
	/// 取消令牌：每读一块检查一次，取消后立刻停止并删掉 .part。
	/// 注意不能只靠"关闭窗口"来中断——窗口关掉而下载线程还在跑，替换照样会发生。
	/// </param>
	public static bool Download(UpdateInfo info, string targetPath, Action<long, long> progress, out string error, CancellationToken cancel = default)
	{
		error = string.Empty;
		string text = targetPath + ".part";
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, info.AssetUrl);
			using HttpResponseMessage response = Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).GetAwaiter().GetResult();
			if (!response.IsSuccessStatusCode)
			{
				error = Loc.T("下载失败：HTTP ") + (int)response.StatusCode + "（" + response.ReasonPhrase + "）";
				return false;
			}
			long? contentLength = response.Content.Headers.ContentLength;
			long num = contentLength.GetValueOrDefault();
			long num2 = 0L;
			byte[] array = new byte[81920];
			// 读超时：连接卡住时不能让下载线程永远阻塞（取消令牌只有在 Read 返回后才会被看到）。
			// 每读到一块就重新计时，所以"慢但一直有进展"的下载不会被误杀。
			using CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
			cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(60));
			using (Stream stream = response.Content.ReadAsStreamAsync(cancel).GetAwaiter().GetResult())
			using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: false))
			{
				int num3;
				while ((num3 = stream.ReadAsync(array, 0, array.Length, cancellationTokenSource.Token).GetAwaiter().GetResult()) > 0)
				{
					// 每块都查一次：点「取消下载」后最迟一块（约 80 KB）就停下来
					if (cancel.IsCancellationRequested)
					{
						throw new OperationCanceledException(cancel);
					}
					fileStream.Write(array, 0, num3);
					num2 += num3;
					progress?.Invoke(num2, num);
					cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(60));
				}
				fileStream.Flush(flushToDisk: true);
			}
			if (num2 <= 0)
			{
				error = Loc.T("下载得到 0 字节，可能是被网络中间设备拦截了。");
				return false;
			}
			if (num > 0 && num2 != num)
			{
				error = Loc.T("下载不完整：期望 ") + num + Loc.T(" 字节，实际 ") + num2 + Loc.T(" 字节。");
				return false;
			}
			if (!LooksLikeWindowsExe(text, out var error2))
			{
				error = error2;
				Log.Error("下载内容校验失败：" + error2);
				return false;
			}
			File.Move(text, targetPath, overwrite: true);
			return true;
		}
		catch (OperationCanceledException)
		{
			// 取消不是"失败"：不弹错误框，调用方据 token 自行处理
			error = Loc.T("已取消下载");
			Log.Info("更新下载已被用户取消");
			return false;
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
			text += Loc.T("（网络不可达：请检查代理 / 防火墙是否允许访问 github.com）");
		}
		return text;
	}
}
