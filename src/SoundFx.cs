using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AutoClickerTool
{
    /// <summary>
    /// 音效播放器: 用系统 MCI(wimmm.dll) 播放 wav/mp3, 零外部依赖。
    /// 覆盖式播放: 新音效开始前停止并关闭正在播放的旧音效。
    /// </summary>
    internal static class SfxPlayer
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder result, int resultLen, IntPtr hwnd);

        private static readonly object Lock = new object();
        private static string _alias;
        private static int _seq;
        private static int _volume = 1000; // 0~1000 (MCI 音量, 1000 = 100%)

        /// <summary>全局音量(0~1000, 1000=100%)。设置后对后续播放生效。</summary>
        public static int Volume
        {
            get { return _volume; }
            set { _volume = Math.Max(0, Math.Min(1000, value)); }
        }

        /// <summary>播放一个音效文件; 会立即打断正在播放的音效(新按键覆盖旧音效)。使用全局音量。</summary>
        public static void Play(string path)
        {
            Play(path, _volume);
        }

        /// <summary>播放一个音效文件并指定音量(0~1000); 覆盖式播放。</summary>
        public static void Play(string path, int volume)
        {
            lock (Lock)
            {
                Stop();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                int n = Interlocked.Increment(ref _seq);
                string alias = "sfx" + n;
                // 按扩展名选 MCI 设备类型; 不支持的格式 open 失败, 静默忽略
                string type = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? "waveaudio" : "mpegvideo";
                string p = path.Replace("\"", "");
                if (mciSendString(string.Format("open \"{0}\" type {1} alias {2}", p, type, alias), null, 0, IntPtr.Zero) != 0)
                    return;
                // 应用音量(0~1000; waveaudio 支持, 不支持音量的设备静默忽略)
                mciSendString(string.Format("setaudio {0} volume to {1}", alias, Math.Max(0, Math.Min(1000, volume))), null, 0, IntPtr.Zero);
                mciSendString(string.Format("play {0} notify", alias), null, 0, IntPtr.Zero);
                _alias = alias;
            }
        }

        /// <summary>停止并释放当前音效。</summary>
        public static void Stop()
        {
            if (_alias == null) return;
            mciSendString("stop " + _alias, null, 0, IntPtr.Zero);
            mciSendString("close " + _alias, null, 0, IntPtr.Zero);
            _alias = null;
        }
    }

    /// <summary>组合键音效绑定(如 Ctrl+C)。</summary>
    internal class SfxCombo
    {
        public Hotkey Combo;   // 组合键
        public string Path;    // 文件绝对路径
        public int Volume;     // 生效音量 0~1000
    }

    /// <summary>
    /// 按键音效管理器: 全局键盘钩子监听按键, 按下已绑定的键或组合键时播放对应音效。
    /// 只监听不拦截, 与热键/录制钩子互不干扰; 忽略注入按键(宏回放不会触发音效)。
    /// 钩子回调只做入队(MCI 打开/播放是磁盘 I/O, 移出回调防止 UI 卡顿与钩子超时被系统摘除)。
    /// </summary>
    internal class SfxManager : IDisposable
    {
        private const int LLKHF_INJECTED = 0x10;

        private sealed class SfxJob
        {
            public string Path;
            public int Volume;
        }

        private readonly NativeMethods.LowLevelProc _kbProc;
        private IntPtr _kbHook;
        private readonly HashSet<uint> _down = new HashSet<uint>(); // 当前按住的键(归一化)
        private readonly Queue<SfxJob> _jobs = new Queue<SfxJob>();
        private readonly object _qLock = new object();
        private bool _pumpRunning;
        private bool _disposed;

        /// <summary>总开关。</summary>
        public volatile bool Enabled;

        /// <summary>回放期间抑制: 驱动级注入没有 INJECTED 标记, 防止回放触发自己的音效。</summary>
        public volatile bool Suppress;

        /// <summary>键码(原始, 可区分左右修饰键) → 音效文件绝对路径。</summary>
        public Dictionary<int, string> Bindings = new Dictionary<int, string>();

        /// <summary>键码 → 生效音量(0~1000)。缺省键未命中时按 1000(100%) 播放。</summary>
        public Dictionary<int, int> Volumes = new Dictionary<int, int>();

        /// <summary>组合键绑定列表。</summary>
        public List<SfxCombo> Combos = new List<SfxCombo>();

        public SfxManager()
        {
            _kbProc = KeyboardProc;
        }

        public void Start()
        {
            if (_kbHook != IntPtr.Zero || _disposed) return;
            _kbHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _kbProc,
                NativeMethods.GetModuleHandle(null), 0);
            if (_kbHook == IntPtr.Zero) Log.Warn("音效钩子安装失败");
        }

        private bool Satisfied(Hotkey hk)
        {
            if (hk == null) return false;
            foreach (var k in hk.Keys) if (!_down.Contains(k)) return false;
            foreach (var m in hk.Modifiers) if (!_down.Contains(m)) return false;
            return true;
        }

        private void Enqueue(string path, int volume)
        {
            lock (_qLock)
            {
                while (_jobs.Count > 0) _jobs.Dequeue(); // 只保留最新一条, 保持"新键覆盖旧音效"
                _jobs.Enqueue(new SfxJob { Path = path, Volume = volume });
                if (_pumpRunning) return;
                _pumpRunning = true;
            }
            ThreadPool.QueueUserWorkItem(Pump);
        }

        /// <summary>后台线程串行播放队列(与 SfxPlayer 的锁一起串行化 MCI 调用)。</summary>
        private void Pump(object state)
        {
            try
            {
                while (true)
                {
                    SfxJob job = null;
                    lock (_qLock)
                    {
                        if (_jobs.Count == 0) { _pumpRunning = false; return; }
                        job = _jobs.Dequeue();
                    }
                    if (job != null) SfxPlayer.Play(job.Path, job.Volume);
                }
            }
            catch (Exception ex)
            {
                try { Log.Warn("音效播放队列异常: " + ex.Message); } catch (Exception) { }
            }
        }

        private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && Enabled && !Suppress)
                {
                    var info = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                    bool up = (int)wParam == NativeMethods.WM_KEYUP || (int)wParam == NativeMethods.WM_SYSKEYUP;
                    uint vk = info.vkCode;
                    uint n = Hotkey.Normalize(vk);
                    if (up)
                    {
                        _down.Remove(n);
                    }
                    else if ((info.flags & LLKHF_INJECTED) == 0) // 忽略注入按键, 防止宏回放触发音效
                    {
                        if (_down.Add(n)) // 按住自动重复的消息不重复触发
                        {
                            string file;
                            if (Bindings.TryGetValue((int)vk, out file))
                            {
                                int vol;
                                if (!Volumes.TryGetValue((int)vk, out vol)) vol = 1000;
                                Enqueue(file, vol);
                            }
                            // 组合键: 刚按下的键是组合的触发键之一, 且所有修饰键/键都已按住
                            foreach (var cb in Combos)
                            {
                                if (cb.Combo != null && cb.Combo.Keys.Contains(n) && Satisfied(cb.Combo))
                                {
                                    Enqueue(cb.Path, cb.Volume);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 回调异常不能穿越原生边界(进程会终止), 兜底吞掉
                try { Log.Warn("音效钩子回调异常: " + ex.Message); } catch (Exception) { }
            }
            return NativeMethods.CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_kbHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_kbHook);
            _kbHook = IntPtr.Zero;
            SfxPlayer.Stop();
        }
    }
}
