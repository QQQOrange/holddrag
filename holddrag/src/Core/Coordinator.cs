// HoldDrag · Core/Coordinator.cs

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

internal sealed class Coordinator : IInputSink, IDisposable
{
	private const int BtnLeft = 0;

	private const int BtnRight = 1;

	private const int BtnMiddle = 2;

	private readonly ConfigStore _store;

	private readonly TriggerState _trigger;

	private readonly DragEngine _drag;

	private readonly long[] _lastDown = new long[3];

	private readonly bool[] _lastMoved = new bool[3];

	private readonly bool[] _swallowUp = new bool[3];

	internal Action ReinstallHooksForPriority;

	internal static double HookMaxMsForTest;

	internal static int SlowHookCountForTest;

	private const double SlowHookMs = 8.0;

	private static long _slowHookLoggedAt;

	private readonly System.Windows.Forms.Timer _frameGuard = new System.Windows.Forms.Timer();

	internal static int CancelModeSentForTest;

	internal static int ReplayedClickForTest;

	internal static bool[] ButtonsDownOverrideForTest;

	internal static bool AssumeButtonsDownForTest;

	private readonly bool[] _heldDuringHold = new bool[3];

	private readonly bool[] _passedDown = new bool[3];

	private readonly bool[] _bufferedDown = new bool[3];

	/// <summary>激活瞬间锁定的目标窗口：键盘布局用它，避免鼠标一抖就换窗口。</summary>
	private nint _activeTarget;

	private readonly Native.POINT[] _bufferedPt = new Native.POINT[3];

	public bool Paused { get; private set; }

	public bool SuppressTrigger { get; set; }

	public TriggerState Trigger => _trigger;

	public DragEngine Drag => _drag;

	public Coordinator(ConfigStore store)
	{
		_store = store;
		_trigger = new TriggerState(() => _store.Current);
		_drag = new DragEngine(() => _store.Current);
		WindowActions.ApplyBlacklist(_store.Current.Blacklist);
		WindowActions.AllowDragOwnWindows = _store.Current.AllowDragOwnWindows;
		_trigger.Activated += OnTriggerActivated;
		_trigger.Deactivated += OnTriggerDeactivated;
		_trigger.ShortPressWithoutActivation += FlushBufferedClicks;
	}

	private void OnTriggerActivated()
	{
		// 新一轮激活 = 新一轮交互：上一轮会话留下的点击时间戳必须作废。
		// 否则「先拖一次窗口 → 再按住触发键按下左键」会被当成双击最大化
		//（_lastDown 还在双击时间窗内，而这次按下又不是"按住期间/已放行"的按下，!flag 成立）。
		for (int i = 0; i <= 2; i++)
		{
			_lastDown[i] = 0L;
			_lastMoved[i] = false;
		}
		if (!Paused && !SuppressTrigger && !_drag.IsDragging)
		{
			Action reinstallHooksForPriority = ReinstallHooksForPriority;
			if (reinstallHooksForPriority != null)
			{
				try
				{
					reinstallHooksForPriority();
				}
				catch (Exception ex)
				{
					Log.Error(ex);
				}
			}
		}
		if (!Native.GetCursorPos(out var lpPoint))
		{
			return;
		}
		AppConfig current = _store.Current;
		bool allowFullscreen = current.AllowFullscreenWindows || current.ExitFullscreenOnTrigger;
		nint num = WindowActions.FindTarget(lpPoint, allowFullscreen);
		_activeTarget = num;
		if (num != IntPtr.Zero)
		{
			bool flag = false;
			for (int i = 0; i <= 2; i++)
			{
				if (_passedDown[i])
				{
					flag = true;
				}
			}
			if (flag)
			{
				Log.Info("[诊断] 激活时目标窗口还按着我们放行过的鼠标键（它可能停在拖选模态） → 先取消 句柄=" + ((IntPtr)num).ToInt64().ToString("X"));
			}
			CancelTargetMouseMode(num);
		}
		// ★ 激活瞬间就把"阈值期间被缓冲、下层从未收到"的鼠标按下补起成拖动。
		// 用户「同时按触发键和左键」时，左键的按下几乎一定落在阈值窗口内（被缓冲），
		// 以前要等激活后的第一次鼠标移动才补起 —— 按下后先不动就完全没有反馈，
		// 表现成"左键什么功能都没有、也拖不动窗口"。
		AdoptBufferedButtonsNow(lpPoint);
		if (!_drag.IsDragging)
		{
			_drag.ShowHoverFrame(lpPoint);
		}
	}


