using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace HoldDrag;

// Minimal stubs so the extracted FrameStyles compiles standalone.
internal static class Native
{
	public struct RECT
	{
		public int Left, Top, Right, Bottom;
		public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
		public int Width => Right - Left;
		public int Height => Bottom - Top;
	}

	public struct POINT
	{
		public int X, Y;
		public POINT(int x, int y) { X = x; Y = y; }
	}

	public struct MONITORINFO
	{
		public RECT rcMonitor, rcWork;
	}

	public static MONITORINFO GetMonitorInfoAt(POINT pt)
	{
		var mi = new MONITORINFO();
		var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
		mi.rcMonitor = new RECT(vs.Left, vs.Top, vs.Right, vs.Bottom);
		mi.rcWork = mi.rcMonitor;
		return mi;
	}
}

internal static class Program
{
	// Real-world geometry from the user's log (config: FrameStyleId=Dashed, FrameThickness=8, FrameRadius=10)
	private const int WinW = 2635;   // visible width of the target window
	private const int WinH = 1636;   // visible height of the target window

	private static void Main()
	{
		string outDir = Path.Combine(AppContext.BaseDirectory, "out");
		Directory.CreateDirectory(outDir);

		string[] styles = { "Glow", "Dashed", "Solid", "Flat", "Neon", "Halo", "Corners", "Double", "Aurora", "Shadow", "Underline", "Brackets" };

		Console.WriteLine("style\tt\tmargin\ttotalAlpha\tmaxAlpha\tpx(a>8)\textent(outside)\textent(inside)\tpeakAt");
		foreach (string style in styles)
		{
			for (int t = 1; t <= 8; t++)
			{
				Measure(style, t, outDir, dumpImages: false);
			}
		}

		Console.WriteLine();
		Console.WriteLine("== 逐个样式：粗细 1..8 的可视变化 ==");
		foreach (string style in styles)
		{
			var rows = new string[8];
			for (int t = 1; t <= 8; t++)
			{
				Measure(style, t, outDir, dumpImages: false);
			}
		}

		// Dump PNG crops for the user's actual config (Dashed) at thickness 1 / 3 / 8
		foreach (int t in new[] { 1, 3, 8 })
		{
			Measure("Dashed", t, outDir, dumpImages: true);
		}
		foreach (int t in new[] { 1, 3, 8 })
		{
			Measure("Glow", t, outDir, dumpImages: true);
		}
	}

	private static void Measure(string style, int thickness, string outDir, bool dumpImages)
	{
		int margin = FrameStyles.MarginFor(style, thickness);
		int w = WinW + margin * 2;
		int h = WinH + margin * 2;
		int radius = 10;

		using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
		using (var g = Graphics.FromImage(bmp))
		{
			g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
			g.Clear(Color.Transparent);
			FrameStyles.Draw(g, new RectangleF(0f, 0f, w, h), Color.FromArgb(255, 91, 170, 226), style, thickness, radius);
		}

		// Alpha profile summed over the vertical middle band of the left edge.
		// The stroke path sits at x == margin (= the visible window edge).
		long totalAlpha = 0;
		int maxAlpha = 0;
		long pxAbove8 = 0;
		int peakAt = -1;
		int y0 = h / 2 - 200, y1 = h / 2 + 200;

		var profile = new long[w];
		for (int x = 0; x < w; x++)
		{
			for (int y = y0; y < y1; y++)
			{
				int a = bmp.GetPixel(x, y).A;
				profile[x] += a;
			}
		}
		for (int x = 0; x < w; x++)
		{
			for (int y = y0; y < y1; y++)
			{
				int a = bmp.GetPixel(x, y).A;
				totalAlpha += a;
				if (a > maxAlpha) maxAlpha = a;
				if (a > 8) pxAbove8++;
			}
		}
		for (int x = 0; x < w; x++)
		{
			if (profile[x] > (peakAt < 0 ? 0 : profile[peakAt])) peakAt = x;
		}

		// Extent: distance from the path (x == margin) where alpha drops below 8.
		int outerEdge = 0;   // smallest x with meaningful alpha
		int innerEdge = w - 1;
		long thr = (long)(y1 - y0);
		for (int x = 0; x < w; x++)
		{
			if (profile[x] > thr) { outerEdge = x; break; }
		}
		for (int x = w - 1; x >= 0; x--)
		{
			if (profile[x] > thr) { innerEdge = x; break; }
		}

		double outside = margin - outerEdge;      // how far the ink reaches outside the window edge
		double inside = innerEdge - margin;       // how far it reaches inside

		Console.WriteLine($"{style}\t{thickness}\t{margin}\t{totalAlpha}\t{maxAlpha}\t{pxAbove8}\t{outside:0.0}\t{inside:0.0}\t{peakAt}");

		if (dumpImages)
		{
			int cropW = margin * 2 + 60;
			int cropH = 160;
			int cx = Math.Max(0, margin - margin - 5);
			using var crop = new Bitmap(cropW, cropH);
			using (var cg = Graphics.FromImage(crop))
			{
				cg.DrawImage(bmp, new Rectangle(0, 0, cropW, cropH), new Rectangle(0, h / 2 - cropH / 2, cropW, cropH), GraphicsUnit.Pixel);
				cg.DrawLine(Pens.Lime, 0, cropH / 2f, cropW, cropH / 2f);
				// path location marker (x == margin)
				cg.DrawLine(Pens.Red, margin, 0, margin, cropH);
			}
			crop.Save(Path.Combine(outDir, $"crop_{style}_t{thickness}.png"), ImageFormat.Png);
		}
	}
}
