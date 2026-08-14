using System;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>按固定间隔自动点击鼠标的引擎。</summary>
    internal class AutoClicker
    {
        private volatile bool _running;
        private int _gen; // 代际: Stop 自增, 旧线程醒来后据此识别自己已过期, 防止 Stop→Start 竞态双线程并发
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
        public int RunMinutes { get; set; }     // 运行分钟数, 0 = 不限
        public string UntilTime { get; set; }   // 运行到系统时刻 "HH:mm", 空 = 不限

        public void Start()
        {
            if (_running) return;
            int gen = _gen; // 创建时代际即绑定线程, 防止"未及启动的旧线程在重启后冒充当前代"
            _running = true;
            _thread = new Thread(delegate() { Loop(gen); }) { IsBackground = true, Name = "AutoClicker" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _gen++;
        }

        /// <summary>退出前等待线程结束(超时后强杀, 确保 finally 已执行, 防止注入键残留)。</summary>
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

        private void Loop(int gen)
        {
            long count = 0;
            DateTime started = DateTime.Now;
            DateTime? untilTarget = Util.ParseUntilTime(UntilTime);
            try
            {
                while (Alive(gen))
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
                    if (RunMinutes > 0 && (DateTime.Now - started).TotalMinutes >= RunMinutes) break;
                    if (untilTarget.HasValue && DateTime.Now >= untilTarget.Value) break;

                    // 分段休眠，保证 Stop() 在长间隔下也能快速响应
                    int sleep = Math.Max(1, Humanizer.NextInterval(IntervalMs));
                    if (!Util.SleepInterruptible(sleep, delegate { return Alive(gen); })) break;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("连点引擎异常停止: " + ex.Message);
            }
            finally
            {
                // 只有当前代际的线程才有资格收尾, 避免旧线程误清新线程的状态/误触发 Stopped
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
