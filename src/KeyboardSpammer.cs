using System;
using System.Threading;

namespace AutoClickerTool
{
    internal enum KeySpamMode
    {
        Tap,  // 每隔一段按一下
        Hold  // 按住不放，直到停止
    }

    /// <summary>
    /// 自动重复发送键盘按键的引擎。
    /// 按键用一条 Hotkey 表示, 因此支持**多个键同时按下**(如 A+Shift)。
    /// </summary>
    internal class KeyboardSpammer
    {
        private volatile bool _running;
        private volatile int _gen; // 代际: Start 自增, 防止 Stop→Start 竞态双线程并发(同 AutoClicker)
        private Thread _thread;

        /// <summary>运行结束时触发，在后台线程上回调。</summary>
        public event Action Stopped;

        public bool Running { get { return _running; } }

        /// <summary>要连按的按键(可多键同时按)。</summary>
        public Hotkey Combo { get; set; }
        public int IntervalMs { get; set; }
        public KeySpamMode Mode { get; set; }
        /// <summary>点按次数, 0 = 不限(按住模式忽略次数)。</summary>
        public int RepeatCount { get; set; }
        /// <summary>运行到该绝对时刻自动停止; null = 不限时。</summary>
        public DateTime? UntilAt { get; set; }

        public void Start()
        {
            if (_running || Combo == null) return;
            _gen++;          // 新代际: 使旧线程 finally 里 _gen != gen 而放弃收尾(Stop→Start 竞态防护)
            int gen = _gen;  // 当前线程绑定新代际
            _running = true;
            _thread = new Thread(delegate() { Loop(gen); }) { IsBackground = true, Name = "KeyboardSpammer" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false; // 只置停止标志, 不增代际: 让当前线程 finally 里 _gen == gen 从而触发 Stopped
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

        /// <summary>到点停止是否已到期。</summary>
        private bool TimeUp(DateTime? untilAt)
        {
            return untilAt.HasValue && DateTime.Now >= untilAt.Value;
        }

        private void Loop(int gen)
        {
            bool held = false;
            try
            {
                if (Mode == KeySpamMode.Hold)
                {
                    InputSimulator.HotkeyDown(Combo);
                    held = true;
                    while (Alive(gen))
                    {
                        // 运行时长/到点停止(按住模式下按 10ms 粒度检查)
                        if (TimeUp(UntilAt)) break;
                        Thread.Sleep(10);
                    }
                }
                else
                {
                    long count = 0;
                    while (Alive(gen))
                    {
                        // 组合键点按(支持多键同时按下), 按下与抬起之间留随机拟人时长
                        InputSimulator.HotkeyTap(Combo);
                        count++;
                        if (RepeatCount > 0 && count >= RepeatCount) break;
                        if (!Alive(gen)) break;
                        if (TimeUp(UntilAt)) break;

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
                if (held && Combo != null)
                {
                    try { InputSimulator.HotkeyUp(Combo); } catch (Exception) { } // 保证松开，避免按键卡死
                }
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
