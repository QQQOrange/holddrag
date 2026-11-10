// HoldDrag · UI/UiTheme.cs
//
// 全局配色与「深浅色判定」的唯一真源。
//
// 历史包袱：这套颜色原来有两份 —— SettingsForm 里私有的 Tokens（32 个颜色，设置窗用），
// 和给托盘菜单 / 更新弹窗用的 AuroraTheme（13 个颜色的公开镜像）。两份靠手工同步，
// 改一处忘另一处就会「设置页和托盘不是一套配色」。现在两边都指向这里。
//
// 约定：P(浅色, 深色) —— 取哪一个由 IsDark 决定。

using System;
using System.Drawing;
using Microsoft.Win32;

namespace HoldDrag;

internal static class UiTheme
{
	private static bool _dark;

	private static bool _probed;

	private static bool? _forced;

	/// <summary>当前是否深色主题（跟随系统，可用 SetForceDark 强制）。</summary>
	public static bool IsDark
	{
		get
		{
			if (!_probed)
			{
				Refresh();
			}
			return _dark;
		}
	}

	/// <summary>重新探测系统主题；返回「和上次比是否变了」。</summary>
	public static bool Refresh()
	{
		bool dark = DetectDark();
		bool changed = _probed && dark != _dark;
		_dark = dark;
		_probed = true;
		return changed;
	}

	/// <summary>截图 / 自检用：强制按某个主题渲染（null = 回到跟随系统）。</summary>
	public static void SetForceDark(bool? dark)
	{
		_forced = dark;
		_probed = false;
	}

