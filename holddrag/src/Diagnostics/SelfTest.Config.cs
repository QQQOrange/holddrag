// HoldDrag · Diagnostics/SelfTest.Config.cs

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
	private static void TestDefaults()
	{
		AppConfig appConfig = new AppConfig();
		Eq(appConfig.TriggerKeyId, "K:0x0D", "默认触发键为 Enter");
		Eq(appConfig.HoldMs, 600, "默认长按阈值 600ms");
		Check(appConfig.RequireHold, "默认需要长按");
		Eq(appConfig.ActionLeft, "Move", "左键默认移动");
		Eq(appConfig.ActionRight, "Resize", "右键默认缩放");
		Eq(appConfig.ActionMiddle, "Maximize", "中键默认最大化");
		Check(appConfig.DoubleClickLeftMaximize && appConfig.DoubleClickRightZone, "双击增强默认开启");
		Check(appConfig.ShowActivationFrame && appConfig.BringToFront, "外框 / 提最前 默认开启");
		Check(appConfig.ShakeMinimizeOthers, "晃窗最小化其它窗口默认开启");
		Check(!appConfig.AllowFullscreenWindows, "全屏窗口默认不动（风险项，需手工开启）");
		Check(appConfig.ExitFullscreenOnTrigger, "触发时退出全屏/最大化默认开启（用户要求）");
		Eq(appConfig.FrameOpacity, 100, "外框不透明度默认 100%（完全不透明）");
		Check(appConfig.RunAsAdmin && appConfig.WarnNonAdmin, "管理员权限默认开启，非管理员时默认提示");
		Eq(appConfig.DragRefreshMode, "Auto", "刷新率默认自动跟随屏幕");
	}
	private static void TestKeyIds()
	{
		Eq(KeysUtil.ToVirtualKey("K:0x0D"), 13, "Enter 虚拟键码");
		Eq(KeysUtil.ToVirtualKey("K:RAlt"), 165, "右 Alt 虚拟键码");
		Eq(KeysUtil.ToVirtualKey("K:LShift"), 160, "左 Shift 虚拟键码");
		Eq(KeysUtil.ToVirtualKey("M:1"), 5, "侧键 X1 虚拟键码");
		Eq(KeysUtil.ToVirtualKey("垃圾值"), 0, "非法 Id 返回 0");
		Check(KeysUtil.IsToggleKey("K:0x14"), "CapsLock 被识别为切换型按键");
		Check(!KeysUtil.IsToggleKey("K:0x0D"), "Enter 不是切换型按键");
		Check(KeysUtil.DisplayName("K:0x0D").Contains("Enter"), "Enter 有中文名");
	}
	private static void TestKeyMatching()
	{
		Native.KBDLLHOOKSTRUCT kb = new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 13u
		};
		Check(KeysUtil.MatchesKeyboard("K:0x0D", kb), "Enter 匹配 Enter");
		Check(!KeysUtil.MatchesKeyboard("K:RAlt", kb), "Enter 不匹配右 Alt");
		Native.KBDLLHOOKSTRUCT kb2 = new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 16u,
			scanCode = 42u
		};
		Check(KeysUtil.MatchesKeyboard("K:LShift", kb2), "扫描码 0x2A 判为左 Shift");
		Check(!KeysUtil.MatchesKeyboard("K:RShift", kb2), "扫描码 0x2A 不是右 Shift");
		Check(KeysUtil.MatchesKeyboard("K:RShift", new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 16u,
			scanCode = 54u
		}), "扫描码 0x36 判为右 Shift");
		Native.KBDLLHOOKSTRUCT kb3 = new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 18u,
			flags = 1u
		};
		Check(KeysUtil.MatchesKeyboard("K:RAlt", kb3), "带 extended 标志判为右 Alt");
		Check(!KeysUtil.MatchesKeyboard("K:LAlt", kb3), "带 extended 标志不是左 Alt");
		Check(KeysUtil.MatchesKeyboard("K:LCtrl", new Native.KBDLLHOOKSTRUCT
		{
			vkCode = 17u
		}), "无 extended 标志判为左 Ctrl");
		Native.MSLLHOOKSTRUCT ms = new Native.MSLLHOOKSTRUCT
		{
			mouseData = 65536u
		};
		Check(KeysUtil.MatchesMouse("M:1", 523, ms), "侧键 1 匹配 X1 按下");
		Check(!KeysUtil.MatchesMouse("M:2", 523, ms), "侧键 1 不匹配 X2");
		Check(!KeysUtil.MatchesMouse("M:1", 512, ms), "移动事件不算侧键");
	}
	private static void TestZones9()
	{
		Native.RECT r = new Native.RECT(100, 100, 500, 500);
		Eq(Zones.Resize(r, 105, 105, 26), ResizeZone.TopLeft, "左上角");
		Eq(Zones.Resize(r, 495, 105, 26), ResizeZone.TopRight, "右上角");
		Eq(Zones.Resize(r, 105, 495, 26), ResizeZone.BottomLeft, "左下角");
		Eq(Zones.Resize(r, 495, 495, 26), ResizeZone.BottomRight, "右下角");
		Eq(Zones.Resize(r, 300, 105, 26), ResizeZone.Top, "上边中段");
		Eq(Zones.Resize(r, 300, 495, 26), ResizeZone.Bottom, "下边中段");
		Eq(Zones.Resize(r, 105, 300, 26), ResizeZone.Left, "左边中段");
		Eq(Zones.Resize(r, 495, 300, 26), ResizeZone.Right, "右边中段");
		Eq(Zones.Resize(r, 300, 300, 26), ResizeZone.Center, "正中区域");
	}
	private static void TestNearestCorner()
	{
		Native.RECT r = new Native.RECT(100, 100, 500, 500);
		Eq(Zones.NearestCorner(r, 150, 150), ResizeZone.TopLeft, "中心格偏左上→左上角");
		Eq(Zones.NearestCorner(r, 450, 150), ResizeZone.TopRight, "中心格偏右上→右上角");
		Eq(Zones.NearestCorner(r, 150, 450), ResizeZone.BottomLeft, "中心格偏左下→左下角");
		Eq(Zones.NearestCorner(r, 450, 450), ResizeZone.BottomRight, "中心格偏右下→右下角");
	}
	private static void TestSnapRects()
	{
		Native.RECT work = new Native.RECT(0, 0, 1920, 1080);
		Native.RECT rECT = Zones.RectOf(SnapZone.LeftHalf, work);
		Check(rECT.Left == 0 && rECT.Top == 0 && rECT.Right == 960 && rECT.Bottom == 1080, "左半屏矩形");
		Native.RECT rECT2 = Zones.RectOf(SnapZone.TopLeftQuarter, work);
		Check(rECT2.Left == 0 && rECT2.Top == 0 && rECT2.Right == 960 && rECT2.Bottom == 540, "左上四分之一矩形");
		Native.RECT rECT3 = Zones.RectOf(SnapZone.BottomHalf, work);
		Check(rECT3.Left == 0 && rECT3.Top == 540 && rECT3.Right == 1920 && rECT3.Bottom == 1080, "下半屏矩形");
	}
	private static void TestZoneMapping()
	{
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Top), SnapZone.TopHalf, "上格→上半屏");
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Bottom), SnapZone.BottomHalf, "下格→下半屏");
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Left), SnapZone.LeftHalf, "左格→左半屏");
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.Center), SnapZone.Maximize, "中心格→最大化");
		Eq(Zones.ScreenZoneOfResizeZone(ResizeZone.BottomRight), SnapZone.BottomRightQuarter, "右下格→右下四分之一");
	}
	private static void TestNormalize()
	{
		AppConfig appConfig = new AppConfig();
		appConfig.HoldMs = 50;
		appConfig.CornerPercent = 90;
		appConfig.ActionLeft = "Bogus";
		appConfig.AutoStart = "X";
		appConfig.DragRefreshMode = "X";
		appConfig.DragRefreshHz = -3;
		appConfig.Normalize();
		Eq(appConfig.HoldMs, 100, "阈值下限 100");
		Eq(appConfig.CornerPercent, 45, "角区比例上限 45");
		Eq(appConfig.ActionLeft, "Move", "非法动作回退为移动");
		Eq(appConfig.AutoStart, "TaskScheduler", "非法自启方式回退");
		Eq(appConfig.DragRefreshMode, "Auto", "非法刷新率模式回退为自动");
		Eq(appConfig.DragRefreshHz, 0, "非法刷新率归零");
	}
	private static void TestConfigRoundTrip(string dir)
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-rt-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			configStore.Current.HoldMs = 888;
			configStore.Current.TriggerKeyId = "K:RAlt";
			configStore.Current.ActionLeft = "Resize";
			configStore.Current.ShakeMinimizeOthers = false;
			configStore.Save(out _);
			ConfigStore configStore2 = new ConfigStore(text);
			configStore2.Load();
			Eq(configStore2.Current.HoldMs, 888, "HoldMs 往返一致");
			Eq(configStore2.Current.TriggerKeyId, "K:RAlt", "触发键往返一致");
			Eq(configStore2.Current.ActionLeft, "Resize", "左键动作往返一致");
			Check(!configStore2.Current.ShakeMinimizeOthers, "布尔开关往返一致");
		}
		finally
		{
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
		}
	}
	private static void TestConfigRecovery(string dir)
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-rc-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			configStore.Current.HoldMs = 777;
			configStore.Save(out _);
			configStore.Current.HoldMs = 888;
			configStore.Save(out _);
			string text2 = Path.Combine(text, "config.json");
			Check(File.Exists(text2 + ".bak"), "备份文件已生成");
			File.WriteAllText(text2, "{ 这不是一个合法的 JSON");
			ConfigStore configStore2 = new ConfigStore(text);
			configStore2.Load();
			Eq(configStore2.Current.HoldMs, 777, "主配置损坏后从备份恢复");
			ConfigStore configStore3 = new ConfigStore(text);
			configStore3.Load();
			Eq(configStore3.Current.HoldMs, 777, "恢复结果已回写主配置");
		}
		finally
		{
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
		}
	}
	private static void TestConfigSurvivesRestart()
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-persist-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:RAlt";
			current.RequireHold = false;
			current.HoldMs = 1234;
			current.ActionLeft = "Resize";
			current.ActionRight = "AlwaysOnTop";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = false;
			current.FrameColorId = "Violet";
			current.FrameStyleId = "Dashed";
			current.FrameThickness = 7;
			current.FrameRadius = 21;
			current.ShakeMinimizeOthers = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.RestoreMaximizedWhenMoved = false;
			current.BringToFront = false;
			current.AllowFullscreenWindows = true;
			current.CornerPercent = 33;
			current.Blacklist = "Notepad - 记事本,CustomWndClass";
			current.DragRefreshMode = "Custom";
			current.DragRefreshHz = 144;
			current.AutoStart = "None";
			current.RunAsAdmin = false;
			current.WarnNonAdmin = false;
			current.ExitFullscreenOnTrigger = false;
			current.FrameOpacity = 55;
			current.SettingsWidth = 1234;
			current.SettingsHeight = 876;
			configStore.Save(out _);
			ConfigStore configStore2 = new ConfigStore(text);
			configStore2.Load();
			AppConfig current2 = configStore2.Current;
			Eq(current2.TriggerKeyId, "K:RAlt", "触发键");
			Eq(current2.HoldMs, 1234, "长按阈值");
			Check(!current2.RequireHold, "长按开关");
			Eq(current2.ActionLeft, "Resize", "左键动作");
			Eq(current2.ActionRight, "AlwaysOnTop", "右键动作");
			Eq(current2.ActionMiddle, "Nothing", "中键动作");
			Check(!current2.ShowActivationFrame, "外框总开关");
			Eq(current2.FrameColorId, current.FrameColorId, "★ 外框颜色");
			Eq(current2.FrameStyleId, current.FrameStyleId, "★ 外框样式");
			Eq(current2.FrameThickness, 7, "★ 外框粗细");
			Eq(current2.FrameRadius, 21, "★ 外框圆角");
			Check(!current2.ShakeMinimizeOthers, "晃窗最小化其它窗口开关");
			Check(!current2.DoubleClickLeftMaximize, "双击左键最大化开关");
			Check(!current2.DoubleClickRightZone, "双击右键九宫格开关");
			Check(!current2.RestoreMaximizedWhenMoved, "移动时还原最大化开关");
			Check(!current2.BringToFront, "置顶开关");
			Check(current2.AllowFullscreenWindows, "允许全屏开关");
			Eq(current2.CornerPercent, 33, "九宫格分区百分比");
			Eq(current2.Blacklist, current.Blacklist, "黑名单");
			Eq(current2.DragRefreshMode, "Custom", "刷新率模式");
			Eq(current2.DragRefreshHz, 144, "刷新率数值");
			Eq(current2.AutoStart, "None", "开机自启模式");
			Check(!current2.ExitFullscreenOnTrigger, "触发时退出全屏开关");
			Eq(current2.FrameOpacity, 55, "★ 外框不透明度");
			Check(!current2.RunAsAdmin, "管理员权限开关");
			Check(!current2.WarnNonAdmin, "非管理员提示开关");
			Eq(current2.SettingsWidth, 1234, "设置窗口宽度");
			Eq(current2.SettingsHeight, 876, "设置窗口高度");
			int width = SystemInformation.VirtualScreen.Width;
			if (width > 2400)
			{
				configStore2.Current.SettingsWidth = width;
				configStore2.Current.SettingsHeight = 1000;
				configStore2.Save(out _);
				ConfigStore configStore3 = new ConfigStore(text);
				configStore3.Load();
				Check(configStore3.Current.SettingsWidth >= width, "★ 窗口宽度上限跟随屏幕（不再写死 2400）", "屏幕宽=" + width + " 存回来=" + configStore3.Current.SettingsWidth);
			}
			else
			{
				Line("（跳过）本机屏幕宽度 " + width + " 不大于 2400，无法验证上限是否跟随屏幕");
			}
			configStore2.Current.HoldMs = 555;
			configStore2.Save(out _);
			configStore2.Current.HoldMs = 666;
			configStore2.Save(out _);
			ConfigStore configStore4 = new ConfigStore(text);
			configStore4.Load();
			Eq(configStore4.Current.HoldMs, 666, "连续多次保存后最新值仍能读回");
			Eq(configStore4.Current.FrameColorId, current.FrameColorId, "多次保存后主题颜色没被覆盖");
			File.WriteAllText(Path.Combine(text, "config.json"), "{ 坏掉的 JSON");
			ConfigStore configStore5 = new ConfigStore(text);
			configStore5.Load();
			Eq(configStore5.Current.HoldMs, 555, "主配置损坏后回退到备份而不是出厂默认");
		}
		finally
		{
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
		}
	}
	private static void TestRecordedKeyAndConfigInvariants()
	{
		Eq(KeysUtil.ToVirtualKey("K:0x00"), 0, "\"K:0x00\" 解析不出有效虚拟键码（纯修饰键被 KeyCode 掩码掩掉的产物）");
		AppConfig appConfig = new AppConfig();
		appConfig.TriggerKeyId = "K:0x00";
		appConfig.Normalize();
		Eq(appConfig.TriggerKeyId, "K:0x0D", "Normalize 兜底：无效触发键 \"K:0x00\" 回落默认回车键（录制守卫的兜底回归）");
		AppConfig appConfig2 = new AppConfig();
		appConfig2.TriggerKeyId = "M:1";
		appConfig2.Normalize();
		Eq(appConfig2.TriggerKeyId, "M:1", "Normalize 不误伤鼠标侧键 X1（vk=5）作为触发键");
		AppConfig appConfig3 = new AppConfig();
		appConfig3.TriggerKeyId = "K:LShift";
		appConfig3.Normalize();
		Eq(appConfig3.TriggerKeyId, "K:LShift", "Normalize 不误伤左 Shift（vk=160）作为触发键");
		AppConfig appConfig4 = new AppConfig();
		appConfig4.TriggerKeyId = "";
		appConfig4.Normalize();
		Eq(appConfig4.TriggerKeyId, "K:0x0D", "Normalize 兜底：空触发键同样回落默认回车键");
		Eq(Updater.QuoteArg("C:\\Program Files\\HoldDrag\\HoldDrag.exe.old"), "\"C:\\Program Files\\HoldDrag\\HoldDrag.exe.old\"", "QuoteArg：路径含空白时整段包引号（否则会被拆成多个参数）");
		Eq(Updater.QuoteArg("--no-elevate"), "--no-elevate", "QuoteArg：无空白的参数原样返回（不多包引号）");
		Eq(Updater.QuoteArg(""), "", "QuoteArg：空串原样返回");
		string composed = Updater.ArgUpdatedFrom + " " + Updater.QuoteArg("D:\\a b\\HoldDrag.exe.old");
		Eq(composed, "--updated-from \"D:\\a b\\HoldDrag.exe.old\"", "ApplyAndRestart 实际拼出的命令行：flag 不包、路径按需包");
	}
	private static void TestConfigSaveFailureIsVisible()
	{
		// 把配置目录指向一个"已存在的文件"：Directory.CreateDirectory 必然抛异常，Save 必然失败
		string blocker = Path.Combine(Path.GetTempPath(), "holddrag-blocker-" + Guid.NewGuid().ToString("N"));
		File.WriteAllText(blocker, "x");
		try
		{
			ConfigStore configStore = new ConfigStore(blocker);
			configStore.Load();
			bool saved = configStore.Save(out string error);
			Check(!saved, "保存失败时 Save 返回 false（不再静默成功）");
			Check(!string.IsNullOrEmpty(error), "保存失败时 error 带回了异常信息（设置页要原样显示给用户）");
			AppConfig appConfig = new AppConfig();
			appConfig.TriggerKeyId = "K:0x0D";
			bool applied = configStore.Apply(appConfig, out string error2);
			Check(!applied, "Apply 同样返回 false（Current 已被替换但磁盘没写成功，调用方必须知道）");
			Check(!string.IsNullOrEmpty(error2), "Apply 失败时同样带回错误信息");
		}
		finally
		{
			try
			{
				File.Delete(blocker);
			}
			catch
			{
			}
		}
		// 正对照：正常目录下必须真的保存成功（防止"永远返回 false"式的假修复）
		string dir = Path.Combine(Path.GetTempPath(), "holddrag-saveok-" + Guid.NewGuid().ToString("N"));
		try
		{
			ConfigStore okStore = new ConfigStore(dir);
			okStore.Load();
			AppConfig okConfig = new AppConfig();
			okConfig.TriggerKeyId = "K:0x0D";
			bool ok = okStore.Apply(okConfig, out string okError);
			Check(ok, "正常目录下 Apply 返回 true（保存失败路径的对照）");
			Check(File.Exists(okStore.FilePath), "正常目录下配置文件真的落盘了");
			Check(string.IsNullOrEmpty(okError), "保存成功时 error 为空串");
		}
		finally
		{
			try
			{
				Directory.Delete(dir, recursive: true);
			}
			catch
			{
			}
		}
	}
	private static void TestGitHubTokenHandling()
	{
		Eq(Updater.NormalizeToken(null), string.Empty, "令牌：null 归一成空串");
		Eq(Updater.NormalizeToken("   "), string.Empty, "令牌：纯空白归一成空串");
		Eq(Updater.NormalizeToken("  ghp_abc123  "), "ghp_abc123", "令牌：去掉首尾空白");
		Eq(Updater.NormalizeToken("Bearer ghp_x"), "ghp_x", "令牌：去掉 Bearer 前缀");
		Eq(Updater.NormalizeToken("token ghp_x"), "ghp_x", "令牌：去掉 token 前缀");
		Eq(Updater.NormalizeToken("QQQOrange:ghp_x"), "ghp_x", "令牌：去掉历史「用户名:」前缀（Obtainium 老格式）");
		Eq(Updater.NormalizeToken("ghp_a\r\nb\tc"), "ghp_abc", "令牌：粘贴带入的换行/制表符被清掉");
		Check(Updater.AuthHeaderValue(string.Empty) == null, "空令牌不产生 Authorization 头（走匿名请求）");
		Eq(Updater.AuthHeaderValue("ghp_x"), "token ghp_x", "有令牌时 Authorization 头是 token 前缀");
		Eq(Updater.MaskToken("ghp_1234567890"), "ghp_…7890", "脱敏只留前 4 后 4");
		Eq(Updater.MaskToken(string.Empty), "（未填写）", "空令牌的脱敏文案");
		Check(Updater.IsAuthRejection(401, "{\"message\":\"Bad credentials\"}"), "401 + Bad credentials 判为令牌问题（改走匿名）");
		Check(Updater.IsAuthRejection(403, "{\"message\":\"Resource not accessible by personal access token\"}"), "403 + access token 判为令牌问题");
		Check(!Updater.IsAuthRejection(403, "{\"message\":\"API rate limit exceeded\"}"), "★ 限额 403 不能被误判成令牌问题（否则会白折腾用户去改令牌）");
		Check(!Updater.IsAuthRejection(404, "{\"message\":\"Not Found\"}"), "404 不是令牌问题");
		Check(Updater.FormatRateLimitMessage(3, withToken: true).Contains("5000") && Updater.FormatRateLimitMessage(3, withToken: true).Contains("3 分钟"), "限额说明：带令牌时提到 5000 次/小时与重置分钟数");
		Check(Updater.FormatRateLimitMessage(3, withToken: false).Contains("60"), "限额说明：匿名时提到 60 次/小时");
		AppConfig appConfig = new AppConfig();
		appConfig.GitHubToken = "  QQQOrange:ghp_legacy  ";
		appConfig.Normalize();
		Eq(appConfig.GitHubToken, "ghp_legacy", "Normalize 会把历史格式的令牌整理干净");
		Eq(new AppConfig().GitHubToken, string.Empty, "默认配置没有令牌");
		// 配置往返：令牌必须存得进、读得回（重启后仍生效）
		string text = Path.Combine(Path.GetTempPath(), "holddrag-token-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig appConfig2 = configStore.Current;
			appConfig2.GitHubToken = "ghp_roundtrip123";
			configStore.Save(out _);
			ConfigStore configStore2 = new ConfigStore(text);
			configStore2.Load();
			Eq(configStore2.Current.GitHubToken, "ghp_roundtrip123", "★ 令牌存进配置后读得回来（重启仍生效）");
		}
		finally
		{
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
		}
		// 决策：没令牌只提示（给下载链接），有令牌才自动下载
		Eq(SettingsForm.DecideUpdateAction(hasToken: false), SettingsForm.UpdateAction.PromptOnly, "★ 没有令牌时这条纯函数仍返回 PromptOnly（v1.9 起真正点「立即下载」才下载，不再由它拦截）");
		Eq(SettingsForm.DecideUpdateAction(hasToken: true), SettingsForm.UpdateAction.AutoDownload, "有令牌：照旧自动下载并替换");
		string text3 = SettingsForm.BuildNoTokenNotice();
		Check(text3.Contains("不会下载到本机"), "★ 没令牌的提示正文明确写「不会下载到本机」（用户一眼看懂程序内不下载）", text3);
		Check(text3.Contains("浏览器"), "★ 没令牌的提示正文指明去浏览器下载", text3);
		Check(text3.Contains("令牌"), "提示正文说明了补令牌后可自动下载");
		// --checkupdate 输出：说明必须与三分支并存（v1.7.0 曾把说明写成互斥分支，把结果整段吞掉）
		UpdateInfo updateInfo = new UpdateInfo
		{
			Tag = "v9.9.9",
			Version = "9.9.9",
			AssetUrl = "https://example.invalid/HoldDrag.exe",
			PageUrl = "https://example.invalid/tag/v9.9.9"
		};
		string text4 = Program.DescribeCheckUpdate(updateInfo, upToDate: false, error: "", note: "令牌无效，已改用匿名方式检查", hasToken: false);
		Check(text4.Contains("说明") && text4.Contains("发现新版本") && text4.Contains("下载地址"), "★ --checkupdate：有说明时仍要报「发现新版本 + 下载地址」（说明吞掉结果的回归）", text4);
		Check(text4.Contains("不会下载"), "★ --checkupdate：未填令牌时明确提示「程序内不会下载」", text4);
		string text5 = Program.DescribeCheckUpdate(null, upToDate: true, error: "", note: "限额已用尽，已改用网页方式检查", hasToken: true);
		Check(text5.Contains("说明") && text5.Contains("已是最新版本"), "★ --checkupdate：有说明时仍要报「已是最新版本」", text5);
		string text6 = Program.DescribeCheckUpdate(null, upToDate: false, error: "网络不可达", note: "令牌无效", hasToken: false);
		Check(text6.Contains("说明") && text6.Contains("检查失败") && text6.Contains("网络不可达"), "★ --checkupdate：有说明时仍要报「检查失败」（否则退出码会误报成功）", text6);
		// 「验证令牌」：结果分类（真实网络没法在自检里跑，所以打表断言分类函数）
		Eq(Updater.ClassifyVerifyResponse(200, "{\"tag_name\":\"v1.0.0\"}", rateLimited: false), Updater.TokenCheckResult.Valid, "验证：200 → 令牌有效");
		Eq(Updater.ClassifyVerifyResponse(401, "{\"message\":\"Bad credentials\"}", rateLimited: false), Updater.TokenCheckResult.Invalid, "验证：401 → 令牌无效/过期");
		Eq(Updater.ClassifyVerifyResponse(403, "{\"message\":\"Resource not accessible by personal access token\"}", rateLimited: false), Updater.TokenCheckResult.Invalid, "验证：403 + access token 正文 → 令牌问题");
		Eq(Updater.ClassifyVerifyResponse(403, "{\"message\":\"API rate limit exceeded\"}", rateLimited: true), Updater.TokenCheckResult.RateLimited, "★ 验证：限额 403（remaining=0）→ 判为限额而不是令牌无效");
		Eq(Updater.ClassifyVerifyResponse(403, "{\"message\":\"Forbidden\"}", rateLimited: false), Updater.TokenCheckResult.NoAccess, "验证：403 无权限 → 令牌有效但读不到仓库");
		Eq(Updater.ClassifyVerifyResponse(404, "{\"message\":\"Not Found\"}", rateLimited: false), Updater.TokenCheckResult.NoAccess, "验证：404 → 令牌有效但仓库不可见（fine-grained 没勾仓库）");
		Eq(Updater.ClassifyVerifyResponse(500, "", rateLimited: false), Updater.TokenCheckResult.Network, "验证：5xx → 服务端异常，判不了令牌好坏");
		string text7;
		Eq(Updater.VerifyToken("   ", out text7), Updater.TokenCheckResult.NotConfigured, "验证：空令牌直接判「没填」，不发请求");
		Check(text7.Contains("令牌"), "验证：空令牌的理由可读", text7);
		string text2 = Updater.Token;
		try
		{
			Updater.Token = string.Empty;
			Check(!Updater.HasToken, "Updater.HasToken 在令牌为空时为 false");
			Updater.Token = "ghp_probe";
			Check(Updater.HasToken, "Updater.HasToken 在令牌非空时为 true");
		}
		finally
		{
			Updater.Token = text2;
		}
	}
}
