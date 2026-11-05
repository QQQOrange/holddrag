// HoldDrag · Diagnostics/DiagnosticsBundle.cs
//
// 一键诊断包：把「排查问题真正需要的东西」打成一个 zip。
//
// 里面只放这些（不放用户文档、不放截图）：
//   config.json + 备份      —— 复现配置
//   logs/ 最近 3 个日志      —— 现场
//   system.txt              —— 版本 / 系统 / DPI / 显示器 / 权限 / 钩子状态

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace HoldDrag;

internal static class DiagnosticsBundle
{
	/// <summary>配置与日志所在目录（和 Log 同一个）。</summary>
	public static string DataDir
	{
		get
		{
			try
			{
				string directoryName = Path.GetDirectoryName(Log.Path);
				if (!string.IsNullOrEmpty(directoryName))
				{
					return directoryName;
				}
			}
			catch
			{
			}
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HoldDrag");
		}
	}

	/// <summary>生成诊断包；返回实际写入的路径（失败抛异常，由调用方提示）。</summary>
	public static string Create(string targetZip, string extra = null)
	{
		string text = targetZip;
		if (string.IsNullOrEmpty(text))
		{
			text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "HoldDrag-诊断-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
		}
		string directoryName = Path.GetDirectoryName(text);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		if (File.Exists(text))
		{
			File.Delete(text);
		}
		using ZipArchive zipArchive = ZipFile.Open(text, ZipArchiveMode.Create);
		string dataDir = DataDir;
		AddFile(zipArchive, Path.Combine(dataDir, "config.json"), "config.json");
		AddFile(zipArchive, Path.Combine(dataDir, "config.json.bak"), "config.json.bak");
		AddFile(zipArchive, Path.Combine(dataDir, "config.json.bak2"), "config.json.bak2");
		try
		{
			foreach (string item in (from f in Directory.GetFiles(dataDir, "holddrag*.log")
				orderby File.GetLastWriteTimeUtc(f) descending
				select f).Take(3))
			{
				AddFile(zipArchive, item, "logs/" + Path.GetFileName(item), 524288);
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		AddText(zipArchive, "system.txt", BuildSystemInfo(extra));
		return text;
	}

	private static void AddFile(ZipArchive zip, string path, string entryName, int maxBytes = 0)
	{
		try
		{
			if (!File.Exists(path))
			{
				return;
			}
			byte[] array = File.ReadAllBytes(path);
			if (maxBytes > 0 && array.Length > maxBytes)
			{
				byte[] array2 = new byte[maxBytes];
				Array.Copy(array, array.Length - maxBytes, array2, 0, maxBytes);
				array = Encoding.UTF8.GetBytes("[…只保留最后 " + maxBytes + " 字节…]\r\n").Concat(array2).ToArray();
			}
			ZipArchiveEntry zipArchiveEntry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
			using Stream stream = zipArchiveEntry.Open();
			stream.Write(array, 0, array.Length);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static void AddText(ZipArchive zip, string entryName, string text)
	{
		try
		{
			ZipArchiveEntry zipArchiveEntry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
			using StreamWriter streamWriter = new StreamWriter(zipArchiveEntry.Open(), new UTF8Encoding(false));
			streamWriter.Write(text);
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
	}

	private static string BuildSystemInfo(string extra)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("HoldDrag 诊断信息");
		stringBuilder.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		stringBuilder.AppendLine("版本：" + AppInfo.Version + " (" + AppInfo.FallbackVersion + ")");
		stringBuilder.AppendLine("exe：" + AppPaths.EntryPath);
		stringBuilder.AppendLine("数据目录：" + DataDir);
		stringBuilder.AppendLine();
		try
		{
			stringBuilder.AppendLine("OS：" + Environment.OSVersion.VersionString);
			stringBuilder.AppendLine("64 位进程：" + Environment.Is64BitProcess);
			stringBuilder.AppendLine("CPU 核数：" + Environment.ProcessorCount);
			stringBuilder.AppendLine(".NET：" + Environment.Version);
			stringBuilder.AppendLine("管理员：" + Program.IsAdministrator());
		}
		catch (Exception ex)
		{
			stringBuilder.AppendLine("系统信息读取失败：" + ex.Message);
		}
		stringBuilder.AppendLine();
		try
		{
			stringBuilder.AppendLine("显示器：");
			foreach (Screen screen in Screen.AllScreens)
			{
				stringBuilder.AppendLine(string.Format("  {0} 主屏={1} 边界={2} 工作区={3}", screen.DeviceName, screen.Primary, screen.Bounds, screen.WorkingArea));
			}
		}
		catch (Exception ex2)
		{
			stringBuilder.AppendLine("显示器信息读取失败：" + ex2.Message);
		}
		stringBuilder.AppendLine();
		try
		{
			stringBuilder.AppendLine("日志文件：");
			foreach (string item in Directory.GetFiles(DataDir, "holddrag*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(6))
			{
				stringBuilder.AppendLine("  " + Path.GetFileName(item) + "  " + new FileInfo(item).Length + " 字节  " + File.GetLastWriteTime(item).ToString("yyyy-MM-dd HH:mm:ss"));
			}
		}
		catch (Exception ex3)
		{
			stringBuilder.AppendLine("日志列表读取失败：" + ex3.Message);
		}
		if (!string.IsNullOrEmpty(extra))
		{
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("附加信息：");
			stringBuilder.AppendLine(extra);
		}
		return stringBuilder.ToString();
	}
}
