// HoldDrag · UI/SettingsForm.Probe.cs
//
// 探针面（自测 / 生产预览）：页面、搜索、颜色与样式的读写入口。

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
	/// <summary>自测用：直接点第 index 个色块，返回新选中的颜色 id。</summary>
	internal string ProbePickSwatch(int index)
	{
		_gridColor.ProbeClick(index);
		Application.DoEvents();
		return _gridColor.SelectedId;
	}
	/// <summary>自测用：色板第 index 格的颜色 id。</summary>
	internal string ProbeSwatchId(int index)
	{
		return _gridColor.ProbeIdAt(index);
	}
	/// <summary>自测用：跑一次搜索，返回命中数。</summary>
	internal int ProbeSearch(string query)
	{
		_txtSearch.Text = query;
		RunSearch(1);
		Application.DoEvents();
		return _searchHits.Count;
	}
	/// <summary>自测用：模拟"只往搜索框里打字，不按回车"。</summary>
	internal int ProbeTypeSearch(string text)
	{
		_txtSearch.Text = text ?? "";
		// TextChanged 里挂的是 250ms 防抖，这里直接触发一次定位，等价于用户停下不动 250ms
		Application.DoEvents();
		if (!string.IsNullOrEmpty(_txtSearch.Text))
		{
			RunSearch(1);
		}
		Application.DoEvents();
		return _searchHits.Count;
	}
	internal int ProbeActivePage() => _activePage;
	internal string ProbeSearchStatus()
	{
		return _lblStatus.Text;
	}
	/// <summary>自测用：页面上是否有这一行（卡片可见 + 行标题匹配）。</summary>
	internal bool ProbeControlVisible(string label)
	{
		foreach (Page page in _pages)
		{
			if (!page.Host.Visible)
			{
				continue;
			}
			foreach (Card card in page.Cards)
			{
				if (card.Visible && card.HasRow(label))
				{
					return true;
				}
			}
		}
		return false;
	}
	/// <summary>自测用：在当前颜色弹层里选第 index 项。</summary>
	internal bool ProbeChooseColorInPopup(int index)
	{
		return _cmbFrameColor.ProbeChooseInPopup(index);
	}
	/// <summary>自测用：打开外框颜色下拉框（截图 / 交互验证）。</summary>
	internal void ProbeOpenColorPopup()
	{
		try
		{
			_cmbFrameColor.ShowPopup();
			Application.DoEvents();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}
	internal void ProbeCloseColorPopup()
	{
		try
		{
			_cmbFrameColor.ClosePopup();
		}
		catch
		{
		}
	}
	internal Rectangle ProbeColorPopupBounds()
	{
		try
		{
			foreach (Form form in Application.OpenForms)
			{
				if (form is FlatComboPopup)
				{
					return form.Bounds;
				}
			}
		}
		catch
		{
		}
		return Rectangle.Empty;
	}
	internal FrameSelection ProbeFrameSelection()
	{
		return new FrameSelection
		{
			ColorId = SelectedId(_cmbFrameColor, "(null)"),
			StyleId = SelectedId(_cmbFrameStyle, "(null)"),
			Thickness = (int)_numFrameThick.Value,
			Radius = (int)_numFrameRadius.Value,
			FrameEnabled = _chkFrame.Checked,
			PreviewColorId = _pnlPreview.ColorId,
			PreviewStyleId = _pnlPreview.StyleId,
			PreviewThickness = _pnlPreview.Thickness,
			PreviewRadius = _pnlPreview.Radius,
			PreviewFrameEnabled = _pnlPreview.FrameEnabled,
			GridColorId = _gridColor.SelectedId,
			GridStyleId = _galleryStyle.SelectedId,
			ColorNameText = _lblColorName.Text,
			FollowAccent = _chkFollowAccent.Checked,
			CustomColorArgb = _customColorArgb,
			EffectiveColorHex = (_pnlPreview.ColorOverride.HasValue ? FrameColors.HexOf(_pnlPreview.ColorOverride.Value) : "")
		};
	}
	internal void ProbeSelectFrameColor(string id)
	{
		SelectPage(2);
		SelectId(_cmbFrameColor, id);
		SyncPreview();
	}
	internal void ProbeSetFollowAccent(bool on)
	{
		SelectPage(2);
		_chkFollowAccent.Checked = on;
		SyncFrameEnabled();
		SyncPreview();
	}
	internal void ProbeSetCustomColor(int argb)
	{
		SelectPage(2);
		_customColorArgb = argb;
		_chkFollowAccent.Checked = false;
		SyncFrameEnabled();
		SyncPreview();
	}
	internal void ProbeSelectFrameStyle(string id)
	{
		SelectPage(2);
		SelectId(_cmbFrameStyle, id);
		SyncPreview();
	}
	internal void ProbeSetFrameShape(int thickness, int radius, bool enabled)
	{
		SelectPage(2);
		_chkFrame.Checked = enabled;
		_numFrameThick.Value = Clamp(thickness, (int)_numFrameThick.Minimum, (int)_numFrameThick.Maximum);
		_numFrameRadius.Value = Clamp(radius, (int)_numFrameRadius.Minimum, (int)_numFrameRadius.Maximum);
		SyncPreview();
	}
	internal void ProbeAppendBlacklist(string kw)
	{
		AppendBlacklistKeyword(kw);
	}
	internal static bool ProbeForceOfflineForTest
	{
		get
		{
			return Updater.ForceOfflineForTest;
		}
		set
		{
			Updater.ForceOfflineForTest = value;
		}
	}
	internal static string[] IconGlyphsForTest
	{
		get
		{
			return new string[8] { Glyph.Keyboard, Glyph.Move, Glyph.Color, Glyph.Speed, Glyph.Shield, Glyph.Filter, Glyph.Help, Glyph.Info };
		}
	}
	internal static string LastStretchDetail = string.Empty;
}
