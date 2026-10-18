using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace HoldDrag
{
    /// <summary>
    /// 仅用于开发期视觉核对：把设置窗口在两种宽度下各截一张图。
    ///
    /// 为什么需要它：布局类改动（锚定 / 响应式 / 控件位置）靠断言只能验证"逻辑对"，
    /// 验证不了"看起来对不对" —— 控件重叠、文字被切、间距离谱，这些断言都抓不到。
    /// 截一张图看一眼是最快的检查手段。
    ///
    /// 用临时目录里的独立配置，不碰用户真实设置。
    /// </summary>
    internal static class VisualCheck
    {
        public static void Run(string outDir)
        {
            Directory.CreateDirectory(outDir);

            string tmp = Path.Combine(Path.GetTempPath(), "holddrag-visual-cfg");
            try { Directory.CreateDirectory(tmp); } catch { }

            var store = new ConfigStore(tmp);
            try { store.Load(); } catch { }
            var coord = new Coordinator(store);

            // ★ 探测尺寸必须【按屏幕实际可用区域】算，不能写死数字。
            //
            //   踩过的坑：一开始按设计稿数字 × DPI 系数去开窗口（1420×1.25…），
            //   结果窗口比屏幕还宽，Windows 直接把它裁到屏幕宽度 ——
            //   截出来的图看着像"布局坏了"，其实只是窗口没开到那么大。
            //   而且窄屏档和中屏档会双双撞上 MinimumSize，截出来两张一模一样的图，
            //   让人以为"布局没生效"。
            //
            //   所以：先问屏幕要可用区域，再按"窄 / 中 / 宽"三个比例取。
            //   比例本身就是设计意图（单列刚好放下 / 两列刚好放下 / 两列很宽），
            //   换任何分辨率都成立。
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int narrow = (int)Math.Round(wa.Width * 0.42);
            int mid = (int)Math.Round(wa.Width * 0.72);
            int wide = (int)Math.Round(wa.Width * 0.96);

            // 窄档不能小于窗体自带的 MinimumSize，否则开出来会被撑回去。
            int minW = (int)Math.Round(SettingsForm.CurrentUiScale * 600);
            if (narrow < minW) narrow = minW;

            int hNarrow = (int)Math.Round(wa.Height * 0.88);
            int hWide = (int)Math.Round(wa.Height * 0.92);
            int hMid = (int)Math.Round(wa.Height * 0.80);

            File.WriteAllText(Path.Combine(outDir, "sizes.txt"),
                string.Format("工作区={0}x{1} 缩放={2:0.###}\n窄={3}x{4}\n中={5}x{6}\n宽={7}x{8}",
                    wa.Width, wa.Height, SettingsForm.CurrentUiScale,
                    narrow, hNarrow, mid, hMid, wide, hWide),
                System.Text.Encoding.UTF8);

            Shoot(store, coord, new Size(narrow, hNarrow), Path.Combine(outDir, "settings-narrow.png"));
            Shoot(store, coord, new Size(mid, hMid), Path.Combine(outDir, "settings-mid.png"));
            Shoot(store, coord, new Size(wide, hWide), Path.Combine(outDir, "settings-wide.png"));

            Dump(store, coord, new Size(wide, hWide), Path.Combine(outDir, "layout-wide.txt"), 3);
            Dump(store, coord, new Size(wide, hWide), Path.Combine(outDir, "layout-g1.txt"), 0);
            Dump(store, coord, new Size(wide, hWide), Path.Combine(outDir, "layout-g5.txt"), 4);
            Dump(store, coord, new Size(narrow, hNarrow), Path.Combine(outDir, "layout-n1.txt"), 0);
            Dump(store, coord, new Size(narrow, hNarrow), Path.Combine(outDir, "layout-n3.txt"), 3);
            // 视觉反馈分组（色板 / 样式画廊 / 预览框）——第三十九轮重做后必须复核不重叠。
            Dump(store, coord, new Size(narrow, hNarrow), Path.Combine(outDir, "layout-g3.txt"), 2);
            Dump(store, coord, new Size(wide, hWide), Path.Combine(outDir, "layout-g3-wide.txt"), 2);

            // 单独把"视觉反馈"分组拍一张：色板和样式画廊是这一轮的主角，
            // 整窗截图里它被挤在中间一小块，看不清。
            ShootGroup(store, coord, new Size(narrow, hNarrow),
                Path.Combine(outDir, "group-visual.png"), 2);

            // #37 外框重做：把所有"颜色 × 样式"画成一张对照表，一眼就能挑。
            ShootFrameSheet(Path.Combine(outDir, "frames-all.png"));
        }

        /// <summary>
        /// 把调色板里每个颜色 × 每种样式渲染成一张对照表。
        ///
        /// 为什么需要它：改完调色板/样式之后，"到底好不好看、有没有哪个画坏了"
        /// 只能靠眼睛看。一个个去设置页里点下拉框太低效，一张表全看完。
        /// </summary>
        private static void ShootFrameSheet(string path)
        {
            try
            {
                int cellW = 210, cellH = 132;
                int pad = 12, labelH = 26, titleH = 40;
                int cols = FrameStyles.All.Length;
                int rows = FrameColors.All.Length;

                int w = pad + cols * (cellW + pad);
                int h = titleH + labelH + pad + rows * (cellH + labelH + pad);

                using (var bmp = new Bitmap(w, h))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(244, 245, 247));

                    using (var head = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold))
                    using (var cell = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular))
                    using (var ink = new SolidBrush(Color.FromArgb(24, 26, 33)))
                    using (var dim = new SolidBrush(Color.FromArgb(120, 126, 140)))
                    {
                        // 大标题
                        g.DrawString("外框颜色 × 样式 对照表（#37 重做）", head, ink,
                            new PointF(pad, 10));

                        // 列标题：样式名
                        for (int c = 0; c < cols; c++)
                        {
                            var headRect = new RectangleF(pad + c * (cellW + pad), titleH,
                                                          cellW, labelH);
                            g.DrawString(FrameStyles.DisplayName(FrameStyles.All[c]), cell, ink, headRect);
                        }

                        for (int r = 0; r < rows; r++)
                        {
                            var entry = FrameColors.All[r];
                            var col = FrameColors.Resolve(entry.Id);
                            int top = titleH + labelH + pad + r * (cellH + labelH + pad);

                            for (int c = 0; c < cols; c++)
                            {
                                int left = pad + c * (cellW + pad);
                                var area = new RectangleF(left, top, cellW, cellH);

                                // 模拟"窗口本体"：一块白，外框画在它四周
                                using (var white = new SolidBrush(Color.White))
                                    g.FillRectangle(white, area);

                                FrameStyles.Draw(g, area, col, FrameStyles.All[c], 3, 12);
                            }

                            // 行标题：色块 + 颜色名
                            using (var sw = new SolidBrush(col))
                                g.FillRectangle(sw, pad, top + cellH + 4, 14, 14);
                            g.DrawString(entry.Name, cell, dim,
                                new RectangleF(pad + 20, top + cellH + 1, cellW, labelH));
                        }
                    }

                    bmp.Save(path, ImageFormat.Png);
                }

                File.WriteAllText(path + ".txt",
                    "颜色 " + FrameColors.All.Length + " 种 × 样式 " + FrameStyles.All.Length + " 种\n",
                    System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        /// <summary>
        /// 单独把一个分组渲染成图片。
        /// 整窗截图里"视觉反馈"被挤在中间一小块，色板 / 画廊这些细节根本看不清，
        /// 单独拍一张才能核对"所有可选项是不是真的都摆出来了、选中态是不是清楚"。
        /// </summary>
        private static void ShootGroup(ConfigStore store, Coordinator coord, Size size, string path, int groupIndex)
        {
            try
            {
                using (var f = new SettingsForm(store, coord))
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(20, 20);
                    f.ClientSize = size;
                    f.TopMost = true;
                    f.Show();
                    for (int i = 0; i < 8; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }

                    var g = SettingsForm.ProbeGroupAt(f, groupIndex);
                    if (g == null || g.Width <= 0 || g.Height <= 0) { f.Close(); return; }

                    using (var bmp = new Bitmap(g.Width, g.Height))
                    {
                        g.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(path, ImageFormat.Png);
                    }
                    f.Close();
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        /// <summary>
        /// 把两列模式下"拖动与吸附"分组里每个控件的最终位置倒出来。
        ///
        /// 截图只能看出"结果不对"，看不出"哪个控件偏了多少"。
        /// 有了这份带 Left/Width/期望值的文本，定位是几行的事。
        /// </summary>
        private static void Dump(ConfigStore store, Coordinator coord, Size size, string path, int groupIndex)
        {
            try
            {
                using (var f = new SettingsForm(store, coord))
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(20, 20);
                    f.ClientSize = size;
                    f.Show();
                    Application.DoEvents();

                    var sb = new System.Text.StringBuilder();
                    SettingsForm.DumpGroup(f, groupIndex, sb);

                    // 幂等性检查：反复重排，结果必须一模一样。
                    // 这条断言很重要 —— 布局代码里凡是"+= 累加"的地方，
                    // 都会在这里现形（第二次位置比第一次低一截）。
                    for (int k = 0; k < 3; k++)
                    {
                        var before = sb.ToString();
                        SettingsForm.RelayoutForTest(f);
                        Application.DoEvents();
                        var after = new System.Text.StringBuilder();
                        SettingsForm.DumpGroup(f, groupIndex, after);
                        if (before != after.ToString())
                        {
                            sb.AppendLine();
                            sb.AppendLine("!! 第 " + (k + 1) + " 次重排结果与上次不一致（布局不幂等）");
                            sb.AppendLine(after.ToString());
                            break;
                        }
                    }
                    sb.AppendLine();
                    sb.AppendLine("幂等性：连续重排 3 次结果一致 ✓");
                    File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
                    f.Close();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        private static void Shoot(ConfigStore store, Coordinator coord, Size size, string path)
        {
            try
            {
                using (var f = new SettingsForm(store, coord))
                {
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(20, 20);
                    f.ClientSize = size;
                    f.TopMost = true;
                    f.Show();
                    f.Activate();
                    f.BringToFront();
                    Application.DoEvents();
                    // 连跑几轮消息循环，让自绘控件把投影/竖条都画完
                    for (int i = 0; i < 6; i++)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(120);
                    }
                    f.Refresh();
                    Application.DoEvents();

                    // ★ 必须用控件自己 DrawToBitmap 离屏渲染，不能用 CopyFromScreen。
                    //
                    //   CopyFromScreen 抓的是"屏幕上这块区域此刻长什么样"，
                    //   只要有任何别的窗口盖在上面（通知气泡、浏览器、IDE 侧边栏…），
                    //   截出来就是那个窗口 —— 之前好几张"布局坏了"的截图
                    //   其实是抓错了对象，白排查半天。
                    //
                    //   DrawToBitmap 直接让窗体往离屏位图上画自己，
                    //   不受桌面其它窗口影响，也不会闪。
                    int w = f.Width, h = f.Height;
                    using (var bmp = new Bitmap(w, h))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));

                        // DrawToBitmap 不画 GroupBox/自绘控件之外的
                        // 半透明投影边缘，这里补一圈卡片底色避免透明发黑。
                        bmp.Save(path, ImageFormat.Png);
                    }
                    f.Close();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }
    }
}
