using System;
using System.IO;
using System.Text.Json;

namespace HoldDrag
{
    /// <summary>三个鼠标键可以指派的动作。</summary>
    internal static class Actions
    {
        public const string Move = "Move";
        public const string Resize = "Resize";
        public const string Maximize = "Maximize";     // 最大化 / 还原 切换
        public const string Minimize = "Minimize";
        public const string Close = "Close";
        public const string Lower = "Lower";
        public const string AlwaysOnTop = "AlwaysOnTop";
        public const string Center = "Center";
        public const string Nothing = "Nothing";

        public static readonly string[] All =
        {
            Move, Resize, Maximize, Minimize, Close, Lower, AlwaysOnTop, Center, Nothing
        };

        public static string DisplayName(string a)
        {
            switch (a)
            {
                case Move: return "移动窗口";
                case Resize: return "调整大小";
                case Maximize: return "最大化 / 还原";
                case Minimize: return "最小化";
                case Close: return "关闭窗口";
                case Lower: return "置于最底层";
                case AlwaysOnTop: return "置顶 / 取消置顶";
                case Center: return "在屏幕上居中";
                case Nothing: return "不绑定动作";
            }
            return a;
        }

        public static bool IsValid(string a)
        {
            for (int i = 0; i < All.Length; i++) if (All[i] == a) return true;
            return false;
        }

        public static bool IsDragAction(string a)
        {
            return a == Move || a == Resize;
        }
    }

    /// <summary>
    /// 全部可配置项。默认值刻意对齐 AltSnap 的出厂绑定：
    /// 左键=移动、右键=调整大小、中键=最大化/还原。
    /// </summary>
    internal sealed class AppConfig
    {
        // 触发方式
        public string TriggerKeyId { get; set; }
        public bool RequireHold { get; set; }
        public int HoldMs { get; set; }

        // 三个鼠标键
        public string ActionLeft { get; set; }
        public string ActionRight { get; set; }
        public string ActionMiddle { get; set; }

        // 拖动时的视觉反馈
        public bool ShowActivationFrame { get; set; }   // 被接管的窗口四周亮起外框

        // 外框外观（颜色 / 样式 / 粗细 / 圆角都可以自己挑）
        public string FrameColorId { get; set; }
        public string FrameStyleId { get; set; }
        public int FrameThickness { get; set; }

        /// <summary>
        /// 外框不透明度（百分比，20~100，默认 100）。
        ///
        /// ★ 第四十五轮新增（用户要求）★
        ///   实现方式是把整层的不透明度交给 UpdateLayeredWindow 的
        ///   SourceConstantAlpha —— 由合成器统一乘一次 alpha。
        ///   好处：不需要重画位图、不需要逐像素改 alpha，拖动时零额外开销；
        ///   而且"柔光/渐变"这类本来就带 alpha 的样式会整体等比变淡，观感自然。
        /// </summary>
        public int FrameOpacity { get; set; }
        public int FrameRadius { get; set; }

        // 可选增强
        // ★ 第三十三轮：自动贴边 / 对齐其它窗口 / 边缘预览 / 吸附距离已整体删除 ★
        //   用户原话："当我拖动窗口接近屏幕边缘的时候，距离屏幕边缘还有一点距离，
        //   它就会自动贴边。这个自动贴边的这相关的功能完全没有必要全部删除掉"、
        //   "拖到屏幕边缘时预览会落在哪里这个功能也要删掉，没有必要"。
        //   这四个字段从配置里彻底移除（旧配置里的值读进来后无人使用，随保存自然消失）。
        //   注意：CornerPercent【不属于】贴边，它是"右键缩放时按住点落在哪个角区"的
        //   判定参数，必须保留。
        public bool DoubleClickLeftMaximize { get; set; }
        public bool DoubleClickRightZone { get; set; }
        public bool RestoreMaximizedWhenMoved { get; set; }
        public bool BringToFront { get; set; }
        public bool AllowFullscreenWindows { get; set; }

