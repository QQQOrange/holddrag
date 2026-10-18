using System;
using System.Diagnostics;

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

        /// <summary>
        /// 触发键激活时抢占钩子链头部的动作（由 TrayContext 接到
        /// HookEngine.ReinstallMouseForChainPriority）。
        ///
        /// 场景（用户报"开着 wgesture 时右键拖拽不触发，左键正常"）：
        /// 低级钩子按安装时间 LIFO 调用，wgesture 的鼠标钩子装在我们后面，
        /// 右键按下先过它、被它当手势键吞掉 —— 我们的钩子压根看不到那条 down。
        /// 激活这一刻用户还没点鼠标，把鼠标钩子就地重装到链头即可。
        /// 详见 HookEngine.WM_REHOOK_MOUSE 的注释。
        /// </summary>
        internal Action ReinstallHooksForPriority;

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
            // ══════ ★ 第四十六轮：激活第一件事 —— 抢占钩子链头部 ★ ══════
            //
            //  用户报障："运行 wgesture 时，长按 Enter 之后左键拖动正常，
            //            右键缩放完全没反应。"
            //
            //  根因：低级钩子按安装时间 LIFO 调用。wgesture（中完整性级别）的
            //  鼠标钩子装在我们后面 → 每次右键按下【先过它】，wgesture 把右键
            //  当自己的手势键吞掉，我们的钩子根本看不到那条 RBUTTONDOWN ——
            //  拖动自然无从发起。左键没事，是因为 wgesture 只吃右键。
            //  （本程序以管理员运行时同理：高完整性不吃亏，链序只论 LIFO。）
            //
            //  修法：激活这一刻（用户还没点击鼠标）把鼠标钩子就地重装到链头。
            //  此后本会话的每个鼠标事件都先过我们这道，wgesture 再也锁存不到
            //  右键 down。整个动作发生在钩子线程自己的消息循环里，亚毫秒级。
            //
            //  两个跳过条件：
            //    · 拖动已经进行中 —— 这条会话的 down 早就被我们收到了，
            //      没有抢链头的必要，还平白多一次拆装；
            //    · 录制触发键 / 暂停中 —— 不该动钩子。
            if (!Paused && !SuppressTrigger && !_drag.IsDragging)
            {
                var reinstall = ReinstallHooksForPriority;
                if (reinstall != null)
                {
                    try { reinstall(); }
                    catch (Exception ex) { Log.Error(ex); }
                }
            }

            // 触发键一到阈值就把光标底下的窗口亮起来。
            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;

            // ══════ ★ 第四十四轮：激活第一件事 —— 取消目标窗口的"拖选模态"★ ══════
            //
            //  用户报障："在文字页面里选中一段文本后按下 Enter，执行后鼠标左键仍然
            //            停留在文本选中状态，怎么拖动都只会继续选中文本。"
            //
            //  根因是时序：那次左键按下发生在【长按 Enter 之前】，于是它被原样放行了，
            //  目标程序（浏览器 / 编辑器）就此进入"按住拖选"模态。等 Enter 到阈值
            //  激活时，_heldDuringHold 那条记账路径根本不覆盖这种时序（它只记录
            //  "触发键已按住之后到达的按下"），拖动一直补不起来；
            //  而 WM_MOUSEMOVE 又绝不能吞（吞了光标冻结），目标程序就靠这些移动消息
            //  一路扩展选区 —— 用户看到的就是"永远在选文字，窗口拖不动"。
            //
            //  收口：激活这一刻就 FindTarget + WM_CANCELMODE。它必须发生在
            //  【亮外框 / 补起拖动】之前 —— 用户要求的顺序正是
            //  "先取消当前选中状态，再执行 Enter 对应的软件功能，最后交互状态完全复位"。
            //
            //  只对"我们本来就会接管"的窗口发（FindTarget 能命中的才发）：
            //  用户在黑名单程序 / 全屏程序里误触了触发键时，不去破坏人家的选中状态。
            var cfg = _store.Current;
            bool allowFs = cfg.AllowFullscreenWindows || cfg.ExitFullscreenOnTrigger;
            IntPtr hwnd = WindowActions.FindTarget(pt, allowFs);
            if (hwnd != IntPtr.Zero)
            {
                // 有"放行过且未配对"的按下才值得记这条诊断 —— 它是本 bug 的指纹。
                bool phantom = false;
                for (int b = BtnLeft; b <= BtnMiddle; b++) if (_passedDown[b]) phantom = true;
                if (phantom)
                    Log.Info("[诊断] 激活时目标窗口还按着我们放行过的鼠标键（它可能停在拖选模态）"
                        + " → 先取消 句柄=" + hwnd.ToInt64().ToString("X"));
                CancelTargetMouseMode(hwnd);
            }

            _drag.ShowHoverFrame(pt);
        }

        private void OnTriggerDeactivated()
        {
            // ★ 松开触发键 → 外框【立即消失】。★
            //
            // 用户明确要求：「执行完命令并松开所有按键后，视觉反馈外框仍然存在」是 bug，
            // 「松开按键后外框应立即消失」才是预期行为。
            // （第三十八轮曾做过"松键后把外框钉在窗口上"，第三十九轮按用户反馈整体撤销。）
            // 拖动中（_dragging）不会走到这里：那时外框归 Begin/End 管，HideHoverFrame 自带守卫。
            _drag.HideHoverFrame();
        }

        public void PrecreateOverlays()
        {
            _drag.Precreate();

            // ★ 外框残留兜底：每 500ms 自检一次"本不该有框却还亮着" ★
            //   放在这里而不是浮层内部：判断需要"触发键是否激活"这个只有协调器知道的信息。
            //   2Hz 的布尔判断，开销可以忽略。
            _frameGuard.Interval = 500;
            _frameGuard.Tick += (s, e) =>
            {
                try { _drag.EnforceFrameInvariant(_trigger.Active); }
                catch (Exception ex) { Log.Error(ex); }
            };
            _frameGuard.Start();
        }

        // ══════ ★ 钩子回调耗时统计（回答"是不是我们把整机拖慢了"）★ ══════
        //
        //  低级键鼠钩子是【系统级】的：全机每一次鼠标移动、每一次按键，
        //  都要先在我们的进程里跑一遍回调，才能继续交给目标程序。
        //  所以回调里多花 1ms，整台机器的输入就多 1ms 延迟 ——
        //  这正是"软件一开整机就变卡"这类反馈最可能的来源。
        //
        //  与其猜，不如把每次回调的耗时量出来：
        //    · HookMaxMsForTest 给出"最慢的一次"；
        //    · 超过 SlowHookMs 就按秒节流记一条日志，用户日志里能直接看到。
        //  开销：每次回调两次高精度时间戳读取（约几十纳秒），可以忽略。
        internal static double HookMaxMsForTest;
        internal static int SlowHookCountForTest;
        private const double SlowHookMs = 8;
        private static long _slowHookLoggedAt;

        private static void NoteHookCost(long startedAt, string what)
        {
            try
            {
                double ms = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
                if (ms > HookMaxMsForTest) HookMaxMsForTest = ms;
                if (ms < SlowHookMs) return;
                SlowHookCountForTest++;

                // 每秒最多记一条：钩子回调里绝不能因为打日志而变慢，这是本末倒置
                long now = Environment.TickCount64;
                if (now - _slowHookLoggedAt < 1000) return;
                _slowHookLoggedAt = now;
                Log.Info("[诊断] 钩子回调偏慢 " + what + " 耗时=" + ms.ToString("0.0")
                    + "ms（系统级钩子：这一段时间整机输入都被拖住）");
            }
            catch { }
        }

        /// <summary>外框残留兜底的定时器（500ms）。</summary>
        private readonly System.Windows.Forms.Timer _frameGuard =
            new System.Windows.Forms.Timer();

        /// <summary>退出路径：同步把浮层收掉。</summary>
        public void HideOverlaysNow()
        {
            try { _frameGuard.Stop(); } catch { }
            _drag.HideOverlaysNow();
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
                _heldDuringHold[i] = false;
                _passedDown[i] = false;
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
            // ★ 停表必须排在 _drag.Dispose() 之前 ★
            //   否则最后一跳 Tick 可能在 _drag 已释放之后才到，
            //   EnforceFrameInvariant 就会去碰已经销毁的浮层（有 catch 兜底不会崩，
            //   但会在退出日志里留一串无意义的错误）。
            try { _frameGuard.Stop(); _frameGuard.Dispose(); } catch { }

            _drag.Dispose();
            _trigger.Dispose();
        }

        // ── 键盘 ────────────────────────────────────────────────

        public bool OnKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb)
        {
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                if (Paused || SuppressTrigger) return false;
                return _trigger.HandleKeyboard(down, ref kb);
            }
            finally { NoteHookCost(t0, down ? "按键按下" : "按键抬起"); }
        }

        // ── 鼠标 ────────────────────────────────────────────────

        public bool OnMouse(int msg, ref Native.MSLLHOOKSTRUCT ms)
        {
            long t0 = Stopwatch.GetTimestamp();
            try { return OnMouseCore(msg, ref ms); }
            finally { NoteHookCost(t0, "鼠标消息"); }
        }

        private bool OnMouseCore(int msg, ref Native.MSLLHOOKSTRUCT ms)
        {
            if (Paused || SuppressTrigger)
            {
                // 暂停 / 录制触发键期间，键鼠消息一律原样放行。放行出去的【抬起】
                // 意味着目标程序自己收下了配对 —— 两条记账随之失效，必须就地清掉，
                // 否则恢复之后会拿着过期记账去补发合成的抬起（对着一扇
                // 早就没有模态的窗口发骚扰消息）。
                int u = ButtonIndexOf(msg);
                if (u >= 0 && !IsDownMessage(msg))
                {
                    _heldDuringHold[u] = false;
                    _passedDown[u] = false;
                }
                return false;
            }

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
                if (_trigger.Active && !_drag.IsDragging)
                {
                    // ★ 先补"触发时鼠标键已经按住"的拖动（见 AdoptHeldButtonIfAny）★
                    //   必须放在 ShowHoverFrame 之前：补起来之后 IsDragging 为真，
                    //   悬停框就不该再抢着画了（拖动帧由工作线程负责）。
                    AdoptHeldButtonIfAny(ms.pt);
                    if (!_drag.IsDragging) _drag.ShowHoverFrame(ms.pt);
                }

                return false;
            }

            int idx = ButtonIndexOf(msg);
            if (idx < 0) return false;

            if (IsDownMessage(msg))
            {
                if (_trigger.Active)
                {
                    if (HandleButtonDown(idx, ms.pt)) return true;
                    // HandleButtonDown 没接管（没绑动作 / 没命中目标 / Begin 失败）：
                    // 这条 down 同样原样放行给了目标程序 —— 记上幽灵按下账。
                    _passedDown[idx] = true;
                    return false;
                }

                // ══════ ★ 第四十三轮：记下"触发键按住期间按下的鼠标键" ★ ══════
                //
                //  用户报障："有时候已经触发功能了（窗口周围已经出现一圈光晕），
                //            但这时候左键已经按住，无论怎么移动那个窗口都没有反应。"
                //
                //  根因是【时序漏洞】：拖动只可能由"按钮按下事件"发起，而用户完全可能
                //  在触发键还没到阈值（HoldMs 那 300ms 窗口里）就把鼠标键按下去了 ——
                //  那条 down 事件到达时 _trigger.Active 还是 false，于是被原样放行、
                //  【什么也没记下来】。等触发键激活、外框亮起时，按钮已经是"按着"的状态，
                //  而没有任何代码会去补一次拖动 → 窗口永远不动，外框却一直亮着。
                //  "有时候"正是因为它取决于用户先按鼠标还是先等触发键激活。
                //
                //  这里只【记账】，不动手 —— 真正的补起放在鼠标移动时做，
                //  因为那一刻才知道光标落在哪扇窗上。
                //
                //  ★ 记账的条件必须是"触发键【正被按住】但还没到激活阈值" ★
                //   不能写成"触发键未激活就记账"：那样"用户在别的程序里拖文件、
                //   顺手按了触发键"也会被记账，激活后就把人家的拖动接管了。
                //   那次按下发生在触发键按下【之前】，此刻 Held 还是 false，天然排除。
                if (_trigger.Held) _heldDuringHold[idx] = true;

                // ★ 第四十四轮：但"幽灵按下"这条账与 Held 无关，一律要记 ★
                //   第四十三轮排除的时序（按下发生在触发键按住之前）恰恰是本次报障的
                //   场景：先选中文字、再长按 Enter。目标程序实收了这条 down，
                //   必须有人记住去给它补一个配对的抬起 / 取消它的模态。
                //   两条记账的分工见 _passedDown 字段的注释。
                _passedDown[idx] = true;
                return false;
            }

            // 抬起：无论如何都要清掉记账（含"按下时触发键还没激活、抬起时已激活"这种跨状态的情况）
            _heldDuringHold[idx] = false;
            _passedDown[idx] = false;
            return HandleButtonUp(idx, ms.pt);
        }

        /// <summary>
        /// 激活时已经被按住的鼠标键，若此刻仍然物理按着，就补起一次拖动/缩放。
        ///
        /// ★ 为什么判据是"我们亲眼看到过那次按下"，而不是"按钮此刻是否按下" ★
        ///   后者会把"用户正在别的程序里拖文件、顺手按了触发键"也一起接管 ——
        ///   那是一次真实的误操作。而这里的两条记账都要求"那次按下事件确实被我们
        ///   处理过"，语义精确：用户确实是想"触发 + 这一键"。
        ///
        /// 覆盖两段时序（两条记账，缺一不可）：
        ///   · _heldDuringHold —— 按下发生在【触发键已按住、还没到阈值】那段时间里；
        ///   · _passedDown     —— 按下发生在【触发键还没被按住】的时候。
        ///     第四十四轮补上它，正是因为用户真实场景是"先选中文字、再长按 Enter"：
        ///     激活时左键早已按着，旧代码只认前者，于是拖动永远补不起来，
        ///     目标程序靠一次又一次的移动消息继续扩展选区。
        ///
        /// 失败时静默重试（不刷日志）：光标可能这一刻还在桌面/黑名单窗口上，
        /// 挪到目标窗口之后这一次就能补起来。
        /// </summary>
        private void AdoptHeldButtonIfAny(Native.POINT pt)
        {
            for (int b = BtnLeft; b <= BtnMiddle; b++)
            {
                // 记账已经过期（键其实早松了，只是我们没收到抬起）：作废
                if (!IsButtonPhysicallyDown(b))
                {
                    _heldDuringHold[b] = false;
                    _passedDown[b] = false;
                    continue;
                }

                if (!_heldDuringHold[b] && !_passedDown[b]) continue;

                if (HandleButtonDown(b, pt, true) && _drag.IsDragging)
                {
                    bool duringHold = _heldDuringHold[b];
                    _heldDuringHold[b] = false;
                    _passedDown[b] = false;
                    Log.Info("[诊断] 触发键激活时该鼠标键已被按住"
                        + (duringHold ? "（按下事件发生在触发键按住期间）"
                                      : "（按下事件发生在触发键按住之前，目标可能停在拖选模态）")
                        + " → 补起拖动 键=" + b + " 坐标=" + pt.X + "," + pt.Y);
                    return;
                }
            }
        }

        /// <summary>仅供自检：向目标窗口发出"取消拖拽模态"的次数。</summary>
        internal static int CancelModeSentForTest;

        /// <summary>仅供自检：向目标窗口补发"配对的按钮抬起"的次数。</summary>
        internal static int PhantomUpSentForTest;

        /// <summary>仅供自检：最近一次补发的"配对的抬起"消息内容。</summary>
        internal sealed class PairedUpProbe
        {
            public IntPtr Hwnd;
            public int Msg;
            public int WParam;
            public IntPtr LParam;
        }

        /// <summary>null 表示本次运行还没补发过。字段含义见 PostPairedButtonUp。</summary>
        internal static volatile PairedUpProbe LastPairedUpForTest;

        /// <summary>
        /// 给目标窗口补发一条与"幽灵按下"配对的按钮抬起消息。
        ///
        /// ★ 为什么需要它（WM_CANCELMODE 不够的那一面）★
        ///   目标程序收到过我们放行过去的真实 down，从此认为"键还按着"。
        ///   WM_CANCELMODE 负责请它结束拖拽模态（标准控件的修法，见
        ///   CancelTargetMouseMode），但真正把"按下标记"复位的往往是
        ///   成对出现的 MouseUp —— 浏览器渲染进程（Blink）靠自己收到的
        ///   MouseDown / MouseUp 维护 mouse_down_ 状态，顶层窗口收到的
        ///   WM_CANCELMODE 并不会转发进渲染进程。所以补一条合成的抬起，
        ///   才是一次完整的配对。
        ///
        /// ★ 为什么用 PostMessage 而不是 SendInput 合成物理抬起 ★
        ///   SendInput 会改动【全局】按键状态表：注入的左键抬起会让
        ///   GetAsyncKeyState 当场报告"左键已松开"，而用户的手还按着。
        ///   我们整套收养/会话逻辑（IsButtonPhysicallyDown、会话吞抬）
        ///   都建立在"逻辑状态 == 物理状态"之上，一旦被注入事件打乱，
        ///   最坏情况是拖动会话永远等不到抬起 → 窗口粘在光标上。
        ///   PostMessage 只动目标窗口自己的消息队列，没有任何全局副作用。
        ///
        /// ★ 为什么发得出去（UIPI 方向问题）★
        ///   UIPI 限制的是"低完整性 → 高完整性"。本程序默认以管理员身份
        ///   运行（高完整性），而目标程序几乎总是普通权限（中完整性）——
        ///   高→低方向完全放行。同权限更是没有任何限制。
        ///   （若目标窗口的完整性比我们还高，Post 会被静默丢弃 ——
        ///     那种窗口本就是极少数，且 WM_CANCELMODE 同样够不着。）
        ///
        /// ★ 发错窗口怎么办 ★
        ///   若按下后光标挪去了别的窗口，这条 up 会发到"此刻光标下的目标"。
        ///   实践中拖选模态的窗口几乎都持有鼠标捕获，用户真正松手时那次
        ///   真实 up 仍会被捕获窗口收走、自动配对，所以最坏情况只是这条
        ///   合成 up 落在无关窗口上当无害的空消息，不会把状态弄得更乱。
        /// </summary>
        private static void PostPairedButtonUp(IntPtr hwnd, int idx, Native.POINT pt)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;

                int msg = idx == BtnLeft ? Native.WM_LBUTTONUP
                        : idx == BtnRight ? Native.WM_RBUTTONUP
                        : Native.WM_MBUTTONUP;

                // wParam：如实报告"抬起发生时其余键的按下状态"。
                // 例：用户左右键一起按着我们收养右键，补发的右键抬起应带 MK_LBUTTON。
                int mk = 0;
                if (idx != BtnLeft && IsButtonPhysicallyDown(BtnLeft)) mk |= Native.MK_LBUTTON;
                if (idx != BtnRight && IsButtonPhysicallyDown(BtnRight)) mk |= Native.MK_RBUTTON;
                if (idx != BtnMiddle && IsButtonPhysicallyDown(BtnMiddle)) mk |= Native.MK_MBUTTON;

                // lParam：客户区坐标（鼠标消息的固定语义）。带真实坐标只是
                // 为了让消息尽量逼真；窗口过程判定"抬起"时并不依赖它。
                Native.POINT client = pt;
                Native.ScreenToClient(hwnd, ref client);
                IntPtr lp = new IntPtr((client.Y << 16) | (client.X & 0xFFFF));

                Native.PostMessageW(hwnd, msg, new IntPtr(mk), lp);
                PhantomUpSentForTest++;

                // 留一份内容给自检："如实报告其余按键的状态"不能只靠注释保证 ——
                // msg / wParam 掩码 / lParam 坐标三项都要可断言。
                LastPairedUpForTest = new PairedUpProbe
                {
                    Hwnd = hwnd,
                    Msg = msg,
                    WParam = mk,
                    LParam = lp
                };
            }
            catch { }
        }

        /// <summary>
        /// 请目标窗口结束当前的鼠标拖拽模态状态。
        ///
        /// ★ 为什么需要这一步 ★
        ///   用户报障："触发全屏切换 / 拖动窗口之后，窗口内的部分文字会被意外选中。"
        ///
        ///   机制：低级鼠标钩子能吞掉按钮的按下与抬起，但【绝不能吞掉 WM_MOUSEMOVE】
        ///   （吞掉之后光标会停在原地，窗口只能跟着原地抖）。于是拖动期间目标程序
        ///   持续收到带 MK_LBUTTON 的移动消息 —— 而只要它【曾经】收到过一次真实的
        ///   按钮按下，就进入了"按住拖选"模态，后面的移动全被解释成扩展选区。
        ///   那种"曾经收到过按下"最常见的情形，就是触发键还没到阈值时用户先按了左键
        ///   （那条 down 当时被原样放行了）。
        ///
        ///   WM_CANCELMODE 是 Windows 为此专设的消息：收到它的窗口应当结束当前的
        ///   鼠标模态（结束拖选、释放它自己的鼠标捕获）。它是 DefWindowProc 标准
        ///   行为，对所有标准控件（EDIT / RICHEDIT / 浏览器渲染控件）都生效，
        ///   且完全异步（PostMessage），不会阻塞钩子回调。
        ///
        ///   与它配合的另外三面：
        ///     · 拖动会话建立之后，该键的按下与抬起都由我们的钩子【吞掉】，
        ///       目标窗口看不到这次"新的按下"，不会再进一次模态；
        ///     · 收养"激活前就已经按着的键"时，先补发一条合成的抬起
        ///       （PostPairedButtonUp），把目标程序单方面认定的按下配对掉；
        ///     · WM_MOUSEMOVE 必须始终放行（否则光标冻结），这是无法消除的
        ///       残余 —— 所以上面那条 WM_CANCELMODE 才是真正的收口。
        ///
        ///   ★ 为什么不学 AltSnap 那样"SetCapture 抢鼠标"★
        ///   SetCapture 的官方文档写得很清楚：只有【前台窗口】能捕获鼠标，
        ///   后台窗口只在自己可见区域内收得到消息，而且"不能用来捕获发往
        ///   另一个进程的鼠标输入"。我们的接管窗口既不是前台、又是隐藏窗口，
        ///   而拖拽目标几乎总是别的进程 —— 机制上就不成立；
        ///   即便在同一进程里侥幸生效，鼠标捕获期间"菜单热键与键盘加速键全部失效"。
        ///   再加上"忘了释放 = 整个桌面的鼠标失灵"这个后果最严重的失败模式，
        ///   收益为零、风险极大，故彻底不采用。这是文档层面的结论，不是经验猜测。
        /// </summary>
        private static void CancelTargetMouseMode(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;
                Native.PostMessageW(hwnd, Native.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
                CancelModeSentForTest++;
            }
            catch { }
        }

        /// <summary>鼠标键此刻是否【物理按下】（GetAsyncKeyState 的最高位）。</summary>
        private static bool IsButtonPhysicallyDown(int idx)
        {
            // 逐键覆盖优先：补发的配对抬起要"如实报告其余按键的状态"，
            // 自检必须能逐键设定这个前提。AssumeButtonsDownForTest 是恒真，
            // 拿它测掩码只会得到"永远全 1"，什么都验证不到。
            var ov = ButtonsDownOverrideForTest;
            if (ov != null) return idx >= 0 && idx < ov.Length && ov[idx];
            if (AssumeButtonsDownForTest) return true;
            int vk = idx == BtnLeft ? Native.VK_LBUTTON
                   : idx == BtnRight ? Native.VK_RBUTTON
                   : Native.VK_MBUTTON;
            try { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return false; }
        }

        /// <summary>
        /// 仅供自检：逐键覆盖"物理按下"判定（null=走 GetAsyncKeyState / AssumeButtonsDownForTest）。
        /// 长度必须为 3（左/右/中，见 BtnLeft/BtnRight/BtnMiddle）。
        /// 存在期间【收养过期判定】（AdoptHeldButtonIfAny）也读它，用例用完务必还原成 null。
        /// </summary>
        internal static bool[] ButtonsDownOverrideForTest;

        /// <summary>仅供自检：让"物理按下"判定恒为真（自检环境里没有真实按键）。</summary>
        internal static bool AssumeButtonsDownForTest;

        /// <summary>触发键按住期间被按下的鼠标键（与 BtnLeft/BtnRight/BtnMiddle 对应）。</summary>
        private readonly bool[] _heldDuringHold = new bool[3];

        /// <summary>仅供自检：某个鼠标键的"按住期间按下"记账是否还在。</summary>
        internal bool HeldDuringHoldForTest(int idx)
        {
            return idx >= 0 && idx < _heldDuringHold.Length && _heldDuringHold[idx];
        }

        /// <summary>
        /// ★ 第四十四轮："幽灵按下"记账（与 BtnLeft/BtnRight/BtnMiddle 对应）。★
        ///
        /// 语义：目标程序手里有一个我们【原样放行】过去的按钮按下，
        /// 并且至今没有见到与它配对的抬起 —— 于是那个程序单方面地
        /// 认为"鼠标键还按着"，停在按住拖选 / 按住拖动之类的模态里。
        ///
        /// 与 _heldDuringHold 的分工（两个都要有，管的是两段时序）：
        ///   · _heldDuringHold：按下发生在【触发键已按住、还没到阈值】那段时间里；
        ///   · _passedDown：     按下发生在【触发键还没被按住】的时候
        ///                       （用户先选中文字，再来长按触发键 —— 正是本次报障的场景）。
        ///
        /// 什么时候置位：任何一次"被放行给目标程序的按钮按下"（OnMouseCore
        /// 的 return false 路径）—— 目标程序确确实实收到了这条真实消息。
        /// 什么时候清除（三条，缺一不可）：
        ///   ① 我们看到配对的真实抬起放行了出去（OnMouseCore 抬起分支）；
        ///   ② 我们补发了合成的抬起给它（PostPairedButtonUp，见下）；
        ///   ③ 记账已过期（按键其实早松了 / 配置变更 / 暂停恢复）。
        /// </summary>
        private readonly bool[] _passedDown = new bool[3];

        /// <summary>仅供自检：某个鼠标键的"幽灵按下"记账是否还在。</summary>
        internal bool PassedDownForTest(int idx)
        {
            return idx >= 0 && idx < _passedDown.Length && _passedDown[idx];
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
            return HandleButtonDown(idx, pt, false);
        }

        /// <summary>
        /// quiet = true 时抑制"没有目标 / 没有绑定动作"这两条诊断日志。
        /// 补起拖动（AdoptHeldButtonIfAny）会在每次鼠标移动时重试，
        /// 不静音的话光标停在桌面上的那几秒会刷出上百行同样的日志。
        /// </summary>
        private bool HandleButtonDown(int idx, Native.POINT pt, bool quiet)
        {
            var cfg = _store.Current;

            // ★ 收养语义：这个键在进来之前就已经物理按着了 ★
            //   激活前 / 阈值前的那条真实 down 被我们放行过（两条记账任一为真）。
            //   收养路径【绝不能】触发双击动作：双击判定依赖 _lastDown，
            //   而收养的这条 down 从未经过 HandleButtonDown，_lastDown 还是
            //   上一轮会话的残留，不排除就会把"补起拖动"误判成双击 → 最大化/吸附。
            bool adopting = _heldDuringHold[idx] || _passedDown[idx];

            string action = ActionFor(idx, cfg);

            // ══ 诊断：为什么"按了没反应"完全查不出来 ══
            //
            //   用户报"长按 enter 之后左键移动、右键改长宽没反应"，
            //   而日志里只有"触发键激活/释放"，中间什么都没有 ——
            //   因为下面这条 return false 是【静默】的。
            //   FindTarget 有一长串拒绝条件（黑名单 / 类名 / 全屏 / 自身进程 / 尺寸），
            //   命中任何一条，用户体验都是"按下去没反应"，而日志一个字都不留。
            //
            //   ★★ 这里必须用 Info 而不是 Trace ★★
            //   上一版把它写成了 Log.Trace（只在 --trace 时输出），结果用户报障时
            //   默认启动根本看不到任何线索，诊断等于白加。
            //   "操作失败"是低频事件（不是每帧都发生），记 INFO 完全不影响性能，
            //   而没有它就等于在黑盒里排查。失败路径的日志等级宁高勿低。
            if (action == Actions.Nothing)
            {
                if (!quiet) Log.Info("[诊断] 鼠标键按下被忽略：该键没有绑定任何动作 键=" + idx);
                return false;
            }

            // ★ 全屏窗口能不能被选中，由两个开关共同决定 ★
            //   AllowFullscreenWindows：老开关（默认关，纯风险控制）
            //   ExitFullscreenOnTrigger：第四十一轮新开关（默认开）——
            //     它的语义是"触发时先把全屏窗口降级成普通窗口再操作"，
            //     既然我们不会在全屏状态下直接 SetWindowPos，风险窗口就只剩
            //     "退出全屏"这一次，因此它同时也放行全屏窗口的选中。
            //     否则新功能在默认配置下永远走不到（窗口在 FindTarget 就被挡了）。
            bool allowFs = cfg.AllowFullscreenWindows || cfg.ExitFullscreenOnTrigger;

            IntPtr hwnd = WindowActions.FindTarget(pt, allowFs);
            if (hwnd == IntPtr.Zero)
            {
                // 桌面 / 任务栏 / 我们自己的浮层 / 全屏窗口 / 黑名单 —— 一律放行。
                // 但必须说清楚到底为什么，否则这条日志等于没写。
                if (!quiet)
                {
                    Log.Info("[诊断] FindTarget 没找到目标，鼠标键原样放行"
                        + " 键=" + idx + " 坐标=" + pt.X + "," + pt.Y
                        + " 原因=" + WindowActions.DescribeReject(pt, allowFs));
                }
                return false;
            }

            // ★ 命中目标之后、执行动作之前：先取消它的鼠标拖拽模态 ★
            //   详见 CancelTargetMouseMode 的注释 —— 不取消的话，若这个窗口
            //   之前收到过真实的按钮按下（典型：触发键还没到阈值就先按了左键），
            //   它会一直停在"按住拖选"模态里，我们后续的移动消息全被当成拖选文字。
            CancelTargetMouseMode(hwnd);

            // ★ 第四十四轮：补发"配对的抬起"，结算幽灵按下 ★
            //   只有"收养一个已经按着的键"时才会有未配对的按下：
            //   那条 down 被我们原样放行了，而接下来这次真实的抬起将被
            //   拖动会话吞掉（目标看不到）→ 必须由我们补一条合成的抬起，
            //   否则目标程序内部永远停在"键还按着"的状态里
            //   （浏览器渲染进程靠 MouseDown/MouseUp 维护自己的按下标记，
            //    仅凭 WM_CANCELMODE 不足以复位 —— 补上这条才是完整的配对）。
            if (_passedDown[idx])
            {
                PostPairedButtonUp(hwnd, idx, pt);
                _passedDown[idx] = false;
            }

            long now = Environment.TickCount64;
            int dct = (int)Native.GetDoubleClickTime();
            if (dct <= 0) dct = 500;
            bool second = _lastDown[idx] != 0 && (now - _lastDown[idx]) <= dct && !_lastMoved[idx];
            _lastDown[idx] = now;
            _lastMoved[idx] = false;

            // 双击左键 = 最大化 / 还原。只在左键绑定为“移动”时生效，
            // 否则会和“左键=最大化”这类自定义绑定互相打架。
            // ★ 收养路径除外：见方法开头 adopting 的说明 ★
            if (idx == BtnLeft && cfg.DoubleClickLeftMaximize && action == Actions.Move && second && !adopting)
            {
                _lastDown[idx] = 0;
                _swallowUp[idx] = true;
                // ★ 第四十九轮：动作与随后的悬停框刷新打成一个闭包投递到工作线程 ★
                //   钩子线程上不写目标窗口（ShowWindow 也是同步跨线程调用，
                //   目标卡住时没有上限）。闭包保证刷新读到动作后的新矩形。
                IntPtr target = hwnd;
                Native.POINT at = pt;
                _drag.PostTargetWork(delegate ()
                {
                    WindowActions.MaximizeRestore(target);
                    RefreshHoverAfterAction(at);
                });

                // ★ 双击之后如果用户【还按着并开始拖动】，应当照常拖动 ★
                //   这正是 Windows 原生的手感：双击标题栏最大化之后继续拖，窗口跟着走。
                //   不补这一步的话，"双击 → 按住 → 移动"会完全没反应 ——
                //   它和用户报的"外框亮着但窗口不动"是同一个观感，
                //   所以一并堵住（走同一套补起机制，见 AdoptHeldButtonIfAny）。
                _heldDuringHold[idx] = true;
                return true;
            }

            // 双击右键 = 按九宫格吸附到屏幕对应位置
            // ★ 收养路径除外：见方法开头 adopting 的说明 ★
            if (idx == BtnRight && cfg.DoubleClickRightZone && action == Actions.Resize && second && !adopting)
            {
                _lastDown[idx] = 0;
                _swallowUp[idx] = true;
                IntPtr target = hwnd;
                Native.POINT at = pt;
                _drag.PostTargetWork(delegate ()
                {
                    ApplyZoneDoubleClick(target, at, cfg);
                    RefreshHoverAfterAction(at);
                });
                _heldDuringHold[idx] = true;   // 同上：双击吸附之后继续拖也要能拖动
                return true;
            }

            if (Actions.IsDragAction(action))
            {
                if (!_drag.Begin(hwnd, action == Actions.Move, idx, pt)) return false;
                return true;
            }

            _swallowUp[idx] = true;
            // ★ 收养路径下必须清掉记账（直接按下路径里它本来就是 false）★
            //   否则一次性动作会被后续【每一次鼠标移动】重新触发一遍：
            //   默认中键=最大化，表现就是“按住中键挪几下鼠标，窗口反复
            //   最大化/还原横跳”。双击分支故意保留记账（为了“双击之后
            //   继续按住要能拖动”），那条路不受这里影响。
            _heldDuringHold[idx] = false;
            // ★ 第四十九轮：一次性动作【派发】到工作线程执行 ★
            //   WindowActions.Apply 里的 SetWindowPos / ShowWindow 都是
            //   同步或准同步的跨进程调用，目标卡住时能把钩子线程拖到
            //   "整机输入无响应"（用户报障原型）。与悬停留新一起打
            //   成闭包投递，保证刷新读到动作后的新矩形。
            IntPtr actTarget = hwnd;
            string actAction = action;
            Native.POINT actAt = pt;
            _drag.PostTargetWork(delegate ()
            {
                WindowActions.Apply(actTarget, actAction);
                RefreshHoverAfterAction(actAt);
            });
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
                //
                // 触发键【已经松开】（先松键后松手）→ 什么都不做：外框在松键那一刻
                // 就被 OnTriggerDeactivated 收掉了，这里绝不能再把它亮回来，
                // 否则就是用户报的"松开所有按键后外框还在"。
                if (_trigger.Active)
                {
                    _drag.ShowHoverFrame(pt);
                }
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
