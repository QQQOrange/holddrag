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
        ///     —— 第四十六轮补最后一条：松手时的最终定位（同步跨进程写）同样派发到
        ///        工作线程。目标窗口不泵消息时同步 SetWindowPos 的等待没有上限，
        ///        钩子线程卡在那里 = 整机输入无响应（用户报过的"死机只能强制重启"）。
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
        // ★ 第三十三轮：贴边吸附（_publishedSnap/_publishedWork）已随自动贴边整体删除。
        //   这里不再有"拖动中命中屏幕边缘"的概念，收尾时也不需要施加吸附。

        // ── 收尾定位的线程模型（第四十六轮）────────────────────
        // 最终定位（End 的那一次 forceWrite）由【拖动工作线程】执行：
        // 钩子线程只做"快照 + 结束状态 + 派发"，绝不在自己身上做同步跨进程写。
        //
        // 为什么这同时消灭了"过期帧落在最终位置后面"的问题：
        //   工作线程单线程、队列 FIFO —— 会话期间积压的移动帧一定在 End
        //   派发的最终写【之前】被处理完；End 之后到达的移动全部被 Step 的
        //   代次闸拦掉。于是最终写永远是本会话最后一次写入，落点依旧精确。
        //   旧版那套"收尾定位存档 + 过期帧补写"（_endFix*）随之删除 ——
        //   它的判定条件本来就是反的（只在新会话开始后才触发，反而会把
        //   旧会话的最终矩形写到新会话脸上）。

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

        // ── #39 晃窗手势（左右晃一晃，其它窗口全部最小化）─────────
        // 只在移动会话里识别（缩放时晃没有意义）；配置关掉时完全不喂。
        // 播种方式：会话第一帧自动 Reset（_shakeGen 记录已播种的代次），
        // 不依赖 Begin 与在途旧帧的竞争结果，天然无竞态。
        private readonly ShakeDetector _shake = new ShakeDetector();
        private int _shakeGen = -1;

        // ── 外框"钉住"（pin）已于第三十九轮整体删除 ─────────────
        // 曾经（第三十八轮）为了让"松键之后外框还留着"而实现过一套钉子：
        //   _pinnedHwnd/_pinnedRect + PinToWindow/TickPinned/ClearPin + LastSessionHwnd/FramedHwnd。
        // 用户的后续反馈明确了预期行为：
        //   「执行完命令并松开所有按键后，视觉反馈外框仍然存在」是 bug，
        //   「松开按键后外框应立即消失」才是对的。
        // 于是整套钉子连同一圈接线（Coordinator 五处 + RefreshOverlayTheme 重贴）全部删除，
        // 回到"触发键一松、外框立刻收掉"这一条最简单的规则。
        // 注意：这不影响用户另一条明确要求 —— 触发键【仍按着】时做完一次性动作
        // （最大化 / 居中 / 九宫格吸附）外框必须继续亮着，那条由
        // Coordinator.RefreshHoverAfterAction 单独负责，与本处无关。

        /// <summary>
        /// 外框当前"该不该在屏幕上"。
        ///
        /// 这是一把额外的保险：光靠会话代次判断还不够 —— 见 Step() 里那次二次校验的注释。
        /// </summary>
        private volatile bool _frameWanted;

        // ══ 仅供诊断：拖动中"外框更新了、窗口那一帧却没写"的统计 ══
        //   用户报"外框会瞬间脱离跟随"。外框和窗口都由 Step() 在同一帧里更新，
        //   唯一能造成"框走了、窗没走"的可能就是【窗口那一帧被跳过了】。
        //   这两个计数器把这件事变成可观测的数字。
        internal static int DiagFrameUpdates;      // 本会话里外框被更新的帧数
        internal static int DiagWindowSkipped;     // 其中"窗口没写"的帧数
        internal static int DiagWindowWritten;     // 其中"窗口真的写了"的帧数
        internal static void DiagReset()
        {
            DiagFrameUpdates = 0;
            DiagWindowSkipped = 0;
            DiagWindowWritten = 0;
        }

        // ── 只有工作线程会碰的字段，不需要锁 ────────────────────
        // ★ 第三十三轮："其它窗口矩形"缓存（_others）随"自动对齐其它窗口"一起删除。

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

        /// <summary>
        /// 仅供自检：浮层【在系统层面】是不是真的可见。
        ///
        /// ★ 必须用系统 API，不能看自己的标志位 ★
        ///   本轮"全屏转窗口没有外框"的根因就是：
        ///   `IsShown`（我们自己记的影子状态）说"显示"，而 `IsWindowVisible` 说"隐藏"。
        ///   所有只断言 IsShown 的用例当时【全是绿的】，却漏掉了真正的故障。
        ///   以后判断"外框在不在"，一律以这个为准。
        /// </summary>
        internal bool FrameReallyVisible
        {
            get
            {
                try { return _frame.IsShown && Native.IsWindowVisible(_frame.Handle); }
                catch { return false; }
            }
        }


        public DragEngine(Func<AppConfig> config)
        {
            _cfg = config;
            _frame = new ActivationFrame();

            _worker = new DragWorker(OnWorkerMove);
            _worker.Start();
        }

        /// <summary>
        /// 释放引擎：先收尾活动会话（隐藏浮层），停掉工作线程，再销毁全部浮层。
        /// </summary>
        public void Dispose()
        {
            Cancel();
            _worker.Dispose();

            if (_overlaysReady)
            {
                _frame.Destroy();
                _overlaysReady = false;
            }
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
        ///
        /// ★ 不丢事件：每个鼠标移动都投递 ★
        ///
        ///   旧版在这里按 MoveRate 计数丢事件（误抄了 AltSnap 老默认值 2/4）。
        ///   实际上工作线程的合并层已保证：积压的移动事件全部抽干、只写
        ///   【最新】位置 —— 目标窗口任意时刻只收到一个在途同步请求，
        ///   消息队列不可能被灌爆（dragbench 实测 250 事件 → 251 次更新、
        ///   1:1 跟踪）。任何钩子侧降频都只会白白拉低有效更新率，故删除。
        /// </summary>
        public void DeliverCursor(Native.POINT pt)
        {
            if (!_dragging) return;

            _worker.PostMove(pt);
        }

        /// <summary>
        /// AltSnap 的 DRAG_WAIT：光标要离开按下点超过 DragOutThresholdPx 才开始动窗口。
        /// 默认（DragThreshold=1）只对"已经最大化"的窗口启用 —— 从最大化还原时，
        /// 窗口会先被摆到光标下，接下来几像素的手抖不该再带着它晃。
        /// </summary>
        private volatile bool _dragWait;

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
            if (!Native.GetWindowRect(hwnd, out r))
            {
                // 失败路径必须留痕：以前这里是裸的 return false，
                // 用户报"按了没反应"时日志里一个字都没有，等于黑盒排查。
                Log.Info("[诊断] 拖动无法开始：拿不到目标窗口矩形 句柄=" + hwnd.ToInt64().ToString("X"));
                return false;
            }

            bool wasMaximized = Native.IsZoomed(hwnd);

            // ══════ ★ 第四十一轮：全屏 / 最大化窗口先降级成普通窗口 ★ ══════
            //
            //   用户原话："如果在全屏的情况下来触发按键，不管是拖动窗口还是调整窗口大小，
            //             都会把全屏窗口变成非全屏窗口。"
            //   用户报障："到达触发阈值后，右键的功能没有了。"
            //
            //   这两件事是同一条链路。旧代码在这里有两个毛病：
            //     ① 只对【移动】做了"还原之后把窗口摆到光标下"，
            //        缩放漏了 —— 于是最大化窗口上右键，窗口先被 SW_RESTORE
            //        还原到屏幕中央，而光标还在原来最大化的位置（可能在屏幕边缘、
            //        根本不在还原后的窗口里），角区判定随即算出一个毫不相干的边，
            //        表现就是"右键按下去没反应 / 窗口乱跳"。
            //     ② 拒绝时是【静默 return false】，日志里一个字都没有 ——
            //        用户体验是"右键失效"，排查时完全无从下手。
            //
            //   现在统一成一条路径：不管是移动还是缩放，先降级成普通窗口
            //   （最大化→还原，真全屏→缩成工作区 72%），并把光标按比例摆回窗口上。
            bool wasFullscreen = !wasMaximized && WindowActions.IsFullscreen(hwnd, r);

            if (wasMaximized || wasFullscreen)
            {
                if (c.ExitFullscreenOnTrigger)
                {
                    // ★ 第四十九轮：降级写全部【派发】到拖动工作线程 ★
                    //   钩子线程上一律不出现对目标窗口的写调用（实测：连
                    //   SWP_ASYNCWINDOWPOS 都会被目标的位置锁挡住，见
                    //   PostTargetWork 的注释）。这里只做"捕获"：预估矩形
                    //   现场构造，写完由工作线程上的 reconcile 对账。
                    var pending = new List<Action>();
                    if (!WindowActions.ExitFullscreenToWindowed(hwnd, ref r, pt,
                            delegate (Action w) { pending.Add(w); }))
                    {
                        Log.Info("[诊断] 目标是" + (wasMaximized ? "最大化" : "全屏")
                            + "窗口，降级成普通窗口失败 → 放行不接管 句柄=" + hwnd.ToInt64().ToString("X"));
                        return false;
                    }

                    if (pending.Count > 0)
                    {
                        int cornerPct = c.CornerPercent;
                        int btn = button;
                        bool mv = move;
                        Native.POINT cur = pt;
                        IntPtr target = hwnd;
                        PostTargetWork(delegate ()
                        {
                            for (int i = 0; i < pending.Count; i++)
                            {
                                try { pending[i](); }
                                catch (Exception ex) { Log.Error(ex); }
                            }
                            // 工作线程单线程 FIFO：本闭包必早于会话第一帧执行。
                            // 回读真实矩形对账，之后所有帧都基于它。
                            Native.RECT real;
                            if (Native.GetWindowRect(target, out real))
                                ReconcileStartRect(target, btn, mv, cur, cornerPct, real);
                        });
                    }

                    // ★ 这里【不能】再读一次窗口矩形并因失败而返回 ★
                    //
                    //  预估矩形已经是 ExitFullscreenToWindowed 按"写完之后"的
                    //  样子构造出来的（最大化分支用 GetWindowPlacement，其余
                    //  分支按写入值构造）。旧代码在这里又读一次、失败就 return
                    //   false —— 而那是【静默返回】：窗口已经被降级了，拖动却
                    //  没建立、外框也没人画。用户看到的就是"全屏变成了窗口，
                    //  但周围没有外框"。重复读取没有任何收益，却多出一个静默
                    //  失败点，直接去掉。
                    //  （万一预估矩形与真实值有偏差，工作线程上的 reconcile
                    //   会在第一帧之前把它校正，不会累积误差。）
                }
                else if (wasMaximized && c.RestoreMaximizedWhenMoved)
                {
                    // 老行为：只允许"先还原再拖"（且只对移动摆正光标位置）
                    double fx = r.Width > 0 ? (pt.X - r.Left) / (double)r.Width : 0.5;
                    double fy = r.Height > 0 ? (pt.Y - r.Top) / (double)r.Height : 0.5;
                    if (fx < 0) fx = 0; if (fx > 1) fx = 1;
                    if (fy < 0) fy = 0; if (fy > 1) fy = 1;

                    // ★ 第四十六轮补漏：这里也带 SWP_ASYNCWINDOWPOS ★
                    //   本方法运行在钩子线程上（Begin ← Coordinator.OnMouse ← 钩子回调）。
                    //   同步 SetWindowPos 要等目标窗口过程返回，目标不泵消息时没有上限 ——
                    //   钩子线程卡死 = 整机输入无响应（用户报过的"死机只能重启"）。
                    //   这一处当初只改了 ExitFullscreenToWindowed，漏了这条还原分支。
                    //
                    // ★ 第四十九轮再补：ASYNC 也不够，整段改【派发】到工作线程 ★
                    //   还原后的矩形先用 GetWindowPlacement 预估（纯内核读），
                    //   ShowWindow(SW_RESTORE) 是同步跨线程调用，同样不能留在
                    //   钩子线程上；落账后由 reconcile 校正会话矩形。
                    var wp = new Native.WINDOWPLACEMENT();
                    wp.length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
                    bool havePlacement = Native.GetWindowPlacement(hwnd, ref wp)
                        && wp.rcNormalPosition.Width > 0 && wp.rcNormalPosition.Height > 0;
                    if (havePlacement) r = wp.rcNormalPosition;
                    else if (!Native.GetWindowRect(hwnd, out r)) return false;

                    IntPtr target = hwnd;
                    int cornerPct = c.CornerPercent;
                    int btn = button;
                    bool mv = move;
                    Native.POINT cur = pt;
                    if (move)
                    {
                        int nx = pt.X - (int)(fx * r.Width);
                        int ny = pt.Y - (int)(fy * r.Height);
                        int ww = r.Width, hh = r.Height;
                        PostTargetWork(delegate ()
                        {
                            Native.ShowWindow(target, Native.SW_RESTORE);
                            Native.SetWindowPos(target, IntPtr.Zero, nx, ny, ww, hh,
                                Native.MOVETHICKBORDERS | Native.SWP_ASYNCWINDOWPOS);
                            Native.RECT real;
                            if (Native.GetWindowRect(target, out real))
                                ReconcileStartRect(target, btn, mv, cur, cornerPct, real);
                        });
                        r = new Native.RECT(nx, ny, nx + ww, ny + hh);
                    }
                    else
                    {
                        PostTargetWork(delegate ()
                        {
                            Native.ShowWindow(target, Native.SW_RESTORE);
                            Native.RECT real;
                            if (Native.GetWindowRect(target, out real))
                                ReconcileStartRect(target, btn, mv, cur, cornerPct, real);
                        });
                    }
                }
                else
                {
                    // ★ 拒绝路径必须留痕 ★
                    //   （以前这里是裸的 return false，用户报"按键没反应"时
                    //     日志里什么都看不到，等于在黑盒里排查。）
                    Log.Info("[诊断] 目标是" + (wasMaximized ? "最大化" : "全屏")
                        + "窗口，且『触发时退出全屏』已关闭 → 放行不接管"
                        + " 键=" + button + " 移动=" + move);
                    return false;
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

            // ★ AltSnap 的 DRAG_WAIT 判定（hooks.c init_movement_and_actions 尾部）：
            //     conf.DragThreshold >= 2 || (conf.DragThreshold == 1 && IsAnySnapped(hwnd))
            //   默认 DragThreshold=1，于是只有"已经最大化 / 已吸附"的窗口需要死区 ——
            //   因为这类窗口刚被还原时会先被摆到光标下，紧接着几像素的手抖不该再带着它晃。
            bool needDeadZone = c.DragThreshold >= 2 || (c.DragThreshold == 1 && wasMaximized);

            _dragWait = needDeadZone && c.DragOutThresholdPx > 0;


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
                _generation++;
                _session.Gen = _generation;
                _inset = inset;
                _frameWanted = c.ShowActivationFrame;
                _dragging = true;
            }

            // 第三十三轮：删除"一次拖动扫一次其它窗口"（AlignToOthers 的缓存）——
            // 它只服务于"自动对齐其它窗口"，该功能已随自动贴边整体移除。
            WindowActions.ResetSessionKickCache();   // 新会话重新探测 WM_SIZING 支持度
            DiagReset();                             // 诊断计数同样按会话清零
            _frameInside = false;                    // 外框绘制模式按会话复位（回滞起点）
            _inset = ComputeInset(hwnd, r);

            // 让目标程序知道进入"移动/缩放循环"了。终端一类程序靠这个切到字符网格对齐模式。
            // 只有两次 PostMessage，不阻塞，可以安全地留在钩子线程上。
            // ★ Outline（空心矩形）模式例外：真窗口此时还不动，enter/exit 都推迟到
            //   松手落位那一刻（AltSnap 同款 —— 它的 NotifySizeMoveStaEnd 也只在
            //   MoveWindowNow_ 真正写窗口时发）。
            if (c.MovePreviewMode != PreviewModes.Outline)
            {
                WindowActions.NotifySizeMove(hwnd, true);
            }

            // ══ 第三十六轮：刷新率节流接通 ══
            //   每次会话开始时按当前配置算一次帧间隔，交给工作线程在处理完每帧后补睡。
            //   Auto = 跟随实测屏幕刷新率；Custom = 用户固定值。见 AppConfig.FrameIntervalMs。
            _worker.FrameIntervalMs = c.FrameIntervalMs(DetectedRefreshHz);

            // ══ 第四十一轮：拖动期间降低 GC 停顿 ══
            //   拖动是"每 6ms 一帧"的硬实时活儿，而 .NET 的阻塞式 GC
            //   （gen2 回收 / 大对象堆整理）会让工作线程停住几十毫秒 ——
            //   表现就是拖到一半"突然顿一下"。SustainedLowLatency 让运行时
            //   尽量避免阻塞式回收（改用后台回收），代价是堆稍微大一点，
            //   对一次几秒的拖动完全划算。收尾时恢复。
            try
            {
                System.Runtime.GCSettings.LatencyMode =
                    System.Runtime.GCLatencyMode.SustainedLowLatency;
            }
            catch (Exception ex) { Log.Error(ex); }

            // 下面是重活，统统甩给工作线程：钩子回调必须尽快返回。
            if (c.BringToFront)
            {
                // ★★★ 绝不能挂在拖动工作线程上 ★★★
                //
                //   ForceForeground 要做 AttachThreadInput + SetForegroundWindow，
                //   是实打实的重活（几十毫秒级）。挂在拖动工作线程的队列上，
                //   就意味着【每一次拖动的第一帧】都要先等它做完才能写窗口 ——
                //   用户感受到的就是"每次开始拖动都要先顿一下才跟手"。
                //
                //   而"提到最前"纯粹是 z 序上的优化，晚几十毫秒完全没有影响，
                //   所以丢给独立线程即发即弃，绝不占用拖动管线的任何一帧。
                IntPtr target = hwnd;
                var raise = new System.Threading.Thread(delegate ()
                {
                    try { WindowActions.ForceForeground(target); }
                    catch (Exception ex) { Log.Error(ex); }
                });
                raise.IsBackground = true;
                raise.Name = "HoldDrag.Raise";
                raise.Start();
            }

            if (c.ShowActivationFrame)
            {
                // 先收悬停框，再按【降级后的新矩形】亮拖动框 —— 两个不会同时留在屏幕上。
                _frame.HideOverlay();
                ShowDragFrame(r, c);

                // ★ 低频诊断（每会话一条）★
                //   "触发了却没有外框"这类问题必须在日志里能一眼确认：
                //   到底是"没让画"（开关/ready），还是"画了但位置不对"。
                // ★ 全链路几何诊断 ★
                //   "全屏转窗口没有外框"排查了几轮都没定论，就是因为缺这条数据。
                //   一次性把【决定外框长在哪】的每个量都打出来：
                //   目标矩形 / 可见区缩进 / 最终外框矩形 / 内缩还是外扩 ——
                //   任何一环算错，日志里一眼就能看出来，不用再猜。
                bool dbgInset;
                var dbgFrame = FrameRectOf(r, c, out dbgInset);
                Log.Info("[诊断] 拖动框已请求显示 开关=" + c.ShowActivationFrame
                    + " 就绪=" + _overlaysReady
                    + " 目标矩形=" + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height
                    + " 可见缩进=" + _inset.Left + "/" + _inset.Top + "/" + _inset.Right + "/" + _inset.Bottom
                    + " 外框矩形=" + dbgFrame.Left + "," + dbgFrame.Top + " "
                    + dbgFrame.Width + "x" + dbgFrame.Height
                    + (dbgInset ? " 模式=内缩" : " 模式=外扩")
                    + " 抬到最顶=" + _frame.RaiseAboveAll
                    // 第四十八轮：显框改走异步管线，此处的 IsShown 还是投递前的
                    // 影子状态；补一个"请求是否已投递"才分得清
                    // "没让画"和"画了在途"两种情形。
                    + " 显示=" + _frame.IsShown
                    + " 请求=" + (_frame.ShowRequestedForTest ? "已投递" : "未投递"));
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
            Step(pt, s);
        }

    /// <summary>
    /// 真正计算并写入窗口矩形。运行在 DragWorker 线程上。
    /// 对应 AltSnap 的 MouseMoveNow()。
    /// </summary>
    private void Step(Native.POINT pt, Session s, bool forceWrite = false)
        {
            try
            {
                if (s.Hwnd == IntPtr.Zero) return;
                if (!Native.IsWindow(s.Hwnd)) { Cancel(); return; }

                var c = _cfg();

                // ══════ AltSnap MouseMoveNow 开头的 ClipCursor 修正 ══════
                //   钩子拿到的坐标有时会落在"系统当前的光标裁剪矩形"之外
                //   （系统 bug，AltSnap 注释里写着 "Ensure the point is in its
                //   clip rectangle (Windows bug)"）。不修的话窗口会被拉到一个
                //   根本不在屏幕上的位置，看起来就是"拖飞了"。
                Native.RECT clip;
                if (Native.GetClipCursor(out clip) && clip.Right > clip.Left && clip.Bottom > clip.Top)
                {
                    if (pt.X < clip.Left) pt.X = clip.Left;
                    if (pt.X > clip.Right - 1) pt.X = clip.Right - 1;
                    if (pt.Y < clip.Top) pt.Y = clip.Top;
                    if (pt.Y > clip.Bottom - 1) pt.Y = clip.Bottom - 1;
                }

                // ══════ AltSnap 的 DRAG_WAIT 死区 ══════
                //   hooks.c LowLevelMouseProc:
                //       if (state.moving == DRAG_WAIT) {
                //           if (IsPtDragOut(&state.prevpt, &pt)) return CallNextHookEx(...);
                //           state.moving = 0;
                //       }
                //   光标还在按下点附近（< DragOutThresholdPx）就【什么都不做】，
                //   窗口保持不动；一旦超出死区就正式开始跟手。
                if (_dragWait)
                {
                    int tx = c.DragOutThresholdPx;
                    if (Math.Abs(pt.X - s.StartCursor.X) < tx &&
                        Math.Abs(pt.Y - s.StartCursor.Y) < tx)
                    {
                        return;
                    }
                    _dragWait = false;
                }

                // ── 关键：绝对推导，不是增量累加 ──
                // AltSnap 是 posx = pt.x - state.offset.x，这里的 offset 就是
                // 「拖动起点光标 − 起点窗口左上角」，等价写法。丢事件不会漂移。
                int dx = pt.X - s.StartCursor.X;
                int dy = pt.Y - s.StartCursor.Y;
                if (Math.Abs(dx) > 2 || Math.Abs(dy) > 2) _moved = true;

                // ── 本帧的目标矩形 ──
                //   Step 不再查询监视器信息（自动贴边已随第三十三轮整体删除）：
                //   MonitorFromPoint + GetMonitorInfo 每帧两次系统调用，
                //   而拖动时 99% 的时间光标都在同一块屏上 —— 没有使用者就一次都不查。
                Native.RECT r;

                if (s.Move)
                {
                    r = new Native.RECT(s.StartRect.Left + dx, s.StartRect.Top + dy,
                                        s.StartRect.Right + dx, s.StartRect.Bottom + dy);
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
                }

                // 会话可能在这期间已经被结束/替换，不要写过期的位置。
                // ★ forceWrite（松手最终定位）是例外：它由 End() 派发到本线程，
                //   到达时会话已正常结束（_dragging=false），属于"理所当然的
                //   最后一写"。放行条件收窄为"还没有开始【新】会话"。
                lock (_gate)
                {
                    if (forceWrite)
                    {
                        if (_session.Gen != s.Gen) return;
                    }
                    else if (!_dragging || _session.Gen != s.Gen) return;
                }

                // ══════ #39 晃窗手势：真晃才触发（第四十轮收紧）══════
                //   用户原话："触发键触发之后按左键选中窗口，那这时候晃一晃这个窗口，
                //   那其他的窗口就都最小化，除了当前这个窗口之外"；
                //   后续反馈："目前只要拖动窗口就会触发，属于误触发"。
                //
                //   判定完全走运动学（见 ShakeDetector 的详细注释）：滑动窗口内
                //   必须同时满足「≥4 次有效往复 + 单向位移不超上限 + 净位移接近 0
                //   + 横向为主 + 路径足够长 + 全程 ≤800ms」。
                //   其中"单向位移上限"和"净位移接近 0"是区分晃动与拖动的关键。
                //
                //   放在代次复核之后：过期帧不会污染识别，Begin 也不用和
                //   在途旧帧抢着 Reset（第一帧自己会播种）。
                if (s.Move)
                {
                    if (_shakeGen != s.Gen)
                    {
                        // 阈值以逻辑像素给出，这里按目标窗口所在显示器的 DPI 换算 ——
                        // 旧实现写死物理像素，200% 屏上等于把门槛缩小一半，必然误触发。
                        int dpi = Native.DpiOfWindow(s.Hwnd);
                        _shake.SetScale(dpi / 96.0);
                        _shake.Reset(s.StartCursor.X, s.StartCursor.Y, Environment.TickCount64);
                        _shakeGen = s.Gen;
                    }
                    if (c.ShakeMinimizeOthers
                        && _shake.Feed(pt.X, pt.Y, Environment.TickCount64))
                    {
                        // 执行器即发即弃（内部起后台线程）：绝不阻塞拖动工作线程。
                        // 窗口消失/光标早已离开等边界情况它自己会兜。
                        ShakeAction.ToggleMinimizeOthers(s.Hwnd);
                    }
                }

                // ══════ ★ AltSnap 的丝滑核心：空心矩形模式（FullWin=0）★ ══════
                //
                //   AltSnap 默认根本不实时移动真窗口 —— 拖动时跟手的是它自己的
                //   透明预览浮层（自家窗口、异步移动、零延迟），真窗口在松手时
                //   一次性落位。跟手性由【构造】保证：浮层不依赖目标窗口的
                //   消息处理速度，再忙的程序也拖不卡它。
                //
                //   FullWin 模式（实时移动真窗口）才需要目标窗口逐帧配合 ——
                //   那是唯一会"卡"的模式，AltSnap 也一样。
                bool outline = c.MovePreviewMode == PreviewModes.Outline && !forceWrite;

                // ══════════════════════════════════════════════════════════════
                //  ★ 第四十四轮：外框【先行】—— 先把框摆到目标位置，再写窗口 ★
                //
                //  用户报障："外框并非始终跟随窗口，拖动窗口时会出现瞬间脱离跟随
                //           （掉帧或错位）。"
                //
                //  根因是【更新顺序 + 异步投递】叠加：
                //    老顺序是「先同步写窗口 → 再更新外框」。窗口写是同步的
                //    （SetWindowPos 要等目标线程处理完，实测平均 2.6ms、重应用上几十毫秒），
                //    也就是说【窗口已经在新位置了，而外框还停在旧位置】——
                //    这段时间屏幕上就是"窗口跑出了框"。
                //    更糟的是外框那一步当时走的是 SWP_ASYNCWINDOWPOS：请求只是被投进
                //    UI 线程的消息队列，UI 线程一忙（GC / 重绘 / 托盘菜单）就迟迟不执行，
                //    错位从"半帧"放大到几十毫秒 —— 肉眼就是"瞬间脱离跟随"。
                //
                //  修法两条，缺一不可：
                //    ① 【顺序】外框先动、窗口后动 —— 于是任何时刻都不会出现
                //       "窗口已经到新位置、框还停在旧位置"。框最多是略微领先，
                //       而"领先"在视觉上几乎不可察，"滞后"却极其刺眼。
                //    ② 【同步】外框移动改成同步 SetWindowPos（见 LayeredBitmapOverlay.SetRectNow），
                //       调用返回时框【一定】已在目标位置，绝不再交给消息队列慢慢处理。
                //
                //  为什么同步不会拖慢拖动：外框是我们自己的窗口，SetWindowPos 只走
                //  它自己的窗口过程，不存在"等目标应用重绘"那种长阻塞。
                //
                //  ★ 第四十八轮修正：这里原来写着"SWP_NOSENDCHANGING，不惊动任何
                //    别的线程"—— 这句是错的，也正是它掩盖了一个整机卡死级 bug：
                //    SWP_NOSENDCHANGING 只抑制 WM_WINDOWPOSCHANGING，
                //    WM_WINDOWPOSCHANGED 仍会【同步】发给拥有浮层窗口的线程。
                //    只要 SetRectNow 持着 _bufGate 做这个调用，而浮层线程恰好
                //    卡在同一把锁上（它自己的 DrainApply→…→Push），双方互等。
                //    在工作线程上这还只是拖动卡住；在钩子线程上（Begin 的
                //    ShowDragFrame）就是 WH_MOUSE_LL 回调不返回 → 系统鼠标
                //    输入整体冻结 → 用户报障"整个电脑死机"。
                //    现行纪律（Overlays.cs 同款注释）：
                //     · 每帧同步直写只允许在【拖动工作线程】上做（本处即是）；
                //     · 钩子线程的显框改走异步管线（ShowDragFrame → SetRect）；
                //     · _bufGate 内绝不执行任何跨线程窗口调用。
                // ══════════════════════════════════════════════════════════════
                if (c.ShowActivationFrame && !outline)
                {
                    bool preInset;
                    var preFrame = FrameRectOf(r, c, out preInset);
                    // 内缩模式 → 浮层必须抬到目标窗口之上，否则框会被窗口盖住
                    _frame.RaiseAboveAll = preInset;
                    if (preFrame.Width >= 4 && preFrame.Height >= 4)
                    {
                        // 判定与写入必须在同一把锁里完成：收尾线程可能刚好在推进代次
                        // 并藏框，过期帧绝不能再把框画出来（否则就是关不掉的"外框残留"）。
                        lock (_gate)
                        {
                            if (_dragging && _session.Gen == s.Gen && _frameWanted)
                            {
                                _frame.ApplyTheme(c.FrameColorId, c.FrameStyleId,
                                                  c.FrameThickness, c.FrameRadius, c.FrameOpacity);
                                _frame.SetRectNow(preFrame);
                                DiagFrameUpdates++;
                            }
                        }
                    }
                }

                if (outline)
                {
                    ShowOutline(r, s, c);
                }
                else if (forceWrite)
                {
                    // 最终定位（松手/吸附）：同步写，确保精确落位。
                    // 运行在拖动工作线程上（End 派发）或自检线程上，
                    // 绝不运行在钩子线程上 —— 见 End() 的注释。
                    WindowActions.SetDragRect(s.Hwnd, r);
                }
                else
                {
                    // 拖动中的每一帧：【同步】写入（AltSnap MoveWindowNow 同款）。
                    // 阻塞时长 = 目标真实消化耗时，是最天然的背压节奏；阻塞期间
                    // 新事件被 DragWorker 合并层抽干只留最新，写出去的永远是光标
                    // 最新位置。慢目标上有效更新率 = 目标真实消化能力，一帧不浪费，
                    // 且永不滑回过过期位置（第十九版"异步+回执"翻车教训）。
                    //
                    // 纯平移会话传 moveOnly=true：尺寸一定不变，
                    // 于是这一帧能省掉一次跨进程 GetWindowRect（见该方法的注释）。
                    if (WindowActions.SetDragRectFrame(s.Hwnd, r, s.Move)) DiagWindowWritten++;
                    else DiagWindowSkipped++;

                    // ★ 第四十六轮：这里曾是"过期帧补写"（_endFix）★
                    //   工作线程单线程 FIFO + End 把最终写也派发到本线程之后，
                    //   "过期帧落在最终位置后面"在构造上已不可能：
                    //   会话内积压帧必先于最终写处理完，会后移动被代次闸拦掉。
                    //   整套存档/补写机制已删除，详见文件头"收尾定位的线程模型"。
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
                        + " 实际=" + real);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        // ── 钩子线程：收尾 ──────────────────────────────────────

        /// <summary>
        /// 结束一次拖动会话（鼠标抬起时由钩子线程调用）。
        ///
        /// ★ 第四十六轮：最终定位【派发给工作线程】，钩子线程自己绝不写窗口 ★
        ///
        ///   旧实现在这里同步跑 Step(forceWrite)，其中的 SetDragRect 是同步
        ///   跨进程写：SetWindowPos 要等目标窗口的窗口过程处理完
        ///   WM_WINDOWPOSCHANGING 才返回，目标一旦不泵消息（卡死 / 弹模态框 /
        ///   渲染卡顿），这个等待【没有上限】——钩子线程就此卡死，之后全系统
        ///   的每一次键鼠事件都要先经过我们这个永远不返回的回调。用户看到的
        ///   现象："整个电脑死机，鼠标还能移动，但所有点击都没反应，只能重启"。
        ///   （右键缩放正好叠了两层：Begin 的降级写 + End 的最终写都曾在
        ///     钩子线程上同步跑。）
        ///
        ///   现在钩子线程只做：快照、结束状态、Cleanup（全是自己的窗口和
        ///   布尔量）；最终的那一次写入随 WM_DOWORK 派发到 DragWorker。
        ///   落点依旧精确：工作线程单线程 FIFO，会话内积压的移动帧必先于
        ///   最终写处理完，End 之后到达的移动被代次闸拦掉（见 Step）。
        /// </summary>
        public void End()
        {
            if (!_dragging) return;

            IntPtr hwnd;
            Session s;
            try
            {
                var c = _cfg();

                lock (_gate)
                {
                    s = _session;
                    hwnd = _session.Hwnd;
                }

                // 松手时用当前光标做最后一次定位，保证落点精确。
                // Outline 模式：真窗口此前一直没动，落位前补发 ENTER（与 AltSnap 一致）
                if (c.MovePreviewMode == PreviewModes.Outline && hwnd != IntPtr.Zero)
                {
                    WindowActions.NotifySizeMove(hwnd, true);
                }

                Native.POINT pt;
                bool haveFinal = Native.GetCursorPos(out pt);

                // 先结束状态再派发：Step 的代次闸靠 _session.Gen 认"这帧属于
                // 哪个会话"。期间若用户已开始新会话，旧会话的最终写自动作废。
                lock (_gate)
                {
                    _dragging = false;
                    _generation++;
                }

                if (haveFinal && hwnd != IntPtr.Zero)
                {
                    // forceWrite = true：最后一帧必须写到真窗口（空心矩形模式也如此）
                    _worker.Post(() => Step(pt, s, forceWrite: true));
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
            // 语义是"最终定位"：Outline（空心矩形）模式下也必须真的写窗口
            Step(pt, s, forceWrite: true);
        }

        /// <summary>
        /// ★ 第四十九轮：把"对目标窗口的写操作"派发到拖动工作线程 ★
        ///
        /// 为什么钩子线程上一次都不能写目标窗口（自检 T85 实测坐实的两条）：
        ///   ① 同步语义的调用（SetWindowPos / ShowWindow / SendMessage…）要等
        ///      目标线程把消息处理完才返回，目标卡住时【没有上限】——
        ///      这就是用户报障"整个电脑死机只能重启"的根因形态；
        ///   ② 连 SWP_ASYNCWINDOWPOS 也躲不开：目标线程正压着一条位置更新
        ///      不放时（在 WM_WINDOWPOSCHANGING 里睡 1.5s 模拟），该窗口的
        ///      位置锁会让另一个线程的异步提交同样排队 —— ToggleTopMost
        ///      实测阻塞 1521ms。唯一的出路是【根本不在这条线程上发起调用】。
        ///
        /// 工作线程允许被慢目标拖住：那是设计内的背压（每帧 SetWindowPos
        /// 本来就会等目标消化），而且阻塞的只是我们自己的一条后台线程，
        /// 与全系统的输入派发链无关。
        ///
        /// 用法：把"动作 + 动作之后的悬停框刷新"打成一个闭包一起投递，
        /// 保证刷新读到的是动作之后的新矩形。
        /// </summary>
        internal void PostTargetWork(Action work)
        {
            if (work == null) return;
            if (_worker != null && _worker.IsRunning)
            {
                _worker.Post(work);
                return;
            }
            // 工作线程没起来的极端情形：只能就地执行（自检直接 new 引擎、
            // 或线程启动失败）。这种路径上没有"钩子线程"这顶帽子。
            try { work(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>
        /// 第四十九轮：全屏/最大化降级写在【工作线程】上完成之后，把会话
        /// 初始化用的预估矩形校正成真实矩形。
        ///
        /// 为什么需要：降级写已从钩子线程派发出去（钩子线程零阻塞），而
        /// Begin 在派发那一刻就必须把会话建好 —— 预估矩形来自
        /// GetWindowPlacement（工作区坐标）或写入值构造，可能与真实矩形
        /// 有偏差。拖动工作线程是单线程 FIFO：降级闭包必然早于会话的
        /// 第一帧移动执行，校正之后所有帧都基于真实矩形，不会跳位。
        /// </summary>
        private void ReconcileStartRect(IntPtr hwnd, int button, bool move, Native.POINT pt,
                                        int cornerPercent, Native.RECT real)
        {
            lock (_gate)
            {
                if (!_dragging) return;                                        // 会话已结束
                if (_moved) return;                                            // 已写过帧，不再改基线
                if (_session.Hwnd != hwnd || _session.Button != button) return; // 已是新会话

                var old = _session.StartRect;
                if (old.Left == real.Left && old.Top == real.Top
                    && old.Right == real.Right && old.Bottom == real.Bottom) return;

                _session.StartRect = real;
                _inset = ComputeInset(hwnd, real);
                if (!move)
                {
                    var zone = Zones.Resize(real, pt.X, pt.Y, cornerPercent);
                    if (zone == ResizeZone.Center) zone = Zones.NearestCorner(real, pt.X, pt.Y);
                    _session.Zone = zone;
                }
                Log.Info("[诊断] 降级落账后校正会话矩形：" + old.Left + "," + old.Top
                    + " " + old.Width + "x" + old.Height + " → " + real.Left + "," + real.Top
                    + " " + real.Width + "x" + real.Height);
            }
        }

        /// <summary>
        /// 退出路径：立刻把浮层从屏幕上收掉（同步，不走消息队列）。
        ///
        /// ★ 为什么退出时还要专门做这一步 ★
        ///   实测：日志写下"退出"之后，进程还要 4~5 秒才真正结束。
        ///   而浮层是 WS_EX_TOPMOST 的顶层窗口 —— 进程没结束，它就一直留在屏幕上。
        ///   用户看到的就是"软件都退出了，外框还赖着不走"。
        ///   （这一条已由 ExitApp 里强制结束进程根治；这里是第一道保险。）
        /// </summary>
        internal void HideOverlaysNow()
        {
            _frameWanted = false;
            _dragging = false;
            try { _frame.HideNowSync(); } catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>
        /// 外框不变量兜底：既没在拖动、又不该显示悬停框时，浮层必须不可见。
        ///
        /// ★ 为什么需要它 ★
        ///   "外框残留"是一类【可以有多条成因】的问题（丢鼠标抬起事件、
        ///   触发键中途失效、窗口被别的程序关掉……）。每一条单独修都可以，
        ///   但只要漏掉一条，用户看到的就是"框一直挂在屏幕上，还不掉"。
        ///   与其指望穷举所有路径，不如加一条【周期性自检】：
        ///   任何时刻只要"本不该有框却还亮着"，立刻收掉。
        ///   代价是每 500ms 一次布尔判断，可以忽略。
        /// </summary>
        internal void EnforceFrameInvariant(bool triggerActive)
        {
            try
            {
                bool shouldShow = _dragging
                               || (triggerActive && _hoverHwnd != IntPtr.Zero && _hoverRectValid);
                if (!shouldShow && _frame.IsShown)
                {
                    Log.Info("[兜底] 外框本不该显示（不在拖动、也没有悬停目标）→ 强制收掉");
                    _frameWanted = false;
                    _frame.HideNowSync();
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>仅供自检：驱动一次拖动中的移动事件（不走工作线程）。</summary>
        internal void StepForTest(Native.POINT pt)
        {
            if (!_dragging) return;
            Session s;
            lock (_gate) { s = _session; }
            Step(pt, s);
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
                // ★ 顺序不能反：先关掉"允许显框"这道闸，再去隐藏。
                //   工作线程可能正卡在 SetWindowPos 里，等它出来时若这道闸还开着，
                //   外框就会被重新画出来再也关不掉（这正是之前"外框不消失"的成因）。
                _frameWanted = false;
                if (_overlaysReady) _frame.HideNowSync();   // 同步收框：松手那一刻就不该再有框

                // 恢复常规 GC 模式（Begin 里为了减少拖动中的停顿切到了 SustainedLowLatency）
                try
                {
                    System.Runtime.GCSettings.LatencyMode =
                        System.Runtime.GCLatencyMode.Interactive;
                }
                catch (Exception gex) { Log.Error(gex); }
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
                }
            }
        }

        // ── 外框与预览 ──────────────────────────────────────────

        /// <summary>
        /// 拖动专用外框：同步直写，和 SetWindowPos 同一拍。
        ///
        /// <summary>
        /// 亮起拖动框。运行在【钩子线程】上（Begin 由 WH_MOUSE_LL / 热键回调驱动）。
        ///
        /// ★ 第四十轮：改用抗锯齿分层框（ActivationFrame）★
        ///   拖动期间和静止期间现在是【同一段绘制代码、同一个浮层】，
        ///   所以"设置里选的样式"和"拖动时看到的样子"天然一致。
        ///
        /// ★ 第四十八轮：改走异步管线（SetRect），钩子线程零阻塞 ★
        ///   原先是直接同步写（SetRectNow）。SetRectNow 会对【别的线程
        ///   拥有的】浮层窗口跨线程调 SetWindowPos，等浮层线程泵
        ///   WM_WINDOWPOSCHANGED；而浮层线程此刻可能正卡在 _bufGate 上
        ///   （它自己的 DrainApply→ApplyNow→OnPlace→Push，与拖动首帧
        ///   并发）—— 互等 → 钩子线程死在 WH_MOUSE_LL 回调里 →
        ///   系统鼠标输入整体冻结。用户报障"Enter+右键缩放整个电脑死机、
        ///   只能强制重启"的根因（自检 T81 两次稳定复现坐实）。
        ///
        ///   首帧显框没有任何"必须同步"的理由：真正的窗口写入发生在拖动
        ///   工作线程上、至少一帧（约 6ms）之后；异步请求投递到【专属浮层
        ///   线程】的执行延迟是微秒级，视觉上与同步没有差别。
        ///   而稳态每帧仍走工作线程上的同步直写（SetRectNow），
        ///   "外框先动、窗口后动"的顺序不变。
        /// </summary>
        private void ShowDragFrame(Native.RECT windowRect, AppConfig c)
        {
            if (!_overlaysReady) return;
            bool inset;
            var f = FrameRectOf(windowRect, c, out inset);
            _frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius, c.FrameOpacity);
            if (f.Width < 4 || f.Height < 4) return;
            _frame.SetRect(f);
        }

        /// <summary>把外框摆到窗口的"可见边"上（用拖动开始时算好的内缩量平移）。</summary>
        private void ShowFrame(Native.RECT windowRect)
        {
            if (!_overlaysReady || !_frameWanted) return;
            var c = _cfg();
            _frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius, c.FrameOpacity);
            bool inset;
            var f = FrameRectOf(windowRect, c, out inset);
            _frame.RaiseAboveAll = inset;
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

            bool inset;
            var f = FrameRectOf(windowRect, c, out inset);
            _frame.RaiseAboveAll = inset;
            if (f.Width < 4 || f.Height < 4) return false;

            lock (_gate)
            {
                if (!_dragging || _session.Gen != gen || !_frameWanted) return false;
                _frame.SetRect(f);
                return true;
            }
        }

        /// <summary>
        /// 窗口矩形 → 外框浮层矩形 + 是否内缩绘制。
        ///
        /// 两步换算，一步都不能省：
        ///   1. 减掉 _inset：Win10/11 的 GetWindowRect 左右各含约 7px 不可见的缩放边框，
        ///      直接用它会明显偏外；DWM 给的"可见边框"才是用户眼睛看到的边。
        ///   2. 外扩 margin：柔光是往窗外扩的，不外扩就会被窗口边界切掉。
        ///
        /// 第 3 步是第三十七轮新增的【边缘自适应】：
        ///   外扩矩形整个放不进虚拟桌面时（窗口靠边 / 最大化），改为内缩绘制 ——
        ///   浮层等于窗口可见矩形，Draw() 的 margin 内缩把圆环放进窗口内侧。
        ///   否则最大化窗口的外框四条边全在屏幕外，用户"完全看不到"（#35 根因）。
        /// </summary>
        private Native.RECT FrameRectOf(Native.RECT windowRect, AppConfig c, out bool inset)
        {
            int m = FrameStyles.MarginFor(c.FrameStyleId, c.FrameThickness);
            var vis = new Native.RECT(
                windowRect.Left + _inset.Left,
                windowRect.Top + _inset.Top,
                windowRect.Right - _inset.Right,
                windowRect.Bottom - _inset.Bottom);

            // ★★★ 第四十五轮：外框几何必须【只由窗口决定】，不许受屏幕边缘影响 ★★★
            //
            //  用户报障："窗口被拖到屏幕边缘、甚至超出屏幕之外时，包住窗口的那一圈
            //            样式就会缩小……会直观影响流畅程度，视觉上显得很卡。"
            //
            //  老实现在这里做两件错事：
            //    ① 判据是"外扩矩形放不放得进虚拟桌面"—— 窗口【刚碰到边缘】就翻面，
            //       外框从外扩跳到内缩，偏移量突变 2×margin；
            //    ② 内缩分支还调了 ClampToScreen 把矩形【钳回屏幕】——
            //       于是越往外拖，框越小，而且每帧都在变。
            //  两者都让"外框几何"依赖了屏幕边缘，拖动时每帧都在改几何 → 观感上就是卡。
            //
            //  现在：只有"窗口几乎铺满整块显示器"（最大化 / 全屏）时才切内缩
            //  （那时外框四条边全在屏幕外，不切就彻底看不见）；
            //  其余情况一律外扩，且【不再钳屏幕】—— 几何恒定，贴边拖也丝滑。
            //  贴边时外框那一条边被屏幕裁掉是物理必然，不是缺陷。
            bool fills = FrameStyles.FillsScreen(vis, m, _frameInside);

            if (!fills)
            {
                inset = false;
                _frameInside = false;
                return new Native.RECT(vis.Left - m, vis.Top - m, vis.Right + m, vis.Bottom + m);
            }

            inset = true;
            _frameInside = true;
            return vis;      // 内缩：直接等于可见矩形，几何仍严格跟着窗口
        }

        /// <summary>外框当前是否处于内缩绘制（用于 FillsScreen 的回滞）。每次会话开始时复位。</summary>
        private bool _frameInside;

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
                    else if (_frame.IsShown && _hoverRectValid
                             && cur.Left == _hoverRect.Left && cur.Top == _hoverRect.Top
                             && cur.Right == _hoverRect.Right && cur.Bottom == _hoverRect.Bottom)
                    {
                        return;                        // 框还亮着、窗没动、光标还在同一扇窗上：真的什么都不用做
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

            // ★ 必须和 HandleButtonDown 用【同一个放行条件】★
            //   那边是 `AllowFullscreenWindows || ExitFullscreenOnTrigger`，
            //   这里原来只看 AllowFullscreenWindows —— 于是开着"触发时退出全屏"
            //   的时候：全屏窗口【按下能操作、悬停却一个框都不亮】，
            //   用户看到的就是"触发了但窗口周围没有样式"。
            //   两个开关必须同源，否则悬停态和拖动态会对不上。
            IntPtr hwnd = WindowActions.FindTarget(pt,
                c.AllowFullscreenWindows || c.ExitFullscreenOnTrigger);
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
                _frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius, c.FrameOpacity);
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

        // ── 外框"钉住"（pin）已于第三十九轮整体删除 ───────────
        // 见类内 _frameWanted 附近的说明：用户要求"松开按键后外框立即消失"，
        // 于是 PinToWindow / TickPinned / ClearPin 连同 Coordinator 的接线一并移除。

        /// <summary>
        /// 空心矩形预览（AltSnap 的 TransWin 同款）：
        /// 用区域环画一个方形描边框，精确贴住目标矩形，每帧同步跟随光标。
        /// 环是自己的窗口 —— SetRectNow 同步直写、目标程序再忙也拖不慢它。
        /// 运行在拖动工作线程上（第四十八轮：同 SetRectNow 的线程纪律）。
        /// </summary>
        private void ShowOutline(Native.RECT target, Session s, AppConfig c)
        {
            if (target.Width < 12 || target.Height < 12) return;

            // ★ 第四十轮：空心矩形预览也用【同一段绘制代码】★
            //   以前这里固定用 Flat + 强调色 + 4px，和设置里选的样式毫无关系 ——
            //   用户会觉得"我选的样式在拖动时完全没体现"。
            //   现在改成按配置绘制：预览框和最终外框长得一模一样，
            //   只是它贴的是"将要落到的位置"而不是窗口本身。
            bool inset;
            var f = FrameRectOf(target, c, out inset);
            _frame.RaiseAboveAll = inset;
            if (f.Width < 4 || f.Height < 4) return;

            _frame.ApplyTheme(c.FrameColorId, c.FrameStyleId, c.FrameThickness, c.FrameRadius, c.FrameOpacity);

            lock (_gate)
            {
                if (_dragging && _session.Gen == s.Gen)
                {
                    _frame.SetRectNow(f);
                }
            }
        }

    }
}
