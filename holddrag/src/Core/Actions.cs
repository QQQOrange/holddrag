// HoldDrag · Core/Actions.cs

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

internal static class Actions
{
	public const string Move = "Move";

	public const string Resize = "Resize";

	public const string Maximize = "Maximize";

	public const string Minimize = "Minimize";

	public const string Close = "Close";

	public const string Lower = "Lower";

	public const string AlwaysOnTop = "AlwaysOnTop";

	public const string Center = "Center";

	public const string Nothing = "Nothing";

	public static readonly string[] All = new string[9] { "Move", "Resize", "Maximize", "Minimize", "Close", "Lower", "AlwaysOnTop", "Center", "Nothing" };

	public static string DisplayName(string a)
	{
		return a switch
		{
			"Move" => Loc.T("移动窗口"), 
			"Resize" => Loc.T("调整大小"), 
			"Maximize" => Loc.T("最大化 / 还原"), 
			"Minimize" => Loc.T("最小化"), 
			"Close" => Loc.T("关闭窗口"), 
			"Lower" => Loc.T("置于最底层"), 
			"AlwaysOnTop" => Loc.T("置顶 / 取消置顶"), 
			"Center" => Loc.T("在屏幕上居中"), 
			"Nothing" => Loc.T("不绑定动作"), 
			_ => a, 
		};
	}

	public static bool IsValid(string a)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i] == a)
			{
				return true;
			}
		}
		return false;
	}

	public static bool IsDragAction(string a)
	{
		if (!(a == "Move"))
		{
			return a == "Resize";
		}
		return true;
	}
}
