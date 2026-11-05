// HoldDrag · Overlay/HintOverlay.cs
//
// 拖动时的两种轻提示，共用一个分层窗口（独立线程）：
//   1) 尺寸 HUD：外框角上浮一个小胶囊，显示 W × H（可选坐标）；
//   2) 吸附引导线：贴边时在吸附位置画一条 2px 的高亮线。
//
// 为什么单独一个窗口：外框本身只是一圈描边，塞不进文字；另开一个分层窗口
// 既不影响外框的绘制，也不会抢焦点（WS_EX_NOACTIVATE + WS_EX_TRANSPARENT）。

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag;

internal sealed class HintOverlay : IDisposable
{
	private const int WM_APPLY = 1024;

	private readonly object _gate = new object();

	private Thread _thread;

	private nint _hwnd;

	private uint _threadId;

	private bool _quit;

	private bool _dirty;

	private bool _visible;

	private bool _isLine;

	private bool _vertical;

	private int _linePos;

	private int _lineStart;

	private int _lineEnd;

	private string _text = "";

	private int _x;

	private int _y;

	private int _w;

	private int _h;

	private float _scale = 1f;

	private Color _accent = Color.FromArgb(145, 140, 255);

	private Native.WndProc _proc;

	private int _appliedGen;

	private int _wantGen;

	internal int ApplyCountForTest;

	internal bool VisibleForTest => _visible;

	public void Start()
	{
		lock (_gate)
		{
			if (_thread != null)
			{
				return;
			}
			_quit = false;
			_thread = new Thread(ThreadProc)
			{
				IsBackground = true,
				Name = "HoldDrag.HintOverlay"
			};
			_thread.SetApartmentState(ApartmentState.STA);
			_thread.Start();
		}
	}