	/// <summary>Ctrl+Z：撤销上一次拖动（5 秒内有效）。</summary>
	private bool TryHandleUndo(ref Native.KBDLLHOOKSTRUCT kb)
	{
		try
		{
			if (!_store.Current.UndoEnabled || kb.vkCode != 0x5A)
			{
				return false;
			}
			if ((Native.GetAsyncKeyState(0x11) & 0x8000) == 0)
			{
				return false;
			}
			if (!_drag.TryUndoLast())
			{
				return false;
			}
			Log.Info("[撤销] Ctrl+Z：已还原上一次拖动");
			return true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	/// <summary>触发键按住期间用方向键 / 数字键布局目标窗口。</summary>
	private bool TryHandleTiling(ref Native.KBDLLHOOKSTRUCT kb)
	{
		try
		{
			if (!_store.Current.KeyboardTiling || !_trigger.Active || _drag.IsDragging)
			{
				return false;
			}
			nint activeTarget = _activeTarget;
			if (activeTarget == IntPtr.Zero || !Native.IsWindow(activeTarget))
			{
				return false;
			}
			if (!Native.GetCursorPos(out var pt))
			{
				return false;
			}
			Native.RECT work = WorkAreaOf(pt);
			int vk = (int)kb.vkCode;
			Native.RECT? rect = null;

			switch (vk)
			{
			case 0x25:
				rect = Tiling.Half(work, left: true);
				break;
			case 0x27:
				rect = Tiling.Half(work, left: false);
				break;
			case 0x26:
				Native.ShowWindow(activeTarget, 3);
				Log.Info("[布局] ↑ 最大化");
				return true;
			case 0x28:
				Native.ShowWindow(activeTarget, 9);
				Log.Info("[布局] ↓ 还原");
				return true;
			case 0x30:
			{
				Native.RECT current;
				if (Native.TryGetVisibleRect(activeTarget, out var r))
				{
					current = r;
				}
				else if (!Native.GetWindowRect(activeTarget, out current))
				{
					return false;
				}
				rect = Tiling.Centered(work, current);
				break;
			}
			default:
				if (vk >= 0x31 && vk <= 0x39)
				{
					rect = Tiling.Zone(vk - 0x30, work);
				}
				else if (vk >= 0x61 && vk <= 0x69)
				{
					rect = Tiling.Zone(vk - 0x60, work);
				}
				break;
			}
			if (!rect.HasValue)
			{
				return false;
			}
			WindowActions.SetVisibleRect(activeTarget, rect.Value);
			Log.Info("[布局] 键=" + vk + " 目标=" + ((IntPtr)activeTarget).ToInt64().ToString("X") + " → " + rect.Value.Left + "," + rect.Value.Top + " " + rect.Value.Width + "x" + rect.Value.Height);
			return true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return false;
		}
	}

	private static Native.RECT WorkAreaOf(Native.POINT pt)
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

	private void OnTriggerDeactivated()
	{
		_activeTarget = IntPtr.Zero;
		// 松开触发键 = 退出拖动模式。此刻若还在拖（真实链路上"鼠标抬起"这条事件同样可能
		// 落在鼠标钩子重装的缝隙里丢掉），必须就地收尾 —— 否则窗口会一直跟着光标跑，
		// 用户看到的是"松开 Enter 了窗口还在被拖着"，比拖不动更莫名其妙。
		if (_drag.IsDragging)
		{
			try
			{
				_drag.End();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		}
		_drag.HideHoverFrame();
	}


	// ======================= 合成按下的「欠账」机制 =======================

	/// <summary>
	/// 我们注入过、「只有按下还没有抬起」的鼠标键位掩码（bit0 左 / bit1 右 / bit2 中）。
	///
	/// 触发键短按时，程序会把阈值期间被吞掉的鼠标按下**只补一个 down** 还给下层，
	/// 依赖「用户真实的 up 稍后送达」来配对。这个假设在下面这些情况下是假的：
	/// 钩子被看门狗重装、暂停、改配置、UAC 安全桌面、程序退出 —— 期间的输入全部丢失。
	/// 一旦配对丢了，Windows 里就永远留着一个「按着」的键：所有程序都以为用户还按着左键，
	/// 用户却根本没按（v1.10.0 之后的报障：鼠标左键一直长按没停过）。
	/// 规则：注入 down 时记账；真实 up 到达时销账；任何「配对不可能再到达」的时刻补发 up。
	/// </summary>
	private int _injectedDownMask;

	/// <summary>第一次注入（还欠着 up）的时刻，用于兜底超时。</summary>
	private long _injectedDownAt;

	private readonly object _ledgerGate = new object();

	/// <summary>自检用：只跑账本逻辑，不真的往系统注入鼠标事件（避免在用户桌面上点出真实点击）。</summary>
	internal static bool SuppressSyntheticMouseForTest;

	/// <summary>自检用：当前还欠着抬起的合成按下掩码。</summary>
	internal int InjectedDownMaskForTest
	{
		get
		{
			lock (_ledgerGate)
			{
				return _injectedDownMask;
			}
		}
	}

	private void MarkInjectedDown(int idx)
	{
		if (idx < 0 || idx > 2)
		{
			return;
		}
		lock (_ledgerGate)
		{
			if (_injectedDownMask == 0)
			{
				_injectedDownAt = Environment.TickCount64;
			}
			_injectedDownMask |= 1 << idx;
		}
	}

	/// <summary>真实的抬起到了：注入出去的那次按下已经配对，销账（不能再补发第二次 up）。</summary>
	private void PairInjectedDown(int idx)
	{
		if (idx < 0 || idx > 2)
		{
			return;
		}
		lock (_ledgerGate)
		{
			_injectedDownMask &= ~(1 << idx);
			if (_injectedDownMask == 0)
			{
				_injectedDownAt = 0L;
			}
		}
	}

	/// <summary>
	/// 把还欠着的抬起补发出去（系统层面真正松开），并销账。
	/// 只在「配对不可能再到达」时调用：钩子重装、暂停、改配置、退出。
	/// </summary>
	private void ReleaseInjectedDowns(string reason)
	{
		int num;
		lock (_ledgerGate)
		{
			num = _injectedDownMask;
			_injectedDownMask = 0;
			_injectedDownAt = 0L;
		}
		if (num == 0)
		{
			return;
		}
		for (int i = 0; i <= 2; i++)
		{
			if ((num & (1 << i)) != 0)
			{
				SendMouseButton(i, down: false);
			}
		}
		Log.Info("补发残留的合成按键抬起（" + reason + "）：掩码=" + num + " —— 不补的话系统会一直以为这个键按着");
	}

	/// <summary>
	/// 兜底巡检（每 500ms）：只处理「我们注入过 down、却再也不会等到配对 up」的情形。
	/// 系统层面已经不是按下状态 → 只销账；还按着而我们早就不该按着 → 超过 8 秒补发 up。
	/// </summary>
	private void SweepInjectedDowns()
	{
		int num;
		long num2;
		lock (_ledgerGate)
		{
			num = _injectedDownMask;
			num2 = _injectedDownAt;
		}
		if (num == 0)
		{
			return;
		}
		if (_drag.IsDragging || _trigger.Held || _trigger.Active)
		{
			return;
		}
		bool flag = false;
		for (int i = 0; i <= 2; i++)
		{
			if ((num & (1 << i)) != 0 && IsButtonPhysicallyDown(i))
			{
				flag = true;
			}
		}
		if (!flag)
		{
			lock (_ledgerGate)
			{
				_injectedDownMask = 0;
				_injectedDownAt = 0L;
			}
			return;
		}
		if (num2 != 0L && Environment.TickCount64 - num2 > 8000)
		{
			ReleaseInjectedDowns("配对的抬起 8 秒都没到达");
		}
	}

	/// <summary>注入单个鼠标键的按下/抬起（带本程序标记，钩子会忽略自己的合成事件）。</summary>
	private static void SendMouseButton(int idx, bool down)
	{
		try
		{
			uint num = idx switch
			{
				1 => (down ? 8u : 16u), 
				0 => (down ? 2u : 4u), 
				_ => (down ? 32u : 64u), 
			};
			if (SuppressSyntheticMouseForTest)
			{
				return;
			}
			Native.INPUT[] array = new Native.INPUT[1];
			array[0].type = 0u;
			array[0].u.mi.dwFlags = num;
			array[0].u.mi.dwExtraInfo = HookEngine.MagicExtraInfo;
			Native.SendInput(1u, array, Marshal.SizeOf(typeof(Native.INPUT)));
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	/// <summary>清空鼠标三本账（放行 / 缓冲 / 阈值期按住）。</summary>
	private void ForgetButtonLedger(string reason)
	{
		for (int i = 0; i <= 2; i++)
		{
			_lastDown[i] = 0L;
			_lastMoved[i] = false;
			_swallowUp[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
			_bufferedDown[i] = false;
		}
		if (!string.IsNullOrEmpty(reason))
		{
			Log.Info("鼠标按键账本已清零：" + reason);
		}
	}

	/// <summary>
	/// 钩子被真正重装之后（看门狗救援）：重装缝隙里的按下/抬起全丢了，
	/// 鼠标三本账与触发状态必须归零，还欠着的合成抬起也要立刻补掉。
	/// </summary>
	internal void OnHooksReinstalled()
	{
		try
		{
			ForgetButtonLedger("钩子重装");
			ReleaseInjectedDowns("钩子重装，缝隙里的抬起已丢失");
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	public void PrecreateOverlays()
	{
		_drag.Precreate();
		_frameGuard.Interval = 500;
		_frameGuard.Tick += (object? s, EventArgs e) =>
		{
			try
			{
				_drag.EnforceFrameInvariant(_trigger.Active);
				SweepInjectedDowns();
			}
			catch (Exception ex)
			{
				Log.Error(ex);
			}
		};
		_frameGuard.Start();
	}

	private static void NoteHookCost(long startedAt, string what)
	{
		try
		{
			double num = (double)(Stopwatch.GetTimestamp() - startedAt) * 1000.0 / (double)Stopwatch.Frequency;
			if (num > HookMaxMsForTest)
			{
				HookMaxMsForTest = num;
			}
			if (!(num < 8.0))
			{
				SlowHookCountForTest++;
				long tickCount = Environment.TickCount64;
				if (tickCount - _slowHookLoggedAt >= 1000)
				{
					_slowHookLoggedAt = tickCount;
					Log.Info("[诊断] 钩子回调偏慢 " + what + " 耗时=" + num.ToString("0.0") + "ms（系统级钩子：这一段时间整机输入都被拖住）");
				}
			}
		}
		catch
		{
		}
	}

	public void HideOverlaysNow()
	{
		try
		{
			_frameGuard.Stop();
		}
		catch
		{
		}
		_drag.HideOverlaysNow();
	}

	public void SetPaused(bool paused)
	{
		Paused = paused;
		if (paused)
		{
			// 暂停期间收不到任何输入，触发键的 up 事件必然丢失；不复位的话 _held/_active 会永久卡住，
			// 恢复后表现为"触发键失灵"或"每次点鼠标都在拖窗口"。
			ReleaseInjectedDowns("暂停");
			_trigger.Reset("暂停");
			_drag.Cancel();
			_drag.HideHoverFrame();
		}
		Log.Info(paused ? "已暂停" : "已恢复");
	}

	public void OnConfigChanged()
	{
		_drag.Cancel();
		// 换触发键 / 改长按阈值时，旧键的松开事件可能永远不会再来，同样要复位。
		ReleaseInjectedDowns("配置变更");
		_trigger.Reset("配置变更");
		WindowActions.ApplyBlacklist(_store.Current.Blacklist);
		WindowActions.AllowDragOwnWindows = _store.Current.AllowDragOwnWindows;
		for (int i = 0; i < 3; i++)
		{
			_lastDown[i] = 0L;
			_lastMoved[i] = false;
			_swallowUp[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
			_bufferedDown[i] = false;
		}
		_drag.RefreshOverlayTheme();
		if (_trigger.Active && !Paused && Native.GetCursorPos(out var lpPoint))
		{
			_drag.ShowHoverFrame(lpPoint);
		}
	}

	public void Dispose()
	{
		// 退出前把欠着的合成抬起补掉：否则进程一走，系统里那个「按着」的键就再也没人放了
		ReleaseInjectedDowns("程序退出");
		try
		{
			_frameGuard.Stop();
			_frameGuard.Dispose();
		}
		catch
		{
		}
		_drag.Dispose();
		_trigger.Dispose();
	}

	public bool OnKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb)
	{
		long timestamp = Stopwatch.GetTimestamp();
		try
		{
			if (Paused || SuppressTrigger)
			{
				return false;
			}
			if (down && TryHandleUndo(ref kb))
			{
				return true;
			}
			if (_trigger.HandleKeyboard(down, ref kb))
			{
				return true;
			}
			if (down && TryHandleTiling(ref kb))
			{
				return true;
			}
			return false;
		}
		finally
		{
			NoteHookCost(timestamp, down ? Loc.T("按键按下") : Loc.T("按键抬起"));
		}
	}

	public bool OnMouse(int msg, ref Native.MSLLHOOKSTRUCT ms)
	{
		long timestamp = Stopwatch.GetTimestamp();
		try
		{
			return OnMouseCore(msg, ref ms);
		}
		finally
		{
			NoteHookCost(timestamp, Loc.T("鼠标消息"));
		}
	}

	private bool OnMouseCore(int msg, ref Native.MSLLHOOKSTRUCT ms)
	{
		if (Paused || SuppressTrigger)
		{
			int num = ButtonIndexOf(msg);
			if (num >= 0 && !IsDownMessage(msg))
			{
				// 四个标志一起清：只清两个的话，暂停前刚缓冲下来的 _bufferedDown / _swallowUp
				// 会在恢复后吃掉一次不相干的抬起（目标窗口留下一个没有配对按下的"粘滞按键"），
				// 或者让下一次短按补发时多注入一个幽灵左键。
				_heldDuringHold[num] = false;
				_passedDown[num] = false;
				_swallowUp[num] = false;
				_bufferedDown[num] = false;
				// 暂停期间的抬起也是真实抬起：同样销掉合成按下的账
				PairInjectedDown(num);
			}
			return false;
		}
		switch (msg)
		{
		case 523:
		case 524:
			if (_trigger.HandleMouseTrigger(msg, msg == 523, ref ms))
			{
				return true;
			}
			return false;
		case 512:
			_drag.DeliverCursor(ms.pt);
			if (_trigger.Active && !_drag.IsDragging)
			{
				AdoptHeldButtonIfAny(ms.pt);
				if (!_drag.IsDragging)
				{
					_drag.ShowHoverFrame(ms.pt);
				}
			}
			return false;
		default:
		{
			int num2 = ButtonIndexOf(msg);
			if (num2 < 0)
			{
				return false;
			}
			// 触发键按住/激活期间，每一次鼠标键按下抬起都留痕。
			// 这两个状态之外不记（否则拖窗口时日志会被"抬起"淹没）。
			// 目的：用户报"外框出现了但窗口拖不动"时，日志必须能直接读出
			// 「这次按下到底进了哪个分支、缓冲有没有被清掉」。
			if (_trigger.Held || _trigger.Active)
			{
				Log.Info("[诊断] 鼠标键" + (IsDownMessage(msg) ? "按下" : "抬起") + " 键=" + num2 + " 触发[按住=" + _trigger.Held + " 激活=" + _trigger.Active + "] 缓冲=" + _bufferedDown[num2] + " 阈值期按住=" + _heldDuringHold[num2] + " 已放行=" + _passedDown[num2] + " 拖动中=" + _drag.IsDragging + " 坐标=" + ms.pt.X + "," + ms.pt.Y);
			}
			if (IsDownMessage(msg))
			{
				if (_trigger.Active)
				{
					return HandleButtonDown(num2, ms.pt);
				}
				bool hasAction = ActionFor(num2, _store.Current) != "Nothing";
				if (_trigger.Held & hasAction)
				{
					// 指针停在自有窗口的可交互控件上时直接放行、不做缓冲。
					// 否则用户按住不动地拖一会儿滑块，HoldMs 一到就会被本程序抢走这次按下。
					if (WindowActions.IsPointOverOwnInteractiveControl(ms.pt))
					{
						_passedDown[num2] = true;
						return false;
					}
					_heldDuringHold[num2] = true;
					_bufferedDown[num2] = true;
					_bufferedPt[num2] = ms.pt;
					return true;
				}
				_passedDown[num2] = true;
				return false;
			}
			_heldDuringHold[num2] = false;
			_passedDown[num2] = false;
			_bufferedDown[num2] = false;
			// 真实的抬起到了：之前补发出去的按下已经配对，不能再补一次 up
			PairInjectedDown(num2);
			return HandleButtonUp(num2, ms.pt);
		}
		}
	}

	private void AdoptHeldButtonIfAny(Native.POINT pt)
	{
		// 触发键已激活、指针又停在自有窗口的可交互控件上 → 这次按下不抢。
		// 被缓冲过（从未投递给任何窗口）的情况补一次按下，让控件的拖拽（拖滑块、选文本）延续下去。
		if (WindowActions.IsPointOverOwnInteractiveControl(pt))
		{
			for (int j = 0; j <= 2; j++)
			{
				if (!_bufferedDown[j])
				{
					// 账本里没有"被我们吞掉、还没还给用户"的按下 → 没什么可还的。
					_heldDuringHold[j] = false;
					_passedDown[j] = false;
					continue;
				}
				_bufferedDown[j] = false;
				_heldDuringHold[j] = false;
				_passedDown[j] = false;
				ReplayBufferedClick(j, keepDown: true);
				Log.Info("[诊断] 触发键激活但指针停在自有窗口控件上 → 把缓冲的按下还给控件 键=" + j + " 控件=" + WindowActions.DescribeInteractiveAt(pt));
			}
			return;
		}
		// ★★ 第六轮核心修复：判据只用【钩子事件账本】，绝不再用 GetAsyncKeyState（IsButtonPhysicallyDown）。
		//
		// 实测（%AppData%\HoldDrag\holddrag.log，21:07:47.635）：
		//   [诊断] 激活瞬间清点缓冲 缓冲[True,False,False] 阈值期按住[True,False,False]
		//                          已放行[False,False,False] 物理按下[False,False,False]
		// 左键明明还按着（真实按下在 47.473 收到、131ms 后才抬起），
		// GetAsyncKeyState(VK_LBUTTON) 却报 False —— 于是老代码在
		//   if (!IsButtonPhysicallyDown(i)) { _bufferedDown[i] = false; continue; }
		// 这一行把【刚刚收到的、还热乎的】缓冲按下当成"用户已经松手"整条丢掉，
		// 之后每一次鼠标移动又被同一个查询挡回去 ⇒ 拖动永远建不起来，
		// 而外框由 ShowHoverFrame 照常画出 —— 这正是用户看到的
		// 「外框被样式包围，但鼠标怎么动窗口都移动不了」。
		//
		// 为什么账本才是可靠信号：我们能**直接看到**每一次鼠标键按下/抬起（低级鼠标钩子），
		// 而 GetAsyncKeyState 在这个进程里被实测证明会漏报（注入输入尤其明显）。
		// 账本语义精确且自洽：收到按下 + 尚未收到配对的抬起 ⟺ 键还按着；
		// 抬起分支（OnMouseCore 的 up 分支）会把三本账全部清零，所以账本不会长期失真。
		// 各种时序（按下落在阈值窗口内 / 按下早于触发键 / 鼠标在激活前就已经在动）到这里都归一。
		for (int i = 0; i <= 2; i++)
		{
			bool flag = _bufferedDown[i];
			bool flag2 = _heldDuringHold[i];
			bool flag3 = _passedDown[i];
			if (!flag && !flag2 && !flag3)
			{
				continue;
			}
			if (ActionFor(i, _store.Current) == "Nothing")
			{
				_bufferedDown[i] = false;
				_heldDuringHold[i] = false;
				_passedDown[i] = false;
				continue;
			}
			_bufferedDown[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
			if (HandleButtonDown(i, pt, quiet: true) && _drag.IsDragging)
			{
				Log.Info("[诊断] 触发键已激活且账本里还有未配对的按下 → 补起拖动" + (flag ? "（按下曾被缓冲，从未投递给任何窗口）" : (flag2 ? "（按下落在触发键按住阶段内）" : "（按下发生在触发键按住之前，目标可能停在拖选模态）")) + " 键=" + i + " 坐标=" + pt.X + "," + pt.Y);
				break;
			}
		}
	}

	/// <summary>
	/// 触发键激活的<b>瞬间</b>：把"阈值期间被缓冲、从未投递给任何窗口"的鼠标按下立刻补起成拖动。
	/// </summary>
	/// <remarks>
	/// 为什么可以立刻接管：这些按下从头到尾没有离开过本进程，目标窗口根本没见到 WM_*BUTTONDOWN，
	/// 所以不存在"用户其实是想在那里选文字/拖选区"的可能 —— 接管它不会破坏任何既有语义。
	/// 反过来，如果一定要等到激活后的第一次鼠标移动才补起，那么用户"同时按住触发键和左键、
	/// 按下后先不动"这段时间里就完全没有反馈，表现就是"左键什么功能都没有、也拖不动窗口"。
	/// 已放行给下层的按下（_passedDown）刻意不走这里：那种按下目标窗口实收了，必须靠用户移动来表明意图，
	/// 否则"先选文字、再按触发键"会被误判成拖窗口。
	/// </remarks>
	private void AdoptBufferedButtonsNow(Native.POINT pt)
	{
		// 「物理按下」那一栏仅作对照留痕：实测它对本进程会漏报（见 AdoptHeldButtonIfAny 的注释），
		// 所以它已经不参与任何判定，只帮助事后核对。
		Log.Info("[诊断] 激活瞬间清点缓冲 缓冲[" + _bufferedDown[0] + "," + _bufferedDown[1] + "," + _bufferedDown[2] + "] 阈值期按住[" + _heldDuringHold[0] + "," + _heldDuringHold[1] + "," + _heldDuringHold[2] + "] 已放行[" + _passedDown[0] + "," + _passedDown[1] + "," + _passedDown[2] + "] 参考·物理按下[" + IsButtonPhysicallyDown(0) + "," + IsButtonPhysicallyDown(1) + "," + IsButtonPhysicallyDown(2) + "] 坐标=" + pt.X + "," + pt.Y);
		for (int i = 0; i <= 2; i++)
		{
			if (!_bufferedDown[i])
			{
				continue;
			}
			// ★ 这里曾经有一条 `if (!IsButtonPhysicallyDown(i)) { 清账并 continue; }`。
			// 它就是本轮报障的真凶：抬起分支已经把"用户松手"这件事记得明明白白，
			// 再用一个会漏报的系统查询去二次判断，只会把好账当成坏账丢掉。
			if (WindowActions.IsPointOverOwnInteractiveControl(pt))
			{
				_bufferedDown[i] = false;
				_heldDuringHold[i] = false;
				_passedDown[i] = false;
				ReplayBufferedClick(i, keepDown: true);
				Log.Info("[诊断] 激活瞬间指针停在自有窗口控件上 → 把缓冲的按下还给控件 键=" + i + " 控件=" + WindowActions.DescribeInteractiveAt(pt));
				continue;
			}
			_bufferedDown[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
			if (HandleButtonDown(i, pt, quiet: true) && _drag.IsDragging)
			{
				Log.Info("[诊断] 激活瞬间补起拖动（阈值期间的按下从未投递给任何窗口，无需再等一次鼠标移动）键=" + i + " 坐标=" + pt.X + "," + pt.Y);
				break;
			}
			Log.Info("[诊断] 激活瞬间补起拖动未成立（对照上一条「鼠标键按下」日志的坐标与目标）键=" + i);
		}
	}

	private static void CancelTargetMouseMode(nint hwnd)
	{
		try
		{
			if (hwnd != IntPtr.Zero)
			{
				Native.PostMessageW(hwnd, 31, IntPtr.Zero, IntPtr.Zero);
				CancelModeSentForTest++;
			}
		}
		catch
		{
		}
	}

	// 触发键短按（未达 HoldMs 阈值）：阈值期间被缓冲的按下从没投递给任何窗口，
	// 用户的本意就是一次普通点击，这里用合成事件把它补回去，避免修复穿透之后丢失点击。
	private void FlushBufferedClicks()
	{
		for (int i = 0; i <= 2; i++)
		{
			if (!_bufferedDown[i])
			{
				continue;
			}
			_bufferedDown[i] = false;
			_heldDuringHold[i] = false;
			_passedDown[i] = false;
			// 缓冲账还在 ⟺ 我们收到过这条按下、却从没收到配对的抬起 ⟺ 用户此刻仍按着。
			// 所以这里只补"按下"（不补抬起），用户真实的抬起稍后会原样送达 —— 他按住的这段时间
			// 就被完整保留给下层控件（拖滑块、选文本都不断）。以前用 GetAsyncKeyState 判断，
			// 它会漏报，会把"一直按住"误判成"早已松开"而补成一次完整点击，按住过程就断了。
			bool flag = true;
			ReplayBufferedClick(i, flag);
			ReplayedClickForTest++;
			Log.Info("[诊断] 触发键短按（未达阈值）→ 把缓冲的鼠标按下还给用户 键=" + i + " 坐标=" + _bufferedPt[i].X + "," + _bufferedPt[i].Y + " 用户仍按住=" + flag);
		}
	}

	private void ReplayBufferedClick(int idx, bool keepDown)
	{
		try
		{
			uint num = idx switch
			{
				1 => 8u, 
				0 => 2u, 
				_ => 32u, 
			};
			uint num2 = idx switch
			{
				1 => 16u, 
				0 => 4u, 
				_ => 64u, 
			};
			Native.INPUT[] array = new Native.INPUT[keepDown ? 1 : 2];
			array[0].type = 0u;
			array[0].u.mi.dwFlags = num;
			array[0].u.mi.dwExtraInfo = HookEngine.MagicExtraInfo;
			if (!keepDown)
			{
				array[1].type = 0u;
				array[1].u.mi.dwFlags = num2;
				array[1].u.mi.dwExtraInfo = HookEngine.MagicExtraInfo;
			}
			if (!SuppressSyntheticMouseForTest)
			{
				Native.SendInput((uint)array.Length, array, Marshal.SizeOf(typeof(Native.INPUT)));
			}
			if (keepDown)
			{
				// 只补了 down：必须记账，否则配对的 up 一旦丢失，系统会永久以为这个键按着
				MarkInjectedDown(idx);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static bool IsButtonPhysicallyDown(int idx)
	{
		bool[] buttonsDownOverrideForTest = ButtonsDownOverrideForTest;
		if (buttonsDownOverrideForTest != null)
		{
			if (idx >= 0 && idx < buttonsDownOverrideForTest.Length)
			{
				return buttonsDownOverrideForTest[idx];
			}
			return false;
		}
		if (AssumeButtonsDownForTest)
		{
			return true;
		}
		int vKey = idx switch
		{
			1 => 2, 
			0 => 1, 
			_ => 4, 
		};
		try
		{
			return (Native.GetAsyncKeyState(vKey) & 0x8000) != 0;
		}
		catch
		{
			return false;
		}
	}

	internal bool HeldDuringHoldForTest(int idx)
	{
		if (idx >= 0 && idx < _heldDuringHold.Length)
		{
			return _heldDuringHold[idx];
		}
		return false;
	}

	internal bool PassedDownForTest(int idx)
	{
		if (idx >= 0 && idx < _passedDown.Length)
		{
			return _passedDown[idx];
		}
		return false;
	}

	internal bool BufferedDownForTest(int idx)
	{
		if (idx >= 0 && idx < _bufferedDown.Length)
		{
			return _bufferedDown[idx];
		}
		return false;
	}

	private static int ButtonIndexOf(int msg)
	{
		switch (msg)
		{
		case 513:
		case 514:
			return 0;
		case 516:
		case 517:
			return 1;
		case 519:
		case 520:
			return 2;
		default:
			return -1;
		}
	}

	private static bool IsDownMessage(int msg)
	{
		if (msg != 513 && msg != 516)
		{
			return msg == 519;
		}
		return true;
	}

	private static string ActionFor(int idx, AppConfig c)
	{
		return idx switch
		{
			0 => c.ActionLeft, 
			1 => c.ActionRight, 
			_ => c.ActionMiddle, 
		};
	}

	private bool HandleButtonDown(int idx, Native.POINT pt)
	{
		return HandleButtonDown(idx, pt, quiet: false);
	}

	private bool HandleButtonDown(int idx, Native.POINT pt, bool quiet)
	{
		AppConfig cfg = _store.Current;
		bool flag = _heldDuringHold[idx] || _passedDown[idx];
		string text = ActionFor(idx, cfg);
		if (text == "Nothing")
		{
			if (!quiet)
			{
				Log.Info("[诊断] 鼠标键按下被忽略：该键没有绑定任何动作 键=" + idx);
			}
			return false;
		}
		bool allowFullscreen = cfg.AllowFullscreenWindows || cfg.ExitFullscreenOnTrigger;
		nint num = WindowActions.FindTarget(pt, allowFullscreen);
		if (num == IntPtr.Zero)
		{
			// 这条以前只在 quiet=false 时打，结果"激活瞬间补起"这条路径静默失败时日志里什么都没有 ——
			// 用户看到的就是"外框出现了、窗口拖不动、日志却一片空白"。改成无条件记录。
			Log.Info("[诊断] FindTarget 没找到目标，按键被吞掉（触发键按住期间不再放行给下层窗口）键=" + idx + " 坐标=" + pt.X + "," + pt.Y + " 原因=" + WindowActions.DescribeReject(pt, allowFullscreen) + (quiet ? " 来源=激活后自动补起" : " 来源=触发键已激活时的实时按下"));
			_passedDown[idx] = false;
			_bufferedDown[idx] = false;
			_swallowUp[idx] = true;
			return true;
		}
		// 指针落在自有窗口（设置页等）内部的可交互控件上时，本轮不接管：
		// 把按下原样放行给控件，抬起也会走 HandleButtonUp 的默认分支（_swallowUp 为 false）。
		// 这样按住触发键去拖滑块、选文本、点按钮都还能用；想拖整个窗口把指针移到标题栏/边框/空白即可。
		if (Actions.IsDragAction(text) && WindowActions.IsPointOverOwnInteractiveControl(pt))
		{
			_passedDown[idx] = true;
			_bufferedDown[idx] = false;
			_heldDuringHold[idx] = false;
			_swallowUp[idx] = false;
			if (!quiet)
			{
				Log.Info("[诊断] 指针停在自有窗口的可交互控件上，按键交还控件 键=" + idx + " 控件=" + WindowActions.DescribeInteractiveAt(pt));
			}
			return false;
		}
		CancelTargetMouseMode(num);
		_passedDown[idx] = false;
		_bufferedDown[idx] = false;
		long tickCount = Environment.TickCount64;
		int num2 = (int)Native.GetDoubleClickTime();
		if (num2 <= 0)
		{
			num2 = 500;
		}
		bool flag2 = _lastDown[idx] != 0L && tickCount - _lastDown[idx] <= num2 && !_lastMoved[idx];
		_lastDown[idx] = tickCount;
		_lastMoved[idx] = false;
		if (((idx == 0 && cfg.DoubleClickLeftMaximize && text == "Move") & flag2) && !flag)
		{
			_lastDown[idx] = 0L;
			_swallowUp[idx] = true;
			nint target = num;
			Native.POINT at = pt;
			_drag.PostTargetWork(() =>
			{
				WindowActions.MaximizeRestore(target);
				RefreshHoverAfterAction(at);
			});
			_heldDuringHold[idx] = true;
			return true;
		}
		if (((idx == 1 && cfg.DoubleClickRightZone && text == "Resize") & flag2) && !flag)
		{
			_lastDown[idx] = 0L;
			_swallowUp[idx] = true;
			nint target2 = num;
			Native.POINT at2 = pt;
			_drag.PostTargetWork(() =>
			{
				ApplyZoneDoubleClick(target2, at2, cfg);
				RefreshHoverAfterAction(at2);
			});
			_heldDuringHold[idx] = true;
			return true;
		}
		if (Actions.IsDragAction(text))
		{
			if (!_drag.Begin(num, text == "Move", idx, pt))
			{
				// Begin 自己会打失败原因；这里再补一条，好让日志能把这次按下和
				// "触发键激活 / 外框出现" 串起来 —— 否则就是"按了左键什么都没发生"，无从定位。
				Log.Info("[诊断] 拖动无法建立（Begin 返回 false，具体原因见上一条日志）动作=" + text + " 键=" + idx + " 目标=" + ((IntPtr)num).ToInt64().ToString("X") + " 坐标=" + pt.X + "," + pt.Y);
				_swallowUp[idx] = true;
				return true;
			}
			return true;
		}
		_swallowUp[idx] = true;
		_heldDuringHold[idx] = false;
		nint actTarget = num;
		string actAction = text;
		Native.POINT actAt = pt;
		_drag.PostTargetWork(() =>
		{
			WindowActions.Apply(actTarget, actAction);
			RefreshHoverAfterAction(actAt);
		});
		return true;
	}

	private void RefreshHoverAfterAction(Native.POINT pt)
	{
		if (_trigger.Active)
		{
			_drag.InvalidateHover();
			_drag.ShowHoverFrame(pt);
		}
	}

	private static void ApplyZoneDoubleClick(nint hwnd, Native.POINT pt, AppConfig cfg)
	{
		if (!Native.GetWindowRect(hwnd, out var lpRect))
		{
			return;
		}
		ResizeZone resizeZone = Zones.Resize(lpRect, pt.X, pt.Y, cfg.CornerPercent);
		if (resizeZone == ResizeZone.Center)
		{
			resizeZone = Zones.NearestCorner(lpRect, pt.X, pt.Y);
		}
		SnapZone snapZone = Zones.ScreenZoneOfResizeZone(resizeZone);
		if (snapZone != SnapZone.None)
		{
			Native.MONITORINFO monitorInfoAt = Native.GetMonitorInfoAt(pt);
			if (monitorInfoAt.rcWork.Width > 0)
			{
				WindowActions.ApplySnap(hwnd, snapZone, monitorInfoAt.rcWork);
			}
		}
	}

	private bool HandleButtonUp(int idx, Native.POINT pt)
	{
		if (_drag.IsDragging && _drag.Button == idx)
		{
			_drag.End();
			_lastMoved[idx] = _drag.LastMoved;
			if (_trigger.Active)
			{
				_drag.ShowHoverFrame(pt);
			}
			return true;
		}
		if (_swallowUp[idx])
		{
			_swallowUp[idx] = false;
			return true;
		}
		return false;
	}
}
