using System;

namespace HoldDrag
{
    /// <summary>缩放时按住点落在窗口九宫格的哪一格。</summary>
    internal enum ResizeZone
    {
        None = 0,
        TopLeft,
        Top,
        TopRight,
        Left,
        Center,
        Right,
        BottomLeft,
        Bottom,
        BottomRight
    }

    /// <summary>拖动到屏幕边缘时命中的吸附目标。</summary>
    internal enum SnapZone
    {
        None = 0,
        LeftHalf,
        RightHalf,
        TopHalf,
        BottomHalf,
        TopLeftQuarter,
        TopRightQuarter,
        BottomLeftQuarter,
        BottomRightQuarter,
        Maximize
    }

    /// <summary>
    /// 纯几何判定。全部是静态无状态方法，方便 --selftest 直接断言。
    /// </summary>
    internal static class Zones
    {
        /// <summary>
        /// 判定按住点落在窗口的哪一格。
        /// 与 AltSnap 一致：窗口划成九宫格，角格比中间格稍大（由 cornerPercent 控制）。
        /// </summary>
        public static ResizeZone Resize(Native.RECT r, int x, int y, int cornerPercent)
        {
            if (r.Width <= 0 || r.Height <= 0) return ResizeZone.Center;

            if (cornerPercent < 5) cornerPercent = 5;
            if (cornerPercent > 45) cornerPercent = 45;

            bool left = x < r.Left + r.Width * cornerPercent / 100;
            bool right = x > r.Right - r.Width * cornerPercent / 100;
            bool top = y < r.Top + r.Height * cornerPercent / 100;
            bool bottom = y > r.Bottom - r.Height * cornerPercent / 100;

            if (top && left) return ResizeZone.TopLeft;
            if (top && right) return ResizeZone.TopRight;
            if (bottom && left) return ResizeZone.BottomLeft;
            if (bottom && right) return ResizeZone.BottomRight;
            if (top) return ResizeZone.Top;
            if (bottom) return ResizeZone.Bottom;
            if (left) return ResizeZone.Left;
            if (right) return ResizeZone.Right;
            return ResizeZone.Center;
        }

        /// <summary>中心格没有明确方向，挑一个最近角来缩放（与 AltSnap “就近角”的手感一致）。</summary>
        public static ResizeZone NearestCorner(Native.RECT r, int x, int y)
        {
            bool leftHalf = x < r.Left + r.Width / 2;
            bool topHalf = y < r.Top + r.Height / 2;
            if (topHalf) return leftHalf ? ResizeZone.TopLeft : ResizeZone.TopRight;
            return leftHalf ? ResizeZone.BottomLeft : ResizeZone.BottomRight;
        }

        /// <summary>该格需要拖动哪几条边。</summary>
        public static void EdgesOf(ResizeZone z, out bool left, out bool top, out bool right, out bool bottom)
        {
            left = top = right = bottom = false;
            switch (z)
            {
                case ResizeZone.TopLeft: left = true; top = true; break;
                case ResizeZone.Top: top = true; break;
                case ResizeZone.TopRight: right = true; top = true; break;
                case ResizeZone.Left: left = true; break;
                case ResizeZone.Right: right = true; break;
                case ResizeZone.BottomLeft: left = true; bottom = true; break;
                case ResizeZone.Bottom: bottom = true; break;
                case ResizeZone.BottomRight: right = true; bottom = true; break;
                case ResizeZone.Center: right = true; bottom = true; break;
            }
        }

