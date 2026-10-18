using System;
using System.Collections.Concurrent;
using System.Threading;

namespace HoldDrag
{
    /// <summary>
    /// 拖动工作线程 —— 对应 AltSnap hooks.c 里的 <c>WorkerThread</c>（WM_DOMOUSEMOVE / WM_DOWORK 分支）。
    ///
    /// 为什么必须有它（这条不是可选的优化，是硬性要求）：
    ///   1) 低级钩子回调是在"安装钩子的线程"上被调用的，如果回调耗时超过
    ///      LowLevelHooksTimeout，Windows 会把钩子【静默摘除】—— 而且没有任何 API
    ///      能告诉我们它被摘了。微软文档 LowLevelMouseProc 明确写着：
    ///      "it should run the hooks on a dedicated thread that passes the work off
    ///       to a worker thread and then immediately returns."
    ///      钩子里做 EnumWindows + SetWindowPos 是典型会超时的写法。
    ///
    ///   2) 鼠标移动事件会堆积。工作线程把队列里连续堆积的移动【全部丢掉，只执行最新的一个】，
    ///      所以窗口永远追最新位置，不会积压出"追不上、一顿一顿"的效果。
    ///      这是 AltSnap WorkerThread 里有意的设计，不是顺手写的。
    ///
    ///   3) 【帧率节流】。把每一帧 SetWindowPos 的节奏限制到显示器刷新率
    ///      （对应 AltSnap 的 conf.RefreshRate）。鼠标每秒能产生几百个移动事件，
    ///      不节流就等于每秒给目标窗口的消息队列塞几百次 WM_WINDOWPOSCHANGING +
    ///      WM_MOVE + WM_SIZE + WM_PAINT —— 目标程序处理不过来就开始掉帧，
    ///      表现出来就是"拖着拖着越来越卡"。节流之后目标窗口每次都能画完。
    ///      注意：只在【处理完之后】补睡到本帧结束，绝不丢队列里最后一个位置，
    ///      而且拖尾的最后一帧由 DragEngine.End 同步补上，落点永远精确。
    ///
    /// 位置本身用的是"光标绝对坐标 − 拖动开始时的固定偏移"，不做增量累加，
    /// 因此即使丢事件也不会漂移 —— 见 DragEngine.Step。
    /// </summary>
    internal sealed class DragWorker : IDisposable
    {
        /// <summary>与 AltSnap 同号的私有消息：WM_APP+7 表示"鼠标移动了，去动窗口"。</summary>
        public const uint WM_DOMOUSEMOVE = 0x8000 + 7;

        /// <summary>与 AltSnap 同号：WM_APP+6 表示"执行一件一次性工作"。</summary>
        public const uint WM_DOWORK = 0x8000 + 6;

        private readonly Action<Native.POINT> _onMove;

        private readonly ConcurrentQueue<Action> _work = new ConcurrentQueue<Action>();

        private Thread _thread;
        private uint _threadId;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

        /// <summary>
        /// 帧间隔（毫秒）。0 = 不节流。由 DragEngine 在每次会话开始时按配置设置
        /// （跟随屏幕 / 固定刷新率，见 AppConfig.FrameIntervalMs）。
        /// 只在【处理完一帧之后】补睡，队列里最后一个最新位置绝不会被丢掉；
        /// 拖尾最后一帧由 DragEngine.End 同步补上，落点永远精确。
        /// </summary>
        private volatile int _frameIntervalMs;
        public int FrameIntervalMs
        {
            get { return _frameIntervalMs; }
            set { _frameIntervalMs = value < 0 ? 0 : value; }
        }

        // ── 自检/基准用帧耗时统计（全部 long，读写原子）──
        /// <summary>最慢一帧 _onMove 的耗时（ms）。</summary>
        public static long MaxMoveMs;
        /// <summary>最慢一次 GetMessage 等待（ms；拖动中应≈事件间隔）。</summary>
        public static long MaxIdleMs;
        /// <summary>本帧合并掉的事件数峰值。</summary>
        public static int MaxDrained;
        public static void ResetStats() { MaxMoveMs = 0; MaxIdleMs = 0; MaxDrained = 0; }
        private volatile bool _disposed;

