using System;
using System.Threading;

namespace AutoClickerTool
{
    internal enum MouseButton
    {
        Left = 0,
        Right = 1,
        Middle = 2
    }

    /// <summary>
    /// 注入方式:
    ///   SendInput          标准系统注入, 兼容性最好, 但带"注入"标记, 部分游戏会识别
    ///   SendMessage        直接向目标窗口投递消息, 不经过系统输入队列, 绕过注入标记检测;
    ///                     对使用原始输入(Raw Input)或轮询 GetAsyncKeyState 的游戏可能无效
    ///   InterceptionDriver 驱动级注入, 从设备驱动层注入, 与真实硬件输入无异, 最彻底(需安装驱动)
    /// </summary>
    internal enum InjectionMethod
    {
        SendInput,
        SendMessage,
        InterceptionDriver
    }

    /// <summary>输入注入中枢: 鼠标/键盘事件按当前注入方式路由, 三种方式共用同一套调用入口。</summary>
    internal static class InputSimulator
    {
        public static volatile InjectionMethod Method = InjectionMethod.SendInput;

        /// <summary>SendMessage 模式的目标窗口标题; 为空时取前台窗口。</summary>
        public static volatile string TargetWindowTitle = "";

        /// <summary>SendInput 模式键盘事件使用扫描码注入(部分游戏校验扫描码)。</summary>
        public static volatile bool KeyboardScanCode = false;

        private static InterceptionDriver _driver;
        private static readonly object DriverLock = new object();
        private static int _lastTargetX;
        private static int _lastTargetY;
        private static bool _hasLastTarget;

        /// <summary>SendMessage 模式共享状态(_lastTarget*)的锁: 多引擎线程并发注入时串行化移动+点击。</summary>
        private static readonly object SendLock = new object();

        // ---------- 鼠标 ----------

        public static void MouseDown(MouseButton button)
        {
            switch (Method)
            {
                case InjectionMethod.SendInput:
                    SendInputMouse(GetDownFlag(button));
                    break;
                case InjectionMethod.SendMessage:
                    PostMouse(GetDownWm(button), GetMsgPos());
                    break;
                case InjectionMethod.InterceptionDriver:
                    GetDriver().MouseDown((int)button);
                    break;
            }
        }

        public static void MouseUp(MouseButton button)
        {
            switch (Method)
            {
                case InjectionMethod.SendInput:
                    SendInputMouse(GetUpFlag(button));
                    break;
                case InjectionMethod.SendMessage:
                    PostMouse(GetUpWm(button), GetMsgPos());
                    break;
                case InjectionMethod.InterceptionDriver:
                    GetDriver().MouseUp((int)button);
                    break;
            }
        }

        /// <summary>在当前位置点击一次, 按下与抬起之间有随机人化时长与点击微拖(见 Humanizer)。</summary>
        public static void Click(MouseButton button)
        {
            MouseDown(button);
            // 点击微拖: 按下与抬起之间 1~2 像素微移, 模拟真人点击的微小滑动
            int mdx, mdy;
            Humanizer.MicroDrag(out mdx, out mdy);
            if (mdx != 0 || mdy != 0)
            {
                NativeMethods.POINT p;
                if (NativeMethods.GetCursorPos(out p)) MoveTo(p.x + mdx, p.y + mdy);
            }
            Thread.Sleep(Humanizer.NextPressDuration());
            MouseUp(button);
            _hasLastTarget = false;
        }

        public static void ClickAt(int x, int y, MouseButton button)
        {
            if (Method == InjectionMethod.SendMessage)
            {
                // SendMessage 移动+点击共享 _lastTarget 状态, 多引擎并发时锁住整段避免坐标错乱
                lock (SendLock)
                {
                    MoveTo(x, y);
                    Click(button);
                }
            }
            else
            {
                MoveTo(x, y);
                Click(button);
            }
        }

