using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AutoClickerTool
{
    internal enum MacroEventKind : int
    {
        Move = 0,
        LeftDown = 1,
        LeftUp = 2,
        RightDown = 3,
        RightUp = 4,
        MiddleDown = 5,
        MiddleUp = 6,
        Wheel = 7,
        KeyDown = 8,
        KeyUp = 9,
        // 按键精灵风格合并事件: 短按一次只留一条记录
        LeftClick = 10,    // 左键单击 = 按下+抬起
        RightClick = 11,   // 右键单击
        MiddleClick = 12,  // 中键单击
        KeyTap = 13,       // 按键点按 = 按下+抬起
        Delay = 14,        // 纯延迟(不产生任何动作)
        KeyComboTap = 15,  // 组合键点按(如 Ctrl+C), 组合串存 Combo 字段
        KeyComboDown = 16, // 组合键按下(按下所有修饰键+键)
        KeyComboUp = 17    // 组合键抬起(松开所有修饰键+键)
    }

    internal class MacroEvent
    {
        public MacroEventKind Kind { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Data { get; set; }      // 按键虚拟键码 或 滚轮增量
        public string Combo { get; set; }   // 组合键字符串(如 "Ctrl+C"), 仅 KeyComboTap 使用
        public int DelayMs { get; set; }    // 与上一事件的间隔
    }

    /// <summary>
    /// 通过低级钩子录制全局鼠标键盘操作。
    /// 录制时做按键精灵风格的合并:
    ///   1. 连续鼠标移动合并成一条"移动到终点"(中途只更新坐标, 延迟累加, 不产生大量中间事件);
    ///   2. 短按(按下后 250ms 内抬起)的鼠标键/键盘键合并为一条"单击/按键"事件;
    ///      按住超过 250ms 的长按仍保留 按下/抬起 两条, 支持蓄力类操作。
    /// </summary>
    internal class MacroRecorder : IDisposable
    {
        public const int MaxEvents = 200000;

        /// <summary>两次移动消息间隔超过该值则视为停顿, 新起一条移动记录。</summary>
        private const int MoveMergeGapMs = 500;

        /// <summary>按下到抬起不超过该值则合并为单击/按键。</summary>
        private const int TapMergeMaxMs = 250;

        private readonly List<MacroEvent> _events = new List<MacroEvent>();
        private readonly NativeMethods.LowLevelProc _mouseProc;
        private readonly NativeMethods.LowLevelProc _keyboardProc;

        private IntPtr _mouseHook;
        private IntPtr _keyboardHook;
        private Stopwatch _sw;
        private readonly Stopwatch _holdSwBtn = new Stopwatch(); // 最近一次鼠标按下的时长计时
        private readonly Stopwatch _holdSwKey = new Stopwatch(); // 最近一次键盘按下的时长计时(分开计时, 避免互相重置误判长按)
        private int _pendBtn = -1;   // 最近按下未抬起的鼠标键: 0左 1右 2中, -1 无
        private int _pendKey;        // 最近按下未抬起的键盘键 vk
        private bool _disposed;

        /// <summary>事件数达到上限时触发（钩子回调线程上），由界面延迟执行 Stop。</summary>
        public event Action AutoStopRequested;

        /// <summary>热键管理器引用: 已绑定的热键按键不会被录进宏, 避免回放时误触开关。</summary>
        public HotkeyManager Hotkeys;

        public bool Recording { get; private set; }
        public int Count { get { return _events.Count; } }

        /// <summary>事件列表(仅停止录制后编辑; 录制中只读访问)。</summary>
        public List<MacroEvent> Events { get { return _events; } }

        public MacroRecorder()
        {
            _mouseProc = MouseHookProc;
            _keyboardProc = KeyboardHookProc;
        }

        public void Start()
        {
            if (Recording || _disposed) return;
            _events.Clear();
            _pendBtn = -1;
            _pendKey = 0;
            _sw = Stopwatch.StartNew();
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, NativeMethods.GetModuleHandle(null), 0);
            _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, NativeMethods.GetModuleHandle(null), 0);
            Recording = _mouseHook != IntPtr.Zero && _keyboardHook != IntPtr.Zero;
            if (!Recording)
            {
                if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
                if (_keyboardHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _mouseHook = IntPtr.Zero;
                _keyboardHook = IntPtr.Zero;
            }
        }

        public void Stop()
        {
            if (!Recording) return;
            if (_mouseHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_keyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            Recording = false;
        }

        public List<MacroEvent> Snapshot()
        {
            return new List<MacroEvent>(_events);
        }

        public void Load(IEnumerable<MacroEvent> events)
        {
            if (Recording) return;
            _events.Clear();
            if (events == null) return;
            foreach (var e in events)
            {
                if (e == null) continue;
                Sanitize(e);
                _events.Add(e);
            }
        }

        /// <summary>校正单条事件数据: Kind 越界 / 坐标非法 / 负延迟 / 组合键字段缺失都收敛到安全值, 防止损坏宏回放异常。</summary>
        private static void Sanitize(MacroEvent e)
        {
            int k = (int)e.Kind;
            if (k < 0 || k > 17) e.Kind = MacroEventKind.Delay;
            // 允许负坐标: 副屏在主屏左侧/上方时系统坐标为负, 钳 0 会让回放全部指向主屏左上角
            if (e.X < -32768) e.X = -32768;
            if (e.Y < -32768) e.Y = -32768;
            if (e.X > 32767) e.X = 32767;
            if (e.Y > 32767) e.Y = 32767;
            if (e.DelayMs < 0) e.DelayMs = 0;
            if (e.Kind == MacroEventKind.KeyComboTap || e.Kind == MacroEventKind.KeyComboDown || e.Kind == MacroEventKind.KeyComboUp)
            {
                if (string.IsNullOrEmpty(e.Combo)) e.Kind = MacroEventKind.Delay; // 组合键缺字段则退化为延迟, 避免空解析
            }
        }

        public void Clear()
        {
            if (Recording) return;
            _events.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        /// <summary>已绑定的热键触发键不录进宏，避免回放时误触开关。</summary>
        private bool IsHotkeyKey(uint vk)
        {
            return Hotkeys != null && Hotkeys.IsHotkeyKey(vk);
        }

        /// <summary>鼠标消息对应的虚拟键码(热键可能绑定鼠标键); 非按键消息返回 true 表示不拦截。</summary>
        private bool MouseKeyAllowed(uint wParam, uint mouseData)
        {
            uint vk;
            switch ((int)wParam)
            {
                case NativeMethods.WM_LBUTTONDOWN:
                case NativeMethods.WM_LBUTTONUP: vk = 0x01; break;
                case NativeMethods.WM_RBUTTONDOWN:
                case NativeMethods.WM_RBUTTONUP: vk = 0x02; break;
                case NativeMethods.WM_MBUTTONDOWN:
                case NativeMethods.WM_MBUTTONUP: vk = 0x04; break;
                case NativeMethods.WM_XBUTTONDOWN:
                case NativeMethods.WM_XBUTTONUP:
                    vk = ((mouseData >> 16) & 0xFFFF) == 1 ? 0x05u : 0x06u;
                    break;
                default: return true; // 移动/滚轮不受热键影响
            }
            return !IsHotkeyKey(vk);
        }

        private void Record(MacroEventKind kind, int x, int y, int data)
        {
            if (_events.Count >= MaxEvents)
            {
                var handler = AutoStopRequested;
                if (handler != null) handler();
                return;
            }
            int delay = (int)_sw.ElapsedMilliseconds;
            _sw.Restart();
            _events.Add(new MacroEvent { Kind = kind, X = x, Y = y, Data = data, DelayMs = delay });
        }

        /// <summary>合并连续移动: 最后一条是 Move 且距上条消息未超停顿阈值时, 只更新终点坐标并累加延迟。</summary>
        private bool TryMergeMove(int x, int y)
        {
            if (_events.Count == 0) return false;
            var last = _events[_events.Count - 1];
            if (last.Kind != MacroEventKind.Move) return false;
            int sinceLast = (int)_sw.ElapsedMilliseconds;
            if (sinceLast > MoveMergeGapMs) return false; // 停顿太久, 新起一条
            last.X = x;
            last.Y = y;
            last.DelayMs += sinceLast;
            _sw.Restart();
            return true;
        }

        /// <summary>鼠标按下/抬起合并: 短按变单击一条, 长按保留 按下/抬起 两条。</summary>
        private void HandleMouseButton(bool down, int btn, int x, int y, MacroEventKind downKind)
        {
            if (down)
            {
                _pendBtn = btn;
                _holdSwBtn.Restart();
                Record(downKind, x, y, 0);
                return;
            }
            long held = _holdSwBtn.ElapsedMilliseconds;
            bool merged = _pendBtn == btn && held >= 0 && held <= TapMergeMaxMs;
            if (merged && _events.Count > 0)
            {
                var last = _events[_events.Count - 1];
                // 仅当抬起前没有任何其它事件插入时才合并(拖拽时中间会有移动事件, 不合并)
                if (last.Kind != downKind) merged = false;
            }
            _pendBtn = -1;
            if (merged)
            {
                var last = _events[_events.Count - 1];
                last.Kind = MergeClickKind(downKind);
            }
            else
            {
                Record(MergeUpKind(downKind), x, y, 0);
            }
        }

        private static MacroEventKind MergeClickKind(MacroEventKind down)
        {
            switch (down)
            {
                case MacroEventKind.RightDown: return MacroEventKind.RightClick;
                case MacroEventKind.MiddleDown: return MacroEventKind.MiddleClick;
                default: return MacroEventKind.LeftClick;
            }
        }

        private static MacroEventKind MergeUpKind(MacroEventKind down)
        {
            switch (down)
            {
                case MacroEventKind.RightDown: return MacroEventKind.RightUp;
                case MacroEventKind.MiddleDown: return MacroEventKind.MiddleUp;
                default: return MacroEventKind.LeftUp;
            }
        }

        /// <summary>键盘按下/抬起合并: 短按变一条"按键", 长按保留两条; 自动重复的 KeyDown 不重复录制。</summary>
        private void HandleKey(bool down, int vk)
        {
            if (down)
            {
                if (_pendKey == vk) return; // 按住时的自动重复消息, 只录第一次按下
                _pendKey = vk;
                _holdSwKey.Restart();
                Record(MacroEventKind.KeyDown, 0, 0, vk);
                return;
            }
            long held = _holdSwKey.ElapsedMilliseconds;
            bool merged = _pendKey == vk && held >= 0 && held <= TapMergeMaxMs;
            if (merged && _events.Count > 0)
            {
                var last = _events[_events.Count - 1];
                if (last.Kind != MacroEventKind.KeyDown || last.Data != vk) merged = false;
            }
            _pendKey = 0;
            if (merged)
            {
                var last = _events[_events.Count - 1];
                last.Kind = MacroEventKind.KeyTap;
            }
            else
            {
                Record(MacroEventKind.KeyUp, 0, 0, vk);
            }
        }

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                // 忽略程序自身注入的事件(LLMHF_INJECTED): 只录制用户真实操作
                if ((info.flags & 0x01) != 0)
                    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                if (!MouseKeyAllowed((uint)wParam, info.mouseData))
                    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                int x = info.pt.x;
                int y = info.pt.y;
                switch ((int)wParam)
                {
                    case NativeMethods.WM_MOUSEMOVE:
                        // 连续移动合并为一条"移动到终点"
                        if (!TryMergeMove(x, y)) Record(MacroEventKind.Move, x, y, 0);
                        break;
                    case NativeMethods.WM_LBUTTONDOWN:
                        HandleMouseButton(true, 0, x, y, MacroEventKind.LeftDown);
                        break;
                    case NativeMethods.WM_LBUTTONUP:
                        HandleMouseButton(false, 0, x, y, MacroEventKind.LeftDown);
                        break;
                    case NativeMethods.WM_RBUTTONDOWN:
                        HandleMouseButton(true, 1, x, y, MacroEventKind.RightDown);
                        break;
                    case NativeMethods.WM_RBUTTONUP:
                        HandleMouseButton(false, 1, x, y, MacroEventKind.RightDown);
                        break;
                    case NativeMethods.WM_MBUTTONDOWN:
                        HandleMouseButton(true, 2, x, y, MacroEventKind.MiddleDown);
                        break;
                    case NativeMethods.WM_MBUTTONUP:
                        HandleMouseButton(false, 2, x, y, MacroEventKind.MiddleDown);
                        break;
                    case NativeMethods.WM_MOUSEWHEEL:
                        Record(MacroEventKind.Wheel, x, y, (short)((info.mouseData >> 16) & 0xFFFF));
                        break;
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                int vk = (int)info.vkCode;
                // 忽略注入按键(LLKHF_INJECTED)与热键按键
                if ((info.flags & 0x10) == 0 && !IsHotkeyKey((uint)vk))
                {
                    switch ((int)wParam)
                    {
                        case NativeMethods.WM_KEYDOWN:
                        case NativeMethods.WM_SYSKEYDOWN:
                            HandleKey(true, vk);
                            break;
                        case NativeMethods.WM_KEYUP:
                        case NativeMethods.WM_SYSKEYUP:
                            HandleKey(false, vk);
                            break;
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }
    }
}
