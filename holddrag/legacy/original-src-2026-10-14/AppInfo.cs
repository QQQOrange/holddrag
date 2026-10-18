using System;
using System.Reflection;

namespace HoldDrag
{
    /// <summary>
    /// 应用元信息（"关于"分组、帮助页、日志抬头共用同一处）。
    ///
    /// ★ 版本号为什么从程序集读，而不是写一个字符串常量 ★
    ///   写常量就会和 csproj 里的 &lt;Version&gt; 形成【两份真相】——
    ///   改一处忘一处，界面上显示的版本和实际构建的版本对不上。
    ///   而这类"看起来对、其实错了"的信息最难被发现，所以一律从程序集取。
    /// </summary>
    internal static class AppInfo
    {
        /// <summary>中文名（界面显示用）。</summary>
        public const string Name = "长按拖窗";

        /// <summary>英文名 / 进程名。</summary>
        public const string EnglishName = "HoldDrag";

        /// <summary>一句话定位（"关于"分组里显示）。</summary>
        public const string Tagline = "长按触发键，随手拖动 / 缩放任意窗口";

        /// <summary>当前版本号，取自程序集（单一真相）。</summary>
        public static string Version
        {
            get
            {
                try
                {
                    var v = Assembly.GetExecutingAssembly().GetName().Version;
                    if (v == null) return "1.0.0";
                    // 只显示三段：第四段是构建号，对用户没有意义
                    return v.Major + "." + v.Minor + "." + Math.Max(0, v.Build);
                }
                catch
                {
                    return "1.0.0";
                }
            }
        }

        /// <summary>本版本的更新时间。</summary>
        public const string ReleaseDate = "2026-10-12";

        /// <summary>开发者。</summary>
        public const string Developer = "QQQOrange";

        /// <summary>开发过程中使用的工具。</summary>
        public const string Tools = "DeepSeek";
    }
}
