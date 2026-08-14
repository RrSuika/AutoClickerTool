using System;
using System.IO;
using System.Text;

namespace AutoClickerTool
{
    /// <summary>
    /// 极简文件日志: 写到 exe 同目录 log.txt, 线程安全, 按时间戳分级。
    /// 超过 2MB 自动清空重来, 避免无限增长。写失败静默忽略(不影响主流程)。
    /// </summary>
    internal static class Log
    {
        private static readonly object Lock = new object();
        private const long MaxBytes = 2 * 1024 * 1024;

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt"); }
        }

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            try
            {
                lock (Lock)
                {
                    try
                    {
                        if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                            File.Delete(FilePath);
                    }
                    catch (Exception)
                    {
                    }
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + msg + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch (Exception)
            {
                // 日志失败不影响主流程
            }
        }
    }
}
