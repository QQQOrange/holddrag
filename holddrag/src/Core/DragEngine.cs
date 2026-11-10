// HoldDrag · Core/DragEngine.cs

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

internal sealed class DragEngine : IDisposable
{
	private struct Session
	{
		public nint Hwnd;

		public bool Move;

		public int Button;

		public Native.POINT StartCursor;

		public Native.RECT StartRect;

		public ResizeZone Zone;

		public int MinW;

		public int MinH;

		public int Gen;
	}

	private readonly Func<AppConfig> _cfg;

	private readonly ActivationFrame _frame;

	private readonly DragWorker _worker;

	private static readonly int OwnPid = Process.GetCurrentProcess().Id;

	private static int _detectedHz;

	private readonly object _gate = new object();

	private Session _session;

	private volatile bool _dragging;

	private volatile bool _moved;

	private int _generation;

	private Native.RECT _inset;

	private nint _hoverRaw;

	private nint _hoverHwnd;

	private Native.RECT _hoverRect;

	private bool _hoverRectValid;

	private readonly ShakeDetector _shake = new ShakeDetector();

	private int _shakeGen = -1;

	private volatile bool _frameWanted;

	/// <summary>尺寸 HUD（拖动时显示 W × H）。</summary>
	private readonly HintOverlay _hud = new HintOverlay();

	/// <summary>吸附引导线（独立窗口，和 HUD 互不干扰）。</summary>
	private readonly HintOverlay _guide = new HintOverlay();

	/// <summary>触发键按住一会儿后浮出来的快捷键提示（只在还没开始拖的时候出现）。</summary>
	private readonly HintOverlay _hint = new HintOverlay();

	private System.Threading.Timer _hintTimer;

	private nint _undoHwnd;

	private Native.RECT _undoRect;

	private long _undoAt;

	/// <summary>已向目标窗口发过 WM_ENTERSIZEMOVE / 事件 10，退出时必须成对补上（见 Cleanup / End）。</summary>
	private volatile bool _sizeMoveActive;

	/// <summary>本次拖动是否成功把系统定时器精度提到 1ms（结束时要还回去，别让整个系统陪着耗电）。</summary>
	private bool _timerResolutionRaised;

	internal static int DiagFrameUpdates;

	internal static int DiagWindowSkipped;

	internal static int DiagWindowWritten;

	private bool _overlaysReady;

	private int _sessionCount;

	private volatile bool _dragWait;

	private bool _frameInside;

	internal static int DetectedRefreshHz
	{
		get
		{
			int detectedHz = _detectedHz;
			if (detectedHz > 0)
			{
				return detectedHz;
			}
			detectedHz = (_detectedHz = Native.GetRefreshRate());
			Log.Info("实测屏幕刷新率：" + detectedHz + " Hz");
			return detectedHz;
		}
	}

	public bool IsDragging => _dragging;

	public int Button => _session.Button;

	public bool LastMoved => _moved;

	public int SessionCount => _sessionCount;

	internal ActivationFrame Frame => _frame;

	internal bool FrameReallyVisible
	{
		get
		{
			try
			{
				return _frame.IsShown && Native.IsWindowVisible(_frame.Handle);
			}
			catch
			{
				return false;
			}
		}
	}

	internal static void DiagReset()
	{
		DiagFrameUpdates = 0;
		DiagWindowSkipped = 0;
		DiagWindowWritten = 0;
	}

	public DragEngine(Func<AppConfig> config)
	{
		_cfg = config;
		_frame = new ActivationFrame();
		_worker = new DragWorker(OnWorkerMove);
		_worker.Start();
	}

	public void Dispose()
	{
		Cancel();
		_hud.Dispose();
		_guide.Dispose();
		_hint.Dispose();
		_worker.Dispose();
		if (_overlaysReady)
		{
			_frame.Destroy();
			_overlaysReady = false;
		}
		_idleReleaseTimer?.Dispose();
	}

