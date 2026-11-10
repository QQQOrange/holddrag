// HoldDrag · App/AppInfo.cs

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

internal static class AppInfo
{
	public const string Name = "长按拖窗";

	public const string EnglishName = "HoldDrag";

	public const string Tagline = "长按触发键，随手拖动 / 缩放任意窗口";

	public const string ReleaseDate = "2026-11-04";

	public const string Developer = "QQQOrange";

	public const string Tools = "DeepSeek";

	public const string RepoOwner = "QQQOrange";

	public const string RepoName = "holddrag";

	public const string GitHubUrl = "https://github.com/QQQOrange/holddrag";

	public const string ReleasesUrl = "https://github.com/QQQOrange/holddrag/releases";

	public const string ReleasesApiUrl = "https://api.github.com/repos/QQQOrange/holddrag/releases/latest";

	/// <summary>
	/// GitHub 个人访问令牌（PAT）的官方中文文档。界面上的「?」帮助图标统一从这里取地址，
	/// 以后换文档地址只改这一处（便于维护）。
	/// </summary>
	public const string PatDocsUrl = "https://docs.github.com/zh/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens";

	/// <summary>程序集版本读不到时的兜底版本号。升级版本时这里与 AssemblyInfo 必须同步改。</summary>
	public const string FallbackVersion = "1.16.0";

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
