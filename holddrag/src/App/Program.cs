// HoldDrag · App/Program.cs

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
		// 启动器用 --host 告诉我们"用户手里那个 exe"在哪（更新/自启都要用它）
		AppPaths.HostPath = ArgValue(args, "--host");
		if (!string.IsNullOrEmpty(AppPaths.HostPath))
		{
			Log.Info("外层启动器：" + AppPaths.HostPath);
		}
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
				Updater.CleanupOldVersion(AppPaths.EntryPath + Updater.OldSuffix);
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
				0 => Loc.T("不感知(0)★危险"), 
				1 => Loc.T("系统感知(1)"), 
				2 => Loc.T("每显示器感知(2)"), 
				_ => Loc.T("查不到(") + num3 + ")", 
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
		if (HasArg(args, "--langshot"))
		{
			string langShotLang = ArgValue(args, "--langshot");
			if (string.IsNullOrEmpty(langShotLang))
			{
				langShotLang = Loc.En;
			}
			SettingsForm.DumpLanguageShots(Path.Combine(Path.GetTempPath(), "holddrag-langshot"), langShotLang);
			StressTest.HardExit(0);
			return;
		}
		ConfigStore configStore = new ConfigStore(text2);
		configStore.Load();
		// 语言必须在任何界面（含提权提示、托盘、设置窗口）出现之前定好，
		// 否则第一屏会先闪一段中文再变成英文。
		Loc.Init(configStore.Current.Language);
		// "接替启动"：更新 / 提权重启 / 以管理员重启都是新进程先起、旧进程后关。
		bool takeOver = HasArg(args, Updater.ArgTakeOver) || !string.IsNullOrEmpty(ArgValue(args, Updater.ArgUpdatedFrom));
		// 1) 先看有没有已在运行的实例。必须在提权之前判断：
		//    旧顺序是先提权、后查单实例，于是"只是再点一次图标想打开设置"也会先弹一次 UAC（拒绝后还要再弹一次权限提示）。
		if (!takeOver && NotifyExistingInstance())
		{
			Log.Info("已有实例在运行，已通知它显示设置窗口，本进程退出");
			return;
		}
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
		// 2) 单实例。注意"更新重启"这条特殊路径：
		// 替换时是新版本【先启动】、旧进程【后退出】的，新进程一启动就会发现互斥体已被旧进程占着。
		// 若照普通启动那样"通知已有实例然后自己退出"，更新完就会出现【一个进程都不剩】。
		// 因此接替启动的实例要在这里等旧进程把互斥体释放掉，再接管。
		Mutex obj3 = null;
		bool createdNew = false;
		long num5 = Environment.TickCount64 + (takeOver ? 60000 : 0);
		while (true)
		{
			obj3 = new Mutex(initiallyOwned: true, "Local\\HoldDrag.SingleInstance.v1", out createdNew);
			if (createdNew)
			{
				break;
			}
			if (!takeOver)
			{
				break;
			}
			// 接替启动：旧进程正在退出，等它释放
			try
			{
				obj3.Dispose();
			}
			catch
			{
			}
			if (Environment.TickCount64 > num5)
			{
				Log.Info("接替启动：等待旧实例退出超过 60 秒，旧实例可能已经卡死；本次让位，避免同时存在两个实例");
				break;
			}
			Thread.Sleep(100);
		}
		if (!createdNew)
		{
			Log.Info("已有实例在运行，通知它把设置窗口显示出来");
			NotifyExistingInstance();
			return;
		}
		if (takeOver)
		{
			Log.Info("接替启动：已接管单实例锁");
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
	/// <summary>
	/// `--checkupdate` 的输出正文（不含"当前版本 / 令牌"这两行）。
	/// 抽成纯函数是为了让自检锁住这条规则：**说明与 发现新版本/已是最新/检查失败 必须同时出现**。
	/// （v1.7.0 曾把"说明"写成互斥分支，结果有说明时整段结果被吞掉、退出码还误报成功。）
	/// </summary>
	internal static string DescribeCheckUpdate(UpdateInfo info, bool upToDate, string error, string note, bool hasToken)
	{
		StringBuilder stringBuilder = new StringBuilder();
		if (!string.IsNullOrEmpty(note))
		{
			stringBuilder.AppendLine(Loc.T("说明　　：") + note);
		}
		if (info != null)
		{
			stringBuilder.AppendLine(Loc.T("发现新版本 ") + info.Tag + Loc.T("（当前 v") + AppInfo.Version + Loc.Close);
			stringBuilder.AppendLine(Loc.T("下载地址：") + info.AssetUrl);
			stringBuilder.AppendLine(Loc.T("发布页　：") + info.PageUrl);
			if (!hasToken)
			{
				stringBuilder.AppendLine(Loc.T("　　　　　（未填令牌：程序内不会下载，请用上面的地址在浏览器下载）"));
			}
		}
		else if (upToDate)
		{
			stringBuilder.AppendLine(Loc.T("已是最新版本（v") + AppInfo.Version + Loc.Close);
		}
		else
		{
			stringBuilder.AppendLine(Loc.T("检查失败：") + error);
			stringBuilder.AppendLine(Loc.T("发布页　：") + AppInfo.ReleasesUrl);
		}
		return stringBuilder.ToString().TrimEnd();
	}

	private static void RunCheckUpdateOnly()
	{
		// 先附着父控制台，否则这几行在"从命令行运行"时根本看不见（WinExe 没有自己的控制台）
		try
		{
			SelfTest.AttachConsoleForTest(uint.MaxValue);
		}
		catch
		{
		}
		Console.WriteLine("当前版本：" + AppInfo.Version);
		Console.WriteLine("日志　　：" + (string.IsNullOrEmpty(Log.Path) ? "(不可用)" : Log.Path));
		Log.Info("--checkupdate：当前版本 " + AppInfo.Version);
		// 诊断路径也要认配置里的令牌，否则用户反馈"填了令牌还是被限流"时没法复现真实链路
		try
		{
			ConfigStore configStore = new ConfigStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag"));
			configStore.Load();
			Updater.Token = Updater.NormalizeToken(configStore.Current?.GitHubToken);
			Console.WriteLine("令牌　　：" + (Updater.HasToken ? "已配置（不显示内容）" : "未配置（匿名请求 60 次/小时）"));
		}
		catch (Exception ex)
		{
			Log.Info("--checkupdate 读配置失败（按匿名继续）：" + ex.Message);
		}
		string text = null;
		// 退出码必须由"是否失败"这个事实决定，不能靠中文文案前缀判断：
		// 旧写法在英文界面下（"检查失败：" 被翻译）永远返回 0，脚本化诊断就废了。
		bool failed = false;
		try
		{
			string error;
			bool upToDate;
			string note;
			UpdateInfo updateInfo = Updater.CheckLatest(out error, out upToDate, out note);
			Console.WriteLine(DescribeCheckUpdate(updateInfo, upToDate, error, note, Updater.HasToken));
			if (updateInfo != null)
			{
				text = Loc.T("发现新版本 ") + updateInfo.Tag + Loc.T("（当前 v") + AppInfo.Version + Loc.Close;
			}
			else if (upToDate)
			{
				text = Loc.T("已是最新版本（v") + AppInfo.Version + Loc.Close;
			}
			else
			{
				failed = true;
				text = Loc.T("检查失败：") + error;
			}
			Log.Info("--checkupdate 结果：" + text);
		}
		catch (Exception ex)
		{
			failed = true;
			text = Loc.T("检查抛异常：") + Updater.Describe(ex);
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
			StressTest.HardExit(failed ? 1 : 0);
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

	/// <summary>
	/// 若已有实例在运行，唤起它的设置窗口并返回 true（本进程应直接退出）。
	/// 用"命名事件能否打开"判断存活：进程退出时它持有的句柄会被系统回收，事件随之消失，
	/// 所以不会出现"进程早没了、却以为有人在跑"的假阳性。
	/// </summary>
	private static bool NotifyExistingInstance()
	{
		try
		{
			using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("Local\\HoldDrag.ShowSettings.v1");
			eventWaitHandle.Set();
			return true;
		}
		catch (WaitHandleCannotBeOpenedException)
		{
			return false;
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
			if (MessageBox.Show(Loc.T("当前正在以【普通用户权限】运行。\r\n\r\n这个状态下：\r\n  · 日常拖动浏览器、编辑器、资源管理器等都正常；\r\n  · 但拖不动以管理员身份运行的窗口（例如任务管理器、部分安装程序）。\r\n\r\n建议改用管理员权限重新打开。现在重启吗？\r\n（选\"否\"就保持现状继续使用，功能不受影响）"), Loc.T("长按拖窗 · 权限提示"), MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk, MessageBoxDefaultButton.Button1) != DialogResult.Yes)
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
			// 提权重启同样是"新进程先起、旧进程后关"，必须带 --take-over，
			// 否则新进程一启动就看到旧进程还占着单实例锁，通知一声就退出了 —— 结果是重启完一个进程都没了。
			list.Add(Updater.ArgTakeOver);
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
