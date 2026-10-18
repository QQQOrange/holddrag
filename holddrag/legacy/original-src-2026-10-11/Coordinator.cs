using System;

namespace HoldDrag
{
    /// <summary>
    /// 输入调度中心：把钩子事件分派给“触发键状态机”和“拖动会话”。
    /// 所有“要不要吞掉这个事件”的判断都集中在这里，方便一眼看清行为边界。
    /// </summary>
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

        public bool Paused { get; private set; }

        /// <summary>设置窗口录制触发键时置位，此时触发键必须原样放行，否则录不到。</summary>
        public bool SuppressTrigger { get; set; }

        public TriggerState Trigger { get { return _trigger; } }
        public DragEngine Drag { get { return _drag; } }

        public Coordinator(ConfigStore store)
        {
            _store = store;
            _trigger = new TriggerState(() => _store.Current);
            _drag = new DragEngine(() => _store.Current);
            WindowActions.ApplyBlacklist(_store.Current.Blacklist);

            // 触发键一到阈值就把光标底下的窗口亮起来 —— 不用等按下鼠标键，
            // 用户长按之后立刻就能确认"现在接管的是这一扇"。
            _trigger.Activated += OnTriggerActivated;
            _trigger.Deactivated += OnTriggerDeactivated;
        }

        private void OnTriggerActivated()
        {
            Native.POINT pt;
            if (Native.GetCursorPos(out pt)) _drag.ShowHoverFrame(pt);
        }

        private void OnTriggerDeactivated()
        {
            _drag.HideHoverFrame();
        }

        public void PrecreateOverlays()
        {
            _drag.Precreate();
        }

        public void SetPaused(bool paused)
        {
            Paused = paused;
            if (paused) { _drag.Cancel(); _drag.HideHoverFrame(); }
            Log.Info(paused ? "已暂停" : "已恢复");
        }

        /// <summary>配置变更后调用：把正在进行的拖动收干净，避免沿用旧参数。</summary>
        public void OnConfigChanged()
        {
            _drag.Cancel();
            WindowActions.ApplyBlacklist(_store.Current.Blacklist);
            for (int i = 0; i < 3; i++)
            {
                _lastDown[i] = 0;
                _lastMoved[i] = false;
                _swallowUp[i] = false;
            }

            // ★ "在设置里改了外框颜色/样式，保存后却看不出变化"的另一半原因在这里。★
            //   Cancel() 只负责把框收掉，它不会把新外观推给浮层。而用户改完外观之后
            //   最常见的心态是"我马上长按一下看看效果" —— 如果这时浮层还缓存着旧主题，
            //   就得等它自然重画才更新。这里主动让外观缓存失效并重新渲染一次，
            //   保证"保存并生效"这四个字是字面意义上的。
            _drag.RefreshOverlayTheme();

            // 触发键还按着（比如用户按住不放、用托盘菜单改的设置）时把框重新亮回来，
            // 否则会出现"改完设置框反而没了"，用户会以为设置把外框关掉了。
            if (_trigger.Active && !Paused)
            {
                Native.POINT pt;
                if (Native.GetCursorPos(out pt)) _drag.ShowHoverFrame(pt);
            }
        }

        public void Dispose()
        {
            _drag.Dispose();
            _trigger.Dispose();
        }

        // ── 键盘 ────────────────────────────────────────────────

