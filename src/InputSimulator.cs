using System;
using System.Collections.Generic;
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
    ///   SendInput          标准系统注入, 兼容性最好, 但带"注入"标记(LLKHF_INJECTED), 部分游戏会识别;
    ///                      部分反作弊还会统计注入事件的比例
    ///   WindowMessage      直接向目标窗口投递 WM_* 消息(PostMessage)。不移动物理光标、不需要前台焦点,
    ///                      事件也不带注入标记; 坐标按目标窗口客户区换算, 窗口移动后不会整体打偏。
    ///                      局限: 只对走窗口过程(WndProc)取输入的程序有效, 纯 Raw Input / DirectInput
    ///                      轮询的程序不认
    ///   InterceptionDriver 驱动级注入, 从设备驱动层注入, 与真实硬件输入无异, 最彻底(需安装驱动)。
    ///                      注意: 开启内核隔离(HVCI)/内存完整性的系统无法加载未签名驱动, 此时该方式不可用
    /// </summary>
    internal enum InjectionMethod
    {
        SendInput,
        WindowMessage,
        InterceptionDriver
    }

    /// <summary>输入注入中枢: 鼠标/键盘事件按当前注入方式路由, 三种方式共用同一套调用入口。</summary>
    internal static class InputSimulator
    {
        public static volatile InjectionMethod Method = InjectionMethod.SendInput;

        /// <summary>WindowMessage 模式的目标窗口标题; 为空时取前台窗口。</summary>
        public static volatile string TargetWindowTitle = "";

        /// <summary>SendInput 模式键盘事件使用扫描码注入(部分游戏校验扫描码)。</summary>
        public static volatile bool KeyboardScanCode = false;

        /// <summary>窗口消息通道的实例(内部自己维护目标句柄/修饰键/按下状态)。</summary>
        private static readonly WindowMessageInjector WindowInjector = new WindowMessageInjector();

        /// <summary>设置 WindowMessage 通道的目标窗口(标题为空 = 跟随前台)。</summary>
        public static void SetTargetWindow(string title)
        {
            TargetWindowTitle = title ?? "";
            WindowInjector.SetTarget(TargetWindowTitle);
        }

        /// <summary>把目标窗口切到前台; 没有绑定标题时用前台窗口。返回是否成功。</summary>
        public static bool FocusTargetWindow()
        {
            IntPtr h = WindowInjector.CachedHandle;
            if (h == IntPtr.Zero || !NativeMethods.IsWindow(h))
            {
                // 未绑定标题时没有可拉前台的"目标", 视为无需处理
                if (string.IsNullOrEmpty(TargetWindowTitle)) return true;
                h = WindowInjector.Resolve();
            }
            return WindowMessageInjector.EnsureForeground(h);
        }

        /// <summary>解析当前目标窗口句柄(诊断/日志用)。</summary>
        public static IntPtr ResolveTargetHandle()
        {
            return WindowInjector.Resolve();
        }

        /// <summary>目标窗口当前是否在前台(诊断用)。</summary>
        public static bool TargetIsForeground()
        {
            return WindowInjector.IsTargetForeground();
        }

        /// <summary>把窗口消息通道里仍按住不放的键全部抬起。</summary>
        public static void ReleaseWindowKeys()
        {
            WindowInjector.ReleaseAll();
        }

        private static InterceptionDriver _driver;
        private static readonly object DriverLock = new object();
        private static int _lastTargetX;
        private static int _lastTargetY;
        private static bool _hasLastTarget;

        /// <summary>SendMessage 模式共享状态(_lastTarget*)的锁: 多引擎线程并发注入时串行化移动+点击。</summary>
        private static readonly object SendLock = new object();

        /// <summary>本程序注入过、尚未抬起的按键(见 HeldKeys 注释); 退出/全部停止时兜底释放。</summary>
        private static readonly HeldKeys HeldByProgram = new HeldKeys();

        // ---------- 鼠标 ----------

        public static void MouseDown(MouseButton button)
        {
            switch (Method)
            {
                case InjectionMethod.SendInput:
                    SendInputMouse(GetDownFlag(button));
                    break;
                case InjectionMethod.WindowMessage:
                    WindowInjector.MouseDown((int)button);
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
                case InjectionMethod.WindowMessage:
                    WindowInjector.MouseUp((int)button);
                    break;
                case InjectionMethod.InterceptionDriver:
                    GetDriver().MouseUp((int)button);
                    break;
            }
        }

        /// <summary>在当前位置点击一次, 按下与抬起之间有随机人化时长与点击微拖(见 Humanizer)。</summary>
        public static void Click(MouseButton button)
        {
            // 窗口消息模式不移动物理光标, "微拖 + 复位"没有意义, 反而会把投递坐标改掉
            if (Method == InjectionMethod.WindowMessage)
            {
                MouseDown(button);
                Thread.Sleep(Humanizer.NextPressDuration());
                MouseUp(button);
                return;
            }

            // 记录按下前的光标位置, 供抬起后复位(避免"跟随光标"模式每次点击累积漂移)
            NativeMethods.POINT orig;
            bool haveOrig = NativeMethods.GetCursorPos(out orig);

            MouseDown(button);
            // 点击微拖: 按下与抬起之间 1~2 像素微移, 模拟真人点击的微小滑动
            int mdx, mdy;
            Humanizer.MicroDrag(out mdx, out mdy);
            if ((mdx != 0 || mdy != 0) && haveOrig)
            {
                MoveTo(orig.x + mdx, orig.y + mdy);
            }
            Thread.Sleep(Humanizer.NextPressDuration());
            MouseUp(button);
            if ((mdx != 0 || mdy != 0) && haveOrig)
            {
                // 抬起后复位: 跟随模式下用户把光标停在目标点, 微拖不回归会让光标逐次漂移
                MoveStep(orig.x, orig.y);
            }
            _hasLastTarget = false;
        }

        public static void ClickAt(int x, int y, MouseButton button)
        {
            if (Method == InjectionMethod.WindowMessage)
            {
                // 窗口消息通道的移动只记录目标点, 没有"异步移动"问题, 无需加锁串行
                MoveTo(x, y);
                Click(button);
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
                case InjectionMethod.WindowMessage:
                    WindowInjector.Wheel(delta);
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
                case InjectionMethod.WindowMessage:
                    // 只记录目标点, 不碰物理光标
                    WindowInjector.SetPosition(x, y);
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
                case InjectionMethod.WindowMessage:
                    WindowInjector.Key(vk, false);
                    break;
                case InjectionMethod.InterceptionDriver:
                    GetDriver().Key(vk, false);
                    break;
            }
            HeldByProgram.Add(vk, extended);
        }

        public static void KeyUp(int vk, bool extended = false)
        {
            switch (Method)
            {
                case InjectionMethod.SendInput:
                    SendInputKey((ushort)vk, true, extended);
                    break;
                case InjectionMethod.WindowMessage:
                    WindowInjector.Key(vk, true);
                    break;
                case InjectionMethod.InterceptionDriver:
                    GetDriver().Key(vk, true);
                    break;
            }
            HeldByProgram.Remove(vk);
        }

        /// <summary>
        /// 兜底: 把"注入过但一直没抬起"的按键全部补发一次抬起。
        /// 注入的键若在系统层面保持按下, 会表现为修饰键卡住、回车变成 Ctrl+Enter/Shift+Enter
        /// 而"完全失灵"(连屏幕键盘的回车也没用), 且进程退出也不会自动释放。
        /// 退出/全部停止时调用, 保证任何异常路径都不会把键留在按下状态。
        /// </summary>
        public static void ReleaseAllKeys()
        {
            // 窗口消息通道有自己的按下集合(它不经过系统输入栈, HeldByProgram 里没有记录),
            // 必须单独补发抬起, 否则 Hold 模式停止后目标窗口会一直认为键按着
            try { WindowInjector.ReleaseAll(); } catch (Exception) { }

            List<KeyValuePair<int, bool>> pending = HeldByProgram.TakeAll();
            if (pending.Count == 0) return;
            var names = new List<string>();
            foreach (KeyValuePair<int, bool> kv in pending)
            {
                names.Add(Hotkey.GetName((uint)kv.Key));
                try { KeyUp(kv.Key, kv.Value); } catch (Exception) { }
            }
            Log.Warn("补发抬起仍按住的按键: " + string.Join("+", names.ToArray()));
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
            foreach (var m in hk.Modifiers) KeyDown((int)m, IsExtendedKey((int)m));
            foreach (var k in hk.Keys) KeyDown((int)k, IsExtendedKey((int)k));
        }

        /// <summary>松开一组组合键(先松触发键, 再松修饰键)。</summary>
        public static void HotkeyUp(Hotkey hk)
        {
            if (hk == null) return;
            for (int i = hk.Keys.Count - 1; i >= 0; i--) KeyUp((int)hk.Keys[i], IsExtendedKey((int)hk.Keys[i]));
            for (int i = hk.Modifiers.Count - 1; i >= 0; i--) KeyUp((int)hk.Modifiers[i], IsExtendedKey((int)hk.Modifiers[i]));
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
            // 坐标先钳到虚拟屏范围再归一化: 负坐标(副屏在主屏左侧/上方)直接 uint 强转会得到错误值
            double px = Math.Max(vx, Math.Min(vx + vw - 1, x));
            double py = Math.Max(vy, Math.Min(vy + vh - 1, y));
            uint fx = (uint)Math.Round((px - vx) * 65535.0 / (vw - 1));
            uint fy = (uint)Math.Round((py - vy) * 65535.0 / (vh - 1));
            var input = new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE };
            // 必须带 VIRTUALDESK: 否则 0~65535 只映射到主显示器, 而这里按整个虚拟桌面归一化,
            // 副屏在主屏左侧/上方(虚拟屏原点非 0,0)时会整体偏移——2K 副屏录制回放"跑到右边"就是这个原因
            input.U.mi.dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE
                                 | NativeMethods.MOUSEEVENTF_VIRTUALDESK;
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
