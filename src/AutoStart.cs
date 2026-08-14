using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutoClickerTool
{
    /// <summary>
    /// 开机自启动: 通过 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 注册/注销本程序。
    /// 写当前用户 Run 键, 无需管理员权限; 失败(权限/组策略等)静默忽略。
    /// </summary>
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AutoClickerTool";

        /// <summary>当前是否已注册开机自启动(值存在即视为开启)。</summary>
        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>开启/关闭开机自启动; 失败静默忽略。</summary>
        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(ValueName, false);
                }
            }
            catch (Exception)
            {
                // 无权限 / 注册表被限制等, 静默失败
            }
        }
    }
}
