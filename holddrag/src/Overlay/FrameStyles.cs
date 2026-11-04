// HoldDrag · Overlay/FrameStyles.cs

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

internal static class FrameStyles
{
	public const string Dashed = "Dashed";

	public const string Hairline = "Hairline";

	public const string Focus = "Focus";

	public const string Glow = "Glow";

	public const string Shadow = "Shadow";

	// 精简后的样式表：#43 起只保留经得起看的 5 种。
	// 旧的 Flat / Solid / Double / Aurora / Neon / Halo / Corners / Underline / Brackets 全部删除
	// （用户反馈：丑、不明显、不实用），虚线环是唯一日常在用的，留作默认。
	public static readonly string[] All = new string[5] { "Dashed", "Hairline", "Focus", "Glow", "Shadow" };

	public static string DefaultId => "Dashed";

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
			"Dashed" => Loc.T("虚线环"), 
			"Hairline" => Loc.T("细线环"), 
			"Focus" => Loc.T("焦点环"), 
			"Glow" => Loc.T("柔光"), 
			"Shadow" => Loc.T("柔和投影"), 
			_ => id, 
		};
	}

	public static string HintFor(string id)
	{
		return id switch
		{
			"Dashed" => Loc.T("圆头短划、疏密跟着圆角走，日常最耐看"), 
			"Hairline" => Loc.T("一根细线，最不打扰界面"), 
			"Focus" => Loc.T("双层描边，最醒目，接近系统焦点框"), 
			"Glow" => Loc.T("细腻的多层衰减光晕，暗色窗口上尤其好看"), 
			"Shadow" => Loc.T("真实柔化投影，像窗口浮起来了一层"), 
			_ => string.Empty, 
		};
	}

	public static float StrokeFor(string style, int thickness)
	{
		if (thickness < 1)
		{
			thickness = 1;
		}
		switch (style)
		{
		case "Hairline":
			return Math.Max(1f, (float)thickness * 0.55f);
		case "Focus":
			return Math.Max(2f, (float)thickness * 1.15f);
		default:
			return thickness;
		}
	}

	public static int MarginFor(string style, int thickness)
	{
		// 外扩量必须 ≥ 笔画宽度，否则 Draw 里内缩 strokeW/2 之后描边外缘会落到浮层边界之外被裁掉。
		int num = (int)Math.Ceiling((double)StrokeFor(style, thickness));
		return style switch
		{
			"Glow" => num + 12, 
			"Shadow" => num + 17, 
			"Focus" => num + 7, 
			"Dashed" => num + 8, 
			_ => num + 4, 
		};
	}

	public static void Draw(Graphics g, RectangleF area, Color color, string style, int thickness, int radius, int marginOverride = -1)
	{
		float strokeW = StrokeFor(style, thickness);
		int num = ((marginOverride >= 0) ? marginOverride : MarginFor(style, thickness));
		float pathOffset = (float)num - strokeW * 0.5f;
		if (pathOffset < 0f)
		{
			pathOffset = 0f;
		}
		RectangleF ring = new RectangleF(area.X + pathOffset, area.Y + pathOffset, area.Width - pathOffset * 2f, area.Height - pathOffset * 2f);
		if (ring.Width < 6f || ring.Height < 6f)
		{
			return;
		}
		float r = ClampRadius(radius, ring.Width, ring.Height);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.PixelOffsetMode = PixelOffsetMode.Half;
		switch (style)
		{
		case "Shadow":
			DrawShadow(g, ring, r, color, strokeW);
			return;
		case "Glow":
			DrawGlow(g, ring, r, color, strokeW);
			return;
		case "Focus":
			DrawFocus(g, ring, r, color, strokeW);
			return;
		case "Hairline":
			DrawHairline(g, ring, r, color, strokeW);
			return;
		}
		DrawDashed(g, ring, r, color, strokeW);
	}

	// 虚线环：外两层极淡的光把虚线"托"起来，三层用同一套物理划长（DashPattern 以笔宽为单位，
	// 必须按各层笔宽换算），否则几层的划子对不齐，看着就是一团毛刺——旧实现正是这个毛病。
	private static void DrawDashed(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		float dashPx = Math.Max(t * 2.4f, Math.Min(Math.Min(ring.Width, ring.Height) * 0.055f, t * 6f));
		float gapPx = dashPx * 0.68f;
		for (int i = 2; i >= 1; i--)
		{
			float w = t + 2.2f * (float)i;
			using GraphicsPath path = RoundedPath(ring, r);
			using Pen pen = new Pen(Color.FromArgb((i == 2) ? 30 : 17, color), w);
			pen.LineJoin = LineJoin.Round;
			pen.DashStyle = DashStyle.Dash;
			pen.DashCap = DashCap.Round;
			pen.DashPattern = new float[2]
			{
				dashPx / w,
				gapPx / w
			};
			g.DrawPath(pen, path);
		}
		using (GraphicsPath path2 = RoundedPath(ring, r))
		{
			using Pen pen2 = new Pen(color, t);
			pen2.LineJoin = LineJoin.Round;
			pen2.DashStyle = DashStyle.Dash;
			pen2.DashCap = DashCap.Round;
			pen2.DashPattern = new float[2]
			{
				dashPx / t,
				gapPx / t
			};
			g.DrawPath(pen2, path2);
		}
	}

	// 细线环：一层几乎看不见的外扩压锯齿 + 一根实线，界面洁癖的选项。
	private static void DrawHairline(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		using (GraphicsPath path = RoundedPath(ring, r))
		{
			using Pen pen = new Pen(Color.FromArgb(26, color), t + 2.4f);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using (GraphicsPath path2 = RoundedPath(ring, r))
		{
			using Pen pen2 = new Pen(color, Math.Max(1f, t));
			pen2.LineJoin = LineJoin.Round;
			g.DrawPath(pen2, path2);
		}
	}

	// 焦点环：外圈实色 + 内圈一道提亮细线（中间留 2px 空隙），接近 Windows 11 的焦点框：
	// 远看干净、近看有层次，是"最醒目"的那一档。
	private static void DrawFocus(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		using (GraphicsPath path = RoundedPath(ring, r))
		{
			using Pen pen = new Pen(color, t);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		float inset = t * 0.5f + 2f;
		RectangleF inner = RectangleF.Inflate(ring, -inset, -inset);
		if (inner.Width > 4f && inner.Height > 4f)
		{
			float ir = Math.Max(0f, r - inset);
			using GraphicsPath path2 = RoundedPath(inner, ir);
			using Pen pen2 = new Pen(Color.FromArgb(132, Lighten(color, 0.58f)), Math.Max(1f, t * 0.34f));
			pen2.LineJoin = LineJoin.Round;
			g.DrawPath(pen2, path2);
		}
	}

	// 柔光：十几层 1.15px 递进+高斯衰减的描边叠出来，没有旧版那种"粗笔刷一圈一圈"的带状感。
	private static void DrawGlow(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		const int layers = 14;
		double sigma = (double)layers * 0.42;
		double twoSigmaSq = 2.0 * sigma * sigma;
		for (int i = layers; i >= 1; i--)
		{
			int alpha = (int)Math.Round(46.0 * Math.Exp(-(double)(i * i) / twoSigmaSq));
			if (alpha < 2)
			{
				continue;
			}
			using GraphicsPath path = RoundedPath(ring, r);
			using Pen pen = new Pen(Color.FromArgb(alpha, color), t + (float)i * 1.15f * 2f);
			pen.LineJoin = LineJoin.Round;
			g.DrawPath(pen, path);
		}
		using (GraphicsPath path2 = RoundedPath(ring, r))
		{
			using Pen pen2 = new Pen(color, t);
			pen2.LineJoin = LineJoin.Round;
			g.DrawPath(pen2, path2);
		}
	}

	// 柔和投影：走"距离场生成低分辨率阴影 + 双三次放大"，是真正的软阴影；
	// 旧版用 4 根几像素粗的半透明笔堆，边缘是一圈圈的硬台阶，所以在浅色窗口上看着脏。
	private static void DrawShadow(Graphics g, RectangleF ring, float r, Color color, float t)
	{
		// pad 必须 ≥ 投影可见半径，否则会在浮层窗口边缘被硬切出一条边
		int pad = (int)Math.Ceiling(t) + 16;
		int w = (int)Math.Ceiling(ring.Width) + pad * 2;
		int h = (int)Math.Ceiling(ring.Height) + pad * 2;
		if (w < 24 || h < 24 || w > 8192 || h > 8192)
		{
			return;
		}
		// 键必须无碰撞地覆盖全部入参：w/h 各 14 位、圆角 10 位、色值 32 位、pad 6 位，
		// 加起来超过 64 位，硬塞进一个 long 必然位域重叠（色值的低位会盖住 h/r），曾导致命中错误缓存。
		var key = (w, h, (int)Math.Round(r), color.ToArgb(), pad);
		lock (ShadowGate)
		{
			if (_shadowCache == null || _shadowKey != key)
			{
				try
				{
					_shadowCache?.Dispose();
				}
				catch
				{
				}
				_shadowCache = BuildShadowBitmap(w, h, (float)pad + r, pad, color);
				_shadowKey = key;
			}
			if (_shadowCache == null)
			{
				return;
			}
			int dy = Math.Max(2, (int)Math.Round((double)(t * 0.9f)));
			InterpolationMode interpolationMode = g.InterpolationMode;
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			try
			{
				g.DrawImage(_shadowCache, new Rectangle((int)Math.Floor((double)(ring.X - (float)pad)), (int)Math.Floor((double)(ring.Y - (float)pad + (float)dy)), w, h));
			}
			finally
			{
				g.InterpolationMode = interpolationMode;
			}
		}
		using GraphicsPath path = RoundedPath(ring, r);
		using Pen pen = new Pen(Color.FromArgb(226, color), Math.Max(1f, Math.Min(2.2f, t * 0.6f)));
		pen.LineJoin = LineJoin.Round;
		g.DrawPath(pen, path);
	}

	private static readonly object ShadowGate = new object();

	private static Bitmap _shadowCache;

	private static (int W, int H, int R, int Color, int Pad) _shadowKey;

	private static Bitmap BuildShadowBitmap(int w, int h, float radius, int pad, Color color)
	{
		try
		{
			const int scale = 4;
			int sw = Math.Max(8, w / scale);
			int sh = Math.Max(8, h / scale);
			Bitmap bitmap = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
			Rectangle rect = new Rectangle(0, 0, sw, sh);
			BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			try
			{
				float num = 1f / (float)scale;
				float num2 = radius * num;
				RectangleF rc = new RectangleF((float)pad * num, (float)pad * num, (float)sw - (float)pad * 2f * num, (float)sh - (float)pad * 2f * num);
				// 2.2 倍模糊半径处基本归零：让衰减正好在 pad 之内收干净
				float blur = Math.Max(1.6f, (float)pad * num / 2.2f);
				int r2 = (int)Math.Round((double)((float)(int)color.R * 0.42f));
				int g2 = (int)Math.Round((double)((float)(int)color.G * 0.42f));
				int b2 = (int)Math.Round((double)((float)(int)color.B * 0.42f));
				byte[] buffer = new byte[bitmapData.Stride * sh];
				for (int i = 0; i < sh; i++)
				{
					int num3 = i * bitmapData.Stride;
					for (int j = 0; j < sw; j++)
					{
						float num4 = SdRoundRect((float)j + 0.5f, (float)i + 0.5f, rc, num2);
						// 只在形状外侧画：内侧必须透明，否则等于给窗口蒙了一层灰（旧版投影"脏"的原因之一）
						if (!(num4 > 0f))
						{
							continue;
						}
						float num5 = num4 / blur;
						if (num5 >= 2.2f)
						{
							continue;
						}
						double num6 = Math.Exp((double)(-1.1f * num5 * num5));
						int num7 = (int)Math.Round(num6 * 96.0);
						if (num7 > 0)
						{
							int num8 = num3 + j * 4;
							buffer[num8] = (byte)b2;
							buffer[num8 + 1] = (byte)g2;
							buffer[num8 + 2] = (byte)r2;
							buffer[num8 + 3] = (byte)Math.Min(255, num7);
						}
					}
				}
				Marshal.Copy(buffer, 0, bitmapData.Scan0, buffer.Length);
			}
			finally
			{
				bitmap.UnlockBits(bitmapData);
			}
			return bitmap;
		}
		catch
		{
			return null;
		}
	}

	// 圆角矩形的有向距离：里面为负、边界为 0、外面为正。
	private static float SdRoundRect(float px, float py, RectangleF rc, float r)
	{
		float num = rc.Width * 0.5f;
		float num2 = rc.Height * 0.5f;
		float num3 = Math.Abs(px - (rc.X + num)) - (num - r);
		float num4 = Math.Abs(py - (rc.Y + num2)) - (num2 - r);
		float num5 = Math.Max(num3, 0f);
		float num6 = Math.Max(num4, 0f);
		return (float)Math.Sqrt((double)(num5 * num5 + num6 * num6)) + Math.Min(Math.Max(num3, num4), 0f) - r;
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
