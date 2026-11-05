// HoldDrag · UI/SettingsForm.Probe.cs

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
	// ======================= 探针面（自测 / 生产预览） =======================

	/// <summary>自测用：直接点第 index 个色块，返回新选中的颜色 id。</summary>
	internal string ProbePickSwatch(int index)
	{
		_gridColor.ProbeClick(index);
		Application.DoEvents();
		return _gridColor.SelectedId;
	}

	/// <summary>自测用：色板第 index 格的颜色 id。</summary>
	internal string ProbeSwatchId(int index)
	{
		return _gridColor.ProbeIdAt(index);
	}

	/// <summary>自测用：跑一次搜索，返回命中数。</summary>
	internal int ProbeSearch(string query)
	{
		_txtSearch.Text = query;
		RunSearch(1);
		Application.DoEvents();
		return _searchHits.Count;
	}

	internal string ProbeSearchStatus()
	{
		return _lblStatus.Text;
	}

	/// <summary>自测用：页面上是否有这一行（卡片可见 + 行标题匹配）。</summary>
	internal bool ProbeControlVisible(string label)
	{
		foreach (Page page in _pages)
		{
			if (!page.Host.Visible)
			{
				continue;
			}
			foreach (Card card in page.Cards)
			{
				if (card.Visible && card.HasRow(label))
				{
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>自测用：切到指定页。</summary>
	internal void ProbeSelectPage(int index)
	{
		SelectPage(index);
	}

	/// <summary>自测用：在当前颜色弹层里选第 index 项。</summary>
	internal bool ProbeChooseColorInPopup(int index)
	{
		return _cmbFrameColor.ProbeChooseInPopup(index);
	}

	/// <summary>自测用：打开外框颜色下拉框（截图 / 交互验证）。</summary>
	internal void ProbeOpenColorPopup()
	{
		try
		{
			_cmbFrameColor.ShowPopup();
			Application.DoEvents();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	internal void ProbeCloseColorPopup()
	{
		try
		{
			_cmbFrameColor.ClosePopup();
		}
		catch
		{
		}
	}

	internal Rectangle ProbeColorPopupBounds()
	{
		try
		{
			foreach (Form form in Application.OpenForms)
			{
				if (form is FlatComboPopup)
				{
					return form.Bounds;
				}
			}
		}
		catch
		{
		}
		return Rectangle.Empty;
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
			ColorNameText = _lblColorName.Text,
			FollowAccent = _chkFollowAccent.Checked,
			CustomColorArgb = _customColorArgb,
			EffectiveColorHex = (_pnlPreview.ColorOverride.HasValue ? FrameColors.HexOf(_pnlPreview.ColorOverride.Value) : "")
		};
	}

	internal void ProbeSelectFrameColor(string id)
	{
		SelectPage(2);
		SelectId(_cmbFrameColor, id);
		SyncPreview();
	}

	internal void ProbeSetFollowAccent(bool on)
	{
		SelectPage(2);
		_chkFollowAccent.Checked = on;
		SyncFrameEnabled();
		SyncPreview();
	}

	internal void ProbeSetCustomColor(int argb)
	{
		SelectPage(2);
		_customColorArgb = argb;
		_chkFollowAccent.Checked = false;
		SyncFrameEnabled();
		SyncPreview();
	}

	internal void ProbeSelectFrameStyle(string id)
	{
		SelectPage(2);
		SelectId(_cmbFrameStyle, id);
		SyncPreview();
	}

	internal void ProbeSetFrameShape(int thickness, int radius, bool enabled)
	{
		SelectPage(2);
		_chkFrame.Checked = enabled;
		_numFrameThick.Value = Clamp(thickness, (int)_numFrameThick.Minimum, (int)_numFrameThick.Maximum);
		_numFrameRadius.Value = Clamp(radius, (int)_numFrameRadius.Minimum, (int)_numFrameRadius.Maximum);
		SyncPreview();
	}

	internal void ProbeCaptureInto(AppConfig target)
	{
		CaptureInto(target);
	}

	internal void ProbeAppendBlacklist(string kw)
	{
		AppendBlacklistKeyword(kw);
	}

	internal string ProbeBlacklistText => _txtBlacklist.Text;

	internal Control ProbeComboLeft => _cmbLeft;

	internal Control ProbeComboRight => _cmbRight;

	internal Control ProbeNumHold => _numHold;

	internal static Control ProbeCardByTitle(Form f, string title)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return null;
		}
		foreach (Card item in AllCards(settingsForm))
		{
			if (item.Text == title)
			{
				return item;
			}
		}
		return null;
	}

	internal static void ProbeSelectPage(Form f, int index)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.SelectPage(index);
		}
	}

	internal static bool ProbePageVisible(Form f, int index)
	{
		if (!(f is SettingsForm settingsForm) || index < 0 || index >= settingsForm._pages.Count)
		{
			return false;
		}
		return settingsForm._pages[index].Host.Visible;
	}

	internal static int ProbePageCount(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return 0;
		}
		return settingsForm._pages.Count;
	}

	internal static int ProbeSidebarCount(Form f)
	{
		if (!(f is SettingsForm { _nav: not null } settingsForm))
		{
			return 0;
		}
		return settingsForm._nav.ItemCount;
	}

	internal static int ProbeSidebarActive(Form f)
	{
		if (!(f is SettingsForm { _nav: not null } settingsForm))
		{
			return -1;
		}
		return settingsForm._nav.ActiveIndex;
	}

	internal static int ProbeWheelScroll(Form f, int notches)
	{
		if (!(f is SettingsForm settingsForm) || settingsForm._pages.Count == 0)
		{
			return 0;
		}
		PageHost host = settingsForm._pages[settingsForm._activePage].Host;
		try
		{
			if (host.IsHandleCreated)
			{
				Native.SendMessageW(host.Handle, 522, (IntPtr)unchecked((int)((notches * 120) << 16)), IntPtr.Zero);
				Application.DoEvents();
			}
			else
			{
				host.Offset -= notches * host.StepPx;
			}
		}
		catch
		{
		}
		return host.Offset;
	}

	internal static string ProbeScrollInfo(Form f)
	{
		if (!(f is SettingsForm settingsForm) || settingsForm._pages.Count == 0)
		{
			return "(none)";
		}
		PageHost host = settingsForm._pages[settingsForm._activePage].Host;
		return Loc.T("页=") + settingsForm._activePage + Loc.T(" 偏移=") + host.Offset + Loc.T(" 内容高=") + ((host.Canvas != null) ? host.Canvas.Height : 0) + Loc.T(" 视口=") + host.ClientSize.Height + Loc.T(" 可滚=") + host.MaxScroll;
	}

	internal static int ProbeScrollTo(Form f, int y)
	{
		if (!(f is SettingsForm settingsForm) || settingsForm._pages.Count == 0)
		{
			return 0;
		}
		PageHost host = settingsForm._pages[settingsForm._activePage].Host;
		host.Offset = y;
		return host.Offset;
	}

	internal static string ProbeSyncDiag(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return "(none)";
		}
		return Loc.T("侧栏高亮=") + ProbeSidebarActive(f) + Loc.T(" 当前页=") + settingsForm._activePage + " " + ProbeScrollInfo(f);
	}

	internal static bool ProbeOnActivePage(Form f, Control c)
	{
		if (!(f is SettingsForm settingsForm) || settingsForm._pages.Count == 0)
		{
			return true;
		}
		PageHost pageHost = settingsForm._pages[settingsForm._activePage].Host;
		for (Control parent = c; parent != null; parent = parent.Parent)
		{
			if (parent == pageHost)
			{
				return true;
			}
			if (parent is PageHost)
			{
				return false;
			}
		}
		return true;
	}

	private static List<Card> AllCards(SettingsForm f)
	{
		List<Card> list = new List<Card>();
		foreach (Page page in f._pages)
		{
			list.AddRange(page.Cards);
		}
		return list;
	}

	internal static int ProbeGroupCount(Form f)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return 0;
		}
		return AllCards(settingsForm).Count;
	}

	internal static Control ProbeGroupAt(Form f, int index)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return null;
		}
		List<Card> list = AllCards(settingsForm);
		if (index < 0 || index >= list.Count)
		{
			return null;
		}
		return list[index];
	}

	internal static bool ProbeGroupInTree(Form f, int index)
	{
		Control control = ProbeGroupAt(f, index);
		if (control == null)
		{
			return false;
		}
		for (Control parent = control.Parent; parent != null; parent = parent.Parent)
		{
			if (parent == f)
			{
				return true;
			}
		}
		return false;
	}

	internal static GroupClipReport ProbeGroupClipping(Form f, int groupIndex)
	{
		GroupClipReport groupClipReport = new GroupClipReport();
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return groupClipReport;
			}
			List<Card> list = AllCards(settingsForm);
			if (groupIndex < 0 || groupIndex >= list.Count)
			{
				return groupClipReport;
			}
			Control control = list[groupIndex];
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
						groupClipReport2.Detail = groupClipReport2.Detail + "「" + (string.IsNullOrEmpty(control2.Text) ? control2.GetType().Name : ShortText(control2.Text)) + "」L=" + control2.Left + " T=" + control2.Top + " W=" + control2.Width + " H=" + control2.Height + Loc.T(" 越界=") + num + "px  ";
					}
				}
			}
		}
		catch (Exception ex)
		{
			groupClipReport.Detail = groupClipReport.Detail + Loc.T("异常 ") + ex.GetType().Name + " ";
		}
		return groupClipReport;
	}

	internal static string ProbeGroupOverlaps(Form f, int groupIndex)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			List<Card> list = AllCards(settingsForm);
			if (groupIndex < 0 || groupIndex >= list.Count)
			{
				return "";
			}
			Control control = list[groupIndex];
			Control[] array = new Control[control.Controls.Count];
			for (int i = 0; i < control.Controls.Count; i++)
			{
				array[i] = control.Controls[i];
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int j = 0; j < array.Length; j++)
			{
				if (array[j] == null || array[j].Width <= 0 || array[j].Height <= 0)
				{
					continue;
				}
				for (int k = j + 1; k < array.Length; k++)
				{
					if (array[k] == null || array[k].Width <= 0 || array[k].Height <= 0)
					{
						continue;
					}
					if (!array[j].Bounds.IntersectsWith(array[k].Bounds))
					{
						continue;
					}
					Rectangle rectangle = Rectangle.Intersect(array[j].Bounds, array[k].Bounds);
					if (rectangle.Width > 2 && rectangle.Height > 2)
					{
						stringBuilder.Append("「").Append(ShortText(array[j].Text ?? array[j].GetType().Name)).Append("」×「")
							.Append(ShortText(array[k].Text ?? array[k].GetType().Name))
							.Append(Loc.T("」交 "))
							.Append(rectangle.Width)
							.Append("x")
							.Append(rectangle.Height)
							.Append(" ");
					}
				}
			}
			return stringBuilder.ToString();
		}
		catch (Exception ex)
		{
			return Loc.T("异常 ") + ex.GetType().Name;
		}
	}

	internal static string ProbeGroupRects(Form f, int index)
	{
		if (!(f is SettingsForm settingsForm))
		{
			return "";
		}
		List<Card> list = AllCards(settingsForm);
		if (index < 0 || index >= list.Count)
		{
			return "";
		}
		Control control = list[index];
		return "z=" + index + " " + control.GetType().Name + " vis=" + control.Visible + " L=" + control.Left + " T=" + control.Top + " W=" + control.Width + " H=" + control.Height + Loc.T(" 右=") + control.Right + Loc.T(" 底=") + control.Bottom;
	}

	internal static int ProbeGroupTop(Form f, int index)
	{
		Control control = ProbeGroupAt(f, index);
		if (control == null)
		{
			return -1;
		}
		return control.Top;
	}

	internal static string ProbeTextFitting(Form f, int groupIndex)
	{
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			List<Card> list = AllCards(settingsForm);
			if (groupIndex < 0 || groupIndex >= list.Count)
			{
				return "";
			}
			Control control = list[groupIndex];
			for (int i = 0; i < control.Controls.Count; i++)
			{
				Control control2 = control.Controls[i];
				if ((control2 is CheckBox || control2 is Label) && !string.IsNullOrEmpty(control2.Text))
				{
					int num = control2.Width - ((control2 is CheckBox) ? Sc(26) : Sc(4));
					if (num < 20)
					{
						num = 20;
					}
					Size size;
					Size size2;
					try
					{
						float num2 = control2.Font.SizeInPoints * 96f / 72f * ((CurrentUiScale > 0f) ? CurrentUiScale : 1f);
						using Font font = new Font(control2.Font.FontFamily, num2, control2.Font.Style, GraphicsUnit.Pixel);
						size = TextRenderer.MeasureText(control2.Text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
						size2 = TextRenderer.MeasureText(control2.Text, font, new Size(num, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
					}
					catch
					{
						continue;
					}
					if ((size2.Width > num && size2.Height > control2.Height) || (size.Width > num && size2.Height > control2.Height))
					{
						stringBuilder.AppendLine("「" + ShortText(control2.Text) + "」W=" + control2.Width + " H=" + control2.Height + Loc.T(" 文本可用=") + num + Loc.T(" 单行需=") + size.Width + Loc.T(" 折行需高=") + size2.Height);
					}
				}
			}
		}
		catch (Exception ex)
		{
			stringBuilder.AppendLine(Loc.T("异常 ") + ex.GetType().Name);
		}
		return stringBuilder.ToString();
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

	internal FramePickerProbe ProbeFramePickers()
	{
		FramePickerProbe framePickerProbe = new FramePickerProbe();
		try
		{
			SelectPage(2);
			Application.DoEvents();
			framePickerProbe.ColorCells = _gridColor.CellCount;
			framePickerProbe.StyleCells = _galleryStyle.ItemsForTest.Count;
			framePickerProbe.GridSize = new Size(_gridColor.Width, _gridColor.Height);
			framePickerProbe.GallerySize = new Size(_galleryStyle.Width, _galleryStyle.Height);
			framePickerProbe.GridSelectedCell = _gridColor.SelectedCellRect;
			framePickerProbe.GallerySelectedCell = _galleryStyle.SelectedCellRect;
			framePickerProbe.GridInsideGroup = InsideCard(_gridColor);
			framePickerProbe.GalleryInsideGroup = InsideCard(_galleryStyle);
			framePickerProbe.GridInk = InkOf(_gridColor, Tokens.Surface);
			framePickerProbe.GalleryInk = InkOf(_galleryStyle, Tokens.Surface);
			framePickerProbe.GridAccentInk = InkNear(_gridColor, Tokens.Accent, 60);
			framePickerProbe.GalleryAccentInk = InkNear(_galleryStyle, Tokens.Accent, 60);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return framePickerProbe;
	}

	private static bool InsideCard(Control c)
	{
		for (Control parent = c; parent != null; parent = parent.Parent)
		{
			if (parent is Card)
			{
				Card card = (Card)parent;
				return c.Left >= -1 && c.Top >= -1 && c.Right <= card.ClientSize.Width + 1 && c.Bottom <= card.ClientSize.Height + 1;
			}
		}
		return false;
	}

	private static long InkOf(Control c, Color bg)
	{
		try
		{
			if (c.Width <= 0 || c.Height <= 0)
			{
				return 0;
			}
			using Bitmap bitmap = new Bitmap(c.Width, c.Height);
			c.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
			long num = 0;
			for (int i = 0; i < bitmap.Height; i++)
			{
				for (int j = 0; j < bitmap.Width; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (Math.Abs(pixel.R - bg.R) + Math.Abs(pixel.G - bg.G) + Math.Abs(pixel.B - bg.B) > 30)
					{
						num++;
					}
				}
			}
			return num;
		}
		catch
		{
			return 0;
		}
	}

	private static long InkNear(Control c, Color target, int tol)
	{
		try
		{
			if (c.Width <= 0 || c.Height <= 0)
			{
				return 0;
			}
			using Bitmap bitmap = new Bitmap(c.Width, c.Height);
			c.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
			long num = 0;
			for (int i = 0; i < bitmap.Height; i++)
			{
				for (int j = 0; j < bitmap.Width; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (Math.Abs(pixel.R - target.R) + Math.Abs(pixel.G - target.G) + Math.Abs(pixel.B - target.B) <= tol)
					{
						num++;
					}
				}
			}
			return num;
		}
		catch
		{
			return 0;
		}
	}

	internal int[] ProbeFrameColorInk(string colorId)
	{
		Color target = Color.Gray;
		try
		{
			target = FrameColors.Resolve(colorId);
		}
		catch
		{
		}
		return new int[3]
		{
			(int)InkNear(_gridColor, target, 24),
			(int)InkNear(_galleryStyle, target, 24),
			(int)InkNear(_pnlPreview, target, 24)
		};
	}

	internal string ProbeGridDiagnostic()
	{
		try
		{
			return Loc.T("色板 ") + _gridColor.CellCount + Loc.T(" 格 ") + _gridColor.ColumnsFor(_gridColor.Width) + Loc.T(" 列 选中=") + (_gridColor.SelectedId ?? "(null)") + Loc.T(" 尺寸=") + _gridColor.Width + "x" + _gridColor.Height + Loc.T(" | 画廊 ") + _galleryStyle.ItemsForTest.Count + Loc.T(" 项 ") + _galleryStyle.ColumnsFor(_galleryStyle.Width) + Loc.T(" 列 选中=") + (_galleryStyle.SelectedId ?? "(null)") + Loc.T(" 尺寸=") + _galleryStyle.Width + "x" + _galleryStyle.Height;
		}
		catch (Exception ex)
		{
			return Loc.T("异常 ") + ex.GetType().Name;
		}
	}

	internal int ProbeDefaultClientHeight()
	{
		return _defaultClientH;
	}

	internal HelpPanel ProbeHelpPanel => _helpPanel;

	internal string ProbeAboutText
	{
		get
		{
			try
			{
				Card card = ProbeCardByTitle(this, Loc.T("关于")) as Card;
				if (card == null)
				{
					return "";
				}
				StringBuilder stringBuilder = new StringBuilder();
				foreach (Control control in card.Controls)
				{
					if (!string.IsNullOrEmpty(control.Text))
					{
						stringBuilder.Append(control.Text).Append(' ');
					}
				}
				return stringBuilder.ToString();
			}
			catch
			{
				return "";
			}
		}
	}

	internal Button ProbeGitHubButton => _btnGitHub;

	internal Button ProbeCheckUpdateButton => _btnCheckUpdate;

	internal CheckBox ProbeAutoUpdateCheck => _chkAutoUpdate;

	/// <summary>自检/调试：语言下拉框（用来模拟用户切换语言）。</summary>
	internal FlatCombo ProbeLanguageCombo => _cmbLanguage;

	internal Label ProbeUpdateStatusLabel => _lblUpdateStatus;

	// v1.7.0：令牌相关的探针（自检用来断言对齐与无障碍，不需要真的弹对话框）
	internal TextBox ProbeTokenBox => _txtToken;

	internal Button ProbeTokenHelpButton => _btnTokenHelp;

	internal Button ProbeSaveTokenButton => _btnSaveToken;

	internal Button ProbeVerifyTokenButton => _btnVerifyToken;

	internal Label ProbeTokenVerifyLabel => _lblTokenVerify;

	internal Label ProbeTokenStateLabel => _lblTokenState;

	internal static bool ProbeForceOfflineForTest
	{
		get
		{
			return Updater.ForceOfflineForTest;
		}
		set
		{
			Updater.ForceOfflineForTest = value;
		}
	}

	internal static FontFamily IconFamilyForTest => Icons.Family;

	internal static string[] IconGlyphsForTest
	{
		get
		{
			return new string[8] { Glyph.Keyboard, Glyph.Move, Glyph.Color, Glyph.Speed, Glyph.Shield, Glyph.Chevron, Glyph.Help, Glyph.Info };
		}
	}

	internal static int MeasureGlyphInk(string glyph, float px)
	{
		try
		{
			FontFamily family = Icons.Family;
			if (family == null || string.IsNullOrEmpty(glyph))
			{
				return 0;
			}
			int num = Math.Max(8, (int)Math.Ceiling(px * 2.0));
			using Bitmap bitmap = new Bitmap(num, num);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.White);
				using Font font = new Font(family, px, FontStyle.Regular, GraphicsUnit.Pixel);
				TextRenderer.DrawText(graphics, glyph, font, new Rectangle(0, 0, num, num), Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
			}
			int num2 = 0;
			for (int i = 0; i < num; i++)
			{
				for (int j = 0; j < num; j++)
				{
					Color pixel = bitmap.GetPixel(j, i);
					if (pixel.R < 250 || pixel.G < 250 || pixel.B < 250)
					{
						num2++;
					}
				}
			}
			return num2;
		}
		catch
		{
			return 0;
		}
	}

	internal static bool VerifyLayout(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			List<Card> list = AllCards(settingsForm);
			if (list.Count < 6)
			{
				return false;
			}
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].Controls.Count == 0)
				{
					return false;
				}
				if (!ProbeGroupInTree(f, i))
				{
					return false;
				}
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	internal static bool VerifyResizable(Form f)
	{
		try
		{
			return f.FormBorderStyle == FormBorderStyle.Sizable && f.MaximizeBox && f.MinimumSize.Width >= 400 && f.MinimumSize.Height >= 300;
		}
		catch
		{
			return false;
		}
	}

	internal static bool VerifyAnchors(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			// 窄：单列（限宽 880、居中）；宽（最大化那种）：两列，把右边那片空白用起来。
			settingsForm.ClientSize = new Size(Sc(900), Sc(700));
			LayoutWithContentOf(settingsForm);
			int width = CardBoxOf(settingsForm).Width;
			int num = ProbeCardsPerRow(f);
			settingsForm.ClientSize = new Size(Sc(1400), Sc(700));
			LayoutWithContentOf(settingsForm);
			Rectangle cardBox = CardBoxOf(settingsForm);
			settingsForm.ClientSize = new Size(Sc(2400), Sc(700));
			SettingsForm.ProbeSelectPage(settingsForm, 1);
			Application.DoEvents();
			int num2 = ProbeCardsPerRow(f);
			Rectangle cardBox2 = CardBoxOf(settingsForm);
			settingsForm.ClientSize = new Size(Sc(900), Sc(700));
			SettingsForm.ProbeSelectPage(settingsForm, 0);
			LayoutWithContentOf(settingsForm);
			int width2 = CardBoxOf(settingsForm).Width;
			LastStretchDetail = Loc.T("卡片宽 ") + width + "->" + cardBox.Width + "->" + width2 + Loc.T(" | 每行张数 ") + num + "->" + num2 + Loc.T(" | 宽窗口下卡片 ") + cardBox2.ToString();
			return width2 == width && cardBox.Width > width && cardBox.Width <= Sc(Tokens.CardMaxW) + Sc(2) && num == 1 && num2 == 2;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>当前页"同一行"有几张卡片（1 = 单列，2 = 两列）。</summary>
	/// <summary>
	/// 侧栏第 index 项右端的实际底色：有胶囊底就是 Hover/AccentSoft，没有就是侧栏底色。
	/// 用真实绘制结果判定，"没选中却有底"这类回归才测得出来。
	/// </summary>
	internal static Color ProbeNavRowColor(Form f, int index)
	{
		try
		{
			if (!(f is SettingsForm settingsForm) || settingsForm._nav == null)
			{
				return Color.Empty;
			}
			SideNav nav = settingsForm._nav;
			if (nav.Width < 40 || nav.Height < 40)
			{
				return Color.Empty;
			}
			using Bitmap bitmap = new Bitmap(nav.Width, nav.Height);
			nav.DrawToBitmap(bitmap, new Rectangle(0, 0, nav.Width, nav.Height));
			int num = (int)Math.Round(84.0 * (double)UiScaleForTest);
			int num2 = (int)Math.Round(38.0 * (double)UiScaleForTest);
			int x = Math.Max(2, nav.Width - (int)Math.Round(18.0 * (double)UiScaleForTest));
			int y = num + index * num2 + num2 / 2;
			if (y < 0 || y >= bitmap.Height)
			{
				return Color.Empty;
			}
			return bitmap.GetPixel(x, y);
		}
		catch
		{
			return Color.Empty;
		}
	}

	internal static int TextLinesForTest(string text, Control ctl, int width)
	{
		// 与卡片排版同一把尺子：折行总高 ÷ 单行高（带硬换行也算得对）
		try
		{
			int num = TextRenderer.MeasureText(Loc.T("字"), ctl.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height;
			int num2 = TextRenderer.MeasureText(text, ctl.Font, new Size(Math.Max(20, width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
			return Math.Max(1, (int)Math.Round((double)num2 / (double)Math.Max(1, num)));
		}
		catch
		{
			return 1;
		}
	}

	internal static int ProbeCardsPerRow(Form f)
	{
		try
		{
			if (f is SettingsForm settingsForm && settingsForm._pages.Count > settingsForm._activePage)
			{
				List<Card> cards = settingsForm._pages[settingsForm._activePage].Cards;
				if (cards.Count > 0)
				{
					int top = cards[0].Top;
					int num = 0;
					foreach (Card card in cards)
					{
						if (card.Top == top)
						{
							num++;
						}
					}
					return num;
				}
			}
		}
		catch
		{
		}
		return 0;
	}

	/// <summary>取当前页第一张卡片的边界（用来量内容列宽与左边距）。</summary>
	internal static Rectangle CardBoxOf(Form f)
	{
		try
		{
			if (f is SettingsForm settingsForm && settingsForm._pages.Count > settingsForm._activePage)
			{
				List<Card> cards = settingsForm._pages[settingsForm._activePage].Cards;
				if (cards.Count > 0)
				{
					return cards[0].Bounds;
				}
			}
		}
		catch
		{
		}
		return Rectangle.Empty;
	}

	internal static string LastStretchDetail = string.Empty;

	internal static bool VerifyInnerStretch(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			// 卡内：整宽块（色板）必须跟着卡片变宽；固定宽的控件（下拉框）保持宽度不变、
			// 但右边界要跟着卡片右边界走（右对齐的代价就是宽度不跟着长）。
			settingsForm.ClientSize = new Size(Sc(900), Sc(720));
			LayoutWithContentOf(settingsForm);
			int width = settingsForm._gridColor.Width;
			int width2 = settingsForm._cmbLeft.Width;
			int num = CardBoxOf(settingsForm).Right - settingsForm._cmbLeft.Right;
			settingsForm.ClientSize = new Size(Sc(1150), Sc(720));
			LayoutWithContentOf(settingsForm);
			int width3 = settingsForm._gridColor.Width;
			int width4 = settingsForm._cmbLeft.Width;
			int num2 = CardBoxOf(settingsForm).Right - settingsForm._cmbLeft.Right;
			LastStretchDetail = Loc.T("色板 ") + width + "->" + width3 + Loc.T(" | 下拉框 ") + width2 + "->" + width4 + Loc.T(" | 右间距 ") + num + "->" + num2;
			return width3 > width && width4 == width2 && num == num2;
		}
		catch
		{
			return false;
		}
	}

	internal static bool VerifyStretchIdempotent(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			// 窄 -> 宽 -> 窄 来回拉 5 轮，回到原尺寸后控件位置必须和一开始完全一致。
			int num = Sc(900);
			int num2 = Sc(1240);
			settingsForm.ClientSize = new Size(num, Sc(720));
			LayoutWithContentOf(settingsForm);
			Rectangle bounds = settingsForm._cmbLeft.Bounds;
			for (int i = 0; i < 5; i++)
			{
				settingsForm.ClientSize = new Size(num2, Sc(720));
				LayoutWithContentOf(settingsForm);
				settingsForm.ClientSize = new Size(num, Sc(720));
				LayoutWithContentOf(settingsForm);
			}
			Rectangle bounds2 = settingsForm._cmbLeft.Bounds;
			LastStretchDetail = "A=" + bounds.ToString() + " B=" + bounds2.ToString();
			return bounds.Left == bounds2.Left && bounds.Width == bounds2.Width && bounds.Top == bounds2.Top;
		}
		catch
		{
			return false;
		}
	}

	private static void LayoutWithContentOf(SettingsForm f)
	{
		// 自检要跨页读几何（比如在"触发方式"页量"视觉反馈"页的色板宽度），
		// 所以测试路径一律全排，不走运行时"只排可见页"的快路径。
		f.LayoutAllPages(f.ContentWidth());
	}

	internal static string DumpLayoutItem(Form f, Control target)
	{
		if (target == null)
		{
			return "(null)";
		}
		try
		{
			return target.GetType().Name + " L=" + target.Left + " T=" + target.Top + " W=" + target.Width + " H=" + target.Height + " vis=" + target.Visible;
		}
		catch
		{
			return "(err)";
		}
	}

	internal static void RelayoutForTest(Form f)
	{
		if (f is SettingsForm settingsForm)
		{
			LayoutWithContentOf(settingsForm);
			settingsForm.Invalidate();
		}
	}

	/// <summary>按运行时的快路径重排一次（只排当前可见页），用于性能自检。</summary>
	internal static void RelayoutActiveForTest(Form f)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.LayoutWithContent(settingsForm.ContentWidth());
		}
	}

	internal static void PopulateForTest(Form f)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.ResetDefaults();
		}
	}

	internal static Rectangle ProbeRectOfTextForTest(Form f, string prefix)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return Rectangle.Empty;
			}
			foreach (Card item in AllCards(settingsForm))
			{
				for (int i = 0; i < item.Controls.Count; i++)
				{
					Control control = item.Controls[i];
					if (control.Text != null && control.Text.StartsWith(prefix))
					{
						return control.Bounds;
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

	internal static void ProbeRescaleForDpi(Form f, int dpi)
	{
		if (f is SettingsForm settingsForm)
		{
			settingsForm.RescaleForDpi(dpi);
		}
	}

	internal static string ProbeLayoutFingerprint(Form f)
	{
		StringBuilder stringBuilder = new StringBuilder();
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			foreach (Card item in AllCards(settingsForm))
			{
				stringBuilder.Append(item.GetType().Name).Append(':').Append(item.Left).Append(',')
					.Append(item.Top)
					.Append(',')
					.Append(item.Width)
					.Append(',')
					.Append(item.Height)
					.Append(';');
			}
			stringBuilder.Append("cmbL=").Append(settingsForm._cmbLeft.Left).Append(',')
				.Append(settingsForm._cmbLeft.Width)
				.Append(';');
			stringBuilder.Append("txtBl=").Append(settingsForm._txtBlacklist.Width).Append(';');
			stringBuilder.Append("numHold=").Append(settingsForm._numHold.Width).Append(';');
			stringBuilder.Append("preview=").Append(settingsForm._pnlPreview.Left).Append(',')
				.Append(settingsForm._pnlPreview.Top)
				.Append(',')
				.Append(settingsForm._pnlPreview.Width)
				.Append(',')
				.Append(settingsForm._pnlPreview.Height)
				.Append(';');
			stringBuilder.Append("grid=").Append(settingsForm._gridColor.Width).Append('x').Append(settingsForm._gridColor.Height)
				.Append(";gal=")
				.Append(settingsForm._galleryStyle.Width)
				.Append('x')
				.Append(settingsForm._galleryStyle.Height)
				.Append(';');
		}
		catch
		{
		}
		return stringBuilder.ToString();
	}

	internal static int ProbeCardMaxWidthPx(Form f)
	{
		return Sc(Tokens.CardMaxW);
	}

		internal static Color ProbeTokenColor(string name)
	{
		switch (name)
		{
		case "Surface":
			return Tokens.Surface;
		case "TextPrimary":
			return Tokens.TextPrimary;
		case "TextSecondary":
			return Tokens.TextSecondary;
		case "Accent":
			return Tokens.Accent;
		case "Canvas":
			return Tokens.Canvas;
		case "Sidebar":
			return Tokens.Sidebar;
		case "AccentSoft":
			return Tokens.AccentSoft;
		default:
			return Color.Empty;
		}
	}

	internal static void ProbeForceDarkTheme(bool? dark)
	{
		Theme.SetForceDark(dark);
	}

	internal static int ProbePreviewRadiusDiff(Form f, int radiusA, int radiusB)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return -1;
			}
			settingsForm.SelectPage(2);
			int width = Math.Max(8, settingsForm._pnlPreview.Width);
			int height = Math.Max(8, settingsForm._pnlPreview.Height);
			using Bitmap bitmap = new Bitmap(width, height);
			using Bitmap bitmap2 = new Bitmap(width, height);
			settingsForm._numFrameRadius.Value = Clamp(radiusA, (int)settingsForm._numFrameRadius.Minimum, (int)settingsForm._numFrameRadius.Maximum);
			settingsForm.SyncPreview();
			settingsForm._pnlPreview.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
			settingsForm._numFrameRadius.Value = Clamp(radiusB, (int)settingsForm._numFrameRadius.Minimum, (int)settingsForm._numFrameRadius.Maximum);
			settingsForm.SyncPreview();
			settingsForm._pnlPreview.DrawToBitmap(bitmap2, new Rectangle(0, 0, width, height));
			int num = 0;
			for (int i = 0; i < height; i++)
			{
				for (int j = 0; j < width; j++)
				{
					if (bitmap.GetPixel(j, i).ToArgb() != bitmap2.GetPixel(j, i).ToArgb())
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
			return -1;
		}
	}

	internal Rectangle ProbePagerBounds()
	{
		return (_pager == null) ? Rectangle.Empty : _pager.Bounds;
	}

	internal int ProbeCaptionButtonCount()
	{
		int num = 0;
		for (int i = 0; i < _titleStrip.Controls.Count; i++)
		{
			if (_titleStrip.Controls[i] is CaptionButton captionButton && captionButton.Visible && captionButton.Width > 0 && captionButton.Height > 0)
			{
				num++;
			}
		}
		return num;
	}

	internal Color ProbeTitleStripColor()
	{
		return _titleStrip.BackColor;
	}

	internal static bool ProbeThemeIsDark()
	{
		return Theme.IsDark;
	}

	internal static RenderStats ProbeFormRender(Form f, string frameColorId)
	{
		RenderStats renderStats = new RenderStats();
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return renderStats;
			}
			settingsForm.SelectPage(2);
			Application.DoEvents();
			int width = f.Width;
			int height = f.Height;
			renderStats.Width = width;
			renderStats.Height = height;
			if (width <= 0 || height <= 0)
			{
				return renderStats;
			}
			using Bitmap bitmap = new Bitmap(width, height);
			f.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
			HashSet<int> hashSet = new HashSet<int>();
			Color color = Color.Gray;
			try
			{
				color = FrameColors.Resolve(frameColorId);
			}
			catch
			{
			}
		long num = 0L;
		int num4 = 0;
		for (int i = 0; i < height; i += 2)
		{
			for (int j = 0; j < width; j += 2)
			{
				Color pixel = bitmap.GetPixel(j, i);
				hashSet.Add(pixel.ToArgb());
				num += (pixel.R + pixel.G + pixel.B) / 3;
				num4++;
				// 只把"纯黑"当成没铺底：深色主题的底色（#17191C）和卡片投影都是有意的颜色，
				// 用 <24 的老阈值会把它们一并算成黑条，深色主题下必假红。
				if (pixel.R < 8 && pixel.G < 8 && pixel.B < 8)
				{
					renderStats.BlackPixels++;
				}
				if (Math.Abs(pixel.R - Tokens.Accent.R) <= 18 && Math.Abs(pixel.G - Tokens.Accent.G) <= 18 && Math.Abs(pixel.B - Tokens.Accent.B) <= 18)
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
		renderStats.MeanBrightness = ((num4 == 0) ? 0.0 : ((double)num / (double)num4));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return renderStats;
	}

	internal static bool ProbeHoverTweenAnimates(out float start, out float end)
	{
		start = 0f;
		end = 0f;
		try
		{
			using FlatButton flatButton = new FlatButton
			{
				Text = Loc.T("悬停探针")
			};
			flatButton.KickHoverForTest();
			start = flatButton.HoverValueForTest;
			int num = 0;
			while (flatButton.HoverValueForTest < 0.999f && num < 240)
			{
				Application.DoEvents();
				Thread.Sleep(8);
				num++;
			}
			end = flatButton.HoverValueForTest;
			return start < 0.05f && end > 0.95f && end > start;
		}
		catch
		{
			return false;
		}
	}

	internal static string ProbeSwitchColors(Form f, bool on)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return Loc.T("异常(no-form)");
			}
			FlatCheck chkHold = settingsForm._chkHold;
			chkHold.SnapCheckForTest(on);
			Application.DoEvents();
			if (chkHold.Width <= 0 || chkHold.Height <= 0)
			{
				return Loc.T("异常(zero-size)");
			}
			using Bitmap bitmap = new Bitmap(chkHold.Width, chkHold.Height);
			chkHold.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
			int r = 0;
			int g = 0;
			int b = 0;
			Sample(bitmap, 1, bitmap.Height / 2, ref r, ref g, ref b);
			Point knobCenter = chkHold.KnobCenter;
			int r2 = 0;
			int g2 = 0;
			int b2 = 0;
			Sample(bitmap, knobCenter.X, knobCenter.Y, ref r2, ref g2, ref b2);
			return Loc.T("轨道=") + r + "," + g + "," + b + Loc.T(" 滑块=") + r2 + "," + g2 + "," + b2 + Loc.T(" 尺寸=") + bitmap.Width + "x" + bitmap.Height;
		}
		catch (Exception ex)
		{
			return Loc.T("异常 ") + ex.GetType().Name;
		}
	}

	private static void Sample(Bitmap bmp, int x, int y, ref int r, ref int g, ref int b)
	{
		x = Math.Max(0, Math.Min(bmp.Width - 1, x));
		y = Math.Max(0, Math.Min(bmp.Height - 1, y));
		Color pixel = bmp.GetPixel(x, y);
		r = pixel.R;
		g = pixel.G;
		b = pixel.B;
	}

	internal static string ProbeGalleryGeometry(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			settingsForm.SelectPage(2);
			StyleGallery galleryStyle = settingsForm._galleryStyle;
			int columns = galleryStyle.ColumnsFor(galleryStyle.Width);
			int num = (galleryStyle.ItemsForTest.Count + columns - 1) / columns;
			return Loc.T("画廊 ") + galleryStyle.ItemsForTest.Count + Loc.T(" 项 ") + columns + Loc.T(" 列 ") + num + Loc.T(" 行 格高=") + galleryStyle.CellH + Loc.T(" 控件高=") + galleryStyle.Height + Loc.T(" 需要=") + (num * (galleryStyle.CellH + galleryStyle.Gap) - galleryStyle.Gap) + Loc.T(" 选中=") + (galleryStyle.SelectedId ?? "(null)");
		}
		catch
		{
			return "";
		}
	}

	internal static bool ProbeGalleryFits(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			settingsForm.SelectPage(2);
			StyleGallery galleryStyle = settingsForm._galleryStyle;
			int columns = galleryStyle.ColumnsFor(galleryStyle.Width);
			int num = (galleryStyle.ItemsForTest.Count + columns - 1) / columns;
			int num2 = num * (galleryStyle.CellH + galleryStyle.Gap) - galleryStyle.Gap;
			return galleryStyle.Height >= num2 && InsideCard(galleryStyle);
		}
		catch
		{
			return false;
		}
	}

	internal static string ProbeColorGridGeometry(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return "";
			}
			settingsForm.SelectPage(2);
			SwatchGrid gridColor = settingsForm._gridColor;
			int columns = gridColor.ColumnsFor(gridColor.Width);
			int num = (gridColor.CellCount + columns - 1) / columns;
			return Loc.T("色板 ") + gridColor.CellCount + Loc.T(" 格 ") + columns + Loc.T(" 列 ") + num + Loc.T(" 行 格高=") + gridColor.Cell + Loc.T(" 控件高=") + gridColor.Height + Loc.T(" 需要=") + (num * (gridColor.Cell + gridColor.Gap) - gridColor.Gap) + Loc.T(" 选中=") + (gridColor.SelectedId ?? "(null)");
		}
		catch
		{
			return "";
		}
	}

	internal static bool ProbeColorGridFits(Form f)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return false;
			}
			settingsForm.SelectPage(2);
			SwatchGrid gridColor = settingsForm._gridColor;
			int columns = gridColor.ColumnsFor(gridColor.Width);
			int num = (gridColor.CellCount + columns - 1) / columns;
			int num2 = num * (gridColor.Cell + gridColor.Gap) - gridColor.Gap;
			return gridColor.Height >= num2 && InsideCard(gridColor);
		}
		catch
		{
			return false;
		}
	}

	internal static void DumpGroup(Form f, int groupIndex, StringBuilder sb)
	{
		try
		{
			if (!(f is SettingsForm settingsForm))
			{
				return;
			}
			List<Card> list = AllCards(settingsForm);
			if (groupIndex < 0 || groupIndex >= list.Count)
			{
				return;
			}
			Control control = list[groupIndex];
			sb.AppendLine(Loc.T("分组[") + groupIndex + "] 「" + control.Text + "」 L=" + control.Left + " T=" + control.Top + " W=" + control.Width + " H=" + control.Height + Loc.T(" 客户区=") + control.ClientSize.Width + "x" + control.ClientSize.Height);
			for (int i = 0; i < control.Controls.Count; i++)
			{
				Control control2 = control.Controls[i];
				sb.AppendLine("   " + control2.GetType().Name + " 「" + ShortText(control2.Text ?? "") + "」 L=" + control2.Left + " T=" + control2.Top + " W=" + control2.Width + " H=" + control2.Height + " vis=" + control2.Visible);
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine(Loc.T("异常 ") + ex.GetType().Name);
		}
	}

	internal static void DumpVisualGroupGeometry(StringBuilder sb, int[] designWidths)
	{
		SettingsForm settingsForm = null;
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "holddrag-vgeom-cfg");
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
			float num = (CurrentUiScale > 0f) ? CurrentUiScale : 1f;
			for (int i = 0; i < designWidths.Length; i++)
			{
				int num2 = (int)Math.Round((float)designWidths[i] * num);
				settingsForm.ClientSize = new Size(num2, 900);
				settingsForm.LayoutWithContent(settingsForm.ClientSize.Width - Sc(208));
				settingsForm.SelectPage(2);
				Application.DoEvents();
				int width = settingsForm.ClientSize.Width;
				sb.Append(Loc.T("══ 客户区宽=")).Append(width).Append(Loc.T("（设计稿 ")).Append(designWidths[i]).Append("px）════")
					.AppendLine();
				List<Card> list = AllCards(settingsForm);
				for (int j = 0; j < list.Count; j++)
				{
					Control control = list[j];
					sb.Append("  z=").Append(j).Append(' ')
						.Append(control.GetType().Name)
						.Append(" vis=")
						.Append(control.Visible)
						.Append(" L=")
						.Append(control.Left)
						.Append(" T=")
						.Append(control.Top)
						.Append(" W=")
						.Append(control.Width)
						.Append(" H=")
						.Append(control.Height)
						.Append(Loc.T(" 右="))
						.Append(control.Right)
						.Append(Loc.T(" 底="))
						.Append(control.Bottom)
						.AppendLine();
				}
				FramePreviewBox pnlPreview = settingsForm._pnlPreview;
				sb.Append("  z=0 FramePreviewBox vis=").Append(pnlPreview.Visible).Append(" L=")
					.Append(pnlPreview.Left)
					.Append(" T=")
					.Append(pnlPreview.Top)
					.Append(" W=")
					.Append(pnlPreview.Width)
					.Append(" H=")
					.Append(pnlPreview.Height)
					.Append(Loc.T(" 右="))
					.Append(pnlPreview.Right)
					.Append(Loc.T(" 底="))
					.Append(pnlPreview.Bottom)
					.AppendLine();
			}
		}
		catch (Exception ex)
		{
			sb.AppendLine(Loc.T("异常 ") + ex.GetType().Name + ": " + ex.Message);
		}
		finally
		{
			try
			{
				settingsForm?.Close();
				settingsForm?.Dispose();
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// 调试/自检用：按指定语言逐页截图设置窗口。
	/// 目的很具体——译文一变长，就可能溢出胶囊、按钮或折行错位，光看代码看不出来，必须真画一遍。
	/// 用法：HoldDrag.exe --langshot en
	/// </summary>

	/// <summary>
	/// UI 截图（设计迭代用）：只截**客户区**，明暗两套主题各一组。
	///
	/// 为什么不用 Form.DrawToBitmap：它会把系统标题栏一起画进来，而本窗口用
	/// WM_NCCALCSIZE 把标题栏吃掉了 —— 截出来的图顶部会盖一条假的标题栏，
	/// 看起来像"内容被裁掉"，完全没法拿来做视觉评审。这里改成把四个顶层子控件
	/// （侧栏 / 标题条 / 内容 / 底栏）分别 DrawToBitmap 再拼回去，得到的就是真实客户区。
	/// </summary>
	internal static void DumpUiShots(string outDir)
	{
		Directory.CreateDirectory(outDir);
		string text = Path.Combine(Path.GetTempPath(), "holddrag-uishot-cfg");
		try
		{
			Directory.CreateDirectory(text);
		}
		catch
		{
		}
		foreach (bool dark in new bool[2] { false, true })
		{
			SettingForceThemeForTest(dark);
			ConfigStore configStore = new ConfigStore(text);
			try
			{
				configStore.Load();
			}
			catch
			{
			}
			configStore.Current.Language = Loc.Zh;
			Coordinator coord = new Coordinator(configStore);
			using SettingsForm form = new SettingsForm(configStore, coord);
			form.StartPosition = FormStartPosition.Manual;
			form.Location = new Point(20, 20);
			form.ClientSize = new Size(1100, 760);
			form.Show();
			Application.DoEvents();
			int count = form._pages.Count;
			for (int i = 0; i < count; i++)
			{
				form.SelectPage(i);
				Application.DoEvents();
				Thread.Sleep(90);
				Application.DoEvents();
				using Bitmap bitmap = CaptureClient(form);
				bitmap.Save(Path.Combine(outDir, string.Format("page{0}-{1}.png", i, dark ? "dark" : "light")), ImageFormat.Png);
			}
			// 下拉框弹层是独立窗口，DrawToBitmap 抓不到，只能屏幕拷贝
			try
			{
				form.SelectPage(2);
				Application.DoEvents();
				Thread.Sleep(120);
				form.ProbeOpenColorPopup();
				Thread.Sleep(260);
				Application.DoEvents();
				Rectangle bounds = form.ProbeColorPopupBounds();
				if (bounds.Width > 20 && bounds.Height > 20)
				{
					using Bitmap bitmap2 = new Bitmap(bounds.Width, bounds.Height);
					using (Graphics graphics = Graphics.FromImage(bitmap2))
					{
						graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
					}
					bitmap2.Save(Path.Combine(outDir, "popup-color-" + (dark ? "dark" : "light") + ".png"), ImageFormat.Png);
				}
				form.ProbeCloseColorPopup();
				Thread.Sleep(120);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		SettingForceThemeForTest(null);
	}

	/// <summary>把窗体客户区拼成一张图（不含系统非客户区）。</summary>
	internal static Bitmap CaptureClient(Form form)
	{
		Bitmap bitmap = new Bitmap(Math.Max(1, form.ClientSize.Width), Math.Max(1, form.ClientSize.Height));
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.Clear(Theme.IsDark ? Color.FromArgb(22, 24, 29) : Color.FromArgb(242, 245, 250));
		}
		foreach (Control control in form.Controls)
		{
			if (control == null || !control.Visible || control.Width <= 0 || control.Height <= 0)
			{
				continue;
			}
			try
			{
				using Bitmap bitmap2 = new Bitmap(control.Width, control.Height);
				control.DrawToBitmap(bitmap2, new Rectangle(0, 0, control.Width, control.Height));
				using Graphics graphics2 = Graphics.FromImage(bitmap);
				graphics2.DrawImageUnscaled(bitmap2, control.Left, control.Top);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		return bitmap;
	}

	internal static void SettingForceThemeForTest(bool? dark)
	{
		Theme.SetForceDark(dark);
	}

	internal static void DumpLanguageShots(string outDir, string lang)
	{
		// 刻意从中文起步，再走"设置里改下拉框"这条真实路径切到目标语言：
		// 这样截出来的图验证的是**运行时切换**（含重建界面 + 写配置），而不只是启动时读配置。
		Loc.Init(Loc.Zh);
		Directory.CreateDirectory(outDir);
		string text = Path.Combine(Path.GetTempPath(), "holddrag-langshot-cfg-" + Loc.Normalize(lang));
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
		configStore.Current.Language = Loc.Normalize(lang);
		Coordinator coord = new Coordinator(configStore);
		using SettingsForm settingsForm = new SettingsForm(configStore, coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.ClientSize = new Size(1080, 760);
		settingsForm.Show();
		Application.DoEvents();
		if (Loc.Normalize(lang) != Loc.Zh)
		{
			try
			{
				settingsForm.ProbeLanguageCombo.SelectedIndex = 1;
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			// 切换是"保存 → 重建"两步，重建排在消息循环下一轮，得多泵几轮
			for (int k = 0; k < 12; k++)
			{
				Application.DoEvents();
				Thread.Sleep(60);
			}
		}
		int count = settingsForm._pages.Count;
		for (int i = 0; i < count; i++)
		{
			settingsForm.SelectPage(i);
			Application.DoEvents();
			Thread.Sleep(120);
			Application.DoEvents();
			using Bitmap bitmap = new Bitmap(settingsForm.Width, settingsForm.Height);
			settingsForm.DrawToBitmap(bitmap, new Rectangle(0, 0, settingsForm.Width, settingsForm.Height));
			bitmap.Save(Path.Combine(outDir, "page" + i + "-" + Loc.Normalize(lang) + ".png"), ImageFormat.Png);
		}
		// 更新相关窗口也一并截图：它们是独立 Form，藏在设置页里面，肉眼核对不到
		try
		{
			UpdateInfo updateInfo = new UpdateInfo
			{
				Tag = "v9.9.9",
				Version = "9.9.9",
				AssetUrl = "https://example.com/HoldDrag.exe",
				PageUrl = "https://example.com/releases"
			};
			using (UpdatePromptForm updatePromptForm = new UpdatePromptForm(updateInfo))
			{
				updatePromptForm.StartPosition = FormStartPosition.Manual;
				updatePromptForm.Location = new Point(20, 20);
				updatePromptForm.Show();
				Application.DoEvents();
				using Bitmap bitmap = new Bitmap(updatePromptForm.Width, updatePromptForm.Height);
				updatePromptForm.DrawToBitmap(bitmap, new Rectangle(0, 0, updatePromptForm.Width, updatePromptForm.Height));
				bitmap.Save(Path.Combine(outDir, "dialog-prompt-" + Loc.Normalize(lang) + ".png"), ImageFormat.Png);
				updatePromptForm.Close();
			}
			// 下载窗口用 previewOnly 构造：会真的显示出来排版，但绝不发起下载
			using (DownloadForm downloadForm = new DownloadForm(updateInfo, previewOnly: true))
			{
				downloadForm.StartPosition = FormStartPosition.Manual;
				downloadForm.Location = new Point(20, 20);
				downloadForm.Show();
				Application.DoEvents();
				using Bitmap bitmap2 = new Bitmap(downloadForm.Width, downloadForm.Height);
				downloadForm.DrawToBitmap(bitmap2, new Rectangle(0, 0, downloadForm.Width, downloadForm.Height));
				bitmap2.Save(Path.Combine(outDir, "dialog-download-" + Loc.Normalize(lang) + ".png"), ImageFormat.Png);
				downloadForm.Close();
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		Log.Info("语言截图已输出：" + outDir + "（" + Loc.Normalize(lang) + "，" + count + " 页）");
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
			settingsForm.SelectPage(2);
			Application.DoEvents();
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine(Loc.T("=== 路径 A：直接设 StyleId 字段 ==="));
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
				stringBuilder.AppendLine("  " + text2 + " size=" + bitmap.Width + "x" + bitmap.Height + Loc.T(" 像素和=") + num + Loc.T(" 非白像素=") + num2 + Loc.T(" 文件=") + Path.GetFileName(text3));
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine(Loc.T("=== 路径 B：改下拉框 SelectedIndex（模拟用户点选）==="));
			stringBuilder.AppendLine(Loc.T("  下拉框里共 ") + settingsForm._cmbFrameStyle.Items.Count + Loc.T(" 项"));
			for (int l = 0; l < settingsForm._cmbFrameStyle.Items.Count; l++)
			{
				settingsForm._cmbFrameStyle.SelectedIndex = l;
				Application.DoEvents();
				string styleId = settingsForm._pnlPreview.StyleId;
				object obj = settingsForm._cmbFrameStyle.Items[l];
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
				stringBuilder.AppendLine("  index=" + l + Loc.T(" 下拉项文字=\"") + ((obj != null) ? obj.ToString() : "(null)") + Loc.T("\" → 预览 StyleId=\"") + styleId + Loc.T("\" 像素和=") + num3 + Loc.T(" 非白像素=") + num4 + ((styleId == settingsForm._pnlPreview.StyleId) ? "" : Loc.T("  ← ★ 不一致！")));
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine(Loc.T("★ 路径 A 各行 [像素和] 全不同 → 样式确实画出了不同结果；"));
			stringBuilder.AppendLine(Loc.T("★ 路径 B 各行 [像素和] 全不同 → 下拉框连线正常，改选项能刷新预览；"));
			stringBuilder.AppendLine(Loc.T("  若路径 B 有任意两行相同，就是[选了形状但预览不变]的真 bug。"));
			stringBuilder.AppendLine();
			stringBuilder.AppendLine(Loc.T("=== 路径 C：加载真实配置后的预览状态 ==="));
			try
			{
				ConfigStore configStore2 = new ConfigStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag"));
				configStore2.Load();
				Coordinator coord2 = new Coordinator(configStore2);
				AppConfig current = configStore2.Current;
				stringBuilder.AppendLine(Loc.T("  配置：颜色=") + current.FrameColorId + Loc.T(" 样式=") + current.FrameStyleId + Loc.T(" 粗细=") + current.FrameThickness + Loc.T(" 圆角=") + current.FrameRadius + Loc.T(" 外框开关=") + current.ShowActivationFrame);
				SettingsForm settingsForm2 = new SettingsForm(configStore2, coord2);
				settingsForm2.StartPosition = FormStartPosition.Manual;
				settingsForm2.Location = new Point(20, 20);
				settingsForm2.ClientSize = new Size(900, 900);
				settingsForm2.Show();
				settingsForm2.SelectPage(2);
				Application.DoEvents();
				for (int num5 = 0; num5 < 4; num5++)
				{
					Application.DoEvents();
					Thread.Sleep(80);
				}
				stringBuilder.AppendLine(Loc.T("  下拉框选中项：颜色=\"") + ((settingsForm2._cmbFrameColor.SelectedItem != null) ? settingsForm2._cmbFrameColor.SelectedItem.ToString() : "(null)") + Loc.T("\" 样式=\"") + ((settingsForm2._cmbFrameStyle.SelectedItem != null) ? settingsForm2._cmbFrameStyle.SelectedItem.ToString() : "(null)") + "\"");
				stringBuilder.AppendLine(Loc.T("  预览控件字段：ColorId=\"") + settingsForm2._pnlPreview.ColorId + "\" StyleId=\"" + settingsForm2._pnlPreview.StyleId + "\" Thickness=" + settingsForm2._pnlPreview.Thickness + " Radius=" + settingsForm2._pnlPreview.Radius + " Enabled=" + settingsForm2._pnlPreview.FrameEnabled);
				FramePreviewBox pnlPreview3 = settingsForm2._pnlPreview;
				using (Bitmap bitmap3 = new Bitmap(pnlPreview3.Width, pnlPreview3.Height))
				{
					pnlPreview3.DrawToBitmap(bitmap3, new Rectangle(0, 0, pnlPreview3.Width, pnlPreview3.Height));
					bitmap3.Save(Path.Combine(outDir, "preview-REALCONFIG.png"), ImageFormat.Png);
					int num6 = 0;
					for (int num7 = 0; num7 < bitmap3.Width; num7++)
					{
						if (bitmap3.GetPixel(num7, bitmap3.Height / 2).A != 0)
						{
							num6++;
						}
					}
					stringBuilder.AppendLine(Loc.T("  中线非透明像素=") + num6 + " / " + bitmap3.Width);
				}
				try
				{
					settingsForm2.Close();
					settingsForm2.Dispose();
				}
				catch
				{
				}
			}
			catch (Exception ex2)
			{
				stringBuilder.AppendLine(Loc.T("  路径 C 失败：") + ex2.Message);
			}
			File.WriteAllText(Path.Combine(outDir, "previewtest.txt"), stringBuilder.ToString(), Encoding.UTF8);
		}
		catch (Exception ex3)
		{
			Log.Error(ex3);
		}
		finally
		{
			try
			{
				settingsForm?.Close();
				settingsForm?.Dispose();
			}
			catch
			{
			}
		}
	}
}
