using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
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

        private readonly TextBox _txtKey = new TextBox();
        private readonly Button _btnRecord = new Button();
        private readonly Label _lblKeyHint = new Label();
        private readonly CheckBox _chkHold = new FlatCheck();
        private readonly NumericUpDown _numHold = new NumericUpDown();

        private readonly ComboBox _cmbLeft = new ComboBox();
        private readonly ComboBox _cmbRight = new ComboBox();
        private readonly ComboBox _cmbMiddle = new ComboBox();

        private readonly CheckBox _chkFrame = new FlatCheck();
        private readonly CheckBox _chkSnapPreview = new FlatCheck();
        private readonly ComboBox _cmbFrameColor = new ComboBox();
        private readonly ComboBox _cmbFrameStyle = new ComboBox();
        private readonly NumericUpDown _numFrameThick = new NumericUpDown();
        private readonly NumericUpDown _numFrameRadius = new NumericUpDown();
        private readonly FramePreviewBox _pnlPreview = new FramePreviewBox();
        private readonly Label _lblFrameTip = new Label();
        private readonly CheckBox _chkEdgeSnap = new FlatCheck();
        private readonly CheckBox _chkSnapWindows = new FlatCheck();
        private readonly NumericUpDown _numDist = new NumericUpDown();
        private readonly NumericUpDown _numCorner = new NumericUpDown();
        private readonly CheckBox _chkDblLeft = new FlatCheck();
        private readonly CheckBox _chkDblRight = new FlatCheck();
        private readonly CheckBox _chkRestoreMax = new FlatCheck();
        private readonly CheckBox _chkFront = new FlatCheck();
        private readonly CheckBox _chkFullscreen = new FlatCheck();
        private readonly TextBox _txtBlacklist = new TextBox();

        private readonly ComboBox _cmbRefreshMode = new ComboBox();
        private readonly NumericUpDown _numHz = new NumericUpDown();
        private readonly Label _lblHzInfo = new Label();

        private readonly ComboBox _cmbAutoStart = new ComboBox();
        private readonly CheckBox _chkRunAsAdmin = new FlatCheck();
        private readonly CheckBox _chkWarnNonAdmin = new FlatCheck();
        private readonly Label _lblAdmin = new Label();
        private readonly Button _btnRestartAdmin = new Button();

        private readonly Label _lblStatus = new Label();

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
            ///   g4（拖动与吸附）里有一行是"吸附距离 + 数字框 + 像素 + 缩放角区 + 数字框 + %"，
            ///   单列 536 宽时正好排满。两列时每列只有约 (可用宽-12)/2，
            ///   一旦低于 560 这一行就会溢出、右端的"%""像"被切掉。
            ///   所以阈值取 2 × 570 + 间隙 = 1160 左右，保证每列不低于 570。
            ///   宁可晚一点才变两列，也不要出现"两列了但内容被切"。
            /// </summary>
            public const int TwoColumnThreshold = 1240;

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
                    groups[i].Margin = new Padding(0, 0, 0, 8);
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
                int want = available >= threshold ? 2 : 1;

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
                        FrameStyles.Draw(g, area, FrameColors.Resolve(ColorId), StyleId, _thickness, Radius);
                    }
                    else
                    {
                        // 关掉外框时给一条灰色虚线，明确"这里本该有框，但你关掉了"
                        using (var p = new Pen(Color.FromArgb(180, 160, 160, 165), 1f))
                        {
                            p.DashStyle = DashStyle.Dash;
                            g.DrawRectangle(p, win.X, win.Y, win.Width - 1, win.Height - 1);
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

        public SettingsForm(ConfigStore store, Coordinator coord)
        {
            _store = store;
            _coord = coord;
            _edit = store.Current.Clone();

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
            _txtKey.BackColor = SystemColors.Window;
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
            _lblKeyHint.ForeColor = Color.FromArgb(113, 113, 122);
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
            var g3 = Group("视觉反馈", 0, 0, GroupWidth, 214);

            _chkFrame.Text = "长按触发键到达阈值后，光标所在的窗口立刻亮起外框";
            _chkFrame.Location = new Point(18, 30);
            _chkFrame.Size = new Size(500, 22);
            _chkFrame.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _chkFrame.CheckedChanged += (s, e) => SyncFrameEnabled();
            g3.Controls.Add(_chkFrame);

            _chkSnapPreview.Text = "拖到屏幕边缘时预览会落在哪（半透明色块）";
            _chkSnapPreview.Location = new Point(18, 54);
            _chkSnapPreview.Size = new Size(500, 22);
            _chkSnapPreview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g3.Controls.Add(_chkSnapPreview);

            g3.Controls.Add(Label("外框颜色", 18, 86, 62));
            StyleCombo(_cmbFrameColor, 84, 82, 152);
            _cmbFrameColor.SelectedIndexChanged += (s, e) => SyncPreview();
            _cmbFrameColor.DrawMode = DrawMode.OwnerDrawFixed;
            _cmbFrameColor.DrawItem += DrawColorItem;
            g3.Controls.Add(_cmbFrameColor);

            g3.Controls.Add(Label("样式", 248, 86, 40));
            StyleCombo(_cmbFrameStyle, 290, 82, 122);
            _cmbFrameStyle.SelectedIndexChanged += (s, e) => SyncPreview();
            g3.Controls.Add(_cmbFrameStyle);

            g3.Controls.Add(Label("粗细", 18, 120, 62));
            StyleNum(_numFrameThick, 84, 116, 66, 1, 8, 1);
            _numFrameThick.ValueChanged += (s, e) => SyncPreview();
            g3.Controls.Add(_numFrameThick);
            g3.Controls.Add(Label("px", 154, 120, 26));

            g3.Controls.Add(Label("圆角", 248, 120, 40));
            StyleNum(_numFrameRadius, 290, 116, 66, 0, 28, 1);
            _numFrameRadius.ValueChanged += (s, e) => SyncPreview();
            g3.Controls.Add(_numFrameRadius);
            g3.Controls.Add(Label("px", 360, 120, 26));

            _lblFrameTip.Location = new Point(18, 150);
            _lblFrameTip.Size = new Size(392, 48);
            _lblFrameTip.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblFrameTip.ForeColor = Color.FromArgb(113, 113, 122);
            _lblFrameTip.Text = "外框用来告诉你「这扇窗口现在已经被接管了」，松手或者松开触发键就会消失。\r\n"
                              + "上面四个都可以随便改，右上角是实时预览。";
            g3.Controls.Add(_lblFrameTip);

            // 预览框钉在右上角：窗口拉宽，它跟着往右移，永远贴着右边不出界。
            _pnlPreview.Location = new Point(424, 78);
            _pnlPreview.Size = new Size(96, 116);
            SetShiftFromRight(_pnlPreview, 16);
            g3.Controls.Add(_pnlPreview);

            // ══ 组 4：拖动与吸附 ════════════════════════════════
            // ★ 高度从 230 提到 292 ★
            //   因为黑名单那一行下面多了一排按钮（选择程序… / 点选窗口）和一行说明文字。
            //   不调的话新按钮会贴到分组底边甚至被裁掉。
            var g4 = Group("拖动与吸附", 0, 0, GroupWidth, 292);

            _chkEdgeSnap.Text = "拖到屏幕边缘自动半屏 / 四分之一屏 / 最大化";
            _chkEdgeSnap.Location = new Point(18, 30);
            _chkEdgeSnap.Size = new Size(500, 22);
            _chkEdgeSnap.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g4.Controls.Add(_chkEdgeSnap);

            _chkSnapWindows.Text = "拖动时自动对齐其它窗口的边框";
            _chkSnapWindows.Location = new Point(18, 54);
            _chkSnapWindows.Size = new Size(500, 22);
            _chkSnapWindows.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            g4.Controls.Add(_chkSnapWindows);

            g4.Controls.Add(Label("吸附距离", 18, 84, 66));
            StyleNum(_numDist, 88, 81, 70, 1, 200, 5);
            g4.Controls.Add(_numDist);
            g4.Controls.Add(Label("像素", 162, 84, 40));

            // 右列（缩放角区）跟着右边缘走：窗口拉宽时它留在右边，不与左列挤在一起。
            var lblCorner = Label("缩放角区", 230, 84, 66);
            SetShiftFromRight(lblCorner, -1);
            g4.Controls.Add(lblCorner);
            StyleNum(_numCorner, 300, 81, 70, 5, 45, 1);
            SetShiftFromRight(_numCorner, -1);
            g4.Controls.Add(_numCorner);
            var lblCornerUnit = Label("%", 374, 84, 30);
            SetShiftFromRight(lblCornerUnit, -1);
            g4.Controls.Add(lblCornerUnit);

            _chkDblLeft.Text = "双击左键 = 最大化 / 还原";
            _chkDblLeft.Location = new Point(18, 110);
            _chkDblLeft.Size = new Size(240, 22);
            g4.Controls.Add(_chkDblLeft);

            _chkDblRight.Text = "双击右键 = 九宫格吸附";
            _chkDblRight.Location = new Point(272, 110);
            _chkDblRight.Size = new Size(240, 22);
            SetShiftFromRight(_chkDblRight, 16);
            g4.Controls.Add(_chkDblRight);

            _chkRestoreMax.Text = "允许拖动已最大化的窗口（先还原再拖）";
            _chkRestoreMax.Location = new Point(18, 136);
            _chkRestoreMax.Size = new Size(240, 22);
            g4.Controls.Add(_chkRestoreMax);

            _chkFront.Text = "开始拖动时自动把窗口提到最前";
            _chkFront.Location = new Point(272, 136);
            _chkFront.Size = new Size(250, 22);
            SetShiftFromRight(_chkFront, 16);
            g4.Controls.Add(_chkFront);

            _chkFullscreen.Text = "允许拖动全屏 / 无边框窗口（游戏、全屏播放器，可能有风险）";
            _chkFullscreen.Location = new Point(18, 162);
            _chkFullscreen.Size = new Size(500, 22);
            _chkFullscreen.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _chkFullscreen.ForeColor = Color.FromArgb(161, 98, 7);
            g4.Controls.Add(_chkFullscreen);

            g4.Controls.Add(Label("不生效的窗口", 18, 190, 90));
            _txtBlacklist.Location = new Point(112, 187);
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
            _btnPickExe.Location = new Point(112, 222);
            _btnPickExe.Size = new Size(92, 26);
            _btnPickExe.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _btnPickExe.Click += (s, e) => PickExeFile();
            g4.Controls.Add(_btnPickExe);

            _btnPickWindow = new FlatButton();
            _btnPickWindow.Text = "点选窗口";
            _btnPickWindow.Location = new Point(212, 222);
            _btnPickWindow.Size = new Size(92, 26);
            _btnPickWindow.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _btnPickWindow.Click += (s, e) => PickWindowByClick();
            g4.Controls.Add(_btnPickWindow);

            var tip4 = Label("填窗口标题 / 程序名 / 窗口类名的关键字，逗号分隔；命中就完全不接管。"
                + "也可以用上面两个按钮直接挑。", 18, 256, 500);
            tip4.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            tip4.ForeColor = Color.FromArgb(113, 113, 122);
            g4.Controls.Add(tip4);

            // ══ 组 5：流畅度 ════════════════════════════════════
            var g5 = Group("流畅度", 0, 0, GroupWidth, 128);

            g5.Controls.Add(Label("拖动刷新率", 16, 32, 76));
            StyleCombo(_cmbRefreshMode, 96, 28, 200);
            _cmbRefreshMode.SelectedIndexChanged += (s, e) => SyncHzEnabled();
            g5.Controls.Add(_cmbRefreshMode);

            StyleNum(_numHz, 306, 28, 70, 20, 500, 5);
            g5.Controls.Add(_numHz);
            g5.Controls.Add(Label("Hz", 382, 32, 30));

            _lblHzInfo.Location = new Point(16, 62);
            _lblHzInfo.Size = new Size(504, 52);
            _lblHzInfo.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _lblHzInfo.ForeColor = Color.FromArgb(113, 113, 122);
            g5.Controls.Add(_lblHzInfo);

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
            _lblStatus.ForeColor = Color.FromArgb(21, 128, 61);
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
            ScaleDesign(g1, g2, g3, g4, g5, g6);

            _innerLayouts.Clear();
            _innerIndex.Clear();
            for (int i = 0; i < 6; i++)
            {
                var gb = new[] { g1, g2, g3, g4, g5, g6 }[i];
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
            _groupHost = new ResponsiveGroupPanel(new[] { g1, g2, g3, g4, g5, g6 });
            _groupHost.Location = new Point(ScrollEdgeMargin, ScrollEdgeMargin);
            _scrollHost.Controls.Add(_groupHost);

            _scrollHost.Dock = DockStyle.Fill;
            _scrollHost.AutoScroll = true;
            _scrollHost.BackColor = Design.Canvas;
            Controls.Add(_scrollHost);

_pager.Dock = DockStyle.Bottom;
  // ★ 46 = 按钮高 26 + 上下各 10 的留白。
            //   早先写58（配合 32 高的按钮），按钮改小之后底栏会显得空荡荡，
         //   而且视觉重心偏上。
_pager.Height = S(46);
            _pager.BackColor = Design.Surface;     // 白色，与内容区一体的"工具条"
            _pager.Paint += (s, e) =>
            {
                // 顶边一条极淡的分隔线，把工具条和滚动内容区分开，
                // 不然一整块白色会糊在一起、看不出哪里是可滚动区域。
                using (var p = new Pen(Design.Stroke))
                    e.Graphics.DrawLine(p, 0, 0, _pager.Width, 0);
            };
            Controls.Add(_pager);

            // 默认尺寸：单列时正好放下一个分组，一屏能看到大部分内容。
            //
            // ★ 这里的三个数字（宽 / 高 / 最小高度）全部走 S() 缩放 ——
            //   控件已经按 DPI 放大了（ScaleDesign），窗口不跟着放大就装不下，
            //   表现是"内容比窗口大、右边和下边被切掉"。
            ClientSize = new Size(SingleColumnClientWidth, S(760));

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
            MinimumSize = new Size(ClientWidthToWindowWidth(SingleColumnClientWidth), S(520));

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

                // 已经差不多贴合就别动，避免反复微调造成抖动。
                if (Math.Abs(ClientSize.Height - want) < S(24)) return;

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
                // 每次都去问 DeviceDpi 没必要（而且窗口句柄没了会抛）。
                if (_uiScale > 0f) return _uiScale;

                float s = 1f;
                try
                {
                    int dpi = 96;
                    try { dpi = DeviceDpi; } catch { }
                    if (dpi <= 0) dpi = 96;
                    s = dpi / 96f;
                    if (s < 0.5f || s > 4f) s = 1f;   // 明显不合理的值一律当 100%
                }
                catch { s = 1f; }

                _uiScale = s;
                CurrentUiScale = s;   // 让内部类（ResponsiveGroupPanel）也能取到
                return s;
            }
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
                    card.TitleFontPx = (int)Math.Round(13 * s);
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

                int need = MinWidthNeed(c);
                if (need <= 0) continue;

                // 放得下 → 钉回单行高
                if (need <= c.Width)
                {
                    if (c.Height != it.BaseHeight) c.Height = it.BaseHeight;
                    continue;
                }

                // 放不下 → 算折行后的真实高度（纯赋值，绝不做 += ）
                int textAvail = c.Width - (c is CheckBox ? 26 : 4);
                if (textAvail < 20) textAvail = 20;

                Size measured;
                try
                {
                    measured = TextRenderer.MeasureText(
                        c.Text, c.Font,
                        new Size(textAvail, 0),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                }
                catch { measured = new Size(0, it.BaseHeight); }

                int realH = it.BaseHeight;
                if (measured.Height > it.BaseHeight) realH = measured.Height + 4;

                c.Height = realH;
            }
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

                int need = MinWidthNeed(c);
                if (need <= 0) continue;

                // 可用宽度就是它现在的宽度。够放下 → 钉回单行高，不用管。
                if (need <= c.Width)
                {
                    if (c.Height != it.BaseHeight) c.Height = it.BaseHeight;
                    continue;
                }

                // 放不下 → 算出"按当前宽度折行后"需要的真实高度。
                //
                // ★ 不能用 GetPreferredSize()：CheckBox 的 GetPreferredSize
                //   即便给了宽度约束，它也只按【单行】回报高度（实测恒等于 BaseHeight），
                //   于是"要折行"这个事实永远量不出来。
                //   必须自己用 TextRenderer + WordBreak 真正按宽度断行量高度 ——
                //   走的是和绘制同一个 GDI 口径，量出来就是真正要占的高度。
                int textAvail = c.Width - (c is CheckBox ? 26 : 4);
                if (textAvail < 20) textAvail = 20;

                Size measured;
                try
                {
                    measured = TextRenderer.MeasureText(
                        c.Text, c.Font,
                        new Size(textAvail, 0),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                }
                catch { measured = new Size(0, it.BaseHeight); }

                int realH = it.BaseHeight;
                if (measured.Height > it.BaseHeight) realH = measured.Height + 4;   // +4 给点行距余量

                c.Height = realH;

                if (realH > it.BaseHeight)
                {
                    c.Height = realH;

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
        private static int MinWidthNeed(Control c)
        {
            if (c == null) return 0;
            try
            {
                if (c is CheckBox)
                {
                    int need = c.PreferredSize.Width;
                    if (need > 0) return need;
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
                        InnerLayout nb;
                        int nbLeft = it.GrowRightNeighbor.Left;
                        if (_innerIndex.TryGetValue(it.GrowRightNeighbor, out nb))
                            nbLeft = nb.BaseLeft + nb.ShiftByOffset;

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
                // ★ 和 TextNeed() 保持同一个口径：CheckBox 的"自然宽度"就是
                //   它自己的 PreferredSize —— 勾选框 + 内边距 + 文字，一个字不多一个字不少。
                //   这里如果再自己写 "文字 + 28"，就会比 TextNeed() 小十几像素，
                //   于是 Grow 分支的封顶值反而低于 TextNeed() 的保底值，
                //   文字照样被切 —— 两个函数必须用同一个来源。
                if (c is CheckBox)
                {
                    int need = c.PreferredSize.Width;
                    if (need > 0) return need;
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutResponsive();
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
        private static class Design
        {
            /// <summary>窗口 / 滚动区底色：带一点冷灰，和卡片白拉开层次。</summary>
            public static readonly Color Canvas = Color.FromArgb(246, 247, 250);

            /// <summary>卡片底色：纯白，和底色形成层次。</summary>
            public static readonly Color Surface = Color.White;

            /// <summary>
            /// 卡片描边：很淡的一条线。
            /// 现代界面靠"底色层次 + 极淡描边"区分卡片，而不是靠粗边框或重阴影 ——
            /// 描边一深，整页立刻显得旧。
            /// </summary>
            public static readonly Color Stroke = Color.FromArgb(228, 231, 238);

            /// <summary>卡片圆角半径（设计稿像素）。12 比 10 更柔，接近现代设计语言。</summary>
            public const int Radius = 12;

            /// <summary>投影颜色：透明度很低，只在卡片下缘露出一线。</summary>
            public static readonly Color Shadow = Color.FromArgb(22, 28, 45);

            /// <summary>投影垂直偏移（设计稿像素）。</summary>
            public const int ShadowOffset = 1;

            /// <summary>投影扩散（设计稿像素）。</summary>
            public const int ShadowSpread = 4;

            /// <summary>卡片左侧强调竖条的宽度（设计稿像素）。</summary>
            public const int AccentBarWidth = 3;

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

            /// <summary>危险提示（"允许拖动全屏"那条橙色警告）。</summary>
            public static readonly Color Warning = Color.FromArgb(190, 106, 12);

            /// <summary>成功提示（状态文字）。</summary>
            public static readonly Color Success = Color.FromArgb(24, 128, 78);

            /// <summary>错误提示。</summary>
            public static readonly Color Danger = Color.FromArgb(196, 40, 40);
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
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

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
                    var shadowRect = new Rectangle(
                        1, ShadowOffsetPx, w - 2, h - 2);
                    var sp = RoundedPath(shadowRect, r);
                    for (int i = ShadowSpreadPx; i >= 1; i--)
                    {
                        // 越外层越淡：alpha 随 i 线性衰减
                        int alpha = Math.Max(4, 20 - i * 2);
                        using (var pen = new Pen(Color.FromArgb(alpha, Design.Shadow), i * 2f))
                            g.DrawPath(pen, sp);
                    }
                    sp.Dispose();
                }

                // ── ② 卡片本体：白底 + 细描边 + 圆角 ──
                var path = RoundedPath(new Rectangle(0, 0, w - 1, h - 1), r);
                using (var fill = new SolidBrush(Design.Surface)) g.FillPath(fill, path);
                using (var pen = new Pen(Design.Stroke)) g.DrawPath(pen, path);

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

                // ── ④ 标题：浮在卡片内左上角 ──
                if (!string.IsNullOrEmpty(Text))
                {
                    using (var f = new Font(Font.FontFamily, TitleFontPx,
                                            FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var b = new SolidBrush(Design.TextPrimary))
                    {
                        g.DrawString(Text, f, b, new PointF(AccentBarPx + 12, 9));
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
        /// 扁平按钮：主按钮实心强调色，次按钮描边，悬停/按下有反馈。
        ///
        /// 用自绘而不是 FlatStyle.Flat 的原因：
        ///   · FlatStyle.Flat 的边框颜色固定跟随系统，画不出"描边按钮"；
        ///   · 圆角做不了；
        ///   · 悬停态要自己挂 MouseEnter/MouseLeave 再 Invalidate，很啰嗦。
        /// </summary>
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
            private bool _hover;

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
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
            protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                float s = CurrentUiScale > 0f ? CurrentUiScale : 1f;

                int box = (int)Math.Round(17 * s);
                if (box < 12) box = 12;
                int gap = (int)Math.Round(9 * s);

                int top = (Height - box) / 2;
                if (top < 0) top = 0;
                var r = new Rectangle(0, top, box, box);

                int rad = (int)Math.Round(5 * s);
                if (rad > box / 2) rad = box / 2;
                if (rad < 1) rad = 1;

                bool on = Checked;
                Color fill, border;
                if (!Enabled)
                {
                    fill = on ? Color.FromArgb(203, 206, 214) : Color.FromArgb(246, 247, 250);
                    border = Color.FromArgb(216, 220, 228);
                }
                else if (on)
                {
                    fill = _hover ? ControlPaint.Light(Design.Accent, 0.10f) : Design.Accent;
                    border = fill;
                }
                else
                {
                    fill = _hover ? Design.AccentSoft : Color.White;
                    border = _hover ? Design.Accent : Design.InputStroke;
                }

                using (var path = RoundedPath(r, rad))
                {
                    using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (var p = new Pen(border, Math.Max(1f, s))) g.DrawPath(p, path);
                }

                if (on)
                {
                    float w = Math.Max(1.6f, 1.9f * s);
                    using (var p = new Pen(Color.White, w))
                    {
                        p.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        p.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        p.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;

                        float cx = r.Left + r.Width * 0.5f;
                        float cy = r.Top + r.Height * 0.5f;
                        float u = r.Width / 17f;

                        g.DrawLines(p, new[]
                        {
                            new PointF(cx - 4.6f * u, cy + 0.2f * u),
                            new PointF(cx - 1.3f * u, cy + 3.6f * u),
                            new PointF(cx + 4.8f * u, cy - 3.6f * u),
                        });
                    }
                }

                var textRect = new Rectangle(r.Right + gap, 0, Width - r.Right - gap, Height);
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
            }

            /// <summary>true = 主按钮（实心强调色），false = 次按钮（描边）。</summary>
            public bool Primary;

            private bool _hover, _down;

            protected override void OnMouseEnter(EventArgs e)
            {
                _hover = true; Invalidate(); base.OnMouseEnter(e);
            }
            protected override void OnMouseLeave(EventArgs e)
            {
                _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e);
            }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                _down = true; Invalidate(); base.OnMouseDown(e);
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                _down = false; Invalidate(); base.OnMouseUp(e);
            }
            protected override void OnEnabledChanged(EventArgs e)
            {
                Invalidate(); base.OnEnabledChanged(e);
            }

            /// <summary>
            /// Button 默认的 ProcessDialogKey 已经会把空格/回车转成 Click，
            /// 这里不需要重复处理 —— 去掉早先那个自己实现的版本，
            /// 免得同一个按键被触发两次 Click（点一下存两次的经典坑）。
            /// </summary>

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

var r = new Rectangle(0, 0, Width - 1, Height - 1);
       if (r.Width <= 0 || r.Height <= 0) return;
   // ★ 圆角也要跟着 DPI 缩放。
 //   写死 6 的话，200% 屏上按钮框变大了、圆角还是那么点，
    //   视觉上就是"一个很方的方块"，跟卡片组的圆角完全不是一个语言。
  int rad = CurrentUiScale > 0f ? (int)Math.Round(6 * CurrentUiScale) : 6;
        if (rad > r.Height / 2) rad = r.Height / 2;
      if (rad < 1) rad = 1;

                Color bg, fg, border;
                if (!Enabled)
                {
                    bg = Color.FromArgb(240, 241, 243);
                    fg = Color.FromArgb(170, 175, 184);
                    border = Color.FromArgb(233, 235, 239);
                }
                else if (Primary)
                {
                    bg = _down ? Design.AccentPressed
                       : _hover ? ControlPaint.Light(Design.Accent, 0.06f)
                       : Design.Accent;
                    fg = Color.White;
                    border = bg;
                }
                else
                {
                    bg = _down ? Color.FromArgb(238, 240, 244)
                       : _hover ? Color.FromArgb(250, 251, 252)
                       : Design.Surface;
                    fg = Design.TextPrimary;
                    border = _hover ? Color.FromArgb(205, 210, 219) : Design.Stroke;
                }

var path = RoundedPath(r, rad);
  using (var b = new SolidBrush(bg)) g.FillPath(b, path);
           using (var p = new Pen(border)) g.DrawPath(p, path);

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

            // ★ 去掉系统 3D 边框 ★
            //   原生下拉框那圈内凹的立体边是"界面显旧"的主要来源之一。
            //   FlatStyle.Flat 会换成一条平直的单线，和卡片式界面能对上。
            //
            //   刻意【不】动 DrawMode / ItemHeight：
            //   DropDownList 模式下的控件高度是由 ItemHeight 推出来的，
            //   一改高度就变，会连带把"底栏按钮和输入框等高"那条布局断言弄红。
            //   收益小、风险大，不做。
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Design.Surface;
            c.ForeColor = Design.TextPrimary;
        }

        private static void StyleNum(NumericUpDown n, int x, int y, int w, int min, int max, int step)
        {
            n.Location = new Point(x, y);
            n.Size = new Size(w, 26);
            n.Minimum = min;
            n.Maximum = max;
            n.Increment = step;
            n.TextAlign = HorizontalAlignment.Right;
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
            _chkSnapPreview.Checked = _edit.SnapPreview;

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

            _chkEdgeSnap.Checked = _edit.EdgeSnap;
            _chkSnapWindows.Checked = _edit.SnapToOtherWindows;
            _numDist.Value = Clamp(_edit.SnapDistance, (int)_numDist.Minimum, (int)_numDist.Maximum);
            _numCorner.Value = Clamp(_edit.CornerPercent, (int)_numCorner.Minimum, (int)_numCorner.Maximum);
            _chkDblLeft.Checked = _edit.DoubleClickLeftMaximize;
            _chkDblRight.Checked = _edit.DoubleClickRightZone;
            _chkRestoreMax.Checked = _edit.RestoreMaximizedWhenMoved;
            _chkFront.Checked = _edit.BringToFront;
            _chkFullscreen.Checked = _edit.AllowFullscreenWindows;
            _txtBlacklist.Text = _edit.Blacklist;

            _cmbRefreshMode.Items.Clear();
            _cmbRefreshMode.Items.Add(new IdItem { Value = RefreshModes.Auto, Text = "自动（跟随屏幕刷新率）" });
            _cmbRefreshMode.Items.Add(new IdItem { Value = RefreshModes.Custom, Text = "手动指定" });
            SelectId(_cmbRefreshMode, _edit.DragRefreshMode);
            _numHz.Value = Clamp(Math.Max((int)_numHz.Minimum, _edit.DragRefreshHz), (int)_numHz.Minimum, (int)_numHz.Maximum);
            SyncHzEnabled();

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
            _pnlPreview.FrameEnabled = on;
            _pnlPreview.Refresh_();
        }

        /// <summary>
        /// 把界面上当前的选择同步进预览框并重画。
        ///
        /// 注意：这里读的是【控件】当前值，不是 _edit。
        /// 两者在用户改动之后、点保存之前是不一样的 —— 预览必须跟控件走，
        /// 否则就变成"我改了颜色，预览还显示保存前的样子"。
        /// </summary>
        private void SyncPreview()
        {
            _pnlPreview.ColorId = SelectedId(_cmbFrameColor, FrameColors.DefaultId);
            _pnlPreview.StyleId = SelectedId(_cmbFrameStyle, FrameStyles.DefaultId);
            _pnlPreview.Thickness = (int)_numFrameThick.Value;   // 属性 setter 内部会 Invalidate
            _pnlPreview.Radius = (int)_numFrameRadius.Value;
            _pnlPreview.FrameEnabled = _chkFrame.Checked;
            _pnlPreview.Refresh_();

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
                : Color.FromArgb(113, 113, 122);

            _lblFrameTip.Text = dirty
                ? "外观已改动，点下面的「保存并生效」才会真正画到实际外框上。\r\n"
                  + "右上角是实时预览，和实际外框用的是同一段绘制代码。"
                : "外框用来告诉你「这扇窗口现在已经被接管了」，松手或者松开触发键就会消失。\r\n"
                  + "上面四个都可以随便改，右上角是实时预览。";
        }

        /// <summary>颜色下拉框自己画：左边一个色块，右边颜色名。</summary>
        private static void DrawColorItem(object sender, DrawItemEventArgs e)
        {
            try
            {
                if (e.Index < 0) return;
                var box = sender as ComboBox;
                if (box == null) return;
                var it = box.Items[e.Index] as ColorItem;
                if (it == null) return;

                e.DrawBackground();
                var swatch = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 3, 18, e.Bounds.Height - 6);
                using (var b = new SolidBrush(it.Color))
                {
                    e.Graphics.FillRectangle(b, swatch);
                }
                using (var p = new Pen(Color.FromArgb(140, 140, 140)))
                {
                    e.Graphics.DrawRectangle(p, swatch);
                }
                var text = new Rectangle(e.Bounds.Left + 28, e.Bounds.Top, e.Bounds.Width - 32, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, it.Text, box.Font, text, e.ForeColor,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        private void SyncHzEnabled()
        {
            bool manual = SelectedId(_cmbRefreshMode, RefreshModes.Auto) == RefreshModes.Custom;
            _numHz.Enabled = manual;

            int detected = DragEngine.DetectedRefreshHz;
            _lblHzInfo.Text = manual
                ? "手动指定时按你填的频率写窗口位置。显示器实际是 " + detected + " Hz。\r\n"
                  + "填得比屏幕刷新率更高没有意义，反而会让目标窗口处理不过来。"
                : "当前检测到显示器 " + detected + " Hz，每帧间隔约 " + (1000 / Math.Max(1, detected)) + " 毫秒。\r\n"
                  + "鼠标一秒能产生几百个事件，不节流的话目标窗口的消息队列会被灌爆，越拖越卡。";
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
            _lblKeyHint.ForeColor = warn.Length > 0 ? Color.FromArgb(161, 98, 7) : Color.FromArgb(113, 113, 122);
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
                    _lblKeyHint.ForeColor = Color.FromArgb(161, 98, 7);
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
                    _lblKeyHint.ForeColor = Color.FromArgb(161, 98, 7);
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
            _edit.SnapPreview = _chkSnapPreview.Checked;
            // ★ 同样必须回落"原配置"，不能回落"出厂默认" ★
//   用户反复报过"改了主题不生效"，这里就是最隐蔽的一条成因：
        //   只要下拉框读不出选中项，就会静默把用户选的主题退回薄荷青，
    // 而界面上看不出任何异常 —— 看着像"保存没生效"，其实是"被悄悄改了"。
            _edit.FrameColorId = SelectedId(_cmbFrameColor, _edit.FrameColorId);
     _edit.FrameStyleId = SelectedId(_cmbFrameStyle, _edit.FrameStyleId);
            _edit.FrameThickness = (int)_numFrameThick.Value;
            _edit.FrameRadius = (int)_numFrameRadius.Value;

            _edit.EdgeSnap = _chkEdgeSnap.Checked;
            _edit.SnapToOtherWindows = _chkSnapWindows.Checked;
            _edit.SnapDistance = (int)_numDist.Value;
            _edit.CornerPercent = (int)_numCorner.Value;
            _edit.DoubleClickLeftMaximize = _chkDblLeft.Checked;
            _edit.DoubleClickRightZone = _chkDblRight.Checked;
            _edit.RestoreMaximizedWhenMoved = _chkRestoreMax.Checked;
            _edit.BringToFront = _chkFront.Checked;
            _edit.AllowFullscreenWindows = _chkFullscreen.Checked;
            _edit.Blacklist = _txtBlacklist.Text;

            _edit.DragRefreshMode = SelectedId(_cmbRefreshMode, _edit.DragRefreshMode);
            _edit.DragRefreshHz = (int)_numHz.Value;

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
                _lblStatus.ForeColor = Color.FromArgb(21, 128, 61);
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
                    : Color.FromArgb(21, 128, 61);
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
            _lblStatus.ForeColor = Color.FromArgb(113, 113, 122);
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
            };
        }

        /// <summary>仅供自检：按 ID 选中某个外框颜色（等价于用户点下拉框）。</summary>
        internal void ProbeSelectFrameColor(string colorId)
        {
            SelectId(_cmbFrameColor, colorId);
            SyncPreview();
        }

        /// <summary>仅供自检：按 ID 选中某个外框样式。</summary>
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

        /// <summary>仅供自检：暴露第一个分组。</summary>
        internal Control ProbeGroup1
        {
            get
            {
                if (_groupHost == null || _groupHost.Controls.Count == 0) return null;
                return _groupHost.Controls[0];
            }
        }

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

            // 拉宽到两列
            sf.ClientSize = new Size(TwoColumnProbeWidth + 40, 820);
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
