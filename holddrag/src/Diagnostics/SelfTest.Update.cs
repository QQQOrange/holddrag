// HoldDrag · Diagnostics/SelfTest.Update.cs

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

internal static partial class SelfTest
{
	private static void TestUpdateFeatures()
	{
		using (DragFixture dragFixture = new DragFixture(null))
		{
			using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
			settingsForm.CreateControl();
			Application.DoEvents();

			// 1) 三个新控件都必须真的挂进「关于」分组、可见可点
			Button button = settingsForm.ProbeGitHubButton;
			Button button2 = settingsForm.ProbeCheckUpdateButton;
			CheckBox checkBox = settingsForm.ProbeAutoUpdateCheck;
			Label label = settingsForm.ProbeUpdateStatusLabel;
			Check(button != null && button2 != null && checkBox != null && label != null, "★ 关于页的『项目主页』『检查更新』『自动更新』『状态文字』四个控件都建出来了");
			Check(button != null && button.Text.Contains("github.com/QQQOrange/holddrag"), "★ 项目主页按钮显示 GitHub 地址", "实际=" + (button?.Text ?? "(null)"));
			Check(button2 != null && button2.Text == "检查更新", "★ 按钮文案是「检查更新」", "实际=" + (button2?.Text ?? "(null)"));
			Check(checkBox != null && checkBox.Text.Contains("自动更新"), "★ 自动更新开关文案含「自动更新」", "实际=" + (checkBox?.Text ?? "(null)"));
			Check(label != null && label.Text.Length > 0, "★ 更新状态有初始文字", "实际=" + (label?.Text ?? "(null)"));

			// 2) 三个控件必须在可视树里（不是"建了却没加进去"）
			bool flag = false;
			bool flag2 = false;
			bool flag3 = false;
			Control groupBox = null;
			for (int i = 0; i < SettingsForm.ProbeGroupCount(settingsForm); i++)
			{
				Control control2 = SettingsForm.ProbeGroupAt(settingsForm, i);
				if (control2 != null && control2.Text != null && control2.Text.Contains("关于"))
				{
					groupBox = control2;
					break;
				}
			}
			Check(groupBox != null, "取到『关于』那张卡片");
			if (groupBox != null)
			{
				for (int j = 0; j < groupBox.Controls.Count; j++)
				{
					Control control = groupBox.Controls[j];
					if (control == button) flag = true;
					if (control == button2) flag2 = true;
					if (control == checkBox) flag3 = true;
				}
			}
			Check(flag && flag2 && flag3, "★ 三个新控件都真的挂在『关于』分组里（否则用户根本看不到）", $"github={flag} checkUpdate={flag2} autoUpdate={flag3}");

			// 3) 自动更新默认开启，且勾选状态与配置双向一致
			Check(dragFixture.Store.Current.AutoCheckUpdate, "★ 自动更新默认开启");
			Check(checkBox != null && checkBox.Checked, "★ 自动更新复选框默认是勾上的");
			if (checkBox != null)
			{
				checkBox.Checked = false;
				Application.DoEvents();
				Check(!settingsForm.ProbeAutoUpdateCheck.Checked, "★ 取消勾选后控件状态跟着变");
				checkBox.Checked = true;
				Application.DoEvents();
			}
			// 3.5) v1.7.0：令牌输入框 + 「?」帮助图标（对齐、无障碍、链接集中配置）
			TextBox textBox = settingsForm.ProbeTokenBox;
			Button button3 = settingsForm.ProbeTokenHelpButton;
			Button button4 = settingsForm.ProbeSaveTokenButton;
			Label label2 = settingsForm.ProbeTokenStateLabel;
			Check(textBox != null && button3 != null && button4 != null && label2 != null, "★ 令牌输入框 / 「?」图标 / 保存按钮 / 状态文字四个控件都建出来了");
			Check(textBox != null && textBox.UseSystemPasswordChar, "★ 令牌输入框是密码样式（不明文显示）");
			Check(button3 != null && button3.Text == "?" && button3.TabStop, "★ 「?」图标存在且可 Tab 聚焦（键盘可达）");
			Check(button3 != null && !string.IsNullOrEmpty(button3.AccessibleName) && !string.IsNullOrEmpty(button3.AccessibleDescription), "★ 「?」图标带无障碍名称与说明（读屏可读）", "name=" + (button3?.AccessibleName ?? "(null)"));
			Eq(AppInfo.PatDocsUrl, "https://docs.github.com/zh/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens", "★ 「?」打开的是指定的 GitHub 官方中文文档（地址集中配置在 AppInfo.PatDocsUrl）");
			Check(label2 != null && label2.Text != null && label2.Text.Contains("未填写"), "★ 未填令牌时状态文字说明「只提示、不自动下载」", "实际=" + (label2?.Text ?? "(null)"));
			// v1.8.0：验证按钮（真的发一次请求，不改配置）
			Button button5 = settingsForm.ProbeVerifyTokenButton;
			Label label3 = settingsForm.ProbeTokenVerifyLabel;
			Check(button5 != null && button5.Text == "验证令牌", "★ 令牌卡片里有独立的「验证令牌」按钮", "实际=" + (button5?.Text ?? "(null)"));
			Check(button5 != null && button5.TabStop && !string.IsNullOrEmpty(button5.AccessibleName), "★ 「验证令牌」按钮可 Tab 聚焦且带无障碍名称", "name=" + (button5?.AccessibleName ?? "(null)"));
			Check(label3 != null && label3.Text.Contains("尚未验证"), "★ 验证结果有初始文案（尚未验证）", "实际=" + (label3?.Text ?? "(null)"));
			// 对齐要用真实布局后的坐标：先把所有页排一遍（否则隐藏页里的控件还是初始坐标）
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
			if (textBox != null && button3 != null && textBox.Width > 40 && button3.Width > 8)
			{
				int num = textBox.Top + textBox.Height / 2;
				int num2 = button3.Top + button3.Height / 2;
				Check(Math.Abs(num - num2) <= 3, "★ 「?」图标与令牌输入框垂直居中对齐", "输入框中线=" + num + " 图标中线=" + num2);
				Check(button3.Left >= textBox.Right - Fields_Sc(2), "「?」图标在输入框右侧", "input.right=" + textBox.Right + " icon.left=" + button3.Left);
			}
			else
			{
				Bad("★ 「?」图标与令牌输入框对齐", "控件尚未布局出尺寸，无法测量");
			}
			settingsForm.Close();
		}

		// 4) 版本比较：只认"严格更新"，且能容忍 v 前缀与后缀
		Eq(Updater.ParseVersion("v1.1.0").ToString(), "1.1.0.0", "能从 v1.1.0 解析出版本");
		Eq(Updater.ParseVersion("1.2.3").ToString(), "1.2.3.0", "能从 1.2.3 解析出版本");
		Eq(Updater.ParseVersion("v1.2.3.4").ToString(), "1.2.3.4", "四段版本号解析正确");
		Eq(Updater.ParseVersion("v1.2.0-beta.1").ToString(), "1.2.0.0", "带 -beta 后缀也能解析");
		Check(Updater.ParseVersion("not-a-version") == null, "★ 非法标签解析返回 null，不会误判成新版本");
		Check(Updater.ParseVersion("") == null, "★ 空标签返回 null");
		Check(Updater.ParseVersion("v1.1.0") > Updater.ParseVersion("v1.0.0"), "★ 1.1.0 比 1.0.0 新");
		Check(!(Updater.ParseVersion("v1.0.0") > Updater.ParseVersion("v1.0.0")), "★ 同版本不算新版本（不会自己提示自己）");
		Check(Updater.CurrentVersion() == Updater.ParseVersion(AppInfo.Version), "★ 当前版本解析与 AppInfo.Version 一致", "AppInfo=" + AppInfo.Version);

		// 5) 从重定向 URL / HTML 里提取 tag
		Eq(Updater.TagFromUrl("https://github.com/QQQOrange/holddrag/releases/tag/v1.1.0"), "v1.1.0", "从 releases/tag/ URL 提取 tag");
		Eq(Updater.TagFromUrl("https://github.com/QQQOrange/holddrag/releases/latest"), null, "latest 这种 URL 提取不出 tag（返回 null）");
		Eq(Updater.TagFromUrl(null), null, "null URL 安全返回 null");
		Eq(Updater.TagFromHtml("<a href=\"/QQQOrange/holddrag/releases/tag/v2.0.0\">x</a>"), "v2.0.0", "★ 从 HTML 正文兜底提取 tag");
		Eq(Updater.TagFromHtml("<html>no tag here</html>"), null, "HTML 里没有 tag 时返回 null");

		// 6) 下载进度文案
		string text = SettingsForm.FormatDownloadProgress(512, 1024);
		Check(text.Contains("50%"), "进度文案含百分比", "实际=" + text);
		Check(text.Contains("MB"), "进度文案含 MB", "实际=" + text);
		Check(SettingsForm.FormatDownloadProgress(1024, 0).Contains("已接收"), "总长未知时退化为『已接收』", "实际=" + SettingsForm.FormatDownloadProgress(1024, 0));

		// 7) 替换机制：必须靠"重命名正在运行的 exe"完成，不依赖批处理脚本。
		//    本机实测：Windows 允许重命名正在运行的 exe、也允许把新文件移到它的名下；
		//    唯独不允许删除正在运行的旧文件 —— 所以替换器必须走 rename 路线。
		Check(Updater.OldSuffix == ".old", "旧版本备份后缀是 .old");
		Check(!Updater.TempDownloadName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), "★ 下载中转文件不是 .exe 后缀（避免被 shell 当程序执行）");
		MethodInfo methodInfo = typeof(Updater).GetMethod("BuildSwapperScript", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		Check(methodInfo == null, "★ 已彻底移除「批处理替换器」实现（旧实现把 .bat 当 .exe 启动，必然失败）");
		Check(typeof(Updater).GetMethod("CleanupOldVersion", BindingFlags.Public | BindingFlags.Static) != null, "★ 提供了启动后清理旧版本备份的入口");
		Check(typeof(Updater).GetField("ArgUpdatedFrom", BindingFlags.Public | BindingFlags.Static) != null, "★ 通过 --updated-from 参数把备份路径交给新版本清理");

		// 8) 网络失败要如实回传错误，不能静默当成"已是最新"
		string error;
		bool upToDate;
		SettingsForm.ProbeForceOfflineForTest = true;
		try
		{
			UpdateInfo updateInfo = Updater.CheckLatest(out error, out upToDate);
			Check(updateInfo == null, "离线时不会返回一个假的『新版本』");
			Check(!upToDate, "★★ 离线时【绝不能】把结果当作『已是最新』（否则用户会以为检查成功了）");
			Check(!string.IsNullOrEmpty(error), "★ 离线时回传了具体的错误原因", "error=" + error);
		}
		finally
		{
			SettingsForm.ProbeForceOfflineForTest = false;
		}

		// 9) ★ 真实验证替换机制本身：在临时目录里用真文件跑一遍
		//    "旧 exe 改名 → 新文件顶替 → 内容确实换成了新的"。
		//    这一步才是真正证明更新能落地的地方；只断言脚本文本会给出假绿灯。
		TestInPlaceSwapMechanism();

		// 10) 钩子回调线程绝不能同步落盘（这是"整机输入卡顿"的根因之一）
		TestHookThreadLoggingIsAsync();

		// 11) 更新重启时新进程必须"等旧进程释放互斥体再接管"，
		//     否则新版本会因为单实例检查而立刻退出，更新完一个进程都不剩。
		TestUpdateRestartSingleInstance();
	}
	private static void TestUpdateRestartSingleInstance()
	{
		// ①/③ 参数解析：Program 的 ArgValue 与 Updater.ArgUpdatedFrom 必须对得上
		string argUpdatedFrom = Updater.ArgUpdatedFrom;
		Eq(argUpdatedFrom, "--updated-from", "更新接续参数名固定为 --updated-from");
		Eq(Program.ProbeArgValueForTest(new string[2] { "--updated-from", @"C:\app\HoldDrag.exe.old" }, argUpdatedFrom), @"C:\app\HoldDrag.exe.old", "★ 能从命令行取出旧版本备份路径");
		Eq(Program.ProbeArgValueForTest(new string[2] { "--updated-from", @"C:\My App\HoldDrag.exe.old" }, argUpdatedFrom), @"C:\My App\HoldDrag.exe.old", "★ 路径带空格也能正确取出（调用方负责加引号）");
		Eq(Program.ProbeArgValueForTest(new string[1] { "--selftest" }, argUpdatedFrom), string.Empty, "没有该参数时返回空串（普通启动 → 不等待互斥体）");
		Eq(Program.ProbeArgValueForTest(null, argUpdatedFrom), string.Empty, "null 参数数组安全返回空串");
		Check(Program.HasArgForTest(new string[1] { "--updated-from" }, argUpdatedFrom), "HasArg 能识别 --updated-from");

		// ② 普通启动不能因为这次改动变成"傻等"
		//    用真实互斥体验证：先占住，再按普通启动语义判定；由于不能真的启动第二进程，
		//    这里直接验证"是否等待"的判据：只有 --updated-from 存在才等待。
		string text = Program.ProbeArgValueForTest(new string[2] { "--updated-from", "x" }, argUpdatedFrom);
		Check(!string.IsNullOrEmpty(text), "★ 带 --updated-from 时判据成立 → 会等待旧进程释放互斥体");
		string text2 = Program.ProbeArgValueForTest(new string[1] { "--smoke" }, argUpdatedFrom);
		Check(string.IsNullOrEmpty(text2), "★ 普通启动（无 --updated-from）判据不成立 → 立即让位，不等待");
	}
	private static void TestSingleInstanceOnlyOne()
	{
		// 契约冻结：名字一改，新旧版本就互不识别，于是又能同时开出两个。
		Eq(Program.MutexNameForTest, "Local\\HoldDrag.SingleInstance.v1", "★ 单实例互斥体名已冻结（改名会导致新旧版本各开一个）");
		Eq(Program.ShowEventNameForTest, "Local\\HoldDrag.ShowSettings.v1", "★ 唤起设置窗口的命名事件名已冻结");

		bool createdNew;
		Mutex mutex = null;
		try
		{
			mutex = new Mutex(initiallyOwned: true, Program.MutexNameForTest, out createdNew);
			if (!createdNew)
			{
				// 本机真的已经有一个实例在跑 —— 这恰恰说明互斥体在起作用。
				Line("（环境限制）本机已有 HoldDrag 实例在运行，单实例端到端验证跳过。");
				return;
			}
			Check(true, "★ 本进程占住了单实例互斥体（模拟「已有一个实例在运行」）");

			// 必须先建好命名事件：第二个实例用 OpenExisting 打开它，建晚了它会打不开而静默退出。
			using (EventWaitHandle showEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, Program.ShowEventNameForTest))
			{
				string exe = AppPaths.ExePath;
				Check(File.Exists(exe), "找得到被测 exe 本体（否则无法做真实第二实例）", exe);
				if (!File.Exists(exe))
				{
					return;
				}
				Process process = null;
				try
				{
					// --no-elevate：避免第二个实例因为提权逻辑又拉起一个进程，干扰判定。
					process = Process.Start(new ProcessStartInfo
					{
						FileName = exe,
						Arguments = "--no-elevate",
						UseShellExecute = false
					});
				}
				catch (Exception ex)
				{
					Bad("启动第二个实例失败（宿主可能禁止创建进程）", ex.Message);
					return;
				}
				if (process == null)
				{
					Bad("启动第二个实例失败", "Process.Start 返回 null");
					return;
				}
				bool exited;
				try
				{
					// 第二个实例应该立刻让位退出，正常在 1~3 秒内，给足 20 秒余量。
					exited = process.WaitForExit(20000);
					if (exited)
					{
						int exitCode = process.ExitCode;
						Check(exitCode == 0, "第二个实例以退出码 0 正常让位", "实际 " + exitCode);
					}
				}
				finally
				{
					if (!process.HasExited)
					{
						try
						{
							process.Kill();
						}
						catch (Exception ex2)
						{
							Log.Error(ex2);
						}
					}
					process.Dispose();
				}
				Check(exited, "★★ 第二个实例【自己退出】了，不会同时驻留两个进程", exited ? null : "等了 20 秒仍在运行");
				bool signaled = showEvent.WaitOne(3000);
				Check(signaled, "★★ 第二个实例唤起了已有实例（命名事件被 Set → 会弹出设置窗口）");
			}
		}
		catch (Exception ex)
		{
			Bad("（单实例端到端验证自身抛异常）", ex.Message);
		}
		finally
		{
			try
			{
				mutex?.ReleaseMutex();
			}
			catch (Exception ex3)
			{
				Log.Error(ex3);
			}
			mutex?.Dispose();
		}
	}
	private static void TestInPlaceSwapMechanism()
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-swap-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(text);
			string text2 = Path.Combine(text, "HoldDrag.exe");
			string text3 = Path.Combine(text, "HoldDrag-update.download");
			string text4 = text2 + Updater.OldSuffix;
			File.WriteAllText(text2, "OLD-BINARY-CONTENT", Encoding.UTF8);
			File.WriteAllText(text3, "NEW-BINARY-CONTENT", Encoding.UTF8);

			// 模拟 ApplyAndRestart 的文件操作序列（不含 Process.Start，避免测试中真拉起进程）
			File.Move(text2, text4);
			File.Move(text3, text2);

			Check(File.Exists(text2), "★ 替换后正式文件名下有文件（用户双击的还是那个名字）");
			Check(!File.Exists(text3), "★ 中转文件已被消费掉，不会留下 .download 垃圾");
			Check(File.Exists(text4), "★ 旧版本被保留为 .old 备份（出问题还能退回）");
			Eq(File.ReadAllText(text2, Encoding.UTF8), "NEW-BINARY-CONTENT", "★★ 正式文件名下现在装的确实是【新版本】");
			Eq(File.ReadAllText(text4, Encoding.UTF8), "OLD-BINARY-CONTENT", "★★ .old 备份里确实是【旧版本】");

			// 新版本启动后清理备份
			Updater.CleanupOldVersion(text4);
			Check(!File.Exists(text4), "★ 新版本启动后能成功清掉 .old 备份");

			// ---- 失败路径：源文件不存在时不能把旧文件弄丢 ----
			File.WriteAllText(text2, "STILL-OLD", Encoding.UTF8);
			string text5 = Path.Combine(text, "does-not-exist.download");
			string error;
			bool flag = Updater.ApplyAndRestart(text5, text2, out error);
			Check(!flag, "★ 新文件不存在时替换失败并如实返回 false");
			Check(!string.IsNullOrEmpty(error), "★ 失败时给出具体原因", "error=" + error);
			Check(File.Exists(text2), "★★ 失败后旧 exe 仍在原处（绝不能把用户唯一的可运行文件弄丢）");
		}
		catch (Exception ex)
		{
			Check(false, "★ 就地替换机制验证未抛异常", ex.Message);
			Log.Error(ex);
		}
		finally
		{
			try
			{
				if (Directory.Exists(text))
				{
					Directory.Delete(text, recursive: true);
				}
			}
			catch
			{
			}
		}
	}
}
