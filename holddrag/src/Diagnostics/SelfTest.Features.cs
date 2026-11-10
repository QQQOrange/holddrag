// HoldDrag · Diagnostics/SelfTest.Features.cs
//
// v1.12 新功能的回归用例：边缘吸附、键盘分屏、撤销、诊断包、新配置项。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HoldDrag;

internal static partial class SelfTest
{
	private static void TestSnapEngine()
	{
		Native.RECT work = new Native.RECT(0, 0, 1920, 1040);
		// 离左边 5px：应该吸到 0
		SnapEngine.Result result = SnapEngine.SnapMove(new Native.RECT(5, 200, 805, 700), work, 8);
		Eq(result.Rect.Left, 0, "左边 5px 内吸附到工作区左沿");
		Eq(result.GuideX, 0, "吸附时仍然算出对齐位置（供内部使用）");
		Check(result.SnappedX, "标记为已吸附（X 轴）");
		// 离右边 3px：右边贴到 1920
		SnapEngine.Result result2 = SnapEngine.SnapMove(new Native.RECT(1112, 100, 1917, 600), work, 8);
		Eq(result2.Rect.Right, 1920, "右边 3px 内吸附到工作区右沿");
		// 离得很远：不吸附，矩形不变
		SnapEngine.Result result3 = SnapEngine.SnapMove(new Native.RECT(400, 300, 1200, 800), work, 8);
		Check(!result3.SnappedX && !result3.SnappedY, "远离边缘时不做任何吸附");
		Eq(result3.Rect.Left, 400, "不吸附时矩形原样返回");
		// 阈值 0 = 关闭
		SnapEngine.Result result4 = SnapEngine.SnapMove(new Native.RECT(1, 200, 801, 700), work, 0);
		Check(!result4.SnappedX, "阈值 0 时松开也不吸附");
		// 缩放：只吸附正在拖的那条边
		SnapEngine.Result result5 = SnapEngine.SnapResize(new Native.RECT(4, 100, 800, 600), dragLeft: true, dragRight: false, dragTop: false, dragBottom: false, work, 8);
		Eq(result5.Rect.Left, 0, "缩放时左边缘 4px 内吸附");
		Eq(result5.Rect.Right, 800, "缩放吸附不影响另一条竖边");
		// 水平居中（阈值内）
		SnapEngine.Result result6 = SnapEngine.SnapMove(new Native.RECT(558, 300, 1358, 800), work, 8);
		Eq(result6.Rect.Left, 560, "接近水平居中时对齐到中线");
	}

	private static void TestTiling()
	{
		Native.RECT work = new Native.RECT(0, 0, 1920, 1080);
		Native.RECT left = Tiling.Half(work, left: true);
		Eq(left.Left, 0, "左半屏从 0 开始");
		Eq(left.Width, 960, "左半屏宽度是一半");
		Native.RECT right = Tiling.Half(work, left: false);
		Eq(right.Left, 960, "右半屏从中间开始");
		Eq(right.Right, 1920, "右半屏贴到右沿");
		Native.RECT zone1 = Tiling.Zone(1, work);
		Eq(zone1.Left, 0, "九宫格 1 在左下：x=0");
		Eq(zone1.Bottom, 1080, "九宫格 1 在左下：贴底");
		Eq(zone1.Width, 640, "九宫格每格宽 1/3");
		Native.RECT zone9 = Tiling.Zone(9, work);
		Eq(zone9.Right, 1920, "九宫格 9 在右上：贴右");
		Eq(zone9.Top, 0, "九宫格 9 在右上：贴顶");
		Native.RECT zone5 = Tiling.Zone(5, work);
		Eq(zone5.Left, 640, "九宫格 5 在正中间");
		Eq(zone5.Top, 360, "九宫格 5 垂直居中");
		Native.RECT centered = Tiling.Centered(work, new Native.RECT(0, 0, 800, 600));
		Eq(centered.Left, 560, "居中：水平位置正确");
		Eq(centered.Top, 240, "居中：垂直位置正确");
		Eq(centered.Width, 800, "居中不改变尺寸");
		// 目标比屏幕还大时夹到工作区
		Native.RECT big = Tiling.Centered(work, new Native.RECT(0, 0, 3000, 2000));
		Eq(big.Width, 1920, "窗口比屏幕大时夹到工作区宽度");
		Eq(big.Left, 0, "夹到工作区后贴左");
	}

