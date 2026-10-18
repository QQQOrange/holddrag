using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HoldDrag;

internal static class CoordSpace
{
	private static double _scale = 1.0;

	private static bool _initialized;

	public static double Scale => _scale;

	public static bool IsIdentity => _scale == 1.0;

	public static void Init()
	{
		if (_initialized)
		{
			return;
		}
		_initialized = true;
		try
		{
			int num = -1;
			try
			{
				num = Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext());
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			if (num != 0)
			{
				_scale = 1.0;
				return;
			}
			int systemMetrics = Native.GetSystemMetrics(0);
			int num2 = PhysicalScreenWidth();
			if (systemMetrics > 0 && num2 > 0 && num2 != systemMetrics)
			{
				_scale = (double)systemMetrics / (double)num2;
				Log.Info("★ 进程是 DPI 不感知的，坐标将按 " + _scale.ToString("0.####") + " 换算（逻辑宽 " + systemMetrics + " / 物理宽 " + num2 + "）");
			}
			else
			{
				Log.Info("★ 进程是 DPI 不感知的，但查不到物理分辨率，坐标保持原样（逻辑宽 " + systemMetrics + "，物理宽 " + num2 + "）");
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
			_scale = 1.0;
		}
	}

	private static int PhysicalScreenWidth()
	{
		try
		{
			nint dC = Native.GetDC(IntPtr.Zero);
			if (dC == IntPtr.Zero)
			{
				return 0;
			}
			try
			{
				return Native.GetDeviceCaps(dC, 118);
			}
			finally
			{
				Native.ReleaseDC(IntPtr.Zero, dC);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return 0;
	}

	internal static void SetScaleForTest(double s)
	{
		_initialized = true;
		_scale = ((s > 0.0) ? s : 1.0);
	}

	public static Native.POINT ToProcess(Native.POINT pt)
	{
		if (_scale == 1.0)
		{
			return pt;
		}
		return new Native.POINT((int)Math.Round((double)pt.X * _scale), (int)Math.Round((double)pt.Y * _scale));
	}

	public static Native.POINT ToPhysical(Native.POINT pt)
	{
		if (_scale == 1.0)
		{
			return pt;
		}
		return new Native.POINT((int)Math.Round((double)pt.X / _scale), (int)Math.Round((double)pt.Y / _scale));
	}
}
internal static class FrameColors
{
	internal struct Entry
	{
		public string Id;

		public string Name;

		public Color Color;
	}

	public const string Teal = "Teal";

	public const string Aqua = "Aqua";

	public const string Green = "Green";

	public const string Amber = "Amber";

	public const string Coral = "Coral";

	public const string Red = "Red";

	public const string Pink = "Pink";

	public const string Violet = "Violet";

	public const string Graphite = "Graphite";

	public const string Silver = "Silver";

	public const string Accent = "Accent";

	public const string Lime = "Lime";

	public const string Rose = "Rose";

	public const string Gold = "Gold";

	public const string Sky = "Sky";

	public const string Obsidian = "Obsidian";

	public const string Pearl = "Pearl";

	internal static readonly Entry[] All = new Entry[17]
	{
		new Entry
		{
			Id = "Teal",
			Name = "薄荷青",
			Color = Color.FromArgb(107, 196, 165)
		},
		new Entry
		{
			Id = "Aqua",
			Name = "湖水青",
			Color = Color.FromArgb(111, 200, 188)
		},
		new Entry
		{
			Id = "Sky",
			Name = "天青",
			Color = Color.FromArgb(100, 194, 214)
		},
		new Entry
		{
			Id = "Green",
			Name = "抹茶绿",
			Color = Color.FromArgb(116, 200, 148)
		},
		new Entry
		{
			Id = "Lime",
			Name = "嫩芽绿",
			Color = Color.FromArgb(168, 203, 106)
		},
		new Entry
		{
			Id = "Gold",
			Name = "暖金",
			Color = Color.FromArgb(214, 193, 106)
		},
		new Entry
		{
			Id = "Amber",
			Name = "蜜橙",
			Color = Color.FromArgb(221, 169, 92)
		},
		new Entry
		{
			Id = "Coral",
			Name = "珊瑚",
			Color = Color.FromArgb(232, 147, 118)
		},
		new Entry
		{
			Id = "Red",
			Name = "暖砖红",
			Color = Color.FromArgb(222, 133, 133)
		},
		new Entry
		{
			Id = "Rose",
			Name = "玫瑰",
			Color = Color.FromArgb(222, 133, 149)
		},
		new Entry
		{
			Id = "Pink",
			Name = "藕粉",
			Color = Color.FromArgb(222, 147, 188)
		},
		new Entry
		{
			Id = "Violet",
			Name = "藕荷",
			Color = Color.FromArgb(163, 142, 219)
		},
		new Entry
		{
			Id = "Graphite",
			Name = "石墨",
			Color = Color.FromArgb(107, 122, 143)
		},
		new Entry
		{
			Id = "Silver",
			Name = "银灰",
			Color = Color.FromArgb(168, 179, 193)
		},
		new Entry
		{
			Id = "Obsidian",
			Name = "曜石",
			Color = Color.FromArgb(58, 68, 83)
		},
		new Entry
		{
			Id = "Pearl",
			Name = "珍珠白",
			Color = Color.FromArgb(237, 241, 246)
		},
		new Entry
		{
			Id = "Accent",
			Name = "跟随系统强调色",
			Color = Color.FromArgb(0, 120, 212)
		}
	};

	private static Color _accentCache;

	private static long _accentAt;

	public static string DefaultId => "Teal";

	public static bool IsValid(string id)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i].Id == id)
			{
				return true;
			}
		}
		return false;
	}

	public static Color Resolve(string id)
	{
		if (id == "Accent")
		{
			long tickCount = Environment.TickCount64;
			if (_accentAt != 0L && tickCount - _accentAt < 2000)
			{
				return _accentCache;
			}
			try
			{
				Native.ColorAccent accent = Native.GetAccent();
				_accentCache = Color.FromArgb(accent.R, accent.G, accent.B);
			}
			catch
			{
				_accentCache = Color.FromArgb(0, 120, 212);
			}
			_accentAt = tickCount;
			return _accentCache;
		}
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i].Id == id)
			{
				return All[i].Color;
			}
		}
		return All[0].Color;
	}
}
internal static class FrameStyles
{
	public const string Glow = "Glow";

	public const string Flat = "Flat";

	public const string Solid = "Solid";

	public const string Double = "Double";

	public const string Dashed = "Dashed";

	public const string Aurora = "Aurora";

	public const string Neon = "Neon";

	public const string Halo = "Halo";

	public const string Corners = "Corners";

	public const string Shadow = "Shadow";

	public const string Underline = "Underline";

	public const string Brackets = "Brackets";

	public static readonly string[] All = new string[12]
	{
		"Glow", "Flat", "Solid", "Double", "Dashed", "Aurora", "Neon", "Halo", "Corners", "Shadow",
		"Underline", "Brackets"
	};

	private static readonly int[] GlowAlpha = new int[3] { 48, 30, 18 };

	public static string DefaultId => "Glow";

