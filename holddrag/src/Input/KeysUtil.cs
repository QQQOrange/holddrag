// HoldDrag · Input/KeysUtil.cs

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

internal static class KeysUtil
{
	public const string LeftAlt = "K:LAlt";

	public const string RightAlt = "K:RAlt";

	public const string LeftCtrl = "K:LCtrl";

	public const string RightCtrl = "K:RCtrl";

	public const string LeftShift = "K:LShift";

	public const string RightShift = "K:RShift";

	public const string MouseX1 = "M:1";

	public const string MouseX2 = "M:2";

	public const string DefaultTrigger = "K:0x0D";

	private const uint VK_SHIFT = 16u;

	private const uint VK_CONTROL = 17u;

	private const uint VK_MENU = 18u;

	private const uint VK_LSHIFT = 160u;

	private const uint VK_RSHIFT = 161u;

	private const uint VK_LCONTROL = 162u;

	private const uint VK_RCONTROL = 163u;

	private const uint VK_LMENU = 164u;

	private const uint VK_RMENU = 165u;

	private const ushort SCAN_LSHIFT = 42;

	private const ushort SCAN_RSHIFT = 54;

	private static readonly Dictionary<int, string> Display;

	private static readonly Dictionary<int, string> SpecialDisplay;

	static KeysUtil()
	{
		Display = new Dictionary<int, string>
		{
			{ 13, "Enter 回车" },
			{ 32, "空格" },
			{ 9, "Tab" },
			{ 20, "CapsLock 大写锁定" },
			{ 93, "菜单键 (Applications)" },
			{ 8, "Backspace 退格" },
			{ 45, "Insert" },
			{ 46, "Delete" },
			{ 36, "Home" },
			{ 35, "End" },
			{ 33, "Page Up" },
			{ 34, "Page Down" },
			{ 37, "方向键 左" },
			{ 38, "方向键 上" },
			{ 39, "方向键 右" },
			{ 40, "方向键 下" },
			{ 27, "Esc" },
			{ 91, "左 Win" },
			{ 92, "右 Win" },
			{ 144, "NumLock" },
			{ 145, "ScrollLock" },
			{ 19, "Pause" },
			{ 44, "PrintScreen" },
			{ 192, "` ~" },
			{ 189, "- _" },
			{ 187, "= +" },
			{ 219, "[ {" },
			{ 221, "] }" },
			{ 220, "\\ |" },
			{ 186, "; :" },
			{ 222, "' \"" },
			{ 188, ", <" },
			{ 190, ". >" },
			{ 191, "/ ?" }
		};
		SpecialDisplay = new Dictionary<int, string>();
	}

	public static string CanonicalFromHook(Native.KBDLLHOOKSTRUCT kb)
	{
		uint vkCode = kb.vkCode;
		uint scanCode = kb.scanCode;
		bool flag = (kb.flags & 1) != 0;
		switch (vkCode)
		{
		case 16u:
			if (scanCode != 54)
			{
				return "K:LShift";
			}
			return "K:RShift";
		case 160u:
			return "K:LShift";
		case 161u:
			return "K:RShift";
		case 17u:
		case 162u:
		case 163u:
			if (!flag)
			{
				return "K:LCtrl";
			}
			return "K:RCtrl";
		case 18u:
		case 164u:
		case 165u:
			if (!flag)
			{
				return "K:LAlt";
			}
			return "K:RAlt";
		default:
			return "K:0x" + vkCode.ToString("X2");
		}
	}

	public static string CanonicalFromMouseHook(int msg, Native.MSLLHOOKSTRUCT ms)
	{
		if (msg != 523)
		{
			return null;
		}
		return (int)(ms.mouseData >> 16) switch
		{
			1 => "M:1", 
			2 => "M:2", 
			_ => null, 
		};
	}

	public static bool MatchesKeyboard(string id, Native.KBDLLHOOKSTRUCT kb)
	{
		if (string.IsNullOrEmpty(id) || id[0] != 'K')
		{
			return false;
		}
		uint vkCode = kb.vkCode;
		uint scanCode = kb.scanCode;
		bool flag = (kb.flags & 1) != 0;
		switch (id)
		{
		case "K:LShift":
			if (vkCode != 16 && vkCode != 160)
			{
				return false;
			}
			return scanCode != 54;
		case "K:RShift":
			if (vkCode == 16 || vkCode == 161)
			{
				return scanCode == 54;
			}
			return false;
		case "K:LCtrl":
			if (vkCode == 17 || vkCode == 162)
			{
				return !flag;
			}
			return false;
		case "K:RCtrl":
			return (vkCode == 17 || vkCode == 163) & flag;
		case "K:LAlt":
			if (vkCode == 18 || vkCode == 164)
			{
				return !flag;
			}
			return false;
		case "K:RAlt":
			return (vkCode == 18 || vkCode == 165) & flag;
		case "M:1":
		case "M:2":
			return false;
		default:
		{
			if (id.Length < 3 || id[1] != ':')
			{
				return false;
			}
			if (!TryParseHex(id.Substring(2), out var value))
			{
				return false;
			}
			if ((long)value == 16 && (vkCode == 16 || vkCode == 160 || vkCode == 161))
			{
				return true;
			}
			if ((long)value == 17 && (vkCode == 17 || vkCode == 162 || vkCode == 163))
			{
				return true;
			}
			if ((long)value == 18 && (vkCode == 18 || vkCode == 164 || vkCode == 165))
			{
				return true;
			}
			return vkCode == (uint)value;
		}
		}
	}

