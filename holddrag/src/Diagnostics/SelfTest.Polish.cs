// HoldDrag · Diagnostics/SelfTest.Polish.cs
//
// v1.15 的四个小改进：Shift 临时关吸附、触发提示浮层、搜索实时定位、规则通配符。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
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

	/// <summary>外部语言包：加载、匹配、缺词条回退、非法包被拒。</summary>
	private static void TestLanguagePacks()
	{
		string dir = LanguagePack.Dir;
		string path = System.IO.Path.Combine(dir, "selftest-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
		try
		{
			System.IO.Directory.CreateDirectory(dir);
			System.IO.File.WriteAllText(path, "{\"id\":\"tst\",\"name\":\"Testish\",\"map\":{\"\u53d6\u6d88\":\"Cancel-ish\",\"\u4e0d\u5b58\u5728\u7684\u952e\":\"x\"}}", new System.Text.UTF8Encoding(false));
			LanguagePack pack = LanguagePack.TryLoad(path);
			Check(pack != null, "★ 外部语言包能被加载");
			if (pack != null)
			{
				Eq(pack.Id, "tst", "语言包 id 读对");
				Eq(pack.Name, "Testish", "语言包显示名读对");
				Eq(pack.TranslatedCount, 2, "统计已翻译条目数（含空值过滤）");
			}
			Loc.RefreshPacks();
			bool found = false;
			StringBuilder ids = new StringBuilder();
			foreach (var lang in Loc.AllLanguages())
			{
				if (lang.Id == "tst")
				{
					found = true;
				}
				ids.Append(lang.Id).Append(' ');
			}
			Check(found, "★ 语言列表里出现了新语言", ids.ToString());
			Eq(Loc.Normalize("tst"), "tst", "Normalize 认这门语言");
			Loc.Init("tst");
			Eq(Loc.T("取消"), "Cancel-ish", "★ 外部语言包里的词条生效");
			Eq(Loc.T("这条根本没翻译过"), "这条根本没翻译过", "★ 缺词条时回退中文原文（不露 key）");
			Eq(Loc.DisplayName("tst"), "Testish", "下拉框显示语言包自己的名字");
			Loc.Init(Loc.Zh);
			Eq(Loc.T("取消"), "取消", "切回中文后不再翻译");
			// 真的把文件删掉再刷新，才叫"语言包被移除"
			System.IO.File.Delete(path);
			Loc.RefreshPacks();
			Eq(Loc.Normalize("tst"), Loc.Zh, "★ 语言包被移除后回落到中文（不会卡在一个不存在的语言上）");
			// 内置语言不允许被文件覆盖
			string bad = System.IO.Path.Combine(dir, "selftest-bad-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
			System.IO.File.WriteAllText(bad, "{\"id\":\"en\",\"name\":\"Hijack\",\"map\":{}}", new System.Text.UTF8Encoding(false));
			Check(LanguagePack.TryLoad(bad) == null, "★ 外部包不能覆盖内置语言（en）");
			try
			{
				System.IO.File.Delete(bad);
			}
			catch
			{
			}
		}
		finally
		{
			try
			{
				if (System.IO.File.Exists(path))
				{
					System.IO.File.Delete(path);
				}
			}
			catch
			{
			}
			Loc.RefreshPacks();
			Loc.Init(Loc.Zh);
		}
	}

	/// <summary>语言包模板能生成，且列出所有内置词条。</summary>
	private static void TestLanguageTemplate()
	{
		string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hd-template-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
		try
		{
			int num = LanguagePack.WriteTemplate(path, Loc.Keys(), "xx", "Template");
			Check(num > 100, "★ 模板里列出了全部内置词条", "共 " + num + " 条");
			Check(System.IO.File.Exists(path), "模板文件已生成");
			string text = System.IO.File.ReadAllText(path);
			Check(text.Contains("\"id\": \"xx\""), "模板带 id 字段");
			Check(text.Contains("取消"), "模板里能看到中文原文（方便逐条翻译）");
		}
		finally
		{
			try
			{
				if (System.IO.File.Exists(path))
				{
					System.IO.File.Delete(path);
				}
			}
			catch
			{
			}
		}
	}

}
