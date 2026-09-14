using System;
using AutoClickerTool;

/// <summary>
/// 最小单元测试: 覆盖纯逻辑模块(Hotkey 解析/序列化、AppConfig 序列化往返)。
/// 不依赖 UI, 用 csc 直接编译核心源文件后运行。退出码: 0=全过, 1=有失败。
/// </summary>
internal static class Tests
{
    private static int _pass;
    private static int _fail;

    private static void Check(bool cond, string name)
    {
        if (cond) { _pass++; Console.WriteLine("  [PASS] " + name); }
        else { _fail++; Console.WriteLine("  [FAIL] " + name); }
    }

    private static void RoundTrip(string text)
    {
        var hk = Hotkey.Parse(text);
        if (hk == null) { Check(false, "Parse " + text); return; }
        string s = hk.ToString();
        var hk2 = Hotkey.Parse(s);
        Check(hk2 != null && hk2.ToString() == s, text + " -> \"" + s + "\" 往返一致");
    }

    private static int Main()
    {
        Console.WriteLine("== Hotkey 解析 / 序列化往返 ==");
        RoundTrip("Ctrl+Shift+K");
        RoundTrip("F6");
        RoundTrip("Ctrl+Q+W");
        RoundTrip("Alt+Q");
        RoundTrip("Ctrl+Alt+Delete");
        RoundTrip("A");
        RoundTrip("5");

        Console.WriteLine("== Hotkey 解析边界 ==");
        Check(Hotkey.Parse("") == null, "空字符串返回 null");
        Check(Hotkey.Parse("   ") == null, "纯空白返回 null");
        Check(Hotkey.Parse("NotAKey!!") == null, "非法输入返回 null");
        Check(Hotkey.Parse("+++") == null, "全空段返回 null");
        Check(Hotkey.Parse("Ctrl") == null, "纯修饰键(无触发键)返回 null");
        {
            // 空段被宽容跳过: "Ctrl++K" 等价于 "Ctrl+K"
            var hk = Hotkey.Parse("Ctrl++K");
            Check(hk != null && hk.ToString() == "Ctrl+K", "\"Ctrl++K\" 宽容解析为 \"Ctrl+K\", 实际:" + (hk == null ? "null" : hk.ToString()));
        }

        Console.WriteLine("== Hotkey 键名 ==");
        Check(Hotkey.GetName(0x41) == "A", "GetName(0x41)=A, 实际:" + Hotkey.GetName(0x41));
        Check(Hotkey.GetName(0x35) == "5", "GetName(0x35)=5, 实际:" + Hotkey.GetName(0x35));
        Check(Hotkey.GetName(0x26) != "", "GetName(方向键上) 非空");
        Check(Hotkey.GetName(0x01) != "", "GetName(鼠标左键) 非空");

        Console.WriteLine("== AppConfig 序列化往返(关键: 字典键必须字符串) ==");
        var cfg = new AppConfig
        {
            PlayLoops = 5,
            PlayMinutes = 3,
            PlayUntilTime = "23:59",
            AnimationsEnabled = false,
            SpamKeyText = "B",
            AutoStart = true,
            StartMinimized = true,
            ConfigVersion = 3
        };
        cfg.SfxBindings["65"] = "a.wav";
        cfg.SfxBindingVolumes["65"] = 80;
        cfg.SfxComboBindings["Ctrl+C"] = "c.wav";
        cfg.SfxComboVolumes["Ctrl+C"] = 60;
        cfg.LaunchPrograms.Add("notepad.exe");

        var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
        string json = ser.Serialize(cfg);
        var cfg2 = ser.Deserialize<AppConfig>(json);

        Check(cfg2 != null, "反序列化非空");
        if (cfg2 != null)
        {
            Check(cfg2.PlayLoops == 5, "PlayLoops 往返");
            Check(cfg2.PlayMinutes == 3, "PlayMinutes 往返");
            Check(cfg2.PlayUntilTime == "23:59", "PlayUntilTime 往返");
            Check(cfg2.AnimationsEnabled == false, "AnimationsEnabled 往返");
            Check(cfg2.SpamKeyText == "B", "SpamKeyText 往返");
            Check(cfg2.SfxBindings.ContainsKey("65") && cfg2.SfxBindings["65"] == "a.wav", "SfxBindings 字符串键往返");
            Check(cfg2.SfxBindingVolumes.ContainsKey("65") && cfg2.SfxBindingVolumes["65"] == 80, "SfxBindingVolumes 往返");
            Check(cfg2.SfxComboBindings.ContainsKey("Ctrl+C") && cfg2.SfxComboBindings["Ctrl+C"] == "c.wav", "SfxComboBindings 往返");
            Check(cfg2.LaunchPrograms.Count == 1 && cfg2.LaunchPrograms[0] == "notepad.exe", "LaunchPrograms 往返");
            Check(cfg2.AutoStart == true, "AutoStart 往返");
            Check(cfg2.StartMinimized == true, "StartMinimized 往返");
            Check(cfg2.ConfigVersion == 3, "ConfigVersion 往返(=3)");
        }

        Console.WriteLine("== AppConfig 新默认值 ==");
        var fresh = new AppConfig();
        Check(fresh.SfxEnabled == true, "SfxEnabled 默认开启");
        Check(fresh.AutoStart == false, "AutoStart 默认关闭");
        Check(fresh.StartMinimized == false, "StartMinimized 默认关闭");
        Check(fresh.ConfigVersion == 0, "ConfigVersion 原始默认=0(经 Load 后迁移为 5)");

        Console.WriteLine("== AppConfig 不可信数据清洗(安全) ==");
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            try
            {
                var evil = new AppConfig();
                evil.LaunchPrograms.Add("C:\\evil.bat");      // 白名单外(脚本)
                evil.LaunchPrograms.Add("notepad.exe");       // 相对路径
                evil.LaunchPrograms.Add("C:\\games\\game.exe"); // 合法
                evil.LaunchPrograms.Add("C:\\games\\game.lnk"); // 合法
                evil.SfxBindings["65"] = "..\\..\\Windows\\Media\\notify.wav"; // 路径穿越
                evil.SfxBindings["66"] = "ok.wav";            // 纯文件名合法
                System.IO.File.WriteAllText(path, ser.Serialize(evil));

                string err;
                var loaded = AppConfig.Load(out err);
                Check(loaded != null, "清洗后配置可加载");
                Check(loaded.LaunchPrograms.Count == 2
                    && loaded.LaunchPrograms[0] == "C:\\games\\game.exe"
                    && loaded.LaunchPrograms[1] == "C:\\games\\game.lnk",
                    "LaunchPrograms 白名单: bat/相对路径被丢弃, exe/lnk 保留");
                Check(!loaded.SfxBindings.ContainsKey("65") && loaded.SfxBindings["66"] == "ok.wav",
                    "音效绑定路径穿越被丢弃, 纯文件名保留");
                Check(loaded.ConfigVersion == 5, "经 Load 后 ConfigVersion 迁移为 5");
            }
            finally
            {
                try { System.IO.File.Delete(path); } catch (Exception) { }
                try { System.IO.File.Delete(path + ".tmp"); } catch (Exception) { }
            }
        }

