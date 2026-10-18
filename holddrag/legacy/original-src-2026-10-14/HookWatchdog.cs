using System;
using System.Windows.Forms;

namespace HoldDrag
{
    /// <summary>
    /// 键鼠钩子存活看门狗。
    ///
    /// 解决的问题（这是"用着用着突然失灵"的根因）：
    ///   低级钩子的回调一旦超过系统的 LowLevelHooksTimeout（默认 300ms 左右），
    ///   从 Win7 起 Windows 会把钩子【静默摘除】—— 不抛异常、不发通知，
    ///   也没有任何 API 能查询"我的钩子还在不在"。程序会看起来完全正常，
    ///   只是再也收不到键鼠事件了。
    ///   典型触发场景：长时间睡眠/休眠唤醒、锁屏切换、显卡驱动重置、
    ///   拖动一个卡死的程序导致回调超时。
    ///
    /// 判定方法（照抄 AltSnap hooks.c 的 REHOOK_TIMER）：
    ///   光标位置是会一直变的。如果间隔一秒再取一次 GetCursorPos，
    ///   发现【光标动了】但【钩子最后一次看到的位置没变】，那就只有一个解释：
    ///   钩子已经收不到事件了。此时卸载重装即可。
    ///
    /// 这个方法不会误判：钩子活着时，光标每动一下 _seenX/_seenY 都会跟着更新，
    /// 两次轮询之间它必然变过。用户不动鼠标时 cursorMoved 为 false，也不会触发。
    /// </summary>
    internal sealed class HookWatchdog : IDisposable
    {
        private readonly HookEngine _hooks;
        private readonly Timer _timer = new Timer();

        private Native.POINT _prevCursor;
        private int _prevSeenX;
        private int _prevSeenY;
        private bool _hasPrev;

        /// <summary>轮询次数。仅供自检观察。</summary>
        public int Checks { get; private set; }

        /// <summary>判定钩子已死并重装的次数。仅供自检观察。</summary>
        public int Rescues { get; private set; }

        public HookWatchdog(HookEngine hooks)
        {
            _hooks = hooks;
            _timer.Interval = 1000;
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            _hasPrev = false;
            _prevSeenX = 0;
            _prevSeenY = 0;
            Checks = 0;
            Rescues = 0;
            _timer.Start();
        }

        public void Stop()
        {
            try { _timer.Stop(); } catch { }
        }

        public void Dispose()
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
        }

