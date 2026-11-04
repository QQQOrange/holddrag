// HoldDrag · UI/SettingsForm.Theme.cs

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

internal sealed partial class SettingsForm : Form
{
	// NCCALCSIZE_PARAMS：只用到 rgrc[0]（新的客户区矩形，屏幕坐标）。
	[StructLayout(LayoutKind.Sequential)]
	private struct NCCALCSIZE_PARAMS
	{
		public Native.RECT rgrc0;

		public Native.RECT rgrc1;

		public Native.RECT rgrc2;

		public IntPtr lppos;
	}

	// ======================= 值对象（自测探针序列化用） =======================

	internal sealed class FrameSelection
	{
		public string ColorId;
		public string StyleId;
		public int Thickness;
		public int Radius;
		public bool FrameEnabled;
		public string PreviewColorId;
		public string PreviewStyleId;
		public int PreviewThickness;
		public int PreviewRadius;
		public bool PreviewFrameEnabled;
		public string GridColorId;
		public string GridStyleId;
		public string ColorNameText;
		public bool FollowAccent;
		public int CustomColorArgb;
		public string EffectiveColorHex;
	}

	internal sealed class FramePickerProbe
	{
		public int ColorCells;
		public int StyleCells;
		public long GridInk;
		public long GalleryInk;
		public long GridAccentInk;
		public long GalleryAccentInk;
		public Rectangle GridSelectedCell;
		public Rectangle GallerySelectedCell;
		public Size GridSize;
		public Size GallerySize;
		public bool GridInsideGroup;
		public bool GalleryInsideGroup;
	}

	internal sealed class GroupClipReport
	{
		public int Total;
		public int Clipped;
		public string Detail = "";
	}

	internal sealed class RenderStats
	{
		public int Width;
		public int Height;
		public int DistinctColors;
		public int BlackPixels;
		public int AccentPixels;
		public int FrameColorPixels;
		public double MeanBrightness;
	}

	// ======================= 系统主题（浅色 / 深色） =======================

	// 跟着 Windows 的「应用模式」走：读 HKCU\...\Themes\Personalize\AppsUseLightTheme。
	// 设置页在 WM_SETTINGCHANGE / WM_THEMECHANGED 时重新读，真的变了才整体重建（和 DPI 变化同一条路），
	// 这样切系统白夜间时不用重开窗口，颜色也不会停在上一次的样子。
	private static class Theme
	{
		private static bool _dark;
		private static bool _probed;
		private static bool? _forced;

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

		/// <summary>重新探测系统主题；返回"和上次比是否变了"。</summary>
		public static bool Refresh()
		{
			bool dark = DetectDark();
			bool changed = _probed && dark != _dark;
			_dark = dark;
			_probed = true;
			return changed;
		}

		/// <summary>截图/自检用：强制按某个主题渲染（null = 回到跟随系统）。</summary>
		public static void SetForceDark(bool? dark)
		{
			_forced = dark;
			_probed = false;
		}

