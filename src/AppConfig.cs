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
        /// <summary>配置结构版本号: 未来改字段时据此做迁移, 避免旧配置静默错乱。0=旧配置缺字段, 加载后迁移到 CurrentVersion。</summary>
        public int ConfigVersion = 0;

        /// <summary>当前配置结构版本。新增字段并需要迁移时 +1, 并在 Migrate() 里按版本补齐。</summary>
        private const int CurrentVersion = 4;

        // 运行限制模式: 三个功能(连点/连按/回放)统一使用同一套语义。
        public const int LimitCount = 0;      // 指定次数
        public const int LimitInfinite = 1;   // 无限循环
        public const int LimitDuration = 2;   // 运行指定时长 / 运行到指定时刻

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
        public int ClickRepeatCount = 0;     // 指定次数模式的点击次数, 0 = 无限
        public int ClickLimitMode = LimitInfinite; // 运行限制模式(见 LimitCount/LimitInfinite/LimitDuration)
        public int ClickSeconds = 0;         // 运行时长(秒); 与 ClickUntilAt 双向同步, 0 = 用 ClickUntilAt
        public string ClickUntilAt = "";     // 绝对截止时刻 "yyyy-MM-dd HH:mm:ss", 空 = 不限
        public bool ClickUntilPrimary = false; // 上次保存时以"截止时刻"为准(而非时长), 重启后截止点不漂移
        public int ClickMinutes = 0;         // (旧字段, 仅用于迁移) 运行分钟数, 0 = 不限
        public string ClickUntilTime = "";   // (旧字段, 仅用于迁移) 运行到系统时刻 "HH:mm"

        // ---- 键盘连按 ----
        public int SpamVk = 0x41;            // (旧字段) 虚拟键码, 默认 A
        public string SpamKeyText = "";      // (旧字段) 直接输入的按键文本(空 = 用下拉框选择)
        public string SpamCombo = "";        // 连按的按键组合(如 "A" / "Ctrl+Shift+A"), 空 = 用 SpamVk
        public int SpamIntervalMs = 100;
        public bool SpamHold = false;
        public int SpamRepeatCount = 0;      // 指定次数模式的点按次数, 0 = 无限
        public int SpamLimitMode = LimitInfinite;
        public int SpamSeconds = 0;          // 运行时长(秒); 与 SpamUntilAt 双向同步
        public string SpamUntilAt = "";      // 绝对截止时刻 "yyyy-MM-dd HH:mm:ss", 空 = 不限
        public bool SpamUntilPrimary = false; // 上次保存时以"截止时刻"为准(而非时长)
        public int SpamMinutes = 0;          // (旧字段, 仅用于迁移) 运行分钟数
        public string SpamUntilTime = "";    // (旧字段, 仅用于迁移) 运行到系统时刻 "HH:mm"

        // ---- 录制回放 ----
        public double PlaySpeed = 1.0;
        public bool PlayLoop = false;        // (旧字段, 仅用于迁移)
        public int PlayLoops = 1;            // 指定次数模式的循环轮数, 0 = 无限
        public int PlayLimitMode = LimitCount; // 默认: 回放一轮(与旧默认一致)
        public int PlaySeconds = 0;          // 运行时长(秒); 与 PlayUntilAt 双向同步
        public string PlayUntilAt = "";      // 绝对截止时刻 "yyyy-MM-dd HH:mm:ss", 空 = 不限
        public bool PlayUntilPrimary = false; // 上次保存时以"截止时刻"为准(而非时长)
        public int PlayMinutes = 0;          // (旧字段, 仅用于迁移) 运行分钟数
        public string PlayUntilTime = "";    // (旧字段, 仅用于迁移) 运行到系统时刻 "HH:mm"

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
                if (!File.Exists(FilePath)) return Default();
                var ser = new JavaScriptSerializer();
                var cfg = ser.Deserialize<AppConfig>(File.ReadAllText(FilePath, Encoding.UTF8));
                if (cfg == null) return Default();
                Migrate(cfg);
                SanitizeUntrusted(cfg);
                return cfg;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return Default();
            }
        }

        /// <summary>全新配置(首次启动/配置损坏): 直接标记为当前版本, 不再走迁移。</summary>
        private static AppConfig Default()
        {
            var c = new AppConfig();
            c.ConfigVersion = CurrentVersion;
            return c;
        }

        /// <summary>按 ConfigVersion 做增量迁移(旧版本存档加载时逐步升级到当前结构)。</summary>
        private static void Migrate(AppConfig cfg)
        {
            // 未来新增字段时在这里按 cfg.ConfigVersion 补齐, 最后设为 CurrentVersion。
            // 注意: ConfigVersion 默认 0, 因此"ConfigVersion 字段出现之前的旧存档"会走完整迁移链。
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
            if (cfg.ConfigVersion < 4)
            {
                // v3 → v4: 三个功能的运行限制统一为「指定次数 / 无限循环 / 运行时长(到点)」三模式。
                // 旧配置只有 "次数(0=无限) + 运行分钟 + HH:mm 到点", 这里换算到新模式。
                Log.Warn(string.Format("配置从版本 {0} 迁移到 {1}", cfg.ConfigVersion, 4));

                cfg.ClickLimitMode = DeriveMode(cfg.ClickRepeatCount, cfg.ClickMinutes, cfg.ClickUntilTime);
                cfg.ClickSeconds = cfg.ClickMinutes * 60;
                cfg.ClickUntilAt = NormalizeUntilAt(cfg.ClickUntilTime);

                cfg.SpamLimitMode = (cfg.SpamMinutes > 0 || !string.IsNullOrEmpty(cfg.SpamUntilTime))
                    ? LimitDuration : LimitInfinite;
                cfg.SpamSeconds = cfg.SpamMinutes * 60;
                cfg.SpamUntilAt = NormalizeUntilAt(cfg.SpamUntilTime);

                if (cfg.PlayMinutes > 0 || !string.IsNullOrEmpty(cfg.PlayUntilTime))
                {
                    cfg.PlayLimitMode = LimitDuration;
                    cfg.PlaySeconds = cfg.PlayMinutes * 60;
                }
                else if (cfg.PlayLoops > 0)
                {
                    cfg.PlayLimitMode = LimitCount;
                }
                else if (cfg.PlayLoop)
                {
                    cfg.PlayLimitMode = LimitInfinite;
                    cfg.PlayLoops = 0;
                }
                else
                {
                    // 旧默认(不勾循环、次数 0): 只回放一轮
                    cfg.PlayLimitMode = LimitCount;
                    cfg.PlayLoops = 1;
                }
                cfg.PlayUntilAt = NormalizeUntilAt(cfg.PlayUntilTime);

                // 旧 "HH:mm" 到点语义是"今天/明天该时刻" → 迁移后以截止时刻为准(主),
                // 加载时按该语义顺延, 截止点不会随重启漂移
                cfg.ClickUntilPrimary = !string.IsNullOrEmpty(cfg.ClickUntilTime);
                cfg.SpamUntilPrimary = !string.IsNullOrEmpty(cfg.SpamUntilTime);
                cfg.PlayUntilPrimary = !string.IsNullOrEmpty(cfg.PlayUntilTime);
            }
            cfg.ConfigVersion = CurrentVersion;
        }

        /// <summary>旧配置 → 新运行限制模式: 有"时长/到点"优先, 其次"指定次数", 否则无限循环。</summary>
        private static int DeriveMode(int repeatCount, int minutes, string untilText)
        {
            if (minutes > 0 || !string.IsNullOrEmpty(untilText)) return LimitDuration;
            if (repeatCount > 0) return LimitCount;
            return LimitInfinite;
        }

        /// <summary>旧 "HH:mm" 到点 → 绝对时刻字符串("yyyy-MM-dd HH:mm:ss"); 空/非法返回空串。</summary>
        private static string NormalizeUntilAt(string oldUntil)
        {
            if (string.IsNullOrEmpty(oldUntil)) return "";
            DateTime? t = Util.ParseUntilTime(oldUntil);
            return t.HasValue ? t.Value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "";
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                string tmp = FilePath + ".tmp";
                // 原子写: 先写临时文件再替换, 避免中途崩溃/断电损坏 config.json 导致设置全丢
                File.WriteAllText(tmp, ser.Serialize(this), Encoding.UTF8);
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(tmp, FilePath, null);
                    }
                    catch (Exception)
                    {
                        // FAT/exFAT 卷不支持 File.Replace, 回退为 删旧+改名(保证能保存)
                        try { File.Delete(FilePath); } catch (Exception) { }
                        File.Move(tmp, FilePath);
                    }
                }
                else File.Move(tmp, FilePath);
            }
            catch (Exception)
            {
                // 保存失败不致命(如程序目录只读), 静默忽略; 清理残留临时文件
                try { if (File.Exists(FilePath + ".tmp")) File.Delete(FilePath + ".tmp"); } catch (Exception) { }
            }
        }

        /// <summary>加载后收紧来自文件的外部数据(config 可能被手工/恶意编辑):
        /// 软件控制程序白名单(防任意程序执行) + 音效绑定纯文件名校验(防路径穿越)。</summary>
        private static void SanitizeUntrusted(AppConfig cfg)
        {
            if (cfg.LaunchPrograms == null) cfg.LaunchPrograms = new List<string>();
            if (cfg.LaunchPrograms.Count > 0)
            {
                var keep = new List<string>();
                foreach (string p in cfg.LaunchPrograms)
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    bool ok = Path.IsPathRooted(p) && (ext == ".exe" || ext == ".lnk");
                    if (ok) keep.Add(p);
                    else Log.Warn("已丢弃白名单外的自动启动程序: " + p);
                }
                cfg.LaunchPrograms = keep;
            }
            SanitizeSfxDict(cfg.SfxBindings);
            SanitizeSfxDict(cfg.SfxComboBindings);
        }

        /// <summary>音效绑定值必须是纯文件名(不含目录分隔符), 防止 config 路径穿越播放磁盘任意媒体文件。</summary>
        private static void SanitizeSfxDict(Dictionary<string, string> dict)
        {
            if (dict == null || dict.Count == 0) return;
            var bad = new List<string>();
            foreach (var kv in dict)
            {
                string v = kv.Value;
                bool ok = !string.IsNullOrEmpty(v) && v == Path.GetFileName(v) && v.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
                if (!ok) bad.Add(kv.Key);
            }
            foreach (string k in bad)
            {
                Log.Warn("已丢弃非法的音效绑定: " + k + " -> " + dict[k]);
                dict.Remove(k);
            }
        }
    }
}
