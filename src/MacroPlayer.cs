using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>按录制的时序回放宏。</summary>
    internal class MacroPlayer
    {
        /// <summary>
        /// 本轮回放按住未抬起的键/按钮集合, 停止/异常时用于补发抬起, 防止卡键。
        /// 必须用集合而不能只记"最后一个键": 同时按住多个键(如 Shift+W)时,
        /// 较早按下那个键的记录一旦被覆盖, 停止回放时它就收不到抬起 ——
        /// 注入的键会在系统层面永久保持按下(见 HeldKeys 注释 / 手册坑 32)。
        /// </summary>
        private sealed class HeldState
        {
            public readonly HashSet<MouseButton> Buttons = new HashSet<MouseButton>();
            public readonly HeldKeys Keys = new HeldKeys();
            public readonly HashSet<string> Combos = new HashSet<string>(); // 组合键原始字符串
        }

        private volatile bool _playing;
        private volatile int _gen; // 代际: Start 自增, 防止 Stop→Start 竞态双线程并发(同 AutoClicker)
        private Thread _thread;

        /// <summary>回放结束时触发，在后台线程上回调。</summary>
        public event Action Finished;

        public bool Playing { get { return _playing; } }
        public List<MacroEvent> Events { get; set; }
        public double Speed { get; set; }
        /// <summary>回放轮数, 0 = 无限循环。</summary>
        public int RepeatCount { get; set; }
        /// <summary>运行到该绝对时刻自动停止; null = 不限时。</summary>
        public DateTime? UntilAt { get; set; }

        public void Start()
        {
            if (_playing) return;
            _gen++;          // 新代际: 使旧线程 finally 里 _gen != gen 而放弃收尾(Stop→Start 竞态防护)
            int gen = _gen;  // 当前线程绑定新代际
            _playing = true;
            _thread = new Thread(delegate() { Run(gen); }) { IsBackground = true, Name = "MacroPlayer" };
            _thread.Start();
        }

        public void Stop()
        {
            _playing = false; // 只置停止标志, 不增代际: 让当前线程 finally 里 _gen == gen 从而触发 Finished
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
                int played = 0;
                do
                {
                    foreach (MacroEvent e in Events)
                    {
                        if (!Alive(gen)) return;
                        if (!PlayEvent(e, held, gen)) return;
                    }
                    played++;
                    if (RepeatCount > 0 && played >= RepeatCount) break;
                    if (UntilAt.HasValue && DateTime.Now >= UntilAt.Value) break;
                } while (Alive(gen));
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

        /// <summary>回放停止/异常时释放所有仍按住的键与鼠标键(逐个补发, 一个都不漏)。</summary>
        private static void ReleaseHeld(HeldState held)
        {
            int released = 0;
            try
            {
                foreach (MouseButton btn in new List<MouseButton>(held.Buttons))
                {
                    try { InputSimulator.MouseUp(btn); released++; }
                    catch (Exception ex) { Log.Warn("补发鼠标抬起失败: " + ex.Message); }
                }
                held.Buttons.Clear();

                foreach (KeyValuePair<int, bool> kv in held.Keys.TakeAll())
                {
                    try { InputSimulator.KeyUp(kv.Key, kv.Value); released++; }
                    catch (Exception ex) { Log.Warn("补发按键抬起失败: " + ex.Message); }
                }

                foreach (string combo in new List<string>(held.Combos))
                {
                    try { InputSimulator.HotkeyUp(Hotkey.Parse(combo)); released++; }
                    catch (Exception ex) { Log.Warn("补发组合键抬起失败: " + ex.Message); }
                }
                held.Combos.Clear();

                // 停止回放时还有按住键 = 宏缺少对应的抬起事件: 记日志便于排查"卡键/回车失灵"
                if (released > 0) Log.Warn("回放停止: 补发抬起 " + released + " 个仍被按住的按键/按钮");
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
                    held.Buttons.Add(MouseButton.Left);
                    return true;
                case MacroEventKind.LeftUp:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseUp(MouseButton.Left);
                    held.Buttons.Remove(MouseButton.Left);
                    return true;
                case MacroEventKind.RightDown:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseDown(MouseButton.Right);
                    held.Buttons.Add(MouseButton.Right);
                    return true;
                case MacroEventKind.RightUp:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseUp(MouseButton.Right);
                    held.Buttons.Remove(MouseButton.Right);
                    return true;
                case MacroEventKind.MiddleDown:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseDown(MouseButton.Middle);
                    held.Buttons.Add(MouseButton.Middle);
                    return true;
                case MacroEventKind.MiddleUp:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.MouseUp(MouseButton.Middle);
                    held.Buttons.Remove(MouseButton.Middle);
                    return true;
                case MacroEventKind.Wheel:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.Wheel(e.Data);
                    return true;
                case MacroEventKind.KeyDown:
                {
                    if (!SleepMs(ms, gen)) return false;
                    bool ext = InputSimulator.IsExtendedKey(e.Data);
                    InputSimulator.KeyDown(e.Data, ext);
                    held.Keys.Add(e.Data, ext);   // 集合记录: 不会覆盖同时按住的其它键
                    return true;
                }
                case MacroEventKind.KeyUp:
                    if (!SleepMs(ms, gen)) return false;
                    InputSimulator.KeyUp(e.Data, InputSimulator.IsExtendedKey(e.Data));
                    held.Keys.Remove(e.Data);     // 只移除这个键, 其它按住的键保持记录
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
                {
                    if (!SleepMs(ms, gen)) return false;
                    Hotkey down = Hotkey.Parse(e.Combo);
                    if (down != null && !string.IsNullOrEmpty(e.Combo)) held.Combos.Add(e.Combo);
                    InputSimulator.HotkeyDown(down);
                    return true;
                }
                case MacroEventKind.KeyComboUp:
                    if (!SleepMs(ms, gen)) return false;
                    if (!string.IsNullOrEmpty(e.Combo)) held.Combos.Remove(e.Combo);
                    InputSimulator.HotkeyUp(Hotkey.Parse(e.Combo));
                    return true;
            }
            return true;
        }
    }
}
