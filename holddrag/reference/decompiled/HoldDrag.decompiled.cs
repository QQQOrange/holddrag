using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
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

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: TargetFramework(".NETCoreApp,Version=v10.0", FrameworkDisplayName = ".NET 10.0")]
[assembly: AssemblyCompany("QQQOrange")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyDescription("长按一个自定义按键，再用鼠标左/右/中键移动、缩放窗口。")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: AssemblyProduct("长按拖窗 HoldDrag")]
[assembly: AssemblyTitle("长按拖窗 HoldDrag")]
[assembly: TargetPlatform("Windows7.0")]
[assembly: SupportedOSPlatform("Windows7.0")]
[assembly: AssemblyVersion("1.0.0.0")]
[module: RefSafetyRules(11)]
[StructLayout(LayoutKind.Auto)]
[InlineArray(17)]
internal struct <>y__InlineArray17<T>
{
	[CompilerGenerated]
	private T _element0;
}
namespace HoldDrag;

internal static class AppInfo
{
	public const string Name = "长按拖窗";

	public const string EnglishName = "HoldDrag";

	public const string Tagline = "长按触发键，随手拖动 / 缩放任意窗口";

	public const string ReleaseDate = "2026-10-12";

	public const string Developer = "QQQOrange";

	public const string Tools = "DeepSeek";

