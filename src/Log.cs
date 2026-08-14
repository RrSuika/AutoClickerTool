using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>
    /// 极简文件日志: 写到 exe 同目录 log.txt, 线程安全, 按时间戳分级。
    /// 异步缓冲写入(不阻塞钩子回调); 超过 2MB 轮转 rename 成 log.txt.old 再重开。
    /// 写失败静默忽略(不影响主流程)。
    /// </summary>
    internal static class Log
    {
        private static readonly object Lock = new object();
        private static readonly StringBuilder Buf = new StringBuilder();
        private const long MaxBytes = 2 * 1024 * 1024;
        private static bool _flushing;

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt"); }
        }

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            string line;
            try
            {
                // 消息可能来自外部数据(程序路径等), 转义换行防日志行注入
                msg = msg.Replace("\r", "\\r").Replace("\n", "\\n");
                line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + msg + Environment.NewLine;
            }
            catch (Exception)
            {
                return;
            }
            lock (Lock)
            {
                Buf.Append(line);
                if (_flushing) return;
                _flushing = true;
            }
            ThreadPool.QueueUserWorkItem(delegate { FlushNow(); });
        }

        /// <summary>把缓冲的日志行落盘; 退出前调用, 避免丢失最后的日志。</summary>
        public static void Flush()
        {
            try
            {
                FlushNow();
            }
            catch (Exception)
            {
            }
        }

        private static void FlushNow()
        {
            try
            {
                string batch;
                lock (Lock)
                {
                    batch = Buf.ToString();
                    Buf.Length = 0;
                }
                if (batch.Length == 0) return;
                RotateIfNeeded();
                File.AppendAllText(FilePath, batch, Encoding.UTF8);
            }
            catch (Exception)
            {
            }
            finally
            {
                _flushing = false;
            }
        }

        /// <summary>超 2MB 时 rename 成 log.txt.old(原子, 不截断其它实例正在追加的内容), 失败则跳过本轮。</summary>
        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                if (new FileInfo(FilePath).Length <= MaxBytes) return;
                try
                {
                    if (File.Exists(FilePath + ".old")) File.Delete(FilePath + ".old");
                    File.Move(FilePath, FilePath + ".old");
                }
                catch (Exception)
                {
                    // 文件被其它实例占用等原因, 本轮不轮转, 下轮再试
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
