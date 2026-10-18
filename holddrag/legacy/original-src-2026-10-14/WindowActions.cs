using System;
using System.Diagnostics;
using System.Text;

namespace HoldDrag
{
    /// <summary>窗口查找与一次性动作。全部是静态方法，不持有状态。</summary>
    internal static class WindowActions
    {
        /// <summary>
        /// 这些类名不是“用户的窗口”，必须排除，否则会把桌面、任务栏、开始菜单拖走。
        /// </summary>
        private static readonly string[] ExcludedClasses =
        {
            "Progman", "WorkerW",                       // 桌面
            "Shell_TrayWnd", "Shell_SecondaryTrayWnd",  // 任务栏
            "TaskListThumbnailWnd", "ForegroundStaging",
            "Windows.UI.Core.CoreWindow",               // 开始菜单 / 搜索 / 通知中心
            "ApplicationManager_ImmersiveShellWindow",
            "MultitaskingViewFrame",                    // 任务视图
            "XamlExplorerHostIslandWindow",
            "TopLevelWindowForOverflowXamlIsland",
            "Windows.Internal.Shell.TabProxyWindow",
            "EdgeUiInputTopWndClass",
            "SysShadow", "tooltips_class32",
            "MSCTFIME UI", "IME", "Default IME"
        };

        private static readonly int OwnPid = Process.GetCurrentProcess().Id;

        /// <summary>
        /// 仅供 --selftest：允许把本进程的窗口当成拖动目标，这样才能在真实窗口上
        /// 跑端到端拖动模拟。正常运行时必须保持 false。
        /// </summary>
        internal static bool AllowOwnProcessAsTargetForTest;

        /// <summary>
        /// 仅供 --selftest：强制指定拖动目标，绕开 WindowFromPoint。
        ///
        /// 端到端用例必须【确定性地】拿到自己建的那个测试窗口。否则在无桌面、
        /// 窗口被遮挡、或多显示器坐标错位的情况下，WindowFromPoint 会命中别的窗口，
        /// 整个用例的断言就全部失去意义 —— 曾经就因为命中了一个 1648x312 的外部窗口
        /// 而假失败过。正常运行时必须保持 IntPtr.Zero。
        /// </summary>
        internal static IntPtr ForcedTargetForTest = IntPtr.Zero;

        /// <summary>
        /// 仅供 --selftest：把指定的 hwnd 当成"别的进程的窗口"。
        ///
        /// 为什么需要它：FindTarget 的核心逻辑是"命中自己 → 沿 Z 序往下穿透 →
        /// 找到第一个不是自己的窗口"。要验证这条路径，理想目标是另一个进程的窗口；
        /// 但受限环境（无桌面 / 受策略限制）里起不了外部进程。
        /// 于是给一个白名单：名单里的 hwnd 即使 PID 相同，也按"外人"处理，
        /// 这样本进程建的测试窗口就能确定性地验证穿透逻辑，且不依赖任何外部条件。
        /// 正常运行时必须保持空。
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<IntPtr> _foreignForTest
            = new System.Collections.Generic.HashSet<IntPtr>();
        private static readonly object ForeignGate = new object();

        internal static void SetForeignForTest(IntPtr h, bool on)
        {
            lock (ForeignGate)
            {
                if (on) _foreignForTest.Add(h);
                else _foreignForTest.Remove(h);
            }
        }

        /// <summary>
        /// 仅供 --selftest：强制跳过 WindowFromPoint，逼程序走兜底策略。
        ///
        /// 为什么必须有这个开关：用户实测环境里 WindowFromPoint 【稳定返回空】，
        /// 而自检环境里它一直是正常的 —— 也就是说兜底那两段代码在自检里
        /// 从来没被执行过。没被执行过的兜底等于没有兜底。
        /// 这个开关让"策略 2 / 策略 3"变成可测的：打开它，
        /// 如果还能正确找到窗口，就说明兜底真的管用。
        /// 正常运行时必须保持 false。
        /// </summary>
        internal static bool SkipWindowFromPointForTest = false;

