using System;
using System.Runtime.InteropServices;

namespace AutoClickerTool
{
    /// <summary>
    /// 驱动级输入注入, 基于开源 Interception 驱动(github.com/oblitum/Interception)。
    /// 在键盘/鼠标设备驱动层注入, 对游戏而言与真实硬件输入完全一致, 可绕过绝大多数
    /// 注入检测(原始输入/Raw Input 也照常收到)。
    /// 使用方式: 1) 以管理员身份运行安装包安装驱动; 2) 把 interception.dll 放到本程序目录。
    /// 注意: 系统开启内核隔离(HVCI/内存完整性)或 Secure Boot 时, 未签名驱动可能无法加载。
    /// interception.dll 通过 DllImport 惰性绑定, 文件不存在时不影响其它注入方式。
    /// </summary>
    internal sealed class InterceptionDriver
    {
        // ---- interception.h 中的常量 ----
        private const int PREDICATE_KEYBOARD = 0; // interception_is_keyboard
        private const int PREDICATE_MOUSE = 1;    // interception_is_mouse

        private const ushort FILTER_KEY_ALL = 0xFFFF;
        private const ushort FILTER_KEY_NONE = 0x0000;
        private const ushort FILTER_MOUSE_ALL = 0xFFFF;
        private const ushort FILTER_MOUSE_NONE = 0x0000;

        private const int DEVICE_KEYBOARD = 1;    // 键盘设备 1..10
        private const int DEVICE_MOUSE = 11;      // 鼠标设备 11..20

        private const ushort KEY_DOWN = 0x00;
        private const ushort KEY_UP = 0x01;

        private const ushort MOUSE_LEFT_DOWN = 0x001;
        private const ushort MOUSE_LEFT_UP = 0x002;
        private const ushort MOUSE_RIGHT_DOWN = 0x004;
        private const ushort MOUSE_RIGHT_UP = 0x008;
        private const ushort MOUSE_MIDDLE_DOWN = 0x010;
        private const ushort MOUSE_MIDDLE_UP = 0x020;
        private const ushort MOUSE_WHEEL = 0x400;
        private const ushort MOUSE_WHEEL_UP = 0x200;
        private const ushort MOUSE_WHEEL_DOWN = 0x100;
        private const ushort MOUSE_MOVE_ABSOLUTE = 0x800;

        // ---- interception.dll 导出函数(惰性绑定, DLL 缺失时首次调用抛异常) ----

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr interception_create_context();

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_destroy_context(IntPtr context);

        [DllImport("interception.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void interception_set_filter(IntPtr context, int predicate, ushort filter);

        [DllImport("interception.dll", EntryPoint = "interception_send", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SendKeyStroke(IntPtr context, int device, InterceptionKeyStroke[] strokes, uint nstrokes);

        [DllImport("interception.dll", EntryPoint = "interception_send", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SendMouseStroke(IntPtr context, int device, InterceptionMouseStroke[] strokes, uint nstrokes);

        [StructLayout(LayoutKind.Sequential)]
        private struct InterceptionKeyStroke
        {
            public ushort code;        // 硬件扫描码(ESC=0x01)
            public ushort state;       // KEY_DOWN / KEY_UP
            public uint information;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InterceptionMouseStroke
        {
            public ushort state;
            public ushort flags;
            public short rolling;      // 滚轮格数
            public int x;              // 绝对屏幕像素坐标
            public int y;
            public uint information;
        }

        private IntPtr _ctx;

        /// <summary>检测 interception.dll 与驱动是否可用。</summary>
        public static bool IsDllPresent()
        {
            try
            {
                IntPtr ctx = interception_create_context();
                if (ctx == IntPtr.Zero) return false;
                interception_destroy_context(ctx);
                return true;
            }
            catch (Exception)
            {
                return false; // DLL 缺失 / 位数不匹配 / 依赖缺失
            }
        }

        /// <summary>初始化; DLL 缺失或驱动不可用时返回 null。</summary>
        public static InterceptionDriver Create()
        {
            try
            {
                var d = new InterceptionDriver();
                d._ctx = interception_create_context();
                if (d._ctx == IntPtr.Zero) return null;
                return d;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool Available { get { return _ctx != IntPtr.Zero; } }

        public void MoveAbsolute(int x, int y)
        {
            if (!Available) return;
            var s = new InterceptionMouseStroke { state = MOUSE_MOVE_ABSOLUTE, x = x, y = y };
            SendMouse(new[] { s });
        }

        public void MouseDown(int button)
        {
            if (!Available) return;
            ushort st;
            switch (button)
            {
                case 1: st = MOUSE_RIGHT_DOWN; break;
                case 2: st = MOUSE_MIDDLE_DOWN; break;
                default: st = MOUSE_LEFT_DOWN; break;
            }
            var s = new InterceptionMouseStroke { state = st, x = CursorX(), y = CursorY() };
            SendMouse(new[] { s });
        }

        public void MouseUp(int button)
        {
            if (!Available) return;
            ushort st;
            switch (button)
            {
                case 1: st = MOUSE_RIGHT_UP; break;
                case 2: st = MOUSE_MIDDLE_UP; break;
                default: st = MOUSE_LEFT_UP; break;
            }
            var s = new InterceptionMouseStroke { state = st, x = CursorX(), y = CursorY() };
            SendMouse(new[] { s });
        }

        public void Wheel(int delta)
        {
            if (!Available) return;
            ushort state = (ushort)(MOUSE_WHEEL | (delta > 0 ? MOUSE_WHEEL_UP : MOUSE_WHEEL_DOWN));
            short rolling = (short)Math.Max(1, Math.Min(100, Math.Abs(delta) / 120));
            var s = new InterceptionMouseStroke { state = state, rolling = rolling, x = CursorX(), y = CursorY() };
            SendMouse(new[] { s });
        }

        public void Key(int vk, bool up)
        {
            if (!Available) return;
            uint scan = NativeMethods.MapVirtualKey((uint)vk, 0);
            if (scan == 0) return;
            var s = new InterceptionKeyStroke
            {
                code = (ushort)scan,
                state = up ? KEY_UP : KEY_DOWN
            };
            SendKey(new[] { s });
        }

        private void SendKey(InterceptionKeyStroke[] strokes)
        {
            interception_set_filter(_ctx, PREDICATE_KEYBOARD, FILTER_KEY_ALL);
            SendKeyStroke(_ctx, DEVICE_KEYBOARD, strokes, (uint)strokes.Length);
            interception_set_filter(_ctx, PREDICATE_KEYBOARD, FILTER_KEY_NONE);
        }

        private void SendMouse(InterceptionMouseStroke[] strokes)
        {
            interception_set_filter(_ctx, PREDICATE_MOUSE, FILTER_MOUSE_ALL);
            SendMouseStroke(_ctx, DEVICE_MOUSE, strokes, (uint)strokes.Length);
            interception_set_filter(_ctx, PREDICATE_MOUSE, FILTER_MOUSE_NONE);
        }

        private static int CursorX()
        {
            NativeMethods.POINT p;
            NativeMethods.GetCursorPos(out p);
            return p.x;
        }

        private static int CursorY()
        {
            NativeMethods.POINT p;
            NativeMethods.GetCursorPos(out p);
            return p.y;
        }
    }
}
