using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

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

        public void EnsureCreated()
        {
            if (_hwnd != IntPtr.Zero) return;

            EnsureClass();

            // WS_EX_NOACTIVATE：绝不抢焦点
            // WS_EX_TOOLWINDOW：不进 Alt+Tab、不显示在任务栏
            // WS_EX_TOPMOST：始终压在最上面
            // WS_EX_LAYERED：位置和像素一次性提交（消除残影的关键）
            // WS_EX_TRANSPARENT 刻意【不加】：它会影响绘制顺序，改用 WM_NCHITTEST 返回
            //   HTTRANSPARENT 来实现点击穿透，效果一样但没有绘制副作用。
            uint ex = (uint)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE
                           | Native.WS_EX_TOPMOST | Native.WS_EX_LAYERED);

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
                try { Native.DestroyWindow(h); } catch (Exception ex) { Log.Error(ex); }
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
                if (!_pendingValid && _lastAppliedValid && SameRect(_lastApplied, r) && _visible) return;

                _pending = r;
                _pendingValid = true;
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

            if (!_visible) return;
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
            if (!_visible)
            {
                _visible = true;
                Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
            }

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

        /// <summary>把内容画进 g。已按尺寸清成全透明，直接画即可。</summary>
        protected abstract void RenderContent(Graphics g, int w, int h);

        protected override void OnSizeChanged(int w, int h)
        {
            if (Handle == IntPtr.Zero || w <= 0 || h <= 0) return;
            _curW = w; _curH = h;
            Rebuild(w, h);
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

        private void Rebuild(int w, int h)
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

        protected override void OnPlace(Native.RECT r)
        {
            _lastPlacedX = r.Left;
            _lastPlacedY = r.Top;
            _lastPlacedValid = true;

            if (_memDc == IntPtr.Zero || _hBitmap == IntPtr.Zero) return;
            Push(r.Left, r.Top);
        }

        private void Push(int x, int y)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return;

            try
            {
                var dst = new Native.POINT(x, y);
                var size = new Native.SIZE(_bw, _bh);
                var src = new Native.POINT(0, 0);
                var blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;

                if (!Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size,
                        _memDc, ref src, 0, ref blend, Native.ULW_ALPHA))
                {
                    Log.Error("UpdateLayeredWindow 失败，Win32 错误码 "
                        + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private void ReleaseBitmap()
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

        protected override void FreeResources()
        {
            ReleaseBitmap();
            if (_memDc != IntPtr.Zero)
            {
                Native.DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
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

        /// <summary>把配置里的外观参数套上来。只在真的变了时才重画。</summary>
        public void ApplyTheme(string colorId, string styleId, int thickness, int radius)
        {
            bool changed = _colorId != colorId || _styleId != styleId
                        || _thickness != thickness || _radius != radius;
            if (!changed) return;

            _colorId = colorId;
            _styleId = styleId;
            _thickness = thickness;
            _radius = radius;
            MarkContentDirty();
        }

        protected override void RenderContent(Graphics g, int w, int h)
        {
            var area = new RectangleF(0, 0, w, h);
            FrameStyles.Draw(g, area, FrameColors.Resolve(_colorId), _styleId, _thickness, _radius);
        }
    }

    /// <summary>
    /// 拖动期环形外框 —— 拖动进行当中亮在目标窗口四周的那圈环。
    ///
    /// 为什么拖动中不用 ActivationFrame（分层位图）而单独要它：
    ///   · 分层浮层每动一帧都要 UpdateLayeredWindow 重新上传整张位图（窗口多大图就多大），
    ///     缩放时还要每帧重建 GDI+ 位图 —— 这就是"缩放不跟手、外框拖残影"的真正来源；
    ///   · 而且位图浮层的更新要先 PostMessage 回 UI 线程，和工作线程的 SetWindowPos 不同拍，
    ///     窗口已经走了、环还在后面追，看起来就是一条甩不掉的外框残影。
    ///
    /// 这个环是【区域窗口】：SetWindowRgn 抠出一个圆角环，纯色填充，完全不透明。
    ///   · 纯平移 = 一次 SetWindowPos，系统 copybits，零重绘、零位图上传；
    ///   · 缩放 = 一次 SetWindowPos + 一次 SetWindowRgn（几微秒的 GDI 调用），不建位图；
    ///   · 全部用 SetRectNow() 由调用线程（钩子/工作线程）【同步直写】，
    ///     和 SetWindowPos 同一拍落地，环永远贴着窗口，不存在"追不上"的问题。
    ///
    /// 代价是区域边缘没有抗锯齿（1bit 掩码）。但这是在【快速拖动过程】中，
    /// 人眼对运动中的 1px 锯齿几乎无感；拖动一结束就换回抗锯齿的 ActivationFrame
    /// （悬停/松手状态），静止时看到的仍然是平滑的弧。
    /// </summary>
    internal sealed class DragRingFrame : OverlayWindow
    {
        private IntPtr _brush = IntPtr.Zero;
        private int _thickness = 3;
        private int _radius = 13;
        private int _margin = 5;
        private int _curR = -1, _curG = -1, _curB = -1;
        private int _w = -1, _h = -1;
        private bool _shown;

        /// <summary>
        /// 换颜色 / 粗细 / 圆角。颜色变了重建画刷，几何参数下一次 ApplyRegion 生效。
        ///
        /// 返回 true 表示外观确实变了（调用方据此决定要不要顺手重画一次）。
        /// 换刷子本身不会重绘 —— 窗口内容还是旧颜色的像素，所以这里补一次
        /// InvalidateRect：拖动中每帧都在写矩形，颜色会在一帧内跟上；
        /// 若正好处于静止（拖动暂停），也能立刻看到新颜色。
        /// </summary>
        public bool ApplyTheme(string colorId, int thickness, int radius, int margin)
        {
            var col = FrameColors.Resolve(colorId);

            bool sameColor = col.R == _curR && col.G == _curG && col.B == _curB;
            bool sameGeom = _thickness == thickness && _radius == radius && _margin == margin;

            // ══════ ★★★ 必须在这里就返回，绝不能走到创建画刷 ★★★ ══════
            //
            // 原来这段代码【每帧】都会执行：
            //     IntPtr nb = CreateSolidBrush(...);
            //     if (_brush != 0) DeleteObject(_brush);
            //     _brush = nb;
            // 而 DragEngine.Step 每一帧都会调用本方法 ——
            // 160Hz 下等于【每秒 320 次 GDI 对象创建 + 销毁】。
            //
            // GDI 对象是内核对象，创建/销毁要动句柄表。这么高的 churn 频率
            // 会明显拖慢帧节奏，用户感知到的就是"右键缩放不跟手、一卡一卡"。
            // 而且外框外观在拖动过程中根本不会变（要变也得等用户去改设置），
            // 所以 99.99% 的调用都是纯粹的浪费。
            if (sameColor && sameGeom) return false;

            _thickness = thickness;
            _radius = radius;
            _margin = margin;

            // 只有颜色真的变了才重建画刷
            if (!sameColor)
            {
                IntPtr nb = Native.CreateSolidBrush(Native.ColorRef(col.R, col.G, col.B));
                if (nb != IntPtr.Zero)
                {
                    if (_brush != IntPtr.Zero) Native.DeleteObject(_brush);
                    _brush = nb;
                }
                _curR = col.R;
                _curG = col.G;
                _curB = col.B;
            }

            // 几何参数改了要立刻重抠一次区域（否则新的粗细/圆角要等下一次改尺寸才生效）
            if (!sameGeom && _shown && IsCreated)
            {
                try { ApplyRegion(_w, _h); } catch { }
            }

            if (_shown && IsCreated)
            {
                try { Native.InvalidateRect(Handle, IntPtr.Zero, false); } catch { }
            }
            return true;
        }

        /// <summary>
        /// 同步直写矩形。任意线程可调用 —— 这是它存在的意义：
        /// 工作线程每帧 SetWindowPos 之后【立刻】把环摆过去，两者同一拍。
        /// 用 SWP_NOSENDCHANGING，避免跨线程同步消息把调用方堵住。
        /// </summary>
        public void SetRectNow(Native.RECT r)
        {
            if (!IsCreated) return;
            if (r.Width < 4 || r.Height < 4) { HideNow(); return; }

            bool sizeChanged = r.Width != _w || r.Height != _h;

            if (!_shown)
            {
                // 先摆好位置/区域再显示，避免在旧位置闪一帧
                Place(r, true);
                Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
                _shown = true;
                return;
            }
            Place(r, sizeChanged);
        }

        /// <summary>同步隐藏。任意线程可调用。</summary>
        public new void HideNow()
        {
            if (!_shown) return;
            _shown = false;
            if (IsCreated) Native.ShowWindow(Handle, Native.SW_HIDE);
            _w = -1; _h = -1;   // 下次显示时强制重建区域
        }

        private void Place(Native.RECT r, bool sizeChanged)
        {
            const uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE
                             | Native.SWP_NOOWNERZORDER | Native.SWP_NOSENDCHANGING;

            if (sizeChanged)
            {
                _w = r.Width; _h = r.Height;
                Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, flags);
                ApplyRegion(r.Width, r.Height);
            }
            else
            {
                Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, 0, 0,
                    flags | Native.SWP_NOSIZE);
            }
        }

        /// <summary>按当前尺寸抠出圆角环：外圆角矩形 − 内圆角矩形。</summary>
        private void ApplyRegion(int w, int h)
        {
            int half = _thickness / 2;
            int o = _margin - half - 1; if (o < 0) o = 0;
            int i = _margin + half + 1;
            if (i > w / 2) i = w / 2;
            if (i > h / 2) i = h / 2;
            int orad = _radius + half;
            int irad = _radius - half; if (irad < 0) irad = 0;

            IntPtr outer = Native.CreateRoundRectRgn(o, o, w - o + 1, h - o + 1, orad * 2, orad * 2);
            IntPtr inner = Native.CreateRoundRectRgn(i, i, w - i + 1, h - i + 1, irad * 2, irad * 2);
            IntPtr ring = Native.CreateRectRgn(0, 0, 0, 0);

            if (outer != IntPtr.Zero && inner != IntPtr.Zero && ring != IntPtr.Zero)
            {
                Native.CombineRgn(ring, outer, inner, Native.RGN_DIFF);
                // SetWindowRgn 成功后系统接管 ring，绝对不能再 DeleteObject 它
                if (Native.SetWindowRgn(Handle, ring, true) == 0) Native.DeleteObject(ring);
            }
            else if (ring != IntPtr.Zero)
            {
                Native.DeleteObject(ring);
            }
            if (outer != IntPtr.Zero) Native.DeleteObject(outer);
            if (inner != IntPtr.Zero) Native.DeleteObject(inner);
        }

        /// <summary>环的内容就是一整块纯色（窗口区域已经裁成环形，填满客户区即可）。</summary>
        protected override bool HandlePaint()
        {
            if (_brush == IntPtr.Zero || !IsCreated) return false;
            IntPtr dc = Native.GetDC(Handle);
            if (dc == IntPtr.Zero) return false;
            try
            {
                Native.RECT rc;
                Native.GetClientRect(Handle, out rc);
                Native.FillRect(dc, ref rc, _brush);
            }
            finally { Native.ReleaseDC(Handle, dc); }
            try { Native.ValidateRect(Handle, IntPtr.Zero); } catch { }
            return true;
        }

        // 异步管线（SetRect → WM_APPLY）对环不适用 —— 它只走 SetRectNow 同步直写。
        protected override void OnSizeChanged(int w, int h) { }
        protected override void OnPlace(Native.RECT r) { }

        protected override void FreeResources()
        {
            if (_brush != IntPtr.Zero)
            {
                Native.DeleteObject(_brush);
                _brush = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// 吸附预览 —— 拖到屏幕边缘时，预先把落点的半透明色块画出来。
    ///
    /// 与外框共用同一套缓存管线：位置 + 尺寸 + 像素一次提交，位图只在尺寸变化时重建。
    /// </summary>
    internal sealed class SnapPreview : LayeredBitmapOverlay
    {
        /// <summary>预览填充的不透明度（0-255）。自检断言用。</summary>
        public const int FillAlpha = 60;
        public const int BorderAlpha = 200;

        protected override void RenderContent(Graphics g, int w, int h)
        {
            var a = Native.GetAccent();
            var accent = Color.FromArgb(a.R, a.G, a.B);

            g.SmoothingMode = SmoothingMode.None;
            using (var fill = new SolidBrush(Color.FromArgb(FillAlpha, accent)))
            {
                g.FillRectangle(fill, 0, 0, w, h);
            }
            using (var pen = new Pen(Color.FromArgb(BorderAlpha, accent), 2f))
            {
                g.DrawRectangle(pen, 1, 1, Math.Max(0, w - 3), Math.Max(0, h - 3));
            }
        }
    }
}
