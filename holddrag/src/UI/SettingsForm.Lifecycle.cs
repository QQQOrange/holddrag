// HoldDrag · UI/SettingsForm.Lifecycle.cs

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

internal sealed partial class SettingsForm
{
	internal static string FormatDownloadProgress(long done, long total)
	{
		double num = (double)done / 1048576.0;
		if (total > 0)
		{
			double num2 = (double)total / 1048576.0;
			int num3 = (int)Math.Round((double)done * 100.0 / (double)total);
			// 整句走 Loc.T：以前这里硬编码中文，英文界面的下载窗口会整段中文
			return string.Format(Loc.T("正在下载… {0}%（{1:F1} / {2:F1} MB）"), num3, num, num2);
		}
		return string.Format(Loc.T("正在下载… 已接收 {0:F1} MB"), num);
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		if (_recording)
		{
			StopRecording(applied: false);
		}
		base.OnFormClosed(e);
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		// 只对齐外壳：内容重排由 _content.Resize 负责（改尺寸必然触发），
		// 这里再排一次等于同一帧干两遍，拖边框时最明显。
		LayoutChrome();
		UpdateMaximizeGlyph();
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		try
		{
			RescaleForDpi(Native.DpiOfWindow(Handle));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		// 无边框窗要自己跟 DWM 要圆角，否则就是一整块直角矩形（"都是直角，很丑"的成因）
		try
		{
			Native.SetWindowRoundedCorners(Handle);
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
	}

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			// WS_EX_COMPOSITED：整窗自底向上合成，父/子控件不会再「先擦后画」出现残影。
			// 自绘控件全部双缓冲之后仍有个别闪烁（滚动容器里的悬停），这一层是最后一道保险。
			createParams.ExStyle |= 0x02000000;
			return createParams;
		}
	}

	protected override void WndProc(ref Message m)
	{
		if (m.Msg == 131)
		{
			// WM_NCCALCSIZE：把系统标题栏整条还给客户区（边框留着，左右下三边照样能拖拽缩放）。
			// 这样"最上面那条"就是我们自己画的画布色，和页面严丝合缝，不存在两套颜色。
			try
			{
				if (m.WParam != IntPtr.Zero)
				{
					NCCALCSIZE_PARAMS nCCALCSIZE_PARAMS = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
					Native.RECT rECT;
					bool windowRect = Native.GetWindowRect(Handle, out rECT);
					if (windowRect && nCCALCSIZE_PARAMS.rgrc0.Top > rECT.Top)
					{
						nCCALCSIZE_PARAMS.rgrc0.Top = rECT.Top;
						Marshal.StructureToPtr(nCCALCSIZE_PARAMS, m.LParam, fDeleteOld: false);
					}
					m.Result = IntPtr.Zero;
					return;
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		else if (m.Msg == 132)
		{
			// WM_NCHITTEST：标题条上按"按住标题栏"处理 → 原生拖动 / 双击最大化 / 贴边分屏都能用。
			try
			{
				int x = (short)(m.LParam.ToInt64() & 0xFFFF);
				int y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
				Point pt = PointToClient(new Point(x, y));
				if (_titleStrip != null && WindowState != FormWindowState.Maximized && pt.X >= _titleStrip.Left && pt.X < _titleStrip.Right && pt.Y >= 0 && pt.Y < _titleStrip.Height)
				{
					m.Result = new IntPtr(2);
					return;
				}
			}
			catch (Exception ex2)
			{
				Log.Error(ex2);
			}
		}
		if (m.Msg == 736)
		{
			int dpi = (int)(short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
			try
			{
				RescaleForDpi(dpi);
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		else if (m.Msg == 26 || m.Msg == 794)
		{
			// WM_SETTINGCHANGE / WM_THEMECHANGED：系统切了白夜间（或换了主题色）时重读一次
			try
			{
				ReloadSystemTheme();
			}
			catch (Exception ex2)
			{
				Log.Error(ex2);
			}
		}
		base.WndProc(ref m);
	}

	// 系统主题真的变了才重建（重建会把没保存的改动先收回 _edit，不会丢）。
	private void ReloadSystemTheme()
	{
		bool dark = Theme.IsDark;
		Theme.Refresh();
		ApplyTitleBarTheme();
		if (Theme.IsDark != dark)
		{
			RebuildUi();
			return;
		}
		Invalidate(invalidateChildren: true);
	}

	private void ApplyTitleBarTheme()
	{
		try
		{
			if (IsHandleCreated)
			{
				// 用画布色当标题栏：和侧栏只差 2% 的明度，接缝看不出来
				Native.SetTitleBarTheme(Handle, Theme.IsDark, Tokens.Canvas, Tokens.TextPrimary, Tokens.Stroke);
			}
		}
		catch
		{
		}
	}

	private void RescaleForDpi(int dpi)
	{
		float num = (float)dpi / 96f;
		if (num < 0.5f)
		{
			num = 0.5f;
		}
		if (Math.Abs(num - UiScale) < 0.001f)
		{
			return;
		}
		UiScale = num;
		RebuildUi();
	}

	// DPI 变化后整体重建：所有控件按新系数重新创建，避免"字号放大了但控件尺寸没跟上"。
	private void RebuildUi()
	{
		if (_recording)
		{
			StopRecording(applied: false);
		}
		// 重建（换 DPI / 切系统主题）之前先把界面上的当前值收回 _edit，否则没保存的改动会被旧值覆盖。
		try
		{
			if (!_populating)
			{
				CaptureInto(_edit);
			}
		}
		catch
		{
		}
		int num = ((_activePage >= 0 && _activePage < _pages.Count) ? _activePage : 0);
		List<Control> list = new List<Control>();
		CollectTree(this, list);
		Controls.Clear();
		for (int i = 0; i < list.Count; i++)
		{
			try
			{
				list[i].Dispose();
			}
			catch
			{
			}
		}
		_pages.Clear();
		_nav = null;
		_content = null;
		_pager = null;
		_lblStatus = null;
		_btnDefaultsRef = null;
		_btnSaveRef = null;
		_btnCancelRef = null;
		BuildUi();
		Populate();
		SelectPage(num);
		ApplySavedSize();
		ApplyKeyboardAffordances();
		Invalidate(invalidateChildren: true);
	}

	private static void CollectTree(Control root, List<Control> sink)
	{
		for (int i = 0; i < root.Controls.Count; i++)
		{
			Control control = root.Controls[i];
			sink.Add(control);
			CollectTree(control, sink);
		}
	}

	private void ApplySavedSize()
	{
		try
		{
			if (_edit.SettingsWidth > 0 && _edit.SettingsHeight > 0)
			{
				ClientSize = new Size(_edit.SettingsWidth, _edit.SettingsHeight);
			}
			else
			{
				ClientSize = new Size(S(1024), _defaultClientH);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			ClientSize = new Size(S(1024), _defaultClientH);
		}
	}

	private void CaptureSizeInto(AppConfig target)
	{
		try
		{
			if (WindowState == FormWindowState.Normal)
			{
				target.SettingsWidth = ClientSize.Width;
				target.SettingsHeight = ClientSize.Height;
			}
		}
		catch
		{
		}
	}
}
