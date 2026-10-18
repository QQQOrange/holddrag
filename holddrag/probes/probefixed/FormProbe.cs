using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag;

// Probes the *settings page itself*: does changing 粗细 change what the user sees
// in the 样式 gallery (style tiles) and in the 实时预览 (live preview box)?
internal static class FormProbe
{
	private static string OutDir;

	[STAThread]
	private static void Main(string[] args)
	{
		Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
		Application.EnableVisualStyles();
		Console.OutputEncoding = System.Text.Encoding.UTF8;

		if (args != null && args.Length > 0 && args[0] == "click")
		{
			ClickScenario();
			return;
		}
		if (args != null && args.Length > 0 && args[0] == "click2")
		{
			ClickScenarioNoTarget();
			return;
		}
		if (args != null && args.Length > 0 && args[0] == "click3")
		{
			OwnWindowScenario();
			return;
		}
		if (args != null && args.Length > 0 && args[0] == "frame")
		{
			FrameInkScenario();
			return;
		}

		string dir = Path.Combine(Path.GetTempPath(), "hdp-form");
		Directory.CreateDirectory(dir);
		Log.Init(dir);
		OutDir = Path.Combine(dir, "shots");
		Directory.CreateDirectory(OutDir);
		Console.WriteLine("log = " + Log.Path);

		var store = new ConfigStore(dir);
		store.Load();
		using var coord = new Coordinator(store);
		using var form = new SettingsForm(store, coord);

		form.StartPosition = FormStartPosition.Manual;
		form.Location = new Point(60, 60);
		form.Show();
		Pump(1500);

		var pv = (Control)Field(form, "_pnlPreview");
		var gl = (Control)Field(form, "_galleryStyle");
		var numThick = (NumericUpDown)Field(form, "_numFrameThick");

		Console.WriteLine($"form      client={form.ClientSize}  bounds={form.Bounds}");
		Console.WriteLine($"preview   bounds={pv.Bounds}  screen={pv.RectangleToScreen(pv.ClientRectangle)}  visible={pv.Visible}");
		Console.WriteLine($"gallery   bounds={gl.Bounds}  screen={gl.RectangleToScreen(gl.ClientRectangle)}");
		Console.WriteLine($"厚度控件  bounds={numThick.Bounds} min={numThick.Minimum} max={numThick.Maximum} enabled={numThick.Enabled}");

		// Pick the style the user actually has configured, plus the default.
		form.ProbeSelectFrameStyle(store.Current.FrameStyleId);
		Pump(300);
		Console.WriteLine("style = " + store.Current.FrameStyleId);

		Bitmap prevPv = null, prevGl = null;
		foreach (int t in new[] { 1, 3, 4, 6, 8 })
		{
			form.ProbeSetFrameShape(t, 10, true);
			Pump(400);

			var pvShot = Shot(pv);
			var glShot = Shot(gl);
			var pvInk = Ink(pvShot, Color.FromArgb(244, 244, 245));
			var glInk = Ink(glShot, Color.White);

			string dPv = prevPv == null ? "-" : Diff(prevPv, pvShot).ToString();
			string dGl = prevGl == null ? "-" : Diff(prevGl, glShot).ToString();
			Console.WriteLine($"thickness={t,2}  preview: ink={pvInk,6} changedVsPrev={dPv,7}   gallery: ink={glInk,6} changedVsPrev={dGl,7}   (num.Value={numThick.Value})");

			pvShot.Save(Path.Combine(OutDir, $"preview_t{t}.png"), ImageFormat.Png);
			glShot.Save(Path.Combine(OutDir, $"gallery_t{t}.png"), ImageFormat.Png);
			prevPv = pvShot;
			prevGl = glShot;
		}

		Console.WriteLine("shots -> " + OutDir);
		form.Close();
		Pump(200);
	}

