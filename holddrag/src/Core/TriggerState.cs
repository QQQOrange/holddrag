// HoldDrag · 触发键状态机：长按超阈值 → 激活；阈值内松开 → 补发原按键
//
// 线程模型（改这里之前务必读完）：
//   · HandleKeyboard / HandleMouseTrigger 在钩子线程（"HoldDrag.HookThread"）上被调用；
//   · 长按计时器回调在 ThreadPool 线程上；
//   · Reset 可能来自 UI 线程（暂停、改配置、开始录制按键）。
// 三者共享 _held / _active，因此状态迁移全部串行化在 _gate 里，
// 而 Activated / Deactivated 事件一律在锁外触发——订阅方会去动窗口、发消息，不能在锁里做。

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HoldDrag;

internal sealed class TriggerState : IDisposable
{
	private readonly Func<AppConfig> _cfg;

	/// <summary>状态迁移锁。只保护下面那几个字段，临界区里绝不能调用外部代码。</summary>
	private readonly object _gate = new object();

	private System.Threading.Timer _holdTimer;

	private volatile bool _held;

	private volatile bool _active;

	private long _downAt;

	private int _activeButton = -1;

	/// <summary>每次按下 / 重置都 +1；计时器回调必须仍带着当前代号才允许激活（防"松手后又被计时器激活"）。</summary>
	private int _gen;

	public bool Active => _active;

	public bool Held => _held;

	public bool LastPressReplayed { get; private set; }

	public event Action Activated;

	public event Action Deactivated;

	// 触发键"短按"（没到 HoldMs 阈值就松开）时触发：此时从没激活过，
	// 但阈值期间我们可能已经把鼠标按下缓冲下来了，必须还给用户，否则会丢点击。
	public event Action ShortPressWithoutActivation;