        public DragWorker(Action<Native.POINT> onMove)
        {
            _onMove = onMove;
        }

        /// <summary>线程是否已经起来并建好了消息队列。</summary>
        public bool IsRunning { get { return _thread != null && _ready.IsSet && !_disposed; } }

        public void Start()
        {
            if (_thread != null) return;

            _thread = new Thread(ThreadMain);
            _thread.IsBackground = true;      // 不阻止进程退出
            // ★ 用户要求"不计性能开销换丝滑"：工作线程提到 AboveNormal。
            //   它每一帧只做一次同步 SetWindowPos（自身会按目标速度自然限流），
            //   提优先级保证鼠标事件到达后立刻被调度，不会在系统调度队列里排队。
            _thread.Priority = System.Threading.ThreadPriority.AboveNormal;
            _thread.Name = "HoldDrag.DragWorker";
            _thread.Start();

            // 等它把消息队列建出来，否则最早的几次 PostThreadMessage 会直接失败。
            if (!_ready.Wait(3000))
            {
                Log.Error("拖动工作线程启动超时");
            }
        }

        /// <summary>
        /// 钩子回调里唯一被允许做的事：把坐标投递出去，然后立刻返回。
        /// 绝不在这里算矩形，更不在这里 SetWindowPos。
        /// </summary>
        public void PostMove(Native.POINT pt)
        {
            if (_disposed || _threadId == 0) return;

            if (!Native.PostThreadMessageW(_threadId, WM_DOMOUSEMOVE, new IntPtr(pt.X), new IntPtr(pt.Y)))
            {
                // 队列满了（比如目标窗口卡死导致处理跟不上）。
                // 丢掉这一次没有关系：下一次移动会用绝对坐标重新算，不会累积误差。
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                if (err != 0) Log.Trace("投递鼠标移动失败，Win32 错误码 " + err);
            }
        }

        /// <summary>
        /// 投递一件"一次性工作"，在工作线程上执行。
        ///
        /// 存在的意义：钩子回调里【不能】出现任何可能长时间阻塞的跨进程调用
        /// （SetForegroundWindow 要先 AttachThreadInput，目标程序卡住就会把我们拖住），
        /// 也不能做 EnumWindows 这种慢活。这些统统扔到这里来。
        /// 对应 AltSnap 的 PostThreadMessage(g_WorkerThreadID, WM_DOWORK, ...)。
        /// </summary>
        public void Post(Action work)
        {
            if (work == null) return;
            if (_disposed || _threadId == 0) return;

            _work.Enqueue(work);
            if (!Native.PostThreadMessageW(_threadId, WM_DOWORK, IntPtr.Zero, IntPtr.Zero))
            {
                // 消息投递失败也要把工作项取回来，否则队列会无限膨胀。
                Action dropped;
                _work.TryDequeue(out dropped);
            }
        }

