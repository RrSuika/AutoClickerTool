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
        /// <summary>配置结构版本号: 未来改字段时据此做迁移, 避免旧配置静默错乱。</summary>
        public int ConfigVersion = 3;

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
        public string SpamKeyText = "";      // 键盘连按页直接输入的按键文本(空 = 用下拉框选择)
        public int SpamIntervalMs = 100;
        public bool SpamHold = false;

        // ---- 录制回放 ----
        public double PlaySpeed = 1.0;
        public bool PlayLoop = false;
        public int PlayLoops = 0;           // 循环次数, 0 = 无限
        public int PlayMinutes = 0;         // 运行分钟数, 0 = 不限
        public string PlayUntilTime = "";   // 运行到系统时刻 "HH:mm", 空 = 不限

        // ---- 软件控制(宏库页) ----
        public List<string> LaunchPrograms = new List<string>();
        public bool LaunchOnStart = false;  // 回放开始时自动启动程序
        public bool LaunchOnEnd = false;    // 回放结束时自动启动程序

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
        // 注意: JavaScriptSerializer 反序列化要求字典键为字符串, 因此单键绑定用字符串键存键码(JSON 中本就是字符串键, 向后兼容)。
        public bool SfxEnabled = true;   // 按键音效默认开启(新安装即生效; 用户可关闭)
        public int SfxVolume = 100;     // 全局音量 0~100(%)
        public Dictionary<string, string> SfxBindings = new Dictionary<string, string>(); // 键码(字符串) → Sounds 文件夹内文件名
        public Dictionary<string, int> SfxBindingVolumes = new Dictionary<string, int>(); // 键码(字符串) → 单键音量 0~100(缺省用全局)
        public Dictionary<string, string> SfxComboBindings = new Dictionary<string, string>(); // 组合键字符串(如 Ctrl+C) → 文件名
        public Dictionary<string, int> SfxComboVolumes = new Dictionary<string, int>(); // 组合键字符串 → 音量 0~100(缺省用全局)

        // ---- 界面 ----
        public bool Topmost = false;
        public bool AnimationsEnabled = true;   // 界面动效(悬停/按压/标签过渡); 关闭 = 全部瞬时(等效减少动态效果)
        public string Language = "zh";       // zh / en
        public string ThemeName = "Clay";    // Clay / ArtDeco / Skeuo / Surreal / Cyber / Y2K

        // ---- 启动 ----
        public bool AutoStart = false;      // 开机自启动(写 HKCU Run 键)
        public bool StartMinimized = false; // 静默启动: 启动后不显示窗口, 最小化到系统托盘

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
                if (cfg == null) return new AppConfig();
                Migrate(cfg);
                return cfg;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return new AppConfig();
            }
        }

        /// <summary>按 ConfigVersion 做增量迁移(旧版本存档加载时逐步升级到当前结构)。</summary>
        private static void Migrate(AppConfig cfg)
        {
            // 当前版本为 3; 未来新增字段时在这里按 cfg.ConfigVersion 补齐, 最后设为最新版本号
            if (cfg.ConfigVersion < 2)
            {
                // 例如: cfg.SpamKeyText = ""; 之类
                Log.Warn(string.Format("配置从版本 {0} 迁移到 {1}", cfg.ConfigVersion, 2));
            }
            if (cfg.ConfigVersion < 3)
            {
                // 新增 AutoStart/StartMinimized(默认 false, 无需迁移); 音效 SfxEnabled 默认改为 true 仅对新配置生效,
                // 老配置保留其显式保存的值(无法区分"用户手动关闭"与"旧默认 false", 故不强制覆盖)。
                Log.Warn(string.Format("配置从版本 {0} 迁移到 {1}", cfg.ConfigVersion, 3));
            }
            cfg.ConfigVersion = 3;
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                string tmp = FilePath + ".tmp";
                // 原子写: 先写临时文件再替换, 避免中途崩溃/断电损坏 config.json 导致设置全丢
                File.WriteAllText(tmp, ser.Serialize(this), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception)
            {
                // 保存失败不致命(如程序目录只读), 静默忽略; 清理残留临时文件
                try { if (File.Exists(FilePath + ".tmp")) File.Delete(FilePath + ".tmp"); } catch (Exception) { }
            }
        }
    }
}
