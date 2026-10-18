using System;

namespace HoldDrag
{
    /// <summary>
    /// 坐标空间换算 —— 处理"进程是 DPI 不感知的"这种情况。
    ///
    /// ══════ 为什么需要它（用户实测踩到的真实故障）══════
    ///
    /// 本程序全程使用【物理像素坐标】：鼠标钩子 MSLLHOOKSTRUCT.pt 给的就是物理坐标。
    ///
    /// 但如果进程是"DPI 不感知"的，系统会把传给 API 的坐标【再缩放一次】：
    ///   屏幕物理 3840x2160 @200%，进程却以为只有 1920x1080；
    ///   钩子给的 (2182,1440) 被系统当成"200% 下的坐标"再放大到 (4364,2880)，
    ///   直接跑到屏幕外面 → WindowFromPoint 返回空句柄
    ///   → FindTarget 返回 0 → 长按触发键后左键右键完全没反应。
    ///
    /// 这就是用户报的"基本的功能都没有了"的根因。
    /// 佐证：用户日志里坐标是 (2182,1440)，而进程的逻辑屏幕宽度只有 1920 ——
    ///       2182 &gt; 1920 说明钩子给的是物理坐标，两者根本不在一个空间里。
    ///
    /// ══════ 三层防护 ══════
    ///   1) app.manifest 里声明 PerMonitorV2（进程启动时最先生效）
    ///   2) Main 最开头再调一次 SetProcessDpiAwarenessContext（防清单被覆盖）
    ///   3) 本类 —— 万一前两层都没生效，就把坐标换算进进程自己的空间，功能依然可用
    ///
    /// 注意：正常情况下（进程是 DPI 感知的）本类是恒等变换，开销为零。
    /// </summary>
    internal static class CoordSpace
    {
        private static double _scale = 1.0;
        private static bool _initialized;

        /// <summary>物理像素 → 进程坐标空间的缩放系数。1.0 表示两者一致。</summary>
        public static double Scale { get { return _scale; } }

        /// <summary>是否恒等变换（进程 DPI 感知时恒为 true）。</summary>
        public static bool IsIdentity { get { return _scale == 1.0; } }

        /// <summary>
        /// 在启动时调用一次。必须在日志初始化之后、任何坐标使用之前。
        /// </summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                int aware = Native.DPI_AWARENESS_INVALID;
                try
                {
                    aware = Native.GetAwarenessFromDpiAwarenessContext(
                        Native.GetThreadDpiAwarenessContext());
                }
                catch (Exception ex) { Log.Error(ex); }

                if (aware != Native.DPI_AWARENESS_UNAWARE)
                {
                    _scale = 1.0;
                    return;                       // 正常情况：什么都不用做
                }

                int logW = Native.GetSystemMetrics(Native.SM_CXSCREEN);
                int physW = PhysicalScreenWidth();

                if (logW > 0 && physW > 0 && physW != logW)
                {
                    _scale = (double)logW / physW;
                    Log.Info("★ 进程是 DPI 不感知的，坐标将按 " + _scale.ToString("0.####")
                        + " 换算（逻辑宽 " + logW + " / 物理宽 " + physW + "）");
                }
                else
                {
                    Log.Info("★ 进程是 DPI 不感知的，但查不到物理分辨率，坐标保持原样"
                        + "（逻辑宽 " + logW + "，物理宽 " + physW + "）");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                _scale = 1.0;
            }
        }

        /// <summary>
        /// 取物理屏幕宽度。
        ///
        /// 必须用 DESKTOPHORZRES 而不是 HORZRES：
        ///   · HORZRES 返回的是【被 DPI 虚拟化过】的值（不感知进程拿到 1920）；
        ///   · DESKTOPHORZRES 返回的才是真正的物理分辨率（3840）。
        /// 用错的话算出来的比例恒等于 1，等于白算。
        /// </summary>
        private static int PhysicalScreenWidth()
        {
            try
            {
                IntPtr dc = Native.GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return 0;
                try
                {
                    int w = Native.GetDeviceCaps(dc, Native.DESKTOPHORZRES);
                    if (w > 0) return w;

                    // 退一步：用高对比度……不，用垂直方向再试一次，仍然拿不到就放弃
                    int h = Native.GetDeviceCaps(dc, Native.DESKTOPVERTRES);
                    int logH = Native.GetSystemMetrics(Native.SM_CYSCREEN);
                    if (h > 0 && logH > 0) return w;   // 拿不到宽度就返回 0，让调用方保持恒等
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, dc);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            return 0;
        }

        /// <summary>
        /// 仅供 --selftest：临时改写缩放系数，用来验证换算数学。
        /// 正常运行时不允许调用（真实系数只能由 Init() 依据进程的 DPI 感知级别算出）。
        /// </summary>
        internal static void SetScaleForTest(double s)
        {
            _initialized = true;
            _scale = s > 0 ? s : 1.0;
        }

        /// <summary>
        /// 把"系统给我们的物理坐标"换算进进程自己的坐标空间。
        ///
        /// 只对【来自低级鼠标钩子的坐标】使用：
        ///   MSLLHOOKSTRUCT.pt 给的是物理坐标（用户日志里 2182 超过了逻辑宽度 1920 就是铁证）。
        ///
        /// 不要对 GetCursorPos / GetWindowRect / Screen.Bounds 的结果使用 ——
        /// 那些 API 对 DPI 不感知的进程已经返回过虚拟化坐标了，再换算一次就重复了。
        /// </summary>
        public static Native.POINT ToProcess(Native.POINT pt)
        {
            if (_scale == 1.0) return pt;
            return new Native.POINT(
                (int)Math.Round(pt.X * _scale),
                (int)Math.Round(pt.Y * _scale));
        }

        /// <summary>
        /// 逻辑坐标 → 物理坐标（<see cref="ToProcess"/> 的逆运算）。
        ///
        /// 给"按屏幕物理坐标取像素"这类操作兜底用：自检里读屏幕像素验证外框颜色时，
        /// 采样点是用逻辑坐标算出来的，而屏幕像素是物理坐标 ——
        /// 进程不感知时两者差一个比例，直接读会读错位置。
        /// </summary>
        public static Native.POINT ToPhysical(Native.POINT pt)
        {
            if (_scale == 1.0) return pt;
            return new Native.POINT(
                (int)Math.Round(pt.X / _scale),
                (int)Math.Round(pt.Y / _scale));
        }
    }
}