        /// <summary>
        /// 触发键生效后，如果目标是【全屏 / 最大化】窗口，先把它变回普通窗口再操作。
        ///
        /// ★ 第四十一轮新增（用户明确要求）★
        ///   用户原话："如果在全屏的情况下来触发按键，不管是拖动窗口还是调整窗口大小，
        ///             都会把全屏窗口变成非全屏窗口。"
        ///
        ///   为什么必须做这件事，而不是"直接在最大化/全屏状态下拖"：
        ///     · 最大化窗口的可视区域铺满工作区，鼠标落在哪都离边很远，
        ///       右键缩放的"角区"判定几乎没有意义（整个屏幕都是中心区）；
        ///     · 真全屏窗口（铺满整个显示器，含任务栏区域）被 SetWindowPos 之后
        ///       往往不重画（全屏播放器 / 游戏不是按窗口消息驱动的），
        ///       表现就是拖完一片黑。
        ///   先降级成普通窗口再操作，两个问题一起消失，手感也和 Windows 原生一致。
        ///
        ///   默认开启。关掉之后回到老行为（最大化窗口只在"允许先还原再拖"开启时才动，
        ///   真全屏窗口一律不接管）。
        /// </summary>
        public bool ExitFullscreenOnTrigger { get; set; }

        /// <summary>
        /// 晃窗手势：拖动窗口中左右来回晃动，把其它窗口全部最小化（再晃一次还原）。
        /// 默认开启；不需要的人可以在设置页关掉。
        /// </summary>
        public bool ShakeMinimizeOthers { get; set; }

        // 手感参数
        public int CornerPercent { get; set; }

        /// <summary>
        /// 不生效的窗口黑名单。逗号分隔的关键字，命中窗口标题或窗口类名即跳过。
        ///
        /// 为什么需要：无论怎么改，总会有个别程序是拖不动的（自绘标题栏、独占呈现、
        /// 或者干脆自己也在监听鼠标）。与其让用户在这些窗口上"按了没反应还以为坏了"，
        /// 不如给他一个明确的排除入口。
        /// </summary>
        public string Blacklist { get; set; }

        // 拖动刷新率节流
        public string DragRefreshMode { get; set; }

        /// <summary>
        /// 拖动预览方式（AltSnap 的 FullWin 开关）：
        ///   Outline = 空心矩形模式（AltSnap 默认，最丝滑）：拖动时只有预览框跟手，
        ///             真窗口在松手那一刻一次性落位。跟手性由构造保证 ——
        ///             预览框是自己的窗口，不依赖目标窗口的处理速度。
        ///   FullWin = 实时移动真窗口（直接拖动）。
        /// </summary>
        public string MovePreviewMode { get; set; }
        public int DragRefreshHz { get; set; }

        // ══════ 性能参数：全部照抄 AltSnap [Performance] 段的默认值 ══════
        //
        // AltSnap 的手感是公认"丝滑"的，而它的做法和直觉相反：
        //   · RefreshRate = 0（默认【不做】按帧节流！）
        //   · RezTimer    = 0（用"每 N 次鼠标事件动一次"代替按时间节流）
        //   · MoveRate    = 2（移动时每 2 个鼠标事件才真正写一次窗口）
        //   · ResizeRate  = 4（缩放时每 4 个）
        //
        // 为什么"少写几次"反而更顺：鼠标每秒能报 125~1000 次位置，
        // 每次都写窗口等于让目标程序的消息队列爆炸（浏览器/IDE 消化不过来），
        // 队列一堆积，窗口就开始"追"过期位置 —— 肉眼看到的就是一格一格顿过去。
        // 按事件计数降频 + 每次同步写，队列里永远只有一个在途请求。
        public int MoveRate { get; set; }
        public int ResizeRate { get; set; }