        public static void Wheel(int delta)
        {
            switch (Method)
            {
                case InjectionMethod.SendInput:
                    var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
                    input.U.mi.dwFlags = NativeMethods.MOUSEEVENTF_WHEEL;
                    input.U.mi.mouseData = unchecked((uint)(delta << 16));
                    NativeMethods.SendInput(1, new[] { input }, NativeMethods.INPUT.Size);
                    break;
                case InjectionMethod.SendMessage:
                    IntPtr h = ResolveTarget();
                    if (h != IntPtr.Zero)
                        NativeMethods.PostMessage(h, NativeMethods.WM_MOUSEWHEEL, (IntPtr)(delta << 16), GetMsgPos());
                    break;
                case InjectionMethod.InterceptionDriver:
                    GetDriver().Wheel(delta);
                    break;
            }
        }

        /// <summary>把鼠标移动到屏幕坐标 (x, y)。开启轨迹仿真时沿贝塞尔曲线平滑移动(时长按距离自适应)。</summary>
        public static void MoveTo(int x, int y)
        {
            if (Humanizer.Enabled && Humanizer.TrajectoryEnabled)
            {
                NativeMethods.POINT cur;
                if (NativeMethods.GetCursorPos(out cur))
                {
                    double d = Math.Sqrt((double)(x - cur.x) * (x - cur.x) + (y - cur.y) * (y - cur.y));
                    MoveToWithTrajectory(x, y, Humanizer.EstimateTrajectoryMs(d));
                    return;
                }
            }
            MoveStep(x, y);
        }

        /// <summary>以指定总时长(毫秒)沿拟人轨迹移动到 (x, y); 预算过小或关闭仿真时一步到位。</summary>
        public static void MoveToWithTrajectory(int x, int y, int budgetMs)
        {
            NativeMethods.POINT cur;
            if (!NativeMethods.GetCursorPos(out cur))
            {
                MoveStep(x, y);
                return;
            }
            Humanizer.Trajectory(cur.x, cur.y, x, y, budgetMs, MoveStep);
        }

        /// <summary>单步移动, 按当前注入方式路由。</summary>
        private static void MoveStep(int x, int y)
        {
            switch (Method)
            {
                case InjectionMethod.SendInput:
                    SendInputMove(x, y);
                    break;
                case InjectionMethod.SendMessage:
                    PostMove(x, y);
                    break;
                case InjectionMethod.InterceptionDriver:
                    GetDriver().MoveAbsolute(x, y);
                    break;
            }
        }

        // ---------- 键盘 ----------

        public static void KeyDown(int vk, bool extended = false)
        {
            switch (Method)
            {
                case InjectionMethod.SendInput:
                    SendInputKey((ushort)vk, false, extended);
                    break;
                case InjectionMethod.SendMessage:
                    PostKey((uint)vk, false, extended);
                    break;
                case InjectionMethod.InterceptionDriver:
                    GetDriver().Key(vk, false);
                    break;
            }
        }

        public static void KeyUp(int vk, bool extended = false)
        {
            switch (Method)
            {
                case InjectionMethod.SendInput:
                    SendInputKey((ushort)vk, true, extended);
                    break;
                case InjectionMethod.SendMessage:
                    PostKey((uint)vk, true, extended);
                    break;
                case InjectionMethod.InterceptionDriver:
                    GetDriver().Key(vk, true);
                    break;
            }
        }

        public static void KeyTap(int vk, bool extended = false)
        {
            KeyDown(vk, extended);
            Thread.Sleep(Humanizer.NextPressDuration());
            KeyUp(vk, extended);
        }

        /// <summary>按下一组组合键(修饰键 + 触发键), 不回弹。</summary>
        public static void HotkeyDown(Hotkey hk)
        {
            if (hk == null) return;
            foreach (var m in hk.Modifiers) KeyDown((int)m);
            foreach (var k in hk.Keys) KeyDown((int)k);
        }

        /// <summary>松开一组组合键(先松触发键, 再松修饰键)。</summary>
        public static void HotkeyUp(Hotkey hk)
        {
            if (hk == null) return;
            for (int i = hk.Keys.Count - 1; i >= 0; i--) KeyUp((int)hk.Keys[i]);
            for (int i = hk.Modifiers.Count - 1; i >= 0; i--) KeyUp((int)hk.Modifiers[i]);
        }

