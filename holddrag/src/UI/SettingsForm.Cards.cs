// HoldDrag · UI/SettingsForm.Cards.cs

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
	// ======================= 卡片 =======================
	//
	// 布局照「按键增强」的设置页做：
	// 一张白卡（圆角 14 + 1px 描边）里是一串"设置行"，
	// 每行左边是标题 + 一行灰色说明，右边是控件（开关/下拉框/数字/按钮，右对齐）；
	// 行与行之间一条极淡的分隔线。这种结构比"标签和控件挤在一行"更好读，
	// 也天然对齐 —— 所有控件都贴在右侧同一列上。

	private interface INeedHeight
	{
		int HeightFor(int width);
	}

	private enum RowKind
	{
		Row,
		Block,
		Note
	}

	private sealed class RowSpec
	{
		public RowKind Kind;
		public Control Ctl;
		public Control Tail;
		public Label TitleCtl;
		public Label NoteCtl;
		public string Label;
		public string Note;
		public int CtlW;
		public int TailW;
		public int BlockH;
	}

	private sealed class Card : Panel
	{
		public readonly string Title;
		public readonly string Subtitle;

		private readonly List<RowSpec> _rows = new List<RowSpec>();
		private readonly List<Control> _fields = new List<Control>();
		private readonly List<int> _dividers = new List<int>();
		private Rectangle _titleRect;
		private Rectangle _subRect;
		// 字体在构造时建一次：原来每次 OnPaint 都 new Font，滚动时一秒钟上千次分配 + GDI 句柄
		private readonly Font _fontTitle;
		private readonly Font _fontSub;

		/// <summary>卡片内边距（参照里 Padding=18，这里给 20 更透气）。</summary>
		public const int PadX = 22;
		private const int PadTop = 20;
		private const int PadBottom = 20;

		public Card(string title, string subtitle)
		{
			Title = title;
			Subtitle = subtitle;
			Text = title;
			BackColor = Tokens.Canvas;
			ForeColor = Tokens.TextPrimary;
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel);
			_fontTitle = new Font(DesignFontFamily, Sc(17), FontStyle.Bold, GraphicsUnit.Pixel);
			_fontSub = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel);
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_fontTitle.Dispose();
				_fontSub.Dispose();
			}
			base.Dispose(disposing);
		}

		// ---------- 行定义 ----------

		/// <summary>标准设置行：左标题 + 说明，右控件（固定宽）。ctlW &lt;= 0 表示按控件自然宽。</summary>
		public void Setting(Control ctl, string label, string note = null, int ctlW = 180)
		{
			Add(new RowSpec
			{
				Kind = RowKind.Row,
				Ctl = ctl,
				Label = label,
				Note = note,
				CtlW = ctlW
			});
		}

		/// <summary>同一行两个控件（触发键 + 重新录制、数值 + 单位）：主控件 + 尾巴控件依次右对齐。</summary>
		public void Setting(Control ctl, int ctlW, Control tail, int tailW, string label, string note = null)
		{
			Add(new RowSpec
			{
				Kind = RowKind.Row,
				Ctl = ctl,
				CtlW = ctlW,
				Tail = tail,
				TailW = tailW,
				Label = label,
				Note = note
			});
		}

		/// <summary>开关行：左标题 + 说明，右边只放一个开关（宽度按开关自身算）。</summary>
		public void Toggle(Control ctl, string label, string note = null)
		{
			Add(new RowSpec
			{
				Kind = RowKind.Row,
				Ctl = ctl,
				Label = label,
				Note = note,
				CtlW = -1
			});
		}

		/// <summary>整行说明文字（不是设置项，只是解释）。</summary>
		public void Note(string text)
		{
			Label label = NewLabel(text, 12, Tokens.TextTertiary, bold: false);
			Add(new RowSpec
			{
				Kind = RowKind.Note,
				Ctl = label
			});
		}

		/// <summary>整宽块：色板 / 样式画廊 / 预览框 / 帮助列表，可以带一个小节标题。</summary>
		public void Block(Control ctl, string label = null, int blockH = 0)
		{
			Label label2 = null;
			if (!string.IsNullOrEmpty(label))
			{
				label2 = NewLabel(label, 12, Tokens.TextSecondary, bold: false);
			}
			Add(new RowSpec
			{
				Kind = RowKind.Block,
				Ctl = ctl,
				TitleCtl = label2,
				Label = label,
				BlockH = blockH
			});
		}

		private void Add(RowSpec spec)
		{
			_rows.Add(spec);
			if (spec.Ctl != null && !Controls.Contains(spec.Ctl))
			{
				Controls.Add(spec.Ctl);
			}
			if (spec.Tail != null && !Controls.Contains(spec.Tail))
			{
				Controls.Add(spec.Tail);
			}
			if (spec.TitleCtl != null && !Controls.Contains(spec.TitleCtl))
			{
				Controls.Add(spec.TitleCtl);
			}
			if (spec.Kind == RowKind.Row)
			{
				if (spec.TitleCtl == null && !string.IsNullOrEmpty(spec.Label))
				{
					spec.TitleCtl = NewLabel(spec.Label, Tokens.FontRow, Tokens.TextPrimary, bold: false);
				}
				if (spec.NoteCtl == null && !string.IsNullOrEmpty(spec.Note))
				{
					spec.NoteCtl = NewLabel(spec.Note, Tokens.FontNote, Tokens.TextTertiary, bold: false);
				}
			}
		}

		private Label NewLabel(string text, float px, Color color, bool bold)
		{
			Label label = new Label
			{
				Text = text ?? "",
				AutoSize = false,
				AutoEllipsis = true,
				UseMnemonic = false,
				// 必须是实色而不是 Transparent：WinForms 里 Transparent 的 Label 每次重绘
				// 都要先让父控件把自己那块重画一遍，一页几十个标签就是几十次卡片重绘 ——
				// 这是"滚动/切页发闷"的主要来源。卡片底就是纯色，用同一个颜色零成本。
				BackColor = Tokens.Surface,
				ForeColor = color,
				// 设计稿给的是像素：按 DPI 换成**整数像素**字号。
				// 用磅值会在 125%/150% 下变成 15.83px 这种小数，GDI 取舍不匀，中文笔画就会出现锯齿。
				Font = new Font(DesignFontFamily, Sc((int)Math.Round(px)), bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel),
				TextAlign = ContentAlignment.MiddleLeft
			};
			Controls.Add(label);
			return label;
		}

		public void RegisterField(Control c)
		{
			if (c == null || _fields.Contains(c))
			{
				return;
			}
			_fields.Add(c);
			try
			{
				c.BackColor = Tokens.FieldFill;
			}
			catch
			{
			}
		}

		// ---------- 度量与排版 ----------

		public int Measure(int width)
		{
			return Arrange(width, apply: false);
		}

		public void Layout(int width)
		{
			SuspendLayout();
			Arrange(width, apply: true);
			ResumeLayout(performLayout: false);
		}

		private int Arrange(int width, bool apply)
		{
			int padX = Sc(PadX);
			int inner = Math.Max(Sc(120), width - padX * 2);
			int y = Sc(PadTop);
			bool header = false;

			if (!string.IsNullOrEmpty(Title))
			{
				_titleRect = new Rectangle(padX, y, inner, Sc(22));
				y += Sc(22);
				header = true;
			}
			else
			{
				_titleRect = Rectangle.Empty;
			}
			if (!string.IsNullOrEmpty(Subtitle))
			{
				_subRect = new Rectangle(padX, y + Sc(1), inner, Sc(17));
				y += Sc(18);
				header = true;
			}
			else
			{
				_subRect = Rectangle.Empty;
			}
			if (header)
			{
				y += Sc(10);
			}
			if (apply)
			{
				_dividers.Clear();
			}

			bool first = true;
			foreach (RowSpec row in _rows)
			{
				if (row.Kind == RowKind.Note)
				{
					int lines = MeasureLines(row.Ctl.Text, row.Ctl, inner);
					int noteH = Sc(21) * Math.Max(1, lines);
					if (apply)
					{
						Place(row.Ctl, new Rectangle(padX, y, inner, noteH));
					}
					y += noteH + Sc(8);
					continue;
				}

				if (row.Kind == RowKind.Block)
				{
					if (!string.IsNullOrEmpty(row.Label))
					{
						if (apply && row.TitleCtl != null)
						{
							Place(row.TitleCtl, new Rectangle(padX, y, inner, Sc(18)));
						}
						y += Sc(22);
					}
					int blockH = ResolveBlockHeight(row, inner);
					if (apply)
					{
						Place(row.Ctl, new Rectangle(padX, y, inner, blockH));
					}
					y += blockH + Sc(6);
					continue;
				}

				// ---- 标准设置行 ----
				int ctlW = ((row.CtlW > 0) ? Sc(row.CtlW) : ((row.CtlW < 0) ? NaturalWidth(row.Ctl) : Sc(180)));
				int tailW = ((row.Tail == null) ? 0 : ((row.TailW > 0) ? Sc(row.TailW) : NaturalWidth(row.Tail)));
				int gap = ((row.Tail == null) ? 0 : Sc(8));
				// 窄窗口兜底：先保证左侧文字还有 90，控件列按剩余空间收缩（尾巴先收、再收主控件），
				// 宁可控件变小，也绝不允许控件叠到文字上。
				int avail = Math.Max(Sc(60), inner - Sc(16));
				int room = Math.Max(Sc(40), avail - Sc(90));
				if (ctlW + gap + tailW > room)
				{
					if (tailW > 0)
					{
						tailW = Math.Min(tailW, Math.Max(Sc(28), room / 2));
					}
					ctlW = Math.Max(Sc(36), room - gap - tailW);
					if (ctlW + gap + tailW > room && tailW > 0)
					{
						tailW = Math.Max(0, room - gap - ctlW);
						gap = ((tailW > 0) ? gap : 0);
					}
				}
				int ctlTotal = ctlW + gap + tailW;
				int labelW = Math.Max(Sc(40), avail - ctlTotal);

				int titleH = Sc(24);
				// 说明文字也要按「折几行」给高度：一行放不下时它真的会折成两行，
				// 只给一行的高度就会把第二行裁掉（这就是「文字显示不全」的根因）。
				int noteH2 = ((row.NoteCtl == null) ? 0 : (Math.Max(1, MeasureLines(row.NoteCtl.Text, row.NoteCtl, labelW)) * Sc(20) + Sc(2)));
				int coreH = titleH + noteH2;
				int ctlH = Math.Max(Sc(24), ControlHeight(row.Ctl, ctlW));
				int contentH = Math.Max(Math.Max(coreH, ctlH), Sc(24));
				int rowH = contentH + Sc(18);

				if (!first && apply)
				{
					_dividers.Add(y);
				}
				first = false;

				int top = y + Sc(10);
				int textY = top + Math.Max(0, (contentH - coreH) / 2);
				if (apply)
				{
					if (row.TitleCtl != null)
					{
						Place(row.TitleCtl, new Rectangle(padX, textY, labelW, titleH));
					}
					if (row.NoteCtl != null)
					{
						Place(row.NoteCtl, new Rectangle(padX, textY + titleH, labelW, noteH2));
					}
					int ctlY = top + Math.Max(0, (contentH - ctlH) / 2);
					Place(row.Ctl, new Rectangle(padX + inner - ctlTotal, ctlY, ctlW, ctlH));
					// ComboBox 之类的高度由系统锁定，SetBounds 改不动 —— 按真实高度重算一次垂直居中
					if (row.Ctl.Height != ctlH)
					{
						row.Ctl.Top = ctlY + Math.Max(0, (ctlH - row.Ctl.Height) / 2);
					}
					if (row.Tail != null)
					{
						int tailH = Math.Max(Sc(24), ControlHeight(row.Tail, tailW));
						int tailY = top + Math.Max(0, (contentH - tailH) / 2);
						Place(row.Tail, new Rectangle(padX + inner - tailW, tailY, tailW, tailH));
						if (row.Tail.Height != tailH)
						{
							row.Tail.Top = tailY + Math.Max(0, (tailH - row.Tail.Height) / 2);
						}
					}
				}
				y += rowH;
			}

			int total = y + Sc(PadBottom) - Sc(6);
			if (apply)
			{
				ApplyFieldRegions();
			}
			return Math.Max(total, Sc(60));
		}

		// 折行数缓存：文字和可用宽度没变就直接复用，避免每次重排都建 Font + 两次 MeasureText
		private readonly Dictionary<string, int> _lineCache = new Dictionary<string, int>();
		private readonly Dictionary<string, int> _lineCacheWidth = new Dictionary<string, int>();

		private int _lineH;

		// 行数 = 折行后的总高 / 单行高。
		// 不能用 TextLines（折行高 / 不折行高）：文案里自带 \r\n 时两边行数相同，会被算成 1 行，
		// 于是长提示只分到一行的高度、第二段被裁掉。
		private int MeasureLinesExact(string text, Control ctl, int width)
		{
			try
			{
				if (_lineH <= 0)
				{
					_lineH = Math.Max(1, TextRenderer.MeasureText(Loc.T("字"), ctl.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Height);
				}
				int num = TextRenderer.MeasureText(text, ctl.Font, new Size(Math.Max(20, width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
				return Math.Max(1, (int)Math.Round((double)num / (double)_lineH));
			}
			catch
			{
				return 1;
			}
		}

		private int MeasureLines(string text, Control ctl, int width)
		{
			if (string.IsNullOrEmpty(text))
			{
				return 1;
			}
			string key = text;
			int num;
			if (_lineCache.TryGetValue(key, out num) && _lineCacheWidth.TryGetValue(key, out num) && num == width)
			{
				return _lineCache[key];
			}
			int num2 = MeasureLinesExact(text, ctl, width);
			_lineCache[key] = num2;
			_lineCacheWidth[key] = width;
			return num2;
		}

		private int ResolveBlockHeight(RowSpec row, int inner)
		{
			// Label 型的整宽块（提示文字）：高度按实际折行数算，不能吃写死的 BlockH，
			// 否则长文案（比如"流畅度 → 固定值"下面那段实测说明）第二行就会被裁掉。
			if (row.Ctl is Label)
			{
				return Math.Max(Sc(20), MeasureLines(row.Ctl.Text, row.Ctl, inner) * Sc(20));
			}
			if (row.Ctl is INeedHeight needHeight)
			{
				int num = needHeight.HeightFor(inner);
				if (num > 0)
				{
					return num;
				}
			}
			if (row.BlockH > 0)
			{
				return Sc(row.BlockH);
			}
			return Math.Max(Sc(32), row.Ctl.Height);
		}

		// 控件的设计高度：开关 24、输入类 32、其它各自 HeightFor。
		private static int ControlHeight(Control ctl, int width)
		{
			if (ctl == null)
			{
				return 0;
			}
			if (ctl is FlatCheck)
			{
				return Sc(24);
			}
			if (ctl is INeedHeight needHeight)
			{
				int num = needHeight.HeightFor(width);
				if (num > 0)
				{
					return num;
				}
			}
			return Sc(32);
		}

		private static int NaturalWidth(Control ctl)
		{
			if (ctl is FlatCheck)
			{
				return Sc(38);
			}
			if (ctl != null && ctl.Width > 0)
			{
				return ctl.Width;
			}
			return Sc(96);
		}

		// Region 是 GDI 对象：每次重排都新建一个、旧的不释放，拖着窗口改尺寸几分钟就能把
		// GDI 句柄堆到上限（之后整机都开始卡）。这里按尺寸缓存 + 换新时释放旧的。
		private readonly Dictionary<Control, Size> _fieldSizes = new Dictionary<Control, Size>();

		private void ApplyFieldRegions()
		{
			foreach (Control field in _fields)
			{
				try
				{
					if (field.Width <= 4 || field.Height <= 4)
					{
						continue;
					}
					Size size = new Size(field.Width, field.Height);
					Size size2;
					if (_fieldSizes.TryGetValue(field, out size2) && size2 == size)
					{
						continue;
					}
					Region region = field.Region;
					// 内缩 1px：控件自己的底色会盖住父控件画的描边，不缩这一圈就只剩四个圆角，
					// 输入框看起来就是「没有框、只有一行文字」。
					int inset = Math.Max(1, Sc(1));
					field.Region = new Region(RoundedPath(new Rectangle(inset, inset, Math.Max(2, size.Width - inset * 2), Math.Max(2, size.Height - inset * 2)), Math.Max(2, Sc(Tokens.RadiusField) - inset)));
					if (region != null)
					{
						region.Dispose();
					}
					_fieldSizes[field] = size;
				}
				catch
				{
				}
			}
		}

		public void SyncFieldRegions()
		{
			ApplyFieldRegions();
		}

		/// <summary>参与"需不需要重排"判定的文字（行标题、说明、整宽块的提示文字）。</summary>
		public void AppendSignature(StringBuilder sb)
		{
			foreach (RowSpec row in _rows)
			{
				if (row.TitleCtl != null)
				{
					sb.Append(row.TitleCtl.Text).Append('|');
				}
				if (row.NoteCtl != null)
				{
					sb.Append(row.NoteCtl.Text).Append('|');
				}
				if (row.Kind == RowKind.Block && row.Ctl is Label label)
				{
					sb.Append(label.Text).Append('|');
				}
			}
		}

		// ---------- 绘制 ----------

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			Rectangle clientRectangle = ClientRectangle;
			using (SolidBrush brush = new SolidBrush(Tokens.Canvas))
			{
				graphics.FillRectangle(brush, clientRectangle);
			}
			Rectangle rect = new Rectangle(0, 0, Math.Max(4, clientRectangle.Width - 1), Math.Max(4, clientRectangle.Height - 1));
			using (GraphicsPath path = RoundedPath(rect, Sc(Tokens.Radius)))
			{
				using (SolidBrush brush2 = new SolidBrush(Tokens.Surface))
				{
					graphics.FillPath(brush2, path);
				}
				using Pen pen = new Pen(Tokens.Stroke);
				graphics.DrawPath(pen, path);
			}
			// 输入框底衬：TextBox / NumericUpDown 自己是无边框的，圆角描边在这里画
			foreach (Control field in _fields)
			{
				if (!field.Visible || field.Width <= 4 || field.Height <= 4)
				{
					continue;
				}
				try
				{
					Rectangle rect2 = new Rectangle(field.Left, field.Top, field.Width, field.Height);
					using GraphicsPath fieldPath = RoundedPath(rect2, Sc(Tokens.RadiusField));
					using (SolidBrush brush3 = new SolidBrush(field.Enabled ? Tokens.FieldFill : Tokens.DisabledFill))
					{
						graphics.FillPath(brush3, fieldPath);
					}
					using Pen pen2 = new Pen(field.Enabled ? Tokens.FieldStroke : Tokens.DisabledStroke);
					graphics.DrawPath(pen2, fieldPath);
				}
				catch
				{
				}
			}
			try
			{
				if (_titleRect.Width > 0)
				{
					// 卡片标题：17px 半粗，和参照里的 H2 同一档
					TextRenderer.DrawText(graphics, Title, _fontTitle, _titleRect, Tokens.TextPrimary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				}
				if (_subRect.Width > 0)
				{
					TextRenderer.DrawText(graphics, Subtitle, _fontSub, _subRect, Tokens.TextTertiary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				}
				if (_dividers.Count > 0)
				{
					int padX = Sc(PadX);
					using Pen pen3 = new Pen(Tokens.Divider);
					foreach (int divider in _dividers)
					{
						graphics.DrawLine(pen3, padX, divider, Math.Max(padX + 4, clientRectangle.Width - padX - 1), divider);
					}
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
	}

	// ======================= 侧栏导航 =======================
	//
	// 参照里的侧栏：整条浅色底 + 右侧 1px 分隔线，
	// 顶部品牌区（图标 + 名称 + 副标题），中间导航胶囊（选中=淡强调色底），
	// 底部一张状态小卡。三段式比"一列项目干挂在顶上"稳得多。

	private sealed class SideNav : Control
	{
		private readonly Tween[] _glow;
		private int _active;
		private int _hot = -1;
		private string[] _titles = Array.Empty<string>();
		private string[] _glyphs = Array.Empty<string>();
		private string _statusTitle = Loc.T("正在运行");
		private string _statusDetail = "";
		private Bitmap _brand;
		private bool _brandTried;
		// 侧栏是常驻控件，字体必须缓存：原来每次 OnPaint 都 new 6 个 Font
		private readonly Font _fontBrand;
		private readonly Font _fontBrandSub;
		private readonly Font _fontItem;
		private readonly Font _fontGlyph;
		private readonly Font _fontStatus;
		private readonly Font _fontStatusSub;
		public event Action<int> Select;

		public SideNav()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Tokens.Sidebar;
			_glow = new Tween[16];
			_fontBrand = new Font(DesignFontFamily, Sc(14), FontStyle.Bold, GraphicsUnit.Pixel);
			_fontBrandSub = new Font(DesignFontFamily, Sc(11), FontStyle.Regular, GraphicsUnit.Pixel);
			_fontItem = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel);
			_fontGlyph = Icons.At(Sc(16));
			_fontStatus = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel);
			_fontStatusSub = new Font(DesignFontFamily, Sc(11), FontStyle.Regular, GraphicsUnit.Pixel);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_fontBrand.Dispose();
				_fontBrandSub.Dispose();
				_fontItem.Dispose();
				_fontGlyph?.Dispose();
				_fontStatus.Dispose();
				_fontStatusSub.Dispose();
				_brand?.Dispose();
			}
			base.Dispose(disposing);
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ActiveIndex
		{
			get
			{
				return _active;
			}
			set
			{
				if (value != _active && value >= 0 && value < _titles.Length)
				{
					_active = value;
					// 其它项的动画值归零：它们不是当前页，不该留下任何底色
					for (int i = 0; i < _glow.Length; i++)
					{
						if (i != value && _glow[i] != null)
						{
							_glow[i].Snap(0f);
						}
					}
					Invalidate();
				}
			}
		}

		public int ItemCount => _titles.Length;

		public void Bind(string[] titles, string[] glyphs, Action<int> onSelect)
		{
			_titles = titles;
			_glyphs = glyphs;
			Select = onSelect;
			Invalidate();
		}

		/// <summary>底部状态卡的两行字（运行状态 + 当前触发键）。</summary>
		public void SetStatus(string title, string detail)
		{
			_statusTitle = title ?? "";
			_statusDetail = detail ?? "";
			Invalidate();
		}

		private int RowH => Sc(38);

		private int RowTop => Sc(84);

		private Bitmap Brand
		{
			get
			{
				if (!_brandTried)
				{
					_brandTried = true;
					try
					{
						using Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
						if (icon != null)
						{
							_brand = icon.ToBitmap();
						}
					}
					catch
					{
						_brand = null;
					}
				}
				return _brand;
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int num = HitRow(e.Y);
			if (num != _hot)
			{
				_hot = num;
				Cursor = ((num >= 0) ? Cursors.Hand : Cursors.Default);
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = -1;
			Cursor = Cursors.Default;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				int num = HitRow(e.Y);
				if (num >= 0 && num < _titles.Length && Select != null)
				{
					Select(num);
					Invalidate();
				}
			}
			base.OnMouseUp(e);
		}

		private int HitRow(int y)
		{
			for (int i = 0; i < _titles.Length; i++)
			{
				int num = RowTop + i * RowH;
				if (y >= num && y < num + RowH)
				{
					return i;
				}
			}
			return -1;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			Rectangle clientRectangle = ClientRectangle;
			using (SolidBrush solidBrush = new SolidBrush(Tokens.Sidebar))
			{
				graphics.FillRectangle(solidBrush, clientRectangle);
			}
			using (Pen pen = new Pen(Tokens.Stroke))
			{
				graphics.DrawLine(pen, clientRectangle.Width - 1, 0, clientRectangle.Width - 1, clientRectangle.Height);
			}
			DrawBrand(graphics, clientRectangle);
			DrawStatus(graphics, clientRectangle);
			if (_titles.Length == 0)
			{
				return;
			}
			Font font = _fontItem;
			Font font2 = _fontGlyph;
			for (int i = 0; i < _titles.Length; i++)
			{
				int num = RowTop + i * RowH;
				Rectangle r = new Rectangle(Sc(10), num + Sc(2), Math.Max(20, clientRectangle.Width - Sc(20)), RowH - Sc(4));
				bool flag = i == _active;
				bool flag2 = i == _hot;
				float num2 = 0f;
				if (i < _glow.Length && _glow[i] != null)
				{
					num2 = _glow[i].Value;
				}
				else if (flag)
				{
					num2 = 1f;
				}
				// 只有"当前页"和"鼠标悬停"才配背景。
				// 之前这里写的是 glow > 0.01 就画 —— 但 glow 是一次性打满的动画值（打到 1 就不再回落），
				// 结果点过的每一项都会永久留一个灰胶囊，看起来像"没选中却高亮"。
				if (flag || flag2)
				{
					Color color = (flag ? Tokens.AccentSoft : Tokens.Hover);
					float num3 = (flag ? Math.Min(1f, Math.Max(0.4f, num2)) : 0.7f);
					using GraphicsPath path = RoundedPath(r, Sc(10));
					using (SolidBrush brush = new SolidBrush(Mix(Color.FromArgb(0, color), color, num3)))
					{
						graphics.FillPath(brush, path);
					}
				}
				// 当前页用强调色文字，悬停用主文字色：一眼能看出「我在哪一页」
				Color color2 = (flag ? Tokens.Accent : ((flag2 ? Tokens.TextPrimary : Tokens.TextSecondary)));
				int num4 = r.Left + Sc(12);
				if (font2 != null && i < _glyphs.Length)
				{
					using (SolidBrush brush2 = new SolidBrush(flag ? Tokens.Accent : Tokens.TextSecondary))
					{
						TextRenderer.DrawText(graphics, _glyphs[i], font2, new Rectangle(num4, r.Top + (r.Height - Sc(16)) / 2, Sc(16), Sc(16)), flag ? Tokens.Accent : Tokens.TextSecondary, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
					}
					num4 += Sc(16) + Sc(11);
				}
				TextRenderer.DrawText(graphics, _titles[i], font, new Rectangle(num4, r.Top, Math.Max(10, r.Right - Sc(10) - num4), r.Height), color2, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
		}

		private void DrawBrand(Graphics graphics, Rectangle client)
		{
			try
			{
				int num = Sc(18);
				int num2 = Sc(30);
				Rectangle rect = new Rectangle(num, num + Sc(2), num2, num2);
				Bitmap brand = Brand;
				if (brand != null)
				{
					using (GraphicsPath path = RoundedPath(new Rectangle(rect.X - Sc(1), rect.Y - Sc(1), rect.Width + Sc(2), rect.Height + Sc(2)), Sc(8)))
					{
						using SolidBrush brush = new SolidBrush(Tokens.Surface);
						graphics.FillPath(brush, path);
						using Pen pen = new Pen(Tokens.Stroke);
						graphics.DrawPath(pen, path);
					}
					graphics.DrawImage(brand, rect);
				}
				else
				{
					using GraphicsPath path2 = RoundedPath(rect, Sc(8));
					using (SolidBrush brush2 = new SolidBrush(Tokens.AccentSoft))
					{
						graphics.FillPath(brush2, path2);
					}
					using Font font = Icons.At(Sc(16));
					if (font != null)
					{
						TextRenderer.DrawText(graphics, Glyph.Move, font, rect, Tokens.Accent, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
					}
				}
				int num3 = rect.Right + Sc(11);
				Font font2 = _fontBrand;
				Font font3 = _fontBrandSub;
				TextRenderer.DrawText(graphics, Loc.T("长按拖窗"), font2, new Rectangle(num3, rect.Top - Sc(2), Math.Max(20, client.Width - num3 - Sc(10)), Sc(20)), Tokens.TextPrimary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				TextRenderer.DrawText(graphics, "HoldDrag · Win11 x64", font3, new Rectangle(num3, rect.Top + Sc(16), Math.Max(20, client.Width - num3 - Sc(10)), Sc(16)), Tokens.TextTertiary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		private void DrawStatus(Graphics graphics, Rectangle client)
		{
			try
			{
				int num = Sc(62);
				Rectangle rect = new Rectangle(Sc(12), Math.Max(RowTop + _titles.Length * RowH + Sc(8), client.Height - num - Sc(14)), Math.Max(40, client.Width - Sc(24)), num);
				if (rect.Bottom > client.Height - Sc(6) || rect.Height < Sc(20))
				{
					return;
				}
				using (GraphicsPath path = RoundedPath(rect, Sc(12)))
				{
					using (SolidBrush brush = new SolidBrush(Tokens.SurfaceAlt))
					{
						graphics.FillPath(brush, path);
					}
					using Pen pen = new Pen(Tokens.Stroke);
					graphics.DrawPath(pen, path);
				}
				int num2 = rect.Left + Sc(13);
				int num3 = rect.Top + Sc(12);
				using (SolidBrush brush2 = new SolidBrush(Tokens.Success))
				{
					graphics.FillEllipse(brush2, new Rectangle(num2, num3 + Sc(6), Sc(8), Sc(8)));
				}
				Font font = _fontStatus;
				Font font2 = _fontStatusSub;
				TextRenderer.DrawText(graphics, _statusTitle, font, new Rectangle(num2 + Sc(15), num3, Math.Max(20, rect.Right - num2 - Sc(24)), Sc(20)), Tokens.TextSecondary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
				TextRenderer.DrawText(graphics, _statusDetail, font2, new Rectangle(num2, num3 + Sc(21), Math.Max(20, rect.Right - num2 - Sc(13)), Sc(18)), Tokens.TextTertiary, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}

		internal void KickGlow(int index)
		{
			if (index >= 0 && index < _glow.Length)
			{
				if (_glow[index] == null)
				{
					_glow[index] = new Tween(this);
				}
				_glow[index].To(1f);
			}
		}
	}

	// ======================= 自绘开关 =======================

	private sealed class FlatCheck : CheckBox
	{
		private readonly Tween _hoverT;
		private readonly Tween _checkT;

		public int TrackW => Sc(38);
		public int TrackH => Sc(20);
		public int KnobInset => Sc(2);
		public int KnobSize => TrackH - KnobInset * 2;

		public FlatCheck()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			FlatStyle = FlatStyle.Flat;
			BackColor = Tokens.Surface;
			ForeColor = Tokens.TextPrimary;
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel);
			Cursor = Cursors.Hand;
			_hoverT = new Tween(this);
			_checkT = new Tween(this);
			_checkT.Snap(Checked ? 1f : 0f);
		}

		public Rectangle TrackRect => new Rectangle(0, Math.Max(0, (Height - TrackH) / 2), TrackW, TrackH);

		public Point KnobCenter
		{
			get
			{
				Rectangle trackRect = TrackRect;
				float num = (Checked ? Math.Min(1f, _checkT.Value) : 0f);
				if (Checked && num <= 0.001f)
				{
					num = 1f;
				}
				int num2 = KnobInset;
				int num3 = TrackW - num2 * 2 - KnobSize;
				int num4 = trackRect.Left + num2 + (int)Math.Round((float)num3 * num);
				return new Point(num4 + KnobSize / 2, trackRect.Top + KnobInset + KnobSize / 2);
			}
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hoverT.To(1f);
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hoverT.To(0f);
			base.OnMouseLeave(e);
		}

		protected override void OnCheckedChanged(EventArgs e)
		{
			if (Checked)
			{
				_checkT.To(1f);
			}
			else
			{
				_checkT.Snap(0f);
			}
			base.OnCheckedChanged(e);
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			Invalidate();
			base.OnEnabledChanged(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			// 同按钮：用父容器底色清屏，避免在卡片上留一块异色直角
			graphics.Clear((Parent != null) ? Parent.BackColor : Tokens.Surface);
			Rectangle trackRect = TrackRect;
			int radius = TrackH / 2;
			bool flag = Checked;
			float value = _hoverT.Value;
			float num = (flag ? Math.Min(1f, _checkT.Value) : 0f);
			if (flag && num <= 0.001f)
			{
				num = 1f;
			}
			Color color;
			if (!Enabled)
			{
				color = (flag ? Tokens.TrackOff : Tokens.Divider);
			}
			else
			{
				Color a = Mix(Tokens.TrackOff, Tokens.TrackOffHover, value);
				Color b = Mix(Tokens.Accent, ControlPaint.Light(Tokens.Accent, 0.08f), value);
				color = Mix(a, b, num);
			}
			using (GraphicsPath path = RoundedPath(trackRect, radius))
			{
				using (SolidBrush brush2 = new SolidBrush(color))
				{
					graphics.FillPath(brush2, path);
				}
			}
			Point knobCenter = KnobCenter;
			Rectangle rect = new Rectangle(knobCenter.X - KnobSize / 2, knobCenter.Y - KnobSize / 2, KnobSize, KnobSize);
			using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(28, Tokens.TextPrimary)))
			{
				graphics.FillEllipse(brush3, new Rectangle(rect.X, rect.Y + Math.Max(1, Sc(1)), rect.Width, rect.Height));
			}
			using (SolidBrush brush4 = new SolidBrush(Enabled ? Tokens.SwitchKnob : Tokens.Surface))
			{
				graphics.FillEllipse(brush4, rect);
			}
			Rectangle bounds = new Rectangle(trackRect.Right + Sc(8), 0, Math.Max(10, Width - trackRect.Right - Sc(10)), Height);
			if (bounds.Width > 0)
			{
				Color foreColor = (Enabled ? ForeColor : Tokens.TextTertiary);
				TextRenderer.DrawText(graphics, Text, Font, bounds, foreColor, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
			}
		}

		internal void KickHoverForTest()
		{
			_hoverT.To(1f);
		}

		internal float HoverValueForTest => _hoverT.Value;

		internal void SnapCheckForTest(bool on)
		{
			_checkT.Snap(on ? 1f : 0f);
			Checked = on;
			Invalidate();
		}
	}

	// ======================= 自绘按钮 =======================

	private sealed class FlatButton : Button
	{
		private readonly Tween _hoverT;
		private bool _primary;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool Primary
		{
			get
			{
				return _primary;
			}
			set
			{
				_primary = value;
				Invalidate();
			}
		}

		public FlatButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			FlatStyle = FlatStyle.Flat;
			BackColor = Tokens.Surface;
			ForeColor = Tokens.TextPrimary;
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel);
			Cursor = Cursors.Hand;
			_hoverT = new Tween(this);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hoverT.To(1f);
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hoverT.To(0f);
			base.OnMouseLeave(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			HQ.Apply(graphics);
			Rectangle clientRectangle = ClientRectangle;
			// 清屏用父容器底色：BackColor 是控件自己的填充色，拿它清屏会在圆角外留下一块同色直角，
			// 看上去就是「方按钮」（深色主题下尤其明显）。
			graphics.Clear((Parent != null) ? Parent.BackColor : Tokens.Canvas);
			float value = _hoverT.Value;
			Color color = (_primary ? Tokens.Accent : Tokens.ButtonHover);
			Color back = Mix(_primary ? Tokens.Accent : Tokens.Surface, color, value);
			if (!Enabled)
			{
				back = Tokens.Canvas;
			}
			using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, clientRectangle.Width, clientRectangle.Height), Sc(Tokens.RadiusField)))
			{
				using (SolidBrush brush = new SolidBrush(back))
				{
					graphics.FillPath(brush, path);
				}
				if (!_primary)
				{
					using (Pen pen = new Pen(Enabled ? Tokens.InputStroke : Tokens.Divider))
					{
						graphics.DrawPath(pen, path);
					}
				}
			}
			Color foreColor = (_primary ? Tokens.TextOnAccent : Tokens.TextPrimary);
			if (!Enabled)
			{
				foreColor = Tokens.TextTertiary;
			}
			TextRenderer.DrawText(graphics, Text, Font, new Rectangle(Sc(8), 0, Math.Max(10, clientRectangle.Width - Sc(16)), clientRectangle.Height), foreColor, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
		}

		internal void KickHoverForTest()
		{
			_hoverT.To(1f);
		}

		internal float HoverValueForTest => _hoverT.Value;
	}

	// ======================= 标题条按钮 =======================

	// 3 个窗口按钮：最小化 / 最大化(还原) / 关闭。
	// 图标用线条自己画（不依赖图标字体），悬停/按下都有反馈，关闭按钮悬停变红。
	private sealed class CaptionButton : Control
	{
		private bool _hot;
		private bool _down;
		private int _kind;
		private bool _maximized;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Kind
		{
			get
			{
				return _kind;
			}
			set
			{
				_kind = value;
				Invalidate();
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool IsMaximized
		{
			get
			{
				return _maximized;
			}
			set
			{
				if (_maximized != value)
				{
					_maximized = value;
					Invalidate();
				}
			}
		}

		public CaptionButton()
		{
			SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
			BackColor = Tokens.Canvas;
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hot = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hot = false;
			_down = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				_down = true;
				Invalidate();
			}
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
			HQ.Apply(graphics);
			Rectangle clientRectangle = ClientRectangle;
			// 清屏用父容器底色：BackColor 是控件自己的填充色，拿它清屏会在圆角外留下一块同色直角，
			// 看上去就是「方按钮」（深色主题下尤其明显）。
			graphics.Clear((Parent != null) ? Parent.BackColor : Tokens.Canvas);
			if (_hot || _down)
			{
				Color color = ((_kind == 2) ? Tokens.Danger : Tokens.Hover);
				if (_down)
				{
					color = ControlPaint.Dark(color, 0.08f);
				}
				Rectangle rect = new Rectangle(Sc(2), Sc(2), Math.Max(4, clientRectangle.Width - Sc(4)), Math.Max(4, clientRectangle.Height - Sc(4)));
				using GraphicsPath path = RoundedPath(rect, Sc(Tokens.RadiusChip));
				using SolidBrush brush = new SolidBrush(color);
				graphics.FillPath(brush, path);
			}
			Color color2 = ((_kind == 2 && _hot) ? Color.White : Tokens.TextSecondary);
			float num = Math.Max(1.1f, (float)Sc(1) * 1.15f);
			using Pen pen = new Pen(color2, num);
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			int num2 = clientRectangle.Width / 2;
			int num3 = clientRectangle.Height / 2;
			int num4 = Math.Max(4, Sc(5));
			switch (_kind)
			{
			case 0:
				graphics.DrawLine(pen, num2 - num4, num3, num2 + num4, num3);
				break;
			case 1:
				if (_maximized)
				{
					graphics.DrawRectangle(pen, num2 - num4 + Sc(1), num3 - num4, num4 * 2 - Sc(2), num4 * 2 - Sc(2));
					graphics.DrawRectangle(pen, num2 - num4, num3 - num4 + Sc(2), num4 * 2 - Sc(2), num4 * 2 - Sc(2));
				}
				else
				{
					graphics.DrawRectangle(pen, num2 - num4, num3 - num4, num4 * 2, num4 * 2);
				}
				break;
			default:
				graphics.DrawLine(pen, num2 - num4 + 1, num3 - num4 + 1, num2 + num4 - 1, num3 + num4 - 1);
				graphics.DrawLine(pen, num2 + num4 - 1, num3 - num4 + 1, num2 - num4 + 1, num3 + num4 - 1);
				break;
			}
		}
	}
}