	// Reproduces issue #3 with the shipped Coordinator, driven by synthetic hook events:
	// the trigger key is held (not yet past the threshold) -> the left press is let through
	// -> once the threshold passes the app posts a synthetic button-up to that same window,
	// which turns the leaked press into a complete click on the window under the cursor.
	private static void ClickScenario()
	{
		string dir = Path.Combine(Path.GetTempPath(), "hdp-click");
		Directory.CreateDirectory(dir);
		Log.Init(dir);

		var store = new ConfigStore(dir);
		store.Load();
		var cfg = store.Current.Clone();
		cfg.RequireHold = true;
		cfg.HoldMs = 200;
		cfg.ActionLeft = "Move";
		cfg.ShowActivationFrame = false;
		store.Apply(cfg);

		using var coord = new Coordinator(store);

		using var target = new Form();
		target.FormBorderStyle = FormBorderStyle.Sizable;
		target.StartPosition = FormStartPosition.Manual;
		target.Bounds = new Rectangle(400, 300, 700, 500);
		target.Text = "click target";
		target.Show();
		Pump(400);

		WindowActions.AllowOwnProcessAsTargetForTest = true;
		WindowActions.ForcedTargetForTest = target.Handle;
		Coordinator.AssumeButtonsDownForTest = true;
		try
		{
			// 1) user presses and holds the trigger key (Enter = 0x0D)
			var kb = new Native.KBDLLHOOKSTRUCT();
			kb.vkCode = 0x0D;
			bool kbSwallowed = coord.OnKeyboard(down: true, ref kb);
			Console.WriteLine($"① 触发键按下：被钩子吞掉={kbSwallowed}  触发键已按住={coord.Trigger.Held}  已激活={coord.Trigger.Active}");

			// 2) user presses the left button while the threshold has not elapsed yet
			var down = new Native.MSLLHOOKSTRUCT();
			down.pt = new Native.POINT(700, 500);
			bool downSwallowed = coord.OnMouse(513, ref down);
			Console.WriteLine($"② 阈值未到时的左键按下：被吞掉={downSwallowed}  → 放行给下层窗口={!downSwallowed}");

			// 3) threshold elapses -> active; a mouse move makes the app adopt the held button
			coord.Trigger.ForceHoldElapsed();
			Console.WriteLine($"③ 阈值到：已激活={coord.Trigger.Active}");
			int phantomBefore = Coordinator.PhantomUpSentForTest;
			var move = new Native.MSLLHOOKSTRUCT();
			move.pt = new Native.POINT(700, 500);
			coord.OnMouse(512, ref move);
			Pump(300);

			Console.WriteLine($"④ 补起拖动：拖动中={coord.Drag.IsDragging}  向该窗口补发的 WM_LBUTTONUP 次数={Coordinator.PhantomUpSentForTest - phantomBefore}");

			// 4) release
			var up = new Native.MSLLHOOKSTRUCT();
			up.pt = new Native.POINT(700, 500);
			bool upSwallowed = coord.OnMouse(514, ref up);
			Console.WriteLine($"⑤ 左键抬起：被吞掉={upSwallowed}");
			Console.WriteLine();
			Console.WriteLine((Coordinator.PhantomUpSentForTest - phantomBefore == 0)
				? "结论：按下从未投递给下层窗口、也没有补发任何合成抬起 → 凑不成点击（已修复）。"
				: "结论：② 的真实按下已经到达下层窗口；④ 又补发了一个抬起事件 → 该窗口收到一对完整的 按下+抬起 = 一次真实点击。");
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
			WindowActions.ForcedTargetForTest = IntPtr.Zero;
			Coordinator.AssumeButtonsDownForTest = false;
			target.Hide();
		}
		Pump(200);
	}

	// Path B of issue #3: the trigger IS active, but no target window can be resolved
	// (own-process window such as the settings page, desktop/taskbar, blacklisted window).
	// Then BOTH the press and the release are let through -> the window under the cursor
	// receives a normal, complete click while the trigger is held.
	private static void ClickScenarioNoTarget()
	{
		string dir = Path.Combine(Path.GetTempPath(), "hdp-click2");
		Directory.CreateDirectory(dir);
		Log.Init(dir);

		var store = new ConfigStore(dir);
		store.Load();
		var cfg = store.Current.Clone();
		cfg.RequireHold = true;
		cfg.HoldMs = 200;
		cfg.ActionLeft = "Move";
		cfg.ShowActivationFrame = false;
		cfg.Blacklist = "click target";
		store.Apply(cfg);

		using var coord = new Coordinator(store);
		// a topmost, full-screen window of the OWN process: nothing but the desktop behind it,
		// exactly like the settings page sitting on top of the desktop
		using var target = new Form();
		target.FormBorderStyle = FormBorderStyle.Sizable;
		target.StartPosition = FormStartPosition.Manual;
		target.Bounds = SystemInformation.VirtualScreen;
		target.TopMost = true;
		target.Text = "click target";
		target.Show();
		Pump(500);

		var under = new Native.POINT(SystemInformation.VirtualScreen.Left + 700, SystemInformation.VirtualScreen.Top + 500);
		nint found = WindowActions.FindTarget(under, allowFullscreen: true);
		Console.WriteLine($"目标解析：FindTarget={(long)found}（0 = 找不到可接管的窗口）");

		var kb = new Native.KBDLLHOOKSTRUCT();
		kb.vkCode = 0x0D;
		coord.OnKeyboard(down: true, ref kb);
		coord.Trigger.ForceHoldElapsed();
		Console.WriteLine($"触发键：已激活={coord.Trigger.Active}");

		var down = new Native.MSLLHOOKSTRUCT();
		down.pt = under;
		bool downSwallowed = coord.OnMouse(513, ref down);
		var up = new Native.MSLLHOOKSTRUCT();
		up.pt = under;
		bool upSwallowed = coord.OnMouse(514, ref up);

		Console.WriteLine($"左键按下 被吞掉={downSwallowed} → 放行={!downSwallowed}");
		Console.WriteLine($"左键抬起 被吞掉={upSwallowed} → 放行={!upSwallowed}");
		Console.WriteLine((downSwallowed && upSwallowed)
			? "结论：按下与抬起都被吞掉 → 触发键按着时不再穿透点击（已修复）。"
			: "结论：按下或抬起被放行 → 触发键按着时仍会在下层窗口上完成一次普通点击。");
		target.Hide();
		Pump(200);
	}