        public static bool IsExcludedClass(string cls)
        {
            for (int i = 0; i < ExcludedClasses.Length; i++)
            {
                if (string.Equals(ExcludedClasses[i], cls, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ── 用户自定义黑名单 ────────────────────────────────────
        // 无论怎么改，总会有个别程序是拖不动的（自绘标题栏、独占呈现、
        // 或者它自己也在全局监听鼠标）。与其让用户在这些窗口上"按了没反应还以为坏了"，
        // 不如给他一个明确的排除入口。

        private static string[] _blacklist = new string[0];

        /// <summary>配置变更时调用，更新黑名单。空字符串表示不排除任何窗口。</summary>
        public static void ApplyBlacklist(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                _blacklist = new string[0];
                return;
            }

            var parts = csv.Split(new char[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new System.Collections.Generic.List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string k = parts[i].Trim();
                if (k.Length > 0) list.Add(k);
            }
            _blacklist = list.ToArray();
        }

        /// <summary>命中窗口标题或窗口类名即跳过。没有配置黑名单时零开销（连标题都不取）。</summary>
        public static bool IsBlacklisted(IntPtr h)
        {
            if (_blacklist.Length == 0) return false;

            string cls = Native.GetClassName(h);
            string title = Native.GetWindowTitle(h);
            string exe = null;          // 懒取：前面的都没命中时才去问进程，省一次系统调用

            for (int i = 0; i < _blacklist.Length; i++)
            {
                string k = _blacklist[i];
                if (cls != null && cls.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (title != null && title.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;

                // ══ ★ 进程可执行文件名也参与匹配 ★ ══
                //
                // "选一个 exe 加进来"这个功能完全靠它：
                //   用户选了 notepad.exe，存进来的关键字是 "notepad"，
                //   而记事本的窗口类名是 "Notepad"（大小写能对上）、
                //   标题是"无标题 - 记事本" —— 中文系统上标题里根本没有 notepad 这个词。
                //   不比对进程名的话，"选了程序却依然被接管"，用户只会觉得功能坏了。
                if (exe == null) exe = ProcessExeName(h);
                if (exe.Length > 0 && exe.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// 取"这扇窗口是哪个 exe 开的"，返回不含扩展名的文件名（如 "notepad"）。
        /// 取不到返回空串（提权进程的窗口可能拒绝查询，属正常情况）。
        /// </summary>
        public static string ProcessExeName(IntPtr h)
        {
            try
            {
                if (h == IntPtr.Zero) return string.Empty;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return string.Empty;
                return ProcessExeNameByPid(pid);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return string.Empty;
            }
        }

        /// <summary>按 PID 取 exe 文件名（不含扩展名）。取不到返回空串。</summary>
        public static string ProcessExeNameByPid(uint pid)
        {
            if (pid == 0) return string.Empty;

            IntPtr hp = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hp == IntPtr.Zero) return string.Empty;      // 权限不够是常态，不是错误

            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (!Native.QueryFullProcessImageNameW(hp, 0, sb, ref size)) return string.Empty;

                string full = sb.ToString();
                if (full.Length == 0) return string.Empty;

                string name = System.IO.Path.GetFileName(full);
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 4);
                return name;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return string.Empty;
            }
            finally
            {
                Native.CloseHandle(hp);
            }
        }

        /// <summary>找出光标下真正该被操作的那个顶层窗口；返回 IntPtr.Zero 表示不处理。</summary>
        public static IntPtr FindTarget(Native.POINT pt)
        {
            return FindTarget(pt, true);
        }

        /// <summary>
        /// allowFullscreen = false 时跳过"占满整个显示器"的窗口。
        ///
        /// 为什么要单独判这一类：全屏播放器 / 全屏游戏 / 独占呈现的窗口，
        /// 被第三方程序 SetWindowPos 之后往往不会重画（它们不是按窗口消息驱动的），
        /// 表现就是拖完一片黑、或者干脆卡死。给一个默认关闭的开关，需要时再打开。
        /// </summary>
        public static IntPtr FindTarget(Native.POINT pt, bool allowFullscreen)
        {
            if (ForcedTargetForTest != IntPtr.Zero) return ForcedTargetForTest;

            IntPtr h = IntPtr.Zero;
            string how;
            IntPtr cur = HitTestTopWindow(pt, out how);

            // 上限 32 层：足够穿过任何正常的窗口叠加关系，又不会在异常 Z 序下打转。
            for (int depth = 0; depth < 32 && cur != IntPtr.Zero; depth++)
            {
                h = Native.GetAncestor(cur, Native.GA_ROOT);
                if (h == IntPtr.Zero) return IntPtr.Zero;

                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);

                // 自检钩子：把指定 hwnd 当成"别的进程的窗口"，
                // 这样本进程里的测试窗口也能确定性地验证穿透逻辑，
                // 不需要真的去起一个外部进程（受限环境里起不来）。
                bool pretendForeign;
                lock (ForeignGate)
                {
                    pretendForeign = _foreignForTest.Count > 0 && _foreignForTest.Contains(h);
                }

                if (pid != (uint)OwnPid || pretendForeign) break;      // 找到别人的窗口了

                // 是我们自己的窗口（浮层 / 托盘菜单 / 设置窗口）→ 往下一层继续找。
                // 注意 AllowOwnProcessAsTargetForTest：自检里测试窗口也是本进程的，
                // 必须在这里就放行，否则 T33 之类全都会失效。
                if (AllowOwnProcessAsTargetForTest) break;

                // ★★★ 方向必须用 GW_HWNDNEXT ★★★
                //   NEXT(2) = 下面（更靠背景）  ← 我们要找的就是"被浮层遮住的那个"
                //   PREV(3) = 上面（更靠前景）
                //   原来写的是 GW_HWNDPREV，等于一路往最前方走，走到尽头拿到空句柄，
                //   FindTarget 于是返回 0 —— 这就是"长按 Enter 后左键右键没反应"。
                cur = Native.GetWindow(h, Native.GW_HWNDNEXT);
                h = IntPtr.Zero;
            }

            if (h == IntPtr.Zero) return IntPtr.Zero;
            if (!Native.IsWindow(h) || !Native.IsWindowVisible(h)) return IntPtr.Zero;

            if (IsExcludedClass(Native.GetClassName(h))) return IntPtr.Zero;

            long style = Native.GetWindowLong(h, Native.GWL_STYLE);
            if ((style & Native.WS_VISIBLE) == 0) return IntPtr.Zero;
            if ((style & Native.WS_MINIMIZE) != 0) return IntPtr.Zero;

            if (IsBlacklisted(h)) return IntPtr.Zero;

            Native.RECT r;
            if (!Native.GetWindowRect(h, out r)) return IntPtr.Zero;
            if (r.Width <= 0 || r.Height <= 0) return IntPtr.Zero;

            if (!allowFullscreen && IsFullscreen(h, r)) return IntPtr.Zero;

            return h;
        }

        /// <summary>
        /// 命中"光标底下那个窗口"，带三级兜底。
        ///
        /// ★ 为什么不能只用 WindowFromPoint ★
        ///
        /// 用户实测日志里它【稳定返回空句柄】—— 同一秒内三个不同坐标
        /// （2182,1440 / 1944,1310 / 1914,1273）全都是空。
        /// 于是 FindTarget 直接返回 0，表现就是"长按 Enter 之后左键右键完全没反应"。
        ///
        /// WindowFromPoint 返回空并不是罕见现象，已知成因至少三种：
        ///   · 提权进程去命中低完整性级别进程的窗口；
        ///   · 命中点落在带 WS_EX_TRANSPARENT 的层叠窗口上（它会被直接跳过）；
        ///   · 多显示器 / DPI 虚拟化造成的坐标空间不一致。
        ///
        /// 任何一种都会让整个程序彻底不可用，所以这里必须兜底，
        /// 而且兜底要能"用完全不同的机制"再问一遍，不能只是重试同一个 API。
        /// </summary>
        internal static IntPtr HitTestTopWindow(Native.POINT pt, out string how)
        {
            // ── 策略 1：标准 API。绝大多数情况走这条 ──
            //   （自检可以强制跳过它，用来验证下面两条兜底真的能顶上）
            IntPtr h = SkipWindowFromPointForTest ? IntPtr.Zero : Native.WindowFromPoint(pt);
            if (h != IntPtr.Zero)
            {
                how = "WindowFromPoint";
                return h;
            }

            // ── 策略 2：把桌面当父窗口，用 ChildWindowFromPointEx 再问一次 ──
            //   它和 WindowFromPoint 走的是不同的内部命中路径，
            //   很多"返回空"的场景它能给出答案。
            IntPtr desk = Native.GetDesktopWindow();
            if (desk != IntPtr.Zero)
            {
                IntPtr c = Native.ChildWindowFromPointEx(desk, pt,
                    Native.CWP_SKIPINVISIBLE | Native.CWP_SKIPTRANSPARENT);
                if (c != IntPtr.Zero && c != desk)
                {
                    how = "ChildWindowFromPointEx";
                    return c;
                }
            }

            // ── 策略 3：按 Z 序枚举顶层窗口，纯矩形判定 ──
            //   EnumWindows 是按 Z 序【从上到下】回调的，
            //   所以第一个"矩形包含该点"的就是最靠前的那个窗口。
            //   这条完全绕开命中测试 API，最笨但最可靠，是最后的保险。
            IntPtr found = IntPtr.Zero;
            try
            {
                Native.EnumWindows(delegate (IntPtr hwnd, IntPtr l)
                {
                    if (!Native.IsWindowVisible(hwnd)) return true;

                    long st = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
                    if ((st & Native.WS_MINIMIZE) != 0) return true;

                    Native.RECT r;
                    if (!Native.GetWindowRect(hwnd, out r)) return true;
                    if (r.Width <= 0 || r.Height <= 0) return true;

                    if (pt.X < r.Left || pt.X >= r.Right
                        || pt.Y < r.Top || pt.Y >= r.Bottom) return true;

                    found = hwnd;
                    return false;      // 停：Z 序里第一个命中的就是最靠前的
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }

            how = found != IntPtr.Zero ? "EnumWindowsZOrder" : "三种策略全部失败";
            return found;
        }

        /// <summary>
        /// ★诊断用：把 FindTarget 拒绝某个坐标的【具体原因】说出来。
        ///
        /// 为什么必须有这个函数：FindTarget 有一长串静默 return IntPtr.Zero，
        /// 而"按了没反应"这个现象对应的不止一种原因（桌面 / 任务栏 / 黑名单 /
        /// 全屏 / 自身浮层 / 尺寸为 0）。没有它，用户报"功能失效"时
        /// 日志里只有"触发键激活/释放"，中间一片空白，根本无从下手。
        ///
        /// 这里的判断顺序与 FindTarget 【严格保持一致】，否则报出来的原因会误导。
        /// 只在 --trace 打开时被调用，不在热路径上。
        /// </summary>
        internal static string DescribeReject(Native.POINT pt, bool allowFullscreen)
        {
            try
            {
                string how;
                IntPtr cur = HitTestTopWindow(pt, out how);

                // ★ 把"用了哪条策略"也报出来 ★
                //   如果命中的是兜底策略，说明 WindowFromPoint 在这台机器上是失效的，
                //   这是极有价值的环境信息 —— 只看"没找到目标"是看不出来的。
                string via = " [命中方式=" + how + "]";

                if (cur == IntPtr.Zero)
                {
                    // ★ 把"这个坐标到底在不在屏幕里"也一并报出来 ★
                    //
                    // 为什么关键：三种命中策略全部落空，有两种完全不同的可能 ——
                    //   (a) 坐标确实落在桌面空白处（正常，用户没对着窗口按）
                    //   (b) 坐标【跑到了虚拟桌面之外】，说明是 DPI 虚拟化造成的
                    //       坐标空间不一致（进程认为屏幕是 1920x1080，
                    //       而钩子给的是 3840x2160 的物理坐标）
                    // 这两种的处理方式天差地别，日志里必须能一眼区分，
                    // 否则又变成"按了没反应但查不出来"。
                    string extra = "";
                    try
                    {
                        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                        bool inside = pt.X >= vs.Left && pt.X < vs.Right
                                   && pt.Y >= vs.Top && pt.Y < vs.Bottom;
                        extra = " 虚拟桌面=" + vs.Left + "," + vs.Top + " " + vs.Width + "x" + vs.Height
                              + " 该点在桌面内=" + inside
                              + (inside ? "" : " ←★坐标空间不一致，疑似 DPI 虚拟化");
                    }
                    catch { }

                    return "三种命中策略全部返回空（桌面/无窗口区域）" + via + extra;
                }

                IntPtr h = IntPtr.Zero;
                for (int depth = 0; depth < 32 && cur != IntPtr.Zero; depth++)
                {
                    h = Native.GetAncestor(cur, Native.GA_ROOT);
                    if (h == IntPtr.Zero) return "取不到根窗口" + via;

                    uint pid;
                    Native.GetWindowThreadProcessId(h, out pid);
                    if (pid != (uint)OwnPid) break;

                    // ★ 与 FindTarget 保持一致：往下走用 GW_HWNDNEXT ★
                    cur = Native.GetWindow(h, Native.GW_HWNDNEXT);
                    h = IntPtr.Zero;
                }

                if (h == IntPtr.Zero)
                    return "命中的全是本进程窗口（浮层/设置窗口），往下找不到别人的窗口" + via;

                string cls = Native.GetClassName(h);
                uint pid2;
                Native.GetWindowThreadProcessId(h, out pid2);
                string title = SafeTitle(h);

                if (!Native.IsWindow(h)) return "窗口已销毁 类名=" + cls;
                if (!Native.IsWindowVisible(h)) return "窗口不可见（隐藏）类名=" + cls + " 标题=" + title;

                if (IsExcludedClass(cls)) return "类名被排除（桌面/任务栏等）类名=" + cls;

                long style = Native.GetWindowLong(h, Native.GWL_STYLE);
                if ((style & Native.WS_VISIBLE) == 0) return "窗口没有 WS_VISIBLE 类名=" + cls;
                if ((style & Native.WS_MINIMIZE) != 0) return "窗口已最小化 类名=" + cls;

                if (IsBlacklisted(h)) return "命中黑名单（不生效的窗口）标题=" + title;

                Native.RECT r;
                if (!Native.GetWindowRect(h, out r)) return "取不到窗口矩形 类名=" + cls;
                if (r.Width <= 0 || r.Height <= 0)
                    return "窗口尺寸为 0（" + r.Width + "x" + r.Height + "）类名=" + cls;

                if (!allowFullscreen && IsFullscreen(h, r))
                    return "窗口占满整个显示器（真全屏）—— 可在设置里勾选【允许全屏窗口】";

                return "未知原因（判定逻辑可能有遗漏）类名=" + cls + " 标题=" + title;
            }
            catch (Exception ex)
            {
                return "诊断本身出错: " + ex.Message;
            }
        }

        private static string SafeTitle(IntPtr h)
        {
            try
            {
                string t = Native.GetWindowTitle(h);
                if (string.IsNullOrEmpty(t)) return "(无标题)";
                if (t.Length > 30) t = t.Substring(0, 30) + "…";
                return t;
            }
            catch { return "(取标题失败)"; }
        }

        /// <summary>窗口是否占满了它所在的整个显示器（= 真全屏，而不是"最大化"）。</summary>
        public static bool IsFullscreen(IntPtr h)
        {
            Native.RECT r;
            if (!Native.GetWindowRect(h, out r)) return false;
            return IsFullscreen(h, r);
        }

        public static bool IsFullscreen(IntPtr h, Native.RECT r)
        {
            var mi = Native.GetMonitorInfoAt(new Native.POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2));
            if (mi.rcMonitor.Width <= 0 || mi.rcMonitor.Height <= 0) return false;

            // 用 rcMonitor（含任务栏区域）比较：最大化窗口只覆盖 rcWork，
            // 所以不会被误判成全屏。
            return r.Left <= mi.rcMonitor.Left
                && r.Top <= mi.rcMonitor.Top
                && r.Right >= mi.rcMonitor.Right
                && r.Bottom >= mi.rcMonitor.Bottom;
        }

        public static bool IsResizable(IntPtr h)
        {
            return (Native.GetWindowLong(h, Native.GWL_STYLE) & Native.WS_THICKFRAME) != 0;
        }

        /// <summary>
        /// ★ 第四十一轮新增：把"全屏 / 最大化"的窗口降级成一个普通窗口。★
        ///
        /// 用户原话："如果在全屏的情况下来触发按键，不管是拖动窗口还是调整窗口大小，
        ///           都会把全屏窗口变成非全屏窗口。"
        ///
        /// 为什么必须先降级、而不是直接在最大化/全屏状态下操作：
        ///   · 最大化窗口铺满工作区，右键缩放的"角区"判定几乎失效
        ///     （整个屏幕都落在中心区），而且还原之后窗口会跑掉、光标不在窗口里；
        ///   · 真全屏窗口被 SetWindowPos 之后常常不重画（全屏播放器 / 游戏不是
        ///     按窗口消息驱动的），表现是"拖完一片黑"。
        ///
        /// 做法（关键是保持手感与 Windows 原生拖动一致）：
        ///   ① 先记下光标在【当前（全屏）矩形】里的相对位置 (fx, fy)；
        ///   ② 最大化窗口 → SW_RESTORE；真全屏窗口 → 直接缩成工作区的 72%；
        ///   ③ 再把窗口摆到"光标按同一相对位置落在窗口上"的地方。
        ///      这样光标不会突然跑到窗口外面，右键缩放的角区判定也才有意义
        ///      （老代码只对"移动"做了这一步，缩放漏了 —— 那正是右键缩放
        ///       在最大化窗口上"按下去没反应/乱跳"的根因）。
        ///
        /// ★ 第四十六轮：这里的 SetWindowPos 一律带 SWP_ASYNCWINDOWPOS ★
        ///   本方法运行在【钩子线程】上（DragEngine.Begin 的调用链）。同步
        ///   SetWindowPos 要等目标窗口的窗口过程处理完 WM_WINDOWPOSCHANGING
        ///   才返回 —— 目标程序一旦不泵消息（卡死 / 弹模态框 / 渲染卡顿），
        ///   这个等待【没有上限】，钩子线程就此卡死，全系统的鼠标键盘事件
        ///   都要先经过我们这个永远不返回的钩子回调：表现就是用户报的
        ///   "整个电脑死机，鼠标能移动但所有点击无响应，只能强制重启"。
        ///   异步提交立即返回；落位由窗口自己完成，第一帧 Step 会兜底。
        ///   返回值矩形也【不能再靠回读 GetWindowRect】（异步写入尚未生效），
        ///   直接用我们写进去的值构造，语义不变。
        ///
        /// 返回 true 表示窗口已经（或本来就是）普通窗口，r 被更新为最新矩形。
        /// </summary>
        public static bool ExitFullscreenToWindowed(IntPtr h, ref Native.RECT r, Native.POINT pt,
                                                    Action<Action> offHook = null)
        {
            if (h == IntPtr.Zero) return false;

            // 所有"写目标窗口"的调用都经过这道闸：有派发口就绝不就地执行。
            // 不传派发口时（设置窗口等非钩子线程路径）才就地写。
            Action<Action> write = offHook ?? (delegate (Action w) { w(); });

            // ① 光标在当前矩形里的相对位置（夹到 0..1，防止光标在窗口外时算出离谱的值）
            double fx = r.Width > 0 ? (pt.X - r.Left) / (double)r.Width : 0.5;
            double fy = r.Height > 0 ? (pt.Y - r.Top) / (double)r.Height : 0.5;
            if (fx < 0) fx = 0; if (fx > 1) fx = 1;
            if (fy < 0) fy = 0; if (fy > 1) fy = 1;

            // ② 最大化 → 还原（Windows 会还原到"最大化之前"的矩形）
            if (Native.IsZoomed(h))
            {
                // 还原后的矩形先用 placement 预估：纯内核读，不等目标窗口过程。
                // （旧行为是 SW_RESTORE 之后回读 GetWindowRect —— ShowWindow 是
                //   同步跨线程调用，目标卡住时没有上限，第四十九轮起废弃。）
                var wp = new Native.WINDOWPLACEMENT();
                wp.length = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
                bool havePlacement = Native.GetWindowPlacement(h, ref wp)
                    && wp.rcNormalPosition.Width > 0 && wp.rcNormalPosition.Height > 0;
                if (havePlacement) r = wp.rcNormalPosition;

                // SW_RESTORE 派发到工作线程：钩子线程立即返回。还原真正落账后，
                // 由 DragEngine.Begin 传进来的 reconcile 闭包把会话矩形校正成真实值。
                write(() => Native.ShowWindow(h, Native.SW_RESTORE));

                if (!havePlacement)
                {
                    // placement 读不到的极端情形：退回就地还原 + 回读（功能不丢）
                    if (!Native.GetWindowRect(h, out r)) return false;
                }
            }

            // ③ 还原之后如果仍然铺满整个显示器（真全屏 / 最大化还原无效），
            //    主动把它缩成一个居中的普通窗口 —— 这就是用户要的"变成非全屏窗口"。
            if (IsFullscreen(h, r))
            {
                var mi = Native.GetMonitorInfoAt(pt);
                Native.RECT work = (mi.rcWork.Width > 0 && mi.rcWork.Height > 0) ? mi.rcWork : mi.rcMonitor;
                if (work.Width <= 0 || work.Height <= 0) return false;

                int w = (int)(work.Width * WindowedRatio);
                int hh = (int)(work.Height * WindowedRatio);
                if (w < 320) w = Math.Min(320, work.Width);
                if (hh < 240) hh = Math.Min(240, work.Height);

                int x = pt.X - (int)(fx * w);
                int y = pt.Y - (int)(fy * hh);

                // 夹进工作区：别把标题栏顶出屏幕（顶出去就再也抓不到了）
                if (x < work.Left) x = work.Left;
                if (y < work.Top) y = work.Top;
                if (x + w > work.Right) x = work.Right - w;
                if (y + hh > work.Bottom) y = work.Bottom - hh;
                if (x < work.Left) x = work.Left;
                if (y < work.Top) y = work.Top;

                // 这里位置和尺寸都变 → 必须用 RESIZEFLAGS（MOVETHICKBORDERS 带 SWP_NOSIZE）。
                // 带 SWP_ASYNCWINDOWPOS + 派发：见方法头注释 —— 钩子线程上不能有同步跨进程写。
                write(() => Native.SetWindowPos(h, IntPtr.Zero, x, y, w, hh,
                    Native.RESIZEFLAGS | Native.SWP_ASYNCWINDOWPOS));
                // 结果矩形直接按写入值构造（异步写回读可能还是旧位置）
                r = new Native.RECT(x, y, x + w, y + hh);
            }
            else
            {
                // ④ 普通窗口（含"最大化刚被还原"）：把光标按同一相对位置摆上去
                int nx = pt.X - (int)(fx * r.Width);
                int ny = pt.Y - (int)(fy * r.Height);
                int rw = r.Width, rh = r.Height;   // ref 参数不能进闭包，先提局部变量
                // 同上：异步提交 + 派发，钩子线程不等目标
                write(() => Native.SetWindowPos(h, IntPtr.Zero, nx, ny, rw, rh,
                    Native.MOVETHICKBORDERS | Native.SWP_ASYNCWINDOWPOS));
                r = new Native.RECT(nx, ny, nx + rw, ny + rh);
            }

            Log.Info("触发时降级为窗口化：句柄=" + h.ToInt64().ToString("X")
                + " 新矩形=" + r.Left + "," + r.Top + " " + r.Width + "x" + r.Height);
            return true;
        }

        /// <summary>真全屏窗口降级成普通窗口时占工作区的比例（宽高各取这个比例）。</summary>
        private const double WindowedRatio = 0.72;

        public static bool IsTopMost(IntPtr h)
        {
            return (Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
        }

        // ── WM_SIZING 回踢的会话级缓存 ──────────────────────────
        /// <summary>
        /// 目标窗口在本会话里对 WM_SIZING "装死"（未修改矩形）时置位，
        /// 之后整个会话不再发送。Begin() 时经 ResetSessionKickCache 重置。
        /// </summary>
        internal static bool KickUselessForSession;

        /// <summary>
        /// 目标窗口在本会话里出现过超过 <see cref="HangThresholdMs"/> 的写入停顿时置位，
        /// 之后本会话放弃实时跟手（帧写入直接跳过，只等松手落位）。
        /// Begin() 时经 ResetSessionKickCache 重置。
        /// </summary>
        internal static bool HungTargetForSession;

        /// <summary>单帧同步写入超过此毫秒数即判定这一帧目标没泵消息。</summary>
        private const double HangThresholdMs = 250;

        /// <summary>连续这么多帧都超时才判定"目标卡死、放弃实时跟手"（偶尔卡一帧不算）。</summary>
        private const int HangStreakToAbandon = 3;

        /// <summary>放弃跟手之后，每这么多帧放一帧过去试探目标是否恢复。</summary>
        private const int ProbeEveryFrames = 24;

        /// <summary>连续超时帧数（任一帧变快就清零）。</summary>
        private static int SlowFrameStreak;

        /// <summary>放弃跟手期间的探测计数。</summary>
        private static int AbandonProbe;

        /// <summary>上一次成功写入的矩形（moveOnly 路径用它省掉每帧一次 GetWindowRect）。</summary>
        private static Native.RECT _lastWriteRect;
        private static bool _lastWriteValid;

        // ── 帧写入诊断计数（--dragbench 输出；把手感问题变成数字）──
        /// <summary>本会话真正写入目标窗口的帧数。</summary>
        public static long FramesWritten;
        /// <summary>本会话"窗口已在目标位、无需写"的帧数（光标静止时免费）。</summary>
        public static long FramesNoop;
        /// <summary>本会话因目标卡死（>250ms）而放弃跟手、跳过的帧数。</summary>
        public static long FramesAbandoned;
        /// <summary>本会话累计同步写入耗时（ms）。平均耗时 = TotalWriteMs / FramesWritten。</summary>
        public static double TotalWriteMs;
        /// <summary>本会话单帧同步写入的最大耗时（ms）—— 目标真实每帧耗时上限。</summary>
        public static double MaxWriteMs;

        public static void ResetFrameStats()
        {
            FramesWritten = 0; FramesNoop = 0; FramesAbandoned = 0;
            TotalWriteMs = 0; MaxWriteMs = 0;
        }

        /// <summary>
        /// 每次拖动会话开始时重置：WM_SIZING 支持度缓存 + 卡死放弃标志 + 帧写入状态。
        /// </summary>
        internal static void ResetSessionKickCache()
        {
            KickUselessForSession = false;
            HungTargetForSession = false;
            SlowFrameStreak = 0;
            AbandonProbe = 0;
            _lastWriteValid = false;
        }

        /// <summary>
        /// 拖动中的【帧写入】：同步写。对应 AltSnap hooks.c 的 MoveWindowNow()。
        ///
        /// ══════ 为什么最终回到同步写（第十九版"异步+回执"翻车的教训）══════
        ///
        /// 物理探针（--probe）钉死的跨线程 SetWindowPos 语义：
        ///   · 异步投递后 GetWindowRect 要 ~22ms 才反映新位置；
        ///   · 连投 10 个异步帧，窗口会用 217ms 滑过全部 10 个中间位置才到最新位
        ///     —— 请求在目标队列里排队，窗口追着一串【过期位置】跑；
        ///   · 同步写每次阻塞 ~24ms（≈目标真实每帧耗时），返回时窗口精确落位，
        ///     队列里永远只有一帧。
        ///
        /// 第十九版想用"异步 + 已读回执"兼得两者，实测（--slow=20，125Hz 事件流）：
        /// 投递率 35.2%（跳过 162/250），有效更新率 43.1Hz，更新间隔最大 122ms
        /// —— 正是用户反馈的"越拖越卡、不跟手"。根因：异步反映有 ~22ms 延迟，
        /// 回执看到的一直是"未消化"，于是整帧整帧跳过，有效更新率崩塌。
        ///
        /// 同步写为何反而最优：
        ///   · 阻塞时长恰好等于目标的真实处理耗时 —— 这是背压的天然节奏：
        ///     快目标几乎不阻塞，慢目标按自己的消化能力平滑追赶；
        ///   · 阻塞期间新事件被 DragWorker 合并层抽干只留最新，写出去的
        ///     永远是光标【最新】位置，绝不会写过期位置；
        ///   · 位置是绝对推导（不累加），阻塞再久也不漂移；
        ///   · 钩子线程独立，工作线程被慢目标拴住不影响键鼠响应。
        ///
        /// AltSnap 同款结论：hooks.c:1343 主拖动路径的 SWP_ASYNCWINDOWPOS
        /// 【被注释掉】，MoveResizeWindowNow_ 用同步 SetWindowPos。
        ///
        /// &gt;250ms 停顿保险：单帧写入超过 250ms = 目标彻底卡死（不泵消息）。
        /// 本会话随即放弃实时跟手（后续帧直接跳过），保住工作线程不被无响应
        /// 窗口无限期拴住；松手时 SetDragRect 仍会同步落位。
        ///
        /// 返回 true = 本次真的写了窗口。
        /// </summary>
        public static bool SetDragRectFrame(IntPtr h, Native.RECT target)
        {
            return SetDragRectFrame(h, target, false);
        }

        /// <summary>
        /// 拖动中的一帧写入。
        ///
        /// moveOnly=true 表示本次会话是【纯平移】（ActionLeft=Move 的默认情形）：
        /// 尺寸一定不会变，于是整帧可以省掉一次跨进程的 GetWindowRect ——
        /// "已经在目标位就跳过"改用【上次写入的矩形】本地比对。
        /// 每帧少一次跨进程往返，就是少一次可能被目标线程拖住的等待。
        /// </summary>
        public static bool SetDragRectFrame(IntPtr h, Native.RECT target, bool moveOnly)
        {
            if (h == IntPtr.Zero) return false;

            // 目标已确认卡死：本会话后续帧不再尝试跟手（松手仍会落位）。
            // ★ 但不是"一棍子打死" ★ —— 每 ProbeEveryFrames 帧放一帧过去试探，
            //   目标缓过来就立刻恢复实时跟手。老实现是【一旦置位整个会话都不再写】，
            //   于是目标偶尔卡一下（浏览器渲染、GC）就会让整次拖动"完全不动、
            //   只有松手才跳过去"，用户感受到的就是"这次拖动特别卡"。
            if (HungTargetForSession)
            {
                AbandonProbe++;
                if (AbandonProbe < ProbeEveryFrames)
                {
                    FramesAbandoned++;
                    return false;
                }
                AbandonProbe = 0;      // 放这一帧过去试探
            }

            bool sizeChanged;
            if (moveOnly)
            {
                sizeChanged = false;
                if (_lastWriteValid && _lastWriteRect.Left == target.Left
                                    && _lastWriteRect.Top == target.Top)
                {
                    FramesNoop++;
                    return false;
                }
            }
            else
            {
                Native.RECT cur;
                bool haveCur = Native.GetWindowRect(h, out cur);
                sizeChanged = !haveCur
                            || (cur.Right - cur.Left) != target.Width
                            || (cur.Bottom - cur.Top) != target.Height;

                if (haveCur && !sizeChanged && cur.Left == target.Left && cur.Top == target.Top)
                {
                    FramesNoop++;
                    return false;
                }
            }

            // 最大化的窗口必须先还原，否则 SetWindowPos 的移动不会生效。
            if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);

            // 二档旗标（AltSnap 同款，均不含 ASYNC）：
            //   尺寸变了 → RESIZEFLAGS；纯平移 → MOVETHICKBORDERS。
            // 不可缩放的窗口也统一同步写：异步在慢目标上会排队追过期位置，
            // 没有例外值得为它保留（第十九版翻车教训）。
            uint flag = sizeChanged ? Native.RESIZEFLAGS : Native.MOVETHICKBORDERS;

            // WM_SIZING 回踢（会话级缓存：目标第一帧不改矩形，整个会话不再发）
            if (sizeChanged && !Native.IsZoomed(h) && !KickUselessForSession)
            {
                if (!LetWindowKickBack(h, ref target)) KickUselessForSession = true;
            }

            // ★ 同步写 + 停顿保险 ★
            // 返回时窗口已精确落在 target；阻塞时长 = 目标真实每帧耗时。
            var sw = Stopwatch.StartNew();
            Native.SetWindowPos(h, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, flag);
            sw.Stop();

            double ms = sw.Elapsed.TotalMilliseconds;
            TotalWriteMs += ms;
            if (ms > MaxWriteMs) MaxWriteMs = ms;
            FramesWritten++;

            // 记下这一帧写出去的位置：moveOnly 路径靠它做"已经在目标位就跳过"的本地判定
            _lastWriteRect = target;
            _lastWriteValid = true;

            if (ms > HangThresholdMs)
            {
                // 单帧超过 250ms：目标这一帧没泵消息（渲染卡顿 / GC / 弹了模态框）。
                //
                // ★ 必须【连续多次】才判定卡死 ★
                //   老实现是"一次超时就整个会话放弃实时跟手"，而现代应用偶尔卡一帧
                //   非常常见 —— 于是整次拖动都变成"完全不动、只有松手才跳过去"，
                //   用户感受到的是"这次拖动特别卡"。改成连续 N 次才算，并且允许恢复。
                SlowFrameStreak++;
                if (SlowFrameStreak >= HangStreakToAbandon)
                {
                    HungTargetForSession = true;
                    AbandonProbe = 0;
                    Log.Info("目标连续 " + SlowFrameStreak + " 帧写入超时（>" + (int)HangThresholdMs
                        + "ms），本次拖动放弃实时跟手（每 " + ProbeEveryFrames + " 帧探测恢复）");
                }
            }
            else
            {
                // 这一帧很快 → 目标恢复正常，立刻恢复实时跟手
                SlowFrameStreak = 0;
                if (HungTargetForSession)
                {
                    HungTargetForSession = false;
                    Log.Info("目标恢复响应，重新开始实时跟手");
                }
            }
            return true;
        }

        /// <summary>
        /// 把矩形同步写进窗口（一次性落位：拖动结束的最终定位、自检驱动）。
        ///
        /// ★ 线程纪律（第四十六轮）★
        ///   同步写要等目标窗口过程处理完才返回，目标不泵消息时没有上限。
        ///   因此本方法【只能在拖动工作线程 / 自检线程上调用】——
        ///   DragEngine.End() 早已把最终定位改派给 DragWorker（见那里的注释），
        ///   钩子线程绝不调用本方法。
        /// </summary>
        public static void SetDragRect(IntPtr h, Native.RECT target)
        {
            if (h == IntPtr.Zero) return;

            Native.RECT cur;
            bool haveCur = Native.GetWindowRect(h, out cur);

            // 尺寸没变就说明这次只是平移。
            bool sizeChanged = !haveCur
                            || (cur.Right - cur.Left) != target.Width
                            || (cur.Bottom - cur.Top) != target.Height;

            // 已经在目标位置，直接跳过。
            if (haveCur && !sizeChanged && cur.Left == target.Left && cur.Top == target.Top)
            {
                return;
            }

            // 最大化的窗口必须先还原，否则 SetWindowPos 的移动不会生效（系统会把它拉回最大化位置）。
            if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);

            uint flag;
            if (sizeChanged) flag = Native.RESIZEFLAGS;
            else if (IsResizable(h)) flag = Native.MOVETHICKBORDERS;
            else flag = Native.MOVEASYNC;

            // ── WM_SIZING 回踢（AltSnap 的 LetWindowKickBack）──
            //   缩放前先给目标一次"我要改成这个尺寸"的机会，终端模拟器一类程序
            //   靠它把尺寸吸附到自己的字符格（不然拖起来尺寸会在格子之间跳）。
            //   用 SendMessageTimeout + SMTO_ABORTIFHUNG + 32ms：目标不响应就跳过这一帧，
            //   绝不允许它把工作线程无限期堵住。
            if (sizeChanged && !Native.IsZoomed(h) && !KickUselessForSession)
            {
                // ★ 会话级缓存：目标第一帧就不响应 WM_SIZING（大多数程序不处理它），
                //   整个会话都不再发 —— 省掉每帧一次跨进程同步消息（缩放卡顿来源之一）。
                //   真正靠 WM_SIZING 吸附字符格的程序（终端类）第一帧就会改矩形，
                //   缓存不会置位，行为与 AltSnap 完全一致。
                if (!LetWindowKickBack(h, ref target))
                {
                    KickUselessForSession = true;
                }
            }

            Native.SetWindowPos(h, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height, flag);

            // 只有用了 SWP_ASYNCWINDOWPOS 才去同步重绘（AltSnap 同此）。
            // 同步写的情况下 SetWindowPos 返回时窗口已经在正确位置了，不需要再补。
            if ((flag & Native.SWP_ASYNCWINDOWPOS) != 0)
            {
                IntPtr dummy;
                Native.SendMessageTimeoutW(h, Native.WM_SYNCPAINT, IntPtr.Zero, IntPtr.Zero,
                    Native.SMTO_NORMAL | Native.SMTO_ABORTIFHUNG, 32, out dummy);
            }
        }

        /// <summary>
        /// AltSnap hooks.c 的 LetWindowKickBack()：把新矩形通过 WM_SIZING 先告诉目标窗口，
        /// 让它有机会调整（终端按字符格、播放器按 16:9 之类），并把调整后的矩形收回来。
        ///
        /// 只有真的改了尺寸才发 —— 纯平移发 WM_SIZING 是没意义的（AltSnap 里也是
        /// `if (rz.x == 0 || rz.y == 0 || lw->moveonly) return FALSE;`）。
        /// </summary>
        private static bool LetWindowKickBack(IntPtr h, ref Native.RECT r)
        {
            // 把"往哪个方向缩放"翻译成 WM_SIZING 的 wParam（WMSZ_*）。
            // 我们只知道目标矩形，用"哪条边动了"来反推。
            Native.RECT cur;
            if (!Native.GetWindowRect(h, out cur)) return false;

            bool left = r.Left != cur.Left;
            bool right = r.Right != cur.Right;
            bool top = r.Top != cur.Top;
            bool bottom = r.Bottom != cur.Bottom;

            // 只动一条边 / 两条边的情况各对应一个 WMSZ_ 常量。
            int wmsz;
            if (left && top) wmsz = 4;              // WMSZ_TOPLEFT
            else if (right && top) wmsz = 5;        // WMSZ_TOPRIGHT
            else if (left && bottom) wmsz = 7;      // WMSZ_BOTTOMLEFT
            else if (right && bottom) wmsz = 8;     // WMSZ_BOTTOMRIGHT
            else if (left) wmsz = 1;                // WMSZ_LEFT
            else if (right) wmsz = 2;               // WMSZ_RIGHT
            else if (top) wmsz = 3;                 // WMSZ_TOP
            else if (bottom) wmsz = 6;              // WMSZ_BOTTOM
            else return false;

            // WM_SIZING 的 lParam 是 RECT*，必须是【可写】的内存，目标窗口会就地改它。
            IntPtr mem = System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
            if (mem == IntPtr.Zero) return false;
            try
            {
                System.Runtime.InteropServices.Marshal.WriteInt32(mem, 0, r.Left);
                System.Runtime.InteropServices.Marshal.WriteInt32(mem, 4, r.Top);
                System.Runtime.InteropServices.Marshal.WriteInt32(mem, 8, r.Right);
                System.Runtime.InteropServices.Marshal.WriteInt32(mem, 12, r.Bottom);

                IntPtr ret;
                IntPtr ok = Native.SendMessageTimeoutW(h, Native.WM_SIZING,
                    new IntPtr(wmsz), mem,
                    Native.SMTO_NORMAL | Native.SMTO_ABORTIFHUNG, 16, out ret);
                if (ok == IntPtr.Zero || ret == IntPtr.Zero) return false;

                int nl = System.Runtime.InteropServices.Marshal.ReadInt32(mem, 0);
                int nt = System.Runtime.InteropServices.Marshal.ReadInt32(mem, 4);
                int nr = System.Runtime.InteropServices.Marshal.ReadInt32(mem, 8);
                int nb = System.Runtime.InteropServices.Marshal.ReadInt32(mem, 12);

                // 目标返回的矩形必须是"合法的、没跑偏的"，否则宁可不采纳。
                // 有些程序会返回全 0 或者离谱的值，直接照用会把窗口甩到屏幕外。
                if (nr - nl < 32 || nb - nt < 32) return false;
                if (Math.Abs(nr - nl) > 20000 || Math.Abs(nb - nt) > 20000) return false;

                r = new Native.RECT(nl, nt, nr, nb);
                return true;   // 目标真的改了矩形 → 本会话继续发 kickback
            }
            catch (Exception ex) { Log.Error(ex); return false; }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(mem); }
        }
        /// <summary>
        /// 通知目标程序“进入/退出移动缩放循环”。
        /// 终端模拟器一类程序靠 WM_ENTERSIZEMOVE 切到字符网格对齐模式，
        /// 不发的话它们在被拖动时会抖得很难看。AltSnap 的 NotifySizeMoveStaEnd() 同此。
        /// </summary>
        public static void NotifySizeMove(IntPtr h, bool start)
        {
            if (h == IntPtr.Zero) return;
            try
            {
                Native.PostMessageW(h, start ? Native.WM_ENTERSIZEMOVE : Native.WM_EXITSIZEMOVE,
                    IntPtr.Zero, IntPtr.Zero);
                Native.NotifyWinEvent(
                    start ? Native.EVENT_SYSTEM_MOVESIZESTART : Native.EVENT_SYSTEM_MOVESIZEEND,
                    h, 0, 0);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        /// <summary>
        /// 把矩形写进窗口（一次性落位，用在吸附 / 居中这类最终定位上）。
        /// 故意带 SWP_ASYNCWINDOWPOS：这个调用可能发生在钩子线程上，
        /// 目标程序万一卡死，同步等待会把钩子一起拖死。
        /// </summary>
        public static void SetRect(IntPtr h, Native.RECT r)
        {
            if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            Native.SetWindowPos(h, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height,
                Native.RESIZEFLAGS | Native.SWP_ASYNCWINDOWPOS);
        }

        // ── 一次性动作 ─────────────────────────────────────────

        public static void MaximizeRestore(IntPtr h)
        {
            if (Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_RESTORE);
            else Native.ShowWindow(h, Native.SW_MAXIMIZE);
        }

        public static void Minimize(IntPtr h)
        {
            Native.ShowWindow(h, Native.SW_MINIMIZE);
        }

        public static void CloseWindow(IntPtr h)
        {
            Native.PostMessageW(h, Native.WM_SYSCOMMAND, new IntPtr(Native.SC_CLOSE), IntPtr.Zero);
        }

        public static void Lower(IntPtr h)
        {
            Native.SetWindowPos(h, Native.HWND_BOTTOM, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
        }

        public static void ToggleTopMost(IntPtr h)
        {
            IntPtr after = IsTopMost(h) ? Native.HWND_NOTOPMOST : Native.HWND_TOPMOST;
            // ★ 第四十六轮：带 SWP_ASYNCWINDOWPOS；第四十九轮：调用点一律派发 ★
            //   本方法的调用链起点是钩子线程（HandleButtonDown → Apply → 这里）。
            //   同步 SetWindowPos 要等目标窗口过程返回，目标不泵消息时【没有上限】——
            //   钩子线程卡死 = 整机输入无响应（用户报过的"死机只能重启"）。
            //   第四十九轮起，钩子线程不直接调本方法：Coordinator 把"动作 + 悬停刷新"
            //   打成闭包经 DragEngine.PostTargetWork 派发到拖动工作线程再执行。
            //   SWP_ASYNCWINDOWPOS 保留：实测目标正压着位置更新不放时，连异步提交
            //   都要排它的位置锁，能省一点等待是一点；置顶状态由窗口管理器落位，
            //   晚几毫秒完全无感。
            Native.SetWindowPos(h, after, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
        }

        /// <summary>尺寸不变，移动到所在显示器工作区的正中。</summary>
        public static void Center(IntPtr h)
        {
            Native.RECT r;
            if (!Native.GetWindowRect(h, out r)) return;
            var mi = Native.GetMonitorInfoAt(new Native.POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2));
            if (mi.rcWork.Width <= 0) return;

            int x = mi.rcWork.Left + (mi.rcWork.Width - r.Width) / 2;
            int y = mi.rcWork.Top + (mi.rcWork.Height - r.Height) / 2;
            Native.SetWindowPos(h, IntPtr.Zero, x, y, r.Width, r.Height,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
        }

        /// <summary>把窗口吸附到指定屏幕区域。</summary>
        public static void ApplySnap(IntPtr h, SnapZone zone, Native.RECT work)
        {
            if (zone == SnapZone.None) return;

            if (zone == SnapZone.Maximize)
            {
                if (!Native.IsZoomed(h)) Native.ShowWindow(h, Native.SW_MAXIMIZE);
                return;
            }
            SetRect(h, Zones.RectOf(zone, work));
        }

        public static void Apply(IntPtr h, string action)
        {
            switch (action)
            {
                case Actions.Maximize: MaximizeRestore(h); break;
                case Actions.Minimize: Minimize(h); break;
                case Actions.Close: CloseWindow(h); break;
                case Actions.Lower: Lower(h); break;
                case Actions.AlwaysOnTop: ToggleTopMost(h); break;
                case Actions.Center: Center(h); break;
            }
        }

        /// <summary>
        /// 强行把窗口提到最前。
        ///
        /// 直接调 SetForegroundWindow 通常会失败——Windows 只允许“当前前台进程”或
        /// “刚收到输入事件的进程”抢前台，而我们只是个后台钩子程序。
        /// 标准做法是临时把自己的输入队列挂到前台线程上，借它的资格完成调用。
        /// </summary>
        public static void ForceForeground(IntPtr h)
        {
            try
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg == h) return;

                uint fgThread = Native.GetWindowThreadProcessId(fg, out _);
                uint myThread = Native.GetCurrentThreadId();

                bool attached = false;
                if (fgThread != 0 && fgThread != myThread)
                {
                    attached = Native.AttachThreadInput(fgThread, myThread, true);
                }

                try
                {
                    Native.SetWindowPos(h, Native.HWND_TOP, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                    Native.SetForegroundWindow(h);
                }
                finally
                {
                    if (attached) Native.AttachThreadInput(fgThread, myThread, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }
}