        private void ThreadMain()
        {
            _threadId = Native.GetCurrentThreadId();

            // 必须先 Peek 一次，强制系统为本线程创建消息队列；
            // 否则 PostThreadMessage 会因为"队列不存在"而失败。
            Native.MSG tmp;
            Native.PeekMessageW(out tmp, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE);

            _ready.Set();

            try
            {
                while (!_disposed)
                {
                    Native.MSG msg;
                    long idleStart = Environment.TickCount64;
                    int got = Native.GetMessageW(out msg, IntPtr.Zero, 0, 0);
                    long idleMs = Environment.TickCount64 - idleStart;
                    if (idleMs > MaxIdleMs) MaxIdleMs = idleMs;
                    if (got == 0) break;      // WM_QUIT
                    if (got < 0) break;       // 出错

                    // 只处理投递给"线程"的消息；有窗口的消息不归我们管。
                    // AltSnap 的 WorkerThread 也是这么过滤的（msg.hwnd != NULL 就跳过）。
                    if (msg.hwnd != IntPtr.Zero) continue;
                    if (msg.message == Native.WM_QUIT) break;

                    if (msg.message == WM_DOWORK)
                    {
                        Action work;
                        while (_work.TryDequeue(out work))
                        {
                            try { work(); }
                            catch (Exception ex) { Log.Error(ex); }
                        }
                        continue;
                    }

                    if (msg.message != WM_DOMOUSEMOVE) continue;


                    // 合并：把队列里已经堆积的移动一次性抽干，只留最后一个。
                    Native.MSG next;
                    int drained = 0;
                    while (Native.PeekMessageW(out next, IntPtr.Zero, WM_DOMOUSEMOVE, WM_DOMOUSEMOVE, Native.PM_REMOVE))
                    {
                        msg = next;
                        drained++;
                    }
                    if (drained > MaxDrained) MaxDrained = drained;
                    // 注意：字符串拼接在钩子/工作线程热路径上必须先判 Verbose，
                    // 否则每帧都在白白分配（GC 压力 → 周期性停顿）。
                    if (drained > 0 && Log.Verbose)
                        Log.Trace("合并掉了 " + drained + " 个过期的鼠标移动位置");

                    var pt = new Native.POINT((int)msg.wParam.ToInt64(), (int)msg.lParam.ToInt64());
                    long moveStart = Environment.TickCount64;
                    try
                    {
                        _onMove(pt);
                    }
                    catch (Exception ex)
                    {
                        // 工作线程绝不能因为一次异常就死掉，否则表现是"拖动突然永久失效"。
                        Log.Error(ex);
                    }

                    long moveMs = Environment.TickCount64 - moveStart;
                    if (moveMs > MaxMoveMs) MaxMoveMs = moveMs;

                    // ══════ 刷新率节流（第三十六轮接通）══════
                    //
                    // 处理完本帧后补睡到"本帧结束"。语义：
                    //   · 绝不丢位置 —— 睡眠期间到达的新移动只是让下一次 GetMessage
                    //     立刻返回，写出去的永远是光标最新位置（绝对推导，不漂移）；
                    //   · 目标窗口每次都能在自己的一个显示周期里画完，不会出现
                    //     "队列堆积 → 追过期位置 → 一格一格顿过去"；
                    //   · 慢目标上 moveMs 本身就超过间隔，remaining ≤ 0 不睡，
                    //     节流自动让位给目标真实消化能力（天然背压）。
                    //
                    // 精度：程序启动时已 TimeBeginPeriod(1)，Thread.Sleep 粒度 1ms，
                    // 不会出现第九轮那种"想睡 10ms 实际睡 15.6ms"的整帧抖动。
                    int interval = _frameIntervalMs;
                    if (interval > 0)
                    {
                        long remaining = interval - (Environment.TickCount64 - moveStart);
                        if (remaining > 1)
                        {
                            Thread.Sleep((int)remaining);
                        }
                        else if (remaining == 1)
                        {
                            Thread.Sleep(1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            finally
            {
                _ready.Set();
            }
        }


        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_threadId != 0)
                {
                    Native.PostThreadMessageW(_threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }

            try
            {
                if (_thread != null && _thread.IsAlive) _thread.Join(1000);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }

            _thread = null;
            _threadId = 0;

            // 线程可能已经退出、工作项再也不会被执行：清空，别让捕获的委托挂着对象不放。
            Action leftover;
            while (_work.TryDequeue(out leftover)) { }

            // 刻意不 Dispose _ready：万一线程没能在超时内退出，它还会在 finally 里 Set，
            // 提前 Dispose 会在线程内部抛 ObjectDisposedException。让 GC 收即可。
        }
    }
}