        public bool OnKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb)
        {
            if (Paused || SuppressTrigger) return false;
            return _trigger.HandleKeyboard(down, ref kb);
        }

        // ── 鼠标 ────────────────────────────────────────────────

        public bool OnMouse(int msg, ref Native.MSLLHOOKSTRUCT ms)
        {
            if (Paused || SuppressTrigger) return false;

            // ★ 注意：坐标换算【不在这里】做。
            //
            //   本方法既会被钩子调用（此时坐标是物理坐标），
            //   也会被自检/内部逻辑直接调用（此时坐标已经是逻辑坐标）。
            //   在这里无条件换算，会把已经是逻辑坐标的值再乘一次比例，
            //   表现就是"拖动只走了一半、窗口追不上光标"（实测踩过）。
            //
            //   正确的做法是在【真正的边界】上换算 —— 见 HookEngine.MouseCallback：
            //   那里是"系统给的物理坐标"进入本进程的唯一入口。

            // 触发键若是鼠标侧键，先交给状态机（侧键不做窗口动作，因此可以提前返回）
            if (msg == Native.WM_XBUTTONDOWN || msg == Native.WM_XBUTTONUP)
            {
                if (_trigger.HandleMouseTrigger(msg, msg == Native.WM_XBUTTONDOWN, ref ms)) return true;
                return false;
            }

            if (msg == Native.WM_MOUSEMOVE)
            {
                // ★ 绝对不要在这里 return true。
                //
                // 在 WH_MOUSE_LL 钩子里吞掉 WM_MOUSEMOVE，系统会认为这次移动被拒绝，
                // 光标就【停在原地不动】了 —— 光标不动，钩子拿到的坐标也就不再变化，
                // 于是窗口只能跟着在原地抖。用户看到的"鼠标移了一段距离，窗口却在原地哆嗦"
                // 就是这么来的。AltSnap 的鼠标钩子在移动分支上同样始终 CallNextHookEx。
                //
                // 真正驱动拖动的是坐标本身，跟"放不放行这个消息"没有任何关系。
                _drag.DeliverCursor(ms.pt);

                // 激活态下还没按鼠标键 —— 让外框跟着光标在窗口之间移动。
                // 这里只在"还在同一扇窗上"时会被 ShowHoverFrame 内部直接跳过，
                // 所以每帧的实际开销就是一个 WindowFromPoint 调用。
                if (_trigger.Active && !_drag.IsDragging) _drag.ShowHoverFrame(ms.pt);

                return false;
            }

            int idx = ButtonIndexOf(msg);
            if (idx < 0) return false;

            if (IsDownMessage(msg))
            {
                if (_trigger.Active) return HandleButtonDown(idx, ms.pt);
                return false;
            }

            return HandleButtonUp(idx, ms.pt);
        }

        private static int ButtonIndexOf(int msg)
        {
            switch (msg)
            {
                case Native.WM_LBUTTONDOWN:
                case Native.WM_LBUTTONUP:
                    return BtnLeft;
                case Native.WM_RBUTTONDOWN:
                case Native.WM_RBUTTONUP:
                    return BtnRight;
                case Native.WM_MBUTTONDOWN:
                case Native.WM_MBUTTONUP:
                    return BtnMiddle;
            }
            return -1;
        }

        private static bool IsDownMessage(int msg)
        {
            return msg == Native.WM_LBUTTONDOWN || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_MBUTTONDOWN;
        }

        private static string ActionFor(int idx, AppConfig c)
        {
            if (idx == BtnLeft) return c.ActionLeft;
            if (idx == BtnRight) return c.ActionRight;
            return c.ActionMiddle;
        }

        // ── 按键落下 ────────────────────────────────────────────

        private bool HandleButtonDown(int idx, Native.POINT pt)
        {
            var cfg = _store.Current;
            string action = ActionFor(idx, cfg);

            // ══ 诊断：为什么"按了没反应"完全查不出来 ══
      //
   // 用户报"长按 enter 之后左键移动、右键改长宽没反应"，
            // 而日志里只有"触发键激活/释放"，中间什么都没有 ——
     // 因为下面这两条 return false 都是【静默】的。
            // FindTarget 有一长串拒绝条件（黑名单 / 类名 / 全屏 / 自身进程 / 尺寸），
            // 命中任何一条，用户体验都是"按下去没反应"，而日志一个字都不留。
     //
       // ★★ 这里必须用 Info 而不是 Trace ★★
      // 上一版把它写成了 Log.Trace（只在 --trace 时输出），结果用户报障时
            // 默认启动根本看不到任何线索，诊断等于白加。
            // "操作失败"是低频事件（不是每帧都发生），记 INFO 完全不影响性能，
    // 而没有它就等于在黑盒里排查。失败路径的日志等级宁高勿低。
       if (action == Actions.Nothing)
       {
   Log.Info("[诊断] 鼠标键按下被忽略：该键没有绑定任何动作 键=" + idx);
        return false;
        }

      IntPtr hwnd = WindowActions.FindTarget(pt, cfg.AllowFullscreenWindows);
    if (hwnd == IntPtr.Zero)
        {
    // 桌面 / 任务栏 / 我们自己的浮层 / 全屏窗口 / 黑名单 —— 一律放行。
     // 但必须说清楚到底为什么，否则这条日志等于没写。
      Log.Info("[诊断] FindTarget 没找到目标，鼠标键原样放行"
      + " 键=" + idx + " 坐标=" + pt.X + "," + pt.Y
          + " 原因=" + WindowActions.DescribeReject(pt, cfg.AllowFullscreenWindows));
   return false;
            }

            long now = Environment.TickCount64;
            int dct = (int)Native.GetDoubleClickTime();
            if (dct <= 0) dct = 500;
            bool second = _lastDown[idx] != 0 && (now - _lastDown[idx]) <= dct && !_lastMoved[idx];
            _lastDown[idx] = now;
            _lastMoved[idx] = false;

            // 双击左键 = 最大化 / 还原。只在左键绑定为“移动”时生效，
            // 否则会和“左键=最大化”这类自定义绑定互相打架。
            if (idx == BtnLeft && cfg.DoubleClickLeftMaximize && action == Actions.Move && second)
            {
                _lastDown[idx] = 0;
                _swallowUp[idx] = true;
                WindowActions.MaximizeRestore(hwnd);
                RefreshHoverAfterAction(pt);
                return true;
            }

            // 双击右键 = 按九宫格吸附到屏幕对应位置
            if (idx == BtnRight && cfg.DoubleClickRightZone && action == Actions.Resize && second)
            {
                _lastDown[idx] = 0;
                _swallowUp[idx] = true;
                ApplyZoneDoubleClick(hwnd, pt, cfg);
                RefreshHoverAfterAction(pt);
                return true;
            }

            if (Actions.IsDragAction(action))
            {
                if (!_drag.Begin(hwnd, action == Actions.Move, idx, pt)) return false;
                return true;
            }

            WindowActions.Apply(hwnd, action);
            _swallowUp[idx] = true;
            RefreshHoverAfterAction(pt);
            return true;
        }

        /// <summary>
        /// ★ 用户明确要求的行为：按住触发键做完一次「非拖动动作」之后，外框必须继续留着。★
        ///
        /// 曾经漏了这一步，结果表现是：长按 Enter 激活 → 中键最大化 → 外框消失，
        /// 用户以为"功能坏了"，其实只是没人去把框重新亮回来。
        /// 拖动类动作走的是另一条路（Begin/End 自带换框），这里只管非拖动路径。
        ///
        /// 注意此时窗口矩形很可能【刚刚变过】（最大化 / 居中 / 九宫格吸附 / 还原），
        /// 而 ShowHoverFrame 内部是"还在同一扇窗上就跳过重算"的短路逻辑 ——
        /// 若沿用旧的 _hoverRaw，框会停在改动前的旧位置。所以必须先让缓存失效。
        /// </summary>
        private void RefreshHoverAfterAction(Native.POINT pt)
        {
            if (!_trigger.Active) return;      // 触发键已松开：本来就不该有框
            _drag.InvalidateHover();
            _drag.ShowHoverFrame(pt);
        }

        private static void ApplyZoneDoubleClick(IntPtr hwnd, Native.POINT pt, AppConfig cfg)
        {
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return;

            var zone = Zones.Resize(r, pt.X, pt.Y, cfg.CornerPercent);
            if (zone == ResizeZone.Center) zone = Zones.NearestCorner(r, pt.X, pt.Y);

            var snap = Zones.ScreenZoneOfResizeZone(zone);
            if (snap == SnapZone.None) return;

            var mi = Native.GetMonitorInfoAt(pt);
            if (mi.rcWork.Width <= 0) return;
            WindowActions.ApplySnap(hwnd, snap, mi.rcWork);
        }

        // ── 按键抬起 ────────────────────────────────────────────

        private bool HandleButtonUp(int idx, Native.POINT pt)
        {
            if (_drag.IsDragging && _drag.Button == idx)
            {
                _drag.End();
                _lastMoved[idx] = _drag.LastMoved;   // 供下一次双击判定：缩放/移动过就不算双击

                // 触发键可能还按着，把"接管中"的指示重新亮回到光标底下那扇窗上。
                // 用钩子事件自带的坐标，不再多调一次 GetCursorPos —— 这是钩子回调里
                // 唯一被允许廉价做的事，能省一次系统调用就省一次。
                if (_trigger.Active) _drag.ShowHoverFrame(pt);
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
}
