using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AutoClickerTool
{
    /// <summary>
    /// 应用程序设置。保存到 exe 同目录下的 config.json，启动时自动加载。
    /// 热键以字符串形式存储(如 "Ctrl+Shift+K"、"F6"、"Alt+Q"、"鼠标X1")。
    /// </summary>
    internal class AppConfig
    {
        // ---- 功能热键 ----
        public string ClickerHotkey = "F6";
        public string RecordHotkey = "F7";
        public string PlayHotkey = "F8";
        public string KeyboardHotkey = "F9";
        public string StopAllHotkey = "F12";

        // ---- 鼠标连点 ----
        public int ClickIntervalMs = 100;
        public int ClickButton = 0;          // 0=左 1=右 2=中
        public bool ClickFixedPosition = false;
        public int ClickFixedX = 0;
        public int ClickFixedY = 0;
        public int ClickRepeatCount = 0;     // 0 = 无限

        // ---- 键盘连按 ----
        public int SpamVk = 0x41;            // 虚拟键码, 默认 A
        public int SpamIntervalMs = 100;
        public bool SpamHold = false;

        // ---- 录制回放 ----
        public double PlaySpeed = 1.0;
        public bool PlayLoop = false;

        // ---- 注入方式与拟人化 ----
        public string InjectionMethod = "SendInput"; // SendInput / SendMessage / InterceptionDriver
        public bool TargetNamed = false;     // true=发往指定标题窗口, false=前台窗口
        public string TargetWindowTitle = "";
        public bool KeyboardScanCode = false;
        public bool HumanizeEnabled = true;  // 拟人化总开关
        public bool HumanizeTiming = true;   // 随机化间隔
        public int HumanizeTimingPct = 15;   // 间隔抖动百分比
        public bool HumanizePosition = true; // 固定坐标微抖动
        public int HumanizePositionPx = 2;   // 抖动像素
        public bool HumanizePressDuration = true; // 随机按键时长
        public bool HumanizeTrajectory = true;    // 贝塞尔移动轨迹

        // ---- 按键音效 ----
        public bool SfxEnabled = false;
        public int SfxVolume = 100;     // 全局音量 0~100(%)
        public Dictionary<int, string> SfxBindings = new Dictionary<int, string>(); // 键码 → Sounds 文件夹内文件名
        public Dictionary<int, int> SfxBindingVolumes = new Dictionary<int, int>(); // 键码 → 单键音量 0~100(缺省用全局)
        public Dictionary<string, string> SfxComboBindings = new Dictionary<string, string>(); // 组合键字符串(如 Ctrl+C) → 文件名
        public Dictionary<string, int> SfxComboVolumes = new Dictionary<string, int>(); // 组合键字符串 → 音量 0~100(缺省用全局)

        // ---- 界面 ----
        public bool Topmost = false;
        public string Language = "zh";       // zh / en
        public string ThemeName = "Clay";    // Clay / ArtDeco / Skeuo / Surreal / Cyber / Y2K

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json"); }
        }

        /// <summary>config.json 是否存在(用于判断是否首次启动, 弹出欢迎窗口)。</summary>
        public static bool ConfigExists
        {
            get { return File.Exists(FilePath); }
        }

        /// <summary>宏存档目录(exe 同目录 Macros)。</summary>
        public static string MacrosDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Macros"); }
        }

        /// <summary>音效文件目录(exe 同目录 Sounds)。</summary>
        public static string SoundsDir
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds"); }
        }

        /// <summary>启动时确保数据目录存在(宏/音效)。</summary>
        public static void EnsureDataDirs()
        {
            try
            {
                Directory.CreateDirectory(MacrosDir);
                Directory.CreateDirectory(SoundsDir);
            }
            catch (Exception)
            {
                // 目录创建失败不致命(如只读目录)
            }
        }

        /// <summary>加载配置; 文件不存在或损坏时返回默认值, 原因通过 out 返回。</summary>
        public static AppConfig Load(out string error)
        {
            error = null;
            try
            {
                if (!File.Exists(FilePath)) return new AppConfig();
                var ser = new JavaScriptSerializer();
                var cfg = ser.Deserialize<AppConfig>(File.ReadAllText(FilePath, Encoding.UTF8));
                return cfg ?? new AppConfig();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return new AppConfig();
            }
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                File.WriteAllText(FilePath, ser.Serialize(this), Encoding.UTF8);
            }
            catch (Exception)
            {
                // 保存失败不致命(如程序目录只读), 静默忽略
            }
        }
    }
}
