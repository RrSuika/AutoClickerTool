using System;
using System.Threading;

namespace AutoClickerTool
{
    internal enum KeySpamMode
    {
        Tap,  // 每隔一段按一下
        Hold  // 按住不放，直到停止
    }

    /// <summary>自动重复发送键盘按键的引擎。</summary>
    internal class KeyboardSpammer
    {
        private volatile bool _running;
        private int _gen; // 代际: Stop 自增, 防止 Stop→Start 竞态双线程并发(同 AutoClicker)
        private Thread _thread;

        /// <summary>运行结束时触发，在后台线程上回调。</summary>
        public event Action Stopped;

        public bool Running { get { return _running; } }
        public int Vk { get; set; }
        public int IntervalMs { get; set; }
        public KeySpamMode Mode { get; set; }
        public bool ExtendedKey { get; set; }
        public int RunMinutes { get; set; }     // 运行分钟数, 0 = 不限
        public string UntilTime { get; set; }   // 运行到系统时刻 "HH:mm", 空 = 不限

        public void Start()
        {
            if (_running) return;
            int gen = _gen;
            _running = true;
            _thread = new Thread(delegate() { Loop(gen); }) { IsBackground = true, Name = "KeyboardSpammer" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _gen++;
        }

        /// <summary>退出前等待线程结束(超时后强杀, 确保 finally 已执行, 防止按键残留)。</summary>
        public void WaitExit(int ms)
        {
            Thread t = _thread;
            if (t == null || t == Thread.CurrentThread) return;
            try
            {
                if (!t.Join(ms))
                {
                    try { t.Abort(); } catch (Exception) { }
                    try { t.Join(200); } catch (Exception) { }
                }
            }
            catch (Exception)
            {
            }
        }

        private bool Alive(int gen) { return _running && _gen == gen; }

        /// <summary>运行时长/到点停止是否已到期。</summary>
        private bool TimeUp(DateTime started, DateTime? untilTarget)
        {
            if (RunMinutes > 0 && (DateTime.Now - started).TotalMinutes >= RunMinutes) return true;
            if (untilTarget.HasValue && DateTime.Now >= untilTarget.Value) return true;
            return false;
        }

        private void Loop(int gen)
        {
            bool held = false;
            DateTime started = DateTime.Now;
            DateTime? untilTarget = Util.ParseUntilTime(UntilTime);
            try
            {
                if (Mode == KeySpamMode.Hold)
                {
                    InputSimulator.KeyDown(Vk, ExtendedKey);
                    held = true;
                    while (Alive(gen))
                    {
                        // 运行时长/到点停止(按住模式下按 10ms 粒度检查)
                        if (TimeUp(started, untilTarget)) break;
                        Thread.Sleep(10);
                    }
                }
                else
                {
                    while (Alive(gen))
                    {
                        InputSimulator.KeyDown(Vk, ExtendedKey);
                        // 拟人化: 按下与抬起之间留随机时长
                        try { Thread.Sleep(Math.Max(10, Humanizer.NextPressDuration())); }
                        finally { InputSimulator.KeyUp(Vk, ExtendedKey); }
                        if (!Alive(gen)) break;
                        if (TimeUp(started, untilTarget)) break;

                        int sleep = Math.Max(1, Humanizer.NextInterval(IntervalMs));
                        if (!Util.SleepInterruptible(sleep, delegate { return Alive(gen); })) break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("键盘连按引擎异常停止: " + ex.Message);
            }
            finally
            {
                if (held) InputSimulator.KeyUp(Vk, ExtendedKey); // 保证松开，避免按键卡死
                if (_gen == gen)
                {
                    _running = false;
                    var handler = Stopped;
                    if (handler != null) handler();
                }
            }
        }
    }
}
