using System;
using System.Globalization;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>跨模块共享的小工具。</summary>
    internal static class Util
    {
        /// <summary>可中断的分段休眠(10ms 粒度), alive 返回 false 时提前返回 false。</summary>
        public static bool SleepInterruptible(int ms, Func<bool> alive)
        {
            if (ms <= 0) return alive != null ? alive() : true;
            for (int i = 0; i < ms / 10; i++)
            {
                if (alive != null && !alive()) return false;
                Thread.Sleep(10);
            }
            if (alive != null && !alive()) return false;
            Thread.Sleep(ms % 10);
            return alive != null ? alive() : true;
        }

        /// <summary>解析"运行到时刻"(HH:mm)。目标时刻已过(如 22:00 设 03:00)视为次日, 支持过夜挂机。
        /// 空串/解析失败返回 null(解析失败会打日志)。</summary>
        public static DateTime? ParseUntilTime(string hhmm)
        {
            if (string.IsNullOrEmpty(hhmm)) return null;
            DateTime until;
            if (!DateTime.TryParseExact(hhmm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out until))
            {
                Log.Warn("运行到时刻解析失败, 已忽略: " + hhmm);
                return null;
            }
            until = DateTime.Today.Add(until.TimeOfDay);
            if (until <= DateTime.Now) until = until.AddDays(1);
            return until;
        }
    }
}