	private static void TestUndo()
	{
		if (!NoRoomForTestWindow())
		{
			return;
		}
		using DragFixture dragFixture = new DragFixture(null);
		Native.GetWindowRect(dragFixture.H, out var lpRect);
		Native.RECT start = lpRect;
		// 把窗口挪走
		Native.RECT moved = new Native.RECT(start.Left + 60, start.Top + 40, start.Right + 60, start.Bottom + 40);
		WindowActions.SetDragRect(dragFixture.H, moved);
		Native.GetWindowRect(dragFixture.H, out var lpRect2);
		Eq(lpRect2.Left, moved.Left, "窗口已被移动（准备撤销）");
		dragFixture.Coord.Drag.ArmUndoForTest(dragFixture.H, start);
		Check(dragFixture.Coord.Drag.HasUndoForTest, "撤销记录已就绪");
		Check(dragFixture.Coord.Drag.TryUndoLast(), "Ctrl+Z 撤销成功执行");
		Native.GetWindowRect(dragFixture.H, out var lpRect3);
		Eq(lpRect3.Left, start.Left, "撤销后左边回到原位");
		Eq(lpRect3.Top, start.Top, "撤销后顶边回到原位");
		Check(!dragFixture.Coord.Drag.HasUndoForTest, "撤销只能用一次（用过即失效）");
		Check(!dragFixture.Coord.Drag.TryUndoLast(), "再按一次 Ctrl+Z 不会重复撤销");
	}

	private static void TestDiagnosticsBundle()
	{
		string text = Path.Combine(Path.GetTempPath(), "holddrag-diag-" + Guid.NewGuid().ToString("N") + ".zip");
		try
		{
			string text2 = DiagnosticsBundle.Create(text, "自检生成");
			Check(File.Exists(text2), "诊断包已生成", text2);
			using (ZipArchive zipArchive = ZipFile.OpenRead(text2))
			{
				Check(zipArchive.Entries.Any((ZipArchiveEntry e) => e.FullName == "system.txt"), "诊断包里含 system.txt");
				using (Stream stream = zipArchive.GetEntry("system.txt").Open())
				{
					using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8);
					string text3 = streamReader.ReadToEnd();
					Check(text3.Contains("HoldDrag 诊断信息"), "system.txt 有标题");
					Check(text3.Contains("版本："), "system.txt 含版本信息");
					Check(text3.Contains("显示器："), "system.txt 含显示器信息");
					Check(text3.Contains("自检生成"), "附加信息被写进 system.txt");
				}
			}
			Check(new FileInfo(text2).Length > 0, "诊断包不是空文件");
		}
		finally
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
		}
	}

	private static void TestNewConfigDefaults()
	{
		AppConfig appConfig = new AppConfig();
		Check(appConfig.ShowDragHud, "默认开启尺寸提示");
		Check(appConfig.SnapToEdges, "默认开启边缘吸附");
		Eq(appConfig.SnapThreshold, 8, "默认吸附距离 8px");
		Check(appConfig.KeyboardTiling, "默认开启键盘分屏");
		Check(appConfig.UndoEnabled, "默认开启撤销");
		Check(!appConfig.OnboardingDone, "首次启动默认未看过引导");
		appConfig.SnapThreshold = 999;
		appConfig.Normalize();
		Eq(appConfig.SnapThreshold, 40, "吸附距离超界会被拉回 40");
		appConfig.SnapThreshold = -5;
		appConfig.Normalize();
		Eq(appConfig.SnapThreshold, 0, "吸附距离负数会被拉回 0");
	}

	private static void TestHudOverlay()
	{
		using DragFixture dragFixture = new DragFixture(null);
		try
		{
			dragFixture.Coord.Drag.ProbeShowHudForTest("1280 × 720  ·  240,180");
			Check(dragFixture.Coord.Drag.HudVisibleForTest, "尺寸 HUD 的分层窗口能创建并显示");
			dragFixture.Coord.Drag.ProbeHideHintsForTest();
			Thread.Sleep(120);
			Check(!dragFixture.Coord.Drag.HudVisibleForTest, "HUD 能隐藏");
		}
		finally
		{
			dragFixture.Coord.Drag.ProbeHideHintsForTest();
		}
	}

	/// <summary>回归：窗口比工作区还宽时，不能吸到右边（会把它弹到屏幕外）。</summary>
	private static void TestSnapWideWindow()
	{
		Native.RECT work = new Native.RECT(0, 0, 1000, 800);
		Native.RECT wide = new Native.RECT(-200, 100, 1300, 900);
		SnapEngine.Result result = SnapEngine.SnapMove(new Native.RECT(-197, 100, 1303, 900), work, 8);
		Check(!result.SnappedX, "比屏幕宽的窗口不吸附右边", "Left=" + result.Rect.Left);
		Native.RECT tall = new Native.RECT(100, -300, 900, 900);
		SnapEngine.Result result2 = SnapEngine.SnapMove(new Native.RECT(100, -296, 900, 904), work, 8);
		Check(!result2.SnappedY, "比屏幕高的窗口不吸附下边", "Top=" + result2.Rect.Top);
	}

}