        /// <summary>
        /// 判定光标是否贴到屏幕工作区边缘，命中则返回吸附目标。
        /// distance 为触发距离（像素）。先判角，再判边。
        /// </summary>
        public static SnapZone ScreenEdge(Native.RECT work, int x, int y, int distance)
        {
            if (distance < 1) distance = 1;

            bool atLeft = x <= work.Left + distance;
            bool atRight = x >= work.Right - distance;
            bool atTop = y <= work.Top + distance;
            bool atBottom = y >= work.Bottom - distance;

            // 先判四角：角比边优先，否则贴角会被判成贴边
            if (atTop && atLeft) return SnapZone.TopLeftQuarter;
            if (atTop && atRight) return SnapZone.TopRightQuarter;
            if (atBottom && atLeft) return SnapZone.BottomLeftQuarter;
            if (atBottom && atRight) return SnapZone.BottomRightQuarter;
            if (atTop) return SnapZone.Maximize;
            if (atLeft) return SnapZone.LeftHalf;
            if (atRight) return SnapZone.RightHalf;
            // 只贴屏幕下边缘时不做吸附（与 Windows 原生 Aero Snap 行为一致）
            return SnapZone.None;
        }

        /// <summary>把吸附目标换算成具体矩形。</summary>
        public static Native.RECT RectOf(SnapZone zone, Native.RECT work)
        {
            int w = work.Width;
            int h = work.Height;
            int hw = w / 2;
            int hh = h / 2;

            switch (zone)
            {
                case SnapZone.LeftHalf:
                    return new Native.RECT(work.Left, work.Top, work.Left + hw, work.Bottom);
                case SnapZone.RightHalf:
                    return new Native.RECT(work.Right - hw, work.Top, work.Right, work.Bottom);
                case SnapZone.TopHalf:
                    return new Native.RECT(work.Left, work.Top, work.Right, work.Top + hh);
                case SnapZone.BottomHalf:
                    return new Native.RECT(work.Left, work.Bottom - hh, work.Right, work.Bottom);
                case SnapZone.TopLeftQuarter:
                    return new Native.RECT(work.Left, work.Top, work.Left + hw, work.Top + hh);
                case SnapZone.TopRightQuarter:
                    return new Native.RECT(work.Right - hw, work.Top, work.Right, work.Top + hh);
                case SnapZone.BottomLeftQuarter:
                    return new Native.RECT(work.Left, work.Bottom - hh, work.Left + hw, work.Bottom);
                case SnapZone.BottomRightQuarter:
                    return new Native.RECT(work.Right - hw, work.Bottom - hh, work.Right, work.Bottom);
                case SnapZone.Maximize:
                    // 最大化时“位置”无意义，交由调用方处理；给个占位矩形方便画高亮
                    return new Native.RECT(work.Left, work.Top, work.Right, work.Bottom);
            }
            return new Native.RECT(work.Left, work.Top, work.Right, work.Bottom);
        }

        /// <summary>双击右键的九宫格 → 屏幕区域（与 AltSnap 的九区双击映射一致）。</summary>
        public static SnapZone ScreenZoneOfResizeZone(ResizeZone z)
        {
            switch (z)
            {
                case ResizeZone.TopLeft: return SnapZone.TopLeftQuarter;
                case ResizeZone.Top: return SnapZone.TopHalf;
                case ResizeZone.TopRight: return SnapZone.TopRightQuarter;
                case ResizeZone.Left: return SnapZone.LeftHalf;
                case ResizeZone.Center: return SnapZone.Maximize;
                case ResizeZone.Right: return SnapZone.RightHalf;
                case ResizeZone.BottomLeft: return SnapZone.BottomLeftQuarter;
                case ResizeZone.Bottom: return SnapZone.BottomHalf;
                case ResizeZone.BottomRight: return SnapZone.BottomRightQuarter;
            }
            return SnapZone.None;
        }

        /// <summary>把一个矩形限制在最小尺寸以上。</summary>
        public static void EnforceMinSize(ref Native.RECT r, int minWidth, int minHeight)
        {
            if (r.Width < minWidth)
            {
                // 收窄了宽度：保持不动的那条边不动
                if (r.Left != r.Right) r.Right = r.Left + minWidth;
            }
            if (r.Height < minHeight)
            {
                if (r.Top != r.Bottom) r.Bottom = r.Top + minHeight;
            }
        }
    }
}
