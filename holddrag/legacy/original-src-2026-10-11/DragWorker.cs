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
        private readonly Func<int> _frameMs;

        private readonly ConcurrentQueue<Action> _work = new ConcurrentQueue<Action>();

        private Thread _thread;
        private uint _threadId;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private volatile bool _disposed;

        public DragWorker(Action<Native.POINT> onMove, Func<int> frameMs)
        {
            _onMove = onMove;
            _frameMs = frameMs;
        }

        /// <summary>线程是否已经起来并建好了消息队列。</summary>
        public bool IsRunning { get { return _thread != null && _ready.IsSet && !_disposed; } }

        public void Start()
        {
            if (_thread != null) return;

            _thread = new Thread(ThreadMain);
            _thread.IsBackground = true;      // 不阻止进程退出
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
                    int got = Native.GetMessageW(out msg, IntPtr.Zero, 0, 0);
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

                    long frameStart = Environment.TickCount64;

                    // 合并：把队列里已经堆积的移动一次性抽干，只留最后一个。
                    Native.MSG next;
                    int drained = 0;
                    while (Native.PeekMessageW(out next, IntPtr.Zero, WM_DOMOUSEMOVE, WM_DOMOUSEMOVE, Native.PM_REMOVE))
                    {
                        msg = next;
                        drained++;
                    }
                    if (drained > 0) Log.Trace("合并掉了 " + drained + " 个过期的鼠标移动位置");

                    var pt = new Native.POINT((int)msg.wParam.ToInt64(), (int)msg.lParam.ToInt64());
                    try
                    {
                        _onMove(pt);
                    }
                    catch (Exception ex)
                    {
                        // 工作线程绝不能因为一次异常就死掉，否则表现是"拖动突然永久失效"。
                        Log.Error(ex);
                    }

                    ThrottleToFrame(frameStart);
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

        /// <summary>补睡到本帧结束，把写入频率压到显示器刷新率。</summary>
        private void ThrottleToFrame(long frameStart)
        {
            int interval;
            try { interval = _frameMs != null ? _frameMs() : 0; }
            catch { interval = 0; }

            if (interval <= 0) return;
            if (interval > 200) interval = 200;

            int spent = (int)(Environment.TickCount64 - frameStart);
            int wait = interval - spent;
            if (wait <= 0) return;

            // 先粗睡，再忙等收尾。系统计时精度已经用 timeBeginPeriod(1) 提到 1ms，
            // 否则 Sleep(3) 有可能实际睡满 15.6ms，节流会变成 60Hz 上限。
            if (wait > 2) Thread.Sleep(wait - 2);
            long deadline = frameStart + interval;
            int guard = 0;
            while (Environment.TickCount64 < deadline && guard++ < 4000)
            {
                Thread.SpinWait(200);
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
