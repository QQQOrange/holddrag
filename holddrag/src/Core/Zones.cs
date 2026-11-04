// HoldDrag · Core/Zones.cs

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

internal enum ResizeZone
{
	None,
	TopLeft,
	Top,
	TopRight,
	Left,
	Center,
	Right,
	BottomLeft,
	Bottom,
	BottomRight
}
internal enum SnapZone
{
	None,
	LeftHalf,
	RightHalf,
	TopHalf,
	BottomHalf,
	TopLeftQuarter,
	TopRightQuarter,
	BottomLeftQuarter,
	BottomRightQuarter,
	Maximize
}
internal static class Zones
{
	public static ResizeZone Resize(Native.RECT r, int x, int y, int cornerPercent)
	{
		if (r.Width <= 0 || r.Height <= 0)
		{
			return ResizeZone.Center;
		}
		if (cornerPercent < 5)
		{
			cornerPercent = 5;
		}
		if (cornerPercent > 45)
		{
			cornerPercent = 45;
		}
		bool flag = x < r.Left + r.Width * cornerPercent / 100;
		bool flag2 = x > r.Right - r.Width * cornerPercent / 100;
		bool flag3 = y < r.Top + r.Height * cornerPercent / 100;
		bool flag4 = y > r.Bottom - r.Height * cornerPercent / 100;
		if (flag3 & flag)
		{
			return ResizeZone.TopLeft;
		}
		if (flag3 & flag2)
		{
			return ResizeZone.TopRight;
		}
		if (flag4 & flag)
		{
			return ResizeZone.BottomLeft;
		}
		if (flag4 & flag2)
		{
			return ResizeZone.BottomRight;
		}
		if (flag3)
		{
			return ResizeZone.Top;
		}
		if (flag4)
		{
			return ResizeZone.Bottom;
		}
		if (flag)
		{
			return ResizeZone.Left;
		}
		if (flag2)
		{
			return ResizeZone.Right;
		}
		return ResizeZone.Center;
	}

	public static ResizeZone NearestCorner(Native.RECT r, int x, int y)
	{
		bool flag = x < r.Left + r.Width / 2;
		if (y < r.Top + r.Height / 2)
		{
			if (!flag)
			{
				return ResizeZone.TopRight;
			}
			return ResizeZone.TopLeft;
		}
		if (!flag)
		{
			return ResizeZone.BottomRight;
		}
		return ResizeZone.BottomLeft;
	}

	public static void EdgesOf(ResizeZone z, out bool left, out bool top, out bool right, out bool bottom)
	{
		left = (top = (right = (bottom = false)));
		switch (z)
		{
		case ResizeZone.TopLeft:
			left = true;
			top = true;
			break;
		case ResizeZone.Top:
			top = true;
			break;
		case ResizeZone.TopRight:
			right = true;
			top = true;
			break;
		case ResizeZone.Left:
			left = true;
			break;
		case ResizeZone.Right:
			right = true;
			break;
		case ResizeZone.BottomLeft:
			left = true;
			bottom = true;
			break;
		case ResizeZone.Bottom:
			bottom = true;
			break;
		case ResizeZone.BottomRight:
			right = true;
			bottom = true;
			break;
		case ResizeZone.Center:
			right = true;
			bottom = true;
			break;
		}
	}

	public static Native.RECT RectOf(SnapZone zone, Native.RECT work)
	{
		int width = work.Width;
		int height = work.Height;
		int num = width / 2;
		int num2 = height / 2;
		return zone switch
		{
			SnapZone.LeftHalf => new Native.RECT(work.Left, work.Top, work.Left + num, work.Bottom), 
			SnapZone.RightHalf => new Native.RECT(work.Right - num, work.Top, work.Right, work.Bottom), 
			SnapZone.TopHalf => new Native.RECT(work.Left, work.Top, work.Right, work.Top + num2), 
			SnapZone.BottomHalf => new Native.RECT(work.Left, work.Bottom - num2, work.Right, work.Bottom), 
			SnapZone.TopLeftQuarter => new Native.RECT(work.Left, work.Top, work.Left + num, work.Top + num2), 
			SnapZone.TopRightQuarter => new Native.RECT(work.Right - num, work.Top, work.Right, work.Top + num2), 
			SnapZone.BottomLeftQuarter => new Native.RECT(work.Left, work.Bottom - num2, work.Left + num, work.Bottom), 
			SnapZone.BottomRightQuarter => new Native.RECT(work.Right - num, work.Bottom - num2, work.Right, work.Bottom), 
			SnapZone.Maximize => new Native.RECT(work.Left, work.Top, work.Right, work.Bottom), 
			_ => new Native.RECT(work.Left, work.Top, work.Right, work.Bottom), 
		};
	}

	public static SnapZone ScreenZoneOfResizeZone(ResizeZone z)
	{
		return z switch
		{
			ResizeZone.TopLeft => SnapZone.TopLeftQuarter, 
			ResizeZone.Top => SnapZone.TopHalf, 
			ResizeZone.TopRight => SnapZone.TopRightQuarter, 
			ResizeZone.Left => SnapZone.LeftHalf, 
			ResizeZone.Center => SnapZone.Maximize, 
			ResizeZone.Right => SnapZone.RightHalf, 
			ResizeZone.BottomLeft => SnapZone.BottomLeftQuarter, 
			ResizeZone.Bottom => SnapZone.BottomHalf, 
			ResizeZone.BottomRight => SnapZone.BottomRightQuarter, 
			_ => SnapZone.None, 
		};
	}

	public static void EnforceMinSize(ref Native.RECT r, int minWidth, int minHeight)
	{
		if (r.Width < minWidth && r.Left != r.Right)
		{
			r.Right = r.Left + minWidth;
		}
		if (r.Height < minHeight && r.Top != r.Bottom)
		{
			r.Bottom = r.Top + minHeight;
		}
	}
}