        /// <summary>
        /// 拖动死区：0 = 不等待；1 = 只对"已最大化 / 已吸附"的窗口等待（AltSnap 默认）；
        /// 2 = 总是等待。等待的意思是：光标要离开按下点超过 DragOutThresholdPx 才开始移动窗口，
        /// 避免刚按下时窗口被几像素的抖动带着乱晃。
        /// </summary>
        public int DragThreshold { get; set; }

        /// <summary>死区半径（像素），AltSnap 默认 20。</summary>
        public int DragOutThresholdPx { get; set; }

        // 启动与权限
        public string AutoStart { get; set; }
        public bool RunAsAdmin { get; set; }
        public bool WarnNonAdmin { get; set; }

        // 设置窗口尺寸（用户拉过之后记住，下次打开还是这个大小）
        public int SettingsWidth { get; set; }
        public int SettingsHeight { get; set; }

        public AppConfig()
        {
            TriggerKeyId = KeysUtil.DefaultTrigger;
            RequireHold = true;
            HoldMs = 600;

            ActionLeft = Actions.Move;
            ActionRight = Actions.Resize;
            ActionMiddle = Actions.Maximize;

            ShowActivationFrame = true;

            FrameColorId = FrameColors.DefaultId;
            FrameStyleId = FrameStyles.DefaultId;
            FrameThickness = 3;
            FrameRadius = 13;
            FrameOpacity = 100;

            DoubleClickLeftMaximize = true;
            DoubleClickRightZone = true;
            RestoreMaximizedWhenMoved = true;
            BringToFront = true;
            AllowFullscreenWindows = false;
            ExitFullscreenOnTrigger = true;
            ShakeMinimizeOthers = true;

            CornerPercent = 26;
            Blacklist = string.Empty;

            DragRefreshMode = RefreshModes.Auto;
            MovePreviewMode = PreviewModes.FullWin;
            // 0 = 固定刷新率模式下的"不限制"。自动模式不看这个值，直接跟随实测屏幕刷新率。
            DragRefreshHz = 0;

            // ── 性能参数：AltSnap [Performance] 的默认值 ──
            // ══════ 第十二轮：降频默认值改为 1/1（每个事件都投递）══════
            //
            //   DragBench 实测：MoveRate=2 把有效更新率压到 56Hz（125Hz 事件流的一半），
            //   肉眼就是"不跟轨迹"。AltSnap 用 2/4 是在 2010 年的低配机器上省 CPU；
            //   现在的机器上多投递的事件在工作线程队列里被合并，成本几乎为零
            //   （防堆积靠"只执行最新位置"+ 同步写，与降频无关）。
            MoveRate = 1;                   // 每个事件都投递，合并层负责防堆积
            ResizeRate = 1;
            DragThreshold = 1;              // 只对已最大化/已吸附的窗口用死区
            DragOutThresholdPx = 20;

            AutoStart = AutoStartModes.TaskScheduler;
            RunAsAdmin = true;
            WarnNonAdmin = true;

            // 0 = 用内置默认尺寸。合法值由 Normalize() 收敛到合理区间，
            // 手改成离谱数字也不会让设置窗口变成一个没法点的怪形状。
            SettingsWidth = 0;
            SettingsHeight = 0;
        }

        public AppConfig Clone()
        {
            return (AppConfig)MemberwiseClone();
        }

        /// <summary>把越界的数值拉回安全区间。配置文件是纯文本，用户可能手改坏。</summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(TriggerKeyId)) TriggerKeyId = KeysUtil.DefaultTrigger;
            if (HoldMs < 100) HoldMs = 100;
            if (HoldMs > 3000) HoldMs = 3000;

            if (!Actions.IsValid(ActionLeft)) ActionLeft = Actions.Move;
            if (!Actions.IsValid(ActionRight)) ActionRight = Actions.Resize;
            if (!Actions.IsValid(ActionMiddle)) ActionMiddle = Actions.Maximize;

