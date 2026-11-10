// HoldDrag · UI/SettingsForm.Visuals.cs

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
	private void UpdateMaximizeGlyph()
	{
		try
		{
			if (_btnMax != null)
			{
				_btnMax.IsMaximized = (WindowState == FormWindowState.Maximized);
			}
		}
		catch
		{
		}
	}

	// ======================= 色板 =======================

	private sealed class SwatchGrid : Control, INeedHeight
	{

		public SwatchGrid()
		{
			// 自绘控件必须自己开双缓冲：不然每次悬停 Invalidate 都会先擦背景再重画，
			// 肉眼看到的就是「鼠标移上去整块闪一下」。
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			DoubleBuffered = true;
			BackColor = UiTheme.Surface;
		}

		/// <summary>
		/// 双缓冲 + AllPaintingInWmPaint 之后背景由 OnPaint 自己画；
		/// 再把系统擦背景这一步挡掉，彻底消除「先擦成底色再画内容」的那一帧闪烁。
		/// </summary>
		protected override void OnPaintBackground(PaintEventArgs e)
		{
		}
		public sealed class Entry
		{
			public string Id;
			public string Name;
			public Color Color;
		}

		private readonly List<Entry> _items = new List<Entry>();
		private int _sel;
		private int _hot = -1;
		public event EventHandler Picked;

		public int Cell => Sc(30);
		public int Gap => Sc(8);

		public void SetItems(List<Entry> items)
		{
			_items.Clear();
			_items.AddRange(items);
			if (_sel >= _items.Count)
			{
				_sel = 0;
			}
			Invalidate();
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string SelectedId
		{
			get
			{
				if (_sel >= 0 && _sel < _items.Count)
				{
					return _items[_sel].Id;
				}
				return null;
			}
			set
			{
				for (int i = 0; i < _items.Count; i++)
				{
					if (_items[i].Id == value)
					{
						_sel = i;
						Invalidate();
						break;
					}
				}
			}
		}

		public int CellCount => _items.Count;

		/// <summary>自测用：模拟点第 index 个色块（走和鼠标一样的 Picked 路径）。</summary>
		internal void ProbeClick(int index)
		{
			if (index < 0 || index >= _items.Count)
			{
				return;
			}
			_sel = index;
			Invalidate();
			Picked?.Invoke(this, EventArgs.Empty);
		}

		internal string ProbeIdAt(int index)
		{
			return (index >= 0 && index < _items.Count) ? _items[index].Id : null;
		}

		public Rectangle SelectedCellRect
		{
			get
			{
				if (_sel < 0 || _sel >= _items.Count)
				{
					return Rectangle.Empty;
				}
				int columns = ColumnsFor(Width);
				int num = _sel % columns;
				int num2 = _sel / columns;
				return new Rectangle(num * (Cell + Gap), num2 * (Cell + Gap), Cell, Cell);
			}
		}

		public int ColumnsFor(int width)
		{
			int num = Cell + Gap;
			int num2 = (width + Gap) / num;
			if (num2 < 4)
			{
				num2 = 4;
			}
			if (num2 > 12)
			{
				num2 = 12;
			}
			return num2;
		}

		public int HeightFor(int width)
		{
			int columns = ColumnsFor(width);
			int num = (_items.Count + columns - 1) / columns;
			return Math.Max(Cell, num * (Cell + Gap) - Gap);
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = Hit(e.X, e.Y);
			if (num != _hot)
			{
				_hot = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = -1;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				int num = Hit(e.X, e.Y);
				if (num >= 0)
				{
					_sel = num;
					Invalidate();
					Picked?.Invoke(this, EventArgs.Empty);
				}
			}
			base.OnMouseUp(e);
		}

		private int Hit(int x, int y)
		{
			int columns = ColumnsFor(Width);
			int num = x / (Cell + Gap);
			int num2 = y / (Cell + Gap);
			if (num < 0 || num2 < 0 || num >= columns)
			{
				return -1;
			}
			int num3 = num2 * columns + num;
			if (num3 >= _items.Count)
			{
				return -1;
			}
			return num3;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear(UiTheme.Surface);
			int columns = ColumnsFor(Width);
			for (int i = 0; i < _items.Count; i++)
			{
				int num = i % columns;
				int num2 = i / columns;
				Rectangle rect = new Rectangle(num * (Cell + Gap), num2 * (Cell + Gap), Cell, Cell);
				bool flag = i == _sel;
				bool flag2 = i == _hot;
				using (GraphicsPath path = RoundedPath(new Rectangle(rect.X + Sc(2), rect.Y + Sc(2), Cell - Sc(4), Cell - Sc(4)), Sc(UiTheme.RadiusField)))
				{
					using (SolidBrush brush = new SolidBrush(_items[i].Color))
					{
						graphics.FillPath(brush, path);
					}
					Color color = (flag ? UiTheme.Accent : (flag2 ? UiTheme.InputStrokeHover : UiTheme.Stroke));
					using (Pen pen = new Pen(color, flag ? Sc(2) : 1f))
					{
						graphics.DrawPath(pen, path);
					}
				}
				if (flag)
				{
					using (GraphicsPath path2 = RoundedPath(new Rectangle(rect.X + Sc(7), rect.Y + Sc(7), Cell - Sc(14), Cell - Sc(14)), Sc(4)))
					{
						using (Pen pen2 = new Pen(UiTheme.Surface, Math.Max(1.5f, Sc(2))))
						{
							graphics.DrawPath(pen2, path2);
						}
					}
				}
			}
		}
	}

	// ======================= 样式画廊 =======================

	private sealed class StyleGallery : Control, INeedHeight
	{

		public StyleGallery()
		{
			// 自绘控件必须自己开双缓冲：不然每次悬停 Invalidate 都会先擦背景再重画，
			// 肉眼看到的就是「鼠标移上去整块闪一下」。
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			DoubleBuffered = true;
			BackColor = UiTheme.Surface;
		}

		/// <summary>
		/// 双缓冲 + AllPaintingInWmPaint 之后背景由 OnPaint 自己画；
		/// 再把系统擦背景这一步挡掉，彻底消除「先擦成底色再画内容」的那一帧闪烁。
		/// </summary>
		protected override void OnPaintBackground(PaintEventArgs e)
		{
		}
		private List<string> _items = new List<string>();
		private int _sel;
		private int _hot = -1;
		private string _colorId;
		private Color? _colorOverride;
		private int _thickness = 3;
		private int _radius = 12;
		private bool _frameEnabled = true;
		public event EventHandler Picked;

		public List<string> ItemsForTest => _items;

		/// <summary>非空时优先用它画（跟随强调色 / 自定义颜色都走这条路）。</summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Color? ColorOverride
		{
			get
			{
				return _colorOverride;
			}
			set
			{
				_colorOverride = value;
				Invalidate();
			}
		}

		public int Gap => Sc(8);

		public void SetItems(List<string> items)
		{
			_items = items;
			if (_sel >= _items.Count)
			{
				_sel = 0;
			}
			Invalidate();
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string SelectedId
		{
			get
			{
				if (_sel >= 0 && _sel < _items.Count)
				{
					return _items[_sel];
				}
				return null;
			}
			set
			{
				for (int i = 0; i < _items.Count; i++)
				{
					if (_items[i] == value)
					{
						_sel = i;
						Invalidate();
						break;
					}
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string ColorId
		{
			get
			{
				return _colorId;
			}
			set
			{
				_colorId = value;
				Invalidate();
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Thickness
		{
			get
			{
				return _thickness;
			}
			set
			{
				_thickness = value;
				Invalidate();
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Radius
		{
			get
			{
				return _radius;
			}
			set
			{
				_radius = value;
				Invalidate();
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool FrameEnabled
		{
			get
			{
				return _frameEnabled;
			}
			set
			{
				_frameEnabled = value;
				Invalidate();
			}
		}

		public int ColumnsFor(int width)
		{
			int num = Sc(88) + Gap;
			int num2 = (width + Gap) / num;
			if (num2 < 2)
			{
				num2 = 2;
			}
			return num2;
		}

		public int CellH => Sc(64);

		public int HeightFor(int width)
		{
			int columns = ColumnsFor(width);
			int num = (_items.Count + columns - 1) / columns;
			return Math.Max(CellH, num * (CellH + Gap) - Gap);
		}

		public Rectangle SelectedCellRect
		{
			get
			{
				if (_sel < 0 || _sel >= _items.Count)
				{
					return Rectangle.Empty;
				}
				int columns = ColumnsFor(Width);
				int num = _sel % columns;
				int num2 = _sel / columns;
				int num3 = (Width + Gap) / columns - Gap;
				return new Rectangle(num * (num3 + Gap), num2 * (CellH + Gap), num3, CellH);
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = Hit(e.X, e.Y);
			if (num != _hot)
			{
				_hot = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = -1;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				int num = Hit(e.X, e.Y);
				if (num >= 0)
				{
					_sel = num;
					Invalidate();
					Picked?.Invoke(this, EventArgs.Empty);
				}
			}
			base.OnMouseUp(e);
		}

		private int Hit(int x, int y)
		{
			int columns = ColumnsFor(Width);
			int num = (Width + Gap) / columns - Gap;
			int num2 = x / (num + Gap);
			int num3 = y / (CellH + Gap);
			if (num2 < 0 || num3 < 0 || num2 >= columns)
			{
				return -1;
			}
			int num4 = num3 * columns + num2;
			if (num4 >= _items.Count)
			{
				return -1;
			}
			return num4;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear(UiTheme.Surface);
			int columns = ColumnsFor(Width);
			int num = (Width + Gap) / columns - Gap;
			Color color = Color.Gray;
			try
			{
				color = _colorOverride ?? FrameColors.Resolve(_colorId ?? FrameColors.DefaultId);
			}
			catch
			{
			}
			using Font font = new Font(DesignFontFamily, Sc(11), FontStyle.Regular, GraphicsUnit.Pixel);
			for (int i = 0; i < _items.Count; i++)
			{
				int num2 = i % columns;
				int num3 = i / columns;
				int num4 = num2 * (num + Gap);
				int num5 = num3 * (CellH + Gap);
				bool flag = i == _sel;
				bool flag2 = i == _hot;
				Rectangle rect = new Rectangle(num4, num5, num, CellH);
				using (GraphicsPath path = RoundedPath(new Rectangle(rect.X, rect.Y, rect.Width, CellH - Sc(18)), Sc(UiTheme.RadiusField)))
				{
					using (SolidBrush brush = new SolidBrush(UiTheme.SurfaceAlt))
					{
						graphics.FillPath(brush, path);
					}
					Color color2 = (flag ? UiTheme.Accent : (flag2 ? UiTheme.InputStrokeHover : UiTheme.Stroke));
					using (Pen pen = new Pen(color2, flag ? Sc(2) : 1f))
					{
						graphics.DrawPath(pen, path);
					}
				}
				RectangleF r = new RectangleF((float)rect.X + Sc(6), (float)rect.Y + Sc(5), (float)rect.Width - Sc(12), (float)CellH - Sc(38));
				if (_frameEnabled && r.Width > 4 && r.Height > 4)
				{
					try
					{
						FrameStyles.Draw(graphics, r, color, _items[i], Math.Max(1, _thickness), _radius);
					}
					catch (Exception ex)
					{
						Log.Error(ex);
					}
				}
				else if (!_frameEnabled && r.Width > 4 && r.Height > 4)
				{
					using Pen pen2 = new Pen(UiTheme.Divider);
					pen2.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
					graphics.DrawRectangle(pen2, r.X, r.Y, r.Width, r.Height);
				}
				TextRenderer.DrawText(graphics, FrameStyles.DisplayName(_items[i]), font, new Rectangle(rect.X, rect.Bottom - Sc(17), rect.Width, Sc(16)), flag ? UiTheme.AccentPressed : UiTheme.TextSecondary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			}
		}
	}

	// ======================= 实时预览 =======================

	private sealed class FramePreviewBox : Control
	{
		private string _colorId;
		private string _styleId;
		private Color? _colorOverride;
		private int _thickness = 3;
		private int _radius = 12;
		private bool _frameEnabled = true;
		private int _opacity = 100;

		/// <summary>非空时优先用它画（跟随强调色 / 自定义颜色都走这条路）。</summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Color? ColorOverride
		{
			get
			{
				return _colorOverride;
			}
			set
			{
				_colorOverride = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string ColorId
		{
			get
			{
				return _colorId;
			}
			set
			{
				_colorId = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string StyleId
		{
			get
			{
				return _styleId;
			}
			set
			{
				_styleId = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Thickness
		{
			get
			{
				return _thickness;
			}
			set
			{
				_thickness = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Radius
		{
			get
			{
				return _radius;
			}
			set
			{
				_radius = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool FrameEnabled
		{
			get
			{
				return _frameEnabled;
			}
			set
			{
				_frameEnabled = value;
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int OpacityPercent
		{
			get
			{
				return _opacity;
			}
			set
			{
				_opacity = value;
			}
		}

		public FramePreviewBox()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = UiTheme.Surface;
		}

		public void Refresh_()
		{
			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear(UiTheme.Surface);
			Rectangle clientRectangle = ClientRectangle;
			int num = Math.Max(2, Sc(6));
			RectangleF rectangleF = new RectangleF((float)num + 1f, (float)num + 1f, (float)clientRectangle.Width - (float)num * 2f - 2f, (float)clientRectangle.Height - (float)num * 2f - 2f);
			if (rectangleF.Width <= 4 || rectangleF.Height <= 4)
			{
				return;
			}
			using (GraphicsPath path = RoundedPath(Rectangle.Ceiling(rectangleF), Sc(UiTheme.RadiusField)))
			{
				using (SolidBrush brush = new SolidBrush(UiTheme.SurfaceAlt))
				{
					graphics.FillPath(brush, path);
				}
				using (Pen pen = new Pen(UiTheme.Divider))
				{
					graphics.DrawPath(pen, path);
				}
			}
			if (!_frameEnabled)
			{
				using Font font = new Font(DesignFontFamily, Sc(11), FontStyle.Regular, GraphicsUnit.Pixel);
				TextRenderer.DrawText(graphics, Loc.T("外框已关闭"), font, clientRectangle, UiTheme.TextTertiary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
				return;
			}
			Color color = Color.Gray;
			try
			{
				color = _colorOverride ?? FrameColors.Resolve(_colorId ?? FrameColors.DefaultId);
			}
			catch
			{
			}
			int alpha = _opacity * 255 / 100;
			if (alpha < 20)
			{
				alpha = 20;
			}
			if (alpha > 255)
			{
				alpha = 255;
			}
			try
			{
				FrameStyles.Draw(graphics, rectangleF, Color.FromArgb(alpha, color), _styleId ?? FrameStyles.DefaultId, Math.Max(1, _thickness), _radius);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
	}

	// ======================= 帮助面板 =======================

	internal sealed class HelpPanel : Control, INeedHeight
	{

		public HelpPanel()
		{
			// 自绘控件必须自己开双缓冲：不然每次悬停 Invalidate 都会先擦背景再重画，
			// 肉眼看到的就是「鼠标移上去整块闪一下」。
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			DoubleBuffered = true;
			BackColor = UiTheme.Surface;
		}

		/// <summary>
		/// 双缓冲 + AllPaintingInWmPaint 之后背景由 OnPaint 自己画；
		/// 再把系统擦背景这一步挡掉，彻底消除「先擦成底色再画内容」的那一帧闪烁。
		/// </summary>
		protected override void OnPaintBackground(PaintEventArgs e)
		{
		}
		internal sealed class Entry
		{
			public string Icon;
			public string Title;
			public string Body;
		}

		private List<Entry> _entries = new List<Entry>();
		private int _open;
		private int _hot = -1;

		public int HeaderH => Sc(36);

		/// <summary>
		/// 面板高度 = 所有标题行 + 最高的那条正文。
		///
		/// 原来是写死的 Sc(360)：条目一多（v1.14 加到 12 条）标题行就把高度占满，
		/// 正文区被挤成 0，最后几条标题直接超出控件边缘看不见。
		/// 现在按内容算，并且取"所有条目里最高的正文"，保证切换展开项时高度不变。
		/// </summary>
		public int HeightFor(int width)
		{
			return _entries.Count * HeaderH + BodyHeightFor(width);
		}

		/// <summary>当前展开项的正文需要多高（按实际换行量出来，不猜）。</summary>
		private int BodyHeightFor(int width)
		{
			if (_entries.Count == 0 || _open < 0 || _open >= _entries.Count)
			{
				return 0;
			}
			string body = _entries[_open].Body;
			if (string.IsNullOrEmpty(body))
			{
				return 0;
			}
			int num = Math.Max(Sc(200), width - Sc(28));
			Font font = _bodyFont ?? (_bodyFont = new Font(DesignFontFamily, Sc(11), FontStyle.Regular, GraphicsUnit.Pixel));
			int num2 = TextRenderer.MeasureText(body, font, new Size(num, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
			return Math.Max(Sc(30), num2 + Sc(12));
		}

		/// <summary>展开项变化时通知宿主重排（手风琴的高度是变的）。</summary>
		public Action OpenChanged;

		private Font _bodyFont;

		public void SetEntries(List<Entry> entries)
		{
			_entries = entries;
			Invalidate();
		}

		public int EntryCountForTest => _entries.Count;

		public int OpenIndexForTest => _open;

		public int BodyH => Math.Max(0, Height - _entries.Count * HeaderH);

		internal int BodyForTest(int width) => BodyHeightFor(width);

		public int HeaderTopForTest(int index)
		{
			return index * HeaderH + ((index > _open) ? BodyH : 0);
		}

		public Rectangle HeaderRect(int index)
		{
			return new Rectangle(0, HeaderTopForTest(index), Width, HeaderH);
		}

		public Rectangle BodyRect()
		{
			return new Rectangle(0, (_open + 1) * HeaderH, Width, BodyH);
		}

		public void OpenRangeForTest(out int top, out int bottom)
		{
			top = _open * HeaderH;
			bottom = top + HeaderH + BodyH;
		}

		private int HitHeader(int y)
		{
			for (int i = 0; i < _entries.Count; i++)
			{
				int num = HeaderTopForTest(i);
				if (y >= num && y < num + HeaderH)
				{
					return i;
				}
			}
			return -1;
		}

		public void ClickAtForTest(int y)
		{
			int num = HitHeader(y);
			if (num >= 0 && num != _open)
			{
				_open = num;
			}
			Invalidate();
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = HitHeader(e.Y);
			if (num != _hot)
			{
				_hot = num;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = -1;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				int num = HitHeader(e.Y);
				if (num >= 0 && num != _open)
				{
					_open = num;
					Invalidate();
					OpenChanged?.Invoke();
				}
			}
			base.OnMouseUp(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear(UiTheme.Surface);
			if (_entries.Count == 0)
			{
				return;
			}
			using Font font = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel);
			using Font font2 = new Font(DesignFontFamily, Sc(11), FontStyle.Regular, GraphicsUnit.Pixel);
			using Font font3 = Icons.At(Sc(14));
			using Font font4 = Icons.At(Sc(12));
			for (int i = 0; i < _entries.Count; i++)
			{
				int num = HeaderTopForTest(i);
				bool flag = i == _open;
				bool flag2 = i == _hot;
				Rectangle rectangle = new Rectangle(0, num, Width, HeaderH);
				if (flag)
				{
					using (SolidBrush brush = new SolidBrush(UiTheme.AccentSoft))
					{
						graphics.FillRectangle(brush, rectangle);
					}
				}
				else if (flag2)
				{
					using (SolidBrush brush2 = new SolidBrush(UiTheme.ButtonHover))
					{
						graphics.FillRectangle(brush2, rectangle);
					}
				}
				int num2 = Sc(14);
				if (font3 != null)
				{
					TextRenderer.DrawText(graphics, _entries[i].Icon, font3, new Rectangle(num2, rectangle.Top + (HeaderH - Sc(16)) / 2, Sc(16), Sc(16)), flag ? UiTheme.Accent : UiTheme.TextSecondary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
					num2 += Sc(22);
				}
				TextRenderer.DrawText(graphics, _entries[i].Title, font, new Rectangle(num2, rectangle.Top, Math.Max(10, Width - num2 - Sc(30)), HeaderH), flag ? UiTheme.AccentPressed : UiTheme.TextPrimary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				if (font4 != null)
				{
					TextRenderer.DrawText(graphics, Glyph.Chevron, font4, new Rectangle(Width - Sc(26), rectangle.Top + (HeaderH - Sc(14)) / 2, Sc(14), Sc(14)), flag ? UiTheme.Accent : UiTheme.TextTertiary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
				}
				using (Pen pen = new Pen(UiTheme.Divider))
				{
					graphics.DrawLine(pen, 0, rectangle.Bottom - 1, Width, rectangle.Bottom - 1);
				}
			}
			Rectangle bodyRect = BodyRect();
			if (bodyRect.Height > 4)
			{
				using (SolidBrush brush3 = new SolidBrush(UiTheme.Surface))
				{
					graphics.FillRectangle(brush3, bodyRect);
				}
				TextRenderer.DrawText(graphics, _entries[_open].Body, font2, new Rectangle(bodyRect.Left + Sc(16), bodyRect.Top + Sc(4), Math.Max(10, bodyRect.Width - Sc(28)), Math.Max(10, bodyRect.Height - Sc(8))), UiTheme.TextSecondary, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Top);
			}
		}
	}

	// ======================= 分页滚动容器 =======================

	// 双缓冲贴图纸：滚动时整页是"把画好的位图往上挪"，不逐控件重画
	private sealed class BufferedPanel : Panel
	{
		public BufferedPanel()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, value: true);
			DoubleBuffered = true;
		}
	}

	private sealed class PageHost : Panel
	{
		private int _offset;
		public Panel Canvas;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Offset
		{
			get
			{
				return _offset;
			}
			set
			{
				int num = Math.Max(0, Math.Min(MaxScroll, value));
				if (num != _offset)
				{
					_offset = num;
					if (Canvas != null)
					{
						Canvas.Top = -_offset;
					}
					Invalidate();
				}
			}
		}

		public int MaxScroll
		{
			get
			{
				if (Canvas == null)
				{
					return 0;
				}
				return Math.Max(0, Canvas.Height - ClientSize.Height);
			}
		}

		public int StepPx => Sc(96);

		public PageHost()
		{
			SetStyle(ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = UiTheme.Canvas;
			AutoScroll = false;
			Canvas = new BufferedPanel
			{
				BackColor = UiTheme.Canvas,
				Location = new Point(0, 0)
			};
			Canvas.Paint += delegate(object s, PaintEventArgs e)
			{
				e.Graphics.Clear(UiTheme.Canvas);
			};
			Controls.Add(Canvas);
		}

		public void UpdateScroll()
		{
			int num = MaxScroll;
			if (_offset > num)
			{
				_offset = num;
			}
			if (Canvas != null && Canvas.Top != -_offset)
			{
				// 值没变还去设一次，会把整页位图重新贴一遍
				Canvas.Top = -_offset;
			}
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if (MaxScroll > 0)
			{
				Offset -= e.Delta / 120 * StepPx;
			}
			base.OnMouseWheel(e);
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == 522)
			{
				int delta = (short)unchecked((int)(m.WParam.ToInt64() >> 16));
				if (MaxScroll > 0)
				{
					Offset -= delta / 120 * StepPx;
					return;
				}
			}
			base.WndProc(ref m);
		}
	}

	private sealed class Page
	{
		public string Title;
		public string Subtitle;
		public PageHost Host;
		public List<Card> Cards = new List<Card>();
		// 上次排版用的宽度和文字签名：宽度/文字都没变就不用重排这一页
		public int LaidOutWidth = -1;
		public string Signature = "";
	}
}
