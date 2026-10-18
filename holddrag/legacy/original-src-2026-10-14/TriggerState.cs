using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HoldDrag
{
    /// <summary>
    /// 触发键状态机——整个程序最需要小心的部分。
    ///
    /// 规则（用户指定：触发键默认 Enter，长按阈值默认 600ms）：
    ///   按下            -> 立刻吞掉，起阈值计时器。
    ///   阈值内松手      -> 合成一次完整的按下+抬起补发出去，Enter 的正常功能完全不受影响。
    ///   超过阈值仍在按  -> 进入“激活态”，此后按鼠标键即触发窗口动作。
    ///   松开触发键      -> 退出激活态，本次按键彻底不发出去（因为用户意图是当修饰键用）。
    ///
    /// 代价（无法规避，已写入 README）：要区分“长按”和“连发”，就必须吞掉
    /// 自动重复的按下事件，所以按住 Enter 连续换行会被压成一次。
    ///
    /// ⚠️ 这里【绝对不能】用 GetAsyncKeyState 去"校验触发键是否还按着"。
    ///    因为我们必须吞掉触发键的按下事件，而事件一旦被吞，系统层面就完全看不到这个键被按过。
    ///    实测（_diag/KeyStateProbe）：钩子里吞掉 F13 的按下后 GetAsyncKeyState 读到 0x0000，
    ///    同一时刻放行的 F14 读到 0x8001。也就是说任何基于 GetAsyncKeyState 的看门狗
    ///    在"按键正被按住"时都会稳定判定为"已松开"。
    ///    曾经就是这么写的，结果每 2 秒误判一次、把激活态打掉，表现为"时灵时不灵"。
    ///
    /// 那"抬起事件真的丢了"怎么办（切屏、锁屏、其它软件抢占）？
    ///    不需要额外机制：状态机本身就能自愈。丢抬起时 _held 仍为 true，
    ///    用户再按一次触发键也只会被吞掉（不会重复激活），
    ///    紧接着那一次正常的【抬起】就会走到 EndHold()，状态自动回到干净。
    /// </summary>
    internal sealed class TriggerState : IDisposable
    {
        private readonly Func<AppConfig> _cfg;

        // ══════ ★★★ 长按计时必须是线程无关的 Threading.Timer ★★★ ══════
        //
        //   原来这里用 WinForms Timer。它的 Tick 依赖"创建它的线程泵消息且
        //   DispatchMessage"。钩子搬上专用线程之后，键盘事件从钩子线程进来，
        //   BeginHold() 里的 Timer.Start() 会被跨线程调用 —— 原生定时器窗口
        //   建到了钩子线程上，而钩子线程的循环只 GetMessage、从不 Dispatch，
        //   WM_TIMER 永远不会被分发 → OnHoldElapsed 永远不来 →
        //   【触发键永远无法激活】，用户看到的就是"功能完全失效，
        //   松开时全被当短按补发"。
        //
        //   Threading.Timer 在线程池上回调，与任何消息循环无关，天生线程安全。
        //   一次性触发（dueTime=间隔, period=Infinite），语义与原来一致。
        private System.Threading.Timer _holdTimer;

        private bool _held;
        private long _downAt;
        private int _activeButton = -1; // 触发键自身是否需要配对吞掉抬起：0=键盘，1=鼠标侧键

        /// <summary>是否处于激活态（可以用鼠标键操作窗口）。</summary>
        public bool Active { get; private set; }

        /// <summary>触发键当前是否被按住。</summary>
        public bool Held { get { return _held; } }

        public event Action Activated;
        public event Action Deactivated;

        /// <summary>短按时是否真的补发了原按键。仅供自检观察。</summary>
        public bool LastPressReplayed { get; private set; }

        public TriggerState(Func<AppConfig> cfg)
        {
            _cfg = cfg;
        }

        public void Dispose()
        {
            if (_holdTimer != null) { _holdTimer.Dispose(); _holdTimer = null; }
        }

        // ── 键盘触发键 ──────────────────────────────────────────

        public bool HandleKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb)
        {
            var c = _cfg();
            if (!KeysUtil.MatchesKeyboard(c.TriggerKeyId, kb)) return false;

            if (down)
            {
                if (_held) return true;   // 自动重复：直接吞掉，绝不能重复激活
                _held = true;
                _activeButton = 0;
                _downAt = Environment.TickCount64;
                BeginHold(c);
                return true;
            }

            if (!_held || _activeButton != 0) return false;
            EndHold();
            if (!Active) ReplayKeyboard(ref kb);
            else SetActive(false);
            return true;
        }

        // ── 鼠标侧键触发键 ──────────────────────────────────────

        public bool HandleMouseTrigger(int msg, bool down, ref Native.MSLLHOOKSTRUCT ms)
        {
            var c = _cfg();
            if (!KeysUtil.MatchesMouse(c.TriggerKeyId, msg, ms)) return false;

            if (down)
            {
                if (_held) return true;
                _held = true;
                _activeButton = 1;
                _downAt = Environment.TickCount64;
                BeginHold(c);
                return true;
            }

            if (!_held || _activeButton != 1) return false;
            EndHold();
            if (!Active) ReplayMouseButton(ref ms);
            else SetActive(false);
            return true;
        }

        // ── 内部 ────────────────────────────────────────────────

        private void BeginHold(AppConfig c)
        {
            LastPressReplayed = false;
            if (c.RequireHold)
            {
                StopHoldTimer();
                int ms = Math.Max(30, Math.Min(3000, c.HoldMs));
                _holdTimer = new System.Threading.Timer(
                    _ => OnHoldElapsed(null, EventArgs.Empty), null, ms, System.Threading.Timeout.Infinite);
            }
            else
            {
                // 关闭长按阈值：该键彻底变成修饰键，短按也不会再发出原按键
                SetActive(true);
            }
        }

        private void EndHold()
        {
            _held = false;
            StopHoldTimer();
            _activeButton = -1;
        }

        private void StopHoldTimer()
        {
            var t = _holdTimer;
            if (t != null)
            {
                _holdTimer = null;
                try { t.Dispose(); } catch { }
            }
        }

        private void OnHoldElapsed(object sender, EventArgs e)
        {
            // 一次性定时器已触发完毕；万一重入，直接置空。
            StopHoldTimer();
            if (!_held) return;
            SetActive(true);
        }

        /// <summary>自检用：跳过等待直接判定“阈值已到”。</summary>
        internal void ForceHoldElapsed()
        {
            OnHoldElapsed(this, EventArgs.Empty);
        }

        /// <summary>自检用：模拟按下 / 松开，不依赖真实钩子和计时器。</summary>
        internal void SimulateKeyDown()
        {
            _held = true;
            _activeButton = 0;
            _downAt = Environment.TickCount64;
            BeginHold(_cfg());
        }

        internal bool SimulateKeyUp(Func<bool> replayCallback)
        {
            StopHoldTimer();
            bool wasActive = Active;
            EndHold();
            if (wasActive)
            {
                SetActive(false);
                return false;
            }
            LastPressReplayed = true;
            if (replayCallback != null) replayCallback();
            return true;
        }

        private void SetActive(bool value)
        {
            if (Active == value) return;
            Active = value;
            var h = value ? Activated : Deactivated;
            if (h != null) h();
            Log.Info(value ? "触发键激活" : "触发键释放");
        }

        // 这里曾经有一个每 2000ms 跑一次的看门狗，用 GetAsyncKeyState 判断触发键是否还按着。
        // 它必然误判，已删除 —— 理由见本文件顶部的类注释与 _diag/KeyStateProbe 的实测结果。
        // 简言之：我们把触发键的按下吞掉了，系统层面这个键就不算按下，看门狗每次都会
        // 得出"抬起事件丢失"的错误结论，把激活态打掉，用户感觉就是"时灵时不灵"。

        /// <summary>把被吞掉的短按原样补发，保证 Enter / Alt 之类的正常功能不受影响。</summary>
        private void ReplayKeyboard(ref Native.KBDLLHOOKSTRUCT kb)
        {
            try
            {
                var inputs = new Native.INPUT[2];

                inputs[0].type = Native.INPUT_KEYBOARD;
                inputs[0].u.ki.wVk = (ushort)kb.vkCode;
                inputs[0].u.ki.wScan = (ushort)kb.scanCode;
                inputs[0].u.ki.dwFlags = (kb.flags & Native.LLKHF_EXTENDED) != 0 ? Native.KEYEVENTF_EXTENDEDKEY : 0u;
                inputs[0].u.ki.dwExtraInfo = HookEngine.MagicExtraInfo;

                inputs[1] = inputs[0];
                inputs[1].u.ki.dwFlags |= Native.KEYEVENTF_KEYUP;

                Native.SendInput(2, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
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
                var inputs = new Native.INPUT[2];

                inputs[0].type = Native.INPUT_MOUSE;
                inputs[0].u.mi.mouseData = ms.mouseData;
                inputs[0].u.mi.dwFlags = 0x0080u; // MOUSEEVENTF_XDOWN
                inputs[0].u.mi.dwExtraInfo = HookEngine.MagicExtraInfo;

                inputs[1] = inputs[0];
                inputs[1].u.mi.dwFlags = 0x0100u; // MOUSEEVENTF_XUP

                Native.SendInput(2, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
                LastPressReplayed = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }
}
