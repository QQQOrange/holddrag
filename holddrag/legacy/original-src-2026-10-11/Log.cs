using System;
using System.IO;
using System.Text;

namespace HoldDrag
{
    /// <summary>
    /// 极简日志。程序没有控制台，出问题时只能靠文件排查，
    /// 因此所有异常都必须落盘，绝不能静默吞掉。
    /// </summary>
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _path = string.Empty;
        private static StreamWriter _writer;

        public static string Path { get { return _path; } }

        /// <summary>
        /// 诊断用的详细日志开关。默认关闭 —— 拖动过程中每帧都要记录，
        /// 开着会写盘写到手软，所以只在排查问题时通过 --trace 打开。
        /// </summary>
        public static bool Verbose { get; set; }

        public static void Init(string dir)
        {
            try
            {
                _path = System.IO.Path.Combine(dir, "holddrag.log");

                // ══ ★ 主日志打不开时必须退到备用文件名，绝不能静默 ══
                //
                // 踩过的坑：本程序常驻托盘，用户机器上很可能【已经有一个实例在跑】，
                // 它用 FileShare.Read 占着日志文件；此时第二个进程（自检、或用户
                // 手动又点了一次 exe）以写入方式打开会直接失败 ——
                // 而原来的代码把失败咽掉了，于是整个进程【一条日志都不写】，
                // 排查时完全看不到任何线索（本次就因此白跑了好几轮）。
                //
                // 退到一个带进程号的备用文件，既能保住自己的日志，
                // 又不会去动正在运行那个实例的文件。
                if (!CanOpenForAppend(_path))
                {
                    string alt = System.IO.Path.Combine(
                        dir, "holddrag." + System.Diagnostics.Process.GetCurrentProcess().Id + ".log");
                    _path = alt;
                }

                Trim();
            }
            catch
            {
                _path = string.Empty;
            }
        }

        /// <summary>能不能以"追加写"的方式打开（别的实例占着时会返回 false）。</summary>
        private static bool CanOpenForAppend(string path)
        {
            try
            {
                if (!File.Exists(path)) return true;
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void Info(string msg)
        {
            Write("INFO", msg);
        }

        public static void Trace(string msg)
        {
            if (!Verbose) return;
            Write("TRC ", msg);
        }

        public static void Error(string msg)
        {
            Write("ERR ", msg);
        }

        public static void Error(Exception ex)
        {
            Write("ERR ", ex != null ? ex.ToString() : "null exception");
        }

        private static void Write(string level, string msg)
        {
            if (string.IsNullOrEmpty(_path)) return;

            // ★ 用常驻的 StreamWriter 而不是 File.AppendAllText。
            //
            //   File.AppendAllText 每次都走一遍"打开 → 写 → 关闭"，
            //   等于每条日志都要打开一次文件句柄。拖动这种高频动作
            //   每次会话都记一条，几百次拖动就是几百次开关文件 ——
            //   句柄数会短暂飙升，压测里能直接看出来。
            //   这里改成开着不关（进程退出/Trim 时 Flush），只在写失败时才退回一次性写。
            lock (Gate)
            {
                try
                {
                    EnsureWriter();

                    if (_writer == null)
                    {
                        // 兜底：常驻写失败了（比如目录被删），退回一次性写
                        File.AppendAllText(_path, Stamp(level, msg), Encoding.UTF8);
                        return;
                    }

                    _writer.Write(Stamp(level, msg));
                    _writer.Flush();     // 出问题要能立刻看到，不做长缓冲
                }
                catch
                {
                    // 日志本身失败绝不能再抛异常
                    CloseWriter();
                }
            }
        }

        private static string Stamp(string level, string msg)
        {
            var sb = new StringBuilder(64);
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            sb.Append(' ');
            sb.Append(level);
            sb.Append(' ');
            sb.Append(msg);
            sb.Append("\r\n");
            return sb.ToString();
        }

        private static void EnsureWriter()
        {
            if (_writer != null) return;
            _writer = new StreamWriter(
                new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read),
                Encoding.UTF8);
            _writer.AutoFlush = false;
        }

        private static void CloseWriter()
        {
            try { if (_writer != null) _writer.Dispose(); } catch { }
            _writer = null;
        }

        /// <summary>超过 512KB 就截断，避免长期运行把磁盘写满。</summary>
        private static void Trim()
        {
            try
            {
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > 512 * 1024)
                {
                    // 删之前先把常驻写关掉，否则 Windows 会因为文件被占用而删不掉。
                    CloseWriter();
                    File.Delete(_path);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// 关掉常驻的日志文件句柄。
        ///
        /// 正常退出可以不调（操作系统会回收），但压测/自检里调一下更干净，
        /// 免得句柄数被算进基线里。
        /// </summary>
        public static void Shutdown()
        {
            lock (Gate) { CloseWriter(); }
        }
    }
}