            if (!FrameColors.IsValid(FrameColorId)) FrameColorId = FrameColors.DefaultId;
            if (!FrameStyles.IsValid(FrameStyleId)) FrameStyleId = FrameStyles.DefaultId;
            if (FrameThickness < 1) FrameThickness = 1;
            if (FrameThickness > 8) FrameThickness = 8;
            if (FrameRadius < 0) FrameRadius = 0;
            if (FrameRadius > 28) FrameRadius = 28;
            // 下限 20%：再低就等于"看不见"，那时用户其实是想要关掉外框总开关，
            // 而不是把不透明度拖到 0 —— 留一个下限避免"以为功能坏了"。
            if (FrameOpacity < 20) FrameOpacity = 20;
            if (FrameOpacity > 100) FrameOpacity = 100;

            if (CornerPercent < 5) CornerPercent = 5;
            if (CornerPercent > 45) CornerPercent = 45;

            if (Blacklist == null) Blacklist = string.Empty;
            if (Blacklist.Length > 2000) Blacklist = Blacklist.Substring(0, 2000);

            if (DragRefreshMode != RefreshModes.Auto && DragRefreshMode != RefreshModes.Custom)
            {
                DragRefreshMode = RefreshModes.Auto;
            }

            // ★ 用户拍板：只保留一种模式 —— 实时移动。旧配置里的 Outline
            //   （空心矩形预览）一律迁移为 FullWin；该选项已从设置页移除。
            MovePreviewMode = PreviewModes.FullWin;
            if (DragRefreshHz < 0) DragRefreshHz = 0;
            if (DragRefreshHz != 0 && DragRefreshHz < 20) DragRefreshHz = 20;
            if (DragRefreshHz > 500) DragRefreshHz = 500;

            // ══════ 第十二轮：老配置迁移 ══════
            //
            //   早期版本把 MoveRate=2/ResizeRate=4 作为默认值写进了配置文件，
            //   用户没有主动改过它 —— 但它把有效更新率砍半（125Hz 鼠标 → 62Hz
            //   窗口更新，DragBench 实测），肉眼就是"不跟鼠标轨迹"。
            //   这对 (2,4) 就是旧出厂默认的特征，静默升级到 (1,1)。
            //   用户后来手动改过任何其它值的配置不会命中这个组合。
            if (MoveRate == 2 && ResizeRate == 4)
            {
                MoveRate = 1;
                ResizeRate = 1;
            }

            // 性能参数收敛（AltSnap 里也是 max(1, ...)）
            // ★ 钩子侧丢事件（MoveRate>1）彻底退役：工作线程合并层保证永远写
            //   【最新位置】，任何降频都只会白白拉低有效更新率（dragbench 实证
            //   250 事件 → 251 次更新、1:1 跟踪）。钉死为 1。
            MoveRate = 1;
            ResizeRate = 1;
            if (DragThreshold < 0) DragThreshold = 0;
            if (DragThreshold > 2) DragThreshold = 2;
            if (DragOutThresholdPx < 0) DragOutThresholdPx = 0;
            if (DragOutThresholdPx > 200) DragOutThresholdPx = 200;

            if (AutoStart != AutoStartModes.TaskScheduler
                && AutoStart != AutoStartModes.Registry
                && AutoStart != AutoStartModes.None)
            {
                AutoStart = AutoStartModes.TaskScheduler;
            }

            // 设置窗口尺寸：0 表示"没存过"，保持不动；非 0 就必须落在能用的范围内。
            // 下限与 SettingsForm.MinimumSize 对齐，上限跟随【当前屏幕实际尺寸】，
            // 免得用户拔掉外接显示器后窗口跑到屏幕外点不到"保存"。
            //
            // ★ 上限以前写死 2400，这是个真实的体验缺陷 ★
            //   用户的显示器是 3840 宽，窗口拉到 3000 也完全放得下，
            //   却被硬压回 2400 —— 表现就是"我明明拉得更宽了，下次打开又变回去了"。
            //   现在按实际虚拟桌面尺寸算，屏幕多大就能拉多大；
            //   屏幕变小（拔显示器）时又会自动收回来，两头都照顾到。
            int maxW = MaxUsableWindowWidth();
            int maxH = MaxUsableWindowHeight();

