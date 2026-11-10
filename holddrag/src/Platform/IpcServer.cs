// HoldDrag · Platform/IpcServer.cs
//
// 给脚本用的命名管道接口。
//
// 协议极简：一行一条命令，服务端回一行结果（OK ... / ERR ...）。
//   > status
//   < OK v1.13.0 paused=False
//
// 客户端：HoldDrag.exe --cmd "layout-restore 办公"
// 这样 AutoHotkey / PowerShell / Python 都能直接调，不用再写一套 UI 自动化。

using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace HoldDrag;

internal sealed class IpcServer : IDisposable
{
	internal const string PipeName = "HoldDrag.v1";

	private readonly Func<string, string> _handler;

	/// <summary>管道名（自检用独立名字，避免和正在运行的实例抢同一个管道）。</summary>
	private readonly string _name;

	private Thread _thread;

	private volatile bool _quit;

	private NamedPipeServerStream _server;

	public IpcServer(Func<string, string> handler, string name = PipeName)
	{
		_handler = handler;
		_name = string.IsNullOrEmpty(name) ? PipeName : name;
	}

	public void Start()
	{
		if (_thread != null)
		{
			return;
		}
		_thread = new Thread(Loop)
		{
			IsBackground = true,
			Name = "HoldDrag.Ipc"
		};
		_thread.Start();
	}

	private void Loop()
	{
		while (!_quit)
		{
			try
			{
				using NamedPipeServerStream namedPipeServerStream = CreateServerPipe(_name);
				_server = namedPipeServerStream;
				namedPipeServerStream.WaitForConnection();
				if (_quit)
				{
					break;
				}
				using StreamReader streamReader = new StreamReader(namedPipeServerStream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
				using StreamWriter streamWriter = new StreamWriter(namedPipeServerStream, new UTF8Encoding(false), 1024, leaveOpen: true)
				{
					AutoFlush = true
				};
				string text = streamReader.ReadLine();
				if (text != null)
				{
					string value;
					try
					{
						value = (_handler != null) ? _handler(text.Trim()) : "ERR no-handler";
					}
					catch (Exception ex)
					{
						Log.Error(ex);
						value = "ERR " + ex.Message.Replace('\r', ' ').Replace('\n', ' ');
					}
					streamWriter.WriteLine(value ?? "OK");
				}
			}
			catch (Exception ex2)
			{
				if (!_quit)
				{
					Log.Error(ex2);
					Thread.Sleep(200);
				}
			}
			finally
			{
				_server = null;
			}
		}
	}

	/// <summary>
	/// 建服务端管道。
	///
	/// 这里必须自己用 CreateNamedPipeW，不能直接用 NamedPipeServerStream 的默认实现：
	/// 「以管理员运行」（默认配置）时，管道对象跟着进程是**高完整性**，同时默认 DACL 只给
	/// 创建者和管理员 —— 普通权限的脚本连 pipe 都打不开（实测 Win32 错误 5 Access denied），
	/// 整套脚本接口等于失效。
	///
	/// 所以显式给一个宽松 DACL（本机已认证用户 + SYSTEM）并把强制完整性标签降到 Medium。
	/// </summary>
	private static NamedPipeServerStream CreateServerPipe(string name)
	{
		IntPtr pSecurityDescriptor = IntPtr.Zero;
		try
		{
			if (ConvertStringSecurityDescriptorToSecurityDescriptorW("D:(A;;GA;;;AU)(A;;GA;;;SY)S:(ML;;NW;;;ME)", 1u, out pSecurityDescriptor, out var _))
			{
				Native.SECURITY_ATTRIBUTES lpSecurityAttributes = default(Native.SECURITY_ATTRIBUTES);
				lpSecurityAttributes.nLength = Marshal.SizeOf(typeof(Native.SECURITY_ATTRIBUTES));
				lpSecurityAttributes.lpSecurityDescriptor = pSecurityDescriptor;
				lpSecurityAttributes.bInheritHandle = false;
				// PIPE_ACCESS_DUPLEX | PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT
				nint num = CreateNamedPipeW("\\\\.\\pipe\\" + name, 3u, 0u, 4u, 4096u, 4096u, 0u, ref lpSecurityAttributes);
				if (num != IntPtr.Zero && num != new IntPtr(-1))
				{
					SafePipeHandle handle = new SafePipeHandle(num, ownsHandle: true);
					return new NamedPipeServerStream(PipeDirection.InOut, isAsync: false, isConnected: false, handle);
				}
				Log.Error("CreateNamedPipeW 失败，Win32 错误码 " + Marshal.GetLastWin32Error());
			}
			else
			{
				Log.Error("解析管道安全描述符失败，Win32 错误码 " + Marshal.GetLastWin32Error());
			}
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		finally
		{
			if (pSecurityDescriptor != IntPtr.Zero)
			{
				LocalFree(pSecurityDescriptor);
			}
		}
		// 兜底：默认管道（管理员权限下普通脚本可能连不上，但功能不至于完全没有）
		return new NamedPipeServerStream(name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096);
	}

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string stringSecurityDescriptor, uint stringSdRevision, out IntPtr securityDescriptor, out uint securityDescriptorSize);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateNamedPipeW(string lpName, uint dwOpenMode, uint dwPipeMode, uint nMaxInstances, uint nOutBufferSize, uint nInBufferSize, uint nDefaultTimeOut, ref Native.SECURITY_ATTRIBUTES lpSecurityAttributes);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint LocalFree(nint hMem);

	/// <summary>客户端：把一条命令发给正在运行的实例，返回它的一行回复。</summary>
	internal static string Send(string command, int timeoutMs = 4000, string name = PipeName)
	{
		try
		{
			using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", string.IsNullOrEmpty(name) ? PipeName : name, PipeDirection.InOut, PipeOptions.None);
			namedPipeClientStream.Connect(timeoutMs);
			using StreamWriter streamWriter = new StreamWriter(namedPipeClientStream, new UTF8Encoding(false), 1024, leaveOpen: true)
			{
				AutoFlush = true
			};
			using StreamReader streamReader = new StreamReader(namedPipeClientStream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
			streamWriter.WriteLine(command);
			string text = streamReader.ReadLine();
			return text ?? "ERR empty-response";
		}
		catch (Exception ex)
		{
			return "ERR " + ex.Message;
		}
	}

	public void Dispose()
	{
		_quit = true;
		try
		{
			_server?.Dispose();
		}
		catch
		{
		}
		try
		{
			_thread?.Join(600);
		}
		catch
		{
		}
	}
}
