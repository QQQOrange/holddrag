using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading;

namespace HoldDrag
{
    /// <summary>
    /// 浮层窗口基类 —— 自建原生窗口，【刻意不用 WinForms 的 Form】。
    ///
    /// 为什么必须换掉原来那套 Form + Opacity + SetBounds + Refresh：
    ///   · Form.Opacity 走 SetLayeredWindowAttributes，改位置/尺寸要靠 SetBounds()，
    ///     它内部是 SetWindowPos + Invalidate + Update，也就是【先移动、再重画】。
    ///     这两步之间被合成器抓到，屏幕上就会出现"窗口已经到新位置、像素还留在旧位置"
    ///     —— 用户看到的拖动残影就是这么来的。
    ///   · 自建窗口用 UpdateLayeredWindow 一次提交"位置 + 尺寸 + 像素"，
    ///     三者是原子的，中间不存在任何可被观察到的状态。
    ///
    /// 跨线程更新走 PostMessage 合并：工作线程每帧都可能要动浮层，
    /// 但只置一个"有更新"标记再投递一次消息，UI 线程取到的永远是最新矩形。
    /// 这样既不会淹掉 UI 线程的消息队列，也不需要在热路径上分配委托。
    /// </summary>
    internal abstract class OverlayWindow : IDisposable
    {
        private const uint WM_APPLY = Native.WM_APP + 1;

        /// <summary>
        /// "外观参数变了，请用当前尺寸重画并重推一次"。走独立消息而不是复用 WM_APPLY，
        /// 因为它必须在【不改变外框位置】的前提下重提交像素。
        /// </summary>
        private const uint WM_RETHEME = Native.WM_APP + 2;

        // ── 窗口类只需注册一次，全进程共用 ──
        private static readonly object ClassGate = new object();
        private static string _className;
        private static Native.WndProc _classProc;          // 必须静态存活，否则非托管侧留野指针
        private static readonly Dictionary<IntPtr, OverlayWindow> Instances =
            new Dictionary<IntPtr, OverlayWindow>();

        private IntPtr _hwnd = IntPtr.Zero;
        private bool _destroyed;

        // ── 待应用的矩形（合并用）──
        private readonly object _pendingGate = new object();
        private Native.RECT _pending;
        private bool _pendingValid;

        /// <summary>
        /// 已请求隐藏、但 UI 线程还没落地。
        ///
        /// ★ 为什么必须有这个标记 ★
        ///   HideOverlay() 只是投递 WM_APPLY，真正的 HideNow() 要等 UI 线程泵消息才跑。
        ///   在那之前 _visible 仍是 true、_lastApplied 仍是上一帧的矩形 —— 于是紧随其后的
        ///   SetRect(同一个矩形) 会被"这个矩形已经在显示了"的短路吃掉，什么都不投递；
        ///   然后那条挂起的隐藏消息落地，浮层就【再也没有人把它显示回来】。
        ///
        ///   触发路径真实存在：#38 里"再按一次触发键"＝ClearPin(隐藏) 之后立刻
        ///   ShowHoverFrame(同一扇窗、同一矩形)，用户看到的就是"第二次长按外框反而没了"。
        ///   有了这个标记，只要隐藏还挂着，SetRect 就绝不走短路。
        /// </summary>
        private bool _hidePending;
        private bool _applyPosted;

        // 外观重画请求也走合并：连续改四个参数（颜色/样式/粗细/圆角）只重画一次。
        private volatile bool _rethemePending;
        private bool _rethemePosted;

        private bool _visible;

        /// <summary>浮层当前是否显示着。子类判断"该不该重推像素"时要用。</summary>
        protected bool IsVisible { get { return _visible; } }

        protected OverlayWindow()
        {
        }

        /// <summary>窗口句柄。仅供自检断言使用。</summary>
        public IntPtr Handle { get { return _hwnd; } }

        public bool IsCreated { get { return _hwnd != IntPtr.Zero; } }

        // ── 生命周期 ────────────────────────────────────────────

        /// <summary>
        /// 创建窗口时是否带 WS_EX_LAYERED。
        ///
        /// ★★★ 这个开关是"拖动中看不到外框"那个 bug 的修复点 ★★★
        ///
        ///   位图型浮层（ActivationFrame）必须带 —— 它靠 UpdateLayeredWindow
        ///   一次提交"位置 + 像素 + alpha"，抗锯齿和柔光全靠它。
        ///
        ///   区域型浮层（DragRingFrame）必须【不带】—— 分层窗口的形状是由
        ///   alpha 通道决定的，SetWindowRgn 抠出来的区域【会被完全忽略】：
        ///   窗口既不会被裁成环，也没有任何像素被提交（我们从没调过
        ///   UpdateLayeredWindow），于是整块窗口【完全不可见】。
        ///   而 IsWindowVisible() 依然返回 true（那只是 WS_VISIBLE 样式位），
        ///   所以只断言"窗口可见"的用例永远抓不到它 —— 直到本轮的截屏用例
        ///   （T57）在拖动中数到 0 个外框像素才把它揪出来。
        /// </summary>
        protected virtual bool LayeredStyle { get { return true; } }

        public void EnsureCreated()
        {
            if (_hwnd != IntPtr.Zero) return;

            StartOwnerThread();

            // 已经在浮层线程上（含线程启动过程中的兜底路径）：直接建
            if (Native.GetCurrentThreadId() == _ownerThreadId) { CreateNow(); return; }

            // 交给浮层线程创建，并等它完成。
            // 窗口创建是亚毫秒级的，而且只在启动 / 首次使用时发生，这点等待完全可以接受。
            // 为什么必须在"拥有它的线程"上创建：窗口过程会在创建线程上被调用，
            // 之后 DestroyWindow 也必须在同一线程上调用 —— 否则会失败并泄漏窗口。
            lock (OwnerGate) { _pendingCreate.Add(this); _created.Reset(); }
            if (!Native.PostThreadMessageW(_ownerThreadId, WM_CREATE_OVERLAYS, IntPtr.Zero, IntPtr.Zero))
            {
                // 投递失败（极少见）：退回在调用线程上创建，功能不受影响
                lock (OwnerGate) { _pendingCreate.Remove(this); }
                Log.Error("投递浮层创建消息失败，退回当前线程创建");
                CreateNow();
                return;
            }
            _created.Wait(3000);
        }

