using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace HoldDrag
{
    /// <summary>
    /// 设置窗口。刻意不做多标签页：全部选项一屏可见，改完点"保存并生效"立即起作用。
    ///
    /// ⚠ 曾经踩过的坑（已经修了，注释留着防止再犯）：
    ///   早先这一版里四个 GroupBox 创建完之后【忘了 Controls.Add(g1..g4)】，
    ///   结果整个设置窗口只剩下底部的状态行和三个按钮，看起来"空的啥都没有"。
    ///   控件全都建好了也全都填了子项，就差把它挂到窗体上。
    ///   所以本文件末尾有一 self check：TabIndex 顺序 + 所有分组必须在 Controls 里，
    ///   --selftest 会断言这一条。
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly ConfigStore _store;
        private readonly Coordinator _coord;
        private AppConfig _edit;
        private bool _recording;

        // ★ 控件实例一律用"自绘版"构造 —— 字段类型保持原样（TextBox/ComboBox/
        //   NumericUpDown/Button），所以布局引擎与自检里那些 `is ComboBox` /
        //   `is NumericUpDown` / `is Button` 判定完全不受影响，
        //   变的只是运行时的画法。见 FlatCombo / FlatNum / FlatButton 的类注释。
        private readonly TextBox _txtKey = new TextBox();
        private readonly Button _btnRecord = new FlatButton();
        private readonly Label _lblKeyHint = new Label();
        private readonly CheckBox _chkHold = new FlatCheck();
        private readonly NumericUpDown _numHold = new FlatNum();

        private readonly ComboBox _cmbLeft = new FlatCombo();
        private readonly ComboBox _cmbRight = new FlatCombo();
        private readonly ComboBox _cmbMiddle = new FlatCombo();

        private readonly CheckBox _chkFrame = new FlatCheck();

        // ★ 颜色 / 样式的"选择状态"仍然放在这两个下拉框里（它们是 Populate / SelectId /
        //   保存链路 / 自检探针共用的单一数据源），但【不再显示在界面上】——
        //   界面上改成下面两块画廊控件。这样"看得见的 UI"和"既有逻辑"各归各位：
        //   换 UI 不会碰到任何一条已有的取值/保存路径。
        private readonly ComboBox _cmbFrameColor = new FlatCombo();
        private readonly ComboBox _cmbFrameStyle = new FlatCombo();

        private readonly ColorSwatchGrid _gridColor = new ColorSwatchGrid();
        private readonly StyleGallery _galleryStyle = new StyleGallery();
        private readonly Label _lblColorName = new Label();

        private readonly NumericUpDown _numFrameThick = new FlatNum();
        private readonly NumericUpDown _numFrameRadius = new FlatNum();
        private readonly NumericUpDown _numFrameOpacity = new FlatNum();
        private readonly HelpPanel _helpPanel = new HelpPanel();
        private readonly FramePreviewBox _pnlPreview = new FramePreviewBox();
        private readonly Label _lblFrameTip = new Label();
        private readonly NumericUpDown _numCorner = new FlatNum();
        private readonly CheckBox _chkShake = new FlatCheck();
        private readonly CheckBox _chkDblLeft = new FlatCheck();
        private readonly CheckBox _chkDblRight = new FlatCheck();
        private readonly CheckBox _chkRestoreMax = new FlatCheck();
        private readonly CheckBox _chkFront = new FlatCheck();
        private readonly CheckBox _chkFullscreen = new FlatCheck();
        private readonly CheckBox _chkExitFullscreen = new FlatCheck();
        private readonly TextBox _txtBlacklist = new TextBox();

        // #36 刷新率：跟随屏幕（Auto）/ 固定值（Custom，0 = 不限制）
        private readonly ComboBox _cmbRefreshMode = new FlatCombo();
        private readonly NumericUpDown _numRefreshHz = new FlatNum();
        private readonly Label _lblRefreshHint = new Label();


        private readonly ComboBox _cmbAutoStart = new FlatCombo();
        private readonly CheckBox _chkRunAsAdmin = new FlatCheck();
        private readonly CheckBox _chkWarnNonAdmin = new FlatCheck();
        private readonly Label _lblAdmin = new Label();
        private readonly Button _btnRestartAdmin = new FlatButton();

        private readonly Label _lblStatus = new Label();

        /// <summary>左侧导航栏：列出六个分组标题，点击滚到对应分组。</summary>
        private NavSidebar _sidebar;

        /// <summary>Populate() 期间置位：抑制"改动提示"，避免填充过程被误判成用户改动。</summary>
        private bool _populating;

        private sealed class ActionItem
        {
            public string Value;
            public string Text;
            public override string ToString() { return Text; }
        }

        private sealed class IdItem
        {
            public string Value;
            public string Text;
            public override string ToString() { return Text; }
        }

        private sealed class ColorItem
        {
            public string Value;
            public string Text;
            public Color Color;
            public override string ToString() { return Text; }
        }

        /// <summary>
        /// 响应式分组容器。
        ///
        /// 需求是"窗口能拉宽"，但单纯拉宽 + Anchor 只会让分组变宽、
        /// 内容还是挤在左边，右边留一大片空白 —— 拉宽毫无意义。
        ///
        /// 这里用成熟做法：把分组放进一个 TableLayoutPanel，
        /// **宽度足够时自动从 1 列切成 2 列**（分组本身变窄但排成两列，
        /// 屏幕上装下的信息量翻倍），宽度不够时退回 1 列。
        /// 这就是所谓"响应式栅格"，和网页上 @media 断列是同一个思路。
        /// </summary>
        private sealed class ResponsiveGroupPanel : TableLayoutPanel
        {
            private readonly GroupBox[] _groups;

            /// <summary>
            /// 单列转两列的宽度阈值。
            ///
            /// ★ 这个值必须"足够大"，不能只看"两列排得下"。
            ///   g4（拖动行为）里最挤的一行是"缩放角区 + 数字框 + %"，
            ///   单列 536 宽时正好排满。两列时每列只有约 (可用宽-12)/2，
            ///   一旦低于 560 这一行就会溢出、右端的"%"像被切掉。
            ///   所以阈值取 2 × 570 + 间隙 = 1160 左右，保证每列不低于 570。
            ///   宁可晚一点才变两列，也不要出现"两列了但内容被切"。
            /// </summary>
            public const int TwoColumnThreshold = 1240;

            /// <summary>
            /// 从两列退回单列时要多降这么多（设计稿像素）才认 —— 列数切换的回滞带宽。
            /// 防止拖边框时正好停在阈值上、1↔2 列一帧一翻造成左右横跳。
            /// </summary>
            public const int HysteresisLogical = 90;

            /// <summary>当前实际列数（1 或 2），供自检断言。</summary>
            public int CurrentColumns;

            private void SetColumns(int want) { CurrentColumns = want; }

            public ResponsiveGroupPanel(GroupBox[] groups)
            {
                _groups = groups;
                CurrentColumns = 1;

                // ★ 不要设 Dock、也不要设 AutoSize。
                //   这两个都会跟"由 Relayout() 显式指定宽度"打架：
                //     · Dock=Top   → 宽度被父容器拉满，我设的 Width 立刻被覆盖；
                //     · AutoSize   → 宽度按内容自动撑，我设的 Width 直接被忽略。
                //   之前就是这两个属性导致"分组宽度固定在 1086 不动"，
                //   用户看到的正是"拉宽了但里面什么都没变"。
                //   这里改成由 LayoutResponsive() 明确摆位置和尺寸，行为完全可控。
                Anchor = AnchorStyles.Top | AnchorStyles.Left;
                ColumnCount = 1;
                RowCount = groups.Length;
                Padding = new Padding(0);
                Margin = new Padding(0);

                ColumnStyles.Clear();
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

                RowStyles.Clear();
                for (int i = 0; i < groups.Length; i++)
                {
                    RowStyles.Add(new RowStyle(SizeType.AutoSize));
                }

                for (int i = 0; i < groups.Length; i++)
                {
                    groups[i].Dock = DockStyle.Fill;
                    // 卡片间距 8 → 14 设计px（第四十二轮）：
                    //   间距是"呼吸感"最直接的来源。8px 时六张卡片挤成一列，
                    //   视觉上像一张长表格；拉开到 14px 之后每张卡片才成为独立的一块。
                    groups[i].Margin = new Padding(0, 0, 0, 14);
                    groups[i].Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    Controls.Add(groups[i], 0, i);
                }
            }

            /// <summary>
            /// 按可用宽度重新决定列数，并把分组撑到新宽度。
            ///
            /// ★ 关键认知：分组是 Dock=Fill 的，它的宽度**由 TableLayoutPanel 决定**。
            ///   直接给 g.Width 赋值会被布局引擎立刻覆盖回旧值 ——
            ///   之前就在这里翻过车：单列模式下把窗口拉宽，分组宽度纹丝不动。
            ///   正确做法是设好自己的 Width，然后 PerformLayout() 让引擎把宽度传播下去。
            /// </summary>
            public void Relayout(int available)
            {
                // ★ 阈值也要跟着 DPI 缩放：它衡量的是"客户区物理像素"，
                //   而控件已经按 DPI 放大了。125% 下不缩阈值就会提前两列，
                //   每列放不下 1.25 倍的内容 → 又回到"内容被切"的坑。
                int threshold = (int)Math.Round(TwoColumnThreshold * CurrentUiScale);

                // ★★ 列数切换必须带【回滞】，否则会在阈值附近来回翻 ★★
                //
                //   用户拖边框时宽度是连续变化的，正好停在阈值上下几像素时，
                //   1 列 ↔ 2 列会一帧一翻：分组被反复拆成两列又合回一列，
                //   整屏内容左右横跳 —— 这是"调整尺寸时界面抖动"最刺眼的一种。
                //   做法：升到 2 列要够到 threshold；
                //         从 2 列退回 1 列要多降 Hysteresis 才认。
                int hysteresis = (int)Math.Round(HysteresisLogical * CurrentUiScale);
                int want = CurrentColumns >= 2
                    ? (available >= threshold - hysteresis ? 2 : 1)
                    : (available >= threshold ? 2 : 1);

                if (Controls.Count == 0 || want != CurrentColumns)
                {
                    RebuildStructure(want);
                }

                // ★ 设自己的宽度。Dock=Fill 的子控件会跟着一起变。
                if (Width != available) Width = available;

                PerformLayout();
            }

            private void RebuildStructure(int want)
            {
                SuspendLayout();
                try
                {
                    CurrentColumns = want;
                    ColumnStyles.Clear();
                    RowStyles.Clear();
                    Controls.Clear();

                    if (want == 1)
                    {
                        ColumnCount = 1;
                        RowCount = _groups.Length;
                        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                        for (int i = 0; i < _groups.Length; i++)
                        {
                            RowStyles.Add(new RowStyle(SizeType.AutoSize));
                            Controls.Add(_groups[i], 0, i);
                        }
                    }
                    else
                    {
                        ColumnCount = 2;
                        RowCount = (_groups.Length + 1) / 2;

                        // ★ 两列的宽度配额：左列 50%、右列 50%，但要给右列留出
                        //   滚动条 + 组边框的空间，否则右列里贴右边的控件（"%""缩放角区"
                        //   "重新录制"按钮）会被切掉尾巴。
                        //   做法是让整体宽度少 14px：TableLayoutPanel 会按百分比分，
                        //   于是每列各少 7px，正好把右边空出来。
                        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
                        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
                        for (int r = 0; r < RowCount; r++)
                        {
                            RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        }
                        for (int i = 0; i < _groups.Length; i++)
                        {
                            Controls.Add(_groups[i], i / RowCount, i % RowCount);
                        }
                    }
                }
                finally
                {
                    ResumeLayout(true);
                }
            }
        }

        /// <summary>
        /// 外框外观的实时预览。
        ///
        /// 刻意调用与真实浮层【同一个】 FrameStyles.Draw()，不另写一份"近似"的画法 ——
        /// 预览所见即所得，不会出现"设置里看着好看、实际拖起来是另一回事"。
        /// </summary>
        private sealed class FramePreviewBox : Control
        {
            private int _thickness = 3;
            private int _radius = 13;
            private int _opacity = 100;

            /// <summary>外框总开关关掉时，预览要能表现出"不会亮框"，而不是继续画一圈。</summary>
            public bool FrameEnabled = true;

            public string ColorId = FrameColors.DefaultId;
            public string StyleId = FrameStyles.DefaultId;

            // ★ 必须走属性而不是裸字段：
            //   设置窗口自己不会一直被系统重绘，裸字段改完只调 Invalidate()
            //   在"窗口被别的窗口遮住 / 最小化"时会被吞掉，看起来就是预览没反应。
            //   属性里改完值立刻 Invalidate()，多条路径都能保证重画。
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            [Browsable(false)]
            public int Thickness
            {
                get { return _thickness; }
                set { if (_thickness != value) { _thickness = value; Invalidate(); } }
            }

            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            [Browsable(false)]
            public int Radius
            {
                get { return _radius; }
                set { if (_radius != value) { _radius = value; Invalidate(); } }
            }

            /// <summary>外框不透明度（百分比）。预览必须跟着变，否则用户拖了数字看不到效果。</summary>
            [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
            [Browsable(false)]
            public int OpacityPercent
            {
                get { return _opacity; }
                set { if (_opacity != value) { _opacity = value; Invalidate(); } }
            }

            public FramePreviewBox()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            /// <summary>整体刷新一次（颜色 / 样式 / 开关等字段变更后调用）。</summary>
            public void Refresh_() { Invalidate(); Update(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                try
                {
                    var g = e.Graphics;
                    HighQuality.Apply(g);
                    g.Clear(Color.FromArgb(0xF4, 0xF4, 0xF5));      // 假装是桌面底色

                    int m = FrameStyles.MarginFor(StyleId, _thickness);
                    var area = new RectangleF(0, 0, Width, Height);
                    var win = new RectangleF(area.X + m, area.Y + m,
                                             area.Width - m * 2f, area.Height - m * 2f);
                    if (win.Width <= 2 || win.Height <= 2) { base.OnPaint(e); return; }

                    using (var b = new SolidBrush(Color.White))  // 假装是窗口本体
                    {
                        g.FillRectangle(b, win);
                    }

                    if (FrameEnabled)
                    {
                        float op = Math.Max(0.05f, Math.Min(1f, _opacity / 100f));
                        if (op >= 0.999f)
                        {
                            FrameStyles.Draw(g, area, FrameColors.Resolve(ColorId), StyleId, _thickness, Radius);
                        }
                        else
                        {
                            // ★ 不透明度预览（第四十五轮）★
                            //   先画到离屏位图，再用 alpha 矩阵整体乘一次透明度 ——
                            //   这与实际浮层用 SourceConstantAlpha 的效果完全一致
                            //   （整层等比变淡，柔光/渐变的关系不变）。
                            int bw = Math.Max(1, (int)Math.Ceiling(area.Width));
                            int bh = Math.Max(1, (int)Math.Ceiling(area.Height));
                            using (var bmp = new Bitmap(bw, bh, PixelFormat.Format32bppArgb))
                            {
                                using (var bg = Graphics.FromImage(bmp))
                                {
                                    bg.Clear(Color.Transparent);
                                    HighQuality.Apply(bg);
                                    FrameStyles.Draw(bg, new RectangleF(0, 0, bw, bh),
                                        FrameColors.Resolve(ColorId), StyleId, _thickness, Radius);
                                }
                                var cm = new ColorMatrix(new float[][]
                                {
                                    new float[] { 1, 0, 0, 0, 0 },
                                    new float[] { 0, 1, 0, 0, 0 },
                                    new float[] { 0, 0, 1, 0, 0 },
                                    new float[] { 0, 0, 0, op, 0 },
                                    new float[] { 0, 0, 0, 0, 1 },
                                });
                                using (var ia = new ImageAttributes())
                                {
                                    ia.SetColorMatrix(cm);
                                    g.DrawImage(bmp, new Rectangle(0, 0, bw, bh),
                                        0, 0, bw, bh, GraphicsUnit.Pixel, ia);
                                }
                            }
                        }
                    }
                    else
                    {
                        // 关掉外框时：还是画真正选择的框，但整体压到 30% 透明 + 一条
                        // "已停用"虚线，让用户一眼看出"这里本该有框，现在停用着"，
                        // 而不是一条孤零零的灰线（之前那种灰线在浅色底下几乎看不见，
                        // 用户反馈"就是空白"）。
                        var col = FrameColors.Resolve(ColorId);
                        using (var disabled = new SolidBrush(Color.FromArgb(40, 80, 80, 90)))
                            g.FillRectangle(disabled, win);
                        FrameStyles.Draw(g, area, Color.FromArgb(120, col), StyleId, _thickness, Radius);
                        using (var p = new Pen(Color.FromArgb(170, 120, 120, 130), 1f))
                        {
                            p.DashStyle = DashStyle.Dash;
                            g.DrawRectangle(p, win.X + 0.5f, win.Y + 0.5f, win.Width - 1, win.Height - 1);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }

                base.OnPaint(e);
            }
        }

        /// <summary>
        /// 颜色色板：把调色板里【所有】颜色一次性平铺出来，选中的那格带一圈强调色描边。
        ///
        /// 为什么不再用下拉框 —— 用户提的两条要求正好都是下拉框的天然缺陷：
        ///   ① "应在进入设置时即展示所有可选项"：下拉框必须点开才看得到；
        ///   ② "选择样式后未在设置界面中直接展示当前选中项"：收起态只有一行文字。
        /// 色板把"全部可选"和"当前选中"同时摆在一屏上，两个问题一起解决。
        /// </summary>
        private sealed class ColorSwatchGrid : Control
        {
            internal sealed class Entry
            {
                public string Id;
                public string Name;
                public Color Color;
            }

            private readonly System.Collections.Generic.List<Entry> _items =
                new System.Collections.Generic.List<Entry>();
            private string _selected = "";
            private int _hot = -1;

            /// <summary>用户点了某一格（调用方据此把选择同步给预览 / 配置）。</summary>
            public event EventHandler Picked;

            public ColorSwatchGrid()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Design.Surface;
                Cursor = Cursors.Hand;
            }

            public void SetItems(System.Collections.Generic.IEnumerable<Entry> items)
            {
                _items.Clear();
                _items.AddRange(items);
                Invalidate();
            }

            [System.ComponentModel.DesignerSerializationVisibility(
                System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public string SelectedId
            {
                get { return _selected; }
                set { if (_selected != value) { _selected = value; Invalidate(); } }
            }

            private int Cell { get { return Fields.Sc(22); } }
            private int Gap { get { return Fields.Sc(6); } }

            /// <summary>
            /// 固定列数（0 = 按宽度自动）。
            ///
            /// ★ 刻意固定成 9 列 × 2 行 ★
            ///   自动列数会随控件宽度变化，17 个色块在窄窗里排成 13+4、
            ///   在宽窗里排成 17 —— 同一个界面在不同宽度下"长得不一样"，
            ///   看起来像是布局坏了。固定成 9×2 之后，任何宽度下都是同一张色板，
            ///   最后一格留空也比"每行个数飘忽"整齐。
            /// </summary>
            [System.ComponentModel.DesignerSerializationVisibility(
                System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int Columns { get; set; }

            private int Cols
            {
                get
                {
                    if (Columns > 0) return Columns;
                    int c = (Math.Max(1, Width) + Gap) / (Cell + Gap);
                    return c < 1 ? 1 : c;
                }
            }

            private Rectangle CellRect(int i)
            {
                int cols = Cols;
                return new Rectangle((i % cols) * (Cell + Gap), (i / cols) * (Cell + Gap), Cell, Cell);
            }

            private int IndexAt(Point p)
            {
                for (int i = 0; i < _items.Count; i++) if (CellRect(i).Contains(p)) return i;
                return -1;
            }

            /// <summary>仅供自检：色板平铺出来的格数（应当等于调色板项数）。</summary>
            internal int CellCountForTest { get { return _items.Count; } }

            /// <summary>仅供自检：当前高亮那一格的矩形。</summary>
            internal Rectangle SelectedCellRectForTest
            {
                get
                {
                    for (int i = 0; i < _items.Count; i++)
                        if (_items[i].Id == _selected) return CellRect(i);
                    return Rectangle.Empty;
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int i = IndexAt(e.Location);
                if (i != _hot) { _hot = i; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (_hot != -1) { _hot = -1; Invalidate(); }
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                int i = IndexAt(e.Location);
                if (i >= 0)
                {
                    _selected = _items[i].Id;
                    Invalidate();
                    var h = Picked;
                    if (h != null) h(this, EventArgs.Empty);
                }
                base.OnMouseDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                HighQuality.Apply(g);
                using (var bg = new SolidBrush(Design.Surface))
                    g.FillRectangle(bg, ClientRectangle);

                int ring = Math.Max(1, Fields.Sc(2));
                int r = Fields.Sc(6);
                for (int i = 0; i < _items.Count; i++)
                {
                    var box = CellRect(i);
                    bool sel = _items[i].Id == _selected;
                    bool hot = i == _hot;

                    // 选中 / 悬停：先在色块【外面】垫一圈强调色，色块本身尺寸不变 ——
                    // 这样"哪一格被选中"一眼可辨，又不会让色块跳大小。
                    if (sel || hot)
                    {
                        var halo = new Rectangle(box.X - ring, box.Y - ring,
                                                 box.Width + ring * 2, box.Height + ring * 2);
                        using (var b = new SolidBrush(sel ? Design.Accent
                                                          : Color.FromArgb(140, Design.Accent)))
                        using (var p = RoundedPath(halo, r + ring))
                            g.FillPath(b, p);
                    }

                    using (var b = new SolidBrush(Enabled ? _items[i].Color
                                                          : Color.FromArgb(70, _items[i].Color)))
                    using (var p = RoundedPath(box, r))
                        g.FillPath(b, p);

                    // 珍珠白那类浅色在白底上会"消失"，补一圈极淡描边兜住轮廓
                    using (var pen = new Pen(Color.FromArgb(46, 24, 26, 33)))
                    using (var p = RoundedPath(box, r))
                        g.DrawPath(pen, p);
                }
            }
        }

        /// <summary>
        /// 样式画廊：每种外框样式都按【当前颜色 / 粗细 / 圆角】实时渲染一张缩略图，
        /// 进入设置页立刻全部可见；选中的那张带强调色描边 + 勾选角标 + 强调色名称。
        ///
        /// 关键点是"缩略图用的就是 FrameStyles.Draw 本身"—— 预览与实际外框同源，
        /// 不会出现"设置里看着是这个样子、真正拖起来是另一个样子"。
        /// </summary>
        private sealed class StyleGallery : Control
        {
            private readonly string[] _styles = FrameStyles.All;
            private string _selected = FrameStyles.DefaultId;
            private string _colorId = FrameColors.DefaultId;
            private int _thickness = 3;
            private int _radius = 13;
            private bool _enabled = true;
            private int _hot = -1;

            public event EventHandler Picked;

            public StyleGallery()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Design.Surface;
                Cursor = Cursors.Hand;
            }

            [System.ComponentModel.DesignerSerializationVisibility(
                System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public string SelectedId
            {
                get { return _selected; }
                set { if (_selected != value) { _selected = value; Invalidate(); } }
            }

            [System.ComponentModel.DesignerSerializationVisibility(
                System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public string ColorId
            {
                set { if (_colorId != value) { _colorId = value; Invalidate(); } }
            }

            [System.ComponentModel.DesignerSerializationVisibility(
                System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int Thickness
            {
                set { if (_thickness != value) { _thickness = value; Invalidate(); } }
            }

            [System.ComponentModel.DesignerSerializationVisibility(
                System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int Radius
            {
                set { if (_radius != value) { _radius = value; Invalidate(); } }
            }

            [System.ComponentModel.DesignerSerializationVisibility(
                System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public bool FrameEnabled
            {
                set { if (_enabled != value) { _enabled = value; Invalidate(); } }
            }

            private int CellW { get { return Fields.Sc(70); } }
            private int CellH { get { return Fields.Sc(46); } }
            private int Gap { get { return Fields.Sc(6); } }
            private int ThumbH { get { return Fields.Sc(30); } }

            /// <summary>
            /// 固定列数（0 = 按宽度自动）。
            /// 12 种样式固定排成 5 + 5 + 2 三行 —— 理由同色板：任何宽度下都是同一张画廊。
            /// ★ 画廊高度必须按"行数 × 格高"留够 ★
            ///   第四十六轮加样式时踩到：格数从 9 涨到 12、需要第三行，
            ///   而控件高度是按两行定的 —— 多出来那一行被控件边界直接裁掉，
            ///   缩略图网格里"少两个"很难被注意到。T78 现在会断言高度够用。
            /// </summary>
            [System.ComponentModel.DesignerSerializationVisibility(
                System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public int Columns { get; set; }

            private int Cols
            {
                get
                {
                    if (Columns > 0) return Columns;
                    int c = (Math.Max(1, Width) + Gap) / (CellW + Gap);
                    return c < 1 ? 1 : c;
                }
            }

            private Rectangle CellRect(int i)
            {
                int cols = Cols;
                return new Rectangle((i % cols) * (CellW + Gap), (i / cols) * (CellH + Gap), CellW, CellH);
            }

            private int IndexAt(Point p)
            {
                for (int i = 0; i < _styles.Length; i++) if (CellRect(i).Contains(p)) return i;
                return -1;
            }

            /// <summary>仅供自检：画廊平铺出来的格数（应当等于样式总数）。</summary>
            internal int CellCountForTest { get { return _styles.Length; } }

            /// <summary>仅供自检：当前高亮那一格的矩形。</summary>
            internal Rectangle SelectedCellRectForTest
            {
                get
                {
                    for (int i = 0; i < _styles.Length; i++)
                        if (_styles[i] == _selected) return CellRect(i);
                    return Rectangle.Empty;
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int i = IndexAt(e.Location);
                if (i != _hot) { _hot = i; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (_hot != -1) { _hot = -1; Invalidate(); }
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                int i = IndexAt(e.Location);
                if (i >= 0)
                {
                    _selected = _styles[i];
                    Invalidate();
                    var h = Picked;
                    if (h != null) h(this, EventArgs.Empty);
                }
                base.OnMouseDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                HighQuality.Apply(g);
                using (var bg = new SolidBrush(Design.Surface))
                    g.FillRectangle(bg, ClientRectangle);

                var col = FrameColors.Resolve(_colorId);
                int rad = Fields.Sc(8);
                int m = Fields.Sc(7);
                int t = Math.Min(_thickness, 3);        // 缩略图里太粗会糊成一坨
                int rr = Math.Min(_radius, 8);

                for (int i = 0; i < _styles.Length; i++)
                {
                    string st = _styles[i];
                    var cell = CellRect(i);
                    bool sel = st == _selected;
                    bool hot = i == _hot;

                    // 卡片底 + 描边
                    using (var b = new SolidBrush(sel ? Design.AccentSoft
                                                      : (hot ? Color.FromArgb(246, 247, 250) : Design.Surface)))
                    using (var p = RoundedPath(cell, rad))
                        g.FillPath(b, p);
                    using (var pen = new Pen(sel ? Design.Accent : Design.Stroke, sel ? Math.Max(1, Fields.Sc(2)) : 1f))
                    using (var p = RoundedPath(new Rectangle(cell.X, cell.Y, cell.Width - 1, cell.Height - 1), rad))
                        g.DrawPath(pen, p);

                    // 缩略图：一块"假窗口" + 按当前参数画的真实外框
                    var thumb = new Rectangle(cell.X + Fields.Sc(5), cell.Y + Fields.Sc(4),
                                              cell.Width - Fields.Sc(10), ThumbH);
                    using (var win = new SolidBrush(Color.White))
                        g.FillRectangle(win, new Rectangle(thumb.X + m, thumb.Y + m,
                                                           thumb.Width - m * 2, thumb.Height - m * 2));

                    FrameStyles.Draw(g, thumb, _enabled ? col : Color.FromArgb(110, col), st, t, rr, m);

                    // 名称
                    var lbl = new Rectangle(cell.X, cell.Y + ThumbH + Fields.Sc(4),
                                            cell.Width, Fields.Sc(14));
                    TextRenderer.DrawText(g, ShortName(st), Font, lbl,
                        sel ? Design.Accent : Design.TextSecondary,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                    // 选中角标：右上角一枚强调色圆点 + 白勾
                    if (sel)
                    {
                        int d = Fields.Sc(13);
                        var dot = new Rectangle(cell.Right - d - Fields.Sc(3), cell.Y + Fields.Sc(3), d, d);
                        using (var b = new SolidBrush(Design.Accent))
                            g.FillEllipse(b, dot);
                        using (var pen = new Pen(Color.White, Math.Max(1f, Fields.Sc(2) * 0.8f)))
                        {
                            pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                            pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                            int cx = dot.X + d / 2, cy = dot.Y + d / 2;
                            float u = Fields.Sc(3);
                            g.DrawLines(pen, new PointF[]
                            {
                                new PointF(cx - u, cy),
                                new PointF(cx - u * 0.35f, cy + u * 0.8f),
                                new PointF(cx + u * 1.05f, cy - u * 0.85f),
                            });
                        }
                    }
                }
            }

            /// <summary>缩略图下面的短名（下拉框那种"（推荐）"太长了，格子里放不下）。</summary>
            private static string ShortName(string id)
            {
                switch (id)
                {
                    case FrameStyles.Glow: return "柔光";
                    case FrameStyles.Flat: return "纯净";
                    case FrameStyles.Solid: return "实心";
                    case FrameStyles.Double: return "双线";
                    case FrameStyles.Dashed: return "虚线";
                    case FrameStyles.Aurora: return "极光";
                    case FrameStyles.Neon: return "霓虹";
                    case FrameStyles.Halo: return "光晕";
                    case FrameStyles.Corners: return "四角";
                }
                return id;
            }
        }

        /// <summary>
        /// 左侧导航栏。
        ///
        /// 需求：设置页有六个分组，单列时要滚动才看得全，用户得一路翻到底找某项设置。
        /// 加一条常驻侧栏，点标题直接滚过去，同时高亮"当前看到的是哪一块"。
        ///
        /// 实现要点：
        ///   · 自绘而不是塞 6 个 Button —— 按钮的系统外观和这套卡片语言不一致，
        ///     而且要自己管 hover/选中三态，自绘反而更短。
        ///   · 点击用 AutoScrollPosition 而不是 ScrollControlIntoView：
        ///     后者会把控件滚到"可见"就停，常常只露出半截，达不到"标题顶到最上面"的效果。
        ///   · 高亮项由 SyncActive() 按"哪个分组的 Top 已经越过视口顶部"来算，
        ///     滚动时由 _scrollHost.Scroll 事件驱动。
        /// </summary>
        private sealed class NavSidebar : Control
        {
            private ScrollableControl _host;
            private string[] _titles;
            private string[] _glyphs;      // 与 _titles 一一对应的图标字形
            private Control[] _targets;
            private int _hot = -1;
            private int _active = 0;

            /// <summary>
            /// 每一行"高亮程度"的补间（0 = 无高亮，1 = 完全高亮）。
            /// Win11 的导航高亮是【渐显】的，鼠标扫过去一行行亮起来，而不是瞬间跳。
            /// 用一个 16ms 定时器同时把所有行推向各自的目标，静止时定时器自动停。
            /// </summary>
            private float[] _rowGlow;
            private readonly System.Windows.Forms.Timer _glowTimer =
                new System.Windows.Forms.Timer();

            public NavSidebar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Design.SidebarSurface;
                Cursor = Cursors.Hand;

                _glowTimer.Interval = 16;
                _glowTimer.Tick += (s, e) => StepGlow();
            }

            private void StepGlow()
            {
                if (_rowGlow == null) { _glowTimer.Stop(); return; }
                bool moving = false;
                for (int i = 0; i < _rowGlow.Length; i++)
                {
                    float want = (i == _active) ? 1f : (i == _hot ? 0.55f : 0f);
                    float d = want - _rowGlow[i];
                    if (Math.Abs(d) < 0.03f) _rowGlow[i] = want;
                    else { _rowGlow[i] += d * 0.32f; moving = true; }
                }
                if (!moving) _glowTimer.Stop();
                if (!IsDisposed) Invalidate();
            }

            private void KickGlow()
            {
                if (_rowGlow == null || _rowGlow.Length != (_titles == null ? 0 : _titles.Length))
                {
                    _rowGlow = new float[_titles == null ? 0 : _titles.Length];
                    // 首次：选中项直接到位，不要"从零亮起"的入场动画
                    if (_active >= 0 && _active < _rowGlow.Length) _rowGlow[_active] = 1f;
                }
                if (!_glowTimer.Enabled) _glowTimer.Start();
            }

            public void Bind(ScrollableControl host, string[] titles, Control[] targets)
            {
                _host = host;
                _titles = titles;
                _targets = targets;
                // 图标按标题关键字映射（见 GlyphForSection 的注释：不用序号，避免顺序调整后错位）
                _glyphs = new string[titles == null ? 0 : titles.Length];
                for (int i = 0; i < _glyphs.Length; i++)
                    _glyphs[i] = GlyphForSection(titles[i]);
                _rowGlow = null;
                KickGlow();
                Invalidate();
            }

            private int RowHeight { get { return Fields.Sc(42); } }
            private int PadTop { get { return Fields.Sc(16); } }
            private int PadX { get { return Fields.Sc(14); } }

            private int IndexAt(int y)
            {
                if (_titles == null || _titles.Length == 0) return -1;
                int i = (y - PadTop) / RowHeight;
                if (i < 0 || i >= _titles.Length) return -1;
                return i;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int i = IndexAt(e.Y);
                if (i != _hot) { _hot = i; KickGlow(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (_hot != -1) { _hot = -1; KickGlow(); }
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                int i = IndexAt(e.Y);
                if (i >= 0)
                {
                    _active = i;
                    ScrollTo(i);
                    KickGlow();
                }
                base.OnMouseDown(e);
            }

            /// <summary>把第 i 个分组滚到视口顶部（留一点点上边距，不要贴死）。</summary>
            private void ScrollTo(int i)
            {
                if (_host == null || _targets == null) return;
                if (i < 0 || i >= _targets.Length) return;
                var t = _targets[i];
                if (t == null) return;
                try
                {
                    int y = t.Top - Fields.Sc(6);
                    if (y < 0) y = 0;
                    _host.AutoScrollPosition = new Point(0, y);
                }
                catch (Exception ex) { Log.Error(ex); }
            }

            /// <summary>仅供自检：当前高亮项 / 条目数 / SyncActive 被调次数。</summary>
            internal int ActiveForTest { get { return _active; } }
            internal int CountForTest { get { return _titles == null ? 0 : _titles.Length; } }
            internal int SyncCountForTest;
            internal int LastTopForTest;
            internal int LastBestForTest = -1;

            /// <summary>按当前滚动位置重算高亮项。滚动 / 改变窗口大小时调用。</summary>
            public void SyncActive()
            {
                SyncCountForTest++;
                if (_host == null || _targets == null) return;
                int top;
                try { top = -_host.AutoScrollPosition.Y; }
                catch { return; }
                LastTopForTest = top;

                int best = 0;
                for (int i = 0; i < _targets.Length; i++)
                {
                    if (_targets[i] == null) continue;
                    if (_targets[i].Top <= top + Fields.Sc(40)) best = i;
                }

                // ★★★ 滚到底时必须高亮【最后一项】★★★
                //
                //  用户报障："我把页面滑到了那个部分，侧边的设置目录却没有跟随到那个部分。"
                //
                //  根因：判据是"哪个分组的 Top 已经越过视口顶部"。但内容总高是有限的，
                //  最后几个分组【永远到不了视口顶部】—— 滚动位置会被钳在
                //  "内容高 − 视口高"，于是无论怎么往下滚，高亮都停在上面某一项。
                //  实测：滚到底时实际偏移 3082（最大值），而最后一个分组的 Top 是 3338，
                //  判据永远不成立，高亮卡在第 6 项。
                //
                //  修法：已经滚到底（或差 2px 以内）就直接认定最后一项。
                //  这也是所有正常应用的导航行为：滚到底 = 高亮最后一段。
                try
                {
                    int maxScroll = _host.DisplayRectangle.Height - _host.ClientSize.Height;
                    if (maxScroll > 0 && top >= maxScroll - 2 && _targets.Length > 0)
                        best = _targets.Length - 1;
                }
                catch { }

                LastBestForTest = best;
                if (best != _active) { _active = best; KickGlow(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                HighQuality.Apply(g);

                using (var b = new SolidBrush(Design.SidebarSurface)) g.FillRectangle(b, ClientRectangle);

                // 右侧一条极淡的分隔线，把侧栏和内容区分开
                using (var p = new Pen(Design.Stroke))
                    g.DrawLine(p, Width - 1, 0, Width - 1, Height);

                if (_titles == null) return;

                int rowH = RowHeight;
                int padTop = PadTop;
                int padX = PadX;

                using (var f = new Font(Font.FontFamily, Fields.Sc(13), FontStyle.Regular, GraphicsUnit.Pixel))
                using (var iconFont = Icons.At(Fields.Sc(16)))
                {
                    for (int i = 0; i < _titles.Length; i++)
                    {
                        int y = padTop + i * rowH;
                        var row = new Rectangle(padX - Fields.Sc(6), y + Fields.Sc(3),
                                                Width - (padX - Fields.Sc(6)) - Fields.Sc(10),
                                                rowH - Fields.Sc(6));
                        bool act = i == _active;
                        bool hot = i == _hot;

                        // 高亮程度走补间：0 = 无，1 = 完全选中，0.55 = 悬停。
                        float glow = (_rowGlow != null && i < _rowGlow.Length) ? _rowGlow[i] : (act ? 1f : 0f);

                        if (glow > 0.01f)
                        {
                            // 底色：悬停是极淡的中性灰，选中是强调色的淡版 —— 两者按 glow 混出来，
                            // 于是"从悬停滑到选中"是连续渐变的，而不是啪地换一个色。
                            Color want = act ? Design.AccentSoft : Color.FromArgb(242, 243, 246);
                            Color from = Color.FromArgb(0, want);
                            Color blend = MixColor(from, want, Math.Min(1f, glow * 1.35f));
                            using (var b = new SolidBrush(blend))
                            using (var path = RoundedPath(row, Fields.Sc(8)))
                                g.FillPath(b, path);
                        }

                        // 选中项左侧一道强调竖条（也按 glow 淡入）
                        if (glow > 0.01f)
                        {
                            var bar = new Rectangle(row.Left + Fields.Sc(2), row.Top + Fields.Sc(7),
                                                    Fields.Sc(3), row.Height - Fields.Sc(14));
                            using (var b = new SolidBrush(Color.FromArgb(
                                       (int)Math.Round(255 * Math.Min(1f, glow)), Design.Accent)))
                            using (var path = RoundedPath(bar, Fields.Sc(2)))
                                g.FillPath(b, path);
                        }

                        var text = new Rectangle(row.Left + Fields.Sc(13), row.Top,
                                                 row.Width - Fields.Sc(18), row.Height);
                        // 文字颜色也随 glow 过渡：次要灰 → 主文字色 → 强调色
                        Color fg = act ? Design.Accent : (hot ? Design.TextPrimary : Design.TextSecondary);
                        fg = MixColor(Design.TextSecondary, fg, Math.Min(1f, glow));

                        // ★ 第四十二轮：导航项加图标 ★
                        //   Win11 的观感有一半来自图标体系；纯文字的导航看起来就是"一张表单"。
                        //   图标画在文字左边，文字整体右移；颜色与文字同步（同一套 glow 过渡），
                        //   于是"从悬停滑到选中"时图标和文字是一起变色的，不会脱节。
                        int textLeft = text.Left;
                        if (iconFont != null && _glyphs != null && i < _glyphs.Length)
                        {
                            int iconSize = Fields.Sc(16);
                            var iconRect = new Rectangle(row.Left + Fields.Sc(13),
                                row.Top + (row.Height - iconSize) / 2, iconSize, iconSize);
                            TextRenderer.DrawText(g, _glyphs[i], iconFont, iconRect, fg,
                                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                            textLeft = row.Left + Fields.Sc(13) + iconSize + Fields.Sc(8);
                        }

                        text = new Rectangle(textLeft, row.Top,
                                             row.Right - Fields.Sc(6) - textLeft, row.Height);
                        TextRenderer.DrawText(g, _titles[i], f, text, fg,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                            | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    }
                }
            }
        }

        public SettingsForm(ConfigStore store, Coordinator coord)
        {
            _store = store;
            _coord = coord;
            _edit = store.Current.Clone();

            // 拖动窗口边框时的布局节流（见 OnResize 的注释）。
            // 交互式改大小的节流定时器。
            //
            // ★ 第四十轮改语义：从"防抖"改成"固定节拍节流" ★
            //   见 OnResize 里的详细说明 —— 防抖会让整个拖动过程一次都不重排。
            _relayoutTimer.Interval = 32;
            _relayoutTimer.Tick += (s, e) =>
            {
                if (!_pendingRelayout) { _relayoutTimer.Stop(); return; }
                _pendingRelayout = false;
                RelayoutFlickerFree();
            };

            BuildUi();
            Populate();
        }

        // ── 界面搭建 ────────────────────────────────────────────

        private void BuildUi()
        {
            SuspendLayout();

            Text = "长按拖窗 · 设置";
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

            // ═══════════════════════════════════════════════════════════════
            //  ★★★ 关于缩放方式的选择：这里刻意【不用】 WinForms 自动缩放 ★★★
            // ═══════════════════════════════════════════════════════════════
            //
            // 早先写的是：
            //     AutoScaleMode = AutoScaleMode.Dpi;
            //     AutoScaleDimensions = new SizeF(96f, 96f);
            // 也就是"让 WinForms 按系统 DPI 自动把界面放大"。看起来很省事，
            // 但它和本窗口的布局机制是根本冲突的，是"设置里选项全部重叠"的根因：
            //
            //   · 本窗口所有控件的位置/尺寸都是【代码里写死的设计稿值】
            //     （Label("吸附距离", 18, 84, 66)、Group(..., 230) 这种），
            //     并且在 BuildUi() 里就把它们记成了布局基准（MarkBase）。
            //   · 而 AutoScaleMode.Dpi 会在窗体第一次显示时，把子控件的
            //     Left/Top/Width/Height 统统乘以 DPI 系数再 SetBounds 一遍。
            //   · 于是"屏幕上控件的真实位置"和"我记的基准"分属两套坐标系：
            //     基准是 110，屏幕上站在 138。之后 LayoutInner() 一执行
            //         c.Top = BaseTop + dy;
            //     就把控件从 138 拽回 110 附近 —— 每一行都被"缩"回去一截，
            //     26px 的行距变成 20.8px，而字体是实打实放大到 125% 的，
            //     行与行随即咬在一起。行数越多叠得越厉害，整屏糊成一团。
            //
            //   · 更麻烦的是"自动缩放到底缩了哪些属性"本身不确定：
            //     实测本机上 Font 被放大了，但控件 bounds 并没有等比放大
            //     （因为位置是代码显式赋的），于是出现了
            //     "字变大、行距没变"这种最难排查的半吊子状态。
            //
            // 所以这里改成 AutoScaleMode.None —— 关掉自动缩放，
            // 由本窗口【自己】把设计稿值按 DPI 缩放一次（见 ScaleDesign()）。
            // 好处：
            //   · 基准和实际位置永远是同一个坐标系，不可能再错位；
            //   · 缩放系数是我自己算的，想缩什么、缩多少都明确可控；
            //   · 布局代码里所有算术都建立在"同一套数"上，幂等性自然成立。
            // ═══════════════════════════════════════════════════════════════
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96f, 96f);
            BackColor = Design.Canvas;
            ForeColor = Design.TextPrimary;

            // ★ 用户可以自由拉伸这个窗口了（用户需求）。
            //   原先 FixedSingle + MaximizeBox=false，窗口尺寸被钉死，小屏 / 放大字体下很难受。
            //   但"能拉"还不够 —— 早先只加 Anchor，拉宽之后分组只是变宽、
            //   内容仍挤在左边，右边一大片空白，用户直接说"拉宽毫无意义"。
            //   现在是真正的响应式：宽度够了分组自动从 1 列变 2 列（见 ResponsiveGroupPanel），
            //   一屏能装下的信息量翻倍；宽度不够就退回 1 列。
            //   纵向内容仍可能超出，交给 _scrollHost 滚动，底部操作条固定不动。
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            // 注意：AutoScroll 挂在 _scrollHost 上，不挂在窗体上。
            // 挂窗体上会让底部按钮跟着内容一起滚走。
            AutoScroll = false;

            // 一边滚一边画、加上 OwnerDraw 下拉框，不做双缓冲会闪。
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;

            try
            {
                var ico = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
                if (ico != null) Icon = ico;
            }
            catch (Exception ex) { Log.Error(ex); }

            // ══ 组 1：触发方式 ══════════════════════════════════
            var g1 = Group("触发方式", 0, 0, GroupWidth, 156);

            g1.Controls.Add(Label("触发键", 16, 31, 60));
            _txtKey.Location = new Point(84, 27);
            _txtKey.Size = new Size(190, 26);
            _txtKey.ReadOnly = true;
            _txtKey.TabStop = false;
            // 外观（去边框 / 圆角 / 只读底色）统一在 ScaleDesign 之后由 StyleText 处理
            g1.Controls.Add(_txtKey);

            _btnRecord.Text = "重新录制";
            _btnRecord.Location = new Point(284, 26);
            _btnRecord.Size = new Size(92, 28);
            _btnRecord.Click += (s, e) => StartRecording();
            g1.Controls.Add(_btnRecord);

            // ★ 告诉布局："触发键输入框变宽时，右边界停在'重新录制'左边 10px 处"。
            //   否则它会一直吃到分组最右边，把按钮挤出可视区（截图里就消失了）。
            SetGrowRightNeighbor(_txtKey, _btnRecord, 10);
            // "重新录制"按钮跟着分组右边跑
            SetShiftFromRight(_btnRecord, 24);

            _lblKeyHint.Location = new Point(16, 60);
            _lblKeyHint.Size = new Size(504, 20);
            _lblKeyHint.ForeColor = Design.Hint;
            g1.Controls.Add(_lblKeyHint);

            _chkHold.Text = "需要长按触发（关闭后只要按住就立即生效）";
            _chkHold.Location = new Point(18, 88);
            _chkHold.Size = new Size(400, 24);
            _chkHold.CheckedChanged += (s, e) => _numHold.Enabled = _chkHold.Checked;
            g1.Controls.Add(_chkHold);

            g1.Controls.Add(Label("长按阈值", 16, 121, 70));
            StyleNum(_numHold, 92, 118, 84, 100, 3000, 50);
            g1.Controls.Add(_numHold);
            var lblHoldUnit = Label("毫秒（默认 600，短按仍然是原本的按键）", 184, 121, 340);
            g1.Controls.Add(lblHoldUnit);

            // ══ 组 2：鼠标键动作 ════════════════════════════════
            var g2 = Group("鼠标键动作（长按触发键之后）", 0, 0, GroupWidth, 126);

            g2.Controls.Add(Label("左键", 16, 32, 60));
            StyleCombo(_cmbLeft, 84, 28, 260);
            _cmbLeft.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g2.Controls.Add(_cmbLeft);

            g2.Controls.Add(Label("右键", 16, 64, 60));
            StyleCombo(_cmbRight, 84, 60, 260);
            _cmbRight.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g2.Controls.Add(_cmbRight);

            g2.Controls.Add(Label("中键", 16, 96, 60));
            StyleCombo(_cmbMiddle, 84, 92, 260);
            _cmbMiddle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g2.Controls.Add(_cmbMiddle);

            // ══ 组 3：视觉反馈 ══════════════════════════════════
            //
            // ★ 第三十九轮重做（用户要求）★
            //   ① "修复在全屏状态下样式框显示为空的问题，应在进入设置时即展示所有可选项。"
            //   ② "修复选择样式后未在设置界面中直接展示当前选中项的问题，
            //       需让已选样式实时可见、状态清晰。"
            //   做法：把"外框颜色""样式"两个下拉框换成【色板 + 样式画廊】——
            //   所有可选项一进设置页就全部平铺可见，选中的那格带强调色描边和勾选角标。
            //   下拉框本身保留（不挂到分组上），继续当选择状态的单一数据源。
            //
            //   布局（设计稿像素，分组内宽约 528）：
            //     y30   复选框（整行）
            //     y58   外框颜色 标签 + 同行右侧"当前：xxx"
            //     y80   色板（9×2 = 18 格装 17 色）
            //     y164  样式 标签
            //     y186  样式画廊（5×2 = 10 格装 9 种）
            //     y296  粗细 / 圆角
            //     y326  提示文字（两行）
            //   右列 y60 起放实时预览框（96×120），与左列不重叠。
            var g3 = Group("视觉反馈", 0, 0, GroupWidth, 420);

            _chkFrame.Text = "长按触发键到达阈值后，光标所在的窗口立刻亮起外框";
            _chkFrame.Location = new Point(18, 30);
            _chkFrame.Size = new Size(500, 22);
            _chkFrame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _chkFrame.CheckedChanged += (s, e) => SyncFrameEnabled();
            g3.Controls.Add(_chkFrame);

            // ── 外框颜色：色板 ──
            //
            // 两个下拉框仍然是"选择状态的单一数据源"（Populate / SelectId / 保存 / 探针都用它），
            // 只是不再挂到界面上。事件必须在这里重新接上 —— 否则 Populate 里
            // SelectId() 设好初值之后，色板/画廊和预览都不会被刷新。
            _cmbFrameColor.SelectedIndexChanged += (s, e) => SyncPreview();
            _cmbFrameStyle.SelectedIndexChanged += (s, e) => SyncPreview();

            g3.Controls.Add(Label("外框颜色", 18, 58, 62));

            // 9 列 × 2 行 = 18 格，装下 17 个颜色（最后一格留空）。
            // 尺寸 246×50 是按"9×(22+6)−6"算出来的，改格子大小要一起改。
            _gridColor.Location = new Point(18, 80);
            _gridColor.Size = new Size(246, 50);
            _gridColor.Columns = 9;
            _gridColor.SetItems(BuildColorEntries());
            _gridColor.Picked += (s, e) => SelectId(_cmbFrameColor, _gridColor.SelectedId);
            g3.Controls.Add(_gridColor);

            // 当前颜色的中文名 —— "选中的到底叫什么"必须写出来，
            // 光靠一圈描边在 17 个色块里还是不够明确。
            // 和"外框颜色"标签同行（y58），色板在下面一行，互不挤。
            _lblColorName.Location = new Point(84, 58);
            _lblColorName.Size = new Size(300, 20);
            _lblColorName.ForeColor = Design.TextPrimary;
            g3.Controls.Add(_lblColorName);

            // ── 样式：画廊（全部平铺，进入即可见） ──
            g3.Controls.Add(Label("样式", 18, 164, 40));

            // 5 列 × 2 行 = 10 格，装下 9 种样式；374×98 = 5×(70+6)−6 × 2×(46+6)−6。
            _galleryStyle.Location = new Point(18, 186);
            _galleryStyle.Size = new Size(374, 150);
            _galleryStyle.Columns = 5;
            _galleryStyle.Picked += (s, e) => SelectId(_cmbFrameStyle, _galleryStyle.SelectedId);
            g3.Controls.Add(_galleryStyle);

            // 第四十五轮：这一行从「粗细 / 圆角」扩成「粗细 / 圆角 / 不透明度」，
            //   三组等距排开（每组 = 标签 + 数字框 + 单位），整行仍在分组宽度内。
            g3.Controls.Add(Label("粗细", 18, 348, 56));
            StyleNum(_numFrameThick, 76, 344, 62, 1, 8, 1);
            _numFrameThick.ValueChanged += (s, e) => SyncPreview();
            g3.Controls.Add(_numFrameThick);
            g3.Controls.Add(Label("px", 142, 348, 24));

            g3.Controls.Add(Label("圆角", 174, 348, 56));
            StyleNum(_numFrameRadius, 232, 344, 62, 0, 28, 1);
            _numFrameRadius.ValueChanged += (s, e) => SyncPreview();
            g3.Controls.Add(_numFrameRadius);
            g3.Controls.Add(Label("px", 298, 348, 24));

            g3.Controls.Add(Label("不透明度", 330, 348, 72));
            StyleNum(_numFrameOpacity, 406, 344, 62, 20, 100, 5);
            _numFrameOpacity.ValueChanged += (s, e) => SyncPreview();
            g3.Controls.Add(_numFrameOpacity);
            g3.Controls.Add(Label("%", 472, 348, 24));

            _lblFrameTip.Location = new Point(18, 378);
            _lblFrameTip.Size = new Size(496, 34);
            _lblFrameTip.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblFrameTip.ForeColor = Design.Hint;
            _lblFrameTip.Text = "上面所有颜色和样式都是一进这个页面就能看到的，点一下就换；"
                              + "右上角是实时预览，和真正拖窗口时用的是同一段绘制代码。";
            g3.Controls.Add(_lblFrameTip);

            // 预览框钉在右上角：窗口拉宽，它跟着往右移，永远贴着右边不出界。
            _pnlPreview.Location = new Point(424, 60);
            _pnlPreview.Size = new Size(96, 120);
            SetShiftFromRight(_pnlPreview, 16);
            g3.Controls.Add(_pnlPreview);

            // ══ 组 4：拖动行为 ══════════════════════════════════
            // 高度 366：缩放角区一行 + 双击一行 + 还原/提最前/全屏/退出全屏/晃窗
            //            各占一整行 + 黑名单一行 + 挑选按钮一排 + 说明文字两行。
            //   ★ 长文本的复选框一律独占整行：挤成两列时文字会折行、高度涨一截，
            //     而布局按设计稿 Top 摆，涨出来的高度会压住下一行（本轮修的就是这个）。
            var g4 = Group("拖动行为", 0, 0, GroupWidth, 366);

            // 右列（缩放角区）跟着右边缘走：窗口拉宽时它留在右边，不与左列挤在一起。
            var lblCorner = Label("缩放角区", 230, 30, 66);
            SetShiftFromRight(lblCorner, -1);
            g4.Controls.Add(lblCorner);
            StyleNum(_numCorner, 300, 27, 70, 5, 45, 1);
            SetShiftFromRight(_numCorner, -1);
            g4.Controls.Add(_numCorner);
            var lblCornerUnit = Label("%", 374, 30, 30);
            SetShiftFromRight(lblCornerUnit, -1);
            g4.Controls.Add(lblCornerUnit);

            _chkDblLeft.Text = "双击左键 = 最大化 / 还原";
            _chkDblLeft.Location = new Point(18, 56);
            _chkDblLeft.Size = new Size(240, 22);
            g4.Controls.Add(_chkDblLeft);

            _chkDblRight.Text = "双击右键 = 九宫格分区";
            _chkDblRight.Location = new Point(272, 56);
            _chkDblRight.Size = new Size(240, 22);
            SetShiftFromRight(_chkDblRight, 16);
            g4.Controls.Add(_chkDblRight);

            // ★★★ 这一行必须【独占整行】★★★
            //
            //  用户报障："允许拖动全屏 / 无边框窗口 被 允许拖动已最大化的窗口 遮住了一半。"
            //  实测几何：允许拖动已最大化(36,228 472x70)、允许拖动全屏(36,280 774x44)
            //           → 228+70=298 > 280，被压住 18px。
            //  根因：这一条原来只有 240 设计px 宽，而文字有 19 个汉字 ——
            //       在高 DPI / 窄窗下必然折成两行，控件高度从 44 涨到 70，
            //       而布局是按设计稿 Top 摆的，涨出来的高度就压住了下一行。
            //  修法：给它整行宽度（500 设计px + 左右锚定），文字一行放得下，
            //       高度恒定，从根本上不产生"折行涨高"这件事。
            _chkRestoreMax.Text = "允许拖动已最大化的窗口（先还原再拖）";
            _chkRestoreMax.Location = new Point(18, 82);
            _chkRestoreMax.Size = new Size(500, 22);
            _chkRestoreMax.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g4.Controls.Add(_chkRestoreMax);

            // 原来和上一条挤在同一行；上一条改成整行之后，这一条顺延到下一行。
            _chkFront.Text = "开始拖动时自动把窗口提到最前";
            _chkFront.Location = new Point(18, 108);
            _chkFront.Size = new Size(500, 22);
            _chkFront.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g4.Controls.Add(_chkFront);

            _chkFullscreen.Text = "允许拖动全屏 / 无边框窗口（游戏、全屏播放器，可能有风险）";
            _chkFullscreen.Location = new Point(18, 134);
            _chkFullscreen.Size = new Size(500, 22);
            _chkFullscreen.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _chkFullscreen.ForeColor = Design.WarnText;
            g4.Controls.Add(_chkFullscreen);

            // ★ 第四十一轮新增（用户明确要求）★
            //   "如果在全屏的情况下来触发按键，不管是拖动窗口还是调整窗口大小，
            //    都会把全屏窗口变成非全屏窗口。"
            //   默认开启。它是上面那个"允许全屏窗口"能真正好用的前提：
            //   直接把全屏窗口拖着走会拖出一片黑，先降级成普通窗口就没这问题。
            _chkExitFullscreen.Text = "触发时先把全屏 / 最大化窗口变回普通窗口，再执行拖动或缩放";
            _chkExitFullscreen.Location = new Point(18, 160);
            _chkExitFullscreen.Size = new Size(500, 22);
            _chkExitFullscreen.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g4.Controls.Add(_chkExitFullscreen);

            // #39 晃窗最小化：触发后选中窗口，左右晃一晃，其它窗口全部最小化。
            _chkShake.Text = "选中窗口后晃一晃，其它窗口全部最小化（再晃一次还原）";
            _chkShake.Location = new Point(18, 186);
            _chkShake.Size = new Size(500, 22);
            _chkShake.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g4.Controls.Add(_chkShake);

            g4.Controls.Add(Label("不生效的窗口", 18, 240, 90));
            _txtBlacklist.Location = new Point(112, 237);
            _txtBlacklist.Size = new Size(406, 26);
            _txtBlacklist.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g4.Controls.Add(_txtBlacklist);

            // ★ 两个"帮我把关键字填进去"的按钮 ★
            //
            // 为什么需要：黑名单原来只能手打关键字，而用户根本不知道该打什么 ——
            //   窗口类名是 "Chrome_WidgetWin_1" 这种内部名，
            //   中文系统下标题里也不会出现程序的英文名。
            //   于是"想排除某个程序"这件事对普通用户基本不可用。
            // 这两个按钮分别对应两种直觉：
            //   · 知道程序在哪 → 直接从文件对话框里挑 exe；
            //   · 看到窗口在屏幕上 → 直接用十字准星点它。
            _btnPickExe = new FlatButton();
            _btnPickExe.Text = "选择程序…";
            _btnPickExe.Location = new Point(112, 272);
            _btnPickExe.Size = new Size(92, 26);
            _btnPickExe.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _btnPickExe.Click += (s, e) => PickExeFile();
            g4.Controls.Add(_btnPickExe);

            _btnPickWindow = new FlatButton();
            _btnPickWindow.Text = "点选窗口";
            _btnPickWindow.Location = new Point(212, 272);
            _btnPickWindow.Size = new Size(92, 26);
            _btnPickWindow.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _btnPickWindow.Click += (s, e) => PickWindowByClick();
            g4.Controls.Add(_btnPickWindow);

            var tip4 = Label("填窗口标题 / 程序名 / 窗口类名的关键字，逗号分隔；命中就完全不接管。"
                + "也可以用上面两个按钮直接挑。", 18, 306, 500);
            tip4.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tip4.ForeColor = Design.Hint;
            g4.Controls.Add(tip4);

            // ══ 组 5：流畅度 ════════════════════════════════════
            // #36：刷新率可选"跟随屏幕"（按实测刷新率节流）或"固定值"（0 = 不限制）。
            //   窗口本身仍是实时移动、异步投递、绝不丢最新位置；这里只决定
            //   工作线程"最多多快写一帧"，与显示器同步可免掉无谓的重绘。
            var g5 = Group("流畅度", 0, 0, GroupWidth, 148);

            g5.Controls.Add(Label("刷新率", 16, 26, 70));
            StyleCombo(_cmbRefreshMode, 90, 22, 220);
            _cmbRefreshMode.SelectedIndexChanged += (s, e) => SyncRefreshEnabled();
            g5.Controls.Add(_cmbRefreshMode);

            g5.Controls.Add(Label("固定值", 16, 58, 70));
            StyleNum(_numRefreshHz, 90, 54, 90, 0, 360, 1);
            g5.Controls.Add(_numRefreshHz);
            g5.Controls.Add(Label("Hz", 186, 58, 30));

            _lblRefreshHint.Location = new Point(16, 90);
            _lblRefreshHint.Size = new Size(504, 44);
            _lblRefreshHint.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblRefreshHint.ForeColor = Design.Hint;
            _lblRefreshHint.Text = "跟随屏幕：按实测刷新率节流，与显示器同步。\r\n"
                                 + "固定值：自定义节流频率，0 = 不限制。改动在下次拖动时生效。";
            g5.Controls.Add(_lblRefreshHint);

            // ══ 组 6：启动与权限 ════════════════════════════════
            var g6 = Group("启动与权限", 0, 0, GroupWidth, 150);

            g6.Controls.Add(Label("开机自启", 16, 32, 70));
            StyleCombo(_cmbAutoStart, 90, 28, 430);
            _cmbAutoStart.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g6.Controls.Add(_cmbAutoStart);

            _chkRunAsAdmin.Text = "启动时自动申请管理员权限（默认开启）";
            _chkRunAsAdmin.Location = new Point(18, 62);
            _chkRunAsAdmin.Size = new Size(500, 22);
            _chkRunAsAdmin.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g6.Controls.Add(_chkRunAsAdmin);

            _chkWarnNonAdmin.Text = "没有管理员权限时提示我（可关闭，不再打扰）";
            _chkWarnNonAdmin.Location = new Point(18, 86);
            _chkWarnNonAdmin.Size = new Size(500, 22);
            _chkWarnNonAdmin.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g6.Controls.Add(_chkWarnNonAdmin);

            _lblAdmin.Location = new Point(16, 112);
            _lblAdmin.Size = new Size(340, 20);
            _lblAdmin.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblAdmin.ForeColor = Design.TextSecondary;
            // ★★ 必须声明"右边到哪儿为止" ——
            //   否则 Label 被 ClassifyStretch 判成 Grow，
            //   ApplyOne 里按 baseRightGap = BaseInner - (BaseLeft + BaseWidth) 反推右边界，
            //   而 ReserveLabelWidth 又会把它按文字长度撑宽
            //   （"当前权限：普通用户（日常窗口没问题）提权窗口拖不动）"
            //     在 200% DPI 下量出 1408px，是分组本身的两倍多）。
            //   两者叠加 → baseRightGap 变成负数 → 控件反向溢出分组 364px，
            //   文字被切掉一整段 —— 实测就是这里。
            //
            //   声明了右邻约束后，右边界固定停在"以管理员重启"按钮左边，
            //   宽度再大也不会越界。文字不够就照常截断（本来也只能显示一行）。
            SetGrowRightNeighbor(_lblAdmin, _btnRestartAdmin, 12);
            g6.Controls.Add(_lblAdmin);

            _btnRestartAdmin.Text = "以管理员重启";
            _btnRestartAdmin.Location = new Point(376, 110);
            _btnRestartAdmin.Size = new Size(144, 24);
            SetShiftFromRight(_btnRestartAdmin, 16);
            _btnRestartAdmin.Click += (s, e) => RestartAsAdmin();
            g6.Controls.Add(_btnRestartAdmin);

            // ══ 底部操作条 ══════════════════════════════════════
            // 固定在窗口最下方（不随内容滚动），按钮靠右排开、状态文字占左侧。
            // 位置在 LayoutResponsive() 里按窗口实际宽度算，拉宽拉窄都贴着右边。
            _lblStatus.Location = new Point(16, 12);
            _lblStatus.Size = new Size(320, 22);
            _lblStatus.ForeColor = Design.Success;
            _pager.Controls.Add(_lblStatus);

            // ★ 三个按钮都换成 FlatButton（自绘扁平按钮，见其类注释）。
            //   主次分明：只有"保存并生效"是主按钮（实心强调色），
            //   "恢复默认值"和"取消"是次按钮（描边）——
            //   原先三个按钮长得一模一样，用户得自己猜哪个是主动作。
            //
            // ★ 高度必须是 26（与输入框/下拉框的设计高一致），不能是 20 ★
            //
            //   踩过的坑：之前设成 20，在 200% 缩放（本机环境）下是 40px，
            //   装得下 24px 的字，看着没问题；但一旦缩放系数是 1（100% 显示器，
            //   或者进程被判定为 DPI 不感知），按钮就只有 20px 高，
            //   而 9pt 的字实际要 17px，加上下留白根本装不下 ——
            //   表现就是用户报的"按钮字跟按钮没有很好的适配"（字被切掉）。
            //
            // ★ 结论：高度不能写死，必须按字体自适应（见 ScaleFlatButton）★
            //
            //   实测过两个固定值，两边都堵不住：
            //     · 20 → 100% 下只有 20px，装不下 17px 的字（差 8px 留白要求）
            //     · 26 → 200% 下变成 52px，而输入框是随字体自适应的 39px，
            //            两者差 13px，视觉上"一排大按钮杵在输入框旁边"
            //
            //   根因：TextBox/ComboBox 的高度是【按字体自动算】的（设 Size 无效），
            //         而 FlatButton 是自绘控件、高度完全由我们说了算 ——
            //         一个跟随字体、一个写死，任何固定值都只能对上一个缩放档。
            //
            //   所以这里保留设计高 20（作为 200% 档的基准），
            //   再由 ScaleFlatButton 在缩放之后兜一个"字体高度 + 留白"的下限。
            const int barBtnW = 96;
            const int barBtnH = 20;
            _btnDefaultsRef = new FlatButton();
            _btnDefaultsRef.Text = "恢复默认值";
            _btnDefaultsRef.Size = new Size(barBtnW, barBtnH);
            _btnDefaultsRef.Click += (s, e) => ResetDefaults();
            _pager.Controls.Add(_btnDefaultsRef);

            _btnSaveRef = new FlatButton();
            ((FlatButton)_btnSaveRef).Primary = true;   // ★ 主按钮（实心强调色）
            _btnSaveRef.Text = "保存并生效";
            _btnSaveRef.Size = new Size(barBtnW, barBtnH);
            _btnSaveRef.Click += (s, e) => Save();
            _pager.Controls.Add(_btnSaveRef);

            _btnCancelRef = new FlatButton();
            _btnCancelRef.Text = "取消";
            _btnCancelRef.Size = new Size(barBtnW, barBtnH);
            _btnCancelRef.Click += (s, e) => Close();
            _pager.Controls.Add(_btnCancelRef);

            // ★★ 基准登记必须放在这里、放在装配之前。★★
            //    此刻所有分组的 Location/Size 都刚设完，而 _groupHost 还没挂到
            //    _scrollHost 上、没经历过任何排版 —— 读到的 Left/Width 就是我们
            //    在代码里写的原始值（如 _cmbLeft: Left=84 Width=260），绝对准确。
            //
            //    早先是等全部排完版再统一抓一遍，那时 GroupBox 可能还不是标准宽度，
            //    控件已被压扁（实测抓到 BaseWidth=0），于是后面所有计算全错，
            //    表现为"下拉框被挤成一个勾、输入框压到按钮上"。
            // ★★ 先把"设计稿坐标"按当前 DPI 缩放一遍，再记基准。★★
            //
            //   因为关掉了 WinForms 自动缩放（AutoScaleMode.None，理由见 BuildUi 顶部），
            //   缩放必须由我们自己做，而且必须在 MarkBase() 【之前】做 ——
            //   这样记下来的基准就是"屏幕上真实生效的坐标"，
            //   后面 LayoutInner() 里的 c.Top = BaseTop + dy 才不会把控件拽回设计稿位置。
            //   顺序反了就会出现"字是大的、行距是小的"→ 行行重叠（用户报的那个 bug）。
            // ══ 组 7：帮助 ══════════════════════════════════════
            // 用户要求：帮助排在关于【上面】—— 找帮助的人比看版本的人多得多。
            var gHelp = Group("帮助", 0, 0, GroupWidth, 396);

            var lblHelpTip = Label("点一下标题，就能展开对应功能的说明。下面每一项都是当前版本的实际行为。",
                18, 28, 500);
            lblHelpTip.ForeColor = Design.Hint;
            gHelp.Controls.Add(lblHelpTip);

            _helpPanel.Location = new Point(18, 56);
            _helpPanel.Size = new Size(500, 320);
            _helpPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _helpPanel.SetEntries(BuildHelpEntries());
            gHelp.Controls.Add(_helpPanel);

            // ══ 组 8：关于 ══════════════════════════════════════
            // 开发者 / 当前版本 / 更新时间 / 开发工具 —— 用户要求补上。
            // 版本号取自程序集（AppInfo.Version），不写常量：写常量就会和
            // csproj 里的 <Version> 形成两份真相，改一处忘一处。
            var gAbout = Group("关于", 0, 0, GroupWidth, 140);

            var lblAppTitle = Label(AppInfo.Name + "  " + AppInfo.EnglishName
                + "    版本 " + AppInfo.Version, 18, 26, 460);
            lblAppTitle.ForeColor = Design.TextPrimary;
            gAbout.Controls.Add(lblAppTitle);

            var lblAppTag = Label(AppInfo.Tagline, 18, 52, 460);
            lblAppTag.ForeColor = Design.Hint;
            gAbout.Controls.Add(lblAppTag);

            gAbout.Controls.Add(Label("开发者", 18, 82, 82));
            gAbout.Controls.Add(Label(AppInfo.Developer, 104, 82, 160));
            gAbout.Controls.Add(Label("开发工具", 280, 82, 82));
            gAbout.Controls.Add(Label(AppInfo.Tools, 366, 82, 160));

            gAbout.Controls.Add(Label("当前版本", 18, 108, 82));
            gAbout.Controls.Add(Label("v" + AppInfo.Version, 104, 108, 160));
            gAbout.Controls.Add(Label("更新时间", 280, 108, 82));
            gAbout.Controls.Add(Label(AppInfo.ReleaseDate, 366, 108, 160));

            _groups = new GroupBox[] { g1, g2, g3, g4, g5, g6, gHelp, gAbout };
            // ★ 在缩放【之前】留一份设计稿几何快照 ★
            //   第四十轮新增：窗口被拖到另一块 DPI 不同的显示器时（WM_DPICHANGED），
            //   要能按新系数把整个界面重算一遍 —— 而 ScaleDesign 是"就地乘系数"的，
            //   重复执行会越乘越大，必须先能还原到这份快照。
            CaptureDesignSnapshot();
            ScaleDesign(_groups);

            // ★ 必须在 ScaleDesign 之后 ★
            //   ScaleDesign 会按 DPI 重设每个控件的 Size；先做的话，
            //   刚锁好的输入框高度会被再乘一次缩放系数（38 → 76）。
            //   传窗体字体而不是控件自己的 Font：此刻分组还没挂到窗体上，
            //   控件继承到的还是默认字体，量出来的高度会差一截。
            StyleText(_txtKey, Font, true);
            StyleText(_txtBlacklist, Font, false);

            _innerLayouts.Clear();
            _innerIndex.Clear();
            for (int i = 0; i < _groups.Length; i++)
            {
                var gb = _groups[i];
                for (int j = 0; j < gb.Controls.Count; j++)
                {
                    MarkBase(gb.Controls[j]);
                }
            }
            _innerCaptured = true;

            // 左侧字段名标签按实际文字长度留够宽度。
            // 不做这一步的话，两列模式下会出现"触""长按""吸附"这种被切掉一半的标签。
            ReserveLabelWidth();

            // ══ 装配 ════════════════════════════════════════════
            // 结构：窗体
            //        ├─ _pager（Dock=Bottom，固定操作条）
            //        └─ _scrollHost（Dock=Fill，滚动区）
            //             └─ _groupHost（TableLayoutPanel，1 列 / 2 列自适应）
            //
            // ★★ 早先忘了 Controls.Add(g1..g6)，整个设置窗口是空的。★★
            //    现在分组由 _groupHost 负责挂载（构造函数里就已 Add），
            //    只要 _groupHost 被加到 _scrollHost 上就一定会显示。
            // ★ 必须直接用 _groups，不能再写死 g1..g6 ★
            //   写死的那一版，本轮新增的「关于 / 帮助」两组【从未被加进界面】——
            //   对象存在、自检（查 _groups 数组）也"通过"，但用户根本看不到，
            //   于是报"没有找到帮助按钮"。分组集合只有一个真相：_groups。
            _groupHost = new ResponsiveGroupPanel(_groups);
            _groupHost.Location = new Point(ScrollEdgeMargin, ScrollEdgeMargin);
            _scrollHost.Controls.Add(_groupHost);

            _scrollHost.Dock = DockStyle.Fill;
            _scrollHost.AutoScroll = true;
            _scrollHost.BackColor = Design.Canvas;
            Controls.Add(_scrollHost);

            // ★ 左侧导航栏 ★
            //   六个分组在单列模式下要滚动才能看全，用户得一路翻到底找某项设置。
            //   加一条常驻侧栏，点标题直接滚过去，一眼能看到"这页一共分几块"。
            //
            //   停靠顺序（WinForms 按【反向】z-order 处理 Dock）：
            //     集合顺序 scrollHost → pager → sidebar，反向即 sidebar → pager → scrollHost，
            //   于是 sidebar 先吃掉整条左边（含底栏高度），pager 再吃掉内容区底部，
            //   scrollHost 最后填满剩下的 —— 正是想要的样子。
            _sidebar = new NavSidebar();
            _sidebar.Dock = DockStyle.Left;
            _sidebar.Width = S(SidebarDesignWidth);
            Controls.Add(_sidebar);

            // ══════ 滚动时同步侧栏高亮项 ══════
            //
            // ★★★ 光挂 Scroll 是不够的 —— 滚轮根本不会触发它 ★★★
            //
            //  用户报障："我滚到了启动与权限，左边菜单里选的还是触发方式。"
            //
            //  实测（T75 打点）：连续发 40 批滚轮消息、页面确实滚到了 3198，
            //  而 SyncActive 只被调用【2 次】—— 那两次还是初始 Resize 触发的。
            //  也就是说：拖滚动条会走 WM_VSCROLL → Scroll 事件，
            //  但【滚轮滚动不经过 Scroll 事件】，于是高亮永远停在第一项。
            //
            //  修法：Scroll（拖滚动条）与 MouseWheel（滚轮）两个都挂。
            //  ★ MouseWheel 必须 BeginInvoke 推迟一帧 ★
            //    事件回调时默认处理函数【还没把滚动应用下去】，
            //    此刻读 AutoScrollPosition 拿到的还是旧值（实测 top=0）。
            //    投递到消息队列末尾再算，拿到的才是滚动后的位置。
            _scrollHost.Scroll += (s, e) => { if (_sidebar != null) _sidebar.SyncActive(); };
            _scrollHost.Resize += (s, e) => { if (_sidebar != null) _sidebar.SyncActive(); };
            // 拖滚动条 / 键盘翻页走的是 WM_VSCROLL → Scroll 事件（上面已挂）；
            // 滚轮不走 Scroll，所以单独挂 MouseWheel。
            _scrollHost.MouseWheel += (s, e) => SyncSidebarLater();

            // 把【全部分组】绑到侧栏上：标题用短的（分组标题带括号说明，侧栏放不下）。
            // ★ 标题数组与目标数组都从 _groups 派生 ★
            //   之前是写死的 6 个字符串 + 6 个控件 —— 新增分组时侧栏不会自动跟上，
            //   用户就"找不到"新分组。现在加分组只需要改 _groups 一处。
            var sidebarTitles = new string[_groups.Length];
            for (int i = 0; i < _groups.Length; i++)
                sidebarTitles[i] = ShortTitleOf(_groups[i].Text);
            _sidebar.Bind(_scrollHost, sidebarTitles, _groups);

            _pager.Dock = DockStyle.Bottom;
            // ★ 46 = 按钮高 26 + 上下各 10 的留白。
            //   早先写58（配合 32 高的按钮），按钮改小之后底栏会显得空荡荡，
            //   而且视觉重心偏上。
            _pager.Height = S(46);
            _pager.BackColor = Design.Surface;     // 白色，与内容区一体的"工具条"
            _pager.Paint += (s, e) =>
            {
                var g = e.Graphics;
                HighQuality.Apply(g);

                // ★ 底栏做成 Win11 那种"浮在上面的命令条" ★
                //   只画一条分隔线的话，底栏和滚动内容仍然是同一块白，
                //   看不出"这块是固定的、上面那块会滚"。这里补两件事：
                //     ① 顶边一条清晰的分隔线；
                //     ② 分隔线下方两道极淡的渐隐阴影 —— 这是"命令条浮在内容之上"
                //        的全部视觉依据（真实的投影被内容盖住了，只留下贴边这一线）。
                using (var p = new Pen(Design.Stroke))
                    g.DrawLine(p, 0, 0, _pager.Width, 0);

                for (int i = 1; i <= 2; i++)
                {
                    int alpha = i == 1 ? 16 : 7;
                    using (var p = new Pen(Color.FromArgb(alpha, Design.Shadow)))
                        g.DrawLine(p, 0, i, _pager.Width, i);
                }
            };
            Controls.Add(_pager);

            // 默认尺寸：单列。
            //
            // ★ 为什么又改回单列 ★
            //   上一版为了"消掉系统滚动条"把默认宽度设成两列宽（S(1320)）。
            //   但用户的屏幕是 3840x2160 @200%（逻辑 1920），1320 逻辑宽的窗口
            //   占了屏幕 69% —— 用户反馈"两列占的地方太大了"。
            //   而且两列把六个分组摊开，视线要左右来回扫，反而不如单列一条线读下来顺。
            //   现在改成默认单列窄窗，需要两列时用户自己把窗口拉宽即可
            //   （ResponsiveGroupPanel 仍会在够宽时自动切两列）。
            //
            //   滚动条重新出现是单列的必然代价，不再强求消除。
            //
            // ★ 默认宽度必须【算上左侧导航栏】★
            //   侧栏是 Dock=Left，先吃掉 176 设计像素，剩下的才是内容区。
            //   只给 SingleColumnClientWidth(560) 的话，内容区只剩 384 设计像素，
            //   连一个分组（536）都放不下 —— 分组会被压缩、右边缘控件被切掉。
            int defaultClientW = SingleColumnClientWidth + S(SidebarDesignWidth);
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                // 极端情况：都放不下（超小屏 / 超大字体），那就用屏幕宽。
                if (defaultClientW > wa.Width - S(80)) defaultClientW = Math.Max(S(420), wa.Width - S(80));
            }
            catch { defaultClientW = SingleColumnClientWidth + S(SidebarDesignWidth); }

            // 默认高度：用户反馈"设置页默认非常非常的大"。
            //   旧值是 S(760)——在 1920x1080 的逻辑桌面上占掉 70% 的屏幕高度，
            //   叠加下面的 FitHeightToContent 还会再往大里长，实际开出来接近满屏。
            //   这里收到 S(640)（约 59%），需要看更多内容时窗口可自由拉伸 / 滚动。
            ClientSize = new Size(defaultClientW, S(DefaultClientHeightDesign));

            // 最小尺寸：宽度必须放得下单个分组（536 + 边距 + 边框），
            // 高度保证至少能看到两个分组加底部操作条。
            //
            // ★ 这里必须和 SingleColumnClientWidth 联动算出来，不能写死数字。
            //   写死就会埋雷：早先 MinimumSize 写 578（按客户区 560 推的），
            //   而 SingleColumnClientWidth 后来调成 560 时忘了同步，
            //   于是"最小尺寸下可用宽"变成了 536-14=522 —— 不足一个分组宽，
            //   分组内所有控件被整体压缩、右侧贴边的控件（"重新录制"按钮、
            //   "缩放角区"的"%""像"）统统被切掉。
            //   用户看到的景象就是：窗口明明"尺寸够"，内容却是挤的。
            // ★ 同样要算上侧栏 ★：最小宽度 = 侧栏 + 单个分组，否则用户能把窗口拖到
            //   "内容区放不下一个分组"，分组被压缩、右边缘控件被切掉。
            MinimumSize = new Size(
                ClientWidthToWindowWidth(SingleColumnClientWidth + S(SidebarDesignWidth)), S(440));

            ResumeLayout(false);

            // ★★ 关键顺序：先按"当前尺寸"排一次，再恢复用户保存的尺寸，最后再排一次。★★
            //
            //   为什么不能只排最后一次：LayoutResponsive() 里 avail 的算法是
            //   · 两列模式 → avail = 客户端宽 - 34（每列各自留右边距）
            //   · 单列模式 → avail = max(GroupWidth, 客户端宽 - 24)
            //   如果直接从 1 列一跳到 2 列，最后一次算出的 avail 会偏大 10px，
            //   于是右列里贴右边的控件（"重新录制"按钮、"%""像"）被切掉尾巴；
            //   在中间尺寸（约 1000 宽）下这个偏差尤其明显。
            //   先排一次把 CurrentColumns 状态推进到正确值，再排一次尺寸就是准的。
            _groupHost.Width = GroupWidth;
            LayoutResponsive();
            ApplySavedSize();
            LayoutResponsive();
        }

        /// <summary>
        /// 把窗口高度收到"内容刚好装下"的程度，不多留空白。
        ///
        /// ── 为什么要做这件事 ──
        ///   用户反馈"设置页面有很大一部分空白"。根源是窗口高度是写死的（S(760)），
        ///   而内容在【不同宽度下高度差别极大】：
        ///     · 单列（窄）：6 个分组纵向排开，内容可能有 2000px，
        ///       这时窗口矮一点就得滚动 —— 留白是必要的（滚动条的存在证明内容装不下）；
        ///     · 两列（宽）：6 个分组摆成 2×3，内容可能只有 700px，
        ///       而窗口还是 760/1080，于是底部空掉一大片 —— 这才是用户说的"空白"。
        ///
        ///   所以正确做法不是"永远去掉留白"（那会让窄屏装不下），
        ///   而是【按当前列数算内容实际高度】：
        ///     · 内容比视口矮 → 把窗口缩到内容高度 + 底栏，一屏装完，不留白；
        ///     · 内容比视口高 → 保持现状并允许滚动。
        ///
        ///   ★ 第三十八轮修正：这里【只允许缩小，绝不允许长大】★
        ///   旧实现无条件 `ClientSize.Height = want`，于是"内容比默认窗口高"时会把窗口
        ///   一路撑到接近满屏 —— 单列模式正好就是这种情况（6 个分组纵向排开），
        ///   用户看到的正是"设置页默认非常非常的大"。
        ///   现在：只有 want 比当前高度【小】才收，其余情况原样返回（交给滚动条）。
        ///
        /// 只在"用户没有手动记过尺寸"时才自动收缩 ——
        /// 否则用户辛辛苦苦拉好的窗口尺寸会被程序每次启动强行改回去，
        /// 那比留白更让人烦（这条是用户明确提过的需求：记住窗口大小）。
        /// </summary>
        private void FitHeightToContent()
        {
            try
            {
                // 用户自己定过尺寸就别动。
                if (_edit != null && _edit.SettingsWidth > 0 && _edit.SettingsHeight > 0) return;
                if (_groupHost == null || _groupHost.Height <= 0) return;

                int contentH = _groupHost.Height + ScrollEdgeMargin * 2;
                int chromeH = S(46) + 8;              // 底栏 + 一点呼吸位
                int want = contentH + chromeH;

                int maxH = Screen.PrimaryScreen.WorkingArea.Height - S(40);
                if (want > maxH) return;             // 内容本来就装不下 → 保持并滚动

                // ★ 只收不放 ★：内容比当前窗口高时保持默认高度，靠滚动看剩下的。
                if (want >= ClientSize.Height) return;

                // 已经差不多贴合就别动，避免反复微调造成抖动。
                if (ClientSize.Height - want < S(24)) return;

                ClientSize = new Size(ClientSize.Width, want);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>
        /// 单列模式下的标准客户区宽度。
        ///
        /// 取值 = 分组标准宽 536 + 左边距 12 + 右边距 12 = 560。
        /// MinimumSize 由它反推，两个数字必须一起看，改一个另一个要跟着算 ——
        /// 见 ClientWidthToWindowWidth()。
        /// </summary>
        private int SingleColumnClientWidth { get { return S(560); } }

        /// <summary>
        /// 客户区宽度 → 窗口外框宽度。
        ///
        /// MinimumSize 约束的是【外框】尺寸，而布局用的是【客户区】宽度，
        /// 两者差着标题栏边框（水平方向一般是 2 × SM_CXSIZEFRAME ≈ 2×8 = 16）。
        /// 早先就是这里口径不一致 —— 按客户区 560 推了个 578 的 MinimumSize，
        /// 结果最小尺寸下真实可用宽只有 520 出头，内容全被压。
        /// 这里统一走"用客户区推外框"的换算，避免再踩。
        /// </summary>
        private static int ClientWidthToWindowWidth(int clientWidth)
        {
            try
            {
                int chrome = (SystemInformation.FrameBorderSize.Width +
                              SystemInformation.Border3DSize.Width * 2) * 2;
                if (chrome <= 0) chrome = 16;
                return clientWidth + chrome;
            }
            catch
            {
                return clientWidth + 16;
            }
        }

        /// <summary>分组在单列模式下的标准宽度。</summary>
        private int GroupWidth { get { return S(536); } }

        /// <summary>
        /// 分组内控件统一使用的左内边距。
        ///
        /// 设计稿里分组内的复选框一律从 Left=18 起（见 BuildUi 里各 _chk*.Location），
        /// 断行后要"占满整行"也必须从同一个位置起，否则视觉上会缩进去一格。
        /// </summary>
        private int GroupInnerPadLeft { get { return S(18); } }

        /// <summary>滚动区左右各留的边距（_groupHost.Location.X = 12，右侧对称留 12）。</summary>
        private int ScrollEdgeMargin { get { return S(12); } }

        /// <summary>
        /// 单列/两列模式各需预留的右侧空间。
        ///
        /// 单列：只需给纵向滚动条留位置。
        /// 两列：TableLayoutPanel 按百分比分列，右边一列会一直顶到容器右边界，
        ///       而 _groupHost 又固定贴在 _scrollHost 左边界上（Location.X=12），
        ///       滚动条占掉的那条宽度正好压在右列贴上 ——
        ///       所以两列时要额外多让出滚动条那一份，否则右列的尾部
        ///       （"重新录制""缩放角区""%""像"）会被切掉。
        /// </summary>
        private int RightReserve()
        {
            int bar = SystemInformation.VerticalScrollBarWidth;

            // 两列时右列永远顶到最右边，滚动条宽度必须整个让出来；
            // 再加 4px 余量，避免"差一点点"看不出来却被裁掉一个字的尾巴。
            if (_groupHost != null && _groupHost.CurrentColumns >= 2) return bar + 4;

            // 单列时 _groupHost 已经在 avail 里扣过滚动条宽度，这里不用重复扣，
            // 留一点像素余量给分组边框即可。
            return 4;
        }

        private ResponsiveGroupPanel _groupHost;
        private readonly Panel _scrollHost = new Panel();
        private readonly Panel _pager = new Panel();
        private Button _btnDefaultsRef;

        /// <summary>黑名单：从文件对话框里挑一个 exe 加进排除列表。</summary>
        private Button _btnPickExe;

        /// <summary>黑名单：十字准星点选窗口，点哪扇排除哪扇。</summary>
        private Button _btnPickWindow;
        private Button _btnSaveRef;
        private Button _btnCancelRef;

        /// <summary>
        /// 记录每个分组内控件的"基准布局"（单列模式下的原始 Left/Width），
        /// 以及每个控件该怎么随宽度伸缩。
        ///
        /// ★ 为什么必须记基准值：如果每次都用 `Left += delta` 累加，
        ///   窗口来回拉几次之后增量就会重复叠加，控件会越跑越偏（飞出去）。
        ///   用"基准 + 总增量"重算才是幂等的，拉多少次结果都一样。
        /// </summary>
        private sealed class InnerLayout
        {
            public Control Control;

            /// <summary>控件基准左边界（相对分组客户区）。</summary>
            public int BaseLeft;

            /// <summary>控件基准宽度。</summary>
            public int BaseWidth;

            /// <summary>
            /// 基准状态下，控件右边界距离分组客户区右边的距离。
            ///
            /// ★ 布局时用"右边界 = 分组内宽 - RightGap"反推，比"基准 + 增量"稳健得多：
            ///   增量法在分组变窄（两列模式）时 delta 为负，会把下拉框压成一条缝
            ///   —— 这正是"中键下拉框被压扁成一个字"的原因。
            ///   右边界距离法在变宽变窄两个方向都成立。
            /// </summary>
            public int RightGap;

            public Mode How;

            /// <summary>记录基准时，这个控件所在分组的客户区宽度。</summary>
            public int BaseInner;

            /// <summary>
            /// Grow 模式下，右边界该停在哪个控件的左边（含间距）。
            /// 为 null 表示"贴分组右边界"。
            ///
            /// ★ 为什么需要它：分组里不只有"左标签 + 控件"，还有"控件 + 右边按钮"的组合
            ///   （触发键输入框 + 重新录制）。如果输入框一直吃到分组最右边，
            ///   按钮就被挤出去了 —— 截图里"重新录制"就是这么消失的。
            /// </summary>
            public Control GrowRightNeighbor;
            public int GrowNeighborGap;

            /// <summary>Shift 模式下，右边界距分组右边界多少像素。</summary>
            public int ShiftRightGap = -1;

            /// <summary>
            /// 这个控件是否【因为文字放不下】被 ReserveLabelWidth() 撑宽过。
            ///
            /// 只有为 true 的控件才允许让右边的邻居让位 ——
            /// 设计稿里 Label 的宽度本来就留了余量（输入框压在留白上是常态），
            /// 把"没有真被撑宽"的标签也算进去，会把整条水平线推得乱七八糟。
            /// </summary>
            public bool WasTextWidened;

            /// <summary>
            /// 撑宽【之前】的宽度，也就是设计稿里给的宽度。
            ///
            /// 用来算"多占了多少"：只有"多占出来的这一截"才需要右边的控件让位。
            /// 直接拿撑宽后的宽度去和邻居比会把本来就允许的留白也算成侵占，
            /// 整条水平线会被推得乱七八糟。
            /// </summary>
            public int OriginalWidth;

            /// <summary>
            /// 因左边标签被文字撑宽而需要额外右移的像素数。
            ///
            /// ★ 这是"增量"而不是"绝对值"：BaseLeft 始终是设计稿的位置，
            ///   每次布局都从 BaseLeft + ShiftByOffset 重新算。
            ///   如果直接改 BaseLeft，来回拉几次窗口就会一次比一次偏 ——
            ///   这是布局代码里最经典的累加 bug。
            /// </summary>
            public int ShiftByOffset;

            /// <summary>
            /// 同行的"右半列"伙伴（基准位置在本控件右边、垂直方向同行的控件）。
            ///
            /// 用途：一行里并排两个复选框，如果左边那个的文字本身就长到
            /// 把它右边那半列的位置吃掉，右边这个就没地方站了 ——
            /// 这时必须让它【断行下移、独占整行】。
            /// 这是排版里的标准做法（等价于 HTML flex 容器的 flex-wrap: wrap），
            /// 而不是硬把文字切掉凑合。
            /// </summary>
            public InnerLayout RightPartner;

            /// <summary>
            /// 是否需要"断行下移"。
            ///
            /// 触发条件：本控件是某行的右半列，且同行的左半列控件
            /// 按其最终宽度算下来，已经越过了"本控件基准左边界 - 最小间隙"。
            /// 这时本控件下移一行、占满整行宽度。
            /// </summary>
            public bool WrapsToNextRow;

            /// <summary>断行时相对基准 Y 的下移量（由 ApplyOne 动态决定）。</summary>
            public int WrapOffsetY;

            /// <summary>
            /// 基准 Y（设计稿里的 Top）。
            ///
            /// 断行（换行）会把控件下移，而反复布局必须每次从基准 Y 重算，
            /// 否则会一次比一次低 —— 和 BaseLeft 是同一个道理。
            /// </summary>
            public int BaseTop;

            /// <summary>
            /// 基准高度（设计稿里的 Height）。
            ///
            /// 复选框被压窄时 WinForms 会自动折行、把 Height 涨上去，
            /// 而 Height 是同一行判定的依据 —— 必须能钉回原值。
            /// </summary>
            public int BaseHeight;

            public enum Mode
            {
                Fixed,      // 完全不动（数字框、单位小标签）
                Grow,       // 左边界不变，右边界按约束贴住（下拉框 / 文本框 / 整行说明）
                Shift,      // 整体右移，宽度不变（只锚右的右列控件、预览框）
            }
        }

        /// <summary>登记表的快速查找：Control -> InnerLayout。</summary>
        private readonly System.Collections.Generic.Dictionary<Control, InnerLayout> _innerIndex =
            new System.Collections.Generic.Dictionary<Control, InnerLayout>();

        /// <summary>把控件标记为"变宽时右边界停在邻居控件左边（留 gap 像素）"。</summary>
        /// <summary>
        /// 登记："这个控件变宽时，右边界停在 neighbor 左边 gap 处"。
        ///
        /// ★ gap 必须在这里就按 DPI 缩放 ★
        ///   调用方传的都是设计稿像素（10 / 24 / 16 这种），
        ///   而 MarkBase() 是在 ScaleDesign()【之后】跑的 ——
        ///   那一刻控件的坐标尺寸都已经放大了，基准也在放大后的坐标系里。
        ///   如果这里原样存下未缩放的 gap，比较时会拿"16（设计稿）"
        ///   去和"32（放大后的实际右余量）"比大小，
        ///   判定结果整个反过来 —— 表现就是控件被多推出去一截、跑出分组。
        ///   实测"以管理员重启"按钮就是这么被顶到分组外面的。
        /// </summary>
        private void SetGrowRightNeighbor(Control c, Control neighbor, int gap)
        {
            _pendingGrowNeighbor[c] = new System.Collections.Generic.KeyValuePair<Control, int>(
                neighbor, S(gap));
        }

        /// <summary>把控件标记为"跟着分组右边界跑，距右边 gap 像素"。</summary>
        /// <summary>
        /// 登记："这个控件跟着分组右边界走，右边留 gap"。
        ///
        /// ★ gap 同样要按 DPI 缩放 ★
        ///   理由和 SetGrowRightNeighbor 完全一样：调用方给的是设计稿像素，
        ///   而登记最终要和"放大后坐标算出来的实际右余量"比大小，
        ///   口径必须一致，否则判定反向、控件被推出分组。
        ///
        ///   gap = -1 是特例，含义是"不指定，保留基准余量"，
        ///   缩放后仍然是 -1（不缩放，避免把"无限制"变成一个具体的大数）。
        /// </summary>
        private void SetShiftFromRight(Control c, int gap)
        {
            _pendingShiftGap[c] = gap < 0 ? -1 : S(gap);
        }

        private readonly System.Collections.Generic.Dictionary<Control, System.Collections.Generic.KeyValuePair<Control, int>>
            _pendingGrowNeighbor = new System.Collections.Generic.Dictionary<Control, System.Collections.Generic.KeyValuePair<Control, int>>();

        private readonly System.Collections.Generic.Dictionary<Control, int> _pendingShiftGap =
            new System.Collections.Generic.Dictionary<Control, int>();

        private readonly System.Collections.Generic.List<InnerLayout> _innerLayouts =
            new System.Collections.Generic.List<InnerLayout>();

        /// <summary>分组在单列模式下的基准内宽（536 宽的 GroupBox，客户区约 528）。</summary>
        private const int BaseInnerWidth = 528;

        /// <summary>基准是否已经记录过。只在首次（强制单列状态）记录一次。</summary>
        private bool _innerCaptured;

        /// <summary>
        /// 给一个控件登记"基准布局"。
        ///
        /// ★ 必须在【刚设定完 Location/Size、控件还没被父容器排版过】时调用。
        ///   之前是在所有分组都排完版之后统一抓一遍，那时 GroupBox 宽度可能
        ///   还不是 GroupWidth，控件已经被压扁甚至压成 0 宽 ——
        ///   抓到的 BaseWidth=0、BaseLeft=168 全是错的，后续怎么算都不对。
        ///   这就是"下拉框被挤成一个勾"的真正原因。
        /// </summary>
        // ═══════════════════════════════════════════════════════════════════
        //  ★ 设计稿 → 当前 DPI 的显式缩放
        // ═══════════════════════════════════════════════════════════════════
        //
        // 为什么自己做而不交给 WinForms 的 AutoScaleMode.Dpi：
        //   本窗口所有控件的坐标/尺寸是代码写死的设计稿值（96 DPI 基准），
        //   而布局引擎（LayoutInner / ApplyRowWraps / GrowGroupsForWraps）
        //   全部建立在这些值上。AutoScale 会在窗体显示时把控件 bounds 改掉，
        //   于是"基准"和"实际位置"分成两套坐标系，布局一执行就把控件拽回去，
        //   造成"字大、行距小、行行重叠"。详见 BuildUi 顶部那段。
        //
        // 现在改成：关掉自动缩放，由这里把设计稿值一次性乘上 DPI 系数，
        // 缩完再记基准 —— 从此基准 == 实际，永远不会错位。
        //
        // 缩放规则：
        //   · 分组框（含它的 Location/Size）—— 整体缩放，这样分组之间的
        //     相对间距也跟着放大，125% 下不会显得挤。
        //   · 分组内的每个控件 —— Location/Size 全部缩放。
        //   · MinimumSize 之类的窗体约束由下面的 ApplySavedSize 另行处理。
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 把"设计稿像素值"换算成"当前 DPI 下的真实像素值"。
        ///
        /// 整个设置窗口的尺寸体系都用它：布局常量（GroupWidth 等）、
        /// 控件的 Location/Size（在 ScaleDesign 里统一处理）、窗体默认尺寸。
        /// 这样"设计稿上的数字"和"运行时的数字"之间只有一个乘法，
        /// 任何一个环节都不会出现两套坐标系混用（那正是重叠 bug 的来源）。
        /// </summary>
        private int S(int design)
        {
            float s = UiScale;
            if (Math.Abs(s - 1f) <= 0.001f) return design;
            return (int)Math.Round(design * s);
        }

        /// <summary>
        /// 当前界面的 DPI 缩放系数（1.0 = 100%，1.25 = 125%，1.5 = 150%）。
        ///
        /// 用 DeviceDpi 而不是查系统设置：DeviceDpi 是"这个窗口实际所在的显示器"
        /// 的 DPI，多显示器 / 混合缩放（笔记本 150% + 外接屏 100%）下才是对的。
        /// 窗口还没创建时 DeviceDpi 可能是 0 或 96，那也正好退回 1.0，安全。
        /// </summary>
        private float UiScale
        {
            get
            {
                // 缓存：布局过程中 S() 会被调用成百上千次，
                // 每次都去问 DPI 没必要。
                if (_uiScale > 0f) return _uiScale;

                float s = ResolveScaleNow();
                _uiScale = s;
                CurrentUiScale = s;   // 让内部类（ResponsiveGroupPanel）也能取到
                return s;
            }
        }

        /// <summary>
        /// 当场问一次"这块显示器现在是多少 DPI"。
        ///
        /// ★ 优先用 GetDpiForWindow，而不是 DeviceDpi ★
        ///   DeviceDpi 在句柄创建之前给的是【主屏】DPI；如果设置窗口最终落在
        ///   另一块缩放比不同的显示器上（笔记本 150% + 外接屏 100% 很常见），
        ///   拿主屏的值去渲染副屏就是错的 —— 这正是"分辨率偏低、文字发糊/锯齿"
        ///   这一类观感的来源之一。
        ///   GetDpiForWindow 问的是"这个窗口此刻所在的那块屏"，永远是对的。
        /// </summary>
        private float ResolveScaleNow()
        {
            float s = 1f;
            try
            {
                int dpi = 0;
                if (IsHandleCreated)
                {
                    try { dpi = Native.DpiOfWindow(Handle); } catch { dpi = 0; }
                }
                if (dpi <= 0)
                {
                    try { dpi = DeviceDpi; } catch { dpi = 0; }
                }
                if (dpi <= 0) dpi = 96;

                s = dpi / 96f;
                if (s < 0.5f || s > 4f) s = 1f;   // 明显不合理的值一律当 100%
            }
            catch { s = 1f; }
            return s;
        }

        private float _uiScale;

        /// <summary>
        /// 供内部类（ResponsiveGroupPanel）取到的缩放系数。
        ///
        /// 嵌套类拿不到外层实例的私有属性，所以这里留一个静态入口。
        /// 静态是安全的：同一个 UI 线程内缩放系数在窗体生命周期里恒定。
        /// </summary>
        internal static float CurrentUiScale
        {
            get
            {
                float v = _sharedUiScale;
                return v > 0f ? v : 1f;
            }
            set { _sharedUiScale = value; }
        }

        private static float _sharedUiScale;

        /// <summary>设计稿几何快照（缩放前），DPI 变化时用它重建界面。</summary>
        private sealed class DesignSnapshot
        {
            public Control Control;
            public Point Location;
            public Size Size;
        }

        private readonly System.Collections.Generic.List<DesignSnapshot> _designSnapshot =
            new System.Collections.Generic.List<DesignSnapshot>();

        private GroupBox[] _groups = new GroupBox[0];

        /// <summary>记录"缩放之前"的设计稿几何（见 RescaleForDpi）。</summary>
        private void CaptureDesignSnapshot()
        {
            _designSnapshot.Clear();
            for (int i = 0; i < _groups.Length; i++)
            {
                var gb = _groups[i];
                if (gb == null) continue;
                _designSnapshot.Add(new DesignSnapshot
                {
                    Control = gb,
                    Location = gb.Location,
                    Size = gb.Size,
                });
                for (int j = 0; j < gb.Controls.Count; j++)
                {
                    var c = gb.Controls[j];
                    if (c == null) continue;

                    // 帮助面板的内部行高也是"设计稿像素"，不缩放的话
                    // 200% 屏上标题行只有 38 物理像素、而字就有 26px，挤成一团。
                    var hp = c as HelpPanel;
                    if (hp != null) hp.SetMetrics(Fields.Sc(38), Fields.Sc(88));
                    _designSnapshot.Add(new DesignSnapshot
                    {
                        Control = c,
                        Location = c.Location,
                        Size = c.Size,
                    });
                }
            }
        }

        /// <summary>
        /// 显示器 DPI 变了（窗口被拖到另一块屏、或用户改了系统缩放比例）→
        /// 按新系数把整个界面重建一遍。
        ///
        /// ★ 这就是"按显示器实时分辨率渲染"的落地 ★
        ///   不做这一步，界面会停在旧 DPI 的尺寸上：换到高 DPI 屏之后，
        ///   控件还是按旧系数排的，字与框的比例全错 ——
        ///   而 Windows 因为我们是 PerMonitorV2 进程，不会替我们拉伸，
        ///   所以既不会自动变清楚、也不会自动变大，只会"一直不对"。
        ///
        /// 做法：回到设计稿几何 → 换系数重放 ScaleDesign → 重建布局基准 → 重排。
        /// 任何一步失败都只记日志，绝不让设置窗口崩掉。
        /// </summary>
        private void RescaleForDpi(int newDpi)
        {
            if (newDpi <= 0) return;
            float ns = newDpi / 96f;
            if (ns < 0.5f || ns > 4f) return;
            if (_uiScale > 0f && Math.Abs(ns - _uiScale) < 0.001f) return;
            if (_groups == null || _groups.Length == 0) return;

            try
            {
                Log.Info("设置窗口 DPI 变化：" + _uiScale.ToString("0.###") + " → " + ns.ToString("0.###")
                    + "（新 DPI=" + newDpi + "），按新分辨率重建界面");

                // ① 回到设计稿几何
                for (int i = 0; i < _designSnapshot.Count; i++)
                {
                    var d = _designSnapshot[i];
                    if (d.Control == null || d.Control.IsDisposed) continue;
                    d.Control.Location = d.Location;
                    d.Control.Size = d.Size;
                }

                // ② 换系数并重放缩放
                _uiScale = ns;
                CurrentUiScale = ns;
                _rebaseDone = false;
                ScaleDesign(_groups);

                // ③ 重建布局基准（_innerIndex 里还留着旧基准，必须先清）
                _innerLayouts.Clear();
                _innerIndex.Clear();
                for (int i = 0; i < _groups.Length; i++)
                {
                    var gb = _groups[i];
                    if (gb == null) continue;
                    for (int j = 0; j < gb.Controls.Count; j++) MarkBase(gb.Controls[j]);
                }
                _innerCaptured = true;

                // ④ 输入框高度是按字体实测的，换 DPI 之后必须重新量一遍
                //   （字体是 Point 单位、物理高度随 DPI 变，缓存必须作废）
                Fields.ResetCaches();
                StyleText(_txtKey, Font, true);
                StyleText(_txtBlacklist, Font, false);
                ReserveLabelWidth();

                // ⑤ 分组基准高度也要按新系数重新记
                _baseGroupHeights = new int[0];

                // ⑥ 窗口的最小尺寸约束同样是物理像素，跟着换
                try
                {
                    MinimumSize = new Size(
                        ClientWidthToWindowWidth(SingleColumnClientWidth + S(SidebarDesignWidth)),
                        S(440));
                }
                catch { }

                if (_sidebar != null) _sidebar.Width = S(SidebarDesignWidth);

                // ⑦ 重排 + 全量重绘
                LayoutResponsive();
                Invalidate(true);
                Update();
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>把设计稿坐标/尺寸按当前 DPI 缩放并直接落到控件上。</summary>
        private void ScaleDesign(params GroupBox[] groups)
        {
            float s = UiScale;

            // ══════ ★★★ 底栏按钮必须在【100% DPI 也要处理】★★★ ══════
            //
            // 原来这三行放在下面那句 "if (s == 1) return;" 之后，
            // 于是 100% DPI 下整段被跳过 —— 按钮保持设计稿的 20px 高，
            // 而 9pt 的字实际要 17px，加上下留白根本装不下，
            // 表现就是用户报的"恢复默认值/保存并生效/取消 这几个按钮
            // 字跟按钮并没有进行很好的适配"（文字被切掉）。
            //
            // 关键区别：
            //   · 100% DPI → 不需要"按 DPI 放大"，但仍然需要"按字体兜底高度"；
            //   · 200% DPI → 两件事都要做。
            // 所以这一段的调用必须提到提前返回之前。
            if (_btnDefaultsRef != null) ScaleFlatButton(_btnDefaultsRef, s);
            if (_btnSaveRef != null) ScaleFlatButton(_btnSaveRef, s);
            if (_btnCancelRef != null) ScaleFlatButton(_btnCancelRef, s);

            if (Math.Abs(s - 1f) <= 0.001f) return;   // 100% DPI 剩下的缩放都不用做

            Log.Info("ScaleDesign: DPI=" + DeviceDpi + " 缩放系数=" + s.ToString("0.###"));

            // ══ ★★★ 必须先把 Anchor 全部摘掉，再做缩放 ★★★ ══
            //
            //   这是本轮最隐蔽的一个坑，症状是"控件宽度莫名变成 1502（设计稿 430）"。
            //
            //   原因：像 _cmbAutoStart 这种控件在设计稿里带了
            //       Anchor = Top | Left | Right
            //   它表示"父容器变宽时，我跟着右边一起拉宽"。
            //   分组框本身也是 Top|Left|Right，而 ResponsiveGroupPanel
            //   在构造时就把分组塞进了 TableLayoutPanel、设好了列宽 ——
            //   于是 WinForms 的锚定逻辑在 ScaleDesign 之前就已经把控件拉宽过了。
            //
            //   时序变成：
            //       设计稿 430 → 【锚定拉伸】→ 751 → 【ScaleDesign ×2】→ 1502
            //   而我们想要的是：
            //       设计稿 430 → 【ScaleDesign ×2】→ 860
            //
            //   多出来的那一倍不是"多了点留白"，而是会让 baseRightGap 变成负数：
            //       baseRightGap = BaseInner - (BaseLeft + BaseWidth)
            //                     = 1072 - (180 + 1502) = -610
            //   ApplyOne 的 Grow 分支于是把右边界算到 innerRight 右边 610px，
            //   下拉框直接反超分组（实测溢出 602px，选项全被切掉）。
            //
            //   为什么可以放心摘掉 Anchor：
            //     MarkBase() 里本来就会把每个控件的 Anchor 改成 Top|Left
            //     （"伸缩完全由 LayoutInner 接管，两边同时作用会算两次"），
            //     也就是说本项目的布局从一开始就【不依赖】系统锚定。
            //     这里只是把"摘掉"这件事提前到缩放之前做一遍。
            for (int i = 0; i < groups.Length; i++)
            {
                var g0 = groups[i];
                if (g0 == null) continue;
                g0.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                for (int j = 0; j < g0.Controls.Count; j++)
                {
                    if (g0.Controls[j] != null)
                        g0.Controls[j].Anchor = AnchorStyles.Top | AnchorStyles.Left;
                }
            }

            // ★ 底栏按钮的缩放已经提到方法开头做了（100% DPI 也要跑，
            //   因为那里还要按字体兜一个高度下限），这里不再重复调用。

            for (int i = 0; i < groups.Length; i++)
            {
                var gb = groups[i];
                if (gb == null) continue;

                // 分组自身：位置和大小都要缩（它的 Location 是相对 _groupHost 的，
                // 但 _groupHost 在 1 列模式下整列就是它，所以缩了也对得上；
                // 真正决定它摆在哪儿的是 TableLayoutPanel，这里缩的是它的尺寸）。
                gb.Size = ScaleSz(gb.Size, s);

                // ★ 卡片标题要占掉顶部一条（TitleInsetPx），
                //   组内控件必须整体下移让位，分组本身也要相应加高 ——
                //   不然标题就会压在第一行控件上（"触发键"那几组最先遭殃）。
                int inset = 0;
                var card = gb as CardGroup;
                if (card != null)
                {
                    // ★ 这几个都是"设计稿像素"，不缩放的话 200% 屏上
                    //   圆角只有 10px（正常是 20）、竖条 3px（正常 6），
                    //   投影细得像条线 —— 整张卡片就退化回"灰底上一块白方块"，
                    //   看起来跟没改样式一样。
                    // 标题字号：13 → 14。Win11 的分区标题是 body-strong 量级，
                    // 13px 在 200% 屏上偏小、和正文拉不开层级。
                    card.TitleFontPx = (int)Math.Round(14 * s);
                    card.TitleY = (int)Math.Round(9 * s);
                    card.RadiusPx = (int)Math.Round(Design.Radius * s);
                    card.ShadowOffsetPx = (int)Math.Round(Design.ShadowOffset * s);
                    card.ShadowSpreadPx = (int)Math.Round(Design.ShadowSpread * s);
                    card.AccentBarPx = (int)Math.Round(Design.AccentBarWidth * s);

                    inset = (int)Math.Round(card.TitleInsetPx * s);
                    gb.Height += inset;
                }

                for (int j = 0; j < gb.Controls.Count; j++)
                {
                    var c = gb.Controls[j];
                    if (c == null) continue;

                    // 位置和尺寸都缩。注意用 Location/Size 而不是 Left/Top/Width/Height，
                    // 一次 SetBounds 完成，避免中间状态触发重排。
                    c.Location = ScalePt(c.Location, s);
                    c.Size = ScaleSz(c.Size, s);

                    // 让开标题：只改 Top，不动 Left/Size。
                    // 放在 DPI 缩放【之后】做，这样 inset 本身已经是缩放后的值，
                    // 不会和上面的乘法重复计算。
                    if (inset > 0) c.Top += inset;

                    // ★★★ 字号【绝对不能】在这里乘s ★★★
                    //
                    // 踩过的坑：曾经这里写着 c.Font = new Font(..., f.SizeInPoints * s, ...)
                    // 结果所有文字都被水平切掉一半（用户原话："字体被遮住了一半"）。
                    //
                    // 根因：字号用的是 GraphicsUnit.Point，而 Point 单位的字体
                    // 【已经】由系统按当前 DPI 换算过像素高度了：
                    //     9pt @ 96dpi  = 12px
                    //     9pt @ 192dpi = 24px← 系统自动放的，已经是设计稿的 2 倍
                    // 代码里再乘一次 s → 18pt @ 192dpi = 48px，
                    // 而控件高度只放大了 2 倍（比如 20 → 40px）。
                    // 结果字高 48px 装在 40px 的框里 → 被裁掉一半。
                    //
                    // 正确分工：
                    //   控件 bounds  ← 我们手动乘 s（因为 AutoScaleMode=None，系统不管）
                    //   字号point   ← 系统按 DPI 自动换算（我们什么都不用做）
                    //
                    // 所以这里【原封不动】保留继承来的字体。
                    // 换Font 时还要保证 Unit 仍是 Point（继承来的就是 Point）。
                }
            }
        }

        private static Point ScalePt(Point p, float s)
        {
            return new Point((int)Math.Round(p.X * s), (int)Math.Round(p.Y * s));
        }
        private static Size ScaleSz(Size z, float s)
        {
            return new Size((int)Math.Round(z.Width * s), (int)Math.Round(z.Height * s));
        }

        /// <summary>
        /// 缩放一个扁平按钮：尺寸 + 字号一起放大。
        ///
        /// 字号不跟着放大是自绘按钮最容易踩的坑 —— 框变大了、字没变，
        /// 视觉上就是"按钮空了一圈"，比不改还难看。
        /// </summary>
        private static void ScaleFlatButton(Button b, float s)
        {
            try
            {
                b.Size = ScaleSz(b.Size, s);
                // ★ 字号同样不乘 s —— 理由见 ScaleDesign 里的长注释：
                //   Point 单位字号已由系统按 DPI 换算过，手动再乘一次会把字撑出框。
                //   按钮框放大了、字号自动跟上 DPI，视觉上一样是对的比例。

                // ══ ★★★ 高度下限：必须按【字体实际高度】兜底 ★★★ ══
                //
                // 为什么不能只靠设计稿的固定高度：
                //   同排的 TextBox / ComboBox 高度是【按字体自动算】的（设 Size 无效），
                //   而 FlatButton 是自绘控件，高度完全由我们说了算。
                //   一个跟随字体、一个写死 —— 任何固定设计值都只能对上某一个缩放档：
                //     · 设计高 20：200% 下 40px，正好配 39px 的输入框；
                //                  但 100% 下只有 20px，装不下 17px 的字（被切掉）。
                //     · 设计高 26：100% 下 26px 够用；
                //                  但 200% 下变成 52px，比 39px 的输入框高出一大截。
                //
                //   所以在缩放之后补一个下限：文字实际高度 + 上下各 4px 留白。
                //   这样两个缩放档都能同时满足"字装得下"和"和输入框差不多高"。
                if (b.Text != null && b.Text.Length > 0 && b.Font != null)
                {
                    var need = TextRenderer.MeasureText(b.Text, b.Font,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

                    int minH = need.Height + 8;      // 上下各 4 的留白
                    if (b.Height < minH) b.Height = minH;
                }
            }
            catch { /* 字体创建失败不影响布局 */ }
        }

        private void MarkBase(Control c)
        {
            if (_innerIndex.ContainsKey(c)) return;

            // ★ BaseInner 用 GroupWidth（我们在代码里写死的分组宽度），
            //   不要用 gb.ClientSize.Width —— 此刻 GroupBox 还没经历过布局，
            //   ClientSize 可能还是未初始化的值，算出来的 RightGap 会完全错。
            int BaseInner = GroupWidth;

            var item = new InnerLayout
            {
                Control = c,
                BaseLeft = c.Left,
                BaseTop = c.Top,
                BaseHeight = c.Height,
                BaseWidth = c.Width,
                OriginalWidth = c.Width,
                BaseInner = BaseInner,
                RightGap = BaseInner - (c.Left + c.Width),
                How = InnerLayout.Mode.Fixed,
            };

            // 套用显式标记（标记优先于自动分类）
            System.Collections.Generic.KeyValuePair<Control, int> nb;
            if (_pendingGrowNeighbor.TryGetValue(c, out nb))
            {
                item.How = InnerLayout.Mode.Grow;
                item.GrowRightNeighbor = nb.Key;
                item.GrowNeighborGap = nb.Value;
            }
            else if (_pendingShiftGap.ContainsKey(c))
            {
                item.How = InnerLayout.Mode.Shift;
                item.ShiftRightGap = _pendingShiftGap[c];
            }
            else
            {
                item.How = ClassifyStretch(c);
            }

            // 关掉自己的 Anchor —— 伸缩完全由 LayoutInner() 接管，
            // 两边同时作用会算两次，控件会跑到错的位置。
            c.Anchor = AnchorStyles.Top | AnchorStyles.Left;

            _innerLayouts.Add(item);
            _innerIndex[c] = item;
        }

        /// <summary>
        /// 兜底：把还没登记过的控件按当前状态补登记一遍。
        ///
        /// 正常情况下 BuildUi 里每个控件已经 MarkBase 过，这里不会有新条目。
        /// 留这个方法是防止以后加控件时忘记调用 —— 漏登记不会崩，
        /// 只是那个控件不参与伸缩（保持原位）。
        /// </summary>
        private void CaptureInnerLayouts()
        {
            if (_innerCaptured) return;
            _innerCaptured = true;

            for (int i = 0; i < _groupHost.Controls.Count; i++)
            {
                var gb = _groupHost.Controls[i] as GroupBox;
                if (gb == null) continue;

                for (int j = 0; j < gb.Controls.Count; j++)
                {
                    MarkBase(gb.Controls[j]);
                }
            }
        }

        /// <summary>是否已经建立过布局基准（EnsureBases 只跑一次）。</summary>
        private bool _rebaseDone;

        /// <summary>仅供诊断：基准建立方式的说明，会写进 DumpGroup 输出。</summary>
        internal static string LastRebaseInfo = "(未运行)";

        /// <summary>占位方法：为了保持 LayoutResponsive() 的调用序列稳定而保留。
        ///
        /// ★ 历史：这里原本做的是"在 WinForms 自动缩放跑完之后重新采样基准"，
        ///   用来绕开"基准是 96 DPI 设计稿值、控件实际位置是缩放后值"的坐标系错位。
        ///   那是"设置里所有选项全部重叠"的根因，但那个绕法只是打补丁。
        ///
        ///   现在采用的是更彻底的做法（见 BuildUi 顶部与 ScaleDesign()）：
        ///   直接关掉 WinForms 自动缩放（AutoScaleMode.None），
        ///   由 ScaleDesign() 在【记基准之前】把设计稿值按 DPI 缩放一次。
        ///   基准即实际，坐标系从根上只有一套，不再有错位的可能。
        /// </summary>
        private void RebaseAfterAutoScale()
        {
            if (_rebaseDone) return;
            _rebaseDone = true;
            LastRebaseInfo = "AutoScaleMode.None，基准由 ScaleDesign 一次性建立";
        }

        /// <summary>判断一个控件该怎么随分组变宽而伸缩。</summary>
        private static InnerLayout.Mode ClassifyStretch(Control c)
        {
            // 预览框：钉在右上角，跟着右移但不改尺寸
            if (c is FramePreviewBox) return InnerLayout.Mode.Shift;

            // 数字框：数字就两三位，拉宽很怪 —— 固定不动
            if (c is NumericUpDown) return InnerLayout.Mode.Fixed;

            // 下拉框 / 文本框：拉长最有用（选项文字、黑名单能显示更多）
            if (c is ComboBox || c is TextBox) return InnerLayout.Mode.Grow;

            // 复选框 + 说明 Label：整行文字，拉长能一行显示完不换行。
            // 判断依据是"原始宽度已经占了大半行"，避免把小标签（如"px"）也拉长。
            if ((c is CheckBox || c is Label) && c.Width >= 200)
                return InnerLayout.Mode.Grow;

            // 其余小控件（左侧字段名标签、单位标签）：不动
            return InnerLayout.Mode.Fixed;
        }

        /// <summary>
        /// 文字宽度保底：把每个"文字型"控件的基准宽度撑到至少放得下自己的文字。
        ///
        /// 为什么需要这一步：分组内控件的坐标/尺寸是代码里写死的（Label("吸附距离", 18, 84, 66)），
        /// 这些数字是按 100% DPI + 默认字体估的。实际运行环境一旦是 125% / 150% DPI，
        /// 或者系统换了更大的 UI 字体，"缩放角区"就放不进 66px ——
        /// 表现就是用户看到的"缩放""长按""吸附距"这种缺字。
        ///
        /// ★★ 这里翻过一次车，务必记住这个教训 ★★
        ///
        ///   第一版写成了"按 y 坐标聚行 → 行内从左到右依次把后面的控件往右推"，
        ///   结果非常糟糕：这些"行"里其实塞着两组互相独立的 label+输入框
        ///   （比如"粗细 + 数字框 + px"和"圆角 + 数字框 + px"），
        ///   一旦把整行当成一条流水线，左边标签多个 20px，
        ///   右边的"圆角""px"就会被连着推走几百像素，直接飞出分组。
        ///   实测"样式"被从 x=248 推到 x=577、"圆角"推到 653 —— 全错。
        ///
        ///   根因是想当然地把"同一水平线"等同于"同一条流水线"。
        ///   绝对坐标布局里这两件事根本不是一回事：x=248 的"样式"和 x=18 的"外框颜色"
        ///   虽然在同一行，但它们各自带自己的输入框，中间隔着几百像素的空白，
        ///   没有任何理由因为左边的标签变宽就要一起挪。
        ///
        /// 所以这里**只做加宽，不做推移**：
        ///   ① 每个文字控件只保证"自己放得下自己的字"；
        ///   ② 谁和它重叠，由 ApplyOne() 的右边界机制自然解决 ——
        ///      Grow 类控件本来就是"右边界跟着分组走"，
        ///      分组变宽它变宽、分组变窄它变窄，压根不需要别人帮它让位。
        ///   这样每个控件只被处理一次，不存在任何累计漂移。
        /// </summary>
        private void ReserveLabelWidth()
        {
            // ══ 第一步：先把"文字放不下的标签"撑宽 ══════════════════
            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var it = _innerLayouts[i];
                int widest = TextNeed(it);
                if (widest <= 0) continue;
                if (widest <= it.BaseWidth) continue;

                // ★★ 撑宽不能越过"右邻约束" ★★
                //
                //   TextNeed 量的是"文字本身要��宽"，但控件实际能有多宽，
                //   上限是由它右边的控件决定的 —— 比如"当前权限：普通用户…"
                //   右边紧挨着"以管理员重启"按钮，可用宽度就那么多。
                //
                //   不设这道上限的话：
                //     BaseWidth 被撑到远超分组宽度
                //       → baseRightGap = BaseInner - (BaseLeft + BaseWidth) 变成负数
                //       → ApplyOne 的 Grow 分支把右边界算到 innerRight 右边去
                //       → 文字反超分组 364px 被切掉
                //   而且更隐蔽的是 LeftBarrier 会把这个撑宽后的宽度当成
                //   "左边那道墙"，把右边的按钮一路顶到分组外面（实测 L=1452，
                //   而分组才 1072 宽）—— 表现就是"按钮不见了 / 点不到"。
                if (it.GrowRightNeighbor != null)
                {
                    int limit;
                    InnerLayout nb;
                    if (_innerIndex.TryGetValue(it.GrowRightNeighbor, out nb))
                        limit = nb.BaseLeft + nb.ShiftByOffset - it.GrowNeighborGap - it.BaseLeft;
                    else
                        limit = it.GrowRightNeighbor.Left - it.GrowNeighborGap - it.BaseLeft;

                    if (limit > 0 && widest > limit) widest = limit;
                }

                if (widest <= it.BaseWidth) continue;

                it.BaseWidth = widest;
                it.WasTextWidened = true;
            }

            // ══ 第二步：把被撑宽的标签压住的控件整体往右推 ═════════════
            //
            // ★★ 用"聚类 + 链式推进"，而不是逐个控件单独算平移量 ★★
            //
            //   这是整块逻辑里最容易写错的地方，前面连着翻了几次车，记一下：
            //
            //   · 第一版：按 y 坐标聚行，行内从左到右依次推。错在"同一水平线"
            //     被当成了"同一条流水线" —— 实测把"样式"从 x=248 推到 577。
            //   · 第二版：每个控件独立找"左边压住我的标签"，按最大重叠量推。
            //     错在"推走的控件没把下一环也带走" —— 数字框被推到"像素"上叠住。
            //
            //   正确模型：布局是一个**有向链**。标签变宽 → 压到右边紧邻的控件
            //   → 它右移 → 又压到它右边紧邻的控件 → 继续右移……
            //   直到某个控件右边本来就有富余空间，链就断了。
            //
            //   所以做法是：
            //     ① 按"水平是否相接或相交"把控件串成若干段（cluster），
            //        留白超过一个阈值（说明本来就是两个独立列）就断开；
            //     ② 每段内按 BaseLeft 排序，从左往右走一遍，
            //        维护一个"下一个控件最小允许左边界 minLeft"，
            //        遇到冲突就把当前控件推到 minLeft（累进，不回头）；
            //     ③ 记录每个控件的"需要右移多少"，存到 ShiftByOffset，
            //        由 ApplyOne() 统一套用 —— 这里不改 BaseLeft，
            //        保证幂等（反复拉窗口不会一次比一次偏）。
            ComputeShiftOffsets();
        }

        /// <summary>
        /// 计算每个控件因"左边标签被文字撑宽"而需要的右移量，存进 <see cref="InnerLayout.ShiftByOffset"/>。
        ///
        /// 只写增量、不改基准，所以反复调用结果一致（幂等）——
        /// 这是"窗口来回拉尺寸不能越拉越歪"的保证。
        /// </summary>
        private void ComputeShiftOffsets()
        {
            // 先全部清零
            for (int i = 0; i < _innerLayouts.Count; i++)
                _innerLayouts[i].ShiftByOffset = 0;

            // ① 取所有"被撑宽过"的控件作为种子，逐个把右边的一串推走。
            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var seed = _innerLayouts[i];
                if (!seed.WasTextWidened) continue;

                int grown = seed.BaseWidth - seed.OriginalWidth;
                if (grown <= 0) continue;

                PropagatePush(seed, grown);
            }
        }

        /// <summary>
        /// 检测"并排的两个复选框，左边那个太长把右边那个挤到没位置"的情况，
        /// 给右边那个打上"断行下移"标记。
        ///
        /// ── 为什么需要这一步（这是本轮修的新问题）────────────────────────
        ///
        /// 中文 + 125% DPI 下，复选框文字实测要：
        ///   "双击左键 = 最大化 / 还原"        → 321px
        ///   "双击右键 = 九宫格吸附"           → 296px
        ///   "允许拖动已最大化的窗口（先还原）"  → 510px
        ///   "开始拖动时自动把窗口提到最前"      → 386px
        ///
        /// 而设计稿里 g4 的第 110/136 行是"两个复选框并排"（Left=18 和 Left=272）。
        /// 并排要靠 272 - 18 = 254px 就能放下左边那个 —— 实测需要 321~510px，
        /// 根本不可能。于是右边那个被挤到 x≈327 之后，
        /// 而它在 645px 宽的列里只有 315px，放不下 386px 的文字 → 尾巴被切。
        ///
        /// ── 解决方式：断行（flex-wrap）─────────────────────────────────
        ///
        /// 不硬撑、不缩字号、不省略号 —— 这些都是"凑合"。
        /// 标准做法是：放不下就换行，右边那个独占一整行、占满整行宽度。
        /// 这样文字 100% 完整显示，视觉上也是清晰的"两行"结构。
        ///
        /// ★★ 必须用【实际渲染宽度】判断，不能用 BaseWidth ★★
        ///
        ///   第一次写的时候用的是"基准宽度 vs 文字需要宽度取大"，
        ///   结果完全不触发 —— 因为左边的复选框多数是 Grow 类，
        ///   它的 BaseWidth 是设计稿给的 240，而它实际会被拉到 318（Grow 分支算出来的）。
        ///   拿 240 去判断"够不够"，永远都是"够"。
        ///
        ///   所以这个方法必须在 ApplyOne() **之后**调用，
        ///   直接读 c.Width —— 那是真正渲染出来的宽度，没有任何估算成分。
        ///
        /// 判定用的是【实际宽度】，
        /// 所以 100% DPI 下如果本来放得下就不会触发，行为完全不变。
        /// </summary>
        private void ComputeRowWraps()
        {
            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var it = _innerLayouts[i];
                it.WrapsToNextRow = false;
                it.RightPartner = null;
            }

            // ① 先给每个复选框找到"同行右邻"（基准位置上在它右边、垂直同行）
            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var a = _innerLayouts[i];
                var ca = a.Control;
                if (ca == null || ca.Parent == null) continue;
                if (!(ca is CheckBox)) continue;      // 只有并排复选框才做断行

                int aTop = a.BaseTop, aBottom = a.BaseTop + ca.Height;
                int bestLeft = int.MaxValue;
                InnerLayout best = null;

                for (int j = 0; j < _innerLayouts.Count; j++)
                {
                    var b = _innerLayouts[j];
                    if (b == a) continue;
                    if (!(b.Control is CheckBox)) continue;
                    if (b.Control.Parent != ca.Parent) continue;
                    if (b.BaseLeft <= a.BaseLeft) continue;

                    // 同行判定带容差（和 LeftBarrier 同一个口径）
                    var cb = b.Control;
                    int wOv = Math.Min(aBottom, b.BaseTop + cb.Height) - Math.Max(aTop, b.BaseTop);
                    if (wOv <= 0) continue;
                    if (wOv * 2 < Math.Min(ca.Height, cb.Height)) continue;

                    if (b.BaseLeft < bestLeft) { bestLeft = b.BaseLeft; best = b; }
                }

                if (best != null) a.RightPartner = best;
            }

            // ② 逐对判断：左边那位【实际渲染出来】的右边界是否吃掉了右边那位的位置
            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var a = _innerLayouts[i];
                var b = a.RightPartner;
                if (b == null) continue;

                var ca = a.Control;
                var cb = b.Control;
                if (ca == null || cb == null) continue;

                // a 的实际右边界（ApplyOne 已经跑过，c.Left / c.Width 都是最终值）
                int aRight = ca.Left + ca.Width;

                // b 的最低可接受左边界 = 它的基准左边界（不能往左挪，会压到 a）
                int bLeft = b.BaseLeft + b.ShiftByOffset;

                // 间隙小于 8px（设计稿像素）就算"挤住了" → b 断行
                // ★ 要按 DPI 缩放：阈值是"人眼能接受的最小间距"，
                //   而 aRight/bLeft 都已经放大了，不缩阈值的话
                //   200% 下 8（未缩放）比 16（已缩放）还松，断行会漏判。
                if (aRight + S(8) > bLeft)
                {
                    b.WrapsToNextRow = true;
                }
            }
        }

        /// <summary>
        /// 处理"整行都放不下"的超长文本：让它折行、变高，并把它下面的行继续下推。
        ///
        /// ── 为什么还需要这一步 ────────────────────────────────────
        ///
        /// ApplyRowWraps() 会把所有控件的高度都钉回设计稿的单行高度，
        /// 这是为了不让"悄悄折行"污染"谁和谁同行"的判断。
        /// 但有一类控件是【即便独占整行也放不下】的：
        ///   "允许拖动全屏 / 无边框窗口（游戏、全屏播放器，可能有风险）" 实测 733px，
        ///   而两列模式下每列只有 679px。
        /// 这种只能用多行显示 —— 硬钉成单行高会把文字裁掉一半（截图里能看到）。
        ///
        /// 所以专门给它开个口子：
        ///   ① 先恢复基准高度、由 WinForms 自己按可用宽度折行量出真实高度；
        ///   ② 用真实高度把它下面（基准 Top 更大）的控件整体下推；
        ///   ③ 记下来，给 ApplyRowWraps() 用。
        ///
        /// 判定依据是"文本需要的宽度 > 可用宽度" —— 不允许轻易触发，
        /// 只有真的放不下才走这条路。
        /// </summary>
        /// <summary>
        /// 只做"折行高度落地"，不碰 Y 坐标。
        ///
        /// ★ 为什么要把"高度"和"下推"拆开：
        ///   SyncWrappedHeights() 老版本是一边量高度、一边 `oc.Top += extra`。
        ///   加法只在"从设计稿状态出发"时才对 ——
        ///   可它在 LayoutResponsive 主流程里被调第二次时，Top 早就被 ApplyRowWraps()
        ///   按基准重排过了，再加一次 extra 就是把同一个下推量算了两遍，
        ///   于是下面每一行都比预期低一截，最后叠出"选项重叠"。
        ///
        ///   正确分工：
        ///     · Y 方向的下推统一由 ApplyRowWraps() 负责（它从 BaseTop 重算，幂等）；
        ///     · 这里只负责"这个控件到底占几行高"，纯赋值、不做加法。
        ///   ApplyRowWraps() 里的 rowH 取的就是同行最大高度，
        ///   所以折行控件自动把本行撑高、下面的行自动让位 —— 不需要在这里再推一次。
        /// </summary>
        private void SyncWrappedHeightsPopulateOnly()
        {
            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var it = _innerLayouts[i];
                var c = it.Control;
                if (c == null || c.Parent == null) continue;
                if (!(c is CheckBox) && !(c is Label)) continue;
                if (it.How != InnerLayout.Mode.Grow && !it.WrapsToNextRow) continue;
                if (it.BaseHeight <= 0) continue;

                int realH = WrappedHeightOf(it);
                if (realH <= 0) continue;
                if (c.Height != realH) c.Height = realH;   // 纯赋值，绝不做 +=
            }
        }

        /// <summary>
        /// 算出"这个复选框 / 标签按当前宽度折行后真正要占的高度"。
        ///
        /// ★★★ 主流程与末轮【必须共用这一个函数】★★★
        ///
        ///   历史上这两处折行处理是两份各自演化的实现，其中一份把
        ///   "勾选框 + 间隙"的占用【写死成 26 物理像素】（没按 DPI 缩放）。
        ///   在用户的 200% 屏上，自绘实际吃掉 52px，它只减了 26 ——
        ///   于是少算了 26px 文字区宽度、判定"放得下"→ 高度被钉回单行 →
        ///   第二行被裁掉。这正是用户报的
        ///   "允许拖动全屏无边框窗口在设置窗口较小时会被遮挡"。
        ///
        ///   教训：同一个口径在代码里出现两次，早晚会有一份先腐烂。
        ///   所以现在文字区宽度、折行测量、绘制三处全部走 CheckTextChrome。
        /// </summary>
        private int WrappedHeightOf(InnerLayout it)
        {
            var c = it.Control;
            if (it.BaseHeight <= 0) return 0;
            if (string.IsNullOrEmpty(c.Text)) return it.BaseHeight;

            int chrome = c is CheckBox ? CheckTextChrome : Fields.Sc(4);
            int textAvail = c.Width - chrome;
            int minAvail = Fields.Sc(20);
            if (textAvail < minAvail) textAvail = minAvail;

            try
            {
                Size measured = TextRenderer.MeasureText(
                    c.Text, c.Font,
                    new Size(textAvail, 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

                if (measured.Height > it.BaseHeight) return measured.Height + Fields.Sc(4);
            }
            catch { }

            return it.BaseHeight;
        }

        private void SyncWrappedHeights()
        {
            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var it = _innerLayouts[i];
                var c = it.Control;
                if (c == null || c.Parent == null) continue;
                if (!(c is CheckBox) && !(c is Label)) continue;
                if (it.How != InnerLayout.Mode.Grow && !it.WrapsToNextRow) continue;
                if (it.BaseHeight <= 0) continue;

                // ★ 口径与末轮的 SyncWrappedHeightsPopulateOnly 完全一致 ★
                //   旧实现这里先判"MinWidthNeed <= c.Width 就算放得下"，
                //   而那个 need 和绘制口径不一致，会误判成放得下 → 第二行被裁。
                int realH = WrappedHeightOf(it);
                if (realH <= 0) continue;

                if (c.Height != realH) c.Height = realH;

                if (realH > it.BaseHeight)
                {
                    int extra = realH - it.BaseHeight;
                    // 把它下面（基准 Top 更大）的同组控件整体下推
                    for (int j = 0; j < _innerLayouts.Count; j++)
                    {
                        var o = _innerLayouts[j];
                        var oc = o.Control;
                        if (oc == null || oc.Parent != c.Parent) continue;
                        if (o.BaseTop <= it.BaseTop) continue;
                        oc.Top += extra;
                    }
                }
            }
        }

        /// <summary>
        /// 断行之后把分组框撑高，保证多出来的那一行仍落在框内。
        ///
        /// ★ 只增不减：窄屏 / 100% DPI 下没有断行，分组高度保持设计稿原值，
        ///   布局和以前完全一致 —— 这条保证了改动不会带来任何回归。
        ///
        /// 高度取"分组内所有控件的底边最大值 + 下边距（12）"，
        /// 与设计稿高度取大。底边用的是<b>断行后</b>的实时 Top + Height。
        /// </summary>
        private void GrowGroupsForWraps()
        {
            for (int gi = 0; gi < _groupHost.Controls.Count; gi++)
            {
                var gb = _groupHost.Controls[gi] as GroupBox;
                if (gb == null) continue;

                int bottom = 0;
                for (int i = 0; i < _innerLayouts.Count; i++)
                {
                    var it = _innerLayouts[i];
                    var c = it.Control;
                    if (c == null || c.Parent != gb) continue;

                    int b = c.Top + c.Height;
                    if (b > bottom) bottom = b;
                }

                if (bottom <= 0) continue;

                int want = bottom + 12;

                // 分组高度是"外框"尺寸，控件底边是"客户区"坐标，差一个顶部边框。
                want += gb.Height - gb.ClientSize.Height;

                if (want > gb.Height) gb.Height = want;
            }
        }

        /// <summary>
        /// 把"需要断行"的控件整体下移，并把它下面同组的控件继续往下顶。
        ///
        /// ★ 这一步必须在 ApplyOne() **之前**做完，因为 ApplyOne 会按 c.Top 判断
        ///   "哪些控件在同一行"（LeftBarrier / PropagatePush 都依赖它）。
        ///   先把 Y 摆正，后面算 X 才准。
        ///
        /// 下移量 = 断行控件的行高 + 4px 行距，取该行所有控件高度的最大值。
        /// 同组内所有"基准 Top ≥ 断行控件基准 Top"的控件一起往下推，
        /// 这样底下的行不会被压住（等价于流式布局里"插入一行"的效果）。
        /// </summary>
        private void ApplyRowWraps()
        {
            // ★ 先全部恢复基准 Y，否则反复布局会一次比一次低（累加 bug）
            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var it = _innerLayouts[i];
                var c = it.Control;
                if (c == null || c.Parent == null) continue;
                if (c.Top != it.BaseTop) c.Top = it.BaseTop;

                // ★★ 顺便把高度也钉回基准 ★★
                //
                //   复选框一旦被压得比文字窄，WinForms 会自动折行、把 Height 从 22 涨到 44。
                //   而"谁和谁同行"是靠 BaseTop + Height 判断的 ——
                //   这个隐形的高度变化会把下一行的控件误判成"同行"，
                //   于是那道"左边的墙"被算到几百像素以外，把整行控件顶飞。
                //   实测就是它把"缩放角区"顶到 x=526 压成 20px 的。
                //   （真正让文字不被切，靠的是宽度下限 + 断行，不是靠允许它折行。）
                //   ★ 但"断行之后仍放不下"的控件（超长整句）例外：
                //     那种情况本来就该显示成两三行，硬钉回单行高会把文字裁掉。
                //     见下面 SyncWrappedHeights()。
                if (it.BaseHeight > 0 && c.Height != it.BaseHeight) c.Height = it.BaseHeight;
            }

            // ★★ 关键：下移量必须按控件"累积"，不能一个个 wrap 直接赋值。★★
            //
            //   早先写成"遇到一个要断行的行，就把该行及以下全部设成 BaseTop + push"，
            //   如果有两处要断行（本机 125% DPI 下 g4 里就有两对），
            //   第二遍会把第一遍的结果整个覆盖掉 —— push 只生效一次，
            //   同时"第一个断行点以下"的控件被推了两次中的一次，位置全乱。
            //
            //   正确做法：先把每个断行点算成"一个带位置的增量"，
            //   再按 BaseTop 顺序累加给"在该行【下面】"的所有控件。
            //   这样 N 个断行点 = 累积下移 N 次，语义正确且幂等。
            //
            // ══ ★★★ 这里踩过一个最严重、也最难看的坑：整屏选项全部重叠 ★★★ ══
            //
            //   原来的判定是 `o.BaseTop >= bumps[b].Key` —— 注意是"大于【等于】"。
            //   断行点的 Key 记的就是【那一行自己的 BaseTop】，
            //   于是"断行所在这行"也被算成"在断行点及以下"，被推了一次。
            //
            //   后果（125% DPI、本机 g4 里 B 行正要断行时）：
            //     · A 行（BaseT=110，含"双击左键 = 最大化 / 还原"）→ 110 >= 110 → 被推 → T=136
            //     · B 行（BaseT=110，含"双击右键 = 九宫格吸附"）→ 同样被推 → T=136
            //     · 但 B 行是【要断到下一行】的那个，它本来也应该在 136 ——
            //       于是 A 行和 B 行挤在了同一个 Y 上，"双击左键"和"双击右键"两行完全重叠。
            //     · C 行被 110 的 bump 推一次、又被 110 的第二个 bump 推一次 ...
            //       每多一对并排复选框就多叠一层，最后整屏选项糊成一团。
            //
            //   正确的语义只有一条：
            //     · 断行点所在【行本身】不动（它在上半行）；
            //     · 该行【下面】的行才整体下移 rowH + 4；
            //     · "被断下来"的那个控件单独挪到 rowY + rowH + 4（它下面按行推）。
            //
            //   所以判定必须是【严格大于】：o.BaseTop > bumps[b].Key。
            var bumps = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, int>>();

            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var wrap = _innerLayouts[i];
                if (!wrap.WrapsToNextRow) continue;

                var wc = wrap.Control;
                if (wc == null || wc.Parent == null) continue;

                // 本行高度：取同行（基准 Top 相同）控件的最大高度
                int rowH = wc.Height;
                for (int j = 0; j < _innerLayouts.Count; j++)
                {
                    var o = _innerLayouts[j];
                    if (o.Control == null || o.Control.Parent != wc.Parent) continue;
                    if (o.BaseTop != wrap.BaseTop) continue;
                    if (o.Control.Height > rowH) rowH = o.Control.Height;
                }

                // ★ 同一个断行点只记一次（一对并排复选框里可能有多个 Wrap=Y，
                //   它们共用同一个 BaseTop，重复记会让下面的行被推两遍）。
                bool dup = false;
                for (int b = 0; b < bumps.Count; b++)
                {
                    if (bumps[b].Key == wrap.BaseTop) { dup = true; break; }
                }
                if (dup) continue;

                bumps.Add(new System.Collections.Generic.KeyValuePair<int, int>(
                    wrap.BaseTop, rowH + 4));
            }

            if (bumps.Count == 0) return;

            // 按"断行点基准 Y"从小到大累加，保证多个断行点叠加正确
            bumps.Sort((x, y) => x.Key.CompareTo(y.Key));

            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var o = _innerLayouts[i];
                var oc = o.Control;
                if (oc == null || oc.Parent == null) continue;

                int dy = 0;
                for (int b = 0; b < bumps.Count; b++)
                {
                    // ★★ 严格大于：断行点所在这行本身不动，它【下面】的行才被推。★★
                    if (o.BaseTop > bumps[b].Key) dy += bumps[b].Value;
                }

                // ★ 被断下来的那个控件（自己 Wrap=Y）落到"本行下方一行"。
                if (o.WrapsToNextRow)
                {
                    int selfShift = 0;
                    for (int b = 0; b < bumps.Count; b++)
                    {
                        if (bumps[b].Key == o.BaseTop) selfShift = bumps[b].Value;
                    }
                    if (selfShift > 0) dy += selfShift;
                }

                oc.Top = o.BaseTop + dy;
            }
        }

        /// <summary>
        /// 把一个控件的"增长量"沿水平方向向右传播。
        ///
        /// 传播规则：只影响"基准位置在本控件右边、且垂直方向有重叠、
        /// 且两者之间本来就没有留出足够空隙"的那一个（最靠近的那个），
        /// 推走之后由它继续往右传播，形成链。
        /// </summary>
        private void PropagatePush(InnerLayout src, int amount)
        {
            var s = src.Control;

            // ★ 和 LeftBarrier 同一个道理：全部用【基准坐标】判断关系。
            //   混用实时 Top 会在断行的多轮试探期间算出错误的"谁压住了谁"。
            int sTop = src.BaseTop, sBottom = src.BaseTop + s.Height;

            InnerLayout next = null;
            int nextLeft = int.MaxValue;

            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var cand = _innerLayouts[i];
                if (cand == src) continue;
                if (cand.Control.Parent != s.Parent) continue;

                // 必须在 src 右边（按基准位置判断）
                if (cand.BaseLeft < src.BaseLeft + src.OriginalWidth) continue;

                // ★ 不要推 Shift 类控件：它们的位置由"分组右边界"决定
                //   （预览框、"重新录制"按钮、右侧复选框…），
                //   你给它加 ShiftByOffset 也没用 —— ApplyOne 里 Shift 分支
                //   根本不读这个值，推了等于白推，反而会让链断在这里。
                //   链遇到它就自然终止，这正是我们要的行为：
                //   它本来就和左边那组不是同一条流水线。
                if (cand.How == InnerLayout.Mode.Shift) continue;

                // 垂直方向要重叠（同一行）
                // 同行判定带容差（和 LeftBarrier 同一个口径，见那里的详细说明）
                var cc = cand.Control;
                int pOv = Math.Min(sBottom, cand.BaseTop + cc.Height) - Math.Max(sTop, cand.BaseTop);
                if (pOv <= 0) continue;
                if (pOv * 2 < Math.Min(s.Height, cc.Height)) continue;

                // 取最靠左的那个（就是紧邻的下一个）
                if (cand.BaseLeft < nextLeft)
                {
                    nextLeft = cand.BaseLeft;
                    next = cand;
                }
            }

            if (next == null) return;

            // ★ 关键：只有"增长之后确实压过去了"才推。
            //   如果两者之间本来就有富余（比如标签右边留了 50px，只长了 20px），
            //   那就什么事都没有 —— 这一条保证了不会出现"没压住也乱推"。
            int newRight = src.BaseLeft + src.BaseWidth + src.ShiftByOffset;
            int gap = next.BaseLeft + next.ShiftByOffset - newRight;

            // ★ keepGap 要缩放：它是"控件之间必须保留的最小空隙"（设计稿 6px），
            //   而 gap 是放大后坐标算出来的差值。两者口径必须一致，
            //   否则高 DPI 下 keepGap 显得过松，标签压住了也不推。
            int keepGap = S(6);
            if (gap >= keepGap) return;    // 还有 keepGap 以上的间隙，不用动

            int push = keepGap - gap;
            next.ShiftByOffset += push;

            // 继续往右传播：它被推走后可能又压到再右边的控件。
            // 递归深度受"同一行控件数量"限制（个位数），不会爆栈。
            PropagatePush(next, push);
        }

        /// <summary>
        /// 控件"至少要多宽才放得下自己的文字"，<b>不看 Grow / Shift 分类</b>。
        ///
        /// 和 <see cref="TextNeed"/> 的区别：TextNeed 是给"文字保底加宽"用的，
        /// 对 Grow 类一律返回 0（因为它们的宽度本来就由右边界动态决定，改 BaseWidth 会算歪）。
        /// 而这里是在 ApplyOne 里给"已经算出来的宽度"兜底下限，
        /// 必须对 Grow 类也生效 —— 否则 Grow 复选框会被压到比文字还窄、
        /// 悄悄折成两行、高度变掉，进而把整行布局判断带偏。
        /// </summary>
        /// <summary>
        /// 复选框文字区左边被"方块 + 间隙"占掉的固定宽度。
        ///
        /// ★★★ 这个数必须被【三个地方】共用，否则就会出现"选项被遮挡"★★★
        ///   ① 绘制：FlatCheck.OnPaint 里文字从 box + gap 处开始；
        ///   ② 布局预算：MinWidthNeed / NaturalWidth 决定这个复选框至少要/至多多宽；
        ///   ③ 折行判定：SyncWrappedHeights 用它算"文字真正能用的宽度"。
        ///
        ///   踩过的坑（用户报的"允许拖动全屏无边框窗口在窗口较小时会被遮挡"）：
        ///   布局用的是 WinForms 的 CheckBox.PreferredSize（约 40px 的方块+内边距），
        ///   而自绘实际吃掉 52px（17×DPI 的方块 + 9×DPI 的间隙）。
        ///   于是"按布局算刚好放得下"的宽度，到绘制时文字区少了十几像素 →
        ///   文字折成两行，而高度被钉死在单行 → 第二行被裁掉。
        ///   控件矩形完全合法，越界检查也发现不了，只有把口径统一才对。
        /// </summary>
        private static int CheckTextChrome
        {
            // 第四十二轮：复选框变成开关之后，左侧占用从 26 设计px 增到 44 设计px
            //   （轨道 34 + 间隙 10）。布局预算、折行测量、绘制三处仍然共用这一个数，
            //   所以加宽之后不会出现"布局以为放得下、绘制时折行被裁掉"。
            get { return Fields.Sc(44); }
        }
        private static int MinWidthNeed(Control c)
        {
            if (c == null) return 0;
            try
            {
                if (c is CheckBox)
                {
                    // ★ 不能用 PreferredSize：那是 WinForms 默认勾选框布局的宽度，
                    //   和自绘实际占用的宽度差十几像素（见 CheckTextChrome）。
                    int tw = TextRenderer.MeasureText(c.Text, c.Font).Width;
                    if (tw > 0) return tw + CheckTextChrome;
                }
                if (c is Label || c is CheckBox)
                {
                    if (string.IsNullOrEmpty(c.Text)) return 0;
                    int tw = TextRenderer.MeasureText(c.Text, c.Font).Width;
                    if (tw <= 0) return 0;
                    return tw + 4;
                }
            }
            catch { }
            return 0;
        }

        /// <summary>控件文字实际需要多宽（不需要保底时返回 0）。</summary>
        private static int TextNeed(InnerLayout it)
        {
            var c = it.Control;

            // ── 按钮：文字不能缺字（"重新录制"被压成"重新录"就是这里没管）──
            //   按钮宽度要按 PreferredSize 算，它包含了内边距、边框，
            //   比自己拿 TextRenderer 量再加常数准得多。
            if (c is Button)
            {
                if (c.Width >= 200) return 0;          // 宽按钮（保存/取消）不用管
                try
                {
                    int need = c.PreferredSize.Width;
                    if (need > 0) return need;
                }
                catch { }
                return 0;
            }

            bool isLabel = c is Label;
            bool isCheck = c is CheckBox;
            if (!isLabel && !isCheck) return 0;

            // ★★ Grow 类控件一律不参与文字保底 ★★
            //
            //   它们的宽度不是"设计稿给的固定值"，而是由"右边界 - 左边界"实时算出来的
            //   （见 ApplyOne 的 Grow 分支），本来就会随分组变宽而变宽。
            //   给它们改 BaseWidth 会连带把 baseRightGap 算歪 ——
            //   baseRightGap = BaseInner - (BaseLeft + BaseWidth)，
            //   一旦 BaseWidth 被撑得比分组还宽，baseRightGap 变成负数，
            //   ApplyOne 里就成了"越宽越往右铺"，实测溢出 194px 直接飞出分组。
            //
            //   至于它们会不会缺字：不会。分组最窄时它们也至少有基准宽度，
            //   而基准宽度是设计稿里按整行排好的，够用。
            if (it.How == InnerLayout.Mode.Grow) return 0;

            // 整行说明类的长文本 Label
            // ★ 注意这一条【不能】管到复选框：复选框宽度超过 200 是常态
            //   （"开始拖动时自动把窗口提到最前"就有 250），
            //   而它恰恰是最容易缺字的那一类。早先把这条写在复选框判断之前，
            //   结果两个 240/250 宽的复选框直接被跳过、尾巴一直被切。
            if (isLabel && it.BaseWidth >= 200) return 0;

            // ★★ 复选框：必须用 PreferredSize，【不要】用 TextRenderer + 常数 ★★
            //
            //   这里翻过一次车，而且方向正好是反的，务必记牢：
            //   我之前误以为"AutoSize=false 时 CheckBox.PreferredSize 会返回当前尺寸"，
            //   于是改用 TextRenderer 量文字再自己加 24px 当勾选框的账。
            //   实测下来这个 24px 根本不够 —— 勾选框本体 13px + 左内距 2 + 文字右留白，
            //   真实开销约 37~46px。于是"文字恰好够宽"被误判成"够用"，
            //   尾巴一直被切：截图里"…就立即生效"缺尾、"九宫格吸附"缺"附"。
            //
            //   实测数据（125% DPI，微软雅黑 9pt，本机）：
            //     文字               TextRenderer   +24    PreferredSize   差额
            //     需要长按触发…生效      494        518        535        +17
            //     双击右键 = 九宫格吸附   262        286        296        +10
            //     开始拖动时自动…最前    350        374        386        +12
            //     双击左键 = 最大化/还原  286        310        321        +11
            //     → TextRenderer+24 恒定少 10~17px，正好切掉最后一个字。
            //
            //   而 CheckBox.PreferredSize 在 AutoSize=false 下【是有效的】，
            //   它内部走的是 ButtonBase.GetPreferredSizeCore，
            //   会把勾选框 + 内边距 + 文字全部算进去（实测 535/296/386，均准确）。
            //   直接用它的返回值，不自己猜常数 —— 这是"成熟方案"。
            //
            //   Shift 类也要管：它们贴着分组右边，宽度不够一样缺字，
            //   而且它们不吃 ShiftByOffset，加宽只会往左长，撞不到别人。
            if (isCheck)
            {
                try
                {
                    int need = c.PreferredSize.Width;
                    if (need > 0) return need;
                }
                catch { }

                // 极端兜底：万一 PreferredSize 不可用，至少保证文字不被切
                int cw = 0;
                try { cw = TextRenderer.MeasureText(c.Text, c.Font).Width; }
                catch { return 0; }
                if (cw <= 0) return 0;
                return cw + 44;
            }

            int textW = 0;
            try { textW = TextRenderer.MeasureText(c.Text, c.Font).Width; }
            catch { return 0; }

            if (textW <= 0) return 0;

            return textW + 4;
        }

        /// <summary>
        /// 按分组当前内宽重算内部控件的位置与宽度。
        ///
        /// ★ 用"右边界反推"而不是"基准 + 增量"：
        ///   增量法在两列模式下 delta 为负，会把下拉框压成一条缝
        ///   （截图里"中键"只剩一个字）—— 这是纯断言抓不到、只有看图才发现的问题。
        ///
        /// ★ 分轮处理：Shift 控件的位置决定 Grow 控件的右边界约束，
        ///   所以必须先算 Shift、再算 Grow，最后 Fixed。
        /// </summary>
        private void LayoutInner()
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var want = pass == 0 ? InnerLayout.Mode.Shift
                         : pass == 1 ? InnerLayout.Mode.Grow
                         : InnerLayout.Mode.Fixed;

                for (int i = 0; i < _innerLayouts.Count; i++)
                {
                    var it = _innerLayouts[i];
                    if (it.How != want) continue;
                    ApplyOne(it);
                }
            }
        }

        private void ApplyOne(InnerLayout it)
        {
            var c = it.Control;
            if (c.Parent == null) return;

            // ★ 左边界 = 设计稿位置 + "被左边撑宽的标签挤走的量"。
            //   这个量由 ReserveLabelWidth() 里的链式传播算好，只跟设计基准有关，
            //   所以反复布局结果一致（幂等），不会越拉越偏。
            int baseLeft = it.BaseLeft + it.ShiftByOffset;

            // ★ 用"实时内宽相对基准内宽的变化量"来算，而不是绝对值。
            //   BaseInner 记的是 GroupWidth（总宽），这里读的是 ClientSize（客户区），
            //   两者差一个固定边框宽度，直接混用会差几像素 ——
            //   用增量就自动把这个固定差消掉了，单列时宽度精确等于设定值。
            int inner = c.Parent.ClientSize.Width;
            int delta = inner - it.BaseInner + GroupChrome;
            int innerRight = inner - GroupChrome;   // 分组"内容区"右边界（客户区口径）

            switch (it.How)
            {
                case InnerLayout.Mode.Grow:
                {
                    // 右边界：
                    //   · 有"右邻居约束"（输入框右边还有个按钮那种）→ 停在邻居左边；
                    //   · 没有 → 贴分组内容区右边界，但要保持"距右边多少"和基准一致。
                    //
                    // ★★ 两条路径互斥，不能叠加 ★★
                    //   早先把 baseRightGap 的扣减写在两个分支外面，
                    //   结果"邻居约束"算完又减了一遍基准余量，输入框被压成 20px。
                    //   邻居约束本来就精确指明了右边缘该在哪，再扣一次纯属重复。
                    int right;
                    if (it.GrowRightNeighbor != null)
                    {
                        // ★★ 右邻居的"实际左边缘"，而不是它的设计基准坐标 ★★
                        //
                        //   LayoutInner() 是【分轮】跑的：先 Shift、再 Grow、最后 Fixed。
                        //   也就是说轮到 Grow 控件时，作为右邻居的 Shift 控件
                        //   （"重新录制""以管理员重启"这类跟着分组右边界跑的按钮）
                        //   已经排好位置了 —— 此时它的 Left 就是屏幕上真实的位置。
                        //
                        //   早先这里无条件用 nb.BaseLeft + nb.ShiftByOffset，
                        //   而 Shift 类控件的 ShiftByOffset 恒为 0、BaseLeft 恒为设计稿值，
                        //   于是输入框的右边界被钉死在"设计稿里按钮的位置"上：
                        //     分组 1800 宽时，输入框右边界停在 548，
                        //     而按钮实际已经跑到 1560 —— 中间空出 1000+ 像素的白板。
                        //   表现就是用户看到的"触发键那一行空了一大片"。
                        //
                        //   Fixed / Grow 类邻居的实时 Left 本来就等于 BaseLeft + ShiftByOffset
                        //   （ApplyOne 就是这么写的），所以只有 Shift 类需要特判。
                        InnerLayout nb;
                        int nbLeft = it.GrowRightNeighbor.Left;
                        if (_innerIndex.TryGetValue(it.GrowRightNeighbor, out nb)
                            && nb.How != InnerLayout.Mode.Shift)
                        {
                            nbLeft = nb.BaseLeft + nb.ShiftByOffset;
                        }

                        right = nbLeft - it.GrowNeighborGap;
                    }
                    else
                    {
                        // 基准下它右边缘距分组内容区右边有多少，现在就留多少。
                        // 用"未偏移"的基准宽度算 —— 这是纯几何余量，
                        // 跟它在水平方向被推了多少无关，把偏移算进来就是重复扣减。
                        int baseRightGap = it.BaseInner - (it.BaseLeft + it.BaseWidth);
                        right = innerRight - baseRightGap;
                    }

                    int w = right - baseLeft;
                    if (w < 20) w = 20;

                    // ★ 复选框不要无脑拉满。
                    //   它的宽度对用户没有任何意义（不像下拉框拉宽能多显示选项），
                    //   拉满只会把右边的邻居顶走 ——
                    //   截图里"允许拖动已最大化的窗口"拉满 341px 之后，
                    //   右边的"开始拖动时自动把窗口提到最前"被挤得只剩半句。
                    //   所以给复选框设个上限：够放下文字再多一点余量就停。
                    if (c is CheckBox)
                    {
                        int natural = NaturalWidth(it);
                        if (natural > 0 && w > natural) w = natural;
                    }

                    // ★ 同时也要有个【下限】：不能比"放得下文字"还窄。
                    //   踩过的坑：只设了上限没设下限，于是"拖动时自动对齐其它窗口的边框"
                    //   被压到 382px（文字要 386px），WinForms 自动折成两行、
                    //   控件高度从 22 悄悄涨到 44 ——
                    //   而布局里所有"谁和谁同行"的判断都按 22 算的，
                    //   这个隐形的高度变化把同一行的"缩放角区"误判成被它压住，
                    //   一路顶到 x=526 压成 20px（截图里那三个挤成一坨的元凶）。
                    //   ★ 用 MinWidthNeed()（不看 Grow 分类），
                    //     不能用 TextNeed()（对 Grow 类一律返回 0，下限永远不生效）。
                    if (c is CheckBox || c is Label)
                    {
                        int need = MinWidthNeed(c);

                        // ★★ 下限绝对不能超过"这一行实际能给到的宽度" ★★
                        //
                        //   踩过的坑：把下限写成"文字需要多宽就多宽"，
                        //   于是"填窗口标题或程序的关键字，逗号分隔；命中就完全不接管"
                        //   这种整行说明（文字要 1030px）把控件直接撑到 1030px、
                        //   冲出分组 365px —— 比原来还糟。
                        //
                        //   正确语义：下限只用来保证"不该折行的地方别折行"，
                        //   上限始终是这一行真正能给到的宽度（right - baseLeft）。
                        //   真的放不下时交给 SyncWrappedHeights() 折行处理，
                        //   而不是靠把控件撑爆来硬塞。
                        int limit = right - baseLeft;
                        if (limit < 20) limit = 20;
                        if (need > limit) need = limit;

                        if (need > 0 && w < need) w = need;
                    }

                    c.Left = baseLeft;
                    c.Width = w;

                    // ★★ 高度也要锁死 ★★
                    //   复选框一旦被压到比文字还窄，WinForms 会自己折行把 Height 涨上去；
                    //   而 Height 是"同一行判定"的依据，偷偷变高会把整行布局带偏。
                    //   这里显式钉回设计稿高度（BaseHeight），
                    //   配合上面的宽度下限，从两头堵住自动折行。
                    if (c is CheckBox && c.Height != it.BaseHeight && it.BaseHeight > 0)
                        c.Height = it.BaseHeight;

                    break;
                }

                case InnerLayout.Mode.Shift:
                {
                    // ★ 断行到整行的控件（"双击右键""开始拖动时自动…"）例外：
                    //   它现在是全行独占，应当从分组左边一直拉到右边，
                    //   而不是继续"贴着右边界、宽度不变"。
                    //   否则断行只是把它挪下来、文字照样放不下 —— 白断。
                    if (it.WrapsToNextRow && c is CheckBox)
                    {
                        int wrapLeft = GroupInnerPadLeft;
                        int wrapRight = innerRight;
                        int wrapW = wrapRight - wrapLeft;
                        if (wrapW < 20) wrapW = 20;

                        c.Left = wrapLeft;
                        c.Width = wrapW;
                        break;
                    }

                    // ★ Shift 类控件（预览框、"缩放角区"那一组、右侧复选框…）的语义是
                    //   "跟着分组右边界走" —— 它们本来就摆在一行的右半边。
                    //
                    //   所以这里**不要用 BaseLeft + delta 硬推**：
                    //   delta 依赖 GroupChrome（分组总宽 - 客户区宽），
                    //   而 GroupChrome 在分组还没真正排过版时量出来是 0，
                    //   两列模式下 delta 会算大几十像素，控件直接被推到分组外面
                    //   （实测"26"跑到最右边、"双击右键 = 九宫"被切掉一半）。
                    //
                    //   正确做法是【先把右边界定死，再往左算宽度】：
                    //     ① 右边界 = 基准右边缘距分组右边的距离，这里是固定不变的；
                    //     ② 左边界 = 右边界 - 宽度。
                    //   这样控件要变宽时是【往左长】，永远不会冲出右边 ——
                    //   早先写成"左边界固定、宽度用新值"，
                    //   复选框一加宽就从 x=272 撑到 x=646，直接飞出分组 51px。
                    int originalRight = it.BaseLeft + it.OriginalWidth;
                    int gapToRight = it.BaseInner - originalRight;

                    // ShiftRightGap 显式指定过就以它为准（比如预览框贴右 16px）
                    if (it.ShiftRightGap >= 0 &&
                        it.ShiftRightGap < gapToRight)
                    {
                        gapToRight = it.ShiftRightGap;
                    }

                    int rightEdge = innerRight - gapToRight;

                    // ★ 左边还有控件时，加宽不能压到它。
                    //   "缩放角区"这种标签右边有富余、加宽会往左长，
                    //   而它左边就紧挨着"像素" —— 不加这道限制就会叠上去。
                    int minLeft = baseLeft;
                    int barrier = LeftBarrier(it);
                    if (barrier != int.MinValue && barrier > minLeft) minLeft = barrier;

                    int width = it.BaseWidth;
                    if (width > rightEdge - minLeft) width = rightEdge - minLeft;  // 放不下就认了
                    if (width < 20) width = 20;

                    int left = rightEdge - width;
                    if (left < minLeft) left = minLeft;
                    if (left < 0) left = 0;

                    c.Left = left;
                    c.Width = Math.Max(20, rightEdge - left);
                    break;
                }

                default:
                    c.Left = baseLeft;
                    c.Width = it.BaseWidth;
                    break;
            }
        }

        /// <summary>
        /// 复选框的"自然宽度"：刚好放得下勾选框 + 文字，再留一点余量。
        ///
        /// 用来给 Grow 类复选框封顶 —— 它们本来会被拉到分组右边界，
        /// 但复选框拉那么宽毫无意义，只会把同行右边的控件顶走。
        /// </summary>
        private static int NaturalWidth(InnerLayout it)
        {
            var c = it.Control;
            try
            {
                // ★ 和 TextNeed()/MinWidthNeed() 保持同一个口径 ★
                //   统一用"文字宽 + 自绘实际占用（CheckTextChrome）"，
                //   不用 WinForms 的 PreferredSize —— 后者比自绘少十几像素，
                //   会让"按布局刚好放得下"的复选框在绘制时折行、第二行被裁掉。
                if (c is CheckBox)
                {
                    int tw = TextRenderer.MeasureText(c.Text, c.Font).Width;
                    if (tw > 0) return tw + CheckTextChrome;
                }

                int textW = TextRenderer.MeasureText(c.Text, c.Font).Width;
                if (textW <= 0) return 0;
                return textW + 28;   // 勾选框 + 内边距 + 一点余量
            }
            catch { return 0; }
        }

        /// <summary>
        /// 本控件左边"最贴近的邻居"的右边界（含间隙）。
        ///
        /// 用途：Shift 类控件因为文字不够、需要往左加宽时，
        /// 用它挡住 —— 不能压到左边的控件上
        /// （截图里"缩放角区"就是从右边往左长，直接盖在"像素"上面）。
        ///
        /// 找不到邻居就返回 <see cref="int.MinValue"/>，表示左边是空的、随便长。
        /// </summary>
        private int LeftBarrier(InnerLayout self)
        {
            var c = self.Control;
            if (c.Parent == null) return int.MinValue;

            // ★★ 必须用 BaseTop / BaseLeft（设计稿坐标）判断"谁在谁左边、谁和谁同行"，
            //    不能用实时的 c.Top。★★
            //
            //   踩过的坑：这里原先用 c.Top 做"垂直是否重叠"的判断，
            //   而水平方向用 other.BaseLeft。两者口径不一致，
            //   在【断行的多轮试探】期间会算出完全错误的结果：
            //   断行会把 Y 改来改去，某一轮里"填窗口标题"那行（BaseTop=216）
            //   的实时 Top 恰好还是 0（还没落地），于是被判定成和 T=84 的
            //   "缩放角区"同行 → 它的右边界 518 被当成"左边那道墙"，
            //   把"缩放角区"一路顶到 526、压成 20px。
            //   截图里那三个挤成一坨的就是它。
            //
            //   布局代码里混用"基准坐标"和"实时坐标"是这类玄学 bug 的头号来源，
            //   要么全用基准，要么全用实时，绝不能一半一半。
            int top = self.BaseTop;
            int bottom = self.BaseTop + c.Height;
            int best = int.MinValue;

            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var other = _innerLayouts[i];
                if (other.Control == self.Control) continue;
                if (other.Control.Parent != c.Parent) continue;

                // 只认在基准位置上就在 self 左边的
                if (other.BaseLeft >= self.BaseLeft) continue;

                // ★★ 判"同行"不能直接用矩形相交 ★★
                //
                //   设计稿里行距很紧（"吸附距离"那行 BaseTop=81，
                //   下一行复选框 BaseTop=110，只差 29px），而 125% DPI 下
                //   数字框本身就有 30 出头的高度 —— 矩形天然会"咬"进去几个像素。
                //   用相交判断就会把【下一行】的复选框也算成本行的"左边的墙"，
                //   于是右边的数字框被顶到 x=574、压成 20px（截图里"26"那一格）。
                //
                //   正确做法：判"同一行"看【两条水平带的重叠是否过半】。
                //   只有真正同排的控件才算同一条流水线 ——
                //   这和 ReserveLabelWidth 里"不能把同一水平线当成同一条流水线"
                //   是同一个教训的另一个面。
                var o = other.Control;
                int oTop = other.BaseTop;
                int oBottom = other.BaseTop + o.Height;
                int ov = Math.Min(bottom, oBottom) - Math.Max(top, oTop);
                if (ov <= 0) continue;
                if (ov * 2 < Math.Min(c.Height, o.Height)) continue;   // 重叠不足半行 → 不算同行

                int right = other.BaseLeft + other.BaseWidth + other.ShiftByOffset;
                if (right > best) best = right;
            }

            if (best == int.MinValue) return int.MinValue;
            return best + 8;
        }

        /// <summary>
        /// 分组客户区宽度与"分组总宽"的差值（GroupBox 的边框 + 内边距）。
        ///
        /// 基准是用 GroupWidth（分组总宽 536）记的，实时计算用的是 ClientSize（客户区），
        /// 两者口径不一致会差这几个像素 —— 单列时会发现控件比设定值略窄。
        ///
        /// ★ 这里不能"懒加载时量一次就缓存"：分组刚建出来还没排过版时，
        ///   Width 和 ClientSize 可能还没建立正确关系（实测量到 0），
        ///   缓存下来就是永久错误，所有 delta 全歪。
        ///   改成每次现算：有关系就用关系的，没关系就用 GroupBox 的固定边框常量兜底。
        ///   GroupBox 的边框宽度是系统固定值（2px 边框 + 3px 内缩 ≈ 6~8），
        ///   估算 + 实测双保险，不会出大偏差。
        /// </summary>
        private int GroupChrome
        {
            get
            {
                if (_groupHost != null && _groupHost.Controls.Count > 0)
                {
                    var gb = _groupHost.Controls[0] as GroupBox;
                    if (gb != null && gb.Width > 0)
                    {
                        int c = gb.Width - gb.ClientSize.Width;

                        // 合理范围校验：边框差值不可能为 0，也不可能超过 20px。
                        // 越界说明此刻还没排好版，不要采信。
                        if (c > 0 && c <= 20) return c;
                    }
                }

                // 兜底：GroupBox 默认边框 = 3px 外框 + 3px 内缩，左右各一份。
                return _groupChromeFallback;
            }
            set { _groupChromeFallback = value; }
        }

        /// <summary>GroupChrome 兜底值，首次量到真实值后写回。</summary>
        private int _groupChromeFallback = 8;

        /// <summary>
        /// 决定"哪些并排复选框需要断行"，并据此把分组撑高。只算不落地。
        ///
        /// ── 为什么要"纯计算"，不能边排边改 ──────────────────────────
        ///
        /// 断行判定需要【最终渲染宽度】，而最终渲染宽度又依赖分组宽度，
        /// 分组宽度又依赖"要不要出滚动条"，滚动条又依赖内容高度，
        /// 内容高度又依赖断行 —— 这是一个环。
        ///
        /// 破解办法是"迭代到不动点"：反复跑【排布 → 判定 → 撑高】，
        /// 直到"断行集合"不再变化（最多 4 轮，实际 2 轮就稳定）。
        /// 这是排版引擎里的标准做法（等价于浏览器 layout 的 multi-pass），
        /// 比"猜一个顺序"稳得多。
        ///
        /// 每一轮都会先恢复基准 Y，所以结果只跟"当前窗口尺寸"有关，与历史无关
        /// （幂等），窗口来回拉不会一次比一次歪。
        /// </summary>
        private void MeasureAndWrap()
        {
            // 先恢复基准高度，保证本轮从"设计稿状态"出发
            RestoreBaseGroupHeights();
            ApplyRowWraps();      // 会先清空所有 Top → 基准

            // ★★ 断行判定必须用"窗口按新尺寸排好版之后"的分组宽度。★★
            //
            //   踩过的坑：一开始直接把 MeasureAndWrap() 放在 LayoutResponsive 最前面，
            //   这时 _groupHost / 各分组的宽度还是【上一次】的尺寸，
            //   于是算出来的真实宽度是旧的 —— 第一次布局时甚至是 0，
            //   结果 Grow 复选框宽度算成 20，断行判定全错、
            //   右列的"%""缩放角区"也被挤成 20px（截图里那一团糟）。
            //
            //   所以先把分组宽度同步到"本次尺寸"，再做测量。
            SyncGroupWidthForMeasure();

            bool[] last = new bool[_innerLayouts.Count];

            for (int round = 0; round < 4; round++)
            {
                // ① 按当前状态算一遍尺寸（X 方向），这样 Grow 控件才有真实宽度
                LayoutInner();

                // ② 用真实宽度判定断行
                ComputeRowWraps();

                // ③ 把断行控件的 Y 落地（含它下面所有管控件的下移）
                ApplyRowWraps();

                // ③b 处理"独占整行也放不下"的超长文本：折行 + 继续下推
                SyncWrappedHeights();

                // ④ 该分组高度重算
                GrowGroupsForWraps();

                // ⑤ 断行集合没变 → 收敛，收工
                bool changed = false;
                for (int i = 0; i < _innerLayouts.Count; i++)
                {
                    if (last[i] != _innerLayouts[i].WrapsToNextRow) { changed = true; break; }
                }
                for (int i = 0; i < _innerLayouts.Count; i++)
                    last[i] = _innerLayouts[i].WrapsToNextRow;

                if (!changed) break;
            }
        }

        /// <summary>
        /// 把 _groupHost 及各分组的宽度同步到"本次将要使用的尺寸"，
        /// 供 MeasureAndWrap() 测量用。
        ///
        /// ★★ 这里【必须】先把列数定下来，再算 avail。★★
        ///
        ///   踩过的坑：MeasureAndWrap 里读的是【上一次】的 CurrentColumns。
        ///   第一次布局前 CurrentColumns 是 1，于是 `RightReserve()` 返回 4、
        ///   `ContentHeightExceeds()` 按 6 行堆叠算 → 判定"一定出滚动条" → 多扣 17px。
        ///   算出来的分组宽度比真实值小一截，Shift 类控件（"缩放角区"那一组）
        ///   按这个假宽度反推右边界，直接被压成 20px —— 就是截图里那一团糟。
        ///
        ///   正确顺序：Relayout() 先把列数 / 列宽定死，再谈其他。
        ///   所以这个方法直接照抄正式排布的 avail 公式，两边绝不能有两套算法。
        /// </summary>
        private void SyncGroupWidthForMeasure()
        {
            try
            {
                int client = _scrollHost.ClientSize.Width;
                if (client <= 0) client = ClientSize.Width;
                if (client <= 0) return;

                // ① 先按"列数取决于原始宽度"这个事实定下列数 ——
                //    列数只跟 client 有关，跟滚动条、跟断行都无关，可以先算。
                int probe = client - ScrollEdgeMargin * 2;
                if (probe < GroupWidth) probe = GroupWidth;

                // ★★ 这里【不要】调 _groupHost.Relayout(probe)！★★
                //
                //   踩过的坑：为了"先把列数定下来"就调了一次 Relayout(probe)，
                //   probe 是按"没扣滚动条"的宽度算的，比真实 avail 大 17px。
                //   Relayout 会把分组宽度真的落到 probe 上（单列模式下就是 556），
                //   然后下面再调 Relayout(avail) 收窄。
                //   但 TableLayoutPanel 在"列数没变"时【不会重建结构】，
                //   于是各分组的 Width 停在上一次的 556 —— 而 ApplyOne 读的
                //   `c.Parent.ClientSize.Width` 正好是 556，
                //   Shift 类控件（"缩放角区"%那一组）按假宽度反推右边界，
                //   被压成 20px（截图里那三个挤成一团的就是它们）。
                //
                //   正确做法：列数【只读不写】——看 probe 落在哪个档，
                //   直接算 avail，一次 Relayout 到位。绝不中途落地中间状态。
                int colsWant = probe >= (int)Math.Round(ResponsiveGroupPanel.TwoColumnThreshold * CurrentUiScale) ? 2 : 1;

                // ② 列数定了，RightReserve() / ContentHeightExceeds() 才有意义
                int bar = SystemInformation.VerticalScrollBarWidth;
                int reserve = colsWant >= 2 ? bar + 4 : 4;

                int avail = client - ScrollEdgeMargin * 2 - reserve;
                if (avail < GroupWidth) avail = GroupWidth;

                _groupHost.Width = avail;
                _groupHost.Relayout(avail);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>把分组高度恢复成设计稿原值（BuildUi 里 `Group(..., 高度)` 给的那个）。</summary>
        private void RestoreBaseGroupHeights()
        {
            for (int i = 0; i < _groupHost.Controls.Count; i++)
            {
                var gb = _groupHost.Controls[i] as GroupBox;
                if (gb == null) continue;
                if (i < _baseGroupHeights.Length && _baseGroupHeights[i] > 0)
                    gb.Height = _baseGroupHeights[i];
            }
        }

        /// <summary>
        /// 各分组的"设计稿高度"。
        ///
        /// ★ 断行会撑高分组，而撑高是"只增不减"的 —— 于是必须有地方记住原值，
        ///   否则窗口从宽拉窄之后分组还停在撑高状态，下面会多出一块空白。
        ///   第一次 LayoutResponsive 时把当前高度记下来即可。
        /// </summary>
        private int[] _baseGroupHeights = new int[0];

        /// <summary>第一次布局时把各组的设计稿高度记下来（之后不再更新，作为基准）。</summary>
        private void CaptureBaseGroupHeights()
        {
            if (_baseGroupHeights.Length > 0) return;
            if (_groupHost == null || _groupHost.Controls.Count == 0) return;

            _baseGroupHeights = new int[_groupHost.Controls.Count];
            for (int i = 0; i < _groupHost.Controls.Count; i++)
            {
                var gb = _groupHost.Controls[i] as GroupBox;
                if (gb != null) _baseGroupHeights[i] = gb.Height;
            }
        }

        /// <summary>
        /// 内容高度是否超过可视区（用来预判纵向滚动条会不会出现）。
        ///
        /// ★★ 必须按【当前列数】分行累加，不能把所有分组高度直接加起来。★★
        ///
        ///   踩过的坑：原先写成 `sum(每个分组的 Height)`。
        ///   单列模式下这是对的（6 个分组纵向堆叠）；
        ///   但两列模式下分组是 3 行 × 2 列，真实内容高度只有 3 行，
        ///   却按 6 行算了 —— 于是"预判一定会出滚动条"永远成立，
        ///   avail 被多扣了 17px，两列模式下右列刚好被切掉一条，
        ///   表现就是"重新录（缺制）""%"被压成 20px 这种莫名其妙的问题。
        ///
        ///   正确算法：两列时每行取两列中较高的那个，再把行高加起来。
        /// </summary>
        private bool ContentHeightExceeds(int clientWidth)
        {
            if (_groupHost == null || _groupHost.Controls.Count == 0) return false;

            int cols = _groupHost.CurrentColumns >= 2 ? 2 : 1;
            int rows = (int)Math.Ceiling(_groupHost.Controls.Count / (double)cols);

            int total = 0;
            for (int r = 0; r < rows; r++)
            {
                int rowH = 0;
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    if (idx >= _groupHost.Controls.Count) break;
                    int h = _groupHost.Controls[idx].Height;
                    if (h > rowH) rowH = h;
                }
                total += rowH + 8;
            }
            total += 24;

            return total > _scrollHost.ClientSize.Height;
        }

        /// <summary>
        /// 按当前窗口宽度做一次响应式排布：决定分组用 1 列还是 2 列、
        /// 把底部按钮摆到右下角、刷新滚动范围。
        ///
        /// 这就是"让拉宽真正有意义"的关键：宽度够了两列排开，
        /// 屏幕上同时看到的信息量翻倍，而不是右边空一大片。
        /// </summary>
        private void LayoutResponsive()
        {
            if (_groupHost == null) return;

            try
            {
                // ★ 第一步永远是这个：把布局基准搬到"当前 DPI 的真实坐标系"。
                //   第一次显示时 WinForms 刚做完自动缩放（96→125%），
                //   而基准值还是构造函数里采的 96 DPI 设计稿值 ——
                //   不重新采样的话，下面所有 BaseTop 算术都会把控件"缩"回去，
                //   几行累积就叠在一起（用户看到的"选项全部重叠"）。
                //   详见 RebaseAfterAutoScale() 上方的完整说明。
                RebaseAfterAutoScale();

                // 第一次进来先把设计稿高度记下来 —— 断行撑高是不可逆的，
                // 必须有个基准才能"拉窄之后缩回去"。
                CaptureBaseGroupHeights();

                // ══ 第 0 阶段：断行判定 + 撑高分组 ══════════════════════════
                //
                // ★★ 顺序是关键，这里踩过一个大坑，务必看懂 ★★
                //
                //   断行（"并排两个复选框放不下就换行"）会改变分组的高度，
                //   而分组高度又参与 _groupHost 的总高、进而影响"要不要扣滚动条"。
                //   所以断行必须在【算分组高度之前】完成。
                //
                //   早先把它放在 LayoutInner() 之后，就出现了非常诡异的现象：
                //   断行把 g4 撑高了 → 总高变了 → 滚动条判断变了 → avail 变了 →
                //   但此刻列宽已经定完，于是右列里贴右边的"%""缩放角区"被挤成 20px，
                //   整个"拖动与吸附"分组的排版全部崩掉（截图里一团糟就是它）。
                //
                //   正确做法：断行的本质是"先量一遍内容，再决定换不换行"，
                //   所以要在正式排布【之前】跑一次"预排布"来拿到真实宽度。
                //   这里用 MeasureAndWrap() 做这件事，它只算不落地（会还原现场）。
                MeasureAndWrap();

                int client = _scrollHost.ClientSize.Width;
                if (client <= 0) client = ClientSize.Width;

                // 扣掉纵向滚动条自身的宽度，否则两列模式会刚好压出横向滚动条。
                // 这里用"预判"：内容高度明显超过窗口时滚动条一定会出现，提前扣掉，
                // 否则第一轮排布会算宽、压出横向滚动条、第二轮才收窄，用户看到闪烁。
                int scrollbar = SystemInformation.VerticalScrollBarWidth;
                if (_scrollHost.VerticalScroll.Visible || ContentHeightExceeds(client)) client -= scrollbar;

                // ★ 这里的公式决定了"分组到底能有多宽"。
                //   必须只减一次边距，不能又减边距又减滚动条 ——
                //   早先写成 client - 24，而 client 已经扣过滚动条（约 17），
                //   等于把滚动条宽度重复扣了两遍，算出来的 avail 比实际可用宽小了 14px。
                //   单列时看不出来（反正有 GroupWidth 兜底），
                //   但两列时右列正好因此被切掉尾巴 —— 就是用户看到的
                //   "重新录（缺 制）""缩放 26 %（缺 角区）"。
                int avail = client - ScrollEdgeMargin * 2 - RightReserve();
                if (avail < GroupWidth) avail = GroupWidth;

                _groupHost.Width = avail;
                _groupHost.Relayout(avail);

                // 分组变宽之后，把里面"该拉长的控件"也拉长，
                // 否则分组是宽了、内容还是挤在左边，等于没利用宽度。
                //
                // ══ ★★★ 顺序在这里被踩过一个大坑：整屏选项重叠 ★★★ ══
                //
                //   原来这里只调了 LayoutInner()（只算 X 方向），
                //   以为接着在 LayoutResponsive 里算分组高度就完事了。
                //   但实际发生的是：
                //     ① Relayout() 内部 RebuildStructure() → Controls.Clear() + 重新 Add，
                //        TableLayoutPanel 在 ResumeLayout(true) 时会把每个分组"摆"一遍，
                //        分组的 Height 被 AutoSize 行样式按 GetPreferredSize() 重算 ——
                //        GroupBox 的 PreferredSize 不考虑子控件，于是高度回到设计稿值；
                //     ② 而组内控件的 Y 是 MeasureAndWrap() 那一轮算的（含断行下推），
                //        已经跑到 T=330 这种位置，远远超过设计稿高度；
                //     ③ 结果就是"控件画在框外、跟下一个分组的控件叠在一起"。
                //
                //   正确做法：在【所有会改变分组尺寸的动作都做完之后】，
                //   再把断行下推 + 分组撑高重放一遍，最后才去算 _groupHost 的总高。
                //   也就是这里的五步，顺序不能换：
                //     LayoutInner  →  ApplyRowWraps  →  SyncWrappedHeights
                //                  →  GrowGroupsForWraps  →  算总高
                LayoutInner();
                ApplyRowWraps();
                SyncWrappedHeightsPopulateOnly();
                GrowGroupsForWraps();

                // 分组高度改完必须让 TableLayoutPanel 重新认一遍，
                // 否则 AutoSize 行样式会把它按旧值摆回去。
                _groupHost.PerformLayout();
                for (int i = 0; i < _groupHost.Controls.Count; i++)
                {
                    var gg = _groupHost.Controls[i] as GroupBox;
                    if (gg != null) gg.PerformLayout();
                }

                // 没有 AutoSize，必须自己算高度：取所有分组底边（含间距）的最大值。
                // 高度算小了底部的分组会被切掉，算大了滚动区多出一段空白。
                int contentBottom = 12;
                for (int i = 0; i < _groupHost.Controls.Count; i++)
                {
                    var g = _groupHost.Controls[i];
                    int b = g.Top + g.Height + g.Margin.Bottom;
                    if (b > contentBottom) contentBottom = b;
                }
                _groupHost.Height = contentBottom + 4;

                // 底部按钮靠右依次排开
                if (_btnCancelRef != null && _btnSaveRef != null && _btnDefaultsRef != null)
                {
                    // ★ 全部走 S()：按钮尺寸、间距、边距都要跟着 DPI 缩放，
                    //   否则 200% 屏上按钮还是 118×30 物理像素，看着会比周围小一圈。
                    int padRight = S(16), gap = S(12);
                    int right = _pager.ClientSize.Width - padRight;
                    int y = (_pager.ClientSize.Height - _btnSaveRef.Height) / 2;

                    _btnCancelRef.Location = new Point(right - _btnCancelRef.Width, y);
                    right -= _btnCancelRef.Width + gap;

                    _btnSaveRef.Location = new Point(right - _btnSaveRef.Width, y);
                    right -= _btnSaveRef.Width + gap;

                    _btnDefaultsRef.Location = new Point(Math.Max(S(8), right - _btnDefaultsRef.Width), y);

                    // 状态文字只占到"恢复默认值"左边，避免压到按钮上
                    int statusW = Math.Max(S(120), _btnDefaultsRef.Left - S(24));
                    _lblStatus.Size = new Size(statusW, S(22));
                    _lblStatus.Location = new Point(S(16), y);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // ★ 句柄建好之后，用"窗口真正所在的这块显示器"的 DPI 复核一次 ★
            //
            //   句柄创建之前只能拿主屏 DPI 顶着，如果设置窗口最终落在另一块
            //   缩放比不同的显示器上，之前那一次缩放就是错的 ——
            //   界面会整体偏大或偏小、字与框的比例不对（观感就是"分辨率不对"）。
            //   这里发现不一致就按真实 DPI 重建一遍（RescaleForDpi 自带幂等保护）。
            try
            {
                int realDpi = Native.DpiOfWindow(Handle);
                float real = realDpi / 96f;
                if (realDpi > 0 && Math.Abs(real - _uiScale) > 0.001f)
                {
                    Log.Info("设置窗口：句柄建立后复核 DPI，缓存=" + _uiScale.ToString("0.###")
                        + " 实际=" + real.ToString("0.###") + "，按实际值重建");
                    RescaleForDpi(realDpi);
                }
            }
            catch (Exception ex) { Log.Error(ex); }

            // ★ Win11 的系统圆角窗口 ★
            //   交给 DWM 在合成阶段做，圆角处带抗锯齿、并和系统投影配套；
            //   自己 SetWindowRgn 裁会得到锯齿边、还会把系统投影一起裁掉。
            //   Win10 上这个属性不存在，调用返回非 0，静默忽略即可。
            bool rounded = Native.SetWindowRoundedCorners(Handle);
            Log.Info("设置窗口圆角（DWM） => " + rounded);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            // ★★ 拖动窗口边缘改大小时必须【节流】★★
            //
            //   原来这里是直接 LayoutResponsive()。而那个函数一次要做：
            //   RebaseAfterAutoScale → CaptureBaseGroupHeights → MeasureAndWrap
            //   （一整轮预排布）→ _groupHost.Relayout（内部 Controls.Clear() +
            //   逐个 re-Add + ResumeLayout(true)，这是最贵的一步）
            //   → LayoutInner → ApplyRowWraps → SyncWrappedHeights → GrowGroupsForWraps。
            //
            //   拖边框时 WM_SIZE 每秒能来 100~200 次，每次都跑完整套 = 明显卡顿。
            //   实测"拉窗口一顿一顿"就是这个。
            //
            //   做法：交互式改大小期间只"记一个待办"，由 30ms 的定时器合并执行；
            //   松开鼠标（WM_EXITSIZEMOVE）时立刻补一次最终布局，保证落点精确。
            //   非交互式改大小（代码 SetBounds / 最大化）仍走同步路径，行为不变。
            if (_inSizeMove)
            {
                // ★★ 第四十轮修正：这里原来是"防抖"，直接导致用户报的"高频晃动" ★★
                //
                //   旧代码是 `_relayoutTimer.Stop(); _relayoutTimer.Start();` ——
                //   每来一次 WM_SIZE 就把 30ms 的倒计时【清零重来】。
                //   而拖边框时 WM_SIZE 每 8~16ms 就来一次，倒计时永远走不完，
                //   于是【整个拖动过程一次都不重排】，而用户手一停就立刻补一次。
                //   结果：
                //     · 拖动中分组宽度停在旧值 → 窗口变宽、内容不跟 → 看着像溢出；
                //     · 手一停，积压的那次重排突然执行 → 内容"啪"地跳一下；
                //     · 再拖又停 → 又跳一下。这就是"高频晃动 + 溢出原有框架"。
                //
                //   正确语义是【节流】：拖动期间按固定节拍持续重排（内容平滑跟随），
                //   松手时再做一次精确的最终重排（WM_EXITSIZEMOVE 里）。
                _pendingRelayout = true;
                if (!_relayoutTimer.Enabled) _relayoutTimer.Start();
                return;
            }

            LayoutResponsive();
        }

        /// <summary>交互式改大小期间"欠一次重排"的标记（节流用）。</summary>
        private bool _pendingRelayout;

        /// <summary>
        /// 重排一次，但把过程中的中间态藏起来。
        ///
        /// 重排是分好几轮收敛的（列数 → 分组宽 → 断行 → 折行 → 分组高），
        /// 中间任何一轮的形态都不该被用户看到 —— 那正是"抖动"的观感来源。
        /// 关掉重绘、排完再打开，屏幕上一帧只有最终状态。
        /// </summary>
        private void RelayoutFlickerFree()
        {
            bool frozen = false;
            try
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    Native.SendMessageW(Handle, Native.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
                    frozen = true;
                }
            }
            catch { }

            try
            {
                LayoutResponsive();
            }
            finally
            {
                if (frozen)
                {
                    try { Native.SendMessageW(Handle, Native.WM_SETREDRAW, new IntPtr(1), IntPtr.Zero); } catch { }
                    try { Invalidate(true); Update(); } catch { }
                }
            }
        }

        /// <summary>交互式改大小的节流定时器：停手后补一次布局。</summary>
        private readonly System.Windows.Forms.Timer _relayoutTimer =
            new System.Windows.Forms.Timer();

        /// <summary>是否正处于"用户拖边框"过程中（WM_ENTERSIZEMOVE ~ WM_EXITSIZEMOVE）。</summary>
        private bool _inSizeMove;

        protected override void WndProc(ref Message m)
        {
            const int WM_ENTERSIZEMOVE = 0x0231;
            const int WM_EXITSIZEMOVE = 0x0232;
            const int WM_DPICHANGED = 0x02E0;

            if (m.Msg == WM_ENTERSIZEMOVE)
            {
                _inSizeMove = true;
            }
            else if (m.Msg == WM_EXITSIZEMOVE)
            {
                _inSizeMove = false;
                _relayoutTimer.Stop();
                _pendingRelayout = false;
                // 松手后立刻做一次"最终布局"，把节流期间的欠账一次结清，
                // 保证落点精确（中间那些节流帧只是让内容平滑跟随）。
                RelayoutFlickerFree();
            }
            else if (m.Msg == WM_DPICHANGED)
            {
                // ★ 窗口被拖到另一块 DPI 不同的显示器（或系统改了缩放比例）★
                //   wParam 低 16 位 = 新的 DPI。窗口本身的尺寸由系统按 lParam
                //   给的建议矩形调整（WinForms 已经处理），我们只需要把【内容】
                //   按新分辨率重算一遍 —— 这就是"按显示器实时分辨率渲染"。
                try
                {
                    int newDpi = (int)(m.WParam.ToInt64() & 0xFFFF);
                    RescaleForDpi(newDpi);
                }
                catch (Exception ex) { Log.Error(ex); }
            }

            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutResponsive();

            // ★ 内容装得下时把窗口收到"刚好装完"的高度，消除底部那片空白。
            //   只在第一次显示时做一次；用户随后手动拉大拉小都随他，
            //   不会被程序改回去（见 FitHeightToContent 的说明）。
            FitHeightToContent();
        }

        /// <summary>把配置里记住的窗口尺寸套回来（0 表示没存过，保持内置默认）。</summary>
        private void ApplySavedSize()
        {
            try
            {
                int w = _edit.SettingsWidth;
                int h = _edit.SettingsHeight;
                if (w <= 0 || h <= 0) return;

                // 设的是【外框】尺寸：MinimumSize 约束的也是外框尺寸，两边口径必须一致，
                // 否则会出现在某些 DPI 下"恢复了但比预期小一圈"的诡异现象。
                Size = new Size(w, h);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        /// <summary>
        /// 记下当前窗口尺寸。在"保存并生效"时调用，不单独落盘 ——
        /// 用户点了"取消"就不该留下任何痕迹，包括窗口大小。
        /// </summary>
        private void CaptureSizeInto(AppConfig target)
        {
            try
            {
                // 最大化 / 最小化时要记的是还原尺寸，否则存下整屏尺寸、
                // 下次打开就是一个占满屏幕的窗口（用户会觉得"它记得的是我最大化时的样子"）。
                var b = RestoreBounds;
                if (b.Width > 0 && b.Height > 0)
                {
                    target.SettingsWidth = b.Width;
                    target.SettingsHeight = b.Height;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        /// <summary>
        /// 建一个分组框。
        ///
        /// resize = true（默认）时加 Top|Left|Right 锚定：窗口拉宽，分组跟着一起变宽。
        /// 这是"设置窗口可调整大小"能真正好用的关键 —— 只把 FormBorderStyle 改成 Sizable
        /// 而不加锚定，拉宽窗口只会得到一片空白，控件还是挤在左边 536px 里。
        /// </summary>
        private static GroupBox Group(string text, int x, int y, int w, int h, bool resize = true)
        {
            // ★ 用 CardGroup 而不是原生 GroupBox：只换画法（圆角 + 细描边 + 浮动标题），
            //   布局坐标口径完全不变 —— 详见 CardGroup 的类注释。
            var g = new CardGroup();
            g.Text = text;
            g.Location = new Point(x, y);
            g.Size = new Size(w, h);
            g.ForeColor = Design.TextPrimary;
            if (resize) g.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            else g.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            return g;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  ★ 视觉现代化：设计令牌 + 卡片式分组 + 扁平按钮
        // ═══════════════════════════════════════════════════════════════════
        //
        // 之前设置窗口是 WinForms 默认外观：灰色 GroupBox 凹边框、
        // 立体凸起的系统按钮、黑色标签。功能上没问题，但看着像 2008 年的工具，
        // 跟应用本身的"薄荷青/柔光环"那种现代配色完全不搭 —— 用户明确提了要现代化。
        //
        // 这里做三件事，都不改动任何布局逻辑：
        //   ① Design：把散落在各处的颜色收敛成一组令牌（Design.*），
        //      以后换主题只改这一处，不会再散落成"到处都是 FromArgb"。
        //   ② CardGroup：自绘的卡片式分组框，替代 GroupBox 的系统凹边框。
        //      视觉上是"白底 + 圆角 + 极淡描边"，标题浮在卡片左上角。
        //   ③ FlatButton：自绘扁平按钮，主次分明（保存=实心强调色，取消=描边）。
        //
        // 为什么要自绘而不是改系统控件属性：
        //   FlatStyle.Flat 能去掉立体感，但边框颜色不可控、圆角做不出来、
        //   悬停态还得另外接事件；自绘这几样一次到位，代价也就几十行。
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 界面设计令牌。所有颜色/圆角/间距都从这里取，改主题只改这一处。
        ///
        /// 取值偏"中性灰 + 靛蓝强调"，理由：
        ///   · 中性底色不抢内容的注意力（这是个设置窗口，不是宣传页）；
        ///   · 唯一的高饱和色留给"保存"按钮，让主动作一眼可辨；
        ///   · 每张卡片左侧一道 3px 强调色竖条 —— 这是把六个分组区分开的
        ///     最省事也最有效的手段，比加深描边更醒目、又不吵。
        ///
        /// ★ 所有"像素"值都是【设计稿像素】，高 DPI 下由 S() 统一放大。
        ///   漏掉缩放的直接后果就是"200% 屏上圆角小得几乎看不见、阴影细得像条线" ——
        ///   上一版就是这么丢效果的：功能全对，但看着跟改之前没区别。
        /// </summary>
        /// <summary>
        /// 图标字体 + 字形表（第四十二轮新增）。
        ///
        /// ★ 为什么这是"精致化"里性价比最高的一件事 ★
        ///   设置页此前【一个图标都没有】：分区标题是纯文字、左侧导航是纯文字、
        ///   复选框是方框 + 勾。而 Win11 的观感有一半来自图标体系 ——
        ///   没有图标，无论圆角和投影做得多细，都会显得"像一张表单"。
        ///
        /// 字体选择：Segoe Fluent Icons（Win11 自带）→ 退回 Segoe MDL2 Assets（Win10 自带）。
        ///   两者码位兼容，所以字形常量只需要一套。
        ///   两个都没有时 Family 返回 null，调用方【跳过画图标】而不是画成豆腐块。
        /// </summary>
        private static class Icons
        {
            private static FontFamily _family;
            private static bool _probed;

            public static FontFamily Family
            {
                get
                {
                    if (_probed) return _family;
                    _probed = true;
                    string[] want = { "Segoe Fluent Icons", "Segoe MDL2 Assets" };
                    try
                    {
                        var all = FontFamily.Families;
                        for (int w = 0; w < want.Length; w++)
                        {
                            for (int i = 0; i < all.Length; i++)
                            {
                                if (string.Equals(all[i].Name, want[w], StringComparison.OrdinalIgnoreCase))
                                {
                                    _family = all[i];
                                    return _family;
                                }
                            }
                        }
                    }
                    catch { }
                    return null;
                }
            }

            public static bool Available { get { return Family != null; } }

            /// <summary>按【像素】大小取图标字体；拿不到字体族时返回 null。</summary>
            public static Font At(float px)
            {
                var fam = Family;
                if (fam == null) return null;
                try { return new Font(fam, px, FontStyle.Regular, GraphicsUnit.Pixel); }
                catch { return null; }
            }
        }

        /// <summary>
        /// 用到的图标字形（Segoe Fluent Icons / Segoe MDL2 Assets 通用码位）。
        ///
        /// ★ 每个字形都由自检 T65 实测"渲染出来有没有墨水" ★
        ///   图标字体缺字形时是【画成空白】而不是画成方框，肉眼很难发现，
        ///   所以必须用像素级断言把它钉住 —— 换字体、改字号都会立刻暴露。
        /// </summary>
        internal static class Glyph
        {
            public const string Keyboard = "\uE765";   // 键盘
            public const string Move = "\uE7C2";       // 移动 / 拖拽
            public const string Color = "\uE790";      // 颜色 / 外观
            public const string Speed = "\uE9D9";      // 速度 / 流畅度
            public const string Shield = "\uE72E";     // 权限 / 安全
            public const string Chevron = "\uE76C";    // 右箭头（导航指示）
            public const string Help = "\uE897";       // 圆圈问号（帮助）
            public const string Info = "\uE946";       // 圆圈 i（关于 / 版本信息）
        }

        /// <summary>
        /// 分区标题 → 图标字形。
        ///
        /// 为什么按标题里的关键字映射、而不是按序号：分组顺序调整过好几轮，
        /// 用序号会在下次调整时悄悄错位（图标和标题对不上，很难发现）。
        /// 关键字命中更稳，最后兜底返回一个通用箭头。
        /// </summary>
        internal static string GlyphForSection(string title)
        {
            if (string.IsNullOrEmpty(title)) return Glyph.Move;
            if (title.Contains("触发")) return Glyph.Keyboard;
            if (title.Contains("鼠标键") || title.Contains("动作")) return Glyph.Move;
            if (title.Contains("视觉")) return Glyph.Color;
            if (title.Contains("拖动")) return Glyph.Move;
            if (title.Contains("流畅")) return Glyph.Speed;
            if (title.Contains("启动") || title.Contains("权限")) return Glyph.Shield;
            if (title.Contains("帮助")) return Glyph.Help;
            if (title.Contains("关于")) return Glyph.Info;
            return Glyph.Chevron;
        }

        private static class Design
        {
            /// <summary>
            /// 窗口 / 滚动区底色：带一点冷灰，和卡片白拉开层次。
            ///
            /// ★ 第三十九轮（Win11 视觉）★ 从 #F4F5F7 再压深一档到 #F0F2F5：
            ///   白色卡片浮在更深的底上，"层次"才真正立得住 ——
            ///   底越接近卡片白，投影和描边就越像"糊了一层灰"，整片界面显得平。
            /// </summary>
            public static readonly Color Canvas = Color.FromArgb(240, 242, 245);

            /// <summary>卡片底色：纯白，和底色形成层次。</summary>
            public static readonly Color Surface = Color.White;

            /// <summary>
            /// 卡片渐变的顶 / 底两色（第四十二轮）。
            /// 只有 2~3 个色阶的差 —— 目的是"面被光照到"，不是"彩色卡片"。
            /// </summary>
            public static readonly Color SurfaceTop = Color.White;
            public static readonly Color SurfaceBottom = Color.FromArgb(251, 252, 254);

            /// <summary>侧栏底色：比卡片白略暗一点点，让导航区和内容区分开层次。</summary>
            public static readonly Color SidebarSurface = Color.FromArgb(250, 251, 252);

            /// <summary>
            /// 卡片描边：从 #E4E7EE 加深到 #DCE0E8。
            ///
            /// 设计稿值对应的"视觉重量"在 200% DPI 屏上只有 96 DPI 屏的约一半
            /// （同样 1 个设计像素在 96 DPI = 1 物理像素、200% DPI = 2 物理像素，
            /// 但同样物理长度的线在 200% DPI 下视觉上显得更"细"——这是同屏尺寸
            /// 下"线宽不变、物理像素翻倍"带来的副效应），所以浅描边在 200% 屏上
            /// 几乎看不见，整张卡片失去边界、看上去"糊"在一起。
            /// 把描边色加深 ~10% 后，200% 屏上的可见度正好等于 96 DPI 屏上的 #E4E7EE，
            /// 但因为画在同一个 design grid 上、且配合 SmoothingMode.HighQuality + Half
            /// 像素对齐，边缘依然干净不糊。
            /// </summary>
            public static readonly Color Stroke = Color.FromArgb(220, 224, 232);

            /// <summary>卡片圆角半径（设计稿像素）。14 比 12 更柔，接近现代设计语言。</summary>
            public const int Radius = 14;

            /// <summary>投影颜色：透明度很低，只在卡片下缘露出一线。</summary>
            public static readonly Color Shadow = Color.FromArgb(22, 28, 45);

            /// <summary>投影垂直偏移（设计稿像素）。</summary>
            public const int ShadowOffset = 1;

            /// <summary>投影扩散（设计稿像素）。</summary>
            public const int ShadowSpread = 5;

            /// <summary>卡片左侧强调竖条的宽度（设计稿像素）。</summary>
            public const int AccentBarWidth = 4;

            /// <summary>主文字：接近黑但不是纯黑，纯黑在白底上会有点刺。</summary>
            public static readonly Color TextPrimary = Color.FromArgb(24, 26, 33);

            /// <summary>次要文字（说明、提示）：中灰，弱化但不消失。</summary>
            public static readonly Color TextSecondary = Color.FromArgb(122, 128, 142);

            /// <summary>
            /// 强调色：偏紫的靛蓝。
            ///
            /// ★ 刻意不用"正蓝" ★
            ///   用户明确说过"不要用蓝色、一点都不现代化"——
            ///   Windows 默认强调色那种蓝在自绘界面里显得很廉价。
            ///   这里往紫挪一档（色相 243°），既保持了"主按钮醒目"，
            ///   又和窗口外框的紫罗兰/薄荷青调性一致。
            /// </summary>
            public static readonly Color Accent = Color.FromArgb(94, 84, 222);

            /// <summary>强调色按下时的加深版。</summary>
            public static readonly Color AccentPressed = Color.FromArgb(76, 66, 198);

            /// <summary>强调色的极淡版，用作选中项 / 悬停底色。</summary>
            public static readonly Color AccentSoft = Color.FromArgb(238, 237, 253);

            /// <summary>输入控件（下拉框 / 输入框）的描边。</summary>
            public static readonly Color InputStroke = Color.FromArgb(205, 210, 222);

            /// <summary>输入控件获得焦点时的描边（强调色）。</summary>
            public static readonly Color InputStrokeFocus = Color.FromArgb(94, 84, 222);

            /// <summary>
            /// 输入控件的圆角（设计稿像素）。
            /// 刻意比卡片(12)小一档、和按钮(6)一致 —— 控件尺寸小，
            /// 圆角跟卡片一样大反而显得"鼓"，同一个语言里要有大小层次。
            /// </summary>
            public const int FieldRadius = 6;

            /// <summary>说明文字（卡片里的灰色小字）。</summary>
            public static readonly Color Hint = Color.FromArgb(113, 113, 122);

            /// <summary>警告文字（"允许拖动全屏"那条橙色）。</summary>
            public static readonly Color WarnText = Color.FromArgb(161, 98, 7);

            /// <summary>只读输入框的底色：比白略暗，一眼看出"这里不能改"。</summary>
            public static readonly Color ReadOnlyFill = Color.FromArgb(247, 248, 251);

            /// <summary>危险提示（"允许拖动全屏"那条橙色警告）。</summary>
            public static readonly Color Warning = Color.FromArgb(190, 106, 12);

            /// <summary>成功提示（状态文字）。</summary>
            public static readonly Color Success = Color.FromArgb(24, 128, 78);

            /// <summary>错误提示。</summary>
            public static readonly Color Danger = Color.FromArgb(196, 40, 40);
        }

        /// <summary>
        /// 把 Graphics 的渲染质量一口气拉满。
        ///
        /// 用法：所有自绘控件的 OnPaint 第一句 `HighQuality.Apply(e.Graphics)`，
        /// 后面再画什么都是高清版。
        ///
        /// 为什么需要这么个 helper：
        ///   WinForms 的 Graphics 默认走"系统默认"，SmoothingMode=AntiAlias、
        ///   TextRenderingHint=SystemDefault、CompositingQuality=AssumeLinear、
        ///   PixelOffsetMode=HighSpeed —— 这套组合在 200% DPI 屏上看起来"对，但糊"，
        ///   卡片描边、复选框边框、按钮描线这些 1 px 的边都带半像素糊边，看着就不够"高清"。
        ///   把四个全设成 HighQuality 之后：边缘贴合像素格、文本 ClearType 子像素清晰、
        ///   弧线平滑过渡。代价是 CPU 多算一点（4× MSAA），但设置窗口一年也点不了几次，
        ///   完全值得。
        /// </summary>
        private static class HighQuality
        {
            public static void Apply(Graphics g)
            {
                // HighQuality = 4× MSAA，曲线更平滑；比 AntiAlias 多花一点 CPU，
                // 但设置窗口几乎不重绘（只在开窗/拉窗/点状态时才 OnPaint），
                // 这点开销完全可以忽略 —— 换来的是肉眼能看出的"边缘更干净"。
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                // HighQuality 比 AssumeLinear 多算一层 alpha 预乘，叠透明边（比如阴影）时不偏色。
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                // 把 1px 描边/文字对齐到"半个像素"，高 DPI 屏上不会糊成半像素的灰带。
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            }
        }

        /// <summary>
        /// 卡片式分组框（CardGroup : GroupBox）。
        ///
        /// 为什么还派生 GroupBox 而不是直接用 Panel：
        ///   整个响应式布局（ResponsiveGroupPanel / MarkBase / ApplyRowWraps /
        ///   GrowGroupsForWraps，以及自检里一堆 `as GroupBox`）都是按 GroupBox 写的，
        ///   换掉容器类型就等于把这些全改一遍 —— 改动面大、回归风险高，
        ///   而这次要解决的是【观感】，不是结构。所以保留继承关系，只换掉"画法"。
        ///
        /// 换掉的画法有三点：
        ///   ① 边框：系统 3D 凹线 → 单色 1px 细描边 + 圆角；
        ///   ② 底色：系统灰 → 纯白卡片（和窗口底色形成层次）；
        ///   ③ 标题：系统画在边框缺口处 → 画在卡片内侧左上角，带一点字重。
        ///
        /// ★ 布局影响必须为 0：
        ///   GroupBox 的客户区原点比 Bounds 原点低（标题那行的位置），
        ///   子控件坐标一直是以这个客户区为基准的，所以这里【不能】去改
        ///   DisplayRectangle —— 一改全盘错位。只换 OnPaint，坐标口径原封不动。
        /// </summary>
        private sealed class CardGroup : GroupBox
        {
            public CardGroup()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint
                         | ControlStyles.OptimizedDoubleBuffer
                         | ControlStyles.UserPaint
                         | ControlStyles.ResizeRedraw, true);
                BackColor = Design.Surface;
                ForeColor = Design.TextPrimary;
            }

            // ── 以下三个都是"设计稿像素"，由 ScaleDesign 按 DPI 放大 ──

            /// <summary>标题字号（像素）。</summary>
            public int TitleFontPx = 13;

            /// <summary>
            /// 标题占用的高度（像素）：组内第一个控件要往下让这么多，否则会和标题撞上。
            ///
            /// 为什么需要它：GroupBox 默认的标题画在【边框缺口】上，不占客户区，
            /// 所以设计稿里第一行控件可以从 y=26 开始。而 CardGroup 把标题
            /// "浮"在卡片内侧（更现代的画法），它就【占】了客户区最上面这一条 ——
            /// 不让位的话，第一个控件（"触发键"/"拖到屏幕边缘…"）会被标题压住。
            ///
            /// 为什么不在 Group() 里统一给每个控件的 y 加一遍：
            ///   那要改几十处坐标常量，而且以后新增控件时又得记得加 ——
            ///   漏一处就是"标题压控件"，属于典型的"隐性回归"。
            ///   放在这里统一处理，新增控件自动就位。
            /// </summary>
            public int TitleInsetPx = 32;

            /// <summary>标题基线的 Y（设计稿像素 9，按 DPI 缩放后由 ScaleDesign 赋值）。</summary>
            public int TitleY = 9;

            /// <summary>圆角半径（像素）。</summary>
            public int RadiusPx = Design.Radius;

            /// <summary>投影垂直偏移（像素）。</summary>
            public int ShadowOffsetPx = Design.ShadowOffset;

            /// <summary>投影扩散（像素）。</summary>
            public int ShadowSpreadPx = Design.ShadowSpread;

            /// <summary>左侧强调竖条宽度（像素）。</summary>
            public int AccentBarPx = Design.AccentBarWidth;

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                HighQuality.Apply(g);

                int w = Width, h = Height;
                if (w <= 0 || h <= 0) return;

                // 底色先铺满整块（CardGroup 四周要留出投影的位置，
                // 不铺的话会露出 GroupBox 的默认灰底，投影周围出现一圈脏边）。
                using (var canvas = new SolidBrush(Parent != null ? Parent.BackColor : Design.Canvas))
                    g.FillRectangle(canvas, 0, 0, w, h);

                int r = Math.Max(2, RadiusPx);

                // ── ① 投影：卡片"浮"起来的关键，也是最像现代 UI 的一步 ──
                //
                //   画法：把圆角矩形往下偏移若干像素、用半透明深色描一个粗边，
                //   相当于把轮廓"晕"开。粗细就是 ShadowSpreadPx。
                //   不用 GraphicsPath 填充阴影，是因为填充会盖住卡片本体；
                //   这里先画影子、再画卡片，层次正好。
                if (ShadowSpreadPx > 0)
                {
                    // ★ 双层高度（第三十八轮 #34 现代化）★
                    //   旧实现只有"一层线性衰减的描边"，看起来就是卡片外面糊了一圈均匀的灰，
                    //   卡片是"贴"在底上的，没有高度感。
                    //   现代 UI 的浮起感来自【两层】：
                    //     ① 环境阴影 —— 范围大、极淡，负责把卡片"托"起来；
                    //     ② 接触阴影 —— 贴身、稍实，负责定住卡片轮廓。
                    //   两层叠在一起才有真实的"浮"的观感。两层的开销都只是几次 DrawPath，
                    //   卡片一年也重绘不了几次，完全可以忽略。

                    // ① 外层：大范围环境阴影
                    int wide = ShadowSpreadPx * 2;
                    var wideRect = new Rectangle(
                        1 - wide / 2, ShadowOffsetPx + wide / 3, w - 2 + wide, h - 2 + wide);
                    using (var spWide = RoundedPath(wideRect, r + wide / 2))
                    {
                        for (int i = wide; i >= 1; i--)
                        {
                            int alpha = Math.Max(2, 8 - i / 3);
                            using (var pen = new Pen(Color.FromArgb(alpha, Design.Shadow), i * 2f))
                                g.DrawPath(pen, spWide);
                        }
                    }

                    // ② 内层：贴身接触阴影
                    var tightRect = new Rectangle(1, ShadowOffsetPx, w - 2, h - 2);
                    using (var spTight = RoundedPath(tightRect, r))
                    {
                        for (int i = ShadowSpreadPx; i >= 1; i--)
                        {
                            int alpha = Math.Max(5, 24 - i * 3);
                            using (var pen = new Pen(Color.FromArgb(alpha, Design.Shadow), i * 2f))
                                g.DrawPath(pen, spTight);
                        }
                    }
                }

                // ── ② 卡片本体：极淡的顶光渐变 + 细描边 + 圆角 ──
                //
                // ★ 第四十二轮：纯白 → 极淡的垂直渐变 ★
                //   纯白卡片在浅灰底上只能靠描边和投影"贴"出层次，
                //   加一层几乎看不出的顶光（#FFFFFF → #FBFCFE）之后，
                //   卡片才像"被上方光源照到的一块面"，而不是一张白纸。
                //   幅度刻意压到 3 个色阶以内 —— 再大就会显脏。
                var path = RoundedPath(new Rectangle(0, 0, w - 1, h - 1), r);
                using (var fill = new System.Drawing.Drawing2D.LinearGradientBrush(
                           new Rectangle(0, 0, w, h),
                           Design.SurfaceTop, Design.SurfaceBottom,
                           System.Drawing.Drawing2D.LinearGradientMode.Vertical))
                    g.FillPath(fill, path);
                using (var pen = new Pen(Design.Stroke)) g.DrawPath(pen, path);

                // ── ②′ 标题下的分隔线 ──
                //   Win11 设置页的分区标题下面都有一条贯通的分隔线，
                //   作用是"把标题和正文分开"，卡片因此读起来是"标题 + 内容区"两段，
                //   而不是"一堆控件顶着一段文字"。
                //   颜色压到 alpha 20 左右，只做暗示、不做强调。
                if (!string.IsNullOrEmpty(Text) && TitleInsetPx > 8)
                {
                    int lineY = TitleInsetPx - Math.Max(2, Fields.Sc(1));
                    int x0 = AccentBarPx + 12;
                    int x1 = w - Fields.Sc(14);
                    if (x1 > x0 + Fields.Sc(20) && lineY > 0 && lineY < h - 2)
                    {
                        using (var p = new Pen(Color.FromArgb(20, Design.Shadow)))
                            g.DrawLine(p, x0, lineY, x1, lineY);
                    }
                }

                // ── ③ 左侧强调竖条 ──
                //
                //   六个分组如果只有"白卡片 + 淡边框"，视觉上还是一片白，
                //   分组与分组之间没有明确的分界（用户反馈"看起来没区别"）。
                //   左边这道 3px 靛蓝竖条相当于"章节标签"，
                //   一眼就能看出"这里是一个独立的设置区块"。
                if (AccentBarPx > 0)
                {
                    // ★ 竖条必须按卡片路径裁剪，否则是个直角矩形 ——
                    //   左上/左下会戳出圆角之外，看着像贴歪了的色块。
                    //   做法：Region(卡片路径) ∩ Region(左侧窄条)，只填交集。
                    using (var barBrush = new SolidBrush(Design.Accent))
                    using (var clip = new Region(path))
                    using (var barRegion = new Region(new Rectangle(0, 0, AccentBarPx, h)))
                    {
                        clip.Intersect(barRegion);
                        g.FillRegion(barBrush, clip);
                    }
                }
                path.Dispose();

                // ── ③′ 输入控件的现代外框 ──
                //   原生 TextBox / ComboBox / NumericUpDown 画不出圆角与自定义描边，
                //   统一由卡片在它们【底下】补一圈 —— 控件是子窗口、画在卡片之上，
                //   所以外框只露出控件 bounds 之外那 1px，正好成一条干净的边。
                Fields.PaintFor(g, this);

                // ── ④ 标题：浮在卡片内左上角（图标 + 文字）──
                //
                // ★ 第四十二轮：标题前面加一个图标 ★
                //   纯文字的六个分区标题排在一起，视觉上就是"六段文字"，
                //   加了图标之后每一段才成为一个可辨识的"章节"。
                //   图标用强调色（比标题正文淡一档），文字仍是主文字色 ——
                //   形成"图标点缀 + 文字主次"的层次，而不是一片同色的黑字。
                if (!string.IsNullOrEmpty(Text))
                {
                    float titleLeft = AccentBarPx + 12;

                    using (var iconFont = Icons.At(TitleFontPx + 1))
                    {
                        if (iconFont != null)
                        {
                            var glyph = GlyphForSection(Text);
                            float iconW = TitleFontPx + 6;
                            using (var ib = new SolidBrush(Design.Accent))
                            {
                                g.DrawString(glyph, iconFont, ib,
                                    new PointF(titleLeft, TitleY - 1));
                            }
                            titleLeft += iconW;
                        }
                    }

                    using (var f = new Font(Font.FontFamily, TitleFontPx,
                                            FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var b = new SolidBrush(Design.TextPrimary))
                    {
                        g.DrawString(Text, f, b, new PointF(titleLeft, TitleY));
                    }
                }
            }
        }

        /// <summary>生成圆角矩形路径（4 个 90° 圆弧拼起来）。CardGroup 与 FlatButton 共用。</summary>
        internal static System.Drawing.Drawing2D.GraphicsPath RoundedPath(
            Rectangle r, int radius)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>
        /// 极简补间器：把一个 0..1 的进度值在约 120ms 内平滑推向目标，并驱动宿主重绘。
        ///
        /// ★ 为什么需要它（第三十九轮 · Win11 视觉）★
        ///   Win11 里所有控件状态切换都是【带过渡】的：悬停高亮渐显、按下轻微下沉、
        ///   导航选中态滑过去。原来的实现全是 `_hover = true; Invalidate();` ——
        ///   颜色瞬变，观感很"硬"，正是"不够现代化"的一部分。
        ///   加一层 120ms 的指数逼近（每帧吃掉剩余差值的 35%）就足够顺滑，
        ///   而且不需要任何缓动曲线表。
        ///
        /// 开销：只有正在过渡的控件才持有并运行定时器，静止时定时器是停的。
        /// </summary>
        private sealed class Tween
        {
            private readonly Control _owner;
            private readonly System.Windows.Forms.Timer _timer;
            private float _value;
            private float _target;

            public Tween(Control owner)
            {
                _owner = owner;
                _timer = new System.Windows.Forms.Timer();
                _timer.Interval = 16;
                _timer.Tick += (s, e) => Step();
            }

            /// <summary>当前显示值（0..1）。</summary>
            public float Value { get { return _value; } }

            /// <summary>把目标推向 target（0 或 1）；已经在跑就只是改目标。</summary>
            public void To(float target)
            {
                if (Math.Abs(target - _target) < 0.001f) return;
                _target = target;
                if (!_timer.Enabled) _timer.Start();
            }

            /// <summary>不走动画，直接落到某个值（构造 / 复位时用）。</summary>
            public void Snap(float v)
            {
                _timer.Stop();
                _value = v;
                _target = v;
            }

            private void Step()
            {
                float d = _target - _value;
                if (Math.Abs(d) < 0.02f) { _value = _target; _timer.Stop(); }
                else _value += d * 0.35f;
                try { if (!_owner.IsDisposed) _owner.Invalidate(); } catch { }
            }
        }

        /// <summary>两色之间按 t（0..1）线性插值。</summary>
        private static Color MixColor(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// 扁平按钮：主按钮实心强调色，次按钮描边，悬停/按下有反馈。
        ///
        /// 用自绘而不是 FlatStyle.Flat 的原因：
        ///   · FlatStyle.Flat 的边框颜色固定跟随系统，画不出"描边按钮"；
        ///   · 圆角做不了；
        ///   · 悬停态要自己挂 MouseEnter/MouseLeave 再 Invalidate，很啰嗦。
        /// </summary>
        // ═══════════════════════════════════════════════════════════════════
        //  输入控件的"现代外框"
        // ═══════════════════════════════════════════════════════════════════
        //
        // 问题：TextBox / NumericUpDown / ComboBox 都是【原生控件】，外框由系统绘制
        //   —— 那是经典主题的 1px 立体边（实测描边色 #ABADB3、方角、内凹感），
        //   和外面那套"白卡片 + 圆角 + 淡描边"完全不是一个时代的东西。
        //   实测截图里这类系统灰约占 1.8% 的像素，是"界面显旧"最大的单一来源。
        //
        // 解法（不动任何布局逻辑，只换画法）：
        //   ① 控件自己去掉系统边框（BorderStyle=None，或覆写 OnPaint 不调 base）；
        //   ② 用 Region 把控件裁成圆角；
        //   ③ 由所在卡片在它【底下】补一圈圆角描边 + 底色 ——
        //      控件是子窗口、画在父窗口之上，所以"底下的外框"不会被盖住，
        //      外框画在控件 bounds 往外 1px 处，正好露出一条干净的边。
        //
        // ★ 高度必须自己兜住 ★
        //   TextBox 的 BorderStyle 从 Fixed3D 改成 None 之后高度会掉 7px
        //   （实测 38 → 31）；NumericUpDown 会掉 4px（38 → 34）。
        //   一旦放任，输入框就和底栏按钮不再等高，
        //   T42「底栏按钮与输入框同一量级」会直接变红。
        //   所以：TextBox 用 AutoSize=false + 实测高度锁死；
        //         NumericUpDown 保留 BorderStyle=FixedSingle（高度不变）再覆写 OnPaint。
        private static class Fields
        {
            private static readonly System.Collections.Generic.Dictionary<Control, int> _radius =
                new System.Collections.Generic.Dictionary<Control, int>();

            private static readonly System.Collections.Generic.Dictionary<Control, Region> _live =
                new System.Collections.Generic.Dictionary<Control, Region>();

            /// <summary>TextBox 去边框后应当保持的高度（按当前字体实测一次并缓存）。</summary>
            private static int _textBoxHeight = -1;
            private static Font _textBoxHeightFont;

            /// <summary>设计稿像素 → 当前 DPI 像素（嵌套静态类拿不到实例的 S()）。</summary>
            public static int Sc(int design)
            {
                float s = CurrentUiScale;
                if (s <= 0f) s = 1f;
                int v = (int)Math.Round(design * s);
                return v < 1 ? 1 : v;
            }

            /// <summary>
            /// 作废"按字体实测"的缓存。
            ///
            /// ★ 换 DPI 时必须调 ★
            ///   TextBox 的高度是按字体实测出来的（FontHeight + 边框），而字体用的是
            ///   Point 单位 —— 同一 9pt 字体在 100% / 200% 下物理高度差一倍。
            ///   缓存键是【字体对象引用】，而窗体字体对象在整个生命周期里是同一个，
            ///   所以不清缓存的话，换到另一块屏之后输入框会继续用旧 DPI 的高度，
            ///   和旁边已经按新系数放大的控件对不齐。
            /// </summary>
            public static void ResetCaches()
            {
                _textBoxHeight = -1;
                _textBoxHeightFont = null;
            }
            /// <summary>
            /// 把 TextBox 变成"无边框 + 锁定高度"的现代输入框。
            ///
            /// 高度为什么必须实测：TextBox 的高度是【按字体算】的，而 BorderStyle
            /// 会影响这个算法（Fixed3D/FixedSingle = FontHeight+7，None = FontHeight）。
            /// 这里用一个同字体的临时 TextBox 量出"带边框时的高度"再补回去，
            /// 100% 与 200% 两个缩放档都对得上。
            /// </summary>
            public static void MakeFlat(TextBox t, Font font, bool readOnly)
            {
                try
                {
                    Font f = font != null ? font : t.Font;
                    if (_textBoxHeight < 0 || !ReferenceEquals(_textBoxHeightFont, f))
                    {
                        using (var probe = new TextBox())
                        {
                            probe.Font = f;
                            probe.BorderStyle = BorderStyle.FixedSingle;
                            _textBoxHeight = probe.Height;
                        }
                        _textBoxHeightFont = f;
                    }

                    t.BorderStyle = BorderStyle.None;
                    t.AutoSize = false;
                    t.Height = _textBoxHeight;
                    t.BackColor = readOnly ? Design.ReadOnlyFill : Design.Surface;
                }
                catch (Exception ex) { Log.Error(ex); }
            }

            /// <summary>登记一个"外框由卡片代画"的控件，并立刻同步圆角裁剪。</summary>
            public static void Attach(Control c, int designRadius)
            {
                _radius[c] = designRadius;
                c.SizeChanged += OnSizeChanged;
                c.HandleCreated += OnSizeChanged;
                Sync(c);
            }

            private static void OnSizeChanged(object sender, EventArgs e)
            {
                Sync(sender as Control);
            }

            /// <summary>按控件当前尺寸重算圆角裁剪区域。</summary>
            public static void Sync(Control c)
            {
                if (c == null) return;
                int design;
                if (!_radius.TryGetValue(c, out design)) return;
                if (c.Width <= 0 || c.Height <= 0) return;

                Region old;
                if (_live.TryGetValue(c, out old))
                {
                    try { c.Region = null; } catch { }
                    old.Dispose();
                    _live.Remove(c);
                }

                int r = Sc(design);
                if (r > c.Height / 2) r = c.Height / 2;
                if (r > c.Width / 2) r = c.Width / 2;
                if (r < 1) return;

                var path = RoundedPath(new Rectangle(0, 0, c.Width, c.Height), r);
                try
                {
                    var reg = new Region(path);
                    c.Region = reg;
                    _live[c] = reg;
                }
                catch (Exception ex) { Log.Error(ex); }
                finally { path.Dispose(); }
            }

            /// <summary>
            /// 由 CardGroup.OnPaint 调用：给登记过的、属于这张卡片的输入控件
            /// 补一圈圆角描边 + 底色。
            /// </summary>
            public static void PaintFor(Graphics g, Control parent)
            {
                int pad = Sc(1);
                foreach (var kv in _radius)
                {
                    var c = kv.Key;
                    if (c == null || !ReferenceEquals(c.Parent, parent)) continue;
                    if (!c.Visible || c.Width <= 0 || c.Height <= 0) continue;

                    var rect = new Rectangle(c.Left - pad, c.Top - pad,
                                             c.Width + pad * 2, c.Height + pad * 2);
                    int r = Sc(kv.Value) + pad;
                    var path = RoundedPath(rect, r);
                    try
                    {
                        Color fill = c.Enabled ? c.BackColor : Design.Canvas;
                        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                        using (var p = new Pen(c.Enabled ? Design.InputStroke
                                                         : Color.FromArgb(228, 231, 238)))
                            g.DrawPath(p, path);
                    }
                    catch (Exception ex) { Log.Error(ex); }
                    finally { path.Dispose(); }
                }
            }
        }

        /// <summary>
        /// 自绘下拉框。
        ///
        /// 为什么必须自绘：原生 ComboBox 即使设成 FlatStyle.Flat，右侧那个下拉箭头
        /// 仍然是系统绘制的（方角 + 黑色实心三角 + 悬停变蓝），在卡片式界面里非常突兀。
        /// DropDownList 模式下 ComboBox 没有子编辑框，整块客户区都是它自己画的，
        /// 所以可以在 WM_PAINT 之后再盖一层自绘 —— 这是把"收起态"整块换掉的唯一办法。
        ///
        /// 布局影响为 0：高度、ItemHeight、DropDownStyle 全都不动，只改画法。
        /// </summary>
        private sealed class FlatCombo : ComboBox
        {
            /// <summary>可选的"收起态内容"绘制器（颜色下拉框用它画色块）。</summary>
            public Action<Graphics, Rectangle, object> ClosedPainter;

            private bool _hover, _open;

            public FlatCombo()
            {
                DropDownStyle = ComboBoxStyle.DropDownList;
                DrawMode = DrawMode.OwnerDrawFixed;
                FlatStyle = FlatStyle.Flat;
                BackColor = Design.Surface;
                ForeColor = Design.TextPrimary;
                SetStyle(ControlStyles.OptimizedDoubleBuffer
                         | ControlStyles.AllPaintingInWmPaint, true);
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnDropDown(EventArgs e) { _open = true; Invalidate(); base.OnDropDown(e); }
            protected override void OnDropDownClosed(EventArgs e) { _open = false; Invalidate(); base.OnDropDownClosed(e); }


            /// <summary>下拉列表里的每一项。</summary>
            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                if (e.Index < 0) { base.OnDrawItem(e); return; }

                var g = e.Graphics;
                HighQuality.Apply(g);

                bool selected = (e.State & DrawItemState.Selected) != 0;
                var r = e.Bounds;

                using (var b = new SolidBrush(selected ? Design.AccentSoft : Design.Surface))
                    g.FillRectangle(b, r);

                // 左侧一道强调条：小字列表里"当前选中"也能一眼可辨
                if (selected)
                    using (var b = new SolidBrush(Design.Accent))
                        g.FillRectangle(b, new Rectangle(r.Left, r.Top, Fields.Sc(3), r.Height));

                object item = e.Index < Items.Count ? Items[e.Index] : null;
                var inner = new Rectangle(r.Left + Fields.Sc(8), r.Top,
                                          r.Width - Fields.Sc(14), r.Height);
                if (ClosedPainter != null) ClosedPainter(g, inner, item);
                else
                    TextRenderer.DrawText(g, item != null ? item.ToString() : "", Font, inner,
                        Design.TextPrimary,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            private const int WM_PAINT_MSG = 0x000F;
            private const int WM_MOUSEWHEEL_MSG = 0x020A;

            protected override void WndProc(ref Message m)
            {
                // ★★★ 滚轮【绝不能】改变选中项（除非下拉列表已经展开）★★★
                //
                // 这是一个极其隐蔽、后果却很严重的坑：
                //   WinForms 的 DropDownList 组合框【默认】响应鼠标滚轮 —— 只要光标停在
                //   它上面，滚一格就换一个选中项，**根本不需要它获得焦点**。
                //   而设置页是可以滚动的，用户用滚轮翻页时光标难免扫过
                //   "左键 / 右键 / 中键"这几个下拉框 —— 于是动作绑定被悄悄改掉，
                //   用户完全不知道。
                //
                //   实测用户配置里 ActionRight=Minimize、ActionMiddle=Close，
                //   而他本人并不记得改过；他报的"到达触发阈值后右键的功能没有了 /
                //   按右键窗口直接消失"，就是右键被滚轮改成了"最小化"。
                //
                // 做法：没展开就把 WM_MOUSEWHEEL 直接吞掉、不往下传。
                //   展开（DroppedDown）时仍然允许滚轮在列表里选值 —— 那时意图很明确。
                if (m.Msg == WM_MOUSEWHEEL_MSG)
                {
                    if (!DroppedDown) { WheelSwallowedForTest++; return; }
                    WheelForwardedForTest++;
                }

                base.WndProc(ref m);
                if (m.Msg == WM_PAINT_MSG) PaintClosed();
            }

            /// <summary>仅供自检：滚轮被吞掉 / 被放行的次数（验证守卫的条件正确）。</summary>
            internal int WheelSwallowedForTest;
            internal int WheelForwardedForTest;

            /// <summary>收起态：盖掉系统画的那套，换成圆角 + 细描边 + 现代箭头。</summary>
            private void PaintClosed()
            {
                try
                {
                    if (!IsHandleCreated || Width <= 0 || Height <= 0) return;
                    using (var g = Graphics.FromHwnd(Handle))
                    {
                        HighQuality.Apply(g);

                        int pad = Fields.Sc(1);
                        var rect = new Rectangle(pad, pad, Width - pad * 2 - 1, Height - pad * 2 - 1);
                        var path = RoundedPath(rect, Fields.Sc(Design.FieldRadius));
                        try
                        {
                            using (var b = new SolidBrush(Enabled ? BackColor : Design.Canvas))
                                g.FillPath(b, path);
                            Color stroke = !Enabled ? Color.FromArgb(228, 231, 238)
                                         : (_hover || _open) ? Design.InputStrokeFocus
                                         : Design.InputStroke;
                            using (var p = new Pen(stroke)) g.DrawPath(p, path);
                        }
                        finally { path.Dispose(); }

                        int arrow = Fields.Sc(26);
                        object item = SelectedIndex >= 0 && SelectedIndex < Items.Count
                                      ? Items[SelectedIndex] : null;
                        var inner = new Rectangle(Fields.Sc(10), 0,
                                                  Width - arrow - Fields.Sc(14), Height);
                        if (ClosedPainter != null) ClosedPainter(g, inner, item);
                        else
                            TextRenderer.DrawText(g, item != null ? item.ToString() : "", Font, inner,
                                Enabled ? Design.TextPrimary : Color.FromArgb(160, 165, 176),
                                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                        // 现代箭头：细折线 chevron，而不是实心三角
                        float cx = Width - arrow / 2f;
                        float cy = Height / 2f;
                        float u = Fields.Sc(2) * 1.15f;
                        using (var p = new Pen(Enabled ? Design.TextSecondary
                                                       : Color.FromArgb(180, 185, 195),
                                               Math.Max(1.2f, Fields.Sc(1) * 1.4f)))
                        {
                            p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                            p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                            p.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                            g.DrawLines(p, new[]
                            {
                                new PointF(cx - u * 1.7f, cy - u * 0.45f),
                                new PointF(cx,             cy + u * 1.15f),
                                new PointF(cx + u * 1.7f, cy - u * 0.45f),
                            });
                        }
                    }
                }
                catch (Exception ex) { Log.Error(ex); }
            }
        }

        /// <summary>
        /// 自绘数字框。
        ///
        /// 原生 NumericUpDown 的外框是系统画的立体边（实测 #ABADB3），
        /// 是这套界面里最"2008 年"的一块。实测确认了两件事：
        ///   · BorderStyle = None 会把 PreferredHeight 从 38 拉到 34 —— 不能用；
        ///   · 保留 BorderStyle = FixedSingle（高度不变）+ 覆写 OnPaint 且不调 base，
        ///     系统边框就完全不会出现，可以换成自己的圆角描边。
        /// 右边的微调按钮是 UpDownBase 的【子控件】，父窗口画不到它，
        /// 所以只把它的底色刷白让它"退"下去，不隐藏控件 ——
        /// 隐藏子控件会让 T38「所有控件可见」变红。
        /// </summary>
        private sealed class FlatNum : NumericUpDown
        {
            public FlatNum()
            {
                BorderStyle = BorderStyle.FixedSingle;   // 保留高度，外框由 OnPaint 接管
                BackColor = Design.Surface;
                ForeColor = Design.TextPrimary;
                SetStyle(ControlStyles.UserPaint
                         | ControlStyles.AllPaintingInWmPaint
                         | ControlStyles.OptimizedDoubleBuffer
                         | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                for (int i = 0; i < Controls.Count; i++)
                {
                    try { Controls[i].BackColor = Design.Surface; } catch { }
                }
            }

            /// <summary>
            /// ★ 滚轮不得改变数值（除非这个数字框已经获得焦点）★
            ///
            /// 与 FlatCombo 是同一个坑的另一半：NumericUpDown 默认也响应鼠标滚轮，
            /// 光标停在上面滚一格就 +1/-1。
            /// 设置页能滚动，用户翻页时光标扫过"长按阈值 / 粗细 / 圆角 / 缩放角区"
            /// 这些数字框，数值就被悄悄改掉了（用户配置里 HoldMs=300、CornerPercent=21、
            /// FrameRadius=7 都和出厂值不同，很可能就是这么来的）。
            ///
            /// 只有【已经聚焦】的数字框才允许滚轮微调 —— 那时用户是在编辑它。
            /// </summary>
            protected override void WndProc(ref Message m)
            {
                const int WM_MOUSEWHEEL = 0x020A;
                if (m.Msg == WM_MOUSEWHEEL && !Focused) return;
                base.WndProc(ref m);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                // ★ 刻意不调用 base.OnPaint —— 那一句就是画系统立体边框的。
                using (var b = new SolidBrush(Enabled ? Design.Surface : Design.Canvas))
                    e.Graphics.FillRectangle(b, ClientRectangle);
            }
        }

        /// <summary>
        /// 自绘复选框。
        ///
        /// 为什么不用原生 CheckBox：
        ///   系统那个方框 + 内凹对勾是 Win95 时代的画法，
        ///   放在这套卡片式界面里非常扎眼 —— 用户说"丑得要命"，
        ///   很大一部分就是它和原生下拉框造成的。
        ///
        /// 画法：
        ///   · 17×17 圆角方块（圆角 5），未选中是白底 + 淡描边；
        ///   · 选中时填强调色 + 白色对勾（折线，圆头，跟手绘一样自然）；
        ///   · 悬停时描边变成强调色、底色微亮；
        ///   · 文字紧跟在方块右边、垂直居中。
        ///
        /// 布局影响为 0：尺寸、Location、Anchor、Text 全都不动，只重写 OnPaint
        /// —— 和 FlatButton 同一套思路（自检里那套布局断言也因此不会受影响）。
        /// </summary>
        private sealed class FlatCheck : CheckBox
        {
            private readonly Tween _hoverT;
            private readonly Tween _checkT;

            public FlatCheck()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint
                         | ControlStyles.OptimizedDoubleBuffer
                         | ControlStyles.UserPaint
                         | ControlStyles.ResizeRedraw, true);
                FlatStyle = FlatStyle.Flat;
                BackColor = Design.Surface;
                ForeColor = Design.TextPrimary;
                Cursor = Cursors.Hand;
                _hoverT = new Tween(this);
                _checkT = new Tween(this);
                _checkT.Snap(Checked ? 1f : 0f);
            }

            protected override void OnMouseEnter(EventArgs e) { _hoverT.To(1f); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hoverT.To(0f); base.OnMouseLeave(e); }
            protected override void OnCheckedChanged(EventArgs e)
            {
                // 勾选时让对勾"弹"出来（0 → 1），取消勾选直接归零（不需要反向动画）。
                if (Checked) _checkT.To(1f); else _checkT.Snap(0f);
                base.OnCheckedChanged(e);
            }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                HighQuality.Apply(g);

                // ★ 必须先铺底色 ★
                //   UserPaint=true 意味着控件位图由我们自己负责，但位图本体并不是
                //   "每帧重置成 BackColor"——首次绘制时是未初始化的（实测黑色），
                //   即便后续帧也会保留上一次没被覆盖到的像素。
                //   不铺底的话，checkbox 后面那块"label 文字区域"就一直黑着，
                //   字也是 TextPrimary 的深灰，于是肉眼看到"文字跟背景同色"——
                //   用户的截图里那一排黑色 bar 就是这个，整行标签都看不见。
                g.Clear(BackColor);

                float s = CurrentUiScale > 0f ? CurrentUiScale : 1f;

                // ══════════════════════════════════════════════════════════════
                //  ★ 第四十二轮：方框复选框 → Win11 开关（Toggle Switch）★
                //
                //  为什么这一条比任何圆角/投影都更能改变观感：
                //    方框 + 对勾是 Windows 95 时代的控件形态。设置页里有十几处布尔项，
                //    十几排"小方块"排下来，无论卡片做得多精致，整体观感都会被拉回表单。
                //    Win11 里布尔项一律是【胶囊轨道 + 圆形滑块】，一眼就是新系统。
                //
                //  轨道宽度由 CheckTextChrome 统一供数（= 轨道 + 间隙），
                //  所以布局那边算出来的可用文字宽度和这里画出来的永远一致 ——
                //  不会再出现"按布局刚好放得下、实际折行被裁掉"那类问题。
                // ══════════════════════════════════════════════════════════════
                int trackW = CheckTextChrome - Fields.Sc(10);   // 留 10 设计px 的间隙
                int trackH = (int)Math.Round(trackW * 0.56);    // Win11 开关的宽高比约 1.8:1
                if (trackW < 20) trackW = 20;
                if (trackH < 12) trackH = 12;
                int gap = Math.Max(2, CheckTextChrome - trackW);

                int top = (Height - trackH) / 2;
                if (top < 0) top = 0;
                var track = new Rectangle(0, top, trackW, trackH);
                int rad = trackH / 2;                            // 胶囊：半径 = 半高

                bool on = Checked;
                float hv = _hoverT.Value;

                // 滑块进度：开 = checkT（0→1 滑过去），关 = 0。
                // 复用既有的 _checkT 补间，所以"打开时滑块滑过去"是天然带过场的。
                float ct = on ? Math.Min(1f, _checkT.Value) : 0f;
                if (on && ct <= 0.001f) ct = 1f;                 // 初次填充时直接到位

                Color trackFill;
                if (!Enabled)
                {
                    trackFill = on ? Color.FromArgb(203, 206, 214) : Color.FromArgb(238, 240, 244);
                }
                else
                {
                    // 关：中性灰（悬停稍深）；开：强调色（悬停稍亮）—— 按 ct 连续插值
                    Color offFill = MixColor(Color.FromArgb(206, 210, 219),
                                             Color.FromArgb(186, 191, 202), hv);
                    Color onFill = MixColor(Design.Accent,
                                            ControlPaint.Light(Design.Accent, 0.12f), hv);
                    trackFill = MixColor(offFill, onFill, ct);
                }

                using (var path = RoundedPath(track, rad))
                using (var b = new SolidBrush(trackFill))
                    g.FillPath(b, path);

                // ── 圆形滑块 ──
                int inset = (int)Math.Round(2.5 * s);
                if (inset < 1) inset = 1;
                int knob = trackH - inset * 2;
                if (knob < 4) knob = 4;
                int travel = trackW - inset * 2 - knob;
                if (travel < 0) travel = 0;
                int kx = track.Left + inset + (int)Math.Round(travel * ct);
                var knobRect = new Rectangle(kx, track.Top + inset, knob, knob);

                // 滑块阴影：一圈极淡的描边 + 一点点下沉，让白滑块在浅色轨道上也立得住
                using (var sh = new SolidBrush(Color.FromArgb(28, Design.Shadow)))
                    g.FillEllipse(sh, new Rectangle(knobRect.X, knobRect.Y + Math.Max(1, Fields.Sc(1)),
                                                    knobRect.Width, knobRect.Height));
                using (var b = new SolidBrush(Enabled ? Color.White : Color.FromArgb(246, 247, 250)))
                    g.FillEllipse(b, knobRect);

                var textRect = new Rectangle(track.Right + gap, 0, Width - track.Right - gap, Height);
                if (textRect.Width > 0)
                {
                    // 尊重调用方显式设过的 ForeColor（比如"允许全屏"那条橙色警告），
                    // 没设过时就是构造里给的 TextPrimary。
                    Color fg = Enabled ? ForeColor : Color.FromArgb(160, 165, 176);
                    TextRenderer.DrawText(g, Text, Font, textRect, fg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
            }
        }

        /// <summary>
        /// 帮助面板：点标题展开对应功能的说明（手风琴）。
        ///
        /// ★ 为什么面板高度固定、且【永远保持一项展开】★
        ///   设置页的布局引擎是按【设计稿几何】排的：每个控件的 BaseHeight 在构建时
        ///   记录一次，之后每帧被钉回这个高度。控件自己改高度，外层布局【不会感知】——
        ///   展开时分组不会跟着长高，多出来的内容会被分组裁掉。
        ///   所以这里不做"控件随内容伸缩"，而是把面板高度定死，内部自己排：
        ///   6 个标题 + 1 个展开体，总高恒定。
        ///   好处有两个：外层布局完全不受影响；展开/收起也不会让整页跳动。
        ///   又因为高度恒定，就不允许"把展开项也收起来"（那样会留一块空白），
        ///   点击已展开的标题不做任何事。
        /// </summary>
        internal sealed class HelpPanel : Control
        {
            internal sealed class Entry
            {
                public string Icon;
                public string Title;
                public string Body;
            }

            private readonly System.Collections.Generic.List<Entry> _entries =
                new System.Collections.Generic.List<Entry>();
            private int _open;
            private int _hover = -1;
            private int _headerH = 38;
            private int _bodyH = 88;

            public HelpPanel()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
            }

            public void SetEntries(System.Collections.Generic.List<Entry> list)
            {
                _entries.Clear();
                if (list != null) _entries.AddRange(list);
                Invalidate();
            }

            /// <summary>由 ScaleDesign 按 DPI 赋值（设计稿像素 → 物理像素）。</summary>
            public void SetMetrics(int headerH, int bodyH)
            {
                _headerH = Math.Max(1, headerH);
                _bodyH = Math.Max(1, bodyH);
                Invalidate();
            }

            // ── 仅供自检 ──
            internal int EntryCountForTest { get { return _entries.Count; } }
            internal int OpenIndexForTest { get { return _open; } }
            internal void ClickAtForTest(int y)
            {
                int h = HitHeader(y);
                if (h >= 0 && h != _open) _open = h;
                Invalidate();
            }
            /// <summary>仅供自检：展开项在面板内的可见区间（标题行 + 说明文字）。</summary>
            internal void OpenRangeForTest(out int top, out int bottom)
            {
                top = RowTop(_open);
                bottom = top + _headerH + _bodyH;
            }

            private int RowTop(int index)
            {
                int y = 0;
                for (int i = 0; i < index && i < _entries.Count; i++)
                {
                    y += _headerH;
                    if (i == _open) y += _bodyH;
                }
                return y;
            }

            private int HitHeader(int y)
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    int top = RowTop(i);
                    if (y >= top && y < top + _headerH) return i;
                }
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int h = HitHeader(e.Y);
                if (h != _hover) { _hover = h; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                if (_hover != -1) { _hover = -1; Invalidate(); }
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                int h = HitHeader(e.Y);
                // 点已展开的那一项不收起：面板高度固定，收起来会留一块空白。
                if (h >= 0 && h != _open) { _open = h; Invalidate(); }
                base.OnMouseDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                try
                {
                    var g = e.Graphics;
                    HighQuality.Apply(g);
                    g.Clear(BackColor);

                    int pad = Fields.Sc(4);
                    using (var iconFont = Icons.At(Fields.Sc(15)))
                    using (var titleFont = new Font(Font.FontFamily, Fields.Sc(13),
                                                    FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var bodyFont = new Font(Font.FontFamily, Fields.Sc(12),
                                                   FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        for (int i = 0; i < _entries.Count; i++)
                        {
                            int top = RowTop(i);
                            bool open = (i == _open);
                            var row = new Rectangle(pad, top + pad / 2,
                                                    Width - pad * 2, _headerH - pad);
                            if (row.Width <= 0 || row.Height <= 0) continue;

                            // 标题行底色：展开 = 强调色淡版；悬停 = 极淡中性灰
                            if (open || i == _hover)
                            {
                                Color bg = open ? Design.AccentSoft : Color.FromArgb(244, 245, 248);
                                using (var b = new SolidBrush(bg))
                                using (var path = RoundedPath(row, Fields.Sc(8)))
                                    g.FillPath(b, path);
                            }

                            // 图标（展开时用强调色，和标题一起"亮起来"）
                            int tx = row.Left + Fields.Sc(12);
                            if (iconFont != null)
                            {
                                var ir = new Rectangle(tx, row.Top, Fields.Sc(16), row.Height);
                                TextRenderer.DrawText(g, _entries[i].Icon, iconFont, ir,
                                    open ? Design.Accent : Design.TextSecondary,
                                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                                tx = ir.Right + Fields.Sc(8);
                            }

                            int chev = Fields.Sc(26);
                            var tr = new Rectangle(tx, row.Top,
                                                   row.Right - chev - tx, row.Height);
                            if (tr.Width > 0)
                            {
                                TextRenderer.DrawText(g, _entries[i].Title, titleFont, tr,
                                    open ? Design.Accent : Design.TextPrimary,
                                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                    | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                            }

                            // 箭头：收起是 ">"，展开是 "v"（Win11 的展开指示都是这么转的）
                            DrawChevron(g, row.Right - Fields.Sc(18),
                                row.Top + row.Height / 2, Fields.Sc(5), open,
                                open ? Design.Accent : Design.TextSecondary);

                            if (open)
                            {
                                var bodyRect = new Rectangle(
                                    row.Left + Fields.Sc(12), top + _headerH,
                                    Width - pad * 2 - Fields.Sc(24), _bodyH);
                                if (bodyRect.Width > 0 && bodyRect.Height > 0)
                                {
                                    TextRenderer.DrawText(g, _entries[i].Body, bodyFont,
                                        bodyRect, Design.TextSecondary,
                                        TextFormatFlags.Left | TextFormatFlags.Top
                                        | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak
                                        | TextFormatFlags.NoPadding);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }

            private static void DrawChevron(Graphics g, int cx, int cy, int u, bool open, Color color)
            {
                using (var p = new Pen(color, Math.Max(1.4f, u * 0.34f)))
                {
                    p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    p.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    if (!open)
                    {
                        g.DrawLines(p, new[]
                        {
                            new PointF(cx - u * 0.4f, cy - u),
                            new PointF(cx + u * 0.5f, cy),
                            new PointF(cx - u * 0.4f, cy + u),
                        });
                    }
                    else
                    {
                        g.DrawLines(p, new[]
                        {
                            new PointF(cx - u, cy - u * 0.4f),
                            new PointF(cx, cy + u * 0.5f),
                            new PointF(cx + u, cy - u * 0.4f),
                        });
                    }
                }
            }
        }

        private sealed class FlatButton : Button
        {
            public FlatButton()
            {
                // ★ 派生 Button 而不是 Control：
                //   设置窗口里有好几个字段声明成 Button，还有自检代码在读 .Bounds/.Enabled，
                //   换基类就得把这些全改一遍。派生 Button 后只重写画法，接口完全兼容。
                SetStyle(ControlStyles.AllPaintingInWmPaint
                         | ControlStyles.OptimizedDoubleBuffer
                         | ControlStyles.UserPaint
                         | ControlStyles.ResizeRedraw, true);

                // 系统 Button 的立体边框/凸起必须彻底关掉，
                // 否则自绘之上还会叠一层系统外观（Windows 上很容易出现"双边框"）。
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                FlatAppearance.MouseOverBackColor = Color.Transparent;
                FlatAppearance.MouseDownBackColor = Color.Transparent;

                BackColor = Design.Surface;
                ForeColor = Design.TextPrimary;
                Cursor = Cursors.Hand;
                UseVisualStyleBackColor = false;

                _hoverT = new Tween(this);
                _downT = new Tween(this);
            }

            /// <summary>true = 主按钮（实心强调色），false = 次按钮（描边）。</summary>
            public bool Primary;

            // 悬停 / 按下都做 120ms 过渡（Win11 的按钮不是瞬变的）。
            // 必须在构造函数里 new（Tween 需要宿主控件引用，字段初始化器里拿不到 this）。
            private readonly Tween _hoverT;
            private readonly Tween _downT;

            protected override void OnMouseEnter(EventArgs e)
            {
                _hoverT.To(1f); base.OnMouseEnter(e);
            }
            protected override void OnMouseLeave(EventArgs e)
            {
                _hoverT.To(0f); _downT.To(0f); base.OnMouseLeave(e);
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                _downT.To(1f); base.OnMouseDown(e);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                _downT.To(0f); base.OnMouseUp(e);
            }
            protected override void OnEnabledChanged(EventArgs e)
            {
                Invalidate(); base.OnEnabledChanged(e);
            }

            /// <summary>仅供自检：把悬停态推到 on，用来验证补间真的在过渡。</summary>
            internal void ProbeSetHover(bool on) { _hoverT.To(on ? 1f : 0f); }

            /// <summary>仅供自检：当前悬停进度（0..1）。</summary>
            internal float ProbeHoverValue { get { return _hoverT.Value; } }

            /// <summary>
            /// Button 默认的 ProcessDialogKey 已经会把空格/回车转成 Click，
            /// 这里不需要重复处理 —— 去掉早先那个自己实现的版本，
            /// 免得同一个按键被触发两次 Click（点一下存两次的经典坑）。
            /// </summary>

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                HighQuality.Apply(g);

                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                if (r.Width <= 0 || r.Height <= 0) return;
                // ★ 圆角也要跟着 DPI 缩放。
                //   写死 6 的话，200% 屏上按钮框变大了、圆角还是那么点，
                //   视觉上就是"一个很方的方块"，跟卡片组的圆角完全不是一个语言。
                int rad = CurrentUiScale > 0f ? (int)Math.Round(6 * CurrentUiScale) : 6;
                if (rad > r.Height / 2) rad = r.Height / 2;
                if (rad < 1) rad = 1;

                Color bg, fg, border;
                float hv = _hoverT.Value;
                float dv = _downT.Value;
                if (!Enabled)
                {
                    bg = Color.FromArgb(240, 241, 243);
                    fg = Color.FromArgb(170, 175, 184);
                    border = Color.FromArgb(233, 235, 239);
                }
                else if (Primary)
                {
                    // 强调色按钮：底色从 Accent → 亮一档 → 按下的深一档，全程插值。
                    Color baseC = Design.Accent;
                    Color hoverC = ControlPaint.Light(Design.Accent, 0.10f);
                    Color pressC = Design.AccentPressed;
                    bg = MixColor(baseC, hoverC, hv);
                    bg = MixColor(bg, pressC, dv);
                    fg = Color.White;
                    border = bg;
                }
                else
                {
                    bg = MixColor(Design.Surface, Color.FromArgb(250, 251, 252), hv);
                    bg = MixColor(bg, Color.FromArgb(238, 240, 244), dv);
                    fg = Design.TextPrimary;
                    border = MixColor(Design.Stroke, Color.FromArgb(205, 210, 219), hv);
                }

                var path = RoundedPath(r, rad);
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);

                // ★ Win11 细节：主按钮上缘一条极淡的高光 ★
                //   真实的 Win11 强调色按钮上沿有一条从上方打下来的亮线 ——
                //   就是这一条 1px，让按钮看起来是"凸起的实体"而不是一块平色。
                if (Primary && Enabled && r.Height > rad * 2 + 4)
                {
                    using (var hl = new Pen(Color.FromArgb(52, Color.White), Math.Max(1f, Fields.Sc(1))))
                        g.DrawLine(hl, r.X + rad, r.Y + 1, r.Right - rad, r.Y + 1);
                }

                // ★★ 文字绘制：原来这行是错的 ★★
                //   g.MeasureString(...) 拿到的 sz 来自 GDI+，
                //   而真正落笔的是 TextRenderer（GDI/Uniscribe）——
                //   两套 API 的字形宽度和行高基准都不一样，
                //   用 A 的度量去推算 B 的位置，还手工减个 -1，纯属凭感觉。
                //   高 DPI 下这个偏差被放大成"字偏出去、字和框不搭"。
                //
                //   正解：把整块矩形交给 TextRenderer，让它自己按 HorizontalCenter|VerticalCenter
                //   对齐 —— 它内部用的度量跟绘制是同一套，天然对齐。
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(0, 0, Width, Height), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

                if (Focused && Enabled)
                    using (var p = new Pen(Design.Accent, 1.6f))
                        g.DrawPath(p, RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), rad - 1));
            }
        }

        private static Label Label(string text, int x, int y, int w)
        {
            var l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.Size = new Size(w, 22);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        private static void StyleCombo(ComboBox c, int x, int y, int w)
        {
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Location = new Point(x, y);
            c.Size = new Size(w, 26);

            // ★ 外框交给外层卡片代画（见 Fields 的类注释）★
            //   原生 ComboBox 即使 FlatStyle.Flat，那圈内凹立体边与右侧实心三角
            //   仍然是系统画的，是"界面显旧"的主要来源。
            //   刻意【不】动 ItemHeight：DropDownList 的高度由它推出，
            //   一改高度就变，会连带把"底栏按钮和输入框等高"那条断言弄红。
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Design.Surface;
            c.ForeColor = Design.TextPrimary;
            Fields.Attach(c, Design.FieldRadius);
        }

        private static void StyleNum(NumericUpDown n, int x, int y, int w, int min, int max, int step)
        {
            n.Location = new Point(x, y);
            n.Size = new Size(w, 26);
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.TextAlign = HorizontalAlignment.Right;
            n.BackColor = Design.Surface;
            n.ForeColor = Design.TextPrimary;
            Fields.Attach(n, Design.FieldRadius);
        }

        /// <summary>把 TextBox 也纳入统一的"现代输入框"外观。</summary>
        private static void StyleText(TextBox t, Font font, bool readOnly)
        {
            Fields.MakeFlat(t, font, readOnly);
            Fields.Attach(t, Design.FieldRadius);
        }

        // ── 数据 <-> 控件 ───────────────────────────────────────

        private void Populate()
        {
            _populating = true;
            try
            {
                PopulateCore();
            }
            finally
            {
                _populating = false;
            }
        }

        private void PopulateCore()
        {
            UpdateKeyText();

            _chkHold.Checked = _edit.RequireHold;
            _numHold.Value = Clamp(_edit.HoldMs, (int)_numHold.Minimum, (int)_numHold.Maximum);
            _numHold.Enabled = _edit.RequireHold;

            _cmbLeft.Items.Clear();
            _cmbRight.Items.Clear();
            _cmbMiddle.Items.Clear();
            for (int i = 0; i < Actions.All.Length; i++)
            {
                string a = Actions.All[i];
                _cmbLeft.Items.Add(new ActionItem { Value = a, Text = Actions.DisplayName(a) });
                _cmbRight.Items.Add(new ActionItem { Value = a, Text = Actions.DisplayName(a) });
                _cmbMiddle.Items.Add(new ActionItem { Value = a, Text = Actions.DisplayName(a) });
            }
            SelectAction(_cmbLeft, _edit.ActionLeft);
            SelectAction(_cmbRight, _edit.ActionRight);
            SelectAction(_cmbMiddle, _edit.ActionMiddle);

            _chkFrame.Checked = _edit.ShowActivationFrame;

            // ★ 顺序很关键：先把下拉框填满，再恢复选中项。
            //   重建 Items 会触发 SelectedIndexChanged -> SyncPreview()，
            //   如果此时另一只下拉框还是空的，SyncPreview 会读到 fallback，
            //   预览就会在"颜色已就位、样式还是空"的瞬间画错一次，
            //   并且把 _edit 里的选择当成"脏"而误报。填满之后再恢复选择最稳。
            _cmbFrameColor.BeginUpdate();
            _cmbFrameStyle.BeginUpdate();
            try
            {
                _cmbFrameColor.Items.Clear();
                for (int i = 0; i < FrameColors.All.Length; i++)
                {
                    var e = FrameColors.All[i];
                    _cmbFrameColor.Items.Add(new ColorItem
                    {
                        Value = e.Id,
                        Text = e.Name,
                        Color = FrameColors.Resolve(e.Id)   // "跟随系统强调色"这里取到真实颜色
                    });
                }

                _cmbFrameStyle.Items.Clear();
                for (int i = 0; i < FrameStyles.All.Length; i++)
                {
                    string s = FrameStyles.All[i];
                    _cmbFrameStyle.Items.Add(new IdItem { Value = s, Text = FrameStyles.DisplayName(s) });
                }
            }
            finally
            {
                _cmbFrameStyle.EndUpdate();
                _cmbFrameColor.EndUpdate();
            }

            SelectId(_cmbFrameStyle, _edit.FrameStyleId);   // 先恢复样式
            SelectId(_cmbFrameColor, _edit.FrameColorId);   // 再恢复颜色（最后触发一次预览）

            _numFrameThick.Value = Clamp(_edit.FrameThickness, (int)_numFrameThick.Minimum, (int)_numFrameThick.Maximum);
            _numFrameRadius.Value = Clamp(_edit.FrameRadius, (int)_numFrameRadius.Minimum, (int)_numFrameRadius.Maximum);
            SyncFrameEnabled();
            SyncPreview();

            _numCorner.Value = Clamp(_edit.CornerPercent, (int)_numCorner.Minimum, (int)_numCorner.Maximum);
            _chkDblLeft.Checked = _edit.DoubleClickLeftMaximize;
            _chkDblRight.Checked = _edit.DoubleClickRightZone;
            _chkRestoreMax.Checked = _edit.RestoreMaximizedWhenMoved;
            _chkFront.Checked = _edit.BringToFront;
            _numFrameOpacity.Value = Clamp(_edit.FrameOpacity, (int)_numFrameOpacity.Minimum, (int)_numFrameOpacity.Maximum);
            _chkFullscreen.Checked = _edit.AllowFullscreenWindows;
            _chkExitFullscreen.Checked = _edit.ExitFullscreenOnTrigger;
            _chkShake.Checked = _edit.ShakeMinimizeOthers;
            _txtBlacklist.Text = _edit.Blacklist;

            // #36 刷新率：模式 + 固定值 + 本机实测提示
            _cmbRefreshMode.Items.Clear();
            _cmbRefreshMode.Items.Add(new IdItem { Value = RefreshModes.Auto, Text = "跟随屏幕" });
            _cmbRefreshMode.Items.Add(new IdItem { Value = RefreshModes.Custom, Text = "固定值" });
            SelectId(_cmbRefreshMode, _edit.DragRefreshMode);
            _numRefreshHz.Value = Clamp(_edit.DragRefreshHz, (int)_numRefreshHz.Minimum, (int)_numRefreshHz.Maximum);
            SyncRefreshEnabled();
            int detectedHz = Native.GetRefreshRate();
            _lblRefreshHint.Text = "跟随屏幕：按实测刷新率节流，与显示器同步（本机实测 "
                + (detectedHz > 0
                    ? (detectedHz + "Hz，每帧约 " + Math.Round(1000.0 / detectedHz) + "ms）")
                    : "不到刷新率，此时不节流）")
                + "。\r\n固定值：自定义节流频率，0 = 不限制。改动在下次拖动时生效。";


            _cmbAutoStart.Items.Clear();
            _cmbAutoStart.Items.Add(new IdItem { Value = AutoStartModes.TaskScheduler, Text = "计划任务（最高权限、登录时静默启动，推荐）" });
            _cmbAutoStart.Items.Add(new IdItem { Value = AutoStartModes.Registry, Text = "注册表 Run 项（每次登录会弹一次 UAC）" });
            _cmbAutoStart.Items.Add(new IdItem { Value = AutoStartModes.None, Text = "不开机启动" });
            SelectId(_cmbAutoStart, _edit.AutoStart);

            _chkRunAsAdmin.Checked = _edit.RunAsAdmin;
            _chkWarnNonAdmin.Checked = _edit.WarnNonAdmin;

            UpdateAdminLabel();
        }

        private void SyncFrameEnabled()
        {
            bool on = _chkFrame.Checked;
            _cmbFrameColor.Enabled = on;
            _cmbFrameStyle.Enabled = on;
            _numFrameThick.Enabled = on;
            _numFrameRadius.Enabled = on;
            _gridColor.Enabled = on;
            _galleryStyle.Enabled = on;
            _galleryStyle.FrameEnabled = on;
            _pnlPreview.FrameEnabled = on;
            _pnlPreview.OpacityPercent = (int)_numFrameOpacity.Value;
            _pnlPreview.Refresh_();
        }

        /// <summary>色板要显示的全部颜色（含"跟随系统强调色"的实时解析结果）。</summary>
        private static System.Collections.Generic.List<ColorSwatchGrid.Entry> BuildColorEntries()
        {
            var list = new System.Collections.Generic.List<ColorSwatchGrid.Entry>();
            for (int i = 0; i < FrameColors.All.Length; i++)
            {
                var e = FrameColors.All[i];
                list.Add(new ColorSwatchGrid.Entry
                {
                    Id = e.Id,
                    Name = e.Name,
                    Color = FrameColors.Resolve(e.Id),
                });
            }
            return list;
        }

        /// <summary>颜色 ID → 中文名（"当前颜色：xxx"那行用）。</summary>
        private static string ColorNameOf(string id)
        {
            for (int i = 0; i < FrameColors.All.Length; i++)
                if (FrameColors.All[i].Id == id) return FrameColors.All[i].Name;
            return id;
        }

        /// <summary>固定值数字框只在"固定值"模式下可改（#36）。</summary>
        private void SyncRefreshEnabled()
        {
            _numRefreshHz.Enabled = SelectedId(_cmbRefreshMode, RefreshModes.Auto) == RefreshModes.Custom;
        }

        /// <summary>
        /// 把界面上当前的选择同步进预览框并重画。
        ///
        /// 注意：这里读的是【控件】当前值，不是 _edit。
        /// 两者在用户改动之后、点保存之前是不一样的 —— 预览必须跟控件走，
        /// 否则就变成"我改了颜色，预览还显示保存前的样子"。
        /// </summary>
        /// <summary>
        /// 帮助内容（手风琴每一项的正文）。
        ///
        /// ★ 内容必须与【当前版本的实际行为】一致 ★
        ///   帮助文字是"承诺"，代码一改就容易和它脱节 —— 所以这里只写
        ///   代码里确实存在的能力（触发/动作/外观/全屏降级/晃窗/黑名单），
        ///   不写"计划中"或已经删掉的功能（比如早已移除的自动贴边）。
        /// </summary>
        /// <summary>
        /// 把"同步侧栏高亮"投递到消息队列末尾执行。
        ///
        /// 为什么不能直接调：MouseWheel 回调时，控件的默认处理函数还没把滚动
        /// 应用下去 —— 此刻 AutoScrollPosition 还是旧值，直接算会得到过期结果
        /// （实测 top 恒为 0）。投递到队尾，等滚动落地之后再算。
        /// </summary>
        private void SyncSidebarLater()
        {
            try
            {
                if (_sidebar == null) return;
                if (!IsHandleCreated) { _sidebar.SyncActive(); return; }
                BeginInvoke((Action)(delegate
                {
                    try { _sidebar.SyncActive(); } catch (Exception ex) { Log.Error(ex); }
                }));
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>
        /// 分组标题 → 侧栏用的短标题。
        ///
        /// 分组标题里常带括号补充说明（"鼠标键动作（长按触发键之后）"），
        /// 侧栏放不下；统一在这里剥掉括号部分，再加一道长度兜底。
        /// 放在一处而不是散在调用点上：以后改标题不用同步改侧栏。
        /// </summary>
        private static string ShortTitleOf(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";

            int cut = title.Length;
            char[] opens = { '（', '(', '【', '[', '《' };
            for (int k = 0; k < opens.Length; k++)
            {
                int i = title.IndexOf(opens[k]);
                if (i > 0 && i < cut) cut = i;
            }

            string s = title.Substring(0, cut).Trim();
            return s.Length <= 8 ? s : s.Substring(0, 8);
        }

        private static System.Collections.Generic.List<HelpPanel.Entry> BuildHelpEntries()
        {
            var list = new System.Collections.Generic.List<HelpPanel.Entry>();

            list.Add(new HelpPanel.Entry
            {
                Icon = Glyph.Keyboard,
                Title = "触发方式",
                Body = "按住触发键（默认 Enter，可改成任意键或鼠标侧键）达到设定时长后生效，"
                     + "光标下那扇窗口四周会亮起外框；松开触发键即退出。"
                     + "触发键只在长按到时才生效，短按会原样放行给当前程序，不影响正常打字。",
            });

            list.Add(new HelpPanel.Entry
            {
                Icon = Glyph.Move,
                Title = "鼠标键动作",
                Body = "触发后：左键拖动窗口、右键缩放窗口、中键执行一次性动作。"
                     + "三个键都能自定义成移动 / 缩放 / 最大化 / 最小化 / 关闭 / 置底 / 置顶 / 居中。"
                     + "另外双击左键可最大化或还原，双击右键按九宫格吸附到屏幕对应区域。",
            });

            list.Add(new HelpPanel.Entry
            {
                Icon = Glyph.Color,
                Title = "外框外观",
                Body = "外框颜色、样式、粗细、圆角、不透明度都可以调，右上角是实时预览，"
                     + "和真正拖窗口时用的是同一段绘制代码。"
                     + "窗口贴近屏幕边缘时外框几何保持不变，不会缩小或跳动。",
            });

            list.Add(new HelpPanel.Entry
            {
                Icon = Glyph.Shield,
                Title = "全屏与最大化窗口",
                Body = "默认开启「触发时先把全屏 / 最大化窗口变回普通窗口」："
                     + "这样拖动和缩放都从一个正常的窗口尺寸开始，"
                     + "不会出现全屏窗口拖完一片黑、或者缩放抓不到边的情况。",
            });

            list.Add(new HelpPanel.Entry
            {
                Icon = Glyph.Speed,
                Title = "晃窗最小化其它窗口",
                Body = "触发后选中窗口，左右来回晃一晃，其它窗口会全部最小化；"
                     + "再晃一次即可还原（还原名单是记下来的，不会漏）。"
                     + "判定要求「多次往复 + 净位移接近 0」，所以正常拖动窗口不会被误触发。",
            });

            list.Add(new HelpPanel.Entry
            {
                Icon = Glyph.Shield,
                Title = "不生效的窗口",
                Body = "把窗口标题 / 程序名 / 窗口类名的关键字填进黑名单（逗号分隔），"
                     + "命中就完全不接管。不知道填什么可以用「选择程序」挑 exe，"
                     + "或用「点选窗口」直接在屏幕上点那扇窗，关键字会自动填进去。",
            });

            return list;
        }

        private void SyncPreview()
        {
            string colorId = SelectedId(_cmbFrameColor, FrameColors.DefaultId);
            string styleId = SelectedId(_cmbFrameStyle, FrameStyles.DefaultId);
            int thick = (int)_numFrameThick.Value;
            int radius = (int)_numFrameRadius.Value;
            bool on = _chkFrame.Checked;

            _pnlPreview.ColorId = colorId;
            _pnlPreview.StyleId = styleId;
            _pnlPreview.Thickness = thick;    // 属性 setter 内部会 Invalidate
            _pnlPreview.Radius = radius;
            _pnlPreview.FrameEnabled = on;
            _pnlPreview.Refresh_();

            // ★ 色板 / 样式画廊同步（第三十九轮）★
            //   它们只负责"显示成什么样"，选择状态仍在两个下拉框里 ——
            //   所以这里永远是"从选择状态推出去"，不会有第二份真相。
            _gridColor.SelectedId = colorId;
            _lblColorName.Text = "当前颜色：" + ColorNameOf(colorId);
            _galleryStyle.SelectedId = styleId;
            _galleryStyle.ColorId = colorId;
            _galleryStyle.Thickness = thick;
            _galleryStyle.Radius = radius;
            _galleryStyle.FrameEnabled = on;

            // 只要用户动过外观，就顺手告诉他"要保存才落到实际外框上"
            MarkFrameDirtyIfChanged();
        }

        /// <summary>
        /// 外观四项相对已保存配置有出入时，给一句明确提示。
        ///
        /// _populating 期间不报脏：填充控件本身就会触发一轮 SelectedIndexChanged，
        /// 那时候读到的可能还是中间状态，误报会让用户以为"我什么都没改它就脏了"。
        /// </summary>
        private void MarkFrameDirtyIfChanged()
        {
            if (_lblFrameTip == null || _populating) return;

            string color = SelectedId(_cmbFrameColor, FrameColors.DefaultId);
            string style = SelectedId(_cmbFrameStyle, FrameStyles.DefaultId);
            bool dirty = color != _edit.FrameColorId
                      || style != _edit.FrameStyleId
                      || (int)_numFrameThick.Value != _edit.FrameThickness
                      || (int)_numFrameRadius.Value != _edit.FrameRadius
                      || _chkFrame.Checked != _edit.ShowActivationFrame;

            _lblFrameTip.ForeColor = dirty
                ? Color.FromArgb(185, 96, 16)     // 脏了就变琥珀色，避免"以为已经生效了"
                : Design.Hint;

            _lblFrameTip.Text = dirty
                ? "外观已改动，点下面的「保存并生效」才会真正画到实际外框上。\r\n"
                    + "右上角是实时预览，和实际外框用的是同一段绘制代码。"
                : "外框用来告诉你「这扇窗口现在已经被接管了」，松手或者松开触发键就会消失。\r\n"
                    + "上面四个都可以随便改，右上角是实时预览。";
        }

        /// <summary>颜色下拉框自己画：左边一个色块，右边颜色名。</summary>
        /// <summary>
        /// 颜色项的绘制：圆角色块 + 名称。
        ///
        /// 同时服务两处 —— FlatCombo 的下拉列表项、以及"收起态"框里显示的内容，
        /// 所以"列表里看到的"和"收起后看到的"永远是同一个样子。
        /// </summary>
        private static void PaintColorEntry(Graphics g, Rectangle bounds, object item, Font font)
        {
            try
            {
                var it = item as ColorItem;
                if (it == null) return;
                if (bounds.Width <= 0 || bounds.Height <= 0) return;

                HighQuality.Apply(g);

                int side = Math.Max(8, (int)Math.Round(bounds.Height * 0.52));
                if (side > bounds.Width / 3) side = Math.Max(8, bounds.Width / 3);
                int top = bounds.Top + (bounds.Height - side) / 2;
                var swatch = new Rectangle(bounds.Left, top, side, side);

                var path = RoundedPath(swatch, Math.Max(2, Fields.Sc(3)));
                try
                {
                    using (var b = new SolidBrush(it.Color)) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(186, 192, 205)))
                        g.DrawPath(p, path);
                }
                finally { path.Dispose(); }

                var text = new Rectangle(swatch.Right + Fields.Sc(8), bounds.Top,
                                         bounds.Width - side - Fields.Sc(10), bounds.Height);
                TextRenderer.DrawText(g, it.Text, font != null ? font : SystemFonts.DefaultFont,
                    text, Design.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left
                    | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        private void UpdateAdminLabel()
        {
            bool admin = Program.IsAdministrator();
            _lblAdmin.Text = admin
                ? "当前权限：管理员（可以拖动任务管理器这类提权窗口）"
                : "当前权限：普通用户（日常窗口没问题，提权窗口拖不动）";
            _btnRestartAdmin.Enabled = !admin;
        }

        private void UpdateKeyText()
        {
            _txtKey.Text = KeysUtil.DisplayName(_edit.TriggerKeyId);
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static void SelectAction(ComboBox c, string value)
        {
            for (int i = 0; i < c.Items.Count; i++)
            {
                var it = c.Items[i] as ActionItem;
                if (it != null && it.Value == value) { c.SelectedIndex = i; return; }
            }
            // 同 SelectId：绝不能停在 -1，否则框里显示空白
            // （用户原话："选项里面显示是空的，但我点开里面却又有好几个选项"）。
            // Items.Clear() 会把 SelectedIndex 重置成 -1，必须兜住。
            if (c.Items.Count > 0) c.SelectedIndex = 0;
        }

        /// <summary>
        /// 从 Items 里按 Value 选中某一项。
        ///
        /// ★ 刻意不设 SelectedIndex = -1：
        ///   DropDownStyle = DropDownList 的 ComboBox，
        ///   一旦 SelectedIndex 是 -1，框里显示的就是空白
        ///   （用户原话："选项里面显示是空的，但我点开里面却又有好几个选项"）。
        ///   保持"永远至少选中一项"，框里就永远有内容。
        /// </summary>
        private static void SelectId(ComboBox c, string value)
        {
            if (c == null || c.Items.Count == 0) return;

            for (int i = 0; i < c.Items.Count; i++)
            {
                var id = c.Items[i] as IdItem;
                if (id != null && id.Value == value) { c.SelectedIndex = i; return; }

                var col = c.Items[i] as ColorItem;
                if (col != null && col.Value == value) { c.SelectedIndex = i; return; }
            }
            c.SelectedIndex = 0;
        }

        private static string SelectedAction(ComboBox c)
        {
            var it = c.SelectedItem as ActionItem;
            return it != null ? it.Value : null;
        }

        /// <summary>
        /// 从下拉框里取回业务 ID。
        ///
        /// ★ 这里踩过一个坑，务必注意：外框颜色下拉框里装的是 <see cref="ColorItem"/>，
        /// 其余下拉框装的是 <see cref="IdItem"/>。早期版本只写了 `as IdItem`，
        /// 于是颜色框永远取到 null、永远回退到 fallback（薄荷青）——
        /// 表现就是"界面上明明选了朱红，预览不变，保存后也不变"。
        /// 现在两种 item 都认。
        /// </summary>
        private static string SelectedId(ComboBox c, string fallback)
        {
            var id = c.SelectedItem as IdItem;
            if (id != null && !string.IsNullOrEmpty(id.Value)) return id.Value;

            var col = c.SelectedItem as ColorItem;
            if (col != null && !string.IsNullOrEmpty(col.Value)) return col.Value;

            return fallback;
        }

        // ── 录制触发键 ──────────────────────────────────────────

        private void StartRecording()
        {
            _recording = true;
            _coord.SuppressTrigger = true;
            _txtKey.Text = "请按下要用的按键…";
            _txtKey.BackColor = Color.FromArgb(255, 244, 214);
            _lblKeyHint.Text = "正在录制：按任意键或鼠标侧键，Esc 取消。"
                             + "左/右/中键已被鼠标动作占用，不能当触发键。";
            Focus();
        }

        private void StopRecording(bool applied)
        {
            _recording = false;
            _coord.SuppressTrigger = false;
            _txtKey.BackColor = SystemColors.Window;
            UpdateKeyText();

            if (!applied)
            {
                _lblKeyHint.Text = "已取消录制。";
                return;
            }

            string warn = string.Empty;
            if (KeysUtil.IsToggleKey(_edit.TriggerKeyId))
            {
                warn = "注意：这是切换型按键，本程序会压制它，因此它不会再切换大小写/数字锁定。";
            }
            else if (KeysUtil.IsWindowsKey(_edit.TriggerKeyId))
            {
                warn = "注意：Win 键被作为触发键期间，Win 组合键（Win+E、Win+D 等）无法使用。";
            }
            _lblKeyHint.Text = warn.Length > 0 ? warn : "已绑定：" + KeysUtil.DisplayName(_edit.TriggerKeyId);
            _lblKeyHint.ForeColor = warn.Length > 0 ? Design.WarnText : Design.Hint;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_recording)
            {
                Keys k = keyData & Keys.KeyCode;
                if (k == Keys.Escape) { StopRecording(false); return true; }

                string id = ResolveRecordedKey(k);
                if (id == null)
                {
                    _lblKeyHint.Text = "左键 / 右键 / 中键不能作为触发键，请用键盘按键或鼠标侧键。";
                    _lblKeyHint.ForeColor = Design.WarnText;
                    return true;
                }

                _edit.TriggerKeyId = id;
                StopRecording(true);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_recording)
            {
                string id = KeysUtil.FromMouseButton(e.Button);
                if (id != null)
                {
                    _edit.TriggerKeyId = id;
                    StopRecording(true);
                    return;
                }
                if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
                {
                    _lblKeyHint.Text = "左键 / 右键 / 中键不能作为触发键，请用键盘按键或鼠标侧键。";
                    _lblKeyHint.ForeColor = Design.WarnText;
                    return;
                }
            }
            base.OnMouseDown(e);
        }

        private static bool IsDown(int vk)
        {
            return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        /// <summary>
        /// WinForms 不区分左右修饰键，这里在录制瞬间用 GetAsyncKeyState 查真实按下的那一侧。
        /// 注意：这里用 GetAsyncKeyState 是安全的 —— 录制过程中 SuppressTrigger 会把
        /// 触发键原样放行，所以按键状态在系统层面是真被按下的。
        /// （平时判断选定触发键【绝不能】用它，原因见 TriggerState 顶部的注释。）
        /// </summary>
        private static string ResolveRecordedKey(Keys k)
        {
            switch (k)
            {
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                    return IsDown(0xA0) ? KeysUtil.LeftShift : KeysUtil.RightShift;

                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                    return IsDown(0xA2) ? KeysUtil.LeftCtrl : KeysUtil.RightCtrl;

                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                    return IsDown(0xA4) ? KeysUtil.LeftAlt : KeysUtil.RightAlt;

                case Keys.LButton:
                case Keys.RButton:
                case Keys.MButton:
                    return null;

                case Keys.LWin:
                    return "K:0x5B";
                case Keys.RWin:
                    return "K:0x5C";
            }
            return KeysUtil.FromWinFormsKey(k);
        }

        // ── 保存 / 恢复默认 / 重启 ──────────────────────────────

        private void Save()
        {
            if (_recording) StopRecording(false);

            _edit.RequireHold = _chkHold.Checked;
            _edit.HoldMs = (int)_numHold.Value;

            _edit.ActionLeft = SelectedAction(_cmbLeft) ?? _edit.ActionLeft;
            _edit.ActionRight = SelectedAction(_cmbRight) ?? _edit.ActionRight;
            _edit.ActionMiddle = SelectedAction(_cmbMiddle) ?? _edit.ActionMiddle;

            _edit.ShowActivationFrame = _chkFrame.Checked;
            // ★ 同样必须回落"原配置"，不能回落"出厂默认" ★
            //   用户反复报过"改了主题不生效"，这里就是最隐蔽的一条成因：
            //   只要下拉框读不出选中项，就会静默把用户选的主题退回薄荷青，
            // 而界面上看不出任何异常 —— 看着像"保存没生效"，其实是"被悄悄改了"。
            _edit.FrameColorId = SelectedId(_cmbFrameColor, _edit.FrameColorId);
            _edit.FrameStyleId = SelectedId(_cmbFrameStyle, _edit.FrameStyleId);
            _edit.FrameThickness = (int)_numFrameThick.Value;
            _edit.FrameRadius = (int)_numFrameRadius.Value;

            _edit.CornerPercent = (int)_numCorner.Value;
            _edit.ShakeMinimizeOthers = _chkShake.Checked;
            _edit.DragRefreshMode = SelectedId(_cmbRefreshMode, _edit.DragRefreshMode);
            _edit.DragRefreshHz = (int)_numRefreshHz.Value;
            _edit.DoubleClickLeftMaximize = _chkDblLeft.Checked;
            _edit.DoubleClickRightZone = _chkDblRight.Checked;
            _edit.RestoreMaximizedWhenMoved = _chkRestoreMax.Checked;
            _edit.BringToFront = _chkFront.Checked;
            _edit.FrameOpacity = (int)_numFrameOpacity.Value;
            _edit.AllowFullscreenWindows = _chkFullscreen.Checked;
            _edit.ExitFullscreenOnTrigger = _chkExitFullscreen.Checked;
            _edit.Blacklist = _txtBlacklist.Text;


            // ★ 回落值必须是"原配置里的值"，不能是 AutoStartModes.None ★
            //
            // 以前这里写的是 SelectedId(_cmbAutoStart, AutoStartModes.None)，
            // 意味着只要下拉框因为任何原因读不出选中项（没填充、选中索引越界、
            // 被 T41 那种"恢复默认值"路径重置），保存时就会把用户的开机自启
            // 静默改成"不自启" —— 而界面上看不出任何异常，
            // 用户重启电脑发现程序没启动，却会以为是"配置没保存"。
            _edit.AutoStart = SelectedId(_cmbAutoStart, _edit.AutoStart);
            _edit.RunAsAdmin = _chkRunAsAdmin.Checked;
            _edit.WarnNonAdmin = _chkWarnNonAdmin.Checked;

            // 顺手把用户拉出来的窗口尺寸记下来
            CaptureSizeInto(_edit);

            _store.Apply(_edit);

            string msg;
            if (AutoStart.Apply(_edit.AutoStart, AppPaths.ExePath, out msg))
            {
                _lblStatus.ForeColor = Design.Success;
                _lblStatus.Text = "已保存并立即生效";
            }
            else
            {
                _lblStatus.ForeColor = Color.FromArgb(185, 28, 28);
                _lblStatus.Text = "配置已保存，但开机自启设置失败";
                MessageBox.Show(msg, "开机自启设置失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (!string.IsNullOrEmpty(msg))
            {
                MessageBox.Show(msg, "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            // 刚勾上"要管理员权限"，而现在是普通权限 —— 立刻问他要不要重启，
            // 免得改完发现没生效、不知道还要这一步。
            if (_edit.RunAsAdmin && !Program.IsAdministrator())
            {
                var r = MessageBox.Show(
                    "已保存。\r\n\r\n管理员权限只在【启动的时候】申请，所以要重启程序才会生效。\r\n" +
                    "现在以管理员身份重启吗？（选\"否\"就等下次启动再提权）",
                    "长按拖窗 · 权限变更",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (r == DialogResult.Yes) RestartAsAdmin();
            }
        }

        // ── 黑名单：两个"帮用户填关键字"的入口 ──────────────────────

        /// <summary>
        /// 从一个 exe 文件里取关键字加进黑名单。
        ///
        /// 存在的意义：黑名单是按【子串】匹配窗口类名 / 标题 / 进程名的，
        /// 而用户根本不知道该填什么 —— 类名是内部名、标题是文档名。
        /// 让他直接指一个 exe 文件，我们把文件名当关键字，最直观。
        /// </summary>
        private void PickExeFile()
        {
            try
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "选择要排除的程序（它的窗口将不再被接管）";
                    dlg.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                    dlg.CheckFileExists = true;
                    dlg.Multiselect = false;

                    if (dlg.ShowDialog(this) != DialogResult.OK) return;

                    string name = System.IO.Path.GetFileName(dlg.FileName);
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        name = name.Substring(0, name.Length - 4);

                    if (string.IsNullOrEmpty(name))
                    {
                        Status("没从文件名里取到关键字", true);
                        return;
                    }
                    AppendBlacklistKeyword(name);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                Status("打开文件对话框失败：" + ex.Message, true);
            }
        }

        /// <summary>
        /// 十字准星点选：点屏幕上哪扇窗，就把哪扇窗的程序加进黑名单。
        ///
        /// 实现方式是一层全屏半透明遮罩（Cursor=Cross），而不是去监听全局鼠标：
        ///   · 本程序自己就装了全局鼠标钩子，触发键激活时会吞事件，
        ///     借它来拾取会互相干扰；
        ///   · 遮罩能给出明确的视觉反馈（屏幕变暗 + 十字光标），
        ///     用户一眼就知道"现在在选窗口"。
        ///
        /// ★ 点击时必须先把遮罩自己藏掉 ★
        ///   它是全屏 TopMost，不藏的话 WindowFromPoint 命中的永远是它自己。
        /// </summary>
        private void PickWindowByClick()
        {
            try
            {
                var mask = new Form();
                mask.FormBorderStyle = FormBorderStyle.None;
                mask.StartPosition = FormStartPosition.Manual;
                mask.Bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
                mask.TopMost = true;
                mask.ShowInTaskbar = false;
                mask.Cursor = Cursors.Cross;
                mask.BackColor = Color.Black;
                mask.Opacity = 0.18;        // 淡淡暗化，提示"正在拾取"
                mask.KeyPreview = true;

                bool done = false;

                mask.MouseDown += (s, e) =>
                {
                    if (done) return;
                    done = true;

                    var pt = new Native.POINT(mask.Left + e.X, mask.Top + e.Y);

                    // 先藏掉自己，否则命中的是遮罩
                    try { mask.Hide(); } catch { }
                    Application.DoEvents();

                    AddBlacklistFromWindowAt(pt);

                    try { mask.Close(); } catch { }
                };

                mask.KeyDown += (s, e) =>
                {
                    if (e.KeyCode != Keys.Escape) return;
                    done = true;
                    Status("已取消窗口点选", false);
                    try { mask.Close(); } catch { }
                };

                Status("请点击要排除的窗口（按 Esc 取消）", false);
                mask.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                Status("窗口点选失败：" + ex.Message, true);
            }
        }

        /// <summary>把"光标下那扇窗"的程序名加进黑名单。</summary>
        private void AddBlacklistFromWindowAt(Native.POINT pt)
        {
            try
            {
                IntPtr h = Native.WindowFromPoint(pt);
                if (h != IntPtr.Zero) h = Native.GetAncestor(h, Native.GA_ROOT);

                if (h == IntPtr.Zero)
                {
                    Status("没找到窗口，请对着目标窗口的标题栏再点一次", true);
                    return;
                }

                string exe = WindowActions.ProcessExeName(h);
                string title = Native.GetWindowTitle(h);
                string cls = Native.GetClassName(h);

                // ★ 优先用【进程名】，其次类名，最后才用标题 ★
                //
                //   进程名最稳定：同一个程序的所有窗口都一样，用户一眼能认出来。
                //   标题会随打开的文档变化（"无标题 - 记事本" → "a.txt - 记事本"），
                //   拿它当关键字只能排除当前那一扇窗，换个文件就又不管用了。
                string kw = !string.IsNullOrEmpty(exe) ? exe
                          : !string.IsNullOrEmpty(cls) ? cls
                          : title;

                if (string.IsNullOrEmpty(kw))
                {
                    Status("这扇窗口取不到可用的标识，请改用「选择程序…」", true);
                    return;
                }

                AppendBlacklistKeyword(kw);

                string shown = title;
                if (shown != null && shown.Length > 18) shown = shown.Substring(0, 18) + "…";
                Status("已排除：" + kw + (string.IsNullOrEmpty(shown) ? "" : "（" + shown + "）"), false);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                Status("点选失败：" + ex.Message, true);
            }
        }

        /// <summary>
        /// 把一个关键字追加进黑名单输入框（已存在就不重复加）。
        /// 分隔符用中文逗号，和提示文字里说的一致，也和 ApplyBlacklist 的解析保持一致。
        /// </summary>
        private void AppendBlacklistKeyword(string kw)
        {
            try
            {
                if (string.IsNullOrEmpty(kw)) return;
                kw = kw.Trim();
                if (kw.Length == 0) return;

                string cur = _txtBlacklist.Text ?? string.Empty;

                // 去重：中英文逗号都当分隔符
                string[] parts = cur.Split(new char[] { ',', '，' },
                    StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (string.Equals(parts[i].Trim(), kw, StringComparison.OrdinalIgnoreCase)) return;
                }

                string head = cur.Trim();
                _txtBlacklist.Text = head.Length == 0 ? kw : head + "，" + kw;

                // 把光标移到末尾，用户接着手打时不会插到中间
                try { _txtBlacklist.SelectionStart = _txtBlacklist.Text.Length; } catch { }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        /// <summary>仅供自检：走一遍"把关键字追加进黑名单输入框"的逻辑。</summary>
        internal void ProbeAppendBlacklist(string kw)
        {
            AppendBlacklistKeyword(kw);
        }

        /// <summary>仅供自检：读回黑名单输入框的内容。</summary>
        internal string ProbeBlacklistText
        {
            get { return _txtBlacklist != null ? _txtBlacklist.Text : string.Empty; }
        }

        /// <summary>在底栏状态区显示一句话。error=true 时用红色。</summary>
        private void Status(string text, bool error)
        {
            try
            {
                if (_lblStatus == null) return;
                _lblStatus.ForeColor = error
                    ? Color.FromArgb(185, 28, 28)
                    : Design.Success;
                _lblStatus.Text = text ?? string.Empty;
            }
            catch { }
        }

        private void ResetDefaults()
        {
            var d = new AppConfig();
            d.AutoStart = _edit.AutoStart;                  // 自启方式属于环境偏好，不跟着"恢复默认"一起被清掉
            d.RunAsAdmin = _edit.RunAsAdmin;                // 同上：权限是用户在环境层面的选择
            d.WarnNonAdmin = _edit.WarnNonAdmin;
            d.Blacklist = _edit.Blacklist;                  // 黑名单是针对用户环境里具体软件的，不跟着重置
            d.SettingsWidth = _edit.SettingsWidth;          // 窗口尺寸也是环境偏好：点了"恢复默认"不该让窗口跳一下
            d.SettingsHeight = _edit.SettingsHeight;
            _edit = d;
            if (_recording) StopRecording(false);
            Populate();
            _lblStatus.ForeColor = Design.Hint;
            _lblStatus.Text = "已载入默认值，点「保存并生效」后写入";
        }

        private void RestartAsAdmin()
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.ExePath,
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                System.Diagnostics.Process.Start(psi);
                _coord.Dispose();
                Application.Exit();
            }
            catch (Exception ex)
            {
                Log.Info("以管理员重启被取消：" + ex.Message);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_recording) StopRecording(false);
            base.OnFormClosed(e);
        }

        /// <summary>
        /// 仅供自检：读出"当前界面上选中的外框外观"。
        ///
        /// ★ 这是本轮那个 bug 的核心探针。
        /// 之前 SelectedId 只认 IdItem，而颜色框里装的是 ColorItem，
        /// 于是无论用户在界面上选了什么，这里读出来永远是 Teal。
        /// 断言 "在界面上选朱红 → 这里读出 Red" 就能守住它。
        /// </summary>
        internal sealed class FrameSelection
        {
            public string ColorId;
            public string StyleId;
            public int Thickness;
            public int Radius;
            public bool FrameEnabled;
            /// <summary>预览框内部真正拿去绘制的颜色 ID（ScreenPreview 快照）。</summary>
            public string PreviewColorId;
            public string PreviewStyleId;
            public int PreviewThickness;
            public int PreviewRadius;
            public bool PreviewFrameEnabled;
            /// <summary>色板控件里高亮的那一格（第三十九轮：界面上"看得见的选中态"）。</summary>
            public string GridColorId;
            /// <summary>样式画廊里高亮的那一格。</summary>
            public string GridStyleId;
            /// <summary>"当前颜色：xxx"那行文字。</summary>
            public string ColorNameText;
        }

        internal FrameSelection ProbeFrameSelection()
        {
            return new FrameSelection
            {
                ColorId = SelectedId(_cmbFrameColor, "(null)"),
                StyleId = SelectedId(_cmbFrameStyle, "(null)"),
                Thickness = (int)_numFrameThick.Value,
                Radius = (int)_numFrameRadius.Value,
                FrameEnabled = _chkFrame.Checked,
                PreviewColorId = _pnlPreview.ColorId,
                PreviewStyleId = _pnlPreview.StyleId,
                PreviewThickness = _pnlPreview.Thickness,
                PreviewRadius = _pnlPreview.Radius,
                PreviewFrameEnabled = _pnlPreview.FrameEnabled,
                GridColorId = _gridColor.SelectedId,
                GridStyleId = _galleryStyle.SelectedId,
                ColorNameText = _lblColorName.Text,
            };
        }

        /// <summary>仅供自检 / 开发期截图：按索引取分组控件。</summary>
        internal static Control ProbeGroupAt(Form f, int index)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groupHost == null) return null;
            if (index < 0 || index >= sf._groupHost.Controls.Count) return null;
            return sf._groupHost.Controls[index];
        }

        /// <summary>仅供自检：强行按指定 DPI 重建界面（等价于窗口被拖到另一块屏）。</summary>
        internal static void ProbeRescaleForDpi(Form f, int dpi)
        {
            var sf = f as SettingsForm;
            if (sf != null) sf.RescaleForDpi(dpi);
        }

        /// <summary>仅供自检：取某个控件的矩形（按文字前缀找）。</summary>
        internal static Rectangle ProbeRectOfTextForTest(Form f, string prefix)
        {
            try
            {
                var sf = f as SettingsForm;
                if (sf == null || sf._groupHost == null) return Rectangle.Empty;
                for (int g = 0; g < sf._groupHost.Controls.Count; g++)
                {
                    var gb = sf._groupHost.Controls[g];
                    for (int i = 0; i < gb.Controls.Count; i++)
                    {
                        var c = gb.Controls[i];
                        if (c.Text != null && c.Text.StartsWith(prefix)) return c.Bounds;
                    }
                }
            }
            catch { }
            return Rectangle.Empty;
        }

        /// <summary>仅供自检 / 开发期截图：取当前窗体的 DPI 缩放系数。</summary>
        internal static float ProbeUiScaleOf(Form f)
        {
            var sf = f as SettingsForm;
            return sf == null ? 1f : sf.UiScale;
        }

        /// <summary>
        /// 仅供自检：把整个窗口里所有控件的矩形拼成一个字符串。
        /// 同一尺寸下反复重排，这个指纹必须【完全一致】——
        /// 不一致就说明布局不幂等（"越拉越偏 / 一帧一抖"的根源）。
        /// </summary>
        internal static string ProbeLayoutFingerprint(Form f)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var sf = f as SettingsForm;
                if (sf == null || sf._groupHost == null) return "";
                sb.Append("host=").Append(sf._groupHost.Width).Append(',').Append(sf._groupHost.Height)
                  .Append(" cols=").Append(sf._groupHost.CurrentColumns).Append(';');
                for (int i = 0; i < sf._innerLayouts.Count; i++)
                {
                    var c = sf._innerLayouts[i].Control;
                    if (c == null) continue;
                    sb.Append(c.GetType().Name).Append(':')
                      .Append(c.Left).Append(',').Append(c.Top).Append(',')
                      .Append(c.Width).Append(',').Append(c.Height).Append(';');
                }
            }
            catch { }
            return sb.ToString();
        }

        /// <summary>仅供自检：两列阈值（物理像素）。</summary>
        internal static int ProbeTwoColumnThresholdPx(Form f)
        {
            var sf = f as SettingsForm;
            float s = sf == null ? 1f : sf.UiScale;
            return (int)Math.Round(ResponsiveGroupPanel.TwoColumnThreshold * s);
        }

        /// <summary>
        /// 仅供自检：强行按指定可用宽度排一次列，返回最终列数。
        /// 用来验证"列数切换带不带回滞"。
        /// </summary>
        internal static int ProbeRelayoutColumns(Form f, int available)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groupHost == null) return -1;
            try
            {
                sf._groupHost.Relayout(available);
                return sf._groupHost.CurrentColumns;
            }
            catch { return -1; }
        }

        /// <summary>
        /// 仅供自检：把某个分组里所有控件的"是否完整落在分组客户区内"倒出来。
        ///
        /// 用户反馈："允许拖动全屏无边框窗口这个选项，在设置窗口较小时会被遮挡"。
        /// 这类问题（被压窄、被推到框外、被同行控件覆盖）光看截图很难定位，
        /// 但用"控件矩形是否越出父容器客户区"来判定就是确定的。
        /// </summary>
        internal sealed class GroupClipReport
        {
            public int Total;
            public int Clipped;
            public string Detail = "";
        }

        internal static GroupClipReport ProbeGroupClipping(Form f, int groupIndex)
        {
            var rep = new GroupClipReport();
            try
            {
                var sf = f as SettingsForm;
                if (sf == null || sf._groupHost == null) return rep;
                if (groupIndex < 0 || groupIndex >= sf._groupHost.Controls.Count) return rep;
                var gb = sf._groupHost.Controls[groupIndex] as Control;
                if (gb == null) return rep;

                var client = gb.ClientRectangle;
                for (int i = 0; i < gb.Controls.Count; i++)
                {
                    var c = gb.Controls[i];
                    if (c == null) continue;
                    rep.Total++;
                    bool ok = c.Left >= 0 && c.Top >= 0
                           && c.Right <= client.Width && c.Bottom <= client.Height;
                    if (ok) continue;

                    // 只报"真的会被切掉"的：文字型控件允许左右各 1px 的取整误差
                    int over = Math.Max(Math.Max(-c.Left, -c.Top),
                              Math.Max(c.Right - client.Width, c.Bottom - client.Height));
                    if (over <= 1) continue;

                    rep.Clipped++;
                    rep.Detail += "「" + (string.IsNullOrEmpty(c.Text) ? c.GetType().Name : ShortText(c.Text))
                                + "」L=" + c.Left + " T=" + c.Top + " W=" + c.Width + " H=" + c.Height
                                + " 越界=" + over + "px  ";
                }
            }
            catch (Exception ex) { rep.Detail += "异常 " + ex.GetType().Name + " "; }
            return rep;
        }

        private static string ShortText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "").Replace("\n", " ");
            return s.Length <= 16 ? s : s.Substring(0, 16) + "…";
        }

        /// <summary>
        /// 仅供自检：检查分组里每个"文字型"控件的文字是否真的放得下。
        ///
        /// 为什么需要单独一条：控件矩形没越界 ≠ 文字没被切。
        /// 复选框 AutoSize=false + 固定高度时，宽度不够会让文字【折行后下半行被裁掉】，
        /// 而矩形本身完全合法 —— 这正是用户说的"选项被遮挡"。
        /// </summary>
        internal static string ProbeTextFitting(Form f, int groupIndex)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var sf = f as SettingsForm;
                if (sf == null || sf._groupHost == null) return "";
                if (groupIndex < 0 || groupIndex >= sf._groupHost.Controls.Count) return "";
                var gb = sf._groupHost.Controls[groupIndex] as Control;
                if (gb == null) return "";

                for (int i = 0; i < gb.Controls.Count; i++)
                {
                    var c = gb.Controls[i];
                    if (!(c is CheckBox) && !(c is Label)) continue;
                    if (string.IsNullOrEmpty(c.Text)) continue;

                    int textAvail = c.Width - (c is CheckBox ? Fields.Sc(26) : Fields.Sc(4));
                    if (textAvail < 20) textAvail = 20;

                    Size one, wrapped;
                    try
                    {
                        one = TextRenderer.MeasureText(c.Text, c.Font,
                            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                        wrapped = TextRenderer.MeasureText(c.Text, c.Font,
                            new Size(textAvail, 0),
                            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    }
                    catch { continue; }

                    // 单行放不下、而且高度不足以容纳折行后的文字 → 文字会被裁掉
                    bool clipped = wrapped.Width > textAvail && wrapped.Height > c.Height
                                || one.Width > textAvail && wrapped.Height > c.Height;
                    if (!clipped) continue;

                    sb.AppendLine("「" + ShortText(c.Text) + "」W=" + c.Width + " H=" + c.Height
                                + " 文本可用=" + textAvail
                                + " 单行需=" + one.Width + " 折行需高=" + wrapped.Height);
                }
            }
            catch (Exception ex) { sb.AppendLine("异常 " + ex.GetType().Name); }
            return sb.ToString();
        }

        /// <summary>仅供自检：按 ID 选中某个外框颜色（等价于用户点色板上的那一格）。</summary>
        internal void ProbeSelectFrameColor(string colorId)
        {
            SelectId(_cmbFrameColor, colorId);
            SyncPreview();
        }

        /// <summary>
        /// 仅供自检：色板 / 样式画廊的"看得见的状态"快照。
        ///
        /// 用户提的两条要求都必须能被断言到：
        ///   ① "进入设置时即展示所有可选项" → 格数 == 调色板/样式总数，且真的画出了像素；
        ///   ② "已选样式实时可见、状态清晰"   → 选中格的强调色像素数 > 0，且矩形在控件内。
        /// </summary>
        internal sealed class FramePickerProbe
        {
            public int ColorCells;
            public int StyleCells;
            public Size GridSize;
            public Size GallerySize;
            public bool GridInsideGroup;
            public bool GalleryInsideGroup;
            public int GridInk;
            public int GalleryInk;
            public int GridAccentInk;
            public int GalleryAccentInk;
            public Rectangle GridSelectedCell;
            public Rectangle GallerySelectedCell;
        }

        internal FramePickerProbe ProbeFramePickers()
        {
            var p = new FramePickerProbe();
            p.ColorCells = _gridColor.CellCountForTest;
            p.StyleCells = _galleryStyle.CellCountForTest;
            p.GridSize = _gridColor.Size;
            p.GallerySize = _galleryStyle.Size;
            p.GridInsideGroup = InsideParentClient(_gridColor);
            p.GalleryInsideGroup = InsideParentClient(_galleryStyle);
            p.GridInk = CountInkOutsideBackground(_gridColor, Design.Surface);
            p.GalleryInk = CountInkOutsideBackground(_galleryStyle, Design.Surface);
            p.GridAccentInk = CountAccentInk(_gridColor);
            p.GalleryAccentInk = CountAccentInk(_galleryStyle);
            p.GridSelectedCell = _gridColor.SelectedCellRectForTest;
            p.GallerySelectedCell = _galleryStyle.SelectedCellRectForTest;
            return p;
        }

        private static bool InsideParentClient(Control c)
        {
            if (c == null || c.Parent == null) return false;
            var pr = c.Parent.ClientRectangle;
            return c.Left >= 0 && c.Top >= 0
                && c.Right <= pr.Width + 1 && c.Bottom <= pr.Height + 1;
        }

        /// <summary>
        /// 仅供自检：验证"悬停补间"真的在过渡（而不是瞬间跳变）。
        /// 返回 false 表示补间没跑起来 —— 那 Win11 那套动效就是形同虚设。
        /// </summary>
        internal static bool ProbeHoverTweenAnimates(out float start, out float end)
        {
            start = 0f; end = 0f;
            try
            {
                using (var b = new FlatButton())
                {
                    b.ProbeSetHover(true);
                    start = b.ProbeHoverValue;

                    // 等它收敛（或最多 900ms）—— 用固定 260ms 在忙的机器上会偶发不到 1。
                    long until = Environment.TickCount64 + 900;
                    while (Environment.TickCount64 < until)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(8);
                        if (b.ProbeHoverValue > 0.98f) break;
                    }
                    end = b.ProbeHoverValue;
                    return start < 0.3f && end > 0.9f;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// 仅供自检：把整个设置窗口渲染成一张位图，返回几个"美化是否到位"的统计量。
        ///
        /// 为什么必须真的渲染一遍：美化类改动靠断言"属性对不对"验证不了，
        /// 而"某一块整片是黑的"（历史上真出现过：自绘复选框没铺底 → 一整排黑色条）
        /// 这类问题只有数像素才能发现。
        /// </summary>
        internal sealed class RenderStats
        {
            public int Width, Height;
            public int DistinctColors;
            public int BlackPixels;       // 近黑像素：正常 UI 里应当几乎为 0
            public int AccentPixels;      // 强调色像素：按钮 / 竖条 / 选中态
            public int FrameColorPixels;  // 外框色像素：色板 + 画廊缩略图 + 预览框
            public double MeanBrightness;
        }

        internal static RenderStats ProbeFormRender(Form f, string frameColorId)
        {
            var st = new RenderStats();
            try
            {
                if (f.Width <= 0 || f.Height <= 0) return st;
                st.Width = f.Width; st.Height = f.Height;

                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));

                    var seen = new System.Collections.Generic.HashSet<int>();
                    var want = FrameColors.Resolve(frameColorId);
                    long bright = 0; int n = 0;
                    for (int y = 0; y < bmp.Height; y += 2)
                        for (int x = 0; x < bmp.Width; x += 2)
                        {
                            var px = bmp.GetPixel(x, y);
                            seen.Add(px.ToArgb());
                            bright += px.R + px.G + px.B;
                            n++;
                            if (px.R < 24 && px.G < 24 && px.B < 24) st.BlackPixels++;
                            if (Math.Abs(px.R - Design.Accent.R) <= 18
                                && Math.Abs(px.G - Design.Accent.G) <= 18
                                && Math.Abs(px.B - Design.Accent.B) <= 18) st.AccentPixels++;
                            if (Math.Abs(px.R - want.R) <= 18
                                && Math.Abs(px.G - want.G) <= 18
                                && Math.Abs(px.B - want.B) <= 18) st.FrameColorPixels++;
                        }
                    st.DistinctColors = seen.Count;
                    st.MeanBrightness = n == 0 ? 0 : (bright / 3.0) / n;
                }
            }
            catch { }
            return st;
        }

        /// <summary>仅供自检：分别统计三块"外框色展示区"里画出的外框色像素。</summary>
        internal int[] ProbeFrameColorInk(string colorId)
        {
            var want = FrameColors.Resolve(colorId);
            return new int[]
            {
                CountExactColor(_gridColor, want),
                CountExactColor(_galleryStyle, want),
                CountExactColor(_pnlPreview, want),
            };
        }

        private static int CountExactColor(Control c, Color want)
        {
            try
            {
                if (c == null || c.Width <= 0 || c.Height <= 0) return -1;
                using (var bmp = new Bitmap(c.Width, c.Height))
                {
                    c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                    int n = 0;
                    for (int y = 0; y < bmp.Height; y++)
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            var px = bmp.GetPixel(x, y);
                            if (Math.Abs(px.R - want.R) <= 18
                                && Math.Abs(px.G - want.G) <= 18
                                && Math.Abs(px.B - want.B) <= 18) n++;
                        }
                    return n;
                }
            }
            catch { return -1; }
        }

        /// <summary>仅供自检：诊断用 —— 返回色板选中格中心像素的 ARGB 与格矩形。</summary>
        internal string ProbeGridDiagnostic()
        {
            try
            {
                var r = _gridColor.SelectedCellRectForTest;
                using (var bmp = new Bitmap(Math.Max(1, _gridColor.Width), Math.Max(1, _gridColor.Height)))
                {
                    _gridColor.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    int x = Math.Min(bmp.Width - 1, r.X + r.Width / 2);
                    int y = Math.Min(bmp.Height - 1, r.Y + r.Height / 2);
                    var px = bmp.GetPixel(x, y);
                    var tl = bmp.GetPixel(0, 0);
                    return "格=" + r + " 尺寸=" + _gridColor.Width + "x" + _gridColor.Height
                         + " 中心像素=" + px.R + "," + px.G + "," + px.B + "," + px.A
                         + " 左上像素=" + tl.R + "," + tl.G + "," + tl.B + "," + tl.A
                         + " Enabled=" + _gridColor.Enabled
                         + " chkFrame=" + _chkFrame.Checked
                         + " 父Enabled=" + (_gridColor.Parent != null ? _gridColor.Parent.Enabled.ToString() : "无父")
                         + " SelectedId=" + _gridColor.SelectedId
                         + " 格数=" + _gridColor.CellCountForTest;
                }
            }
            catch (Exception ex) { return "异常 " + ex.GetType().Name + " " + ex.Message; }
        }

        private static int CountInkOutsideBackground(Control c, Color bg)
        {
            try
            {
                if (c.Width <= 0 || c.Height <= 0) return -1;
                using (var bmp = new Bitmap(c.Width, c.Height))
                {
                    c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                    int n = 0;
                    for (int y = 0; y < bmp.Height; y += 2)
                        for (int x = 0; x < bmp.Width; x += 2)
                        {
                            var px = bmp.GetPixel(x, y);
                            if (Math.Abs(px.R - bg.R) > 8 || Math.Abs(px.G - bg.G) > 8
                                || Math.Abs(px.B - bg.B) > 8) n++;
                        }
                    return n;
                }
            }
            catch { return -1; }
        }

        private static int CountAccentInk(Control c)
        {
            var want = Design.Accent;
            try
            {
                if (c.Width <= 0 || c.Height <= 0) return -1;
                using (var bmp = new Bitmap(c.Width, c.Height))
                {
                    c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                    int n = 0;
                    for (int y = 0; y < bmp.Height; y++)
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            var px = bmp.GetPixel(x, y);
                            if (Math.Abs(px.R - want.R) <= 24
                                && Math.Abs(px.G - want.G) <= 24
                                && Math.Abs(px.B - want.B) <= 24) n++;
                        }
                    return n;
                }
            }
            catch { return -1; }
        }

        /// <summary>仅供自检：按 ID 选中某个外框样式（等价于用户点画廊上的那一格）。</summary>
        internal void ProbeSelectFrameStyle(string styleId)
        {
            SelectId(_cmbFrameStyle, styleId);
            SyncPreview();
        }

        /// <summary>仅供自检：设置粗细 / 圆角 / 总开关。</summary>
        internal void ProbeSetFrameShape(int thickness, int radius, bool enabled)
        {
            _numFrameThick.Value = Clamp(thickness, (int)_numFrameThick.Minimum, (int)_numFrameThick.Maximum);
            _numFrameRadius.Value = Clamp(radius, (int)_numFrameRadius.Minimum, (int)_numFrameRadius.Maximum);
            _chkFrame.Checked = enabled;
            SyncPreview();
        }

        /// <summary>仅供自检：把界面上选的外观写进配置（等价于点「保存并生效」的核心一步）。</summary>
        internal void ProbeCaptureInto(AppConfig target)
        {
            target.ShowActivationFrame = _chkFrame.Checked;
            // 刻意保留 target 里已有的值作为回落：探针若读不到选中项就悄悄写成默认值，
            // 断言反而会通过 —— 那是自检在骗自己，不是产品行为。
            target.FrameColorId = SelectedId(_cmbFrameColor, target.FrameColorId);
            target.FrameStyleId = SelectedId(_cmbFrameStyle, target.FrameStyleId);
            target.FrameThickness = (int)_numFrameThick.Value;
            target.FrameRadius = (int)_numFrameRadius.Value;
        }

        /// <summary>仅供自检：暴露左键动作下拉框。</summary>
        internal Control ProbeComboLeft { get { return _cmbLeft; } }

        /// <summary>仅供自检：取到的图标字体族（null = 两个字体都没有）。</summary>
        internal static FontFamily IconFamilyForTest { get { return Icons.Family; } }

        /// <summary>仅供自检：用到的全部字形（顺序与 SelfTest 里的名称表一致）。</summary>
        internal static string[] IconGlyphsForTest
        {
            get
            {
                return new string[]
                {
                    Glyph.Keyboard, Glyph.Move, Glyph.Color,
                    Glyph.Speed, Glyph.Shield, Glyph.Chevron,
                    Glyph.Help, Glyph.Info,
                };
            }
        }

        /// <summary>
        /// 仅供自检：把一个字形按指定像素大小渲染出来，数"有多少像素不是背景"。
        /// 缺字形 → 空白 → 墨水为 0，于是"静默变空白"能被抓出来。
        /// </summary>
        internal static int MeasureGlyphInk(string glyph, float px)
        {
            try
            {
                using (var f = Icons.At(px))
                {
                    if (f == null) return -1;
                    int size = (int)Math.Ceiling(px * 2);
                    using (var bmp = new Bitmap(size, size))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.White);
                            g.TextRenderingHint =
                                System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            g.DrawString(glyph, f, Brushes.Black, 0, 0);
                        }
                        int ink = 0;
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            for (int x = 0; x < bmp.Width; x++)
                            {
                                var c = bmp.GetPixel(x, y);
                                if (c.R < 200 || c.G < 200 || c.B < 200) ink++;
                            }
                        }
                        return ink;
                    }
                }
            }
            catch { return -1; }
        }

        /// <summary>
        /// 仅供自检：把一个开关渲染出来，回报"轨道中心"和"滑块中心"的颜色。
        ///
        /// 为什么必须像素级验证：开关是自绘的，一旦画错（轨道透明、滑块没画、
        /// 颜色取错）控件依然"存在且可点"，只有截图才看得出来。
        /// 这里把"开 = 强调色轨道 + 白滑块"和"关 = 中性灰轨道"钉成断言。
        /// </summary>
        internal static string ProbeSwitchColors(Form f, bool on)
        {
            try
            {
                var sf = f as SettingsForm;
                if (sf == null) return "";
                FlatCheck target = null;
                for (int i = 0; i < sf._innerLayouts.Count; i++)
                {
                    var fc = sf._innerLayouts[i].Control as FlatCheck;
                    if (fc != null && fc.Width > 40 && fc.Enabled) { target = fc; break; }
                }
                if (target == null) return "(没找到可用的开关)";

                bool old = target.Checked;
                target.Checked = on;
                Application.DoEvents();

                using (var bmp = new Bitmap(Math.Max(1, target.Width), Math.Max(1, target.Height)))
                {
                    target.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));

                    int trackW = CheckTextChrome - Fields.Sc(10);
                    int trackH = (int)Math.Round(trackW * 0.56);
                    int cy = Math.Min(bmp.Height - 1, trackH / 2);
                    // 轨道中心：取"没有滑块"的那一端 —— 开时取左端、关时取右端
                    int trackX = on ? Math.Max(0, trackW / 5) : Math.Max(0, trackW - trackW / 5);
                    var track = bmp.GetPixel(trackX, cy);
                    // 滑块中心：开在右端、关在左端
                    int knobX = on ? Math.Max(0, trackW - trackH / 2) : Math.Max(0, trackH / 2);
                    var knob = bmp.GetPixel(knobX, cy);

                    target.Checked = old;
                    Application.DoEvents();

                    return "轨道=" + track.R + "," + track.G + "," + track.B
                         + " 滑块=" + knob.R + "," + knob.G + "," + knob.B;
                }
            }
            catch (Exception ex) { return "异常 " + ex.GetType().Name; }
        }

        /// <summary>仅供自检：样式画廊的几何（列数 / 格高 / 间隙 / 控件高）。</summary>
        internal static string ProbeGalleryGeometry(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null) return "";
            try
            {
                int n = FrameStyles.All.Length;
                int cols = sf._galleryStyle.Columns;
                if (cols <= 0) cols = 5;
                int rows = (n + cols - 1) / cols;
                return "样式数=" + n + " 列=" + cols + " 行=" + rows
                     + " 格高=" + Fields.Sc(46) + " 间隙=" + Fields.Sc(6)
                     + " 需要高=" + (rows * Fields.Sc(46) + (rows - 1) * Fields.Sc(6))
                     + " 控件高=" + sf._galleryStyle.Height;
            }
            catch (Exception ex) { return "异常 " + ex.GetType().Name; }
        }

        /// <summary>仅供自检：颜色色板的几何（查"颜色是不是被裁掉了"）。</summary>
        internal static string ProbeColorGridGeometry(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null) return "";
            try
            {
                int n = FrameColors.All.Length;
                int cols = sf._gridColor.Columns;
                if (cols <= 0) cols = 5;
                int rows = (n + cols - 1) / cols;
                // ★ 色板的格子是 22 设计px（画廊是 46）—— 两个控件的几何完全不同，
                //   用画廊的尺寸套色板会算出"需要 196、只有 100"的假告警（我第一版就是）。
                return "颜色数=" + n + " 列=" + cols + " 行=" + rows
                     + " 格=" + Fields.Sc(22) + " 间隙=" + Fields.Sc(6)
                     + " 需要高=" + (rows * Fields.Sc(22) + (rows - 1) * Fields.Sc(6))
                     + " 控件高=" + sf._gridColor.Height;
            }
            catch (Exception ex) { return "异常 " + ex.GetType().Name; }
        }

        /// <summary>仅供自检：色板高度是否够放所有颜色。</summary>
        internal static bool ProbeColorGridFits(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null) return false;
            int n = FrameColors.All.Length;
            int cols = sf._gridColor.Columns;
            if (cols <= 0) cols = 5;
            int rows = (n + cols - 1) / cols;
            int need = rows * Fields.Sc(22) + (rows - 1) * Fields.Sc(6);   // 色板格子 22
            return sf._gridColor.Height >= need;
        }

        /// <summary>仅供自检：样式画廊高度是否够放所有样式。</summary>
        internal static bool ProbeGalleryFits(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null) return false;
            int n = FrameStyles.All.Length;
            int cols = sf._galleryStyle.Columns;
            if (cols <= 0) cols = 5;
            int rows = (n + cols - 1) / cols;
            int need = rows * Fields.Sc(46) + (rows - 1) * Fields.Sc(6);
            return sf._galleryStyle.Height >= need;
        }

        /// <summary>仅供自检：当前 UI 缩放系数。</summary>
        internal static float UiScaleForTest { get { return CurrentUiScale; } }

        /// <summary>仅供自检：程序集版本号（界面显示的就是它）。</summary>
        internal static string AppVersionForTest { get { return AppInfo.Version; } }

        /// <summary>
        /// 仅供自检：分组总数。
        /// 用例里绝不能写死 6 —— 本轮加了「关于 / 帮助」两组，
        /// 而三处裁切检查还写死 gi&lt;6，新分组就【完全没被检查到】。
        /// 探针取实际数量，以后再加分组自动覆盖。
        /// </summary>
        internal static int ProbeGroupCount(Form f)
        {
            var sf = f as SettingsForm;
            return (sf == null || sf._groups == null) ? 0 : sf._groups.Length;
        }

        /// <summary>
        /// 仅供自检：第 i 个分组是否【真的在窗体的可视树里】。
        ///
        /// ★ 为什么必须单独查这一条 ★
        ///   本轮踩的坑：新增了「关于 / 帮助」两个分组，`_groups` 里有它们、
        ///   几何检查也全过，但分组宿主是 `new ResponsiveGroupPanel(new[]{g1..g6})`
        ///   —— 写死 6 个，新分组【从未被加进界面】。对象在、检查过、用户看不见。
        ///   所有"按 _groups 数组做检查"的用例都抓不到这种情况，
        ///   必须从窗体往下走一遍父链。
        /// </summary>
        internal static bool ProbeGroupInTree(Form f, int index)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groups == null) return false;
            if (index < 0 || index >= sf._groups.Length) return false;

            Control c = sf._groups[index];
            for (int guard = 0; guard < 16 && c != null; guard++)
            {
                if (c == sf) return true;
                c = c.Parent;
            }
            return false;
        }

        /// <summary>仅供自检：分组内每个控件的实际矩形（查"为什么挤在一起"）。</summary>
        internal static string ProbeGroupRects(Form f, int index)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groups == null) return "";
            if (index < 0 || index >= sf._groups.Length) return "";

            var gb = sf._groups[index];
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < gb.Controls.Count; i++)
            {
                var c = gb.Controls[i];
                if (c == null) continue;
                sb.Append(ShortText(c)).Append('(').Append(c.Left).Append(',').Append(c.Top)
                  .Append(' ').Append(c.Width).Append('x').Append(c.Height).Append(") ");
            }
            return sb.ToString();
        }

        /// <summary>仅供自检：分组内互相重叠的控件对（返回可读描述，空串表示没有重叠）。</summary>
        internal static string ProbeGroupOverlaps(Form f, int index)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groups == null) return "";
            if (index < 0 || index >= sf._groups.Length) return "";

            var gb = sf._groups[index];
            var sb = new System.Text.StringBuilder();
            var cs = gb.Controls;
            for (int i = 0; i < cs.Count; i++)
            {
                for (int j = i + 1; j < cs.Count; j++)
                {
                    var a = cs[i];
                    var b = cs[j];
                    if (a == null || b == null || !a.Visible || !b.Visible) continue;
                    var ra = a.Bounds;
                    var rb = b.Bounds;
                    var hit = Rectangle.Intersect(ra, rb);
                    // 允许 1px 的边界接触（相邻控件的边界会贴在一起）
                    if (hit.Width > 1 && hit.Height > 1)
                    {
                        sb.Append('[').Append(i).Append(':').Append(ShortText(a))
                          .Append(" x ").Append(j).Append(':').Append(ShortText(b))
                          .Append(" 重叠 ").Append(hit.Width).Append('x').Append(hit.Height)
                          .Append("] ");
                    }
                }
            }
            return sb.ToString();
        }

        private static string ShortText(Control c)
        {
            string t = c.Text;
            if (string.IsNullOrEmpty(t)) t = c.GetType().Name;
            return t.Length <= 12 ? t : t.Substring(0, 12);
        }

        /// <summary>仅供自检：侧栏当前高亮项 / 条目数。</summary>
        internal static int ProbeSidebarActive(Form f)
        {
            var sf = f as SettingsForm;
            return (sf == null || sf._sidebar == null) ? -1 : sf._sidebar.ActiveForTest;
        }

        internal static int ProbeSidebarCount(Form f)
        {
            var sf = f as SettingsForm;
            return (sf == null || sf._sidebar == null) ? -1 : sf._sidebar.CountForTest;
        }

        /// <summary>
        /// 仅供自检：往滚动区发【真实的滚轮消息】，返回实际滚动偏移。
        ///
        /// ★ 为什么必须走真实消息 ★
        ///   上一轮 T74 是"直接改 AutoScrollPosition + 手动调 SyncActive()"，
        ///   那只验证了 SyncActive 的算法，**完全没验证 Scroll 事件有没有接上**。
        ///   用户报"滚到启动与权限，侧栏还停在触发方式" —— 正好落在那个盲区里。
        ///   这里发 WM_MOUSEWHEEL，走的就是用户滚轮那条路。
        /// </summary>
        internal static int ProbeWheelScroll(Form f, int notches)
        {
            var sf = f as SettingsForm;
            if (sf == null) return -1;
            try
            {
                var pt = sf._scrollHost.PointToScreen(
                    new Point(sf._scrollHost.ClientSize.Width / 2,
                              sf._scrollHost.ClientSize.Height / 2));
                long lp = ((long)pt.X & 0xFFFF) | (((long)pt.Y & 0xFFFF) << 16);

                for (int i = 0; i < Math.Abs(notches); i++)
                {
                    int delta = notches > 0 ? 120 : -120;
                    long wp = ((long)(ushort)delta) << 16;
                    Native.SendMessageW(sf._scrollHost.Handle, 0x020A,
                        new IntPtr((int)wp), new IntPtr((int)lp));
                }
                Application.DoEvents();
                return -sf._scrollHost.AutoScrollPosition.Y;
            }
            catch (Exception ex) { Log.Error(ex); return -2; }
        }

        /// <summary>仅供自检：滚动区的尺寸诊断（查"为什么滚不动"）。</summary>
        internal static string ProbeScrollInfo(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null) return "";
            try
            {
                return "窗体客户区=" + sf.ClientSize.Width + "x" + sf.ClientSize.Height
                     + " 滚动区=" + sf._scrollHost.ClientSize.Width + "x" + sf._scrollHost.ClientSize.Height
                     + " 显示矩形=" + sf._scrollHost.DisplayRectangle
                     + " 分组宿主高=" + (sf._groupHost == null ? -1 : sf._groupHost.Height)
                     + " AutoScroll=" + sf._scrollHost.AutoScroll
                     + " 可见=" + sf._scrollHost.Visible;
            }
            catch (Exception ex) { return "异常 " + ex.GetType().Name; }
        }

        /// <summary>仅供自检：SyncActive 的诊断（被调次数 / 最后算出的 top 与 best）。</summary>
        internal static string ProbeSyncDiag(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._sidebar == null) return "";
            return "SyncActive被调=" + sf._sidebar.SyncCountForTest
                 + " 末次top=" + sf._sidebar.LastTopForTest
                 + " 末次best=" + sf._sidebar.LastBestForTest
                 + " 当前滚动=" + (-sf._scrollHost.AutoScrollPosition.Y);
        }

        /// <summary>仅供自检：把滚动区滚到指定偏移，并驱动一次同步。返回实际生效的偏移。</summary>
        internal static int ProbeScrollTo(Form f, int y)
        {
            var sf = f as SettingsForm;
            if (sf == null) return -1;
            try
            {
                sf._scrollHost.AutoScrollPosition = new Point(0, Math.Max(0, y));
                sf._scrollHost.PerformLayout();
                sf._sidebar.SyncActive();
                return -sf._scrollHost.AutoScrollPosition.Y;
            }
            catch (Exception ex) { Log.Error(ex); return -2; }
        }

        /// <summary>仅供自检：第 i 个分组在滚动内容里的顶端（相对滚动区）。</summary>
        internal static int ProbeGroupTop(Form f, int index)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groups == null) return -1;
            if (index < 0 || index >= sf._groups.Length) return -1;
            return sf._groups[index].Top;
        }

        /// <summary>仅供自检：帮助面板。</summary>
        internal HelpPanel ProbeHelpPanel { get { return _helpPanel; } }

        /// <summary>
        /// 仅供自检："关于"分组里的全部文字拼起来。
        /// 开发者 / 版本 / 更新时间 / 开发工具这四项必须真的出现在界面上 ——
        /// 光有 AppInfo 常量而没接到 UI 上，是这类"补信息"任务最容易翻车的地方。
        /// </summary>
        internal string ProbeAboutText
        {
            get
            {
                if (_groups == null) return "";

                // ★ 按标题找，不写索引 ★
                //   写索引（原来是 _groups[7]）在"调整分组顺序"时必然错位 ——
                //   本轮刚把「帮助」挪到「关于」前面，就是活生生的例子。
                GroupBox gb = null;
                for (int i = 0; i < _groups.Length; i++)
                {
                    if (_groups[i] != null && _groups[i].Text != null
                        && _groups[i].Text.Contains("关于"))
                    {
                        gb = _groups[i];
                        break;
                    }
                }
                if (gb == null) return "";

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < gb.Controls.Count; i++)
                {
                    var c = gb.Controls[i];
                    if (c != null && !string.IsNullOrEmpty(c.Text)) sb.Append(c.Text).Append(' ');
                }
                return sb.ToString();
            }
        }

        /// <summary>仅供自检：暴露右键动作下拉框。</summary>
        internal Control ProbeComboRight { get { return _cmbRight; } }

        /// <summary>仅供自检：右键下拉框吞掉/放行滚轮的次数。</summary>
        internal static int ProbeWheelSwallowed(Control c)
        {
            var fc = c as FlatCombo;
            return fc == null ? -1 : fc.WheelSwallowedForTest;
        }

        internal static int ProbeWheelForwarded(Control c)
        {
            var fc = c as FlatCombo;
            return fc == null ? -1 : fc.WheelForwardedForTest;
        }

        /// <summary>仅供自检：暴露"长按阈值"数字框。</summary>
        internal Control ProbeNumHold { get { return _numHold; } }

        /// <summary>仅供自检：暴露第一个分组。</summary>
        internal Control ProbeGroup1
        {
            get
            {
                if (_groupHost == null || _groupHost.Controls.Count == 0) return null;
                return _groupHost.Controls[0];
            }
        }

        /// <summary>
        /// 仅供自检：内置的默认客户区高度（按当前 DPI 换算后的物理像素）。
        /// 断言"窗口显示后不会比它更大"，用来锁死 #33（默认尺寸过大）。
        /// </summary>
        internal int ProbeDefaultClientHeight()
        {
            return S(DefaultClientHeightDesign);
        }

        /// <summary>内置默认客户区高度（设计稿像素）。BuildUi 与自检共用同一个数。</summary>
        private const int DefaultClientHeightDesign = 640;

        /// <summary>仅供自检：确认所有分组真的挂到了窗体上（防止再犯"设置窗口是空的"）。</summary>
        /// 注意分组现在挂在 _groupHost（TableLayoutPanel）里，
        /// 而 _groupHost 挂在 _scrollHost 上、_scrollHost 挂在窗体上 ——
        /// 所以不能只看 f.Controls，要顺着这条链找。
        /// </summary>
        internal static bool VerifyLayout(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groupHost == null) return false;

            // _groupHost 必须真的在窗体上（顺着父子链往上走）
            if (!IsInFormChain(f, sf._groupHost)) return false;

            int groups = 0;
            for (int i = 0; i < sf._groupHost.Controls.Count; i++)
            {
                if (sf._groupHost.Controls[i] is GroupBox) groups++;
            }
            return groups >= 6;
        }

        /// <summary>控件是否（间接）挂在窗体上。</summary>
        private static bool IsInFormChain(Form f, Control c)
        {
            var p = c;
            while (p != null)
            {
                if (p == f) return true;
                p = p.Parent;
            }
            return false;
        }

        /// <summary>
        /// 仅供自检：设置窗口必须可调整大小（用户需求），而且不能是"能拉但内容不跟着走"。
        /// 这里断言三件事：边框可缩放、最大/最小化按钮可用、最小尺寸有意义。
        /// </summary>
        internal static bool VerifyResizable(Form f)
        {
            if (f.FormBorderStyle != FormBorderStyle.Sizable) return false;
            if (!f.MaximizeBox) return false;
            if (f.MinimumSize.Width < 400 || f.MinimumSize.Height < 300) return false;
            return true;
        }

        /// <summary>
        /// 自检/压测里用来触发"两列模式"的探测宽度。
        ///
        /// ★ 必须跟着 ResponsiveGroupPanel.TwoColumnThreshold 走，不要另写一个数。
        ///   早先测试里写死 `GroupWidth * 2 + 40`（=1112），
        ///   而阈值后来从 1160 提到 1240，
        ///   于是断言"拉宽会变两列"直接失败 —— 是测试自己过期了，
        ///   但报告上看起来像布局坏了，白白浪费一轮排查。
        ///   用这个常量统一口径，以后调阈值不会再漏改测试。
        /// </summary>
        internal const int TwoColumnProbeWidth = ResponsiveGroupPanel.TwoColumnThreshold + 60;

        /// <summary>左侧导航栏宽度（设计稿像素）。</summary>
        internal const int SidebarDesignWidth = 176;

        /// <summary>
        /// 仅供自检：要让【内容区】达到两列，窗口客户区得开多宽（物理像素）。
        ///
        /// ★ 加了侧栏之后不能再用 TwoColumnProbeWidth 当客户区宽度 ★
        ///   侧栏是 Dock=Left，会先吃掉 176 设计像素，剩下的才给 _scrollHost。
        ///   所以窗口客户区必须 = 侧栏宽 + 内容区所需宽，否则自检会把
        ///   "窗口够宽了"误判成"还不够"，断言就永远红着。
        /// </summary>
        internal static int ClientWidthForTwoColumns
        {
            get
            {
                int s = (int)Math.Round(CurrentUiScale);
                if (s < 1) s = 1;
                return (TwoColumnProbeWidth + 40 + SidebarDesignWidth) * s;
            }
        }

        /// <summary>
        /// 仅供自检：确认"拉宽真的有意义"。
        ///
        /// ★ 这条是本轮用户反馈的核心："设置页面被莫名拉宽，拉宽的部分没增加任何内容，
        ///   这种拉宽毫无意义"。所以断言的不是"有没有 Anchor"，而是
        ///   **宽度真的涨上去之后，分组列数会不会从 1 变 2、内部输入框会不会真的变宽**。
        ///
        /// 光断言 Anchor 是不够的 —— 早先每个分组都老老实实设了 Left|Right，
        /// 测试绿着，用户体验却是"右边空一大片"。
        /// </summary>
        internal static bool VerifyAnchors(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groupHost == null) return false;

            // ★ 这些探测宽度必须和真实布局走同一套缩放，
            //   否则自检是在"按 100% DPI 的宽度"去测一个"按 125% 排出来的界面"，
            //   测出来的结论对用户实际看到的界面没有意义。
            int gw = sf.GroupWidth;

            // 1) 窄窗口必须是 1 列
            sf._groupHost.Width = gw;
            sf._groupHost.Relayout(gw);
            int narrowCols = sf._groupHost.CurrentColumns;
            if (narrowCols != 1) return false;

            // 2) 宽窗口必须切成 2 列
            int wide = (int)Math.Round(TwoColumnProbeWidth * CurrentUiScale);
            sf._groupHost.Width = wide;
            sf._groupHost.Relayout(wide);
            int wideCols = sf._groupHost.CurrentColumns;
            if (wideCols != 2) return false;

            // 3) 回窄窗口必须能退回 1 列（来回拉不能卡在 2 列）
            sf._groupHost.Width = gw;
            sf._groupHost.Relayout(gw);
            if (sf._groupHost.CurrentColumns != 1) return false;

            return true;
        }

        /// <summary>
        /// 仅供自检：窗口拉宽时，分组内的下拉框 / 文本框必须真的跟着变宽。
        /// 只把分组变宽、内容不动，就是用户说的"拉宽毫无意义"。
        /// </summary>
        internal static bool VerifyInnerStretch(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groupHost == null || sf._innerLayouts.Count == 0) return false;

            // ★ 走真实的"窗口尺寸改变"路径，而不是直接改 _groupHost.Width。
            //   只有这样才能复现用户真实操作，也才验证得了 GroupBox 宽度、
            //   TableLayoutPanel 列宽、滚动条这些真实约束。
            sf.ClientSize = new Size(560, 760);
            Application.DoEvents();
            sf.LayoutResponsive();

            int narrowCombo = sf._cmbLeft.Width;
            int narrowBlack = sf._txtBlacklist.Width;
            int narrowGroupInner = sf._cmbLeft.Parent != null ? sf._cmbLeft.Parent.ClientSize.Width : -1;
            int narrowCols = sf._groupHost.CurrentColumns;

            // 拉宽到两列（★ 必须算上侧栏宽度，见 ClientWidthForTwoColumns 的注释）
            sf.ClientSize = new Size(ClientWidthForTwoColumns, 820 * ((int)Math.Round(CurrentUiScale) < 1 ? 1 : (int)Math.Round(CurrentUiScale)));
            Application.DoEvents();
            sf.LayoutResponsive();

            int wideCombo = sf._cmbLeft.Width;
            int wideBlack = sf._txtBlacklist.Width;
            int wideGroupInner = sf._cmbLeft.Parent != null ? sf._cmbLeft.Parent.ClientSize.Width : -1;
            int wideCols = sf._groupHost.CurrentColumns;

            LastStretchDetail = "列数 " + narrowCols + "->" + wideCols
                + " | 下拉框 " + narrowCombo + "->" + wideCombo
                + " | 黑名单 " + narrowBlack + "->" + wideBlack
                + " | 分组内宽 " + narrowGroupInner + "->" + wideGroupInner
                + " | " + DumpLayoutItem(f, sf._cmbLeft);

            // 两列必然是窄分组，所以这里不断言"变宽"，而是断言
            // **下拉框在两列模式下仍然有足够可用宽度**（用户抱怨的就是被挤扁）。
            if (wideCols == 2)
            {
                if (wideCombo < 160) return false;     // 至少能显示几个字
                if (wideBlack < 160) return false;
            }
            else
            {
                // 没切成两列（屏幕/DPI 不同），退回验证单列拉宽确实变宽
                if (wideCombo <= narrowCombo) return false;
                if (wideBlack <= narrowBlack) return false;
            }

            return true;
        }

        /// <summary>自检失败时用来解释原因的一行诊断。</summary>
        internal static string LastStretchDetail = string.Empty;

        /// <summary>仅供开发期诊断：把某个分组里所有控件的最终位置/基准倒成文本。</summary>
        /// <summary>仅供开发期测试：强制再跑一次响应式布局（幂等性检查用）。</summary>
        internal static void RelayoutForTest(Form f)
        {
            var sf = f as SettingsForm;
            if (sf != null) sf.LayoutResponsive();
        }

        /// <summary>仅供自检：走一遍"重新读配置→填充全部控件"的路径。</summary>
        internal static void PopulateForTest(Form f)
        {
            var sf = f as SettingsForm;
            if (sf != null) sf.Populate();
        }

        /// <summary>
        /// 仅供诊断：把外框预览框在每一种"样式"下各渲染一张 PNG。
        ///
        /// 为什么需要它：用户反馈"选了外框形状但预览不变"。光看代码分不清是
        ///   (a) 下拉框没把 StyleId 传下去，
        ///   (b) 传下去了但 FrameStyles.Draw 各种 style 画出来一样，
        ///   (c) 传下去了也画了，但那个尺寸下差异肉眼不可见。
        /// 把五种样式各渲一张、两两做像素差，答案立刻明确。
        /// </summary>
        internal static void DumpPreviewStyles(string outDir)
        {
            var sf = (SettingsForm)null;
            try
            {
                System.IO.Directory.CreateDirectory(outDir);
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "holddrag-previewtest-cfg");
                try { System.IO.Directory.CreateDirectory(tmp); } catch { }
                var store = new ConfigStore(tmp);
                try { store.Load(); } catch { }
                var coord = new Coordinator(store);

                sf = new SettingsForm(store, coord);
                sf.StartPosition = FormStartPosition.Manual;
                sf.Location = new Point(20, 20);
                sf.ClientSize = new Size(700, 900);
                sf.Show();
                Application.DoEvents();

                var sb = new System.Text.StringBuilder();
                var hashes = new System.Collections.Generic.List<string>();

                sb.AppendLine("=== 路径 A：直接设 StyleId 字段 ===");
                foreach (string style in FrameStyles.All)
                {
                    // 直接设 StyleId 字段（和 SyncPreview 走的是同一条路）
                    sf._pnlPreview.StyleId = style;
                    sf._pnlPreview.ColorId = FrameColors.Red;   // 固定一个高对比色，便于比对
                    sf._pnlPreview.Thickness = 4;
                    sf._pnlPreview.Radius = 13;
                    sf._pnlPreview.FrameEnabled = true;
                    sf._pnlPreview.Refresh_();
                    Application.DoEvents();

                    var ctl = sf._pnlPreview;
                    using (var bmp = new Bitmap(ctl.Width, ctl.Height))
                    {
                        ctl.DrawToBitmap(bmp, new Rectangle(0, 0, ctl.Width, ctl.Height));
                        string path = System.IO.Path.Combine(outDir, "preview-" + style + ".png");
                        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);

                        // 算一个简单的像素指纹：所有像素的 RGB 和
                        long sum = 0; int nonWhite = 0;
                        for (int y = 0; y < bmp.Height; y++)
                            for (int x = 0; x < bmp.Width; x++)
                            {
                                var c = bmp.GetPixel(x, y);
                                sum += c.R + c.G + c.B;
                                if (c.R < 250 || c.G < 250 || c.B < 250) nonWhite++;
                            }
                        hashes.Add(style);
                        sb.AppendLine(string.Format(
                            "  {0,-8} size={1}x{2} 像素和={3} 非白像素={4} 文件={5}",
                            style, bmp.Width, bmp.Height, sum, nonWhite,
                            System.IO.Path.GetFileName(path)));
                    }
                }

                // ═══ 路径 B：模拟"用户点下拉框" ═══
                // 这一路才真正验证 UI 连线：SelectedIndex 变了 → SyncPreview() → 预览变了？
                sb.AppendLine();
                sb.AppendLine("=== 路径 B：改下拉框 SelectedIndex（模拟用户点选）===");
                sb.AppendLine("  下拉框里共 " + sf._cmbFrameStyle.Items.Count + " 项");
                for (int i = 0; i < sf._cmbFrameStyle.Items.Count; i++)
                {
                    sf._cmbFrameStyle.SelectedIndex = i;
                    Application.DoEvents();
                    string shown = sf._pnlPreview.StyleId;
                    var item = sf._cmbFrameStyle.Items[i];
                    var ctl = sf._pnlPreview;
                    long sum = 0; int nonWhite = 0;
                    using (var bmp = new Bitmap(ctl.Width, ctl.Height))
                    {
                        ctl.DrawToBitmap(bmp, new Rectangle(0, 0, ctl.Width, ctl.Height));
                        bmp.Save(System.IO.Path.Combine(outDir, "combo-" + i + "-" + shown + ".png"),
                            System.Drawing.Imaging.ImageFormat.Png);
                        for (int y = 0; y < bmp.Height; y++)
                            for (int x = 0; x < bmp.Width; x++)
                            {
                                var c = bmp.GetPixel(x, y);
                                sum += c.R + c.G + c.B;
                                if (c.R < 250 || c.G < 250 || c.B < 250) nonWhite++;
                            }
                    }
                    sb.AppendLine(string.Format(
                        "  index={0} 下拉项文字=\"{1}\" → 预览 StyleId=\"{2}\" 像素和={3} 非白像素={4} {5}",
                        i, item != null ? item.ToString() : "(null)", shown, sum, nonWhite,
                        shown == sf._pnlPreview.StyleId ? "" : "  ← ★ 不一致！"));
                }

                sb.AppendLine();
                sb.AppendLine("★ 路径 A 各行 [像素和] 全不同 → 五种样式确实画出了不同结果；");
                sb.AppendLine("★ 路径 B 各行 [像素和] 全不同 → 下拉框连线正常，改选项能刷新预览；");
                sb.AppendLine("  若路径 B 有任意两行相同，就是[选了形状但预览不变]的真 bug。");

                // ═══ 路径 C：直接吃用户真实 config.json，看打开设置时预览到底是什么样 ═══
                // 这一路最关键：用户报告"配置里存的是 Double，但预览只画了一条线"。
                sb.AppendLine();
                sb.AppendLine("=== 路径 C：加载真实配置后的预览状态 ===");
                try
                {
                    string realDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
                    var realStore = new ConfigStore(realDir);
                    realStore.Load();
                    var realCoord = new Coordinator(realStore);
                    var cfg = realStore.Current;

                    sb.AppendLine(string.Format("  配置：颜色={0} 样式={1} 粗细={2} 圆角={3} 外框开关={4}",
                        cfg.FrameColorId, cfg.FrameStyleId, cfg.FrameThickness, cfg.FrameRadius,
                        cfg.ShowActivationFrame));

                    var rf = new SettingsForm(realStore, realCoord);
                    rf.StartPosition = FormStartPosition.Manual;
                    rf.Location = new Point(20, 20);
                    rf.ClientSize = new Size(900, 900);
                    rf.Show();
                    Application.DoEvents();
                    for (int k = 0; k < 4; k++) { Application.DoEvents(); System.Threading.Thread.Sleep(80); }

                    sb.AppendLine(string.Format("  下拉框选中项：颜色=\"{0}\" 样式=\"{1}\"",
                        rf._cmbFrameColor.SelectedItem != null ? rf._cmbFrameColor.SelectedItem.ToString() : "(null)",
                        rf._cmbFrameStyle.SelectedItem != null ? rf._cmbFrameStyle.SelectedItem.ToString() : "(null)"));
                    sb.AppendLine(string.Format("  预览控件字段：ColorId=\"{0}\" StyleId=\"{1}\" Thickness={2} Radius={3} Enabled={4}",
                        rf._pnlPreview.ColorId, rf._pnlPreview.StyleId,
                        rf._pnlPreview.Thickness, rf._pnlPreview.Radius, rf._pnlPreview.FrameEnabled));

                    var rc = rf._pnlPreview;
                    using (var bmp = new Bitmap(rc.Width, rc.Height))
                    {
                        rc.DrawToBitmap(bmp, new Rectangle(0, 0, rc.Width, rc.Height));
                        bmp.Save(System.IO.Path.Combine(outDir, "preview-REALCONFIG.png"),
                            System.Drawing.Imaging.ImageFormat.Png);

                        // 数一数左边有几条"红线"（用于判断 Double 是否真的画了两条）
                        int ymid = bmp.Height / 2;
                        int lines = 0; bool inLine = false;
                        var sb2 = new System.Text.StringBuilder();
                        for (int x = 0; x < bmp.Width; x++)
                        {
                            var c = bmp.GetPixel(x, ymid);
                            bool red = c.R > c.G + 8 && c.R > c.B + 8;
                            if (red && !inLine) { lines++; sb2.Append(" 第" + lines + "条@x=" + x); }
                            inLine = red;
                        }
                        sb.AppendLine("  ★ 左半行(y=" + ymid + ")扫到的红线数 = " + lines + sb2);
                        sb.AppendLine("    Double 样式应该是 2 条；1 条说明预览没按配置画。");
                    }
                    rf.Close();
                }
                catch (Exception ex) { sb.AppendLine("  路径 C 异常：" + ex.Message); }

                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(outDir, "preview-styles.txt"),
                    sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex) { Log.Error(ex); }
            finally { if (sf != null) { try { sf.Close(); } catch { } } }
        }

        /// <summary>
        /// 仅供诊断：把"视觉反馈"分组（g3）在若干客户区宽度下的控件几何倒成文本。
        ///
        /// 为什么需要它：用户报"样式预览在窗口缩小时看得见、拉宽后就完全看不见"。
        /// 这类"只在特定宽度出现"的问题，纯读代码推不出来（布局引擎分支太多），
        /// 必须把每一档宽度下的【实际落位 + 基准 + z 序 + 可见性】全部打出来对比。
        /// 宽度参数给的是【设计稿像素】，内部会按当前 DPI 放大，和真实操作一致。
        /// </summary>
        internal static void DumpVisualGroupGeometry(System.Text.StringBuilder sb, int[] designWidths)
        {
            SettingsForm sf = null;
            try
            {
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "holddrag-geom-cfg");
                System.IO.Directory.CreateDirectory(tmp);
                var store = new ConfigStore(tmp);
                store.Load();
                var coord = new Coordinator(store);

                sf = new SettingsForm(store, coord);
                sf.StartPosition = FormStartPosition.Manual;
                sf.Location = new Point(-2400, 20);       // 屏幕外，不挡别的窗口
                sf.Show();
                Application.DoEvents();
                Pump();

                int scale = (int)Math.Round(CurrentUiScale);
                if (scale < 1) scale = 1;

                for (int k = 0; k < designWidths.Length; k++)
                {
                    int w = designWidths[k] * scale;
                    int h = Math.Min(Screen.PrimaryScreen.WorkingArea.Height - 80,
                                     Math.Max(600, 900 * scale));
                    sf.ClientSize = new Size(w, h);
                    Application.DoEvents();
                    Pump();
                    sf.LayoutResponsive();
                    Application.DoEvents();

                    sb.AppendLine();
                    sb.AppendLine("════ 视觉反馈组几何 · 客户区宽=" + w + "（设计稿 " + designWidths[k] + "px）════");
                    DumpGroup(sf, 2, sb);       // g3 = 视觉反馈

                    var gb = sf._groupHost.Controls[2] as GroupBox;
                    if (gb != null)
                    {
                        sb.AppendLine("z 序 / 可见性 / 实际 bounds（客户区坐标，z=0 在最上层）：");
                        for (int i = 0; i < gb.Controls.Count; i++)
                        {
                            var c = gb.Controls[i];
                            sb.AppendLine(string.Format(
                                "  z={0,-2} {1,-16} vis={2,-5} L={3,-5} T={4,-5} W={5,-5} H={6,-5} 右={7,-5} 底={8}",
                                gb.Controls.GetChildIndex(c), c.GetType().Name, c.Visible,
                                c.Left, c.Top, c.Width, c.Height,
                                c.Left + c.Width, c.Top + c.Height));
                        }
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("异常：" + ex.Message); }
            finally { if (sf != null) { try { sf.Close(); } catch { } } }
        }

        private static void Pump()
        {
            for (int i = 0; i < 3; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(30); }
        }

        /// <summary>仅供诊断：某分组内所有控件的底边最大值（客户区坐标）。</summary>
        internal int NeedGroupBottom(GroupBox gb)
        {
            int bottom = 0;
            for (int i = 0; i < _innerLayouts.Count; i++)
            {
                var c = _innerLayouts[i].Control;
                if (c == null || c.Parent != gb) continue;
                int b = c.Top + c.Height;
                if (b > bottom) bottom = b;
            }
            return bottom;
        }

        /// <summary>仅供诊断：某分组"装得下内容"所需的高度（外框尺寸）。</summary>
        internal int NeedGroupHeight(GroupBox gb)
        {
            int bottom = NeedGroupBottom(gb);
            if (bottom <= 0) return gb.Height;
            return bottom + 12 + (gb.Height - gb.ClientSize.Height);
        }

        internal static void DumpGroup(Form f, int groupIndex, System.Text.StringBuilder sb)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groupHost == null) { sb.AppendLine("(no form)"); return; }
            if (groupIndex < 0 || groupIndex >= sf._groupHost.Controls.Count)
            {
                sb.AppendLine("(bad index)");
                return;
            }

            var gb = sf._groupHost.Controls[groupIndex] as GroupBox;
            if (gb == null) { sb.AppendLine("(not groupbox)"); return; }

            sb.AppendLine();
            sb.AppendLine("列数=" + sf._groupHost.CurrentColumns
                        + " 分组宽=" + gb.Width + " 分组内宽=" + gb.ClientSize.Width
                        + " 高=" + gb.Height + " 基准高=" + (groupIndex < sf._baseGroupHeights.Length ? sf._baseGroupHeights[groupIndex] : -1)
                        + " 需要高=" + sf.NeedGroupHeight(gb)
                        + " 组内底=" + sf.NeedGroupBottom(gb));
            sb.AppendLine("缩放判定=" + LastRebaseInfo);
            sb.AppendLine();

            for (int i = 0; i < sf._innerLayouts.Count; i++)
            {
                var it = sf._innerLayouts[i];
                var c = it.Control;
                if (c.Parent != gb) continue;

                string txt = c.Text;
                if (txt != null && txt.Length > 16) txt = txt.Substring(0, 16) + "…";
                if (txt != null) txt = txt.Replace("\r", " ").Replace("\n", " ");

                int right = c.Left + c.Width;
                int overflow = right - gb.ClientSize.Width;

                sb.AppendLine(string.Format(
                    "{0,-22} 类型={1,-14} OrigW={11,-5} BaseL={2,-4} BaseT={12,-4} BaseW={3,-4} 实际L={4,-4} T={13,-4} W={5,-4} 右={6,-4} 溢出={7,-5} Mode={8} Need={9,-4} Wrap={14} SB={15,-4} pW={16,-4} 文字=\"{10}\"",
                    "[" + i + "]", c.GetType().Name, it.BaseLeft, it.BaseWidth,
                    c.Left, c.Width, right, overflow, it.How, TextNeed(it), txt,
                    it.OriginalWidth, it.BaseTop, c.Top, it.WrapsToNextRow ? "Y" : "-", it.ShiftByOffset, c.Parent.ClientSize.Width));
            }
        }

        /// <summary>仅供自检：导出某个控件的布局基准，用来定位宽度算错的原因。</summary>
        internal static string DumpLayoutItem(Form f, Control target)
        {
            var sf = f as SettingsForm;
            if (sf == null) return "(not SettingsForm)";
            for (int i = 0; i < sf._innerLayouts.Count; i++)
            {
                var it = sf._innerLayouts[i];
                if (it.Control != target) continue;
                int inner = target.Parent != null ? target.Parent.ClientSize.Width : -1;
                return "Mode=" + it.How + " BaseLeft=" + it.BaseLeft + " BaseWidth=" + it.BaseWidth
                     + " RightGap=" + it.RightGap + " BaseInner=" + it.BaseInner
                     + " 当前内宽=" + inner + " 当前Left=" + target.Left + " 当前W=" + target.Width;
            }
            return "(未登记)";
        }

        /// <summary>强制所有分组完成一次真实布局（不这么做读到的 ClientSize 是旧的）。</summary>
        private void ForceGroupLayout()
        {
            _groupHost.PerformLayout();
            for (int i = 0; i < _groupHost.Controls.Count; i++)
            {
                var g = _groupHost.Controls[i] as GroupBox;
                if (g != null) g.PerformLayout();
            }
        }

        /// <summary>
        /// 仅供自检：分组内控件的伸缩必须是幂等的。
        /// 窗口来回拉几次之后，控件位置不能越跑越偏（累加 bug 的典型症状）。
        /// </summary>
        internal static bool VerifyStretchIdempotent(Form f)
        {
            var sf = f as SettingsForm;
            if (sf == null || sf._groupHost == null) return false;

            int wide = sf.GroupWidth + (int)Math.Round(200 * CurrentUiScale);

            sf._groupHost.Width = wide;
            sf._groupHost.Relayout(wide);
            sf.LayoutInner();
            var a = sf._cmbLeft.Bounds;

            // 再来回折腾几轮
            for (int i = 0; i < 5; i++)
            {
                sf._groupHost.Width = sf.GroupWidth;
                sf._groupHost.Relayout(sf.GroupWidth);
                sf.LayoutInner();

                sf._groupHost.Width = wide;
                sf._groupHost.Relayout(wide);
                sf.LayoutInner();
            }

            var b = sf._cmbLeft.Bounds;
            return a.Left == b.Left && a.Width == b.Width;
        }
    }
}
