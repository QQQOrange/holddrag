using System.Reflection;
using System.Runtime.InteropServices;

// 启动器是用户唯一直接接触的那个 exe（更新替换的也是它），
// 所以它必须自带版本/产品信息：否则资源管理器、任务管理器、SmartScreen 里都是 0.0.0.0 和空白，
// 排查"用户装的是哪一版启动器"时完全无从下手。
[assembly: AssemblyTitle("长按拖窗 HoldDrag")]
[assembly: AssemblyDescription("启动器：检查 .NET 10 桌面运行时，解包并启动主程序。")]
[assembly: AssemblyCompany("QQQOrange")]
[assembly: AssemblyProduct("长按拖窗")]
[assembly: AssemblyCopyright("GPL-3.0 · 派生自 AltDrag")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyVersion("1.11.1.0")]
[assembly: AssemblyFileVersion("1.11.1.0")]
[assembly: AssemblyInformationalVersion("1.11.1")]
[assembly: ComVisible(false)]
