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
				using Pen pen = new Pen(Tokens.InputStroke);
				g.DrawPath(pen, graphicsPath);
			}
			finally
			{
				graphicsPath.Dispose();
			}
			// 字体必须跟界面一致：SystemFonts.DefaultFont 是 9pt 的另一种字族，
			// 展开的下拉列表里文字会比收起状态小一号，而且不随 DPI 缩放。
			using Font font = new Font(DesignFontFamily, Sc(12), FontStyle.Regular, GraphicsUnit.Pixel);
			TextRenderer.DrawText(bounds: new Rectangle(r.Right + Sc(8), bounds.Top, Math.Max(10, bounds.Width - num - Sc(10)), bounds.Height), dc: g, text: colorItem.Text, font: font, foreColor: Tokens.TextPrimary, flags: TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	internal static int ProbeWheelSwallowed(ComboBox c)
	{
		if (c is FlatCombo flatCombo)
		{
			return flatCombo.WheelSwallowedForTest;
		}
		return 0;
	}

	internal static int ProbeWheelForwarded(ComboBox c)
	{
		if (c is FlatCombo flatCombo)
		{
			return flatCombo.WheelForwardedForTest;
		}
		return 0;
	}
}
