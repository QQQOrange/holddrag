using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HoldDrag
{
internal static class Native
    {

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
            public override string ToString() { return X + "," + Y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
            public bool IsEmpty { get { return Width <= 0 || Height <= 0; } }
            public override string ToString() { return Left + "," + Top + " " + Width + "x" + Height; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

    

        // ── 钩子 ────────────────────────────────────────────────
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        // ── 键盘 / 鼠标消息 ─────────────────────────────────────
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_WINDOWPOSCHANGING = 0x0046;   // 位置/尺寸【将要】改变（改之前，可拦截）
        public const int WM_WINDOWPOSCHANGED = 0x0047;    // 位置/尺寸【已经】改变
        public const int WM_LBUTTONDOWN = 0x0201;

        /// <summary>
        /// 请目标窗口"结束当前鼠标拖拽模态状态"（结束拖选、释放它的鼠标捕获）。
        /// 详见 Coordinator.CancelTargetMouseMode 的注释。
        /// </summary>
        public const int WM_CANCELMODE = 0x001F;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;
        public const int WM_XBUTTONDOWN = 0x020B;
        public const int WM_XBUTTONUP = 0x020C;
        public const int WM_MOUSEHWHEEL = 0x020E;

        /// <summary>鼠标消息 wParam 里的按键掩码（补发"配对的抬起"时要如实报告其余按键的状态）。</summary>
        public const int MK_LBUTTON = 0x0001;
        public const int MK_RBUTTON = 0x0002;
        public const int MK_MBUTTON = 0x0010;

        public const int WM_SYSCOMMAND = 0x0112;
        public const int WM_CLOSE = 0x0010;

        public const int SC_CLOSE = 0xF060;
        public const int SC_MAXIMIZE = 0xF030;
        public const int SC_MINIMIZE = 0xF020;
        public const int SC_RESTORE = 0xF120;

        // ── 钩子结构体标志位 ────────────────────────────────────
        public const uint LLKHF_EXTENDED = 0x01;
        public const uint LLKHF_INJECTED = 0x10;
        public const uint LLKHF_LOWER_IL_INJECTED = 0x02;
        public const uint LLMHF_INJECTED = 0x01;
        public const uint LLMHF_LOWER_IL_INJECTED = 0x02;

        // ── SetWindowPos ────────────────────────────────────────
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_NOSENDCHANGING = 0x0400;
        public const uint SWP_NOCOPYBITS = 0x0100;      // 禁止系统把旧窗口内容复制到新位置，消除拖动残影
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;    // 异步提交，SetWindowPos 立即返回，不阻塞工作线程

        // ══════ 严格照抄 AltSnap hooks.c 的三档标志 ══════
        //
        //   #define RESIZEFLAG       SWP_NOZORDER|SWP_NOOWNERZORDER|SWP_NOACTIVATE
        //   #define MOVETHICKBORDERS RESIZEFLAG|SWP_NOSIZE
        //   #define MOVEASYNC        MOVETHICKBORDERS|SWP_ASYNCWINDOWPOS
        //
        // ★ 旗标档案（见 WindowActions.SetDragRectFrame 的完整说明）★
        //
        //   【现行策略：帧写入与最终定位一律同步写】（AltSnap MoveWindowNow 同款）
        //   · 同步写返回时窗口已精确落位，队列里永远只有一帧；
        //   · 阻塞时长 = 目标真实每帧耗时，是最天然的背压节奏；
        //   · 阻塞期间新事件被 DragWorker 合并层抽干只留最新，永不写过期位置。
        //
        //   历史教训（两条路都实测翻过车，物理探针 --probe 钉死）：
        //   · 裸异步：请求全排进目标队列，窗口 217ms 滑过 10 个过期位置；
        //   · 异步 + 已读回执（第十九版）：异步反映有 ~22ms 延迟，回执永远看到
        //     "未消化"，125Hz 事件流被砍成 43.1Hz，更新间隔最大 122ms ——
        //     正是用户反馈的"越拖越卡、不跟手"。
        //
        //   MOVEASYNC 仅保留给：松手最终定位中的【不可缩放】窗口
        //   （AltSnap 同款：异步 + SendMessageTimeout(WM_SYNCPAINT) 屏障）。
        public const uint RESIZEFLAGS = SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE;
        public const uint MOVETHICKBORDERS = SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_NOSIZE;
        public const uint MOVEASYNC = MOVETHICKBORDERS | SWP_ASYNCWINDOWPOS;

        // ── 尺寸移动循环 / 同步重绘 ─────────────────────────────
        public const int WM_ENTERSIZEMOVE = 0x0231;
        public const int WM_EXITSIZEMOVE = 0x0232;
        public const int WM_SIZING = 0x0214;        // 缩放前问目标"这个尺寸行不行"（AltSnap LetWindowKickBack）
        public const int WM_SYNCPAINT = 0x0088;

        public const uint SMTO_NORMAL = 0x0000;
        public const uint SMTO_ABORTIFHUNG = 0x0002;

        public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
        public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;

        // ── 线程消息队列（工作线程用）───────────────────────────
        public const uint PM_NOREMOVE = 0x0000;
        public const uint PM_REMOVE = 0x0001;
        public const int WM_QUIT = 0x0012;

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        // ── ShowWindow ──────────────────────────────────────────
        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_MAXIMIZE = 3;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;

        // ── 窗口样式 ────────────────────────────────────────────
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const long WS_VISIBLE = 0x10000000L;
        public const long WS_CAPTION = 0x00C00000L;
        public const long WS_THICKFRAME = 0x00040000L;
        public const long WS_MINIMIZE = 0x20000000L;
        public const long WS_MAXIMIZE = 0x01000000L;
        public const long WS_SYSMENU = 0x00080000L;

        public const long WS_EX_LAYERED = 0x00080000L;
        public const long WS_EX_TRANSPARENT = 0x00000020L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;
        public const long WS_EX_TOPMOST = 0x00000008L;

        public const uint GA_PARENT = 1;
        public const uint GA_ROOT = 2;
        public const uint GA_ROOTOWNER = 3;

        public const uint MONITOR_DEFAULTTONULL = 0;
        public const uint MONITOR_DEFAULTTOPRIMARY = 1;
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        public const uint LWA_COLORKEY = 0x00000001;
        public const uint LWA_ALPHA = 0x00000002;

    public const uint INPUT_MOUSE = 0;
        public const uint INPUT_KEYBOARD = 1;

        // ── 鼠标事件标志（SendInput）──────────────────────────────
        // 缺了这些就没法在自检里注入真实鼠标事件，
  // 而"直接调Coordinator.OnMouse"这条测法会绕开钩子的注入过滤，
        // 也就永远测不到"真实鼠标事件到底有没有进到调度中心"这类问题。
    public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        /// <summary>
        /// 绝对坐标覆盖【整个虚拟桌面】（多显示器），而不是只映射到主屏。
        /// 必须配合 MOUSEEVENTF_ABSOLUTE 使用。
        /// </summary>
        public const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    //别名：供自检注入使用。
      //
        // 为什么起别名：`Native.MOUSEEVENTF_RIGHTDOWN` 这种写法在跨行传参时
        // 会被编译器误判成限定名的一部分（"X.Y.Z"里的 Y 被当成类型），
      // 于时报 CS0117 "Native 未包含 MOUSEEVENTF_RIGHTDOWN"。
     // 这个报错极具迷惑性：常量明明就在Native 里而且源码完全正确，
    // 清缓存、重建、删 obj 全都无效，纯粹是解析歧义。
      // 所以自检统一用这两个语义更清楚的别名，不再重复那个歧义写法。
        public const uint INJECT_LEFTDOWN = MOUSEEVENTF_LEFTDOWN;
        public const uint INJECT_LEFTUP = MOUSEEVENTF_LEFTUP;
        public const uint INJECT_RIGHTDOWN = MOUSEEVENTF_RIGHTDOWN;
        public const uint INJECT_RIGHTUP = MOUSEEVENTF_RIGHTUP;

        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_SCANCODE = 0x0008;

        public const ushort XBUTTON1 = 0x0001;
        public const ushort XBUTTON2 = 0x0002;

        public const int SM_CXMINTRACK = 34;
        public const int SM_CYMINTRACK = 35;
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;

        // ── user32 ──────────────────────────────────────────────
        internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// 读窗口的"还原位置"（rcNormalPosition）与显示状态。
        /// ★ 为什么钩子线程上需要它（第四十九轮）★
        ///   最大化窗口降级时不能再"ShowWindow(SW_RESTORE) 后回读 GetWindowRect"——
        ///   那是同步跨线程调用，目标不泵消息时没有上限（钩子线程卡死 = 整机输入冻结）。
        ///   GetWindowPlacement 是纯内核读（不发消息、不等目标窗口过程），
        ///   可以先拿到还原后的矩形做会话初始化，真正的还原写到工作线程上做。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// WM_SETREDRAW：wParam=0 冻结重绘、=1 恢复。
        /// 交互式改大小时用它把"重排的中间态"藏起来，只呈现最终帧。
        /// </summary>
        internal const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool PostMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // ── 取窗口所属进程的可执行文件名 ──────────────────────────
        //
        // 为什么要它：黑名单现在支持"选一个 exe 加进来"和"十字准星点选窗口"。
        //   只按窗口类名/标题匹配是不够的 ——
        //   类名往往是 "Chrome_WidgetWin_1" 这种内部名，
        //   标题里则通常是文档名而不是程序名，
        //   用户选了 notepad.exe，结果匹配不上，会以为功能坏了。
        //   所以必须能拿到"这扇窗是哪个 exe 开的"。
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool QueryFullProcessImageNameW(
            IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        /// <summary>查询进程信息所需的最小权限（能拿到路径即可，不需要读写内存）。</summary>
        internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int GetSystemMetrics(int nIndex);

        // ── DPI 感知级别查询（诊断用）─────────────────────────────
        // 为什么必须能查：本程序全程用【物理像素坐标】（鼠标钩子给的就是物理坐标）。
        // 如果进程是"DPI 不感知"的，系统会把传进去的坐标再缩放一次，
        // WindowFromPoint 之类的命中测试就会整体错位、甚至返回空句柄。
        // 这个值一旦不对，整个程序的核心功能就是废的，所以必须能一眼看到。
        [DllImport("user32.dll")]
        internal static extern IntPtr GetThreadDpiAwarenessContext();

        [DllImport("user32.dll")]
        internal static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

        /// <summary>
        /// 窗口当前所在显示器的 DPI（Win10 1607+）。
        ///
        /// ★ 第四十轮新增：整个界面按"显示器实时分辨率"渲染的前提就是能拿到它。★
        ///   比 Control.DeviceDpi 更可靠 —— DeviceDpi 在句柄创建前恒为 96，
        ///   而且不响应窗口被拖到另一块 DPI 不同的显示器。
        /// </summary>
        [DllImport("user32.dll")]
        internal static extern uint GetDpiForWindow(IntPtr hwnd);

        /// <summary>取窗口所在显示器的 DPI；取不到 / 值离谱时退回 96。</summary>
        internal static int DpiOfWindow(IntPtr hwnd)
        {
            try
            {
                if (hwnd != IntPtr.Zero)
                {
                    uint d = GetDpiForWindow(hwnd);
                    if (d >= 48 && d <= 960) return (int)d;
                }
            }
            catch { }
            return 96;
        }

        // GetAwarenessFromDpiAwarenessContext 的返回值
        internal const int DPI_AWARENESS_INVALID = -1;
        internal const int DPI_AWARENESS_UNAWARE = 0;
        internal const int DPI_AWARENESS_SYSTEM_AWARE = 1;
        internal const int DPI_AWARENESS_PER_MONITOR_AWARE = 2;

        /// <summary>
        /// 在【进程级】设置 DPI 感知。必须在创建任何窗口之前、且在 Main 的最开头调用。
        ///
        /// 为什么除了 app.manifest 还要在代码里再设一次：
        ///   实测发现同一个 exe 在不同位置运行会得到不同的 DPI 感知级别
        ///   （清单里的设置可能被兼容性设置、组策略、旧系统覆盖掉）。
        ///   一旦进程变成"不感知"，本程序的坐标体系就是错的、核心功能直接失效。
        ///   这条调用是最后一道保险：能设就设上，设不上也不影响（清单已经兜住了）。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetProcessDpiAware();

        [DllImport("user32.dll")]
        internal static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        internal static readonly IntPtr DPI_AWARENESS_CONTEXT_UNAWARE = new IntPtr(-1);
        internal static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new IntPtr(-2);
        internal static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new IntPtr(-3);
        internal static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        // ── 物理分辨率查询用的常量（GetDC / GetDeviceCaps 已在本文件下方声明）──
        /// <summary>物理桌面宽度。与 HORZRES 不同，它【不会】被 DPI 虚拟化。</summary>
        internal const int DESKTOPHORZRES = 118;

        /// <summary>物理桌面高度。与 VERTRES 不同，它【不会】被 DPI 虚拟化。</summary>
        internal const int DESKTOPVERTRES = 117;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetDoubleClickTime();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        // ══════ ★★★ GetWindow 的 uCmd 到底哪个是"往下"？★★★ ══════
        //
        // 这里曾经把方向搞反，而且注释还理直气壮地写反了，是"核心功能失效"的直接原因之一。
        //
        // Win32 的定义（user32 文档）：
        //   GW_HWNDNEXT  (2) = 指定窗口【下面】的窗口（Z 序更靠后，被它遮住的那一层）
        //   GW_HWNDPREV  (3) = 指定窗口【上面】的窗口（Z 序更靠前，遮住它的那一层）
        //
        // 我们要做的是"命中自己 → 继续往下找第一个不是自己的窗口"，
        // 所以必须用 GW_HWNDNEXT。原来用的是 GW_HWNDPREV，等于一路往屏幕最前方走，
        // 走到尽头自然是空句柄 → FindTarget 返回 0 → 用户按左键右键毫无反应。
        //
        // 助记：NEXT 是"往后/往下"（更靠背景），PREV 是"往前/往上"（更靠前景）。
        internal const uint GW_HWNDNEXT = 2;

        /// <summary>取压在 hWnd 下面一个的窗口（Z 序往下一层）。</summary>
        internal const uint GW_HWNDPREV = 3;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT pt, uint uFlags);

        // ChildWindowFromPointEx 的跳过选项
        internal const uint CWP_SKIPINVISIBLE = 0x0001;
        internal const uint CWP_SKIPDISABLED = 0x0002;
        internal const uint CWP_SKIPTRANSPARENT = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern short GetAsyncKeyState(int vKey);

        /// <summary>按类名/标题找顶层窗口（自检用：定位"外部窗口宿主"）。</summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindowW(string lpClassName, string lpWindowName);

        // GetAsyncKeyState 用的虚拟键码（鼠标三键的"物理按下"状态查询）
        internal const int VK_LBUTTON = 0x01;
        internal const int VK_RBUTTON = 0x02;
        internal const int VK_MBUTTON = 0x04;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetCapture(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr GetCapture();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        // ── 工作线程消息队列 ────────────────────────────────────
        // 钩子回调绝不能自己做重活：微软文档明确要求"把工作交给 worker 线程后立刻返回"，
        // 并且 LowLevelHooksTimeout 超时后钩子会被【静默摘除】，程序自己无法察觉。
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr DispatchMessageW(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern void NotifyWinEvent(uint eventId, IntPtr hWnd, int idObject, int idChild);

        // ── 光标裁剪矩形（AltSnap MouseMoveNow 用它修正越界坐标）──
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetClipCursor(out RECT lpRect);

        // ── 窗口样式读写（分 32/64 位）──────────────────────────
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLong64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        internal static long GetWindowLong(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8) return GetWindowLong64(hWnd, nIndex).ToInt64();
            return GetWindowLong32(hWnd, nIndex);
        }

        internal static void SetWindowLong(IntPtr hWnd, int nIndex, long value)
        {
            if (IntPtr.Size == 8) SetWindowLong64(hWnd, nIndex, new IntPtr(value));
            else SetWindowLong32(hWnd, nIndex, unchecked((int)value));
        }

        // ── shell32 ─────────────────────────────────────────────
        [DllImport("shell32.dll", SetLastError = true)]
        internal static extern bool IsUserAnAdmin();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        // ══════════════════════════════════════════════════════════
        //  浮层窗口（激活外框 / 吸附预览）
        // ══════════════════════════════════════════════════════════
        //
        // 浮层刻意【不用 WinForms 的 Form】，而是自己 RegisterClassEx + CreateWindowEx：
        //   · Form + Opacity 走的是 SetLayeredWindowAttributes，每次改尺寸/位置都要
        //     SetBounds() → SetWindowPos + Invalidate + Update，一整帧下来是"先移动、
        //     再重画"，中间态会被合成器看到 —— 这就是之前拖动时的残影来源。
        //   · 自建窗口可以精确控制 UpdateLayeredWindow（位置+尺寸+像素一次原子提交），
        //     或者用 SetWindowRgn 做成"环"，两者都不会出现中间态。

        internal const uint WM_NCHITTEST = 0x0084;
        internal const uint WM_ERASEBKGND = 0x0014;
        internal const uint WM_PAINT = 0x000F;
        internal const uint WM_DESTROY = 0x0002;
        internal const uint WM_MOUSEACTIVATE = 0x0021;
        internal const uint WM_APP = 0x8000;
        internal const uint WM_DISPLAYCHANGE = 0x007E;

        internal const int HTTRANSPARENT = -1;
        internal const int MA_NOACTIVATE = 3;

        internal const uint WS_POPUP = 0x80000000;
        internal const uint WS_CLIPSIBLINGS = 0x04000000;

        internal const uint ULW_ALPHA = 0x00000002;
        internal const byte AC_SRC_OVER = 0x00;
        internal const byte AC_SRC_ALPHA = 0x01;

        internal const int RGN_AND = 1;
        internal const int RGN_OR = 2;
        internal const int RGN_DIFF = 4;

        internal const int VREFRESH = 116;

        internal const uint GR_GDIOBJECTS = 0;
        internal const uint GR_USEROBJECTS = 1;

        internal const int COLOR_HIGHLIGHT = 13;
        internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [StructLayout(LayoutKind.Sequential)]
        internal struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int w, int h) { cx = w; cy = h; }
        }

        /// <summary>系统强调色（0-255 三通道），避免为了三个字节把 System.Drawing 拖进 Native。</summary>
        internal struct ColorAccent
        {
            public int R;
            public int G;
            public int B;
            public ColorAccent(int r, int g, int b) { R = r; G = g; B = b; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        internal delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern ushort RegisterClassExW(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyWindow(IntPtr hWnd);

        // ── 区域（激活外框用 SetWindowRgn 做成"环"，完全不透明，天然无残影）──
        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern int CombineRgn(IntPtr hrgnDst, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int iMode);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern bool DeleteObject(IntPtr ho);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        /// <summary>
        /// 把客户区标记为"已经画好了"。
        ///
        /// 必须调用：如果 WndProc 处理了 WM_PAINT 却既没有走 BeginPaint/EndPaint、
        /// 也没有调用它，窗口的无效区域就永远不会被证实有效，系统会【无限重发 WM_PAINT】。
        /// 表现是 Application.DoEvents() 陷在一个永不结束的消息循环里出不来，
        /// 整个进程看起来就是"卡死"。曾经真的这么踩过一次。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ValidateRect(IntPtr hWnd, IntPtr lpRect);

        // ── 拖动期环形外框（DragRingFrame）用 ──
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        /// <summary>
        /// 屏幕坐标 → 客户区坐标。补发"配对的按钮抬起"时用它把钩子给的物理坐标
        /// 换算成目标窗口的客户区坐标（lParam 的语义要求客户区坐标）。
        /// </summary>
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        internal static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateSolidBrush(uint crColor);

        [DllImport("user32.dll")]
        internal static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        internal static uint ColorRef(int r, int g, int b)
        {
            return (uint)(r | (g << 8) | (b << 16));
        }

        // ── 分层窗口（吸附预览用，位置+尺寸+像素一次提交）──
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey,
            ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        /// <summary>
        /// 读屏幕上的一个像素（颜色格式 0x00BBGGRR）。失败返回 0xFFFFFFFF（CLR_INVALID）。
        /// 仅供自检断言"外观真的画到屏幕上了"用 —— 分层浮层的内容用窗口 DC 读不到，
        /// 必须直接读屏幕。返回 uint 是为了能和 CLR_INVALID 比较。
        /// </summary>
        [DllImport("gdi32.dll")]
        internal static extern uint GetPixel(IntPtr hdc, int x, int y);

        // ── 显示刷新率 ──────────────────────────────────────────
        [DllImport("gdi32.dll")]
        internal static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        // ── 提高系统计时精度 ────────────────────────────────────
        // 默认的定时器精度是 15.6ms。要把拖动节流到"屏幕刷新率"（16.7ms@60Hz、
        // 6.9ms@144Hz），就必须把精度提到 1ms，否则 Thread.Sleep(3) 实际会睡 15ms，
        // 节流直接变成 60Hz 上限，高刷屏上反而更卡。
        // 这也是 AltSnap 在 main 里做的事情。
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        internal static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        internal static extern uint TimeEndPeriod(uint uMilliseconds);

        // ── 资源占用（泄漏压测用）──────────────────────────────
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// 带退出码地终止当前进程。
        /// 只有诊断入口（--selftest / --stress）用：它们必须"跑完就死"，
        /// 不能出现报告写完了进程还挂着的情况。
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetProcessHandleCount(IntPtr hProcess, out uint pdwHandleCount);

        // ── 系统强调色 / DWM 可见边框 ───────────────────────────
        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetColorizationColor(out uint pcrColorization, out bool pfOpaqueBlend);

        [DllImport("user32.dll")]
        internal static extern uint GetSysColor(int nIndex);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        /// <summary>DWMWA_WINDOW_CORNER_PREFERENCE（Win11 22000+）。</summary>
        internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        /// <summary>DWMWCP_ROUND：让系统给窗口做圆角（含投影与系统边框）。</summary>
        internal const int DWMWCP_ROUND = 2;

        /// <summary>
        /// 让窗口使用 Win11 的系统圆角外观。
        ///
        /// 为什么要交给系统而不是自己裁 Region：
        ///   系统圆角是【DWM 在合成阶段】做的，圆角处带抗锯齿、
        ///   并且和窗口投影、系统边框是配套的；自己用 SetWindowRgn 裁
        ///   会得到锯齿边、还会把系统投影一起裁掉。
        /// Win10 上这个属性不存在，调用返回非 0，静默忽略即可。
        /// </summary>
        internal static bool SetWindowRoundedCorners(IntPtr hwnd)
        {
            try
            {
                int pref = DWMWCP_ROUND;
                return DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref pref, sizeof(int)) == 0;
            }
            catch
            {
                return false;
            }
        }

        // ── 便捷封装 ────────────────────────────────────────────

        /// <summary>
        /// 取当前显示器的垂直刷新率。取不到（0 或 1 表示"硬件默认"）就回退 60。
        /// 用于把拖动刷新率节流到屏幕节奏，避免目标窗口的消息队列被灌爆。
        /// </summary>
        internal static int GetRefreshRate()
        {
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero)
                {
                    try
                    {
                        int hz = GetDeviceCaps(dc, VREFRESH);
                        if (hz >= 24 && hz <= 1000) return hz;
                    }
                    finally { ReleaseDC(IntPtr.Zero, dc); }
                }
            }
            catch (Exception ex) { Log.Error(ex); }
            return 60;
        }

        /// <summary>系统强调色。依次尝试 DWM 的着色色 → COLOR_HIGHLIGHT → 固定的 Win11 蓝。</summary>
        internal static ColorAccent GetAccent()
        {
            try
            {
                uint c;
                bool opaque;
                if (DwmGetColorizationColor(out c, out opaque) == 0)
                {
                    // DWM 返回的是 0xAARRGGBB
                    int r = (int)((c >> 16) & 0xFF);
                    int g = (int)((c >> 8) & 0xFF);
                    int b = (int)(c & 0xFF);
                    if (r + g + b > 60) return new ColorAccent(r, g, b);
                }
            }
            catch (Exception ex) { Log.Error(ex); }

            try
            {
                uint hl = GetSysColor(COLOR_HIGHLIGHT);
                int r = (int)(hl & 0xFF);
                int g = (int)((hl >> 8) & 0xFF);
                int b = (int)((hl >> 16) & 0xFF);
                if (r + g + b > 60) return new ColorAccent(r, g, b);
            }
            catch (Exception ex) { Log.Error(ex); }

            return new ColorAccent(0x00, 0x78, 0xD4);   // Windows 11 默认蓝
        }

        /// <summary>
        /// 窗口"可见边框"矩形。
        ///
        /// GetWindowRect 给的是【含不可见缩放边框】的矩形：Win10/11 上一个普通窗口左右各多
        /// 约 7px、底部多约 7px。直接按它画外框会明显偏外。
        /// DWM 的 EXTENDED_FRAME_BOUNDS 给的才是眼睛看到的那个边。
        /// </summary>
        internal static bool TryGetVisibleRect(IntPtr hwnd, out RECT r)
        {
            r = new RECT();
            try
            {
                var tmp = new RECT();
                int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS,
                    out tmp, Marshal.SizeOf(typeof(RECT)));
                if (hr == 0 && tmp.Width > 0 && tmp.Height > 0)
                {
                    r = tmp;
                    return true;
                }
            }
            catch (Exception ex) { Log.Error(ex); }
            return false;
        }

        internal static MONITORINFO GetMonitorInfoAt(POINT pt)
        {
            var mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            IntPtr h = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (h == IntPtr.Zero || !GetMonitorInfoW(h, ref mi)) return mi;
            return mi;
        }

        internal static bool IsEqualRect(RECT a, RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        internal static string GetClassName(IntPtr hWnd)        {
            var sb = new StringBuilder(256);
            GetClassNameW(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        internal static string GetWindowTitle(IntPtr hWnd)
        {
            int len = GetWindowTextLengthW(hWnd);
            if (len <= 0) return string.Empty;
            var sb = new StringBuilder(len + 2);
            GetWindowTextW(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