        /// <summary>按下一组组合键并释放, 用于回放 Ctrl+C 之类的事件。</summary>
        public static void HotkeyTap(Hotkey hk)
        {
            if (hk == null) return;
            HotkeyDown(hk);
            Thread.Sleep(Humanizer.NextPressDuration());
            HotkeyUp(hk);
        }

        /// <summary>方向键等需要 KEYEVENTF_EXTENDEDKEY 标记的按键。</summary>
        public static bool IsExtendedKey(int vk)
        {
            switch (vk)
            {
                case 0x21: // PageUp
                case 0x22: // PageDown
                case 0x23: // End
                case 0x24: // Home
                case 0x25: // Left
                case 0x26: // Up
                case 0x27: // Right
                case 0x28: // Down
                case 0x2D: // Insert
                case 0x2E: // Delete
                    return true;
                default:
                    return false;
            }
        }

        // ---------- SendInput 实现 ----------

        private static void SendInputMouse(uint flags)
        {
            var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
            input.U.mi.dwFlags = flags;
            NativeMethods.SendInput(1, new[] { input }, NativeMethods.INPUT.Size);
        }

        /// <summary>绝对坐标注入, 按整个虚拟桌面(多显示器)归一化, 修复副屏坐标偏移问题。</summary>
        private static void SendInputMove(int x, int y)
        {
            int vx = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int vy = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int vw = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int vh = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
            if (vw <= 1 || vh <= 1) return;
            // 坐标先钳到虚拟屏原点再归一化: 负坐标(副屏在主屏左侧/上方)直接 uint 强转会得到错误值
            uint fx = (uint)((Math.Max(0, x - vx) * 65535.0) / (vw - 1));
            uint fy = (uint)((Math.Max(0, y - vy) * 65535.0) / (vh - 1));
            var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
            input.U.mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE;
            input.U.mi.dx = unchecked((int)fx);
            input.U.mi.dy = unchecked((int)fy);
            NativeMethods.SendInput(1, new[] { input }, NativeMethods.INPUT.Size);
        }

