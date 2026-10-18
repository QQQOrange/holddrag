// InputE2E —— 真实输入链路端到端验收
// 目的：用 SendInput 完整复刻「按住触发键(Enter) + 按左键 + 移动」的真实钩子链路，
//       逐帧记录目标窗口是否跟手，用来判定「修复是否真的生效」，不依赖进程内直调 OnMouse。
//
// 用法： InputE2E.exe <HoldDrag.exe 路径> <mode>
//        mode = simult  同时按（Enter 按下后 30ms 内按左键，左键落在 HoldMs 阈值窗口内）
//               sequ    先按 Enter 等过阈值，再按左键
//               nomove  同时按，但之后完全不动鼠标
//               tap     「按 Enter → 快速点一下左键（按下 50ms 后抬起）」→ 之后移动鼠标
//               fastmove「按 Enter → 立刻按左键 → 不等待，马上开始逐帧移动」（左键全程按住）
//               taphold 「按 Enter → 快速点一下左键 → 再按住左键」→ 之后移动鼠标
//        mode 也可写 all（依次跑同时/顺序/不动）、order（依次跑先左/先右）、user（用户三连场景）
//
// 输出：机器可读的 TRACE 行 + 末尾 RESULT 行。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    const ushort VK_RETURN = 0x0D;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out RECT_PT p);
    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    struct RECT_PT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    static readonly List<INPUT> Pending = new List<INPUT>();
    static int _vw, _vh;

    static void QueueKey(ushort vk, bool up)
    {
        INPUT i = new INPUT();
        i.type = INPUT_KEYBOARD;
        i.ki.wVk = vk;
        i.ki.wScan = 0;
        i.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0u;
        i.ki.dwExtraInfo = IntPtr.Zero;   // ★ 0 = 真实输入，不是 HoldDrag 自签事件
        Pending.Add(i);
    }

    static void QueueMouseAbs(int x, int y, uint extra)
    {
        INPUT i = new INPUT();
        i.type = INPUT_MOUSE;
        i.mi.dx = (int)Math.Round((double)x * 65535.0 / (_vw - 1));
        i.mi.dy = (int)Math.Round((double)y * 65535.0 / (_vh - 1));
        i.mi.mouseData = 0;
        i.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | extra;
        i.mi.dwExtraInfo = IntPtr.Zero;
        Pending.Add(i);
    }

    /// <summary>只发按键标志、不带位移的鼠标事件（抬起专用，避免污染窗口位移读数）。</summary>
    static void QueueMouseRaw(uint flags)
    {
        INPUT i = new INPUT();
        i.type = INPUT_MOUSE;
        i.mi.dx = 0;
        i.mi.dy = 0;
        i.mi.mouseData = 0;
        i.mi.dwFlags = flags;
        i.mi.dwExtraInfo = IntPtr.Zero;
        Pending.Add(i);
    }

    static void Flush()
    {
        if (Pending.Count == 0) return;
        INPUT[] arr = Pending.ToArray();
        Pending.Clear();
        uint sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
        if (sent != arr.Length) Console.WriteLine("  (警告) SendInput 只投递了 " + sent + "/" + arr.Length);
    }

    static Form _target;

    static string RectOf(IntPtr h)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) return "?";
        return r.Left + "," + r.Top + " " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top);
    }

    static int[] PosOf(IntPtr h)
    {
        RECT r; GetWindowRect(h, out r);
        return new[] { r.Left, r.Top };
    }

    static void Pump(int ms)
    {
        int deadline = Environment.TickCount + ms;
        while (Environment.TickCount < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: InputE2E.exe <HoldDrag.exe> <simult|sequ|nomove|all>");
            return;
        }
        string exe = args[0];
        string mode = args[1].ToLowerInvariant();
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        _vw = GetSystemMetrics(0);
        _vh = GetSystemMetrics(1);
        Console.WriteLine("屏幕 = " + _vw + "x" + _vh + "   被测 exe = " + exe);

        _target = new Form();
        _target.Text = "InputE2E-Target";
        _target.StartPosition = FormStartPosition.Manual;
        _target.FormBorderStyle = FormBorderStyle.Sizable;
        _target.Bounds = new Rectangle(500, 300, 700, 460);
        _target.TopMost = true;
        _target.ShowInTaskbar = false;
        _target.Show();
        SetForegroundWindow(_target.Handle);
        _target.Activate();
        Pump(600);

        Process child = null;
        if (exe == "-")
        {
            // 直连"已经在跑"的 HoldDrag（例如用户以管理员身份启动的那个），不另起进程。
            Console.WriteLine("使用当前已在运行 HoldDrag 实例（不另起进程）");
        }
        else
        {
            child = Process.Start(new ProcessStartInfo(exe, "--no-elevate") { UseShellExecute = false });
            Console.WriteLine("已启动被测进程 PID=" + (child == null ? -1 : child.Id) + "（--no-elevate 跳过 UAC）");
        }
        Pump(2600);   // 等钩子装好、看门狗就绪

        string[] modes = mode == "all" ? new[] { "simult", "sequ", "nomove" }
                       : mode == "order" ? new[] { "simult", "leftfirst", "rightfirst" }
                       : mode == "user" ? new[] { "tap", "taphold", "fastmove" }
                       : new[] { mode };
        foreach (string m in modes) RunOne(m);

        try
        {
            SetCursorPos(50, 50);
            if (child != null && !child.HasExited) { child.Kill(); child.WaitForExit(4000); }
        }
        catch { }
        Console.WriteLine("DONE");
    }

    static void RunOne(string mode)
    {
        _target.Bounds = new Rectangle(500, 300, 700, 460);
        SetForegroundWindow(_target.Handle);
        Pump(400);
        IntPtr h = _target.Handle;
        int[] p0 = PosOf(h);
        Console.WriteLine();
        Console.WriteLine("======== mode=" + mode + "  起始窗口=" + p0[0] + "," + p0[1]);
        Console.WriteLine("TRACE mode=" + mode + " step=start win=" + p0[0] + "," + p0[1] + " cur=" + Cursor());

        int cx = _target.Left + _target.Width / 2;
        int cy = _target.Top + _target.Height / 2;

        // 惰性定好指针，再注入
        SetCursorPos(cx, cy);
        Pump(250);
        cx = _target.Left + _target.Width / 2;
        cy = _target.Top + _target.Height / 2;
        SetCursorPos(cx, cy);
        Pump(120);
        Console.WriteLine("   指针定在目标中心 " + cx + "," + cy + "（窗口=" + RectOf(h) + "）");

        // ---- 注入手势 ----
        // 人手"同时按"其实有两种落键次序，必须分别测：
        //   simult / leftfirst : 左键先落（左键的 down 在触发键还没被按下时就已经到了）
        //   rightfirst         : 触发键先落，左键紧跟着落（左键 down 落在 HoldMs 阈值窗口内）
        if (mode == "leftfirst")
        {
            QueueMouseAbs(cx, cy, MOUSEEVENTF_LEFTDOWN);
            Flush();
            Pump(25);
            QueueKey(VK_RETURN, false);
            Flush();
        }
        else if (mode == "tap")
        {
            // 用户原话：「按 enter 然后快速的按一下左键」
            QueueKey(VK_RETURN, false);
            Flush();
            Pump(40);
            QueueMouseAbs(cx, cy, MOUSEEVENTF_LEFTDOWN);
            Flush();
            Pump(50);
            QueueMouseAbs(cx, cy, MOUSEEVENTF_LEFTUP);
            Flush();
        }
        else if (mode == "taphold")
        {
            // 点一下左键（被吞掉）之后再按住左键，看是否还能接管
            QueueKey(VK_RETURN, false);
            Flush();
            Pump(40);
            QueueMouseAbs(cx, cy, MOUSEEVENTF_LEFTDOWN);
            Flush();
            Pump(50);
            QueueMouseAbs(cx, cy, MOUSEEVENTF_LEFTUP);
            Flush();
            Pump(80);
            QueueMouseAbs(cx, cy, MOUSEEVENTF_LEFTDOWN);
            Flush();
        }
        else
        {
            QueueKey(VK_RETURN, false);
            Flush();
            if (mode == "sequ") Pump(420);
            else if (mode == "fastmove") Pump(20);   // 几乎不停顿，左键按下后马上开始移动
            else Pump(30);

            QueueMouseAbs(cx, cy, MOUSEEVENTF_LEFTDOWN);
            Flush();
        }

        bool fast = mode == "fastmove";
        int holdExtra = mode == "nomove" ? 900 : (fast ? 0 : 700);
        if (holdExtra > 0) Pump(holdExtra);   // 按住不动，等激活
        Console.WriteLine("TRACE mode=" + mode + " step=afterActivate(未移动) win=" + PosOf(h)[0] + "," + PosOf(h)[1] + " cur=" + Cursor());

        if (mode != "nomove")
        {
            // 每一步只走一格，逐帧看窗口跟不跟。
            // ★ 必须用 SendInput 注入 MOVE：SetCursorPos 不产生鼠标输入事件，低级钩子看不到。
            int steps = fast ? 12 : 6;
            int stepMs = fast ? 40 : 90;
            for (int k = 1; k <= steps; k++)
            {
                QueueMouseAbs(cx + k * 25, cy + k * 10, MOUSEEVENTF_MOVE);
                Flush();
                Pump(stepMs);
                int[] p = PosOf(h);
                Console.WriteLine("TRACE mode=" + mode + " step=move" + k
                    + " cur=" + Cursor() + " 期望=+" + (k * 25) + ",+" + (k * 10)
                    + " win=" + p[0] + "," + p[1]
                    + " 实际=+" + (p[0] - p0[0]) + ",+" + (p[1] - p0[1]));
            }
        }

        // 抬起：★ 绝不带 MOUSEEVENTF_MOVE —— 抬起注入里夹一个坐标会把光标拽回去，
        // 窗口跟着回退，末尾的 RESULT 位移就被自己的"测量动作"读错了。
        if (mode != "tap")
        {
            QueueMouseRaw(MOUSEEVENTF_LEFTUP);
        }
        QueueKey(VK_RETURN, true);
        Flush();
        Pump(400);

        int[] pEnd = PosOf(h);
        Console.WriteLine("RESULT mode=" + mode
            + " 起始=" + p0[0] + "," + p0[1]
            + " 结束=" + pEnd[0] + "," + pEnd[1]
            + " 总位移=+" + (pEnd[0] - p0[0]) + ",+" + (pEnd[1] - p0[1])
            + (mode == "tap" ? " (点按一下左键：用户期望窗口被'抓住'并跟随鼠标)"
                             : (mode == "fastmove" ? " (左键全程按住，最末一步期望 +300,+120)"
                                                   : " (期望 +" + (mode == "nomove" ? 0 : 150) + ",+" + (mode == "nomove" ? 0 : 60) + ")")));
    }

    static string Cursor()
    {
        RECT_PT p; GetCursorPos(out p);
        return p.x + "," + p.y;
    }
}
