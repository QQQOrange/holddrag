using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace HoldDrag
{
    /// <summary>
    /// 触发键的标识体系。
    ///
    /// Id 采用两类前缀：
    ///   "K:0xXX"  普通键盘按键，按虚拟键码（VK）标识
    ///   "K:LAlt" / "K:RAlt" / "K:LCtrl" / "K:RCtrl" / "K:LShift" / "K:RShift"
    ///             修饰键区分左右——低级键盘钩子里左右 Alt/Ctrl 靠 extended 标志区分，
    ///             左右 Shift 靠扫描码区分，因此必须在此处归一化。
    ///   "M:1" / "M:2"  鼠标侧键（XBUTTON1 / XBUTTON2）
    ///
    /// 之所以要自己建这套标识，是因为 System.Windows.Forms.Keys 无法表达
    /// “右 Alt 与左 Alt 不同”“鼠标侧键”这两类情况。
    /// </summary>
    internal static class KeysUtil
    {
        public const string LeftAlt = "K:LAlt";
        public const string RightAlt = "K:RAlt";
        public const string LeftCtrl = "K:LCtrl";
        public const string RightCtrl = "K:RCtrl";
        public const string LeftShift = "K:LShift";
        public const string RightShift = "K:RShift";
        public const string MouseX1 = "M:1";
        public const string MouseX2 = "M:2";

        public const string DefaultTrigger = "K:0x0D"; // Enter

        private const uint VK_SHIFT = 0x10;
        private const uint VK_CONTROL = 0x11;
        private const uint VK_MENU = 0x12;   // Alt
        private const uint VK_LSHIFT = 0xA0;
        private const uint VK_RSHIFT = 0xA1;
        private const uint VK_LCONTROL = 0xA2;
        private const uint VK_RCONTROL = 0xA3;
        private const uint VK_LMENU = 0xA4;
        private const uint VK_RMENU = 0xA5;

        private const ushort SCAN_LSHIFT = 0x2A;
        private const ushort SCAN_RSHIFT = 0x36;

        private static readonly Dictionary<int, string> Display = new Dictionary<int, string>
        {
            { 0x0D, "Enter 回车" },
            { 0x20, "空格" },
            { 0x09, "Tab" },
            { 0x14, "CapsLock 大写锁定" },
            { 0x5D, "菜单键 (Applications)" },
            { 0x08, "Backspace 退格" },
            { 0x2D, "Insert" },
            { 0x2E, "Delete" },
            { 0x24, "Home" },
            { 0x23, "End" },
            { 0x21, "Page Up" },
            { 0x22, "Page Down" },
            { 0x25, "方向键 左" },
            { 0x26, "方向键 上" },
            { 0x27, "方向键 右" },
            { 0x28, "方向键 下" },
            { 0x1B, "Esc" },
            { 0x5B, "左 Win" },
            { 0x5C, "右 Win" },
            { 0x90, "NumLock" },
            { 0x91, "ScrollLock" },
            { 0x13, "Pause" },
            { 0x2C, "PrintScreen" },
            { 0xC0, "` ~" },
            { 0xBD, "- _" },
            { 0xBB, "= +" },
            { 0xDB, "[ {" },
            { 0xDD, "] }" },
            { 0xDC, "\\ |" },
            { 0xBA, "; :" },
            { 0xDE, "' \"" },
            { 0xBC, ", <" },
            { 0xBE, ". >" },
            { 0xBF, "/ ?" },
        };

        private static readonly Dictionary<int, string> SpecialDisplay = new Dictionary<int, string>();

        static KeysUtil()
        {
            // 空——特殊键名在 DisplayName 中直接判断，避免额外字典查表开销
        }

        /// <summary>把低级键盘钩子事件归一化成 Id；返回 null 表示不认识。</summary>
        public static string CanonicalFromHook(Native.KBDLLHOOKSTRUCT kb)
        {
            uint vk = kb.vkCode;
            uint scan = kb.scanCode;
            bool ext = (kb.flags & Native.LLKHF_EXTENDED) != 0;

            switch (vk)
            {
                case VK_SHIFT:
                    return scan == SCAN_RSHIFT ? RightShift : LeftShift;
                case VK_LSHIFT: return LeftShift;
                case VK_RSHIFT: return RightShift;
                case VK_CONTROL:
                case VK_LCONTROL:
                case VK_RCONTROL:
                    return ext ? RightCtrl : LeftCtrl;
                case VK_MENU:
                case VK_LMENU:
                case VK_RMENU:
                    return ext ? RightAlt : LeftAlt;
                default:
                    return "K:0x" + vk.ToString("X2");
            }
        }

        /// <summary>低级鼠标钩子事件归一化成 Id（仅侧键有资格当触发键）。</summary>
        public static string CanonicalFromMouseHook(int msg, Native.MSLLHOOKSTRUCT ms)
        {
            if (msg != Native.WM_XBUTTONDOWN) return null;
            int btn = (int)(ms.mouseData >> 16);
            if (btn == 1) return MouseX1;
            if (btn == 2) return MouseX2;
            return null;
        }

        /// <summary>判断一次键盘事件是否命中指定的触发键。</summary>
        public static bool MatchesKeyboard(string id, Native.KBDLLHOOKSTRUCT kb)
        {
            if (string.IsNullOrEmpty(id) || id[0] != 'K') return false;

            uint vk = kb.vkCode;
            uint scan = kb.scanCode;
            bool ext = (kb.flags & Native.LLKHF_EXTENDED) != 0;

            switch (id)
            {
                case LeftShift: return vk == VK_SHIFT || vk == VK_LSHIFT ? (scan != SCAN_RSHIFT) : false;
                case RightShift: return (vk == VK_SHIFT || vk == VK_RSHIFT) && scan == SCAN_RSHIFT;
                case LeftCtrl: return (vk == VK_CONTROL || vk == VK_LCONTROL) && !ext;
                case RightCtrl: return (vk == VK_CONTROL || vk == VK_RCONTROL) && ext;
                case LeftAlt: return (vk == VK_MENU || vk == VK_LMENU) && !ext;
                case RightAlt: return (vk == VK_MENU || vk == VK_RMENU) && ext;
                case MouseX1:
                case MouseX2:
                    return false;
            }

            if (id.Length < 3 || id[1] != ':') return false;
            int want;
            if (!TryParseHex(id.Substring(2), out want)) return false;

            // 泛化匹配：若配置里的 VK 是修饰键的“通用”形式，则左右都算命中
            if (want == VK_SHIFT && (vk == VK_SHIFT || vk == VK_LSHIFT || vk == VK_RSHIFT)) return true;
            if (want == VK_CONTROL && (vk == VK_CONTROL || vk == VK_LCONTROL || vk == VK_RCONTROL)) return true;
            if (want == VK_MENU && (vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU)) return true;

            return (int)vk == want;
        }

        /// <summary>判断一次鼠标事件是否命中指定的触发键。</summary>
        public static bool MatchesMouse(string id, int msg, Native.MSLLHOOKSTRUCT ms)
        {
            if (id != MouseX1 && id != MouseX2) return false;
            bool isDown = msg == Native.WM_XBUTTONDOWN;
            bool isUp = msg == Native.WM_XBUTTONUP;
            if (!isDown && !isUp) return false;
            int btn = (int)(ms.mouseData >> 16);
            if (id == MouseX1) return btn == 1;
            return btn == 2;
        }

        /// <summary>把 WinForms 的按键（设置窗口录制用）转成 Id。</summary>
        public static string FromWinFormsKey(Keys key)
        {
            switch (key)
            {
                case Keys.LShiftKey: return LeftShift;
                case Keys.RShiftKey: return RightShift;
                case Keys.LControlKey: return LeftCtrl;
                case Keys.RControlKey: return RightCtrl;
                case Keys.LMenu: return LeftAlt;
                case Keys.RMenu: return RightAlt;
                case Keys.LWin: return "K:0x5B";
                case Keys.RWin: return "K:0x5C";
                case Keys.ShiftKey: return LeftShift;
                case Keys.ControlKey: return LeftCtrl;
                case Keys.Menu: return LeftAlt;
            }

            int vk = (int)key & 0xFF;
            if (vk == 0) vk = (int)key;
            return "K:0x" + vk.ToString("X2");
        }

        /// <summary>鼠标侧键（设置窗口录制用）。</summary>
        public static string FromMouseButton(MouseButtons b)
        {
            if (b == MouseButtons.XButton1) return MouseX1;
            if (b == MouseButtons.XButton2) return MouseX2;
            return null;
        }

        /// <summary>把 Id 折算成可用于 GetAsyncKeyState 的虚拟键码；返回 0 表示无法折算。</summary>
        public static int ToVirtualKey(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            switch (id)
            {
                case LeftShift: return (int)VK_LSHIFT;
                case RightShift: return (int)VK_RSHIFT;
                case LeftCtrl: return (int)VK_LCONTROL;
                case RightCtrl: return (int)VK_RCONTROL;
                case LeftAlt: return (int)VK_LMENU;
                case RightAlt: return (int)VK_RMENU;
                case MouseX1: return 0x05; // VK_XBUTTON1
                case MouseX2: return 0x06; // VK_XBUTTON2
            }

            int vk;
            if (id.Length >= 3 && id[1] == ':' && TryParseHex(id.Substring(2), out vk)) return vk;
            return 0;
        }

        /// <summary>给用户看的名字。</summary>
        public static string DisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "（未设置）";
            switch (id)
            {
                case LeftAlt: return "左 Alt";
                case RightAlt: return "右 Alt（AltGr）";
                case LeftCtrl: return "左 Ctrl";
                case RightCtrl: return "右 Ctrl";
                case LeftShift: return "左 Shift";
                case RightShift: return "右 Shift";
                case MouseX1: return "鼠标侧键 前进 (X1)";
                case MouseX2: return "鼠标侧键 后退 (X2)";
            }

            int vk;
            if (id.Length >= 3 && id[1] == ':' && TryParseHex(id.Substring(2), out vk))
            {
                string name;
                if (Display.TryGetValue(vk, out name)) return name;

                if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);           // F1..F24
                if (vk >= 0x30 && vk <= 0x39) return "数字 " + (char)vk;          // 0..9
                if (vk >= 0x41 && vk <= 0x5A) return "字母 " + (char)vk;          // A..Z
                if (vk >= 0x60 && vk <= 0x69) return "小键盘 " + (vk - 0x60);
                if (vk == 0x6A) return "小键盘 *";
                if (vk == 0x6B) return "小键盘 +";
                if (vk == 0x6D) return "小键盘 -";
                if (vk == 0x6E) return "小键盘 .";
                if (vk == 0x6F) return "小键盘 /";

                return "虚拟键 0x" + vk.ToString("X2");
            }
            return id;
        }

        /// <summary>该触发键是否属于“切换型”按键（需要额外压制，否则会切走状态）。</summary>
        public static bool IsToggleKey(string id)
        {
            int vk;
            if (id != null && id.Length >= 3 && id[1] == ':' && TryParseHex(id.Substring(2), out vk))
            {
                return vk == 0x14 /*CapsLock*/ || vk == 0x90 /*NumLock*/ || vk == 0x91 /*ScrollLock*/;
            }
            return false;
        }

        /// <summary>该触发键是否会与系统组合键冲突（Win 键需要提示用户）。</summary>
        public static bool IsWindowsKey(string id)
        {
            return id == "K:0x5B" || id == "K:0x5C";
        }

        private static bool TryParseHex(string s, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s)) return false;
            try
            {
                value = Convert.ToInt32(s, 16);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
