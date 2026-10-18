using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace HoldDrag
{
    /// <summary>
    /// 激活外框的可选颜色。
    ///
    /// 早期版本直接跟随系统强调色，而 Windows 默认强调色恰恰是 Win11 那个蓝，
    /// 用户明确表示"不要用蓝色、一点都不现代化"。所以这里改成一份自带调色板，
    /// 系统强调色降级为其中一个可选项（真的想要的人自己选）。
    ///
    /// 刻意不收录蓝色系：这套 UI 的用途是"指示这扇窗被接管了"，需要的是
    /// 一眼可辨、且不与窗口自身内容撞色，蓝色在这两点上都不是最优解。
    /// </summary>
    internal static class FrameColors
    {
        public const string Teal = "Teal";
        public const string Aqua = "Aqua";
        public const string Green = "Green";
        public const string Amber = "Amber";
        public const string Coral = "Coral";
        public const string Red = "Red";
        public const string Pink = "Pink";
        public const string Violet = "Violet";
        public const string Graphite = "Graphite";
        public const string Silver = "Silver";
        public const string Accent = "Accent";

        internal struct Entry
        {
            public string Id;
            public string Name;
            public Color Color;
        }

        /// <summary>顺序即下拉框里的顺序。Accent 走 Resolve() 动态取系统色，这里给个占位。</summary>
        internal static readonly Entry[] All =
        {
            new Entry { Id = Teal,      Name = "薄荷青",   Color = Color.FromArgb(0x1D, 0x9E, 0x75) },
            new Entry { Id = Aqua,      Name = "湖水青",   Color = Color.FromArgb(0x5D, 0xCA, 0xA5) },
            new Entry { Id = Green,     Name = "翠绿",     Color = Color.FromArgb(0x63, 0x99, 0x22) },
            new Entry { Id = Amber,     Name = "琥珀",     Color = Color.FromArgb(0xEF, 0x9F, 0x27) },
            new Entry { Id = Coral,     Name = "珊瑚橙",   Color = Color.FromArgb(0xD8, 0x5A, 0x30) },
            new Entry { Id = Red,       Name = "朱红",     Color = Color.FromArgb(0xE2, 0x4B, 0x4A) },
            new Entry { Id = Pink,      Name = "樱粉",     Color = Color.FromArgb(0xD4, 0x53, 0x7E) },
            new Entry { Id = Violet,    Name = "紫罗兰",   Color = Color.FromArgb(0x7F, 0x77, 0xDD) },
            new Entry { Id = Graphite,  Name = "石墨灰",   Color = Color.FromArgb(0x5F, 0x5E, 0x5A) },
            new Entry { Id = Silver,    Name = "银灰",     Color = Color.FromArgb(0x88, 0x87, 0x80) },
            new Entry { Id = Accent,    Name = "跟随系统强调色", Color = Color.FromArgb(0x00, 0x78, 0xD4) },
        };

        public static string DefaultId { get { return Teal; } }

        public static bool IsValid(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return true;
            return false;
        }

        /// <summary>取实际颜色。"跟随系统强调色"在这里才去问 DWM，避免启动时就做跨进程调用。</summary>
        public static Color Resolve(string id)
        {
            if (id == Accent)
            {
                try
                {
                    var a = Native.GetAccent();
                    return Color.FromArgb(a.R, a.G, a.B);
                }
                catch { return Color.FromArgb(0x00, 0x78, 0xD4); }
            }
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id) return All[i].Color;
            }
            return All[0].Color;
        }
    }

    /// <summary>
    /// 激活外框的可选样式 —— 只描述"怎么画"，不关心画在哪。
    ///
    /// 这个 Draw() 同时被两处调用：真实的浮层窗口，和设置窗口里的实时预览。
    /// 同一份代码意味着预览所见即所得，不会出现"设置里看着好看、实际不一样"。
    /// </summary>
    internal static class FrameStyles
    {
        public const string Glow = "Glow";         // 柔光环（默认）：主环 + 三层渐弱外扩
        public const string Flat = "Flat";         // 纯净环：只有主环
        public const string Solid = "Solid";       // 实心描边：加粗无柔光
        public const string Double = "Double";     // 双线：主环 + 外侧细线
        public const string Dashed = "Dashed";     // 虚线环：主环虚线 + 轻柔光

        public static readonly string[] All = { Glow, Flat, Solid, Double, Dashed };

        public static string DefaultId { get { return Glow; } }

        public static bool IsValid(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i] == id) return true;
            return false;
        }

        public static string DisplayName(string id)
        {
            switch (id)
            {
                case Glow: return "柔光环（推荐）";
                case Flat: return "纯净环（无光晕）";
                case Solid: return "实心描边（最醒目）";
                case Double: return "双线（极简）";
                case Dashed: return "虚线环";
            }
            return id;
        }

        /// <summary>
        /// 浮层窗口需要比窗口矩形大出的那圈留白。
        /// 柔光是往外扩的，不留白的话光晕会被窗口边界直接切掉，看上去就像被削平了。
        /// </summary>
        public static int MarginFor(string style, int thickness)
        {
            int half = thickness / 2 + 1;
            switch (style)
            {
                case Glow: return half + 10;
                case Dashed: return half + 7;
                case Solid: return (int)(thickness * 0.9f) + 3;
                case Double: return half + 7;
                default: return half + 3;
            }
        }

        private static readonly int[] GlowAlpha = { 48, 30, 18 };

        /// <summary>
        /// 在 area 里画一圈外框。
        /// area 是浮层窗口的整个客户区；外框本体画在 area 内缩 margin 之后的那个矩形上，
        /// 与 DragEngine.ShowFrame() 的换算方式严格对应。
        /// </summary>
        public static void Draw(Graphics g, RectangleF area, Color color, string style, int thickness, int radius)
        {
            int margin = MarginFor(style, thickness);
            var ring = new RectangleF(area.X + margin, area.Y + margin,
                                      area.Width - margin * 2f, area.Height - margin * 2f);
            if (ring.Width < 6f || ring.Height < 6f) return;

            float r = ClampRadius(radius, ring.Width, ring.Height);
            float t = thickness;
            if (style == Solid) t = Math.Max(2f, thickness * 1.8f);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // 柔光：几圈越来越宽、越来越淡的同心描边，叠出柔和的外扩。
            // 刻意不用 blur —— 每帧做模糊太贵，而同心描边的观感几乎一样。
            if (style == Glow || style == Dashed)
            {
                int layers = style == Glow ? 3 : 2;
                for (int k = layers; k >= 1; k--)
                {
                    using (var path = RoundedPath(ring, r))
                    using (var pen = new Pen(Color.FromArgb(GlowAlpha[k - 1], color), t + 3f * k))
                    {
                        pen.LineJoin = LineJoin.Round;
                        ApplyDash(pen, style, r);
                        g.DrawPath(pen, path);
                    }
                }
            }

            using (var path = RoundedPath(ring, r))
            using (var pen = new Pen(color, t))
            {
                pen.LineJoin = LineJoin.Round;
                ApplyDash(pen, style, r);
                g.DrawPath(pen, path);
            }

            if (style == Double)
            {
                var outer = new RectangleF(ring.X - 4.5f, ring.Y - 4.5f, ring.Width + 9f, ring.Height + 9f);
                using (var path = RoundedPath(outer, r + 4.5f))
                using (var pen = new Pen(Color.FromArgb(140, color), 1.5f))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }
        }

        private static void ApplyDash(Pen pen, string style, float radius)
        {
            if (style != Dashed) return;
            pen.DashStyle = DashStyle.Dash;
            pen.DashCap = DashCap.Round;
            // 虚线段长度跟着圆角走，圆角越大段越长，转角处才不会挤成一团
            float unit = Math.Max(4f, radius * 0.85f);
            pen.DashPattern = new float[] { unit, unit * 0.72f };
        }

        /// <summary>圆角不能超过短边的一半，否则两条对边会互相穿过。</summary>
        public static float ClampRadius(int radius, float w, float h)
        {
            float max = Math.Min(w, h) / 2f - 1f;
            if (max < 0f) max = 0f;
            float r = radius;
            if (r < 0f) r = 0f;
            if (r > max) r = max;
            return r;
        }

        private static GraphicsPath RoundedPath(RectangleF rc, float radius)
        {
            var p = new GraphicsPath();
            if (radius <= 0.5f)
            {
                p.AddRectangle(rc);
                return p;
            }

            float d = radius * 2f;
            p.AddArc(rc.X, rc.Y, d, d, 180f, 90f);
            p.AddArc(rc.Right - d, rc.Y, d, d, 270f, 90f);
            p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0f, 90f);
            p.AddArc(rc.X, rc.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure();
            return p;
        }
    }
}
