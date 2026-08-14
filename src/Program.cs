using System;
using System.Windows.Forms;

namespace AutoClickerTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
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
    }
}