	// 问题一的端到端复现：盖在桌面之上的【本进程窗口】（就是设置页的情形）。
	// 修复前 FindTarget 会跳过本进程窗口 -> 返回 0（或背后的第三方窗口）-> 设置页永远拖不动。
	// 修复后必须解析到它本身，并且真的能把它拖走。
	private static void OwnWindowScenario()
	{
		string dir = Path.Combine(Path.GetTempPath(), "hdp-click3");
		Directory.CreateDirectory(dir);
		Log.Init(dir);

		var store = new ConfigStore(dir);
		store.Load();
		var cfg = store.Current.Clone();
		cfg.RequireHold = false;
		cfg.ActionLeft = "Move";
		cfg.ShowActivationFrame = false;
		cfg.BringToFront = false;
		store.Apply(cfg);

		using var coord = new Coordinator(store);
		using var target = new Form();
		target.FormBorderStyle = FormBorderStyle.Sizable;
		target.StartPosition = FormStartPosition.Manual;
		target.Bounds = new Rectangle(SystemInformation.VirtualScreen.Left + 100, SystemInformation.VirtualScreen.Top + 100, 900, 650);
		target.TopMost = true;
		target.Text = "own settings-like window";
		target.Show();
		Pump(500);

		var p = new Native.POINT(target.Left + 300, target.Top + 200);
		Console.WriteLine($"本进程目标窗口句柄={(long)target.Handle}  光标点={p.X},{p.Y}");
		nint found = WindowActions.FindTarget(p, allowFullscreen: true);
		Console.WriteLine($"FindTarget={(long)found}  命中本进程窗口={found == target.Handle}");

		var kb = new Native.KBDLLHOOKSTRUCT();
		kb.vkCode = 0x0D;
		coord.OnKeyboard(down: true, ref kb);
		Console.WriteLine($"触发键：已激活={coord.Trigger.Active}");

		Native.GetWindowRect(target.Handle, out var before);
		var down = new Native.MSLLHOOKSTRUCT();
		down.pt = p;
		bool downSwallowed = coord.OnMouse(513, ref down);
		Console.WriteLine($"左键按下 被吞掉={downSwallowed}  拖动中={coord.Drag.IsDragging}");

		var move = new Native.MSLLHOOKSTRUCT();
		move.pt = new Native.POINT(p.X + 120, p.Y + 80);
		coord.OnMouse(512, ref move);
		Pump(400);
		Native.GetWindowRect(target.Handle, out var after);
		Console.WriteLine($"窗口位移 水平={after.Left - before.Left} 垂直={after.Top - before.Top}  （期望 120 / 80）");

		var up = new Native.MSLLHOOKSTRUCT();
		up.pt = move.pt;
		bool upSwallowed = coord.OnMouse(514, ref up);
		Console.WriteLine($"左键抬起 被吞掉={upSwallowed}");
		Console.WriteLine("结论：本进程的设置页这类窗口现在既【能被解析为拖动目标】，也【不会被穿透点击】。");
		target.Hide();
		Pump(200);
	}

