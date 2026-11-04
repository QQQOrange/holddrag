// HoldDrag · Platform/AutoStart.cs

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HoldDrag;

internal static class AutoStart
{
	public const string TaskName = "HoldDrag-AutoStart";

	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string RunValueName = "HoldDrag";

	public static bool Apply(string mode, string exePath, out string message)
	{
		message = string.Empty;
		try
		{
			if (!(mode == "TaskScheduler"))
			{
				if (mode == "Registry")
				{
					RemoveTask();
					if (WriteRegistry(exePath))
					{
						return true;
					}
					message = Loc.T("写注册表 Run 项失败，可能被安全软件拦截。");
					Log.Error(message);
					return false;
				}
				return RemoveAll(out message);
			}
			RemoveRegistry();
			if (CreateTask(exePath, out var error))
			{
				return true;
			}
			RemoveTask();
			if (WriteRegistry(exePath))
			{
				message = Loc.T("计划任务创建失败（") + error + Loc.T("），已改用注册表方式。注意：注册表方式每次登录会弹一次 UAC 确认框。");
				Log.Info(message);
				return true;
			}
			message = Loc.T("计划任务与注册表方式都失败了：") + error;
			Log.Error(message);
			return false;
		}
		catch (Exception ex)
		{
			message = Loc.T("设置开机自启时出错：") + ex.Message;
			Log.Error(ex);
			return false;
		}
	}

	/// <summary>
	/// 关闭开机自启：删完要**再查一遍**确认真的没了。
	/// 之前无论 schtasks 成功与否都返回 true，界面会显示"已保存并生效"，
	/// 而下次启动 Detect() 又查到计划任务、把配置改回开启，用户会以为程序在自作主张。
	/// </summary>
	private static bool RemoveAll(out string message)
	{
		message = string.Empty;
		RemoveTask();
		RemoveRegistry();
		string text = null;
		bool flag = TaskExists(out text);
		bool flag2 = RegistryExists();
		if (!flag && !flag2)
		{
			return true;
		}
		if (flag)
		{
			message = Loc.T("计划任务没能删掉（多半是它以管理员权限创建，而现在程序不是管理员）。请右键以管理员身份运行后重试。");
		}
		else
		{
			message = Loc.T("注册表自启项没能删掉，可能被安全软件拦截。");
		}
		if (!string.IsNullOrEmpty(text))
		{
			message = message + " " + text;
		}
		Log.Error("关闭开机自启失败：" + message);
		return false;
	}

	public static string Detect()
	{
		try
		{
			if (TaskExists(out var error))
			{
				return "TaskScheduler";
			}
			if (!string.IsNullOrEmpty(error))
			{
				Log.Info("查询计划任务未成功（若被安全软件/策略拦截会走到这里）：" + error);
			}
			if (RegistryExists())
			{
				return "Registry";
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return "None";
	}

	private static bool CreateTask(string exePath, out string error)
	{
		string args = "/Create /F /TN \"HoldDrag-AutoStart\" /SC ONLOGON /RL HIGHEST /IT /TR \"\\\"" + exePath + "\\\"\"";
		int num = Run("schtasks.exe", args, out error);
		if (num == 0)
		{
			return true;
		}
		Log.Error("schtasks 创建失败 code=" + num + " :: " + error);
		return false;
	}

	private static void RemoveTask()
	{
		Run("schtasks.exe", "/Delete /F /TN \"HoldDrag-AutoStart\"", out var _);
	}

	private static bool TaskExists(out string error)
	{
		return Run("schtasks.exe", "/Query /TN \"HoldDrag-AutoStart\"", out error) == 0;
	}

	private static bool WriteRegistry(string exePath)
	{
		try
		{
			using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true))
			{
				if (registryKey == null)
				{
					return false;
				}
				registryKey.SetValue("HoldDrag", "\"" + exePath + "\"");
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
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
			if (registryKey != null && registryKey.GetValue("HoldDrag") != null)
			{
				registryKey.DeleteValue("HoldDrag", throwOnMissingValue: false);
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
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: false);
			return registryKey != null && registryKey.GetValue("HoldDrag") != null;
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
			ProcessStartInfo startInfo = new ProcessStartInfo(exe, args)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using Process process = Process.Start(startInfo);
			if (process == null)
			{
				output = Loc.T("无法启动 ") + exe;
				return -1;
			}
			// 必须先起异步读取再等退出：旧写法先 ReadToEnd 再 WaitForExit，
			// 两个管道任何一边写满就会互相卡死，8 秒超时形同虚设（而且调用方在 UI 线程上）。
			Task<string> task = process.StandardOutput.ReadToEndAsync();
			Task<string> task2 = process.StandardError.ReadToEndAsync();
			if (!process.WaitForExit(8000))
			{
				try
				{
					process.Kill(entireProcessTree: true);
				}
				catch
				{
				}
				output = Loc.T("命令超时（8 秒）：") + exe + " " + args;
				Log.Error(output);
				return -1;
			}
			Task.WaitAll(new Task[2] { task, task2 }, 2000);
			output = ((task.IsCompleted ? task.Result : string.Empty) + " " + (task2.IsCompleted ? task2.Result : string.Empty)).Trim();
			return process.ExitCode;
		}
		catch (Exception ex)
		{
			output = ex.Message;
			return -1;
		}
	}
}
