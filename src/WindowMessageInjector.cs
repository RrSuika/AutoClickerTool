using System;
using System.Text;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>
    /// 窗口消息注入(PostMessage), 作为 SendInput 之外的第三条通道。
    ///
    /// 与 SendInput 的差别(实测于 Widget Idle Demo 的 b1/Servo 引擎, 2026-11-15):
    ///   * 完全不移动物理光标、不改变前台窗口 —— 目标窗口即便被遮挡/失焦也照样收到;
    ///   * 事件不带 LLKHF_INJECTED / LLMHF_INJECTED 标记, 因为它根本不走系统输入队列;
    ///   * 坐标按目标窗口的客户区投递, 窗口移动后重新换算, 不会像绝对屏幕坐标那样整体打偏。
    /// 实测结论: 该通道的点击被游戏计为"玩家输入"(nut_cutter 的 lifetimeCutterClicks/切盘数同步增长)。
    ///
    /// 局限(同样是实测):
    ///   * 只有走窗口过程(WndProc)的输入才有效。用 Raw Input / GetAsyncKeyState 轮询的
    ///     真·游戏不认这条通道(例如大量 DirectInput 游戏);
    ///   * 文本输入若依赖 IME 组合(WM_IME_*), 只补 WM_CHAR 仍可能打不进输入框。
    /// </summary>
    internal sealed class WindowMessageInjector
    {
        private const int MK_LBUTTON = 0x0001;
        private const int MK_RBUTTON = 0x0002;
        private const int MK_SHIFT = 0x0004;
        private const int MK_CONTROL = 0x0008;
        private const int MK_MBUTTON = 0x0010;

        /// <summary>串行化: 多看板引擎线程并发时会话交错, 导致按下/抬起配错对。</summary>
        private static readonly object Lock = new object();

        /// <summary>自检模式打开: 每次按下都把实际投递的客户区坐标写进日志(--wmtest 用)。</summary>
        public static bool DebugLog;

        private string _title = "";
        private IntPtr _hwnd = IntPtr.Zero;

        // 已注入的修饰键状态(窗口消息模式下系统键盘状态不会跟着变, 必须自己记)
        private bool _altDown;
        private bool _ctrlDown;
        private bool _shiftDown;
        private bool _winDown;

        // 鼠标按下状态, 用于给鼠标消息带正确的 MK_* 掩码
        private int _buttons;

        // 最近一次 MoveTo 的目标(客户区坐标)
        private int _clientX;
        private int _clientY;
        private bool _hasPending;

        // 最近一次真正投递出去的 hover 位置; 位置没变就不再重发(见 PostMove)
        private int _lastPostedX = int.MinValue;
        private int _lastPostedY = int.MinValue;

        /// <summary>绑定了标题却找不到窗口时只警告一次, 避免每帧刷日志。</summary>
        private bool _warnedNotFound;

        /// <summary>设置目标窗口标题; 空串 = 跟随前台窗口。会清掉已缓存的句柄。</summary>
        public void SetTarget(string title)
        {
            lock (Lock)
            {
                _title = title ?? "";
                _hwnd = IntPtr.Zero;
                _hasPending = false;
                _lastPostedX = int.MinValue;
                _lastPostedY = int.MinValue;
                _warnedNotFound = false;
            }
        }

        /// <summary>当前缓存的窗口句柄(未解析时为 0)。</summary>
        public IntPtr CachedHandle
        {
            get { lock (Lock) { return _hwnd; } }
        }

        /// <summary>
        /// 解析目标窗口: 先按标题精确匹配, 再按"标题包含"兜底(游戏窗口标题常带状态后缀),
        /// 都找不到就用前台窗口。句柄会缓存, 失效时自动重新解析。
        /// </summary>
        public IntPtr Resolve()
        {
            lock (Lock)
            {
                if (_hwnd != IntPtr.Zero && NativeMethods.IsWindow(_hwnd)) return _hwnd;

                string title = _title;
                if (!string.IsNullOrEmpty(title))
                {
                    IntPtr h = NativeMethods.FindWindow(null, title);
                    if (h == IntPtr.Zero)
                    {
                        // 兜底: 标题包含匹配(取第一个可见的顶层窗口)
                        string needle = title.ToLowerInvariant();
                        NativeMethods.EnumWindows(delegate(IntPtr w, IntPtr p)
                        {
                            if (!NativeMethods.IsWindowVisible(w)) return true;
                            var sb = new StringBuilder(512);
                            NativeMethods.GetWindowText(w, sb, 512);
                            string t = sb.ToString();
                            if (t.Length > 0 && t.ToLowerInvariant().IndexOf(needle, StringComparison.Ordinal) >= 0)
                            {
                                h = w;
                                return false;
                            }
                            return true;
                        }, IntPtr.Zero);
                    }

                    if (h != IntPtr.Zero)
                    {
                        _hwnd = h;
                        _warnedNotFound = false;
                        return _hwnd;
                    }

                    // 绑定了标题却找不到窗口: 绝不能退化成"点前台窗口" ——
                    // 那会把点击投到用户正在用的别的程序上(例如浏览器), 后果比不点更糟
                    if (!_warnedNotFound)
                    {
                        _warnedNotFound = true;
                        Log.Warn("窗口消息注入: 找不到标题含 \"" + title + "\" 的窗口, 已停止投递(不会退化为前台窗口)");
                    }
                    return IntPtr.Zero;
                }

                _warnedNotFound = false;
                _hwnd = NativeMethods.GetForegroundWindow();
                return _hwnd;
            }
        }

        /// <summary>屏幕坐标 → 目标窗口客户区坐标。</summary>
        private bool ToClient(IntPtr hwnd, int screenX, int screenY, out int cx, out int cy)
        {
            var p = new NativeMethods.POINT();
            p.x = screenX;
            p.y = screenY;
            if (!NativeMethods.ScreenToClient(hwnd, ref p))
            {
                cx = cy = 0;
                return false;
            }
            cx = p.x;
            cy = p.y;
            return true;
        }

        /// <summary>记录一次移动目标(客户区坐标), 不产生任何消息。</summary>
        public void SetPosition(int screenX, int screenY)
        {
            lock (Lock)
            {
                IntPtr h = Resolve();
                if (h == IntPtr.Zero) return;
                int cx, cy;
                if (!ToClient(h, screenX, screenY, out cx, out cy)) return;
                _clientX = cx;
                _clientY = cy;
                _hasPending = true;
            }
        }

        /// <summary>没有显式 MoveTo 时(跟随光标模式), 用物理光标当前位置换算客户区坐标。</summary>
        private void EnsurePending(IntPtr hwnd)
        {
            if (_hasPending) return;
            NativeMethods.POINT p;
            if (NativeMethods.GetCursorPos(out p) && ToClient(hwnd, p.x, p.y, out _clientX, out _clientY))
            {
                _hasPending = true;
            }
        }

        private static IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
        }

        private int KeyStateMask()
        {
            int mask = _buttons;
            if (_shiftDown) mask |= MK_SHIFT;
            if (_ctrlDown) mask |= MK_CONTROL;
            return mask;
        }

        /// <summary>
        /// 先发 WM_MOUSEMOVE, 让目标窗口更新 hover/指针状态; 很多自绘 UI 依赖它做命中测试。
        /// 必须与随后的按下消息之间留一点时间(实测 40ms 起效): 中间没有间隔时,
        /// 目标引擎往往还在用上一帧的 hover 状态做 hit-test, 点击会被判成"点在空白处"。
        /// 位置没变时不重复发送 —— 否则默认 50ms 的连点间隔里每条都白等 40ms, 实际速率腰斩。
        /// </summary>
        private void PostMove(IntPtr hwnd)
        {
            if (_lastPostedX == _clientX && _lastPostedY == _clientY) return;
            NativeMethods.PostMessage(hwnd, (uint)NativeMethods.WM_MOUSEMOVE, (IntPtr)KeyStateMask(),
                                      MakeLParam(_clientX, _clientY));
            Thread.Sleep(40);
            _lastPostedX = _clientX;
            _lastPostedY = _clientY;
        }

        public void MouseDown(int button)
        {
            lock (Lock)
            {
                IntPtr h = Resolve();
                if (h == IntPtr.Zero) return;
                EnsurePending(h);
                PostMove(h);

                int downMsg;
                int flag;
                switch (button)
                {
                    case 1: downMsg = NativeMethods.WM_RBUTTONDOWN; flag = MK_RBUTTON; break;
                    case 2: downMsg = NativeMethods.WM_MBUTTONDOWN; flag = MK_MBUTTON; break;
                    default: downMsg = NativeMethods.WM_LBUTTONDOWN; flag = MK_LBUTTON; break;
                }
                _buttons |= flag;
                bool okDown = NativeMethods.PostMessage(h, (uint)downMsg, (IntPtr)KeyStateMask(), MakeLParam(_clientX, _clientY));
                if (DebugLog)
                {
                    Log.Info("WM down hwnd=0x" + h.ToInt64().ToString("X") + " client=(" + _clientX + "," + _clientY
                             + ") pending=" + _hasPending + " ok=" + okDown);
                }
            }
        }

        public void MouseUp(int button)
        {
            lock (Lock)
            {
                IntPtr h = Resolve();
                if (h == IntPtr.Zero) return;
                EnsurePending(h);

                int upMsg;
                int flag;
                switch (button)
                {
                    case 1: upMsg = NativeMethods.WM_RBUTTONUP; flag = MK_RBUTTON; break;
                    case 2: upMsg = NativeMethods.WM_MBUTTONUP; flag = MK_MBUTTON; break;
                    default: upMsg = NativeMethods.WM_LBUTTONUP; flag = MK_LBUTTON; break;
                }
                _buttons &= ~flag;
                NativeMethods.PostMessage(h, (uint)upMsg, (IntPtr)KeyStateMask(), MakeLParam(_clientX, _clientY));
            }
        }

        /// <summary>滚轮: lParam 必须是屏幕坐标, 不是客户区坐标(微软文档的坑)。</summary>
        public void Wheel(int delta)
        {
            lock (Lock)
            {
                IntPtr h = Resolve();
                if (h == IntPtr.Zero) return;
                EnsurePending(h);
                var p = new NativeMethods.POINT();
                p.x = 0;
                p.y = 0;
                if (!NativeMethods.ClientToScreen(h, ref p)) return;
                // 客户区原点 + 目标点
                p.x += _clientX;
                p.y += _clientY;
                int wParam = ((delta & 0xFFFF) << 16) | KeyStateMask();
                NativeMethods.PostMessage(h, (uint)NativeMethods.WM_MOUSEWHEEL, (IntPtr)wParam, MakeLParam(p.x, p.y));
            }
        }

        /// <summary>已按下的键(用于给 lParam 的"此前状态"位填正确值, 也便于异常退出时补发抬起)。</summary>
        private readonly System.Collections.Generic.HashSet<int> _pressed = new System.Collections.Generic.HashSet<int>();

        /// <summary>按下/抬起一个键。lParam 必须带扫描码, 否则目标窗口无法从 VK 反推物理键(很多游戏只认扫描码)。</summary>
        public void Key(int vk, bool up)
        {
            lock (Lock)
            {
                IntPtr h = Resolve();
                if (h == IntPtr.Zero) return;

                bool prevDown = _pressed.Contains(vk);
                if (up) _pressed.Remove(vk); else _pressed.Add(vk);

                UpdateModifier(vk, up);

                bool extended = InputSimulator.IsExtendedKey(vk);
                uint scan = NativeMethods.MapVirtualKey((uint)vk, 0);
                // 部分无扫描码映射的键(如多媒体键)退回 VK, 好过丢失
                if (scan == 0) scan = (uint)vk;

                long lp = 1;                                  // repeat count = 1
                lp |= ((long)scan & 0xFF) << 16;              // 扫描码
                if (extended) lp |= 1L << 24;                 // 扩展键(0xE0 前缀)
                if (prevDown) lp |= 1L << 30;                 // 此前已按下 → 自重复
                if (up) lp |= 1L << 31;                       // 抬起

                // Alt 参与的组合必须走 SYSKEY, 否则应用收不到 Alt 组合
                bool sys = _altDown || vk == 0x12;
                uint msg = (uint)(up
                    ? (sys ? NativeMethods.WM_SYSKEYUP : NativeMethods.WM_KEYUP)
                    : (sys ? NativeMethods.WM_SYSKEYDOWN : NativeMethods.WM_KEYDOWN));

                NativeMethods.PostMessage(h, msg, (IntPtr)vk, (IntPtr)lp);

                // 补一个字符消息, 让文本框/搜索框能真正收到文本
                if (!up && !_ctrlDown && !_winDown && !_altDown)
                {
                    char ch;
                    if (TryTranslate(vk, out ch))
                    {
                        NativeMethods.PostMessage(h, (uint)NativeMethods.WM_CHAR, (IntPtr)ch, (IntPtr)1);
                    }
                }
            }
        }

        /// <summary>把仍处于按下状态的键全部补发抬起(引擎停止/退出时调用, 防止目标侧卡键)。</summary>
        public void ReleaseAll()
        {
            lock (Lock)
            {
                int[] pending = new int[_pressed.Count];
                _pressed.CopyTo(pending);
                // 先解的放最后: 修饰键最后抬起
                Array.Sort(pending, delegate(int a, int b)
                {
                    bool ma = Hotkey.IsModifier((uint)a);
                    bool mb = Hotkey.IsModifier((uint)b);
                    if (ma == mb) return 0;
                    return ma ? 1 : -1;
                });
                foreach (int vk in pending)
                {
                    try { Key(vk, true); } catch (Exception) { }
                }
            }
        }

        private void UpdateModifier(int vk, bool up)
        {
            switch (vk)
            {
                case 0x12: _altDown = !up; break;      // Alt
                case 0x11: _ctrlDown = !up; break;     // Ctrl
                case 0x10: _shiftDown = !up; break;    // Shift
                case 0x5B:
                case 0x5C: _winDown = !up; break;      // Win
            }
        }

        /// <summary>VK + 当前修饰键状态 → 字符。优先 ToUnicode(跟随键盘布局), 失败则退回常见键表。</summary>
        private bool TryTranslate(int vk, out char ch)
        {
            ch = '\0';

            byte[] state = new byte[256];
            try { NativeMethods.GetKeyboardState(state); }
            catch (Exception) { }

            state[0x10] = (byte)(_shiftDown ? 0x80 : 0x00);
            state[0x11] = (byte)(_ctrlDown ? 0x80 : 0x00);
            state[0x12] = (byte)(_altDown ? 0x80 : 0x00);
            try { state[0x14] = (byte)(Console.CapsLock ? 0x01 : 0x00); }
            catch (Exception) { }

            uint scan = NativeMethods.MapVirtualKey((uint)vk, 0);
            try
            {
                var sb = new StringBuilder(8);
                int n = NativeMethods.ToUnicode((uint)vk, scan, state, sb, sb.Capacity, 0);
                if (n == 1 && sb.Length > 0)
                {
                    ch = sb[0];
                    return true;
                }
            }
            catch (Exception)
            {
                // ToUnicode 在多线程/无布局环境下可能抛异常, 走下面的兜底
            }

            // 兜底: 字母/数字/空格(不依赖键盘布局)
            if (vk >= 0x41 && vk <= 0x5A)
            {
                char c = (char)vk;
                bool shift = _shiftDown;
                bool caps = false;
                try { caps = Console.CapsLock; }
                catch (Exception) { }
                if (shift ^ caps) c = char.ToLowerInvariant(c);
                ch = c;
                return true;
            }
            if (vk >= 0x30 && vk <= 0x39)
            {
                ch = _shiftDown ? ")!@#$%^&*("[vk - 0x30] : (char)vk;
                return true;
            }
            if (vk == 0x20)
            {
                ch = ' ';
                return true;
            }
            return false;
        }

        /// <summary>
        /// 把目标窗口切到前台。窗口消息模式并不需要焦点, 但 SendInput 模式需要 ——
        /// 而且游戏类程序普遍在失焦时降帧/暂停, 所以开跑前主动拉一次前台更稳。
        /// 普通 SetForegroundWindow 会被前台锁拒绝, 这里用 AttachThreadInput 提升成功率。
        /// </summary>
        public static bool EnsureForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd)) return false;
            if (NativeMethods.GetForegroundWindow() == hwnd) return true;
            try
            {
                if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

                IntPtr fg = NativeMethods.GetForegroundWindow();
                uint pid;
                uint fgTid = NativeMethods.GetWindowThreadProcessId(fg, out pid);
                uint myTid = NativeMethods.GetCurrentThreadId();

                bool attached = false;
                if (fgTid != 0 && fgTid != myTid)
                {
                    attached = NativeMethods.AttachThreadInput(myTid, fgTid, true);
                }
                try
                {
                    NativeMethods.BringWindowToTop(hwnd);
                    NativeMethods.SetForegroundWindow(hwnd);
                }
                finally
                {
                    if (attached) NativeMethods.AttachThreadInput(myTid, fgTid, false);
                }
                Thread.Sleep(120);
                return NativeMethods.GetForegroundWindow() == hwnd;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>目标窗口是否还在前台(仅诊断用)。</summary>
        public bool IsTargetForeground()
        {
            IntPtr h = Resolve();
            return h != IntPtr.Zero && NativeMethods.GetForegroundWindow() == h;
        }
    }
}