        private static void SendInputKey(ushort vk, bool up, bool extended)
        {
            var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_KEYBOARD };
            if (KeyboardScanCode)
            {
                // 扫描码注入: 部分游戏不信任 wVk 字段, 只认扫描码
                uint scan = NativeMethods.MapVirtualKey(vk, 0);
                if (scan == 0) scan = vk; // 无扫描码映射时回退用 vk(避免静默丢失按键)
                input.U.ki.wScan = (ushort)scan;
                input.U.ki.dwFlags = NativeMethods.KEYEVENTF_SCANCODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0);
                // 扩展键(方向键/Insert/Delete/Home/End/多媒体键等)必须加 EXTENDEDKEY, 否则扫描码不带 0xE0 前缀注入错键
                if (extended) input.U.ki.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            }
            else
            {
                input.U.ki.wVk = vk;
                input.U.ki.dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0;
                if (extended) input.U.ki.dwFlags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
            }
            NativeMethods.SendInput(1, new[] { input }, NativeMethods.INPUT.Size);
        }

        // ---------- SendMessage 实现 ----------

        /// <summary>解析目标窗口: 配置了标题则按标题查找, 找不到或未配置时用前台窗口。</summary>
        private static IntPtr ResolveTarget()
        {
            if (!string.IsNullOrEmpty(TargetWindowTitle))
            {
                IntPtr h = NativeMethods.FindWindow(null, TargetWindowTitle);
                if (h != IntPtr.Zero) return h;
            }
            return NativeMethods.GetForegroundWindow();
        }

        /// <summary>把屏幕坐标转成目标窗口的客户区坐标, 记录下来供后续按下/抬起使用。</summary>
        private static void PostMove(int x, int y)
        {
            IntPtr h = ResolveTarget();
            if (h == IntPtr.Zero) return;
            var p = new NativeMethods.POINT { x = x, y = y };
            NativeMethods.ScreenToClient(h, ref p);
            _lastTargetX = p.x;
            _lastTargetY = p.y;
            _hasLastTarget = true;
            NativeMethods.PostMessage(h, NativeMethods.WM_MOUSEMOVE, IntPtr.Zero, MakeLParam(p.x, p.y));
        }

        /// <summary>点击消息坐标: 优先用最近一次 MoveTo 的目标(固定坐标模式), 否则用当前光标位置(跟随模式)。</summary>
        private static IntPtr GetMsgPos()
        {
            int x, y;
            if (_hasLastTarget)
            {
                x = _lastTargetX;
                y = _lastTargetY;
            }
            else
            {
                NativeMethods.POINT p;
                NativeMethods.GetCursorPos(out p);
                IntPtr h = ResolveTarget();
                if (h != IntPtr.Zero) NativeMethods.ScreenToClient(h, ref p);
                x = p.x;
                y = p.y;
            }
            return MakeLParam(x, y);
        }

        private static IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)unchecked((y << 16) | (x & 0xFFFF));
        }

        private static void PostMouse(uint wm, IntPtr lParam)
        {
            IntPtr h = ResolveTarget();
            if (h == IntPtr.Zero) return;
            NativeMethods.PostMessage(h, wm, IntPtr.Zero, lParam);
        }

        private static void PostKey(uint vk, bool up, bool extended)
        {
            IntPtr h = ResolveTarget();
            if (h == IntPtr.Zero) return;
            uint scan = NativeMethods.MapVirtualKey(vk, 0);
            int lParam = (int)((scan << 16) | 0x1);
            if (extended) lParam |= (1 << 24);
            NativeMethods.PostMessage(h, (uint)(up ? NativeMethods.WM_KEYUP : NativeMethods.WM_KEYDOWN),
                (IntPtr)(long)vk, (IntPtr)lParam);
        }

        // ---------- 驱动注入 ----------

        /// <summary>惰性初始化驱动; 不可用时返回空壳驱动(所有操作静默跳过, 引擎不中断)。线程安全, DLL 晚些放入目录时自动重试。</summary>
        private static InterceptionDriver GetDriver()
        {
            lock (DriverLock)
            {
                if (_driver == null || !_driver.Available)
                {
                    InterceptionDriver d = InterceptionDriver.Create();
                    if (d != null)
                    {
                        if (_driver != null) _driver.Dispose(); // 换掉旧空壳/旧上下文
                        _driver = d;
                    }
                    else if (_driver == null)
                    {
                        _driver = new InterceptionDriver(); // 空壳兜底: _ctx 为 0, 所有操作静默跳过
                    }
                }
            }
            return _driver;
        }

        /// <summary>退出前销毁驱动上下文(释放句柄)。</summary>
        public static void Shutdown()
        {
            lock (DriverLock)
            {
                if (_driver != null)
                {
                    _driver.Dispose();
                    _driver = null;
                }
            }
        }

        // ---------- 映射 ----------

        private static uint GetDownFlag(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Right: return NativeMethods.MOUSEEVENTF_RIGHTDOWN;
                case MouseButton.Middle: return NativeMethods.MOUSEEVENTF_MIDDLEDOWN;
                default: return NativeMethods.MOUSEEVENTF_LEFTDOWN;
            }
        }

        private static uint GetUpFlag(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Right: return NativeMethods.MOUSEEVENTF_RIGHTUP;
                case MouseButton.Middle: return NativeMethods.MOUSEEVENTF_MIDDLEUP;
                default: return NativeMethods.MOUSEEVENTF_LEFTUP;
            }
        }

        private static uint GetDownWm(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Right: return NativeMethods.WM_RBUTTONDOWN;
                case MouseButton.Middle: return NativeMethods.WM_MBUTTONDOWN;
                default: return NativeMethods.WM_LBUTTONDOWN;
            }
        }

        private static uint GetUpWm(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Right: return NativeMethods.WM_RBUTTONUP;
                case MouseButton.Middle: return NativeMethods.WM_MBUTTONUP;
                default: return NativeMethods.WM_LBUTTONUP;
            }
        }
    }
}
