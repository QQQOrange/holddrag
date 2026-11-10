// HoldDrag · Diagnostics/SelfTest.Extras.cs
//
// v1.13 新功能的回归：按程序规则、工作区布局、脚本接口、设置搜索、便携模式。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag;

internal static partial class SelfTest
{
	private static void TestAppRules()
	{
		AppRules.Apply("wps.exe=Ignore; code.exe=MoveOnly;bad-entry; game.exe=NoFrame;x.exe=Bogus");
		Eq(AppRules.Count, 3, "规则表只收合法条目（非法条目丢弃）");
		Eq(AppRules.ModeForExe("WPS.EXE"), "Ignore", "程序名大小写不敏感");
		Eq(AppRules.ModeForExe("code.exe"), "MoveOnly", "MoveOnly 规则能读出来");
		Eq(AppRules.ModeForExe("game.exe"), "NoFrame", "NoFrame 规则能读出来");
		Eq(AppRules.ModeForExe("unknown.exe"), null, "没有规则的程序返回 null");
		string text = AppRules.Normalize("b.exe=MoveOnly;a.exe=Ignore;a.exe=NoFrame");
		Check(text.Contains("a.exe=NoFrame"), "同名规则后者覆盖前者");
		Check(text.StartsWith("a.exe=NoFrame;b.exe=MoveOnly", StringComparison.OrdinalIgnoreCase), "规范化后按程序名排序", text);
		AppRules.Apply("");
		Eq(AppRules.Count, 0, "空规则表解析出 0 条");
		AppRules.Apply(string.Empty);
	}

	private static void TestLayoutStore()
	{
		string text = "自检布局-" + Guid.NewGuid().ToString("N").Substring(0, 6);
		try
		{
			LayoutStore.Layout layout = LayoutStore.Save(text);
			Check(layout.Windows.Count >= 0, "捕获布局不抛异常", "窗口数=" + layout.Windows.Count);
			List<LayoutStore.Layout> list = LayoutStore.LoadAll();
			Check(list.Any((LayoutStore.Layout l) => l.Name == text), "保存后能从磁盘读回来");
			LayoutStore.Layout layout2 = list.First((LayoutStore.Layout l) => l.Name == text);
			Check(!string.IsNullOrEmpty(layout2.SavedAt), "布局带保存时间");
			int num = LayoutStore.Restore(layout2);
			Check(num >= 0, "恢复布局不抛异常（找不到窗口就跳过）", "恢复了 " + num + " 个");
			Check(LayoutStore.Delete(text), "布局能删除");
			Check(!LayoutStore.LoadAll().Any((LayoutStore.Layout l) => l.Name == text), "删除后读不到了");
		}
		finally
		{
			try
			{
				LayoutStore.Delete(text);
			}
			catch
			{
			}
		}
	}

	private static void TestIpcRoundTrip()
	{
		// 用独立管道名：正在运行的实例占着正式管道，抢同一个名字会让这条用例误判
		string name = "HoldDrag.selftest." + Guid.NewGuid().ToString("N").Substring(0, 8);
		using IpcServer ipcServer = new IpcServer(delegate(string cmd)
		{
			return (cmd == "ping") ? "OK pong" : "ERR unknown";
		}, name);
		ipcServer.Start();
		Thread.Sleep(150);
		string text = IpcServer.Send("ping", 3000, name);
		Eq(text, "OK pong", "命名管道能收发一条命令");
		string text2 = IpcServer.Send("nope", 3000, name);
		Check(text2.StartsWith("ERR", StringComparison.Ordinal), "未知命令返回 ERR", text2);
	}

	private static void TestSettingsSearch()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		int num = settingsForm.ProbeSearch("吸附");
		Check(num > 0, "搜索「吸附」能找到结果", "命中 " + num + " 处");
		int num2 = settingsForm.ProbeSearch("不存在的关键词zzz");
		Eq(num2, 0, "搜不到时返回 0");
		int num3 = settingsForm.ProbeSearch("外框");
		Check(num3 > 1, "搜索「外框」有多处命中", "命中 " + num3 + " 处");
		string text = settingsForm.ProbeSearchStatus();
		Check(!string.IsNullOrEmpty(text), "搜索后状态栏有提示", text);
	}

	private static void TestPortableMode()
	{
		// 正常安装（没有 portable.txt）：数据目录必须是 %APPDATA%\HoldDrag
		if (!AppPaths.IsPortable)
		{
			Check(AppPaths.DataDir.EndsWith("HoldDrag", StringComparison.OrdinalIgnoreCase), "非便携模式数据目录在 AppData", AppPaths.DataDir);
		}
		else
		{
			Check(AppPaths.DataDir.EndsWith("data", StringComparison.OrdinalIgnoreCase), "便携模式数据目录是 exe 旁的 data", AppPaths.DataDir);
		}
		Check(Directory.Exists(AppPaths.DataDir), "数据目录存在");
	}

	private static void TestRulesAndLayoutUi()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		settingsForm.ProbeSelectPage(4);
		Application.DoEvents();
		Check(settingsForm.ProbeControlVisible("规则列表"), "「规则列表」这一行在页面上");
		Check(settingsForm.ProbeControlVisible("已保存的布局"), "「已保存的布局」这一行在页面上");
		Check(settingsForm.ProbeControlVisible("保存 / 删除"), "「保存 / 删除」按钮行在页面上");
	}
}
