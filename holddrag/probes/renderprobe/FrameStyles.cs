using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace HoldDrag;

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