	private void ThreadProc()
	{
		try
		{
			_threadId = Native.GetCurrentThreadId();
			_proc = WindowProc;
			Native.WNDCLASSEX lpwcx = new Native.WNDCLASSEX
			{
				cbSize = (uint)Marshal.SizeOf(typeof(Native.WNDCLASSEX)),
				style = 0u,
				lpfnWndProc = _proc,
				hInstance = Native.GetModuleHandleW(null),
				hCursor = IntPtr.Zero,
				hbrBackground = IntPtr.Zero,
				lpszClassName = "HoldDragHintOverlay"
			};
			Native.RegisterClassExW(ref lpwcx);
			// WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST
			_hwnd = Native.CreateWindowExW(134742024u, "HoldDragHintOverlay", "HoldDrag Hint", 2147483648u, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandleW(null), IntPtr.Zero);
			if (_hwnd == IntPtr.Zero)
			{
				Log.Error("HintOverlay 创建失败，Win32 错误码 " + Marshal.GetLastWin32Error());
				return;
			}
			Native.MSG mSG;
			while (!_quit && Native.GetMessageW(out mSG, IntPtr.Zero, 0u, 0u) > 0)
			{
				if (mSG.message == WM_APPLY)
				{
					ApplyNow();
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
	{
		if (msg == WM_APPLY)
		{
			ApplyNow();
			return IntPtr.Zero;
		}
		return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
	}

	private void Post()
	{
		try
		{
			if (_threadId != 0)
			{
				Native.PostThreadMessageW(_threadId, WM_APPLY, IntPtr.Zero, IntPtr.Zero);
			}
		}
		catch
		{
		}
	}

	/// <summary>显示尺寸胶囊。rect 是外框矩形（屏幕坐标），scale 是目标窗口的 DPI 缩放。</summary>
	public void ShowText(string text, Native.RECT rect, float scale)
	{
		lock (_gate)
		{
			if (_hwnd == IntPtr.Zero)
			{
				return;
			}
			_text = text ?? "";
			_isLine = false;
			_scale = (scale <= 0f) ? 1f : scale;
			MeasureAndPlace(rect);
			_visible = true;
			_wantGen++;
			_dirty = true;
		}
		Post();
	}

	/// <summary>显示吸附引导线（屏幕坐标）。</summary>
	public void ShowLine(bool vertical, int pos, int start, int end, Color color)
	{
		lock (_gate)
		{
			if (_hwnd == IntPtr.Zero)
			{
				return;
			}
			_isLine = true;
			_vertical = vertical;
			_linePos = pos;
			_lineStart = start;
			_lineEnd = end;
			_accent = color;
			_visible = true;
			_wantGen++;
			_dirty = true;
		}
		Post();
	}

	public void Hide()
	{
		lock (_gate)
		{
			if (!_visible)
			{
				return;
			}
			_visible = false;
			_wantGen++;
			_dirty = true;
		}
		Post();
	}

	private void MeasureAndPlace(Native.RECT frame)
	{
		float num = _scale;
		using Font font = new Font(SettingsForm.DesignFontFamily, 12f * num, FontStyle.Regular, GraphicsUnit.Pixel);
		Size size;
		using (Bitmap bitmap = new Bitmap(1, 1))
		{
			using Graphics graphics = Graphics.FromImage(bitmap);
			size = TextRenderer.MeasureText(graphics, _text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
		}
		int num2 = (int)Math.Round(16f * num);
		int num3 = (int)Math.Round(7f * num);
		_w = Math.Max(20, size.Width + num2 * 2);
		_h = Math.Max(18, size.Height + num3 * 2);
		int num4 = frame.Left + (frame.Width - _w) / 2;
		int num5 = frame.Top - _h - (int)Math.Round(10f * num);
		Rectangle workingArea = Screen.FromPoint(new Point(frame.Left + frame.Width / 2, frame.Top + frame.Height / 2)).WorkingArea;
		if (num5 < workingArea.Top + 4)
		{
			num5 = frame.Top + (int)Math.Round(10f * num);
		}
		if (num4 < workingArea.Left + 4)
		{
			num4 = workingArea.Left + 4;
		}
		if (num4 + _w > workingArea.Right - 4)
		{
			num4 = workingArea.Right - 4 - _w;
		}
		_x = num4;
		_y = num5;
	}

	private void ApplyNow()
	{
		try
		{
			bool visible;
			bool isLine;
			bool vertical;
			int linePos;
			int lineStart;
			int lineEnd;
			string text;
			int x;
			int y;
			int w;
			int h;
			float scale;
			Color accent;
			int gen;
			lock (_gate)
			{
				if (!_dirty)
				{
					return;
				}
				_dirty = false;
				visible = _visible;
				isLine = _isLine;
				vertical = _vertical;
				linePos = _linePos;
				lineStart = _lineStart;
				lineEnd = _lineEnd;
				text = _text;
				x = _x;
				y = _y;
				w = _w;
				h = _h;
				scale = _scale;
				accent = _accent;
				gen = _wantGen;
			}
			if (!visible)
			{
				if (_hwnd != IntPtr.Zero)
				{
					Native.ShowWindow(_hwnd, 0);
				}
				return;
			}
			using Bitmap bitmap = isLine ? RenderLine(vertical, linePos, lineStart, lineEnd, accent) : RenderText(text, w, h, scale);
			Push(bitmap, x, y);
			_appliedGen = gen;
			ApplyCountForTest++;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static Bitmap RenderText(string text, int w, int h, float scale)
	{
		Bitmap bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
		graphics.Clear(Color.Transparent);
		Rectangle rect = new Rectangle(0, 0, w - 1, h - 1);
		using (GraphicsPath path = Rounded(rect, (int)Math.Round(10f * scale)))
		{
			using SolidBrush brush = new SolidBrush(Color.FromArgb(238, 16, 17, 23));
			graphics.FillPath(brush, path);
			using Pen pen = new Pen(Color.FromArgb(64, 255, 255, 255));
			graphics.DrawPath(pen, path);
		}
		using Font font = new Font(SettingsForm.DesignFontFamily, 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
		TextRenderer.DrawText(graphics, text, font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		return bitmap;
	}

	private static Bitmap RenderLine(bool vertical, int pos, int start, int end, Color color)
	{
		int num = Math.Max(1, end - start);
		int num2 = 2;
		Bitmap bitmap = new Bitmap(vertical ? num2 : num, vertical ? num : num2, PixelFormat.Format32bppArgb);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.SmoothingMode = SmoothingMode.None;
		graphics.Clear(Color.Transparent);
		using SolidBrush brush = new SolidBrush(Color.FromArgb(210, color));
		if (vertical)
		{
			graphics.FillRectangle(brush, 0, 0, num2, num);
		}
		else
		{
			graphics.FillRectangle(brush, 0, 0, num, num2);
		}
		return bitmap;
	}

	private void Push(Bitmap bitmap, int x, int y)
	{
		int width = bitmap.Width;
		int height = bitmap.Height;
		nint dC = Native.GetDC(IntPtr.Zero);
		if (dC == IntPtr.Zero)
		{
			return;
		}
		nint num = Native.CreateCompatibleDC(dC);
		nint num2 = bitmap.GetHbitmap(Color.FromArgb(0));
		nint num3 = Native.SelectObject(num, num2);
		try
		{
			Native.POINT pptDst = new Native.POINT(x, y);
			Native.SIZE psize = new Native.SIZE(width, height);
			Native.POINT pptSrc = new Native.POINT(0, 0);
			Native.BLENDFUNCTION pblend = new Native.BLENDFUNCTION
			{
				BlendOp = 0,
				SourceConstantAlpha = 255,
				AlphaFormat = 1
			};
			if (!Native.UpdateLayeredWindow(_hwnd, dC, ref pptDst, ref psize, num, ref pptSrc, 0u, ref pblend, 2u))
			{
				Log.Error("HintOverlay.UpdateLayeredWindow 失败，错误码 " + Marshal.GetLastWin32Error());
			}
			Native.ShowWindow(_hwnd, 8);
		}
		finally
		{
			Native.SelectObject(num, num3);
			Native.DeleteObject(num2);
			Native.DeleteDC(num);
			Native.ReleaseDC(IntPtr.Zero, dC);
		}
	}

	private static GraphicsPath Rounded(Rectangle r, int radius)
	{
		GraphicsPath path = new GraphicsPath();
		int num = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
		if (num <= 0)
		{
			path.AddRectangle(r);
			return path;
		}
		path.AddArc(r.X, r.Y, num * 2, num * 2, 180f, 90f);
		path.AddArc(r.Right - num * 2, r.Y, num * 2, num * 2, 270f, 90f);
		path.AddArc(r.Right - num * 2, r.Bottom - num * 2, num * 2, num * 2, 0f, 90f);
		path.AddArc(r.X, r.Bottom - num * 2, num * 2, num * 2, 90f, 90f);
		path.CloseFigure();
		return path;
	}

	public void Dispose()
	{
		try
		{
			lock (_gate)
			{
				_quit = true;
			}
			if (_threadId != 0)
			{
				Native.PostThreadMessageW(_threadId, 18u, IntPtr.Zero, IntPtr.Zero);
			}
			if (_thread != null)
			{
				_thread.Join(800);
			}
			if (_hwnd != IntPtr.Zero)
			{
				Native.DestroyWindow(_hwnd);
				_hwnd = IntPtr.Zero;
			}
		}
		catch
		{
		}
	}
}
