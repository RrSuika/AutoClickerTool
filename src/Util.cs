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

        /// <summary>「运行到时刻」接受的时间格式(仅时刻, 或完整的日期+时刻)。</summary>
        private static readonly string[] UntilFormats =
        {
            "HH:mm", "HH:mm:ss",
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss",
            "yyyy/M/d HH:mm", "yyyy/M/d HH:mm:ss",
            "yyyy-M-d H:mm", "yyyy-M-d HH:mm:ss",
            "M/d/yyyy H:mm", "M/d/yyyy HH:mm:ss",
            "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"
        };

        /// <summary>
        /// 解析「运行到时刻」。支持两种写法:
        ///   1. 仅时刻 "HH:mm" / "HH:mm:ss" —— 目标时刻已过(如 22:00 设 03:00)视为次日, 支持过夜挂机;
        ///   2. 完整日期时刻 "yyyy-MM-dd HH:mm:ss" —— 按字面绝对时刻(过去就是过去, 用于"运行时长"同步出来的截止点)。
        /// 空串/解析失败返回 null(解析失败会打日志)。
        /// </summary>
        public static DateTime? ParseUntilTime(string text)
        {
            if (text == null) return null;
            text = text.Trim();
            if (text.Length == 0) return null;
            DateTime until;
            if (!DateTime.TryParseExact(text, UntilFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out until))
            {
                Log.Warn("运行到时刻解析失败, 已忽略: " + text);
                return null;
            }
            if (until.Year == 1) // 只给了时刻, 没给日期 → 今天/明天
            {
                DateTime t = DateTime.Today.Add(until.TimeOfDay);
                if (t <= DateTime.Now) t = t.AddDays(1);
                return t;
            }
            return until;
        }

        /// <summary>把时长(秒)格式化成 "H小时M分S秒"(用于界面提示), 负数/0 返回 "0秒"。</summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds < 0) seconds = 0;
            int h = seconds / 3600;
            int m = seconds % 3600 / 60;
            int s = seconds % 60;
            if (h > 0) return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m {2}s", h, m, s);
            if (m > 0) return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s", m, s);
            return string.Format(CultureInfo.InvariantCulture, "{0}s", s);
        }
    }
}
