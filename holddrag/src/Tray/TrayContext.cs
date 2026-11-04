// HoldDrag · Tray/TrayContext.cs

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

internal sealed class TrayContext : ApplicationContext
{
	private readonly string _dir;

	private readonly ConfigStore _store;

	private readonly Coordinator _coord;

	private readonly HookEngine _hooks;

	private readonly HookWatchdog _watchdog;

	private readonly NotifyIcon _tray;

	/// <summary>v1.7.0：气泡里要打开的下载地址（没填令牌时点气泡即打开）。</summary>
	private string _balloonUpdateUrl;

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

	/// <summary>托盘菜单里那几个字段项的 Click 是否已接线（重建菜单时不能重复 +=）。</summary>
	private bool _menuHandlersWired;

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
		// 钩子被看门狗真正重装时：重装缝隙里的按键/鼠标事件都丢了，
		// 必须把鼠标三本账、触发状态和欠着的合成抬起一起收拾干净。
		_hooks.HooksReinstalled += delegate
		{
			// 只清「合成按下」的欠账并补发抬起；触发状态复位保持 v1.10.0 原样，
			// 免得把「激活瞬间抢占链头」这种计划内重装也当成事件丢失。
			_coord.OnHooksReinstalled();
			_coord.Trigger.Reset("钩子重装");
		};
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
		// 原生菜单是系统浅灰、方角、SystemFonts —— 和设置页完全两个世界。
		// 这里换自绘渲染器：圆角高亮、跟着深色/浅色主题走、字体和设置页同族。
		AuroraTheme.Refresh();
		_menu.Renderer = new AuroraMenuRenderer();
		_menu.ShowImageMargin = false;
		_menu.ShowCheckMargin = true;
		_menu.Font = AuroraTheme.Font(13f);
		_menu.BackColor = AuroraTheme.Surface;
		_menu.ForeColor = AuroraTheme.TextPrimary;
		_menu.Padding = new Padding(4, 6, 4, 6);
		_menu.Opening += delegate
		{
			try
			{
				AuroraTheme.Refresh();
				_menu.BackColor = AuroraTheme.Surface;
				_menu.ForeColor = AuroraTheme.TextPrimary;
			}
			catch
			{
			}
		};
		BuildTray();
		ApplyConfig(initial: true);
		_marshal = new Control();
		_marshal.CreateControl();
		// 语言切换后托盘菜单要整份重建（菜单项文字是创建时取的译文）
		Loc.Changed += OnLanguageChanged;
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
			ShowBalloon(Loc.T("钩子安装失败"), Loc.T("无法安装全局键鼠钩子（错误码 ") + _hooks.LastError + Loc.T("），程序将无法工作。常见原因是被杀软/安全软件拦截，请把本程序加入信任列表后重启。"), ToolTipIcon.Error);
		}
		else if (flag)
		{
			ShowBalloon(Loc.T("长按拖窗已启动"), Loc.T("长按 ") + KeysUtil.DisplayName(_store.Current.TriggerKeyId) + " " + _store.Current.HoldMs + Loc.T(" 毫秒后：左键拖动移动窗口、右键拖动调整大小、中键最大化/还原。双击托盘图标可打开设置。"), ToolTipIcon.Info);
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
					ShowUpdatePrompt(updateInfo);
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

	/// <summary>
	/// 启动检查发现新版本：弹一个**独立窗口**（任务栏里也有），而不是托盘气泡。
	/// 气泡几秒就消失、也点不了按钮，用户常常根本没看见；这里改成明确的「立即下载 / 稍后」。
	/// 「同一版本只提示一次」的记账写在真正弹出之前，避免用户关掉窗口后又反复被打断。
	/// </summary>
	private void ShowUpdatePrompt(UpdateInfo info)
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
					AppConfig appConfig = _store.Current;
					if (appConfig != null && !string.Equals(appConfig.LastUpdateNotified, info.Tag, StringComparison.OrdinalIgnoreCase))
					{
						appConfig.LastUpdateNotified = info.Tag;
						_store.Save(out _);
					}
					bool flag;
					using (UpdatePromptForm updatePromptForm = new UpdatePromptForm(info))
					{
						updatePromptForm.ShowDialog();
						flag = updatePromptForm.DownloadRequested;
					}
					Log.Info("启动更新提示：" + info.Tag + (flag ? "，用户选择下载" : "，用户选择稍后"));
					if (!flag)
					{
						return;
					}
					using DownloadForm downloadForm = new DownloadForm(info);
					downloadForm.ShowDialog();
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
					_balloonUpdateUrl = info.AssetUrl;
					_tray.BalloonTipTitle = Loc.T("长按拖窗：发现新版本 ") + info.Tag;
					_tray.BalloonTipText = Updater.HasToken ? (Loc.T("当前 v") + AppInfo.Version + Loc.T("。双击托盘图标打开设置，点「检查更新」即可下载并自动替换。")) : (Loc.T("当前 v") + AppInfo.Version + Loc.T("。点这个气泡用浏览器打开下载页（未填令牌，不会自动下载）。"));
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
		_tray.Text = Loc.T("长按拖窗");
		_tray.Visible = true;
		_tray.DoubleClick += (object? s, EventArgs e) =>
		{
			ShowSettings();
		};
		// v1.7.0：没填令牌时的"发现新版本"气泡点一下就打开 GitHub 下载页
		// （气泡本身显示不了可点的网址，所以整条气泡当成一个下载入口）。
		_tray.BalloonTipClicked += (object? s, EventArgs e) =>
		{
			try
			{
				string text = _balloonUpdateUrl;
				if (!string.IsNullOrEmpty(text))
				{
					Process.Start(new ProcessStartInfo(text)
					{
						UseShellExecute = true
					});
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		};
		_menu.Opening += (object? s, CancelEventArgs e) =>
		{
			UpdateStatusText();
		};
		_tray.ContextMenuStrip = _menu;
		RebuildTrayMenu();
	}

	/// <summary>
	/// 重建托盘菜单项。
	/// 菜单项的文字是在**创建那一刻**取译文的，所以切语言必须整份重建；
	/// 只清空 Items 不去 Dispose 字段项（_miPause 等），勾选状态与事件接线都保留。
	/// </summary>
	private void RebuildTrayMenu()
	{
		_menu.Items.Clear();
		_menu.Font = AuroraTheme.Font(13f);
		_menu.Items.Add(new ToolStripMenuItem(Loc.T("设置…"), null, (object? s, EventArgs e) =>
		{
			ShowSettings();
		}));
		// 字段项（暂停/自启/提权）只接线一次，重建时仅重新挂回 Items，避免重复叠加处理器
		if (!_menuHandlersWired)
		{
			_menuHandlersWired = true;
			_miPause.Click += (object? s, EventArgs e) =>
			{
				TogglePause();
			};
			_miAutoStart.Click += (object? s, EventArgs e) =>
			{
				ToggleAutoStart();
			};
			_miElevate.Click += (object? s, EventArgs e) =>
			{
				RestartAsAdmin();
			};
		}
		_menu.Items.Add(_miPause);
		_menu.Items.Add(_miAutoStart);
		_menu.Items.Add(_miElevate);
		_menu.Items.Add(new ToolStripSeparator());
		_menu.Items.Add(_miStatus);
		_menu.Items.Add(new ToolStripMenuItem(Loc.T("打开配置文件夹"), null, (object? s, EventArgs e) =>
		{
			OpenPath(_dir);
		}));
		_menu.Items.Add(new ToolStripMenuItem(Loc.T("查看日志"), null, (object? s, EventArgs e) =>
		{
			OpenPath(Log.Path);
		}));
		_menu.Items.Add(new ToolStripMenuItem(Loc.T("检查更新…"), null, (object? s, EventArgs e) =>
		{
			OpenSettingsAndCheckUpdate();
		}));
		_menu.Items.Add(new ToolStripMenuItem(Loc.T("项目主页（GitHub）"), null, (object? s, EventArgs e) =>
		{
			OpenUrlStatic(AppInfo.GitHubUrl);
		}));
		_menu.Items.Add(new ToolStripSeparator());
		_menu.Items.Add(new ToolStripMenuItem(Loc.T("退出"), null, (object? s, EventArgs e) =>
		{
			ExitApp();
		}));
		UpdateMenuState();
		UpdateStatusText();
	}

	/// <summary>语言变更（设置窗口里切了语言）：托盘菜单与图标提示要在 UI 线程重建。</summary>
	private void OnLanguageChanged()
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
					RebuildTrayMenu();
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

	private void UpdateStatusText()
	{
		_miStatus.Enabled = false;
		if (_watchdog == null)
		{
			_miStatus.Text = Loc.T("钩子状态：—");
			return;
		}
		int checks = _watchdog.Checks;
		int reinstallCount = _hooks.ReinstallCount;
		// 走 Loc.T：以前这两行是硬编码中文，切到 English 后托盘里会永远挂着中文
		_miStatus.Text = ((reinstallCount == 0)
			? string.Format(Loc.T("钩子状态：正常（已检查 {0} 次）"), checks)
			: string.Format(Loc.T("钩子状态：已自动恢复 {0} 次（检查 {1} 次）"), reinstallCount, checks));
	}

	private void UpdateMenuState()
	{
		AppConfig current = _store.Current;
		_miPause.Checked = _coord.Paused;
		_miPause.Text = (_coord.Paused ? Loc.T("恢复使用") : Loc.T("暂停使用"));
		_miAutoStart.Checked = current.AutoStart != "None";
		_miAutoStart.Text = ((current.AutoStart == "Registry") ? Loc.T("开机启动（注册表）") : Loc.T("开机启动（计划任务）"));
		_miElevate.Visible = Program.LaunchedWithoutElevation;
		_tray.Icon = (_coord.Paused ? _iconPaused : _iconNormal);
		_tray.Text = (_coord.Paused ? Loc.T("长按拖窗（已暂停）") : (Loc.T("长按拖窗：") + KeysUtil.DisplayName(current.TriggerKeyId)));
	}

	private void TogglePause()
	{
		bool flag = !_coord.Paused;
		_coord.SetPaused(flag);
		UpdateMenuState();
		ShowBalloon(flag ? Loc.T("已暂停使用") : Loc.T("已恢复使用"), flag ? Loc.T("拖拽功能已停止，触发键恢复成它本来的用法。右键托盘图标点「恢复使用」即可重新开启。") : (Loc.T("长按 ") + KeysUtil.DisplayName(_store.Current.TriggerKeyId) + Loc.T(" 后可以再用鼠标拖窗口了。")), ToolTipIcon.Info);
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
			ShowBalloon(Loc.T("开机自启切换失败"), Loc.T("配置没能写入磁盘：") + saveError + Loc.T("（本次切换没有生效，重启后会恢复原样；请检查配置目录权限或磁盘空间）"), ToolTipIcon.Warning);
			return;
		}
		ApplyConfig(initial: false);
		AutoStart.Apply(mode, AppPaths.EntryPath, out var message);
		if (!string.IsNullOrEmpty(message))
		{
			ShowBalloon(Loc.T("开机自启"), message, ToolTipIcon.Info);
		}
	}

	private void RestartAsAdmin()
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = AppPaths.ExePath,
				// 提权重启后仍要知道外层 exe 在哪；
				// --take-over 让新进程等本进程释放单实例锁后再接管（新先起、旧后关），否则重启完一个进程都不剩
				Arguments = "--host " + Updater.QuoteArg(AppPaths.EntryPath) + " " + Updater.ArgTakeOver,
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
		// 令牌是给后台线程读的：配置每次加载/保存都同步一次，保证「填完立刻生效」且不读到旧值
		Updater.Token = Updater.NormalizeToken(_store.Current?.GitHubToken);
		if (initial)
		{
			_store.Changed += OnConfigChanged;
		}
	}

	private void OnConfigChanged()
	{
		try
		{
			Updater.Token = Updater.NormalizeToken(_store.Current?.GitHubToken);
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