	public void Precreate()
	{
		try
		{
			_hud.Start();
			_guide.Start();
			_hint.Start();
			_frame.EnsureCreated();
			_overlaysReady = true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public void DeliverCursor(Native.POINT pt)
	{
		if (_dragging)
		{
			_worker.PostMove(pt);
		}
	}

	public bool Begin(nint hwnd, bool move, int button, Native.POINT pt)
	{
		// 一旦真的开始拖，提示就该让位（它只是"还没动手时"的说明）
		CancelHintTimer();
		if (hwnd != IntPtr.Zero)
		{
			if (move && !AppRules.AllowsMove(hwnd))
			{
				Log.Info("[规则] 该程序只允许缩放，忽略这次移动");
				return false;
			}
			if (!move && !AppRules.AllowsResize(hwnd))
			{
				Log.Info("[规则] 该程序只允许移动，忽略这次缩放");
				return false;
			}
		}
		return BeginCore(hwnd, move, button, pt);
	}

	private bool BeginCore(nint hwnd, bool move, int button, Native.POINT pt)
	{
		AppConfig appConfig = _cfg();
		if (!Native.GetWindowRect(hwnd, out var lpRect))
		{
			Log.Info("[诊断] 拖动无法开始：拿不到目标窗口矩形 句柄=" + ((IntPtr)hwnd).ToInt64().ToString("X"));
			return false;
		}
		bool flag = Native.IsZoomed(hwnd);
		bool flag2 = !flag && WindowActions.IsFullscreen(hwnd, lpRect);
		// 不能缩放（没有 WS_THICKFRAME 的固定大小对话框）就干脆不接管缩放：
		// 用 SetWindowPos 硬改是能改的，但那会绕过窗口自己的最小/最大跟踪，把对话框压变形而且它自己修复不了。
		if (!move && !flag && !flag2 && !WindowActions.IsResizable(hwnd))
		{
			Log.Info("[诊断] 目标窗口不可缩放（无 WS_THICKFRAME）→ 放行不接管 句柄=" + ((IntPtr)hwnd).ToInt64().ToString("X"));
			return false;
		}
		if (flag | flag2)
		{
			if (appConfig.ExitFullscreenOnTrigger)
			{
				List<Action> pending = new List<Action>();
				if (!WindowActions.ExitFullscreenToWindowed(hwnd, ref lpRect, pt, (Action w) =>
				{
					pending.Add(w);
				}))
				{
					Log.Info("[诊断] 目标是" + (flag ? "最大化" : "全屏") + "窗口，降级成普通窗口失败 → 放行不接管 句柄=" + ((IntPtr)hwnd).ToInt64().ToString("X"));
					return false;
				}
				if (pending.Count > 0)
				{
					int cornerPct = appConfig.CornerPercent;
					int btn = button;
					bool mv = move;
					Native.POINT cur = pt;
					nint target = hwnd;
					PostTargetWork(() =>
					{
						for (int i = 0; i < pending.Count; i++)
						{
							try
							{
								pending[i]();
							}
							catch (Exception ex2)
							{
								Log.Error(ex2);
							}
						}
						if (Native.GetWindowRect(target, out var lpRect2))
						{
							ReconcileStartRect(target, btn, mv, cur, cornerPct, lpRect2);
						}
					});
				}
			}
			else
			{
				if (!flag || !appConfig.RestoreMaximizedWhenMoved)
				{
					Log.Info("[诊断] 目标是" + (flag ? "最大化" : "全屏") + "窗口，且『触发时退出全屏』已关闭 → 放行不接管 键=" + button + " 移动=" + move);
					return false;
				}
				double num = ((lpRect.Width > 0) ? ((double)(pt.X - lpRect.Left) / (double)lpRect.Width) : 0.5);
				double num2 = ((lpRect.Height > 0) ? ((double)(pt.Y - lpRect.Top) / (double)lpRect.Height) : 0.5);
				if (num < 0.0)
				{
					num = 0.0;
				}
				if (num > 1.0)
				{
					num = 1.0;
				}
				if (num2 < 0.0)
				{
					num2 = 0.0;
				}
				if (num2 > 1.0)
				{
					num2 = 1.0;
				}
				Native.WINDOWPLACEMENT lpwndpl = new Native.WINDOWPLACEMENT
				{
					length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT))
				};
				if (Native.GetWindowPlacement(hwnd, ref lpwndpl) && lpwndpl.rcNormalPosition.Width > 0 && lpwndpl.rcNormalPosition.Height > 0)
				{
					lpRect = lpwndpl.rcNormalPosition;
				}
				else if (!Native.GetWindowRect(hwnd, out lpRect))
				{
					return false;
				}
				nint target2 = hwnd;
				int cornerPct2 = appConfig.CornerPercent;
				int btn2 = button;
				bool mv2 = move;
				Native.POINT cur2 = pt;
				if (move)
				{
					int nx = pt.X - (int)(num * (double)lpRect.Width);
					int ny = pt.Y - (int)(num2 * (double)lpRect.Height);
					int ww = lpRect.Width;
					int hh = lpRect.Height;
					PostTargetWork(() =>
					{
						Native.ShowWindow(target2, 9);
						Native.SetWindowPos(target2, IntPtr.Zero, nx, ny, ww, hh, 16917u);
						if (Native.GetWindowRect(target2, out var lpRect2))
						{
							ReconcileStartRect(target2, btn2, mv2, cur2, cornerPct2, lpRect2);
						}
					});
					lpRect = new Native.RECT(nx, ny, nx + ww, ny + hh);
				}
				else
				{
					PostTargetWork(() =>
					{
						Native.ShowWindow(target2, 9);
						if (Native.GetWindowRect(target2, out var lpRect2))
						{
							ReconcileStartRect(target2, btn2, mv2, cur2, cornerPct2, lpRect2);
						}
					});
				}
			}
		}
		ResizeZone resizeZone = ResizeZone.Center;
		if (!move)
		{
			resizeZone = Zones.Resize(lpRect, pt.X, pt.Y, appConfig.CornerPercent);
			if (resizeZone == ResizeZone.Center)
			{
				resizeZone = Zones.NearestCorner(lpRect, pt.X, pt.Y);
			}
		}
		int num3 = Native.GetSystemMetrics(34);
		int num4 = Native.GetSystemMetrics(35);
		if (num3 < 64)
		{
			num3 = 64;
		}
		if (num4 < 32)
		{
			num4 = 32;
		}
		Native.RECT inset = ComputeInset(hwnd, lpRect);
		bool flag3 = appConfig.DragThreshold >= 2 || ((appConfig.DragThreshold == 1) & flag);
		_dragWait = flag3 && appConfig.DragOutThresholdPx > 0;
		lock (_gate)
		{
			_session = default;
			_session.Hwnd = hwnd;
			_session.Move = move;
			_session.Button = button;
			_session.StartCursor = pt;
			_session.StartRect = lpRect;
			_session.Zone = resizeZone;
			_session.MinW = num3;
			_session.MinH = num4;
			_moved = false;
			_generation++;
			_session.Gen = _generation;
			_inset = inset;
			_frameWanted = appConfig.ShowActivationFrame;
			_dragging = true;
		}
		WindowActions.ResetSessionKickCache();
		DiagReset();
		_frameInside = false;
		// 一次拖动期间把系统定时器精度提到 1ms：帧间隔 7ms（144Hz）这类设置在默认 15.6ms 精度下根本达不到，
		// 而且每次 Thread.Sleep 都会连带推迟合并队列里的鼠标移动。拖动结束立刻还回去（见 Cleanup）。
		if (!_timerResolutionRaised)
		{
			try
			{
				_timerResolutionRaised = Native.TimeBeginPeriod(1u) == 0;
			}
			catch (Exception ex2)
			{
				Log.Error(ex2);
			}
		}
		WindowActions.NotifySizeMove(hwnd, start: true);
		_sizeMoveActive = true;
		_worker.FrameIntervalMs = appConfig.FrameIntervalMs(DetectedRefreshHz);
		try
		{
			GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		if (appConfig.BringToFront)
		{
			nint target3 = hwnd;
			Thread thread = new Thread(() =>
			{
				try
				{
					WindowActions.ForceForeground(target3);
				}
				catch (Exception ex2)
				{
					Log.Error(ex2);
				}
			});
			thread.IsBackground = true;
			thread.Name = "HoldDrag.Raise";
			thread.Start();
		}
		if (appConfig.ShowActivationFrame && AppRules.AllowsFrame(hwnd))
		{
			_frame.HideOverlay();
			ShowDragFrame(lpRect, appConfig);
			Native.RECT rECT = FrameRectOf(lpRect, appConfig, out var inset2);
			Log.Info("[诊断] 拖动框已请求显示 开关=" + appConfig.ShowActivationFrame + " 就绪=" + _overlaysReady + " 目标矩形=" + lpRect.Left + "," + lpRect.Top + " " + lpRect.Width + "x" + lpRect.Height + " 可见缩进=" + _inset.Left + "/" + _inset.Top + "/" + _inset.Right + "/" + _inset.Bottom + " 外框矩形=" + rECT.Left + "," + rECT.Top + " " + rECT.Width + "x" + rECT.Height + (inset2 ? " 模式=内缩" : " 模式=外扩") + " 抬到最顶=" + _frame.RaiseAboveAll + " 显示=" + _frame.IsShown + " 请求=" + (_frame.ShowRequestedForTest ? "已投递" : "未投递"));
		}
		Log.Info((move ? "开始移动" : "开始缩放") + " 句柄=" + ((IntPtr)hwnd).ToInt64().ToString("X") + " 刷新率=" + DetectedRefreshHz + "Hz");
		Interlocked.Increment(ref _sessionCount);
		return true;
	}

	/// <summary>
	/// 撤销上一次拖动：把窗口矩形还原回拖动开始前。
	/// 只在 5 秒内有效，且窗口还活着 —— 否则用户按 Ctrl+Z 会去改一个已经关掉的窗口。
	/// </summary>
	public bool TryUndoLast()
	{
		if (_undoAt == 0L || _undoHwnd == IntPtr.Zero)
		{
			return false;
		}
		if (Environment.TickCount64 - _undoAt > 5000)
		{
			_undoAt = 0L;
			return false;
		}
		if (!Native.IsWindow(_undoHwnd))
		{
			_undoAt = 0L;
			return false;
		}
		try
		{
			WindowActions.SetDragRect(_undoHwnd, _undoRect);
			Log.Info("[撤销] 已把窗口 " + ((IntPtr)_undoHwnd).ToInt64().ToString("X") + " 还原到 " + _undoRect.Left + "," + _undoRect.Top + " " + _undoRect.Width + "x" + _undoRect.Height);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
		_undoAt = 0L;
		return true;
	}

	internal bool HasUndoForTest => _undoAt != 0L && Environment.TickCount64 - _undoAt <= 5000;

	/// <summary>自检：直接显示一次 HUD，验证分层窗口从创建到渲染整条链路。</summary>
	internal void ProbeShowHudForTest(string text)
	{
		_hud.Start();
		_guide.Start();
		Native.RECT rect = new Native.RECT(200, 200, 900, 700);
		_hud.ShowText(text, rect, 1f);
	}

	internal void ProbeShowGuideForTest()
	{
		_guide.Start();
		_guide.ShowLine(vertical: true, 400, 100, 900, Color.FromArgb(145, 140, 255));
	}

	internal bool HudVisibleForTest
	{
		get
		{
			Thread.Sleep(80);
			return _hud.VisibleForTest;
		}
	}

	internal bool GuideVisibleForTest
	{
		get
		{
			Thread.Sleep(80);
			return _guide.VisibleForTest;
		}
	}

	internal Rectangle GuideBoundsForTest
	{
		get
		{
			Thread.Sleep(120);
			return _guide.BoundsForTest;
		}
	}

	internal void ProbeHideHintsForTest()
	{
		_hud.Hide();
		_guide.Hide();
	}

	internal void ArmUndoForTest(nint hwnd, Native.RECT rect)
	{
		_undoHwnd = hwnd;
		_undoRect = rect;
		_undoAt = Environment.TickCount64;
	}

	/// <summary>光标所在显示器的工作区（不含任务栏）。</summary>
	private static Native.RECT WorkAreaOfPoint(Native.POINT pt)
	{
		try
		{
			Rectangle workingArea = Screen.FromPoint(new Point(pt.X, pt.Y)).WorkingArea;
			return new Native.RECT(workingArea.Left, workingArea.Top, workingArea.Right, workingArea.Bottom);
		}
		catch
		{
			return new Native.RECT(0, 0, 1920, 1080);
		}
	}

	/// <summary>
	/// 按住 Shift 时临时不吸附：想精确摆放时不用跑去设置里关掉再打开。
	/// </summary>
	private static bool IsShiftDown()
	{
		try
		{
			return (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>吸附阈值按目标窗口的 DPI 缩放（150% 屏上 8px 会显得很紧）。</summary>
	private static int ScaledThreshold(int threshold, nint hwnd)
	{
		try
		{
			int num = Native.DpiOfWindow(hwnd);
			return Math.Max(1, (int)Math.Round((double)threshold * (double)num / 96.0));
		}
		catch
		{
			return threshold;
		}
	}

	private static Native.RECT ComputeInset(nint hwnd, Native.RECT wr)
	{
		if (!Native.TryGetVisibleRect(hwnd, out var r))
		{
			return default;
		}
		if (r.Width <= 0 || r.Height <= 0)
		{
			return default;
		}
		int num = r.Left - wr.Left;
		int num2 = r.Top - wr.Top;
		int num3 = wr.Right - r.Right;
		int num4 = wr.Bottom - r.Bottom;
		if (num < 0 || num2 < 0 || num3 < 0 || num4 < 0)
		{
			return default;
		}
		if (num > 40 || num2 > 40 || num3 > 40 || num4 > 40)
		{
			return default;
		}
		return new Native.RECT(num, num2, num3, num4);
	}

	private void OnWorkerMove(Native.POINT pt)
	{
		Session session;
		lock (_gate)
		{
			if (!_dragging)
			{
				return;
			}
			session = _session;
		}
		Step(pt, session);
	}

	private void Step(Native.POINT pt, Session s, bool forceWrite = false)
	{
		try
		{
			if (s.Hwnd == IntPtr.Zero)
			{
				return;
			}
			if (!Native.IsWindow(s.Hwnd))
			{
				Cancel();
				return;
			}
			AppConfig appConfig = _cfg();
			if (Native.GetClipCursor(out var lpRect) && lpRect.Right > lpRect.Left && lpRect.Bottom > lpRect.Top)
			{
				if (pt.X < lpRect.Left)
				{
					pt.X = lpRect.Left;
				}
				if (pt.X > lpRect.Right - 1)
				{
					pt.X = lpRect.Right - 1;
				}
				if (pt.Y < lpRect.Top)
				{
					pt.Y = lpRect.Top;
				}
				if (pt.Y > lpRect.Bottom - 1)
				{
					pt.Y = lpRect.Bottom - 1;
				}
			}
			if (_dragWait)
			{
				int dragOutThresholdPx = appConfig.DragOutThresholdPx;
				if (Math.Abs(pt.X - s.StartCursor.X) < dragOutThresholdPx && Math.Abs(pt.Y - s.StartCursor.Y) < dragOutThresholdPx)
				{
					return;
				}
				_dragWait = false;
			}
			int num = pt.X - s.StartCursor.X;
			int num2 = pt.Y - s.StartCursor.Y;
			if (Math.Abs(num) > 2 || Math.Abs(num2) > 2)
			{
				_moved = true;
			}
			Native.RECT rECT;
			if (s.Move)
			{
				rECT = new Native.RECT(s.StartRect.Left + num, s.StartRect.Top + num2, s.StartRect.Right + num, s.StartRect.Bottom + num2);
			}
			else
			{
				rECT = s.StartRect;
				Zones.EdgesOf(s.Zone, out var left, out var top, out var right, out var bottom);
				if (left)
				{
					rECT.Left = s.StartRect.Left + num;
				}
				if (right)
				{
					rECT.Right = s.StartRect.Right + num;
				}
				if (top)
				{
					rECT.Top = s.StartRect.Top + num2;
				}
				if (bottom)
				{
					rECT.Bottom = s.StartRect.Bottom + num2;
				}
				// 只夹"真的有拖动边"的轴：旧实现两个轴都夹，
				// 于是上下边缩放一个本来就很窄（< SM_CXMIN）的窗口时，宽度会被莫名撑到 112px。
				bool dragX = left || right;
				bool dragY = top || bottom;
				if (dragX && rECT.Width < s.MinW)
				{
					if (left)
					{
						rECT.Left = rECT.Right - s.MinW;
					}
					else
					{
						rECT.Right = rECT.Left + s.MinW;
					}
				}
				if (dragY && rECT.Height < s.MinH)
				{
					if (top)
					{
						rECT.Top = rECT.Bottom - s.MinH;
					}
					else
					{
						rECT.Bottom = rECT.Top + s.MinH;
					}
				}
			}
			lock (_gate)
			{
				if (forceWrite)
				{
					if (_session.Gen != s.Gen)
					{
						return;
					}
				}
				else if (!_dragging || _session.Gen != s.Gen)
				{
					return;
				}
			}
			if (s.Move)
			{
				if (_shakeGen != s.Gen)
				{
					int num3 = Native.DpiOfWindow(s.Hwnd);
					_shake.SetScale((double)num3 / 96.0);
					_shake.Reset(s.StartCursor.X, s.StartCursor.Y, Environment.TickCount64);
					_shakeGen = s.Gen;
				}
				if (appConfig.ShakeMinimizeOthers && _shake.Feed(pt.X, pt.Y, Environment.TickCount64))
				{
					ShakeAction.ToggleMinimizeOthers(s.Hwnd);
				}
			}
			if (appConfig.SnapToEdges && appConfig.SnapThreshold > 0 && !IsShiftDown() && (s.Move || s.Zone != ResizeZone.None))
			{
				Native.RECT work = WorkAreaOfPoint(pt);
				SnapEngine.Result snap = (s.Move ? SnapEngine.SnapMove(rECT, work, ScaledThreshold(appConfig.SnapThreshold, s.Hwnd)) : SnapEngine.SnapResize(rECT, s.Zone == ResizeZone.Left, s.Zone == ResizeZone.Right, s.Zone == ResizeZone.Top, s.Zone == ResizeZone.Bottom, work, ScaledThreshold(appConfig.SnapThreshold, s.Hwnd)));
				rECT = snap.Rect;
				if (snap.GuideX >= 0)
				{
					_guide.ShowLine(vertical: true, snap.GuideX, work.Top, work.Bottom, appConfig.EffectiveFrameColor);
				}
				else if (snap.GuideY >= 0)
				{
					_guide.ShowLine(vertical: false, snap.GuideY, work.Left, work.Right, appConfig.EffectiveFrameColor);
				}
				else
				{
					_guide.Hide();
				}
			}
			else
			{
				_guide.Hide();
			}
			if (appConfig.ShowDragHud)
			{
				string text = rECT.Width + " × " + rECT.Height;
				if (appConfig.DragHudShowPosition)
				{
					text = text + "  ·  " + rECT.Left + "," + rECT.Top;
				}
				_hud.ShowText(text, FrameRectOf(rECT, appConfig, out var _), (float)Native.DpiOfWindow(s.Hwnd) / 96f);
			}
			if (appConfig.ShowActivationFrame && AppRules.AllowsFrame(s.Hwnd))
			{
				Native.RECT rectNow = FrameRectOf(rECT, appConfig, out var inset);
				_frame.RaiseAboveAll = true;
				if (rectNow.Width >= 4 && rectNow.Height >= 4)
				{
					lock (_gate)
					{
						if (_dragging && _session.Gen == s.Gen && _frameWanted)
						{
							_frame.ApplyTheme(appConfig.FrameColorId, appConfig.FrameStyleId, appConfig.FrameThickness, appConfig.FrameRadius, appConfig.FrameOpacity, appConfig.EffectiveFrameColor);
							_frame.SetRectNow(rectNow);
							DiagFrameUpdates++;
						}
					}
				}
			}
			if (forceWrite)
			{
				WindowActions.SetDragRect(s.Hwnd, rECT);
			}
			else if (WindowActions.SetDragRectFrame(s.Hwnd, rECT, s.Move))
			{
				DiagWindowWritten++;
			}
			else
			{
				DiagWindowSkipped++;
			}
			if (Log.Verbose)
			{
				Native.GetWindowRect(s.Hwnd, out var lpRect2);
				string[] array = new string[9]
				{
					s.Move ? Loc.T("移动") : Loc.T("缩放"),
					Loc.T(" 光标="),
					pt.X.ToString(),
					",",
					pt.Y.ToString(),
					Loc.T(" 期望="),
					null,
					null,
					null
				};
				Native.RECT rECT2 = rECT;
				array[6] = rECT2.ToString();
				array[7] = Loc.T(" 实际=");
				rECT2 = lpRect2;
				array[8] = rECT2.ToString();
				Log.Trace(string.Concat(array));
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public void End()
	{
		if (!_dragging)
		{
			return;
		}
		try
		{
			AppConfig appConfig = _cfg();
			Session s;
			nint hwnd;
			lock (_gate)
			{
				s = _session;
				hwnd = _session.Hwnd;
			}
			bool cursorPos = Native.GetCursorPos(out var pt);
			lock (_gate)
			{
				_dragging = false;
				_generation++;
			}
			if (cursorPos && hwnd != IntPtr.Zero)
			{
				// 最后一帧几何写入与"退出尺寸移动"通知必须在同一个工作项里，且顺序固定：
				// 很多程序是在 WM_EXITSIZEMOVE 里保存窗口位置的，通知先到就会把"松手前"的矩形存下来。
				_sizeMoveActive = false;
				_worker.Post(() =>
				{
					Step(pt, s, forceWrite: true);
					WindowActions.NotifySizeMove(hwnd, start: false);
					_hud.Hide();
					_guide.Hide();
				});
				_undoHwnd = hwnd;
				_undoRect = s.StartRect;
				_undoAt = Environment.TickCount64;
			}
			else if (hwnd != IntPtr.Zero)
			{
				_sizeMoveActive = false;
				WindowActions.NotifySizeMove(hwnd, start: false);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			Cleanup();
		}
	}

	public void UpdateFinal(Native.POINT pt)
	{
		if (_dragging)
		{
			Session session;
			lock (_gate)
			{
				session = _session;
			}
			Step(pt, session, forceWrite: true);
		}
	}

	internal void PostTargetWork(Action work)
	{
		if (work == null)
		{
			return;
		}
		if (_worker != null && _worker.IsRunning)
		{
			_worker.Post(work);
			return;
		}
		try
		{
			work();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ReconcileStartRect(nint hwnd, int button, bool move, Native.POINT pt, int cornerPercent, Native.RECT real)
	{
		lock (_gate)
		{
			if (!_dragging || _moved || _session.Hwnd != hwnd || _session.Button != button)
			{
				return;
			}
			Native.RECT startRect = _session.StartRect;
			if (startRect.Left == real.Left && startRect.Top == real.Top && startRect.Right == real.Right && startRect.Bottom == real.Bottom)
			{
				return;
			}
			_session.StartRect = real;
			_inset = ComputeInset(hwnd, real);
			if (!move)
			{
				ResizeZone resizeZone = Zones.Resize(real, pt.X, pt.Y, cornerPercent);
				if (resizeZone == ResizeZone.Center)
				{
					resizeZone = Zones.NearestCorner(real, pt.X, pt.Y);
				}
				_session.Zone = resizeZone;
			}
			Log.Info("[诊断] 降级落账后校正会话矩形：" + startRect.Left + "," + startRect.Top + " " + startRect.Width + "x" + startRect.Height + " → " + real.Left + "," + real.Top + " " + real.Width + "x" + real.Height);
		}
	}

	internal void HideOverlaysNow()
	{
		_frameWanted = false;
		_dragging = false;
		try
		{
			_frame.HideNowSync();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	internal void EnforceFrameInvariant(bool triggerActive)
	{
		try
		{
			if (!_dragging && (!triggerActive || _hoverHwnd == IntPtr.Zero || !_hoverRectValid) && _frame.IsShown)
			{
				Log.Info("[兜底] 外框本不该显示（不在拖动、也没有悬停目标）→ 强制收掉");
				_frameWanted = false;
				_frame.HideNowSync();
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	internal void StepForTest(Native.POINT pt)
	{
		if (_dragging)
		{
			Session session;
			lock (_gate)
			{
				session = _session;
			}
			Step(pt, session);
		}
	}

	public void Cancel()
	{
		if (_dragging)
		{
			lock (_gate)
			{
				_dragging = false;
				_generation++;
			}
			Cleanup();
		}
	}

	private void Cleanup()
	{
		try
		{
			_frameWanted = false;
			// Cancel 路径（暂停、改配置、目标窗口没了）不会走 End 的通知，
			// 这里补发一次，否则目标窗口会一直以为自己还在尺寸拖动循环里（部分程序会卡在"拖拽模式"）。
			if (_sizeMoveActive)
			{
				_sizeMoveActive = false;
				nint hwnd;
				lock (_gate)
				{
					hwnd = _session.Hwnd;
				}
				WindowActions.NotifySizeMove(hwnd, start: false);
			}
			if (_timerResolutionRaised)
			{
				_timerResolutionRaised = false;
				try
				{
					Native.TimeEndPeriod(1u);
				}
				catch (Exception ex0)
				{
					Log.Error(ex0);
				}
			}
			if (_overlaysReady)
			{
				_frame.HideNowSync();
			_hud.Hide();
			_guide.Hide();
			}
			try
			{
				GCSettings.LatencyMode = GCLatencyMode.Interactive;
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
			// v1.3 空闲内存回收：拖动结束后外框位图就是一块纯闲置的大缓冲
			//（4K 屏上每个外框 ~33MB 托管 + ~33MB 非托管）。旧版一直攥着不放，
			// "用完一次拖窗，常驻内存就高几十 MB"。这里延迟 15 秒释放：
			// 期间若又开始拖动（位图会被 Rebuild 复用）就取消，避免抖动。
			ScheduleIdleBufferRelease();
		}
		catch (Exception ex2)
		{
			Log.Error(ex2);
		}
		finally
		{
			InvalidateHover();
			lock (_gate)
			{
				_session.Hwnd = IntPtr.Zero;
				_session.Button = -1;
			}
		}
	}

	private System.Threading.Timer _idleReleaseTimer;

	private int _idleReleaseGeneration;

	/// <summary>
	/// 15 秒后释放外框像素缓冲。
	///
	/// 必须用 System.Threading.Timer：这里经常由钩子线程（End）调用，
	/// 而 System.Windows.Forms.Timer 的 WM_TIMER 只会派发给"创建它的那条线程的消息泵"，
	/// 钩子线程的循环里从不 DispatchMessage —— 定时器永远不会触发，文档里承诺的几十 MB 回收是死代码。
	/// ReleaseBuffersForIdle 只动 GDI 缓冲与锁，不碰窗口消息，因此在线程池线程上执行是安全的。
	/// </summary>
	private void ScheduleIdleBufferRelease()
	{
		try
		{
			int num;
			lock (_gate)
			{
				num = _generation;
			}
			_idleReleaseGeneration = num;
			System.Threading.Timer timer = _idleReleaseTimer;
			if (timer == null)
			{
				timer = new System.Threading.Timer(OnIdleReleaseTick, null, Timeout.Infinite, Timeout.Infinite);
				_idleReleaseTimer = timer;
			}
			timer.Change(15000, Timeout.Infinite);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void OnIdleReleaseTick(object state)
	{
		try
		{
			int num;
			bool dragging = _dragging;
			lock (_gate)
			{
				num = _generation;
			}
			if (dragging || num != _idleReleaseGeneration || _frameWanted || (_hoverRectValid && _hoverHwnd != IntPtr.Zero))
			{
				return;
			}
			// 真闲着：把外框的像素缓冲放掉（窗口句柄保留，下次拖动 Rebuild 会重建）。
			_frame.ReleaseBuffersForIdle();
			Log.Trace("[内存] 拖动已结束 15 秒，外框像素缓冲已释放");
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ShowDragFrame(Native.RECT windowRect, AppConfig c)
	{
		if (_overlaysReady)
		{
			Native.RECT rect = FrameRectOf(windowRect, c, out var _);
			_frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius, c.FrameOpacity, c.EffectiveFrameColor);
			if (rect.Width >= 4 && rect.Height >= 4)
			{
				_frame.SetRect(rect);
			}
		}
	}

	private void ShowFrame(Native.RECT windowRect)
	{
		if (_overlaysReady && _frameWanted)
		{
			AppConfig appConfig = _cfg();
			_frame.ApplyTheme(appConfig.FrameColorId, appConfig.FrameStyleId, appConfig.FrameThickness, appConfig.FrameRadius, appConfig.FrameOpacity, appConfig.EffectiveFrameColor);
			Native.RECT rect = FrameRectOf(windowRect, appConfig, out var inset);
			_frame.RaiseAboveAll = true;
			if (rect.Width >= 4 && rect.Height >= 4)
			{
				_frame.SetRect(rect);
			}
		}
	}

	internal bool TryShowFrame(Native.RECT windowRect, AppConfig c, int gen)
	{
		if (!_overlaysReady)
		{
			return false;
		}
		Native.RECT rect = FrameRectOf(windowRect, c, out var inset);
		_frame.RaiseAboveAll = true;
		if (rect.Width < 4 || rect.Height < 4)
		{
			return false;
		}
		lock (_gate)
		{
			if (!_dragging || _session.Gen != gen || !_frameWanted)
			{
				return false;
			}
			_frame.SetRect(rect);
			return true;
		}
	}

	private Native.RECT FrameRectOf(Native.RECT windowRect, AppConfig c, out bool inset)
	{
		int num = FrameStyles.MarginFor(c.FrameStyleId, c.FrameThickness);
		Native.RECT rECT = new Native.RECT(windowRect.Left + _inset.Left, windowRect.Top + _inset.Top, windowRect.Right - _inset.Right, windowRect.Bottom - _inset.Bottom);
		if (!FrameStyles.FillsScreen(rECT, num, _frameInside))
		{
			inset = false;
			_frameInside = false;
			return new Native.RECT(rECT.Left - num, rECT.Top - num, rECT.Right + num, rECT.Bottom + num);
		}
		inset = true;
		_frameInside = true;
		return rECT;
	}


	/// <summary>
	/// 触发键按住 1.2 秒还没开始拖 → 在光标旁浮一条操作提示。
	///
	/// 键盘分屏、Shift 关吸附这些能力没有可见入口，用户基本发现不了；
	/// 与其在设置里写一堆说明，不如在真正用得到的那一刻提示一次。
	/// </summary>
	private void ArmHintTimer(Native.POINT pt)
	{
		try
		{
			if (!_cfg().ShowTriggerHint)
			{
				return;
			}
			if (_hintTimer == null)
			{
				_hintTimer = new System.Threading.Timer(OnHintTimer, null, Timeout.Infinite, Timeout.Infinite);
			}
			_hintTimer.Change(1200, Timeout.Infinite);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>提示浮层的延时回调（复用同一个 Timer，避免在高频路径上反复 new）。</summary>
	private void OnHintTimer(object state)
	{
		try
		{
			lock (_gate)
			{
				if (_dragging || !_hoverRectValid || !_frameWanted)
				{
					return;
				}
			}
			Native.RECT rect = _hoverRect;
			nint hwnd = _hoverHwnd;
			string text = HintText();
			if (!string.IsNullOrEmpty(text))
			{
				_hint.ShowText(text, rect, (float)Native.DpiOfWindow(hwnd) / 96f);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private string HintText()
	{
		try
		{
			AppConfig appConfig = _cfg();
			List<string> list = new List<string>();
			list.Add(Loc.T("左键") + " " + Actions.DisplayName(appConfig.ActionLeft));
			list.Add(Loc.T("右键") + " " + Actions.DisplayName(appConfig.ActionRight));
			if (appConfig.KeyboardTiling)
			{
				list.Add(Loc.T("方向键 分屏"));
			}
			if (appConfig.SnapToEdges && appConfig.SnapThreshold > 0)
			{
				list.Add(Loc.T("Shift 不吸附"));
			}
			return string.Join("  ·  ", list);
		}
		catch
		{
			return string.Empty;
		}
	}

	internal void ProbeShowTriggerHintForTest()
	{
		_hint.Start();
		Native.POINT pt = default(Native.POINT);
		Native.GetCursorPos(out pt);
		Native.RECT rect = new Native.RECT(pt.X - 200, pt.Y - 100, pt.X + 400, pt.Y + 300);
		_hint.ShowText(HintText(), rect, 1f);
	}

	internal void ProbeHideTriggerHintForTest()
	{
		CancelHintTimer();
	}

	internal bool HintVisibleForTest
	{
		get
		{
			Thread.Sleep(100);
			return _hint.VisibleForTest;
		}
	}

	internal string HintTextForTest => HintText();

	internal static bool ProbeShiftDownForTest() => IsShiftDown();

	private void CancelHintTimer()
	{
		try
		{
			_hintTimer?.Dispose();
			_hintTimer = null;
			_hint.Hide();
		}
		catch
		{
		}
	}

	public void ShowHoverFrame(Native.POINT pt)
	{
		if (!_overlaysReady || _dragging)
		{
			return;
		}
		AppConfig appConfig = _cfg();
		if (!appConfig.ShowActivationFrame)
		{
			HideHoverFrame();
			return;
		}
		nint num = Native.WindowFromPoint(pt);
		if (num == IntPtr.Zero)
		{
			HideHoverFrame();
			return;
		}
		if (num != _hoverHwnd || !_hoverRectValid)
		{
			CancelHintTimer();
			ArmHintTimer(pt);
		}
		if (num == _hoverRaw)
		{
			if (_hoverHwnd == IntPtr.Zero || !Native.IsWindow(_hoverHwnd))
			{
				InvalidateHover();
			}
			else
			{
				if (Native.GetWindowRect(_hoverHwnd, out var lpRect))
				{
					if (!_frame.IsShown || !_hoverRectValid || lpRect.Left != _hoverRect.Left || lpRect.Top != _hoverRect.Top || lpRect.Right != _hoverRect.Right || lpRect.Bottom != _hoverRect.Bottom)
					{
						_hoverRect = lpRect;
						_hoverRectValid = true;
						_frameWanted = true;
						ShowFrame(lpRect);
					}
					return;
				}
				InvalidateHover();
			}
		}
		nint num2 = WindowActions.FindTarget(pt, appConfig.AllowFullscreenWindows || appConfig.ExitFullscreenOnTrigger);
		if (num2 == IntPtr.Zero)
		{
			_hoverRaw = num;
			HideHoverFrame();
			return;
		}
		if (!Native.GetWindowRect(num2, out var lpRect2))
		{
			_hoverRaw = num;
			HideHoverFrame();
			return;
		}
		_hoverRaw = num;
		_hoverHwnd = num2;
		_hoverRect = lpRect2;
		_hoverRectValid = true;
		_inset = ComputeInset(num2, lpRect2);
		_frameWanted = true;
		ShowFrame(lpRect2);
	}

	public void RefreshOverlayTheme()
	{
		InvalidateHover();
		if (!_overlaysReady)
		{
			return;
		}
		try
		{
			AppConfig appConfig = _cfg();
			_frame.ApplyTheme(appConfig.FrameColorId, appConfig.FrameStyleId, appConfig.FrameThickness, appConfig.FrameRadius, appConfig.FrameOpacity, appConfig.EffectiveFrameColor);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public void InvalidateHover()
	{
		_hoverRaw = IntPtr.Zero;
		_hoverHwnd = IntPtr.Zero;
		_hoverRectValid = false;
	}

	public void HideHoverFrame()
	{
		_hoverRaw = IntPtr.Zero;
		_hoverHwnd = IntPtr.Zero;
		_hoverRectValid = false;
		if (!_dragging)
		{
			_frameWanted = false;
			if (_overlaysReady)
			{
				_frame.HideOverlay();
			}
		}
	}
}
