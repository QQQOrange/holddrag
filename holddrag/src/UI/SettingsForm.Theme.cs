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
	/// <summary>
	/// 兼容壳：真正的深浅色判定在 UiTheme 里（和 32 个颜色同源，避免两边判断不一致）。
	/// </summary>
	private static class Theme
	{
		public static bool IsDark => UiTheme.IsDark;

		public static bool Refresh() => UiTheme.Refresh();

		public static void SetForceDark(bool? dark) => UiTheme.SetForceDark(dark);

		public static bool DetectDark() => UiTheme.DetectDark();
	}

	// ======================= 设计令牌 =======================

	// 一套亮色、一套暗色。颜色统一走属性而不是 const/readonly 字段，
	// 这样控件在重绘时天然拿到当前主题的颜色（不需要挨个控件重新赋值）。
	// 调色板与圆角照「按键增强」那套来：
	// 底色是极浅的冷灰蓝，卡片纯白 + 1px 描边分层；不用投影堆层次 ——
	// WinForms 里阴影又慢又让文字发虚，浅描边反而更干净、更"产品"。


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

	/// <summary>漏斗：窗口规则页（按程序筛选）。</summary>
	public const string Filter = "\ue71c";

	/// <summary>窗口叠放：工作区布局。</summary>
	public const string Layout = "\ue8a9";
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
