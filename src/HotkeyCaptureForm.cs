using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClickerTool
{
    /// <summary>
    /// 热键捕获对话框: 用键盘+鼠标低级钩子捕获(而非 KeyPreview, 这样多媒体键
    /// 如音量+/播放暂停、以及鼠标侧键 X1/X2 也能捕获)。按下任意组合键,
    /// 全部松开后完成捕获, Esc 取消。捕获到的热键通过 Captured 属性返回。
    /// 可选 hint 参数自定义提示文字(音效绑定等复用本对话框)。
    /// </summary>
    internal class HotkeyCaptureForm : Form
    {
        private readonly List<uint> _modifiers = new List<uint>();
        private readonly List<uint> _keys = new List<uint>();
        private readonly HashSet<uint> _held = new HashSet<uint>(); // 当前按住的原始键码
        private readonly Label _lbl;
        private readonly NativeMethods.LowLevelProc _kbProc;
        private readonly NativeMethods.LowLevelProc _mouseProc;
        private IntPtr _kbHook;
        private IntPtr _mouseHook;

        public Hotkey Captured { get; private set; }

        public HotkeyCaptureForm() : this(null) { }

        public HotkeyCaptureForm(string hint)
        {
            Text = Lang.T("Set hotkey");
            AutoScaleMode = AutoScaleMode.None; // 手工布局, 禁用自动缩放
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(Dpi.X(420), Dpi.X(150));
            StartPosition = FormStartPosition.CenterParent;
            TopMost = true;
            BackColor = Clay.WindowBg;
            Font = new Font("Microsoft YaHei UI", 9F);
            _kbProc = KeyboardProc;
            _mouseProc = MouseProc;

            var card = new ClayPanel { Location = new Point(Dpi.X(10), Dpi.X(10)), Size = new Size(Dpi.X(400), Dpi.X(130)), BackColor = Clay.CardBg };
            _lbl = new Label
            {
                Text = hint != null ? hint : Lang.T("Press the new hotkey combo...\r\nSupports Ctrl/Alt/Shift/Win + any key, multi-key combos, mouse side buttons\r\nRelease all keys to finish, Esc to cancel"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Clay.Ink,
                BackColor = Clay.CardBg
            };
            card.Controls.Add(_lbl);
            Controls.Add(card);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Clay.ApplyFrameTheme(Handle); // 弹窗边框跟随主题
            // 模态循环期间 UI 线程持续泵消息, 低级钩子可正常工作
            _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc, NativeMethods.GetModuleHandle(null), 0);
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, NativeMethods.GetModuleHandle(null), 0);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_kbHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_kbHook);
            if (_mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _kbHook = IntPtr.Zero;
            _mouseHook = IntPtr.Zero;
            base.OnFormClosed(e);
        }

        private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                if ((info.flags & 0x10) != 0) // 忽略注入按键(引擎/回放产生的输入不能混进捕获)
                    return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
                uint raw = info.vkCode;
                bool up = (int)wParam == NativeMethods.WM_KEYUP || (int)wParam == NativeMethods.WM_SYSKEYUP;
                if (up)
                {
                    _held.Remove(raw);
                    TryFinish();
                }
                else
                {
                    if (raw == 0x1B) // Esc 取消
                    {
                        DialogResult = DialogResult.Cancel;
                        Close();
                        return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
                    }
                    if (_held.Add(raw)) // 按住自动重复的消息忽略
                    {
                        uint n = Hotkey.Normalize(raw);
                        if (Hotkey.IsModifier(n))
                        {
                            if (!_modifiers.Contains(n)) _modifiers.Add(n);
                        }
                        else
                        {
                            if (!_keys.Contains(n)) _keys.Add(n);
                        }
                        UpdateHint();
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                if ((info.flags & 0x01) != 0) // 忽略注入点击
                    return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
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
                    // 点击落在本对话框内时忽略: 那只是操作对话框本身, 不是要绑定鼠标键
                    if (Bounds.Contains(info.pt.x, info.pt.y))
                        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                    if (up)
                    {
                        _held.Remove(vk);
                        TryFinish();
                    }
                    else if (_held.Add(vk))
                    {
                        if (!_keys.Contains(vk)) _keys.Add(vk);
                        UpdateHint();
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void TryFinish()
        {
            if (_held.Count > 0) return;
            if (_keys.Count == 0)
            {
                _lbl.Text = Lang.T("No key detected, try again... (Esc to cancel)");
                return;
            }
            Captured = new Hotkey();
            Captured.Modifiers.AddRange(_modifiers);
            Captured.Keys.AddRange(_keys);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void UpdateHint()
        {
            var hk = new Hotkey();
            hk.Modifiers.AddRange(_modifiers);
            hk.Keys.AddRange(_keys);
            _lbl.Text = Lang.F("Current combo: {0}\r\nKeep pressing other keys to extend the combo; release all to finish\r\nPress Esc to cancel", hk);
        }
    }
}
