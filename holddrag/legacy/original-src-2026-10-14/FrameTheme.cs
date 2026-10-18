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
    ///
    /// ★ 第三十八轮重做（用户："外框太丑了，又丑，颜色什么的又复古"）★
    ///   旧调色板是从旧版 Office / 系统主题里取的色，饱和度低、灰味重，
    ///   在 4K 屏上看着发闷。现在整份换成现代 UI 调色板（Tailwind / Material 3
    ///   那一档的明度与饱和度）：高饱和、明快、彼此拉开色相距离，
    ///   在浅色和深色窗口背景上都能一眼认出来。
    ///   ID 保持不变 —— 老配置里的 FrameColorId 不会失效。
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

        // ── 第三十八轮新增的现代色 ──
        public const string Lime = "Lime";         // 青柠
        public const string Rose = "Rose";         // 玫瑰
        public const string Gold = "Gold";         // 暖金
        public const string Sky = "Sky";           // 天青（偏青不偏蓝）
        public const string Obsidian = "Obsidian"; // 曜石黑
        public const string Pearl = "Pearl";       // 珍珠白

        internal struct Entry
        {
            public string Id;
            public string Name;
            public Color Color;
        }

        /// <summary>
        /// 顺序即下拉框里的顺序。Accent 走 Resolve() 动态取系统色，这里给个占位。
        ///
        /// ══════ ★ 第四十六轮：整组调色板改成"柔和"版本 ★ ══════
        ///
        ///  用户原话："现在的颜色选择看起来很多，但实际上真正能用于当做外框的
        ///           颜色没有多少，重新设置一些视觉上没那么刺激、偏柔和一些的颜色。"
        ///
        ///  问题出在原来的取值上：那一批全是"高饱和 + 中等明度"的亮色
        ///  （#10B981 / #EF4444 / #FB712A / #8B5CF6 …，饱和度普遍 80~96%）。
        ///  这类颜色画成【一圈包住窗口的框】时会非常抢眼 —— 拖动时视线被边框
        ///  一直拽着，久了就刺眼，所以"看着有十几个，真正敢用的没几个"。
        ///
        ///  改法：**只动颜色值，不动 id 与名称** ——
        ///    · id 不变 → 用户已保存的 FrameColorId 全部继续有效，不会被打回默认；
        ///    · 饱和度统一压到 40%~68%，明度提到 58%~70%；
        ///    · 色相全部保持原样（青还是青、红还是红），所以"想找某个色"依然找得到。
        ///  结果是每一个都变成"看得清但不扎眼"的柔和色，整组都能用。
        /// </summary>
        internal static readonly Entry[] All =
        {
            // ── 冷色系（柔和）──
            new Entry { Id = Teal,      Name = "薄荷青",   Color = Color.FromArgb(0x6B, 0xC4, 0xA5) },
            new Entry { Id = Aqua,      Name = "湖水青",   Color = Color.FromArgb(0x6F, 0xC8, 0xBC) },
            new Entry { Id = Sky,       Name = "天青",     Color = Color.FromArgb(0x64, 0xC2, 0xD6) },
            new Entry { Id = Green,     Name = "抹茶绿",   Color = Color.FromArgb(0x74, 0xC8, 0x94) },
            new Entry { Id = Lime,      Name = "嫩芽绿",   Color = Color.FromArgb(0xA8, 0xCB, 0x6A) },
            // ── 暖色系（柔和）──
            new Entry { Id = Gold,      Name = "暖金",     Color = Color.FromArgb(0xD6, 0xC1, 0x6A) },
            new Entry { Id = Amber,     Name = "蜜橙",     Color = Color.FromArgb(0xDD, 0xA9, 0x5C) },
            new Entry { Id = Coral,     Name = "珊瑚",     Color = Color.FromArgb(0xE8, 0x93, 0x76) },
            new Entry { Id = Red,       Name = "暖砖红",   Color = Color.FromArgb(0xDE, 0x85, 0x85) },
            new Entry { Id = Rose,      Name = "玫瑰",     Color = Color.FromArgb(0xDE, 0x85, 0x95) },
            new Entry { Id = Pink,      Name = "藕粉",     Color = Color.FromArgb(0xDE, 0x93, 0xBC) },
            // ── 紫色系（柔和）──
            new Entry { Id = Violet,    Name = "藕荷",     Color = Color.FromArgb(0xA3, 0x8E, 0xDB) },
            // ── 中性色 ──
            new Entry { Id = Graphite,  Name = "石墨",     Color = Color.FromArgb(0x6B, 0x7A, 0x8F) },
            new Entry { Id = Silver,    Name = "银灰",     Color = Color.FromArgb(0xA8, 0xB3, 0xC1) },
            new Entry { Id = Obsidian,  Name = "曜石",     Color = Color.FromArgb(0x3A, 0x44, 0x53) },
            new Entry { Id = Pearl,     Name = "珍珠白",   Color = Color.FromArgb(0xED, 0xF1, 0xF6) },
            new Entry { Id = Accent,    Name = "跟随系统强调色", Color = Color.FromArgb(0x00, 0x78, 0xD4) },
        };

        public static string DefaultId { get { return Teal; } }

        public static bool IsValid(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return true;
            return false;
        }

        /// <summary>取实际颜色。"跟随系统强调色"在这里才去问 DWM，避免启动时就做跨进程调用。</summary>
        // ══════ Accent 色缓存 ══════
        // Resolve() 在拖动的每一帧都会被 ApplyTheme 调用。Accent 分支原来
        // 每帧调一次 GetAccent() → DwmGetColorizationColor（读注册表）。
        // 注册表读可能落到磁盘，周期性拖慢帧节奏。缓存 2 秒足够跟上主题切换。
        private static Color _accentCache;
        private static long _accentAt;

        public static Color Resolve(string id)
        {
            if (id == Accent)
            {
                long now = Environment.TickCount64;
                if (_accentAt != 0 && now - _accentAt < 2000) return _accentCache;
                try
                {
                    var a = Native.GetAccent();
                    _accentCache = Color.FromArgb(a.R, a.G, a.B);
                }
                catch { _accentCache = Color.FromArgb(0x00, 0x78, 0xD4); }
                _accentAt = now;
                return _accentCache;
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

        // ── 第三十八轮新增的现代样式 ──
        public const string Aurora = "Aurora";     // 极光：渐变环，颜色沿对角由亮到暗过渡
        public const string Neon = "Neon";         // 霓虹：强光晕 + 提亮的亮芯
        public const string Halo = "Halo";         // 光晕：大范围柔和弥散
        public const string Corners = "Corners";   // 四角框：只在四个角画括线（最不打扰内容）

        // ── 第四十六轮新增：三种"更轻、更柔和"的样式 ──
        //   用户："现在的外框样式很多，但总觉得还不够。"
        //   补的这三种刻意都走"少打扰内容"这一路：
        //   一个不画实线（投影）、一个只画底边（下缘光条）、一个只在边中点（四边中点）。
        public const string Shadow = "Shadow";         // 柔和投影：只铺一层淡影，不画实线
        public const string Underline = "Underline";   // 下缘光条：只在底边一条渐隐光条
        public const string Brackets = "Brackets";     // 四边中点：四条边正中各一小段

        public static readonly string[] All =
        {
            Glow, Flat, Solid, Double, Dashed,
            Aurora, Neon, Halo, Corners,
            Shadow, Underline, Brackets,
        };

        public static string DefaultId { get { return Glow; } }

        /// <summary>
        /// 仅供自检：把一个样式画进离屏位图，数"有多少像素不是背景"。
        ///
        /// 为什么必须像素级验证：十二种样式里有一半是"分支里各自画法"，
        /// 手滑写错一个（比如矩形算成负宽、画笔颜色全透明）不会报错、不会崩，
        /// 只是在设置页里变成一片空白 —— 而缩略图网格里空一格很难被注意到。
        /// </summary>
        internal static int MeasureStyleInkForTest(string style)
        {
            try
            {
                const int W = 120, H = 90;
                using (var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        Draw(g, new RectangleF(0, 0, W, H), Color.FromArgb(0x6B, 0xC4, 0xA5),
                             style, 4, 8);
                    }
                    int ink = 0;
                    for (int y = 0; y < H; y++)
                    {
                        for (int x = 0; x < W; x++)
                        {
                            if (bmp.GetPixel(x, y).A > 8) ink++;
                        }
                    }
                    return ink;
                }
            }
            catch { return -1; }
        }

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
                case Aurora: return "极光（渐变）";
                case Neon: return "霓虹（发光）";
                case Halo: return "光晕（弥散）";
                case Corners: return "四角框（最轻量）";
                case Shadow: return "柔和投影（不画实线）";
                case Underline: return "下缘光条（只提示底边）";
                case Brackets: return "四边中点（四条短线）";
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
                case Aurora: return half + 10;
                case Neon: return half + 12;
                case Halo: return half + 14;
                case Dashed: return half + 7;
                case Solid: return (int)(thickness * 0.9f) + 3;
                case Double: return half + 7;
                case Corners: return half + 4;
                case Shadow: return half + 16;    // 投影要铺得开，留白给足
                case Underline: return half + 10;
                case Brackets: return half + 6;
                default: return half + 3;
            }
        }

        private static readonly int[] GlowAlpha = { 48, 30, 18 };

        /// <summary>
        /// 在 area 里画一圈外框。
        /// area 是浮层窗口的整个客户区；外框本体画在 area 内缩 margin 之后的那个矩形上，
        /// 与 DragEngine.ShowFrame() 的换算方式严格对应。
        /// </summary>
        /// <param name="marginOverride">
        /// 强制指定留白（-1 = 用 MarginFor 算）。
        /// 只给【缩略图】用：Halo 的真实 margin 有 15px、Neon 有 13px，
        /// 设置页里 70×30 的缩略格根本装不下，会画成一片空白。
        /// 缩略图传一个小 margin 把同一段绘制代码压缩进小格子，
        /// 形状/层次仍然忠实，只是等比缩小。
        /// </param>
        public static void Draw(Graphics g, RectangleF area, Color color, string style, int thickness, int radius,
                                int marginOverride = -1)
        {
            int margin = marginOverride >= 0 ? marginOverride : MarginFor(style, thickness);
            var ring = new RectangleF(area.X + margin, area.Y + margin,
                                      area.Width - margin * 2f, area.Height - margin * 2f);
            if (ring.Width < 6f || ring.Height < 6f) return;

            float r = ClampRadius(radius, ring.Width, ring.Height);
            float t = thickness;
            if (style == Solid) t = Math.Max(2f, thickness * 1.8f);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // ── 第三十八轮新增的四种样式：各自独立的画法，画完直接返回 ──
            switch (style)
            {
                case Aurora: DrawAurora(g, ring, r, color, t); return;
                case Neon: DrawNeon(g, ring, r, color, t); return;
                case Halo: DrawHalo(g, ring, r, color, t); return;
                case Corners: DrawCorners(g, ring, r, color, t); return;
                case Shadow: DrawShadow(g, ring, r, color, t); return;
                case Underline: DrawUnderline(g, ring, r, color, t); return;
                case Brackets: DrawBrackets(g, ring, r, color, t); return;
            }

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

        // ── 第三十八轮新增样式的画法 ─────────────────────────────

        /// <summary>极光：颜色沿 45° 方向由亮到暗过渡，外面垫一层很淡的柔光。</summary>
        private static void DrawAurora(Graphics g, RectangleF ring, float r, Color color, float t)
        {
            using (var path = RoundedPath(ring, r))
            using (var pen = new Pen(Color.FromArgb(38, color), t + 8f))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, path);
            }

            using (var path = RoundedPath(ring, r))
            using (var br = new LinearGradientBrush(ring, Lighten(color, 0.45f), Darken(color, 0.42f), 45f))
            {
                br.InterpolationColors = new ColorBlend(3)
                {
                    Colors = new[] { Lighten(color, 0.48f), color, Darken(color, 0.42f) },
                    Positions = new[] { 0f, 0.5f, 1f },
                };
                using (var pen = new Pen(br, Math.Max(2f, t * 1.35f)))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }
        }

        /// <summary>霓虹：三层外扩强光 + 一条提亮过的"灯管"芯。</summary>
        private static void DrawNeon(Graphics g, RectangleF ring, float r, Color color, float t)
        {
            for (int k = 3; k >= 1; k--)
            {
                int a = 20 + (3 - k) * 22;                 // 外圈最淡，内圈最亮
                using (var path = RoundedPath(ring, r))
                using (var pen = new Pen(Color.FromArgb(a, color), t + 3.4f * k))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }

            using (var path = RoundedPath(ring, r))
            using (var pen = new Pen(Lighten(color, 0.5f), Math.Max(1.6f, t * 0.8f)))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, path);
            }
        }

        /// <summary>光晕：四层大范围、低透明度的弥散，加一条清晰的主环。</summary>
        private static void DrawHalo(Graphics g, RectangleF ring, float r, Color color, float t)
        {
            for (int k = 4; k >= 1; k--)
            {
                int a = 12 + (4 - k) * 15;
                using (var path = RoundedPath(ring, r))
                using (var pen = new Pen(Color.FromArgb(a, color), t + 4.4f * k))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }

            using (var path = RoundedPath(ring, r))
            using (var pen = new Pen(color, t))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, path);
            }
        }

        /// <summary>四角框：只在四个角画一段圆角括线，中间完全不挡内容。</summary>
        /// <summary>
        /// 柔和投影：一圈很宽、很淡的同心描边，像窗口浮起来时落下的影子。
        ///
        /// 为什么不画实线：这是十二种样式里【唯一完全不画边界线】的一种 ——
        /// 只想让窗口"看起来浮起来"，不想在内容边缘加任何一道线时用它。
        ///
        /// 为什么用"所选颜色调暗"而不是纯黑：纯黑在浅色底上像脏了一圈；
        /// 用所选颜色调暗之后，影子仍然属于同一个色系，和别的样式是一套语言。
        /// </summary>
        private static void DrawShadow(Graphics g, RectangleF ring, float r, Color color, float t)
        {
            Color dark = Color.FromArgb(
                (int)(color.R * 0.42f), (int)(color.G * 0.42f), (int)(color.B * 0.42f));

            // 由外向内：越靠外越淡、越宽（叠出柔和的过渡，而不是一圈硬边）
            int[] widths = { 15, 11, 7, 4 };
            int[] alphas = { 12, 18, 26, 38 };
            for (int i = 0; i < widths.Length; i++)
            {
                float grow = widths[i];
                var rc = RectangleF.Inflate(ring, grow * 0.5f, grow * 0.5f);
                float rad = Math.Min(r + grow * 0.5f, Math.Min(rc.Width, rc.Height) / 2f);
                if (rad < 0f) rad = 0f;
                using (var path = RoundedPath(rc, rad))
                using (var pen = new Pen(Color.FromArgb(alphas[i], dark), grow))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
            }

            // 最内侧一条极细的本色线：给一点"边界感"，否则整圈太糊、看不出窗口在哪
            using (var path = RoundedPath(ring, r))
            using (var pen = new Pen(Color.FromArgb(110, color), Math.Max(1f, t * 0.45f)))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, path);
            }
        }

        /// <summary>
        /// 下缘光条：只在窗口底边画一条两端渐隐的光条。
        /// 上半圈完全不动 —— 只想在底部留一个"当前窗口在这里"的提示时用。
        /// </summary>
        private static void DrawUnderline(Graphics g, RectangleF ring, float r, Color color, float t)
        {
            float barH = Math.Max(2f, t * 1.1f);
            float left = ring.Left + ring.Width * 0.10f;
            float right = ring.Right - ring.Width * 0.10f;
            float y = ring.Bottom - barH / 2f;
            if (right <= left) return;

            var linRect = new RectangleF(left, y - barH, right - left, barH * 2f);
            using (var lg = new LinearGradientBrush(linRect, color, color, LinearGradientMode.Horizontal))
            {
                // 水平两端渐隐：整条光是"中间亮、两头化开"，不是一根硬棒
                var blend = new ColorBlend(3);
                blend.Colors = new[]
                {
                    Color.FromArgb(0, color), Color.FromArgb(235, color), Color.FromArgb(0, color),
                };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                lg.InterpolationColors = blend;

                using (var glow = new Pen(lg, barH + 9f))
                {
                    glow.StartCap = LineCap.Round;
                    glow.EndCap = LineCap.Round;
                    g.DrawLine(glow, left, y, right, y);
                }
                using (var pen = new Pen(lg, barH))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, left, y, right, y);
                }
            }
        }

        /// <summary>
        /// 四边中点：四条边的正中各画一小段，像相机的对焦提示。
        /// 与"四角框"刻意区分 —— 那个在四个角上，这个在四条边的中点，
        /// 视觉语言完全不同（一个"框住"，一个"对准"）。
        /// </summary>
        private static void DrawBrackets(Graphics g, RectangleF ring, float r, Color color, float t)
        {
            float len = Math.Max(18f, Math.Min(ring.Width, ring.Height) * 0.22f);
            float cx = ring.Left + ring.Width / 2f;
            float cy = ring.Top + ring.Height / 2f;
            float h = len / 2f;

            using (var pen = new Pen(color, t))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - h, ring.Top, cx + h, ring.Top);       // 上
                g.DrawLine(pen, cx - h, ring.Bottom, cx + h, ring.Bottom); // 下
                g.DrawLine(pen, ring.Left, cy - h, ring.Left, cy + h);     // 左
                g.DrawLine(pen, ring.Right, cy - h, ring.Right, cy + h);   // 右
            }

            // 一圈极淡的辅助环：只有四条短线会显得零碎，加一层若隐若现的轮廓把它们串起来
            using (var path = RoundedPath(ring, r))
            using (var pen = new Pen(Color.FromArgb(34, color), Math.Max(1f, t * 0.38f)))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, path);
            }
        }

        private static void DrawCorners(Graphics g, RectangleF ring, float r, Color color, float t)
        {
            float len = Math.Max(16f, Math.Min(ring.Width, ring.Height) * 0.2f);
            float rad = Math.Min(r, Math.Min(len * 0.7f, Math.Min(ring.Width, ring.Height) / 2f - 1f));
            if (rad < 0f) rad = 0f;

            using (var path = CornerBrackets(ring, rad, len))
            using (var pen = new Pen(color, t))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawPath(pen, path);
            }
        }

        /// <summary>四个角的括线，各成一段独立子路径（不互相连起来）。</summary>
        private static GraphicsPath CornerBrackets(RectangleF rc, float rad, float len)
        {
            var p = new GraphicsPath();
            float d = rad * 2f;

            // 左上：竖线 → 圆角 → 横线
            p.StartFigure();
            p.AddLine(rc.Left, rc.Top + len, rc.Left, rc.Top + rad);
            if (rad > 0.5f) p.AddArc(rc.Left, rc.Top, d, d, 180f, 90f);
            p.AddLine(rc.Left + rad, rc.Top, rc.Left + len, rc.Top);

            // 右上
            p.StartFigure();
            p.AddLine(rc.Right - len, rc.Top, rc.Right - rad, rc.Top);
            if (rad > 0.5f) p.AddArc(rc.Right - d, rc.Top, d, d, 270f, 90f);
            p.AddLine(rc.Right, rc.Top + rad, rc.Right, rc.Top + len);

            // 右下
            p.StartFigure();
            p.AddLine(rc.Right, rc.Bottom - len, rc.Right, rc.Bottom - rad);
            if (rad > 0.5f) p.AddArc(rc.Right - d, rc.Bottom - d, d, d, 0f, 90f);
            p.AddLine(rc.Right - rad, rc.Bottom, rc.Right - len, rc.Bottom);

            // 左下
            p.StartFigure();
            p.AddLine(rc.Left + len, rc.Bottom, rc.Left + rad, rc.Bottom);
            if (rad > 0.5f) p.AddArc(rc.Left, rc.Bottom - d, d, d, 90f, 90f);
            p.AddLine(rc.Left, rc.Bottom - rad, rc.Left, rc.Bottom - len);

            return p;
        }

        private static Color Lighten(Color c, float amt)
        {
            return Color.FromArgb(c.A, Mix(c.R, 255, amt), Mix(c.G, 255, amt), Mix(c.B, 255, amt));
        }

        private static Color Darken(Color c, float amt)
        {
            return Color.FromArgb(c.A, Mix(c.R, 0, amt), Mix(c.G, 0, amt), Mix(c.B, 0, amt));
        }

        private static int Mix(int v, int target, float amt)
        {
            int r = (int)Math.Round(v + (target - v) * amt);
            if (r < 0) r = 0;
            if (r > 255) r = 255;
            return r;
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

        // ══════════════════════════════════════════════════════════════
        // 外框几何：外扩 or 内缩
        // ══════════════════════════════════════════════════════════════
        //
        // 【问题】外框默认画在窗口可见边沿上，柔光还要再往外漫一圈（margin）。
        //   窗口一旦贴着屏幕边缘（靠边停放、尤其是【最大化】），外扩部分整体
        //   移出屏幕被系统裁掉 —— 最大化时四条边全在屏幕外，外框"完全看不到"。
        //   这就是用户反馈的"窗口小的时候能看见外框样式，窗口一放大就没了"。
        //
        // 【解法】外扩矩形整个放不进虚拟桌面时，切换成【内缩】绘制：
        //   浮层不再外扩，直接等于窗口可见矩形；Draw() 的 margin 内缩让圆环
        //   落在窗口内侧 margin 处，柔光朝窗口内部漫（那里有 margin 的空间，
        //   不会被位图边界切掉）。框永远在屏幕内，永远可见。
        //
        //   两个模式共用同一个 Draw()：区别只在调用方传进来的 area 是
        //   "窗口+margin"（外扩）还是"窗口本身"（内缩）。

        /// <summary>
        /// 窗口是否"几乎铺满整块显示器" —— 只有这时才值得切到内缩绘制。
        ///
        /// ★★★ 为什么不能沿用"外扩矩形放不放得进屏幕"作为判据 ★★★
        ///
        ///   那个判据会在窗口【刚碰到屏幕边缘】时就翻面，于是拖动过程中：
        ///     · 外框从"外扩"跳到"内缩"，相对窗口的偏移量突变 2×margin
        ///       （200% 屏上 48 物理像素，视觉上猛跳一下）；
        ///     · 之后再往外拖，内缩分支还会把矩形【钳回屏幕】——
        ///       框越拖越小、每帧都在变。
        ///
        ///   用户报的正是这个："窗口被拖到屏幕边缘、甚至超出屏幕之外时，
        ///   包住窗口的那一圈样式就会缩小……会直观影响流畅程度，显得很卡。"
        ///   根因就是：外框几何开始依赖【屏幕边缘】，而不再只依赖窗口。
        ///
        ///   正确判据是"窗口是不是已经大到外框必然全部落在屏幕外"：
        ///     · 普通窗口贴边 / 半出屏时，外框只是【部分】被屏幕裁掉，
        ///       其余几边照样可见 —— 完全没必要改变几何，保持外扩即可。
        ///       几何恒定 = 拖动过程零跳变 = 丝滑。
        ///     · 只有最大化 / 铺满整屏时，外框四条边才全在屏幕外，
        ///       这时才必须切到内缩，否则框彻底看不见。
        ///
        ///   wasInside 用于回滞（进 92%、退 88%），避免在阈值上反复翻面。
        /// </summary>
        public static bool FillsScreen(Native.RECT vis, int margin, bool wasInside)
        {
            try
            {
                int mw = 0, mh = 0;
                try
                {
                    var mi = Native.GetMonitorInfoAt(
                        new Native.POINT((vis.Left + vis.Right) / 2, (vis.Top + vis.Bottom) / 2));
                    mw = mi.rcMonitor.Width;
                    mh = mi.rcMonitor.Height;
                }
                catch { }

                // ★ 显示器信息拿不到时，退回虚拟桌面尺寸 ★
                //   原来这里是 `return false`（= 一律外扩）。而"铺满屏幕的窗口"一旦走外扩，
                //   整圈框都在屏幕之外 —— 屏幕上【一根线都看不到】，用户看到的就是"没有外框"。
                //   虚拟桌面尺寸几乎不可能失败，用它兜底至少能判对"这窗口是不是铺满了屏"。
                if (mw <= 0 || mh <= 0)
                {
                    var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
                    mw = vs.Width;
                    mh = vs.Height;
                }
                if (mw <= 0 || mh <= 0) return false;

                int pct = wasInside ? 88 : 92;
                return vis.Width >= (long)mw * pct / 100
                    && vis.Height >= (long)mh * pct / 100;
            }
            catch
            {
                return false;   // 取不到显示器信息时保持外扩（老行为，安全）
            }
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
