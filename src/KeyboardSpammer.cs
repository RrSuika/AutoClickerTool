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
        private Thread _thread;

        /// <summary>运行结束时触发，在后台线程上回调。</summary>
        public event Action Stopped;

        public bool Running { get { return _running; } }
        public int Vk { get; set; }
        public int IntervalMs { get; set; }
        public KeySpamMode Mode { get; set; }
        public bool ExtendedKey { get; set; }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "KeyboardSpammer" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
        }

        private void Loop()
        {
            bool held = false;
            try
            {
                if (Mode == KeySpamMode.Hold)
                {
                    InputSimulator.KeyDown(Vk, ExtendedKey);
                    held = true;
                    while (_running) Thread.Sleep(10);
                }
                else
                {
                    while (_running)
                    {
                        InputSimulator.KeyDown(Vk, ExtendedKey);
                        // 拟人化: 按下与抬起之间留随机时长
                        try { Thread.Sleep(Math.Max(10, Humanizer.NextPressDuration())); }
                        finally { InputSimulator.KeyUp(Vk, ExtendedKey); }
                        if (!_running) break;

                        int sleep = Math.Max(1, Humanizer.NextInterval(IntervalMs));
                        for (int i = 0; i < sleep / 10; i++)
                        {
                            if (!_running) break;
                            Thread.Sleep(10);
                        }
                        if (!_running) break;
                        Thread.Sleep(sleep % 10);
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                if (held) InputSimulator.KeyUp(Vk, ExtendedKey); // 保证松开，避免按键卡死
                _running = false;
                var handler = Stopped;
                if (handler != null) handler();
            }
        }
    }
}
