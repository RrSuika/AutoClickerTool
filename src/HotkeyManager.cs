using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AutoClickerTool
{
    internal enum HotkeyAction
    {
        Clicker = 0,   // 鼠标连点开关
        Record = 1,    // 录制开关
        Play = 2,      // 回放开关
        Keyboard = 3,  // 键盘连按开关
        StopAll = 4    // 全部停止
    }

    /// <summary>
    /// 一条热键: 修饰键集合 + 触发键集合。
    /// 支持任意键、Ctrl/Alt/Shift/Win 组合、多键同时按下组合(如 Ctrl+Q+W)、鼠标键(左/右/中/X1/X2)。
    /// </summary>
    internal class Hotkey
    {
        public const uint VK_SHIFT = 0x10;
        public const uint VK_CONTROL = 0x11;
        public const uint VK_MENU = 0x12;
        public const uint VK_LWIN = 0x5B;

        public readonly List<uint> Modifiers = new List<uint>();
        public readonly List<uint> Keys = new List<uint>();

        public static bool IsModifier(uint vk)
        {
            return vk == VK_SHIFT || vk == VK_CONTROL || vk == VK_MENU || vk == VK_LWIN;
        }

        /// <summary>左/右修饰键归一化到通用键码。</summary>
        public static uint Normalize(uint vk)
        {
            switch (vk)
            {
                case 0xA0: case 0xA1: return VK_SHIFT;   // 左/右 Shift
                case 0xA2: case 0xA3: return VK_CONTROL; // 左/右 Ctrl
                case 0xA4: case 0xA5: return VK_MENU;    // 左/右 Alt
                case 0x5C: return VK_LWIN;               // 右 Win
                default: return vk;
            }
        }

        // ---------- 名称 ↔ 虚拟键码 ----------

        private static readonly Dictionary<uint, string> VkToName = BuildVkToName();
        private static readonly Dictionary<string, uint> NameToVk = BuildNameToVk();

        private static Dictionary<string, uint> BuildNameToVk()
        {
            var d = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            Add(d, "CTRL", "CONTROL", "CTR").To(VK_CONTROL);
            Add(d, "SHIFT").To(VK_SHIFT);
            Add(d, "ALT").To(VK_MENU);
            Add(d, "WIN", "WINDOWS", "META").To(VK_LWIN);
            Add(d, "ESC", "ESCAPE").To(0x1B);
            Add(d, "SPACE", "SPACEBAR", "空格").To(0x20);
            Add(d, "ENTER", "RETURN", "回车").To(0x0D);
            Add(d, "TAB").To(0x09);
            Add(d, "BACKSPACE", "BACK", "退格").To(0x08);
            Add(d, "CAPSLOCK", "CAPS").To(0x14);
            Add(d, "INSERT", "INS").To(0x2D);
            Add(d, "DELETE", "DEL").To(0x2E);
            Add(d, "HOME").To(0x24);
            Add(d, "END").To(0x23);
            Add(d, "PAGEUP", "PGUP").To(0x21);
            Add(d, "PAGEDOWN", "PGDN").To(0x22);
            Add(d, "PRINTSCREEN", "PRTSC").To(0x2C);
            Add(d, "SCROLLLOCK").To(0x91);
            Add(d, "PAUSE").To(0x13);
            Add(d, "NUMLOCK").To(0x90);
            Add(d, "UP", "上").To(0x26);
            Add(d, "DOWN", "下").To(0x28);
            Add(d, "LEFT", "左").To(0x25);
            Add(d, "RIGHT", "右").To(0x27);
            Add(d, "LBUTTON", "MOUSELEFT", "MOUSE LEFT", "鼠标左键").To(0x01);
            Add(d, "RBUTTON", "MOUSERIGHT", "MOUSE RIGHT", "鼠标右键").To(0x02);
            Add(d, "MBUTTON", "MOUSEMIDDLE", "MOUSE MIDDLE", "鼠标中键").To(0x04);
            Add(d, "X1", "XBUTTON1", "MOUSEX1", "MOUSE X1", "鼠标X1", "鼠标侧键1").To(0x05);
            Add(d, "X2", "XBUTTON2", "MOUSEX2", "MOUSE X2", "鼠标X2", "鼠标侧键2").To(0x06);
            Add(d, "UP ARROW", "↑").To(0x26);
            Add(d, "DOWN ARROW", "↓").To(0x28);
            Add(d, "LEFT ARROW", "←").To(0x25);
            Add(d, "RIGHT ARROW", "→").To(0x27);
            Add(d, "OEM1", ";", "分号").To(0xBA);
            Add(d, "OEMPLUS", "=").To(0xBB);
            Add(d, "OEMCOMMA", ",").To(0xBC);
            Add(d, "OEMMINUS", "-").To(0xBD);
            Add(d, "OEMPERIOD", ".").To(0xBE);
            Add(d, "OEM2", "/", "斜杠").To(0xBF);
            Add(d, "OEM3", "`", "反引号").To(0xC0);
            Add(d, "OEM4", "[").To(0xDB);
            Add(d, "OEM5", "\\", "反斜杠").To(0xDC);
            Add(d, "OEM6", "]").To(0xDD);
            Add(d, "OEM7", "'", "引号").To(0xDE);
            Add(d, "MULTIPLY", "小键盘*").To(0x6A);
            Add(d, "ADD", "小键盘+").To(0x6B);
            Add(d, "SUBTRACT", "小键盘-").To(0x6D);
            Add(d, "DECIMAL", "小键盘.").To(0x6E);
            Add(d, "DIVIDE", "小键盘/").To(0x6F);
            for (int i = 1; i <= 24; i++) Add(d, "F" + i).To((uint)(0x6F + i));
            for (int i = 0; i <= 9; i++)
            {
                Add(d, "NUMPAD" + i, "小键盘" + i).To((uint)(0x60 + i));
                Add(d, i.ToString()).To((uint)(0x30 + i));
            }
            // 把显示名(如 "↑"、"鼠标X1"、"Num1")也纳入解析, 保证 ToString 输出可以完整反解析
            foreach (var kv in BuildVkToName())
            {
                if (!d.ContainsKey(kv.Value)) d[kv.Value] = kv.Key;
            }
            return d;
        }

        private static Dictionary<uint, string> BuildVkToName()
        {
            var d = new Dictionary<uint, string>();
            d[VK_CONTROL] = "Ctrl"; d[VK_SHIFT] = "Shift"; d[VK_MENU] = "Alt"; d[VK_LWIN] = "Win";
            d[0x1B] = "Esc"; d[0x20] = "Space"; d[0x0D] = "Enter"; d[0x09] = "Tab"; d[0x08] = "Backspace";
            d[0x14] = "CapsLock"; d[0x2D] = "Insert"; d[0x2E] = "Delete"; d[0x24] = "Home"; d[0x23] = "End";
            d[0x21] = "PageUp"; d[0x22] = "PageDown"; d[0x2C] = "PrintScreen"; d[0x91] = "ScrollLock";
            d[0x13] = "Pause"; d[0x90] = "NumLock";
            d[0x26] = "↑"; d[0x28] = "↓"; d[0x25] = "←"; d[0x27] = "→";
            d[0x01] = "鼠标左键"; d[0x02] = "鼠标右键"; d[0x04] = "鼠标中键"; d[0x05] = "鼠标X1"; d[0x06] = "鼠标X2";
            // 左右修饰键精确名(录制宏时按键码不归一化, 用精确名显示)
            d[0xA0] = "左Shift"; d[0xA1] = "右Shift"; d[0xA2] = "左Ctrl"; d[0xA3] = "右Ctrl";
            d[0xA4] = "左Alt"; d[0xA5] = "右Alt"; d[0x5B] = "左Win"; d[0x5C] = "右Win";
            // 常见扩展键, 尽量让兜底 "Key0xNN" 不出现
            d[0x5D] = "Apps"; d[0x03] = "Cancel"; d[0x0B] = "Clear";
            d[0x1C] = "Convert"; d[0x1D] = "NonConvert"; d[0x15] = "Kana"; d[0x19] = "Kanji";
            d[0x6A] = "小键盘*"; d[0x6B] = "小键盘+"; d[0x6D] = "小键盘-"; d[0x6E] = "小键盘."; d[0x6F] = "小键盘/";
            d[0x92] = "Fn"; d[0x3A] = "FakeLeftMouse"; d[0x3B] = "FakeRightMouse"; d[0x3C] = "FakeMiddleMouse";
            // 多媒体键(很多小键盘/自定义键盘会烧录这些键码)
            d[0xAD] = "静音"; d[0xAE] = "音量-"; d[0xAF] = "音量+";
            d[0xB0] = "下一首"; d[0xB1] = "上一首"; d[0xB2] = "停止播放"; d[0xB3] = "播放/暂停";
            d[0xB4] = "邮件"; d[0xB5] = "媒体选择"; d[0xB6] = "应用1"; d[0xB7] = "应用2";
            d[0xBA] = ";"; d[0xBB] = "="; d[0xBC] = ","; d[0xBD] = "-"; d[0xBE] = "."; d[0xBF] = "/";
            d[0xC0] = "`"; d[0xDB] = "["; d[0xDC] = "\\"; d[0xDD] = "]"; d[0xDE] = "'";
            d[0xDF] = "IME转换"; d[0xE2] = "OEM102"; d[0xE5] = "IME处理"; d[0xF6] = "Attn"; d[0xF7] = "CrSel"; d[0xF8] = "ExSel"; d[0xF9] = "EraseEOF"; d[0xFA] = "Play"; d[0xFB] = "Zoom";
            for (int i = 1; i <= 24; i++) d[(uint)(0x6F + i)] = "F" + i;
            for (int i = 0; i <= 9; i++) d[(uint)(0x60 + i)] = "Num" + i;
            for (char c = 'A'; c <= 'Z'; c++) d[(uint)c] = c.ToString();
            return d;
        }

        // 便捷链式写法: Add(d, "CTRL","CONTROL").To(vk)
        private class VkAdder
        {
            private readonly Dictionary<string, uint> _d;
            private readonly string[] _names;
            public VkAdder(Dictionary<string, uint> d, params string[] names) { _d = d; _names = names; }
            public void To(uint vk) { foreach (var n in _names) _d[n] = vk; }
        }

        private static VkAdder Add(Dictionary<string, uint> d, params string[] names)
        {
            return new VkAdder(d, names);
        }

        public static string GetName(uint vk)
        {
            // 鼠标键、方向键、左右修饰键与多媒体键按当前语言显示
            switch (vk)
            {
                case 0x01: return Lang.T("Mouse left");
                case 0x02: return Lang.T("Mouse right");
                case 0x04: return Lang.T("Mouse middle");
                case 0x05: return Lang.T("Mouse X1");
                case 0x06: return Lang.T("Mouse X2");
                case 0x26: return Lang.T("Up arrow");
                case 0x28: return Lang.T("Down arrow");
                case 0x25: return Lang.T("Left arrow");
                case 0x27: return Lang.T("Right arrow");
                case 0xA0: return Lang.T("Left Shift");
                case 0xA1: return Lang.T("Right Shift");
                case 0xA2: return Lang.T("Left Ctrl");
                case 0xA3: return Lang.T("Right Ctrl");
                case 0xA4: return Lang.T("Left Alt");
                case 0xA5: return Lang.T("Right Alt");
                case 0x5B: return Lang.T("Left Win");
                case 0x5C: return Lang.T("Right Win");
                case 0xAD: return Lang.T("Mute");
                case 0xAE: return Lang.T("Volume down");
                case 0xAF: return Lang.T("Volume up");
                case 0xB0: return Lang.T("Next track");
                case 0xB1: return Lang.T("Previous track");
                case 0xB2: return Lang.T("Stop media");
                case 0xB3: return Lang.T("Play/Pause");
            }
            string name;
            if (VkToName.TryGetValue(vk, out name)) return name;
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            return "Key0x" + vk.ToString("X2");
        }

        private static bool TryParseVkHex(string tok, out uint vk)
        {
            vk = 0;
            if (tok.Length != 6) return false; // VK0xNN
            if (tok[0] != 'V' && tok[0] != 'v') return false;
            if (tok[1] != 'K' && tok[1] != 'k') return false;
            return uint.TryParse(tok.Substring(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out vk);
        }

        // ---------- 解析 / 显示 ----------

        /// <summary>解析 "Ctrl+Shift+K" / "F6" / "Alt+Q" / "鼠标X1" 等字符串; 非法返回 null。</summary>
        public static Hotkey Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var hk = new Hotkey();
            foreach (var raw in text.Split('+'))
            {
                string tok = raw.Trim();
                if (tok.Length == 0) continue;
                uint vk;
                if (tok.Length == 1)
                {
                    char c = char.ToUpperInvariant(tok[0]);
                    if (char.IsLetterOrDigit(c)) vk = (uint)c;
                    else if (!NameToVk.TryGetValue(tok, out vk)) return null; // 单字符符号(如 ↑ ; =)
                }
                else if (!NameToVk.TryGetValue(tok, out vk) && !TryParseVkHex(tok, out vk))
                {
                    return null;
                }
                vk = Normalize(vk);
                if (IsModifier(vk))
                {
                    if (!hk.Modifiers.Contains(vk)) hk.Modifiers.Add(vk);
                }
                else
                {
                    if (!hk.Keys.Contains(vk)) hk.Keys.Add(vk);
                }
            }
            if (hk.Keys.Count == 0) return null; // 纯修饰键无效
            hk.Modifiers.Sort(CompareModifiers);
            hk.Keys.Sort();
            return hk;
        }

        private static int CompareModifiers(uint a, uint b)
        {
            return Rank(a).CompareTo(Rank(b));
        }

        private static int Rank(uint vk)
        {
            if (vk == VK_CONTROL) return 0;
            if (vk == VK_MENU) return 1;
            if (vk == VK_SHIFT) return 2;
            return 3; // Win
        }

        public override string ToString()
        {
            var parts = new List<string>();
            foreach (var m in Modifiers) parts.Add(GetName(m));
            foreach (var k in Keys) parts.Add(GetName(k));
            return string.Join("+", parts.ToArray());
        }

        public bool SameAs(Hotkey other)
        {
            if (other == null) return false;
            return SetEquals(Modifiers, other.Modifiers) && SetEquals(Keys, other.Keys);
        }

        private static bool SetEquals(List<uint> a, List<uint> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var v in a) if (!b.Contains(v)) return false;
            return true;
        }
    }

    /// <summary>
    /// 全局热键引擎: 用键盘+鼠标低级钩子监听按键状态, 支持任意键与多键组合。
    /// 只响应"真实"输入(忽略 LLKHF_INJECTED / LLMHF_INJECTED 注入标记),
    /// 因此程序自己注入的点击/按键不会误触发热键。
    /// </summary>
    internal class HotkeyManager : IDisposable
    {
        private const int LLKHF_INJECTED = 0x10;
        private const int LLMHF_INJECTED = 0x01;

        private readonly Dictionary<HotkeyAction, Hotkey> _bindings = new Dictionary<HotkeyAction, Hotkey>();
        private readonly Dictionary<HotkeyAction, Action> _callbacks = new Dictionary<HotkeyAction, Action>();
        private readonly HashSet<uint> _pressed = new HashSet<uint>();
        private readonly HashSet<HotkeyAction> _fired = new HashSet<HotkeyAction>();

        private readonly NativeMethods.LowLevelProc _kbProc;
        private readonly NativeMethods.LowLevelProc _mouseProc;
        private IntPtr _kbHook;
        private IntPtr _mouseHook;
        private bool _disposed;

        /// <summary>回放期间置 true, 抑制热键触发(驱动级注入无 INJECTED 标记, 会自触发); F8/F12 例外放行。</summary>
        public volatile bool Suppress = false;

        /// <summary>捕获新热键/新按键期间置 true, 暂停全部触发(含 F8/F12——捕获框里按这些键不能误触发功能)。</summary>
        public volatile bool CaptureActive = false;

        public HotkeyManager()
        {
            _kbProc = KeyboardProc;
            _mouseProc = MouseProc;
        }

        public static Hotkey Default(HotkeyAction action)
        {
            switch (action)
            {
                case HotkeyAction.Clicker: return Hotkey.Parse("F6");
                case HotkeyAction.Record: return Hotkey.Parse("F7");
                case HotkeyAction.Play: return Hotkey.Parse("F8");
                case HotkeyAction.Keyboard: return Hotkey.Parse("F9");
                default: return Hotkey.Parse("F12");
            }
        }

        public void SetBinding(HotkeyAction action, Hotkey hk)
        {
            if (hk != null) _bindings[action] = hk;
        }

        public Hotkey GetBinding(HotkeyAction action)
        {
            Hotkey hk;
            return _bindings.TryGetValue(action, out hk) ? hk : null;
        }

        public void SetCallback(HotkeyAction action, Action callback)
        {
            _callbacks[action] = callback;
        }

        public string Describe(HotkeyAction action)
        {
            var hk = GetBinding(action);
            return hk == null ? Lang.T("(unset)") : hk.ToString();
        }

        /// <summary>检查新热键与其它功能是否冲突, 返回冲突的功能。</summary>
        public bool FindConflict(Hotkey hk, HotkeyAction self, out HotkeyAction conflict)
        {
            foreach (var kv in _bindings)
            {
                if (kv.Key != self && kv.Value != null && hk.SameAs(kv.Value))
                {
                    conflict = kv.Key;
                    return true;
                }
            }
            conflict = HotkeyAction.Clicker;
            return false;
        }

        /// <summary>该按键是否是某个热键的触发键(录制宏时跳过, 避免回放误触开关)。</summary>
        public bool IsHotkeyKey(uint vk)
        {
            uint n = Hotkey.Normalize(vk);
            foreach (var hk in _bindings.Values)
            {
                if (hk != null && hk.Keys.Contains(n)) return true;
            }
            return false;
        }

        public void Start()
        {
            if (_kbHook != IntPtr.Zero || _disposed) return;
            _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc,
                NativeMethods.GetModuleHandle(null), 0);
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc,
                NativeMethods.GetModuleHandle(null), 0);
            if (_kbHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
            {
                // 部分失败: 回滚已装钩子并清零, 允许下次重试(否则鼠标热键永久失效且无提示)
                Log.Warn("热键钩子安装失败(kb=" + (_kbHook != IntPtr.Zero) + ", mouse=" + (_mouseHook != IntPtr.Zero) + ")");
                if (_kbHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
                if (_mouseHook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            }
        }

        private void Fire(HotkeyAction action)
        {
            Action cb;
            if (_callbacks.TryGetValue(action, out cb) && cb != null) cb();
        }

        private bool Satisfied(Hotkey hk)
        {
            foreach (var k in hk.Keys) if (!_pressed.Contains(k)) return false;
            foreach (var m in hk.Modifiers) if (!_pressed.Contains(m)) return false;
            return true;
        }

        /// <summary>completedVk 刚按下; 若它补全了某个热键组合则触发(按住期间只触发一次)。</summary>
        private void TryFire(uint completedVk)
        {
            foreach (var kv in _bindings)
            {
                var hk = kv.Value;
                if (hk == null || _fired.Contains(kv.Key)) continue;
                if (!hk.Keys.Contains(completedVk)) continue;
                if (Satisfied(hk))
                {
                    _fired.Add(kv.Key);
                    // 捕获期间: 全部暂停(按 F8/F12 也不能触发功能)
                    if (CaptureActive) continue;
                    // 回放期间抑制(驱动级注入无 INJECTED 标记, 会自触发)。
                    // 但「回放开关」(F8) 与「全部停止」(F12) 必须始终放行:
                    //   1) 一并抑制的话, 驱动模式下开始回放后就再也停不下来(用户按物理键同样被吞掉);
                    //   2) 录制端 MacroRecorder.IsHotkeyKey 已把已绑定的热键键排除在宏之外,
                    //      放行它们基本不会被宏里的按键自触发。
                    if (Suppress && kv.Key != HotkeyAction.StopAll && kv.Key != HotkeyAction.Play) continue;
                    Fire(kv.Key);
                }
            }
        }

        private void OnRelease(uint vk)
        {
            _pressed.Remove(vk);
            // 松开组合中的任一键后, 该组合可以再次触发
            var broken = new List<HotkeyAction>();
            foreach (var a in _fired)
            {
                var hk = GetBinding(a);
                if (hk == null || hk.Keys.Contains(vk)) broken.Add(a);
            }
            foreach (var a in broken) _fired.Remove(a);
        }

        private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // 托管异常穿越原生回调边界会终止进程; 钩子回调必须整体兜底(且始终 CallNextHookEx, 否则会吞掉用户输入)
            try
            {
                if (nCode >= 0)
                {
                    var info = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    bool up = (int)wParam == NativeMethods.WM_KEYUP || (int)wParam == NativeMethods.WM_SYSKEYUP;
                    if (up)
                    {
                        OnRelease(Hotkey.Normalize(info.vkCode));
                    }
                    else if ((info.flags & LLKHF_INJECTED) == 0) // 忽略注入按键, 防止自触发
                    {
                        uint n = Hotkey.Normalize(info.vkCode);
                        if (!_pressed.Contains(n))
                        {
                            _pressed.Add(n);
                            TryFire(n);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { Log.Warn("热键键盘钩子异常: " + ex.Message); } catch (Exception) { }
            }
            return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    var info = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                    uint vk = 0;
                    bool up = false;
                    switch ((int)wParam)
                    {
                        case NativeMethods.WM_LBUTTONDOWN: vk = 0x01; break;
                        case NativeMethods.WM_LBUTTONUP: vk = 0x01; up = true; break;
                        case NativeMethods.WM_RBUTTONDOWN: vk = 0x02; break;
                        case NativeMethods.WM_RBUTTONUP: vk = 0x02; up = true; break;
                        case NativeMethods.WM_MBUTTONDOWN: vk = 0x04; break;
                        case NativeMethods.WM_MBUTTONUP: vk = 0x04; up = true; break;
                        case NativeMethods.WM_XBUTTONDOWN: vk = ((info.mouseData >> 16) & 0xFFFF) == 1 ? 0x05u : 0x06u; break;
                        case NativeMethods.WM_XBUTTONUP: vk = ((info.mouseData >> 16) & 0xFFFF) == 1 ? 0x05u : 0x06u; up = true; break;
                    }
                    if (vk != 0)
                    {
                        if (up)
                        {
                            OnRelease(vk);
                        }
                        else if ((info.flags & LLMHF_INJECTED) == 0)
                        {
                            if (!_pressed.Contains(vk))
                            {
                                _pressed.Add(vk);
                                TryFire(vk);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { Log.Warn("热键鼠标钩子异常: " + ex.Message); } catch (Exception) { }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_kbHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_kbHook);
            if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _kbHook = IntPtr.Zero;
            _mouseHook = IntPtr.Zero;
        }
    }
}
