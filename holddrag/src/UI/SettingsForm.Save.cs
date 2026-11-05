// HoldDrag · UI/SettingsForm.Save.cs

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
		_cmbTriggerMode.Items.Clear();
		_cmbTriggerMode.Items.Add(new IdItem
		{
			Value = "Hold",
			Text = Loc.T("长按")
		});
		_cmbTriggerMode.Items.Add(new IdItem
		{
			Value = "Toggle",
			Text = Loc.T("点按切换")
		});
		SelectId(_cmbTriggerMode, _edit.TriggerMode);
		SyncTriggerModeEnabled();
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
					Text = Loc.T(entry.Name),
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
				Name = Loc.T(FrameColors.All[l].Name),
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
		_chkShowHud.Checked = _edit.ShowDragHud;
		_chkHudPos.Checked = _edit.DragHudShowPosition;
		_chkSnap.Checked = _edit.SnapToEdges;
		_numSnapThreshold.Value = Clamp(_edit.SnapThreshold, (int)_numSnapThreshold.Minimum, (int)_numSnapThreshold.Maximum);
		_chkTiling.Checked = _edit.KeyboardTiling;
		_chkUndo.Checked = _edit.UndoEnabled;
		_txtAppRules.Text = _edit.AppRules ?? "";
		if (_cmbRuleMode.Items.Count == 0)
		{
			string[] all = AppRules.All;
			foreach (string text in all)
			{
				_cmbRuleMode.Items.Add(new IdItem
				{
					Value = text,
					Text = AppRules.DisplayName(text)
				});
			}
			_cmbRuleMode.SelectedIndex = 0;
		}
		RefreshLayoutCombo();
		_txtBlacklist.Text = _edit.Blacklist;
		_cmbRefreshMode.Items.Clear();
		_cmbRefreshMode.Items.Add(new IdItem
		{
			Value = "Auto",
			Text = Loc.T("跟随屏幕")
		});
		_cmbRefreshMode.Items.Add(new IdItem
		{
			Value = "Custom",
			Text = Loc.T("固定值")
		});
		SelectId(_cmbRefreshMode, _edit.DragRefreshMode);
		_numRefreshHz.Value = Clamp(_edit.DragRefreshHz, (int)_numRefreshHz.Minimum, (int)_numRefreshHz.Maximum);
		SyncRefreshEnabled();
		int refreshRate = Native.GetRefreshRate();
		_lblRefreshHint.Text = Loc.T("跟随屏幕：按实测刷新率节流（本机 ") + ((refreshRate > 0) ? (refreshRate + "Hz") : Loc.T("未知")) + Loc.T("）。\r\n固定值：自定义频率，0 = 不限制。");
		// 语言下拉：只有两项，显示各自的母语写法（English / 简体中文），换哪种语言都认得出
		_cmbLanguage.Items.Clear();
		_cmbLanguage.Items.Add(new IdItem
		{
			Value = Loc.Zh,
			Text = Loc.DisplayName(Loc.Zh)
		});
		_cmbLanguage.Items.Add(new IdItem
		{
			Value = Loc.En,
			Text = Loc.DisplayName(Loc.En)
		});
		SelectId(_cmbLanguage, Loc.Current);
		_cmbAutoStart.Items.Clear();
		_cmbAutoStart.Items.Add(new IdItem
		{
			Value = "TaskScheduler",
			Text = Loc.T("计划任务（最高权限、登录时静默启动，推荐）")
		});
		_cmbAutoStart.Items.Add(new IdItem
		{
			Value = "Registry",
			Text = Loc.T("注册表 Run 项（每次登录会弹一次 UAC）")
		});
		_cmbAutoStart.Items.Add(new IdItem
		{
			Value = "None",
			Text = Loc.T("不开机启动")
		});
		SelectId(_cmbAutoStart, _edit.AutoStart);
		_chkRunAsAdmin.Checked = _edit.RunAsAdmin;
		_chkWarnNonAdmin.Checked = _edit.WarnNonAdmin;
		_chkAutoUpdate.Checked = _edit.AutoCheckUpdate;
		_txtToken.Text = _edit.GitHubToken;
		RefreshTokenState();
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

	/// <summary>点按模式下「长按阈值」这一整块没有意义，置灰比让人改一个不生效的数字更诚实。</summary>
	private void SyncTriggerModeEnabled()
	{
		bool flag = string.Equals(SelectedId(_cmbTriggerMode, "Hold"), "Hold", StringComparison.OrdinalIgnoreCase);
		_chkHold.Enabled = flag;
		_numHold.Enabled = flag && _chkHold.Checked;
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
		_lblColorName.Text = Loc.T("当前颜色：") + FrameColors.NameOf(text, followAccent, _customColorArgb) + " " + FrameColors.HexOf(color);
		_lblCustomColor.Text = ((_customColorArgb != 0) ? (Loc.T("已设自定义色 ") + FrameColors.HexOf(Color.FromArgb(_customColorArgb))) : Loc.T("还没有设置自定义颜色"));
		_lblStyleHeader.Text = Loc.T("样式（当前粗细 ") + thickness + Loc.T(" px・圆角 ") + radius + " px" + Loc.Close;
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
		_lblFrameTip.Text = (flag ? Loc.T("外观已改动，点「保存并生效」才会画到实际外框上。") : Loc.T("颜色与样式即点即换，预览与外框同一段代码。"));
	}

	private void UpdateAdminLabel()
	{
		bool flag = Program.IsAdministrator();
		_lblAdmin.Text = (flag ? Loc.T("当前权限：管理员（可拖动任务管理器这类提权窗口）") : Loc.T("当前权限：普通用户（日常窗口没问题，提权窗口拖不动）"));
		_btnRestartAdmin.Enabled = !flag;
	}

	private void UpdateKeyText()
	{
		_txtKey.Text = KeysUtil.DisplayName(_edit.TriggerKeyId);
		if (_nav != null)
		{
			// 侧栏底部状态卡：一眼看到"正在运行"和当前触发键
			_nav.SetStatus(Loc.T("正在运行"), KeysUtil.DisplayName(_edit.TriggerKeyId) + Loc.T(" + 左键拖动"));
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

	private static void SelectAction(FlatCombo c, string value)
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

	private static void SelectId(FlatCombo c, string value)
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

	private static string SelectedAction(FlatCombo c)
	{
		if (!(c.SelectedItem is ActionItem actionItem))
		{
			return null;
		}
		return actionItem.Value;
	}

	private static string SelectedId(FlatCombo c, string fallback)
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
				return Loc.T(FrameColors.All[i].Name);
			}
		}
		return id;
	}

	private void StartRecording()
	{
		_recording = true;
		// 录制期间触发键被压制，它的"松开"事件我们收不到；先复位，避免录完按键后状态卡死。
		_coord.Trigger.Reset("开始录制按键");
		_coord.SuppressTrigger = true;
		_txtKey.Text = Loc.T("请按下要用的按键…");
		// 底色与文字色必须成对改：只改底色的话，暗色主题里浅色字落在浅黄底上几乎看不见
		_txtKey.BackColor = Tokens.FieldRecording;
		_txtKey.ForeColor = Tokens.FieldRecordingText;
		_lblKeyHint.Text = Loc.T("正在录制：按任意键或鼠标侧键，Esc 取消。");
		_lblKeyHint.ForeColor = Tokens.WarnText;
		Focus();
	}

	private void StopRecording(bool applied)
	{
		if (!_recording)
		{
			return;
		}
		_recording = false;
		_coord.SuppressTrigger = false;
		_txtKey.BackColor = Tokens.FieldFill;
		_txtKey.ForeColor = Tokens.TextPrimary;
		UpdateKeyText();
		if (!applied)
		{
			_lblKeyHint.Text = Loc.T("已取消录制。");
			return;
		}
		string text = string.Empty;
		if (KeysUtil.IsToggleKey(_edit.TriggerKeyId))
		{
			text = Loc.T("注意：这是切换型按键，本程序会压制它，因此它不会再切换大小写/数字锁定。");
		}
		else if (KeysUtil.IsWindowsKey(_edit.TriggerKeyId))
		{
			text = Loc.T("注意：Win 键被作为触发键期间，Win 组合键（Win+E、Win+D 等）无法使用。");
		}
		_lblKeyHint.Text = ((text.Length > 0) ? text : (Loc.T("已绑定：") + KeysUtil.DisplayName(_edit.TriggerKeyId)));
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
				_lblKeyHint.Text = Loc.T("修饰键（Ctrl / Shift / Alt / Win）不能单独作为触发键，请再按一个主键。");
				_lblKeyHint.ForeColor = Tokens.WarnText;
				return true;
			}
			string text = ResolveRecordedKey(keys);
			if (text == null)
			{
				_lblKeyHint.Text = Loc.T("左键 / 右键 / 中键不能作为触发键，请用键盘按键或鼠标侧键。");
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
				_lblKeyHint.Text = Loc.T("左键 / 右键 / 中键不能作为触发键，请用键盘按键或鼠标侧键。");
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
			_lblStatus.Text = Loc.T("保存失败：") + saveError + Loc.T("（本次修改没有写入磁盘，重启后会丢失；请检查配置目录权限或磁盘空间）");
			return;
		}
		string message;
		bool autoStartOk = AutoStart.Apply(_edit.AutoStart, AppPaths.EntryPath, out message);
		// 保存成功之后把 _edit 换成"已落盘的那份"，并回填界面：
		// 配置在落盘时会被 Normalize 规范化（比如刷新频率 5 → 20），不回填就会出现
		// "界面显示 5、实际按 20 跑"；同时 _edit 不再是内存里那份活配置，"保存后再取消"也就不会改到正在生效的设置。
		try
		{
			_edit = _store.Current.Clone();
			Populate();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		if (autoStartOk)
		{
			_lblStatus.ForeColor = string.IsNullOrEmpty(message) ? Tokens.Success : Tokens.WarnText;
			_lblStatus.Text = string.IsNullOrEmpty(message) ? Loc.T("已保存并立即生效") : (Loc.T("已保存并立即生效") + " · " + message);
		}
		else
		{
			_lblStatus.ForeColor = Tokens.Danger;
			_lblStatus.Text = Loc.T("配置已保存，但开机自启设置失败");
			MessageBox.Show(message, Loc.T("开机自启设置失败"), MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		}
		if (_edit.RunAsAdmin && !Program.IsAdministrator() && MessageBox.Show(Loc.T("已保存。\r\n\r\n管理员权限只在【启动的时候】申请，所以要重启程序才会生效。\r\n现在以管理员身份重启吗？（选\"否\"就等下次启动再提权）"), Loc.T("长按拖窗 · 权限变更"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
		{
			RestartAsAdmin();
		}
	}

	/// <summary>
	/// 语言下拉框：选中即保存并立刻重建界面，不需要再点「保存并生效」。
	/// 只改配置里的 Language 一项（克隆当前已落盘的配置再写），
	/// 避免把界面上其它还没确认的半成品改动一起提交。
	/// </summary>
	private void OnLanguageSelectionChanged()
	{
		if (_populating)
		{
			return;
		}
		string text = SelectedId(_cmbLanguage, Loc.Current);
		if (string.Equals(Loc.Normalize(text), Loc.Current, StringComparison.Ordinal))
		{
			return;
		}
		try
		{
			AppConfig appConfig = _store.Current?.Clone() ?? new AppConfig();
			appConfig.Language = Loc.Normalize(text);
			if (!_store.Apply(appConfig, out string error))
			{
				if (_lblStatus != null)
				{
					_lblStatus.ForeColor = Tokens.Danger;
					_lblStatus.Text = Loc.T("语言没能写入配置：") + error;
				}
				return;
			}
			if (_edit != null)
			{
				_edit.Language = appConfig.Language;
			}
			Loc.Set(appConfig.Language);
			// 重建必须推迟到消息循环的下一轮：此刻还在 ComboBox 自己的事件里，
			// 立刻 Dispose 掉它会连正在派发事件的控件一起拔掉。
			BeginInvoke(delegate
			{
				try
				{
					RebuildUi();
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

	private void CaptureInto(AppConfig target)
	{
		target.TriggerMode = SelectedId(_cmbTriggerMode, target.TriggerMode);
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
		target.ShowDragHud = _chkShowHud.Checked;
		target.DragHudShowPosition = _chkHudPos.Checked;
		target.SnapToEdges = _chkSnap.Checked;
		target.SnapThreshold = (int)_numSnapThreshold.Value;
		target.KeyboardTiling = _chkTiling.Checked;
		target.UndoEnabled = _chkUndo.Checked;
		target.AppRules = AppRules.Normalize(_txtAppRules.Text);
		AppRules.Apply(target.AppRules);
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
		target.Language = SelectedId(_cmbLanguage, target.Language);
		target.GitHubToken = Updater.NormalizeToken(_txtToken.Text);
		CaptureSizeInto(target);
	}

	private void PickExeFile()
	{
		try
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog();
			openFileDialog.Title = Loc.T("选择要排除的程序（它的窗口将不再被接管）");
			openFileDialog.Filter = Loc.T("可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*");
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
					Status(Loc.T("没从文件名里取到关键字"), error: true);
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
			Status(Loc.T("打开文件对话框失败：") + ex.Message, error: true);
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
			Status(Loc.T("打开取色器失败：") + ex.Message, error: true);
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
				Status(Loc.T("已取消窗口点选"), error: false);
				try
				{
					mask.Close();
				}
				catch
				{
				}
			};
			Status(Loc.T("请点击要排除的窗口（按 Esc 取消）"), error: false);
			mask.ShowDialog(this);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			Status(Loc.T("窗口点选失败：") + ex.Message, error: true);
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
				Status(Loc.T("没找到窗口，请对着目标窗口的标题栏再点一次"), error: true);
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
				Status(Loc.T("这扇窗口取不到可用的标识，请改用「选择程序…」"), error: true);
				return;
			}
			AppendBlacklistKeyword(text2);
			string text3 = windowTitle;
			if (text3 != null && text3.Length > 18)
			{
				text3 = text3.Substring(0, 18) + "…";
			}
			Status(Loc.T("已排除：") + text2 + Loc.InParens(text3), error: false);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			Status(Loc.T("点选失败：") + ex.Message, error: true);
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
		// 语言属「环境类」设置：恢复默认值不该把用户选的语言改回中文
		appConfig.Language = _edit.Language;
		// 令牌属「环境类」设置：恢复默认值不该把用户的令牌抹掉
		appConfig.GitHubToken = _edit.GitHubToken;
		_edit = appConfig;
		if (_recording)
		{
			StopRecording(applied: false);
		}
		Populate();
		_lblStatus.ForeColor = Tokens.Hint;
		_lblStatus.Text = Loc.T("已载入默认值，点「保存并生效」后写入");
	}

	private void RestartAsAdmin()
	{
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = AppPaths.ExePath,
			// 提权重启后仍要知道外层 exe 在哪，否则新进程的更新会打到 %LOCALAPPDATA% 的副本上。
			// --take-over：新进程要等本进程释放单实例锁后再接管（新先起、旧后关），
			// 否则新进程一启动就判定"已有实例"→ 通知一声退出，重启完一个进程都不剩。
			Arguments = "--host " + Updater.QuoteArg(AppPaths.EntryPath) + " " + Updater.ArgTakeOver,
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
				SetUpdateStatus(Loc.T("打不开浏览器，链接已复制到剪贴板：") + url, Tokens.WarnText);
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
}
