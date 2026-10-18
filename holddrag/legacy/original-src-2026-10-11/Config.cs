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
        public bool ShowActivationFrame { get; set; }   // 被接管的窗口四周亮一圈强调色外框
        public bool SnapPreview { get; set; }           // 贴边时预览落点

        // 外框外观（颜色 / 样式 / 粗细 / 圆角都可以自己挑）
        public string FrameColorId { get; set; }
        public string FrameStyleId { get; set; }
        public int FrameThickness { get; set; }
        public int FrameRadius { get; set; }

        // 可选增强
        public bool EdgeSnap { get; set; }
        public bool SnapToOtherWindows { get; set; }
        public bool DoubleClickLeftMaximize { get; set; }
        public bool DoubleClickRightZone { get; set; }
        public bool RestoreMaximizedWhenMoved { get; set; }
        public bool BringToFront { get; set; }
        public bool AllowFullscreenWindows { get; set; }

        // 手感参数
        public int SnapDistance { get; set; }
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
        public int DragRefreshHz { get; set; }

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
            SnapPreview = true;

            FrameColorId = FrameColors.DefaultId;
            FrameStyleId = FrameStyles.DefaultId;
            FrameThickness = 3;
            FrameRadius = 13;

            EdgeSnap = true;
            SnapToOtherWindows = true;
            DoubleClickLeftMaximize = true;
            DoubleClickRightZone = true;
            RestoreMaximizedWhenMoved = true;
            BringToFront = true;
            AllowFullscreenWindows = false;

            SnapDistance = 25;
            CornerPercent = 26;
            Blacklist = string.Empty;

            DragRefreshMode = RefreshModes.Auto;
            DragRefreshHz = 0;              // 0 = 用实测到的屏幕刷新率

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

            if (SnapDistance < 1) SnapDistance = 1;
            if (SnapDistance > 200) SnapDistance = 200;

            if (CornerPercent < 5) CornerPercent = 5;
            if (CornerPercent > 45) CornerPercent = 45;

            if (Blacklist == null) Blacklist = string.Empty;
            if (Blacklist.Length > 2000) Blacklist = Blacklist.Substring(0, 2000);

            if (DragRefreshMode != RefreshModes.Auto && DragRefreshMode != RefreshModes.Custom)
            {
                DragRefreshMode = RefreshModes.Auto;
            }
            if (DragRefreshHz < 0) DragRefreshHz = 0;
            if (DragRefreshHz != 0 && DragRefreshHz < 20) DragRefreshHz = 20;
            if (DragRefreshHz > 500) DragRefreshHz = 500;

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
        /// 每帧之间要等多少毫秒。返回 0 表示不节流。
        /// 自动模式用实测屏幕刷新率；拿不到或者异常值一律回退 60Hz。
        /// </summary>
        public int FrameIntervalMs(int detectedHz)
        {
            int hz;
            if (DragRefreshMode == RefreshModes.Custom && DragRefreshHz > 0) hz = DragRefreshHz;
            else hz = detectedHz;

            if (hz < 20) hz = 60;
            if (hz > 500) hz = 500;

            int ms = 1000 / hz;
            return ms < 1 ? 1 : ms;
        }
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

         // ★★★ 原子替换，绝不能"先删主文件再改名" ★★★
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
