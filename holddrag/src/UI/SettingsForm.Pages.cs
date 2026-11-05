// HoldDrag · UI/SettingsForm.Pages.cs

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
	private void BuildPages()
	{
		// ---------- 1. 触发方式 ----------
		Page page = NewPage(Loc.T("触发方式"), Loc.T("决定什么时候开始接管窗口；短按仍然原样放行"));
		Card card = NewCard(page, Loc.T("触发键"), Loc.T("按住它，光标下的窗口才开始响应拖动"));
		card.RegisterField(_txtKey);
		card.Setting(_txtKey, 150, _btnRecord, 92, Loc.T("当前触发键"), Loc.T("点「重新录制」后按任意键或鼠标侧键即可更换"));
		card.Block(_lblKeyHint, null, 22);
		card.Setting(_cmbTriggerMode, Loc.T("触发模式"), Loc.T("长按：按住才接管、松手即退出；点按：点一下抓住、再点一下放下（此时触发键不再输入字符）"), 200);
		Card cardHold = NewCard(page, Loc.T("长按判定"), Loc.T("区分「长按触发」和「正常打字」就靠这一段时长"));
		cardHold.Toggle(_chkHold, Loc.T("需要长按触发"), Loc.T("关掉后按住触发键立即生效，不再等阈值"));
		cardHold.Setting(_numHold, 84, UnitLabel(Loc.T("毫秒")), 34, Loc.T("长按阈值"), Loc.T("按住多久才算长按；太短容易误触发，太长会显得迟钝"));

		// ---------- 2. 鼠标键动作 ----------
		Page page2 = NewPage(Loc.T("鼠标键动作"), Loc.T("长按触发键之后，三个鼠标键各自做什么"));
		Card card2 = NewCard(page2, Loc.T("鼠标键动作"), Loc.T("触发期间：左键拖动窗口，右键从边缘缩放，中键执行一次动作"));
		card2.Setting(_cmbLeft, Loc.T("左键"), Loc.T("按住不放，窗口跟着鼠标走"), 200);
		card2.Setting(_cmbRight, Loc.T("右键"), Loc.T("按住不放，从被抓的那条边/角缩放"), 200);
		card2.Setting(_cmbMiddle, Loc.T("中键"), Loc.T("按一下执行一次，不需要按住"), 200);
		Card card3 = NewCard(page2, Loc.T("双击手势"), Loc.T("触发期间快速连按两次鼠标键"));
		card3.Toggle(_chkDblLeft, Loc.T("双击左键 = 最大化 / 还原"), Loc.T("不用去够标题栏，在窗口任意位置都行"));
		card3.Toggle(_chkDblRight, Loc.T("双击右键 = 九宫格分区"), Loc.T("把窗口吸附到屏幕的九个区域之一"));

		// ---------- 3. 视觉反馈 ----------
		Page page3 = NewPage(Loc.T("视觉反馈"), Loc.T("外框的颜色、样式与实时预览"));
		Card card4 = NewCard(page3, Loc.T("外框"), Loc.T("触发期间在窗口四周画一圈提示，让你看清这次会拖动谁"));
		card4.Toggle(_chkFrame, Loc.T("显示外框"), Loc.T("关掉后触发时没有任何视觉提示"));
		card4.Setting(_cmbFrameColor, Loc.T("外框颜色"), Loc.T("先选一个预设颜色；下面色板可以直接点"), 200);
		card4.Block(_gridColor, Loc.T("色板"));
		card4.Toggle(_chkFollowAccent, Loc.T("颜色跟随系统强调色"), Loc.T("换 Windows 主题色时外框一起变；优先级高于自定义颜色"));
		card4.Setting(_btnCustomColor, 120, _lblCustomColor, 240, Loc.T("自定义颜色"), Loc.T("打开系统取色器，可以选任意颜色"));
		Card card5 = NewCard(page3, Loc.T("样式"), Loc.T("外框长什么样：虚线、细线、焦点、柔光、投影"));
		// 这三个输入框是加在 card5 上的，必须由 card5 注册：
		// RegisterField 决定"卡片在自己的坐标系里给输入框补那块圆角底板"，
		// 注册到别的卡片上会让 card5 的输入框没有底板、同时在 card4 上画出三个幽灵白块。
		card5.Setting(_cmbFrameStyle, Loc.T("样式"), Loc.T("选一种画法；下面的缩略图也可以直接点"), 200);
		card5.Block(_galleryStyle, Loc.T("缩略图"));
		card5.Block(_lblStyleHint, null, 22);
		card5.Setting(_numFrameThick, 72, UnitLabel("px"), 26, Loc.T("粗细"), Loc.T("线有多粗；虚线 / 焦点环靠它区分轻重"));
		card5.Setting(_numFrameRadius, 72, UnitLabel("px"), 26, Loc.T("圆角"), Loc.T("外框跟着窗口的圆角走，改这里能立刻在下面的预览里看到"));
		card5.Setting(_numFrameOpacity, 72, UnitLabel("%"), 26, Loc.T("不透明度"), Loc.T("调低会变成半透明，暗色桌面上更柔和"));
		Card card6 = NewCard(page3, Loc.T("预览"), Loc.T("和真正拖动时用的是同一段绘制代码，看到什么就是什么"));
		card6.Block(_pnlPreview, null, 170);
		card6.Block(_lblFrameTip, null, 22);

		// ---------- 4. 拖动行为 ----------
		Page page4 = NewPage(Loc.T("拖动行为"), Loc.T("双击手势、全屏窗口与黑名单"));
		Card card7 = NewCard(page4, Loc.T("拖动与缩放"), Loc.T("触发期间的拖动、缩放细节"));
		card7.Setting(_numCorner, 72, UnitLabel("%"), 26, Loc.T("缩放角区"), Loc.T("鼠标离角多近才算抓住角；越大越好抓，但容易误触"));
		card7.Toggle(_chkRestoreMax, Loc.T("允许拖动已最大化的窗口"), Loc.T("会先把窗口还原成普通大小，再开始拖"));
		Card cardAux = NewCard(page4, Loc.T("辅助"), Loc.T("尺寸提示、贴边吸附、键盘布局与撤销"));
		cardAux.Toggle(_chkShowHud, Loc.T("拖动时显示尺寸提示"), Loc.T("在光标旁浮一个小标签，实时显示窗口宽高"));
		cardAux.Toggle(_chkHudPos, Loc.T("尺寸提示里带上坐标"), Loc.T("同时显示窗口左上角的屏幕坐标"));
		cardAux.Toggle(_chkSnap, Loc.T("拖到屏幕边缘时自动吸附"), Loc.T("离屏幕/工作区边缘很近时自动贴齐，并画一条引导线"));
		cardAux.Setting(_numSnapThreshold, 72, UnitLabel("px"), 26, Loc.T("吸附距离"), Loc.T("离边缘多近才吸附；0 = 关闭"));
		cardAux.Toggle(_chkTiling, Loc.T("键盘分屏"), Loc.T("按住触发键后：← 左半屏、→ 右半屏、↑ 最大化、↓ 还原、1-9 九宫格、0 居中"));
		cardAux.Toggle(_chkUndo, Loc.T("允许 Ctrl+Z 撤销"), Loc.T("拖完 5 秒内按 Ctrl+Z 可以还原窗口原来的位置和大小"));
		Card cardRules = NewCard(page4, Loc.T("按程序规则"), Loc.T("对某个程序单独放行或限制，比黑名单细一档"));
		cardRules.RegisterField(_txtAppRules);
		cardRules.Setting(_cmbRuleMode, 170, _btnAddRule, DesignButtonWidth(Loc.T("点选窗口添加"), 110), Loc.T("添加规则"), Loc.T("先选规则类型，再点右边的按钮，然后把鼠标移到目标窗口上"));
		cardRules.Setting(_txtAppRules, Loc.T("规则列表"), Loc.T("格式：程序名=类型，分号分隔。例：wps.exe=Ignore; code.exe=MoveOnly"));
		Card cardLayout = NewCard(page4, Loc.T("工作区布局"), Loc.T("把当前所有窗口的位置存下来，之后一键恢复"));
		cardLayout.Setting(_cmbLayouts, Loc.T("已保存的布局"), Loc.T("按 程序名 + 标题 匹配窗口，找不到的自动跳过"), 200);
		cardLayout.Setting(_btnLayoutSave, DesignButtonWidth(Loc.T("保存当前…"), 100), _btnLayoutDelete, DesignButtonWidth(Loc.T("删除"), 74), Loc.T("保存 / 删除"), Loc.T("保存会记录当前所有可见窗口的位置和大小"));
		cardLayout.Setting(_btnLayoutRestore, DesignButtonWidth(Loc.T("恢复选中布局"), 120), null, 0, Loc.T("恢复"), Loc.T("把窗口搬回保存时的位置；已经关掉的窗口会被忽略"));
		Card card8 = NewCard(page4, Loc.T("窗口层级与全屏"), Loc.T("全屏 / 无边框窗口容易被拖成一片黑"));
		card8.Toggle(_chkFront, Loc.T("开始拖动时把窗口提到最前"), Loc.T("不用先点一下激活它"));
		card8.Toggle(_chkFullscreen, Loc.T("允许拖动全屏 / 无边框窗口（有风险）"), Loc.T("部分游戏和视频窗口拖完会显示异常"));
		card8.Toggle(_chkExitFullscreen, Loc.T("触发时先把全屏 / 最大化窗口变回普通"), Loc.T("推荐开着：拖动和缩放都从正常尺寸开始"));
		Card card9 = NewCard(page4, Loc.T("晃窗最小化"), Loc.T("选中窗口后左右晃一晃"));
		card9.Toggle(_chkShake, Loc.T("晃窗把其它窗口最小化"), Loc.T("再晃一次还原；判定要求多次往复且净位移接近 0，正常拖动不会误触发"));
		Card card10 = NewCard(page4, Loc.T("不生效的窗口"), Loc.T("命中黑名单的窗口完全不接管"));
		card10.RegisterField(_txtBlacklist);
		card10.Setting(_txtBlacklist, Loc.T("关键字"), Loc.T("窗口标题 / 程序名 / 类名，逗号分隔；命中就完全不接管"));
		card10.Setting(_btnPickExe, DesignButtonWidth(_btnPickExe.Text, 96), _btnPickWindow, DesignButtonWidth(_btnPickWindow.Text, 92), Loc.T("快速添加"), Loc.T("不知道填什么时，用「选择程序」挑 exe，或「点选窗口」直接在屏幕上点那扇窗"));

		// ---------- 5. 流畅度 ----------
		Page page5 = NewPage(Loc.T("流畅度"), Loc.T("拖动过程中的刷新节流"));
		Card card11 = NewCard(page5, Loc.T("刷新模式"), Loc.T("刷新越快越跟手，但也更吃 CPU"));
		card11.Setting(_cmbRefreshMode, Loc.T("刷新模式"), Loc.T("「自动」会按当前显示器的刷新率决定"), 200);
		Card card11b = NewCard(page5, Loc.T("固定值"), Loc.T("只在「固定值」模式下用得上"));
		card11b.Setting(_numRefreshHz, 72, UnitLabel("Hz"), 26, Loc.T("刷新频率"), Loc.T("0 = 不限制；非 0 时下限钳到 20"));
		card11b.Block(_lblRefreshHint, null, 22);

		// ---------- 6. 启动与权限 ----------
		Page page6 = NewPage(Loc.T("启动与权限"), Loc.T("语言、开机自启与管理员权限"));
		Card cardLang = NewCard(page6, Loc.T("语言 / Language"), Loc.T("界面文案用哪种语言显示；切换后立即生效并写入配置"));
		cardLang.Setting(_cmbLanguage, Loc.T("界面语言"), Loc.T("简体中文 / English。选中即保存，整个界面马上换成该语言，下次启动沿用"), 220);
		Card card12 = NewCard(page6, Loc.T("开机自启"), Loc.T("登录 Windows 后自动在托盘运行"));
		card12.Setting(_cmbAutoStart, Loc.T("开机自启"), Loc.T("显示的是系统里的真实状态（会去查计划任务与注册表）"), 320);
		Card card12b = NewCard(page6, Loc.T("管理员权限"), Loc.T("管理员权限能拖动更多程序，代价是每次启动弹一次 UAC"));
		card12b.Toggle(_chkRunAsAdmin, Loc.T("开机时自动申请管理员权限"), Loc.T("默认开启；关掉后遇到管理员程序可能拖不动"));
		card12b.Toggle(_chkWarnNonAdmin, Loc.T("没有管理员权限时提示我"), Loc.T("不提示的话，拖不动会很像程序坏了"));
		card12b.Block(_lblAdmin, null, 22);
		card12b.Setting(_btnRestartAdmin, Loc.T("立即生效"), Loc.T("改完权限相关设置后，用这个按钮重启一次"), 128);

		// ---------- 7. 帮助 ----------
		Page page7 = NewPage(Loc.T("帮助"), Loc.T("每一项功能的实际行为说明"));
		Card cardDiag = NewCard(page7, Loc.T("诊断"), Loc.T("遇到问题先导出诊断包：配置 + 最近日志 + 系统信息"));
		cardDiag.Setting(_btnExportDiag, DesignButtonWidth(Loc.T("导出诊断包…"), 130), null, 0, Loc.T("导出诊断包"), Loc.T("打成一个 zip，方便发给开发者"));
		Card card13 = NewCard(page7, Loc.T("功能说明"), Loc.T("点一下标题，就能展开对应功能的说明"));
		card13.Block(_helpPanel);

		// ---------- 8. 关于 ----------
		Page page8 = NewPage(Loc.T("关于"), Loc.T("版本、开发者与更新"));
		Card card14 = NewCard(page8, Loc.T("关于"), Loc.T("长按拖窗 HoldDrag · 长按触发键，随手拖动 / 缩放任意窗口"));
		card14.Setting(ValueLabel("QQQOrange"), Loc.T("开发者"), null, 140);
		card14.Setting(ValueLabel("DeepSeek"), Loc.T("开发工具"), null, 140);
		card14.Setting(ValueLabel("v" + AppInfo.Version, accent: true), Loc.T("当前版本"), null, 140);
		card14.Setting(ValueLabel(AppInfo.ReleaseDate), Loc.T("更新时间"), null, 140);
		card14.Setting(_btnGitHub, Loc.T("项目主页"), "github.com/QQQOrange/holddrag", 250);
		card14.Toggle(_chkAutoUpdate, Loc.T("自动更新"), Loc.T("启动时检查一次，有新版本会提示"));
		card14.Setting(_btnCheckUpdate, DesignButtonWidth(_btnCheckUpdate.Text, 104), _lblUpdateStatus, 240, Loc.T("检查更新"));

		// v1.7.0：GitHub 个人访问令牌。它同时是「自动下载」的开关：
		// 填了 → 带令牌请求接口（5000 次/小时）并自动下载；没填 → 只提示新版本 + 给出可点击的下载地址。
		Card card15 = NewCard(page8, Loc.T("GitHub 访问令牌"), Loc.T("可选。填了就带令牌请求 GitHub 接口（限额 60 → 5000 次/小时）；无论填不填，下载前都会先弹窗问一次"));
		card15.RegisterField(_txtToken);
		card15.Setting(_txtToken, 240, _btnTokenHelp, 32, Loc.T("个人访问令牌"), Loc.T("只需要公开仓库的读取权限，不需要写权限；令牌明文保存在本机 config.json 里"));
		card15.Setting(_btnSaveToken, 104, _lblTokenState, 250, Loc.T("保存令牌"), Loc.T("点一下立即生效，不必再点底部的「保存并生效」"));
		card15.Setting(_btnVerifyToken, 104, _lblTokenVerify, 250, Loc.T("验证令牌"), Loc.T("真的向 GitHub 发一次请求：确认令牌有效、能读到本仓库的发布信息（不改配置）"));
	}

	private static List<HelpPanel.Entry> BuildHelpEntries()
	{
		return new List<HelpPanel.Entry>
		{
			new HelpPanel.Entry
			{
				Icon = Glyph.Keyboard,
				Title = Loc.T("触发方式"),
				Body = Loc.T("按住触发键（默认 Enter，可改成任意键或鼠标侧键）达到设定时长后生效，光标下那扇窗口四周会亮起外框；松开触发键即退出。短按会原样放行，不影响正常打字。")
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Move,
				Title = Loc.T("鼠标键动作"),
				Body = Loc.T("触发后：左键拖动窗口、右键缩放窗口、中键执行一次性动作。三个键都能自定义成移动 / 缩放 / 最大化 / 最小化 / 关闭 / 置底 / 置顶 / 居中。双击左键可最大化，双击右键按九宫格吸附。")
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Color,
				Title = Loc.T("外框外观"),
				Body = Loc.T("外框颜色、样式、粗细、圆角、不透明度都可以调，页面里就是实时预览，和真正拖窗口时用的是同一段绘制代码。窗口贴近屏幕边缘时外框几何保持不变。")
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Shield,
				Title = Loc.T("全屏与最大化窗口"),
				Body = Loc.T("默认开启「触发时先把全屏 / 最大化窗口变回普通窗口」：这样拖动和缩放都从一个正常的窗口尺寸开始，不会出现全屏窗口拖完一片黑、或者缩放抓不到边的情况。")
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Speed,
				Title = Loc.T("晃窗最小化其它窗口"),
				Body = Loc.T("触发后选中窗口，左右来回晃一晃，其它窗口会全部最小化；再晃一次即可还原。判定要求「多次往复 + 净位移接近 0」，正常拖动窗口不会被误触发。")
			},
			new HelpPanel.Entry
			{
				Icon = Glyph.Shield,
				Title = Loc.T("不生效的窗口"),
				Body = Loc.T("把窗口标题 / 程序名 / 窗口类名的关键字填进黑名单（逗号分隔），命中就完全不接管。不知道填什么可以用「选择程序」挑 exe，或用「点选窗口」直接在屏幕上点那扇窗。")
			}
		};
	}

	private void SelectPage(int index)
	{
		if (index < 0 || index >= _pages.Count)
		{
			return;
		}
		_activePage = index;
		for (int i = 0; i < _pages.Count; i++)
		{
			_pages[i].Host.Visible = i == index;
		}
		if (_nav != null)
		{
			_nav.ActiveIndex = index;
			_nav.KickGlow(index);
		}
		// 切页时把目标页按当前宽度补排一次（运行时只排可见页，所以这里必须补）
		// 标题栏跟着换页：当前页的名字 + 一句说明（参照里也是标题在顶上、内容在下面）
		_lblPageTitle.Text = _pages[index].Title;
		_lblPageSub.Text = _pages[index].Subtitle;
		LayoutChrome();
		LayoutWithContent(ContentWidth());
		_pages[index].Host.Invalidate();
	}

	/// <summary>
	/// 内容列宽。窗口不宽时单列、限宽 880 并水平居中（不在右边留一大片空白）；
	/// 窗口拉得很宽（最大化）时改成两列卡片，把宽度用满，而不是让右半边空着。
	/// </summary>
	private int CardWidth(int contentW, out bool twoColumns, out int left, out int gap)
	{
		int num = S(24);
		int num2 = Math.Max(S(200), contentW - num * 2);
		twoColumns = num2 >= S(1200);
		gap = S(16);
		if (twoColumns)
		{
			left = num;
			return Math.Max(S(320), (num2 - gap) / 2);
		}
		int num3 = Math.Min(num2, S(Tokens.CardMaxW));
		// 单列但窗口还比较宽：左右均分留白，视觉上居中，而不是全挤在左边
		left = num + Math.Max(0, (num2 - num3) / 2);
		return num3;
	}

	private int _lastLayoutW = -1;
	private string _lastLayoutSig = "";

	// 影响行高的只有"文字 + 可用宽度"。两者都没变时跳过整轮重排：
	// 拖窗口时 WM_SIZE 一来就是几十次，之前每次都把 8 页 17 张卡片全排一遍。
	private string LayoutSignature()
	{
		StringBuilder stringBuilder = new StringBuilder(1024);
		foreach (Page page in _pages)
		{
			foreach (Card card in page.Cards)
			{
				card.AppendSignature(stringBuilder);
			}
		}
		return stringBuilder.ToString();
	}

	private void LayoutWithContent(int contentW)
	{
		LayoutChrome();
		contentW = ContentWidth();
		if (contentW < 10)
		{
			contentW = 10;
		}
		LayoutPages(contentW, forceAll: false);
	}

	/// <summary>把 8 页全部排一遍（首次构建、DPI 重建、自检用）。</summary>
	private void LayoutAllPages(int contentW)
	{
		LayoutChrome();
		contentW = ContentWidth();
		if (contentW < 10)
		{
			contentW = 10;
		}
		LayoutPages(contentW, forceAll: true);
	}

	private static string PageSignature(Page page)
	{
		StringBuilder stringBuilder = new StringBuilder(256);
		foreach (Card card in page.Cards)
		{
			card.AppendSignature(stringBuilder);
		}
		return stringBuilder.ToString();
	}

	private void LayoutPages(int contentW, bool forceAll)
	{
		for (int i = 0; i < _pages.Count; i++)
		{
			Page page = _pages[i];
			bool flag = i == _activePage;
			// 看不见的页不排：拖窗口/最大化时只有当前页会真的显示出来，
			// 之前 8 页 17 张卡每次都全排一遍，绝大部分是无用功。切页时会补排。
			if (!flag && !forceAll)
			{
				continue;
			}
			string text = PageSignature(page);
			if (!forceAll && page.LaidOutWidth == contentW && page.Signature == text)
			{
				continue;
			}
			LayoutOnePage(page, contentW, text);
		}
	}

	private void LayoutOnePage(Page page, int contentW, string signature)
	{
		// 每页单独算列宽：
		//  - 两张以上卡片 + 窗口够宽 → 两列，把宽度用满；
		//  - 只有一张卡片 → 占满（最多 1200 逻辑）并居中，不排两列（否则单卡只占左半边，右边又空了）；
		//  - 其余 → 单列限宽 880，居中。
		bool flag;
		int num;
		int num2;
		int numW = CardWidth(contentW, out flag, out num, out num2);
		bool flag2 = flag && page.Cards.Count >= 2;
		int cardW;
		int numX;
		if (flag2)
		{
			cardW = numW;
			numX = num;
		}
		else
		{
			int numY = Math.Max(S(200), contentW - S(48));
			cardW = Math.Min(numY, (page.Cards.Count <= 1) ? S(1200) : S(Tokens.CardMaxW));
			numX = S(24) + Math.Max(0, (numY - cardW) / 2);
		}
		page.Host.SuspendLayout();
		page.Host.Canvas.SuspendLayout();
		try
		{
			int num3 = S(6);
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			foreach (Card card in page.Cards)
			{
				int num7 = card.Measure(cardW);
				if (flag2)
				{
					// 两列：0/2/4… 放左列，1/3/5… 放右列；同一行取两张卡里较高的那个做行高
					if (num4 % 2 == 0)
					{
						num5 = num3;
						num6 = num7;
						Place(card, new Rectangle(numX, num3, cardW, num7));
					}
					else
					{
						Place(card, new Rectangle(numX + cardW + num2, num5, cardW, num7));
						num3 = num5 + Math.Max(num6, num7) + S(Tokens.CardGap);
					}
					num4++;
				}
				else
				{
					Place(card, new Rectangle(numX, num3, cardW, num7));
					num3 += num7 + S(Tokens.CardGap);
				}
				card.Layout(cardW);
			}
			if (flag2 && num4 % 2 == 1)
			{
				num3 = num5 + num6 + S(Tokens.CardGap);
			}
			Size size = new Size(contentW, Math.Max(num3 + S(4), S(80)));
			if (page.Host.Canvas.Size != size)
			{
				page.Host.Canvas.Size = size;
			}
			page.Host.UpdateScroll();
			page.LaidOutWidth = contentW;
			page.Signature = signature;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			page.Host.Canvas.ResumeLayout(performLayout: false);
			page.Host.ResumeLayout(performLayout: false);
		}
	}
}