        /// <summary>真正创建窗口。必须运行在浮层线程上。</summary>
        private void CreateNow()
        {
            if (_hwnd != IntPtr.Zero) return;

            EnsureClass();

            // WS_EX_NOACTIVATE：绝不抢焦点
            // WS_EX_TOOLWINDOW：不进 Alt+Tab、不显示在任务栏
            // WS_EX_TOPMOST：始终压在最上面
            // WS_EX_LAYERED：位置和像素一次性提交（消除残影的关键）—— 仅位图型浮层需要
            // WS_EX_TRANSPARENT 刻意【不加】：它会影响绘制顺序，改用 WM_NCHITTEST 返回
            //   HTTRANSPARENT 来实现点击穿透，效果一样但没有绘制副作用。
            uint ex = (uint)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE
                           | Native.WS_EX_TOPMOST);
            if (LayeredStyle) ex |= (uint)Native.WS_EX_LAYERED;

            _hwnd = Native.CreateWindowExW(ex, _className, "HoldDrag Overlay",
                Native.WS_POPUP, -32000, -32000, 1, 1,
                IntPtr.Zero, IntPtr.Zero, Native.GetModuleHandleW(null), IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                Log.Error("创建浮层窗口失败，Win32 错误码 " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                return;
            }

            lock (ClassGate) { Instances[_hwnd] = this; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  浮层专用线程（第四十四轮新增）
        //
        //  ★ 为什么必须独立线程 ★
        //    用户报障："外框并非始终跟随窗口，拖动窗口时会出现瞬间脱离跟随
        //              （掉帧或错位）。"
        //
        //    根因之一：浮层窗口原来挂在【UI 线程】上。任何跨线程的位置更新
        //    都要等 UI 线程泵到消息才会真正执行 —— 而 UI 线程同时还要跑
        //    设置窗口、托盘菜单、WinForms 定时器、GC……它一忙，
        //    外框的位置更新就被压后，屏幕上就是"窗口走了、框还留在原地"。
        //    把浮层挪到一条【只泵消息、不做别的事】的线程上之后，
        //    这类延迟从"取决于 UI 线程有多忙"变成"恒定的亚毫秒级"。
        //
        //  创建与销毁都必须在这条线程上做（窗口过程 / DestroyWindow 的线程亲和性），
        //  所以用 PostThreadMessage + 完成事件把这两件事投递过来。
        // ══════════════════════════════════════════════════════════════════
        private const uint WM_CREATE_OVERLAYS = Native.WM_APP + 11;
        private const uint WM_DESTROY_OVERLAY = Native.WM_APP + 12;

        private static Thread _ownerThread;
        private static uint _ownerThreadId;
        private static readonly object OwnerGate = new object();
        private static readonly ManualResetEventSlim _ownerReady = new ManualResetEventSlim(false);
        private static readonly List<OverlayWindow> _pendingCreate = new List<OverlayWindow>();
        private static volatile bool _ownerQuit;

        /// <summary>本实例"窗口已创建完成"的信号（跨线程创建时用）。</summary>
        private readonly ManualResetEventSlim _created = new ManualResetEventSlim(false);

        /// <summary>启动浮层线程（幂等）。</summary>
        public static void StartOwnerThread()
        {
            lock (OwnerGate)
            {
                if (_ownerThread != null) return;
                _ownerThread = new Thread(OwnerThreadMain);
                _ownerThread.IsBackground = true;
                // 与拖动工作线程同级：外框位置更新是硬实时活儿，不能排在普通线程后面
                _ownerThread.Priority = ThreadPriority.AboveNormal;
                _ownerThread.Name = "HoldDrag.Overlays";
                _ownerThread.Start();
            }
            _ownerReady.Wait(3000);
        }

        /// <summary>浮层线程 ID（仅自检用）。</summary>
        internal static uint OwnerThreadIdForTest { get { return _ownerThreadId; } }

        private static void OwnerThreadMain()
        {
            try
            {
                _ownerThreadId = Native.GetCurrentThreadId();

                // 先 Peek 一次强制建立消息队列：PostThreadMessage 要求目标线程有队列，
                // 否则消息会被丢掉（这是本线程启动完成前必须做的事）。
                Native.MSG tmp;
                Native.PeekMessageW(out tmp, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE);
                _ownerReady.Set();

                while (!_ownerQuit)
                {
                    Native.MSG msg;
                    int r = Native.GetMessageW(out msg, IntPtr.Zero, 0, 0);
                    if (r <= 0) break;

                    if (msg.hwnd == IntPtr.Zero)
                    {
                        if (msg.message == WM_CREATE_OVERLAYS) { CreatePendingNow(); continue; }
                        if (msg.message == WM_DESTROY_OVERLAY)
                        {
                            try { Native.DestroyWindow(msg.wParam); }
                            catch (Exception ex) { Log.Error(ex); }
                            continue;
                        }
                        continue;
                    }

                    Native.TranslateMessage(ref msg);
                    Native.DispatchMessageW(ref msg);
                }
            }
            catch (Exception ex) { Log.Error(ex); }
            finally { _ownerReady.Set(); }
        }

        private static void CreatePendingNow()
        {
            OverlayWindow[] list;
            lock (OwnerGate)
            {
                list = _pendingCreate.ToArray();
                _pendingCreate.Clear();
            }
            for (int i = 0; i < list.Length; i++)
            {
                try { list[i].CreateNow(); }
                catch (Exception ex) { Log.Error(ex); }
                finally { list[i]._created.Set(); }
            }
        }

        private static void EnsureClass()
        {
            lock (ClassGate)
            {
                if (_className != null) return;

                _classProc = OverlayWndProc;
                var wc = new Native.WNDCLASSEX();
                wc.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WNDCLASSEX));
                wc.style = 0;
                wc.lpfnWndProc = _classProc;
                wc.hInstance = Native.GetModuleHandleW(null);
                wc.hCursor = IntPtr.Zero;                   // 不给光标，反正点击穿透
                wc.hbrBackground = IntPtr.Zero;             // 我们自己画，不要系统背景刷
                wc.lpszClassName = "HoldDragOverlayWindow";

                Native.RegisterClassExW(ref wc);
                // 同进程内重复注册会返回 0 + ERROR_CLASS_ALREADY_EXISTS，这种情况下
                // 继续用同名类创建窗口是安全的，所以这里不把 0 当致命错误。
                _className = wc.lpszClassName;
            }
        }

