using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag
{
    /// <summary>托盘程序主体：图标、菜单、钩子生命周期。</summary>
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

        /// <summary>
        /// store 由 Program.Main 传入 —— 因为"要不要提权重启"本身就是配置项，
        /// 配置必须在提权决策【之前】读出来（见 Program.Main 的注释）。
        /// </summary>
        public TrayContext(string dir, ConfigStore store, EventWaitHandle showEvent)
        {
            _dir = dir;
            _store = store;

            bool firstRun = !File.Exists(Path.Combine(dir, "config.json"));

            // 开机自启的真实状态以系统为准，不能只信配置文件里的"意愿值"，
            // 否则用户可能看到打了勾、实际却没注册。
            try
            {
                string actual = AutoStart.Detect();
                if (actual != _store.Current.AutoStart)
                {
                    Log.Info("开机自启实际状态为 " + actual + "，与配置记录的 "
                             + _store.Current.AutoStart + " 不一致，以实际为准");
                    _store.Current.AutoStart = actual;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }

            _coord = new Coordinator(_store);
            _coord.PrecreateOverlays();

            _hooks = new HookEngine(_coord);
            bool hooksOk = _hooks.Install();

            // 钩子被系统静默摘除之后，程序不会报错、只会"看起来在工作"，
            // 所以必须挂一个看门狗来判定并重装 —— 详见 HookWatchdog 的注释。
            _watchdog = new HookWatchdog(_hooks);
            _watchdog.Start();

            _iconNormal = LoadAppIcon();
            _iconPaused = CreatePausedIcon();
            _tray = new NotifyIcon();
            _menu = new ContextMenuStrip();

            BuildTray();
            ApplyConfig(true);

            // 后台线程收到“第二个实例要求显示设置”的信号后，必须切回 UI 线程再动窗口
            _marshal = new Control();
            _marshal.CreateControl();

            if (showEvent != null)
            {
                try
                {
                    _showWait = ThreadPool.RegisterWaitForSingleObject(
                        showEvent,
                        OnShowSignal,
                        null,
                        Timeout.Infinite,
                        false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }

            if (Program.SmokeMode)
            {
                Log.Info("冒烟模式：2 秒后打开设置窗口，9 秒后自动退出");
                var open = new System.Windows.Forms.Timer();
                open.Interval = 2000;
                open.Tick += (s, e) => { open.Stop(); ShowSettings(); };
                open.Start();

                var quit = new System.Windows.Forms.Timer();
                quit.Interval = 9000;
                quit.Tick += (s, e) => { quit.Stop(); Log.Info("冒烟测试完成，自动退出"); ExitApp(); };
                quit.Start();
            }

            if (!hooksOk)
            {
                ShowBalloon("钩子安装失败",
                    "无法安装全局键鼠钩子（错误码 " + _hooks.LastError + "），程序将无法工作。"
                    + "常见原因是被杀软/安全软件拦截，请把本程序加入信任列表后重启。",
                    ToolTipIcon.Error);
            }
            else if (firstRun)
            {
                ShowBalloon("长按拖窗已启动",
                    "长按 " + KeysUtil.DisplayName(_store.Current.TriggerKeyId)
                    + " " + _store.Current.HoldMs + " 毫秒后：左键拖动移动窗口、右键拖动调整大小、中键最大化/还原。"
                    + "双击托盘图标可打开设置。",
                    ToolTipIcon.Info);
            }
        }

        // ── 托盘与菜单 ──────────────────────────────────────────

        private void BuildTray()
        {
            _tray.Icon = _iconNormal;
            _tray.Text = "长按拖窗";
            _tray.Visible = true;
            _tray.DoubleClick += (s, e) => ShowSettings();

            _menu.Items.Add(new ToolStripMenuItem("设置…", null, (s, e) => ShowSettings()));

            _miPause.Click += (s, e) => TogglePause();
            _menu.Items.Add(_miPause);

            _miAutoStart.Click += (s, e) => ToggleAutoStart();
            _menu.Items.Add(_miAutoStart);

            _miElevate.Click += (s, e) => RestartAsAdmin();
            _menu.Items.Add(_miElevate);

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miStatus);
            _menu.Items.Add(new ToolStripMenuItem("打开配置文件夹", null, (s, e) => OpenPath(_dir)));
            _menu.Items.Add(new ToolStripMenuItem("查看日志", null, (s, e) => OpenPath(Log.Path)));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("退出", null, (s, e) => ExitApp()));

            // 状态是每次右键时才现算的，所以菜单一打开就刷新一次，看到的一定是当时的数字。
            _menu.Opening += (s, e) => UpdateStatusText();
            _tray.ContextMenuStrip = _menu;

            UpdateStatusText();   // 先填一次，避免菜单首次展开前是空白
        }

        /// <summary>
        /// 把钩子看门狗的累计检查次数 / 救援次数暴露出来。
        /// 「救援次数」持续上涨说明系统里有别的东西在拖慢钩子回调，是排查"时灵时不灵"的第一手依据。
        /// </summary>
        private void UpdateStatusText()
        {
            _miStatus.Enabled = false;   // 纯展示，点了也没事做
            if (_watchdog == null) { _miStatus.Text = "钩子状态：—"; return; }

            // ReinstallCount 才是"实际重装过几次"的总数（看门狗救援时也会走 Reinstall），
            // Rescues 只统计看门狗这一条路径，两个数不能相加，否则会重复计。
            int checks = _watchdog.Checks, reinstalls = _hooks.ReinstallCount;
            _miStatus.Text = reinstalls == 0
                ? string.Format("钩子状态：正常（已检查 {0} 次）", checks)
                : string.Format("钩子状态：已自动恢复 {0} 次（检查 {1} 次）", reinstalls, checks);
        }

        private void UpdateMenuState()
        {
            var c = _store.Current;

            _miPause.Checked = _coord.Paused;
            _miPause.Text = _coord.Paused ? "恢复使用" : "暂停使用";

            _miAutoStart.Checked = c.AutoStart != AutoStartModes.None;
            _miAutoStart.Text = c.AutoStart == AutoStartModes.Registry
                ? "开机启动（注册表）"
                : "开机启动（计划任务）";

            _miElevate.Visible = Program.LaunchedWithoutElevation;

            _tray.Icon = _coord.Paused ? _iconPaused : _iconNormal;
            _tray.Text = _coord.Paused
                ? "长按拖窗（已暂停）"
                : "长按拖窗：" + KeysUtil.DisplayName(c.TriggerKeyId);
        }

        private void TogglePause()
        {
            bool paused = !_coord.Paused;
            _coord.SetPaused(paused);
            UpdateMenuState();

            // 三重反馈（勾选 + 图标 + 气泡），保证用户一眼能看出当前究竟是开着还是停着。
            ShowBalloon(
                paused ? "已暂停使用" : "已恢复使用",
                paused
                    ? "拖拽功能已停止，触发键恢复成它本来的用法。右键托盘图标点「恢复使用」即可重新开启。"
                    : "长按 " + KeysUtil.DisplayName(_store.Current.TriggerKeyId) + " 后可以再用鼠标拖窗口了。",
                ToolTipIcon.Info);
        }

        private void ToggleAutoStart()
        {
            var c = _store.Current;
            string next = c.AutoStart == AutoStartModes.None ? AutoStartModes.TaskScheduler : AutoStartModes.None;
            c.AutoStart = next;
            _store.Save();
            ApplyConfig(false);

            string msg;
            AutoStart.Apply(next, AppPaths.ExePath, out msg);
            if (!string.IsNullOrEmpty(msg))
            {
                ShowBalloon("开机自启", msg, ToolTipIcon.Info);
            }
        }

        private void RestartAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = AppPaths.ExePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                Process.Start(psi);
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
                if (string.IsNullOrEmpty(path)) return;
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        /// <summary>配置变化后重新读取并刷新菜单文案。</summary>
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

        // ── 设置窗口 ────────────────────────────────────────────

        private void OnShowSignal(object state, bool timedOut)
        {
            if (timedOut) return;
            try
            {
                _marshal.BeginInvoke(new Action(ShowSettings));
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
                    _settings.FormClosed += (s, e) => { _settings = null; };
                    _settings.Show();
                }
                else
                {
                    if (!_settings.Visible) _settings.Show();
                    if (_settings.WindowState == FormWindowState.Minimized) _settings.WindowState = FormWindowState.Normal;
                    _settings.Activate();
                }
                _settings.BringToFront();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        // ── 图标 ────────────────────────────────────────────────

        private static Icon LoadAppIcon()
        {
            try
            {
                var ico = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
                if (ico != null) return ico;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            return SystemIcons.Application;
        }

        /// <summary>“已暂停”状态图标：灰底加两道竖杠，运行时绘制，随 DPI 自适应。</summary>
        private static Icon CreatePausedIcon()
        {
            try
            {
                var size = SystemInformation.SmallIconSize;
                int w = Math.Max(16, size.Width);
                int h = Math.Max(16, size.Height);

                using (var bmp = new Bitmap(w, h))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);

                        using (var back = new SolidBrush(Color.FromArgb(120, 122, 132)))
                        {
                            g.FillEllipse(back, 0, 0, w - 1, h - 1);
                        }
                        using (var bar = new SolidBrush(Color.White))
                        {
                            int bw = Math.Max(2, w / 6);
                            int bh = Math.Max(5, (int)(h * 0.46));
                            int y = (h - bh) / 2;
                            g.FillRectangle(bar, w / 2 - bw - 1, y, bw, bh);
                            g.FillRectangle(bar, w / 2 + 1, y, bw, bh);
                        }
                    }

                    IntPtr hIcon = bmp.GetHicon();
                    try
                    {
                        // 必须 Clone：FromHandle 返回的 Icon 不拥有句柄，销毁后不能再用
                        return (Icon)Icon.FromHandle(hIcon).Clone();
                    }
                    finally
                    {
                        Native.DestroyIcon(hIcon);
                    }
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

        // ── 退出 ────────────────────────────────────────────────

        private void ExitApp()
        {
            Log.Info("退出");
            Cleanup();
            ExitThread();
        }

        private void Cleanup()
        {
            if (_cleaned) return;
            _cleaned = true;

            try { _store.Changed -= OnConfigChanged; } catch { }

            if (_showWait != null)
            {
                try { _showWait.Unregister(null); } catch { }
                _showWait = null;
            }

            if (_watchdog != null)
            {
                try { _watchdog.Dispose(); } catch { }
            }

            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            try { _marshal.Dispose(); } catch { }
            try { _hooks.Dispose(); } catch { }
            try { _coord.Dispose(); } catch { }
            try { if (_settings != null && !_settings.IsDisposed) _settings.Dispose(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Cleanup();
            base.Dispose(disposing);
        }
    }
}
