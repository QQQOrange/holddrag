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
using System.Text;
using System.Threading;

namespace HoldDrag;

internal sealed class IpcServer : IDisposable
{
	internal const string PipeName = "HoldDrag.v1";

	private readonly Func<string, string> _handler;

	private Thread _thread;

	private volatile bool _quit;

	private NamedPipeServerStream _server;

	public IpcServer(Func<string, string> handler)
	{
		_handler = handler;
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
				using NamedPipeServerStream namedPipeServerStream = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096);
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

	/// <summary>客户端：把一条命令发给正在运行的实例，返回它的一行回复。</summary>
	internal static string Send(string command, int timeoutMs = 4000)
	{
		try
		{
			using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
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
