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

		public static Color Canvas => P(Color.FromArgb(242, 245, 250), Color.FromArgb(22, 24, 29));

		public static Color Sidebar => P(Color.FromArgb(251, 252, 254), Color.FromArgb(27, 30, 36));

		public static Color Surface => P(Color.FromArgb(255, 255, 255), Color.FromArgb(33, 36, 41));

		/// <summary>再凹一层的底色（预览框内衬、样式缩略图、侧栏状态卡）。</summary>
		public static Color SurfaceAlt => P(Color.FromArgb(245, 248, 252), Color.FromArgb(26, 29, 34));

		public static Color Stroke => P(Color.FromArgb(228, 232, 240), Color.FromArgb(46, 50, 58));

		public static Color StrokeStrong => P(Color.FromArgb(211, 217, 230), Color.FromArgb(58, 63, 73));

		public static Color Divider => P(Color.FromArgb(237, 241, 247), Color.FromArgb(42, 46, 53));

		/// <summary>悬停底色（导航项、列表行、次要按钮）。</summary>
		public static Color Hover => P(Color.FromArgb(240, 243, 249), Color.FromArgb(38, 42, 49));

		public static Color TextPrimary => P(Color.FromArgb(22, 26, 35), Color.FromArgb(237, 239, 244));

		public static Color TextSecondary => P(Color.FromArgb(91, 100, 120), Color.FromArgb(168, 176, 191));

		public static Color TextTertiary => P(Color.FromArgb(138, 147, 166), Color.FromArgb(120, 128, 143));

		public static Color Accent => P(Color.FromArgb(43, 108, 246), Color.FromArgb(91, 141, 249));

		public static Color AccentPressed => P(Color.FromArgb(31, 91, 224), Color.FromArgb(127, 167, 255));

		public static Color AccentSoft => P(Color.FromArgb(232, 240, 255), Color.FromArgb(27, 41, 66));

		public static Color AccentSoftStroke => P(Color.FromArgb(199, 218, 255), Color.FromArgb(44, 68, 112));

		public static Color InputStroke => P(Color.FromArgb(220, 226, 238), Color.FromArgb(52, 58, 68));

		public static Color InputStrokeHover => P(Color.FromArgb(201, 210, 227), Color.FromArgb(65, 72, 84));

		public static Color FieldFill => P(Color.FromArgb(255, 255, 255), Color.FromArgb(28, 31, 36));

		public static Color FieldStroke => P(Color.FromArgb(220, 226, 238), Color.FromArgb(52, 58, 68));

		/// <summary>录制按键时输入框的底色。暗色主题不能用浅黄（浅底 + 浅字等于看不见）。</summary>
		public static Color FieldRecording => P(Color.FromArgb(255, 244, 214), Color.FromArgb(64, 52, 24));

		/// <summary>录制按键时输入框上的文字色，与 <see cref="FieldRecording"/> 成对使用。</summary>
		public static Color FieldRecordingText => P(Color.FromArgb(22, 26, 35), Color.FromArgb(245, 232, 185));

		/// <summary>禁用态统一底色（下拉框、数字框、文本框、自绘控件都用它）。</summary>
		public static Color DisabledFill => P(Color.FromArgb(246, 248, 251), Color.FromArgb(32, 35, 40));

		/// <summary>禁用态统一描边。</summary>
		public static Color DisabledStroke => P(Color.FromArgb(230, 234, 241), Color.FromArgb(48, 52, 60));

		/// <summary>禁用态文字。</summary>
		public static Color DisabledText => P(Color.FromArgb(160, 168, 183), Color.FromArgb(110, 118, 132));

		public static Color Hint => TextTertiary;

		public static Color WarnText => P(Color.FromArgb(138, 90, 11), Color.FromArgb(226, 180, 100));

		public static Color Success => P(Color.FromArgb(31, 157, 98), Color.FromArgb(79, 206, 143));

		public static Color Danger => P(Color.FromArgb(229, 72, 77), Color.FromArgb(242, 118, 118));

		public static Color TrackOff => P(Color.FromArgb(219, 226, 238), Color.FromArgb(62, 68, 78));

		public static Color TrackOffHover => P(Color.FromArgb(203, 212, 227), Color.FromArgb(76, 83, 95));

		public static Color ButtonHover => P(Color.FromArgb(240, 243, 249), Color.FromArgb(42, 46, 53));

		/// <summary>强调色底上的文字（暗色主题的强调色偏亮，用深色字更清楚）。</summary>
		public static Color TextOnAccent => P(Color.FromArgb(255, 255, 255), Color.FromArgb(9, 14, 22));

		/// <summary>开关滑块（暗色主题不用纯白，避免刺眼）。</summary>
		public static Color SwitchKnob => P(Color.FromArgb(255, 255, 255), Color.FromArgb(244, 246, 249));

		public static Color CardShadow => P(Color.FromArgb(10, 16, 32, 64), Color.FromArgb(80, 0, 0, 0));

		/// <summary>卡片圆角（参照里是 14）。</summary>
		public const int Radius = 14;

		/// <summary>控件圆角（输入框、按钮、色块、导航胶囊，参照里是 9）。</summary>
		public const int RadiusField = 9;

		/// <summary>侧栏宽度（参照 1240 宽窗口用 228）。</summary>
		public const int SidebarW = 228;

		/// <summary>标题栏高度（参照里是 56）。</summary>
		public const int TitleBarH = 56;

		/// <summary>内容列最大宽度（参照里是 880，左对齐）。</summary>
		public const int CardMaxW = 880;

		/// <summary>卡片之间的垂直间距。</summary>
		public const int CardGap = 14;
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
