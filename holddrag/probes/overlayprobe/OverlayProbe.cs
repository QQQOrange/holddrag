using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag;

// End-to-end probe: does changing FrameThickness actually change what is painted
// on screen, through the very same path the app uses
// (ApplyTheme -> MarkContentDirty -> RequestRetheme -> OnRetheme -> Rebuild -> Push)?
internal static class OverlayProbe
{
	private static int VW, VH;

	[STAThread]
	private static void Main()
	{
		// Same DPI awareness the app requests, otherwise coordinates get virtualized.
		Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
		Application.EnableVisualStyles();
		Console.OutputEncoding = System.Text.Encoding.UTF8;

		string logDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hdp-probe");
		System.IO.Directory.CreateDirectory(logDir);
		Log.Init(logDir);
		Console.WriteLine("log = " + Log.Path + "");

		var vs = SystemInformation.VirtualScreen;
		VW = vs.Width; VH = vs.Height;
		Console.WriteLine($"virtual screen = {vs.Left},{vs.Top} {VW}x{VH}");

		using var frame = new ActivationFrame();
		frame.EnsureCreated();
		if (!frame.IsCreated)
		{
			Console.WriteLine("创建浮层失败");
			return;
		}
		frame.RaiseAboveAll = true;

		// A "visible window rect" the frame is asked to decorate (same numbers as the user's log).
		int winL = vs.Left + 400, winT = vs.Top + 300, winW = 1200, winH = 700;

		Console.WriteLine("mode\tstyle\tthickness\tmargin\tinkBandWidth(px)\tpeakDelta\tinkPixels");
		foreach (string style in new[] { "Dashed" })
		{
			foreach (int t in new[] { 1, 3, 6, 7, 8 })
			{
				ProbeHoverPath(frame, style, t, winL, winT, winW, winH);
			}
		}
		Console.WriteLine();
		Console.WriteLine("== 拖动路径（SetRectNow，每帧都会调） ==");
		Console.WriteLine("mode\tstyle\tthickness\tmargin\tinkBandWidth(px)\tpeakDelta\tinkPixels");
		foreach (string style in new[] { "Dashed" })
		{
			foreach (int t in new[] { 1, 3, 6, 7, 8 })
			{
				ProbeDragPath(frame, style, t, winL, winT, winW, winH);
			}
		}

		frame.HideOverlay();
		Pump(200);
	}

	// Hover path: ShowHoverFrame -> ShowFrame -> ApplyTheme + SetRect
	private static void ProbeHoverPath(ActivationFrame frame, string style, int t, int l, int tp, int w, int h)
	{
		frame.HideOverlay();
		Pump(150);
		var baseline = CaptureBand(l, tp, h);

		frame.ApplyTheme("Sky", style, t, 10, 100);
		int margin = FrameStyles.MarginFor(style, t);
		// hover path measures with _inset = 0 (no DWM inset applied here), so the frame rect
		// is the visible rect grown by the margin, exactly like FrameRectOf does.
		frame.SetRect(new Native.RECT(l - margin, tp - margin, l + w + margin, tp + h + margin));
		Pump(300);

		{ lastState = frame.ProbeOverlayStateForTest(); lastPixel = frame.ProbeBitmapPixelForTest(margin + 2, (h + margin * 2) / 2); }
		Report("hover", style, t, margin, l, tp, h, baseline);
	}

	// Drag path: every frame ApplyTheme + SetRectNow
	private static void ProbeDragPath(ActivationFrame frame, string style, int t, int l, int tp, int w, int h)
	{
		frame.HideOverlay();
		Pump(150);
		var baseline = CaptureBand(l, tp, h);

		int margin = FrameStyles.MarginFor(style, t);
		for (int i = 0; i < 3; i++)
		{
			frame.ApplyTheme("Sky", style, t, 10, 100);
			frame.SetRectNow(new Native.RECT(l - margin, tp - margin, l + w + margin, tp + h + margin));
			Pump(60);
		}
		Pump(200);
		{ lastState = frame.ProbeOverlayStateForTest(); lastPixel = frame.ProbeBitmapPixelForTest(margin + 2, (h + margin * 2) / 2); }
		Report("drag", style, t, margin, l, tp, h, baseline);
	}

	private static string lastState = "";
	private static string lastPixel = "";

	private static void Report(string mode, string style, int t, int margin, int l, int tp, int h, Color[][] baseline)
	{
		var now = CaptureBand(l, tp, h);
		int outerInk = 0, innerInk = 0, peak = 0, reach = 0;
		// x < 30 is the OUTSIDE of the window edge (that is the part the user can see
		// in the real app, because the target window covers the inner half).
		for (int y = 0; y < now.Length; y++)
		{
			for (int x = 0; x < now[y].Length; x++)
			{
				int d = Diff(now[y][x], baseline[y][x]);
				if (d > peak) peak = d;
				if (d > 24)
				{
					if (x < 30) { outerInk++; if (30 - x > reach) reach = 30 - x; }
					else innerInk++;
				}
			}
		}
		Console.WriteLine($"{mode}\t{style}\t{t}\t{margin}\t外露可见墨量={outerInk}\t外扩最大距离={reach}px\t内侧墨量={innerInk}\t峰值差={peak}");
		Console.WriteLine($"      state: {lastState}");
		Console.WriteLine($"      bitmap(margin+2, h/2): {lastPixel}");
	}

	private static int Diff(Color a, Color b)
	{
		return Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
	}

	// vertical strip of 60px straddling the left edge of the "window"
	private static Color[][] CaptureBand(int l, int tp, int h)
	{
		int x0 = Math.Max(0, l - 30);
		int wpx = 60;
		int y0 = tp + 60;
		int hpx = Math.Min(h - 120, 400);
		if (x0 + wpx > VW) wpx = VW - x0;
		if (y0 + hpx > VH) hpx = VH - y0;
		var res = new Color[hpx][];
		using var bmp = new Bitmap(wpx, hpx, PixelFormat.Format32bppArgb);
		using (var g = Graphics.FromImage(bmp))
		{
			g.CopyFromScreen(x0, y0, 0, 0, new Size(wpx, hpx));
		}
		for (int y = 0; y < hpx; y++)
		{
			res[y] = new Color[wpx];
			for (int x = 0; x < wpx; x++) res[y][x] = bmp.GetPixel(x, y);
		}
		return res;
	}

	private static void Pump(int ms)
	{
		long end = Environment.TickCount64 + ms;
		while (Environment.TickCount64 < end)
		{
			Application.DoEvents();
			Thread.Sleep(5);
		}
	}
}
