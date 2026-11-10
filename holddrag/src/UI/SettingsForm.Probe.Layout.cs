// HoldDrag · UI/SettingsForm.Probe.Layout.cs
//
// 探针面：分页 / 卡片 / 分组 / 滚动 / 重排 的几何与一致性校验。

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
	/// <summary>自测用：切到指定页。</summary>
	internal void ProbeSelectPage(int index)
	{
		SelectPage(index);
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
	internal int ProbeDefaultClientHeight()
	{
		return _defaultClientH;
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
			return width2 == width && cardBox.Width > width && cardBox.Width <= Sc(UiTheme.CardMaxW) + Sc(2) && num == 1 && num2 == 2;
		}
		catch
		{
			return false;
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
		return Sc(UiTheme.CardMaxW);
	}

		internal static Color ProbeTokenColor(string name)
	{
		switch (name)
		{
		case "Surface":
			return UiTheme.Surface;
		case "TextPrimary":
			return UiTheme.TextPrimary;
		case "TextSecondary":
			return UiTheme.TextSecondary;
		case "Accent":
			return UiTheme.Accent;
		case "Canvas":
			return UiTheme.Canvas;
		case "Sidebar":
			return UiTheme.Sidebar;
		case "AccentSoft":
			return UiTheme.AccentSoft;
		default:
			return Color.Empty;
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
}
