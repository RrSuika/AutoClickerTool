using System;
using System.Windows.Forms;

namespace AutoClickerTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            NativeMethods.EnablePerMonitorDpiAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
