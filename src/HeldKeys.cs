using System;
using System.Collections.Generic;

namespace AutoClickerTool
{
    /// <summary>
    /// 记录"本程序注入过、但还没有抬起"的按键(虚拟键码 → 是否扩展键)。
    ///
    /// 为什么必须有它: SendInput / 驱动注入的键按下后若一直没有对应的抬起, 这个键会在
    /// **系统层面保持按下**(进程退出也不会自动释放), 直到用户物理按一次这个键才会复位。
    /// 回放/连按里如果用单个字段记录"最后按下的键", 同时按住多个键(如 Shift+W)时,
    /// 较早按下那个键的记录会被后一个键覆盖 —— 停止时它就收不到抬起, 于是卡住;
    /// 用户看到的现象通常是: 修饰键卡住之后回车变成 Shift+Enter / Ctrl+Enter,
    /// 在任何程序里都"完全失灵", 连 Windows 屏幕键盘的回车也没用。
    /// 所以按住状态必须用集合完整记录, 收尾时逐个补发抬起。
    /// </summary>
    internal sealed class HeldKeys
    {
        private readonly Dictionary<int, bool> _down = new Dictionary<int, bool>(); // vk → 是否扩展键

        public int Count { get { return _down.Count; } }

        /// <summary>记录按下; 同一个键重复按下只保留一条(抬起一次即可)。</summary>
        public void Add(int vk, bool extended)
        {
            if (vk == 0) return;
            _down[vk] = extended;
        }

        /// <summary>抬起指定键; 返回它此前是否处于按住状态(只影响这一个键, 不会清掉其它按住的键)。</summary>
        public bool Remove(int vk)
        {
            return _down.Remove(vk);
        }

        public bool Contains(int vk)
        {
            return _down.ContainsKey(vk);
        }

        /// <summary>取出全部仍按住的键并清空集合; 调用方负责逐个补发抬起。</summary>
        public List<KeyValuePair<int, bool>> TakeAll()
        {
            var list = new List<KeyValuePair<int, bool>>(_down);
            _down.Clear();
            return list;
        }

        public void Clear()
        {
            _down.Clear();
        }
    }
}
