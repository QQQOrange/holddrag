// HoldDrag · Overlay/LayeredBitmapOverlay.cs

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

	/// <summary>换缓冲区时"退役"的旧 HBITMAP：等没有在途的 UpdateLayeredWindow 再删。</summary>
	private readonly List<nint> _retiredBitmaps = new List<nint>();

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
		// 0x4000 = SWP_ASYNCWINDOWPOS：这个窗口由界面线程持有，而本方法可能跑在拖动线程上。
		// 同步 SetWindowPos 会跨线程等待界面线程抽消息，而界面线程此时可能正卡在拖动引擎的锁上 —— 互相等就是死锁。
		uint num2 = 17936u;
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
					return Loc.T("无位图");
				}
				if (x < 0 || y < 0 || x >= _bmp.Width || y >= _bmp.Height)
				{
					return Loc.T("越界");
				}
				Color pixel = _bmp.GetPixel(x, y);
				return pixel.R + "," + pixel.G + "," + pixel.B + ",a=" + pixel.A;
			}
		}
		catch (Exception ex)
		{
			return Loc.T("异常 ") + ex.GetType().Name;
		}
	}

	internal string ProbeOverlayStateForTest()
	{
		return "alpha=" + _constantAlpha + Loc.T(" 位图=") + _bw + "x" + _bh + " cur=" + _curW + "x" + _curH + " memDc=" + (_memDc != IntPtr.Zero) + " hBitmap=" + (_hBitmap != IntPtr.Zero) + " visible=" + IsVisible + " placed=" + (_lastPlacedValid ? (_lastPlacedX + "," + _lastPlacedY) : Loc.T("无")) + Loc.T(" 抬顶=") + RaiseAboveAll;
	}

	private void Rebuild(int w, int h)
	{
		lock (_bufGate)
		{
			// --------------------------------------------------------------
			// v1.3 性能：尺寸没变就【复用】托管 Bitmap，只重画内容。
			// 旧实现每次都：释放全部 → 新建 Bitmap → GetHbitmap()（一次完整像素
			// 拷贝）→ DeleteObject 旧句柄。纯移动（尺寸不变）是拖动中最常见情形，
			// 其中的托管 Bitmap 分配/GC 压力在这里省掉。
			// 注意：GetHbitmap 产出的 HBITMAP 是当时像素的【快照】，重画托管
			// Bitmap 不会同步过去——所以 HBITMAP 仍需重做（见下），但 Bitmap
			// 对象本身与 GC 压力省了，且免去了"先释放再全建"的窗口期。
			// --------------------------------------------------------------
			bool flag = _bmp != null && _bw == w && _bh == h;
			Bitmap bitmap;
			if (flag)
			{
				bitmap = _bmp;
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
					bitmap = null;
				}
			}
			else
			{
				bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
			}
			if (bitmap == null)
			{
				return;
			}
			if (!flag)
			{
				try
				{
					using Graphics graphics2 = Graphics.FromImage(bitmap);
					graphics2.SmoothingMode = SmoothingMode.AntiAlias;
					graphics2.PixelOffsetMode = PixelOffsetMode.Half;
					graphics2.Clear(Color.Transparent);
					RenderContent(graphics2, w, h);
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
					bitmap.Dispose();
					return;
				}
			}
			// HBITMAP 阶段：无论是否复用 Bitmap，快照都要按当前像素重做。
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
			if (_memDc == IntPtr.Zero)
			{
				if (!flag)
				{
					bitmap.Dispose();
				}
				return;
			}
			nint zero = IntPtr.Zero;
			try
			{
				zero = bitmap.GetHbitmap(Color.FromArgb(0));
			}
			catch (Exception ex3)
			{
				Log.Error(ex3);
				if (!flag)
				{
					bitmap.Dispose();
				}
				return;
			}
			if (zero == IntPtr.Zero)
			{
				return;
			}
			// 换新 HBITMAP：先把旧的从 DC 里换出来再删（顺序不能反，DC 里选着的不能删）。
			if (_memDc != IntPtr.Zero && _oldBmp != IntPtr.Zero)
			{
				Native.SelectObject(_memDc, _oldBmp);
				_oldBmp = IntPtr.Zero;
			}
			DrainRetiredLocked();
			if (_hBitmap != IntPtr.Zero)
			{
				// 仍有在途的 UpdateLayeredWindow 在引用这块位图时先"退休"，等它落地再删——
				// 直接 DeleteObject 会让 ULW 读到已释放的内存（错误日志 + 那一帧位置不更新）。
				RetireBitmapLocked(_hBitmap);
				_hBitmap = IntPtr.Zero;
			}
			if (!flag)
			{
				if (_bmp != null)
				{
					_bmp.Dispose();
				}
				_bmp = bitmap;
			}
			_hBitmap = zero;
			_bw = w;
			_bh = h;
			_curW = w;
			_curH = h;
			_oldBmp = Native.SelectObject(_memDc, zero);
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
				// 同上：异步置顶，避免跨线程同步等待
				Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, 17427u);
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
					// 最后一帧已经落地，之前退休的位图现在可以安全删掉了
					DrainRetiredLocked();
				}
			}
		}
	}

	/// <summary>把旧 HBITMAP 交出去删除：还有在途 ULW 时先挂起（见 <see cref="_retiredBitmaps"/>）。</summary>
	private void RetireBitmapLocked(nint hbitmap)
	{
		if (hbitmap == IntPtr.Zero)
		{
			return;
		}
		if (_pushInFlight > 0)
		{
			_retiredBitmaps.Add(hbitmap);
			return;
		}
		try
		{
			Native.DeleteObject(hbitmap);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>删除挂起的旧位图（调用方必须已持有 _bufGate）。</summary>
	private void DrainRetiredLocked()
	{
		if (_pushInFlight > 0 || _retiredBitmaps.Count == 0)
		{
			return;
		}
		for (int i = 0; i < _retiredBitmaps.Count; i++)
		{
			try
			{
				Native.DeleteObject(_retiredBitmaps[i]);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		_retiredBitmaps.Clear();
	}

	private void ReleaseBitmap()
	{
		lock (_bufGate)
		{
			DrainRetiredLocked();
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
			// v1.3：_curW/_curH 也必须归零。SetRectNow 靠"尺寸没变就跳过 Rebuild"省拷贝，
			// 若这里不归零，空闲释放后同尺寸的矩形会带着已删除的 HBITMARK 去 Push（黑框/崩）。
			_curW = 0;
			_curH = 0;
			// ApplyNow 用的是另一份尺寸缓存（_lastW/_lastH），同样必须失效：
			// 否则空闲释放缓冲区之后，遇到同尺寸窗口会误判"没变"而跳过重建，外框就停在上一个窗口上不动了。
			InvalidateSizeCache();
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

	/// <summary>
	/// v1.3：空闲时释放像素缓冲（托管 Bitmap + 非托管 HBITMAP），但保留 DC 与窗口句柄。
	/// 下一次显示会经 Rebuild 重建缓冲；DC 不释放可以避免"频繁拖动时反复建 DC"的开销。
	/// 必须先等在途的 Push 完成再放（UpdateLayeredWindow 还拿着那块内存）。
	/// </summary>
	internal void ReleaseBuffersForIdle()
	{
		try
		{
			HideNowSync();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		lock (_bufGate)
		{
			while (_pushInFlight > 0)
			{
				try
				{
					Monitor.Wait(_bufGate);
				}
				catch
				{
					break;
				}
			}
			ReleaseBitmap();
		}
	}
}