        public void Destroy()
        {
            if (_destroyed) return;
            _destroyed = true;

            IntPtr h = _hwnd;
            _hwnd = IntPtr.Zero;

            if (h != IntPtr.Zero)
            {
                lock (ClassGate) { Instances.Remove(h); }

                // ★ DestroyWindow 必须在【创建它的线程】上调用 ★
                //   跨线程调用会失败（返回 0），窗口就永久泄漏 —— 一个隐形但
                //   实实在在的顶层窗口，还会挡住命中测试。所以这里投递到浮层线程。
                if (Native.GetCurrentThreadId() == _ownerThreadId)
                {
                    try { Native.DestroyWindow(h); } catch (Exception ex) { Log.Error(ex); }
                }
                else
                {
                    try { Native.PostThreadMessageW(_ownerThreadId, WM_DESTROY_OVERLAY, h, IntPtr.Zero); }
                    catch (Exception ex) { Log.Error(ex); }
                }
            }

            try { FreeResources(); } catch (Exception ex) { Log.Error(ex); }
        }

        public void Dispose()
        {
            Destroy();
            GC.SuppressFinalize(this);
        }

        /// <summary>子类释放自己持有的 GDI 资源（位图 / 内存 DC）。</summary>
        protected virtual void FreeResources() { }

        /// <summary>
        /// WM_PAINT 时子类自己画内容。返回 true 表示已画完并已证实无效区。
        /// 默认实现什么都不画（分层浮层的像素走 UpdateLayeredWindow，不走 WM_PAINT）。
        /// </summary>
        protected virtual bool HandlePaint() { return false; }

        // ── 跨线程：设置矩形 ────────────────────────────────────

        /// <summary>
        /// 请求把浮层摆到屏幕矩形 r。任意线程可调用。
        ///
        /// 只保留最后一个请求：工作线程一秒可能投递上百次，每次一个消息会把 UI 线程淹掉。
        /// 这里只置标记 + 投递一次 WM_APPLY，UI 线程处理时取到的就是最新的那个矩形。
        ///
        /// 再加一道"和上次投递的矩形完全相同就直接返回"：拖动刚开始的那几毫秒、
        /// 以及悬停时窗口一动不动的情形，都会重复送同一个矩形过来。
        /// 一条 PostMessage 虽然便宜，但 UI 线程要为此醒来跑一次 DrainApply + Push ——
        /// 安静时每次唤醒都是纯粹的浪费，而键盘 / 鼠标钩子运行时又特别在意这种小抖动。
        /// </summary>
        public void SetRect(Native.RECT r)
        {
            if (_destroyed || _hwnd == IntPtr.Zero) return;
            if (r.Width <= 0 || r.Height <= 0) { HideOverlay(); return; }

            bool needPost;
            lock (_pendingGate)
            {
                // 与"待处理"或"已落地"的矩形一致，就没事可做。
                if (_pendingValid && SameRect(_pending, r)) return;
                // ★ 隐藏还挂着的时候绝不能短路（否则隐藏落地后没人再显示，见 _hidePending 注释）。
                if (!_pendingValid && !_hidePending && _lastAppliedValid && SameRect(_lastApplied, r) && _visible) return;

                _pending = r;
                _pendingValid = true;
                _hidePending = false;
                needPost = !_applyPosted;
                if (needPost) _applyPosted = true;
            }

            if (needPost && !Native.PostMessageW(_hwnd, unchecked((int)WM_APPLY), IntPtr.Zero, IntPtr.Zero))
            {
                lock (_pendingGate) { _applyPosted = false; }
            }
        }

        private Native.RECT _lastApplied;
        private bool _lastAppliedValid;

