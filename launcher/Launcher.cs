using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace HoldDragLauncher
{
	/// <summary>
	/// HoldDrag 启动器。
	///
	/// 职责只有两件事，都在启动主程序之前完成：
	///   1. **检查 .NET 10 桌面运行时**：缺了就弹一个中文窗口，按钮直接打开微软官网下载页；
	///   2. 运行时齐全时，原样把参数转给同目录的 HoldDrag.App.exe 并退出。
	///
	/// 为什么必须有这一层：主程序是 framework-dependent 的 .NET 10 应用，
	/// 缺运行时时它的 apphost 只能弹出英文的 "You must install or update .NET"，
	/// 普通用户看到 "Microsoft.WindowsDesktop.App 10.0.x" 这种字眼根本不知道要装什么。
	/// 而本程序用 .NET Framework 4.8（Win10 1903+ / Win11 自带），一定跑得起来。
	/// </summary>
	internal static class Program
	{
		/// <summary>主程序文件名（与启动器同目录）。改名时这里与打包脚本必须同步。</summary>
		internal const string AppFileName = "HoldDrag.App.exe";

		/// <summary>.NET 10 桌面运行时的官方下载页。</summary>
		internal const string DownloadUrl = "https://dotnet.microsoft.com/download/dotnet/10.0";

		/// <summary>要检测的共享框架目录名（桌面运行时 = Microsoft.WindowsDesktop.App）。</summary>
		private const string DesktopFramework = "Microsoft.WindowsDesktop.App";

		/// <summary>
		/// 桌面运行时的"装完整了"标志文件。注意：**没有** Microsoft.WindowsDesktop.App.dll 这个文件，
		/// 框架本体是 deps.json + runtimeconfig.json + 63 个 dll；拿一个不存在的文件名去判断，
		/// 会让每一台装了运行时的机器都误报"缺少 .NET 10"。
		/// </summary>
		private const string DesktopMarker = "Microsoft.WindowsDesktop.App.deps.json";

		/// <summary>要检测的主版本号。</summary>
		private const string RequiredMajor = "10.";

		/// <summary>测试开关：置 1 时假装没装运行时，用来验证弹窗（自检/排查用）。</summary>
		private const string ForceMissingEnv = "HOLDDRAAG_FORCE_MISSING_DOTNET";

		/// <summary>内嵌主程序的资源名（在 csproj 的 LogicalName 里指定）。</summary>
		private const string PayloadResource = "HoldDrag.Payload.exe";

		/// <summary>传给主程序的外层 exe 路径参数：自动更新要替换的是这一个文件，不是解出来的副本。</summary>
		private const string HostArg = "--host";

		[STAThread]
		private static int Main(string[] args)
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			string selfPath;
			try
			{
				selfPath = Assembly.GetExecutingAssembly().Location;
			}
			catch (Exception)
			{
				selfPath = Process.GetCurrentProcess().MainModule.FileName;
			}
			string dir = Path.GetDirectoryName(selfPath);


			if (!HasDesktopRuntime10())
			{
				ShowRuntimeMissing();
				return 1;
			}

			// 运行时齐全：把内嵌的主程序解出来（同一份只解一次），再启动
			string payloadPath;
			try
			{
				payloadPath = EnsurePayload();
			}
			catch (Exception ex)
			{
				ShowPayloadFailed(ex);
				return 4;
			}

			return StartApp(payloadPath, dir, args, selfPath);
		}

		/// <summary>本机是否装了 .NET 10 的**桌面**运行时（WPF/WinForms 需要它，只有 NETCore.App 不够）。</summary>
		internal static bool HasDesktopRuntime10()
		{
			if (string.Equals(Environment.GetEnvironmentVariable(ForceMissingEnv), "1", StringComparison.Ordinal))
			{
				return false;
			}
			foreach (string root in CandidateDotnetRoots())
			{
				try
				{
					string shared = Path.Combine(root, "shared", DesktopFramework);
					if (!Directory.Exists(shared))
					{
						continue;
					}
					string[] dirs = Directory.GetDirectories(shared);
					for (int i = 0; i < dirs.Length; i++)
					{
						string name = Path.GetFileName(dirs[i]);
						// 排除 10.0.0-preview.* 这类预览版：正式版应用不会向上滚动到它们
						if (name.IndexOf('-') >= 0)
						{
							continue;
						}
						if (!name.StartsWith(RequiredMajor, StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
						// 目录存在不等于装好了（卸载残留会留下空目录）：必须能看见运行时本体
						if (File.Exists(Path.Combine(dirs[i], DesktopMarker)))
						{
							return true;
						}
					}
				}
				catch (Exception)
				{
					// 目录不可读就跳过，继续试下一个候选根
				}
			}
			return false;
		}

		/// <summary>
		/// 可能装 .NET 的位置（只认 x64：主程序是 x64，32 位运行时对它没用）：
		/// DOTNET_ROOT / DOTNET_ROOT_X64（用户自定义安装）→ 机器级注册表 → 用户级注册表
		/// → 默认的 Program Files → 用户级安装目录（dotnet-install.ps1 / winget 的默认位置）。
		/// </summary>
		private static System.Collections.Generic.List<string> CandidateDotnetRoots()
		{
			System.Collections.Generic.List<string> list = new System.Collections.Generic.List<string>();
			AddUnique(list, Environment.GetEnvironmentVariable("DOTNET_ROOT"));
			AddUnique(list, Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"));
			AddUnique(list, ReadInstallLocation(Registry.LocalMachine, @"SOFTWARE\dotnet\Setup\InstalledVersions\x64"));
			AddUnique(list, ReadInstallLocation(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64"));
			AddUnique(list, ReadInstallLocation(Registry.CurrentUser, @"SOFTWARE\dotnet\Setup\InstalledVersions\x64"));
			try
			{
				AddUnique(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
			}
			catch (Exception)
			{
			}
			try
			{
				AddUnique(list, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet"));
			}
			catch (Exception)
			{
			}
			return list;
		}

		private static void AddUnique(System.Collections.Generic.List<string> list, string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return;
			}
			for (int i = 0; i < list.Count; i++)
			{
				if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
			}
			list.Add(value);
		}

		private static string ReadInstallLocation(RegistryKey root, string keyPath)
		{
			try
			{
				using (RegistryKey key = root.OpenSubKey(keyPath))
				{
					if (key == null)
					{
						return null;
					}
					object value = key.GetValue("InstallLocation");
					string text = value as string;
					if (string.IsNullOrEmpty(text))
					{
						return null;
					}
					// 统一去掉结尾的斜杠，避免出现 "C:\Program Files\dotnet\\shared"
					return text.TrimEnd('\\', '/');
				}
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>
		/// 启动主程序：参数原样转发（含空格的按引号包好），并补一个 <c>--host &lt;本启动器路径&gt;</c>。
		/// 主程序靠这个参数知道"用户手里那个 exe"是哪个 —— 自动更新要替换的是它，不是 %LOCALAPPDATA% 里的副本；
		/// 开机自启也要注册它，否则下次开机绕过启动器，缺运行时时又没有提示了。
		/// </summary>
		private static int StartApp(string appPath, string dir, string[] args, string selfPath)
		{
			try
			{
				ProcessStartInfo info = new ProcessStartInfo(appPath);
				// 必须关掉 ShellExecute：
				//  1) 目标可能是 .tmp（同版本缓存被占用时的兜底文件名）——外壳按扩展名找不到关联，
				//     Process.Start 会**返回 null 且不抛异常**，表现为"双击没反应、连报错都没有"；
				//  2) 只有直接 CreateProcess 才能拿到子进程句柄，进而把退出码带回去。
				info.UseShellExecute = false;
				info.WorkingDirectory = dir;
				string forwarded = HostArg + " " + QuoteArg(selfPath);
				if (args != null && args.Length > 0)
				{
					forwarded = forwarded + " " + JoinArgs(args);
				}
				info.Arguments = forwarded;
				Process process = Process.Start(info);
				if (process == null)
				{
					MessageBox.Show(
						IsChinese()
							? "无法启动主程序（系统没有返回进程）。\r\n\r\n文件位置：" + appPath
							: "Could not start the application (no process was created).\r\n\r\nPath: " + appPath,
						"HoldDrag", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return 3;
				}
				// 诊断类参数（--selftest / --stress / --checkupdate …）本来就是"跑完看退出码"，
				// 启动器要等它结束并把退出码原样返回，否则 README 里"退出码 0 = 全通过"永远成立不了。
				if (IsDiagnosticRun(args))
				{
					process.WaitForExit();
					return process.ExitCode;
				}
				return 0;
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					IsChinese()
						? "无法启动主程序：\r\n\r\n" + ex.Message + "\r\n\r\n文件位置：" + appPath
						: "Could not start the application:\r\n\r\n" + ex.Message + "\r\n\r\nPath: " + appPath,
					"HoldDrag", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return 3;
			}
		}

		/// <summary>这些参数是一次性诊断/冒烟运行：调用方（脚本、CI、排障的人）需要拿到真实退出码。</summary>
		internal static bool IsDiagnosticRun(string[] args)
		{
			if (args == null)
			{
				return false;
			}
			string[] names = { "--selftest", "--stress", "--dragbench", "--probe", "--smoke", "--checkupdate", "--visualcheck", "--previewtest", "--langshot" };
			for (int i = 0; i < args.Length; i++)
			{
				for (int j = 0; j < names.Length; j++)
				{
					if (string.Equals(args[i], names[j], StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// 按 Windows 命令行解析规则（MSVCRT / CommandLineToArgvW）转义单个参数：
		/// 反斜杠只在引号前需要成倍转义、末尾反斜杠也要成倍——否则 "C:\My Docs\" 会把结尾的引号吃掉，
		/// 后面所有参数连成一串。
		/// </summary>
		internal static string QuoteArg(string value)
		{
			if (value == null)
			{
				return "\"\"";
			}
			if (value.Length == 0)
			{
				return "\"\"";
			}
			bool needQuotes = value.IndexOf(' ') >= 0 || value.IndexOf('\t') >= 0 || value.IndexOf('"') >= 0;
			if (!needQuotes)
			{
				return value;
			}
			System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length + 8);
			builder.Append('"');
			for (int i = 0; i < value.Length; i++)
			{
				int backslashes = 0;
				while (i < value.Length && value[i] == '\\')
				{
					backslashes++;
					i++;
				}
				if (i == value.Length)
				{
					// 结尾的反斜杠：必须成倍，否则会转义掉收尾的引号
					builder.Append('\\', backslashes * 2);
					break;
				}
				if (value[i] == '"')
				{
					builder.Append('\\', backslashes * 2 + 1);
					builder.Append('"');
				}
				else
				{
					builder.Append('\\', backslashes);
					builder.Append(value[i]);
				}
			}
			builder.Append('"');
			return builder.ToString();
		}

		/// <summary>给每个参数按需加引号：路径带空格时不加会被拆成多个参数。</summary>
		internal static string JoinArgs(string[] args)
		{
			System.Text.StringBuilder builder = new System.Text.StringBuilder();
			for (int i = 0; i < args.Length; i++)
			{
				if (i > 0)
				{
					builder.Append(' ');
				}
				builder.Append(QuoteArg(args[i]));
			}
			return builder.ToString();
		}

		private static bool IsChinese()
		{
			try
			{
				return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName
					.Equals("zh", StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception)
			{
				return false;
			}
		}

		/// <summary>缺运行时：弹「去官网下载」窗口。关闭窗口就直接退出，绝不尝试启动主程序。</summary>
		private static void ShowRuntimeMissing()
		{
			bool zh = IsChinese();
			using (Form form = new Form())
			{
				form.Text = "HoldDrag";
				form.FormBorderStyle = FormBorderStyle.FixedDialog;
				form.StartPosition = FormStartPosition.CenterScreen;
				form.MinimizeBox = false;
				form.MaximizeBox = false;
				form.ShowInTaskbar = true;
				form.AutoScaleMode = AutoScaleMode.Dpi;
				form.AutoSize = true;
				form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
				form.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
				form.BackColor = SystemColors.Window;

				TableLayoutPanel root = new TableLayoutPanel();
				root.AutoSize = true;
				root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
				root.ColumnCount = 1;
				root.Dock = DockStyle.Fill;
				root.Padding = new Padding(18, 16, 18, 14);

				Label title = new Label();
				title.AutoSize = true;
				title.MaximumSize = new Size(430, 0);
				title.Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold, GraphicsUnit.Point);
				title.Margin = new Padding(0, 0, 0, 10);
				title.Text = zh ? "需要先安装 .NET 10 桌面运行时" : "The .NET 10 Desktop Runtime is required";

				Label body = new Label();
				body.AutoSize = true;
				body.MaximumSize = new Size(430, 0);
				body.Margin = new Padding(0, 0, 0, 8);
				body.Text = zh
					? ("长按拖窗（HoldDrag）是用 .NET 10 写的，这台电脑上还没有安装它需要的「.NET 10 桌面运行时」，"
						+ "所以现在打不开。\r\n\r\n点下面的「去官网下载」，在微软官网下载并安装 "
						+ ".NET Desktop Runtime 10.0（约 60 MB），装完再双击本程序就能用了。\r\n\r\n"
						+ "注意选 x64 版本；只装 ASP.NET Core 或只装 .NET Runtime 都不够，要装「Desktop Runtime」。")
					: ("HoldDrag is built with .NET 10 and the .NET 10 Desktop Runtime is not installed on this PC, "
						+ "so it cannot start yet.\r\n\r\nClick \"Download from Microsoft\" below, install the "
						+ ".NET Desktop Runtime 10.0 (x64, about 60 MB), then launch this app again.\r\n\r\n"
						+ "Note: the plain \".NET Runtime\" or \"ASP.NET Core Runtime\" is not enough — you need the "
						+ "\"Desktop Runtime\".");

				LinkLabel link = new LinkLabel();
				link.AutoSize = true;
				link.MaximumSize = new Size(430, 0);
				link.Margin = new Padding(0, 0, 0, 12);
				link.Text = DownloadUrl;

				FlowLayoutPanel buttons = new FlowLayoutPanel();
				buttons.AutoSize = true;
				buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
				buttons.FlowDirection = FlowDirection.RightToLeft;
				buttons.WrapContents = false;
				buttons.Dock = DockStyle.Fill;
				buttons.Margin = new Padding(0, 4, 0, 0);

				Button close = new Button();
				close.Text = zh ? "关闭" : "Close";
				close.AutoSize = true;
				close.MinimumSize = new Size(96, 30);
				close.Margin = new Padding(8, 0, 0, 0);
				close.Padding = new Padding(10, 0, 10, 0);

				Button copy = new Button();
				copy.Text = zh ? "复制链接" : "Copy link";
				copy.AutoSize = true;
				copy.MinimumSize = new Size(96, 30);
				copy.Margin = new Padding(8, 0, 0, 0);
				copy.Padding = new Padding(10, 0, 10, 0);
				copy.Click += delegate
				{
					try
					{
						Clipboard.SetText(DownloadUrl);
						copy.Text = zh ? "已复制" : "Copied";
					}
					catch (Exception)
					{
					}
				};

				Button open = new Button();
				open.Text = zh ? "去官网下载" : "Download from Microsoft";
				open.AutoSize = true;
				open.MinimumSize = new Size(120, 30);
				open.Margin = new Padding(8, 0, 0, 0);
				open.Padding = new Padding(10, 0, 10, 0);
				open.Click += delegate
				{
					if (OpenDownloadPage())
					{
						form.Close();
					}
				};

				link.LinkClicked += delegate
				{
					OpenDownloadPage();
				};
				close.Click += delegate
				{
					form.Close();
				};

				buttons.Controls.Add(close);
				buttons.Controls.Add(copy);
				buttons.Controls.Add(open);

				root.Controls.Add(title);
				root.Controls.Add(body);
				root.Controls.Add(link);
				root.Controls.Add(buttons);
				form.Controls.Add(root);
				form.AcceptButton = open;
				form.CancelButton = close;
				form.ShowDialog();
			}
		}

		private static bool OpenDownloadPage()
		{
			try
			{
				Process.Start(new ProcessStartInfo(DownloadUrl)
				{
					UseShellExecute = true
				});
				return true;
			}
			catch (Exception ex)
			{
				MessageBox.Show(
					IsChinese()
						? "打不开浏览器。请手动打开下面的地址下载（已可复制）：\r\n\r\n" + DownloadUrl + "\r\n\r\n" + ex.Message
						: "Could not open your browser. Please open this URL manually:\r\n\r\n" + DownloadUrl + "\r\n\r\n" + ex.Message,
					"HoldDrag", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return false;
			}
		}

		/// <summary>
		/// 把内嵌的主程序解到 %LOCALAPPDATA%\HoldDrag\app\&lt;内容哈希&gt;\ 并返回它的路径。
		///
		/// 用**内容哈希**做目录名：换一个版本就是换一个目录，天然不会把新旧文件混在一起；
		/// 同一份 payload 只会解一次（第二次启动直接命中缓存）。
		/// 解包而不是"内存里加载"，是因为单文件 .NET 应用必须落盘才能跑。
		/// </summary>
		private static string EnsurePayload()
		{
			using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
			{
				if (stream == null)
				{
					throw new InvalidOperationException(
						"启动器里没有内嵌主程序（打包时没把 HoldDrag.App.exe 嵌进去）。");
				}
				string hash = ShortHash(stream);
				string root = Path.Combine(
					Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoldDrag", "app");
				string dir = Path.Combine(root, hash);
				string target = Path.Combine(dir, AppFileName);
				long length = stream.Length;

				if (File.Exists(target) && new FileInfo(target).Length == length)
				{
					return target;
				}

				Directory.CreateDirectory(dir);
				// 临时名带进程号：两个实例同时首次启动时不会互相抢同一个 .tmp（那样必有一个报"写不进去"），
				// 也不会出现"我刚写好的文件被另一个实例删掉"的情况。
				string temp = target + "." + Process.GetCurrentProcess().Id + ".tmp";
				try
				{
					stream.Position = 0;
					using (FileStream fileStream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
					{
						stream.CopyTo(fileStream);
						fileStream.Flush(true);
					}
					for (int attempt = 0; attempt < 3; attempt++)
					{
						try
						{
							if (File.Exists(target))
							{
								// 已存在就用替换（比"先删再移动"安全：删掉的那一刻另一个实例可能正要启动它）
								File.Replace(temp, target, null);
							}
							else
							{
								File.Move(temp, target);
							}
							CleanupOldPayloads(root, hash);
							return target;
						}
						catch (IOException)
						{
							// 目标被占用（例如上一版还在跑）：稍等重试
							System.Threading.Thread.Sleep(120 * (attempt + 1));
						}
					}
				}
				finally
				{
					try
					{
						if (File.Exists(temp))
						{
							File.Delete(temp);
						}
					}
					catch (Exception)
					{
					}
				}
				// 重试用尽：只要目标已经在了就直接用它（可能是并发实例刚写好的），别把用户卡死在"解包失败"上
				if (File.Exists(target))
				{
					return target;
				}
				throw new IOException("无法写入解包目录：" + target);
			}
		}

		/// <summary>SHA-256 前 6 字节（12 位十六进制）——只用来当目录名，不用于安全校验。</summary>
		private static string ShortHash(Stream stream)
		{
			using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(stream);
				System.Text.StringBuilder builder = new System.Text.StringBuilder(12);
				for (int i = 0; i < 6; i++)
				{
					builder.Append(hash[i].ToString("x2"));
				}
				return builder.ToString();
			}
		}

		/// <summary>清掉旧版本解包目录（留着只会越积越多）；删不掉就跳过，绝不因此让启动失败。</summary>
		private static void CleanupOldPayloads(string root, string keep)
		{
			try
			{
				string[] dirs = Directory.GetDirectories(root);
				for (int i = 0; i < dirs.Length; i++)
				{
					if (string.Equals(Path.GetFileName(dirs[i]), keep, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					try
					{
						Directory.Delete(dirs[i], true);
					}
					catch (Exception)
					{
						// 旧版本进程还在跑就会删不掉，下次启动再试
					}
				}
			}
			catch (Exception)
			{
			}
		}

		/// <summary>解包失败（磁盘满、权限、杀软拦截…）：说清楚，而不是闪退。</summary>
		private static void ShowPayloadFailed(Exception ex)
		{
			bool zh = IsChinese();
			MessageBox.Show(
				zh
					? ("无法展开程序文件，启动失败。\r\n\r\n" + ex.Message
						+ "\r\n\r\n常见原因：%LOCALAPPDATA% 所在的磁盘空间不足、目录没有写权限，"
						+ "或被安全软件拦截。清理后重新双击本程序即可。")
					: ("Could not unpack the application files.\r\n\r\n" + ex.Message
						+ "\r\n\r\nCommon causes: not enough free space on the %LOCALAPPDATA% drive, "
						+ "no write permission, or security software blocking it. Free some space and try again."),
				"HoldDrag", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}
}
