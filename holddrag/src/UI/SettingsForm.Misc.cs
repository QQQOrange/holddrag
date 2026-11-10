// HoldDrag · UI/SettingsForm.Misc.cs

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
	// ======================= 小值对象 =======================

	private sealed class ActionItem
	{
		public string Value;
		public string Text;

		public override string ToString()
		{
			return Text;
		}
	}

	private sealed class IdItem
	{
		public string Value;
		public string Text;

		public override string ToString()
		{
			return Text;
		}
	}

	private sealed class ColorItem
	{
		public string Value;
		public string Text;
		public Color Color;

		public override string ToString()
		{
			return Text;
		}
	}

	private static void PaintColorEntry(Graphics g, Rectangle bounds, object item)
	{
		try
		{
			if (!(item is ColorItem colorItem) || bounds.Width <= 0 || bounds.Height <= 0)
			{
				return;
			}
			HQ.Apply(g);
			int num = Math.Max(8, (int)Math.Round((double)bounds.Height * 0.52));
			if (num > bounds.Width / 3)
			{
				num = Math.Max(8, bounds.Width / 3);
			}
			int y = bounds.Top + (bounds.Height - num) / 2;
			Rectangle r = new Rectangle(bounds.Left, y, num, num);
			GraphicsPath graphicsPath = RoundedPath(r, Sc(3));
			try
			{
				using (SolidBrush brush = new SolidBrush(colorItem.Color))
				{
					g.FillPath(brush, graphicsPath);
				}
				using Pen pen = new Pen(UiTheme.InputStroke);
				g.DrawPath(pen, graphicsPath);
			}
			finally
			{
				graphicsPath.Dispose();
			}
			// 字体必须跟界面一致：SystemFonts.DefaultFont 是 9pt 的另一种字族，
			// 展开的下拉列表里文字会比收起状态小一号，而且不随 DPI 缩放。
			using Font font = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel);
			TextRenderer.DrawText(bounds: new Rectangle(r.Right + Sc(8), bounds.Top, Math.Max(10, bounds.Width - num - Sc(10)), bounds.Height), dc: g, text: colorItem.Text, font: font, foreColor: UiTheme.TextPrimary, flags: TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}


	// ======================= 按程序规则 =======================

	private void AddRuleFromWindow()
	{
		AddRuleAtCursor();
	}

	private void AddRuleAtCursor()
	{
		try
		{
			if (!Native.GetCursorPos(out var pt))
			{
				return;
			}
			AddRuleAt(pt);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>把 pt 处的窗口程序名 + 当前选中的规则类型追加到规则列表。</summary>
	private void AddRuleAt(Native.POINT pt)
	{
		try
		{
			nint num = WindowActions.FindTarget(pt, allowFullscreen: true);
			if (num == IntPtr.Zero)
			{
				_lblStatus.Text = Loc.T("没有找到窗口：把鼠标移到目标窗口上再点");
				_lblStatus.ForeColor = UiTheme.Danger;
				return;
			}
			string text = WindowActions.ProcessExeName(num);
			if (string.IsNullOrEmpty(text))
			{
				_lblStatus.Text = Loc.T("读不到这个窗口的程序名（权限更高？）");
				_lblStatus.ForeColor = UiTheme.Danger;
				return;
			}
			string text2 = ((_cmbRuleMode.SelectedItem is IdItem idItem) ? idItem.Value : AppRules.Ignore);
			List<string> list = new List<string>();
			string[] array = (_txtAppRules.Text ?? "").Split(new char[1] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string text3 in array)
			{
				string text4 = text3.Trim();
				if (text4.Length != 0 && !text4.StartsWith(text + "=", StringComparison.OrdinalIgnoreCase))
				{
					list.Add(text4);
				}
			}
			list.Add(text + "=" + text2);
			_txtAppRules.Text = string.Join(";", list);
			_lblStatus.Text = Loc.T("已添加：") + text + " → " + AppRules.DisplayName(text2);
			_lblStatus.ForeColor = UiTheme.Success;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	// ======================= 工作区布局 =======================

	private void RefreshLayoutCombo()
	{
		try
		{
			string text = ((_cmbLayouts.SelectedItem is IdItem idItem) ? idItem.Value : null);
			List<LayoutStore.Layout> list = LayoutStore.LoadAll();
			list.Sort((LayoutStore.Layout a, LayoutStore.Layout b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			_cmbLayouts.BeginUpdate();
			_cmbLayouts.Items.Clear();
			foreach (LayoutStore.Layout item in list)
			{
				_cmbLayouts.Items.Add(new IdItem
				{
					Value = item.Name,
					Text = item.Name + "  ·  " + item.Windows.Count + Loc.T(" 个窗口")
				});
			}
			_cmbLayouts.EndUpdate();
			if (_cmbLayouts.Items.Count > 0)
			{
				int num = 0;
				for (int i = 0; i < _cmbLayouts.Items.Count; i++)
				{
					if (_cmbLayouts.Items[i] is IdItem idItem2 && string.Equals(idItem2.Value, text, StringComparison.OrdinalIgnoreCase))
					{
						num = i;
						break;
					}
				}
				_cmbLayouts.SelectedIndex = num;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void SaveLayoutFromUi()
	{
		try
		{
			string text = AskText(Loc.T("保存布局"), Loc.T("布局 ") + DateTime.Now.ToString("MMdd-HHmm"));
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			LayoutStore.Layout layout = LayoutStore.Save(text);
			RefreshLayoutCombo();
			_lblStatus.Text = Loc.T("已保存布局：") + text + "（" + layout.Windows.Count + Loc.T(" 个窗口）");
			_lblStatus.ForeColor = UiTheme.Success;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			_lblStatus.Text = Loc.T("保存布局失败：") + ex.Message;
			_lblStatus.ForeColor = UiTheme.Danger;
		}
	}

	private void RestoreLayoutFromUi()
	{
		try
		{
			if (!(_cmbLayouts.SelectedItem is IdItem idItem))
			{
				_lblStatus.Text = Loc.T("还没有保存过布局");
				_lblStatus.ForeColor = UiTheme.Danger;
				return;
			}
			LayoutStore.Layout layout = LayoutStore.LoadAll().Find((LayoutStore.Layout l) => string.Equals(l.Name, idItem.Value, StringComparison.OrdinalIgnoreCase));
			if (layout == null)
			{
				_lblStatus.Text = Loc.T("找不到这个布局");
				_lblStatus.ForeColor = UiTheme.Danger;
				return;
			}
			int num = LayoutStore.Restore(layout);
			_lblStatus.Text = Loc.T("已恢复布局：") + layout.Name + "（" + num + " / " + layout.Windows.Count + Loc.T(" 个窗口）");
			_lblStatus.ForeColor = UiTheme.Success;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void DeleteLayoutFromUi()
	{
		try
		{
			if (!(_cmbLayouts.SelectedItem is IdItem idItem))
			{
				return;
			}
			LayoutStore.Delete(idItem.Value);
			RefreshLayoutCombo();
			_lblStatus.Text = Loc.T("已删除布局：") + idItem.Value;
			_lblStatus.ForeColor = UiTheme.Success;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>一个极简的输入框（Aurora 配色），用来问布局名字。</summary>
	private string AskText(string title, string initial)
	{
		using Form form = new Form
		{
			Text = title,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			StartPosition = FormStartPosition.CenterParent,
			ClientSize = new Size(Sc(380), Sc(140)),
			BackColor = UiTheme.Surface,
			ForeColor = UiTheme.TextPrimary,
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel),
			ShowInTaskbar = false,
			MaximizeBox = false,
			MinimizeBox = false
		};
		TextBox textBox = new TextBox
		{
			Bounds = new Rectangle(Sc(20), Sc(26), Sc(340), Sc(28)),
			Text = initial ?? "",
			BorderStyle = BorderStyle.FixedSingle,
			BackColor = UiTheme.FieldFill,
			ForeColor = UiTheme.TextPrimary,
			Font = new Font(DesignFontFamily, Sc(13), FontStyle.Regular, GraphicsUnit.Pixel)
		};
		FlatButton flatButton = new FlatButton
		{
			Text = Loc.T("确定"),
			Bounds = new Rectangle(Sc(200), Sc(84), Sc(74), Sc(34))
		};
		flatButton.Click += delegate
		{
			form.DialogResult = DialogResult.OK;
		};
		FlatButton flatButton2 = new FlatButton
		{
			Text = Loc.T("取消"),
			Bounds = new Rectangle(Sc(286), Sc(84), Sc(74), Sc(34))
		};
		flatButton2.Click += delegate
		{
			form.DialogResult = DialogResult.Cancel;
		};
		form.Controls.Add(textBox);
		form.Controls.Add(flatButton);
		form.Controls.Add(flatButton2);
		form.ActiveControl = textBox;
		textBox.SelectAll();
		if (form.ShowDialog(this) != DialogResult.OK)
		{
			return null;
		}
		return textBox.Text.Trim();
	}

	// ======================= 设置页搜索 =======================

	private void RunSearch(int direction)
	{
		try
		{
			string text = (_txtSearch.Text ?? "").Trim();
			if (text.Length == 0)
			{
				return;
			}
			if (!string.Equals(text, _searchQuery, StringComparison.Ordinal))
			{
				_searchQuery = text;
				_searchHits = FindHits(text);
				_searchIndex = -1;
			}
			if (_searchHits.Count == 0)
			{
				if (_lblStatus != null)
				{
					_lblStatus.Text = Loc.T("没有找到：") + text;
					_lblStatus.ForeColor = UiTheme.Danger;
				}
				return;
			}
			_searchIndex = (_searchIndex + direction + _searchHits.Count) % _searchHits.Count;
			SearchHit searchHit = _searchHits[_searchIndex];
			SelectPage(searchHit.Page);
			Page page = _pages[searchHit.Page];
			if (searchHit.Card != null && page.Host != null)
			{
				page.Host.Offset = Math.Max(0, searchHit.Card.Top - Sc(20));
			}
			if (_lblStatus != null)
			{
				_lblStatus.Text = Loc.T("「") + text + Loc.T("」 ") + (_searchIndex + 1) + "/" + _searchHits.Count + Loc.T("：") + searchHit.Label;
			}
			_lblStatus.ForeColor = UiTheme.TextSecondary;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private List<SearchHit> FindHits(string query)
	{
		List<SearchHit> list = new List<SearchHit>();
		try
		{
			for (int i = 0; i < _pages.Count; i++)
			{
				foreach (Card card in _pages[i].Cards)
				{
					if (card.Matches(query, out var label))
					{
						list.Add(new SearchHit
						{
							Page = i,
							Card = card,
							Label = label
						});
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return list;
	}

	/// <summary>把配置 + 最近日志 + 系统信息打成一个 zip，方便用户报障。</summary>
	private void ExportDiagnosticsBundle()
	{
		try
		{
			using SaveFileDialog saveFileDialog = new SaveFileDialog
			{
				Title = Loc.T("导出诊断包"),
				Filter = Loc.T("压缩包") + " (*.zip)|*.zip",
				FileName = "HoldDrag-诊断-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip",
				AddExtension = true,
				DefaultExt = "zip"
			};
			if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}
			string text = DiagnosticsBundle.Create(saveFileDialog.FileName, "当前页=" + _activePage + " 目标=" + _edit.TriggerKeyId);
			if (_lblStatus != null)
			{
				_lblStatus.Text = Loc.T("诊断包已导出：") + text;
				_lblStatus.ForeColor = UiTheme.Success;
			}
			try
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + text + "\"")
				{
					UseShellExecute = true
				});
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			if (_lblStatus != null)
			{
				_lblStatus.Text = Loc.T("导出失败：") + ex.Message;
				_lblStatus.ForeColor = UiTheme.Danger;
			}
		}
	}

	internal static int ProbeWheelSwallowed(FlatCombo c)
	{
		if (c is FlatCombo flatCombo)
		{
			return flatCombo.WheelSwallowedForTest;
		}
		return 0;
	}

	internal static int ProbeWheelForwarded(FlatCombo c)
	{
		if (c is FlatCombo flatCombo)
		{
			return flatCombo.WheelForwardedForTest;
		}
		return 0;
	}
}
