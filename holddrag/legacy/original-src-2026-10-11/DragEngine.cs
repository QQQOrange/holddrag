using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace HoldDrag
{
    /// <summary>
    /// 一次拖动会话。
    ///
    /// 刻意不用 Windows 原生的移动循环（WM_NCLBUTTONDOWN + HTCAPTION）：
    /// 原生循环一旦启动就由系统接管，我们就无法拦截"双击左键最大化""双击右键九宫格"
    /// 这两个用户明确要求的功能。代价是边缘吸附 / 窗口对齐要自己实现（AltSnap 也是这么做的）。
    ///
    /// 线程模型（照 AltSnap hooks.c 的结构来）：
    ///   · 钩子线程：只做"开合会话"和"投递坐标"两件事。绝不做任何可能长时间阻塞的事。
    ///     —— SetForegroundWindow 要先 AttachThreadInput 到前台线程，目标程序一卡就会
    ///        把我们拖住；EnumWindows 更是慢活。这两样统统 Post 到工作线程去。
    ///     理由是硬性的：回调超过 LowLevelHooksTimeout，Windows 会把钩子【静默摘除】，
    ///     而且没有任何 API 能告诉我们它被摘了。表现出来就是"用着用着突然失灵"。
    ///   · 工作线程（DragWorker）：真正算矩形、SetWindowPos、EnumWindows 找对齐目标，
    ///     并按屏幕刷新率节流。
    ///
    /// 位置算法（这是"窗口跟手"的关键）：
    ///   新位置 = 当前光标 − 拖动开始时的固定偏移。
    ///   绝对推导，不做增量累加 —— 丢事件、合并事件都不会漂移。
    /// </summary>
    internal sealed class DragEngine : IDisposable
    {
        private readonly Func<AppConfig> _cfg;
        private readonly ActivationFrame _frame;
        private readonly DragRingFrame _ring;
        private readonly SnapPreview _preview;
        private readonly Native.EnumWindowsProc _enumProc;
        private readonly DragWorker _worker;

        private static readonly int OwnPid = Process.GetCurrentProcess().Id;

        // 屏幕刷新率实测一次就够，没必要每帧去问显卡。
        private static int _detectedHz;

        internal static int DetectedRefreshHz
        {
            get
            {
                int hz = _detectedHz;
                if (hz > 0) return hz;
                hz = Native.GetRefreshRate();
                _detectedHz = hz;
                Log.Info("实测屏幕刷新率：" + hz + " Hz");
                return hz;
            }
        }

        // ── 会话状态 ────────────────────────────────────────────
        // 钩子线程开合会话、工作线程按帧读取，所以整体用 _gate 保护。
        // 注意：绝不在持锁期间调用 SetWindowPos —— 目标窗口万一卡死，
        // 持锁阻塞会把钩子线程一起拖死。
        private readonly object _gate = new object();

        /// <summary>一次拖动会话的不可变快照。取一次、用整帧，避免反复加锁。</summary>
        private struct Session
        {
            public IntPtr Hwnd;
            public bool Move;
            public int Button;
            public Native.POINT StartCursor;
            public Native.RECT StartRect;
            public ResizeZone Zone;
            public int MinW;
            public int MinH;
            public int Gen;
        }

        private Session _session;
        private volatile bool _dragging;
        private volatile bool _moved;
        private int _generation;                 // 会话代次：丢弃上一轮会话遗留的工作

        // 工作线程发布、钩子线程在收尾时读取
        private SnapZone _publishedSnap;
        private Native.RECT _publishedWork;

        // 窗口"可见边框"相对 GetWindowRect 的内缩量，拖动开始时算一次。
        private Native.RECT _inset;

        // ── 悬停高亮（触发键刚激活、还没按鼠标键时）──────────────
        // _hoverRaw 是 WindowFromPoint 的原始结果，只用来"还在同一扇窗上就别重算"，
        // 真正的接管对象仍然要过 FindTarget（要排除桌面、任务栏、黑名单、全屏窗口）。
        private IntPtr _hoverRaw;
        private IntPtr _hoverHwnd;

        // 上次给悬停外框用的【窗口真实矩形】。同一扇窗时拿它和当前矩形比一下：
        // 一致就彻底跳过热路径（光标在窗口里划来划去是最常见的情形），
        // 不一致说明窗口刚被挪过/缩放过，必须重贴，否则外框会脱离窗口停在旧位置上。
        private Native.RECT _hoverRect;
        private bool _hoverRectValid;

        /// <summary>
        /// 外框当前"该不该在屏幕上"。
        ///
        /// 这是一把额外的保险：光靠会话代次判断还不够 —— 见 Step() 里那次二次校验的注释。
        /// </summary>
        private volatile bool _frameWanted;

        // ── 只有工作线程会碰的字段，不需要锁 ────────────────────
        private readonly List<Native.RECT> _others = new List<Native.RECT>();
        private long _lastEdgeRefresh;

        private bool _overlaysReady;

        public bool IsDragging { get { return _dragging; } }
        public int Button { get { return _session.Button; } }
        public bool LastMoved { get { return _moved; } }

        /// <summary>
        /// 累计成功启动过多少个拖动会话（只增不减）。
        ///
        /// 为什么需要这个计数：<see cref="IsDragging"/> 只反映"【此刻】是否在拖"，
        /// 而端到端用例注入的是 down+up 连发的一对事件 ——
        /// down 让会话起来、up 立刻把它结束，等测试线程拿到 CPU 时
        /// IsDragging 早就归零了，于是"明明成功了却断言失败"。
        ///
        /// 这类误报比没有测试更坏：它会让人怀疑好好的功能是不是坏了，
        /// 从而去改本来正确的代码。所以这里给自检一个不受时序影响的观测点：
        /// 只要 Begin 返回过 true，计数就会 +1。
        /// </summary>
public int SessionCount { get { return _sessionCount; } }

        private int _sessionCount;

        /// <summary>仅供自检：拿到外框浮层句柄去断言"有没有残留"。</summary>
        internal ActivationFrame Frame { get { return _frame; } }

        /// <summary>仅供自检：拖动期环形外框（拖动中替代分层外框的那个）。</summary>
        internal DragRingFrame Ring { get { return _ring; } }

        public DragEngine(Func<AppConfig> cfg)
        {
            _cfg = cfg;
            _frame = new ActivationFrame();
            _ring = new DragRingFrame();
            _preview = new SnapPreview();
            _enumProc = OnEnumWindow;
            _worker = new DragWorker(OnWorkerMove, CurrentFrameIntervalMs);
            _worker.Start();
        }

        public void Dispose()
        {
            Cancel();
            _worker.Dispose();
            _frame.Destroy();
            _ring.Destroy();
            _preview.Destroy();
        }

        private int CurrentFrameIntervalMs()
        {
            try { return _cfg().FrameIntervalMs(DetectedRefreshHz); }
            catch { return 16; }
        }

        /// <summary>
        /// 启动前先把浮层句柄建好。
        ///
        /// 必须在【UI 线程】建：浮层的 WM_APPLY 要靠创建它的那个线程的消息循环来跑，
        /// 而工作线程的消息循环会把一切 hwnd 非零的消息直接丢掉。
        /// </summary>
        public void Precreate()
        {
            try
            {
                _frame.EnsureCreated();
                _ring.EnsureCreated();
                _preview.EnsureCreated();
                _overlaysReady = true;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        // ── 钩子线程：投递光标 ──────────────────────────────────

        /// <summary>
        /// 钩子线程拿到鼠标移动后调这里。只做一次「投递」就返回，
        /// 真正的窗口移动在工作线程上做（见 DragWorker）。
        /// </summary>
        public void DeliverCursor(Native.POINT pt)
        {
            if (!_dragging) return;
            _worker.PostMove(pt);
        }

        // ── 钩子线程：开始 ──────────────────────────────────────

        /// <summary>
        /// 开始一次拖动会话。
        ///
        /// pt 必须是【按钮按下那一次鼠标事件】的坐标，而不是另外去调 GetCursorPos()：
        ///   · 后续每一帧的位移都以它为基准（offset = 按下点 − 窗口左上角），
        ///     同一个来源才不会出现基准和更新对不上的问题；
        ///   · AltSnap 的 init_movement_and_actions(pt) 传的也正是钩子里那个 pt。
        /// </summary>
        public bool Begin(IntPtr hwnd, bool move, int button, Native.POINT pt)
        {
            var c = _cfg();

            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return false;

            bool wasMaximized = Native.IsZoomed(hwnd);

            if (wasMaximized)
            {
                if (!c.RestoreMaximizedWhenMoved) return false;

                // 记录光标在"最大化矩形"里的相对位置，还原后把窗口挪到对应位置，
                // 这样光标不会突然跑到窗口外面（与 Windows 原生拖动的手感一致）。
                double fx = r.Width > 0 ? (pt.X - r.Left) / (double)r.Width : 0.5;
                double fy = r.Height > 0 ? (pt.Y - r.Top) / (double)r.Height : 0.5;
                if (fx < 0) fx = 0; if (fx > 1) fx = 1;
                if (fy < 0) fy = 0; if (fy > 1) fy = 1;

                Native.ShowWindow(hwnd, Native.SW_RESTORE);
                if (!Native.GetWindowRect(hwnd, out r)) return false;

                if (move)
                {
                    int nx = pt.X - (int)(fx * r.Width);
                    int ny = pt.Y - (int)(fy * r.Height);
                    Native.SetWindowPos(hwnd, IntPtr.Zero, nx, ny, r.Width, r.Height,
                        Native.MOVETHICKBORDERS);
                    r = new Native.RECT(nx, ny, nx + r.Width, ny + r.Height);
                }
            }

            ResizeZone zone = ResizeZone.Center;
            if (!move)
            {
                zone = Zones.Resize(r, pt.X, pt.Y, c.CornerPercent);
                if (zone == ResizeZone.Center) zone = Zones.NearestCorner(r, pt.X, pt.Y);
            }

            int minW = Native.GetSystemMetrics(Native.SM_CXMINTRACK);
            int minH = Native.GetSystemMetrics(Native.SM_CYMINTRACK);
            if (minW < 64) minW = 64;
            if (minH < 32) minH = 32;

            // ★ 必须在放开 _dragging 之前算好。
            // 一旦 _dragging 变 true，工作线程就可能立刻跑第一帧，
            // 而第一帧要用 _inset 把外框摆到"可见边"上。
            var inset = ComputeInset(hwnd, r);

            lock (_gate)
            {
                _session = new Session();
                _session.Hwnd = hwnd;
                _session.Move = move;
                _session.Button = button;
                _session.StartCursor = pt;
                _session.StartRect = r;
                _session.Zone = zone;
                _session.MinW = minW;
                _session.MinH = minH;
                _moved = false;
                _publishedSnap = SnapZone.None;
                _publishedWork = new Native.RECT();
                _generation++;
                _session.Gen = _generation;
                _inset = inset;
                _frameWanted = c.ShowActivationFrame;
                _dragging = true;
            }

            _lastEdgeRefresh = 0;
            _inset = ComputeInset(hwnd, r);

            // 让目标程序知道进入"移动/缩放循环"了。终端一类程序靠这个切到字符网格对齐模式。
            // 只有两次 PostMessage，不阻塞，可以安全地留在钩子线程上。
            WindowActions.NotifySizeMove(hwnd, true);

            // 下面是重活，统统甩给工作线程：钩子回调必须尽快返回。
            if (c.BringToFront)
            {
                IntPtr target = hwnd;
                _worker.Post(delegate () { WindowActions.ForceForeground(target); });
            }
            _worker.Post(delegate () { RefreshOtherWindows(); });

            if (c.ShowActivationFrame)
            {
                // 悬停时亮的是抗锯齿分层框；拖动一旦开始就换成区域环（见 DragRingFrame 的注释）。
                // 先收分层框再亮环，两个不会同时留在屏幕上。
                _frame.HideOverlay();
                ShowDragFrame(r, c);
            }

            Log.Info((move ? "开始移动" : "开始缩放") + " 句柄=" + hwnd.ToInt64().ToString("X")
                + " 刷新率=" + DetectedRefreshHz + "Hz");
            Interlocked.Increment(ref _sessionCount);
            return true;
        }

        /// <summary>
        /// 窗口 GetWindowRect 与"眼睛看到的边"之间的差值。
        ///
        /// Win10/11 上一个普通窗口的 GetWindowRect 左右各含约 7px 不可见缩放边框，
        /// 直接拿它画外框会明显偏外。算一次差值，之后每帧平移即可，
        /// 不必每帧去问 DWM（那是跨进程调用，热路径上不能有）。
        /// </summary>
        private static Native.RECT ComputeInset(IntPtr hwnd, Native.RECT wr)
        {
            Native.RECT vis;
            if (!Native.TryGetVisibleRect(hwnd, out vis)) return new Native.RECT();
            if (vis.Width <= 0 || vis.Height <= 0) return new Native.RECT();

            int l = vis.Left - wr.Left;
            int t = vis.Top - wr.Top;
            int rr = wr.Right - vis.Right;
            int b = wr.Bottom - vis.Bottom;

            // 极端值说明 DWM 给了个不合理的矩形，宁可不用
            if (l < 0 || t < 0 || rr < 0 || b < 0) return new Native.RECT();
            if (l > 40 || t > 40 || rr > 40 || b > 40) return new Native.RECT();

            return new Native.RECT(l, t, rr, b);
        }

        // ── 工作线程：每一帧 ────────────────────────────────────

        private void OnWorkerMove(Native.POINT pt)
        {
            // 会话开始那一帧以前就可能进来鼠标移动（Begin 是异步投递前做的），
            // 这里再取一次快照就够了。
            Session s;
            lock (_gate)
            {
                if (!_dragging) return;
                s = _session;
            }
            Step(pt, s, true);
        }

        /// <summary>
        /// 真正计算并写入窗口矩形。运行在 DragWorker 线程上。
        /// 对应 AltSnap 的 MouseMoveNow()。
        /// </summary>
        private void Step(Native.POINT pt, Session s, bool allowEdgeRefresh)
        {
            try
            {
                if (s.Hwnd == IntPtr.Zero) return;
                if (!Native.IsWindow(s.Hwnd)) { Cancel(); return; }

                var c = _cfg();

                // ── 关键：绝对推导，不是增量累加 ──
                // AltSnap 是 posx = pt.x - state.offset.x，这里的 offset 就是
                // 「拖动起点光标 − 起点窗口左上角」，等价写法。丢事件不会漂移。
                int dx = pt.X - s.StartCursor.X;
                int dy = pt.Y - s.StartCursor.Y;
                if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2) _moved = true;

                var mi = Native.GetMonitorInfoAt(pt);
                Native.RECT r;

                SnapZone snap = SnapZone.None;
                Native.RECT snapWork = new Native.RECT();

                if (s.Move)
                {
                    r = new Native.RECT(s.StartRect.Left + dx, s.StartRect.Top + dy,
                                        s.StartRect.Right + dx, s.StartRect.Bottom + dy);

                    // AltSnap 的 init_movement_and_actions(pt, NULL, action, button) 与 DragEngine 的起点都用钩子给的 pt。
                    if (c.EdgeSnap)
                    {
                        snap = Zones.ScreenEdge(mi.rcWork, pt.X, pt.Y, c.SnapDistance);
                        snapWork = mi.rcWork;
                    }

                    if (snap != SnapZone.None)
                    {
                        // 贴边时窗口本体先不动，只显示吸附预览，落点由 End() 统一施加。
                        ShowSnapPreview(Zones.RectOf(snap, snapWork));
                    }
                    else
                    {
                        if (c.SnapToOtherWindows)
                        {
                            if (allowEdgeRefresh) RefreshEdgesIfStale();
                            r = AlignToOthers(r, true, true, true, true, c.SnapDistance, mi.rcWork);
                        }
                        HideSnapPreview();
                    }
                }
                else
                {
                    r = s.StartRect;
                    bool l, t, rr, b;
                    Zones.EdgesOf(s.Zone, out l, out t, out rr, out b);
                    if (l) r.Left = s.StartRect.Left + dx;
                    if (rr) r.Right = s.StartRect.Right + dx;
                    if (t) r.Top = s.StartRect.Top + dy;
                    if (b) r.Bottom = s.StartRect.Bottom + dy;

                    // 最小尺寸：锁住不动的那条边
                    if (r.Width < s.MinW)
                    {
                        if (l) r.Left = r.Right - s.MinW;
                        else r.Right = r.Left + s.MinW;
                    }
                    if (r.Height < s.MinH)
                    {
                        if (t) r.Top = r.Bottom - s.MinH;
                        else r.Bottom = r.Top + s.MinH;
                    }

                    if (c.SnapToOtherWindows)
                    {
                        if (allowEdgeRefresh) RefreshEdgesIfStale();
                        r = AlignToOthers(r, l, rr, t, b, c.SnapDistance, mi.rcWork);
                    }
                }

                // 会话可能在这期间已经被结束/替换，不要再写过期的位置。
                lock (_gate)
                {
                    if (!_dragging || _session.Gen != s.Gen) return;
                    _publishedSnap = snap;
                    _publishedWork = snapWork;
                }

                WindowActions.SetDragRect(s.Hwnd, r);

                // ★★ 这里是"外框永远不消失"那个 bug 的根因所在，务必看明白再改。★★
                //
                // 上面那次代次校验是在【调 SetWindowPos 之前】做的。而 SetWindowPos 在
                // 目标窗口正忙的时候会阻塞相当久（几十毫秒乃至更久），这段时间里用户
                // 已经松开鼠标：钩子线程走完了 End() → 收尾 → 把外框隐藏了。
                // 等工作线程从 SetWindowPos 里出来，它手里那份"会话仍有效"的判断已经过期，
                // 却还是照着它把外框又画了出来 —— 之后再没有任何人会去关掉它。
                //
                // 所以显框之前必须【再校验一次】，而且这一次必须在持锁状态下做完投递，
                // 让"判定有效"和"投递显框"之间不可能再插进一次收尾。
                if (c.ShowActivationFrame)
                {
                    // 拖动中的外框 = 区域环（DragRingFrame），不是分层位图框。
                    // 判定与写入在同一把锁里完成（理由同 TryShowFrame 的注释）：
                    // 上面的 SetWindowPos 可能阻塞很久，期间收尾线程已经藏框并推进了代次，
                    // 过期帧绝不能再把环画出来 —— 否则环就成了关不掉的"外框残留"。
                    // 环的写入是 SWP_NOSENDCHANGING 的 SetWindowPos/SetWindowRgn，
                    // 不发送同步消息，锁内调用不会阻塞。
                    var f = FrameRectOf(r, c);
                    if (f.Width >= 4 && f.Height >= 4)
                    {
                        lock (_gate)
                        {
                            if (_dragging && _session.Gen == s.Gen && _frameWanted)
                            {
                                _ring.ApplyTheme(c.FrameColorId, c.FrameThickness, c.FrameRadius,
                                    FrameStyles.MarginFor(c.FrameStyleId, c.FrameThickness));
                                _ring.SetRectNow(f);
                            }
                        }
                    }
                }

                if (Log.Verbose)
                {
                    // 逐帧记录"我要它去哪"和"它实际在哪"。
                    // 手感问题必须靠这个对比下结论，光靠读代码争不出来。
                    Native.RECT real;
                    Native.GetWindowRect(s.Hwnd, out real);
                    Log.Trace((s.Move ? "移动" : "缩放")
                        + " 光标=" + pt.X + "," + pt.Y
                        + " 期望=" + r
                        + " 实际=" + real
                        + (snap != SnapZone.None ? " 已贴边" : ""));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        // ── 钩子线程：收尾 ──────────────────────────────────────

        public void End()
        {
            if (!_dragging) return;

            IntPtr hwnd;
            bool move;
            bool applySnap;
            SnapZone snap;
            Native.RECT work;
            Session s;
            try
            {
                var c = _cfg();

                lock (_gate)
                {
                    s = _session;
                    hwnd = _session.Hwnd;
                    move = _session.Move;
                    snap = _publishedSnap;
                    work = _publishedWork;
                }

                // 松手时用当前光标做最后一次定位，保证落点精确。
                // 这里【不做】"对齐其它窗口"的重新枚举：沿用上一次的 _others 缓存即可，
                // EnumWindows 是慢活，绝不能出现在钩子线程上。
                Native.POINT pt;
                if (Native.GetCursorPos(out pt))
                {
                    Step(pt, s, false);
                }

                lock (_gate)
                {
                    _dragging = false;
                    _generation++;
                    applySnap = move && c.EdgeSnap && _publishedSnap != SnapZone.None && hwnd != IntPtr.Zero;
                    snap = _publishedSnap;
                    work = _publishedWork;
                }

                if (applySnap)
                {
                    WindowActions.ApplySnap(hwnd, snap, work);
                }

                WindowActions.NotifySizeMove(hwnd, false);
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

        /// <summary>松手时的最后一次刷新（供自检直接驱动）。</summary>
        public void UpdateFinal(Native.POINT pt)
        {
            if (!_dragging) return;
            Session s;
            lock (_gate) { s = _session; }
            Step(pt, s, false);
        }

        /// <summary>异常路径（暂停、退出、配置变更、目标窗口消失）下的收尾，不施加任何吸附。</summary>
        public void Cancel()
        {
            if (!_dragging) return;
            lock (_gate)
            {
                _dragging = false;
                _generation++;
            }
            Cleanup();
        }

        private void Cleanup()
        {
            try
            {
                HideSnapPreview();
                // ★ 顺序不能反：先关掉"允许显框"这道闸，再去隐藏。
                //   工作线程可能正卡在 SetWindowPos 里，等它出来时若这道闸还开着，
                //   外框就会被重新画出来再也关不掉（这正是之前"外框不消失"的成因）。
                _frameWanted = false;
                _ring.HideNow();                        // 拖动环是同步直写的，随叫随收
                if (_overlaysReady) _frame.HideOverlay();
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            finally
            {
                // 清掉悬停缓存：拖动结束后如果触发键还按着，要让悬停高亮能重新算一遍，
                // 否则 _hoverRaw 还是同一扇窗，ShowHoverFrame 会直接跳过、外框不再出现。
                // （拖动把窗口挪走了，此时必须无条件重算，不能走"窗没动就跳过"的快捷路径。）
                InvalidateHover();

                lock (_gate)
                {
                    _session.Hwnd = IntPtr.Zero;
                    _session.Button = -1;
                    _publishedSnap = SnapZone.None;
                }
            }
        }

        // ── 外框与预览 ──────────────────────────────────────────

        /// <summary>
        /// 拖动专用外框（区域环）：同步直写，和 SetWindowPos 同一拍。
        /// 拖动期间的每一帧都走它，不碰分层位图框。
        /// </summary>
        private void ShowDragFrame(Native.RECT windowRect, AppConfig c)
        {
            if (!_overlaysReady) return;
            _ring.ApplyTheme(c.FrameColorId, c.FrameThickness, c.FrameRadius,
                FrameStyles.MarginFor(c.FrameStyleId, c.FrameThickness));
            var f = FrameRectOf(windowRect, c);
            if (f.Width < 4 || f.Height < 4) return;
            _ring.SetRectNow(f);
        }

        /// <summary>把外框摆到窗口的"可见边"上（用拖动开始时算好的内缩量平移）。</summary>
        private void ShowFrame(Native.RECT windowRect)
        {
            if (!_overlaysReady || !_frameWanted) return;
            var c = _cfg();
            _frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius);
            var f = FrameRectOf(windowRect, c);
            if (f.Width < 4 || f.Height < 4) return;
            _frame.SetRect(f);
        }

        /// <summary>
        /// 显框前的最后一道闸。gen 是"我这次判定所依据的会话代次"。
        ///
        /// 判定与投递必须在【同一把锁】里完成：只要有那么一瞬间是在锁外做的判定，
        /// 收尾线程就能钻进这个空隙把外框关掉，而我们还拿着过期的结论把它再画出来 ——
        /// 外框从此再没人关，就成了用户看到的"蓝色框一直留在屏幕上"。
        ///
        /// 返回 true 表示真的投递了显框请求。
        /// </summary>
        internal bool TryShowFrame(Native.RECT windowRect, AppConfig c, int gen)
        {
            if (!_overlaysReady) return false;

            var f = FrameRectOf(windowRect, c);
            if (f.Width < 4 || f.Height < 4) return false;

            lock (_gate)
            {
                if (!_dragging || _session.Gen != gen || !_frameWanted) return false;
                _frame.SetRect(f);
                return true;
            }
        }

        /// <summary>
        /// 窗口矩形 → 外框浮层矩形。
        ///
        /// 两步换算，一步都不能省：
        ///   1. 减掉 _inset：Win10/11 的 GetWindowRect 左右各含约 7px 不可见的缩放边框，
        ///      直接用它会明显偏外；DWM 给的"可见边框"才是用户眼睛看到的边。
        ///   2. 外扩 margin：柔光是往窗外扩的，不外扩就会被窗口边界切掉。
        /// </summary>
        private Native.RECT FrameRectOf(Native.RECT windowRect, AppConfig c)
        {
            int m = FrameStyles.MarginFor(c.FrameStyleId, c.FrameThickness);
            return new Native.RECT(
                windowRect.Left + _inset.Left - m,
                windowRect.Top + _inset.Top - m,
                windowRect.Right - _inset.Right + m,
                windowRect.Bottom - _inset.Bottom + m);
        }

        // ── 悬停高亮：触发键到达阈值之后、还没按鼠标键 ──────────

        /// <summary>
        /// 把光标底下那扇窗的外框亮起来。
        ///
        /// 调用方是钩子线程，所以这里刻意做得很便宜：先拿 WindowFromPoint 的原始结果
        /// 比一下是不是还在同一扇窗上，是就直接返回 —— 拖动之外窗口不会自己移动，
        /// 没必要每次移动都去跑 FindTarget + GetWindowRect + DwmGetWindowAttribute。
        /// </summary>
        public void ShowHoverFrame(Native.POINT pt)
        {
            if (!_overlaysReady || _dragging) return;

            var c = _cfg();
            if (!c.ShowActivationFrame) { HideHoverFrame(); return; }

            IntPtr raw = Native.WindowFromPoint(pt);
            if (raw == IntPtr.Zero) { HideHoverFrame(); return; }

            if (raw == _hoverRaw)
            {
                // ★ 还是同一扇窗，但窗口自己可能刚被别的程序挪过 / 改过尺寸
                //   （用户拖别的窗口、程序自己还原、我们执行完一次性动作…）。
                //   这时候外框如果不重新贴，就会停在旧位置上"脱框"。
                //   一次 GetWindowRect + 矩形比较足够便宜，可以放在鼠标热路径上。
                if (_hoverHwnd == IntPtr.Zero || !Native.IsWindow(_hoverHwnd))
                {
                    InvalidateHover();
                }
                else
                {
                    Native.RECT cur;
                    if (!Native.GetWindowRect(_hoverHwnd, out cur))
                    {
                        InvalidateHover();
                    }
                    else if (_hoverRectValid && cur.Left == _hoverRect.Left && cur.Top == _hoverRect.Top
                             && cur.Right == _hoverRect.Right && cur.Bottom == _hoverRect.Bottom)
                    {
                        return;                        // 窗没动、光标还在同一扇窗上：真的什么都不用做
                    }
                    else
                    {
                        _hoverRect = cur;
                        _hoverRectValid = true;
                        _frameWanted = true;
                        ShowFrame(cur);
                        return;
                    }
                }
            }

            IntPtr hwnd = WindowActions.FindTarget(pt, c.AllowFullscreenWindows);
            if (hwnd == IntPtr.Zero) { _hoverRaw = raw; HideHoverFrame(); return; }

            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) { _hoverRaw = raw; HideHoverFrame(); return; }

            _hoverRaw = raw;
            _hoverHwnd = hwnd;
            _hoverRect = r;
            _hoverRectValid = true;
            _inset = ComputeInset(hwnd, r);
            _frameWanted = true;
            ShowFrame(r);
        }

        /// <summary>
        /// 配置变更后主动刷新浮层外观。
        ///
        /// 两件事：
        ///   1. 丢弃悬停缓存 —— 新的"外框要不要显示"开关可能刚被改动过，
        ///      留着旧缓存会让 ShowHoverFrame 短路掉整段逻辑。
        ///   2. 按新配置重渲染浮层（颜色/样式/粗细/圆角）。
        ///
        /// 拖动环（DragRingFrame）由工作线程每帧 ApplyTheme，本来就跟得上；
        /// 这里主要管"静止时"那圈抗锯齿外框。
        /// </summary>
        public void RefreshOverlayTheme()
        {
            InvalidateHover();
            if (!_overlaysReady) return;

            try
            {
                var c = _cfg();
                _frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        /// <summary>
        /// 让悬停外框的"同一扇窗就跳过"缓存失效，下一次 ShowHoverFrame 会重新算一遍。
        ///
        /// 什么时候需要：窗口矩形【刚刚被程序改过】的时候（最大化 / 还原 / 居中 / 九宫格吸附 / 一键置底…）。
        /// 这时 raw hwnd 没变，ShowHoverFrame 会直接短路返回，框就停在被改动之前的旧位置上
        /// —— 看起来就像"外框没跟着窗口走"。调用方做完这类动作后必须调它。
        /// </summary>
        public void InvalidateHover()
        {
            _hoverRaw = IntPtr.Zero;
            _hoverHwnd = IntPtr.Zero;
            _hoverRectValid = false;
        }

        /// <summary>退出激活态、或光标移到了不接管的区域时收掉外框。正在拖动时不收。</summary>
        public void HideHoverFrame()
        {
            _hoverRaw = IntPtr.Zero;
            _hoverHwnd = IntPtr.Zero;
            _hoverRectValid = false;
            if (_dragging) return;                          // 拖动中的外框由 Begin/End 管
            _frameWanted = false;
            if (_overlaysReady) _frame.HideOverlay();
        }

        private void ShowSnapPreview(Native.RECT r)
        {
            if (!_overlaysReady) return;
            if (!_cfg().SnapPreview) { HideSnapPreview(); return; }
            _preview.SetRect(r);
        }

        private void HideSnapPreview()
        {
            if (!_overlaysReady) return;
            _preview.HideOverlay();
        }

        // ── 与其它窗口对齐 ──────────────────────────────────────

        private void RefreshEdgesIfStale()
        {
            long now = Environment.TickCount64;

            // ══ 间隔从 300ms 放宽到 600ms ══
            //
            // RefreshOtherWindows 内部是一次完整的 EnumWindows，对每个顶层窗口
            // 都要问可见性 / 样式 / 进程 / 类名 / 矩形（近千次系统调用）。
            // 它跑在【拖动工作线程】上，所以这期间一帧都发不出去 ——
            // 300ms 一次等于每 300ms 固定丢一帧，用户感知到的就是"缩放时一卡一卡"。
            //
            // 但这份列表本来就不需要那么新：拖动期间除了被拖的那扇窗，
            // 其它窗口的位置基本不会变（被拖的窗口自己还会被排除掉）。
            // 所以 600ms 足够捕捉到"有窗口新开/关闭"，同时把卡顿尖峰砍掉一半。
            if (now - _lastEdgeRefresh < 600 && _others.Count > 0) return;
            _lastEdgeRefresh = now;
            RefreshOtherWindows();
        }

        private void RefreshOtherWindows()
        {
            _others.Clear();
            try
            {
                Native.EnumWindows(_enumProc, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        /// <summary>
        /// EnumWindows 是按 z 序从上到下的，所以可以利用"已经被更上层窗口完全盖住"来剔除。
        /// 不对齐到看不见的窗口，是 AltSnap 的做法，否则会把窗口对到一扇根本看不到的窗上。
        /// </summary>
        private bool OnEnumWindow(IntPtr h, IntPtr lParam)
        {
            try
            {
                if (h == _session.Hwnd) return true;
                if (!Native.IsWindowVisible(h)) return true;

                long style = Native.GetWindowLong(h, Native.GWL_STYLE);
                if ((style & Native.WS_MINIMIZE) != 0) return true;
                if ((style & Native.WS_VISIBLE) == 0) return true;

                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == (uint)OwnPid) return true;

                if (WindowActions.IsExcludedClass(Native.GetClassName(h))) return true;

                Native.RECT r;
                if (!Native.GetWindowRect(h, out r)) return true;
                if (r.Width <= 0 || r.Height <= 0) return true;
                if (r.Width > 20000 || r.Height > 20000) return true;   // 虚拟桌面之类

                // 被前面（更上层）的窗口完全盖住，就当作看不见
                for (int i = 0; i < _others.Count; i++)
                {
                    if (Covers(_others[i], r)) return true;
                }

                _others.Add(r);
                if (_others.Count >= 64) return false;                  // 够了，别做无谓的开销
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            return true;
        }

        private static bool Covers(Native.RECT outer, Native.RECT inner)
        {
            return outer.Left <= inner.Left && outer.Top <= inner.Top
                && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;
        }

        /// <summary>
        /// 把正在拖动的边对齐到"其它窗口的边"或"屏幕工作区的边"。
        /// 只处理横向有重叠的窗口参与纵向对齐、纵向有重叠的参与横向对齐，避免对齐到无关窗口。
        /// </summary>
        private Native.RECT AlignToOthers(Native.RECT r, bool moveLeft, bool moveRight, bool moveTop, bool moveBottom, int dist, Native.RECT work)
        {
            if (dist < 1) dist = 1;

            // ── 横向 ──
            int bestDx = 0;
            int bestAbs = dist + 1;

            ConsiderEdge(work.Left, r.Left, moveLeft, ref bestDx, ref bestAbs, dist);
            ConsiderEdge(work.Right, r.Right, moveRight, ref bestDx, ref bestAbs, dist);

            for (int i = 0; i < _others.Count; i++)
            {
                var o = _others[i];
                bool vOverlap = o.Bottom > r.Top + 4 && o.Top < r.Bottom - 4;
                if (!vOverlap) continue;
                ConsiderEdge(o.Left, r.Left, moveLeft, ref bestDx, ref bestAbs, dist);
                ConsiderEdge(o.Right, r.Right, moveRight, ref bestDx, ref bestAbs, dist);
            }

            if (bestAbs <= dist)
            {
                r.Left += bestDx;
                r.Right += bestDx;
            }

            // ── 纵向 ──
            int bestDy = 0;
            bestAbs = dist + 1;

            ConsiderEdge(work.Top, r.Top, moveTop, ref bestDy, ref bestAbs, dist);
            ConsiderEdge(work.Bottom, r.Bottom, moveBottom, ref bestDy, ref bestAbs, dist);

            for (int i = 0; i < _others.Count; i++)
            {
                var o = _others[i];
                bool hOverlap = o.Right > r.Left + 4 && o.Left < r.Right - 4;
                if (!hOverlap) continue;
                ConsiderEdge(o.Top, r.Top, moveTop, ref bestDy, ref bestAbs, dist);
                ConsiderEdge(o.Bottom, r.Bottom, moveBottom, ref bestDy, ref bestAbs, dist);
            }

            if (bestAbs <= dist)
            {
                r.Top += bestDy;
                r.Bottom += bestDy;
            }

            return r;
        }

        private void ConsiderEdge(int target, int myEdge, bool enabled, ref int bestDelta, ref int bestAbs, int dist)
        {
            if (!enabled) return;
            int d = target - myEdge;
            int a = d < 0 ? -d : d;
            if (a < bestAbs)
            {
                bestAbs = a;
                bestDelta = d;
            }
        }
    }
}