	internal static int MeasureStyleInkForTest(string style)
	{
		try
		{
			using Bitmap bitmap = new Bitmap(120, 90, PixelFormat.Format32bppArgb);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.Transparent);
				Draw(graphics, new RectangleF(0f, 0f, 120f, 90f), Color.FromArgb(107, 196, 165), style, 4, 8);
			}
			int num = 0;
			for (int i = 0; i < 90; i++)
			{
				for (int j = 0; j < 120; j++)
				{
					if (bitmap.GetPixel(j, i).A > 8)
					{
						num++;
					}
				}
			}
			return num;
		}
		catch
		{
			return -1;
		}
	}

	public static bool IsValid(string id)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i] == id)
			{
				return true;
			}
		}
		return false;
	}

	public static string DisplayName(string id)
	{
		return id switch
		{
			"Glow" => "柔光环（推荐）", 
			"Flat" => "纯净环（无光晕）", 
			"Solid" => "实心描边（最醒目）", 
			"Double" => "双线（极简）", 
			"Dashed" => "虚线环", 
			"Aurora" => "极光（渐变）", 
			"Neon" => "霓虹（发光）", 
			"Halo" => "光晕（弥散）", 
			"Corners" => "四角框（最轻量）", 
			"Shadow" => "柔和投影（不画实线）", 
			"Underline" => "下缘光条（只提示底边）", 
			"Brackets" => "四边中点（四条短线）", 
			_ => id, 
		};
	}

	public static int MarginFor(string style, int thickness)
	{
		int num = thickness / 2 + 1;
		return style switch
		{
			"Glow" => num + 10, 
			"Aurora" => num + 10, 
			"Neon" => num + 12, 
			"Halo" => num + 14, 
			"Dashed" => num + 7, 
			"Solid" => (int)((float)thickness * 0.9f) + 3, 
			"Double" => num + 7, 
			"Corners" => num + 4, 
			"Shadow" => num + 16, 
			"Underline" => num + 10, 
			"Brackets" => num + 6, 
			_ => num + 3, 
		};
	}

	public static void Draw(Graphics g, RectangleF area, Color color, string style, int thickness, int radius, int marginOverride = -1)
	{
		int num = ((marginOverride >= 0) ? marginOverride : MarginFor(style, thickness));
		RectangleF rectangleF = new RectangleF(area.X + (float)num, area.Y + (float)num, area.Width - (float)num * 2f, area.Height - (float)num * 2f);
		if (rectangleF.Width < 6f || rectangleF.Height < 6f)
		{
			return;
		}
		float num2 = ClampRadius(radius, rectangleF.Width, rectangleF.Height);
		float num3 = thickness;
		if (style == "Solid")
		{
			num3 = Math.Max(2f, (float)thickness * 1.8f);
		}
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.Half;
		switch (style)
		{
		case "Aurora":
			DrawAurora(g, rectangleF, num2, color, num3);
			return;
		case "Neon":
			DrawNeon(g, rectangleF, num2, color, num3);
			return;
		case "Halo":
			DrawHalo(g, rectangleF, num2, color, num3);
			return;
		case "Corners":
			DrawCorners(g, rectangleF, num2, color, num3);
			return;
		case "Shadow":
			DrawShadow(g, rectangleF, num2, color, num3);
			return;
		case "Underline":
			DrawUnderline(g, rectangleF, num2, color, num3);
			return;
		case "Brackets":
			DrawBrackets(g, rectangleF, num2, color, num3);
			return;
		}
		if (style == "Glow" || style == "Dashed")
		{
			for (int num4 = ((style == "Glow") ? 3 : 2); num4 >= 1; num4--)
			{
				using GraphicsPath path = RoundedPath(rectangleF, num2);
				using Pen pen = new Pen(Color.FromArgb(GlowAlpha[num4 - 1], color), num3 + 3f * (float)num4);
				pen.LineJoin = LineJoin.Round;
				ApplyDash(pen, style, num2);
				g.DrawPath(pen, path);
			}
		}
		using (GraphicsPath path2 = RoundedPath(rectangleF, num2))
		{
			using Pen pen2 = new Pen(color, num3);
			pen2.LineJoin = LineJoin.Round;
			ApplyDash(pen2, style, num2);
			g.DrawPath(pen2, path2);
		}
		if (!(style == "Double"))
		{
			return;
		}
		using GraphicsPath path3 = RoundedPath(new RectangleF(rectangleF.X - 4.5f, rectangleF.Y - 4.5f, rectangleF.Width + 9f, rectangleF.Height + 9f), num2 + 4.5f);
		using Pen pen3 = new Pen(Color.FromArgb(140, color), 1.5f);
		pen3.LineJoin = LineJoin.Round;
		g.DrawPath(pen3, path3);
	}

	private static void DrawAurora(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		using (GraphicsPath path = RoundedPath(ring, r))
		{
			using Pen pen = new Pen(Color.FromArgb(38, color), t + 8f);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using GraphicsPath path2 = RoundedPath(ring, r);
		using LinearGradientBrush linearGradientBrush = new LinearGradientBrush(ring, Lighten(color, 0.45f), Darken(color, 0.42f), 45f);
		linearGradientBrush.InterpolationColors = new ColorBlend(3)
		{
			Colors = new Color[3]
			{
				Lighten(color, 0.48f),
				color,
				Darken(color, 0.42f)
			},
			Positions = new float[3] { 0f, 0.5f, 1f }
		};
		using Pen pen2 = new Pen(linearGradientBrush, Math.Max(2f, t * 1.35f));
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path2);
	}

	private static void DrawNeon(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		for (int num = 3; num >= 1; num--)
		{
			int alpha = 20 + (3 - num) * 22;
			using GraphicsPath path = RoundedPath(ring, r);
			using Pen pen = new Pen(Color.FromArgb(alpha, color), t + 3.4f * (float)num);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using GraphicsPath path2 = RoundedPath(ring, r);
		using Pen pen2 = new Pen(Lighten(color, 0.5f), Math.Max(1.6f, t * 0.8f));
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path2);
	}

	private static void DrawHalo(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		for (int num = 4; num >= 1; num--)
		{
			int alpha = 12 + (4 - num) * 15;
			using GraphicsPath path = RoundedPath(ring, r);
			using Pen pen = new Pen(Color.FromArgb(alpha, color), t + 4.4f * (float)num);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using GraphicsPath path2 = RoundedPath(ring, r);
		using Pen pen2 = new Pen(color, t);
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path2);
	}

	private static void DrawShadow(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		Color baseColor = Color.FromArgb((int)((float)(int)color.R * 0.42f), (int)((float)(int)color.G * 0.42f), (int)((float)(int)color.B * 0.42f));
		int[] array = new int[4] { 15, 11, 7, 4 };
		int[] array2 = new int[4] { 12, 18, 26, 38 };
		for (int i = 0; i < array.Length; i++)
		{
			float num = array[i];
			RectangleF rc = RectangleF.Inflate(ring, num * 0.5f, num * 0.5f);
			float num2 = Math.Min(r + num * 0.5f, Math.Min(rc.Width, rc.Height) / 2f);
			if (num2 < 0f)
			{
				num2 = 0f;
			}
			using GraphicsPath path = RoundedPath(rc, num2);
			using Pen pen = new Pen(Color.FromArgb(array2[i], baseColor), num);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using GraphicsPath path2 = RoundedPath(ring, r);
		using Pen pen2 = new Pen(Color.FromArgb(110, color), Math.Max(1f, t * 0.45f));
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path2);
	}

	private static void DrawUnderline(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		float num = Math.Max(2f, t * 1.1f);
		float num2 = ring.Left + ring.Width * 0.1f;
		float num3 = ring.Right - ring.Width * 0.1f;
		float num4 = ring.Bottom - num / 2f;
		if (num3 <= num2)
		{
			return;
		}
		using LinearGradientBrush linearGradientBrush = new LinearGradientBrush(new RectangleF(num2, num4 - num, num3 - num2, num * 2f), color, color, LinearGradientMode.Horizontal);
		ColorBlend colorBlend = new ColorBlend(3);
		colorBlend.Colors = new Color[3]
		{
			Color.FromArgb(0, color),
			Color.FromArgb(235, color),
			Color.FromArgb(0, color)
		};
		colorBlend.Positions = new float[3] { 0f, 0.5f, 1f };
		linearGradientBrush.InterpolationColors = colorBlend;
		using (Pen pen = new Pen(linearGradientBrush, num + 9f))
		{
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			g.DrawLine(pen, num2, num4, num3, num4);
		}
		using Pen pen2 = new Pen(linearGradientBrush, num);
		pen2.StartCap = LineCap.Round;
		pen2.EndCap = LineCap.Round;
		g.DrawLine(pen2, num2, num4, num3, num4);
	}

	private static void DrawBrackets(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		float num = Math.Max(18f, Math.Min(ring.Width, ring.Height) * 0.22f);
		float num2 = ring.Left + ring.Width / 2f;
		float num3 = ring.Top + ring.Height / 2f;
		float num4 = num / 2f;
		using (Pen pen = new Pen(color, t))
		{
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			g.DrawLine(pen, num2 - num4, ring.Top, num2 + num4, ring.Top);
			g.DrawLine(pen, num2 - num4, ring.Bottom, num2 + num4, ring.Bottom);
			g.DrawLine(pen, ring.Left, num3 - num4, ring.Left, num3 + num4);
			g.DrawLine(pen, ring.Right, num3 - num4, ring.Right, num3 + num4);
		}
		using GraphicsPath path = RoundedPath(ring, r);
		using Pen pen2 = new Pen(Color.FromArgb(34, color), Math.Max(1f, t * 0.38f));
		pen2.LineJoin = LineJoin.Round;
		g.DrawPath(pen2, path);
	}

	private static void DrawCorners(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		float num = Math.Max(16f, Math.Min(ring.Width, ring.Height) * 0.2f);
		float num2 = Math.Min(r, Math.Min(num * 0.7f, Math.Min(ring.Width, ring.Height) / 2f - 1f));
		if (num2 < 0f)
		{
			num2 = 0f;
		}
		using GraphicsPath path = CornerBrackets(ring, num2, num);
		using Pen pen = new Pen(color, t);
		pen.LineJoin = LineJoin.Round;
		pen.StartCap = LineCap.Round;
		pen.EndCap = LineCap.Round;
		g.DrawPath(pen, path);
	}

	private static GraphicsPath CornerBrackets(RectangleF rc, float rad, float len)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = rad * 2f;
		graphicsPath.StartFigure();
		graphicsPath.AddLine(rc.Left, rc.Top + len, rc.Left, rc.Top + rad);
		if (rad > 0.5f)
		{
			graphicsPath.AddArc(rc.Left, rc.Top, num, num, 180f, 90f);
		}
		graphicsPath.AddLine(rc.Left + rad, rc.Top, rc.Left + len, rc.Top);
		graphicsPath.StartFigure();
		graphicsPath.AddLine(rc.Right - len, rc.Top, rc.Right - rad, rc.Top);
		if (rad > 0.5f)
		{
			graphicsPath.AddArc(rc.Right - num, rc.Top, num, num, 270f, 90f);
		}
		graphicsPath.AddLine(rc.Right, rc.Top + rad, rc.Right, rc.Top + len);
		graphicsPath.StartFigure();
		graphicsPath.AddLine(rc.Right, rc.Bottom - len, rc.Right, rc.Bottom - rad);
		if (rad > 0.5f)
		{
			graphicsPath.AddArc(rc.Right - num, rc.Bottom - num, num, num, 0f, 90f);
		}
		graphicsPath.AddLine(rc.Right - rad, rc.Bottom, rc.Right - len, rc.Bottom);
		graphicsPath.StartFigure();
		graphicsPath.AddLine(rc.Left + len, rc.Bottom, rc.Left + rad, rc.Bottom);
		if (rad > 0.5f)
		{
			graphicsPath.AddArc(rc.Left, rc.Bottom - num, num, num, 90f, 90f);
		}
		graphicsPath.AddLine(rc.Left, rc.Bottom - rad, rc.Left, rc.Bottom - len);
		return graphicsPath;
	}

	private static Color Lighten(Color c, float amt)
	{
		return Color.FromArgb(c.A, Mix(c.R, 255, amt), Mix(c.G, 255, amt), Mix(c.B, 255, amt));
	}

	private static Color Darken(Color c, float amt)
	{
		return Color.FromArgb(c.A, Mix(c.R, 0, amt), Mix(c.G, 0, amt), Mix(c.B, 0, amt));
	}

	private static int Mix(int v, int target, float amt)
	{
		int num = (int)Math.Round((float)v + (float)(target - v) * amt);
		if (num < 0)
		{
			num = 0;
		}
		if (num > 255)
		{
			num = 255;
		}
		return num;
	}

	private static void ApplyDash(Pen pen, string style, float radius)
	{
		if (!(style != "Dashed"))
		{
			pen.DashStyle = DashStyle.Dash;
			pen.DashCap = DashCap.Round;
			float num = Math.Max(4f, radius * 0.85f);
			pen.DashPattern = new float[2]
			{
				num,
				num * 0.72f
			};
		}
	}

	public static float ClampRadius(int radius, float w, float h)
	{
		float num = Math.Min(w, h) / 2f - 1f;
		if (num < 0f)
		{
			num = 0f;
		}
		float num2 = radius;
		if (num2 < 0f)
		{
			num2 = 0f;
		}
		if (num2 > num)
		{
			num2 = num;
		}
		return num2;
	}

	public static bool FillsScreen(Native.RECT vis, int margin, bool wasInside)
	{
		try
		{
			int num = 0;
			int num2 = 0;
			try
			{
				Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(new Native.POINT((vis.Left + vis.Right) / 2, (vis.Top + vis.Bottom) / 2));
				num = monitorInfoAt.rcMonitor.Width;
				num2 = monitorInfoAt.rcMonitor.Height;
			}
			catch
			{
			}
			if (num <= 0 || num2 <= 0)
			{
				Rectangle virtualScreen = SystemInformation.VirtualScreen;
				num = virtualScreen.Width;
				num2 = virtualScreen.Height;
			}
			if (num <= 0 || num2 <= 0)
			{
				return false;
			}
			int num3 = (wasInside ? 88 : 92);
			return vis.Width >= (long)num * (long)num3 / 100 && vis.Height >= (long)num2 * (long)num3 / 100;
		}
		catch
		{
			return false;
		}
	}

	private static GraphicsPath RoundedPath(RectangleF rc, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		if (radius <= 0.5f)
		{
			graphicsPath.AddRectangle(rc);
			return graphicsPath;
		}
		float num = radius * 2f;
		graphicsPath.AddArc(rc.X, rc.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(rc.Right - num, rc.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(rc.Right - num, rc.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rc.X, rc.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
internal static class Log
{
	private static readonly object Gate = new object();

	private static string _path = string.Empty;

	private static StreamWriter _writer;

	public static string Path => _path;

	public static bool Verbose { get; set; }

	public static void Init(string dir)
	{
		try
		{
			_path = System.IO.Path.Combine(dir, "holddrag.log");
			if (!CanOpenForAppend(_path))
			{
				_path = System.IO.Path.Combine(dir, "holddrag." + Process.GetCurrentProcess().Id + ".log");
			}
			Trim();
			CleanupOldLogs(dir);
		}
		catch
		{
			_path = string.Empty;
		}
	}

	private static bool CanOpenForAppend(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				return true;
			}
			using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
			{
				return true;
			}
		}
		catch
		{
			return false;
		}
	}

	public static void Info(string msg)
	{
		Write("INFO", msg);
	}

	public static void Trace(string msg)
	{
		if (Verbose)
		{
			Write("TRC ", msg);
		}
	}

	public static void Error(string msg)
	{
		Write("ERR ", msg);
	}

	public static void Error(Exception ex)
	{
		Write("ERR ", (ex != null) ? ex.ToString() : "null exception");
	}

	private static void Write(string level, string msg)
	{
		if (string.IsNullOrEmpty(_path))
		{
			return;
		}
		lock (Gate)
		{
			try
			{
				EnsureWriter();
				if (_writer == null)
				{
					File.AppendAllText(_path, Stamp(level, msg), Encoding.UTF8);
					return;
				}
				_writer.Write(Stamp(level, msg));
				_writer.Flush();
			}
			catch
			{
				CloseWriter();
			}
		}
	}

	private static string Stamp(string level, string msg)
	{
		StringBuilder stringBuilder = new StringBuilder(64);
		stringBuilder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
		stringBuilder.Append(' ');
		stringBuilder.Append(level);
		stringBuilder.Append(' ');
		stringBuilder.Append(msg);
		stringBuilder.Append("\r\n");
		return stringBuilder.ToString();
	}

	private static void EnsureWriter()
	{
		if (_writer == null)
		{
			_writer = new StreamWriter(new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
			_writer.AutoFlush = false;
		}
	}

	private static void CloseWriter()
	{
		try
		{
			if (_writer != null)
			{
				_writer.Dispose();
			}
		}
		catch
		{
		}
		_writer = null;
	}

	private static void CleanupOldLogs(string dir)
	{
		try
		{
			string[] files = Directory.GetFiles(dir, "holddrag*.log");
			if (files.Length <= 10)
			{
				return;
			}
			Array.Sort(files, (string a, string b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
			for (int num = 10; num < files.Length; num++)
			{
				if (!string.Equals(files[num], _path, StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						File.Delete(files[num]);
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}

	private static void Trim()
	{
		try
		{
			FileInfo fileInfo = new FileInfo(_path);
			if (fileInfo.Exists && fileInfo.Length > 524288)
			{
				CloseWriter();
				File.Delete(_path);
			}
		}
		catch
		{
		}
	}

	internal static void FlushAndClose()
	{
		lock (Gate)
		{
			CloseWriter();
		}
	}

	public static void Shutdown()
	{
		lock (Gate)
		{
			CloseWriter();
		}
	}
}
internal static class Native
{
	internal struct POINT(int x, int y)
	{
		public int X = x;

		public int Y = y;

		public override string ToString()
		{
			return X + "," + Y;
		}
	}

	internal struct RECT(int l, int t, int r, int b)
	{
		public int Left = l;

		public int Top = t;

		public int Right = r;

		public int Bottom = b;

		public int Width => Right - Left;

		public int Height => Bottom - Top;

		public bool IsEmpty
		{
			get
			{
				if (Width > 0)
				{
					return Height <= 0;
				}
				return true;
			}
		}

		public override string ToString()
		{
			return Left + "," + Top + " " + Width + "x" + Height;
		}
	}

	internal struct KBDLLHOOKSTRUCT
	{
		public uint vkCode;

		public uint scanCode;

		public uint flags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct MSLLHOOKSTRUCT
	{
		public POINT pt;

		public uint mouseData;

		public uint flags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public uint dwFlags;
	}

	internal struct WINDOWPLACEMENT
	{
		public int length;

		public int flags;

		public int showCmd;

		public POINT ptMinPosition;

		public POINT ptMaxPosition;

		public RECT rcNormalPosition;
	}

	internal struct MSG
	{
		public nint hwnd;

		public uint message;

		public nint wParam;

		public nint lParam;

		public uint time;

		public int ptX;

		public int ptY;
	}

	internal struct INPUT
	{
		public uint type;

		public INPUTUNION u;
	}

	[StructLayout(LayoutKind.Explicit)]
	internal struct INPUTUNION
	{
		[FieldOffset(0)]
		public MOUSEINPUT mi;

		[FieldOffset(0)]
		public KEYBDINPUT ki;

		[FieldOffset(0)]
		public HARDWAREINPUT hi;
	}

	internal struct MOUSEINPUT
	{
		public int dx;

		public int dy;

		public uint mouseData;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct KEYBDINPUT
	{
		public ushort wVk;

		public ushort wScan;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	internal struct HARDWAREINPUT
	{
		public uint uMsg;

		public ushort wParamL;

		public ushort wParamH;
	}

	internal delegate nint HookProc(int nCode, nint wParam, nint lParam);

	internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	internal struct SIZE(int w, int h)
	{
		public int cx = w;

		public int cy = h;
	}

	internal struct ColorAccent(int r, int g, int b)
	{
		public int R = r;

		public int G = g;

		public int B = b;
	}

	internal struct BLENDFUNCTION
	{
		public byte BlendOp;

		public byte BlendFlags;

		public byte SourceConstantAlpha;

		public byte AlphaFormat;
	}

	internal delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	internal struct WNDCLASSEX
	{
		public uint cbSize;

		public uint style;

		public WndProc lpfnWndProc;

		public int cbClsExtra;

		public int cbWndExtra;

		public nint hInstance;

		public nint hIcon;

		public nint hCursor;

		public nint hbrBackground;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string lpszMenuName;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string lpszClassName;

		public nint hIconSm;
	}

	public const int WH_KEYBOARD_LL = 13;

	public const int WH_MOUSE_LL = 14;

	public const int WM_KEYDOWN = 256;

	public const int WM_KEYUP = 257;

	public const int WM_SYSKEYDOWN = 260;

	public const int WM_SYSKEYUP = 261;

	public const int WM_MOUSEMOVE = 512;

	public const int WM_WINDOWPOSCHANGING = 70;

	public const int WM_WINDOWPOSCHANGED = 71;

	public const int WM_LBUTTONDOWN = 513;

	public const int WM_CANCELMODE = 31;

	public const int WM_LBUTTONUP = 514;

	public const int WM_RBUTTONDOWN = 516;

	public const int WM_RBUTTONUP = 517;

	public const int WM_MBUTTONDOWN = 519;

	public const int WM_MBUTTONUP = 520;

	public const int WM_MOUSEWHEEL = 522;

	public const int WM_XBUTTONDOWN = 523;

	public const int WM_XBUTTONUP = 524;

	public const int WM_MOUSEHWHEEL = 526;

	public const int MK_LBUTTON = 1;

	public const int MK_RBUTTON = 2;

	public const int MK_MBUTTON = 16;

	public const int WM_SYSCOMMAND = 274;

	public const int WM_CLOSE = 16;

	public const int SC_CLOSE = 61536;

	public const int SC_MAXIMIZE = 61488;

	public const int SC_MINIMIZE = 61472;

	public const int SC_RESTORE = 61728;

	public const uint LLKHF_EXTENDED = 1u;

	public const uint LLKHF_INJECTED = 16u;

	public const uint LLKHF_LOWER_IL_INJECTED = 2u;

	public const uint LLMHF_INJECTED = 1u;

	public const uint LLMHF_LOWER_IL_INJECTED = 2u;

	public const uint SWP_NOSIZE = 1u;

	public const uint SWP_NOMOVE = 2u;

	public const uint SWP_NOZORDER = 4u;

	public const uint SWP_NOACTIVATE = 16u;

	public const uint SWP_SHOWWINDOW = 64u;

	public const uint SWP_NOOWNERZORDER = 512u;

	public const uint SWP_NOSENDCHANGING = 1024u;

	public const uint SWP_NOCOPYBITS = 256u;

	public const uint SWP_ASYNCWINDOWPOS = 16384u;

	public const uint RESIZEFLAGS = 532u;

	public const uint MOVETHICKBORDERS = 533u;

	public const uint MOVEASYNC = 16917u;

	public const int WM_ENTERSIZEMOVE = 561;

	public const int WM_EXITSIZEMOVE = 562;

	public const int WM_SIZING = 532;

	public const int WM_SYNCPAINT = 136;

	public const uint SMTO_NORMAL = 0u;

	public const uint SMTO_ABORTIFHUNG = 2u;

	public const uint EVENT_SYSTEM_MOVESIZESTART = 10u;

	public const uint EVENT_SYSTEM_MOVESIZEEND = 11u;

	public const uint PM_NOREMOVE = 0u;

	public const uint PM_REMOVE = 1u;

	public const int WM_QUIT = 18;

	public static readonly nint HWND_TOP = IntPtr.Zero;

	public static readonly nint HWND_BOTTOM = new IntPtr(1);

	public static readonly nint HWND_TOPMOST = new IntPtr(-1);

	public static readonly nint HWND_NOTOPMOST = new IntPtr(-2);

	public const int SW_HIDE = 0;

	public const int SW_SHOWNORMAL = 1;

	public const int SW_SHOWMINIMIZED = 2;

	public const int SW_MAXIMIZE = 3;

	public const int SW_SHOWNOACTIVATE = 4;

	public const int SW_MINIMIZE = 6;

	public const int SW_RESTORE = 9;

	public const int GWL_STYLE = -16;

	public const int GWL_EXSTYLE = -20;

	public const long WS_VISIBLE = 268435456L;

	public const long WS_CAPTION = 12582912L;

	public const long WS_THICKFRAME = 262144L;

	public const long WS_MINIMIZE = 536870912L;

	public const long WS_MAXIMIZE = 16777216L;

	public const long WS_SYSMENU = 524288L;

	public const long WS_EX_LAYERED = 524288L;

	public const long WS_EX_TRANSPARENT = 32L;

	public const long WS_EX_TOOLWINDOW = 128L;

	public const long WS_EX_NOACTIVATE = 134217728L;

	public const long WS_EX_TOPMOST = 8L;

	public const uint GA_PARENT = 1u;

	public const uint GA_ROOT = 2u;

	public const uint GA_ROOTOWNER = 3u;

	public const uint MONITOR_DEFAULTTONULL = 0u;

	public const uint MONITOR_DEFAULTTOPRIMARY = 1u;

	public const uint MONITOR_DEFAULTTONEAREST = 2u;

	public const uint LWA_COLORKEY = 1u;

	public const uint LWA_ALPHA = 2u;

	public const uint INPUT_MOUSE = 0u;

	public const uint INPUT_KEYBOARD = 1u;

	public const uint MOUSEEVENTF_MOVE = 1u;

	public const uint MOUSEEVENTF_LEFTDOWN = 2u;

	public const uint MOUSEEVENTF_LEFTUP = 4u;

	public const uint MOUSEEVENTF_RIGHTDOWN = 8u;

	public const uint MOUSEEVENTF_RIGHTUP = 16u;

	public const uint MOUSEEVENTF_MIDDLEDOWN = 32u;

	public const uint MOUSEEVENTF_MIDDLEUP = 64u;

	public const uint MOUSEEVENTF_ABSOLUTE = 32768u;

	public const uint MOUSEEVENTF_VIRTUALDESK = 16384u;

	public const uint INJECT_LEFTDOWN = 2u;

	public const uint INJECT_LEFTUP = 4u;

	public const uint INJECT_RIGHTDOWN = 8u;

	public const uint INJECT_RIGHTUP = 16u;

	public const uint KEYEVENTF_EXTENDEDKEY = 1u;

	public const uint KEYEVENTF_KEYUP = 2u;

	public const uint KEYEVENTF_SCANCODE = 8u;

	public const ushort XBUTTON1 = 1;

	public const ushort XBUTTON2 = 2;

	public const int SM_CXMINTRACK = 34;

	public const int SM_CYMINTRACK = 35;

	public const int SM_CXSCREEN = 0;

	public const int SM_CYSCREEN = 1;

	internal const int WM_SETREDRAW = 11;

	internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 4096u;

	internal const int DPI_AWARENESS_INVALID = -1;

	internal const int DPI_AWARENESS_UNAWARE = 0;

	internal const int DPI_AWARENESS_SYSTEM_AWARE = 1;

	internal const int DPI_AWARENESS_PER_MONITOR_AWARE = 2;

	internal static readonly nint DPI_AWARENESS_CONTEXT_UNAWARE = new IntPtr(-1);

	internal static readonly nint DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new IntPtr(-2);

	internal static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new IntPtr(-3);

	internal static readonly nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

	internal const int DESKTOPHORZRES = 118;

	internal const int DESKTOPVERTRES = 117;

	internal const uint GW_HWNDNEXT = 2u;

	internal const uint GW_HWNDPREV = 3u;

	internal const uint CWP_SKIPINVISIBLE = 1u;

	internal const uint CWP_SKIPDISABLED = 2u;

	internal const uint CWP_SKIPTRANSPARENT = 4u;

	internal const int VK_LBUTTON = 1;

	internal const int VK_RBUTTON = 2;

	internal const int VK_MBUTTON = 4;

	internal const uint WM_NCHITTEST = 132u;

	internal const uint WM_ERASEBKGND = 20u;

	internal const uint WM_PAINT = 15u;

	internal const uint WM_DESTROY = 2u;

	internal const uint WM_MOUSEACTIVATE = 33u;

	internal const uint WM_APP = 32768u;

	internal const uint WM_DISPLAYCHANGE = 126u;

	internal const int HTTRANSPARENT = -1;

	internal const int MA_NOACTIVATE = 3;

	internal const uint WS_POPUP = 2147483648u;

	internal const uint WS_CLIPSIBLINGS = 67108864u;

	internal const uint ULW_ALPHA = 2u;

	internal const byte AC_SRC_OVER = 0;

	internal const byte AC_SRC_ALPHA = 1;

	internal const int RGN_AND = 1;

	internal const int RGN_OR = 2;

	internal const int RGN_DIFF = 4;

	internal const int VREFRESH = 116;

	internal const uint GR_GDIOBJECTS = 0u;

	internal const uint GR_USEROBJECTS = 1u;

	internal const int COLOR_HIGHLIGHT = 13;

	internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

	internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

	internal const int DWMWCP_ROUND = 2;

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint SetWindowsHookExW(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint GetModuleHandleW(string lpModuleName);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint WindowFromPoint(POINT Point);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetAncestor(nint hwnd, uint gaFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetWindowPlacement(nint hWnd, ref WINDOWPLACEMENT lpwndpl);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsWindow(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsZoomed(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetForegroundWindow();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SendMessageW(nint hWnd, int Msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool PostMessageW(nint hWnd, int Msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool CloseHandle(nint hObject);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetClassNameW(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetWindowTextW(nint hWnd, StringBuilder lpString, int nMaxCount);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int GetWindowTextLengthW(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int GetSystemMetrics(int nIndex);

	[DllImport("user32.dll")]
	internal static extern nint GetThreadDpiAwarenessContext();

	[DllImport("user32.dll")]
	internal static extern int GetAwarenessFromDpiAwarenessContext(nint value);

	[DllImport("user32.dll")]
	internal static extern uint GetDpiForWindow(nint hwnd);

	internal static int DpiOfWindow(nint hwnd)
	{
		try
		{
			if (hwnd != IntPtr.Zero)
			{
				uint dpiForWindow = GetDpiForWindow(hwnd);
				if (dpiForWindow >= 48 && dpiForWindow <= 960)
				{
					return (int)dpiForWindow;
				}
			}
		}
		catch
		{
		}
		return 96;
	}

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetProcessDpiAwarenessContext(nint value);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetProcessDpiAware();

	[DllImport("user32.dll")]
	internal static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint GetDoubleClickTime();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetParent(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetLayeredWindowAttributes(nint hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetWindow(nint hWnd, uint uCmd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint GetDesktopWindow();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint ChildWindowFromPointEx(nint hWndParent, POINT pt, uint uFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFO lpmi);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern short GetAsyncKeyState(int vKey);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint FindWindowW(string lpClassName, string lpWindowName);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SetCapture(nint hWnd);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ReleaseCapture();

	[DllImport("user32.dll")]
	internal static extern nint GetCapture();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

	[DllImport("kernel32.dll")]
	internal static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern bool PeekMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint DispatchMessageW(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool TranslateMessage(ref MSG lpMsg);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern nint SendMessageTimeoutW(nint hWnd, int msg, nint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern void NotifyWinEvent(uint eventId, nint hWnd, int idObject, int idChild);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetClipCursor(out RECT lpRect);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
	private static extern int GetWindowLong32(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
	private static extern nint GetWindowLong64(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
	private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	private static extern nint SetWindowLong64(nint hWnd, int nIndex, nint dwNewLong);

	internal static long GetWindowLong(nint hWnd, int nIndex)
	{
		if (IntPtr.Size == 8)
		{
			return ((IntPtr)GetWindowLong64(hWnd, nIndex)).ToInt64();
		}
		return GetWindowLong32(hWnd, nIndex);
	}

	internal static void SetWindowLong(nint hWnd, int nIndex, long value)
	{
		if (IntPtr.Size == 8)
		{
			SetWindowLong64(hWnd, nIndex, new IntPtr(value));
		}
		else
		{
			SetWindowLong32(hWnd, nIndex, (int)value);
		}
	}

	[DllImport("shell32.dll", SetLastError = true)]
	internal static extern bool IsUserAnAdmin();

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool DestroyIcon(nint hIcon);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	internal static extern nint CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	internal static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool DestroyWindow(nint hWnd);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateRectRgn(int x1, int y1, int x2, int y2);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern int CombineRgn(nint hrgnDst, nint hrgnSrc1, nint hrgnSrc2, int iMode);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern bool DeleteObject(nint ho);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern int SetWindowRgn(nint hWnd, nint hRgn, bool bRedraw);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ValidateRect(nint hWnd, nint lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool GetClientRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

	[DllImport("user32.dll")]
	internal static extern int FillRect(nint hDC, ref RECT lprc, nint hbr);

	[DllImport("gdi32.dll")]
	internal static extern nint CreateSolidBrush(uint crColor);

	[DllImport("user32.dll")]
	internal static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);

	internal static uint ColorRef(int r, int g, int b)
	{
		return (uint)(r | (g << 8) | (b << 16));
	}

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern bool UpdateLayeredWindow(nint hwnd, nint hdcDst, ref POINT pptDst, ref SIZE psize, nint hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint CreateCompatibleDC(nint hdc);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern nint SelectObject(nint hdc, nint h);

	[DllImport("gdi32.dll", SetLastError = true)]
	internal static extern bool DeleteDC(nint hdc);

	[DllImport("user32.dll")]
	internal static extern nint GetDC(nint hWnd);

	[DllImport("user32.dll")]
	internal static extern int ReleaseDC(nint hWnd, nint hDC);

	[DllImport("gdi32.dll")]
	internal static extern uint GetPixel(nint hdc, int x, int y);

	[DllImport("gdi32.dll")]
	internal static extern int GetDeviceCaps(nint hdc, int nIndex);

	[DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
	internal static extern uint TimeBeginPeriod(uint uMilliseconds);

	[DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
	internal static extern uint TimeEndPeriod(uint uMilliseconds);

	[DllImport("user32.dll", SetLastError = true)]
	internal static extern uint GetGuiResources(nint hProcess, uint uiFlags);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern nint GetCurrentProcess();

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool TerminateProcess(nint hProcess, uint uExitCode);

	[DllImport("kernel32.dll", SetLastError = true)]
	internal static extern bool GetProcessHandleCount(nint hProcess, out uint pdwHandleCount);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

	[DllImport("user32.dll")]
	internal static extern uint GetSysColor(int nIndex);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

	[DllImport("dwmapi.dll")]
	internal static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

	internal static bool SetWindowRoundedCorners(nint hwnd)
	{
		try
		{
			int pvAttribute = 2;
			return DwmSetWindowAttribute(hwnd, 33, ref pvAttribute, 4) == 0;
		}
		catch
		{
			return false;
		}
	}

	internal static int GetRefreshRate()
	{
		try
		{
			nint dC = GetDC(IntPtr.Zero);
			if (dC != IntPtr.Zero)
			{
				try
				{
					int deviceCaps = GetDeviceCaps(dC, 116);
					if (deviceCaps >= 24 && deviceCaps <= 1000)
					{
						return deviceCaps;
					}
				}
				finally
				{
					ReleaseDC(IntPtr.Zero, dC);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return 60;
	}

	internal static ColorAccent GetAccent()
	{
		try
		{
			if (DwmGetColorizationColor(out var pcrColorization, out var _) == 0)
			{
				int num = (int)((pcrColorization >> 16) & 0xFF);
				int num2 = (int)((pcrColorization >> 8) & 0xFF);
				int num3 = (int)(pcrColorization & 0xFF);
				if (num + num2 + num3 > 60)
				{
					return new ColorAccent(num, num2, num3);
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		try
		{
			uint sysColor = GetSysColor(13);
			int num4 = (int)(sysColor & 0xFF);
			int num5 = (int)((sysColor >> 8) & 0xFF);
			int num6 = (int)((sysColor >> 16) & 0xFF);
			if (num4 + num5 + num6 > 60)
			{
				return new ColorAccent(num4, num5, num6);
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		return new ColorAccent(0, 120, 212);
	}

	internal static bool TryGetVisibleRect(nint hwnd, out RECT r)
	{
		r = default;
		try
		{
			RECT pvAttribute = default;
			if (DwmGetWindowAttribute(hwnd, 9, out pvAttribute, Marshal.SizeOf(typeof(RECT))) == 0 && pvAttribute.Width > 0 && pvAttribute.Height > 0)
			{
				r = pvAttribute;
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return false;
	}

	internal static MONITORINFO GetMonitorInfoAt(POINT pt)
	{
		MONITORINFO lpmi = new MONITORINFO
		{
			cbSize = Marshal.SizeOf(typeof(MONITORINFO))
		};
		nint num = MonitorFromPoint(pt, 2u);
		if (num != IntPtr.Zero)
		{
			GetMonitorInfoW(num, ref lpmi);
		}
		return lpmi;
	}

	internal static bool IsEqualRect(RECT a, RECT b)
	{
		if (a.Left == b.Left && a.Top == b.Top && a.Right == b.Right)
		{
			return a.Bottom == b.Bottom;
		}
		return false;
	}

	internal static string GetClassName(nint hWnd)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		GetClassNameW(hWnd, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}

	internal static string GetWindowTitle(nint hWnd)
	{
		int windowTextLengthW = GetWindowTextLengthW(hWnd);
		if (windowTextLengthW <= 0)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(windowTextLengthW + 2);
		GetWindowTextW(hWnd, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString();
	}
}
internal abstract class OverlayWindow : IDisposable
{
	private const uint WM_APPLY = 32769u;

	private const uint WM_RETHEME = 32770u;

	private static readonly object ClassGate = new object();

	private static string _className;

	private static Native.WndProc _classProc;

	private static readonly Dictionary<nint, OverlayWindow> Instances = new Dictionary<nint, OverlayWindow>();

	private nint _hwnd = IntPtr.Zero;

	private bool _destroyed;

	private readonly object _pendingGate = new object();

	private Native.RECT _pending;

	private bool _pendingValid;

	private bool _hidePending;

	private bool _applyPosted;

	private volatile bool _rethemePending;

	private bool _rethemePosted;

	private bool _visible;

	private const uint WM_CREATE_OVERLAYS = 32779u;

	private const uint WM_DESTROY_OVERLAY = 32780u;

	private static Thread _ownerThread;

	private static uint _ownerThreadId;

	private static readonly object OwnerGate = new object();

	private static readonly ManualResetEventSlim _ownerReady = new ManualResetEventSlim(initialState: false);

	private static readonly List<OverlayWindow> _pendingCreate = new List<OverlayWindow>();

	private static volatile bool _ownerQuit;

	private readonly ManualResetEventSlim _created = new ManualResetEventSlim(initialState: false);

	private Native.RECT _lastApplied;

	private bool _lastAppliedValid;

	private int _lastW = -1;

	private int _lastH = -1;

	protected bool IsVisible => _visible;

	public nint Handle => _hwnd;

	public bool IsCreated => _hwnd != IntPtr.Zero;

	protected virtual bool LayeredStyle => true;

	internal static uint OwnerThreadIdForTest => _ownerThreadId;

	internal bool ShowRequestedForTest
	{
		get
		{
			lock (_pendingGate)
			{
				return _pendingValid && !_hidePending;
			}
		}
	}

	public void EnsureCreated()
	{
		if (_hwnd != IntPtr.Zero)
		{
			return;
		}
		StartOwnerThread();
		if (Native.GetCurrentThreadId() == _ownerThreadId)
		{
			CreateNow();
			return;
		}
		lock (OwnerGate)
		{
			_pendingCreate.Add(this);
			_created.Reset();
		}
		if (!Native.PostThreadMessageW(_ownerThreadId, 32779u, IntPtr.Zero, IntPtr.Zero))
		{
			lock (OwnerGate)
			{
				_pendingCreate.Remove(this);
			}
			Log.Error("投递浮层创建消息失败，退回当前线程创建");
			CreateNow();
		}
		else
		{
			_created.Wait(3000);
		}
	}

	private void CreateNow()
	{
		if (_hwnd != IntPtr.Zero)
		{
			return;
		}
		EnsureClass();
		uint num = 134217864u;
		if (LayeredStyle)
		{
			num |= 0x80000;
		}
		_hwnd = Native.CreateWindowExW(num, _className, "HoldDrag Overlay", 2147483648u, -32000, -32000, 1, 1, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandleW(null), IntPtr.Zero);
		if (_hwnd == IntPtr.Zero)
		{
			Log.Error("创建浮层窗口失败，Win32 错误码 " + Marshal.GetLastWin32Error());
			return;
		}
		lock (ClassGate)
		{
			Instances[_hwnd] = this;
		}
	}

	public static void StartOwnerThread()
	{
		lock (OwnerGate)
		{
			if (_ownerThread != null)
			{
				return;
			}
			_ownerThread = new Thread(OwnerThreadMain);
			_ownerThread.IsBackground = true;
			_ownerThread.Priority = ThreadPriority.AboveNormal;
			_ownerThread.Name = "HoldDrag.Overlays";
			_ownerThread.Start();
		}
		_ownerReady.Wait(3000);
	}

	private static void OwnerThreadMain()
	{
		try
		{
			_ownerThreadId = Native.GetCurrentThreadId();
			Native.PeekMessageW(out var _, IntPtr.Zero, 0u, 0u, 0u);
			_ownerReady.Set();
			Native.MSG lpMsg2;
			while (!_ownerQuit && Native.GetMessageW(out lpMsg2, IntPtr.Zero, 0u, 0u) > 0)
			{
				if (lpMsg2.hwnd == IntPtr.Zero)
				{
					if (lpMsg2.message == 32779)
					{
						CreatePendingNow();
					}
					else if (lpMsg2.message == 32780)
					{
						try
						{
							Native.DestroyWindow(lpMsg2.wParam);
						}
						catch (Exception ex)
						{
							Log.Error(ex);
						}
					}
				}
				else
				{
					Native.TranslateMessage(ref lpMsg2);
					Native.DispatchMessageW(ref lpMsg2);
				}
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		finally
		{
			_ownerReady.Set();
		}
	}

	private static void CreatePendingNow()
	{
		OverlayWindow[] array;
		lock (OwnerGate)
		{
			array = _pendingCreate.ToArray();
			_pendingCreate.Clear();
		}
		for (int i = 0; i < array.Length; i++)
		{
			try
			{
				array[i].CreateNow();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			finally
			{
				array[i]._created.Set();
			}
		}
	}

	private static void EnsureClass()
	{
		lock (ClassGate)
		{
			if (_className == null)
			{
				_classProc = OverlayWndProc;
				Native.WNDCLASSEX lpwcx = new Native.WNDCLASSEX
				{
					cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX)),
					style = 0u,
					lpfnWndProc = _classProc,
					hInstance = Native.GetModuleHandleW(null),
					hCursor = IntPtr.Zero,
					hbrBackground = IntPtr.Zero,
					lpszClassName = "HoldDragOverlayWindow"
				};
				Native.RegisterClassExW(ref lpwcx);
				_className = lpwcx.lpszClassName;
			}
		}
	}

	public void Destroy()
	{
		if (_destroyed)
		{
			return;
		}
		_destroyed = true;
		nint hwnd = _hwnd;
		_hwnd = IntPtr.Zero;
		if (hwnd != IntPtr.Zero)
		{
			lock (ClassGate)
			{
				Instances.Remove(hwnd);
			}
			if (Native.GetCurrentThreadId() == _ownerThreadId)
			{
				try
				{
					Native.DestroyWindow(hwnd);
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			}
			else
			{
				try
				{
					Native.PostThreadMessageW(_ownerThreadId, 32780u, hwnd, IntPtr.Zero);
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
				}
			}
		}
		try
		{
			FreeResources();
		}
		catch (Exception ex3)
		{
			Log.Error(ex3);
		}
	}

	public void Dispose()
	{
		Destroy();
		GC.SuppressFinalize(this);
	}

	protected virtual void FreeResources()
	{
	}

	protected virtual bool HandlePaint()
	{
		return false;
	}

	public void SetRect(Native.RECT r)
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		if (r.Width <= 0 || r.Height <= 0)
		{
			HideOverlay();
			return;
		}
		bool flag;
		lock (_pendingGate)
		{
			if ((_pendingValid && SameRect(_pending, r)) || (!_pendingValid && !_hidePending && _lastAppliedValid && SameRect(_lastApplied, r) && _visible))
			{
				return;
			}
			_pending = r;
			_pendingValid = true;
			_hidePending = false;
			flag = !_applyPosted;
			if (flag)
			{
				_applyPosted = true;
			}
		}
		if (!flag || Native.PostMessageW(_hwnd, 32769, IntPtr.Zero, IntPtr.Zero))
		{
			return;
		}
		lock (_pendingGate)
		{
			_applyPosted = false;
		}
	}

	private static bool SameRect(Native.RECT a, Native.RECT b)
	{
		if (a.Left == b.Left && a.Top == b.Top && a.Right == b.Right)
		{
			return a.Bottom == b.Bottom;
		}
		return false;
	}

	public void HideOverlay()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		bool flag;
		lock (_pendingGate)
		{
			_pendingValid = false;
			_hidePending = true;
			flag = !_applyPosted;
			if (flag)
			{
				_applyPosted = true;
			}
		}
		if (!flag || Native.PostMessageW(_hwnd, 32769, IntPtr.Zero, IntPtr.Zero))
		{
			return;
		}
		lock (_pendingGate)
		{
			_applyPosted = false;
		}
	}

	protected void RequestRetheme()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		_rethemePending = true;
		bool flag;
		lock (_pendingGate)
		{
			flag = !_rethemePosted;
			if (flag)
			{
				_rethemePosted = true;
			}
		}
		if (!flag || Native.PostMessageW(_hwnd, 32770, IntPtr.Zero, IntPtr.Zero))
		{
			return;
		}
		lock (_pendingGate)
		{
			_rethemePosted = false;
			_rethemePending = false;
		}
	}

	private void DrainRetheme()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		lock (_pendingGate)
		{
			_rethemePosted = false;
		}
		if (!_rethemePending)
		{
			return;
		}
		_rethemePending = false;
		try
		{
			OnRetheme();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	protected virtual void OnRetheme()
	{
	}

	private static nint OverlayWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
	{
		try
		{
			switch (msg)
			{
			case 132u:
				return new IntPtr(-1);
			case 33u:
				return new IntPtr(3);
			case 20u:
				return new IntPtr(1);
			default:
			{
				OverlayWindow value;
				lock (ClassGate)
				{
					if (!Instances.TryGetValue(hwnd, out value))
					{
						value = null;
					}
				}
				if (value != null)
				{
					switch (msg)
					{
					case 32769u:
						value.DrainApply();
						return IntPtr.Zero;
					case 32770u:
						value.DrainRetheme();
						return IntPtr.Zero;
					case 2u:
						return IntPtr.Zero;
					}
				}
				if (msg != 15)
				{
					break;
				}
				bool flag = false;
				if (value != null)
				{
					try
					{
						flag = value.HandlePaint();
					}
					catch (Exception ex)
					{
						Log.Error(ex);
					}
				}
				if (!flag)
				{
					try
					{
						Native.ValidateRect(hwnd, IntPtr.Zero);
					}
					catch
					{
					}
				}
				return IntPtr.Zero;
			}
			}
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
	}

	protected void MarkDirectWrite(Native.RECT r)
	{
		lock (_pendingGate)
		{
			_lastApplied = r;
			_lastAppliedValid = true;
			_pending = r;
			_pendingValid = true;
			_hidePending = false;
		}
		_visible = true;
	}

	protected void MarkDirectHide()
	{
		lock (_pendingGate)
		{
			_pendingValid = false;
			_hidePending = false;
			_lastAppliedValid = false;
		}
		_visible = false;
	}

	private void DrainApply()
	{
		if (_destroyed || _hwnd == IntPtr.Zero)
		{
			return;
		}
		bool pendingValid;
		Native.RECT pending;
		lock (_pendingGate)
		{
			_applyPosted = false;
			pendingValid = _pendingValid;
			pending = _pending;
			_pendingValid = false;
			_hidePending = false;
		}
		try
		{
			if (!pendingValid)
			{
				HideNow();
			}
			else
			{
				ApplyNow(pending);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	protected void HideNow()
	{
		lock (_pendingGate)
		{
			_lastAppliedValid = false;
		}
		_visible = false;
		try
		{
			if (_hwnd != IntPtr.Zero)
			{
				Native.ShowWindow(_hwnd, 0);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ApplyNow(Native.RECT r)
	{
		lock (_pendingGate)
		{
			_lastApplied = r;
			_lastAppliedValid = true;
		}
		if (r.Width != _lastW || r.Height != _lastH)
		{
			OnSizeChanged(r.Width, r.Height);
			_lastW = r.Width;
			_lastH = r.Height;
		}
		_visible = true;
		try
		{
			Native.ShowWindow(_hwnd, 4);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		OnPlace(r);
	}

	protected abstract void OnSizeChanged(int w, int h);

	protected abstract void OnPlace(Native.RECT r);

	protected static void MoveTopmost(nint hwnd, int x, int y, int w, int h)
	{
		Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 532u);
	}
}
internal abstract class LayeredBitmapOverlay : OverlayWindow
{
	private Bitmap _bmp;

	private nint _hBitmap = IntPtr.Zero;

	private nint _memDc = IntPtr.Zero;

	private nint _oldBmp = IntPtr.Zero;

	private int _bw;

	private int _bh;

	private int _curW;

	private int _curH;

	private readonly object _bufGate = new object();

	private int _pushInFlight;

	protected int _constantAlpha = 255;

	public bool RaiseAboveAll = true;

	private int _lastPlacedX;

	private int _lastPlacedY;

	private bool _lastPlacedValid;

	protected abstract void RenderContent(Graphics g, int w, int h);

	public void SetRectNow(Native.RECT r)
	{
		if (!IsCreated)
		{
			return;
		}
		if (r.Width < 4 || r.Height < 4)
		{
			HideNow();
			return;
		}
		bool isVisible = IsVisible;
		bool flag;
		lock (_bufGate)
		{
			int num;
			if (r.Width == _curW)
			{
				num = ((r.Height != _curH) ? 1 : 0);
				if (num == 0)
				{
					goto IL_007a;
				}
			}
			else
			{
				num = 1;
			}
			Rebuild(r.Width, r.Height);
			goto IL_007a;
			IL_007a:
			flag = (num != 0 || !isVisible) && _memDc != IntPtr.Zero && _hBitmap != IntPtr.Zero;
		}
		MarkDirectWrite(r);
		try
		{
			Native.ShowWindow(Handle, 4);
		}
		catch
		{
		}
		if (flag)
		{
			Push(r.Left, r.Top);
			return;
		}
		uint num2 = 1552u;
		nint hWndInsertAfter = IntPtr.Zero;
		if (RaiseAboveAll)
		{
			hWndInsertAfter = new IntPtr(-1);
		}
		else
		{
			num2 |= 4;
		}
		Native.SetWindowPos(Handle, hWndInsertAfter, r.Left, r.Top, r.Width, r.Height, num2);
	}

	public void HideNowSync()
	{
		if (IsCreated)
		{
			HideNow();
			MarkDirectHide();
		}
	}

	protected override void OnSizeChanged(int w, int h)
	{
		if (Handle == IntPtr.Zero || w <= 0 || h <= 0)
		{
			return;
		}
		lock (_bufGate)
		{
			_curW = w;
			_curH = h;
			Rebuild(w, h);
		}
	}

	protected void MarkContentDirty()
	{
		RequestRetheme();
	}

	protected override void OnRetheme()
	{
		if (_curW > 0 && _curH > 0)
		{
			Rebuild(_curW, _curH);
			if (_lastPlacedValid && _memDc != IntPtr.Zero && _hBitmap != IntPtr.Zero && IsVisible)
			{
				Push(_lastPlacedX, _lastPlacedY);
			}
		}
	}

	internal string ProbeBitmapPixelForTest(int x, int y)
	{
		try
		{
			lock (_bufGate)
			{
				if (_bmp == null)
				{
					return "无位图";
				}
				if (x < 0 || y < 0 || x >= _bmp.Width || y >= _bmp.Height)
				{
					return "越界";
				}
				Color pixel = _bmp.GetPixel(x, y);
				return pixel.R + "," + pixel.G + "," + pixel.B + ",a=" + pixel.A;
			}
		}
		catch (Exception ex)
		{
			return "异常 " + ex.GetType().Name;
		}
	}

	internal string ProbeOverlayStateForTest()
	{
		return "alpha=" + _constantAlpha + " 位图=" + _bw + "x" + _bh + " cur=" + _curW + "x" + _curH + " memDc=" + (_memDc != IntPtr.Zero) + " hBitmap=" + (_hBitmap != IntPtr.Zero) + " visible=" + IsVisible + " placed=" + (_lastPlacedValid ? (_lastPlacedX + "," + _lastPlacedY) : "无") + " 抬顶=" + RaiseAboveAll;
	}

	private void Rebuild(int w, int h)
	{
		lock (_bufGate)
		{
			ReleaseBitmap();
			Bitmap bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			try
			{
				using Graphics graphics = Graphics.FromImage(bitmap);
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.PixelOffsetMode = PixelOffsetMode.Half;
				graphics.Clear(Color.Transparent);
				RenderContent(graphics, w, h);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
				bitmap.Dispose();
				return;
			}
			_bmp = bitmap;
			_bw = w;
			_bh = h;
			_curW = w;
			_curH = h;
			if (_memDc == IntPtr.Zero)
			{
				nint dC = Native.GetDC(IntPtr.Zero);
				try
				{
					_memDc = Native.CreateCompatibleDC(dC);
				}
				finally
				{
					if (dC != IntPtr.Zero)
					{
						Native.ReleaseDC(IntPtr.Zero, dC);
					}
				}
			}
			if (_memDc != IntPtr.Zero)
			{
				nint zero = IntPtr.Zero;
				try
				{
					zero = bitmap.GetHbitmap(Color.FromArgb(0));
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
					return;
				}
				if (zero != IntPtr.Zero)
				{
					_hBitmap = zero;
					_oldBmp = Native.SelectObject(_memDc, zero);
				}
			}
		}
	}

	protected override void OnPlace(Native.RECT r)
	{
		_lastPlacedX = r.Left;
		_lastPlacedY = r.Top;
		_lastPlacedValid = true;
		if (_memDc != IntPtr.Zero && _hBitmap != IntPtr.Zero)
		{
			Push(r.Left, r.Top);
		}
	}

	private void Push(int x, int y)
	{
		nint memDc;
		int bw;
		int bh;
		lock (_bufGate)
		{
			if (_memDc == IntPtr.Zero || _hBitmap == IntPtr.Zero)
			{
				return;
			}
			memDc = _memDc;
			bw = _bw;
			bh = _bh;
			_pushInFlight++;
		}
		try
		{
			nint dC = Native.GetDC(IntPtr.Zero);
			if (dC == IntPtr.Zero)
			{
				return;
			}
			try
			{
				Native.POINT pptDst = new Native.POINT(x, y);
				Native.SIZE psize = new Native.SIZE(bw, bh);
				Native.POINT pptSrc = new Native.POINT(0, 0);
				Native.BLENDFUNCTION pblend = new Native.BLENDFUNCTION
				{
					BlendOp = 0,
					SourceConstantAlpha = (byte)_constantAlpha,
					AlphaFormat = 1
				};
				if (!Native.UpdateLayeredWindow(Handle, dC, ref pptDst, ref psize, memDc, ref pptSrc, 0u, ref pblend, 2u))
				{
					Log.Error("UpdateLayeredWindow 失败，Win32 错误码 " + Marshal.GetLastWin32Error());
				}
			}
			finally
			{
				Native.ReleaseDC(IntPtr.Zero, dC);
			}
			if (RaiseAboveAll)
			{
				Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 1043u);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			lock (_bufGate)
			{
				_pushInFlight--;
				if (_pushInFlight == 0)
				{
					Monitor.PulseAll(_bufGate);
				}
			}
		}
	}

	private void ReleaseBitmap()
	{
		lock (_bufGate)
		{
			if (_memDc != IntPtr.Zero && _oldBmp != IntPtr.Zero)
			{
				Native.SelectObject(_memDc, _oldBmp);
				_oldBmp = IntPtr.Zero;
			}
			if (_hBitmap != IntPtr.Zero)
			{
				Native.DeleteObject(_hBitmap);
				_hBitmap = IntPtr.Zero;
			}
			if (_bmp != null)
			{
				_bmp.Dispose();
				_bmp = null;
			}
			_bw = 0;
			_bh = 0;
		}
	}

	protected override void FreeResources()
	{
		lock (_bufGate)
		{
			while (_pushInFlight > 0)
			{
				Monitor.Wait(_bufGate);
			}
			ReleaseBitmap();
			if (_memDc != IntPtr.Zero)
			{
				Native.DeleteDC(_memDc);
				_memDc = IntPtr.Zero;
			}
		}
	}
}
internal sealed class ActivationFrame : LayeredBitmapOverlay
{
	private string _colorId = FrameColors.DefaultId;

	private string _styleId = FrameStyles.DefaultId;

	private int _thickness = 3;

	private int _radius = 13;

	public bool IsShown => IsVisible;

	internal string ColorIdForTest => _colorId;

	internal string StyleIdForTest => _styleId;

	public void ApplyTheme(string colorId, string styleId, int thickness, int radius, int opacityPercent = 100)
	{
		int num;
		if (opacityPercent < 5)
		{
			num = 5;
		}
		else
		{
			num = ((opacityPercent > 100) ? 255 : ((int)Math.Round((double)opacityPercent * 255.0 / 100.0)));
		}
		if (_colorId != colorId || _styleId != styleId || _thickness != thickness || _radius != radius || _constantAlpha != num)
		{
			_colorId = colorId;
			_styleId = styleId;
			_thickness = thickness;
			_radius = radius;
			_constantAlpha = num;
			MarkContentDirty();
		}
	}

	protected override void RenderContent(Graphics g, int w, int h)
	{
		RectangleF area = new RectangleF(0f, 0f, w, h);
		FrameStyles.Draw(g, area, FrameColors.Resolve(_colorId), _styleId, _thickness, _radius);
	}
}
