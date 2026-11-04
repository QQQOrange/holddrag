// HoldDrag · UI/AuroraTheme.cs
//
// 托盘菜单 / 更新弹窗这类「设置窗口之外」的界面也共用同一套颜色与字体。
// 设置页自己的 Tokens 是 private 的，这里抽一份公共版本，避免把整个 SettingsForm 暴露出去；
// 两边取值必须保持一致（测试 T30 会对照关键色值）。

using System;
using System.Drawing;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HoldDrag;

internal static class AuroraTheme
{
	private static bool _probed;

	private static bool _dark;

	/// <summary>跟系统「应用模式」走（HKCU Personalize\AppsUseLightTheme）。</summary>
	public static bool IsDark
	{
		get
		{
			if (!_probed)
			{
				Refresh();
			}
			return _dark;
		}
	}

	public static bool Refresh()
	{
		bool dark = DetectDark();
		bool changed = _probed && dark != _dark;
		_dark = dark;
		_probed = true;
		return changed;
	}

	private static bool DetectDark()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			if (registryKey != null && registryKey.GetValue("AppsUseLightTheme") is int num)
			{
				return num == 0;
			}
		}
		catch
		{
		}
		return false;
	}

	private static Color P(Color light, Color dark)
	{
		return IsDark ? dark : light;
	}

	public static Color Canvas => P(Color.FromArgb(247, 247, 251), Color.FromArgb(11, 12, 16));

	public static Color Surface => P(Color.FromArgb(255, 255, 255), Color.FromArgb(21, 22, 27));

	public static Color SurfaceAlt => P(Color.FromArgb(249, 249, 253), Color.FromArgb(18, 19, 24));

	public static Color Stroke => P(Color.FromArgb(233, 233, 241), Color.FromArgb(36, 38, 46));

	public static Color Divider => P(Color.FromArgb(240, 240, 246), Color.FromArgb(30, 32, 39));

	public static Color Hover => P(Color.FromArgb(242, 242, 249), Color.FromArgb(27, 29, 36));

	public static Color TextPrimary => P(Color.FromArgb(20, 21, 27), Color.FromArgb(239, 240, 245));

	public static Color TextSecondary => P(Color.FromArgb(91, 95, 112), Color.FromArgb(165, 169, 186));

	public static Color TextTertiary => P(Color.FromArgb(142, 146, 165), Color.FromArgb(112, 116, 137));

	public static Color Accent => P(Color.FromArgb(91, 91, 214), Color.FromArgb(145, 140, 255));

	public static Color AccentSoft => P(Color.FromArgb(239, 237, 254), Color.FromArgb(26, 25, 48));

	public static Color TextOnAccent => P(Color.FromArgb(255, 255, 255), Color.FromArgb(18, 16, 31));

	public static Color Danger => P(Color.FromArgb(220, 38, 38), Color.FromArgb(248, 113, 113));

	public const int Radius = 10;

	/// <summary>按像素字号取设计字体（和设置页同一族，保证两处字形一致）。</summary>
	public static Font Font(float px, bool bold = false)
	{
		try
		{
			return new Font(SettingsForm.DesignFontFamily, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
		}
		catch
		{
			return Control.DefaultFont;
		}
	}

	public static GraphicsPath RoundRect(Rectangle r, int radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		int num = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
		if (num <= 0)
		{
			graphicsPath.AddRectangle(r);
			return graphicsPath;
		}
		graphicsPath.AddArc(r.X, r.Y, num * 2, num * 2, 180f, 90f);
		graphicsPath.AddArc(r.Right - num * 2, r.Y, num * 2, num * 2, 270f, 90f);
		graphicsPath.AddArc(r.Right - num * 2, r.Bottom - num * 2, num * 2, num * 2, 0f, 90f);
		graphicsPath.AddArc(r.X, r.Bottom - num * 2, num * 2, num * 2, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}

/// <summary>
/// 弹窗里的扁平按钮（更新提示 / 下载窗口用）：圆角、悬停、主次两种样式，
/// 和设置页的按钮同一套视觉。系统 Button 在深色主题下是一块浅灰，必须换掉。
/// </summary>
internal sealed class AuroraButton : Button
{
	private bool _hover;

	private bool _down;

	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	public bool Primary { get; set; }

	public AuroraButton()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
		FlatStyle = FlatStyle.Flat;
		FlatAppearance.BorderSize = 0;
		Cursor = Cursors.Hand;
		Font = AuroraTheme.Font(13f);
		BackColor = AuroraTheme.Surface;
		ForeColor = AuroraTheme.TextPrimary;
		AutoSize = true;
		AutoSizeMode = AutoSizeMode.GrowAndShrink;
		MinimumSize = new Size(96, 34);
		Padding = new Padding(14, 0, 14, 0);
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
		_down = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		_down = true;
		Invalidate();
		base.OnMouseDown(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		_down = false;
		Invalidate();
		base.OnMouseUp(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
		graphics.Clear((Parent != null) ? Parent.BackColor : AuroraTheme.Surface);
		Rectangle rect = new Rectangle(0, 0, Math.Max(2, Width - 1), Math.Max(2, Height - 1));
		using GraphicsPath path = AuroraTheme.RoundRect(rect, AuroraTheme.Radius);
		Color color;
		Color foreColor;
		if (!Enabled)
		{
			color = AuroraTheme.SurfaceAlt;
			foreColor = AuroraTheme.TextTertiary;
		}
		else if (Primary)
		{
			color = (_down ? ControlPaint.Dark(AuroraTheme.Accent, 0.08f) : (_hover ? ControlPaint.Light(AuroraTheme.Accent, 0.08f) : AuroraTheme.Accent));
			foreColor = AuroraTheme.TextOnAccent;
		}
		else
		{
			color = (_down ? AuroraTheme.Divider : (_hover ? AuroraTheme.Hover : AuroraTheme.Surface));
			foreColor = AuroraTheme.TextPrimary;
		}
		using (SolidBrush brush = new SolidBrush(color))
		{
			graphics.FillPath(brush, path);
		}
		if (!Primary && Enabled)
		{
			using Pen pen = new Pen(AuroraTheme.Stroke);
			graphics.DrawPath(pen, path);
		}
		TextRenderer.DrawText(graphics, Text, Font, rect, foreColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
	}
}

/// <summary>托盘菜单的渲染器：圆角高亮、分隔线、深色/浅色跟随，和设置页同一套颜色。</summary>
internal sealed class AuroraMenuRenderer : ToolStripProfessionalRenderer
{
	public AuroraMenuRenderer()
		: base(new AuroraColorTable())
	{
	}

	protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
	{
		using SolidBrush brush = new SolidBrush(AuroraTheme.Surface);
		e.Graphics.FillRectangle(brush, e.AffectedBounds);
	}

	protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Rectangle rect = new Rectangle(0, 0, Math.Max(2, e.ToolStrip.Width - 1), Math.Max(2, e.ToolStrip.Height - 1));
		using GraphicsPath path = AuroraTheme.RoundRect(rect, 12);
		using Pen pen = new Pen(AuroraTheme.Stroke);
		graphics.DrawPath(pen, path);
	}

	protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
	{
		if (!e.Item.Selected && !e.Item.Pressed)
		{
			return;
		}
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Rectangle rect = new Rectangle(3, 1, Math.Max(2, e.Item.Width - 6), Math.Max(2, e.Item.Height - 2));
		using GraphicsPath path = AuroraTheme.RoundRect(rect, 8);
		using SolidBrush brush = new SolidBrush(e.Item.Pressed ? AuroraTheme.Divider : AuroraTheme.Hover);
		graphics.FillPath(brush, path);
	}

	protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
	{
		e.TextColor = e.Item.Enabled ? (e.Item.Selected ? AuroraTheme.Accent : AuroraTheme.TextPrimary) : AuroraTheme.TextTertiary;
		base.OnRenderItemText(e);
	}

	protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
	{
		Graphics graphics = e.Graphics;
		using Pen pen = new Pen(AuroraTheme.Divider);
		int num = e.Item.Height / 2;
		graphics.DrawLine(pen, 10, num, Math.Max(12, e.Item.Width - 10), num);
	}

	protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
	{
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		Rectangle rect = new Rectangle(e.ImageRectangle.X + 2, e.ImageRectangle.Y + 3, 14, 14);
		using GraphicsPath path = AuroraTheme.RoundRect(rect, 4);
		using (SolidBrush brush = new SolidBrush(AuroraTheme.Accent))
		{
			graphics.FillPath(brush, path);
		}
		using Pen pen = new Pen(AuroraTheme.TextOnAccent, 1.8f);
		pen.StartCap = LineCap.Round;
		pen.EndCap = LineCap.Round;
		graphics.DrawLines(pen, new PointF[3]
		{
			new PointF(rect.Left + 3.4f, rect.Top + 7.2f),
			new PointF(rect.Left + 6.2f, rect.Top + 10f),
			new PointF(rect.Left + 10.8f, rect.Top + 4.4f)
		});
	}

	private sealed class AuroraColorTable : ProfessionalColorTable
	{
		public override Color ToolStripDropDownBackground => AuroraTheme.Surface;

		public override Color MenuItemSelected => AuroraTheme.Hover;

		public override Color MenuItemSelectedGradientBegin => AuroraTheme.Hover;

		public override Color MenuItemSelectedGradientEnd => AuroraTheme.Hover;

		public override Color MenuItemPressedGradientBegin => AuroraTheme.Divider;

		public override Color MenuItemPressedGradientEnd => AuroraTheme.Divider;

		public override Color MenuBorder => AuroraTheme.Stroke;

		public override Color MenuItemBorder => Color.Transparent;

		public override Color SeparatorDark => AuroraTheme.Divider;

		public override Color SeparatorLight => AuroraTheme.Divider;
	}
}
