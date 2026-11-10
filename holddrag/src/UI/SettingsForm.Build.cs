// HoldDrag · UI/SettingsForm.Build.cs

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
	// ======================= 建界面 =======================

	private void BuildUi()
	{
		SuspendLayout();
		Text = Loc.T("长按拖窗 · 设置");
		// 语言变了字体族也要跟着换（中文雅黑 / 英文 Segoe UI）
		try
		{
			DesignFontFamily = PickDesignFont();
		}
		catch
		{
		}
		Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel);
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
			Font = new Font(DesignFontFamily, Sc(17), FontStyle.Bold, GraphicsUnit.Pixel),
			TextAlign = ContentAlignment.MiddleLeft,
			Text = ""
		};
		_txtSearch = new TextBox
		{
			BorderStyle = BorderStyle.FixedSingle,
			ForeColor = Tokens.TextPrimary,
			BackColor = Tokens.Surface,
			Font = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel),
			PlaceholderText = Loc.T("搜索设置…")
		};
		_txtSearch.KeyDown += delegate(object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter)
			{
				e.SuppressKeyPress = true;
				RunSearch(1);
			}
			else if (e.KeyCode == Keys.Escape)
			{
				_txtSearch.Clear();
				_searchHits.Clear();
				_searchIndex = -1;
				_searchQuery = "";
			}
		};
		_lblPageSub = new Label
		{
			AutoSize = false,
			BackColor = Tokens.Canvas,
			ForeColor = Tokens.TextTertiary,
			Font = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel),
			TextAlign = ContentAlignment.MiddleLeft,
			Text = ""
		};
		_titleStrip.Controls.Add(_lblPageTitle);
		_titleStrip.Controls.Add(_txtSearch);
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
			Font = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel),
			BackColor = Tokens.Canvas,
			Location = new Point(S(24), S(14)),
			Size = new Size(S(360), S(32)),
			Text = ""
		};
		_pager.Controls.Add(_lblStatus);
		_btnDefaultsRef = new FlatButton
		{
			Text = Loc.T("恢复默认值"),
			// 英文「Restore defaults」比中文长，按语言给宽度，避免被截成「Restore defau…」
			Size = new Size(S(Loc.IsEnglish ? 144 : 112), S(34))
		};
		_btnDefaultsRef.Click += delegate
		{
			ResetDefaults();
		};
		_pager.Controls.Add(_btnDefaultsRef);
		_btnSaveRef = new FlatButton
		{
			Primary = true,
			Text = Loc.T("保存并生效"),
			Size = new Size(S(112), S(34))
		};
		_btnSaveRef.Click += delegate
		{
			Save();
		};
		_pager.Controls.Add(_btnSaveRef);
		_btnCancelRef = new FlatButton
		{
			Text = Loc.T("取消"),
			Size = new Size(S(88), S(34))
		};
		_btnCancelRef.Click += delegate
		{
			Close();
		};
		_pager.Controls.Add(_btnCancelRef);
		// 键盘可达：Enter = 保存并生效，Esc = 取消关闭。
		// 录制按键时 ProcessCmdKey 会先截住 Esc（取消录制），不会误关窗口。
		try
		{
			AcceptButton = _btnSaveRef;
			CancelButton = _btnCancelRef;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
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
		ApplyKeyboardAffordances();
		// 首次构建：8 页都排好，切页时不会看到"半成品"
		LayoutAllPages(ContentWidth());
		SelectPage(0);
	}

	/// <summary>
	/// 键盘可达性收尾。
	///
	/// 这些自绘控件（色板、样式缩略图、帮助手风琴、侧栏、预览框、标题栏按钮）只实现了鼠标事件，
	/// 留成 Tab 停靠点只会让键盘用户连续撞死胡同：Tab 停在它们身上，按空格/回车没有任何反应。
	/// 它们对应的可访问入口是同一张卡片里的下拉框（颜色 / 样式），那里是标准 ComboBox，键盘完全可用。
	/// </summary>
	private void ApplyKeyboardAffordances()
	{
		try
		{
			List<Control> list = new List<Control>();
			CollectTree(this, list);
			for (int i = 0; i < list.Count; i++)
			{
				Control control = list[i];
				if (control is SwatchGrid || control is StyleGallery || control is HelpPanel || control is SideNav || control is FramePreviewBox || control is CaptionButton || control is Card || control is Page || control is PageHost)
				{
					control.TabStop = false;
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>
	/// 按"设计像素"量出按钮需要多宽：固定宽度在英文下必然把「Check for updates」截成「Check for upd…」。
	/// 用 13px 的设计字号量（与 Sc(13) 同源），再留 30px 内边距。
	/// </summary>
	private static int DesignButtonWidth(string text, int floor)
	{
		try
		{
			using Font font = new Font(DesignFontFamily, 13f, FontStyle.Regular, GraphicsUnit.Pixel);
			Size proposed = new Size(int.MaxValue, int.MaxValue);
			int width = TextRenderer.MeasureText(text, font, proposed, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
			return Math.Max(floor, width + 30);
		}
		catch
		{
			return floor;
		}
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
			// 搜索框：贴在最小化按钮左边（不影响标题/副标题的可用宽度）
			int numSearch = S(180);
			Place(_txtSearch, new Rectangle(_btnMin.Left - numSearch - S(10), num6 + (num5 - S(26)) / 2 + S(2), numSearch, S(26)));
			int num8 = S(24);
			int num9 = Math.Max(S(60), _btnMin.Left - num8 - S(16) - numSearch - S(20));
			// 副标题保持原来的宽度（不因搜索框而变窄，否则会折行）
			num9 = Math.Max(S(60), _btnMin.Left - num8 - S(16));
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
		return new string[9]
		{
			Loc.T("触发方式"), Loc.T("鼠标键动作"), Loc.T("视觉反馈"), Loc.T("拖动行为"), Loc.T("窗口规则"), Loc.T("流畅度"), Loc.T("启动与权限"), Loc.T("帮助"), Loc.T("关于")
		};
	}

	private static string[] NavGlyphs()
	{
		return new string[9] { Glyph.Keyboard, Glyph.Move, Glyph.Color, Glyph.Move, Glyph.Filter, Glyph.Speed, Glyph.Shield, Glyph.Help, Glyph.Info };
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
				FlatCombo flatCombo = FindCombo(Controls[i]);
				if (flatCombo != null && flatCombo.Height > 8)
				{
					return Math.Max(Sc(24), flatCombo.Height);
				}
			}
		}
		catch
		{
		}
		return Sc(32);
	}

	private static FlatCombo FindCombo(Control root)
	{
		try
		{
			for (int i = 0; i < root.Controls.Count; i++)
			{
				if (root.Controls[i] is FlatCombo result)
				{
					return result;
				}
				FlatCombo flatCombo2 = FindCombo(root.Controls[i]);
				if (flatCombo2 != null)
				{
					return flatCombo2;
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
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel),
			TextAlign = HorizontalAlignment.Center
		};
		_btnRecord = new FlatButton
		{
			Text = Loc.T("重新录制")
		};
		_btnRecord.Click += delegate
		{
			StartRecording();
		};
		_lblKeyHint = HintLabel();
		_chkHold = new FlatCheck
		{
			Text = Loc.T("需要长按触发（关闭后按住立即生效）"),
			Cursor = Cursors.Hand
		};
		_chkHold.CheckedChanged += delegate
		{
			_numHold.Enabled = _chkHold.Checked;
		};
		_numHold = NewNum(100m, 3000m, 50m);
		_cmbTriggerMode = NewCombo();
		_cmbTriggerMode.SelectedIndexChanged += delegate
		{
			if (!_populating)
			{
				SyncTriggerModeEnabled();
			}
		};
		_cmbLeft = NewCombo();
		_cmbRight = NewCombo();
		_cmbMiddle = NewCombo();
		_chkFrame = new FlatCheck
		{
			Text = Loc.T("长按到达阈值后，光标所在窗口亮起外框"),
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
			Text = Loc.T("颜色跟随系统强调色（换 Windows 主题色时外框一起变）"),
			Cursor = Cursors.Hand
		};
		_chkFollowAccent.CheckedChanged += delegate
		{
			SyncFrameEnabled();
			SyncPreview();
		};
		_btnCustomColor = new FlatButton
		{
			Text = Loc.T("自定义颜色…")
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
		_chkDblLeft = Check(Loc.T("双击左键 = 最大化 / 还原"));
		_chkDblRight = Check(Loc.T("双击右键 = 九宫格分区"));
		_chkRestoreMax = Check(Loc.T("允许拖动已最大化的窗口（先还原再拖）"));
		_chkFront = Check(Loc.T("开始拖动时自动把窗口提到最前"));
		_chkFullscreen = Check(Loc.T("允许拖动全屏 / 无边框窗口（有风险）"));
		_chkExitFullscreen = Check(Loc.T("触发时先把全屏/最大化窗口变回普通"));
		_chkShake = Check(Loc.T("选中窗口后晃一晃，其它窗口全部最小化"));
		_chkShowHud = Check(Loc.T("拖动时显示尺寸提示"));
		_chkHudPos = Check(Loc.T("尺寸提示里带上窗口坐标"));
		_chkSnap = Check(Loc.T("拖到屏幕边缘时自动吸附"));
		_numSnapThreshold = NewNum(0m, 40m, 1m);
		_chkTiling = Check(Loc.T("按住触发键时用方向键 / 数字键布局窗口"));
		_chkUndo = Check(Loc.T("允许 Ctrl+Z 撤销上一次拖动（5 秒内）"));
		_txtBlacklist = new TextBox
		{
			BorderStyle = BorderStyle.None,
			ForeColor = Tokens.TextPrimary,
			BackColor = Tokens.FieldFill,
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel)
		};
		_txtAppRules = new TextBox
		{
			BorderStyle = BorderStyle.None,
			ForeColor = Tokens.TextPrimary,
			BackColor = Tokens.FieldFill,
			Font = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel)
		};
		_cmbRuleMode = NewCombo();
		_btnAddRule = new FlatButton
		{
			Text = Loc.T("点选窗口添加")
		};
		_btnAddRule.Click += delegate
		{
			PickWindowByClick(AddRuleAt);
		};
		_cmbLayouts = NewCombo();
		_btnLayoutSave = new FlatButton
		{
			Text = Loc.T("保存当前…")
		};
		_btnLayoutSave.Click += delegate
		{
			SaveLayoutFromUi();
		};
		_btnLayoutRestore = new FlatButton
		{
			Text = Loc.T("恢复选中布局")
		};
		_btnLayoutRestore.Click += delegate
		{
			RestoreLayoutFromUi();
		};
		_btnLayoutDelete = new FlatButton
		{
			Text = Loc.T("删除")
		};
		_btnLayoutDelete.Click += delegate
		{
			DeleteLayoutFromUi();
		};
		_btnPickExe = new FlatButton
		{
			Text = Loc.T("选择程序…")
		};
		_btnPickExe.Click += delegate
		{
			PickExeFile();
		};
		_btnPickWindow = new FlatButton
		{
			Text = Loc.T("点选窗口")
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
		// 上限和 AppConfig.Normalize 的钳位保持一致（0 = 不限制；非 0 的有效区间是 20..500）
		_numRefreshHz = NewNum(0m, 500m, 1m);
		_numRefreshHz.ValueChanged += delegate
		{
			if (_populating)
			{
				return;
			}
			// 1..19 不是有效值：当场吸附到 20，避免「界面显示 5、实际按 20 跑」
			decimal value = _numRefreshHz.Value;
			if (value > 0m && value < 20m)
			{
				_numRefreshHz.Value = 20m;
			}
		};
		_lblRefreshHint = HintLabel();
		_cmbAutoStart = NewCombo();
		_cmbLanguage = NewCombo();
		_cmbLanguage.SelectedIndexChanged += delegate
		{
			OnLanguageSelectionChanged();
		};
		_chkRunAsAdmin = Check(Loc.T("开机时自动申请管理员权限（默认开启）"));
		_chkWarnNonAdmin = Check(Loc.T("没有管理员权限时提示我（可关闭）"));
		_lblAdmin = HintLabel();
		_btnRestartAdmin = new FlatButton
		{
			Text = Loc.T("以管理员重启")
		};
		_btnRestartAdmin.Click += delegate
		{
			RestartAsAdmin();
		};
		_helpPanel = new HelpPanel();
		_helpPanel.SetEntries(BuildHelpEntries());
		_helpPanel.OpenChanged = delegate
		{
			try
			{
				LayoutPages(Math.Max(10, ContentWidth()), forceAll: true);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		};
		_btnExportDiag = new FlatButton
		{
			Text = Loc.T("导出诊断包…")
		};
		_btnExportDiag.Click += delegate
		{
			ExportDiagnosticsBundle();
		};
		_btnGitHub = new FlatButton
		{
			Text = "github.com/QQQOrange/holddrag"
		};
		_btnGitHub.Click += delegate
		{
			OpenUrl(AppInfo.GitHubUrl);
		};
		_chkAutoUpdate = Check(Loc.T("自动更新（启动时检查，有新版提示我）"));
		_chkAutoUpdate.CheckedChanged += delegate
		{
			if (!_populating && _edit != null)
			{
				_edit.AutoCheckUpdate = _chkAutoUpdate.Checked;
			}
		};
		_btnCheckUpdate = new FlatButton
		{
			Text = Loc.T("检查更新")
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
			// 必须是实色：卡片是自绘的，Transparent 子标签会让父级每帧整块重绘（滚动/切页发闷的主因）
			BackColor = Tokens.Surface,
			ForeColor = Tokens.Hint,
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel),
			Text = Loc.T("尚未检查")
		};
		// ---------- v1.7.0：GitHub 个人访问令牌 ----------
		_txtToken = new TextBox
		{
			BorderStyle = BorderStyle.None,
			ForeColor = Tokens.TextPrimary,
			BackColor = Tokens.FieldFill,
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel),
			// 令牌是敏感信息：输入即遮住，屏幕录制/旁人路过都看不到明文
			UseSystemPasswordChar = true,
			MaxLength = 255,
			AccessibleName = Loc.T("GitHub 个人访问令牌"),
			AccessibleDescription = Loc.T("可选。填写后检查更新会带上该令牌请求 GitHub 接口，接口限额从匿名 60 次/小时提升到 5000 次/小时")
		};
		_txtToken.TextChanged += delegate
		{
			RefreshTokenState();
		};
		_lblTokenState = HintLabelWith(Loc.T("未填写：检查更新走匿名接口"));
		_lblTokenState.AutoEllipsis = true;
		_btnSaveToken = new FlatButton
		{
			Text = Loc.T("保存令牌")
		};
		_btnSaveToken.Click += delegate
		{
			SaveToken();
		};
		// 「验证令牌」：真的向 api.github.com 发一次请求（用的是检查更新那条接口），
		// 把"令牌到底有没有用"从猜测变成结论——保存前就能确认，不用等下次检查更新才发现是坏的。
		_btnVerifyToken = new FlatButton
		{
			Text = Loc.T("验证令牌"),
			AccessibleName = Loc.T("验证 GitHub 个人访问令牌"),
			AccessibleDescription = Loc.T("用当前输入框里的令牌向 GitHub 发一次请求，确认令牌有效且能读到本仓库的发布信息")
		};
		_btnVerifyToken.Click += delegate
		{
			VerifyTokenNow();
		};
		_lblTokenVerify = HintLabelWith(Loc.T("尚未验证"));
		_lblTokenVerify.AutoEllipsis = true;
		// 小「?」图标：位置在输入框右侧、与输入框垂直居中（由卡片行布局保证）；
		// 无障碍：有 AccessibleName/Description，Tab 可聚焦，空格/回车即触发（Button 的默认键盘行为）。
		_btnTokenHelp = new FlatButton
		{
			Text = "?",
			AccessibleName = Loc.T("什么是 GitHub 个人访问令牌"),
			AccessibleDescription = Loc.T("打开 GitHub 官方中文文档，了解个人访问令牌的定义、创建与权限设置方法"),
			AccessibleRole = AccessibleRole.HelpBalloon,
			TabStop = true
		};
		_btnTokenHelp.Click += delegate
		{
			OpenUrl(AppInfo.PatDocsUrl);
		};
		_tipToken = new ToolTip();
		_tipToken.SetToolTip(_btnTokenHelp, Loc.T("什么是 GitHub 个人访问令牌？\r\n点击用浏览器打开 GitHub 官方中文文档"));
	}

	private static FlatCombo NewCombo()
	{
		return new FlatCombo();
	}

	private static FlatNum NewNum(decimal min, decimal max, decimal step)
	{
		return new FlatNum(min, max, step);
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
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel),
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
			Font = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel),
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
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel),
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
}
