// HoldDrag · UI/SettingsForm.Inputs.cs
//
// 自绘输入控件：下拉框（含弹出列表）与数字框。
//
// 为什么不用系统控件：ComboBox 的下拉按钮和 NumericUpDown 的微调按钮都是**系统主题绘制的子窗口**，
// 它们不跟随深色主题（深色页上一块刺眼的白方块），圆角、行高、字体也完全不听话。
// v1.11 起这两个控件改成纯自绘：圆角、描边、悬停、弹出层全部走同一套设计令牌，
// 和按钮/输入框在视觉上才是一个系统。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HoldDrag;

internal sealed partial class SettingsForm
{
	// ======================= 下拉框 =======================

	internal sealed class FlatCombo : Control
	{
		/// <summary>选项集合：增删后自动让控件失效重绘（调用方只管 Add/Clear）。</summary>
		public sealed class ItemCollection : IList<object>
		{
			private readonly List<object> _items = new List<object>();
			private readonly FlatCombo _owner;

			public ItemCollection(FlatCombo owner)
			{
				_owner = owner;
			}

			public int Count => _items.Count;

			public bool IsReadOnly => false;

			public object this[int index]
			{
				get
				{
					return _items[index];
				}
				set
				{
					_items[index] = value;
					_owner.Invalidate();
				}
			}

			public void Add(object item)
			{
				_items.Add(item);
				_owner.Invalidate();
			}

			public void Clear()
			{
				_items.Clear();
				_owner.SelectedIndex = -1;
				_owner.Invalidate();
			}

			public bool Contains(object item) => _items.Contains(item);

			public void CopyTo(object[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

			public IEnumerator<object> GetEnumerator() => _items.GetEnumerator();

			public int IndexOf(object item) => _items.IndexOf(item);

			public void Insert(int index, object item)
			{
				_items.Insert(index, item);
				_owner.Invalidate();
			}

			public bool Remove(object item)
			{
				bool flag = _items.Remove(item);
				if (flag)
				{
					_owner.Invalidate();
				}
				return flag;
			}

			public void RemoveAt(int index)
			{
				_items.RemoveAt(index);
				_owner.Invalidate();
			}

			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();

			internal object Get(int index)
			{
				if (index < 0 || index >= _items.Count)
				{
					return null;
				}
				return _items[index];
			}
		}

		private int _selectedIndex = -1;
		private bool _hover;
		private bool _pressed;
		private FlatComboPopup _popup;
		private readonly Font _font;

		/// <summary>颜色项这类需要自绘条目时挂这里（闭合态与列表项共用）。</summary>
		public Action<Graphics, Rectangle, object> ClosedPainter;

		internal int WheelSwallowedForTest;

		internal int WheelForwardedForTest;

		public FlatCombo()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, value: true);
			BackColor = Color.Transparent;
			Font = new Font(DesignFontFamily, Sc((int)Tokens.FontRow), FontStyle.Regular, GraphicsUnit.Pixel);
			_font = Font;
			Cursor = Cursors.Hand;
			TabStop = true;
			// 集合必须在构造时就建好：Populate() 在窗体句柄创建之前就会往里填选项，
			// 放到 OnCreateControl 里会直接空引用。
			Items = new ItemCollection(this);
		}

		public ItemCollection Items { get; private set; }

