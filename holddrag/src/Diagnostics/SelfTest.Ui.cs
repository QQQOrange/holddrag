// HoldDrag · Diagnostics/SelfTest.Ui.cs

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
	private static void TestSettingsLayout()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		int num = SettingsForm.ProbeGroupCount(settingsForm);
		for (int i = 0; i < num; i++)
		{
			Control control = SettingsForm.ProbeGroupAt(settingsForm, i);
			Check(control != null && control.Controls.Count > 0, "卡片「" + (control?.Text ?? "(null)") + "」里有控件（不是空卡）");
		}
		Check(num >= 6 && SettingsForm.VerifyLayout(settingsForm), "设置窗体上卡片数 >= 6，实际 " + num);

		// 卡里的文字必须放得下：Label 高度按"实际宽度折行后需要的高度"来比，
		// 半行被裁（比如"流畅度 → 固定值"下面那段说明）就是这里会报红。
		StringBuilder clipped = new StringBuilder();
		int numLabel = 0;
		for (int j = 0; j < num; j++)
		{
			Control card = SettingsForm.ProbeGroupAt(settingsForm, j);
			if (card == null)
			{
				continue;
			}
			for (int k = 0; k < card.Controls.Count; k++)
			{
				Label label = card.Controls[k] as Label;
				if (label == null || string.IsNullOrEmpty(label.Text) || label.Width <= 0)
				{
					continue;
				}
				numLabel++;
				int num8x = SettingsForm.TextLinesForTest(label.Text, label, label.Width);
					// 行高按设计稿的 18 逻辑像素算（Font.Height 是设备像素，跨 DPI 不可比）
					int num9x = num8x * (int)Math.Round(18f * SettingsForm.UiScaleForTest);
				if (num9x > label.Height + (int)Math.Round(4f * SettingsForm.UiScaleForTest))
				{
					clipped.Append("「").Append(HoldDragText(label.Text, 10)).Append("」需要 ").Append(num8x).Append(" 行(").Append(num9x).Append(") 实际 ").Append(label.Height).Append("；");
				}
			}
		}
		Check(clipped.Length == 0, "★ 卡片里每条文字都放得下（不会被裁成半行）", "检查 " + numLabel + " 个标签：" + clipped);
	}
	private static void TestSettingsResizable()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		Check(SettingsForm.VerifyResizable(settingsForm), "设置窗口可缩放（边框 Sizable、最大化按钮可用、最小尺寸有意义）", "BorderStyle=" + settingsForm.FormBorderStyle.ToString() + " Max=" + settingsForm.MaximizeBox + " Min=" + settingsForm.MinimumSize);
		Check(SettingsForm.VerifyAnchors(settingsForm), "拉宽后卡片跟着变宽（单列、最宽 880 左对齐）");
		AppConfig appConfig = new AppConfig();
		Eq(appConfig.SettingsWidth, 0, "默认不记住窗口宽度（0 = 用内置默认）");
		Eq(appConfig.SettingsHeight, 0, "默认不记住窗口高度");
		appConfig.SettingsWidth = 100;
		appConfig.SettingsHeight = 100;
		appConfig.Normalize();
		Eq(appConfig.SettingsWidth, 578, "过小的记忆宽度被拉回最小尺寸");
		Eq(appConfig.SettingsHeight, 420, "过小的记忆高度被拉回最小尺寸");
		Rectangle virtualScreen = SystemInformation.VirtualScreen;
		int expected = ((virtualScreen.Width >= 640) ? virtualScreen.Width : 2400);
		int expected2 = ((virtualScreen.Height >= 480) ? virtualScreen.Height : 2400);
		appConfig.SettingsWidth = 99999;
		appConfig.SettingsHeight = 99999;
		appConfig.Normalize();
		Eq(appConfig.SettingsWidth, expected, "离谱的记忆宽度被夹到屏幕上限（否则窗口会跑到屏幕外）");
		Eq(appConfig.SettingsHeight, expected2, "离谱的记忆高度被夹到屏幕上限");
	}
	private static void TestVisualGroupGeometry()
	{
		int[] designWidths = new int[5] { 736, 900, 1180, 1516, 1900 };
		StringBuilder stringBuilder = new StringBuilder();
		SettingsForm.DumpVisualGroupGeometry(stringBuilder, designWidths);
		Line(stringBuilder.ToString());
		string text = stringBuilder.ToString();
		int num = 0;
		int num2 = 0;
		string text2 = "";
		Regex regex = new Regex("客户区宽=(\\d+)（设计稿 (\\d+)px）════");
		Regex regex2 = new Regex("z=(\\d+)\\s+(\\w+)\\s+vis=(\\w+)\\s+L=(-?\\d+)\\s+T=(-?\\d+)\\s+W=(-?\\d+)\\s+H=(-?\\d+)\\s+右=(-?\\d+)\\s+底=(-?\\d+)");
		List<Match> list = new List<Match>();
		foreach (Match item in regex.Matches(text))
		{
			list.Add(item);
		}
		for (int i = 0; i < list.Count; i++)
		{
			Match match = list[i];
			int num3 = int.Parse(match.Groups[1].Value);
			int num4 = int.Parse(match.Groups[2].Value);
			float num5 = (float)num3 / (float)num4;
			int num6 = match.Index + match.Length;
			int num7 = ((i + 1 < list.Count) ? list[i + 1].Index : text.Length);
			if (num7 <= num6)
			{
				continue;
			}
			string input = text.Substring(num6, num7 - num6);
			int num8 = (int)Math.Round(86.4 * (double)num5);
			foreach (Match item2 in regex2.Matches(input))
			{
				if (!(item2.Groups[2].Value != "FramePreviewBox"))
				{
					int num9 = int.Parse(item2.Groups[4].Value);
					int num10 = int.Parse(item2.Groups[6].Value);
					int num11 = int.Parse(item2.Groups[8].Value);
					num2++;
					if (num10 >= num8 && num9 >= 0 && num11 > num9)
					{
						num++;
						continue;
					}
					text2 = text2 + " 第" + (i + 1) + "档(scale=" + num5.ToString("0.##") + ") L=" + num9 + " W=" + num10 + " R=" + num11 + " 最小应≥" + num8;
				}
			}
		}
		Check(num2 > 0, "每一档宽度都转到了预览框几何（探针本身有效）", "共 " + num2 + " 档");
		Check(num == num2, "★ 所有宽度档位下样式预览框都保持可见尺寸（拉宽后不消失）", num + "/" + num2 + text2);
	}
	private static void TestSettingsThemeSelectionRoundTrip()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Eq(settingsForm.ProbeFrameSelection().ColorId, "Teal", "打开设置时，颜色下拉框读回的默认 ID 是薄荷青");
		settingsForm.ProbeSelectFrameColor("Red");
		SettingsForm.FrameSelection frameSelection = settingsForm.ProbeFrameSelection();
		Eq(frameSelection.ColorId, "Red", "★ 在下拉框里选「朱红」之后，读回的 ID 必须是 Red（不能回退成薄荷青）");
		Eq(frameSelection.PreviewColorId, "Red", "★ 预览框内部状态同步成了朱红（否则预览永远显示薄荷青）");
		settingsForm.ProbeSelectFrameStyle("Focus");
		SettingsForm.FrameSelection frameSelection2 = settingsForm.ProbeFrameSelection();
		Eq(frameSelection2.StyleId, "Focus", "换个样式，读回的 ID 跟着换");
		Eq(frameSelection2.PreviewStyleId, "Focus", "预览框的样式也同步了");
		settingsForm.ProbeSetFrameShape(7, 22, enabled: true);
		SettingsForm.FrameSelection frameSelection3 = settingsForm.ProbeFrameSelection();
		Eq(frameSelection3.Thickness, 7, "粗细读回 7");
		Eq(frameSelection3.PreviewThickness, 7, "预览框的粗细同步成 7");
		Eq(frameSelection3.Radius, 22, "圆角读回 22");
		Eq(frameSelection3.PreviewRadius, 22, "预览框的圆角同步成 22");
		Check(frameSelection3.FrameEnabled && frameSelection3.PreviewFrameEnabled, "总开关与预览框保持一致（开）");
		settingsForm.ProbeSetFrameShape(7, 22, enabled: false);
		SettingsForm.FrameSelection frameSelection4 = settingsForm.ProbeFrameSelection();
		Check(!frameSelection4.FrameEnabled && !frameSelection4.PreviewFrameEnabled, "关掉总开关后，预览框也跟着变成「不画框」的状态");
		int num = 0;
		string text = "";
		for (int i = 0; i < FrameColors.All.Length; i++)
		{
			string id = FrameColors.All[i].Id;
			settingsForm.ProbeSelectFrameColor(id);
			SettingsForm.FrameSelection frameSelection5 = settingsForm.ProbeFrameSelection();
			if (frameSelection5.ColorId != id)
			{
				num++;
				text = text + id + "→" + frameSelection5.ColorId + " ";
			}
			if (frameSelection5.PreviewColorId != id)
			{
				num++;
				text = text + id + "(预览)→" + frameSelection5.PreviewColorId + " ";
			}
		}
		Check(num == 0, "★ 调色板里每一个颜色都能正确选中并同步到预览", (num == 0) ? "" : text);
		settingsForm.ProbeSelectFrameColor("Violet");
		settingsForm.ProbeSelectFrameStyle("Dashed");
		settingsForm.ProbeSetFrameShape(4, 9, enabled: true);
		AppConfig appConfig = new AppConfig();
		settingsForm.ProbeCaptureInto(appConfig);
		appConfig.Normalize();
		Eq(appConfig.FrameColorId, "Violet", "★ 保存进配置的颜色是紫罗兰（不是薄荷青）");
		Eq(appConfig.FrameStyleId, "Dashed", "保存进配置的样式是虚线环");
		Eq(appConfig.FrameThickness, 4, "保存进配置的粗细是 4");
		Eq(appConfig.FrameRadius, 9, "保存进配置的圆角是 9");
		Check(appConfig.ShowActivationFrame, "保存进配置的总开关是开");
		settingsForm.ProbeSetFrameShape(4, 9, enabled: false);
		AppConfig appConfig2 = new AppConfig();
		settingsForm.ProbeCaptureInto(appConfig2);
		appConfig2.Normalize();
		Check(!appConfig2.ShowActivationFrame, "关掉外框后保存，配置里确实是关");
		settingsForm.ProbeSelectFrameColor("这不是一个颜色");
		SettingsForm.FrameSelection frameSelection6 = settingsForm.ProbeFrameSelection();
		Check(!string.IsNullOrEmpty(frameSelection6.ColorId), "传入非法颜色 ID 时不崩（回退到某个合法项）", "读回=" + frameSelection6.ColorId);
	}
	private static void TestSettingsResizeIsMeaningful()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		Check(SettingsForm.VerifyAnchors(settingsForm), "★ 拉宽窗口卡片跟着变宽、回窄能恢复，且不超过 880（内容列限宽、左对齐）", SettingsForm.LastStretchDetail);
		Check(SettingsForm.VerifyInnerStretch(settingsForm), "★ 拉宽后卡内整宽块跟着变宽、固定宽控件保持宽度并右对齐（不是只把卡片撑大）", SettingsForm.LastStretchDetail + " || " + SettingsForm.DumpLayoutItem(settingsForm, settingsForm.ProbeComboLeft));
		Check(SettingsForm.VerifyStretchIdempotent(settingsForm), "★ 来回拉多次，控件位置稳定不漂移（伸缩是幂等的）");
		// 底栏现在是"浮在底部的圆角卡片"，不再靠 Dock=Bottom：改查它的实际位置是否贴在窗口下沿
		Rectangle pagerBounds = settingsForm.ProbePagerBounds();
		bool ok = pagerBounds.Height > 0 && pagerBounds.Bottom >= settingsForm.ClientSize.Height - settingsForm.ClientSize.Height / 20 && pagerBounds.Right >= settingsForm.ClientSize.Width - settingsForm.ClientSize.Width / 20;
		Check(ok, "底部操作条固定在窗口下方（不随内容滚走）", pagerBounds.ToString() + " 客户区=" + settingsForm.ClientSize.ToString());
		settingsForm.Close();
	}
	private static void TestSettingsControlsReachable()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.Size = settingsForm.Size;
		Application.DoEvents();
		int pageCount = SettingsForm.ProbePageCount(settingsForm);
		Check(pageCount >= 6, "设置页分页数（至少包含关于 / 帮助）", "实际 " + pageCount);
		StringBuilder hidden = new StringBuilder();
		StringBuilder dead = new StringBuilder();
		int interactive = 0;
		int checkedCount = 0;
		// 新 UI 是分页的：只有当前页的控件可见，其余页整体隐藏。
		// 因此逐页切过去检查：当前页内不允许有看不见 / 尺寸为 0 / 掉出容器的控件。
		for (int p = 0; p < pageCount; p++)
		{
			SettingsForm.ProbeSelectPage(settingsForm, p);
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
			Walk(settingsForm, delegate(Control c)
			{
				if (!SettingsForm.ProbeOnActivePage(settingsForm, c))
				{
					return;
				}
				checkedCount++;
				if (!c.Visible)
				{
					hidden.Append(Name(c)).Append(' ');
				}
				else if (c.Width <= 0 || c.Height <= 0)
				{
					dead.Append(Name(c)).Append("(尺寸0) ");
				}
				else if (c is CheckBox || c is TextBox || c is ComboBox || c is Button || c is NumericUpDown)
				{
					interactive++;
					if (c.Enabled)
					{
						Control parent = c.Parent;
						if (parent != null)
						{
							Rectangle clientRectangle = parent.ClientRectangle;
							Rectangle rect = new Rectangle(c.Left, c.Top, c.Width, c.Height);
							if (clientRectangle.Width > 0 && !clientRectangle.IntersectsWith(rect))
							{
								dead.Append(Name(c)).Append("(在容器外 ")
									.Append("L=").Append(rect.Left)
									.Append(" T=").Append(rect.Top)
									.Append(" W=").Append(rect.Width)
									.Append(" H=").Append(rect.Height)
									.Append(" 容器=").Append(clientRectangle.Width)
									.Append("x").Append(clientRectangle.Height)
									.Append(") ");
							}
						}
					}
				}
			});
		}
		Check(hidden.Length == 0, "设置窗口当前页没有【看不见】的控件（分组/卡片化之后可见性回归）", (hidden.Length == 0) ? (checkedCount + " 个控件全部可见") : ("不可见: " + hidden));
		Check(dead.Length == 0, "★ 设置窗口所有可交互控件都在容器内、尺寸正常（自绘后功能失效回归）", (dead.Length == 0) ? (interactive + " 个可交互控件全部可达") : ("异常: " + dead));
		settingsForm.Close();
	}
	private static void TestHoverFrameDoesNotBlockDrag()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		string text = Path.Combine(Path.GetTempPath(), "holddrag-hover-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(text);
		WindowActions.ForcedTargetForTest = IntPtr.Zero;
		WindowActions.AllowOwnProcessAsTargetForTest = false;
		Form form = null;
		Coordinator coordinator = null;
		try
		{
			ConfigStore configStore = new ConfigStore(text);
			configStore.Load();
			AppConfig current = configStore.Current;
			current.TriggerKeyId = "K:0x0D";
			current.RequireHold = true;
			current.HoldMs = 400;
			current.ActionLeft = "Move";
			current.ActionRight = "Resize";
			current.ActionMiddle = "Nothing";
			current.ShowActivationFrame = true;
			current.BringToFront = false;
			current.DoubleClickLeftMaximize = false;
			current.DoubleClickRightZone = false;
			current.AllowFullscreenWindows = true;
			form = new Form
			{
				FormBorderStyle = FormBorderStyle.Sizable,
				StartPosition = FormStartPosition.Manual,
				ShowInTaskbar = false,
				Text = "HoldDrag T39 target",
				TopMost = true
			};
			Native.GetCursorPos(out var lpPoint);
			Native.RECT rECT = WorkAreaOfNearestMonitor(lpPoint);
			int num = 380;
			int num2 = 260;
			int num3 = rECT.Right - num - 8;
			int num4 = rECT.Bottom - num2 - 8;
			if (num3 < rECT.Left)
			{
				num3 = rECT.Left + 8;
			}
			if (num4 < rECT.Top)
			{
				num4 = rECT.Top + 8;
			}
			form.Bounds = new Rectangle(num3, num4, num, num2);
			form.Show();
			Pump();
			Settle();
			WindowActions.SetForeignForTest(form.Handle, on: true);
			coordinator = new Coordinator(configStore);
			coordinator.PrecreateOverlays();
			Settle();
			Native.POINT pOINT = default;
			bool flag = false;
			for (int i = 0; i < 4; i++)
			{
				if (flag)
				{
					break;
				}
				if (i > 0)
				{
					form.Left = Math.Max(rECT.Left + 8, form.Left - 420);
					form.Top = Math.Max(rECT.Top + 8, form.Top - 260);
				}
				try
				{
					form.BringToFront();
					form.Activate();
				}
				catch
				{
				}
				Pump();
				Settle();
				pOINT = CenterOf(form.Handle);
				Native.SetCursorPos(pOINT.X, pOINT.Y);
				Settle();
				nint num5 = Native.WindowFromPoint(pOINT);
				flag = Native.GetAncestor(num5, 2u) == form.Handle;
				if (!flag)
				{
					Line("    （诊断）落点 " + i + " 顶层不是测试窗口 -> " + DescribeWindow(num5));
				}
			}
			if (!flag)
			{
				Line("（环境限制）桌面上有别的窗口始终压在测试窗口上方，无法做真实命中测试，跳过。");
				Line("参考：T39 的核心价值是『外框亮着仍能接管窗口』，其中拖动能否启动由 coord.OnMouse 断言覆盖，不依赖真实命中。");
				Line("另见 T38（控件可见可点）与 T43（真实鼠标事件走完整钩子链路）。");
				return;
			}
			nint num6 = WindowActions.FindTarget(pOINT, allowFullscreen: true);
			if (num6 != form.Handle)
			{
				Line("    （诊断）基线未命中目标 -> " + DescribeWindow(num6));
			}
			Check(num6 == form.Handle, "基线：外框未亮时 FindTarget 命中测试窗口（环境正常）", "实际 " + ((IntPtr)num6).ToInt64().ToString("X") + " 期望 " + ((IntPtr)form.Handle).ToInt64().ToString("X"));
			ActivateTrigger(coordinator);
			Settle();
			Check(coordinator.Trigger.Active, "触发键已进入激活态");
			Check(Native.IsWindowVisible(coordinator.Drag.Frame.Handle), "激活后外框确实亮着（浮层已压在目标窗口上）");
			nint num7 = Native.WindowFromPoint(pOINT);
			bool flag2 = num7 != IntPtr.Zero && num7 != form.Handle;
			Line("    （诊断）此刻 WindowFromPoint=" + ((IntPtr)num7).ToInt64().ToString("X") + " 目标=" + ((IntPtr)form.Handle).ToInt64().ToString("X") + " 浮层=" + ((IntPtr)coordinator.Drag.Frame.Handle).ToInt64().ToString("X") + (flag2 ? " ←命中的是浮层，已被穿透" : " ←命中的是目标窗口"));
			nint num8 = WindowActions.FindTarget(pOINT, allowFullscreen: true);
			Check(num8 == form.Handle, "★ 外框亮着时 FindTarget 依然命中目标窗口（浮层不再挡路）", "实际 " + ((IntPtr)num8).ToInt64().ToString("X") + " 期望 " + ((IntPtr)form.Handle).ToInt64().ToString("X"));
			Native.MSLLHOOKSTRUCT ms = Ms(pOINT);
			bool ok = coordinator.OnMouse(513, ref ms);
			Settle();
			Check(ok, "外框亮着时左键按下被吞掉（不会漏给目标窗口）", "swallowed=" + ok);
			Check(coordinator.Drag.IsDragging, "★ 外框亮着时左键依然能启动拖动会话（核心功能失效回归）", "IsDragging=" + coordinator.Drag.IsDragging);
			Native.MSLLHOOKSTRUCT ms2 = Ms(pOINT);
			coordinator.OnMouse(514, ref ms2);
			Settle();
			ActivateTrigger(coordinator);
			Settle();
			Native.MSLLHOOKSTRUCT ms3 = Ms(pOINT);
			coordinator.OnMouse(516, ref ms3);
			Settle();
			Check(coordinator.Drag.IsDragging && coordinator.Drag.Button == 1, "★ 外框亮着时右键依然能启动缩放会话", "IsDragging=" + coordinator.Drag.IsDragging + " Button=" + coordinator.Drag.Button);
			Native.MSLLHOOKSTRUCT ms4 = Ms(pOINT);
			coordinator.OnMouse(517, ref ms4);
			Settle();
			coordinator.Trigger.SimulateKeyUp(null);
			Settle();
			Check(!coordinator.Trigger.Active, "触发键已退出激活态");
			Check(!Native.IsWindowVisible(coordinator.Drag.Frame.Handle), "松开触发键后外框立即消失（外框残留回归）");
		}
		finally
		{
			try
			{
				if (form != null)
				{
					WindowActions.SetForeignForTest(form.Handle, on: false);
				}
			}
			catch
			{
			}
			try
			{
				coordinator?.Dispose();
			}
			catch
			{
			}
			try
			{
				if (form != null && !form.IsDisposed)
				{
					form.Visible = false;
					Native.DestroyWindow(form.Handle);
				}
				if (form != null)
				{
					form.Close();
					form.Dispose();
				}
			}
			catch
			{
			}
			for (int j = 0; j < 6; j++)
			{
				Application.DoEvents();
				Thread.Sleep(5);
			}
			try
			{
				Directory.Delete(text, recursive: true);
			}
			catch
			{
			}
			try
			{
				Native.SetCursorPos(400, 400);
			}
			catch
			{
			}
		}
	}
	private static void TestNoTextClippedByHeight()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.Size = settingsForm.Size;
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		StringBuilder clipped = new StringBuilder();
		int measured = 0;
		Walk(settingsForm, (Control c) =>
		{
			if (c.Visible)
			{
				string text = null;
				try
				{
					text = c.Text;
				}
				catch
				{
				}
				// v1.4.6：NumericUpDown 的行高由系统 PreferredHeight 锁定（Height 赋值会被覆盖），
				// 它的文字能否装下不由本程序控制，纳入本断言只会产生无法修复的假红。
				// 与之配套的 10111 行同样把它归为"系统原生控件"。
				if (!string.IsNullOrEmpty(text) && !(text == "请按下要用的按键…") && !(c is TextBoxBase) && !(c is ComboBox) && !(c is Button) && !(c is NumericUpDown))
				{
					int num;
					try
					{
						using Graphics graphics = c.CreateGraphics();
						num = (int)Math.Ceiling(graphics.MeasureString(text, c.Font, int.MaxValue, StringFormat.GenericTypographic).Height * 1.28f);
					}
					catch
					{
						return;
					}
					measured++;
					if (num > c.Height + 2)
					{
						clipped.Append(Name(c)).Append("(字高").Append(num)
							.Append(" > 框高")
							.Append(c.Height)
							.Append(") ");
					}
				}
			}
		});
		Check(clipped.Length == 0, "★ 高 DPI 下所有文字都装得进自己的控件（字号被放大两次回归）", (clipped.Length == 0) ? (measured + " 个控件文字全部完整显示") : ("被裁切: " + clipped));
		settingsForm.Close();
	}

	/// <summary>
	/// 换颜色必须真的生效（v1.11.2 回归锁）。
	///
	/// 自绘下拉框最初是在 MouseDown 里弹层的：弹层刚 Show 出来就被自己的 Deactivate 关掉，
	/// 用户看到的就是「点了没反应、颜色换不了」。这个用例锁两条真实路径：
	/// 色板点一下、下拉框弹层里选一项。
	/// </summary>
	private static void TestFrameColorSwitch()
	{
		using DragFixture dragFixture = new DragFixture(delegate(AppConfig c)
		{
			c.FrameStyleId = "Aurora";
			c.FrameColorId = "Teal";
			c.ShowActivationFrame = true;
		});
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		settingsForm.ProbeSelectPage(2);
		Application.DoEvents();
		string before = settingsForm.ProbeFrameSelection().ColorId;
		Check(!string.IsNullOrEmpty(before), "初始外框颜色非空", "值=" + before);
		// 找一个和当前不同的色块
		int pick = -1;
		string pickId = null;
		for (int i = 0; i < 16; i++)
		{
			string id = settingsForm.ProbeSwatchId(i);
			if (!string.IsNullOrEmpty(id) && id != before)
			{
				pick = i;
				pickId = id;
				break;
			}
		}
		Check(pick >= 0, "色板里有可切换的颜色", "当前=" + before);
		if (pick >= 0)
		{
			string now = settingsForm.ProbePickSwatch(pick);
			Application.DoEvents();
			Check(now == pickId, "点色板后选中颜色变了", "期望=" + pickId + " 实际=" + now);
			SettingsForm.FrameSelection sel = settingsForm.ProbeFrameSelection();
			Check(sel.ColorId == pickId, "★ 点色板后外框颜色配置跟着变（颜色换不了的回归）", "配置=" + sel.ColorId + " 期望=" + pickId);
			Check(sel.PreviewColorId == pickId, "★ 预览框同步到新颜色", "预览=" + sel.PreviewColorId);
		}
		// 下拉框：弹层要能打开、能选中
		settingsForm.ProbeOpenColorPopup();
		Application.DoEvents();
		Rectangle bounds = settingsForm.ProbeColorPopupBounds();
		Check(bounds.Width > 20 && bounds.Height > 20, "★ 外框颜色下拉框的弹层真的打开了（点了没反应的回归）", "bounds=" + bounds);
		bool chosen = settingsForm.ProbeChooseColorInPopup(1);
		Application.DoEvents();
		Check(chosen, "弹层里能选中一项");
		if (chosen)
		{
			SettingsForm.FrameSelection sel2 = settingsForm.ProbeFrameSelection();
			Check(!string.IsNullOrEmpty(sel2.ColorId), "弹层选完后颜色配置有效", "值=" + sel2.ColorId);
			Check(sel2.PreviewColorId == sel2.ColorId, "弹层选完后预览同步", "预览=" + sel2.PreviewColorId + " 配置=" + sel2.ColorId);
		}
		settingsForm.ProbeCloseColorPopup();
		Application.DoEvents();
	}

	private static void TestKeyCombosHaveSelection()
	{
		using DragFixture dragFixture = new DragFixture((AppConfig c) =>
		{
			c.FrameStyleId = "Dashed";
			c.FrameColorId = "Mint";
			c.DragRefreshMode = "Custom";
			c.AutoStart = "None";
			c.ActionLeft = "Move";
			c.ActionRight = "Resize";
		});
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.Size = settingsForm.Size;
		Application.DoEvents();
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		StringBuilder bad = new StringBuilder();
		int comboCount = 0;
		Walk(settingsForm, (Control c) =>
		{
			if (c is ComboBox comboBox)
			{
				comboCount++;
				int selectedIndex = comboBox.SelectedIndex;
				if (selectedIndex < 0 || comboBox.SelectedItem == null)
				{
					bad.Append(Name(c)).Append("(SelectedIndex=").Append(selectedIndex)
						.Append(" 项数=")
						.Append(comboBox.Items.Count)
						.Append(" 框里显示=\"")
						.Append(comboBox.Text)
						.Append("\") ");
				}
				else if (string.IsNullOrEmpty(comboBox.SelectedItem.ToString()))
				{
					bad.Append(Name(c)).Append("(选中项文字为空) ");
				}
			}
		});
		Check(bad.Length == 0, "★ 所有下拉框都有可见的选中项（外框样式显示为空回归）", (bad.Length == 0) ? (comboCount + " 个下拉框全部有选中项") : ("异常: " + bad));
		SettingsForm.PopulateForTest(settingsForm);
		Application.DoEvents();
		StringBuilder bad2 = new StringBuilder();
		Walk(settingsForm, (Control c) =>
		{
			if (c is ComboBox { SelectedIndex: <0 } comboBox)
			{
				bad2.Append(Name(c)).Append("(恢复默认后 SelectedIndex=-1 项数=").Append(comboBox.Items.Count)
					.Append(") ");
			}
		});
		Check(bad2.Length == 0, "★ 恢复默认值之后下拉框依然都有选中项", (bad2.Length == 0) ? "全部下拉框正常" : ("异常: " + bad2));
		settingsForm.Close();
	}
	private static void TestThemeAppliesImmediately()
	{
		using ActivationFrame activationFrame = new ActivationFrame();
		activationFrame.EnsureCreated();
		if (!activationFrame.IsCreated)
		{
			Check(ok: false, "外框窗口创建成功", "CreateWindowEx 失败");
			return;
		}
		Native.RECT rECT = new Native.RECT(200, 160, 560, 440);
		activationFrame.ApplyTheme("Teal", "Focus", 6, 6);
		activationFrame.SetRect(rECT);
		Settle();
		Check(Native.IsWindowVisible(activationFrame.Handle), "浮层已显示");
		Color color = FrameColors.Resolve("Teal");
		Color color2 = FrameColors.Resolve("Red");
		Color? color3 = CaptureCornerPixel(activationFrame.Handle, rECT, color, color2);
		activationFrame.ApplyTheme("Red", "Focus", 6, 6);
		Settle();
		Color? color4 = CaptureCornerPixel(activationFrame.Handle, rECT, color, color2);
		Check(color3.HasValue && color4.HasValue, "能读回浮层的实际像素", "before=" + (color3.HasValue ? color3.Value.ToString() : "null") + " after=" + (color4.HasValue ? color4.Value.ToString() : "null"));
		if (color3.HasValue && color4.HasValue)
		{
			int num = ColorDistance(color3.Value, color);
			int num2 = ColorDistance(color3.Value, color2);
			Check(num < num2, "改主题之前，像素接近旧颜色（薄荷青）而不是新颜色", "像素=" + color3.Value.ToString() + " 距薄荷青=" + num + " 距朱红=" + num2);
			int num3 = ColorDistance(color4.Value, color2);
			int num4 = ColorDistance(color4.Value, color);
			Check(num3 < num4, "★ 改主题之后，像素立刻变成新颜色（不需要等窗口移动）", "像素=" + color4.Value.ToString() + " 距朱红=" + num3 + " 距薄荷青=" + num4);
		}
		activationFrame.HideOverlay();
		Settle();
	}
	private static void TestSettingsRelayoutPerformance()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		float num = SettingsForm.ProbeUiScaleOf(settingsForm);
		int height = (int)Math.Round(760f * num);
		// 预热（首次会建字体、算折行缓存，不算进成绩）
		for (int i = 0; i < 3; i++)
		{
			settingsForm.ClientSize = new Size((int)Math.Round((980f + i * 40f) * num), height);
			SettingsForm.RelayoutActiveForTest(settingsForm);
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		int num2 = 20;
		for (int j = 0; j < num2; j++)
		{
			// 每档宽度都不一样，确保每次都真的重排（模拟拖动边框）
			settingsForm.ClientSize = new Size((int)Math.Round((980f + j * 17f) * num), height);
			SettingsForm.RelayoutActiveForTest(settingsForm);
		}
		stopwatch.Stop();
		double num3 = stopwatch.Elapsed.TotalMilliseconds / (double)num2;
		// 同一尺寸重复调用应当直接被去重挡住
		settingsForm.ClientSize = new Size((int)Math.Round(1100f * num), height);
		SettingsForm.RelayoutActiveForTest(settingsForm);
		Stopwatch stopwatch2 = Stopwatch.StartNew();
		for (int k = 0; k < 20; k++)
		{
			SettingsForm.RelayoutActiveForTest(settingsForm);
		}
		stopwatch2.Stop();
		double num4 = stopwatch2.Elapsed.TotalMilliseconds / 20.0;
		Line("    （重排耗时）换宽度 " + num3.ToString("0.00") + " ms/次，同宽度重复 " + num4.ToString("0.00") + " ms/次");
		Check(num3 < 60.0, "★ 换一档宽度重排一次 < 60ms（拖窗口/最大化不会一顿一顿）", num3.ToString("0.00") + " ms");
		Check(num4 < 3.0, "★ 同尺寸重复重排会被去重（不重复干活）", num4.ToString("0.00") + " ms");
		settingsForm.Close();
	}
	private static void TestNavHighlightFollowsSelection()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		SettleNavAnimation();
		Color color = SettingsForm.ProbeNavRowColor(settingsForm, 2);
		Color color2 = SettingsForm.ProbeNavRowColor(settingsForm, 1);
		Color color3 = SettingsForm.ProbeTokenColor("Sidebar");
		Color color4 = SettingsForm.ProbeTokenColor("AccentSoft");
		Line("    （侧栏底色）第1项=" + color2.R + "," + color2.G + "," + color2.B + " 第3项=" + color.R + "," + color.G + "," + color.B);
		Check(color3.ToArgb() == color2.ToArgb(), "未选中的导航项没有底色（就是侧栏底色）", color2.ToString());
		// 点一下第 3 项，再切回第 1 项：第 3 项必须恢复成"无底色"
		SettingsForm.ProbeSelectPage(settingsForm, 2);
		SettleNavAnimation();
		SettingsForm.ProbeSelectPage(settingsForm, 0);
		SettleNavAnimation();
		Color color5 = SettingsForm.ProbeNavRowColor(settingsForm, 2);
		Color color6 = SettingsForm.ProbeNavRowColor(settingsForm, 0);
		// 注意：真实鼠标可能恰好停在侧栏上，那时会有一层悬停过渡色 —— 那是正常交互，不是残留。
		// 这里只断言「没有留下强调色胶囊」，那才是这条回归用例真正要抓的东西。
		Check(color5.ToArgb() != color4.ToArgb(), "★ 点过又切走的导航项不残留强调色胶囊（回归）", "第3项=" + color5.ToString() + " 胶囊色=" + color4.ToString());
		Check(SettingsForm.ProbeSidebarActive((Form)settingsForm) == 0, "★ 点过又切走后，侧栏的当前页仍然指向第 1 项");
		Check(color6.ToArgb() == color4.ToArgb(), "当前页那项是强调色胶囊", "第1项=" + color6.ToString());
		settingsForm.Close();
	}
	private static void TestSettingsThemeFollowsSystem()
	{
		bool flag = SettingsForm.ProbeThemeIsDark();
		Line("    （系统主题）" + (flag ? "深色" : "浅色"));
		bool registryDark = false;
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
			if (registryKey != null && registryKey.GetValue("AppsUseLightTheme") is int num4)
			{
				registryDark = num4 == 0;
			}
		}
		catch
		{
		}
		Check(SettingsForm.ProbeThemeIsDark() == registryDark, "主题探测和注册表里的 AppsUseLightTheme 一致（不是写死的）");
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		SettingsForm.RenderStats renderStats = SettingsForm.ProbeFormRender(settingsForm, "Teal");
		Line("    （主题渲染）亮度=" + renderStats.MeanBrightness.ToString("0.0") + " 纯黑=" + renderStats.BlackPixels + " 颜色数=" + renderStats.DistinctColors);
		if (flag)
		{
			Check(renderStats.MeanBrightness < 120.0, "★ 系统深色时设置页跟着变深色（不再固定成浅色）", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
		}
		else
		{
			Check(renderStats.MeanBrightness > 200.0, "系统浅色时设置页是浅色底", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
		}
		Check(renderStats.DistinctColors >= 24, "深浅色都是一整套层次（不是一块纯色刷到底）", "颜色数 " + renderStats.DistinctColors);
		double num = ContrastRatio(SettingsForm.ProbeTokenColor("TextPrimary"), SettingsForm.ProbeTokenColor("Surface"));
		Line("    （对比度）正文/卡片=" + num.ToString("0.0"));
		Check(num >= 7.0, "★ 正文与卡片底的对比度达标（看得清才谈得上精致）", "对比度 " + num.ToString("0.0"));
		double num2 = ContrastRatio(SettingsForm.ProbeTokenColor("TextSecondary"), SettingsForm.ProbeTokenColor("Surface"));
		Check(num2 >= 3.5, "次要文字也够清楚（不是灰到看不清）", "对比度 " + num2.ToString("0.0"));
		double num3 = ContrastRatio(SettingsForm.ProbeTokenColor("Accent"), SettingsForm.ProbeTokenColor("Surface"));
		Check(num3 >= 2.2, "强调色在卡片底上够显眼", "对比度 " + num3.ToString("0.0"));
		settingsForm.Close();
	}
	private static void TestSettingsDefaultSizeCompact()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		int num = settingsForm.ProbeDefaultClientHeight();
		int height = settingsForm.ClientSize.Height;
		Check(height <= num + 4, "#33 设置页显示后不会比默认高度更大（不再被内容撑到满屏）", "实际 " + height + " 默认 " + num);
		Check(height >= num / 2, "#33 设置页默认高度没有被压成一条缝", "实际 " + height);
		Check(settingsForm.MinimumSize.Height >= 300, "最小高度仍可用（>= 300 物理像素）", "实际 " + settingsForm.MinimumSize.Height);
		Check(settingsForm.MinimumSize.Height <= num, "最小高度不大于默认高度（否则默认值无意义）", "最小 " + settingsForm.MinimumSize.Height + " 默认 " + num);
		settingsForm.Close();
	}
	private static void TestVisualPolish()
	{
		Check(SettingsForm.ProbeHoverTweenAnimates(out var start, out var end), "★ 悬停是带过渡的（补间从 0 平滑推到 1），不是瞬变", "起点 " + start.ToString("0.00") + " → 终点 " + end.ToString("0.00"));
		using DragFixture dragFixture = new DragFixture((AppConfig c) =>
		{
			c.ShowActivationFrame = true;
		});
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		settingsForm.ClientSize = new Size(1000, 2400);
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		Application.DoEvents();
		SettingsForm.RenderStats renderStats = SettingsForm.ProbeFormRender(settingsForm, dragFixture.Store.Current.FrameColorId);
		int[] array = settingsForm.ProbeFrameColorInk(dragFixture.Store.Current.FrameColorId);
		Line("    （渲染体检）" + renderStats.Width + "x" + renderStats.Height + " 颜色数=" + renderStats.DistinctColors + " 近黑=" + renderStats.BlackPixels + " 强调色=" + renderStats.AccentPixels + " 外框色=" + renderStats.FrameColorPixels + " 平均亮度=" + renderStats.MeanBrightness.ToString("0.0"));
		Line("    （外框色分区）色板=" + array[0] + " 画廊=" + array[1] + " 预览框=" + array[2]);
		Line("    （色板诊断）" + settingsForm.ProbeGridDiagnostic());
		Check(renderStats.Width > 400 && renderStats.Height > 300, "设置窗口渲染出了有效的画布尺寸", renderStats.Width + "x" + renderStats.Height);
		Check(renderStats.DistinctColors > 400, "★ 画面颜色层次足够丰富（不是一块平色，说明圆角/描边/投影/文字都在画）", "不同颜色数 " + renderStats.DistinctColors);
		Check(renderStats.BlackPixels < 400, "★ 画面里没有成片的纯黑像素（自绘控件没铺底导致黑条的老问题没有复发）", "纯黑像素 " + renderStats.BlackPixels);
		if (SettingsForm.ProbeThemeIsDark())
		{
			Check(renderStats.MeanBrightness < 120.0, "★ 系统深色时设置页整体也是深色（不再固定浅色刺眼）", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
			Check(renderStats.MeanBrightness > 15.0, "深色底不是死黑（有层次，不发灰发脏）", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
		}
		else
		{
			Check(renderStats.MeanBrightness > 200.0, "系统浅色时设置页整体是浅色 Win11 底（平均亮度够高）", "平均亮度 " + renderStats.MeanBrightness.ToString("0.0"));
		}
		Check(renderStats.AccentPixels > 300, "★ 强调色真的出现在画面上（主按钮 / 分组竖条 / 选中态）", "强调色像素 " + renderStats.AccentPixels);
		Check(renderStats.FrameColorPixels > 150, "★ 外框色真的出现在画面上（色板 / 样式缩略图 / 实时预览）", "外框色像素 " + renderStats.FrameColorPixels);
		settingsForm.Close();
	}
	private static void TestNoControlClippedAtMinSize()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		float num = SettingsForm.ProbeUiScaleOf(settingsForm);
		int num2 = (int)Math.Round(736f * num);
		int val = (int)Math.Round(440f * num);
		int[] array = new int[4]
		{
			num2,
			num2 + 60,
			num2 + 140,
			num2 + 260
		};
		int num3 = 0;
		string text = "";
		int num4 = 0;
		string text2 = "";
		int[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			int width = array2[i];
			settingsForm.ClientSize = new Size(width, Math.Max(val, 900));
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
			for (int j = 0; j < SettingsForm.ProbeGroupCount(settingsForm); j++)
			{
				SettingsForm.GroupClipReport groupClipReport = SettingsForm.ProbeGroupClipping(settingsForm, j);
				if (groupClipReport.Clipped != 0)
				{
					num3 += groupClipReport.Clipped;
					text = text + "宽" + width + " 第" + (j + 1) + "组：" + groupClipReport.Detail;
					string text3 = SettingsForm.ProbeTextFitting(settingsForm, j);
					if (text3.Length > 0)
					{
						num4++;
						text2 = text2 + "宽" + width + " 第" + (j + 1) + "组：" + text3.Replace("\r\n", " | ");
					}
				}
			}
		}
		Check(num3 == 0, "★ 最小尺寸及附近各档下，没有控件越出所属分组（全屏选项始终完整可见）", text);
		Check(num4 == 0, "★ 最小尺寸及附近各档下，没有任何选项的文字被裁掉（遮挡回归）", text2);
		settingsForm.ClientSize = new Size(num2, Math.Max(val, 900));
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		string text4 = SettingsForm.ProbeSwitchColors(settingsForm, on: true);
		string text5 = SettingsForm.ProbeSwitchColors(settingsForm, on: false);
		Line("    （开关·开）" + text4);
		Line("    （开关·关）" + text5);
		Check(!text4.Contains("异常") && !text5.Contains("异常"), "开关能正常渲染（探针没有报错）", text4 + " / " + text5);
		Check(text4 != text5, "★ 开关的开 / 关两态渲染结果不同（不是画成了同一个样子）", "开：" + text4 + "  关：" + text5);
		int[] array3 = ParseRgb(text4, 0);
		int[] array4 = ParseRgb(text5, 0);
		int num5 = Math.Abs(array3[0] - array4[0]) + Math.Abs(array3[1] - array4[1]) + Math.Abs(array3[2] - array4[2]);
		Check(num5 > 60, "★ 开关的轨道颜色在开 / 关之间有明确差异（强调色 vs 中性灰）", "开轨道=" + string.Join(",", array3) + " 关轨道=" + string.Join(",", array4) + " 差值=" + num5);
		int[] array5 = ParseRgb(text4, 1);
		Check(array5[0] > 230 && array5[1] > 230 && array5[2] > 230, "开关的滑块是浅色（白滑块，在两种轨道上都清晰可辨）", "滑块=" + string.Join(",", array5));
		Control control = SettingsForm.ProbeGroupAt(settingsForm, 3);
		SettingsForm.GroupClipReport groupClipReport2 = SettingsForm.ProbeGroupClipping(settingsForm, 3);
		Check(control != null && groupClipReport2.Clipped == 0, "★ 最小尺寸下『拖动行为』分组自身无控件被遮挡", groupClipReport2.Detail);
		settingsForm.Close();
	}
	private static void TestResizeIsStable()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		float num = SettingsForm.ProbeUiScaleOf(settingsForm);
		int height = (int)Math.Round(760f * num);
		settingsForm.ClientSize = new Size((int)Math.Round(1100f * num), height);
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		string text = SettingsForm.ProbeLayoutFingerprint(settingsForm);
		for (int i = 0; i < 3; i++)
		{
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
		}
		string text2 = SettingsForm.ProbeLayoutFingerprint(settingsForm);
		Check(text.Length > 0 && text == text2, "★ 同一尺寸反复重排结果完全一致（布局幂等，不会越排越偏）", (text == text2) ? "" : "两次指纹不同");
		int num2 = 0;
		string text3 = "";
		int num3 = Math.Max(1, (int)Math.Round(29f * num));
		int num4 = (int)Math.Round(736f * num);
		int num5 = (int)Math.Round(1500f * num);
		for (int j = num4; j <= num5; j += num3)
		{
			settingsForm.ClientSize = new Size(j, height);
			SettingsForm.RelayoutForTest(settingsForm);
			Application.DoEvents();
			for (int k = 0; k < SettingsForm.ProbeGroupCount(settingsForm); k++)
			{
				SettingsForm.GroupClipReport groupClipReport = SettingsForm.ProbeGroupClipping(settingsForm, k);
				if (groupClipReport.Clipped > 0)
				{
					num2 += groupClipReport.Clipped;
					text3 = text3 + "w=" + j + " 第" + (k + 1) + "组：" + groupClipReport.Detail;
				}
			}
		}
		Check(num2 == 0, "★ 连续改变尺寸的每一步都没有控件越界（拖动过程不溢出）", text3);
		settingsForm.ClientSize = new Size((int)Math.Round(2400f * num), height);
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		Rectangle cardBox = SettingsForm.CardBoxOf(settingsForm);
		int num6 = SettingsForm.ProbeCardMaxWidthPx(settingsForm);
		int num8x = Math.Max(num6, settingsForm.ClientSize.Width / 2);
		Check(cardBox.Width <= num8x + (int)Math.Round(4f * num), "★ 窗口拉到超宽时改用两列或仍限宽 " + (num6 / num) + "px，不会把卡片拉成一条细长条", cardBox.ToString() + " 客户区宽=" + settingsForm.ClientSize.Width);
		settingsForm.Close();
	}
	private static void TestDpiRendering()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.StartPosition = FormStartPosition.Manual;
		settingsForm.Location = new Point(20, 20);
		settingsForm.Show();
		Application.DoEvents();
		float num = SettingsForm.ProbeUiScaleOf(settingsForm);
		int num2 = Native.DpiOfWindow(settingsForm.Handle);
		float num3 = (float)num2 / 96f;
		Check(Math.Abs(num - num3) < 0.01f, "★ 界面按窗口所在显示器的真实 DPI 渲染（不是写死 100%，也不是主屏 DPI）", "生效系数=" + num.ToString("0.###") + " 真实 DPI=" + num2 + " → 期望 " + num3.ToString("0.###"));
		settingsForm.ClientSize = new Size((int)Math.Round(900f * num), (int)Math.Round(760f * num));
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		Rectangle rectangle = SettingsForm.ProbeRectOfTextForTest(settingsForm, "外框颜色");
		Check(rectangle.Width > 0, "能定位到样本控件（探针有效）", rectangle.ToString());
		string text = SettingsForm.ProbeLayoutFingerprint(settingsForm);
		SettingsForm.ProbeRescaleForDpi(settingsForm, num2);
		Application.DoEvents();
		string text2 = SettingsForm.ProbeLayoutFingerprint(settingsForm);
		Check(text == text2, "★ 同一 DPI 下重复触发重建是 no-op（幂等，不会越缩越小）", (text == text2) ? "" : "指纹被改动");
		int num4 = ((Math.Abs(num2 - 96) < 24) ? 192 : 96);
		SettingsForm.ProbeRescaleForDpi(settingsForm, num4);
		Application.DoEvents();
		float num5 = SettingsForm.ProbeUiScaleOf(settingsForm);
		Check(Math.Abs(num5 - (float)num4 / 96f) < 0.01f, "★ DPI 变化后缩放系数跟着换（按新分辨率重建界面）", "生效系数=" + num5.ToString("0.###") + " 期望 " + ((float)num4 / 96f).ToString("0.###"));
		int num6 = 0;
		string text3 = "";
		for (int i = 0; i < SettingsForm.ProbeGroupCount(settingsForm); i++)
		{
			SettingsForm.GroupClipReport groupClipReport = SettingsForm.ProbeGroupClipping(settingsForm, i);
			if (groupClipReport.Clipped > 0)
			{
				num6 += groupClipReport.Clipped;
				text3 = text3 + "第" + (i + 1) + "组：" + groupClipReport.Detail;
			}
		}
		Check(num6 == 0, "换 DPI 重建之后布局依然合法（无控件越界）", text3);
		SettingsForm.ProbeRescaleForDpi(settingsForm, num2);
		Application.DoEvents();
		Check(Math.Abs(SettingsForm.ProbeUiScaleOf(settingsForm) - num) < 0.01f, "★ DPI 改回原值后缩放系数回到原值（可逆）", SettingsForm.ProbeUiScaleOf(settingsForm).ToString("0.###"));
		settingsForm.Close();
	}
	private static void TestWheelDoesNotChangeSettings()
	{
		nint wParam = new IntPtr(7864320);
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		SettingsForm.FlatCombo comboBox = settingsForm.ProbeComboRight as SettingsForm.FlatCombo;
		Check(comboBox != null && comboBox.Items.Count > 1, "取到右键动作下拉框");
		if (comboBox != null && comboBox.Items.Count > 1)
		{
			int selectedIndex = comboBox.SelectedIndex;
			for (int i = 0; i < 5; i++)
			{
				Native.SendMessageW(comboBox.Handle, 522, wParam, IntPtr.Zero);
			}
			Application.DoEvents();
			Eq(comboBox.SelectedIndex, selectedIndex, "★ 滚轮扫过『右键』下拉框不会改掉动作绑定（误改的根因）");
		}
		if (settingsForm.ProbeComboLeft is SettingsForm.FlatCombo comboBox2 && comboBox2.Items.Count > 1)
		{
			int selectedIndex2 = comboBox2.SelectedIndex;
			Native.SendMessageW(comboBox2.Handle, 522, wParam, IntPtr.Zero);
			Application.DoEvents();
			Eq(comboBox2.SelectedIndex, selectedIndex2, "★ 滚轮扫过『左键』下拉框不会改掉动作绑定");
		}
		SettingsForm.FlatNum numericUpDown = settingsForm.ProbeNumHold as SettingsForm.FlatNum;
		Check(numericUpDown != null, "取到长按阈值数字框");
		if (numericUpDown != null)
		{
			decimal value = numericUpDown.Value;
			for (int j = 0; j < 3; j++)
			{
				Native.SendMessageW(numericUpDown.Handle, 522, wParam, IntPtr.Zero);
			}
			Application.DoEvents();
			Check(numericUpDown.Value == value, "★ 滚轮扫过数字框不会改掉数值（HoldMs/CornerPercent 被误改的根因）", "前=" + value + " 后=" + numericUpDown.Value);
		}
		if (comboBox != null)
		{
			int num = SettingsForm.ProbeWheelSwallowed(comboBox);
			int num2 = SettingsForm.ProbeWheelForwarded(comboBox);
			Native.SendMessageW(comboBox.Handle, 522, wParam, IntPtr.Zero);
			Application.DoEvents();
			int num3 = SettingsForm.ProbeWheelSwallowed(comboBox);
			int num4 = SettingsForm.ProbeWheelForwarded(comboBox);
			Check(num3 == num + 1 && num4 == num2, "收起状态下滚轮被守卫吞掉（不放行给 WinForms）", "吞 " + num + "→" + num3 + " 放 " + num2 + "→" + num4);
			bool flag = false;
			try
			{
				comboBox.DroppedDown = true;
				flag = comboBox.DroppedDown;
			}
			catch
			{
			}
			if (flag)
			{
				Native.SendMessageW(comboBox.Handle, 522, wParam, IntPtr.Zero);
				Application.DoEvents();
				int num5 = SettingsForm.ProbeWheelSwallowed(comboBox);
				int num6 = SettingsForm.ProbeWheelForwarded(comboBox);
				Check(num6 == num4 + 1 && num5 == num3, "★ 展开状态下滚轮照常放行（没有把下拉框的滚轮功能一并禁掉）", "吞 " + num3 + "→" + num5 + " 放 " + num4 + "→" + num6);
				try
				{
					comboBox.DroppedDown = false;
				}
				catch
				{
				}
			}
			else
			{
				Line("    （本环境无法展开下拉列表，跳过『展开后放行』一条）");
			}
		}
		settingsForm.Close();
	}
	private static void TestIconGlyphs()
	{
		FontFamily iconFamilyForTest = SettingsForm.IconFamilyForTest;
		Check(iconFamilyForTest != null, "图标字体可用（Segoe Fluent Icons 或退回 Segoe MDL2 Assets）", (iconFamilyForTest == null) ? "两个都没有，界面会退化成无图标（不崩，但会显得单薄）" : iconFamilyForTest.Name);
		if (iconFamilyForTest == null)
		{
			return;
		}
		string[] array = new string[8] { "Keyboard", "Move", "Color", "Speed", "Shield", "Chevron", "Help", "Info" };
		string[] iconGlyphsForTest = SettingsForm.IconGlyphsForTest;
		Eq(iconGlyphsForTest.Length, array.Length, "字形表与名称表一一对应");
		StringBuilder stringBuilder = new StringBuilder();
		int num = SettingsForm.MeasureGlyphInk("\uffff", 32f);
		Line("    （基准）U+FFFF 豆腐块墨水=" + num);
		for (int i = 0; i < iconGlyphsForTest.Length; i++)
		{
			int num2 = SettingsForm.MeasureGlyphInk(iconGlyphsForTest[i], 32f);
			Line("    （字形）" + array[i] + " U+" + ((int)iconGlyphsForTest[i][0]).ToString("X4") + " 墨水=" + num2);
			if (num2 <= 12)
			{
				stringBuilder.Append(array[i] + "(空白" + num2 + ") ");
			}
			else if (num > 0 && Math.Abs(num2 - num) <= 2)
			{
				stringBuilder.Append(array[i] + "(是豆腐块" + num2 + ") ");
			}
		}
		Check(stringBuilder.Length == 0, "★ 每个用到的图标字形都渲染出了【真实字形】（既不是空白、也不是豆腐块）", stringBuilder.ToString());
	}
	private static void TestHelpPanel()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		SettingsForm.HelpPanel probeHelpPanel = settingsForm.ProbeHelpPanel;
		Check(probeHelpPanel != null, "取到帮助面板");
		if (probeHelpPanel != null)
		{
			int helpCount = probeHelpPanel.EntryCountForTest;
		Check(helpCount >= 6, "帮助条目数（至少覆盖触发/动作/外观/全屏/晃窗/黑名单）", "实际 " + helpCount);
		Check(helpCount >= 12, "★ 新增的 HUD / 吸附 / 分屏 / 撤销 / 规则 / 布局 / 命令行 / 诊断都在帮助里", "实际 " + helpCount);
			probeHelpPanel.OpenRangeForTest(out var top, out var bottom);
			Check(bottom <= probeHelpPanel.Height, "★ 默认展开项的内容完全落在面板高度内（不会被裁掉）", "内容底 " + bottom + " 面板高 " + probeHelpPanel.Height);
			Check(top == 0, "默认展开第一项（从顶部开始）", "top=" + top);
			// 手风琴：展开项变了高度会变，所以每次点击前都重新按"当前"几何算一次落点
			int y = probeHelpPanel.HeaderTopForTest(helpCount - 1) + probeHelpPanel.HeaderH / 2;
			probeHelpPanel.ClickAtForTest(y);
			Application.DoEvents();
			Eq(probeHelpPanel.OpenIndexForTest, helpCount - 1, "点最后一项的标题能把它展开");
			probeHelpPanel.OpenRangeForTest(out top, out bottom);
			Check(bottom <= probeHelpPanel.Height, "★ 展开最后一项后内容仍然不越界", "内容底 " + bottom + " 面板高 " + probeHelpPanel.Height);
			Check(probeHelpPanel.BodyH > 0, "★ 展开项真的有正文区（不容易被挤成 0）", "BodyH=" + probeHelpPanel.BodyH);
			y = probeHelpPanel.HeaderTopForTest(helpCount - 1) + probeHelpPanel.HeaderH / 2;
			probeHelpPanel.ClickAtForTest(y);
			Eq(probeHelpPanel.OpenIndexForTest, helpCount - 1, "再点一次已展开的项不会把它收起来（同一项保持展开）");
			probeHelpPanel.ClickAtForTest(Fields_Sc(10));
			Eq(probeHelpPanel.OpenIndexForTest, 0, "点第一项能切回去");
			settingsForm.Close();
		}
	}
	private static void TestAboutInfo()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		string text = settingsForm.ProbeAboutText ?? "";
		Line("    （关于分组文字）" + text);
		Check(text.Length > 0, "取到『关于』分组的内容");
		Check(text.Contains("QQQOrange"), "★ 开发者 QQQOrange 显示在界面上");
		Check(text.Contains("DeepSeek"), "★ 开发工具 DeepSeek 显示在界面上");
		Check(text.Contains(AppInfo.ReleaseDate), "★ 更新时间显示在界面上", "期望 " + AppInfo.ReleaseDate);
		Check(text.Contains("版本") && text.Contains(SettingsForm.AppVersionForTest), "★ 当前版本号显示在界面上（且与程序集版本一致）", "程序集版本=" + SettingsForm.AppVersionForTest);
		// 版本号会随每次发版变化，这里锁的是"界面版本 == 程序集版本"以及"不低于本次基线"，不再写死某个具体版本
		Check(string.Equals(SettingsForm.AppVersionForTest, AppInfo.FallbackVersion, StringComparison.Ordinal), "★ 界面版本与兜底版本号一致（发版时两处必须同步改）", "实际=" + SettingsForm.AppVersionForTest + " 兜底=" + AppInfo.FallbackVersion);
		settingsForm.Close();
	}
	private static void TestAllGroupsReachable()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		// 必须 Show()：Control.Visible 会沿父链一路问到窗体，窗体没显示过时整棵树都报不可见。
		settingsForm.CreateControl();
		settingsForm.Show();
		Application.DoEvents();
		int num = SettingsForm.ProbeGroupCount(settingsForm);
		Check(num >= 8, "分组数（至少包含关于 / 帮助）", "实际 " + num);
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < num; i++)
		{
			if (!SettingsForm.ProbeGroupInTree(settingsForm, i))
			{
				stringBuilder.Append(i).Append(' ');
			}
		}
		Check(stringBuilder.Length == 0, "★ 每个分组都真的在窗体可视树里（不存在「建了却没加进去」的分组）", "缺失索引：" + stringBuilder);
		// 新 UI：侧栏一项对应一个分页（不是一张分组）；每一页都必须能直达，且打开时只有它可见。
		int num2 = SettingsForm.ProbePageCount(settingsForm);
		Eq(SettingsForm.ProbeSidebarCount(settingsForm), num2, "★ 侧栏条目数 = 分页数（每一页都能从侧栏直达，用户才找得到）");
		StringBuilder stringBuilder2 = new StringBuilder();
		for (int j = 0; j < num2; j++)
		{
			SettingsForm.ProbeSelectPage(settingsForm, j);
			Application.DoEvents();
			if (!SettingsForm.ProbePageVisible(settingsForm, j))
			{
				stringBuilder2.Append(j).Append(' ');
			}
			int num3 = SettingsForm.ProbeSidebarActive(settingsForm);
			if (num3 != j)
			{
				stringBuilder2.Append("(页").Append(j).Append("高亮").Append(num3).Append(") ");
			}
		}
		Check(stringBuilder2.Length == 0, "★ 点侧栏每一项，对应分页就打开并且高亮对得上", stringBuilder2.ToString());
		settingsForm.Close();
	}
	private static void TestNoControlOverlap()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		int num = SettingsForm.ProbeGroupCount(settingsForm);
		int width = settingsForm.MinimumSize.Width;
		List<int> list = new List<int>();
		for (int i = width; i <= width + 1200; i += 16)
		{
			list.Add(i);
		}
		int[] array = list.ToArray();
		Line("    （宽度扫描）最小宽=" + width + "，共 " + array.Length + " 档");
		for (int j = 0; j < array.Length; j++)
		{
			int num2 = array[j];
			if (num2 < width)
			{
				num2 = width;
			}
			try
			{
				settingsForm.Width = num2;
				settingsForm.PerformLayout();
				Application.DoEvents();
			}
			catch
			{
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int k = 0; k < num; k++)
			{
				string value = SettingsForm.ProbeGroupOverlaps(settingsForm, k);
				if (!string.IsNullOrEmpty(value))
				{
					stringBuilder.Append('(').Append(k).Append(')')
						.Append(value);
				}
			}
			if (stringBuilder.Length > 0)
			{
				Line("    （宽 " + num2 + " 重叠）" + stringBuilder);
			}
			if (stringBuilder.Length > 0)
			{
				Line("    （g4 @宽" + num2 + "）" + SettingsForm.ProbeGroupRects(settingsForm, 3));
			}
			Check(stringBuilder.Length == 0, "★ 宽 " + num2 + "px 下，所有分组内都没有控件互相遮挡", stringBuilder.ToString());
		}
		settingsForm.Close();
	}
	private static void TestSidebarFollowsScroll()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		settingsForm.Show();
		Application.DoEvents();
		Settle();
		// 新 UI：侧栏高亮跟着「点了哪一页」走；页面内部用滚轮滚动。两者各查各的。
		int num = SettingsForm.ProbePageCount(settingsForm);
		Eq(SettingsForm.ProbeSidebarCount(settingsForm), num, "侧栏条目数 = 分页数");
		Line("    （滚动区）" + SettingsForm.ProbeScrollInfo(settingsForm));
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < num; i++)
		{
			SettingsForm.ProbeSelectPage(settingsForm, i);
			Application.DoEvents();
			int num2 = SettingsForm.ProbeSidebarActive(settingsForm);
			if (num2 != i)
			{
				stringBuilder.Append("页").Append(i).Append("→高亮").Append(num2).Append(' ');
			}
			if (!SettingsForm.ProbePageVisible(settingsForm, i))
			{
				stringBuilder.Append("页").Append(i).Append("(不可见) ");
			}
		}
		Line("    （导航跟随）" + ((stringBuilder.Length == 0) ? "全部一致" : stringBuilder.ToString()));
		Check(stringBuilder.Length == 0, "★ 点侧栏任一分页，高亮与可见页都对得上", stringBuilder.ToString());
		// 滚轮滚的是当前页：挑一个内容超过视口的高页（拖动行为：4 张卡），把窗口压矮后滚。
		SettingsForm.ProbeSelectPage(settingsForm, 3);
		settingsForm.ClientSize = new Size(settingsForm.ClientSize.Width, Math.Max(settingsForm.MinimumSize.Height, 620));
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		int num3 = SettingsForm.ProbeWheelScroll(settingsForm, -3);
		Application.DoEvents();
		Check(num3 > 0, "★ 滚轮消息确实让当前页滚动了（用例前提）", "滚动偏移=" + num3);
		settingsForm.Close();
	}
	private static void TestSidebarFollowsRealWheel()
	{
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.Show();
		Application.DoEvents();
		Settle();
		int num = SettingsForm.ProbeSidebarActive(settingsForm);
		int num2 = SettingsForm.ProbePageCount(settingsForm);
		// 新 UI：高亮跟着导航点击切换，滚轮只滚当前页。先验证滚轮真能滚动，
		// 再验证「切到最后一页 → 高亮也是最后一项」。
		SettingsForm.ProbeSelectPage(settingsForm, 3);
		settingsForm.ClientSize = new Size(settingsForm.ClientSize.Width, Math.Max(settingsForm.MinimumSize.Height, 620));
		SettingsForm.RelayoutForTest(settingsForm);
		Application.DoEvents();
		int num3 = 0;
		int num4 = 0;
		for (int i = 0; i < 40; i++)
		{
			int num5 = SettingsForm.ProbeWheelScroll(settingsForm, -3);
			if (num5 == num4 && i > 3)
			{
				break;
			}
			num4 = num5;
			num3 = i;
		}
		Line("    （真实滚轮）滚动 " + num3 + " 步后偏移=" + num4 + "，共 " + num2 + " 页");
		Line("    （同步诊断）" + SettingsForm.ProbeSyncDiag(settingsForm));
		Check(num4 > 0, "★ 真实滚轮消息确实让页面滚动了（用例前提）", "最终滚动偏移=" + num4);
		SettingsForm.ProbeSelectPage(settingsForm, num2 - 1);
		Application.DoEvents();
		int num6 = SettingsForm.ProbeSidebarActive(settingsForm);
		Check(num6 == num2 - 1, "★ 切到最后一页，侧栏高亮也是最后一项", "最终高亮=" + num6 + "（共 " + num2 + " 页）");
		Check(num6 != num, "★ 切换分页后高亮确实发生了变化（不是一直停在第一项）", num + " → " + num6);
		settingsForm.Close();
	}
	private static void TestAllStylesRender()
	{
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = new StringBuilder();
		for (int i = 0; i < FrameStyles.All.Length; i++)
		{
			string text = FrameStyles.All[i];
			string text2 = FrameStyles.DisplayName(text);
			int num = FrameStyles.MeasureStyleInkForTest(text);
			stringBuilder2.Append(text2).Append('(').Append(num)
				.Append(") ");
			if (text2 == text)
			{
				stringBuilder.Append(text + "没有显示名 ");
			}
			if (num < 20)
			{
				stringBuilder.Append(text + "画不出东西(" + num + ") ");
			}
		}
		Line("    （样式 墨水像素）" + stringBuilder2);
		Check(stringBuilder.Length == 0, "★ 每个外框样式都有中文名、且都能画出内容（括号里是实测墨水像素）", stringBuilder.ToString());
		Check(FrameStyles.All.Length >= 5, "样式数量（太少了不够选，但也不该堆垃圾）", FrameStyles.All.Length + " 种");
		Check(FrameStyles.IsValid(FrameStyles.DefaultId), "默认样式必须在样式表里（否则配置归一化会打架）");
		using DragFixture dragFixture = new DragFixture(null);
		using SettingsForm settingsForm = new SettingsForm(dragFixture.Store, dragFixture.Coord);
		settingsForm.CreateControl();
		Application.DoEvents();
		Line("    （样式画廊）" + SettingsForm.ProbeGalleryGeometry(settingsForm));
		Check(SettingsForm.ProbeGalleryFits(settingsForm), "★ 样式画廊的高度放得下全部样式（不会把最后一行裁掉）", SettingsForm.ProbeGalleryGeometry(settingsForm));
		Check(SettingsForm.ProbeColorGridFits(settingsForm), "★ 颜色色板的高度放得下全部颜色", SettingsForm.ProbeColorGridGeometry(settingsForm));
		settingsForm.Close();
	}
	private static void TestOwnInteractiveControlPassthrough()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowDragOwnWindows = true;
		// 本用例会把光标挪到滑块 / 标题栏上，结束后必须还原，
		// 否则会污染同一次（乃至下一次）运行里其它依赖光标初始位置的用例。
		Native.GetCursorPos(out var originalCursor);
		try
		{
			using DragFixture dragFixture = new DragFixture(null);
			TrackBar trackBar = new TrackBar
			{
				Minimum = 1,
				Maximum = 8,
				Value = 3,
				TickStyle = TickStyle.None,
				Location = new Point(12, 36),
				Size = new Size(dragFixture.Window.ClientSize.Width - 24, 45)
			};
			dragFixture.Window.Controls.Add(trackBar);
			dragFixture.Window.TopMost = true;
			dragFixture.Window.Activate();
			Pump();
			Settle();
			if (!PlaceCursorOn(trackBar.Handle, CenterOf(trackBar.Handle)))
			{
				Line("    （跳过：指针无法稳定停在滑块上，可能被其它窗口遮挡）");
				return;
			}
			Native.POINT trackPoint = CenterOf(trackBar.Handle);
			Check(WindowActions.IsPointOverOwnInteractiveControl(trackPoint), "★ 指针停在自有窗口的滑块上时，被识别为「可交互控件」", "命中=" + WindowActions.DescribeInteractiveAt(trackPoint));
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms = Ms(trackPoint);
			bool flag = dragFixture.Coord.OnMouse(513, ref ms);
			Settle();
			Check(!flag, "★ 触发键激活时落在滑块上的左键【不被吞掉】，交还给控件", "被吞=" + flag);
			Check(!dragFixture.Coord.Drag.IsDragging, "落在滑块上的左键不会启动窗口拖动");
			Native.MSLLHOOKSTRUCT ms2 = Ms(trackPoint);
			dragFixture.Coord.OnMouse(514, ref ms2);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
			Native.POINT point = new Native.POINT(dragFixture.Window.Left + dragFixture.Window.Width / 2, dragFixture.Window.Top + 6);
			Check(!WindowActions.IsPointOverOwnInteractiveControl(point), "标题栏不被当作可交互控件（窗口仍可被拖动）", "命中=" + WindowActions.DescribeInteractiveAt(point));
			ActivateTrigger(dragFixture.Coord);
			Native.MSLLHOOKSTRUCT ms3 = Ms(point);
			bool flag2 = dragFixture.Coord.OnMouse(513, ref ms3);
			Check(flag2, "★ 触发键激活时落在标题栏上的左键被接管", "被吞=" + flag2);
			Check(dragFixture.Coord.Drag.IsDragging, "标题栏上的左键确实启动了拖动");
			Native.MSLLHOOKSTRUCT ms4 = Ms(point);
			dragFixture.Coord.OnMouse(514, ref ms4);
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowDragOwnWindows = true;
			try
			{
				Native.SetCursorPos(originalCursor.X, originalCursor.Y);
				Pump();
			}
			catch
			{
			}
		}
	}
	private static void TestGroupBoxNotInteractive()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		Native.GetCursorPos(out var cursorBefore);
		try
		{
			using Form form = new Form
			{
				Text = "HoldDrag 分组框判定用例",
				StartPosition = FormStartPosition.Manual,
				Location = new Point(60, 60),
				Size = new Size(460, 380),
				ShowInTaskbar = false,
				TopMost = true
			};
			GroupBox groupBox = new GroupBox
			{
				Text = "空白分组",
				Location = new Point(12, 12),
				Size = new Size(420, 330)
			};
			Button button = new Button
			{
				Text = "确定",
				Location = new Point(24, 288),
				Size = new Size(120, 36)
			};
			groupBox.Controls.Add(button);
			form.Controls.Add(groupBox);
			form.Show();
			Settle();
			Native.POINT boxPoint = new Native.POINT(form.Left + groupBox.Left + 210, form.Top + groupBox.Top + 140);
			if (!PlaceCursorOn(groupBox.Handle, boxPoint))
			{
				Line("    （跳过：指针无法稳定停在分组框上，可能被其它窗口遮挡）");
				return;
			}
			Line("    （分组框空白）命中=" + WindowActions.DescribeInteractiveAt(boxPoint));
			Check(!WindowActions.IsPointOverOwnInteractiveControl(boxPoint), "★★ 自有窗口的分组框【不被】当作可交互控件（按住触发键仍能把设置页整体拖走）", "命中=" + WindowActions.DescribeInteractiveAt(boxPoint));
			Native.POINT buttonPoint = new Native.POINT(form.Left + groupBox.Left + button.Left + 60, form.Top + groupBox.Top + button.Top + 18);
			if (PlaceCursorOn(button.Handle, buttonPoint))
			{
				Line("    （真按钮）命中=" + WindowActions.DescribeInteractiveAt(buttonPoint));
				Check(WindowActions.IsPointOverOwnInteractiveControl(buttonPoint), "★ 真按钮仍被识别为可交互控件（区分没有误伤）", "命中=" + WindowActions.DescribeInteractiveAt(buttonPoint));
			}
			else
			{
				Line("    （跳过：指针无法稳定停在按钮上）");
			}
		}
		finally
		{
			try
			{
				Native.SetCursorPos(cursorBefore.X, cursorBefore.Y);
				Pump();
			}
			catch
			{
			}
		}
	}
	private static void TestNoContentInterference()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		WindowActions.AllowOwnProcessAsTargetForTest = true;
		try
		{
			using DragFixture dragFixture = new DragFixture((AppConfig c) =>
			{
				c.ShowActivationFrame = true;
				c.BringToFront = false;
				c.DoubleClickLeftMaximize = false;
			});
			DragEngine drag = dragFixture.Coord.Drag;
			int cancelModeSentForTest = Coordinator.CancelModeSentForTest;
			ActivateTrigger(dragFixture.Coord);
			Settle();
			Native.POINT p = CenterOf(dragFixture.H);
			Native.MSLLHOOKSTRUCT ms = Ms(p);
			dragFixture.Coord.OnMouse(513, ref ms);
			Settle();
			Check(drag.IsDragging, "用例前提：左键拖动已建立");
			Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest, "★ 命中目标后向它发出了 WM_CANCELMODE（结束它的拖拽模态，防止拖选文字）", "发出次数=" + (Coordinator.CancelModeSentForTest - cancelModeSentForTest));
			Native.MSLLHOOKSTRUCT ms2 = Ms(new Native.POINT
			{
				X = p.X + 6,
				Y = p.Y + 6
			});
			bool flag = dragFixture.Coord.OnMouse(512, ref ms2);
			Check(!flag, "拖动期间 WM_MOUSEMOVE 绝不吞掉（吞了光标会冻结、窗口原地哆嗦）", "返回值=" + flag);
			Native.MSLLHOOKSTRUCT ms3 = Ms(p);
			bool ok = dragFixture.Coord.OnMouse(514, ref ms3);
			Settle();
			Check(ok, "拖动会话内的按钮抬起被我们吞掉（目标窗口看不到这次释放）", "返回值=" + ok);
			Check(!drag.IsDragging, "用例前提：抬起后拖动已结束");
			int cancelModeSentForTest2 = Coordinator.CancelModeSentForTest;
			Native.MSLLHOOKSTRUCT ms4 = Ms(p);
			dragFixture.Coord.OnMouse(516, ref ms4);
			Settle();
			Native.MSLLHOOKSTRUCT ms5 = Ms(p);
			dragFixture.Coord.OnMouse(517, ref ms5);
			Settle();
			Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest2, "★ 右键操作同样先取消目标窗口的拖拽模态", "发出次数=" + (Coordinator.CancelModeSentForTest - cancelModeSentForTest2));
			int cancelModeSentForTest3 = Coordinator.CancelModeSentForTest;
			Native.MSLLHOOKSTRUCT ms6 = Ms(p);
			dragFixture.Coord.OnMouse(519, ref ms6);
			Settle();
			Native.MSLLHOOKSTRUCT ms7 = Ms(p);
			dragFixture.Coord.OnMouse(520, ref ms7);
			Settle();
			Check(Coordinator.CancelModeSentForTest > cancelModeSentForTest3, "★ 中键操作同样先取消目标窗口的拖拽模态", "发出次数=" + (Coordinator.CancelModeSentForTest - cancelModeSentForTest3));
			dragFixture.Coord.Trigger.SimulateKeyUp(null);
			Settle();
		}
		finally
		{
			WindowActions.AllowOwnProcessAsTargetForTest = false;
		}
	}
}
