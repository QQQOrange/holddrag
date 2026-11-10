// HoldDrag · Diagnostics/SelfTest.Polish.cs
//
// v1.15 的四个小改进：Shift 临时关吸附、触发提示浮层、搜索实时定位、规则通配符。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag;

internal static partial class SelfTest
{
	/// <summary>规则里的通配符：* 匹配任意串、? 匹配单个字符，且大小写不敏感。</summary>
	private static void TestRuleWildcards()
	{
		AppRules.Apply("*chrome*=MoveOnly; note?.exe=Ignore; exact.exe=NoFrame");
		Eq(AppRules.ModeForExe("chrome.exe"), "MoveOnly", "★ 通配符 *chrome* 能匹配 chrome.exe");
		Eq(AppRules.ModeForExe("GoogleChrome.exe"), "MoveOnly", "通配符匹配不区分大小写且可命中中缀");
		Eq(AppRules.ModeForExe("note1.exe"), "Ignore", "★ 通配符 ? 匹配单个字符");
		Eq(AppRules.ModeForExe("note10.exe"), null, "? 只匹配一个字符，不匹配两个");
		Eq(AppRules.ModeForExe("exact.exe"), "NoFrame", "精确规则仍然优先走精确匹配");
		Eq(AppRules.ModeForExe("other.exe"), null, "不匹配的程序没有规则");
		AppRules.Apply("wps.exe=Ignore");
		Eq(AppRules.ModeForExe("wps.exe"), "Ignore", "清掉通配符后精确规则仍然有效");
		Eq(AppRules.ModeForExe("chrome.exe"), null, "清掉通配符后不再匹配");
		AppRules.Apply(string.Empty);
	}

	/// <summary>设置页：输入即定位（不需要先按回车）。</summary>
	private static void TestSearchLive()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		settingsForm.ProbeSelectPage(0);
		Application.DoEvents();
		int num = settingsForm.ProbeTypeSearch("布局");
		Check(num > 0, "★ 只输入关键字（不按回车）就能定位到结果", "命中 " + num);
		int active = settingsForm.ProbeActivePage();
		Check(active != 0, "★ 定位后自动切到命中的页面", "当前页 " + active);
		string status = settingsForm.ProbeSearchStatus();
		Check(status != null && status.Contains("布局"), "状态栏显示了命中的位置", status);
		int num2 = settingsForm.ProbeTypeSearch("zzz没有这个");
		Eq(num2, 0, "输入不存在的关键字时命中数为 0");
	}

	/// <summary>触发提示浮层：能显示、能隐藏，文字覆盖主操作。</summary>
	private static void TestTriggerHint()
	{
		using DragFixture dragFixture = new DragFixture(delegate(AppConfig c)
		{
			c.ShowTriggerHint = true;
			c.KeyboardTiling = true;
			c.SnapToEdges = true;
		});
		try
		{
			dragFixture.Coord.Drag.ProbeShowTriggerHintForTest();
			Check(dragFixture.Coord.Drag.HintVisibleForTest, "★ 触发提示浮层能显示（独立分层窗口）");
			string text = dragFixture.Coord.Drag.HintTextForTest;
			Check(!string.IsNullOrEmpty(text), "提示文字非空", text);
			Check(text.Contains("左键") || text.Contains("Move"), "提示里写了左键动作", text);
			Check(text.Contains("方向键"), "开了键盘分屏时提示里有方向键", text);
			Check(text.Contains("Shift"), "开了吸附时提示里有 Shift", text);
			dragFixture.Coord.Drag.ProbeHideTriggerHintForTest();
			Thread.Sleep(120);
			Check(!dragFixture.Coord.Drag.HintVisibleForTest, "提示浮层能隐藏");
		}
		finally
		{
			dragFixture.Coord.Drag.ProbeHideTriggerHintForTest();
		}
	}

	/// <summary>Shift 关吸附：提示与配置都能反映"当前按住 Shift"这个状态。</summary>
	private static void TestShiftBypassSnap()
	{
		AppConfig appConfig = new AppConfig();
		Check(appConfig.SnapToEdges, "默认开启吸附");
		Check(appConfig.ShowTriggerHint, "默认开启触发提示");
		// 按住 Shift 时吸附应当被跳过：这里验证纯函数 + 状态读取路径不抛异常
		Native.RECT work = new Native.RECT(0, 0, 1920, 1040);
		SnapEngine.Result result = SnapEngine.SnapMove(new Native.RECT(4, 100, 804, 600), work, 8);
		Eq(result.Rect.Left, 0, "不按 Shift 时仍然吸附");
		Check(DragEngine.ProbeShiftDownForTest() == ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0), "Shift 状态读取与实际按键一致");
	}
}
