using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace HoldDrag
{
    /// <summary>
    /// 开机自启的两种落地方式。
    ///
    /// 计划任务（默认）：/RL HIGHEST + /IT，登录时以最高权限静默启动，**不弹 UAC**。
    /// 这正是“始终以管理员身份运行”和“开机不要弹窗”能同时成立的关键。
    /// 注册表 Run 项：简单可靠，但程序启动时要自行提权，于是每次登录都会弹一次 UAC。
    /// </summary>
    internal static class AutoStart
    {
        public const string TaskName = "HoldDrag-AutoStart";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "HoldDrag";

        public static bool Apply(string mode, string exePath, out string message)
        {
            message = string.Empty;
            try
            {
                switch (mode)
                {
                    case AutoStartModes.TaskScheduler:
                    {
                        RemoveRegistry();
                        string err;
                        if (CreateTask(exePath, out err)) return true;

                        // 计划任务失败时退到注册表，保证“开机自启”这个功能不至于完全没有
                        RemoveTask();
                        if (WriteRegistry(exePath))
                        {
                            message = "计划任务创建失败（" + err + "），已改用注册表方式。"
                                    + "注意：注册表方式每次登录会弹一次 UAC 确认框。";
                            Log.Info(message);
                            return true;
                        }
                        message = "计划任务与注册表方式都失败了：" + err;
                        Log.Error(message);
                        return false;
                    }

                    case AutoStartModes.Registry:
                    {
                        RemoveTask();
                        if (WriteRegistry(exePath)) return true;
                        message = "写注册表 Run 项失败，可能被安全软件拦截。";
                        Log.Error(message);
                        return false;
                    }

                    default:
                        RemoveTask();
                        RemoveRegistry();
                        return true;
                }
            }
            catch (Exception ex)
            {
                message = "设置开机自启时出错：" + ex.Message;
                Log.Error(ex);
                return false;
            }
        }

        public static string Detect()
        {
            try
            {
                string err;
                if (TaskExists(out err)) return AutoStartModes.TaskScheduler;

                // schtasks 被安全软件拦截时，这里失败与“任务不存在”外观一致，
                // 因此把原始输出记进日志，用户从日志能看出到底是被拦了还是真没有。
                if (!string.IsNullOrEmpty(err))
                {
                    Log.Info("查询计划任务未成功（若被安全软件/策略拦截会走到这里）：" + err);
                }

                if (RegistryExists()) return AutoStartModes.Registry;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            return AutoStartModes.None;
        }

        // ── 计划任务 ────────────────────────────────────────────

        private static bool CreateTask(string exePath, out string error)
        {
            string args = "/Create /F /TN \"" + TaskName + "\" /SC ONLOGON /RL HIGHEST /IT /TR \"\\\"" + exePath + "\\\"\"";
            int code = Run("schtasks.exe", args, out error);
            if (code == 0) return true;
            Log.Error("schtasks 创建失败 code=" + code + " :: " + error);
            return false;
        }

        private static void RemoveTask()
        {
            string err;
            Run("schtasks.exe", "/Delete /F /TN \"" + TaskName + "\"", out err);
        }

        private static bool TaskExists(out string error)
        {
            return Run("schtasks.exe", "/Query /TN \"" + TaskName + "\"", out error) == 0;
        }

        // ── 注册表 ──────────────────────────────────────────────

        private static bool WriteRegistry(string exePath)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return false;
                    key.SetValue(RunValueName, "\"" + exePath + "\"");
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return false;
            }
        }

        private static void RemoveRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key != null && key.GetValue(RunValueName) != null) key.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
        }

        private static bool RegistryExists()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return key != null && key.GetValue(RunValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static int Run(string exe, string args, out string output)
        {
            output = string.Empty;
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    p.WaitForExit(8000);
                    output = (so + " " + se).Trim();
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }
    }
}
