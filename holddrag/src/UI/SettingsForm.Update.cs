// HoldDrag · UI/SettingsForm.Update.cs

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

internal sealed partial class SettingsForm
{
	// ---------- v1.7.0：GitHub 令牌的保存与状态回显 ----------

	/// <summary>上一次"验证通过"的令牌，用来在输入框改动后提示需要重新验证。</summary>
	private string _verifiedToken;

	/// <summary>
	/// 「验证令牌」：后台线程真的向 GitHub 请求一次（就是检查更新用的那条接口），
	/// 结果写进单独的验证标签——不覆盖「已生效 / 待保存」那行，也不改动配置。
	/// </summary>
	private void VerifyTokenNow()
	{
		string token = Updater.NormalizeToken(_txtToken.Text);
		if (string.IsNullOrEmpty(token))
		{
			_lblTokenVerify.ForeColor = UiTheme.WarnText;
			_lblTokenVerify.Text = Loc.T("先填令牌再验证");
			return;
		}
		_btnVerifyToken.Enabled = false;
		_lblTokenVerify.ForeColor = UiTheme.Hint;
		_lblTokenVerify.Text = Loc.T("正在向 GitHub 验证…");
		ThreadPool.QueueUserWorkItem(delegate
		{
			string message;
			Updater.TokenCheckResult result = Updater.VerifyToken(token, out message);
			PostUpdateAction(delegate
			{
				try
				{
					if (_btnVerifyToken != null && !_btnVerifyToken.IsDisposed)
					{
						_btnVerifyToken.Enabled = true;
					}
					if (_lblTokenVerify == null || _lblTokenVerify.IsDisposed)
					{
						return;
					}
					if (result == Updater.TokenCheckResult.Valid)
					{
						_verifiedToken = token;
					}
					string text = message;
					// 验证通过但还没落盘：明说一句，免得用户以为已经生效
					if (result == Updater.TokenCheckResult.Valid && !string.Equals(token, Updater.NormalizeToken(_store.Current?.GitHubToken), StringComparison.Ordinal))
					{
						text += Loc.T("（还没保存：点「保存令牌」才会生效）");
					}
					_lblTokenVerify.ForeColor = TokenCheckColor(result);
					_lblTokenVerify.Text = text;
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			});
		});
	}

	private static Color TokenCheckColor(Updater.TokenCheckResult result)
	{
		switch (result)
		{
		case Updater.TokenCheckResult.Valid:
			return UiTheme.Success;
		case Updater.TokenCheckResult.NotConfigured:
		case Updater.TokenCheckResult.RateLimited:
			return UiTheme.WarnText;
		default:
			return UiTheme.Danger;
		}
	}

	/// <summary>
	/// 只保存令牌：克隆当前已落盘的配置、只改这一项再写盘，
	/// 避免把界面上其它还没点「保存并生效」的半成品改动一起提交。
	/// </summary>
	private void SaveToken()
	{
		try
		{
			string token = Updater.NormalizeToken(_txtToken.Text);
			_txtToken.Text = token;
			AppConfig appConfig = _store.Current?.Clone() ?? new AppConfig();
			appConfig.GitHubToken = token;
			if (!_store.Apply(appConfig, out string error))
			{
				_lblTokenState.ForeColor = UiTheme.Danger;
				_lblTokenState.Text = Loc.T("保存失败：") + error;
				return;
			}
			if (_edit != null)
			{
				_edit.GitHubToken = token;
			}
			// 立刻生效，不等下一次启动：后台检查更新读的就是这个静态值
			Updater.Token = token;
			RefreshTokenState();
			Log.Info("GitHub 令牌已" + (string.IsNullOrEmpty(token) ? "清除" : "保存") + "（长度 " + token.Length + "，不记录内容）");
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			_lblTokenState.ForeColor = UiTheme.Danger;
			_lblTokenState.Text = Loc.T("保存令牌失败：") + ex.Message;
		}
	}

	/// <summary>把输入框里的值与已保存的值对比，回显「已生效 / 待保存 / 未填写」——用户一眼能看出到底生效了没有。</summary>
	private void RefreshTokenState()
	{
		try
		{
			if (_lblTokenState == null || _lblTokenState.IsDisposed)
			{
				return;
			}
			string text = Updater.NormalizeToken(_txtToken.Text);
			string text2 = Updater.NormalizeToken(_store.Current?.GitHubToken);
			// 令牌改动过 → 之前的验证结论作废（否则会出现"看着已验证、其实验的是旧令牌"）
			if (_lblTokenVerify != null && !_lblTokenVerify.IsDisposed && !string.Equals(text, _verifiedToken, StringComparison.Ordinal))
			{
				_lblTokenVerify.ForeColor = UiTheme.Hint;
				_lblTokenVerify.Text = (_verifiedToken == null) ? Loc.T("尚未验证") : Loc.T("令牌已改动，请重新验证");
			}
			bool flag = string.Equals(text, text2, StringComparison.Ordinal);
			if (string.IsNullOrEmpty(text))
			{
				_lblTokenState.ForeColor = flag ? UiTheme.WarnText : UiTheme.Hint;
				_lblTokenState.Text = (flag ? Loc.T("未填写：检查更新走匿名接口") : Loc.T("已清空，点「保存令牌」生效"));
				return;
			}
			_lblTokenState.ForeColor = flag ? UiTheme.Success : UiTheme.Hint;
			// 不显示令牌的任何字符（哪怕只露前 4 后 4）：旁人扫一眼屏幕就能记下片段，
			// 这里只说"生效没生效"。
			_lblTokenState.Text = (flag ? Loc.T("已生效（接口限额 5000 次/小时）") : Loc.T("待保存：点「保存令牌」后生效"));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void CheckForUpdates(bool userInitiated)
	{
		if (_updateBusy)
		{
			if (userInitiated)
			{
				SetUpdateStatus(Loc.T("正在检查，请稍候…"), UiTheme.Hint);
			}
			return;
		}
		_updateBusy = true;
		SetUpdateStatus(Loc.T("正在检查更新…"), UiTheme.Hint);
		ThreadPool.QueueUserWorkItem(delegate
		{
			string error;
			bool upToDate;
			string note;
			UpdateInfo updateInfo = Updater.CheckLatest(out error, out upToDate, out note);
			PostUpdateAction(delegate
			{
				_updateBusy = false;
				if (updateInfo != null)
				{
					OnUpdateFound(updateInfo, note);
					return;
				}
				if (upToDate)
				{
					SetUpdateStatus(Loc.T("已是最新版本（v") + AppInfo.Version + Loc.Close + NoteSuffix(note), UiTheme.Success);
					if (userInitiated)
					{
						MessageBox.Show(this, Loc.T("当前已是最新版本 v") + AppInfo.Version + "。" + NoteSuffix(note), Loc.T("检查更新"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
					}
					return;
				}
				SetUpdateStatus(Loc.T("检查失败：") + error, UiTheme.Danger);
				if (userInitiated)
				{
					MessageBox.Show(this, Loc.T("检查更新失败：\r\n\r\n") + error + Loc.T("\r\n\r\n你也可以手动打开发布页查看：\r\n") + AppInfo.ReleasesUrl, Loc.T("检查更新"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
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

	/// <summary>v1.7.0：发现新版本后的动作。</summary>
	internal enum UpdateAction
	{
		/// <summary>只提示（并给出可点击的下载地址），不自动下载。</summary>
		PromptOnly,
		/// <summary>自动下载并替换。</summary>
		AutoDownload
	}

	/// <summary>
	/// v1.7.0 的旧规则：没填令牌 → 只提示；填了令牌 → 自动下载。
	/// v1.9.0 起下载一律先弹 UpdatePromptForm 让用户点「立即下载」，本函数不再决定 UI 走向，
	/// 保留它是为了自检仍能断言"令牌有无"这条纯逻辑，也方便将来回退。
	/// </summary>
	internal static UpdateAction DecideUpdateAction(bool hasToken)
	{
		return hasToken ? UpdateAction.AutoDownload : UpdateAction.PromptOnly;
	}

	/// <summary>把「这次是怎么查的」的补充说明拼进状态文字（没有就返回空串）。</summary>
	internal static string NoteSuffix(string note)
	{
		return Loc.InParens(note);
	}

	private void OnUpdateFound(UpdateInfo info)
	{
		OnUpdateFound(info, null);
	}

	private void OnUpdateFound(UpdateInfo info, string note)
	{
		SetUpdateStatus(Loc.T("发现新版本 ") + info.Tag + Loc.T("（当前 v") + AppInfo.Version + Loc.Close + NoteSuffix(note), UiTheme.WarnText);
		// v1.8.1：统一走「弹窗 → 下载窗口」这条路径。
		// 没填令牌也允许下载：发布资产直链不吃接口限额，用户点「立即下载」就是明确同意。
		PromptAndDownload(info);
	}

	/// <summary>
	/// 弹「发现新版本」窗口；用户点「立即下载」才进下载窗口。
	/// 这个窗口是独立的（ShowInTaskbar = true），不会像气泡那样几秒就消失，
	/// 用户切到别的窗口后还能在任务栏里找回来。
	/// </summary>
	private void PromptAndDownload(UpdateInfo info)
	{
		bool flag;
		try
		{
			using UpdatePromptForm updatePromptForm = new UpdatePromptForm(info);
			updatePromptForm.ShowDialog(this);
			flag = updatePromptForm.DownloadRequested;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			flag = false;
		}
		if (!flag)
		{
			SetUpdateStatus(Loc.T("已跳过 ") + info.Tag + Loc.T("；需要时再点「检查更新」即可"), UiTheme.Hint);
			return;
		}
		DownloadAndApply(info);
	}

	/// <summary>
	/// v1.7.0：没填令牌时的提示正文。抽成静态方法是为了让自检能断言"只提示、程序内不下载"这条规则
	/// （对话框本身没法在自检里点）。
	/// </summary>
	internal static string BuildNoTokenNotice()
	{
		return Loc.T("还没有填写 GitHub 个人访问令牌，所以这次只是在程序里提示，不会下载到本机。\r\n点下面的链接用浏览器下载；想以后自动下载并替换，就到「关于 → GitHub 访问令牌」填好令牌再点「检查更新」。");
	}

	/// <summary>
	/// v1.7.0：没填令牌时的更新提示——只展示版本与 GitHub 下载地址，程序内绝不下载。
	/// 下载地址用 LinkLabel（可点击、可 Tab 聚焦、回车即打开），另有「用浏览器打开」「复制链接」兜底，
	/// 浏览器打不开时也能把地址拷走。
	/// </summary>
	internal void ShowUpdateLinkDialog(UpdateInfo info)
	{
		using Form form = new Form();
		form.Text = Loc.T("发现新版本");
		form.FormBorderStyle = FormBorderStyle.FixedDialog;
		form.StartPosition = FormStartPosition.CenterParent;
		form.MinimizeBox = false;
		form.MaximizeBox = false;
		form.ShowInTaskbar = false;
		form.BackColor = UiTheme.Surface;
		form.ForeColor = UiTheme.TextPrimary;
		form.Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel);
		form.AutoScaleMode = AutoScaleMode.Dpi;
		int num = Sc(20);
		int num2 = Sc(520);
		Label label = new Label
		{
			AutoSize = false,
			BackColor = Color.Transparent,
			ForeColor = UiTheme.TextPrimary,
			Font = new Font(DesignFontFamily, Sc(15), FontStyle.Bold, GraphicsUnit.Pixel),
			Text = Loc.T("发现新版本 ") + info.Tag + Loc.T("（当前 v") + AppInfo.Version + Loc.Close
		};
		Label label2 = new Label
		{
			AutoSize = false,
			BackColor = Color.Transparent,
			ForeColor = UiTheme.TextSecondary,
			Text = BuildNoTokenNotice()
		};
		Label label3 = new Label
		{
			AutoSize = false,
			BackColor = Color.Transparent,
			ForeColor = UiTheme.Hint,
			Text = Loc.T("下载地址（点击或按回车打开）")
		};
		LinkLabel linkLabel = new LinkLabel
		{
			AutoSize = false,
			BackColor = Color.Transparent,
			ForeColor = UiTheme.Accent,
			LinkColor = UiTheme.Accent,
			ActiveLinkColor = UiTheme.AccentPressed,
			VisitedLinkColor = UiTheme.Accent,
			Text = info.AssetUrl,
			AccessibleName = Loc.T("GitHub 下载地址"),
			AccessibleDescription = Loc.T("在浏览器中打开该链接，下载新版本 ") + info.Tag
		};
		linkLabel.LinkClicked += delegate
		{
			OpenUrl(info.AssetUrl);
		};
		LinkLabel linkLabel2 = new LinkLabel
		{
			AutoSize = false,
			BackColor = Color.Transparent,
			ForeColor = UiTheme.TextTertiary,
			LinkColor = UiTheme.TextTertiary,
			ActiveLinkColor = UiTheme.Accent,
			VisitedLinkColor = UiTheme.TextTertiary,
			Text = Loc.T("发布页：") + info.PageUrl,
			AccessibleName = Loc.T("GitHub 发布页地址")
		};
		linkLabel2.LinkClicked += delegate
		{
			OpenUrl(info.PageUrl);
		};
		FlatButton flatButton = new FlatButton
		{
			Text = Loc.T("复制链接")
		};
		flatButton.Click += delegate
		{
			try
			{
				Clipboard.SetText(info.AssetUrl);
				flatButton.Text = Loc.T("已复制");
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		};
		FlatButton flatButton2 = new FlatButton
		{
			Text = Loc.T("用浏览器打开"),
			Primary = true
		};
		flatButton2.Click += delegate
		{
			OpenUrl(info.AssetUrl);
		};
		FlatButton flatButton3 = new FlatButton
		{
			Text = Loc.T("关闭")
		};
		flatButton3.Click += delegate
		{
			form.Close();
		};
		form.AcceptButton = flatButton2;
		form.CancelButton = flatButton3;
		int num3 = num;
		label.SetBounds(num, num3, num2, Sc(26));
		num3 += Sc(32);
		label2.SetBounds(num, num3, num2, Sc(46));
		num3 += Sc(56);
		label3.SetBounds(num, num3, num2, Sc(20));
		num3 += Sc(24);
		linkLabel.SetBounds(num, num3, num2, Sc(24));
		num3 += Sc(30);
		linkLabel2.SetBounds(num, num3, num2, Sc(22));
		num3 += Sc(36);
		flatButton.SetBounds(num, num3, Sc(88), Sc(32));
		flatButton3.SetBounds(num + num2 - Sc(88), num3, Sc(88), Sc(32));
		flatButton2.SetBounds(num + num2 - Sc(88) - Sc(8) - Sc(104), num3, Sc(104), Sc(32));
		num3 += Sc(32) + num;
		form.ClientSize = new Size(num2 + num * 2, num3);
		form.Controls.AddRange(new Control[8] { label, label2, label3, linkLabel, linkLabel2, flatButton, flatButton2, flatButton3 });
		form.ShowDialog(this);
	}

	private void DownloadAndApply(UpdateInfo info)
	{
		_updateBusy = true;
		_btnCheckUpdate.Enabled = false;
		SetUpdateStatus(Loc.T("正在下载 ") + info.Tag + "…", UiTheme.Hint);
		try
		{
			using DownloadForm downloadForm = new DownloadForm(info);
			downloadForm.ShowDialog(this);
			switch (downloadForm.Result)
			{
			case DownloadForm.Outcome.Cancelled:
				SetUpdateStatus(Loc.T("已取消下载，仍是 v") + AppInfo.Version, UiTheme.Hint);
				break;
			case DownloadForm.Outcome.Failed:
				SetUpdateStatus(Loc.T("下载失败"), UiTheme.Danger);
				break;
			case DownloadForm.Outcome.Applied:
				SetUpdateStatus(Loc.T("更新已就绪，程序即将退出并自动替换"), UiTheme.Success);
				break;
			default:
				SetUpdateStatus(Loc.T("下载窗口已关闭"), UiTheme.Hint);
				break;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			SetUpdateStatus(Loc.T("下载失败：") + ex.Message, UiTheme.Danger);
		}
		finally
		{
			_updateBusy = false;
			if (_btnCheckUpdate != null && !_btnCheckUpdate.IsDisposed)
			{
				_btnCheckUpdate.Enabled = true;
			}
		}
	}
}
