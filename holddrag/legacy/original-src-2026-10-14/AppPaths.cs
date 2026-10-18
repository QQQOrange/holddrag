using System;
using System.IO;
using System.Windows.Forms;

namespace HoldDrag
{
    /// <summary>
    /// 统一的“本程序 exe 路径”解析。
    ///
    /// 直接写 Application.ExecutablePath 有个坑：以 `dotnet HoldDrag.dll` 方式启动时
    /// 它返回的是 dotnet.exe 的路径，于是内嵌图标会取成 .NET 的图标、
    /// 开机自启会注册错的可执行文件。这里两种情况都处理掉。
    /// </summary>
    internal static class AppPaths
    {
        public static string ExePath
        {
            get
            {
                try
                {
                    string p = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(p)
                        && !string.Equals(Path.GetFileName(p), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return p;
                    }

                    var asm = System.Reflection.Assembly.GetEntryAssembly();
                    string name = asm != null ? asm.GetName().Name : "HoldDrag";
                    if (string.IsNullOrEmpty(name)) name = "HoldDrag";

                    // 故意不用 Assembly.Location：单文件发布下它恒为空串（IL3000）
                    string guess = Path.Combine(AppContext.BaseDirectory, name + ".exe");
                    if (File.Exists(guess)) return guess;
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
                return Application.ExecutablePath;
            }
        }
    }
}
