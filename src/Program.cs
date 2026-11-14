using System;
using System.Threading;
using System.Windows.Forms;

namespace AutoClickerTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // 免 GUI 的注入自检: 用来判断"某种注入方式对某个窗口到底有没有效",
            // 不需要热键(工具的全局热键按设计会忽略注入按键, 自动化测试无法触发)
            //   AutoClicker.exe --wmtest "<窗口标题>" <屏幕X> <屏幕Y> <点击次数> [间隔ms]
            //   AutoClicker.exe --wmtest "<窗口标题>" --key <VK> <次数>
            if (args != null && args.Length >= 2 && args[0] == "--wmtest")
            {
                // 必须在任何坐标换算之前开启 DPI 感知: 否则 ScreenToClient/ClientToScreen 走
                // 96dpi 虚拟化, 投递出去的客户区坐标会整体偏移(实测 150% 缩放时偏了 480px)
                NativeMethods.EnablePerMonitorDpiAware();
                RunWindowMessageTest(args);
                return;
            }

            Log.Info("=== AutoClickerTool " + VersionInfo.Version + " 启动 ===");

            // 未捕获 UI 线程异常兜底: 必须在创建任何窗口之前设置。退出时触发 OnFormClosing→StopAll 松键,
            // 防止 Hold 模式/回放卡键残留; 不做 UI 操作避免递归异常。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
            {
                try { Log.Error("未捕获 UI 线程异常: " + e.Exception); } catch (Exception) { }
                try { Application.Exit(); } catch (Exception) { }
            };

            NativeMethods.EnablePerMonitorDpiAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>
        /// 窗口消息注入自检。日志写到 log.txt(程序是 winexe, 没有控制台输出)。
        /// 用途: 换游戏/换注入方式时快速判断通道有没有真的送到目标窗口。
        /// </summary>
        private static void RunWindowMessageTest(string[] args)
        {
            try
            {
                string title = args.Length > 1 ? args[1] : "";
                InputSimulator.SetTargetWindow(title);
                WindowMessageInjector.DebugLog = true;
                InputSimulator.Method = InjectionMethod.WindowMessage;

                IntPtr h = InputSimulator.ResolveTargetHandle();
                Log.Info("WM自检: 标题=\"" + title + "\" hwnd=0x" + h.ToString("X")
                         + " 是否前台=" + InputSimulator.TargetIsForeground());

                if (h == IntPtr.Zero)
                {
                    Log.Warn("WM自检: 目标窗口未解析到, 放弃");
                    return;
                }

                if (args.Length >= 4 && args[2] == "--key")
                {
                    int vk = int.Parse(args[3]);
                    int count = args.Length > 4 ? int.Parse(args[4]) : 1;
                    for (int i = 0; i < count; i++)
                    {
                        InputSimulator.KeyTap(vk);
                        Thread.Sleep(120);
                    }
                    Log.Info("WM自检: 已发送按键 vk=0x" + vk.ToString("X") + " x" + count);
                    return;
                }

                int x = int.Parse(args[2]);
                int y = int.Parse(args[3]);
                int n = args.Length > 4 ? int.Parse(args[4]) : 1;
                int gap = args.Length > 5 ? int.Parse(args[5]) : 400;
                for (int i = 0; i < n; i++)
                {
                    InputSimulator.ClickAt(x, y, MouseButton.Left);
                    Thread.Sleep(gap);
                }
                Log.Info("WM自检: 已发送点击 (" + x + "," + y + ") x" + n + " 间隔" + gap + "ms");
            }
            catch (Exception ex)
            {
                Log.Error("WM自检异常: " + ex);
            }
        }
    }
}