        private void OnTick(object sender, EventArgs e)
        {
            try { CheckOnce(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>
        /// 跑一次判定。返回 true 表示这次判定"钩子已死"并执行了重装。
        ///
        /// ★★★ 这里曾经是"用一次轮询就下结论"，导致自激循环 ★★
        ///
        /// 老逻辑：光标动了 && 钩子记录的没变 → 立刻重装。
        /// 问题在于"钩子记录的没变"有太多种正常原因：
        ///   · 光标在同一个像素内抖动（位置不变，但事件其实收到了）
        ///   · 鼠标被某个窗口抓走/穿透，GetCursorPos 变了但钩子收不到
        ///   · 刚重装完，_seenValid 刚被清掉，基准还没建立
        /// 一旦误判就重装，重装又清空基准，下一轮接着误判 ——
        /// 实测日志里出现过 66 次重装、几秒内连发十几次，
        /// 表现为钩子被反复拆装、鼠标事件时有时无、功能像随机失灵。
        ///
        /// 正解（三道保险）：
        ///   1) 优先用【事件计数】交叉验证 —— 只要钩子回调还被调用，计数就在涨，
        ///      这是"钩子活着"最直接的证据；
        ///   2) 位置比对必须【连续 N 轮】都成立才动手，中途任何一次计数增长即清零；
        ///   3) 两次重装之间强制冷却，避免再出现高频拆装。
        /// </summary>
        public bool CheckOnce()
        {
            Checks++;

            // ★ 第四十六轮：钩子回调卡死告警 ★
            //   回调一旦卡住（典型：钩子线程上出现了没有上限的同步跨进程调用），
            //   全系统的键鼠事件都会排在它后面 —— 用户侧就是"整机输入无响应"。
            //   我们无法从外部中止它，但必须让日志留下证据。
            CheckCallbackStall();

            Native.POINT cur;
            if (!Native.GetCursorPos(out cur)) return false;

            // ★ 保险 1：事件计数。钩子回调只要还在跑，这个数就一定在涨。
            long countNow = _hooks.SeenCount;
            if (countNow != _prevSeenCount)
            {
                _prevSeenCount = countNow;
                _prevCursor = cur;
                _strikes = 0;            // 有任何事件到达，一切正常，之前的怀疑全部作废
                return false;
            }

            int sx, sy;
            bool seenValid = _hooks.TryGetLastSeen(out sx, out sy);

            if (!_hasPrev || !seenValid)
            {
                _prevCursor = cur;
                _prevSeenX = sx;
                _prevSeenY = sy;
                _prevSeenCount = countNow;
                _hasPrev = true;
                return false;
            }

            bool cursorMoved = cur.X != _prevCursor.X || cur.Y != _prevCursor.Y;
            bool hookAdvanced = sx != _prevSeenX || sy != _prevSeenY;

            _prevCursor = cur;
            _prevSeenX = sx;
            _prevSeenY = sy;

            if (cursorMoved && !hookAdvanced)
            {
                _strikes++;
            }
            else
            {
                _strikes = 0;       // 有一轮对上了就说明钩子在动，怀疑清零
            }

            // ★ 保险 2：必须连续 RequiredStrikes 轮都判定"钩子已死"才动手。
            // 一次误判不够，避免抖动 / 抓取 / 基准未建立时的误报。
            if (_strikes < RequiredStrikes) return false;

            // ★ 保险 3：冷却。重装一次之后至少等 CooldownMs 才考虑下一次，
            // 杜绝"重装→失去基准→再误判→再重装"的自激。
            long now = Environment.TickCount64;
            if (now - _lastRescueAt < CooldownMs) return false;

            _strikes = 0;
            _lastRescueAt = now;

            Rescues++;
            _hooks.Reinstall();
            // 重装之后 _seenValid 会被清掉，下一次 CheckOnce 会重新取基准
            _hasPrev = false;
            return true;
        }

        /// <summary>连续多少轮判定成立才真的动手重装。</summary>
        private const int RequiredStrikes = 3;

        /// <summary>两次重装之间的最小间隔（毫秒），用来阻断自激循环。</summary>
        private const int CooldownMs = 5000;

        // ── 回调卡死告警（第四十六轮）────────────────────────────
        // 回调卡住这么久就报警。阈值取 2000ms：健康的回调最坏也就几十毫秒
        // （慢目标上的一帧同步写），正常操作绝不可能到 2 秒。
        private const double CallbackStallAlertMs = 2000;

        private bool _stallAlerted;

        /// <summary>
        /// 看一眼钩子回调是不是卡住了。每次"进入卡死状态"只报一条 ERROR，
        /// 恢复后复位，避免刷屏。卡死的成因与修法见 HookEngine.CallbackStalledMs 的注释。
        /// </summary>
        private void CheckCallbackStall()
        {
            try
            {
                double stalled = _hooks.CallbackStalledMs;
                if (stalled >= CallbackStallAlertMs)
                {
                    if (!_stallAlerted)
                    {
                        _stallAlerted = true;
                        Log.Error("钩子回调已卡住 " + (int)stalled + " ms"
                            + " —— 低级钩子回调正在阻塞，全系统键鼠事件都会排在它后面"
                            + "（整机输入无响应）。最常见的原因：钩子线程上出现了"
                            + "没有上限的同步跨进程窗口调用，请检查 DragEngine/WindowActions。");
                    }
                }
                else if (_stallAlerted && stalled == 0)
                {
                    _stallAlerted = false;   // 恢复了：下次卡住再报
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private int _strikes;
        private long _lastRescueAt;
        private long _prevSeenCount;
    }
}
