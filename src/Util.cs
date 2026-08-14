using System;
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
    }
}