        private static bool SameRect(Native.RECT a, Native.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        /// <summary>请求隐藏浮层。任意线程可调用。</summary>
        public void HideOverlay()
        {
            if (_destroyed || _hwnd == IntPtr.Zero) return;

            bool needPost;
            lock (_pendingGate)
            {
                _pendingValid = false;
                _hidePending = true;          // 见 _hidePending 注释：挡住紧随其后的 SetRect 短路
                needPost = !_applyPosted;
                if (needPost) _applyPosted = true;
            }

            if (needPost && !Native.PostMessageW(_hwnd, unchecked((int)WM_APPLY), IntPtr.Zero, IntPtr.Zero))
            {
                lock (_pendingGate) { _applyPosted = false; }
            }
        }

        /// <summary>
        /// 请求"外观变了，按当前尺寸重画并原地重推一次"。任意线程可调用。
        ///
        /// 与 SetRect 的区别：这里完全不碰位置和尺寸，只重提交像素。
        /// 所以"改主题"不会让外框跳位置，也不会被"窗口没动就跳过"之类的短路逻辑吃掉。
        /// 合并策略与 SetRect 相同：只保留一个待处理请求。
        /// </summary>
        protected void RequestRetheme()
        {
            if (_destroyed || _hwnd == IntPtr.Zero) return;

            _rethemePending = true;

            bool needPost;
            lock (_pendingGate)
            {
                needPost = !_rethemePosted;
                if (needPost) _rethemePosted = true;
            }

            if (needPost && !Native.PostMessageW(_hwnd, unchecked((int)WM_RETHEME), IntPtr.Zero, IntPtr.Zero))
            {
                lock (_pendingGate)
                {
                    _rethemePosted = false;
                    _rethemePending = false;
                }
            }
        }

        /// <summary>UI 线程：把挂起的外观重画落地。</summary>
        private void DrainRetheme()
        {
            if (_destroyed || _hwnd == IntPtr.Zero) return;

            lock (_pendingGate) { _rethemePosted = false; }
            if (!_rethemePending) return;
            _rethemePending = false;

            try { OnRetheme(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>子类在这里按当前外观重画并重推像素。默认什么都不做。</summary>
        protected virtual void OnRetheme() { }

        // ── 窗口过程 ────────────────────────────────────────────

        private static IntPtr OverlayWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (msg == Native.WM_NCHITTEST) return new IntPtr(Native.HTTRANSPARENT);
                if (msg == Native.WM_MOUSEACTIVATE) return new IntPtr(Native.MA_NOACTIVATE);
                if (msg == Native.WM_ERASEBKGND) return new IntPtr(1);   // 自己全画，别擦背景（防闪）

                OverlayWindow self;
                lock (ClassGate)
                {
                    if (!Instances.TryGetValue(hwnd, out self)) self = null;
                }

                if (self != null)
                {
                    if (msg == WM_APPLY) { self.DrainApply(); return IntPtr.Zero; }
                    if (msg == WM_RETHEME) { self.DrainRetheme(); return IntPtr.Zero; }
                    if (msg == Native.WM_DESTROY) return IntPtr.Zero;
                }

                // ★ 分层窗口不走系统绘制，但万一收到 WM_PAINT 也必须把无效区证实掉。
                //   不证实的话系统会无限重发 WM_PAINT，宿主消息循环会陷进去出不来
                //   （这个坑真的踩过，卡了很久）。
                //   需要自己画内容的子类（DragRingFrame）可以重写 HandlePaint()。
                if (msg == Native.WM_PAINT)
                {
                    bool handled = false;
                    if (self != null)
                    {
                        try { handled = self.HandlePaint(); }
                        catch (Exception ex) { Log.Error(ex); }
                    }
                    if (!handled)
                    {
                        try { Native.ValidateRect(hwnd, IntPtr.Zero); } catch { }
                    }
                    return IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                // 绝不能让异常逃逸回非托管调用方
                Log.Error(ex);
            }

            return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        /// <summary>
        /// 同步直写路径的"落地记账"：把可见状态与最后矩形直接置位，不经过消息队列。
        ///
        /// ★ 第四十轮新增（拖动期改用分层框）★
        ///   异步管线（SetRect / HideOverlay）靠 _visible / _lastApplied 做
        ///   "这个矩形已经在显示了"的短路判断。直写路径不记账的话，两边状态会打架：
        ///   直写刚把框画出来，UI 线程随后一次 DrainApply 可能因为"看不见"而重推，
        ///   或者反过来把直写的框判成已隐藏。这里一次性把账记平。
        /// </summary>
        protected void MarkDirectWrite(Native.RECT r)
        {
            lock (_pendingGate)
            {
                _lastApplied = r;
                _lastAppliedValid = true;
                // ★ 关键：待处理状态要记成"显示这个矩形"，而不是"无待处理" ★
                //   直写之前往往刚调过 HideOverlay()（比如拖动开始时先收悬停框），
                //   那条 WM_APPLY 还在队列里。如果这里把 _pendingValid 留成 false，
                //   排到队的那次 DrainApply 就会执行 HideNow()，
                //   把我们刚直写出来的框又藏掉 —— 表现就是"拖动一开始框闪一下就不见了"。
                _pending = r;
                _pendingValid = true;
                _hidePending = false;
            }
            _visible = true;
        }

        /// <summary>
        /// 同步隐藏路径的记账：把"已隐藏"直接置位，并清掉队列里可能残留的显示请求。
        /// 与 MarkDirectWrite 配对使用（拖动收尾时"松手即消失"要求同步落地）。
        /// </summary>
        protected void MarkDirectHide()
        {
            lock (_pendingGate)
            {
                _pendingValid = false;
                _hidePending = false;
                _lastAppliedValid = false;
            }
            _visible = false;
        }

        /// <summary>
        /// 仅供自检 / 日志诊断：异步管线上是否已投递"显示某矩形"的请求。
        ///
        /// 第四十八轮：钩子线程的显框改走异步管线后，刚投递完的那一刻
        /// IsShown 还是 false（要等浮层线程排到 WM_APPLY）—— 直接打印
        /// IsShown 会每次都误报"没有外框"。这个探针把"请求已投递"
        /// 与"尚未投递"分开，日志才能继续用来回答"触发了却没有外框"。
        ///
        /// 只读两个布尔，临界区极短、里面没有任何窗口调用，
        /// 因此在钩子线程上调用也是安全的。
        /// </summary>
        internal bool ShowRequestedForTest
        {
            get { lock (_pendingGate) { return _pendingValid && !_hidePending; } }
        }

        /// <summary>UI 线程：把挂起的请求落地。</summary>
        private void DrainApply()
        {
            if (_destroyed || _hwnd == IntPtr.Zero) return;

            Native.RECT r;
            bool show;
            lock (_pendingGate)
            {
                _applyPosted = false;
                show = _pendingValid;
                r = _pending;
                _pendingValid = false;
                _hidePending = false;
            }

            try
            {
                if (!show) { HideNow(); return; }
                ApplyNow(r);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        protected void HideNow()
        {
            lock (_pendingGate) { _lastAppliedValid = false; }

            // ★★★ 不能再用 `if (!_visible) return;` 把自己挡住 ★★★
            //
            //  _visible 只是我们自己记的一个影子状态，它会和【窗口的真实状态】脱节：
            //  MarkDirectWrite / ApplyNow 都会无条件把 _visible 置 true，
            //  于是完全可能出现"_visible=true 而窗口其实是隐藏的"。
            //  一旦脱节，这里提前返回就【再也藏不掉】，而下面 SetRectNow/ApplyNow
            //  又会因为"_visible 已经是 true"而跳过 ShowWindow —— 两边一起把窗口
            //  卡死在隐藏态：标志位说在显示，屏幕上一个像素都没有。
            //  （用户报的"全屏转窗口没有外框"就是这么来的，实测
            //    IsShown=True 而 IsWindowVisible=False。）
            //
            //  修法：不拿影子状态当闸门。ShowWindow(SW_HIDE) 本身是幂等的，
            //  重复调用没有副作用，代价是一次极廉价的 win32k 调用。
            _visible = false;
            try
            {
                if (_hwnd != IntPtr.Zero) Native.ShowWindow(_hwnd, Native.SW_HIDE);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void ApplyNow(Native.RECT r)
        {
            lock (_pendingGate)
            {
                _lastApplied = r;
                _lastAppliedValid = true;
            }

            bool sizeChanged = r.Width != _lastW || r.Height != _lastH;

            if (sizeChanged)
            {
                OnSizeChanged(r.Width, r.Height);
                _lastW = r.Width;
                _lastH = r.Height;
            }

            // 分层窗口必须先显示再提交像素：UpdateLayeredWindow 在窗口未显示时会失败。
            // ★ 无条件调用（ShowWindow 幂等）★ —— 见 HideNow 的注释：
            //   只要用影子状态当闸门，就有机会把窗口永久卡在隐藏态。
            _visible = true;
            try { Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE); }
            catch (Exception ex) { Log.Error(ex); }

            OnPlace(r);
        }

        private int _lastW = -1;
        private int _lastH = -1;

        /// <summary>尺寸变化时重建位图。纯移动不会走到这里 —— 热路径上零分配。</summary>
        protected abstract void OnSizeChanged(int w, int h);

        /// <summary>把窗口摆到目标位置（位图已经在 OnSizeChanged 里渲染好）。</summary>
        protected abstract void OnPlace(Native.RECT r);

        // ── 子类公用的小工具 ────────────────────────────────────

        protected static void MoveTopmost(IntPtr hwnd, int x, int y, int w, int h)
        {
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }
    }

    /// <summary>
    /// 位图型分层浮层的公共实现：缓存「托管 Bitmap + GDI 位图句柄 + 内存 DC」三件套。
    ///
    /// 为什么要把这三样都缓存住：早期写法每帧调一次 GetHbitmap()，
    /// 也就是每帧新建一个 GDI 位图对象再删掉。虽然测出来不泄漏，但拖动时每秒几百次的
    /// GDI 对象创建/销毁既浪费，又容易在系统忙时被拖慢。缓存之后，
    /// 一次拖动的稳态是【零 GDI 分配、零托管分配】——只有尺寸变化时才重建。
    /// </summary>
    internal abstract class LayeredBitmapOverlay : OverlayWindow
    {
        private Bitmap _bmp;
        private IntPtr _hBitmap = IntPtr.Zero;
        private IntPtr _memDc = IntPtr.Zero;
        private IntPtr _oldBmp = IntPtr.Zero;
        private int _bw, _bh;
        private int _curW, _curH;

        /// <summary>
        /// 位图三件套的互斥锁。
        ///
        /// ★ 第四十轮新增：拖动期改走【同步直写】之后，Rebuild/Push 会被
        ///   拖动工作线程调用，而 UI 线程的 OnRetheme（改主题时）也会调 ——
        ///   两边同时重建位图就会一个把另一个的 _hBitmap 删掉（泄漏 + 画错）。
        ///   所有碰 _bmp/_hBitmap/_memDc 的地方都在这把锁里。
        /// </summary>
        private readonly object _bufGate = new object();

        /// <summary>
        /// 正在 _bufGate 之外执行窗口调用（UpdateLayeredWindow / SetWindowPos）
        /// 的 Push 次数。第四十八轮新增。
        ///
        /// Push 的窗口调用必须放在锁外（否则与浮层线程互等死锁，见 Push 注释），
        /// 代价是"位图可能被并发 Rebuild 换掉 / DC 可能被并发回收"。
        /// 用这个计数让 FreeResources 能等在飞推送落地后再动手，
        /// 把安全性补回来。
        /// </summary>
        private int _pushInFlight;

        /// <summary>
        /// 整层统一不透明度（0~255）。由 UpdateLayeredWindow 的 SourceConstantAlpha 生效。
        /// 默认 255（完全不透明）。
        /// </summary>
        protected int _constantAlpha = 255;

        /// <summary>
        /// 是否要把浮层【抬到所有顶层窗口之上】。内缩绘制时置位。
        ///
        /// ★ 为什么内缩模式必须抬 ★
        ///   用户报障："只有本来就是窗口的才有外框，从全屏转过来的窗口没有外框。"
        ///   内缩模式下，那圈框是画在【窗口自己的边界之内】的 ——
        ///   而全屏应用/播放器常常自己也是 topmost，并且在我们之后被激活，
        ///   于是它压在浮层上面，把画在它里面的框整条盖住 → "没有外框"。
        ///   外扩模式不存在这个问题：框在窗口外面，窗口盖不到它 ——
        ///   这也正好解释了"窗口形式的有框、全屏转过来的没框"这个差异。
        ///   所以内缩时把浮层抬到最顶，保证框画得出来。
        ///
        /// 第四十七轮修正：**默认就是 true，别再关掉**。
        ///   之前只在"内缩模式"抬，理由是"外扩时框在窗口外面，窗口盖不到"。
        ///   但那只对【普通窗口】成立 —— 目标窗口如果自己是 topmost
        ///   （全屏应用/播放器/游戏很常见），它依然会盖住我们已经处于 topmost 层的浮层，
        ///   而"外框总是不见"正是用户反复反馈的现象。
        ///   抬一次的代价是一次 SetWindowPos（微秒级），
        ///   换来的是"框在任何情况下都在最上面"，这个交换非常划算。
        /// </summary>
        public bool RaiseAboveAll = true;   // 默认就抬：外框"看得见"优先于任何 z 序讲究

        /// <summary>把内容画进 g。已按尺寸清成全透明，直接画即可。</summary>
        protected abstract void RenderContent(Graphics g, int w, int h);

        /// <summary>
        /// ★ 同步直写：把浮层摆到 r，需要时重建位图。任意线程可调用。★
        ///
        /// 这是"拖动过程中的视觉反馈"能够既【和设置里选的样式完全一致】、
        /// 又【跟得上窗口】的关键：
        ///   · 拖动中每帧由拖动工作线程调用，和写目标窗口同一拍 ——
        ///     不走 PostMessage，所以不存在"窗口走了、框还在后面追"的残影；
        ///   · 纯移动（尺寸没变，最常见）只做一次 SetWindowPos，位图复用，零重建；
        ///   · 只有缩放时尺寸真的变了才重建位图 + 重推像素。
        ///
        /// 与异步管线（SetRect → WM_APPLY）并存：那条路留给"静止/悬停"状态，
        /// 两者靠 MarkDirectWrite / _pendingGate 保持状态一致。
        ///
        /// ★★★ 第四十八轮：_bufGate 里绝不放跨线程窗口调用 ★★★
        ///   SetWindowPos 对【别的线程拥有的窗口】会同步发 WM_WINDOWPOSCHANGED
        ///   并等对方泵掉（SWP_NOSENDCHANGING 只抑制 WM_WINDOWPOSCHANGING，
        ///   CHANGED 照发不误）。一旦持着 _bufGate 做这件事，而浮层线程此刻
        ///   正卡在同一把锁上（它自己的 Push 要走 DrainApply→ApplyNow→OnPlace），
        ///   双方互等 = 死锁；更糟的是调用方若是钩子线程，整个系统的鼠标
        ///   输入管线都会冻结（用户报障"电脑死机、只能强制重启"的根因）。
        ///   所以：锁内只碰位图三件套，窗口调用一律在锁外执行。
        /// </summary>
        public void SetRectNow(Native.RECT r)
        {
            if (!IsCreated) return;
            if (r.Width < 4 || r.Height < 4) { HideNow(); return; }

            bool pushPixels;
            bool wasVisible = IsVisible;

            // 锁内：只做尺寸判定 + 位图重建 + 取"要不要重推像素"的结论。
            lock (_bufGate)
            {
                bool sizeChanged = (r.Width != _curW || r.Height != _curH);
                if (sizeChanged) Rebuild(r.Width, r.Height);
                pushPixels = (sizeChanged || !wasVisible)
                             && _memDc != IntPtr.Zero && _hBitmap != IntPtr.Zero;
            }

            MarkDirectWrite(r);

            // ★ 无条件显示（幂等）★：wasVisible 读的是影子状态，可能已经不准了。
            //   原来这里是 `if (!wasVisible)`，一旦影子状态说"已经可见"而真实是隐藏的，
            //   就再也没人把它显示出来 —— 外框从此不可见。
            try { Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE); } catch { }

            if (pushPixels)
            {
                Push(r.Left, r.Top);
                return;
            }

            // ★★★ 纯移动：必须【同步】写，而且不能省 ★★★
            //
            //  用户报障："外框并非始终跟随窗口，拖动窗口时会出现瞬间脱离跟随。"
            //
            //  这里原来用的是 SWP_ASYNCWINDOWPOS —— 请求只是被投进浮层所在线程的
            //  消息队列，什么时候执行取决于那个线程什么时候泵到这条消息。
            //  正常情况下延迟很小，可一旦 UI 线程忙起来（GC、重绘、托盘菜单、
            //  设置窗口），这条请求就被压后几毫秒到几十毫秒 ——
            //  屏幕上就是"窗口已经走了、框还留在原地"，也就是用户看到的"瞬间脱离"。
            //
            //  改成同步之后语义变得确定：本调用【返回时框一定已经在目标位置】。
            //  配合 DragEngine 里"外框先动、窗口后动"的顺序，任何时刻都不可能
            //  出现"窗口跑出框"。
            //
            //  为什么同步不会拖慢拖动：
            //    · 这是【我们自己的】窗口，SetWindowPos 只走它自己的窗口过程，
            //      不存在"等目标应用重绘"那种长阻塞；
            //    · 带 SWP_NOSENDCHANGING，不向任何别的线程发同步消息；
            //    · 实测 dragbench 写入率 94~95%、有效更新率 118Hz 档位不变。
            //
            // ★ 第四十八轮修正：最后一条理由要说清楚 ★
            //   SWP_NOSENDCHANGING 只抑制 WM_WINDOWPOSCHANGING，WM_WINDOWPOSCHANGED
            //   仍然会同步发给拥有浮层窗口的那条线程 —— 这就是死锁的等待对象。
            //   本调用现在【不在 _bufGate 内】执行：浮层线程最多被位图工作
            //   （Rebuild，微秒~毫秒级、有界）挡住一小会儿，不存在互等。
            // 内缩模式要同时把浮层抬到最顶（见 RaiseAboveAll 的注释）：
            // 抬的时候不能带 SWP_NOZORDER，且 hWndInsertAfter 用 HWND_TOPMOST。
            uint flags = Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER
                       | Native.SWP_NOSENDCHANGING;
            IntPtr after = IntPtr.Zero;
            if (RaiseAboveAll) after = new IntPtr(-1);      // HWND_TOPMOST
            else flags |= Native.SWP_NOZORDER;
            Native.SetWindowPos(Handle, after, r.Left, r.Top, r.Width, r.Height, flags);
        }

        /// <summary>
        /// 同步隐藏：立刻把框收掉，不等消息队列。
        ///
        /// ★ 拖动收尾专用 ★
        ///   用户要求"松开按键后外框立即消失"。异步的 HideOverlay 虽然也很快，
        ///   但要走一次消息泵；而拖动收尾发生在钩子线程上，用同步版本可以保证
        ///   "松手那一刻"屏幕上就没有框了，不依赖任何后续泵送。
        /// </summary>
        public void HideNowSync()
        {
            if (!IsCreated) return;
            HideNow();          // 真正 ShowWindow(SW_HIDE)
            MarkDirectHide();   // 记平账，防止队列里残留的显示请求又把它弄回来
        }

        protected override void OnSizeChanged(int w, int h)
        {
            if (Handle == IntPtr.Zero || w <= 0 || h <= 0) return;
            lock (_bufGate)
            {
                _curW = w; _curH = h;
                Rebuild(w, h);
            }
        }

        /// <summary>
        /// 外观参数变了但尺寸没变时调用：记下"需要重画"，交给 UI 线程按当前尺寸重画并原地重推。
        ///
        /// ★ 为什么必须"重推"：UpdateLayeredWindow 才是真正把像素交给合成器的那一步。
        ///   v3.2 之前的写法只重建位图、不重推，新像素就静静躺在内存里，
        ///   要等到下一次 SetRect（鼠标移开、光标换到另一扇窗）才被画出来 ——
        ///   用户看到的就是"在设置里改了颜色/样式，保存后外框完全没变"。
        ///
        /// ★ 为什么必须回 UI 线程：Rebuild 会创建 GDI 位图、Push 会调 UpdateLayeredWindow，
        ///   而调用方（ApplyTheme）在钩子线程上。走 WM_RETHEME 投递回创建浮层的
        ///   那个线程执行，和 SetRect 的管线保持同一套纪律。任意线程可调用。
        /// </summary>
        protected void MarkContentDirty()
        {
            RequestRetheme();
        }

        private int _lastPlacedX;
        private int _lastPlacedY;
        private bool _lastPlacedValid;

        /// <summary>UI 线程：按当前尺寸用新外观重画，并在上次落地的位置原地重推。</summary>
        protected override void OnRetheme()
        {
            if (_curW <= 0 || _curH <= 0) return;
            Rebuild(_curW, _curH);

            // 还没显示过就不用推：第一次 ApplyNow 会用新外观渲染。
            if (!_lastPlacedValid) return;
            if (_memDc == IntPtr.Zero || _hBitmap == IntPtr.Zero) return;
            if (!IsVisible) return;
            Push(_lastPlacedX, _lastPlacedY);
        }

        /// <summary>
        /// 仅供自检：直接读浮层【自己那张位图】里的一个像素。
        ///
        /// 用来把两种"屏幕上看不见"彻底分开：
        ///   · 位图里是透明 → 是【没画上去】（渲染这一环的问题）；
        ///   · 位图里是实色 → 是【画了但没合成上屏】（z 序/ULW 这一环的问题）。
        /// 只盯着屏幕猜，这两者永远分不清。
        /// </summary>
        internal string ProbeBitmapPixelForTest(int x, int y)
        {
            try
            {
                lock (_bufGate)
                {
                    if (_bmp == null) return "无位图";
                    if (x < 0 || y < 0 || x >= _bmp.Width || y >= _bmp.Height) return "越界";
                    var c = _bmp.GetPixel(x, y);
                    return c.R + "," + c.G + "," + c.B + ",a=" + c.A;
                }
            }
            catch (Exception ex) { return "异常 " + ex.GetType().Name; }
        }

        /// <summary>
        /// 仅供自检：把"这层浮层到底处于什么状态"全部列出来。
        /// 排查"IsShown 是 true、屏幕上一个像素都没有"时，
        /// 只差这一组值就能定位：究竟是位图没建好、还是整层透明度是 0。
        /// </summary>
        internal string ProbeOverlayStateForTest()
        {
            return "alpha=" + _constantAlpha
                 + " 位图=" + _bw + "x" + _bh
                 + " cur=" + _curW + "x" + _curH
                 + " memDc=" + (_memDc != IntPtr.Zero)
                 + " hBitmap=" + (_hBitmap != IntPtr.Zero)
                 + " visible=" + IsVisible
                 + " placed=" + (_lastPlacedValid ? _lastPlacedX + "," + _lastPlacedY : "无")
                 + " 抬顶=" + RaiseAboveAll;
        }

        private void Rebuild(int w, int h)
        {
            lock (_bufGate)
            {
                ReleaseBitmap();

                var buf = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                try
                {
                    using (var g = Graphics.FromImage(buf))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.Clear(Color.Transparent);
                        RenderContent(g, w, h);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    buf.Dispose();
                    return;
                }

                _bmp = buf;
                _bw = w; _bh = h;
                // ★ 必须同时更新 _curW/_curH ★
                //   SetRectNow 用它们判断"尺寸变了没有"。忘了更新的话，
                //   每一帧都会判成"尺寸变了" → 每帧重建整张位图 + 重新上传 →
                //   拖动帧率从 121Hz 掉到 100Hz（dragbench 实测）。
                //   纯移动是绝大多数场景，它必须是【零重建】的。
                _curW = w; _curH = h;

                if (_memDc == IntPtr.Zero)
                {
                    IntPtr screen = Native.GetDC(IntPtr.Zero);
                    try { _memDc = Native.CreateCompatibleDC(screen); }
                    finally { if (screen != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screen); }
                }
                if (_memDc == IntPtr.Zero) return;

                IntPtr hb = IntPtr.Zero;
                try { hb = buf.GetHbitmap(Color.FromArgb(0)); }
                catch (Exception ex) { Log.Error(ex); return; }
                if (hb == IntPtr.Zero) return;

                _hBitmap = hb;
                _oldBmp = Native.SelectObject(_memDc, hb);
            }
        }

        protected override void OnPlace(Native.RECT r)
        {
            _lastPlacedX = r.Left;
            _lastPlacedY = r.Top;
            _lastPlacedValid = true;

            if (_memDc == IntPtr.Zero || _hBitmap == IntPtr.Zero) return;
            Push(r.Left, r.Top);
        }

        /// <summary>
        /// 把当前位图推上屏（UpdateLayeredWindow），并按需把浮层抬到最顶。
        ///
        /// ★★★ 第四十八轮：窗口调用全部在 _bufGate 之外执行 ★★★
        ///
        ///   【原来的写法】ULW 和抬顶 SetWindowPos 都在锁里。这酝酿了一个
        ///   双向死锁：
        ///     · 本方法可能运行在拖动工作线程 / 钩子线程上。SetWindowPos 对
        ///       【别的线程拥有的窗口】会同步发 WM_WINDOWPOSCHANGED 并等
        ///       浮层线程泵掉（SWP_NOSENDCHANGING 只挡 CHANGING，不挡 CHANGED）；
        ///     · 浮层线程同时可能在跑 DrainApply→ApplyNow→OnPlace→Push，
        ///       卡在 `lock (_bufGate)` 上 —— 而锁被上面那位持有者攥着。
        ///     双方互等，线程各自挂死。若持有者是钩子线程，阻塞发生在
        ///   WH_MOUSE_LL 回调里 → 系统鼠标输入整体停摆（用户报障 1：
        ///   "整个电脑死机、只能强制重启"，自检 T81 两次稳定复现，
        ///   末条语句级标记定格在持锁的 SetWindowPos 上，坐实）。
        ///
        ///   【现在的纪律】锁内只取快照（memDc / 尺寸）+ 在飞计数；
        ///   ULW、SetWindowPos 一律锁外执行。于是没有任何线程
        ///   "持锁等待别的线程"，互等环从结构上被消除。
        ///
        ///   锁外执行的安全性由两件事兜住：
        ///     · Rebuild 永远先 SelectObject 新位图再删旧位图 —— _memDc 任何
        ///       时刻都指向一张有效位图，在飞的 ULW 最坏读到一个混合帧，
        ///       随后一次 Push 即修正（无崩溃、无泄漏）；
        ///     · _pushInFlight 让 FreeResources 等在飞推送落地后再回收 DC，
        ///       不会把正在被 ULW 读取的 DC 删掉。
        /// </summary>
        private void Push(int x, int y)
        {
            // 锁内：校验 + 取快照 + 在飞计数。绝不放窗口调用。
            IntPtr memDc;
            int bw, bh;
            lock (_bufGate)
            {
                if (_memDc == IntPtr.Zero || _hBitmap == IntPtr.Zero) return;
                memDc = _memDc;
                bw = _bw;
                bh = _bh;
                _pushInFlight++;
            }

            try
            {
                IntPtr screenDc = Native.GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) return;

                try
                {
                    var dst = new Native.POINT(x, y);
                    var size = new Native.SIZE(bw, bh);
                    var src = new Native.POINT(0, 0);
                    var blend = new Native.BLENDFUNCTION();
                    blend.BlendOp = Native.AC_SRC_OVER;
                    // ★ 第四十五轮：外框不透明度 ★
                    //   UpdateLayeredWindow 的 SourceConstantAlpha 就是"整层统一乘一个 alpha"，
                    //   正是我们要的"外框透明度"语义 —— 且是合成器直接做，
                    //   不需要重画位图、不需要逐像素改 alpha，零额外开销。
                    blend.SourceConstantAlpha = (byte)_constantAlpha;
                    blend.AlphaFormat = Native.AC_SRC_ALPHA;

                    if (!Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size,
                            memDc, ref src, 0, ref blend, Native.ULW_ALPHA))
                    {
                        Log.Error("UpdateLayeredWindow 失败，Win32 错误码 "
                            + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    }
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, screenDc);
                }

                // 内缩模式下"尺寸变了"的那一帧走的就是这条路径 —— 同样要把浮层抬到最顶，
                // 否则那一帧会被目标窗口盖住（表现是外框闪一下不见）。
                if (RaiseAboveAll)
                {
                    Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE
                        | Native.SWP_NOSENDCHANGING);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            finally
            {
                lock (_bufGate)
                {
                    _pushInFlight--;
                    if (_pushInFlight == 0) Monitor.PulseAll(_bufGate);
                }
            }
        }

        private void ReleaseBitmap()
        {
            // 位图三件套会被两个线程碰：拖动工作线程（Rebuild/Push）与销毁路径
            // （FreeResources，可能来自 UI 线程）。没有这把锁就会一个把另一个
            // 正在用的句柄删掉 —— 表现是偶发的"外框画不出来"或 GDI 泄漏。
            lock (_bufGate)
            {
                if (_memDc != IntPtr.Zero && _oldBmp != IntPtr.Zero)
                {
                    Native.SelectObject(_memDc, _oldBmp);
                    _oldBmp = IntPtr.Zero;
                }
                if (_hBitmap != IntPtr.Zero)
                {
                    Native.DeleteObject(_hBitmap);
                    _hBitmap = IntPtr.Zero;
                }
            if (_bmp != null)
            {
                _bmp.Dispose();
                _bmp = null;
            }
            _bw = 0; _bh = 0;
            }
        }

        protected override void FreeResources()
        {
            lock (_bufGate)
            {
                // ★ 第四十八轮：等在飞的 Push 落地 ★
                //   Push 的 UpdateLayeredWindow / SetWindowPos 刻意在锁外执行
                //   （持锁做跨线程窗口调用 = 死锁，见 Push 注释）。回收位图和
                //   DC 之前必须等它们读完，否则会把正在被 ULW 读取的 DC 删掉。
                //   等待无死锁风险：在飞推送的窗口调用只等【浮层线程泵消息】，
                //   而浮层线程此刻正常泵消息，几微秒内必然返回。
                while (_pushInFlight > 0) Monitor.Wait(_bufGate);

                ReleaseBitmap();
                if (_memDc != IntPtr.Zero)
                {
                    Native.DeleteDC(_memDc);
                    _memDc = IntPtr.Zero;
                }
            }
        }
    }

    /// <summary>
    /// 拖动激活外框 —— 被触发功能接管的那扇窗口，四周亮起一圈圆角边框。
    ///
    /// 三个实现要点，每条都对应一个真实踩过的坑：
    ///
    /// 1. 【不用 SetWindowRgn 做区域】早期版本用 CreateRoundRectRgn 抠出一个"环"，
    ///    环本身确实不会留残影，但 GDI 区域是【无抗锯齿】的 —— 圆角在屏幕上就是
    ///    一级一级的锯齿，用户看到的"四个角又直又尖"正是这个。改成在 32bpp 位图上
    ///    用 GDI+ 的 SmoothingMode.AntiAlias 描边，圆角才是真正平滑的弧。
    ///
    /// 2. 【位置和像素必须一次提交】用 UpdateLayeredWindow 同时给出位置、尺寸、像素。
    ///    早期 Form + SetBounds + Refresh 是"先移动、后重画"，中间态被看见就是残影。
    ///
    /// 3. 【位图按尺寸缓存】纯移动只 UpdateLayeredWindow，不重建位图，
    ///    所以拖动热路径上没有任何分配。
    ///
    /// 浮层窗口比窗口矩形【大一圈】（见 FrameStyles.MarginFor）：柔光是往外扩的，
    /// 不留白就会被窗口边界切掉。
    /// </summary>
    internal sealed class ActivationFrame : LayeredBitmapOverlay
    {
        private string _colorId = FrameColors.DefaultId;
        private string _styleId = FrameStyles.DefaultId;
        private int _thickness = 3;
        private int _radius = 13;

        /// <summary>
        /// 分层框此刻是否真的显示着（UI 线程落地后的状态）。
        ///
        /// 供 DragEngine.ShowHoverFrame 的"同一扇窗就短路"判定使用：
        /// 只有框【确实还亮着】才允许短路，否则必须重贴一次 ——
        /// 否则一旦框曾被收掉（松键、动作收尾），那条短路会让框永远不再出现。
        /// </summary>
        public bool IsShown { get { return IsVisible; } }

        /// <summary>仅供自检：当前生效的颜色 / 样式 ID（验证"拖动中用的就是配置里的"）。</summary>
        internal string ColorIdForTest { get { return _colorId; } }
        internal string StyleIdForTest { get { return _styleId; } }

        /// <summary>把配置里的外观参数套上来。只在真的变了时才重画。</summary>
        public void ApplyTheme(string colorId, string styleId, int thickness, int radius,
                               int opacityPercent = 100)
        {
            int alpha = opacityPercent < 5 ? 5
                      : opacityPercent > 100 ? 255
                      : (int)Math.Round(opacityPercent * 255.0 / 100.0);

            bool changed = _colorId != colorId || _styleId != styleId
                        || _thickness != thickness || _radius != radius
                        || _constantAlpha != alpha;
            if (!changed) return;

            _colorId = colorId;
            _styleId = styleId;
            _thickness = thickness;
            _radius = radius;
            _constantAlpha = alpha;
            // 改透明度也要重推一次像素：SourceConstantAlpha 只在 UpdateLayeredWindow
            // 那一步生效，不重推的话新透明度要等下一次 SetRect 才被应用 ——
            // 表现就是"在设置里拖了不透明度，外框一点变化都没有"。
            MarkContentDirty();
        }

        protected override void RenderContent(Graphics g, int w, int h)
        {
            var area = new RectangleF(0, 0, w, h);
            FrameStyles.Draw(g, area, FrameColors.Resolve(_colorId), _styleId, _thickness, _radius);
        }
    }

}
