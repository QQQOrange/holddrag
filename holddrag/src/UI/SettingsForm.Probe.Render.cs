// HoldDrag · UI/SettingsForm.Probe.Render.cs
//
// 探针面：墨迹统计、像素采样、截图与主题切换验证。

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
			framePickerProbe.GridInk = InkOf(_gridColor, UiTheme.Surface);
			framePickerProbe.GalleryInk = InkOf(_galleryStyle, UiTheme.Surface);
			framePickerProbe.GridAccentInk = InkNear(_gridColor, UiTheme.Accent, 60);
			framePickerProbe.GalleryAccentInk = InkNear(_galleryStyle, UiTheme.Accent, 60);
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
	internal static FontFamily IconFamilyForTest => Icons.Family;
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
				if (Math.Abs(pixel.R - UiTheme.Accent.R) <= 18 && Math.Abs(pixel.G - UiTheme.Accent.G) <= 18 && Math.Abs(pixel.B - UiTheme.Accent.B) <= 18)
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