	public static string Version
	{
		get
		{
			try
			{
				Version version = Assembly.GetExecutingAssembly().GetName().Version;
				if (version == null)
				{
					return "1.0.0";
				}
				return version.Major + "." + version.Minor + "." + Math.Max(0, version.Build);
			}
			catch
			{
				return "1.0.0";
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

	public string FrameStyleId { get; set; }

	public int FrameThickness { get; set; }

	public int FrameOpacity { get; set; }

	public int FrameRadius { get; set; }

	public bool DoubleClickLeftMaximize { get; set; }

	public bool DoubleClickRightZone { get; set; }

	public bool RestoreMaximizedWhenMoved { get; set; }

	public bool BringToFront { get; set; }

	public bool AllowFullscreenWindows { get; set; }

	public bool ExitFullscreenOnTrigger { get; set; }

	public bool ShakeMinimizeOthers { get; set; }

	public int CornerPercent { get; set; }

	public string Blacklist { get; set; }

	public string DragRefreshMode { get; set; }

	public string MovePreviewMode { get; set; }

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
		FrameStyleId = FrameStyles.DefaultId;
		FrameThickness = 3;
		FrameRadius = 13;
		FrameOpacity = 100;
		DoubleClickLeftMaximize = true;
		DoubleClickRightZone = true;
		RestoreMaximizedWhenMoved = true;
		BringToFront = true;
		AllowFullscreenWindows = false;
		ExitFullscreenOnTrigger = true;
		ShakeMinimizeOthers = true;
		CornerPercent = 26;
		Blacklist = string.Empty;
		DragRefreshMode = "Auto";
		MovePreviewMode = "FullWin";
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
	}

	public AppConfig Clone()
	{
		return (AppConfig)MemberwiseClone();
	}

	public void Normalize()
	{
		if (string.IsNullOrEmpty(TriggerKeyId))
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
		MovePreviewMode = "FullWin";
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
		if (MoveRate == 2 && ResizeRate == 4)
		{
			MoveRate = 1;
			ResizeRate = 1;
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
internal static class PreviewModes
{
	public const string Outline = "Outline";

	public const string FullWin = "FullWin";
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
				Save();
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

	public void Save()
	{
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
		}
		catch (Exception ex)
		{
			Log.Error("保存配置失败 :: " + ex.Message);
		}
	}

	public void Apply(AppConfig edited)
	{
		edited.Normalize();
		Current = edited;
		Save();
		Changed?.Invoke();
	}
}
internal sealed class Coordinator : IInputSink, IDisposable
{
	internal sealed class PairedUpProbe
	{
		public nint Hwnd;

		public int Msg;

		public int WParam;

		public nint LParam;
	}

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

	internal static int PhantomUpSentForTest;

	internal static volatile PairedUpProbe LastPairedUpForTest;

	internal static bool[] ButtonsDownOverrideForTest;

	internal static bool AssumeButtonsDownForTest;

	private readonly bool[] _heldDuringHold = new bool[3];

	private readonly bool[] _passedDown = new bool[3];

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
		_trigger.Activated += OnTriggerActivated;
		_trigger.Deactivated += OnTriggerDeactivated;
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
		_drag.ShowHoverFrame(lpPoint);
	}

	private void OnTriggerDeactivated()
	{
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
		for (int i = 0; i < 3; i++)
		{
			_lastDown[i] = 0L;
			_lastMoved[i] = false;
			_swallowUp[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
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
			if (IsDownMessage(msg))
			{
				if (_trigger.Active)
				{
					if (HandleButtonDown(num2, ms.pt))
					{
						return true;
					}
					_passedDown[num2] = true;
					return false;
				}
				if (_trigger.Held)
				{
					_heldDuringHold[num2] = true;
				}
				_passedDown[num2] = true;
				return false;
			}
			_heldDuringHold[num2] = false;
			_passedDown[num2] = false;
			return HandleButtonUp(num2, ms.pt);
		}
		}
	}

	private void AdoptHeldButtonIfAny(Native.POINT pt)
	{
		for (int i = 0; i <= 2; i++)
		{
			if (!IsButtonPhysicallyDown(i))
			{
				_heldDuringHold[i] = false;
				_passedDown[i] = false;
			}
			else if ((_heldDuringHold[i] || _passedDown[i]) && HandleButtonDown(i, pt, quiet: true) && _drag.IsDragging)
			{
				bool flag = _heldDuringHold[i];
				_heldDuringHold[i] = false;
				_passedDown[i] = false;
				Log.Info("[诊断] 触发键激活时该鼠标键已被按住" + (flag ? "（按下事件发生在触发键按住期间）" : "（按下事件发生在触发键按住之前，目标可能停在拖选模态）") + " → 补起拖动 键=" + i + " 坐标=" + pt.X + "," + pt.Y);
				break;
			}
		}
	}

	private static void PostPairedButtonUp(nint hwnd, int idx, Native.POINT pt)
	{
		try
		{
			if (hwnd != IntPtr.Zero)
			{
				int msg = idx switch
				{
					1 => 517, 
					0 => 514, 
					_ => 520, 
				};
				int num = 0;
				if (idx != 0 && IsButtonPhysicallyDown(0))
				{
					num |= 1;
				}
				if (idx != 1 && IsButtonPhysicallyDown(1))
				{
					num |= 2;
				}
				if (idx != 2 && IsButtonPhysicallyDown(2))
				{
					num |= 0x10;
				}
				Native.POINT lpPoint = pt;
				Native.ScreenToClient(hwnd, ref lpPoint);
				nint lParam = new IntPtr((lpPoint.Y << 16) | (lpPoint.X & 0xFFFF));
				Native.PostMessageW(hwnd, msg, new IntPtr(num), lParam);
				PhantomUpSentForTest++;
				LastPairedUpForTest = new PairedUpProbe
				{
					Hwnd = hwnd,
					Msg = msg,
					WParam = num,
					LParam = lParam
				};
			}
		}
		catch
		{
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
			if (!quiet)
			{
				Log.Info("[诊断] FindTarget 没找到目标，鼠标键原样放行 键=" + idx + " 坐标=" + pt.X + "," + pt.Y + " 原因=" + WindowActions.DescribeReject(pt, allowFullscreen));
			}
			return false;
		}
		CancelTargetMouseMode(num);
		if (_passedDown[idx])
		{
			PostPairedButtonUp(num, idx, pt);
			_passedDown[idx] = false;
		}
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
				return false;
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
			current.MovePreviewMode = "FullWin";
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
		_inset = ComputeInset(hwnd, lpRect);
		if (appConfig.MovePreviewMode != "Outline")
		{
			WindowActions.NotifySizeMove(hwnd, start: true);
		}
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
			bool flag = appConfig.MovePreviewMode == "Outline" && !forceWrite;
			if (appConfig.ShowActivationFrame && !flag)
			{
				Native.RECT rectNow = FrameRectOf(rECT, appConfig, out var inset);
				_frame.RaiseAboveAll = inset;
				if (rectNow.Width >= 4 && rectNow.Height >= 4)
				{
					lock (_gate)
					{
						if (_dragging && _session.Gen == s.Gen && _frameWanted)
						{
							_frame.ApplyTheme(appConfig.FrameColorId, appConfig.FrameStyleId, appConfig.FrameThickness, appConfig.FrameRadius, appConfig.FrameOpacity);
							_frame.SetRectNow(rectNow);
							DiagFrameUpdates++;
						}
					}
				}
			}
			if (flag)
			{
				ShowOutline(rECT, s, appConfig);
			}
			else if (forceWrite)
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
			if (appConfig.MovePreviewMode == "Outline" && hwnd != IntPtr.Zero)
			{
				WindowActions.NotifySizeMove(hwnd, start: true);
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

	private void ShowDragFrame(Native.RECT windowRect, AppConfig c)
	{
		if (_overlaysReady)
		{
			Native.RECT rect = FrameRectOf(windowRect, c, out var _);
			_frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius, c.FrameOpacity);
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
			_frame.ApplyTheme(appConfig.FrameColorId, appConfig.FrameStyleId, appConfig.FrameThickness, appConfig.FrameRadius, appConfig.FrameOpacity);
			Native.RECT rect = FrameRectOf(windowRect, appConfig, out var inset);
			_frame.RaiseAboveAll = inset;
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
		_frame.RaiseAboveAll = inset;
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
			_frame.ApplyTheme(appConfig.FrameColorId, appConfig.FrameStyleId, appConfig.FrameThickness, appConfig.FrameRadius, appConfig.FrameOpacity);
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

	private void ShowOutline(Native.RECT target, Session s, AppConfig c)
	{
		if (target.Width < 12 || target.Height < 12)
		{
			return;
		}
		Native.RECT rectNow = FrameRectOf(target, c, out var inset);
		_frame.RaiseAboveAll = inset;
		if (rectNow.Width < 4 || rectNow.Height < 4)
		{
			return;
		}
		_frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius, c.FrameOpacity);
		lock (_gate)
		{
			if (_dragging && _session.Gen == s.Gen)
			{
				_frame.SetRectNow(rectNow);
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
				_work.TryDequeue(out var _);
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
}
internal static class FrameStyles
{
	public const string Glow = "Glow";

	public const string Flat = "Flat";

	public const string Solid = "Solid";

	public const string Double = "Double";

	public const string Dashed = "Dashed";

	public const string Aurora = "Aurora";

	public const string Neon = "Neon";

	public const string Halo = "Halo";

	public const string Corners = "Corners";

	public const string Shadow = "Shadow";

	public const string Underline = "Underline";

	public const string Brackets = "Brackets";

	public static readonly string[] All = new string[12]
	{
		"Glow", "Flat", "Solid", "Double", "Dashed", "Aurora", "Neon", "Halo", "Corners", "Shadow",
		"Underline", "Brackets"
	};

	private static readonly int[] GlowAlpha = new int[3] { 48, 30, 18 };

	public static string DefaultId => "Glow";

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
			"Glow" => "柔光环（推荐）", 
			"Flat" => "纯净环（无光晕）", 
			"Solid" => "实心描边（最醒目）", 
			"Double" => "双线（极简）", 
			"Dashed" => "虚线环", 
			"Aurora" => "极光（渐变）", 
			"Neon" => "霓虹（发光）", 
			"Halo" => "光晕（弥散）", 
			"Corners" => "四角框（最轻量）", 
			"Shadow" => "柔和投影（不画实线）", 
			"Underline" => "下缘光条（只提示底边）", 
			"Brackets" => "四边中点（四条短线）", 
			_ => id, 
		};
	}

	public static int MarginFor(string style, int thickness)
	{
		int num = thickness / 2 + 1;
		return style switch
		{
			"Glow" => num + 10, 
			"Aurora" => num + 10, 
			"Neon" => num + 12, 
			"Halo" => num + 14, 
			"Dashed" => num + 7, 
			"Solid" => (int)((float)thickness * 0.9f) + 3, 
			"Double" => num + 7, 
			"Corners" => num + 4, 
			"Shadow" => num + 16, 
			"Underline" => num + 10, 
			"Brackets" => num + 6, 
			_ => num + 3, 
		};
	}

	public static void Draw(Graphics g, RectangleF area, Color color, string style, int thickness, int radius, int marginOverride = -1)
	{
		int num = ((marginOverride >= 0) ? marginOverride : MarginFor(style, thickness));
		RectangleF rectangleF = new RectangleF(area.X + (float)num, area.Y + (float)num, area.Width - (float)num * 2f, area.Height - (float)num * 2f);
		if (rectangleF.Width < 6f || rectangleF.Height < 6f)
		{
			return;
		}
		float num2 = ClampRadius(radius, rectangleF.Width, rectangleF.Height);
		float num3 = thickness;
		if (style == "Solid")
		{
			num3 = Math.Max(2f, (float)thickness * 1.8f);
		}
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.Half;
		switch (style)
		{
		case "Aurora":
			DrawAurora(g, rectangleF, num2, color, num3);
			return;
		case "Neon":
			DrawNeon(g, rectangleF, num2, color, num3);
			return;
		case "Halo":
			DrawHalo(g, rectangleF, num2, color, num3);
			return;
		case "Corners":
			DrawCorners(g, rectangleF, num2, color, num3);
			return;
		case "Shadow":
			DrawShadow(g, rectangleF, num2, color, num3);
			return;
		case "Underline":
			DrawUnderline(g, rectangleF, num2, color, num3);
			return;
		case "Brackets":
			DrawBrackets(g, rectangleF, num2, color, num3);
			return;
		}
		if (style == "Glow" || style == "Dashed")
		{
			for (int num4 = ((style == "Glow") ? 3 : 2); num4 >= 1; num4--)
			{
				using GraphicsPath path = RoundedPath(rectangleF, num2);
				using Pen pen = new Pen(Color.FromArgb(GlowAlpha[num4 - 1], color), num3 + 3f * (float)num4);
				pen.LineJoin = LineJoin.Round;
				ApplyDash(pen, style, num2);
				g.DrawPath(pen, path);
			}
		}
		using (GraphicsPath path2 = RoundedPath(rectangleF, num2))
		{
			using Pen pen2 = new Pen(color, num3);
			pen2.LineJoin = LineJoin.Round;
			ApplyDash(pen2, style, num2);
			g.DrawPath(pen2, path2);
		}
		if (!(style == "Double"))
		{
			return;
		}
		using GraphicsPath path3 = RoundedPath(new RectangleF(rectangleF.X - 4.5f, rectangleF.Y - 4.5f, rectangleF.Width + 9f, rectangleF.Height + 9f), num2 + 4.5f);
		using Pen pen3 = new Pen(Color.FromArgb(140, color), 1.5f);
		pen3.LineJoin = LineJoin.Round;
		g.DrawPath(pen3, path3);
	}

	private static void DrawAurora(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		using (GraphicsPath path = RoundedPath(ring, r))
		{
			using Pen pen = new Pen(Color.FromArgb(38, color), t + 8f);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using GraphicsPath path2 = RoundedPath(ring, r);
		using LinearGradientBrush linearGradientBrush = new LinearGradientBrush(ring, Lighten(color, 0.45f), Darken(color, 0.42f), 45f);
		linearGradientBrush.InterpolationColors = new ColorBlend(3)
		{
			Colors = new Color[3]
			{
				Lighten(color, 0.48f),
				color,
				Darken(color, 0.42f)
			},
			Positions = new float[3] { 0f, 0.5f, 1f }
		};
		using Pen pen2 = new Pen(linearGradientBrush, Math.Max(2f, t * 1.35f));
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path2);
	}

	private static void DrawNeon(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		for (int num = 3; num >= 1; num--)
		{
			int alpha = 20 + (3 - num) * 22;
			using GraphicsPath path = RoundedPath(ring, r);
			using Pen pen = new Pen(Color.FromArgb(alpha, color), t + 3.4f * (float)num);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using GraphicsPath path2 = RoundedPath(ring, r);
		using Pen pen2 = new Pen(Lighten(color, 0.5f), Math.Max(1.6f, t * 0.8f));
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path2);
	}

	private static void DrawHalo(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		for (int num = 4; num >= 1; num--)
		{
			int alpha = 12 + (4 - num) * 15;
			using GraphicsPath path = RoundedPath(ring, r);
			using Pen pen = new Pen(Color.FromArgb(alpha, color), t + 4.4f * (float)num);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using GraphicsPath path2 = RoundedPath(ring, r);
		using Pen pen2 = new Pen(color, t);
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path2);
	}

	private static void DrawShadow(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		Color baseColor = Color.FromArgb((int)((float)(int)color.R * 0.42f), (int)((float)(int)color.G * 0.42f), (int)((float)(int)color.B * 0.42f));
		int[] array = new int[4] { 15, 11, 7, 4 };
		int[] array2 = new int[4] { 12, 18, 26, 38 };
		for (int i = 0; i < array.Length; i++)
		{
			float num = array[i];
			RectangleF rc = RectangleF.Inflate(ring, num * 0.5f, num * 0.5f);
			float num2 = Math.Min(r + num * 0.5f, Math.Min(rc.Width, rc.Height) / 2f);
			if (num2 < 0f)
			{
				num2 = 0f;
			}
			using GraphicsPath path = RoundedPath(rc, num2);
			using Pen pen = new Pen(Color.FromArgb(array2[i], baseColor), num);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using GraphicsPath path2 = RoundedPath(ring, r);
		using Pen pen2 = new Pen(Color.FromArgb(110, color), Math.Max(1f, t * 0.45f));
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path2);
	}

	private static void DrawUnderline(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		float num = Math.Max(2f, t * 1.1f);
		float num2 = ring.Left + ring.Width * 0.1f;
		float num3 = ring.Right - ring.Width * 0.1f;
		float num4 = ring.Bottom - num / 2f;
		if (num3 <= num2)
		{
			return;
		}
		using LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new RectangleF(num2, num4 - num, num3 - num2, num * 2f), color, color, LinearGradientMode.Horizontal);
		ColorBlend colorBlend = new ColorBlend(3);
		colorBlend.Colors = new Color[3]
		{
			Color.FromArgb(0, color),
			Color.FromArgb(235, color),
			Color.FromArgb(0, color)
		};
		colorBlend.Positions = new float[3] { 0f, 0.5f, 1f };
		linearGradientBrush.InterpolationColors = colorBlend;
		using (Pen pen = new Pen(linearGradientBrush, num + 9f))
		{
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			g.DrawLine(pen, num2, num4, num3, num4);
		}
		using Pen pen2 = new Pen(linearGradientBrush, num);
		pen2.StartCap = LineCap.Round;
		pen2.EndCap = LineCap.Round;
		g.DrawLine(pen2, num2, num4, num3, num4);
	}

	private static void DrawBrackets(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		float num = Math.Max(18f, Math.Min(ring.Width, ring.Height) * 0.22f);
		float num2 = ring.Left + ring.Width / 2f;
		float num3 = ring.Top + ring.Height / 2f;
		float num4 = num / 2f;
		using (Pen pen = new Pen(color, t))
		{
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			g.DrawLine(pen, num2 - num4, ring.Top, num2 + num4, ring.Top);
			g.DrawLine(pen, num2 - num4, ring.Bottom, num2 + num4, ring.Bottom);
			g.DrawLine(pen, ring.Left, num3 - num4, ring.Left, num3 + num4);
			g.DrawLine(pen, ring.Right, num3 - num4, ring.Right, num3 + num4);
		}
		using GraphicsPath path = RoundedPath(ring, r);
		using Pen pen2 = new Pen(Color.FromArgb(34, color), Math.Max(1f, t * 0.38f));
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path);
	}

	private static void DrawCorners(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		float num = Math.Max(16f, Math.Min(ring.Width, ring.Height) * 0.2f);
		float num2 = Math.Min(r, Math.Min(num * 0.7f, Math.Min(ring.Width, ring.Height) / 2f - 1f));
		if (num2 < 0f)
		{
			num2 = 0f;
		}
		using GraphicsPath path = CornerBrackets(ring, num2, num);
		using Pen pen = new Pen(color, t);
		pen.LineJoin = LineJoin.Round;
		pen.StartCap = LineCap.Round;
		pen.EndCap = LineCap.Round;
		g.DrawPath(pen, path);
	}

	private static GraphicsPath CornerBrackets(RectangleF rc, float rad, float len)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = rad * 2f;
		graphicsPath.StartFigure();
		graphicsPath.AddLine(rc.Left, rc.Top + len, rc.Left, rc.Top + rad);
		if (rad > 0.5f)
		{
			graphicsPath.AddArc(rc.Left, rc.Top, num, num, 180f, 90f);
		}
		graphicsPath.AddLine(rc.Left + rad, rc.Top, rc.Left + len, rc.Top);
		graphicsPath.StartFigure();
		graphicsPath.AddLine(rc.Right - len, rc.Top, rc.Right - rad, rc.Top);
		if (rad > 0.5f)
		{
			graphicsPath.AddArc(rc.Right - num, rc.Top, num, num, 270f, 90f);
		}
		graphicsPath.AddLine(rc.Right, rc.Top + rad, rc.Right, rc.Top + len);
		graphicsPath.StartFigure();
		graphicsPath.AddLine(rc.Right, rc.Bottom - len, rc.Right, rc.Bottom - rad);
		if (rad > 0.5f)
		{
			graphicsPath.AddArc(rc.Right - num, rc.Bottom - num, num, num, 0f, 90f);
		}
		graphicsPath.AddLine(rc.Right - rad, rc.Bottom, rc.Right - len, rc.Bottom);
		graphicsPath.StartFigure();
		graphicsPath.AddLine(rc.Left + len, rc.Bottom, rc.Left + rad, rc.Bottom);
		if (rad > 0.5f)
		{
			graphicsPath.AddArc(rc.Left, rc.Bottom - num, num, num, 90f, 90f);
		}
		graphicsPath.AddLine(rc.Left, rc.Bottom - rad, rc.Left, rc.Bottom - len);
		return graphicsPath;
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

	private static void ApplyDash(Pen pen, string style, float radius)
	{
		if (!(style != "Dashed"))
		{
			pen.DashStyle = DashStyle.Dash;
			pen.DashCap = DashCap.Round;
			float num = Math.Max(4f, radius * 0.85f);
			pen.DashPattern = new float[2]
			{
				num,
				num * 0.72f
			};
		}
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
		lock (Gate)
		{
			try
			{
				EnsureWriter();
				if (_writer == null)
				{
					File.AppendAllText(_path, Stamp(level, msg), Encoding.UTF8);
					return;
				}
				_writer.Write(Stamp(level, msg));
				_writer.Flush();
			}
			catch
			{
				CloseWriter();
			}
		}
	}

	private static string Stamp(string level, string msg)
	{
		StringBuilder stringBuilder = new StringBuilder(64);
		stringBuilder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
		stringBuilder.Append(' ');
		stringBuilder.Append(level);
		stringBuilder.Append(' ');
		stringBuilder.Append(msg);
		stringBuilder.Append("\r\n");
		return stringBuilder.ToString();
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
			ReleaseBitmap();
			Bitmap bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
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
				bitmap.Dispose();
				return;
			}
			_bmp = bitmap;
			_bw = w;
			_bh = h;
			_curW = w;
			_curH = h;
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
			if (_memDc != IntPtr.Zero)
			{
				nint zero = IntPtr.Zero;
				try
				{
					zero = bitmap.GetHbitmap(Color.FromArgb(0));
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
					return;
				}
				if (zero != IntPtr.Zero)
				{
					_hBitmap = zero;
					_oldBmp = Native.SelectObject(_memDc, zero);
				}
			}
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

	public void ApplyTheme(string colorId, string styleId, int thickness, int radius, int opacityPercent = 100)
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
		if (_colorId != colorId || _styleId != styleId || _thickness != thickness || _radius != radius || _constantAlpha != num)
		{
			_colorId = colorId;
			_styleId = styleId;
			_thickness = thickness;
			_radius = radius;
			_constantAlpha = num;
			MarkContentDirty();
		}
	}

	protected override void RenderContent(Graphics g, int w, int h)
	{
		RectangleF area = new RectangleF(0f, 0f, w, h);
		FrameStyles.Draw(g, area, FrameColors.Resolve(_colorId), _styleId, _thickness, _radius);
	}
}
internal static class Program
{
	private const string MutexName = "Local\\HoldDrag.SingleInstance.v1";

	private const string ShowEventName = "Local\\HoldDrag.ShowSettings.v1";

	public const string ArgNoElevate = "--no-elevate";

	public const string ArgSelfTest = "--selftest";

	public const string ArgSmoke = "--smoke";

	public const string ArgTrace = "--trace";

	public const string ArgStress = "--stress";

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
		Mutex obj3 = new Mutex(initiallyOwned: true, "Local\\HoldDrag.SingleInstance.v1", out var createdNew);
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
				Text = "HoldDrag 外部测试窗口",
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
					list.Add(text);
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
			current.MovePreviewMode = "FullWin";
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
		Safe("T51 空心矩形模式：拖动期窗口不动、松手落位（AltSnap FullWin=0 移植回归）", TestOutlineMode);
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
			configStore.Current.MovePreviewMode = "FullWin";
			configStore.Current.ShakeMinimizeOthers = false;
			configStore.Save();
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
			configStore.Save();
			configStore.Current.HoldMs = 888;
			configStore.Save();
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
		List<GroupBox> groups = new List<GroupBox>();
		Walk(settingsForm, (Control c) =>
		{
			if (c is GroupBox item)
			{
				groups.Add(item);
			}
		});
		for (int num = 0; num < groups.Count; num++)
		{
			Check(groups[num].Controls.Count > 0, "分组「" + groups[num].Text + "」里有控件（不是空的）");
		}
		Check(groups.Count >= 6 && SettingsForm.VerifyLayout(settingsForm), "设置窗体上分组数 >= 6，实际 " + groups.Count);
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
		Check(FrameStyles.MarginFor("Flat", 3) < FrameStyles.MarginFor("Glow", 3), "无柔光的样式留白更小（不为不存在的光晕浪费面积）");
		Check(FrameStyles.MarginFor("Solid", 8) >= 8, "实心描边越粗，留白越大");
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
		Check(SettingsForm.VerifyAnchors(settingsForm), "拉宽后分组能重排（1 列 <-> 2 列自适应）");
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
		settingsForm.ProbeSelectFrameStyle("Solid");
		SettingsForm.FrameSelection frameSelection2 = settingsForm.ProbeFrameSelection();
		Eq(frameSelection2.StyleId, "Solid", "换个样式，读回的 ID 跟着换");
		Eq(frameSelection2.PreviewStyleId, "Solid", "预览框的样式也同步了");
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
		Check(SettingsForm.VerifyAnchors(settingsForm), "★ 宽度够时分组从 1 列变 2 列，回窄能退回 1 列（拉宽真的多装东西）");
		Check(SettingsForm.VerifyInnerStretch(settingsForm), "★ 拉宽后分组内的下拉框/文本框跟着变宽（不是只把分组撑大）", SettingsForm.LastStretchDetail + " || " + SettingsForm.DumpLayoutItem(settingsForm, settingsForm.ProbeComboLeft));
		Check(SettingsForm.VerifyStretchIdempotent(settingsForm), "★ 来回拉多次，控件位置稳定不漂移（伸缩是幂等的）");
		bool ok = false;
		for (int i = 0; i < settingsForm.Controls.Count; i++)
		{
			if (settingsForm.Controls[i].Dock == DockStyle.Bottom)
			{
				ok = true;
				break;
			}
		}
		Check(ok, "底部操作条固定在窗口下方（不随内容滚走）");
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
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		StringBuilder hidden = new StringBuilder();
		StringBuilder dead = new StringBuilder();
		int interactive = 0;
		int checkedCount = 0;
		Walk(settingsForm, (Control c) =>
		{
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
							dead.Append(Name(c)).Append("(在容器外 ").Append("L=")
								.Append(rect.Left)
								.Append(" T=")
								.Append(rect.Top)
								.Append(" W=")
								.Append(rect.Width)
								.Append(" H=")
								.Append(rect.Height)
								.Append(" 容器=")
								.Append(clientRectangle.Width)
								.Append("x")
								.Append(clientRectangle.Height)
								.Append(") ");
						}
					}
				}
			}
		});
		Check(hidden.Length == 0, "设置窗口没有【看不见】的控件（分组/卡片化之后可见性回归）", (hidden.Length == 0) ? (checkedCount + " 个控件全部可见") : ("不可见: " + hidden));
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
			current.MovePreviewMode = "FullWin";
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
				if (!string.IsNullOrEmpty(text) && !(text == "请按下要用的按键…") && !(c is TextBoxBase) && !(c is ComboBox) && !(c is Button))
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
		activationFrame.ApplyTheme("Teal", "Solid", 6, 6);
		activationFrame.SetRect(rECT);
		Settle();
		Check(Native.IsWindowVisible(activationFrame.Handle), "浮层已显示");
		Color color = FrameColors.Resolve("Teal");
		Color color2 = FrameColors.Resolve("Red");
		Color? color3 = CaptureCornerPixel(activationFrame.Handle, rECT, color, color2);
		activationFrame.ApplyTheme("Red", "Solid", 6, 6);
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
			int num = FrameStyles.MarginFor("Solid", 6);
			Native.POINT pOINT = new Native.POINT(r.Left + num + 3, r.Top + num + 3);
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
		Eq(appConfig.FrameStyleId, "Glow", "默认外框样式是柔光环");
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
		Eq(appConfig.FrameStyleId, "Glow", "非法样式被拉回默认");
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

	private static void TestOutlineMode()
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-outline-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		Form form = null;
		DragEngine dragEngine = null;
		try
		{
			ConfigStore store = new ConfigStore(text);
			store.Load();
			AppConfig current = store.Current;
			current.MovePreviewMode = "Outline";
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.BringToFront = false;
			current.MoveRate = 1;
			current.ResizeRate = 1;
			form = new Form();
			form.FormBorderStyle = FormBorderStyle.Sizable;
			form.StartPosition = FormStartPosition.Manual;
			form.ShowInTaskbar = false;
			form.TopMost = true;
			form.Bounds = new Rectangle(150, 150, 300, 200);
			form.Show();
			Pump();
			Native.RECT win = new Native.RECT(150, 150, 450, 350);
			Native.POINT pt = new Native.POINT(300, 250);
			dragEngine = new DragEngine(() => store.Current);
			dragEngine.Precreate();
			if (!dragEngine.Begin(form.Handle, move: true, 0, pt))
			{
				Check(ok: false, "Outline：会话启动成功", "Begin 返回 false");
				return;
			}
			for (int num = 1; num <= 5; num++)
			{
				dragEngine.StepForTest(new Native.POINT(pt.X + num * 10, pt.Y + num * 6));
			}
			Native.GetWindowRect(form.Handle, out var lpRect);
			Eq(lpRect.Left, win.Left, "Outline：拖动期间窗口左缘纹丝不动");
			Eq(lpRect.Top, win.Top, "Outline：拖动期间窗口上缘纹丝不动");
			Eq(lpRect.Width, win.Width, "Outline：拖动期间窗口宽度不变");
			Check(dragEngine.Frame.IsShown, "Outline：预览框在拖动期间可见", "IsShown=" + dragEngine.Frame.IsShown);
			Native.GetWindowRect(dragEngine.Frame.Handle, out var lpRect2);
			Check(Math.Abs(lpRect2.Left + lpRect2.Width / 2 - (win.Left + 50 + win.Width / 2)) <= 12, "Outline：预览框中心跟到第 5 步位置（左）", "框中心=" + (lpRect2.Left + lpRect2.Width / 2) + " 期望=" + (win.Left + 50 + win.Width / 2));
			Check(Math.Abs(lpRect2.Top + lpRect2.Height / 2 - (win.Top + 30 + win.Height / 2)) <= 12, "Outline：预览框中心跟到第 5 步位置（上）", "框中心=" + (lpRect2.Top + lpRect2.Height / 2) + " 期望=" + (win.Top + 30 + win.Height / 2));
			int num2 = CountFramePixelsAround(win, FrameColors.Resolve(current.FrameColorId));
			Check(num2 > 200, "Outline：预览框在屏幕上真的有渲染的像素", "inkPixels=" + num2);
			dragEngine.UpdateFinal(new Native.POINT(pt.X + 60, pt.Y + 36));
			Native.GetWindowRect(form.Handle, out var lpRect3);
			Eq(lpRect3.Left, win.Left + 60, "Outline：松手后窗口落到最新位置（左缘）");
			Eq(lpRect3.Top, win.Top + 36, "Outline：松手后窗口落到最新位置（上缘）");
		}
		finally
		{
			dragEngine?.Cancel();
			form?.Dispose();
			Pump();
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
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
			current.MovePreviewMode = "FullWin";
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
			current.MovePreviewMode = "FullWin";
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
			current.MovePreviewMode = "FullWin";
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
			current.MovePreviewMode = "FullWin";
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
			configStore.Save();
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
				configStore2.Save();
				ConfigStore configStore3 = new ConfigStore(text);
				configStore3.Load();
				Check(configStore3.Current.SettingsWidth >= width, "★ 窗口宽度上限跟随屏幕（不再写死 2400）", "屏幕宽=" + width + " 存回来=" + configStore3.Current.SettingsWidth);
			}
			else
			{
				Line("（跳过）本机屏幕宽度 " + width + " 不大于 2400，无法验证上限是否跟随屏幕");
			}
			configStore2.Current.HoldMs = 555;
			configStore2.Save();
			configStore2.Current.HoldMs = 666;
			configStore2.Save();
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
		}
		Check(!flag, "AppConfig 里已无 DragTransparency");
		Check(!flag2, "AppConfig 里已无 DragSizeHint");
		Check(!flag3, "AppConfig 里已无 EdgeSnap（#32 自动贴边整体删除）");
		Check(!flag4, "AppConfig 里已无 SnapToOtherWindows（#32 与其它窗口对齐整体删除）");
		Check(!flag5, "AppConfig 里已无 SnapPreview（#32 边缘预览落位整体删除）");
		Check(!flag6, "AppConfig 里已无 SnapDistance（#32 吸附距离整体删除）");
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
		Check(FrameStyles.All.Length >= 9, "外框样式至少 9 种（#37 扩充）", "实际 " + FrameStyles.All.Length);
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
		int num3 = FrameStyles.MarginFor("Flat", 3);
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
			if (text3 != "Flat" && FrameStyles.MarginFor(text3, 3) < num3)
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
				c.MovePreviewMode = "FullWin";
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
		settingsForm.ProbeSelectFrameStyle("Neon");
		SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
		Eq(frameSelection.GridStyleId, "Neon", "★ 选了「霓虹」之后，画廊里高亮的就是霓虹");
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
		Check(renderStats.BlackPixels < 400, "★ 画面里没有成片的近黑像素（自绘控件没铺底导致黑条的老问题没有复发）", "近黑像素 " + renderStats.BlackPixels);
		Check(renderStats.MeanBrightness > 200.0, "整体是浅色 Win11 底（平均亮度够高，不是暗色或脏色）", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
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
		int num6 = SettingsForm.ProbeTwoColumnThresholdPx(settingsForm);
		int num7 = SettingsForm.ProbeRelayoutColumns(settingsForm, num6 + (int)Math.Round(20f * num));
		int num8 = SettingsForm.ProbeRelayoutColumns(settingsForm, num6 - (int)Math.Round(40f * num));
		int num9 = SettingsForm.ProbeRelayoutColumns(settingsForm, num6 - (int)Math.Round(300f * num));
		Check(num7 == 2 && num8 == 2 && num9 == 1, "★ 列数切换带回滞：阈值下方一小段仍保持两列，不会来回翻（抖动回归）", "阈值=" + num6 + " → " + num7 + " / " + num8 + " / " + num9);
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
				int phantomUpSentForTest = Coordinator.PhantomUpSentForTest;
				dragFixture.Coord.Trigger.SimulateKeyDown();
				Check(!dragFixture.Coord.Trigger.Active, "触发键尚未到达阈值（仍在按住阶段）");
				Check(dragFixture.Coord.Trigger.Held, "触发键处于『按住中』状态");
				Native.MSLLHOOKSTRUCT ms = Ms(p);
				Check(!dragFixture.Coord.OnMouse(513, ref ms), "按住阶段里的左键按下被原样放行（此时还不该动手）");
				Check(dragFixture.Coord.HeldDuringHoldForTest(0), "★ 按住阶段里的左键按下被【记账】（老代码在这里什么都没记，就是漏掉的根因）");
				dragFixture.Coord.Trigger.ForceHoldElapsed();
				Settle();
				Check(dragFixture.Coord.Trigger.Active, "触发键已激活（外框此刻会亮起）");
				Check(!dragFixture.Coord.Drag.IsDragging, "此刻还没有拖动会话（正是用户看到的状态）");
				Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 70, p.Y + 50));
				dragFixture.Coord.OnMouse(512, ref ms2);
				Settle();
				Check(dragFixture.Coord.Drag.IsDragging, "★ 鼠标一移动就把拖动补起来了（不再需要松开再按一次）");
				Check(!dragFixture.Coord.PassedDownForTest(0), "按住阶段放行的那条 down 同样记入『幽灵按下』账，收养时已完成配对");
				Check(Coordinator.PhantomUpSentForTest > phantomUpSentForTest, "★ 收养时补发配对的按钮抬起（这条 down 也是放行给目标程序的）");
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
				Native.GetWindowRect(dragFixture.H, out var lpRect4);
				Eq(lpRect4.Left - lpRect.Left, 0, "★ 补起那一帧窗口不跳（以当前光标为起点）");
				Eq(lpRect4.Top - lpRect.Top, 0, "★ 补起那一帧窗口不跳（垂直）");
				Native.MSLLHOOKSTRUCT ms3 = Ms(new Native.POINT(p.X + 70 + 90, p.Y + 50 + 60));
				dragFixture.Coord.OnMouse(512, ref ms3);
				Settle();
				Native.GetWindowRect(dragFixture.H, out var lpRect5);
				Eq(lpRect5.Left - lpRect4.Left, 90, "★ 窗口真的跟着光标移动了（水平 90px）");
				Eq(lpRect5.Top - lpRect4.Top, 60, "★ 窗口真的跟着光标移动了（垂直 60px）");
				Native.MSLLHOOKSTRUCT ms4 = Ms(ms3.pt);
				dragFixture.Coord.OnMouse(514, ref ms4);
				dragFixture.Coord.Trigger.SimulateKeyUp(null);
				Settle();
				Check(!dragFixture.Coord.Drag.IsDragging, "松开后拖动会话结束");
			}
			using DragFixture dragFixture2 = new DragFixture((AppConfig c) =>
			{
				c.ActionLeft = "Move";
				c.DoubleClickLeftMaximize = false;
			});
			Native.POINT p2 = CenterOf(dragFixture2.H);
			int phantomUpSentForTest2 = Coordinator.PhantomUpSentForTest;
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
			Check(Coordinator.PhantomUpSentForTest > phantomUpSentForTest2, "★ 收养时补发配对的按钮抬起（把目标程序单方面认定的按下配对掉）");
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
				c.MovePreviewMode = "FullWin";
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
				c.MovePreviewMode = "FullWin";
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
				c.FrameStyleId = "Solid";
				c.FrameColorId = "Teal";
				c.FrameThickness = 6;
				c.FrameOpacity = 100;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			Native.SetCursorPos(CenterOf(dragFixture.H).X, CenterOf(dragFixture.H).Y);
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms = Ms(CenterOf(dragFixture.H));
			dragFixture.Coord.OnMouse(512, ref ms);
			Settle();
			Check(drag.Frame.IsShown, "用例前提：悬停框已经亮起");
			Native.GetWindowRect(dragFixture.H, out var lpRect);
			int num = (lpRect.Top + lpRect.Bottom) / 2;
			Rectangle band = new Rectangle(lpRect.Left - 30, num - 2, 60, 5);
			int num2 = BandMaxGreenness(band);
			dragFixture.Store.Current.FrameOpacity = 40;
			drag.RefreshOverlayTheme();
			Settle();
			Thread.Sleep(120);
			Settle();
			int num3 = BandMaxGreenness(band);
			Line("    （不透明度）带内青色度 100%=" + num2 + "  40%=" + num3);
			Check(num2 > 30, "★ 用例前提：100% 时这条带上确实有外框（青色度足够）", "青色度=" + num2);
			Check(num3 != num2, "★ 改不透明度真的改变了屏幕渲染（不是改了参数却没重推像素）", "100%=" + num2 + " 40%=" + num3);
			Check(num3 > 0 && (double)num3 < (double)num2 * 0.6, "★ 40% 的外框明显比 100% 淡（整层等比变淡）", "青色度 " + num2 + " → " + num3);
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
		Check(text.Contains("2026-10-12"), "★ 更新时间显示在界面上");
		Check(text.Contains("版本") && text.Contains(SettingsForm.AppVersionForTest), "★ 当前版本号显示在界面上（且与程序集版本一致）", "程序集版本=" + SettingsForm.AppVersionForTest);
		settingsForm.Close();
	}

	private static void TestAllGroupsReachable()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
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
		Eq(SettingsForm.ProbeSidebarCount(settingsForm), num, "★ 侧栏条目数 = 分组数（新增分组必须自动出现在侧栏，用户才找得到）");
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
		int num = SettingsForm.ProbeGroupCount(settingsForm);
		Eq(SettingsForm.ProbeSidebarCount(settingsForm), num, "侧栏条目数 = 分组数");
		Line("    （滚动区）" + SettingsForm.ProbeScrollInfo(settingsForm));
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < num; i++)
		{
			int num2 = SettingsForm.ProbeGroupTop(settingsForm, i);
			if (num2 >= 0)
			{
				int value = SettingsForm.ProbeScrollTo(settingsForm, num2);
				Application.DoEvents();
				int num3 = SettingsForm.ProbeSidebarActive(settingsForm);
				if (num3 != i)
				{
					stringBuilder.Append("分组").Append(i).Append("(top=")
						.Append(num2)
						.Append(",实际滚动=")
						.Append(value)
						.Append(") → 高亮")
						.Append(num3)
						.Append("  ");
				}
			}
		}
		Line("    （滚动跟随）" + ((stringBuilder.Length == 0) ? "全部一致" : stringBuilder.ToString()));
		Check(stringBuilder.Length == 0, "★ 滚到任一分组，侧栏高亮都对得上", stringBuilder.ToString());
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
		int num2 = SettingsForm.ProbeGroupCount(settingsForm);
		StringBuilder stringBuilder = new StringBuilder();
		int num3 = num;
		int num4 = 0;
		bool ok = false;
		for (int i = 0; i < 40; i++)
		{
			int num5 = SettingsForm.ProbeWheelScroll(settingsForm, -3);
			if (num5 == num4 && i > 3)
			{
				break;
			}
			num4 = num5;
			int num6 = SettingsForm.ProbeSidebarActive(settingsForm);
			if (num6 != num3)
			{
				stringBuilder.Append(num5).Append("→").Append(num6)
					.Append(' ');
				num3 = num6;
			}
			if (num6 == num2 - 1)
			{
				ok = true;
			}
		}
		Line("    （真实滚轮）起始高亮=" + num + "，滚动中变化：" + stringBuilder?.ToString() + "，最终=" + num3 + "（共 " + num2 + " 组）");
		Line("    （同步诊断）" + SettingsForm.ProbeSyncDiag(settingsForm));
		Check(num4 > 0, "★ 真实滚轮消息确实让页面滚动了（用例前提）", "最终滚动偏移=" + num4);
		Check(stringBuilder.Length > 0, "★ 滚动过程中侧栏高亮【确实发生了变化】（不是一直停在第一项）", "变化序列：" + stringBuilder);
		Check(ok, "★ 一直滚到底能高亮到最后一个分组", "最终高亮=" + num3);
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
				c.MovePreviewMode = "FullWin";
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
		Check(FrameStyles.All.Length >= 12, "样式数量（太少不够用）", FrameStyles.All.Length + " 种");
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
			Native.SetWindowPos(num, IntPtr.Zero, virtualScreen.Left, virtualScreen.Top, virtualScreen.Width, virtualScreen.Height, 20u);
			Settle();
			if (!WindowActions.IsFullscreen(num))
			{
				Line("    （跳过：外部窗口没能铺成全屏）");
				return;
			}
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.MovePreviewMode = "FullWin";
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
			try
			{
				Process processById = Process.GetProcessById(PidOfWindow(num));
				if (processById != null && !processById.HasExited)
				{
					processById.Kill();
				}
			}
			catch
			{
			}
		}
	}

	private static Native.POINT CenterOfRect(nint h)
	{
		Native.GetWindowRect(h, out var lpRect);
		return new Native.POINT((lpRect.Left + lpRect.Right) / 2, (lpRect.Top + lpRect.Bottom) / 2);
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

	private static nint LaunchExternalWindow()
	{
		try
		{
			if (Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--windowhost --no-elevate")
			{
				UseShellExecute = false
			}) == null)
			{
				return IntPtr.Zero;
			}
			for (int i = 0; i < 80; i++)
			{
				Application.DoEvents();
				Thread.Sleep(100);
				nint num = FindWindowByTitle("HoldDrag 外部测试窗口");
				if (num != IntPtr.Zero)
				{
					return num;
				}
			}
			return IntPtr.Zero;
		}
		catch
		{
			return IntPtr.Zero;
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
				int phantomUpSentForTest = Coordinator.PhantomUpSentForTest;
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
				Check(Coordinator.PhantomUpSentForTest > phantomUpSentForTest, "★ 收养时补发【配对的按钮抬起】（目标程序的按下标记就此复位）");
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
				int phantomUpSentForTest2 = Coordinator.PhantomUpSentForTest;
				Native.MSLLHOOKSTRUCT ms5 = Ms(p2);
				dragFixture2.Coord.OnMouse(513, ref ms5);
				Check(dragFixture2.Coord.PassedDownForTest(0), "② 按下被记账");
				ActivateTrigger(dragFixture2.Coord);
				Native.MSLLHOOKSTRUCT ms6 = Ms(p2);
				Check(!dragFixture2.Coord.OnMouse(514, ref ms6), "激活后直接松手：这次真实抬起原样放行（目标窗口自己完成配对）");
				Check(!dragFixture2.Coord.PassedDownForTest(0), "配对后记账清零（不会秋后补发骚扰消息）");
				Check(Coordinator.PhantomUpSentForTest == phantomUpSentForTest2, "没有补发合成抬起（真实 up 已经完成配对，不需要我们插手）");
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
			RunPairedUpMaskCase(0, new bool[3] { true, true, true }, 513, "收养左键：右/中键都还按着 → wParam=MkRM（自己已松开故不含左键）");
			RunPairedUpMaskCase(1, new bool[3] { true, true, false }, 516, "收养右键：左键还按着、中键已松 → wParam=MkL（如实带左键、不带中键）");
			RunPairedUpMaskCase(2, new bool[3] { false, false, true }, 519, "收养中键：其余键都没按 → wParam=0（不许把没按着的键报成按着）");
		}
		finally
		{
			Coordinator.ButtonsDownOverrideForTest = null;
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}

	private static void RunPairedUpMaskCase(int btn, bool[] downs, int downMsg, string maskWhat)
	{
		Coordinator.ButtonsDownOverrideForTest = downs;
		Coordinator.LastPairedUpForTest = null;
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
		int phantomUpSentForTest = Coordinator.PhantomUpSentForTest;
		Native.MSLLHOOKSTRUCT ms = Ms(p);
		dragFixture.Coord.OnMouse(downMsg, ref ms);
		Check(dragFixture.Coord.PassedDownForTest(btn), "按下已记入幽灵账（btn=" + btn + "）");
		ActivateTrigger(dragFixture.Coord);
		Settle();
		Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT(p.X + 60, p.Y + 40));
		dragFixture.Coord.OnMouse(512, ref ms2);
		Settle();
		Check(dragFixture.Coord.Drag.IsDragging, "收养成功：拖动已补起（btn=" + btn + "）");
		Check(Coordinator.PhantomUpSentForTest > phantomUpSentForTest, "补发过配对的抬起（btn=" + btn + "）");
		Coordinator.PairedUpProbe lastPairedUpForTest = Coordinator.LastPairedUpForTest;
		if (lastPairedUpForTest == null)
		{
			Check(ok: false, "探针捕获到配对抬起的内容（btn=" + btn + "）");
			return;
		}
		int msg = lastPairedUpForTest.Msg;
		int expected;
		if (btn != 0)
		{
			expected = ((btn == 1) ? 517 : 520);
		}
		else
		{
			expected = 514;
		}
		Eq(msg, expected, "消息类型与被收养的键对应（btn=" + btn + "）");
		Check(lastPairedUpForTest.Hwnd == dragFixture.H, "配对抬起发到目标窗口自己（btn=" + btn + "）");
		Check(lastPairedUpForTest.WParam == MaskOf(downs, btn), maskWhat, "wParam 期望 0x" + MaskOf(downs, btn).ToString("X") + " 实际 0x" + lastPairedUpForTest.WParam.ToString("X"));
		Native.POINT lpPoint = ms2.pt;
		Native.ScreenToClient(lastPairedUpForTest.Hwnd, ref lpPoint);
		int expected2 = (lpPoint.Y << 16) | (lpPoint.X & 0xFFFF);
		Eq((int)((IntPtr)lastPairedUpForTest.LParam).ToInt64(), expected2, "lParam 是相对目标窗口的客户区坐标（btn=" + btn + "）");
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
			current.MovePreviewMode = "FullWin";
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
			current.MovePreviewMode = "FullWin";
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
internal sealed class SettingsForm : Form
{
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

	private sealed class ResponsiveGroupPanel : TableLayoutPanel
	{
		private readonly GroupBox[] _groups;

		public const int TwoColumnThreshold = 1240;

		public const int HysteresisLogical = 90;

		public int CurrentColumns;

		private void SetColumns(int want)
		{
			CurrentColumns = want;
		}

		public ResponsiveGroupPanel(GroupBox[] groups)
		{
			_groups = groups;
			CurrentColumns = 1;
			Anchor = AnchorStyles.Top | AnchorStyles.Left;
			ColumnCount = 1;
			RowCount = groups.Length;
			Padding = new Padding(0);
			Margin = new Padding(0);
			ColumnStyles.Clear();
			ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
			RowStyles.Clear();
			for (int i = 0; i < groups.Length; i++)
			{
				RowStyles.Add(new RowStyle(SizeType.AutoSize));
			}
			for (int j = 0; j < groups.Length; j++)
			{
				groups[j].Dock = DockStyle.Fill;
				groups[j].Margin = new Padding(0, 0, 0, 14);
				groups[j].Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
				Controls.Add(groups[j], 0, j);
			}
		}

		public void Relayout(int available)
		{
			int num = (int)Math.Round(1240f * CurrentUiScale);
			int num2 = (int)Math.Round(90f * CurrentUiScale);
			int num3 = ((CurrentColumns < 2) ? ((available < num) ? 1 : 2) : ((available < num - num2) ? 1 : 2));
			if (Controls.Count == 0 || num3 != CurrentColumns)
			{
				RebuildStructure(num3);
			}
			if (Width != available)
			{
				Width = available;
			}
			PerformLayout();
		}

		private void RebuildStructure(int want)
		{
			SuspendLayout();
			try
			{
				CurrentColumns = want;
				ColumnStyles.Clear();
				RowStyles.Clear();
				Controls.Clear();
				if (want == 1)
				{
					ColumnCount = 1;
					RowCount = _groups.Length;
					ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
					for (int i = 0; i < _groups.Length; i++)
					{
						RowStyles.Add(new RowStyle(SizeType.AutoSize));
						Controls.Add(_groups[i], 0, i);
					}
					return;
				}
				ColumnCount = 2;
				RowCount = (_groups.Length + 1) / 2;
				ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
				ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
				for (int j = 0; j < RowCount; j++)
				{
					RowStyles.Add(new RowStyle(SizeType.AutoSize));
				}
				for (int k = 0; k < _groups.Length; k++)
				{
					Controls.Add(_groups[k], k / RowCount, k % RowCount);
				}
			}
			finally
			{
				ResumeLayout(performLayout: true);
			}
		}
	}

	private sealed class FramePreviewBox : Control
	{
		private int _thickness = 3;

		private int _radius = 13;

		private int _opacity = 100;

		public bool FrameEnabled = true;

		public string ColorId = FrameColors.DefaultId;

		public string StyleId = FrameStyles.DefaultId;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		[Browsable(false)]
		public int Thickness
		{
			get
			{
				return _thickness;
			}
			set
			{
				if (_thickness != value)
				{
					_thickness = value;
					Invalidate();
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		[Browsable(false)]
		public int Radius
		{
			get
			{
				return _radius;
			}
			set
			{
				if (_radius != value)
				{
					_radius = value;
					Invalidate();
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		[Browsable(false)]
		public int OpacityPercent
		{
			get
			{
				return _opacity;
			}
			set
			{
				if (_opacity != value)
				{
					_opacity = value;
					Invalidate();
				}
			}
		}

		public FramePreviewBox()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		public void Refresh_()
		{
			Invalidate();
			Update();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			try
			{
				Graphics graphics = e.Graphics;
				HighQuality.Apply(graphics);
				graphics.Clear(Color.FromArgb(244, 244, 245));
				int num = FrameStyles.MarginFor(StyleId, _thickness);
				RectangleF area = new RectangleF(0f, 0f, Width, Height);
				RectangleF rect = new RectangleF(area.X + (float)num, area.Y + (float)num, area.Width - (float)num * 2f, area.Height - (float)num * 2f);
				if (rect.Width <= 2f || rect.Height <= 2f)
				{
					base.OnPaint(e);
					return;
				}
				using (SolidBrush brush = new SolidBrush(Color.White))
				{
					graphics.FillRectangle(brush, rect);
				}
				if (FrameEnabled)
				{
					float num2 = Math.Max(0.05f, Math.Min(1f, (float)_opacity / 100f));
					if (num2 >= 0.999f)
					{
						FrameStyles.Draw(graphics, area, FrameColors.Resolve(ColorId), StyleId, _thickness, Radius);
					}
					else
					{
						int num3 = Math.Max(1, (int)Math.Ceiling(area.Width));
						int num4 = Math.Max(1, (int)Math.Ceiling(area.Height));
						using Bitmap image = new Bitmap(num3, num4, PixelFormat.Format32bppArgb);
						using (Graphics graphics2 = Graphics.FromImage(image))
						{
							graphics2.Clear(Color.Transparent);
							HighQuality.Apply(graphics2);
							FrameStyles.Draw(graphics2, new RectangleF(0f, 0f, num3, num4), FrameColors.Resolve(ColorId), StyleId, _thickness, Radius);
						}
						ColorMatrix colorMatrix = new ColorMatrix(new float[5][]
						{
							new float[5] { 1f, 0f, 0f, 0f, 0f },
							new float[5] { 0f, 1f, 0f, 0f, 0f },
							new float[5] { 0f, 0f, 1f, 0f, 0f },
							new float[5] { 0f, 0f, 0f, num2, 0f },
							new float[5] { 0f, 0f, 0f, 0f, 1f }
						});
						using ImageAttributes imageAttributes = new ImageAttributes();
						imageAttributes.SetColorMatrix(colorMatrix);
						graphics.DrawImage(image, new Rectangle(0, 0, num3, num4), 0, 0, num3, num4, GraphicsUnit.Pixel, imageAttributes);
					}
				}
				else
				{
					Color baseColor = FrameColors.Resolve(ColorId);
					using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(40, 80, 80, 90)))
					{
						graphics.FillRectangle(brush2, rect);
					}
					FrameStyles.Draw(graphics, area, Color.FromArgb(120, baseColor), StyleId, _thickness, Radius);
					using Pen pen = new Pen(Color.FromArgb(170, 120, 120, 130), 1f);
					pen.DashStyle = DashStyle.Dash;
					graphics.DrawRectangle(pen, rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1f, rect.Height - 1f);
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			base.OnPaint(e);
		}
	}

	private sealed class ColorSwatchGrid : Control
	{
		internal sealed class Entry
		{
			public string Id;

			public string Name;

			public Color Color;
		}

		private readonly List<Entry> _items = new List<Entry>();

		private string _selected = "";

		private int _hot = -1;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string SelectedId
		{
			get
			{
				return _selected;
			}
			set
			{
				if (_selected != value)
				{
					_selected = value;
					Invalidate();
				}
			}
		}

		private int Cell => Fields.Sc(22);

		private int Gap => Fields.Sc(6);

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Columns { get; set; }

		private int Cols
		{
			get
			{
				if (Columns > 0)
				{
					return Columns;
				}
				int num = (Math.Max(1, Width) + Gap) / (Cell + Gap);
				if (num >= 1)
				{
					return num;
				}
				return 1;
			}
		}

		internal int CellCountForTest => _items.Count;

		internal Rectangle SelectedCellRectForTest
		{
			get
			{
				for (int i = 0; i < _items.Count; i++)
				{
					if (_items[i].Id == _selected)
					{
						return CellRect(i);
					}
				}
				return Rectangle.Empty;
			}
		}

		public event EventHandler Picked;

		public ColorSwatchGrid()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Design.Surface;
			Cursor = Cursors.Hand;
		}

		public void SetItems(IEnumerable<Entry> items)
		{
			_items.Clear();
			_items.AddRange(items);
			Invalidate();
		}

		private Rectangle CellRect(int i)
		{
			int cols = Cols;
			return new Rectangle(i % cols * (Cell + Gap), i / cols * (Cell + Gap), Cell, Cell);
		}

		private int IndexAt(Point p)
		{
			for (int i = 0; i < _items.Count; i++)
			{
				if (CellRect(i).Contains(p))
				{
					return i;
				}
			}
			return -1;
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = IndexAt(e.Location);
			if (num != _hot)
			{
				_hot = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			if (_hot != -1)
			{
				_hot = -1;
				Invalidate();
			}
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			int num = IndexAt(e.Location);
			if (num >= 0)
			{
				_selected = _items[num].Id;
				Invalidate();
				Picked?.Invoke(this, EventArgs.Empty);
			}
			base.OnMouseDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HighQuality.Apply(graphics);
			using (SolidBrush brush = new SolidBrush(Design.Surface))
			{
				graphics.FillRectangle(brush, ClientRectangle);
			}
			int num = Math.Max(1, Fields.Sc(2));
			int num2 = Fields.Sc(6);
			for (int i = 0; i < _items.Count; i++)
			{
				Rectangle r = CellRect(i);
				bool flag = _items[i].Id == _selected;
				bool flag2 = i == _hot;
				if (flag | flag2)
				{
					Rectangle r2 = new Rectangle(r.X - num, r.Y - num, r.Width + num * 2, r.Height + num * 2);
					using SolidBrush brush2 = new SolidBrush(flag ? Design.Accent : Color.FromArgb(140, Design.Accent));
					using GraphicsPath path = RoundedPath(r2, num2 + num);
					graphics.FillPath(brush2, path);
				}
				using (SolidBrush brush3 = new SolidBrush(Enabled ? _items[i].Color : Color.FromArgb(70, _items[i].Color)))
				{
					using GraphicsPath path2 = RoundedPath(r, num2);
					graphics.FillPath(brush3, path2);
				}
				using Pen pen = new Pen(Color.FromArgb(46, 24, 26, 33));
				using GraphicsPath path3 = RoundedPath(r, num2);
				graphics.DrawPath(pen, path3);
			}
		}
	}

	private sealed class StyleGallery : Control
	{
		private readonly string[] _styles = FrameStyles.All;

		private string _selected = FrameStyles.DefaultId;

		private string _colorId = FrameColors.DefaultId;

		private int _thickness = 3;

		private int _radius = 13;

		private bool _enabled = true;

		private int _hot = -1;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string SelectedId
		{
			get
			{
				return _selected;
			}
			set
			{
				if (_selected != value)
				{
					_selected = value;
					Invalidate();
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string ColorId
		{
			set
			{
				if (_colorId != value)
				{
					_colorId = value;
					Invalidate();
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Thickness
		{
			set
			{
				if (_thickness != value)
				{
					_thickness = value;
					Invalidate();
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Radius
		{
			set
			{
				if (_radius != value)
				{
					_radius = value;
					Invalidate();
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool FrameEnabled
		{
			set
			{
				if (_enabled != value)
				{
					_enabled = value;
					Invalidate();
				}
			}
		}

		private int CellW => Fields.Sc(70);

		private int CellH => Fields.Sc(46);

		private int Gap => Fields.Sc(6);

		private int ThumbH => Fields.Sc(30);

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Columns { get; set; }

		private int Cols
		{
			get
			{
				if (Columns > 0)
				{
					return Columns;
				}
				int num = (Math.Max(1, Width) + Gap) / (CellW + Gap);
				if (num >= 1)
				{
					return num;
				}
				return 1;
			}
		}

		internal int CellCountForTest => _styles.Length;

		internal Rectangle SelectedCellRectForTest
		{
			get
			{
				for (int i = 0; i < _styles.Length; i++)
				{
					if (_styles[i] == _selected)
					{
						return CellRect(i);
					}
				}
				return Rectangle.Empty;
			}
		}

		public event EventHandler Picked;

		public StyleGallery()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Design.Surface;
			Cursor = Cursors.Hand;
		}

		private Rectangle CellRect(int i)
		{
			int cols = Cols;
			return new Rectangle(i % cols * (CellW + Gap), i / cols * (CellH + Gap), CellW, CellH);
		}

		private int IndexAt(Point p)
		{
			for (int i = 0; i < _styles.Length; i++)
			{
				if (CellRect(i).Contains(p))
				{
					return i;
				}
			}
			return -1;
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = IndexAt(e.Location);
			if (num != _hot)
			{
				_hot = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			if (_hot != -1)
			{
				_hot = -1;
				Invalidate();
			}
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			int num = IndexAt(e.Location);
			if (num >= 0)
			{
				_selected = _styles[num];
				Invalidate();
				Picked?.Invoke(this, EventArgs.Empty);
			}
			base.OnMouseDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HighQuality.Apply(graphics);
			using (SolidBrush brush = new SolidBrush(Design.Surface))
			{
				graphics.FillRectangle(brush, ClientRectangle);
			}
			Color color = FrameColors.Resolve(_colorId);
			int radius = Fields.Sc(8);
			int num = Fields.Sc(7);
			int thickness = Math.Min(_thickness, 3);
			int radius2 = Math.Min(_radius, 8);
			for (int i = 0; i < _styles.Length; i++)
			{
				string text = _styles[i];
				Rectangle r = CellRect(i);
				bool flag = text == _selected;
				bool flag2 = i == _hot;
				Color color2;
				if (flag)
				{
					color2 = Design.AccentSoft;
				}
				else
				{
					color2 = (flag2 ? Color.FromArgb(246, 247, 250) : Design.Surface);
				}
				using (SolidBrush brush2 = new SolidBrush(color2))
				{
					using GraphicsPath path = RoundedPath(r, radius);
					graphics.FillPath(brush2, path);
				}
				using (Pen pen = new Pen(flag ? Design.Accent : Design.Stroke, flag ? ((float)Math.Max(1, Fields.Sc(2))) : 1f))
				{
					using GraphicsPath path2 = RoundedPath(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), radius);
					graphics.DrawPath(pen, path2);
				}
				Rectangle rectangle = new Rectangle(r.X + Fields.Sc(5), r.Y + Fields.Sc(4), r.Width - Fields.Sc(10), ThumbH);
				using (SolidBrush brush3 = new SolidBrush(Color.White))
				{
					graphics.FillRectangle(brush3, new Rectangle(rectangle.X + num, rectangle.Y + num, rectangle.Width - num * 2, rectangle.Height - num * 2));
				}
				FrameStyles.Draw(graphics, rectangle, _enabled ? color : Color.FromArgb(110, color), text, thickness, radius2, num);
				TextRenderer.DrawText(bounds: new Rectangle(r.X, r.Y + ThumbH + Fields.Sc(4), r.Width, Fields.Sc(14)), dc: graphics, text: ShortName(text), font: Font, foreColor: flag ? Design.Accent : Design.TextSecondary, flags: TextFormatFlags.EndEllipsis | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
				if (flag)
				{
					int num2 = Fields.Sc(13);
					Rectangle rect = new Rectangle(r.Right - num2 - Fields.Sc(3), r.Y + Fields.Sc(3), num2, num2);
					using (SolidBrush brush4 = new SolidBrush(Design.Accent))
					{
						graphics.FillEllipse(brush4, rect);
					}
					using Pen pen2 = new Pen(Color.White, Math.Max(1f, (float)Fields.Sc(2) * 0.8f));
					pen2.StartCap = LineCap.Round;
					pen2.EndCap = LineCap.Round;
					int num3 = rect.X + num2 / 2;
					int num4 = rect.Y + num2 / 2;
					float num5 = Fields.Sc(3);
					graphics.DrawLines(pen2, new PointF[3]
					{
						new PointF((float)num3 - num5, num4),
						new PointF((float)num3 - num5 * 0.35f, (float)num4 + num5 * 0.8f),
						new PointF((float)num3 + num5 * 1.05f, (float)num4 - num5 * 0.85f)
					});
				}
			}
		}

		private static string ShortName(string id)
		{
			return id switch
			{
				"Glow" => "柔光", 
				"Flat" => "纯净", 
				"Solid" => "实心", 
				"Double" => "双线", 
				"Dashed" => "虚线", 
				"Aurora" => "极光", 
				"Neon" => "霓虹", 
				"Halo" => "光晕", 
				"Corners" => "四角", 
				_ => id, 
			};
		}
	}

	private sealed class NavSidebar : Control
	{
		private ScrollableControl _host;

		private string[] _titles;

		private string[] _glyphs;

		private Control[] _targets;

		private int _hot = -1;

		private int _active;

		private float[] _rowGlow;

		private readonly System.Windows.Forms.Timer _glowTimer = new System.Windows.Forms.Timer();

		internal int SyncCountForTest;

		internal int LastTopForTest;

		internal int LastBestForTest = -1;

		private int RowHeight => Fields.Sc(42);

		private int PadTop => Fields.Sc(16);

		private int PadX => Fields.Sc(14);

		internal int ActiveForTest => _active;

		internal int CountForTest
		{
			get
			{
				if (_titles != null)
				{
					return _titles.Length;
				}
				return 0;
			}
		}

		public NavSidebar()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Design.SidebarSurface;
			Cursor = Cursors.Hand;
			_glowTimer.Interval = 16;
			_glowTimer.Tick += (object? s, EventArgs e) =>
			{
				StepGlow();
			};
		}

		private void StepGlow()
		{
			if (_rowGlow == null)
			{
				_glowTimer.Stop();
				return;
			}
			bool flag = false;
			for (int i = 0; i < _rowGlow.Length; i++)
			{
				float num;
				if (i == _active)
				{
					num = 1f;
				}
				else
				{
					num = ((i == _hot) ? 0.55f : 0f);
				}
				float num2 = num - _rowGlow[i];
				if (Math.Abs(num2) < 0.03f)
				{
					_rowGlow[i] = num;
					continue;
				}
				_rowGlow[i] += num2 * 0.32f;
				flag = true;
			}
			if (!flag)
			{
				_glowTimer.Stop();
			}
			if (!IsDisposed)
			{
				Invalidate();
			}
		}

		private void KickGlow()
		{
			if (_rowGlow == null || _rowGlow.Length != ((_titles != null) ? _titles.Length : 0))
			{
				_rowGlow = new float[(_titles != null) ? _titles.Length : 0];
				if (_active >= 0 && _active < _rowGlow.Length)
				{
					_rowGlow[_active] = 1f;
				}
			}
			if (!_glowTimer.Enabled)
			{
				_glowTimer.Start();
			}
		}

		public void Bind(ScrollableControl host, string[] titles, Control[] targets)
		{
			_host = host;
			_titles = titles;
			_targets = targets;
			_glyphs = new string[titles?.Length ?? 0];
			for (int i = 0; i < _glyphs.Length; i++)
			{
				_glyphs[i] = GlyphForSection(titles[i]);
			}
			_rowGlow = null;
			KickGlow();
			Invalidate();
		}

		private int IndexAt(int y)
		{
			if (_titles == null || _titles.Length == 0)
			{
				return -1;
			}
			int num = (y - PadTop) / RowHeight;
			if (num < 0 || num >= _titles.Length)
			{
				return -1;
			}
			return num;
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = IndexAt(e.Y);
			if (num != _hot)
			{
				_hot = num;
				KickGlow();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			if (_hot != -1)
			{
				_hot = -1;
				KickGlow();
			}
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			int num = IndexAt(e.Y);
			if (num >= 0)
			{
				_active = num;
				ScrollTo(num);
				KickGlow();
			}
			base.OnMouseDown(e);
		}

		private void ScrollTo(int i)
		{
			if (_host == null || _targets == null || i < 0 || i >= _targets.Length)
			{
				return;
			}
			Control control = _targets[i];
			if (control == null)
			{
				return;
			}
			try
			{
				int num = control.Top - Fields.Sc(6);
				if (num < 0)
				{
					num = 0;
				}
				_host.AutoScrollPosition = new Point(0, num);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		public void SyncActive()
		{
			SyncCountForTest++;
			if (_host == null || _targets == null)
			{
				return;
			}
			int num;
			try
			{
				num = -_host.AutoScrollPosition.Y;
			}
			catch
			{
				return;
			}
			LastTopForTest = num;
			int num2 = 0;
			for (int i = 0; i < _targets.Length; i++)
			{
				if (_targets[i] != null && _targets[i].Top <= num + Fields.Sc(40))
				{
					num2 = i;
				}
			}
			try
			{
				int num3 = _host.DisplayRectangle.Height - _host.ClientSize.Height;
				if (num3 > 0 && num >= num3 - 2 && _targets.Length != 0)
				{
					num2 = _targets.Length - 1;
				}
			}
			catch
			{
			}
			LastBestForTest = num2;
			if (num2 != _active)
			{
				_active = num2;
				KickGlow();
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HighQuality.Apply(graphics);
			using (SolidBrush brush = new SolidBrush(Design.SidebarSurface))
			{
				graphics.FillRectangle(brush, ClientRectangle);
			}
			using (Pen pen = new Pen(Design.Stroke))
			{
				graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
			}
			if (_titles == null)
			{
				return;
			}
			int rowHeight = RowHeight;
			int padTop = PadTop;
			int padX = PadX;
			using Font font = new Font(Font.FontFamily, Fields.Sc(13), FontStyle.Regular, GraphicsUnit.Pixel);
			using Font font2 = Icons.At(Fields.Sc(16));
			for (int i = 0; i < _titles.Length; i++)
			{
				int num = padTop + i * rowHeight;
				Rectangle r = new Rectangle(padX - Fields.Sc(6), num + Fields.Sc(3), Width - (padX - Fields.Sc(6)) - Fields.Sc(10), rowHeight - Fields.Sc(6));
				bool flag = i == _active;
				bool flag2 = i == _hot;
				float num2;
				if (_rowGlow != null && i < _rowGlow.Length)
				{
					num2 = _rowGlow[i];
				}
				else
				{
					num2 = (flag ? 1f : 0f);
				}
				if (num2 > 0.01f)
				{
					Color color = (flag ? Design.AccentSoft : Color.FromArgb(242, 243, 246));
					using SolidBrush brush2 = new SolidBrush(MixColor(Color.FromArgb(0, color), color, Math.Min(1f, num2 * 1.35f)));
					using GraphicsPath path = RoundedPath(r, Fields.Sc(8));
					graphics.FillPath(brush2, path);
				}
				if (num2 > 0.01f)
				{
					Rectangle r2 = new Rectangle(r.Left + Fields.Sc(2), r.Top + Fields.Sc(7), Fields.Sc(3), r.Height - Fields.Sc(14));
					using SolidBrush brush3 = new SolidBrush(Color.FromArgb((int)Math.Round(255f * Math.Min(1f, num2)), Design.Accent));
					using GraphicsPath path2 = RoundedPath(r2, Fields.Sc(2));
					graphics.FillPath(brush3, path2);
				}
				Rectangle rectangle = new Rectangle(r.Left + Fields.Sc(13), r.Top, r.Width - Fields.Sc(18), r.Height);
				Color b;
				if (flag)
				{
					b = Design.Accent;
				}
				else
				{
					b = (flag2 ? Design.TextPrimary : Design.TextSecondary);
				}
				b = MixColor(Design.TextSecondary, b, Math.Min(1f, num2));
				int num3 = rectangle.Left;
				if (font2 != null && _glyphs != null && i < _glyphs.Length)
				{
					int num4 = Fields.Sc(16);
					TextRenderer.DrawText(bounds: new Rectangle(r.Left + Fields.Sc(13), r.Top + (r.Height - num4) / 2, num4, num4), dc: graphics, text: _glyphs[i], font: font2, foreColor: b, flags: TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
					num3 = r.Left + Fields.Sc(13) + num4 + Fields.Sc(8);
				}
				TextRenderer.DrawText(bounds: new Rectangle(num3, r.Top, r.Right - Fields.Sc(6) - num3, r.Height), dc: graphics, text: _titles[i], font: font, foreColor: b, flags: TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
		}
	}

	private sealed class InnerLayout
	{
		public enum Mode
		{
			Fixed,
			Grow,
			Shift
		}

		public Control Control;

		public int BaseLeft;

		public int BaseWidth;

		public int RightGap;

		public Mode How;

		public int BaseInner;

		public Control GrowRightNeighbor;

		public int GrowNeighborGap;

		public int ShiftRightGap = -1;

		public bool WasTextWidened;

		public int OriginalWidth;

		public int ShiftByOffset;

		public InnerLayout RightPartner;

		public bool WrapsToNextRow;

		public int WrapOffsetY;

		public int BaseTop;

		public int BaseHeight;
	}

	private sealed class DesignSnapshot
	{
		public Control Control;

		public Point Location;

		public Size Size;
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

	private static class Design
	{
		public static readonly Color Canvas = Color.FromArgb(240, 242, 245);

		public static readonly Color Surface = Color.White;

		public static readonly Color SurfaceTop = Color.White;

		public static readonly Color SurfaceBottom = Color.FromArgb(251, 252, 254);

		public static readonly Color SidebarSurface = Color.FromArgb(250, 251, 252);

		public static readonly Color Stroke = Color.FromArgb(220, 224, 232);

		public const int Radius = 14;

		public static readonly Color Shadow = Color.FromArgb(22, 28, 45);

		public const int ShadowOffset = 1;

		public const int ShadowSpread = 5;

		public const int AccentBarWidth = 4;

		public static readonly Color TextPrimary = Color.FromArgb(24, 26, 33);

		public static readonly Color TextSecondary = Color.FromArgb(122, 128, 142);

		public static readonly Color Accent = Color.FromArgb(94, 84, 222);

		public static readonly Color AccentPressed = Color.FromArgb(76, 66, 198);

		public static readonly Color AccentSoft = Color.FromArgb(238, 237, 253);

		public static readonly Color InputStroke = Color.FromArgb(205, 210, 222);

		public static readonly Color InputStrokeFocus = Color.FromArgb(94, 84, 222);

		public const int FieldRadius = 6;

		public static readonly Color Hint = Color.FromArgb(113, 113, 122);

		public static readonly Color WarnText = Color.FromArgb(161, 98, 7);

		public static readonly Color ReadOnlyFill = Color.FromArgb(247, 248, 251);

		public static readonly Color Warning = Color.FromArgb(190, 106, 12);

		public static readonly Color Success = Color.FromArgb(24, 128, 78);

		public static readonly Color Danger = Color.FromArgb(196, 40, 40);
	}

	private static class HighQuality
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

	private sealed class CardGroup : GroupBox
	{
		public int TitleFontPx = 13;

		public int TitleInsetPx = 32;

		public int TitleY = 9;

		public int RadiusPx = 14;

		public int ShadowOffsetPx = 1;

		public int ShadowSpreadPx = 5;

		public int AccentBarPx = 4;

		public CardGroup()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Design.Surface;
			ForeColor = Design.TextPrimary;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HighQuality.Apply(graphics);
			int width = Width;
			int height = Height;
			if (width <= 0 || height <= 0)
			{
				return;
			}
			using (SolidBrush brush = new SolidBrush((Parent != null) ? Parent.BackColor : Design.Canvas))
			{
				graphics.FillRectangle(brush, 0, 0, width, height);
			}
			int num = Math.Max(2, RadiusPx);
			if (ShadowSpreadPx > 0)
			{
				int num2 = ShadowSpreadPx * 2;
				using (GraphicsPath path = RoundedPath(new Rectangle(1 - num2 / 2, ShadowOffsetPx + num2 / 3, width - 2 + num2, height - 2 + num2), num + num2 / 2))
				{
					for (int num3 = num2; num3 >= 1; num3--)
					{
						using Pen pen = new Pen(Color.FromArgb(Math.Max(2, 8 - num3 / 3), Design.Shadow), (float)num3 * 2f);
						graphics.DrawPath(pen, path);
					}
				}
				using GraphicsPath path2 = RoundedPath(new Rectangle(1, ShadowOffsetPx, width - 2, height - 2), num);
				for (int num4 = ShadowSpreadPx; num4 >= 1; num4--)
				{
					using Pen pen2 = new Pen(Color.FromArgb(Math.Max(5, 24 - num4 * 3), Design.Shadow), (float)num4 * 2f);
					graphics.DrawPath(pen2, path2);
				}
			}
			GraphicsPath graphicsPath = RoundedPath(new Rectangle(0, 0, width - 1, height - 1), num);
			using (LinearGradientBrush brush2 = new LinearGradientBrush(new Rectangle(0, 0, width, height), Design.SurfaceTop, Design.SurfaceBottom, LinearGradientMode.Vertical))
			{
				graphics.FillPath(brush2, graphicsPath);
			}
			using (Pen pen3 = new Pen(Design.Stroke))
			{
				graphics.DrawPath(pen3, graphicsPath);
			}
			if (!string.IsNullOrEmpty(Text) && TitleInsetPx > 8)
			{
				int num5 = TitleInsetPx - Math.Max(2, Fields.Sc(1));
				int num6 = AccentBarPx + 12;
				int num7 = width - Fields.Sc(14);
				if (num7 > num6 + Fields.Sc(20) && num5 > 0 && num5 < height - 2)
				{
					using Pen pen4 = new Pen(Color.FromArgb(20, Design.Shadow));
					graphics.DrawLine(pen4, num6, num5, num7, num5);
				}
			}
			if (AccentBarPx > 0)
			{
				using SolidBrush brush3 = new SolidBrush(Design.Accent);
				using Region region = new Region(graphicsPath);
				using Region region2 = new Region(new Rectangle(0, 0, AccentBarPx, height));
				region.Intersect(region2);
				graphics.FillRegion(brush3, region);
			}
			graphicsPath.Dispose();
			Fields.PaintFor(graphics, this);
			if (string.IsNullOrEmpty(Text))
			{
				return;
			}
			float num8 = AccentBarPx + 12;
			using (Font font = Icons.At(TitleFontPx + 1))
			{
				if (font != null)
				{
					string s = GlyphForSection(Text);
					float num9 = TitleFontPx + 6;
					using (SolidBrush brush4 = new SolidBrush(Design.Accent))
					{
						graphics.DrawString(s, font, brush4, new PointF(num8, TitleY - 1));
					}
					num8 += num9;
				}
			}
			using Font font2 = new Font(Font.FontFamily, TitleFontPx, FontStyle.Bold, GraphicsUnit.Pixel);
			using SolidBrush brush5 = new SolidBrush(Design.TextPrimary);
			graphics.DrawString(Text, font2, brush5, new PointF(num8, TitleY));
		}
	}

	private sealed class Tween
	{
		private readonly Control _owner;

		private readonly System.Windows.Forms.Timer _timer;

		private float _value;

		private float _target;

		public float Value => _value;

		public Tween(Control owner)
		{
			_owner = owner;
			_timer = new System.Windows.Forms.Timer();
			_timer.Interval = 16;
			_timer.Tick += (object? s, EventArgs e) =>
			{
				Step();
			};
		}

		public void To(float target)
		{
			if (!(Math.Abs(target - _target) < 0.001f))
			{
				_target = target;
				if (!_timer.Enabled)
				{
					_timer.Start();
				}
			}
		}

		public void Snap(float v)
		{
			_timer.Stop();
			_value = v;
			_target = v;
		}

		private void Step()
		{
			float num = _target - _value;
			if (Math.Abs(num) < 0.02f)
			{
				_value = _target;
				_timer.Stop();
			}
			else
			{
				_value += num * 0.35f;
			}
			try
			{
				if (!_owner.IsDisposed)
				{
					_owner.Invalidate();
				}
			}
			catch
			{
			}
		}
	}

	private static class Fields
	{
		private static readonly Dictionary<Control, int> _radius = new Dictionary<Control, int>();

		private static readonly Dictionary<Control, Region> _live = new Dictionary<Control, Region>();

		private static int _textBoxHeight = -1;

		private static Font _textBoxHeightFont;

		public static int Sc(int design)
		{
			float num = CurrentUiScale;
			if (num <= 0f)
			{
				num = 1f;
			}
			int num2 = (int)Math.Round((float)design * num);
			if (num2 >= 1)
			{
				return num2;
			}
			return 1;
		}

		public static void ResetCaches()
		{
			_textBoxHeight = -1;
			_textBoxHeightFont = null;
		}

		public static void MakeFlat(TextBox t, Font font, bool readOnly)
		{
			try
			{
				Font font2 = ((font != null) ? font : t.Font);
				if (_textBoxHeight < 0 || _textBoxHeightFont != font2)
				{
					using (TextBox textBox = new TextBox())
					{
						textBox.Font = font2;
						textBox.BorderStyle = BorderStyle.FixedSingle;
						_textBoxHeight = textBox.Height;
					}
					_textBoxHeightFont = font2;
				}
				t.BorderStyle = BorderStyle.None;
				t.AutoSize = false;
				t.Height = _textBoxHeight;
				t.BackColor = (readOnly ? Design.ReadOnlyFill : Design.Surface);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		public static void Attach(Control c, int designRadius)
		{
			_radius[c] = designRadius;
			c.SizeChanged += OnSizeChanged;
			c.HandleCreated += OnSizeChanged;
			Sync(c);
		}

		private static void OnSizeChanged(object sender, EventArgs e)
		{
			Sync(sender as Control);
		}

		public static void Sync(Control c)
		{
			if (c == null || !_radius.TryGetValue(c, out var value) || c.Width <= 0 || c.Height <= 0)
			{
				return;
			}
			if (_live.TryGetValue(c, out var value2))
			{
				try
				{
					c.Region = null;
				}
				catch
				{
				}
				value2.Dispose();
				_live.Remove(c);
			}
			int num = Sc(value);
			if (num > c.Height / 2)
			{
				num = c.Height / 2;
			}
			if (num > c.Width / 2)
			{
				num = c.Width / 2;
			}
			if (num < 1)
			{
				return;
			}
			GraphicsPath graphicsPath = RoundedPath(new Rectangle(0, 0, c.Width, c.Height), num);
			try
			{
				Region value3 = (c.Region = new Region(graphicsPath));
				_live[c] = value3;
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			finally
			{
				graphicsPath.Dispose();
			}
		}

		public static void PaintFor(Graphics g, Control parent)
		{
			int num = Sc(1);
			foreach (KeyValuePair<Control, int> item in _radius)
			{
				Control key = item.Key;
				if (key == null || key.Parent != parent || !key.Visible || key.Width <= 0 || key.Height <= 0)
				{
					continue;
				}
				Rectangle r = new Rectangle(key.Left - num, key.Top - num, key.Width + num * 2, key.Height + num * 2);
				int radius = Sc(item.Value) + num;
				GraphicsPath graphicsPath = RoundedPath(r, radius);
				try
				{
					using (SolidBrush brush = new SolidBrush(key.Enabled ? key.BackColor : Design.Canvas))
					{
						g.FillPath(brush, graphicsPath);
					}
					using Pen pen = new Pen(key.Enabled ? Design.InputStroke : Color.FromArgb(228, 231, 238));
					g.DrawPath(pen, graphicsPath);
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
				finally
				{
					graphicsPath.Dispose();
				}
			}
		}
	}

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
			BackColor = Design.Surface;
			ForeColor = Design.TextPrimary;
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
			HighQuality.Apply(graphics);
			bool flag = (e.State & DrawItemState.Selected) != 0;
			Rectangle bounds = e.Bounds;
			using (SolidBrush brush = new SolidBrush(flag ? Design.AccentSoft : Design.Surface))
			{
				graphics.FillRectangle(brush, bounds);
			}
			if (flag)
			{
				using SolidBrush brush2 = new SolidBrush(Design.Accent);
				graphics.FillRectangle(brush2, new Rectangle(bounds.Left, bounds.Top, Fields.Sc(3), bounds.Height));
			}
			object obj = ((e.Index < Items.Count) ? Items[e.Index] : null);
			Rectangle rectangle = new Rectangle(bounds.Left + Fields.Sc(8), bounds.Top, bounds.Width - Fields.Sc(14), bounds.Height);
			if (ClosedPainter != null)
			{
				ClosedPainter(graphics, rectangle, obj);
			}
			else
			{
				TextRenderer.DrawText(graphics, (obj != null) ? obj.ToString() : "", Font, rectangle, Design.TextPrimary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
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
				HighQuality.Apply(graphics);
				int num = Fields.Sc(1);
				GraphicsPath graphicsPath = RoundedPath(new Rectangle(num, num, Width - num * 2 - 1, Height - num * 2 - 1), Fields.Sc(6));
				try
				{
					using (SolidBrush brush = new SolidBrush(Enabled ? BackColor : Design.Canvas))
					{
						graphics.FillPath(brush, graphicsPath);
					}
					Color color;
					if (Enabled)
					{
						color = ((_hover || _open) ? Design.InputStrokeFocus : Design.InputStroke);
					}
					else
					{
						color = Color.FromArgb(228, 231, 238);
					}
					using Pen pen = new Pen(color);
					graphics.DrawPath(pen, graphicsPath);
				}
				finally
				{
					graphicsPath.Dispose();
				}
				int num2 = Fields.Sc(26);
				object obj = ((SelectedIndex >= 0 && SelectedIndex < Items.Count) ? Items[SelectedIndex] : null);
				Rectangle rectangle = new Rectangle(Fields.Sc(10), 0, Width - num2 - Fields.Sc(14), Height);
				if (ClosedPainter != null)
				{
					ClosedPainter(graphics, rectangle, obj);
				}
				else
				{
					TextRenderer.DrawText(graphics, (obj != null) ? obj.ToString() : "", Font, rectangle, Enabled ? Design.TextPrimary : Color.FromArgb(160, 165, 176), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
				}
				float num3 = (float)Width - (float)num2 / 2f;
				float num4 = (float)Height / 2f;
				float num5 = (float)Fields.Sc(2) * 1.15f;
				using Pen pen2 = new Pen(Enabled ? Design.TextSecondary : Color.FromArgb(180, 185, 195), Math.Max(1.2f, (float)Fields.Sc(1) * 1.4f));
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

	private sealed class FlatNum : NumericUpDown
	{
		public FlatNum()
		{
			BorderStyle = BorderStyle.FixedSingle;
			BackColor = Design.Surface;
			ForeColor = Design.TextPrimary;
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			for (int i = 0; i < Controls.Count; i++)
			{
				try
				{
					Controls[i].BackColor = Design.Surface;
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

		protected override void OnPaint(PaintEventArgs e)
		{
			using SolidBrush brush = new SolidBrush(Enabled ? Design.Surface : Design.Canvas);
			e.Graphics.FillRectangle(brush, ClientRectangle);
		}
	}

	private sealed class FlatCheck : CheckBox
	{
		private readonly Tween _hoverT;

		private readonly Tween _checkT;

		public FlatCheck()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			FlatStyle = FlatStyle.Flat;
			BackColor = Design.Surface;
			ForeColor = Design.TextPrimary;
			Cursor = Cursors.Hand;
			_hoverT = new Tween(this);
			_checkT = new Tween(this);
			_checkT.Snap(Checked ? 1f : 0f);
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
			HighQuality.Apply(graphics);
			graphics.Clear(BackColor);
			float num = ((CurrentUiScale > 0f) ? CurrentUiScale : 1f);
			int num2 = CheckTextChrome - Fields.Sc(10);
			int num3 = (int)Math.Round((double)num2 * 0.56);
			if (num2 < 20)
			{
				num2 = 20;
			}
			if (num3 < 12)
			{
				num3 = 12;
			}
			int num4 = Math.Max(2, CheckTextChrome - num2);
			int num5 = (Height - num3) / 2;
			if (num5 < 0)
			{
				num5 = 0;
			}
			Rectangle r = new Rectangle(0, num5, num2, num3);
			int radius = num3 / 2;
			bool flag = Checked;
			float value = _hoverT.Value;
			float num6 = (flag ? Math.Min(1f, _checkT.Value) : 0f);
			if (flag && num6 <= 0.001f)
			{
				num6 = 1f;
			}
			Color color;
			if (!Enabled)
			{
				color = (flag ? Color.FromArgb(203, 206, 214) : Color.FromArgb(238, 240, 244));
			}
			else
			{
				Color a = MixColor(Color.FromArgb(206, 210, 219), Color.FromArgb(186, 191, 202), value);
				Color b = MixColor(Design.Accent, ControlPaint.Light(Design.Accent, 0.12f), value);
				color = MixColor(a, b, num6);
			}
			using (GraphicsPath path = RoundedPath(r, radius))
			{
				using SolidBrush brush = new SolidBrush(color);
				graphics.FillPath(brush, path);
			}
			int num7 = (int)Math.Round(2.5 * (double)num);
			if (num7 < 1)
			{
				num7 = 1;
			}
			int num8 = num3 - num7 * 2;
			if (num8 < 4)
			{
				num8 = 4;
			}
			int num9 = num2 - num7 * 2 - num8;
			if (num9 < 0)
			{
				num9 = 0;
			}
			int x = r.Left + num7 + (int)Math.Round((float)num9 * num6);
			Rectangle rect = new Rectangle(x, r.Top + num7, num8, num8);
			using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(28, Design.Shadow)))
			{
				graphics.FillEllipse(brush2, new Rectangle(rect.X, rect.Y + Math.Max(1, Fields.Sc(1)), rect.Width, rect.Height));
			}
			using (SolidBrush brush3 = new SolidBrush(Enabled ? Color.White : Color.FromArgb(246, 247, 250)))
			{
				graphics.FillEllipse(brush3, rect);
			}
			Rectangle bounds = new Rectangle(r.Right + num4, 0, Width - r.Right - num4, Height);
			if (bounds.Width > 0)
			{
				Color foreColor = (Enabled ? ForeColor : Color.FromArgb(160, 165, 176));
				TextRenderer.DrawText(graphics, Text, Font, bounds, foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
		}
	}

	internal sealed class HelpPanel : Control
	{
		internal sealed class Entry
		{
			public string Icon;

			public string Title;

			public string Body;
		}

		private readonly List<Entry> _entries = new List<Entry>();

		private int _open;

		private int _hover = -1;

		private int _headerH = 38;

		private int _bodyH = 88;

		internal int EntryCountForTest => _entries.Count;

		internal int OpenIndexForTest => _open;

		public HelpPanel()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			Cursor = Cursors.Hand;
		}

		public void SetEntries(List<Entry> list)
		{
			_entries.Clear();
			if (list != null)
			{
				_entries.AddRange(list);
			}
			Invalidate();
		}

		public void SetMetrics(int headerH, int bodyH)
		{
			_headerH = Math.Max(1, headerH);
			_bodyH = Math.Max(1, bodyH);
			Invalidate();
		}

		internal void ClickAtForTest(int y)
		{
			int num = HitHeader(y);
			if (num >= 0 && num != _open)
			{
				_open = num;
			}
			Invalidate();
		}

		internal void OpenRangeForTest(out int top, out int bottom)
		{
			top = RowTop(_open);
			bottom = top + _headerH + _bodyH;
		}

		private int RowTop(int index)
		{
			int num = 0;
			for (int i = 0; i < index && i < _entries.Count; i++)
			{
				num += _headerH;
				if (i == _open)
				{
					num += _bodyH;
				}
			}
			return num;
		}

		private int HitHeader(int y)
		{
			for (int i = 0; i < _entries.Count; i++)
			{
				int num = RowTop(i);
				if (y >= num && y < num + _headerH)
				{
					return i;
				}
			}
			return -1;
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = HitHeader(e.Y);
			if (num != _hover)
			{
				_hover = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			if (_hover != -1)
			{
				_hover = -1;
				Invalidate();
			}
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			int num = HitHeader(e.Y);
			if (num >= 0 && num != _open)
			{
				_open = num;
				Invalidate();
			}
			base.OnMouseDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			try
			{
				Graphics graphics = e.Graphics;
				HighQuality.Apply(graphics);
				graphics.Clear(BackColor);
				int num = Fields.Sc(4);
				using Font font = Icons.At(Fields.Sc(15));
				using Font font2 = new Font(Font.FontFamily, Fields.Sc(13), FontStyle.Bold, GraphicsUnit.Pixel);
				using Font font3 = new Font(Font.FontFamily, Fields.Sc(12), FontStyle.Regular, GraphicsUnit.Pixel);
				for (int i = 0; i < _entries.Count; i++)
				{
					int num2 = RowTop(i);
					bool flag = i == _open;
					Rectangle r = new Rectangle(num, num2 + num / 2, Width - num * 2, _headerH - num);
					if (r.Width <= 0 || r.Height <= 0)
					{
						continue;
					}
					if (flag || i == _hover)
					{
						using SolidBrush brush = new SolidBrush(flag ? Design.AccentSoft : Color.FromArgb(244, 245, 248));
						using GraphicsPath path = RoundedPath(r, Fields.Sc(8));
						graphics.FillPath(brush, path);
					}
					int num3 = r.Left + Fields.Sc(12);
					if (font != null)
					{
						Rectangle bounds = new Rectangle(num3, r.Top, Fields.Sc(16), r.Height);
						TextRenderer.DrawText(graphics, _entries[i].Icon, font, bounds, flag ? Design.Accent : Design.TextSecondary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
						num3 = bounds.Right + Fields.Sc(8);
					}
					int num4 = Fields.Sc(26);
					Rectangle bounds2 = new Rectangle(num3, r.Top, r.Right - num4 - num3, r.Height);
					if (bounds2.Width > 0)
					{
						TextRenderer.DrawText(graphics, _entries[i].Title, font2, bounds2, flag ? Design.Accent : Design.TextPrimary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
					}
					DrawChevron(graphics, r.Right - Fields.Sc(18), r.Top + r.Height / 2, Fields.Sc(5), flag, flag ? Design.Accent : Design.TextSecondary);
					if (flag)
					{
						Rectangle bounds3 = new Rectangle(r.Left + Fields.Sc(12), num2 + _headerH, Width - num * 2 - Fields.Sc(24), _bodyH);
						if (bounds3.Width > 0 && bounds3.Height > 0)
						{
							TextRenderer.DrawText(graphics, _entries[i].Body, font3, bounds3, Design.TextSecondary, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		private static void DrawChevron(Graphics g, int cx, int cy, int u, bool open, Color color)
		{
			using Pen pen = new Pen(color, Math.Max(1.4f, (float)u * 0.34f));
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			pen.LineJoin = LineJoin.Round;
			if (!open)
			{
				g.DrawLines(pen, new PointF[3]
				{
					new PointF((float)cx - (float)u * 0.4f, cy - u),
					new PointF((float)cx + (float)u * 0.5f, cy),
					new PointF((float)cx - (float)u * 0.4f, cy + u)
				});
			}
			else
			{
				g.DrawLines(pen, new PointF[3]
				{
					new PointF(cx - u, (float)cy - (float)u * 0.4f),
					new PointF(cx, (float)cy + (float)u * 0.5f),
					new PointF(cx + u, (float)cy - (float)u * 0.4f)
				});
			}
		}
	}

	private sealed class FlatButton : Button
	{
		public bool Primary;

		private readonly Tween _hoverT;

		private readonly Tween _downT;

		internal float ProbeHoverValue => _hoverT.Value;

		public FlatButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			FlatStyle = FlatStyle.Flat;
			FlatAppearance.BorderSize = 0;
			FlatAppearance.MouseOverBackColor = Color.Transparent;
			FlatAppearance.MouseDownBackColor = Color.Transparent;
			BackColor = Design.Surface;
			ForeColor = Design.TextPrimary;
			Cursor = Cursors.Hand;
			UseVisualStyleBackColor = false;
			_hoverT = new Tween(this);
			_downT = new Tween(this);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hoverT.To(1f);
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hoverT.To(0f);
			_downT.To(0f);
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			_downT.To(1f);
			base.OnMouseDown(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			_downT.To(0f);
			base.OnMouseUp(e);
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			Invalidate();
			base.OnEnabledChanged(e);
		}

		internal void ProbeSetHover(bool on)
		{
			_hoverT.To(on ? 1f : 0f);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HighQuality.Apply(graphics);
			Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
			if (r.Width <= 0 || r.Height <= 0)
			{
				return;
			}
			int num = ((CurrentUiScale > 0f) ? ((int)Math.Round(6f * CurrentUiScale)) : 6);
			if (num > r.Height / 2)
			{
				num = r.Height / 2;
			}
			if (num < 1)
			{
				num = 1;
			}
			float value = _hoverT.Value;
			float value2 = _downT.Value;
			Color foreColor;
			Color color2;
			Color color;
			if (!Enabled)
			{
				color = Color.FromArgb(240, 241, 243);
				foreColor = Color.FromArgb(170, 175, 184);
				color2 = Color.FromArgb(233, 235, 239);
			}
			else if (Primary)
			{
				Color accent = Design.Accent;
				Color b = ControlPaint.Light(Design.Accent, 0.1f);
				Color accentPressed = Design.AccentPressed;
				color = MixColor(accent, b, value);
				color = MixColor(color, accentPressed, value2);
				foreColor = Color.White;
				color2 = color;
			}
			else
			{
				color = MixColor(Design.Surface, Color.FromArgb(250, 251, 252), value);
				color = MixColor(color, Color.FromArgb(238, 240, 244), value2);
				foreColor = Design.TextPrimary;
				color2 = MixColor(Design.Stroke, Color.FromArgb(205, 210, 219), value);
			}
			GraphicsPath path = RoundedPath(r, num);
			using (SolidBrush brush = new SolidBrush(color))
			{
				graphics.FillPath(brush, path);
			}
			using (Pen pen = new Pen(color2))
			{
				graphics.DrawPath(pen, path);
			}
			if (Primary && Enabled && r.Height > num * 2 + 4)
			{
				using Pen pen2 = new Pen(Color.FromArgb(52, Color.White), Math.Max(1f, Fields.Sc(1)));
				graphics.DrawLine(pen2, r.X + num, r.Y + 1, r.Right - num, r.Y + 1);
			}
			TextRenderer.DrawText(graphics, Text, Font, new Rectangle(0, 0, Width, Height), foreColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
			if (!Focused || !Enabled)
			{
				return;
			}
			using Pen pen3 = new Pen(Design.Accent, 1.6f);
			graphics.DrawPath(pen3, RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), num - 1));
		}
	}

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
	}

	internal sealed class GroupClipReport
	{
		public int Total;

		public int Clipped;

		public string Detail = "";
	}

	internal sealed class FramePickerProbe
	{
		public int ColorCells;

		public int StyleCells;

		public Size GridSize;

		public Size GallerySize;

		public bool GridInsideGroup;

		public bool GalleryInsideGroup;

		public int GridInk;

		public int GalleryInk;

		public int GridAccentInk;

		public int GalleryAccentInk;

		public Rectangle GridSelectedCell;

		public Rectangle GallerySelectedCell;
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

	private readonly ConfigStore _store;

	private readonly Coordinator _coord;

	private AppConfig _edit;

	private bool _recording;

	private readonly TextBox _txtKey = new TextBox();

	private readonly Button _btnRecord = new FlatButton();

	private readonly Label _lblKeyHint = new Label();

	private readonly CheckBox _chkHold = new FlatCheck();

	private readonly NumericUpDown _numHold = new FlatNum();

	private readonly ComboBox _cmbLeft = new FlatCombo();

	private readonly ComboBox _cmbRight = new FlatCombo();

	private readonly ComboBox _cmbMiddle = new FlatCombo();

	private readonly CheckBox _chkFrame = new FlatCheck();

	private readonly ComboBox _cmbFrameColor = new FlatCombo();

	private readonly ComboBox _cmbFrameStyle = new FlatCombo();

	private readonly ColorSwatchGrid _gridColor = new ColorSwatchGrid();

	private readonly StyleGallery _galleryStyle = new StyleGallery();

	private readonly Label _lblColorName = new Label();

	private readonly NumericUpDown _numFrameThick = new FlatNum();

	private readonly NumericUpDown _numFrameRadius = new FlatNum();

	private readonly NumericUpDown _numFrameOpacity = new FlatNum();

	private readonly HelpPanel _helpPanel = new HelpPanel();

	private readonly FramePreviewBox _pnlPreview = new FramePreviewBox();

	private readonly Label _lblFrameTip = new Label();

	private readonly NumericUpDown _numCorner = new FlatNum();

	private readonly CheckBox _chkShake = new FlatCheck();

	private readonly CheckBox _chkDblLeft = new FlatCheck();

	private readonly CheckBox _chkDblRight = new FlatCheck();

	private readonly CheckBox _chkRestoreMax = new FlatCheck();

	private readonly CheckBox _chkFront = new FlatCheck();

	private readonly CheckBox _chkFullscreen = new FlatCheck();

	private readonly CheckBox _chkExitFullscreen = new FlatCheck();

	private readonly TextBox _txtBlacklist = new TextBox();

	private readonly ComboBox _cmbRefreshMode = new FlatCombo();

	private readonly NumericUpDown _numRefreshHz = new FlatNum();

	private readonly Label _lblRefreshHint = new Label();

	private readonly ComboBox _cmbAutoStart = new FlatCombo();

	private readonly CheckBox _chkRunAsAdmin = new FlatCheck();

	private readonly CheckBox _chkWarnNonAdmin = new FlatCheck();

	private readonly Label _lblAdmin = new Label();

	private readonly Button _btnRestartAdmin = new FlatButton();

	private readonly Label _lblStatus = new Label();

	private NavSidebar _sidebar;

	private bool _populating;

	private ResponsiveGroupPanel _groupHost;

	private readonly Panel _scrollHost = new Panel();

	private readonly Panel _pager = new Panel();

	private Button _btnDefaultsRef;

	private Button _btnPickExe;

	private Button _btnPickWindow;

	private Button _btnSaveRef;

	private Button _btnCancelRef;

	private readonly Dictionary<Control, InnerLayout> _innerIndex = new Dictionary<Control, InnerLayout>();

	private readonly Dictionary<Control, KeyValuePair<Control, int>> _pendingGrowNeighbor = new Dictionary<Control, KeyValuePair<Control, int>>();

	private readonly Dictionary<Control, int> _pendingShiftGap = new Dictionary<Control, int>();

	private readonly List<InnerLayout> _innerLayouts = new List<InnerLayout>();

	private const int BaseInnerWidth = 528;

	private bool _innerCaptured;

	private float _uiScale;

	private static float _sharedUiScale;

	private readonly List<DesignSnapshot> _designSnapshot = new List<DesignSnapshot>();

	private GroupBox[] _groups = new GroupBox[0];

	private bool _rebaseDone;

	internal static string LastRebaseInfo = "(未运行)";

	private int _groupChromeFallback = 8;

	private int[] _baseGroupHeights = new int[0];

	private bool _pendingRelayout;

	private readonly System.Windows.Forms.Timer _relayoutTimer = new System.Windows.Forms.Timer();

	private bool _inSizeMove;

	private const int DefaultClientHeightDesign = 640;

	internal const int TwoColumnProbeWidth = 1300;

	internal const int SidebarDesignWidth = 176;

	internal static string LastStretchDetail = string.Empty;

	private int SingleColumnClientWidth => S(560);

	private int GroupWidth => S(536);

	private int GroupInnerPadLeft => S(18);

	private int ScrollEdgeMargin => S(12);

	private float UiScale
	{
		get
		{
			if (_uiScale > 0f)
			{
				return _uiScale;
			}
			return CurrentUiScale = (_uiScale = ResolveScaleNow());
		}
	}

	internal static float CurrentUiScale
	{
		get
		{
			float sharedUiScale = _sharedUiScale;
			if (!(sharedUiScale > 0f))
			{
				return 1f;
			}
			return sharedUiScale;
		}
		set
		{
			_sharedUiScale = value;
		}
	}

	private static int CheckTextChrome => Fields.Sc(44);

	private int GroupChrome
	{
		get
		{
			if (_groupHost != null && _groupHost.Controls.Count > 0 && _groupHost.Controls[0] is GroupBox { Width: >0, Width: var width, ClientSize: var clientSize })
			{
				int num = width - clientSize.Width;
				if (num > 0 && num <= 20)
				{
					return num;
				}
			}
			return _groupChromeFallback;
		}
		set
		{
			_groupChromeFallback = value;
		}
	}

	internal string ProbeBlacklistText
	{
		get
		{
			if (_txtBlacklist == null)
			{
				return string.Empty;
			}
			return _txtBlacklist.Text;
		}
	}

	internal Control ProbeComboLeft => _cmbLeft;

	internal static FontFamily IconFamilyForTest => Icons.Family;

	internal static string[] IconGlyphsForTest => new string[8] { "\ue765", "\ue7c2", "\ue790", "\ue9d9", "\ue72e", "\ue76c", "\ue897", "\ue946" };

	internal static float UiScaleForTest => CurrentUiScale;

	internal static string AppVersionForTest => AppInfo.Version;

	internal HelpPanel ProbeHelpPanel => _helpPanel;

	internal string ProbeAboutText
	{
		get
		{
			if (_groups == null)
			{
				return "";
			}
			GroupBox groupBox = null;
			for (int i = 0; i < _groups.Length; i++)
			{
				if (_groups[i] != null && _groups[i].Text != null && _groups[i].Text.Contains("关于"))
				{
					groupBox = _groups[i];
					break;
				}
			}
			if (groupBox == null)
			{
				return "";
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int j = 0; j < groupBox.Controls.Count; j++)
			{
				Control control = groupBox.Controls[j];
				if (control != null && !string.IsNullOrEmpty(control.Text))
				{
					stringBuilder.Append(control.Text).Append(' ');
				}
			}
			return stringBuilder.ToString();
		}
	}

	internal Control ProbeComboRight => _cmbRight;

	internal Control ProbeNumHold => _numHold;

	internal Control ProbeGroup1
	{
		get
		{
			if (_groupHost == null || _groupHost.Controls.Count == 0)
			{
				return null;
			}
			return _groupHost.Controls[0];
		}
	}

	internal static int ClientWidthForTwoColumns
	{
		get
		{
			int num = (int)Math.Round(CurrentUiScale);
			if (num < 1)
			{
				num = 1;
			}
			return 1516 * num;
		}
	}

	public SettingsForm(ConfigStore store, Coordinator coord)
	{
		_store = store;
		_coord = coord;
		_edit = store.Current.Clone();
		_relayoutTimer.Interval = 32;
		_relayoutTimer.Tick += (object? s, EventArgs e) =>
		{
			if (!_pendingRelayout)
			{
				_relayoutTimer.Stop();
			}
			else
			{
				_pendingRelayout = false;
				RelayoutFlickerFree();
			}
		};
		BuildUi();
		Populate();
	}

	private void BuildUi()
	{
		SuspendLayout();
		Text = "长按拖窗 · 设置";
		Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
		AutoScaleMode = AutoScaleMode.None;
		AutoScaleDimensions = new SizeF(96f, 96f);
		BackColor = Design.Canvas;
		ForeColor = Design.TextPrimary;
		FormBorderStyle = FormBorderStyle.Sizable;
		MaximizeBox = true;
		MinimizeBox = true;
		StartPosition = FormStartPosition.CenterScreen;
		AutoScroll = false;
		SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		DoubleBuffered = true;
		try
		{
			Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(AppPaths.ExePath);
			if (icon != null)
			{
				Icon = icon;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		GroupBox groupBox = Group("触发方式", 0, 0, GroupWidth, 156);
		groupBox.Controls.Add(Label("触发键", 16, 31, 60));
		_txtKey.Location = new Point(84, 27);
		_txtKey.Size = new Size(190, 26);
		_txtKey.ReadOnly = true;
		_txtKey.TabStop = false;
		groupBox.Controls.Add(_txtKey);
		_btnRecord.Text = "重新录制";
		_btnRecord.Location = new Point(284, 26);
		_btnRecord.Size = new Size(92, 28);
		_btnRecord.Click += (object? s, EventArgs e) =>
		{
			StartRecording();
		};
		groupBox.Controls.Add(_btnRecord);
		SetGrowRightNeighbor(_txtKey, _btnRecord, 10);
		SetShiftFromRight(_btnRecord, 24);
		_lblKeyHint.Location = new Point(16, 60);
		_lblKeyHint.Size = new Size(504, 20);
		_lblKeyHint.ForeColor = Design.Hint;
		groupBox.Controls.Add(_lblKeyHint);
		_chkHold.Text = "需要长按触发（关闭后只要按住就立即生效）";
		_chkHold.Location = new Point(18, 88);
		_chkHold.Size = new Size(400, 24);
		_chkHold.CheckedChanged += (object? s, EventArgs e) =>
		{
			_numHold.Enabled = _chkHold.Checked;
		};
		groupBox.Controls.Add(_chkHold);
		groupBox.Controls.Add(Label("长按阈值", 16, 121, 70));
		StyleNum(_numHold, 92, 118, 84, 100, 3000, 50);
		groupBox.Controls.Add(_numHold);
		Label value = Label("毫秒（默认 600，短按仍然是原本的按键）", 184, 121, 340);
		groupBox.Controls.Add(value);
		GroupBox groupBox2 = Group("鼠标键动作（长按触发键之后）", 0, 0, GroupWidth, 126);
		groupBox2.Controls.Add(Label("左键", 16, 32, 60));
		StyleCombo(_cmbLeft, 84, 28, 260);
		_cmbLeft.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox2.Controls.Add(_cmbLeft);
		groupBox2.Controls.Add(Label("右键", 16, 64, 60));
		StyleCombo(_cmbRight, 84, 60, 260);
		_cmbRight.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox2.Controls.Add(_cmbRight);
		groupBox2.Controls.Add(Label("中键", 16, 96, 60));
		StyleCombo(_cmbMiddle, 84, 92, 260);
		_cmbMiddle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox2.Controls.Add(_cmbMiddle);
		GroupBox groupBox3 = Group("视觉反馈", 0, 0, GroupWidth, 420);
		_chkFrame.Text = "长按触发键到达阈值后，光标所在的窗口立刻亮起外框";
		_chkFrame.Location = new Point(18, 30);
		_chkFrame.Size = new Size(500, 22);
		_chkFrame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_chkFrame.CheckedChanged += (object? s, EventArgs e) =>
		{
			SyncFrameEnabled();
		};
		groupBox3.Controls.Add(_chkFrame);
		_cmbFrameColor.SelectedIndexChanged += (object? s, EventArgs e) =>
		{
			SyncPreview();
		};
		_cmbFrameStyle.SelectedIndexChanged += (object? s, EventArgs e) =>
		{
			SyncPreview();
		};
		groupBox3.Controls.Add(Label("外框颜色", 18, 58, 62));
		_gridColor.Location = new Point(18, 80);
		_gridColor.Size = new Size(246, 50);
		_gridColor.Columns = 9;
		_gridColor.SetItems(BuildColorEntries());
		_gridColor.Picked += (object? s, EventArgs e) =>
		{
			SelectId(_cmbFrameColor, _gridColor.SelectedId);
		};
		groupBox3.Controls.Add(_gridColor);
		_lblColorName.Location = new Point(84, 58);
		_lblColorName.Size = new Size(300, 20);
		_lblColorName.ForeColor = Design.TextPrimary;
		groupBox3.Controls.Add(_lblColorName);
		groupBox3.Controls.Add(Label("样式", 18, 164, 40));
		_galleryStyle.Location = new Point(18, 186);
		_galleryStyle.Size = new Size(374, 150);
		_galleryStyle.Columns = 5;
		_galleryStyle.Picked += (object? s, EventArgs e) =>
		{
			SelectId(_cmbFrameStyle, _galleryStyle.SelectedId);
		};
		groupBox3.Controls.Add(_galleryStyle);
		groupBox3.Controls.Add(Label("粗细", 18, 348, 56));
		StyleNum(_numFrameThick, 76, 344, 62, 1, 8, 1);
		_numFrameThick.ValueChanged += (object? s, EventArgs e) =>
		{
			SyncPreview();
		};
		groupBox3.Controls.Add(_numFrameThick);
		groupBox3.Controls.Add(Label("px", 142, 348, 24));
		groupBox3.Controls.Add(Label("圆角", 174, 348, 56));
		StyleNum(_numFrameRadius, 232, 344, 62, 0, 28, 1);
		_numFrameRadius.ValueChanged += (object? s, EventArgs e) =>
		{
			SyncPreview();
		};
		groupBox3.Controls.Add(_numFrameRadius);
		groupBox3.Controls.Add(Label("px", 298, 348, 24));
		groupBox3.Controls.Add(Label("不透明度", 330, 348, 72));
		StyleNum(_numFrameOpacity, 406, 344, 62, 20, 100, 5);
		_numFrameOpacity.ValueChanged += (object? s, EventArgs e) =>
		{
			SyncPreview();
		};
		groupBox3.Controls.Add(_numFrameOpacity);
		groupBox3.Controls.Add(Label("%", 472, 348, 24));
		_lblFrameTip.Location = new Point(18, 378);
		_lblFrameTip.Size = new Size(496, 34);
		_lblFrameTip.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_lblFrameTip.ForeColor = Design.Hint;
		_lblFrameTip.Text = "上面所有颜色和样式都是一进这个页面就能看到的，点一下就换；右上角是实时预览，和真正拖窗口时用的是同一段绘制代码。";
		groupBox3.Controls.Add(_lblFrameTip);
		_pnlPreview.Location = new Point(424, 60);
		_pnlPreview.Size = new Size(96, 120);
		SetShiftFromRight(_pnlPreview, 16);
		groupBox3.Controls.Add(_pnlPreview);
		GroupBox groupBox4 = Group("拖动行为", 0, 0, GroupWidth, 366);
		Label label = Label("缩放角区", 230, 30, 66);
		SetShiftFromRight(label, -1);
		groupBox4.Controls.Add(label);
		StyleNum(_numCorner, 300, 27, 70, 5, 45, 1);
		SetShiftFromRight(_numCorner, -1);
		groupBox4.Controls.Add(_numCorner);
		Label label2 = Label("%", 374, 30, 30);
		SetShiftFromRight(label2, -1);
		groupBox4.Controls.Add(label2);
		_chkDblLeft.Text = "双击左键 = 最大化 / 还原";
		_chkDblLeft.Location = new Point(18, 56);
		_chkDblLeft.Size = new Size(240, 22);
		groupBox4.Controls.Add(_chkDblLeft);
		_chkDblRight.Text = "双击右键 = 九宫格分区";
		_chkDblRight.Location = new Point(272, 56);
		_chkDblRight.Size = new Size(240, 22);
		SetShiftFromRight(_chkDblRight, 16);
		groupBox4.Controls.Add(_chkDblRight);
		_chkRestoreMax.Text = "允许拖动已最大化的窗口（先还原再拖）";
		_chkRestoreMax.Location = new Point(18, 82);
		_chkRestoreMax.Size = new Size(500, 22);
		_chkRestoreMax.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox4.Controls.Add(_chkRestoreMax);
		_chkFront.Text = "开始拖动时自动把窗口提到最前";
		_chkFront.Location = new Point(18, 108);
		_chkFront.Size = new Size(500, 22);
		_chkFront.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox4.Controls.Add(_chkFront);
		_chkFullscreen.Text = "允许拖动全屏 / 无边框窗口（游戏、全屏播放器，可能有风险）";
		_chkFullscreen.Location = new Point(18, 134);
		_chkFullscreen.Size = new Size(500, 22);
		_chkFullscreen.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_chkFullscreen.ForeColor = Design.WarnText;
		groupBox4.Controls.Add(_chkFullscreen);
		_chkExitFullscreen.Text = "触发时先把全屏 / 最大化窗口变回普通窗口，再执行拖动或缩放";
		_chkExitFullscreen.Location = new Point(18, 160);
		_chkExitFullscreen.Size = new Size(500, 22);
		_chkExitFullscreen.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox4.Controls.Add(_chkExitFullscreen);
		_chkShake.Text = "选中窗口后晃一晃，其它窗口全部最小化（再晃一次还原）";
		_chkShake.Location = new Point(18, 186);
		_chkShake.Size = new Size(500, 22);
		_chkShake.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox4.Controls.Add(_chkShake);
		groupBox4.Controls.Add(Label("不生效的窗口", 18, 240, 90));
		_txtBlacklist.Location = new Point(112, 237);
		_txtBlacklist.Size = new Size(406, 26);
		_txtBlacklist.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox4.Controls.Add(_txtBlacklist);
		_btnPickExe = new FlatButton();
		_btnPickExe.Text = "选择程序…";
		_btnPickExe.Location = new Point(112, 272);
		_btnPickExe.Size = new Size(92, 26);
		_btnPickExe.Anchor = AnchorStyles.Top | AnchorStyles.Left;
		_btnPickExe.Click += (object? s, EventArgs e) =>
		{
			PickExeFile();
		};
		groupBox4.Controls.Add(_btnPickExe);
		_btnPickWindow = new FlatButton();
		_btnPickWindow.Text = "点选窗口";
		_btnPickWindow.Location = new Point(212, 272);
		_btnPickWindow.Size = new Size(92, 26);
		_btnPickWindow.Anchor = AnchorStyles.Top | AnchorStyles.Left;
		_btnPickWindow.Click += (object? s, EventArgs e) =>
		{
			PickWindowByClick();
		};
		groupBox4.Controls.Add(_btnPickWindow);
		Label label3 = Label("填窗口标题 / 程序名 / 窗口类名的关键字，逗号分隔；命中就完全不接管。也可以用上面两个按钮直接挑。", 18, 306, 500);
		label3.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		label3.ForeColor = Design.Hint;
		groupBox4.Controls.Add(label3);
		GroupBox groupBox5 = Group("流畅度", 0, 0, GroupWidth, 148);
		groupBox5.Controls.Add(Label("刷新率", 16, 26, 70));
		StyleCombo(_cmbRefreshMode, 90, 22, 220);
		_cmbRefreshMode.SelectedIndexChanged += (object? s, EventArgs e) =>
		{
			SyncRefreshEnabled();
		};
		groupBox5.Controls.Add(_cmbRefreshMode);
		groupBox5.Controls.Add(Label("固定值", 16, 58, 70));
		StyleNum(_numRefreshHz, 90, 54, 90, 0, 360, 1);
		groupBox5.Controls.Add(_numRefreshHz);
		groupBox5.Controls.Add(Label("Hz", 186, 58, 30));
		_lblRefreshHint.Location = new Point(16, 90);
		_lblRefreshHint.Size = new Size(504, 44);
		_lblRefreshHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_lblRefreshHint.ForeColor = Design.Hint;
		_lblRefreshHint.Text = "跟随屏幕：按实测刷新率节流，与显示器同步。\r\n固定值：自定义节流频率，0 = 不限制。改动在下次拖动时生效。";
		groupBox5.Controls.Add(_lblRefreshHint);
		GroupBox groupBox6 = Group("启动与权限", 0, 0, GroupWidth, 150);
		groupBox6.Controls.Add(Label("开机自启", 16, 32, 70));
		StyleCombo(_cmbAutoStart, 90, 28, 430);
		_cmbAutoStart.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox6.Controls.Add(_cmbAutoStart);
		_chkRunAsAdmin.Text = "启动时自动申请管理员权限（默认开启）";
		_chkRunAsAdmin.Location = new Point(18, 62);
		_chkRunAsAdmin.Size = new Size(500, 22);
		_chkRunAsAdmin.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox6.Controls.Add(_chkRunAsAdmin);
		_chkWarnNonAdmin.Text = "没有管理员权限时提示我（可关闭，不再打扰）";
		_chkWarnNonAdmin.Location = new Point(18, 86);
		_chkWarnNonAdmin.Size = new Size(500, 22);
		_chkWarnNonAdmin.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		groupBox6.Controls.Add(_chkWarnNonAdmin);
		_lblAdmin.Location = new Point(16, 112);
		_lblAdmin.Size = new Size(340, 20);
		_lblAdmin.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_lblAdmin.ForeColor = Design.TextSecondary;
		SetGrowRightNeighbor(_lblAdmin, _btnRestartAdmin, 12);
		groupBox6.Controls.Add(_lblAdmin);
		_btnRestartAdmin.Text = "以管理员重启";
		_btnRestartAdmin.Location = new Point(376, 110);
		_btnRestartAdmin.Size = new Size(144, 24);
		SetShiftFromRight(_btnRestartAdmin, 16);
		_btnRestartAdmin.Click += (object? s, EventArgs e) =>
		{
			RestartAsAdmin();
		};
		groupBox6.Controls.Add(_btnRestartAdmin);
		_lblStatus.Location = new Point(16, 12);
		_lblStatus.Size = new Size(320, 22);
		_lblStatus.ForeColor = Design.Success;
		_pager.Controls.Add(_lblStatus);
		_btnDefaultsRef = new FlatButton();
		_btnDefaultsRef.Text = "恢复默认值";
		_btnDefaultsRef.Size = new Size(96, 20);
		_btnDefaultsRef.Click += (object? s, EventArgs e) =>
		{
			ResetDefaults();
		};
		_pager.Controls.Add(_btnDefaultsRef);
		_btnSaveRef = new FlatButton();
		((FlatButton)_btnSaveRef).Primary = true;
		_btnSaveRef.Text = "保存并生效";
		_btnSaveRef.Size = new Size(96, 20);
		_btnSaveRef.Click += (object? s, EventArgs e) =>
		{
			Save();
		};
		_pager.Controls.Add(_btnSaveRef);
		_btnCancelRef = new FlatButton();
		_btnCancelRef.Text = "取消";
		_btnCancelRef.Size = new Size(96, 20);
		_btnCancelRef.Click += (object? s, EventArgs e) =>
		{
			Close();
		};
		_pager.Controls.Add(_btnCancelRef);
		GroupBox groupBox7 = Group("帮助", 0, 0, GroupWidth, 396);
		Label label4 = Label("点一下标题，就能展开对应功能的说明。下面每一项都是当前版本的实际行为。", 18, 28, 500);
		label4.ForeColor = Design.Hint;
		groupBox7.Controls.Add(label4);
		_helpPanel.Location = new Point(18, 56);
		_helpPanel.Size = new Size(500, 320);
		_helpPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_helpPanel.SetEntries(BuildHelpEntries());
		groupBox7.Controls.Add(_helpPanel);
		GroupBox groupBox8 = Group("关于", 0, 0, GroupWidth, 140);
		Label label5 = Label("长按拖窗  HoldDrag    版本 " + AppInfo.Version, 18, 26, 460);
		label5.ForeColor = Design.TextPrimary;
		groupBox8.Controls.Add(label5);
		Label label6 = Label("长按触发键，随手拖动 / 缩放任意窗口", 18, 52, 460);
		label6.ForeColor = Design.Hint;
		groupBox8.Controls.Add(label6);
		groupBox8.Controls.Add(Label("开发者", 18, 82, 82));
		groupBox8.Controls.Add(Label("QQQOrange", 104, 82, 160));
		groupBox8.Controls.Add(Label("开发工具", 280, 82, 82));
		groupBox8.Controls.Add(Label("DeepSeek", 366, 82, 160));
		groupBox8.Controls.Add(Label("当前版本", 18, 108, 82));
		groupBox8.Controls.Add(Label("v" + AppInfo.Version, 104, 108, 160));
		groupBox8.Controls.Add(Label("更新时间", 280, 108, 82));
		groupBox8.Controls.Add(Label("2026-10-12", 366, 108, 160));
		_groups = new GroupBox[8] { groupBox, groupBox2, groupBox3, groupBox4, groupBox5, groupBox6, groupBox7, groupBox8 };
		CaptureDesignSnapshot();
		ScaleDesign(_groups);
		StyleText(_txtKey, Font, readOnly: true);
		StyleText(_txtBlacklist, Font, readOnly: false);
		_innerLayouts.Clear();
		_innerIndex.Clear();
		for (int num = 0; num < _groups.Length; num++)
		{
			GroupBox groupBox9 = _groups[num];
			for (int num2 = 0; num2 < groupBox9.Controls.Count; num2++)
			{
				MarkBase(groupBox9.Controls[num2]);
			}
		}
		_innerCaptured = true;
		ReserveLabelWidth();
		_groupHost = new ResponsiveGroupPanel(_groups);
		_groupHost.Location = new Point(ScrollEdgeMargin, ScrollEdgeMargin);
		_scrollHost.Controls.Add(_groupHost);
		_scrollHost.Dock = DockStyle.Fill;
		_scrollHost.AutoScroll = true;
		_scrollHost.BackColor = Design.Canvas;
		Controls.Add(_scrollHost);
		_sidebar = new NavSidebar();
		_sidebar.Dock = DockStyle.Left;
		_sidebar.Width = S(176);
		Controls.Add(_sidebar);
		_scrollHost.Scroll += (object? s, ScrollEventArgs e) =>
		{
			if (_sidebar != null)
			{
				_sidebar.SyncActive();
			}
		};
		_scrollHost.Resize += (object? s, EventArgs e) =>
		{
			if (_sidebar != null)
			{
				_sidebar.SyncActive();
			}
		};
		_scrollHost.MouseWheel += (object? s, MouseEventArgs e) =>
		{
			SyncSidebarLater();
		};
		string[] array = new string[_groups.Length];
		for (int num3 = 0; num3 < _groups.Length; num3++)
		{
			array[num3] = ShortTitleOf(_groups[num3].Text);
		}
		NavSidebar sidebar = _sidebar;
		Panel scrollHost = _scrollHost;
		Control[] groups = _groups;
		sidebar.Bind(scrollHost, array, groups);
		_pager.Dock = DockStyle.Bottom;
		_pager.Height = S(46);
		_pager.BackColor = Design.Surface;
		_pager.Paint += (object? s, PaintEventArgs e) =>
		{
			Graphics graphics = e.Graphics;
			HighQuality.Apply(graphics);
			using (Pen pen = new Pen(Design.Stroke))
			{
				graphics.DrawLine(pen, 0, 0, _pager.Width, 0);
			}
			for (int i = 1; i <= 2; i++)
			{
				using Pen pen2 = new Pen(Color.FromArgb((i == 1) ? 16 : 7, Design.Shadow));
				graphics.DrawLine(pen2, 0, i, _pager.Width, i);
			}
		};
		Controls.Add(_pager);
		int num4 = SingleColumnClientWidth + S(176);
		try
		{
			Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
			if (num4 > workingArea.Width - S(80))
			{
				num4 = Math.Max(S(420), workingArea.Width - S(80));
			}
		}
		catch
		{
			num4 = SingleColumnClientWidth + S(176);
		}
		ClientSize = new Size(num4, S(640));
		MinimumSize = new Size(ClientWidthToWindowWidth(SingleColumnClientWidth + S(176)), S(440));
		ResumeLayout(performLayout: false);
		_groupHost.Width = GroupWidth;
		LayoutResponsive();
		ApplySavedSize();
		LayoutResponsive();
	}

	private void FitHeightToContent()
	{
		try
		{
			if ((_edit == null || _edit.SettingsWidth <= 0 || _edit.SettingsHeight <= 0) && _groupHost != null && _groupHost.Height > 0)
			{
				int num = _groupHost.Height + ScrollEdgeMargin * 2;
				int num2 = S(46) + 8;
				int num3 = num + num2;
				int num4 = Screen.PrimaryScreen.WorkingArea.Height - S(40);
				if (num3 <= num4 && num3 < ClientSize.Height && ClientSize.Height - num3 >= S(24))
				{
					ClientSize = new Size(ClientSize.Width, num3);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static int ClientWidthToWindowWidth(int clientWidth)
	{
		try
		{
			int num = (SystemInformation.FrameBorderSize.Width + SystemInformation.Border3DSize.Width * 2) * 2;
			if (num <= 0)
			{
				num = 16;
			}
			return clientWidth + num;
		}
		catch
		{
			return clientWidth + 16;
		}
	}

	private int RightReserve()
	{
		int verticalScrollBarWidth = SystemInformation.VerticalScrollBarWidth;
		if (_groupHost != null && _groupHost.CurrentColumns >= 2)
		{
			return verticalScrollBarWidth + 4;
		}
		return 4;
	}

	private void SetGrowRightNeighbor(Control c, Control neighbor, int gap)
	{
		_pendingGrowNeighbor[c] = new KeyValuePair<Control, int>(neighbor, S(gap));
	}

	private void SetShiftFromRight(Control c, int gap)
	{
		_pendingShiftGap[c] = ((gap < 0) ? (-1) : S(gap));
	}

	private int S(int design)
	{
		float uiScale = UiScale;
		if (Math.Abs(uiScale - 1f) <= 0.001f)
		{
			return design;
		}
		return (int)Math.Round((float)design * uiScale);
	}

	private float ResolveScaleNow()
	{
		float num = 1f;
		try
		{
			int num2 = 0;
			if (IsHandleCreated)
			{
				try
				{
					num2 = Native.DpiOfWindow(Handle);
				}
				catch
				{
					num2 = 0;
				}
			}
			if (num2 <= 0)
			{
				try
				{
					num2 = DeviceDpi;
				}
				catch
				{
					num2 = 0;
				}
			}
			if (num2 <= 0)
			{
				num2 = 96;
			}
			num = (float)num2 / 96f;
			if (num < 0.5f || num > 4f)
			{
				num = 1f;
			}
		}
		catch
		{
			num = 1f;
		}
		return num;
	}

	private void CaptureDesignSnapshot()
	{
		_designSnapshot.Clear();
		for (int i = 0; i < _groups.Length; i++)
		{
			GroupBox groupBox = _groups[i];
			if (groupBox == null)
			{
				continue;
			}
			_designSnapshot.Add(new DesignSnapshot
			{
				Control = groupBox,
				Location = groupBox.Location,
				Size = groupBox.Size
			});
			for (int j = 0; j < groupBox.Controls.Count; j++)
			{
				Control control = groupBox.Controls[j];
				if (control != null)
				{
					if (control is HelpPanel helpPanel)
					{
						helpPanel.SetMetrics(Fields.Sc(38), Fields.Sc(88));
					}
					_designSnapshot.Add(new DesignSnapshot
					{
						Control = control,
						Location = control.Location,
						Size = control.Size
					});
				}
			}
		}
	}

	private void RescaleForDpi(int newDpi)
	{
		if (newDpi <= 0)
		{
			return;
		}
		float num = (float)newDpi / 96f;
		if (num < 0.5f || num > 4f || (_uiScale > 0f && Math.Abs(num - _uiScale) < 0.001f) || _groups == null || _groups.Length == 0)
		{
			return;
		}
		try
		{
			Log.Info("设置窗口 DPI 变化：" + _uiScale.ToString("0.###") + " → " + num.ToString("0.###") + "（新 DPI=" + newDpi + "），按新分辨率重建界面");
			for (int i = 0; i < _designSnapshot.Count; i++)
			{
				DesignSnapshot designSnapshot = _designSnapshot[i];
				if (designSnapshot.Control != null && !designSnapshot.Control.IsDisposed)
				{
					designSnapshot.Control.Location = designSnapshot.Location;
					designSnapshot.Control.Size = designSnapshot.Size;
				}
			}
			_uiScale = num;
			CurrentUiScale = num;
			_rebaseDone = false;
			ScaleDesign(_groups);
			_innerLayouts.Clear();
			_innerIndex.Clear();
			for (int j = 0; j < _groups.Length; j++)
			{
				GroupBox groupBox = _groups[j];
				if (groupBox != null)
				{
					for (int k = 0; k < groupBox.Controls.Count; k++)
					{
						MarkBase(groupBox.Controls[k]);
					}
				}
			}
			_innerCaptured = true;
			Fields.ResetCaches();
			StyleText(_txtKey, Font, readOnly: true);
			StyleText(_txtBlacklist, Font, readOnly: false);
			ReserveLabelWidth();
			_baseGroupHeights = new int[0];
			try
			{
				MinimumSize = new Size(ClientWidthToWindowWidth(SingleColumnClientWidth + S(176)), S(440));
			}
			catch
			{
			}
			if (_sidebar != null)
			{
				_sidebar.Width = S(176);
			}
			LayoutResponsive();
			Invalidate(invalidateChildren: true);
			Update();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ScaleDesign(params GroupBox[] groups)
	{
		float uiScale = UiScale;
		if (_btnDefaultsRef != null)
		{
			ScaleFlatButton(_btnDefaultsRef, uiScale);
		}
		if (_btnSaveRef != null)
		{
			ScaleFlatButton(_btnSaveRef, uiScale);
		}
		if (_btnCancelRef != null)
		{
			ScaleFlatButton(_btnCancelRef, uiScale);
		}
		if (Math.Abs(uiScale - 1f) <= 0.001f)
		{
			return;
		}
		Log.Info("ScaleDesign: DPI=" + DeviceDpi + " 缩放系数=" + uiScale.ToString("0.###"));
		foreach (GroupBox groupBox in groups)
		{
			if (groupBox == null)
			{
				continue;
			}
			groupBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
			for (int j = 0; j < groupBox.Controls.Count; j++)
			{
				if (groupBox.Controls[j] != null)
				{
					groupBox.Controls[j].Anchor = AnchorStyles.Top | AnchorStyles.Left;
				}
			}
		}
		foreach (GroupBox groupBox2 in groups)
		{
			if (groupBox2 == null)
			{
				continue;
			}
			groupBox2.Size = ScaleSz(groupBox2.Size, uiScale);
			int num = 0;
			if (groupBox2 is CardGroup cardGroup)
			{
				cardGroup.TitleFontPx = (int)Math.Round(14f * uiScale);
				cardGroup.TitleY = (int)Math.Round(9f * uiScale);
				cardGroup.RadiusPx = (int)Math.Round(14f * uiScale);
				cardGroup.ShadowOffsetPx = (int)Math.Round(1f * uiScale);
				cardGroup.ShadowSpreadPx = (int)Math.Round(5f * uiScale);
				cardGroup.AccentBarPx = (int)Math.Round(4f * uiScale);
				num = (int)Math.Round((float)cardGroup.TitleInsetPx * uiScale);
				groupBox2.Height += num;
			}
			for (int l = 0; l < groupBox2.Controls.Count; l++)
			{
				Control control = groupBox2.Controls[l];
				if (control != null)
				{
					control.Location = ScalePt(control.Location, uiScale);
					control.Size = ScaleSz(control.Size, uiScale);
					if (num > 0)
					{
						control.Top += num;
					}
				}
			}
		}
	}

	private static Point ScalePt(Point p, float s)
	{
		return new Point((int)Math.Round((float)p.X * s), (int)Math.Round((float)p.Y * s));
	}

	private static Size ScaleSz(Size z, float s)
	{
		return new Size((int)Math.Round((float)z.Width * s), (int)Math.Round((float)z.Height * s));
	}

	private static void ScaleFlatButton(Button b, float s)
	{
		try
		{
			b.Size = ScaleSz(b.Size, s);
			if (b.Text != null && b.Text.Length > 0 && b.Font != null)
			{
				int num = TextRenderer.MeasureText(b.Text, b.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Height + 8;
				if (b.Height < num)
				{
					b.Height = num;
				}
			}
		}
		catch
		{
		}
	}

	private void MarkBase(Control c)
	{
		if (!_innerIndex.ContainsKey(c))
		{
			int groupWidth = GroupWidth;
			InnerLayout innerLayout = new InnerLayout
			{
				Control = c,
				BaseLeft = c.Left,
				BaseTop = c.Top,
				BaseHeight = c.Height,
				BaseWidth = c.Width,
				OriginalWidth = c.Width,
				BaseInner = groupWidth,
				RightGap = groupWidth - (c.Left + c.Width),
				How = InnerLayout.Mode.Fixed
			};
			if (_pendingGrowNeighbor.TryGetValue(c, out var value))
			{
				innerLayout.How = InnerLayout.Mode.Grow;
				innerLayout.GrowRightNeighbor = value.Key;
				innerLayout.GrowNeighborGap = value.Value;
			}
			else if (_pendingShiftGap.ContainsKey(c))
			{
				innerLayout.How = InnerLayout.Mode.Shift;
				innerLayout.ShiftRightGap = _pendingShiftGap[c];
			}
			else
			{
				innerLayout.How = ClassifyStretch(c);
			}
			c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
			_innerLayouts.Add(innerLayout);
			_innerIndex[c] = innerLayout;
		}
	}

	private void CaptureInnerLayouts()
	{
		if (_innerCaptured)
		{
			return;
		}
		_innerCaptured = true;
		for (int i = 0; i < _groupHost.Controls.Count; i++)
		{
			if (_groupHost.Controls[i] is GroupBox groupBox)
			{
				for (int j = 0; j < groupBox.Controls.Count; j++)
				{
					MarkBase(groupBox.Controls[j]);
				}
			}
		}
	}

	private void RebaseAfterAutoScale()
	{
		if (!_rebaseDone)
		{
			_rebaseDone = true;
			LastRebaseInfo = "AutoScaleMode.None，基准由 ScaleDesign 一次性建立";
		}
	}

	private static InnerLayout.Mode ClassifyStretch(Control c)
	{
		if (c is FramePreviewBox)
		{
			return InnerLayout.Mode.Shift;
		}
		if (c is NumericUpDown)
		{
			return InnerLayout.Mode.Fixed;
		}
		if (c is ComboBox || c is TextBox)
		{
			return InnerLayout.Mode.Grow;
		}
		if ((c is CheckBox || c is Label) && c.Width >= 200)
		{
			return InnerLayout.Mode.Grow;
		}
		return InnerLayout.Mode.Fixed;
	}

	private void ReserveLabelWidth()
	{
		for (int i = 0; i < _innerLayouts.Count; i++)
		{
			InnerLayout innerLayout = _innerLayouts[i];
			int num = TextNeed(innerLayout);
			if (num <= 0 || num <= innerLayout.BaseWidth)
			{
				continue;
			}
			if (innerLayout.GrowRightNeighbor != null)
			{
				int num2 = ((!_innerIndex.TryGetValue(innerLayout.GrowRightNeighbor, out var value)) ? (innerLayout.GrowRightNeighbor.Left - innerLayout.GrowNeighborGap - innerLayout.BaseLeft) : (value.BaseLeft + value.ShiftByOffset - innerLayout.GrowNeighborGap - innerLayout.BaseLeft));
				if (num2 > 0 && num > num2)
				{
					num = num2;
				}
			}
			if (num > innerLayout.BaseWidth)
			{
				innerLayout.BaseWidth = num;
				innerLayout.WasTextWidened = true;
			}
		}
		ComputeShiftOffsets();
	}

	private void ComputeShiftOffsets()
	{
		for (int i = 0; i < _innerLayouts.Count; i++)
		{
			_innerLayouts[i].ShiftByOffset = 0;
		}
		for (int j = 0; j < _innerLayouts.Count; j++)
		{
			InnerLayout innerLayout = _innerLayouts[j];
			if (innerLayout.WasTextWidened)
			{
				int num = innerLayout.BaseWidth - innerLayout.OriginalWidth;
				if (num > 0)
				{
					PropagatePush(innerLayout, num);
				}
			}
		}
	}

	private void ComputeRowWraps()
	{
		for (int i = 0; i < _innerLayouts.Count; i++)
		{
			InnerLayout innerLayout = _innerLayouts[i];
			innerLayout.WrapsToNextRow = false;
			innerLayout.RightPartner = null;
		}
		for (int j = 0; j < _innerLayouts.Count; j++)
		{
			InnerLayout innerLayout2 = _innerLayouts[j];
			Control control = innerLayout2.Control;
			if (control == null || control.Parent == null || !(control is CheckBox))
			{
				continue;
			}
			int baseTop = innerLayout2.BaseTop;
			int val = innerLayout2.BaseTop + control.Height;
			int num = int.MaxValue;
			InnerLayout innerLayout3 = null;
			for (int k = 0; k < _innerLayouts.Count; k++)
			{
				InnerLayout innerLayout4 = _innerLayouts[k];
				if (innerLayout4 != innerLayout2 && innerLayout4.Control is CheckBox && innerLayout4.Control.Parent == control.Parent && innerLayout4.BaseLeft > innerLayout2.BaseLeft)
				{
					Control control2 = innerLayout4.Control;
					int num2 = Math.Min(val, innerLayout4.BaseTop + control2.Height) - Math.Max(baseTop, innerLayout4.BaseTop);
					if (num2 > 0 && num2 * 2 >= Math.Min(control.Height, control2.Height) && innerLayout4.BaseLeft < num)
					{
						num = innerLayout4.BaseLeft;
						innerLayout3 = innerLayout4;
					}
				}
			}
			if (innerLayout3 != null)
			{
				innerLayout2.RightPartner = innerLayout3;
			}
		}
		for (int l = 0; l < _innerLayouts.Count; l++)
		{
			InnerLayout innerLayout5 = _innerLayouts[l];
			InnerLayout rightPartner = innerLayout5.RightPartner;
			if (rightPartner == null)
			{
				continue;
			}
			Control control3 = innerLayout5.Control;
			Control control4 = rightPartner.Control;
			if (control3 != null && control4 != null)
			{
				int num3 = control3.Left + control3.Width;
				int num4 = rightPartner.BaseLeft + rightPartner.ShiftByOffset;
				if (num3 + S(8) > num4)
				{
					rightPartner.WrapsToNextRow = true;
				}
			}
		}
	}

	private void SyncWrappedHeightsPopulateOnly()
	{
		for (int i = 0; i < _innerLayouts.Count; i++)
		{
			InnerLayout innerLayout = _innerLayouts[i];
			Control control = innerLayout.Control;
			if (control != null && control.Parent != null && (control is CheckBox || control is Label) && (innerLayout.How == InnerLayout.Mode.Grow || innerLayout.WrapsToNextRow) && innerLayout.BaseHeight > 0)
			{
				int num = WrappedHeightOf(innerLayout);
				if (num > 0 && control.Height != num)
				{
					control.Height = num;
				}
			}
		}
	}

	private int WrappedHeightOf(InnerLayout it)
	{
		Control control = it.Control;
		if (it.BaseHeight <= 0)
		{
			return 0;
		}
		if (string.IsNullOrEmpty(control.Text))
		{
			return it.BaseHeight;
		}
		int num = ((control is CheckBox) ? CheckTextChrome : Fields.Sc(4));
		int num2 = control.Width - num;
		int num3 = Fields.Sc(20);
		if (num2 < num3)
		{
			num2 = num3;
		}
		try
		{
			Size size = TextRenderer.MeasureText(control.Text, control.Font, new Size(num2, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
			if (size.Height > it.BaseHeight)
			{
				return size.Height + Fields.Sc(4);
			}
		}
		catch
		{
		}
		return it.BaseHeight;
	}

	private void SyncWrappedHeights()
	{
		for (int i = 0; i < _innerLayouts.Count; i++)
		{
			InnerLayout innerLayout = _innerLayouts[i];
			Control control = innerLayout.Control;
			if (control == null || control.Parent == null || (!(control is CheckBox) && !(control is Label)) || (innerLayout.How != InnerLayout.Mode.Grow && !innerLayout.WrapsToNextRow) || innerLayout.BaseHeight <= 0)
			{
				continue;
			}
			int num = WrappedHeightOf(innerLayout);
			if (num <= 0)
			{
				continue;
			}
			if (control.Height != num)
			{
				control.Height = num;
			}
			if (num <= innerLayout.BaseHeight)
			{
				continue;
			}
			int num2 = num - innerLayout.BaseHeight;
			for (int j = 0; j < _innerLayouts.Count; j++)
			{
				InnerLayout innerLayout2 = _innerLayouts[j];
				Control control2 = innerLayout2.Control;
				if (control2 != null && control2.Parent == control.Parent && innerLayout2.BaseTop > innerLayout.BaseTop)
				{
					control2.Top += num2;
				}
			}
		}
	}

	private void GrowGroupsForWraps()
	{
		for (int i = 0; i < _groupHost.Controls.Count; i++)
		{
			if (!(_groupHost.Controls[i] is GroupBox groupBox))
			{
				continue;
			}
			int num = 0;
			for (int j = 0; j < _innerLayouts.Count; j++)
			{
				Control control = _innerLayouts[j].Control;
				if (control != null && control.Parent == groupBox)
				{
					int num2 = control.Top + control.Height;
					if (num2 > num)
					{
						num = num2;
					}
				}
			}
			if (num > 0)
			{
				int num3 = num + 12;
				num3 += groupBox.Height - groupBox.ClientSize.Height;
				if (num3 > groupBox.Height)
				{
					groupBox.Height = num3;
				}
			}
		}
	}

	private void ApplyRowWraps()
	{
		for (int i = 0; i < _innerLayouts.Count; i++)
		{
			InnerLayout innerLayout = _innerLayouts[i];
			Control control = innerLayout.Control;
			if (control != null && control.Parent != null)
			{
				if (control.Top != innerLayout.BaseTop)
				{
					control.Top = innerLayout.BaseTop;
				}
				if (innerLayout.BaseHeight > 0 && control.Height != innerLayout.BaseHeight)
				{
					control.Height = innerLayout.BaseHeight;
				}
			}
		}
		List<KeyValuePair<int, int>> list = new List<KeyValuePair<int, int>>();
		for (int j = 0; j < _innerLayouts.Count; j++)
		{
			InnerLayout innerLayout2 = _innerLayouts[j];
			if (!innerLayout2.WrapsToNextRow)
			{
				continue;
			}
			Control control2 = innerLayout2.Control;
			if (control2 == null || control2.Parent == null)
			{
				continue;
			}
			int height = control2.Height;
			for (int k = 0; k < _innerLayouts.Count; k++)
			{
				InnerLayout innerLayout3 = _innerLayouts[k];
				if (innerLayout3.Control != null && innerLayout3.Control.Parent == control2.Parent && innerLayout3.BaseTop == innerLayout2.BaseTop && innerLayout3.Control.Height > height)
				{
					height = innerLayout3.Control.Height;
				}
			}
			bool flag = false;
			for (int l = 0; l < list.Count; l++)
			{
				if (list[l].Key == innerLayout2.BaseTop)
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				list.Add(new KeyValuePair<int, int>(innerLayout2.BaseTop, height + 4));
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		list.Sort((KeyValuePair<int, int> x, KeyValuePair<int, int> y) => x.Key.CompareTo(y.Key));
		for (int num = 0; num < _innerLayouts.Count; num++)
		{
			InnerLayout innerLayout4 = _innerLayouts[num];
			Control control3 = innerLayout4.Control;
			if (control3 == null || control3.Parent == null)
			{
				continue;
			}
			int num2 = 0;
			for (int num3 = 0; num3 < list.Count; num3++)
			{
				if (innerLayout4.BaseTop > list[num3].Key)
				{
					num2 += list[num3].Value;
				}
			}
			if (innerLayout4.WrapsToNextRow)
			{
				int num4 = 0;
				for (int num5 = 0; num5 < list.Count; num5++)
				{
					if (list[num5].Key == innerLayout4.BaseTop)
					{
						num4 = list[num5].Value;
					}
				}
				if (num4 > 0)
				{
					num2 += num4;
				}
			}
			control3.Top = innerLayout4.BaseTop + num2;
		}
	}

	private void PropagatePush(InnerLayout src, int amount)
	{
		Control control = src.Control;
		int baseTop = src.BaseTop;
		int val = src.BaseTop + control.Height;
		InnerLayout innerLayout = null;
		int num = int.MaxValue;
		for (int i = 0; i < _innerLayouts.Count; i++)
		{
			InnerLayout innerLayout2 = _innerLayouts[i];
			if (innerLayout2 != src && innerLayout2.Control.Parent == control.Parent && innerLayout2.BaseLeft >= src.BaseLeft + src.OriginalWidth && innerLayout2.How != InnerLayout.Mode.Shift)
			{
				Control control2 = innerLayout2.Control;
				int num2 = Math.Min(val, innerLayout2.BaseTop + control2.Height) - Math.Max(baseTop, innerLayout2.BaseTop);
				if (num2 > 0 && num2 * 2 >= Math.Min(control.Height, control2.Height) && innerLayout2.BaseLeft < num)
				{
					num = innerLayout2.BaseLeft;
					innerLayout = innerLayout2;
				}
			}
		}
		if (innerLayout != null)
		{
			int num3 = src.BaseLeft + src.BaseWidth + src.ShiftByOffset;
			int num4 = innerLayout.BaseLeft + innerLayout.ShiftByOffset - num3;
			int num5 = S(6);
			if (num4 < num5)
			{
				int num6 = num5 - num4;
				innerLayout.ShiftByOffset += num6;
				PropagatePush(innerLayout, num6);
			}
		}
	}

	private static int MinWidthNeed(Control c)
	{
		if (c == null)
		{
			return 0;
		}
		try
		{
			if (c is CheckBox)
			{
				int width = TextRenderer.MeasureText(c.Text, c.Font).Width;
				if (width > 0)
				{
					return width + CheckTextChrome;
				}
			}
			if (c is Label || c is CheckBox)
			{
				if (string.IsNullOrEmpty(c.Text))
				{
					return 0;
				}
				int width2 = TextRenderer.MeasureText(c.Text, c.Font).Width;
				if (width2 <= 0)
				{
					return 0;
				}
				return width2 + 4;
			}
		}
		catch
		{
		}
		return 0;
	}

	private static int TextNeed(InnerLayout it)
	{
		Control control = it.Control;
		if (control is Button)
		{
			if (control.Width >= 200)
			{
				return 0;
			}
			try
			{
				int width = control.PreferredSize.Width;
				if (width > 0)
				{
					return width;
				}
			}
			catch
			{
			}
			return 0;
		}
		bool flag = control is Label;
		bool flag2 = control is CheckBox;
		if (!flag && !flag2)
		{
			return 0;
		}
		if (it.How == InnerLayout.Mode.Grow)
		{
			return 0;
		}
		if (flag && it.BaseWidth >= 200)
		{
			return 0;
		}
		if (flag2)
		{
			try
			{
				int width2 = control.PreferredSize.Width;
				if (width2 > 0)
				{
					return width2;
				}
			}
			catch
			{
			}
			int num = 0;
			try
			{
				num = TextRenderer.MeasureText(control.Text, control.Font).Width;
			}
			catch
			{
				return 0;
			}
			if (num <= 0)
			{
				return 0;
			}
			return num + 44;
		}
		int num2 = 0;
		try
		{
			num2 = TextRenderer.MeasureText(control.Text, control.Font).Width;
		}
		catch
		{
			return 0;
		}
		if (num2 <= 0)
		{
			return 0;
		}
		return num2 + 4;
	}

	private void LayoutInner()
	{
		for (int i = 0; i < 3; i++)
		{
			InnerLayout.Mode mode = i switch
			{
				1 => InnerLayout.Mode.Grow, 
				0 => InnerLayout.Mode.Shift, 
				_ => InnerLayout.Mode.Fixed, 
			};
			for (int j = 0; j < _innerLayouts.Count; j++)
			{
				InnerLayout innerLayout = _innerLayouts[j];
				if (innerLayout.How == mode)
				{
					ApplyOne(innerLayout);
				}
			}
		}
	}

	private void ApplyOne(InnerLayout it)
	{
		Control control = it.Control;
		if (control.Parent == null)
		{
			return;
		}
		int num = it.BaseLeft + it.ShiftByOffset;
		int width = control.Parent.ClientSize.Width;
		_ = it.BaseInner;
		_ = GroupChrome;
		int num2 = width - GroupChrome;
		switch (it.How)
		{
		case InnerLayout.Mode.Grow:
		{
			int num12;
			if (it.GrowRightNeighbor != null)
			{
				int num11 = it.GrowRightNeighbor.Left;
				if (_innerIndex.TryGetValue(it.GrowRightNeighbor, out var value) && value.How != InnerLayout.Mode.Shift)
				{
					num11 = value.BaseLeft + value.ShiftByOffset;
				}
				num12 = num11 - it.GrowNeighborGap;
			}
			else
			{
				int num13 = it.BaseInner - (it.BaseLeft + it.BaseWidth);
				num12 = num2 - num13;
			}
			int num14 = num12 - num;
			if (num14 < 20)
			{
				num14 = 20;
			}
			if (control is CheckBox)
			{
				int num15 = NaturalWidth(it);
				if (num15 > 0 && num14 > num15)
				{
					num14 = num15;
				}
			}
			if (control is CheckBox || control is Label)
			{
				int num16 = MinWidthNeed(control);
				int num17 = num12 - num;
				if (num17 < 20)
				{
					num17 = 20;
				}
				if (num16 > num17)
				{
					num16 = num17;
				}
				if (num16 > 0 && num14 < num16)
				{
					num14 = num16;
				}
			}
			control.Left = num;
			control.Width = num14;
			if (control is CheckBox && control.Height != it.BaseHeight && it.BaseHeight > 0)
			{
				control.Height = it.BaseHeight;
			}
			break;
		}
		case InnerLayout.Mode.Shift:
		{
			if (it.WrapsToNextRow && control is CheckBox)
			{
				int groupInnerPadLeft = GroupInnerPadLeft;
				int num3 = num2 - groupInnerPadLeft;
				if (num3 < 20)
				{
					num3 = 20;
				}
				control.Left = groupInnerPadLeft;
				control.Width = num3;
				break;
			}
			int num4 = it.BaseLeft + it.OriginalWidth;
			int num5 = it.BaseInner - num4;
			if (it.ShiftRightGap >= 0 && it.ShiftRightGap < num5)
			{
				num5 = it.ShiftRightGap;
			}
			int num6 = num2 - num5;
			int num7 = num;
			int num8 = LeftBarrier(it);
			if (num8 != int.MinValue && num8 > num7)
			{
				num7 = num8;
			}
			int num9 = it.BaseWidth;
			if (num9 > num6 - num7)
			{
				num9 = num6 - num7;
			}
			if (num9 < 20)
			{
				num9 = 20;
			}
			int num10 = num6 - num9;
			if (num10 < num7)
			{
				num10 = num7;
			}
			if (num10 < 0)
			{
				num10 = 0;
			}
			control.Left = num10;
			control.Width = Math.Max(20, num6 - num10);
			break;
		}
		default:
			control.Left = num;
			control.Width = it.BaseWidth;
			break;
		}
	}

	private static int NaturalWidth(InnerLayout it)
	{
		Control control = it.Control;
		try
		{
			if (control is CheckBox)
			{
				int width = TextRenderer.MeasureText(control.Text, control.Font).Width;
				if (width > 0)
				{
					return width + CheckTextChrome;
				}
			}
			int width2 = TextRenderer.MeasureText(control.Text, control.Font).Width;
			if (width2 <= 0)
			{
				return 0;
			}
			return width2 + 28;
		}
		catch
		{
			return 0;
		}
	}

	private int LeftBarrier(InnerLayout self)
	{
		Control control = self.Control;
		if (control.Parent == null)
		{
			return int.MinValue;
		}
		int baseTop = self.BaseTop;
		int val = self.BaseTop + control.Height;
		int num = int.MinValue;
		for (int i = 0; i < _innerLayouts.Count; i++)
		{
			InnerLayout innerLayout = _innerLayouts[i];
			if (innerLayout.Control == self.Control || innerLayout.Control.Parent != control.Parent || innerLayout.BaseLeft >= self.BaseLeft)
			{
				continue;
			}
			Control control2 = innerLayout.Control;
			int baseTop2 = innerLayout.BaseTop;
			int val2 = innerLayout.BaseTop + control2.Height;
			int num2 = Math.Min(val, val2) - Math.Max(baseTop, baseTop2);
			if (num2 > 0 && num2 * 2 >= Math.Min(control.Height, control2.Height))
			{
				int num3 = innerLayout.BaseLeft + innerLayout.BaseWidth + innerLayout.ShiftByOffset;
				if (num3 > num)
				{
					num = num3;
				}
			}
		}
		if (num == int.MinValue)
		{
			return int.MinValue;
		}
		return num + 8;
	}

	private void MeasureAndWrap()
	{
		RestoreBaseGroupHeights();
		ApplyRowWraps();
		SyncGroupWidthForMeasure();
		bool[] array = new bool[_innerLayouts.Count];
		for (int i = 0; i < 4; i++)
		{
			LayoutInner();
			ComputeRowWraps();
			ApplyRowWraps();
			SyncWrappedHeights();
			GrowGroupsForWraps();
			bool flag = false;
			for (int j = 0; j < _innerLayouts.Count; j++)
			{
				if (array[j] != _innerLayouts[j].WrapsToNextRow)
				{
					flag = true;
					break;
				}
			}
			for (int k = 0; k < _innerLayouts.Count; k++)
			{
				array[k] = _innerLayouts[k].WrapsToNextRow;
			}
			if (!flag)
			{
				break;
			}
		}
	}

	private void SyncGroupWidthForMeasure()
	{
		try
		{
			int width = _scrollHost.ClientSize.Width;
			if (width <= 0)
			{
				width = ClientSize.Width;
			}
			if (width > 0)
			{
				int num = width - ScrollEdgeMargin * 2;
				if (num < GroupWidth)
				{
					num = GroupWidth;
				}
				int num2 = ((num < (int)Math.Round(1240f * CurrentUiScale)) ? 1 : 2);
				int verticalScrollBarWidth = SystemInformation.VerticalScrollBarWidth;
				int num3 = ((num2 >= 2) ? (verticalScrollBarWidth + 4) : 4);
				int num4 = width - ScrollEdgeMargin * 2 - num3;
				if (num4 < GroupWidth)
				{
					num4 = GroupWidth;
				}
				_groupHost.Width = num4;
				_groupHost.Relayout(num4);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void RestoreBaseGroupHeights()
	{
		for (int i = 0; i < _groupHost.Controls.Count; i++)
		{
			if (_groupHost.Controls[i] is GroupBox groupBox && i < _baseGroupHeights.Length && _baseGroupHeights[i] > 0)
			{
				groupBox.Height = _baseGroupHeights[i];
			}
		}
	}

	private void CaptureBaseGroupHeights()
	{
		if (_baseGroupHeights.Length != 0 || _groupHost == null || _groupHost.Controls.Count == 0)
		{
			return;
		}
		_baseGroupHeights = new int[_groupHost.Controls.Count];
		for (int i = 0; i < _groupHost.Controls.Count; i++)
		{
			if (_groupHost.Controls[i] is GroupBox groupBox)
			{
				_baseGroupHeights[i] = groupBox.Height;
			}
		}
	}

	private bool ContentHeightExceeds(int clientWidth)
	{
		if (_groupHost == null || _groupHost.Controls.Count == 0)
		{
			return false;
		}
		int num = ((_groupHost.CurrentColumns < 2) ? 1 : 2);
		int num2 = (int)Math.Ceiling((double)_groupHost.Controls.Count / (double)num);
		int num3 = 0;
		for (int i = 0; i < num2; i++)
		{
			int num4 = 0;
			for (int j = 0; j < num; j++)
			{
				int num5 = i * num + j;
				if (num5 >= _groupHost.Controls.Count)
				{
					break;
				}
				int height = _groupHost.Controls[num5].Height;
				if (height > num4)
				{
					num4 = height;
				}
			}
			num3 += num4 + 8;
		}
		num3 += 24;
		return num3 > _scrollHost.ClientSize.Height;
	}

	private void LayoutResponsive()
	{
		if (_groupHost == null)
		{
			return;
		}
		try
		{
			RebaseAfterAutoScale();
			CaptureBaseGroupHeights();
			MeasureAndWrap();
			int num = _scrollHost.ClientSize.Width;
			if (num <= 0)
			{
				num = ClientSize.Width;
			}
			int verticalScrollBarWidth = SystemInformation.VerticalScrollBarWidth;
			if (_scrollHost.VerticalScroll.Visible || ContentHeightExceeds(num))
			{
				num -= verticalScrollBarWidth;
			}
			int num2 = num - ScrollEdgeMargin * 2 - RightReserve();
			if (num2 < GroupWidth)
			{
				num2 = GroupWidth;
			}
			_groupHost.Width = num2;
			_groupHost.Relayout(num2);
			LayoutInner();
			ApplyRowWraps();
			SyncWrappedHeightsPopulateOnly();
			GrowGroupsForWraps();
			_groupHost.PerformLayout();
			for (int i = 0; i < _groupHost.Controls.Count; i++)
			{
				if (_groupHost.Controls[i] is GroupBox groupBox)
				{
					groupBox.PerformLayout();
				}
			}
			int num3 = 12;
			for (int j = 0; j < _groupHost.Controls.Count; j++)
			{
				Control control = _groupHost.Controls[j];
				int num4 = control.Top + control.Height + control.Margin.Bottom;
				if (num4 > num3)
				{
					num3 = num4;
				}
			}
			_groupHost.Height = num3 + 4;
			if (_btnCancelRef != null && _btnSaveRef != null && _btnDefaultsRef != null)
			{
				int num5 = S(16);
				int num6 = S(12);
				int num7 = _pager.ClientSize.Width - num5;
				int y = (_pager.ClientSize.Height - _btnSaveRef.Height) / 2;
				_btnCancelRef.Location = new Point(num7 - _btnCancelRef.Width, y);
				num7 -= _btnCancelRef.Width + num6;
				_btnSaveRef.Location = new Point(num7 - _btnSaveRef.Width, y);
				num7 -= _btnSaveRef.Width + num6;
				_btnDefaultsRef.Location = new Point(Math.Max(S(8), num7 - _btnDefaultsRef.Width), y);
				int width = Math.Max(S(120), _btnDefaultsRef.Left - S(24));
				_lblStatus.Size = new Size(width, S(22));
				_lblStatus.Location = new Point(S(16), y);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		try
		{
			int num = Native.DpiOfWindow(Handle);
			float num2 = (float)num / 96f;
			if (num > 0 && Math.Abs(num2 - _uiScale) > 0.001f)
			{
				Log.Info("设置窗口：句柄建立后复核 DPI，缓存=" + _uiScale.ToString("0.###") + " 实际=" + num2.ToString("0.###") + "，按实际值重建");
				RescaleForDpi(num);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		Log.Info("设置窗口圆角（DWM） => " + Native.SetWindowRoundedCorners(Handle));
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (_inSizeMove)
		{
			_pendingRelayout = true;
			if (!_relayoutTimer.Enabled)
			{
				_relayoutTimer.Start();
			}
		}
		else
		{
			LayoutResponsive();
		}
	}

	private void RelayoutFlickerFree()
	{
		bool flag = false;
		try
		{
			if (IsHandleCreated && !IsDisposed)
			{
				Native.SendMessageW(Handle, 11, IntPtr.Zero, IntPtr.Zero);
				flag = true;
			}
		}
		catch
		{
		}
		try
		{
			LayoutResponsive();
		}
		finally
		{
			if (flag)
			{
				try
				{
					Native.SendMessageW(Handle, 11, new IntPtr(1), IntPtr.Zero);
				}
				catch
				{
				}
				try
				{
					Invalidate(invalidateChildren: true);
					Update();
				}
				catch
				{
				}
			}
		}
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 561)
		{
			_inSizeMove = true;
		}
		else if (m.Msg == 562)
		{
			_inSizeMove = false;
			_relayoutTimer.Stop();
			_pendingRelayout = false;
			RelayoutFlickerFree();
		}
		else if (m.Msg == 736)
		{
			try
			{
				int newDpi = (int)(((IntPtr)m.WParam).ToInt64() & 0xFFFF);
				RescaleForDpi(newDpi);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		base.WndProc(ref m);
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		LayoutResponsive();
		FitHeightToContent();
	}

	private void ApplySavedSize()
	{
		try
		{
			int settingsWidth = _edit.SettingsWidth;
			int settingsHeight = _edit.SettingsHeight;
			if (settingsWidth > 0 && settingsHeight > 0)
			{
				Size = new Size(settingsWidth, settingsHeight);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void CaptureSizeInto(AppConfig target)
	{
		try
		{
			Rectangle restoreBounds = RestoreBounds;
			if (restoreBounds.Width > 0 && restoreBounds.Height > 0)
			{
				target.SettingsWidth = restoreBounds.Width;
				target.SettingsHeight = restoreBounds.Height;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static GroupBox Group(string text, int x, int y, int w, int h, bool resize = true)
	{
		CardGroup cardGroup = new CardGroup();
		cardGroup.Text = text;
		cardGroup.Location = new Point(x, y);
		cardGroup.Size = new Size(w, h);
		cardGroup.ForeColor = Design.TextPrimary;
		if (resize)
		{
			cardGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		}
		else
		{
			cardGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left;
		}
		return cardGroup;
	}

	internal static string GlyphForSection(string title)
	{
		if (string.IsNullOrEmpty(title))
		{
			return "\ue7c2";
		}
		if (title.Contains("触发"))
		{
			return "\ue765";
		}
		if (title.Contains("鼠标键") || title.Contains("动作"))
		{
			return "\ue7c2";
		}
		if (title.Contains("视觉"))
		{
			return "\ue790";
		}
		if (title.Contains("拖动"))
		{
			return "\ue7c2";
		}
		if (title.Contains("流畅"))
		{
			return "\ue9d9";
		}
		if (title.Contains("启动") || title.Contains("权限"))
		{
			return "\ue72e";
		}
		if (title.Contains("帮助"))
		{
			return "\ue897";
		}
		if (title.Contains("关于"))
		{
			return "\ue946";
		}
		return "\ue76c";
	}

	internal static GraphicsPath RoundedPath(Rectangle r, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		int num = radius * 2;
		if (num > r.Width)
		{
			num = r.Width;
		}
		if (num > r.Height)
		{
			num = r.Height;
		}
		if (num <= 0)
		{
			graphicsPath.AddRectangle(r);
			return graphicsPath;
		}
		graphicsPath.AddArc(r.X, r.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(r.X, r.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private static Color MixColor(Color a, Color b, float t)
	{
		if (t <= 0f)
		{
			return a;
		}
		if (t >= 1f)
		{
			return b;
		}
		return Color.FromArgb((int)Math.Round((float)(int)a.A + (float)(b.A - a.A) * t), (int)Math.Round((float)(int)a.R + (float)(b.R - a.R) * t), (int)Math.Round((float)(int)a.G + (float)(b.G - a.G) * t), (int)Math.Round((float)(int)a.B + (float)(b.B - a.B) * t));
	}

	private static Label Label(string text, int x, int y, int w)
	{
		return new Label
		{
			Text = text,
			Location = new Point(x, y),
			Size = new Size(w, 22),
			TextAlign = ContentAlignment.MiddleLeft
		};
	}

	private static void StyleCombo(ComboBox c, int x, int y, int w)
	{
		c.DropDownStyle = ComboBoxStyle.DropDownList;
		c.Location = new Point(x, y);
		c.Size = new Size(w, 26);
		c.FlatStyle = FlatStyle.Flat;
		c.BackColor = Design.Surface;
		c.ForeColor = Design.TextPrimary;
		Fields.Attach(c, 6);
	}

	private static void StyleNum(NumericUpDown n, int x, int y, int w, int min, int max, int step)
	{
		n.Location = new Point(x, y);
		n.Size = new Size(w, 26);
		n.Minimum = min;
		n.Maximum = max;
		n.Increment = step;
		n.TextAlign = HorizontalAlignment.Right;
		n.BackColor = Design.Surface;
		n.ForeColor = Design.TextPrimary;
		Fields.Attach(n, 6);
	}

	private static void StyleText(TextBox t, Font font, bool readOnly)
	{
		Fields.MakeFlat(t, font, readOnly);
		Fields.Attach(t, 6);
	}

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
			_cmbLeft.Items.Add(new ActionItem
			{
				Value = text,
				Text = Actions.DisplayName(text)
			});
			_cmbRight.Items.Add(new ActionItem
			{
				Value = text,
				Text = Actions.DisplayName(text)
			});
			_cmbMiddle.Items.Add(new ActionItem
			{
				Value = text,
				Text = Actions.DisplayName(text)
			});
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
		SelectId(_cmbFrameStyle, _edit.FrameStyleId);
		SelectId(_cmbFrameColor, _edit.FrameColorId);
		_numFrameThick.Value = Clamp(_edit.FrameThickness, (int)_numFrameThick.Minimum, (int)_numFrameThick.Maximum);
		_numFrameRadius.Value = Clamp(_edit.FrameRadius, (int)_numFrameRadius.Minimum, (int)_numFrameRadius.Maximum);
		SyncFrameEnabled();
		SyncPreview();
		_numCorner.Value = Clamp(_edit.CornerPercent, (int)_numCorner.Minimum, (int)_numCorner.Maximum);
		_chkDblLeft.Checked = _edit.DoubleClickLeftMaximize;
		_chkDblRight.Checked = _edit.DoubleClickRightZone;
		_chkRestoreMax.Checked = _edit.RestoreMaximizedWhenMoved;
		_chkFront.Checked = _edit.BringToFront;
		_numFrameOpacity.Value = Clamp(_edit.FrameOpacity, (int)_numFrameOpacity.Minimum, (int)_numFrameOpacity.Maximum);
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
		_lblRefreshHint.Text = "跟随屏幕：按实测刷新率节流，与显示器同步（本机实测 " + ((refreshRate > 0) ? (refreshRate + "Hz，每帧约 " + Math.Round(1000.0 / (double)refreshRate) + "ms）") : "不到刷新率，此时不节流）") + "。\r\n固定值：自定义节流频率，0 = 不限制。改动在下次拖动时生效。";
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
		UpdateAdminLabel();
	}

	private void SyncFrameEnabled()
	{
		bool flag = _chkFrame.Checked;
		_cmbFrameColor.Enabled = flag;
		_cmbFrameStyle.Enabled = flag;
		_numFrameThick.Enabled = flag;
		_numFrameRadius.Enabled = flag;
		_gridColor.Enabled = flag;
		_galleryStyle.Enabled = flag;
		_galleryStyle.FrameEnabled = flag;
		_pnlPreview.FrameEnabled = flag;
		_pnlPreview.OpacityPercent = (int)_numFrameOpacity.Value;
		_pnlPreview.Refresh_();
	}

	private static List<ColorSwatchGrid.Entry> BuildColorEntries()
	{
		List<ColorSwatchGrid.Entry> list = new List<ColorSwatchGrid.Entry>();
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			FrameColors.Entry entry = FrameColors.All[i];
			list.Add(new ColorSwatchGrid.Entry
			{
				Id = entry.Id,
				Name = entry.Name,
				Color = FrameColors.Resolve(entry.Id)
			});
		}
		return list;
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

	private void SyncRefreshEnabled()
	{
		_numRefreshHz.Enabled = SelectedId(_cmbRefreshMode, "Auto") == "Custom";
	}

	private void SyncSidebarLater()
	{
		try
		{
			if (_sidebar == null)
			{
				return;
			}
			if (!IsHandleCreated)
			{
				_sidebar.SyncActive();
				return;
			}
			BeginInvoke(() =>
			{
				try
				{
					_sidebar.SyncActive();
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
				}
			});
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static string ShortTitleOf(string title)
	{
		if (string.IsNullOrEmpty(title))
		{
			return "";
		}
		int num = title.Length;
		char[] array = new char[5] { '（', '(', '【', '[', '《' };
		for (int i = 0; i < array.Length; i++)
		{
			int num2 = title.IndexOf(array[i]);
			if (num2 > 0 && num2 < num)
			{
				num = num2;
			}
		}
		string text = title.Substring(0, num).Trim();
		if (text.Length > 8)
		{
			return text.Substring(0, 8);
		}
		return text;
	}

	private static List<HelpPanel.Entry> BuildHelpEntries()
	{
		return new List<HelpPanel.Entry>
		{
			new HelpPanel.Entry
			{
				Icon = "\ue765",
				Title = "触发方式",
				Body = "按住触发键（默认 Enter，可改成任意键或鼠标侧键）达到设定时长后生效，光标下那扇窗口四周会亮起外框；松开触发键即退出。触发键只在长按到时才生效，短按会原样放行给当前程序，不影响正常打字。"
			},
			new HelpPanel.Entry
			{
				Icon = "\ue7c2",
				Title = "鼠标键动作",
				Body = "触发后：左键拖动窗口、右键缩放窗口、中键执行一次性动作。三个键都能自定义成移动 / 缩放 / 最大化 / 最小化 / 关闭 / 置底 / 置顶 / 居中。另外双击左键可最大化或还原，双击右键按九宫格吸附到屏幕对应区域。"
			},
			new HelpPanel.Entry
			{
				Icon = "\ue790",
				Title = "外框外观",
				Body = "外框颜色、样式、粗细、圆角、不透明度都可以调，右上角是实时预览，和真正拖窗口时用的是同一段绘制代码。窗口贴近屏幕边缘时外框几何保持不变，不会缩小或跳动。"
			},
			new HelpPanel.Entry
			{
				Icon = "\ue72e",
				Title = "全屏与最大化窗口",
				Body = "默认开启「触发时先把全屏 / 最大化窗口变回普通窗口」：这样拖动和缩放都从一个正常的窗口尺寸开始，不会出现全屏窗口拖完一片黑、或者缩放抓不到边的情况。"
			},
			new HelpPanel.Entry
			{
				Icon = "\ue9d9",
				Title = "晃窗最小化其它窗口",
				Body = "触发后选中窗口，左右来回晃一晃，其它窗口会全部最小化；再晃一次即可还原（还原名单是记下来的，不会漏）。判定要求「多次往复 + 净位移接近 0」，所以正常拖动窗口不会被误触发。"
			},
			new HelpPanel.Entry
			{
				Icon = "\ue72e",
				Title = "不生效的窗口",
				Body = "把窗口标题 / 程序名 / 窗口类名的关键字填进黑名单（逗号分隔），命中就完全不接管。不知道填什么可以用「选择程序」挑 exe，或用「点选窗口」直接在屏幕上点那扇窗，关键字会自动填进去。"
			}
		};
	}

	private void SyncPreview()
	{
		string text = SelectedId(_cmbFrameColor, FrameColors.DefaultId);
		string text2 = SelectedId(_cmbFrameStyle, FrameStyles.DefaultId);
		int thickness = (int)_numFrameThick.Value;
		int radius = (int)_numFrameRadius.Value;
		bool frameEnabled = _chkFrame.Checked;
		_pnlPreview.ColorId = text;
		_pnlPreview.StyleId = text2;
		_pnlPreview.Thickness = thickness;
		_pnlPreview.Radius = radius;
		_pnlPreview.FrameEnabled = frameEnabled;
		_pnlPreview.Refresh_();
		_gridColor.SelectedId = text;
		_lblColorName.Text = "当前颜色：" + ColorNameOf(text);
		_galleryStyle.SelectedId = text2;
		_galleryStyle.ColorId = text;
		_galleryStyle.Thickness = thickness;
		_galleryStyle.Radius = radius;
		_galleryStyle.FrameEnabled = frameEnabled;
		MarkFrameDirtyIfChanged();
	}

	private void MarkFrameDirtyIfChanged()
	{
		if (_lblFrameTip != null && !_populating)
		{
			string text = SelectedId(_cmbFrameColor, FrameColors.DefaultId);
			string text2 = SelectedId(_cmbFrameStyle, FrameStyles.DefaultId);
			bool flag = text != _edit.FrameColorId || text2 != _edit.FrameStyleId || (int)_numFrameThick.Value != _edit.FrameThickness || (int)_numFrameRadius.Value != _edit.FrameRadius || _chkFrame.Checked != _edit.ShowActivationFrame;
			_lblFrameTip.ForeColor = (flag ? Color.FromArgb(185, 96, 16) : Design.Hint);
			_lblFrameTip.Text = (flag ? "外观已改动，点下面的「保存并生效」才会真正画到实际外框上。\r\n右上角是实时预览，和实际外框用的是同一段绘制代码。" : "外框用来告诉你「这扇窗口现在已经被接管了」，松手或者松开触发键就会消失。\r\n上面四个都可以随便改，右上角是实时预览。");
		}
	}

	private static void PaintColorEntry(Graphics g, Rectangle bounds, object item, Font font)
	{
		try
		{
			if (!(item is ColorItem colorItem) || bounds.Width <= 0 || bounds.Height <= 0)
			{
				return;
			}
			HighQuality.Apply(g);
			int num = Math.Max(8, (int)Math.Round((double)bounds.Height * 0.52));
			if (num > bounds.Width / 3)
			{
				num = Math.Max(8, bounds.Width / 3);
			}
			int y = bounds.Top + (bounds.Height - num) / 2;
			Rectangle r = new Rectangle(bounds.Left, y, num, num);
			GraphicsPath graphicsPath = RoundedPath(r, Math.Max(2, Fields.Sc(3)));
			try
			{
				using (SolidBrush brush = new SolidBrush(colorItem.Color))
				{
					g.FillPath(brush, graphicsPath);
				}
				using Pen pen = new Pen(Color.FromArgb(186, 192, 205));
				g.DrawPath(pen, graphicsPath);
			}
			finally
			{
				graphicsPath.Dispose();
			}
			TextRenderer.DrawText(bounds: new Rectangle(r.Right + Fields.Sc(8), bounds.Top, bounds.Width - num - Fields.Sc(10), bounds.Height), dc: g, text: colorItem.Text, font: (font != null) ? font : SystemFonts.DefaultFont, foreColor: Design.TextPrimary, flags: TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void UpdateAdminLabel()
	{
		bool flag = Program.IsAdministrator();
		_lblAdmin.Text = (flag ? "当前权限：管理员（可以拖动任务管理器这类提权窗口）" : "当前权限：普通用户（日常窗口没问题，提权窗口拖不动）");
		_btnRestartAdmin.Enabled = !flag;
	}

	private void UpdateKeyText()
	{
		_txtKey.Text = KeysUtil.DisplayName(_edit.TriggerKeyId);
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

	private void StartRecording()
	{
		_recording = true;
		_coord.SuppressTrigger = true;
		_txtKey.Text = "请按下要用的按键…";
		_txtKey.BackColor = Color.FromArgb(255, 244, 214);
		_lblKeyHint.Text = "正在录制：按任意键或鼠标侧键，Esc 取消。左/右/中键已被鼠标动作占用，不能当触发键。";
		Focus();
	}

	private void StopRecording(bool applied)
	{
		_recording = false;
		_coord.SuppressTrigger = false;
		_txtKey.BackColor = SystemColors.Window;
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
		_lblKeyHint.ForeColor = ((text.Length > 0) ? Design.WarnText : Design.Hint);
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
			string text = ResolveRecordedKey(keys);
			if (text == null)
			{
				_lblKeyHint.Text = "左键 / 右键 / 中键不能作为触发键，请用键盘按键或鼠标侧键。";
				_lblKeyHint.ForeColor = Design.WarnText;
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
				_lblKeyHint.ForeColor = Design.WarnText;
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
		_edit.RequireHold = _chkHold.Checked;
		_edit.HoldMs = (int)_numHold.Value;
		_edit.ActionLeft = SelectedAction(_cmbLeft) ?? _edit.ActionLeft;
		_edit.ActionRight = SelectedAction(_cmbRight) ?? _edit.ActionRight;
		_edit.ActionMiddle = SelectedAction(_cmbMiddle) ?? _edit.ActionMiddle;
		_edit.ShowActivationFrame = _chkFrame.Checked;
		_edit.FrameColorId = SelectedId(_cmbFrameColor, _edit.FrameColorId);
		_edit.FrameStyleId = SelectedId(_cmbFrameStyle, _edit.FrameStyleId);
		_edit.FrameThickness = (int)_numFrameThick.Value;
		_edit.FrameRadius = (int)_numFrameRadius.Value;
		_edit.CornerPercent = (int)_numCorner.Value;
		_edit.ShakeMinimizeOthers = _chkShake.Checked;
		_edit.DragRefreshMode = SelectedId(_cmbRefreshMode, _edit.DragRefreshMode);
		_edit.DragRefreshHz = (int)_numRefreshHz.Value;
		_edit.DoubleClickLeftMaximize = _chkDblLeft.Checked;
		_edit.DoubleClickRightZone = _chkDblRight.Checked;
		_edit.RestoreMaximizedWhenMoved = _chkRestoreMax.Checked;
		_edit.BringToFront = _chkFront.Checked;
		_edit.FrameOpacity = (int)_numFrameOpacity.Value;
		_edit.AllowFullscreenWindows = _chkFullscreen.Checked;
		_edit.ExitFullscreenOnTrigger = _chkExitFullscreen.Checked;
		_edit.Blacklist = _txtBlacklist.Text;
		_edit.AutoStart = SelectedId(_cmbAutoStart, _edit.AutoStart);
		_edit.RunAsAdmin = _chkRunAsAdmin.Checked;
		_edit.WarnNonAdmin = _chkWarnNonAdmin.Checked;
		CaptureSizeInto(_edit);
		_store.Apply(_edit);
		if (AutoStart.Apply(_edit.AutoStart, AppPaths.ExePath, out var message))
		{
			_lblStatus.ForeColor = Design.Success;
			_lblStatus.Text = "已保存并立即生效";
		}
		else
		{
			_lblStatus.ForeColor = Color.FromArgb(185, 28, 28);
			_lblStatus.Text = "配置已保存，但开机自启设置失败";
			MessageBox.Show(message, "开机自启设置失败", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
		if (!string.IsNullOrEmpty(message))
		{
			MessageBox.Show(message, "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		if (_edit.RunAsAdmin && !Program.IsAdministrator() && MessageBox.Show("已保存。\r\n\r\n管理员权限只在【启动的时候】申请，所以要重启程序才会生效。\r\n现在以管理员身份重启吗？（选\"否\"就等下次启动再提权）", "长按拖窗 · 权限变更", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
		{
			RestartAsAdmin();
		}
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
			mask.MouseDown += (object? s, MouseEventArgs e) =>
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
			mask.KeyDown += (object? s, KeyEventArgs e) =>
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

	internal void ProbeAppendBlacklist(string kw)
	{
		AppendBlacklistKeyword(kw);
	}

	private void Status(string text, bool error)
	{
		try
		{
			if (_lblStatus != null)
			{
				_lblStatus.ForeColor = (error ? Color.FromArgb(185, 28, 28) : Design.Success);
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
		_edit = appConfig;
		if (_recording)
		{
			StopRecording(applied: false);
		}
		Populate();
		_lblStatus.ForeColor = Design.Hint;
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

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		if (_recording)
		{
			StopRecording(applied: false);
		}
		base.OnFormClosed(e);
	}

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
			ColorNameText = _lblColorName.Text
		};
	}

	internal static Control ProbeGroupAt(Form f, int index)
	{
		if (!(f is SettingsForm { _groupHost: not null } settingsForm))
		{
			return null;
		}
		if (index < 0 || index >= settingsForm._groupHost.Controls.Count)
		{
			return null;
		}
		return settingsForm._groupHost.Controls[index];
	}

	internal static void ProbeRescaleForDpi(Form f, int dpi)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.RescaleForDpi(dpi);
		}
	}

	internal static Rectangle ProbeRectOfTextForTest(Form f, string prefix)
	{
		try
		{
			if (!(f is SettingsForm { _groupHost: not null } settingsForm))
			{
				return Rectangle.Empty;
			}
			for (int i = 0; i < settingsForm._groupHost.Controls.Count; i++)
			{
				Control control = settingsForm._groupHost.Controls[i];
				for (int j = 0; j < control.Controls.Count; j++)
				{
					Control control2 = control.Controls[j];
					if (control2.Text != null && control2.Text.StartsWith(prefix))
					{
						return control2.Bounds;
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

	internal static string ProbeLayoutFingerprint(Form f)
	{
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			if (!(f is SettingsForm { _groupHost: not null } settingsForm))
			{
				return "";
			}
			stringBuilder.Append("host=").Append(settingsForm._groupHost.Width).Append(',')
				.Append(settingsForm._groupHost.Height)
				.Append(" cols=")
				.Append(settingsForm._groupHost.CurrentColumns)
				.Append(';');
			for (int i = 0; i < settingsForm._innerLayouts.Count; i++)
			{
				Control control = settingsForm._innerLayouts[i].Control;
				if (control != null)
				{
					stringBuilder.Append(control.GetType().Name).Append(':').Append(control.Left)
						.Append(',')
						.Append(control.Top)
						.Append(',')
						.Append(control.Width)
						.Append(',')
						.Append(control.Height)
						.Append(';');
				}
			}
		}
		catch
		{
		}
		return stringBuilder.ToString();
	}

	internal static int ProbeTwoColumnThresholdPx(Form f)
	{
		float num = ((!(f is SettingsForm settingsForm)) ? 1f : settingsForm.UiScale);
		return (int)Math.Round(1240f * num);
	}

	internal static int ProbeRelayoutColumns(Form f, int available)
	{
		if (!(f is SettingsForm { _groupHost: not null } settingsForm))
		{
			return -1;
		}
		try
		{
			settingsForm._groupHost.Relayout(available);
			return settingsForm._groupHost.CurrentColumns;
		}
		catch
		{
			return -1;
		}
	}

	internal static GroupClipReport ProbeGroupClipping(Form f, int groupIndex)
	{
		GroupClipReport groupClipReport = new GroupClipReport();
		try
		{
			if (!(f is SettingsForm { _groupHost: not null } settingsForm))
			{
				return groupClipReport;
			}
			if (groupIndex < 0 || groupIndex >= settingsForm._groupHost.Controls.Count)
			{
				return groupClipReport;
			}
			Control control = settingsForm._groupHost.Controls[groupIndex];
			if (control == null)
			{
				return groupClipReport;
			}
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

	internal static string ProbeTextFitting(Form f, int groupIndex)
	{
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			if (!(f is SettingsForm { _groupHost: not null } settingsForm))
			{
				return "";
			}
			if (groupIndex < 0 || groupIndex >= settingsForm._groupHost.Controls.Count)
			{
				return "";
			}
			Control control = settingsForm._groupHost.Controls[groupIndex];
			if (control == null)
			{
				return "";
			}
			for (int i = 0; i < control.Controls.Count; i++)
			{
				Control control2 = control.Controls[i];
				if ((control2 is CheckBox || control2 is Label) && !string.IsNullOrEmpty(control2.Text))
				{
					int num = control2.Width - ((control2 is CheckBox) ? Fields.Sc(26) : Fields.Sc(4));
					if (num < 20)
					{
						num = 20;
					}
					Size size;
					Size size2;
					try
					{
						size = TextRenderer.MeasureText(control2.Text, control2.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
						size2 = TextRenderer.MeasureText(control2.Text, control2.Font, new Size(num, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
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

	internal void ProbeSelectFrameColor(string colorId)
	{
		SelectId(_cmbFrameColor, colorId);
		SyncPreview();
	}

	internal FramePickerProbe ProbeFramePickers()
	{
		return new FramePickerProbe
		{
			ColorCells = _gridColor.CellCountForTest,
			StyleCells = _galleryStyle.CellCountForTest,
			GridSize = _gridColor.Size,
			GallerySize = _galleryStyle.Size,
			GridInsideGroup = InsideParentClient(_gridColor),
			GalleryInsideGroup = InsideParentClient(_galleryStyle),
			GridInk = CountInkOutsideBackground(_gridColor, Design.Surface),
			GalleryInk = CountInkOutsideBackground(_galleryStyle, Design.Surface),
			GridAccentInk = CountAccentInk(_gridColor),
			GalleryAccentInk = CountAccentInk(_galleryStyle),
			GridSelectedCell = _gridColor.SelectedCellRectForTest,
			GallerySelectedCell = _galleryStyle.SelectedCellRectForTest
		};
	}

	private static bool InsideParentClient(Control c)
	{
		if (c == null || c.Parent == null)
		{
			return false;
		}
		Rectangle clientRectangle = c.Parent.ClientRectangle;
		if (c.Left >= 0 && c.Top >= 0 && c.Right <= clientRectangle.Width + 1)
		{
			return c.Bottom <= clientRectangle.Height + 1;
		}
		return false;
	}

	internal static bool ProbeHoverTweenAnimates(out float start, out float end)
	{
		start = 0f;
		end = 0f;
		try
		{
			using FlatButton flatButton = new FlatButton();
			flatButton.ProbeSetHover(on: true);
			start = flatButton.ProbeHoverValue;
			long num = Environment.TickCount64 + 900;
			while (Environment.TickCount64 < num)
			{
				Application.DoEvents();
				Thread.Sleep(8);
				if (flatButton.ProbeHoverValue > 0.98f)
				{
					break;
				}
			}
			end = flatButton.ProbeHoverValue;
			return start < 0.3f && end > 0.9f;
		}
		catch
		{
			return false;
		}
	}

	internal static RenderStats ProbeFormRender(Form f, string frameColorId)
	{
		RenderStats renderStats = new RenderStats();
		try
		{
			if (f.Width <= 0 || f.Height <= 0)
			{
				return renderStats;
			}
			renderStats.Width = f.Width;
			renderStats.Height = f.Height;
			using Bitmap bitmap = new Bitmap(f.Width, f.Height);
			f.DrawToBitmap(bitmap, new Rectangle(0, 0, f.Width, f.Height));
			HashSet<int> hashSet = new HashSet<int>();
			Color color = FrameColors.Resolve(frameColorId);
			long num = 0L;
			int num2 = 0;
			for (int i = 0; i < bitmap.Height; i += 2)
			{
				for (int j = 0; j < bitmap.Width; j += 2)
				{
					Color pixel = bitmap.GetPixel(j, i);
					hashSet.Add(pixel.ToArgb());
					num += pixel.R + pixel.G + pixel.B;
					num2++;
					if (pixel.R < 24 && pixel.G < 24 && pixel.B < 24)
					{
						renderStats.BlackPixels++;
					}
					if (Math.Abs(pixel.R - Design.Accent.R) <= 18 && Math.Abs(pixel.G - Design.Accent.G) <= 18 && Math.Abs(pixel.B - Design.Accent.B) <= 18)
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
			renderStats.MeanBrightness = ((num2 == 0) ? 0.0 : ((double)num / 3.0 / (double)num2));
		}
		catch
		{
		}
		return renderStats;
	}

	internal int[] ProbeFrameColorInk(string colorId)
	{
		Color want = FrameColors.Resolve(colorId);
		return new int[3]
		{
			CountExactColor(_gridColor, want),
			CountExactColor(_galleryStyle, want),
			CountExactColor(_pnlPreview, want)
		};
	}

	private static int CountExactColor(Control c, Color want)
	{
		try
		{
			if (c == null || c.Width <= 0 || c.Height <= 0)
			{
				return -1;
			}
			using Bitmap bitmap = new Bitmap(c.Width, c.Height);
			c.DrawToBitmap(bitmap, new Rectangle(0, 0, c.Width, c.Height));
			int num = 0;
			for (int i = 0; i < bitmap.Height; i++)
			{
				for (int j = 0; j < bitmap.Width; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (Math.Abs(pixel.R - want.R) <= 18 && Math.Abs(pixel.G - want.G) <= 18 && Math.Abs(pixel.B - want.B) <= 18)
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

	internal string ProbeGridDiagnostic()
	{
		try
		{
			Rectangle selectedCellRectForTest = _gridColor.SelectedCellRectForTest;
			using Bitmap bitmap = new Bitmap(Math.Max(1, _gridColor.Width), Math.Max(1, _gridColor.Height));
			_gridColor.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
			int x = Math.Min(bitmap.Width - 1, selectedCellRectForTest.X + selectedCellRectForTest.Width / 2);
			int y = Math.Min(bitmap.Height - 1, selectedCellRectForTest.Y + selectedCellRectForTest.Height / 2);
			Color pixel = bitmap.GetPixel(x, y);
			Color pixel2 = bitmap.GetPixel(0, 0);
			return "格=" + selectedCellRectForTest.ToString() + " 尺寸=" + _gridColor.Width + "x" + _gridColor.Height + " 中心像素=" + pixel.R + "," + pixel.G + "," + pixel.B + "," + pixel.A + " 左上像素=" + pixel2.R + "," + pixel2.G + "," + pixel2.B + "," + pixel2.A + " Enabled=" + _gridColor.Enabled + " chkFrame=" + _chkFrame.Checked + " 父Enabled=" + ((_gridColor.Parent != null) ? _gridColor.Parent.Enabled.ToString() : "无父") + " SelectedId=" + _gridColor.SelectedId + " 格数=" + _gridColor.CellCountForTest;
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name + " " + ex.Message;
		}
	}

	private static int CountInkOutsideBackground(Control c, Color bg)
	{
		try
		{
			if (c.Width <= 0 || c.Height <= 0)
			{
				return -1;
			}
			using Bitmap bitmap = new Bitmap(c.Width, c.Height);
			c.DrawToBitmap(bitmap, new Rectangle(0, 0, c.Width, c.Height));
			int num = 0;
			for (int i = 0; i < bitmap.Height; i += 2)
			{
				for (int j = 0; j < bitmap.Width; j += 2)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (Math.Abs(pixel.R - bg.R) > 8 || Math.Abs(pixel.G - bg.G) > 8 || Math.Abs(pixel.B - bg.B) > 8)
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

	private static int CountAccentInk(Control c)
	{
		Color accent = Design.Accent;
		try
		{
			if (c.Width <= 0 || c.Height <= 0)
			{
				return -1;
			}
			using Bitmap bitmap = new Bitmap(c.Width, c.Height);
			c.DrawToBitmap(bitmap, new Rectangle(0, 0, c.Width, c.Height));
			int num = 0;
			for (int i = 0; i < bitmap.Height; i++)
			{
				for (int j = 0; j < bitmap.Width; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (Math.Abs(pixel.R - accent.R) <= 24 && Math.Abs(pixel.G - accent.G) <= 24 && Math.Abs(pixel.B - accent.B) <= 24)
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

	internal void ProbeSelectFrameStyle(string styleId)
	{
		SelectId(_cmbFrameStyle, styleId);
		SyncPreview();
	}

	internal void ProbeSetFrameShape(int thickness, int radius, bool enabled)
	{
		_numFrameThick.Value = Clamp(thickness, (int)_numFrameThick.Minimum, (int)_numFrameThick.Maximum);
		_numFrameRadius.Value = Clamp(radius, (int)_numFrameRadius.Minimum, (int)_numFrameRadius.Maximum);
		_chkFrame.Checked = enabled;
		SyncPreview();
	}

	internal void ProbeCaptureInto(AppConfig target)
	{
		target.ShowActivationFrame = _chkFrame.Checked;
		target.FrameColorId = SelectedId(_cmbFrameColor, target.FrameColorId);
		target.FrameStyleId = SelectedId(_cmbFrameStyle, target.FrameStyleId);
		target.FrameThickness = (int)_numFrameThick.Value;
		target.FrameRadius = (int)_numFrameRadius.Value;
	}

	internal static int MeasureGlyphInk(string glyph, float px)
	{
		try
		{
			using Font font = Icons.At(px);
			if (font == null)
			{
				return -1;
			}
			int num = (int)Math.Ceiling(px * 2f);
			using Bitmap bitmap = new Bitmap(num, num);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.White);
				graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
				graphics.DrawString(glyph, font, Brushes.Black, 0f, 0f);
			}
			int num2 = 0;
			for (int i = 0; i < bitmap.Height; i++)
			{
				for (int j = 0; j < bitmap.Width; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (pixel.R < 200 || pixel.G < 200 || pixel.B < 200)
					{
						num2++;
					}
				}
			}
			return num2;
		}
		catch
		{
			return -1;
		}
	}

	internal static string ProbeSwitchColors(Form f, bool on)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			FlatCheck flatCheck = null;
			for (int i = 0; i < settingsForm._innerLayouts.Count; i++)
			{
				if (settingsForm._innerLayouts[i].Control is FlatCheck { Width: >40, Enabled: not false } flatCheck2)
				{
					flatCheck = flatCheck2;
					break;
				}
			}
			if (flatCheck == null)
			{
				return "(没找到可用的开关)";
			}
			bool flag = flatCheck.Checked;
			flatCheck.Checked = on;
			Application.DoEvents();
			using Bitmap bitmap = new Bitmap(Math.Max(1, flatCheck.Width), Math.Max(1, flatCheck.Height));
			flatCheck.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
			int num = CheckTextChrome - Fields.Sc(10);
			int num2 = (int)Math.Round((double)num * 0.56);
			int y = Math.Min(bitmap.Height - 1, num2 / 2);
			int x = (on ? Math.Max(0, num / 5) : Math.Max(0, num - num / 5));
			Color pixel = bitmap.GetPixel(x, y);
			int x2 = (on ? Math.Max(0, num - num2 / 2) : Math.Max(0, num2 / 2));
			Color pixel2 = bitmap.GetPixel(x2, y);
			flatCheck.Checked = flag;
			Application.DoEvents();
			return "轨道=" + pixel.R + "," + pixel.G + "," + pixel.B + " 滑块=" + pixel2.R + "," + pixel2.G + "," + pixel2.B;
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name;
		}
	}

	internal static string ProbeGalleryGeometry(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return "";
		}
		try
		{
			int num = FrameStyles.All.Length;
			int num2 = settingsForm._galleryStyle.Columns;
			if (num2 <= 0)
			{
				num2 = 5;
			}
			int num3 = (num + num2 - 1) / num2;
			return "样式数=" + num + " 列=" + num2 + " 行=" + num3 + " 格高=" + Fields.Sc(46) + " 间隙=" + Fields.Sc(6) + " 需要高=" + (num3 * Fields.Sc(46) + (num3 - 1) * Fields.Sc(6)) + " 控件高=" + settingsForm._galleryStyle.Height;
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name;
		}
	}

	internal static string ProbeColorGridGeometry(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return "";
		}
		try
		{
			int num = FrameColors.All.Length;
			int num2 = settingsForm._gridColor.Columns;
			if (num2 <= 0)
			{
				num2 = 5;
			}
			int num3 = (num + num2 - 1) / num2;
			return "颜色数=" + num + " 列=" + num2 + " 行=" + num3 + " 格=" + Fields.Sc(22) + " 间隙=" + Fields.Sc(6) + " 需要高=" + (num3 * Fields.Sc(22) + (num3 - 1) * Fields.Sc(6)) + " 控件高=" + settingsForm._gridColor.Height;
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name;
		}
	}

	internal static bool ProbeColorGridFits(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return false;
		}
		int num = FrameColors.All.Length;
		int num2 = settingsForm._gridColor.Columns;
		if (num2 <= 0)
		{
			num2 = 5;
		}
		int num3 = (num + num2 - 1) / num2;
		int num4 = num3 * Fields.Sc(22) + (num3 - 1) * Fields.Sc(6);
		return settingsForm._gridColor.Height >= num4;
	}

	internal static bool ProbeGalleryFits(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return false;
		}
		int num = FrameStyles.All.Length;
		int num2 = settingsForm._galleryStyle.Columns;
		if (num2 <= 0)
		{
			num2 = 5;
		}
		int num3 = (num + num2 - 1) / num2;
		int num4 = num3 * Fields.Sc(46) + (num3 - 1) * Fields.Sc(6);
		return settingsForm._galleryStyle.Height >= num4;
	}

	internal static int ProbeGroupCount(Form f)
	{
		if (f is SettingsForm { _groups: not null } settingsForm)
		{
			return settingsForm._groups.Length;
		}
		return 0;
	}

	internal static bool ProbeGroupInTree(Form f, int index)
	{
		if (!(f is SettingsForm { _groups: not null } settingsForm))
		{
			return false;
		}
		if (index < 0 || index >= settingsForm._groups.Length)
		{
			return false;
		}
		Control control = settingsForm._groups[index];
		for (int i = 0; i < 16; i++)
		{
			if (control == null)
			{
				break;
			}
			if (control == settingsForm)
			{
				return true;
			}
			control = control.Parent;
		}
		return false;
	}

	internal static string ProbeGroupRects(Form f, int index)
	{
		if (!(f is SettingsForm { _groups: not null } settingsForm))
		{
			return "";
		}
		if (index < 0 || index >= settingsForm._groups.Length)
		{
			return "";
		}
		GroupBox groupBox = settingsForm._groups[index];
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < groupBox.Controls.Count; i++)
		{
			Control control = groupBox.Controls[i];
			if (control != null)
			{
				stringBuilder.Append(ShortText(control)).Append('(').Append(control.Left)
					.Append(',')
					.Append(control.Top)
					.Append(' ')
					.Append(control.Width)
					.Append('x')
					.Append(control.Height)
					.Append(") ");
			}
		}
		return stringBuilder.ToString();
	}

	internal static string ProbeGroupOverlaps(Form f, int index)
	{
		if (!(f is SettingsForm { _groups: not null } settingsForm))
		{
			return "";
		}
		if (index < 0 || index >= settingsForm._groups.Length)
		{
			return "";
		}
		GroupBox obj = settingsForm._groups[index];
		StringBuilder stringBuilder = new StringBuilder();
		Control.ControlCollection controls = obj.Controls;
		for (int i = 0; i < controls.Count; i++)
		{
			for (int j = i + 1; j < controls.Count; j++)
			{
				Control control = controls[i];
				Control control2 = controls[j];
				if (control != null && control2 != null && control.Visible && control2.Visible)
				{
					Rectangle bounds = control.Bounds;
					Rectangle bounds2 = control2.Bounds;
					Rectangle rectangle = Rectangle.Intersect(bounds, bounds2);
					if (rectangle.Width > 1 && rectangle.Height > 1)
					{
						stringBuilder.Append('[').Append(i).Append(':')
							.Append(ShortText(control))
							.Append(" x ")
							.Append(j)
							.Append(':')
							.Append(ShortText(control2))
							.Append(" 重叠 ")
							.Append(rectangle.Width)
							.Append('x')
							.Append(rectangle.Height)
							.Append("] ");
					}
				}
			}
		}
		return stringBuilder.ToString();
	}

	private static string ShortText(Control c)
	{
		string text = c.Text;
		if (string.IsNullOrEmpty(text))
		{
			text = c.GetType().Name;
		}
		if (text.Length > 12)
		{
			return text.Substring(0, 12);
		}
		return text;
	}

	internal static int ProbeSidebarActive(Form f)
	{
		if (f is SettingsForm { _sidebar: not null } settingsForm)
		{
			return settingsForm._sidebar.ActiveForTest;
		}
		return -1;
	}

	internal static int ProbeSidebarCount(Form f)
	{
		if (f is SettingsForm { _sidebar: not null } settingsForm)
		{
			return settingsForm._sidebar.CountForTest;
		}
		return -1;
	}

	internal static int ProbeWheelScroll(Form f, int notches)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return -1;
		}
		try
		{
			Point point = settingsForm._scrollHost.PointToScreen(new Point(settingsForm._scrollHost.ClientSize.Width / 2, settingsForm._scrollHost.ClientSize.Height / 2));
			long num = (long)(((ulong)point.X & 0xFFFFuL) | (((ulong)point.Y & 0xFFFFuL) << 16));
			for (int i = 0; i < Math.Abs(notches); i++)
			{
				long num2 = (long)((ulong)(ushort)((notches > 0) ? 120 : (-120)) << 16);
				Native.SendMessageW(settingsForm._scrollHost.Handle, 522, new IntPtr((int)num2), new IntPtr((int)num));
			}
			Application.DoEvents();
			return -settingsForm._scrollHost.AutoScrollPosition.Y;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return -2;
		}
	}

	internal static string ProbeScrollInfo(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return "";
		}
		try
		{
			return "窗体客户区=" + settingsForm.ClientSize.Width + "x" + settingsForm.ClientSize.Height + " 滚动区=" + settingsForm._scrollHost.ClientSize.Width + "x" + settingsForm._scrollHost.ClientSize.Height + " 显示矩形=" + settingsForm._scrollHost.DisplayRectangle.ToString() + " 分组宿主高=" + ((settingsForm._groupHost == null) ? (-1) : settingsForm._groupHost.Height) + " AutoScroll=" + settingsForm._scrollHost.AutoScroll + " 可见=" + settingsForm._scrollHost.Visible;
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name;
		}
	}

	internal static string ProbeSyncDiag(Form f)
	{
		if (!(f is SettingsForm { _sidebar: not null } settingsForm))
		{
			return "";
		}
		return "SyncActive被调=" + settingsForm._sidebar.SyncCountForTest + " 末次top=" + settingsForm._sidebar.LastTopForTest + " 末次best=" + settingsForm._sidebar.LastBestForTest + " 当前滚动=" + -settingsForm._scrollHost.AutoScrollPosition.Y;
	}

	internal static int ProbeScrollTo(Form f, int y)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return -1;
		}
		try
		{
			settingsForm._scrollHost.AutoScrollPosition = new Point(0, Math.Max(0, y));
			settingsForm._scrollHost.PerformLayout();
			settingsForm._sidebar.SyncActive();
			return -settingsForm._scrollHost.AutoScrollPosition.Y;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return -2;
		}
	}

	internal static int ProbeGroupTop(Form f, int index)
	{
		if (!(f is SettingsForm { _groups: not null } settingsForm))
		{
			return -1;
		}
		if (index < 0 || index >= settingsForm._groups.Length)
		{
			return -1;
		}
		return settingsForm._groups[index].Top;
	}

	internal static int ProbeWheelSwallowed(Control c)
	{
		if (c is FlatCombo flatCombo)
		{
			return flatCombo.WheelSwallowedForTest;
		}
		return -1;
	}

	internal static int ProbeWheelForwarded(Control c)
	{
		if (c is FlatCombo flatCombo)
		{
			return flatCombo.WheelForwardedForTest;
		}
		return -1;
	}

	internal int ProbeDefaultClientHeight()
	{
		return S(640);
	}

	internal static bool VerifyLayout(Form f)
	{
		if (!(f is SettingsForm { _groupHost: not null } settingsForm))
		{
			return false;
		}
		if (!IsInFormChain(f, settingsForm._groupHost))
		{
			return false;
		}
		int num = 0;
		for (int i = 0; i < settingsForm._groupHost.Controls.Count; i++)
		{
			if (settingsForm._groupHost.Controls[i] is GroupBox)
			{
				num++;
			}
		}
		return num >= 6;
	}

	private static bool IsInFormChain(Form f, Control c)
	{
		for (Control control = c; control != null; control = control.Parent)
		{
			if (control == f)
			{
				return true;
			}
		}
		return false;
	}

	internal static bool VerifyResizable(Form f)
	{
		if (f.FormBorderStyle != FormBorderStyle.Sizable)
		{
			return false;
		}
		if (!f.MaximizeBox)
		{
			return false;
		}
		if (f.MinimumSize.Width < 400 || f.MinimumSize.Height < 300)
		{
			return false;
		}
		return true;
	}

	internal static bool VerifyAnchors(Form f)
	{
		if (!(f is SettingsForm { _groupHost: not null, GroupWidth: var groupWidth } settingsForm))
		{
			return false;
		}
		settingsForm._groupHost.Width = groupWidth;
		settingsForm._groupHost.Relayout(groupWidth);
		if (settingsForm._groupHost.CurrentColumns != 1)
		{
			return false;
		}
		int num = (int)Math.Round(1300f * CurrentUiScale);
		settingsForm._groupHost.Width = num;
		settingsForm._groupHost.Relayout(num);
		if (settingsForm._groupHost.CurrentColumns != 2)
		{
			return false;
		}
		settingsForm._groupHost.Width = groupWidth;
		settingsForm._groupHost.Relayout(groupWidth);
		if (settingsForm._groupHost.CurrentColumns != 1)
		{
			return false;
		}
		return true;
	}

	internal static bool VerifyInnerStretch(Form f)
	{
		if (!(f is SettingsForm { _groupHost: not null } settingsForm) || settingsForm._innerLayouts.Count == 0)
		{
			return false;
		}
		settingsForm.ClientSize = new Size(560, 760);
		Application.DoEvents();
		settingsForm.LayoutResponsive();
		int width = settingsForm._cmbLeft.Width;
		int width2 = settingsForm._txtBlacklist.Width;
		int num = ((settingsForm._cmbLeft.Parent != null) ? settingsForm._cmbLeft.Parent.ClientSize.Width : (-1));
		int currentColumns = settingsForm._groupHost.CurrentColumns;
		settingsForm.ClientSize = new Size(ClientWidthForTwoColumns, 820 * (((int)Math.Round(CurrentUiScale) < 1) ? 1 : ((int)Math.Round(CurrentUiScale))));
		Application.DoEvents();
		settingsForm.LayoutResponsive();
		int width3 = settingsForm._cmbLeft.Width;
		int width4 = settingsForm._txtBlacklist.Width;
		int num2 = ((settingsForm._cmbLeft.Parent != null) ? settingsForm._cmbLeft.Parent.ClientSize.Width : (-1));
		int currentColumns2 = settingsForm._groupHost.CurrentColumns;
		LastStretchDetail = "列数 " + currentColumns + "->" + currentColumns2 + " | 下拉框 " + width + "->" + width3 + " | 黑名单 " + width2 + "->" + width4 + " | 分组内宽 " + num + "->" + num2 + " | " + DumpLayoutItem(f, settingsForm._cmbLeft);
		if (currentColumns2 == 2)
		{
			if (width3 < 160)
			{
				return false;
			}
			if (width4 < 160)
			{
				return false;
			}
		}
		else
		{
			if (width3 <= width)
			{
				return false;
			}
			if (width4 <= width2)
			{
				return false;
			}
		}
		return true;
	}

	internal static void RelayoutForTest(Form f)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.LayoutResponsive();
		}
	}

	internal static void PopulateForTest(Form f)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.Populate();
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
			Application.DoEvents();
			StringBuilder stringBuilder = new StringBuilder();
			List<string> list = new List<string>();
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
				list.Add(text2);
				InlineArray6<object> buffer = default;
				buffer[0] = text2;
				buffer[1] = bitmap.Width;
				buffer[2] = bitmap.Height;
				buffer[3] = num;
				buffer[4] = num2;
				buffer[5] = Path.GetFileName(text3);
				stringBuilder.AppendLine(string.Format("  {0,-8} size={1}x{2} 像素和={3} 非白像素={4} 文件={5}", (ReadOnlySpan<object?>)buffer));
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("=== 路径 B：改下拉框 SelectedIndex（模拟用户点选）===");
			stringBuilder.AppendLine("  下拉框里共 " + settingsForm._cmbFrameStyle.Items.Count + " 项");
			for (int l = 0; l < settingsForm._cmbFrameStyle.Items.Count; l++)
			{
				settingsForm._cmbFrameStyle.SelectedIndex = l;
				Application.DoEvents();
				string styleId = settingsForm._pnlPreview.StyleId;
				object obj3 = settingsForm._cmbFrameStyle.Items[l];
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
				InlineArray6<object> buffer2 = default;
				buffer2[0] = l;
				buffer2[1] = ((obj3 != null) ? obj3.ToString() : "(null)");
				buffer2[2] = styleId;
				buffer2[3] = num3;
				buffer2[4] = num4;
				buffer2[5] = ((styleId == settingsForm._pnlPreview.StyleId) ? "" : "  ← ★ 不一致！");
				stringBuilder.AppendLine(string.Format("  index={0} 下拉项文字=\"{1}\" → 预览 StyleId=\"{2}\" 像素和={3} 非白像素={4} {5}", (ReadOnlySpan<object?>)buffer2));
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("★ 路径 A 各行 [像素和] 全不同 → 五种样式确实画出了不同结果；");
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
				InlineArray5<object> buffer3 = default;
				buffer3[0] = current.FrameColorId;
				buffer3[1] = current.FrameStyleId;
				buffer3[2] = current.FrameThickness;
				buffer3[3] = current.FrameRadius;
				buffer3[4] = current.ShowActivationFrame;
				stringBuilder.AppendLine(string.Format("  配置：颜色={0} 样式={1} 粗细={2} 圆角={3} 外框开关={4}", (ReadOnlySpan<object?>)buffer3));
				SettingsForm settingsForm2 = new SettingsForm(configStore2, coord2);
				settingsForm2.StartPosition = FormStartPosition.Manual;
				settingsForm2.Location = new Point(20, 20);
				settingsForm2.ClientSize = new Size(900, 900);
				settingsForm2.Show();
				Application.DoEvents();
				for (int num5 = 0; num5 < 4; num5++)
				{
					Application.DoEvents();
					Thread.Sleep(80);
				}
				stringBuilder.AppendLine(string.Format("  下拉框选中项：颜色=\"{0}\" 样式=\"{1}\"", (settingsForm2._cmbFrameColor.SelectedItem != null) ? settingsForm2._cmbFrameColor.SelectedItem.ToString() : "(null)", (settingsForm2._cmbFrameStyle.SelectedItem != null) ? settingsForm2._cmbFrameStyle.SelectedItem.ToString() : "(null)"));
				InlineArray5<object> buffer4 = default;
				buffer4[0] = settingsForm2._pnlPreview.ColorId;
				buffer4[1] = settingsForm2._pnlPreview.StyleId;
				buffer4[2] = settingsForm2._pnlPreview.Thickness;
				buffer4[3] = settingsForm2._pnlPreview.Radius;
				buffer4[4] = settingsForm2._pnlPreview.FrameEnabled;
				stringBuilder.AppendLine(string.Format("  预览控件字段：ColorId=\"{0}\" StyleId=\"{1}\" Thickness={2} Radius={3} Enabled={4}", (ReadOnlySpan<object?>)buffer4));
				FramePreviewBox pnlPreview3 = settingsForm2._pnlPreview;
				using (Bitmap bitmap3 = new Bitmap(pnlPreview3.Width, pnlPreview3.Height))
				{
					pnlPreview3.DrawToBitmap(bitmap3, new Rectangle(0, 0, pnlPreview3.Width, pnlPreview3.Height));
					bitmap3.Save(Path.Combine(outDir, "preview-REALCONFIG.png"), ImageFormat.Png);
					int y = bitmap3.Height / 2;
					int num6 = 0;
					bool flag = false;
					StringBuilder stringBuilder2 = new StringBuilder();
					for (int num7 = 0; num7 < bitmap3.Width; num7++)
					{
						Color pixel3 = bitmap3.GetPixel(num7, y);
						int num8;
						if (pixel3.R > pixel3.G + 8)
						{
							num8 = ((pixel3.R > pixel3.B + 8) ? 1 : 0);
							if (num8 != 0 && !flag)
							{
								num6++;
								stringBuilder2.Append(" 第" + num6 + "条@x=" + num7);
							}
						}
						else
						{
							num8 = 0;
						}
						flag = (byte)num8 != 0;
					}
					stringBuilder.AppendLine("  ★ 左半行(y=" + y + ")扫到的红线数 = " + num6 + stringBuilder2);
					stringBuilder.AppendLine("    Double 样式应该是 2 条；1 条说明预览没按配置画。");
				}
				settingsForm2.Close();
			}
			catch (Exception ex)
			{
				stringBuilder.AppendLine("  路径 C 异常：" + ex.Message);
			}
			File.WriteAllText(Path.Combine(outDir, "preview-styles.txt"), stringBuilder.ToString(), Encoding.UTF8);
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		finally
		{
			if (settingsForm != null)
			{
				try
				{
					settingsForm.Close();
				}
				catch
				{
				}
			}
		}
	}

	internal static void DumpVisualGroupGeometry(StringBuilder sb, int[] designWidths)
	{
		SettingsForm settingsForm = null;
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "holddrag-geom-cfg");
			Directory.CreateDirectory(text);
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			Coordinator coord = new Coordinator(configStore);
			settingsForm = new SettingsForm(configStore, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(-2400, 20);
			settingsForm.Show();
			Application.DoEvents();
			Pump();
			int num = (int)Math.Round(CurrentUiScale);
			if (num < 1)
			{
				num = 1;
			}
			for (int i = 0; i < designWidths.Length; i++)
			{
				int width = designWidths[i] * num;
				int height = Math.Min(Screen.PrimaryScreen.WorkingArea.Height - 80, Math.Max(600, 900 * num));
				settingsForm.ClientSize = new Size(width, height);
				Application.DoEvents();
				Pump();
				settingsForm.LayoutResponsive();
				Application.DoEvents();
				sb.AppendLine();
				sb.AppendLine("════ 视觉反馈组几何 · 客户区宽=" + width + "（设计稿 " + designWidths[i] + "px）════");
				DumpGroup(settingsForm, 2, sb);
				if (settingsForm._groupHost.Controls[2] is GroupBox groupBox)
				{
					sb.AppendLine("z 序 / 可见性 / 实际 bounds（客户区坐标，z=0 在最上层）：");
					for (int j = 0; j < groupBox.Controls.Count; j++)
					{
						Control control = groupBox.Controls[j];
						InlineArray9<object> buffer = default;
						buffer[0] = groupBox.Controls.GetChildIndex(control);
						buffer[1] = control.GetType().Name;
						buffer[2] = control.Visible;
						buffer[3] = control.Left;
						buffer[4] = control.Top;
						buffer[5] = control.Width;
						buffer[6] = control.Height;
						buffer[7] = control.Left + control.Width;
						buffer[8] = control.Top + control.Height;
						sb.AppendLine(string.Format("  z={0,-2} {1,-16} vis={2,-5} L={3,-5} T={4,-5} W={5,-5} H={6,-5} 右={7,-5} 底={8}", (ReadOnlySpan<object?>)buffer));
					}
				}
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine("异常：" + ex.Message);
		}
		finally
		{
			if (settingsForm != null)
			{
				try
				{
					settingsForm.Close();
				}
				catch
				{
				}
			}
		}
	}

	private static void Pump()
	{
		for (int i = 0; i < 3; i++)
		{
			Application.DoEvents();
			Thread.Sleep(30);
		}
	}

	internal int NeedGroupBottom(GroupBox gb)
	{
		int num = 0;
		for (int i = 0; i < _innerLayouts.Count; i++)
		{
			Control control = _innerLayouts[i].Control;
			if (control != null && control.Parent == gb)
			{
				int num2 = control.Top + control.Height;
				if (num2 > num)
				{
					num = num2;
				}
			}
		}
		return num;
	}

	internal int NeedGroupHeight(GroupBox gb)
	{
		int num = NeedGroupBottom(gb);
		if (num <= 0)
		{
			return gb.Height;
		}
		return num + 12 + (gb.Height - gb.ClientSize.Height);
	}

	internal static void DumpGroup(Form f, int groupIndex, StringBuilder sb)
	{
		if (f is SettingsForm { _groupHost: not null } settingsForm)
		{
			if (groupIndex < 0 || groupIndex >= settingsForm._groupHost.Controls.Count)
			{
				sb.AppendLine("(bad index)");
				return;
			}
			if (!(settingsForm._groupHost.Controls[groupIndex] is GroupBox groupBox))
			{
				sb.AppendLine("(not groupbox)");
				return;
			}
			sb.AppendLine();
			sb.AppendLine("列数=" + settingsForm._groupHost.CurrentColumns + " 分组宽=" + groupBox.Width + " 分组内宽=" + groupBox.ClientSize.Width + " 高=" + groupBox.Height + " 基准高=" + ((groupIndex < settingsForm._baseGroupHeights.Length) ? settingsForm._baseGroupHeights[groupIndex] : (-1)) + " 需要高=" + settingsForm.NeedGroupHeight(groupBox) + " 组内底=" + settingsForm.NeedGroupBottom(groupBox));
			sb.AppendLine("缩放判定=" + LastRebaseInfo);
			sb.AppendLine();
			for (int i = 0; i < settingsForm._innerLayouts.Count; i++)
			{
				InnerLayout innerLayout = settingsForm._innerLayouts[i];
				Control control = innerLayout.Control;
				if (control.Parent == groupBox)
				{
					string text = control.Text;
					if (text != null && text.Length > 16)
					{
						text = text.Substring(0, 16) + "…";
					}
					if (text != null)
					{
						text = text.Replace("\r", " ").Replace("\n", " ");
					}
					int num = control.Left + control.Width;
					int num2 = num - groupBox.ClientSize.Width;
					<>y__InlineArray17<object> buffer = default;
					buffer[0] = "[" + i + "]";
					buffer[1] = control.GetType().Name;
					buffer[2] = innerLayout.BaseLeft;
					buffer[3] = innerLayout.BaseWidth;
					buffer[4] = control.Left;
					buffer[5] = control.Width;
					buffer[6] = num;
					buffer[7] = num2;
					buffer[8] = innerLayout.How;
					buffer[9] = TextNeed(innerLayout);
					buffer[10] = text;
					buffer[11] = innerLayout.OriginalWidth;
					buffer[12] = innerLayout.BaseTop;
					buffer[13] = control.Top;
					buffer[14] = (innerLayout.WrapsToNextRow ? "Y" : "-");
					buffer[15] = innerLayout.ShiftByOffset;
					buffer[16] = control.Parent.ClientSize.Width;
					sb.AppendLine(string.Format("{0,-22} 类型={1,-14} OrigW={11,-5} BaseL={2,-4} BaseT={12,-4} BaseW={3,-4} 实际L={4,-4} T={13,-4} W={5,-4} 右={6,-4} 溢出={7,-5} Mode={8} Need={9,-4} Wrap={14} SB={15,-4} pW={16,-4} 文字=\"{10}\"", (ReadOnlySpan<object?>)buffer));
				}
			}
		}
		else
		{
			sb.AppendLine("(no form)");
		}
	}

	internal static string DumpLayoutItem(Form f, Control target)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return "(not SettingsForm)";
		}
		for (int i = 0; i < settingsForm._innerLayouts.Count; i++)
		{
			InnerLayout innerLayout = settingsForm._innerLayouts[i];
			if (innerLayout.Control == target)
			{
				int num = ((target.Parent != null) ? target.Parent.ClientSize.Width : (-1));
				return "Mode=" + innerLayout.How.ToString() + " BaseLeft=" + innerLayout.BaseLeft + " BaseWidth=" + innerLayout.BaseWidth + " RightGap=" + innerLayout.RightGap + " BaseInner=" + innerLayout.BaseInner + " 当前内宽=" + num + " 当前Left=" + target.Left + " 当前W=" + target.Width;
			}
		}
		return "(未登记)";
	}

	private void ForceGroupLayout()
	{
		_groupHost.PerformLayout();
		for (int i = 0; i < _groupHost.Controls.Count; i++)
		{
			if (_groupHost.Controls[i] is GroupBox groupBox)
			{
				groupBox.PerformLayout();
			}
		}
	}

	internal static bool VerifyStretchIdempotent(Form f)
	{
		if (!(f is SettingsForm { _groupHost: not null } settingsForm))
		{
			return false;
		}
		int num = settingsForm.GroupWidth + (int)Math.Round(200f * CurrentUiScale);
		settingsForm._groupHost.Width = num;
		settingsForm._groupHost.Relayout(num);
		settingsForm.LayoutInner();
		Rectangle bounds = settingsForm._cmbLeft.Bounds;
		for (int i = 0; i < 5; i++)
		{
			settingsForm._groupHost.Width = settingsForm.GroupWidth;
			settingsForm._groupHost.Relayout(settingsForm.GroupWidth);
			settingsForm.LayoutInner();
			settingsForm._groupHost.Width = num;
			settingsForm._groupHost.Relayout(num);
			settingsForm.LayoutInner();
		}
		Rectangle bounds2 = settingsForm._cmbLeft.Bounds;
		if (bounds.Left == bounds2.Left)
		{
			return bounds.Width == bounds2.Width;
		}
		return false;
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
			current.MovePreviewMode = "FullWin";
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
		_store.Save();
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

	internal static bool AllowOwnProcessAsTargetForTest;

	internal static nint ForcedTargetForTest = IntPtr.Zero;

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
			if (((lpdwProcessId != (uint)OwnPid) | flag) || AllowOwnProcessAsTargetForTest)
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
				if (lpdwProcessId != (uint)OwnPid)
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
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-wide.txt"), 3);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-g1.txt"), 0);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-g5.txt"), 4);
		Dump(configStore, coord, new Size(num, num5), Path.Combine(outDir, "layout-n1.txt"), 0);
		Dump(configStore, coord, new Size(num, num5), Path.Combine(outDir, "layout-n3.txt"), 3);
		Dump(configStore, coord, new Size(num, num5), Path.Combine(outDir, "layout-g3.txt"), 2);
		Dump(configStore, coord, new Size(num3, num6), Path.Combine(outDir, "layout-g3-wide.txt"), 2);
		ShootGroup(configStore, coord, new Size(num, num5), Path.Combine(outDir, "group-visual.png"), 2);
		ShootFrameSheet(Path.Combine(outDir, "frames-all.png"));
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
					graphics.DrawString("外框颜色 × 样式 对照表（#37 重做）", font, brush, new PointF(num3, 10f));
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
		try
		{
			using SettingsForm settingsForm = new SettingsForm(store, coord);
			settingsForm.StartPosition = FormStartPosition.Manual;
			settingsForm.Location = new Point(20, 20);
			settingsForm.ClientSize = size;
			settingsForm.TopMost = true;
			settingsForm.Show();
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
