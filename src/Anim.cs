using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AutoClickerTool
{
    /// <summary>
    /// 轻量动效引擎: 单个全局 Timer 驱动, 对每个动画属性做指数平滑逼近目标值。
    /// 特性:
    ///  - 可中断/可重定向: 鼠标移出再移入、按压中松手都不会跳变(等效于可中断的 CSS transition)
    ///  - 指数平滑天然是 ease-out(起手快、收尾慢), 符合 UI 微动效规范(不用 ease-in)
    ///  - 空闲时 Timer 自动停止, 零持续开销
    ///  - Enabled = false 时所有动画瞬间到达目标(等效于 prefers-reduced-motion)
    /// 用法: 控件在事件里调用 Anim.To(setValue, current, target, tauMs), setValue 更新字段并 Invalidate。
    /// </summary>
    internal static class Anim
    {
        public static bool Enabled = true;

        private sealed class Item
        {
            public Action<float> SetValue;
            public float Value;
            public float Target;
            public float Tau; // 时间常数(ms), 越小越快
        }

        private static readonly List<Item> Items = new List<Item>();
        private static Timer _timer;
        private static DateTime _last;

        /// <summary>开始(或重定向)一个动画: 把 setValue 对应的值从 current 平滑逼近 target。</summary>
        public static void To(Action<float> setValue, float current, float target, float tauMs)
        {
            if (!Enabled)
            {
                setValue(target);
                return;
            }
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].SetValue == setValue)
                {
                    Items[i].Target = target; // 重定向: 从当前值继续, 不跳变
                    Items[i].Tau = Math.Max(1f, tauMs);
                    EnsureTimer();
                    return;
                }
            }
            Items.Add(new Item { SetValue = setValue, Value = current, Target = target, Tau = Math.Max(1f, tauMs) });
            EnsureTimer();
        }

        /// <summary>目标值取反方向的动画(快捷方式, 供 enter/leave 切换用)。</summary>
        public static void To(Action<float> setValue, float current, bool target, float tauMs)
        {
            To(setValue, current, target ? 1f : 0f, tauMs);
        }

        private static void EnsureTimer()
        {
            if (_timer == null)
            {
                _timer = new Timer { Interval = 16 };
                _timer.Tick += Tick;
                _last = DateTime.Now;
            }
            if (!_timer.Enabled) _timer.Start();
        }

        private static void Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            float dt = (float)(now - _last).TotalMilliseconds;
            _last = now;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var it = Items[i];
                if (!Enabled)
                {
                    it.Value = it.Target;
                    try { it.SetValue(it.Target); }
                    catch (Exception) { }
                    Items.RemoveAt(i);
                    continue;
                }
                float k = 1f - (float)Math.Exp(-dt / it.Tau);
                it.Value += (it.Target - it.Value) * k;
                try
                {
                    it.SetValue(it.Value);
                }
                catch (Exception)
                {
                    // 控件可能已被销毁(主题/DPI 重建时 Controls.Clear): 停止该动画
                    Items.RemoveAt(i);
                    continue;
                }
                if (Math.Abs(it.Target - it.Value) < 0.0015f)
                {
                    try { it.SetValue(it.Target); }
                    catch (Exception) { }
                    Items.RemoveAt(i);
                }
            }
            if (Items.Count == 0 && _timer != null) _timer.Stop();
        }

        /// <summary>清空所有动画(主题/DPI 重建、销毁控件前调用, 避免回调已销毁控件)。</summary>
        public static void Clear()
        {
            Items.Clear();
            if (_timer != null) _timer.Stop();
        }

        // ---- 缓动曲线 ----
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        /// <summary>强 ease-out: 起手快、收尾缓(UI 反馈用)。</summary>
        public static float EaseOutCubic(float t) { float p = 1f - Clamp01(t); return 1f - p * p * p; }

        /// <summary>ease-in-out: 屏幕内位移/形态变化用。</summary>
        public static float EaseInOutCubic(float t)
        {
            t = Clamp01(t);
            return t < 0.5f ? 4f * t * t * t : 1f - (float)Math.Pow(-2f * t + 2f, 3f) / 2f;
        }
    }
}
