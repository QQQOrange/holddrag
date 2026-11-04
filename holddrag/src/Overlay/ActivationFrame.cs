// HoldDrag · Overlay/ActivationFrame.cs

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

internal sealed class ActivationFrame : LayeredBitmapOverlay
{
	private string _colorId = FrameColors.DefaultId;

	private string _styleId = FrameStyles.DefaultId;

	private int _thickness = 3;

	private int _radius = 13;

	public bool IsShown => IsVisible;

	internal string ColorIdForTest => _colorId;

	internal string StyleIdForTest => _styleId;

	internal Color? ColorOverrideForTest => _colorOverride;

	private Color? _colorOverride;

	public void ApplyTheme(string colorId, string styleId, int thickness, int radius, int opacityPercent = 100, Color? colorOverride = null)
	{
		int num;
		if (opacityPercent < 5)
		{
			num = 5;
		}
		else
		{
			num = ((opacityPercent > 100) ? 255 : ((int)Math.Round((double)opacityPercent * 255.0 / 100.0)));
		}
		if (_colorId != colorId || _styleId != styleId || _thickness != thickness || _radius != radius || _constantAlpha != num || !SameColor(_colorOverride, colorOverride))
		{
			_colorId = colorId;
			_styleId = styleId;
			_thickness = thickness;
			_radius = radius;
			_constantAlpha = num;
			_colorOverride = colorOverride;
			MarkContentDirty();
		}
	}

	private static bool SameColor(Color? a, Color? b)
	{
		if (a.HasValue != b.HasValue)
		{
			return false;
		}
		return !a.HasValue || a.Value.ToArgb() == b.Value.ToArgb();
	}

	protected override void RenderContent(Graphics g, int w, int h)
	{
		RectangleF area = new RectangleF(0f, 0f, w, h);
		Color color = (_colorOverride.HasValue ? _colorOverride.Value : FrameColors.Resolve(_colorId));
		FrameStyles.Draw(g, area, color, _styleId, _thickness, _radius);
	}
}
