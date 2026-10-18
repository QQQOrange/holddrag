using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace HoldDrag
{
    /// <summary>钩子事件的接收方。返回 true 表示吞掉该事件（不再向下传递）。</summary>
    internal interface IInputSink
    {
        bool OnKeyboard(bool down, ref Native.KBDLLHOOKSTRUCT kb);
        bool OnMouse(int msg, ref Native.MSLLHOOKSTRUCT ms);
    }

    /// <summary>
    /// 全局低级键鼠钩子。
    ///
    /// 三条硬性纪律（违反任意一条都会导致钩子被系统静默摘除）：
    ///   1) 回调里绝不做阻塞式 SendMessage / 模态对话框；
    ///   2) 任何异常都必须就地吞掉，绝不能抛回非托管调用方；
    ///   3) 注入产生的事件（INJECTED）必须原样放过，否则自己触发自己会死循环。
    /// </summary>
    internal sealed class HookEngine : IDisposable
    {
        /// <summary>
        /// 钩子线程私事：就地重装【鼠标】钩子，把本进程的钩子重新顶到链头。
        ///
        /// 为什么需要（wgesture 抢占右键的场景）：
        ///   低级钩子按【安装时间 LIFO】调用 —— 后装的最先被调用。用户机器上
        ///   wgesture（中完整性级别）的鼠标钩子装在我们后面，于是每次右键按下
        ///   先经过它：wgesture 把右键当自己的手势键吞掉，我们的钩子根本看不到
        ///   那条 down —— 表现就是"长按 Enter 后左键能拖、右键没反应"。
        ///   左键不受影响，是因为 wgesture 只吃右键。
        ///
        ///   解法：触发键激活的那一刻（用户还没点击鼠标），把鼠标钩子卸载重装。
        ///   新钩子按 LIFO 排到链头，此后本会话的每个鼠标事件都先过我们这道。
        ///   wgesture 再也看不到右键 down，它的手势状态不会被锁存。
        ///
        /// 为什么"就地"重装而不是 Uninstall+Install 整套：
        ///   · 整套重装要重建线程，键盘钩子也会有一个收不到事件的窗口期；
        ///   · 就地重装只动鼠标钩子（卸载→安装都发生在钩子线程自己的消息循环里，
        ///     两个调用之间隔一次 GetMessage 返回，亚毫秒级），键盘钩子全程在场。
        ///   · 安装必须在钩子线程上做：LL 钩子的回调运行在【安装它的线程】，
        ///     装到别的线程上那个线程就得跟着泵消息 —— 所以这里用线程消息
        ///     让钩子线程自己动手。
        /// </summary>
        public const uint WM_REHOOK_MOUSE = 0x8000 + 8;   // WM_APP+8（私有消息，只走线程队列）

        private readonly IInputSink _sink;

        // 委托必须存字段，否则会被 GC 回收，非托管侧留野指针直接崩进程
        private readonly Native.HookProc _kbProc;
        private readonly Native.HookProc _msProc;

        private IntPtr _kbHook = IntPtr.Zero;
        private IntPtr _msHook = IntPtr.Zero;

        // ══════ ★★★ 钩子必须住在自己的专用线程上 ★★★ ══════
        //
        //   以前钩子直接装在 UI 线程（TrayContext 构造于 Application.Run 之前）。
        //   低级钩子的回调在【安装它的线程】上被调用，而 UI 线程同时还要跑
        //   WinForms 消息循环、托盘菜单、设置窗、WinForms Timer、GC ……
        //   它一忙（哪怕几毫秒），全系统的鼠标/键盘事件处理就跟着排队 ——
        //   拖动窗口时表现就是"突然几帧没跟上，然后突然追上"。
        //
        //   微软文档明确要求：低级钩子回调"should run the hooks on a dedicated
        //   thread"。AltSnap 的 UI 线程几乎是零负担的 C 消息循环所以没这毛病，
        //   .NET 的 UI 线程重得多，必须隔离。
        //
        //   本线程唯一的职责就是泵消息（LL 钩子要求安装线程泵消息），
        //   泵的频率极高、做的事为零 —— 回调再快也快不过它消化。
        private Thread _hookThread;
        private uint _hookThreadId;
        private readonly ManualResetEventSlim _hookReady = new ManualResetEventSlim(false);
        private volatile bool _hookQuit;

        // 鼠标钩子最后一次"真正看到"的光标位置。
        // 看门狗拿它和 GetCursorPos() 比对，用来判断钩子是不是被系统悄悄摘掉了。
        // 用两个 int 而不是 POINT 结构：跨线程读写不需要锁，最坏只是多轮询一次。
        private volatile int _seenX;
        private volatile int _seenY;
        private volatile bool _seenValid;

        public int LastError { get; private set; }

        public bool KeyboardInstalled { get { return _kbHook != IntPtr.Zero; } }
        public bool MouseInstalled { get { return _msHook != IntPtr.Zero; } }

        /// <summary>钩子最后一次看到的光标位置。_seenValid=false 表示还没收到过任何鼠标事件。</summary>
        public bool TryGetLastSeen(out int x, out int y)
        {
            x = _seenX;
            y = _seenY;
            return _seenValid;
        }

        /// <summary>看门狗重装过几次。仅供自检与日志观察。</summary>
        public int ReinstallCount { get; private set; }

        public HookEngine(IInputSink sink)
        {
            _sink = sink;
            _kbProc = KeyboardCallback;
            _msProc = MouseCallback;
        }

        public bool Install()
        {
            if (_kbHook != IntPtr.Zero && _msHook != IntPtr.Zero) return true;

            LastError = 0;
            _hookQuit = false;
            _hookReady.Reset();

            _hookThread = new Thread(HookThreadMain);
            _hookThread.IsBackground = true;
            // 钩子回调越快返回越好：AboveNormal 保证系统鼠标事件第一时间被消化
            _hookThread.Priority = System.Threading.ThreadPriority.AboveNormal;
            _hookThread.Name = "HoldDrag.HookThread";
            _hookThread.Start();

            // 等钩子在线程上装好（装不好说明队列/权限有问题，照旧报告错误码）
            if (!_hookReady.Wait(3000))
            {
                Log.Error("钩子线程启动超时");
            }

            return KeyboardInstalled && MouseInstalled;
        }

        private void HookThreadMain()
        {
            try
            {
                _hookThreadId = Native.GetCurrentThreadId();

                // 先 Peek 一次强制建消息队列：LL 钩子要求安装线程泵消息。
                Native.MSG tmp;
                Native.PeekMessageW(out tmp, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE);

                IntPtr hMod = Native.GetModuleHandleW(null);

                if (_kbHook == IntPtr.Zero)
                {
                    _kbHook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _kbProc, hMod, 0);
                    if (_kbHook == IntPtr.Zero)
                    {
                        LastError = Marshal.GetLastWin32Error();
                        Log.Error("键盘钩子安装失败，Win32 错误码 " + LastError);
                    }
                }

                if (_msHook == IntPtr.Zero)
                {
                    _msHook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, _msProc, hMod, 0);
                    if (_msHook == IntPtr.Zero)
                    {
                        LastError = Marshal.GetLastWin32Error();
                        Log.Error("鼠标钩子安装失败，Win32 错误码 " + LastError);
                    }
                    else
                    {
                        // 记下当前光标当作"最后看到的位置"的初值，
                        // 否则看门狗第一次跑就会把"用户还没动鼠标"误判成"钩子死了"。
                        Native.POINT p;
                        if (Native.GetCursorPos(out p))
                        {
                            _seenX = p.X;
                            _seenY = p.Y;
                            _seenValid = true;
                        }
                    }
                }

                _hookReady.Set();

                // 纯消息泵：这个线程除了消化钩子回调什么都不做。
                // GetMessage 阻塞等待，不占 CPU。
                while (!_hookQuit)
                {
                    Native.MSG msg;
                    int got = Native.GetMessageW(out msg, IntPtr.Zero, 0, 0);
                    if (got <= 0) break;

                    // 激活时抢占钩子链头：就地重装鼠标钩子（详见 WM_REHOOK_MOUSE 的注释）
                    if (msg.message == WM_REHOOK_MOUSE)
                    {
                        ReinstallMouseOnThisThread();
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _hookReady.Set();
            }
        }

        /// <summary>
        /// 在钩子线程上就地重装鼠标钩子（卸载 → 安装，两步之间只隔一次 GetMessage 返回）。
        /// </summary>
        private void ReinstallMouseOnThisThread()
        {
            if (_hookQuit) return;   // 正在退出：别又装一个回来

            try
            {
                if (_msHook != IntPtr.Zero)
                {
                    Native.UnhookWindowsHookEx(_msHook);
                    _msHook = IntPtr.Zero;
                }

                IntPtr hMod = Native.GetModuleHandleW(null);
                _msHook = Native.SetWindowsHookExW(Native.WH_MOUSE_LL, _msProc, hMod, 0);
                if (_msHook == IntPtr.Zero)
                {
                    LastError = Marshal.GetLastWin32Error();
                    Log.Error("抢占链头：鼠标钩子重装失败，Win32 错误码 " + LastError);
                    return;
                }

                // 刷新"最后看到的位置"基准，免得看门狗拿旧基准误判钩子已死
                Native.POINT p;
                if (Native.GetCursorPos(out p))
                {
                    _seenX = p.X;
                    _seenY = p.Y;
                    _seenValid = true;
                }

                ReinstallCount++;
                Log.Info("鼠标钩子已就地重装并顶到钩子链头部（激活时抢占，第三方手势工具不再先拿到鼠标键）");
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        /// <summary>
        /// 抢占钩子链头部：把鼠标钩子重装到最前面。
        /// 由 Coordinator 在触发键激活时调用（见 <see cref="WM_REHOOK_MOUSE"/> 的场景说明）。
        /// 钩子线程不在（尚未安装 / 已拆卸）时退化成整套 Reinstall。
        /// </summary>
        public bool ReinstallMouseForChainPriority()
        {
            uint tid = _hookThreadId;
            if (tid != 0 && _msHook != IntPtr.Zero)
            {
                return Native.PostThreadMessageW(tid, WM_REHOOK_MOUSE, IntPtr.Zero, IntPtr.Zero);
            }
            return Reinstall();
        }

        /// <summary>
        /// 卸载后重新安装。给看门狗用。
        ///
        /// 为什么需要：从 Win7 起，低级钩子的回调如果超过 LowLevelHooksTimeout
        /// （默认 300ms 左右），系统会把它【静默摘除】—— 不报错、不通知，任何 API 都查不出来。
        /// 长时间睡眠、锁屏、切显卡驱动、目标程序卡死，都会触发。
        /// AltSnap 的做法（hooks.c 的 REHOOK_TIMER）是：光标动了但钩子没收到事件 => 钩子已死 => 重装。
        /// </summary>
        public bool Reinstall()
        {
            Log.Info("看门狗判定键鼠钩子已失效，正在重新安装");
            Uninstall();

            _seenValid = false;
            bool ok = Install();
            ReinstallCount++;
            Log.Info("钩子重装" + (ok ? "成功" : "仍然失败，错误码 " + LastError));
            return ok;
        }

        // ══════ 钩子回调卡死观测（第四十六轮）══════
        //
        //   症状："整个电脑死机，鼠标还能移动，但所有点击无响应，只能强制重启"。
        //   根因绝大多数是：钩子回调里出现了【没有上限的同步跨进程调用】
        //   （同步 SetWindowPos / SendMessage 到一个不泵消息的窗口）——
        //   回调不返回，之后全系统的键鼠事件都要先经过它。
        //
        //   我们无法从外部中止一个卡死的内核调用，但至少要让它在日志里留证：
        //   两个回调进出时各打一个时间戳，看门狗周期性地看一眼"最久卡了多久"。
        //   下次用户再遇到，日志里会有一条明确的 ERROR 指出卡点，不用再猜。
        // C# 的 volatile 不支持 long（只能修饰引用类型和 int/short/byte 等），
        // 这里与 _seenCount 保持同一套写法：Interlocked 收发，x64 上长整型读写本就原子。
        private long _callbackSinceTicks;   // 0 = 当前不在回调里

        /// <summary>钩子回调已连续卡住的毫秒数。0 = 没有卡住。</summary>
        public double CallbackStalledMs
        {
            get
            {
                long t = Interlocked.Read(ref _callbackSinceTicks);
                if (t == 0) return 0;
                double ms = (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency;
                return ms < 0 ? 0 : ms;
            }
        }

        private void EnterCallback()
        {
            Interlocked.Exchange(ref _callbackSinceTicks, Stopwatch.GetTimestamp());
        }

        private void LeaveCallback()
        {
            Interlocked.Exchange(ref _callbackSinceTicks, 0);
        }

        /// <summary>
        /// 记录钩子"看到了"这次鼠标事件。任何线程写、看门狗读。
        ///
        /// ★★★ 关键：除了位置，还要记一个【事件计数】★★★
        /// 原来的看门狗只比对光标位置（_seenX/_seenY），于是有两种情况会被误判成
        /// "钩子已死"：
        ///   1) 光标在同一个像素内来回抖动 —— 位置没变，但事件其实收到了；
        ///   2) 用户移动了鼠标但被某个窗口吞掉（鼠标穿透、抓取中），
        /// 而钩子其实一直活着。
        /// 一旦误判 → 重装 → _seenValid 被清掉 → 下轮重新取基准 → 再次误判，
        /// 于是变成"看门狗自己把自己打成高频重装"，钩子被反复拆装，
        /// 表现出来就是【鼠标事件时有时无、功能像是随机失灵】。
        ///
        /// 事件计数才是"钩子还活着"的可靠证据：只要回调还在被调用，计数就一定在涨。
        /// </summary>
        private void MarkSeen(Native.MSLLHOOKSTRUCT ms)
        {
            _seenX = ms.pt.X;
            _seenY = ms.pt.Y;
            _seenValid = true;
            Interlocked.Increment(ref _seenCount);
        }

        /// <summary>钩子回调被调用过的总次数。看门狗用它在两个轮询之间做交叉验证。</summary>
        public long SeenCount { get { return Interlocked.Read(ref _seenCount); } }

        private long _seenCount;

        public void Uninstall()
        {
            if (_kbHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_kbHook);
                _kbHook = IntPtr.Zero;
            }
            if (_msHook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_msHook);
                _msHook = IntPtr.Zero;
            }

            // 让钩子线程退出（UnhookWindowsHookEx 允许从任意线程调用）。
            if (_hookThreadId != 0)
            {
                Native.PostThreadMessageW(_hookThreadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                _hookThreadId = 0;
            }
        }

        public void Dispose()
        {
            Uninstall();
        }

        private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            EnterCallback();
            try
            {
                if (nCode >= 0 && _sink != null)
                {
                    var kb = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);

                    // ══════ ★★★ 同 MouseCallback：只看"是不是我们自己补发的" ══════
                    //
                    // 原来这里同样用 LLKHF_INJECTED 一刀切，但那是"由【某个进程】合成"，
                    // 不等于"我们自己合成"。RDP 键盘重定向、虚拟机输入穿透、
                    // AutoHotkey / PowerToys / 某些输入法都会因此被整体丢弃 ——
                    // 症状就是"触发键按了没反应"或"打完字少了字母"。
                    //
                    // 只认 MagicExtraInfo 这一个精确标记就够了：
                    // TriggerState 补发原按键时正是用它打标记的。
                    bool ours = kb.dwExtraInfo == MagicExtraInfo;
                    if (!ours)
                    {
                        int msg = unchecked((int)wParam.ToInt64());
                        bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                        bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;

                        if ((down || up) && _sink.OnKeyboard(down, ref kb))
                        {
                            return new IntPtr(1);
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
                LeaveCallback();
            }

            return Native.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            EnterCallback();
            try
            {
                if (nCode >= 0 && _sink != null)
                {
                    var ms = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);

                    // ══════ ★★★ 坐标换算：这里是唯一正确的换算点 ★★★ ══════
                    //
                    // 低级鼠标钩子给的 MSLLHOOKSTRUCT.pt 是【物理像素坐标】。
                    // 如果进程是 DPI 不感知的（用户实测就是这种环境），
                    // 下游的 FindTarget / GetWindowRect / SetWindowPos 全都工作在
                    // 一个被系统缩放过的坐标空间里 —— 直接拿物理坐标去问必然错位，
                    // WindowFromPoint 返回空句柄，整个核心功能失效。
                    // （用户日志：坐标 2182,1440，而进程逻辑宽度只有 1920，铁证。）
                    //
                    // ★ 为什么放在这里而不是 Coordinator.OnMouse ★
                    //   OnMouse 也会被自检和内部逻辑直接调用，那些调用传进来的
                    //   已经是逻辑坐标；在那里无条件换算会把值再乘一次比例，
                    //   表现是"拖动只走了一半、窗口追不上光标"。
                    //   而本回调是"系统物理坐标"进入本进程的【唯一入口】，
                    //   在边界上换算一次，下游拿到的就都是同一个坐标空间。
                    //
                    // ★ 必须在 MarkSeen 之前 ★
                    //   看门狗会拿这里记下的位置和 GetCursorPos() 比对，
                    //   而 GetCursorPos 对不感知进程返回的是逻辑坐标 ——
                    //   两者必须在同一空间，否则看门狗会一直误判"钩子已死"。
                    //
                    // 进程是 DPI 感知时（绝大多数情况）这是恒等变换，开销为零。
                    ms.pt = CoordSpace.ToProcess(ms.pt);

                    // 先记录"钩子看到了这个事件" —— 必须放在注入过滤之前：
                    // 我们自己的 SendInput 补发事件也是钩子确实活着才能收到的证据。
                    MarkSeen(ms);

                    int msg = unchecked((int)wParam.ToInt64());

                    // ── 探针：只在 --trace 下记录按键事件，为的是让"事件到底卡在哪一步"
                    //    变得可观测。以前这里是纯静默的，一卡住就完全无从下手。
                    //    只记按键（WM_LBUTTONDOWN/UP、RBUTTONDOWN/UP、MIDDLEDOWN/UP），
                    //    不记移动，否则拖动时会刷屏。
                    if (Log.Verbose)
                    {
                        if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_LBUTTONUP
                            || msg == Native.WM_RBUTTONDOWN || msg == Native.WM_RBUTTONUP
                            || msg == Native.WM_MBUTTONDOWN || msg == Native.WM_MBUTTONUP)
                        {
                            Log.Trace("[探针] 钩子收到按键 msg=" + msg + " flags=0x" + ms.flags.ToString("X")
                                + " extra=" + ms.dwExtraInfo.ToInt64().ToString("X"));
                        }
                    }

                    // ═══════ ★★★ 这里就是"触发后左键右键功能没了"的根因 ═══════
                    //
                    // 原来判据是：
                    //     bool injected = (ms.flags & LLMHF_INJECTED) != 0
                    //                  || (ms.flags & LLMHF_LOWER_IL_INJECTED) != 0;
                    //     if (!injected) { _sink.OnMouse(msg, ref ms); }
                    //
                    // 问题：LLMHF_INJECTED 的含义是"这个事件是由【某个进程】用 SendInput
                    // 合成出来的"，而【不只是】"我们自己合成的"。
                    // 于是所有外部来源的合成事件 —— 远程桌面 / RDP 客户端重定向、
                    // 虚拟机（VMware/VirtualBox/Hyper-V）的鼠标穿透、
                    // 某些输入法和自动化工具、以及用户自己在用 AutoHotkey/PowerToys ——
                    // 全都被当成"自己补发的"直接丢掉。
                    //
                    // 真实表现就是：日志里"触发键激活"有，但没有"开始移动"，
                    // 因为左键按下事件压根没进到 Coordinator（用户原话：
                    //   "触发阈值后左键和右键的功能没了"）。
                    //
                    // 自检之前测不出来，是因为所有端到端用例都【直接调 Coordinator.OnMouse】，
                    // 绕开了钩子的这段过滤；新增的 T43 走真实 SendInput 才立刻暴露。
                    //
                    // 正解：只过滤【我们自己】用 MagicExtraInfo 标记的那批补发事件。
                    // 别的一律放行 —— 哪怕它也是合成的。
                    bool ours = (IntPtr)ms.dwExtraInfo == MagicExtraInfo;
                    if (!ours)
                    {
                        if (_sink.OnMouse(msg, ref ms))
                        {
                            return new IntPtr(1);
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
                LeaveCallback();
            }

            return Native.CallNextHookEx(_msHook, nCode, wParam, lParam);
        }

        /// <summary>我们自己合成事件时写入的标记，用于让注入事件在回调里被识别。</summary>
        public static readonly IntPtr MagicExtraInfo = new IntPtr(0x486F6C64); // "Hold"
    }
}
