using System;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>按固定间隔自动点击鼠标的引擎。</summary>
    internal class AutoClicker
    {
        private volatile bool _running;
        private Thread _thread;

        /// <summary>运行结束（手动停止或执行完次数）时触发，在后台线程上回调。</summary>
        public event Action Stopped;

        public bool Running { get { return _running; } }

        public int IntervalMs { get; set; }
        public MouseButton Button { get; set; }
        public bool FixedPosition { get; set; }
        public int FixedX { get; set; }
        public int FixedY { get; set; }
        public int RepeatCount { get; set; } // 0 = 无限

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "AutoClicker" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
        }

        private void Loop()
        {
            long count = 0;
            try
            {
                while (_running)
                {
                    if (FixedPosition)
                    {
                        // 拟人化: 固定坐标上做 ±N 像素微抖动, 避免每次落点完全一致
                        int jx = Humanizer.JitterX(FixedX);
                        int jy = Humanizer.JitterY(FixedY);
                        InputSimulator.ClickAt(jx, jy, Button);
                    }
                    else
                    {
                        InputSimulator.Click(Button);
                    }

                    count++;
                    if (RepeatCount > 0 && count >= RepeatCount) break;

                    // 分段休眠，保证 Stop() 在长间隔下也能快速响应
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
            catch (Exception)
            {
                // 输入失败时静默退出，由 Stopped 事件通知界面
            }
            finally
            {
                _running = false;
                var handler = Stopped;
                if (handler != null) handler();
            }
        }
    }
}