		/// <summary>兼容旧 ComboBox 的写法：可读可写（写 true 打开、false 关闭）。</summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool DroppedDown
		{
			get
			{
				return _popup != null;
			}
			set
			{
				if (value)
				{
					ShowPopup();
				}
				else
				{
					ClosePopup();
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int SelectedIndex
		{
			get
			{
				return _selectedIndex;
			}
			set
			{
				int num = ((value >= 0 && Items != null && value < Items.Count) ? value : (-1));
				if (num == _selectedIndex)
				{
					return;
				}
				_selectedIndex = num;
				Invalidate();
				SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public object SelectedItem
		{
			get
			{
				return Items?.Get(_selectedIndex);
			}
			set
			{
				SelectedIndex = ((value == null) ? (-1) : Items.IndexOf(value));
			}
		}

		public event EventHandler SelectedIndexChanged;

		/// <summary>兼容旧调用点：批量增删时本来用来抑制重绘，自绘控件里重绘很便宜，直接忽略。</summary>
		public void BeginUpdate()
		{
		}

		public void EndUpdate()
		{
			Invalidate();
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
			_pressed = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && Enabled)
			{
				_pressed = true;
				Invalidate();
				TogglePopup();
			}
			base.OnMouseDown(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (Enabled)
			{
				if (e.KeyCode == Keys.Down || e.KeyCode == Keys.F4)
				{
					if (!DroppedDown)
					{
						ShowPopup();
					}
					else
					{
						_popup.MoveSelection(1);
					}
					e.Handled = true;
				}
				else if (e.KeyCode == Keys.Up)
				{
					if (!DroppedDown)
					{
						Step(-1);
					}
					else
					{
						_popup.MoveSelection(-1);
					}
					e.Handled = true;
				}
				else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
				{
					if (!DroppedDown)
					{
						ShowPopup();
					}
					e.Handled = true;
				}
			}
			base.OnKeyDown(e);
		}

		protected override void OnGotFocus(EventArgs e)
		{
			Invalidate();
			base.OnGotFocus(e);
		}

		protected override void OnLostFocus(EventArgs e)
		{
			Invalidate();
			base.OnLostFocus(e);
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == 522)
			{
				// 滚轮不改变选择：设置页整体在滚动容器里，滚到下拉框上不该顺手改配置。
				if (!DroppedDown)
				{
					WheelSwallowedForTest++;
					return;
				}
				WheelForwardedForTest++;
			}
			base.WndProc(ref m);
		}

		private void Step(int delta)
		{
			if (Items == null || Items.Count == 0)
			{
				return;
			}
			int num = _selectedIndex + delta;
			if (num < 0)
			{
				num = 0;
			}
			if (num >= Items.Count)
			{
				num = Items.Count - 1;
			}
			SelectedIndex = num;
		}

		internal void TogglePopup()
		{
			if (DroppedDown)
			{
				ClosePopup();
			}
			else
			{
				ShowPopup();
			}
		}

		internal void ShowPopup()
		{
			if (Items == null || Items.Count == 0 || !Enabled)
			{
				return;
			}
			ClosePopup();
			_popup = new FlatComboPopup(this);
			_popup.FormClosed += delegate
			{
				_popup = null;
				_pressed = false;
				Invalidate();
			};
			_popup.ShowPopup();
		}

		internal void ClosePopup()
		{
			FlatComboPopup popup = _popup;
			_popup = null;
			if (popup != null && !popup.IsDisposed)
			{
				popup.Close();
			}
		}

		internal void CommitSelection(int index)
		{
			SelectedIndex = index;
		}

		internal Font ItemFont => _font;

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear((Parent != null) ? Parent.BackColor : Tokens.Surface);
			Rectangle rect = new Rectangle(0, 0, Math.Max(2, Width - 1), Math.Max(2, Height - 1));
			using GraphicsPath path = RoundedPath(rect, Sc(Tokens.RadiusField));
			using (SolidBrush brush = new SolidBrush(Enabled ? Tokens.FieldFill : Tokens.DisabledFill))
			{
				graphics.FillPath(brush, path);
			}
			Color color;
			if (!Enabled)
			{
				color = Tokens.DisabledStroke;
			}
			else if (Focused || _pressed)
			{
				color = Tokens.Accent;
			}
			else
			{
				color = (_hover ? Tokens.InputStrokeHover : Tokens.FieldStroke);
			}
			using (Pen pen = new Pen(color, (Focused || _pressed) ? Math.Max(1.6f, Sc(1)) : 1f))
			{
				graphics.DrawPath(pen, path);
			}
			Rectangle content = new Rectangle(Sc(12), 0, Math.Max(10, Width - Sc(40)), Height);
			object obj = Items?.Get(_selectedIndex);
			if (ClosedPainter != null)
			{
				ClosedPainter(graphics, content, obj);
			}
			else
			{
				TextRenderer.DrawText(graphics, (obj != null) ? obj.ToString() : string.Empty, _font, content, Enabled ? Tokens.TextPrimary : Tokens.TextTertiary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
			DrawChevron(graphics, new Rectangle(Width - Sc(30), 0, Sc(18), Height), Enabled ? Tokens.TextSecondary : Tokens.TextTertiary);
		}

		internal static void DrawChevron(Graphics graphics, Rectangle area, Color color)
		{
			float num = (float)area.Left + (float)area.Width / 2f;
			float num2 = (float)area.Top + (float)area.Height / 2f;
			float num3 = Math.Max(2.4f, Sc(2));
			using Pen pen = new Pen(color, Math.Max(1.3f, Sc(1) * 1.35f));
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			pen.LineJoin = LineJoin.Round;
			graphics.DrawLines(pen, new PointF[3]
			{
				new PointF(num - num3 * 1.55f, num2 - num3 * 0.42f),
				new PointF(num, num2 + num3 * 0.88f),
				new PointF(num + num3 * 1.55f, num2 - num3 * 0.42f)
			});
		}
	}

	/// <summary>下拉框的弹出层：无边框 + 投影，条目自己画（跟随选中/悬停）。</summary>
	private sealed class FlatComboPopup : Form
	{
		private readonly FlatCombo _owner;
		private readonly PopupList _list;
		private readonly int _rowH;

		public FlatComboPopup(FlatCombo owner)
		{
			_owner = owner;
			_rowH = Sc(34);
			FormBorderStyle = FormBorderStyle.None;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.Manual;
			BackColor = Tokens.Surface;
			DoubleBuffered = true;
			AutoScaleMode = AutoScaleMode.None;
			int num = Math.Min(owner.Items.Count, 9);
			_list = new PopupList(this, _rowH);
			_list.Width = Math.Max(Sc(80), owner.Width - Sc(2));
			_list.Height = num * _rowH + Sc(10);
			Padding = new Padding(Sc(1));
			ClientSize = new Size(_list.Width + Sc(2), _list.Height + Sc(2));
			Controls.Add(_list);
			_list.SetItems(owner.Items, owner.SelectedIndex, owner.ClosedPainter, owner.ItemFont);
		}

		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams createParams = base.CreateParams;
				// 无边框窗也要投影，否则浮在页面上像一张贴纸
				createParams.ClassStyle |= 0x20000;
				return createParams;
			}
		}

		public void ShowPopup()
		{
			Point point = _owner.PointToScreen(new Point(0, _owner.Height + Sc(6)));
			Rectangle workingArea = Screen.FromControl(_owner).WorkingArea;
			if (point.Y + Height > workingArea.Bottom)
			{
				point = _owner.PointToScreen(new Point(0, -Height - Sc(6)));
			}
			if (point.Y < workingArea.Top)
			{
				point.Y = workingArea.Top;
			}
			Location = point;
			Show();
			_list.Focus();
		}

		protected override void OnDeactivate(EventArgs e)
		{
			base.OnDeactivate(e);
			Close();
		}

		internal void MoveSelection(int delta)
		{
			_list.MoveSelection(delta);
		}

		internal void Accept()
		{
			_owner.CommitSelection(_list.SelectedIndex);
			Close();
		}

		internal void Cancel()
		{
			Close();
		}

		private sealed class PopupList : Control
		{
			private readonly FlatComboPopup _popup;
			private readonly int _rowH;
			private List<object> _items = new List<object>();
			private int _index = -1;
			private int _hot = -1;
			private Action<Graphics, Rectangle, object> _painter;
			private Font _font;

			public PopupList(FlatComboPopup popup, int rowH)
			{
				_popup = popup;
				_rowH = rowH;
				SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
				BackColor = Tokens.Surface;
				TabStop = true;
			}

			public int SelectedIndex => _index;

			public void SetItems(IList<object> items, int index, Action<Graphics, Rectangle, object> painter, Font font)
			{
				_items = new List<object>();
				for (int i = 0; i < items.Count; i++)
				{
					_items.Add(items[i]);
				}
				_index = index;
				_painter = painter;
				_font = font;
				Invalidate();
			}

			public void MoveSelection(int delta)
			{
				if (_items.Count == 0)
				{
					return;
				}
				int num = _index + delta;
				if (num < 0)
				{
					num = 0;
				}
				if (num >= _items.Count)
				{
					num = _items.Count - 1;
				}
				_index = num;
				Invalidate();
			}

			protected override void OnMouseMove(MouseEventArgs e)
			{
				int num = e.Y / _rowH;
				if (num < 0 || num >= _items.Count)
				{
					num = -1;
				}
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
				int num = e.Y / _rowH;
				if (num >= 0 && num < _items.Count)
				{
					_index = num;
					_popup.Accept();
				}
				base.OnMouseUp(e);
			}

			protected override bool IsInputKey(Keys keyData)
			{
				if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Enter || keyData == Keys.Escape)
				{
					return true;
				}
				return base.IsInputKey(keyData);
			}

			protected override void OnKeyDown(KeyEventArgs e)
			{
				switch (e.KeyCode)
				{
				case Keys.Up:
					MoveSelection(-1);
					e.Handled = true;
					break;
				case Keys.Down:
					MoveSelection(1);
					e.Handled = true;
					break;
				case Keys.Enter:
					_popup.Accept();
					e.Handled = true;
					break;
				case Keys.Escape:
					_popup.Cancel();
					e.Handled = true;
					break;
				}
				base.OnKeyDown(e);
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				Graphics graphics = e.Graphics;
				HQ.Apply(graphics);
				graphics.Clear(Tokens.Surface);
				for (int i = 0; i < _items.Count; i++)
				{
					Rectangle rect = new Rectangle(Sc(4), Sc(5) + i * _rowH, Math.Max(10, Width - Sc(8)), _rowH);
					bool flag = i == _index;
					bool flag2 = i == _hot;
					if (flag || flag2)
					{
						using GraphicsPath path = RoundedPath(rect, Sc(Tokens.RadiusChip));
						using SolidBrush brush = new SolidBrush(flag ? Tokens.AccentSoft : Tokens.Hover);
						graphics.FillPath(brush, path);
					}
					Rectangle bounds = new Rectangle(rect.Left + Sc(10), rect.Top, Math.Max(10, rect.Width - Sc(20)), rect.Height);
					if (_painter != null)
					{
						_painter(graphics, bounds, _items[i]);
					}
					else
					{
						TextRenderer.DrawText(graphics, (_items[i] != null) ? _items[i].ToString() : string.Empty, _font ?? Font, bounds, flag ? Tokens.Accent : Tokens.TextPrimary, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
					}
				}
			}
		}
	}

	// ======================= 数字框 =======================

	/// <summary>
	/// 自绘数字框：左边数值、右边一组上下微调箭头（悬停高亮）。
	/// 系统 NumericUpDown 的微调按钮是主题子窗口 —— 深色页上是两块白底，且高度被锁死，
	/// 和旁边的输入框永远对不齐，所以整个换掉。
	/// </summary>
	internal sealed class FlatNum : Control
	{
		private decimal _value;
		private decimal _minimum;
		private decimal _maximum = 100m;
		private decimal _increment = 1m;
		private int _hot = -1;
		private readonly Font _font;

		public FlatNum(decimal minimum, decimal maximum, decimal increment)
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, value: true);
			_minimum = minimum;
			_maximum = (maximum < minimum) ? minimum : maximum;
			_increment = (increment <= 0m) ? 1m : increment;
			_value = Math.Min(Math.Max(minimum, 0m), _maximum);
			Font = new Font(DesignFontFamily, Sc((int)Tokens.FontRow), FontStyle.Regular, GraphicsUnit.Pixel);
			_font = Font;
			BackColor = Tokens.FieldFill;
			Cursor = Cursors.Hand;
			TabStop = true;
		}

		public decimal Minimum => _minimum;

		public decimal Maximum => _maximum;

		public decimal Increment => _increment;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public decimal Value
		{
			get
			{
				return _value;
			}
			set
			{
				decimal num = Math.Min(Math.Max(value, _minimum), _maximum);
				if (num == _value)
				{
					return;
				}
				_value = num;
				Invalidate();
				ValueChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public event EventHandler ValueChanged;

		internal void Nudge(int delta)
		{
			Value = _value + _increment * delta;
		}

		internal void SetValueSilent(decimal value)
		{
			_value = Math.Min(Math.Max(value, _minimum), _maximum);
			Invalidate();
		}

		private Rectangle UpRect => new Rectangle(Width - Sc(28), Sc(4), Sc(24), Math.Max(6, Height / 2 - Sc(5)));

		private Rectangle DownRect => new Rectangle(Width - Sc(28), Height / 2 + Sc(1), Sc(24), Math.Max(6, Height / 2 - Sc(5)));

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = (UpRect.Contains(e.Location) ? 0 : (DownRect.Contains(e.Location) ? 1 : (-1)));
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

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && Enabled)
			{
				if (UpRect.Contains(e.Location))
				{
					Nudge(1);
				}
				else if (DownRect.Contains(e.Location))
				{
					Nudge(-1);
				}
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			if (Focused && Enabled)
			{
				Nudge((e.Delta > 0) ? 1 : (-1));
			}
			base.OnMouseWheel(e);
		}

		protected override bool IsInputKey(Keys keyData)
		{
			if (keyData == Keys.Up || keyData == Keys.Down)
			{
				return true;
			}
			return base.IsInputKey(keyData);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (Enabled)
			{
				if (e.KeyCode == Keys.Up)
				{
					Nudge(1);
					e.Handled = true;
				}
				else if (e.KeyCode == Keys.Down)
				{
					Nudge(-1);
					e.Handled = true;
				}
			}
			base.OnKeyDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			graphics.Clear(Tokens.Surface);
			Rectangle rect = new Rectangle(0, 0, Math.Max(2, Width - 1), Math.Max(2, Height - 1));
			using GraphicsPath path = RoundedPath(rect, Sc(Tokens.RadiusField));
			using (SolidBrush brush = new SolidBrush(Enabled ? Tokens.FieldFill : Tokens.DisabledFill))
			{
				graphics.FillPath(brush, path);
			}
			Color color = (!Enabled) ? Tokens.DisabledStroke : (Focused ? Tokens.Accent : Tokens.FieldStroke);
			using (Pen pen = new Pen(color, Focused ? Math.Max(1.6f, Sc(1)) : 1f))
			{
				graphics.DrawPath(pen, path);
			}
			Rectangle bounds = new Rectangle(Sc(12), 0, Math.Max(10, Width - Sc(40)), Height);
			TextRenderer.DrawText(graphics, _value.ToString("0"), _font, bounds, Enabled ? Tokens.TextPrimary : Tokens.TextTertiary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
			if (Enabled)
			{
				DrawStep(graphics, UpRect, up: true, _hot == 0);
				DrawStep(graphics, DownRect, up: false, _hot == 1);
			}
		}

		private void DrawStep(Graphics graphics, Rectangle area, bool up, bool hot)
		{
			if (hot)
			{
				using GraphicsPath path = RoundedPath(area, Sc(4));
				using SolidBrush brush = new SolidBrush(Tokens.Hover);
				graphics.FillPath(brush, path);
			}
			float num = (float)area.Left + (float)area.Width / 2f;
			float num2 = (float)area.Top + (float)area.Height / 2f;
			float num3 = Math.Max(2.2f, (float)Sc(2));
			using Pen pen = new Pen(Enabled ? Tokens.TextSecondary : Tokens.TextTertiary, Math.Max(1.3f, Sc(1) * 1.3f));
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			pen.LineJoin = LineJoin.Round;
			if (up)
			{
				graphics.DrawLines(pen, new PointF[3]
				{
					new PointF(num - num3, num2 + num3 * 0.55f),
					new PointF(num, num2 - num3 * 0.62f),
					new PointF(num + num3, num2 + num3 * 0.55f)
				});
			}
			else
			{
				graphics.DrawLines(pen, new PointF[3]
				{
					new PointF(num - num3, num2 - num3 * 0.55f),
					new PointF(num, num2 + num3 * 0.62f),
					new PointF(num + num3, num2 - num3 * 0.55f)
				});
			}
		}
	}
}
