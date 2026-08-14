using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>按录制的时序回放宏。</summary>
    internal class MacroPlayer
    {
        /// <summary>本轮回放按住未抬起的键/按钮集合, 停止/异常时用于补发抬起, 防止卡键。</summary>
        private sealed class HeldState
        {
            public MouseButton Btn = (MouseButton)(-1);
            public int KeyVk;
            public bool KeyExt;
            public Hotkey Combo;
        }

        private volatile bool _playing;
        private int _gen; // 代际: Stop 自增, 防止 Stop→Start 竞态双线程并发(同 AutoClicker)
        private Thread _thread;

        /// <summary>回放结束时触发，在后台线程上回调。</summary>
        public event Action Finished;

        public bool Playing { get { return _playing; } }
        public List<MacroEvent> Events { get; set; }
        public double Speed { get; set; }
        public bool Loop { get; set; }
        public int LoopCount { get; set; }      // 循环次数, 0 = 无限
        public int RunMinutes { get; set; }     // 运行分钟数, 0 = 不限
        public string UntilTime { get; set; }   // 运行到系统时刻 "HH:mm", 空 = 不限

        public void Start()
        {
            if (_playing) return;
            int gen = _gen; // 创建时代际即绑定线程, 防止"未及启动的旧线程在重启后冒充当前代"
            _playing = true;
            _thread = new Thread(delegate() { Run(gen); }) { IsBackground = true, Name = "MacroPlayer" };
            _thread.Start();
        }

        public void Stop()
        {
            _playing = false;
            _gen++;
        }

        /// <summary>退出前等待线程结束(超时后强杀, 确保 finally 补发抬起已执行, 防止卡键)。</summary>
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

        private bool Alive(int gen) { return _playing && _gen == gen; }

        /// <summary>可中断的分段休眠, 保证 Stop() 快速响应; 返回 false 表示回放已停止。</summary>
        private bool SleepMs(int ms, int gen)
        {
            if (ms <= 0) return Alive(gen);
            for (int i = 0; i < ms / 10; i++)
            {
                if (!Alive(gen)) return false;
                Thread.Sleep(10);
            }
            if (!Alive(gen)) return false;
            Thread.Sleep(ms % 10);
            return Alive(gen);
        }

        private void Run(int gen)
        {
            HeldState held = new HeldState(); // 按住状态线程局部化, 旧代线程无法干扰新代
            try
            {
                DateTime started = DateTime.Now;
                int played = 0;

                // 运行到时刻: 解析一次; 目标时刻已过(如 22:00 设 03:00)视为次日, 支持过夜挂机
                DateTime? untilTarget = null;
                if (!string.IsNullOrEmpty(UntilTime))
                {
                    DateTime until;
                    if (DateTime.TryParseExact(UntilTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out until))
                    {
                        until = DateTime.Today.Add(until.TimeOfDay);
                        if (until <= DateTime.Now) until = until.AddDays(1);
                        untilTarget = until;
                    }
                    else
                    {
                        Log.Warn("运行到时刻解析失败, 已忽略: " + UntilTime);
                    }
                }

                do
                {
                    foreach (MacroEvent e in Events)
                    {
                        if (!Alive(gen)) return;
                        if (!PlayEvent(e, held, gen)) return;
                    }
                    played++;
                    if (LoopCount > 0 && played >= LoopCount) break;
                    if (RunMinutes > 0 && (DateTime.Now - started).TotalMinutes >= RunMinutes) break;
                    if (untilTarget.HasValue && DateTime.Now >= untilTarget.Value) break;
                } while (Loop && Alive(gen));
            }
            catch (Exception ex)
            {
                Log.Warn("回放引擎异常停止: " + ex.Message);
            }
            finally
            {
                ReleaseHeld(held); // 补发抬起, 避免停止时按住键/鼠标键残留卡键
                if (_gen == gen)
                {
                    _playing = false;
                    var handler = Finished;
                    if (handler != null) handler();
                }
            }
        }

        /// <summary>回放停止/异常时释放所有仍按住的键与鼠标键。</summary>
        private static void ReleaseHeld(HeldState held)
        {
            try
            {
                if (held.Btn != (MouseButton)(-1))
                {
                    InputSimulator.MouseUp(held.Btn);
                    held.Btn = (MouseButton)(-1);
                }
                if (held.KeyVk != 0)
                {
                    InputSimulator.KeyUp(held.KeyVk, held.KeyExt);
                    held.KeyVk = 0;
                }
                if (held.Combo != null)
                {
                    InputSimulator.HotkeyUp(held.Combo);
                    held.Combo = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("补发抬起失败: " + ex.Message);
            }
        }

        private bool PlayEvent(MacroEvent e, HeldState held, int gen)
        {
            // 拟人化: 对事件间隔加随机抖动, 消除"完全一致"的时序特征
            int ms = (int)Math.Max(1, Humanizer.NextInterval(e.DelayMs) / Math.Max(0.01, Speed));

            switch (e.Kind)
            {
                case MacroEventKind.Move:
                {
                    int tx = Humanizer.JitterX(e.X);
                    int ty = Humanizer.JitterY(e.Y);
                    if (Humanizer.Enabled && Humanizer.TrajectoryEnabled)
                    {
                        // 轨迹移动: 拟人时长从该事件延迟预算中扣除, 回放总时长不变
                        NativeMethods.POINT cur;
                        int est = 0;
                        if (NativeMethods.GetCursorPos(out cur))
                        {
                            double dx = tx - cur.x;
                            double dy = ty - cur.y;
                            est = Humanizer.EstimateTrajectoryMs(Math.Sqrt(dx * dx + dy * dy));
                        }
                        if (est > 0 && ms > est)
                        {
                            if (!SleepMs(ms - est, gen)) return false;
                            InputSimulator.MoveToWithTrajectory(tx, ty, est);
                        }
                        else
                        {
                            // 延迟预算不足以完成平滑轨迹(如倍速很高): 先等剩余预算再快速移动
                            if (!SleepMs(Math.Max(0, ms - 30), gen)) return false;
                            InputSimulator.MoveToWithTrajectory(tx, ty, Math.Min(ms, 30));
                        }
                    }
                    else
                    {
                        if (!SleepMs(ms, gen)) return false;
                        InputSimulator.MoveTo(tx, ty);
                    }
                    return true;
                }
                case MacroEventKind.LeftDown:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseDown(MouseButton.Left);
                    held.Btn = MouseButton.Left;
                    return true;
                case MacroEventKind.LeftUp:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseUp(MouseButton.Left);
                    held.Btn = (MouseButton)(-1);
                    return true;
                case MacroEventKind.RightDown:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseDown(MouseButton.Right);
                    held.Btn = MouseButton.Right;
                    return true;
                case MacroEventKind.RightUp:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseUp(MouseButton.Right);
                    held.Btn = (MouseButton)(-1);
                    return true;
                case MacroEventKind.MiddleDown:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseDown(MouseButton.Middle);
                    held.Btn = MouseButton.Middle;
                    return true;
                case MacroEventKind.MiddleUp:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseUp(MouseButton.Middle);
                    held.Btn = (MouseButton)(-1);
                    return true;
                case MacroEventKind.Wheel:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.Wheel(e.Data);
                    return true;
                case MacroEventKind.KeyDown:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.KeyDown(e.Data, InputSimulator.IsExtendedKey(e.Data));
                    held.KeyVk = e.Data;
                    held.KeyExt = InputSimulator.IsExtendedKey(e.Data);
                    return true;
                case MacroEventKind.KeyUp:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.KeyUp(e.Data, InputSimulator.IsExtendedKey(e.Data));
                    held.KeyVk = 0;
                    return true;
                // 按键精灵风格合并事件: 单击/点按 = 按下 + 拟人时长 + 抬起
                case MacroEventKind.LeftClick:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.Click(MouseButton.Left);
                    return true;
                case MacroEventKind.RightClick:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.Click(MouseButton.Right);
                    return true;
                case MacroEventKind.MiddleClick:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.Click(MouseButton.Middle);
                    return true;
                case MacroEventKind.KeyTap:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.KeyTap(e.Data, InputSimulator.IsExtendedKey(e.Data));
                    return true;
                case MacroEventKind.Delay:
                    // 纯延迟事件: 只需等待(上面已按拟人化间隔睡眠), 不产生动作
                    if (!SleepMs(ms, gen)) return false;
                    return true;
                case MacroEventKind.KeyComboTap:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.HotkeyTap(Hotkey.Parse(e.Combo));
                    return true;
                case MacroEventKind.KeyComboDown:
                    if (!SleepMs(ms, gen)) return false;
                    held.Combo = Hotkey.Parse(e.Combo);
                    InputSimulator.HotkeyDown(held.Combo);
                    return true;
                case MacroEventKind.KeyComboUp:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.HotkeyUp(Hotkey.Parse(e.Combo));
                    held.Combo = null;
                    return true;
            }
            return true;
        }
    }
}
