// HoldDrag · Overlay/FrameColors.cs

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

internal static class FrameColors
{
	internal struct Entry
	{
		public string Id;

		public string Name;

		public Color Color;
	}

	public const string Teal = "Teal";

	public const string Aqua = "Aqua";

	public const string Green = "Green";

	public const string Amber = "Amber";

	public const string Coral = "Coral";

	public const string Red = "Red";

	public const string Pink = "Pink";

	public const string Violet = "Violet";

	public const string Graphite = "Graphite";

	public const string Silver = "Silver";

	public const string Accent = "Accent";

	public const string Lime = "Lime";

	public const string Rose = "Rose";

	public const string Gold = "Gold";

	public const string Sky = "Sky";

	public const string Obsidian = "Obsidian";

	public const string Pearl = "Pearl";

	internal static readonly Entry[] All = new Entry[17]
	{
		new Entry
		{
			Id = "Teal",
			Name = "薄荷青",
			Color = Color.FromArgb(107, 196, 165)
		},
		new Entry
		{
			Id = "Aqua",
			Name = "湖水青",
			Color = Color.FromArgb(111, 200, 188)
		},
		new Entry
		{
			Id = "Sky",
			Name = "天青",
			Color = Color.FromArgb(100, 194, 214)
		},
		new Entry
		{
			Id = "Green",
			Name = "抹茶绿",
			Color = Color.FromArgb(116, 200, 148)
		},
		new Entry
		{
			Id = "Lime",
			Name = "嫩芽绿",
			Color = Color.FromArgb(168, 203, 106)
		},
		new Entry
		{
			Id = "Gold",
			Name = "暖金",
			Color = Color.FromArgb(214, 193, 106)
		},
		new Entry
		{
			Id = "Amber",
			Name = "蜜橙",
			Color = Color.FromArgb(221, 169, 92)
		},
		new Entry
		{
			Id = "Coral",
			Name = "珊瑚",
			Color = Color.FromArgb(232, 147, 118)
		},
		new Entry
		{
			Id = "Red",
			Name = "暖砖红",
			Color = Color.FromArgb(222, 133, 133)
		},
		new Entry
		{
			Id = "Rose",
			Name = "玫瑰",
			Color = Color.FromArgb(222, 133, 149)
		},
		new Entry
		{
			Id = "Pink",
			Name = "藕粉",
			Color = Color.FromArgb(222, 147, 188)
		},
		new Entry
		{
			Id = "Violet",
			Name = "藕荷",
			Color = Color.FromArgb(163, 142, 219)
		},
		new Entry
		{
			Id = "Graphite",
			Name = "石墨",
			Color = Color.FromArgb(107, 122, 143)
		},
		new Entry
		{
			Id = "Silver",
			Name = "银灰",
			Color = Color.FromArgb(168, 179, 193)
		},
		new Entry
		{
			Id = "Obsidian",
			Name = "曜石",
			Color = Color.FromArgb(58, 68, 83)
		},
		new Entry
		{
			Id = "Pearl",
			Name = "珍珠白",
			Color = Color.FromArgb(237, 241, 246)
		},
		new Entry
		{
			Id = "Accent",
			Name = "跟随系统强调色",
			Color = Color.FromArgb(0, 120, 212)
		}
	};

	private static Color _accentCache;

	private static long _accentAt;

	public static string DefaultId => "Teal";

	public static bool IsValid(string id)
	{
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i].Id == id)
			{
				return true;
			}
		}
		return false;
	}

	public static Color Resolve(string id)
	{
		if (id == "Accent")
		{
			long tickCount = Environment.TickCount64;
			if (_accentAt != 0L && tickCount - _accentAt < 2000)
			{
				return _accentCache;
			}
			try
			{
				Native.ColorAccent accent = Native.GetAccent();
				_accentCache = Color.FromArgb(accent.R, accent.G, accent.B);
			}
			catch
			{
				_accentCache = Color.FromArgb(0, 120, 212);
			}
			_accentAt = tickCount;
			return _accentCache;
		}
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i].Id == id)
			{
				return All[i].Color;
			}
		}
		return All[0].Color;
	}

	/// <summary>按「跟随系统强调色 &gt; 自定义颜色 &gt; 调色板」的优先级算最终颜色。</summary>
	public static Color ResolveEffective(string id, bool followAccent, int customArgb)
	{
		if (followAccent)
		{
			return Resolve("Accent");
		}
		if (customArgb != 0 && ((customArgb >> 24) & 0xFF) != 0)
		{
			return Color.FromArgb(customArgb);
		}
		return Resolve(id);
	}

	/// <summary>当前颜色的显示名（自定义颜色显示成 #RRGGBB）。</summary>
	public static string NameOf(string id, bool followAccent, int customArgb)
	{
		if (followAccent)
		{
			return Loc.T("跟随系统强调色");
		}
		if (customArgb != 0 && ((customArgb >> 24) & 0xFF) != 0)
		{
			Color color = Color.FromArgb(customArgb);
			return Loc.T("自定义 #") + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
		}
		for (int i = 0; i < All.Length; i++)
		{
			if (All[i].Id == id)
			{
				return All[i].Name;
			}
		}
		return id;
	}

	public static string HexOf(Color color)
	{
		return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
	}
}