		public static bool DetectDark()
		{
			if (_forced.HasValue)
			{
				return _forced.Value;
			}
			try
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
				if (registryKey != null)
				{
					object value = registryKey.GetValue("AppsUseLightTheme");
					if (value is int num)
					{
						return num == 0;
					}
				}
			}
			catch
			{
			}
			return false;
		}
	}

	// ======================= 设计令牌 =======================

	// 一套亮色、一套暗色。颜色统一走属性而不是 const/readonly 字段，
	// 这样控件在重绘时天然拿到当前主题的颜色（不需要挨个控件重新赋值）。
	// 调色板与圆角照「按键增强」那套来：
	// 底色是极浅的冷灰蓝，卡片纯白 + 1px 描边分层；不用投影堆层次 ——
	// WinForms 里阴影又慢又让文字发虚，浅描边反而更干净、更"产品"。
	private static class Tokens
	{
		private static Color P(Color light, Color dark)
		{
			return Theme.IsDark ? dark : light;
		}

		// ——— v1.11「Aurora」调色板 ———
		// 底色从偏蓝的冷灰换成中性带一点紫的暖灰，卡片纯白分层；
		// 主色从"老蓝" #2B6CF6 换成靛紫 #5B5BD6（暗色下提亮到 #918CFF），
		// 灰阶拉开 3 级（主文字 / 次文字 / 提示），保证层级一眼分得清。

		public static Color Canvas => P(Color.FromArgb(247, 247, 251), Color.FromArgb(11, 12, 16));

		public static Color Sidebar => P(Color.FromArgb(252, 252, 254), Color.FromArgb(15, 16, 20));

		public static Color Surface => P(Color.FromArgb(255, 255, 255), Color.FromArgb(21, 22, 27));

		/// <summary>再凹一层的底色（预览框内衬、样式缩略图、侧栏状态卡）。</summary>
		public static Color SurfaceAlt => P(Color.FromArgb(249, 249, 253), Color.FromArgb(18, 19, 24));

		public static Color Stroke => P(Color.FromArgb(233, 233, 241), Color.FromArgb(36, 38, 46));

		public static Color StrokeStrong => P(Color.FromArgb(218, 218, 231), Color.FromArgb(47, 50, 60));

		public static Color Divider => P(Color.FromArgb(240, 240, 246), Color.FromArgb(30, 32, 39));

		/// <summary>悬停底色（导航项、列表行、次要按钮）。</summary>
		public static Color Hover => P(Color.FromArgb(242, 242, 249), Color.FromArgb(27, 29, 36));

		public static Color TextPrimary => P(Color.FromArgb(20, 21, 27), Color.FromArgb(239, 240, 245));

		public static Color TextSecondary => P(Color.FromArgb(91, 95, 112), Color.FromArgb(165, 169, 186));

		public static Color TextTertiary => P(Color.FromArgb(142, 146, 165), Color.FromArgb(112, 116, 137));

		public static Color Accent => P(Color.FromArgb(91, 91, 214), Color.FromArgb(145, 140, 255));

		public static Color AccentPressed => P(Color.FromArgb(74, 74, 194), Color.FromArgb(168, 163, 255));

		public static Color AccentSoft => P(Color.FromArgb(239, 237, 254), Color.FromArgb(26, 25, 48));

		public static Color AccentSoftStroke => P(Color.FromArgb(221, 217, 252), Color.FromArgb(43, 41, 80));

		public static Color InputStroke => P(Color.FromArgb(223, 223, 235), Color.FromArgb(46, 49, 64));

		public static Color InputStrokeHover => P(Color.FromArgb(203, 203, 221), Color.FromArgb(59, 62, 74));

		public static Color FieldFill => P(Color.FromArgb(246, 246, 250), Color.FromArgb(29, 30, 38));

		public static Color FieldStroke => P(Color.FromArgb(223, 223, 235), Color.FromArgb(46, 49, 64));

		/// <summary>录制按键时输入框的底色。暗色主题不能用浅黄（浅底 + 浅字等于看不见）。</summary>
		public static Color FieldRecording => P(Color.FromArgb(255, 246, 222), Color.FromArgb(58, 48, 26));

		/// <summary>录制按键时输入框上的文字色，与 <see cref="FieldRecording"/> 成对使用。</summary>
		public static Color FieldRecordingText => P(Color.FromArgb(20, 21, 27), Color.FromArgb(247, 236, 196));

		/// <summary>禁用态统一底色（下拉框、数字框、文本框、自绘控件都用它）。</summary>
		public static Color DisabledFill => P(Color.FromArgb(247, 247, 251), Color.FromArgb(24, 25, 31));

		/// <summary>禁用态统一描边。</summary>
		public static Color DisabledStroke => P(Color.FromArgb(236, 236, 243), Color.FromArgb(36, 38, 46));

		/// <summary>禁用态文字。</summary>
		public static Color DisabledText => P(Color.FromArgb(160, 164, 180), Color.FromArgb(104, 108, 124));

		public static Color Hint => TextTertiary;

		public static Color WarnText => P(Color.FromArgb(180, 83, 9), Color.FromArgb(251, 191, 36));

		public static Color Success => P(Color.FromArgb(22, 163, 74), Color.FromArgb(74, 222, 128));

		public static Color Danger => P(Color.FromArgb(220, 38, 38), Color.FromArgb(248, 113, 113));

		public static Color TrackOff => P(Color.FromArgb(220, 220, 232), Color.FromArgb(52, 54, 63));

		public static Color TrackOffHover => P(Color.FromArgb(201, 201, 218), Color.FromArgb(67, 70, 79));

		public static Color ButtonHover => P(Color.FromArgb(243, 243, 250), Color.FromArgb(29, 31, 39));

		/// <summary>强调色底上的文字（暗色主题的强调色偏亮，用深色字更清楚）。</summary>
		public static Color TextOnAccent => P(Color.FromArgb(255, 255, 255), Color.FromArgb(18, 16, 31));

		/// <summary>开关滑块（暗色主题不用纯白，避免刺眼）。</summary>
		public static Color SwitchKnob => P(Color.FromArgb(255, 255, 255), Color.FromArgb(244, 244, 248));

		public static Color CardShadow => P(Color.FromArgb(9, 17, 24, 39), Color.FromArgb(72, 0, 0, 0));

		/// <summary>卡片圆角。</summary>
		public const int Radius = 16;

		/// <summary>控件圆角（输入框、按钮、色块、导航胶囊）。</summary>
		public const int RadiusField = 10;

		/// <summary>小圆角（缩略图、色块内圈、标签）。</summary>
		public const int RadiusChip = 8;

		/// <summary>胶囊圆角（开关轨道、状态点）。</summary>
		public const int RadiusPill = 999;

		/// <summary>侧栏宽度。</summary>
		public const int SidebarW = 232;

		/// <summary>标题栏高度。</summary>
		public const int TitleBarH = 60;

		/// <summary>内容列最大宽度（参照里是 880，左对齐）。</summary>
		public const int CardMaxW = 880;

		/// <summary>卡片之间的垂直间距。</summary>
		public const int CardGap = 16;

		// ——— 字号（设计像素，统一 6 档，杜绝"到处都是 12/13/13.5/17"） ———
		public const float FontBrand = 14f;

		public const float FontPageTitle = 17f;

		public const float FontCardTitle = 15f;

		public const float FontRow = 13f;

		public const float FontNote = 12f;

		public const float FontTiny = 11f;

	}

	private static class HQ
	{
		public static void Apply(Graphics g)
		{
			g.SmoothingMode = SmoothingMode.HighQuality;
			g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			g.CompositingQuality = CompositingQuality.HighQuality;
			g.PixelOffsetMode = PixelOffsetMode.Half;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		}
	}

	private static class Icons
	{
		private static FontFamily _family;
		private static bool _probed;

		public static FontFamily Family
		{
			get
			{
				if (_probed)
				{
					return _family;
				}
				_probed = true;
				string[] array = new string[2] { "Segoe Fluent Icons", "Segoe MDL2 Assets" };
				try
				{
					FontFamily[] families = FontFamily.Families;
					for (int i = 0; i < array.Length; i++)
					{
						for (int j = 0; j < families.Length; j++)
						{
							if (string.Equals(families[j].Name, array[i], StringComparison.OrdinalIgnoreCase))
							{
								_family = families[j];
								return _family;
							}
						}
					}
				}
				catch
				{
				}
				return null;
			}
		}

		public static bool Available => Family != null;

		public static Font At(float px)
		{
			FontFamily family = Family;
			if (family == null)
			{
				return null;
			}
			try
			{
				return new Font(family, px, FontStyle.Regular, GraphicsUnit.Pixel);
			}
			catch
			{
				return null;
			}
		}
	}

	internal static class Glyph
	{
		public const string Keyboard = "\ue765";
		public const string Move = "\ue7c2";
		public const string Color = "\ue790";
		public const string Speed = "\ue9d9";
		public const string Shield = "\ue72e";
		public const string Chevron = "\ue76c";
		public const string Help = "\ue897";
		public const string Info = "\ue946";
	}

	// ======================= 补间动画 =======================

	private sealed class Tween
	{
		private static readonly List<Tween> Active = new List<Tween>();
		private static readonly System.Windows.Forms.Timer Timer = new System.Windows.Forms.Timer
		{
			Interval = 16
		};

		private readonly Control _owner;
		private float _from;
		private float _to;
		private float _value;
		private int _elapsed;
		private const int Duration = 170;

		static Tween()
		{
			Timer.Tick += delegate
			{
				for (int i = Active.Count - 1; i >= 0; i--)
				{
					Tween tween = Active[i];
					if (tween._owner == null || tween._owner.IsDisposed)
					{
						Active.RemoveAt(i);
						continue;
					}
					tween._elapsed += Timer.Interval;
					float num = Math.Min(1f, (float)tween._elapsed / (float)Duration);
					float num2 = 1f - (float)Math.Pow(1f - num, 3.0);
					tween._value = tween._from + (tween._to - tween._from) * num2;
					if (num >= 1f)
					{
						tween._value = tween._to;
						Active.RemoveAt(i);
					}
					try
					{
						tween._owner.Invalidate();
					}
					catch
					{
					}
				}
				if (Active.Count == 0)
				{
					Timer.Stop();
				}
			};
		}

		public Tween(Control owner)
		{
			_owner = owner;
		}

		public float Value => _value;

		public void Snap(float v)
		{
			_value = v;
			_to = v;
			_from = v;
			_elapsed = Duration;
			for (int i = 0; i < Active.Count; i++)
			{
				if (Active[i] == this)
				{
					Active.RemoveAt(i);
					break;
				}
			}
		}

		public void To(float target)
		{
			if (Math.Abs(_value - target) < 0.0001f && Math.Abs(_to - target) < 0.0001f)
			{
				return;
			}
			_from = _value;
			_to = target;
			_elapsed = 0;
			if (!Active.Contains(this))
			{
				Active.Add(this);
			}
			if (!Timer.Enabled)
			{
				Timer.Start();
			}
		}
	}
}