	public static bool DetectDark()
	{
		if (_forced.HasValue)
		{
			return _forced.Value;
		}
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			if (registryKey != null && registryKey.GetValue("AppsUseLightTheme") is int num)
			{
				return num == 0;
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return false;
	}

	internal static Color P(Color light, Color dark)
	{
		return IsDark ? dark : light;
	}


		// ——— v1.11「Aurora」调色板 ———
		// 底色从偏蓝的冷灰换成中性带一点紫的暖灰，卡片纯白分层；
		// 主色从"老蓝" #2B6CF6 换成靛紫 #5B5BD6（暗色下提亮到 #918CFF），
		// 灰阶拉开 3 级（主文字 / 次文字 / 提示），保证层级一眼分得清。

		public static Color Canvas => P(Color.FromArgb(247, 247, 251), Color.FromArgb(11, 12, 16));

		public static Color Sidebar => P(Color.FromArgb(252, 252, 254), Color.FromArgb(15, 16, 20));

		public static Color Surface => P(Color.FromArgb(255, 255, 255), Color.FromArgb(21, 22, 27));

		/// <summary>再凹一层的底色（预览框内衬、样式缩略图、侧栏状态卡）。</summary>
		public static Color SurfaceAlt => P(Color.FromArgb(249, 249, 253), Color.FromArgb(18, 19, 24));

		public static Color Stroke => P(Color.FromArgb(233, 233, 241), Color.FromArgb(36, 38, 46));

		public static Color StrokeStrong => P(Color.FromArgb(218, 218, 231), Color.FromArgb(47, 50, 60));

		public static Color Divider => P(Color.FromArgb(240, 240, 246), Color.FromArgb(30, 32, 39));

		/// <summary>悬停底色（导航项、列表行、次要按钮）。</summary>
		public static Color Hover => P(Color.FromArgb(242, 242, 249), Color.FromArgb(27, 29, 36));

		public static Color TextPrimary => P(Color.FromArgb(20, 21, 27), Color.FromArgb(239, 240, 245));

		public static Color TextSecondary => P(Color.FromArgb(91, 95, 112), Color.FromArgb(165, 169, 186));

		public static Color TextTertiary => P(Color.FromArgb(142, 146, 165), Color.FromArgb(112, 116, 137));

		public static Color Accent => P(Color.FromArgb(91, 91, 214), Color.FromArgb(145, 140, 255));

		public static Color AccentPressed => P(Color.FromArgb(74, 74, 194), Color.FromArgb(168, 163, 255));

		public static Color AccentSoft => P(Color.FromArgb(239, 237, 254), Color.FromArgb(26, 25, 48));

		public static Color AccentSoftStroke => P(Color.FromArgb(221, 217, 252), Color.FromArgb(43, 41, 80));

		public static Color InputStroke => P(Color.FromArgb(223, 223, 235), Color.FromArgb(46, 49, 64));

		public static Color InputStrokeHover => P(Color.FromArgb(203, 203, 221), Color.FromArgb(59, 62, 74));

		public static Color FieldFill => P(Color.FromArgb(246, 246, 250), Color.FromArgb(29, 30, 38));

		public static Color FieldStroke => P(Color.FromArgb(223, 223, 235), Color.FromArgb(46, 49, 64));

		/// <summary>录制按键时输入框的底色。暗色主题不能用浅黄（浅底 + 浅字等于看不见）。</summary>
		public static Color FieldRecording => P(Color.FromArgb(255, 246, 222), Color.FromArgb(58, 48, 26));

		/// <summary>录制按键时输入框上的文字色，与 <see cref="FieldRecording"/> 成对使用。</summary>
		public static Color FieldRecordingText => P(Color.FromArgb(20, 21, 27), Color.FromArgb(247, 236, 196));

		/// <summary>禁用态统一底色（下拉框、数字框、文本框、自绘控件都用它）。</summary>
		public static Color DisabledFill => P(Color.FromArgb(247, 247, 251), Color.FromArgb(24, 25, 31));

		/// <summary>禁用态统一描边。</summary>
		public static Color DisabledStroke => P(Color.FromArgb(236, 236, 243), Color.FromArgb(36, 38, 46));

		/// <summary>禁用态文字。</summary>
		public static Color DisabledText => P(Color.FromArgb(160, 164, 180), Color.FromArgb(104, 108, 124));

		public static Color Hint => TextTertiary;

		public static Color WarnText => P(Color.FromArgb(180, 83, 9), Color.FromArgb(251, 191, 36));

		public static Color Success => P(Color.FromArgb(22, 163, 74), Color.FromArgb(74, 222, 128));

		public static Color Danger => P(Color.FromArgb(220, 38, 38), Color.FromArgb(248, 113, 113));

		public static Color TrackOff => P(Color.FromArgb(220, 220, 232), Color.FromArgb(52, 54, 63));

		public static Color TrackOffHover => P(Color.FromArgb(201, 201, 218), Color.FromArgb(67, 70, 79));

		public static Color ButtonHover => P(Color.FromArgb(243, 243, 250), Color.FromArgb(29, 31, 39));

		/// <summary>强调色底上的文字（暗色主题的强调色偏亮，用深色字更清楚）。</summary>
		public static Color TextOnAccent => P(Color.FromArgb(255, 255, 255), Color.FromArgb(18, 16, 31));

		/// <summary>开关滑块（暗色主题不用纯白，避免刺眼）。</summary>
		public static Color SwitchKnob => P(Color.FromArgb(255, 255, 255), Color.FromArgb(244, 244, 248));

		public static Color CardShadow => P(Color.FromArgb(9, 17, 24, 39), Color.FromArgb(72, 0, 0, 0));

		/// <summary>卡片圆角。</summary>
		public const int Radius = 16;

		/// <summary>控件圆角（输入框、按钮、色块、导航胶囊）。</summary>
		public const int RadiusField = 10;

		/// <summary>小圆角（缩略图、色块内圈、标签）。</summary>
		public const int RadiusChip = 8;

		/// <summary>胶囊圆角（开关轨道、状态点）。</summary>
		public const int RadiusPill = 999;

		/// <summary>侧栏宽度。</summary>
		public const int SidebarW = 232;

		/// <summary>标题栏高度。</summary>
		public const int TitleBarH = 60;

		/// <summary>内容列最大宽度（参照里是 880，左对齐）。</summary>
		public const int CardMaxW = 880;

		/// <summary>卡片之间的垂直间距。</summary>
		public const int CardGap = 16;

		// ——— 字号（设计像素，统一 6 档，杜绝"到处都是 12/13/13.5/17"） ———
		public const float FontBrand = 14f;

		public const float FontPageTitle = 17f;

		public const float FontCardTitle = 15f;

		public const float FontRow = 13f;

		public const float FontNote = 12f;

		public const float FontTiny = 11f;
}