	// 问题二的量化验证：真实外框（ActivationFrame）的"外露可见墨量"是否随粗细线性增长。
	// 外框窗口按生产几何摆放：窗口矩形外扩 FrameStyles.MarginFor(style, thickness)。
	private static void FrameInkScenario()
	{
		string dir = Path.Combine(Path.GetTempPath(), "hdp-frame");
		Directory.CreateDirectory(dir);
		Log.Init(dir);

		var frame = new ActivationFrame();
		frame.EnsureCreated();
		Console.WriteLine("外框窗口已创建=" + frame.IsCreated + "  句柄=" + (long)frame.Handle);

		var window = new Rectangle(SystemInformation.VirtualScreen.Left + 400, SystemInformation.VirtualScreen.Top + 300, 600, 400);
		var band = Rectangle.Inflate(window, 30, 30);

		frame.HideOverlay();
		Pump(400);
		using var baseline = CaptureScreen(band);

		foreach (string style in new[] { "Dashed", "Solid" })
		{
			foreach (int t in new[] { 1, 3, 6, 8 })
			{
				int m = FrameStyles.MarginFor(style, t);
				frame.ApplyTheme("Sky", style, t, 10, 100);
				frame.SetRect(new Native.RECT(window.Left - m, window.Top - m, window.Right + m, window.Bottom + m));
				Pump(400);

				using var shot = CaptureScreen(band);
				int ink = 0;
				for (int y = 0; y < band.Height; y++)
				{
					for (int x = 0; x < band.Width; x++)
					{
						var p = shot.GetPixel(x, y);
						var q = baseline.GetPixel(x, y);
						if (Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B))) <= 24) continue;
						ink++;
					}
				}
				int width = BandWidth(shot, baseline, band, window);
				for (int k = 0; k < 3 && width <= t; k++)
				{
					Pump(250);
					using var again = CaptureScreen(band);
					int w2 = BandWidth(again, baseline, band, window);
					if (w2 > width) width = w2;
				}
				Console.WriteLine($"{style} Sky 粗细={t,2}  外扩边距={m,3}px  外露可见墨量={ink,6}  外侧可见带宽={width}px  笔画宽={(style == "Solid" ? Math.Max(2.0, t * 1.8) : t):0.#}");
			}
		}
		frame.HideOverlay();
		Pump(200);
	}

	// 在窗口左边中段逐行扫描，取"从窗口边缘向外连续可见的描边长度"的最大值。
	// 逐行取最大可以规避虚线样式的间隙；多行取最大可以规避个别行被别的窗口内容恰好抵掉。
	private static int BandWidth(Bitmap shot, Bitmap baseline, Rectangle band, Rectangle window)
	{
		int best = 0;
		int y0 = window.Top + window.Height / 4;
		int y1 = window.Top + window.Height * 3 / 4;
		for (int sy = y0; sy <= y1; sy++)
		{
			int by = sy - band.Top;
			if (by < 0 || by >= band.Height) continue;
			int run = 0;
			for (int sx = window.Left - 1; sx >= band.Left; sx--)
			{
				int bx = sx - band.Left;
				if (bx < 0 || bx >= band.Width) break;
				var p = shot.GetPixel(bx, by);
				var q = baseline.GetPixel(bx, by);
				if (Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B))) <= 24) break;
				run++;
			}
			if (run > best) best = run;
		}
		return best;
	}

	private static Bitmap CaptureScreen(Rectangle r)
	{
		var bmp = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format32bppArgb);
		using var g = Graphics.FromImage(bmp);
		g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
		return bmp;
	}

	private static object Field(object o, string name)
	{
		var t = o.GetType();
		while (t != null)
		{
			var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (f != null) return f.GetValue(o);
			t = t.BaseType;
		}
		throw new Exception("field not found: " + name);
	}

	// deterministic render of a control through its own paint path
	private static Bitmap Shot(Control c)
	{
		var bmp = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height), PixelFormat.Format32bppArgb);
		c.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
		return bmp;
	}

	private static int Ink(Bitmap b, Color bg)
	{
		int n = 0;
		for (int y = 0; y < b.Height; y++)
			for (int x = 0; x < b.Width; x++)
			{
				var c = b.GetPixel(x, y);
				if (Math.Max(Math.Abs(c.R - bg.R), Math.Max(Math.Abs(c.G - bg.G), Math.Abs(c.B - bg.B))) > 30) n++;
			}
		return n;
	}

	private static int Diff(Bitmap a, Bitmap b)
	{
		int n = 0;
		int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				var p = a.GetPixel(x, y); var q = b.GetPixel(x, y);
				if (Math.Max(Math.Abs(p.R - q.R), Math.Max(Math.Abs(p.G - q.G), Math.Abs(p.B - q.B))) > 24) n++;
			}
		return n;
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