            if (SettingsWidth != 0)
            {
                if (SettingsWidth < 578) SettingsWidth = 578;
                if (SettingsWidth > maxW) SettingsWidth = maxW;
            }
            if (SettingsHeight != 0)
            {
                if (SettingsHeight < 420) SettingsHeight = 420;
                if (SettingsHeight > maxH) SettingsHeight = maxH;
            }
        }

        /// <summary>
        /// 当前桌面能容纳的最大窗口宽度。
        ///
        /// 用虚拟桌面（所有显示器合起来的矩形）而不是主屏：
        /// 用户完全可能把设置窗口横跨两块屏来用，那是他的自由。
        /// 取不到就退回一个保守值，绝不返回 0 或负数
        /// （那会把窗口压成一条缝，比不限制还糟）。
        /// </summary>
        private static int MaxUsableWindowWidth()
        {
            try
            {
                int w = System.Windows.Forms.SystemInformation.VirtualScreen.Width;
                if (w >= 640) return w;
            }
            catch { }
            return 2400;
        }

        private static int MaxUsableWindowHeight()
        {
            try
            {
                int h = System.Windows.Forms.SystemInformation.VirtualScreen.Height;
                if (h >= 480) return h;
            }
            catch { }
            return 2400;
        }

        /// <summary>
        /// 每帧之间要等多少毫秒。返回 0 表示【不节流】。
        ///
        /// ══════ 第三十六轮：刷新率选项重新接通，且必须是【真节流】══════
        ///
        /// 用户原话："在流畅度里面可以选择刷新率……跟随屏幕还是设定一个固定的刷新率。
        /// 默认是跟随屏幕刷新率……如果还有一些别的能够完善的刷新率选项，那你也可以加进去，
        /// 但一定要让其有实际的功能"。
        ///
        ///   · Auto（默认）＝ 跟随屏幕：用 DragEngine 实测到的显示器刷新率。
        ///     为什么自动模式也节流：鼠标事件流（125~1000Hz）远高于屏幕刷新率时，
        ///     每个事件都写一次窗口 = 给目标程序的消息队列灌入它消化不了的
        ///     WM_WINDOWPOSCHANGING/WM_MOVE/WM_SIZE/WM_PAINT。队列一堆积，
        ///     窗口就开始"追"过期位置，肉眼就是一格一格顿过去。节流到刷新率之后，
        ///     目标每次都能在自己的一个显示周期内画完。
        ///   · Custom ＝ 固定值：用户指定 24~360Hz。有人就是想跑更低（省电/降温），
        ///     也有人外接 240Hz 屏想拉满，都给路。
        ///
        /// 精度保障：程序启动时已 TimeBeginPeriod(1)（见 Program.Run），
        /// Thread.Sleep 的粒度是 1ms 而不是默认的 15.6ms —— 这正是第九轮
        /// "节流造成周期性丢帧"的那个根因，现已消除。
        ///
        /// 语义保证：节流只发生在【处理完一帧之后】的补睡上，队列里最后一个
        /// 最新位置绝不会被丢掉；拖尾最后一帧由 DragEngine.End 同步补上，
        /// 松手落点永远精确。
        /// </summary>
        public int FrameIntervalMs(int detectedHz)
        {
            if (DragRefreshMode == RefreshModes.Custom)
            {
                int hz = DragRefreshHz;
                if (hz <= 0) return 0;                 // 0 = 用户主动选择"不限制"
                if (hz < 20) hz = 20;
                if (hz > 500) hz = 500;
                return (int)Math.Round(1000.0 / hz);
            }

            // Auto：跟随屏幕。实测不到（返回 0）时保守不节流，
            // 宁可多写几帧也不要凭空造一个 60Hz 上限（也可能是 30Hz 的老显示器）。
            if (detectedHz <= 0) return 0;
            if (detectedHz > 500) detectedHz = 500;
            return (int)Math.Round(1000.0 / detectedHz);
        }
    }

    internal static class PreviewModes
    {
        public const string Outline = "Outline";   // 空心矩形（AltSnap 默认）
        public const string FullWin = "FullWin";   // 实时移动真窗口
    }

    internal static class RefreshModes
    {
        public const string Auto = "Auto";
        public const string Custom = "Custom";
    }

    internal static class AutoStartModes
    {
        public const string TaskScheduler = "TaskScheduler";
        public const string Registry = "Registry";
        public const string None = "None";
    }

    /// <summary>
    /// 配置读写。三级轮换（主文件 + bak + bak2），
    /// 任何一级损坏都能自动回退，绝不因为一次坏写而丢掉用户设置。
    /// </summary>
    internal sealed class ConfigStore
    {
        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public string Dir { get; private set; }
        public string FilePath { get; private set; }
        private string BakPath { get { return FilePath + ".bak"; } }
        private string Bak2Path { get { return FilePath + ".bak2"; } }

        public AppConfig Current { get; private set; }

        public event Action Changed;

        public ConfigStore(string dir)
        {
            Dir = dir;
            FilePath = System.IO.Path.Combine(dir, "config.json");
        }

        public void Load()
        {
            AppConfig cfg = TryRead(FilePath);
            bool recovered = false;

            if (cfg == null)
            {
                cfg = TryRead(BakPath);
                if (cfg != null) recovered = true;
            }
            if (cfg == null)
            {
                cfg = TryRead(Bak2Path);
                if (cfg != null) recovered = true;
            }
            if (cfg == null)
            {
                cfg = new AppConfig();
                Log.Info("配置不存在或全部损坏，使用出厂默认值");
            }
            else if (recovered)
            {
                Log.Info("主配置损坏，已从备份恢复");
            }

            cfg.Normalize();
            Current = cfg;

            if (recovered)
            {
                try { Save(); } catch (Exception ex) { Log.Error(ex); }
            }
        }

        private static AppConfig TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return null;
                return JsonSerializer.Deserialize<AppConfig>(text, Opts);
            }
            catch (Exception ex)
            {
                Log.Error("读取配置失败 " + path + " :: " + ex.Message);
                return null;
            }
        }

        public void Save()
        {
            try
            {
                Current.Normalize();
                Directory.CreateDirectory(Dir);

                // 轮换：bak -> bak2, main -> bak
                if (File.Exists(FilePath))
                {
                    if (File.Exists(BakPath)) File.Copy(BakPath, Bak2Path, true);
                    File.Copy(FilePath, BakPath, true);
                }

                string json = JsonSerializer.Serialize(Current, Opts);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);

                // ★★★ 原子替换，绝不能"先删主文件再改名" ★★
                //
                // 原来写的是：
                //     if (File.Exists(FilePath)) File.Delete(FilePath);
                //     File.Move(tmp, FilePath);
                //
                // Delete 和 Move 之间存在一个【主文件不存在】的时间窗。
                // 用户恰好在这个窗口里关电脑 / 任务管理器强杀进程 / 系统崩溃，
                // 就会连主配置一起丢掉 —— 而这正是用户明确提出的那条要求：
                // "电脑重启后相关的配置是能够得到保存的"。
                //
                // File.Move(src, dst, overwrite: true) 在 Windows 上走的是
                // MoveFileEx + MOVEFILE_REPLACE_EXISTING，由内核在同一次操作里完成替换，
                // 不存在中间态。
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex)
            {
                Log.Error("保存配置失败 :: " + ex.Message);
            }
        }

        /// <summary>设置窗口点“保存”时调用：替换配置、落盘、通知各子系统重载。</summary>
        public void Apply(AppConfig edited)
        {
            edited.Normalize();
            Current = edited;
            Save();
            var h = Changed;
            if (h != null) h();
        }
    }
}