	public static bool MatchesMouse(string id, int msg, Native.MSLLHOOKSTRUCT ms)
	{
		if (id != "M:1" && id != "M:2")
		{
			return false;
		}
		bool flag = msg == 523;
		bool flag2 = msg == 524;
		if (!flag && !flag2)
		{
			return false;
		}
		int num = (int)(ms.mouseData >> 16);
		if (id == "M:1")
		{
			return num == 1;
		}
		return num == 2;
	}

	public static string FromWinFormsKey(Keys key)
	{
		switch (key)
		{
		case Keys.LShiftKey:
			return "K:LShift";
		case Keys.RShiftKey:
			return "K:RShift";
		case Keys.LControlKey:
			return "K:LCtrl";
		case Keys.RControlKey:
			return "K:RCtrl";
		case Keys.LMenu:
			return "K:LAlt";
		case Keys.RMenu:
			return "K:RAlt";
		case Keys.LWin:
			return "K:0x5B";
		case Keys.RWin:
			return "K:0x5C";
		case Keys.ShiftKey:
			return "K:LShift";
		case Keys.ControlKey:
			return "K:LCtrl";
		case Keys.Menu:
			return "K:LAlt";
		default:
		{
			int num = (int)(key & (Keys.F16 | Keys.F17));
			if (num == 0)
			{
				num = (int)key;
			}
			return "K:0x" + num.ToString("X2");
		}
		}
	}

	public static string FromMouseButton(MouseButtons b)
	{
		return b switch
		{
			MouseButtons.XButton1 => "M:1", 
			MouseButtons.XButton2 => "M:2", 
			_ => null, 
		};
	}

	public static int ToVirtualKey(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return 0;
		}
		switch (id)
		{
		case "K:LShift":
			return 160;
		case "K:RShift":
			return 161;
		case "K:LCtrl":
			return 162;
		case "K:RCtrl":
			return 163;
		case "K:LAlt":
			return 164;
		case "K:RAlt":
			return 165;
		case "M:1":
			return 5;
		case "M:2":
			return 6;
		default:
		{
			if (id.Length >= 3 && id[1] == ':' && TryParseHex(id.Substring(2), out var value))
			{
				return value;
			}
			return 0;
		}
		}
	}

	public static string DisplayName(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return Loc.T("（未设置）");
		}
		switch (id)
		{
		case "K:LAlt":
			return Loc.T("左 Alt");
		case "K:RAlt":
			return Loc.T("右 Alt（AltGr）");
		case "K:LCtrl":
			return Loc.T("左 Ctrl");
		case "K:RCtrl":
			return Loc.T("右 Ctrl");
		case "K:LShift":
			return Loc.T("左 Shift");
		case "K:RShift":
			return Loc.T("右 Shift");
		case "M:1":
			return Loc.T("鼠标侧键 前进 (X1)");
		case "M:2":
			return Loc.T("鼠标侧键 后退 (X2)");
		default:
		{
			if (id.Length >= 3 && id[1] == ':' && TryParseHex(id.Substring(2), out var value))
			{
				if (Display.TryGetValue(value, out var value2))
				{
					// 表里存的是中文原文，取用时才翻译：静态表只初始化一次，不能把语言烤进去
					return Loc.T(value2);
				}
				if (value >= 112 && value <= 135)
				{
					return "F" + (value - 111);
				}
				if (value >= 48 && value <= 57)
				{
					return Loc.T("数字 ") + (char)value;
				}
				if (value >= 65 && value <= 90)
				{
					return Loc.T("字母 ") + (char)value;
				}
				if (value >= 96 && value <= 105)
				{
					return Loc.T("小键盘 ") + (value - 96);
				}
				return value switch
				{
					106 => Loc.T("小键盘 *"), 
					107 => Loc.T("小键盘 +"), 
					109 => Loc.T("小键盘 -"), 
					110 => Loc.T("小键盘 ."), 
					111 => Loc.T("小键盘 /"), 
					_ => Loc.T("虚拟键 0x") + value.ToString("X2"), 
				};
			}
			return id;
		}
		}
	}

	public static bool IsToggleKey(string id)
	{
		if (id != null && id.Length >= 3 && id[1] == ':' && TryParseHex(id.Substring(2), out var value))
		{
			if (value != 20 && value != 144)
			{
				return value == 145;
			}
			return true;
		}
		return false;
	}

	public static bool IsWindowsKey(string id)
	{
		if (!(id == "K:0x5B"))
		{
			return id == "K:0x5C";
		}
		return true;
	}

	private static bool TryParseHex(string s, out int value)
	{
		value = 0;
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		try
		{
			value = Convert.ToInt32(s, 16);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