	public TriggerState(Func<AppConfig> cfg)
	{
		_cfg = cfg;
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_gen++;
			_held = false;
			_active = false;
			StopHoldTimerLocked();
		}
	}

	/// <summary>
	/// 把状态机拉回"什么都没按"的干净状态。
	///
	/// 为什么必须有它：触发键的"松开"是一条**会被弄丢**的输入事件——开始录制快捷键（按键被抑制）、
	/// 暂停、钩子被看门狗重装、UAC/Win+L 切到安全桌面、系统因回调超时静默摘掉钩子，
	/// 这几种情况下 up 事件根本到不了我们这里。旧实现只在 up 分支里清 _held，
	/// 于是 _held 会永久为真：之后任何一次按下都被当成"自动重复"吞掉，触发键彻底失灵；
	/// 若 _active 也被卡住，之后每一次鼠标按下都会被当成拖动接管，用户会觉得鼠标坏了。
	/// </summary>
	public void Reset(string reason = null)
	{
		bool wasActive;
		lock (_gate)
		{
			wasActive = _active;
			_gen++;
			_held = false;
			_active = false;
			_activeButton = -1;
			LastPressReplayed = false;
			StopHoldTimerLocked();
		}
		if (!string.IsNullOrEmpty(reason))
		{
			Log.Info("触发状态已复位：" + reason);
		}
		if (wasActive)
		{
			RaiseDeactivated();
		}
	}

	public bool HandleKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb)
	{
		AppConfig appConfig = _cfg();
		if (!KeysUtil.MatchesKeyboard(appConfig.TriggerKeyId, kb))
		{
			return false;
		}
		// 点按切换模式：按一下 = 抓住，再按一下 = 放下；按键本身被吞掉（它是命令，不是文字）。
		if (IsToggleMode(appConfig))
		{
			ToggleHeld(down);
			return true;
		}
		if (down)
		{
			if (!BeginHold(appConfig, 0))
			{
				return true; // 已是按下状态：吞掉自动重复
			}
			return true;
		}
		bool replayed;
		bool activated;
		if (!EndHold(0, out replayed, out activated))
		{
			return false;
		}
		if (replayed)
		{
			ReplayKeyboard(ref kb);
			LastPressReplayed = true;
			RaiseShortPress();
		}
		else if (activated)
		{
			RaiseDeactivated();
		}
		return true;
	}

	public bool HandleMouseTrigger(int msg, bool down, ref Native.MSLLHOOKSTRUCT ms)
	{
		AppConfig appConfig = _cfg();
		if (!KeysUtil.MatchesMouse(appConfig.TriggerKeyId, msg, ms))
		{
			return false;
		}
		if (IsToggleMode(appConfig))
		{
			ToggleHeld(down);
			return true;
		}
		if (down)
		{
			BeginHold(appConfig, 1);
			return true;
		}
		bool replayed;
		bool activated;
		if (!EndHold(1, out replayed, out activated))
		{
			return false;
		}
		if (replayed)
		{
			ReplayMouseButton(ref ms);
			LastPressReplayed = true;
			RaiseShortPress();
		}
		else if (activated)
		{
			RaiseDeactivated();
		}
		return true;
	}

	private static bool IsToggleMode(AppConfig c)
	{
		return c != null && string.Equals(c.TriggerMode, "Toggle", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 点按模式的一次按键：按下时翻转激活态，抬起时什么都不做（松手不再退出）。
	/// 不做"长按阈值"，也不需要计时器——用户要的就是"点一下抓住"。
	/// </summary>
	private void ToggleHeld(bool down)
	{
		bool flag;
		bool flag2;
		lock (_gate)
		{
			_gen++;
			StopHoldTimerLocked();
			_activeButton = -1;
			LastPressReplayed = false;
			if (!down)
			{
				_held = false;
				return;
			}
			_held = true;
			_active = !_active;
			flag = _active;
			flag2 = true;
		}
		if (flag2)
		{
			Log.Info(flag ? "触发键激活（点按模式）" : "触发键释放（点按模式）");
			if (flag)
			{
				RaiseActivated();
			}
			else
			{
				RaiseDeactivated();
			}
		}
	}

	/// <summary>按下：拿起触发器。返回 false 表示"本来就按着"（自动重复）。</summary>
	private bool BeginHold(AppConfig c, int button)
	{
		bool activateNow = false;
		int gen;
		lock (_gate)
		{
			if (_held)
			{
				return false;
			}
			_held = true;
			_activeButton = button;
			_downAt = Environment.TickCount64;
			LastPressReplayed = false;
			gen = ++_gen;
			if (c.RequireHold)
			{
				StopHoldTimerLocked();
				int dueTime = Math.Max(30, Math.Min(3000, c.HoldMs));
				_holdTimer = new System.Threading.Timer(delegate
				{
					OnHoldElapsed(gen);
				}, null, dueTime, -1);
			}
			else
			{
				activateNow = ActivateLocked();
			}
		}
		if (activateNow)
		{
			RaiseActivated();
		}
		return true;
	}

	/// <summary>
	/// 松开：收起触发器。返回 false 表示"这次 up 与我们的按下不配对"（例如按下被暂停吞掉了）。
	/// replayed = 短按，需要调用方补发原按键；activated = 之前确实处于激活态，需要广播 Deactivated。
	/// </summary>
	private bool EndHold(int button, out bool replayed, out bool activated)
	{
		replayed = false;
		activated = false;
		lock (_gate)
		{
			if (!_held || _activeButton != button)
			{
				return false;
			}
			_held = false;
			StopHoldTimerLocked();
			_activeButton = -1;
			_gen++;
			if (_active)
			{
				_active = false;
				activated = true;
			}
			else
			{
				replayed = true;
			}
		}
		return true;
	}

	private bool ActivateLocked()
	{
		if (_active)
		{
			return false;
		}
		_active = true;
		Log.Info("触发键激活");
		return true;
	}

	private void StopHoldTimer()
	{
		lock (_gate)
		{
			StopHoldTimerLocked();
		}
	}

	private void StopHoldTimerLocked()
	{
		System.Threading.Timer holdTimer = _holdTimer;
		if (holdTimer != null)
		{
			_holdTimer = null;
			try
			{
				holdTimer.Dispose();
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// 计时器到期。必须校验"代号 + 仍按住"：用户可能在阈值的最后一毫秒松手，
	/// 旧实现只读一次 _held 就激活，会造成"键早就松了，程序还以为在激活态"。
	/// 这里还在 ThreadPool 线程上，任何异常都会终结进程，所以整段包 try/catch。
	/// </summary>
	private void OnHoldElapsed(int gen)
	{
		try
		{
			bool flag = false;
			lock (_gate)
			{
				if (gen != _gen || !_held)
				{
					return;
				}
				StopHoldTimerLocked();
				flag = ActivateLocked();
			}
			if (flag)
			{
				RaiseActivated();
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	internal void ForceHoldElapsed()
	{
		int gen;
		lock (_gate)
		{
			gen = _gen;
		}
		OnHoldElapsed(gen);
	}

	internal void SimulateKeyDown()
	{
		BeginHold(_cfg(), 0);
	}

	internal bool SimulateKeyUp(Func<bool> replayCallback)
	{
		bool replayed;
		bool activated;
		if (!EndHold(0, out replayed, out activated))
		{
			return false;
		}
		if (activated)
		{
			RaiseDeactivated();
			return false;
		}
		LastPressReplayed = true;
		replayCallback?.Invoke();
		RaiseShortPress();
		return true;
	}

	private void RaiseActivated()
	{
		try
		{
			Activated?.Invoke();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void RaiseDeactivated()
	{
		try
		{
			Deactivated?.Invoke();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void RaiseShortPress()
	{
		try
		{
			ShortPressWithoutActivation?.Invoke();
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ReplayKeyboard(ref Native.KBDLLHOOKSTRUCT kb)
	{
		try
		{
			Native.INPUT[] array = new Native.INPUT[2];
			array[0].type = 1u;
			array[0].u.ki.wVk = (ushort)kb.vkCode;
			array[0].u.ki.wScan = (ushort)kb.scanCode;
			array[0].u.ki.dwFlags = (((kb.flags & 1) != 0) ? 1u : 0u);
			array[0].u.ki.dwExtraInfo = HookEngine.MagicExtraInfo;
			array[1] = array[0];
			array[1].u.ki.dwFlags |= 2u;
			Native.SendInput(2u, array, Marshal.SizeOf(typeof(Native.INPUT)));
			LastPressReplayed = true;
			Log.Info("短按触发键，已补发原按键");
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private void ReplayMouseButton(ref Native.MSLLHOOKSTRUCT ms)
	{
		try
		{
			Native.INPUT[] array = new Native.INPUT[2];
			array[0].type = 0u;
			array[0].u.mi.mouseData = ms.mouseData;
			array[0].u.mi.dwFlags = 128u;
			array[0].u.mi.dwExtraInfo = HookEngine.MagicExtraInfo;
			array[1] = array[0];
			array[1].u.mi.dwFlags = 256u;
			Native.SendInput(2u, array, Marshal.SizeOf(typeof(Native.INPUT)));
			LastPressReplayed = true;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}
}