        Console.WriteLine("== 运行到时刻解析 ==");
        {
            var t1 = Util.ParseUntilTime("23:59");
            Check(t1.HasValue && t1.Value > DateTime.Now && (t1.Value - DateTime.Now).TotalHours <= 25,
                "\"HH:mm\" 解析为今天/明天的该时刻");

            var t2 = Util.ParseUntilTime("2099-01-02 03:04:05");
            Check(t2.HasValue && t2.Value.Year == 2099 && t2.Value.Hour == 3
                && t2.Value.Minute == 4 && t2.Value.Second == 5, "完整日期时刻按字面解析");

            Check(Util.ParseUntilTime("") == null, "空串 → null");
            Check(Util.ParseUntilTime("not-a-time") == null, "非法串 → null");

            Check(Util.FormatDuration(605) == "10m 5s", "FormatDuration(605), 实际:" + Util.FormatDuration(605));
            Check(Util.FormatDuration(3600) == "1h 0m 0s", "FormatDuration(3600), 实际:" + Util.FormatDuration(3600));
        }

        Console.WriteLine("== 运行限制配置迁移(v3 → v4) ==");
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            try
            {
                // 辅助: 写旧配置 → 加载(触发迁移) → 返回迁移后配置
                Func<AppConfig, AppConfig> loadMigrated = delegate(AppConfig old)
                {
                    System.IO.File.WriteAllText(path, ser.Serialize(old));
                    string err;
                    return AppConfig.Load(out err);
                };

                // 旧: 勾了循环但没设次数 → 无限循环(PlayLoop 分支)
                var c1 = loadMigrated(new AppConfig { ConfigVersion = 3, PlayLoop = true, PlayLoops = 0 });
                Check(c1.PlayLimitMode == AppConfig.LimitInfinite && c1.PlayLoops == 0,
                    "旧 PlayLoop=true+0 次 → 无限循环");

                // 旧默认: 不勾循环、次数 0 → 只回放一轮
                var c2 = loadMigrated(new AppConfig { ConfigVersion = 3, PlayLoop = false, PlayLoops = 0 });
                Check(c2.PlayLimitMode == AppConfig.LimitCount && c2.PlayLoops == 1,
                    "旧默认(不循环,0 次) → 一轮");

                // 旧: 次数优先于循环开关(PlayLoops>0 分支先于 PlayLoop 分支)
                var c3 = loadMigrated(new AppConfig { ConfigVersion = 3, PlayLoop = true, PlayLoops = 900 });
                Check(c3.PlayLimitMode == AppConfig.LimitCount && c3.PlayLoops == 900,
                    "旧 PlayLoop=true+900 次 → 指定次数 900");

                // 旧: 点击运行 10 分钟 → 时长模式 600 秒
                var c4 = loadMigrated(new AppConfig { ConfigVersion = 3, ClickMinutes = 10 });
                Check(c4.ClickLimitMode == AppConfig.LimitDuration && c4.ClickSeconds == 600,
                    "旧 运行 10 分钟 → 时长模式 600 秒");

                // 旧: 连按运行到 "23:59" → 时长模式 + 以截止时刻为准 + 绝对时刻已归一化(重启不漂移)
                var c5 = loadMigrated(new AppConfig { ConfigVersion = 3, SpamUntilTime = "23:59" });
                Check(c5.SpamLimitMode == AppConfig.LimitDuration && c5.SpamUntilPrimary
                    && !string.IsNullOrEmpty(c5.SpamUntilAt), "旧 到点 23:59 → 时长模式 + 截止时刻为准");

                // 旧: 只设了到点(没设分钟) → 同样迁移为时长模式 + 截止时刻为准(默认是无限, 有区分力)
                var c6 = loadMigrated(new AppConfig { ConfigVersion = 3, ClickUntilTime = "08:00" });
                Check(c6.ClickLimitMode == AppConfig.LimitDuration && c6.ClickUntilPrimary
                    && !string.IsNullOrEmpty(c6.ClickUntilAt), "旧 到点 08:00(无分钟) → 时长模式 + 截止时刻为准");

                // 音效场景迁移(v4 → v5): 旧扁平绑定归入默认场景("")
                var oldSfx = new AppConfig { ConfigVersion = 4 };
                oldSfx.SfxBindings["83"] = "iphone.wav";
                oldSfx.SfxBindingVolumes["83"] = 100;
                oldSfx.SfxComboBindings["Ctrl+C"] = "c.wav";
                var c7 = loadMigrated(oldSfx);
                Check(c7.SfxCurrentScene == "" && c7.SfxScenes.ContainsKey("")
                    && c7.SfxScenes[""].Bindings.ContainsKey("83")
                    && c7.SfxScenes[""].Bindings["83"] == "iphone.wav"
                    && c7.SfxScenes[""].Volumes.ContainsKey("83")
                    && c7.SfxScenes[""].ComboBindings.ContainsKey("Ctrl+C"),
                    "旧音效绑定 → 默认场景(场景化迁移)");
            }
            finally
            {
                try { System.IO.File.Delete(path); } catch (Exception) { }
                try { System.IO.File.Delete(path + ".tmp"); } catch (Exception) { }
            }
        }

        Console.WriteLine();
        Console.WriteLine("===== " + _pass + " 通过, " + _fail + " 失败 =====");
        return _fail == 0 ? 0 : 1;
    }
}
