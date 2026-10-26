using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag
{
	/// <summary>
	/// 更新相关窗口共用的排版小工具。
	///
	/// 这两个窗口都是**独立 Form**（要出现在任务栏里），没法复用设置窗口那套手工 DPI 缩放
	/// （那是按 UiScale 重排面板的）。这里改用 AutoSize + TableLayoutPanel，
	/// 让 WinForms 自己按字体重排——英文比中文长一大截，写死坐标一定会溢出。
	/// </summary>
	internal static class UpdateUi
	{
		public static Font BodyFont()
		{
			return new Font(SettingsForm.DesignFontFamily, 9.5f, FontStyle.Regular, GraphicsUnit.Point);
		}

		public static Font TitleFont()
		{
			return new Font(SettingsForm.DesignFontFamily, 11.5f, FontStyle.Bold, GraphicsUnit.Point);
		}

		/// <summary>自动折行的多行标签：宽度上限固定，高度随内容长。</summary>
		public static Label Wrap(string text, int maxWidth, Font font = null, Color? color = null)
		{
			return new Label
			{
				AutoSize = true,
				MaximumSize = new Size(maxWidth, 0),
				Font = font ?? BodyFont(),
				ForeColor = color ?? SystemColors.ControlText,
				Text = text ?? string.Empty,
				Margin = new Padding(0, 0, 0, 10)
			};
		}

		/// <summary>统一的根布局：单列、按内容长高、四周留白。</summary>
		public static TableLayoutPanel Root(int contentWidth)
		{
			TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				ColumnCount = 1,
				Dock = DockStyle.Fill,
				Padding = new Padding(18, 16, 18, 16)
			};
			tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			return tableLayoutPanel;
		}

		/// <summary>右对齐的按钮条：按传入顺序从右往左排。</summary>
		public static FlowLayoutPanel ButtonRow(params Button[] buttons)
		{
			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				AutoSize = true,
				AutoSizeMode = AutoSizeMode.GrowAndShrink,
				FlowDirection = FlowDirection.RightToLeft,
				WrapContents = false,
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 6, 0, 0)
			};
			for (int i = buttons.Length - 1; i >= 0; i--)
			{
				Button button = buttons[i];
				button.AutoSize = true;
				button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
				button.MinimumSize = new Size(96, 30);
				button.Margin = new Padding(8, 0, 0, 0);
				button.Padding = new Padding(10, 0, 10, 0);
				button.Font = BodyFont();
				flowLayoutPanel.Controls.Add(button);
			}
			return flowLayoutPanel;
		}
	}

	/// <summary>
	/// 「发现新版本」弹窗。
	///
	/// 和原来的托盘气泡相比，这里是**独立窗口**：ShowInTaskbar = true，
	/// 用户切到别的窗口后也能在任务栏里找回它，不会像气泡那样几秒就消失。
	/// 点「立即下载」才真正开始下载，点「稍后」什么都不做。
	/// </summary>
	internal sealed class UpdatePromptForm : Form
	{
		private readonly UpdateInfo _info;

		/// <summary>用户是否点了「立即下载」。</summary>
		public bool DownloadRequested { get; private set; }

		public UpdatePromptForm(UpdateInfo info)
		{
			_info = info;
			Text = Loc.T("发现新版本");
			FormBorderStyle = FormBorderStyle.FixedDialog;
			StartPosition = FormStartPosition.CenterScreen;
			MinimizeBox = false;
			MaximizeBox = false;
			// ★ 任务栏里要能看到这个窗口（用户可能正忙着，切走以后还得找得回来）
			ShowInTaskbar = true;
			AutoScaleMode = AutoScaleMode.Dpi;
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			Font = UpdateUi.BodyFont();
			BackColor = SystemColors.Window;
			try
			{
				Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
				if (icon != null)
				{
					Icon = icon;
				}
			}
			catch
			{
			}

			TableLayoutPanel tableLayoutPanel = UpdateUi.Root(430);
			tableLayoutPanel.Controls.Add(UpdateUi.Wrap(Loc.T("发现新版本 ") + info.Tag, 430, UpdateUi.TitleFont()));
			tableLayoutPanel.Controls.Add(UpdateUi.Wrap(
				Loc.T("当前版本：v") + AppInfo.Version + "\r\n" +
				Loc.T("最新版本：") + info.Tag + "\r\n\r\n" +
				Loc.T("现在下载并更新吗？下载完成后会自动替换并重启程序。"), 430));
			tableLayoutPanel.Controls.Add(UpdateUi.Wrap(Loc.T("也可以在浏览器里手动下载：") + info.PageUrl, 430, UpdateUi.BodyFont(), SystemColors.GrayText));

			Button download = new Button
			{
				Text = Loc.T("立即下载")
			};
			download.Click += delegate
			{
				DownloadRequested = true;
				Close();
			};
			Button later = new Button
			{
				Text = Loc.T("稍后"),
				DialogResult = DialogResult.Cancel
			};
			Button openPage = new Button
			{
				Text = Loc.T("打开发布页")
			};
			openPage.Click += delegate
			{
				try
				{
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.PageUrl)
					{
						UseShellExecute = true
					});
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			};
			tableLayoutPanel.Controls.Add(UpdateUi.ButtonRow(openPage, later, download));

			Controls.Add(tableLayoutPanel);
			AcceptButton = download;
			CancelButton = later;
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			try
			{
				Activate();
				BringToFront();
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// 下载窗口。
	///
	/// 三条硬要求（对应反馈）：
	///   1) **任务栏里必须有它** —— ShowInTaskbar = true，且不设 Owner，独立成一个任务栏按钮；
	///   2) **取消要能真的中断** —— 取消按钮 → CancellationTokenSource.Cancel()，
	///      下载线程每读一块就检查一次，读到取消立刻退出并删掉 .part，不会"点了取消还在后台偷偷下完"；
	///   3) 下载期间界面不假死 —— 下载在后台线程，进度通过 BeginInvoke 回到 UI 线程。
	/// </summary>
	internal sealed class DownloadForm : Form
	{
		/// <summary>下载窗口的结局，供调用方（设置页状态文字）如实回显。</summary>
		internal enum Outcome
		{
			/// <summary>还没结束（窗口被强行关掉等）。</summary>
			None,
			/// <summary>用户取消。</summary>
			Cancelled,
			/// <summary>下载或替换失败。</summary>
			Failed,
			/// <summary>已替换并请求重启。</summary>
			Applied
		}

		/// <summary>本次下载的结果。</summary>
		internal Outcome Result { get; private set; }

		private readonly UpdateInfo _info;

		private readonly ProgressBar _bar;

		private readonly Label _lblTitle;

		private readonly Label _lblDetail;

		private readonly Button _btnCancel;

		private readonly Button _btnPage;

		private readonly CancellationTokenSource _cts = new CancellationTokenSource();

		private bool _finished;

		/// <summary>只排版不下载：给 --langshot 截图用，避免"看一眼布局就真的开始下载"。</summary>
		private readonly bool _previewOnly;

		public DownloadForm(UpdateInfo info)
			: this(info, previewOnly: false)
		{
		}

		internal DownloadForm(UpdateInfo info, bool previewOnly)
		{
			_info = info;
			_previewOnly = previewOnly;
			Text = Loc.T("正在下载更新");
			FormBorderStyle = FormBorderStyle.FixedDialog;
			StartPosition = FormStartPosition.CenterScreen;
			MinimizeBox = true;
			MaximizeBox = false;
			ShowInTaskbar = true;
			ControlBox = true;
			AutoScaleMode = AutoScaleMode.Dpi;
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			Font = UpdateUi.BodyFont();
			BackColor = SystemColors.Window;
			try
			{
				Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
				if (icon != null)
				{
					Icon = icon;
				}
			}
			catch
			{
			}

			TableLayoutPanel tableLayoutPanel = UpdateUi.Root(430);
			_lblTitle = UpdateUi.Wrap(Loc.T("正在下载 ") + info.Tag + "…", 430, UpdateUi.TitleFont());
			_bar = new ProgressBar
			{
				AutoSize = false,
				Height = 18,
				MinimumSize = new Size(430, 18),
				Minimum = 0,
				Maximum = 1000,
				Style = ProgressBarStyle.Continuous,
				Margin = new Padding(0, 0, 0, 8)
			};
			_lblDetail = UpdateUi.Wrap(Loc.T("准备下载…"), 430);
			_btnCancel = new Button
			{
				Text = Loc.T("取消下载")
			};
			_btnCancel.Click += OnCancelClick;
			_btnPage = new Button
			{
				Text = Loc.T("打开发布页"),
				Visible = false
			};
			_btnPage.Click += delegate
			{
				try
				{
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_info.PageUrl)
					{
						UseShellExecute = true
					});
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			};

			tableLayoutPanel.Controls.Add(_lblTitle);
			tableLayoutPanel.Controls.Add(_bar);
			tableLayoutPanel.Controls.Add(_lblDetail);
			tableLayoutPanel.Controls.Add(UpdateUi.ButtonRow(_btnPage, _btnCancel));
			Controls.Add(tableLayoutPanel);
			CancelButton = _btnCancel;
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			try
			{
				Activate();
				BringToFront();
			}
			catch
			{
			}
			if (!_previewOnly)
			{
				StartDownload();
			}
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			// 用户直接点 × 也当中断：不然窗口关了、下载线程还在跑，替换照样发生
			if (!_finished)
			{
				try
				{
					_cts.Cancel();
				}
				catch
				{
				}
			}
			base.OnFormClosing(e);
		}

		private void OnCancelClick(object sender, EventArgs e)
		{
			CancelDownload();
		}

		private void CancelDownload()
		{
			_btnCancel.Enabled = false;
			_lblDetail.Text = Loc.T("正在取消…");
			try
			{
				_cts.Cancel();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		private void StartDownload()
		{
			// 替换的是"用户手里那个 exe"（启动器），不是 %LOCALAPPDATA% 里的副本
			string text = AppPaths.EntryPath;
			string text2 = Path.GetDirectoryName(text);
			string targetPath = Path.Combine(text2, Updater.TempDownloadName);
			CancellationToken token = _cts.Token;
			ThreadPool.QueueUserWorkItem(delegate
			{
				long num = 0L;
				string error;
				bool flag = Updater.Download(_info, targetPath, delegate(long done, long total)
				{
					long num2 = Environment.TickCount64;
					if (num2 - num >= 100)
					{
						num = num2;
						Post(delegate
						{
							ShowProgress(done, total);
						});
					}
				}, out error, token);
				if (token.IsCancellationRequested)
				{
					Post(FinishCancelled);
					return;
				}
				if (!flag)
				{
					Post(delegate
					{
						FinishFailed(error);
					});
					return;
				}
				Post(delegate
				{
					FinishApply(targetPath, text);
				});
			});
		}

		private void Post(Action action)
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

		private void ShowProgress(long done, long total)
		{
			if (_finished || IsDisposed)
			{
				return;
			}
			double num = (double)done / 1048576.0;
			if (total > 0)
			{
				double num2 = (double)total / 1048576.0;
				int num3 = (int)Math.Round((double)done * 100.0 / (double)total);
				_bar.Value = Math.Max(0, Math.Min(1000, num3 * 10));
				// 括号跟着语言走：中文用全角、英文用半角，避免「42%（1.2 / 3.4 MB）」这种混排
				_lblDetail.Text = Loc.T("正在下载…") + " " + num3 + "%" + (Loc.IsEnglish ? " (" : "（") + $"{num:F1} / {num2:F1} MB" + (Loc.IsEnglish ? ")" : "）");
			}
			else
			{
				_bar.Style = ProgressBarStyle.Marquee;
				_lblDetail.Text = Loc.T("正在下载…已接收 ") + $"{num:F1} MB";
			}
		}

		private void FinishCancelled()
		{
			_finished = true;
			Result = Outcome.Cancelled;
			Log.Info("用户取消了更新下载（" + _info.Tag + "）");
			MessageBox.Show(this, Loc.T("已取消下载，程序保持当前版本。"), Loc.T("下载已取消"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			Close();
		}

		private void FinishFailed(string error)
		{
			_finished = true;
			Result = Outcome.Failed;
			_bar.Style = ProgressBarStyle.Continuous;
			_bar.Value = 0;
			_btnCancel.Text = Loc.T("关闭");
			_btnCancel.Enabled = true;
			_btnCancel.Click -= OnCancelClick;
			_btnCancel.Click += delegate
			{
				Close();
			};
			_btnPage.Visible = true;
			_lblTitle.Text = Loc.T("下载失败");
			_lblDetail.Text = error;
			MessageBox.Show(this, Loc.T("下载失败：") + "\r\n\r\n" + error + "\r\n\r\n" + Loc.T("可手动到发布页下载：") + "\r\n" + _info.PageUrl, Loc.T("更新失败"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}

		private void FinishApply(string downloadedPath, string exePath)
		{
			_finished = true;
			_bar.Value = 1000;
			_lblTitle.Text = Loc.T("下载完成，正在替换并重启…");
			_lblDetail.Text = Loc.T("新版本已就位，程序将退出后自动启动新版本。");
			string error;
			bool flag = Updater.ApplyAndRestart(downloadedPath, exePath, out error);
			if (flag)
			{
				Result = Outcome.Applied;
				MessageBox.Show(this, Loc.T("更新已下载完成。") + "\r\n\r\n" + Loc.T("点击「确定」后程序将退出，自动替换为新版本并重新启动。"), Loc.T("更新就绪"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				TrayContext.RequestExitForUpdate();
				return;
			}
			Result = Outcome.Failed;
			_btnPage.Visible = true;
			_lblTitle.Text = Loc.T("替换失败");
			_lblDetail.Text = error;
			MessageBox.Show(this, Loc.T("已下载新版本，但自动替换失败：") + "\r\n\r\n" + error + "\r\n\r\n" + Loc.T("新版本已保存在：") + "\r\n" + downloadedPath + "\r\n\r\n" + Loc.T("可手动改名覆盖，或到发布页重新下载：") + "\r\n" + _info.PageUrl, Loc.T("更新失败"), MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}
}
