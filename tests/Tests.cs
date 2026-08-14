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
        Check(fresh.ConfigVersion == 0, "ConfigVersion 原始默认=0(经 Load 后迁移为 3)");

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
                Check(loaded.ConfigVersion == 3, "经 Load 后 ConfigVersion 迁移为 3");
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
