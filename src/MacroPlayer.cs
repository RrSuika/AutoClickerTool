using System;
using System.Collections.Generic;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>按录制的时序回放宏。</summary>
    internal class MacroPlayer
    {
        private volatile bool _playing;
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

        // 回放中当前按住未抬起的键/按钮, 停止/异常时用于补发抬起, 防止卡键
        private int _heldKeyVk;
        private bool _heldKeyExt;
        private MouseButton _heldBtn = (MouseButton)(-1);
        private Hotkey _heldCombo;

        public void Start()
        {
            if (_playing) return;
            _playing = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "MacroPlayer" };
            _thread.Start();
        }

        public void Stop()
        {
            _playing = false;
        }

        /// <summary>可中断的分段休眠, 保证 Stop() 快速响应; 返回 false 表示回放已停止。</summary>
        private bool SleepMs(int ms)
        {
            if (ms <= 0) return _playing;
            for (int i = 0; i < ms / 10; i++)
            {
                if (!_playing) return false;
                Thread.Sleep(10);
            }
            if (!_playing) return false;
            Thread.Sleep(ms % 10);
            return _playing;
        }

        private void Run()
        {
            try
            {
                DateTime started = DateTime.Now;
                int played = 0;
                do
                {
                    foreach (MacroEvent e in Events)
                    {
                        if (!_playing) return;
                        if (!PlayEvent(e)) return;
                    }
                    played++;
                    if (LoopCount > 0 && played >= LoopCount) break;
                    if (RunMinutes > 0 && (DateTime.Now - started).TotalMinutes >= RunMinutes) break;
                    if (!string.IsNullOrEmpty(UntilTime))
                    {
                        DateTime until;
                        if (DateTime.TryParseExact(UntilTime, "HH:mm",
                                System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.None, out until))
                        {
                            if (DateTime.Now >= DateTime.Today.Add(until.TimeOfDay)) break;
                        }
                    }
                } while (Loop && _playing);
            }
            catch (Exception)
            {
            }
            finally
            {
                ReleaseHeld(); // 补发抬起, 避免停止时按住键/鼠标键残留卡键
                _playing = false;
                var handler = Finished;
                if (handler != null) handler();
            }
        }

        /// <summary>回放停止/异常时释放所有仍按住的键与鼠标键。</summary>
        private void ReleaseHeld()
        {
            try
            {
                if (_heldBtn != (MouseButton)(-1))
                {
                    InputSimulator.MouseUp(_heldBtn);
                    _heldBtn = (MouseButton)(-1);
                }
                if (_heldKeyVk != 0)
                {
                    InputSimulator.KeyUp(_heldKeyVk, _heldKeyExt);
                    _heldKeyVk = 0;
                }
                if (_heldCombo != null)
                {
                    InputSimulator.HotkeyUp(_heldCombo);
                    _heldCombo = null;
                }
            }
            catch (Exception)
            {
            }
        }

        private bool PlayEvent(MacroEvent e)
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
                            if (!SleepMs(ms - est)) return false;
                            InputSimulator.MoveToWithTrajectory(tx, ty, est);
                        }
                        else
                        {
                            // 延迟预算不足以完成平滑轨迹(如倍速很高): 先等剩余预算再快速移动
                            if (!SleepMs(Math.Max(0, ms - 30))) return false;
                            InputSimulator.MoveToWithTrajectory(tx, ty, Math.Min(ms, 30));
                        }
                    }
                    else
                    {
                        if (!SleepMs(ms)) return false;
                        InputSimulator.MoveTo(tx, ty);
                    }
                    return true;
                }
                case MacroEventKind.LeftDown:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.MouseDown(MouseButton.Left);
                    _heldBtn = MouseButton.Left;
                    return true;
                case MacroEventKind.LeftUp:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.MouseUp(MouseButton.Left);
                    _heldBtn = (MouseButton)(-1);
                    return true;
                case MacroEventKind.RightDown:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.MouseDown(MouseButton.Right);
                    _heldBtn = MouseButton.Right;
                    return true;
                case MacroEventKind.RightUp:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.MouseUp(MouseButton.Right);
                    _heldBtn = (MouseButton)(-1);
                    return true;
                case MacroEventKind.MiddleDown:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.MouseDown(MouseButton.Middle);
                    _heldBtn = MouseButton.Middle;
                    return true;
                case MacroEventKind.MiddleUp:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.MouseUp(MouseButton.Middle);
                    _heldBtn = (MouseButton)(-1);
                    return true;
                case MacroEventKind.Wheel:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.Wheel(e.Data);
                    return true;
                case MacroEventKind.KeyDown:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.KeyDown(e.Data, InputSimulator.IsExtendedKey(e.Data));
                    _heldKeyVk = e.Data;
                    _heldKeyExt = InputSimulator.IsExtendedKey(e.Data);
                    return true;
                case MacroEventKind.KeyUp:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.KeyUp(e.Data, InputSimulator.IsExtendedKey(e.Data));
                    _heldKeyVk = 0;
                    return true;
                // 按键精灵风格合并事件: 单击/点按 = 按下 + 拟人时长 + 抬起
                case MacroEventKind.LeftClick:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.Click(MouseButton.Left);
                    return true;
                case MacroEventKind.RightClick:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.Click(MouseButton.Right);
                    return true;
                case MacroEventKind.MiddleClick:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.Click(MouseButton.Middle);
                    return true;
                case MacroEventKind.KeyTap:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.KeyTap(e.Data, InputSimulator.IsExtendedKey(e.Data));
                    return true;
                case MacroEventKind.Delay:
                    // 纯延迟事件: 只需等待(上面已按拟人化间隔睡眠), 不产生动作
                    if (!SleepMs(ms)) return false;
                    return true;
                case MacroEventKind.KeyComboTap:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.HotkeyTap(Hotkey.Parse(e.Combo));
                    return true;
                case MacroEventKind.KeyComboDown:
                    if (!SleepMs(ms)) return false;
                    _heldCombo = Hotkey.Parse(e.Combo);
                    InputSimulator.HotkeyDown(_heldCombo);
                    return true;
                case MacroEventKind.KeyComboUp:
                    if (!SleepMs(ms)) return false;
                    InputSimulator.HotkeyUp(Hotkey.Parse(e.Combo));
                    _heldCombo = null;
                    return true;
            }
            return true;
        }
    }
}
